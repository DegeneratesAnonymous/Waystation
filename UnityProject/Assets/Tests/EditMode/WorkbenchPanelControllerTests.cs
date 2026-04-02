// WorkbenchPanelControllerTests.cs
// EditMode unit tests for WorkbenchPanelController (UI-025).
//
// Tests cover:
//   • Material availability indicator — all three states (Sufficient/Partial/Missing)
//     via InventorySystem.CheckSingleMaterial and CheckRecipeMaterials
//   • Locked recipe is non-interactive (pickingMode == Ignore, opacity < 1)
//   • Null-safety — Refresh with null station does not throw
//   • Queue operations — RemoveFromQueue and MoveInQueue call CraftingSystem correctly
//   • GetAllRecipesForWorkbench returns locked recipes (those whose unlock tag
//     is not in station.activeTags) alongside unlocked recipes

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── Test helpers ───────────────────────────────────────────────────────────

    internal static class WorkbenchTestInventoryHelpers
    {
        /// <summary>Adds a module-based cargo hold with optional item stock.</summary>
        public static ModuleInstance AddCargoModule(
            ContentRegistry registry, StationState station,
            string itemId = null, int qty = 0)
        {
            const string ModDefId = "module.wbtest.cargo";
            if (!registry.Modules.ContainsKey(ModDefId))
            {
                registry.Modules[ModDefId] = new ModuleDefinition
                {
                    id            = ModDefId,
                    displayName   = "Test Cargo",
                    cargoCapacity = 100,
                };
            }
            var mod = ModuleInstance.Create(ModDefId, "Test Cargo Hold", "cargo");
            if (itemId != null) mod.inventory[itemId] = qty;
            station.modules[mod.uid] = mod;
            return mod;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Material Availability Indicator (unit — InventorySystem helpers)
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class WorkbenchMaterialAvailabilityTests
    {
        private ContentRegistry _registry;
        private InventorySystem _inventory;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            (_station, _registry, _) = CraftingTestHelpers.MakeSetup();
            _inventory = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => CraftingTestHelpers.DestroyCraftingRegistry(_registry);

        // ── CheckSingleMaterial ───────────────────────────────────────────────

        [Test]
        public void CheckSingleMaterial_Sufficient_WhenEnoughPresent()
        {
            WorkbenchTestInventoryHelpers.AddCargoModule(
                _registry, _station, CraftingTestHelpers.InputItemId, qty: 5);

            var status = _inventory.CheckSingleMaterial(_station, CraftingTestHelpers.InputItemId, 2);
            Assert.AreEqual(InventorySystem.MaterialStatus.Sufficient, status);
        }

        [Test]
        public void CheckSingleMaterial_Partial_WhenSomePresentButNotEnough()
        {
            WorkbenchTestInventoryHelpers.AddCargoModule(
                _registry, _station, CraftingTestHelpers.InputItemId, qty: 1);

            // Need 5, have 1.
            var status = _inventory.CheckSingleMaterial(_station, CraftingTestHelpers.InputItemId, 5);
            Assert.AreEqual(InventorySystem.MaterialStatus.Partial, status);
        }

        [Test]
        public void CheckSingleMaterial_Missing_WhenNonePresent()
        {
            // No cargo holds added — station is empty.
            var status = _inventory.CheckSingleMaterial(_station, CraftingTestHelpers.InputItemId, 2);
            Assert.AreEqual(InventorySystem.MaterialStatus.Missing, status);
        }

        // ── CheckRecipeMaterials ──────────────────────────────────────────────

        [Test]
        public void CheckRecipeMaterials_Sufficient_WhenAllInputsPresent()
        {
            var recipe = new RecipeDefinition
            {
                id             = "recipe.x",
                inputMaterials = new Dictionary<string, int>
                {
                    { CraftingTestHelpers.InputItemId, 2 },
                },
                outputItemId = "item.x",
            };

            WorkbenchTestInventoryHelpers.AddCargoModule(
                _registry, _station, CraftingTestHelpers.InputItemId, qty: 3);

            var status = _inventory.CheckRecipeMaterials(_station, recipe);
            Assert.AreEqual(InventorySystem.MaterialStatus.Sufficient, status);
        }

        [Test]
        public void CheckRecipeMaterials_Partial_WhenSomeMaterialsPresent()
        {
            var recipe = new RecipeDefinition
            {
                id             = "recipe.x",
                inputMaterials = new Dictionary<string, int>
                {
                    { CraftingTestHelpers.InputItemId, 5 },
                },
                outputItemId = "item.x",
            };

            WorkbenchTestInventoryHelpers.AddCargoModule(
                _registry, _station, CraftingTestHelpers.InputItemId, qty: 2);

            var status = _inventory.CheckRecipeMaterials(_station, recipe);
            Assert.AreEqual(InventorySystem.MaterialStatus.Partial, status);
        }

        [Test]
        public void CheckRecipeMaterials_Missing_WhenNoMaterialsPresent()
        {
            var recipe = new RecipeDefinition
            {
                id             = "recipe.x",
                inputMaterials = new Dictionary<string, int>
                {
                    { CraftingTestHelpers.InputItemId, 2 },
                },
                outputItemId = "item.x",
            };

            // No cargo.
            var status = _inventory.CheckRecipeMaterials(_station, recipe);
            Assert.AreEqual(InventorySystem.MaterialStatus.Missing, status);
        }

        [Test]
        public void CheckRecipeMaterials_Sufficient_WhenNoInputMaterialsRequired()
        {
            var recipe = new RecipeDefinition
            {
                id             = "recipe.free",
                inputMaterials = new Dictionary<string, int>(),
                outputItemId   = "item.free",
            };
            var status = _inventory.CheckRecipeMaterials(_station, recipe);
            Assert.AreEqual(InventorySystem.MaterialStatus.Sufficient, status);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetAllRecipesForWorkbench — includes locked recipes
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class GetAllRecipesForWorkbenchTests
    {
        private ContentRegistry    _registry;
        private CraftingSystem     _crafting;
        private StationState       _station;
        private FoundationInstance _bench;

        [SetUp]
        public void SetUp()
        {
            (_station, _registry, _crafting) = CraftingTestHelpers.MakeSetup(unlockResearch: false);
            _bench = CraftingTestHelpers.AddWorkbench(_station);
        }

        [TearDown]
        public void TearDown() => CraftingTestHelpers.DestroyCraftingRegistry(_registry);

        [Test]
        public void GetAllRecipesForWorkbench_ReturnsLockedRecipe_WhenTagInactive()
        {
            // The test recipe has an unlock tag that is NOT active on this station.
            var all = _crafting.GetAllRecipesForWorkbench(_bench.uid, _station);
            Assert.AreEqual(1, all.Count,
                "GetAllRecipesForWorkbench should return the locked recipe even when its tag is inactive.");
            Assert.AreEqual(CraftingTestHelpers.RecipeId, all[0].id);
        }

        [Test]
        public void GetAllRecipesForWorkbench_ReturnsRecipe_WhenTagActive()
        {
            _station.SetTag(CraftingTestHelpers.UnlockTag);
            var all = _crafting.GetAllRecipesForWorkbench(_bench.uid, _station);
            Assert.AreEqual(1, all.Count);
        }

        [Test]
        public void GetAllRecipesForWorkbench_ExcludesRecipesForOtherWorkbenchType()
        {
            _registry.Recipes["recipe.refinery"] = new RecipeDefinition
            {
                id                    = "recipe.refinery",
                displayName           = "Refinery Recipe",
                requiredWorkbenchType = "refinery",
                unlockTag             = "",
                outputItemId          = "item.refined",
                outputQuantity        = 1,
                baseTimeTicks         = 10,
            };

            var all = _crafting.GetAllRecipesForWorkbench(_bench.uid, _station);
            Assert.IsFalse(all.Exists(r => r.id == "recipe.refinery"),
                "Recipe for a different workbench type should not appear.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Queue operations — RemoveFromQueue / MoveInQueue
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class WorkbenchQueueOperationTests
    {
        private ContentRegistry    _registry;
        private CraftingSystem     _crafting;
        private StationState       _station;
        private FoundationInstance _bench;

        [SetUp]
        public void SetUp()
        {
            (_station, _registry, _crafting) = CraftingTestHelpers.MakeSetup(unlockResearch: true);
            _bench = CraftingTestHelpers.AddWorkbench(_station);
        }

        [TearDown]
        public void TearDown() => CraftingTestHelpers.DestroyCraftingRegistry(_registry);

        private string EnqueueTestRecipe()
        {
            var (ok, _, uid) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            Assert.IsTrue(ok, "QueueRecipe should succeed in test setup.");
            return uid;
        }

        [Test]
        public void RemoveFromQueue_RemovesQueuedEntry()
        {
            string uid = EnqueueTestRecipe();

            bool removed = _crafting.RemoveFromQueue(_bench.uid, uid, _station);

            Assert.IsTrue(removed, "RemoveFromQueue should return true for a valid queued entry.");
            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(0, queue.Count, "Queue should be empty after removing the only entry.");
        }

        [Test]
        public void RemoveFromQueue_ReturnsFalse_WhenEntryNotFound()
        {
            EnqueueTestRecipe();

            bool removed = _crafting.RemoveFromQueue(_bench.uid, "nonexistent-uid", _station);
            Assert.IsFalse(removed, "RemoveFromQueue should return false for an unknown entry uid.");
        }

        [Test]
        public void RemoveFromQueue_ReturnsFalse_ForExecutingEntry()
        {
            string uid = EnqueueTestRecipe();
            // Manually promote entry to executing to simulate in-progress.
            _station.workbenchQueues[_bench.uid][0].status = "executing";

            bool removed = _crafting.RemoveFromQueue(_bench.uid, uid, _station);
            Assert.IsFalse(removed, "RemoveFromQueue should not remove an entry that is executing.");
        }

        [Test]
        public void MoveInQueue_MovesEntryUp()
        {
            // Enqueue two recipes; the second should move up past the first.
            var (_, _, uid1) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            var (_, _, uid2) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            bool moved = _crafting.MoveInQueue(_bench.uid, uid2, -1, _station);

            Assert.IsTrue(moved, "MoveInQueue up should succeed.");
            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(uid2, queue[0].uid, "Entry 2 should now be first after moving up.");
            Assert.AreEqual(uid1, queue[1].uid, "Entry 1 should now be second.");
        }

        [Test]
        public void MoveInQueue_MovesEntryDown()
        {
            var (_, _, uid1) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            var (_, _, uid2) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            bool moved = _crafting.MoveInQueue(_bench.uid, uid1, +1, _station);

            Assert.IsTrue(moved, "MoveInQueue down should succeed.");
            var queue = _crafting.GetQueue(_bench.uid, _station);
            Assert.AreEqual(uid2, queue[0].uid, "Entry 2 should now be first after moving entry 1 down.");
            Assert.AreEqual(uid1, queue[1].uid, "Entry 1 should now be second.");
        }

        [Test]
        public void MoveInQueue_ReturnsFalse_WhenAlreadyAtTop()
        {
            var (_, _, uid) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);

            bool moved = _crafting.MoveInQueue(_bench.uid, uid, -1, _station);

            Assert.IsFalse(moved, "MoveInQueue up should fail when entry is already at the top.");
        }

        [Test]
        public void MoveInQueue_ReturnsFalse_ForExecutingEntry()
        {
            var (_, _, uid1) = _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            _crafting.QueueRecipe(_bench.uid, CraftingTestHelpers.RecipeId, _station);
            // Promote first entry to executing.
            _station.workbenchQueues[_bench.uid][0].status = "executing";

            bool moved = _crafting.MoveInQueue(_bench.uid, uid1, +1, _station);
            Assert.IsFalse(moved, "MoveInQueue should not reorder an executing entry.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WorkbenchPanelController — null-safety and locked recipe rendering
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    public class WorkbenchPanelNullSafetyTests
    {
        private WorkbenchPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new WorkbenchPanelController();

        [Test]
        public void Refresh_NullState_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _panel.Refresh(null, null, null, null, null, null));
        }

        [Test]
        public void Refresh_ValidFoundationUid_NullDependencies_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _panel.Refresh("uid-1", null, null, null, null, null));
        }
    }

    [TestFixture]
    public class WorkbenchPanelLockedRecipeTests
    {
        private ContentRegistry    _registry;
        private CraftingSystem     _crafting;
        private InventorySystem    _inventory;
        private StationState       _station;
        private FoundationInstance _bench;

        [SetUp]
        public void SetUp()
        {
            (_station, _registry, _crafting) = CraftingTestHelpers.MakeSetup(unlockResearch: false);
            _bench     = CraftingTestHelpers.AddWorkbench(_station);
            _inventory = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => CraftingTestHelpers.DestroyCraftingRegistry(_registry);

        [Test]
        public void RecipesTab_LockedRecipeRow_IsNonInteractive()
        {
            // The test recipe has an unlock tag that is NOT active — it should be locked.
            var panel = new WorkbenchPanelController();
            panel.Refresh(_bench.uid, _station, _registry, null, _crafting, _inventory);
            panel.SelectTab("recipes");

            // Find the recipe rows.  Locked rows get pickingMode == Ignore.
            var rows = panel.Query<VisualElement>(className: "ws-workbench-panel__recipe-row").ToList();
            Assert.IsTrue(rows.Count > 0, "At least one recipe row should be present.");

            bool anyLocked = false;
            foreach (var row in rows)
            {
                if (row.pickingMode == PickingMode.Ignore)
                {
                    anyLocked = true;
                    break;
                }
            }
            Assert.IsTrue(anyLocked,
                "A locked recipe row should have pickingMode == Ignore (non-interactive).");
        }

        [Test]
        public void RecipesTab_UnlockedRecipeRow_IsInteractive()
        {
            // Activate the unlock tag so the recipe becomes available.
            _station.SetTag(CraftingTestHelpers.UnlockTag);

            var panel = new WorkbenchPanelController();
            panel.Refresh(_bench.uid, _station, _registry, null, _crafting, _inventory);
            panel.SelectTab("recipes");

            var rows = panel.Query<VisualElement>(className: "ws-workbench-panel__recipe-row").ToList();
            Assert.IsTrue(rows.Count > 0);

            bool anyInteractive = false;
            foreach (var row in rows)
            {
                if (row.pickingMode == PickingMode.Position)
                {
                    anyInteractive = true;
                    break;
                }
            }
            Assert.IsTrue(anyInteractive,
                "An unlocked recipe row should have pickingMode == Position (interactive).");
        }
    }
}
