// TraitSystem — centralized management of NPC trait acquisition, decay,
// conflict resolution, and effect application.
//
// Integration points:
//   • RegisterConditionPressure(npc, category, deltaPerDay) — called by external
//     systems (mood system, environment system, etc.) once per day tick.
//   • TriggerEventRemoval(npc, traitId, currentTick) — called by events to remove event-gated traits.
//   • NotifyCrewDeath(deadNpc, station) — called when a crew member dies; applies
//     WitnessDeath pressure to all surviving crew on the station.
//   • OnExtendedCombat(npc, station) — called when an NPC has been in extended combat.
//   • OnMentoringSession(npc, station) — called when an NPC completes a mentoring session.
//   • OnCounsellingComplete(npc, station) — called by therapy/counselling system to
//     remove all therapy-removable traits from the NPC (WO-NPC-003 integration point).
//   • Tick(station, currentTick) — called once per game tick from GameManager.
//
// Gated by FeatureFlags.NpcTraits.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class TraitSystem
    {
        // ── Constants ────────────────────────────────────────────────────────

        /// <summary>
        /// Condition pressure required before a trait acquisition roll fires.
        /// Systems accumulate deltaPerDay each day tick; a roll occurs when this is reached.
        /// </summary>
        public float AcquisitionPressureThreshold = 5f;

        /// <summary>Weight multiplier applied to a condition category when an NPC already
        /// has traits in the same category (reduces additional accumulation).</summary>
        public float SaturatedCategoryDampening = 0.5f;

        /// <summary>Sentinel value passed to MoodSystem.PushModifier for modifiers that
        /// should never expire on their own (e.g., per-trait mood impact).</summary>
        private const int PermanentDuration = -1;

        // ── Registry — trait definitions and pools ───────────────────────────

        private readonly Dictionary<string, NpcTraitDefinition>       _traits =
            new Dictionary<string, NpcTraitDefinition>();
        private readonly Dictionary<TraitConditionCategory, TraitPoolDefinition> _pools =
            new Dictionary<TraitConditionCategory, TraitPoolDefinition>();
        private readonly Dictionary<string, TraitLineageDefinition> _lineages =
            new Dictionary<string, TraitLineageDefinition>();

        // ── Dependencies ─────────────────────────────────────────────────────

        private MoodSystem _mood;

        public void SetMoodSystem(MoodSystem m) => _mood = m;

        // ── Registration ─────────────────────────────────────────────────────

        public void RegisterTrait(NpcTraitDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.traitId)) return;
            _traits[def.traitId] = def;
        }

        public void RegisterPool(TraitPoolDefinition pool)
        {
            if (pool == null) return;
            _pools[pool.conditionCategory] = pool;
        }

        public void RegisterTraitLineage(TraitLineageDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.lineageId)) return;
            _lineages[def.lineageId] = def;
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public bool TryGetTrait(string traitId, out NpcTraitDefinition def)
            => _traits.TryGetValue(traitId, out def);

        public NpcTraitDefinition GetTrait(string traitId)
        {
            _traits.TryGetValue(traitId, out var def);
            return def;
        }

        public List<NpcTraitDefinition> GetByCategory(TraitCategory category)
        {
            var result = new List<NpcTraitDefinition>();
            foreach (var def in _traits.Values)
                if (def.category == category) result.Add(def);
            return result;
        }

        public int TraitCount => _traits.Count;
        public int PoolCount  => _pools.Count;

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>Called once per game tick from GameManager.AdvanceTick.</summary>
        public void Tick(StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            if (station.tick % TimeSystem.TicksPerDay != 0) return; // once per day

            foreach (var npc in station.npcs.Values)
            {
                if (npc.traitProfile == null) continue;
                ProcessDecay(npc, station.tick);
                EvaluateAcquisition(npc, station.tick);
            }
            TickLineages(station);

            // Resonant passive tick — mood bleed from Emotional Expression +3 NPCs
            if (_axisDefs.Count > 0) TickResonantBleed(station);
        }

        // ── Condition Pressure API ────────────────────────────────────────────

        /// <summary>
        /// Accumulate pressure for a condition category on an NPC.
        /// Called by external systems (MoodSystem, EnvironmentSystem, etc.) once per day tick.
        /// </summary>
        public void RegisterConditionPressure(NPCInstance npc,
                                              TraitConditionCategory category,
                                              float deltaPerDay)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.GetOrCreateTraitProfile();
            string key = category.ToString();
            if (!profile.conditionPressure.ContainsKey(key))
                profile.conditionPressure[key] = 0f;
            profile.conditionPressure[key] += deltaPerDay;
        }

        // ── Acquisition ──────────────────────────────────────────────────────

        private void EvaluateAcquisition(NPCInstance npc, int currentTick)
        {
            var profile = npc.traitProfile;
            foreach (TraitConditionCategory category in System.Enum.GetValues(typeof(TraitConditionCategory)))
            {
                string key = category.ToString();
                if (!profile.conditionPressure.TryGetValue(key, out float pressure)) continue;
                if (pressure < AcquisitionPressureThreshold) continue;

                // Threshold crossed — attempt a trait roll from this pool
                if (_pools.TryGetValue(category, out var pool) && pool.entries.Count > 0)
                {
                    string rolledId = WeightedRoll(pool.entries, npc);
                    if (rolledId != null)
                        TryAddTrait(npc, rolledId, currentTick);
                }

                // Reset pressure after the roll (regardless of whether a trait was added)
                profile.conditionPressure[key] = 0f;
            }
        }

        /// <summary>
        /// Performs a weighted random selection from the pool.
        /// Skips traits the NPC already possesses.
        /// Returns null if no eligible trait can be selected.
        /// </summary>
        private string WeightedRoll(List<WeightedTraitEntry> entries, NPCInstance npc)
        {
            // Build eligible list (exclude already-held traits)
            var eligible = new List<WeightedTraitEntry>();
            float totalWeight = 0f;
            foreach (var e in entries)
            {
                if (NpcHasTrait(npc, e.traitId)) continue;
                eligible.Add(e);
                totalWeight += e.weight;
            }
            if (eligible.Count == 0 || totalWeight <= 0f) return null;

            float roll = UnityEngine.Random.value * totalWeight;
            float cumulative = 0f;
            foreach (var e in eligible)
            {
                cumulative += e.weight;
                if (roll <= cumulative) return e.traitId;
            }
            return eligible[eligible.Count - 1].traitId;
        }

        /// <summary>
        /// Attempts to add a trait to an NPC.
        /// Resolves conflicts on the same conflict axis:
        ///   — if a conflictDowngradeTarget is defined and the axes are compatible (equal or
        ///     one/both unspecified), both conflicting traits are replaced by the downgrade target.
        ///   — otherwise, the existing conflicting trait is removed and the new one is not added.
        /// </summary>
        public void TryAddTrait(NPCInstance npc, string traitId, int currentTick)
        {
            if (!FeatureFlags.NpcTraits) return;
            if (!_traits.TryGetValue(traitId, out var def)) return;
            if (NpcHasTrait(npc, traitId)) return;   // already held

            var profile = npc.GetOrCreateTraitProfile();

            // Conflict resolution — check conflictingTraitIds in the incoming trait's definition
            bool conflictFound = false;
            string downgradeTarget = null;
            for (int i = profile.traits.Count - 1; i >= 0; i--)
            {
                var active = profile.traits[i];
                if (!_traits.TryGetValue(active.traitId, out var existingDef)) continue;

                // Conflict is category-scoped
                if (existingDef.category != def.category) continue;

                if (def.conflictingTraitIds.Contains(active.traitId))
                {
                    // Determine downgrade target: prefer the incoming trait's definition,
                    // fall back to the existing trait's definition.
                    // Downgrade is allowed when axes match OR when one/both axes are unspecified
                    // (a trait without a conflictAxis is compatible with any axis).
                    if (downgradeTarget == null)
                    {
                        if (!string.IsNullOrEmpty(def.conflictDowngradeTarget) &&
                            AxesCompatible(def.conflictAxis, existingDef.conflictAxis))
                        {
                            downgradeTarget = def.conflictDowngradeTarget;
                        }
                        else if (!string.IsNullOrEmpty(existingDef.conflictDowngradeTarget) &&
                                 AxesCompatible(existingDef.conflictAxis, def.conflictAxis))
                        {
                            downgradeTarget = existingDef.conflictDowngradeTarget;
                        }
                    }

                    // Reverse the conflicting trait's permanent mood modifier before removing it.
                    string removedTraitId = active.traitId;
                    profile.traits.RemoveAt(i);
                    _mood?.RemoveModifier(npc, TraitMoodEventId(removedTraitId), "trait_system");
                    conflictFound = true;
                }
            }
            if (conflictFound)
            {
                ApplyTraitEffects(npc);

                // If a downgrade target is defined, add it now instead of leaving both deleted.
                if (!string.IsNullOrEmpty(downgradeTarget))
                    TryAddTrait(npc, downgradeTarget, currentTick);

                return;
            }

            // No conflict — add the trait
            profile.traits.Add(new ActiveTrait
            {
                traitId        = traitId,
                strength       = 1f,
                acquisitionTick = currentTick,
            });

            ApplyTraitEffects(npc);

            // Push a permanent mood modifier for traits with MoodModifier effects.
            // PermanentDuration (-1) ensures the impact persists while the trait is held;
            // it is reversed by RemoveModifier when the trait is removed.
            if (_mood != null)
            {
                foreach (var effect in def.effects)
                {
                    if (effect.target == TraitEffectTarget.MoodModifier)
                        _mood.PushModifier(npc, TraitMoodEventId(traitId), effect.magnitude,
                                           PermanentDuration, currentTick, "trait_system");
                }
            }
        }

        // ── Decay ─────────────────────────────────────────────────────────────

        private void ProcessDecay(NPCInstance npc, int currentTick)
        {
            var profile = npc.traitProfile;
            bool effectsDirty = false;
            for (int i = profile.traits.Count - 1; i >= 0; i--)
            {
                var active = profile.traits[i];
                if (!_traits.TryGetValue(active.traitId, out var def)) continue;
                if (def.requiresEventToRemove) continue;      // exempt from passive decay
                if (def.decayRatePerDay <= 0f) continue;      // no passive decay configured

                float oldStrength = active.strength;
                active.strength -= def.decayRatePerDay;
                if (active.strength < 0f) active.strength = 0f;

                if (active.strength <= 0f)
                {
                    profile.traits.RemoveAt(i);
                    effectsDirty = true;
                }
                else if (!Mathf.Approximately(oldStrength, active.strength))
                {
                    // Strength changed but trait still active — effects must be recomputed.
                    effectsDirty = true;
                }
            }
            if (effectsDirty) ApplyTraitEffects(npc);
        }

        // ── Event-gated removal ───────────────────────────────────────────────

        /// <summary>
        /// Removes an event-gated trait from an NPC and fires a mood modifier event
        /// to reflect the positive change of losing a negative trait (or vice versa).
        /// Called by external event systems (e.g. MedicalTickSystem on wound recovery,
        /// counselling system on therapy completion).
        /// </summary>
        public void TriggerEventRemoval(NPCInstance npc, string traitId, int currentTick)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.traitProfile;
            if (profile == null) return;

            for (int i = profile.traits.Count - 1; i >= 0; i--)
            {
                if (profile.traits[i].traitId != traitId) continue;

                profile.traits.RemoveAt(i);
                ApplyTraitEffects(npc);

                if (_traits.TryGetValue(traitId, out var def) && _mood != null)
                {
                    // Reverse the permanent mood modifier that was applied at acquisition.
                    _mood.RemoveModifier(npc, TraitMoodEventId(traitId), "trait_system");

                    // Fire a short mood boost/penalty to mark the trait-removal event itself.
                    // Negative traits being removed are a positive mood event; positive traits
                    // being removed are a negative mood event.
                    float moodDelta = def.valence == TraitValence.Negative ? 5f : -3f;
                    _mood.PushModifier(npc, $"trait_removed_{traitId}", moodDelta,
                                       48, currentTick, "trait_system");
                }

                return;
            }
        }

        // ── Life event trigger APIs ───────────────────────────────────────────

        /// <summary>
        /// Called when a crew member dies.  Registers a WitnessDeath condition pressure
        /// spike on every other living crew NPC — the pressure can eventually cause a
        /// trauma trait to fire if it accumulates past the acquisition threshold.
        /// </summary>
        public void NotifyCrewDeath(NPCInstance deadNpc, StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            foreach (var npc in station.npcs.Values)
            {
                if (npc.uid == deadNpc.uid) continue;
                if (!npc.IsCrew()) continue;
                if (npc.statusTags.Contains("dead")) continue;
                RegisterConditionPressure(npc, TraitConditionCategory.WitnessDeath, 3f);
                // 12-axis: witnessing death pushes Emotional Expression toward empathy (+)
                if (FeatureFlags.UseFullTraitSystem)
                    AddPressure(npc, "emotional_expression", 3f);
            }
            station.LogEvent($"Crew death witnessed by station. Trauma pressure applied.");
        }

        /// <summary>
        /// Called when an NPC has been involved in extended combat for a significant period.
        /// Registers an ExtendedCombat condition pressure event.
        /// </summary>
        public void OnExtendedCombat(NPCInstance npc, StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            RegisterConditionPressure(npc, TraitConditionCategory.ExtendedCombat, 2f);
            // 12-axis: extended combat pushes Courage toward bold (+)
            if (FeatureFlags.UseFullTraitSystem)
                AddPressure(npc, "courage", 2f);
        }

        /// <summary>
        /// Called when an NPC completes a mentoring session with another crew member.
        /// Registers a LongTermMentoring condition pressure event on the mentee.
        /// </summary>
        public void OnMentoringSession(NPCInstance mentee, StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            RegisterConditionPressure(mentee, TraitConditionCategory.LongTermMentoring, 2f);
            // 12-axis: mentoring pushes Intellectual Drive toward curious (+)
            if (FeatureFlags.UseFullTraitSystem)
                AddPressure(mentee, "intellectual_drive", 2f);
        }

        /// <summary>
        /// Called by the counselling/therapy system (WO-NPC-003) when a session completes
        /// successfully.  Removes all therapy-removable traits from the NPC.
        /// </summary>
        public void OnCounsellingComplete(NPCInstance npc, StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.traitProfile;
            if (profile == null) return;

            // Collect removable trait IDs first to avoid modifying the list mid-iteration.
            var toRemove = new List<string>();
            foreach (var at in profile.traits)
            {
                if (_traits.TryGetValue(at.traitId, out var def) && def.therapyRemovable)
                    toRemove.Add(at.traitId);
            }

            foreach (var tid in toRemove)
                TriggerEventRemoval(npc, tid, station?.tick ?? 0);

            station?.LogEvent($"{npc.name}: counselling complete — therapy-removable traits cleared.");
        }

        // ── Effect Application ────────────────────────────────────────────────

        /// <summary>
        /// Recalculates and applies the combined trait effects to the NPC's
        /// traitWorkModifier. Called after any trait is added or removed.
        /// workModifier is managed exclusively by MoodSystem and is not touched here.
        /// </summary>
        public void ApplyTraitEffects(NPCInstance npc)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.traitProfile;
            if (profile == null) return;

            float workSpeedDelta = 0f;
            foreach (var active in profile.traits)
            {
                if (!_traits.TryGetValue(active.traitId, out var def)) continue;
                float scale = active.strength;
                foreach (var effect in def.effects)
                {
                    switch (effect.target)
                    {
                        case TraitEffectTarget.WorkSpeedModifier:
                            workSpeedDelta += effect.magnitude * scale;
                            break;
                        case TraitEffectTarget.MoodModifier:
                            // Mood effects are applied as permanent modifiers via MoodSystem.PushModifier
                            // in TryAddTrait and reversed via MoodSystem.RemoveModifier in TriggerEventRemoval.
                            // ApplyTraitEffects only manages workSpeedDelta; mood is handled separately.
                            break;
                        default:
                            // Surface unsupported effect targets so data in core_traits.json
                            // does not silently do nothing.
                            Debug.LogWarning(
                                $"[TraitSystem] Unsupported TraitEffectTarget '{effect.target}' " +
                                $"on trait '{def.traitId}' for NPC '{npc.name}'. Effect ignored.");
                            break;
                    }
                }
            }
            // Store in a dedicated field so it doesn't conflict with MoodSystem's workModifier.
            npc.traitWorkModifier = Mathf.Clamp(1.0f + workSpeedDelta, 0.5f, 2.0f);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public bool NpcHasTrait(NPCInstance npc, string traitId)
        {
            if (npc.traitProfile == null) return false;
            foreach (var t in npc.traitProfile.traits)
                if (t.traitId == traitId) return true;
            return false;
        }

        /// <summary>Returns the ActiveTrait entry for the given traitId, or null.</summary>
        public ActiveTrait GetActiveTrait(NPCInstance npc, string traitId)
        {
            if (npc.traitProfile == null) return null;
            foreach (var t in npc.traitProfile.traits)
                if (t.traitId == traitId) return t;
            return null;
        }

        /// <summary>
        /// Returns the MoodSystem event-ID used for the permanent per-trait mood modifier.
        /// Centralised here so addition (TryAddTrait) and removal (TriggerEventRemoval,
        /// conflict resolution) always refer to the same key.
        /// </summary>
        private static string TraitMoodEventId(string traitId) => $"trait_mood_{traitId}";

        /// <summary>
        /// Returns true when two conflict axes are compatible for downgrade purposes.
        /// Compatibility holds when axes are equal OR when one or both are unspecified
        /// (an empty axis means "compatible with any axis").
        /// </summary>
        private static bool AxesCompatible(string axisA, string axisB) =>
            string.IsNullOrEmpty(axisA) ||
            string.IsNullOrEmpty(axisB) ||
            axisA == axisB;

        // ── Lineage API ───────────────────────────────────────────────────────

        /// <summary>
        /// Apply a lineage pressure event to an NPC.
        /// direction = +1 (positive push) or -1 (negative push).
        /// Respects a 24h cooldown per lineage axis and reduces negative trigger chance
        /// by WIS modifier × wisModifierReductionPerPoint.
        /// </summary>
        public void ApplyLineageEvent(NPCInstance npc, string lineageId, int direction,
                                      StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            if (!_lineages.TryGetValue(lineageId, out var def)) return;

            var profile = npc.GetOrCreateTraitProfile();

            // Cooldown: one trigger per lineage per 24 in-game hours (96 ticks)
            if (profile.lineageCooldownEndTick.TryGetValue(lineageId, out int cooldownEnd) &&
                station.tick < cooldownEnd) return;

            // Probability check (base 30 %; WIS modifier reduces negative trigger chance only)
            float chance = def.baseTriggerChance;
            int wisMod = AbilityScores.GetModifier(npc.abilityScores.WIS);
            if (direction < 0)
                chance = Mathf.Max(0.05f, chance - wisMod * def.wisModifierReductionPerPoint);
            if (UnityEngine.Random.value > chance) return;

            // Move position ±1 clamped to -2..+2
            if (!profile.lineagePositions.TryGetValue(lineageId, out int current))
                current = 0;
            int newPos = Mathf.Clamp(current + direction, -2, 2);
            if (newPos == current) return; // already at limit

            profile.lineagePositions[lineageId]        = newPos;
            profile.lineageCooldownEndTick[lineageId]  = station.tick + 96;

            SyncLineageTraitToPosition(npc, def, current, newPos, station.tick);
            string sign = direction > 0 ? "+" : "-";
            station.LogEvent($"{npc.name}: {def.displayName} shifted to position {newPos} ({sign}1).");
        }

        /// <summary>
        /// Daily pass: ensures every NPC's lineage positions map to the correct
        /// active traits. Called once per day from Tick().
        /// </summary>
        public void TickLineages(StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;

            foreach (var npc in station.npcs.Values)
            {
                if (npc.traitProfile == null) continue;
                foreach (var kv in npc.traitProfile.lineagePositions)
                {
                    if (!_lineages.TryGetValue(kv.Key, out var def)) continue;
                    string targetTrait = def.GetTraitIdAtPosition(kv.Value);

                    // Remove any stale lineage traits for this axis
                    for (int pos = -2; pos <= 2; pos++)
                    {
                        string candidateId = def.GetTraitIdAtPosition(pos);
                        if (candidateId == null || candidateId == targetTrait) continue;
                        for (int i = npc.traitProfile.traits.Count - 1; i >= 0; i--)
                            if (npc.traitProfile.traits[i].traitId == candidateId)
                            { npc.traitProfile.traits.RemoveAt(i); break; }
                    }

                    // Ensure the target trait is present (position 0 = neutral, no trait)
                    if (targetTrait != null && !NpcHasTrait(npc, targetTrait))
                        TryAddTrait(npc, targetTrait, station.tick);
                }
            }
        }

        private void SyncLineageTraitToPosition(NPCInstance npc, TraitLineageDefinition def,
                                                int oldPos, int newPos, int currentTick)
        {
            // Remove old trait (if any)
            string oldTrait = def.GetTraitIdAtPosition(oldPos);
            if (oldTrait != null)
            {
                for (int i = npc.traitProfile.traits.Count - 1; i >= 0; i--)
                {
                    if (npc.traitProfile.traits[i].traitId == oldTrait)
                    {
                        npc.traitProfile.traits.RemoveAt(i);
                        ApplyTraitEffects(npc);
                        break;
                    }
                }
            }

            // Add new trait (position 0 = neutral, no trait)
            string newTrait = def.GetTraitIdAtPosition(newPos);
            if (newTrait != null)
                TryAddTrait(npc, newTrait, currentTick);
        }

        // ── Axis Trait Definitions (loaded from JSON) ─────────────────────────
        private readonly Dictionary<string, List<AxisTraitDef>> _axisDefs = new Dictionary<string, List<AxisTraitDef>>();
        private readonly List<CompatibilityEntry> _compatMatrix = new List<CompatibilityEntry>();

        public struct AxisTraitDef
        {
            public string id;
            public string displayName;
            public string description;
            public string axisId;
            public int stage;
            public Dictionary<string, float> modifiers;
            public string passiveEffect; // e.g. "mood_bleed" for Resonant
            public bool subterfugeConcealmentImpossible;
        }

        public struct CompatibilityEntry
        {
            public string axisA;
            public string axisB;
            public float positivePositive;
            public float positiveNegative;
            public float negativePositive;
            public float negativeNegative;
            public string specialEvent;
        }

        public void LoadAxisData(string traitDefsJson, string compatMatrixJson)
        {
            // Clear existing data to avoid duplicates on reload
            _axisDefs.Clear();
            _compatMatrix.Clear();

            // Parse trait definitions — accepts a raw JSON array or a {"traits":[...]} wrapper
            List<object> defs = null;
            var defsRoot = MiniJSON.Json.Deserialize(traitDefsJson);
            if (defsRoot is List<object> rawArray)
                defs = rawArray;
            else if (defsRoot is Dictionary<string, object> defsWrapper &&
                     defsWrapper.ContainsKey("traits") &&
                     defsWrapper["traits"] is List<object> wrappedArray)
                defs = wrappedArray;

            if (defs != null)
            {
                foreach (var obj in defs)
                {
                    if (obj is not Dictionary<string, object> d) continue;
                    var def = new AxisTraitDef
                    {
                        id = d.GetString("id"),
                        displayName = d.GetString("display_name", d.GetString("id")),
                        description = d.GetString("description", ""),
                        axisId = d.GetString("axis_id"),
                        stage = d.GetInt("stage"),
                        modifiers = new Dictionary<string, float>(),
                        passiveEffect = d.ContainsKey("passive_effect") ? d.GetString("passive_effect") : null,
                        subterfugeConcealmentImpossible = d.GetBool("subterfuge_concealment_impossible")
                    };
                    if (d.ContainsKey("modifiers") && d["modifiers"] is Dictionary<string, object> mods)
                    {
                        foreach (var kv in mods)
                        {
                            if (kv.Value is double dv) def.modifiers[kv.Key] = (float)dv;
                            else if (kv.Value is long lv) def.modifiers[kv.Key] = lv;
                            else if (kv.Value is bool bv) def.modifiers[kv.Key] = bv ? 1f : 0f;
                        }
                    }
                    if (!_axisDefs.ContainsKey(def.axisId))
                        _axisDefs[def.axisId] = new List<AxisTraitDef>();
                    _axisDefs[def.axisId].Add(def);
                }
            }

            // Parse compatibility matrix — accepts a {"pairs":[...]} wrapper or a raw array
            List<object> matrix = null;
            var matrixRoot = MiniJSON.Json.Deserialize(compatMatrixJson);
            if (matrixRoot is Dictionary<string, object> matrixWrapper &&
                matrixWrapper.ContainsKey("pairs") &&
                matrixWrapper["pairs"] is List<object> pairsArray)
                matrix = pairsArray;
            else if (matrixRoot is List<object> rawMatrix)
                matrix = rawMatrix;

            if (matrix != null)
            {
                foreach (var obj in matrix)
                {
                    if (obj is not Dictionary<string, object> d) continue;
                    _compatMatrix.Add(new CompatibilityEntry
                    {
                        axisA = d.GetString("axis_a"),
                        axisB = d.GetString("axis_b"),
                        positivePositive = d.GetFloat("positive_positive"),
                        positiveNegative = d.GetFloat("positive_negative"),
                        negativePositive = d.GetFloat("negative_positive"),
                        negativeNegative = d.GetFloat("negative_negative"),
                        specialEvent = d.ContainsKey("special_event") && d["special_event"] != null ? d.GetString("special_event") : null,
                    });
                }
            }
        }

        // ── Axis Pressure System ──────────────────────────────────────────────

        /// Pressure thresholds per stage (absolute value).
        /// Stage ±3 = 3 pts, ±2 = 5, ±1 = 7, 0 = 10.
        private static int GetPressureThreshold(int stage)
        {
            int abs = Math.Abs(stage);
            return abs switch { 3 => 3, 2 => 5, 1 => 7, _ => 10 };
        }

        /// <summary>
        /// Adds pressure on an axis for an NPC. Positive amount pushes toward +3,
        /// negative amount pushes toward -3.
        /// </summary>
        public void AddPressure(NPCInstance npc, string axisId, float amount)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.GetOrCreateTraitProfile();
            var state = GetOrCreateAxisState(profile, axisId);

            if (amount > 0f)
                state.positivePressure += amount;
            else if (amount < 0f)
                state.negativePressure += -amount; // store as positive value

            // Cancel opposing pressures
            float cancel = Mathf.Min(state.positivePressure, state.negativePressure);
            if (cancel > 0f)
            {
                state.positivePressure -= cancel;
                state.negativePressure -= cancel;
            }

            // Check threshold for shifts
            int threshold = GetPressureThreshold(state.currentStage);

            if (state.positivePressure >= threshold && state.currentStage < 3)
            {
                state.currentStage++;
                state.positivePressure = 0f;
                state.negativePressure = 0f;
            }
            else if (state.negativePressure >= threshold && state.currentStage > -3)
            {
                state.currentStage--;
                state.positivePressure = 0f;
                state.negativePressure = 0f;
            }
        }

        private static AxisState GetOrCreateAxisState(NpcTraitProfile profile, string axisId)
        {
            foreach (var s in profile.axisStates)
                if (s.axisId == axisId) return s;
            var ns = new AxisState { axisId = axisId, currentStage = 0 };
            profile.axisStates.Add(ns);
            return ns;
        }

        /// <summary>
        /// Returns trait compatibility between two NPCs. Positive = harmony, negative = friction.
        /// Sums all matching axis-pair modifiers from compatibility matrix, scaled by stage.
        /// </summary>
        public float GetCompatibility(NPCInstance npcA, NPCInstance npcB)
        {
            var profileA = npcA.traitProfile;
            var profileB = npcB.traitProfile;
            if (profileA == null || profileB == null) return 0f;

            float total = 0f;

            foreach (var entry in _compatMatrix)
            {
                var stateA_axisA = FindAxisState(profileA, entry.axisA);
                var stateA_axisB = FindAxisState(profileA, entry.axisB);
                var stateB_axisA = FindAxisState(profileB, entry.axisA);
                var stateB_axisB = FindAxisState(profileB, entry.axisB);

                // NPC A on axis_a, NPC B on axis_b
                total += ComputePairModifier(entry, stateA_axisA?.currentStage ?? 0, stateB_axisB?.currentStage ?? 0);

                // NPC A on axis_b, NPC B on axis_a (only if axes differ to avoid double count)
                if (entry.axisA != entry.axisB)
                    total += ComputePairModifier(entry, stateB_axisA?.currentStage ?? 0, stateA_axisB?.currentStage ?? 0);
            }

            return total;
        }

        private static float ComputePairModifier(CompatibilityEntry entry, int stageA, int stageB)
        {
            if (stageA == 0 || stageB == 0) return 0f;

            bool posA = stageA > 0;
            bool posB = stageB > 0;

            float baseModifier;
            if (posA && posB) baseModifier = entry.positivePositive;
            else if (posA && !posB) baseModifier = entry.positiveNegative;
            else if (!posA && posB) baseModifier = entry.negativePositive;
            else baseModifier = entry.negativeNegative;

            float scaling = (Math.Abs(stageA) / 3f) * (Math.Abs(stageB) / 3f);
            return baseModifier * scaling;
        }

        private static AxisState FindAxisState(NpcTraitProfile profile, string axisId)
        {
            foreach (var s in profile.axisStates)
                if (s.axisId == axisId) return s;
            return null;
        }

        /// <summary>
        /// Aggregates all active trait modifiers for an NPC based on their axis states.
        /// </summary>
        public TraitModifierSet GetTraitModifiers(NPCInstance npc)
        {
            var result = TraitModifierSet.Default();
            var profile = npc.traitProfile;
            if (profile == null) return result;

            foreach (var state in profile.axisStates)
            {
                if (state.currentStage == 0) continue;
                var traitDef = FindAxisTraitDef(state.axisId, state.currentStage);
                if (traitDef.id == null) continue;

                foreach (var kv in traitDef.modifiers)
                {
                    switch (kv.Key)
                    {
                        case "mood_recovery_mult": result.MoodRecoveryMult *= kv.Value; break;
                        case "mood_floor_delta": result.MoodFloorDelta += kv.Value; break;
                        case "work_speed_mult": result.WorkSpeedMult *= kv.Value; break;
                        case "all_xp_mult": result.AllXpMult *= kv.Value; break;
                        case "tension_accumulation_mult": result.TensionAccumulationMult *= kv.Value; break;
                        case "relationship_gain_mult": result.RelationshipGainMult *= kv.Value; break;
                        case "social_need_depletion_mult": result.SocialNeedDepletionMult *= kv.Value; break;
                        case "sanity_recovery_mult": result.SanityRecoveryMult *= kv.Value; break;
                        case "injury_risk_mult": result.InjuryRiskMult *= kv.Value; break;
                        case "illness_recovery_mult": result.IllnessRecoveryMult *= kv.Value; break;
                        case "combat_xp_mult": result.CombatXpMult *= kv.Value; break;
                        case "departure_threshold_mult": result.DepartureThresholdMult *= kv.Value; break;
                        case "science_xp_mult": result.ScienceXpMult *= kv.Value; break;
                        case "conditioning_xp_mult": result.ConditioningXpMult *= kv.Value; break;
                        case "manipulation_quality_bonus": result.ManipulationQualityBonus += kv.Value; break;
                        case "articulation_quality_bonus": result.ArticulationQualityBonus += kv.Value; break;
                        case "trust_gain_mult": result.TrustGainMult *= kv.Value; break;
                        case "hostile_conversation_immune": if (kv.Value > 0) result.HostileConversationImmune = true; break;
                        case "faction_recruitment_immune": if (kv.Value > 0) result.FactionRecruitmentImmune = true; break;
                    }
                }

                if (traitDef.subterfugeConcealmentImpossible)
                    result.SubterfugeConcealmentImpossible = true;
            }

            return result;
        }

        private AxisTraitDef FindAxisTraitDef(string axisId, int stage)
        {
            if (_axisDefs.TryGetValue(axisId, out var list))
            {
                foreach (var d in list)
                    if (d.stage == stage) return d;
            }
            return default;
        }

        /// <summary>
        /// Resonant passive tick: mood bleed to nearby NPCs.
        /// Called from the main Tick method when axis data is loaded.
        /// </summary>
        private void TickResonantBleed(StationState station)
        {
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                var profile = npc.traitProfile;
                if (profile == null) continue;

                var emExState = FindAxisState(profile, "emotional_expression");
                if (emExState == null || emExState.currentStage != 3) continue;

                // This NPC is Resonant — bleed mood to nearby NPCs
                float moodBleed = npc.moodScore > 50f ? 2f : -2f;

                foreach (var other in station.npcs.Values)
                {
                    if (other.uid == npc.uid) continue;
                    if (!other.IsCrew()) continue;

                    int dist = SpatialHelpers.TileDistance(npc.tileCol, npc.tileRow, other.tileCol, other.tileRow);
                    if (dist > 3) continue;

                    float strength = dist <= 1 ? 1f : 0.5f;
                    _mood?.PushModifier(other,
                        $"resonant_bleed_{npc.uid}",
                        moodBleed * strength,
                        5, station.tick, "trait_passive");
                }
            }
        }
    }

    public struct TraitModifierSet
    {
        public float MoodRecoveryMult;
        public float MoodFloorDelta;
        public float WorkSpeedMult;
        public float AllXpMult;
        public float TensionAccumulationMult;
        public float RelationshipGainMult;
        public float SocialNeedDepletionMult;
        public float SanityRecoveryMult;
        public float InjuryRiskMult;
        public float IllnessRecoveryMult;
        public float CombatXpMult;
        public float DepartureThresholdMult;
        public float ScienceXpMult;
        public float ConditioningXpMult;
        public float ManipulationQualityBonus;
        public float ArticulationQualityBonus;
        public float TrustGainMult;
        public bool HostileConversationImmune;
        public bool FactionRecruitmentImmune;
        public bool SubterfugeConcealmentImpossible;
        public bool SubterfugeBypassImpossible;
        public bool AllMoraleModifiersImmune;

        public static TraitModifierSet Default() => new TraitModifierSet
        {
            MoodRecoveryMult = 1f, WorkSpeedMult = 1f, AllXpMult = 1f,
            TensionAccumulationMult = 1f, RelationshipGainMult = 1f,
            SocialNeedDepletionMult = 1f, SanityRecoveryMult = 1f,
            InjuryRiskMult = 1f, IllnessRecoveryMult = 1f,
            CombatXpMult = 1f, DepartureThresholdMult = 1f,
            ScienceXpMult = 1f, ConditioningXpMult = 1f, TrustGainMult = 1f,
        };
    }
}
