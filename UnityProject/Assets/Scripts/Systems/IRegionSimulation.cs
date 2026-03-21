// IRegionSimulation — interface for the Horizon Simulation work order.
//
// All methods are no-ops in the current stub implementation.
// The Horizon Simulation work order will replace RegionSimulationStub with
// a full implementation without requiring changes to trait or faction systems.
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Interface for the Horizon Simulation system.
    /// Defined here so trait and faction systems can be built against a stable API;
    /// implementation is a follow-on work order.
    /// </summary>
    public interface IRegionSimulation
    {
        /// <summary>Advances the simulation for a specific region by the given number of days.</summary>
        void TickRegion(string regionId, float deltaDays);

        /// <summary>Expands the simulation horizon outward from the player's current sector.</summary>
        void ExpandHorizon(Vector2Int playerSectorPosition, int horizonRadius);

        /// <summary>
        /// Generates a new RegionData stub for a sector at the horizon frontier.
        /// </summary>
        RegionData GenerateRegionAtHorizon(Vector2Int sectorPosition,
                                            System.Collections.Generic.List<string> neighbourRegionIds);

        /// <summary>Marks a region as discovered when the player enters its sector.</summary>
        void DiscoverRegion(string regionId);
    }
}
