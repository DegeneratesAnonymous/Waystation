// InventorySubPanelControllerTests.cs
// EditMode unit and integration tests for InventorySubPanelController (UI-010)
// and InventorySystem.GetCargoHoldContents / InventorySystem.OnContentsChanged.
//
// Tests cover:
//   * Filter chip correctly shows/hides items by category (itemType)
//   * Sort by quantity, weight, and category produces correct ordering
//   * Expanded row shows correct container breakdown (room name, quantity)
//   * List updates when OnContentsChanged fires

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

    internal static class InventoryTestHelpers
    {
        public static StationState MakeStation() => new StationState("InventoryTest");

        public static ContentRegistry MakeRegistry()
        {
            var go  = new GameObject("InventoryTestRegistry");
            var reg = go.AddComponent<ContentRegistry>();
            return reg;
        }

        /// <summary>
        /// Registers an item definition in the registry and returns it.
        /// </summary>
        public static ItemDefinition RegisterItem(ContentRegistry registry,
            string id, string displayName, string itemType, float weight)
        {
            var defn = new ItemDefinition
            {
                id          = id,
                displayName = displayName,
                itemType    = itemType,
                weight      = weight,
            };
            registry.Items[id] = defn;
            return defn;
        }

        /// <summary>
        /// Adds a complete cargo-hold foundation container at (col, row) assigned to a
        /// "cargo_hold" room, with the given cargo contents.
        /// Returns the foundation instance.
        /// </summary>
        public static FoundationInstance AddCargoContainer(
            StationState station, int col, int row, Dictionary<string, int> cargo,
            int capacity = 1000)
        {
            string uid     = $"f_{col}_{row}";
            string tileKey = $"{col}_{row}";
            string roomKey = $"room_{col}_{row}";

            var f = new FoundationInstance
            {
                uid           = uid,
                buildableId   = "buildable.crate",
                tileCol       = col,
                tileRow       = row,
                status        = "complete",
                cargoCapacity = capacity,
            };
            if (cargo != null)
                foreach (var kv in cargo) f.cargo[kv.Key] = kv.Value;

            station.foundations[uid] = f;

            // Map tile → room.
            station.tileToRoomKey[tileKey] = roomKey;
            // Designate the room as a cargo hold.
            station.playerRoomTypeAssignments[roomKey] = "cargo_hold";

            return f;
        }

        /// <summary>Sets a custom name for the room associated with a container added by AddCargoContainer.</summary>
        public static void SetRoomName(StationState station, int col, int row, string name)
        {
            string roomKey = $"room_{col}_{row}";
            station.customRoomNames[roomKey] = name;
        }
    }

    // ── InventorySystem.GetCargoHoldContents ──────────────────────────────────

    [TestFixture]
    internal class GetCargoHoldContentsTests
    {
        private GameObject       _registryGo;
        private ContentRegistry  _registry;
        private InventorySystem  _inventory;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("GetCargoHoldContentsTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _inventory  = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void GetCargoHoldContents_NullStation_ReturnsEmpty()
        {
            var result = _inventory.GetCargoHoldContents(null);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetCargoHoldContents_EmptyStation_ReturnsEmpty()
        {
            var station = InventoryTestHelpers.MakeStation();
            var result  = _inventory.GetCargoHoldContents(station);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetCargoHoldContents_OneItem_ReturnsOneRow()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 1, 1,
                new Dictionary<string, int> { { "item.ore_iron", 10 } });

            var result = _inventory.GetCargoHoldContents(station);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("item.ore_iron", result[0].itemId);
            Assert.AreEqual("Iron Ore",      result[0].displayName);
            Assert.AreEqual("Material",      result[0].itemType);
            Assert.AreEqual(10,              result[0].totalQuantity);
            Assert.AreEqual(20f,             result[0].totalWeight, 0.001f);
        }

        [Test]
        public void GetCargoHoldContents_TwoContainersSameItem_AggregatesQuantityAndWeight()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 } });
            InventoryTestHelpers.AddCargoContainer(station, 1, 0,
                new Dictionary<string, int> { { "item.ore_iron", 3 } });

            var result = _inventory.GetCargoHoldContents(station);

            Assert.AreEqual(1, result.Count, "Items from two containers must be merged into one row.");
            Assert.AreEqual(8,   result[0].totalQuantity);
            Assert.AreEqual(16f, result[0].totalWeight, 0.001f);
            Assert.AreEqual(2,   result[0].containers.Count, "Two container entries expected.");
        }

        [Test]
        public void GetCargoHoldContents_DifferentItems_OneRowEach()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron",   "Iron Ore",   "Material",  2f);
            InventoryTestHelpers.RegisterItem(_registry, "item.repair_parts", "Repair Parts", "Material", 1f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int>
                {
                    { "item.ore_iron",     4 },
                    { "item.repair_parts", 2 },
                });

            var result = _inventory.GetCargoHoldContents(station);

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void GetCargoHoldContents_NonCargoHoldRoom_ExcludesContainer()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);

            // Add container but assign the room to a non-cargo-hold type.
            var f = InventoryTestHelpers.AddCargoContainer(station, 2, 2,
                new Dictionary<string, int> { { "item.ore_iron", 5 } });
            station.playerRoomTypeAssignments[$"room_2_2"] = "engineering_workshop";

            var result = _inventory.GetCargoHoldContents(station);

            Assert.AreEqual(0, result.Count, "Container not in cargo_hold room must be excluded.");
        }

        [Test]
        public void GetCargoHoldContents_ContainerDetail_IncludesRoomName()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 3 } });
            InventoryTestHelpers.SetRoomName(station, 0, 0, "Hold Alpha");

            var result = _inventory.GetCargoHoldContents(station);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].containers.Count);
            Assert.AreEqual("Hold Alpha", result[0].containers[0].roomName);
            Assert.AreEqual(3,            result[0].containers[0].quantity);
        }
    }

    // ── InventorySystem.OnContentsChanged ─────────────────────────────────────

    [TestFixture]
    internal class OnContentsChangedTests
    {
        private GameObject       _registryGo;
        private ContentRegistry  _registry;
        private InventorySystem  _inventory;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("OnContentsChangedTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _inventory  = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void AddItemToContainer_FiresOnContentsChanged()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            var f = InventoryTestHelpers.AddCargoContainer(station, 0, 0, null);

            int count = 0;
            _inventory.OnContentsChanged += () => count++;

            _inventory.AddItemToContainer(station, f.uid, "item.ore_iron", 5);

            Assert.AreEqual(1, count, "OnContentsChanged must fire once after AddItemToContainer.");
        }

        [Test]
        public void RemoveItemFromContainer_FiresOnContentsChanged()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            var f = InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 10 } });

            int count = 0;
            _inventory.OnContentsChanged += () => count++;

            _inventory.RemoveItemFromContainer(station, f.uid, "item.ore_iron", 3);

            Assert.AreEqual(1, count, "OnContentsChanged must fire once after RemoveItemFromContainer.");
        }

        [Test]
        public void AddItemToContainer_ZeroActual_DoesNotFire()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            // Container with capacity 0 cannot accept items.
            var f = new FoundationInstance
            {
                uid           = "f_zero_cap",
                tileCol       = 9,
                tileRow       = 9,
                status        = "complete",
                cargoCapacity = 0,
            };
            station.foundations[f.uid] = f;

            int count = 0;
            _inventory.OnContentsChanged += () => count++;

            _inventory.AddItemToContainer(station, f.uid, "item.ore_iron", 5);

            Assert.AreEqual(0, count, "OnContentsChanged must not fire when nothing is added.");
        }
    }

    // ── InventorySubPanelController — filter chips ─────────────────────────────

    [TestFixture]
    internal class InventorySubPanelFilterTests
    {
        private InventorySubPanelController _panel;
        private GameObject                  _registryGo;
        private ContentRegistry             _registry;
        private InventorySystem             _inventory;

        [SetUp]
        public void SetUp()
        {
            _panel      = new InventorySubPanelController();
            _registryGo = new GameObject("InventorySubPanelFilterTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _inventory  = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, _inventory));
        }

        [Test]
        public void Refresh_NullInventory_DoesNotThrow()
        {
            var station = InventoryTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }

        [Test]
        public void Refresh_NoItems_ShowsAllChipOnly()
        {
            var station = InventoryTestHelpers.MakeStation();
            _panel.Refresh(station, _inventory);

            var chips = _panel.Query<Button>(className: "ws-inventory-panel__filter-btn").ToList();
            Assert.AreEqual(1, chips.Count, "Only the 'ALL' chip should appear when there are no items.");
            StringAssert.AreEqualIgnoringCase("ALL", chips[0].text);
        }

        [Test]
        public void Refresh_TwoCategories_CreatesThreeChips()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron",   "Iron Ore",    "Material",  2f);
            InventoryTestHelpers.RegisterItem(_registry, "item.medkit",     "Medkit",      "Equipment", 0.5f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 }, { "item.medkit", 2 } });

            _panel.Refresh(station, _inventory);

            var chips = _panel.Query<Button>(className: "ws-inventory-panel__filter-btn").ToList();
            // ALL + Material + Equipment = 3
            Assert.AreEqual(3, chips.Count, "ALL chip plus one per distinct category expected.");
        }

        [Test]
        public void Refresh_DuplicateCategory_OnlyOneExtraChip()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron",   "Iron Ore",    "Material", 2f);
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_copper", "Copper Ore",  "Material", 1.8f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 }, { "item.ore_copper", 3 } });

            _panel.Refresh(station, _inventory);

            var chips = _panel.Query<Button>(className: "ws-inventory-panel__filter-btn").ToList();
            // ALL + Material (deduplicated) = 2
            Assert.AreEqual(2, chips.Count, "Duplicate category must produce only one extra chip.");
        }

        [Test]
        public void FilterByCategory_HidesNonMatchingRows()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material",  2f);
            InventoryTestHelpers.RegisterItem(_registry, "item.medkit",   "Medkit",   "Equipment", 0.5f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 }, { "item.medkit", 2 } });

            _panel.Refresh(station, _inventory);

            // Click the Material chip.
            var chips = _panel.Query<Button>(className: "ws-inventory-panel__filter-btn").ToList();
            Button materialChip = null;
            foreach (var c in chips)
                if (c.text.ToUpper().Contains("MATERIAL")) { materialChip = c; break; }

            Assert.IsNotNull(materialChip, "A chip for 'Material' must exist.");

            using var evt = ClickEvent.GetPooled();
            evt.target = materialChip;
            materialChip.SendEvent(evt);

            var rows = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row").ToList();
            int visible = 0;
            foreach (var r in rows)
                if (r.style.display != DisplayStyle.None) visible++;

            Assert.AreEqual(1, visible, "Only the Material row should be visible after filtering.");
        }

        [Test]
        public void FilterByAll_ShowsAllRows()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material",  2f);
            InventoryTestHelpers.RegisterItem(_registry, "item.medkit",   "Medkit",   "Equipment", 0.5f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 }, { "item.medkit", 2 } });

            _panel.Refresh(station, _inventory);

            var chips = _panel.Query<Button>(className: "ws-inventory-panel__filter-btn").ToList();
            Button allChip      = null;
            Button materialChip = null;
            foreach (var c in chips)
            {
                if (c.text.ToUpper() == "ALL") allChip = c;
                if (c.text.ToUpper().Contains("MATERIAL")) materialChip = c;
            }
            Assert.IsNotNull(allChip);
            Assert.IsNotNull(materialChip);

            // Narrow to Material first.
            using (var eMat = ClickEvent.GetPooled())
            {
                eMat.target = materialChip;
                materialChip.SendEvent(eMat);
            }

            // Then restore All.
            using (var eAll = ClickEvent.GetPooled())
            {
                eAll.target = allChip;
                allChip.SendEvent(eAll);
            }

            var rows = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row").ToList();
            int visible = 0;
            foreach (var r in rows)
                if (r.style.display != DisplayStyle.None) visible++;

            Assert.AreEqual(2, visible, "After clicking ALL, both rows should be visible again.");
        }
    }

    // ── InventorySubPanelController — sort ────────────────────────────────────

    [TestFixture]
    internal class InventorySubPanelSortTests
    {
        private InventorySubPanelController _panel;
        private GameObject                  _registryGo;
        private ContentRegistry             _registry;
        private InventorySystem             _inventory;

        [SetUp]
        public void SetUp()
        {
            _panel      = new InventorySubPanelController();
            _registryGo = new GameObject("InventorySubPanelSortTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _inventory  = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void SortByQuantity_RowsOrderedDescending()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.a", "A Item", "Material", 1f);
            InventoryTestHelpers.RegisterItem(_registry, "item.b", "B Item", "Material", 1f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.a", 3 }, { "item.b", 10 } });

            _panel.Refresh(station, _inventory);

            // Click the QTY sort button.
            var sortBtns = _panel.Query<Button>(className: "ws-inventory-panel__sort-btn").ToList();
            Button qtyBtn = null;
            foreach (var b in sortBtns)
                if (b.text == "QTY") { qtyBtn = b; break; }
            Assert.IsNotNull(qtyBtn);
            using (var e = ClickEvent.GetPooled()) { e.target = qtyBtn; qtyBtn.SendEvent(e); }

            // The first name label in the list should be for the higher-qty item.
            var nameLabels = _panel.Query<Label>(className: "ws-inventory-panel__item-name").ToList();
            Assert.GreaterOrEqual(nameLabels.Count, 2);
            Assert.AreEqual("B Item", nameLabels[0].text, "Higher quantity item must appear first.");
        }

        [Test]
        public void SortByWeight_RowsOrderedDescending()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.light", "Light Item", "Material", 0.5f);
            InventoryTestHelpers.RegisterItem(_registry, "item.heavy", "Heavy Item", "Material", 5f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.light", 10 }, { "item.heavy", 2 } });

            _panel.Refresh(station, _inventory);

            var sortBtns = _panel.Query<Button>(className: "ws-inventory-panel__sort-btn").ToList();
            Button weightBtn = null;
            foreach (var b in sortBtns)
                if (b.text == "WEIGHT") { weightBtn = b; break; }
            Assert.IsNotNull(weightBtn);
            using (var e = ClickEvent.GetPooled()) { e.target = weightBtn; weightBtn.SendEvent(e); }

            // heavy: 2×5kg=10kg, light: 10×0.5kg=5kg → heavy first
            var nameLabels = _panel.Query<Label>(className: "ws-inventory-panel__item-name").ToList();
            Assert.GreaterOrEqual(nameLabels.Count, 2);
            Assert.AreEqual("Heavy Item", nameLabels[0].text, "Heavier total-weight item must appear first.");
        }

        [Test]
        public void SortByCategory_RowsOrderedAlphabetically()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.equip", "Equip Item",    "Equipment", 1f);
            InventoryTestHelpers.RegisterItem(_registry, "item.mat",   "Material Item", "Material",  1f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.equip", 5 }, { "item.mat", 5 } });

            _panel.Refresh(station, _inventory);

            var sortBtns = _panel.Query<Button>(className: "ws-inventory-panel__sort-btn").ToList();
            Button catBtn = null;
            foreach (var b in sortBtns)
                if (b.text == "CATEGORY") { catBtn = b; break; }
            Assert.IsNotNull(catBtn);
            using (var e = ClickEvent.GetPooled()) { e.target = catBtn; catBtn.SendEvent(e); }

            // "Equipment" < "Material" alphabetically.
            var nameLabels = _panel.Query<Label>(className: "ws-inventory-panel__item-name").ToList();
            Assert.GreaterOrEqual(nameLabels.Count, 2);
            Assert.AreEqual("Equip Item", nameLabels[0].text,
                "Items whose category sorts first alphabetically must appear first.");
        }
    }

    // ── InventorySubPanelController — expanded detail row ─────────────────────

    [TestFixture]
    internal class InventorySubPanelDetailTests
    {
        private InventorySubPanelController _panel;
        private GameObject                  _registryGo;
        private ContentRegistry             _registry;
        private InventorySystem             _inventory;

        [SetUp]
        public void SetUp()
        {
            _panel      = new InventorySubPanelController();
            _registryGo = new GameObject("InventorySubPanelDetailTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _inventory  = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void ClickItemRowHeader_TogglesDetailVisible()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 } });

            _panel.Refresh(station, _inventory);

            // Detail should be hidden by default.
            var details = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-detail").ToList();
            Assert.AreEqual(1, details.Count);
            Assert.AreEqual(DisplayStyle.None, details[0].style.display.value,
                "Detail section must be hidden before the row is clicked.");

            // Click the header to expand.
            var header = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row-header").ToList();
            Assert.AreEqual(1, header.Count);
            using (var e = ClickEvent.GetPooled()) { e.target = header[0]; header[0].SendEvent(e); }

            Assert.AreEqual(DisplayStyle.Flex, details[0].style.display.value,
                "Detail section must be visible after clicking the row header.");
        }

        [Test]
        public void ClickItemRowHeader_Twice_CollapsesDetail()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 } });

            _panel.Refresh(station, _inventory);

            var header  = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row-header").ToList();
            var details = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-detail").ToList();

            // Expand then collapse.
            using (var e1 = ClickEvent.GetPooled()) { e1.target = header[0]; header[0].SendEvent(e1); }
            using (var e2 = ClickEvent.GetPooled()) { e2.target = header[0]; header[0].SendEvent(e2); }

            Assert.AreEqual(DisplayStyle.None, details[0].style.display.value,
                "Detail must collapse on second click.");
        }

        [Test]
        public void ExpandedDetail_ShowsCorrectContainerAndRoom()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 7 } });
            InventoryTestHelpers.SetRoomName(station, 0, 0, "Cargo Bay 1");

            _panel.Refresh(station, _inventory);

            // Expand the row.
            var header = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row-header").ToList();
            using (var e = ClickEvent.GetPooled()) { e.target = header[0]; header[0].SendEvent(e); }

            // Check that the detail row shows the room name.
            var detailRows = _panel.Query<VisualElement>(className: "ws-inventory-panel__detail-row").ToList();
            Assert.AreEqual(1, detailRows.Count, "One detail row for one container.");

            bool foundRoomName = false;
            foreach (var label in detailRows[0].Query<Label>().ToList())
                if (label.text == "Cargo Bay 1") { foundRoomName = true; break; }

            Assert.IsTrue(foundRoomName, "Room name 'Cargo Bay 1' must appear in the expanded detail.");
        }

        [Test]
        public void ExpandedDetail_TwoContainers_ShowsBothRooms()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            InventoryTestHelpers.AddCargoContainer(station, 0, 0,
                new Dictionary<string, int> { { "item.ore_iron", 5 } });
            InventoryTestHelpers.AddCargoContainer(station, 1, 0,
                new Dictionary<string, int> { { "item.ore_iron", 3 } });
            InventoryTestHelpers.SetRoomName(station, 0, 0, "Bay Alpha");
            InventoryTestHelpers.SetRoomName(station, 1, 0, "Bay Beta");

            _panel.Refresh(station, _inventory);

            var header = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row-header").ToList();
            using (var e = ClickEvent.GetPooled()) { e.target = header[0]; header[0].SendEvent(e); }

            var detailRows = _panel.Query<VisualElement>(className: "ws-inventory-panel__detail-row").ToList();
            Assert.AreEqual(2, detailRows.Count, "Two detail rows expected for two containers.");
        }
    }

    // ── InventorySubPanelController — OnContentsChanged live update ───────────

    [TestFixture]
    internal class InventorySubPanelLiveUpdateTests
    {
        private InventorySubPanelController _panel;
        private GameObject                  _registryGo;
        private ContentRegistry             _registry;
        private InventorySystem             _inventory;

        [SetUp]
        public void SetUp()
        {
            _panel      = new InventorySubPanelController();
            _registryGo = new GameObject("InventorySubPanelLiveUpdateTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _inventory  = new InventorySystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void OnContentsChanged_AddItem_UpdatesRowInPanel()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            var f = InventoryTestHelpers.AddCargoContainer(station, 0, 0, null);

            _panel.Refresh(station, _inventory);

            // Before adding any item, list should be empty.
            var rowsBefore = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row").ToList();
            Assert.AreEqual(0, rowsBefore.Count, "No item rows before any items added.");

            // Add an item — this fires OnContentsChanged which auto-refreshes the panel.
            _inventory.AddItemToContainer(station, f.uid, "item.ore_iron", 5);

            var rowsAfter = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row").ToList();
            Assert.AreEqual(1, rowsAfter.Count, "Panel must update when OnContentsChanged fires.");
        }

        [Test]
        public void Detach_StopsListeningToOnContentsChanged()
        {
            var station = InventoryTestHelpers.MakeStation();
            InventoryTestHelpers.RegisterItem(_registry, "item.ore_iron", "Iron Ore", "Material", 2f);
            var f = InventoryTestHelpers.AddCargoContainer(station, 0, 0, null);

            _panel.Refresh(station, _inventory);
            _panel.Detach();

            // After Detach, adding items must not trigger a panel rebuild.
            _inventory.AddItemToContainer(station, f.uid, "item.ore_iron", 5);

            var rows = _panel.Query<VisualElement>(className: "ws-inventory-panel__item-row").ToList();
            Assert.AreEqual(0, rows.Count,
                "Panel must not update after Detach() even when OnContentsChanged fires.");
        }
    }
}
