// TraitSystem — centralized management of NPC trait acquisition, decay,
// conflict resolution, and effect application.
//
// Integration points:
//   • RegisterConditionPressure(npc, category, deltaPerDay) — called by external
//     systems (mood system, environment system, etc.) once per day tick.
//   • TriggerEventRemoval(npc, traitId) — called by events to remove event-gated traits.
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

        // ── Registry — trait definitions and pools ───────────────────────────

        private readonly Dictionary<string, NpcTraitDefinition>       _traits =
            new Dictionary<string, NpcTraitDefinition>();
        private readonly Dictionary<TraitConditionCategory, TraitPoolDefinition> _pools =
            new Dictionary<TraitConditionCategory, TraitPoolDefinition>();
        private readonly Dictionary<string, TraitLineageDefinition> _lineages =
            new Dictionary<string, TraitLineageDefinition>();

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
        /// Resolves conflicts (same category) — if a conflict exists both traits are removed
        /// and no new trait is added.
        /// </summary>
        public void TryAddTrait(NPCInstance npc, string traitId, int currentTick)
        {
            if (!FeatureFlags.NpcTraits) return;
            if (!_traits.TryGetValue(traitId, out var def)) return;
            if (NpcHasTrait(npc, traitId)) return;   // already held

            var profile = npc.GetOrCreateTraitProfile();

            // Conflict resolution — check conflictingTraitIds in the incoming trait's definition
            bool conflictFound = false;
            for (int i = profile.traits.Count - 1; i >= 0; i--)
            {
                var active = profile.traits[i];
                if (!_traits.TryGetValue(active.traitId, out var existingDef)) continue;

                // Conflict is category-scoped
                if (existingDef.category != def.category) continue;

                if (def.conflictingTraitIds.Contains(active.traitId))
                {
                    // Both removed; no new trait added
                    profile.traits.RemoveAt(i);
                    conflictFound = true;
                }
            }
            if (conflictFound)
            {
                // Traits were removed due to conflict; recompute effects to avoid stale modifiers.
                ApplyTraitEffects(npc);
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
        /// Removes an event-gated trait from an NPC.
        /// Called by external event systems (e.g. medical care, therapy).
        /// TODO: Wire specific event triggers here when medical/therapy systems are implemented.
        /// </summary>
        public void TriggerEventRemoval(NPCInstance npc, string traitId)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.traitProfile;
            if (profile == null) return;

            for (int i = profile.traits.Count - 1; i >= 0; i--)
            {
                if (profile.traits[i].traitId == traitId)
                {
                    profile.traits.RemoveAt(i);
                    ApplyTraitEffects(npc);
                    return;
                }
            }
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
                            // Intentionally not applied here: MoodSystem observes trait changes
                            // and applies mood modifiers via MoodSystem.PushModifier.
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
