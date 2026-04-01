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
        private MapSystem _mapSystem;

        // ── Child elements ────────────────────────────────────────────────────

        private readonly DrawerPanel       _drawer;
        private readonly VisualElement     _tabStrip;
        private readonly Button[]          _tabButtons;
        private readonly VisualElement[]   _tabIcons;
        private readonly Label[]           _tabLabels;

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
            _tabLabels   = new Label[TabDefs.Length];

            AddToClassList("ws-side-panel");

            // Inline styles — mirrors USS .ws-side-panel so the panel is visible
            // even if the stylesheet fails to load via Resources.
            style.position = Position.Absolute;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Stretch;

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

            // Inline fallback layout — ensures the strip is visible and correctly
            // sized even before USS custom properties resolve.
            _tabStrip.style.width           = 52;
            _tabStrip.style.flexDirection   = FlexDirection.Column;
            _tabStrip.style.alignItems      = Align.Center;
            _tabStrip.style.backgroundColor = new Color(0.09f, 0.11f, 0.18f, 1f);
            _tabStrip.style.borderLeftWidth = 1;
            _tabStrip.style.borderLeftColor = new Color(0.13f, 0.17f, 0.25f, 1f);
            _tabStrip.style.paddingTop      = 6;
            _tabStrip.style.paddingBottom   = 8;

            Add(_tabStrip);

            // ── Tab buttons ───────────────────────────────────────────────────
            for (int i = 0; i < TabDefs.Length; i++)
            {
                var (tabId, label, svgPath) = TabDefs[i];
                int capturedIndex = i;

                var btn = new Button();
                btn.AddToClassList("ws-side-panel__tab");
                btn.RegisterCallback<ClickEvent>(_ => OnTabClicked(TabDefs[capturedIndex].id));

                // Inline fallback sizing — USS sets same values via custom property.
                btn.style.width           = 52;
                btn.style.height          = 52;
                btn.style.flexDirection   = FlexDirection.Column;
                btn.style.alignItems      = Align.Center;
                btn.style.justifyContent  = Justify.Center;
                btn.style.backgroundColor = StyleKeyword.Null; // let USS control normal/hover background
                btn.style.borderTopWidth  = 0;
                btn.style.borderRightWidth = 0;
                btn.style.borderBottomWidth = 0;
                btn.style.borderLeftWidth = 0;

                // Icon container (hosts inline SVG via VectorImage or placeholder)
                var iconEl = new VisualElement();
                iconEl.AddToClassList("ws-side-panel__tab-icon");
                iconEl.tooltip = label;
                SetSvgIcon(iconEl, svgPath, tabId);
                btn.Add(iconEl);

                // Label — hidden by default, shown on hover or when active.
                // fontSize and display are left unset (StyleKeyword.Null) so USS
                // (.ws-side-panel__tab-label) drives those values; only the colour
                // fallback is applied inline.
                var lbl = new Label(label);
                lbl.AddToClassList("ws-side-panel__tab-label");
                lbl.style.fontSize       = StyleKeyword.Null; // let USS var(--ws-fs-section) apply
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                lbl.style.color          = new Color(0.34f, 0.47f, 0.63f, 1f); // text-mid fallback
                lbl.style.marginTop      = 2;
                lbl.style.display        = StyleKeyword.Null; // visibility controlled via USS class
                btn.Add(lbl);

                _tabLabels[i] = lbl;

                Label capturedLabel = lbl;
                btn.RegisterCallback<PointerEnterEvent>(_ => capturedLabel.style.display = DisplayStyle.Flex);
                btn.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    // Keep label visible if this tab is active
                    bool isActive = _activeTab.HasValue && TabDefs[capturedIndex].id == _activeTab.Value;
                    if (!isActive)
                        capturedLabel.style.display = StyleKeyword.Null; // let USS hide via display: none
                });

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

        /// <summary>
        /// Injects (or replaces) the live <see cref="MapSystem"/> reference.
        /// Call once the game has finished loading so the Map tab can update
        /// <see cref="MapSystem.IsFullscreenActive"/> correctly.
        /// </summary>
        public void InjectMapSystem(MapSystem mapSystem)
        {
            _mapSystem = mapSystem;
        }

        // ── Tab-click handler ─────────────────────────────────────────────────

        private void OnTabClicked(Tab tab) => ActivateTab(tab);

        // ── Map tab special case ──────────────────────────────────────────────

        private void HandleMapTab()
        {
            // Fire OnActiveTabChanged(null) only if a drawer tab was previously active
            // so any mounted content listeners can unmount before fullscreen takes over.
            bool hadActiveTab = _activeTab.HasValue;
            _activeTab = null;
            RefreshTabHighlights();
            _drawer.Close();
            if (hadActiveTab)
                OnActiveTabChanged?.Invoke(null);

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

                // Inline fallback for active state.
                // Inactive tabs clear inline styles so USS (incl. :hover) can drive them.
                if (active)
                {
                    _tabButtons[i].style.backgroundColor = new Color(0.12f, 0.16f, 0.24f, 1f); // bg-select
                    _tabButtons[i].style.borderLeftWidth = 2;
                    _tabButtons[i].style.borderLeftColor = new Color(0.12f, 0.36f, 0.62f, 1f); // acc
                }
                else
                {
                    // Clear inline overrides so USS :hover background works for inactive tabs.
                    _tabButtons[i].style.backgroundColor = StyleKeyword.Null;
                    _tabButtons[i].style.borderLeftWidth = StyleKeyword.Null;
                    _tabButtons[i].style.borderLeftColor = StyleKeyword.Null;
                }

                // Icon color: bright when active
                var iconPlaceholder = _tabIcons[i]?.Q<Label>(className: "ws-side-panel__tab-icon-placeholder");
                if (iconPlaceholder != null)
                    iconPlaceholder.style.color = active
                        ? new Color(0.39f, 0.75f, 1.00f, 1f) // acc-bright
                        : new Color(0.34f, 0.47f, 0.63f, 1f); // text-mid

                // Label: use the cached reference from construction; show/hide via USS class.
                // active → inline Flex override; inactive → clear override so USS display: none applies.
                var label = _tabLabels[i];
                if (label != null)
                {
                    label.style.display = active ? DisplayStyle.Flex : StyleKeyword.Null;
                    label.style.color = active
                        ? new Color(0.39f, 0.75f, 1.00f, 1f)  // acc-bright
                        : new Color(0.34f, 0.47f, 0.63f, 1f);  // text-mid
                }
            }
        }

        // ── SVG icon helper ───────────────────────────────────────────────────

        private static string TabUnicodeIcon(Tab tab) => tab switch
        {
            Tab.Station  => "⬡",
            Tab.Crew     => "♦",
            Tab.World    => "◎",
            Tab.Research => "⚗",
            Tab.Map      => "✦",
            Tab.Fleet    => "▲",
            Tab.Settings => "⚙",
            _            => "●",
        };

        private static void SetSvgIcon(VisualElement el, string svgMarkup, Tab tab = Tab.Station)
        {
            if (!string.IsNullOrEmpty(svgMarkup))
                el.userData = svgMarkup;

            if (el.childCount == 0)
            {
                var placeholder = new Label { text = TabUnicodeIcon(tab) };
                placeholder.AddToClassList("ws-side-panel__tab-icon-placeholder");
                placeholder.style.fontSize       = 16;
                placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
                placeholder.style.color          = new Color(0.34f, 0.47f, 0.63f, 1f);
                el.Add(placeholder);
            }
        }
    }
}
