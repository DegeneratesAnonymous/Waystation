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
        private readonly ScrollView    _viewArea;
        private readonly VisualElement _detailSidebar;

        // ── State ──────────────────────────────────────────────────────────────

        private MapLayer     _currentView = MapLayer.System;
        private StationState _station;
        private MapSystem    _map;

        // Currently selected sector for the detail sidebar.
        // Stored so the sidebar can be re-populated after a sector unlock.
        private SectorData   _selectedSector;

        // Dirty-flag values: track last known state to skip full RebuildView()
        // on EP-only tick updates (the most frequent Refresh() call path).
        private int  _lastEp          = -1;
        private int  _lastSectorCount = -1;
        private bool _lastCanSector;

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
        private static readonly Color ColPanelBg     = new Color(0.05f, 0.07f, 0.12f, 0.97f);
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
            Add(_contentRow);

            // View area (fills remaining width, left of detail sidebar)
            _viewArea = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _viewArea.AddToClassList(ViewAreaClass);
            _viewArea.style.flexGrow = 1;
            _viewArea.style.height   = Length.Percent(100);
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
            _epLabel.text = $"✦ {ep} EP";

            // Enable/disable Sector toggle based on map view level
            bool canSector = map?.GetMapViewLevel(station) == MapViewLevel.Sector;
            _sectorBtn.SetEnabled(canSector);
            _sectorBtn.tooltip = canSector ? "" : "Requires Sector Map research";

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
            _currentView = layer;
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
            _viewArea.Clear();

            if (_currentView == MapLayer.System)
                BuildSystemView();
            else
                BuildSectorView();
        }

        // ── System view ───────────────────────────────────────────────────────

        private void BuildSystemView()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft   = 20;
            root.style.paddingTop    = 16;
            root.style.paddingRight  = 20;

            var sys = _station?.solarSystem;
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

            // Star name header
            var starHeader = new Label($"{sys.systemName}  ·  {StarTypeLabel(sys.starType)}");
            starHeader.AddToClassList(SectionHeaderClass);
            starHeader.style.fontSize       = 15;
            starHeader.style.color          = ColAccentText;
            starHeader.style.marginBottom   = 12;
            starHeader.style.paddingTop     = 8;
            starHeader.style.paddingBottom  = 8;
            root.Add(starHeader);

            // Note about uGUI orbital animation
            var animNote = new Label("Orbital animation rendered by system map layer below.");
            animNote.style.color     = ColTextMid;
            animNote.style.fontSize  = 11;
            animNote.style.marginBottom = 16;
            root.Add(animNote);

            // Orbital bodies section
            if (sys.bodies.Count > 0)
            {
                var bodiesHdr = MakeSectionHeader("ORBITAL BODIES");
                root.Add(bodiesHdr);

                for (int i = 0; i < sys.bodies.Count; i++)
                {
                    var body = sys.bodies[i];
                    bool isStation = sys.stationOrbitIndex == i;
                    var row = BuildBodyRow(body, isStation);
                    int capturedIndex = i;
                    row.RegisterCallback<ClickEvent>(_ => ShowBodyDetail(sys.bodies[capturedIndex], sys.stationOrbitIndex == capturedIndex));
                    root.Add(row);
                }
            }
            else
            {
                var noData = new Label("No orbital bodies catalogued.");
                noData.style.color = ColTextMid;
                root.Add(noData);
            }

            // POIs section
            var pois = _map?.GetDiscoveredPois(_station);
            if (pois != null && pois.Count > 0)
            {
                root.Add(MakeSectionHeader("POINTS OF INTEREST"));
                foreach (var poi in pois)
                {
                    var poiRow = new VisualElement();
                    poiRow.style.flexDirection   = FlexDirection.Row;
                    poiRow.style.paddingLeft      = 8;
                    poiRow.style.paddingTop       = 5;
                    poiRow.style.paddingBottom    = 5;
                    poiRow.style.marginBottom     = 2;
                    poiRow.style.backgroundColor  = ColBodyRow;
                    poiRow.style.BorderRadius(3);

                    var poiName = new Label(poi.displayName ?? poi.poiType);
                    poiName.style.color     = ColTextBright;
                    poiName.style.flexGrow  = 1;
                    var poiType = new Label(poi.poiType);
                    poiType.style.color  = ColTextMid;
                    poiType.style.width  = 110;
                    poiRow.Add(poiName);
                    poiRow.Add(poiType);
                    root.Add(poiRow);
                }
            }

            _viewArea.Add(root);
        }

        private VisualElement BuildBodyRow(SolarBody body, bool hasStation)
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
            row.Add(nameLabel);

            // Body type
            var typeLabel = new Label(BodyTypeLabel(body.bodyType));
            typeLabel.AddToClassList(SystemBodyTypeClass);
            typeLabel.style.color = ColTextMid;
            typeLabel.style.width = 100;
            row.Add(typeLabel);

            // Station marker
            if (hasStation)
            {
                var stationLbl = new Label("⬡ STATION");
                stationLbl.style.color     = new Color(0.25f, 1.00f, 0.50f, 1f);
                stationLbl.style.fontSize  = 10;
                stationLbl.style.marginLeft = 6;
                row.Add(stationLbl);
            }

            // Hover highlight
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = ColBodyRowHov);
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = ColBodyRow);

            return row;
        }

        // ── Sector view ───────────────────────────────────────────────────────

        private void BuildSectorView()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft   = 20;
            root.style.paddingTop    = 16;
            root.style.paddingRight  = 20;

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

            var hdr = new Label("SECTOR GRID");
            hdr.AddToClassList(SectionHeaderClass);
            hdr.style.fontSize      = 14;
            hdr.style.color         = ColAccentText;
            hdr.style.marginBottom  = 12;
            hdr.style.paddingTop    = 8;
            hdr.style.paddingBottom = 8;
            root.Add(hdr);

            // Layout sectors in a flex-wrap grid.
            var grid = new VisualElement();
            grid.AddToClassList(SectorGridClass);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap      = Wrap.Wrap;
            grid.style.paddingBottom = 20;
            root.Add(grid);

            // Sort sectors — Visited first, then Detected, then Uncharted, each alpha.
            var sorted = new List<SectorData>(_station.sectors.Values);
            sorted.Sort((a, b) =>
            {
                int stateA = (int)a.discoveryState;
                int stateB = (int)b.discoveryState;
                // Visited > Detected > Uncharted (descending)
                if (stateB != stateA) return stateB.CompareTo(stateA);
                return string.Compare(a.uid, b.uid, StringComparison.Ordinal);
            });

            foreach (var sector in sorted)
            {
                var box = BuildSectorBox(sector);
                grid.Add(box);
            }

            _viewArea.Add(root);
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

            // Header row with close button
            var headerRow = MakeDetailHeader(body.name ?? BodyTypeLabel(body.bodyType));
            _detailSidebar.Add(headerRow);

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
                AddDetailRow(body2, "Orbital Period",  $"{body.orbitalPeriod:F0} ticks");
            }

            if (body.moons.Count > 0)
                AddDetailRow(body2, "Moons", body.moons.Count.ToString());

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

            // Unlock button for adjacent Uncharted sectors
            if (_map != null && _station != null)
            {
                int ep = _station.explorationPoints;
                bool canAfford = ep >= MapSystem.SectorUnlockPointCost;

                body.Add(MakeDivider());

                var unlockHeader = new Label("EXPLORATION");
                unlockHeader.style.color     = ColTextMid;
                unlockHeader.style.fontSize  = 10;
                unlockHeader.style.marginTop  = 8;
                unlockHeader.style.marginBottom = 4;
                body.Add(unlockHeader);

                var unlockCostLabel = new Label($"Unlock adjacent sector: {MapSystem.SectorUnlockPointCost} EP");
                unlockCostLabel.style.color   = ColTextMid;
                unlockCostLabel.style.fontSize = 11;
                unlockCostLabel.style.marginBottom = 6;
                body.Add(unlockCostLabel);

                var capturedSector = sector;
                var unlockBtn = new Button(() =>
                {
                    if (_map == null || _station == null) return;
                    // Attempt to unlock a sector adjacent to this one, trying all four
                    // cardinal directions and capturing the newly generated sector.
                    int col = Mathf.RoundToInt(
                        (capturedSector.coordinates.x - GalaxyGenerator.HomeX) / MapSystem.GalUnitPerCell);
                    int row = Mathf.RoundToInt(
                        (capturedSector.coordinates.y - GalaxyGenerator.HomeY) / MapSystem.GalUnitPerCell);

                    SectorData newSector = TryUnlockAdjacentAndGetSector(col, row);
                    if (newSector != null)
                    {
                        // Notify subscribers (e.g. WaystationHUDController → FactionSystem)
                        OnSectorUnlocked?.Invoke(newSector);
                        // Refresh detects the increased sector count via dirty check and
                        // rebuilds the grid; it also re-populates the detail sidebar for
                        // the currently selected sector (_selectedSector) so the EP
                        // balance and Unlock button state are up-to-date.
                        Refresh(_station, _map);
                    }
                });
                unlockBtn.text = $"UNLOCK ADJACENT ({MapSystem.SectorUnlockPointCost} EP)";
                unlockBtn.AddToClassList(UnlockBtnClass);
                StyleActionButton(unlockBtn);
                unlockBtn.SetEnabled(canAfford);
                if (!canAfford)
                    unlockBtn.tooltip = $"Need {MapSystem.SectorUnlockPointCost} EP (have {ep})";
                body.Add(unlockBtn);
            }
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
