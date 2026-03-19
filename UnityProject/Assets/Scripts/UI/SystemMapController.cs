// SystemMapController — renders the procedurally generated solar system as an
// interactive UI overlay.
//
// Setup (in the Unity Inspector):
//   1. Create a full-screen Canvas panel (mapPanel).
//   2. Inside it, add a square child RectTransform (mapContainer) — this is the
//      drawing area. Anchor it however suits your layout (centre is easiest).
//   3. Add a sidebar panel (detailPanel) with TMP_Text labels for planet info.
//   4. Assign all [SerializeField] references below.
//   5. Call Open() from a HUD button (e.g. a "System Map" button in GameViewController).
//
// The map is fully procedural at runtime — no art assets are required. Planet dots
// and the star are drawn as soft-edged circles via a generated Sprite; orbits use
// the UIRing custom Graphic component.
//
// Planets animate along their orbits every game tick using orbitalPeriod from
// SolarSystemState, so the map reflects live simulation time.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;
using Waystation.View;

namespace Waystation.UI
{
    public class SystemMapController : MonoBehaviour
    {
        // ── Inspector references ──────────────────────────────────────────────

        [Header("Root panels")]
        [SerializeField] private GameObject    mapPanel;
        [SerializeField] private RectTransform mapContainer;   // square drawing area

        [Header("System header")]
        [SerializeField] private TMP_Text systemNameLabel;
        [SerializeField] private TMP_Text starTypeLabel;

        [Header("Detail sidebar")]
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private TMP_Text   detailNameLabel;
        [SerializeField] private TMP_Text   detailTypeLabel;
        [SerializeField] private TMP_Text   detailTagsLabel;
        [SerializeField] private TMP_Text   detailMoonsLabel;
        [SerializeField] private TMP_Text   detailStationLabel;

        [Header("Controls")]
        [SerializeField] private Button closeButton;

        // ── Colour constants ──────────────────────────────────────────────────

        private static readonly Color OrbitColor   = new Color(0.35f, 0.50f, 0.65f, 0.30f);
        private static readonly Color BeltColor    = new Color(0.55f, 0.45f, 0.30f, 0.40f);
        private static readonly Color StationColor = new Color(0.25f, 1.00f, 0.50f, 1.00f);
        private static readonly Color LabelColor   = new Color(0.85f, 0.90f, 1.00f, 0.75f);

        // ── Runtime state ─────────────────────────────────────────────────────

        private GameManager      _gm;
        private SolarSystemState _sys;         // home system
        private SolarSystemState _viewedSystem; // currently shown in System layer
        private float            _mapRadius;  // pixels from centre to edge of mapContainer

        // Map layer
        private MapLayer _layer = MapLayer.System;

        // Parallel lists indexed by _viewedSystem.bodies
        private readonly List<RectTransform> _planetMarkers = new List<RectTransform>();
        private readonly List<TMP_Text>      _planetLabels  = new List<TMP_Text>();
        private readonly List<UIRing>        _orbitRings    = new List<UIRing>();

        private RectTransform _starMarker;
        private RectTransform _stationMarker;

        // ── Explore map (Sector / Galaxy) ───────────────────────────

        // The scrollable/zoomable root container for sector & galaxy layers
        private RectTransform   _exploreWorld;    // panned/scaled child
        private RectTransform   _mapAreaRt;       // stored mapArea reference
        private float           _exploreZoom   = 1f;
        private Vector2         _exploreOffset = Vector2.zero; // in LY from home
        private bool            _isPanning;
        private Vector2         _panStartMouse;
        private Vector2         _panStartOffset;
        // pixels per light-year at zoom=1 for each layer
        private const float SectorPxPerLY  = 2.8f;   // 100LY ≈ 280px → fits 340px radius
        private const float GalaxyPxPerLY  = 1.0f;   // starting scale
        private const float GalaxyZoomMin  = 0.15f;
        private const float GalaxyZoomMax  = 8f;

        // Explore dots (neighbor system markers)
        private readonly List<(RectTransform dot, NeighborSystem sys)> _exploreDots
            = new List<(RectTransform, NeighborSystem)>();
        // Galaxy chunk cache: chunk coords → list of systems in that chunk
        private readonly Dictionary<(int, int), List<NeighborSystem>> _galaxyChunks
            = new Dictionary<(int, int), List<NeighborSystem>>();

        // Sector designation dot/label tracking
        private readonly List<GameObject> _sectorObjects = new List<GameObject>();
        // Cached zoom-threshold state — used to skip needless sector-dot rebuilds in Sector layer.
        private bool _prevZoomedOut;
        // Currently selected sector (for detail panel)
        private SectorData _selectedSector;
        // Sector detail UI refs (assigned in EnsureCanvas)
        private TMP_Text _sectorRenamedLabel;
        private Button   _sectorRenameBtn;
        // Designation colour constants matching design spec
        private static readonly Color ColDetected  = new Color(0.306f, 0.329f, 0.439f); // fBevel #4e5470
        private static readonly Color ColVisited   = new Color(0.282f, 0.502f, 0.667f); // acc   #4880aa
        private static readonly Color ColUncharted = new Color(0.25f,  0.28f,  0.32f,  0.45f);

        // ── Header / Layer UI ────────────────────────────────────────

        private Button   _tabSystemBtn, _tabSectorBtn, _tabGalaxyBtn;
        private TMP_Text _tabSystemLbl, _tabSectorLbl, _tabGalaxyLbl;
        private TMP_Text _contextLabel;
        private GameObject _lockedPanel;
        private TMP_Text   _lockedMessage;

        // ── Static state ──────────────────────────────────────────────────

        /// <summary>Bypass all research/equipment requirements (Dev Tools).</summary>
        public static bool TelescopeMode { get; set; }
        /// <summary>True while any map layer is visible — used to suppress station camera zoom.</summary>
        public static bool IsOpen        { get; private set; }
        // Lazily generated unit-circle sprite (white disc, anti-aliased)
        private static Sprite _circleSprite;
        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _gm = GameManager.Instance;
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void Start()
        {
            if (mapPanel    != null) mapPanel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
        }

        private void OnEnable()
        {
            if (_gm != null) _gm.OnTick += HandleTick;
        }

        private void OnDisable()
        {
            if (_gm != null) _gm.OnTick -= HandleTick;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_layer != MapLayer.Sector && _layer != MapLayer.Galaxy) return;
            if (_exploreWorld == null) return;

