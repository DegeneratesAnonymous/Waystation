// FeatureFlags — runtime feature gates.
// Set a flag to false to disable a feature. Use static (not const) to avoid
// CS0162 unreachable-code warnings from guard checks like
//   if (!FeatureFlags.X) return;
namespace Waystation.Core
{
    public static class FeatureFlags
    {
        /// <summary>
        /// Enables the full blueprint-to-built construction pipeline:
        /// material haul phase, time-based construction, mid-build material halts,
        /// damage states with performance scalars, and the repair task pipeline.
        /// Set to false to revert to instant placement (legacy DevMode behaviour).
        /// </summary>
        public static bool ConstructionPipeline = true;

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

        /// <summary>
        /// Enables the body-part-based medical system: wounds, bleeding, pain,
        /// consciousness, infection, surgery, and scarring on all NPCs.
        /// Set to false to disable all medical tick processing, treatment actions,
        /// and surgery without removing code.
        /// </summary>
        public static bool MedicalSystem = true;

        /// <summary>
        /// Enables NPC death consequences: body object spawned at death tile,
        /// proximity mood penalty, close-relationship grief events, body haul task
        /// generation, escalating unhandled-body penalty, and body removal on
        /// successful haul to a designated disposal tile.
        /// Body object is a new prefab — can be disabled via this flag without
        /// affecting existing death logic.
        /// </summary>
        public static bool NpcDeathHandling = true;

        /// <summary>
        /// Enables the Hygiene need: per-tick depletion, hygiene restoration when an
        /// NPC uses a shower or sink (prefers showers over sinks), and crisis state
        /// (mood + social penalties). Set to false to disable all hygiene tick
        /// processing without removing code. Species and trait depletion rate
        /// modifiers for all six needs are always active regardless of this flag.
        /// </summary>
        public static bool HygieneNeed = true;
    }
}
