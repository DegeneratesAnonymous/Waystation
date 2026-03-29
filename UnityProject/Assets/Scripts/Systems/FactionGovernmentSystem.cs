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
//   Pirate / FederalCouncil : all members averaged with equal weight (flat/collective)
//   Theocracy             : leaders only (like Monarchy/Authoritarian)
//   Technocracy           : all members; highest-skilled weighted 1.5×
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
        /// Returns null only when no valid NPC data is available.
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
                case GovernmentType.FederalCouncil:
                    // Flat collective — all members have equal weight; no leadership tier.
                    return AggregateCollective(faction, allNpcs, traitSystem, currentTick);

                case GovernmentType.Theocracy:
                    // Religious authority: leaders only (like monarchy/authoritarian).
                    return AggregateLeaderOnly(faction, allNpcs, traitSystem, currentTick);

                case GovernmentType.Technocracy:
                    // Merit-based: all members, but highest total skill weighted 1.5×.
                    return AggregateTechnocratic(faction, allNpcs, traitSystem, currentTick);

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

        // ── Pirate / FederalCouncil — flat collective ─────────────────────────

        private static FactionTraitAggregate AggregateCollective(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem,
            int currentTick)
        {
            var pool = new List<(NPCInstance npc, float weight)>();

            // All members have equal weight — no leadership tier.
            foreach (var npcId in faction.memberNpcIds)
            {
                if (allNpcs.TryGetValue(npcId, out var npc))
                    pool.Add((npc, 1.0f));
            }

            return BuildAggregate(pool, traitSystem, faction.governmentType, currentTick);
        }

        // ── Technocracy — merit-weighted ──────────────────────────────────────

        private static FactionTraitAggregate AggregateTechnocratic(
            FactionDefinition faction,
            Dictionary<string, NPCInstance> allNpcs,
            TraitSystem traitSystem,
            int currentTick)
        {
            var pool = new List<(NPCInstance npc, float weight)>();

            foreach (var npcId in faction.memberNpcIds)
            {
                if (!allNpcs.TryGetValue(npcId, out var npc)) continue;

                // Calculate total skill level as a merit proxy.
                int totalSkill = 0;
                foreach (var si in npc.skillInstances)
                    totalSkill += si.Level;

                // Leaders or high-skill NPCs receive a 1.5× weight boost.
                bool isHighSkill = totalSkill >= 10;
                float weight = (faction.leaderNpcIds.Contains(npcId) || isHighSkill)
                    ? LeaderWeightMultiplier
                    : 1.0f;
                pool.Add((npc, weight));
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
                // Select the best candidate: highest rank, then highest moodScore as tiebreaker.
                string bestId    = null;
                int    bestRank  = -1;
                float  bestMood  = -1f;
                foreach (var npcId in faction.memberNpcIds)
                {
                    if (faction.leaderNpcIds.Contains(npcId)) continue;
                    if (!allNpcs.TryGetValue(npcId, out var candidate)) continue;
                    if (candidate.rank > bestRank ||
                        (candidate.rank == bestRank && candidate.moodScore > bestMood))
                    {
                        bestRank = candidate.rank;
                        bestMood = candidate.moodScore;
                        bestId   = npcId;
                    }
                }
                if (bestId != null)
                {
                    faction.leaderNpcIds.Add(bestId);
                    faction.successionState = SuccessionState.Stable;
                    OnSuccessionResolved?.Invoke(faction.id);
                }
            }
            else if (candidates == 1)
            {
                // Single candidate — auto-select.
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

        // ── Stability constants ───────────────────────────────────────────────

        /// <summary>Stability score below which faction enters critical instability (rebellion risk).</summary>
        public const float StabilityThresholdCritical = 20f;

        /// <summary>Stability score below which faction is in low-stability (civil unrest).</summary>
        public const float StabilityThresholdLow = 40f;

        /// <summary>Stability score below which faction is in moderate tension.</summary>
        public const float StabilityThresholdMedium = 60f;

        /// <summary>Ticks of uninterrupted government required for full tenure bonus (≈1 year).</summary>
        public const int TenureFullStabilityTicks = TimeSystem.TicksPerDay * 360;

        // ── Reputation carry-over weights per government disposition ──────────

        /// <summary>Fraction of previous reputation retained when successor government is friendly.</summary>
        public const float RepCarryOverFriendly = 0.75f;

        /// <summary>Fraction of previous reputation retained when successor government is neutral.</summary>
        public const float RepCarryOverNeutral = 0.60f;

        /// <summary>Fraction of previous reputation retained when successor government is hostile.</summary>
        public const float RepCarryOverHostile = 0.50f;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when a faction's government type changes via any route.
        /// Payload: factionId, previous type, new type.
        /// </summary>
        public static event Action<string, GovernmentType, GovernmentType> OnGovernmentShifted;

        /// <summary>
        /// Fired when a faction's stability crosses a response threshold.
        /// Payload: factionId, response label.
        /// </summary>
        public static event Action<string, string> OnStabilityResponse;

        public FactionGovernmentSystem(TraitSystem traits) => _traits = traits;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Called once per game tick from GameManager.AdvanceTick.</summary>
        public void Tick(StationState station,
                         Dictionary<string, FactionDefinition> allFactions)
        {
            if (!FeatureFlags.FactionGovernment) return;
            if (station.tick % TimeSystem.TicksPerDay != 0) return; // once per day

            foreach (var kv in allFactions)
            {
                var faction = kv.Value;

                // Increment government tenure (guarded to once per day by the early return above).
                faction.governmentTenureTicks++;

                // Recalculate aggregate if absent or stale.
                if (!station.factionAggregates.TryGetValue(kv.Key, out var cached) ||
                    cached == null ||
                    station.tick - cached.calculatedAtTick >= FactionAggregator.AggregateStalenessWindowTicks)
                {
                    var aggregate = FactionAggregator.Calculate(
                        faction, station.npcs, _traits, allFactions, station.tick);
                    if (aggregate != null)
                    {
                        station.factionAggregates[kv.Key] = aggregate;
                        // Check whether the trait distribution shift warrants a government type recalculation.
                        CheckGovernmentShift(faction, aggregate, cached, station);
                    }
                    else
                        station.factionAggregates.Remove(kv.Key);
                }

                // Update stability and fire threshold response events.
                station.factionAggregates.TryGetValue(kv.Key, out var currentAggregate);
                float previousStability = faction.stabilityScore;
                faction.stabilityScore = ComputeStability(faction, currentAggregate, station);
                CheckStabilityResponse(faction, faction.stabilityScore, previousStability, station);
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
        /// Also checks for a government type shift.
        /// </summary>
        public void InvalidateAggregate(StationState station,
                                         FactionDefinition faction,
                                         Dictionary<string, FactionDefinition> allFactions)
        {
            if (!FeatureFlags.FactionGovernment) return;
            station.factionAggregates.TryGetValue(faction.id, out var previous);
            var aggregate = FactionAggregator.Calculate(
                faction, station.npcs, _traits, allFactions, station.tick);
            if (aggregate != null)
            {
                station.factionAggregates[faction.id] = aggregate;
                CheckGovernmentShift(faction, aggregate, previous, station);
            }
            else
                station.factionAggregates.Remove(faction.id);
        }

        /// <summary>
        /// Triggers a rapid government shift caused by an internal crisis event.
        /// The new government type is derived from the current trait aggregate.
        /// Reputation carry-over is applied based on successor disposition.
        /// </summary>
        public void TriggerInternalCrisisShift(
            FactionDefinition faction,
            StationState station,
            Dictionary<string, FactionDefinition> allFactions)
        {
            if (!FeatureFlags.FactionGovernment) return;
            station.factionAggregates.TryGetValue(faction.id, out var aggregate);
            var derivedType = FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(aggregate);
            if (derivedType != faction.governmentType)
            {
                ApplyGovernmentShift(faction, derivedType, station,
                    $"Internal crisis in '{faction.displayName}' forces government collapse.");
            }
        }

        /// <summary>
        /// Triggers a government shift caused by external pressure (player action or faction war).
        /// <paramref name="newType"/> is the government type the external pressure imposes.
        /// Reputation carry-over is applied based on successor disposition.
        /// </summary>
        public void TriggerExternalPressureShift(
            FactionDefinition faction,
            GovernmentType newType,
            StationState station,
            Dictionary<string, FactionDefinition> allFactions)
        {
            if (!FeatureFlags.FactionGovernment) return;
            if (newType != faction.governmentType)
            {
                ApplyGovernmentShift(faction, newType, station,
                    $"External pressure forces '{faction.displayName}' into a government change.");
            }
        }

        /// <summary>
        /// Returns the patron faction's reputation with the player for a vassalised faction.
        /// Only relevant for military and trade interaction contexts.
        /// Returns 0 if the faction is not vassalised or the patron is not found.
        /// </summary>
        public static float GetPatronReputation(
            string factionId,
            StationState station,
            Dictionary<string, FactionDefinition> allFactions)
        {
            if (!allFactions.TryGetValue(factionId, out var faction)) return 0f;
            if (string.IsNullOrEmpty(faction.vassalParentFactionId)) return 0f;
            return station.GetFactionRep(faction.vassalParentFactionId);
        }

        // ── Stability helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Computes the stability score (0–100) for a faction from four inputs, each
        /// contributing 25 points:
        ///   • Economic prosperity — mapped from player reputation with the faction.
        ///   • Military strength   — Physical trait category score from the aggregate.
        ///   • Population mood     — average moodScore of member NPCs (0–100).
        ///   • Government tenure   — normalised ticks since last government shift.
        /// </summary>
        public static float ComputeStability(
            FactionDefinition faction,
            FactionTraitAggregate aggregate,
            StationState station)
        {
            // 1. Economic prosperity: normalise reputation (-100..100) to 0..1.
            float rep = station.GetFactionRep(faction.id);
            float economic = Mathf.Clamp01((rep + 100f) / 200f);

            // 2. Military strength: Physical category score, clamped 0..1.
            float military = 0.5f; // default mid-range when no aggregate is available
            if (aggregate != null)
            {
                military = Mathf.Clamp01(
                    aggregate.categoryScores.TryGetValue(
                        TraitCategory.Physical.ToString(), out var physScore)
                    ? physScore : 0.5f);
            }

            // 3. Population mood/cohesion: average moodScore of known members.
            float moodSum   = 0f;
            int   moodCount = 0;
            foreach (var npcId in faction.memberNpcIds)
            {
                if (station.npcs.TryGetValue(npcId, out var npc))
                {
                    moodSum += npc.moodScore;
                    moodCount++;
                }
            }
            float populationMood = moodCount > 0 ? Mathf.Clamp01(moodSum / (moodCount * 100f)) : 0.5f;

            // 4. Government tenure: full bonus at TenureFullStabilityTicks.
            float tenure = Mathf.Clamp01((float)faction.governmentTenureTicks / TenureFullStabilityTicks);

            return (economic + military + populationMood + tenure) * 25f;
        }

        /// <summary>
        /// Fires a population response event when stability crosses a threshold.
        /// Four response types:
        ///   Critical (below 20) : Revolution — government collapse imminent.
        ///   Low      (20–40)    : Civil Unrest — protests and strikes.
        ///   Moderate (40–60)    : Tension — quiet discontent and defection risk.
        ///   Stable   (above 60) : No special response.
        /// </summary>
        private void CheckStabilityResponse(
            FactionDefinition faction,
            float newStability,
            float previousStability,
            StationState station)
        {
            // Only fire when a threshold is newly crossed (transition, not persistent state).
            if (newStability < StabilityThresholdCritical &&
                previousStability >= StabilityThresholdCritical)
            {
                string label = "Revolution";
                string msg   = $"⚠ CRITICAL: '{faction.displayName}' is on the verge of revolution " +
                               $"(stability {newStability:F0}).";
                station.LogEvent(msg);
                OnStabilityResponse?.Invoke(faction.id, label);

                // Critical instability can trigger a rapid government shift.
                station.factionAggregates.TryGetValue(faction.id, out var agg);
                var derivedType = FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg);
                if (derivedType != faction.governmentType)
                    ApplyGovernmentShift(faction, derivedType, station,
                        $"Revolution topples the government of '{faction.displayName}'.");
            }
            else if (newStability < StabilityThresholdLow &&
                     previousStability >= StabilityThresholdLow)
            {
                string label = "CivilUnrest";
                string msg   = $"⚠ '{faction.displayName}' is experiencing civil unrest " +
                               $"(stability {newStability:F0}).";
                station.LogEvent(msg);
                OnStabilityResponse?.Invoke(faction.id, label);
            }
            else if (newStability < StabilityThresholdMedium &&
                     previousStability >= StabilityThresholdMedium)
            {
                string label = "Tension";
                string msg   = $"'{faction.displayName}' shows signs of internal tension " +
                               $"(stability {newStability:F0}).";
                station.LogEvent(msg);
                OnStabilityResponse?.Invoke(faction.id, label);
            }
        }

        // ── Government shift helpers ──────────────────────────────────────────

        /// <summary>
        /// Compares the new aggregate against the previous one.  If the dominant trait
        /// category has shifted by more than <see cref="FactionProceduralGenerator.GovernmentShiftThreshold"/>
        /// (or the government type derived from the new aggregate differs), updates
        /// <see cref="FactionDefinition.governmentType"/> accordingly (population drift path).
        /// </summary>
        private void CheckGovernmentShift(
            FactionDefinition faction,
            FactionTraitAggregate newAggregate,
            FactionTraitAggregate previousAggregate,
            StationState station)
        {
            var derivedType = FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(newAggregate);

            // Always recalculate for generated factions so their government type evolves
            // naturally.  For static (data-loaded) factions only change if the shift crosses
            // the threshold and the derived type actually differs.
            if (!faction.isGenerated)
            {
                if (derivedType == faction.governmentType) return;

                // Check whether the dominant category score has shifted enough
                if (previousAggregate != null && !HasSignificantShift(newAggregate, previousAggregate))
                    return;
            }

            if (derivedType != faction.governmentType)
                ApplyGovernmentShift(faction, derivedType, station,
                    $"Population drift shifts '{faction.displayName}' government.");
        }

        /// <summary>
        /// Applies a government type change, updates reputation carry-over, resets tenure,
        /// logs the event, and fires <see cref="OnGovernmentShifted"/>.
        /// </summary>
        private static void ApplyGovernmentShift(
            FactionDefinition faction,
            GovernmentType newType,
            StationState station,
            string logMessage)
        {
            var oldType = faction.governmentType;

            // Reputation carry-over based on successor disposition.
            float carryOver = GetRepCarryOverMultiplier(newType);
            float currentRep = station.GetFactionRep(faction.id);
            float newRep     = Mathf.Clamp(currentRep * carryOver, -100f, 100f);
            station.factionReputation[faction.id] = newRep;

            faction.governmentType    = newType;
            faction.governmentTenureTicks = 0;

            string fullMsg = $"{logMessage} {oldType} → {newType} " +
                             $"(rep: {currentRep:+0.0;-0.0} → {newRep:+0.0;-0.0})";
            Debug.Log($"[FactionGovernmentSystem] {fullMsg}");
            station.LogEvent(fullMsg);

            OnGovernmentShifted?.Invoke(faction.id, oldType, newType);
        }

        /// <summary>
        /// Returns the reputation carry-over multiplier for the given successor government type.
        /// Friendly government types retain more of a positive reputation;
        /// hostile types discount it.
        /// </summary>
        private static float GetRepCarryOverMultiplier(GovernmentType newType)
        {
            switch (newType)
            {
                case GovernmentType.Democracy:
                case GovernmentType.Republic:
                case GovernmentType.Technocracy:
                case GovernmentType.FederalCouncil:
                    return RepCarryOverFriendly;

                case GovernmentType.Monarchy:
                case GovernmentType.CorporateVassal:
                    return RepCarryOverNeutral;

                case GovernmentType.Authoritarian:
                case GovernmentType.Pirate:
                case GovernmentType.Theocracy:
                    return RepCarryOverHostile;

                default:
                    return RepCarryOverNeutral;
            }
        }

        /// <summary>
        /// Returns true if any category's score has changed by more than
        /// <see cref="FactionProceduralGenerator.GovernmentShiftThreshold"/> between
        /// the two aggregates.
        /// </summary>
        private static bool HasSignificantShift(FactionTraitAggregate current,
                                                 FactionTraitAggregate previous)
        {
            foreach (var kv in current.categoryScores)
            {
                float prev = previous.categoryScores.TryGetValue(kv.Key, out var p) ? p : 0f;
                if (Mathf.Abs(kv.Value - prev) >= FactionProceduralGenerator.GovernmentShiftThreshold)
                    return true;
            }
            return false;
        }
    }
}