            HandleExploreZoom();
            HandleExplorePan();
        }

        private void HandleExploreZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.001f) return;

            float minZ = _layer == MapLayer.Galaxy ? GalaxyZoomMin : 0.5f;
            float maxZ = _layer == MapLayer.Galaxy ? GalaxyZoomMax : 3f;

            float newZoom = Mathf.Clamp(_exploreZoom * (1f + scroll * 0.12f), minZ, maxZ);
            if (Mathf.Approximately(newZoom, _exploreZoom)) return;

            // Zoom toward screen cursor inside the map container
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mapAreaRt, Input.mousePosition, null, out Vector2 localMouse);

            // The offset of the mouse from the current world origin in screen coords
            float zoomRatio = newZoom / _exploreZoom;
            _exploreWorld.anchoredPosition =
                localMouse + ((_exploreWorld.anchoredPosition - localMouse) * zoomRatio);

            _exploreZoom = newZoom;
            _exploreWorld.localScale = new Vector3(newZoom, newZoom, 1f);

            if (_layer == MapLayer.Galaxy)
                RefreshGalaxyChunks();
            else if (_layer == MapLayer.Sector)
            {
                // Only rebuild sector dots when the label-detail threshold (0.8x) actually changes.
                bool zoomedOut = _exploreZoom < 0.8f;
                if (zoomedOut != _prevZoomedOut)
                {
                    _prevZoomedOut = zoomedOut;
                    foreach (var go in _sectorObjects) if (go != null) Destroy(go);
                    _sectorObjects.Clear();
                    RenderSectorDots(SectorPxPerLY, zoomedOut);
                }
            }
        }

        private void HandleExplorePan()
        {
            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                _isPanning       = true;
                _panStartMouse  = mouse;
                _panStartOffset = _exploreWorld.anchoredPosition;
            }

            if (Input.GetMouseButtonUp(0))
                _isPanning = false;

            if (!_isPanning) return;
            Vector2 delta = mouse - _panStartMouse;
            _exploreWorld.anchoredPosition = _panStartOffset + delta;

            if (_layer == MapLayer.Galaxy)
                RefreshGalaxyChunks();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Open()
        {
            EnsureCanvas();
            _gm  = _gm != null ? _gm : GameManager.Instance;
            // Register tick handler here — OnEnable fires before Start sets _gm,
            // so the subscription in OnEnable is a no-op on first load.
            if (_gm != null) { _gm.OnTick -= HandleTick; _gm.OnTick += HandleTick; }
            _sys = _gm?.Station?.solarSystem;
            if (_sys == null)
            {
                Debug.LogWarning("[SystemMapController] No solar system data. " +
                                 "Ensure SolarSystemGenerator.Generate() is called in NewGame().");
                return;
            }
            _viewedSystem = _sys;

            // System layer (home solar system) is always available.
            // Sector requires Local Sensors research + built antenna.
            // Galaxy requires Interstellar Sensors research.
            bool hasSector = TelescopeMode ||
                             ((_gm.Station?.HasTag("tech.local_sensors") == true) && HasBuiltAntenna());
            bool hasGalaxy = TelescopeMode ||
                             (_gm.Station?.HasTag("tech.interstellar_sensors") == true);

            _lockedPanel?.SetActive(false);
            mapPanel.SetActive(true);
            IsOpen = true;

            UpdateLayerTabs(hasSector, hasGalaxy);
            SwitchLayer(MapLayer.System);
        }

        public void Close()
        {
            if (mapPanel != null) mapPanel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
            IsOpen = false;
        }

        // ── Tick handler ──────────────────────────────────────────────────────

        private void HandleTick(StationState station)
        {
            if (mapPanel == null || !mapPanel.activeSelf) return;
            AnimatePlanets(station.tick);
        }

        // ── Map construction ──────────────────────────────────────────────────

        private void RebuildMap()
        {
            ClearMap();

            // Leave a small margin so outer orbits don't clip the container edge.
            _mapRadius = Mathf.Min(mapContainer.rect.width,
                                   mapContainer.rect.height) * 0.5f - 24f;

            // Compute pixel-per-AU scale so the outermost orbit fits inside _mapRadius.
            float maxOrbit = 1f;
            foreach (var b in _viewedSystem.bodies)
                if (b.orbitalRadius > maxOrbit) maxOrbit = b.orbitalRadius;
            float scale = _mapRadius / maxOrbit;

            // ── Star ───────────────────────────────────────────────────
            float starPx = Mathf.Clamp(_viewedSystem.starSize * 30f, 16f, 48f);
            _starMarker = CreateCircleDot(mapContainer, Vector2.zero,
                                          starPx, ParseColor(_viewedSystem.starColorHex), "Star");
            _starMarker.SetAsFirstSibling();   // render behind everything else

            // ── Bodies ──────────────────────────────────────────────────
            for (int i = 0; i < _viewedSystem.bodies.Count; i++)
            {
                var   body    = _viewedSystem.bodies[i];
                float ringPx  = body.orbitalRadius * scale;
                bool  isBelt  = body.bodyType == BodyType.AsteroidBelt;

                // Orbit ring
                float ringThickness = isBelt ? 0.07f : 0.018f;
                Color ringColor     = isBelt ? BeltColor : OrbitColor;
                var   ring          = CreateOrbitRing(mapContainer, ringPx, ringColor, ringThickness);
                _orbitRings.Add(ring);

                if (isBelt)
                {
                    // Belt has no single planet dot — just the wide ring.
                    _planetMarkers.Add(null);
                    _planetLabels.Add(null);

                    if (body.stationIsHere)
                        _stationMarker = CreateStationMarker(mapContainer, ringPx);
                    continue;
                }

                // Planet dot
                float dotPx = Mathf.Clamp(body.size * 20f, 6f, 28f);
                var   dot   = CreateCircleDot(mapContainer, Vector2.zero,
                                              dotPx, ParseColor(body.colorHex), body.name);
                AddClickHandler(dot, body);
                _planetMarkers.Add(dot);

                // Small name label beneath the dot (hidden at small dot sizes)
                var label = CreateLabel(mapContainer, body.name, dotPx * 0.5f + 8f);
                _planetLabels.Add(label);

                if (body.stationIsHere)
                    _stationMarker = CreateStationMarker(mapContainer, ringPx);
            }

            // Initial placement
            AnimatePlanets(_gm?.Station?.tick ?? 0);
        }

        private void AnimatePlanets(int tick)
        {
            if (_viewedSystem == null) return;

            float maxOrbit = 1f;
            foreach (var b in _viewedSystem.bodies)
                if (b.orbitalRadius > maxOrbit) maxOrbit = b.orbitalRadius;
            float scale = _mapRadius / maxOrbit;

            for (int i = 0; i < _viewedSystem.bodies.Count; i++)
            {
                if (i >= _planetMarkers.Count) break;
                var dot = _planetMarkers[i];
                if (dot == null) continue;

                var   body  = _viewedSystem.bodies[i];
                float angle = body.initialPhase + Mathf.PI * 2f * (tick / body.orbitalPeriod);
                float r     = body.orbitalRadius * scale;
                var   pos   = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);

                dot.anchoredPosition = pos;

                // Follow-on label
                if (i < _planetLabels.Count && _planetLabels[i] != null)
                {
                    float dotHalf = dot.sizeDelta.y * 0.5f;
                    ((RectTransform)_planetLabels[i].transform).anchoredPosition =
                        pos + new Vector2(0f, -(dotHalf + 10f));
                }

                // Station marker tracks its host body with a slight angular offset.
                if (body.stationIsHere && _stationMarker != null)
                {
                    float sa  = angle + 0.18f;
                    _stationMarker.anchoredPosition =
                        new Vector2(Mathf.Cos(sa) * r, Mathf.Sin(sa) * r);
                }
            }
        }

        // ── Detail panel ──────────────────────────────────────────────────────

        private void ShowDetail(SolarBody body)
        {
            if (detailPanel == null) return;
            detailPanel.SetActive(true);

            if (detailNameLabel    != null)
                detailNameLabel.text    = body.name;
            if (detailTypeLabel    != null)
                detailTypeLabel.text    = BodyTypeLabel(body.bodyType);
            if (detailTagsLabel    != null)
                detailTagsLabel.text    = body.tags.Count > 0
                    ? string.Join(" · ", body.tags)
                    : "No anomalies detected";
            if (detailMoonsLabel   != null)
                detailMoonsLabel.text   = body.moons.Count > 0
                    ? $"{body.moons.Count} moon{(body.moons.Count > 1 ? "s" : "")}"
                    : "No moons";
            if (detailStationLabel != null)
                detailStationLabel.text = body.stationIsHere ? "Station orbiting here" : "";
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        private void ClearMap()
        {
            foreach (var m in _planetMarkers) if (m != null) Destroy(m.gameObject);
            foreach (var l in _planetLabels)  if (l != null) Destroy(l.gameObject);
            foreach (var r in _orbitRings)    if (r != null) Destroy(r.gameObject);
            if (_starMarker    != null) { Destroy(_starMarker.gameObject);    _starMarker    = null; }
            if (_stationMarker != null) { Destroy(_stationMarker.gameObject); _stationMarker = null; }
            _planetMarkers.Clear();
            _planetLabels.Clear();
            _orbitRings.Clear();
        }

        // ── Layer switching ───────────────────────────────────────────────────

        private void SwitchLayer(MapLayer layer)
        {
            _layer = layer;
            ClearMap();
            ClearExplore();

            bool explore = (layer == MapLayer.Sector || layer == MapLayer.Galaxy);
            if (mapContainer           != null) mapContainer.gameObject.SetActive(!explore);
            // Toggle the ExploreClip panel (parent of _exploreWorld)
            if (_exploreWorld != null)
                _exploreWorld.parent.gameObject.SetActive(explore);
            if (detailPanel            != null) detailPanel.SetActive(false);

            // Highlight active tab
            SetTabActive(_tabSystemBtn, _tabSystemLbl, layer == MapLayer.System);
            SetTabActive(_tabSectorBtn, _tabSectorLbl, layer == MapLayer.Sector);
            SetTabActive(_tabGalaxyBtn, _tabGalaxyLbl, layer == MapLayer.Galaxy);

            switch (layer)
            {
                case MapLayer.System: RebuildMap(); break;
                case MapLayer.Sector: RebuildSector(); break;
                case MapLayer.Galaxy: RebuildGalaxy(); break;
            }
            UpdateContextLabel();
        }

        private void SetTabActive(Button btn, TMP_Text lbl, bool active)
        {
            if (btn == null) return;
            var colors = btn.colors;
            colors.normalColor = active
                ? new Color(0.22f, 0.55f, 0.90f, 1f)
                : new Color(0.10f, 0.14f, 0.24f, 0.90f);
            btn.colors = colors;
            if (lbl != null)
                lbl.color = active ? Color.white : new Color(0.60f, 0.72f, 0.90f);
        }

        private void UpdateLayerTabs(bool hasSector, bool hasGalaxy)
        {
            if (_tabSectorBtn != null) _tabSectorBtn.interactable = hasSector;
            if (_tabGalaxyBtn != null) _tabGalaxyBtn.interactable = hasGalaxy;
            if (_tabSectorLbl != null) _tabSectorLbl.color = hasSector
                ? new Color(0.60f, 0.72f, 0.90f)
                : new Color(0.35f, 0.40f, 0.50f);
            if (_tabGalaxyLbl != null) _tabGalaxyLbl.color = hasGalaxy
                ? new Color(0.60f, 0.72f, 0.90f)
                : new Color(0.35f, 0.40f, 0.50f);
        }

        private void UpdateContextLabel()
        {
            if (_contextLabel == null) return;
            switch (_layer)
            {
                case MapLayer.System:
                    _contextLabel.text = _viewedSystem != null
                        ? $"{_viewedSystem.systemName}  ·  {StarTypeLabel(_viewedSystem.starType)}"
                        : "";
                    break;
                case MapLayer.Sector:
                    _contextLabel.text = "Sector Map  ·  100 LY radius";
                    break;
                case MapLayer.Galaxy:
                    _contextLabel.text = "Galaxy Map  ·  scroll to zoom · drag to pan";
                    break;
            }
        }

        private void ShowLocked(string message)
        {
            EnsureCanvas();
            mapPanel.SetActive(true);
            IsOpen = true;
            if (_lockedPanel  != null) _lockedPanel.SetActive(true);
            if (_lockedMessage != null) _lockedMessage.text = message;
        }

        private bool HasBuiltAntenna()
        {
            var station = _gm?.Station;
            if (station == null) return false;
            return _gm.Antenna.HasPoweredAntenna(station);
        }

        // ── Sector map ────────────────────────────────────────────────────────

        private void RebuildSector()
        {
            _exploreZoom   = 1f;
            _exploreWorld.anchoredPosition = Vector2.zero;
            _exploreWorld.localScale = Vector3.one;

            if (_sys == null) return;
            var neighbors = SolarSystemGenerator.GenerateNeighbors(_sys.seed, 100f);

            // Home system dot at centre (bright white/yellow)
            CreateExploreDot(_exploreWorld, Vector2.zero, 12f,
                new Color(1f, 0.95f, 0.50f), _sys, isHome: true);

            foreach (var n in neighbors)
            {
                var pos = n.positionLY * SectorPxPerLY;
                CreateExploreDot(_exploreWorld, pos, Mathf.Clamp(n.starSize * 9f, 5f, 14f),
                    ParseColor(n.starColorHex), neighbor: n);
            }

            // Overlay sector designation labels on the sector map.
            RenderSectorDots(SectorPxPerLY, zoomedOut: false);
        }

        // ── Galaxy map ────────────────────────────────────────────────────────

        private void RebuildGalaxy()
        {
            _exploreZoom = 1f;
            _exploreWorld.anchoredPosition = Vector2.zero;
            _exploreWorld.localScale = Vector3.one;
            RefreshGalaxyChunks();
        }

        private void RefreshGalaxyChunks()
        {
            if (_sys == null || _exploreWorld == null) return;

            // Determine which LY rect is visible based on current offset + zoom
            float pxPerLY   = GalaxyPxPerLY * _exploreZoom;
            float viewW = (_mapAreaRt != null && _mapAreaRt.rect.width  > 0f) ? _mapAreaRt.rect.width  : 680f;
            float viewH = (_mapAreaRt != null && _mapAreaRt.rect.height > 0f) ? _mapAreaRt.rect.height : 680f;
            float halfW = viewW / 2f / Mathf.Max(pxPerLY, 0.01f);
            float halfH = viewH / 2f / Mathf.Max(pxPerLY, 0.01f);
            // Centre of view in LY coords
            Vector2 centreLY = -_exploreWorld.anchoredPosition / Mathf.Max(pxPerLY, 0.01f);
            float   genRadius = Mathf.Max(halfW, halfH) + 80f;  // add one chunk margin

            var newSystems = SolarSystemGenerator.GenerateNeighbors(
                _sys.seed, genRadius, centreLY);

            // Remove dots that are far out of view
            for (int i = _exploreDots.Count - 1; i >= 0; i--)
            {
                var (dot, sys) = _exploreDots[i];
                if (sys == null) continue;  // home dot
                Vector2 sysLY = sys.positionLY;
                float dx = sysLY.x - centreLY.x;
                float dy = sysLY.y - centreLY.y;
                if (dx * dx + dy * dy > (genRadius + 80f) * (genRadius + 80f))
                {
                    if (dot != null) Destroy(dot.gameObject);
                    _exploreDots.RemoveAt(i);
                }
            }

            // Collect existing seeds to avoid duplicates
            var existingSeeds = new HashSet<int>();
            foreach (var (_, sys) in _exploreDots)
                if (sys != null) existingSeeds.Add(sys.seed);

            foreach (var n in newSystems)
            {
                if (existingSeeds.Contains(n.seed)) continue;
                existingSeeds.Add(n.seed);
                var pos = n.positionLY * GalaxyPxPerLY;
                CreateExploreDot(_exploreWorld, pos, Mathf.Clamp(n.starSize * 7f, 3f, 11f),
                    ParseColor(n.starColorHex), neighbor: n);
            }

            // Home system at origin
            if (_exploreDots.Count(t => t.sys == null) == 0)
                CreateExploreDot(_exploreWorld, Vector2.zero, 12f,
                    new Color(1f, 0.95f, 0.50f), _sys, isHome: true);

            // Overlay sector designation dots/labels on the galaxy map (created once; no per-pan churn).
            if (_sectorObjects.Count == 0)
                RenderSectorDots(GalaxyPxPerLY, _exploreZoom < 0.8f);
        }

        // ── Sector designation rendering ──────────────────────────────────────

        /// <summary>
        /// Renders sector designation dots and labels on the explore map.
        /// Home sector is at (22, 51) in galaxy coordinate space; all other sector
        /// positions are converted to LY offsets from home and then to screen pixels.
        /// <para>
        /// Uncharted sectors render as dim grey dots with no label.
        /// Detected sectors show the Short designation format (or Minimal when zoomed out).
        /// Visited sectors show the same labels but in the accent colour.
        /// </para>
        /// </summary>
        private void RenderSectorDots(float pxPerUnit, bool zoomedOut)
        {
            var station = _gm?.Station;
            if (station == null || station.sectors.Count == 0) return;

            foreach (var sector in station.sectors.Values)
            {
                // Convert galaxy coordinates to LY offset from home sector.
                float offsetX = (sector.coordinates.x - GalaxyGenerator.HomeX) * pxPerUnit;
                float offsetY = (sector.coordinates.y - GalaxyGenerator.HomeY) * pxPerUnit;
                var   pos     = new Vector2(offsetX, offsetY);

                Color dotColor;
                string labelText = null;

                switch (sector.discoveryState)
                {
                    case SectorDiscoveryState.Uncharted:
                        dotColor  = ColUncharted;
                        labelText = null; // no label for uncharted
                        break;
                    case SectorDiscoveryState.Detected:
                        dotColor  = ColDetected;
                        labelText = zoomedOut
                            ? sector.MinimalDesignation()
                            : sector.ShortDesignation();
                        break;
                    case SectorDiscoveryState.Visited:
                    default:
                        dotColor  = ColVisited;
                        labelText = zoomedOut
                            ? sector.MinimalDesignation()
                            : sector.ShortDesignation();
                        break;
                }

                // ── Sector dot ────────────────────────────────────────────────
                var goName  = $"Sector_{sector.uid}";
                var dotGo   = new GameObject(goName, typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(_exploreWorld, false);
                var dotRt   = dotGo.GetComponent<RectTransform>();
                dotRt.anchorMin = dotRt.anchorMax = new Vector2(0.5f, 0.5f);
                dotRt.sizeDelta        = new Vector2(8f, 8f);
                dotRt.anchoredPosition = pos;
                var img = dotGo.GetComponent<Image>();
                img.sprite = GetCircleSprite();
                img.color  = dotColor;

                // Click handler on the dot
                var captured = sector;
                var btn = dotGo.AddComponent<Button>();
                btn.onClick.AddListener(() => ShowSectorDetail(captured));
                _sectorObjects.Add(dotGo);

                // ── Designation label ─────────────────────────────────────────
                if (!string.IsNullOrEmpty(labelText))
                {
                    var lblGo = new GameObject(goName + "_Lbl",
                        typeof(RectTransform), typeof(TextMeshProUGUI));
                    lblGo.transform.SetParent(_exploreWorld, false);
                    var lblRt = lblGo.GetComponent<RectTransform>();
                    lblRt.anchorMin = lblRt.anchorMax = new Vector2(0.5f, 0.5f);
                    lblRt.sizeDelta        = new Vector2(160f, 18f);
                    lblRt.anchoredPosition = pos + new Vector2(0f, 10f);
                    var lbl = lblGo.GetComponent<TMP_Text>();
                    lbl.text      = labelText;
                    lbl.fontSize  = 7f;
                    lbl.alignment = TextAlignmentOptions.Center;
                    lbl.color     = dotColor;
                    lbl.raycastTarget = false;
                    _sectorObjects.Add(lblGo);
                }
            }
        }

        /// <summary>
        /// Show the sector detail panel for a clicked <see cref="SectorData"/>.
        /// </summary>
        private void ShowSectorDetail(SectorData sector)
        {
            _selectedSector = sector;
            if (detailPanel == null) return;
            detailPanel.SetActive(true);

            // ── Reuse existing detail panel labels for sector data ─────────────
            // The detail panel labels are shared between solar body and sector detail.
            // We populate each one with sector-specific content here.

            if (detailNameLabel != null)
            {
                switch (sector.discoveryState)
                {
                    case SectorDiscoveryState.Uncharted:
                        detailNameLabel.text = "Uncharted Sector";
                        break;
                    default:
                        detailNameLabel.text = sector.properName
                            + (sector.isRenamed ? "  (renamed)" : "");
                        break;
                }
            }

            if (detailTypeLabel != null)
            {
                switch (sector.discoveryState)
                {
                    case SectorDiscoveryState.Uncharted:
                        detailTypeLabel.text = "Uncharted — extend Antenna range to detect.";
                        break;
                    default:
                        detailTypeLabel.text = sector.FullDesignation();
                        break;
                }
            }

            if (detailTagsLabel != null)
            {
                if (sector.discoveryState == SectorDiscoveryState.Uncharted)
                {
                    detailTagsLabel.text = "";
                }
                else
                {
                    var phenomParts = new System.Collections.Generic.List<string>();
                    foreach (var code in sector.phenomenonCodes)
                        phenomParts.Add(PhenomenonCodeLabel(code));
                    detailTagsLabel.text = string.Join(" · ", phenomParts);
                }
            }

            if (detailMoonsLabel != null)
            {
                detailMoonsLabel.text = sector.discoveryState != SectorDiscoveryState.Uncharted
                    ? $"Position: {sector.CoordString()}"
                    : "";
            }

            if (detailStationLabel != null)
            {
                // Show the station-present label for the home sector specifically
                // (identified by canonical home coordinates). Visited state alone is
                // insufficient once multi-station founding ships (future work order).
                bool isHomeSector =
                    Mathf.Approximately(sector.coordinates.x, GalaxyGenerator.HomeX) &&
                    Mathf.Approximately(sector.coordinates.y, GalaxyGenerator.HomeY);
                detailStationLabel.text = isHomeSector ? "Your station is in this sector" : "";
            }

            // Hide the "View System Map" button; it's for NeighborSystem clicks only.
            if (_detailViewBtn != null) _detailViewBtn.gameObject.SetActive(false);

            // Show rename button only for Detected or Visited sectors.
            if (_sectorRenameBtn != null)
                _sectorRenameBtn.gameObject.SetActive(sector.CanRename());

            // Show "(renamed)" indicator via the _sectorRenamedLabel if available.
            if (_sectorRenamedLabel != null)
                _sectorRenamedLabel.gameObject.SetActive(sector.isRenamed);
        }

        private static string PhenomenonCodeLabel(PhenomenonCode code) => code switch
        {
            PhenomenonCode.NB => "Nebula",
            PhenomenonCode.PL => "Pulsar",
            PhenomenonCode.BH => "Black Hole",
            PhenomenonCode.DW => "Dwarf Stars",
            PhenomenonCode.GI => "Giant Stars",
            PhenomenonCode.MS => "Main Sequence",
            PhenomenonCode.OR => "Ore-Rich",
            PhenomenonCode.IC => "Ice-Rich",
            PhenomenonCode.GS => "Gas-Rich",
            PhenomenonCode.VD => "Void",
            PhenomenonCode.RD => "Radiation",
            PhenomenonCode.GV => "Gravitational",
            PhenomenonCode.DK => "Dark Matter",
            PhenomenonCode.ST => "Storm",
            _                 => code.ToString(),
        };

        private void TryRenameSelectedSector(string newName)
        {
            if (_selectedSector == null) return;
            if (!_selectedSector.TryRename(newName)) return;
            // Refresh the detail panel with updated info.
            ShowSectorDetail(_selectedSector);
            // Refresh sector label in world (clear and re-render).
            if (_layer == MapLayer.Sector) RebuildSector();
            else if (_layer == MapLayer.Galaxy) RefreshGalaxyChunks();
        }

        // ── Explore dot factory ───────────────────────────────────────────────

        private void CreateExploreDot(RectTransform parent, Vector2 pos, float diameter,
            Color col, SolarSystemState sysState = null,
            NeighborSystem neighbor = null, bool isHome = false)
        {
            var go  = new GameObject(neighbor?.systemName ?? "Home", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(diameter, diameter);
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color  = col;

            // Home gets a bright ring outline
            if (isHome)
            {
                var ringGo = new GameObject("HomeRing", typeof(RectTransform), typeof(UIRing));
                ringGo.transform.SetParent(parent, false);
                var rrt = ringGo.GetComponent<RectTransform>();
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
                rrt.sizeDelta        = new Vector2(diameter + 8f, diameter + 8f);
                rrt.anchoredPosition = pos;
                var ur = ringGo.GetComponent<UIRing>();
                ur.color     = new Color(1f, 0.95f, 0.50f, 0.55f);
                ur.thickness = 0.12f;
                ur.segments  = 32;
                ur.raycastTarget = false;
            }

            // Click handler — show neighbor detail
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.onClick.AddListener(() =>
            {
                if (neighbor != null)
                    ShowNeighborDetail(neighbor);
                else if (sysState != null)
                    ShowHomeDetail(sysState);
            });

            _exploreDots.Add((rt, neighbor));
        }

        private void ShowNeighborDetail(NeighborSystem n)
        {
            if (detailPanel == null) return;
            detailPanel.SetActive(true);
            if (detailNameLabel    != null) detailNameLabel.text    = n.systemName;
            if (detailTypeLabel    != null) detailTypeLabel.text    = StarTypeLabel(n.starType);
            if (detailTagsLabel    != null) detailTagsLabel.text    = $"{n.positionLY.magnitude:F0} LY from home";
            if (detailMoonsLabel   != null) detailMoonsLabel.text   = "";
            if (detailStationLabel != null) detailStationLabel.text = "";

            // "View System" button — generate the system and switch to System layer
            SetDetailViewButton(() =>
            {
                _viewedSystem = SolarSystemGenerator.Generate(n.systemName, n.seed);
                SwitchLayer(MapLayer.System);
            });
        }

        private void ShowHomeDetail(SolarSystemState s)
        {
            if (detailPanel == null) return;
            detailPanel.SetActive(true);
            if (detailNameLabel    != null) detailNameLabel.text    = s.systemName + "  (Home)";
            if (detailTypeLabel    != null) detailTypeLabel.text    = StarTypeLabel(s.starType);
            if (detailTagsLabel    != null) detailTagsLabel.text    = "Your station is here";
            if (detailMoonsLabel   != null) detailMoonsLabel.text   = "";
            if (detailStationLabel != null) detailStationLabel.text = "";
            SetDetailViewButton(() =>
            {
                _viewedSystem = _sys;
                SwitchLayer(MapLayer.System);
            });
        }

        // The detail panel has a button slot at the bottom — wire it once via a stored ref
        private Button _detailViewBtn;
        private void SetDetailViewButton(UnityEngine.Events.UnityAction action)
        {
            if (_detailViewBtn == null) return;
            _detailViewBtn.onClick.RemoveAllListeners();
            _detailViewBtn.gameObject.SetActive(true);
            _detailViewBtn.onClick.AddListener(action);
        }

        // ── Clear explore ─────────────────────────────────────────────────────

        private void ClearExplore()
        {
            if (_exploreWorld != null)
                foreach (Transform child in _exploreWorld)
                    Destroy(child.gameObject);
            _exploreDots.Clear();
            _sectorObjects.Clear();
            _selectedSector = null;
        }

        // ── Factory helpers ───────────────────────────────────────────────────

        private RectTransform CreateCircleDot(Transform parent, Vector2 pos,
                                              float diameter, Color col, string goName)
        {
            var go  = new GameObject(goName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(diameter, diameter);
            rt.anchoredPosition = pos;
            var img   = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color  = col;
            return rt;
        }

        private UIRing CreateOrbitRing(Transform parent, float radiusPx,
                                       Color col, float thickness)
        {
            var go = new GameObject("Orbit", typeof(RectTransform), typeof(UIRing));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            float d = radiusPx * 2f;
            rt.sizeDelta        = new Vector2(d, d);
            rt.anchoredPosition = Vector2.zero;
            var ring = go.GetComponent<UIRing>();
            ring.color     = col;
            ring.thickness = thickness;
            ring.segments  = 64;
            ring.raycastTarget = false;   // orbit rings never intercept clicks
            return ring;
        }

        private RectTransform CreateStationMarker(Transform parent, float ringPx)
        {
            // A small bright square rotated 45° (diamond shape) to distinguish from planets.
            var go  = new GameObject("StationMarker", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(9f, 9f);
            rt.anchoredPosition = new Vector2(ringPx, 0f);
            var img   = go.GetComponent<Image>();
            img.color  = StationColor;
            // Use a plain white square for the station diamond (no circle sprite needed).
            return rt;
        }

        private TMP_Text CreateLabel(Transform parent, string text, float yOffset)
        {
            var go  = new GameObject("Label_" + text, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(120f, 20f);
            rt.anchoredPosition = new Vector2(0f, -yOffset);
            var lbl = go.GetComponent<TMP_Text>();
            lbl.text      = text;
            lbl.fontSize  = 9f;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.color     = LabelColor;
            lbl.raycastTarget = false;
            return lbl;
        }

        private void AddClickHandler(RectTransform dot, SolarBody body)
        {
            var btn = dot.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.onClick.AddListener(() => ShowDetail(body));
        }

        // ── Static helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Generates a soft-edged white disc Sprite once and caches it.
        /// Used for planet dots and the star.
        /// </summary>
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            const int size = 64;
            var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float c  = size * 0.5f;
            float r2 = c * c;
            // Soft alpha edge: full inside, fades over ~3 pixels at the rim.
            float feather = size * 0.04f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx   = x - c + 0.5f;
                float dy   = y - c + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a    = Mathf.Clamp01((c - dist) / feather);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex,
                new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        private static Color ParseColor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var col) ? col : Color.white;
        }

        private static string BodyTypeLabel(BodyType t) => t switch
        {
            BodyType.RockyPlanet  => "Rocky Planet",
            BodyType.GasGiant     => "Gas Giant",
            BodyType.IcePlanet    => "Ice Planet",
            BodyType.AsteroidBelt => "Asteroid Belt",
            _                     => t.ToString(),
        };

        private static string StarTypeLabel(StarType t) => t switch
        {
            StarType.RedDwarf       => "Red Dwarf",
            StarType.YellowDwarf    => "Yellow Dwarf",
            StarType.BlueGiant      => "Blue Giant",
            StarType.OrangeSubgiant => "Orange Subgiant",
            StarType.WhiteDwarf     => "White Dwarf",
            _                       => t.ToString(),
        };

        // ── Auto-install (no manual scene setup needed) ───────────────────────

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (FindAnyObjectByType<SystemMapController>() != null) return;

            // UGUI buttons require a UnityEngine.EventSystems.EventSystem to process clicks.
            // GameHUD uses IMGUI so one is never auto-created — ensure it exists here.
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                DontDestroyOnLoad(esGo);
            }

            var go     = new GameObject("SystemMapController",
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            go.AddComponent<SystemMapController>();
        }

        // ── Procedural canvas construction ────────────────────────────────────
        // Builds the full UGUI hierarchy so no Unity Editor wiring is required.

        private void EnsureCanvas()
        {
            if (mapPanel != null) return;

            // ── Local helpers ─────────────────────────────────────────────────
            RectTransform MakeRect(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                return go.GetComponent<RectTransform>();
            }
            RectTransform MakeImage(Transform parent, string name, Color col)
            {
                var go  = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = col;
                return go.GetComponent<RectTransform>();
            }
            void Stretch(RectTransform rt,
                         float aMinX, float aMinY, float aMaxX, float aMaxY,
                         float offL = 0f, float offB = 0f,
                         float offR = 0f, float offT = 0f)
            {
                rt.anchorMin = new Vector2(aMinX, aMinY);
                rt.anchorMax = new Vector2(aMaxX, aMaxY);
                rt.offsetMin = new Vector2(offL,  offB);
                rt.offsetMax = new Vector2(offR,  offT);
            }
            TMP_Text MakeLabel(Transform parent, string name, float fontSize,
                               TextAlignmentOptions align, Color col, bool bold = false)
            {
                var rt = MakeRect(parent, name);
                rt.gameObject.AddComponent<TextMeshProUGUI>();
                var t = rt.GetComponent<TMP_Text>();
                t.fontSize  = fontSize;
                t.alignment = align;
                t.color     = col;
                t.raycastTarget = false;
                if (bold) t.fontStyle = FontStyles.Bold;
                return t;
            }
            Button MakeButton(Transform parent, string name, Color bg,
                              string label, float fontSize, UnityEngine.Events.UnityAction onClick)
            {
                var bgRt = MakeImage(parent, name, bg);
                var btn  = bgRt.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(onClick);
                var lblRt = MakeRect(bgRt, "Lbl");
                lblRt.gameObject.AddComponent<TextMeshProUGUI>();
                Stretch(lblRt, 0f, 0f, 1f, 1f);
                var lbl = lblRt.GetComponent<TMP_Text>();
                lbl.text      = label;
                lbl.fontSize  = fontSize;
                lbl.alignment = TextAlignmentOptions.Center;
                lbl.color     = new Color(0.85f, 0.90f, 1f);
                lbl.raycastTarget = false;
                return btn;
            }

            // ── Full-screen dark backdrop ─────────────────────────────────────
            var rootRt = MakeImage(transform, "MapPanel",
                new Color(0.03f, 0.05f, 0.10f, 0.97f));
            Stretch(rootRt, 0f, 0f, 1f, 1f);
            mapPanel = rootRt.gameObject;
            mapPanel.SetActive(false);

            // ── Header strip (top 56 px) ──────────────────────────────────────
            var headerRt = MakeImage(mapPanel.transform, "Header",
                new Color(0.06f, 0.09f, 0.18f, 1f));
            Stretch(headerRt, 0f, 1f, 1f, 1f, 0f, -56f, 0f, 0f);

            // Layer tab buttons (left 50 %)
            Color tabActiveCol  = new Color(0.22f, 0.55f, 0.90f, 1f);
            Color tabNormalCol  = new Color(0.10f, 0.14f, 0.24f, 0.90f);
            void SetupTab(ref Button btn, ref TMP_Text lbl, RectTransform parent,
                          string text, float anchorX0, float anchorX1, MapLayer target)
            {
                var rt = MakeImage(parent, text + "Tab", tabNormalCol);
                rt.anchorMin = new Vector2(anchorX0, 0.1f);
                rt.anchorMax = new Vector2(anchorX1, 0.9f);
                rt.offsetMin = new Vector2(2f, 0f);
                rt.offsetMax = new Vector2(-2f, 0f);
                var b = rt.gameObject.AddComponent<Button>();
                var lblRt = MakeRect(rt, "Lbl");
                lblRt.gameObject.AddComponent<TextMeshProUGUI>();
                Stretch(lblRt, 0f, 0f, 1f, 1f);
                var l = lblRt.GetComponent<TMP_Text>();
                l.text      = text;
                l.fontSize  = 12f;
                l.alignment = TextAlignmentOptions.Center;
                l.color     = new Color(0.60f, 0.72f, 0.90f);
                l.raycastTarget = false;
                MapLayer captured = target;
                b.onClick.AddListener(() => SwitchLayer(captured));
                btn = b;
                lbl = l;
            }
            SetupTab(ref _tabSystemBtn, ref _tabSystemLbl, headerRt, "⊙  System", 0f,     0.167f, MapLayer.System);
            SetupTab(ref _tabSectorBtn, ref _tabSectorLbl, headerRt, "◎  Sector", 0.167f, 0.334f, MapLayer.Sector);
            SetupTab(ref _tabGalaxyBtn, ref _tabGalaxyLbl, headerRt, "✦  Galaxy", 0.334f, 0.50f,  MapLayer.Galaxy);

            //   Context label (centre 40 %)
            {
                var rt = MakeLabel(headerRt, "ContextLabel", 12f,
                    TextAlignmentOptions.Center, new Color(0.72f, 0.82f, 1.00f));
                Stretch(rt.rectTransform, 0.50f, 0f, 0.84f, 1f, 8f, 0f, -8f, 0f);
                _contextLabel = rt;
            }

            //   Close button (right side)
            {
                var btn = MakeButton(headerRt, "CloseBtn",
                    new Color(0.22f, 0.28f, 0.44f, 0.90f), "\u00d7  Close", 13f, Close);
                closeButton = btn;
                var rt = (RectTransform)btn.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.sizeDelta        = new Vector2(90f, 36f);
                rt.anchoredPosition = new Vector2(-54f, 0f);
            }

            // ── Map area (left 68 %, below header) ───────────────────────────
            _mapAreaRt = MakeRect(mapPanel.transform, "MapArea");
            Stretch(_mapAreaRt, 0f, 0f, 0.68f, 1f, 0f, 0f, 0f, -56f);

            //   System layer drawing container (square, centred)
            mapContainer = MakeRect(_mapAreaRt, "MapContainer");
            mapContainer.anchorMin = mapContainer.anchorMax = new Vector2(0.5f, 0.5f);
            mapContainer.sizeDelta        = new Vector2(680f, 680f);
            mapContainer.anchoredPosition = Vector2.zero;

            //   Explore container (fills mapArea, used for Sector & Galaxy)
            {
                //   Clip panel — RectMask2D clips by bounds (no stencil/sprite needed,
                //   avoids the alpha=0 Image bug that breaks Mask stencil writes)
                var clipRt = MakeRect(_mapAreaRt, "ExploreClip");
                clipRt.gameObject.AddComponent<RectMask2D>();
                Stretch(clipRt, 0f, 0f, 1f, 1f);

                //   World root — gets translated/scaled for pan+zoom
                _exploreWorld = MakeRect(clipRt, "ExploreWorld");
                _exploreWorld.anchorMin = _exploreWorld.anchorMax = new Vector2(0.5f, 0.5f);
                _exploreWorld.sizeDelta        = new Vector2(10000f, 10000f);
                _exploreWorld.anchoredPosition = Vector2.zero;
                clipRt.gameObject.SetActive(false);  // hidden until Sector/Galaxy layer
                // Store reference via exploring container being the parent
                // (we toggle clipRt, not _exploreWorld directly)
                // Re-wire: keep reference to clip root via _exploreWorld.parent
            }

            // ── Detail sidebar (right 32 %, below header) ─────────────────────
            var detailBgRt = MakeImage(mapPanel.transform, "DetailPanel",
                new Color(0.06f, 0.09f, 0.18f, 0.95f));
            Stretch(detailBgRt, 0.68f, 0f, 1f, 1f, 6f, 0f, 0f, -56f);
            detailPanel = detailBgRt.gameObject;
            detailPanel.SetActive(false);

            float dy = -16f;
            TMP_Text AddDetailLabel(string name, float size, bool bold, Color col)
            {
                var rt = MakeRect(detailBgRt, name);
                rt.gameObject.AddComponent<TextMeshProUGUI>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.sizeDelta        = new Vector2(0f, size + 8f);
                rt.anchoredPosition = new Vector2(0f, dy);
                var t = rt.GetComponent<TMP_Text>();
                t.fontSize  = size;
                t.alignment = TextAlignmentOptions.TopLeft;
                t.color     = col;
                t.margin    = new Vector4(14f, 4f, 8f, 0f);
                t.raycastTarget = false;
                if (bold) t.fontStyle = FontStyles.Bold;
                dy -= size + 14f;
                return t;
            }
            detailNameLabel    = AddDetailLabel("BodyName",    18f, true,  new Color(0.92f, 0.96f, 1.00f));
            detailTypeLabel    = AddDetailLabel("BodyType",    13f, false, new Color(0.62f, 0.72f, 0.90f));
            detailTagsLabel    = AddDetailLabel("BodyTags",    11f, false, new Color(0.55f, 0.65f, 0.78f));
            detailMoonsLabel   = AddDetailLabel("BodyMoons",   11f, false, new Color(0.55f, 0.65f, 0.78f));
            detailStationLabel = AddDetailLabel("BodyStation", 11f, false, new Color(0.30f, 1.00f, 0.55f));

            // "View System Map" button in the detail panel
            {
                dy -= 8f;
                var btn = MakeButton(detailBgRt, "ViewSystemBtn",
                    new Color(0.14f, 0.36f, 0.65f, 0.95f), "View System Map", 12f, () => { });
                var rt = (RectTransform)btn.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.sizeDelta        = new Vector2(-24f, 30f);
                rt.anchoredPosition = new Vector2(0f, dy);
                btn.gameObject.SetActive(false);  // shown only for explore-layer selections
                _detailViewBtn = btn;
                dy -= 38f;
            }

            // "Rename" button — shown only for Detected/Visited sectors
            {
                dy -= 4f;
                var btn = MakeButton(detailBgRt, "RenameSectorBtn",
                    new Color(0.18f, 0.28f, 0.42f, 0.95f), "✎  Rename Sector", 11f, () => { });
                // Disabled until a proper TMP_InputField rename dialog is implemented.
                btn.interactable = false;
                _sectorRenameBtn = btn;
                var rt = (RectTransform)btn.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.sizeDelta        = new Vector2(-24f, 28f);
                rt.anchoredPosition = new Vector2(0f, dy);
                btn.gameObject.SetActive(false);
                dy -= 34f;
            }

            // "(renamed)" indicator label
            {
                var rt = MakeRect(detailBgRt, "RenamedIndicator");
                rt.gameObject.AddComponent<TextMeshProUGUI>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.sizeDelta        = new Vector2(0f, 16f);
                rt.anchoredPosition = new Vector2(0f, dy);
                _sectorRenamedLabel = rt.GetComponent<TMP_Text>();
                _sectorRenamedLabel.text      = "(renamed)";
                _sectorRenamedLabel.fontSize  = 10f;
                _sectorRenamedLabel.alignment = TextAlignmentOptions.Center;
                _sectorRenamedLabel.color     = new Color(0.60f, 0.75f, 0.50f);
                _sectorRenamedLabel.fontStyle  = FontStyles.Italic;
                _sectorRenamedLabel.raycastTarget = false;
                _sectorRenamedLabel.gameObject.SetActive(false);
            }

            // ── Locked overlay ────────────────────────────────────────────────
            {
                var lockRt = MakeImage(mapPanel.transform, "LockedPanel",
                    new Color(0.00f, 0.00f, 0.03f, 0.90f));
                Stretch(lockRt, 0f, 0f, 1f, 1f);
                _lockedPanel = lockRt.gameObject;

                var msgRt = MakeRect(_lockedPanel.transform, "LockedMsg");
                msgRt.gameObject.AddComponent<TextMeshProUGUI>();
                msgRt.anchorMin = msgRt.anchorMax = new Vector2(0.5f, 0.5f);
                msgRt.sizeDelta        = new Vector2(600f, 80f);
                msgRt.anchoredPosition = Vector2.zero;
                _lockedMessage = msgRt.GetComponent<TMP_Text>();
                _lockedMessage.fontSize  = 18f;
                _lockedMessage.alignment = TextAlignmentOptions.Center;
                _lockedMessage.color     = new Color(0.70f, 0.75f, 0.90f);
                _lockedMessage.raycastTarget = false;

                // Close button on locked screen
                var cbtn = MakeButton(_lockedPanel.transform, "CloseBtn2",
                    new Color(0.22f, 0.28f, 0.44f, 0.90f), "\u00d7  Close", 13f, Close);
                var crt = (RectTransform)cbtn.transform;
                crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.sizeDelta        = new Vector2(100f, 36f);
                crt.anchoredPosition = new Vector2(0f, -50f);

                _lockedPanel.SetActive(false);
            }
        }
    }
}

