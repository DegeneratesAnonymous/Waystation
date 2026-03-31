// HorizonSimulation — full IRegionSimulation implementation.
//
// Ticks faction activity, region state, and resource flows beyond the player's
// visibility frontier.  Registered in GameManager.InitSystems() replacing
// RegionSimulationStub.
//
// Fidelity tiers (assigned per region based on RegionSimulationState):
//   Full    — Discovered / FullyMapped sectors: complete faction + resource simulation.
//   Summary — OnHorizon sectors: simplified faction state, approximate resource flows.
//   Minimal — Undiscovered sectors: conflict drift only; no resource recording.
using System;
using System.Collections.Generic;
using Random = System.Random;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Processing fidelity for Horizon Simulation regions.
    /// Determined by the region's <see cref="RegionSimulationState"/>.
    /// </summary>
    public enum HorizonFidelityTier
    {
        /// <summary>Full simulation — explored (Discovered / FullyMapped) regions.</summary>
        Full,

        /// <summary>Summary simulation — detected (OnHorizon) regions at reduced cost.</summary>
        Summary,

        /// <summary>Minimal simulation — uncharted (Undiscovered) regions.</summary>
        Minimal,
    }

    /// <summary>
    /// Full Horizon Simulation.  Implements <see cref="IRegionSimulation"/> and drives
    /// faction activity, territorial shifts, and resource flows for regions beyond the
    /// player's direct observation.
    /// </summary>
    public class HorizonSimulation : IRegionSimulation
    {
        // ── Faction simulation probabilities (per day of game time) ───────────

        /// <summary>Probability per day that a multi-faction region sees a war declaration.</summary>
        public const float WarProbabilityPerDay      = 0.002f;

        /// <summary>Probability per day that a low-conflict multi-faction region forms an alliance.</summary>
        public const float AllianceProbabilityPerDay = 0.001f;

        /// <summary>Conflict level increase per day when multiple factions are present.</summary>
        public const float ConflictGrowthRate = 0.01f;

        /// <summary>Conflict level decrease per day when a single faction occupies the region.</summary>
        public const float ConflictDecayRate  = 0.005f;

        // ── Resource flow constants ────────────────────────────────────────────

        /// <summary>Base daily resource output (units) at populationDensity = 1.0.</summary>
        public const float BaseResourceFlow = 10f;

        // ── Variance by fidelity tier (fraction of base flow) ─────────────────

        private const float FullVariance    = 0.15f;
        private const float SummaryVariance = 0.05f;

        // ── Low conflict threshold for alliance formation ──────────────────────

        private const float AllianceConflictCeiling = 0.3f;

        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly RegionRegistry          _registry;
        private readonly IFactionHistoryProvider _factionHistory;
        private readonly Random                  _rng;

        /// <param name="registry">RegionRegistry used to look up and record region state.</param>
        /// <param name="factionHistory">Provider used to persist faction events.</param>
        /// <param name="seed">Seed for the internal RNG; 0 yields a time-based seed.</param>
        public HorizonSimulation(RegionRegistry registry,
                                  IFactionHistoryProvider factionHistory,
                                  int seed = 0)
        {
            _registry       = registry ?? throw new ArgumentNullException(nameof(registry));
            _factionHistory = factionHistory ?? throw new ArgumentNullException(nameof(factionHistory));
            _rng            = seed == 0 ? new Random() : new Random(seed);
        }

        // ── IRegionSimulation ─────────────────────────────────────────────────

        /// <summary>
        /// Advances simulation for the given region by <paramref name="deltaDays"/> game-days.
        /// Processing depth is determined by the region's fidelity tier.
        /// </summary>
        public void TickRegion(string regionId, float deltaDays)
        {
            if (!_registry.TryGetRegion(regionId, out var region)) return;

            switch (GetFidelityTier(region.simulationState))
            {
                case HorizonFidelityTier.Full:
                    TickFull(region, deltaDays);
                    break;
                case HorizonFidelityTier.Summary:
                    TickSummary(region, deltaDays);
                    break;
                default: // Minimal
                    TickMinimal(region, deltaDays);
                    break;
            }
        }

        /// <summary>
        /// Promotes Undiscovered regions within <paramref name="horizonRadius"/> sectors
        /// of <paramref name="playerSectorPosition"/> to OnHorizon when the player
        /// expands the horizon outward.
        /// </summary>
        public void ExpandHorizon(Vector2Int playerSectorPosition, int horizonRadius)
        {
            if (horizonRadius < 0)
                return;

            int maxDistanceSquared = horizonRadius * horizonRadius;

            foreach (var region in _registry.AllRegions)
            {
                if (region.simulationState != RegionSimulationState.Undiscovered)
                    continue;

                if (!TryGetSectorPositionFromRegionId(region.regionId, out var sectorPosition))
                    continue;

                Vector2Int delta = sectorPosition - playerSectorPosition;
                if (delta.sqrMagnitude <= maxDistanceSquared)
                    region.simulationState = RegionSimulationState.OnHorizon;
            }
        }

        /// <summary>
        /// Parses the integer XY coordinates embedded in a region ID of the form
        /// "region_X_Y" and returns them as a <see cref="Vector2Int"/>.
        /// Returns false when the ID does not match the expected format.
        /// </summary>
        private static bool TryGetSectorPositionFromRegionId(string regionId, out Vector2Int sectorPosition)
        {
            sectorPosition = default;
            if (string.IsNullOrEmpty(regionId))
                return false;

            var parts = regionId.Split('_');
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
                return false;

            sectorPosition = new Vector2Int(x, y);
            return true;
        }

        /// <summary>
        /// Generates a new region at the horizon frontier, seeding it with faction
        /// presence inherited from neighbouring regions and a randomised initial state.
        /// The generated region is registered in the registry before being returned.
        /// </summary>
        public RegionData GenerateRegionAtHorizon(Vector2Int sectorPosition,
                                                   List<string> neighbourRegionIds)
        {
            var regionId   = $"region_{sectorPosition.x}_{sectorPosition.y}";
            var factionIds = new List<string>();

            if (neighbourRegionIds != null)
            {
                foreach (var nId in neighbourRegionIds)
                {
                    if (!_registry.TryGetRegion(nId, out var neighbour)) continue;
                    foreach (var fId in neighbour.factionIds)
                        if (!factionIds.Contains(fId))
                            factionIds.Add(fId);
                }
            }

            var region = new RegionData
            {
                regionId          = regionId,
                displayName       = $"Sector ({sectorPosition.x}, {sectorPosition.y})",
                simulationState   = RegionSimulationState.OnHorizon,
                factionIds        = factionIds,
                conflictLevel     = (float)(_rng.NextDouble() * 0.3),
                populationDensity = (float)_rng.NextDouble(),
            };

            _registry.Register(region);
            return region;
        }

        /// <summary>
        /// Marks a region as discovered when the player enters its sector.
        /// Promotes its simulation state to <see cref="RegionSimulationState.Discovered"/>.
        /// </summary>
        public void DiscoverRegion(string regionId)
        {
            if (_registry.TryGetRegion(regionId, out var region))
                region.simulationState = RegionSimulationState.Discovered;
        }

        // ── Fidelity tier mapping ─────────────────────────────────────────────

        /// <summary>
        /// Maps a <see cref="RegionSimulationState"/> to its <see cref="HorizonFidelityTier"/>.
        /// </summary>
        public static HorizonFidelityTier GetFidelityTier(RegionSimulationState state)
        {
            switch (state)
            {
                case RegionSimulationState.Discovered:
                case RegionSimulationState.FullyMapped:
                    return HorizonFidelityTier.Full;
                case RegionSimulationState.OnHorizon:
                    return HorizonFidelityTier.Summary;
                default:
                    return HorizonFidelityTier.Minimal;
            }
        }

        // ── Tier-specific tick implementations ───────────────────────────────

        private void TickFull(RegionData region, float deltaDays)
        {
            SimulateFactionActivity(region, deltaDays);
            RecordResourceFlows(region, deltaDays, FullVariance);
        }

        private void TickSummary(RegionData region, float deltaDays)
        {
            // Summary: faction state changes at same rate; resource flows at lower variance
            SimulateFactionActivity(region, deltaDays);
            RecordResourceFlows(region, deltaDays, SummaryVariance);
        }

        private void TickMinimal(RegionData region, float deltaDays)
        {
            // Minimal: only drift the conflict level — no resource tracking
            UpdateConflictLevel(region, deltaDays);
        }

        // ── Faction activity simulation ───────────────────────────────────────

        private void SimulateFactionActivity(RegionData region, float deltaDays)
        {
            if (region.factionIds.Count >= 2)
            {
                // War declaration check
                if (_rng.NextDouble() < WarProbabilityPerDay * deltaDays)
                    TryDeclareWar(region);

                // Alliance formation check (only when conflict is low)
                if (region.conflictLevel < AllianceConflictCeiling
                    && _rng.NextDouble() < AllianceProbabilityPerDay * deltaDays)
                    TryFormAlliance(region);
            }

            UpdateConflictLevel(region, deltaDays);
        }

        private void TryDeclareWar(RegionData region)
        {
            var attacker = region.factionIds[_rng.Next(region.factionIds.Count)];
            var defender = region.factionIds[_rng.Next(region.factionIds.Count)];
            if (attacker == defender) return;

            var evt = new HistoricalEvent
            {
                eventId            = $"war_{region.regionId}_{attacker}_{defender}",
                description        = $"Faction {attacker} declared war on {defender} in {region.regionId}.",
                involvedFactionIds = new[] { attacker, defender },
            };

            _factionHistory.RecordFactionEvent(attacker, evt);
            _factionHistory.RecordFactionEvent(defender, evt);

            region.conflictLevel = Mathf.Min(1f, region.conflictLevel + 0.2f);
        }

        private void TryFormAlliance(RegionData region)
        {
            var f1 = region.factionIds[_rng.Next(region.factionIds.Count)];
            var f2 = region.factionIds[_rng.Next(region.factionIds.Count)];
            if (f1 == f2) return;

            var evt = new HistoricalEvent
            {
                eventId            = $"alliance_{region.regionId}_{f1}_{f2}",
                description        = $"Faction {f1} formed an alliance with {f2} in {region.regionId}.",
                involvedFactionIds = new[] { f1, f2 },
            };

            _factionHistory.RecordFactionEvent(f1, evt);
            _factionHistory.RecordFactionEvent(f2, evt);
        }

        private void UpdateConflictLevel(RegionData region, float deltaDays)
        {
            if (region.factionIds.Count >= 2)
                region.conflictLevel = Mathf.Min(1f, region.conflictLevel + ConflictGrowthRate * deltaDays);
            else
                region.conflictLevel = Mathf.Max(0f, region.conflictLevel - ConflictDecayRate * deltaDays);
        }

        // ── Cached resource type values (avoids per-tick allocation + reflection) ─

        private static readonly ResourceType[] _resourceTypes =
            (ResourceType[])Enum.GetValues(typeof(ResourceType));

        // ── Resource flow recording ────────────────────────────────────────────

        private void RecordResourceFlows(RegionData region, float deltaDays, float variance)
        {
            float baseFlow       = BaseResourceFlow * region.populationDensity;
            float conflictFactor = 1f - region.conflictLevel * 0.5f;

            foreach (ResourceType resource in _resourceTypes)
            {
                float jitter = (float)(_rng.NextDouble() * 2.0 - 1.0) * variance;
                float amount = Mathf.Max(0f, baseFlow * conflictFactor * (1f + jitter)) * deltaDays;
                _registry.RecordDailyResource(region.regionId, resource, amount);
            }
        }
    }
}
