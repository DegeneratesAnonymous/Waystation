using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class FarmingSystemNegativeEventsTests
    {
        private ContentRegistry _registry;
        private FarmingSystem _farming;
        private StationState _station;
        private bool _originalNegativeEventsFlag;

        [SetUp]
        public void SetUp()
        {
            _originalNegativeEventsFlag = FeatureFlags.FarmingNegativeEvents;
            FeatureFlags.FarmingNegativeEvents = true;

            var go = new GameObject("FarmingNegativeEventsRegistry");
            _registry = go.AddComponent<ContentRegistry>();
            _registry.Buildables["buildable.hydroponics_planter"] = new BuildableDefinition
            {
                id = "buildable.hydroponics_planter",
                displayName = "Planter"
            };
            _registry.Buildables["buildable.pipe"] = new BuildableDefinition
            {
                id = "buildable.pipe",
                displayName = "Pipe",
                networkType = "pipe",
                requiresPower = false
            };
            _registry.Crops["crop.test"] = new CropDataDefinition
            {
                id = "crop.test",
                cropName = "Test Crop",
                seedItemId = "seed.test",
                harvestItemId = "item.test_crop",
                harvestQtyMin = 4,
                harvestQtyMax = 4,
                growthTimePerStage = 10f,
                idealLightMin = 0f,
                idealLightMax = 1f,
                acceptableLightMin = 0f,
                acceptableLightMax = 1f,
                idealTempMin = -100f,
                idealTempMax = 200f,
                acceptableTempMin = -100f,
                acceptableTempMax = 200f,
                requiresWater = true,
                damagePerSecond = 0f
            };

            _farming = new FarmingSystem(_registry);
            _station = new StationState("FarmingNegTests");
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FarmingNegativeEvents = _originalNegativeEventsFlag;
            if (_registry != null)
                Object.DestroyImmediate(_registry.gameObject);
        }

        [Test]
        public void NegativeEventTrigger_GatesOnAccumulatorThreshold()
        {
            Assert.IsFalse(FarmingSystem.ShouldTriggerNegativeEvent(10, 20, 0.5f, 0.0f));
            Assert.IsTrue(FarmingSystem.ShouldTriggerNegativeEvent(20, 20, 0.5f, 0.1f));
            Assert.IsFalse(FarmingSystem.ShouldTriggerNegativeEvent(20, 20, 0.5f, 0.9f));
        }

        [Test]
        public void TemperatureModifier_IncreasesSpreadChance_WhenWarmer()
        {
            float cool = FarmingSystem.ComputeBlightSpreadChance(8f, throughFirebreak: false);
            float warm = FarmingSystem.ComputeBlightSpreadChance(30f, throughFirebreak: false);
            Assert.Greater(warm, cool, "Blight spread chance should increase in warmer temperatures.");
        }

        [Test]
        public void FirebreakSpreadChance_IsReducedComparedToAdjacent()
        {
            float adjacent = FarmingSystem.ComputeBlightSpreadChance(20f, throughFirebreak: false);
            float firebreak = FarmingSystem.ComputeBlightSpreadChance(20f, throughFirebreak: true);
            Assert.Less(firebreak, adjacent, "Firebreak spread chance should be lower than adjacent spread chance.");
        }

        [Test]
        public void DetectionDelay_DecreasesWithHigherBotanySkill()
        {
            int low = FarmingSystem.ComputeDetectionDelayFromBotany(2, FarmingSystem.BlightDetectionBaseDelayTicks);
            int mid = FarmingSystem.ComputeDetectionDelayFromBotany(10, FarmingSystem.BlightDetectionBaseDelayTicks);
            int high = FarmingSystem.ComputeDetectionDelayFromBotany(20, FarmingSystem.BlightDetectionBaseDelayTicks);

            Assert.Greater(low, mid);
            Assert.Greater(mid, high);
        }

        [Test]
        public void PestYieldMultiplier_DecreasesProgressivelyAndClampsAtZero()
        {
            Assert.AreEqual(1f, FarmingSystem.ComputePestYieldMultiplier(0), 0.0001f);
            Assert.Less(FarmingSystem.ComputePestYieldMultiplier(60), 1f);
            Assert.AreEqual(0f, FarmingSystem.ComputePestYieldMultiplier(FarmingSystem.PestYieldZeroTicks), 0.0001f);
            Assert.AreEqual(0f, FarmingSystem.ComputePestYieldMultiplier(FarmingSystem.PestYieldZeroTicks + 500), 0.0001f);
        }

        [Test]
        public void NeglectAccumulatesWithoutTending_AndResetsAfterTendTask()
        {
            var planter = FoundationInstance.Create("buildable.hydroponics_planter", 0, 0);
            planter.status = "complete";
            planter.cropId = "crop.test";
            planter.growthStage = 1;
            planter.lastTendedTick = 0;
            _station.foundations[planter.uid] = planter;

            var pipe = FoundationInstance.Create("buildable.pipe", 0, 0);
            pipe.status = "complete";
            _station.foundations[pipe.uid] = pipe;

            _station.tick = FarmingSystem.TendFrequencyTicks + 5;
            _farming.Tick(_station);

            Assert.Greater(planter.neglectAccumulator, 0,
                "Neglect accumulator should increase when planter is overdue for tending.");

            planter.neglectAccumulator = 10;
            planter.pestAccumulator = 7;
            _station.farmingTasks.Add(FarmingTaskInstance.Create("tend", planter.uid, null, 1));

            var npc = NPCInstance.Create("npc.farmer", "Farmer", "class.farming");
            npc.statusTags.Add("crew");
            npc.currentJobId = "job.farming";
            npc.jobTimer = 5;
            _station.npcs[npc.uid] = npc;

            _farming.Tick(_station); // claims task
            _farming.Tick(_station); // completes task

            Assert.AreEqual(0, planter.neglectAccumulator);
            Assert.AreEqual(0, planter.pestAccumulator);
        }

        [Test]
        public void BlightedPlanterProducesZeroYield()
        {
            var planter = FoundationInstance.Create("buildable.hydroponics_planter", 1, 1);
            planter.status = "complete";
            planter.cropId = "crop.test";
            planter.growthStage = 3;
            planter.hasBlight = true;
            _station.foundations[planter.uid] = planter;

            var storage = FoundationInstance.Create("buildable.storage_crate", 2, 1, cargoCapacity: 100);
            storage.status = "complete";
            _station.foundations[storage.uid] = storage;

            _station.farmingTasks.Add(FarmingTaskInstance.Create("harvest", planter.uid, null, 1));
            var npc = NPCInstance.Create("npc.farmer", "Farmer", "class.farming");
            npc.statusTags.Add("crew");
            npc.currentJobId = "job.farming";
            npc.jobTimer = 5;
            _station.npcs[npc.uid] = npc;

            _farming.Tick(_station); // claim
            _farming.Tick(_station); // complete

            int harvested = storage.cargo.TryGetValue("item.test_crop", out var qty) ? qty : 0;
            Assert.AreEqual(0, harvested, "Blighted planter should produce zero harvest yield.");
        }

        [Test]
        public void PestThresholdDestroysCropWhenUntreatedLongEnough()
        {
            var planter = FoundationInstance.Create("buildable.hydroponics_planter", 0, 1);
            planter.status = "complete";
            planter.cropId = "crop.test";
            planter.growthStage = 2;
            planter.hasPests = true;
            planter.pestTicks = FarmingSystem.PestDestroyThresholdTicks - 1;
            _station.foundations[planter.uid] = planter;

            var pipe = FoundationInstance.Create("buildable.pipe", 0, 1);
            pipe.status = "complete";
            _station.foundations[pipe.uid] = pipe;

            _farming.Tick(_station);

            Assert.AreEqual(0, planter.growthStage, "Planter crop should be destroyed at pest destruction threshold.");
            Assert.IsFalse(planter.hasPests, "Pest state should clear after destruction reset.");
        }

        [Test]
        public void NeverTendedPlanter_HasInitialGracePeriodBeforeNeglectAccumulates()
        {
            var planter = FoundationInstance.Create("buildable.hydroponics_planter", 5, 5);
            planter.status = "complete";
            planter.cropId = "crop.test";
            planter.growthStage = 1;
            planter.lastTendedTick = -1;
            _station.foundations[planter.uid] = planter;

            var pipe = FoundationInstance.Create("buildable.pipe", 5, 5);
            pipe.status = "complete";
            _station.foundations[pipe.uid] = pipe;

            _station.tick = FarmingSystem.TendFrequencyTicks - 1;
            _farming.Tick(_station);
            Assert.AreEqual(0, planter.neglectAccumulator,
                "Never-tended planters should not accumulate neglect before the initial grace period expires.");

            _station.tick = FarmingSystem.TendFrequencyTicks;
            _farming.Tick(_station);
            Assert.AreEqual(1, planter.neglectAccumulator,
                "Neglect should begin accumulating once the initial grace period expires.");
        }

        [Test]
        public void DetectedTreatmentNeed_DoesNotAlsoGenerateTendTaskSameTick()
        {
            var planter = FoundationInstance.Create("buildable.hydroponics_planter", 6, 6);
            planter.status = "complete";
            planter.cropId = "crop.test";
            planter.growthStage = 1;
            planter.hasBlight = true;
            planter.blightDetected = true;
            planter.lastTendedTick = 0;
            _station.foundations[planter.uid] = planter;

            var pipe = FoundationInstance.Create("buildable.pipe", 6, 6);
            pipe.status = "complete";
            _station.foundations[pipe.uid] = pipe;

            _station.tick = FarmingSystem.TendFrequencyTicks + 5;
            _farming.Tick(_station);

            int tendCount = 0;
            int treatCount = 0;
            foreach (var task in _station.farmingTasks)
            {
                if (task.planterUid != planter.uid) continue;
                if (task.taskType == "tend") tendCount++;
                if (task.taskType == "treat_blight") treatCount++;
            }

            Assert.AreEqual(1, treatCount, "A detected blight should generate exactly one treatment task.");
            Assert.AreEqual(0, tendCount, "Tend task should be suppressed when treatment is required.");
        }
    }
}
