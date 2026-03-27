// RoomSystem.cs
// Evaluates room bonus conditions every 10 ticks.
// Bonuses are only applied to rooms that have a player-assigned room type
// (StationState.playerRoomTypeAssignments).  Rooms without an assignment
// receive no bonus — this is the explicit null state required by the
// acceptance criteria.
//
// Auto-suggest: the dominant workbench type in a room is surfaced in
// RoomBonusState.autoSuggestedRoomType as a non-binding hint for the UI.
//
// A bonus room requires:
//   1. A player-assigned room type whose workbenchCap > 0.
//   2. One or more workbenches whose workbenchRoomType matches the assignment.
//   3. The room's non-workbench furniture meets the type's requirementsPerWorkbench.
//   4. At most <workbenchCap> workbenches earn the bonus; extras above the cap
//      still exist but do not receive hasRoomBonus.
//
// When all requirements are met, every qualifying workbench foundation has
// hasRoomBonus=true and roomBonusMultiplier set from the type's skillBonuses
// (maximum single-skill value drives roomBonusMultiplier; per-skill lookup
// happens in the consuming system).
//
// Layout change hooks: RebuildBonusCache() is called directly by GameHUD after
// any wall/door/workbench placement or removal, and by BuildingSystem (via
// GameManager) when a workbench foundation completes construction.

