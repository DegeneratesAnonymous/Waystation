// MapSubPanelControllerTests.cs
// EditMode unit tests for MapSubPanelController (UI-019).
//
// Tests cover:
//   * Sector discovery state colours for all three states (Uncharted, Detected, Visited)
//   * Panel construction does not throw
//   * Refresh with null station does not throw
//   * Refresh with null map system does not throw
//   * OnCloseRequested fires when close is invoked
//   * EP label shows current exploration points balance
//   * GetTopResources returns up to three most abundant resources
//   * Fullscreen enter/exit cycle: side panel collapses, OnMapFullscreenExited fires

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── Sector discovery state colour tests ───────────────────────────────────

    [TestFixture]
    internal class MapSectorStateColourTests
    {
        [Test]
        public void SectorStateColour_Uncharted_IsDimGrey()
        {
            var col = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Uncharted);

            // Uncharted should be semi-transparent (low alpha, dark grey)
            Assert.Less(col.a, 0.6f, "Uncharted should be semi-transparent.");
            Assert.Less(col.r + col.g + col.b, 1.0f, "Uncharted should be dark.");
        }

        [Test]
        public void SectorStateColour_Detected_IsDimBlue()
        {
            var col = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Detected);

            // Detected should be fully opaque and blue-biased (b >= r)
            Assert.AreEqual(1f, col.a, 0.01f, "Detected should be fully opaque.");
            Assert.GreaterOrEqual(col.b, col.r, "Detected should be blue-biased.");
        }

        [Test]
        public void SectorStateColour_Visited_IsBrightAccent()
        {
            var col = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Visited);

            // Visited should be fully opaque and brighter (higher blue/green) than Detected
            var detected = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Detected);
            Assert.AreEqual(1f, col.a, 0.01f, "Visited should be fully opaque.");
            float visitedBrightness  = col.r  + col.g  + col.b;
            float detectedBrightness = detected.r + detected.g + detected.b;
            Assert.Greater(visitedBrightness, detectedBrightness, "Visited should be brighter than Detected.");
        }

        [Test]
        public void SectorStateColour_AllThreeStates_AreDistinct()
        {
            var uncharted = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Uncharted);
            var detected  = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Detected);
            var visited   = MapSubPanelController.SectorStateColour(SectorDiscoveryState.Visited);

            Assert.AreNotEqual(uncharted, detected, "Uncharted and Detected colours must differ.");
            Assert.AreNotEqual(detected,  visited,  "Detected and Visited colours must differ.");
            Assert.AreNotEqual(uncharted, visited,  "Uncharted and Visited colours must differ.");
        }
    }

    // ── Panel construction and null-safety tests ──────────────────────────────

    [TestFixture]
    internal class MapSubPanelConstructionTests
    {
        [Test]
        public void Constructor_DoesNotThrow()
        {
            MapSubPanelController panel = null;
            Assert.DoesNotThrow(() => panel = new MapSubPanelController());
            Assert.IsNotNull(panel);
        }

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            var panel = new MapSubPanelController();
            Assert.DoesNotThrow(() => panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_NullMapSystem_DoesNotThrow()
        {
            var station = new StationState("Map-NullSys");
            var panel   = new MapSubPanelController();
            Assert.DoesNotThrow(() => panel.Refresh(station, null));
        }

        [Test]
        public void Refresh_ValidState_DoesNotThrow()
        {
            var station = new StationState("Map-Valid");
            station.explorationPoints = 50;
            var map   = new MapSystem();
            var panel = new MapSubPanelController();
            Assert.DoesNotThrow(() => panel.Refresh(station, map));
        }
    }

    // ── OnCloseRequested event ────────────────────────────────────────────────

    [TestFixture]
    internal class MapSubPanelCloseTests
    {
        [Test]
        public void OnCloseRequested_FiresWhenSimulateCloseIsCalled()
        {
            var panel = new MapSubPanelController();
            bool fired = false;
            panel.OnCloseRequested += () => fired = true;

            panel.SimulateCloseRequested();

            Assert.IsTrue(fired, "OnCloseRequested should fire when SimulateCloseRequested is called.");
        }

        [Test]
        public void OnCloseRequested_MultipleSubscribers_AllFire()
        {
            var panel = new MapSubPanelController();
            int callCount = 0;
            panel.OnCloseRequested += () => callCount++;
            panel.OnCloseRequested += () => callCount++;

            panel.SimulateCloseRequested();

            Assert.AreEqual(2, callCount, "All subscribers should be notified.");
        }

        [Test]
        public void OnCloseRequested_NoSubscribers_DoesNotThrow()
        {
            var panel = new MapSubPanelController();
            Assert.DoesNotThrow(() => panel.SimulateCloseRequested());
        }
    }

    // ── EP label ─────────────────────────────────────────────────────────────

    // ── Refresh dirty-flag tests ──────────────────────────────────────────────

    [TestFixture]
    internal class MapSubPanelRefreshDirtyFlagTests
    {
        [Test]
        public void Refresh_SameEpAndSectorCount_DoesNotThrow()
        {
            var station = new StationState("Map-Dirty");
            station.explorationPoints = 20;
            var panel = new MapSubPanelController();
            panel.Refresh(station, null);  // first call: always rebuilds

            // Second call with same data should skip full rebuild (no throw)
            Assert.DoesNotThrow(() => panel.Refresh(station, null));
        }

        [Test]
        public void Refresh_EpLabelAlwaysUpdates_EvenWithoutRebuild()
        {
            var station = new StationState("Map-EpOnly");
            station.explorationPoints = 5;
            var panel = new MapSubPanelController();
            panel.Refresh(station, null);

            // Change EP but leave sector count unchanged
            station.explorationPoints = 99;
            panel.Refresh(station, null);

            var epLabel = panel.Q<Label>(className: "ws-map-panel__ep-label");
            Assert.AreEqual("✦ 99 EP", epLabel.text,
                "EP label must always update even when sector count is unchanged (no RebuildView).");
        }
    }

    [TestFixture]
    internal class MapSubPanelEpLabelTests
    {
        [Test]
        public void Refresh_SetsEpLabel_ToStationExplorationPoints()
        {
            var station = new StationState("Map-EP");
            station.explorationPoints = 75;
            var panel = new MapSubPanelController();

            panel.Refresh(station, null);

            var epLabel = panel.Q<Label>(className: "ws-map-panel__ep-label");
            Assert.IsNotNull(epLabel, "EP label must be present in the panel hierarchy.");
            Assert.AreEqual("✦ 75 EP", epLabel.text,
                "EP label text must reflect station.explorationPoints.");
        }

        [Test]
        public void Refresh_UpdatesEpLabel_WhenEpChanges()
        {
            var station = new StationState("Map-EP2");
            station.explorationPoints = 10;
            var panel = new MapSubPanelController();
            panel.Refresh(station, null);

            station.explorationPoints = 35;
            panel.Refresh(station, null);

            var epLabel = panel.Q<Label>(className: "ws-map-panel__ep-label");
            Assert.AreEqual("✦ 35 EP", epLabel.text,
                "EP label must reflect the updated exploration points.");
        }

        [Test]
        public void Refresh_WithNullStation_SetsEpLabelToZero()
        {
            var panel = new MapSubPanelController();
            panel.Refresh(null, null);

            var epLabel = panel.Q<Label>(className: "ws-map-panel__ep-label");
            Assert.IsNotNull(epLabel);
            Assert.AreEqual("✦ 0 EP", epLabel.text);
        }
    }

    // ── GetTopResources ───────────────────────────────────────────────────────

    [TestFixture]
    internal class MapSubPanelTopResourceTests
    {
        [Test]
        public void GetTopResources_NullSector_ReturnsEmpty()
        {
            var result = MapSubPanelController.GetTopResources(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetTopResources_OreRichSector_OreIsFirst()
        {
            var sector = SectorData.Create(
                "sector_ore",
                new Vector2(25f, 54f),
                SurveyPrefix.GSC,
                new List<PhenomenonCode> { PhenomenonCode.MS, PhenomenonCode.OR },
                "Ore Test");
            sector.discoveryState = SectorDiscoveryState.Detected;

            var result = MapSubPanelController.GetTopResources(sector);

            Assert.IsNotNull(result);
            Assert.IsNotEmpty(result);
            Assert.AreEqual("Ore", result[0], "Ore should be the top resource for an OR sector.");
        }

        [Test]
        public void GetTopResources_IceRichSector_IceIsFirst()
        {
            var sector = SectorData.Create(
                "sector_ice",
                new Vector2(26f, 55f),
                SurveyPrefix.GSC,
                new List<PhenomenonCode> { PhenomenonCode.MS, PhenomenonCode.IC },
                "Ice Test");
            sector.discoveryState = SectorDiscoveryState.Detected;

            var result = MapSubPanelController.GetTopResources(sector);

            Assert.IsNotEmpty(result);
            Assert.AreEqual("Ice", result[0]);
        }

        [Test]
        public void GetTopResources_AnomalySector_AnomalyInResults()
        {
            var sector = SectorData.Create(
                "sector_anomaly",
                new Vector2(27f, 56f),
                SurveyPrefix.ANC,
                new List<PhenomenonCode> { PhenomenonCode.BH },
                "Anomaly Test");
            sector.discoveryState = SectorDiscoveryState.Detected;

            var result = MapSubPanelController.GetTopResources(sector);

            Assert.IsNotEmpty(result);
            Assert.IsTrue(result.Contains("Anomaly"),
                "BH (Black Hole) should produce an Anomaly resource entry.");
        }

        [Test]
        public void GetTopResources_ReturnsAtMostThreeItems()
        {
            var sector = SectorData.Create(
                "sector_all",
                new Vector2(28f, 57f),
                SurveyPrefix.FRN,
                new List<PhenomenonCode> { PhenomenonCode.NB, PhenomenonCode.OR, PhenomenonCode.IC },
                "All Test");
            sector.discoveryState = SectorDiscoveryState.Detected;

            var result = MapSubPanelController.GetTopResources(sector);

            Assert.LessOrEqual(result.Count, 3);
        }
    }

    // ── Fullscreen enter/exit integration cycle ───────────────────────────────

    [TestFixture]
    internal class MapFullscreenCycleTests
    {
        private SidePanelController _panel;
        private MapSystem           _map;

        [SetUp]
        public void SetUp()
        {
            _map   = new MapSystem();
            _panel = new SidePanelController(_map);
        }

        [Test]
        public void EnterFullscreen_SidePanelCollapsesDrawer()
        {
            _panel.ActivateTab(SidePanelController.Tab.Station); // open drawer
            Assert.IsTrue(_panel.IsDrawerOpen);

            _panel.ActivateTab(SidePanelController.Tab.Map);

            Assert.IsFalse(_panel.IsDrawerOpen,
                "Side panel drawer should collapse when Map fullscreen is entered.");
        }

        [Test]
        public void EnterFullscreen_SetsMapSystemFullscreenActive()
        {
            _panel.ActivateTab(SidePanelController.Tab.Map);

            Assert.IsTrue(_map.IsFullscreenActive);
        }

        [Test]
        public void ExitFullscreen_ViaEscape_ClearsMapSystemFullscreen()
        {
            _panel.ActivateTab(SidePanelController.Tab.Map);
            Assert.IsTrue(_map.IsFullscreenActive);

            _panel.HandleEscapeKey();

            Assert.IsFalse(_map.IsFullscreenActive);
        }

        [Test]
        public void ExitFullscreen_ViaEscape_FiresOnMapFullscreenExitedEvent()
        {
            _panel.ActivateTab(SidePanelController.Tab.Map);

            bool exitFired = false;
            _panel.OnMapFullscreenExited += () => exitFired = true;

            _panel.HandleEscapeKey();

            Assert.IsTrue(exitFired,
                "OnMapFullscreenExited should fire when Escape exits map fullscreen.");
        }

        [Test]
        public void ExitFullscreen_ViaEscape_PreservesNoActiveTab()
        {
            _panel.ActivateTab(SidePanelController.Tab.Map);
            _panel.HandleEscapeKey();

            Assert.IsNull(_panel.ActiveTab,
                "No tab should be active after exiting map fullscreen.");
        }

        [Test]
        public void EnterThenExitFullscreen_DrawerRemainsCollapsed()
        {
            // Enter with no previously open drawer
            _panel.ActivateTab(SidePanelController.Tab.Map);
            _panel.HandleEscapeKey();

            Assert.IsFalse(_panel.IsDrawerOpen,
                "Drawer should remain collapsed after fullscreen exit if it was not open before.");
        }

        [Test]
        public void ExitFullscreen_OnMapFullscreenExited_NotFiredIfNotFullscreen()
        {
            bool exitFired = false;
            _panel.OnMapFullscreenExited += () => exitFired = true;

            // Escape with nothing active
            _panel.HandleEscapeKey();

            Assert.IsFalse(exitFired,
                "OnMapFullscreenExited should not fire when fullscreen was not active.");
        }
    }
}
