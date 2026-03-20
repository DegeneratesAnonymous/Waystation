// ColourSource — discriminated union representing the three ways a clothing
// colour slot can be specified.
//
// ExplicitColour  — a player-chosen, free colour value.
// DeptColour      — a token; the actual colour is resolved at render time from
//                   the owning NPC's department via DepartmentRegistry.
// MaterialDefault — no tint; the base texture renders unmodified for this slot.
using System;
using UnityEngine;

namespace Waystation.NPC
{
    [Serializable]
    public abstract class ColourSource
    {
        // ── Concrete cases ────────────────────────────────────────────────────

        /// <summary>A player-chosen explicit colour value.</summary>
        [Serializable]
        public sealed class ExplicitColour : ColourSource
        {
            public Color value;
            public ExplicitColour() { value = Color.white; }
            public ExplicitColour(Color value) { this.value = value; }
        }

        /// <summary>
        /// Token that resolves at render time from the owning NPC's department
        /// colour via <see cref="DepartmentRegistry"/>.
        /// When no department colour is assigned, falls back to MaterialDefault.
        /// </summary>
        [Serializable]
        public sealed class DeptColour : ColourSource { }

        /// <summary>
        /// No tint applied — the base texture renders without colour modification.
        /// </summary>
        [Serializable]
        public sealed class MaterialDefault : ColourSource { }

        // ── Convenience factories ─────────────────────────────────────────────

        /// <summary>Returns an ExplicitColour source for the given colour value.</summary>
        public static ColourSource Explicit(Color c) => new ExplicitColour(c);

        /// <summary>Returns a DeptColour token source.</summary>
        public static ColourSource Dept() => new DeptColour();

        /// <summary>Returns a MaterialDefault (no-tint) source.</summary>
        public static ColourSource Default() => new MaterialDefault();

        // ── Resolution helper ─────────────────────────────────────────────────

        /// <summary>
        /// Resolves this source to a concrete Unity Color.
        /// Returns <see langword="null"/> when the result is MaterialDefault
        /// (i.e. no tint should be applied to the slot).
        /// </summary>
        /// <param name="departmentId">The owning NPC's department UID (may be null).</param>
        /// <param name="registry">
        /// The DepartmentRegistry used to look up department colours.
        /// May be null; treated as department-with-no-colour.
        /// </param>
        public Color? Resolve(string departmentId, DepartmentRegistry registry)
        {
            switch (this)
            {
                case ExplicitColour ec:
                    return ec.value;

                case DeptColour _:
                    Color? deptCol = registry?.GetColour(departmentId);
                    return deptCol; // null when department has no colour → no tint

                case MaterialDefault _:
                    return null;

                default:
                    return null;
            }
        }
    }
}
