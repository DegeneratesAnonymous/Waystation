// RoomSystem.cs
// Evaluates room bonus conditions every 10 ticks.
// A "bonus room" requires:
//   1. One or more workbenches of the same workbenchRoomType in the room.
//   2. The room's non-workbench objects have enough combined beautyScore.
//   3. The room has enough non-workbench, non-floor, non-wall foundations.
//   4. At most 3 workbenches of that type (no bonus granted above the cap, but
//      existing workbenches up to the cap still receive the bonus).
//
// When all requirements are met, every qualifying workbench in the room has
// hasRoomBonus=true and roomBonusMultiplier set from workbenchSkillBonuses.
// The multiplier is the MAXIMUM single-skill value (so one number drives all skill
// modifiers; individual skill lookups happen in the calling system).
//
// The RoomBonusState cache in StationState.roomBonusCache is also populated so
// the UI can display progress.

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

            // Collect all foundations within the room tiles
            var tileSet        = new HashSet<(int, int)>(tiles);
            var roomFoundations = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
                if (tileSet.Contains((f.tileCol, f.tileRow)))
                    roomFoundations.Add(f);

            // Find all typed workbenches
            var workbenches = roomFoundations
                .Where(f => GetDef(f) is { } d && d.workbenchRoomType != null)
                .ToList();

            if (workbenches.Count == 0) return;

            // Group by workbench type — pick the type with the most representatives
            var groups = workbenches
                .GroupBy(f => GetDef(f).workbenchRoomType)
                .OrderByDescending(g => g.Count())
                .ToList();

            // Build canonical room key from the minimum tile position
            int minC = tiles.Min(t => t.col);
            int minR = tiles.Min(t => t.row);
            string roomKey = $"{minC}_{minR}";

            foreach (var group in groups)
            {
                string roomType = group.Key;
                var    benches  = group.ToList();

                // Look up the RoomTypeDefinition — registry first, then custom
                RoomTypeDefinition typeDef = null;
                if (_registry?.RoomTypes != null)
                    _registry.RoomTypes.TryGetValue(roomType, out typeDef);
                if (typeDef == null)
                    typeDef = station.customRoomTypes.FirstOrDefault(t => t.id == roomType);
                if (typeDef == null) continue;

                int cap              = typeDef.workbenchCap;
                var eligibleBenches  = benches.Take(cap).ToList();
                int workbenchCount   = benches.Count;
                int eligibleCount    = eligibleBenches.Count;

                // Non-workbench, non-floor/wall foundations available as furniture
                var furnitureFoundations = roomFoundations
                    .Where(f => !IsFloorOrWall(f) &&
                                !(GetDef(f) is { } d && d.workbenchRoomType != null))
                    .ToList();

                // Evaluate each requirement slot
                var progressList = new List<RoomRequirementProgress>();
                bool allMet      = true;

                foreach (var req in typeDef.requirementsPerWorkbench)
                {
                    int required = req.countPerWorkbench * eligibleCount;
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

                bool bonusActive = allMet && typeDef.requirementsPerWorkbench.Count > 0;

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

                // Store state in cache for UI display
                string cacheKey = $"{roomKey}_{roomType}";
                station.roomBonusCache[cacheKey] = new RoomBonusState
                {
                    roomKey           = roomKey,
                    workbenchRoomType = roomType,
                    displayName       = typeDef.displayName,
                    bonusActive       = bonusActive,
                    workbenchCount    = workbenchCount,
                    workbenchUids     = eligibleBenches.Select(b => b.uid).ToList(),
                    requirements      = progressList,
                };
            }
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
