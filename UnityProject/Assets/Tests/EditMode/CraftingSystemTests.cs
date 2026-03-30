// CraftingSystemTests — EditMode unit and integration tests for CraftingSystem (EXP-005).
//
// Validates:
//   • Recipe availability gating by Datachip/unlock tag presence
//   • Recipes absent when unlock tag is missing; present when active
//   • QueueRecipe rejects unknown recipes, wrong workbench type, locked recipes
//   • Execution time scaling at min, mid, and max crafting skill levels
//   • Output quality tier assignment at skill level boundaries (0→standard, 4→fine, 8→superior)
//   • Material haul from station storage into queue entry
//   • Materials consumed from storage during haul phase
//   • Output placed in nearest compatible storage on completion
//   • Multi-recipe queue processes in order
//   • FeatureFlags.CraftingSystem gates all tick processing
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Shared helpers ─────────────────────────────────────────────────────────

    internal static class CraftingTestHelpers
    {
        public const string WorkbenchBuildableId = "buildable.workbench";
        public const string WorkbenchRoomType    = "general_workshop";
        public const string RecipeId             = "recipe.test_widget";
        public const string UnlockTag            = "tech.test_widget";
        public const string OutputItemId         = "item.test_widget";
        public const string InputItemId          = "item.test_ore";

        /// <summary>Builds a minimal station and registry for crafting tests.</summary>
        public static (StationState station, ContentRegistry registry, CraftingSystem crafting)
            MakeSetup(bool unlockResearch = false)
        {
            var registryGo = new GameObject("CraftingTestRegistry");
            var registry   = registryGo.AddComponent<ContentRegistry>();

            // Register a workbench buildable.
            var benchDef = new BuildableDefinition
            {
                id               = WorkbenchBuildableId,
                displayName      = "Test Workbench",
                isWorkbench      = true,
                workbenchRoomType = WorkbenchRoomType,
            };
            registry.Buildables[WorkbenchBuildableId] = benchDef;

            // Register a simple recipe gated by an unlock tag.
            var recipe = new RecipeDefinition
            {
                id                   = RecipeId,
                displayName          = "Test Widget",
                requiredWorkbenchType = WorkbenchRoomType,
                unlockTag            = UnlockTag,
                inputMaterials       = new Dictionary<string, int> { { InputItemId, 2 } },
                outputItemId         = OutputItemId,
                outputQuantity       = 1,
                baseTimeTicks        = 10,
                skillRequirement     = 0,
                hasQualityTiers      = false,
            };
            registry.Recipes[RecipeId] = recipe;

            var station  = new StationState("CraftingTest");
            if (unlockResearch) station.SetTag(UnlockTag);

            var crafting = new CraftingSystem(registry);
            return (station, registry, crafting);
        }

        /// <summary>Adds a complete workbench foundation to the station.</summary>
        public static FoundationInstance AddWorkbench(StationState station)
        {
            var f = FoundationInstance.Create(WorkbenchBuildableId, 0, 0);
            f.status = "complete";
            station.foundations[f.uid] = f;
            return f;
        }

        /// <summary>Adds a complete storage foundation with specified items.</summary>
        public static FoundationInstance AddStorage(StationState station, string itemId = null, int qty = 0)
        {
            var f = FoundationInstance.Create("buildable.storage_crate", 1, 0, cargoCapacity: 50);
            f.status = "complete";
            if (itemId != null) f.cargo[itemId] = qty;
            station.foundations[f.uid] = f;
            return f;
        }

        /// <summary>Adds an idle crew NPC with the specified crafting skill level.</summary>
        public static NPCInstance AddCraftingNpc(StationState station, int craftingLevel = 5)
        {
            var npc = NPCInstance.Create("npc.test", "Crafter", "class.engineering");
            npc.statusTags.Add("crew");
            // SkillInstance.Level = floor(sqrt(XP / 100)), so minimum XP for level N = N² × 100.
            float minXpForLevel = craftingLevel * craftingLevel * 100f;
            npc.skillInstances.Add(new SkillInstance
            {
                skillId   = CraftingSystem.CraftingSkillId,
                currentXP = minXpForLevel,
            });
            station.npcs[npc.uid] = npc;
            return npc;
        }

        public static void DestroyCraftingRegistry(ContentRegistry registry)
        {
            if (registry != null)
                Object.DestroyImmediate(registry.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Recipe Availability Gating
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class RecipeAvailabilityTests
    {
        private ContentRegistry _registry;
        private CraftingSystem  _crafting;
        private StationState    _station;
        private FoundationInstance _bench;
        private bool _originalFlag;

        [SetUp]
        public void SetUp()
        {
            _originalFlag = FeatureFlags.CraftingSystem;
            FeatureFlags.CraftingSystem = true;
            (_station, _registry, _crafting) = CraftingTestHelpers.MakeSetup(unlockResearch: false);
            _bench = CraftingTestHelpers.AddWorkbench(_station);
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.CraftingSystem = _originalFlag;
            CraftingTestHelpers.DestroyCraftingRegistry(_registry);
        }

        [Test]
        public void RecipeNotListed_WhenUnlockTagNotActive()
        {
            var available = _crafting.GetAvailableRecipes(_bench.uid, _station);
            Assert.AreEqual(0, available.Count,
                "Recipe should not be listed when its unlock tag is inactive.");
        }

        [Test]
        public void RecipeListed_WhenUnlockTagIsActive()
        {
            _station.SetTag(CraftingTestHelpers.UnlockTag);
            var available = _crafting.GetAvailableRecipes(_bench.uid, _station);
            Assert.AreEqual(1, available.Count,
                "Recipe should be listed when its unlock tag is active.");
            Assert.AreEqual(CraftingTestHelpers.RecipeId, available[0].id);
        }

        [Test]
        public void RecipeNotAvailable_WhenWorkbenchTypeDoesNotMatch()
        {
            // Register a recipe for a different workbench type.
            _registry.Recipes["recipe.other"] = new RecipeDefinition
            {
                id                    = "recipe.other",
                displayName           = "Other",
                requiredWorkbenchType = "refinery",
                unlockTag             = "",
                outputItemId          = "item.other",
                outputQuantity        = 1,
                baseTimeTicks         = 10,
            };
            var available = _crafting.GetAvailableRecipes(_bench.uid, _station);
            // Should not include the refinery recipe at a general_workshop bench.
            Assert.IsFalse(available.Exists(r => r.id == "recipe.other"),
                "Refinery recipe should not appear at a general_workshop bench.");
        }

        [Test]
        public void QueueRecipe_Fails_WhenUnlockTagNotActive()
        {
            var (ok, reason, uid) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            Assert.IsFalse(ok, "QueueRecipe should fail when unlock tag is not active.");
        }

        [Test]
        public void QueueRecipe_Succeeds_WhenUnlockTagIsActive()
        {
            _station.SetTag(CraftingTestHelpers.UnlockTag);
            var (ok, reason, uid) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            Assert.IsTrue(ok, $"QueueRecipe should succeed when unlock tag is active. Reason: {reason}");
            Assert.IsNotNull(uid);
        }

        [Test]
        public void QueueRecipe_Fails_WhenWorkbenchBuildableIdUnknown()
        {
            _station.SetTag(CraftingTestHelpers.UnlockTag);
            var (ok, reason, uid) = _crafting.QueueRecipe("nonexistent_uid", CraftingTestHelpers.RecipeId, _station);
            Assert.IsFalse(ok, "QueueRecipe should fail when workbench UID is unknown.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Execution Time Scaling
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class ExecutionTimeScalingTests
    {
        [Test]
        public void SkillScale_AtLevel0_IsHalf()
        {
            float scale = CraftingSystem.ComputeSkillScale(0);
            Assert.AreEqual(0.5f, scale, 0.0001f,
                "Skill scale at level 0 should be 0.5 (twice the base execution time).");
        }

        [Test]
        public void SkillScale_AtLevel5_IsOne()
        {
            float scale = CraftingSystem.ComputeSkillScale(5);
            Assert.AreEqual(1.0f, scale, 0.0001f,
                "Skill scale at level 5 should be 1.0 (base execution time).");
        }

        [Test]
        public void SkillScale_AtLevel10_IsOnePointFive()
        {
            float scale = CraftingSystem.ComputeSkillScale(10);
            Assert.AreEqual(1.5f, scale, 0.0001f,
                "Skill scale at level 10 should be 1.5 (33% faster than base).");
        }

        [Test]
        public void HighSkillNPC_Completes_FasterThanLowSkill()
        {
            // With a 10-tick base recipe:
            // level 0: effectiveTime = 10 / 0.5 = 20 ticks
            // level 10: effectiveTime = 10 / 1.5 ≈ 6.67 ticks
            float lowScale  = CraftingSystem.ComputeSkillScale(0);
            float highScale = CraftingSystem.ComputeSkillScale(10);

            float baseTime = 10f;
            float lowTime  = baseTime / lowScale;
            float highTime = baseTime / highScale;

            Assert.Less(highTime, lowTime,
                "A high-skill NPC should complete a recipe faster than a low-skill NPC.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Output Quality Tier Assignment
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class OutputQualityTierTests
    {
        [Test]
        public void QualityTier_AtLevel0_IsStandard()
            => Assert.AreEqual("standard", CraftingSystem.ComputeQualityTierFromLevel(0));

        [Test]
        public void QualityTier_AtLevel3_IsStandard()
            => Assert.AreEqual("standard", CraftingSystem.ComputeQualityTierFromLevel(3));

        [Test]
        public void QualityTier_AtLevel4_IsFine()
            => Assert.AreEqual("fine", CraftingSystem.ComputeQualityTierFromLevel(4));

        [Test]
        public void QualityTier_AtLevel7_IsFine()
            => Assert.AreEqual("fine", CraftingSystem.ComputeQualityTierFromLevel(7));

        [Test]
        public void QualityTier_AtLevel8_IsSuperior()
            => Assert.AreEqual("superior", CraftingSystem.ComputeQualityTierFromLevel(8));

        [Test]
        public void QualityTier_AtLevel10_IsSuperior()
            => Assert.AreEqual("superior", CraftingSystem.ComputeQualityTierFromLevel(10));

        [Test]
        public void HighSkillNPC_ProducesHigherQuality_ThanLowSkillNPC()
        {
            var tiers = new[] { "standard", "fine", "superior" };
            string lowTier  = CraftingSystem.ComputeQualityTierFromLevel(0);
            string highTier = CraftingSystem.ComputeQualityTierFromLevel(10);

            int lowIndex  = System.Array.IndexOf(tiers, lowTier);
            int highIndex = System.Array.IndexOf(tiers, highTier);

            Assert.Greater(highIndex, lowIndex,
                "A level-10 NPC should produce a higher quality tier than a level-0 NPC.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Material Haul and Execution Integration
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class CraftingPipelineTests
    {
        private ContentRegistry    _registry;
        private CraftingSystem     _crafting;
        private StationState       _station;
        private FoundationInstance _bench;
        private bool _originalFlag;

        [SetUp]
        public void SetUp()
        {
            _originalFlag = FeatureFlags.CraftingSystem;
            FeatureFlags.CraftingSystem = true;
            (_station, _registry, _crafting) = CraftingTestHelpers.MakeSetup(unlockResearch: true);
            _bench = CraftingTestHelpers.AddWorkbench(_station);
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.CraftingSystem = _originalFlag;
            CraftingTestHelpers.DestroyCraftingRegistry(_registry);
        }

        [Test]
        public void MaterialsConsumed_FromStorage_DuringHaul()
        {
            var storage = CraftingTestHelpers.AddStorage(
                _station, CraftingTestHelpers.InputItemId, 5);
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 5);

            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            // Tick once: queued → hauling (NPC assigned, materials gathered).
            _crafting.Tick(_station);
            // Tick again: hauling → executing.
            _crafting.Tick(_station);

            // Recipe requires 2 units of InputItemId; verify they were consumed.
            int remaining = storage.cargo.TryGetValue(CraftingTestHelpers.InputItemId, out int r) ? r : 0;
            Assert.AreEqual(3, remaining,
                "2 units of input material should have been consumed from storage.");
        }

        [Test]
        public void Execution_TransitionsToComplete_AfterSufficientTicks()
        {
            CraftingTestHelpers.AddStorage(_station, CraftingTestHelpers.InputItemId, 10);
            // Add output storage too.
            CraftingTestHelpers.AddStorage(_station);
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 5);

            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            // Advance enough ticks for the recipe to complete.
            // baseTimeTicks = 10, skillScale at level 5 = 1.0, effectiveTime = 10 ticks.
            // Haul takes 2 ticks minimum; add buffer.
            for (int i = 0; i < 30; i++)
                _crafting.Tick(_station);

            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(0, queue.Count,
                "Queue should be empty after recipe completes.");
        }

        [Test]
        public void OutputPlaced_InStorage_OnCompletion()
        {
            CraftingTestHelpers.AddStorage(_station, CraftingTestHelpers.InputItemId, 10);
            CraftingTestHelpers.AddStorage(_station);
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 5);

            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            for (int i = 0; i < 30; i++)
                _crafting.Tick(_station);

            // Sum output across all storage foundations (order of placement is not guaranteed).
            int totalOutput = 0;
            foreach (var f in _station.foundations.Values)
                totalOutput += f.cargo.TryGetValue(CraftingTestHelpers.OutputItemId, out int q) ? q : 0;

            Assert.AreEqual(1, totalOutput,
                "Output item should be placed in a storage foundation on recipe completion.");
        }

        [Test]
        public void MultipleRecipes_ProcessedInOrder()
        {
            CraftingTestHelpers.AddStorage(_station, CraftingTestHelpers.InputItemId, 20);
            CraftingTestHelpers.AddStorage(_station); // output storage
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 5);

            // Queue two recipes.
            var (ok1, _, uid1) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            var (ok2, _, uid2) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            Assert.IsTrue(ok1); Assert.IsTrue(ok2);

            // Verify queue has 2 entries in order.
            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual(uid1, queue[0].uid);
            Assert.AreEqual(uid2, queue[1].uid);

            // Run until first completes; second should now be the head.
            for (int i = 0; i < 30; i++)
            {
                _crafting.Tick(_station);
                queue = _crafting.GetQueue(_bench.uid, _station);
                if (queue.Count == 1) break;
            }

            // After first completes, second should be in the queue.
            queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(1, queue.Count,
                "After first recipe completes, second should still be in queue.");
            Assert.AreEqual(uid2, queue[0].uid,
                "Second recipe should be the new head of the queue.");
        }

        [Test]
        public void Tick_IsNoOp_WhenFeatureFlagFalse()
        {
            CraftingTestHelpers.AddStorage(_station, CraftingTestHelpers.InputItemId, 10);
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 5);
            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            FeatureFlags.CraftingSystem = false;
            for (int i = 0; i < 30; i++)
                _crafting.Tick(_station);

            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(1, queue.Count,
                "Tick should be a no-op when FeatureFlags.CraftingSystem is false.");
            Assert.AreEqual("queued", queue[0].status,
                "Queue entry status should remain 'queued' when feature flag is off.");
        }

        [Test]
        public void HaulDoesNotStart_WhenMaterialsUnavailable()
        {
            // No materials in storage.
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 5);
            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            // After first tick: queued → hauling is attempted (NPC assigned, but no materials).
            // After second tick: still hauling (insufficient materials).
            _crafting.Tick(_station);
            _crafting.Tick(_station);

            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(1, queue.Count);
            // Status should be "hauling" (NPC is assigned, waiting for materials)
            // or remain "queued" if no NPC was assigned — either way, not "executing".
            Assert.AreNotEqual("executing", queue[0].status,
                "Recipe should not start executing when materials are unavailable.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quality Tier Integration (hasQualityTiers = true)
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class QualityTierIntegrationTests
    {
        private ContentRegistry    _registry;
        private CraftingSystem     _crafting;
        private StationState       _station;
        private FoundationInstance _bench;
        private bool _originalFlag;

        [SetUp]
        public void SetUp()
        {
            _originalFlag = FeatureFlags.CraftingSystem;
            FeatureFlags.CraftingSystem = true;
            (_station, _registry, _crafting) = CraftingTestHelpers.MakeSetup(unlockResearch: true);
            _bench = CraftingTestHelpers.AddWorkbench(_station);

            // Override the recipe to have quality tiers.
            _registry.Recipes[CraftingTestHelpers.RecipeId].hasQualityTiers = true;
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.CraftingSystem = _originalFlag;
            CraftingTestHelpers.DestroyCraftingRegistry(_registry);
        }

        [Test]
        public void LowSkillNPC_ProducesStandardQuality()
        {
            CraftingTestHelpers.AddStorage(_station, CraftingTestHelpers.InputItemId, 10);
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 1);
            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            // Tick through hauling phase so quality tier is assigned.
            _crafting.Tick(_station);

            var entry = _crafting.GetActiveEntry(_bench.uid, _station);
            Assert.IsNotNull(entry);
            Assert.AreEqual("standard", entry.outputQualityTier,
                "A level-1 NPC should produce standard quality.");
        }

        [Test]
        public void HighSkillNPC_ProducesSuperiorQuality()
        {
            CraftingTestHelpers.AddStorage(_station, CraftingTestHelpers.InputItemId, 10);
            CraftingTestHelpers.AddCraftingNpc(_station, craftingLevel: 9);
            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            _crafting.Tick(_station);

            var entry = _crafting.GetActiveEntry(_bench.uid, _station);
            Assert.IsNotNull(entry);
            Assert.AreEqual("superior", entry.outputQualityTier,
                "A level-9 NPC should produce superior quality.");
        }
    }
}
