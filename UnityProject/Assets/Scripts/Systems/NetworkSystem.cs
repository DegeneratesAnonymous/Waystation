// NetworkSystem — manages connected graphs of wire/pipe/duct foundations.
// Flood-fills connectivity whenever the building layout changes.
// Networks are identified by NetworkInstance.uid stored on each member FoundationInstance.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class NetworkSystem
    {
        private readonly ContentRegistry _registry;

        // Positional lookup: (col, row) → foundations at that tile.
        // Rebuilt once in RebuildNetworks and reused by all adjacency queries.
        private Dictionary<(int, int), List<FoundationInstance>> _posLookup
            = new Dictionary<(int, int), List<FoundationInstance>>();

        public NetworkSystem(ContentRegistry registry) => _registry = registry;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuild all networks from scratch by flood-filling connected network foundations.
        /// Call after any PlaceFoundation or DemolishFoundation.
        /// </summary>
        public void RebuildNetworks(StationState station)
        {
            // Clear existing network assignments on all foundations
            foreach (var f in station.foundations.Values)
                f.networkId = null;
            station.networks.Clear();

            // Build positional lookup for O(1) neighbour queries
            _posLookup = BuildPosLookup(station);

            var visited = new HashSet<string>();

            foreach (var kv in station.foundations)
            {
                var f = kv.Value;
                if (visited.Contains(f.uid)) continue;

                // Only network-capable tiles
                string netType = GetNetworkType(f);
                if (netType == null) continue;

                // BFS flood-fill
                var members = new List<string>();
                var queue   = new Queue<string>();
                queue.Enqueue(f.uid);
                visited.Add(f.uid);

                while (queue.Count > 0)
                {
                    string uid = queue.Dequeue();
                    members.Add(uid);

                    if (!station.foundations.TryGetValue(uid, out var cur)) continue;
                    // Find adjacent foundations of the same network type
                    foreach (var neighbor in GetAdjacentFoundations(cur.tileCol, cur.tileRow, netType))
                    {
                        if (!visited.Contains(neighbor.uid))
                        {
                            visited.Add(neighbor.uid);
                            queue.Enqueue(neighbor.uid);
                        }
                    }
                }

                // Create network and assign uid to all members
                var net = NetworkInstance.Create(netType);
                foreach (var uid in members)
                {
                    net.memberUids.Add(uid);
                    if (station.foundations.TryGetValue(uid, out var mf))
                        mf.networkId = net.uid;
                }
                station.networks[net.uid] = net;
            }

            // After rebuild, infer content type from connected producers
            InferNetworkContent(station);
        }

        /// <summary>
        /// Returns the NetworkInstance that a given foundation belongs to, or null.
        /// </summary>
        public NetworkInstance GetNetwork(StationState station, string foundationUid)
        {
            if (!station.foundations.TryGetValue(foundationUid, out var f)) return null;
            if (f.networkId == null) return null;
            station.networks.TryGetValue(f.networkId, out var net);
            return net;
        }

        /// <summary>
        /// Returns a 4-bit connection mask for a foundation at (col, row).
        /// N=1, E=2, S=4, W=8.  A bit is set when an adjacent tile belongs to the
        /// same network type, allowing TileAtlas to pick the correct topology sprite.
        /// </summary>
        public int GetConnectionMask(StationState station, int col, int row, string networkType)
        {
            // Use cached lookup when available; fall back to rebuilding if stale.
            var lookup = _posLookup.Count > 0 ? _posLookup : BuildPosLookup(station);
            int mask = 0;
            if (HasNetworkNeighbor(lookup, col,   row+1, networkType)) mask |= 1; // N
            if (HasNetworkNeighbor(lookup, col+1, row,   networkType)) mask |= 2; // E
            if (HasNetworkNeighbor(lookup, col,   row-1, networkType)) mask |= 4; // S
            if (HasNetworkNeighbor(lookup, col-1, row,   networkType)) mask |= 8; // W
            return mask;
        }

        private bool HasNetworkNeighbor(Dictionary<(int, int), List<FoundationInstance>> lookup,
                                        int col, int row, string netType)
        {
            if (!lookup.TryGetValue((col, row), out var list)) return false;
            foreach (var f in list)
                if (GetNetworkType(f) == netType) return true;
            return false;
        }

        /// <summary>
        /// Sets the fluid/gas content type of a network (used when a producer first connects).
        /// Only sets if contentType is currently null (first-producer-wins rule).
        /// </summary>
        public void SetContent(StationState station, string networkId, string contentType)
        {
            if (!station.networks.TryGetValue(networkId, out var net)) return;
            // Validate contentType against known resource ids
            if (!IsValidContentType(contentType))
            {
                Debug.LogWarning($"[NetworkSystem] Rejected unknown contentType '{contentType}'");
                return;
            }
            if (net.contentType == null)
                net.contentType = contentType;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private string GetNetworkType(FoundationInstance f)
        {
            if (!(_registry?.Buildables.TryGetValue(f.buildableId, out var def) == true)) return null;
            return def.networkType;
        }

        private IEnumerable<FoundationInstance> GetAdjacentFoundations(
            int col, int row, string netType)
        {
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (dc, dr) in dirs)
            {
                int nc = col + dc, nr = row + dr;
                if (!_posLookup.TryGetValue((nc, nr), out var list)) continue;
                foreach (var f in list)
                {
                    if (GetNetworkType(f) != netType) continue;
                    yield return f;
                }
            }
        }

        private static Dictionary<(int, int), List<FoundationInstance>> BuildPosLookup(
            StationState station)
        {
            var lookup = new Dictionary<(int, int), List<FoundationInstance>>();
            foreach (var f in station.foundations.Values)
            {
                var key = (f.tileCol, f.tileRow);
                if (!lookup.TryGetValue(key, out var list))
                    lookup[key] = list = new List<FoundationInstance>();
                list.Add(f);
            }
            return lookup;
        }

        private void InferNetworkContent(StationState station)
        {
            // For each complete foundation that is a producer/consumer, infer the
            // network content from the buildable id if not already set.
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.networkId == null) continue;
                if (!station.networks.TryGetValue(f.networkId, out var net)) continue;
                if (net.contentType != null) continue; // already set

                // Infer from buildable
                if (f.buildableId.Contains("ice_refiner"))
                {
                    // ice refiner connects to both pipe (water) and duct (oxygen) networks,
                    // but we can't set both here — skip; content must be set when refining begins
                }
            }
        }

        private static readonly HashSet<string> ValidContentTypes = new HashSet<string>
        {
            "water", "oxygen", "fuel", "coolant", "waste_water"
        };
        private static bool IsValidContentType(string t) => ValidContentTypes.Contains(t);
    }
}
