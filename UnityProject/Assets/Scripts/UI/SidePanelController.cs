// SidePanelController.cs
// Right-edge collapsible side panel: icon tab strip + content drawer.
//
// This is the container shell (WO-UI-005). Individual tab content is
// registered separately by subsequent Work Orders.
//
// Structure (flex-direction: row, anchored right):
//   SidePanelController (ws-side-panel)
//     DrawerPanel        (ws-side-panel__drawer, horizontal slide)
//     VisualElement      (ws-side-panel__tab-strip)
//       Button × 7       (ws-side-panel__tab)
//
// State rules:
//   • Clicking an inactive tab: opens drawer, makes tab active.
//   • Clicking the active tab: collapses drawer, deactivates tab.
//   • Map tab (special): does not open drawer; calls MapSystem.EnterFullscreen()
//     and collapses any open drawer.
//   • Escape key: collapses drawer if open; exits map fullscreen if active.
//   • IsMouseOverDrawer: true while pointer is over the tab strip or drawer.
//
// Usage:
//   var panel = new SidePanelController(mapSystem);
//   panel.RegisterKeyboard(rootVisualElement);  // to handle Escape
//   someRoot.Add(panel);
//   // query state:
//   bool over = panel.IsMouseOverPanel;

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Side panel shell: right-edge icon tab strip with a sliding content drawer.
    /// </summary>
    public class SidePanelController : VisualElement
    {
        // ── Tab identity ──────────────────────────────────────────────────────

        /// <summary>All tabs in display order.</summary>
        public enum Tab
        {
            Station,
            Crew,
            World,
            Research,
            Map,
            Fleet,
            Settings,
        }

        // ── Tab metadata ──────────────────────────────────────────────────────

        private static readonly (Tab id, string label, string svgPath)[] TabDefs =
        {
            (Tab.Station,  "STATION",  SvgIcons.Station),
            (Tab.Crew,     "CREW",     SvgIcons.Crew),
            (Tab.World,    "WORLD",    SvgIcons.World),
            (Tab.Research, "RESEARCH", SvgIcons.Research),
            (Tab.Map,      "MAP",      SvgIcons.Map),
            (Tab.Fleet,    "FLEET",    SvgIcons.Fleet),
            (Tab.Settings, "SETTINGS", SvgIcons.Settings),
        };

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when a non-Map tab is activated and the drawer opens, or when the
        /// drawer is collapsed (argument is null).
        /// </summary>
        public event Action<Tab?> OnActiveTabChanged;

        /// <summary>
        /// Fired when the Map tab is clicked and full-screen map mode should start.
        /// </summary>
        public event Action OnMapFullscreenRequested;

        // ── State ─────────────────────────────────────────────────────────────

        private Tab? _activeTab;
        private bool _mapFullscreenActive;
        private int  _panelsUnderPointer;
        private readonly MapSystem _mapSystem;

        // ── Child elements ────────────────────────────────────────────────────

        private readonly DrawerPanel       _drawer;
        private readonly VisualElement     _tabStrip;
        private readonly Button[]          _tabButtons;
        private readonly VisualElement[]   _tabIcons;

        // ── Public properties ─────────────────────────────────────────────────

        /// <summary>The currently active tab, or null when the drawer is closed.</summary>
        public Tab? ActiveTab => _activeTab;

        /// <summary>True while the drawer is fully or partially open.</summary>
        public bool IsDrawerOpen => _drawer.IsOpen;

        /// <summary>
        /// True while the pointer is over the tab strip or the open drawer.
        /// Write to <see cref="GameHUD.IsMouseOverDrawer"/> from Update() each frame.
        /// </summary>
        public bool IsMouseOverPanel => _panelsUnderPointer > 0;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <param name="mapSystem">
        /// The live MapSystem instance. Pass null in unit tests to suppress
        /// fullscreen state changes on the system object.
        /// </param>
        public SidePanelController(MapSystem mapSystem = null)
        {
            _mapSystem   = mapSystem;
            _tabButtons  = new Button[TabDefs.Length];
            _tabIcons    = new VisualElement[TabDefs.Length];

            AddToClassList("ws-side-panel");

            // ── Drawer (left portion, slides horizontally) ─────────────────────
            _drawer = new DrawerPanel(DrawerPanel.Direction.Horizontal);
            _drawer.AddToClassList("ws-side-panel__drawer");
            _drawer.RegisterCallback<PointerEnterEvent>(_ => OnPointerEnter());
            _drawer.RegisterCallback<PointerLeaveEvent>(_ => OnPointerLeave());
            Add(_drawer);

            // ── Tab strip (right column) ──────────────────────────────────────
            _tabStrip = new VisualElement();
            _tabStrip.AddToClassList("ws-side-panel__tab-strip");
            _tabStrip.RegisterCallback<PointerEnterEvent>(_ => OnPointerEnter());
            _tabStrip.RegisterCallback<PointerLeaveEvent>(_ => OnPointerLeave());
            Add(_tabStrip);

            // ── Tab buttons ───────────────────────────────────────────────────
            for (int i = 0; i < TabDefs.Length; i++)
            {
                var (tabId, label, svgPath) = TabDefs[i];
                int capturedIndex = i;

                var btn = new Button();
                btn.AddToClassList("ws-side-panel__tab");
                btn.RegisterCallback<ClickEvent>(_ => OnTabClicked(TabDefs[capturedIndex].id));

                // Icon container (hosts inline SVG via VectorImage or placeholder)
                var iconEl = new VisualElement();
                iconEl.AddToClassList("ws-side-panel__tab-icon");
                iconEl.tooltip = label;
                SetSvgIcon(iconEl, svgPath);
                btn.Add(iconEl);

                // Label (hidden by default, revealed via CSS hover)
                var lbl = new Label(label);
                lbl.AddToClassList("ws-side-panel__tab-label");
                btn.Add(lbl);

                _tabStrip.Add(btn);
                _tabButtons[i] = btn;
                _tabIcons[i]   = iconEl;
            }
        }

        // ── Keyboard registration ─────────────────────────────────────────────

        /// <summary>
        /// Registers an Escape-key handler on the given root element.
        /// Call once after the panel is attached to the panel hierarchy.
        /// </summary>
        public void RegisterKeyboard(VisualElement root)
        {
            if (root == null) return;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Activates the given tab. Follows all tab-click rules:
        /// same tab → collapse; Map → fullscreen + collapse; other → open drawer.
        /// </summary>
        public void ActivateTab(Tab tab)
        {
            if (tab == Tab.Map)
            {
                HandleMapTab();
                return;
            }

            if (_activeTab == tab)
            {
                // Clicking the active tab collapses the drawer
                CollapseDrawer();
                return;
            }

            _activeTab = tab;
            RefreshTabHighlights();
            _drawer.Open();
            OnActiveTabChanged?.Invoke(_activeTab);
        }

        /// <summary>
        /// Collapses the drawer and clears the active tab.
        /// </summary>
        public void CollapseDrawer()
        {
            _activeTab = null;
            RefreshTabHighlights();
            _drawer.Close();
            OnActiveTabChanged?.Invoke(null);
        }

        /// <summary>
        /// Handles the Escape key press: collapses the drawer if open, or exits
        /// map fullscreen if active.  Returns true if the event was consumed.
        /// Also callable directly for testing without a keyboard event.
        /// </summary>
        public bool HandleEscapeKey()
        {
            if (_mapFullscreenActive)
            {
                _mapFullscreenActive = false;
                _mapSystem?.ExitFullscreen();
                return true;
            }

            if (_drawer.IsOpen)
            {
                CollapseDrawer();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the content area inside the drawer where tab content can be added.
        /// </summary>
        public VisualElement DrawerContentRoot => _drawer;

        // ── Tab-click handler ─────────────────────────────────────────────────

        private void OnTabClicked(Tab tab) => ActivateTab(tab);

        // ── Map tab special case ──────────────────────────────────────────────

        private void HandleMapTab()
        {
            // Collapse any open drawer and clear active tab
            _activeTab = null;
            RefreshTabHighlights();
            _drawer.Close();

            _mapFullscreenActive = true;
            _mapSystem?.EnterFullscreen();
            OnMapFullscreenRequested?.Invoke();
        }

        // ── Escape key ────────────────────────────────────────────────────────

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            if (HandleEscapeKey())
                evt.StopPropagation();
        }

        // ── Mouse-over tracking ───────────────────────────────────────────────

        private void OnPointerEnter()
        {
            _panelsUnderPointer++;
        }

        private void OnPointerLeave()
        {
            _panelsUnderPointer = Mathf.Max(0, _panelsUnderPointer - 1);
        }

        /// <summary>
        /// Simulates a pointer-enter event for test purposes.
        /// In production, pointer events fire automatically via the VisualElement hierarchy.
        /// </summary>
        public void SimulatePointerEnter() => OnPointerEnter();

        /// <summary>
        /// Simulates a pointer-leave event for test purposes.
        /// In production, pointer events fire automatically via the VisualElement hierarchy.
        /// </summary>
        public void SimulatePointerLeave() => OnPointerLeave();

        // ── Visual refresh ────────────────────────────────────────────────────

        private void RefreshTabHighlights()
        {
            for (int i = 0; i < TabDefs.Length; i++)
            {
                bool active = _activeTab.HasValue && TabDefs[i].id == _activeTab.Value;
                _tabButtons[i].EnableInClassList("ws-side-panel__tab--active", active);
            }
        }

        // ── SVG icon helper ───────────────────────────────────────────────────

        private static void SetSvgIcon(VisualElement el, string svgMarkup)
        {
            // USS background-image with inline SVG is not supported in UI Toolkit.
            // Instead, we encode the icon as a tooltip for now and rely on the
            // USS class for visual styling.  Individual tab icons are authored as
            // inline VectorImage assets and assigned here when the asset pipeline
            // is set up.  For the shell implementation the icon host element is
            // present in the hierarchy and will be back-filled by content WOs.
            _ = svgMarkup; // reserved for VectorImage asset assignment
        }
    }
}
