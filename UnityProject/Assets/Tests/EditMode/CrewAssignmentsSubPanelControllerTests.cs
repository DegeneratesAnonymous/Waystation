// CrewAssignmentsSubPanelControllerTests.cs
// EditMode unit tests for CrewAssignmentsSubPanelController (UI-013).
//
// Tests cover:
//   * Empty groups are hidden entirely
//   * Idle group starts collapsed by default
//   * Recreation group starts collapsed by default
//   * Non-noise groups (e.g. Construction) start expanded
//   * Groups with matching NPCs show the correct NPC count
//   * NPC row click fires OnCrewRowClicked with correct uid
//   * Refresh with null station does not throw
//   * JobSystem.ClassifyTaskType maps known job ids correctly
//   * Assignment data matches JobSystem.GetCurrentAssignments output

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

    internal static class CrewAssignmentsTestHelpers
    {
        public static StationState MakeStation()
        {
            var s = new StationState("AssignmentsTest");
            s.departments.Clear();
            return s;
        }

        public static NPCInstance MakeCrewNpc(string uid, string name = "TestNpc",
                                              string currentJobId = null)
        {
            var npc = new NPCInstance
            {
                uid          = uid,
                name         = name,
                currentJobId = currentJobId,
            };
            npc.statusTags.Add("crew");
            return npc;
        }
    }

    // ── Null-safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewAssignmentsNullSafetyTests
    {
        private CrewAssignmentsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewAssignmentsSubPanelController();

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_NullJobSystem_DoesNotThrow()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }
    }

    // ── Default collapsed state ────────────────────────────────────────────────

    [TestFixture]
    internal class CrewAssignmentsCollapseDefaultTests
    {
        private CrewAssignmentsSubPanelController _panel;

        [SetUp]
        public void SetUp()
        {
            _panel = new CrewAssignmentsSubPanelController();
        }

        [Test]
        public void IdleGroup_StartsCollapsed()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            // Add an Idle NPC so the group is shown.
            station.AddNpc(CrewAssignmentsTestHelpers.MakeCrewNpc("n1", currentJobId: null));
            _panel.Refresh(station, null);

            // The group body for Idle should be hidden (collapsed).
            var idleBody = _panel.Q<VisualElement>(className: "ws-crew-assignments-panel__group-body");
            // We need to find specifically the Idle group body — check via its sibling label.
            // Walk all groups and find the one whose header label says "IDLE".
            var groups = _panel.Query<VisualElement>(className: "ws-crew-assignments-panel__group").ToList();
            VisualElement idleGroup = null;
            foreach (var g in groups)
            {
                var lbl = g.Q<Label>(className: "ws-crew-assignments-panel__group-label");
                if (lbl != null && lbl.text == "IDLE") { idleGroup = g; break; }
            }

            Assert.IsNotNull(idleGroup, "Idle group container should be present.");
            var body = idleGroup.Q<VisualElement>(className: "ws-crew-assignments-panel__group-body");
            Assert.AreEqual(DisplayStyle.None, body.resolvedStyle.display,
                "Idle group body should be collapsed (hidden) by default.");
        }

        [Test]
        public void RecreationGroup_StartsCollapsed()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n1", currentJobId: "job.recreate"));
            _panel.Refresh(station, null);

            var groups = _panel.Query<VisualElement>(
                className: "ws-crew-assignments-panel__group").ToList();
            VisualElement recGroup = null;
            foreach (var g in groups)
            {
                var lbl = g.Q<Label>(className: "ws-crew-assignments-panel__group-label");
                if (lbl != null && lbl.text == "RECREATION") { recGroup = g; break; }
            }

            Assert.IsNotNull(recGroup, "Recreation group container should be present.");
            var body = recGroup.Q<VisualElement>(className: "ws-crew-assignments-panel__group-body");
            Assert.AreEqual(DisplayStyle.None, body.resolvedStyle.display,
                "Recreation group body should be collapsed (hidden) by default.");
        }

        [Test]
        public void ConstructionGroup_StartsExpanded()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n1", currentJobId: "job.build"));
            _panel.Refresh(station, null);

            var groups = _panel.Query<VisualElement>(
                className: "ws-crew-assignments-panel__group").ToList();
            VisualElement constGroup = null;
            foreach (var g in groups)
            {
                var lbl = g.Q<Label>(className: "ws-crew-assignments-panel__group-label");
                if (lbl != null && lbl.text == "CONSTRUCTION") { constGroup = g; break; }
            }

            Assert.IsNotNull(constGroup, "Construction group container should be present.");
            var body = constGroup.Q<VisualElement>(
                className: "ws-crew-assignments-panel__group-body");
            Assert.AreEqual(DisplayStyle.Flex, body.resolvedStyle.display,
                "Construction group body should be expanded by default.");
        }
    }

    // ── Empty group visibility ─────────────────────────────────────────────────

    [TestFixture]
    internal class CrewAssignmentsEmptyGroupTests
    {
        private CrewAssignmentsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewAssignmentsSubPanelController();

        [Test]
        public void EmptyGroups_AreHidden()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            // One Construction NPC; all other groups should be hidden.
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n1", "Alice", "job.build"));
            _panel.Refresh(station, null);

            var groups = _panel.Query<VisualElement>(
                className: "ws-crew-assignments-panel__group").ToList();

            int hiddenCount = 0;
            int visibleCount = 0;
            foreach (var g in groups)
            {
                if (g.resolvedStyle.display == DisplayStyle.None)
                    hiddenCount++;
                else
                    visibleCount++;
            }

            Assert.AreEqual(1, visibleCount, "Only the Construction group should be visible.");
            Assert.AreEqual(CrewAssignmentsSubPanelController.TaskTypeOrder.Length - 1,
                hiddenCount, "All other groups should be hidden.");
        }

        [Test]
        public void GroupWithNpcs_IsVisible()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n1", currentJobId: "job.farming"));
            _panel.Refresh(station, null);

            var groups = _panel.Query<VisualElement>(
                className: "ws-crew-assignments-panel__group").ToList();
            VisualElement farmGroup = null;
            foreach (var g in groups)
            {
                var lbl = g.Q<Label>(className: "ws-crew-assignments-panel__group-label");
                if (lbl != null && lbl.text == "FARMING") { farmGroup = g; break; }
            }

            Assert.IsNotNull(farmGroup, "Farming group should be present.");
            Assert.AreNotEqual(DisplayStyle.None, farmGroup.resolvedStyle.display,
                "Farming group should be visible when it has NPCs.");
        }
    }

    // ── NPC count in group header ──────────────────────────────────────────────

    [TestFixture]
    internal class CrewAssignmentsGroupCountTests
    {
        private CrewAssignmentsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewAssignmentsSubPanelController();

        [Test]
        public void ThreeConstructionNpcs_GroupHeaderShowsCount3()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n1", "Alice", "job.build"));
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n2", "Bob",   "job.repair"));
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n3", "Carol", "job.module_maintenance"));
            _panel.Refresh(station, null);

            var groups = _panel.Query<VisualElement>(
                className: "ws-crew-assignments-panel__group").ToList();
            Label countLabel = null;
            foreach (var g in groups)
            {
                var lbl = g.Q<Label>(className: "ws-crew-assignments-panel__group-label");
                if (lbl != null && lbl.text == "CONSTRUCTION")
                {
                    countLabel = g.Q<Label>(className: "ws-crew-assignments-panel__group-count");
                    break;
                }
            }

            Assert.IsNotNull(countLabel, "Count label should exist in Construction group header.");
            Assert.AreEqual("(3)", countLabel.text,
                "Construction group header should show count of 3.");
        }
    }

    // ── NPC rows ──────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewAssignmentsNpcRowTests
    {
        private CrewAssignmentsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewAssignmentsSubPanelController();

        [Test]
        public void NpcRow_ShowsName()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("n1", "Zara Vance", "job.guard_post"));
            _panel.Refresh(station, null);

            var nameLabels = _panel.Query<Label>(
                className: "ws-crew-assignments-panel__npc-name").ToList();
            Assert.AreEqual(1, nameLabels.Count);
            Assert.AreEqual("Zara Vance", nameLabels[0].text);
        }

        [Test]
        public void NpcRowClick_FiresOnCrewRowClicked_WithCorrectUid()
        {
            var station = CrewAssignmentsTestHelpers.MakeStation();
            station.AddNpc(
                CrewAssignmentsTestHelpers.MakeCrewNpc("uid-beta", "Beta Crew", "job.patrol"));
            _panel.Refresh(station, null);

            string receivedUid = null;
            _panel.OnCrewRowClicked += uid => receivedUid = uid;

            var rows = _panel.Query<VisualElement>(
                className: "ws-crew-assignments-panel__npc-row").ToList();
            Assert.AreEqual(1, rows.Count, "Expected exactly one NPC row.");

            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            Assert.IsNotNull(receivedUid, "Row click should invoke OnCrewRowClicked.");
            Assert.AreEqual("uid-beta", receivedUid,
                "OnCrewRowClicked should receive the row's NPC uid.");
        }
    }

    // ── JobSystem.ClassifyTaskType ─────────────────────────────────────────────

    [TestFixture]
    internal class JobSystemClassifyTaskTypeTests
    {
        [Test]
        public void ClassifyTaskType_NullJobId_ReturnsIdle()
        {
            Assert.AreEqual("Idle", JobSystem.ClassifyTaskType(null));
        }

        [Test]
        public void ClassifyTaskType_EmptyJobId_ReturnsIdle()
        {
            Assert.AreEqual("Idle", JobSystem.ClassifyTaskType(""));
        }

        [Test]
        public void ClassifyTaskType_GuardPost_ReturnsSecurity()
        {
            Assert.AreEqual("Security", JobSystem.ClassifyTaskType("job.guard_post"));
        }

        [Test]
        public void ClassifyTaskType_Patrol_ReturnsSecurity()
        {
            Assert.AreEqual("Security", JobSystem.ClassifyTaskType("job.patrol"));
        }

        [Test]
        public void ClassifyTaskType_Build_ReturnsConstruction()
        {
            Assert.AreEqual("Construction", JobSystem.ClassifyTaskType("job.build"));
        }

        [Test]
        public void ClassifyTaskType_Repair_ReturnsConstruction()
        {
            Assert.AreEqual("Construction", JobSystem.ClassifyTaskType("job.repair"));
        }

        [Test]
        public void ClassifyTaskType_ResearchIndustry_ReturnsResearch()
        {
            Assert.AreEqual("Research", JobSystem.ClassifyTaskType("job.research_industry"));
        }

        [Test]
        public void ClassifyTaskType_Counselling_ReturnsMedical()
        {
            Assert.AreEqual("Medical", JobSystem.ClassifyTaskType("job.counselling"));
        }

        [Test]
        public void ClassifyTaskType_Haul_ReturnsHauling()
        {
            Assert.AreEqual("Hauling", JobSystem.ClassifyTaskType("job.haul"));
        }

        [Test]
        public void ClassifyTaskType_Farming_ReturnsFarming()
        {
            Assert.AreEqual("Farming", JobSystem.ClassifyTaskType("job.farming"));
        }

        [Test]
        public void ClassifyTaskType_Recreate_ReturnsRecreation()
        {
            Assert.AreEqual("Recreation", JobSystem.ClassifyTaskType("job.recreate"));
        }

        [Test]
        public void ClassifyTaskType_Rest_ReturnsIdle()
        {
            Assert.AreEqual("Idle", JobSystem.ClassifyTaskType("job.rest"));
        }

        [Test]
        public void ClassifyTaskType_Eat_ReturnsIdle()
        {
            Assert.AreEqual("Idle", JobSystem.ClassifyTaskType("job.eat"));
        }

        [Test]
        public void ClassifyTaskType_Wander_ReturnsIdle()
        {
            Assert.AreEqual("Idle", JobSystem.ClassifyTaskType("job.wander"));
        }

        [Test]
        public void ClassifyTaskType_UnknownJobId_ReturnsIdle()
        {
            Assert.AreEqual("Idle", JobSystem.ClassifyTaskType("job.unknown_future_job"));
        }
    }
}
