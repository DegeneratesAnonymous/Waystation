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

        public const float ContrabandDetectionChance = 0.25f;

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

        public VisitorSystem(ContentRegistry registry, NPCSystem npcSystem,
                              EventSystem eventSystem, TradeSystem tradeSystem = null)
        {
            _registry    = registry;
            _npcSystem   = npcSystem;
            _eventSystem = eventSystem;
            _tradeSystem = tradeSystem;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            ProcessIncoming(station);
            TickDocked(station);
            TickContrabandChecks(station);
        }

        // ── Arrival generation ────────────────────────────────────────────────

        private void ProcessIncoming(StationState station)
        {
            if (!ShouldGenerateArrival(station)) return;
            var ship = GenerateShip(station);
            if (ship == null) return;

            station.AddShip(ship);
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

            var templates = new List<ShipTemplate>(_registry.Ships.Values);
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
            station.LogEvent($"{ship.name} docked at {dock.displayName}.");

            SpawnPassengers(ship, station);

            if ((ship.intent == "trade" || ship.intent == "smuggle") && _tradeSystem != null)
            {
                var offer = _tradeSystem.GenerateOffer(ship, station);
                if (offer != null)
                {
                    station.tradeOffers[shipUid] = offer;
                    station.LogEvent($"Trade offer from {ship.name}: {offer.GetSellLines().Count} sell, {offer.GetBuyLines().Count} buy lines.");
                }
            }

            if (ship.intent == "refuge")
                station.LogEvent($"Refugees aboard {ship.name} request food and shelter.");

            return true;
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
                station.LogEvent($"{ship.name} denied — ship turning hostile!");
                _eventSystem.QueueEvent("event.hostile_ship",
                    new Dictionary<string, object> { { "ship_uid", shipUid } });
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
                ship.ticksDocked++;
                int stay = UnityEngine.Random.Range(DockedDurationMin, DockedDurationMax + 1);
                if (ship.ticksDocked >= stay)
                    DepartShip(ship.uid, station);
            }
        }

        // ── Contraband detection ──────────────────────────────────────────────

        private void TickContrabandChecks(StationState station)
        {
            if (!station.HasTag("inspection_in_progress")) return;
            if (station.tick % 3 != 0) return;

            bool securityOnDuty = false;
            foreach (var n in station.npcs.Values)
                if (n.IsCrew() && n.classId == "class.security" &&
                    (n.currentJobId == "job.patrol" || n.currentJobId == "job.contraband_inspection"))
                { securityOnDuty = true; break; }

            if (!securityOnDuty) return;

            foreach (var ship in station.GetDockedShips())
            {
                if (!_registry.Ships.TryGetValue(ship.templateId, out var template)) continue;
                if (!template.behaviorTags.Contains("carries_contraband")) continue;
                if (UnityEngine.Random.value < ContrabandDetectionChance)
                {
                    _eventSystem.QueueEvent("event.contraband_found",
                        new Dictionary<string, object> { { "ship_uid", ship.uid }, { "ship_name", ship.name } });
                    station.ClearTag("inspection_in_progress");
                    return;
                }
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
                npc.location = ship.dockedAt ?? "docking_bay";
                station.AddNpc(npc);
                ship.passengerUids.Add(npc.uid);
            }
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
                { "transport", "npc.refugee"  }
            };
            if (roleMap.TryGetValue(ship.role, out var tmplId) && _registry.Npcs.ContainsKey(tmplId))
                return tmplId;
            foreach (var id in _registry.Npcs.Keys)
                if (id.StartsWith("npc.")) return id;
            return null;
        }
    }
}
