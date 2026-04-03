// SectorGenerator — orchestrates noise sampling, archetype classification,
// system count resolution, and per-system budget/hazard derivation for a sector.
//
// Call PopulateSectorFields() after a SectorData is created (e.g. in
// GalaxyGenerator) to fill in noise values, archetype, and system count.

using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class SectorGenerator
    {
        /// <summary>
        /// Sample noise fields, classify archetype, and resolve system count
        /// for an existing <see cref="SectorData"/>.  Safe to call multiple times
        /// (idempotent — values are overwritten).
        /// </summary>
        public static void PopulateSectorFields(SectorData sector, int worldSeed)
        {
            int gridCol = Mathf.RoundToInt(
                (sector.coordinates.x - GalaxyGenerator.HomeX) / MapSystem.GalUnitPerCell);
            int gridRow = Mathf.RoundToInt(
                (sector.coordinates.y - GalaxyGenerator.HomeY) / MapSystem.GalUnitPerCell);

            sector.gridCol = gridCol;
            sector.gridRow = gridRow;
            sector.noiseFields = SectorNoiseFields.SampleAll(gridCol, gridRow, worldSeed);
            sector.archetype   = SectorClassifier.Classify(sector.noiseFields);
            sector.systemCount = SectorClassifier.ResolveSystemCount(
                sector.archetype, sector.noiseFields.density);

            // Ensure at least 1 system.
            if (sector.systemCount < 1) sector.systemCount = 1;
        }

        /// <summary>
        /// Build a <see cref="SystemBudget"/> for system index <paramref name="i"/> within
        /// a sector, using the sector's noise fields plus per-system local variance.
        /// </summary>
        public static SystemBudget BuildSystemBudget(SectorData sector, int systemIndex)
        {
            // Deterministic per-system RNG from sector UID + system index.
            int seed = SolarSystemGenerator.StableHash(sector.uid + $"_budget_{systemIndex}");
            var rng = new System.Random(seed);

            float res = sector.noiseFields.resources;
            float hz  = sector.noiseFields.hazard;
            float age = sector.noiseFields.stellarAge;

            float Variance(System.Random r, float range)
                => (float)(r.NextDouble() * range * 2.0 - range);

            var budget = new SystemBudget
            {
                oreBudget    = Mathf.Clamp01(res + Variance(rng, 0.15f)),
                gasBudget    = Mathf.Clamp01(res * 0.8f + Variance(rng, 0.15f)),
                exoticBudget = Mathf.Clamp01((res - 0.5f) * 0.6f + Variance(rng, 0.1f)),

                scanRangeMultiplier = Mathf.Lerp(1.0f, 0.35f, hz),
                passiveDamageRate   = hz > 0.6f
                    ? Mathf.Lerp(0f, 0.8f, (hz - 0.6f) / 0.4f)
                    : 0f,

                stellarAge   = age,
                isSingularity = false,
            };

            // SingularityReach: the system closest to grid centre gets singularity status.
            // We designate system index 0 as the centre-most (caller should sort by
            // distance to centre before assigning indices, but for simplicity index 0
            // is the anchor).
            if (sector.archetype == SectorArchetype.SingularityReach && systemIndex == 0)
            {
                budget.isSingularity       = true;
                budget.scanRangeMultiplier  = 0.2f;
                budget.passiveDamageRate    = 1.0f;
                budget.exoticBudget        = Mathf.Clamp01(res + 0.3f);
            }

            return budget;
        }

        /// <summary>
        /// Determine weighted star type for a system based on stellar age.
        /// Returns a <see cref="StarType"/> biased by the sector's stellarAge field.
        /// </summary>
        public static StarType BiasStarType(float stellarAge, System.Random rng)
        {
            if (stellarAge >= 0.7f)
            {
                // Old stars: WhiteDwarf, NeutronStar, RedDwarf weighted.
                double roll = rng.NextDouble();
                if (roll < 0.35) return StarType.WhiteDwarf;
                if (roll < 0.60) return StarType.NeutronStar;
                if (roll < 0.85) return StarType.RedDwarf;
                return StarType.YellowDwarf;  // small chance of standard
            }

            if (stellarAge <= 0.25f)
            {
                // Young stars: ProtoStar, YoungStar weighted.
                double roll = rng.NextDouble();
                if (roll < 0.30) return StarType.ProtoStar;
                if (roll < 0.65) return StarType.YoungStar;
                if (roll < 0.85) return StarType.BlueGiant;
                return StarType.YellowDwarf;
            }

            // Standard age: use default distribution.
            return (StarType)rng.Next(5);  // 0..4 = original five types
        }
    }

    /// <summary>
    /// Per-system resource budget, hazard conditions, and generation flags
    /// derived from sector noise fields.
    /// </summary>
    [System.Serializable]
    public struct SystemBudget
    {
        public float oreBudget;
        public float gasBudget;
        public float exoticBudget;

        public float scanRangeMultiplier;
        public float passiveDamageRate;

        public float stellarAge;
        public bool  isSingularity;
    }
}
