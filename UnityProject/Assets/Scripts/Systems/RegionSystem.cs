// RegionSystem — registry of region data and NPC generation biasing.
//
// Regions carry a 30-day resource history that biases the trait pool
// during NPC generation. High scarcity increases probability of
// scarcity/danger traits; high abundance increases stability traits.
//
// This file also contains NpcGenerationBiaser, a static utility that
// translates region resource pressure into condition category weights
// for the initial trait roll.
//
// Gated by FeatureFlags.RegionSimulation.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // =========================================================================
    // RegionRegistry — save-bound registry of all RegionData instances
    // =========================================================================

    public class RegionRegistry
    {
        private readonly Dictionary<string, RegionData> _regions =
            new Dictionary<string, RegionData>();

        /// <summary>Registers or updates a region.</summary>
        public void Register(RegionData region)
        {
            if (region == null || string.IsNullOrEmpty(region.regionId)) return;
            _regions[region.regionId] = region;
        }

        public bool TryGetRegion(string regionId, out RegionData region)
            => _regions.TryGetValue(regionId, out region);

        public RegionData GetRegion(string regionId)
        {
            _regions.TryGetValue(regionId, out var r);
            return r;
        }

        public IEnumerable<RegionData> AllRegions => _regions.Values;
        public int Count => _regions.Count;

        /// <summary>Records a daily resource sample for a region.</summary>
        public void RecordDailyResource(string regionId, ResourceType resource, float amount)
        {
            if (!FeatureFlags.RegionSimulation) return;
            if (!_regions.TryGetValue(regionId, out var region)) return;
            region.resourceHistory.RecordDailyAmount(resource, amount);
        }
    }

    // =========================================================================
    // NpcGenerationBiaser — translates region pressure into trait pool weights
    // =========================================================================

    public static class NpcGenerationBiaser
    {
        /// <summary>
        /// Minimum resource pressure score before a category is given elevated weight.
        /// </summary>
        public static float PressureActivationThreshold = 0.3f;

        /// <summary>
        /// Maximum weight multiplier applied to a biased condition category.
        /// </summary>
        public static float MaxBiasMultiplier = 3.0f;

        /// <summary>
        /// Probability that any individual NPC in a region acquires a biased trait.
        /// Proportional to the maximum pressure score across all categories.
        /// </summary>
        public static float BaseAcquisitionProbability = 0.35f;

        // ── Main entry point ──────────────────────────────────────────────────

        /// <summary>
        /// Generates a biased NpcTraitProfile for an NPC being created in the given region.
        /// Returns null when feature is disabled or no region is provided.
        /// </summary>
        public static NpcTraitProfile GenerateBiasedProfile(
            RegionData region,
            TraitSystem traitSystem,
            int currentTick)
        {
            if (!FeatureFlags.RegionSimulation) return null;
            if (region == null) return null;

            var weights = ComputeCategoryWeights(region.resourceHistory);
            if (weights.Count == 0) return null;

            float maxPressure = 0f;
            foreach (var w in weights.Values)
                if (w > maxPressure) maxPressure = w;

            // Not all NPCs acquire biased traits — probability scales with pressure
            float acquisitionProbability = BaseAcquisitionProbability * maxPressure;
            if (UnityEngine.Random.value > acquisitionProbability) return null;

            // Select the highest-weighted category and perform an initial trait roll
            TraitConditionCategory selectedCategory = TraitConditionCategory.Stability;
            float highestWeight = 0f;
            foreach (var kv in weights)
                if (kv.Value > highestWeight) { highestWeight = kv.Value; selectedCategory = kv.Key; }

            // Use a temporary NPC stub to call into the trait system
            var tempProfile = new NpcTraitProfile();
            tempProfile.conditionPressure[selectedCategory.ToString()] = traitSystem.AcquisitionPressureThreshold + 0.1f;

            // Return the profile; actual trait roll will happen on first Tick
            return tempProfile;
        }

        // ── Category weight mapping ───────────────────────────────────────────

        /// <summary>
        /// Maps region resource pressure scores to TraitConditionCategory weights.
        /// High scarcity → ResourceScarcity, Danger weights elevated.
        /// High abundance → ResourceAbundance, Stability weights elevated.
        /// </summary>
        public static Dictionary<TraitConditionCategory, float> ComputeCategoryWeights(
            RegionResourceHistory history)
        {
            var weights = new Dictionary<TraitConditionCategory, float>();
            if (history == null) return weights;

            float overallPressure = history.GetOverallResourcePressure();

            // Scarcity-related categories
            if (overallPressure >= PressureActivationThreshold)
            {
                weights[TraitConditionCategory.ResourceScarcity] =
                    1f + (overallPressure * (MaxBiasMultiplier - 1f));
                weights[TraitConditionCategory.Danger] =
                    1f + (overallPressure * (MaxBiasMultiplier - 1f) * 0.5f);
            }

            // Abundance-related categories (inverse of scarcity)
            float abundance = 1f - overallPressure;
            if (abundance >= PressureActivationThreshold)
            {
                weights[TraitConditionCategory.ResourceAbundance] =
                    1f + (abundance * (MaxBiasMultiplier - 1f));
                weights[TraitConditionCategory.Stability] =
                    1f + (abundance * (MaxBiasMultiplier - 1f) * 0.5f);
            }

            return weights;
        }
    }

    // =========================================================================
    // RegionSystem — tick-driven system that updates region resource history
    // =========================================================================

    public class RegionSystem
    {
        public readonly RegionRegistry Registry = new RegionRegistry();

        /// <summary>Called once per game tick. Records daily resource snapshots.</summary>
        public void Tick(StationState station)
        {
            if (!FeatureFlags.RegionSimulation) return;
            if (station.tick % TimeSystem.TicksPerDay != 0) return; // once per day

            // Sync in-memory registry with the station's persisted region dict
            foreach (var kv in station.regions)
                if (!Registry.TryGetRegion(kv.Key, out _))
                    Registry.Register(kv.Value);

            // TODO: Record actual resource flows when Horizon Simulation provides them.
            // For now the registry is populated but daily amounts must be set by external callers.
        }

        /// <summary>Creates and registers a new region stub.</summary>
        public RegionData CreateRegion(string regionId, string displayName,
                                        StationState station)
        {
            var region = new RegionData
            {
                regionId        = regionId,
                displayName     = displayName,
                simulationState = RegionSimulationState.Undiscovered,
            };
            Registry.Register(region);
            station.regions[regionId] = region;
            return region;
        }
    }
}
