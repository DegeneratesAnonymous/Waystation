// EventLogControllerTests.cs
// EditMode unit tests for EventLogBuffer and EventLogController (WO-UI-003).
//
// Tests cover:
//   • Priority ordering in GetCollapsedEntry: Alert surfaces before Crew
//   • Entry cap: add 201 entries, list length is 200 and oldest is removed
//   • Filter chip: SetFilter(Crew) shows only Crew entries
//   • IsMouseOverStrip returns true when pointer is over the strip
//   • IsMouseOverStrip returns false after pointer leaves the strip
//   • OnBufferChanged fires after Add
//   • GetFiltered(null) returns all entries
//   • GetCollapsedEntry returns null for empty buffer
//   • SetExpanded / toggle expand/collapse state
using NUnit.Framework;
using Waystation.UI;

namespace Waystation.Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // EventLogBuffer
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class EventLogBufferTests
    {
        [TearDown]
        public void TearDown()
        {
            EventLogBuffer.Reset();
        }

        [Test]
        public void Add_IncreasesEntryCount()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.Alert, "Alert message");

            Assert.AreEqual(1, buf.Entries.Count);
        }

        [Test]
        public void Add_FiresOnBufferChanged()
        {
            var buf = EventLogBuffer.Instance;
            int fired = 0;
            buf.OnBufferChanged += () => fired++;

            buf.Add(LogCategory.Crew, "crew warning");

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Add_201Entries_ListContainsExactly200()
        {
            var buf = EventLogBuffer.Instance;
            for (int i = 0; i < 201; i++)
                buf.Add(LogCategory.World, $"entry {i}");

            Assert.AreEqual(200, buf.Entries.Count);
        }

        [Test]
        public void Add_201Entries_OldestEntryDropped()
        {
            var buf = EventLogBuffer.Instance;
            // Entry 0 is added first (will become the oldest after 201 adds)
            buf.Add(LogCategory.World, "entry 0");
            for (int i = 1; i < 201; i++)
                buf.Add(LogCategory.World, $"entry {i}");

            // After 201 adds, the list has 200 entries.
            // The most-recent entry is "entry 200" (at index 0),
            // the oldest remaining is "entry 1" (at the tail).
            // "entry 0" should be gone.
            Assert.AreEqual(200, buf.Entries.Count);
            Assert.AreEqual("entry 200", buf.Entries[0].BodyText,   "Most recent must be first");
            Assert.AreEqual("entry 1",   buf.Entries[199].BodyText,  "Oldest remaining must be last");
        }

        [Test]
        public void GetCollapsedEntry_ReturnsNull_WhenBufferEmpty()
        {
            var buf = EventLogBuffer.Instance;

            Assert.IsNull(buf.GetCollapsedEntry());
        }

        [Test]
        public void GetCollapsedEntry_ReturnsAlertBeforeCrew()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.Crew,  "crew msg");
            buf.Add(LogCategory.Alert, "alert msg");

            var collapsed = buf.GetCollapsedEntry();

            Assert.AreEqual(LogCategory.Alert, collapsed.Category);
        }

        [Test]
        public void GetCollapsedEntry_PriorityOrder_AlertBeforeCrewBeforeStationBeforeWorld()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.World,   "world");
            buf.Add(LogCategory.Station, "station");
            buf.Add(LogCategory.Crew,    "crew");
            buf.Add(LogCategory.Alert,   "alert");

            var collapsed = buf.GetCollapsedEntry();

            Assert.AreEqual(LogCategory.Alert, collapsed.Category);
        }

        [Test]
        public void GetCollapsedEntry_NoAlert_ReturnsCrew()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.World,   "world");
            buf.Add(LogCategory.Station, "station");
            buf.Add(LogCategory.Crew,    "crew");

            var collapsed = buf.GetCollapsedEntry();

            Assert.AreEqual(LogCategory.Crew, collapsed.Category);
        }

        [Test]
        public void GetFiltered_Null_ReturnsAllEntries()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.Alert,   "a");
            buf.Add(LogCategory.Crew,    "c");
            buf.Add(LogCategory.Station, "s");

            var all = buf.GetFiltered(null);

            Assert.AreEqual(3, all.Count);
        }

        [Test]
        public void GetFiltered_CrewCategory_ReturnsOnlyCrewEntries()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.Alert, "alert");
            buf.Add(LogCategory.Crew,  "crew1");
            buf.Add(LogCategory.Crew,  "crew2");
            buf.Add(LogCategory.World, "world");

            var crewOnly = buf.GetFiltered(LogCategory.Crew);

            Assert.AreEqual(2, crewOnly.Count);
            foreach (var e in crewOnly)
                Assert.AreEqual(LogCategory.Crew, e.Category);
        }

        [Test]
        public void Clear_RemovesAllEntries_FiresOnBufferChanged()
        {
            var buf = EventLogBuffer.Instance;
            buf.Add(LogCategory.Alert, "a");
            int fired = 0;
            buf.OnBufferChanged += () => fired++;

            buf.Clear();

            Assert.AreEqual(0, buf.Entries.Count);
            Assert.AreEqual(1, fired);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EventLogController
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class EventLogControllerTests
    {
        private EventLogController _strip;

        [SetUp]
        public void SetUp()
        {
            EventLogBuffer.Reset();
            _strip = new EventLogController();
        }

        [TearDown]
        public void TearDown()
        {
            EventLogBuffer.Reset();
        }

        [Test]
        public void DefaultState_IsCollapsed()
        {
            Assert.IsFalse(_strip.IsExpanded);
        }

        [Test]
        public void Toggle_Expands_WhenCollapsed()
        {
            _strip.Toggle();

            Assert.IsTrue(_strip.IsExpanded);
        }

        [Test]
        public void Toggle_Collapses_WhenExpanded()
        {
            _strip.Toggle();
            _strip.Toggle();

            Assert.IsFalse(_strip.IsExpanded);
        }

        [Test]
        public void SetExpanded_True_SetsExpandedState()
        {
            _strip.SetExpanded(true);

            Assert.IsTrue(_strip.IsExpanded);
        }

        [Test]
        public void SetExpanded_False_SetsCollapsedState()
        {
            _strip.SetExpanded(true);
            _strip.SetExpanded(false);

            Assert.IsFalse(_strip.IsExpanded);
        }

        [Test]
        public void DefaultFilter_IsAll()
        {
            Assert.IsNull(_strip.ActiveFilter);
        }

        [Test]
        public void SetFilter_Crew_ActiveFilterIsCrew()
        {
            _strip.SetFilter(LogCategory.Crew);

            Assert.AreEqual(LogCategory.Crew, _strip.ActiveFilter);
        }

        [Test]
        public void SetFilter_Null_ActiveFilterIsNull()
        {
            _strip.SetFilter(LogCategory.Crew);
            _strip.SetFilter(null);

            Assert.IsNull(_strip.ActiveFilter);
        }

        [Test]
        public void IsMouseOverStrip_FalseByDefault()
        {
            Assert.IsFalse(_strip.IsMouseOverStrip);
        }

        [Test]
        public void IsMouseOverStrip_TrueAfterPointerEnter()
        {
            _strip.SimulatePointerEnter();

            Assert.IsTrue(_strip.IsMouseOverStrip);
        }

        [Test]
        public void IsMouseOverStrip_FalseAfterPointerLeave()
        {
            _strip.SimulatePointerEnter();
            _strip.SimulatePointerLeave();

            Assert.IsFalse(_strip.IsMouseOverStrip);
        }

        [Test]
        public void OnBufferChanged_UpdatesPreviewText()
        {
            EventLogBuffer.Instance.Add(LogCategory.Crew, "Mara Voss reached DepartureRisk");
            _strip.OnBufferChanged();

            Assert.AreEqual("Mara Voss reached DepartureRisk", _strip.PreviewText);
        }

        [Test]
        public void OnBufferChanged_PreviewShowsAlertOverCrew()
        {
            EventLogBuffer.Instance.Add(LogCategory.Crew,  "crew event");
            EventLogBuffer.Instance.Add(LogCategory.Alert, "boarding party detected");
            _strip.OnBufferChanged();

            Assert.AreEqual("boarding party detected", _strip.PreviewText);
        }

        [Test]
        public void SetFilter_Crew_VisibleEntryCountMatchesCrewOnly()
        {
            EventLogBuffer.Instance.Add(LogCategory.Alert,   "alert");
            EventLogBuffer.Instance.Add(LogCategory.Crew,    "crew1");
            EventLogBuffer.Instance.Add(LogCategory.Crew,    "crew2");
            EventLogBuffer.Instance.Add(LogCategory.Station, "station");

            _strip.SetFilter(LogCategory.Crew);
            _strip.SetExpanded(true);
            // Re-fire buffer changed to rebuild list
            _strip.OnBufferChanged();

            Assert.AreEqual(2, _strip.VisibleEntryCount);
        }

        [Test]
        public void SetFilter_All_VisibleEntryCountIsAllEntries()
        {
            EventLogBuffer.Instance.Add(LogCategory.Alert,   "alert");
            EventLogBuffer.Instance.Add(LogCategory.Crew,    "crew");
            EventLogBuffer.Instance.Add(LogCategory.Station, "station");
            EventLogBuffer.Instance.Add(LogCategory.World,   "world");

            _strip.SetFilter(null);
            _strip.SetExpanded(true);
            _strip.OnBufferChanged();

            Assert.AreEqual(4, _strip.VisibleEntryCount);
        }
    }
}
