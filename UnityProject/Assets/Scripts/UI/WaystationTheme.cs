// WaystationTheme.cs
// Runtime colour injection API for the Waystation UI style system.
//
// Provides SetDepartmentColour(deptId, primary, accent) which updates
// inline styles on all registered department-scoped VisualElements
// without requiring USS recompilation.
//
// Usage — registering department-scoped elements:
//   WaystationTheme.RegisterDepartmentElement("eng-dept", myElement);
//   WaystationTheme.RegisterDepartmentElement("eng-dept", anotherElement);
//
// Usage — updating colours (e.g. called from DepartmentRegistry event handler):
//   WaystationTheme.SetDepartmentColour("eng-dept", primaryColor, accentColor);
//
// Integration with DepartmentRegistry:
//   departmentRegistry.OnDeptColourChanged += deptId => {
//       var primary = departmentRegistry.GetDeptColour(deptId);
//       var accent  = departmentRegistry.GetDeptSecondaryColour(deptId);
//       if (primary.HasValue)
//           WaystationTheme.SetDepartmentColour(deptId, primary.Value,
//               accent ?? primary.Value);
//   };
//
// For elements that need fine-grained colour control, implement
// IDepartmentColoured on your VisualElement subclass.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Optional interface for VisualElement subclasses that handle department
    /// colour application themselves. Implement this to receive typed colour
    /// callbacks instead of the default border-left-color injection.
    /// </summary>
    public interface IDepartmentColoured
    {
        void ApplyDepartmentColours(Color primary, Color accent);
    }

    /// <summary>
    /// Static runtime theme API. Manages department colour injection into
    /// UI Toolkit VisualElement inline styles.
    /// </summary>
    public static class WaystationTheme
    {
        // ── Department colour record ──────────────────────────────────────

        /// <summary>Stores the current primary and accent colour for a department.</summary>
        public readonly struct DepartmentColours
        {
            public readonly Color Primary;
            public readonly Color Accent;
            public DepartmentColours(Color primary, Color accent)
            {
                Primary = primary;
                Accent  = accent;
            }
        }

        // ── Internal state ────────────────────────────────────────────────

        // Map: deptId → list of registered elements
        private static readonly Dictionary<string, List<VisualElement>> _elements =
            new Dictionary<string, List<VisualElement>>(StringComparer.Ordinal);

        // Map: deptId → last applied colours (so late-registering elements can
        // be coloured immediately on registration)
        private static readonly Dictionary<string, DepartmentColours> _colours =
            new Dictionary<string, DepartmentColours>(StringComparer.Ordinal);

        // ── Events ────────────────────────────────────────────────────────

        /// <summary>
        /// Fired after department colours are applied to all registered elements.
        /// Argument: the department id that changed.
        /// </summary>
        public static event Action<string> OnDepartmentColourApplied;

        // ── Registration ──────────────────────────────────────────────────

        /// <summary>
        /// Registers a VisualElement as belonging to the given department scope.
        /// If colours are already cached for this department, they are applied
        /// immediately. The element is automatically unregistered when it
        /// detaches from its panel (via DetachFromPanelEvent).
        /// </summary>
        public static void RegisterDepartmentElement(string deptId, VisualElement element)
        {
            if (string.IsNullOrEmpty(deptId) || element == null) return;

            if (!_elements.TryGetValue(deptId, out var list))
            {
                list = new List<VisualElement>();
                _elements[deptId] = list;
            }

            if (!list.Contains(element))
            {
                list.Add(element);

                // Auto-unregister when the element leaves its panel so we
                // don't hold stale references after UI teardown.
                element.RegisterCallback<DetachFromPanelEvent>(_ =>
                    UnregisterDepartmentElement(deptId, element));
            }

            // Apply cached colours immediately if available
            if (_colours.TryGetValue(deptId, out var cached))
                ApplyColours(element, cached);
        }

        /// <summary>
        /// Unregisters a VisualElement from a department scope.
        /// </summary>
        public static void UnregisterDepartmentElement(string deptId, VisualElement element)
        {
            if (string.IsNullOrEmpty(deptId) || element == null) return;
            if (_elements.TryGetValue(deptId, out var list))
                list.Remove(element);
        }

        /// <summary>
        /// Removes all registrations for the given department.
        /// </summary>
        public static void UnregisterDepartment(string deptId)
        {
            _elements.Remove(deptId);
        }

        // ── Colour injection ──────────────────────────────────────────────

        /// <summary>
        /// Sets the primary and accent colours for all VisualElements registered
        /// under <paramref name="deptId"/>. Does not require USS recompilation.
        /// </summary>
        /// <param name="deptId">Department unique identifier.</param>
        /// <param name="primary">Primary/main department colour.</param>
        /// <param name="accent">Accent/secondary department colour.</param>
        public static void SetDepartmentColour(string deptId, Color primary, Color accent)
        {
            if (string.IsNullOrEmpty(deptId)) return;

            var colours = new DepartmentColours(primary, accent);
            _colours[deptId] = colours;

            if (!_elements.TryGetValue(deptId, out var list)) return;

            // Iterate in reverse to handle removals during traversal
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var el = list[i];
                if (el == null || el.panel == null)
                {
                    list.RemoveAt(i);
                    continue;
                }
                ApplyColours(el, colours);
            }

            OnDepartmentColourApplied?.Invoke(deptId);
        }

        /// <summary>
        /// Overload that uses the same colour for primary and accent.
        /// </summary>
        public static void SetDepartmentColour(string deptId, Color primary)
            => SetDepartmentColour(deptId, primary, primary);

        /// <summary>
        /// Returns the current cached colours for the given department, or null
        /// if no colours have been set.
        /// </summary>
        public static DepartmentColours? GetDepartmentColour(string deptId)
        {
            if (string.IsNullOrEmpty(deptId)) return null;
            if (_colours.TryGetValue(deptId, out var c)) return c;
            return null;
        }

        // ── Style injection ───────────────────────────────────────────────

        /// <summary>
        /// Applies department colours to a VisualElement's inline styles.
        ///
        /// For elements that implement <see cref="IDepartmentColoured"/>, the
        /// typed interface method is called so the element can apply colours to
        /// whichever properties are semantically correct.
        ///
        /// For all other elements the primary colour is applied as
        /// border-left-color (the standard department stripe colour), which is
        /// the most common department colour use-case. Elements that need more
        /// control should implement IDepartmentColoured.
        /// </summary>
        private static void ApplyColours(VisualElement element, DepartmentColours colours)
        {
            if (element is IDepartmentColoured typed)
            {
                typed.ApplyDepartmentColours(colours.Primary, colours.Accent);
            }
            else
            {
                // Default: tint the left border — covers CategoryStripe and
                // any simple stripe / indicator elements.
                element.style.borderLeftColor = colours.Primary;
            }
        }

        // ── Clear all state (for tests / scene unload) ────────────────────

        /// <summary>Clears all registrations and cached colours.</summary>
        public static void Reset()
        {
            _elements.Clear();
            _colours.Clear();
        }
    }
}
