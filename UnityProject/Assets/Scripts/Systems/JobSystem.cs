// Job System — NPC maintenance and work loops.
// Every crew member has a job during day phase; they rest at night.
// Events can interrupt the current job temporarily.
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
        private const float  HungerCritical  = 0.25f;
        private const float  RestCritical    = 0.20f;

        // Class → preferred day-phase jobs (in priority order)
        private static readonly Dictionary<string, List<string>> ClassDayJobs =
            new Dictionary<string, List<string>>
        {
            { "class.security",    new List<string> { "job.guard_post", "job.patrol", "job.contraband_inspection" } },
            { "class.engineering", new List<string> { "job.build", "job.module_maintenance", "job.power_management", "job.life_support" } },
            { "class.operations",  new List<string> { "job.dock_control", "job.visitor_intake", "job.resource_management" } }
        };

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
                var available = new List<string>();
                foreach (var j in candidates)
                    if (_registry.Jobs.ContainsKey(j)) available.Add(j);
                if (available.Count > 0)
                    jobId = available[UnityEngine.Random.Range(0, available.Count)];
            }

            SetJob(npc, jobId, station);
        }

        private void SetJob(NPCInstance npc, string jobId, StationState station)
        {
            if (!_registry.Jobs.TryGetValue(jobId, out var job)) return;

            var module = FindModule(job, station);
            npc.currentJobId  = jobId;
            npc.jobModuleUid  = module?.uid;
            npc.jobTimer      = job.durationTicks;
            if (module != null) npc.location = module.definitionId;
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
