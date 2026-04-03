// Job System — NPC maintenance and work loops.
// Crew members follow a per-NPC 24-slot schedule (Work/Rest/Recreation).
// Job eligibility is determined by task tag intersection: each job has task_tags,
// and an NPC's department restricts which jobs (and therefore which tags) they
// can perform.  Unassigned NPCs bypass the job filter (wildcard behaviour).
// Crisis NPCs (mood < 20) are immediately switched to recreational tasks.
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

        // Class → preferred day-phase jobs — legacy fallback when UseJobTaskFilter is disabled.
        private static readonly Dictionary<string, List<string>> ClassDayJobs =
            new Dictionary<string, List<string>>
        {
            { "class.security",    new List<string> { "job.guard_post", "job.patrol", "job.contraband_inspection", "job.research_security" } },
            { "class.engineering", new List<string> { "job.build", "job.module_maintenance", "job.power_management", "job.life_support", "job.haul", "job.refine", "job.craft", "job.research_industry", "job.research_science" } },
            { "class.operations",  new List<string> { "job.dock_control", "job.visitor_intake", "job.resource_management", "job.haul", "job.research_exploration", "job.research_diplomacy", "job.research_security", "job.research_science" } },
            { "class.farming",     new List<string> { "job.farming" } }
        };

        /// <summary>When true, job assignment uses department task-tag intersection.
        /// When false, falls back to the legacy ClassDayJobs mapping.</summary>
        public static bool UseJobTaskFilter = true;

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
            // Crisis guard: a crisis NPC must always receive a recreational task,
            // regardless of their current schedule slot or department.  This ensures
            // that when jobInterrupted fires due to crisis entry or recovery the
            // schedule gate below cannot accidentally assign Rest or a work job.
            if (npc.inCrisis)
            {
                AssignRecreationalTask(npc, station);
                return;
            }

            // ── Schedule-slot gate ────────────────────────────────────────────
            int hourOfDay = TimeSystem.HourOfDay(station);
            ScheduleSlot slot = npc.GetScheduleSlot(hourOfDay);

            if (slot == ScheduleSlot.Rest)
            {
                SetJob(npc, RestJob, station);
                return;
            }
            if (slot == ScheduleSlot.Recreation)
            {
                AssignRecreationalTask(npc, station);
                return;
            }

            // slot == ScheduleSlot.Work — proceed with normal assignment

            if (UseJobTaskFilter)
            {
                AssignJobByTagFilter(npc, station);
            }
            else
            {
                AssignJobByClassLegacy(npc, station);
            }
        }

        /// <summary>
        /// New tag-filter-based job assignment (WO-JOB-001).
        /// Builds the candidate pool from the NPC's department allowedJobs.
        /// Unassigned NPCs can take any registered day-phase job.
        /// </summary>
        private void AssignJobByTagFilter(NPCInstance npc, StationState station)
        {
            // Determine department (if any)
            Department dept = null;
            if (!string.IsNullOrEmpty(npc.departmentId))
            {
                foreach (var d in station.departments)
                {
                    if (d.uid == npc.departmentId) { dept = d; break; }
                }
            }

            // Build candidate list: check personal task queue first
            if (npc.personalTaskQueue != null && npc.personalTaskQueue.Count > 0)
            {
                string queuedJob = npc.personalTaskQueue[0];
                if (_registry.Jobs.ContainsKey(queuedJob))
                {
                    npc.personalTaskQueue.RemoveAt(0);
                    SetJob(npc, queuedJob, station);
                    if (npc.jobModuleUid != null) return;
                    // No module found — fall through to general pool
                }
            }

            // Build eligible job pool
            var pool = new List<string>();
            if (dept != null && dept.allowedJobs.Count > 0)
            {
                // Department-constrained: only jobs from the department's allowedJobs
                foreach (var jobId in dept.allowedJobs)
                {
                    if (!_registry.Jobs.TryGetValue(jobId, out var jobDef)) continue;
                    // Skip night-only jobs during work hours and vice versa
                    if (jobDef.phase == "night") continue;
                    pool.Add(jobId);
                }
            }
            else
            {
                // Unassigned NPC — eligible for all registered day/any jobs
                foreach (var kv in _registry.Jobs)
                {
                    if (kv.Value.phase == "night") continue;
                    // Skip universal jobs (rest, eat, recreate, wander) from the work pool
                    if (kv.Key == RestJob || kv.Key == EatJob || kv.Key == RecreateJob
                        || kv.Key == "job.wander") continue;
                    pool.Add(kv.Key);
                }
            }

            // Respect per-NPC work assignments if set
            if (station.workAssignments.TryGetValue(npc.uid, out var assigned) &&
                assigned != null && assigned.Count > 0)
            {
                var filtered = new List<string>();
                foreach (var j in pool)
                    if (assigned.Contains(j)) filtered.Add(j);
                if (filtered.Count > 0) pool = filtered;
            }

            if (pool.Count > 0)
            {
                string jobId = pool[UnityEngine.Random.Range(0, pool.Count)];
                SetJob(npc, jobId, station);
                if (npc.jobModuleUid == null)
                    SetWander(npc, station);
                return;
            }

            SetWander(npc, station);
        }

        /// <summary>
        /// Legacy class-based job assignment. Used when UseJobTaskFilter is false.
        /// </summary>
        private void AssignJobByClassLegacy(NPCInstance npc, StationState station)
        {
            if (!ClassDayJobs.TryGetValue(npc.classId, out var classCandidates))
            {
                SetWander(npc, station);
                return;
            }

            List<string> allowed = null;
            if (station.workAssignments.TryGetValue(npc.uid, out var assigned) &&
                assigned != null && assigned.Count > 0)
            {
                allowed = new List<string>();
                foreach (var j in classCandidates)
                    if (assigned.Contains(j)) allowed.Add(j);
                if (allowed.Count == 0) allowed = null;
            }

            var classPool = allowed ?? classCandidates;

            List<string> pool = classPool;
            if (!string.IsNullOrEmpty(npc.departmentId))
            {
                Department dept = null;
                foreach (var d in station.departments)
                {
                    if (d.uid == npc.departmentId) { dept = d; break; }
                }

                if (dept != null && dept.allowedJobs.Count > 0)
                {
                    var deptPool = new List<string>();
                    foreach (var j in classPool)
                        if (dept.allowedJobs.Contains(j)) deptPool.Add(j);
                    if (deptPool.Count > 0)
                        pool = deptPool;
                }
            }

            var available = new List<string>();
            foreach (var j in pool)
                if (_registry.Jobs.ContainsKey(j)) available.Add(j);

            if (available.Count > 0)
            {
                string jobId = available[UnityEngine.Random.Range(0, available.Count)];
                SetJob(npc, jobId, station);
                if (npc.jobModuleUid == null)
                    SetWander(npc, station);
                return;
            }

            SetWander(npc, station);
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
            // traitWorkModifier stacks multiplicatively (from TraitSystem trait effects).
            // tensionWorkModifier stacks multiplicatively (from TensionSystem stage effects).
            // proximityWorkModifier stacks multiplicatively (from ProximitySystem mentor bonus).
            int baseDuration  = job.durationTicks;
            float modifier    = (npc.workModifier          > 0f ? npc.workModifier          : 1.0f)
                              * (npc.expertiseModifier      > 0f ? npc.expertiseModifier      : 1.0f)
                              * (npc.traitWorkModifier      > 0f ? npc.traitWorkModifier      : 1.0f)
                              * (npc.tensionWorkModifier    > 0f ? npc.tensionWorkModifier    : 1.0f)
                              * (npc.proximityWorkModifier  > 0f ? npc.proximityWorkModifier  : 1.0f);
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

        /// <summary>
        /// Returns all current crew NPC assignments grouped by task type.
        /// Task types: Construction, Farming, Research, Medical, Hauling, Security,
        /// Recreation, Idle.  Groups with zero members are not included.
        /// </summary>
        public Dictionary<string, List<NPCInstance>> GetCurrentAssignments(StationState station)
        {
            var result = new Dictionary<string, List<NPCInstance>>(StringComparer.Ordinal);
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                string taskType = ClassifyTaskType(npc.currentJobId);
                if (!result.TryGetValue(taskType, out var list))
                {
                    list = new List<NPCInstance>();
                    result[taskType] = list;
                }
                list.Add(npc);
            }
            return result;
        }

        /// <summary>
        /// Returns a human-readable task description for the given NPC, e.g.
        /// "Guard Post in security_wing" or "Idle".
        /// </summary>
        public string GetTaskDescription(NPCInstance npc, StationState station)
        {
            if (npc.isSleeping) return "Sleeping";
            if (npc.missionUid != null) return "On Mission";

            if (!string.IsNullOrEmpty(npc.currentJobId))
            {
                if (_registry.Jobs.TryGetValue(npc.currentJobId, out var job))
                {
                    if (!string.IsNullOrEmpty(npc.jobModuleUid) &&
                        station.modules.TryGetValue(npc.jobModuleUid, out var mod))
                    {
                        string loc = !string.IsNullOrEmpty(mod.displayName)
                            ? mod.displayName
                            : (!string.IsNullOrEmpty(mod.definitionId)
                                ? mod.definitionId.Replace("_", " ")
                                : string.Empty);
                        if (!string.IsNullOrEmpty(loc))
                            return $"{job.displayName} in {loc}";
                    }
                    return job.displayName;
                }
                // Unknown job id — show a cleaned label rather than "Idle"
                return npc.currentJobId.Replace("job.", "").Replace("_", " ");
            }

            if (!string.IsNullOrEmpty(npc.currentTaskId))
                return npc.currentTaskId.Replace("task.", "").Replace("_", " ");

            return "Idle";
        }

        // ── Schedule API (UI-014) ─────────────────────────────────────────────

        /// <summary>
        /// Returns the per-NPC 24-slot schedule for every crew member.
        /// If an NPC has no custom schedule array, <see cref="NPCInstance.InitDefaultSchedule"/>
        /// is called first so the caller always receives a full 24-element array.
        /// Key = npcUid, Value = reference to the NPC's <c>npcSchedule</c> array.
        /// <para>
        /// <b>Note:</b> The returned arrays are direct references to the live NPC schedule data.
        /// Mutating them directly will not interrupt the NPC's current job — always use
        /// <see cref="SetSlot"/> or <see cref="ApplyTemplate"/> when a slot change should
        /// take effect on the next scheduler tick.
        /// </para>
        /// </summary>
        public Dictionary<string, ScheduleSlot[]> GetSchedules(StationState station)
        {
            var result = new Dictionary<string, ScheduleSlot[]>(StringComparer.Ordinal);
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.npcSchedule == null || npc.npcSchedule.Length != 24)
                    npc.InitDefaultSchedule();
                result[npc.uid] = npc.npcSchedule;
            }
            return result;
        }

        /// <summary>
        /// Sets a single schedule slot for one NPC and interrupts its current job
        /// so the scheduler re-evaluates on the next tick.
        /// <paramref name="tick"/> is clamped to 0–23.
        /// </summary>
        public void SetSlot(string npcUid, int tick, ScheduleSlot slotType, StationState station)
        {
            tick = Math.Max(0, Math.Min(23, tick));
            if (station == null || string.IsNullOrEmpty(npcUid)) return;
            if (!station.npcs.TryGetValue(npcUid, out var npc) || !npc.IsCrew()) return;
            if (npc.npcSchedule == null || npc.npcSchedule.Length != 24)
                npc.InitDefaultSchedule();
            npc.npcSchedule[tick] = slotType;
            npc.jobInterrupted = true;
        }

        /// <summary>
        /// Applies a named schedule template to one or more NPCs and interrupts
        /// their current jobs so the scheduler picks up the new schedule immediately.
        /// Supported templates: <c>"Day Worker"</c> (Work 06–20, Rest otherwise),
        /// <c>"Night Worker"</c> (Work 21–05, Rest otherwise).
        /// <c>"Custom"</c> is a no-op — the player defines it slot-by-slot.
        /// </summary>
        public void ApplyTemplate(string[] npcUids, string template, StationState station)
        {
            if (npcUids == null || template == null || station == null) return;
            foreach (var uid in npcUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc) || !npc.IsCrew()) continue;
                if (npc.npcSchedule == null || npc.npcSchedule.Length != 24)
                    npc.InitDefaultSchedule();
                ApplyTemplateToSchedule(npc.npcSchedule, template);
                npc.jobInterrupted = true;
            }
        }

        private static void ApplyTemplateToSchedule(ScheduleSlot[] schedule, string template)
        {
            switch (template)
            {
                case "Day Worker":
                    for (int h = 0; h < 24; h++)
                        schedule[h] = (h >= 6 && h <= 20) ? ScheduleSlot.Work : ScheduleSlot.Rest;
                    break;
                case "Night Worker":
                    for (int h = 0; h < 24; h++)
                        schedule[h] = (h >= 21 || h < 6) ? ScheduleSlot.Work : ScheduleSlot.Rest;
                    break;
                // "Custom": user-defined — leave schedule unchanged
            }
        }

        /// <summary>
        /// Maps a job id to one of the canonical task-type group names used by
        /// the Assignments sub-panel (UI-013).
        /// </summary>
        public static string ClassifyTaskType(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return "Idle";
            switch (jobId)
            {
                case "job.guard_post":
                case "job.patrol":
                case "job.contraband_inspection":
                    return "Security";

                case "job.build":
                case "job.repair":
                case "job.module_maintenance":
                case "job.power_management":
                case "job.life_support":
                case "job.refine":
                case "job.craft":
                    return "Construction";

                case "job.research_industry":
                case "job.research_exploration":
                case "job.research_diplomacy":
                case "job.research_security":
                case "job.research_science":
                    return "Research";

                case "job.counselling":
                case "job.medical_bay":
                case "job.specimen_analysis":
                case "job.haul_body":
                    return "Medical";

                case "job.haul":
                case "job.dock_control":
                case "job.visitor_intake":
                case "job.resource_management":
                    return "Hauling";

                case "job.farming":
                    return "Farming";

                case "job.recreate":
                    return "Recreation";

                case "job.rest":
                case "job.eat":
                case "job.wander":
                    return "Idle";

                default:
                    return "Idle";
            }
        }
    }
}
