// CraftingSystem — EXP-005: workbench-based recipe execution.
//
// Workflow:
//   1. Player (or UI) calls QueueRecipe() to add a recipe entry to a workbench queue.
//      A recipe is only queueable when its research unlock tag is active in station tags.
//   2. Tick() detects "queued" entries and transitions them to "hauling":
//      an idle NPC with a crafting job is assigned and required materials are consumed
//      from nearby station foundation cargo into the entry's hauledMaterials.
//      When all materials are gathered, the entry transitions to "executing".
//   3. During "executing" the assigned NPC's job timer ticks down; execution progress
//      advances each tick by (1 / effectiveTimeTicks).
//      effectiveTimeTicks = baseTimeTicks / (0.5 + skillLevel / 10) — higher skill = faster.
//   4. On completion the output is placed in the nearest compatible storage foundation.
//      If hasQualityTiers == true the quality tier is logged alongside the output.
//   5. Workbench queues support multiple entries; the next entry begins immediately
//      after the previous completes (no player intervention required).
//
// Crafting/Manufacturing formula: DEX + INT/2 (Advanced skill — skill.crafting).
// Quality tiers (skill level 0–3 → standard, 4–7 → fine, 8–10 → superior) apply only
// to recipes with hasQualityTiers == true.
//
// Feature gate: FeatureFlags.CraftingSystem
//   When false, Tick() is a no-op. QueueRecipe() still validates and enqueues so that
//   recipe state is preserved, but no execution occurs until the flag is re-enabled.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class CraftingSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Crafting skill id used for execution speed and quality scaling.</summary>
        public const string CraftingSkillId = "skill.crafting";

        /// <summary>Job id checked when selecting an NPC to staff a workbench.</summary>
        public const string CraftingJobId = "job.craft";

        /// <summary>Ticks an assigned NPC's job timer is refreshed to (prevents reassignment).</summary>
        public const int JobTimerRefreshTicks = 5;

        // ── Private state ─────────────────────────────────────────────────────

        private readonly ContentRegistry _registry;
        private SkillSystem              _skillSystem;

        // ── Constructor ───────────────────────────────────────────────────────

        public CraftingSystem(ContentRegistry registry) => _registry = registry;

        /// <summary>Wire up SkillSystem after construction (called from GameManager).</summary>
        public void SetSkillSystem(SkillSystem skillSystem) => _skillSystem = skillSystem;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all recipes that are available at the given workbench foundation.
        /// A recipe is available when:
        ///   • Its requiredWorkbenchType matches the workbench's workbenchRoomType.
        ///   • Its unlockTag is either empty or active in station.activeTags.
        /// </summary>
        public List<RecipeDefinition> GetAvailableRecipes(string workbenchFoundationUid,
                                                          StationState station)
        {
            if (!station.foundations.TryGetValue(workbenchFoundationUid, out var bench))
                return new List<RecipeDefinition>();
            if (!_registry.Buildables.TryGetValue(bench.buildableId, out var benchDef))
                return new List<RecipeDefinition>();

            var result = new List<RecipeDefinition>();
            foreach (var recipe in _registry.Recipes.Values)
            {
                if (recipe.requiredWorkbenchType != benchDef.workbenchRoomType) continue;
                if (!string.IsNullOrEmpty(recipe.unlockTag) && !station.HasTag(recipe.unlockTag)) continue;
                result.Add(recipe);
            }
            return result;
        }

        /// <summary>
        /// Enqueues a recipe at the specified workbench.
        /// Returns (true, entryUid) on success, or (false, reason) on failure.
        /// Checks: workbench exists and is complete, recipe exists, recipe available (unlock tag).
        /// </summary>
        public (bool ok, string reason, string entryUid) QueueRecipe(
            string workbenchFoundationUid, string recipeId, StationState station)
        {
            if (!station.foundations.TryGetValue(workbenchFoundationUid, out var bench))
                return (false, $"Workbench '{workbenchFoundationUid}' not found.", null);
            if (bench.status != "complete")
                return (false, "Workbench is not yet built.", null);
            if (!_registry.Recipes.TryGetValue(recipeId, out var recipe))
                return (false, $"Recipe '{recipeId}' not found.", null);

            // Unlock tag check — recipe must be research-gated only when a tag is defined.
            if (!string.IsNullOrEmpty(recipe.unlockTag) && !station.HasTag(recipe.unlockTag))
                return (false, $"Recipe '{recipe.displayName}' is not unlocked (missing tag: {recipe.unlockTag}).", null);

            // Verify workbench type match.
            if (_registry.Buildables.TryGetValue(bench.buildableId, out var benchDef))
            {
                if (benchDef.workbenchRoomType != recipe.requiredWorkbenchType)
                    return (false, $"Recipe '{recipe.displayName}' requires workbench type '{recipe.requiredWorkbenchType}', but this workbench is '{benchDef.workbenchRoomType}'.", null);
            }

            var entry = WorkbenchQueueEntry.Create(recipeId);

            if (!station.workbenchQueues.ContainsKey(workbenchFoundationUid))
                station.workbenchQueues[workbenchFoundationUid] = new List<WorkbenchQueueEntry>();

            station.workbenchQueues[workbenchFoundationUid].Add(entry);
            return (true, null, entry.uid);
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (!FeatureFlags.CraftingSystem) return;

            foreach (var kv in station.workbenchQueues)
            {
                string workbenchUid = kv.Key;
                var    queue        = kv.Value;

                if (queue.Count == 0) continue;
                if (!station.foundations.TryGetValue(workbenchUid, out var bench)) continue;
                if (bench.status != "complete") continue;
                if (!_registry.Buildables.TryGetValue(bench.buildableId, out var benchDef)) continue;

                // Only process the head of the queue at any time.
                var entry = queue[0];

                switch (entry.status)
                {
                    case "queued":
                        TickQueued(entry, bench, benchDef, station);
                        break;
                    case "hauling":
                        TickHauling(entry, bench, benchDef, station);
                        break;
                    case "executing":
                        TickExecuting(entry, bench, benchDef, station);
                        break;
                    case "complete":
                        // Completed entry: remove it so the next recipe in queue begins.
                        queue.RemoveAt(0);
                        break;
                }
            }
        }

        // ── Phase: queued → hauling ───────────────────────────────────────────

        private void TickQueued(WorkbenchQueueEntry entry, FoundationInstance bench,
                                BuildableDefinition benchDef, StationState station)
        {
            if (!_registry.Recipes.TryGetValue(entry.recipeId, out var recipe)) return;

            // Re-check unlock tag (research chip may have been removed since queuing).
            if (!string.IsNullOrEmpty(recipe.unlockTag) && !station.HasTag(recipe.unlockTag)) return;

            // Find an idle NPC who can work this recipe (crafting job, minimum skill level).
            var npc = FindCraftingNpc(recipe, station);
            if (npc == null) return;

            // Assign NPC.
            entry.assignedNpcUid = npc.uid;
            npc.currentJobId     = CraftingJobId;
            npc.jobModuleUid     = bench.uid;
            RefreshNpcTimer(entry, station);

            // Determine quality tier now (at assignment time) from the NPC's crafting skill.
            if (recipe.hasQualityTiers)
                entry.outputQualityTier = ComputeQualityTier(npc);

            entry.status = "hauling";
            station.LogEvent($"{npc.name} begins hauling materials for {recipe.displayName} " +
                             $"at workbench [{bench.uid}].");
        }

        // ── Phase: hauling → executing ────────────────────────────────────────

        private void TickHauling(WorkbenchQueueEntry entry, FoundationInstance bench,
                                 BuildableDefinition benchDef, StationState station)
        {
            if (!_registry.Recipes.TryGetValue(entry.recipeId, out var recipe)) return;

            // Refresh assigned NPC timer.
            RefreshNpcTimer(entry, station);

            // Attempt to gather materials from station containers.
            bool allGathered = GatherMaterials(entry, recipe, station);

            if (allGathered)
            {
                entry.status = "executing";
                station.LogEvent($"Materials gathered — {recipe.displayName} execution begins.");
            }
        }

        // ── Phase: executing → complete ───────────────────────────────────────

        private void TickExecuting(WorkbenchQueueEntry entry, FoundationInstance bench,
                                   BuildableDefinition benchDef, StationState station)
        {
            if (!_registry.Recipes.TryGetValue(entry.recipeId, out var recipe)) return;

            RefreshNpcTimer(entry, station);

            NPCInstance craftingNpc = null;
            int skillLevel = 0;
            if (entry.assignedNpcUid != null &&
                station.npcs.TryGetValue(entry.assignedNpcUid, out craftingNpc))
                skillLevel = SkillSystem.GetSkillLevel(craftingNpc, CraftingSkillId);

            float skillScale    = ComputeSkillScale(skillLevel);
            float effectiveTime = Mathf.Max(1f, recipe.baseTimeTicks / skillScale);
            float increment     = 1f / effectiveTime;

            entry.executionProgress = Mathf.Min(1f, entry.executionProgress + increment);

            if (entry.executionProgress >= 1f)
                CompleteRecipe(entry, recipe, bench, station, craftingNpc);
        }

        // ── Completion ────────────────────────────────────────────────────────

        private void CompleteRecipe(WorkbenchQueueEntry entry, RecipeDefinition recipe,
                                    FoundationInstance bench, StationState station,
                                    NPCInstance craftingNpc)
        {
            // Place output in nearest compatible storage.
            bool stored = TryStoreOutput(recipe.outputItemId, recipe.outputQuantity, station);

            string qualityNote = recipe.hasQualityTiers
                ? $" (quality: {entry.outputQualityTier})"
                : "";
            string storageNote = stored
                ? $" → stored in cargo."
                : $" → no storage available; output lost.";

            station.LogEvent($"{recipe.displayName} complete{qualityNote}: " +
                             $"+{recipe.outputQuantity}× {recipe.outputItemId}{storageNote}");

            // Award XP to the NPC.
            if (craftingNpc != null)
                _skillSystem?.AwardSkillXP(craftingNpc, CraftingSkillId,
                    recipe.baseTimeTicks / 10f, station);

            // Free the NPC.
            ReleaseNpc(entry, station);

            entry.status = "complete";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Finds an idle crew NPC eligible for this recipe:
        ///   • Has no current job (currentJobId == null) — not already assigned elsewhere
        ///   • Not in crisis
        ///   • Not on a mission
        ///   • Meets the recipe's minimum skill requirement
        /// </summary>
        private NPCInstance FindCraftingNpc(RecipeDefinition recipe, StationState station)
        {
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew())                     continue;
                if (npc.statusTags.Contains("dead"))   continue;
                if (npc.inCrisis)                      continue;
                if (npc.missionUid != null)            continue;
                if (npc.isSleeping)                    continue;
                if (npc.currentJobId != null)          continue;

                int craftLevel = SkillSystem.GetSkillLevel(npc, CraftingSkillId);
                if (craftLevel < recipe.skillRequirement) continue;

                return npc;
            }
            return null;
        }

        /// <summary>
        /// Consumes required materials from station foundation cargo into hauledMaterials.
        /// Returns true when all required materials are gathered.
        /// Materials are consumed immediately (no physical movement simulation).
        /// </summary>
        private bool GatherMaterials(WorkbenchQueueEntry entry, RecipeDefinition recipe,
                                     StationState station)
        {
            foreach (var kv in recipe.inputMaterials)
            {
                string itemId   = kv.Key;
                int    required = kv.Value;

                int alreadyHauled = entry.hauledMaterials.TryGetValue(itemId, out int h) ? h : 0;
                int stillNeeded   = required - alreadyHauled;
                if (stillNeeded <= 0) continue;

                // Pull from any available station storage foundation.
                int remaining = stillNeeded;
                foreach (var foundation in station.foundations.Values)
                {
                    if (foundation.status != "complete") continue;
                    if (!foundation.cargo.TryGetValue(itemId, out int inCargo) || inCargo <= 0) continue;

                    int toTake = Mathf.Min(remaining, inCargo);
                    foundation.cargo[itemId] = inCargo - toTake;
                    if (foundation.cargo[itemId] == 0)
                        foundation.cargo.Remove(itemId);

                    entry.hauledMaterials[itemId] = alreadyHauled + toTake;
                    remaining -= toTake;
                    if (remaining <= 0) break;
                }

                if (remaining > 0)
                    return false;  // Not all materials available yet.
            }
            return true;
        }

        /// <summary>
        /// Places the output into the nearest complete storage foundation that has capacity.
        /// Returns true if stored successfully, false if no storage found.
        /// </summary>
        private bool TryStoreOutput(string itemId, int qty, StationState station)
        {
            foreach (var foundation in station.foundations.Values)
            {
                if (foundation.status != "complete") continue;
                if (foundation.cargoCapacity <= 0)   continue;

                int used = 0;
                foreach (var v in foundation.cargo.Values) used += v;
                int free = foundation.cargoCapacity - used;

                if (free < qty) continue;

                foundation.cargo[itemId] = (foundation.cargo.TryGetValue(itemId, out int cur) ? cur : 0) + qty;
                return true;
            }
            return false;
        }

        /// <summary>Refreshes the assigned NPC's job timer to prevent JobSystem reassignment.</summary>
        private void RefreshNpcTimer(WorkbenchQueueEntry entry, StationState station)
        {
            if (entry.assignedNpcUid == null) return;
            if (!station.npcs.TryGetValue(entry.assignedNpcUid, out var npc)) return;
            npc.jobTimer = JobTimerRefreshTicks;
        }

        /// <summary>Clears the NPC's crafting job assignment when work finishes.</summary>
        private void ReleaseNpc(WorkbenchQueueEntry entry, StationState station)
        {
            if (entry.assignedNpcUid == null) return;
            if (!station.npcs.TryGetValue(entry.assignedNpcUid, out var npc)) return;

            npc.currentJobId     = null;
            npc.jobModuleUid     = null;
            npc.jobTimer         = 0;
            entry.assignedNpcUid = null;
        }

        // ── Skill scaling formulas ────────────────────────────────────────────

        /// <summary>
        /// Execution speed multiplier from skill level.
        /// level 0 → 0.5× (twice as slow), level 10 → 1.5× (33% faster).
        /// Effective time = baseTimeTicks / skillScale.
        /// </summary>
        public static float ComputeSkillScale(int skillLevel)
            => 0.5f + Mathf.Clamp(skillLevel, 0, 10) / 10f;

        /// <summary>
        /// Quality tier string from skill level.
        /// 0–3 → "standard", 4–7 → "fine", 8–10 → "superior".
        /// </summary>
        public static string ComputeQualityTier(NPCInstance npc)
        {
            int level = SkillSystem.GetSkillLevel(npc, CraftingSkillId);
            if (level >= 8) return "superior";
            if (level >= 4) return "fine";
            return "standard";
        }

        /// <summary>
        /// Quality tier string from skill level integer (for testing without an NPCInstance).
        /// </summary>
        public static string ComputeQualityTierFromLevel(int skillLevel)
        {
            if (skillLevel >= 8) return "superior";
            if (skillLevel >= 4) return "fine";
            return "standard";
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>Returns the current head entry for a workbench, or null if the queue is empty.</summary>
        public WorkbenchQueueEntry GetActiveEntry(string workbenchFoundationUid, StationState station)
        {
            if (!station.workbenchQueues.TryGetValue(workbenchFoundationUid, out var queue)) return null;
            return queue.Count > 0 ? queue[0] : null;
        }

        /// <summary>Returns all queued entries for a workbench in order.</summary>
        public List<WorkbenchQueueEntry> GetQueue(string workbenchFoundationUid, StationState station)
        {
            if (!station.workbenchQueues.TryGetValue(workbenchFoundationUid, out var queue))
                return new List<WorkbenchQueueEntry>();
            return new List<WorkbenchQueueEntry>(queue);
        }
    }
}
