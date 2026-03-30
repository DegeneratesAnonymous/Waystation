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

        /// <summary>
        /// Enables crew departure execution: departure announcement events, player
        /// intervention window, physical departure sequence (NPC moves to landing pad
        /// or exit tile), and removal from active roster to the departed NPC pool.
        /// Set to false to revert TensionSystem to mood-penalty-only behaviour at
        /// DepartureRisk (the pre-NPC-007 default).
        /// </summary>
        public static bool NpcDeparture = true;

        /// <summary>
        /// Enables the counselling system (WO-NPC-003): Counsellor-role NPCs are
        /// automatically assigned counselling tasks when a breakdown NPC is present.
        /// Successful sessions call RegisterIntervention() on the patient (halting
        /// passive breakdown drain) and TriggerEventRemoval() for therapy-removable traits.
        /// Set to false to disable all counselling task assignment and outcome rolls
        /// without removing any SanitySystem or TraitSystem logic.
        /// </summary>
        public static bool NpcCounselling = true;

        /// <summary>
        /// Enables the DepartmentSystem per-tick processing loop (DepartmentSystem.Tick),
        /// including automated behaviours such as escalation alerts and any
        /// time-based department logic.
        /// When set to false, tick processing is skipped but existing departments,
        /// NPC assignments, and jobs are left unchanged, and department CRUD/
        /// assignment APIs remain callable. Callers are responsible for honouring
        /// this flag before invoking those APIs if they wish to fully disable
        /// department management in higher-level flows.
        /// </summary>
        public static bool DepartmentManagement = true;

        /// <summary>
        /// Enables procedural faction generation on sector grid unlock.
        /// When true, a faction generation roll fires for each newly-unlocked sector
        /// based on its regional resource profile and adjacent faction density.
        /// The starting scenario always seeds two factions (one friendly, one unfriendly)
        /// in adjacent sectors regardless of this flag — set to false to prevent
        /// further faction generation on subsequent sector unlocks.
        /// </summary>
        public static bool FactionProceduralGeneration = true;

        /// <summary>
        /// Enables the Persuasion skill price modifier in trade transactions.
        /// When true, the best negotiator's skill.persuasion level reduces buy prices
        /// and increases sell prices up to a maximum of 15%.
        /// Set to false to revert to manual-only trading at reputation-modified prices.
        /// </summary>
        public static bool TradePersuasionModifier = true;

        /// <summary>
        /// Enables standing order automation in TradeSystem.
        /// When true, configured buy/sell rules execute automatically on matching
        /// ship arrival when credits/inventory are sufficient.
        /// Manual and standing orders remain active simultaneously without conflict.
        /// Set to false to disable automatic execution without removing standing order data.
        /// </summary>
        public static bool TradeStandingOrders = true;

        /// <summary>
        /// Enables the full EconomySystem income/expenditure cycle:
        /// docking fees, faction contract payments, and supply/demand market dynamics.
        /// When false, credits revert to a flat ResourceSystem resource with no market
        /// dynamics. Supply/demand modifiers in TradeSystem are also gated by this flag.
        /// </summary>
        public static bool EconomySystem = true;

        /// <summary>
        /// Enables the fleet management system (WO-EXP-003): ship acquisition through
        /// purchase and construction, role-based mission eligibility, NPC crew
        /// assignment with full simulation carry-through (needs deplete during missions),
        /// ship damage states, repair, and destruction with crew outcome resolution.
        /// Set to false to disable fleet management without affecting visitor ships,
        /// MissionSystem, or any other existing system.
        /// </summary>
        public static bool FleetManagement = true;

        /// <summary>
        /// Switches the in-game HUD from the legacy IMGUI/uGUI system (GameHUD.cs /
        /// GameViewController.cs) to the UI Toolkit panel stack
        /// (WaystationHUDController.cs).
        ///
        /// When true:
        ///   • WaystationHUDController auto-installs in GameScene and registers as
        ///     listener for GameManager.OnTick / OnNewEvent / OnGameLoaded.
        ///   • GameHUD is not instantiated; all IMGUI/uGUI rendering (including the
        ///     ghost placement overlay, rotation indicator, drag-select rectangle, and
        ///     deconstruct hover outline) is inactive. These overlays are reimplemented
        ///     as UI Toolkit or dedicated overlay components as each panel is migrated.
        ///   • GameViewController uGUI components are bypassed.
        ///   • All callers continue to read HUD state from GameHUD.IsMouseOverDrawer,
        ///     GameHUD.InBuildMode, and GameHUD.SelectCrewMember(); WaystationHUDController
        ///     writes to those statics directly.
        ///
        /// When false (default during migration):
        ///   • Legacy IMGUI/uGUI HUD renders unchanged — no regressions.
        ///   • UI Toolkit panels are not installed.
        ///
        /// Set to true to opt in to the new UI Toolkit HUD; set to false to revert
        /// to the legacy system at any point without data loss.
        /// </summary>
        public static bool UseUIToolkitHUD = false;

        /// <summary>
        /// Enables the full real-time asteroid mission execution system (WO-EXP-004):
        /// four failure states (abandonment, autonomous abort, distress signal, total loss),
        /// crew viability evaluation, player retreat orders, distress signal rescue window,
        /// and in-HUD mission observation mode with tile map display.
        /// When false, asteroid missions complete normally via time expiry only (pre-EXP-004
        /// behaviour).
        /// </summary>
        public static bool AsteroidMissions = true;

        /// <summary>
        /// Enables the workbench-based crafting system (WO-EXP-005): recipe execution at
        /// typed workbenches, research-Datachip gating, material haul tasks, skill-scaled
        /// execution time, quality-tier output scaling, and multi-recipe workbench queues.
        /// CraftingSystem is a new system — disabling this flag has no effect on existing
        /// systems. Set to false to disable all crafting tick processing without removing
        /// recipe data or queue state.
        /// </summary>
        public static bool CraftingSystem = true;

        /// <summary>
        /// Enables farming negative events (STA-001): neglect accumulation, blight,
        /// pest infestation, spread, detection, and treatment mechanics.
        /// Set to false to revert TendTask to its previous no-effect stub behaviour.
        /// </summary>
        public static bool FarmingNegativeEvents = true;
    }
}
