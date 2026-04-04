// DockingBayManager — berth tracking and bay assignment (WO-FAC-007).
using System.Collections.Generic;
using System.Linq;
using Waystation.Models;

namespace Waystation.Systems
{
    public class DockingBayManager
    {
        // ── State ─────────────────────────────────────────────────────────────
        /// <summary>Ship UID → assigned bay UID.</summary>
        private readonly Dictionary<string, string> _assignments = new Dictionary<string, string>();

        // ── Bay Queries ───────────────────────────────────────────────────────

        /// <summary>Count total berths available across all docking bays.</summary>
        public int GetTotalBerths(StationState station)
        {
            if (station.landingPads == null) return 0;
            return station.landingPads.Count; // Each pad = 1 berth (upgradeable in future)
        }

        /// <summary>Count currently occupied berths.</summary>
        public int GetOccupiedBerths()
        {
            return _assignments.Count;
        }

        /// <summary>Count free berths.</summary>
        public int GetFreeBerths(StationState station)
        {
            return GetTotalBerths(station) - GetOccupiedBerths();
        }

        /// <summary>Check if any bay is available for docking.</summary>
        public bool HasFreeBay(StationState station)
        {
            return GetFreeBerths(station) > 0;
        }

        /// <summary>Check if the station has any docking bays placed at all.</summary>
        public bool HasAnyBays(StationState station)
        {
            return station.landingPads != null && station.landingPads.Count > 0;
        }

        // ── Assignment ────────────────────────────────────────────────────────

        /// <summary>Assign a ship to the next available bay. Returns the bay UID or null.</summary>
        public string AssignBay(string shipUid, StationState station)
        {
            if (!HasFreeBay(station)) return null;
            if (_assignments.ContainsKey(shipUid)) return _assignments[shipUid];

            // Find an unoccupied pad
            var occupiedBays = new HashSet<string>(_assignments.Values);
            foreach (var kv in station.landingPads)
            {
                if (!occupiedBays.Contains(kv.Key))
                {
                    _assignments[shipUid] = kv.Key;
                    return kv.Key;
                }
            }

            return null;
        }

        /// <summary>Release a bay when a ship departs.</summary>
        public void ReleaseBay(string shipUid)
        {
            _assignments.Remove(shipUid);
        }

        /// <summary>Get the bay assigned to a ship.</summary>
        public string GetAssignedBay(string shipUid)
        {
            return _assignments.TryGetValue(shipUid, out var bay) ? bay : null;
        }

        /// <summary>Check if a specific ship is assigned a bay.</summary>
        public bool IsAssigned(string shipUid) => _assignments.ContainsKey(shipUid);

        public IReadOnlyDictionary<string, string> AllAssignments => _assignments;
    }
}
