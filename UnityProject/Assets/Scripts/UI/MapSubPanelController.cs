// MapSubPanelController.cs
// Map tab fullscreen overlay (UI-019).
//
// Fullscreen overlay that activates when the Map tab is clicked.
// Shows:
//   • Compact toolbar: System/Sector view toggle, Exploration Points balance,
//     and a close-map button.
//   • System view: orbital body list (uGUI UIRing retained for animations;
//     this panel composites on top of the legacy uGUI layer).
//   • Sector view: scrollable grid of known sectors colour-coded by discovery
//     state (Uncharted = fog, Detected = dim blue, Visited = accent).
//   • Detail sidebar: body detail or sector detail panel that slides in on click.
//
// Clicking the close button fires OnCloseRequested; the caller
// (WaystationHUDController) is responsible for wiring Escape to
// SidePanelController.HandleEscapeKey(), which exits fullscreen on MapSystem.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Fullscreen map overlay panel.  Extends <see cref="VisualElement"/> so it can
    /// be added directly to the HUD content area.
    /// </summary>
    public class MapSubPanelController : VisualElement
    {
        // ── USS class names ───────────────────────────────────────────────────

        private const string PanelClass          = "ws-map-panel";
        private const string ToolbarClass        = "ws-map-panel__toolbar";
        private const string ToggleGroupClass    = "ws-map-panel__toggle-group";
        private const string ToggleBtnClass      = "ws-map-panel__toggle-btn";
        private const string ToggleBtnActiveClass = "ws-map-panel__toggle-btn--active";
        private const string EpLabelClass        = "ws-map-panel__ep-label";
        private const string CloseBtnClass       = "ws-map-panel__close-btn";
        private const string ContentRowClass     = "ws-map-panel__content-row";
        private const string ViewAreaClass       = "ws-map-panel__view-area";
        private const string DetailSidebarClass  = "ws-map-panel__detail-sidebar";
        private const string DetailTitleClass    = "ws-map-panel__detail-title";
        private const string DetailBodyClass     = "ws-map-panel__detail-body";
        private const string DetailRowClass      = "ws-map-panel__detail-row";
        private const string DetailLabelClass    = "ws-map-panel__detail-label";
        private const string DetailValueClass    = "ws-map-panel__detail-value";
        private const string DetailCloseClass    = "ws-map-panel__detail-close";
        private const string SectorGridClass     = "ws-map-panel__sector-grid";
        private const string SectorBoxClass      = "ws-map-panel__sector-box";
        private const string SectorBoxNameClass  = "ws-map-panel__sector-box-name";
        private const string SectorBoxCodeClass  = "ws-map-panel__sector-box-code";
        private const string SectorBoxFogClass   = "ws-map-panel__sector-box--fog";
        private const string SystemBodyRowClass  = "ws-map-panel__body-row";
        private const string SystemBodyNameClass = "ws-map-panel__body-name";
        private const string SystemBodyTypeClass = "ws-map-panel__body-type";
        private const string UnlockBtnClass      = "ws-map-panel__unlock-btn";
        private const string SectionHeaderClass  = "ws-map-panel__section-header";
        private const string EmptyClass          = "ws-map-panel__empty";

        // ── Toolbar child elements ────────────────────────────────────────────

        private readonly Button _systemBtn;
        private readonly Button _sectorBtn;
        private readonly Label  _epLabel;
        private readonly Button _closeBtn;

        // ── Content area ──────────────────────────────────────────────────────

        private readonly VisualElement _contentRow;
        private readonly VisualElement _viewArea;
        private readonly VisualElement _detailSidebar;

        /// <summary>
        /// True while the UI Toolkit map overlay is attached to a panel.
        /// Used by world-space renderers to suppress labels beneath fullscreen map.
        /// </summary>
        public static bool IsOverlayOpen { get; private set; }

        // ── State ──────────────────────────────────────────────────────────────

        private MapLayer     _currentView = MapLayer.System;
        private StationState _station;
        private MapSystem    _map;

        // Currently selected sector for the detail sidebar.
        // Stored so the sidebar can be re-populated after a sector unlock.
        private SectorData   _selectedSector;

        // The solar system currently displayed in the System view.
        // Defaults to the station's home system; set to a generated system when
        // the player drills into a sector system dot.
        private SolarSystemState _viewedSystem;
        private bool _viewedSystemIsHome = true;

        // Dirty-flag values: track last known state to skip full RebuildView()
        // on EP-only tick updates (the most frequent Refresh() call path).
        private int  _lastEp          = -1;
        private int  _lastSectorCount = -1;
        private bool _lastCanSector;

        // System-view orbital visualization state.
        private readonly List<OrbitVisual> _orbitVisuals = new List<OrbitVisual>();
        private IVisualElementScheduledItem _orbitAnimator;
        private VisualElement _orbitCanvas;
        private VisualElement _orbitSelectionRing;
        private VisualElement _orbitHoverRing;
        private float _orbitTime;
        private int _selectedOrbitBodyIndex = -1;
        private int _hoverOrbitBodyIndex = -1;
        private MoonVisual _hoverMoonVisual;
        private bool _hoverStation;
        private Vector2 _canvasPointerLocal;
        private bool _canvasHasPointer;
        private string _proximityHoverTarget;
        private float _proximityHoverTime;
        private const float HoverProximityThreshold = 18f;
        private const float TooltipDelaySeconds = 0.5f;
        private const float OrbitPreviewTicksPerSecond = 1.0f;
        private const float MinutesPerTick = 15f;

        private Label _systemHoverTooltip;
        private Label _sectorHoverTooltip;

        private sealed class OrbitVisual
        {
            public int bodyIndex;
            public VisualElement dot;
            public float radius;
            public float orbitalPeriodTicks;
            public float phase;
            public float size;
            public float currentCenterX;   // updated every animation tick
            public float currentCenterY;
        }

        private sealed class MoonVisual
        {
            public OrbitVisual parent;
            public VisualElement dot;
            public float moonOrbitRadius;  // pixel radius around parent body center
            public float orbitalPeriodTicks;
            public float phase;
            public float size;
            public string name;
        }

        // ── Additional orbit / responsive state ───────────────────────────────
        private float _orbitCenterX  = 260f;
        private float _orbitCenterY  = 150f;
        private float _fontScale     = 1.0f;
        private bool  _isBuilding;

        // Route Plotter state.
        private bool _routePlotterActive;
        private readonly List<RouteWaypoint> _routeWaypoints = new List<RouteWaypoint>();
        private VisualElement _routeOverlay;
        private Button _routePlotterBtn;

        private sealed class RouteWaypoint
        {
            public string name;
            public float orbitalRadius; // AU for bodies
            public float angle;         // radians at selection time
            public Vector2 positionLY;  // for systems (sector view)
            public bool isSystem;       // true = LY scale, false = AU scale
        }
        private const float ReferencePanelWidth  = 1600f;
        private const float StationPhaseOffset   = 0.22f;
        private readonly List<MoonVisual> _moonVisuals = new List<MoonVisual>();
        private VisualElement _stationDot;
        private int   _stationParentBodyIndex = -1;
        private VisualElement _starDotEl;
        private readonly List<(VisualElement ring, float radius)> _orbitRingEls
            = new List<(VisualElement ring, float radius)>();
        private VisualElement _orbitWorld;
        private float _orbitZoom = 1f;
        private Vector2 _orbitPan = Vector2.zero;
        private bool _orbitPanning;
        private Vector2 _orbitPanStartMouse;
        private Vector2 _orbitPanStartOffset;

        private IVisualElementScheduledItem _systemHoverDelay;
        private Vector2 _systemHoverMouse;

        // Sector chart interaction state.
        private VisualElement _sectorChartViewport;
        private VisualElement _sectorChartWorld;
        private float _sectorZoom = 1f;
        private Vector2 _sectorPan = Vector2.zero;
        private bool _sectorPanning;
        private Vector2 _sectorPanStartMouse;
        private Vector2 _sectorPanStartOffset;

        private IVisualElementScheduledItem _sectorHoverDelay;
        private Vector2 _sectorHoverMouse;

        private int Fs(float size)
            => Mathf.RoundToInt(size * _fontScale);

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks the close button.
        /// The caller must call <see cref="SidePanelController.HandleEscapeKey"/> to
        /// exit fullscreen mode on the MapSystem and fire <see cref="SidePanelController.OnMapFullscreenExited"/>.
        /// </summary>
        public event Action OnCloseRequested;

        /// <summary>
        /// Fired after <see cref="MapSystem.TryUnlockSector"/> succeeds, passing the
        /// newly generated <see cref="SectorData"/>.  Subscribe in
        /// <c>WaystationHUDController</c> to call
        /// <c>FactionSystem.OnSectorUnlocked(sector, station)</c> and any other
        /// post-unlock side effects that the legacy path applies.
        /// </summary>
        public event Action<SectorData> OnSectorUnlocked;

        // ── Colour constants ──────────────────────────────────────────────────

        // Background colours matching SystemMapController and the design spec.
        private static readonly Color ColToolbar     = new Color(0.09f, 0.11f, 0.18f, 0.96f);
        private static readonly Color ColPanelBg     = new Color(0.05f, 0.07f, 0.12f, 1.00f);
        private static readonly Color ColDetailBg    = new Color(0.08f, 0.11f, 0.17f, 1.00f);
        private static readonly Color ColSectionHdr  = new Color(0.12f, 0.16f, 0.24f, 1.00f);
        private static readonly Color ColBodyRow     = new Color(0.10f, 0.14f, 0.22f, 1.00f);
        private static readonly Color ColBodyRowHov  = new Color(0.15f, 0.21f, 0.32f, 1.00f);
        private static readonly Color ColAccent      = new Color(0.12f, 0.36f, 0.62f, 1.00f);
        private static readonly Color ColAccentText  = new Color(0.39f, 0.75f, 1.00f, 1.00f);
        private static readonly Color ColTextMid     = new Color(0.34f, 0.47f, 0.63f, 1.00f);
        private static readonly Color ColTextBright  = new Color(0.85f, 0.90f, 1.00f, 1.00f);
        private static readonly Color ColDivider     = new Color(0.13f, 0.17f, 0.25f, 1.00f);
        private static readonly Color ColUnlockBtn   = new Color(0.10f, 0.32f, 0.55f, 1.00f);

        // ── Construction ──────────────────────────────────────────────────────

        public MapSubPanelController()
        {
            AddToClassList(PanelClass);

            // Fullscreen overlay: covers the entire content area.
            style.position = Position.Absolute;
            style.top      = 0;
            style.left     = 0;
            style.right    = 0;
            style.bottom   = 0;
            style.flexDirection = FlexDirection.Column;
            style.backgroundColor = ColPanelBg;

            RegisterCallback<AttachToPanelEvent>(_ => IsOverlayOpen = true);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                IsOverlayOpen = false;
                StopOrbitVisualization();
            });

            // Responsive font scaling: recompute _fontScale when panel width changes.
            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (_isBuilding || evt.newRect.width < 100f) return;
                float newScale = Mathf.Clamp(evt.newRect.width / ReferencePanelWidth, 0.65f, 1.30f);
                if (Mathf.Abs(newScale - _fontScale) > 0.05f)
                {
                    _fontScale = newScale;
                    if (_station != null && _map != null)
                        RebuildView();
                }
            });

            // ── Toolbar ───────────────────────────────────────────────────────
            var toolbar = new VisualElement();
            toolbar.AddToClassList(ToolbarClass);
            toolbar.style.flexDirection     = FlexDirection.Row;
            toolbar.style.alignItems        = Align.Center;
            toolbar.style.height            = 40;
            toolbar.style.minHeight         = 40;
            toolbar.style.backgroundColor   = ColToolbar;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = ColDivider;
            toolbar.style.paddingLeft       = 12;
            toolbar.style.paddingRight      = 12;
            toolbar.style.paddingTop        = 4;
            toolbar.style.paddingBottom     = 4;
            Add(toolbar);

            // System / Sector toggle group
            var toggleGroup = new VisualElement();
            toggleGroup.AddToClassList(ToggleGroupClass);
            toggleGroup.style.flexDirection   = FlexDirection.Row;
            toggleGroup.style.alignItems      = Align.Center;
            toggleGroup.style.marginRight     = 16;
            toolbar.Add(toggleGroup);

            _systemBtn = MakeToggleButton("SYSTEM");
            _sectorBtn = MakeToggleButton("SECTOR");
            toggleGroup.Add(_systemBtn);
            toggleGroup.Add(_sectorBtn);

            _systemBtn.RegisterCallback<ClickEvent>(_ => SetView(MapLayer.System));
            _sectorBtn.RegisterCallback<ClickEvent>(_ => SetView(MapLayer.Sector));

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);

            // Route Plotter toggle
            _routePlotterBtn = new Button();
            _routePlotterBtn.text = "📐 ROUTE PLOTTER";
            StyleActionButton(_routePlotterBtn);
            _routePlotterBtn.style.marginRight = 8;
            _routePlotterBtn.RegisterCallback<ClickEvent>(_ => ToggleRoutePlotter());
            toolbar.Add(_routePlotterBtn);

            // Exploration Points balance
            _epLabel = new Label("✦ 0 EP");
            _epLabel.AddToClassList(EpLabelClass);
            _epLabel.style.color      = ColAccentText;
            _epLabel.style.marginRight = 16;
            toolbar.Add(_epLabel);

            // Close button
            _closeBtn = new Button();
            _closeBtn.text = "✕  CLOSE MAP";
            _closeBtn.AddToClassList(CloseBtnClass);
            StyleActionButton(_closeBtn);
            _closeBtn.RegisterCallback<ClickEvent>(_ => OnCloseRequested?.Invoke());
            toolbar.Add(_closeBtn);

            // ── Content row ───────────────────────────────────────────────────
            _contentRow = new VisualElement();
            _contentRow.AddToClassList(ContentRowClass);
            _contentRow.style.flexDirection = FlexDirection.Row;
            _contentRow.style.flexGrow      = 1;
            _contentRow.style.overflow      = Overflow.Hidden;
            _contentRow.style.backgroundColor = ColPanelBg;
            Add(_contentRow);

            // View area (fills remaining width, left of detail sidebar)
            _viewArea = new VisualElement();
            _viewArea.AddToClassList(ViewAreaClass);
            _viewArea.style.flexDirection = FlexDirection.Column;
            _viewArea.style.flexGrow = 1;
            _viewArea.style.height   = Length.Percent(100);
            _viewArea.style.minHeight = 0;
            _contentRow.Add(_viewArea);

            // Detail sidebar (hidden initially)
            _detailSidebar = new VisualElement();
            _detailSidebar.AddToClassList(DetailSidebarClass);
            _detailSidebar.style.width           = 260;
            _detailSidebar.style.minWidth        = 260;
            _detailSidebar.style.backgroundColor = ColDetailBg;
            _detailSidebar.style.borderLeftWidth = 1;
            _detailSidebar.style.borderLeftColor = ColDivider;
            _detailSidebar.style.display         = DisplayStyle.None;
            _contentRow.Add(_detailSidebar);

            // ── Initial view ──────────────────────────────────────────────────
            RefreshToggleHighlights();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes current game state into the panel.  Call on mount and on each tick
        /// while the panel is the active fullscreen view.
        /// </summary>
        public void Refresh(StationState station, MapSystem map)
        {
            _station = station;
            _map     = map;

            // Update EP label (always cheap in-place update)
            int ep = station?.explorationPoints ?? 0;
            _epLabel.text = SystemMapController.TelescopeMode ? "✦ ∞ EP" : $"✦ {ep} EP";

            // Enable/disable Sector toggle based on map view level
            bool canSector = (map?.GetMapViewLevel(station) == MapViewLevel.Sector)
                          || SystemMapController.TelescopeMode;
            _sectorBtn.SetEnabled(canSector);
            _sectorBtn.tooltip = canSector ? "" : "Requires Sector Map research (or DEV God-View).";

            if (!canSector && _currentView == MapLayer.Sector)
            {
                _currentView = MapLayer.System;
                RefreshToggleHighlights();
            }

            // Only rebuild the view when data that affects the grid has changed.
            // EP-only changes (the most frequent tick path, e.g. Interstellar Antenna
            // producing 1 EP per tick) skip the full rebuild to avoid allocation churn.
            int sectorCount = station?.sectors.Count ?? 0;
            bool needsRebuild = sectorCount != _lastSectorCount
                             || canSector   != _lastCanSector
                             || _lastEp     == -1;   // always rebuild on first Refresh

            _lastEp          = ep;
            _lastSectorCount = sectorCount;
            _lastCanSector   = canSector;

            if (needsRebuild)
            {
                RebuildView();

                // Re-populate the detail sidebar when it was already open and the
                // grid data has changed (e.g. after a sector unlock).
                if (_selectedSector != null)
                    ShowSectorDetail(_selectedSector);
            }
        }

        // ── Sector state colour (static — testable without instance) ──────────

        /// <summary>
        /// Returns the background colour used to represent a sector's discovery state
        /// in the Sector view grid.
        /// <list type="bullet">
        ///   <item><description>Uncharted — dark fog (semi-transparent grey)</description></item>
        ///   <item><description>Detected — dim blue (antenna range)</description></item>
        ///   <item><description>Visited — accent blue (player has been here)</description></item>
        /// </list>
        /// </summary>
        public static Color SectorStateColour(SectorDiscoveryState state) => state switch
        {
            SectorDiscoveryState.Uncharted => new Color(0.25f, 0.28f, 0.32f, 0.45f),
            SectorDiscoveryState.Detected  => new Color(0.306f, 0.329f, 0.439f, 1f),
            SectorDiscoveryState.Visited   => new Color(0.282f, 0.502f, 0.667f, 1f),
            _                              => Color.grey,
        };

        /// <summary>
        /// Simulates a close-map action for testing purposes.
        /// In production, the close button fires this same event.
        /// </summary>
        public void SimulateCloseRequested() => OnCloseRequested?.Invoke();

        // ── View switching ────────────────────────────────────────────────────

        private void SetView(MapLayer layer)
        {
            if (layer == MapLayer.Sector && _sectorBtn != null && !_sectorBtn.enabledSelf)
                return;

            // Switching back to System view without an explicit ViewSystem call
            // should return to the home system.
            if (layer == MapLayer.System)
            {
                _viewedSystem = _station?.solarSystem;
                _viewedSystemIsHome = true;
            }

            _currentView = layer;
            RefreshToggleHighlights();
            HideDetailSidebar();
            RebuildView();
        }

        /// <summary>
        /// Switches to the System view showing the given solar system.
        /// Used when drilling into a sector system dot.
        /// </summary>
        private void ViewSystem(SolarSystemState sys, bool isHome)
        {
            _viewedSystem = sys;
            _viewedSystemIsHome = isHome;
            _currentView = MapLayer.System;
            RefreshToggleHighlights();
            HideDetailSidebar();
            RebuildView();
        }

        private void RefreshToggleHighlights()
        {
            bool sys = _currentView == MapLayer.System;
            SetToggleActive(_systemBtn, sys);
            SetToggleActive(_sectorBtn, !sys);
        }

        private static void SetToggleActive(Button btn, bool active)
        {
            btn.EnableInClassList(ToggleBtnActiveClass, active);
            if (active)
            {
                btn.style.backgroundColor = new Color(0.12f, 0.36f, 0.62f, 1f);
                btn.style.color           = new Color(0.85f, 0.95f, 1.00f, 1f);
                btn.style.borderLeftColor = btn.style.borderRightColor =
                btn.style.borderTopColor  = btn.style.borderBottomColor =
                    new Color(0.12f, 0.36f, 0.62f, 1f);
            }
            else
            {
                btn.style.backgroundColor = new Color(0.11f, 0.14f, 0.21f, 1f);
                btn.style.color           = new Color(0.34f, 0.47f, 0.63f, 1f);
                btn.style.borderLeftColor = btn.style.borderRightColor =
                btn.style.borderTopColor  = btn.style.borderBottomColor =
                    new Color(0.13f, 0.17f, 0.25f, 1f);
            }
        }

        private void RebuildView()
        {
            _isBuilding = true;
            StopOrbitVisualization();
            _viewArea.Clear();

            if (_currentView == MapLayer.System)
                BuildSystemView();
            else
            {
                // Ensure a home sector exists for dev/telescope mode before building the sector view.
                _map?.EnsureHomeSectorForDev(_station);
                BuildSectorView();
            }

            // Rebuild route overlay on the new canvas if route plotter is active.
            if (_routePlotterActive)
            {
                _routeWaypoints.Clear();
                BuildRouteOverlay();
            }

            _isBuilding = false;
        }

        // ── System view ───────────────────────────────────────────────────────

        private void BuildSystemView()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow      = 1;
            root.style.height        = Length.Percent(100);
            root.style.paddingLeft   = 8;
            root.style.paddingTop    = 8;
            root.style.paddingRight  = 8;
            root.style.paddingBottom = 8;

            var sys = _viewedSystem ?? _station?.solarSystem;
            if (sys == null)
            {
                var empty = new Label("No system data available.");
                empty.AddToClassList(EmptyClass);
                empty.style.color = ColTextMid;
                empty.style.marginTop = 40;
                root.Add(empty);
                _viewArea.Add(root);
                return;
            }

            // Pictorial orbital view for quick spatial readability.
            root.Add(BuildOrbitVisualization(sys));

            // If viewing a remote system, add a "Back to Sector" bar.
            if (!_viewedSystemIsHome)
            {
                var backBar = new VisualElement();
                backBar.style.flexDirection = FlexDirection.Row;
                backBar.style.alignItems = Align.Center;
                backBar.style.paddingLeft = 8;
                backBar.style.paddingTop = 4;
                backBar.style.paddingBottom = 4;

                var backBtn = new Button(() => SetView(MapLayer.Sector)) { text = "◂ Back to Sector" };
                backBtn.style.fontSize = Fs(10);
                backBtn.style.color = ColAccentText;
                backBtn.style.backgroundColor = new Color(0.10f, 0.16f, 0.26f, 0.90f);
                backBtn.style.borderTopWidth = 1;
                backBtn.style.borderRightWidth = 1;
                backBtn.style.borderBottomWidth = 1;
                backBtn.style.borderLeftWidth = 1;
                backBtn.style.borderTopColor = ColDivider;
                backBtn.style.borderRightColor = ColDivider;
                backBtn.style.borderBottomColor = ColDivider;
                backBtn.style.borderLeftColor = ColDivider;
                backBtn.style.paddingLeft = 10;
                backBtn.style.paddingRight = 10;
                backBtn.style.paddingTop = 4;
                backBtn.style.paddingBottom = 4;
                backBar.Add(backBtn);

                var homeBtn = new Button(() =>
                {
                    _viewedSystem = _station?.solarSystem;
                    _viewedSystemIsHome = true;
                    RebuildView();
                }) { text = "⌂ Home System" };
                homeBtn.style.fontSize = Fs(10);
                homeBtn.style.color = new Color(0.25f, 1.00f, 0.50f, 1f);
                homeBtn.style.backgroundColor = new Color(0.10f, 0.16f, 0.26f, 0.90f);
                homeBtn.style.borderTopWidth = 1;
                homeBtn.style.borderRightWidth = 1;
                homeBtn.style.borderBottomWidth = 1;
                homeBtn.style.borderLeftWidth = 1;
                homeBtn.style.borderTopColor = ColDivider;
                homeBtn.style.borderRightColor = ColDivider;
                homeBtn.style.borderBottomColor = ColDivider;
                homeBtn.style.borderLeftColor = ColDivider;
                homeBtn.style.paddingLeft = 10;
                homeBtn.style.paddingRight = 10;
                homeBtn.style.paddingTop = 4;
                homeBtn.style.paddingBottom = 4;
                homeBtn.style.marginLeft = 8;
                backBar.Add(homeBtn);

                root.Insert(0, backBar);
            }

            // Floating bottom-left listing overlay inside the map canvas.
            AddSystemListingsOverlay(sys);

            _viewArea.Add(root);
        }

        private void AddSystemListingsOverlay(SolarSystemState sys)
        {
            if (_orbitCanvas == null) return;

            var listsOverlay = new VisualElement();
            listsOverlay.style.position = Position.Absolute;
            listsOverlay.style.left = 10;
            listsOverlay.style.bottom = 10;
            listsOverlay.style.width = 660;
            listsOverlay.style.maxWidth = Length.Percent(62);
            listsOverlay.style.flexDirection = FlexDirection.Row;
            listsOverlay.style.alignItems = Align.FlexStart;
            _orbitCanvas.Add(listsOverlay);

            var bodiesCard = new VisualElement();
            bodiesCard.style.flexGrow = 3;
            bodiesCard.style.flexShrink = 1;
            bodiesCard.style.minWidth = 220;
            bodiesCard.style.maxHeight = 210;
            bodiesCard.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.78f);
            bodiesCard.style.borderTopWidth = 1;
            bodiesCard.style.borderRightWidth = 1;
            bodiesCard.style.borderBottomWidth = 1;
            bodiesCard.style.borderLeftWidth = 1;
            bodiesCard.style.borderTopColor = ColDivider;
            bodiesCard.style.borderRightColor = ColDivider;
            bodiesCard.style.borderBottomColor = ColDivider;
            bodiesCard.style.borderLeftColor = ColDivider;
            bodiesCard.style.marginRight = 10;
            listsOverlay.Add(bodiesCard);

            var bodiesHdr = MakeSectionHeader("ORBITAL BODIES");
            bodiesHdr.style.marginBottom = 0;
            bodiesCard.Add(bodiesHdr);

            var bodiesScroll = new ScrollView(ScrollViewMode.Vertical);
            bodiesScroll.style.flexGrow = 1;
            bodiesScroll.style.maxHeight = 170;
            bodiesScroll.style.paddingLeft = 4;
            bodiesScroll.style.paddingRight = 4;
            bodiesCard.Add(bodiesScroll);

            if (sys.bodies.Count > 0)
            {
                for (int i = 0; i < sys.bodies.Count; i++)
                {
                    var body = sys.bodies[i];
                    bool isStation = _viewedSystemIsHome && sys.stationOrbitIndex == i;
                    var row = BuildBodyRow(body, isStation, i, sys);
                    bodiesScroll.Add(row);
                    foreach (var moon in body.moons)
                        bodiesScroll.Add(BuildMoonRow(moon));
                    if (isStation)
                        bodiesScroll.Add(BuildStationSubRow());
                }
            }
            else
            {
                var noData = new Label("No orbital bodies catalogued.");
                noData.style.color = ColTextMid;
                noData.style.fontSize = Fs(10);
                bodiesScroll.Add(noData);
            }

            var poiCard = new VisualElement();
            poiCard.style.flexGrow = 2;
            poiCard.style.flexShrink = 1;
            poiCard.style.minWidth = 180;
            poiCard.style.maxHeight = 210;
            poiCard.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.78f);
            poiCard.style.borderTopWidth = 1;
            poiCard.style.borderRightWidth = 1;
            poiCard.style.borderBottomWidth = 1;
            poiCard.style.borderLeftWidth = 1;
            poiCard.style.borderTopColor = ColDivider;
            poiCard.style.borderRightColor = ColDivider;
            poiCard.style.borderBottomColor = ColDivider;
            poiCard.style.borderLeftColor = ColDivider;
            listsOverlay.Add(poiCard);

            var poiHdr = MakeSectionHeader("POINTS OF INTEREST");
            poiHdr.style.marginBottom = 0;
            poiCard.Add(poiHdr);

            var poiScroll = new ScrollView(ScrollViewMode.Vertical);
            poiScroll.style.flexGrow = 1;
            poiScroll.style.maxHeight = 170;
            poiScroll.style.paddingLeft = 4;
            poiScroll.style.paddingRight = 4;
            poiCard.Add(poiScroll);

            var pois = _map?.GetDiscoveredPois(_station);
            if (pois != null && pois.Count > 0)
            {
                foreach (var poi in pois)
                {
                    var poiRow = new VisualElement();
                    poiRow.style.flexDirection = FlexDirection.Row;
                    poiRow.style.paddingLeft = 8;
                    poiRow.style.paddingTop = 5;
                    poiRow.style.paddingBottom = 5;
                    poiRow.style.marginBottom = 2;
                    poiRow.style.backgroundColor = ColBodyRow;
                    poiRow.style.BorderRadius(3);

                    var poiName = new Label(poi.displayName ?? poi.poiType);
                    poiName.style.color = ColTextBright;
                    poiName.style.flexGrow = 1;
                    poiName.style.fontSize = Fs(10);
                    poiRow.Add(poiName);

                    var capturedPoi = poi;
                    poiRow.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (_routePlotterActive)
                        {
                            float pr = Mathf.Sqrt(capturedPoi.posX * capturedPoi.posX + capturedPoi.posY * capturedPoi.posY);
                            float pa = Mathf.Atan2(capturedPoi.posY, capturedPoi.posX);
                            AddRouteWaypoint(new RouteWaypoint
                            {
                                name = capturedPoi.displayName ?? capturedPoi.poiType,
                                orbitalRadius = pr,
                                angle = pa,
                                isSystem = false,
                            });
                            return;
                        }
                        ShowPoiDetail(capturedPoi);
                    });
                    poiRow.RegisterCallback<PointerEnterEvent>(evt =>
                    {
                        _systemHoverMouse = ToLocalPoint(_orbitCanvas, evt.position);
                        _systemHoverDelay?.Pause();
                        _systemHoverDelay = poiRow.schedule.Execute(() =>
                        {
                            ShowSystemHoverTooltip(capturedPoi.displayName ?? capturedPoi.poiType, _systemHoverMouse + new Vector2(12f, 12f));
                        }).StartingIn(1000);
                    });
                    poiRow.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        _systemHoverMouse = ToLocalPoint(_orbitCanvas, evt.position);
                        if (_systemHoverTooltip != null && _systemHoverTooltip.style.display == DisplayStyle.Flex)
                            PositionSystemHoverTooltip(_systemHoverMouse + new Vector2(12f, 12f));
                    });
                    poiRow.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        _systemHoverDelay?.Pause();
                        HideSystemHoverTooltip();
                    });

                    poiScroll.Add(poiRow);
                }
            }
            else
            {
                var noPoi = new Label("No points of interest detected.");
                noPoi.style.color = ColTextMid;
                noPoi.style.fontSize = Fs(10);
                poiScroll.Add(noPoi);
            }
        }

        private VisualElement BuildOrbitVisualization(SolarSystemState sys)
        {
            _orbitVisuals.Clear();
            _moonVisuals.Clear();
            _orbitRingEls.Clear();
            _orbitZoom = 1f;
            _orbitPan = Vector2.zero;
            _orbitPanning = false;
            _starDotEl         = null;
            _stationDot        = null;

            if (_selectedOrbitBodyIndex >= sys.bodies.Count)
                _selectedOrbitBodyIndex = -1;

            var card = new VisualElement();
            card.style.alignSelf = Align.Stretch;
            card.style.marginBottom = 16;
            card.style.paddingTop   = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft  = 8;
            card.style.paddingRight = 8;
            card.style.backgroundColor  = new Color(0.08f, 0.11f, 0.17f, 0.92f);
            card.style.borderTopWidth    = 1;
            card.style.borderRightWidth  = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth   = 1;
            card.style.borderTopColor    = ColDivider;
            card.style.borderRightColor  = ColDivider;
            card.style.borderBottomColor = ColDivider;
            card.style.borderLeftColor   = ColDivider;

            _orbitCanvas = new VisualElement();
            _orbitCanvas.style.position  = Position.Relative;
            _orbitCanvas.style.alignSelf = Align.Stretch;   // fills card width
            _orbitCanvas.style.flexGrow  = 1;
            _orbitCanvas.style.minHeight = 280;
            _orbitCanvas.style.backgroundColor  = new Color(0.03f, 0.05f, 0.09f, 0.94f);
            _orbitCanvas.style.borderTopWidth    = 1;
            _orbitCanvas.style.borderRightWidth  = 1;
            _orbitCanvas.style.borderBottomWidth = 1;
            _orbitCanvas.style.borderLeftWidth   = 1;
            _orbitCanvas.style.borderTopColor    = new Color(0.14f, 0.20f, 0.30f, 1f);
            _orbitCanvas.style.borderRightColor  = new Color(0.14f, 0.20f, 0.30f, 1f);
            _orbitCanvas.style.borderBottomColor = new Color(0.14f, 0.20f, 0.30f, 1f);
            _orbitCanvas.style.borderLeftColor   = new Color(0.14f, 0.20f, 0.30f, 1f);
            _orbitCanvas.style.overflow = Overflow.Hidden;
            card.Add(_orbitCanvas);

            // Overlay in top-left: system name + collapsible map key.
            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 8;
            overlay.style.top = 8;
            overlay.style.flexDirection = FlexDirection.Column;
            _orbitCanvas.Add(overlay);

            var systemName = new Label(sys.systemName);
            systemName.style.color = ColAccentText;
            systemName.style.fontSize = Fs(14);
            systemName.style.unityFontStyleAndWeight = FontStyle.Bold;
            systemName.style.marginBottom = 4;
            systemName.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.72f);
            systemName.style.paddingLeft = 8;
            systemName.style.paddingRight = 8;
            systemName.style.paddingTop = 4;
            systemName.style.paddingBottom = 4;
            systemName.style.borderTopWidth = 1;
            systemName.style.borderRightWidth = 1;
            systemName.style.borderBottomWidth = 1;
            systemName.style.borderLeftWidth = 1;
            systemName.style.borderTopColor = ColDivider;
            systemName.style.borderRightColor = ColDivider;
            systemName.style.borderBottomColor = ColDivider;
            systemName.style.borderLeftColor = ColDivider;
            overlay.Add(systemName);

            overlay.Add(BuildMapLegend(compact: true, sectorMode: false));

            // Stretch the map card to fill available panel height.
            card.style.flexGrow  = 1;
            card.style.minHeight = 340;
            card.style.marginBottom = 0;
            _orbitCanvas.style.flexGrow = 1;

            // Defer orrery content build until the canvas has its real layout width.
            var capturedSys  = sys;
            bool orreryBuilt = false;
            _orbitCanvas.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (orreryBuilt || evt.newRect.width < 10f) return;
                orreryBuilt    = true;
                float cx = evt.newRect.width  * 0.5f;
                // Keep the system cluster fully above bottom-left info cards.
                float cy = evt.newRect.height * 0.40f;
                _orbitCenterX  = cx;
                _orbitCenterY  = cy;
                BuildOrbitContent(capturedSys, cx, cy, evt.newRect.width, evt.newRect.height);
                StartOrbitAnimation();
            });

            _orbitCanvas.RegisterCallback<WheelEvent>(evt =>
            {
                float zoom = Mathf.Clamp(_orbitZoom * (1f - evt.delta.y * 0.0075f), 0.45f, 3.2f);
                if (Mathf.Abs(zoom - _orbitZoom) < 0.001f) return;
                _orbitZoom = zoom;
                ApplyOrbitWorldTransform();
                evt.StopPropagation();
            });
            _orbitCanvas.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return; // right-drag to pan
                _orbitPanning = true;
                _orbitPanStartMouse = (Vector2)evt.position;
                _orbitPanStartOffset = _orbitPan;
                _orbitCanvas.CapturePointer(evt.pointerId);
            });
            _orbitCanvas.RegisterCallback<PointerMoveEvent>(evt =>
            {
                _canvasPointerLocal = ToLocalPoint(_orbitCanvas, evt.position);
                _canvasHasPointer = true;
                if (!_orbitPanning) return;
                Vector2 delta = (Vector2)evt.position - _orbitPanStartMouse;
                _orbitPan = _orbitPanStartOffset + delta;
                ApplyOrbitWorldTransform();
            });
            _orbitCanvas.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 1) return;
                _orbitPanning = false;
                _orbitCanvas.ReleasePointer(evt.pointerId);
            });
            _orbitCanvas.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _canvasHasPointer = false;
            });

            return card;
        }

        private void BuildOrbitContent(SolarSystemState sys, float centerX, float centerY, float canvasW, float canvasH)
        {
            if (_orbitCanvas == null) return;

            EnsureSystemHoverTooltip();

            _orbitWorld = new VisualElement();
            _orbitWorld.style.position = Position.Absolute;
            _orbitWorld.style.left = 0;
            _orbitWorld.style.top = 0;
            _orbitWorld.style.right = 0;
            _orbitWorld.style.bottom = 0;
            _orbitCanvas.Add(_orbitWorld);
            ApplyOrbitWorldTransform();

            // ── Star ─────────────────────────────────────────────────────────
            float starSize = Mathf.Clamp(22f + sys.starSize * 3f, 22f, 34f);
            var star = new VisualElement();
            star.style.position = Position.Absolute;
            star.style.width    = starSize;
            star.style.height   = starSize;
            star.style.borderTopLeftRadius     = starSize / 2f;
            star.style.borderTopRightRadius    = starSize / 2f;
            star.style.borderBottomLeftRadius  = starSize / 2f;
            star.style.borderBottomRightRadius = starSize / 2f;
            star.style.left = centerX - starSize / 2f;
            star.style.top  = centerY - starSize / 2f;
            if (!ColorUtility.TryParseHtmlString(sys.starColorHex, out Color starColor))
                starColor = new Color(0.96f, 0.86f, 0.44f, 1f);
            star.style.backgroundColor = starColor;
            _orbitWorld.Add(star);
            _starDotEl = star;

            // ── Orbital bodies ────────────────────────────────────────────────
            int count = Mathf.Min(sys.bodies.Count, 9);
            // Distribute radii proportionally: 12% of half-width for innermost,
            // 88% for outermost — so orbits scale with the canvas size.
            float halfW = Mathf.Max(centerX, 60f);
            float innerR = halfW * 0.11f;
            float bottomReserve = 240f;
            float verticalLimit = Mathf.Max(30f, canvasH - bottomReserve - centerY);
            float outerR = Mathf.Min(halfW * 0.74f, verticalLimit);

            for (int i = 0; i < count; i++)
            {
                var   body   = sys.bodies[i];
                float radius = count == 1
                    ? (innerR + outerR) * 0.5f
                    : innerR + i * (outerR - innerR) / (count - 1f);

                var ring = new VisualElement();
                ring.style.position = Position.Absolute;
                ring.style.left     = centerX - radius;
                ring.style.top      = centerY - radius;
                ring.style.width    = radius * 2f;
                ring.style.height   = radius * 2f;
                ring.style.borderTopLeftRadius     = radius;
                ring.style.borderTopRightRadius    = radius;
                ring.style.borderBottomLeftRadius  = radius;
                ring.style.borderBottomRightRadius = radius;
                ring.style.borderTopWidth    = 1;
                ring.style.borderRightWidth  = 1;
                ring.style.borderBottomWidth = 1;
                ring.style.borderLeftWidth   = 1;
                ring.style.borderTopColor    = new Color(0.18f, 0.25f, 0.36f, 0.75f);
                ring.style.borderRightColor  = new Color(0.18f, 0.25f, 0.36f, 0.75f);
                ring.style.borderBottomColor = new Color(0.18f, 0.25f, 0.36f, 0.75f);
                ring.style.borderLeftColor   = new Color(0.18f, 0.25f, 0.36f, 0.75f);
                _orbitWorld.Add(ring);
                _orbitRingEls.Add((ring, radius));

                float bodySize = Mathf.Clamp(7f + body.size * 1.8f, 7f, 14f);
                var dot = new VisualElement();
                dot.style.position = Position.Absolute;
                dot.style.width    = bodySize;
                dot.style.height   = bodySize;
                dot.style.borderTopLeftRadius     = bodySize / 2f;
                dot.style.borderTopRightRadius    = bodySize / 2f;
                dot.style.borderBottomLeftRadius  = bodySize / 2f;
                dot.style.borderBottomRightRadius = bodySize / 2f;
                if (!ColorUtility.TryParseHtmlString(body.colorHex, out Color bodyColor))
                    bodyColor = new Color(0.62f, 0.68f, 0.76f, 1f);
                dot.style.backgroundColor = bodyColor;

                int capturedIdx = i;
                dot.RegisterCallback<ClickEvent>(_ =>
                {
                    if (capturedIdx >= 0 && capturedIdx < sys.bodies.Count)
                    {
                        var clickedBody = sys.bodies[capturedIdx];
                        if (_routePlotterActive)
                        {
                            float a = clickedBody.initialPhase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, clickedBody.orbitalPeriod));
                            AddRouteWaypoint(new RouteWaypoint
                            {
                                name = clickedBody.name ?? BodyTypeLabel(clickedBody.bodyType),
                                orbitalRadius = clickedBody.orbitalRadius,
                                angle = a,
                                isSystem = false,
                            });
                            return;
                        }
                        SelectOrbitBody(capturedIdx);
                        ShowBodyDetail(clickedBody, _viewedSystemIsHome && sys.stationOrbitIndex == capturedIdx);
                    }
                });
                _orbitWorld.Add(dot);

                var ov = new OrbitVisual
                {
                    bodyIndex    = i,
                    dot          = dot,
                    radius       = radius,
                    orbitalPeriodTicks = Mathf.Max(1f, body.orbitalPeriod),
                    phase        = body.initialPhase,
                    size         = bodySize,
                };
                _orbitVisuals.Add(ov);

                // ── Moons orbiting this body ──────────────────────────────────
                float moonOrbitBaseR = bodySize * 1.5f + 9f;
                for (int m = 0; m < Mathf.Min(body.moons.Count, 3); m++)
                {
                    var   moon     = body.moons[m];
                    float moonSize = Mathf.Clamp(bodySize * 0.32f, 3f, 5f);
                    var   moonDot  = new VisualElement();
                    moonDot.style.position = Position.Absolute;
                    moonDot.style.width    = moonSize;
                    moonDot.style.height   = moonSize;
                    moonDot.style.borderTopLeftRadius     = moonSize / 2f;
                    moonDot.style.borderTopRightRadius    = moonSize / 2f;
                    moonDot.style.borderBottomLeftRadius  = moonSize / 2f;
                    moonDot.style.borderBottomRightRadius = moonSize / 2f;
                    if (!ColorUtility.TryParseHtmlString(moon.colorHex, out Color moonCol))
                        moonCol = new Color(0.62f, 0.62f, 0.62f, 1f);
                    moonDot.style.backgroundColor = moonCol;
                    moonDot.style.opacity         = 0.75f;
                    _orbitWorld.Add(moonDot);
                    var moonVisual = new MoonVisual
                    {
                        parent          = ov,
                        dot             = moonDot,
                        moonOrbitRadius = moonOrbitBaseR + m * 6f,
                        orbitalPeriodTicks = Mathf.Max(12f, ov.orbitalPeriodTicks * (0.13f + m * 0.06f)),
                        phase           = moon.initialPhase,
                        size            = moonSize,
                        name            = moon.name ?? "Moon",
                    };
                    _moonVisuals.Add(moonVisual);

                    moonDot.RegisterCallback<ClickEvent>(_ => ShowBodyDetail(moon, false));
                }
            }

            // ── Station: independent entity orbiting its host body ────────────
            // Only show the station dot when viewing the home system.
            if (_viewedSystemIsHome
                && sys.stationOrbitIndex >= 0
                && sys.stationOrbitIndex < sys.bodies.Count
                && sys.stationOrbitIndex < _orbitVisuals.Count)
            {
                _stationParentBodyIndex = sys.stationOrbitIndex;
                float hostSize = _orbitVisuals[sys.stationOrbitIndex].size;
                float stSize   = 7f;
                _stationDot = new VisualElement();
                _stationDot.style.position = Position.Absolute;
                _stationDot.style.width    = stSize;
                _stationDot.style.height   = stSize;
                // Diamond shape via 45° rotation
                _stationDot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
                _stationDot.style.backgroundColor   = new Color(0.25f, 1.00f, 0.50f, 1f);
                _stationDot.style.borderTopWidth    = 1;
                _stationDot.style.borderRightWidth  = 1;
                _stationDot.style.borderBottomWidth = 1;
                _stationDot.style.borderLeftWidth   = 1;
                _stationDot.style.borderTopColor    = new Color(0.60f, 1f, 0.70f, 0.8f);
                _stationDot.style.borderRightColor  = new Color(0.60f, 1f, 0.70f, 0.8f);
                _stationDot.style.borderBottomColor = new Color(0.60f, 1f, 0.70f, 0.8f);
                _stationDot.style.borderLeftColor   = new Color(0.60f, 1f, 0.70f, 0.8f);
                _stationDot.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_stationParentBodyIndex >= 0 && _stationParentBodyIndex < _orbitVisuals.Count)
                        SelectOrbitBody(_stationParentBodyIndex);
                });
                _orbitWorld.Add(_stationDot);
            }
            else
            {
                _stationParentBodyIndex = -1;
                _stationDot = null;
            }

            // ── Selection ring (rendered on top) ──────────────────────────────
            _orbitSelectionRing = new VisualElement();
            _orbitSelectionRing.style.position       = Position.Absolute;
            _orbitSelectionRing.pickingMode           = PickingMode.Ignore;
            _orbitSelectionRing.style.borderTopWidth    = 2;
            _orbitSelectionRing.style.borderRightWidth  = 2;
            _orbitSelectionRing.style.borderBottomWidth = 2;
            _orbitSelectionRing.style.borderLeftWidth   = 2;
            _orbitSelectionRing.style.borderTopColor    = new Color(1f, 1f, 1f, 0.95f);
            _orbitSelectionRing.style.borderRightColor  = new Color(1f, 1f, 1f, 0.95f);
            _orbitSelectionRing.style.borderBottomColor = new Color(1f, 1f, 1f, 0.95f);
            _orbitSelectionRing.style.borderLeftColor   = new Color(1f, 1f, 1f, 0.95f);
            _orbitSelectionRing.style.display = DisplayStyle.None;
            _orbitWorld.Add(_orbitSelectionRing);

            _orbitHoverRing = new VisualElement();
            _orbitHoverRing.style.position       = Position.Absolute;
            _orbitHoverRing.pickingMode           = PickingMode.Ignore;
            _orbitHoverRing.style.borderTopWidth    = 2;
            _orbitHoverRing.style.borderRightWidth  = 2;
            _orbitHoverRing.style.borderBottomWidth = 2;
            _orbitHoverRing.style.borderLeftWidth   = 2;
            _orbitHoverRing.style.borderTopColor    = new Color(1f, 1f, 1f, 0.90f);
            _orbitHoverRing.style.borderRightColor  = new Color(1f, 1f, 1f, 0.90f);
            _orbitHoverRing.style.borderBottomColor = new Color(1f, 1f, 1f, 0.90f);
            _orbitHoverRing.style.borderLeftColor   = new Color(1f, 1f, 1f, 0.90f);
            _orbitHoverRing.style.display = DisplayStyle.None;
            _orbitWorld.Add(_orbitHoverRing);
        }

        private void ApplyOrbitWorldTransform()
        {
            if (_orbitWorld == null) return;
            _orbitWorld.style.translate = new Translate(_orbitPan.x, _orbitPan.y);
            _orbitWorld.style.scale = new Scale(new Vector3(_orbitZoom, _orbitZoom, 1f));
        }

        private void StartOrbitAnimation()
        {
            _orbitTime = _station?.tick ?? 0f;
            _orbitAnimator = schedule.Execute(() =>
            {
                if (_orbitCanvas == null || _orbitCanvas.panel == null) return;

                float dt = Mathf.Max(0.008f, Time.unscaledDeltaTime);
                _orbitTime += dt * OrbitPreviewTicksPerSecond;
                float cx = _orbitCenterX;
                float cy = _orbitCenterY;

                // Animate main orbital bodies and record their screen centres.
                foreach (var body in _orbitVisuals)
                {
                    float angle = body.phase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, body.orbitalPeriodTicks));
                    float x = cx + Mathf.Cos(angle) * body.radius - body.size * 0.5f;
                    float y = cy + Mathf.Sin(angle) * body.radius - body.size * 0.5f;
                    body.dot.style.left  = x;
                    body.dot.style.top   = y;
                    body.currentCenterX  = x + body.size * 0.5f;
                    body.currentCenterY  = y + body.size * 0.5f;
                }

                // Animate moons orbiting their parent bodies.
                foreach (var moon in _moonVisuals)
                {
                    float moonAngle = moon.phase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, moon.orbitalPeriodTicks));
                    float mx = moon.parent.currentCenterX
                             + Mathf.Cos(moonAngle) * moon.moonOrbitRadius
                             - moon.size * 0.5f;
                    float my = moon.parent.currentCenterY
                             + Mathf.Sin(moonAngle) * moon.moonOrbitRadius
                             - moon.size * 0.5f;
                    moon.dot.style.left = mx;
                    moon.dot.style.top  = my;
                }

                // Animate station orbiting its host body.
                if (_stationDot != null
                    && _stationParentBodyIndex >= 0
                    && _stationParentBodyIndex < _orbitVisuals.Count)
                {
                    var   host         = _orbitVisuals[_stationParentBodyIndex];
                    float stOrbitR     = host.size * 1.5f + 10f;
                    float stationPeriodTicks = Mathf.Max(8f, host.orbitalPeriodTicks * 0.10f);
                    float stAngle = StationPhaseOffset + Mathf.PI * 2f * (_orbitTime / stationPeriodTicks);
                    float sx = host.currentCenterX + Mathf.Cos(stAngle) * stOrbitR - 3.5f;
                    float sy = host.currentCenterY + Mathf.Sin(stAngle) * stOrbitR - 3.5f;
                    _stationDot.style.left = sx;
                    _stationDot.style.top  = sy;
                }

                UpdateProximityHover(dt);
                UpdateOrbitSelectionRing();
                UpdateOrbitHoverRing();
            }).Every(33);
        }

        private void StopOrbitVisualization()
        {
            _orbitAnimator?.Pause();
            _orbitAnimator = null;
            _orbitVisuals.Clear();
            _moonVisuals.Clear();
            _orbitRingEls.Clear();
            _stationDot             = null;
            _stationParentBodyIndex = -1;
            _starDotEl              = null;
            _orbitWorld             = null;
            _orbitSelectionRing     = null;
            _orbitHoverRing         = null;
            _orbitCanvas            = null;
            _hoverOrbitBodyIndex    = -1;
            _hoverMoonVisual        = null;
            _hoverStation           = false;
            _canvasHasPointer       = false;
            _proximityHoverTarget   = null;
            _systemHoverDelay?.Pause();
            _systemHoverDelay = null;
            _systemHoverTooltip = null;
        }

        private void SelectOrbitBody(int bodyIndex)
        {
            _selectedOrbitBodyIndex = bodyIndex;
            UpdateOrbitSelectionRing();
        }

        private void UpdateOrbitSelectionRing()
        {
            if (_orbitSelectionRing == null)
                return;

            OrbitVisual selected = null;
            foreach (var v in _orbitVisuals)
            {
                if (v.bodyIndex == _selectedOrbitBodyIndex)
                {
                    selected = v;
                    break;
                }
            }

            if (selected == null)
            {
                _orbitSelectionRing.style.display = DisplayStyle.None;
                return;
            }

            // Compute position from orbital math (avoids resolvedStyle lag/bounce).
            float angle = selected.phase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, selected.orbitalPeriodTicks));
            float dotCx  = _orbitCenterX + Mathf.Cos(angle) * selected.radius;
            float dotCy  = _orbitCenterY + Mathf.Sin(angle) * selected.radius;
            float ringSize = selected.size + 10f;
            _orbitSelectionRing.style.width  = ringSize;
            _orbitSelectionRing.style.height = ringSize;
            _orbitSelectionRing.style.left   = dotCx - ringSize * 0.5f;
            _orbitSelectionRing.style.top    = dotCy - ringSize * 0.5f;
            _orbitSelectionRing.style.borderTopLeftRadius     = ringSize * 0.5f;
            _orbitSelectionRing.style.borderTopRightRadius    = ringSize * 0.5f;
            _orbitSelectionRing.style.borderBottomLeftRadius  = ringSize * 0.5f;
            _orbitSelectionRing.style.borderBottomRightRadius = ringSize * 0.5f;
            _orbitSelectionRing.style.display = DisplayStyle.Flex;
        }

        private void UpdateOrbitHoverRing()
        {
            if (_orbitHoverRing == null) return;

            OrbitVisual hovered = null;
            foreach (var v in _orbitVisuals)
            {
                if (v.bodyIndex == _hoverOrbitBodyIndex)
                {
                    hovered = v;
                    break;
                }
            }

            if (hovered == null)
            {
                if (_hoverMoonVisual != null)
                {
                    float moonAngle = _hoverMoonVisual.phase
                                    + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, _hoverMoonVisual.orbitalPeriodTicks));
                    float moonCx = _hoverMoonVisual.parent.currentCenterX + Mathf.Cos(moonAngle) * _hoverMoonVisual.moonOrbitRadius;
                    float moonCy = _hoverMoonVisual.parent.currentCenterY + Mathf.Sin(moonAngle) * _hoverMoonVisual.moonOrbitRadius;
                    float moonRing = _hoverMoonVisual.size + 10f;
                    _orbitHoverRing.style.width = moonRing;
                    _orbitHoverRing.style.height = moonRing;
                    _orbitHoverRing.style.left = moonCx - moonRing * 0.5f;
                    _orbitHoverRing.style.top = moonCy - moonRing * 0.5f;
                    _orbitHoverRing.style.borderTopLeftRadius = moonRing * 0.5f;
                    _orbitHoverRing.style.borderTopRightRadius = moonRing * 0.5f;
                    _orbitHoverRing.style.borderBottomLeftRadius = moonRing * 0.5f;
                    _orbitHoverRing.style.borderBottomRightRadius = moonRing * 0.5f;
                    _orbitHoverRing.style.display = DisplayStyle.Flex;
                    return;
                }

                if (_hoverStation && _stationDot != null)
                {
                    var host = (_stationParentBodyIndex >= 0 && _stationParentBodyIndex < _orbitVisuals.Count)
                        ? _orbitVisuals[_stationParentBodyIndex]
                        : null;
                    if (host != null)
                    {
                        float stOrbitR = host.size * 1.5f + 10f;
                        float stationPeriodTicks = Mathf.Max(8f, host.orbitalPeriodTicks * 0.10f);
                        float stAngle = StationPhaseOffset + Mathf.PI * 2f * (_orbitTime / stationPeriodTicks);
                        float sx = host.currentCenterX + Mathf.Cos(stAngle) * stOrbitR;
                        float sy = host.currentCenterY + Mathf.Sin(stAngle) * stOrbitR;
                        float stRing = 12f;
                        _orbitHoverRing.style.width = stRing;
                        _orbitHoverRing.style.height = stRing;
                        _orbitHoverRing.style.left = sx - stRing * 0.5f;
                        _orbitHoverRing.style.top = sy - stRing * 0.5f;
                        _orbitHoverRing.style.borderTopLeftRadius = stRing * 0.5f;
                        _orbitHoverRing.style.borderTopRightRadius = stRing * 0.5f;
                        _orbitHoverRing.style.borderBottomLeftRadius = stRing * 0.5f;
                        _orbitHoverRing.style.borderBottomRightRadius = stRing * 0.5f;
                        _orbitHoverRing.style.display = DisplayStyle.Flex;
                        return;
                    }
                }

                _orbitHoverRing.style.display = DisplayStyle.None;
                return;
            }

            float angle = hovered.phase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, hovered.orbitalPeriodTicks));
            float dotCx = _orbitCenterX + Mathf.Cos(angle) * hovered.radius;
            float dotCy = _orbitCenterY + Mathf.Sin(angle) * hovered.radius;
            float ringSize = hovered.size + 12f;
            _orbitHoverRing.style.width = ringSize;
            _orbitHoverRing.style.height = ringSize;
            _orbitHoverRing.style.left = dotCx - ringSize * 0.5f;
            _orbitHoverRing.style.top = dotCy - ringSize * 0.5f;
            _orbitHoverRing.style.borderTopLeftRadius = ringSize * 0.5f;
            _orbitHoverRing.style.borderTopRightRadius = ringSize * 0.5f;
            _orbitHoverRing.style.borderBottomLeftRadius = ringSize * 0.5f;
            _orbitHoverRing.style.borderBottomRightRadius = ringSize * 0.5f;
            _orbitHoverRing.style.display = DisplayStyle.Flex;
        }

        // ── Proximity-based hover detection ───────────────────────────────────

        /// <summary>
        /// Runs every animation tick.  Finds the closest body/moon/station to the
        /// stored pointer position and drives hover state + tooltip from there.
        /// This replaces per-dot PointerEnter/Leave which are unreliable on small
        /// moving elements.
        /// </summary>
        private void UpdateProximityHover(float dt)
        {
            if (!_canvasHasPointer || _orbitCanvas == null)
            {
                if (_hoverOrbitBodyIndex >= 0 || _hoverMoonVisual != null || _hoverStation)
                {
                    ClearHoverScale();
                    _hoverOrbitBodyIndex = -1;
                    _hoverMoonVisual = null;
                    _hoverStation = false;
                    _proximityHoverTarget = null;
                    HideSystemHoverTooltip();
                    _systemHoverDelay?.Pause();
                }
                return;
            }

            // Convert canvas-local pointer to orbit-world coordinates.
            float canvasW = _orbitCanvas.resolvedStyle.width;
            float canvasH = _orbitCanvas.resolvedStyle.height;
            if (canvasW < 10f || canvasH < 10f) return;
            float pivotX = canvasW * 0.5f;
            float pivotY = canvasH * 0.5f;
            float worldPtrX = (_canvasPointerLocal.x - pivotX - _orbitPan.x) / Mathf.Max(0.01f, _orbitZoom) + pivotX;
            float worldPtrY = (_canvasPointerLocal.y - pivotY - _orbitPan.y) / Mathf.Max(0.01f, _orbitZoom) + pivotY;

            float bestDistSq = float.MaxValue;
            int bestBody = -1;
            MoonVisual bestMoon = null;
            bool bestStation = false;
            string bestName = null;

            // Check bodies.
            var sys = _viewedSystem ?? _station?.solarSystem;
            foreach (var bv in _orbitVisuals)
            {
                float dx = bv.currentCenterX - worldPtrX;
                float dy = bv.currentCenterY - worldPtrY;
                float dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestBody = bv.bodyIndex;
                    bestMoon = null;
                    bestStation = false;
                    bestName = sys != null && bv.bodyIndex < sys.bodies.Count
                        ? sys.bodies[bv.bodyIndex].name ?? BodyTypeLabel(sys.bodies[bv.bodyIndex].bodyType)
                        : $"Body {bv.bodyIndex}";
                }
            }

            // Check moons.
            foreach (var moon in _moonVisuals)
            {
                float moonAngle = moon.phase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, moon.orbitalPeriodTicks));
                float mx = moon.parent.currentCenterX + Mathf.Cos(moonAngle) * moon.moonOrbitRadius;
                float my = moon.parent.currentCenterY + Mathf.Sin(moonAngle) * moon.moonOrbitRadius;
                float dx = mx - worldPtrX;
                float dy = my - worldPtrY;
                float dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestBody = -1;
                    bestMoon = moon;
                    bestStation = false;
                    bestName = moon.name;
                }
            }

            // Check station.
            if (_stationDot != null && _stationParentBodyIndex >= 0 && _stationParentBodyIndex < _orbitVisuals.Count)
            {
                var host = _orbitVisuals[_stationParentBodyIndex];
                float stOrbitR = host.size * 1.5f + 10f;
                float stationPeriodTicks = Mathf.Max(8f, host.orbitalPeriodTicks * 0.10f);
                float stAngle = StationPhaseOffset + Mathf.PI * 2f * (_orbitTime / stationPeriodTicks);
                float sx = host.currentCenterX + Mathf.Cos(stAngle) * stOrbitR;
                float sy = host.currentCenterY + Mathf.Sin(stAngle) * stOrbitR;
                float dx = sx - worldPtrX;
                float dy = sy - worldPtrY;
                float dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestBody = -1;
                    bestMoon = null;
                    bestStation = true;
                    bestName = "Your Station";
                }
            }

            float threshold = HoverProximityThreshold / Mathf.Max(0.01f, _orbitZoom);
            float threshSq = threshold * threshold;

            if (bestDistSq <= threshSq)
            {
                // Target changed — clear previous scale.
                if (bestBody != _hoverOrbitBodyIndex || bestMoon != _hoverMoonVisual || bestStation != _hoverStation)
                    ClearHoverScale();

                _hoverOrbitBodyIndex = bestBody;
                _hoverMoonVisual = bestMoon;
                _hoverStation = bestStation;

                // Apply hover scale.
                if (bestBody >= 0)
                {
                    foreach (var v in _orbitVisuals)
                        if (v.bodyIndex == bestBody)
                        {
                            v.dot.style.scale = new Scale(new Vector3(1.25f, 1.25f, 1f));
                            break;
                        }
                }
                else if (bestMoon != null)
                    bestMoon.dot.style.scale = new Scale(new Vector3(1.25f, 1.25f, 1f));

                // Tooltip management.
                if (bestName != _proximityHoverTarget)
                {
                    _proximityHoverTarget = bestName;
                    _proximityHoverTime = 0f;
                    HideSystemHoverTooltip();
                }
                else
                {
                    _proximityHoverTime += dt;
                    if (_proximityHoverTime >= TooltipDelaySeconds && bestName != null)
                    {
                        EnsureSystemHoverTooltip();
                        ShowSystemHoverTooltip(bestName, _canvasPointerLocal + new Vector2(12f, 12f));
                    }
                }
            }
            else
            {
                if (_hoverOrbitBodyIndex >= 0 || _hoverMoonVisual != null || _hoverStation)
                {
                    ClearHoverScale();
                    _hoverOrbitBodyIndex = -1;
                    _hoverMoonVisual = null;
                    _hoverStation = false;
                    _proximityHoverTarget = null;
                    HideSystemHoverTooltip();
                }
                else if (_proximityHoverTarget != null)
                {
                    _proximityHoverTarget = null;
                    HideSystemHoverTooltip();
                }
            }
        }

        private void ClearHoverScale()
        {
            if (_hoverOrbitBodyIndex >= 0)
            {
                foreach (var v in _orbitVisuals)
                    if (v.bodyIndex == _hoverOrbitBodyIndex)
                    {
                        v.dot.style.scale = new Scale(new Vector3(1f, 1f, 1f));
                        break;
                    }
            }
            if (_hoverMoonVisual != null)
                _hoverMoonVisual.dot.style.scale = new Scale(new Vector3(1f, 1f, 1f));
        }

        private VisualElement BuildBodyRow(SolarBody body, bool hasStation, int bodyIndex, SolarSystemState sys)
        {
            var row = new VisualElement();
            row.AddToClassList(SystemBodyRowClass);
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.paddingLeft     = 8;
            row.style.paddingTop      = 6;
            row.style.paddingBottom   = 6;
            row.style.paddingRight    = 8;
            row.style.marginBottom    = 2;
            row.style.backgroundColor = ColBodyRow;
            row.style.BorderRadius(3);

            // Colour dot
            var dot = new VisualElement();
            dot.style.width           = 10;
            dot.style.height          = 10;
            dot.style.minWidth        = 10;
            dot.style.marginRight     = 8;
            dot.style.borderTopLeftRadius     = 5;
            dot.style.borderTopRightRadius    = 5;
            dot.style.borderBottomLeftRadius  = 5;
            dot.style.borderBottomRightRadius = 5;

            if (ColorUtility.TryParseHtmlString(body.colorHex, out Color bodyCol))
                dot.style.backgroundColor = bodyCol;
            else
                dot.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);

            row.Add(dot);

            // Body name
            var nameLabel = new Label(body.name ?? BodyTypeLabel(body.bodyType));
            nameLabel.AddToClassList(SystemBodyNameClass);
            nameLabel.style.color     = ColTextBright;
            nameLabel.style.flexGrow  = 1;
            nameLabel.style.fontSize  = Fs(11);
            row.Add(nameLabel);

            // Orbit distance (AU)
            if (body.orbitalRadius > 0f)
            {
                var distLabel = new Label($"{body.orbitalRadius:F2} AU");
                distLabel.style.color    = ColTextMid;
                distLabel.style.fontSize = Fs(9);
                distLabel.style.marginRight = 6;
                row.Add(distLabel);
            }

            // Orbit time (compact)
            if (body.orbitalPeriod > 0f)
            {
                float minutes = body.orbitalPeriod * MinutesPerTick;
                float hours = minutes / 60f;
                float days = hours / 24f;
                string orbitText = days >= 1f ? $"{days:F1}d" : $"{hours:F1}h";
                var orbitLabel = new Label(orbitText);
                orbitLabel.style.color    = ColTextMid;
                orbitLabel.style.fontSize = Fs(9);
                orbitLabel.style.marginRight = 4;
                row.Add(orbitLabel);
            }

            // Hover highlight
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = ColBodyRowHov);
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = ColBodyRow);

            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (_routePlotterActive)
                {
                    float angle = body.initialPhase + Mathf.PI * 2f * (_orbitTime / Mathf.Max(1f, body.orbitalPeriod));
                    AddRouteWaypoint(new RouteWaypoint
                    {
                        name = body.name ?? BodyTypeLabel(body.bodyType),
                        orbitalRadius = body.orbitalRadius,
                        angle = angle,
                        isSystem = false,
                    });
                    return;
                }
                SelectOrbitBody(bodyIndex);
                ShowBodyDetail(body, _viewedSystemIsHome && sys.stationOrbitIndex == bodyIndex);
            });

            return row;
        }

        // ── Sector view ───────────────────────────────────────────────────────

            private VisualElement BuildMoonRow(SolarBody moon)
            {
                var row = new VisualElement();
                row.style.flexDirection   = FlexDirection.Row;
                row.style.alignItems      = Align.Center;
                row.style.paddingLeft     = 26;
                row.style.paddingTop      = 3;
                row.style.paddingBottom   = 3;
                row.style.paddingRight    = 8;
                row.style.marginBottom    = 1;
                row.style.backgroundColor = new Color(0.08f, 0.11f, 0.17f, 0.80f);
                row.style.BorderRadius(2);

                // Connector line ─
                var connector = new VisualElement();
                connector.style.width           = 10;
                connector.style.height          = 1;
                connector.style.backgroundColor = ColDivider;
                connector.style.marginRight     = 6;
                row.Add(connector);

                var dot = new VisualElement();
                dot.style.width  = 6;
                dot.style.height = 6;
                dot.style.minWidth               = 6;
                dot.style.marginRight            = 6;
                dot.style.borderTopLeftRadius    = 3;
                dot.style.borderTopRightRadius   = 3;
                dot.style.borderBottomLeftRadius = 3;
                dot.style.borderBottomRightRadius = 3;
                if (ColorUtility.TryParseHtmlString(moon.colorHex, out Color moonCol))
                    dot.style.backgroundColor = moonCol;
                else
                    dot.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                row.Add(dot);

                var nameLabel = new Label(moon.name ?? "Moon");
                nameLabel.style.color   = new Color(0.60f, 0.70f, 0.80f, 1f);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.fontSize = (int)(10 * _fontScale);
                row.Add(nameLabel);

                return row;
            }

            private VisualElement BuildStationSubRow()
            {
                var row = new VisualElement();
                row.style.flexDirection   = FlexDirection.Row;
                row.style.alignItems      = Align.Center;
                row.style.paddingLeft     = 26;
                row.style.paddingTop      = 3;
                row.style.paddingBottom   = 3;
                row.style.paddingRight    = 8;
                row.style.marginBottom    = 1;
                row.style.backgroundColor = new Color(0.07f, 0.14f, 0.10f, 0.70f);
                row.style.BorderRadius(2);

                // Connector line ─
                var connector = new VisualElement();
                connector.style.width           = 10;
                connector.style.height          = 1;
                connector.style.backgroundColor = new Color(0.25f, 1.00f, 0.50f, 0.4f);
                connector.style.marginRight     = 6;
                row.Add(connector);

                // Diamond marker
                var diamond = new VisualElement();
                diamond.style.width               = 6;
                diamond.style.height              = 6;
                diamond.style.minWidth            = 6;
                diamond.style.marginRight         = 6;
                diamond.style.backgroundColor     = new Color(0.25f, 1.00f, 0.50f, 1f);
                diamond.style.rotate              = new Rotate(new Angle(45f, AngleUnit.Degree));
                row.Add(diamond);

                var label = new Label("YOUR STATION");
                label.style.color                   = new Color(0.25f, 1.00f, 0.50f, 1f);
                label.style.flexGrow                = 1;
                label.style.fontSize                = (int)(10 * _fontScale);
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(label);

                return row;
            }

        private VisualElement BuildMapLegend(bool compact = false, bool sectorMode = false)
        {
            var card = new VisualElement();
            card.style.alignSelf = Align.FlexStart;
            card.style.marginBottom = compact ? 0 : 8;
            card.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.72f);
            card.style.borderTopWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderTopColor = ColDivider;
            card.style.borderRightColor = ColDivider;
            card.style.borderBottomColor = ColDivider;
            card.style.borderLeftColor = ColDivider;

            var toggle = new Button { text = "MAP KEY ▾" };
            toggle.style.fontSize = Fs(10);
            toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
            toggle.style.paddingLeft = 8;
            toggle.style.paddingRight = 8;
            toggle.style.paddingTop = 4;
            toggle.style.paddingBottom = 4;
            toggle.style.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 1f);
            toggle.style.color = ColTextBright;
            card.Add(toggle);

            var body = new VisualElement();
            body.style.paddingLeft = 8;
            body.style.paddingRight = 8;
            body.style.paddingTop = 6;
            body.style.paddingBottom = 6;
            body.style.display = DisplayStyle.None;
            card.Add(body);

            if (sectorMode)
            {
                body.Add(BuildLegendLine("□", new Color(0.36f, 0.54f, 0.76f, 0.95f), "Sector tile"));
                body.Add(BuildLegendLine("+", new Color(0.39f, 0.75f, 1f, 0.95f), "Expandable edge"));
                body.Add(BuildLegendLine("•", new Color(0.62f, 0.86f, 1.00f, 0.95f), "System marker"));
                body.Add(BuildLegendLine("◉", new Color(0.70f, 0.82f, 0.96f, 0.85f), "Archetype icon"));
                body.Add(BuildLegendLine("░", new Color(0.44f, 0.53f, 0.68f, 0.55f), "Dust lane"));
            }
            else
            {
                body.Add(BuildLegendLine("●", new Color(0.80f, 0.68f, 0.52f, 1f), "Planet body"));
                body.Add(BuildLegendLine("◆", new Color(0.25f, 1.00f, 0.50f, 1f), "Your station"));
                body.Add(BuildLegendLine("○", new Color(0.56f, 0.71f, 0.90f, 0.8f), "Orbit ring"));
                body.Add(BuildLegendLine("•", new Color(0.64f, 0.74f, 0.84f, 0.9f), "Moon"));
            }

            bool expanded = false;
            toggle.clicked += () =>
            {
                expanded = !expanded;
                body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                toggle.text = expanded ? "MAP KEY ▴" : "MAP KEY ▾";
            };

            return card;
        }

        private VisualElement BuildLegendLine(string icon, Color iconColor, string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            var iconLabel = new Label(icon);
            iconLabel.style.width = 14;
            iconLabel.style.minWidth = 14;
            iconLabel.style.color = iconColor;
            iconLabel.style.fontSize = Fs(10);
            row.Add(iconLabel);

            var textLabel = new Label(text);
            textLabel.style.color = ColTextBright;
            textLabel.style.fontSize = Fs(10);
            row.Add(textLabel);

            return row;
        }

        private void BuildSectorView()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow      = 1;
            root.style.height        = Length.Percent(100);
            root.style.paddingLeft   = 8;
            root.style.paddingTop    = 8;
            root.style.paddingRight  = 8;
            root.style.paddingBottom = 8;

            if (_station == null || _station.sectors.Count == 0)
            {
                var empty = new Label("No sectors charted. Build an Interstellar Antenna to explore.");
                empty.AddToClassList(EmptyClass);
                empty.style.color     = ColTextMid;
                empty.style.marginTop = 40;
                root.Add(empty);
                _viewArea.Add(root);
                return;
            }

            root.Add(BuildSectorStarChart(_station));

            _viewArea.Add(root);
        }

        private VisualElement BuildSectorStarChart(StationState station)
        {
            var card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.height = Length.Percent(100);
            card.style.minHeight = 360;
            card.style.marginBottom = 0;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.96f);
            card.style.borderTopWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderTopColor = ColDivider;
            card.style.borderRightColor = ColDivider;
            card.style.borderBottomColor = ColDivider;
            card.style.borderLeftColor = ColDivider;

            _sectorChartViewport = new VisualElement();
            _sectorChartViewport.style.position = Position.Relative;
            _sectorChartViewport.style.flexGrow = 1;
            _sectorChartViewport.style.height = Length.Percent(100);
            _sectorChartViewport.style.minHeight = 340;
            _sectorChartViewport.style.overflow = Overflow.Hidden;
            _sectorChartViewport.style.backgroundColor = new Color(0.03f, 0.05f, 0.09f, 0.95f);
            _sectorChartViewport.style.borderTopWidth = 1;
            _sectorChartViewport.style.borderRightWidth = 1;
            _sectorChartViewport.style.borderBottomWidth = 1;
            _sectorChartViewport.style.borderLeftWidth = 1;
            _sectorChartViewport.style.borderTopColor = new Color(0.14f, 0.20f, 0.30f, 1f);
            _sectorChartViewport.style.borderRightColor = new Color(0.14f, 0.20f, 0.30f, 1f);
            _sectorChartViewport.style.borderBottomColor = new Color(0.14f, 0.20f, 0.30f, 1f);
            _sectorChartViewport.style.borderLeftColor = new Color(0.14f, 0.20f, 0.30f, 1f);
            card.Add(_sectorChartViewport);

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 8;
            overlay.style.top = 8;
            overlay.style.flexDirection = FlexDirection.Column;
            _sectorChartViewport.Add(overlay);

            var sectorName = new Label("SECTOR GRID");
            sectorName.style.color = ColAccentText;
            sectorName.style.fontSize = Fs(14);
            sectorName.style.unityFontStyleAndWeight = FontStyle.Bold;
            sectorName.style.marginBottom = 4;
            sectorName.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.72f);
            sectorName.style.paddingLeft = 8;
            sectorName.style.paddingRight = 8;
            sectorName.style.paddingTop = 4;
            sectorName.style.paddingBottom = 4;
            sectorName.style.borderTopWidth = 1;
            sectorName.style.borderRightWidth = 1;
            sectorName.style.borderBottomWidth = 1;
            sectorName.style.borderLeftWidth = 1;
            sectorName.style.borderTopColor = ColDivider;
            sectorName.style.borderRightColor = ColDivider;
            sectorName.style.borderBottomColor = ColDivider;
            sectorName.style.borderLeftColor = ColDivider;
            overlay.Add(sectorName);

            overlay.Add(BuildMapLegend(compact: true, sectorMode: true));

            _sectorChartWorld = new VisualElement();
            _sectorChartWorld.style.position = Position.Absolute;
            _sectorChartWorld.style.left = 0;
            _sectorChartWorld.style.top = 0;
            _sectorChartWorld.style.right = 0;
            _sectorChartWorld.style.bottom = 0;
            _sectorChartViewport.Add(_sectorChartWorld);

            _sectorZoom = 1f;
            _sectorPan = Vector2.zero;
            _sectorPanning = false;
            ApplySectorWorldTransform();

            var sectors = new List<SectorData>(station.sectors.Values);
            if (sectors.Count == 0) return card;

            void RebuildChart(float width, float height)
            {
                _sectorChartWorld.Clear();
                AddNebulaBackdrop(_sectorChartWorld, width, height, station.galaxySeed);

                var known = new Dictionary<Vector2Int, SectorData>();
                foreach (var s in sectors)
                {
                    int col = Mathf.RoundToInt((s.coordinates.x - GalaxyGenerator.HomeX) / MapSystem.GalUnitPerCell);
                    int row = Mathf.RoundToInt((s.coordinates.y - GalaxyGenerator.HomeY) / MapSystem.GalUnitPerCell);
                    known[new Vector2Int(col, row)] = s;
                }

                var candidates = new HashSet<Vector2Int>();
                var dirs = new[]
                {
                    new Vector2Int(0, 1),
                    new Vector2Int(1, 0),
                    new Vector2Int(0, -1),
                    new Vector2Int(-1, 0)
                };
                foreach (var cell in known.Keys)
                {
                    foreach (var d in dirs)
                    {
                        var n = new Vector2Int(cell.x + d.x, cell.y + d.y);
                        if (!known.ContainsKey(n)) candidates.Add(n);
                    }
                }

                int minCol = int.MaxValue;
                int maxCol = int.MinValue;
                int minRow = int.MaxValue;
                int maxRow = int.MinValue;
                foreach (var k in known.Keys)
                {
                    if (k.x < minCol) minCol = k.x;
                    if (k.x > maxCol) maxCol = k.x;
                    if (k.y < minRow) minRow = k.y;
                    if (k.y > maxRow) maxRow = k.y;
                }
                foreach (var c in candidates)
                {
                    if (c.x < minCol) minCol = c.x;
                    if (c.x > maxCol) maxCol = c.x;
                    if (c.y < minRow) minRow = c.y;
                    if (c.y > maxRow) maxRow = c.y;
                }

                const float cellW = 168f;
                const float cellH = 116f;
                const float cellGap = 3f;
                const float pad = 48f;

                float worldW = (maxCol - minCol + 1) * cellW + (maxCol - minCol) * cellGap + pad * 2f;
                float worldH = (maxRow - minRow + 1) * cellH + (maxRow - minRow) * cellGap + pad * 2f;

                Vector2 CellPos(int col, int row)
                {
                    float x = (width - worldW) * 0.5f + pad + (col - minCol) * (cellW + cellGap);
                    float y = (height - worldH) * 0.5f + pad + (maxRow - row) * (cellH + cellGap);
                    return new Vector2(x, y);
                }

                bool canUnlock = SystemMapController.TelescopeMode || station.explorationPoints >= MapSystem.SectorUnlockPointCost;

                foreach (var kv in known)
                {
                    Vector2 p = CellPos(kv.Key.x, kv.Key.y);
                    var sector = kv.Value;

                    var sectorBox = new VisualElement();
                    sectorBox.style.position = Position.Absolute;
                    sectorBox.style.left = p.x;
                    sectorBox.style.top = p.y;
                    sectorBox.style.width = cellW;
                    sectorBox.style.height = cellH;
                    sectorBox.style.overflow = Overflow.Visible;
                    sectorBox.style.backgroundColor = sector.discoveryState == SectorDiscoveryState.Visited
                        ? new Color(0.08f, 0.16f, 0.23f, 0.94f)
                        : new Color(0.08f, 0.12f, 0.19f, 0.90f);

                    // Archetype tint overlay.
                    var tint = ArchetypeTint(sector.archetype);
                    if (tint.a > 0f)
                    {
                        var tintOverlay = new VisualElement();
                        tintOverlay.style.position = Position.Absolute;
                        tintOverlay.style.left = 0; tintOverlay.style.top = 0;
                        tintOverlay.style.right = 0; tintOverlay.style.bottom = 0;
                        tintOverlay.style.backgroundColor = tint;
                        tintOverlay.pickingMode = PickingMode.Ignore;
                        sectorBox.Add(tintOverlay);
                    }
                    sectorBox.style.borderTopWidth = 1;
                    sectorBox.style.borderRightWidth = 1;
                    sectorBox.style.borderBottomWidth = 1;
                    sectorBox.style.borderLeftWidth = 1;
                    Color baseBorder = ResolveSectorBorderColor(sector);
                    sectorBox.style.borderTopColor = baseBorder;
                    sectorBox.style.borderRightColor = sectorBox.style.borderTopColor;
                    sectorBox.style.borderBottomColor = sectorBox.style.borderTopColor;
                    sectorBox.style.borderLeftColor = sectorBox.style.borderTopColor;
                    _sectorChartWorld.Add(sectorBox);

                    string hoverNameText = string.IsNullOrWhiteSpace(sector.properName)
                        ? sector.ShortCodeAndCoord()
                        : $"{sector.ShortCodeAndCoord()}  \"{sector.properName}\"";
                    var hoverName = new Label(hoverNameText);
                    hoverName.style.position = Position.Absolute;
                    hoverName.style.left = 4;
                    hoverName.style.top = -22;
                    hoverName.style.maxWidth = cellW + 70f;
                    hoverName.style.paddingLeft = 6;
                    hoverName.style.paddingRight = 6;
                    hoverName.style.paddingTop = 2;
                    hoverName.style.paddingBottom = 2;
                    hoverName.style.backgroundColor = new Color(0.06f, 0.10f, 0.18f, 0.96f);
                    hoverName.style.borderTopWidth = 1;
                    hoverName.style.borderRightWidth = 1;
                    hoverName.style.borderBottomWidth = 1;
                    hoverName.style.borderLeftWidth = 1;
                    hoverName.style.borderTopColor = new Color(0.34f, 0.52f, 0.74f, 0.95f);
                    hoverName.style.borderRightColor = new Color(0.34f, 0.52f, 0.74f, 0.95f);
                    hoverName.style.borderBottomColor = new Color(0.12f, 0.20f, 0.32f, 0.95f);
                    hoverName.style.borderLeftColor = new Color(0.12f, 0.20f, 0.32f, 0.95f);
                    hoverName.style.color = new Color(0.82f, 0.90f, 1.00f, 1f);
                    hoverName.style.fontSize = Fs(10);
                    hoverName.style.unityFontStyleAndWeight = FontStyle.Bold;
                    hoverName.style.display = DisplayStyle.None;
                    hoverName.pickingMode = PickingMode.Ignore;
                    sectorBox.Add(hoverName);

                    // Archetype icon (top-right, visible only for visited sectors).
                    if (sector.discoveryState == SectorDiscoveryState.Visited)
                    {
                        var icon = new Label(ArchetypeIcon(sector.archetype));
                        icon.style.position = Position.Absolute;
                        icon.style.right = 4;
                        icon.style.top = 3;
                        icon.style.fontSize = Fs(12);
                        icon.style.color = new Color(0.70f, 0.82f, 0.96f, 0.85f);
                        icon.pickingMode = PickingMode.Ignore;
                        sectorBox.Add(icon);
                    }

                    var cluster = new VisualElement();
                    cluster.style.position = Position.Absolute;
                    cluster.style.left = 1;
                    cluster.style.top = 1;
                    cluster.style.right = 1;
                    cluster.style.bottom = 1;
                    cluster.style.backgroundColor = new Color(0.03f, 0.05f, 0.10f, 0.85f);
                    sectorBox.Add(cluster);

                    int dotCount = sector.systemCount > 0 ? sector.systemCount : 12;
                    AddSectorSystemCluster(cluster, cellW - 2f, cellH - 2f, station.galaxySeed ^ sector.uid.GetHashCode(), sector, dotCount, hoverName, hoverNameText);

                    var capturedSector = sector;
                    sectorBox.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        sectorBox.BringToFront();
                        hoverName.style.display = DisplayStyle.Flex;
                        sectorBox.style.borderTopColor = new Color(1f, 1f, 1f, 0.95f);
                        sectorBox.style.borderRightColor = new Color(1f, 1f, 1f, 0.95f);
                        sectorBox.style.borderBottomColor = new Color(1f, 1f, 1f, 0.95f);
                        sectorBox.style.borderLeftColor = new Color(1f, 1f, 1f, 0.95f);
                    });
                    sectorBox.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        hoverName.style.display = DisplayStyle.None;
                        Color border = ResolveSectorBorderColor(sector);
                        sectorBox.style.borderTopColor = border;
                        sectorBox.style.borderRightColor = border;
                        sectorBox.style.borderBottomColor = border;
                        sectorBox.style.borderLeftColor = border;
                    });
                    sectorBox.RegisterCallback<ClickEvent>(_ => ShowSectorDetail(capturedSector));
                }

                foreach (var c in candidates)
                {
                    Vector2 p = CellPos(c.x, c.y);
                    var plus = new Button();
                    plus.text = "+";
                    plus.style.position = Position.Absolute;
                    plus.style.left = p.x + (cellW * 0.5f) - 16f;
                    plus.style.top = p.y + (cellH * 0.5f) - 16f;
                    plus.style.width = 32;
                    plus.style.height = 32;
                    plus.style.paddingLeft = 0;
                    plus.style.paddingRight = 0;
                    plus.style.paddingTop = 0;
                    plus.style.paddingBottom = 0;
                    plus.style.unityTextAlign = TextAnchor.MiddleCenter;
                    plus.style.fontSize = Fs(15);
                    plus.style.unityFontStyleAndWeight = FontStyle.Bold;
                    plus.style.backgroundColor = canUnlock
                        ? new Color(0.12f, 0.22f, 0.34f, 0.95f)
                        : new Color(0.12f, 0.14f, 0.18f, 0.75f);
                    plus.style.color = canUnlock ? ColAccentText : new Color(0.35f, 0.40f, 0.48f, 0.95f);
                    plus.style.borderTopWidth = 1;
                    plus.style.borderRightWidth = 1;
                    plus.style.borderBottomWidth = 1;
                    plus.style.borderLeftWidth = 1;
                    plus.style.borderTopColor = ColDivider;
                    plus.style.borderRightColor = ColDivider;
                    plus.style.borderBottomColor = ColDivider;
                    plus.style.borderLeftColor = ColDivider;
                    plus.SetEnabled(canUnlock);
                    plus.tooltip = canUnlock
                        ? "Expand sector grid"
                        : $"Need {MapSystem.SectorUnlockPointCost} EP to expand";

                    int unlockCol = c.x;
                    int unlockRow = c.y;
                    plus.clicked += () =>
                    {
                        var newSector = TryUnlockSectorAtAndGetSector(unlockCol, unlockRow);
                        if (newSector == null) return;
                        OnSectorUnlocked?.Invoke(newSector);
                        ShowSectorDetail(newSector);
                        Refresh(_station, _map);
                    };

                    _sectorChartWorld.Add(plus);
                }
            }

            _sectorChartViewport.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.width < 20f || evt.newRect.height < 20f) return;
                RebuildChart(evt.newRect.width, evt.newRect.height);
            });
            _sectorChartViewport.RegisterCallback<WheelEvent>(evt =>
            {
                float zoom = Mathf.Clamp(_sectorZoom * (1f - evt.delta.y * 0.0075f), 0.45f, 3.4f);
                if (Mathf.Abs(zoom - _sectorZoom) < 0.001f) return;
                _sectorZoom = zoom;
                ApplySectorWorldTransform();
                evt.StopPropagation();
            });
            _sectorChartViewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return;
                _sectorPanning = true;
                _sectorPanStartMouse = (Vector2)evt.position;
                _sectorPanStartOffset = _sectorPan;
                _sectorChartViewport.CapturePointer(evt.pointerId);
            });
            _sectorChartViewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_sectorPanning) return;
                Vector2 delta = (Vector2)evt.position - _sectorPanStartMouse;
                _sectorPan = _sectorPanStartOffset + delta;
                ApplySectorWorldTransform();
            });
            _sectorChartViewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 1) return;
                _sectorPanning = false;
                _sectorChartViewport.ReleasePointer(evt.pointerId);
            });

            return card;
        }

        private void ApplySectorWorldTransform()
        {
            if (_sectorChartWorld == null) return;
            _sectorChartWorld.style.translate = new Translate(_sectorPan.x, _sectorPan.y);
            _sectorChartWorld.style.scale = new Scale(new Vector3(_sectorZoom, _sectorZoom, 1f));
        }

        private static void AddNebulaBackdrop(VisualElement parent, float width, float height, int seed)
        {
            var rng = new System.Random(seed ^ 0x5f3759df);

            // Thin stratified haze bands (less "blob-like" than ovals).
            int bands = 4;
            for (int i = 0; i < bands; i++)
            {
                float w = 220f + (float)rng.NextDouble() * 360f;
                float h = 36f + (float)rng.NextDouble() * 80f;
                float x = (float)rng.NextDouble() * Mathf.Max(1f, width - w);
                float y = (float)rng.NextDouble() * Mathf.Max(1f, height - h);

                var band = new VisualElement();
                band.style.position = Position.Absolute;
                band.style.left = x;
                band.style.top = y;
                band.style.width = w;
                band.style.height = h;
                band.style.backgroundColor = new Color(
                    0.11f + (float)rng.NextDouble() * 0.10f,
                    0.16f + (float)rng.NextDouble() * 0.12f,
                    0.24f + (float)rng.NextDouble() * 0.14f,
                    0.10f);
                band.style.borderTopWidth = 1;
                band.style.borderBottomWidth = 1;
                band.style.borderTopColor = new Color(0.28f, 0.42f, 0.62f, 0.15f);
                band.style.borderBottomColor = new Color(0.03f, 0.06f, 0.10f, 0.35f);
                parent.Add(band);

                // Layer a shifted slice to break up hard rectangle edges.
                var slice = new VisualElement();
                slice.style.position = Position.Absolute;
                slice.style.left = x + 14f;
                slice.style.top = y - 8f;
                slice.style.width = Mathf.Max(40f, w * 0.72f);
                slice.style.height = Mathf.Max(18f, h * 0.58f);
                slice.style.backgroundColor = new Color(0.15f, 0.20f, 0.31f, 0.10f);
                parent.Add(slice);
            }

            // Sparse stardust points to avoid flat negative space.
            int stars = 70;
            for (int i = 0; i < stars; i++)
            {
                float size = 1f + (float)rng.NextDouble() * 1.5f;
                float x = (float)rng.NextDouble() * Mathf.Max(1f, width - size - 2f);
                float y = (float)rng.NextDouble() * Mathf.Max(1f, height - size - 2f);

                var star = new VisualElement();
                star.style.position = Position.Absolute;
                star.style.left = x;
                star.style.top = y;
                star.style.width = size;
                star.style.height = size;
                star.style.borderTopLeftRadius = size * 0.5f;
                star.style.borderTopRightRadius = size * 0.5f;
                star.style.borderBottomLeftRadius = size * 0.5f;
                star.style.borderBottomRightRadius = size * 0.5f;
                star.style.backgroundColor = new Color(0.46f, 0.60f, 0.80f, 0.22f + (float)rng.NextDouble() * 0.25f);
                parent.Add(star);
            }
        }

        private static void AddSectorLane(VisualElement parent, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 2f) return;

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var line = new VisualElement();
            line.style.position = Position.Absolute;
            line.style.left = a.x;
            line.style.top = a.y;
            line.style.width = len;
            line.style.height = 1;
            line.style.backgroundColor = new Color(0.30f, 0.48f, 0.68f, 0.34f);
            line.style.transformOrigin = new TransformOrigin(0, 0, 0);
            line.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            parent.Add(line);
        }

        private void AddSectorSystemCluster(VisualElement parent, float width, float height, int seed,
            SectorData sector, int systemCount, Label hoverLabel, string hoverLabelDefault)
        {
            // Generate actual system data for this sector so each dot maps to a real system.
            bool isHome = _station?.solarSystem != null &&
                          Mathf.Approximately(sector.coordinates.x, GalaxyGenerator.HomeX) &&
                          Mathf.Approximately(sector.coordinates.y, GalaxyGenerator.HomeY);
            var systems = SolarSystemGenerator.GenerateSectorSystems(sector, isHome, _station?.solarSystem);

            var rng = new System.Random(seed ^ 0x2f6e2b1);
            int count = Mathf.Min(systemCount > 0 ? systemCount : rng.Next(12, 21), systems.Count);
            if (count > systems.Count) count = systems.Count;

            const float edgePad = 6f;
            var placed = new List<(Vector2 center, float radius)>();
            for (int i = 0; i < count; i++)
            {
                float size = 3.2f + (float)rng.NextDouble() * 2.4f;
                float x = edgePad;
                float y = edgePad;
                float radius = size * 0.5f;

                bool found = false;
                float rangeW = Mathf.Max(1f, width - 2f * edgePad - size);
                float rangeH = Mathf.Max(1f, height - 2f * edgePad - size);
                for (int tries = 0; tries < 28; tries++)
                {
                    float tx = edgePad + (float)rng.NextDouble() * rangeW;
                    float ty = edgePad + (float)rng.NextDouble() * rangeH;
                    Vector2 c = new Vector2(tx + radius, ty + radius);

                    bool overlaps = false;
                    for (int p = 0; p < placed.Count; p++)
                    {
                        float minDist = radius + placed[p].radius + 2.4f;
                        if ((c - placed[p].center).sqrMagnitude < minDist * minDist)
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                    {
                        x = tx;
                        y = ty;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    x = edgePad + (float)rng.NextDouble() * rangeW;
                    y = edgePad + (float)rng.NextDouble() * rangeH;
                }
                placed.Add((new Vector2(x + radius, y + radius), radius));

                var ring = new VisualElement();
                ring.style.position = Position.Absolute;
                ring.style.left = x - 2f;
                ring.style.top = y - 2f;
                ring.style.width = size + 4f;
                ring.style.height = size + 4f;
                ring.style.borderTopLeftRadius = (size + 4f) * 0.5f;
                ring.style.borderTopRightRadius = (size + 4f) * 0.5f;
                ring.style.borderBottomLeftRadius = (size + 4f) * 0.5f;
                ring.style.borderBottomRightRadius = (size + 4f) * 0.5f;
                ring.style.borderTopWidth = 1;
                ring.style.borderRightWidth = 1;
                ring.style.borderBottomWidth = 1;
                ring.style.borderLeftWidth = 1;
                ring.style.borderTopColor = Color.white;
                ring.style.borderRightColor = Color.white;
                ring.style.borderBottomColor = Color.white;
                ring.style.borderLeftColor = Color.white;
                ring.style.display = DisplayStyle.None;
                ring.pickingMode = PickingMode.Ignore;
                parent.Add(ring);

                // Tint the home system dot green so the player can identify it.
                bool isDotHome = isHome && i == 0;
                var dot = new VisualElement();
                dot.style.position = Position.Absolute;
                dot.style.left = x;
                dot.style.top = y;
                dot.style.width = size;
                dot.style.height = size;
                dot.style.borderTopLeftRadius = size * 0.5f;
                dot.style.borderTopRightRadius = size * 0.5f;
                dot.style.borderBottomLeftRadius = size * 0.5f;
                dot.style.borderBottomRightRadius = size * 0.5f;
                dot.style.backgroundColor = isDotHome
                    ? new Color(0.25f, 1.00f, 0.50f, 1f)
                    : sector.discoveryState == SectorDiscoveryState.Visited
                        ? new Color(0.62f, 0.86f, 1.00f, 0.95f)
                        : new Color(0.46f, 0.62f, 0.80f, 0.78f);

                int capturedIndex = i;
                var capturedNeighbor = systems[i];
                bool capturedIsHome = isDotHome;
                dot.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    ring.style.display = DisplayStyle.Flex;
                    dot.style.scale = new Scale(new Vector3(1.65f, 1.65f, 1f));
                    if (hoverLabel != null)
                    {
                        float dLY = capturedNeighbor.positionLY.magnitude;
                        string distText = dLY > 0.01f ? $"  ({dLY:F1} LY)" : "";
                        hoverLabel.text = capturedNeighbor.systemName + distText;
                    }
                });
                dot.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    ring.style.display = DisplayStyle.None;
                    dot.style.scale = new Scale(new Vector3(1f, 1f, 1f));
                    if (hoverLabel != null)
                        hoverLabel.text = hoverLabelDefault;
                });
                dot.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_routePlotterActive)
                    {
                        AddRouteWaypoint(new RouteWaypoint
                        {
                            name = capturedNeighbor.systemName,
                            positionLY = capturedNeighbor.positionLY,
                            isSystem = true,
                        });
                        return;
                    }
                    if (capturedIsHome && _station?.solarSystem != null)
                    {
                        ViewSystem(_station.solarSystem, true);
                    }
                    else
                    {
                        var fullSys = SolarSystemGenerator.Generate("sys", capturedNeighbor.seed);
                        ViewSystem(fullSys, false);
                    }
                });

                parent.Add(dot);
            }
        }

        private static Vector2 ToLocalPoint(VisualElement relativeTo, Vector2 panelPosition)
        {
            if (relativeTo == null) return panelPosition;
            return new Vector2(panelPosition.x - relativeTo.worldBound.x, panelPosition.y - relativeTo.worldBound.y);
        }

        private static void StyleHoverTooltip(Label tip)
        {
            tip.style.position = Position.Absolute;
            tip.style.paddingLeft = 8;
            tip.style.paddingRight = 8;
            tip.style.paddingTop = 4;
            tip.style.paddingBottom = 4;
            tip.style.fontSize = 10;
            tip.style.backgroundColor = new Color(0.06f, 0.10f, 0.18f, 0.97f);
            tip.style.color = new Color(0.88f, 0.93f, 1f, 1f);
            tip.style.borderTopWidth = 1;
            tip.style.borderRightWidth = 1;
            tip.style.borderBottomWidth = 1;
            tip.style.borderLeftWidth = 1;
            tip.style.borderTopColor = new Color(0.32f, 0.50f, 0.72f, 0.95f);
            tip.style.borderRightColor = new Color(0.32f, 0.50f, 0.72f, 0.95f);
            tip.style.borderBottomColor = new Color(0.12f, 0.20f, 0.32f, 0.95f);
            tip.style.borderLeftColor = new Color(0.12f, 0.20f, 0.32f, 0.95f);
            tip.style.display = DisplayStyle.None;
            tip.pickingMode = PickingMode.Ignore;
        }

        private void EnsureSystemHoverTooltip()
        {
            if (_orbitCanvas == null || _systemHoverTooltip != null) return;
            _systemHoverTooltip = new Label();
            StyleHoverTooltip(_systemHoverTooltip);
            _orbitCanvas.Add(_systemHoverTooltip);
        }

        private void ShowSystemHoverTooltip(string text, Vector2 localPos)
        {
            if (_systemHoverTooltip == null) return;
            _systemHoverTooltip.text = text;
            PositionSystemHoverTooltip(localPos);
            _systemHoverTooltip.style.display = DisplayStyle.Flex;
            _systemHoverTooltip.BringToFront();
        }

        private void PositionSystemHoverTooltip(Vector2 localPos)
        {
            if (_systemHoverTooltip == null || _orbitCanvas == null) return;
            float x = Mathf.Clamp(localPos.x, 4f, Mathf.Max(4f, _orbitCanvas.resolvedStyle.width - 220f));
            float y = Mathf.Clamp(localPos.y, 4f, Mathf.Max(4f, _orbitCanvas.resolvedStyle.height - 34f));
            _systemHoverTooltip.style.left = x;
            _systemHoverTooltip.style.top = y;
        }

        private void HideSystemHoverTooltip()
        {
            if (_systemHoverTooltip == null) return;
            _systemHoverTooltip.style.display = DisplayStyle.None;
        }

        private void EnsureSectorHoverTooltip()
        {
            if (_sectorChartViewport == null || _sectorHoverTooltip != null) return;
            _sectorHoverTooltip = new Label();
            StyleHoverTooltip(_sectorHoverTooltip);
            _sectorChartViewport.Add(_sectorHoverTooltip);
        }

        private void ShowSectorHoverTooltip(string text, Vector2 localPos)
        {
            if (_sectorHoverTooltip == null) return;
            _sectorHoverTooltip.text = text;
            PositionSectorHoverTooltip(localPos);
            _sectorHoverTooltip.style.display = DisplayStyle.Flex;
            _sectorHoverTooltip.BringToFront();
        }

        private void PositionSectorHoverTooltip(Vector2 localPos)
        {
            if (_sectorHoverTooltip == null || _sectorChartViewport == null) return;
            float x = Mathf.Clamp(localPos.x, 4f, Mathf.Max(4f, _sectorChartViewport.resolvedStyle.width - 220f));
            float y = Mathf.Clamp(localPos.y, 4f, Mathf.Max(4f, _sectorChartViewport.resolvedStyle.height - 34f));
            _sectorHoverTooltip.style.left = x;
            _sectorHoverTooltip.style.top = y;
        }

        private void HideSectorHoverTooltip()
        {
            if (_sectorHoverTooltip == null) return;
            _sectorHoverTooltip.style.display = DisplayStyle.None;
        }

        private static string GenerateSectorSystemName(SectorData sector, int index)
        {
            string code = sector.ShortCodeAndCoord();
            return $"{code} System {index + 1:D2}";
        }

        private Color ResolveSectorBorderColor(SectorData sector)
        {
            Color neutral = sector.discoveryState == SectorDiscoveryState.Visited
                ? new Color(0.25f, 0.80f, 0.45f, 0.95f)
                : ColDivider;

            if (sector == null || sector.factionIds == null || sector.factionIds.Count == 0)
                return neutral;

            // Full control tint requires >1 controlled systems unless the sector only has one.
            int totalSystems = Mathf.Max(1, sector.systemCount);
            int controlledEstimate = Mathf.Max(1, Mathf.RoundToInt((float)totalSystems / Mathf.Max(1, sector.factionIds.Count)));
            bool qualifies = totalSystems == 1 || controlledEstimate > 1;
            if (!qualifies)
                return neutral;

            string factionId = sector.factionIds[0];
            if (string.IsNullOrWhiteSpace(factionId))
                return neutral;

            // Use player faction brand color when the id indicates player control.
            if (_station != null && factionId.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
                && ColorUtility.TryParseHtmlString(_station.playerFactionColor, out Color playerCol))
                return new Color(playerCol.r, playerCol.g, playerCol.b, 0.95f);

            int hash = StableHash(factionId);
            float hue = (hash % 360) / 360f;
            Color tint = Color.HSVToRGB(hue, 0.52f, 0.86f);
            return new Color(tint.r, tint.g, tint.b, 0.95f);
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
                return (int)(hash & int.MaxValue);
            }
        }

        private void ShowPoiDetail(PointOfInterest poi)
        {
            if (poi == null)
                return;

            _detailSidebar.Clear();
            _detailSidebar.style.display = DisplayStyle.Flex;

            var headerRow = MakeDetailHeader(poi.displayName ?? poi.poiType);
            _detailSidebar.Add(headerRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _detailSidebar.Add(scroll);

            var body = new VisualElement();
            body.AddToClassList(DetailBodyClass);
            body.style.paddingTop = 12;
            body.style.paddingBottom = 12;
            body.style.paddingLeft = 12;
            body.style.paddingRight = 12;
            scroll.Add(body);

            AddDetailRow(body, "Type", poi.poiType ?? "Unknown");
            AddDetailRow(body, "State", poi.visited ? "Visited" : "Detected");
            AddDetailRow(body, "Coordinates", $"{poi.posX:F1}, {poi.posY:F1}");

            // Distance from star and from station.
            float poiDist = Mathf.Sqrt(poi.posX * poi.posX + poi.posY * poi.posY);
            if (poiDist > 0.01f)
                AddDetailRow(body, "Dist. from Star", $"{poiDist:F2} AU");

            if (_viewedSystemIsHome)
            {
                var sys = _viewedSystem ?? _station?.solarSystem;
                if (sys != null && sys.stationOrbitIndex >= 0 && sys.stationOrbitIndex < sys.bodies.Count)
                {
                    float stR = sys.bodies[sys.stationOrbitIndex].orbitalRadius;
                    float minD = Mathf.Abs(poiDist - stR);
                    float maxD = poiDist + stR;
                    AddDetailRow(body, "Dist. to Station", $"{minD:F2}–{maxD:F2} AU");
                }
            }

            if (poi.resourceYield != null && poi.resourceYield.Count > 0)
            {
                body.Add(MakeDivider());
                var hdr = new Label("RESOURCE YIELD");
                hdr.style.color = ColTextMid;
                hdr.style.fontSize = 10;
                hdr.style.marginBottom = 4;
                body.Add(hdr);

                foreach (var kv in poi.resourceYield)
                {
                    var line = new Label($"• {kv.Key}: {kv.Value}");
                    line.style.color = ColTextBright;
                    line.style.fontSize = 11;
                    body.Add(line);
                }
            }
        }

        private static Color ArchetypeTint(SectorArchetype archetype)
        {
            return archetype switch
            {
                SectorArchetype.Confluence       => new Color(0.165f, 0.290f, 0.416f, 0.18f), // #2a4a6a
                SectorArchetype.MineralBelt      => new Color(0.290f, 0.227f, 0.102f, 0.18f), // #4a3a1a
                SectorArchetype.SingularityReach  => new Color(0.102f, 0.039f, 0.165f, 0.18f), // #1a0a2a
                SectorArchetype.RemnantsZone     => new Color(0.102f, 0.102f, 0.165f, 0.18f), // #1a1a2a
                SectorArchetype.StormBelt        => new Color(0.102f, 0.165f, 0.102f, 0.18f), // #1a2a1a
                SectorArchetype.NebulaField      => new Color(0.165f, 0.102f, 0.227f, 0.18f), // #2a1a3a
                SectorArchetype.ContestedCore    => new Color(0.227f, 0.102f, 0.102f, 0.18f), // #3a1a1a
                SectorArchetype.Cradle           => new Color(0.102f, 0.227f, 0.165f, 0.18f), // #1a3a2a
                SectorArchetype.FrontierScatter  => new Color(0.165f, 0.165f, 0.102f, 0.18f), // #2a2a1a
                _                                => new Color(0f, 0f, 0f, 0f),                // VoidFringe / default = no tint
            };
        }

        private static string ArchetypeIcon(SectorArchetype archetype)
        {
            return archetype switch
            {
                SectorArchetype.Confluence       => "⊕",
                SectorArchetype.MineralBelt      => "◈",
                SectorArchetype.SingularityReach  => "◉",
                SectorArchetype.RemnantsZone     => "☠",
                SectorArchetype.StormBelt        => "⚡",
                SectorArchetype.NebulaField      => "☁",
                SectorArchetype.ContestedCore    => "⚔",
                SectorArchetype.Cradle           => "☀",
                SectorArchetype.FrontierScatter  => "◇",
                SectorArchetype.VoidFringe       => "·",
                _                                => "·",
            };
        }

        private VisualElement BuildSectorBox(SectorData sector)
        {
            bool isUncharted = sector.discoveryState == SectorDiscoveryState.Uncharted;

            var box = new VisualElement();
            box.AddToClassList(SectorBoxClass);
            box.style.width            = 140;
            box.style.height           = 100;
            box.style.marginTop        = 4;
            box.style.marginBottom     = 4;
            box.style.marginLeft       = 4;
            box.style.marginRight      = 4;
            box.style.BorderRadius(4);
            box.style.backgroundColor  = SectorStateColour(sector.discoveryState);
            box.style.borderTopWidth   = 1;
            box.style.borderRightWidth = 1;
            box.style.borderBottomWidth = 1;
            box.style.borderLeftWidth  = 1;

            Color borderCol = sector.discoveryState switch
            {
                SectorDiscoveryState.Visited   => new Color(0.12f, 0.36f, 0.62f, 1f),
                SectorDiscoveryState.Detected  => new Color(0.20f, 0.26f, 0.38f, 1f),
                _                             => new Color(0.18f, 0.20f, 0.25f, 0.5f),
            };
            box.style.borderTopColor    = borderCol;
            box.style.borderRightColor  = borderCol;
            box.style.borderBottomColor = borderCol;
            box.style.borderLeftColor   = borderCol;

            box.style.paddingTop       = 6;
            box.style.paddingBottom    = 6;
            box.style.paddingLeft      = 6;
            box.style.paddingRight     = 6;
            box.style.flexDirection    = FlexDirection.Column;
            box.style.justifyContent   = Justify.SpaceBetween;
            box.style.overflow         = Overflow.Hidden;

            if (isUncharted)
            {
                // Uncharted: only show fog label
                box.AddToClassList(SectorBoxFogClass);
                var fogLabel = new Label("UNCHARTED");
                fogLabel.style.color          = new Color(0.28f, 0.31f, 0.37f, 0.8f);
                fogLabel.style.fontSize       = 9;
                fogLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                fogLabel.style.flexGrow       = 1;
                box.Add(fogLabel);
            }
            else
            {
                // Detected / Visited: show designation and optional proper name
                var codeLabel = new Label(sector.ShortCodeAndCoord());
                codeLabel.AddToClassList(SectorBoxCodeClass);
                codeLabel.style.fontSize = 9;
                codeLabel.style.color    = sector.discoveryState == SectorDiscoveryState.Visited
                    ? ColAccentText
                    : ColTextMid;
                box.Add(codeLabel);

                if (!string.IsNullOrEmpty(sector.properName))
                {
                    var nameLabel = new Label($"\"{sector.properName}\"");
                    nameLabel.AddToClassList(SectorBoxNameClass);
                    nameLabel.style.fontSize = 11;
                    nameLabel.style.color    = sector.discoveryState == SectorDiscoveryState.Visited
                        ? ColTextBright
                        : new Color(0.60f, 0.72f, 0.85f, 1f);
                    nameLabel.style.unityTextAlign   = TextAnchor.UpperLeft;
                    nameLabel.style.whiteSpace = WhiteSpace.Normal;
                    box.Add(nameLabel);
                }

                // Discovery state badge
                var stateBadge = new Label(sector.discoveryState == SectorDiscoveryState.Visited ? "VISITED" : "DETECTED");
                stateBadge.style.fontSize     = 8;
                stateBadge.style.color        = sector.discoveryState == SectorDiscoveryState.Visited
                    ? new Color(0.25f, 0.80f, 0.45f, 1f)
                    : new Color(0.45f, 0.60f, 0.78f, 1f);
                stateBadge.style.unityTextAlign = TextAnchor.LowerLeft;
                box.Add(stateBadge);
            }

            // Only Detected/Visited are clickable for detail
            if (!isUncharted)
            {
                var capturedSector = sector;
                box.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    box.style.borderTopColor   = new Color(0.39f, 0.75f, 1.00f, 0.6f);
                    box.style.borderRightColor = new Color(0.39f, 0.75f, 1.00f, 0.6f);
                    box.style.borderBottomColor = new Color(0.39f, 0.75f, 1.00f, 0.6f);
                    box.style.borderLeftColor  = new Color(0.39f, 0.75f, 1.00f, 0.6f);
                });
                box.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    box.style.borderTopColor   = borderCol;
                    box.style.borderRightColor = borderCol;
                    box.style.borderBottomColor = borderCol;
                    box.style.borderLeftColor  = borderCol;
                });
                box.RegisterCallback<ClickEvent>(_ => ShowSectorDetail(capturedSector));
            }

            return box;
        }

        // ── Body detail sidebar ───────────────────────────────────────────────

        private void ShowBodyDetail(SolarBody body, bool hasStation)
        {
            _detailSidebar.Clear();
            _detailSidebar.style.display = DisplayStyle.Flex;

            string displayName = body.name ?? BodyTypeLabel(body.bodyType);

            // Header row with close button
            var headerRow = MakeDetailHeader(displayName);
            _detailSidebar.Add(headerRow);

            // Rename row — text field + confirm, only for the home system's bodies.
            if (_viewedSystemIsHome)
            {
                var renameRow = new VisualElement();
                renameRow.style.flexDirection = FlexDirection.Row;
                renameRow.style.alignItems = Align.Center;
                renameRow.style.paddingLeft = 10;
                renameRow.style.paddingRight = 10;
                renameRow.style.paddingTop = 6;
                renameRow.style.paddingBottom = 6;
                renameRow.style.borderBottomWidth = 1;
                renameRow.style.borderBottomColor = ColDivider;

                var renameField = new TextField();
                renameField.value = displayName;
                renameField.style.flexGrow = 1;
                renameField.style.fontSize = 11;
                renameRow.Add(renameField);

                var renameBtn = new Button(() =>
                {
                    string newName = renameField.value?.Trim();
                    if (!string.IsNullOrEmpty(newName) && newName != body.name)
                    {
                        body.name = newName;
                        // Refresh the header label
                        var titleEl = headerRow.Q<Label>(className: DetailTitleClass);
                        if (titleEl != null) titleEl.text = newName;
                    }
                }) { text = "Rename" };
                renameBtn.style.fontSize = 10;
                renameBtn.style.marginLeft = 6;
                renameBtn.style.paddingLeft = 8;
                renameBtn.style.paddingRight = 8;
                renameBtn.style.paddingTop = 3;
                renameBtn.style.paddingBottom = 3;
                renameBtn.style.color = ColAccentText;
                renameBtn.style.backgroundColor = new Color(0.10f, 0.16f, 0.26f, 0.90f);
                renameBtn.style.borderTopWidth = 1;
                renameBtn.style.borderRightWidth = 1;
                renameBtn.style.borderBottomWidth = 1;
                renameBtn.style.borderLeftWidth = 1;
                renameBtn.style.borderTopColor = ColDivider;
                renameBtn.style.borderRightColor = ColDivider;
                renameBtn.style.borderBottomColor = ColDivider;
                renameBtn.style.borderLeftColor = ColDivider;
                renameRow.Add(renameBtn);

                _detailSidebar.Add(renameRow);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _detailSidebar.Add(scroll);

            var body2 = new VisualElement();
            body2.AddToClassList(DetailBodyClass);
            body2.style.paddingTop    = 12;
            body2.style.paddingBottom = 12;
            body2.style.paddingLeft   = 12;
            body2.style.paddingRight  = 12;
            scroll.Add(body2);

            // Class row
            AddDetailRow(body2, "Type",  BodyTypeLabel(body.bodyType));
            AddDetailRow(body2, "Class", PlanetClassLabel(body.planetClass));

            if (body.orbitalRadius > 0f)
            {
                AddDetailRow(body2, "Orbital Radius",  $"{body.orbitalRadius:F2} AU");
                float minutes = body.orbitalPeriod * MinutesPerTick;
                float hours = minutes / 60f;
                float days = hours / 24f;
                AddDetailRow(body2, "Orbital Period", $"{body.orbitalPeriod:F0} ticks ({days:F1} d / {hours:F1} h)");
            }

            if (body.moons.Count > 0)
                AddDetailRow(body2, "Moons", body.moons.Count.ToString());

            // Distance from station body (if in home system and station exists).
            if (_viewedSystemIsHome)
            {
                var sys = _viewedSystem ?? _station?.solarSystem;
                if (sys != null && sys.stationOrbitIndex >= 0 && sys.stationOrbitIndex < sys.bodies.Count)
                {
                    var stBody = sys.bodies[sys.stationOrbitIndex];
                    if (stBody != body && body.orbitalRadius > 0f && stBody.orbitalRadius > 0f)
                    {
                        float minDist = Mathf.Abs(body.orbitalRadius - stBody.orbitalRadius);
                        float maxDist = body.orbitalRadius + stBody.orbitalRadius;
                        AddDetailRow(body2, "Dist. to Station", $"{minDist:F2}–{maxDist:F2} AU");
                    }
                }
            }

            if (hasStation)
            {
                var stationNote = new Label("⬡ Your station orbits here.");
                stationNote.style.color      = new Color(0.25f, 1.00f, 0.50f, 1f);
                stationNote.style.marginTop  = 10;
                stationNote.style.fontSize   = 11;
                body2.Add(stationNote);
            }

            if (body.tags.Count > 0)
            {
                body2.Add(MakeDivider());
                var tagsHdr = new Label("RESOURCE PROFILE");
                tagsHdr.style.color     = ColTextMid;
                tagsHdr.style.fontSize  = 10;
                tagsHdr.style.marginTop = 8;
                body2.Add(tagsHdr);

                foreach (var tag in body.tags)
                {
                    var tagLabel = new Label($"• {FormatTag(tag)}");
                    tagLabel.style.color    = ColTextBright;
                    tagLabel.style.fontSize = 11;
                    body2.Add(tagLabel);
                }
            }
        }

        // ── Sector detail sidebar ─────────────────────────────────────────────

        private void ShowSectorDetail(SectorData sector)
        {
            // Remember the selected sector so Refresh() can re-populate the sidebar
            // if grid data changes (e.g. after EP income or a sector unlock).
            _selectedSector = sector;

            _detailSidebar.Clear();
            _detailSidebar.style.display = DisplayStyle.Flex;

            // Header
            var headerRow = MakeDetailHeader(string.IsNullOrEmpty(sector.properName)
                ? sector.ShortCodeAndCoord()
                : $"\"{sector.properName}\"");
            _detailSidebar.Add(headerRow);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _detailSidebar.Add(scroll);

            var body = new VisualElement();
            body.AddToClassList(DetailBodyClass);
            body.style.paddingTop    = 12;
            body.style.paddingBottom = 12;
            body.style.paddingLeft   = 12;
            body.style.paddingRight  = 12;
            scroll.Add(body);

            // Full designation (naming convention: [prefix]-[codes] XX.YY "name")
            var designationLabel = new Label(sector.FullDesignation());
            designationLabel.style.color     = ColAccentText;
            designationLabel.style.fontSize  = 11;
            designationLabel.style.marginBottom = 10;
            designationLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(designationLabel);

            // Discovery state
            AddDetailRow(body, "State", sector.discoveryState.ToString());

            body.Add(MakeDivider());

            // Resource profile (Detected or Visited)
            if (sector.discoveryState != SectorDiscoveryState.Uncharted)
            {
                var resourceHeader = new Label("RESOURCE PROFILE");
                resourceHeader.style.color    = ColTextMid;
                resourceHeader.style.fontSize = 10;
                resourceHeader.style.marginTop  = 8;
                resourceHeader.style.marginBottom = 4;
                body.Add(resourceHeader);

                var topResources = GetTopResources(sector);
                if (topResources.Count > 0)
                {
                    foreach (var res in topResources)
                    {
                        var resLabel = new Label($"• {res}");
                        resLabel.style.color   = ColTextBright;
                        resLabel.style.fontSize = 11;
                        body.Add(resLabel);
                    }
                }
                else
                {
                    var noneLabel = new Label("No significant resources detected.");
                    noneLabel.style.color   = ColTextMid;
                    noneLabel.style.fontSize = 11;
                    body.Add(noneLabel);
                }
            }

            // Adjacent expansion now lives exclusively on the sector grid '+' nodes.
        }

        /// <summary>
        /// Tries all four cardinal neighbours of the given grid cell and returns the
        /// <see cref="SectorData"/> of the first one successfully unlocked, or
        /// <c>null</c> if no adjacent cell could be unlocked.
        /// </summary>
        private SectorData TryUnlockAdjacentAndGetSector(int col, int row)
        {
            int[] dc = {  1, -1,  0,  0 };
            int[] dr = {  0,  0,  1, -1 };
            for (int i = 0; i < 4; i++)
            {
                int nc = col + dc[i];
                int nr = row + dr[i];
                if (!_map.TryUnlockSector(_station, nc, nr)) continue;

                // Find the newly generated sector by its grid coordinates.
                float gx = GalaxyGenerator.HomeX + nc * MapSystem.GalUnitPerCell;
                float gy = GalaxyGenerator.HomeY + nr * MapSystem.GalUnitPerCell;
                foreach (var sec in _station.sectors.Values)
                    if (Mathf.Approximately(sec.coordinates.x, gx) &&
                        Mathf.Approximately(sec.coordinates.y, gy))
                        return sec;
            }
            return null;
        }

        private SectorData TryUnlockSectorAtAndGetSector(int col, int row)
        {
            if (_map == null || _station == null) return null;
            if (!_map.TryUnlockSector(_station, col, row)) return null;

            float gx = GalaxyGenerator.HomeX + col * MapSystem.GalUnitPerCell;
            float gy = GalaxyGenerator.HomeY + row * MapSystem.GalUnitPerCell;
            foreach (var sec in _station.sectors.Values)
                if (Mathf.Approximately(sec.coordinates.x, gx) &&
                    Mathf.Approximately(sec.coordinates.y, gy))
                    return sec;
            return null;
        }

        // ── Route Plotter ─────────────────────────────────────────────────────

        private void ToggleRoutePlotter()
        {
            _routePlotterActive = !_routePlotterActive;

            if (_routePlotterActive)
            {
                _routeWaypoints.Clear();
                if (_routePlotterBtn != null)
                {
                    _routePlotterBtn.style.backgroundColor = new Color(0.12f, 0.36f, 0.62f, 1f);
                    _routePlotterBtn.style.color = new Color(0.85f, 0.95f, 1.00f, 1f);
                }
                BuildRouteOverlay();
            }
            else
            {
                if (_routePlotterBtn != null)
                {
                    StyleActionButton(_routePlotterBtn);
                    _routePlotterBtn.style.marginRight = 8;
                }
                HideRouteOverlay();
            }
        }

        private void BuildRouteOverlay()
        {
            HideRouteOverlay();

            // Attach the overlay to whichever canvas is currently active.
            VisualElement parent = _currentView == MapLayer.System ? _orbitCanvas : _sectorChartViewport;
            if (parent == null) return;

            _routeOverlay = new VisualElement();
            _routeOverlay.style.position = Position.Absolute;
            _routeOverlay.style.right = 10;
            _routeOverlay.style.top = 10;
            _routeOverlay.style.width = 240;
            _routeOverlay.style.maxHeight = Length.Percent(70);
            _routeOverlay.style.backgroundColor = new Color(0.07f, 0.10f, 0.16f, 0.92f);
            _routeOverlay.style.borderTopWidth = 1;
            _routeOverlay.style.borderRightWidth = 1;
            _routeOverlay.style.borderBottomWidth = 1;
            _routeOverlay.style.borderLeftWidth = 1;
            _routeOverlay.style.borderTopColor = ColDivider;
            _routeOverlay.style.borderRightColor = ColDivider;
            _routeOverlay.style.borderBottomColor = ColDivider;
            _routeOverlay.style.borderLeftColor = ColDivider;
            _routeOverlay.pickingMode = PickingMode.Position;
            parent.Add(_routeOverlay);

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.paddingTop = 6;
            header.style.paddingBottom = 6;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = ColDivider;
            header.style.backgroundColor = ColSectionHdr;

            var title = new Label("ROUTE PLOTTER");
            title.style.color = ColAccentText;
            title.style.fontSize = Fs(11);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 0.5f;
            title.style.flexGrow = 1;
            header.Add(title);

            var clearBtn = new Button(() =>
            {
                _routeWaypoints.Clear();
                UpdateRouteOverlay();
            }) { text = "Clear" };
            clearBtn.style.fontSize = Fs(9);
            clearBtn.style.color = ColTextMid;
            clearBtn.style.backgroundColor = StyleKeyword.Null;
            clearBtn.style.borderTopWidth = 0;
            clearBtn.style.borderRightWidth = 0;
            clearBtn.style.borderBottomWidth = 0;
            clearBtn.style.borderLeftWidth = 0;
            clearBtn.style.paddingLeft = 6;
            clearBtn.style.paddingRight = 6;
            clearBtn.style.paddingTop = 2;
            clearBtn.style.paddingBottom = 2;
            header.Add(clearBtn);

            var closeBtn = new Button(() => ToggleRoutePlotter()) { text = "✕" };
            closeBtn.style.width = 20;
            closeBtn.style.height = 20;
            closeBtn.style.fontSize = Fs(10);
            closeBtn.style.color = ColTextMid;
            closeBtn.style.backgroundColor = StyleKeyword.Null;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.paddingLeft = 0;
            closeBtn.style.paddingRight = 0;
            closeBtn.style.paddingTop = 0;
            closeBtn.style.paddingBottom = 0;
            closeBtn.style.marginLeft = 4;
            header.Add(closeBtn);

            _routeOverlay.Add(header);

            // Instruction label (shown when empty)
            var instruction = new Label(_currentView == MapLayer.System
                ? "Click bodies to add waypoints."
                : "Click system dots to add waypoints.");
            instruction.name = "RouteInstruction";
            instruction.style.color = ColTextMid;
            instruction.style.fontSize = Fs(9);
            instruction.style.paddingLeft = 8;
            instruction.style.paddingTop = 8;
            instruction.style.paddingBottom = 8;
            _routeOverlay.Add(instruction);

            // Scrollable waypoint list
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "RouteList";
            scroll.style.flexGrow = 1;
            scroll.style.maxHeight = 300;
            scroll.style.paddingLeft = 4;
            scroll.style.paddingRight = 4;
            scroll.style.display = DisplayStyle.None;
            _routeOverlay.Add(scroll);

            // Total distance footer
            var footer = new VisualElement();
            footer.name = "RouteFooter";
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.paddingLeft = 8;
            footer.style.paddingRight = 8;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 6;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = ColDivider;
            footer.style.display = DisplayStyle.None;

            var totalLabel = new Label("TOTAL");
            totalLabel.style.color = ColTextMid;
            totalLabel.style.fontSize = Fs(9);
            totalLabel.style.flexGrow = 1;
            totalLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            footer.Add(totalLabel);

            var totalValue = new Label("0.00 AU");
            totalValue.name = "RouteTotalValue";
            totalValue.style.color = ColAccentText;
            totalValue.style.fontSize = Fs(11);
            totalValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            footer.Add(totalValue);

            _routeOverlay.Add(footer);
        }

        private void HideRouteOverlay()
        {
            if (_routeOverlay != null)
            {
                _routeOverlay.RemoveFromHierarchy();
                _routeOverlay = null;
            }
        }

        private void AddRouteWaypoint(RouteWaypoint wp)
        {
            _routeWaypoints.Add(wp);
            UpdateRouteOverlay();
        }

        private void UpdateRouteOverlay()
        {
            if (_routeOverlay == null) return;

            var instruction = _routeOverlay.Q("RouteInstruction");
            var scroll = _routeOverlay.Q("RouteList") as ScrollView;
            var footer = _routeOverlay.Q("RouteFooter");
            var totalValue = _routeOverlay.Q<Label>("RouteTotalValue");

            if (scroll == null) return;

            bool hasWaypoints = _routeWaypoints.Count > 0;
            if (instruction != null) instruction.style.display = hasWaypoints ? DisplayStyle.None : DisplayStyle.Flex;
            scroll.style.display = hasWaypoints ? DisplayStyle.Flex : DisplayStyle.None;
            if (footer != null) footer.style.display = _routeWaypoints.Count >= 2 ? DisplayStyle.Flex : DisplayStyle.None;

            scroll.Clear();

            float totalDist = 0f;
            string unit = _routeWaypoints.Count > 0 && _routeWaypoints[0].isSystem ? "LY" : "AU";

            for (int i = 0; i < _routeWaypoints.Count; i++)
            {
                var wp = _routeWaypoints[i];
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 4;
                row.style.paddingRight = 4;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                row.style.marginBottom = 1;
                row.style.backgroundColor = i % 2 == 0 ? ColBodyRow : new Color(0.08f, 0.12f, 0.19f, 0.90f);

                // Waypoint number
                var numLabel = new Label($"{i + 1}.");
                numLabel.style.color = ColAccentText;
                numLabel.style.fontSize = Fs(9);
                numLabel.style.width = 18;
                numLabel.style.minWidth = 18;
                row.Add(numLabel);

                // Waypoint name
                var nameLabel = new Label(wp.name);
                nameLabel.style.color = ColTextBright;
                nameLabel.style.fontSize = Fs(10);
                nameLabel.style.flexGrow = 1;
                nameLabel.style.overflow = Overflow.Hidden;
                row.Add(nameLabel);

                // Leg distance (from previous)
                if (i > 0)
                {
                    float legDist = CalculateLegDistance(_routeWaypoints[i - 1], wp);
                    totalDist += legDist;
                    var distLabel = new Label($"{legDist:F2} {unit}");
                    distLabel.style.color = ColTextMid;
                    distLabel.style.fontSize = Fs(9);
                    row.Add(distLabel);
                }

                // Remove button
                int capturedIndex = i;
                var rmBtn = new Button(() =>
                {
                    if (capturedIndex < _routeWaypoints.Count)
                    {
                        _routeWaypoints.RemoveAt(capturedIndex);
                        UpdateRouteOverlay();
                    }
                }) { text = "✕" };
                rmBtn.style.width = 16;
                rmBtn.style.height = 16;
                rmBtn.style.fontSize = Fs(8);
                rmBtn.style.color = ColTextMid;
                rmBtn.style.backgroundColor = StyleKeyword.Null;
                rmBtn.style.borderTopWidth = 0;
                rmBtn.style.borderRightWidth = 0;
                rmBtn.style.borderBottomWidth = 0;
                rmBtn.style.borderLeftWidth = 0;
                rmBtn.style.paddingLeft = 0;
                rmBtn.style.paddingRight = 0;
                rmBtn.style.paddingTop = 0;
                rmBtn.style.paddingBottom = 0;
                rmBtn.style.marginLeft = 4;
                row.Add(rmBtn);

                scroll.Add(row);
            }

            if (totalValue != null)
                totalValue.text = $"{totalDist:F2} {unit}";
        }

        private static float CalculateLegDistance(RouteWaypoint a, RouteWaypoint b)
        {
            if (a.isSystem && b.isSystem)
            {
                // Inter-system: straight-line distance in LY.
                float dx = b.positionLY.x - a.positionLY.x;
                float dy = b.positionLY.y - a.positionLY.y;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }

            // Intra-system: distance between two bodies based on orbital position.
            float ax = a.orbitalRadius * Mathf.Cos(a.angle);
            float ay = a.orbitalRadius * Mathf.Sin(a.angle);
            float bx = b.orbitalRadius * Mathf.Cos(b.angle);
            float by = b.orbitalRadius * Mathf.Sin(b.angle);
            return Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        }

        // ── Detail sidebar close ──────────────────────────────────────────────

        private void HideDetailSidebar()
        {
            _selectedSector = null;
            _detailSidebar.style.display = DisplayStyle.None;
            _detailSidebar.Clear();
        }

        // ── Builder helpers ───────────────────────────────────────────────────

        private Button MakeToggleButton(string label)
        {
            var btn = new Button { text = label };
            btn.AddToClassList(ToggleBtnClass);
            btn.style.paddingLeft   = 12;
            btn.style.paddingRight  = 12;
            btn.style.paddingTop    = 4;
            btn.style.paddingBottom = 4;
            btn.style.marginLeft    = 0;
            btn.style.marginRight   = 1;
            btn.style.borderTopLeftRadius     = 3;
            btn.style.borderTopRightRadius    = 3;
            btn.style.borderBottomLeftRadius  = 3;
            btn.style.borderBottomRightRadius = 3;
            return btn;
        }

        private VisualElement MakeDetailHeader(string title)
        {
            var row = new VisualElement();
            row.style.flexDirection    = FlexDirection.Row;
            row.style.alignItems       = Align.Center;
            row.style.paddingTop       = 10;
            row.style.paddingBottom    = 10;
            row.style.paddingLeft      = 10;
            row.style.paddingRight     = 10;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = ColDivider;
            row.style.backgroundColor  = ColSectionHdr;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(DetailTitleClass);
            titleLabel.style.flexGrow  = 1;
            titleLabel.style.color     = ColTextBright;
            titleLabel.style.fontSize  = 13;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(titleLabel);

            var closeBtn = new Button(HideDetailSidebar) { text = "✕" };
            closeBtn.AddToClassList(DetailCloseClass);
            closeBtn.style.width           = 24;
            closeBtn.style.height          = 24;
            closeBtn.style.color           = ColTextMid;
            closeBtn.style.backgroundColor = StyleKeyword.Null;
            closeBtn.style.borderTopWidth  = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            row.Add(closeBtn);

            return row;
        }

        private static void AddDetailRow(VisualElement parent, string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList(DetailRowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;

            var lbl = new Label(label);
            lbl.AddToClassList(DetailLabelClass);
            lbl.style.color    = ColTextMid;
            lbl.style.width    = 110;
            lbl.style.minWidth = 110;
            lbl.style.fontSize = 11;
            row.Add(lbl);

            var val = new Label(value);
            val.AddToClassList(DetailValueClass);
            val.style.color  = ColTextBright;
            val.style.flexGrow = 1;
            val.style.fontSize = 11;
            row.Add(val);

            parent.Add(row);
        }

        private VisualElement MakeSectionHeader(string text)
        {
            var el = new VisualElement();
            el.AddToClassList(SectionHeaderClass);
            el.style.paddingTop       = 6;
            el.style.paddingBottom    = 6;
            el.style.paddingLeft      = 4;
            el.style.marginBottom     = 6;
            el.style.borderBottomWidth = 1;
            el.style.borderBottomColor = ColDivider;

            var lbl = new Label(text);
            lbl.style.color   = ColTextMid;
            lbl.style.fontSize = 10;
            el.Add(lbl);

            return el;
        }

        private static VisualElement MakeDivider()
        {
            var div = new VisualElement();
            div.style.height          = 1;
            div.style.marginTop       = 8;
            div.style.marginBottom    = 8;
            div.style.backgroundColor = ColDivider;
            return div;
        }

        private static void StyleActionButton(Button btn)
        {
            btn.style.paddingLeft       = 10;
            btn.style.paddingRight      = 10;
            btn.style.paddingTop        = 5;
            btn.style.paddingBottom     = 5;
            btn.style.backgroundColor   = new Color(0.10f, 0.14f, 0.22f, 1f);
            btn.style.color             = new Color(0.34f, 0.47f, 0.63f, 1f);
            btn.style.borderTopWidth    = 1;
            btn.style.borderRightWidth  = 1;
            btn.style.borderBottomWidth = 1;
            btn.style.borderLeftWidth   = 1;
            btn.style.borderTopColor    = new Color(0.13f, 0.17f, 0.25f, 1f);
            btn.style.borderRightColor  = new Color(0.13f, 0.17f, 0.25f, 1f);
            btn.style.borderBottomColor = new Color(0.13f, 0.17f, 0.25f, 1f);
            btn.style.borderLeftColor   = new Color(0.13f, 0.17f, 0.25f, 1f);
            btn.style.borderTopLeftRadius     = 3;
            btn.style.borderTopRightRadius    = 3;
            btn.style.borderBottomLeftRadius  = 3;
            btn.style.borderBottomRightRadius = 3;
        }

        // ── Resource profile helper ───────────────────────────────────────────

        /// <summary>
        /// Returns the three most abundant resource categories for a sector,
        /// derived from its phenomenon codes and modifier.
        /// Only returns resources with a positive score.
        /// </summary>
        public static List<string> GetTopResources(SectorData sector)
        {
            if (sector == null) return new List<string>();

            var scores = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "Ore",     0f },
                { "Ice",     0f },
                { "Gas",     0f },
                { "Salvage", 0f },
                { "Anomaly", 0f },
            };

            foreach (var code in sector.phenomenonCodes)
            {
                switch (code)
                {
                    case PhenomenonCode.OR: scores["Ore"]     += 3f; break;
                    case PhenomenonCode.IC: scores["Ice"]     += 3f; break;
                    case PhenomenonCode.GS: scores["Gas"]     += 3f; break;
                    case PhenomenonCode.VD: scores["Salvage"] += 2f; break;
                    case PhenomenonCode.NB:
                    case PhenomenonCode.BH:
                    case PhenomenonCode.PL: scores["Anomaly"] += 2f; break;
                }
            }

            switch (sector.modifier)
            {
                case SectorModifier.RichOreDeposit:  scores["Ore"]     += 3f; break;
                case SectorModifier.IceField:        scores["Ice"]     += 3f; break;
                case SectorModifier.GasPocket:       scores["Gas"]     += 3f; break;
                case SectorModifier.SalvageGraveyard:
                case SectorModifier.DerelictStation: scores["Salvage"] += 3f; break;
                case SectorModifier.AncientRuins:
                case SectorModifier.DarkMatterFilament: scores["Anomaly"] += 3f; break;
            }

            var ranked = new List<KeyValuePair<string, float>>(scores);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            var result = new List<string>();
            for (int i = 0; i < Mathf.Min(3, ranked.Count); i++)
                if (ranked[i].Value > 0f)
                    result.Add(ranked[i].Key);
            return result;
        }

        // ── Display label helpers ─────────────────────────────────────────────

        private static string StarTypeLabel(StarType type) => type switch
        {
            StarType.RedDwarf       => "Red Dwarf",
            StarType.YellowDwarf    => "Yellow Dwarf",
            StarType.BlueGiant      => "Blue Giant",
            StarType.OrangeSubgiant => "Orange Subgiant",
            StarType.WhiteDwarf     => "White Dwarf",
            _                       => type.ToString(),
        };

        private static string BodyTypeLabel(BodyType type) => type switch
        {
            BodyType.RockyPlanet  => "Rocky Planet",
            BodyType.GasGiant     => "Gas Giant",
            BodyType.IcePlanet    => "Ice Planet",
            BodyType.AsteroidBelt => "Asteroid Belt",
            _                     => type.ToString(),
        };

        private static string PlanetClassLabel(PlanetClass pc) => pc switch
        {
            PlanetClass.None             => "—",
            PlanetClass.T1_BarrenRock    => "T-I Barren Rock",
            PlanetClass.T2_Volcanic      => "T-II Volcanic",
            PlanetClass.T3_Desert        => "T-III Desert",
            PlanetClass.T4_Tectonic      => "T-IV Tectonic",
            PlanetClass.T5_Oceanic       => "T-V Oceanic",
            PlanetClass.T6_Terran        => "T-VI Terran",
            PlanetClass.T7_Frozen        => "T-VII Frozen",
            PlanetClass.G1_AmmoniaCloud  => "G-I Ammonia Cloud",
            PlanetClass.G2_WaterCloud    => "G-II Water Cloud",
            PlanetClass.G3_Cloudless     => "G-III Cloudless",
            PlanetClass.G4_AlkaliMetal   => "G-IV Alkali Metal",
            PlanetClass.G5_SilicateCloud => "G-V Silicate Cloud",
            PlanetClass.I1_IceDwarf      => "I-I Ice Dwarf",
            PlanetClass.I2_CryogenicMoon => "I-II Cryogenic Moon",
            PlanetClass.I3_CometaryBody  => "I-III Cometary Body",
            PlanetClass.E1_Chthonian     => "E-I Chthonian",
            PlanetClass.E2_CarbonPlanet  => "E-II Carbon Planet",
            PlanetClass.E3_IronPlanet    => "E-III Iron Planet",
            PlanetClass.E4_HeliumPlanet  => "E-IV Helium Planet",
            PlanetClass.E5_RogueBody     => "E-V Rogue Body",
            _                            => pc.ToString(),
        };

        private static string FormatTag(string tag) => tag switch
        {
            "habitable"        => "Habitable",
            "rich_ore"         => "Rich Ore Deposits",
            "gas_harvest"      => "Gas Harvest Site",
            "ice_deposits"     => "Ice Deposits",
            "ancient_ruins"    => "Ancient Ruins",
            "storm_activity"   => "Storm Activity",
            "subsurface_ocean" => "Subsurface Ocean",
            _                  => tag,
        };
    }

    // ── StyleUtility extension ────────────────────────────────────────────────

    internal static class MapPanelStyleExtensions
    {
        /// <summary>Sets all four corner radii in one call.</summary>
        public static void BorderRadius(this IStyle style, float r)
        {
            style.borderTopLeftRadius     = r;
            style.borderTopRightRadius    = r;
            style.borderBottomLeftRadius  = r;
            style.borderBottomRightRadius = r;
        }
    }
}
