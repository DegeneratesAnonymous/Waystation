// BuildMenuControllerTests.cs
// EditMode unit tests for the Station → Build sub-tab (UI-007).
//
// Tests cover:
//   • BuildingSystem.BeginPlacement sets PendingPlacementId correctly
//   • BuildingSystem.BeginPlacement returns false for unknown buildables
//   • BuildingSystem.EndPlacement clears PendingPlacementId
//   • BuildingSystem.GetQueue returns only in-progress foundations
//   • InventorySystem.CheckMaterials returns Sufficient when all materials are present
//   • InventorySystem.CheckMaterials returns Partial when some materials are present
//   • InventorySystem.CheckMaterials returns Missing when no materials are present
//   • InventorySystem.GetMissingMaterials returns correct shortfalls
//   • BuildSubPanelController queue renders green pip for Sufficient
//   • BuildSubPanelController queue renders amber pip for Partial
//   • BuildSubPanelController queue renders red pip for Missing
//   • BuildSubPanelController queue row count matches GetQueue result
//   • BuildSubPanelController empty label shown when queue is empty
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
    // ── Shared helpers ─────────────────────────────────────────────────────────

    internal static class BuildTestHelpers
    {
        public const string WallBuildableId   = "buildable.wall";
        public const string FloorBuildableId  = "buildable.floor";
        public const string OreItemId         = "item.ore";

        /// <summary>
        /// Creates a minimal ContentRegistry + BuildingSystem + InventorySystem
        /// with two registered buildables (wall requires ore; floor requires nothing).
        /// </summary>
        public static (StationState station, ContentRegistry registry,
                       BuildingSystem building, InventorySystem inventory) MakeSetup()
        {
            var go       = new GameObject("BuildTestRegistry");
            var registry = go.AddComponent<ContentRegistry>();

            // Wall — requires 2 ore
            registry.Buildables[WallBuildableId] = new BuildableDefinition
            {
                id              = WallBuildableId,
                displayName     = "Wall",
                requiredMaterials = new Dictionary<string, int> { { OreItemId, 2 } },
            };

            // Floor — no material requirements
            registry.Buildables[FloorBuildableId] = new BuildableDefinition
            {
                id          = FloorBuildableId,
                displayName = "Floor",
            };

            var station   = new StationState("Test");
            var building  = new BuildingSystem(registry);
            var inventory = new InventorySystem(registry);

            return (station, registry, building, inventory);
        }

        /// <summary>
        /// Adds a module cargo hold with specified items to the station.
        /// </summary>
        public static ModuleInstance AddCargoHold(ContentRegistry registry,
                                                   StationState station,
                                                   string itemId = null, int qty = 0)
        {
            const string ModDefId = "module.cargo";
            if (!registry.Modules.ContainsKey(ModDefId))
            {
                registry.Modules[ModDefId] = new ModuleDefinition
                {
                    id            = ModDefId,
                    displayName   = "Cargo",
                    cargoCapacity = 100,
                };
            }

            var mod = ModuleInstance.Create(ModDefId, "Cargo Hold", "cargo");
            if (itemId != null) mod.inventory[itemId] = qty;
            station.modules[mod.uid] = mod;
            return mod;
        }

        public static void Destroy(ContentRegistry registry)
        {
            if (registry != null)
                Object.DestroyImmediate(registry.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildingSystem — BeginPlacement / EndPlacement / GetQueue
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class BuildingSystemPlacementTests
    {
        private ContentRegistry _registry;
        private BuildingSystem  _building;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            var setup = BuildTestHelpers.MakeSetup();
            _registry = setup.registry;
            _building = setup.building;
            _station  = setup.station;
        }

        [TearDown]
        public void TearDown() => BuildTestHelpers.Destroy(_registry);

        [Test]
        public void BeginPlacement_KnownBuildable_SetsPendingIdAndReturnsTrue()
        {
            bool result = _building.BeginPlacement(BuildTestHelpers.WallBuildableId);

            Assert.IsTrue(result, "BeginPlacement should return true for a registered buildable.");
            Assert.AreEqual(BuildTestHelpers.WallBuildableId, _building.PendingPlacementId);
        }

        [Test]
        public void BeginPlacement_UnknownBuildable_ReturnsFalseAndDoesNotSetId()
        {
            bool result = _building.BeginPlacement("buildable.nonexistent");

            Assert.IsFalse(result, "BeginPlacement should return false for an unregistered buildable.");
            Assert.IsNull(_building.PendingPlacementId,
                "PendingPlacementId should remain null when buildable is unknown.");
        }

        [Test]
        public void BeginPlacement_NullBuildableId_ReturnsFalseAndDoesNotThrow()
        {
            bool result = false;
            Assert.DoesNotThrow(() => result = _building.BeginPlacement(null),
                "BeginPlacement(null) should not throw.");
            Assert.IsFalse(result, "BeginPlacement(null) should return false.");
            Assert.IsNull(_building.PendingPlacementId);
        }

        [Test]
        public void BeginPlacement_EmptyBuildableId_ReturnsFalseAndDoesNotThrow()
        {
            bool result = false;
            Assert.DoesNotThrow(() => result = _building.BeginPlacement(string.Empty),
                "BeginPlacement(\"\") should not throw.");
            Assert.IsFalse(result, "BeginPlacement(\"\") should return false.");
            Assert.IsNull(_building.PendingPlacementId);
        }

        [Test]
        public void EndPlacement_ClearsPendingId()
        {
            _building.BeginPlacement(BuildTestHelpers.WallBuildableId);
            _building.EndPlacement();

            Assert.IsNull(_building.PendingPlacementId,
                "EndPlacement should clear PendingPlacementId.");
        }

        [Test]
        public void PendingPlacementId_IsNullByDefault()
        {
            Assert.IsNull(_building.PendingPlacementId);
        }

        [Test]
        public void BeginPlacement_OverwritesPreviousId()
        {
            _building.BeginPlacement(BuildTestHelpers.WallBuildableId);
            _building.BeginPlacement(BuildTestHelpers.FloorBuildableId);

            Assert.AreEqual(BuildTestHelpers.FloorBuildableId, _building.PendingPlacementId);
        }
    }

    [TestFixture]
    internal class BuildingSystemGetQueueTests
    {
        private ContentRegistry _registry;
        private BuildingSystem  _building;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            var setup = BuildTestHelpers.MakeSetup();
            _registry = setup.registry;
            _building = setup.building;
            _station  = setup.station;
        }

        [TearDown]
        public void TearDown() => BuildTestHelpers.Destroy(_registry);

        [Test]
        public void GetQueue_EmptyStation_ReturnsEmptyList()
        {
            var queue = _building.GetQueue(_station);
            Assert.IsNotNull(queue);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void GetQueue_OnlyReturnsInProgressFoundations()
        {
            var awaiting    = FoundationInstance.Create(BuildTestHelpers.WallBuildableId,  0, 0);
            var constructing = FoundationInstance.Create(BuildTestHelpers.FloorBuildableId, 1, 0);
            var complete    = FoundationInstance.Create(BuildTestHelpers.WallBuildableId,  2, 0);

            awaiting.status     = "awaiting_haul";
            constructing.status = "constructing";
            complete.status     = "complete";

            _station.foundations[awaiting.uid]     = awaiting;
            _station.foundations[constructing.uid] = constructing;
            _station.foundations[complete.uid]     = complete;

            var queue = _building.GetQueue(_station);

            Assert.AreEqual(2, queue.Count,
                "GetQueue should return only awaiting_haul and constructing foundations.");
            Assert.IsTrue(queue.Contains(awaiting),     "Queue should contain awaiting_haul foundation.");
            Assert.IsTrue(queue.Contains(constructing), "Queue should contain constructing foundation.");
            Assert.IsFalse(queue.Contains(complete),    "Queue should not contain complete foundation.");
        }

        [Test]
        public void GetQueue_ReturnsSnapshot_NotLiveReference()
        {
            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "awaiting_haul";
            _station.foundations[f.uid] = f;

            var queue = _building.GetQueue(_station);
            Assert.AreEqual(1, queue.Count);

            // Add another foundation AFTER getting the snapshot
            var f2 = FoundationInstance.Create(BuildTestHelpers.FloorBuildableId, 1, 0);
            f2.status = "constructing";
            _station.foundations[f2.uid] = f2;

            Assert.AreEqual(1, queue.Count, "Snapshot should not reflect later additions.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InventorySystem.CheckMaterials
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CheckMaterialsTests
    {
        private ContentRegistry _registry;
        private InventorySystem _inventory;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            var setup = BuildTestHelpers.MakeSetup();
            _registry = setup.registry;
            _inventory = setup.inventory;
            _station  = setup.station;
        }

        [TearDown]
        public void TearDown() => BuildTestHelpers.Destroy(_registry);

        [Test]
        public void CheckMaterials_AllMaterialsPresent_ReturnsSufficient()
        {
            BuildTestHelpers.AddCargoHold(_registry, _station,
                itemId: BuildTestHelpers.OreItemId, qty: 10);

            var status = _inventory.CheckMaterials(_station, BuildTestHelpers.WallBuildableId);

            Assert.AreEqual(InventorySystem.MaterialStatus.Sufficient, status);
        }

        [Test]
        public void CheckMaterials_SomeMaterialsPresent_ReturnsPartial()
        {
            // Wall requires 2 ore; add only 1
            BuildTestHelpers.AddCargoHold(_registry, _station,
                itemId: BuildTestHelpers.OreItemId, qty: 1);

            var status = _inventory.CheckMaterials(_station, BuildTestHelpers.WallBuildableId);

            Assert.AreEqual(InventorySystem.MaterialStatus.Partial, status);
        }

        [Test]
        public void CheckMaterials_NoMaterialsPresent_ReturnsMissing()
        {
            // No cargo holds added — station is empty
            var status = _inventory.CheckMaterials(_station, BuildTestHelpers.WallBuildableId);

            Assert.AreEqual(InventorySystem.MaterialStatus.Missing, status);
        }

        [Test]
        public void CheckMaterials_NoBuildableRequirements_ReturnsSufficient()
        {
            // Floor has no required materials
            var status = _inventory.CheckMaterials(_station, BuildTestHelpers.FloorBuildableId);

            Assert.AreEqual(InventorySystem.MaterialStatus.Sufficient, status);
        }

        [Test]
        public void CheckMaterials_UnknownBuildable_ReturnsMissing()
        {
            var status = _inventory.CheckMaterials(_station, "buildable.unknown");

            Assert.AreEqual(InventorySystem.MaterialStatus.Missing, status);
        }

        [Test]
        public void GetMissingMaterials_AllPresent_ReturnsEmptyDict()
        {
            BuildTestHelpers.AddCargoHold(_registry, _station,
                itemId: BuildTestHelpers.OreItemId, qty: 5);

            var missing = _inventory.GetMissingMaterials(_station, BuildTestHelpers.WallBuildableId);

            Assert.AreEqual(0, missing.Count, "No missing materials when all are present.");
        }

        [Test]
        public void GetMissingMaterials_Shortfall_ReturnsCorrectAmount()
        {
            // Wall requires 2 ore; add only 1 → shortfall of 1
            BuildTestHelpers.AddCargoHold(_registry, _station,
                itemId: BuildTestHelpers.OreItemId, qty: 1);

            var missing = _inventory.GetMissingMaterials(_station, BuildTestHelpers.WallBuildableId);

            Assert.IsTrue(missing.ContainsKey(BuildTestHelpers.OreItemId));
            Assert.AreEqual(1, missing[BuildTestHelpers.OreItemId]);
        }

        [Test]
        public void GetMissingMaterials_NonePresent_ReturnsFullRequirement()
        {
            // Wall requires 2 ore; nothing in stock → shortfall of 2
            var missing = _inventory.GetMissingMaterials(_station, BuildTestHelpers.WallBuildableId);

            Assert.IsTrue(missing.ContainsKey(BuildTestHelpers.OreItemId));
            Assert.AreEqual(2, missing[BuildTestHelpers.OreItemId]);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BuildSubPanelController — queue display
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class BuildSubPanelQueueTests
    {
        private ContentRegistry        _registry;
        private BuildingSystem         _building;
        private InventorySystem        _inventory;
        private StationState           _station;
        private BuildSubPanelController _panel;

        [SetUp]
        public void SetUp()
        {
            var setup = BuildTestHelpers.MakeSetup();
            _registry  = setup.registry;
            _building  = setup.building;
            _inventory = setup.inventory;
            _station   = setup.station;
            _panel     = new BuildSubPanelController();
        }

        [TearDown]
        public void TearDown() => BuildTestHelpers.Destroy(_registry);

        [Test]
        public void Refresh_EmptyQueue_ShowsEmptyLabel()
        {
            _panel.Refresh(_station, _building, _inventory, _registry);

            var emptyLabel = _panel.Q<Label>(className: "ws-build-panel__empty");
            Assert.IsNotNull(emptyLabel,    "Empty label should exist.");
            Assert.AreEqual(DisplayStyle.Flex, emptyLabel.style.display.value,
                "Empty label should be visible when the queue is empty.");
        }

        [Test]
        public void Refresh_QueuedFoundation_AddsQueueRow()
        {
            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "awaiting_haul";
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);

            var rows = _panel.Query<VisualElement>(className: "ws-build-panel__queue-row").ToList();
            Assert.AreEqual(1, rows.Count, "One queue row should appear for one in-progress foundation.");
        }

        [Test]
        public void Refresh_SufficientMaterials_ShowsGreenPip()
        {
            // Add enough ore so the wall's materials are covered
            BuildTestHelpers.AddCargoHold(_registry, _station,
                itemId: BuildTestHelpers.OreItemId, qty: 10);

            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "awaiting_haul";
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);

            var pip = _panel.Q<StatusPip>();
            Assert.IsNotNull(pip, "A StatusPip should be present in the queue row.");
            Assert.AreEqual(StatusPip.State.On, pip.PipState,
                "Green pip (On) expected when all materials are sufficient.");
        }

        [Test]
        public void Refresh_PartialMaterials_ShowsAmberPip()
        {
            // Wall needs 2 ore; only 1 available
            BuildTestHelpers.AddCargoHold(_registry, _station,
                itemId: BuildTestHelpers.OreItemId, qty: 1);

            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "awaiting_haul";
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);

            var pip = _panel.Q<StatusPip>();
            Assert.IsNotNull(pip);
            Assert.AreEqual(StatusPip.State.Warning, pip.PipState,
                "Amber pip (Warning) expected when materials are partially available.");
        }

        [Test]
        public void Refresh_MissingMaterials_ShowsRedPip()
        {
            // No ore in stock at all
            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "awaiting_haul";
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);

            var pip = _panel.Q<StatusPip>();
            Assert.IsNotNull(pip);
            Assert.AreEqual(StatusPip.State.Fault, pip.PipState,
                "Red pip (Fault) expected when all materials are missing.");
        }

        [Test]
        public void Refresh_CompletedFoundation_NotIncludedInQueue()
        {
            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "complete";
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);

            var rows = _panel.Query<VisualElement>(className: "ws-build-panel__queue-row").ToList();
            Assert.AreEqual(0, rows.Count, "Complete foundations should not appear in the queue.");
        }

        [Test]
        public void Refresh_RemovesRowWhenFoundationCompletes()
        {
            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status = "awaiting_haul";
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);
            Assert.AreEqual(1, _panel.Query<VisualElement>(className: "ws-build-panel__queue-row").ToList().Count);

            // Mark complete and refresh again
            f.status = "complete";
            _panel.Refresh(_station, _building, _inventory, _registry);

            var rows = _panel.Query<VisualElement>(className: "ws-build-panel__queue-row").ToList();
            Assert.AreEqual(0, rows.Count, "Row should be removed after foundation completes.");
        }

        [Test]
        public void Refresh_ProgressUpdates_ReflectedInFillWidth()
        {
            var f = FoundationInstance.Create(BuildTestHelpers.WallBuildableId, 0, 0);
            f.status        = "constructing";
            f.buildProgress = 0.5f;
            _station.foundations[f.uid] = f;

            _panel.Refresh(_station, _building, _inventory, _registry);

            var fill = _panel.Q<VisualElement>(className: "ws-build-panel__progress-fill");
            Assert.IsNotNull(fill, "Progress fill element should exist.");
            // Width is set inline as a percentage via style.width
            Assert.AreEqual(LengthUnit.Percent, fill.style.width.value.unit,
                "Progress fill width should be expressed as a percentage.");
        }
    }
}
