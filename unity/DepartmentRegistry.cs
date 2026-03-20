// DepartmentRegistry — maps department UIDs to their configured colours.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.NPC
{
    /// <summary>
    /// Wraps a list of Department instances and exposes colour lookups for the tinting system.
    /// </summary>
    public class DepartmentRegistry
    {
        private readonly Dictionary<string, Department> _byUid = new Dictionary<string, Department>();

        public DepartmentRegistry(List<Department> departments)
        {
            if (departments == null) return;
            foreach (var dept in departments)
            {
                if (dept != null && !string.IsNullOrEmpty(dept.uid))
                    _byUid[dept.uid] = dept;
            }
        }

        /// <summary>
        /// Returns the department's colour token if it has one configured, otherwise null.
        /// </summary>
        public Color? GetDepartmentColour(string deptUid)
        {
            if (string.IsNullOrEmpty(deptUid)) return null;
            if (!_byUid.TryGetValue(deptUid, out var dept)) return null;
            return dept.colour;
        }
    }
}
