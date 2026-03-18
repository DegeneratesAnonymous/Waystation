// TaskEligibilityResolver — checks whether an NPC can perform a given task type.
//
// CanPerform checks:
//   1. NPC is not in mood Crisis (blocks all non-recreational tasks).
//   2. If the task type is capability-locked (registered via RegisterCapability),
//      the NPC must hold the corresponding expertise in their chosenExpertise list.
//
// Capability-unlocking expertise registers their task types at startup via
// ExpertiseDefinition.capabilityUnlocks → SkillSystem.RegisterAllCapabilities().
// No hardcoded task-expertise pairs exist here.
//
// Feature flag: SkillSystem.CapabilityChecksEnabled = false makes all capability-
// locked tasks universally accessible without removing expertise data.
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class TaskEligibilityResolver
    {
        // Static dictionary: taskType → expertiseId that unlocks it.
        // Populated at startup from ExpertiseDefinition.capabilityUnlocks.
        private static readonly Dictionary<string, string> CapabilityRegistry =
            new Dictionary<string, string>();

        // ── Registration (called at startup) ──────────────────────────────────

        /// <summary>
        /// Register a capability-locked task type and the expertise that unlocks it.
        /// If called multiple times for the same task type, the last registration wins.
        /// </summary>
        public static void RegisterCapability(string taskType, string expertiseId)
        {
            CapabilityRegistry[taskType] = expertiseId;
        }

        /// <summary>Clear all registered capabilities (used in tests / resets).</summary>
        public static void ClearCapabilities()
        {
            CapabilityRegistry.Clear();
        }

        // ── Main eligibility check ────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="npc"/> can be assigned <paramref name="taskType"/>.
        ///
        /// Blocking conditions:
        ///   • NPC is in mood Crisis (inCrisis == true) — blocks all tasks.
        ///   • Task is capability-locked and NPC lacks the required expertise.
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

            // Capability check (may be disabled globally)
            if (SkillSystem.CapabilityChecksEnabled &&
                CapabilityRegistry.TryGetValue(taskType, out var requiredExpertise))
            {
                if (!npc.chosenExpertise.Contains(requiredExpertise))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if <paramref name="taskType"/> is capability-locked.
        /// </summary>
        public static bool IsCapabilityLocked(string taskType)
            => CapabilityRegistry.ContainsKey(taskType);

        /// <summary>
        /// Returns the expertise ID required to perform a capability-locked task,
        /// or null if the task is not capability-locked.
        /// </summary>
        public static string GetRequiredExpertise(string taskType)
            => CapabilityRegistry.TryGetValue(taskType, out var id) ? id : null;
    }
}
