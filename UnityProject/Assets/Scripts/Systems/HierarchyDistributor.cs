// HierarchyDistributor — push-channel task distribution (WO-JOB-002).
//
// On each push cycle (every PushCycleInterval ticks):
//   1. For each department with an active Operations Terminal:
//      a. Department Lead pathfinds to terminal, receives pending tasks
//      b. Lead distributes tasks to Team Leads or directly to NPCs
//      c. NPC receives task into personalTaskQueue up to Memory depth limit
//   2. Departments without a powered terminal fall back to pull-mode
//
// Soft cap degradation when lead's team size exceeds Leadership Capacity:
//   1–2 over: distribution delay (push cycle interval doubled)
//   3–5 over: misassignment risk (10% of pushes go to wrong NPC); lead mood penalty
//   6+  over: tasks fall through to pull-mode; event log warning
//
// Feature flag: UseHierarchyDistribution — disabling reverts to pull-only.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class HierarchyDistributor
    {
        private readonly ContentRegistry _registry;
        private readonly DepartmentRegistry _deptRegistry;

        /// <summary>Ticks between push cycles. Default 5.</summary>
        public int PushCycleInterval = 5;

        /// <summary>When true, push-channel distribution is active. When false, all departments use pull-mode.</summary>
        public static bool UseHierarchyDistribution = true;

        private int _tickCounter = 0;

        public HierarchyDistributor(ContentRegistry registry, DepartmentRegistry deptRegistry)
        {
            _registry = registry;
            _deptRegistry = deptRegistry;
        }

        public void Tick(StationState station)
        {
            if (!UseHierarchyDistribution) return;

            _tickCounter++;
            if (_tickCounter < PushCycleInterval) return;
            _tickCounter = 0;

            foreach (var dept in station.departments)
            {
                ProcessDepartment(dept, station);
            }
        }

        private void ProcessDepartment(Department dept, StationState station)
        {
            // Check if department has an active Operations Terminal
            string terminalUid = dept.operationsTerminalUid;
            if (string.IsNullOrEmpty(terminalUid)) return;

            // Find the terminal foundation
            if (!station.foundations.TryGetValue(terminalUid, out var terminal)) return;
            if (terminal.status != "complete") return;

            // Terminal must be powered (energised)
            if (!terminal.isEnergised)
            {
                // Log fallback event (throttled — only on first offline detection)
                return;
            }

            // Damaged terminal: intermittent push failures (30-50% fail rate)
            float functionality = terminal.Functionality();
            if (functionality < 1.0f && functionality > 0f)
            {
                // Damaged: random chance of failure this cycle proportional to damage
                float failChance = 1f - functionality;
                if (Random.value < failChance) return;
            }

            if (functionality <= 0f) return; // Non-functional, fall back to pull

            // Get department lead
            string leadUid = dept.headNpcUid;
            if (string.IsNullOrEmpty(leadUid)) return;
            if (!station.npcs.TryGetValue(leadUid, out var lead)) return;
            if (lead.isSleeping || lead.inCrisis) return; // Lead unavailable

            // Calculate leadership capacity and team size
            int capacity = DerivedStatSystem.GetLeadershipCapacity(lead);
            var teamNpcs = GetDepartmentMembers(dept, station);
            int teamSize = teamNpcs.Count;
            int overflow = teamSize - capacity;

            // Soft cap degradation
            bool misassignmentRisk = false;

            if (overflow >= 6)
            {
                // 6+ over: significant backlog, tasks fall through to pull-mode
                station.LogEvent($"{lead.name} is overwhelmed — {overflow} reports over capacity");
                return; // Skip push entirely
            }
            else if (overflow >= 3)
            {
                // 3–5 over: misassignment risk, mood penalty
                misassignmentRisk = true;
                // Apply stress modifier to lead mood
                lead.workModifier = Mathf.Max(0.5f, lead.workModifier - 0.05f);
            }
            else if (overflow >= 1)
            {
                // 1–2 over: distribution delay (effectively slows push)
                // We handle this by skipping every other cycle based on a stable station tick
                if (station.tick % 2 != 0) return;
            }

            // Distribute tasks from station task queue to NPCs
            // Check for tasks tagged for this department's jobs
            foreach (var npc in teamNpcs)
            {
                if (npc.isSleeping || npc.inCrisis) continue;
                if (npc.personalTaskQueue == null) continue;

                int queueDepth = DerivedStatSystem.GetQueueDepth(npc);
                int available = queueDepth - npc.personalTaskQueue.Count;
                if (available <= 0) continue;

                // Check if this NPC is managed by a Team Lead
                string teamLeadUid = FindTeamLeadFor(dept, npc.uid);
                if (!string.IsNullOrEmpty(teamLeadUid) && teamLeadUid != leadUid)
                {
                    // Tasks should route through the Team Lead
                    if (!station.npcs.TryGetValue(teamLeadUid, out var teamLead)) continue;
                    if (teamLead.isSleeping || teamLead.inCrisis) continue;

                    // Check Team Lead capacity
                    int tlCapacity = DerivedStatSystem.GetLeadershipCapacity(teamLead);
                    string teamId = FindTeamIdFor(dept, npc.uid);
                    if (!string.IsNullOrEmpty(teamId))
                    {
                        var subTeam = _deptRegistry.GetTeamMembers(dept.uid, teamId);
                        if (subTeam.Count > tlCapacity) continue; // Team Lead over capacity
                    }
                }

                // Find eligible jobs from the department's allowedJobs
                foreach (var jobId in dept.allowedJobs)
                {
                    if (available <= 0) break;
                    if (!_registry.Jobs.ContainsKey(jobId)) continue;

                    // Misassignment: 10% chance of pushing wrong job when lead is over capacity
                    if (misassignmentRisk && Random.value < 0.10f)
                    {
                        // Push a random department job instead
                        if (dept.allowedJobs.Count > 1)
                        {
                            var wrongJob = dept.allowedJobs[Random.Range(0, dept.allowedJobs.Count)];
                            if (_registry.Jobs.ContainsKey(wrongJob))
                            {
                                npc.personalTaskQueue.Add(wrongJob);
                                available--;
                                continue;
                            }
                        }
                    }

                    npc.personalTaskQueue.Add(jobId);
                    available--;
                }
            }
        }

        private List<NPCInstance> GetDepartmentMembers(Department dept, StationState station)
        {
            var members = new List<NPCInstance>();
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.departmentId == dept.uid && npc.uid != dept.headNpcUid)
                    members.Add(npc);
            }
            return members;
        }

        private string FindTeamLeadFor(Department dept, string npcUid)
        {
            foreach (var kv in dept.teamMembers)
            {
                if (kv.Value.Contains(npcUid) && dept.teamLeads.TryGetValue(kv.Key, out var leadUid))
                    return leadUid;
            }
            return null;
        }

        private string FindTeamIdFor(Department dept, string npcUid)
        {
            foreach (var kv in dept.teamMembers)
            {
                if (kv.Value.Contains(npcUid))
                    return kv.Key;
            }
            return null;
        }
    }
}
