// FactionDetailPanelControllerTests.cs
// EditMode unit tests for FactionDetailPanelController (UI-026).
//
// Tests cover:
//   * Vassalized indicator (vassal note section) appears only for CorporateVassal government type
//   * Patron faction name is shown in the vassal section
//   * Stability factors (economic, military, mood, tenure) appear when expanded
//   * History log shows all entries with gameTick (up to 10)
//   * History log limited to last 10 events when more than 10 are present
//   * Empty history shows "No faction history recorded."
//   * GovernmentAxisLabels returns correct axis labels per government type
//   * Reputation change history shows last 5 changes
//   * Refresh with null station does not throw
//   * Refresh with null FactionSystem does not throw
//   * OnCloseRequested fires when close button is clicked

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

    internal static class FactionDetailTestHelpers
    {
        public static StationState MakeStation(string name = "FactionDetailTest")
            => new StationState(name);

        public static FactionDefinition MakeFaction(
            string id,
            string displayName,
            GovernmentType govType = GovernmentType.Republic,
            string vassalParentId  = null)
        {
            return new FactionDefinition
            {
                id                  = id,
                displayName         = displayName,
                governmentType      = govType,
                vassalParentFactionId = vassalParentId,
                type                = "minor",
                description         = $"Description of {displayName}.",
            };
        }

        /// <summary>
        /// Builds a FactionSystem-free stub that reads from station.generatedFactions only.
        /// </summary>
        public static FactionDefinition AddFactionToStation(
            StationState station,
            string id,
            string displayName,
            GovernmentType govType    = GovernmentType.Republic,
            string vassalParentId     = null,
            float rep                 = 0f)
        {
            var def = MakeFaction(id, displayName, govType, vassalParentId);
            station.generatedFactions[id] = def;
            station.factionReputation[id] = rep;
            return def;
        }

        public static HistoricalEvent MakeEvent(string description, int gameTick = 1)
            => new HistoricalEvent
            {
                eventId    = "evt.test",
                description = description,
                gameTick   = gameTick,
                involvedFactionIds = System.Array.Empty<string>(),
            };
    }

    // ── GovernmentAxisLabels tests ─────────────────────────────────────────────

    [TestFixture]
    internal class FactionDetailGovernmentAxisTests
    {
        [TestCase(GovernmentType.Democracy,      "Distributed",  "Consensual")]
        [TestCase(GovernmentType.Republic,        "Balanced",     "Earned")]
        [TestCase(GovernmentType.Monarchy,        "Centralised",  "Traditional")]
        [TestCase(GovernmentType.Authoritarian,   "Centralised",  "Coercive")]
        [TestCase(GovernmentType.CorporateVassal, "Balanced",     "Economic")]
        [TestCase(GovernmentType.Pirate,          "Anarchic",     "None")]
        [TestCase(GovernmentType.Theocracy,       "Centralised",  "Divine Mandate")]
        [TestCase(GovernmentType.Technocracy,     "Balanced",     "Merit-Based")]
        [TestCase(GovernmentType.FederalCouncil,  "Distributed",  "Accepted Order")]
        public void GovernmentAxisLabels_ReturnsCorrectLabels(
            GovernmentType govType, string expectedPower, string expectedLeg)
        {
            var (powerAxis, legAxis) = FactionDetailPanelController.GovernmentAxisLabels(govType);
            Assert.AreEqual(expectedPower, powerAxis, $"Power axis mismatch for {govType}");
            Assert.AreEqual(expectedLeg,   legAxis,   $"Legitimacy axis mismatch for {govType}");
        }
    }

    // ── Vassalized indicator tests ─────────────────────────────────────────────

    [TestFixture]
    internal class FactionDetailVassalSectionTests
    {
        [Test]
        public void VassalSection_CorporateVassalFaction_IsVisible()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            string patronId = "faction.patron";
            string vassalId = "faction.vassal";

            FactionDetailTestHelpers.AddFactionToStation(
                station, patronId, "The Corp", GovernmentType.Democracy);
            FactionDetailTestHelpers.AddFactionToStation(
                station, vassalId, "Vassal Co",
                GovernmentType.CorporateVassal, vassalParentId: patronId);

            var panel = new FactionDetailPanelController();
            panel.Refresh(vassalId, station, null, null);

            // The vassal note element has USS class ws-faction-detail-panel__vassal-note.
            var vassalNote = panel.Q(className: "ws-faction-detail-panel__vassal-note");
            Assert.IsNotNull(vassalNote,
                "Vassal note section must be visible for CorporateVassal factions.");
        }

        [Test]
        public void VassalSection_NonVassalFaction_IsHidden()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            FactionDetailTestHelpers.AddFactionToStation(
                station, "faction.rep", "The Republic", GovernmentType.Republic);

            var panel = new FactionDetailPanelController();
            panel.Refresh("faction.rep", station, null, null);

            var vassalNote = panel.Q(className: "ws-faction-detail-panel__vassal-note");
            Assert.IsNull(vassalNote,
                "Vassal note section must NOT appear for non-CorporateVassal factions.");
        }

        [Test]
        public void VassalSection_ShowsPatronFactionName()
        {
            var station  = FactionDetailTestHelpers.MakeStation();
            string patronId = "faction.parent";
            FactionDetailTestHelpers.AddFactionToStation(
                station, patronId, "Parent Corp", GovernmentType.Democracy);
            FactionDetailTestHelpers.AddFactionToStation(
                station, "faction.child", "Child Vassal",
                GovernmentType.CorporateVassal, vassalParentId: patronId);

            var panel = new FactionDetailPanelController();
            panel.Refresh("faction.child", station, null, null);

            // The patron name should appear somewhere in the panel text.
            bool foundPatronName = false;
            panel.Query<Label>().ForEach(lbl =>
            {
                if (lbl.text != null && lbl.text.Contains("Parent Corp"))
                    foundPatronName = true;
            });
            Assert.IsTrue(foundPatronName,
                "The patron faction display name ('Parent Corp') must appear in the vassal section.");
        }
    }

    // ── Stability factors (expandable) tests ───────────────────────────────────

    [TestFixture]
    internal class FactionDetailStabilityFactorTests
    {
        [Test]
        public void StabilityFactors_WhenExpanded_ShowsAllFourFactors()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            FactionDetailTestHelpers.AddFactionToStation(
                station, "faction.test", "Test Faction");

            var panel = new FactionDetailPanelController();
            panel.Refresh("faction.test", station, null, null);

            // Click the stability section header row to expand factors.
            // The row containing the "▼" toggle label is the expand trigger.
            VisualElement expandRow = null;
            panel.Query<VisualElement>().ForEach(el =>
            {
                if (expandRow != null) return;
                bool hasToggle = false;
                el.Query<Label>().ForEach(lbl =>
                {
                    if (lbl.text == "▼" || lbl.text == "▲") hasToggle = true;
                });
                if (hasToggle) expandRow = el;
            });

            if (expandRow != null)
            {
                using var evt = ClickEvent.GetPooled();
                evt.target = expandRow;
                expandRow.SendEvent(evt);
            }

            // Re-refresh to pick up the expanded state (simulating what click event does).
            panel.Refresh("faction.test", station, null, null);

            // After expansion, factor rows with these labels should be visible.
            var allLabels = new List<string>();
            panel.Query<Label>().ForEach(lbl => { if (lbl.text != null) allLabels.Add(lbl.text); });

            Assert.IsTrue(allLabels.Exists(l => l.Contains("Economic")),
                "Expected 'Economic Prosperity' factor row.");
            Assert.IsTrue(allLabels.Exists(l => l.Contains("Military")),
                "Expected 'Military Strength' factor row.");
            Assert.IsTrue(allLabels.Exists(l => l.Contains("Mood") || l.Contains("Cohesion")),
                "Expected 'Mood/Cohesion' factor row.");
            Assert.IsTrue(allLabels.Exists(l => l.Contains("Tenure")),
                "Expected 'Tenure' factor row.");
        }
    }

    // ── History log tests ──────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionDetailHistoryLogTests
    {
        [Test]
        public void HistoryLog_FiveEntries_ShowsAllFiveWithTimestamps()
        {
            var station  = FactionDetailTestHelpers.MakeStation();
            string id    = "faction.hist";
            FactionDetailTestHelpers.AddFactionToStation(station, id, "History Faction");

            var histProvider = new FactionHistory();
            for (int i = 1; i <= 5; i++)
                histProvider.RecordFactionEvent(
                    id, FactionDetailTestHelpers.MakeEvent($"Event {i}", gameTick: i * 10));

            var panel = new FactionDetailPanelController();
            panel.Refresh(id, station, null, histProvider);

            // Verify all 5 gameTick timestamps are shown.
            for (int i = 1; i <= 5; i++)
            {
                int tick = i * 10;
                bool found = false;
                panel.Query<Label>().ForEach(lbl =>
                {
                    if (lbl.text != null && lbl.text.Contains($"Tick {tick}"))
                        found = true;
                });
                Assert.IsTrue(found, $"Expected 'Tick {tick}' to appear in the history log.");
            }
        }

        [Test]
        public void HistoryLog_MoreThanTenEntries_ShowsAtMostTen()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            string id   = "faction.hist2";
            FactionDetailTestHelpers.AddFactionToStation(station, id, "Many Events Faction");

            var histProvider = new FactionHistory();
            for (int i = 1; i <= 15; i++)
                histProvider.RecordFactionEvent(
                    id, FactionDetailTestHelpers.MakeEvent($"Event {i}", gameTick: i));

            var panel = new FactionDetailPanelController();
            panel.Refresh(id, station, null, histProvider);

            int tickLabelCount = 0;
            panel.Query<Label>().ForEach(lbl =>
            {
                if (lbl.text != null && lbl.text.StartsWith("Tick "))
                    tickLabelCount++;
            });

            Assert.LessOrEqual(tickLabelCount, 10,
                "History log must show at most 10 entries.");
            Assert.Greater(tickLabelCount, 0,
                "History log must show at least 1 entry when events are present.");
        }

        [Test]
        public void HistoryLog_NoEntries_ShowsEmptyMessage()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            string id   = "faction.empty";
            FactionDetailTestHelpers.AddFactionToStation(station, id, "Empty Faction");

            var histProvider = new FactionHistory();   // no events recorded

            var panel = new FactionDetailPanelController();
            panel.Refresh(id, station, null, histProvider);

            bool foundEmpty = false;
            panel.Query<Label>().ForEach(lbl =>
            {
                if (lbl.text != null && lbl.text.Contains("No faction history"))
                    foundEmpty = true;
            });
            Assert.IsTrue(foundEmpty,
                "Should show 'No faction history recorded.' when no events are present.");
        }

        [Test]
        public void HistoryLog_NullHistoryProvider_ShowsEmptyMessage()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            string id   = "faction.null";
            FactionDetailTestHelpers.AddFactionToStation(station, id, "Null Provider Faction");

            var panel = new FactionDetailPanelController();
            panel.Refresh(id, station, null, null);   // null history provider

            bool foundEmpty = false;
            panel.Query<Label>().ForEach(lbl =>
            {
                if (lbl.text != null && lbl.text.Contains("No faction history"))
                    foundEmpty = true;
            });
            Assert.IsTrue(foundEmpty,
                "Should show empty message when history provider is null.");
        }
    }

    // ── Reputation history tests ────────────────────────────────────────────────

    [TestFixture]
    internal class FactionDetailRepHistoryTests
    {
        [Test]
        public void RepHistory_FiveChanges_ShowsAllFive()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            string id   = "faction.rep";
            FactionDetailTestHelpers.AddFactionToStation(station, id, "Rep Faction");

            // Record 5 rep changes by modifying rep directly.
            for (int i = 1; i <= 5; i++)
            {
                station.tick = i * 100;
                station.ModifyFactionRep(id, i % 2 == 0 ? 5f : -3f);
            }

            var panel = new FactionDetailPanelController();
            panel.Refresh(id, station, null, null);

            // Count delta labels (they start with '+' or '−').
            int deltaCount = 0;
            panel.Query<Label>().ForEach(lbl =>
            {
                if (lbl.text != null &&
                    (lbl.text.StartsWith("+") || lbl.text.StartsWith("−") || lbl.text.StartsWith("-")))
                    deltaCount++;
            });

            // Expect at least 5 delta labels (rep history changes) in the rep section.
            Assert.GreaterOrEqual(deltaCount, 5,
                "Expected at least 5 reputation change entries in the history section.");
        }
    }

    // ── Null safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionDetailPanelNullSafetyTests
    {
        [Test]
        public void Refresh_NullStation_DoesNotThrow()
        {
            var panel = new FactionDetailPanelController();
            Assert.DoesNotThrow(() => panel.Refresh("faction.test", null, null, null));
        }

        [Test]
        public void Refresh_NullFactionSystem_DoesNotThrow()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            FactionDetailTestHelpers.AddFactionToStation(station, "faction.test", "Test");
            var panel = new FactionDetailPanelController();
            Assert.DoesNotThrow(() => panel.Refresh("faction.test", station, null, null));
        }

        [Test]
        public void Refresh_UnknownFactionId_DoesNotThrow()
        {
            var station = FactionDetailTestHelpers.MakeStation();
            var panel   = new FactionDetailPanelController();
            Assert.DoesNotThrow(() => panel.Refresh("no-such-faction", station, null, null));
        }
    }

    // ── OnCloseRequested ──────────────────────────────────────────────────────

    [TestFixture]
    internal class FactionDetailPanelCloseTests
    {
        [Test]
        public void CloseButton_Click_FiresOnCloseRequested()
        {
            var panel  = new FactionDetailPanelController();
            bool fired = false;
            panel.OnCloseRequested += () => fired = true;

            var closeBtn = panel.Q<Button>(className: "ws-faction-detail-panel__close-btn");
            Assert.IsNotNull(closeBtn, "Close button should exist.");

            using var evt = ClickEvent.GetPooled();
            evt.target = closeBtn;
            closeBtn.SendEvent(evt);

            Assert.IsTrue(fired, "OnCloseRequested should fire when close button is clicked.");
        }
    }
}
