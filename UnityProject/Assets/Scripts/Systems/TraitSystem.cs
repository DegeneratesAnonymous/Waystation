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
        }

        /// <summary>
        /// Called when an NPC completes a mentoring session with another crew member.
        /// Registers a LongTermMentoring condition pressure event on the mentee.
        /// </summary>
        public void OnMentoringSession(NPCInstance mentee, StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            RegisterConditionPressure(mentee, TraitConditionCategory.LongTermMentoring, 2f);
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
    }
}
