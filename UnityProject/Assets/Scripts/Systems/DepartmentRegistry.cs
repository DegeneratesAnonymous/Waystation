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
    }
}
