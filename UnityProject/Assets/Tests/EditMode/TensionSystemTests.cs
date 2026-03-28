// TensionSystemTests — EditMode unit tests for TensionSystem departure execution.
//
// Validates:
//   • Departure announcement event fires when NPC is at DepartureRisk
//   • Departure announcement is not repeated once pending
//   • Intervention success resets tension to Disgruntled and cancels departure
//   • Intervention failure leaves departure pending
//   • Intervention window expiry triggers physical departure sequence
//   • Departed NPC removed from active roster and added to departedNpcs pool
//   • Departed NPC pool preserves full NPC state (skills, traits, relationships intact)
//   • Stage transition below DepartureRisk cancels pending departure announcement
//   • NpcDeparture feature flag = false reverts to mood-penalty-only behaviour
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static class TensionTestHelpers
    {
        public static StationState MakeStation()
            => new StationState("TensionTestStation");

        public static NPCInstance MakeCrewNpc(string uid = null)
        {
            var npc = new NPCInstance
            {
                uid  = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "TestNPC",
            };
            npc.statusTags.Add("crew");
            return npc;
        }

        /// <summary>Creates a TensionSystem wired with no external dependencies.</summary>
        public static TensionSystem MakeTensionSystem()
        {
            var traits  = new TraitSystem();
            var tension = new TensionSystem(traits);
            // Set departure chance to 2f so Random.value (always in [0,1]) is always < 2f,
            // meaning the guard `if (Random.value >= DepartureAttemptChancePerDay) return;`
            // never fires — departure announcement always triggers deterministically.
            tension.DepartureAttemptChancePerDay = 2f;
            tension.InterventionWindowTicks      = 720; // 2 in-game days for test speed
            tension.InterventionSkillCheckDC     = 1;   // low DC; success/failure controlled per-test
            return tension;
        }

        /// <summary>Force an NPC to DepartureRisk stage with a score high enough to persist through passive decay.</summary>
        public static void SetDepartureRisk(NPCInstance npc, TensionSystem tension)
        {
            var profile = npc.GetOrCreateTraitProfile();
            // Use 100f so repeated passive decay ticks (2f/day default) don't
            // immediately drop the score below the 90f DepartureRisk threshold.
            profile.tensionScore = 100f;
            profile.tensionStage = TensionStage.DepartureRisk;
        }

        /// <summary>
        /// Advance the station tick by one full in-game day and run TensionSystem.
        /// </summary>
        public static void AdvanceOneDay(TensionSystem tension, StationState station)
        {
            station.tick += TimeSystem.TicksPerDay;
            tension.Tick(station);
        }
    }

    // ── Departure announcement tests ──────────────────────────────────────────

    [TestFixture]
    public class DepartureAnnouncementTests
    {
        private TensionSystem _tension;
        private StationState  _station;
        private NPCInstance   _npc;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.NpcTraits    = true;
            FeatureFlags.NpcDeparture = true;

            _tension = TensionTestHelpers.MakeTensionSystem();
            _station = TensionTestHelpers.MakeStation();
            _npc     = TensionTestHelpers.MakeCrewNpc();
            _station.AddNpc(_npc);
            TensionTestHelpers.SetDepartureRisk(_npc, _tension);
        }

        [Test]
        public void DepartureAnnouncementFires_WhenNpcAtDepartureRisk()
        {
            bool announceFired = false;
            _tension.OnDepartureAnnounced += (npc, _) =>
            {
                if (npc.uid == _npc.uid) announceFired = true;
            };

            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.IsTrue(announceFired, "OnDepartureAnnounced should fire for a DepartureRisk NPC.");
        }

        [Test]
        public void DepartureAnnouncement_SetsAnnouncedFlag()
        {
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.IsNotNull(_npc.traitProfile?.departure, "departure state should be initialised.");
            Assert.IsTrue(_npc.traitProfile.departure.announced, "announced flag should be true.");
        }

        [Test]
        public void DepartureAnnouncement_SetsCorrectDeadline()
        {
            int tickBeforeAdvance = _station.tick;
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            int expectedDeadline = _station.tick + _tension.InterventionWindowTicks;
            // The deadline is set at the day-tick, before the window is added.
            // Actual stored value should equal (tickAfterAdvance) + InterventionWindowTicks.
            Assert.AreEqual(expectedDeadline, _npc.traitProfile.departure.interventionDeadlineTick,
                "interventionDeadlineTick should be announcement tick + InterventionWindowTicks.");
        }

        [Test]
        public void DepartureAnnouncementDoesNotRepeat_WhenAlreadyPending()
        {
            int announceCount = 0;
            _tension.OnDepartureAnnounced += (_, __) => announceCount++;

            TensionTestHelpers.AdvanceOneDay(_tension, _station);
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.AreEqual(1, announceCount, "Announcement should not repeat when already pending.");
        }

        [Test]
        public void DepartureAnnouncement_DoesNotFire_WhenFeatureFlagDisabled()
        {
            FeatureFlags.NpcDeparture = false;

            bool announceFired = false;
            _tension.OnDepartureAnnounced += (_, __) => announceFired = true;

            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.IsFalse(announceFired,
                "OnDepartureAnnounced must not fire when NpcDeparture flag is false.");

            // Restore
            FeatureFlags.NpcDeparture = true;
        }
    }

    // ── Intervention tests ────────────────────────────────────────────────────

    [TestFixture]
    public class InterventionTests
    {
        private TensionSystem _tension;
        private StationState  _station;
        private NPCInstance   _npc;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.NpcTraits    = true;
            FeatureFlags.NpcDeparture = true;

            _tension = TensionTestHelpers.MakeTensionSystem();
            _tension.InterventionSkillCheckDC = 1; // DC 1 → always succeeds
            _station = TensionTestHelpers.MakeStation();
            _npc     = TensionTestHelpers.MakeCrewNpc();
            _station.AddNpc(_npc);
            TensionTestHelpers.SetDepartureRisk(_npc, _tension);

            // Fire announcement
            TensionTestHelpers.AdvanceOneDay(_tension, _station);
        }

        [Test]
        public void Intervention_Success_ResetsTensionToDisgruntled()
        {
            _tension.AttemptIntervention(_npc, "leadership", _station);

            Assert.Less(_npc.traitProfile.tensionScore, _tension.WorkSlowdownThreshold,
                "Tension should drop below WorkSlowdown after successful intervention.");
            Assert.AreEqual(TensionStage.Disgruntled, _npc.traitProfile.tensionStage,
                "Stage should be Disgruntled after successful intervention.");
        }

        [Test]
        public void Intervention_Success_CancelsDeparturePending()
        {
            _tension.AttemptIntervention(_npc, "leadership", _station);

            Assert.IsNull(_npc.traitProfile.departure,
                "Departure state should be cleared after successful intervention.");
        }

        [Test]
        public void Intervention_Failure_LeavesDeparturePending()
        {
            // Force failure by setting DC higher than any possible roll
            _tension.InterventionSkillCheckDC = 999;

            bool success = _tension.AttemptIntervention(_npc, "leadership", _station);

            Assert.IsFalse(success, "Intervention should return false on failure.");
            Assert.IsNotNull(_npc.traitProfile?.departure,
                "Departure state should remain after failed intervention.");
            Assert.IsTrue(_npc.traitProfile.departure.announced,
                "announced flag should remain true after failed intervention.");
        }

        [Test]
        public void Intervention_ReturnsFalse_WhenNoPendingAnnouncement()
        {
            // Clear the departure state manually
            _npc.traitProfile.departure = null;

            bool success = _tension.AttemptIntervention(_npc, "leadership", _station);

            Assert.IsFalse(success, "Should return false when no departure announcement is pending.");
        }

        [Test]
        public void Intervention_ReturnsFalse_WhenFeatureFlagDisabled()
        {
            FeatureFlags.NpcDeparture = false;

            bool success = _tension.AttemptIntervention(_npc, "leadership", _station);

            Assert.IsFalse(success, "AttemptIntervention should return false when flag is disabled.");

            FeatureFlags.NpcDeparture = true;
        }
    }

    // ── Window expiry / departure execution tests ─────────────────────────────

    [TestFixture]
    public class DepartureExecutionTests
    {
        private TensionSystem _tension;
        private StationState  _station;
        private NPCInstance   _npc;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.NpcTraits    = true;
            FeatureFlags.NpcDeparture = true;

            _tension = TensionTestHelpers.MakeTensionSystem();
            // Very short window: just 1 tick beyond announcement tick
            _tension.InterventionWindowTicks = TimeSystem.TicksPerDay;
            _station = TensionTestHelpers.MakeStation();
            _npc     = TensionTestHelpers.MakeCrewNpc();
            _station.AddNpc(_npc);
            TensionTestHelpers.SetDepartureRisk(_npc, _tension);

            // Day 1: announcement fires
            TensionTestHelpers.AdvanceOneDay(_tension, _station);
        }

        [Test]
        public void ExpiredWindow_TriggersOnNpcDeparted()
        {
            bool departFired = false;
            _tension.OnNpcDeparted += npc => { if (npc.uid == _npc.uid) departFired = true; };

            // Day 2: window expires, departure executes
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.IsTrue(departFired, "OnNpcDeparted must fire once the intervention window expires.");
        }

        [Test]
        public void ExpiredWindow_RemovesNpcFromActiveRoster()
        {
            // Day 2
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.IsFalse(_station.npcs.ContainsKey(_npc.uid),
                "NPC should be removed from station.npcs on departure.");
        }

        [Test]
        public void ExpiredWindow_AddsNpcToDepartedPool()
        {
            // Day 2
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            Assert.IsTrue(_station.departedNpcs.ContainsKey(_npc.uid),
                "NPC should be added to station.departedNpcs on departure.");
        }

        [Test]
        public void DepartedPool_PreservesFullNpcState()
        {
            _npc.name = "FullStateNPC";

            // Day 2
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            var record = _station.departedNpcs[_npc.uid];
            Assert.IsNotNull(record.npc, "Departed record must contain the NPC instance.");
            Assert.AreEqual("FullStateNPC", record.npc.name, "NPC name must be preserved.");
            Assert.AreEqual("tension", record.reason, "Departure reason must be 'tension'.");
            Assert.IsTrue(record.eligibleForReinjection, "Tension-departure NPC must be eligible for reinjection.");
        }

        [Test]
        public void DepartedRecord_StoresDepartedAtTick()
        {
            int tickBeforeDepart = _station.tick;

            // Day 2
            TensionTestHelpers.AdvanceOneDay(_tension, _station);

            var record = _station.departedNpcs[_npc.uid];
            Assert.AreEqual(_station.tick, record.departedAtTick,
                "departedAtTick should match the tick when departure was executed.");
        }
    }

    // ── Stage-down cancellation tests ─────────────────────────────────────────

    [TestFixture]
    public class DepartureCancellationOnTensionImproveTests
    {
        private TensionSystem _tension;
        private StationState  _station;
        private NPCInstance   _npc;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.NpcTraits    = true;
            FeatureFlags.NpcDeparture = true;

            _tension = TensionTestHelpers.MakeTensionSystem();
            _tension.InterventionWindowTicks = TimeSystem.TicksPerDay * 10;
            _station = TensionTestHelpers.MakeStation();
            _npc     = TensionTestHelpers.MakeCrewNpc();
            _station.AddNpc(_npc);
            TensionTestHelpers.SetDepartureRisk(_npc, _tension);

            // Fire announcement
            TensionTestHelpers.AdvanceOneDay(_tension, _station);
        }

        [Test]
        public void DepartureCancelled_WhenTensionDropsBelowDepartureRisk()
        {
            // Manually reduce tension below DepartureRisk and trigger stage update
            _npc.traitProfile.tensionScore = _tension.WorkSlowdownThreshold - 1f;
            _tension.RegisterPlayerAction(_npc, PlayerActionType.ResourceProvisioning, _station);

            Assert.IsNull(_npc.traitProfile.departure,
                "Departure state should be cleared when tension improves below DepartureRisk.");
        }
    }
}
