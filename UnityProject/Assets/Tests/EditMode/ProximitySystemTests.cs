// ProximitySystemTests — EditMode unit tests for NPC-012 ProximitySystem effects.
//
// Validates:
//   • Friend mood boost targets the happy/sad axis with the correct delta
//   • Enemy mood penalty is applied on both axes (happy/sad and calm/stressed)
//   • Mentor/Student pair: both receive friend mood boost; student receives work speed bonus
//   • Mentor work speed bonus value matches MentorWorkBonus constant (1.1 scalar)
//   • Proximity work modifier expires after ProximityModifierDurationTicks when pair separates
//   • Relationship type cache invalidates correctly: after a Friend→Enemy change, the next
//     tick applies the enemy penalty instead of the friend boost
//   • Module-grouping optimisation: NPCs in different modules receive no proximity effect
//   • Performance: 20 NPCs in the same module complete tick evaluation without error
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class ProximityTestHelpers
    {
        public static StationState MakeStation(int tick = 0)
        {
            return new StationState { stationName = "ProxTestStation", tick = tick };
        }

        public static NPCInstance MakeCrewNpc(string uid = null, string location = "room1",
                                               float moodScore = 50f, float stressScore = 50f)
        {
            string finalUid = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8);
            var npc = new NPCInstance
            {
                uid         = finalUid,
                name        = "NPC_" + finalUid,
                moodScore   = moodScore,
                stressScore = stressScore,
                location    = location,
            };
            npc.statusTags.Add("crew");
            return npc;
        }

        public static void AddToStation(StationState station, NPCInstance npc)
        {
            station.npcs[npc.uid] = npc;
        }

        public static RelationshipRecord MakeRelationship(StationState station,
                                                           NPCInstance a, NPCInstance b,
                                                           RelationshipType type,
                                                           string mentorUid = null)
        {
            var rec = RelationshipRegistry.GetOrCreate(station, a.uid, b.uid);
            rec.affinityScore    = type == RelationshipType.Enemy ? -10f : 25f;
            rec.relationshipType = type;
            rec.mentorUid        = mentorUid;
            return rec;
        }
    }

    // ── Friend boost tests ────────────────────────────────────────────────────

    [TestFixture]
    public class ProximityFriendBoostTests
    {
        private MoodSystem    _mood;
        private ProximitySystem _prox;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
            _prox = new ProximitySystem();
        }

        [Test]
        public void FriendNpcs_SameModule_BothReceiveHappySadBoost()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", moodScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1", moodScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Friend);

            _prox.Tick(station, _mood, null);

            Assert.Greater(a.moodScore, 50f, "NPC A must receive a happy/sad mood boost.");
            Assert.Greater(b.moodScore, 50f, "NPC B must receive a happy/sad mood boost.");
        }

        [Test]
        public void FriendNpcs_SameModule_ModifierNamedProximityFriend()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1");
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1");
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Friend);

            _prox.Tick(station, _mood, null);

            Assert.IsTrue(a.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "NPC A must have a 'proximity_friend' modifier on the happy/sad axis.");
            Assert.IsTrue(b.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "NPC B must have a 'proximity_friend' modifier on the happy/sad axis.");
        }

        [Test]
        public void FriendNpcs_SameModule_StressScoreUnchanged()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", stressScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1", stressScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Friend);

            _prox.Tick(station, _mood, null);

            Assert.AreEqual(50f, a.stressScore, 0.001f, "Friend boost must not affect calm/stressed axis.");
            Assert.AreEqual(50f, b.stressScore, 0.001f, "Friend boost must not affect calm/stressed axis.");
        }

        [Test]
        public void FriendNpcs_DifferentModules_NoModifierApplied()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", moodScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room2", moodScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Friend);

            _prox.Tick(station, _mood, null);

            Assert.AreEqual(50f, a.moodScore, 0.001f, "Different-module Friends must not receive a mood boost.");
            Assert.AreEqual(50f, b.moodScore, 0.001f, "Different-module Friends must not receive a mood boost.");
        }
    }

    // ── Enemy penalty tests ───────────────────────────────────────────────────

    [TestFixture]
    public class ProximityEnemyPenaltyTests
    {
        private MoodSystem    _mood;
        private ProximitySystem _prox;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
            _prox = new ProximitySystem();
        }

        [Test]
        public void EnemyNpcs_SameModule_BothReceiveHappySadPenalty()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", moodScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1", moodScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Enemy);

            _prox.Tick(station, _mood, null);

            Assert.Less(a.moodScore, 50f, "Enemy NPC A must receive a happy/sad mood penalty.");
            Assert.Less(b.moodScore, 50f, "Enemy NPC B must receive a happy/sad mood penalty.");
        }

        [Test]
        public void EnemyNpcs_SameModule_BothReceiveCalmStressedPenalty()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", stressScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1", stressScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Enemy);

            _prox.Tick(station, _mood, null);

            Assert.Less(a.stressScore, 50f, "Enemy NPC A must receive a calm/stressed penalty.");
            Assert.Less(b.stressScore, 50f, "Enemy NPC B must receive a calm/stressed penalty.");
        }

        [Test]
        public void EnemyNpcs_SameModule_BothAxesHaveProximityEnemyModifier()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1");
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1");
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Enemy);

            _prox.Tick(station, _mood, null);

            Assert.IsTrue(a.moodModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "NPC A must have 'proximity_enemy' on the happy/sad axis.");
            Assert.IsTrue(a.stressModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "NPC A must have 'proximity_enemy' on the calm/stressed axis.");
            Assert.IsTrue(b.moodModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "NPC B must have 'proximity_enemy' on the happy/sad axis.");
            Assert.IsTrue(b.stressModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "NPC B must have 'proximity_enemy' on the calm/stressed axis.");
        }

        [Test]
        public void EnemyNpcs_DifferentModules_NoModifierApplied()
        {
            var station = ProximityTestHelpers.MakeStation();
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", moodScore: 50f, stressScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room2", moodScore: 50f, stressScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);
            ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Enemy);

            _prox.Tick(station, _mood, null);

            Assert.AreEqual(50f, a.moodScore,   0.001f, "No penalty for different-module enemies.");
            Assert.AreEqual(50f, a.stressScore, 0.001f, "No stress penalty for different-module enemies.");
        }
    }

    // ── Mentor work speed bonus tests ─────────────────────────────────────────

    [TestFixture]
    public class ProximityMentorWorkBonusTests
    {
        private MoodSystem      _mood;
        private ProximitySystem _prox;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
            _prox = new ProximitySystem();
        }

        [Test]
        public void MentorStudent_SameModule_StudentReceivesWorkSpeedBonus()
        {
            var station = ProximityTestHelpers.MakeStation(tick: 0);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1");
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1");
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            _prox.Tick(station, _mood, null);

            Assert.Greater(student.proximityWorkModifier, 1.0f,
                "Student must have a work speed bonus > 1.0 when mentor is in the same module.");
        }

        [Test]
        public void MentorStudent_SameModule_WorkBonusIsCorrectValue()
        {
            var station = ProximityTestHelpers.MakeStation(tick: 0);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1");
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1");
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            _prox.Tick(station, _mood, null);

            // MentorWorkBonus = 0.1 → proximityWorkModifier should be 1.1
            Assert.AreEqual(1.1f, student.proximityWorkModifier, 0.001f,
                "Student proximityWorkModifier must equal 1.0 + MentorWorkBonus (1.1).");
        }

        [Test]
        public void MentorStudent_SameModule_MentorDoesNotReceiveWorkBonus()
        {
            var station = ProximityTestHelpers.MakeStation(tick: 0);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1");
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1");
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            _prox.Tick(station, _mood, null);

            Assert.AreEqual(1.0f, mentor.proximityWorkModifier, 0.001f,
                "Mentor must not receive a work speed bonus from their own proximity.");
        }

        [Test]
        public void MentorStudent_SameModule_BothReceiveFriendMoodBoost()
        {
            var station = ProximityTestHelpers.MakeStation(tick: 0);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1", moodScore: 50f);
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1", moodScore: 50f);
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            _prox.Tick(station, _mood, null);

            Assert.IsTrue(mentor.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "Mentor must also receive the proximity_friend mood boost.");
            Assert.IsTrue(student.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "Student must receive the proximity_friend mood boost.");
        }

        [Test]
        public void MentorStudent_SameModule_WorkBonusHasExpirySet()
        {
            const int currentTick = 10;
            var station = ProximityTestHelpers.MakeStation(tick: currentTick);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1");
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1");
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            _prox.Tick(station, _mood, null);

            int expectedExpiry = currentTick + ProximitySystem.ProximityModifierDurationTicks;
            Assert.AreEqual(expectedExpiry, student.proximityWorkModifierExpiresAtTick,
                "Expiry tick must equal current tick + ProximityModifierDurationTicks.");
        }
    }

    // ── Modifier expiry tests ─────────────────────────────────────────────────

    [TestFixture]
    public class ProximityModifierExpiryTests
    {
        private MoodSystem      _mood;
        private ProximitySystem _prox;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
            _prox = new ProximitySystem();
        }

        [Test]
        public void MentorWorkBonus_ExpiresAfterProximityModifierDurationTicks()
        {
            // Arrange: set the expiry tick to a past tick
            var station = ProximityTestHelpers.MakeStation(tick: 100);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1");
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room2"); // different room
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            // Manually set the bonus as if it was applied at tick 0 (expires at tick 20)
            student.proximityWorkModifier            = 1.1f;
            student.proximityWorkModifierExpiresAtTick = 80; // already in the past

            // Act: Tick at 100 (> expiry)
            _prox.Tick(station, _mood, null);

            // Assert: bonus must be reset
            Assert.AreEqual(1.0f, student.proximityWorkModifier, 0.001f,
                "proximityWorkModifier must reset to 1.0 after expiry.");
            Assert.AreEqual(-1, student.proximityWorkModifierExpiresAtTick,
                "proximityWorkModifierExpiresAtTick must reset to -1 after expiry.");
        }

        [Test]
        public void MentorWorkBonus_NotExpiredBeforeDurationTicks()
        {
            var station = ProximityTestHelpers.MakeStation(tick: 10);
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1");
            ProximityTestHelpers.AddToStation(station, student);

            // Apply a bonus that expires at tick 50 (not yet expired at tick 10)
            student.proximityWorkModifier            = 1.1f;
            student.proximityWorkModifierExpiresAtTick = 50;

            _prox.Tick(station, _mood, null);

            Assert.AreEqual(1.1f, student.proximityWorkModifier, 0.001f,
                "proximityWorkModifier must not reset before expiry tick.");
        }

        [Test]
        public void MentorWorkBonus_RefreshedEachTickWhilePairInSameModule()
        {
            var station = ProximityTestHelpers.MakeStation(tick: 0);
            var mentor  = ProximityTestHelpers.MakeCrewNpc("M", "room1");
            var student = ProximityTestHelpers.MakeCrewNpc("S", "room1");
            ProximityTestHelpers.AddToStation(station, mentor);
            ProximityTestHelpers.AddToStation(station, student);
            ProximityTestHelpers.MakeRelationship(station, mentor, student,
                                                   RelationshipType.Mentor, mentor.uid);

            // First tick
            _prox.Tick(station, _mood, null);
            int firstExpiry = student.proximityWorkModifierExpiresAtTick;

            // Advance tick and tick again — expiry must refresh
            station.tick = 5;
            _prox.Tick(station, _mood, null);

            Assert.Greater(student.proximityWorkModifierExpiresAtTick, firstExpiry,
                "Expiry must advance each tick while pair remains in the same module.");
        }
    }

    // ── Relationship type change tests ────────────────────────────────────────
    // (formerly "cache invalidation" — ProximitySystem reads directly from the
    //  RelationshipRegistry each tick, so changes take effect immediately)

    [TestFixture]
    public class ProximityRelationshipChangeTests
    {
        [Test]
        public void AfterFriendToEnemyChange_NextTick_AppliesEnemyPenalty()
        {
            var mood    = new MoodSystem();
            var prox    = new ProximitySystem();
            var station = ProximityTestHelpers.MakeStation(tick: 0);

            // Keep the same NPC instances across both ticks
            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", moodScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1", moodScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);

            var rec = ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Friend);

            // Tick 0: Friend — proximity_friend modifier applied, expiry = 0 + 20 = 20
            prox.Tick(station, mood, null);
            Assert.IsTrue(a.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "After first tick as Friends, NPC A must have proximity_friend modifier.");
            int friendExpiryAfterTick0 = a.moodModifiers.Find(m => m.eventId == "proximity_friend")
                                          .expiresAtTick;

            // Change relationship to Enemy without replacing the NPC instances
            rec.relationshipType = RelationshipType.Enemy;
            rec.affinityScore    = -10f;

            // Tick 1: Enemy — proximity_enemy must now be applied;
            //         proximity_friend must NOT have its expiry refreshed.
            station.tick = 1;
            prox.Tick(station, mood, null);

            Assert.IsTrue(a.moodModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "After Friend→Enemy change, NPC A must have proximity_enemy modifier on next tick.");
            Assert.IsTrue(b.moodModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "After Friend→Enemy change, NPC B must have proximity_enemy modifier on next tick.");

            // The proximity_friend modifier remains on the list (not yet expired by MoodSystem),
            // but its expiry was NOT refreshed — still at the tick-0 value (20, not 21).
            var friendMod = a.moodModifiers.Find(m => m.eventId == "proximity_friend");
            Assert.IsNotNull(friendMod,
                "proximity_friend modifier should still be on the list (not yet expired by MoodSystem).");
            Assert.AreEqual(friendExpiryAfterTick0, friendMod.expiresAtTick,
                "proximity_friend expiry must NOT be refreshed after the relationship changed to Enemy.");
        }

        [Test]
        public void AfterEnemyToFriendChange_NextTick_AppliesFriendBoost()
        {
            var mood    = new MoodSystem();
            var prox    = new ProximitySystem();
            var station = ProximityTestHelpers.MakeStation(tick: 0);

            var a = ProximityTestHelpers.MakeCrewNpc("A", "room1", moodScore: 50f);
            var b = ProximityTestHelpers.MakeCrewNpc("B", "room1", moodScore: 50f);
            ProximityTestHelpers.AddToStation(station, a);
            ProximityTestHelpers.AddToStation(station, b);

            var rec = ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Enemy);

            // Tick 0: Enemy
            prox.Tick(station, mood, null);
            Assert.IsTrue(a.moodModifiers.Exists(m => m.eventId == "proximity_enemy"),
                "Tick 0: NPC A must have proximity_enemy modifier.");

            // Change to Friend
            rec.relationshipType = RelationshipType.Friend;
            rec.affinityScore    = 25f;

            // Tick 1: Friend modifier must now be applied
            station.tick = 1;
            prox.Tick(station, mood, null);

            Assert.IsTrue(a.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "After Enemy→Friend change, NPC A must have proximity_friend modifier on next tick.");
        }
    }

    // ── Performance / stress tests ────────────────────────────────────────────

    [TestFixture]
    public class ProximityPerformanceTests
    {
        [Test]
        public void TwentyNpcs_SameModule_TickCompletesWithoutError()
        {
            var mood    = new MoodSystem();
            var prox    = new ProximitySystem();
            var station = ProximityTestHelpers.MakeStation(tick: 0);

            // Create 20 NPCs in the same module with Friend relationships between all pairs
            var npcs = new List<NPCInstance>();
            for (int i = 0; i < 20; i++)
            {
                var npc = ProximityTestHelpers.MakeCrewNpc(i.ToString(), "shared_room");
                ProximityTestHelpers.AddToStation(station, npc);
                npcs.Add(npc);
            }

            for (int i = 0; i < npcs.Count; i++)
                for (int j = i + 1; j < npcs.Count; j++)
                    ProximityTestHelpers.MakeRelationship(station, npcs[i], npcs[j], RelationshipType.Friend);

            // Should complete without exceptions
            Assert.DoesNotThrow(() => prox.Tick(station, mood, null),
                "ProximitySystem.Tick must not throw with 20 NPCs in a single module.");
        }

        [Test]
        public void NpcsInDifferentModules_OnlyProcessSameModulePairs()
        {
            var mood    = new MoodSystem();
            var prox    = new ProximitySystem();
            var station = ProximityTestHelpers.MakeStation(tick: 0);

            // 10 NPCs spread across 5 modules (2 each) — only 5 pairs should be evaluated
            for (int room = 0; room < 5; room++)
            {
                var a = ProximityTestHelpers.MakeCrewNpc($"r{room}a", $"room{room}", moodScore: 50f);
                var b = ProximityTestHelpers.MakeCrewNpc($"r{room}b", $"room{room}", moodScore: 50f);
                ProximityTestHelpers.AddToStation(station, a);
                ProximityTestHelpers.AddToStation(station, b);
                ProximityTestHelpers.MakeRelationship(station, a, b, RelationshipType.Friend);

                // cross-room pair: should NOT receive a boost
                if (room > 0)
                {
                    var prev = station.npcs[$"r{room - 1}a"];
                    ProximityTestHelpers.MakeRelationship(station, prev, a, RelationshipType.Friend);
                }
            }

            Assert.DoesNotThrow(() => prox.Tick(station, mood, null),
                "ProximitySystem.Tick must not throw for multi-module crew.");

            // Cross-module NPCs must not have received a boost
            var npcR0a = station.npcs["r0a"];
            var npcR1a = station.npcs["r1a"];
            // Both are only boosted by their same-room partner, not cross-room
            Assert.IsTrue(npcR0a.moodModifiers.Exists(m => m.eventId == "proximity_friend"),
                "NPC in room0 must receive boost from their room-mate.");
            Assert.AreEqual(1, npcR0a.moodModifiers.Count,
                "NPC must only receive one proximity_friend modifier (same module only).");
        }
    }
}
