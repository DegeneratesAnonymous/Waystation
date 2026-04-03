// DerivedStatSystemTests — EditMode unit tests for WO-JOB-003 derived stats.
//
// Validates:
//   • Memory formula: INT + WIS/2 (integer division)
//   • QueueDepth = max(1, floor(Memory / 4)) at each tier boundary
//   • Memory recalculates on INT change and WIS change
//   • Leadership Capacity: floor(Social level / 2) + 1 with and without Articulation
//   • Leadership Capacity recalculates on Social level-up and Articulation claim
//   • Acceptance criteria from issue:
//     - INT 14, WIS 10 → Memory 19, queue depth 4
//     - WIS changes to 14 → Memory 21, queue depth 5
//     - Social level 8, no Articulation → Leadership 5
//     - Social level 8, with Articulation → Leadership 7
//     - Social level 5, with Articulation → Leadership 5
using NUnit.Framework;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class DerivedStatSystemTests
    {
        private static NPCInstance MakeNpc(int INT, int WIS, int CHA = 8, int socialXp = 0, bool hasArticulation = false)
        {
            var npc = new NPCInstance
            {
                uid = "test_npc",
                name = "Test NPC",
                abilityScores = new AbilityScores { INT = INT, WIS = WIS, CHA = CHA }
            };
            if (socialXp > 0)
            {
                npc.skillInstances.Add(new SkillInstance
                {
                    skillId = "skill_social",
                    currentXP = socialXp
                });
            }
            if (hasArticulation)
            {
                npc.chosenExpertise.Add("exp_articulation");
            }
            return npc;
        }

        // Social skill level = floor(sqrt(xp / 100))
        // Level 1 = 100 XP, Level 5 = 2500 XP, Level 8 = 6400 XP, Level 12 = 14400 XP
        private static int XpForLevel(int level) => level * level * 100;

        // ── Memory formula ───────────────────────────────────────────────────

        [Test]
        public void Memory_INT14_WIS10_Equals19()
        {
            var npc = MakeNpc(INT: 14, WIS: 10);
            Assert.AreEqual(19, DerivedStatSystem.GetMemoryScore(npc));
        }

        [Test]
        public void Memory_INT14_WIS14_Equals21()
        {
            var npc = MakeNpc(INT: 14, WIS: 14);
            Assert.AreEqual(21, DerivedStatSystem.GetMemoryScore(npc));
        }

        [Test]
        public void Memory_MinStats_INT1_WIS1_Equals1()
        {
            var npc = MakeNpc(INT: 1, WIS: 1);
            Assert.AreEqual(1, DerivedStatSystem.GetMemoryScore(npc));
        }

        [Test]
        public void Memory_MaxStats_INT20_WIS20_Equals30()
        {
            var npc = MakeNpc(INT: 20, WIS: 20);
            Assert.AreEqual(30, DerivedStatSystem.GetMemoryScore(npc));
        }

        [Test]
        public void Memory_MidStats_INT10_WIS10_Equals15()
        {
            var npc = MakeNpc(INT: 10, WIS: 10);
            Assert.AreEqual(15, DerivedStatSystem.GetMemoryScore(npc));
        }

        // ── QueueDepth conversion ────────────────────────────────────────────

        [Test]
        public void QueueDepth_Memory4_Returns1()
        {
            Assert.AreEqual(1, DerivedStatSystem.GetQueueDepth(4));
        }

        [Test]
        public void QueueDepth_Memory7_Returns1()
        {
            Assert.AreEqual(1, DerivedStatSystem.GetQueueDepth(7));
        }

        [Test]
        public void QueueDepth_Memory8_Returns2()
        {
            Assert.AreEqual(2, DerivedStatSystem.GetQueueDepth(8));
        }

        [Test]
        public void QueueDepth_Memory11_Returns2()
        {
            Assert.AreEqual(2, DerivedStatSystem.GetQueueDepth(11));
        }

        [Test]
        public void QueueDepth_Memory12_Returns3()
        {
            Assert.AreEqual(3, DerivedStatSystem.GetQueueDepth(12));
        }

        [Test]
        public void QueueDepth_Memory15_Returns3()
        {
            Assert.AreEqual(3, DerivedStatSystem.GetQueueDepth(15));
        }

        [Test]
        public void QueueDepth_Memory16_Returns4()
        {
            Assert.AreEqual(4, DerivedStatSystem.GetQueueDepth(16));
        }

        [Test]
        public void QueueDepth_Memory19_Returns4()
        {
            Assert.AreEqual(4, DerivedStatSystem.GetQueueDepth(19));
        }

        [Test]
        public void QueueDepth_Memory20_Returns5()
        {
            Assert.AreEqual(5, DerivedStatSystem.GetQueueDepth(20));
        }

        [Test]
        public void QueueDepth_Memory1_Returns1_MinClamped()
        {
            Assert.AreEqual(1, DerivedStatSystem.GetQueueDepth(1));
        }

        // ── Acceptance criteria: Memory recalculates on stat change ──────────

        [Test]
        public void Memory_RecalculatesOnWISChange()
        {
            var npc = MakeNpc(INT: 14, WIS: 10);
            Assert.AreEqual(19, DerivedStatSystem.GetMemoryScore(npc));
            Assert.AreEqual(4, DerivedStatSystem.GetQueueDepth(npc));

            npc.abilityScores.WIS = 14;
            Assert.AreEqual(21, DerivedStatSystem.GetMemoryScore(npc));
            Assert.AreEqual(5, DerivedStatSystem.GetQueueDepth(npc));
        }

        [Test]
        public void Memory_RecalculatesOnINTChange()
        {
            var npc = MakeNpc(INT: 10, WIS: 10);
            Assert.AreEqual(15, DerivedStatSystem.GetMemoryScore(npc));

            npc.abilityScores.INT = 16;
            Assert.AreEqual(21, DerivedStatSystem.GetMemoryScore(npc));
        }

        // ── GetMemoryDepth acceptance ────────────────────────────────────────

        [Test]
        public void GetQueueDepth_Memory14_Returns3()
        {
            // Given GetMemoryDepth(npcUid) is called, when the NPC has Memory 14, then it returns 3
            var npc = MakeNpc(INT: 10, WIS: 8); // Memory = 10 + 8/2 = 14
            Assert.AreEqual(14, DerivedStatSystem.GetMemoryScore(npc));
            Assert.AreEqual(3, DerivedStatSystem.GetQueueDepth(npc));
        }

        // ── Leadership Capacity formula ──────────────────────────────────────

        [Test]
        public void Leadership_SocialLevel0_NoArticulation_Returns1()
        {
            var npc = MakeNpc(INT: 8, WIS: 8);
            Assert.AreEqual(1, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel1_NoArticulation_Returns1()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(1));
            Assert.AreEqual(1, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel3_NoArticulation_Returns2()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(3));
            Assert.AreEqual(2, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel5_NoArticulation_Returns3()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(5));
            Assert.AreEqual(3, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel8_NoArticulation_Returns5()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(8));
            Assert.AreEqual(5, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel12_NoArticulation_Returns7()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(12));
            Assert.AreEqual(7, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        // ── Acceptance criteria: Leadership with Articulation ─────────────────

        [Test]
        public void Leadership_SocialLevel8_WithArticulation_Returns7()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(8), hasArticulation: true);
            Assert.AreEqual(7, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel5_WithArticulation_Returns5()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(5), hasArticulation: true);
            Assert.AreEqual(5, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_SocialLevel1_WithArticulation_Returns3()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(1), hasArticulation: true);
            Assert.AreEqual(3, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        // ── Leadership recalculates on Social level-up and Articulation claim ─

        [Test]
        public void Leadership_RecalculatesOnSocialLevelUp()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(5));
            Assert.AreEqual(3, DerivedStatSystem.GetLeadershipCapacity(npc));

            // Level up social to 8
            npc.skillInstances[0].currentXP = XpForLevel(8);
            Assert.AreEqual(5, DerivedStatSystem.GetLeadershipCapacity(npc));
        }

        [Test]
        public void Leadership_RecalculatesOnArticulationClaim()
        {
            var npc = MakeNpc(INT: 8, WIS: 8, socialXp: XpForLevel(8));
            Assert.AreEqual(5, DerivedStatSystem.GetLeadershipCapacity(npc));

            npc.chosenExpertise.Add("exp_articulation");
            Assert.AreEqual(7, DerivedStatSystem.GetLeadershipCapacity(npc));
        }
    }
}
