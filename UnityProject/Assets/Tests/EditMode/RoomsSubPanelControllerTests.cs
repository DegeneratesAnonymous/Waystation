// RoomsSubPanelControllerTests.cs
// EditMode unit and integration tests for RoomsSubPanelController (UI-008)
// and RoomSystem.GetAllRooms / RoomSystem.OnLayoutChanged.
//
// Tests cover:
//   * Filter chip correctly shows/hides rooms by type
//   * Room row click fires OnRoomRowClicked with the correct roomId
//   * GetAllRooms returns correct room info (name, type, NPC count)
//   * OnLayoutChanged fires after RebuildBonusCache

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class RoomsTestHelpers
    {
        public static StationState MakeStation()
        {
            return new StationState("RoomsTest");
        }

        /// <summary>
        /// Registers a floor tile at (col, row), sets the canonical room key in
        /// tileToRoomKey so GetAllRooms can discover the room without a full flood-fill.
        /// </summary>
        public static void RegisterRoomTile(StationState station, int col, int row, string roomKey)
        {
            station.tileToRoomKey[$"{col}_{row}"] = roomKey;
        }

        public static NPCInstance MakeCrewNpc(string uid = null)
        {
            var npc = new NPCInstance
            {
                uid  = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "TestCrew",
            };
            npc.statusTags.Add("crew");
            return npc;
        }

        public static RoomTypeDefinition MakeRoomType(string id, string displayName)
            => new RoomTypeDefinition { id = id, displayName = displayName, isBuiltIn = true };
    }

    // ── RoomSystem.GetAllRooms ─────────────────────────────────────────────────

    [TestFixture]
    internal class GetAllRoomsTests
    {
        private RoomSystem _rooms;

        [SetUp]
        public void SetUp() => _rooms = new RoomSystem(null);

        [Test]
        public void GetAllRooms_EmptyStation_ReturnsEmptyList()
        {
            var station = RoomsTestHelpers.MakeStation();

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual(0, result.Count, "No rooms expected for a station with no tiles.");
        }

        [Test]
        public void GetAllRooms_NullStation_ReturnsEmptyList()
        {
            var result = _rooms.GetAllRooms(null);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetAllRooms_OneRoomKey_ReturnsOneEntry()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 2, 3, "2_3");

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual(1, result.Count, "One room expected.");
            Assert.AreEqual("2_3", result[0].roomKey);
        }

        [Test]
        public void GetAllRooms_MultipleTilesSameRoom_DeduplicatesEntry()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 1, 1, "1_1");
            RoomsTestHelpers.RegisterRoomTile(station, 1, 2, "1_1");
            RoomsTestHelpers.RegisterRoomTile(station, 2, 1, "1_1");

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual(1, result.Count, "Three tiles in the same room must produce only one RoomInfo.");
        }

        [Test]
        public void GetAllRooms_UnassignedRoom_HasNullTypeId()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");

            var result = _rooms.GetAllRooms(station);

            Assert.IsNull(result[0].assignedTypeId, "Unassigned room must have null assignedTypeId.");
            Assert.IsNull(result[0].typeName,        "Unassigned room must have null typeName.");
        }

        [Test]
        public void GetAllRooms_AssignedRoom_ReturnsTypeId()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            station.playerRoomTypeAssignments["0_0"] = "medical_bay";

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual("medical_bay", result[0].assignedTypeId);
        }

        [Test]
        public void GetAllRooms_CustomRoomName_UsedInDisplayName()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 1, 0, "1_0");
            station.customRoomNames["1_0"] = "Sickbay";

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual("Sickbay", result[0].displayName);
        }

        [Test]
        public void GetAllRooms_NoCustomName_FallsBackToRoomKey()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 5, 7, "5_7");

            var result = _rooms.GetAllRooms(station);

            StringAssert.Contains("5_7", result[0].displayName,
                "Fallback display name must include the room key.");
        }

        [Test]
        public void GetAllRooms_NpcInRoom_IncrementsNpcCount()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 3, 3, "3_3");

            var npc = RoomsTestHelpers.MakeCrewNpc();
            npc.location = "3_3";
            station.npcs[npc.uid] = npc;

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual(1, result[0].npcCount, "NPC located on a room tile must be counted.");
        }

        [Test]
        public void GetAllRooms_NpcOutsideAllRooms_NotCounted()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");

            var npc = RoomsTestHelpers.MakeCrewNpc();
            npc.location = "99_99";   // not mapped
            station.npcs[npc.uid] = npc;

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual(0, result[0].npcCount, "NPC outside all room tiles must not be counted.");
        }

        [Test]
        public void GetAllRooms_TypeNameFromCustomRoomTypes()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            station.playerRoomTypeAssignments["0_0"] = "custom_lab";
            station.customRoomTypes.Add(new RoomTypeDefinition
            {
                id          = "custom_lab",
                displayName = "Research Lab",
                isBuiltIn   = false,
            });

            var result = _rooms.GetAllRooms(station);

            Assert.AreEqual("Research Lab", result[0].typeName);
        }
    }

    // ── RoomSystem.OnLayoutChanged ─────────────────────────────────────────────

    [TestFixture]
    internal class OnLayoutChangedTests
    {
        private RoomSystem _rooms;

        [SetUp]
        public void SetUp() => _rooms = new RoomSystem(null);

        [Test]
        public void OnLayoutChanged_FiredAfterRebuildBonusCache()
        {
            var station = RoomsTestHelpers.MakeStation();
            bool fired = false;
            _rooms.OnLayoutChanged += () => fired = true;

            _rooms.RebuildBonusCache(station);

            Assert.IsTrue(fired, "OnLayoutChanged must fire after RebuildBonusCache completes.");
        }

        [Test]
        public void OnLayoutChanged_FiredOnAssignRoomType()
        {
            var station = RoomsTestHelpers.MakeStation();
            int count = 0;
            _rooms.OnLayoutChanged += () => count++;

            _rooms.AssignRoomType(station, "0_0", "medical_bay");

            Assert.AreEqual(1, count, "OnLayoutChanged must fire once when a room type is assigned.");
        }
    }

    // ── RoomsSubPanelController — filter chips ─────────────────────────────────

    [TestFixture]
    internal class RoomsSubPanelFilterTests
    {
        private RoomsSubPanelController _panel;
        private RoomSystem              _rooms;

        [SetUp]
        public void SetUp()
        {
            _panel = new RoomsSubPanelController();
            _rooms = new RoomSystem(null);
        }

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, _rooms, null));
        }

        [Test]
        public void Refresh_NullRooms_DoesNotThrow()
        {
            var station = RoomsTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null, null));
        }

        [Test]
        public void Refresh_NoRooms_ShowsAllChipOnly()
        {
            var station = RoomsTestHelpers.MakeStation();
            _panel.Refresh(station, _rooms, null);

            var chips = _panel.Query<Button>(className: "ws-rooms-panel__filter-btn").ToList();
            Assert.AreEqual(1, chips.Count, "Only the 'ALL' chip should appear when there are no rooms.");
            StringAssert.AreEqualIgnoringCase("ALL", chips[0].text);
        }

        [Test]
        public void Refresh_AssignedRooms_CreatesOneChipPerType()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            RoomsTestHelpers.RegisterRoomTile(station, 5, 0, "5_0");
            station.playerRoomTypeAssignments["0_0"] = "medical_bay";
            station.playerRoomTypeAssignments["5_0"] = "engineering_workshop";

            _panel.Refresh(station, _rooms, null);

            var chips = _panel.Query<Button>(className: "ws-rooms-panel__filter-btn").ToList();
            // ALL + medical_bay + engineering_workshop = 3
            Assert.AreEqual(3, chips.Count, "ALL chip plus one chip per distinct assigned type expected.");
        }

        [Test]
        public void Refresh_DuplicateType_OnlyOneExtraChip()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            RoomsTestHelpers.RegisterRoomTile(station, 1, 0, "1_0");
            station.playerRoomTypeAssignments["0_0"] = "medical_bay";
            station.playerRoomTypeAssignments["1_0"] = "medical_bay";

            _panel.Refresh(station, _rooms, null);

            var chips = _panel.Query<Button>(className: "ws-rooms-panel__filter-btn").ToList();
            // ALL + medical_bay (deduplicated) = 2
            Assert.AreEqual(2, chips.Count, "Duplicate type must produce only one extra chip.");
        }

        [Test]
        public void FilterByType_HidesNonMatchingRows()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            RoomsTestHelpers.RegisterRoomTile(station, 1, 0, "1_0");
            station.playerRoomTypeAssignments["0_0"] = "medical_bay";
            // 1_0 is unassigned

            _panel.Refresh(station, _rooms, null);

            // Click the medical_bay chip (second chip after ALL).
            var chips = _panel.Query<Button>(className: "ws-rooms-panel__filter-btn").ToList();
            Button medChip = null;
            foreach (var c in chips)
                if (c.text.ToUpper().Contains("MEDICAL")) { medChip = c; break; }

            Assert.IsNotNull(medChip, "A chip for medical_bay must exist.");

            // Simulate click via the panel's internal click registration.
            using var evt = ClickEvent.GetPooled();
            evt.target = medChip;
            medChip.SendEvent(evt);

            // Count visible room rows.
            var rows = _panel.Query<VisualElement>(className: "ws-rooms-panel__room-row").ToList();
            int visibleCount = 0;
            foreach (var r in rows)
                if (r.style.display != DisplayStyle.None) visibleCount++;

            Assert.AreEqual(1, visibleCount,
                "After selecting the Medical chip, only the medical room should be visible.");
        }

        [Test]
        public void FilterByAll_ShowsAllRows()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            RoomsTestHelpers.RegisterRoomTile(station, 1, 0, "1_0");
            station.playerRoomTypeAssignments["0_0"] = "medical_bay";

            _panel.Refresh(station, _rooms, null);

            // Click the medical chip first to narrow the filter.
            var chips = _panel.Query<Button>(className: "ws-rooms-panel__filter-btn").ToList();
            Button medChip = null;
            Button allChip = null;
            foreach (var c in chips)
            {
                if (c.text.ToUpper().Contains("MEDICAL")) medChip = c;
                if (c.text.ToUpper() == "ALL") allChip = c;
            }
            Assert.IsNotNull(medChip);
            Assert.IsNotNull(allChip);

            using (var clickMed = ClickEvent.GetPooled())
            {
                clickMed.target = medChip;
                medChip.SendEvent(clickMed);
            }

            // Then click ALL to restore.
            using (var clickAll = ClickEvent.GetPooled())
            {
                clickAll.target = allChip;
                allChip.SendEvent(clickAll);
            }

            var rows = _panel.Query<VisualElement>(className: "ws-rooms-panel__room-row").ToList();
            int visibleCount = 0;
            foreach (var r in rows)
                if (r.style.display != DisplayStyle.None) visibleCount++;

            Assert.AreEqual(2, visibleCount,
                "After clicking ALL, both rooms should be visible again.");
        }
    }

    // ── RoomsSubPanelController — room row click ───────────────────────────────

    [TestFixture]
    internal class RoomsSubPanelRowClickTests
    {
        private RoomsSubPanelController _panel;
        private RoomSystem              _rooms;

        [SetUp]
        public void SetUp()
        {
            _panel = new RoomsSubPanelController();
            _rooms = new RoomSystem(null);
        }

        [Test]
        public void ClickRoomRow_FiresOnRoomRowClickedWithCorrectId()
        {
            var station = RoomsTestHelpers.MakeStation();
            const string expectedKey = "2_4";
            RoomsTestHelpers.RegisterRoomTile(station, 2, 4, expectedKey);

            string receivedId = null;
            _panel.OnRoomRowClicked += id => receivedId = id;

            _panel.Refresh(station, _rooms, null);

            // Find and click the room row.
            var rows = _panel.Query<VisualElement>(className: "ws-rooms-panel__room-row").ToList();
            Assert.AreEqual(1, rows.Count, "Exactly one room row expected.");

            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            Assert.AreEqual(expectedKey, receivedId,
                "OnRoomRowClicked must be fired with the correct roomId.");
        }

        [Test]
        public void ClickRoomRow_MultipleRooms_FiresCorrectId()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            RoomsTestHelpers.RegisterRoomTile(station, 3, 3, "3_3");

            var received = new List<string>();
            _panel.OnRoomRowClicked += id => received.Add(id);

            _panel.Refresh(station, _rooms, null);

            var rows = _panel.Query<VisualElement>(className: "ws-rooms-panel__room-row").ToList();
            Assert.AreEqual(2, rows.Count, "Two room rows expected.");

            // Click the first row.
            using var evt1 = ClickEvent.GetPooled();
            evt1.target = rows[0];
            rows[0].SendEvent(evt1);

            // Click the second row.
            using var evt2 = ClickEvent.GetPooled();
            evt2.target = rows[1];
            rows[1].SendEvent(evt2);

            Assert.AreEqual(2, received.Count, "Each click must fire exactly one event.");
            CollectionAssert.AreEquivalent(new[] { "0_0", "3_3" }, received,
                "Both room ids must appear in the fired events.");
        }
    }

    // ── RoomsSubPanelController — room row content ────────────────────────────

    [TestFixture]
    internal class RoomsSubPanelRowContentTests
    {
        private RoomsSubPanelController _panel;
        private RoomSystem              _rooms;

        [SetUp]
        public void SetUp()
        {
            _panel = new RoomsSubPanelController();
            _rooms = new RoomSystem(null);
        }

        [Test]
        public void Refresh_RoomWithCustomName_ShowsCustomName()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            station.customRoomNames["0_0"] = "Infirmary";

            _panel.Refresh(station, _rooms, null);

            var nameLabels = _panel.Query<Label>(className: "ws-rooms-panel__room-name").ToList();
            Assert.AreEqual(1, nameLabels.Count);
            Assert.AreEqual("Infirmary", nameLabels[0].text);
        }

        [Test]
        public void Refresh_UnassignedRoom_BadgeShowsUnassigned()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");

            _panel.Refresh(station, _rooms, null);

            var badges = _panel.Query<Label>(className: "ws-rooms-panel__room-badge").ToList();
            Assert.AreEqual(1, badges.Count);
            StringAssert.AreEqualIgnoringCase("UNASSIGNED", badges[0].text);
        }

        [Test]
        public void Refresh_AssignedRoom_BadgeShowsTypeName()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            station.playerRoomTypeAssignments["0_0"] = "medical_bay";
            station.customRoomTypes.Add(new RoomTypeDefinition
            {
                id          = "medical_bay",
                displayName = "Medical Bay",
                isBuiltIn   = false,
            });

            _panel.Refresh(station, _rooms, null);

            var badges = _panel.Query<Label>(className: "ws-rooms-panel__room-badge").ToList();
            Assert.AreEqual(1, badges.Count);
            StringAssert.AreEqualIgnoringCase("MEDICAL BAY", badges[0].text);
        }

        [Test]
        public void Refresh_MultipleRooms_CreatesOneRowEach()
        {
            var station = RoomsTestHelpers.MakeStation();
            RoomsTestHelpers.RegisterRoomTile(station, 0, 0, "0_0");
            RoomsTestHelpers.RegisterRoomTile(station, 1, 0, "1_0");
            RoomsTestHelpers.RegisterRoomTile(station, 2, 0, "2_0");

            _panel.Refresh(station, _rooms, null);

            var rows = _panel.Query<VisualElement>(className: "ws-rooms-panel__room-row").ToList();
            Assert.AreEqual(3, rows.Count, "One row per room expected.");
        }
    }
}
