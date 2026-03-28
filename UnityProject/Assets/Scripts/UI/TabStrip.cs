// TabStrip.cs
// Custom UI Toolkit VisualElement that renders a horizontal or vertical tab
// row with an active state indicator.
//
// Usage in UXML:
//   <Waystation.UI.TabStrip orientation="Horizontal">
//     <!-- tabs are added in C# via AddTab() -->
//   </Waystation.UI.TabStrip>
//
// Usage in C#:
//   var tabs = new TabStrip();
//   tabs.AddTab("OVERVIEW", "overview");
//   tabs.AddTab("CREW", "crew");
//   tabs.OnTabSelected += id => Debug.Log("Selected: " + id);
//   panel.Add(tabs);

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Horizontal or vertical tab row with active state indicator.
    /// </summary>
    public class TabStrip : VisualElement
    {
        // ── Orientation enum ──────────────────────────────────────────────
        public enum Orientation { Horizontal, Vertical }

        // ── UXML factory ──────────────────────────────────────────────────
        public new class UxmlFactory : UxmlFactory<TabStrip, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlEnumAttributeDescription<Orientation> _orientation =
                new UxmlEnumAttributeDescription<Orientation>
                {
                    name = "orientation",
                    defaultValue = Orientation.Horizontal,
                };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var strip = (TabStrip)ve;
                strip.TabOrientation = _orientation.GetValueFromBag(bag, cc);
            }
        }

        // ── Internal tab record ───────────────────────────────────────────
        private readonly struct TabEntry
        {
            public readonly string Id;
            public readonly Button Button;
            public TabEntry(string id, Button btn) { Id = id; Button = btn; }
        }

        // ── Fields ────────────────────────────────────────────────────────
        private readonly List<TabEntry> _tabs = new List<TabEntry>();
        private readonly HashSet<string> _tabIds = new HashSet<string>(StringComparer.Ordinal);
        private string _activeId;
        private Orientation _orientation = Orientation.Horizontal;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fired when the active tab changes. Argument is the tab id.</summary>
        public event Action<string> OnTabSelected;

        // ── Properties ────────────────────────────────────────────────────
        public Orientation TabOrientation
        {
            get => _orientation;
            set
            {
                _orientation = value;
                EnableInClassList("ws-tab-strip--vertical", value == Orientation.Vertical);
            }
        }

        /// <summary>The id of the currently active tab, or null if none.</summary>
        public string ActiveTabId => _activeId;

        // ── Constructor ───────────────────────────────────────────────────
        public TabStrip(Orientation orientation = Orientation.Horizontal)
        {
            AddToClassList("ws-tab-strip");
            TabOrientation = orientation;
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Adds a tab with the given label and id. The first tab added becomes active.
        /// Throws <see cref="ArgumentException"/> if <paramref name="id"/> is null,
        /// empty, or already registered.
        /// </summary>
        public Button AddTab(string label, string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Tab id must not be null or empty.", nameof(id));

            if (_tabIds.Contains(id))
                throw new ArgumentException($"A tab with id '{id}' already exists.", nameof(id));

            var btn = new Button();
            btn.AddToClassList("ws-tab-strip__tab");
            btn.text = label;
            btn.RegisterCallback<ClickEvent>(_ => SelectTab(id));
            Add(btn);

            _tabs.Add(new TabEntry(id, btn));
            _tabIds.Add(id);

            if (_tabs.Count == 1)
                SelectTab(id);

            return btn;
        }

        /// <summary>
        /// Programmatically activates the tab with the given id.
        /// Returns true if the tab was found and activated; false if no tab with
        /// that id exists (state is not modified in that case).
        /// </summary>
        public bool SelectTab(string id)
        {
            if (!_tabIds.Contains(id)) return false;

            _activeId = id;
            foreach (var entry in _tabs)
            {
                entry.Button.EnableInClassList("ws-tab-strip__tab--active", entry.Id == id);
            }
            OnTabSelected?.Invoke(id);
            return true;
        }

        /// <summary>
        /// Removes all tabs.
        /// </summary>
        public void ClearTabs()
        {
            _tabs.Clear();
            _tabIds.Clear();
            _activeId = null;
            Clear();
        }
    }
}
