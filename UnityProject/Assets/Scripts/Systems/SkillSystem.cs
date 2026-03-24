// SkillSystem — manages NPC skill XP, character levels, and expertise.
//
// XP curve:   level = floor(sqrt(xp / 100))
//             level 1 = 100 XP, level 5 = 2500 XP, level 10 = 10000 XP, level 20 = 40000 XP
//
// Character level (derived): floor(totalXP / 1000)
// Expertise slots: one per 5 character levels.
//
// Daily soft cap: first 500 XP/day/skill at 100% rate; excess at 70%.
// Resets at in-game midnight (beginning of each new day).
//
// Feature flag: SkillSystem.Enabled = false pauses all XP and level calculation.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class SkillSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>XP above this value per skill per day is at ReducedXPRate.</summary>
        public const float DailySoftCapXP    = 500f;
        /// <summary>Rate applied to XP above the daily soft cap.</summary>
        public const float ReducedXPRate     = 0.70f;
        /// <summary>Divisor for character level: floor(totalXP / CharLevelDivisor).</summary>
        public const int   CharLevelDivisor  = 1000;
        /// <summary>Character level increment at which a new expertise slot is earned.</summary>
        public const int   SlotEveryNLevels  = 5;

        // Mood event constants
        private const float LevelUpMoodDelta      = 5f;
        private const int   LevelUpMoodDurationTicks = 300;
        private const float ExpertiseMoodDelta    = 8f;
        private const int   ExpertiseMoodDurationTicks = 600;

        // ── Feature flags ─────────────────────────────────────────────────────

        /// <summary>Set false to disable all XP awards and level calculation.</summary>
        public bool Enabled = true;

        /// <summary>
        /// Set false to make ALL capability-locked tasks universally accessible
        /// (disables capability checks without removing expertise data).
        /// </summary>
        public static bool CapabilityChecksEnabled = true;

        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly ContentRegistry _registry;
        private MoodSystem               _mood;

        // ── Notification events ───────────────────────────────────────────────

        /// <summary>
        /// Fired when an NPC reaches a character level that earns a new expertise slot.
        /// Payload: (npc, newCharacterLevel).
        /// </summary>
        public event Action<NPCInstance, int> OnSlotEarned;

        /// <summary>
        /// Fired on every character level increase (including non-slot levels).
        /// Payload: (npc, newCharacterLevel).
        /// </summary>
        public event Action<NPCInstance, int> OnCharacterLevelUp;

        // ── Constructor ───────────────────────────────────────────────────────

        public SkillSystem(ContentRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>Wire up mood system after construction (called from GameManager.InitSystems).</summary>
        public void SetMoodSystem(MoodSystem mood) => _mood = mood;

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>Called once per game tick from GameManager to handle periodic skill work.</summary>
        public void Tick(StationState station)
        {
            if (!Enabled) return;
            // Currently nothing to tick per-frame; XP is awarded reactively.
            // Reserved for future time-based diminishing curve resets if tick-based days are used.
        }

        // ── Public XP API ────────────────────────────────────────────────────

        /// <summary>
        /// Award XP for completing a task. Looks up which skill(s) the taskType is
        /// associated with and awards xpPerTaskCompletion to each.
        /// </summary>
        public void AwardXP(NPCInstance npc, string taskType, StationState station)
        {
            if (!Enabled || npc == null) return;
            EnsureSkillInstances(npc);

            foreach (var skill in _registry.Skills.Values)
            {
                if (!skill.associatedTaskTypes.Contains(taskType)) continue;
                var inst = GetOrCreateSkillInstance(npc, skill.skillId);
                float xp = ApplyXPGainModifier(npc, skill.skillId, skill.xpPerTaskCompletion);
                int priorCharLevel = GetCharacterLevel(npc);
                AddXPWithDiminishing(npc, inst, xp, station);
                CheckLevelUp(npc, priorCharLevel, station);
            }
        }

        /// <summary>
        /// Award XP over time for workstation tasks (e.g. research terminals).
        /// <paramref name="xpPerSecond"/> is multiplied by <paramref name="deltaTime"/>.
        /// </summary>
        public void AwardXPOverTime(NPCInstance npc, string skillId, float xpPerSecond,
                                    float deltaTime, StationState station)
        {
            if (!Enabled || npc == null) return;
            if (!_registry.Skills.ContainsKey(skillId)) return;

            EnsureSkillInstances(npc);
            var inst = GetOrCreateSkillInstance(npc, skillId);
            float rawXP = xpPerSecond * deltaTime;
            float xp    = ApplyXPGainModifier(npc, skillId, rawXP);
            int priorCharLevel = GetCharacterLevel(npc);
            AddXPWithDiminishing(npc, inst, xp, station);
            CheckLevelUp(npc, priorCharLevel, station);
        }

        /// <summary>
        /// Award a fixed amount of XP directly to the named skill.
        /// Used for Social (conversation) and Piloting.
        /// </summary>
        public void AwardSkillXP(NPCInstance npc, string skillId, float amount, StationState station)
        {
            if (!Enabled || npc == null) return;
            if (!_registry.Skills.ContainsKey(skillId)) return;

            EnsureSkillInstances(npc);
            var inst = GetOrCreateSkillInstance(npc, skillId);
            float xp = ApplyXPGainModifier(npc, skillId, amount);
            int priorCharLevel = GetCharacterLevel(npc);
            AddXPWithDiminishing(npc, inst, xp, station);
            CheckLevelUp(npc, priorCharLevel, station);
        }

        // ── Character level / slots ───────────────────────────────────────────

        /// <summary>Returns the NPC's current character level (derived from total XP).</summary>
        public static int GetCharacterLevel(NPCInstance npc)
        {
            float total = GetTotalXP(npc);
            return Mathf.FloorToInt(total / CharLevelDivisor);
        }

        /// <summary>Total number of expertise slots earned at the current character level.</summary>
        public static int GetExpertiseSlotCount(NPCInstance npc)
        {
            int charLevel = GetCharacterLevel(npc);
            return charLevel / SlotEveryNLevels;
        }

        /// <summary>Unspent expertise slots (banked indefinitely).</summary>
        public static int GetUnspentSlots(NPCInstance npc)
        {
            return Mathf.Max(0, GetExpertiseSlotCount(npc) - npc.chosenExpertise.Count);
        }

        /// <summary>Total cumulative XP across all skills.</summary>
        public static float GetTotalXP(NPCInstance npc)
        {
            float total = 0f;
            foreach (var inst in npc.skillInstances)
                total += inst.currentXP;
            return total;
        }

        /// <summary>
        /// Minimum XP required to reach the given level.
        /// Formula: level^2 * 100.
        /// </summary>
        public static float GetXPForLevel(int level) => level * level * 100f;

        // ── Expertise management ──────────────────────────────────────────────

        /// <summary>
        /// Attempt to choose an expertise for an NPC. Spends one unspent slot.
        /// Returns (true, "") on success, or (false, reason) on failure.
        /// </summary>
        public (bool ok, string msg) ChooseExpertise(NPCInstance npc, string expertiseId,
                                                      StationState station)
        {
            if (!_registry.Expertises.TryGetValue(expertiseId, out var def))
                return (false, $"Unknown expertise '{expertiseId}'.");

            if (npc.chosenExpertise.Contains(expertiseId))
                return (false, $"{def.displayName} is already chosen.");

            if (GetUnspentSlots(npc) <= 0)
                return (false, "No unspent expertise slots available.");

            // Check required skill level
            if (!string.IsNullOrEmpty(def.requiredSkillId))
            {
                int skillLevel = GetSkillLevel(npc, def.requiredSkillId);
                if (skillLevel < def.requiredSkillLevel)
                    return (false,
                        $"Requires {def.requiredSkillId.Replace("skill.", "")} level {def.requiredSkillLevel}.");
            }

            npc.chosenExpertise.Add(expertiseId);
            RebuildExpertiseModifier(npc);

            // Register any capability unlocks
            foreach (var cap in def.capabilityUnlocks)
                TaskEligibilityResolver.RegisterCapability(cap, expertiseId);

            // Push mood modifier
            _mood?.PushModifier(npc, "expertise_unlocked", ExpertiseMoodDelta,
                               ExpertiseMoodDurationTicks, station.tick, "skill_system");

            station.LogEvent($"{npc.name} gained expertise: {def.displayName}.");
            return (true, "");
        }

        /// <summary>
        /// Replace one chosen expertise with another.
        /// Net slot count is unchanged. The old expertise is simply removed; no
        /// per-expertise progress is tracked so there is nothing to reset.
        /// </summary>
        public (bool ok, string msg) SwapExpertise(NPCInstance npc, string oldExpertiseId,
                                                    string newExpertiseId, StationState station)
        {
            if (!npc.chosenExpertise.Contains(oldExpertiseId))
                return (false, $"NPC does not have expertise '{oldExpertiseId}'.");

            if (!_registry.Expertises.TryGetValue(newExpertiseId, out var newDef))
                return (false, $"Unknown expertise '{newExpertiseId}'.");

            if (npc.chosenExpertise.Contains(newExpertiseId))
                return (false, $"{newDef.displayName} is already chosen.");

            // Check required skill level for new expertise
            if (!string.IsNullOrEmpty(newDef.requiredSkillId))
            {
                int skillLevel = GetSkillLevel(npc, newDef.requiredSkillId);
                if (skillLevel < newDef.requiredSkillLevel)
                    return (false,
                        $"Requires {newDef.requiredSkillId.Replace("skill.", "")} level {newDef.requiredSkillLevel}.");
            }

            // Perform swap: remove old, add new
            npc.chosenExpertise.Remove(oldExpertiseId);
            npc.chosenExpertise.Add(newExpertiseId);
            RebuildExpertiseModifier(npc);

            // Re-register capabilities (old removed, new added)
            RebuildCapabilityRegistry(npc);

            _mood?.PushModifier(npc, "expertise_unlocked", ExpertiseMoodDelta,
                               ExpertiseMoodDurationTicks, station.tick, "skill_system");

            if (_registry.Expertises.TryGetValue(oldExpertiseId, out var oldDef))
                station.LogEvent($"{npc.name} replaced {oldDef.displayName} with {newDef.displayName}.");

            return (true, "");
        }

        // ── Skill queries ─────────────────────────────────────────────────────

        /// <summary>Get the current level (0–20) for a skill on an NPC.</summary>
        public static int GetSkillLevel(NPCInstance npc, string skillId)
        {
            foreach (var inst in npc.skillInstances)
                if (inst.skillId == skillId) return inst.Level;
            return 0;
        }

        /// <summary>Get the SkillInstance for a skill, or null if none found.</summary>
        public static SkillInstance GetSkillInstance(NPCInstance npc, string skillId)
        {
            foreach (var inst in npc.skillInstances)
                if (inst.skillId == skillId) return inst;
            return null;
        }

        /// <summary>
        /// Returns the effective skill check result: skill level + governing ability modifier
        /// + need-state modifier.  Range is roughly -4 to level+5.
        /// </summary>
        public int GetSkillCheckResult(NPCInstance npc, string skillId)
        {
            int level     = GetSkillLevel(npc, skillId);
            int abilityMod = 0;
            if (_registry.Skills.TryGetValue(skillId, out var def) &&
                !string.IsNullOrEmpty(def.governingAbility))
            {
                abilityMod = def.governingAbility switch
                {
                    "STR" => AbilityScores.GetModifier(npc.abilityScores.STR),
                    "DEX" => AbilityScores.GetModifier(npc.abilityScores.DEX),
                    "INT" => AbilityScores.GetModifier(npc.abilityScores.INT),
                    "WIS" => AbilityScores.GetModifier(npc.abilityScores.WIS),
                    "CHA" => AbilityScores.GetModifier(npc.abilityScores.CHA),
                    "END" => AbilityScores.GetModifier(npc.abilityScores.END),
                    _     => 0,
                };
            }
            return level + abilityMod + NeedSystem.GetSkillCheckModifier(npc);
        }

        // ── Expertise modifier ────────────────────────────────────────────────

        /// <summary>
        /// Recompute expertiseModifier by multiplying all WorkSpeed passive bonuses.
        /// WorkSpeed bonuses are applied as a global job-duration multiplier regardless
        /// of <c>bonus.targetSkillId</c>; per-skill speed differentiation is not
        /// implemented at the JobSystem level.
        /// Called after expertise is chosen/swapped.
        /// </summary>
        public void RebuildExpertiseModifier(NPCInstance npc)
        {
            float modifier = 1.0f;
            foreach (var id in npc.chosenExpertise)
            {
                if (!_registry.Expertises.TryGetValue(id, out var def)) continue;
                foreach (var bonus in def.passiveBonuses)
                {
                    if (bonus.bonusType == ExpertiseBonusType.WorkSpeed)
                        modifier *= bonus.value;
                }
            }
            npc.expertiseModifier = modifier;
        }

        /// <summary>
        /// Get the combined research output multiplier from all chosen expertise
        /// that grant ResearchOutput bonuses (and optionally match a branch skill).
        /// </summary>
        public float GetResearchOutputMultiplier(NPCInstance npc, string skillId = "")
        {
            float mult = 1.0f;
            foreach (var id in npc.chosenExpertise)
            {
                if (!_registry.Expertises.TryGetValue(id, out var def)) continue;
                foreach (var bonus in def.passiveBonuses)
                {
                    if (bonus.bonusType != ExpertiseBonusType.ResearchOutput) continue;
                    if (!string.IsNullOrEmpty(bonus.targetSkillId) &&
                        bonus.targetSkillId != skillId) continue;
                    mult *= bonus.value;
                }
            }
            return mult;
        }

        /// <summary>
        /// Get the combined yield multiplier from chosen expertise for a given skill.
        /// </summary>
        public float GetYieldMultiplier(NPCInstance npc, string skillId)
        {
            float mult = 1.0f;
            foreach (var id in npc.chosenExpertise)
            {
                if (!_registry.Expertises.TryGetValue(id, out var def)) continue;
                foreach (var bonus in def.passiveBonuses)
                {
                    if (bonus.bonusType != ExpertiseBonusType.YieldMultiplier) continue;
                    if (!string.IsNullOrEmpty(bonus.targetSkillId) &&
                        bonus.targetSkillId != skillId) continue;
                    mult *= bonus.value;
                }
            }
            return mult;
        }

        // ── Eligibility helpers ───────────────────────────────────────────────

        /// <summary>
        /// Returns all ExpertiseDefinitions the NPC qualifies for but has not yet chosen,
        /// grouped for UI display.  Includes unqualified entries (for greyed-out display)
        /// when showAll is true.
        /// </summary>
        public List<ExpertiseDefinition> GetSelectableExpertise(NPCInstance npc, bool showAll = false)
        {
            var result = new List<ExpertiseDefinition>();
            foreach (var def in _registry.Expertises.Values)
            {
                if (npc.chosenExpertise.Contains(def.expertiseId)) continue;
                bool qualified = string.IsNullOrEmpty(def.requiredSkillId)
                    || GetSkillLevel(npc, def.requiredSkillId) >= def.requiredSkillLevel;
                if (qualified || showAll)
                    result.Add(def);
            }
            return result;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void EnsureSkillInstances(NPCInstance npc)
        {
            if (npc.skillInstances == null) npc.skillInstances = new List<SkillInstance>();
        }

        private SkillInstance GetOrCreateSkillInstance(NPCInstance npc, string skillId)
        {
            foreach (var inst in npc.skillInstances)
                if (inst.skillId == skillId) return inst;
            var newInst = SkillInstance.Create(skillId);
            npc.skillInstances.Add(newInst);
            return newInst;
        }

        private void AddXPWithDiminishing(NPCInstance npc, SkillInstance inst,
                                           float xp, StationState station)
        {
            int today = station.tick / Mathf.Max(1, TimeSystem.TicksPerDay);

            // Reset daily accumulator when a new day begins
            if (inst.dailyXPDay != today)
            {
                inst.dailyXPAccumulated = 0f;
                inst.dailyXPDay         = today;
            }

            float remaining = DailySoftCapXP - inst.dailyXPAccumulated;
            float applied;
            if (remaining >= xp)
            {
                // All XP fits within the full-rate window
                applied = xp;
            }
            else
            {
                // Partial full-rate + reduced-rate on the remainder
                float fullPart    = Mathf.Max(0f, remaining);
                float reducedPart = (xp - fullPart) * ReducedXPRate;
                applied = fullPart + reducedPart;
            }

            inst.currentXP          += applied;
            inst.dailyXPAccumulated += xp;    // track raw XP for cap purposes
        }

        private float ApplyXPGainModifier(NPCInstance npc, string skillId, float baseXP)
        {
            float mult = 1.0f;
            foreach (var id in npc.chosenExpertise)
            {
                if (!_registry.Expertises.TryGetValue(id, out var def)) continue;
                foreach (var bonus in def.passiveBonuses)
                {
                    if (bonus.bonusType != ExpertiseBonusType.XPGain) continue;
                    if (!string.IsNullOrEmpty(bonus.targetSkillId) &&
                        bonus.targetSkillId != skillId) continue;
                    mult *= bonus.value;
                }
            }
            return baseXP * mult;
        }

        private void CheckLevelUp(NPCInstance npc, int priorCharLevel, StationState station)
        {
            int newCharLevel = GetCharacterLevel(npc);

            if (newCharLevel > priorCharLevel)
            {
                // Fire character level-up event (wired to station log in GameManager)
                OnCharacterLevelUp?.Invoke(npc, newCharLevel);

                // Push mood bonus
                _mood?.PushModifier(npc, "level_up", LevelUpMoodDelta,
                                   LevelUpMoodDurationTicks, station.tick, "skill_system");

                // Check slot threshold
                int priorSlots = priorCharLevel / SlotEveryNLevels;
                int newSlots   = newCharLevel   / SlotEveryNLevels;
                if (newSlots > priorSlots)
                {
                    // OnSlotEarned is also wired to station log in GameManager
                    OnSlotEarned?.Invoke(npc, newCharLevel);
                }
            }
        }

        private void RebuildCapabilityRegistry(NPCInstance npc)
        {
            // Clear all capabilities registered for this NPC's expertise, then re-register.
            // TaskEligibilityResolver stores per-expertise caps globally — just re-register
            // new ones; removals are handled by CanPerform checking npc.chosenExpertise.
            foreach (var id in npc.chosenExpertise)
            {
                if (!_registry.Expertises.TryGetValue(id, out var def)) continue;
                foreach (var cap in def.capabilityUnlocks)
                    TaskEligibilityResolver.RegisterCapability(cap, id);
            }
        }

        // ── Bootstrap ─────────────────────────────────────────────────────────

        /// <summary>
        /// Register all capabilities from loaded ExpertiseDefinitions.
        /// Called once at startup after ContentRegistry finishes loading.
        /// </summary>
        public void RegisterAllCapabilities()
        {
            foreach (var def in _registry.Expertises.Values)
                foreach (var cap in def.capabilityUnlocks)
                    TaskEligibilityResolver.RegisterCapability(cap, def.expertiseId);
        }

        /// <summary>
        /// Ensure every NPC has a SkillInstance for every defined skill.
        /// Also migrate old socialSkill int to Social SkillInstance if needed.
        /// Called once on load.
        /// </summary>
        public void InitialiseNpcSkills(StationState station)
        {
            foreach (var npc in station.npcs.Values)
                InitialiseNpcSkills(npc);
        }

        /// <summary>Initialise skill instances for a single NPC.</summary>
        public void InitialiseNpcSkills(NPCInstance npc)
        {
            if (npc.skillInstances == null) npc.skillInstances = new List<SkillInstance>();

            foreach (var skill in _registry.Skills.Values)
            {
                bool found = false;
                foreach (var inst in npc.skillInstances)
                    if (inst.skillId == skill.skillId) { found = true; break; }
                if (!found)
                    npc.skillInstances.Add(SkillInstance.Create(skill.skillId));
            }

            // Migrate legacy socialSkill int to skill.social SkillInstance
            if (npc.socialSkill > 1)
            {
                var socialInst = GetOrCreateSkillInstance(npc, "skill.social");
                if (socialInst.currentXP == 0f)
                {
                    // Convert old level to approximate XP (level^2 * 100)
                    float migratedXP = npc.socialSkill * npc.socialSkill * 100f;
                    socialInst.currentXP = migratedXP;
                }
                npc.socialSkill = 1;  // reset after migration
            }

            // Rebuild expertise modifier in case it was serialised as 1.0 default
            RebuildExpertiseModifier(npc);
        }
    }
}
