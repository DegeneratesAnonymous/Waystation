// RegionSimulationStub — no-op default implementations of IRegionSimulation
// and IFactionHistoryProvider.
//
// Registered in GameManager.InitSystems() as the active implementations.
// The Horizon Simulation work order replaces these without requiring changes
// to the trait or faction systems.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Default no-op implementation of IRegionSimulation.
    /// All methods log TODO markers and return safe defaults.
    /// </summary>
    public class RegionSimulationStub : IRegionSimulation
    {
        public void TickRegion(string regionId, float deltaDays)
        {
            // TODO: Implement region simulation tick in Horizon Simulation work order.
        }

        public void ExpandHorizon(Vector2Int playerSectorPosition, int horizonRadius)
        {
            // TODO: Implement horizon expansion in Horizon Simulation work order.
        }

        public RegionData GenerateRegionAtHorizon(
            Vector2Int sectorPosition,
            List<string> neighbourRegionIds)
        {
            // TODO: Implement procedural region generation in Horizon Simulation work order.
            return new RegionData
            {
                regionId        = $"region_{sectorPosition.x}_{sectorPosition.y}",
                displayName     = $"Sector ({sectorPosition.x}, {sectorPosition.y})",
                simulationState = RegionSimulationState.OnHorizon,
            };
        }

        public void DiscoverRegion(string regionId)
        {
            // TODO: Implement region discovery in Horizon Simulation work order.
        }
    }

    /// <summary>
    /// Default no-op implementation of IFactionHistoryProvider.
    /// Always returns an empty history list.
    /// </summary>
    public class FactionHistoryStub : IFactionHistoryProvider
    {
        private readonly Dictionary<string, List<HistoricalEvent>> _history =
            new Dictionary<string, List<HistoricalEvent>>();

        public List<HistoricalEvent> GetFactionHistory(string factionId)
        {
            // TODO: Return actual history in Horizon Simulation work order.
            if (_history.TryGetValue(factionId, out var list)) return list;
            return new List<HistoricalEvent>();
        }

        public void RecordFactionEvent(string factionId, HistoricalEvent evt)
        {
            // TODO: Persist history in Horizon Simulation work order.
            if (!_history.ContainsKey(factionId))
                _history[factionId] = new List<HistoricalEvent>();
            _history[factionId].Add(evt);
        }
    }
}
