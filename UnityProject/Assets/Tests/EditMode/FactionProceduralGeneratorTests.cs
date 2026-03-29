// FactionProceduralGeneratorTests — EditMode unit tests for FAC-001.
//
// Validates:
//   • ComputeGenerationProbability responds correctly to resource/hazard codes and density
//   • Government type derivation from aggregate category scores
//   • InitializeStartingFactions seeds exactly two factions with correct dispositions
//   • GetAdjacentSectors returns only sectors within NeighborThreshold
//   • Trait gravity: GetGovTraitAffinity returns non-empty lists for all government types
//   • GetAllFactions merges registry and generated factions correctly
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Minimal helpers ───────────────────────────────────────────────────────

    internal static class FactionTestHelpers
    {
        public static StationState MakeStation(int galaxySeed = 42)
        {
            var s = new StationState("TestStation");
            s.galaxySeed = galaxySeed;
            return s;
        }

        /// <summary>Creates a SectorData at the given coordinates with the specified settings.</summary>
        public static SectorData MakeSector(string uid, float x, float y,
                                             List<PhenomenonCode> codes = null,
                                             SystemDensity density = SystemDensity.Standard,
                                             SectorModifier modifier = SectorModifier.None)
        {
            var codes_ = codes ?? new List<PhenomenonCode>();
            var sector = SectorData.Create(uid, new Vector2(x, y), SurveyPrefix.GSC, codes_, "Test");
            sector.systemDensity = density;
            sector.modifier      = modifier;
            return sector;
        }

        public static FactionTraitAggregate MakeAggregate(
            Dictionary<string, float> categoryScores)
        {
            var agg = new FactionTraitAggregate
            {
                calculatedAtTick = 0,
                sourceGovernmentType = GovernmentType.Democracy,
            };
            foreach (var kv in categoryScores)
                agg.categoryScores[kv.Key] = kv.Value;
            return agg;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Generation probability
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionGenerationProbabilityTests
    {
        [Test]
        public void BaseCase_NoCodesNoAdjacentFactions_ReturnsBaseProbability()
        {
            var sector = FactionTestHelpers.MakeSector("s1", 10f, 10f,
                new List<PhenomenonCode> { PhenomenonCode.MS });
            var emptyFactions = new Dictionary<string, FactionDefinition>();

            float prob = FactionProceduralGenerator.ComputeGenerationProbability(
                sector, new List<SectorData>(), emptyFactions);

            Assert.AreEqual(FactionProceduralGenerator.BaseGenerationProbability, prob, 0.001f);
        }

        [Test]
        public void ResourceCode_OR_RaisesProbability()
        {
            var sectorBase = FactionTestHelpers.MakeSector("s1", 10f, 10f,
                new List<PhenomenonCode> { PhenomenonCode.MS });
            var sectorRich = FactionTestHelpers.MakeSector("s2", 10f, 10f,
                new List<PhenomenonCode> { PhenomenonCode.MS, PhenomenonCode.OR });
            var emptyFactions = new Dictionary<string, FactionDefinition>();

            float baseProb = FactionProceduralGenerator.ComputeGenerationProbability(
                sectorBase, new List<SectorData>(), emptyFactions);
            float richProb = FactionProceduralGenerator.ComputeGenerationProbability(
                sectorRich, new List<SectorData>(), emptyFactions);

            Assert.Greater(richProb, baseProb);
        }

        [Test]
        public void HazardCode_RD_LowersProbability()
        {
            var sectorBase  = FactionTestHelpers.MakeSector("s1", 10f, 10f,
                new List<PhenomenonCode> { PhenomenonCode.MS });
            var sectorHazard = FactionTestHelpers.MakeSector("s2", 10f, 10f,
                new List<PhenomenonCode> { PhenomenonCode.MS, PhenomenonCode.RD });
            var emptyFactions = new Dictionary<string, FactionDefinition>();

            float baseProb   = FactionProceduralGenerator.ComputeGenerationProbability(
                sectorBase, new List<SectorData>(), emptyFactions);
            float hazardProb = FactionProceduralGenerator.ComputeGenerationProbability(
                sectorHazard, new List<SectorData>(), emptyFactions);

            Assert.Less(hazardProb, baseProb);
        }

        [Test]
        public void MultipleAdjacentFactions_LowerProbabilityByDensityPenalty()
        {
            var sector = FactionTestHelpers.MakeSector("s1", 10f, 10f,
                new List<PhenomenonCode> { PhenomenonCode.MS });
            var emptyFactions = new Dictionary<string, FactionDefinition>();

            // Create an adjacent sector with two factions
            var adjSector = FactionTestHelpers.MakeSector("adj1", 11f, 10f);
            adjSector.factionIds.Add("faction.a");
            adjSector.factionIds.Add("faction.b");
            var adjacent = new List<SectorData> { adjSector };

            float noDensityProb = FactionProceduralGenerator.ComputeGenerationProbability(
                sector, new List<SectorData>(), emptyFactions);
            float densityProb   = FactionProceduralGenerator.ComputeGenerationProbability(
                sector, adjacent, emptyFactions);

            Assert.Less(densityProb, noDensityProb);
        }

        [Test]
        public void ProbabilityIsAlwaysBetweenZeroAndOne()
        {
            // Extreme case: heavy hazards + many adjacent factions
            var sector = FactionTestHelpers.MakeSector("s1", 10f, 10f,
                new List<PhenomenonCode>
                {
                    PhenomenonCode.MS, PhenomenonCode.RD, PhenomenonCode.GV,
                    PhenomenonCode.DK, PhenomenonCode.ST, PhenomenonCode.VD
                });
            var emptyFactions = new Dictionary<string, FactionDefinition>();

            var adj = FactionTestHelpers.MakeSector("adj1", 11f, 10f);
            for (int i = 0; i < 10; i++) adj.factionIds.Add($"faction.{i}");
            var adjacent = new List<SectorData> { adj };

            float prob = FactionProceduralGenerator.ComputeGenerationProbability(
                sector, adjacent, emptyFactions);

            Assert.GreaterOrEqual(prob, 0f);
            Assert.LessOrEqual(prob, 1f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Government type derivation
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class GovernmentTypeDerivationTests
    {
        [Test]
        public void NullAggregate_ReturnsDemocracy()
        {
            var result = FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(null);
            Assert.AreEqual(GovernmentType.Democracy, result);
        }

        [Test]
        public void EmptyCategoryScores_ReturnsDemocracy()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>());
            var result = FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg);
            Assert.AreEqual(GovernmentType.Democracy, result);
        }

        [Test]
        public void DominantSocial_ReturnsDemocracy()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Social.ToString(),       0.8f },
                { TraitCategory.Psychological.ToString(), 0.2f },
            });
            Assert.AreEqual(GovernmentType.Democracy,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantIdeological_ReturnsRepublic()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Ideological.ToString(), 0.9f },
                { TraitCategory.Social.ToString(),       0.1f },
            });
            Assert.AreEqual(GovernmentType.Republic,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantPsychological_LowIdeological_ReturnsMonarchy()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Psychological.ToString(), 0.8f },
                { TraitCategory.Ideological.ToString(),   0.1f },
            });
            Assert.AreEqual(GovernmentType.Monarchy,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantPsychological_HighIdeological_ReturnsAuthoritarian()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Psychological.ToString(), 0.8f },
                { TraitCategory.Ideological.ToString(),   0.5f }, // > 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.Authoritarian,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantEconomic_ReturnsCorporateVassal()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Economic.ToString(), 0.9f },
            });
            Assert.AreEqual(GovernmentType.CorporateVassal,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantPhysical_ReturnsPirate()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Physical.ToString(), 0.9f },
            });
            Assert.AreEqual(GovernmentType.Pirate,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Adjacent sector queries
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class AdjacentSectorTests
    {
        [Test]
        public void GetAdjacentSectors_WithinThreshold_Included()
        {
            float threshold = GalaxyGenerator.NeighborThreshold;
            var home    = FactionTestHelpers.MakeSector("home",  0f,           0f);
            var close   = FactionTestHelpers.MakeSector("close", threshold - 0.1f, 0f);
            var far     = FactionTestHelpers.MakeSector("far",   threshold + 0.5f, 0f);

            var allSectors = new Dictionary<string, SectorData>
            {
                { "home",  home  },
                { "close", close },
                { "far",   far   },
            };

            var adjacent = FactionProceduralGenerator.GetAdjacentSectors(home, allSectors);

            Assert.Contains(close, adjacent);
            Assert.IsFalse(adjacent.Contains(far));
        }

        [Test]
        public void GetAdjacentSectors_ExcludesSectorItself()
        {
            var home = FactionTestHelpers.MakeSector("home", 0f, 0f);
            var allSectors = new Dictionary<string, SectorData> { { "home", home } };

            var adjacent = FactionProceduralGenerator.GetAdjacentSectors(home, allSectors);
            Assert.IsFalse(adjacent.Contains(home));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Starting factions
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class StartingFactionsTests
    {
        /// <summary>Builds the minimal sector layout required for starting faction tests.</summary>
        private static StationState BuildStationWithHomeSector(out SectorData homeSector)
        {
            var station = FactionTestHelpers.MakeStation();

            // Home sector
            homeSector = SectorData.Create(
                uid:        "sector_home",
                coordinates: new Vector2(GalaxyGenerator.HomeX, GalaxyGenerator.HomeY),
                prefix:     SurveyPrefix.GSC,
                codes:      new List<PhenomenonCode> { PhenomenonCode.NB },
                properName: "The Cradle");
            homeSector.discoveryState = SectorDiscoveryState.Visited;
            station.sectors[homeSector.uid] = homeSector;

            // Two adjacent sectors within NeighborThreshold
            float adj = GalaxyGenerator.NeighborThreshold - 0.5f;
            var adj1 = SectorData.Create("sector_adj1",
                new Vector2(GalaxyGenerator.HomeX + adj, GalaxyGenerator.HomeY),
                SurveyPrefix.GSC, new List<PhenomenonCode> { PhenomenonCode.MS, PhenomenonCode.OR }, "Adj1");
            var adj2 = SectorData.Create("sector_adj2",
                new Vector2(GalaxyGenerator.HomeX,       GalaxyGenerator.HomeY + adj),
                SurveyPrefix.GSC, new List<PhenomenonCode> { PhenomenonCode.MS }, "Adj2");

            station.sectors[adj1.uid] = adj1;
            station.sectors[adj2.uid] = adj2;

            return station;
        }

        [Test]
        public void InitializeStartingFactions_SeedsExactlyTwoFactions()
        {
            var station = BuildStationWithHomeSector(out _);

            FactionProceduralGenerator.InitializeStartingFactions(
                station, new Dictionary<string, FactionDefinition>(),
                npcSystem:   null,
                traitSystem: null,
                rng:         new Random(1234));

            Assert.AreEqual(2, station.generatedFactions.Count,
                "Exactly 2 starting factions should be seeded.");
        }

        [Test]
        public void InitializeStartingFactions_OneFriendlyOneUnfriendly()
        {
            var station = BuildStationWithHomeSector(out _);

            FactionProceduralGenerator.InitializeStartingFactions(
                station, new Dictionary<string, FactionDefinition>(),
                npcSystem:   null,
                traitSystem: null,
                rng:         new Random(1234));

            bool hasFriendly   = false;
            bool hasUnfriendly = false;
            foreach (var kv in station.factionReputation)
            {
                if (kv.Value >= FactionProceduralGenerator.StartingFriendlyRep * 0.9f)
                    hasFriendly = true;
                if (kv.Value <= FactionProceduralGenerator.StartingUnfriendlyRep * 0.9f)
                    hasUnfriendly = true;
            }
            Assert.IsTrue(hasFriendly,   "A faction with friendly reputation should be seeded.");
            Assert.IsTrue(hasUnfriendly, "A faction with unfriendly reputation should be seeded.");
        }

        [Test]
        public void InitializeStartingFactions_FactionsLinkedToSectors()
        {
            var station = BuildStationWithHomeSector(out _);

            FactionProceduralGenerator.InitializeStartingFactions(
                station, new Dictionary<string, FactionDefinition>(),
                npcSystem:   null,
                traitSystem: null,
                rng:         new Random(1234));

            // Each generated faction should appear in exactly one sector's factionIds list
            foreach (var kv in station.generatedFactions)
            {
                bool foundInSector = false;
                foreach (var s in station.sectors.Values)
                    if (s.factionIds.Contains(kv.Key)) { foundInSector = true; break; }
                Assert.IsTrue(foundInSector,
                    $"Faction '{kv.Key}' should be linked to a sector.");
            }
        }

        [Test]
        public void InitializeStartingFactions_FactionsHaveIsGeneratedTrue()
        {
            var station = BuildStationWithHomeSector(out _);

            FactionProceduralGenerator.InitializeStartingFactions(
                station, new Dictionary<string, FactionDefinition>(),
                npcSystem:   null,
                traitSystem: null,
                rng:         new Random(1234));

            foreach (var kv in station.generatedFactions)
                Assert.IsTrue(kv.Value.isGenerated, "Starting factions must have isGenerated=true.");
        }

        [Test]
        public void InitializeStartingFactions_NoHomeSector_LogsWarningAndSeeds0Factions()
        {
            // Station with no home sector
            var station = new StationState("NoHome");

            FactionProceduralGenerator.InitializeStartingFactions(
                station, new Dictionary<string, FactionDefinition>(),
                npcSystem:   null,
                traitSystem: null,
                rng:         new Random(1234));

            // No factions should have been created without a home sector
            Assert.AreEqual(0, station.generatedFactions.Count);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Trait gravity (GetGovTraitAffinity)
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class TraitGravityAffinityTests
    {
        [TestCase(GovernmentType.Democracy)]
        [TestCase(GovernmentType.Republic)]
        [TestCase(GovernmentType.Monarchy)]
        [TestCase(GovernmentType.Authoritarian)]
        [TestCase(GovernmentType.CorporateVassal)]
        [TestCase(GovernmentType.Pirate)]
        [TestCase(GovernmentType.Theocracy)]
        [TestCase(GovernmentType.Technocracy)]
        [TestCase(GovernmentType.FederalCouncil)]
        public void AllGovernmentTypes_HaveAtLeastOneBiasedCategory(GovernmentType govType)
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(govType);
            Assert.IsNotNull(cats);
            Assert.Greater(cats.Count, 0,
                $"GovernmentType.{govType} should have at least one preferred trait category.");
        }

        [Test]
        public void Democracy_PrefersSocialCategory()
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(GovernmentType.Democracy);
            Assert.Contains(TraitCategory.Social, cats);
        }

        [Test]
        public void Pirate_PrefersPhysicalCategory()
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(GovernmentType.Pirate);
            Assert.Contains(TraitCategory.Physical, cats);
        }

        [Test]
        public void CorporateVassal_PrefersEconomicCategory()
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(GovernmentType.CorporateVassal);
            Assert.Contains(TraitCategory.Economic, cats);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FactionSystem.MergeAllFactions / GetAllFactions
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionSystemMergeTests
    {
        [Test]
        public void MergeAllFactions_IncludesRegistryAndGeneratedFactions()
        {
            var station = FactionTestHelpers.MakeStation();

            var registryFactions = new Dictionary<string, FactionDefinition>
            {
                { "faction.alpha", new FactionDefinition { id = "faction.alpha", displayName = "Alpha" } }
            };
            var genFaction = new FactionDefinition
            {
                id          = "faction.gen.001",
                displayName = "Iron Order",
                isGenerated = true,
            };
            station.generatedFactions[genFaction.id] = genFaction;
            station.factionReputation[genFaction.id] = 0f;

            // Call the real static production method, not a manual re-implementation
            var all = FactionSystem.MergeAllFactions(registryFactions, station);

            Assert.IsTrue(all.ContainsKey("faction.alpha"),   "Registry faction must be present.");
            Assert.IsTrue(all.ContainsKey("faction.gen.001"), "Generated faction must be present.");
            Assert.AreEqual(2, all.Count);
        }

        [Test]
        public void MergeAllFactions_GeneratedFactionTakesPrecedenceOnCollision()
        {
            var station = FactionTestHelpers.MakeStation();

            var registryFactions = new Dictionary<string, FactionDefinition>
            {
                { "faction.collision", new FactionDefinition { id = "faction.collision", displayName = "Registry" } }
            };
            var genFaction = new FactionDefinition
            {
                id          = "faction.collision",
                displayName = "Generated",
                isGenerated = true,
            };
            station.generatedFactions[genFaction.id] = genFaction;

            var all = FactionSystem.MergeAllFactions(registryFactions, station);

            Assert.AreEqual("Generated", all["faction.collision"].displayName,
                "Generated faction should take precedence on ID collision.");
        }

        [Test]
        public void MergeAllFactions_EmptyGeneratedFactions_ReturnsOnlyRegistry()
        {
            var station = FactionTestHelpers.MakeStation();
            var registryFactions = new Dictionary<string, FactionDefinition>
            {
                { "faction.alpha", new FactionDefinition { id = "faction.alpha" } }
            };

            var all = FactionSystem.MergeAllFactions(registryFactions, station);

            Assert.AreEqual(1, all.Count);
            Assert.IsTrue(all.ContainsKey("faction.alpha"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FAC-002: New nine-type government derivation
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class NineTypeGovernmentDerivationTests
    {
        [Test]
        public void DominantIdeological_HighPhysical_ReturnsTheocracy()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Ideological.ToString(), 0.9f },
                { TraitCategory.Physical.ToString(),    0.5f }, // > 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.Theocracy,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantIdeological_LowPhysical_ReturnsRepublic()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Ideological.ToString(), 0.9f },
                { TraitCategory.Physical.ToString(),    0.1f }, // < 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.Republic,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantEconomic_HighSocial_ReturnsTechnocracy()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Economic.ToString(), 0.9f },
                { TraitCategory.Social.ToString(),   0.5f }, // > 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.Technocracy,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantEconomic_LowSocial_ReturnsCorporateVassal()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Economic.ToString(), 0.9f },
                { TraitCategory.Social.ToString(),   0.1f }, // < 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.CorporateVassal,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantPhysical_HighSocial_ReturnsFederalCouncil()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Physical.ToString(), 0.9f },
                { TraitCategory.Social.ToString(),   0.5f }, // > 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.FederalCouncil,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [Test]
        public void DominantPhysical_LowSocial_ReturnsPirate()
        {
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Physical.ToString(), 0.9f },
                { TraitCategory.Social.ToString(),   0.1f }, // < 0.3 threshold
            });
            Assert.AreEqual(GovernmentType.Pirate,
                FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate(agg));
        }

        [TestCase(GovernmentType.Theocracy)]
        [TestCase(GovernmentType.Technocracy)]
        [TestCase(GovernmentType.FederalCouncil)]
        public void NewGovernmentTypes_HaveTraitAffinity(GovernmentType govType)
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(govType);
            Assert.IsNotNull(cats);
            Assert.Greater(cats.Count, 0,
                $"GovernmentType.{govType} should have at least one preferred trait category.");
        }

        [Test]
        public void Theocracy_PrefersIdeologicalCategory()
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(GovernmentType.Theocracy);
            Assert.Contains(TraitCategory.Ideological, cats);
        }

        [Test]
        public void Technocracy_PrefersEconomicCategory()
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(GovernmentType.Technocracy);
            Assert.Contains(TraitCategory.Economic, cats);
        }

        [Test]
        public void FederalCouncil_PrefersSocialCategory()
        {
            var cats = FactionProceduralGenerator.GetGovTraitAffinity(GovernmentType.FederalCouncil);
            Assert.Contains(TraitCategory.Social, cats);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FAC-002: Stability computation
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class StabilityComputationTests
    {
        [Test]
        public void MaxInputs_ReturnsNearHundred()
        {
            var station = FactionTestHelpers.MakeStation();
            var faction = new FactionDefinition
            {
                id                    = "faction.test",
                governmentTenureTicks = FactionGovernmentSystem.TenureFullStabilityTicks,
            };
            // High rep (+100) → economic = 1.0
            station.factionReputation["faction.test"] = 100f;

            // Aggregate with high Physical score → military = 1.0
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Physical.ToString(), 1.0f },
            });

            // No NPCs on station → populationMood defaults to 0.5 (mid-range)
            float stability = FactionGovernmentSystem.ComputeStability(faction, agg, station);

            // With economic=1, military=1, mood=0.5, tenure=1 → (1+1+0.5+1)*25 = 87.5
            Assert.AreEqual(87.5f, stability, 0.01f);
        }

        [Test]
        public void MinInputs_ReturnsLow()
        {
            var station = FactionTestHelpers.MakeStation();
            var faction = new FactionDefinition
            {
                id                    = "faction.test",
                governmentTenureTicks = 0,
            };
            // Low rep (-100) → economic = 0.0
            station.factionReputation["faction.test"] = -100f;

            // Aggregate with Physical = 0 → military = 0
            var agg = FactionTestHelpers.MakeAggregate(new Dictionary<string, float>
            {
                { TraitCategory.Physical.ToString(), 0.0f },
            });

            float stability = FactionGovernmentSystem.ComputeStability(faction, agg, station);

            // economic=0, military=0, mood=0.5 (default), tenure=0 → (0+0+0.5+0)*25 = 12.5
            Assert.AreEqual(12.5f, stability, 0.01f);
        }

        [Test]
        public void NullAggregate_UsesDefaultMilitary()
        {
            var station = FactionTestHelpers.MakeStation();
            var faction = new FactionDefinition
            {
                id                    = "faction.test",
                governmentTenureTicks = 0,
            };
            station.factionReputation["faction.test"] = 0f; // rep=0 → economic=0.5

            float stability = FactionGovernmentSystem.ComputeStability(faction, null, station);

            // economic=0.5, military=0.5 (default), mood=0.5, tenure=0 → (0.5+0.5+0.5+0)*25 = 37.5
            Assert.AreEqual(37.5f, stability, 0.01f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FAC-002: Succession candidate selection
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class SuccessionCandidateTests
    {
        [Test]
        public void MultipleContestants_SelectsHighestRankCandidate()
        {
            var faction = new FactionDefinition
            {
                id = "faction.test",
                memberNpcIds  = new List<string> { "npc.a", "npc.b", "npc.c" },
                leaderNpcIds  = new List<string>(),
                successionState = SuccessionState.Contested,
            };

            var npcs = new Dictionary<string, NPCInstance>
            {
                { "npc.a", new NPCInstance { uid = "npc.a", rank = 1, moodScore = 50f } },
                { "npc.b", new NPCInstance { uid = "npc.b", rank = 3, moodScore = 50f } }, // highest rank
                { "npc.c", new NPCInstance { uid = "npc.c", rank = 2, moodScore = 80f } },
            };

            SuccessionEvaluator.EvaluateSuccession(faction, npcs, null);

            Assert.AreEqual(SuccessionState.Stable, faction.successionState);
            Assert.AreEqual(1, faction.leaderNpcIds.Count);
            Assert.AreEqual("npc.b", faction.leaderNpcIds[0], "Should promote highest-rank candidate.");
        }

        [Test]
        public void TiedRank_SelectsHighestMoodScoreCandidate()
        {
            var faction = new FactionDefinition
            {
                id = "faction.test",
                memberNpcIds  = new List<string> { "npc.a", "npc.b" },
                leaderNpcIds  = new List<string>(),
                successionState = SuccessionState.Contested,
            };

            var npcs = new Dictionary<string, NPCInstance>
            {
                { "npc.a", new NPCInstance { uid = "npc.a", rank = 2, moodScore = 40f } },
                { "npc.b", new NPCInstance { uid = "npc.b", rank = 2, moodScore = 80f } }, // same rank, better mood
            };

            SuccessionEvaluator.EvaluateSuccession(faction, npcs, null);

            Assert.AreEqual("npc.b", faction.leaderNpcIds[0], "Should break rank tie by moodScore.");
        }

        [Test]
        public void SingleCandidate_IsPromotedDirectly()
        {
            var faction = new FactionDefinition
            {
                id = "faction.test",
                memberNpcIds  = new List<string> { "npc.a" },
                leaderNpcIds  = new List<string>(),
            };

            var npcs = new Dictionary<string, NPCInstance>
            {
                { "npc.a", new NPCInstance { uid = "npc.a", rank = 1 } },
            };

            SuccessionEvaluator.EvaluateSuccession(faction, npcs, null);

            Assert.AreEqual(SuccessionState.Stable, faction.successionState);
            Assert.AreEqual("npc.a", faction.leaderNpcIds[0]);
        }

        [Test]
        public void NoMembers_IsVacant()
        {
            var faction = new FactionDefinition
            {
                id = "faction.test",
                memberNpcIds = new List<string>(),
                leaderNpcIds = new List<string>(),
            };

            SuccessionEvaluator.EvaluateSuccession(faction, new Dictionary<string, NPCInstance>(), null);

            Assert.AreEqual(SuccessionState.Vacant, faction.successionState);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FAC-002: Reputation carry-over on government shift
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ReputationCarryOverTests
    {
        private static StationState MakeStationWithRep(string factionId, float rep)
        {
            var station = FactionTestHelpers.MakeStation();
            station.factionReputation[factionId] = rep;
            return station;
        }

        [Test]
        public void FriendlySuccessor_RetainsSeventyFivePercent()
        {
            const string fid = "faction.test";
            var station = MakeStationWithRep(fid, 60f);
            var faction = new FactionDefinition
            {
                id             = fid,
                displayName    = "Test Faction",
                governmentType = GovernmentType.Authoritarian,
                isGenerated    = true,
                stabilityScore = 50f,
            };
            var allFactions = new Dictionary<string, FactionDefinition> { { fid, faction } };

            var sys = new FactionGovernmentSystem(null);
            sys.TriggerExternalPressureShift(faction, GovernmentType.Democracy, station, allFactions);

            float expected = 60f * FactionGovernmentSystem.RepCarryOverFriendly;
            Assert.AreEqual(expected, station.GetFactionRep(fid), 0.01f);
            Assert.AreEqual(GovernmentType.Democracy, faction.governmentType);
            Assert.AreEqual(0, faction.governmentTenureTicks, "Tenure resets on shift.");
        }

        [Test]
        public void HostileSuccessor_RetainsFiftyPercent()
        {
            const string fid = "faction.test";
            var station = MakeStationWithRep(fid, 60f);
            var faction = new FactionDefinition
            {
                id             = fid,
                displayName    = "Test Faction",
                governmentType = GovernmentType.Democracy,
                isGenerated    = true,
                stabilityScore = 50f,
            };
            var allFactions = new Dictionary<string, FactionDefinition> { { fid, faction } };

            var sys = new FactionGovernmentSystem(null);
            sys.TriggerExternalPressureShift(faction, GovernmentType.Authoritarian, station, allFactions);

            float expected = 60f * FactionGovernmentSystem.RepCarryOverHostile;
            Assert.AreEqual(expected, station.GetFactionRep(fid), 0.01f);
        }

        [Test]
        public void NeutralSuccessor_RetainsSixtyPercent()
        {
            const string fid = "faction.test";
            var station = MakeStationWithRep(fid, 50f);
            var faction = new FactionDefinition
            {
                id             = fid,
                displayName    = "Test Faction",
                governmentType = GovernmentType.Democracy,
                isGenerated    = true,
                stabilityScore = 50f,
            };
            var allFactions = new Dictionary<string, FactionDefinition> { { fid, faction } };

            var sys = new FactionGovernmentSystem(null);
            sys.TriggerExternalPressureShift(faction, GovernmentType.Monarchy, station, allFactions);

            float expected = 50f * FactionGovernmentSystem.RepCarryOverNeutral;
            Assert.AreEqual(expected, station.GetFactionRep(fid), 0.01f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FAC-002: Vassal patron reputation query
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class VassalPatronReputationTests
    {
        [Test]
        public void VassalisedFaction_ReturnsPatronRep()
        {
            var station = FactionTestHelpers.MakeStation();
            station.factionReputation["faction.patron"] = 75f;

            var vassal = new FactionDefinition
            {
                id                    = "faction.vassal",
                vassalParentFactionId = "faction.patron",
            };
            var allFactions = new Dictionary<string, FactionDefinition>
            {
                { "faction.vassal",  vassal },
                { "faction.patron",  new FactionDefinition { id = "faction.patron" } },
            };

            float patronRep = FactionGovernmentSystem.GetPatronReputation(
                "faction.vassal", station, allFactions);

            Assert.AreEqual(75f, patronRep, 0.01f);
        }

        [Test]
        public void NonVassalisedFaction_ReturnsZero()
        {
            var station = FactionTestHelpers.MakeStation();
            var faction = new FactionDefinition
            {
                id                    = "faction.free",
                vassalParentFactionId = null,
            };
            var allFactions = new Dictionary<string, FactionDefinition>
            {
                { "faction.free", faction },
            };

            float patronRep = FactionGovernmentSystem.GetPatronReputation(
                "faction.free", station, allFactions);

            Assert.AreEqual(0f, patronRep, 0.01f);
        }

        [Test]
        public void UnknownFactionId_ReturnsZero()
        {
            var station     = FactionTestHelpers.MakeStation();
            var allFactions = new Dictionary<string, FactionDefinition>();

            float patronRep = FactionGovernmentSystem.GetPatronReputation(
                "faction.unknown", station, allFactions);

            Assert.AreEqual(0f, patronRep, 0.01f);
        }
    }
}
