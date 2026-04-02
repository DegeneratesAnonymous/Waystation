// VisitingShipPanelControllerTests.cs
// EditMode unit tests for VisitingShipPanelController (UI-026).
//
// Tests cover:
//   * Trade manifest button only appears for Trader ships with a trade offer
//   * Trade manifest button does NOT appear for non-Trader ships
//   * Trade manifest button does NOT appear for Trader ships without a trade offer
//   * Docking tab renders Grant/Deny/Negotiate buttons for pending decisions
//   * Docking tab does NOT render decision buttons for non-pending ships
//   * "Request departure" button appears only when ship is docked
//   * ActivityLabel maps location strings to the correct activity labels
//   * RoleBadgeLabel maps role strings correctly
//   * Refresh with null station does not throw
//   * Refresh with null VisitorSystem does not throw
//   * OnCloseRequested fires when close button is clicked

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

    internal static class VisitingShipTestHelpers
    {
        public static StationState MakeStation(string name = "VisitingShipTest")
            => new StationState(name);

        public static ShipInstance MakeShip(
            string role = "trader",
            string status = "docked",
            string intent = "trade",
            string uid = null)
        {
            var ship = ShipInstance.Create("ship.test", "ISV Test", role, intent, null, 0);
            ship.status = status;
            if (uid != null) ship.uid = uid;
            return ship;
        }

        /// <summary>
        /// Builds a minimal VisitorSystem backed by a stub registry.
        /// NPCSystem is null — only PendingDecisions is exercised.
        /// Callers are responsible for destroying <paramref name="registryGo"/>.
        /// </summary>
        public static (VisitorSystem visitors, GameObject registryGo) MakeVisitorSystem()
        {
            var go       = new GameObject("VisitingShipTestRegistry");
            var registry = go.AddComponent<ContentRegistry>();
            var eventStub = new EventStubRegistry();
            var evtSys   = new EventSystem(eventStub, "normal");
            var visitors = new VisitorSystem(registry, null, evtSys);
            return (visitors, go);
        }
    }

    // ── ActivityLabel tests ────────────────────────────────────────────────────

    [TestFixture]
    internal class VisitingShipActivityLabelTests
    {
        [TestCase("hangar_bay",    "In hangar")]
        [TestCase("dock_alpha",    "In hangar")]
        [TestCase("shop_level1",   "At shop")]
        [TestCase("trade_hub",     "At shop")]
        [TestCase("medical_ward",  "In medical bay")]
        [TestCase("med_center",    "In medical bay")]
        [TestCase("commons",       "Wandering")]
        [TestCase("cafeteria",     "Wandering")]
        public void ActivityLabel_MapsLocationToExpectedActivity(string location, string expected)
        {
            Assert.AreEqual(expected, VisitingShipPanelController.ActivityLabel(location));
        }

        [Test]
        public void ActivityLabel_NullLocation_ReturnsWandering()
        {
            Assert.AreEqual("Wandering", VisitingShipPanelController.ActivityLabel(null));
        }

        [Test]
        public void ActivityLabel_EmptyLocation_ReturnsWandering()
        {
            Assert.AreEqual("Wandering", VisitingShipPanelController.ActivityLabel(""));
        }
    }

    // ── RoleBadgeLabel tests ───────────────────────────────────────────────────

    [TestFixture]
    internal class VisitingShipRoleBadgeLabelTests
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
            Assert.AreEqual(expected, VisitingShipPanelController.RoleBadgeLabel(role));
        }

        [Test]
        public void RoleBadgeLabel_NullRole_ReturnsUnknown()
        {
            Assert.AreEqual("Unknown", VisitingShipPanelController.RoleBadgeLabel(null));
        }

        [Test]
        public void RoleBadgeLabel_UnknownRole_ReturnsThatRoleString()
        {
            Assert.AreEqual("diplomat", VisitingShipPanelController.RoleBadgeLabel("diplomat"));
        }
    }

    // ── Docking tab: trade manifest button ────────────────────────────────────

    [TestFixture]
    internal class VisitingShipTradeManifestButtonTests
    {
        [Test]
        public void DockingTab_TraderWithTradeOffer_ShowsTradeManifestButton()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(role: "trader", status: "docked");
            station.ships[ship.uid]     = ship;
            station.tradeOffers[ship.uid] = new object();   // any non-null object

            var panel = new VisitingShipPanelController();
            panel.Refresh(ship.uid, station, null, null);

            // Select the Docking tab manually (it is the 3rd tab, key = "docking").
            // The panel creates tabs in order: info(0), crew(1), docking(2).
            // Access via TabStrip selection.
            var tabStrip = panel.Q<TabStrip>();
            tabStrip?.SelectTab("docking");

            var tradeBtn = panel.Q<Button>("btn-trade-manifest");
            Assert.IsNotNull(tradeBtn,
                "Expected 'Open trade manifest' button for a Trader ship with a trade offer.");
        }

        [Test]
        public void DockingTab_TraderWithoutTradeOffer_HidesTradeManifestButton()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(role: "trader", status: "docked");
            station.ships[ship.uid] = ship;
            // No entry in station.tradeOffers.

            var panel = new VisitingShipPanelController();
            panel.Refresh(ship.uid, station, null, null);

            var tabStrip = panel.Q<TabStrip>();
            tabStrip?.SelectTab("docking");

            var tradeBtn = panel.Q<Button>("btn-trade-manifest");
            Assert.IsNull(tradeBtn,
                "Trade manifest button must NOT appear when no trade offer is present.");
        }

        [Test]
        public void DockingTab_NonTraderWithTradeOffer_HidesTradeManifestButton()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(role: "refugee", status: "docked");
            station.ships[ship.uid]     = ship;
            station.tradeOffers[ship.uid] = new object();   // has trade offer but wrong role

            var panel = new VisitingShipPanelController();
            panel.Refresh(ship.uid, station, null, null);

            var tabStrip = panel.Q<TabStrip>();
            tabStrip?.SelectTab("docking");

            var tradeBtn = panel.Q<Button>("btn-trade-manifest");
            Assert.IsNull(tradeBtn,
                "Trade manifest button must NOT appear for non-Trader ships regardless of trade offer.");
        }

        [Test]
        public void DockingTab_InspectorRole_HidesTradeManifestButton()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(role: "inspector", status: "docked", intent: "inspect");
            station.ships[ship.uid]     = ship;
            station.tradeOffers[ship.uid] = new object();   // inspector has offer entry (edge case)

            var panel = new VisitingShipPanelController();
            panel.Refresh(ship.uid, station, null, null);

            var tabStrip = panel.Q<TabStrip>();
            tabStrip?.SelectTab("docking");

            var tradeBtn = panel.Q<Button>("btn-trade-manifest");
            Assert.IsNull(tradeBtn,
                "Trade manifest button must NOT appear for Inspector ships.");
        }
    }

    // ── Docking tab: Grant / Deny / Negotiate buttons ─────────────────────────

    [TestFixture]
    internal class VisitingShipDockingDecisionButtonTests
    {
        [Test]
        public void DockingTab_PendingShip_ShowsDecisionButtons()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(status: "incoming");
            station.ships[ship.uid] = ship;

            var (visitorSys, go) = VisitingShipTestHelpers.MakeVisitorSystem();
            try
            {
                visitorSys.PendingDecisions.Add(ship.uid);

                var panel = new VisitingShipPanelController();
                panel.Refresh(ship.uid, station, visitorSys, null);

                var tabStrip = panel.Q<TabStrip>();
                tabStrip?.SelectTab("docking");

                // Grant / Deny / Negotiate buttons should exist.
                var buttons = panel.Query<Button>().ToList();
                bool hasGrant     = false;
                bool hasDeny      = false;
                bool hasNegotiate = false;
                foreach (var b in buttons)
                {
                    if (b.text == "Grant")     hasGrant     = true;
                    if (b.text == "Deny")      hasDeny      = true;
                    if (b.text == "Negotiate") hasNegotiate = true;
                }

                Assert.IsTrue(hasGrant,     "Expected Grant button for pending ship.");
                Assert.IsTrue(hasDeny,      "Expected Deny button for pending ship.");
                Assert.IsTrue(hasNegotiate, "Expected Negotiate button for pending ship.");
            }
            finally { GameObject.DestroyImmediate(go); }
        }

        [Test]
        public void DockingTab_NonPendingShip_HidesDecisionButtons()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(status: "docked");
            station.ships[ship.uid] = ship;

            var (visitorSys, go) = VisitingShipTestHelpers.MakeVisitorSystem();
            try
            {
                // NOT in PendingDecisions.
                var panel = new VisitingShipPanelController();
                panel.Refresh(ship.uid, station, visitorSys, null);

                var tabStrip = panel.Q<TabStrip>();
                tabStrip?.SelectTab("docking");

                var buttons = panel.Query<Button>().ToList();
                foreach (var b in buttons)
                {
                    Assert.AreNotEqual("Grant",     b.text, "Grant button must not appear for non-pending ship.");
                    Assert.AreNotEqual("Deny",      b.text, "Deny button must not appear for non-pending ship.");
                    Assert.AreNotEqual("Negotiate", b.text, "Negotiate button must not appear for non-pending ship.");
                }
            }
            finally { GameObject.DestroyImmediate(go); }
        }
    }

    // ── Docking tab: request departure button ─────────────────────────────────

    [TestFixture]
    internal class VisitingShipRequestDepartureTests
    {
        [Test]
        public void DockingTab_DockedShip_ShowsRequestDepartureButton()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(status: "docked");
            station.ships[ship.uid] = ship;

            var panel = new VisitingShipPanelController();
            panel.Refresh(ship.uid, station, null, null);

            var tabStrip = panel.Q<TabStrip>();
            tabStrip?.SelectTab("docking");

            var btn = panel.Q<Button>("btn-request-departure");
            Assert.IsNotNull(btn, "Expected 'Request departure' button for docked ship.");
        }

        [Test]
        public void DockingTab_IncomingShip_HidesRequestDepartureButton()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip(status: "incoming");
            station.ships[ship.uid] = ship;

            var panel = new VisitingShipPanelController();
            panel.Refresh(ship.uid, station, null, null);

            var tabStrip = panel.Q<TabStrip>();
            tabStrip?.SelectTab("docking");

            var btn = panel.Q<Button>("btn-request-departure");
            Assert.IsNull(btn, "'Request departure' button must not appear for incoming ships.");
        }
    }

    // ── Null safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class VisitingShipPanelNullSafetyTests
    {
        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            var panel = new VisitingShipPanelController();
            Assert.DoesNotThrow(() => panel.Refresh("uid", null, null, null));
        }

        [Test]
        public void Refresh_NullVisitorSystem_DoesNotThrow()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var ship    = VisitingShipTestHelpers.MakeShip();
            station.ships[ship.uid] = ship;

            var panel = new VisitingShipPanelController();
            Assert.DoesNotThrow(() => panel.Refresh(ship.uid, station, null, null));
        }

        [Test]
        public void Refresh_UnknownShipUid_DoesNotThrow()
        {
            var station = VisitingShipTestHelpers.MakeStation();
            var panel   = new VisitingShipPanelController();
            Assert.DoesNotThrow(() => panel.Refresh("no-such-uid", station, null, null));
        }
    }

    // ── OnCloseRequested ──────────────────────────────────────────────────────

    [TestFixture]
    internal class VisitingShipPanelCloseTests
    {
        [Test]
        public void CloseButton_Click_FiresOnCloseRequested()
        {
            var panel    = new VisitingShipPanelController();
            bool fired   = false;
            panel.OnCloseRequested += () => fired = true;

            // The close button is the button with text "✕" in the header.
            var closeBtn = panel.Q<Button>(className: "ws-visiting-ship-panel__close-btn");
            Assert.IsNotNull(closeBtn, "Close button should exist.");

            using var evt = ClickEvent.GetPooled();
            evt.target = closeBtn;
            closeBtn.SendEvent(evt);

            Assert.IsTrue(fired, "OnCloseRequested should fire when close button is clicked.");
        }
    }
}
