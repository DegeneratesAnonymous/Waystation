// GameManager — main orchestrator for Waystation.
// Singleton MonoBehaviour that owns all game systems and drives the main
// game loop tick by tick using Unity's Update cycle.
//
// Station policies:
//   visitor_policy : open / inspect / restrict
//   refugee_policy : accept / evaluate / deny
//   trade_stance   : active / passive / closed
//   security_level : minimal / standard / high
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Core
{
    public partial class GameManager : MonoBehaviour, ITopBarGameManager
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public const float RecruitCost       = 150f;
        public const float RepairPartsCost   =  10f;
        public const float RepairDamageAmount = 0.25f;

        /// <summary>Current save-file schema version.  Increment when breaking changes are introduced.</summary>
        private const int SaveVersion = 1;

        // ── Singleton ─────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Serialised settings (Inspector) ──────────────────────────────────
        [Header("Game Speed")]
        [Tooltip("Real-time seconds between game ticks.  At 1× one tick = 1 real second = 15 in-game minutes.")]
        [SerializeField] private float secondsPerTick = 1.0f;

        [Header("Difficulty")]
        [SerializeField] private string difficulty = "normal";

        [Header("Saves")]
        [SerializeField] private string saveFileName = "waystation_save.json";

        [Header("Autosave")]
        [Tooltip("Number of ticks between automatic saves.  0 = autosave disabled.")]
        [SerializeField] private int autosaveIntervalTicks = 120;

        // ── Save / load runtime state ─────────────────────────────────────────
        /// <summary>
        /// Human-readable error message from the most recent failed LoadGame() call.
        /// Empty when no error has occurred.  Set before OnLoadError is raised.
        /// </summary>
        public string LastLoadError { get; private set; } = string.Empty;

        /// <summary>Fired when LoadGame() fails — carries the player-facing error message.</summary>
        public event Action<string> OnLoadError;

        private bool _autosaveInProgress;

        /// <summary>
        /// ID of the scenario used when the current game was started via
        /// <see cref="NewGame"/>, or empty if no scenario was selected.
        /// Persisted in the save file and restored on load.
        /// </summary>
        public string ActiveScenarioId { get; private set; } = string.Empty;

        // ── Slot-based save constants ─────────────────────────────────────────
        /// <summary>
        /// Number of manual save slots available to the player (not including the
        /// autosave slot which uses a separate file).
        /// </summary>
        public const int SaveSlotCount = 5;

        /// <summary>
        /// Slot index used for the dedicated autosave file.
        /// Pass to <see cref="GetSaveSlotInfo"/> to read autosave metadata.
        /// </summary>
        public const int AutosaveSlotIndex = 0;

        // ── System references ─────────────────────────────────────────────────
        public ContentRegistry      Registry          { get; private set; }
        public ResourceSystem       Resources         { get; private set; }
        public NPCSystem            Npcs              { get; private set; }
        public JobSystem            Jobs              { get; private set; }
        public FactionSystem        Factions          { get; private set; }
        public CombatSystem         Combat            { get; private set; }
        public TradeSystem          Trade             { get; private set; }
        public EventSystem          Events            { get; private set; }
        public InventorySystem      Inventory         { get; private set; }
        public VisitorSystem        Visitors          { get; private set; }
        public BuildingSystem       Building          { get; private set; }
        public CommsSystem          Comms             { get; private set; }
        public NetworkSystem        Networks          { get; private set; }
        public UtilityNetworkManager UtilityNetworks  { get; private set; }
        public MissionSystem        Missions          { get; private set; }
        public RoomSystem           Rooms             { get; private set; }
        public ResearchSystem       Research          { get; private set; }
        public MapSystem            Map               { get; private set; }
        public AsteroidMissionSystem AsteroidMissions { get; private set; }

        // ── Visitor pipeline systems ──────────────────────────────────────────────────────────
        public AntennaSystem            Antenna       { get; private set; }
        public ShipVisitStateMachine    ShipVisits    { get; private set; }
        public NPCTaskQueueManager      TaskQueue     { get; private set; }
        public CommunicationsSystem     CommSystem    { get; private set; }

        // ── Farming / climate systems ───────────────────────────────────────────────────────
        public FarmingSystem            Farming       { get; private set; }
        public TemperatureSystem        Temperature   { get; private set; }

        // ── Mood & social systems ───────────────────────────────────────────────────────────────
        public MoodSystem               Mood          { get; private set; }
        public RelationshipRegistry     Relationships { get; private set; }
        public ConversationSystem       Conversations { get; private set; }
        public ProximitySystem          Proximity     { get; private set; }

        // ── Need & Sanity systems ─────────────────────────────────────────────────────────────
        public NeedSystem               Needs         { get; private set; }
        public SanitySystem             Sanity        { get; private set; }

        // ── Skill & Expertise system ────────────────────────────────────────────────────────────
        public SkillSystem              Skills        { get; private set; }
        public MentoringSystem          Mentoring     { get; private set; }

        // ── Trait, Tension, Faction Government, Region systems ───────────────────────────────────
        public TraitSystem              Traits        { get; private set; }
        public TensionSystem            Tension       { get; private set; }
        public FactionGovernmentSystem  FactionGov    { get; private set; }
        public RegionSystem             Regions       { get; private set; }

        /// <summary>Active region simulation — defaults to RegionSimulationStub.</summary>
        public IRegionSimulation        RegionSim     { get; private set; }

        /// <summary>Active faction history provider — defaults to FactionHistoryStub.</summary>
        public IFactionHistoryProvider  FactionHistory { get; private set; }

        // ── Medical system ─────────────────────────────────────────────────────────────────────
        public MedicalTickSystem        Medical       { get; private set; }
        public SurgerySystem            Surgery       { get; private set; }

        // ── Death handling system ──────────────────────────────────────────────────────────────
        public DeathHandlingSystem      DeathHandling { get; private set; }

        // ── Counselling system ─────────────────────────────────────────────────────────────────
        public CounsellingSystem        Counselling   { get; private set; }

        // ── Department system ──────────────────────────────────────────────────────────────────
        public DepartmentRegistry       DeptRegistry  { get; private set; }
        public DepartmentSystem         Departments   { get; private set; }

        // ── Hierarchy distribution (WO-JOB-002) ──────────────────────────────────────────────
        public HierarchyDistributor     Hierarchy     { get; private set; }

        // ── Interaction system (WO-NPC-014) ───────────────────────────────────────────────────
        public InteractionSystem        Interactions  { get; private set; }

        // ── Tick scheduler (WO-SYS-001) ───────────────────────────────────────────────────────
        public TickScheduler            Scheduler     { get; private set; }
        public LegacyTickSubscriber     LegacyTick    { get; private set; }

        // ── Economy system ─────────────────────────────────────────────────────────────────────
        public EconomySystem            Economy       { get; private set; }

        // ── Fleet management system (EXP-003) ─────────────────────────────────────────────────
        public ShipSystem               Fleet         { get; private set; }

        // ── Crafting system (EXP-005) ──────────────────────────────────────────────────────────
        public CraftingSystem           Crafting      { get; private set; }

        // ── Runtime state ─────────────────────────────────────────────────────
        public StationState Station  { get; private set; }
        public bool         IsLoaded { get; private set; }

        private bool _isPaused = true;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused == value) return;
                _isPaused = value;
                Time.timeScale = value ? 0f : 1f;
            }
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<StationState>    OnTick;
        public event Action<PendingEvent>    OnNewEvent;
        public event Action<string>          OnLogMessage;
        public event Action                  OnGameLoaded;

        /// <summary>
        /// Fired when an NPC announces intent to depart. Separated from OnNewEvent
        /// so the UI can show a non-pausing departure warning instead of the
        /// standard event decision modal.  Payload: (npc, interventionDeadlineTick).
        /// </summary>
        public event Action<NPCInstance, int> OnDepartureWarning;

        private float _tickTimer;
        private List<PendingEvent> _pendingEventsBuffer = new List<PendingEvent>();

        /// <summary>
        /// Tracks the UIDs of crew NPCs that were alive at the start of each tick.
        /// Used to detect newly-dead crew this tick and fire witness events.
        /// </summary>
        private readonly HashSet<string> _livingCrewIds = new HashSet<string>();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Load persisted settings from PlayerPrefs.
            if (PlayerPrefs.HasKey("autosave_interval_ticks"))
            {
                int savedInterval = PlayerPrefs.GetInt("autosave_interval_ticks");
                autosaveIntervalTicks = Mathf.Max(0, savedInterval);
            }

            Registry = ContentRegistry.Instance ?? FindAnyObjectByType<ContentRegistry>();
            if (Registry == null)
            {
                var go = new GameObject("ContentRegistry");
                Registry = go.AddComponent<ContentRegistry>();
                DontDestroyOnLoad(go);
            }
            StartCoroutine(Bootstrap());
        }

        private void Update()
        {
            if (!IsLoaded || IsPaused) return;
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= secondsPerTick)
            {
                _tickTimer -= secondsPerTick;
                AdvanceTick();
            }
        }

        // ── Bootstrap ────────────────────────────────────────────────────────

        private IEnumerator Bootstrap()
        {
            yield return StartCoroutine(Registry.LoadCoreAsync());
            InitSystems();
            IsLoaded = true;
            OnGameLoaded?.Invoke();
        }

        private void InitSystems()
        {
            Resources = new ResourceSystem(Registry);
            Npcs      = new NPCSystem(Registry);
            Jobs      = new JobSystem(Registry);
            Factions  = new FactionSystem(Registry);
            Combat    = new CombatSystem();
            Trade     = new TradeSystem(Registry);
            Events    = new EventSystem(Registry, difficulty);
            Inventory = new InventorySystem(Registry);
            TaskQueue  = new NPCTaskQueueManager();
            Visitors  = new VisitorSystem(Registry, Npcs, Events, Trade, TaskQueue, Inventory);
            Building  = new BuildingSystem(Registry);
            Comms     = new CommsSystem();
            Networks  = new NetworkSystem(Registry);
            UtilityNetworks = new UtilityNetworkManager(Registry, Networks);
            Missions  = new MissionSystem(Registry);
            Rooms     = new RoomSystem(Registry);
            Research  = new ResearchSystem(Registry);
            Map       = new MapSystem();
            AsteroidMissions = new AsteroidMissionSystem();

            // Visitor pipeline systems
            Antenna    = new AntennaSystem(Registry);
            ShipVisits = new ShipVisitStateMachine(Registry, Npcs, secondsPerTick);
            CommSystem = new CommunicationsSystem(Registry, TaskQueue, ShipVisits);

            // Farming / climate systems
            Farming   = new FarmingSystem(Registry);
            Temperature = new TemperatureSystem(Registry);

            // Mood & social systems
            Mood          = new MoodSystem();
            Relationships = new RelationshipRegistry();
            Conversations = new ConversationSystem();
            Proximity     = new ProximitySystem();

            // Wire MoodSystem into ResourceSystem so deprivation penalties use PushModifier
            // (deduped by eventId+source) instead of stacking direct moodScore deltas.
            Resources.SetMoodSystem(Mood);

            // Skill & Expertise system
            Skills = new SkillSystem(Registry);
            Skills.SetMoodSystem(Mood);
            // Register capability unlocks from all loaded ExpertiseDefinitions.
            Skills.RegisterAllCapabilities();
            // Wire skill system into systems that need to award XP.
            Research.SetSkillSystem(Skills);
            Research.SetSecondsPerTick(secondsPerTick);
            Farming.SetSkillSystem(Skills);
            Farming.SetMoodSystem(Mood);
            Conversations.SetSkillSystem(Skills);

            // Mentoring system — tracks co-working, forms Mentor/Student bonds, applies XP multiplier.
            Mentoring = new MentoringSystem();
            Skills.SetMentoringSystem(Mentoring);

            // Trait, Tension, Faction Government, Region systems
            Traits = new TraitSystem();
            Traits.SetMoodSystem(Mood);
            foreach (var kv in Registry.Traits)      Traits.RegisterTrait(kv.Value);
            foreach (var kv in Registry.TraitPools)  Traits.RegisterPool(kv.Value);
            foreach (var kv in Registry.TraitLineages) Traits.RegisterTraitLineage(kv.Value);

            // Need & Sanity systems
            Needs  = new NeedSystem();
            Sanity = new SanitySystem();
            Needs.SetMoodSystem(Mood);
            Needs.SetSanitySystem(Sanity);
            Needs.SetTraitSystem(Traits);

            Tension    = new TensionSystem(Traits);
            Tension.SetMoodSystem(Mood);
            Tension.SetSkillSystem(Skills);
            // Apply balance-data values loaded by ContentRegistry
            if (Registry != null && Registry.Balance != null)
            {
                Tension.InterventionWindowTicks  = Registry.Balance.interventionWindowTicks;
                Tension.InterventionSkillCheckDC = Registry.Balance.interventionSkillCheckDC;
                Research.SetPointsPerNpcPerTick(Registry.Balance.researchPointsPerNpcPerTick);
            }
            FactionGov = new FactionGovernmentSystem(Traits);
            Regions    = new RegionSystem();

            // Wire FactionSystem with procedural generation dependencies
            Factions.SetSystems(Npcs, Traits);

            // Horizon Simulation — full implementations replacing stubs
            var factionHistory = new FactionHistory();
            var horizonSim     = new HorizonSimulation(Regions.Registry, factionHistory);
            FactionHistory     = factionHistory;
            RegionSim          = horizonSim;
            Regions.Simulation = horizonSim;

            // Wire tension stage change events to station log
            Tension.OnTensionStageChanged += (npc, stage) =>
            {
                if (stage != TensionStage.Normal)
                    Station?.LogEvent($"{npc.name}: tension stage → {TensionSystem.GetTensionStageLabel(stage)}");
            };

            // Wire departure events: surface player alert and log departure
            Tension.OnDepartureAnnounced += (npc, deadline) =>
            {
                // Log to event buffer for the event log strip, but fire through
                // OnDepartureWarning instead of OnNewEvent so the UI shows a
                // non-pausing departure warning panel (UI-030).
                var alert = MakeDepartureAlertEvent(npc, deadline);
                var category = alert.definition.hostile ? LogCategory.Alert : LogCategory.World;
                var tickFired = Station?.tick ?? 0;
                EventLogBuffer.Instance?.Add(category, alert.definition.description ?? alert.definition.id, tickFired);
                OnDepartureWarning?.Invoke(npc, deadline);
            };
            Tension.OnNpcDeparted += npc =>
            {
                Station?.LogEvent($"{npc.name} has left the station and will not return.");
            };

            // Wire sleep/wake events from NPCSystem → MoodSystem
            // Note: sleep transitions are now driven by NeedSystem; these hooks are kept
            // for any future systems that still subscribe to OnNPCSleeps/OnNPCWakes.
            Npcs.OnNPCSleeps += npc => Mood.OnNPCSleeps(npc);
            Npcs.OnNPCWakes  += npc => Mood.OnNPCWakes(npc);

            // Log crisis events to the station log (requires station to be non-null,
            // so we use a lambda that captures Station at fire time)
            Mood.OnNpcEnteredCrisis       += npc => Station?.LogEvent(
                $"{npc.name} is in crisis and has abandoned their duties.");
            Mood.OnNpcRecoveredFromCrisis += npc => Station?.LogEvent(
                $"{npc.name} has recovered from crisis and returned to work.");

            // ── Reactive trigger wiring ───────────────────────────────────────
            // Each source system fires a named trigger on the EventSystem so data-authored
            // events can respond to these conditions without polling every tick.

            // Mood crisis entry/exit
            Mood.OnNpcEnteredCrisis       += npc =>
            {
                if (Station != null)
                    Events.FireReactiveTrigger("mood_crisis_entry", Station,
                        new Dictionary<string, object> { { "npc_uid", npc.uid } });
            };
            Mood.OnNpcRecoveredFromCrisis += npc =>
            {
                if (Station != null)
                    Events.FireReactiveTrigger("mood_crisis_exit", Station,
                        new Dictionary<string, object> { { "npc_uid", npc.uid } });
            };

            // Tension stage escalation (only fire on escalation, not on recovery to Normal)
            Tension.OnTensionStageChanged += (npc, stage) =>
            {
                if (stage != TensionStage.Normal && Station != null)
                    Events.FireReactiveTrigger("tension_stage_escalated", Station,
                        new Dictionary<string, object> { { "npc_uid", npc.uid }, { "tension_stage", stage.ToString() } });
            };

            // Faction reputation threshold crossings
            Factions.OnFactionRepThresholdCrossed += (factionId, oldRep, newRep) =>
            {
                if (Station != null)
                    Events.FireReactiveTrigger("faction_rep_threshold_crossed", Station,
                        new Dictionary<string, object> { { "faction_id", factionId }, { "old_rep", oldRep }, { "new_rep", newRep } });
            };

            // Government change (static event on FactionGovernmentSystem)
            FactionGovernmentSystem.OnGovernmentShifted += (factionId, oldGov, newGov) =>
            {
                if (Station != null)
                    Events.FireReactiveTrigger("government_changed", Station,
                        new Dictionary<string, object> { { "faction_id", factionId }, { "old_government", oldGov.ToString() }, { "new_government", newGov.ToString() } });
            };

            // Relationship milestone (static event on RelationshipRegistry)
            RelationshipRegistry.OnRelationshipMilestoneReached += (uid1, uid2, relType) =>
            {
                if (Station != null)
                    Events.FireReactiveTrigger("relationship_milestone_reached", Station,
                        new Dictionary<string, object> { { "npc_uid1", uid1 }, { "npc_uid2", uid2 }, { "relationship_type", relType.ToString() } });
            };

            // Log skill level-up and slot-earned notifications
            Skills.OnCharacterLevelUp += (npc, level) => Station?.LogEvent(
                $"{npc.name} reached character level {level}.");
            Skills.OnSlotEarned += (npc, skillId, skillLevel) =>
            {
                string skillName = Registry.Skills.TryGetValue(skillId, out var sdef)
                    ? sdef.displayName : skillId;
                Station?.LogEvent(
                    $"{npc.name}'s {skillName} reached level {skillLevel}. A new expertise slot is available.");
            };

            // Register external effect handlers on the event system
            Events.RegisterEffectHandler("resolve_boarding", HandleResolveBoardingEffect);
            Events.RegisterEffectHandler("spawn_npc",        HandleSpawnNpcEffect);

            // Medical system
            if (FeatureFlags.MedicalSystem)
            {
                Medical = new MedicalTickSystem();
                Medical.Initialise();
                Medical.SetMoodSystem(Mood);
                Medical.SetSanitySystem(Sanity);
                Medical.SetTraitSystem(Traits);

                Surgery = new SurgerySystem();
                Surgery.SetSanitySystem(Sanity);
                Surgery.SetTraitSystem(Traits);
            }

            // Death handling system
            if (FeatureFlags.NpcDeathHandling)
            {
                DeathHandling = new DeathHandlingSystem();
                DeathHandling.SetMoodSystem(Mood);
                DeathHandling.OnNpcDied += (npc, state) =>
                    Events.FireReactiveTrigger("npc_died", state,
                        new Dictionary<string, object> { { "npc_uid", npc.uid } });
            }

            // Wire CombatSystem optional dependencies (STA-003)
            Combat.SetBuildingSystem(Building);
            if (FeatureFlags.MedicalSystem)      Combat.SetMedicalSystem(Medical);
            if (FeatureFlags.NpcDeathHandling)   Combat.SetDeathHandlingSystem(DeathHandling);

            // Register additional combat scenario effect handlers (STA-003)
            Events.RegisterEffectHandler("resolve_raid",               HandleResolveRaidEffect);
            Events.RegisterEffectHandler("resolve_ship_to_station",    HandleResolveShipToStationEffect);
            Events.RegisterEffectHandler("resolve_station_to_station", HandleResolveStationToStationEffect);
            Events.RegisterEffectHandler("resolve_sabotage",           HandleResolveSabotageEffect);
            Events.RegisterEffectHandler("resolve_mental_break_combat",HandleResolveMentalBreakCombatEffect);

            // Wire ResourceSystem depletion reactive trigger
            Resources.OnResourceDepleted += resourceId =>
            {
                if (Station != null)
                    Events.FireReactiveTrigger("resource_depleted", Station,
                        new Dictionary<string, object> { { "resource_id", resourceId } });
            };

            // Counselling system (NPC-003)
            if (FeatureFlags.NpcCounselling)
            {
                Counselling = new CounsellingSystem();
                Counselling.SetTraitSystem(Traits);
                Counselling.SetSkillSystem(Skills);
            }

            // Department system
            DeptRegistry = new DepartmentRegistry();
            Departments  = new DepartmentSystem();

            // Hierarchy distribution (WO-JOB-002) — push-channel task distribution
            Hierarchy = new HierarchyDistributor(Registry, DeptRegistry);

            // Wire DepartmentRegistry colour-change events → DepartmentSystem so that
            // the rendering layer (via Departments.OnNpcsNeedColourResolve) can re-apply
            // DeptColour shader bindings within the same tick as the colour change.
            DeptRegistry.OnDeptColourChanged += deptUid =>
                Departments.NotifyColourChanged(deptUid, Station);

            // Economy system
            Economy = new EconomySystem();

            // Fleet management system (EXP-003)
            if (FeatureFlags.FleetManagement)
                Fleet = new ShipSystem(Registry);

            // Crafting system (EXP-005)
            if (FeatureFlags.CraftingSystem)
            {
                Crafting = new CraftingSystem(Registry);
                Crafting.SetSkillSystem(Skills);
            }

            // Interaction system (WO-NPC-014) — replaces ConversationSystem outcomes
            if (FeatureFlags.UseInteractionSystem)
            {
                Interactions = new InteractionSystem();
                Interactions.SetDependencies(Traits, Mood, Factions, Skills);
                // Disable legacy ConversationSystem when InteractionSystem is active
                Conversations.Enabled = false;
            }

            // Full trait system axis data loading (WO-NPC-015)
            if (FeatureFlags.UseFullTraitSystem)
            {
                string traitDefsPath = System.IO.Path.Combine(Application.streamingAssetsPath, "data", "traits", "TraitDefinitions.json");
                string traitMatrixPath = System.IO.Path.Combine(Application.streamingAssetsPath, "data", "traits", "TraitCompatibilityMatrix.json");
                if (System.IO.File.Exists(traitDefsPath) && System.IO.File.Exists(traitMatrixPath))
                {
                    string defsJson = System.IO.File.ReadAllText(traitDefsPath);
                    string matrixJson = System.IO.File.ReadAllText(traitMatrixPath);
                    Traits.LoadAxisData(defsJson, matrixJson);
                }
            }

            // Tick scheduler (WO-SYS-001) — multi-channel load balancing
            if (FeatureFlags.UseTickScheduler)
            {
                Scheduler = new TickScheduler();
                string schedulerConfigPath = System.IO.Path.Combine(Application.streamingAssetsPath, "data", "scheduler", "SchedulerConfig.json");
                if (System.IO.File.Exists(schedulerConfigPath))
                    Scheduler.LoadConfig(System.IO.File.ReadAllText(schedulerConfigPath));
                LegacyTick = new LegacyTickSubscriber(Scheduler);
            }
        }

        // ── New game ─────────────────────────────────────────────────────────

        public void NewGame(string stationName, int? seed = null, ScenarioDefinition scenario = null)
        {
            // If a scenario specifies a layout seed, it takes precedence over the provided seed.
            int? effectiveSeed = (FeatureFlags.ScenarioSelection && scenario?.layoutSeed.HasValue == true)
                                 ? scenario.layoutSeed
                                 : seed;

            if (effectiveSeed.HasValue) UnityEngine.Random.InitState(effectiveSeed.Value);

            Station = new StationState(stationName);
            Station.solarSystem = SolarSystemGenerator.Generate(stationName, effectiveSeed);
            SetupStartingModules();

            ActiveScenarioId = (FeatureFlags.ScenarioSelection && scenario != null)
                ? scenario.id ?? string.Empty
                : string.Empty;

            if (FeatureFlags.ScenarioSelection && scenario != null)
            {
                SetupStartingCrewFromScenario(scenario);
                ApplyScenarioResources(scenario);
                ApplyScenarioShips(scenario);
                if (scenario.startingFactionDisposition != "standard")
                    Debug.LogWarning($"[GameManager] Scenario '{scenario.id}' requests non-standard faction disposition " +
                                     $"'{scenario.startingFactionDisposition}' — only 'standard' is currently supported. " +
                                     "Initializing with one friendly and one unfriendly faction.");
            }
            else
            {
                SetupStartingCrew();
            }

            SetupStartingPolicies();
            Factions.Initialize(Station);

            // Generate the galaxy sector map. Uses the same seed as solar system generation.
            int galaxySeed = effectiveSeed.HasValue ? effectiveSeed.Value : SolarSystemGenerator.StableHash(stationName);
            GalaxyGenerator.Generate(galaxySeed, Station);

            // Seed starting factions (always: one friendly, one unfriendly in adjacent sectors).
            var factionRng = new System.Random(galaxySeed ^ 0x5A5A5A5A);
            Factions.InitializeStartingFactions(Station, factionRng);

            // Initialise skill instances for all starting crew.
            Skills.InitialiseNpcSkills(Station);

            // Initialise DepartmentRegistry with the new station's department list.
            DeptRegistry.Init(Station.departments);

            // Reset threshold crossing state so warnings fire correctly from tick 1.
            Resources.ResetWarningState();

            string scenarioLabel = (FeatureFlags.ScenarioSelection && scenario != null)
                                   ? $" [{scenario.name}]"
                                   : string.Empty;
            Log($"Waystation '{stationName}' operational. All systems nominal.{scenarioLabel}");

            IsPaused = false;
            OnGameLoaded?.Invoke();
            // Seed the room bonus cache immediately so it's available on the first frame.
            Rooms.RebuildBonusCache(Station);
            // Rebuild utility networks so grid connectivity is available from tick 1.
            UtilityNetworks.RebuildAll(Station);
            Debug.Log($"[GameManager] New game started: {stationName}{scenarioLabel}");
        }

        private void SetupStartingModules()
        {
            // Station layout — four rooms connected by a 3-wide central hallway spine:
            //
            //  col: 0123456  789  0123456   (10-16)
            //
            //  row 16: [══ Room A 7×7 ═══]─[═══ Room B 7×7 ══]   ← single cap wall row
            //  row 15:  A right wall        |||        B left wall ← corridor floor row
            //  row 13:  A single door (6,13)|||  B single door (10,13)
            //  row 10: [═════════════════] ─── [══════════════════]
            //                            │ 3w │
            //                            │hall│
            //  row 6:  [═══ Room C 7×7 ═══] ─── [══ Room D 9×5 ══]
            //  row 3:   C single door (6,3) |||   D single door (10,2)
            //  row 1:  corridor floor row   |||   corridor floor row ← corridor floor row
            //  row 0:  [═════════════════] ─── [══════════════════════]  ← single cap wall row
            //           cols 0-6                cols 10-18
            //
            // Each room has exactly one center door connecting to the 3-wide corridor.
            // The corridor floor runs rows 1-15 (extended by one row at each end vs old 2-14).
            // Cap walls are a single row at row 0 and row 16, flush with room perimeters.

            // ── Room A (7×7) — top-left ───────────────────────────────────────
            PlaceRoom(0, 10, 7, 7, new HashSet<(int, int)>
            {
                (6, 13),   // single center door → hallway
            });

            // ── Room B (7×7) — top-right ──────────────────────────────────────
            PlaceRoom(10, 10, 7, 7, new HashSet<(int, int)>
            {
                (10, 13),  // single center door → hallway
            });

            // ── Room C (7×7) — bottom-left ────────────────────────────────────
            PlaceRoom(0, 0, 7, 7, new HashSet<(int, int)>
            {
                (6, 3),    // single center door → hallway
            });

            // ── Room D (9×5) — bottom-right (wider utility / bridge room) ─────
            PlaceRoom(10, 0, 9, 5, new HashSet<(int, int)>
            {
                (10, 2),   // single center door → hallway
            });

            // ── Central hallway spine (3 tiles wide, cols 7-9) ────────────────
            // Floor: rows 1-15 (one row wider at each end than before).
            // This keeps the corridor flush with the room perimeters — the single cap
            // walls at rows 0 and 16 align with the room bottom/top walls.
            for (int hc = 7; hc <= 9; hc++)
            for (int hr = 1; hr <= 15; hr++)
                PlaceBuilt("buildable.floor", hc, hr);

            // Top cap: single wall row at row 16, flush with Room A and B top walls.
            for (int hc = 7; hc <= 9; hc++)
                PlaceBuilt("buildable.wall", hc, 16);

            // Bottom cap: single wall row at row 0, flush with Room C and D bottom walls.
            for (int hc = 7; hc <= 9; hc++)
                PlaceBuilt("buildable.wall", hc, 0);

            // Left corridor side wall: rows 7-9 bridge the gap between Room C (top wall
            // row 6) and Room A (bottom wall row 10). Without these the corridor would
            // be open on the left between the two rooms.
            for (int hr = 7; hr <= 9; hr++)
                PlaceBuilt("buildable.wall", 6, hr);

            // Right corridor side wall: rows 5-9 bridge the gap between Room D (top wall
            // row 4) and Room B (bottom wall row 10).
            for (int hr = 5; hr <= 9; hr++)
                PlaceBuilt("buildable.wall", 10, hr);
        }

        /// <summary>
        /// Places a rectangular room: interior floor tiles and a perimeter of walls,
        /// substituting doors at any position listed in <paramref name="doorPositions"/>.
        /// originCol/originRow is the south-west corner of the room (inclusive).
        /// </summary>
        private void PlaceRoom(int originCol, int originRow, int width, int height,
                               HashSet<(int col, int row)> doorPositions)
        {
            // Interior floor
            for (int dx = 1; dx < width - 1; dx++)
            for (int dy = 1; dy < height - 1; dy++)
                PlaceBuilt("buildable.floor", originCol + dx, originRow + dy);

            // Bottom and top perimeter rows
            for (int dx = 0; dx < width; dx++)
            {
                PlaceWallOrDoor(originCol + dx, originRow,              doorPositions);
                PlaceWallOrDoor(originCol + dx, originRow + height - 1, doorPositions);
            }
            // Left and right perimeter columns (skip corners — handled above)
            for (int dy = 1; dy < height - 1; dy++)
            {
                PlaceWallOrDoor(originCol,             originRow + dy, doorPositions);
                PlaceWallOrDoor(originCol + width - 1, originRow + dy, doorPositions);
            }
        }

        private void PlaceWallOrDoor(int col, int row, HashSet<(int col, int row)> doorPositions)
        {
            string id = doorPositions.Contains((col, row)) ? "buildable.door" : "buildable.wall";
            PlaceBuilt(id, col, row);
        }

        private void PlaceBuilt(string buildableId, int col, int row, int rotation = 0)
        {
            if (!Registry.Buildables.TryGetValue(buildableId, out var defn))
            {
                Debug.LogError($"[GameManager] PlaceBuilt: unknown buildable '{buildableId}' — check core_buildables.json is loaded.");
                return;
            }
            var f = FoundationInstance.Create(buildableId, col, row,
                                              defn.maxHealth, defn.buildQuality,
                                              rotation, defn.cargoCapacity);
            f.status        = "complete";
            f.buildProgress = 1f;
            f.tileLayer     = defn.tileLayer;
            f.tileWidth     = defn.tileWidth;
            f.tileHeight    = defn.tileHeight;
            Station.foundations[f.uid] = f;
        }

        private void SetupStartingCrew()
        {
            // Pick 3 random crew from the available templates
            string[] crewPool = { "npc.engineer", "npc.security_officer", "npc.scientist",
                                  "npc.trader", "npc.engineer", "npc.security_officer" };
            // Fisher-Yates shuffle using Unity's seeded RNG
            for (int i = crewPool.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (crewPool[i], crewPool[j]) = (crewPool[j], crewPool[i]);
            }
            var seen = new HashSet<string>();
            int spawned = 0;
            foreach (var tmpl in crewPool)
            {
                if (spawned >= 3) break;
                if (!seen.Add(tmpl)) continue;
                if (!Registry.Npcs.ContainsKey(tmpl)) continue;
                var npc = Npcs.SpawnCrewMember(tmpl);
                if (npc != null) { Station.AddNpc(npc); spawned++; }
            }
        }

        /// <summary>
        /// Spawns crew defined by the scenario's <see cref="ScenarioDefinition.crewComposition"/> list.
        /// Each entry is an NPC template ID; duplicates are allowed (the scenario author controls count).
        /// Falls back to <see cref="SetupStartingCrew"/> when the composition list is empty.
        /// </summary>
        private void SetupStartingCrewFromScenario(ScenarioDefinition scenario)
        {
            if (scenario.crewComposition == null || scenario.crewComposition.Count == 0)
            {
                SetupStartingCrew();
                return;
            }
            foreach (var tmplId in scenario.crewComposition)
            {
                if (!Registry.Npcs.ContainsKey(tmplId))
                {
                    Debug.LogWarning($"[GameManager] Scenario '{scenario.id}' references unknown NPC template '{tmplId}' — skipped.");
                    continue;
                }
                var npc = Npcs.SpawnCrewMember(tmplId);
                if (npc != null) Station.AddNpc(npc);
            }
        }

        /// <summary>
        /// Overwrites the station's starting resources with values defined in the scenario.
        /// Only keys present in the scenario are overwritten; any resource not listed retains
        /// the StationState default value.
        /// </summary>
        private void ApplyScenarioResources(ScenarioDefinition scenario)
        {
            if (scenario.startingResources == null) return;
            foreach (var kv in scenario.startingResources)
                Station.resources[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Seeds the player's starting fleet from the scenario's <see cref="ScenarioDefinition.startingShips"/>
        /// list.  Each entry is a ship template ID.  Only runs when <see cref="FeatureFlags.FleetManagement"/>
        /// is enabled; skipped silently otherwise.
        /// </summary>
        private void ApplyScenarioShips(ScenarioDefinition scenario)
        {
            if (!FeatureFlags.FleetManagement) return;
            if (scenario.startingShips == null || scenario.startingShips.Count == 0) return;
            if (Fleet == null)
            {
                Debug.LogWarning("[GameManager] ApplyScenarioShips: FleetManagement is enabled but ShipSystem is null — starting ships not added.");
                return;
            }
            foreach (var templateId in scenario.startingShips)
            {
                if (!Registry.Ships.TryGetValue(templateId, out var tmpl))
                {
                    Debug.LogWarning($"[GameManager] Scenario '{scenario.id}' references unknown ship template '{templateId}' — skipped.");
                    continue;
                }
                Fleet.AddShipToFleet(templateId, tmpl.role, Station);
            }
        }

        private void SetupStartingPolicies()
        {
            Station.policy["visitor_policy"] = "open";
            Station.policy["refugee_policy"] = "accept";
            Station.policy["trade_stance"]   = "active";
            Station.policy["security_level"] = "standard";
            ApplyPolicyEffects("trade_stance",   "active",   "");
            ApplyPolicyEffects("security_level", "standard", "");
        }

        // ── Main tick ────────────────────────────────────────────────────────

        public void AdvanceTick()
        {
            if (Station == null) return;

            Station.tick++;

            // Snapshot living crew UIDs before any systems run this tick so we can detect
            // newly-dead NPCs after all damage/medical/surgery systems have processed.
            _livingCrewIds.Clear();
            foreach (var npc in Station.npcs.Values)
                if (npc.IsCrew() && !npc.statusTags.Contains("dead"))
                    _livingCrewIds.Add(npc.uid);

            // Tick all systems in deterministic order
            Resources.Tick(Station);
            Npcs.Tick(Station);
            // Counselling system: assign sessions before Jobs.Tick so the idle-counsellor
            // filter (currentJobId == null) works correctly — counsellors get a session
            // before JobSystem assigns them a fallback wander task.
            if (FeatureFlags.NpcCounselling)
                Counselling?.Tick(Station);
            // Push-channel distribution: populate personal task queues before JobSystem
            // picks jobs so NPCs can consume queued tasks within the same tick.
            Hierarchy?.Tick(Station);
            Jobs.Tick(Station);
            Factions.Tick(Station);
            Inventory.Tick(Station);
            Visitors.Tick(Station);
            Building.Tick(Station);
            // If a network-capable foundation just completed, rebuild utility networks
            // so the new tile joins its network before the simulation tick runs.
            if (Building.NetworkRebuildNeeded)
            {
                UtilityNetworks.RebuildAll(Station);
                Building.ClearNetworkRebuildFlag();
            }
            // If a workbench or structural foundation completed, rebuild room bonus
            // cache within the same tick so bonuses are current for Research/Building.
            // Track whether a forced rebuild occurred so the interval-based Rooms.Tick()
            // is skipped this tick to avoid scanning all foundations twice.
            bool roomRebuildDoneThisTick = Building.RoomRebuildNeeded;
            if (roomRebuildDoneThisTick)
            {
                Rooms.RebuildBonusCache(Station);
                Building.ClearRoomRebuildFlag();
            }
            Comms.Tick(Station);
            Missions.Tick(Station);
            // Fleet management: resolve completed fleet missions.
            // Runs after MissionSystem so NPC missionUid state is up to date.
            if (FeatureFlags.FleetManagement)
                Fleet?.Tick(Station);
            // Only run the interval tick when no forced rebuild already occurred this tick.
            if (!roomRebuildDoneThisTick) Rooms.Tick(Station);
            Research.Tick(Station);
            Map.TickExplorationState(Station);
            Map.Tick(Station);
            AsteroidMissions.Tick(Station);
            // Crafting: runs after Research so research unlock tags are current when gating recipes.
            if (FeatureFlags.CraftingSystem)
                Crafting?.Tick(Station);
            UtilityNetworks.Tick(Station);
            Departments.Tick(Station, AsteroidMissions);

            // Visitor pipeline (antenna detection → state machine → comms tasks)
            Antenna.Tick(Station);
            ShipVisits.Tick(Station);
            CommSystem.Tick(Station);

            // Economy system: docking fees and faction contract payments.
            // Runs after Visitors.Tick so newly-docked ships are already in the docked list.
            Economy.Tick(Station);

            // Farming / climate (temperature before farming so planter temps are fresh)
            Temperature.Tick(Station);
            Farming.Tick(Station);

            // Mood & social systems (run after job assignment so crisis is set before
            // next job tick, and after NPC needs so sleep state is current)
            Needs.Tick(Station);

            // Medical tick runs before Mood so that pain/blood/disease mood modifiers are
            // included in the current-tick mood score and therefore in Sanity's daily accumulator.
            if (FeatureFlags.MedicalSystem)
                Medical?.Tick(Station);

            // Death witness events: detect crew that died this tick and apply WitnessDeath
            // condition pressure to all surviving crew so the trauma trait system can fire.
            if (FeatureFlags.NpcTraits)
            {
                foreach (var npc in Station.npcs.Values)
                {
                    if (_livingCrewIds.Contains(npc.uid) && npc.statusTags.Contains("dead"))
                        Traits.NotifyCrewDeath(npc, Station);
                }
            }

            // Death handling: spawn bodies, grief modifiers, haul task assignment.
            // Runs in the same detection window as trait death events so both systems
            // observe the same set of newly-dead crew this tick.
            if (FeatureFlags.NpcDeathHandling)
            {
                foreach (var npc in Station.npcs.Values)
                {
                    if (_livingCrewIds.Contains(npc.uid) && npc.statusTags.Contains("dead"))
                        DeathHandling?.OnNPCDied(npc, Station);
                }
                DeathHandling?.Tick(Station);
            }

            Mood.Tick(Station);
            Sanity.Tick(Station);

            Proximity.Tick(Station, Mood, Relationships);
            Conversations.Tick(Station, Mood, Relationships);
            // Interaction system: five-input quality conversations (WO-NPC-014)
            if (FeatureFlags.UseInteractionSystem)
                Interactions?.Tick(Station);
            Relationships.Tick(Station, Mood);

            // Skill system
            Skills.Tick(Station);
            Mentoring.Tick(Station);

            // Trait, Tension, Faction Government, Region systems (after mood so moodScore is current)
            if (FeatureFlags.NpcTraits)
            {
                // Register mood-based condition pressure for all crew NPCs
                if (Station.tick % TimeSystem.TicksPerDay == 0)
                {
                    foreach (var npc in Station.npcs.Values)
                    {
                        if (!npc.IsCrew()) continue;
                        if (npc.moodScore < MoodSystem.CrisisThreshold)
                        {
                            Traits.RegisterConditionPressure(npc, TraitConditionCategory.LowMood, 2f);
                            // 12-axis: low mood pushes Disposition toward pessimistic (-)
                            if (FeatureFlags.UseFullTraitSystem)
                                Traits.AddPressure(npc, "disposition", -2f);
                        }
                        else if (npc.moodScore >= MoodSystem.ThrivingThreshold)
                        {
                            Traits.RegisterConditionPressure(npc, TraitConditionCategory.HighMood, 1f);
                            // 12-axis: high mood pushes Disposition toward optimistic (+)
                            if (FeatureFlags.UseFullTraitSystem)
                                Traits.AddPressure(npc, "disposition", 1f);
                        }
                    }
                }
                Traits.Tick(Station);
                Tension.Tick(Station);
            }
            if (FeatureFlags.FactionGovernment)
                FactionGov.Tick(Station, Factions.GetAllFactions(Station));
            if (FeatureFlags.RegionSimulation)
                Regions.Tick(Station);

            // Process events
            var newEvents = Events.Tick(Station);
            foreach (var ev in newEvents)
            {
                OnNewEvent?.Invoke(ev);
                if (ev.definition.hostile) IsPaused = true;
            }

            OnTick?.Invoke(Station);

            // Tick scheduler: execute budget-scheduled systems (WO-SYS-001)
            if (FeatureFlags.UseTickScheduler)
                Scheduler?.Tick(Station.tick);

            // Autosave: non-blocking, fires at the configured interval (0 = disabled).
            if (FeatureFlags.FullSaveLoad && autosaveIntervalTicks > 0
                && Station.tick % autosaveIntervalTicks == 0
                && !_autosaveInProgress)
            {
                StartCoroutine(AutosaveCoroutine());
            }
        }

        // ── Player actions ────────────────────────────────────────────────────

        public bool AdmitShip(string shipUid)  => Visitors.AdmitShip(shipUid, Station);
        public void DenyShip(string shipUid)   => Visitors.DenyShip(shipUid, Station);
        public void ResolveEventChoice(PendingEvent pending, string choiceId)
            => Events.ResolveChoice(pending, choiceId, Station);

        /// <summary>
        /// Attempt to hail a ship via the Communications Menu Call button.
        /// Returns a message to display in the UI (success, failure reason, or cooldown).
        /// </summary>
        public string TryHailShip(string shipUid) => CommSystem.TryHailShip(shipUid, Station);

        /// <summary>
        /// Attempt a player intervention to persuade a DepartureRisk NPC to stay.
        /// Uses the supplied skill for the check roll.
        /// Returns (success, message) — on success tension resets to Disgruntled and
        /// the departure announcement is cancelled.
        /// </summary>
        public (bool ok, string msg) AttemptDepartureIntervention(string npcUid, string skillId = "skill.persuasion")
        {
            if (!FeatureFlags.NpcDeparture)
                return (false, "Departure system is disabled.");
            if (!Station.npcs.TryGetValue(npcUid, out var npc))
                return (false, "NPC not found.");
            if (npc.traitProfile?.departure == null || !npc.traitProfile.departure.announced)
                return (false, $"{npc.name} has no pending departure announcement.");

            bool success = Tension.AttemptIntervention(npc, skillId, Station);
            return success
                ? (true,  $"Intervention succeeded — {npc.name} has agreed to stay.")
                : (false, $"Intervention failed — {npc.name} remains determined to leave.");
        }

        public (bool ok, string msg) RecruitVisitor(string npcUid)
        {
            if (!Station.npcs.TryGetValue(npcUid, out var npc))
                return (false, "NPC not found.");
            if (!npc.IsVisitor())
                return (false, $"{npc.name} is not a visitor.");
            if (Station.GetResource("credits") < RecruitCost)
                return (false, $"Insufficient credits (need {RecruitCost:F0}).");

            Station.ModifyResource("credits", -RecruitCost);
            npc.statusTags.Remove("visitor");
            npc.statusTags.Add("crew");
            // Initialise skill instances for the newly recruited crew member.
            Skills?.InitialiseNpcSkills(npc);
            Log($"{npc.name} recruited as crew ({RecruitCost:F0} credits).");
            return (true, $"{npc.name} is now crew.");
        }

        public (bool ok, string msg) RepairModule(string moduleUid)
        {
            if (!Station.modules.TryGetValue(moduleUid, out var module))
                return (false, "Module not found.");
            if (module.damage <= 0f)
                return (false, $"{module.displayName} is not damaged.");
            if (Station.GetResource("parts") < RepairPartsCost)
                return (false, $"Not enough parts (need {RepairPartsCost:F0}).");

            Station.ModifyResource("parts", -RepairPartsCost);
            float oldDamage = module.damage;
            module.damage   = Mathf.Max(0f, module.damage - RepairDamageAmount);
            if (module.damage == 0f && !module.active) module.active = true;
            Log($"Repair: {module.displayName} {oldDamage:P0} → {module.damage:P0}.");
            return (true, $"Repaired {module.displayName}.");
        }

        public void SetPolicy(string key, string value)
        {
            string old = Station.policy.ContainsKey(key) ? Station.policy[key] : "";
            Station.policy[key] = value;
            ApplyPolicyEffects(key, value, old);
            Log($"Policy '{key}' changed: {old} → {value}.");
        }

        // Logs a message to the station log and fires OnLogMessage for listeners.
        private void Log(string msg)
        {
            Station.LogEvent(msg);
            OnLogMessage?.Invoke(msg);
        }

        /// <summary>
        /// Builds a synthetic PendingEvent to surface a departure announcement
        /// as a player alert (same path as EventSystem events in the UI).
        /// </summary>
        private static PendingEvent MakeDepartureAlertEvent(NPCInstance npc, int interventionDeadlineTick)
        {
            var def = new EventDefinition
            {
                id          = $"crew_departure_alert_{npc.uid}",
                category    = "crew",
                title       = $"⚠ {npc.name} Plans to Leave",
                description = $"{npc.name} has announced intent to depart the station. " +
                              $"You have until tick {interventionDeadlineTick} to intervene.",
                weight      = 0,
                hostile     = false,
            };
            var pending = new PendingEvent
            {
                definition  = def,
                expiresAt   = interventionDeadlineTick,
            };
            pending.context["npc_uid"] = npc.uid;
            return pending;
        }

        private void ApplyPolicyEffects(string key, string value, string oldValue)
        {
            if (key == "trade_stance")
            {
                Station.ClearTag("active_trading");
                if (value == "active") Station.SetTag("active_trading");
            }
            else if (key == "security_level")
            {
                Station.ClearTag("station_guarded");
                if (value == "high") Station.SetTag("station_guarded");
            }
            else if (key == "visitor_policy")
            {
                Station.ClearTag("inspection_in_progress");
                if (value == "inspect") Station.SetTag("inspection_in_progress");
            }
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        /// <summary>Returns true if a save file exists and is non-empty.</summary>
        public bool HasSaveFile()
        {
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            return File.Exists(path) && new FileInfo(path).Length > 10;
        }

        /// <summary>
        /// Saves all runtime state to disk.  When FeatureFlags.FullSaveLoad is true,
        /// all NPC/foundation/ship/mission/etc. state is serialised.  When false, only
        /// the legacy partial state (resources, tags, research, galaxy) is saved.
        /// </summary>
        public void SaveGame()
        {
            if (Station == null) return;
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            try
            {
                var data = BuildSaveData();
                string json = MiniJSON.Json.Serialize(data);
                File.WriteAllText(path, json);
                Log("Game saved.");
                Debug.Log($"[GameManager] Saved to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameManager] SaveGame failed: {ex}");
            }
        }

        private IEnumerator AutosaveCoroutine()
        {
            _autosaveInProgress = true;
            Dictionary<string, object> data = null;
            string json = null;
            try
            {
                data = BuildSaveData();
                json = MiniJSON.Json.Serialize(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameManager] Autosave serialise failed: {ex.Message}");
                _autosaveInProgress = false;
                yield break;
            }

            string path = SlotFilePath(AutosaveSlotIndex);
            var writeTask = Task.Run(() => File.WriteAllText(path, json));
            yield return new WaitUntil(() => writeTask.IsCompleted);

            if (writeTask.IsFaulted)
                Debug.LogWarning($"[GameManager] Autosave write failed: {writeTask.Exception?.InnerException?.Message}");
            else
                Debug.Log($"[GameManager] Autosaved at tick {Station?.tick}.");

            _autosaveInProgress = false;
        }

        /// <summary>
        /// Loads the save file and restores all runtime state.
        /// Fires <see cref="OnLoadError"/> with a player-facing message on any failure.
        /// </summary>
        public void LoadGame()
        {
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            LastLoadError = string.Empty;

            if (!File.Exists(path)) { RaiseLoadError("No save file found."); return; }

            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception ex) { RaiseLoadError($"Could not read save file: {ex.Message}"); return; }

            if (string.IsNullOrWhiteSpace(json)) { RaiseLoadError("Save file is empty."); return; }

            Dictionary<string, object> data;
            try { data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>; }
            catch (Exception ex) { RaiseLoadError($"Save file could not be parsed: {ex.Message}"); return; }

            if (data == null) { RaiseLoadError("Save file is corrupt or in an unexpected format."); return; }

            int saveVersion = data.TryGetValue("version", out var vv) ? Convert.ToInt32(vv) : 0;
            if (saveVersion > SaveVersion)
                Debug.LogWarning($"[GameManager] Save version {saveVersion} is newer than supported ({SaveVersion}). Some state may not restore correctly.");

            bool isFullSave = data.TryGetValue("full_save", out var fsv) && Convert.ToBoolean(fsv);
            string stationName = data.TryGetValue("station_name", out var sn) ? sn?.ToString() : "Waystation";

            try
            {
                ApplySaveData(data, isFullSave);
            }
            catch (Exception ex)
            {
                RaiseLoadError($"Save file could not be restored: {ex.Message}");
                Debug.LogError($"[GameManager] LoadGame failed: {ex}");
                return;
            }

            Log($"Save loaded: '{stationName}' at tick {Station.tick}.");
            IsPaused = false;
            OnGameLoaded?.Invoke();
            Debug.Log($"[GameManager] Save loaded from {path}");
        }

        private void RaiseLoadError(string message)
        {
            LastLoadError = message;
            Debug.LogWarning($"[GameManager] LoadGame: {message}");
            OnLoadError?.Invoke(message);
        }

        // ── Slot-based save / load ────────────────────────────────────────────

        /// <summary>
        /// Returns the save-file path for the given slot index.
        /// Slot 0 is the autosave file; slots 1–<see cref="SaveSlotCount"/> are manual save slots.
        /// </summary>
        private string SlotFilePath(int slotIndex)
        {
            string filename = slotIndex == AutosaveSlotIndex
                ? "waystation_autosave.json"
                : $"waystation_save_slot{slotIndex}.json";
            return Path.Combine(Application.persistentDataPath, filename);
        }

        /// <summary>
        /// Saves the current game state to the specified manual save slot (1–<see cref="SaveSlotCount"/>).
        /// Slot 0 (autosave) cannot be written by this method; use the autosave coroutine instead.
        /// </summary>
        public void SaveGame(int slotIndex)
        {
            if (slotIndex <= AutosaveSlotIndex || slotIndex > SaveSlotCount)
            {
                Debug.LogWarning($"[GameManager] SaveGame(slot): invalid slot index {slotIndex}. Use 1–{SaveSlotCount}.");
                return;
            }
            if (Station == null) return;
            string path = SlotFilePath(slotIndex);
            try
            {
                var data = BuildSaveData();
                string json = MiniJSON.Json.Serialize(data);
                File.WriteAllText(path, json);
                Log($"Game saved to slot {slotIndex}.");
                Debug.Log($"[GameManager] Saved slot {slotIndex} to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameManager] SaveGame(slot {slotIndex}) failed: {ex}");
            }
        }

        /// <summary>
        /// Loads the game state from the specified save slot.
        /// Slot 0 loads the autosave; slots 1–<see cref="SaveSlotCount"/> load manual saves.
        /// Fires <see cref="OnLoadError"/> on failure.
        /// </summary>
        public void LoadGame(int slotIndex)
        {
            if (slotIndex < AutosaveSlotIndex || slotIndex > SaveSlotCount)
            {
                RaiseLoadError($"Invalid save slot index {slotIndex}.");
                return;
            }

            string path = SlotFilePath(slotIndex);
            LastLoadError = string.Empty;

            if (!File.Exists(path)) { RaiseLoadError($"No save file found in slot {slotIndex}."); return; }

            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception ex) { RaiseLoadError($"Could not read save slot {slotIndex}: {ex.Message}"); return; }

            if (string.IsNullOrWhiteSpace(json)) { RaiseLoadError($"Save slot {slotIndex} is empty."); return; }

            Dictionary<string, object> data;
            try { data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>; }
            catch (Exception ex) { RaiseLoadError($"Save slot {slotIndex} could not be parsed: {ex.Message}"); return; }

            if (data == null) { RaiseLoadError($"Save slot {slotIndex} is corrupt or in an unexpected format."); return; }

            int saveVersion = data.TryGetValue("version", out var vv) ? Convert.ToInt32(vv) : 0;
            if (saveVersion > SaveVersion)
                Debug.LogWarning($"[GameManager] Slot {slotIndex} save version {saveVersion} is newer than supported ({SaveVersion}).");

            bool isFullSave = data.TryGetValue("full_save", out var fsv) && Convert.ToBoolean(fsv);
            string stationName = data.TryGetValue("station_name", out var sn) ? sn?.ToString() : "Waystation";

            try
            {
                ApplySaveData(data, isFullSave);
            }
            catch (Exception ex)
            {
                RaiseLoadError($"Save slot {slotIndex} could not be restored: {ex.Message}");
                Debug.LogError($"[GameManager] LoadGame(slot {slotIndex}) failed: {ex}");
                return;
            }

            Log($"Save loaded from slot {slotIndex}: '{stationName}' at tick {Station.tick}.");
            IsPaused = false;
            OnGameLoaded?.Invoke();
            Debug.Log($"[GameManager] Loaded slot {slotIndex} from {path}");
        }

        /// <summary>
        /// Deletes the save file for the given manual save slot (1–<see cref="SaveSlotCount"/>).
        /// Slot 0 (autosave) cannot be deleted via this method.
        /// </summary>
        public void DeleteSaveSlot(int slotIndex)
        {
            if (slotIndex <= AutosaveSlotIndex || slotIndex > SaveSlotCount)
            {
                Debug.LogWarning($"[GameManager] DeleteSaveSlot: invalid slot index {slotIndex}.");
                return;
            }
            string path = SlotFilePath(slotIndex);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[GameManager] Deleted save slot {slotIndex}.");
            }
        }

        /// <summary>
        /// Returns metadata for the specified save slot, or null if the slot is empty.
        /// Slot 0 returns autosave metadata; slots 1–<see cref="SaveSlotCount"/> return manual save metadata.
        /// </summary>
        public SaveSlotInfo GetSaveSlotInfo(int slotIndex)
        {
            if (slotIndex < AutosaveSlotIndex || slotIndex > SaveSlotCount) return null;
            string path = SlotFilePath(slotIndex);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var data = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
                if (data == null) return null;

                string stationName = data.TryGetValue("station_name", out var sn) ? sn?.ToString() : "Waystation";
                int tick = data.TryGetValue("tick", out var t) ? Convert.ToInt32(t) : 0;
                string savedAt = data.TryGetValue("saved_at", out var sa) ? sa?.ToString() : "";
                long savedAtTicks = 0;
                if (!string.IsNullOrEmpty(savedAt))
                    long.TryParse(savedAt, out savedAtTicks);

                return new SaveSlotInfo
                {
                    slotIndex    = slotIndex,
                    stationName  = stationName,
                    tick         = tick,
                    savedAt      = savedAtTicks > 0 ? new DateTime(savedAtTicks, DateTimeKind.Utc) : (DateTime?)null,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sets and persists the autosave interval.
        /// Pass 0 to disable autosave entirely.
        /// </summary>
        public void SetAutosaveInterval(int ticks)
        {
            autosaveIntervalTicks = Mathf.Max(0, ticks);
            PlayerPrefs.SetInt("autosave_interval_ticks", autosaveIntervalTicks);
            PlayerPrefs.Save();
        }

        /// <summary>Current autosave interval in ticks (0 = disabled).</summary>
        public int AutosaveIntervalTicks => autosaveIntervalTicks;

        // ── Effect handlers (called by EventSystem) ───────────────────────────

        private void HandleResolveBoardingEffect(OutcomeEffect effect, StationState station,
                                                   Dictionary<string, object> context)
        {
            string shipUid = context.ContainsKey("ship_uid") ? context["ship_uid"].ToString() : "";
            if (!station.ships.TryGetValue(shipUid, out var ship)) return;
            var outcome = Combat.ResolveBoardingAttempt(station, ship);
            station.LogEvent(outcome.narrative);
            station.LogEvent($"Combat outcome: {outcome.tier}");
        }

        private void HandleSpawnNpcEffect(OutcomeEffect effect, StationState station,
                                           Dictionary<string, object> context)
        {
            string templateId = effect.target;
            if (string.IsNullOrEmpty(templateId)) return;
            var npc = Npcs.SpawnVisitor(templateId);
            if (npc != null) station.AddNpc(npc);
        }

        // ── Additional combat scenario effect handlers (STA-003) ──────────────

        private void HandleResolveRaidEffect(OutcomeEffect effect, StationState station,
                                              Dictionary<string, object> context)
        {
            string shipUid = context.ContainsKey("ship_uid") ? context["ship_uid"].ToString() : "";
            if (!station.ships.TryGetValue(shipUid, out var ship)) return;
            var outcome = Combat.ResolveRaid(station, ship);
            station.LogEvent(outcome.narrative);
            station.LogEvent($"Raid combat outcome: {outcome.tier}");
        }

        private void HandleResolveShipToStationEffect(OutcomeEffect effect, StationState station,
                                                       Dictionary<string, object> context)
        {
            string shipUid = context.ContainsKey("ship_uid") ? context["ship_uid"].ToString() : "";
            if (!station.ships.TryGetValue(shipUid, out var ship)) return;
            var outcome = Combat.ResolveShipToStation(station, ship);
            station.LogEvent(outcome.narrative);
            station.LogEvent($"Ship-to-station combat outcome: {outcome.tier}");
        }

        private void HandleResolveStationToStationEffect(OutcomeEffect effect, StationState station,
                                                          Dictionary<string, object> context)
        {
            string attackerName = context.ContainsKey("attacker_name")
                ? context["attacker_name"].ToString() : "Hostile station";
            int threatLevel = context.ContainsKey("threat_level")
                ? System.Convert.ToInt32(context["threat_level"]) : 5;
            var outcome = Combat.ResolveStationToStation(station, attackerName, threatLevel);
            station.LogEvent(outcome.narrative);
            station.LogEvent($"Station-to-station combat outcome: {outcome.tier}");
        }

        private void HandleResolveSabotageEffect(OutcomeEffect effect, StationState station,
                                                   Dictionary<string, object> context)
        {
            string saboteurName = context.ContainsKey("saboteur_name")
                ? context["saboteur_name"].ToString() : "Unknown saboteur";
            var outcome = Combat.ResolveSabotage(station, saboteurName);
            station.LogEvent(outcome.narrative);
            station.LogEvent($"Sabotage combat outcome: {outcome.tier}");
        }

        private void HandleResolveMentalBreakCombatEffect(OutcomeEffect effect, StationState station,
                                                            Dictionary<string, object> context)
        {
            if (!FeatureFlags.MentalBreakCombat) return;

            string npcUid = context.ContainsKey("npc_uid") ? context["npc_uid"].ToString() : "";
            if (!station.npcs.TryGetValue(npcUid, out var breakdown)) return;

            // Counsellor is available if any crew member has the counsellor class and is not in crisis
            bool counsellorAvailable = false;
            if (FeatureFlags.NpcCounselling)
            {
                foreach (var npc in station.GetCrew())
                {
                    if (npc.uid == breakdown.uid) continue;
                    if (npc.classId == "class.counsellor")
                    {
                        counsellorAvailable = true;
                        break;
                    }
                }
            }

            var outcome = Combat.ResolveMentalBreakCombat(station, breakdown, counsellorAvailable);
            station.LogEvent(outcome.narrative);
            station.LogEvent($"Mental break combat outcome: {outcome.tier}");
        }

        // ── Speed control ─────────────────────────────────────────────────────

        /// <summary>Current real-time seconds between game ticks.</summary>
        public float SecondsPerTick => secondsPerTick;

        public void SetSpeed(float ticksPerSecond)
            => secondsPerTick = ticksPerSecond > 0f ? 1f / ticksPerSecond : 0.5f;

        // ── Difficulty control ────────────────────────────────────────────────

        /// <summary>
        /// Override the difficulty setting. Can be called before or during a game.
        /// Delegates to EventSystem.SetDifficulty() so no system rebuild is needed.
        /// </summary>
        public void SetDifficulty(string newDifficulty)
        {
            if (string.IsNullOrEmpty(newDifficulty)) return;
            difficulty = newDifficulty;
            // Update the EventSystem in-place if it has already been created.
            Events?.SetDifficulty(difficulty, Station != null ? Station.tick : 0);
        }
    }
}
