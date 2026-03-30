// TopBarControllerTests.cs
// EditMode unit tests for TopBarController, LogEntryBuffer, and ViewContextManager (WO-UI-004).
//
// Tests cover:
//   • Badge count reflects LogEntryBuffer.UnreadAlertCount after Add()
//   • Badge hidden when unread count is zero
//   • Badge visible and correct when unread count is non-zero
//   • Location label updates on ViewContextManager.OnContextChanged
//   • Speed button active state at each of the four states (Pause, 1×, 2×, 3×)
//   • Alert tray opens on badge click; entries sorted by urgency
//   • Alert tray closes on second badge click
//   • LogEntryBuffer.GetSortedByUrgency returns entries in urgency order
//   • ViewContextManager.SetContext does not fire when name unchanged
using NUnit.Framework;
using Waystation.Core;
using Waystation.UI;

namespace Waystation.Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // LogEntryBuffer
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class LogEntryBufferTests
    {
        [TearDown]
        public void TearDown()
        {
            LogEntryBuffer.Reset();
        }

        [Test]
        public void Add_IncreasesUnreadCount()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.Alert, "Test alert");

            Assert.AreEqual(1, buf.UnreadAlertCount);
        }

        [Test]
        public void Add_FiresOnBufferChanged()
        {
            var buf = LogEntryBuffer.Instance;
            int fired = 0;
            buf.OnBufferChanged += () => fired++;

            buf.Add(AlertCategory.Crew, "crew warning");

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void MarkAllRead_SetsUnreadToZero()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.Resource, "low power");
            buf.Add(AlertCategory.Visitors, "ship incoming");

            buf.MarkAllRead();

            Assert.AreEqual(0, buf.UnreadAlertCount);
        }

        [Test]
        public void MarkAllRead_FiresOnBufferChanged()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.Alert, "alert");
            int fired = 0;
            buf.OnBufferChanged += () => fired++;

            buf.MarkAllRead();

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.MissionDistress, "distress signal");
            buf.Clear();

            Assert.AreEqual(0, buf.UnreadAlertCount);
            Assert.AreEqual(0, buf.Entries.Count);
        }

        [Test]
        public void GetSortedByUrgency_ReturnsAlertBeforeCrew()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.Crew,  "crew msg");
            buf.Add(AlertCategory.Alert, "alert msg");

            var sorted = buf.GetSortedByUrgency();

            Assert.AreEqual(AlertCategory.Alert, sorted[0].Category);
            Assert.AreEqual(AlertCategory.Crew,  sorted[1].Category);
        }

        [Test]
        public void GetSortedByUrgency_FullOrder()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.MissionDistress, "distress");
            buf.Add(AlertCategory.Visitors,        "visitor");
            buf.Add(AlertCategory.Resource,        "resource");
            buf.Add(AlertCategory.Crew,            "crew");
            buf.Add(AlertCategory.Alert,           "alert");

            var sorted = buf.GetSortedByUrgency();

            Assert.AreEqual(AlertCategory.Alert,           sorted[0].Category);
            Assert.AreEqual(AlertCategory.Crew,            sorted[1].Category);
            Assert.AreEqual(AlertCategory.Resource,        sorted[2].Category);
            Assert.AreEqual(AlertCategory.Visitors,        sorted[3].Category);
            Assert.AreEqual(AlertCategory.MissionDistress, sorted[4].Category);
        }

        [Test]
        public void ThreeUnreadEntries_UnreadAlertCountIsThree()
        {
            var buf = LogEntryBuffer.Instance;
            buf.Add(AlertCategory.Alert,    "a1");
            buf.Add(AlertCategory.Crew,     "c1");
            buf.Add(AlertCategory.Resource, "r1");

            Assert.AreEqual(3, buf.UnreadAlertCount);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ViewContextManager
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ViewContextManagerTests
    {
        [TearDown]
        public void TearDown()
        {
            ViewContextManager.Reset();
        }

        [Test]
        public void Default_CurrentContextName_IsEmpty()
        {
            Assert.AreEqual(string.Empty, ViewContextManager.Instance.CurrentContextName);
        }

        [Test]
        public void SetContext_UpdatesCurrentContextName()
        {
            var mgr = ViewContextManager.Instance;
            mgr.SetContext("Waystation Alpha");

            Assert.AreEqual("Waystation Alpha", mgr.CurrentContextName);
        }

        [Test]
        public void SetContext_FiresOnContextChanged()
        {
            var mgr = ViewContextManager.Instance;
            string received = null;
            mgr.OnContextChanged += name => received = name;

            mgr.SetContext("Alpha");

            Assert.AreEqual("Alpha", received);
        }

        [Test]
        public void SetContext_SameName_DoesNotFireEvent()
        {
            var mgr = ViewContextManager.Instance;
            mgr.SetContext("Alpha");
            int fired = 0;
            mgr.OnContextChanged += _ => fired++;

            mgr.SetContext("Alpha"); // same name — no event

            Assert.AreEqual(0, fired);
        }

        [Test]
        public void SetContext_ChangingName_FiresEvent()
        {
            var mgr = ViewContextManager.Instance;
            mgr.SetContext("Alpha");
            string received = null;
            mgr.OnContextChanged += name => received = name;

            mgr.SetContext("Beta");

            Assert.AreEqual("Beta", received);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TopBarController — badge
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class TopBarBadgeTests
    {
        private TopBarController   _bar;
        private LogEntryBuffer     _buf;
        private ViewContextManager _ctx;
        private StubGameManager    _gm;

        [SetUp]
        public void SetUp()
        {
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();

            _bar = new TopBarController();
            _buf = LogEntryBuffer.Instance;
            _ctx = ViewContextManager.Instance;
            _gm  = new StubGameManager();

            _bar.InjectDependencies(_gm, _buf, _ctx);
        }

        [TearDown]
        public void TearDown()
        {
            _bar.Detach();
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();
        }

        [Test]
        public void Badge_HiddenWhenNoUnreadAlerts()
        {
            Assert.IsFalse(_bar.BadgeVisible, "Badge should be hidden when unread count is zero.");
        }

        [Test]
        public void Badge_VisibleAfterAddingAlert()
        {
            _buf.Add(AlertCategory.Alert, "test alert");

            Assert.IsTrue(_bar.BadgeVisible);
        }

        [Test]
        public void Badge_ShowsCorrectCount_ThreeAlerts()
        {
            _buf.Add(AlertCategory.Alert,    "a1");
            _buf.Add(AlertCategory.Crew,     "c1");
            _buf.Add(AlertCategory.Resource, "r1");

            Assert.AreEqual("3", _bar.BadgeText);
        }

        [Test]
        public void Badge_HiddenAfterMarkAllRead()
        {
            _buf.Add(AlertCategory.Alert, "a1");
            _buf.MarkAllRead();

            Assert.IsFalse(_bar.BadgeVisible);
        }

        [Test]
        public void Badge_UpdatesOnBufferChanged()
        {
            _buf.Add(AlertCategory.Crew, "c1");
            Assert.AreEqual("1", _bar.BadgeText);

            _buf.Add(AlertCategory.Resource, "r1");
            Assert.AreEqual("2", _bar.BadgeText);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TopBarController — context label
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class TopBarContextLabelTests
    {
        private TopBarController   _bar;
        private LogEntryBuffer     _buf;
        private ViewContextManager _ctx;
        private StubGameManager    _gm;

        [SetUp]
        public void SetUp()
        {
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();

            _bar = new TopBarController();
            _buf = LogEntryBuffer.Instance;
            _ctx = ViewContextManager.Instance;
            _gm  = new StubGameManager();
        }

        [TearDown]
        public void TearDown()
        {
            _bar.Detach();
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();
        }

        [Test]
        public void LocationLabel_ShowsContextNameOnInject()
        {
            _ctx.SetContext("Waystation Alpha");
            _bar.InjectDependencies(_gm, _buf, _ctx);

            Assert.AreEqual("Waystation Alpha", _bar.LocationText);
        }

        [Test]
        public void LocationLabel_UpdatesOnContextChanged()
        {
            _bar.InjectDependencies(_gm, _buf, _ctx);
            _ctx.SetContext("Waystation Alpha");

            _ctx.SetContext("Scout Ship Beta");

            Assert.AreEqual("Scout Ship Beta", _bar.LocationText);
        }

        [Test]
        public void LocationLabel_DoesNotUpdateAfterDetach()
        {
            _bar.InjectDependencies(_gm, _buf, _ctx);
            _ctx.SetContext("Alpha");
            _bar.Detach();

            _ctx.SetContext("Beta");

            Assert.AreEqual("Alpha", _bar.LocationText);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TopBarController — speed button active state
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class TopBarSpeedButtonTests
    {
        private TopBarController   _bar;
        private LogEntryBuffer     _buf;
        private ViewContextManager _ctx;
        private StubGameManager    _gm;

        [SetUp]
        public void SetUp()
        {
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();

            _bar = new TopBarController();
            _buf = LogEntryBuffer.Instance;
            _ctx = ViewContextManager.Instance;
            _gm  = new StubGameManager();
            _bar.InjectDependencies(_gm, _buf, _ctx);
        }

        [TearDown]
        public void TearDown()
        {
            _bar.Detach();
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();
        }

        [Test]
        public void PauseButton_ActiveWhenPaused()
        {
            _gm.IsPaused = true;
            _bar.RefreshSpeedButtons();

            Assert.IsTrue(_bar.PauseButtonActive);
            Assert.IsFalse(_bar.SpeedButtonActive(0));
            Assert.IsFalse(_bar.SpeedButtonActive(1));
            Assert.IsFalse(_bar.SpeedButtonActive(2));
        }

        [Test]
        public void SpeedButton1x_ActiveAt1x()
        {
            _gm.IsPaused       = false;
            _gm.SecondsPerTick = 0.5f; // 1×
            _bar.RefreshSpeedButtons();

            Assert.IsFalse(_bar.PauseButtonActive);
            Assert.IsTrue(_bar.SpeedButtonActive(0),  "1× button should be active");
            Assert.IsFalse(_bar.SpeedButtonActive(1), "2× button should not be active");
            Assert.IsFalse(_bar.SpeedButtonActive(2), "3× button should not be active");
        }

        [Test]
        public void SpeedButton2x_ActiveAt2x()
        {
            _gm.IsPaused       = false;
            _gm.SecondsPerTick = 0.25f; // 2×
            _bar.RefreshSpeedButtons();

            Assert.IsFalse(_bar.PauseButtonActive);
            Assert.IsFalse(_bar.SpeedButtonActive(0), "1× button should not be active");
            Assert.IsTrue(_bar.SpeedButtonActive(1),  "2× button should be active");
            Assert.IsFalse(_bar.SpeedButtonActive(2), "3× button should not be active");
        }

        [Test]
        public void SpeedButton3x_ActiveAt3x()
        {
            _gm.IsPaused       = false;
            _gm.SecondsPerTick = 1f / 6f; // 3×
            _bar.RefreshSpeedButtons();

            Assert.IsFalse(_bar.PauseButtonActive);
            Assert.IsFalse(_bar.SpeedButtonActive(0), "1× button should not be active");
            Assert.IsFalse(_bar.SpeedButtonActive(1), "2× button should not be active");
            Assert.IsTrue(_bar.SpeedButtonActive(2),  "3× button should be active");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TopBarController — alert tray open/close
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class TopBarAlertTrayTests
    {
        private TopBarController   _bar;
        private LogEntryBuffer     _buf;
        private ViewContextManager _ctx;
        private StubGameManager    _gm;

        [SetUp]
        public void SetUp()
        {
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();

            _bar = new TopBarController();
            _buf = LogEntryBuffer.Instance;
            _ctx = ViewContextManager.Instance;
            _gm  = new StubGameManager();
            _bar.InjectDependencies(_gm, _buf, _ctx);
        }

        [TearDown]
        public void TearDown()
        {
            _bar.Detach();
            LogEntryBuffer.Reset();
            ViewContextManager.Reset();
        }

        [Test]
        public void Tray_ClosedByDefault()
        {
            Assert.IsFalse(_bar.TrayOpen);
        }

        [Test]
        public void BadgeClick_OpensTray()
        {
            _buf.Add(AlertCategory.Alert, "alert");
            _bar.SimulateBadgeClick();

            Assert.IsTrue(_bar.TrayOpen);
        }

        [Test]
        public void BadgeClick_Twice_ClosesTray()
        {
            _buf.Add(AlertCategory.Alert, "alert");
            _bar.SimulateBadgeClick();
            _bar.SimulateBadgeClick();

            Assert.IsFalse(_bar.TrayOpen);
        }

        [Test]
        public void OpenTray_MarksAllRead()
        {
            _buf.Add(AlertCategory.Alert, "a1");
            _buf.Add(AlertCategory.Crew,  "c1");

            _bar.SimulateBadgeClick(); // open

            Assert.AreEqual(0, _buf.UnreadAlertCount);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // StubGameManager — minimal stand-in for unit tests
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal stub that satisfies TopBarController's dependency on GameManager.
    /// Exposes the same public surface as GameManager for the top-bar feature.
    /// </summary>
    internal class StubGameManager : Waystation.UI.ITopBarGameManager
    {
        public bool  IsPaused       { get; set; } = true;
        public float SecondsPerTick { get; set; } = 0.5f;

        public void SetSpeed(float ticksPerSecond)
        {
            SecondsPerTick = ticksPerSecond > 0f ? 1f / ticksPerSecond : 0.5f;
        }
    }
}
