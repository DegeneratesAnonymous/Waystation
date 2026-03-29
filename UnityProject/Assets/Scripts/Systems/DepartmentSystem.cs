// DepartmentSystem — player-facing department management layer.
//
// Provides:
//   • CreateDepartment / RenameDepartment / DeleteDepartment (with NPC cascade)
//   • AssignNpcToDepartment / RemoveNpcFromDepartment
//   • AssignJobToDepartment / RemoveJobFromDepartment
//   • AppointHead / RemoveHead (rank-eligibility enforced)
//   • Tick — automated Department Head functions:
//       - Away mission dispatch delegation (via AsteroidMissionSystem)
//       - Player escalation alerts for in-crisis NPCs
//   • NotifyColourChanged / GetNpcsInDepartment — colour re-resolution wiring
//       (GameManager subscribes to DepartmentRegistry.OnDeptColourChanged and calls
//        NotifyColourChanged; rendering systems subscribe to OnNpcsNeedColourResolve
//        to re-apply DeptColour shader bindings within the same tick)
//
// Feature flag: Tick() self-gates on FeatureFlags.DepartmentManagement; callers are
// responsible for gating other DepartmentSystem API usage as appropriate.
using System;
using System.Collections.Generic;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class DepartmentSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// Minimum NPC rank required for Department Head appointment.
        /// Rank 1 = Officer; ranks are defined in NPCSystem.
        /// </summary>
        public const int MinHeadRank = 1;

        /// <summary>
        /// Ticks between away-mission dispatch evaluations per department with a Head.
        /// </summary>
        private const int MissionCheckIntervalTicks = 48;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired after <see cref="NotifyColourChanged"/> is called for a department.
        /// The argument is the list of NPC uids belonging to that department; rendering
        /// systems subscribe to this to re-apply DeptColour shader bindings within the
        /// same tick.
        /// </summary>
        public event Action<List<string>> OnNpcsNeedColourResolve;

        // ── Construction ──────────────────────────────────────────────────────

        public DepartmentSystem() { }

        // ── Department CRUD ────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new department with a generated uid and the given display name.
        /// Returns the new <see cref="Department"/>, or <see langword="null"/> when:
        ///   • <paramref name="name"/> is null/empty, or
        ///   • a department with that name already exists (case-insensitive), or
        ///   • <paramref name="station"/> is null.
        /// </summary>
        public Department CreateDepartment(string name, StationState station)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (station == null) return null;

            // Prevent duplicate names (case-insensitive)
            foreach (var existing in station.departments)
                if (string.Equals(existing.name, name, StringComparison.OrdinalIgnoreCase))
                    return null;

            string uid = "dept." + name.ToLowerInvariant().Replace(" ", "_")
                         + "_" + Guid.NewGuid().ToString("N").Substring(0, 4);

            var dept = Department.Create(uid, name);
            station.departments.Add(dept);
            station.LogEvent($"Department '{name}' created.");
            return dept;
        }

        /// <summary>
        /// Renames a department.
        /// Returns <c>(false, reason)</c> when the department is not found, the name
        /// is empty, or another department already uses that name.
        /// </summary>
        public (bool ok, string reason) RenameDepartment(
            string deptUid, string newName, StationState station)
        {
            if (station == null)                        return (false, "Station is null.");
            if (string.IsNullOrWhiteSpace(newName))     return (false, "Name cannot be empty.");

            var dept = FindDept(deptUid, station);
            if (dept == null) return (false, $"Department '{deptUid}' not found.");

            foreach (var d in station.departments)
                if (d.uid != deptUid &&
                    string.Equals(d.name, newName, StringComparison.OrdinalIgnoreCase))
                    return (false, $"A department named '{newName}' already exists.");

            string oldName = dept.name;
            dept.name = newName;
            station.LogEvent($"Department '{oldName}' renamed to '{newName}'.");
            return (true, null);
        }

        /// <summary>
        /// Deletes a department and clears all assignments:
        ///   • All NPCs whose <c>departmentId</c> matches are set to <see langword="null"/>.
        ///   • The Head role is implicitly cleared with the department record.
        /// Returns <c>(false, reason)</c> when the department is not found.
        /// </summary>
        public (bool ok, string reason) DeleteDepartment(string deptUid, StationState station)
        {
            if (station == null) return (false, "Station is null.");

            var dept = FindDept(deptUid, station);
            if (dept == null) return (false, $"Department '{deptUid}' not found.");

            // Cascade: unassign all NPCs in this department
            foreach (var npc in station.npcs.Values)
                if (npc.departmentId == deptUid)
                    npc.departmentId = null;

            string name = dept.name;
            station.departments.Remove(dept);
            station.LogEvent($"Department '{name}' deleted. All assignments cleared.");
            return (true, null);
        }

        // ── NPC assignment ─────────────────────────────────────────────────────

        /// <summary>
        /// Assigns an NPC to a department.  An NPC can only belong to one department;
        /// calling this while the NPC is already in another department moves them.
        /// </summary>
        public (bool ok, string reason) AssignNpcToDepartment(
            string npcUid, string deptUid, StationState station)
        {
            if (station == null) return (false, "Station is null.");

            if (!station.npcs.TryGetValue(npcUid, out var npc))
                return (false, $"NPC '{npcUid}' not found.");

            var dept = FindDept(deptUid, station);
            if (dept == null) return (false, $"Department '{deptUid}' not found.");

            // If the NPC is moving from another department where they are the Head,
            // clear the previous department's Head role to avoid leaving a stale
            // headNpcUid pointing at an NPC who is no longer a member.
            if (!string.IsNullOrEmpty(npc.departmentId) && npc.departmentId != deptUid)
            {
                var previousDept = FindDept(npc.departmentId, station);
                if (previousDept != null && previousDept.headNpcUid == npcUid)
                    previousDept.headNpcUid = null;
            }

            npc.departmentId = deptUid;
            return (true, null);
        }

        /// <summary>
        /// Removes an NPC from their current department (sets <c>departmentId</c> to
        /// <see langword="null"/>).  If the NPC is the department Head, the Head role
        /// is cleared first.
        /// </summary>
        public void RemoveNpcFromDepartment(string npcUid, StationState station)
        {
            if (station == null) return;
            if (!station.npcs.TryGetValue(npcUid, out var npc)) return;

            // If this NPC is the Head of their department, clear the Head role
            if (!string.IsNullOrEmpty(npc.departmentId))
            {
                var dept = FindDept(npc.departmentId, station);
                if (dept != null && dept.headNpcUid == npcUid)
                    dept.headNpcUid = null;
            }

            npc.departmentId = null;
        }

        // ── Job assignment ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds a job to a department's allowed job list.  No-op if already present.
        /// </summary>
        public (bool ok, string reason) AssignJobToDepartment(
            string jobId, string deptUid, StationState station)
        {
            if (station == null)            return (false, "Station is null.");
            if (string.IsNullOrEmpty(jobId)) return (false, "Job ID cannot be empty.");

            var dept = FindDept(deptUid, station);
            if (dept == null) return (false, $"Department '{deptUid}' not found.");

            if (!dept.allowedJobs.Contains(jobId))
                dept.allowedJobs.Add(jobId);

            return (true, null);
        }

        /// <summary>
        /// Removes a job from a department's allowed job list.  No-op if not present.
        /// </summary>
        public void RemoveJobFromDepartment(string jobId, string deptUid, StationState station)
        {
            if (station == null) return;
            FindDept(deptUid, station)?.allowedJobs.Remove(jobId);
        }

        // ── Department Head ────────────────────────────────────────────────────

        /// <summary>
        /// Appoints an NPC as Department Head.
        /// Requirements:
        ///   • NPC must be assigned to the department.
        ///   • NPC rank must be ≥ <see cref="MinHeadRank"/>.
        /// Returns <c>(false, reason)</c> on any validation failure.
        /// </summary>
        public (bool ok, string reason) AppointHead(
            string deptUid, string npcUid, StationState station)
        {
            if (station == null) return (false, "Station is null.");

            var dept = FindDept(deptUid, station);
            if (dept == null) return (false, $"Department '{deptUid}' not found.");

            if (!station.npcs.TryGetValue(npcUid, out var npc))
                return (false, $"NPC '{npcUid}' not found.");

            if (npc.rank < MinHeadRank)
                return (false,
                    $"{npc.name} does not meet the minimum rank requirement (rank {MinHeadRank}).");

            if (npc.departmentId != deptUid)
                return (false, $"{npc.name} is not assigned to department '{dept.name}'.");

            dept.headNpcUid = npcUid;
            station.LogEvent($"{npc.name} appointed as Head of {dept.name}.");
            return (true, null);
        }

        /// <summary>
        /// Removes the Head role from the current Head of a department.
        /// No-op if no Head is set.
        /// </summary>
        public void RemoveHead(string deptUid, StationState station)
        {
            if (station == null) return;

            var dept = FindDept(deptUid, station);
            if (dept == null || string.IsNullOrEmpty(dept.headNpcUid)) return;

            if (station.npcs.TryGetValue(dept.headNpcUid, out var head))
                station.LogEvent($"{head.name} removed from Head role in {dept.name}.");

            dept.headNpcUid = null;
        }

        // ── Colour re-resolution wiring ────────────────────────────────────────

        /// <summary>
        /// Called by GameManager (subscribed to
        /// <see cref="DepartmentRegistry.OnDeptColourChanged"/>) to enumerate the NPCs
        /// affected by the colour change and fire
        /// <see cref="OnNpcsNeedColourResolve"/> so the rendering layer can re-apply
        /// DeptColour shader bindings within the same tick.
        /// </summary>
        public void NotifyColourChanged(string deptUid, StationState station)
        {
            var npcUids = GetNpcsInDepartment(deptUid, station);
            OnNpcsNeedColourResolve?.Invoke(npcUids);
        }

        /// <summary>
        /// Returns the uids of all NPCs currently assigned to the given department.
        /// Used by the rendering layer to identify which NPCs need shader re-resolution
        /// after a department colour change.
        /// </summary>
        public List<string> GetNpcsInDepartment(string deptUid, StationState station)
        {
            var result = new List<string>();
            if (station == null || string.IsNullOrEmpty(deptUid)) return result;
            foreach (var npc in station.npcs.Values)
                if (npc.departmentId == deptUid)
                    result.Add(npc.uid);
            return result;
        }

        // ── Tick — automated Head functions ────────────────────────────────────

        /// <summary>
        /// Per-tick update.  For each department with an active Head the system:
        ///   1. Evaluates away mission dispatch at
        ///      <see cref="MissionCheckIntervalTicks"/> intervals.
        ///   2. Generates player escalation alerts when department NPCs enter crisis.
        /// Gated by <see cref="FeatureFlags.DepartmentManagement"/>.
        /// </summary>
        public void Tick(StationState station, AsteroidMissionSystem asteroidMissions = null)
        {
            if (!FeatureFlags.DepartmentManagement) return;
            if (station == null) return;

            foreach (var dept in station.departments)
            {
                if (string.IsNullOrEmpty(dept.headNpcUid)) continue;
                if (!station.npcs.TryGetValue(dept.headNpcUid, out var head)) continue;

                // Guard: head must still be assigned to this department (can become stale
                // if the NPC was moved without going through RemoveNpcFromDepartment).
                if (head.departmentId != dept.uid) continue;

                // Head must be on-station (not on mission) and not in crisis
                if (head.missionUid != null || head.inCrisis) continue;

                // Away mission dispatch delegation
                if (asteroidMissions != null &&
                    station.tick % MissionCheckIntervalTicks == 0)
                {
                    TryDispatchDepartmentMission(dept, station, asteroidMissions);
                }

                // Player escalation alerts for in-crisis NPCs
                TickEscalationAlerts(dept, head, station);
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static void TryDispatchDepartmentMission(
            Department dept, StationState station, AsteroidMissionSystem asteroidMissions)
        {
            // Find the first unvisited asteroid POI
            string poiUid = null;
            foreach (var kv in station.pointsOfInterest)
            {
                if (kv.Value.poiType == "Asteroid" && !kv.Value.visited)
                {
                    poiUid = kv.Key;
                    break;
                }
            }
            if (poiUid == null) return;

            // Build crew list: department crew NPCs who are available
            var crew = new List<string>();
            foreach (var npc in station.npcs.Values)
            {
                if (npc.departmentId != dept.uid) continue;
                if (!npc.IsCrew())               continue;
                if (npc.missionUid != null)       continue;
                if (npc.inCrisis)                 continue;
                crew.Add(npc.uid);
            }
            if (crew.Count == 0) return;

            var (ok, _, _) = asteroidMissions.DispatchAsteroidMission(poiUid, crew, station);
            if (ok)
                station.LogEvent($"{dept.name} Head dispatched away mission automatically.");
        }

        private static void TickEscalationAlerts(
            Department dept, NPCInstance head, StationState station)
        {
            foreach (var npc in station.npcs.Values)
            {
                if (npc.departmentId != dept.uid) continue;
                if (npc.uid == head.uid)          continue;

                string alertKey = $"dept_crisis_alert_{npc.uid}";

                if (npc.inCrisis)
                {
                    // Alert once per crisis onset to avoid flooding the log
                    if (!head.memory.ContainsKey(alertKey))
                    {
                        head.memory[alertKey] = station.tick;
                        station.LogEvent(
                            $"[DEPT ALERT] {head.name} reports {npc.name} is in crisis " +
                            $"({dept.name}).");
                    }
                }
                else
                {
                    // Clear the alert key once the NPC recovers
                    head.memory.Remove(alertKey);
                }
            }
        }

        private static Department FindDept(string deptUid, StationState station)
        {
            if (string.IsNullOrEmpty(deptUid)) return null;
            foreach (var d in station.departments)
                if (d.uid == deptUid) return d;
            return null;
        }
    }
}
