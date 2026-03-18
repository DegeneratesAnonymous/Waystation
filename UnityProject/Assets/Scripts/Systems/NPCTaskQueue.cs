// NPC Task Queue and task types for the Visitor System.
//
// NPCTask: base unit of work. Subclasses define specific behaviours.
// NPCTaskQueue: ordered list of NPCTask objects attached to a station.
//   On empty queue: NPC walks to nearest Access Terminal and claims next task.
//
// For this work order only one task type is fully implemented:
//   CommunicationsTask — walk to terminal, run Social check, return result.
// All other task types (shop visit, wander) are TODO stubs.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Task status ───────────────────────────────────────────────────────────

    public enum NPCTaskStatus
    {
        Pending,    // not yet started
        InProgress, // NPC is executing
        Succeeded,  // completed successfully
        Failed,     // could not complete (no path, terminal gone, etc.)
    }

    // ── Base task ─────────────────────────────────────────────────────────────

    public abstract class NPCTask
    {
        public string        Id     { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public NPCTaskStatus Status { get; protected set; } = NPCTaskStatus.Pending;

        /// <summary>Tick is called every game tick while this task is InProgress.</summary>
        public abstract void Tick(NPCInstance npc, StationState station);
    }

    // ── Communications Task ───────────────────────────────────────────────────

    /// <summary>
    /// Directs a crew NPC to walk to an Access Terminal with JobType.Communications,
    /// perform a Social skill check against the target ship's socialResistance, and
    /// report the result back via the provided callback.
    /// </summary>
    public class CommunicationsTask : NPCTask
    {
        public string TerminalFoundationUid { get; }
        public string TargetShipUid        { get; }

        private readonly Action<bool> _onResult;
        private readonly ContentRegistry _registry;

        // State machine: "walk_to_terminal" | "at_terminal" | "done"
        private string _phase = "walk_to_terminal";
        private List<PathStep> _path;
        private int _pathIndex;

        public CommunicationsTask(string terminalFoundationUid, string targetShipUid,
                                   ContentRegistry registry, Action<bool> onResult)
        {
            TerminalFoundationUid = terminalFoundationUid;
            TargetShipUid         = targetShipUid;
            _registry             = registry;
            _onResult             = onResult;
        }

        public override void Tick(NPCInstance npc, StationState station)
        {
            if (Status == NPCTaskStatus.Succeeded || Status == NPCTaskStatus.Failed) return;
            Status = NPCTaskStatus.InProgress;

            switch (_phase)
            {
                case "walk_to_terminal":
                    WalkToTerminal(npc, station);
                    break;
                case "at_terminal":
                    PerformSocialCheck(npc, station);
                    break;
            }
        }

        private void WalkToTerminal(NPCInstance npc, StationState station)
        {
            if (!station.foundations.TryGetValue(TerminalFoundationUid, out var terminal))
            {
                Debug.LogWarning($"[CommunicationsTask] Terminal {TerminalFoundationUid} not found; task Failed.");
                Status = NPCTaskStatus.Failed;
                _onResult?.Invoke(false);
                return;
            }

            // Parse NPC location
            int npcCol = 0, npcRow = 0;
            ParseLocation(npc.location, out npcCol, out npcRow);

            if (npcCol == terminal.tileCol && npcRow == terminal.tileRow)
            {
                _phase = "at_terminal";
                return;
            }

            // Compute or follow path
            if (_path == null || _pathIndex >= _path.Count)
            {
                _path = NPCPathfinder.FindPath(station, npcCol, npcRow,
                                                terminal.tileCol, terminal.tileRow);
                _pathIndex = 0;

                if (_path == null)
                {
                    Debug.LogWarning($"[CommunicationsTask] NPC {npc.name}: no path to terminal; task Failed.");
                    Status = NPCTaskStatus.Failed;
                    _onResult?.Invoke(false);
                    return;
                }
            }

            if (_pathIndex < _path.Count)
            {
                var step = _path[_pathIndex];

                // Check obstruction BEFORE moving — NPC must never step onto a blocked tile
                if (IsBlocked(station, step.Col, step.Row))
                {
                    // Tile became blocked mid-path; replan from current (uncommitted) position
                    _path = NPCPathfinder.FindPath(station, npcCol, npcRow,
                                                    terminal.tileCol, terminal.tileRow);
                    _pathIndex = 0;
                    if (_path == null)
                    {
                        Status = NPCTaskStatus.Failed;
                        _onResult?.Invoke(false);
                    }
                    return;
                }

                // Safe to move
                _pathIndex++;
                npc.location = $"{step.Col}_{step.Row}";
            }
        }

        private void PerformSocialCheck(NPCInstance npc, StationState station)
        {
            if (!station.ships.TryGetValue(TargetShipUid, out var ship))
            {
                Debug.LogWarning($"[CommunicationsTask] Ship {TargetShipUid} not found; task Failed.");
                Status = NPCTaskStatus.Failed;
                _onResult?.Invoke(false);
                return;
            }

            int socialResistance = 5;
            if (_registry.Ships.TryGetValue(ship.templateId, out var template))
                socialResistance = template.socialResistance;

            int roll  = UnityEngine.Random.Range(1, 21);  // d20
            int skill = npc.socialSkill;
            int target = socialResistance * 2;
            bool pass  = roll + skill >= target;

            Debug.Log($"[CommunicationsTask] {npc.name} hail attempt: " +
                      $"roll {roll} + skill {skill} vs resistance {socialResistance} " +
                      $"(target {target}) — {(pass ? "PASS" : "FAIL")}");

            Status = NPCTaskStatus.Succeeded;
            _phase  = "done";
            _onResult?.Invoke(pass);
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

        private static bool IsBlocked(StationState station, int col, int row)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.tileLayer < 2) continue;
                if (f.buildableId.Contains("door")) continue;
                // Check all tiles in the foundation's footprint (multi-tile aware)
                for (int dc = 0; dc < f.tileWidth;  dc++)
                for (int dr = 0; dr < f.tileHeight; dr++)
                    if (f.tileCol + dc == col && f.tileRow + dr == row) return true;
            }
            return false;
        }
    }

    // ── Shop Visit Task (TODO stub) ───────────────────────────────────────────

    /// <summary>
    /// TODO: Visitor NPC walks to a shop module and interacts.
    /// Behaviour tree nodes are stubbed here for a future work order.
    /// </summary>
    public class ShopVisitTask : NPCTask
    {
        public string TargetModuleUid { get; }
        public ShopVisitTask(string targetModuleUid) { TargetModuleUid = targetModuleUid; }

        public override void Tick(NPCInstance npc, StationState station)
        {
            // TODO: implement shop visit behaviour
            Status = NPCTaskStatus.Succeeded;
        }
    }

    // ── Idle Task (TODO stub) ─────────────────────────────────────────────────

    /// <summary>
    /// TODO: Visitor NPC idles in the hangar.
    /// Currently succeeds immediately — visitors simply stand at spawn position.
    /// </summary>
    public class IdleInHangarTask : NPCTask
    {
        public int DurationTicks { get; }
        private int _elapsedTicks = 0;
        public IdleInHangarTask(int durationTicks = 10) { DurationTicks = durationTicks; }

        public override void Tick(NPCInstance npc, StationState station)
        {
            Status = NPCTaskStatus.InProgress;
            if (++_elapsedTicks >= DurationTicks)
                Status = NPCTaskStatus.Succeeded;
        }
    }

    // ── NPC Task Queue ────────────────────────────────────────────────────────

    /// <summary>
    /// Per-station task queue manager.
    /// Maintains a queue of tasks for each NPC (keyed by npcUid).
    /// On empty queue, the NPC is directed to the nearest Access Terminal to claim a task.
    /// Called once per tick per NPC by the system that owns it.
    /// </summary>
    public class NPCTaskQueueManager
    {
        private readonly Dictionary<string, Queue<NPCTask>> _queues
            = new Dictionary<string, Queue<NPCTask>>();

        private readonly Dictionary<string, NPCTask> _activeTasks
            = new Dictionary<string, NPCTask>();

        // Tracks which terminal is being used by which NPC
        private readonly Dictionary<string, string> _terminalLocks
            = new Dictionary<string, string>();  // terminalFoundationUid → npcUid

        public NPCTaskQueueManager() { }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Returns true if no NPC currently holds a lock on this terminal.</summary>
        public bool IsTerminalFree(string terminalFoundationUid)
            => !_terminalLocks.ContainsKey(terminalFoundationUid);

        /// <summary>
        /// Lock a terminal to a specific NPC. Called by CommunicationsSystem when assigning
        /// a CommunicationsTask so no other hail can claim the same terminal.
        /// </summary>
        public void LockTerminal(string terminalFoundationUid, string npcUid)
            => _terminalLocks[terminalFoundationUid] = npcUid;

        /// <summary>Enqueue a task for the given NPC.</summary>
        public void Enqueue(string npcUid, NPCTask task)
        {
            if (!_queues.ContainsKey(npcUid))
                _queues[npcUid] = new Queue<NPCTask>();
            _queues[npcUid].Enqueue(task);
        }

        /// <summary>
        /// Tick the task queue for every NPC in the station.
        /// Call once per game tick.
        /// </summary>
        public void Tick(StationState station)
        {
            foreach (var npc in new List<NPCInstance>(station.npcs.Values))
                TickNpc(npc, station);
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void TickNpc(NPCInstance npc, StationState station)
        {
            // If there's an active task, continue it
            if (_activeTasks.TryGetValue(npc.uid, out var current) &&
                current.Status == NPCTaskStatus.InProgress)
            {
                current.Tick(npc, station);
                if (current.Status == NPCTaskStatus.Succeeded ||
                    current.Status == NPCTaskStatus.Failed)
                {
                    _activeTasks.Remove(npc.uid);
                    // Release any terminal lock held by this NPC
                    ReleaseTerminalLock(npc.uid);
                }
                return;
            }

            // Try dequeue next task
            if (_queues.TryGetValue(npc.uid, out var queue) && queue.Count > 0)
            {
                var next = queue.Dequeue();
                _activeTasks[npc.uid] = next;
                next.Tick(npc, station);
                return;
            }

            // Empty queue: only applies to crew NPCs; visitor NPCs idle in hangar
            if (!npc.IsCrew()) return;

            // Seek nearest unoccupied Access Terminal and claim a task
            TryClaimTerminalTask(npc, station);
        }

        private void TryClaimTerminalTask(NPCInstance npc, StationState station)
        {
            // Find nearest Access Terminal (buildable.access_terminal) not locked by another NPC
            int npcCol = 0, npcRow = 0;
            ParseLocation(npc.location, out npcCol, out npcRow);

            FoundationInstance bestTerminal = null;
            int bestDist = int.MaxValue;

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.access_terminal" || f.status != "complete")
                    continue;
                if (_terminalLocks.ContainsKey(f.uid)) continue;  // already claimed

                int dist = Math.Abs(f.tileCol - npcCol) + Math.Abs(f.tileRow - npcRow);
                if (dist < bestDist) { bestDist = dist; bestTerminal = f; }
            }

            if (bestTerminal == null) return;

            // Do not lock the terminal — only lock when a concrete task is assigned.
            // For this work order we don't persist job type lists on foundations,
            // so we treat all Access Terminals as accepting JobType.Communications.
            // TODO: query task registry and lock only when a task is actually claimed.
        }

        internal void ReleaseTerminalLock(string npcUid)
        {
            var toRemove = new List<string>();
            foreach (var kv in _terminalLocks)
                if (kv.Value == npcUid) toRemove.Add(kv.Key);
            foreach (var k in toRemove) _terminalLocks.Remove(k);
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
