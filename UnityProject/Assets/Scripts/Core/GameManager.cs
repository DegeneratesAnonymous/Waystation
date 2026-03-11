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
        public ContentRegistry   Registry       { get; private set; }
        public ResourceSystem    Resources      { get; private set; }
        public NPCSystem         Npcs           { get; private set; }
        public JobSystem         Jobs           { get; private set; }
        public FactionSystem     Factions       { get; private set; }
        public CombatSystem      Combat         { get; private set; }
        public TradeSystem       Trade          { get; private set; }
        public EventSystem       Events         { get; private set; }
        public InventorySystem   Inventory      { get; private set; }
        public VisitorSystem     Visitors       { get; private set; }

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

            // Register external effect handlers on the event system
            Events.RegisterEffectHandler("resolve_boarding", HandleResolveBoardingEffect);
            Events.RegisterEffectHandler("spawn_npc",        HandleSpawnNpcEffect);
        }

        // ── New game ─────────────────────────────────────────────────────────

        public void NewGame(string stationName, int? seed = null)
        {
            if (seed.HasValue) UnityEngine.Random.InitState(seed.Value);

            Station = new StationState(stationName);
            SetupStartingModules();
            SetupStartingCrew();
            SetupStartingPolicies();
            Factions.Initialize(Station);
            Station.LogEvent($"Waystation '{stationName}' operational. All systems nominal.");

            IsPaused = false;
            OnGameLoaded?.Invoke();
            Debug.Log($"[GameManager] New game started: {stationName}");
        }

        private void SetupStartingModules()
        {
            var s = Station;
            // Add the core starting modules
            string[] startingModules =
            {
                "module.command_center",
                "module.docking_bay_a",
                "module.docking_bay_b",
                "module.crew_quarters",
                "module.storage_hold",
                "module.power_core"
            };
            foreach (var defId in startingModules)
            {
                if (Registry.Modules.TryGetValue(defId, out var defn))
                {
                    var mod = ModuleInstance.Create(defn.id, defn.displayName, defn.category);
                    if (defn.cargoCapacity > 0)
                        mod.cargoSettings = new CargoHoldSettings();
                    s.AddModule(mod);
                }
            }
        }

        private void SetupStartingCrew()
        {
            string[] startingTemplates = { "npc.engineer", "npc.security_officer", "npc.operations" };
            foreach (var tmpl in startingTemplates)
            {
                if (!Registry.Npcs.ContainsKey(tmpl)) continue;
                var npc = Npcs.SpawnCrewMember(tmpl);
                if (npc != null) Station.AddNpc(npc);
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
            Station.LogEvent($"{npc.name} recruited as crew ({RecruitCost:F0} credits).");
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
            Station.LogEvent($"Repair: {module.displayName} {oldDamage:P0} → {module.damage:P0}.");
            return (true, $"Repaired {module.displayName}.");
        }

        public void SetPolicy(string key, string value)
        {
            string old = Station.policy.ContainsKey(key) ? Station.policy[key] : "";
            Station.policy[key] = value;
            ApplyPolicyEffects(key, value, old);
            Station.LogEvent($"Policy '{key}' changed: {old} → {value}.");
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

        public void SaveGame()
        {
            if (Station == null) return;
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            var data    = new Dictionary<string, object>
            {
                { "station_name",         Station.stationName },
                { "tick",                 Station.tick },
                { "resources",            Station.resources },
                { "faction_reputation",   Station.factionReputation },
                { "active_tags",          new List<string>(Station.activeTags) },
                { "policy",               Station.policy },
                { "event_cooldowns",      Station.eventCooldowns },
                { "log",                  Station.log }
                // Full NPC/ship/module serialisation would go here in a production build
            };
            File.WriteAllText(path, MiniJSON.Json.Serialize(data));
            Station.LogEvent("Game saved.");
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
