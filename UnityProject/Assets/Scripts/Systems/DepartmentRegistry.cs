// DepartmentRegistry — runtime registry for department colour management.
//
// Wraps the departments stored in StationState and provides:
//   • GetDeptColour(deptUid)  — resolved UnityEngine.Color? (null = no colour set)
//   • SetDeptColour(…)        — updates the hex value and fires the changed event
//   • ClearDeptColour(…)      — removes the colour assignment
//   • OnDeptColourChanged     — event fired after any colour change
//
// The event triggers live re-resolution on all equipped garments (the NPC
// rendering loop subscribes to it and re-applies ColourSource.DeptColour bindings).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Systems
{
    using Waystation.Models;

    public class DepartmentRegistry
    {
        // ── Event ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when a department's primary or secondary colour is changed or cleared.
        /// Argument: the uid of the affected department.
        /// </summary>
        public event Action<string> OnDeptColourChanged;

        // ── Internal state ────────────────────────────────────────────────────

        // Reference to the live station departments list (set during init).
        private List<Department> _departments;

        // ── Initialisation ────────────────────────────────────────────────────

        public void Init(List<Department> departments)
        {
            _departments = departments ?? throw new ArgumentNullException(nameof(departments));
        }

        // ── Primary colour ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the primary colour for the given department, or null if none is set.
        /// </summary>
        public Color? GetDeptColour(string deptUid)
        {
            var dept = Find(deptUid);
            return dept?.GetColour();
        }

        /// <summary>
        /// Sets the primary colour on a department and fires OnDeptColourChanged.
        /// </summary>
        public void SetDeptColour(string deptUid, Color colour)
        {
            var dept = Find(deptUid);
            if (dept == null) return;
            dept.colourHex = "#" + ColorUtility.ToHtmlStringRGB(colour);
            OnDeptColourChanged?.Invoke(deptUid);
        }

        /// <summary>
        /// Removes the primary colour from a department and fires OnDeptColourChanged.
        /// </summary>
        public void ClearDeptColour(string deptUid)
        {
            var dept = Find(deptUid);
            if (dept == null) return;
            dept.colourHex = null;
            OnDeptColourChanged?.Invoke(deptUid);
        }

        // ── Secondary (accent) colour ─────────────────────────────────────────

        /// <summary>
        /// Returns the accent colour for the given department, or null if none is set.
        /// </summary>
        public Color? GetDeptSecondaryColour(string deptUid)
        {
            var dept = Find(deptUid);
            return dept?.GetSecondaryColour();
        }

        /// <summary>
        /// Sets the accent colour on a department and fires OnDeptColourChanged.
        /// </summary>
        public void SetDeptSecondaryColour(string deptUid, Color colour)
        {
            var dept = Find(deptUid);
            if (dept == null) return;
            dept.secondaryColourHex = "#" + ColorUtility.ToHtmlStringRGB(colour);
            OnDeptColourChanged?.Invoke(deptUid);
        }

        /// <summary>
        /// Removes the accent colour from a department and fires OnDeptColourChanged.
        /// </summary>
        public void ClearDeptSecondaryColour(string deptUid)
        {
            var dept = Find(deptUid);
            if (dept == null) return;
            dept.secondaryColourHex = null;
            OnDeptColourChanged?.Invoke(deptUid);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Department Find(string deptUid)
        {
            if (_departments == null || string.IsNullOrEmpty(deptUid)) return null;
            return _departments.Find(d => d.uid == deptUid);
        }

        // ── Department Lead ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the NPC uid of the Department Lead for the given department,
        /// or null if no lead is assigned.
        /// </summary>
        public string GetDepartmentLead(string deptUid)
        {
            return Find(deptUid)?.headNpcUid;
        }

        // ── Team Lead ─────────────────────────────────────────────────────────

        /// <summary>
        /// Assigns <paramref name="npcUid"/> as Team Lead for the named
        /// <paramref name="teamId"/> within the department.  Creates the sub-team
        /// record if it does not yet exist.
        /// </summary>
        public void AssignTeamLead(string deptUid, string npcUid, string teamId)
        {
            var dept = Find(deptUid);
            if (dept == null || string.IsNullOrEmpty(npcUid) || string.IsNullOrEmpty(teamId))
                return;
            dept.teamLeads[teamId] = npcUid;
            if (!dept.teamMembers.ContainsKey(teamId))
                dept.teamMembers[teamId] = new List<string>();
        }

        /// <summary>
        /// Returns the NPC uid of the Team Lead for <paramref name="teamId"/>,
        /// or null if no Team Lead is assigned to that sub-team.
        /// </summary>
        public string GetTeamLead(string deptUid, string teamId)
        {
            var dept = Find(deptUid);
            if (dept == null || string.IsNullOrEmpty(teamId)) return null;
            return dept.teamLeads.TryGetValue(teamId, out var uid) ? uid : null;
        }

        /// <summary>
        /// Returns the NPC uids of all members assigned to <paramref name="teamId"/>
        /// within the department, or an empty list if the sub-team does not exist.
        /// </summary>
        public List<string> GetTeamMembers(string deptUid, string teamId)
        {
            var dept = Find(deptUid);
            if (dept == null || string.IsNullOrEmpty(teamId))
                return new List<string>();
            return dept.teamMembers.TryGetValue(teamId, out var members)
                ? members
                : new List<string>();
        }

        /// <summary>
        /// Removes the Team Lead role for <paramref name="teamId"/>.  NPCs in that
        /// sub-team continue to exist in <c>teamMembers</c> but report directly to
        /// the Department Lead.
        /// </summary>
        public void RemoveTeamLead(string deptUid, string teamId)
        {
            var dept = Find(deptUid);
            if (dept == null || string.IsNullOrEmpty(teamId)) return;
            dept.teamLeads.Remove(teamId);
        }

        // ── Operations Terminal ───────────────────────────────────────────────

        /// <summary>
        /// Assigns an Operations Terminal (identified by <paramref name="terminalUid"/>)
        /// to the given department.  Pass null to clear the assignment.
        /// </summary>
        public void AssignOperationsTerminal(string deptUid, string terminalUid)
        {
            var dept = Find(deptUid);
            if (dept == null) return;
            dept.operationsTerminalUid = terminalUid;
        }

        /// <summary>
        /// Returns the UID of the Operations Terminal assigned to the department,
        /// or null if none has been assigned.
        /// </summary>
        public string GetOperationsTerminal(string deptUid)
        {
            return Find(deptUid)?.operationsTerminalUid;
        }
    }
}
