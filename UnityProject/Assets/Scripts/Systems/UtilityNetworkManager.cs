// UtilityNetworkManager — orchestrates all four utility networks (Electrical,
// Plumbing, Ducting, Fuel Lines) and exposes the overlay-mode cycling used by the UI.
//
// Usage:
//   - GameManager owns one instance and calls Tick(station) every game tick.
//   - Call RebuildAll(station) after any tile placement, removal, or isolator toggle.
//   - Feature flags (ElectricalEnabled / PlumbingEnabled / DuctingEnabled / FuelEnabled) allow
//     individual systems to be hot-disabled without removing code (rollback safety).
//   - OverlayMode is cycled by OverlayModeController via CycleOverlay().
//
// Overlay cycle: Off → Electrical → Plumbing → Ducting → Fuel → Off
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // Overlay display modes for the station view.
    public enum OverlayMode
    {
        Off,
        Electrical,
        Plumbing,
        Ducting,
        Fuel
    }

    public class UtilityNetworkManager
    {
        // ── Feature flags (hot-disable individual systems for rollback) ───────
        public static bool ElectricalEnabled = true;
        public static bool PlumbingEnabled   = true;
        public static bool DuctingEnabled    = true;
        public static bool FuelEnabled       = true;

        // ── Overlay state ─────────────────────────────────────────────────────
        public OverlayMode CurrentOverlay { get; private set; } = OverlayMode.Off;

        // Fired whenever the overlay mode changes — UI should subscribe.
        public event Action<OverlayMode> OnOverlayChanged;

        // Fired after a network rebuild — UI/view layers should re-tint tiles.
        public event Action<StationState> OnNetworkChanged;

        private readonly NetworkSystem _networks;
        private readonly ContentRegistry _registry;

        public UtilityNetworkManager(ContentRegistry registry, NetworkSystem networks)
        {
            _registry = registry;
            _networks = networks;
        }

        // ── Overlay cycling ───────────────────────────────────────────────────

        /// <summary>
        /// Advance overlay mode: Off → Electrical → Plumbing → Ducting → Fuel → Off.
        /// Called by OverlayModeController when the player presses Tab.
        /// </summary>
        public void CycleOverlay()
        {
            CurrentOverlay = CurrentOverlay switch
            {
                OverlayMode.Off        => OverlayMode.Electrical,
                OverlayMode.Electrical => OverlayMode.Plumbing,
                OverlayMode.Plumbing   => OverlayMode.Ducting,
                OverlayMode.Ducting    => OverlayMode.Fuel,
                _                      => OverlayMode.Off
            };
            OnOverlayChanged?.Invoke(CurrentOverlay);
        }

        /// <summary>Set overlay directly (e.g. from settings or hotkey).</summary>
        public void SetOverlay(OverlayMode mode)
        {
            if (CurrentOverlay == mode) return;
            CurrentOverlay = mode;
            OnOverlayChanged?.Invoke(CurrentOverlay);
        }

        // ── Network management ────────────────────────────────────────────────

        /// <summary>
        /// Rebuild all three utility networks from the current tile layout.
        /// Call after PlaceFoundation, DemolishFoundation, or isolator toggle.
        /// </summary>
        public void RebuildAll(StationState station)
        {
            _networks.RebuildNetworks(station);
            OnNetworkChanged?.Invoke(station);
        }

        /// <summary>
        /// Toggle an isolator (Switch / Valve / Breaker) and rebuild networks.
        /// </summary>
        public void ToggleIsolator(StationState station, string foundationUid)
        {
            _networks.ToggleIsolator(station, foundationUid);
            OnNetworkChanged?.Invoke(station);
        }

        /// <summary>
        /// Validate that placing a pipe/duct tile will not merge incompatible content types.
        /// Returns null if placement is allowed, or a user-facing error string if blocked.
        /// </summary>
        public string ValidatePlacement(StationState station, int col, int row,
                                         string networkType, string contentType)
            => _networks.ValidateContentTypeConnection(station, col, row, networkType, contentType);

        // ── Tick simulation ───────────────────────────────────────────────────

        /// <summary>
        /// Run one simulation tick across all enabled networks.
        /// Called by GameManager.AdvanceTick() on every game tick.
        /// </summary>
        public void Tick(StationState station)
        {
            if (station == null) return;
            _networks.Tick(station);
        }

        // ── Network health helpers ────────────────────────────────────────────

        /// <summary>
        /// Returns an aggregated health summary for all networks of the given type
        /// (e.g. "electric", "pipe", "duct", "fuel").
        /// Used by the Networks sub-panel UI.
        /// </summary>
        public NetworkHealthSummary GetNetworkHealth(StationState station, string networkType)
            => _networks.GetNetworkHealth(station, networkType);

        /// <summary>
        /// Returns per-network-type connectivity status for all tiles in the given room.
        /// Used by the Room contextual panel Networks tab (UI-024).
        /// </summary>
        public List<RoomNetworkInfo> GetRoomConnectivity(StationState station, string roomKey)
            => _networks.GetRoomConnectivity(station, roomKey);

        /// <summary>
        /// Returns the aggregate battery charge level [0, 1] across all electrical networks.
        /// Returns 0 if there is no battery storage capacity.
        /// </summary>
        public float GetBatteryLevel(StationState station)
            => _networks.GetBatteryLevel(station);

        // ── Inspection helpers ────────────────────────────────────────────────

        /// <summary>
        /// Returns summary information for the network containing the given foundation.
        /// Used by the inspection panel UI.
        /// </summary>
        public NetworkInspectionData GetInspectionData(StationState station, string foundationUid)
        {
            var net = _networks.GetNetwork(station, foundationUid);
            if (net == null) return null;

            var data = new NetworkInspectionData
            {
                NetworkId       = net.uid,
                NetworkType     = net.networkType,
                ContentType     = net.contentType,
                TotalSupply     = net.totalSupply,
                TotalDemand     = net.totalDemand,
                StoredEnergy    = net.storedEnergy,
                StorageCapacity = net.storageCapacity,
                StoredVolume    = net.contentAmount,
            };

            foreach (var uid in net.memberUids)
            {
                if (!station.foundations.TryGetValue(uid, out var f)) continue;
                if (f.status != "complete") continue;
                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;
                if (def.nodeRole == null) continue;

                data.Members.Add(new NetworkMemberInfo
                {
                    FoundationUid = f.uid,
                    BuildableId   = f.buildableId,
                    DisplayName   = def.displayName,
                    Role          = def.nodeRole,
                    OutputWatts   = def.outputWatts,
                    DemandWatts   = def.demandWatts,
                    IsEnergised   = f.isEnergised,
                    IsSupplied    = f.isFluidSupplied || f.isGasSupplied || f.isFuelSupplied,
                    StoredAmount  = net.networkType == "electric" ? f.storedEnergy
                                  : net.networkType == "pipe"     ? f.storedFluid
                                  : net.networkType == "fuel"     ? f.storedFuel
                                  : f.storedGas,
                });
            }

            return data;
        }
    }

    // ── Room network connectivity ──────────────────────────────────────────────

    /// <summary>
    /// Connectivity of a single network type to a specific room.
    /// </summary>
    public enum RoomNetworkStatus
    {
        /// <summary>No conduit of this type is present in any tile of the room.</summary>
        NotConnected,
        /// <summary>At least one conduit of this type is in the room and the network is healthy.</summary>
        Connected,
        /// <summary>At least one conduit is present but the network is fragmented (Degraded or Severed).</summary>
        Severed,
    }

    /// <summary>
    /// Per-network-type connectivity status for a room, used by the Room contextual panel.
    /// </summary>
    public class RoomNetworkInfo
    {
        /// <summary>"electric" | "pipe" | "duct" | "fuel"</summary>
        public string NetworkType;
        public RoomNetworkStatus Status;
    }

    // ── Network health summary ─────────────────────────────────────────────────

    /// <summary>
    /// Overall health status for a network type.
    /// </summary>
    public enum NetworkHealthStatus { Healthy, Degraded, Severed }

    /// <summary>
    /// Health snapshot for all networks of one type, used by the Networks sub-panel.
    /// </summary>
    public class NetworkHealthSummary
    {
        /// <summary>Total foundation nodes across all networks of this type.</summary>
        public int ConnectedNodes;
        /// <summary>
        /// Number of closed isolators that are actively severing at least one adjacent
        /// same-type node connection (each such isolator is counted once, regardless of
        /// how many edges it blocks).
        /// </summary>
        public int SeveredCount;
        /// <summary>Overall health status derived from ConnectedNodes and SeveredCount.</summary>
        public NetworkHealthStatus Status;
    }

    // ── Inspection data containers ────────────────────────────────────────────

    /// <summary>
    /// Snapshot of a network's state for the inspection panel UI.
    /// </summary>
    public class NetworkInspectionData
    {
        public string NetworkId;
        public string NetworkType;      // "electric" | "pipe" | "duct" | "fuel"
        public string ContentType;      // fluid/gas type, null for electric
        public float  TotalSupply;
        public float  TotalDemand;
        public float  StoredEnergy;     // watt-hours (electrical only)
        public float  StorageCapacity;  // watt-hours or litres capacity
        public float  StoredVolume;     // litres (fluid/gas only)

        public List<NetworkMemberInfo> Members = new List<NetworkMemberInfo>();
    }

    /// <summary>
    /// Per-member info for the inspection panel member list.
    /// </summary>
    public class NetworkMemberInfo
    {
        public string FoundationUid;
        public string BuildableId;
        public string DisplayName;
        public string Role;         // "producer" | "consumer" | "storage" | "conduit" | "isolator"
        public float  OutputWatts;
        public float  DemandWatts;
        public bool   IsEnergised;
        public bool   IsSupplied;
        public float  StoredAmount;
    }
}
