// SidePanelControllerTests.cs
// EditMode unit tests for SidePanelController (WO-UI-005).
//
// Tests cover:
//   • Tab activation / deactivation state machine
//   • Active-tab-click collapses drawer
//   • Map tab fires fullscreen and suppresses drawer
//   • Escape key collapses drawer
//   • Escape key exits map fullscreen
//   • IsMouseOverPanel with pointer in strip, in drawer, and outside both
//   • Tab switching replaces active tab
using NUnit.Framework;
using Waystation.Systems;
using Waystation.UI;
using static Waystation.UI.SidePanelController;

namespace Waystation.Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tab State Machine
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class SidePanelTabStateMachineTests
    {
        private SidePanelController _panel;

        [SetUp]
        public void SetUp()
        {
            _panel = new SidePanelController(mapSystem: null);
        }

        [Test]
        public void DefaultState_NoActiveTab_DrawerClosed()
        {
            Assert.IsNull(_panel.ActiveTab);
            Assert.IsFalse(_panel.IsDrawerOpen);
        }

        [Test]
        public void ActivateTab_Station_OpensDrawer_SetsActiveTab()
        {
            _panel.ActivateTab(Tab.Station);

            Assert.AreEqual(Tab.Station, _panel.ActiveTab);
            Assert.IsTrue(_panel.IsDrawerOpen);
        }

        [Test]
        public void ActivateTab_SameTabTwice_CollapsesDrawer()
        {
            _panel.ActivateTab(Tab.Station);
            _panel.ActivateTab(Tab.Station);

            Assert.IsNull(_panel.ActiveTab);
            Assert.IsFalse(_panel.IsDrawerOpen);
        }

        [Test]
        public void ActivateTab_DifferentTab_SwitchesActiveTab()
        {
            _panel.ActivateTab(Tab.Station);
            _panel.ActivateTab(Tab.Crew);

            Assert.AreEqual(Tab.Crew, _panel.ActiveTab);
            Assert.IsTrue(_panel.IsDrawerOpen);
        }

        [Test]
        public void CollapseDrawer_ClearsActiveTab_ClosesDrawer()
        {
            _panel.ActivateTab(Tab.Research);
            _panel.CollapseDrawer();

            Assert.IsNull(_panel.ActiveTab);
            Assert.IsFalse(_panel.IsDrawerOpen);
        }

        [TestCase(Tab.Station)]
        [TestCase(Tab.Crew)]
        [TestCase(Tab.World)]
        [TestCase(Tab.Research)]
        [TestCase(Tab.Fleet)]
        [TestCase(Tab.Settings)]
        public void ActivateTab_NonMapTab_OpensDrawer(Tab tab)
        {
            _panel.ActivateTab(tab);

            Assert.AreEqual(tab, _panel.ActiveTab);
            Assert.IsTrue(_panel.IsDrawerOpen);
        }

        [Test]
        public void OnActiveTabChanged_FiredOnActivation()
        {
            Tab? received = null;
            _panel.OnActiveTabChanged += t => received = t;

            _panel.ActivateTab(Tab.Fleet);

            Assert.AreEqual(Tab.Fleet, received);
        }

        [Test]
        public void OnActiveTabChanged_FiredWithNull_OnCollapse()
        {
            Tab? received = Tab.Station; // sentinel
            _panel.ActivateTab(Tab.Fleet);
            _panel.OnActiveTabChanged += t => received = t;

            _panel.CollapseDrawer();

            Assert.IsNull(received);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Map Tab
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class SidePanelMapTabTests
    {
        private SidePanelController _panel;
        private MapSystem            _map;

        [SetUp]
        public void SetUp()
        {
            _map   = new MapSystem();
            _panel = new SidePanelController(_map);
        }

        [Test]
        public void MapTab_DoesNotOpenDrawer()
        {
            _panel.ActivateTab(Tab.Map);

            Assert.IsFalse(_panel.IsDrawerOpen);
        }

        [Test]
        public void MapTab_SetsNoActiveTab()
        {
            _panel.ActivateTab(Tab.Map);

            Assert.IsNull(_panel.ActiveTab);
        }

        [Test]
        public void MapTab_CallsMapSystemEnterFullscreen()
        {
            _panel.ActivateTab(Tab.Map);

            Assert.IsTrue(_map.IsFullscreenActive);
        }

        [Test]
        public void MapTab_FiresOnMapFullscreenRequestedEvent()
        {
            bool fired = false;
            _panel.OnMapFullscreenRequested += () => fired = true;

            _panel.ActivateTab(Tab.Map);

            Assert.IsTrue(fired);
        }

        [Test]
        public void MapTab_CollapsesOpenDrawer()
        {
            _panel.ActivateTab(Tab.Station);   // open drawer
            Assert.IsTrue(_panel.IsDrawerOpen);

            _panel.ActivateTab(Tab.Map);       // Map tab should collapse it

            Assert.IsFalse(_panel.IsDrawerOpen);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Escape Key
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class SidePanelEscapeKeyTests
    {
        private SidePanelController _panel;
        private MapSystem            _map;

        [SetUp]
        public void SetUp()
        {
            _map   = new MapSystem();
            _panel = new SidePanelController(_map);
        }

        [Test]
        public void Escape_WithDrawerOpen_CollapsesDrawer()
        {
            _panel.ActivateTab(Tab.Crew);
            Assert.IsTrue(_panel.IsDrawerOpen);

            bool consumed = _panel.HandleEscapeKey();

            Assert.IsTrue(consumed);
            Assert.IsFalse(_panel.IsDrawerOpen);
            Assert.IsNull(_panel.ActiveTab);
        }

        [Test]
        public void Escape_WithMapFullscreenActive_ExitsFullscreen()
        {
            _panel.ActivateTab(Tab.Map);
            Assert.IsTrue(_map.IsFullscreenActive);

            bool consumed = _panel.HandleEscapeKey();

            Assert.IsTrue(consumed);
            Assert.IsFalse(_map.IsFullscreenActive);
        }

        [Test]
        public void Escape_WithNothing_ReturnsFalse()
        {
            bool consumed = _panel.HandleEscapeKey();

            Assert.IsFalse(consumed);
        }

        [Test]
        public void Escape_PrioritisesMapFullscreenOverDrawer()
        {
            // Simulate a state where both fullscreen and a drawer could be "open";
            // the implementation activates map (collapses drawer) so drawer is closed,
            // but the fullscreen flag is set.
            _panel.ActivateTab(Tab.Map);
            Assert.IsTrue(_map.IsFullscreenActive);
            Assert.IsFalse(_panel.IsDrawerOpen);

            bool consumed = _panel.HandleEscapeKey();

            Assert.IsTrue(consumed);
            Assert.IsFalse(_map.IsFullscreenActive);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsMouseOverPanel
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class SidePanelMouseOverTests
    {
        private SidePanelController _panel;

        [SetUp]
        public void SetUp()
        {
            _panel = new SidePanelController(mapSystem: null);
        }

        [Test]
        public void Default_NotMouseOver()
        {
            Assert.IsFalse(_panel.IsMouseOverPanel);
        }

        [Test]
        public void SimulatePointerEnter_ReturnsTrue()
        {
            _panel.SimulatePointerEnter();

            Assert.IsTrue(_panel.IsMouseOverPanel);
        }

        [Test]
        public void SimulatePointerLeave_AfterEnter_ReturnsFalse()
        {
            _panel.SimulatePointerEnter();
            _panel.SimulatePointerLeave();

            Assert.IsFalse(_panel.IsMouseOverPanel);
        }

        [Test]
        public void MultipleEnters_AllMustLeave_BeforeFalse()
        {
            _panel.SimulatePointerEnter();
            _panel.SimulatePointerEnter();
            _panel.SimulatePointerLeave();

            Assert.IsTrue(_panel.IsMouseOverPanel, "Still over one child panel");

            _panel.SimulatePointerLeave();

            Assert.IsFalse(_panel.IsMouseOverPanel);
        }

        [Test]
        public void ExtraLeaves_DoNotGoBelowZero()
        {
            _panel.SimulatePointerLeave();
            _panel.SimulatePointerLeave();

            Assert.IsFalse(_panel.IsMouseOverPanel);
        }
    }
}
