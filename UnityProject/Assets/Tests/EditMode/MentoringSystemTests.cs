// MentoringSystemTests — EditMode unit tests for NPC-008 Mentor/Student bond.
//
// Validates:
//   • Co-working tick accumulator increments correctly per tick for room-sharing pairs
//   • Mentor/Student bond forms at threshold with correct sub-type assignment
//   • Bond does not form below skill threshold, below affinity threshold, or before tick threshold
//   • XP multiplier formula produces correct values at min, mid, and max for each modifier
//   • High-Communication, high-affinity mentor yields a higher multiplier than low values
//   • Mentor in mood crisis reduces the XP multiplier
//   • Mentor/Student pair receives Friend proximity bonus in ProximitySystem
//   • Mentor bond decays to Friend after 7-day co-working inactivity
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class MentoringTestHelpers
    {
        public static StationState MakeStation(int tick = 0)
        {
            var s = new StationState { stationName = "MentoringTestStation", tick = tick };
            return s;
        }

        /// <summary>Creates a crew NPC with one skill at the given level.</summary>
        public static NPCInstance MakeCrewNpc(
            string uid       = null,
            string skillId   = "skill.engineering",
            int    skillLevel = 0,
            float  moodScore  = 50f,
            string location  = "workshop")
        {
            var npc = new NPCInstance
            {
                uid        = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name       = "TestNPC_" + (uid ?? "x"),
                moodScore  = moodScore,
                location   = location,
            };
            npc.statusTags.Add("crew");
            if (skillLevel > 0)
            {
                // Set XP to exactly reach the desired level: level = floor(sqrt(xp/100))
                // So xp = level^2 * 100
                npc.skillInstances.Add(new SkillInstance
                {
                    skillId   = skillId,
                    currentXP = skillLevel * skillLevel * 100f
                });
            }
            return npc;
        }

        /// <summary>Sets the Communication (skill.social) skill level on an NPC.</summary>
        public static void SetCommSkill(NPCInstance npc, int level)
        {
            foreach (var inst in npc.skillInstances)
            {
                if (inst.skillId == MentoringSystem.CommunicationSkillId)
                {
                    inst.currentXP = level * level * 100f;
                    return;
                }
            }
            npc.skillInstances.Add(new SkillInstance
            {
                skillId   = MentoringSystem.CommunicationSkillId,
                currentXP = level * level * 100f
            });
        }

        /// <summary>
        /// Establishes a Friend-level relationship record so the mentoring bond can form.
        /// </summary>
        public static RelationshipRecord MakeFriendRelationship(
            StationState station, NPCInstance a, NPCInstance b, float affinity = 25f)
        {
            var rec = RelationshipRegistry.GetOrCreate(station, a.uid, b.uid);
            rec.affinityScore     = affinity;
            rec.relationshipType  = RelationshipType.Friend;
            return rec;
        }
    }

    // ── Co-working accumulator tests ──────────────────────────────────────────

    [TestFixture]
    public class CoWorkingAccumulatorTests
    {
        private MentoringSystem _mentoring;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            _mentoring = new MentoringSystem();
            _station   = MentoringTestHelpers.MakeStation();
        }

        [Test]
        public void CoWorkingTicks_IncrementPerTickForRoomSharingPair()
        {
            var mentor  = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 10, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering",  2, location: "room1");
            _station.npcs[mentor.uid]  = mentor;
            _station.npcs[student.uid] = student;

            MentoringTestHelpers.MakeFriendRelationship(_station, mentor, student);

            _mentoring.Tick(_station);
            _mentoring.Tick(_station);
            _mentoring.Tick(_station);

            var rec = RelationshipRegistry.Get(_station, mentor.uid, student.uid);
            Assert.AreEqual(3, rec.coWorkingTicks, "Three ticks should yield coWorkingTicks == 3.");
        }

        [Test]
        public void CoWorkingTicks_NotIncrementedWhenInDifferentRooms()
        {
            var a = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 10, location: "room1");
            var b = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering",  2, location: "room2");
            _station.npcs[a.uid] = a;
            _station.npcs[b.uid] = b;

            _mentoring.Tick(_station);
            _mentoring.Tick(_station);

            // No record should be created for NPCs that never share a room.
            var rec = RelationshipRegistry.Get(_station, a.uid, b.uid);
            Assert.IsNull(rec, "No record should be created for NPCs in different rooms.");
        }

        [Test]
        public void LastCoWorkingTick_UpdatedEachCoWorkingTick()
        {
            var mentor  = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 10, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering",  2, location: "room1");
            _station.npcs[mentor.uid]  = mentor;
            _station.npcs[student.uid] = student;

            MentoringTestHelpers.MakeFriendRelationship(_station, mentor, student);

            _station.tick = 5;
            _mentoring.Tick(_station);

            var rec = RelationshipRegistry.Get(_station, mentor.uid, student.uid);
            Assert.AreEqual(5, rec.lastCoWorkingTick, "lastCoWorkingTick should match station tick.");
        }
    }

    // ── Bond formation tests ──────────────────────────────────────────────────

    [TestFixture]
    public class MentorBondFormationTests
    {
        private MentoringSystem _mentoring;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            _mentoring = new MentoringSystem();
            _station   = MentoringTestHelpers.MakeStation();
        }

        /// <summary>Run the mentoring tick enough times to reach the threshold.</summary>
        private void RunToThreshold(NPCInstance a, NPCInstance b)
        {
            for (int i = 0; i < MentoringSystem.CoWorkingTicksThreshold; i++)
                _mentoring.Tick(_station);
        }

        [Test]
        public void BondForms_WhenSkillThresholdAndGapAndAffinityMet()
        {
            var mentor  = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 10, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering",  2, location: "room1");
            _station.npcs[mentor.uid]  = mentor;
            _station.npcs[student.uid] = student;
            MentoringTestHelpers.MakeFriendRelationship(_station, mentor, student);

            RunToThreshold(mentor, student);

            var rec = RelationshipRegistry.Get(_station, mentor.uid, student.uid);
            Assert.IsNotNull(rec, "Relationship record must exist.");
            Assert.AreEqual(RelationshipType.Mentor, rec.relationshipType,
                "Bond should form as Mentor sub-type.");
            Assert.AreEqual(mentor.uid, rec.mentorUid,
                "mentorUid should be the higher-skilled NPC.");
        }

        [Test]
        public void BondDoesNotForm_WhenMentorSkillBelowMinimum()
        {
            // mentor skill = 6, below MentorMinSkillLevel (8)
            var a = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 6, location: "room1");
            var b = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering", 2, location: "room1");
            _station.npcs[a.uid] = a;
            _station.npcs[b.uid] = b;
            MentoringTestHelpers.MakeFriendRelationship(_station, a, b);

            RunToThreshold(a, b);

            var rec = RelationshipRegistry.Get(_station, a.uid, b.uid);
            Assert.AreNotEqual(RelationshipType.Mentor, rec?.relationshipType,
                "Bond must not form when mentor skill is below MentorMinSkillLevel.");
        }

        [Test]
        public void BondDoesNotForm_WhenSkillGapTooSmall()
        {
            // mentor=9, student=7: gap=2, below SkillLevelGapRequired (3)
            var a = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 9, location: "room1");
            var b = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering", 7, location: "room1");
            _station.npcs[a.uid] = a;
            _station.npcs[b.uid] = b;
            MentoringTestHelpers.MakeFriendRelationship(_station, a, b);

            RunToThreshold(a, b);

            var rec = RelationshipRegistry.Get(_station, a.uid, b.uid);
            Assert.AreNotEqual(RelationshipType.Mentor, rec?.relationshipType,
                "Bond must not form when skill gap is below required threshold.");
        }

        [Test]
        public void BondDoesNotForm_WhenAffinityBelowFriendThreshold()
        {
            // Affinity = 15, below Friend threshold of 20.
            var mentor  = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 10, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering",  2, location: "room1");
            _station.npcs[mentor.uid]  = mentor;
            _station.npcs[student.uid] = student;
            MentoringTestHelpers.MakeFriendRelationship(_station, mentor, student, affinity: 15f);
            var rec = RelationshipRegistry.Get(_station, mentor.uid, student.uid);
            rec.relationshipType = RelationshipType.Acquaintance;

            RunToThreshold(mentor, student);

            Assert.AreNotEqual(RelationshipType.Mentor, rec.relationshipType,
                "Bond must not form when affinity is below Friend threshold.");
        }

        [Test]
        public void BondDoesNotForm_BeforeCoWorkingThreshold()
        {
            var mentor  = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering", 10, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering",  2, location: "room1");
            _station.npcs[mentor.uid]  = mentor;
            _station.npcs[student.uid] = student;
            MentoringTestHelpers.MakeFriendRelationship(_station, mentor, student);

            // Run only half the required ticks.
            for (int i = 0; i < MentoringSystem.CoWorkingTicksThreshold / 2; i++)
                _mentoring.Tick(_station);

            var rec = RelationshipRegistry.Get(_station, mentor.uid, student.uid);
            Assert.AreNotEqual(RelationshipType.Mentor, rec?.relationshipType,
                "Bond must not form before CoWorkingTicksThreshold is reached.");
        }

        [Test]
        public void StudentIsLowerSkilledNpc_MentorIsHigherSkilledNpc()
        {
            // b has higher skill, should be identified as mentor
            var a = MentoringTestHelpers.MakeCrewNpc("A", "skill.engineering",  2, location: "room1");
            var b = MentoringTestHelpers.MakeCrewNpc("B", "skill.engineering", 10, location: "room1");
            _station.npcs[a.uid] = a;
            _station.npcs[b.uid] = b;
            MentoringTestHelpers.MakeFriendRelationship(_station, a, b);

            RunToThreshold(a, b);

            var rec = RelationshipRegistry.Get(_station, a.uid, b.uid);
            Assert.AreEqual(RelationshipType.Mentor, rec.relationshipType,
                "Bond should form.");
            Assert.AreEqual(b.uid, rec.mentorUid,
                "mentorUid should be the higher-skilled NPC (b).");
        }
    }

    // ── XP multiplier formula tests ───────────────────────────────────────────

    [TestFixture]
    public class MentoringXpMultiplierTests
    {
        private MentoringSystem _mentoring;
        private StationState    _station;

        [SetUp]
        public void SetUp()
        {
            _mentoring = new MentoringSystem();
            _station   = MentoringTestHelpers.MakeStation();
        }

        /// <summary>
        /// Creates a Mentor/Student bond directly without running the accumulator.
        /// </summary>
        private (NPCInstance mentor, NPCInstance student) SetupBond(
            int   mentorSkillLevel = 10,
            int   commLevel        = 0,
            float affinity         = 50f,
            float mentorMood       = 50f)
        {
            var mentor  = MentoringTestHelpers.MakeCrewNpc("M", "skill.engineering", mentorSkillLevel,
                              moodScore: mentorMood, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("S", "skill.engineering", 2,
                              location: "room1");

            if (commLevel > 0)
                MentoringTestHelpers.SetCommSkill(mentor, commLevel);

            _station.npcs[mentor.uid]  = mentor;
            _station.npcs[student.uid] = student;

            var rec = RelationshipRegistry.GetOrCreate(_station, mentor.uid, student.uid);
            rec.affinityScore    = affinity;
            rec.relationshipType = RelationshipType.Mentor;
            rec.mentorUid        = mentor.uid;

            return (mentor, student);
        }

        [Test]
        public void Multiplier_IsOneWhenNoMentorBond()
        {
            var student = MentoringTestHelpers.MakeCrewNpc("S", location: "room1");
            _station.npcs[student.uid] = student;

            float mult = _mentoring.GetMentoringXPMultiplier(student, _station);
            Assert.AreEqual(1f, mult, 0.0001f, "Multiplier must be 1.0 with no bond.");
        }

        [Test]
        public void Multiplier_IsGreaterThanOne_WithValidBond()
        {
            var (_, student) = SetupBond(mentorSkillLevel: 10, commLevel: 5, affinity: 50f, mentorMood: 50f);

            float mult = _mentoring.GetMentoringXPMultiplier(student, _station);
            Assert.Greater(mult, 1f, "Multiplier must exceed 1.0 when mentor is present.");
        }

        [Test]
        public void Multiplier_IsOne_WhenMentorInDifferentRoom()
        {
            var (mentor, student) = SetupBond(mentorSkillLevel: 10, commLevel: 5, affinity: 50f);
            mentor.location = "other_room";   // mentor moves away

            float mult = _mentoring.GetMentoringXPMultiplier(student, _station);
            Assert.AreEqual(1f, mult, 0.0001f,
                "Multiplier must be 1.0 when mentor is not in the same room.");
        }

        [Test]
        public void Multiplier_HighCommHighAffinity_GreaterThanLowCommLowAffinity()
        {
            // High Communication + high affinity setup
            var (_, studentHigh) = SetupBond(mentorSkillLevel: 10, commLevel: 15, affinity: 80f, mentorMood: 50f);
            float multHigh = _mentoring.GetMentoringXPMultiplier(studentHigh, _station);

            // Clear state
            _station = MentoringTestHelpers.MakeStation();

            // Low Communication + low affinity setup
            var (_, studentLow) = SetupBond(mentorSkillLevel: 10, commLevel: 0, affinity: 20f, mentorMood: 50f);
            float multLow = _mentoring.GetMentoringXPMultiplier(studentLow, _station);

            Assert.Greater(multHigh, multLow,
                "High Communication and high affinity mentor must yield a greater multiplier.");
        }

        [Test]
        public void Multiplier_MentorInCrisis_ReducesMultiplier()
        {
            // Normal mood (50) vs crisis mood (<20)
            var (_, studentNormal) = SetupBond(mentorSkillLevel: 10, commLevel: 5, affinity: 50f, mentorMood: 50f);
            float multNormal = _mentoring.GetMentoringXPMultiplier(studentNormal, _station);

            _station = MentoringTestHelpers.MakeStation();

            var (_, studentCrisis) = SetupBond(mentorSkillLevel: 10, commLevel: 5, affinity: 50f, mentorMood: 10f);
            float multCrisis = _mentoring.GetMentoringXPMultiplier(studentCrisis, _station);

            Assert.Greater(multNormal, multCrisis,
                "Mentor in mood crisis must yield a lower XP multiplier.");
        }

        [Test]
        public void Multiplier_AtMinValues_IsOne()
        {
            // skillLevel=0 (mentor has no skill — edge case), commLevel=0, affinity=0, moodScore=0
            var (_, student) = SetupBond(mentorSkillLevel: 0, commLevel: 0, affinity: 0f, mentorMood: 0f);
            float mult = _mentoring.GetMentoringXPMultiplier(student, _station);
            Assert.AreEqual(1f, mult, 0.0001f, "Multiplier should be 1.0 when all factors are zero.");
        }

        [Test]
        public void Multiplier_AtMaxValues_IsCorrect()
        {
            // skillLevel=20, commLevel=20, affinity=100, moodScore=100
            var (_, student) = SetupBond(mentorSkillLevel: 20, commLevel: 20, affinity: 100f, mentorMood: 100f);
            float mult = _mentoring.GetMentoringXPMultiplier(student, _station);

            // Expected: 1 + (1*0.5 + 1*0.3 + 1*0.2) * (100/50) = 1 + 1.0 * 2 = 3.0
            Assert.AreEqual(3f, mult, 0.001f, "Multiplier at max values should be 3.0.");
        }

        [Test]
        public void Multiplier_AtMidValues_IsCorrect()
        {
            // skillLevel=10, commLevel=10, affinity=50, moodScore=50 (baseline)
            var (_, student) = SetupBond(mentorSkillLevel: 10, commLevel: 10, affinity: 50f, mentorMood: 50f);
            float mult = _mentoring.GetMentoringXPMultiplier(student, _station);

            // Expected: 1 + (0.5*0.5 + 0.5*0.3 + 0.5*0.2) * (50/50)
            //         = 1 + (0.25 + 0.15 + 0.10) * 1.0 = 1 + 0.5 = 1.5
            Assert.AreEqual(1.5f, mult, 0.001f, "Multiplier at mid values should be 1.5.");
        }
    }

    // ── ProximitySystem integration test ──────────────────────────────────────

    [TestFixture]
    public class MentorProximityBonusTests
    {
        [Test]
        public void MentorStudent_InSameRoom_ReceivesFriendProximityBonus()
        {
            var station = MentoringTestHelpers.MakeStation(tick: 0);
            var mood    = new MoodSystem();
            var rels    = new RelationshipRegistry();
            var prox    = new ProximitySystem();

            var mentor  = MentoringTestHelpers.MakeCrewNpc("M", location: "room1", moodScore: 50f);
            var student = MentoringTestHelpers.MakeCrewNpc("S", location: "room1", moodScore: 50f);
            station.npcs[mentor.uid]  = mentor;
            station.npcs[student.uid] = student;

            var rec = RelationshipRegistry.GetOrCreate(station, mentor.uid, student.uid);
            rec.affinityScore    = 25f;
            rec.relationshipType = RelationshipType.Mentor;
            rec.mentorUid        = mentor.uid;

            prox.Tick(station, mood, rels);

            bool mentorHasBonus  = mentor.moodModifiers.Exists(m => m.eventId == "proximity_friend");
            bool studentHasBonus = student.moodModifiers.Exists(m => m.eventId == "proximity_friend");
            Assert.IsTrue(mentorHasBonus,  "Mentor must receive proximity_friend mood modifier.");
            Assert.IsTrue(studentHasBonus, "Student must receive proximity_friend mood modifier.");
        }
    }

    // ── Bond decay tests ──────────────────────────────────────────────────────

    [TestFixture]
    public class MentorBondDecayTests
    {
        [Test]
        public void MentorBond_DecaysToFriend_AfterSevenDayInactivity()
        {
            var station    = MentoringTestHelpers.MakeStation(tick: 0);
            var registry   = new RelationshipRegistry();
            var mentor     = MentoringTestHelpers.MakeCrewNpc("M", location: "room1");
            var student    = MentoringTestHelpers.MakeCrewNpc("S", location: "room1");
            station.npcs[mentor.uid]  = mentor;
            station.npcs[student.uid] = student;

            var rec = RelationshipRegistry.GetOrCreate(station, mentor.uid, student.uid);
            rec.affinityScore    = 25f;
            rec.relationshipType = RelationshipType.Mentor;
            rec.mentorUid        = mentor.uid;
            rec.lastCoWorkingTick = 0;

            // Advance station tick past the 7-day inactivity window.
            station.tick = RelationshipRegistry.DecayIntervalTicks + 1;
            registry.Tick(station, null);

            Assert.AreNotEqual(RelationshipType.Mentor, rec.relationshipType,
                "Mentor bond must not persist after 7-day co-working inactivity.");
            Assert.IsNull(rec.mentorUid,
                "mentorUid must be cleared when the bond lapses.");
            // Affinity (25) is still >= Friend threshold (20), so type should be Friend.
            Assert.AreEqual(RelationshipType.Friend, rec.relationshipType,
                "Bond should decay to Friend when affinity is still >= 20.");
        }

        [Test]
        public void MentorBond_Preserved_WhenCoWorkingContinues()
        {
            var station  = MentoringTestHelpers.MakeStation(tick: 0);
            var registry = new RelationshipRegistry();
            var mentoring = new MentoringSystem();

            var mentor  = MentoringTestHelpers.MakeCrewNpc("M", "skill.engineering", 10, location: "room1");
            var student = MentoringTestHelpers.MakeCrewNpc("S", "skill.engineering",  2, location: "room1");
            station.npcs[mentor.uid]  = mentor;
            station.npcs[student.uid] = student;
            MentoringTestHelpers.MakeFriendRelationship(station, mentor, student);

            // Form the bond.
            for (int i = 0; i < MentoringSystem.CoWorkingTicksThreshold; i++)
                mentoring.Tick(station);

            var rec = RelationshipRegistry.Get(station, mentor.uid, student.uid);
            Assert.AreEqual(RelationshipType.Mentor, rec.relationshipType, "Bond should have formed.");

            // Continue co-working past 7-day mark — bond must persist.
            int decayTick = RelationshipRegistry.DecayIntervalTicks + 1;
            for (int tick = 1; tick <= decayTick; tick++)
            {
                station.tick = tick;
                mentoring.Tick(station);
                registry.Tick(station, null);
            }

            Assert.AreEqual(RelationshipType.Mentor, rec.relationshipType,
                "Mentor bond must persist while co-working continues.");
        }
    }
}
