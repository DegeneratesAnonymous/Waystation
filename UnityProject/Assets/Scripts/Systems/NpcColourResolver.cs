// NpcColourResolver — resolves a ColourSource to a concrete UnityEngine.Color
// given the runtime context of an NPC (department membership).
//
// Usage:
//   var resolver = new NpcColourResolver(departmentRegistry, departmentUid);
//   Color c = resolver.Resolve(colourSource);
using UnityEngine;

namespace Waystation.Systems
{
    using Waystation.Models;

    public class NpcColourResolver
    {
        private readonly DepartmentRegistry _registry;
        private readonly string             _deptUid;

        public NpcColourResolver(DepartmentRegistry registry, string deptUid)
        {
            _registry = registry;
            _deptUid  = deptUid;
        }

        /// <summary>
        /// Resolves a ColourSource to a concrete Color.
        ///
        /// ColourSourceType.Explicit        → parses the stored hex value.
        /// ColourSourceType.DeptColour      → looks up the NPC's department primary
        ///                                    colour; falls back to white on miss.
        /// ColourSourceType.MaterialDefault → returns white (shader identity tint).
        /// </summary>
        public Color Resolve(ColourSource source)
        {
            if (source == null) return Color.white;

            switch (source.type)
            {
                case ColourSourceType.Explicit:
                    if (source.TryGetExplicit(out Color c)) return c;
                    return Color.white;

                case ColourSourceType.DeptColour:
                    if (_registry != null)
                    {
                        Color? dc = _registry.GetDeptColour(_deptUid);
                        if (dc.HasValue) return dc.Value;
                    }
                    return Color.white;

                case ColourSourceType.MaterialDefault:
                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// Resolves the secondary (accent) colour source.
        /// DeptColour maps to the department secondaryColour instead of primary.
        /// </summary>
        public Color ResolveSecondary(ColourSource source)
        {
            if (source == null) return Color.white;

            if (source.type == ColourSourceType.DeptColour && _registry != null)
            {
                Color? sc = _registry.GetDeptSecondaryColour(_deptUid);
                if (sc.HasValue) return sc.Value;
                return Color.white;
            }

            return Resolve(source);
        }
    }
}
