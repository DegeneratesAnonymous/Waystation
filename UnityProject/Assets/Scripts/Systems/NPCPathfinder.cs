// NPC Pathfinder — A* grid pathfinding over the station tile grid.
//
// Walkable = any tile that has a floor foundation but no solid blocking object
// on an upper layer (tileLayer >= 2 and not a door).
// Movement is 4-directional (no diagonals).
//
// Usage:
//   var path = NPCPathfinder.FindPath(station, startCol, startRow, goalCol, goalRow);
//   Returns null if no path exists (goal is unreachable).
//   Returns an empty list if start == goal.
//
// NPCPathfinder is a static utility class; it does not hold per-NPC state.
// Per-NPC path progress is managed by NPCTaskQueue / NPCInstance fields.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>A single step in a computed path.</summary>
    public readonly struct PathStep
    {
        public readonly int Col;
        public readonly int Row;
        public PathStep(int col, int row) { Col = col; Row = row; }
        public override string ToString() => $"({Col},{Row})";
    }

    public static class NPCPathfinder
    {
        // Toggle to disable all NPC movement without removing the class
        public static bool Enabled = true;

        private static readonly (int dc, int dr)[] Neighbours =
        {
            (0,  1), (0, -1), (1, 0), (-1, 0)  // N S E W — no diagonals
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Compute the shortest walkable path from (startCol, startRow) to
        /// (goalCol, goalRow) on the station tile grid using A*.
        /// Returns the path as an ordered list of PathSteps (excluding start, including goal),
        /// or null if no path exists.
        /// Returns an empty list when start == goal.
        /// </summary>
        public static List<PathStep> FindPath(StationState station,
                                               int startCol, int startRow,
                                               int goalCol,  int goalRow)
        {
            if (!Enabled) return null;

            if (startCol == goalCol && startRow == goalRow)
                return new List<PathStep>();

            // Build walkability sets from the station foundations
            var walkable  = BuildWalkableSet(station);
            var blocked   = BuildBlockedSet(station);

            // A tile is passable if it is in the walkable set AND not blocked
            bool IsPassable(int c, int r)
            {
                var pos = (c, r);
                return walkable.Contains(pos) && !blocked.Contains(pos);
            }

            // Validate start and goal
            var startPos = (startCol, startRow);
            var goalPos  = (goalCol,  goalRow);

            // If start isn't walkable, try to path anyway (NPC may be on a door tile etc.)
            if (!IsPassable(goalCol, goalRow))
            {
                Debug.LogWarning($"[NPCPathfinder] Goal ({goalCol},{goalRow}) is not walkable; no path.");
                return null;
            }

            // ── A* ────────────────────────────────────────────────────────────
            var openSet   = new SortedDictionary<float, List<(int, int)>>();
            var cameFrom  = new Dictionary<(int, int), (int, int)>();
            var gScore    = new Dictionary<(int, int), float>();
            var fScore    = new Dictionary<(int, int), float>();
            var closed    = new HashSet<(int, int)>();

            float h0 = Heuristic(startCol, startRow, goalCol, goalRow);
            gScore[startPos] = 0f;
            fScore[startPos] = h0;
            Enqueue(openSet, h0, startPos);

            int iterations = 0;
            const int MaxIterations = 10000;

            while (openSet.Count > 0 && iterations++ < MaxIterations)
            {
                var current = Dequeue(openSet);
                if (current == goalPos)
                    return ReconstructPath(cameFrom, current);

                closed.Add(current);
                var (cc, cr) = current;

                foreach (var (dc, dr) in Neighbours)
                {
                    int nc = cc + dc, nr = cr + dr;
                    var neighbour = (nc, nr);

                    if (closed.Contains(neighbour)) continue;
                    // Allow starting on a non-walkable tile (e.g., mid-placement NPC)
                    if (current != startPos && !IsPassable(nc, nr)) continue;
                    if (current == startPos && !IsPassable(nc, nr)) continue;

                    float tentativeG = (gScore.ContainsKey(current) ? gScore[current] : float.MaxValue) + 1f;

                    if (!gScore.ContainsKey(neighbour) || tentativeG < gScore[neighbour])
                    {
                        cameFrom[neighbour] = current;
                        gScore[neighbour]   = tentativeG;
                        float f = tentativeG + Heuristic(nc, nr, goalCol, goalRow);
                        fScore[neighbour]   = f;
                        Enqueue(openSet, f, neighbour);
                    }
                }
            }

            // No path found
            Debug.LogWarning($"[NPCPathfinder] No walkable path from ({startCol},{startRow}) " +
                              $"to ({goalCol},{goalRow}) — NPC stays in place.");
            return null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Build a set of tile positions that have at least one floor-layer foundation
        /// (tileLayer == 1) and are not a wall.
        /// </summary>
        private static HashSet<(int, int)> BuildWalkableSet(StationState station)
        {
            var walkable = new HashSet<(int, int)>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                // Layer 1 = floor, 1 = structural floor — doors are also walkable
                if (f.tileLayer == 1 &&
                    !f.buildableId.Contains("wall"))
                    walkable.Add((f.tileCol, f.tileRow));
            }
            return walkable;
        }

        /// <summary>
        /// Build a set of tile positions blocked by a solid object (tileLayer >= 2,
        /// not a door, complete, occupying the same tile).
        /// </summary>
        private static HashSet<(int, int)> BuildBlockedSet(StationState station)
        {
            var blocked = new HashSet<(int, int)>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.tileLayer < 2) continue;         // floor-level never blocks
                if (f.buildableId.Contains("door")) continue; // doors passable
                // Add all tiles in the foundation's footprint
                for (int dc = 0; dc < f.tileWidth;  dc++)
                for (int dr = 0; dr < f.tileHeight; dr++)
                    blocked.Add((f.tileCol + dc, f.tileRow + dr));
            }
            return blocked;
        }

        private static float Heuristic(int ac, int ar, int bc, int br)
            => Mathf.Abs(ac - bc) + Mathf.Abs(ar - br);  // Manhattan distance

        private static void Enqueue(SortedDictionary<float, List<(int, int)>> open,
                                     float f, (int, int) node)
        {
            if (!open.TryGetValue(f, out var list))
            {
                list = new List<(int, int)>();
                open[f] = list;
            }
            list.Add(node);
        }

        private static (int, int) Dequeue(SortedDictionary<float, List<(int, int)>> open)
        {
            using var e = open.GetEnumerator();
            e.MoveNext();
            float key  = e.Current.Key;
            var   list = e.Current.Value;
            var   node = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0) open.Remove(key);
            return node;
        }

        private static List<PathStep> ReconstructPath(
            Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
        {
            var path = new List<PathStep>();
            while (cameFrom.ContainsKey(current))
            {
                path.Add(new PathStep(current.Item1, current.Item2));
                current = cameFrom[current];
            }
            path.Reverse();
            return path;
        }
    }
}
