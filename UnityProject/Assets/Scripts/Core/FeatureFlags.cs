// FeatureFlags — runtime feature gates.
// Set a flag to false to disable a feature. Use static (not const) to avoid
// CS0162 unreachable-code warnings from guard checks like
//   if (!FeatureFlags.X) return;
namespace Waystation.Core
{
    public static class FeatureFlags
    {
        /// <summary>
        /// Enables NPC trait acquisition, decay, conflict resolution,
        /// and trait display in the Crew Menu.
        /// </summary>
        public static bool NpcTraits = true;

        /// <summary>
        /// Enables faction government aggregation and succession logic.
        /// Requires NpcTraits = true to produce meaningful aggregates.
        /// </summary>
        public static bool FactionGovernment = true;

        /// <summary>
        /// Enables regional resource history tracking and NPC generation biasing.
        /// Stub implementations are active when this is false.
        /// </summary>
        public static bool RegionSimulation = true;
    }
}
