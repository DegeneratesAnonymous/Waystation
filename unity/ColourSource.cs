// ColourSource — discriminated union describing where a clothing layer's tint colour comes from.
using System;
using UnityEngine;

namespace Waystation.NPC
{
    /// <summary>Base class for colour source tokens used by the shader-driven tinting system.</summary>
    [Serializable]
    public abstract class ColourSource
    {
        /// <summary>
        /// Resolves to the actual tint colour, or null if no tint should be applied.
        /// </summary>
        public abstract Color? Resolve(DepartmentRegistry registry, string departmentId);
    }

    /// <summary>A player-chosen explicit colour. Always resolves to the stored value.</summary>
    [Serializable]
    public sealed class ExplicitColour : ColourSource
    {
        public Color value;

        public ExplicitColour() { }
        public ExplicitColour(Color value) { this.value = value; }

        public override Color? Resolve(DepartmentRegistry registry, string departmentId) => value;
    }

    /// <summary>
    /// Department colour token. Resolves to the department's configured colour,
    /// or null if registry/departmentId is missing.
    /// </summary>
    [Serializable]
    public sealed class DeptColour : ColourSource
    {
        public override Color? Resolve(DepartmentRegistry registry, string departmentId)
        {
            if (registry == null || string.IsNullOrEmpty(departmentId)) return null;
            return registry.GetDepartmentColour(departmentId);
        }
    }

    /// <summary>No tint — material default. Always resolves to null.</summary>
    [Serializable]
    public sealed class MaterialDefault : ColourSource
    {
        public override Color? Resolve(DepartmentRegistry registry, string departmentId) => null;
    }
}
