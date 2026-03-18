// Job System — NPC maintenance and work loops.
// Every crew member has a job during day phase; they rest at night.
// Events can interrupt the current job temporarily.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class JobSystem
    {
        private readonly ContentRegistry _registry;

        private const string RestJob         = "job.rest";
        private const string EatJob          = "job.eat";
        private const string RecreateJob     = "job.recreate";
        private const float  HungerCritical  = 0.25f;
        private const float  RestCritical    = 0.20f;

        // Class → preferred day-phase jobs (in priority order)
        private static readonly Dictionary<string, List<string>> ClassDayJobs =
            new Dictionary<string, List<string>>
        {
            { "class.security",    new List<string> { "job.guard_post", "job.patrol", "job.contraband_inspection", "job.research_security" } },
            { "class.engineering", new List<string> { "job.build", "job.module_maintenance", "job.power_management", "job.life_support", "job.haul", "job.refine", "job.craft", "job.research_industry", "job.research_science" } },
            { "class.operations",  new List<string> { "job.dock_control", "job.visitor_intake", "job.resource_management", "job.haul", "job.research_exploration", "job.research_diplomacy", "job.research_security", "job.research_science" } },
            { "class.farming",     new List<string> { "job.farming" } }
        };

        // Category priority for wander fallback: lower index = preferred
        private static readonly string[] WanderCategoryPriority =
            { "hab", "utility", "production", "security", "cargo", "dock" };

        public JobSystem(ContentRegistry registry) => _registry = registry;

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                TickNpc(npc, station);
            }
        }

        private void TickNpc(NPCInstance npc, StationState station)
        {
            // If interrupted by event, reassign immediately
            if (npc.jobInterrupted)
            {
                npc.jobInterrupted = false;
                AssignJob(npc, station);
                return;
            }

            // Needs override
            float hunger = npc.needs.ContainsKey("hunger") ? npc.needs["hunger"] : 1f;
            float rest   = npc.needs.ContainsKey("rest")   ? npc.needs["rest"]   : 1f;

            if (hunger < HungerCritical && npc.currentJobId != EatJob)
            {
                SetJob(npc, EatJob, station);
                return;
            }
            if (rest < RestCritical && npc.currentJobId != RestJob)
            {
                SetJob(npc, RestJob, station);
                return;
            }

            // Crisis override: force recreational task, reject productive work
            if (npc.inCrisis)
            {
                if (npc.currentJobId != RecreateJob || npc.jobTimer <= 0)
                    AssignRecreationalTask(npc, station);
                else
                    npc.jobTimer--;
                return;
            }

            // Count down current job
            if (npc.currentJobId != null && npc.jobTimer > 0)
            {
                npc.jobTimer--;
                ApplyJobEffects(npc, station);
                return;
            }

            // Job complete or no job → pick next
            AssignJob(npc, station);
        }

        private void AssignJob(NPCInstance npc, StationState station)
        {
            bool isDay = TimeSystem.IsDayPhase(station);
            string jobId = RestJob;

            if (isDay && ClassDayJobs.TryGetValue(npc.classId, out var candidates))
            {
                // Respect per-NPC work assignments if set
                List<string> allowed = null;
                if (station.workAssignments.TryGetValue(npc.uid, out var assigned) &&
                    assigned != null && assigned.Count > 0)
                {
                    allowed = new List<string>();
                    foreach (var j in candidates)
                        if (assigned.Contains(j)) allowed.Add(j);
                    if (allowed.Count == 0) allowed = null; // no overlap → use all
                }

                var pool = allowed ?? candidates;
                var available = new List<string>();
                foreach (var j in pool)
                    if (_registry.Jobs.ContainsKey(j)) available.Add(j);

                if (available.Count > 0)
                {
                    jobId = available[UnityEngine.Random.Range(0, available.Count)];
                    SetJob(npc, jobId, station);
                    // If no suitable module was found, fall back to wandering
                    if (npc.jobModuleUid == null)
                        SetWander(npc, station);
                    return;
                }
            }

            // Night, no class match, or all candidates missing — rest or wander
            if (isDay)
                SetWander(npc, station);
            else
                SetJob(npc, jobId, station);
        }

        private void SetWander(NPCInstance npc, StationState station)
        {
            // Find the most "comfortable" active module for the NPC to idle in.
            // Preference order mirrors beauty: hab > utility > production > etc.
            ModuleInstance best = null;
            int            bestPriority = int.MaxValue;

            foreach (var mod in station.modules.Values)
            {
                if (!mod.active) continue;
                int priority = Array.IndexOf(WanderCategoryPriority, mod.category);
                if (priority < 0) priority = WanderCategoryPriority.Length;
                if (priority < bestPriority) { bestPriority = priority; best = mod; }
            }

            if (best != null)
            {
                // Use the job definition's duration so data changes take effect without code edits
                int wanderDuration = _registry.Jobs.TryGetValue("job.wander", out var wanderJob)
                    ? wanderJob.durationTicks : 2;
                npc.currentJobId = "job.wander";
                npc.jobModuleUid = best.uid;
                npc.jobTimer     = wanderDuration;
                npc.location     = best.definitionId;
            }
            else
            {
                // No modules at all — just rest in place
                npc.currentJobId = RestJob;
                npc.jobModuleUid = null;
                npc.jobTimer     = 4;
            }
        }

        private void SetJob(NPCInstance npc, string jobId, StationState station)
        {
            if (!_registry.Jobs.TryGetValue(jobId, out var job)) return;

            var module = FindModule(job, station);
            npc.currentJobId  = jobId;
            npc.jobModuleUid  = module?.uid;
            // Apply WorkModifier to job duration: higher mood = shorter duration (faster work).
            // expertiseModifier stacks multiplicatively (from SkillSystem WorkSpeed bonuses).
            int baseDuration  = job.durationTicks;
            float modifier    = (npc.workModifier     > 0f ? npc.workModifier     : 1.0f)
                              * (npc.expertiseModifier > 0f ? npc.expertiseModifier : 1.0f);
            npc.jobTimer      = Mathf.Max(1, Mathf.RoundToInt(baseDuration / modifier));
            if (module != null) npc.location = module.definitionId;
        }

        /// <summary>
        /// Assigns a recreational task to an NPC in crisis.  The NPC wanders to
        /// the most comfortable non-work module and idles there.
        /// </summary>
        private void AssignRecreationalTask(NPCInstance npc, StationState station)
        {
            // Prefer hab modules for recreation
            ModuleInstance best = null;
            int bestPriority = int.MaxValue;
            foreach (var mod in station.modules.Values)
            {
                if (!mod.active) continue;
                int priority = System.Array.IndexOf(WanderCategoryPriority, mod.category);
                if (priority < 0) priority = WanderCategoryPriority.Length;
                if (priority < bestPriority) { bestPriority = priority; best = mod; }
            }

            npc.currentJobId  = RecreateJob;
            npc.jobModuleUid  = best?.uid;
            npc.jobTimer      = 4;
            if (best != null) npc.location = best.definitionId;
        }

        private ModuleInstance FindModule(JobDefinition job, StationState station)
        {
            var preferred = new List<ModuleInstance>();
            var fallback  = new List<ModuleInstance>();
            foreach (var m in station.modules.Values)
            {
                if (!m.active) continue;
                if (m.category == job.preferredModuleCategory) preferred.Add(m);
                else if (m.category == job.fallbackModuleCategory) fallback.Add(m);
            }
            if (preferred.Count > 0) return preferred[UnityEngine.Random.Range(0, preferred.Count)];
            if (fallback.Count  > 0) return fallback [UnityEngine.Random.Range(0, fallback.Count)];
            return null;
        }

        private void ApplyJobEffects(NPCInstance npc, StationState station)
        {
            if (!_registry.Jobs.TryGetValue(npc.currentJobId ?? "", out var job)) return;

            // Station resource effects — scaled by NPC skill
            foreach (var kv in job.resourceEffects)
            {
                int skillLevel = (job.skillUsed != null && npc.skills.ContainsKey(job.skillUsed))
                                  ? npc.skills[job.skillUsed] : 5;
                float scale = 0.5f + skillLevel / 10f;
                station.ModifyResource(kv.Key, kv.Value * scale);
            }

            // NPC need effects
            npc.UpdateNeeds(job.needEffects);

            // Station special effects
            if (job.stationEffects.ContainsKey("add_tag"))
                station.SetTag(job.stationEffects["add_tag"].ToString());

            if (job.stationEffects.ContainsKey("repair_module") && npc.jobModuleUid != null)
            {
                if (station.modules.TryGetValue(npc.jobModuleUid, out var mod) && mod.damage > 0f)
                {
                    float repairAmt = System.Convert.ToSingle(job.stationEffects["repair_module"]);
                    mod.damage = Mathf.Max(0f, mod.damage - repairAmt);
                }
            }
        }

        // ── External interface ────────────────────────────────────────────────

        public void InterruptNpc(NPCInstance npc) => npc.jobInterrupted = true;

        public string GetJobLabel(NPCInstance npc)
        {
            if (npc.currentJobId == null) return "idle";
            if (_registry.Jobs.TryGetValue(npc.currentJobId, out var job)) return job.displayName;
            return npc.currentJobId;
        }
    }
}
