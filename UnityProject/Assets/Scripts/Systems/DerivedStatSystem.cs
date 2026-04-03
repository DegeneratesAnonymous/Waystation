using UnityEngine;
using System.Linq;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Derived passive stats that recalculate automatically from source stats.
    /// Follows the same pattern as SkillSystem.GetPerceptionScore (WO-NPC-013).
    /// Implements WO-JOB-003: Memory and Leadership Capacity.
    /// </summary>
    public static class DerivedStatSystem
    {
        // ── Memory ────────────────────────────────────────────────────────────

        /// <summary>
        /// Memory = INT + WIS / 2  (integer division on WIS before addition).
        /// Represents how many concurrent tasks an NPC can mentally juggle.
        /// </summary>
        public static int GetMemoryScore(NPCInstance npc)
            => npc.abilityScores.INT + npc.abilityScores.WIS / 2;

        /// <summary>
        /// Converts a Memory score into a personal task queue depth.
        /// QueueDepth = max(1, floor(Memory / 4)).
        /// </summary>
        public static int GetQueueDepth(NPCInstance npc)
            => Mathf.Max(1, GetMemoryScore(npc) / 4);

        /// <summary>
        /// Convenience overload: returns queue depth from a pre-computed memory score.
        /// </summary>
        public static int GetQueueDepth(int memoryScore)
            => Mathf.Max(1, memoryScore / 4);

        // ── Leadership Capacity ───────────────────────────────────────────────

        private const string SocialSkillId     = "skill_social";
        private const string ArticulationExpId = "exp_articulation";
        private const int    ArticulationBonus = 2;

        /// <summary>
        /// Leadership Capacity = floor(Social skill level / 2) + 1 + articulation bonus.
        /// Articulation bonus = +2 if the NPC has claimed exp_articulation, +0 otherwise.
        /// Determines how many direct reports a lead NPC can manage before soft-cap
        /// degradation kicks in (WO-JOB-002).
        /// </summary>
        public static int GetLeadershipCapacity(NPCInstance npc)
        {
            int socialLevel = 0;
            if (npc.skillInstances != null)
            {
                var social = npc.skillInstances.FirstOrDefault(s => s.skillId == SocialSkillId);
                if (social != null)
                    socialLevel = social.Level;
            }

            int baseCapacity = socialLevel / 2 + 1;
            int bonus = (npc.chosenExpertise != null && npc.chosenExpertise.Contains(ArticulationExpId))
                        ? ArticulationBonus
                        : 0;
            return baseCapacity + bonus;
        }
    }
}
