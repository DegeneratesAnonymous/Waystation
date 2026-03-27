// NetworkSystem — manages connected graphs of wire/pipe/duct/fuel-line foundations.
// Uses Union-Find (disjoint set with path compression + union-by-rank) for
// efficient network rebuilding whenever the building layout changes.
// Networks are identified by NetworkInstance.uid stored on each member FoundationInstance.
//
// Supply / demand tick:
//   Tick() is called by UtilityNetworkManager once per game tick.  For each network:
//     Electrical: sums producer OutputWatts, charges/discharges batteries, marks consumers.
//     Plumbing  : sums fluid producers, fills storage tanks, marks consumers IsFluidSupplied.
//     Ducting   : sums gas producers,  fills storage tanks, marks consumers IsGasSupplied.
//     Fuel Lines: sums fuel producers, fills fuel tanks,    marks consumers IsFuelSupplied.
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
        /// Rebuild all networks from scratch using Union-Find over the foundation graph.
        /// Call after any PlaceFoundation, DemolishFoundation, or isolator-state change.
        /// </summary>
        public void RebuildNetworks(StationState station)
        {
            // Build positional lookup for O(1) neighbour queries
            _posLookup = BuildPosLookup(station);

            // Collect all network-capable, fully-built foundations.
            // Non-complete foundations (awaiting_haul, constructing) are excluded
            // so unbuilt conduits cannot prematurely connect components.
            var netFoundations = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status == "complete" && GetNetworkType(f) != null)
                    netFoundations.Add(f);
            }

            // ── Union-Find setup ─────────────────────────────────────────────
            // Map uid → index for O(1) lookup
            var indexMap = new Dictionary<string, int>(netFoundations.Count);
            for (int i = 0; i < netFoundations.Count; i++)
                indexMap[netFoundations[i].uid] = i;

            int n = netFoundations.Count;
            var parent = new int[n];
            var rank   = new int[n];
            for (int i = 0; i < n; i++) { parent[i] = i; rank[i] = 0; }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]]; // path compression (halving)
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (rank[ra] < rank[rb]) { parent[ra] = rb; }
                else if (rank[ra] > rank[rb]) { parent[rb] = ra; }
                else { parent[rb] = ra; rank[ra]++; }
            }

            // Union adjacent foundations of the same network type.
            // Isolators that are closed (isolatorOpen == false) are treated as gaps.
            foreach (var f in netFoundations)
            {
                if (!indexMap.TryGetValue(f.uid, out int fi)) continue;
                string netType = GetNetworkType(f);

                // A closed isolator stops connectivity in all directions from this node
                bool fIsClosedIsolator = IsClosedIsolator(f);

                foreach (var dir in s_Dirs)
                {
                    int nc = f.tileCol + dir.x, nr = f.tileRow + dir.y;
                    if (!_posLookup.TryGetValue((nc, nr), out var list)) continue;
                    foreach (var nb in list)
                    {
                        if (GetNetworkType(nb) != netType) continue;
                        if (!indexMap.TryGetValue(nb.uid, out int ni)) continue;

                        // If either side is a closed isolator, do not union
                        if (fIsClosedIsolator || IsClosedIsolator(nb)) continue;

                        Union(fi, ni);
                    }
                }
            }

            // ── Assign NetworkIDs ────────────────────────────────────────────
            // Reset all network assignments
            foreach (var f in station.foundations.Values)
                f.networkId = null;
            station.networks.Clear();

            // Group by root → create one NetworkInstance per component
            var rootToNetwork = new Dictionary<int, NetworkInstance>();
            foreach (var f in netFoundations)
            {
                if (!indexMap.TryGetValue(f.uid, out int fi)) continue;
                int root = Find(fi);
                if (!rootToNetwork.TryGetValue(root, out var net))
                {
                    net = NetworkInstance.Create(GetNetworkType(f));
                    rootToNetwork[root] = net;
                    station.networks[net.uid] = net;
                }
                net.memberUids.Add(f.uid);
                f.networkId = net.uid;
            }

            // After rebuild, infer content type from connected producers
            InferNetworkContent(station);
        }

        /// <summary>
        /// Simulate one tick of supply/demand for all networks.
        /// Called by UtilityNetworkManager.Tick() on a fixed interval.
        /// </summary>
        public void Tick(StationState station)
        {
            foreach (var net in station.networks.Values)
            {
                switch (net.networkType)
                {
                    case "electric":
                        if (UtilityNetworkManager.ElectricalEnabled) TickElectrical(station, net);
                        break;
                    case "pipe":
                        if (UtilityNetworkManager.PlumbingEnabled)   TickFluid(station, net);
                        break;
                    case "duct":
                        if (UtilityNetworkManager.DuctingEnabled)    TickGas(station, net);
                        break;
                    case "fuel":
                        if (UtilityNetworkManager.FuelEnabled)       TickFuel(station, net);
                        break;
                }
            }
        }

        /// <summary>
        /// Toggle an isolator foundation between open and closed.
        /// Automatically triggers a network rebuild.
        /// </summary>
        public void ToggleIsolator(StationState station, string foundationUid)
        {
            if (!station.foundations.TryGetValue(foundationUid, out var f)) return;
            if (!IsIsolatorRole(f)) return;
            f.isolatorOpen = !f.isolatorOpen;
            RebuildNetworks(station);
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
            if (_posLookup.Count == 0)
                _posLookup = BuildPosLookup(station);
            var lookup = _posLookup;
            int mask = 0;
            if (HasNetworkNeighbor(lookup, col,   row+1, networkType)) mask |= 1; // N
            if (HasNetworkNeighbor(lookup, col+1, row,   networkType)) mask |= 2; // E
            if (HasNetworkNeighbor(lookup, col,   row-1, networkType)) mask |= 4; // S
            if (HasNetworkNeighbor(lookup, col-1, row,   networkType)) mask |= 8; // W
            return mask;
        }

        /// <summary>
        /// Validates whether a new tile of the given fluid/gas type can connect to
        /// the adjacent network.  Returns null if OK, or an error message if blocked.
        /// </summary>
        public string ValidateContentTypeConnection(StationState station,
                                                     int col, int row,
                                                     string networkType,
                                                     string proposedContentType)
        {
            if (proposedContentType == null) return null;
            if (_posLookup.Count == 0)
                _posLookup = BuildPosLookup(station);

            foreach (var dir in s_Dirs)
            {
                int nc = col + dir.x, nr = row + dir.y;
                if (!_posLookup.TryGetValue((nc, nr), out var list)) continue;
                foreach (var nb in list)
                {
                    if (GetNetworkType(nb) != networkType) continue;
                    if (nb.networkId == null) continue;
                    if (!station.networks.TryGetValue(nb.networkId, out var net)) continue;
                    if (net.contentType != null && net.contentType != proposedContentType)
                    {
                        string kind = networkType == "pipe" ? "fluid"
                                    : networkType == "duct" ? "gas"
                                    : "fuel";
                        return $"Incompatible {kind} network";
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Sets the fluid/gas content type of a network (used when a producer first connects).
        /// Only sets if contentType is currently null (first-producer-wins rule).
        /// </summary>
        public void SetContent(StationState station, string networkId, string contentType)
        {
            if (!station.networks.TryGetValue(networkId, out var net)) return;
            if (!IsValidContentType(contentType))
            {
                Debug.LogWarning($"[NetworkSystem] Rejected unknown contentType '{contentType}'");
                return;
            }
            if (net.contentType == null)
                net.contentType = contentType;
        }

        // ── Tick helpers ─────────────────────────────────────────────────────

        private void TickElectrical(StationState station, NetworkInstance net)
        {
            // Gather all complete foundations on this network
            float supply  = 0f;
            float demand  = 0f;
            float stored  = 0f;
            float maxCap  = 0f;

            var producers = new List<FoundationInstance>();
            var consumers = new List<FoundationInstance>();
            var batteries = new List<FoundationInstance>();

            foreach (var uid in net.memberUids)
            {
                if (!station.foundations.TryGetValue(uid, out var f)) continue;
                if (f.status != "complete") continue;
                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;
                if (def.nodeRole == null) continue;

                switch (def.nodeRole)
                {
                    case "producer":
                        supply += def.outputWatts * f.Functionality();
                        producers.Add(f);
                        break;
                    case "consumer":
                        demand += def.demandWatts;
                        consumers.Add(f);
                        break;
                    case "storage":
                        stored  += f.storedEnergy;
                        maxCap  += def.storageCapacityWh;
                        batteries.Add(f);
                        break;
                }
            }

            float deficit = demand - supply;

            if (deficit <= 0f)
            {
                // Surplus — charge batteries
                float surplus = -deficit;
                foreach (var bat in batteries)
                {
                    if (!_registry.Buildables.TryGetValue(bat.buildableId, out var def)) continue;
                    float space = def.storageCapacityWh - bat.storedEnergy;
                    float charge = Mathf.Min(surplus, space);
                    bat.storedEnergy = Mathf.Clamp(bat.storedEnergy + charge, 0f, def.storageCapacityWh);
                    surplus -= charge;
                    if (surplus <= 0f) break;
                }
                // All consumers energised
                foreach (var c in consumers) c.isEnergised = true;
            }
            else
            {
                // Deficit — draw from batteries
                float remaining = deficit;
                foreach (var bat in batteries)
                {
                    float draw = Mathf.Min(remaining, bat.storedEnergy);
                    bat.storedEnergy = Mathf.Max(0f, bat.storedEnergy - draw);
                    remaining -= draw;
                    if (remaining <= 0f) break;
                }
                bool hasEnoughTotal = remaining <= 0f;
                foreach (var c in consumers) c.isEnergised = hasEnoughTotal;
            }

            // Update network aggregate stats
            net.totalSupply    = supply;
            net.totalDemand    = demand;
            net.storedEnergy   = 0f;
            net.storageCapacity = maxCap;
            foreach (var bat in batteries)
                net.storedEnergy += bat.storedEnergy;
        }

        private void TickFluid(StationState station, NetworkInstance net)
        {
            float produced = 0f;
            var consumers = new List<FoundationInstance>();
            var tanks     = new List<(FoundationInstance f, float cap)>();

            foreach (var uid in net.memberUids)
            {
                if (!station.foundations.TryGetValue(uid, out var f)) continue;
                if (f.status != "complete") continue;
                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;
                if (def.nodeRole == null) continue;

                switch (def.nodeRole)
                {
                    case "producer":
                        // Fluid producers require power when requiresPower is set
                        if (!def.requiresPower || f.isEnergised)
                            produced += def.fluidProducePerTick * f.Functionality();
                        break;
                    case "consumer":
                        consumers.Add(f);
                        break;
                    case "storage":
                        tanks.Add((f, def.fluidStorageCapacity));
                        break;
                }
            }

            // Pour production into tanks
            float toStore = produced;
            foreach (var (tank, cap) in tanks)
            {
                float space = cap - tank.storedFluid;
                float fill  = Mathf.Min(toStore, space);
                tank.storedFluid = Mathf.Clamp(tank.storedFluid + fill, 0f, cap);
                toStore -= fill;
                if (toStore <= 0f) break;
            }

            // Supply consumers from tanks
            float totalStored = 0f;
            foreach (var (tank, _) in tanks) totalStored += tank.storedFluid;

            float totalDemand = 0f;
            foreach (var c in consumers)
            {
                if (_registry.Buildables.TryGetValue(c.buildableId, out var def))
                    totalDemand += def.fluidDemandPerTick;
            }

            bool canSupply = totalStored >= totalDemand;
            if (canSupply && totalDemand > 0f)
            {
                float remaining = totalDemand;
                foreach (var (tank, _) in tanks)
                {
                    float draw = Mathf.Min(remaining, tank.storedFluid);
                    tank.storedFluid = Mathf.Max(0f, tank.storedFluid - draw);
                    remaining -= draw;
                    if (remaining <= 0f) break;
                }
            }
            foreach (var c in consumers) c.isFluidSupplied = canSupply;

            // Update aggregate
            net.totalSupply     = produced;
            net.totalDemand     = totalDemand;
            net.storedEnergy    = 0f; // not used for fluid
            float cap2 = 0f;
            float stored2 = 0f;
            foreach (var (tank, capv) in tanks) { cap2 += capv; stored2 += tank.storedFluid; }
            net.contentAmount   = stored2;
            net.contentCapacity = cap2;
            net.storageCapacity = cap2;
        }

        private void TickGas(StationState station, NetworkInstance net)
        {
            float produced = 0f;
            var consumers = new List<FoundationInstance>();
            var tanks     = new List<(FoundationInstance f, float cap)>();

            foreach (var uid in net.memberUids)
            {
                if (!station.foundations.TryGetValue(uid, out var f)) continue;
                if (f.status != "complete") continue;
                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;
                if (def.nodeRole == null) continue;

                switch (def.nodeRole)
                {
                    case "producer":
                        if (!def.requiresPower || f.isEnergised)
                            produced += def.gasProducePerTick * f.Functionality();
                        break;
                    case "consumer":
                        consumers.Add(f);
                        break;
                    case "storage":
                        tanks.Add((f, def.gasStorageCapacity));
                        break;
                }
            }

            float toStore = produced;
            foreach (var (tank, cap) in tanks)
            {
                float space = cap - tank.storedGas;
                float fill  = Mathf.Min(toStore, space);
                tank.storedGas = Mathf.Clamp(tank.storedGas + fill, 0f, cap);
                toStore -= fill;
                if (toStore <= 0f) break;
            }

            float totalStored = 0f;
            foreach (var (tank, _) in tanks) totalStored += tank.storedGas;

            float totalDemand = 0f;
            foreach (var c in consumers)
            {
                if (_registry.Buildables.TryGetValue(c.buildableId, out var def))
                    totalDemand += def.gasDemandPerTick;
            }

            bool canSupply = totalStored >= totalDemand;
            if (canSupply && totalDemand > 0f)
            {
                float remaining = totalDemand;
                foreach (var (tank, _) in tanks)
                {
                    float draw = Mathf.Min(remaining, tank.storedGas);
                    tank.storedGas = Mathf.Max(0f, tank.storedGas - draw);
                    remaining -= draw;
                    if (remaining <= 0f) break;
                }
            }
            foreach (var c in consumers) c.isGasSupplied = canSupply;

            net.totalSupply     = produced;
            net.totalDemand     = totalDemand;
            float cap3 = 0f; float stored3 = 0f;
            foreach (var (tank, capv) in tanks) { cap3 += capv; stored3 += tank.storedGas; }
            net.contentAmount   = stored3;
            net.contentCapacity = cap3;
            net.storageCapacity = cap3;
        }

        private void TickFuel(StationState station, NetworkInstance net)
        {
            float produced = 0f;
            var consumers = new List<FoundationInstance>();
            var tanks     = new List<(FoundationInstance f, float cap)>();

            foreach (var uid in net.memberUids)
            {
                if (!station.foundations.TryGetValue(uid, out var f)) continue;
                if (f.status != "complete") continue;
                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;
                if (def.nodeRole == null) continue;

                switch (def.nodeRole)
                {
                    case "producer":
                        if (!def.requiresPower || f.isEnergised)
                            produced += def.fuelProducePerTick * f.Functionality();
                        break;
                    case "consumer":
                        consumers.Add(f);
                        break;
                    case "storage":
                        tanks.Add((f, def.fuelStorageCapacity));
                        break;
                }
            }

            // Pour production into tanks
            float toStore = produced;
            foreach (var (tank, cap) in tanks)
            {
                float space = cap - tank.storedFuel;
                float fill  = Mathf.Min(toStore, space);
                tank.storedFuel = Mathf.Clamp(tank.storedFuel + fill, 0f, cap);
                toStore -= fill;
                if (toStore <= 0f) break;
            }

            // Supply consumers from tanks
            float totalStored = 0f;
            foreach (var (tank, _) in tanks) totalStored += tank.storedFuel;

            float totalDemand = 0f;
            foreach (var c in consumers)
            {
                if (_registry.Buildables.TryGetValue(c.buildableId, out var def))
                    totalDemand += def.fuelDemandPerTick;
            }

            bool canSupply = totalStored >= totalDemand;
            if (canSupply && totalDemand > 0f)
            {
                float remaining = totalDemand;
                foreach (var (tank, _) in tanks)
                {
                    float draw = Mathf.Min(remaining, tank.storedFuel);
                    tank.storedFuel = Mathf.Max(0f, tank.storedFuel - draw);
                    remaining -= draw;
                    if (remaining <= 0f) break;
                }
            }
            foreach (var c in consumers) c.isFuelSupplied = canSupply;

            net.totalSupply     = produced;
            net.totalDemand     = totalDemand;
            float cap4 = 0f; float stored4 = 0f;
            foreach (var (tank, capv) in tanks) { cap4 += capv; stored4 += tank.storedFuel; }
            net.contentAmount   = stored4;
            net.contentCapacity = cap4;
            net.storageCapacity = cap4;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static readonly (int x, int y)[] s_Dirs = { (1,0), (-1,0), (0,1), (0,-1) };

        private string GetNetworkType(FoundationInstance f)
        {
            if (!(_registry?.Buildables.TryGetValue(f.buildableId, out var def) == true)) return null;
            return def.networkType;
        }

        private bool IsIsolatorRole(FoundationInstance f)
        {
            if (!(_registry?.Buildables.TryGetValue(f.buildableId, out var def) == true)) return false;
            return def.nodeRole == "isolator";
        }

        private bool IsClosedIsolator(FoundationInstance f)
            => IsIsolatorRole(f) && !f.isolatorOpen;

        private bool HasNetworkNeighbor(Dictionary<(int, int), List<FoundationInstance>> lookup,
                                        int col, int row, string netType)
        {
            if (!lookup.TryGetValue((col, row), out var list)) return false;
            foreach (var f in list)
                if (f.status == "complete" && GetNetworkType(f) == netType) return true;
            return false;
        }

        private static Dictionary<(int, int), List<FoundationInstance>> BuildPosLookup(
            StationState station)
        {
            var lookup = new Dictionary<(int, int), List<FoundationInstance>>();
            foreach (var f in station.foundations.Values)
            {
                // Only index complete foundations; in-progress tiles must not
                // contribute to connectivity or topology sprite masks.
                if (f.status != "complete") continue;
                var key = (f.tileCol, f.tileRow);
                if (!lookup.TryGetValue(key, out var list))
                    lookup[key] = list = new List<FoundationInstance>();
                list.Add(f);
            }
            return lookup;
        }

        private void InferNetworkContent(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.networkId == null) continue;
                if (!station.networks.TryGetValue(f.networkId, out var net)) continue;
                if (net.contentType != null) continue;

                // Fuel networks always carry fuel — no per-member inference needed.
                if (net.networkType == "fuel")
                {
                    net.contentType = "fuel";
                    continue;
                }

                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;

                // Infer fluid type from producer definition
                if (def.nodeRole == "producer" && def.fluidType != null)
                    net.contentType = def.fluidType;
                else if (def.nodeRole == "producer" && def.gasType != null)
                    net.contentType = def.gasType;
                else if (def.fluidType != null)
                    net.contentType = def.fluidType;
                else if (def.gasType != null)
                    net.contentType = def.gasType;
            }
        }

        private static readonly HashSet<string> ValidContentTypes = new HashSet<string>
        {
            "water", "oxygen", "fuel", "coolant", "waste_water",
            "carbon_dioxide", "nitrogen"
        };
        private static bool IsValidContentType(string t) => ValidContentTypes.Contains(t);
    }
}
