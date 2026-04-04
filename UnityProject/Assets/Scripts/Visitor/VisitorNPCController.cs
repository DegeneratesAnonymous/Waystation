// VisitorNPCController — pathfinding destination routing for visitor NPCs (WO-FAC-007).
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Routes visitor NPCs to their destination based on visitor type.
    /// Visitor NPCs have full NPC simulation during their stay.
    /// </summary>
    public class VisitorNPCController
    {
        // ── Destination Mappings ──────────────────────────────────────────────

        /// <summary>
        /// Determine the target room type for a visitor based on their type/role.
        /// Returns the room type tag the NPC should pathfind to.
        /// </summary>
        public static string GetDestinationRoomType(string visitorType)
        {
            switch (visitorType)
            {
                case "trader":           return "cargo_hold";
                case "diplomat":         return "communications";
                case "refugee":          return "medical_bay";
                case "raider":           return "docking_bay";
                case "inspector":        return "cargo_hold";
                case "smuggler":         return "cargo_hold";
                case "medical":          return "medical_bay";
                case "passerby":         return "common_area";
                case "unknown":          return "docking_bay";
                default:                 return "common_area";
            }
        }

        /// <summary>
        /// Get a secondary destination if the primary is unavailable.
        /// </summary>
        public static string GetSecondaryDestination(string visitorType)
        {
            switch (visitorType)
            {
                case "refugee":  return "common_area";
                case "diplomat": return "common_area";
                case "medical":  return "common_area";
                default:         return null;
            }
        }

        /// <summary>
        /// Find an available tile in the target room type for a visitor NPC.
        /// Returns (col, row) or (-1, -1) if none found.
        /// </summary>
        public static (int col, int row) FindDestinationTile(string visitorType,
            StationState station)
        {
            string targetRoom = GetDestinationRoomType(visitorType);

            // Search room assignments for a matching room type
            if (station.playerRoomTypeAssignments != null)
            {
                foreach (var kv in station.playerRoomTypeAssignments)
                {
                    if (kv.Value == targetRoom)
                    {
                        // Parse room key to get a tile position
                        var parts = kv.Key.Split('_');
                        if (parts.Length >= 2 &&
                            int.TryParse(parts[parts.Length - 2], out int col) &&
                            int.TryParse(parts[parts.Length - 1], out int row))
                        {
                            return (col, row);
                        }
                    }
                }
            }

            // Fallback: try secondary destination
            string secondary = GetSecondaryDestination(visitorType);
            if (secondary != null && station.playerRoomTypeAssignments != null)
            {
                foreach (var kv in station.playerRoomTypeAssignments)
                {
                    if (kv.Value == secondary)
                    {
                        var parts = kv.Key.Split('_');
                        if (parts.Length >= 2 &&
                            int.TryParse(parts[parts.Length - 2], out int col) &&
                            int.TryParse(parts[parts.Length - 1], out int row))
                        {
                            return (col, row);
                        }
                    }
                }
            }

            return (-1, -1);
        }

        /// <summary>
        /// Check if a visitor is a recurring trader contact (builds relationship history).
        /// </summary>
        public static bool IsRecurringContact(NPCInstance visitor, StationState station)
        {
            if (visitor == null) return false;
            // A recurring contact is a visitor whose ship has visited 3+ times
            int visitCount = 0;
            if (station.visitHistory != null)
            {
                foreach (var record in station.visitHistory)
                {
                    if (record.shipName == visitor.name)
                        visitCount++;
                }
            }
            return visitCount >= 3;
        }
    }
}
