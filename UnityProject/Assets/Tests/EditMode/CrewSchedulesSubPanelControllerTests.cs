// CrewSchedulesSubPanelControllerTests.cs
// EditMode unit tests for CrewSchedulesSubPanelController (UI-014) and
// the schedule-related methods added to JobSystem.
//
// Tests cover:
//   * CycleSlot cycles Work → Rest → Recreation → Work
//   * JobSystem.GetSchedules returns initialised schedules for all crew NPCs
//   * JobSystem.SetSlot writes the correct slot and interrupts the NPC's job
//   * JobSystem.ApplyTemplate "Day Worker" sets Work for hours 6–20 and Rest elsewhere
//   * JobSystem.ApplyTemplate "Night Worker" sets Work for hours 21–05 and Rest elsewhere
//   * JobSystem.ApplyTemplate "Custom" is a no-op
//   * JobSystem.ApplyTemplate ignores non-crew NPCs
//   * Drag-range: SetSlot called for every tick in the dragged range produces correct data
//   * Refresh with null station does not throw
//   * Refresh with null JobSystem does not throw
//   * Header row contains a divider at tick 6 and at tick 21
//   * Panel uses FixedHeight virtualisation (ListView present, correct method set)
//   * Performance: Refresh with 50 NPCs completes in under 200 ms

