// AsteroidMissionSystemTests — EditMode unit and integration tests for
// AsteroidMissionSystem (EXP-004).
//
// Validates:
//   • NPC autonomous abort threshold evaluation (viability score formula)
//   • Distress signal window expiry → total loss escalation
//   • Rescue dispatch resolves distress signal → partial yield
//   • Manual retreat (abandonment) → partial yield
//   • Catastrophic loss → immediate total loss with no yield
//   • Yield calculation formula: min, mid, and max crew/tile inputs
//   • Normal full-yield completion on time expiry
//   • Department Head automated dispatch (integration with DepartmentSystem)
//   • FeatureFlags.AsteroidMissions gates failure-state processing
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    internal static class AsteroidTestHelpers
    {
        public static StationState MakeStation()
        {
            var s = new StationState("TestStation");
            s.departments.Clear();
            return s;
        }

        public static NPCInstance MakeCrewNpc(string uid = null, int rank = 0)
        {
            var npc = new NPCInstance
            {
                uid  = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "Crew_" + (uid ?? "x"),
                rank = rank,
            };
            npc.statusTags.Add("crew");
            npc.moodScore = 75f;   // healthy baseline
            npc.injuries  = 0;
            return npc;
        }

        /// <summary>
        /// Creates a dispatched asteroid mission and returns the map.
        /// Adds a POI and two crew NPCs to the station, dispatches, and returns the map.
        /// </summary>
        public static AsteroidMapState DispatchTestMission(
            AsteroidMissionSystem sys, StationState station,
            int durationTicks = 480, int crewCount = 2)
        {
            var poi = PointOfInterest.Create("Asteroid", "TestRock", 10f, 10f, 12345);
            station.pointsOfInterest[poi.uid] = poi;

            var crewUids = new List<string>();
            for (int i = 0; i < crewCount; i++)
            {
                var npc = MakeCrewNpc();
                station.AddNpc(npc);
                crewUids.Add(npc.uid);
            }

            var (ok, reason, map) = sys.DispatchAsteroidMission(
                poi.uid, crewUids, station, durationTicks);
            Assert.IsTrue(ok, $"Dispatch failed: {reason}");
            return map;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Viability Score
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ViabilityScoreTests
    {
        [Test]
        public void FullHealthCrew_ViabilityIsHigh()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            // All crew at full health and good mood
            foreach (var uid in map.assignedNpcUids)
            {
                var npc = station.npcs[uid];
                npc.moodScore = 100f;
                npc.injuries  = 0;
            }
            map.threatLevel = 0f;

            float v = sys.ComputeViabilityScore(map, station);
            Assert.GreaterOrEqual(v, AsteroidMissionSystem.ViabilityAbortThreshold,
                "Full-health crew should have viability above the abort threshold.");
        }

        [Test]
        public void AllCrewInCrisis_ViabilityIsZero()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            foreach (var uid in map.assignedNpcUids)
                station.npcs[uid].inCrisis = true;

            float v = sys.ComputeViabilityScore(map, station);
            Assert.AreEqual(0f, v, 0.001f,
                "All crew in crisis should yield viability = 0.");
        }

        [Test]
        public void HeavilyInjuredCrew_ViabilityDropsBelowThreshold()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            foreach (var uid in map.assignedNpcUids)
            {
                var npc = station.npcs[uid];
                npc.injuries  = 5;     // max injury factor penalty (1 - 5*0.2 = 0)
                npc.moodScore = 10f;   // very low mood
            }
            map.threatLevel = 1f;

            float v = sys.ComputeViabilityScore(map, station);
            Assert.Less(v, AsteroidMissionSystem.ViabilityAbortThreshold,
                "Heavily injured, low-mood crew under max threat should be below abort threshold.");
        }

        [Test]
        public void NoAssignedNpcs_ViabilityIsOne()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            // Clear assigned NPCs from the map without removing them from station
            map.assignedNpcUids.Clear();

            float v = sys.ComputeViabilityScore(map, station);
            Assert.AreEqual(1f, v, 0.001f, "Empty crew list should return safe default of 1.");
        }

        [Test]
        public void ThreatLevel_ReducesViabilityProperly()
        {
            var station  = AsteroidTestHelpers.MakeStation();
            var sys      = new AsteroidMissionSystem();
            var map      = AsteroidTestHelpers.DispatchTestMission(sys, station, crewCount: 1);

            string npcUid = map.assignedNpcUids[0];
            var npc = station.npcs[npcUid];
            npc.moodScore = 100f;
            npc.injuries  = 0;
            npc.inCrisis  = false;

            map.threatLevel = 0f;
            float vLow = sys.ComputeViabilityScore(map, station);

            map.threatLevel = 1f;
            float vHigh = sys.ComputeViabilityScore(map, station);

            Assert.Greater(vLow, vHigh, "Higher threat level should reduce viability.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Autonomous Abort
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class AutonomousAbortTests
    {
        [Test]
        public void ViabilityBelowThreshold_TickResolvesAsAutonomousAbort()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

                // Force viability below threshold: all crew in crisis
                foreach (var uid in map.assignedNpcUids)
                    station.npcs[uid].inCrisis = true;

                sys.Tick(station);

                Assert.AreEqual("autonomous_abort", map.status,
                    "Mission should resolve as autonomous_abort when viability drops below threshold.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void AutoAbort_FreesCrewMissionUids()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

                foreach (var uid in map.assignedNpcUids)
                    station.npcs[uid].inCrisis = true;

                sys.Tick(station);

                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid,
                        "Crew missionUid should be cleared after autonomous abort.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void AutoAbort_RecordsPartialYield()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

                foreach (var uid in map.assignedNpcUids)
                    station.npcs[uid].inCrisis = true;

                // Ensure map has some ore/ice tiles (generated map will have some)
                // We just verify the multiplier applies: parts+ice may be 0 for a small map
                // but the key "parts" should exist in extractedResources.
                sys.Tick(station);

                Assert.IsTrue(map.extractedResources.ContainsKey("parts"),
                    "extractedResources should contain 'parts' key after abort.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void HighViabilityCrew_DoesNotAbort()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

                // Crew is healthy — mission should not abort prematurely
                station.tick = map.startTick + 1; // before end tick

                sys.Tick(station);

                Assert.AreEqual("active", map.status,
                    "Healthy crew before end tick should not trigger abort.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Manual Retreat (Abandonment)
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class ManualRetreatTests
    {
        [Test]
        public void IssueRetreatOrder_SetsFlag()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            var (ok, reason) = sys.IssueRetreatOrder(map.uid, station);

            Assert.IsTrue(ok, $"IssueRetreatOrder failed: {reason}");
            Assert.IsTrue(map.retreatOrdered, "retreatOrdered should be true after issuing retreat.");
        }

        [Test]
        public void IssueRetreatOrder_OnCompletedMission_ReturnsFalse()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);
            map.status  = "complete";

            var (ok, _) = sys.IssueRetreatOrder(map.uid, station);
            Assert.IsFalse(ok, "Cannot issue retreat on a non-active mission.");
        }

        [Test]
        public void RetreatOrdered_TickResolvesAsAbandonment()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

                sys.IssueRetreatOrder(map.uid, station);
                sys.Tick(station);

                Assert.AreEqual("abandonment", map.status,
                    "Retreat order should resolve mission as 'abandonment'.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void Abandonment_FreesCrewAndRecordsPartialYield()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

                sys.IssueRetreatOrder(map.uid, station);
                sys.Tick(station);

                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid,
                        "Crew missionUid should be null after abandonment.");

                Assert.IsTrue(map.extractedResources.ContainsKey("parts"),
                    "Partial yield should be recorded after abandonment.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Distress Signal
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DistressSignalTests
    {
        [Test]
        public void TriggerDistressSignal_SetsActiveFlag()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            var (ok, reason) = sys.TriggerDistressSignal(map.uid, station, windowTicks: 60);

            Assert.IsTrue(ok, $"TriggerDistressSignal failed: {reason}");
            Assert.IsTrue(map.distressSignalActive, "distressSignalActive should be true.");
            Assert.AreEqual(station.tick + 60, map.distressWindowExpiryTick);
        }

        [Test]
        public void TriggerDistressSignal_Twice_ReturnsFalse()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            sys.TriggerDistressSignal(map.uid, station, 60);
            var (ok, _) = sys.TriggerDistressSignal(map.uid, station, 60);

            Assert.IsFalse(ok, "Second distress signal on same mission should fail.");
        }

        [Test]
        public void DistressWindowExpiry_WithoutRescue_ResolvesTotalLoss()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                sys.TriggerDistressSignal(map.uid, station, windowTicks: 10);

                // Advance tick past window
                station.tick = map.distressWindowExpiryTick + 1;
                sys.Tick(station);

                Assert.AreEqual("total_loss", map.status,
                    "Expired distress window without rescue should become total loss.");
                Assert.IsEmpty(map.extractedResources,
                    "Total loss should yield no resources.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void DistressWindowExpiry_TotalLoss_FreesCrewMissionUids()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                sys.TriggerDistressSignal(map.uid, station, windowTicks: 10);
                station.tick = map.distressWindowExpiryTick + 1;
                sys.Tick(station);

                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid,
                        "Crew missionUid should be null after total loss.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void RespondToDistress_ClearsSignal()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            sys.TriggerDistressSignal(map.uid, station, 60);
            var (ok, reason) = sys.RespondToDistressSignal(map.uid, station);

            Assert.IsTrue(ok, $"RespondToDistressSignal failed: {reason}");
            Assert.IsFalse(map.distressSignalActive,
                "distressSignalActive should be false after player responds.");
            Assert.IsTrue(map.rescueDispatched, "rescueDispatched should be true.");
        }

        [Test]
        public void RespondToDistress_NoActiveSignal_ReturnsFalse()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            var (ok, _) = sys.RespondToDistressSignal(map.uid, station);
            Assert.IsFalse(ok, "Responding when no distress signal active should fail.");
        }

        [Test]
        public void RescueDispatched_TickResolvesAsRescued_WithPartialYield()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                sys.TriggerDistressSignal(map.uid, station, windowTicks: 9999);
                sys.RespondToDistressSignal(map.uid, station);

                sys.Tick(station);

                Assert.AreEqual("rescued", map.status,
                    "After rescue dispatch, mission should resolve as 'rescued'.");
                Assert.IsTrue(map.extractedResources.ContainsKey("parts"),
                    "Rescued mission should record partial yield.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Catastrophic Total Loss
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class CatastrophicLossTests
    {
        [Test]
        public void TriggerCatastrophicLoss_ImmediatelySetsTotalLoss()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

            var (ok, reason) = sys.TriggerCatastrophicLoss(map.uid, station);

            Assert.IsTrue(ok, $"TriggerCatastrophicLoss failed: {reason}");
            Assert.AreEqual("total_loss", map.status, "Status should be total_loss.");
            Assert.IsEmpty(map.extractedResources, "Catastrophic loss yields no resources.");
        }

        [Test]
        public void CatastrophicLoss_NoPriorDistressSignal()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

            sys.TriggerCatastrophicLoss(map.uid, station);

            Assert.IsFalse(map.distressSignalActive,
                "Catastrophic loss should not leave a distress signal active.");
        }

        [Test]
        public void CatastrophicLoss_FreesCrewMissionUids()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            sys.TriggerCatastrophicLoss(map.uid, station);

            foreach (var uid in map.assignedNpcUids)
                Assert.IsNull(station.npcs[uid].missionUid,
                    "Crew missionUid should be null after catastrophic loss.");
        }

        [Test]
        public void CatastrophicLoss_OnNonActiveMission_ReturnsFalse()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);
            map.status  = "complete";

            var (ok, _) = sys.TriggerCatastrophicLoss(map.uid, station);
            Assert.IsFalse(ok, "Catastrophic loss on non-active mission should fail.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Yield Calculation
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class YieldCalculationTests
    {
        /// <summary>
        /// Full completion with a map seeded to guarantee ore/ice tiles and
        /// verifiable yield formula: oreTiles * 0.10 * crew == oreYield.
        /// </summary>
        [Test]
        public void FullCompletion_YieldIsProportionalToTilesAndCrew()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false; // use simple time-expiry path
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station,
                    durationTicks: 1, crewCount: 3);

                // Count ore and ice tiles
                int oreTiles = 0, iceTiles = 0;
                foreach (byte t in map.tiles)
                {
                    if (t == (byte)AsteroidTile.Ore) oreTiles++;
                    else if (t == (byte)AsteroidTile.Ice) iceTiles++;
                }

                // Advance to end tick
                station.tick = map.endTick;
                sys.Tick(station);

                Assert.AreEqual("complete", map.status, "Mission should be complete.");
                int expectedOre = UnityEngine.Mathf.RoundToInt(oreTiles * 0.10f * 3);
                int expectedIce = UnityEngine.Mathf.RoundToInt(iceTiles * 0.10f * 3);
                Assert.AreEqual(expectedOre, map.extractedResources["parts"],
                    "Full yield for ore tiles should match formula.");
                Assert.AreEqual(expectedIce, map.extractedResources["ice"],
                    "Full yield for ice tiles should match formula.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void PartialYield_IsHalfOfFullYield()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;

                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station,
                    durationTicks: 9999, crewCount: 2);

                // Count tile yields directly to compute expectations
                int oreTiles = 0, iceTiles = 0;
                foreach (byte t in map.tiles)
                {
                    if (t == (byte)AsteroidTile.Ore) oreTiles++;
                    else if (t == (byte)AsteroidTile.Ice) iceTiles++;
                }
                int crewCount = map.assignedNpcUids.Count;
                int expectedOre = UnityEngine.Mathf.RoundToInt(
                    oreTiles * 0.10f * crewCount * AsteroidMissionSystem.PartialYieldMultiplier);
                int expectedIce = UnityEngine.Mathf.RoundToInt(
                    iceTiles * 0.10f * crewCount * AsteroidMissionSystem.PartialYieldMultiplier);

                sys.IssueRetreatOrder(map.uid, station);
                sys.Tick(station);

                Assert.AreEqual("abandonment", map.status);
                Assert.AreEqual(expectedOre, map.extractedResources["parts"],
                    "Partial yield for parts should match the expected partial formula.");
                Assert.AreEqual(expectedIce, map.extractedResources["ice"],
                    "Partial yield for ice should match the expected partial formula.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void TotalLoss_YieldsZeroResources()
        {
            var station = AsteroidTestHelpers.MakeStation();
            var sys     = new AsteroidMissionSystem();
            var map     = AsteroidTestHelpers.DispatchTestMission(sys, station);

            sys.TriggerCatastrophicLoss(map.uid, station);

            bool hasResources = map.extractedResources.ContainsKey("parts") &&
                                map.extractedResources["parts"] > 0;
            Assert.IsFalse(hasResources, "Total loss should not yield any parts.");
        }

        [Test]
        public void MinCrewCount_CalculationUsesMinOfOne()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station,
                    durationTicks: 1, crewCount: 1);

                // Force no assigned NPCs to test the Mathf.Max(1, count) guard
                map.assignedNpcUids.Clear();

                station.tick = map.endTick;
                sys.Tick(station);

                Assert.AreEqual("complete", map.status,
                    "Mission should still complete even with 0 assigned NPCs.");
                // Yield should be computed as crewCount=1 (the minimum guard)
                int oreTiles = 0;
                foreach (byte t in map.tiles)
                    if (t == (byte)AsteroidTile.Ore) oreTiles++;

                int expectedOre = UnityEngine.Mathf.RoundToInt(oreTiles * 0.10f * 1);
                Assert.AreEqual(expectedOre, map.extractedResources["parts"],
                    "With no assigned NPCs, crewCount should default to 1.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature Flag Gating
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class AsteroidMissionsFeatureFlagTests
    {
        [Test]
        public void FlagFalse_IssueRetreatOrder_ReturnsFalseAndDoesNotMutate()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                var (ok, _) = sys.IssueRetreatOrder(map.uid, station);

                Assert.IsFalse(ok, "IssueRetreatOrder should return false when flag is off.");
                Assert.IsFalse(map.retreatOrdered, "retreatOrdered must not be set when flag is off.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FlagFalse_TriggerDistressSignal_ReturnsFalseAndDoesNotMutate()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                var (ok, _) = sys.TriggerDistressSignal(map.uid, station, 60);

                Assert.IsFalse(ok, "TriggerDistressSignal should return false when flag is off.");
                Assert.IsFalse(map.distressSignalActive, "distressSignalActive must not be set when flag is off.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FlagFalse_RespondToDistressSignal_ReturnsFalse()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                // Manually set distress fields to simulate an active signal
                map.distressSignalActive     = true;
                map.distressWindowExpiryTick = station.tick + 100;

                var (ok, _) = sys.RespondToDistressSignal(map.uid, station);

                Assert.IsFalse(ok, "RespondToDistressSignal should return false when flag is off.");
                Assert.IsFalse(map.rescueDispatched, "rescueDispatched must not be set when flag is off.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FlagFalse_TriggerCatastrophicLoss_ReturnsFalseAndDoesNotResolve()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                var (ok, _) = sys.TriggerCatastrophicLoss(map.uid, station);

                Assert.IsFalse(ok, "TriggerCatastrophicLoss should return false when flag is off.");
                Assert.AreEqual("active", map.status, "Mission must stay active when flag is off.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FlagFalse_RetreatOrder_DoesNotResolveOnTick()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                // IssueRetreatOrder now also returns false when flag is off, so
                // directly set the field to verify Tick() also ignores it.
                map.retreatOrdered = true;
                station.tick = map.startTick + 1;
                sys.Tick(station);

                Assert.AreEqual("active", map.status,
                    "With flag off, retreat order should not trigger resolution mid-mission.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FlagFalse_DistressExpiry_DoesNotResolveTotalLoss()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                // Manually set distress fields to simulate expiry
                map.distressSignalActive     = true;
                map.distressWindowExpiryTick = station.tick - 1;

                sys.Tick(station);

                Assert.AreEqual("active", map.status,
                    "With flag off, expired distress signal should not trigger total loss.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FlagFalse_NormalCompletion_StillWorks()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = false;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 1);

                station.tick = map.endTick;
                sys.Tick(station);

                Assert.AreEqual("complete", map.status,
                    "Normal completion should still work when flag is false.");
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integration: Full Mission Lifecycle (each failure state)
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class MissionLifecycleTests
    {
        [Test]
        public void FullLifecycle_NormalCompletion_YieldsResourcesAndFreesCrews()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 5);

                // Verify crew is locked
                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNotNull(station.npcs[uid].missionUid, "Crew should be locked.");

                station.tick = map.endTick;
                sys.Tick(station);

                Assert.AreEqual("complete", map.status);
                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid, "Crew freed after completion.");
                Assert.IsTrue(map.extractedResources.ContainsKey("parts"));
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FullLifecycle_Abandonment_PartialYieldAndFreedCrew()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                sys.IssueRetreatOrder(map.uid, station);
                sys.Tick(station);

                Assert.AreEqual("abandonment", map.status);
                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid);
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FullLifecycle_AutonomousAbort_PartialYieldAndFreedCrew()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                foreach (var uid in map.assignedNpcUids)
                    station.npcs[uid].inCrisis = true;

                sys.Tick(station);

                Assert.AreEqual("autonomous_abort", map.status);
                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid);
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FullLifecycle_DistressSignalRescued_PartialYieldAndFreedCrew()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                sys.TriggerDistressSignal(map.uid, station, windowTicks: 9999);
                sys.RespondToDistressSignal(map.uid, station);
                sys.Tick(station);

                Assert.AreEqual("rescued", map.status);
                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid);
                Assert.IsTrue(map.extractedResources.ContainsKey("parts"));
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }

        [Test]
        public void FullLifecycle_TotalLoss_NoYieldAndFreedCrew()
        {
            bool wasEnabled = FeatureFlags.AsteroidMissions;
            try
            {
                FeatureFlags.AsteroidMissions = true;
                var station = AsteroidTestHelpers.MakeStation();
                var sys     = new AsteroidMissionSystem();
                var map     = AsteroidTestHelpers.DispatchTestMission(sys, station, durationTicks: 9999);

                sys.TriggerDistressSignal(map.uid, station, windowTicks: 10);
                station.tick = map.distressWindowExpiryTick + 1;
                sys.Tick(station);

                Assert.AreEqual("total_loss", map.status);
                Assert.IsEmpty(map.extractedResources);
                foreach (var uid in map.assignedNpcUids)
                    Assert.IsNull(station.npcs[uid].missionUid);
            }
            finally { FeatureFlags.AsteroidMissions = wasEnabled; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Integration: Department Head automated dispatch
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DepartmentHeadDispatchTests
    {
        [Test]
        public void HeadWithAvailableCrew_AutomaticallyDispatchesMission()
        {
            bool deptWasEnabled = FeatureFlags.DepartmentManagement;
            try
            {
                FeatureFlags.DepartmentManagement = true;

                var station     = AsteroidTestHelpers.MakeStation();
                var deptSystem  = new DepartmentSystem();
                var asteroidSys = new AsteroidMissionSystem();

                // Add an unvisited asteroid POI
                var poi = PointOfInterest.Create("Asteroid", "Dispatch Rock", 5f, 5f, 999);
                station.pointsOfInterest[poi.uid] = poi;

                // Create department with a head and crew
                var dept = deptSystem.CreateDepartment("Mining Corps", station);

                var head = AsteroidTestHelpers.MakeCrewNpc("head1", rank: 1);
                station.AddNpc(head);
                deptSystem.AssignNpcToDepartment(head.uid, dept.uid, station);
                deptSystem.AppointHead(dept.uid, head.uid, station);

                var crewMember = AsteroidTestHelpers.MakeCrewNpc("crew1");
                station.AddNpc(crewMember);
                deptSystem.AssignNpcToDepartment(crewMember.uid, dept.uid, station);

                // Advance tick to trigger mission check interval
                // MissionCheckIntervalTicks = 48; set tick to 0 so 0 % 48 == 0
                station.tick = 0;

                deptSystem.Tick(station, asteroidSys);

                // Verify a mission was dispatched
                bool missionDispatched = false;
                foreach (var am in station.asteroidMaps.Values)
                    if (am.poiUid == poi.uid) { missionDispatched = true; break; }

                Assert.IsTrue(missionDispatched,
                    "Department Head should automatically dispatch a mission to the asteroid POI.");
            }
            finally { FeatureFlags.DepartmentManagement = deptWasEnabled; }
        }

        [Test]
        public void HeadWithNoCrew_DoesNotDispatch()
        {
            bool deptWasEnabled = FeatureFlags.DepartmentManagement;
            try
            {
                FeatureFlags.DepartmentManagement = true;

                var station     = AsteroidTestHelpers.MakeStation();
                var deptSystem  = new DepartmentSystem();
                var asteroidSys = new AsteroidMissionSystem();

                var poi = PointOfInterest.Create("Asteroid", "Empty Rock", 5f, 5f, 111);
                station.pointsOfInterest[poi.uid] = poi;

                var dept = deptSystem.CreateDepartment("Mining Corps", station);
                var head = AsteroidTestHelpers.MakeCrewNpc("head1", rank: 1);
                station.AddNpc(head);
                deptSystem.AssignNpcToDepartment(head.uid, dept.uid, station);
                deptSystem.AppointHead(dept.uid, head.uid, station);
                // No additional crew assigned

                station.tick = 0;
                deptSystem.Tick(station, asteroidSys);

                Assert.AreEqual(0, station.asteroidMaps.Count,
                    "No mission should be dispatched when department has no available crew.");
            }
            finally { FeatureFlags.DepartmentManagement = deptWasEnabled; }
        }

        [Test]
        public void HeadOnMission_DoesNotDispatch()
        {
            bool deptWasEnabled = FeatureFlags.DepartmentManagement;
            try
            {
                FeatureFlags.DepartmentManagement = true;

                var station     = AsteroidTestHelpers.MakeStation();
                var deptSystem  = new DepartmentSystem();
                var asteroidSys = new AsteroidMissionSystem();

                var poi = PointOfInterest.Create("Asteroid", "Rock B", 5f, 5f, 222);
                station.pointsOfInterest[poi.uid] = poi;

                var dept = deptSystem.CreateDepartment("Mining Corps", station);
                var head = AsteroidTestHelpers.MakeCrewNpc("head1", rank: 1);
                station.AddNpc(head);
                deptSystem.AssignNpcToDepartment(head.uid, dept.uid, station);
                deptSystem.AppointHead(dept.uid, head.uid, station);

                // Head is on a mission — should skip dispatch evaluation
                head.missionUid = "some_mission";

                var crew = AsteroidTestHelpers.MakeCrewNpc("crew1");
                station.AddNpc(crew);
                deptSystem.AssignNpcToDepartment(crew.uid, dept.uid, station);

                station.tick = 0;
                deptSystem.Tick(station, asteroidSys);

                Assert.AreEqual(0, station.asteroidMaps.Count,
                    "Head on mission should prevent automated dispatch.");
            }
            finally { FeatureFlags.DepartmentManagement = deptWasEnabled; }
        }
    }
}
