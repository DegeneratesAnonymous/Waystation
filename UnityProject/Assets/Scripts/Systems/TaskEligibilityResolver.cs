// TaskEligibilityResolver — checks whether an NPC can perform a given task type.
//
// CanPerform checks:
//   1. NPC is not in mood Crisis (blocks all non-recreational tasks).
//   2. If the task type is hard-capability-locked (registered via RegisterCapability),
//      the NPC must hold the corresponding expertise in their chosenExpertise list.
//
// GetPerformancePenalty checks:
//   Returns a scalar < 1.0 when a task is soft-locked and the NPC lacks the expertise,
//   representing a performance reduction (e.g. 0.70 = 30% penalty).
//   Returns 1.0 when no penalty applies (task not soft-locked or NPC has expertise).
//
// Capability-unlocking expertise registers their task types at startup via
// ExpertiseDefinition.capabilityUnlocks / softCapabilityUnlocks →
// SkillSystem.RegisterAllCapabilities().
// No hardcoded task-expertise pairs exist here.
//
// Feature flag: SkillSystem.CapabilityChecksEnabled = false makes all capability-
// locked tasks universally accessible and all soft penalties inactive.
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class TaskEligibilityResolver
    {
        // Static dictionary: taskType → expertiseId for hard-locked tasks.
        private static readonly Dictionary<string, string> CapabilityRegistry =
            new Dictionary<string, string>();

        // Static dictionary: taskType → expertiseId for soft-locked tasks.
        // Missing expertise applies SoftLockPenalty to job performance, but does not block.
        private static readonly Dictionary<string, string> SoftLockRegistry =
            new Dictionary<string, string>();

        /// <summary>Performance scalar applied when a soft-locked task is attempted without the expertise.</summary>
        public const float SoftLockPenalty = 0.70f;

        // ── Registration (called at startup) ──────────────────────────────────

        /// <summary>
        /// Register a hard-capability-locked task type and the expertise that unlocks it.
        /// If called multiple times for the same task type, the last registration wins.
        /// </summary>
        public static void RegisterCapability(string taskType, string expertiseId)
        {
            CapabilityRegistry[taskType] = expertiseId;
        }

        /// <summary>
        /// Register a soft-capability-locked task type and the expertise that reduces the penalty.
        /// NPCs without the expertise can still perform the task at SoftLockPenalty performance.
        /// </summary>
        public static void RegisterSoftCapability(string taskType, string expertiseId)
        {
            SoftLockRegistry[taskType] = expertiseId;
        }

        /// <summary>Clear all registered hard and soft capabilities (used in tests / resets).</summary>
        public static void ClearCapabilities()
        {
            CapabilityRegistry.Clear();
            SoftLockRegistry.Clear();
        }

        // ── Main eligibility check ────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="npc"/> can be assigned <paramref name="taskType"/>.
        ///
        /// Blocking conditions:
        ///   • NPC is in mood Crisis (inCrisis == true) — blocks all tasks.
        ///   • Task is hard-capability-locked and NPC lacks the required expertise.
        ///
        /// Soft-locked tasks are NOT blocked by this method — use GetPerformancePenalty
        /// to apply the scalar penalty at the job execution site.
        ///
        /// When SkillSystem.CapabilityChecksEnabled is false, capability locks are
        /// ignored and only the Crisis check applies.
        /// </summary>
        public static bool CanPerform(NPCInstance npc, string taskType)
        {
            if (npc == null) return false;

            // Treat null/empty task as not capability-locked
            if (string.IsNullOrEmpty(taskType)) return true;

            // Crisis blocks all tasks
            if (npc.inCrisis) return false;

            // Hard-lock capability check (may be disabled globally)
            if (SkillSystem.CapabilityChecksEnabled &&
                CapabilityRegistry.TryGetValue(taskType, out var requiredExpertise))
            {
                if (!npc.chosenExpertise.Contains(requiredExpertise))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the performance penalty scalar for a soft-locked task.
        /// 1.0 = no penalty (task is not soft-locked, or NPC has the required expertise).
        /// SoftLockPenalty (0.70) = 30% performance reduction when expertise is absent.
        /// Always returns 1.0 when SkillSystem.CapabilityChecksEnabled is false.
        /// </summary>
        public static float GetPerformancePenalty(NPCInstance npc, string taskType)
        {
            if (npc == null) return 1.0f;
            if (!SkillSystem.CapabilityChecksEnabled) return 1.0f;
            if (!SoftLockRegistry.TryGetValue(taskType, out var reqExp)) return 1.0f;
            return npc.chosenExpertise.Contains(reqExp) ? 1.0f : SoftLockPenalty;
        }

        /// <summary>
        /// Returns true if <paramref name="taskType"/> is hard-capability-locked.
        /// </summary>
        public static bool IsCapabilityLocked(string taskType)
            => CapabilityRegistry.ContainsKey(taskType);

        /// <summary>
        /// Returns true if <paramref name="taskType"/> is soft-capability-locked.
        /// </summary>
        public static bool IsSoftLocked(string taskType)
            => SoftLockRegistry.ContainsKey(taskType);

        /// <summary>
        /// Returns the expertise ID required to hard-unlock a capability-locked task,
        /// or null if the task is not hard-capability-locked.
        /// </summary>
        public static string GetRequiredExpertise(string taskType)
            => CapabilityRegistry.TryGetValue(taskType, out var id) ? id : null;

        /// <summary>
        /// Returns the expertise ID that removes the soft-lock penalty for a task,
        /// or null if the task is not soft-locked.
        /// </summary>
        public static string GetSoftLockExpertise(string taskType)
            => SoftLockRegistry.TryGetValue(taskType, out var id) ? id : null;
    }
}
