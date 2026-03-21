// FeatureFlags — compile-time feature gates.
// Set a flag to false to eliminate dead-code paths at build time.
// All three flags must compile cleanly when set to false.
namespace Waystation.Core
{
    public static class FeatureFlags
    {
        /// <summary>
        /// Enables NPC trait acquisition, decay, conflict resolution,
        /// and trait display in the Crew Menu.
        /// </summary>
        public const bool NpcTraits = true;

        /// <summary>
        /// Enables faction government aggregation and succession logic.
        /// Requires NpcTraits = true to produce meaningful aggregates.
        /// </summary>
        public const bool FactionGovernment = true;

        /// <summary>
        /// Enables regional resource history tracking and NPC generation biasing.
        /// Stub implementations are active when this is false.
        /// </summary>
        public const bool RegionSimulation = true;
    }
}
