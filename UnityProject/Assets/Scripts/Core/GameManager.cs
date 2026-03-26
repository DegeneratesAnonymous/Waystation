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
            Skills.OnSlotEarned += (npc, level) =>
            {
                Station?.LogEvent(
                    $"{npc.name} has grown as a person. A new expertise slot is available.");
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
            // Three connected 5×5 rooms arranged horizontally:
            //   Room A  cols  0– 4, rows 0–4
            //   Room B  cols  6–10, rows 0–4
            //   Room C  cols 12–16, rows 0–4
            //
            // Doors sit flush in each room's perimeter wall at the connection points
            // (4,2), (6,2), (10,2), (12,2). A single-tile corridor floor at cols 5
            // and 11, row 2 is enclosed above and below by wall tiles.
            var roomOrigins = new[] { (0, 0), (6, 0), (12, 0) };

            foreach (var (ox, oy) in roomOrigins)
            {
                // Floor: fill the entire 5×5 area
                for (int dx = 0; dx < 5; dx++)
                for (int dy = 0; dy < 5; dy++)
                    PlaceBuilt("buildable.floor", ox + dx, oy + dy);

                // Walls: top and bottom rows — place door at connection openings, wall elsewhere
                for (int dx = 0; dx < 5; dx++)
                {
                    PlaceBuilt(IsDoorwayConnection(ox + dx, oy)     ? "buildable.door" : "buildable.wall", ox + dx, oy);
                    PlaceBuilt(IsDoorwayConnection(ox + dx, oy + 4) ? "buildable.door" : "buildable.wall", ox + dx, oy + 4);
                }
                // Walls: left and right columns (corners already handled above)
                for (int dy = 1; dy <= 3; dy++)
                {
                    PlaceBuilt(IsDoorwayConnection(ox,     oy + dy) ? "buildable.door" : "buildable.wall", ox,     oy + dy);
                    PlaceBuilt(IsDoorwayConnection(ox + 4, oy + dy) ? "buildable.door" : "buildable.wall", ox + 4, oy + dy);
                }
            }

            // Corridor tiles: single-tile floors enclosed by walls above and below.
            // No door in the corridor itself — doors are flush in the room walls at each end.
            PlaceBuilt("buildable.floor", 5,  2);
            PlaceBuilt("buildable.wall",  5,  1);
            PlaceBuilt("buildable.wall",  5,  3);
            PlaceBuilt("buildable.floor", 11, 2);
            PlaceBuilt("buildable.wall",  11, 1);
            PlaceBuilt("buildable.wall",  11, 3);
        }

        /// <summary>Returns true if this position should be a door rather than a wall.</summary>
        private static bool IsDoorwayConnection(int col, int row) =>
            (col == 4  && row == 2) || (col == 6  && row == 2) ||
            (col == 10 && row == 2) || (col == 12 && row == 2);

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
            Comms.Tick(Station);
            Missions.Tick(Station);
            Rooms.Tick(Station);
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
                { "station_name",         Station.stationName },
                { "tick",                 Station.tick },
                { "resources",            Station.resources },
                { "faction_reputation",   Station.factionReputation },
                { "active_tags",          new List<string>(Station.activeTags) },
                { "policy",               Station.policy },
                { "event_cooldowns",      Station.eventCooldowns },
                { "log",                  Station.log },
                { "custom_room_names",    Station.customRoomNames },
                { "research",             researchData },
                { "galaxy",               sectorSaveData },
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