using System.Collections.Generic;
using System.Linq;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class RoomSystem
    {
        private readonly ContentRegistry _registry;
        private const int TickInterval = 10;        // evaluate every N game ticks

        public RoomSystem(ContentRegistry registry)
        {
            _registry = registry;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station.tick % TickInterval != 0) return;
            RebuildBonusCache(station);
        }

        /// <summary>
        /// Assign a room type to a room and immediately rebuild the bonus cache.
        /// Pass null or empty string to remove the assignment (room receives no bonus).
        /// </summary>
        public void AssignRoomType(StationState station, string roomKey, string roomTypeId)
        {
            if (string.IsNullOrEmpty(roomTypeId))
                station.playerRoomTypeAssignments.Remove(roomKey);
            else
                station.playerRoomTypeAssignments[roomKey] = roomTypeId;

            RebuildBonusCache(station);
        }

        /// <summary>
        /// Returns the auto-suggested room type for the given room key (dominant
        /// workbench type in the room), or null if the room has no typed workbenches.
        /// The suggestion is non-binding — it is only used to pre-populate the UI.
        /// </summary>
        public string GetAutoSuggest(StationState station, string roomKey)
        {
            if (station.roomBonusCache.TryGetValue(roomKey, out var bs))
                return bs.autoSuggestedRoomType;
            return null;
        }

        /// Force an immediate cache rebuild (call on game load).
        public void RebuildBonusCache(StationState station)
        {
            // Clear all existing bonus flags on workbench foundations
            foreach (var f in station.foundations.Values)
            {
                f.hasRoomBonus        = false;
                f.roomBonusMultiplier = 1.0f;
            }

            station.roomBonusCache.Clear();
            station.tileToRoomKey.Clear();

            // Flood-fill each unvisited floor tile to discover rooms
            var visited  = new HashSet<(int c, int r)>();
            foreach (var f in station.foundations.Values)
            {
                if (!IsFloorLike(f)) continue;
                var pos = (f.tileCol, f.tileRow);
                if (visited.Contains(pos)) continue;

                var roomTiles = FloodFillRoom(station, f.tileCol, f.tileRow);
                foreach (var t in roomTiles) visited.Add(t);

                // Compute canonical room key and populate the reverse mapping
                if (roomTiles.Count > 0)
                {
                    int minC = int.MaxValue, minR = int.MaxValue;
                    foreach (var t in roomTiles)
                    {
                        if (t.col < minC) minC = t.col;
                        if (t.row < minR) minR = t.row;
                    }
                    string roomKey = $"{minC}_{minR}";
                    foreach (var t in roomTiles)
                        station.tileToRoomKey[$"{t.col}_{t.row}"] = roomKey;
                }

                EvaluateRoom(station, roomTiles);
            }

            ClassifyGreenhouseRooms(station);
        }

        /// Returns all floor tiles connected to (startCol, startRow) via BFS,
        /// respecting placed walls as barriers and placed doors as pass-throughs.
        /// Public so GameHUD can reuse it instead of maintaining its own copy.
        public List<(int col, int row)> FloodFillRoom(StationState station,
                                                       int startCol, int startRow)
        {
            var result   = new List<(int, int)>();
            var queue    = new Queue<(int, int)>();
            var seen     = new HashSet<(int, int)>();

            // Build fast lookup sets from foundations
            var wallSet  = new HashSet<(int, int)>();
            var doorSet  = new HashSet<(int, int)>();
            var floorSet = new HashSet<(int, int)>();

            foreach (var f in station.foundations.Values)
            {
                var pos = (f.tileCol, f.tileRow);
                if (f.tileLayer == 1 && f.buildableId.Contains("wall") &&
                    !f.buildableId.Contains("door"))
                    wallSet.Add(pos);
                else if (f.buildableId.Contains("door"))
                    doorSet.Add(pos);
                else
                    floorSet.Add(pos);
            }

            // Start tile must be floor-like (has a floor foundation or is in interior default)
            if (!floorSet.Contains((startCol, startRow)) &&
                !doorSet.Contains((startCol, startRow)))
                return result;

            queue.Enqueue((startCol, startRow));
            seen.Add((startCol, startRow));

            var offsets = new[] { (0, 1), (0, -1), (1, 0), (-1, 0) };
            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                result.Add((c, r));

                foreach (var (dc, dr) in offsets)
                {
                    int nc = c + dc, nr = r + dr;
                    var np = (nc, nr);
                    if (seen.Contains(np)) continue;
                    seen.Add(np);

                    if (wallSet.Contains(np)) continue;          // wall blocks BFS
                    if (floorSet.Contains(np) || doorSet.Contains(np))
                        queue.Enqueue(np);
                }
            }

            return result;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Any sealed room containing at least one Hydroponics Planter Tile is
        /// classified as a Greenhouse and stored in station.roomRoles using the
        /// stable id "greenhouse" (matches the RoomRoles entry in GameHUD).
        /// Rooms that lose all planters have the greenhouse designation removed.
        /// </summary>
        private void ClassifyGreenhouseRooms(StationState station)
        {
            // Build set of room keys that contain planters
            var greenhouseKeys = new HashSet<string>();
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;
                string tileKey = $"{f.tileCol}_{f.tileRow}";
                if (station.tileToRoomKey.TryGetValue(tileKey, out var roomKey))
                    greenhouseKeys.Add(roomKey);
            }

            // Remove old greenhouse designations for rooms that no longer qualify
            var toRemove = new System.Collections.Generic.List<string>();
            foreach (var kv in station.roomRoles)
                if (kv.Value == "greenhouse" && !greenhouseKeys.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            foreach (var k in toRemove) station.roomRoles.Remove(k);

            // Apply greenhouse to qualifying rooms
            foreach (var key in greenhouseKeys)
                station.roomRoles[key] = "greenhouse";
        }

        private void EvaluateRoom(StationState station, List<(int col, int row)> tiles)
        {
            if (tiles.Count == 0) return;

            // Build canonical room key from the minimum tile position
            int minC = tiles.Min(t => t.col);
            int minR = tiles.Min(t => t.row);
            string roomKey = $"{minC}_{minR}";

            // Collect all foundations within the room tiles
            var tileSet         = new HashSet<(int, int)>(tiles);
            var roomFoundations = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
                if (tileSet.Contains((f.tileCol, f.tileRow)))
                    roomFoundations.Add(f);

            // Identify all typed workbenches (complete only)
            var workbenches = roomFoundations
                .Where(f => f.status == "complete" &&
                            GetDef(f) is { } d && d.workbenchRoomType != null)
                .ToList();

            // Compute auto-suggest: dominant workbench type (non-binding)
            string autoSuggest = null;
            if (workbenches.Count > 0)
            {
                autoSuggest = workbenches
                    .GroupBy(f => GetDef(f).workbenchRoomType)
                    .OrderByDescending(g => g.Count())
                    .First().Key;
            }

            // Check whether the player has assigned a room type
            station.playerRoomTypeAssignments.TryGetValue(roomKey, out string assignedTypeId);

            if (string.IsNullOrEmpty(assignedTypeId))
            {
                // No player assignment → no bonus, but store auto-suggest so UI can show hint
                if (autoSuggest != null || workbenches.Count > 0)
                {
                    station.roomBonusCache[roomKey] = new RoomBonusState
                    {
                        roomKey               = roomKey,
                        workbenchRoomType     = null,
                        displayName           = null,
                        bonusActive           = false,
                        workbenchCount        = workbenches.Count,
                        autoSuggestedRoomType = autoSuggest,
                        workbenchUids         = workbenches.Select(b => b.uid).ToList(),
                        requirements          = new List<RoomRequirementProgress>(),
                    };
                }
                return;
            }

            // Resolve assigned type definition
            RoomTypeDefinition typeDef = null;
            if (_registry?.RoomTypes != null)
                _registry.RoomTypes.TryGetValue(assignedTypeId, out typeDef);
            if (typeDef == null)
                typeDef = station.customRoomTypes.FirstOrDefault(t => t.id == assignedTypeId);
            if (typeDef == null) return;

            // Workbenches that match the assigned type
            var matchingBenches = workbenches
                .Where(f => GetDef(f)?.workbenchRoomType == assignedTypeId)
                .ToList();

            int cap             = typeDef.workbenchCap;
            // workbench_cap == 0: this type grants bonuses from room designation alone
            // (e.g. medical_bay). Eligible bench list is unused for bonus activation.
            bool workbenchRequired = cap > 0;
            var eligibleBenches = workbenchRequired ? matchingBenches.Take(cap).ToList()
                                                    : new List<FoundationInstance>();
            int workbenchCount  = matchingBenches.Count;
            int eligibleCount   = eligibleBenches.Count;

            // Non-workbench, non-floor/wall foundations available as furniture
            var furnitureFoundations = roomFoundations
                .Where(f => !IsFloorOrWall(f) &&
                            !(GetDef(f) is { } d && d.workbenchRoomType != null))
                .ToList();

            // Evaluate each requirement slot
            var progressList = new List<RoomRequirementProgress>();
            bool allMet       = true;

            foreach (var req in typeDef.requirementsPerWorkbench)
            {
                // For workbench-required types: scale by eligible bench count.
                // For designation-only types (cap == 0): count per slot is 1.
                int required = req.countPerWorkbench * (workbenchRequired
                    ? (eligibleCount > 0 ? eligibleCount : 1)
                    : 1);
                int current  = furnitureFoundations
                    .Count(f => MatchesRequirement(f, req.buildableIdOrTag));

                var prog = new RoomRequirementProgress
                {
                    displayLabel = req.displayLabel,
                    current      = current,
                    required     = required,
                };
                progressList.Add(prog);
                if (!prog.IsMet) allMet = false;
            }

            // Bonus is active:
            //   • Workbench-required types: need ≥1 eligible workbench + all furniture met.
            //   • Designation-only types (cap == 0): active when all requirements met
            //     (or no requirements) — the room designation itself provides the bonus.
            bool bonusActive;
            if (workbenchRequired)
                bonusActive = eligibleCount > 0 &&
                              (typeDef.requirementsPerWorkbench.Count == 0 || allMet);
            else
                bonusActive = typeDef.requirementsPerWorkbench.Count == 0 || allMet;

            // Compute multiplier from the type's skill bonus map
            float multiplier = 1.0f;
            if (bonusActive && typeDef.skillBonuses.Count > 0)
                multiplier = typeDef.skillBonuses.Values.Max();

            // Apply flags to each qualifying workbench foundation
            if (bonusActive)
            {
                foreach (var bench in eligibleBenches)
                {
                    bench.hasRoomBonus        = true;
                    bench.roomBonusMultiplier = multiplier;
                }
            }

            // Store state in cache (keyed by roomKey so one entry per room when assigned)
            station.roomBonusCache[roomKey] = new RoomBonusState
            {
                roomKey               = roomKey,
                workbenchRoomType     = assignedTypeId,
                displayName           = typeDef.displayName,
                bonusActive           = bonusActive,
                workbenchCount        = workbenchCount,
                autoSuggestedRoomType = autoSuggest,
                workbenchUids         = eligibleBenches.Select(b => b.uid).ToList(),
                requirements          = progressList,
            };
        }

        private BuildableDefinition GetDef(FoundationInstance f)
        {
            if (_registry?.Buildables == null) return null;
            _registry.Buildables.TryGetValue(f.buildableId, out var def);
            return def;
        }

        private static bool IsFloorLike(FoundationInstance f)
        {
            return f.tileLayer == 1 &&
                   !f.buildableId.Contains("wall") &&
                   !f.buildableId.Contains("door");
        }

        private static bool IsFloorOrWall(FoundationInstance f)
        {
            return f.buildableId.Contains("floor") ||
                   f.buildableId.Contains("wall")  ||
                   f.buildableId.Contains("door");
        }

        private bool MatchesRequirement(FoundationInstance f, string buildableIdOrTag)
        {
            if (string.IsNullOrEmpty(buildableIdOrTag)) return false;
            if (buildableIdOrTag.StartsWith("tag:"))
            {
                string tag = buildableIdOrTag.Substring(4);
                return GetDef(f)?.furnitureTag == tag;
            }
            return f.buildableId == buildableIdOrTag;
        }
    }
}
