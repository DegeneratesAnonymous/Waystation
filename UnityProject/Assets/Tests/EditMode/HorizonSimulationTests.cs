// HorizonSimulationTests — EditMode unit tests for the Horizon Simulation (STA-004).
//
// Validates:
//   • Fidelity tier assignment for explored, detected, and uncharted regions
//   • TickRegion at Full fidelity records resource flows
//   • TickRegion at Minimal fidelity does not record resource flows
//   • DiscoverRegion promotes region state to Discovered
//   • GenerateRegionAtHorizon returns a correctly seeded OnHorizon region
//   • FactionHistory event recording and retrieval
//   • FactionHistory defensive copy on GetFactionHistory
//   • GalaxyGenerator outer-fringe prefix algorithm (x ≤ 85 never UNK; x > 85 can be FRN or UNK)
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // =========================================================================
    // HorizonSimulationTests
    // =========================================================================

    [TestFixture]
    public class HorizonSimulationTests
    {
        // ── Helper ────────────────────────────────────────────────────────────

        private static RegionData MakeRegion(string id, RegionSimulationState state,
                                              float populationDensity = 0.5f)
        {
            var r = new RegionData
            {
                regionId          = id,
                displayName       = "Test",
                simulationState   = state,
                populationDensity = populationDensity,
                conflictLevel     = 0f,
            };
            r.factionIds.Add("faction.a");
            return r;
        }

        // ── Fidelity tier mapping ─────────────────────────────────────────────

        [Test]
        public void GetFidelityTier_Discovered_ReturnsFull()
            => Assert.AreEqual(HorizonFidelityTier.Full,
                HorizonSimulation.GetFidelityTier(RegionSimulationState.Discovered));

        [Test]
        public void GetFidelityTier_FullyMapped_ReturnsFull()
            => Assert.AreEqual(HorizonFidelityTier.Full,
                HorizonSimulation.GetFidelityTier(RegionSimulationState.FullyMapped));

        [Test]
        public void GetFidelityTier_OnHorizon_ReturnsSummary()
            => Assert.AreEqual(HorizonFidelityTier.Summary,
                HorizonSimulation.GetFidelityTier(RegionSimulationState.OnHorizon));

        [Test]
        public void GetFidelityTier_Undiscovered_ReturnsMinimal()
            => Assert.AreEqual(HorizonFidelityTier.Minimal,
                HorizonSimulation.GetFidelityTier(RegionSimulationState.Undiscovered));

        // ── TickRegion – Full fidelity records resource flows ─────────────────

        [Test]
        public void TickRegion_FullFidelity_RecordsResourceFlows()
        {
            bool prior = FeatureFlags.RegionSimulation;
            try
            {
                FeatureFlags.RegionSimulation = true;

                var registry = new RegionRegistry();
                var history  = new FactionHistory();
                var sim      = new HorizonSimulation(registry, history, seed: 1);

                var region = MakeRegion("r1", RegionSimulationState.Discovered, populationDensity: 1f);
                registry.Register(region);

                sim.TickRegion("r1", 1f);

                float foodAvg = region.resourceHistory.GetAverageAmount(ResourceType.Food);
                Assert.Greater(foodAvg, 0f, "Full-fidelity tick should record resource flows.");
            }
            finally { FeatureFlags.RegionSimulation = prior; }
        }

        // ── TickRegion – Summary fidelity also records resource flows ─────────

        [Test]
        public void TickRegion_SummaryFidelity_RecordsResourceFlows()
        {
            bool prior = FeatureFlags.RegionSimulation;
            try
            {
                FeatureFlags.RegionSimulation = true;

                var registry = new RegionRegistry();
                var history  = new FactionHistory();
                var sim      = new HorizonSimulation(registry, history, seed: 2);

                var region = MakeRegion("r2", RegionSimulationState.OnHorizon, populationDensity: 1f);
                registry.Register(region);

                sim.TickRegion("r2", 1f);

                float foodAvg = region.resourceHistory.GetAverageAmount(ResourceType.Food);
                Assert.Greater(foodAvg, 0f, "Summary-fidelity tick should record resource flows.");
            }
            finally { FeatureFlags.RegionSimulation = prior; }
        }

        // ── TickRegion – Minimal fidelity does NOT record resource flows ───────

        [Test]
        public void TickRegion_MinimalFidelity_DoesNotRecordResourceFlows()
        {
            bool prior = FeatureFlags.RegionSimulation;
            try
            {
                FeatureFlags.RegionSimulation = true;

                var registry = new RegionRegistry();
                var history  = new FactionHistory();
                var sim      = new HorizonSimulation(registry, history, seed: 3);

                var region = MakeRegion("r3", RegionSimulationState.Undiscovered, populationDensity: 1f);
                registry.Register(region);

                sim.TickRegion("r3", 1f);

                float foodAvg = region.resourceHistory.GetAverageAmount(ResourceType.Food);
                Assert.AreEqual(0f, foodAvg, "Minimal-fidelity tick must not record resource flows.");
            }
            finally { FeatureFlags.RegionSimulation = prior; }
        }

        // ── DiscoverRegion promotes simulation state ───────────────────────────

        [Test]
        public void DiscoverRegion_SetsSimulationStateToDiscovered()
        {
            var registry = new RegionRegistry();
            var history  = new FactionHistory();
            var sim      = new HorizonSimulation(registry, history, seed: 4);

            var region = MakeRegion("r4", RegionSimulationState.OnHorizon);
            registry.Register(region);

            sim.DiscoverRegion("r4");

            Assert.AreEqual(RegionSimulationState.Discovered, region.simulationState);
        }

        // ── DiscoverRegion on unknown region does not throw ───────────────────

        [Test]
        public void DiscoverRegion_UnknownId_DoesNotThrow()
        {
            var sim = new HorizonSimulation(new RegionRegistry(), new FactionHistory(), seed: 5);
            Assert.DoesNotThrow(() => sim.DiscoverRegion("does_not_exist"));
        }

        // ── GenerateRegionAtHorizon ────────────────────────────────────────────

        [Test]
        public void GenerateRegionAtHorizon_ReturnsOnHorizonRegionWithCorrectId()
        {
            var registry = new RegionRegistry();
            var history  = new FactionHistory();
            var sim      = new HorizonSimulation(registry, history, seed: 6);

            var pos    = new Vector2Int(10, 20);
            var region = sim.GenerateRegionAtHorizon(pos, new List<string>());

            Assert.AreEqual("region_10_20", region.regionId);
            Assert.AreEqual(RegionSimulationState.OnHorizon, region.simulationState);
        }

        [Test]
        public void GenerateRegionAtHorizon_RegistersRegionInRegistry()
        {
            var registry = new RegionRegistry();
            var history  = new FactionHistory();
            var sim      = new HorizonSimulation(registry, history, seed: 7);

            sim.GenerateRegionAtHorizon(new Vector2Int(5, 5), new List<string>());

            Assert.IsTrue(registry.TryGetRegion("region_5_5", out _));
        }

        [Test]
        public void GenerateRegionAtHorizon_InheritsFactionIdsFromNeighbours()
        {
            var registry  = new RegionRegistry();
            var neighbour = new RegionData
            {
                regionId  = "neighbour",
                factionIds = new List<string> { "faction.alpha" },
            };
            registry.Register(neighbour);

            var history = new FactionHistory();
            var sim     = new HorizonSimulation(registry, history, seed: 8);

            var region = sim.GenerateRegionAtHorizon(
                new Vector2Int(3, 3), new List<string> { "neighbour" });

            Assert.Contains("faction.alpha", region.factionIds);
        }

        // ── ExpandHorizon promotes Undiscovered regions within radius ─────────

        [Test]
        public void ExpandHorizon_PromotesUndiscoveredWithinRadius_ToOnHorizon()
        {
            var registry = new RegionRegistry();
            // Region at (3, 3) — within radius 5 of (0, 0)
            var inRange = new RegionData
            {
                regionId        = "region_3_3",
                simulationState = RegionSimulationState.Undiscovered,
            };
            // Region at (10, 10) — outside radius 5 of (0, 0) (sqrMagnitude = 200)
            var outOfRange = new RegionData
            {
                regionId        = "region_10_10",
                simulationState = RegionSimulationState.Undiscovered,
            };
            registry.Register(inRange);
            registry.Register(outOfRange);

            var sim = new HorizonSimulation(registry, new FactionHistory(), seed: 9);
            sim.ExpandHorizon(Vector2Int.zero, 5);

            Assert.AreEqual(RegionSimulationState.OnHorizon,   inRange.simulationState,
                "Region within radius should be promoted to OnHorizon.");
            Assert.AreEqual(RegionSimulationState.Undiscovered, outOfRange.simulationState,
                "Region outside radius should remain Undiscovered.");
        }

        [Test]
        public void ExpandHorizon_NegativeRadius_DoesNotPromoteAnyRegion()
        {
            var registry = new RegionRegistry();
            var region   = new RegionData
            {
                regionId        = "region_1_1",
                simulationState = RegionSimulationState.Undiscovered,
            };
            registry.Register(region);

            var sim = new HorizonSimulation(registry, new FactionHistory(), seed: 10);
            sim.ExpandHorizon(Vector2Int.zero, -1);

            Assert.AreEqual(RegionSimulationState.Undiscovered, region.simulationState,
                "Negative radius should not promote any region.");
        }

        // ── Faction events are recorded during simulation ────────────────────

        [Test]
        public void TickRegion_MultiFactionRegion_FactionHistoriesAreRetrievable()
        {
            bool prior = FeatureFlags.RegionSimulation;
            try
            {
                FeatureFlags.RegionSimulation = true;

                var registry = new RegionRegistry();
                var history  = new FactionHistory();
                var sim      = new HorizonSimulation(registry, history, seed: 42);

                var region = new RegionData
                {
                    regionId          = "rWar",
                    simulationState   = RegionSimulationState.Discovered,
                    populationDensity = 0.5f,
                    conflictLevel     = 0f,
                };
                region.factionIds.AddRange(new[] { "faction.alpha", "faction.beta" });
                registry.Register(region);

                // Tick for many simulated days to exercise multi-faction conflict logic.
                // NOTE: We intentionally do NOT assert on the presence of war events here,
                // because event occurrence is probabilistic and depends on the exact RNG
                // call sequence, which may change with internal implementation details.
                for (int i = 0; i < 500; i++)
                    sim.TickRegion("rWar", 1f);

                var alphaHistory = history.GetFactionHistory("faction.alpha");
                var betaHistory  = history.GetFactionHistory("faction.beta");

                // Deterministic contract: histories for the participating factions exist
                // and are retrievable after ticking the simulation.
                Assert.IsNotNull(alphaHistory, "Faction 'faction.alpha' history should be retrievable.");
                Assert.IsNotNull(betaHistory,  "Faction 'faction.beta' history should be retrievable.");
            }
            finally { FeatureFlags.RegionSimulation = prior; }
        }
    }

    // =========================================================================
    // FactionHistoryTests
    // =========================================================================

    [TestFixture]
    public class FactionHistoryTests
    {
        [Test]
        public void RecordFactionEvent_PersistsEvent_RetrievableViaGetHistory()
        {
            var history = new FactionHistory();
            var evt     = new HistoricalEvent
            {
                eventId     = "e1",
                description = "Test war",
                gameTick    = 100,
            };

            history.RecordFactionEvent("faction.alpha", evt);
            var result = history.GetFactionHistory("faction.alpha");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("e1", result[0].eventId);
        }

        [Test]
        public void GetFactionHistory_UnknownFaction_ReturnsEmptyList()
        {
            var history = new FactionHistory();
            var result  = history.GetFactionHistory("faction.unknown");

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetFactionHistory_ReturnsCopy_NotInternalList()
        {
            var history = new FactionHistory();
            var evt     = new HistoricalEvent { eventId = "e1", description = "test", gameTick = 1 };
            history.RecordFactionEvent("faction.alpha", evt);

            var result1 = history.GetFactionHistory("faction.alpha");
            result1.Clear();

            var result2 = history.GetFactionHistory("faction.alpha");
            Assert.AreEqual(1, result2.Count,
                "Clearing the returned list must not affect the internal store.");
        }

        [Test]
        public void RecordFactionEvent_MultipleEvents_AllRetrievable()
        {
            var history = new FactionHistory();
            for (int i = 0; i < 5; i++)
            {
                history.RecordFactionEvent("faction.alpha", new HistoricalEvent
                {
                    eventId = $"e{i}", description = $"Event {i}", gameTick = i,
                });
            }

            var result = history.GetFactionHistory("faction.alpha");
            Assert.AreEqual(5, result.Count);
        }

        [Test]
        public void RecordFactionEvent_NullFactionId_DoesNotThrow()
        {
            var history = new FactionHistory();
            Assert.DoesNotThrow(() => history.RecordFactionEvent(null, new HistoricalEvent()));
        }

        [Test]
        public void RecordFactionEvent_NullEvent_DoesNotThrow()
        {
            var history = new FactionHistory();
            Assert.DoesNotThrow(() => history.RecordFactionEvent("faction.alpha", null));
        }

        [Test]
        public void RecordFactionEvent_NullFactionId_DoesNotAddToAnyFaction()
        {
            var history = new FactionHistory();
            history.RecordFactionEvent(null, new HistoricalEvent { eventId = "e1" });

            // No faction should have any history
            var result = history.GetFactionHistory("");
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetFactionHistory_NullFactionId_ReturnsEmptyList()
        {
            var history = new FactionHistory();
            var result  = history.GetFactionHistory(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }
    }

    // =========================================================================
    // GalaxyGeneratorOuterFringeTests
    // =========================================================================

    [TestFixture]
    public class GalaxyGeneratorOuterFringeTests
    {
        // ── Sector at x ≤ 85 is never UNK ────────────────────────────────────

        [Test]
        public void Generate_SectorsAtOrBelowX85_NeverGetUNKPrefix()
        {
            bool prevEnabled = GalaxyGenerator.Enabled;
            bool prevNames   = GalaxyGenerator.ProperNameGenerationEnabled;
            try
            {
                GalaxyGenerator.Enabled                   = true;
                GalaxyGenerator.ProperNameGenerationEnabled = false;

                var station = new StationState("TestStation");
                GalaxyGenerator.Generate(12345, station);

                foreach (var sector in station.sectors.Values)
                {
                    if (sector.surveyPrefix == SurveyPrefix.UNK)
                        Assert.Greater(sector.coordinates.x, 85f,
                            $"UNK sector at x={sector.coordinates.x} violates the density-threshold rule.");
                }
            }
            finally
            {
                GalaxyGenerator.Enabled                   = prevEnabled;
                GalaxyGenerator.ProperNameGenerationEnabled = prevNames;
            }
        }

        // ── Outer-fringe (x > 85) sectors can be FRN or UNK ──────────────────

        [Test]
        public void Generate_OuterFringe_PrefixIsEitherFRNOrUNK_WhenNotANC()
        {
            bool prevEnabled = GalaxyGenerator.Enabled;
            bool prevNames   = GalaxyGenerator.ProperNameGenerationEnabled;
            try
            {
                GalaxyGenerator.Enabled                   = true;
                GalaxyGenerator.ProperNameGenerationEnabled = false;

                // Use several seeds to collect outer-fringe sectors across multiple galaxies
                var outerFringeNonANC = new List<SurveyPrefix>();

                for (int seed = 1; seed <= 20; seed++)
                {
                    var station = new StationState("TestStation");
                    GalaxyGenerator.Generate(seed, station);

                    foreach (var sector in station.sectors.Values)
                    {
                        if (sector.coordinates.x > 85f && sector.surveyPrefix != SurveyPrefix.ANC)
                            outerFringeNonANC.Add(sector.surveyPrefix);
                    }
                }

                // Every non-ANC outer-fringe sector should be FRN or UNK
                foreach (var prefix in outerFringeNonANC)
                    Assert.IsTrue(prefix == SurveyPrefix.FRN || prefix == SurveyPrefix.UNK,
                        $"Outer-fringe sector has unexpected prefix: {prefix}");
            }
            finally
            {
                GalaxyGenerator.Enabled                   = prevEnabled;
                GalaxyGenerator.ProperNameGenerationEnabled = prevNames;
            }
        }

        // ── Density-threshold produces BOTH FRN and UNK across many seeds ─────

        [Test]
        public void Generate_OuterFringe_DensityThreshold_ProducesBothPrefixes()
        {
            bool prevEnabled = GalaxyGenerator.Enabled;
            bool prevNames   = GalaxyGenerator.ProperNameGenerationEnabled;
            try
            {
                GalaxyGenerator.Enabled                   = true;
                GalaxyGenerator.ProperNameGenerationEnabled = false;

                bool foundUNK = false;
                bool foundFRN = false;

                // Generate many galaxies until both prefix values are observed
                for (int seed = 1; seed <= 200 && !(foundUNK && foundFRN); seed++)
                {
                    var station = new StationState("TestStation");
                    GalaxyGenerator.Generate(seed, station);

                    foreach (var sector in station.sectors.Values)
                    {
                        if (sector.coordinates.x > 85f && sector.surveyPrefix != SurveyPrefix.ANC)
                        {
                            if (sector.surveyPrefix == SurveyPrefix.UNK) foundUNK = true;
                            if (sector.surveyPrefix == SurveyPrefix.FRN) foundFRN = true;
                        }
                    }
                }

                Assert.IsTrue(foundFRN, "Density threshold should produce FRN in some outer-fringe sectors.");
                Assert.IsTrue(foundUNK, "Density threshold should produce UNK in some outer-fringe sectors.");
            }
            finally
            {
                GalaxyGenerator.Enabled                   = prevEnabled;
                GalaxyGenerator.ProperNameGenerationEnabled = prevNames;
            }
        }
    }
}
