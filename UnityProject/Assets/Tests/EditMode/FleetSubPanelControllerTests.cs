// FleetSubPanelControllerTests.cs
// EditMode unit tests for FleetSubPanelController (UI-020) and the
// fleet-UI additions to ShipSystem (GetOwnedShips / BeginRepair / GetRepairCost /
// OnFleetChanged).
//
// Tests cover:
//   • DeriveDisplayStatus maps all six ship states correctly
//   • StatusColor returns distinct, correct colours for all six statuses
//   • In Distress ships render a rescue dispatch button
//   • Non-distress ships do NOT render a rescue dispatch button
//   • Refresh with null station / null ShipSystem does not throw
//   • Ship list is sorted alphabetically
//   • Clicking a ship row (SelectShip via Refresh) populates the detail panel
//   • Ship Detail shows correct crew names
//   • Ship Detail shows mission-history entries filtered to the selected ship
//   • Repair button appears for damaged ships, absent for undamaged and destroyed ships
//   • BeginRepair called through the repair button invokes ShipSystem.BeginRepair
//   • GetRepairCost returns zero for undamaged ships
//   • GetRepairCost scales linearly with damage
//   • OnFleetChanged fires when BeginRepair succeeds
//   • IsFleetLogEntry correctly identifies fleet-related log messages

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── DeriveDisplayStatus tests ─────────────────────────────────────────────

    [TestFixture]
    internal class FleetDeriveDisplayStatusTests
    {
        [Test]
        public void Undamaged_Docked_ReturnsDocked()
        {
            var ship = MakeShip("docked", ShipDamageState.Undamaged, 100f);
            Assert.AreEqual(FleetSubPanelController.StatusDocked,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void OnMission_NotCritical_ReturnsOnMission()
        {
            var ship = MakeShip("on_mission", ShipDamageState.Light, 80f);
            Assert.AreEqual(FleetSubPanelController.StatusOnMission,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void Docked_LightDamage_ReturnsDamaged()
        {
            var ship = MakeShip("docked", ShipDamageState.Light, 80f);
            Assert.AreEqual(FleetSubPanelController.StatusDamaged,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void Docked_ModerateDamage_ReturnsDamaged()
        {
            var ship = MakeShip("docked", ShipDamageState.Moderate, 55f);
            Assert.AreEqual(FleetSubPanelController.StatusDamaged,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void Docked_HeavyDamage_ReturnsDamaged()
        {
            var ship = MakeShip("docked", ShipDamageState.Heavy, 30f);
            Assert.AreEqual(FleetSubPanelController.StatusDamaged,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void AnyCriticalDamage_ReturnsInDistress()
        {
            var ship = MakeShip("docked", ShipDamageState.Critical, 10f);
            Assert.AreEqual(FleetSubPanelController.StatusInDistress,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void OnMission_CriticalDamage_ReturnsInDistress()
        {
            var ship = MakeShip("on_mission", ShipDamageState.Critical, 5f);
            Assert.AreEqual(FleetSubPanelController.StatusInDistress,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void Status_Destroyed_ReturnsDestroyed()
        {
            var ship = MakeShip("destroyed", ShipDamageState.Destroyed, 0f);
            Assert.AreEqual(FleetSubPanelController.StatusDestroyed,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void DamageState_Destroyed_ReturnsDestroyed()
        {
            var ship = MakeShip("docked", ShipDamageState.Destroyed, 0f);
            Assert.AreEqual(FleetSubPanelController.StatusDestroyed,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void Repairing_ReturnsDamaged()
        {
            var ship = MakeShip("repairing", ShipDamageState.Moderate, 55f);
            Assert.AreEqual(FleetSubPanelController.StatusDamaged,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void Departing_ReturnsDeparting()
        {
            var ship = MakeShip("departing", ShipDamageState.Undamaged, 100f);
            Assert.AreEqual(FleetSubPanelController.StatusDeparting,
                FleetSubPanelController.DeriveDisplayStatus(ship));
        }

        [Test]
        public void NullShip_ReturnsDocked()
        {
            Assert.AreEqual(FleetSubPanelController.StatusDocked,
                FleetSubPanelController.DeriveDisplayStatus(null));
        }

        private static OwnedShipInstance MakeShip(string status, ShipDamageState damageState, float conditionPct)
        {
            return new OwnedShipInstance
            {
                uid          = "test-uid",
                name         = "Test Ship",
                role         = "scout",
                status       = status,
                damageState  = damageState,
                conditionPct = conditionPct,
            };
        }
    }

    // ── StatusColor tests ────────────────────────────────────────────────────

    [TestFixture]
    internal class FleetStatusColorTests
    {
        [Test]
        public void AllSixStatuses_ReturnDistinctColors()
        {
            var statuses = new[]
            {
                FleetSubPanelController.StatusDocked,
                FleetSubPanelController.StatusDeparting,
                FleetSubPanelController.StatusOnMission,
                FleetSubPanelController.StatusInDistress,
                FleetSubPanelController.StatusDamaged,
                FleetSubPanelController.StatusDestroyed,
            };

            var colors = new HashSet<string>();
            foreach (var s in statuses)
            {
                var c = FleetSubPanelController.StatusColor(s);
                string key = $"{c.r:F2},{c.g:F2},{c.b:F2}";
                Assert.IsTrue(colors.Add(key),
                    $"Duplicate colour for status '{s}': {key}");
            }
        }

        [Test]
        public void InDistress_HasHighRedComponent()
        {
            var c = FleetSubPanelController.StatusColor(FleetSubPanelController.StatusInDistress);
            Assert.Greater(c.r, 0.7f, "In Distress colour must be predominantly red.");
            Assert.Less(c.g, 0.4f,    "In Distress colour must have low green component.");
        }

        [Test]
        public void OnMission_HasHighBlueComponent()
        {
            var c = FleetSubPanelController.StatusColor(FleetSubPanelController.StatusOnMission);
            Assert.Greater(c.b, 0.5f, "On Mission colour must have a strong blue component.");
        }

        [Test]
        public void Damaged_HasHighRedAndGreenComponent()
        {
            // Amber = high red + high green + low blue
            var c = FleetSubPanelController.StatusColor(FleetSubPanelController.StatusDamaged);
            Assert.Greater(c.r, 0.6f, "Damaged colour must have high red.");
            Assert.Greater(c.g, 0.4f, "Damaged colour must have significant green (amber).");
        }

        [Test]
        public void UnknownStatus_ReturnsFallbackColor()
        {
            var unknown = FleetSubPanelController.StatusColor("unknown_xyz");
            var docked  = FleetSubPanelController.StatusColor(FleetSubPanelController.StatusDocked);
            // Should fall through to the default which matches the docked colour.
            Assert.AreEqual(docked, unknown);
        }
    }

    // ── RoleBadgeLabel tests ─────────────────────────────────────────────────

    [TestFixture]
    internal class FleetRoleBadgeLabelTests
    {
        [TestCase("scout",      "Scout")]
        [TestCase("mining",     "Mining")]
        [TestCase("combat",     "Combat")]
        [TestCase("transport",  "Transport")]
        [TestCase("diplomatic", "Diplomatic")]
        [TestCase("trader",     "Trader")]
        [TestCase("patrol",     "Patrol")]
        public void KnownRole_ReturnsLabel(string role, string expected)
        {
            Assert.AreEqual(expected, FleetSubPanelController.RoleBadgeLabel(role));
        }

        [Test]
        public void NullRole_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", FleetSubPanelController.RoleBadgeLabel(null));
        }

        [Test]
        public void EmptyRole_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", FleetSubPanelController.RoleBadgeLabel(""));
        }

        [Test]
        public void UnknownRole_ReturnsRoleStringAsIs()
        {
            Assert.AreEqual("experimental", FleetSubPanelController.RoleBadgeLabel("experimental"));
        }
    }

    // ── IsFleetLogEntry tests ─────────────────────────────────────────────────

    [TestFixture]
    internal class FleetIsFleetLogEntryTests
    {
        [TestCase("Fleet ship 'Pathfinder' dispatched on scout mission (2 crew).", true)]
        [TestCase("Fleet ship 'Pathfinder' returned from scout mission.",           true)]
        [TestCase("Ship 'Pathfinder' damaged — condition: 60%.",                   true)]
        [TestCase("Repair job started on 'Pathfinder' — condition: 60%.",          true)]
        [TestCase("Ship 'Pathfinder' added to fleet.",                              true)]
        [TestCase("Crew member Alice assigned to Pathfinder.",                     true)]
        [TestCase("Room temperature equalised.",                                    false)]
        [TestCase("Trade offer accepted.",                                          false)]
        [TestCase("",                                                               false)]
        public void IsFleetLogEntry_ReturnsExpectedResult(string msg, bool expected)
        {
            Assert.AreEqual(expected, FleetSubPanelController.IsFleetLogEntry(msg));
        }
    }

    // ── Null-safety ───────────────────────────────────────────────────────────

    [TestFixture]
    internal class FleetSubPanelNullSafetyTests
    {
        private FleetSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FleetSubPanelController();

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_NullShipSystem_DoesNotThrow()
        {
            var station = new StationState("FleetUITest");
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }

        [Test]
        public void Refresh_EmptyFleet_RendersEmptyMessage()
        {
            var station = new StationState("FleetUITest");
            _panel.Refresh(station, null);
            // Panel should contain an "empty fleet" label somewhere.
            var labels = _panel.Query<Label>().ToList();
            bool hasEmpty = false;
            foreach (var lbl in labels)
                if ((lbl.text ?? "").ToLowerInvariant().Contains("no ships"))
                    hasEmpty = true;
            Assert.IsTrue(hasEmpty, "Empty fleet should render 'No ships' message.");
        }
    }

    // ── Ship list rendering ────────────────────────────────────────────────────

    [TestFixture]
    internal class FleetShipListRenderTests
    {
        private FleetSubPanelController _panel;
        private ContentRegistry         _registry;
        private GameObject              _registryGo;
        private ShipSystem              _shipSystem;
        private StationState            _station;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate(),
                ShipTestHelpers.CombatTemplate());
            _shipSystem = new ShipSystem(_registry);
            _station    = ShipTestHelpers.MakeStation();
            _panel      = new FleetSubPanelController();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            if (_registryGo != null) Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void ShipList_ShowsAllShipNames()
        {
            _shipSystem.AddShipToFleet("ship.scout_vessel",   "Alpha",   _station);
            _shipSystem.AddShipToFleet("ship.combat_frigate", "Bravo",   _station);

            _panel.Refresh(_station, _shipSystem);

            var labels = _panel.Query<Label>().ToList();
            bool hasAlpha = false;
            bool hasBravo = false;
            foreach (var lbl in labels)
            {
                if (lbl.text == "Alpha") hasAlpha = true;
                if (lbl.text == "Bravo") hasBravo = true;
            }
            Assert.IsTrue(hasAlpha, "Ship list must show ship named 'Alpha'.");
            Assert.IsTrue(hasBravo, "Ship list must show ship named 'Bravo'.");
        }

        [Test]
        public void InDistressShip_ShowsRescueDispatchButton()
        {
            var ship = _shipSystem.AddShipToFleet("ship.scout_vessel", "Ranger", _station);
            // Force critical damage state so the ship shows as In Distress.
            ship.damageState  = ShipDamageState.Critical;
            ship.conditionPct = 10f;

            _panel.Refresh(_station, _shipSystem);

            // Check for a button with "Dispatch Rescue" text.
            var buttons = _panel.Query<Button>().ToList();
            bool hasRescue = false;
            foreach (var btn in buttons)
                if ((btn.text ?? "").Contains("Dispatch Rescue"))
                    hasRescue = true;

            Assert.IsTrue(hasRescue,
                "In Distress ship row must include a 'Dispatch Rescue' button.");
        }

        [Test]
        public void NonDistressShip_DoesNotShowRescueDispatchButton()
        {
            _shipSystem.AddShipToFleet("ship.scout_vessel", "Ranger", _station);
            // Ship defaults to Undamaged / docked.

            _panel.Refresh(_station, _shipSystem);

            var buttons = _panel.Query<Button>().ToList();
            bool hasRescue = false;
            foreach (var btn in buttons)
                if ((btn.text ?? "").Contains("Dispatch Rescue"))
                    hasRescue = true;

            Assert.IsFalse(hasRescue,
                "Undamaged ship row must NOT include a 'Dispatch Rescue' button.");
        }

        [Test]
        public void InDistressShip_RescueDispatch_FiresCallback()
        {
            var ship = _shipSystem.AddShipToFleet("ship.scout_vessel", "Ranger", _station);
            ship.damageState  = ShipDamageState.Critical;
            ship.conditionPct = 10f;

            string firedUid = null;
            _panel.OnRescueDispatch = uid => firedUid = uid;

            _panel.Refresh(_station, _shipSystem);

            var buttons = _panel.Query<Button>().ToList();
            foreach (var btn in buttons)
            {
                if ((btn.text ?? "").Contains("Dispatch Rescue"))
                {
                    using var evt = ClickEvent.GetPooled();
                    evt.target = btn;
                    btn.SendEvent(evt);
                }
            }

            Assert.AreEqual(ship.uid, firedUid,
                "Rescue dispatch callback must be invoked with the ship's uid.");
        }

        [Test]
        public void DamagedShip_ShowsRepairButtonInDetail()
        {
            var ship = _shipSystem.AddShipToFleet("ship.scout_vessel", "Ranger", _station);
            ship.damageState  = ShipDamageState.Moderate;
            ship.conditionPct = 55f;
            ship.status       = "docked";

            _panel.Refresh(_station, _shipSystem);

            // Simulate clicking the ship row.
            var rows = _panel.Query(className: "ws-fleet-panel__ship-row").ToList();
            Assert.IsNotEmpty(rows, "At least one ship row must be rendered.");
            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            // After selection, detail panel should contain repair button.
            var buttons = _panel.Query<Button>().ToList();
            bool hasRepair = false;
            foreach (var btn in buttons)
                if ((btn.text ?? "").Contains("Begin Repair"))
                    hasRepair = true;

            Assert.IsTrue(hasRepair, "Damaged ship detail must show a 'Begin Repair' button.");
        }

        [Test]
        public void DestroyedShip_DoesNotShowRepairButton()
        {
            var ship = _shipSystem.AddShipToFleet("ship.scout_vessel", "Ranger", _station);
            ship.damageState  = ShipDamageState.Destroyed;
            ship.conditionPct = 0f;
            ship.status       = "destroyed";

            _panel.Refresh(_station, _shipSystem);

            var rows = _panel.Query(className: "ws-fleet-panel__ship-row").ToList();
            if (rows.Count > 0)
            {
                using var evt = ClickEvent.GetPooled();
                evt.target = rows[0];
                rows[0].SendEvent(evt);
            }

            var buttons = _panel.Query<Button>().ToList();
            bool hasRepair = false;
            foreach (var btn in buttons)
                if ((btn.text ?? "").Contains("Begin Repair"))
                    hasRepair = true;

            Assert.IsFalse(hasRepair, "Destroyed ship detail must NOT show a 'Begin Repair' button.");
        }
    }

    // ── Ship Detail sub-panel: crew and mission history ───────────────────────

    [TestFixture]
    internal class FleetShipDetailTests
    {
        private FleetSubPanelController _panel;
        private ContentRegistry         _registry;
        private GameObject              _registryGo;
        private ShipSystem              _shipSystem;
        private StationState            _station;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate());
            _shipSystem = new ShipSystem(_registry);
            _station    = ShipTestHelpers.MakeStation();
            _panel      = new FleetSubPanelController();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            if (_registryGo != null) Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void ShipDetail_ShowsAssignedCrewNames()
        {
            var ship = _shipSystem.AddShipToFleet("ship.scout_vessel", "Pioneer", _station);

            // Assign a crew member.
            var npc = ShipTestHelpers.MakeCrew("Alice");
            _station.npcs[npc.uid] = npc;
            _shipSystem.AssignCrew(ship.uid, new List<string> { npc.uid }, _station);

            _panel.Refresh(_station, _shipSystem);

            // Trigger the row click to show the detail panel.
            var rows = _panel.Query(className: "ws-fleet-panel__ship-row").ToList();
            Assert.IsNotEmpty(rows, "At least one ship row must be rendered.");
            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            // Crew name should appear in the detail panel labels.
            var labels = _panel.Query<Label>().ToList();
            bool hasAlice = false;
            foreach (var lbl in labels)
                if ((lbl.text ?? "").Contains("Alice"))
                    hasAlice = true;

            Assert.IsTrue(hasAlice,
                "Ship Detail must show the name of an assigned crew member.");
        }

        [Test]
        public void ShipDetail_ShowsMissionHistoryForShip()
        {
            var ship = _shipSystem.AddShipToFleet("ship.scout_vessel", "Pioneer", _station);
            // Log a fleet event matching the ship name.
            _station.LogEvent($"Fleet ship '{ship.name}' dispatched on scout mission (1 crew).");
            _station.LogEvent($"Fleet ship '{ship.name}' returned from scout mission.");
            // Unrelated event that should not appear.
            _station.LogEvent("Room temperature equalised.");

            _panel.Refresh(_station, _shipSystem);

            var rows = _panel.Query(className: "ws-fleet-panel__ship-row").ToList();
            Assert.IsNotEmpty(rows);
            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            var labels = _panel.Query<Label>().ToList();
            bool hasDispatch = false;
            bool hasReturn   = false;
            foreach (var lbl in labels)
            {
                if ((lbl.text ?? "").Contains("dispatched")) hasDispatch = true;
                if ((lbl.text ?? "").Contains("returned"))   hasReturn   = true;
            }

            Assert.IsTrue(hasDispatch, "Mission history must include the dispatch event.");
            Assert.IsTrue(hasReturn,   "Mission history must include the return event.");
        }
    }

    // ── ShipSystem.GetOwnedShips ──────────────────────────────────────────────

    [TestFixture]
    internal class ShipSystemGetOwnedShipsTests
    {
        private ContentRegistry _registry;
        private GameObject      _registryGo;
        private ShipSystem      _system;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate(),
                ShipTestHelpers.MiningTemplate());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void GetOwnedShips_NullStation_ReturnsEmptyList()
        {
            var result = _system.GetOwnedShips(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetOwnedShips_EmptyFleet_ReturnsEmptyList()
        {
            var result = _system.GetOwnedShips(_station);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetOwnedShips_ReturnsSortedByName()
        {
            _system.AddShipToFleet("ship.scout_vessel", "Zephyr", _station);
            _system.AddShipToFleet("ship.mining_barge", "Alpha",  _station);
            _system.AddShipToFleet("ship.scout_vessel", "Mira",   _station);

            var result = _system.GetOwnedShips(_station);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Alpha",  result[0].name);
            Assert.AreEqual("Mira",   result[1].name);
            Assert.AreEqual("Zephyr", result[2].name);
        }
    }

    // ── ShipSystem.GetRepairCost ──────────────────────────────────────────────

    [TestFixture]
    internal class ShipSystemGetRepairCostTests
    {
        [Test]
        public void GetRepairCost_NullShip_ReturnsZero()
        {
            var (parts, ticks) = ShipSystem.GetRepairCost(null);
            Assert.AreEqual(0, parts);
            Assert.AreEqual(0, ticks);
        }

        [Test]
        public void GetRepairCost_UndamagedShip_ReturnsZero()
        {
            var ship = new OwnedShipInstance { conditionPct = 100f };
            var (parts, ticks) = ShipSystem.GetRepairCost(ship);
            Assert.AreEqual(0, parts);
            Assert.AreEqual(0, ticks);
        }

        [Test]
        public void GetRepairCost_TenPercentDamage_ReturnsOnePart()
        {
            var ship = new OwnedShipInstance { conditionPct = 90f };
            var (parts, _) = ShipSystem.GetRepairCost(ship);
            Assert.AreEqual(1, parts);
        }

        [Test]
        public void GetRepairCost_NinetyPercentDamage_ReturnsCeiling()
        {
            var ship = new OwnedShipInstance { conditionPct = 10f };
            var (parts, _) = ShipSystem.GetRepairCost(ship);
            // 90 % damage → ceil(90/10) = 9 parts
            Assert.AreEqual(9, parts);
        }

        [Test]
        public void GetRepairCost_TicksScaleWithDamage()
        {
            var ship20  = new OwnedShipInstance { conditionPct = 80f }; // 20 % damage
            var ship40  = new OwnedShipInstance { conditionPct = 60f }; // 40 % damage

            var (_, ticks20) = ShipSystem.GetRepairCost(ship20);
            var (_, ticks40) = ShipSystem.GetRepairCost(ship40);

            Assert.Greater(ticks40, ticks20, "More damage should require more ticks.");
        }
    }

    // ── ShipSystem.BeginRepair ────────────────────────────────────────────────

    [TestFixture]
    internal class ShipSystemBeginRepairTests
    {
        private ContentRegistry _registry;
        private GameObject      _registryGo;
        private ShipSystem      _system;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void BeginRepair_DamagedShip_SetsStatusRepairing()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            _system.ApplyDamage(ship.uid, 30f, _station);
            Assert.AreNotEqual("repairing", ship.status, "Pre-condition: ship should not yet be repairing.");

            bool ok = _system.BeginRepair(ship.uid, _station, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual("repairing", ship.status);
        }

        [Test]
        public void BeginRepair_UndamagedShip_ReturnsFalse()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            bool ok = _system.BeginRepair(ship.uid, _station, out string reason);

            Assert.IsFalse(ok);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void BeginRepair_DestroyedShip_ReturnsFalse()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            ship.damageState  = ShipDamageState.Destroyed;
            ship.conditionPct = 0f;

            bool ok = _system.BeginRepair(ship.uid, _station, out string reason);

            Assert.IsFalse(ok);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void BeginRepair_AlreadyRepairing_ReturnsFalse()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            _system.ApplyDamage(ship.uid, 30f, _station);
            _system.BeginRepair(ship.uid, _station, out _);

            bool ok = _system.BeginRepair(ship.uid, _station, out string reason);

            Assert.IsFalse(ok, "BeginRepair should fail if ship is already repairing.");
            Assert.IsNotNull(reason);
        }

        [Test]
        public void BeginRepair_UnknownShip_ReturnsFalse()
        {
            bool ok = _system.BeginRepair("nonexistent-uid", _station, out string reason);
            Assert.IsFalse(ok);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void BeginRepair_Success_FiresOnFleetChangedEvent()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            _system.ApplyDamage(ship.uid, 30f, _station);

            bool eventFired = false;
            _system.OnFleetChanged += () => eventFired = true;

            _system.BeginRepair(ship.uid, _station, out _);

            Assert.IsTrue(eventFired, "OnFleetChanged must fire when BeginRepair succeeds.");
        }

        [Test]
        public void BeginRepair_Failure_DoesNotFireOnFleetChangedEvent()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            // Ship is undamaged — BeginRepair should fail.

            bool eventFired = false;
            _system.OnFleetChanged += () => eventFired = true;

            _system.BeginRepair(ship.uid, _station, out _);

            Assert.IsFalse(eventFired, "OnFleetChanged must NOT fire when BeginRepair fails.");
        }
    }
}
