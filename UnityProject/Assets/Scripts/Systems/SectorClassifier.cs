// SectorClassifier — resolves a SectorArchetype from noise field values.
//
// Archetypes are evaluated in priority order; the first match wins.
// If nothing matches, VoidFringe is assigned as the default.

using Waystation.Models;

namespace Waystation.Systems
{
    public static class SectorClassifier
    {
        /// <summary>
        /// Classify a sector's archetype from its five noise field values.
        /// Evaluates threshold rules in priority order (1 = highest).
        /// </summary>
        public static SectorArchetype Classify(SectorNoiseValues f)
        {
            // Priority 1 — Confluence
            if (f.density >= 0.7f && f.resources >= 0.6f && f.hazard <= 0.3f)
                return SectorArchetype.Confluence;

            // Priority 2 — MineralBelt
            if (f.density >= 0.4f && f.resources >= 0.8f && f.hazard <= 0.5f)
                return SectorArchetype.MineralBelt;

            // Priority 3 — SingularityReach
            if (f.hazard >= 0.85f && f.stellarAge >= 0.7f)
                return SectorArchetype.SingularityReach;

            // Priority 4 — RemnantsZone
            if (f.density <= 0.35f && f.resources <= 0.4f && f.hazard >= 0.6f &&
                f.factionPressure <= 0.3f && f.stellarAge >= 0.75f)
                return SectorArchetype.RemnantsZone;

            // Priority 5 — StormBelt
            if (f.resources <= 0.35f && f.hazard >= 0.7f)
                return SectorArchetype.StormBelt;

            // Priority 6 — NebulaField
            if (f.density >= 0.4f && f.resources >= 0.3f && f.hazard >= 0.5f &&
                f.stellarAge <= 0.5f)
                return SectorArchetype.NebulaField;

            // Priority 7 — ContestedCore
            if (f.density >= 0.65f && f.resources <= 0.45f && f.factionPressure >= 0.7f)
                return SectorArchetype.ContestedCore;

            // Priority 8 — Cradle
            if (f.density >= 0.5f && f.resources >= 0.5f && f.hazard >= 0.4f &&
                f.factionPressure <= 0.3f && f.stellarAge <= 0.2f)
                return SectorArchetype.Cradle;

            // Priority 9 — FrontierScatter
            if (f.density <= 0.45f && f.resources >= 0.4f && f.hazard <= 0.4f &&
                f.factionPressure <= 0.35f)
                return SectorArchetype.FrontierScatter;

            // Default
            return SectorArchetype.VoidFringe;
        }

        /// <summary>System count range per archetype (min, max).</summary>
        public static (int min, int max) SystemCountRange(SectorArchetype archetype)
        {
            return archetype switch
            {
                SectorArchetype.Confluence      => (4, 7),
                SectorArchetype.MineralBelt     => (3, 6),
                SectorArchetype.SingularityReach => (2, 4),
                SectorArchetype.RemnantsZone    => (1, 3),
                SectorArchetype.StormBelt       => (2, 5),
                SectorArchetype.NebulaField     => (3, 6),
                SectorArchetype.ContestedCore   => (4, 7),
                SectorArchetype.Cradle          => (3, 6),
                SectorArchetype.FrontierScatter => (2, 4),
                SectorArchetype.VoidFringe      => (1, 2),
                _ => (1, 2),
            };
        }

        /// <summary>
        /// Resolve the exact system count from archetype range and density field.
        /// </summary>
        public static int ResolveSystemCount(SectorArchetype archetype, float density)
        {
            var (min, max) = SystemCountRange(archetype);
            return UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Lerp(min, max, density));
        }
    }
}
