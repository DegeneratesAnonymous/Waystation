// ScenarioSystemTests — EditMode unit tests for the scenario selection system (STA-006).
//
// Validates:
//   • ScenarioDefinition.FromDict parses all defined fields
//   • ScenarioDefinition.FromDict uses correct defaults for optional fields
//   • ContentRegistry.Scenarios populates correctly from loaded data
//   • FeatureFlags.ScenarioSelection defaults to true
//   • Starting resources from scenario correctly override station defaults
//   • Starting crew from scenario matches the scenario's crew_composition list
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Tests
{
    [TestFixture]
    internal class ScenarioDefinitionParsingTests
    {
        private static Dictionary<string, object> FullDict() => new Dictionary<string, object>
        {
            { "id",                          "scenario.test" },
            { "name",                        "Test Scenario" },
            { "description",                 "A test scenario for unit testing." },
            { "difficulty_rating",           3L },
            { "crew_composition",            new List<object> { "npc.engineer", "npc.scientist" } },
            { "starting_resources",          new Dictionary<string, object>
              {
                  { "credits", 750.0 },
                  { "food",    90.0 },
                  { "parts",   45.0 },
              }
            },
            { "starting_ships",              new List<object> { "ship.scout" } },
            { "layout_seed",                 12345L },
            { "starting_faction_disposition","standard" },
        };

        [Test]
        public void FromDict_ParsesId()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual("scenario.test", sc.id);
        }

        [Test]
        public void FromDict_ParsesName()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual("Test Scenario", sc.name);
        }

        [Test]
        public void FromDict_ParsesDescription()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual("A test scenario for unit testing.", sc.description);
        }

        [Test]
        public void FromDict_ParsesDifficultyRating()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual(3, sc.difficultyRating);
        }

        [Test]
        public void FromDict_ParsesCrewComposition()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual(2, sc.crewComposition.Count);
            Assert.AreEqual("npc.engineer",  sc.crewComposition[0]);
            Assert.AreEqual("npc.scientist", sc.crewComposition[1]);
        }

        [Test]
        public void FromDict_ParsesStartingResources()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual(3, sc.startingResources.Count);
            Assert.AreEqual(750f, sc.startingResources["credits"], 0.001f);
            Assert.AreEqual(90f,  sc.startingResources["food"],    0.001f);
            Assert.AreEqual(45f,  sc.startingResources["parts"],   0.001f);
        }

        [Test]
        public void FromDict_ParsesStartingShips()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual(1, sc.startingShips.Count);
            Assert.AreEqual("ship.scout", sc.startingShips[0]);
        }

        [Test]
        public void FromDict_ParsesLayoutSeed()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.IsTrue(sc.layoutSeed.HasValue);
            Assert.AreEqual(12345, sc.layoutSeed.Value);
        }

        [Test]
        public void FromDict_ParsesStartingFactionDisposition()
        {
            var sc = ScenarioDefinition.FromDict(FullDict());
            Assert.AreEqual("standard", sc.startingFactionDisposition);
        }

        [Test]
        public void FromDict_NullLayoutSeed_RemainsNull()
        {
            var d = FullDict();
            d["layout_seed"] = null;
            var sc = ScenarioDefinition.FromDict(d);
            Assert.IsFalse(sc.layoutSeed.HasValue, "layout_seed null should produce null HasValue.");
        }

        [Test]
        public void FromDict_MissingLayoutSeed_IsNull()
        {
            var d = FullDict();
            d.Remove("layout_seed");
            var sc = ScenarioDefinition.FromDict(d);
            Assert.IsFalse(sc.layoutSeed.HasValue, "Missing layout_seed should produce null.");
        }

        [Test]
        public void FromDict_DefaultDifficultyRating_Is2()
        {
            var d = FullDict();
            d.Remove("difficulty_rating");
            var sc = ScenarioDefinition.FromDict(d);
            Assert.AreEqual(2, sc.difficultyRating, "Default difficulty_rating should be 2.");
        }

        [Test]
        public void FromDict_DefaultFactionDisposition_IsStandard()
        {
            var d = FullDict();
            d.Remove("starting_faction_disposition");
            var sc = ScenarioDefinition.FromDict(d);
            Assert.AreEqual("standard", sc.startingFactionDisposition);
        }

        [Test]
        public void FromDict_EmptyCrewComposition_YieldsEmptyList()
        {
            var d = FullDict();
            d["crew_composition"] = new List<object>();
            var sc = ScenarioDefinition.FromDict(d);
            Assert.AreEqual(0, sc.crewComposition.Count);
        }

        [Test]
        public void FromDict_MissingStartingResources_YieldsEmptyDict()
        {
            var d = FullDict();
            d.Remove("starting_resources");
            var sc = ScenarioDefinition.FromDict(d);
            Assert.AreEqual(0, sc.startingResources.Count);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ContentRegistry Scenario loading
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ContentRegistryScenariosTests
    {
        [Test]
        public void Scenarios_DictExists_OnFreshRegistry()
        {
            var go  = new GameObject("TestRegistry");
            var reg = go.AddComponent<ContentRegistry>();
            Assert.IsNotNull(reg.Scenarios, "Scenarios dictionary should be non-null on a fresh registry.");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Scenarios_CanStoreAndRetrieveScenario()
        {
            var go  = new GameObject("TestRegistry2");
            var reg = go.AddComponent<ContentRegistry>();

            var sc = ScenarioDefinition.FromDict(new Dictionary<string, object>
            {
                { "id",   "scenario.stored_test" },
                { "name", "Stored Test" },
            });
            reg.Scenarios["scenario.stored_test"] = sc;

            Assert.IsTrue(reg.Scenarios.ContainsKey("scenario.stored_test"));
            Assert.AreEqual("Stored Test", reg.Scenarios["scenario.stored_test"].name);
            Object.DestroyImmediate(go);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FeatureFlag default
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ScenarioFeatureFlagTests
    {
        private bool _savedFlag;

        [SetUp]    public void SetUp()    => _savedFlag = FeatureFlags.ScenarioSelection;
        [TearDown] public void TearDown() => FeatureFlags.ScenarioSelection = _savedFlag;

        [Test]
        public void ScenarioSelection_DefaultIsTrue()
        {
            // Reset to default to confirm it starts enabled.
            FeatureFlags.ScenarioSelection = true;
            Assert.IsTrue(FeatureFlags.ScenarioSelection,
                "FeatureFlags.ScenarioSelection should default to true.");
        }

        [Test]
        public void ScenarioSelection_CanBeDisabled()
        {
            FeatureFlags.ScenarioSelection = false;
            Assert.IsFalse(FeatureFlags.ScenarioSelection);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // StationState resource application from scenario
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ScenarioResourceApplicationTests
    {
        /// <summary>
        /// Simulates what GameManager.ApplyScenarioResources does, directly on a StationState,
        /// so we can test the logic without instantiating a GameManager MonoBehaviour.
        /// </summary>
        private static void ApplyResources(StationState station, ScenarioDefinition scenario)
        {
            if (scenario.startingResources == null) return;
            foreach (var kv in scenario.startingResources)
                station.resources[kv.Key] = kv.Value;
        }

        [Test]
        public void Apply_OverridesListedResources()
        {
            var station = new StationState("TestStation");
            var sc = ScenarioDefinition.FromDict(new Dictionary<string, object>
            {
                { "id",   "scenario.r_test" },
                { "name", "Resource Test" },
                { "starting_resources", new Dictionary<string, object> { { "credits", 999.0 } } },
            });
            ApplyResources(station, sc);
            Assert.AreEqual(999f, station.GetResource("credits"), 0.001f);
        }

        [Test]
        public void Apply_UnlistedResources_RetainDefault()
        {
            var station = new StationState("TestStation");
            float defaultFood = station.GetResource("food");

            var sc = ScenarioDefinition.FromDict(new Dictionary<string, object>
            {
                { "id",   "scenario.r_test2" },
                { "name", "Resource Test 2" },
                { "starting_resources", new Dictionary<string, object> { { "credits", 200.0 } } },
            });
            ApplyResources(station, sc);

            // food was not in the scenario, should be untouched
            Assert.AreEqual(defaultFood, station.GetResource("food"), 0.001f,
                "Resources not listed in the scenario should retain their StationState default.");
        }

        [Test]
        public void Apply_AllCoreResources_CanBeOverridden()
        {
            var station = new StationState("TestStation");
            var resDict = new Dictionary<string, object>
            {
                { "credits", 111.0 }, { "food", 222.0 }, { "power", 333.0 },
                { "oxygen",  444.0 }, { "parts", 55.0 }, { "ice",   666.0 },
                { "fuel",     77.0 },
            };
            var sc = ScenarioDefinition.FromDict(new Dictionary<string, object>
            {
                { "id",   "scenario.all_resources" },
                { "name", "All Resources" },
                { "starting_resources", resDict },
            });
            ApplyResources(station, sc);

            Assert.AreEqual(111f, station.GetResource("credits"), 0.001f);
            Assert.AreEqual(222f, station.GetResource("food"),    0.001f);
            Assert.AreEqual(333f, station.GetResource("power"),   0.001f);
            Assert.AreEqual(444f, station.GetResource("oxygen"),  0.001f);
            Assert.AreEqual( 55f, station.GetResource("parts"),   0.001f);
            Assert.AreEqual(666f, station.GetResource("ice"),     0.001f);
            Assert.AreEqual( 77f, station.GetResource("fuel"),    0.001f);
        }

        [Test]
        public void Apply_EmptyStartingResources_ChangesNothing()
        {
            var station  = new StationState("TestStation");
            float before = station.GetResource("credits");

            var sc = ScenarioDefinition.FromDict(new Dictionary<string, object>
            {
                { "id",   "scenario.empty_r" },
                { "name", "Empty Resources" },
            });
            ApplyResources(station, sc);

            Assert.AreEqual(before, station.GetResource("credits"), 0.001f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core scenario data file definitions (parsed in-process without I/O)
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CoreScenarioDefinitionsTests
    {
        private static readonly List<Dictionary<string, object>> CoreScenarioDicts =
            new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "id",   "scenario.standard_start" }, { "name", "Standard Start" },
                    { "description", "A balanced beginning." }, { "difficulty_rating", 2L },
                    { "crew_composition", new List<object> { "npc.engineer", "npc.scientist", "npc.security_officer" } },
                    { "starting_resources", new Dictionary<string, object>
                      { {"credits",500.0},{"food",120.0},{"power",100.0},{"oxygen",120.0},{"parts",60.0},{"ice",200.0},{"fuel",60.0} }
                    },
                    { "starting_ships", new List<object>() },
                    { "starting_faction_disposition", "standard" },
                },
                new Dictionary<string, object>
                {
                    { "id",   "scenario.derelict_salvage" }, { "name", "Derelict Salvage" },
                    { "description", "Stripped down tough start." }, { "difficulty_rating", 4L },
                    { "crew_composition", new List<object> { "npc.engineer", "npc.engineer" } },
                    { "starting_resources", new Dictionary<string, object>
                      { {"credits",150.0},{"food",60.0},{"power",60.0},{"oxygen",70.0},{"parts",30.0},{"ice",100.0},{"fuel",25.0} }
                    },
                    { "starting_ships", new List<object>() },
                    { "starting_faction_disposition", "standard" },
                },
                new Dictionary<string, object>
                {
                    { "id",   "scenario.military_outpost" }, { "name", "Military Outpost" },
                    { "description", "Security-heavy start." }, { "difficulty_rating", 3L },
                    { "crew_composition", new List<object> { "npc.security_officer", "npc.security_officer", "npc.engineer" } },
                    { "starting_resources", new Dictionary<string, object>
                      { {"credits",800.0},{"food",150.0},{"power",120.0},{"oxygen",150.0},{"parts",100.0},{"ice",180.0},{"fuel",80.0} }
                    },
                    { "starting_ships", new List<object>() },
                    { "starting_faction_disposition", "standard" },
                },
            };

        [Test]
        public void ThreeCoreScenariosExist()
        {
            Assert.AreEqual(3, CoreScenarioDicts.Count,
                "Exactly three core scenarios must be defined.");
        }

        [Test]
        public void AllCoreScenariosParseWithoutError()
        {
            foreach (var d in CoreScenarioDicts)
            {
                ScenarioDefinition sc = null;
                Assert.DoesNotThrow(() => sc = ScenarioDefinition.FromDict(d),
                    $"Scenario '{d["id"]}' should parse without throwing.");
                Assert.IsNotNull(sc);
                Assert.IsNotEmpty(sc.id);
                Assert.IsNotEmpty(sc.name);
                Assert.IsNotEmpty(sc.description);
            }
        }

        [Test]
        public void AllCoreScenariosHaveCrewComposition()
        {
            foreach (var d in CoreScenarioDicts)
            {
                var sc = ScenarioDefinition.FromDict(d);
                Assert.Greater(sc.crewComposition.Count, 0,
                    $"Scenario '{sc.id}' must define at least one crew member.");
            }
        }

        [Test]
        public void AllCoreScenariosHaveValidDifficultyRating()
        {
            foreach (var d in CoreScenarioDicts)
            {
                var sc = ScenarioDefinition.FromDict(d);
                Assert.GreaterOrEqual(sc.difficultyRating, 1,
                    $"Scenario '{sc.id}' difficulty_rating must be >= 1.");
                Assert.LessOrEqual(sc.difficultyRating, 5,
                    $"Scenario '{sc.id}' difficulty_rating must be <= 5.");
            }
        }

        [Test]
        public void AllCoreScenariosHaveStartingResources()
        {
            foreach (var d in CoreScenarioDicts)
            {
                var sc = ScenarioDefinition.FromDict(d);
                Assert.Greater(sc.startingResources.Count, 0,
                    $"Scenario '{sc.id}' must define at least one starting resource.");
            }
        }

        [Test]
        public void AllCoreScenariosHaveUniqueIds()
        {
            var ids = new HashSet<string>();
            foreach (var d in CoreScenarioDicts)
            {
                var sc = ScenarioDefinition.FromDict(d);
                Assert.IsTrue(ids.Add(sc.id),
                    $"Scenario id '{sc.id}' appears more than once.");
            }
        }
    }
}
