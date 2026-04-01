// Visitor / Docking System — processes ship arrivals and NPC intake.
//
// Responsibilities:
//   - Generate incoming ships from templates based on regional pressure
//   - Handle docking decisions (admit / deny)
//   - Spawn passenger NPCs for docked ships
//   - Manage departure timing
//   - Trigger hostile escalation for denied ships with hostile_if_denied tag
//   - Generate trade offers for trading ships
//   - Run contraband checks when inspection is active
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class VisitorSystem
    {
        private readonly ContentRegistry _registry;
        private readonly NPCSystem       _npcSystem;
        private readonly EventSystem     _eventSystem;
        private readonly TradeSystem     _tradeSystem;
        private readonly NPCTaskQueueManager _taskQueue;
        private readonly InventorySystem  _inventorySystem;

        public const float ContrabandDetectionChance = 0.25f;
        public const int ContrabandBaseDifficulty = 12;
        public const float ContrabandCreditPenalty = 200f;
        public const float ContrabandRepPenalty = -10f;

        // How many ticks a docked ship stays before departing
        public const int DockedDurationMin = 3;
        public const int DockedDurationMax = 12;

        private static readonly string[] ShipPrefixes =
        {
            "ISV","MCV","RSV","FSS","RVS","DSV","CRV","ASV","STV"
        };
        private static readonly string[] ShipNames =
        {
            "Wayward Star","Iron Margin","Pale Accord","Threshold Crossing",
            "Second Dawn","Drift Signal","Open Hand","Cold Meridian",
            "Faded Mark","Running Tide","Broken Covenant","Quiet Passage",
            "Ember Trade","Scatterlight","Long Reach","Veil Runner",
            "Dust Pilgrim","Low Road","Amber Frontier","Signal Lost"
        };

        private static readonly Dictionary<string, List<(string intent, float weight)>> RoleIntentMap =
            new Dictionary<string, List<(string, float)>>
        {
            { "trader",    new List<(string,float)> { ("trade",0.85f),("smuggle",0.15f) } },
            { "refugee",   new List<(string,float)> { ("refuge",0.90f),("transit",0.10f) } },
            { "inspector", new List<(string,float)> { ("inspect",1.0f) } },
            { "smuggler",  new List<(string,float)> { ("smuggle",0.70f),("trade",0.30f) } },
            { "raider",    new List<(string,float)> { ("raid",0.80f),("threaten",0.20f) } },
            { "transport", new List<(string,float)> { ("transit",0.60f),("refuge",0.40f) } },
            { "patrol",    new List<(string,float)> { ("patrol",1.0f) } }
        };

        // ── Pending docking decisions ─────────────────────────────────────────
        // Ship UIDs that require an explicit player Grant / Deny / Negotiate decision
        // before they are admitted.  Populated in ProcessIncoming and cleared by the
        // three decision methods below.
        public readonly HashSet<string> PendingDecisions = new HashSet<string>(StringComparer.Ordinal);

        public VisitorSystem(ContentRegistry registry, NPCSystem npcSystem,
                              EventSystem eventSystem, TradeSystem tradeSystem = null,
                              NPCTaskQueueManager taskQueue = null,
                              InventorySystem inventorySystem = null)
        {
            _registry    = registry;
            _npcSystem   = npcSystem;
            _eventSystem = eventSystem;
            _tradeSystem = tradeSystem;
            _taskQueue   = taskQueue;
            _inventorySystem = inventorySystem;
        }

        // ── Docking decision API (UI-016) ─────────────────────────────────────

        /// <summary>Returns all docked ships for the given station.</summary>
        public List<ShipInstance> GetDockedShips(StationState station)
            => station?.GetDockedShips() ?? new List<ShipInstance>();

        /// <summary>Returns all incoming ships for the given station.</summary>
        public List<ShipInstance> GetIncomingShips(StationState station)
            => station?.GetIncomingShips() ?? new List<ShipInstance>();

        /// <summary>
        /// Player grants docking permission.  The ship is admitted and removed from
        /// <see cref="PendingDecisions"/>.
        /// </summary>
        public void GrantDocking(string shipId, StationState station)
        {
            PendingDecisions.Remove(shipId);
            AdmitShip(shipId, station);
        }

        /// <summary>
        /// Player denies docking permission.  The ship is denied entry and removed from
        /// <see cref="PendingDecisions"/>.
        /// </summary>
        public void DenyDocking(string shipId, StationState station)
        {
            PendingDecisions.Remove(shipId);
            DenyShip(shipId, station);
        }

        /// <summary>
        /// Player opens a negotiation with the incoming ship.  The ship is admitted
        /// (it docks to allow talks) and removed from <see cref="PendingDecisions"/>.
        /// A negotiation event is logged so the comms log reflects the interaction.
        /// </summary>
        public void NegotiateDocking(string shipId, StationState station)
        {
            PendingDecisions.Remove(shipId);
            if (station.ships.TryGetValue(shipId, out var ship))
                station.LogEvent($"Negotiation opened with {ship.name}.");
            AdmitShip(shipId, station);
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            ProcessIncoming(station);
            TickInspectionPolicy(station);
            TickDocked(station);
        }

        private void TickInspectionPolicy(StationState station)
        {
            if (!station.HasTag("inspection_in_progress")) return;

            bool inspectorDocked = false;
            foreach (var ship in station.GetDockedShips())
            {
                if (ship.role == "inspector")
                {
                    inspectorDocked = true;
                    break;
                }
            }
            if (inspectorDocked) return;

            foreach (var ship in station.GetIncomingShips())
            {
                if (ship.role == "inspector") return;
            }

            if (_registry.Ships.Count == 0) return;

            ShipTemplate inspectorTemplate = null;
            foreach (var t in _registry.Ships.Values)
            {
                if (t.role == "inspector")
                {
                    inspectorTemplate = t;
                    break;
                }
            }
            if (inspectorTemplate == null) return;

            string factionId = PickFactionForShip(inspectorTemplate);
            string name = ShipPrefixes[UnityEngine.Random.Range(0, ShipPrefixes.Length)]
                        + " " + ShipNames[UnityEngine.Random.Range(0, ShipNames.Length)];
            var shipInstance = ShipInstance.Create(
                inspectorTemplate.id, name, inspectorTemplate.role, "inspect", factionId, inspectorTemplate.threatLevel);
            station.AddShip(shipInstance);
            PendingDecisions.Add(shipInstance.uid);
            station.LogEvent($"Inspection patrol inbound: {shipInstance.name}.");
            _eventSystem.QueueEvent("event.arrival_generic",
                new Dictionary<string, object> { { "ship_uid", shipInstance.uid } });
        }

        // ── Arrival generation ────────────────────────────────────────────────

        private void ProcessIncoming(StationState station)
        {
            if (!ShouldGenerateArrival(station)) return;
            var ship = GenerateShip(station);
            if (ship == null) return;

            station.AddShip(ship);
            // Flag ship for player docking decision (UI-016).
            PendingDecisions.Add(ship.uid);
            station.LogEvent(
                $"Incoming: {ship.name} ({ship.role}, intent={ship.intent}, threat={ship.ThreatLabel()})");
            _eventSystem.QueueEvent("event.arrival_generic",
                new Dictionary<string, object> { { "ship_uid", ship.uid } });
        }

        private bool ShouldGenerateArrival(StationState station)
        {
            float prob = 0.20f;
            if (station.HasTag("active_trading"))   prob += 0.10f;
            if (station.HasTag("under_blockade"))   prob -= 0.15f;
            return UnityEngine.Random.value < Mathf.Max(0.02f, prob);
        }

        private ShipInstance GenerateShip(StationState station)
        {
            if (_registry.Ships.Count == 0) return null;

            // Fleet-only templates are player-owned ships and must not appear as visitors.
            var templates = new List<ShipTemplate>();
            foreach (var t in _registry.Ships.Values)
                if (!t.fleetOnly) templates.Add(t);

            if (templates.Count == 0) return null;

            var weights   = new List<float>();
            bool dangerous = station.HasTag("dangerous_region");
            foreach (var t in templates)
            {
                float w = t.role == "raider"    ? (dangerous ? 2f : 0.3f) :
                          t.role == "inspector" ? 0.5f : 1f;
                weights.Add(w);
            }

            float total = 0f; foreach (var w in weights) total += w;
            float roll = UnityEngine.Random.Range(0f, total);
            float acc  = 0f;
            ShipTemplate template = templates[templates.Count - 1];
            for (int i = 0; i < templates.Count; i++)
            {
                acc += weights[i];
                if (roll <= acc) { template = templates[i]; break; }
            }

            string factionId = PickFactionForShip(template);
            string intent    = RollIntent(template.role);
            string name      = ShipPrefixes[UnityEngine.Random.Range(0, ShipPrefixes.Length)]
                             + " " + ShipNames[UnityEngine.Random.Range(0, ShipNames.Length)];

            return ShipInstance.Create(template.id, name, template.role,
                                       intent, factionId, template.threatLevel);
        }

        private string PickFactionForShip(ShipTemplate template)
        {
            if (template.factionRestrictions.Count > 0)
                return template.factionRestrictions[UnityEngine.Random.Range(0, template.factionRestrictions.Count)];
            if (_registry.Factions.Count > 0)
            {
                var keys = new List<string>(_registry.Factions.Keys);
                return keys[UnityEngine.Random.Range(0, keys.Count)];
            }
            return null;
        }

        private static string RollIntent(string role)
        {
            if (!RoleIntentMap.TryGetValue(role, out var options))
                return "unknown";
            float total = 0f; foreach (var (_, w) in options) total += w;
            float roll = UnityEngine.Random.Range(0f, total);
            float acc  = 0f;
            foreach (var (intent, w) in options)
            {
                acc += w;
                if (roll <= acc) return intent;
            }
            return options[options.Count - 1].intent;
        }

        // ── Docking decisions ─────────────────────────────────────────────────

        public bool AdmitShip(string shipUid, StationState station)
        {
            if (!station.ships.TryGetValue(shipUid, out var ship) || ship.status != "incoming")
                return false;

            var dock = station.GetAvailableDock();
            if (dock == null)
            {
                station.LogEvent($"No dock available for {ship.name} — ship queuing.");
                return false;
            }

            ship.status    = "docked";
            ship.dockedAt  = dock.uid;
            dock.dockedShip = shipUid;
            // Record a deterministic departure tick so TickDocked doesn't re-roll every tick.
            ship.plannedDepartureTick = station.tick +
                UnityEngine.Random.Range(DockedDurationMin, DockedDurationMax + 1);
            station.LogEvent($"{ship.name} docked at {dock.displayName}.");

            SpawnPassengers(ship, station);

            if ((ship.intent == "trade" || ship.intent == "smuggle") && _tradeSystem != null)
            {
                var offer = _tradeSystem.GenerateOffer(ship, station);
                if (offer != null)
                {
                    station.tradeOffers[shipUid] = offer;
                    station.LogEvent($"Trade offer from {ship.name}: {offer.GetSellLines().Count} sell, {offer.GetBuyLines().Count} buy lines.");

                    if (FeatureFlags.TradeStandingOrders)
                        _tradeSystem.ExecuteStandingOrders(ship, offer, station);
                }
            }

            if (ship.intent == "refuge")
                station.LogEvent($"Refugees aboard {ship.name} request food and shelter.");

            return true;
        }

        /// <summary>
        /// Dev tool: spawn and immediately admit a trade ship on demand.
        /// Falls back to any ship template if no trader template exists.
        /// If no dock is available the ship queues as incoming.
        /// </summary>
        public void SpawnTradeShip(StationState station)
        {
            ShipTemplate template = null;
            foreach (var t in _registry.Ships.Values)
                if (t.role == "trader") { template = t; break; }
            if (template == null)
                foreach (var t in _registry.Ships.Values) { template = t; break; }
            if (template == null)
            {
                Debug.LogWarning("[VisitorSystem] SpawnTradeShip: no ship templates found.");
                return;
            }

            string factionId = PickFactionForShip(template);
            string name = ShipPrefixes[UnityEngine.Random.Range(0, ShipPrefixes.Length)]
                        + " " + ShipNames[UnityEngine.Random.Range(0, ShipNames.Length)];
            var ship = ShipInstance.Create(template.id, name, template.role,
                                           "trade",  // intent: here to buy/sell goods
                                           factionId, template.threatLevel);
            station.AddShip(ship);
            station.LogEvent($"[DEV] Trade ship called: {ship.name}");

            if (!AdmitShip(ship.uid, station))
                station.LogEvent($"{ship.name} is incoming \u2014 no dock free.");

            _eventSystem?.QueueEvent("event.arrival_generic",
                new Dictionary<string, object> { { "ship_uid", ship.uid } });
        }

        public void DenyShip(string shipUid, StationState station)
        {
            if (!station.ships.TryGetValue(shipUid, out var ship)) return;

            bool willEscalate = false;
            if (_registry.Ships.TryGetValue(ship.templateId, out var template))
                willEscalate = template.behaviorTags.Contains("hostile_if_denied") &&
                               (ship.intent == "raid" || ship.intent == "threaten");

            if (willEscalate)
            {
                ship.status = "hostile";
                ship.intent = "raid";
                if (!ship.behaviorTags.Contains("hostile_task_queue"))
                    ship.behaviorTags.Add("hostile_task_queue");
                station.LogEvent($"{ship.name} denied — ship turning hostile!");
                QueueBoardingEvent(shipUid, station);
            }
            else
            {
                station.LogEvent($"{ship.name} denied entry — ship departing.");
                station.RemoveShip(shipUid);
            }

            if (ship.factionId != null) station.ModifyFactionRep(ship.factionId, -5f);
        }

        public void DepartShip(string shipUid, StationState station)
        {
            if (!station.ships.TryGetValue(shipUid, out var ship)) return;

            // Clean up pending decision entry if the ship departs without player action.
            PendingDecisions.Remove(shipUid);

            if (ship.dockedAt != null && station.modules.TryGetValue(ship.dockedAt, out var dock))
                dock.dockedShip = null;

            foreach (var npcUid in new List<string>(ship.passengerUids))
                station.RemoveNpc(npcUid);

            station.tradeOffers.Remove(shipUid);
            station.LogEvent($"{ship.name} departed.");
            station.RemoveShip(shipUid);
        }

        // ── Docked ship tick ──────────────────────────────────────────────────

        private void TickDocked(StationState station)
        {
            foreach (var ship in new List<ShipInstance>(station.GetDockedShips()))
            {
                EvaluateVisitorCompletion(ship, station);
                ship.ticksDocked++;
                // Compare against the departure tick assigned at docking time.
                // If not set (legacy / edge-case), reconstruct from ticksDocked so the
                // ship doesn't depart immediately if already docked for a while.
                if (ship.plannedDepartureTick < 0)
                    ship.plannedDepartureTick = (station.tick - ship.ticksDocked) +
                        UnityEngine.Random.Range(DockedDurationMin, DockedDurationMax + 1);

                if (station.tick >= ship.plannedDepartureTick)
                    DepartShip(ship.uid, station);
            }
        }

        // ── Passenger spawning ────────────────────────────────────────────────

        private void SpawnPassengers(ShipInstance ship, StationState station)
        {
            if (!_registry.Ships.TryGetValue(ship.templateId, out var template)) return;
            if (template.passengerCapacity <= 0) return;

            int count = UnityEngine.Random.Range(0, template.passengerCapacity + 1);
            string npcTemplateId = PickVisitorTemplate(ship);
            if (npcTemplateId == null) return;

            for (int i = 0; i < count; i++)
            {
                var npc = _npcSystem.SpawnVisitor(npcTemplateId, ship.factionId);
                if (npc == null) continue;
                var spawnLocation = ResolveVisitorSpawnTile(station);
                npc.location = spawnLocation;
                npc.memory["visitor_ship_uid"] = ship.uid;
                npc.memory["visitor_ship_tile"] = spawnLocation;
                npc.memory["visitor_visit_complete"] = false;
                station.AddNpc(npc);
                ship.passengerUids.Add(npc.uid);
                EnqueueVisitTasksForRole(npc, ship, station);
            }
        }

        private void EnqueueVisitTasksForRole(NPCInstance npc, ShipInstance ship, StationState station)
        {
            if (_taskQueue == null) return;

            string roomType = GetRoomTypeForRole(ship.role);
            bool isPasserby = string.Equals(ship.role, "passerby", StringComparison.Ordinal);
            bool isInspector = string.Equals(ship.role, "inspector", StringComparison.Ordinal);

            if (isPasserby || string.IsNullOrEmpty(roomType))
            {
                _taskQueue.Enqueue(npc.uid, new IdleInHangarTask(12));
            }
            else if (ship.role == "trader" || ship.role == "smuggler")
            {
                _taskQueue.Enqueue(npc.uid, new ShopVisitTask("cargo_hold", 6));
            }
            else
            {
                _taskQueue.Enqueue(npc.uid, new VisitRoomTask(roomType, 8));
            }

            if (isInspector)
                _taskQueue.Enqueue(npc.uid, new InspectorScanTask(
                    _registry, _eventSystem, _inventorySystem,
                    ContrabandBaseDifficulty, ContrabandCreditPenalty, ContrabandRepPenalty));

            NPCTaskHelpers.ParseLocation(
                npc.memory.TryGetValue("visitor_ship_tile", out var back) ? back?.ToString() : "0_0",
                out int returnCol, out int returnRow);

            _taskQueue.Enqueue(npc.uid, new ReturnToShipTask(returnCol, returnRow));
            _taskQueue.Enqueue(npc.uid, new MarkVisitCompleteTask());
        }

        public static string GetRoomTypeForRole(string role)
        {
            switch (role)
            {
                case "trader":
                case "inspector":
                case "smuggler":
                    return "cargo_hold";
                case "diplomat":
                    return "comms_room";
                case "medical_emergency":
                    return "medical_bay";
                case "refugee":
                case "transport":
                    return "common_area";
                case "passerby":
                case "raider":
                case "pirate":
                    return "hangar";
                default:
                    return "hangar";
            }
        }

        private static string ResolveVisitorSpawnTile(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId == "buildable.shuttle_landing_pad" && f.status == "complete")
                    return $"{f.tileCol}_{f.tileRow}";
            }
            return "0_0";
        }

        private void EvaluateVisitorCompletion(ShipInstance ship, StationState station)
        {
            if (ship.passengerUids.Count == 0) return;
            bool allComplete = true;
            foreach (var npcUid in ship.passengerUids)
            {
                if (!station.npcs.TryGetValue(npcUid, out var npc)) continue;
                bool done = npc.memory.TryGetValue("visitor_visit_complete", out var completeObj)
                            && completeObj is bool complete && complete;
                if (!done) { allComplete = false; break; }
            }
            if (allComplete)
                ship.plannedDepartureTick = Mathf.Min(ship.plannedDepartureTick, station.tick + 1);
        }

        private void QueueBoardingEvent(string shipUid, StationState station)
        {
            if (string.IsNullOrEmpty(shipUid)) return;
            if (station == null) return;

            // Ensure subsequent resolve_boarding has ship context.
            _eventSystem.QueueEvent("event.hostile_ship",
                new Dictionary<string, object> { { "ship_uid", shipUid } });
        }

        private string PickVisitorTemplate(ShipInstance ship)
        {
            var roleMap = new Dictionary<string, string>
            {
                { "trader",    "npc.trader"   },
                { "refugee",   "npc.refugee"  },
                { "inspector", "npc.inspector"},
                { "smuggler",  "npc.smuggler" },
                { "raider",    "npc.raider"   },
                { "transport", "npc.refugee"  },
                { "diplomat",  "npc.trader"   },
                { "medical_emergency", "npc.refugee" },
                { "passerby",  "npc.refugee" }
            };
            if (roleMap.TryGetValue(ship.role, out var tmplId) && _registry.Npcs.ContainsKey(tmplId))
                return tmplId;
            foreach (var id in _registry.Npcs.Keys)
                if (id.StartsWith("npc.")) return id;
            return null;
        }
    }
}
