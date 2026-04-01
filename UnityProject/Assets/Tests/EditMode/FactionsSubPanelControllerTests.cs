// FactionsSubPanelControllerTests.cs
// EditMode unit tests for FactionsSubPanelController (UI-015).
//
// Tests cover:
//   * RepTierLabel at boundary values (−100, −51, 0, 50, 100)
//   * RepTierLabel for acceptance-criteria value −60 → "Hostile"
//   * RepTierFilter correctly segments factions into Hostile / Neutral / Friendly buckets
//   * Filter chips show/hide rows correctly
//   * Each faction row shows the correct name, government badge, and tier label
//   * Reputation meter fill width reflects the rep value
//   * Clicking a faction row fires OnFactionRowClicked with the correct faction id
//   * Active contracts count appears on a faction row when contracts exist
//   * Refresh with null station does not throw
//   * Refresh with null FactionSystem does not throw
//   * List updates when OnFactionRepThresholdCrossed fires

using System;
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

    internal static class FactionsTestHelpers
    {
        public static StationState MakeStation()
        {
            var s = new StationState("FactionsTest");
            return s;
        }

        public static FactionDefinition MakeFaction(
            string id,
            string displayName,
            GovernmentType govType = GovernmentType.Republic)
        {
            return new FactionDefinition
            {
                id            = id,
                displayName   = displayName,
                governmentType = govType,
            };
        }

        /// <summary>
        /// Creates a minimal ContentRegistry-free FactionSystem that exposes
        /// GetAllFactions via static MergeAllFactions.  Returns (system, station)
        /// where the station already has <paramref name="factions"/> seeded with the
        /// given reputation values.
        /// </summary>
        public static (StationState station, Dictionary<string, FactionDefinition> factions)
            MakeStationWithFactions(params (string id, string name, float rep)[] entries)
        {
            var station  = MakeStation();
            var factions = new Dictionary<string, FactionDefinition>(StringComparer.Ordinal);

            foreach (var (id, name, rep) in entries)
            {
                factions[id] = MakeFaction(id, name);
                station.factionReputation[id] = rep;
            }

            // Seed generatedFactions so MergeAllFactions picks them up.
            foreach (var kv in factions)
                station.generatedFactions[kv.Key] = kv.Value;

            return (station, factions);
        }
    }

    // ── RepTierLabel boundary tests ────────────────────────────────────────────

    [TestFixture]
    internal class FactionsRepTierLabelTests
    {
        [Test]
        public void RepTierLabel_Minus100_ReturnsHostile()
        {
            Assert.AreEqual("Hostile", FactionsSubPanelController.RepTierLabel(-100f));
        }

        [Test]
        public void RepTierLabel_Minus51_ReturnsHostile()
        {
            Assert.AreEqual("Hostile", FactionsSubPanelController.RepTierLabel(-51f));
        }

        [Test]
        public void RepTierLabel_Minus60_ReturnsHostile()
        {
            // Acceptance criterion: rep = −60 → "Hostile"
            Assert.AreEqual("Hostile", FactionsSubPanelController.RepTierLabel(-60f));
        }

        [Test]
        public void RepTierLabel_Minus50_ReturnsUnfriendly()
        {
            // −50 is the Hostile/Unfriendly boundary (inclusive on Unfriendly side).
            Assert.AreEqual("Unfriendly", FactionsSubPanelController.RepTierLabel(-50f));
        }

        [Test]
        public void RepTierLabel_Zero_ReturnsNeutral()
        {
            Assert.AreEqual("Neutral", FactionsSubPanelController.RepTierLabel(0f));
        }

        [Test]
        public void RepTierLabel_Fifty_ReturnsFriendly()
        {
            Assert.AreEqual("Friendly", FactionsSubPanelController.RepTierLabel(50f));
        }

        [Test]
        public void RepTierLabel_OneHundred_ReturnsAllied()
        {
            Assert.AreEqual("Allied", FactionsSubPanelController.RepTierLabel(100f));
        }

        [Test]
        public void RepTierLabel_SeventyFive_ReturnsAllied()
        {
            Assert.AreEqual("Allied", FactionsSubPanelController.RepTierLabel(75f));
        }

        [Test]
        public void RepTierLabel_FortyNine_ReturnsNeutral()
        {
            // 49 is just below the Friendly boundary at 50, so it falls in Neutral (0 ≤ rep < 50).
            Assert.AreEqual("Neutral", FactionsSubPanelController.RepTierLabel(49f));
        }
    }

    // ── RepTierFilter tests ────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionsRepTierFilterTests
    {
        [Test]
        public void RepTierFilter_NegativeRep_ReturnsHostile()
        {
            Assert.AreEqual("hostile", FactionsSubPanelController.RepTierFilter(-60f));
            Assert.AreEqual("hostile", FactionsSubPanelController.RepTierFilter(-1f));
            Assert.AreEqual("hostile", FactionsSubPanelController.RepTierFilter(-100f));
        }

        [Test]
        public void RepTierFilter_ZeroRep_ReturnsNeutral()
        {
            Assert.AreEqual("neutral", FactionsSubPanelController.RepTierFilter(0f));
        }

        [Test]
        public void RepTierFilter_PositiveBelow50_ReturnsNeutral()
        {
            Assert.AreEqual("neutral", FactionsSubPanelController.RepTierFilter(30f));
            Assert.AreEqual("neutral", FactionsSubPanelController.RepTierFilter(49f));
        }

        [Test]
        public void RepTierFilter_FiftyAndAbove_ReturnsFriendly()
        {
            Assert.AreEqual("friendly", FactionsSubPanelController.RepTierFilter(50f));
            Assert.AreEqual("friendly", FactionsSubPanelController.RepTierFilter(75f));
            Assert.AreEqual("friendly", FactionsSubPanelController.RepTierFilter(100f));
        }
    }

    // ── Null-safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionsSubPanelNullSafetyTests
    {
        private FactionsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FactionsSubPanelController();

        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null));
        }

        [Test]
        public void Refresh_NullFactionSystem_DoesNotThrow()
        {
            var station = FactionsTestHelpers.MakeStation();
            Assert.DoesNotThrow(() => _panel.Refresh(station, null));
        }
    }

    // ── Faction rows render correctly ─────────────────────────────────────────

    [TestFixture]
    internal class FactionsSubPanelRowRenderTests
    {
        private FactionsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FactionsSubPanelController();

        [Test]
        public void FactionRow_ShowsName()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Iron Collective", 20f));

            _panel.Refresh(station, null);

            var nameLabels = _panel.Query<Label>(className: "ws-factions-panel__faction-name").ToList();
            Assert.AreEqual(1, nameLabels.Count);
            Assert.AreEqual("Iron Collective", nameLabels[0].text);
        }

        [Test]
        public void FactionRow_ShowsGovernmentBadge()
        {
            var (station, factions) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Pirate Clans", 0f));
            // Override government type to Pirate.
            factions["f1"].governmentType = GovernmentType.Pirate;
            station.generatedFactions["f1"] = factions["f1"];

            _panel.Refresh(station, null);

            var badges = _panel.Query<Label>(className: "ws-factions-panel__gov-badge").ToList();
            Assert.AreEqual(1, badges.Count);
            Assert.AreEqual("Pirate", badges[0].text);
        }

        [Test]
        public void FactionRow_ShowsTierLabel_Hostile()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Enemy Bloc", -60f));

            _panel.Refresh(station, null);

            var tierLabels = _panel.Query<Label>(className: "ws-factions-panel__tier-label").ToList();
            Assert.AreEqual(1, tierLabels.Count);
            StringAssert.Contains("Hostile", tierLabels[0].text);
        }

        [Test]
        public void FactionRow_ShowsTierLabel_Allied()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Allied Bloc", 80f));

            _panel.Refresh(station, null);

            var tierLabels = _panel.Query<Label>(className: "ws-factions-panel__tier-label").ToList();
            Assert.AreEqual(1, tierLabels.Count);
            StringAssert.Contains("Allied", tierLabels[0].text);
        }

        [Test]
        public void FactionRow_RepMeterFill_CorrectForMinusHundred()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(("f1", "Worst", -100f));
            _panel.Refresh(station, null);

            var fills = _panel.Query<VisualElement>(className: "ws-factions-panel__rep-meter-fill").ToList();
            Assert.AreEqual(1, fills.Count);
            // Fill pct for rep=−100 = (−100+100)/200 = 0%
            Assert.AreEqual(0f, fills[0].style.width.value.value, 0.01f);
        }

        [Test]
        public void FactionRow_RepMeterFill_CorrectForPlusHundred()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(("f1", "Best", 100f));
            _panel.Refresh(station, null);

            var fills = _panel.Query<VisualElement>(className: "ws-factions-panel__rep-meter-fill").ToList();
            Assert.AreEqual(1, fills.Count);
            // Fill pct for rep=+100 = (100+100)/200 = 100%
            Assert.AreEqual(100f, fills[0].style.width.value.value, 0.01f);
        }

        [Test]
        public void FactionRow_RepMeterFill_CorrectForZero()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(("f1", "Neutral", 0f));
            _panel.Refresh(station, null);

            var fills = _panel.Query<VisualElement>(className: "ws-factions-panel__rep-meter-fill").ToList();
            Assert.AreEqual(1, fills.Count);
            // Fill pct for rep=0 = (0+100)/200 = 50%
            Assert.AreEqual(50f, fills[0].style.width.value.value, 0.01f);
        }
    }

    // ── Row click event ────────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionsSubPanelRowClickTests
    {
        private FactionsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FactionsSubPanelController();

        [Test]
        public void FactionRowClick_FiresOnFactionRowClicked_WithCorrectId()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("faction.alpha", "Alpha Union", 30f));

            _panel.Refresh(station, null);

            string receivedId = null;
            _panel.OnFactionRowClicked += id => receivedId = id;

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(1, rows.Count, "Expected exactly one faction row.");

            using var evt = ClickEvent.GetPooled();
            evt.target = rows[0];
            rows[0].SendEvent(evt);

            Assert.IsNotNull(receivedId, "Row click should invoke OnFactionRowClicked.");
            Assert.AreEqual("faction.alpha", receivedId,
                "OnFactionRowClicked should receive the row's faction id.");
        }
    }

    // ── Filter chips ──────────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionsSubPanelFilterTests
    {
        private FactionsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FactionsSubPanelController();

        [Test]
        public void AllFilter_ShowsAllFactions()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Hostile Faction",  -80f),
                ("f2", "Neutral Faction",   10f),
                ("f3", "Friendly Faction",  70f));

            _panel.Refresh(station, null);
            // Default filter is "all"
            Assert.AreEqual("all", _panel.ActiveFilter);

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(3, rows.Count, "All filter should show all 3 factions.");
        }

        [Test]
        public void HostileFilter_ShowsOnlyNegativeRepFactions()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Enemy",    -60f),
                ("f2", "Cautious", -10f),
                ("f3", "Neutral",   20f),
                ("f4", "Allied",    80f));

            _panel.Refresh(station, null);

            // Simulate clicking the Hostile filter chip.
            var chips = _panel.Query<Label>(className: "ws-factions-panel__filter-chip").ToList();
            Label hostileChip = null;
            foreach (var c in chips)
                if (c.text == "Hostile") { hostileChip = c; break; }

            Assert.IsNotNull(hostileChip, "Hostile filter chip should exist.");
            using var evt = ClickEvent.GetPooled();
            evt.target = hostileChip;
            hostileChip.SendEvent(evt);

            Assert.AreEqual("hostile", _panel.ActiveFilter);

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(2, rows.Count,
                "Hostile filter should show only the 2 factions with negative rep.");
        }

        [Test]
        public void NeutralFilter_ShowsOnlyNeutralTierFactions()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Hostile",  -60f),
                ("f2", "Neutral1",   0f),
                ("f3", "Neutral2",  25f),
                ("f4", "Friendly",  60f));

            _panel.Refresh(station, null);

            var chips = _panel.Query<Label>(className: "ws-factions-panel__filter-chip").ToList();
            Label neutralChip = null;
            foreach (var c in chips)
                if (c.text == "Neutral") { neutralChip = c; break; }

            Assert.IsNotNull(neutralChip, "Neutral filter chip should exist.");
            using var evt = ClickEvent.GetPooled();
            evt.target = neutralChip;
            neutralChip.SendEvent(evt);

            Assert.AreEqual("neutral", _panel.ActiveFilter);

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(2, rows.Count,
                "Neutral filter should show only factions with 0 ≤ rep < 50.");
        }

        [Test]
        public void FriendlyFilter_ShowsOnlyPositiveRepFactions()
        {
            // Acceptance criterion: Friendly filter → only factions with positive reputation (rep ≥ 50)
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Enemy",    -80f),
                ("f2", "Neutral",    0f),
                ("f3", "Friendly",  55f),
                ("f4", "Allied",    90f));

            _panel.Refresh(station, null);

            var chips = _panel.Query<Label>(className: "ws-factions-panel__filter-chip").ToList();
            Label friendlyChip = null;
            foreach (var c in chips)
                if (c.text == "Friendly") { friendlyChip = c; break; }

            Assert.IsNotNull(friendlyChip, "Friendly filter chip should exist.");
            using var evt = ClickEvent.GetPooled();
            evt.target = friendlyChip;
            friendlyChip.SendEvent(evt);

            Assert.AreEqual("friendly", _panel.ActiveFilter);

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(2, rows.Count,
                "Friendly filter should show only factions with positive reputation (rep ≥ 50).");
        }

        [Test]
        public void EmptyResult_NoMatchingFilter_ShowsEmptyMessage()
        {
            // Only hostile factions; switch to Friendly filter → empty list.
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Enemy", -80f));

            _panel.Refresh(station, null);

            var chips = _panel.Query<Label>(className: "ws-factions-panel__filter-chip").ToList();
            Label friendlyChip = null;
            foreach (var c in chips)
                if (c.text == "Friendly") { friendlyChip = c; break; }

            using var evt = ClickEvent.GetPooled();
            evt.target = friendlyChip;
            friendlyChip.SendEvent(evt);

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(0, rows.Count, "No faction rows should be visible under Friendly filter.");
        }
    }

    // ── Active contracts count ─────────────────────────────────────────────────

    [TestFixture]
    internal class FactionsSubPanelContractsTests
    {
        private FactionsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FactionsSubPanelController();

        [Test]
        public void FactionRow_ShowsContractsLabel_WhenContractsExist()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Trade Guild", 40f));

            // Add an active contract for this faction.
            station.factionContracts["c1"] = new FactionContract
            {
                contractId          = "c1",
                factionId           = "f1",
                creditPerPayment    = 100f,
                paymentIntervalTicks = 10,
            };

            _panel.Refresh(station, null);

            var contractsLabels = _panel.Query<Label>(className: "ws-factions-panel__contracts").ToList();
            Assert.AreEqual(1, contractsLabels.Count,
                "Contracts label should be present when the faction has active contracts.");
            StringAssert.Contains("1 contract", contractsLabels[0].text);
        }

        [Test]
        public void FactionRow_NoContractsLabel_WhenNoContracts()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Trade Guild", 40f));

            _panel.Refresh(station, null);

            var contractsLabels = _panel.Query<Label>(className: "ws-factions-panel__contracts").ToList();
            Assert.AreEqual(0, contractsLabels.Count,
                "Contracts label should NOT be present when the faction has no contracts.");
        }
    }

    // ── Integration: list updates on OnReputationChanged ─────────────────────

    [TestFixture]
    internal class FactionsSubPanelReputationChangedTests
    {
        private FactionsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new FactionsSubPanelController();

        [Test]
        public void AfterReputationChange_Refresh_UpdatesTierLabel()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Shifting Faction", -60f));

            _panel.Refresh(station, null);

            var tierLabels = _panel.Query<Label>(className: "ws-factions-panel__tier-label").ToList();
            StringAssert.Contains("Hostile", tierLabels[0].text,
                "Initially the tier label should show Hostile for rep=−60.");

            // Simulate the faction rep improving — cross into Neutral tier.
            station.factionReputation["f1"] = 20f;

            // Re-fresh (mimics what OnFactionRepThresholdCrossed would trigger).
            _panel.Refresh(station, null);

            tierLabels = _panel.Query<Label>(className: "ws-factions-panel__tier-label").ToList();
            StringAssert.Contains("Neutral", tierLabels[0].text,
                "After rep improves to 20, tier label should show Neutral.");
        }

        [Test]
        public void MultipleRefreshCalls_DoNotDuplicateRows()
        {
            var (station, _) = FactionsTestHelpers.MakeStationWithFactions(
                ("f1", "Faction A", 10f));

            _panel.Refresh(station, null);
            _panel.Refresh(station, null);
            _panel.Refresh(station, null);

            var rows = _panel.Query<VisualElement>(className: "ws-factions-panel__faction-row").ToList();
            Assert.AreEqual(1, rows.Count,
                "Multiple Refresh calls should not duplicate faction rows.");
        }
    }
}
