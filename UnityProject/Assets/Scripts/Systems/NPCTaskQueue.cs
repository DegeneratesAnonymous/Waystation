// NPC Task Queue and task types for the Visitor System.
//
// NPCTask: base unit of work. Subclasses define specific behaviours.
// NPCTaskQueue: ordered list of NPCTask objects attached to a station.
//   On empty queue: NPC walks to nearest Access Terminal and claims next task.
//
// For this work order, visitor movement tasks are implemented so visitors can
// physically navigate to room-type destinations and idle in hangar space.
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
        public string TargetRoomTypeId { get; }
        public int    InteractionTicks { get; }

        private string _phase = "walk";
        private List<PathStep> _path;
        private int _pathIndex;
        private int _interactionElapsed = 0;
        private int _targetCol = int.MinValue;
        private int _targetRow = int.MinValue;

        public ShopVisitTask(string targetRoomTypeId = "cargo_hold", int interactionTicks = 6)
        {
            TargetRoomTypeId = string.IsNullOrEmpty(targetRoomTypeId) ? "cargo_hold" : targetRoomTypeId;
            InteractionTicks = Mathf.Max(1, interactionTicks);
        }

        public override void Tick(NPCInstance npc, StationState station)
        {
            if (Status == NPCTaskStatus.Succeeded || Status == NPCTaskStatus.Failed) return;
            Status = NPCTaskStatus.InProgress;

            if (_phase == "walk")
            {
                if (!TryWalkToTargetRoom(npc, station))
                {
                    Status = NPCTaskStatus.Failed;
                    return;
                }
                return;
            }

            if (_phase == "interact")
            {
                if (_interactionElapsed == 0)
                    station.LogEvent($"{npc.name} starts trading in the {TargetRoomTypeId.Replace('_', ' ')}.");

                _interactionElapsed++;
                if (_interactionElapsed >= InteractionTicks)
                {
                    npc.memory["visitor_trade_completed"] = true;
                    Status = NPCTaskStatus.Succeeded;
                }
            }
        }

        private bool TryWalkToTargetRoom(NPCInstance npc, StationState station)
        {
            NPCTaskHelpers.ParseLocation(npc.location, out int npcCol, out int npcRow);

            if (_targetCol == int.MinValue)
            {
                if (!NPCTaskHelpers.TryFindNearestReachableRoomTile(
                    station, npcCol, npcRow, TargetRoomTypeId, out _targetCol, out _targetRow))
                {
                    return false;
                }
            }

            if (npcCol == _targetCol && npcRow == _targetRow)
            {
                _phase = "interact";
                npc.memory["visitor_room_target"] = TargetRoomTypeId;
                return true;
            }

            if (NPCTaskHelpers.TryAdvancePathStep(station,
                    ref _path, ref _pathIndex,
                    npcCol, npcRow, _targetCol, _targetRow,
                    out var step))
            {
                npc.location = $"{step.Col}_{step.Row}";
            }
            else
            {
                return false;
            }

            return true;
        }
    }

    // ── Idle Task ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Visitor NPC idles in the hangar by wandering between reachable hangar tiles.
    /// </summary>
    public class IdleInHangarTask : NPCTask
    {
        public int DurationTicks { get; }
        private readonly int _moveIntervalTicks;
        private int _elapsedTicks = 0;
        private int _lastMoveTick = -1;
        private List<PathStep> _path;
        private int _pathIndex = 0;
        private int _targetCol = int.MinValue;
        private int _targetRow = int.MinValue;

        public IdleInHangarTask(int durationTicks = 10, int moveIntervalTicks = 2)
        {
            DurationTicks = Mathf.Max(1, durationTicks);
            _moveIntervalTicks = Mathf.Max(1, moveIntervalTicks);
        }

        public override void Tick(NPCInstance npc, StationState station)
        {
            Status = NPCTaskStatus.InProgress;

            _elapsedTicks++;
            if (_elapsedTicks >= DurationTicks)
            {
                Status = NPCTaskStatus.Succeeded;
                return;
            }

            if ((_elapsedTicks - _lastMoveTick) < _moveIntervalTicks) return;
            _lastMoveTick = _elapsedTicks;

            NPCTaskHelpers.ParseLocation(npc.location, out int npcCol, out int npcRow);

            if (_targetCol == int.MinValue || (_path != null && _pathIndex >= _path.Count))
            {
                if (!NPCTaskHelpers.TryPickRandomReachableHangarTile(
                    station, npcCol, npcRow, out _targetCol, out _targetRow))
                    return;
                _path = NPCPathfinder.FindPath(station, npcCol, npcRow, _targetCol, _targetRow);
                _pathIndex = 0;
                if (_path == null) return;
            }

            if (_pathIndex < _path.Count)
            {
                if (!NPCTaskHelpers.TryAdvancePathStep(station,
                        ref _path, ref _pathIndex,
                        npcCol, npcRow, _targetCol, _targetRow,
                        out var step))
                {
                    _targetCol = int.MinValue;
                    _targetRow = int.MinValue;
                    _path = null;
                    _pathIndex = 0;
                    return;
                }
                npc.location = $"{step.Col}_{step.Row}";
            }
        }
    }

    /// <summary>
    /// Generic visitor movement task: navigate to the nearest tile of a target room type,
    /// then wait in-place for the configured dwell period.
    /// </summary>
    public class VisitRoomTask : NPCTask
    {
        public string TargetRoomTypeId { get; }
        public int DwellTicks { get; }

        private string _phase = "walk";
        private List<PathStep> _path;
        private int _pathIndex = 0;
        private int _targetCol = int.MinValue;
        private int _targetRow = int.MinValue;
        private int _elapsedDwell = 0;

        public VisitRoomTask(string targetRoomTypeId, int dwellTicks = 8)
        {
            TargetRoomTypeId = targetRoomTypeId;
            DwellTicks = Mathf.Max(1, dwellTicks);
        }

        public override void Tick(NPCInstance npc, StationState station)
        {
            if (Status == NPCTaskStatus.Succeeded || Status == NPCTaskStatus.Failed) return;
            Status = NPCTaskStatus.InProgress;

            NPCTaskHelpers.ParseLocation(npc.location, out int npcCol, out int npcRow);

            if (_phase == "walk")
            {
                if (_targetCol == int.MinValue)
                {
                    if (!NPCTaskHelpers.TryFindNearestReachableRoomTile(
                        station, npcCol, npcRow, TargetRoomTypeId, out _targetCol, out _targetRow))
                    {
                        Status = NPCTaskStatus.Failed;
                        return;
                    }
                }

                if (npcCol == _targetCol && npcRow == _targetRow)
                {
                    _phase = "dwell";
                    npc.memory["visitor_room_target"] = TargetRoomTypeId;
                    return;
                }

                if (NPCTaskHelpers.TryAdvancePathStep(station,
                        ref _path, ref _pathIndex,
                        npcCol, npcRow, _targetCol, _targetRow,
                        out var step))
                {
                    npc.location = $"{step.Col}_{step.Row}";
                }
                else
                {
                    Status = NPCTaskStatus.Failed;
                }
                return;
            }

            _elapsedDwell++;
            if (_elapsedDwell >= DwellTicks)
                Status = NPCTaskStatus.Succeeded;
        }
    }

    /// <summary>
    /// Visitor departure movement: walk back to a ship-side tile (typically the landing pad)
    /// and mark the NPC ready to depart.
    /// </summary>
    public class ReturnToShipTask : NPCTask
    {
        private readonly int _fallbackCol;
        private readonly int _fallbackRow;
        private List<PathStep> _path;
        private int _pathIndex = 0;
        private int _targetCol = int.MinValue;
        private int _targetRow = int.MinValue;

        public ReturnToShipTask(int fallbackCol, int fallbackRow)
        {
            _fallbackCol = fallbackCol;
            _fallbackRow = fallbackRow;
        }

        public override void Tick(NPCInstance npc, StationState station)
        {
            if (Status == NPCTaskStatus.Succeeded || Status == NPCTaskStatus.Failed) return;
            Status = NPCTaskStatus.InProgress;

            NPCTaskHelpers.ParseLocation(npc.location, out int npcCol, out int npcRow);

            if (_targetCol == int.MinValue)
            {
                if (!NPCTaskHelpers.TryResolveVisitorShipTile(
                    npc, station, _fallbackCol, _fallbackRow, out _targetCol, out _targetRow))
                {
                    Status = NPCTaskStatus.Failed;
                    return;
                }
            }

            if (npcCol == _targetCol && npcRow == _targetRow)
            {
                npc.memory["visitor_ready_to_depart"] = true;
                Status = NPCTaskStatus.Succeeded;
                return;
            }

            if (NPCTaskHelpers.TryAdvancePathStep(station,
                    ref _path, ref _pathIndex,
                    npcCol, npcRow, _targetCol, _targetRow,
                    out var step))
            {
                npc.location = $"{step.Col}_{step.Row}";
            }
            else
            {
                Status = NPCTaskStatus.Failed;
            }
        }
    }

    /// <summary>
    /// Inspector scan task — rolls inspector awareness against concealment difficulty
    /// for each contraband-bearing cargo container in cargo_hold rooms.
    /// </summary>
    public class InspectorScanTask : NPCTask
    {
        private readonly ContentRegistry _registry;
        private readonly EventSystem _eventSystem;
        private readonly InventorySystem _inventorySystem;
        private readonly int _baseDifficulty;
        private readonly float _creditPenalty;
        private readonly float _reputationPenalty;
        private bool _completed = false;

        public InspectorScanTask(
            ContentRegistry registry,
            EventSystem eventSystem,
            InventorySystem inventorySystem,
            int baseDifficulty,
            float creditPenalty,
            float reputationPenalty)
        {
            _registry = registry;
            _eventSystem = eventSystem;
            _inventorySystem = inventorySystem;
            _baseDifficulty = Mathf.Max(1, baseDifficulty);
            _creditPenalty = Mathf.Max(0f, creditPenalty);
            _reputationPenalty = Mathf.Min(0f, reputationPenalty);
        }

        public override void Tick(NPCInstance npc, StationState station)
        {
            if (_completed)
            {
                Status = NPCTaskStatus.Succeeded;
                return;
            }

            Status = NPCTaskStatus.InProgress;

            var containers = GatherCargoContainers(station);
            bool anyDetection = false;

            int concealmentDc = ComputeConcealmentDifficulty(npc, station);
            foreach (var container in containers)
            {
                if (!ContainsContraband(container)) continue;

                int wisMod = AbilityScores.GetModifier(npc.abilityScores.WIS);
                int investigation = npc.skills.TryGetValue("investigation", out var inv) ? inv : 0;
                int roll = UnityEngine.Random.Range(1, 21) + wisMod + investigation;

                if (roll >= concealmentDc)
                {
                    anyDetection = true;
                    ApplyContrabandPenalty(npc, station);
                    break;
                }
            }

            npc.memory["inspector_scan_detected"] = anyDetection;
            npc.memory["inspector_scan_complete"] = true;
            _completed = true;
            Status = NPCTaskStatus.Succeeded;
        }

        private List<FoundationInstance> GatherCargoContainers(StationState station)
        {
            if (_inventorySystem != null)
                return _inventorySystem.GetCargoHoldContainers(station);

            var result = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.cargoCapacity <= 0) continue;
                if (!NPCTaskHelpers.IsFoundationInRoomType(station, f, "cargo_hold")) continue;
                result.Add(f);
            }
            return result;
        }

        private bool ContainsContraband(FoundationInstance container)
        {
            foreach (var kv in container.cargo)
            {
                if (kv.Value <= 0) continue;
                if (!_registry.Items.TryGetValue(kv.Key, out var item)) continue;
                if (!item.legal || item.tags.Contains("contraband")) return true;
            }
            return false;
        }

        private int ComputeConcealmentDifficulty(NPCInstance inspector, StationState station)
        {
            int concealerSkill = 0;
            if (inspector.memory.TryGetValue("visitor_ship_uid", out var shipObj))
            {
                string shipUid = shipObj?.ToString();
                if (!string.IsNullOrEmpty(shipUid) &&
                    station.ships.TryGetValue(shipUid, out var ship))
                {
                    foreach (var uid in ship.passengerUids)
                    {
                        if (!station.npcs.TryGetValue(uid, out var npc)) continue;
                        if (npc.uid == inspector.uid) continue;
                        if (npc.skills.TryGetValue("deception", out var deception))
                            concealerSkill = Mathf.Max(concealerSkill, deception);
                    }
                }
            }
            return _baseDifficulty + concealerSkill;
        }

        private void ApplyContrabandPenalty(NPCInstance inspector, StationState station)
        {
            station.ModifyResource("credits", -_creditPenalty);

            string shipUid = inspector.memory.TryGetValue("visitor_ship_uid", out var shipObj)
                ? shipObj?.ToString()
                : null;

            string shipName = "Unknown ship";
            string factionId = null;
            if (!string.IsNullOrEmpty(shipUid) && station.ships.TryGetValue(shipUid, out var ship))
            {
                shipName = ship.name;
                factionId = ship.factionId;
            }

            if (!string.IsNullOrEmpty(factionId))
                station.ModifyFactionRep(factionId, _reputationPenalty);

            _eventSystem?.QueueEvent("event.contraband_found",
                new Dictionary<string, object>
                {
                    { "ship_uid", shipUid ?? "" },
                    { "ship_name", shipName },
                    { "credits_penalty", _creditPenalty },
                    { "reputation_penalty", _reputationPenalty },
                    { "inspector_uid", inspector.uid }
                });
        }
    }

    /// <summary>
    /// Marks a visitor as complete once all prior tasks in their queue are done.
    /// </summary>
    public class MarkVisitCompleteTask : NPCTask
    {
        private bool _marked = false;

        public override void Tick(NPCInstance npc, StationState station)
        {
            if (!_marked)
            {
                npc.memory["visitor_visit_complete"] = true;
                _marked = true;
                Status = NPCTaskStatus.InProgress;
                return;
            }

            Status = NPCTaskStatus.Succeeded;
        }
    }

    internal static class NPCTaskHelpers
    {
        public static void ParseLocation(string location, out int col, out int row)
        {
            col = 0; row = 0;
            if (string.IsNullOrEmpty(location)) return;
            int sep = location.IndexOf('_');
            if (sep < 0) return;
            int.TryParse(location.Substring(0, sep), out col);
            int.TryParse(location.Substring(sep + 1), out row);
        }

        public static bool TryFindNearestReachableRoomTile(
            StationState station, int fromCol, int fromRow, string roomTypeId,
            out int bestCol, out int bestRow)
        {
            bestCol = 0;
            bestRow = 0;
            int bestDist = int.MaxValue;

            foreach (var tile in EnumerateTilesForRoomType(station, roomTypeId))
            {
                int dist = Math.Abs(tile.col - fromCol) + Math.Abs(tile.row - fromRow);
                if (dist >= bestDist) continue;
                var path = NPCPathfinder.FindPath(station, fromCol, fromRow, tile.col, tile.row);
                if (path == null) continue;
                bestDist = dist;
                bestCol = tile.col;
                bestRow = tile.row;
            }

            return bestDist != int.MaxValue;
        }

        public static bool TryPickRandomReachableHangarTile(
            StationState station, int fromCol, int fromRow, out int col, out int row)
        {
            col = fromCol;
            row = fromRow;
            var candidates = new List<(int col, int row)>();
            foreach (var t in EnumerateTilesForRoomType(station, "hangar"))
                candidates.Add(t);

            if (candidates.Count == 0)
            {
                foreach (var f in station.foundations.Values)
                    if (f.buildableId == "buildable.shuttle_landing_pad" && f.status == "complete")
                        candidates.Add((f.tileCol, f.tileRow));
            }

            if (candidates.Count == 0) return false;

            for (int i = 0; i < Mathf.Min(8, candidates.Count); i++)
            {
                var candidate = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                var path = NPCPathfinder.FindPath(station, fromCol, fromRow, candidate.col, candidate.row);
                if (path == null) continue;
                col = candidate.col;
                row = candidate.row;
                return true;
            }

            return TryFindNearestReachableRoomTile(station, fromCol, fromRow, "hangar", out col, out row);
        }

        public static bool TryResolveVisitorShipTile(
            NPCInstance npc, StationState station, int fallbackCol, int fallbackRow, out int col, out int row)
        {
            col = fallbackCol;
            row = fallbackRow;

            if (npc.memory.TryGetValue("visitor_ship_tile", out var stored) &&
                stored is string storedLocation && !string.IsNullOrEmpty(storedLocation))
            {
                ParseLocation(storedLocation, out col, out row);
                return true;
            }

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.shuttle_landing_pad" || f.status != "complete") continue;
                col = f.tileCol;
                row = f.tileRow;
                return true;
            }

            return true;
        }

        public static bool IsFoundationInRoomType(
            StationState station, FoundationInstance foundation, string roomTypeId)
        {
            string tileKey = $"{foundation.tileCol}_{foundation.tileRow}";
            if (!station.tileToRoomKey.TryGetValue(tileKey, out var roomKey)) return false;
            if (!station.playerRoomTypeAssignments.TryGetValue(roomKey, out var assignedType)) return false;
            return assignedType == roomTypeId;
        }

        public static bool TryAdvancePathStep(
            StationState station,
            ref List<PathStep> path,
            ref int pathIndex,
            int fromCol,
            int fromRow,
            int targetCol,
            int targetRow,
            out PathStep step)
        {
            step = default;

            if (path == null || pathIndex >= path.Count)
            {
                path = NPCPathfinder.FindPath(station, fromCol, fromRow, targetCol, targetRow);
                pathIndex = 0;
                if (path == null || path.Count == 0) return false;
            }

            var candidate = path[pathIndex];
            if (IsBlocked(station, candidate.Col, candidate.Row))
            {
                path = NPCPathfinder.FindPath(station, fromCol, fromRow, targetCol, targetRow);
                pathIndex = 0;
                if (path == null || path.Count == 0) return false;
                candidate = path[pathIndex];
                if (IsBlocked(station, candidate.Col, candidate.Row)) return false;
            }

            pathIndex++;
            step = candidate;
            return true;
        }

        public static bool IsBlocked(StationState station, int col, int row)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.tileLayer < 2) continue;
                if (f.buildableId.Contains("door")) continue;
                for (int dc = 0; dc < f.tileWidth; dc++)
                for (int dr = 0; dr < f.tileHeight; dr++)
                    if (f.tileCol + dc == col && f.tileRow + dr == row) return true;
            }
            return false;
        }

        private static IEnumerable<(int col, int row)> EnumerateTilesForRoomType(
            StationState station, string roomTypeId)
        {
            foreach (var assignment in station.playerRoomTypeAssignments)
            {
                if (assignment.Value != roomTypeId) continue;
                bool yieldedAny = false;

                foreach (var tile in station.tileToRoomKey)
                {
                    if (tile.Value != assignment.Key) continue;
                    ParseLocation(tile.Key, out int tc, out int tr);
                    yieldedAny = true;
                    yield return (tc, tr);
                }

                if (!yieldedAny)
                {
                    ParseLocation(assignment.Key, out int rc, out int rr);
                    yield return (rc, rr);
                }
            }
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
            // If there's an active task, continue it or clean up if it already finished.
            if (_activeTasks.TryGetValue(npc.uid, out var current))
            {
                if (current.Status == NPCTaskStatus.Pending ||
                    current.Status == NPCTaskStatus.InProgress)
                {
                    current.Tick(npc, station);
                }

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
