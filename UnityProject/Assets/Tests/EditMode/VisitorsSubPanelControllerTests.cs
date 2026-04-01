// VisitorsSubPanelControllerTests.cs
// EditMode unit tests for VisitorsSubPanelController (UI-016) and the
// docking-decision API additions to VisitorSystem (GrantDocking / DenyDocking /
// NegotiateDocking / PendingDecisions).
//
// Tests cover:
//   * RoleBadgeLabel maps role strings correctly
//   * IsVisitorLogEntry correctly identifies visitor-related log entries
//   * Refresh with null station / null VisitorSystem does not throw
//   * Pending ships render at the top of the panel with Grant / Deny / Negotiate buttons
//   * Docked ship row is clickable and fires OnShipRowClicked
//   * Docked ship row shows passenger count and departure countdown
//   * Grant / Deny / Negotiate callbacks are invoked with the correct ship uid
//   * VisitorSystem.GrantDocking admits ship and removes from PendingDecisions
//   * VisitorSystem.DenyDocking denies ship and removes from PendingDecisions
//   * VisitorSystem.NegotiateDocking admits ship and removes from PendingDecisions
//   * VisitorSystem.DepartShip removes uid from PendingDecisions

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
    // ── Shared helpers ─────────────────────────────────────────────────────────

    internal static class VisitorsTestHelpers
    {
        public static StationState MakeStation(string name = "VisitorsTest")
            => new StationState(name);

        public static ShipInstance MakeIncomingShip(
            string name = "ISV Test Ship", string role = "trader", string factionId = null)
        {
            var ship = ShipInstance.Create("ship.test", name, role, "trade", factionId, 0);
            ship.status = "incoming";
            return ship;
        }

        public static ShipInstance MakeDockedShip(
            string name = "ISV Docked Ship", string role = "trader", string factionId = null)
        {
            var ship = ShipInstance.Create("ship.test", name, role, "trade", factionId, 0);
            ship.status = "docked";
            return ship;
        }

        /// <summary>
        /// Adds an available dock module to the station so AdmitShip can succeed.
        /// </summary>
        public static void AddDock(StationState station)
        {
            var dock = ModuleInstance.Create("module.dock", "Docking Bay", "dock");
            dock.active = true;
            station.AddModule(dock);
        }
    }

    // ── RoleBadgeLabel tests ───────────────────────────────────────────────────

    [TestFixture]
    internal class VisitorRoleBadgeLabelTests
    {
        [TestCase("trader",    "Trader")]
        [TestCase("refugee",   "Refugee")]
        [TestCase("inspector", "Inspector")]
        [TestCase("smuggler",  "Smuggler")]
        [TestCase("raider",    "Raider")]
        [TestCase("transport", "Transport")]
        [TestCase("patrol",    "Patrol")]
        public void RoleBadgeLabel_KnownRole_ReturnsExpectedLabel(string role, string expected)
        {
            Assert.AreEqual(expected, VisitorsSubPanelController.RoleBadgeLabel(role));
        }

        [Test]
        public void RoleBadgeLabel_NullRole_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", VisitorsSubPanelController.RoleBadgeLabel(null));
        }

        [Test]
        public void RoleBadgeLabel_EmptyRole_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", VisitorsSubPanelController.RoleBadgeLabel(""));
        }

        [Test]
        public void RoleBadgeLabel_UnknownRole_ReturnsRoleString()
        {
            Assert.AreEqual("diplomat", VisitorsSubPanelController.RoleBadgeLabel("diplomat"));
        }
    }

    // ── IsVisitorLogEntry tests ────────────────────────────────────────────────

    [TestFixture]
    internal class VisitorIsVisitorLogEntryTests
    {
        [TestCase("[T0001] Incoming: ISV Test (trader, intent=trade, threat=none)", true)]
        [TestCase("[T0002] ISV Test docked at Docking Bay.", true)]
        [TestCase("[T0003] ISV Test denied entry — ship departing.", true)]
        [TestCase("[T0004] ISV Test departed.", true)]
        [TestCase("[T0005] Trade offer from ISV Test: 3 sell, 2 buy lines.", true)]
        [TestCase("[T0006] Inspection patrol inbound: CRV Authority.", true)]
        [TestCase("[T0007] CRV Authority denied — ship turning hostile!", true)]
        [TestCase("[T0008] Negotiation opened with ISV Test.", true)]
        [TestCase("[T0009] No dock available for ISV Test — ship queuing.", true)]
        [TestCase("[T0010] Crew member Alice completed shift.", false)]
        [TestCase("[T0011] Room temperature equalised.", false)]
        [TestCase("",  false)]
        public void IsVisitorLogEntry_ReturnsExpectedResult(string entry, bool expected)
        {
            Assert.AreEqual(expected, VisitorsSubPanelController.IsVisitorLogEntry(entry));
        }
    }

    // ── Null-safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class VisitorsSubPanelNullSafetyTests
    {
        private VisitorsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new VisitorsSubPanelController();

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_NullVisitorSystem_DoesNotThrow()
        {
            var station = VisitorsTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }
    }

    // ── Pending decisions appear at the top ────────────────────────────────────

    [TestFixture]
    internal class VisitorsPendingDecisionsRenderTests
    {
        private VisitorsSubPanelController _panel;
        private GameObject _registryGo;

        [SetUp]
        public void SetUp() => _panel = new VisitorsSubPanelController();

        [TearDown]
        public void TearDown()
        {
            if (_registryGo != null)
                Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void PendingShip_AppearsWithGrantDenyNegotiateButtons()
        {
            _registryGo = new GameObject("VisitorsTestRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            eventStub.Events["event.arrival_generic"] = new EventDefinition
                { id = "event.arrival_generic", title = "Arrival", weight = 0f };
            var eventSystem = new EventSystem(eventStub, "normal");
            var visitors = new VisitorSystem(registry, null, eventSystem);

            var station = VisitorsTestHelpers.MakeStation();
            var pending = VisitorsTestHelpers.MakeIncomingShip("ISV Pending");
            station.AddShip(pending);
            visitors.PendingDecisions.Add(pending.uid);

            _panel.Refresh(station, visitors);

            // Grant, Deny, Negotiate buttons should all be present.
            var buttons = _panel.Query<Label>(className: "ws-visitors-panel__action-btn").ToList();
            Assert.AreEqual(3, buttons.Count,
                "Pending ship row must have exactly 3 action buttons (Grant, Deny, Negotiate).");

            bool hasGrant    = false;
            bool hasDeny     = false;
            bool hasNegotiate = false;
            foreach (var btn in buttons)
            {
                if (btn.text == "Grant")     hasGrant = true;
                if (btn.text == "Deny")      hasDeny = true;
                if (btn.text == "Negotiate") hasNegotiate = true;
            }
            Assert.IsTrue(hasGrant,     "Grant button must be present for pending ship.");
            Assert.IsTrue(hasDeny,      "Deny button must be present for pending ship.");
            Assert.IsTrue(hasNegotiate, "Negotiate button must be present for pending ship.");
        }

        [Test]
        public void PendingShip_RendersAboveDockedShip()
        {
            _registryGo = new GameObject("VisitorsTestRegistry2");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            eventStub.Events["event.arrival_generic"] = new EventDefinition
                { id = "event.arrival_generic", title = "Arrival", weight = 0f };
            var visitors = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation();

            // Add a docked ship first to ensure ordering is by section, not insertion.
            var docked = VisitorsTestHelpers.MakeDockedShip("ISV Alpha");
            station.AddShip(docked);

            var pending = VisitorsTestHelpers.MakeIncomingShip("ISV Beta");
            station.AddShip(pending);
            visitors.PendingDecisions.Add(pending.uid);

            _panel.Refresh(station, visitors);

            // The section headers appear in order: pending, docked, incoming.
            // The first section header in the list must be the "Pending Decisions" header.
            var headers = _panel.Query<Label>(className: "ws-visitors-panel__section-header").ToList();
            Assert.GreaterOrEqual(headers.Count, 2, "At least 2 section headers expected.");
            StringAssert.Contains("Pending", headers[0].text,
                "Pending Decisions section must appear first.");
        }

        [Test]
        public void NoPendingShips_NoActionButtonsRendered()
        {
            _registryGo = new GameObject("VisitorsTestRegistry3");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            var visitors = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation();
            var docked = VisitorsTestHelpers.MakeDockedShip("ISV Docked");
            station.AddShip(docked);

            _panel.Refresh(station, visitors);

            var buttons = _panel.Query<Label>(className: "ws-visitors-panel__action-btn").ToList();
            Assert.AreEqual(0, buttons.Count,
                "No action buttons should appear when there are no pending ships.");
        }

        [Test]
        public void PendingShip_DoesNotAppearInIncomingSection()
        {
            _registryGo = new GameObject("VisitorsTestRegistry4");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            eventStub.Events["event.arrival_generic"] = new EventDefinition
                { id = "event.arrival_generic", title = "Arrival", weight = 0f };
            var visitors = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation();

            // Add two incoming ships; mark only the first as pending.
            var pending  = VisitorsTestHelpers.MakeIncomingShip("ISV Pending Only");
            var incoming = VisitorsTestHelpers.MakeIncomingShip("ISV True Incoming");
            station.AddShip(pending);
            station.AddShip(incoming);
            visitors.PendingDecisions.Add(pending.uid);

            _panel.Refresh(station, visitors);

            // The panel should have exactly one ship row for the Incoming section
            // (the truly incoming ship) plus one pending row — not two incoming rows.
            var allRows = _panel.Query<VisualElement>(className: "ws-visitors-panel__ship-row").ToList();
            Assert.AreEqual(2, allRows.Count,
                "Expected 1 pending row + 1 incoming row = 2 total rows (no duplicate).");

            // The pending row must have action buttons; the incoming row must not.
            var actionBtns = _panel.Query<Label>(className: "ws-visitors-panel__action-btn").ToList();
            Assert.AreEqual(3, actionBtns.Count,
                "Only the pending ship should have action buttons; the incoming ship must not.");
        }
    }

    // ── Docked ship row click ──────────────────────────────────────────────────

    [TestFixture]
    internal class VisitorsDockedRowClickTests
    {
        private VisitorsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new VisitorsSubPanelController();

        [Test]
        public void DockedShipRowClick_FiresOnShipRowClicked_WithCorrectUid()
        {
            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeDockedShip("ISV Rover");
            station.AddShip(ship);

            _panel.Refresh(station, null);

            string receivedUid = null;
            _panel.OnShipRowClicked += uid => receivedUid = uid;

            var rows = _panel.Query<VisualElement>(className: "ws-visitors-panel__ship-row").ToList();
            Assert.AreEqual(1, rows.Count, "Expected exactly one ship row.");

            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            Assert.IsNotNull(receivedUid, "Row click should invoke OnShipRowClicked.");
            Assert.AreEqual(ship.uid, receivedUid,
                "OnShipRowClicked should receive the docked ship's uid.");
        }
    }

    // ── Docked ship row detail labels ──────────────────────────────────────────

    [TestFixture]
    internal class VisitorsDockedRowDetailTests
    {
        private VisitorsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new VisitorsSubPanelController();

        [Test]
        public void DockedShip_ShowsRoleBadge()
        {
            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeDockedShip("ISV Merchant", "trader");
            station.AddShip(ship);

            _panel.Refresh(station, null);

            var badges = _panel.Query<Label>(className: "ws-visitors-panel__role-badge").ToList();
            Assert.AreEqual(1, badges.Count);
            Assert.AreEqual("Trader", badges[0].text);
        }

        [Test]
        public void DockedShip_ShowsPassengerCount_WhenPassengersPresent()
        {
            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeDockedShip("ISV Full", "refugee");
            ship.passengerUids.Add("npc_1");
            ship.passengerUids.Add("npc_2");
            station.AddShip(ship);

            _panel.Refresh(station, null);

            var details = _panel.Query<Label>(className: "ws-visitors-panel__ship-detail").ToList();
            bool foundPassengerCount = false;
            foreach (var d in details)
                if (d.text.Contains("2 visitors")) { foundPassengerCount = true; break; }

            Assert.IsTrue(foundPassengerCount, "Docked row should display passenger count.");
        }

        [Test]
        public void DockedShip_ShowsName()
        {
            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeDockedShip("ISV Named Vessel");
            station.AddShip(ship);

            _panel.Refresh(station, null);

            var names = _panel.Query<Label>(className: "ws-visitors-panel__ship-name").ToList();
            Assert.AreEqual(1, names.Count);
            Assert.AreEqual("ISV Named Vessel", names[0].text);
        }
    }

    // ── Grant / Deny / Negotiate callbacks ────────────────────────────────────

    [TestFixture]
    internal class VisitorsDockingDecisionCallbackTests
    {
        private VisitorsSubPanelController _panel;
        private GameObject _registryGo;

        [SetUp]
        public void SetUp() => _panel = new VisitorsSubPanelController();

        [TearDown]
        public void TearDown()
        {
            if (_registryGo != null)
                Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void GrantButton_InvokesOnGrantDocking_WithCorrectShipId()
        {
            _registryGo = new GameObject("GrantCallbackRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            var visitors = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeIncomingShip("ISV Grant Test");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            string receivedId = null;
            _panel.OnGrantDocking = id => receivedId = id;
            _panel.Refresh(station, visitors);

            var buttons = _panel.Query<Label>(className: "ws-visitors-panel__action-btn").ToList();
            Label grantBtn = null;
            foreach (var btn in buttons) if (btn.text == "Grant") { grantBtn = btn; break; }
            Assert.IsNotNull(grantBtn, "Grant button should be present.");

            using var evt = ClickEvent.GetPooled();
            evt.target = grantBtn;
            grantBtn.SendEvent(evt);

            Assert.AreEqual(ship.uid, receivedId,
                "OnGrantDocking must be called with the pending ship's uid.");
        }

        [Test]
        public void DenyButton_InvokesOnDenyDocking_WithCorrectShipId()
        {
            _registryGo = new GameObject("DenyCallbackRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            var visitors = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeIncomingShip("ISV Deny Test");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            string receivedId = null;
            _panel.OnDenyDocking = id => receivedId = id;
            _panel.Refresh(station, visitors);

            var buttons = _panel.Query<Label>(className: "ws-visitors-panel__action-btn").ToList();
            Label denyBtn = null;
            foreach (var btn in buttons) if (btn.text == "Deny") { denyBtn = btn; break; }
            Assert.IsNotNull(denyBtn, "Deny button should be present.");

            using var evt = ClickEvent.GetPooled();
            evt.target = denyBtn;
            denyBtn.SendEvent(evt);

            Assert.AreEqual(ship.uid, receivedId,
                "OnDenyDocking must be called with the pending ship's uid.");
        }

        [Test]
        public void NegotiateButton_InvokesOnNegotiateDocking_WithCorrectShipId()
        {
            _registryGo = new GameObject("NegotiateCallbackRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            var visitors = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation();
            var ship    = VisitorsTestHelpers.MakeIncomingShip("ISV Negotiate Test");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            string receivedId = null;
            _panel.OnNegotiateDocking = id => receivedId = id;
            _panel.Refresh(station, visitors);

            var buttons = _panel.Query<Label>(className: "ws-visitors-panel__action-btn").ToList();
            Label negotiateBtn = null;
            foreach (var btn in buttons) if (btn.text == "Negotiate") { negotiateBtn = btn; break; }
            Assert.IsNotNull(negotiateBtn, "Negotiate button should be present.");

            using var evt = ClickEvent.GetPooled();
            evt.target = negotiateBtn;
            negotiateBtn.SendEvent(evt);

            Assert.AreEqual(ship.uid, receivedId,
                "OnNegotiateDocking must be called with the pending ship's uid.");
        }
    }

    // ── VisitorSystem docking decision API ────────────────────────────────────

    [TestFixture]
    internal class VisitorSystemDockingDecisionApiTests
    {
        private GameObject _registryGo;

        [TearDown]
        public void TearDown()
        {
            if (_registryGo != null)
                Object.DestroyImmediate(_registryGo);
        }

        private (VisitorSystem visitors, StationState station) MakeSetup(
            string stationName = "ApiTest")
        {
            _registryGo = new GameObject("ApiTestRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            registry.Ships["ship.test"] = new ShipTemplate
                { id = "ship.test", role = "trader", passengerCapacity = 0 };
            var eventStub = new EventStubRegistry();
            eventStub.Events["event.arrival_generic"] = new EventDefinition
                { id = "event.arrival_generic", title = "Arrival", weight = 0f };
            var events   = new EventSystem(eventStub, "normal");
            var visitors = new VisitorSystem(registry, null, events);

            var station = VisitorsTestHelpers.MakeStation(stationName);
            VisitorsTestHelpers.AddDock(station);
            return (visitors, station);
        }

        [Test]
        public void GrantDocking_AdmitsShipAndRemovesFromPending()
        {
            var (visitors, station) = MakeSetup("GrantTest");
            var ship = VisitorsTestHelpers.MakeIncomingShip("ISV Grant");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            bool admitted = visitors.GrantDocking(ship.uid, station);

            Assert.IsTrue(admitted, "GrantDocking should return true when dock is available.");
            Assert.IsFalse(visitors.PendingDecisions.Contains(ship.uid),
                "GrantDocking must remove the ship uid from PendingDecisions on success.");
            Assert.IsTrue(station.ships.TryGetValue(ship.uid, out var updated));
            Assert.AreEqual("docked", updated.status,
                "GrantDocking must admit the ship (status → docked).");
        }

        [Test]
        public void GrantDocking_NoDockAvailable_KeepsPendingDecision()
        {
            // Use a setup WITHOUT a dock so AdmitShip returns false.
            _registryGo = new GameObject("GrantNoDockRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            registry.Ships["ship.test"] = new ShipTemplate
                { id = "ship.test", role = "trader", passengerCapacity = 0 };
            var eventStub = new EventStubRegistry();
            var events    = new EventSystem(eventStub, "normal");
            var visitors  = new VisitorSystem(registry, null, events);

            var station = VisitorsTestHelpers.MakeStation("GrantNoDock");
            // Intentionally no dock added.
            var ship = VisitorsTestHelpers.MakeIncomingShip("ISV No Dock");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            bool admitted = visitors.GrantDocking(ship.uid, station);

            Assert.IsFalse(admitted, "GrantDocking should return false when no dock is available.");
            Assert.IsTrue(visitors.PendingDecisions.Contains(ship.uid),
                "PendingDecisions must NOT be cleared when docking fails.");
            Assert.AreEqual("incoming", ship.status,
                "Ship status must remain incoming when docking fails.");
        }

        [Test]
        public void DenyDocking_DeniesShipAndRemovesFromPending_NonHostileShip()
        {
            var (visitors, station) = MakeSetup("DenyTest");
            var ship = VisitorsTestHelpers.MakeIncomingShip("ISV Deny");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            visitors.DenyDocking(ship.uid, station);

            Assert.IsFalse(visitors.PendingDecisions.Contains(ship.uid),
                "DenyDocking must remove the ship uid from PendingDecisions.");
            // Non-hostile ship should be removed from the station entirely.
            Assert.IsFalse(station.ships.ContainsKey(ship.uid),
                "Denied non-hostile ship should be removed from station.ships.");
        }

        [Test]
        public void NegotiateDocking_AdmitsShipAndRemovesFromPending()
        {
            var (visitors, station) = MakeSetup("NegotiateTest");
            var ship = VisitorsTestHelpers.MakeIncomingShip("ISV Negotiate");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            bool admitted = visitors.NegotiateDocking(ship.uid, station);

            Assert.IsTrue(admitted, "NegotiateDocking should return true when dock is available.");
            Assert.IsFalse(visitors.PendingDecisions.Contains(ship.uid),
                "NegotiateDocking must remove the ship uid from PendingDecisions on success.");
            Assert.IsTrue(station.ships.TryGetValue(ship.uid, out var updated));
            Assert.AreEqual("docked", updated.status,
                "NegotiateDocking must admit the ship (status → docked).");
        }

        [Test]
        public void NegotiateDocking_NullStation_ReturnsFalse()
        {
            var (visitors, _) = MakeSetup("NegotiateNullTest");
            Assert.IsFalse(visitors.NegotiateDocking("any_uid", null),
                "NegotiateDocking must return false and not throw when station is null.");
        }

        [Test]
        public void NegotiateDocking_NoDockAvailable_KeepsPendingDecision()
        {
            // Station without a dock.
            _registryGo = new GameObject("NegotiateNoDockRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            registry.Ships["ship.test"] = new ShipTemplate
                { id = "ship.test", role = "trader", passengerCapacity = 0 };
            var eventStub = new EventStubRegistry();
            var visitors  = new VisitorSystem(registry, null, new EventSystem(eventStub, "normal"));

            var station = VisitorsTestHelpers.MakeStation("NegotiateNoDock");
            var ship    = VisitorsTestHelpers.MakeIncomingShip("ISV No Dock Negotiate");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            bool admitted = visitors.NegotiateDocking(ship.uid, station);

            Assert.IsFalse(admitted,
                "NegotiateDocking should return false when no dock is available.");
            Assert.IsTrue(visitors.PendingDecisions.Contains(ship.uid),
                "PendingDecisions must NOT be cleared when negotiate-docking fails.");
        }

        [Test]
        public void DepartShip_RemovesUidFromPendingDecisions()
        {
            var (visitors, station) = MakeSetup("DepartTest");
            var ship = VisitorsTestHelpers.MakeIncomingShip("ISV Depart");
            station.AddShip(ship);
            visitors.PendingDecisions.Add(ship.uid);

            // Depart without granting first — ship should still be cleaned up.
            visitors.DepartShip(ship.uid, station);

            Assert.IsFalse(visitors.PendingDecisions.Contains(ship.uid),
                "DepartShip must remove the ship uid from PendingDecisions.");
        }

        [Test]
        public void GetDockedShips_DelegatesToStation()
        {
            var (visitors, station) = MakeSetup("GetDockedTest");
            var ship = VisitorsTestHelpers.MakeDockedShip("ISV Docked Query");
            station.AddShip(ship);

            var result = visitors.GetDockedShips(station);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(ship.uid, result[0].uid);
        }

        [Test]
        public void GetIncomingShips_DelegatesToStation()
        {
            var (visitors, station) = MakeSetup("GetIncomingTest");
            var ship = VisitorsTestHelpers.MakeIncomingShip("ISV Incoming Query");
            station.AddShip(ship);

            var result = visitors.GetIncomingShips(station);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(ship.uid, result[0].uid);
        }

        [Test]
        public void GetDockedShips_NullStation_ReturnsEmptyList()
        {
            var (visitors, _) = MakeSetup("GetDockedNullTest");
            var result = visitors.GetDockedShips(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void GetIncomingShips_NullStation_ReturnsEmptyList()
        {
            var (visitors, _) = MakeSetup("GetIncomingNullTest");
            var result = visitors.GetIncomingShips(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }
    }

    // ── Comms log section ──────────────────────────────────────────────────────

    [TestFixture]
    internal class VisitorsCommsLogTests
    {
        private VisitorsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new VisitorsSubPanelController();

        [Test]
        public void CommsLog_ShowsVisitorRelatedEntries()
        {
            var station = VisitorsTestHelpers.MakeStation();
            station.LogEvent("ISV Test docked at Docking Bay.");     // visitor
            station.LogEvent("Crew member completed daily task.");   // not visitor

            _panel.Refresh(station, null);

            var commsRows = _panel.Query<Label>(className: "ws-visitors-panel__comms-row").ToList();
            Assert.AreEqual(1, commsRows.Count,
                "Comms log should only include visitor-related entries.");
            StringAssert.Contains("docked", commsRows[0].text.ToLower());
        }

        [Test]
        public void CommsLog_CapsAtTwentyEntries()
        {
            var station = VisitorsTestHelpers.MakeStation();
            for (int i = 0; i < 30; i++)
                station.LogEvent($"Incoming: ISV Ship{i} (trader, intent=trade, threat=none)");

            _panel.Refresh(station, null);

            var commsRows = _panel.Query<Label>(className: "ws-visitors-panel__comms-row").ToList();
            Assert.LessOrEqual(commsRows.Count, 20,
                "Comms log must show at most 20 visitor-related entries.");
        }

        [Test]
        public void CommsLog_EmptyLog_ShowsPlaceholder()
        {
            var station = VisitorsTestHelpers.MakeStation();
            _panel.Refresh(station, null);

            // No comms rows — placeholder label should be visible.
            var commsRows = _panel.Query<Label>(className: "ws-visitors-panel__comms-row").ToList();
            Assert.AreEqual(0, commsRows.Count);

            bool foundPlaceholder = false;
            var allLabels = _panel.Query<Label>().ToList();
            foreach (var label in allLabels)
            {
                if (label.text.Contains("No visitor events recorded"))
                {
                    foundPlaceholder = true;
                    break;
                }
            }
            Assert.IsTrue(foundPlaceholder,
                "Comms log section should show placeholder text when no visitor events exist.");
        }
    }
}
