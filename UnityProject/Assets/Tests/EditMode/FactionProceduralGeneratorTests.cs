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
}