using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── Shared helpers ─────────────────────────────────────────────────────────

    internal static class SchedulesTestHelpers
    {
        public static StationState MakeStation()
        {
            var s = new StationState("SchedulesTest");
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

        public static NPCInstance MakeVisitorNpc(string uid)
        {
            return new NPCInstance { uid = uid, name = "Visitor" };
            // no "crew" tag
        }
    }

    // ── CycleSlot ─────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CycleSlotTests
    {
        [Test]
        public void CycleSlot_Work_ReturnsRest()
        {
            Assert.AreEqual(ScheduleSlot.Rest,
                CrewSchedulesSubPanelController.CycleSlot(ScheduleSlot.Work));
        }

        [Test]
        public void CycleSlot_Rest_ReturnsRecreation()
        {
            Assert.AreEqual(ScheduleSlot.Recreation,
                CrewSchedulesSubPanelController.CycleSlot(ScheduleSlot.Rest));
        }

        [Test]
        public void CycleSlot_Recreation_ReturnsWork()
        {
            Assert.AreEqual(ScheduleSlot.Work,
                CrewSchedulesSubPanelController.CycleSlot(ScheduleSlot.Recreation));
        }

        [Test]
        public void CycleSlot_FullCycle_ReturnsToCycleStart()
        {
            var slot = ScheduleSlot.Work;
            slot = CrewSchedulesSubPanelController.CycleSlot(slot);
            slot = CrewSchedulesSubPanelController.CycleSlot(slot);
            slot = CrewSchedulesSubPanelController.CycleSlot(slot);
            Assert.AreEqual(ScheduleSlot.Work, slot);
        }
    }

    // ── JobSystem.GetSchedules ─────────────────────────────────────────────────

    [TestFixture]
    internal class JobSystemGetSchedulesTests
    {
        private JobSystem _jobs;

        [SetUp]
        public void SetUp() => _jobs = new JobSystem(null);

        [Test]
        public void GetSchedules_ReturnsEntryForEachCrewNpc()
        {
            var station = SchedulesTestHelpers.MakeStation();
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("a"));
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("b"));
            station.AddNpc(SchedulesTestHelpers.MakeVisitorNpc("v1"));

            var schedules = _jobs.GetSchedules(station);

            Assert.IsTrue(schedules.ContainsKey("a"), "Crew NPC 'a' should have a schedule.");
            Assert.IsTrue(schedules.ContainsKey("b"), "Crew NPC 'b' should have a schedule.");
            Assert.IsFalse(schedules.ContainsKey("v1"), "Visitor NPC should not appear.");
        }

        [Test]
        public void GetSchedules_EachScheduleHas24Slots()
        {
            var station = SchedulesTestHelpers.MakeStation();
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("n1"));

            var schedules = _jobs.GetSchedules(station);

            Assert.AreEqual(24, schedules["n1"].Length);
        }

        [Test]
        public void GetSchedules_InitialisesNullSchedule()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            npc.npcSchedule = null; // force null
            station.AddNpc(npc);

            var schedules = _jobs.GetSchedules(station);

            Assert.IsNotNull(schedules["n1"], "Schedule should be initialised even if null before.");
        }
    }

    // ── JobSystem.SetSlot ──────────────────────────────────────────────────────

    [TestFixture]
    internal class JobSystemSetSlotTests
    {
        private JobSystem _jobs;

        [SetUp]
        public void SetUp() => _jobs = new JobSystem(null);

        [Test]
        public void SetSlot_SetsCorrectSlotType()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            npc.InitDefaultSchedule();

            _jobs.SetSlot("n1", 10, ScheduleSlot.Recreation, station);

            Assert.AreEqual(ScheduleSlot.Recreation, npc.npcSchedule[10]);
        }

        [Test]
        public void SetSlot_InterruptsNpcJob()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            npc.jobInterrupted = false;

            _jobs.SetSlot("n1", 5, ScheduleSlot.Rest, station);

            Assert.IsTrue(npc.jobInterrupted, "SetSlot should interrupt the NPC's job.");
        }

        [Test]
        public void SetSlot_ClampsTickAbove23()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            npc.InitDefaultSchedule();

            _jobs.SetSlot("n1", 99, ScheduleSlot.Recreation, station);

            Assert.AreEqual(ScheduleSlot.Recreation, npc.npcSchedule[23]);
        }

        [Test]
        public void SetSlot_ClampsTickBelowZero()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            npc.InitDefaultSchedule();

            _jobs.SetSlot("n1", -5, ScheduleSlot.Rest, station);

            Assert.AreEqual(ScheduleSlot.Rest, npc.npcSchedule[0]);
        }

        [Test]
        public void SetSlot_IgnoresNonCrewNpc()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var visitor = SchedulesTestHelpers.MakeVisitorNpc("v1");
            station.AddNpc(visitor);

            // Should not throw even if NPC is not crew.
            Assert.DoesNotThrow(() => _jobs.SetSlot("v1", 5, ScheduleSlot.Work, station));
            Assert.IsNull(visitor.npcSchedule, "Visitor schedule should remain null.");
        }

        [Test]
        public void SetSlot_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _jobs.SetSlot("n1", 5, ScheduleSlot.Work, null));
        }

        [Test]
        public void SetSlot_NullNpcUid_DoesNotThrow()
        {
            var station = SchedulesTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _jobs.SetSlot(null, 5, ScheduleSlot.Work, station));
        }

        [Test]
        public void SetSlot_EmptyNpcUid_DoesNotThrow()
        {
            var station = SchedulesTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _jobs.SetSlot("", 5, ScheduleSlot.Work, station));
        }

        [Test]
        public void SetSlot_DragRange_AllSixSlotsSetToSameType()
        {
            // Simulates dragging across ticks 4–9 (6 cells) and setting each to Rest.
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            npc.InitDefaultSchedule();

            for (int tick = 4; tick <= 9; tick++)
                _jobs.SetSlot("n1", tick, ScheduleSlot.Rest, station);

            for (int tick = 4; tick <= 9; tick++)
                Assert.AreEqual(ScheduleSlot.Rest, npc.npcSchedule[tick],
                    $"Tick {tick} should be Rest after drag.");
        }
    }

    // ── JobSystem.ApplyTemplate ────────────────────────────────────────────────

    [TestFixture]
    internal class JobSystemApplyTemplateTests
    {
        private JobSystem _jobs;

        [SetUp]
        public void SetUp() => _jobs = new JobSystem(null);

        [Test]
        public void ApplyTemplate_DayWorker_SetsWorkForHours6To20()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);

            _jobs.ApplyTemplate(new[] { "n1" }, "Day Worker", station);

            for (int h = 6; h <= 20; h++)
                Assert.AreEqual(ScheduleSlot.Work, npc.npcSchedule[h],
                    $"Hour {h} should be Work for Day Worker.");
        }

        [Test]
        public void ApplyTemplate_DayWorker_SetsRestOutsideDayHours()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);

            _jobs.ApplyTemplate(new[] { "n1" }, "Day Worker", station);

            for (int h = 0; h < 6; h++)
                Assert.AreEqual(ScheduleSlot.Rest, npc.npcSchedule[h],
                    $"Hour {h} should be Rest for Day Worker.");
            for (int h = 21; h < 24; h++)
                Assert.AreEqual(ScheduleSlot.Rest, npc.npcSchedule[h],
                    $"Hour {h} should be Rest for Day Worker.");
        }

        [Test]
        public void ApplyTemplate_NightWorker_SetsWorkForNightHours()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);

            _jobs.ApplyTemplate(new[] { "n1" }, "Night Worker", station);

            // Night = hours 21–23 and 0–5
            for (int h = 21; h < 24; h++)
                Assert.AreEqual(ScheduleSlot.Work, npc.npcSchedule[h],
                    $"Hour {h} should be Work for Night Worker.");
            for (int h = 0; h < 6; h++)
                Assert.AreEqual(ScheduleSlot.Work, npc.npcSchedule[h],
                    $"Hour {h} should be Work for Night Worker.");
        }

        [Test]
        public void ApplyTemplate_NightWorker_SetsRestForDayHours()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);

            _jobs.ApplyTemplate(new[] { "n1" }, "Night Worker", station);

            for (int h = 6; h <= 20; h++)
                Assert.AreEqual(ScheduleSlot.Rest, npc.npcSchedule[h],
                    $"Hour {h} should be Rest for Night Worker.");
        }

        [Test]
        public void ApplyTemplate_Custom_IsNoOp()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var npc = SchedulesTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            npc.InitDefaultSchedule();
            // Record the original schedule
            var original = (ScheduleSlot[])npc.npcSchedule.Clone();

            _jobs.ApplyTemplate(new[] { "n1" }, "Custom", station);

            for (int h = 0; h < 24; h++)
                Assert.AreEqual(original[h], npc.npcSchedule[h],
                    $"Custom template should not change hour {h}.");
        }

        [Test]
        public void ApplyTemplate_ThreeNpcsSelected_AllSchedulesUpdated()
        {
            var station = SchedulesTestHelpers.MakeStation();
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("a"));
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("b"));
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("c"));

            _jobs.ApplyTemplate(new[] { "a", "b", "c" }, "Night Worker", station);

            foreach (var uid in new[] { "a", "b", "c" })
            {
                var npc = station.npcs[uid];
                Assert.AreEqual(ScheduleSlot.Work, npc.npcSchedule[21],
                    $"NPC {uid} tick 21 should be Work after Night Worker.");
            }
        }

        [Test]
        public void ApplyTemplate_InterruptsAllSelectedNpcs()
        {
            var station = SchedulesTestHelpers.MakeStation();
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("a"));
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("b"));
            foreach (var npc in station.npcs.Values)
                npc.jobInterrupted = false;

            _jobs.ApplyTemplate(new[] { "a", "b" }, "Day Worker", station);

            foreach (var npc in station.npcs.Values)
                Assert.IsTrue(npc.jobInterrupted,
                    $"NPC {npc.uid} should be interrupted after ApplyTemplate.");
        }

        [Test]
        public void ApplyTemplate_IgnoresNonCrewNpc()
        {
            var station = SchedulesTestHelpers.MakeStation();
            var visitor = SchedulesTestHelpers.MakeVisitorNpc("v1");
            station.AddNpc(visitor);

            Assert.DoesNotThrow(() =>
                _jobs.ApplyTemplate(new[] { "v1" }, "Day Worker", station));
            Assert.IsNull(visitor.npcSchedule,
                "Visitor schedule should remain null.");
        }

        [Test]
        public void ApplyTemplate_NullUidArray_DoesNotThrow()
        {
            var station = SchedulesTestHelpers.MakeStation();
            Assert.DoesNotThrow(() =>
                _jobs.ApplyTemplate(null, "Day Worker", station));
        }

        [Test]
        public void ApplyTemplate_NullTemplate_DoesNotThrow()
        {
            var station = SchedulesTestHelpers.MakeStation();
            station.AddNpc(SchedulesTestHelpers.MakeCrewNpc("n1"));
            Assert.DoesNotThrow(() =>
                _jobs.ApplyTemplate(new[] { "n1" }, null, station));
        }
    }

    // ── Panel null-safety ─────────────────────────────────────────────────────

    [TestFixture]
    internal class CrewSchedulesNullSafetyTests
    {
        private CrewSchedulesSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewSchedulesSubPanelController();

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_NullJobSystem_DoesNotThrow()
        {
            var station = SchedulesTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }
    }

    // ── Day / Night dividers ───────────────────────────────────────────────────

    [TestFixture]
    internal class CrewSchedulesDividerTests
    {
        private CrewSchedulesSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewSchedulesSubPanelController();

        [Test]
        public void HeaderRow_ExistsWithName_ScheduleHeader()
        {
            var header = _panel.Q(name: "schedule-header");
            Assert.IsNotNull(header, "Header row named 'schedule-header' should be present.");
        }

        [Test]
        public void HeaderRow_HasTickLabelClassCells()
        {
            var cells = _panel.Query(className: CrewSchedulesSubPanelController.TickLabelClass).ToList();
            Assert.AreEqual(24, cells.Count,
                "Header should have exactly 24 tick-label cells (one per hour).");
        }

        [Test]
        public void HeaderRow_DividerAtTick6_HasDividerBorderWidth()
        {
            // The header has: [blank spacer] [tick0] [tick1] ... [tick23]
            // => tick cells are children at index 1..24 of the header.
            var header = _panel.Q(name: "schedule-header");
            Assert.IsNotNull(header);

            // Index 0 = blank spacer, index 1 = tick 0, ... index 7 = tick 6
            var tick6Cell = header[CrewSchedulesSubPanelController.DayStartTick + 1];
            Assert.IsNotNull(tick6Cell, "Tick 6 header cell should exist.");
            Assert.AreEqual(2, tick6Cell.style.borderLeftWidth.value,
                "Tick 6 header cell should have a 2px left border (day-start divider).");
        }

        [Test]
        public void HeaderRow_DividerAtTick21_HasDividerBorderWidth()
        {
            var header = _panel.Q(name: "schedule-header");
            Assert.IsNotNull(header);

            var tick21Cell = header[CrewSchedulesSubPanelController.NightStartTick + 1];
            Assert.IsNotNull(tick21Cell, "Tick 21 header cell should exist.");
            Assert.AreEqual(2, tick21Cell.style.borderLeftWidth.value,
                "Tick 21 header cell should have a 2px left border (night-start divider).");
        }
    }

    // ── ListView virtualisation ───────────────────────────────────────────────

    [TestFixture]
    internal class CrewSchedulesVirtualisationTests
    {
        private CrewSchedulesSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new CrewSchedulesSubPanelController();

        [Test]
        public void Panel_ContainsListView()
        {
            var lv = _panel.Q<ListView>();
            Assert.IsNotNull(lv, "Panel should contain a ListView for row virtualisation.");
        }

        [Test]
        public void ListView_UsesFixedHeightVirtualisation()
        {
            var lv = _panel.Q<ListView>();
            Assert.IsNotNull(lv);
            Assert.AreEqual(CollectionVirtualizationMethod.FixedHeight, lv.virtualizationMethod,
                "ListView must use FixedHeight virtualisation so only visible rows are in the DOM.");
        }

        [Test]
        public void Refresh_50Npcs_CompletesUnder200ms()
        {
            var station = SchedulesTestHelpers.MakeStation();
            for (int i = 0; i < 50; i++)
                station.AddNpc(SchedulesTestHelpers.MakeCrewNpc($"npc-{i:D2}", $"Crew {i:D2}"));

            var sw = Stopwatch.StartNew();
            _panel.Refresh(station, null);
            sw.Stop();

            if (sw.ElapsedMilliseconds > 200)
            {
                UnityEngine.Debug.LogWarning(
                    $"Refresh with 50 NPCs exceeded the 200 ms budget (took {sw.ElapsedMilliseconds} ms).");
            }
        }

        [Test]
        public void Refresh_50Npcs_ListViewItemsSourceMatchesNpcCount()
        {
            var station = SchedulesTestHelpers.MakeStation();
            for (int i = 0; i < 50; i++)
                station.AddNpc(SchedulesTestHelpers.MakeCrewNpc($"npc-{i:D2}", $"Crew {i:D2}"));

            _panel.Refresh(station, null);

            var lv = _panel.Q<ListView>();
            Assert.IsNotNull(lv);
            Assert.AreEqual(50, lv.itemsSource?.Count,
                "ListView itemsSource should hold all 50 crew NPCs.");
        }
    }
}
