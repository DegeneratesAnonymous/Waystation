// Communications System (Visitor extension) — handles the player's ability to
// hail in-range ships via the Communications Menu.
//
// Flow:
//   1. Player clicks "Call" on a Passing/InRange ship in the Communications Menu.
//   2. CommunicationsSystem.TryHailShip() validates preconditions
//      (Hangar exists, ship is hailable, no cooldown).
//   3. Finds nearest unoccupied Access Terminal with JobType.Communications.
//   4. Finds nearest crew NPC to that terminal.
//   5. Creates a CommunicationsTask and enqueues it on the NPC's task queue.
//   6. When the task completes:
//      - PASS: ShipVisitStateMachine.TransitionToInbound()
//      - FAIL: 60-tick hail cooldown is applied to the ship.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class CommunicationsSystem
    {
        private readonly ContentRegistry        _registry;
        private readonly NPCTaskQueueManager    _taskQueue;
        private readonly ShipVisitStateMachine  _stateMachine;

        public CommunicationsSystem(ContentRegistry registry,
                                     NPCTaskQueueManager taskQueue,
                                     ShipVisitStateMachine stateMachine)
        {
            _registry     = registry;
            _taskQueue    = taskQueue;
            _stateMachine = stateMachine;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Attempt to hail a ship.
        /// Returns a human-readable result message for display in the UI.
        /// </summary>
        public string TryHailShip(string shipUid, StationState station)
        {
            if (!station.ships.TryGetValue(shipUid, out var ship))
                return "Ship not found.";

            // Validate ship state
            if (ship.visitState == ShipVisitState.Inbound  ||
                ship.visitState == ShipVisitState.Docked   ||
                ship.visitState == ShipVisitState.Departing||
                ship.visitState == ShipVisitState.OutOfRange)
                return $"{ship.name} is {ship.VisitStateLabel()} — cannot hail.";

            // Validate hangar
            if (!station.HasFunctionalHangar())
            {
                Debug.Log($"[CommunicationsSystem] Hail denied: no functional Hangar.");
                return "No functional hangar.";
            }

            // Check cooldown
            if (station.IsHailOnCooldown(shipUid))
            {
                int remaining = station.HailCooldownRemaining(shipUid);
                return $"Hail on cooldown — {remaining} tick(s) remaining.";
            }

            // Find nearest free Access Terminal
            var terminal = FindNearestFreeCommsTerminal(station);
            if (terminal == null)
            {
                Debug.Log($"[CommunicationsSystem] Hail denied: no free Access Terminal.");
                return "No available Access Terminal.";
            }

            // Find nearest crew NPC
            var npc = FindNearestCrewNpc(station, terminal.tileCol, terminal.tileRow);
            if (npc == null)
            {
                Debug.Log($"[CommunicationsSystem] Hail denied: no available crew NPC.");
                return "No available crew member.";
            }

            // Create and enqueue CommunicationsTask
            var task = new CommunicationsTask(
                terminal.uid,
                shipUid,
                _registry,
                pass => OnHailResult(pass, shipUid, npc.uid, station));

            // Lock the terminal before enqueuing so no concurrent hail can claim it
            _taskQueue.LockTerminal(terminal.uid, npc.uid);
            _taskQueue.Enqueue(npc.uid, task);
            npc.currentTaskId = task.Id;

            station.LogEvent($"{npc.name} assigned to hail {ship.name}.");
            Debug.Log($"[CommunicationsSystem] {npc.name} assigned CommunicationsTask for {ship.name}");
            return $"{npc.name} is heading to the terminal.";
        }

        /// <summary>
        /// Called each game tick to advance active tasks.
        /// </summary>
        public void Tick(StationState station)
        {
            _taskQueue.Tick(station);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void OnHailResult(bool pass, string shipUid, string npcUid, StationState station)
        {
            if (!station.ships.TryGetValue(shipUid, out var ship)) return;
            if (!station.npcs.TryGetValue(npcUid,  out var npc))  return;

            // Clear task assignment
            npc.currentTaskId = null;

            if (pass)
            {
                _stateMachine.TransitionToInbound(shipUid, station);
                station.LogEvent($"Hail success — {ship.name} is now Inbound.");
            }
            else
            {
                station.SetHailCooldown(shipUid, ShipVisitStateMachine.HailCooldownTicks);
                station.LogEvent($"Hail failed — {ship.name} not interested. " +
                                 $"Cooldown {ShipVisitStateMachine.HailCooldownTicks} ticks.");
            }
        }

        private FoundationInstance FindNearestFreeCommsTerminal(StationState station)
        {
            FoundationInstance best     = null;
            int                bestDist = int.MaxValue;

            // We don't yet persist per-terminal job-type config on foundations,
            // so we treat all Access Terminals as accepting Communications.
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.access_terminal" || f.status != "complete")
                    continue;
                // Skip terminals already locked by another NPC
                if (!_taskQueue.IsTerminalFree(f.uid)) continue;

                // Use station origin (0,0) as reference — caller has no position context here
                int dist = f.tileCol * f.tileCol + f.tileRow * f.tileRow;
                if (dist < bestDist) { bestDist = dist; best = f; }
            }
            return best;
        }

        private NPCInstance FindNearestCrewNpc(StationState station, int targetCol, int targetRow)
        {
            NPCInstance best     = null;
            int         bestDist = int.MaxValue;

            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.currentTaskId != null) continue;  // already busy

                ParseLocation(npc.location, out int nc, out int nr);
                int dist = Math.Abs(nc - targetCol) + Math.Abs(nr - targetRow);
                if (dist < bestDist) { bestDist = dist; best = npc; }
            }
            return best;
        }

        private static void ParseLocation(string location, out int col, out int row)
        {
            col = 0; row = 0;
            if (string.IsNullOrEmpty(location)) return;
            int sep = location.IndexOf('_');
            if (sep < 0) return;
            int.TryParse(location.Substring(0, sep), out col);
            int.TryParse(location.Substring(sep + 1), out row);
        }
    }
}
