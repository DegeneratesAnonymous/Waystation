// Ship Visit State Machine — drives all valid state transitions for the full
// ship visit lifecycle and handles shuttle dispatch / landing / departure.
//
// State graph:
//   OutOfRange  → InRange       (antenna detects ship)
//   InRange     → Passing       (no self-dock trigger)
//   InRange     → Inbound       (self-dock OR successful player hail)
//   Passing     → Inbound       (successful player hail)
//   Inbound     → Docked        (shuttle lands on a free pad)
//   Docked      → Departing     (visit timer expires)
//   Departing   → OutOfRange    (shuttle returns, ship moves out of range)
//   Any         → OutOfRange    (ship drifts beyond antenna range OR antenna offline)
//
// Invalid transitions are logged and rejected.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ShipVisitStateMachine
    {
        // Visit duration is converted from seconds to ticks using this constant.
        // Real-time seconds per tick is set in GameManager (default 0.5s).
        // To avoid coupling, visit duration ticks are calculated at docking time.
        // Default visit duration ticks if ShipTemplate doesn't specify (60 ticks ≈ 30s)
        private const int DefaultVisitDurationTicks = 60;
        // Each second in real time corresponds to this many ticks.
        // Mirrors GameManager.secondsPerTick (default 0.5s). Change both together if speed is altered.
        private float _secondsPerTick;

        /// <summary>
        /// Hail cooldown in ticks (60 ticks = 30 real-time seconds at 0.5s/tick).
        /// Matches acceptance criteria: player may re-hail after 60 ticks.
        /// Used by StationState.SetHailCooldown() via CommunicationsSystem.
        /// </summary>
        public const int HailCooldownTicks = 60;

        private readonly ContentRegistry _registry;
        private readonly NPCSystem       _npcSystem;

        public ShipVisitStateMachine(ContentRegistry registry, NPCSystem npcSystem,
                                      float secondsPerTick = 0.5f)
        {
            _registry      = registry;
            _npcSystem     = npcSystem;
            _secondsPerTick = secondsPerTick;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Main tick: advance each tracked ship and its shuttle through the state machine.
        /// Called every game tick.
        /// </summary>
        public void Tick(StationState station)
        {
            foreach (var ship in new List<ShipInstance>(station.ships.Values))
            {
                switch (ship.visitState)
                {
                    case ShipVisitState.Inbound:   TickInbound(ship, station);   break;
                    case ShipVisitState.Docked:    TickDocked(ship, station);    break;
                    case ShipVisitState.Departing: TickDeparting(ship, station); break;
                    // InRange / Passing / OutOfRange drift handled by AntennaSystem
                }
            }

            // Tick active shuttles
            foreach (var shuttle in new List<ShuttleInstance>(station.shuttles.Values))
                TickShuttle(shuttle, station);
        }

        /// <summary>
        /// Attempt to transition a ship to Inbound after a successful Social check.
        /// Returns true if the transition succeeded; false if already Inbound/Docked
        /// or no Hangar exists.
        /// </summary>
        public bool TransitionToInbound(string shipUid, StationState station)
        {
            if (!station.ships.TryGetValue(shipUid, out var ship)) return false;

            if (ship.visitState == ShipVisitState.Inbound  ||
                ship.visitState == ShipVisitState.Docked   ||
                ship.visitState == ShipVisitState.Departing)
            {
                Debug.LogWarning($"[SVSM] TransitionToInbound: {ship.name} already past Inbound.");
                return false;
            }

            if (!IsValidTransition(ship.visitState, ShipVisitState.Inbound))
            {
                Debug.LogWarning($"[SVSM] Invalid transition {ship.visitState} → Inbound for {ship.name}");
                return false;
            }

            if (!station.HasFunctionalHangar())
            {
                Debug.LogWarning($"[SVSM] TransitionToInbound: no functional Hangar.");
                return false;
            }

            ship.visitState = ShipVisitState.Inbound;
            ship.status     = "incoming";
            station.LogEvent($"{ship.name} is now Inbound.");
            Debug.Log($"[SVSM] {ship.name}: InRange/Passing → Inbound");
            return true;
        }

        // ── State tick helpers ────────────────────────────────────────────────

        private void TickInbound(ShipInstance ship, StationState station)
        {
            // Dispatch a shuttle if one hasn't been dispatched yet
            if (ship.shuttleUid != null) return;

            var pad = station.GetFreeLandingPad();
            if (pad == null)
            {
                // No free pad — add this ship to the first available pad's waiting queue
                foreach (var p in station.landingPads.Values)
                {
                    if (!p.waitingShipUids.Contains(ship.uid))
                        p.waitingShipUids.Add(ship.uid);
                    break;
                }
                return;
            }

            DispatchShuttle(ship, pad, station);
        }

        private void TickDocked(ShipInstance ship, StationState station)
        {
            // Wait for visit timer
            if (station.tick < ship.plannedDepartureTick) return;

            // Time to depart
            TransitionToDeparting(ship, station);
        }

        private void TickDeparting(ShipInstance ship, StationState station)
        {
            // The shuttle departure animation is ticked by TickShuttle.
            // When shuttle departs, it removes itself and triggers OutOfRange.
        }

        private void TickShuttle(ShuttleInstance shuttle, StationState station)
        {
            if (!station.ships.TryGetValue(shuttle.shipUid, out var ship)) return;

            switch (shuttle.state)
            {
                case "inbound":
                    // Animate shuttle toward landing pad tile
                    var pad = station.landingPads.ContainsKey(shuttle.landingPadUid)
                        ? station.landingPads[shuttle.landingPadUid] : null;
                    if (pad == null) { CleanupShuttle(shuttle, ship, station); return; }

                    // Convert tile position to a rough world position
                    float targetWx = shuttle.targetCol * 16f;
                    float targetWy = shuttle.targetRow * 16f;
                    float dx = targetWx - shuttle.worldX;
                    float dy = targetWy - shuttle.worldY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist < 4f)
                    {
                        // Landed
                        shuttle.state = "docked";
                        shuttle.worldX = targetWx;
                        shuttle.worldY = targetWy;
                        // pad.occupiedByShuttleUid was already set at dispatch time;
                        // confirm it is still pointing at this shuttle
                        if (pad.occupiedByShuttleUid != shuttle.uid)
                            pad.occupiedByShuttleUid = shuttle.uid;
                        OnShuttleLanded(shuttle, ship, station);
                    }
                    else
                    {
                        float speed = 8f;
                        float step  = Mathf.Min(speed, dist);
                        shuttle.worldX += (dx / dist) * step;
                        shuttle.worldY += (dy / dist) * step;
                    }
                    break;

                case "docked":
                    // Nothing to do — TickDocked handles timer
                    break;

                case "departing":
                    // Animate back to ship
                    float ddx = ship.worldX - shuttle.worldX;
                    float ddy = ship.worldY - shuttle.worldY;
                    float ddist = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (ddist < 8f)
                    {
                        // Returned to ship
                        OnShuttleReturned(shuttle, ship, station);
                    }
                    else
                    {
                        float speed = 10f;
                        float step  = Mathf.Min(speed, ddist);
                        shuttle.worldX += (ddx / ddist) * step;
                        shuttle.worldY += (ddy / ddist) * step;
                    }
                    break;
            }
        }

        // ── Transition actions ────────────────────────────────────────────────

        private void DispatchShuttle(ShipInstance ship, ShuttleLandingPadState pad,
                                      StationState station)
        {
            // Find the landing pad foundation to get its tile position
            if (!station.foundations.TryGetValue(pad.foundationUid, out var padFoundation))
                return;

            var shuttle = ShuttleInstance.Create(
                ship.uid, pad.foundationUid,
                padFoundation.tileCol, padFoundation.tileRow,
                ship.worldX, ship.worldY);

            station.shuttles[shuttle.uid] = shuttle;
            ship.shuttleUid = shuttle.uid;

            // Reserve the pad immediately so no other ship can claim it during approach
            pad.occupiedByShuttleUid = shuttle.uid;

            station.LogEvent($"{ship.name} dispatched a shuttle to landing pad.");
            Debug.Log($"[SVSM] {ship.name}: shuttle {shuttle.uid} dispatched → pad {pad.foundationUid} (reserved)");
        }

        private void OnShuttleLanded(ShuttleInstance shuttle, ShipInstance ship,
                                      StationState station)
        {
            ship.visitState = ShipVisitState.Docked;
            ship.status     = "docked";

            // Calculate visit duration in ticks
            int visitTicks = DefaultVisitDurationTicks;
            if (_registry.Ships.TryGetValue(ship.templateId, out var template))
                visitTicks = Mathf.Max(1, Mathf.RoundToInt(template.visitDuration / _secondsPerTick));

            ship.plannedDepartureTick = station.tick + visitTicks;

            // Spawn visitor NPCs at the landing pad tile
            SpawnVisitors(shuttle, ship, station);

            station.LogEvent($"{ship.name} shuttle docked — {shuttle.visitorNpcUids.Count} visitor(s) aboard. Departs in ~{visitTicks} ticks.");
            Debug.Log($"[SVSM] {ship.name} docked; visitors={shuttle.visitorNpcUids.Count} plannedDepart={ship.plannedDepartureTick}");
        }

        private void TransitionToDeparting(ShipInstance ship, StationState station)
        {
            if (!IsValidTransition(ship.visitState, ShipVisitState.Departing)) return;

            ship.visitState = ShipVisitState.Departing;
            ship.status     = "departing";

            // Remove visitor NPCs
            if (ship.shuttleUid != null && station.shuttles.TryGetValue(ship.shuttleUid, out var shuttle))
            {
                // Capture count before clearing so OnShuttleReturned can record it
                shuttle.peakVisitorCount = shuttle.visitorNpcUids.Count;

                foreach (var npcUid in new List<string>(shuttle.visitorNpcUids))
                    station.RemoveNpc(npcUid);
                shuttle.visitorNpcUids.Clear();

                // Free the landing pad
                if (station.landingPads.TryGetValue(shuttle.landingPadUid, out var pad))
                {
                    pad.occupiedByShuttleUid = null;
                    // Process any waiting shuttles
                    ProcessPadQueue(pad, station);
                }

                shuttle.state = "departing";
                Debug.Log($"[SVSM] {ship.name}: Docked → Departing; shuttle departing");
            }

            station.LogEvent($"{ship.name} visit timer expired — shuttle departing.");
        }

        private void OnShuttleReturned(ShuttleInstance shuttle, ShipInstance ship,
                                        StationState station)
        {
            // Remove shuttle
            station.shuttles.Remove(shuttle.uid);
            ship.shuttleUid = null;

            // Transition ship to OutOfRange
            ship.visitState = ShipVisitState.OutOfRange;
            station.RemoveShip(ship.uid);

            // Record visit history (use peakVisitorCount captured at departure time)
            station.visitHistory.Add(new ShipVisitRecord
            {
                shipUid      = ship.uid,
                shipName     = ship.name,
                shipRole     = ship.role,
                arrivedTick  = ship.inRangeSinceTick,
                departedTick = station.tick,
                visitorCount = shuttle.peakVisitorCount,
            });

            station.LogEvent($"{ship.name} departed — shuttle returned. Ship out of range.");
            Debug.Log($"[SVSM] {ship.name} OutOfRange (shuttle returned)");
        }

        private void CleanupShuttle(ShuttleInstance shuttle, ShipInstance ship,
                                     StationState station)
        {
            station.shuttles.Remove(shuttle.uid);
            ship.shuttleUid = null;
            ship.visitState = ShipVisitState.OutOfRange;
            station.RemoveShip(ship.uid);
            Debug.LogWarning($"[SVSM] Cleanup: shuttle {shuttle.uid} had no valid pad; ship removed.");
        }

        // ── Visitor NPC spawning ──────────────────────────────────────────────

        private void SpawnVisitors(ShuttleInstance shuttle, ShipInstance ship,
                                    StationState station)
        {
            if (!_registry.Ships.TryGetValue(ship.templateId, out var template)) return;
            int count = Mathf.Max(0, template.visitorCount);
            if (count == 0) return;  // ship template specifies no visitors (e.g. raider_corvette)

            string npcTemplateId = PickVisitorTemplate(ship);
            if (npcTemplateId == null) return;

            if (!station.foundations.TryGetValue(shuttle.landingPadUid, out var padFoundation))
                return;

            for (int i = 0; i < count; i++)
            {
                var npc = _npcSystem.SpawnVisitor(npcTemplateId, ship.factionId);
                if (npc == null) continue;

                // Place NPC at landing pad tile (location encoded as "col_row")
                npc.location  = $"{padFoundation.tileCol}_{padFoundation.tileRow}";
                npc.statusTags.Add("visitor");
                // TODO: visitors idle in hangar — shop/wander behaviour is a future work order
                station.AddNpc(npc);
                shuttle.visitorNpcUids.Add(npc.uid);
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

        // ── Landing pad queue ─────────────────────────────────────────────────

        private void ProcessPadQueue(ShuttleLandingPadState pad, StationState station)
        {
            if (pad.waitingShipUids.Count == 0) return;

            string nextShipUid = pad.waitingShipUids[0];
            pad.waitingShipUids.RemoveAt(0);

            if (!station.ships.TryGetValue(nextShipUid, out var nextShip)) return;
            if (nextShip.shuttleUid != null) return; // already dispatched somehow

            DispatchShuttle(nextShip, pad, station);
            Debug.Log($"[SVSM] Queued ship {nextShip.name} now dispatching to freed pad {pad.foundationUid}");
        }

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true for all valid state transitions as defined in the state graph.
        /// </summary>
        public static bool IsValidTransition(ShipVisitState from, ShipVisitState to)
        {
            switch (from)
            {
                case ShipVisitState.OutOfRange:
                    return to == ShipVisitState.InRange;
                case ShipVisitState.InRange:
                    return to == ShipVisitState.Passing  ||
                           to == ShipVisitState.Inbound  ||
                           to == ShipVisitState.OutOfRange;
                case ShipVisitState.Passing:
                    return to == ShipVisitState.Inbound  ||
                           to == ShipVisitState.OutOfRange;
                case ShipVisitState.Inbound:
                    return to == ShipVisitState.Docked   ||
                           to == ShipVisitState.OutOfRange;
                case ShipVisitState.Docked:
                    return to == ShipVisitState.Departing||
                           to == ShipVisitState.OutOfRange;
                case ShipVisitState.Departing:
                    return to == ShipVisitState.OutOfRange;
                default:
                    return false;
            }
        }
    }
}
