// MoodSystemTests — EditMode unit tests for the two-axis MoodSystem.
//
// Validates:
//   • Happy/sad and calm/stressed axes update independently
//   • Deduplication: same (eventId, source) pair on an axis does not stack delta
//   • Drift-toward-50 on both axes each tick
//   • Crisis threshold fires at < 20 on the happy/sad axis only
//   • Station morale aggregate correctly reflects mean happy/sad scores
//   • Per-NPC modifier breakdown lists modifiers from both axes
//   • SanitySystem accumulator updated with both axis values
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static class MoodTestHelpers
    {
        public static StationState MakeStation()
        {
            return new StationState { stationName = "MoodTestStation", tick = 0 };
        }

        public static NPCInstance MakeCrewNpc(string uid = null, float moodScore = 50f, float stressScore = 50f)
        {
            var npc = new NPCInstance
            {
                uid        = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name       = "TestNPC",
                moodScore  = moodScore,
                stressScore = stressScore,
            };
            npc.statusTags.Add("crew");
            return npc;
        }
    }

    // ── Axis independence tests ───────────────────────────────────────────────

    [TestFixture]
    public class MoodAxisIndependenceTests
    {
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
        }

        [Test]
        public void PushModifier_HappySad_DoesNotAffectStressScore()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 50f);

            _mood.PushModifier(npc, "test_event", -15f, 100, 0, MoodAxis.HappySad, "test");

            Assert.AreEqual(35f, npc.moodScore,   0.001f, "moodScore should decrease by 15.");
            Assert.AreEqual(50f, npc.stressScore, 0.001f, "stressScore must be unchanged.");
        }

        [Test]
        public void PushModifier_CalmStressed_DoesNotAffectMoodScore()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 50f);

            _mood.PushModifier(npc, "test_stress", -12f, 100, 0, MoodAxis.CalmStressed, "test");

            Assert.AreEqual(50f, npc.moodScore,   0.001f, "moodScore must be unchanged.");
            Assert.AreEqual(38f, npc.stressScore, 0.001f, "stressScore should decrease by 12.");
        }

        [Test]
        public void PushModifier_BothAxes_UpdateIndependently()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 50f);

            _mood.PushModifier(npc, "mood_event",   10f, 100, 0, MoodAxis.HappySad,    "test");
            _mood.PushModifier(npc, "stress_event", -8f, 100, 0, MoodAxis.CalmStressed, "test");

            Assert.AreEqual(60f, npc.moodScore,   0.001f, "moodScore should be 60.");
            Assert.AreEqual(42f, npc.stressScore, 0.001f, "stressScore should be 42.");
        }

        [Test]
        public void NpcQuery_ReturnsBothAxisValues()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 72f, stressScore: 35f);

            Assert.AreEqual(72f, npc.moodScore,   0.001f, "happy/sad score should be 72.");
            Assert.AreEqual(35f, npc.stressScore, 0.001f, "calm/stressed score should be 35.");
        }
    }

    // ── Deduplication tests ───────────────────────────────────────────────────

    [TestFixture]
    public class MoodDeduplicationTests
    {
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
        }

        [Test]
        public void PushModifier_SameEventAndSource_HappySad_DoesNotStack()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f);

            _mood.PushModifier(npc, "event_a", 10f, 100, 0, MoodAxis.HappySad, "src");
            _mood.PushModifier(npc, "event_a", 10f, 100, 5, MoodAxis.HappySad, "src");  // same event+source

            Assert.AreEqual(60f, npc.moodScore, 0.001f,
                "Re-pushing the same (eventId, source) must refresh duration without adding delta again.");
        }

        [Test]
        public void PushModifier_SameEventAndSource_CalmStressed_DoesNotStack()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(stressScore: 50f);

            _mood.PushModifier(npc, "stress_a", -8f, 100, 0, MoodAxis.CalmStressed, "src");
            _mood.PushModifier(npc, "stress_a", -8f, 100, 5, MoodAxis.CalmStressed, "src");  // same event+source

            Assert.AreEqual(42f, npc.stressScore, 0.001f,
                "Re-pushing the same (eventId, source) on CalmStressed must not double-stack.");
        }

        [Test]
        public void PushModifier_SameEventId_DifferentAxis_AreIndependent()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 50f);

            _mood.PushModifier(npc, "shared_event", 10f, 100, 0, MoodAxis.HappySad,    "src");
            _mood.PushModifier(npc, "shared_event", -5f, 100, 0, MoodAxis.CalmStressed, "src");

            Assert.AreEqual(60f, npc.moodScore,   0.001f, "HappySad axis should gain +10.");
            Assert.AreEqual(45f, npc.stressScore, 0.001f, "CalmStressed axis should lose 5.");
        }
    }

    // ── Drift tests ───────────────────────────────────────────────────────────

    [TestFixture]
    public class MoodDriftTests
    {
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
        }

        [Test]
        public void Tick_HappySadAbove50_DriftsDown()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 80f, stressScore: 50f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            _mood.Tick(station);

            Assert.Less(npc.moodScore, 80f, "Happy/sad score above 50 should drift down.");
            Assert.AreEqual(50f, npc.stressScore, 0.001f, "Stress score at 50 should not change.");
        }

        [Test]
        public void Tick_HappySadBelow50_DriftsUp()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 20f, stressScore: 50f);
            npc.isSleeping = false;
            // Put in crisis manually to avoid event side effects interfering
            npc.inCrisis = true;
            station.npcs[npc.uid] = npc;

            _mood.Tick(station);

            Assert.Greater(npc.moodScore, 20f, "Happy/sad score below 50 should drift up.");
        }

        [Test]
        public void Tick_StressAbove50_DriftsDown()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 80f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            _mood.Tick(station);

            Assert.Less(npc.stressScore, 80f, "Stress score above 50 should drift down.");
        }

        [Test]
        public void Tick_StressBelow50_DriftsUp()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 20f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            _mood.Tick(station);

            Assert.Greater(npc.stressScore, 20f, "Stress score below 50 should drift up.");
        }

        [Test]
        public void Tick_BothAxesAt50_NoChange()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 50f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            _mood.Tick(station);

            Assert.AreEqual(50f, npc.moodScore,   0.001f, "Happy/sad at 50 should stay at 50.");
            Assert.AreEqual(50f, npc.stressScore, 0.001f, "Stress at 50 should stay at 50.");
        }

        [Test]
        public void Tick_WhileSleeping_NoDrift()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 80f, stressScore: 80f);
            npc.isSleeping = true;
            station.npcs[npc.uid] = npc;

            _mood.Tick(station);

            Assert.AreEqual(80f, npc.moodScore,   0.001f, "Happy/sad must not drift while sleeping.");
            Assert.AreEqual(80f, npc.stressScore, 0.001f, "Stress must not drift while sleeping.");
        }
    }

    // ── Crisis threshold tests ────────────────────────────────────────────────

    [TestFixture]
    public class MoodCrisisTests
    {
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
        }

        [Test]
        public void PushModifier_HappySadDropsBelow20_TriggersCrisis()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 30f);

            _mood.PushModifier(npc, "bad_event", -15f, 100, 0, MoodAxis.HappySad, "test");

            Assert.IsTrue(npc.inCrisis, "Crisis should be triggered when happy/sad drops below 20.");
            Assert.AreEqual(15f, npc.moodScore, 0.001f);
        }

        [Test]
        public void PushModifier_CalmStressedDropsBelow20_DoesNotTriggerCrisis()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 30f);

            _mood.PushModifier(npc, "stress_event", -15f, 100, 0, MoodAxis.CalmStressed, "test");

            Assert.IsFalse(npc.inCrisis,
                "Crisis must NOT be triggered by calm/stressed axis alone.");
            Assert.AreEqual(15f, npc.stressScore, 0.001f);
        }

        [Test]
        public void Crisis_WhenHappySadIsVeryLow_CalmStressedUnrelated()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 10f, stressScore: 90f);

            // Trigger threshold evaluation via a push
            _mood.PushModifier(npc, "any", 0f, 1, 0, MoodAxis.HappySad, "test");

            Assert.IsTrue(npc.inCrisis, "Crisis should fire when moodScore < 20, even if stressScore is high.");
        }
    }

    // ── Station morale aggregate tests ────────────────────────────────────────

    [TestFixture]
    public class StationMoraleTests
    {
        [Test]
        public void GetStationMorale_EmptyCrew_Returns50()
        {
            var station = MoodTestHelpers.MakeStation();

            float morale = MoodSystem.GetStationMorale(station);

            Assert.AreEqual(50f, morale, 0.001f, "Empty crew should return neutral morale (50).");
        }

        [Test]
        public void GetStationMorale_SingleNpc_ReturnsHappySadScore()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 80f, stressScore: 20f);
            station.npcs[npc.uid] = npc;

            float morale = MoodSystem.GetStationMorale(station);

            Assert.AreEqual(80f, morale, 0.001f,
                "Station morale must be the happy/sad score, not stress score.");
        }

        [Test]
        public void GetStationMorale_MultipleNpcs_ReturnsMean()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc1    = MoodTestHelpers.MakeCrewNpc(moodScore: 100f);
            var npc2    = MoodTestHelpers.MakeCrewNpc(moodScore: 60f);
            var npc3    = MoodTestHelpers.MakeCrewNpc(moodScore: 20f);
            station.npcs[npc1.uid] = npc1;
            station.npcs[npc2.uid] = npc2;
            station.npcs[npc3.uid] = npc3;

            float morale = MoodSystem.GetStationMorale(station);

            Assert.AreEqual(60f, morale, 0.001f, "Station morale should be the mean of all happy/sad scores.");
        }

        [Test]
        public void GetStationMorale_StressScoreDoesNotContribute()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc1    = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 0f);
            var npc2    = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 100f);
            station.npcs[npc1.uid] = npc1;
            station.npcs[npc2.uid] = npc2;

            float morale = MoodSystem.GetStationMorale(station);

            Assert.AreEqual(50f, morale, 0.001f,
                "Station morale must not be skewed by calm/stressed scores.");
        }
    }

    // ── Modifier breakdown tests ──────────────────────────────────────────────

    [TestFixture]
    public class MoodModifierBreakdownTests
    {
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
        }

        [Test]
        public void GetModifierBreakdown_ListsModifiersWithCorrectAxis()
        {
            var npc = MoodTestHelpers.MakeCrewNpc(moodScore: 50f, stressScore: 50f);

            _mood.PushModifier(npc, "happy_event",  10f, 100, 0, MoodAxis.HappySad,    "src_h");
            _mood.PushModifier(npc, "stress_event", -8f, 100, 0, MoodAxis.CalmStressed, "src_s");

            var breakdown = MoodSystem.GetModifierBreakdown(npc);

            Assert.AreEqual(2, breakdown.Count, "Breakdown should have one entry per modifier.");

            bool foundHappy  = false;
            bool foundStress = false;
            foreach (var (axis, rec) in breakdown)
            {
                if (rec.eventId == "happy_event")
                {
                    Assert.AreEqual(MoodAxis.HappySad, axis);
                    Assert.AreEqual(10f, rec.delta, 0.001f);
                    foundHappy = true;
                }
                else if (rec.eventId == "stress_event")
                {
                    Assert.AreEqual(MoodAxis.CalmStressed, axis);
                    Assert.AreEqual(-8f, rec.delta, 0.001f);
                    foundStress = true;
                }
            }
            Assert.IsTrue(foundHappy,  "HappySad modifier should appear in breakdown.");
            Assert.IsTrue(foundStress, "CalmStressed modifier should appear in breakdown.");
        }

        [Test]
        public void GetModifierBreakdown_IncludesSourceAndDuration()
        {
            var npc = MoodTestHelpers.MakeCrewNpc();

            _mood.PushModifier(npc, "test_event", 5f, 48, 100, MoodAxis.HappySad, "test_source");

            var breakdown = MoodSystem.GetModifierBreakdown(npc);

            Assert.AreEqual(1, breakdown.Count);
            var (axis, rec) = breakdown[0];
            Assert.AreEqual(MoodAxis.HappySad, axis);
            Assert.AreEqual("test_source", rec.source);
            Assert.AreEqual(148, rec.expiresAtTick);  // 100 + 48
        }
    }

    // ── Modifier expiry tests ─────────────────────────────────────────────────

    [TestFixture]
    public class MoodModifierExpiryTests
    {
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
        }

        [Test]
        public void Tick_ExpiredHappySadModifier_DeltaReversed()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 50f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            // Push a modifier that expires at tick 5
            _mood.PushModifier(npc, "short_event", 10f, 5, 0, MoodAxis.HappySad, "test");
            Assert.AreEqual(60f, npc.moodScore, 0.001f, "Modifier applied.");

            // Advance to tick 6 (past expiry)
            station.tick = 6;
            _mood.Tick(station);

            // The modifier reversed the +10, then drift ran once.
            // After reversal moodScore returns to 50; drift at exactly 50 produces no change.
            Assert.AreEqual(50f, npc.moodScore, 0.5f,
                "After modifier expiry the delta should be reversed and score returns toward 50.");
        }

        [Test]
        public void Tick_ExpiredStressModifier_DeltaReversed()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(stressScore: 50f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            _mood.PushModifier(npc, "stress_short", -10f, 5, 0, MoodAxis.CalmStressed, "test");
            Assert.AreEqual(40f, npc.stressScore, 0.001f, "Modifier applied.");

            station.tick = 6;
            _mood.Tick(station);

            Assert.AreEqual(50f, npc.stressScore, 0.5f,
                "After stress modifier expiry the delta should be reversed.");
        }
    }

    // ── SanitySystem accumulator tests ────────────────────────────────────────

    [TestFixture]
    public class MoodSanityAccumulatorTests
    {
        [Test]
        public void AccumulateMood_AveragesBothAxes()
        {
            var npc = MoodTestHelpers.MakeCrewNpc();
            npc.GetOrCreateSanity();  // ensure sanity profile exists

            SanitySystem.AccumulateMood(npc, 100f, 0f);  // avg = 50

            Assert.AreEqual(50f,  npc.sanity.dailyMoodAccumulator, 0.001f,
                "Accumulator should store the average of both axes.");
            Assert.AreEqual(1, npc.sanity.dailyMoodSampleCount);
        }

        [Test]
        public void AccumulateMood_BothAxesHigh_AccumulatesHighValue()
        {
            var npc = MoodTestHelpers.MakeCrewNpc();
            npc.GetOrCreateSanity();

            SanitySystem.AccumulateMood(npc, 90f, 80f);  // avg = 85

            Assert.AreEqual(85f, npc.sanity.dailyMoodAccumulator, 0.001f);
        }

        [Test]
        public void Tick_MoodSystem_FeedsAccumulatorEachTick()
        {
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 80f, stressScore: 60f);
            npc.isSleeping = false;
            npc.GetOrCreateSanity();
            station.npcs[npc.uid] = npc;

            var mood = new MoodSystem();
            mood.Tick(station);

            Assert.AreEqual(1, npc.sanity.dailyMoodSampleCount,
                "MoodSystem.Tick should have fed one sample to the sanity accumulator.");
        }
    }

    // ── OnNPCWakes reset tests ────────────────────────────────────────────────

    [TestFixture]
    public class MoodWakeResetTests
    {
        [Test]
        public void OnNPCWakes_ResetsBothAxesToBaseline_WhenNoActiveModifiers()
        {
            var mood = new MoodSystem();
            var npc  = MoodTestHelpers.MakeCrewNpc(moodScore: 10f, stressScore: 20f);

            mood.OnNPCWakes(npc);

            Assert.AreEqual(MoodSystem.SleepRecoveryScore, npc.moodScore,   0.001f,
                "Happy/sad score should reset to baseline on wake.");
            Assert.AreEqual(MoodSystem.SleepRecoveryScore, npc.stressScore, 0.001f,
                "Calm/stressed score should reset to baseline on wake.");
        }

        [Test]
        public void OnNPCWakes_PreservesActiveModifiers_AddedBeforeWake()
        {
            // Simulate NeedSystem pushing "well_rested" then calling OnNPCWakes.
            var mood = new MoodSystem();
            var npc  = MoodTestHelpers.MakeCrewNpc(moodScore: 10f, stressScore: 20f);

            // Push a modifier before waking (e.g. NeedSystem "well_rested")
            mood.PushModifier(npc, "well_rested", 8f, 300, 0, MoodAxis.HappySad, "need_system");
            mood.OnNPCWakes(npc);

            // Score should be baseline (50) + active modifier delta (8) = 58
            Assert.AreEqual(58f, npc.moodScore, 0.001f,
                "Wake reset should incorporate already-active modifiers (e.g. well_rested +8).");
        }

        [Test]
        public void OnNPCWakes_ActiveModifierExpiresCleanly_AfterWake()
        {
            // After OnNPCWakes recomputes from baseline + modifiers, expiry should
            // reverse only the delta that was re-applied — no double-reversal.
            var mood    = new MoodSystem();
            var station = MoodTestHelpers.MakeStation();
            var npc     = MoodTestHelpers.MakeCrewNpc(moodScore: 10f, stressScore: 20f);
            npc.isSleeping = false;
            station.npcs[npc.uid] = npc;

            // Push +8 modifier expiring at tick 5
            mood.PushModifier(npc, "well_rested", 8f, 5, 0, MoodAxis.HappySad, "need_system");
            // Simulate wake — score becomes 50 + 8 = 58
            mood.OnNPCWakes(npc);
            Assert.AreEqual(58f, npc.moodScore, 0.001f, "Score after wake should be 50+8=58.");

            // Advance past expiry — modifier reverses, score returns to ~50
            station.tick = 6;
            mood.Tick(station);

            Assert.AreEqual(50f, npc.moodScore, 0.5f,
                "After modifier expires, score should return to baseline (no double-reversal).");
        }
    }
}
