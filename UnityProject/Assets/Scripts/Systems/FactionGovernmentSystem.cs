// FactionGovernmentSystem — faction trait aggregation and succession logic.
//
// Government type determines which NPC trait profiles are included in an
// aggregation and how they are weighted. The same NPC population under
// different government types produces different faction behaviour.
//
// Aggregation strategies:
//   Democracy / Republic  : all members averaged; leaders weighted 1.5×
//   Monarchy / Authoritarian : leaders only; falls back if vacant
//   CorporateVassal       : parent-faction leaders > installed management > workers
//   Pirate                : no aggregation — null returned; individual NPC level only
//
// Gated by FeatureFlags.FactionGovernment.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // =========================================================================
    // FactionAggregator — static utility
    // =========================================================================

    public static class FactionAggregator
    {
        // Configurable weights
        public static float LeaderWeightMultiplier    = 1.5f;   // Democracy/Republic
        public static float VassalParentTierWeight    = 1.0f;   // CorporateVassal: parent leaders
        public static float VassalManagementTierWeight = 0.6f; // CorporateVassal: installed mgmt
        public static float VassalWorkerTierWeight    = 0.25f;  // CorporateVassal: workers

        // Staleness window — aggregate is recalculated if this many ticks have elapsed.
        public static int AggregateStalenessWindowTicks = TimeSystem.TicksPerDay;

        // ── Main entry point ──────────────────────────────────────────────────

        /// <summary>
        /// Calculates the trait aggregate for a faction using the appropriate
        /// aggregation function for its government type.
        /// Returns null for Pirate factions or when no valid NPC data is available.
        /// </summary>
        public static FactionTraitAggregate Calculate(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem,
            Dictionary<string, FactionDefinition> allFactions,
            int currentTick)
        {
            if (!FeatureFlags.FactionGovernment) return null;
            if (faction == null) return null;

            // Check cache staleness
            if (allNpcs == null || allNpcs.Count == 0) return null;

            switch (faction.governmentType)
            {
                case GovernmentType.Democracy:
                case GovernmentType.Republic:
                    return AggregateDemocratic(faction, allNpcs, traitSystem, currentTick);

                case GovernmentType.Monarchy:
                case GovernmentType.Authoritarian:
                    return AggregateLeaderOnly(faction, allNpcs, traitSystem, currentTick);

                case GovernmentType.CorporateVassal:
                    return AggregateCorporateVassal(faction, allNpcs, traitSystem,
                                                     allFactions, currentTick);

                case GovernmentType.Pirate:
                    // TODO: Pirate Region mechanics — stub only. Individual NPC resolution.
                    return null;

                default:
                    return AggregateDemocratic(faction, allNpcs, traitSystem, currentTick);
            }
        }

        // ── Democracy / Republic ──────────────────────────────────────────────

        private static FactionTraitAggregate AggregateDemocratic(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem,
            int currentTick)
        {
            var pool = new List<(NPCInstance npc, float weight)>();

            foreach (var npcId in faction.memberNpcIds)
            {
                if (!allNpcs.TryGetValue(npcId, out var npc)) continue;
                float weight = faction.leaderNpcIds.Contains(npcId)
                    ? LeaderWeightMultiplier
                    : 1.0f;
                pool.Add((npc, weight));
            }

            return BuildAggregate(pool, traitSystem, faction.governmentType, currentTick);
        }

        // ── Monarchy / Authoritarian ──────────────────────────────────────────

        private static FactionTraitAggregate AggregateLeaderOnly(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem,
            int currentTick)
        {
            var pool = new List<(NPCInstance npc, float weight)>();

            foreach (var npcId in faction.leaderNpcIds)
            {
                if (!allNpcs.TryGetValue(npcId, out var npc)) continue;
                pool.Add((npc, 1.0f));
            }

            if (pool.Count == 0)
            {
                // No leaders defined — fall back to highest-seniority member
                NPCInstance fallback = null;
                int highestRank = -1;
                foreach (var npcId in faction.memberNpcIds)
                {
                    if (!allNpcs.TryGetValue(npcId, out var npc)) continue;
                    if (npc.rank > highestRank)
                    {
                        highestRank = npc.rank;
                        fallback    = npc;
                    }
                }

                if (fallback != null)
                {
                    pool.Add((fallback, 1.0f));
                }
                else
                {
                    // Vacant — set succession state
                    faction.successionState = SuccessionState.Vacant;
                    return null;
                }
            }

            return BuildAggregate(pool, traitSystem, faction.governmentType, currentTick);
        }

        // ── CorporateVassal ───────────────────────────────────────────────────

        private static FactionTraitAggregate AggregateCorporateVassal(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem,
            Dictionary<string, FactionDefinition> allFactions,
            int currentTick)
        {
            var pool = new List<(NPCInstance npc, float weight)>();

            // Tier 1: parent faction leaders
            if (!string.IsNullOrEmpty(faction.vassalParentFactionId) &&
                allFactions != null &&
                allFactions.TryGetValue(faction.vassalParentFactionId, out var parentFaction))
            {
                foreach (var npcId in parentFaction.leaderNpcIds)
                {
                    if (allNpcs.TryGetValue(npcId, out var npc))
                        pool.Add((npc, VassalParentTierWeight));
                }
            }

            // Tier 2: this faction's installed management (leaderNpcIds)
            foreach (var npcId in faction.leaderNpcIds)
            {
                if (allNpcs.TryGetValue(npcId, out var npc))
                    pool.Add((npc, VassalManagementTierWeight));
            }

            // Tier 3: worker population (members not in leaderNpcIds)
            foreach (var npcId in faction.memberNpcIds)
            {
                if (faction.leaderNpcIds.Contains(npcId)) continue;
                if (allNpcs.TryGetValue(npcId, out var npc))
                    pool.Add((npc, VassalWorkerTierWeight));
            }

            return BuildAggregate(pool, traitSystem, faction.governmentType, currentTick);
        }

        // ── Aggregate builder ─────────────────────────────────────────────────

        private static FactionTraitAggregate BuildAggregate(
            List<(NPCInstance npc, float weight)> pool,
            TraitSystem traitSystem,
            GovernmentType govType,
            int currentTick)
        {
            if (pool.Count == 0) return null;

            var aggregate = new FactionTraitAggregate
            {
                sourceGovernmentType = govType,
                calculatedAtTick     = currentTick,
            };

            float totalWeight = 0f;
            var catAccum    = new Dictionary<string, float>();
            var effectAccum = new Dictionary<string, float>();

            foreach (var (npc, weight) in pool)
            {
                totalWeight += weight;
                if (npc.traitProfile == null) continue;

                foreach (var active in npc.traitProfile.traits)
                {
                    if (!traitSystem.TryGetTrait(active.traitId, out var def)) continue;

                    string catKey = def.category.ToString();
                    if (!catAccum.ContainsKey(catKey)) catAccum[catKey] = 0f;
                    catAccum[catKey] += active.strength * weight;

                    foreach (var effect in def.effects)
                    {
                        string efxKey = effect.target.ToString();
                        if (!effectAccum.ContainsKey(efxKey)) effectAccum[efxKey] = 0f;
                        effectAccum[efxKey] += effect.magnitude * active.strength * weight;
                    }
                }
            }

            // Normalise category scores by total weight
            foreach (var kv in catAccum)
                aggregate.categoryScores[kv.Key] = totalWeight > 0f
                    ? kv.Value / totalWeight : 0f;

            // Effect sums are additive across the pool (not averaged)
            foreach (var kv in effectAccum)
                aggregate.aggregateEffects[kv.Key] = kv.Value;

            return aggregate;
        }
    }

    // =========================================================================
    // SuccessionEvaluator — static utility
    // =========================================================================

    public static class SuccessionEvaluator
    {
        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when succession resolves and a new leader is assigned.
        /// Payload: faction ID.
        /// </summary>
        public static event Action<string> OnSuccessionResolved;

        // ── Main entry point ──────────────────────────────────────────────────

        /// <summary>
        /// Called when a leader NPC is removed (death, departure, deposition).
        /// Updates successionState based on remaining candidates.
        /// Successor selection logic is stubbed — see TODO below.
        /// </summary>
        public static void EvaluateSuccession(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem)
        {
            if (!FeatureFlags.FactionGovernment) return;
            if (faction == null) return;

            // Count valid successor candidates
            int candidates = 0;
            foreach (var npcId in faction.memberNpcIds)
            {
                if (!faction.leaderNpcIds.Contains(npcId) &&
                    allNpcs.TryGetValue(npcId, out _))
                    candidates++;
            }

            if (faction.leaderNpcIds.Count > 0)
            {
                faction.successionState = SuccessionState.Stable;
            }
            else if (candidates > 1)
            {
                faction.successionState = SuccessionState.Contested;
            }
            else if (candidates == 1)
            {
                // TODO: Auto-select the single candidate as the new leader.
                // Deferred to follow-on work order (succession condition logic).
                faction.successionState = SuccessionState.Stable;
                string newLeaderId = null;
                foreach (var npcId in faction.memberNpcIds)
                    if (!faction.leaderNpcIds.Contains(npcId) && allNpcs.ContainsKey(npcId))
                    { newLeaderId = npcId; break; }
                if (newLeaderId != null)
                {
                    faction.leaderNpcIds.Add(newLeaderId);
                    OnSuccessionResolved?.Invoke(faction.id);
                }
            }
            else
            {
                faction.successionState = SuccessionState.Vacant;
            }
        }

        /// <summary>
        /// Removes a leader NPC from a faction and triggers succession evaluation.
        /// Call this instead of modifying leaderNpcIds directly.
        /// </summary>
        public static void RemoveLeader(
            FactionDefinition faction,
            string npcId,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem)
        {
            if (!FeatureFlags.FactionGovernment) return;
            faction.leaderNpcIds.Remove(npcId);
            EvaluateSuccession(faction, allNpcs, traitSystem);
        }
    }

    // =========================================================================
    // FactionGovernmentSystem — tick-driven system that keeps aggregates fresh
    // =========================================================================

    public class FactionGovernmentSystem
    {
        private readonly TraitSystem _traits;

        public FactionGovernmentSystem(TraitSystem traits) => _traits = traits;

        /// <summary>Called once per game tick from GameManager.AdvanceTick.</summary>
        public void Tick(StationState station,
                         Dictionary<string, FactionDefinition> allFactions)
        {
            if (!FeatureFlags.FactionGovernment) return;
            if (station.tick % TimeSystem.TicksPerDay != 0) return; // once per day

            foreach (var kv in allFactions)
            {
                var faction = kv.Value;
                // Recalculate if aggregate is absent or stale
                if (!station.factionAggregates.TryGetValue(kv.Key, out var cached) ||
                    cached == null ||
                    station.tick - cached.calculatedAtTick >= FactionAggregator.AggregateStalenessWindowTicks)
                {
                    var aggregate = FactionAggregator.Calculate(
                        faction, station.npcs, _traits, allFactions, station.tick);
                    if (aggregate != null)
                        station.factionAggregates[kv.Key] = aggregate;
                    else
                        station.factionAggregates.Remove(kv.Key);
                }
            }
        }

        /// <summary>
        /// Returns the cached aggregate for a faction, or null if unavailable.
        /// </summary>
        public FactionTraitAggregate GetAggregate(StationState station, string factionId)
        {
            if (!FeatureFlags.FactionGovernment) return null;
            station.factionAggregates.TryGetValue(factionId, out var agg);
            return agg;
        }

        /// <summary>
        /// Forces an immediate recalculation for a faction (e.g. after membership change).
        /// </summary>
        public void InvalidateAggregate(StationState station,
                                         FactionDefinition faction,
                                         Dictionary<string, FactionDefinition> allFactions)
        {
            if (!FeatureFlags.FactionGovernment) return;
            var aggregate = FactionAggregator.Calculate(
                faction, station.npcs, _traits, allFactions, station.tick);
            if (aggregate != null)
                station.factionAggregates[faction.id] = aggregate;
            else
                station.factionAggregates.Remove(faction.id);
        }
    }
}
