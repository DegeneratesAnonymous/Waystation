// CrewRosterSubPanelControllerTests.cs
// EditMode unit tests for CrewRosterSubPanelController (UI-011).
//
// Tests cover:
//   * Filter application: department (via DropdownField), mood state, health state
//   * Sort ordering: by name, mood
//   * Row click fires OnCrewRowClicked with correct NPC uid
//   * Refresh with null station does not throw
//   * GetMoodState / GetHealthState classification helpers

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── Shared helpers ─────────────────────────────────────────────────────────

    internal static class CrewRosterTestHelpers
    {
        public static StationState MakeStation()
        {
            var s = new StationState("CrewRosterTest");
            s.departments.Clear();
            return s;
        }

        public static NPCInstance MakeCrewNpc(string uid, string name = "TestCrew",
                                              string departmentId = null,
                                              float moodScore = 60f,
                                              float stressScore = 60f,
                                              bool inCrisis = false,
                                              int injuries = 0)
        {
            var npc = new NPCInstance
            {
                uid          = uid,
                name         = name,
                departmentId = departmentId,
                moodScore    = moodScore,
                stressScore  = stressScore,
                inCrisis     = inCrisis,
                injuries     = injuries,
            };
            npc.statusTags.Add("crew");
            return npc;
        }

        public static Department MakeDepartment(string uid, string name)
            => Department.Create(uid, name);
    }

    // ── Null-safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewRosterNullSafetyTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_EmptyCrew_ShowsEmptyLabel()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            _panel.Refresh(station, null);

            var empties = _panel.Query<Label>(className: "ws-crew-roster-panel__empty").ToList();
            Assert.AreEqual(1, empties.Count, "Empty label should be shown when no crew.");
        }

        [Test]
        public void Refresh_WithCrew_DoesNotShowEmptyLabel()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("npc1", "Alice"));
            _panel.Refresh(station, null);

            var empties = _panel.Query<Label>(className: "ws-crew-roster-panel__empty").ToList();
            Assert.AreEqual(0, empties.Count,
                "Empty label should not appear when at least one crew member exists.");
        }
    }

    // ── Row creation ───────────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewRosterRowCreationTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void Refresh_TwoCrewMembers_CreatesTwoRows()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("npc1", "Alice"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("npc2", "Bob"));

            _panel.Refresh(station, null);

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(2, rows.Count, "One row per crew member expected.");
        }

        [Test]
        public void Refresh_NpcName_AppearsInRow()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("npc1", "Zara Vance"));

            _panel.Refresh(station, null);

            var names = _panel.Query<Label>(className: "ws-crew-roster-panel__crew-name").ToList();
            Assert.AreEqual(1, names.Count);
            Assert.AreEqual("Zara Vance", names[0].text);
        }
    }

    // ── Row click ─────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewRosterRowClickTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void RowClick_FiresOnCrewRowClicked_WithCorrectUid()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            var npc = CrewRosterTestHelpers.MakeCrewNpc("uid-alpha", "Alpha Crew");
            station.AddNpc(npc);

            _panel.Refresh(station, null);

            string receivedUid = null;
            _panel.OnCrewRowClicked += uid => receivedUid = uid;

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(1, rows.Count, "Expected one row.");

            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            Assert.IsNotNull(receivedUid, "Row click should invoke OnCrewRowClicked.");
            Assert.AreEqual("uid-alpha", receivedUid, "OnCrewRowClicked should receive the row's NPC uid.");
        }
    }

    // ── Mood state classification ─────────────────────────────────────────────

    [TestFixture]
    internal class CrewRosterMoodStateTests
    {
        [Test]
        public void GetMoodState_InCrisis_ReturnsCrisis()
        {
            var npc = CrewRosterTestHelpers.MakeCrewNpc("n1", inCrisis: true, moodScore: 10f);
            Assert.AreEqual(CrewRosterSubPanelController.MoodFilter.Crisis,
                            CrewRosterSubPanelController.GetMoodState(npc));
        }

        [Test]
        public void GetMoodState_LowMoodScoreNotCrisis_ReturnsAtRisk()
        {
            var npc = CrewRosterTestHelpers.MakeCrewNpc("n1", inCrisis: false, moodScore: 25f);
            Assert.AreEqual(CrewRosterSubPanelController.MoodFilter.AtRisk,
                            CrewRosterSubPanelController.GetMoodState(npc));
        }

        [Test]
        public void GetMoodState_HighMoodScore_ReturnsNormal()
        {
            var npc = CrewRosterTestHelpers.MakeCrewNpc("n1", inCrisis: false, moodScore: 70f);
            Assert.AreEqual(CrewRosterSubPanelController.MoodFilter.Normal,
                            CrewRosterSubPanelController.GetMoodState(npc));
        }

        [Test]
        public void GetMoodState_BaselineMoodScore_ReturnsNormal()
        {
            var npc = CrewRosterTestHelpers.MakeCrewNpc("n1", inCrisis: false, moodScore: 50f);
            Assert.AreEqual(CrewRosterSubPanelController.MoodFilter.Normal,
                            CrewRosterSubPanelController.GetMoodState(npc));
        }
    }

    // ── Health state classification ────────────────────────────────────────────

    [TestFixture]
    internal class CrewRosterHealthStateTests
    {
        [Test]
        public void GetHealthState_ZeroInjuries_ReturnsHealthy()
        {
            Assert.AreEqual(CrewRosterSubPanelController.HealthFilter.Healthy,
                            CrewRosterSubPanelController.GetHealthState(0));
        }

        [Test]
        public void GetHealthState_OneInjury_ReturnsInjured()
        {
            Assert.AreEqual(CrewRosterSubPanelController.HealthFilter.Injured,
                            CrewRosterSubPanelController.GetHealthState(1));
        }

        [Test]
        public void GetHealthState_TwoInjuries_ReturnsInjured()
        {
            Assert.AreEqual(CrewRosterSubPanelController.HealthFilter.Injured,
                            CrewRosterSubPanelController.GetHealthState(2));
        }

        [Test]
        public void GetHealthState_ThreeInjuries_ReturnsCritical()
        {
            Assert.AreEqual(CrewRosterSubPanelController.HealthFilter.Critical,
                            CrewRosterSubPanelController.GetHealthState(3));
        }

        [Test]
        public void GetHealthState_FiveInjuries_ReturnsCritical()
        {
            Assert.AreEqual(CrewRosterSubPanelController.HealthFilter.Critical,
                            CrewRosterSubPanelController.GetHealthState(5));
        }
    }

    // ── Department filter (via dropdown change) ───────────────────────────────

    [TestFixture]
    internal class CrewRosterDeptFilterTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void Refresh_MultiDept_AllRowsVisible_WhenNoDeptFilter()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            var eng  = CrewRosterTestHelpers.MakeDepartment("dept.eng", "Engineering");
            var sci  = CrewRosterTestHelpers.MakeDepartment("dept.sci", "Sciences");
            station.departments.Add(eng);
            station.departments.Add(sci);

            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", departmentId: "dept.eng"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", departmentId: "dept.sci"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n3", departmentId: null));

            _panel.Refresh(station, null);

            // Default filter = All Depts: all three rows should be in the list.
            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(3, rows.Count, "All rows should be present with no dept filter.");
        }

        [Test]
        public void DeptFilter_Engineering_ShowsOnlyEngineeringCrew()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            var eng = CrewRosterTestHelpers.MakeDepartment("dept.eng", "Engineering");
            var sci = CrewRosterTestHelpers.MakeDepartment("dept.sci", "Sciences");
            station.departments.Add(eng);
            station.departments.Add(sci);

            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "Alice", departmentId: "dept.eng"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "Bob",   departmentId: "dept.sci"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n3", "Carol", departmentId: null));

            _panel.Refresh(station, null);

            // Change the dept dropdown by name — triggers OnDeptDropdownChanged.
            var deptDropdown = _panel.Q<DropdownField>("crew-roster-dept-filter");
            Assert.IsNotNull(deptDropdown, "Expected a DropdownField named 'crew-roster-dept-filter'.");
            deptDropdown.value = "Engineering";

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(1, rows.Count, "Only Engineering crew should be shown after dept filter.");

            // The single row should belong to Alice (dept.eng).
            var nameLabel = rows[0].Q<Label>(className: "ws-crew-roster-panel__crew-name");
            Assert.AreEqual("Alice", nameLabel?.text, "The Engineering row should be Alice.");
        }

        [Test]
        public void DeptFilter_ResetToAll_ShowsAllCrew()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            var eng = CrewRosterTestHelpers.MakeDepartment("dept.eng", "Engineering");
            station.departments.Add(eng);

            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", departmentId: "dept.eng"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", departmentId: null));

            _panel.Refresh(station, null);

            var deptDropdown = _panel.Q<DropdownField>("crew-roster-dept-filter");
            deptDropdown.value = "Engineering";   // narrow
            deptDropdown.value = "All Depts";     // reset

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(2, rows.Count, "After resetting to All Depts, all rows should reappear.");
        }
    }

    // ── Mood filter (via dropdown change) ─────────────────────────────────────

    [TestFixture]
    internal class CrewRosterMoodFilterTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void Refresh_CrewWithCrisis_BothNpcsInRoster_WithNoFilter()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "Normal",   moodScore: 60f, inCrisis: false));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "InCrisis", moodScore: 10f, inCrisis: true));

            _panel.Refresh(station, null);

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(2, rows.Count, "Both crew members should appear in roster with no mood filter.");
        }

        [Test]
        public void MoodFilter_Crisis_ShowsOnlyCrisisNpc()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "Normal",   moodScore: 60f, inCrisis: false));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "InCrisis", moodScore: 10f, inCrisis: true));

            _panel.Refresh(station, null);

            // Set mood dropdown by name.
            var moodDropdown = _panel.Q<DropdownField>("crew-roster-mood-filter");
            Assert.IsNotNull(moodDropdown, "Expected a DropdownField named 'crew-roster-mood-filter'.");
            moodDropdown.value = "Crisis";

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(1, rows.Count, "Only the crisis NPC should be shown after Crisis filter.");

            var nameLabel = rows[0].Q<Label>(className: "ws-crew-roster-panel__crew-name");
            Assert.AreEqual("InCrisis", nameLabel?.text, "The remaining row should be the crisis NPC.");
        }
    }

    // ── Health filter ─────────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewRosterHealthFilterTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void Refresh_MixedInjuries_AllRowsCreated_WithNoFilter()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", injuries: 0));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", injuries: 1));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n3", injuries: 3));

            _panel.Refresh(station, null);

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(3, rows.Count, "All health states should produce rows with no filter.");
        }

        [Test]
        public void HealthFilter_Injured_ShowsOnlyInjuredCrew()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "Healthy",  injuries: 0));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "Injured",  injuries: 1));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n3", "Critical", injuries: 3));

            _panel.Refresh(station, null);

            // Set health dropdown by name.
            var healthDropdown = _panel.Q<DropdownField>("crew-roster-health-filter");
            Assert.IsNotNull(healthDropdown, "Expected a DropdownField named 'crew-roster-health-filter'.");
            healthDropdown.value = "Injured";

            var rows = _panel.Query<VisualElement>(className: "ws-crew-roster-panel__crew-row").ToList();
            Assert.AreEqual(1, rows.Count, "Only the injured NPC should be shown after Injured filter.");

            var nameLabel = rows[0].Q<Label>(className: "ws-crew-roster-panel__crew-name");
            Assert.AreEqual("Injured", nameLabel?.text, "The remaining row should be the injured NPC.");
        }
    }

    // ── Sort ordering (via sort buttons) ──────────────────────────────────────

    [TestFixture]
    internal class CrewRosterSortTests
    {
        private CrewRosterSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewRosterSubPanelController();

        [Test]
        public void SortByName_RowsOrderedAlphabetically()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "Zara"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "Alice"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n3", "Mike"));

            _panel.Refresh(station, null);

            // Click the NAME sort button.
            var sortBtns = _panel.Query<Button>(className: "ws-crew-roster-panel__sort-btn").ToList();
            Button nameBtn = null;
            foreach (var b in sortBtns)
                if (b.text == "NAME") { nameBtn = b; break; }
            Assert.IsNotNull(nameBtn, "NAME sort button must exist.");
            using (var e = ClickEvent.GetPooled()) { e.target = nameBtn; nameBtn.SendEvent(e); }

            var nameLabels = _panel.Query<Label>(className: "ws-crew-roster-panel__crew-name").ToList();
            Assert.GreaterOrEqual(nameLabels.Count, 3);
            Assert.AreEqual("Alice", nameLabels[0].text, "Alice must appear first when sorted by name.");
            Assert.AreEqual("Mike",  nameLabels[1].text);
            Assert.AreEqual("Zara",  nameLabels[2].text, "Zara must appear last when sorted by name.");
        }

        [Test]
        public void SortByMood_RowsOrderedDescending()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "LowMood",  moodScore: 20f));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "HighMood", moodScore: 90f));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n3", "MidMood",  moodScore: 55f));

            _panel.Refresh(station, null);

            // Click the MOOD sort button.
            var sortBtns = _panel.Query<Button>(className: "ws-crew-roster-panel__sort-btn").ToList();
            Button moodBtn = null;
            foreach (var b in sortBtns)
                if (b.text == "MOOD") { moodBtn = b; break; }
            Assert.IsNotNull(moodBtn, "MOOD sort button must exist.");
            using (var e = ClickEvent.GetPooled()) { e.target = moodBtn; moodBtn.SendEvent(e); }

            var nameLabels = _panel.Query<Label>(className: "ws-crew-roster-panel__crew-name").ToList();
            Assert.GreaterOrEqual(nameLabels.Count, 3);
            Assert.AreEqual("HighMood", nameLabels[0].text,
                "Highest mood score must appear first when sorted by mood (descending).");
            Assert.AreEqual("MidMood",  nameLabels[1].text);
            Assert.AreEqual("LowMood",  nameLabels[2].text);
        }

        [Test]
        public void SortByDepartment_RowsOrderedAlphabetically()
        {
            var station = CrewRosterTestHelpers.MakeStation();
            var sci = CrewRosterTestHelpers.MakeDepartment("dept.sci", "Sciences");
            var eng = CrewRosterTestHelpers.MakeDepartment("dept.eng", "Engineering");
            station.departments.Add(sci);
            station.departments.Add(eng);

            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n1", "SciNpc", departmentId: "dept.sci"));
            station.AddNpc(CrewRosterTestHelpers.MakeCrewNpc("n2", "EngNpc", departmentId: "dept.eng"));

            _panel.Refresh(station, null);

            // Click the DEPARTMENT sort button.
            var sortBtns = _panel.Query<Button>(className: "ws-crew-roster-panel__sort-btn").ToList();
            Button deptBtn = null;
            foreach (var b in sortBtns)
                if (b.text == "DEPARTMENT") { deptBtn = b; break; }
            Assert.IsNotNull(deptBtn, "DEPARTMENT sort button must exist.");
            using (var e = ClickEvent.GetPooled()) { e.target = deptBtn; deptBtn.SendEvent(e); }

            var nameLabels = _panel.Query<Label>(className: "ws-crew-roster-panel__crew-name").ToList();
            Assert.GreaterOrEqual(nameLabels.Count, 2);
            // "Engineering" < "Sciences" alphabetically → EngNpc first.
            Assert.AreEqual("EngNpc", nameLabels[0].text,
                "Engineering dept crew must appear before Sciences when sorted by department.");
        }
    }
}

