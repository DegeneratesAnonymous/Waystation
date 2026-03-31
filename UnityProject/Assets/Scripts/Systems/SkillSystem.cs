// SkillSystem — manages NPC skill XP, character levels, and expertise.
//
// XP curve:   level = floor(sqrt(xp / 100))
//             level 1 = 100 XP, level 5 = 2500 XP, level 10 = 10000 XP, level 20 = 40000 XP
//
// Character level (derived): floor(totalXP / 1000)
// Expertise slots: one slot per 4 levels in any individual skill.
//   e.g. Farming Lv 4 → +1 slot; Farming Lv 8 → +1 more; Engineering Lv 4 → +1 more.
//   Total slots = sum over all skills of floor(skillLevel / SlotEverySkillLevels).
//
// Daily soft cap: first 500 XP/day/skill at 100% rate; excess at 70%.
// Resets at in-game midnight (beginning of each new day).
//
// Skill check resolution:
//   Simple:   level + governing_ability_mod + need_mod
//   Advanced: level + governing_ability_mod + composite_terms_sum + need_mod
//
// Domain skill score (WO-NPC-013, requires FeatureFlags.UseNewSkillSystem):
//   PrimaryDominant: primary_stat + secondary_stat / 2  (raw stat values, integer division)
//   EqualWeight:     (primary_stat + secondary_stat) / 2
//
// Perception (WO-NPC-013): WIS + (INT + CHA) / 4  (raw stat values, integer division)
//   Fires as a contested check against Subterfuge rolls (Stealth, Concealment, Manipulation).
//   Fires as a flat threshold check for environmental fault detection.
//
// Proficiency (WO-NPC-013): 2 base slots + INT modifier slots.
//   Non-proficient skills: XP accrues at 50% rate; skill caps at level 6.
//
// Raw ability check: raw stat value (no skill modifier), used for direct stat rolls.
//
// Hauling search radius: BaseHaulRadius + INT_modifier × HaulRadiusPerIntMod (min 3).
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
        /// <summary>
        /// Individual skill level increment at which one expertise slot is earned per skill.
        /// e.g. Farming Lv 4 earns a slot; Farming Lv 8 earns another; Engineering Lv 4 earns another.
        /// </summary>
        public const int   SlotEverySkillLevels = 4;
        /// <summary>Base hauling search radius in tiles (before INT modifier).</summary>
        public const int   BaseHaulRadius       = 10;
        /// <summary>Extra tiles of haul radius per INT modifier point.</summary>
        public const int   HaulRadiusPerIntMod  = 3;

        // ── Domain skill constants (WO-NPC-013) ───────────────────────────────

        /// <summary>
        /// XP rate multiplier for non-proficient domain skills (WO-NPC-013).
        /// Non-proficient NPCs earn XP at this fraction of the normal rate.
        /// </summary>
        public const float NonProficientXPRate = 0.50f;
        /// <summary>
        /// Maximum skill level for non-proficient domain skills (WO-NPC-013).
        /// XP stops accruing once the skill reaches this level without proficiency.
        /// </summary>
        public const int   NonProficientLevelCap = 6;
        /// <summary>
        /// Base number of proficiency slots before INT modifier (WO-NPC-013).
        /// Total = BaseProficiencySlots + INT modifier.
        /// </summary>
        public const int   BaseProficiencySlots = 2;

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
        private MentoringSystem          _mentoring;

        // ── Notification events ───────────────────────────────────────────────

        /// <summary>
        /// Fired when an NPC's individual skill level crosses a multiple of SlotEverySkillLevels (4).
        /// Payload: (npc, skillId, newSkillLevel).
        /// </summary>
        public event Action<NPCInstance, string, int> OnSlotEarned;

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

        /// <summary>Wire up mentoring system after construction (called from GameManager.InitSystems).</summary>
        public void SetMentoringSystem(MentoringSystem mentoring) => _mentoring = mentoring;

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

            // Mentoring multiplier is the same for all skills in this task completion.
            float mentoringMult = _mentoring?.GetMentoringXPMultiplier(npc, station) ?? 1f;

            foreach (var skill in _registry.Skills.Values)
            {
                if (!skill.associatedTaskTypes.Contains(taskType)) continue;
                var inst = GetOrCreateSkillInstance(npc, skill.skillId);
                float xp = ApplyXPGainModifier(npc, skill.skillId, skill.xpPerTaskCompletion);
                xp *= mentoringMult;
                int priorCharLevel  = GetCharacterLevel(npc);
                int priorSkillLevel = inst.Level;
                AddXPWithDiminishing(npc, inst, xp, station);
                CheckLevelUp(npc, priorCharLevel, station);
                CheckSkillLevelUp(npc, skill.skillId, inst, priorSkillLevel, station);
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
            xp *= _mentoring?.GetMentoringXPMultiplier(npc, station) ?? 1f;
            int priorCharLevel  = GetCharacterLevel(npc);
            int priorSkillLevel = inst.Level;
            AddXPWithDiminishing(npc, inst, xp, station);
            CheckLevelUp(npc, priorCharLevel, station);
            CheckSkillLevelUp(npc, skillId, inst, priorSkillLevel, station);
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
            xp *= _mentoring?.GetMentoringXPMultiplier(npc, station) ?? 1f;
            int priorCharLevel  = GetCharacterLevel(npc);
            int priorSkillLevel = inst.Level;
            AddXPWithDiminishing(npc, inst, xp, station);
            CheckLevelUp(npc, priorCharLevel, station);
            CheckSkillLevelUp(npc, skillId, inst, priorSkillLevel, station);
        }

        // ── Character level / slots ───────────────────────────────────────────

        /// <summary>Returns the NPC's current character level (derived from total XP).</summary>
        public static int GetCharacterLevel(NPCInstance npc)
        {
            float total = GetTotalXP(npc);
            return Mathf.FloorToInt(total / CharLevelDivisor);
        }

        /// <summary>
        /// Total number of expertise slots earned. One slot per SlotEverySkillLevels (4) levels
        /// in each individual skill — summed across all skills.
        /// </summary>
        public static int GetExpertiseSlotCount(NPCInstance npc)
        {
            if (npc.skillInstances == null) return 0;
            int total = 0;
            foreach (var inst in npc.skillInstances)
                total += inst.Level / SlotEverySkillLevels;
            return total;
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
        /// If the NPC has a pending expertise skill in the queue matching this expertise's
        /// required skill, that entry is removed from the pending queue.
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

            // Register hard-locked and soft-locked capability unlocks
            foreach (var cap in def.capabilityUnlocks)
                TaskEligibilityResolver.RegisterCapability(cap, expertiseId);
            foreach (var cap in def.softCapabilityUnlocks)
                TaskEligibilityResolver.RegisterSoftCapability(cap, expertiseId);

            // Dequeue one pending entry for the triggering skill (if any)
            if (!string.IsNullOrEmpty(def.requiredSkillId) && npc.pendingExpertiseSkillIds != null)
                npc.pendingExpertiseSkillIds.Remove(def.requiredSkillId);

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
        /// Returns the effective skill check result.
        /// Simple skills:   level + governing_ability_mod + need_mod
        /// Advanced skills: level + governing_ability_mod + composite_terms_sum + need_mod
        /// </summary>
        public int GetSkillCheckResult(NPCInstance npc, string skillId)
        {
            int level      = GetSkillLevel(npc, skillId);
            int abilityMod = 0;
            float compositeMod = 0f;

            if (_registry.Skills.TryGetValue(skillId, out var def))
            {
                if (!string.IsNullOrEmpty(def.governingAbility))
                    abilityMod = ResolveAbilityMod(npc, def.governingAbility);

                if (def.skillType == SkillType.Advanced)
                {
                    foreach (var term in def.compositeTerms)
                    {
                        if (term.termType == "ability")
                            compositeMod += ResolveAbilityMod(npc, term.ability) * term.weight;
                        else if (term.termType == "skill")
                            compositeMod += GetSkillLevel(npc, term.skillId) * term.weight;
                    }
                }
            }
            return level + abilityMod + Mathf.RoundToInt(compositeMod) + NeedSystem.GetSkillCheckModifier(npc);
        }

        /// <summary>
        /// Performs a raw ability check using the raw stat value — no modifier applied.
        /// Useful for pure stat-based rolls (e.g. carrying capacity, environmental resist).
        /// Returns the raw score (e.g. INT 18 → 18), not the derived modifier.
        /// </summary>
        public static int GetRawAbilityCheck(NPCInstance npc, string ability)
            => ability switch
            {
                "STR" => npc.abilityScores.STR,
                "DEX" => npc.abilityScores.DEX,
                "INT" => npc.abilityScores.INT,
                "WIS" => npc.abilityScores.WIS,
                "CHA" => npc.abilityScores.CHA,
                "END" => npc.abilityScores.END,
                _     => 0,
            };

        /// <summary>
        /// Returns the hauling destination search radius in tiles for an NPC.
        /// Scales with INT modifier: BaseHaulRadius + INTmod × HaulRadiusPerIntMod, minimum 3.
        /// INT=5 (mod -1) → 7;  INT=10 (mod 0) → 10;  INT=18 (mod 3) → 19.
        /// </summary>
        public static int GetHaulSearchRadius(NPCInstance npc)
        {
            int intMod = AbilityScores.GetModifier(npc.abilityScores.INT);
            return Mathf.Max(3, BaseHaulRadius + intMod * HaulRadiusPerIntMod);
        }

        // ── Domain skill queries (WO-NPC-013) ────────────────────────────────

        /// <summary>
        /// Calculates the domain skill score using raw primary and secondary stat values.
        /// PrimaryDominant: score = primary_stat + secondary_stat / 2  (integer division).
        /// EqualWeight:     score = (primary_stat + secondary_stat) / 2.
        /// Returns 0 if the skill is not found or does not define primary/secondary stats.
        /// Does NOT include skill level — the score reflects current stat values only.
        /// </summary>
        public int GetDomainSkillScore(NPCInstance npc, string skillId)
        {
            if (!_registry.Skills.TryGetValue(skillId, out var def)) return 0;
            if (!def.IsDomainSkill) return 0;

            int primary   = GetRawAbilityCheck(npc, def.primaryStat);
            int secondary = GetRawAbilityCheck(npc, def.secondaryStat);

            return def.weight == SkillWeight.EqualWeight
                ? (primary + secondary) / 2
                : primary + secondary / 2;
        }

        /// <summary>
        /// Calculates the Perception derived passive stat for an NPC (WO-NPC-013).
        /// Formula: WIS + (INT + CHA) / 4  (integer division; raw stat values).
        /// </summary>
        public static int GetPerceptionScore(NPCInstance npc)
            => npc.abilityScores.WIS + (npc.abilityScores.INT + npc.abilityScores.CHA) / 4;

        /// <summary>
        /// Performs a Perception contested check (WO-NPC-013).
        /// The detecting NPC detects when their Perception score strictly exceeds the
        /// opposing roll (e.g. a Subterfuge Stealth / Concealment / Manipulation roll).
        /// Returns true when the NPC detects; false otherwise.
        /// Outcome is binary — no partial detection.
        /// </summary>
        public static bool PerceptionContestedCheck(int perceptionScore, int opposingRoll)
            => perceptionScore > opposingRoll;

        /// <summary>
        /// Returns true if the NPC is proficient in the given skill (WO-NPC-013).
        /// Proficiency is stored in NPCInstance.proficiencySkillIds, which is populated
        /// at NPC generation from background and trait data.
        /// </summary>
        public static bool IsSkillProficient(NPCInstance npc, string skillId)
            => npc.proficiencySkillIds != null && npc.proficiencySkillIds.Contains(skillId);

        /// <summary>
        /// Returns the total number of proficiency slots available to this NPC (WO-NPC-013).
        /// Formula: BaseProficiencySlots (2) + INT modifier.
        /// </summary>
        public static int GetProficiencySlotCount(NPCInstance npc)
            => BaseProficiencySlots + AbilityScores.GetModifier(npc.abilityScores.INT);

        /// <summary>
        /// Attempts to claim a domain expertise option for an NPC (WO-NPC-013).
        /// The option must belong to a slot in the skill's domainExpertiseSlots at
        /// the appropriate unlock level.  Adding the option ID to chosenExpertise and
        /// registering its task tags as soft capabilities in TaskEligibilityResolver.
        /// Returns (true, "") on success; (false, reason) on failure.
        /// </summary>
        public (bool ok, string msg) ChooseDomainExpertise(NPCInstance npc,
                                                            string skillId,
                                                            string optionId,
                                                            StationState station)
        {
            if (!_registry.Skills.TryGetValue(skillId, out var skillDef))
                return (false, $"Unknown skill '{skillId}'.");

            // Find the option across all slots in this skill
            DomainExpertiseOptionDefinition optionDef = null;
            int requiredLevel = 0;
            foreach (var slot in skillDef.domainExpertiseSlots)
            {
                foreach (var opt in slot.options)
                {
                    if (opt.id == optionId)
                    {
                        optionDef     = opt;
                        requiredLevel = slot.unlockLevel;
                        break;
                    }
                }
                if (optionDef != null) break;
            }

            if (optionDef == null)
                return (false, $"Unknown expertise option '{optionId}' in skill '{skillId}'.");

            if (npc.chosenExpertise.Contains(optionId))
                return (false, $"{optionDef.name} is already chosen.");

            int currentLevel = GetSkillLevel(npc, skillId);
            if (currentLevel < requiredLevel)
                return (false, $"Requires {skillDef.displayName} level {requiredLevel}.");

            npc.chosenExpertise.Add(optionId);

            // Register soft capability unlocks so TaskEligibilityResolver can check them
            foreach (var tag in optionDef.taskTagsUnlocked)
                TaskEligibilityResolver.RegisterSoftCapability(tag, optionId);

            // Dequeue pending prompt for this skill if present
            if (npc.pendingExpertiseSkillIds != null)
                npc.pendingExpertiseSkillIds.Remove(skillId);

            _mood?.PushModifier(npc, "expertise_unlocked", ExpertiseMoodDelta,
                               ExpertiseMoodDurationTicks, station.tick, "skill_system");

            station.LogEvent($"{npc.name} gained expertise: {optionDef.name}.");
            return (true, "");
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
            // Domain skill proficiency enforcement (WO-NPC-013)
            if (FeatureFlags.UseNewSkillSystem &&
                _registry.Skills.TryGetValue(inst.skillId, out var skillDef) &&
                skillDef.IsDomainSkill && skillDef.proficiencyRequiredForMaxLevel)
            {
                bool proficient = IsSkillProficient(npc, inst.skillId);
                if (!proficient)
                {
                    // Hard cap: stop awarding XP once the level cap is reached
                    if (inst.Level >= NonProficientLevelCap) return;
                    // Reduced XP rate for non-proficient skills
                    xp *= NonProficientXPRate;
                }
            }

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
            }
        }

        /// <summary>
        /// Checks whether an individual skill level crossed a multiple of SlotEverySkillLevels.
        /// If so, fires OnSlotEarned and enqueues the skillId on the NPC's pending list.
        /// </summary>
        private void CheckSkillLevelUp(NPCInstance npc, string skillId,
                                        SkillInstance inst, int priorSkillLevel,
                                        StationState station)
        {
            int newSkillLevel = inst.Level;
            if (newSkillLevel <= priorSkillLevel) return;

            // Check every slot threshold crossed (handles large XP jumps)
            int priorSlots = priorSkillLevel / SlotEverySkillLevels;
            int newSlots   = newSkillLevel   / SlotEverySkillLevels;
            if (newSlots > priorSlots)
            {
                // Enqueue pending prompt for each newly earned slot from this skill
                if (npc.pendingExpertiseSkillIds == null)
                    npc.pendingExpertiseSkillIds = new List<string>();
                for (int i = 0; i < (newSlots - priorSlots); i++)
                    npc.pendingExpertiseSkillIds.Add(skillId);

                OnSlotEarned?.Invoke(npc, skillId, newSkillLevel);
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
                foreach (var cap in def.softCapabilityUnlocks)
                    TaskEligibilityResolver.RegisterSoftCapability(cap, id);
            }
        }

        // ── Bootstrap ─────────────────────────────────────────────────────────

        /// <summary>
        /// Register all hard-locked and soft-locked capabilities from loaded ExpertiseDefinitions
        /// and from domain skill embedded expertise slots (WO-NPC-013).
        /// Called once at startup after ContentRegistry finishes loading.
        /// </summary>
        public void RegisterAllCapabilities()
        {
            // Legacy expertise definitions (WO-NPC-004)
            foreach (var def in _registry.Expertises.Values)
            {
                foreach (var cap in def.capabilityUnlocks)
                    TaskEligibilityResolver.RegisterCapability(cap, def.expertiseId);
                foreach (var cap in def.softCapabilityUnlocks)
                    TaskEligibilityResolver.RegisterSoftCapability(cap, def.expertiseId);
            }

            // Domain skill embedded expertise slots (WO-NPC-013)
            if (FeatureFlags.UseNewSkillSystem)
            {
                foreach (var skill in _registry.Skills.Values)
                {
                    foreach (var slot in skill.domainExpertiseSlots)
                    {
                        foreach (var opt in slot.options)
                        {
                            foreach (var tag in opt.taskTagsUnlocked)
                                TaskEligibilityResolver.RegisterSoftCapability(tag, opt.id);
                        }
                    }
                }
            }
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

            // Ensure pending expertise list is initialised
            if (npc.pendingExpertiseSkillIds == null)
                npc.pendingExpertiseSkillIds = new List<string>();

            // Rebuild expertise modifier in case it was serialised as 1.0 default
            RebuildExpertiseModifier(npc);
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        /// <summary>Returns the ability modifier for the named ability (STR/DEX/INT/WIS/CHA/END).</summary>
        private static int ResolveAbilityMod(NPCInstance npc, string ability)
            => ability switch
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
}
