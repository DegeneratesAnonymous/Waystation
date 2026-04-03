// SpatialHelpers — shared tile-distance utilities.
//
// Provides Manhattan/Chebyshev distance and radius queries for tile-based
// systems (InteractionSystem, Resonant mood bleed, etc.).
using System;
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class SpatialHelpers
    {
        /// <summary>
        /// Chebyshev (chessboard) distance between two tile positions.
        /// Returns max of absolute column and row differences.
        /// </summary>
        public static int TileDistance(int colA, int rowA, int colB, int rowB)
        {
            return Math.Max(Math.Abs(colA - colB), Math.Abs(rowA - rowB));
        }

        /// <summary>
        /// Returns all crew NPCs within a Chebyshev tile radius of a given NPC.
        /// Excludes the origin NPC itself.
        /// </summary>
        public static List<NPCInstance> GetNPCsWithinRadius(
            NPCInstance origin, int radius, StationState station)
        {
            var result = new List<NPCInstance>();
            foreach (var npc in station.npcs.Values)
            {
                if (npc.uid == origin.uid) continue;
                if (!npc.IsCrew() && !npc.IsVisitor()) continue;
                int dist = TileDistance(origin.tileCol, origin.tileRow,
                                       npc.tileCol, npc.tileRow);
                if (dist <= radius) result.Add(npc);
            }
            return result;
        }

        /// <summary>
        /// Returns all crew NPCs within a Chebyshev tile radius of a tile position.
        /// </summary>
        public static List<NPCInstance> GetNPCsWithinRadius(
            int col, int row, int radius, StationState station)
        {
            var result = new List<NPCInstance>();
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew() && !npc.IsVisitor()) continue;
                int dist = TileDistance(col, row, npc.tileCol, npc.tileRow);
                if (dist <= radius) result.Add(npc);
            }
            return result;
        }
    }
}
