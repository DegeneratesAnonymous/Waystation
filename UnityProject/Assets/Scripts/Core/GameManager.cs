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
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Core
{
    public class GameManager : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public const float RecruitCost       = 150f;
        public const float RepairPartsCost   =  10f;
        public const float RepairDamageAmount = 0.25f;

        // ── Singleton ─────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Serialised settings (Inspector) ──────────────────────────────────
        [Header("Game Speed")]
        [Tooltip("Real-time seconds between game ticks.")]
        [SerializeField] private float secondsPerTick = 0.5f;

        [Header("Difficulty")]
        [SerializeField] private string difficulty = "normal";

        [Header("Saves")]
        [SerializeField] private string saveFileName = "waystation_save.json";

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
        // ── Runtime state ─────────────────────────────────────────────────────
        public StationState Station  { get; private set; }
        public bool         IsPaused { get; set; } = true;
        public bool         IsLoaded { get; private set; }

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<StationState>    OnTick;
        public event Action<PendingEvent>    OnNewEvent;
        public event Action<string>          OnLogMessage;
        public event Action                  OnGameLoaded;

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
            Visitors  = new VisitorSystem(Registry, Npcs, Events, Trade);
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
            TaskQueue  = new NPCTaskQueueManager();
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
            Conversations.SetSkillSystem(Skills);

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
            FactionGov = new FactionGovernmentSystem(Traits);
            Regions    = new RegionSystem();

            // Stub implementations — replaced by Horizon Simulation work order
            RegionSim     = new RegionSimulationStub();
            FactionHistory = new FactionHistoryStub();

            // Wire tension stage change events to station log
            Tension.OnTensionStageChanged += (npc, stage) =>
            {
                if (stage != TensionStage.Normal)
                    Station?.LogEvent($"{npc.name}: tension stage → {TensionSystem.GetTensionStageLabel(stage)}");
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
            }
        }

        // ── New game ─────────────────────────────────────────────────────────

        public void NewGame(string stationName, int? seed = null)
        {
            if (seed.HasValue) UnityEngine.Random.InitState(seed.Value);

            Station = new StationState(stationName);
            Station.solarSystem = SolarSystemGenerator.Generate(stationName, seed);
            SetupStartingModules();
            SetupStartingCrew();
            SetupStartingPolicies();
            Factions.Initialize(Station);

            // Generate the galaxy sector map. Uses the same seed as solar system generation.
            int galaxySeed = seed.HasValue ? seed.Value : SolarSystemGenerator.StableHash(stationName);
            GalaxyGenerator.Generate(galaxySeed, Station);

            // Initialise skill instances for all starting crew.
            Skills.InitialiseNpcSkills(Station);

            // Reset threshold crossing state so warnings fire correctly from tick 1.
            Resources.ResetWarningState();

            Log($"Waystation '{stationName}' operational. All systems nominal.");

            IsPaused = false;
            OnGameLoaded?.Invoke();
            // Seed the room bonus cache immediately so it's available on the first frame.
            Rooms.RebuildBonusCache(Station);
            // Rebuild utility networks so grid connectivity is available from tick 1.
            UtilityNetworks.RebuildAll(Station);
            Debug.Log($"[GameManager] New game started: {stationName}");
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
            // Only run the interval tick when no forced rebuild already occurred this tick.
            if (!roomRebuildDoneThisTick) Rooms.Tick(Station);
            Research.Tick(Station);
            Map.Tick(Station);
            AsteroidMissions.Tick(Station);
            UtilityNetworks.Tick(Station);

            // Visitor pipeline (antenna detection → state machine → comms tasks)
            Antenna.Tick(Station);
            ShipVisits.Tick(Station);
            CommSystem.Tick(Station);

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
            Relationships.Tick(Station, Mood);

            // Skill system
            Skills.Tick(Station);

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
                            Traits.RegisterConditionPressure(npc, TraitConditionCategory.LowMood, 2f);
                        else if (npc.moodScore >= MoodSystem.ThrivingThreshold)
                            Traits.RegisterConditionPressure(npc, TraitConditionCategory.HighMood, 1f);
                    }
                }
                Traits.Tick(Station);
                Tension.Tick(Station);
            }
            if (FeatureFlags.FactionGovernment)
                FactionGov.Tick(Station, Registry.Factions);
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
        /// Loads the persisted save file and restores the fields that are
        /// currently serialised (resources, tags, log, research, galaxy, etc.).
        /// Full NPC/ship/module round-trip will be added once that data is saved.
        /// </summary>
        public void LoadGame()
        {
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            if (!File.Exists(path)) { Debug.LogWarning("[GameManager] No save file found."); return; }

            string json = File.ReadAllText(path);
            if (!(MiniJSON.Json.Deserialize(json) is Dictionary<string, object> data))
            {
                Debug.LogError("[GameManager] Save file could not be parsed.");
                return;
            }

            string stationName = data.TryGetValue("station_name", out var sn) ? sn.ToString() : "Waystation";
            Station = new StationState(stationName);

            if (data.TryGetValue("tick", out var tick))
                Station.tick = System.Convert.ToInt32(tick);

            if (data.TryGetValue("resources", out var res) && res is Dictionary<string, object> resDict)
                foreach (var kv in resDict)
                    Station.resources[kv.Key] = System.Convert.ToSingle(kv.Value);

            if (data.TryGetValue("active_tags", out var tags) && tags is List<object> tagList)
                foreach (var t in tagList) Station.activeTags.Add(t.ToString());

            if (data.TryGetValue("policy", out var pol) && pol is Dictionary<string, object> polDict)
                foreach (var kv in polDict) Station.policy[kv.Key] = kv.Value.ToString();

            if (data.TryGetValue("event_cooldowns", out var cd) && cd is Dictionary<string, object> cdDict)
                foreach (var kv in cdDict)
                    Station.eventCooldowns[kv.Key] = System.Convert.ToInt32(kv.Value);

            if (data.TryGetValue("log", out var lg) && lg is List<object> logList)
                foreach (var l in logList) Station.log.Add(l.ToString());

            if (data.TryGetValue("custom_room_names", out var crn) && crn is Dictionary<string, object> crnDict)
                foreach (var kv in crnDict) Station.customRoomNames[kv.Key] = kv.Value.ToString();

            if (data.TryGetValue("player_room_type_assignments", out var prta)
                && prta is Dictionary<string, object> prtaDict)
                foreach (var kv in prtaDict)
                    Station.playerRoomTypeAssignments[kv.Key] = kv.Value.ToString();

            // Research
            if (data.TryGetValue("research", out var rsr) && rsr is Dictionary<string, object> rsrDict
                && Station.research != null)
            {
                if (rsrDict.TryGetValue("pending_datachips", out var pdv))
                    Station.research.pendingDatachips = System.Convert.ToInt32(pdv);
                foreach (var kv in rsrDict)
                {
                    if (kv.Key == "pending_datachips") continue;
                    if (!(kv.Value is Dictionary<string, object> branchDict)) continue;
                    if (!System.Enum.TryParse(kv.Key, out ResearchBranch branch)) continue;
                    if (!Station.research.branches.TryGetValue(branch, out var bs)) continue;
                    if (branchDict.TryGetValue("points", out var pts))
                        bs.points = System.Convert.ToSingle(pts);
                    if (branchDict.TryGetValue("unlocked", out var ul) && ul is List<object> ulList)
                        foreach (var u in ulList) bs.unlockedNodeIds.Add(u.ToString());
                }
            }

            // Galaxy
            if (data.TryGetValue("galaxy", out var gal) && gal is Dictionary<string, object> galDict)
            {
                if (galDict.TryGetValue("galaxy_seed", out var gsv))
                    Station.galaxySeed = System.Convert.ToInt32(gsv);
                if (galDict.TryGetValue("sectors", out var secObj) && secObj is List<object> secList)
                {
                    foreach (var secRaw in secList)
                    {
                        if (!(secRaw is Dictionary<string, object> sd)) continue;
                        string uid = sd.TryGetValue("uid", out var uidv) ? uidv.ToString() : null;
                        if (uid == null || !Station.sectors.TryGetValue(uid, out var sec)) continue;
                        if (sd.TryGetValue("proper_name",  out var pn))  sec.properName = pn.ToString();
                        if (sd.TryGetValue("is_renamed",   out var ir))  sec.isRenamed  = System.Convert.ToBoolean(ir);
                        if (sd.TryGetValue("discovery",    out var dv)
                            && System.Enum.TryParse(dv.ToString(), out SectorDiscoveryState disc))
                            sec.discoveryState = disc;
                    }
                }
            }

            SetupStartingModules();
            SetupStartingCrew();
            Factions.Initialize(Station);
            Skills.InitialiseNpcSkills(Station);
            Rooms.RebuildBonusCache(Station);
            UtilityNetworks.RebuildAll(Station);

            // Reset threshold crossing state so warnings fire correctly on the first tick.
            Resources.ResetWarningState();

            Log($"Save loaded: '{stationName}' at tick {Station.tick}.");
            IsPaused = false;
            OnGameLoaded?.Invoke();
            Debug.Log($"[GameManager] Save loaded from {path}");
        }

        public void SaveGame()
        {
            if (Station == null) return;
            string path = Path.Combine(Application.persistentDataPath, saveFileName);

            // Serialise ResearchState: one entry per branch with points + unlocked node ids.
            // Also captures pending_datachips (chips produced but awaiting storage space).
            var researchData = new Dictionary<string, object>();
            if (Station.research != null)
            {
                foreach (var kv in Station.research.branches)
                {
                    researchData[kv.Key.ToString()] = new Dictionary<string, object>
                    {
                        { "points",   kv.Value.points },
                        { "unlocked", new List<string>(kv.Value.unlockedNodeIds) },
                    };
                }
                researchData["pending_datachips"] = Station.research.pendingDatachips;
            }

            // Serialise galaxy sector mutable state: seed + per-sector (properName, isRenamed, discoveryState)
            // The permanent designation code/coordinates are re-generated from the seed on load
            // if the sector list is missing, but for completeness we persist the mutable fields.
            var sectorSaveData = new Dictionary<string, object>();
            sectorSaveData["galaxy_seed"] = Station.galaxySeed;
            var sectorList = new List<object>();
            foreach (var s in Station.sectors.Values)
            {
                sectorList.Add(new Dictionary<string, object>
                {
                    { "uid",              s.uid },
                    { "proper_name",      s.properName },
                    { "is_renamed",       s.isRenamed },
                    { "discovery",        s.discoveryState.ToString() },
                    { "coordinates_x",    s.coordinates.x },
                    { "coordinates_y",    s.coordinates.y },
                    { "designation_code", s.designationCode },
                    { "prefix",           s.surveyPrefix.ToString() },
                });
            }
            sectorSaveData["sectors"] = sectorList;

            var data = new Dictionary<string, object>
            {
                { "station_name",                  Station.stationName },
                { "tick",                          Station.tick },
                { "resources",                     Station.resources },
                { "faction_reputation",            Station.factionReputation },
                { "active_tags",                   new List<string>(Station.activeTags) },
                { "policy",                        Station.policy },
                { "event_cooldowns",               Station.eventCooldowns },
                { "log",                           Station.log },
                { "custom_room_names",             Station.customRoomNames },
                { "player_room_type_assignments",  Station.playerRoomTypeAssignments },
                { "research",                      researchData },
                { "galaxy",                        sectorSaveData },
                // Full NPC/ship/module serialisation would go here in a production build
            };
            File.WriteAllText(path, MiniJSON.Json.Serialize(data));
            Log("Game saved.");
            Debug.Log($"[GameManager] Saved to {path}");
        }

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

        // ── Speed control ─────────────────────────────────────────────────────

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
