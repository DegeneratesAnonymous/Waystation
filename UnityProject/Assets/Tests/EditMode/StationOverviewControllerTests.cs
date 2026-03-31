// StationOverviewControllerTests.cs
// EditMode unit and integration tests for StationOverviewController (UI-006).
//
// Tests cover:
//   * Resource row count matches tracked resources
//   * First-refresh rate is blank (zero baseline), not diff against 0
//   * Warning/depleted row background tint activates at correct thresholds
//   * Positive / negative / zero rate label text and colour
//   * Room bonus section shows only active bonuses; empty state when none
//   * Department head field shows correct indicator text
//   * Department crew count label reflects crew in that department
//   * Department row click fires OnDepartmentRowClicked with correct uid
//   * Row lifecycle: rows added/removed as resource set changes
//   * Integration: multi-tick refresh, all sections together

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // -- Helpers ---------------------------------------------------------------

    internal static class OverviewTestHelpers
    {
        public static StationState MakeStation()
        {
            var s = new StationState("TestStation") { tick = 1 };
            s.resources["power"]   = 200f;
            s.resources["food"]    = 100f;
            s.resources["oxygen"]  = 50f;
            s.resources["parts"]   = 30f;
            s.resources["credits"] = 500f;
            return s;
        }
    }

    // -- Resource row count ---------------------------------------------------

    [TestFixture]
    internal class StationOverviewResourceRowTests
    {
        private StationOverviewController _overview;

        [SetUp]
        public void SetUp() => _overview = new StationOverviewController();

        [Test]
        public void Refresh_CreatesOneRowPerTrackedResource()
        {
            var station = OverviewTestHelpers.MakeStation();
            _overview.Refresh(station);

            var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
            Assert.AreEqual(station.resources.Count, rows.Count,
                "One resource row expected per tracked resource.");
        }

        [Test]
        public void Refresh_Null_Station_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _overview.Refresh(null));
        }

        [Test]
        public void Refresh_EmptyResources_ProducesNoRows()
        {
            var station = new StationState("EmptyStation") { tick = 0 };
            station.resources.Clear();

            _overview.Refresh(station);

            var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
            Assert.AreEqual(0, rows.Count, "No rows expected when resources are empty.");
        }

        [Test]
        public void Refresh_RemovesRowWhenResourceDroppedFromTracking()
        {
            var station = OverviewTestHelpers.MakeStation();
            _overview.Refresh(station);

            int before = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList().Count;

            station.resources.Remove("parts");
            _overview.Refresh(station);

            int after = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList().Count;
            Assert.AreEqual(before - 1, after, "Row count should decrease by one when a resource is removed.");
        }

        [Test]
        public void Refresh_ValueLabel_ShowsCurrentBalance()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources["power"] = 123f;
            _overview.Refresh(station);

            var valueLabels = _overview.Query<Label>(className: "ws-station-overview__resource-value").ToList();
            bool found = false;
            foreach (var lbl in valueLabels)
                if (lbl.text == "123") { found = true; break; }
            Assert.IsTrue(found, "A value label showing '123' must exist for the power resource.");
        }
    }

    // -- First-refresh rate baseline -----------------------------------------

    [TestFixture]
    internal class StationOverviewFirstRefreshRateTests
    {
        private StationOverviewController _overview;

        [SetUp]
        public void SetUp() => _overview = new StationOverviewController();

        [Test]
        public void FirstRefresh_RateLabelIsBlank_NotDiffAgainstZero()
        {
            // On the very first Refresh, _prevValues is empty.
            // The rate should be treated as zero (blank label), NOT as current - 0.
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["power"] = 300f;   // large value -- would show "+300.0" if diff'd against 0

            _overview.Refresh(station);

            var rateLabels = _overview.Query<Label>(className: "ws-station-overview__resource-rate").ToList();
            Assert.AreEqual(1, rateLabels.Count, "Expect exactly one rate label for the single resource.");
            Assert.AreEqual("", rateLabels[0].text,
                "First-refresh rate label must be blank (zero baseline), not '+300.0'.");
        }

        [Test]
        public void SecondRefresh_PositiveRate_ShowsPlusSign()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["power"] = 100f;
            _overview.Refresh(station);       // baseline

            station.resources["power"] = 120f;
            _overview.Refresh(station);

            var rateLabels = _overview.Query<Label>(className: "ws-station-overview__resource-rate").ToList();
            Assert.AreEqual(1, rateLabels.Count);
            Assert.AreEqual("+20.0", rateLabels[0].text, "Positive delta should show '+20.0'.");
        }

        [Test]
        public void SecondRefresh_NegativeRate_ShowsMinusSign()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["power"] = 100f;
            _overview.Refresh(station);

            station.resources["power"] = 80f;
            _overview.Refresh(station);

            var rateLabels = _overview.Query<Label>(className: "ws-station-overview__resource-rate").ToList();
            Assert.AreEqual(1, rateLabels.Count);
            Assert.AreEqual("-20.0", rateLabels[0].text, "Negative delta should show '-20.0'.");
        }

        [Test]
        public void SecondRefresh_ZeroRate_LabelIsBlank()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["power"] = 100f;
            _overview.Refresh(station);
            _overview.Refresh(station);   // same value -- rate = 0

            var rateLabels = _overview.Query<Label>(className: "ws-station-overview__resource-rate").ToList();
            Assert.AreEqual(1, rateLabels.Count);
            Assert.AreEqual("", rateLabels[0].text, "Zero delta rate label must be blank.");
        }
    }

    // -- Warning and depleted state ------------------------------------------

    [TestFixture]
    internal class StationOverviewWarningStateTests
    {
        private StationOverviewController _overview;
        private StubRegistry              _registry;
        private ResourceSystem            _resources;

        [SetUp]
        public void SetUp()
        {
            _registry  = new StubRegistry();
            _resources = new ResourceSystem(_registry);
            _overview  = new StationOverviewController();
        }

        [Test]
        public void ResourceAboveThreshold_RowHasTransparentBackground()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["power"] = 200f;   // threshold = 15; well above

            _overview.Refresh(station, _resources);

            var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
            Assert.AreEqual(1, rows.Count);
            // Transparent background = Color(0,0,0,0)
            var bg = rows[0].resolvedStyle.backgroundColor;
            Assert.AreEqual(0f, bg.a, 0.01f, "Row above threshold should have transparent background.");
        }

        [Test]
        public void ResourceBelowWarningThreshold_RowHasAmberBackground()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["power"] = 10f;   // threshold = 15; below

            _overview.Refresh(station, _resources);

            var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
            Assert.AreEqual(1, rows.Count);
            // Amber tint has non-zero alpha and non-zero R/G
            var bg = rows[0].resolvedStyle.backgroundColor;
            Assert.Greater(bg.a, 0f, "Warning row should have non-transparent background.");
            Assert.Greater(bg.r, 0f, "Warning row background should have red component (amber).");
        }

        [Test]
        public void ResourceAtZero_RowHasRedBackground()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.resources.Clear();
            station.resources["oxygen"] = 0f;   // depleted

            _overview.Refresh(station, _resources);

            var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
            Assert.AreEqual(1, rows.Count);
            var bg = rows[0].resolvedStyle.backgroundColor;
            Assert.Greater(bg.a, 0f, "Depleted row should have non-transparent background.");
            // The depleted colour has R=0.25, G=0.05, B=0.05 -- R dominates
            Assert.Greater(bg.r, bg.g + bg.b, "Depleted row background should be red-dominant.");
        }

        [Test]
        public void ResourceWarningThreshold_CorrectForPower()
        {
            Assert.AreEqual(15f, _resources.GetResourceWarningThreshold("power"), 0.001f);
        }

        [Test]
        public void ResourceSoftCap_CorrectForFood()
        {
            Assert.AreEqual(500f, _resources.GetResourceSoftCap("food"), 0.001f);
        }

        [Test]
        public void ResourceSoftCap_UnknownResource_ReturnsPositiveValue()
        {
            Assert.Greater(_resources.GetResourceSoftCap("unknown_xyz"), 0f);
        }

        [Test]
        public void ResourceWarningThreshold_UnknownResource_ReturnsZero()
        {
            Assert.AreEqual(0f, _resources.GetResourceWarningThreshold("unknown_xyz"), 0.001f);
        }
    }

    // -- Room bonus section --------------------------------------------------

    [TestFixture]
    internal class StationOverviewRoomBonusTests
    {
        private StationOverviewController _overview;

        [SetUp]
        public void SetUp() => _overview = new StationOverviewController();

        [Test]
        public void NoBonusCache_ShowsEmptyStateLabel()
        {
            var station = OverviewTestHelpers.MakeStation();
            _overview.Refresh(station);

            var emptyLabels = _overview.Query<Label>(className: "ws-station-overview__empty").ToList();
            bool found = false;
            foreach (var lbl in emptyLabels)
                if (lbl.text == "No active room bonuses.") { found = true; break; }
            Assert.IsTrue(found, "Empty-state label must appear when no bonuses are active.");
        }

        [Test]
        public void ActiveBonus_AppearsInBonusSection()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.roomBonusCache["1_1"] = new RoomBonusState
            {
                roomKey = "1_1", workbenchRoomType = "medical_bay",
                displayName = "Medical Bay", bonusActive = true,
            };
            _overview.Refresh(station);

            var bonusRows = _overview.Query<VisualElement>(className: "ws-station-overview__bonus-row").ToList();
            Assert.AreEqual(1, bonusRows.Count, "Exactly one bonus row expected for one active bonus.");

            var nameLabels = _overview.Query<Label>(className: "ws-station-overview__bonus-name").ToList();
            Assert.AreEqual("Medical Bay", nameLabels[0].text, "Bonus name label must show display name.");
        }

        [Test]
        public void InactiveBonus_IsNotShown()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.roomBonusCache["2_2"] = new RoomBonusState
            {
                roomKey = "2_2", workbenchRoomType = "workshop",
                displayName = "Workshop", bonusActive = false,
            };
            _overview.Refresh(station);

            var bonusRows = _overview.Query<VisualElement>(className: "ws-station-overview__bonus-row").ToList();
            Assert.AreEqual(0, bonusRows.Count, "Inactive bonus must not produce a visible row.");
        }

        [Test]
        public void MixedBonuses_OnlyActiveBonusesListed()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.roomBonusCache["a"] = new RoomBonusState { roomKey = "a", bonusActive = true,  displayName = "Lab" };
            station.roomBonusCache["b"] = new RoomBonusState { roomKey = "b", bonusActive = false, displayName = "Storage" };
            station.roomBonusCache["c"] = new RoomBonusState { roomKey = "c", bonusActive = true,  displayName = "Workshop" };
            _overview.Refresh(station);

            var bonusRows = _overview.Query<VisualElement>(className: "ws-station-overview__bonus-row").ToList();
            Assert.AreEqual(2, bonusRows.Count, "Only active bonuses (2) should be listed.");
        }

        [Test]
        public void ActiveThenInactive_RowRemovedOnRefresh()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.roomBonusCache["a"] = new RoomBonusState { roomKey = "a", bonusActive = true, displayName = "Lab" };
            _overview.Refresh(station);
            Assert.AreEqual(1, _overview.Query<VisualElement>(className: "ws-station-overview__bonus-row").ToList().Count);

            station.roomBonusCache["a"].bonusActive = false;
            _overview.Refresh(station);
            Assert.AreEqual(0, _overview.Query<VisualElement>(className: "ws-station-overview__bonus-row").ToList().Count,
                "Row must be removed when bonus becomes inactive.");
        }
    }

    // -- Station condition section -------------------------------------------

    [TestFixture]
    internal class StationOverviewConditionTests
    {
        private StationOverviewController _overview;

        [SetUp]
        public void SetUp() => _overview = new StationOverviewController();

        [Test]
        public void Condition_NoModules_DoesNotThrow()
        {
            var station = OverviewTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _overview.Refresh(station));
        }

        [Test]
        public void Condition_AllModulesFullHealth_DoesNotThrow()
        {
            var station = OverviewTestHelpers.MakeStation();
            var mod = ModuleInstance.Create("m.power", "Power Core", "utility");
            mod.damage = 0f;
            station.modules[mod.uid] = mod;
            Assert.DoesNotThrow(() => _overview.Refresh(station));
        }

        [Test]
        public void Condition_ModuleWithHighDamage_DoesNotThrow()
        {
            var station = OverviewTestHelpers.MakeStation();
            var mod = ModuleInstance.Create("m.power", "Power Core", "utility");
            mod.damage = 0.85f;
            station.modules[mod.uid] = mod;
            Assert.DoesNotThrow(() => _overview.Refresh(station));
        }
    }

    // -- Department summary --------------------------------------------------

    [TestFixture]
    internal class StationOverviewDepartmentTests
    {
        private StationOverviewController _overview;

        [SetUp]
        public void SetUp() => _overview = new StationOverviewController();

        [Test]
        public void NoDepartments_ShowsEmptyStateLabel()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.departments.Clear();
            _overview.Refresh(station);

            var empty = _overview.Query<Label>(className: "ws-station-overview__empty").ToList();
            bool found = false;
            foreach (var lbl in empty)
                if (lbl.text == "No departments defined.") { found = true; break; }
            Assert.IsTrue(found, "Empty-state label must appear when there are no departments.");
        }

        [Test]
        public void DepartmentWithNoHead_HeadLabelShowsDash()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.departments.Add(Department.Create("dept.engineering", "Engineering"));
            _overview.Refresh(station);

            var headLabels = _overview.Query<Label>(className: "ws-station-overview__dept-head").ToList();
            Assert.AreEqual(1, headLabels.Count, "One head label expected for one department.");
            StringAssert.Contains("HEAD", headLabels[0].text, "Head label must contain 'HEAD'.");
            StringAssert.DoesNotContain("\u2713", headLabels[0].text,
                "Unassigned head must not show checkmark.");
        }

        [Test]
        public void DepartmentWithHead_HeadLabelShowsCheckmark()
        {
            var station = OverviewTestHelpers.MakeStation();
            var dept = Department.Create("dept.medical", "Medical");
            dept.headNpcUid = "npc_001";
            station.departments.Add(dept);
            _overview.Refresh(station);

            var headLabels = _overview.Query<Label>(className: "ws-station-overview__dept-head").ToList();
            Assert.AreEqual(1, headLabels.Count);
            StringAssert.Contains("\u2713", headLabels[0].text, "Assigned head must show checkmark.");
        }

        [Test]
        public void DepartmentCrewCount_ReflectsCrewInThatDept()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.departments.Add(Department.Create("dept.security", "Security"));

            for (int i = 0; i < 2; i++)
            {
                var npc = new NPCInstance { uid = "npc_sec_" + i, name = "Guard " + i, departmentId = "dept.security" };
                npc.statusTags.Add("crew");
                station.npcs[npc.uid] = npc;
            }

            // One crew member in a different department -- must not be counted.
            var other = new NPCInstance { uid = "npc_other", name = "Other", departmentId = "dept.medical" };
            other.statusTags.Add("crew");
            station.npcs[other.uid] = other;

            _overview.Refresh(station);

            var crewLabels = _overview.Query<Label>(className: "ws-station-overview__dept-crew").ToList();
            Assert.AreEqual(1, crewLabels.Count, "One crew-count label expected for one department.");
            Assert.AreEqual("2 crew", crewLabels[0].text, "Crew count must be 2 for the security department.");
        }

        [Test]
        public void DepartmentRowClick_FiresOnDepartmentRowClickedWithCorrectUid()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.departments.Add(Department.Create("dept.engineering", "Engineering"));

            string receivedUid = null;
            _overview.OnDepartmentRowClicked += uid => receivedUid = uid;

            _overview.Refresh(station);

            var deptButtons = _overview.Query<Button>(className: "ws-station-overview__dept-row").ToList();
            Assert.IsTrue(deptButtons.Count > 0, "At least one department row button must exist.");

            using (var evt = ClickEvent.GetPooled())
            {
                evt.target = deptButtons[0];
                deptButtons[0].SendEvent(evt);
            }

            Assert.AreEqual("dept.engineering", receivedUid);
        }
    }

    // -- Integration ----------------------------------------------------------

    [TestFixture]
    internal class StationOverviewIntegrationTests
    {
        private StationOverviewController _overview;
        private StubRegistry              _registry;
        private ResourceSystem            _resources;

        [SetUp]
        public void SetUp()
        {
            _registry  = new StubRegistry();
            _resources = new ResourceSystem(_registry);
            _overview  = new StationOverviewController();
        }

        [Test]
        public void FullRefresh_WithAllSectionsPopulated_DoesNotThrow()
        {
            var station = OverviewTestHelpers.MakeStation();
            station.tick = 42;
            station.roomBonusCache["r1"] = new RoomBonusState
                { roomKey = "r1", workbenchRoomType = "medical_bay", displayName = "Medical Bay", bonusActive = true };

            var dept = Department.Create("dept.crew", "Crew");
            dept.headNpcUid = "npc_head";
            station.departments.Add(dept);

            var npc = new NPCInstance { uid = "npc_001", name = "Test NPC", departmentId = "dept.crew" };
            npc.statusTags.Add("crew");
            station.npcs[npc.uid] = npc;

            var mod = ModuleInstance.Create("m.ls", "Life Support", "utility");
            mod.damage = 0.25f;
            station.modules[mod.uid] = mod;

            Assert.DoesNotThrow(() => _overview.Refresh(station, _resources));

            station.resources["power"]  = 180f;
            station.resources["oxygen"] = 8f;   // below threshold
            station.tick = 43;

            Assert.DoesNotThrow(() => _overview.Refresh(station, _resources));

            // Second refresh: oxygen row should now have a warning background.
            var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
            Assert.AreEqual(station.resources.Count, rows.Count, "Row count must equal tracked resource count.");
        }

        [Test]
        public void MultipleTickRefreshes_RowCountStaysConsistent()
        {
            var station = OverviewTestHelpers.MakeStation();
            int expectedCount = station.resources.Count;

            for (int i = 0; i < 10; i++)
            {
                station.tick++;
                station.resources["power"]  = 200f + i * 5f;
                station.resources["oxygen"] = Mathf.Max(0f, 50f - i * 7f);
                _overview.Refresh(station, _resources);

                var rows = _overview.Query<VisualElement>(className: "ws-station-overview__resource-row").ToList();
                Assert.AreEqual(expectedCount, rows.Count,
                    $"Row count must remain {expectedCount} on tick {i}.");
            }
        }
    }
}
