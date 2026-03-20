// DepartmentRegistry — lightweight runtime helper that resolves department
// colours from StationState.departments.
//
// Used by ColourSource.DeptColour.Resolve() during render-time tint evaluation.
// Should be instantiated once (e.g. in NpcSpriteController or a GameManager
// facade) and passed to colour resolution calls.
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.NPC
{
    public class DepartmentRegistry
    {
        // ── Internal state ────────────────────────────────────────────────────

        // departmentId → resolved Color (nullable)
        private readonly Dictionary<string, Color?> _colours
            = new Dictionary<string, Color?>();

        // ── Construction ──────────────────────────────────────────────────────

        public DepartmentRegistry() { }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Registers (or updates) the colour for a department.
        /// Pass <see langword="null"/> to indicate the department has no configured colour.
        /// </summary>
        public void SetColour(string departmentId, Color? colour)
        {
            if (departmentId == null) return;
            _colours[departmentId] = colour;
        }

        /// <summary>
        /// Returns the configured colour for <paramref name="departmentId"/>,
        /// or <see langword="null"/> if the department has no colour set or is
        /// unknown.
        /// </summary>
        public Color? GetColour(string departmentId)
        {
            if (departmentId == null) return null;
            return _colours.TryGetValue(departmentId, out Color? c) ? c : null;
        }

        /// <summary>Removes all registered department colours.</summary>
        public void Clear() => _colours.Clear();
    }
}
