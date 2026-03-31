// NetworksSubPanelControllerTests.cs
// EditMode unit tests for NetworksSubPanelController (UI-009)
// and NetworkSystem.GetNetworkHealth / GetBatteryLevel.
//
// Tests cover:
//   * Overlay button active state matches UtilityNetworkManager.CurrentOverlay
//   * Clicking each overlay button calls SetOverlay with the correct argument
//   * OnOverlayChanged from an external source (e.g. Tab key) syncs button state
//   * Network health row shows correct node count after Refresh
//   * Network health row shows severed count in warning colour when severed > 0
//   * Battery meter reflects GetBatteryLevel on Refresh
//   * Refresh with null station/manager does not throw

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

    internal static class NetworksTestHelpers
    {
        public static StationState MakeStation() => new StationState("NetworksTest");

        /// <summary>Places a complete foundation of the given buildable at (col, row).</summary>
        public static FoundationInstance AddFoundation(
            StationState station, string buildableId, int col, int row)
        {
            var f = new FoundationInstance
            {
                uid        = System.Guid.NewGuid().ToString("N"),
                buildableId = buildableId,
                tileCol    = col,
                tileRow    = row,
                status     = "complete",
            };
            station.foundations[f.uid] = f;
            return f;
        }
    }

    // ── NetworkSystem.GetNetworkHealth ─────────────────────────────────────────

    [TestFixture]
    internal class GetNetworkHealthTests
    {
        private GameObject       _registryGo;
        private ContentRegistry  _registry;
        private NetworkSystem    _networks;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("GetNetworkHealthTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();

            _registry.Buildables["buildable.wire"] = new BuildableDefinition
            {
                id = "buildable.wire", networkType = "electric", nodeRole = "conduit"
            };
            _registry.Buildables["buildable.reactor"] = new BuildableDefinition
            {
                id = "buildable.reactor", networkType = "electric", nodeRole = "producer",
                outputWatts = 20f
            };
            _registry.Buildables["buildable.battery"] = new BuildableDefinition
            {
                id = "buildable.battery", networkType = "electric", nodeRole = "storage",
                storageCapacityWh = 100f
            };
            _registry.Buildables["buildable.pipe"] = new BuildableDefinition
            {
                id = "buildable.pipe", networkType = "pipe", nodeRole = "conduit"
            };
            _registry.Buildables["buildable.isolator"] = new BuildableDefinition
            {
                id = "buildable.isolator", networkType = "electric", nodeRole = "isolator"
            };

            _networks = new NetworkSystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void GetNetworkHealth_NullStation_ReturnsEmptySummary()
        {
            var summary = _networks.GetNetworkHealth(null, "electric");

            Assert.AreEqual(0,                           summary.ConnectedNodes);
            Assert.AreEqual(0,                           summary.SeveredCount);
            Assert.AreEqual(NetworkHealthStatus.Healthy, summary.Status);
        }

        [Test]
        public void GetNetworkHealth_NoNodes_ReturnsZeroAndHealthy()
        {
            var station = NetworksTestHelpers.MakeStation();
            _networks.RebuildNetworks(station);

            var summary = _networks.GetNetworkHealth(station, "electric");

            Assert.AreEqual(0,                           summary.ConnectedNodes);
            Assert.AreEqual(0,                           summary.SeveredCount);
            Assert.AreEqual(NetworkHealthStatus.Healthy, summary.Status);
        }

        [Test]
        public void GetNetworkHealth_TwoConnectedNodes_ReturnsHealthy()
        {
            var station = NetworksTestHelpers.MakeStation();
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 1, 0);
            _networks.RebuildNetworks(station);

            var summary = _networks.GetNetworkHealth(station, "electric");

            Assert.AreEqual(2,                            summary.ConnectedNodes);
            Assert.AreEqual(0,                            summary.SeveredCount);
            Assert.AreEqual(NetworkHealthStatus.Healthy,  summary.Status);
        }

        [Test]
        public void GetNetworkHealth_ClosedIsolatorBetweenNodes_CountsSeveredPair()
        {
            var station = NetworksTestHelpers.MakeStation();
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            var iso = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 1, 0);
            iso.isolatorOpen = false;   // closed → severs connection
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 2, 0);
            _networks.RebuildNetworks(station);

            var summary = _networks.GetNetworkHealth(station, "electric");

            Assert.Greater(summary.SeveredCount, 0,
                "Closed isolator must produce at least one severed pair.");
        }

        [Test]
        public void GetNetworkHealth_OpenIsolatorBetweenNodes_NoSeveredPairs()
        {
            var station = NetworksTestHelpers.MakeStation();
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            var iso = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 1, 0);
            iso.isolatorOpen = true;    // open → allows connection
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 2, 0);
            _networks.RebuildNetworks(station);

            var summary = _networks.GetNetworkHealth(station, "electric");

            Assert.AreEqual(0, summary.SeveredCount,
                "Open isolator must not produce severed pairs.");
        }

        [Test]
        public void GetNetworkHealth_SeveredCount_ShowsTwoWhenTwoIsolators()
        {
            // Wire — closed isolator — wire — closed isolator — wire
            // produces 2 severed connections (one per closed isolator).
            var station = NetworksTestHelpers.MakeStation();
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            var iso1 = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 1, 0);
            iso1.isolatorOpen = false;
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 2, 0);
            var iso2 = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 3, 0);
            iso2.isolatorOpen = false;
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 4, 0);
            _networks.RebuildNetworks(station);

            var summary = _networks.GetNetworkHealth(station, "electric");

            Assert.AreEqual(2, summary.SeveredCount,
                "Two closed isolators must produce two severed connections.");
        }
    }

    // ── NetworkSystem.GetBatteryLevel ──────────────────────────────────────────

    [TestFixture]
    internal class GetBatteryLevelTests
    {
        private GameObject       _registryGo;
        private ContentRegistry  _registry;
        private NetworkSystem    _networks;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("GetBatteryLevelTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _registry.Buildables["buildable.battery"] = new BuildableDefinition
            {
                id = "buildable.battery", networkType = "electric", nodeRole = "storage",
                storageCapacityWh = 100f
            };
            _registry.Buildables["buildable.wire"] = new BuildableDefinition
            {
                id = "buildable.wire", networkType = "electric", nodeRole = "conduit"
            };
            _networks = new NetworkSystem(_registry);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_registryGo);

        [Test]
        public void GetBatteryLevel_NullStation_ReturnsZero()
        {
            Assert.AreEqual(0f, _networks.GetBatteryLevel(null));
        }

        [Test]
        public void GetBatteryLevel_NoStorage_ReturnsZero()
        {
            var station = NetworksTestHelpers.MakeStation();
            _networks.RebuildNetworks(station);

            Assert.AreEqual(0f, _networks.GetBatteryLevel(station));
        }

        [Test]
        public void GetBatteryLevel_BatteryAt40Percent_Returns0Point4()
        {
            var station = NetworksTestHelpers.MakeStation();
            var bat = NetworksTestHelpers.AddFoundation(station, "buildable.battery", 0, 0);
            _networks.RebuildNetworks(station);

            // Directly set stored energy to 40% of capacity (100 Wh).
            bat.storedEnergy = 40f;
            // Tick to update net.storedEnergy aggregate.
            _networks.Tick(station);

            float level = _networks.GetBatteryLevel(station);
            Assert.AreEqual(0.4f, level, 0.001f, "Battery at 40 Wh out of 100 Wh capacity must return 0.4.");
        }

        [Test]
        public void GetBatteryLevel_FullBattery_Returns1()
        {
            var station = NetworksTestHelpers.MakeStation();
            var bat = NetworksTestHelpers.AddFoundation(station, "buildable.battery", 0, 0);
            bat.storedEnergy = 100f;
            _networks.RebuildNetworks(station);
            _networks.Tick(station);

            Assert.AreEqual(1f, _networks.GetBatteryLevel(station), 0.001f);
        }
    }

    // ── NetworksSubPanelController — overlay buttons ───────────────────────────

    [TestFixture]
    internal class NetworksSubPanelOverlayButtonTests
    {
        private NetworksSubPanelController _panel;
        private GameObject                 _registryGo;
        private ContentRegistry            _registry;
        private UtilityNetworkManager      _manager;

        [SetUp]
        public void SetUp()
        {
            _panel      = new NetworksSubPanelController();
            _registryGo = new GameObject("NetworksSubPanelOverlayButtonTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            var networks = new NetworkSystem(_registry);
            _manager = new UtilityNetworkManager(_registry, networks);
        }

        [TearDown]
        public void TearDown()
        {
            _panel.Detach();
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, _manager));
        }

        [Test]
        public void Refresh_NullManager_DoesNotThrow()
        {
            var station = NetworksTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }

        [Test]
        public void Refresh_OverlayOff_OffButtonActive()
        {
            var station = NetworksTestHelpers.MakeStation();
            // CurrentOverlay starts as Off.
            _panel.Refresh(station, _manager);

            var offBtn = FindOverlayButton("OFF");
            Assert.IsNotNull(offBtn, "OFF button must exist.");
            Assert.IsTrue(offBtn.ClassListContains("ws-networks-panel__overlay-btn--active"),
                "OFF button must be active when CurrentOverlay is Off.");
        }

        [Test]
        public void Refresh_OverlayElectrical_ElecButtonActive()
        {
            _manager.SetOverlay(OverlayMode.Electrical);
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            var elecBtn = FindOverlayButton("ELEC");
            Assert.IsNotNull(elecBtn, "ELEC button must exist.");
            Assert.IsTrue(elecBtn.ClassListContains("ws-networks-panel__overlay-btn--active"),
                "ELEC button must be active when CurrentOverlay is Electrical.");

            var offBtn = FindOverlayButton("OFF");
            Assert.IsFalse(offBtn.ClassListContains("ws-networks-panel__overlay-btn--active"),
                "OFF button must not be active when Electrical is selected.");
        }

        [Test]
        public void ClickElecButton_SetsOverlayToElectrical()
        {
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            var elecBtn = FindOverlayButton("ELEC");
            Assert.IsNotNull(elecBtn);

            using var evt = ClickEvent.GetPooled();
            evt.target = elecBtn;
            elecBtn.SendEvent(evt);

            Assert.AreEqual(OverlayMode.Electrical, _manager.CurrentOverlay,
                "Clicking ELEC button must set CurrentOverlay to Electrical.");
        }

        [Test]
        public void ClickPlumbButton_SetsOverlayToPlumbing()
        {
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            var btn = FindOverlayButton("PLUMB");
            Assert.IsNotNull(btn);

            using var evt = ClickEvent.GetPooled();
            evt.target = btn;
            btn.SendEvent(evt);

            Assert.AreEqual(OverlayMode.Plumbing, _manager.CurrentOverlay);
        }

        [Test]
        public void ClickDuctButton_SetsOverlayToDucting()
        {
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            var btn = FindOverlayButton("DUCT");
            Assert.IsNotNull(btn);

            using var evt = ClickEvent.GetPooled();
            evt.target = btn;
            btn.SendEvent(evt);

            Assert.AreEqual(OverlayMode.Ducting, _manager.CurrentOverlay);
        }

        [Test]
        public void ClickFuelButton_SetsOverlayToFuel()
        {
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            var btn = FindOverlayButton("FUEL");
            Assert.IsNotNull(btn);

            using var evt = ClickEvent.GetPooled();
            evt.target = btn;
            btn.SendEvent(evt);

            Assert.AreEqual(OverlayMode.Fuel, _manager.CurrentOverlay);
        }

        [Test]
        public void ClickOffButton_SetsOverlayToOff()
        {
            _manager.SetOverlay(OverlayMode.Electrical);
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            var offBtn = FindOverlayButton("OFF");
            Assert.IsNotNull(offBtn);

            using var evt = ClickEvent.GetPooled();
            evt.target = offBtn;
            offBtn.SendEvent(evt);

            Assert.AreEqual(OverlayMode.Off, _manager.CurrentOverlay);
        }

        [Test]
        public void OnOverlayChanged_ExternalTabPress_SyncsButtonActiveState()
        {
            var station = NetworksTestHelpers.MakeStation();
            _panel.Refresh(station, _manager);

            // Simulate Tab key cycling to Electrical (bypasses panel entirely).
            _manager.CycleOverlay(); // Off → Electrical

            var elecBtn = FindOverlayButton("ELEC");
            Assert.IsTrue(elecBtn.ClassListContains("ws-networks-panel__overlay-btn--active"),
                "ELEC button must be active after external CycleOverlay changes mode to Electrical.");

            var offBtn = FindOverlayButton("OFF");
            Assert.IsFalse(offBtn.ClassListContains("ws-networks-panel__overlay-btn--active"),
                "OFF button must not be active after mode changes to Electrical.");
        }

        // ── Helper ──────────────────────────────────────────────────────────────

        private Button FindOverlayButton(string label)
        {
            foreach (var btn in _panel.Query<Button>(className: "ws-networks-panel__overlay-btn").ToList())
                if (btn.text == label) return btn;
            return null;
        }
    }

    // ── NetworksSubPanelController — health rows ───────────────────────────────

    [TestFixture]
    internal class NetworksSubPanelHealthRowTests
    {
        private NetworksSubPanelController _panel;
        private GameObject                 _registryGo;
        private ContentRegistry            _registry;
        private NetworkSystem              _networks;
        private UtilityNetworkManager      _manager;

        [SetUp]
        public void SetUp()
        {
            _panel      = new NetworksSubPanelController();
            _registryGo = new GameObject("NetworksSubPanelHealthRowTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();

            _registry.Buildables["buildable.wire"] = new BuildableDefinition
            {
                id = "buildable.wire", networkType = "electric", nodeRole = "conduit"
            };
            _registry.Buildables["buildable.battery"] = new BuildableDefinition
            {
                id = "buildable.battery", networkType = "electric", nodeRole = "storage",
                storageCapacityWh = 100f
            };
            _registry.Buildables["buildable.isolator"] = new BuildableDefinition
            {
                id = "buildable.isolator", networkType = "electric", nodeRole = "isolator"
            };
            _registry.Buildables["buildable.pipe"] = new BuildableDefinition
            {
                id = "buildable.pipe", networkType = "pipe", nodeRole = "conduit"
            };

            _networks = new NetworkSystem(_registry);
            _manager  = new UtilityNetworkManager(_registry, _networks);
        }

        [TearDown]
        public void TearDown()
        {
            _panel.Detach();
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void Refresh_TwoElectricalNodes_ShowsTwoNodes()
        {
            var station = NetworksTestHelpers.MakeStation();
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 1, 0);
            _networks.RebuildNetworks(station);

            _panel.Refresh(station, _manager);

            var nodeLabel = FindHealthLabel("ws-networks-panel__health-nodes", "ELECTRICAL");
            Assert.IsNotNull(nodeLabel, "Electrical health nodes label must exist.");
            StringAssert.Contains("2", nodeLabel.text,
                "Health row must show 2 nodes for two connected electrical foundations.");
        }

        [Test]
        public void Refresh_TwoSeveredConnections_ShowsTwo()
        {
            var station = NetworksTestHelpers.MakeStation();
            // wire — closed isolator — wire — closed isolator — wire = 2 closed isolators
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            var iso1 = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 1, 0);
            iso1.isolatorOpen = false;
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 2, 0);
            var iso2 = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 3, 0);
            iso2.isolatorOpen = false;
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 4, 0);
            _networks.RebuildNetworks(station);

            _panel.Refresh(station, _manager);

            var sevLabel = FindHealthLabel("ws-networks-panel__health-severed", "ELECTRICAL");
            Assert.IsNotNull(sevLabel, "Electrical severed label must exist.");
            StringAssert.StartsWith("2", sevLabel.text,
                "Severed count must show 2 when there are two closed isolators severing connections.");
        }

        [Test]
        public void Refresh_SeveredCountNonZero_SevLabelInWarningColour()
        {
            var station = NetworksTestHelpers.MakeStation();
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 0, 0);
            var iso = NetworksTestHelpers.AddFoundation(station, "buildable.isolator", 1, 0);
            iso.isolatorOpen = false;
            NetworksTestHelpers.AddFoundation(station, "buildable.wire", 2, 0);
            _networks.RebuildNetworks(station);

            _panel.Refresh(station, _manager);

            var sevLabel = FindHealthLabel("ws-networks-panel__health-severed", "ELECTRICAL");
            Assert.IsNotNull(sevLabel);
            // Warning colour has high red channel (≥ 0.9) and low blue channel (≤ 0.3).
            Color c = sevLabel.style.color.value;
            Assert.Greater(c.r, 0.9f, "Severed count label must use warning (amber) colour.");
            Assert.Less(c.b,    0.3f, "Severed count label must use warning (amber) colour.");
        }

        [Test]
        public void Refresh_BatteryAt40Percent_MeterShows40()
        {
            var station = NetworksTestHelpers.MakeStation();
            var bat     = NetworksTestHelpers.AddFoundation(station, "buildable.battery", 0, 0);
            bat.storedEnergy = 40f;
            _networks.RebuildNetworks(station);
            _networks.Tick(station);  // propagate storedEnergy to network aggregate

            _panel.Refresh(station, _manager);

            var meter = _panel.Q<ResourceMeter>();
            Assert.IsNotNull(meter, "A ResourceMeter for battery must exist in the panel.");
            Assert.AreEqual(0.4f, meter.Value, 0.001f,
                "Battery meter must show 40% when battery is at 40 Wh out of 100 Wh.");
        }

        // ── Helper ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds a label with the given CSS class whose parent health row corresponds
        /// to <paramref name="networkDisplayName"/> (case-insensitive contains check).
        /// Returns the first match if <paramref name="networkDisplayName"/> is null.
        /// </summary>
        private Label FindHealthLabel(string cssClass, string networkDisplayName)
        {
            var rows = _panel.Query<VisualElement>(className: "ws-networks-panel__health-row").ToList();
            foreach (var row in rows)
            {
                var nameLabel = row.Q<Label>(className: "ws-networks-panel__health-name");
                if (networkDisplayName != null &&
                    (nameLabel == null ||
                     nameLabel.text.IndexOf(networkDisplayName, System.StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                var label = row.Q<Label>(className: cssClass);
                if (label != null) return label;
            }
            return null;
        }
    }
}
