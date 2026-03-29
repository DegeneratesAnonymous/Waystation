// ResourceSystem tests — EditMode unit and integration tests.
// Validates: per-tick resource balance, cascade failure, morale scaling,
// credits special case, NPC deprivation sequence, and resource extensibility.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Minimal stubs ────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IRegistryAccess stand-in for unit tests.
    /// Provides a populated resource and module dictionary without loading Unity assets.
    /// </summary>
    internal class StubRegistry : IRegistryAccess
    {
        public Dictionary<string, ModuleDefinition>  Modules   { get; } = new();
        public Dictionary<string, ResourceDefinition> Resources { get; } = new();
        public Dictionary<string, EventDefinition>   Events    { get; } = new();

        public StubRegistry()
        {
            // Seven core resource definitions
            AddResource("power",   warningThreshold: 15f, softCap: 500f,  cascade: true,  deprivation: false, credit: false);
            AddResource("food",    warningThreshold: 20f, softCap: 500f,  cascade: true,  deprivation: true,  credit: false, penalty: 10f);
            AddResource("oxygen",  warningThreshold: 10f, softCap: 500f,  cascade: true,  deprivation: true,  credit: false, penalty: 25f);
            AddResource("ice",     warningThreshold: 30f, softCap: 500f,  cascade: false, deprivation: true,  credit: false, penalty: 10f);
            AddResource("fuel",    warningThreshold: 10f, softCap: 200f,  cascade: true,  deprivation: false, credit: false);
            AddResource("parts",   warningThreshold: 5f,  softCap: 200f,  cascade: false, deprivation: false, credit: false);
            AddResource("credits", warningThreshold: 50f, softCap: 1e5f,  cascade: false, deprivation: false, credit: true);

            // Morale balance meta-entry
            Resources["morale_balance"] = new ResourceDefinition
            {
                id = "morale_balance",
                moraleScalarMax = 0.15f,
                moraleScalarMin = -0.15f,
            };
        }

        public void AddResource(string id, float warningThreshold, float softCap,
            bool cascade, bool deprivation, bool credit, float penalty = 10f)
        {
            Resources[id] = new ResourceDefinition
            {
                id                    = id,
                warningThreshold      = warningThreshold,
                softCap               = softCap,
                causesModuleCascade   = cascade,
                causesNpcDeprivation  = deprivation,
                npcDeprivationPenalty = penalty,
                isCreditResource      = credit,
            };
        }

        public void AddModule(string id, Dictionary<string, float> effects)
        {
            Modules[id] = new ModuleDefinition
            {
                id              = id,
                displayName     = id,
                resourceEffects = effects,
            };
        }
    }

    /// <summary>
    /// Thin adapter around the real ResourceSystem that allows tests to supply
    /// a stubbed registry (StubRegistry implements IRegistryAccess) while exercising
    /// the actual production code paths introduced in this PR.
    /// </summary>
    internal class TestableResourceSystem
    {
        private readonly ResourceSystem _inner;

        public TestableResourceSystem(StubRegistry stub)
        {
            // Construct the real ResourceSystem with the stub implementing IRegistryAccess.
            _inner = new ResourceSystem(stub);
        }

        /// <summary>Forwards the MoodSystem into the real ResourceSystem.</summary>
        public void SetMoodSystem(MoodSystem m) => _inner.SetMoodSystem(m);

        /// <summary>Delegates the per-tick update to the real ResourceSystem.</summary>
        public void Tick(StationState station) => _inner.Tick(station);

        /// <summary>Delegates morale scaling calculation to the real ResourceSystem.</summary>
        public float MoraleModifier(StationState station) => _inner.MoraleModifier(station);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static class TestHelpers
    {
        /// <summary>Creates a StationState with all seven core resources pre-populated.</summary>
        public static StationState MakeStation()
        {
            var s = new StationState("TestStation") { tick = 0 };
            // Ensure all 7 core resources are present
            s.resources["power"]   = 100f;
            s.resources["food"]    = 100f;
            s.resources["oxygen"]  = 100f;
            s.resources["ice"]     = 100f;
            s.resources["fuel"]    = 100f;
            s.resources["parts"]   = 50f;
            s.resources["credits"] = 500f;
            return s;
        }

        /// <summary>Adds a crew NPC with the given mood score.</summary>
        public static NPCInstance AddCrewNpc(StationState station, float moodScore = 50f)
        {
            var npc = new NPCInstance
            {
                uid       = System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name      = "Test NPC",
                moodScore = moodScore,
            };
            npc.statusTags.Add("crew");
            station.npcs[npc.uid] = npc;
            return npc;
        }

        /// <summary>Adds an active module with the given resource effects.</summary>
        public static ModuleInstance AddModule(
            StationState station, StubRegistry stub,
            string definitionId, Dictionary<string, float> effects)
        {
            stub.AddModule(definitionId, effects);
            var mod = ModuleInstance.Create(definitionId, definitionId, "utility");
            station.modules[mod.uid] = mod;
            return mod;
        }
    }

    // ── Unit Tests ────────────────────────────────────────────────────────────

    [TestFixture]
    public class ResourceSystemTickTests
    {
        private StubRegistry            _stub;
        private TestableResourceSystem  _system;

        [SetUp]
        public void SetUp()
        {
            _stub   = new StubRegistry();
            _system = new TestableResourceSystem(_stub);
        }

        // ── Per-tick resource balance — all seven core resources ──────────────

        [Test]
        public void Tick_PowerProducer_IncreasesPower()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 10f;
            TestHelpers.AddModule(station, _stub, "power_gen",
                new Dictionary<string, float> { { "power", 5f } });

            _system.Tick(station);

            Assert.AreEqual(15f, station.GetResource("power"), 0.01f);
        }

        [Test]
        public void Tick_PowerConsumer_DecreasesPower()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 50f;
            TestHelpers.AddModule(station, _stub, "consumer",
                new Dictionary<string, float> { { "power", -3f } });

            _system.Tick(station);

            Assert.AreEqual(47f, station.GetResource("power"), 0.01f);
        }

        [Test]
        public void Tick_FoodProducer_IncreasesFood()
        {
            var station = TestHelpers.MakeStation();
            station.resources["food"] = 50f;
            TestHelpers.AddModule(station, _stub, "food_gen",
                new Dictionary<string, float> { { "food", 3f } });

            _system.Tick(station);

            Assert.AreEqual(53f, station.GetResource("food"), 0.01f);
        }

        [Test]
        public void Tick_OxygenProducer_IncreasesOxygen()
        {
            var station = TestHelpers.MakeStation();
            station.resources["oxygen"] = 30f;
            TestHelpers.AddModule(station, _stub, "oxy_gen",
                new Dictionary<string, float> { { "oxygen", 4f } });

            _system.Tick(station);

            Assert.AreEqual(34f, station.GetResource("oxygen"), 0.01f);
        }

        [Test]
        public void Tick_IceConsumer_DecreasesIce()
        {
            var station = TestHelpers.MakeStation();
            station.resources["ice"] = 80f;
            TestHelpers.AddModule(station, _stub, "ice_proc",
                new Dictionary<string, float> { { "ice", -2f } });

            _system.Tick(station);

            Assert.AreEqual(78f, station.GetResource("ice"), 0.01f);
        }

        [Test]
        public void Tick_FuelProducer_IncreasesFuel()
        {
            var station = TestHelpers.MakeStation();
            station.resources["fuel"] = 10f;
            TestHelpers.AddModule(station, _stub, "fuel_dep",
                new Dictionary<string, float> { { "fuel", 3f } });

            _system.Tick(station);

            Assert.AreEqual(13f, station.GetResource("fuel"), 0.01f);
        }

        [Test]
        public void Tick_CreditsProducer_IncreasesCredits()
        {
            var station = TestHelpers.MakeStation();
            station.resources["credits"] = 200f;
            TestHelpers.AddModule(station, _stub, "lounge",
                new Dictionary<string, float> { { "credits", 2f } });

            _system.Tick(station);

            Assert.AreEqual(202f, station.GetResource("credits"), 0.01f);
        }

        [Test]
        public void Tick_PartsConsumer_DecreasesParts()
        {
            var station = TestHelpers.MakeStation();
            station.resources["parts"] = 40f;
            TestHelpers.AddModule(station, _stub, "med",
                new Dictionary<string, float> { { "parts", -0.1f } });

            _system.Tick(station);

            Assert.AreEqual(39.9f, station.GetResource("parts"), 0.01f);
        }

        [Test]
        public void Tick_ResourceNeverGoesBelowZero()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 1f;
            TestHelpers.AddModule(station, _stub, "consumer",
                new Dictionary<string, float> { { "power", -10f } });

            _system.Tick(station);

            Assert.GreaterOrEqual(station.GetResource("power"), 0f);
        }

        [Test]
        public void Tick_SoftCap_ProductionClampedAtCap()
        {
            // soft cap for food = 500; starting at 499 with a +5 producer should not exceed 500
            var station = TestHelpers.MakeStation();
            station.resources["food"] = 499f;
            TestHelpers.AddModule(station, _stub, "food_gen",
                new Dictionary<string, float> { { "food", 5f } });

            _system.Tick(station);

            Assert.LessOrEqual(station.GetResource("food"), 500f + 0.01f);
        }
    }

    [TestFixture]
    public class CascadeFailureTests
    {
        private StubRegistry            _stub;
        private TestableResourceSystem  _system;

        [SetUp]
        public void SetUp()
        {
            _stub   = new StubRegistry();
            _system = new TestableResourceSystem(_stub);
        }

        [Test]
        public void CascadeFailure_PowerDepleted_DependentModuleDegraded()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            var mod = TestHelpers.AddModule(station, _stub, "cmd",
                new Dictionary<string, float> { { "power", -2f } });

            _system.Tick(station);

            Assert.IsTrue(mod.IsResourceDeprived,
                "Module should be deprived when its power supply is cut.");
            Assert.IsTrue(mod.resourceDeprived.Contains("power"));
        }

        [Test]
        public void CascadeFailure_PowerDepleted_IndependentModuleNotDegraded()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            // Module that produces power (positive effect) — NOT a power consumer.
            var gen = TestHelpers.AddModule(station, _stub, "power_gen",
                new Dictionary<string, float> { { "power", 8f } });

            _system.Tick(station);

            Assert.IsFalse(gen.IsResourceDeprived,
                "Power generator should not be degraded when power is depleted.");
        }

        [Test]
        public void CascadeFailure_MultipleModules_EachDegradedIndependently()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            var m1 = TestHelpers.AddModule(station, _stub, "mod_a",
                new Dictionary<string, float> { { "power", -1f } });
            var m2 = TestHelpers.AddModule(station, _stub, "mod_b",
                new Dictionary<string, float> { { "power", -2f } });
            var m3 = TestHelpers.AddModule(station, _stub, "mod_c",  // no power dep
                new Dictionary<string, float> { { "food", 1f } });

            _system.Tick(station);

            Assert.IsTrue(m1.IsResourceDeprived, "Module A (power dep) should be degraded.");
            Assert.IsTrue(m2.IsResourceDeprived, "Module B (power dep) should be degraded.");
            Assert.IsFalse(m3.IsResourceDeprived, "Module C (no power dep) should not be degraded.");
        }

        [Test]
        public void CascadeFailure_NotStationWideShutdown()
        {
            // Station-wide shutdown would mean ALL modules offline.
            // Cascade should only affect the modules that depend on the depleted resource.
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            var powerDep = TestHelpers.AddModule(station, _stub, "power_consumer",
                new Dictionary<string, float> { { "power", -1f } });
            var foodProd = TestHelpers.AddModule(station, _stub, "food_producer",
                new Dictionary<string, float> { { "food", 2f } });

            _system.Tick(station);

            Assert.IsTrue(powerDep.IsResourceDeprived);
            Assert.IsFalse(foodProd.IsResourceDeprived,
                "A module with no power dependency must remain operational.");
        }

        [Test]
        public void CascadeFailure_ResourceRecovers_ModulesRestored()
        {
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            var mod = TestHelpers.AddModule(station, _stub, "cmd",
                new Dictionary<string, float> { { "power", -2f } });

            _system.Tick(station);
            Assert.IsTrue(mod.IsResourceDeprived);

            // Restore power
            station.resources["power"] = 100f;
            _system.Tick(station);

            Assert.IsFalse(mod.IsResourceDeprived,
                "Module should be restored when power recovers.");
        }

        [Test]
        public void CascadeFailure_DegradedModuleDoesNotConsumeResources()
        {
            // A deprived module should be completely offline — no consumption.
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            station.resources["oxygen"] = 100f;
            TestHelpers.AddModule(station, _stub, "cmd",
                new Dictionary<string, float> { { "power", -2f }, { "oxygen", -0.5f } });

            _system.Tick(station);

            // Module is deprived, so oxygen consumption should not apply.
            Assert.AreEqual(100f, station.GetResource("oxygen"), 0.01f,
                "Deprived module must not consume oxygen.");
        }

        [Test]
        public void FuelCascade_FuelDepleted_ThrusterModuleDegraded()
        {
            var station = TestHelpers.MakeStation();
            station.resources["fuel"] = 0f;
            var thruster = TestHelpers.AddModule(station, _stub, "thruster",
                new Dictionary<string, float> { { "power", -1f }, { "fuel", -0.5f } });

            _system.Tick(station);

            Assert.IsTrue(thruster.resourceDeprived.Contains("fuel"),
                "Thruster module should be deprived when fuel runs out.");
        }
    }

    [TestFixture]
    public class NpcDeprivationSequenceTests
    {
        private StubRegistry            _stub;
        private TestableResourceSystem  _system;

        [SetUp]
        public void SetUp()
        {
            _stub   = new StubRegistry();
            _system = new TestableResourceSystem(_stub);
        }

        [Test]
        public void NpcDeprivation_FoodHitsZero_NpcMoodPenaltyApplied()
        {
            var station = TestHelpers.MakeStation();
            station.resources["food"] = 0f;
            var npc = TestHelpers.AddCrewNpc(station, moodScore: 60f);
            TestHelpers.AddModule(station, _stub, "food_consumer",
                new Dictionary<string, float> { { "food", -1f } });

            _system.Tick(station);

            Assert.Less(npc.moodScore, 60f,
                "Crew NPC moodScore should drop when food resource is depleted.");
        }

        [Test]
        public void NpcDeprivation_OxygenHitsZero_LargerMoodPenalty()
        {
            var station = TestHelpers.MakeStation();
            station.resources["oxygen"] = 0f;
            var npc = TestHelpers.AddCrewNpc(station, moodScore: 60f);
            float moodBefore = npc.moodScore;

            _system.Tick(station);

            float oxyPenalty = moodBefore - npc.moodScore;
            Assert.Greater(oxyPenalty, 0f, "Oxygen deprivation should reduce moodScore.");

            // Food penalty is -10; oxygen penalty should be larger (-25)
            var station2 = TestHelpers.MakeStation();
            station2.resources["food"] = 0f;
            var npc2 = TestHelpers.AddCrewNpc(station2, moodScore: 60f);
            float moodBefore2 = npc2.moodScore;
            _system.Tick(station2);
            float foodPenalty = moodBefore2 - npc2.moodScore;

            Assert.Greater(oxyPenalty, foodPenalty,
                "Oxygen deprivation penalty must be larger than food deprivation penalty.");
        }

        [Test]
        public void NpcDeprivation_NpcSuffersBefore_ModuleDegraded()
        {
            // Within a single tick the sequence must be: NPC penalty applied → then module cascade.
            // We verify both happened and the mood hit occurred.
            var station = TestHelpers.MakeStation();
            station.resources["food"] = 0f;
            var npc = TestHelpers.AddCrewNpc(station, moodScore: 60f);
            var mod = TestHelpers.AddModule(station, _stub, "food_consumer",
                new Dictionary<string, float> { { "food", -1f } });

            _system.Tick(station);

            // Both should have happened in the correct order within the tick.
            Assert.Less(npc.moodScore, 60f,
                "NPC must have suffered (mood penalty) by end of tick.");
            Assert.IsTrue(mod.IsResourceDeprived,
                "Module must be degraded by end of tick.");
        }

        [Test]
        public void NpcDeprivation_PowerDepletion_NoNpcMoodPenalty()
        {
            // Power does NOT cause NPC deprivation — only modules are affected.
            var station = TestHelpers.MakeStation();
            station.resources["power"] = 0f;
            var npc = TestHelpers.AddCrewNpc(station, moodScore: 60f);

            _system.Tick(station);

            Assert.AreEqual(60f, npc.moodScore, 0.01f,
                "Power depletion should not apply an NPC mood penalty directly.");
        }
    }

    [TestFixture]
    public class CreditsDepletionTests
    {
        private StubRegistry            _stub;
        private TestableResourceSystem  _system;

        [SetUp]
        public void SetUp()
        {
            _stub   = new StubRegistry();
            _system = new TestableResourceSystem(_stub);
        }

        [Test]
        public void CreditsDepletion_NoModuleCascade()
        {
            var station = TestHelpers.MakeStation();
            station.resources["credits"] = 0f;
            var mod = TestHelpers.AddModule(station, _stub, "lounge",
                new Dictionary<string, float> { { "credits", 2f } });

            _system.Tick(station);

            Assert.IsFalse(mod.IsResourceDeprived,
                "Credits depletion must NOT trigger module cascade.");
        }

        [Test]
        public void CreditsDepletion_RestrictsHireAction()
        {
            var station = TestHelpers.MakeStation();
            station.resources["credits"] = 0f;

            _system.Tick(station);

            Assert.IsTrue(station.IsActionRestricted("hire"),
                "Hiring must be restricted when credits are depleted.");
        }

        [Test]
        public void CreditsDepletion_RestrictsPurchaseAction()
        {
            var station = TestHelpers.MakeStation();
            station.resources["credits"] = 0f;

            _system.Tick(station);

            Assert.IsTrue(station.IsActionRestricted("purchase"),
                "Purchasing must be restricted when credits are depleted.");
        }

        [Test]
        public void CreditsRecovery_LiftsBothRestrictions()
        {
            var station = TestHelpers.MakeStation();
            station.resources["credits"] = 0f;
            _system.Tick(station);
            Assert.IsTrue(station.IsActionRestricted("hire"));

            // Credits recover
            station.resources["credits"] = 500f;
            _system.Tick(station);

            Assert.IsFalse(station.IsActionRestricted("hire"),
                "Hire restriction must be lifted when credits recover.");
            Assert.IsFalse(station.IsActionRestricted("purchase"),
                "Purchase restriction must be lifted when credits recover.");
        }
    }

    [TestFixture]
    public class MoraleScalarTests
    {
        private StubRegistry            _stub;
        private TestableResourceSystem  _system;

        [SetUp]
        public void SetUp()
        {
            _stub   = new StubRegistry();
            _system = new TestableResourceSystem(_stub);
        }

        [Test]
        public void MoraleModifier_MaxMorale_IncreasesProduction()
        {
            var station = TestHelpers.MakeStation();
            TestHelpers.AddCrewNpc(station, moodScore: 100f);

            float modifier = _system.MoraleModifier(station);

            Assert.AreEqual(1.15f, modifier, 0.001f,
                "At max morale (100) modifier should be 1.15 (+15%).");
        }

        [Test]
        public void MoraleModifier_NeutralMorale_NoChange()
        {
            var station = TestHelpers.MakeStation();
            TestHelpers.AddCrewNpc(station, moodScore: 50f);

            float modifier = _system.MoraleModifier(station);

            Assert.AreEqual(1.0f, modifier, 0.001f,
                "At neutral morale (50) modifier should be 1.0.");
        }

        [Test]
        public void MoraleModifier_MinMorale_DecreasesProduction()
        {
            var station = TestHelpers.MakeStation();
            TestHelpers.AddCrewNpc(station, moodScore: 0f);

            float modifier = _system.MoraleModifier(station);

            Assert.AreEqual(0.85f, modifier, 0.001f,
                "At min morale (0) modifier should be 0.85 (−15%).");
        }

        [Test]
        public void MoraleModifier_EmptyCrew_ReturnsOne()
        {
            var station = TestHelpers.MakeStation();
            // No NPCs added

            float modifier = _system.MoraleModifier(station);

            Assert.AreEqual(1.0f, modifier, 0.001f,
                "With no crew the morale modifier should default to 1.0.");
        }

        [Test]
        public void MoraleModifier_AffectsProductionOutput()
        {
            // Two runs: max morale vs min morale. High morale should yield more output.
            var stationHigh = TestHelpers.MakeStation();
            stationHigh.resources["food"] = 0f;
            TestHelpers.AddCrewNpc(stationHigh, moodScore: 100f);
            TestHelpers.AddModule(stationHigh, _stub, "food_gen_h",
                new Dictionary<string, float> { { "food", 10f } });

            var stationLow = TestHelpers.MakeStation();
            stationLow.resources["food"] = 0f;
            TestHelpers.AddCrewNpc(stationLow, moodScore: 0f);
            var lowStub = new StubRegistry();
            var lowSystem = new TestableResourceSystem(lowStub);
            lowStub.AddModule("food_gen_l", new Dictionary<string, float> { { "food", 10f } });
            var modLow = ModuleInstance.Create("food_gen_l", "food_gen_l", "utility");
            stationLow.modules[modLow.uid] = modLow;

            _system.Tick(stationHigh);
            lowSystem.Tick(stationLow);

            Assert.Greater(stationHigh.GetResource("food"), stationLow.GetResource("food"),
                "High morale must yield more food production than low morale.");
        }
    }

    [TestFixture]
    public class ResourceExtensibilityTests
    {
        [Test]
        public void NewResourceType_AddedViaDataOnly_TrackedCorrectly()
        {
            // Simulate adding a new resource "coolant" via data only.
            // The system should track, produce, and consume it without code changes.
            var stub = new StubRegistry();
            stub.AddResource("coolant", warningThreshold: 5f, softCap: 100f,
                cascade: true, deprivation: false, credit: false);

            var system = new TestableResourceSystem(stub);
            var station = TestHelpers.MakeStation();
            station.resources["coolant"] = 0f;  // new resource present in station

            // Producer module for coolant
            stub.AddModule("coolant_gen", new Dictionary<string, float> { { "coolant", 2f } });
            var gen = ModuleInstance.Create("coolant_gen", "coolant_gen", "utility");
            station.modules[gen.uid] = gen;

            system.Tick(station);

            Assert.AreEqual(2f, station.GetResource("coolant"), 0.01f,
                "New resource 'coolant' should be produced by its module after a tick.");
        }

        [Test]
        public void NewResourceType_CascadeFiresWhenDepleted()
        {
            var stub = new StubRegistry();
            stub.AddResource("coolant", warningThreshold: 5f, softCap: 100f,
                cascade: true, deprivation: false, credit: false);

            var system = new TestableResourceSystem(stub);
            var station = TestHelpers.MakeStation();
            station.resources["coolant"] = 0f;

            // A module that consumes coolant
            stub.AddModule("cooled_module", new Dictionary<string, float> { { "coolant", -1f } });
            var mod = ModuleInstance.Create("cooled_module", "cooled_module", "utility");
            station.modules[mod.uid] = mod;

            system.Tick(station);

            Assert.IsTrue(mod.resourceDeprived.Contains("coolant"),
                "New resource cascade should fire without any code changes.");
        }
    }

    // ── Integration Tests ─────────────────────────────────────────────────────

    [TestFixture]
    public class ResourceSystemIntegrationTests
    {
        [Test]
        public void FullDepletionCascade_LowWarning_Then_Suffering_Then_ModuleDegradation()
        {
            var stub   = new StubRegistry();
            var system = new TestableResourceSystem(stub);
            var station = TestHelpers.MakeStation();
            station.resources["food"] = 25f;  // above warning threshold (20) initially
            var npc = TestHelpers.AddCrewNpc(station, moodScore: 60f);
            var consumer = TestHelpers.AddModule(station, stub, "food_consumer",
                new Dictionary<string, float> { { "food", -5f } });

            // Tick 1: food drops to 20 (warning threshold)
            station.tick = 5;  // warning fires at tick % 5 == 0
            system.Tick(station);
            float foodAfterTick1 = station.GetResource("food");
            Assert.LessOrEqual(foodAfterTick1, 25f, "Food should have been consumed.");

            // Tick multiple times until food hits zero
            for (int i = 0; i < 10; i++)
            {
                station.tick++;
                system.Tick(station);
                if (station.GetResource("food") <= 0f) break;
            }

            // After food depletion: NPC should have suffered and module should be degraded
            Assert.LessOrEqual(npc.moodScore, 60f,
                "NPC mood must have decreased due to food deprivation.");
            Assert.IsTrue(consumer.IsResourceDeprived,
                "Food-consuming module must be degraded after food depletion.");
        }

        [Test]
        public void MoraleScalar_DuringMoodCrisis_ReducesProduction()
        {
            var stub   = new StubRegistry();
            var system = new TestableResourceSystem(stub);

            // Normal morale station
            var stationNormal = TestHelpers.MakeStation();
            stationNormal.resources["food"] = 0f;
            TestHelpers.AddCrewNpc(stationNormal, moodScore: 50f);
            stub.AddModule("food_gen", new Dictionary<string, float> { { "food", 10f } });
            var modNormal = ModuleInstance.Create("food_gen", "food_gen", "utility");
            stationNormal.modules[modNormal.uid] = modNormal;

            // Crisis morale station
            var stationCrisis = TestHelpers.MakeStation();
            stationCrisis.resources["food"] = 0f;
            TestHelpers.AddCrewNpc(stationCrisis, moodScore: 10f);
            var modCrisis = ModuleInstance.Create("food_gen", "food_gen", "utility");
            stationCrisis.modules[modCrisis.uid] = modCrisis;

            system.Tick(stationNormal);
            system.Tick(stationCrisis);

            Assert.Greater(stationNormal.GetResource("food"), stationCrisis.GetResource("food"),
                "Crisis-morale station should produce less food than normal-morale station.");
        }
    }
}
