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
using UnityEngine.EventSystems;
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
        private Vector2         _exploreOffset = Vector2.zero;
        private bool            _isPanning;
        private Vector2         _panStartMouse;
        private Vector2         _panStartOffset;

        // ── Sector grid constants ─────────────────────────────────────────────
        // Each discovered sector is rendered as a fixed square box.
        // The grid step converts galaxy-coordinate units to pixel positions;
        // adjacent sectors (PoissonMinDist≈2.5, NeighborThreshold≈5.0) round to ±1.
        private const float SectorBoxSize   = 160f;
        private const float SectorBoxGap    = 2f;
        private const float SectorBoxStride = SectorBoxSize + SectorBoxGap;  // 162 px
        private const float GalUnitPerCell  = 3.0f;
        // Minimum separation between system dots (normalised 0–1 within the interior).
        private const float SysDotMinDist   = 0.09f;

        // Explore dots (neighbor system markers)
        private readonly List<(RectTransform dot, NeighborSystem sys)> _exploreDots
            = new List<(RectTransform, NeighborSystem)>();

        // Sector designation dot/label tracking
        private readonly List<GameObject> _sectorObjects = new List<GameObject>();
        // Currently selected sector (for detail panel)
        private SectorData _selectedSector;
        // Double-click detection for system dots (sector map layer)
        private string _dotLastClickKey  = "";
        private float  _dotLastClickTime = -10f;
        // Sector detail UI refs (assigned in EnsureCanvas)
        private TMP_Text _sectorRenamedLabel;
        private Button   _sectorRenameBtn;
        // Designation colour constants matching design spec
        private static readonly Color ColDetected  = new Color(0.306f, 0.329f, 0.439f); // fBevel #4e5470
        private static readonly Color ColVisited   = new Color(0.282f, 0.502f, 0.667f); // acc   #4880aa
        private static readonly Color ColUncharted = new Color(0.25f,  0.28f,  0.32f,  0.45f);

        // ── Header / Layer UI ────────────────────────────────────────

        private Button   _tabSystemBtn, _tabSectorBtn;
        private TMP_Text _tabSystemLbl, _tabSectorLbl;
        private TMP_Text _contextLabel;
        private GameObject _lockedPanel;
        private TMP_Text   _lockedMessage;

        // ── Static state ──────────────────────────────────────────────────

        /// <summary>Bypass all research/equipment requirements (Dev Tools). Delegates to <see cref="Waystation.Core.FeatureFlags.TelescopeMode"/>.</summary>
        public static bool TelescopeMode
        {
            get => Waystation.Core.FeatureFlags.TelescopeMode;
            set => Waystation.Core.FeatureFlags.TelescopeMode = value;
        }
        /// <summary>True while any map layer is visible — used to suppress station camera zoom.</summary>
        public static bool IsOpen        { get; private set; }
        // Lazily generated unit-circle sprite (white disc, anti-aliased)
        private static Sprite _circleSprite;

        // ── Multi-select (Sector / Galaxy layers) ─────────────────────────────
        private readonly Dictionary<string, (Image img, Color defaultCol, SectorData sector, RectTransform rt)>
            _sectorBoxRegistry = new Dictionary<string, (Image img, Color defaultCol, SectorData sector, RectTransform rt)>();
        private readonly HashSet<string> _multiSelectedUids = new HashSet<string>();
        private const float  BoxSelectThreshold = 6f;
        private bool         _isBoxSelecting;
        private Vector2      _boxSelectStartScreen;
        private GameObject   _boxSelectOverlay;

        // ── System map pan / zoom ──────────────────────────────────────────────
        private RectTransform _sysWorld;
        private float         _sysZoom   = 1f;
        private bool          _sysPanning;
        private Vector2       _sysPanStart;
        private Vector2       _sysPanOffset;
        private const float   SysZoomMin = 0.3f;
        private const float   SysZoomMax = 5f;

        // ── Public API ────────────────────────────────────────────────────────
        /// <summary>The sector the player has most recently clicked. Read by GameHUD.</summary>
        public static SectorData SelectedSector { get; private set; }
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

            // ESC: close the map immediately regardless of layer.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (_layer == MapLayer.System)
            {
                HandleSystemZoom();
                HandleSystemPan();
                return;
            }

            if (_layer != MapLayer.Sector) return;
            if (_exploreWorld == null) return;

            HandleExploreZoom();
            HandleExplorePan();
            HandleExploreDragSelect();
        }

        private void HandleExploreZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.001f) return;

            float minZ = 0.5f;
            float maxZ = 8f;

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

        }

        private void HandleExplorePan()
        {
            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(1))
            {
                _isPanning      = true;
                _panStartMouse  = mouse;
                _panStartOffset = _exploreWorld.anchoredPosition;
            }

            if (Input.GetMouseButtonUp(1))
                _isPanning = false;

            if (!_isPanning) return;
            Vector2 delta = mouse - _panStartMouse;
            _exploreWorld.anchoredPosition = _panStartOffset + delta;

        }

        // ── System map pan / zoom ─────────────────────────────────────────────

        private void HandleSystemZoom()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.001f || _sysWorld == null) return;

            float newZoom = Mathf.Clamp(_sysZoom * (1f + scroll * 0.12f), SysZoomMin, SysZoomMax);
            if (Mathf.Approximately(newZoom, _sysZoom)) return;

            // Zoom toward screen cursor position.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapContainer, Input.mousePosition, null, out Vector2 localMouse);
            float ratio = newZoom / _sysZoom;
            _sysWorld.anchoredPosition =
                localMouse + ((_sysWorld.anchoredPosition - localMouse) * ratio);
            _sysZoom = newZoom;
            _sysWorld.localScale = new Vector3(newZoom, newZoom, 1f);
        }

        private void HandleSystemPan()
        {
            if (_sysWorld == null) return;
            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(1))
            {
                _sysPanning   = true;
                _sysPanStart  = mouse;
                _sysPanOffset = _sysWorld.anchoredPosition;
            }

            if (Input.GetMouseButtonUp(1))
                _sysPanning = false;

            if (!_sysPanning) return;
            _sysWorld.anchoredPosition = _sysPanOffset + (mouse - _sysPanStart);
        }

        // ── Left-drag box select (Sector / Galaxy layers) ─────────────────────

        private void HandleExploreDragSelect()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _isBoxSelecting       = false;
                _boxSelectStartScreen = Input.mousePosition;
            }

            if (Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _boxSelectStartScreen;
                if (!_isBoxSelecting && delta.magnitude > BoxSelectThreshold)
                {
                    _isBoxSelecting = true;
                    EnsureBoxSelectOverlay();
                }
                if (_isBoxSelecting)
                    UpdateBoxSelectOverlay(_boxSelectStartScreen, Input.mousePosition);
            }

            if (Input.GetMouseButtonUp(0) && _isBoxSelecting)
            {
                FinishBoxSelect(_boxSelectStartScreen, Input.mousePosition);
                DestroyBoxSelectOverlay();
                _isBoxSelecting = false;
            }
        }

        private void EnsureBoxSelectOverlay()
        {
            if (_boxSelectOverlay != null) return;
            _boxSelectOverlay = new GameObject("BoxSelectOverlay",
                typeof(RectTransform), typeof(Image));
            _boxSelectOverlay.transform.SetParent(_mapAreaRt, false);
            _boxSelectOverlay.transform.SetAsLastSibling();
            var rt  = _boxSelectOverlay.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            var img = _boxSelectOverlay.GetComponent<Image>();
            img.color         = new Color(0.40f, 0.70f, 1.00f, 0.10f);
            img.raycastTarget = false;
            // Visible border frame
            var borderGo = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderGo.transform.SetParent(_boxSelectOverlay.transform, false);
            var borderRt  = borderGo.GetComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = Vector2.zero;
            borderRt.offsetMax = Vector2.zero;
            var borderImg = borderGo.GetComponent<Image>();
            borderImg.color         = new Color(0.45f, 0.78f, 1.00f, 0.60f);
            borderImg.raycastTarget = false;
            // Inner solid fill (thinner, covers border) to give hollow border look
            var innerGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            innerGo.transform.SetParent(_boxSelectOverlay.transform, false);
            var innerRt  = innerGo.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2( 1f,  1f);
            innerRt.offsetMax = new Vector2(-1f, -1f);
            var innerImg = innerGo.GetComponent<Image>();
            innerImg.color         = new Color(0.35f, 0.65f, 1.00f, 0.06f);
            innerImg.raycastTarget = false;
        }

        private void UpdateBoxSelectOverlay(Vector2 screenA, Vector2 screenB)
        {
            if (_boxSelectOverlay == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mapAreaRt, screenA, null, out Vector2 la);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mapAreaRt, screenB, null, out Vector2 lb);
            var rt = _boxSelectOverlay.GetComponent<RectTransform>();
            rt.anchoredPosition = (la + lb) * 0.5f;
            rt.sizeDelta        = new Vector2(Mathf.Abs(lb.x - la.x), Mathf.Abs(lb.y - la.y));
        }

        private void DestroyBoxSelectOverlay()
        {
            if (_boxSelectOverlay == null) return;
            Destroy(_boxSelectOverlay);
            _boxSelectOverlay = null;
        }

        private void FinishBoxSelect(Vector2 screenA, Vector2 screenB)
        {
            Vector2 smin = Vector2.Min(screenA, screenB);
            Vector2 smax = Vector2.Max(screenA, screenB);
            var screenRect = new Rect(smin, smax - smin);
            if (screenRect.width < 2f && screenRect.height < 2f) return;

            bool additive = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!additive) ClearMultiSelect();

            // For ScreenSpaceOverlay canvas, GetWorldCorners returns positions in screen pixels.
            var corners = new Vector3[4];
            bool anyHit = false;
            foreach (var kv in _sectorBoxRegistry)
            {
                kv.Value.rt.GetWorldCorners(corners);
                float xMin = corners[0].x, xMax = corners[0].x;
                float yMin = corners[0].y, yMax = corners[0].y;
                for (int ci = 1; ci < 4; ci++)
                {
                    if (corners[ci].x < xMin) xMin = corners[ci].x;
                    if (corners[ci].x > xMax) xMax = corners[ci].x;
                    if (corners[ci].y < yMin) yMin = corners[ci].y;
                    if (corners[ci].y > yMax) yMax = corners[ci].y;
                }
                if (screenRect.Overlaps(new Rect(xMin, yMin, xMax - xMin, yMax - yMin)))
                {
                    anyHit = true;
                    SetSectorSelected(kv.Key, true);
                }
            }

            if (!anyHit) return;
            var selected = GetSelectedSectors();
            if      (selected.Count == 1) ShowSectorDetail(selected[0]);
            else if (selected.Count  > 1) ShowMultiSectorDetail(selected);
        }

        // ── Multi-select helpers ──────────────────────────────────────────────

        private void SetSectorSelected(string uid, bool isSelected)
        {
            if (!_sectorBoxRegistry.TryGetValue(uid, out var entry)) return;
            if (isSelected)
            {
                _multiSelectedUids.Add(uid);
                entry.img.color = Color.white;
                _selectedSector = entry.sector;
                SelectedSector  = entry.sector;
            }
            else
            {
                _multiSelectedUids.Remove(uid);
                entry.img.color = entry.defaultCol;
                if (_selectedSector == entry.sector)
                {
                    _selectedSector = null;
                    SelectedSector  = null;
                }
            }
        }

        private void ToggleSectorSelected(string uid)
        {
            if (_multiSelectedUids.Contains(uid)) SetSectorSelected(uid, false);
            else                                   SetSectorSelected(uid, true);
        }

        private void ClearMultiSelect()
        {
            // Snapshot keys to avoid modifying collection while iterating.
            var keys = new List<string>(_multiSelectedUids);
            foreach (var uid in keys)
                if (_sectorBoxRegistry.TryGetValue(uid, out var entry))
                    entry.img.color = entry.defaultCol;
            _multiSelectedUids.Clear();
            _selectedSector   = null;
            SelectedSector    = null;
        }

        private List<SectorData> GetSelectedSectors()
        {
            var result = new List<SectorData>();
            foreach (var uid in _multiSelectedUids)
                if (_sectorBoxRegistry.TryGetValue(uid, out var entry))
                    result.Add(entry.sector);
            return result;
        }

        private void ShowMultiSectorDetail(List<SectorData> sectors)
        {
            if (detailPanel == null || sectors.Count == 0) return;
            detailPanel.SetActive(true);
            if (_sectorSystemsSection != null) _sectorSystemsSection.SetActive(false);

            if (detailNameLabel != null)
                detailNameLabel.text = $"{sectors.Count} Sectors Selected";

            if (detailTypeLabel != null)
            {
                var sb  = new System.Text.StringBuilder();
                int max = Mathf.Min(sectors.Count, 8);
                for (int i = 0; i < max; i++)
                {
                    var s = sectors[i];
                    sb.AppendLine(string.IsNullOrEmpty(s.properName)
                        ? s.ShortDesignation()
                        : s.properName);
                }
                if (sectors.Count > max) sb.AppendLine($"... +{sectors.Count - max} more");
                detailTypeLabel.text = sb.ToString().TrimEnd();
            }

            if (detailTagsLabel    != null) detailTagsLabel.text    = "";
            if (detailMoonsLabel   != null) detailMoonsLabel.text   = "";
            if (detailStationLabel != null) detailStationLabel.text = "";
            if (_detailViewBtn     != null) _detailViewBtn.gameObject.SetActive(false);
            if (_sectorRenameBtn   != null) _sectorRenameBtn.gameObject.SetActive(false);
            if (_sectorRenamedLabel != null) _sectorRenamedLabel.gameObject.SetActive(false);
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
            // Sector requires map-sector unlock + powered Sector Antenna.
            bool hasSector = TelescopeMode ||
                             ((_gm.Station?.HasTag("tech.map_sector") == true) && HasBuiltAntenna());

            _lockedPanel?.SetActive(false);
            mapPanel.SetActive(true);
            IsOpen = true;

            UpdateLayerTabs(hasSector);
            SwitchLayer(MapLayer.System);
        }

        public void Close()
        {
            if (mapPanel != null) mapPanel.SetActive(false);
            if (detailPanel != null) detailPanel.SetActive(false);
            IsOpen = false;
            // Clear selection when map is closed.
            SelectedSector       = null;
            _selectedSector      = null;
            _multiSelectedUids.Clear();
            _sectorBoxRegistry.Clear();
            DestroyBoxSelectOverlay();
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

            // Lazily create the panning/zooming world root inside mapContainer.
            if (_sysWorld == null)
            {
                var sysWorldGo = new GameObject("SysWorld", typeof(RectTransform));
                sysWorldGo.transform.SetParent(mapContainer, false);
                _sysWorld = sysWorldGo.GetComponent<RectTransform>();
                _sysWorld.anchorMin        = _sysWorld.anchorMax = new Vector2(0.5f, 0.5f);
                _sysWorld.sizeDelta        = new Vector2(4000f, 4000f);
                _sysWorld.anchoredPosition = Vector2.zero;
            }

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
            _starMarker = CreateCircleDot(_sysWorld, Vector2.zero,
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
                var   ring          = CreateOrbitRing(_sysWorld, ringPx, ringColor, ringThickness);
                _orbitRings.Add(ring);

                if (isBelt)
                {
                    // Belt has no single planet dot — just the wide ring.
                    _planetMarkers.Add(null);
                    _planetLabels.Add(null);

                    if (body.stationIsHere)
                        _stationMarker = CreateStationMarker(_sysWorld, ringPx);
                    continue;
                }

                // Planet dot
                float dotPx = Mathf.Clamp(body.size * 20f, 6f, 28f);
                var   dot   = CreateCircleDot(_sysWorld, Vector2.zero,
                                              dotPx, ParseColor(body.colorHex), body.name);
                AddClickHandler(dot, body);
                _planetMarkers.Add(dot);

                // Small name label beneath the dot (hidden at small dot sizes)
                var label = CreateLabel(_sysWorld, body.name, dotPx * 0.5f + 8f);
                _planetLabels.Add(label);

                if (body.stationIsHere)
                    _stationMarker = CreateStationMarker(_sysWorld, ringPx);
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
            if (_sectorSystemsSection != null) _sectorSystemsSection.SetActive(false);

            if (detailNameLabel    != null)
                detailNameLabel.text    = body.name;
            if (detailTypeLabel    != null)
                detailTypeLabel.text    = body.planetClass != PlanetClass.None
                    ? $"{BodyTypeLabel(body.bodyType)}\n{PlanetClassLabel(body.planetClass)}"
                    : BodyTypeLabel(body.bodyType);
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
            // Reset system-map pan/zoom.
            if (_sysWorld != null)
            {
                _sysWorld.anchoredPosition = Vector2.zero;
                _sysWorld.localScale       = Vector3.one;
            }
            _sysZoom    = 1f;
            _sysPanning = false;
        }

        // ── Layer switching ───────────────────────────────────────────────────

        private void SwitchLayer(MapLayer layer)
        {
            _layer = layer;
            ClearMap();
            ClearExplore();

            bool explore = (layer == MapLayer.Sector);
            if (mapContainer           != null) mapContainer.gameObject.SetActive(!explore);
            // Toggle the ExploreClip panel (parent of _exploreWorld)
            if (_exploreWorld != null)
                _exploreWorld.parent.gameObject.SetActive(explore);
            if (detailPanel            != null) detailPanel.SetActive(false);

            // Highlight active tab
            SetTabActive(_tabSystemBtn, _tabSystemLbl, layer == MapLayer.System);
            SetTabActive(_tabSectorBtn, _tabSectorLbl, layer == MapLayer.Sector);

            switch (layer)
            {
                case MapLayer.System: RebuildMap(); break;
                case MapLayer.Sector: RebuildSector(); break;
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

        private void UpdateLayerTabs(bool hasSector)
        {
            if (_tabSectorBtn != null) _tabSectorBtn.interactable = hasSector;
            if (_tabSectorLbl != null) _tabSectorLbl.color = hasSector
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
                        ? $"{_viewedSystem.systemName}  \u00b7  {StarTypeLabel(_viewedSystem.starType)}  \u00b7  scroll to zoom  \u00b7  right-drag to pan"
                        : "";
                    break;
                case MapLayer.Sector:
                    _contextLabel.text = "Sector Map  ·  scroll to zoom  ·  right-drag to pan  ·  left-drag or ctrl+click to multi-select";
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
            return _gm?.Map?.HasPoweredCompleteBuildable(station, "buildable.sector_antenna") == true;
        }

        // ── Sector map ────────────────────────────────────────────────────────
        // Shows each discovered sector as a square box arranged in a grid.
        // Within each box, procedurally generated star systems are rendered as
        // colour-coded circles spaced realistically across the interior.

        private void RebuildSector()
        {
            _exploreZoom   = 1f;
            _exploreWorld.anchoredPosition = Vector2.zero;
            _exploreWorld.localScale = Vector3.one;

            if (_sys == null) return;
            var station = _gm?.Station;
            if (station == null || station.sectors.Count == 0) return;

            // Track occupied grid cells to detect rare Poisson collisions.
            var usedCells = new HashSet<(int, int)>();

            foreach (var sector in station.sectors.Values)
            {
                bool isHome = Mathf.Approximately(sector.coordinates.x, GalaxyGenerator.HomeX) &&
                              Mathf.Approximately(sector.coordinates.y, GalaxyGenerator.HomeY);

                // Only home + detected/visited sectors are visible.
                if (!isHome && sector.discoveryState == SectorDiscoveryState.Uncharted)
                    continue;

                // Map galaxy coordinates to a grid cell.
                int col = Mathf.RoundToInt(
                    (sector.coordinates.x - GalaxyGenerator.HomeX) / GalUnitPerCell);
                int row = Mathf.RoundToInt(
                    (sector.coordinates.y - GalaxyGenerator.HomeY) / GalUnitPerCell);

                // Nudge on collision (extremely rare with PoissonMinDist=2.5).
                while (usedCells.Contains((col, row)))
                    col++;
                usedCells.Add((col, row));

                var screenPos = new Vector2(col * SectorBoxStride, row * SectorBoxStride);
                CreateSectorBox(_exploreWorld, screenPos, sector, isHome);
            }

            var plusCells = new HashSet<(int, int)>();
            int[] dx = { -1, 1,  0, 0 };
            int[] dy = {  0, 0, -1, 1 };
            foreach (var (c, r) in usedCells)
                for (int d = 0; d < 4; d++)
                {
                    int nc = c + dx[d], nr = r + dy[d];
                    if (!usedCells.Contains((nc, nr)))
                        plusCells.Add((nc, nr));
                }
            foreach (var (nc, nr) in plusCells)
            {
                var btnPos = new Vector2(nc * SectorBoxStride, nr * SectorBoxStride);
                CreateAddSectorButton(_exploreWorld, btnPos, nc, nr);
            }
        }

        /// <summary>
        /// Creates an unlock "+" button at a grid position adjacent to known sectors.
        /// </summary>
        private void CreateAddSectorButton(RectTransform parent, Vector2 pos, int col, int row)
        {
            // Ghost sector box — same footprint as a real sector, clearly clickable.
            var go = new GameObject($"AddSector_{col}_{row}",
                typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(SectorBoxSize, SectorBoxSize);
            rt.anchoredPosition = pos;
            // Dim green border (same size as real box border image).
            go.GetComponent<Image>().color = new Color(0.30f, 0.75f, 0.40f, 0.45f);
            _sectorObjects.Add(go);

            // Dark semi-transparent fill inset 2 px (mirrors CreateSectorBox fill).
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(rt, false);
            var fillRt2 = fillGo.GetComponent<RectTransform>();
            fillRt2.anchorMin = Vector2.zero;
            fillRt2.anchorMax = Vector2.one;
            fillRt2.offsetMin = new Vector2( 2f,  2f);
            fillRt2.offsetMax = new Vector2(-2f, -2f);
            fillGo.GetComponent<Image>().color = new Color(0.04f, 0.14f, 0.07f, 0.08f);

            // "+" centred in the ghost box.
            var lblGo = new GameObject("Lbl", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(rt, false);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
            var txt = lblGo.GetComponent<TMP_Text>();
            txt.text          = "+";
            txt.fontSize      = 48f;
            txt.fontStyle     = FontStyles.Bold;
            txt.alignment     = TextAlignmentOptions.Center;
            txt.color         = new Color(0.45f, 1.0f, 0.55f, 0.90f);
            txt.raycastTarget = false;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = new Color(1f, 1f, 1f, 1f);
            colors.highlightedColor = new Color(0.5f, 1.0f, 0.60f, 1.00f);
            colors.pressedColor     = new Color(0.6f, 1.0f, 0.70f, 1.00f);
            btn.colors = colors;
            int capturedCol = col, capturedRow = row;
            btn.onClick.AddListener(() => TryUnlockSector(capturedCol, capturedRow));
        }

        /// <summary>
        /// Attempts to unlock and generate a sector at the given grid cell.
        /// In TelescopeMode this bypasses exploration-point cost.
        /// </summary>
        private void TryUnlockSector(int col, int row)
        {
            var station = _gm?.Station;
            if (station == null) return;

            bool unlocked;
            if (TelescopeMode)
            {
                float tgx = GalaxyGenerator.HomeX + col * GalUnitPerCell;
                float tgy = GalaxyGenerator.HomeY + row * GalUnitPerCell;
                bool exists = false;
                foreach (var sec in station.sectors.Values)
                {
                    if (Mathf.Approximately(sec.coordinates.x, tgx) &&
                        Mathf.Approximately(sec.coordinates.y, tgy))
                    { exists = true; break; }
                }

                unlocked = false;
                if (!exists)
                {
                    var generated = GalaxyGenerator.GenerateSectorAtCoordinates(
                        station.galaxySeed, new Vector2(tgx, tgy), station);
                    generated.discoveryState = SectorDiscoveryState.Detected;
                    station.sectors[generated.uid] = generated;
                    unlocked = true;
                }
            }
            else
            {
                unlocked = _gm.Map?.TryUnlockSector(station, col, row) == true;
            }
            if (!unlocked) return;

            float gx = GalaxyGenerator.HomeX + col * GalUnitPerCell;
            float gy = GalaxyGenerator.HomeY + row * GalUnitPerCell;
            foreach (var sec in station.sectors.Values)
            {
                if (!Mathf.Approximately(sec.coordinates.x, gx) ||
                    !Mathf.Approximately(sec.coordinates.y, gy))
                    continue;
                _gm?.Factions?.OnSectorUnlocked(sec, station);
                break;
            }

            ClearExplore();
            RebuildSector();
        }

        private void CreateSectorBox(RectTransform parent, Vector2 pos,
                                     SectorData sector, bool isHome)
        {
            bool uncharted = sector.discoveryState == SectorDiscoveryState.Uncharted;
            bool visited   = sector.discoveryState == SectorDiscoveryState.Visited;

            // Determine if this sector is player-owned (home sector = player-owned).
            bool isPlayerOwned = isHome;

            // Faction primary/secondary colours (player-owned sectors only).
            Color factionPrimary   = new Color(1.00f, 0.85f, 0.00f, 0.90f);
            Color factionSecondary = new Color(0.05f, 0.15f, 0.25f, 1.00f);   // alpha ignored — overridden by fillCol
            if (isPlayerOwned && _gm?.Station != null)
            {
                if (!ColorUtility.TryParseHtmlString(_gm.Station.playerFactionColor, out factionPrimary))
                    factionPrimary = new Color(1.00f, 0.85f, 0.00f, 0.90f);
                factionPrimary.a = 0.90f;
                if (!ColorUtility.TryParseHtmlString(_gm.Station.playerFactionColorSecondary, out factionSecondary))
                    factionSecondary = new Color(0.05f, 0.15f, 0.25f, 1.00f);
                // alpha is intentionally NOT forced here — fillCol pins it to 0.08f
            }

            // Border: faction primary for owned, otherwise discovery-state colour.
            Color borderCol = isPlayerOwned
                ? factionPrimary
                : uncharted
                    ? new Color(0.25f, 0.30f, 0.40f, 0.30f)
                    : visited ? ColVisited : ColDetected;

            // Standard dark-grey fill for every sector — keeps dots visible.
            Color fillCol = new Color(0.13f, 0.13f, 0.15f, 0.92f);

            // Faction ring colour for the home star dot.
            Color factionRingCol = factionPrimary;

            // ── Outer border rectangle ────────────────────────────────────────
            var boxGo = new GameObject($"Sector_{sector.uid}",
                typeof(RectTransform), typeof(Image));
            boxGo.transform.SetParent(parent, false);
            var boxRt = boxGo.GetComponent<RectTransform>();
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta        = new Vector2(SectorBoxSize, SectorBoxSize);
            boxRt.anchoredPosition = pos;
            boxGo.GetComponent<Image>().color = borderCol;
            _sectorObjects.Add(boxGo);

            // Restore selection highlight if this sector was previously selected.
            var boxImg = boxGo.GetComponent<Image>();
            bool isPrimarySelected = SelectedSector != null && sector.uid == SelectedSector.uid;
            bool isMultiSelected   = _multiSelectedUids != null && _multiSelectedUids.Contains(sector.uid);
            if (isPrimarySelected || isMultiSelected)
            {
                boxImg.color = Color.white;  // bright white = selected
            }

            // ── Inner fill (2 px inset) ───────────────────────────────────────
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(boxRt, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2( 2f,  2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            fillGo.GetComponent<Image>().color = fillCol;

            // ── Faction corner accents (owned sectors only) ───────────────────
            // Triangles use the faction PRIMARY colour so they always match the border.
            if (isPlayerOwned)
            {
                Color accentCol = new Color(
                    factionPrimary.r, factionPrimary.g, factionPrimary.b, 0.45f);
                AddCornerAccents(fillRt, accentCol);
            }

            // ── Sector modifier badge (top-right pip) ─────────────────────────
            if (sector.modifier != SectorModifier.None)
                DrawModifierBadge(fillRt, sector.modifier);

            // ── System dots inside the box ────────────────────────────────────
            // Interior area (in fill-local coords, origin = fill bottom-left):
            //   x: [pad .. fillInnerW - pad]
            //   y: [pad .. fillInnerH - pad]
            float fillInner = SectorBoxSize - 4f;  // fill inner edge
            float interiorPadX = 8f;
            float botReserve   = 32f;  // keep dots above the footer label
            float areaW = fillInner - interiorPadX * 2f;
            float areaH = fillInner - botReserve;
            PlaceSectorSystemDots(fillRt, sector, isHome,
                interiorPadX, botReserve, areaW, areaH, factionRingCol);

            // ── Click handler ─────────────────────────────────────────────────
            // Designation / name footer (dim at rest, revealed on hover)
            string footerLine = string.IsNullOrEmpty(sector.properName)
                ? sector.ShortCodeAndCoord()
                : $"{sector.properName}\n<size=6.5><color=#7799bb>{sector.ShortCodeAndCoord()}</color></size>";
            var footGo = new GameObject("SectorFooter",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            footGo.transform.SetParent(fillRt, false);
            var footRt = footGo.GetComponent<RectTransform>();
            footRt.anchorMin        = new Vector2(0f, 0f);
            footRt.anchorMax        = new Vector2(1f, 0f);
            footRt.pivot            = new Vector2(0.5f, 0f);
            footRt.sizeDelta        = new Vector2(0f, 36f);
            footRt.anchoredPosition = new Vector2(0f, 2f);
            var footTxt = footGo.GetComponent<TMP_Text>();
            footTxt.text          = footerLine;
            footTxt.fontSize      = 7.5f;
            footTxt.alignment     = TextAlignmentOptions.Center;
            footTxt.color         = new Color(0.70f, 0.82f, 1.00f, 0.28f);
            footTxt.raycastTarget = false;

            var btn      = boxGo.AddComponent<Button>();
            var capturedSector     = sector;
            var capturedImg        = boxGo.GetComponent<Image>();
            var capturedDefaultCol = borderCol;
            // Register for box-select intersection tests and selection management.
            _sectorBoxRegistry[sector.uid] = (capturedImg, capturedDefaultCol, capturedSector, boxRt);
            btn.onClick.AddListener(() =>
            {
                // Skip if the mouse moved far enough to be a drag gesture.
                if (Vector2.Distance(_boxSelectStartScreen, Input.mousePosition) > BoxSelectThreshold)
                    return;
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl)
                    ToggleSectorSelected(capturedSector.uid);
                else
                {
                    ClearMultiSelect();
                    SetSectorSelected(capturedSector.uid, true);
                }
                var sel = GetSelectedSectors();
                if      (sel.Count == 1) ShowSectorDetail(sel[0]);
                else if (sel.Count  > 1) ShowMultiSectorDetail(sel);
            });

            var trigger    = boxGo.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            var exitEntry  = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            enterEntry.callback.AddListener(_ => footTxt.color = new Color(0.85f, 0.93f, 1.00f, 0.95f));
            exitEntry.callback.AddListener(_ =>  footTxt.color = new Color(0.70f, 0.82f, 1.00f, 0.28f));
            trigger.triggers.Add(enterEntry);
            trigger.triggers.Add(exitEntry);
        }

        // ── Modifier badge ────────────────────────────────────────────────────
        // A small coloured pip + 2-char label placed in the top-right of the fill rect.
        // Physical = cold blue-grey, Resource = amber, Political = red.

        // Explicit mapping from SectorModifier to badge (label, colour hex).
        // Using a Dictionary keyed by enum value prevents silent mis-mapping if the
        // SectorModifier enum is ever reordered or extended.
        private static readonly Dictionary<SectorModifier, (string label, string hex)> ModifierBadgeMap =
            new Dictionary<SectorModifier, (string label, string hex)>
            {
                // Physical
                { SectorModifier.Nebula,                    ("NB", "#7AAED6") },
                { SectorModifier.AsteroidBelt,              ("AB", "#7AAED6") },
                { SectorModifier.DustCloud,                 ("DC", "#7AAED6") },
                { SectorModifier.PlanetaryRingDebris,       ("RD", "#7AAED6") },
                { SectorModifier.CometaryTail,              ("CT", "#7AAED6") },
                { SectorModifier.AccretionDisk,             ("AD", "#7AAED6") },
                { SectorModifier.PulsarWash,                ("PW", "#A0D6E8") },
                { SectorModifier.MagnetarField,             ("MF", "#A0D6E8") },
                { SectorModifier.GravitationalLens,         ("GL", "#A0D6E8") },
                { SectorModifier.GravityWell,               ("GW", "#A0D6E8") },
                { SectorModifier.TidalShearZone,            ("TS", "#A0D6E8") },
                { SectorModifier.CosmicRaySurge,            ("CR", "#A0D6E8") },
                { SectorModifier.RadiationBelt,             ("RB", "#A0D6E8") },
                { SectorModifier.DarkMatterFilament,        ("DM", "#A0D6E8") },
                { SectorModifier.FrameDraggingAnomaly,      ("FA", "#A0D6E8") },
                { SectorModifier.GravitationalTimeDilation, ("GT", "#A0D6E8") },
                { SectorModifier.EinsteinRosenRemnant,      ("ER", "#A0D6E8") },
                { SectorModifier.QuantumFoamPocket,         ("QF", "#A0D6E8") },
                { SectorModifier.HawkingRadiationZone,      ("HR", "#A0D6E8") },
                // Resource
                { SectorModifier.RichOreDeposit,            ("OR", "#E8C060") },
                { SectorModifier.IceField,                  ("IF", "#E8C060") },
                { SectorModifier.GasPocket,                 ("GP", "#E8C060") },
                { SectorModifier.SalvageGraveyard,          ("SG", "#E8C060") },
                { SectorModifier.DerelictStation,           ("DS", "#E8C060") },
                { SectorModifier.AncientRuins,              ("AR", "#E8C060") },
                { SectorModifier.BiologicalBloom,           ("BB", "#E8C060") },
                // Political
                { SectorModifier.ContestedSpace,            ("CS", "#D06060") },
                { SectorModifier.ExclusionZone,             ("EZ", "#D06060") },
                { SectorModifier.QuarantineSeal,            ("QS", "#D06060") },
                { SectorModifier.PatrolRoute,               ("PR", "#D06060") },
            };

        private static (string label, string hex) GetModifierBadge(SectorModifier m)
        {
            if (ModifierBadgeMap.TryGetValue(m, out var badge)) return badge;
            return ("??", "#888888");
        }

        private static void DrawModifierBadge(RectTransform fillRt, SectorModifier modifier)
        {
            var (label, hex) = GetModifierBadge(modifier);
            Color col = ParseColor(hex);

            const float pipSize = 13f;
            const float pad     = 4f;

            // Pip (tiny square) anchored to top-right, inset by pad.
            var pipGo = new GameObject("ModBadgePip", typeof(RectTransform), typeof(Image));
            pipGo.transform.SetParent(fillRt, false);
            var pipRt = pipGo.GetComponent<RectTransform>();
            pipRt.anchorMin        = Vector2.one;
            pipRt.anchorMax        = Vector2.one;
            pipRt.pivot            = Vector2.one;
            pipRt.sizeDelta        = new Vector2(pipSize, pipSize);
            pipRt.anchoredPosition = new Vector2(-pad, -pad);
            var pipImg = pipGo.GetComponent<Image>();
            pipImg.color         = new Color(col.r, col.g, col.b, 0.80f);
            pipImg.raycastTarget = false;

            // 2-char label centred on the pip.
            var lblGo = new GameObject("ModBadgeLbl",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(pipGo.GetComponent<RectTransform>(), false);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            var lbl = lblGo.GetComponent<TMP_Text>();
            lbl.text          = label;
            lbl.fontSize      = 6f;
            lbl.alignment     = TextAlignmentOptions.Center;
            lbl.color         = new Color(0f, 0f, 0f, 0.90f);
            lbl.raycastTarget = false;
        }

        // ── Planet-class dot colour ───────────────────────────────────────────
        // Returns a hex string for a non-home dot based on body type when available.

        private static string PlanetClassDotColor(BodyType bodyType)
        {
            return bodyType switch
            {
                BodyType.RockyPlanet  => "#CC9977",   // warm tan
                BodyType.GasGiant     => "#AABB88",   // olive-green
                BodyType.IcePlanet    => "#99BBDD",   // icy blue
                BodyType.AsteroidBelt => "#887766",   // dull brown
                _                     => "#D0C8A8",   // default cream
            };
        }

        /// <summary>
        /// Adds four right-angle triangle accents at the corners of <paramref name="parent"/>.
        /// Each triangle has its right-angle at the corresponding corner and its hypotenuse
        /// cutting diagonally inward, giving a tapered corner-flag effect.
        /// They sit in the hierarchy before system dots so dots always render on top.
        /// </summary>
        private static void AddCornerAccents(RectTransform parent, Color col, float size = 12f, float pad = 2f)
        {
            // Each triangle's RectTransform is anchored to its corner and inset
            // `pad` pixels from each border wall, with legs of length `size`.
            var defs = new (Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, CornerTriangle.Corner c)[]
            {
                (Vector2.zero,  Vector2.zero,  new Vector2(       pad,        pad), new Vector2( size+pad,  size+pad), CornerTriangle.Corner.BottomLeft),
                (Vector2.right, Vector2.right, new Vector2(-size-pad,        pad), new Vector2(      -pad,  size+pad), CornerTriangle.Corner.BottomRight),
                (Vector2.up,    Vector2.up,    new Vector2(       pad, -size-pad), new Vector2( size+pad,       -pad), CornerTriangle.Corner.TopLeft),
                (Vector2.one,   Vector2.one,   new Vector2(-size-pad, -size-pad), new Vector2(      -pad,       -pad), CornerTriangle.Corner.TopRight),
            };
            string[] names = { "CornerBL", "CornerBR", "CornerTL", "CornerTR" };
            for (int i = 0; i < 4; i++)
            {
                var go  = new GameObject(names[i], typeof(RectTransform), typeof(CornerTriangle));
                go.transform.SetParent(parent, false);
                var rt  = go.GetComponent<RectTransform>();
                rt.anchorMin = defs[i].amin;
                rt.anchorMax = defs[i].amax;
                rt.offsetMin = defs[i].omin;
                rt.offsetMax = defs[i].omax;
                var tri = go.GetComponent<CornerTriangle>();
                tri.corner        = defs[i].c;
                tri.color         = col;
                tri.raycastTarget = false;
            }
        }

        /// <summary>
        /// Places procedurally generated star system dots inside one sector box.
        /// Positions are deterministic (seeded from sector uid) and respect a
        /// minimum separation so dots never overlap.
        /// </summary>
        private void PlaceSectorSystemDots(
            RectTransform fillRt, SectorData sector, bool isHome,
            float areaX, float areaY, float areaW, float areaH,
            Color factionRingCol)
        {
            int seed = SolarSystemGenerator.StableHash(sector.uid);
            var rng  = new System.Random(seed);

            var positions  = new List<Vector2>();
            var sizes      = new List<float>();
            var colorHexes = new List<string>();

            // Home star placed first — near centre with slight randomised jitter.
            if (isHome && _sys != null)
            {
                positions.Add(new Vector2(
                    0.42f + (float)rng.NextDouble() * 0.16f,
                    0.42f + (float)rng.NextDouble() * 0.16f));
                sizes.Add(Mathf.Clamp(_sys.starSize * 10f, 8f, 16f));
                colorHexes.Add(_sys.starColorHex);
            }

            // Target dot count driven by sector density tier.
            int targetCount = sector.systemDensity switch
            {
                SystemDensity.Sparse   => rng.Next(3,  7),
                SystemDensity.Low      => rng.Next(7, 11),
                SystemDensity.Standard => rng.Next(11, 16),
                SystemDensity.High     => rng.Next(16, 21),
                _                      => rng.Next(11, 16),
            };

            // Remaining systems: random positions with minimum separation.
            int attempts    = 0;
            while (positions.Count < targetCount && attempts < 600)
            {
                attempts++;
                float nx = 0.06f + (float)rng.NextDouble() * 0.88f;
                float ny = 0.06f + (float)rng.NextDouble() * 0.88f;
                bool  tooClose = false;
                foreach (var p in positions)
                {
                    float ddx = p.x - nx, ddy = p.y - ny;
                    if (ddx * ddx + ddy * ddy < SysDotMinDist * SysDotMinDist)
                    { tooClose = true; break; }
                }
                if (tooClose) continue;
                positions.Add(new Vector2(nx, ny));
                sizes.Add(2.5f + (float)rng.NextDouble() * 4.0f);

                // Use nx as a loose zone proxy (inner = rocky/hot, mid = gas, outer = ice)
                // to give sector-map dots planet-class flavour without storing full body data.
                BodyType dotType;
                double btRoll = rng.NextDouble();
                if (nx < 0.35f)
                    dotType = BodyType.RockyPlanet;
                else if (nx < 0.65f)
                    dotType = btRoll < 0.55 ? BodyType.RockyPlanet : BodyType.GasGiant;
                else
                    dotType = btRoll < 0.45 ? BodyType.GasGiant : BodyType.IcePlanet;

                colorHexes.Add(PlanetClassDotColor(dotType));
            }

            // Place each dot.
            for (int i = 0; i < positions.Count; i++)
            {
                float px  = areaX + positions[i].x * areaW;
                float py  = areaY + positions[i].y * areaH;
                float sz  = sizes[i];
                Color col = ParseColor(colorHexes[i]);
                col.a = (i == 0 && isHome) ? 1.00f : 0.85f;

                var dotGo = new GameObject($"Sys_{i}",
                    typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(fillRt, false);
                var dotRt = dotGo.GetComponent<RectTransform>();
                // anchor at fill's bottom-left; anchoredPosition = px,py from that corner
                dotRt.anchorMin = dotRt.anchorMax = Vector2.zero;
                dotRt.pivot           = new Vector2(0.5f, 0.5f);
                dotRt.sizeDelta       = new Vector2(sz, sz);
                dotRt.anchoredPosition = new Vector2(px, py);
                var img = dotGo.GetComponent<Image>();
                img.sprite = GetCircleSprite();
                img.color  = col;

                // Double-click on any dot in a visible (non-uncharted) sector or in
                // Telescope Mode switches to the System layer.
                bool allowDblClick = TelescopeMode ||
                                     sector.discoveryState != SectorDiscoveryState.Uncharted;
                img.raycastTarget = allowDblClick;

                // Deterministic star name for this dot — used in the hover tooltip.
                string dotStarName = (isHome && i == 0)
                    ? (_sys?.starName ?? "")
                    : SolarSystemGenerator.GetStarNameForSeed(
                        SolarSystemGenerator.StableHash(sector.uid + $"_sys_{i}"));

                if (allowDblClick)
                {
                    // Use Transition.None so the Button never modifies the dot's color.
                    var dotBtn          = dotGo.AddComponent<Button>();
                    dotBtn.transition   = Selectable.Transition.None;
                    string dotKey = $"{sector.uid}_{i}";
                    dotBtn.onClick.AddListener(() =>
                    {
                        float now = Time.unscaledTime;
                        if (_dotLastClickKey == dotKey &&
                            now - _dotLastClickTime < 0.35f)
                        {
                            SwitchLayer(MapLayer.System);
                        }
                        _dotLastClickKey  = dotKey;
                        _dotLastClickTime = now;
                    });

                    // White hover-ring — visible on mouse-over via EventTrigger.
                    var hoverRingGo = new GameObject("HoverRing",
                        typeof(RectTransform), typeof(UIRing));
                    hoverRingGo.transform.SetParent(dotGo.transform, false);
                    var hrRt = hoverRingGo.GetComponent<RectTransform>();
                    hrRt.anchorMin = hrRt.anchorMax = new Vector2(0.5f, 0.5f);
                    hrRt.pivot            = new Vector2(0.5f, 0.5f);
                    hrRt.sizeDelta        = new Vector2(sz + 6f, sz + 6f);
                    hrRt.anchoredPosition = Vector2.zero;
                    var hr = hoverRingGo.GetComponent<UIRing>();
                    hr.color         = new Color(1f, 1f, 1f, 0.90f);
                    hr.thickness     = 0.22f;
                    hr.segments      = 32;
                    hr.raycastTarget = false;
                    hoverRingGo.SetActive(false);

                    var trig      = dotGo.AddComponent<EventTrigger>();
                    var captHvr   = hoverRingGo;
                    var captName  = dotStarName;
                    var enterEvt  = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    var exitEvt   = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    enterEvt.callback.AddListener(_ =>
                    {
                        captHvr.SetActive(true);
                        if (_mapHoverLabel != null)
                        {
                            _mapHoverLabel.text = captName;
                            _mapHoverLabel.gameObject.SetActive(true);
                        }
                    });
                    exitEvt.callback.AddListener(_ =>
                    {
                        captHvr.SetActive(false);
                        if (_mapHoverLabel != null)
                            _mapHoverLabel.gameObject.SetActive(false);
                    });
                    trig.triggers.Add(enterEvt);
                    trig.triggers.Add(exitEvt);
                }

                // Home star gets a faction-coloured glow ring.
                if (i == 0 && isHome)
                {
                    var ringGo = new GameObject("HomeRing",
                        typeof(RectTransform), typeof(UIRing));
                    ringGo.transform.SetParent(fillRt, false);
                    var rrt = ringGo.GetComponent<RectTransform>();
                    rrt.anchorMin = rrt.anchorMax = Vector2.zero;
                    rrt.pivot           = new Vector2(0.5f, 0.5f);
                    rrt.sizeDelta       = new Vector2(sz + 10f, sz + 10f);
                    rrt.anchoredPosition = new Vector2(px, py);
                    var ur = ringGo.GetComponent<UIRing>();
                    ur.color        = new Color(factionRingCol.r, factionRingCol.g,
                                               factionRingCol.b, 0.85f);
                    ur.thickness    = 0.22f;
                    ur.segments     = 32;
                    ur.raycastTarget = false;
                }
            }
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
            SelectedSector  = sector;
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
                        detailTypeLabel.text = sector.CodeOnlyDesignation();
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
                    bool isHomeSector =
                        Mathf.Approximately(sector.coordinates.x, GalaxyGenerator.HomeX) &&
                        Mathf.Approximately(sector.coordinates.y, GalaxyGenerator.HomeY);
                    bool vagueOnly = !isHomeSector;
                    if (vagueOnly && _gm?.Map != null)
                    {
                        var systems = SolarSystemGenerator.GenerateSectorSystems(sector, false, _sys);
                        foreach (var sys in systems)
                        {
                            if (_gm.Map.IsSystemCharted(_gm.Station, sys.seed))
                            {
                                vagueOnly = false;
                                break;
                            }
                        }
                    }
                    if (vagueOnly)
                    {
                        var profile = GetVagueResourceProfile(sector);
                        detailTagsLabel.text = $"Resource profile: {string.Join(" · ", profile)}";
                    }
                    else
                    {
                        var phenomParts = new System.Collections.Generic.List<string>();
                        foreach (var code in sector.phenomenonCodes)
                            phenomParts.Add(PhenomenonCodeLabel(code));
                        string densityStr = sector.systemDensity switch
                        {
                            SystemDensity.Sparse   => "3–6 systems (Sparse)",
                            SystemDensity.Low      => "7–10 systems (Low)",
                            SystemDensity.Standard => "11–15 systems (Standard)",
                            SystemDensity.High     => "16–20 systems (High)",
                            _                      => "",
                        };
                        detailTagsLabel.text = string.Join(" · ", phenomParts)
                            + (densityStr.Length > 0 ? $"\n{densityStr}" : "");
                    }
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

            // Populate systems scroll list for known sectors.
            bool canShowSystems = sector.discoveryState != SectorDiscoveryState.Uncharted;
            if (_sectorSystemsSection != null)
                _sectorSystemsSection.SetActive(canShowSystems);
            if (canShowSystems)
                PopulateSectorSystemsList(sector);
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
        }

        // ── Sector systems list ───────────────────────────────────────────────

        private void PopulateSectorSystemsList(SectorData sector)
        {
            if (_sectorSystemsList == null) return;

            // Remove old entries.
            foreach (Transform child in _sectorSystemsList)
                Destroy(child.gameObject);

            bool isHomeSector =
                Mathf.Approximately(sector.coordinates.x, GalaxyGenerator.HomeX) &&
                Mathf.Approximately(sector.coordinates.y, GalaxyGenerator.HomeY);

            var systems = SolarSystemGenerator.GenerateSectorSystems(sector, isHomeSector, _sys);
            bool anyCharted = isHomeSector;

            for (int idx = 0; idx < systems.Count; idx++)
            {
                var sys         = systems[idx];
                bool isThisHome = isHomeSector && idx == 0;
                bool isCharted = isThisHome || (_gm?.Map?.IsSystemCharted(_gm.Station, sys.seed) == true);
                if (isCharted) anyCharted = true;

                // Row background
                var rowGo = new GameObject($"SysRow_{idx}", typeof(RectTransform), typeof(Image));
                rowGo.transform.SetParent(_sectorSystemsList, false);
                rowGo.GetComponent<Image>().color = new Color(0.10f, 0.13f, 0.20f, 0.80f);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = 26f;
                le.minHeight       = 26f;

                // Star colour dot
                var dotGo = new GameObject("StarDot", typeof(RectTransform), typeof(Image));
                dotGo.transform.SetParent(rowGo.transform, false);
                var dotRt = dotGo.GetComponent<RectTransform>();
                dotRt.anchorMin        = new Vector2(0f, 0.5f);
                dotRt.anchorMax        = new Vector2(0f, 0.5f);
                dotRt.pivot            = new Vector2(0.5f, 0.5f);
                dotRt.sizeDelta        = new Vector2(8f, 8f);
                dotRt.anchoredPosition = new Vector2(14f, 0f);
                var dotImg = dotGo.GetComponent<Image>();
                dotImg.color  = ParseColor(sys.starColorHex);
                dotImg.sprite = GetCircleSprite();

                // System name + star type
                var lblGo = new GameObject("SysLbl", typeof(RectTransform), typeof(TextMeshProUGUI));
                lblGo.transform.SetParent(rowGo.transform, false);
                var lblRt = lblGo.GetComponent<RectTransform>();
                lblRt.anchorMin = new Vector2(0f, 0f);
                lblRt.anchorMax = new Vector2(1f, 1f);
                lblRt.offsetMin = new Vector2(26f, 0f);
                lblRt.offsetMax = new Vector2(-62f, 0f);
                var lbl = lblGo.GetComponent<TMP_Text>();
                lbl.text         = isCharted
                    ? sys.systemName + (isThisHome ? "  \u2605" : "")
                    : $"Uncharted System {idx + 1}";
                lbl.fontSize     = 11f;
                lbl.alignment    = TextAlignmentOptions.Left;
                lbl.color        = new Color(0.82f, 0.90f, 1.00f);
                lbl.overflowMode = TextOverflowModes.Ellipsis;
                lbl.raycastTarget = false;

                // View button
                var btnGo = new GameObject("ViewBtn", typeof(RectTransform), typeof(Image));
                btnGo.transform.SetParent(rowGo.transform, false);
                var btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.anchorMin        = new Vector2(1f, 0.5f);
                btnRt.anchorMax        = new Vector2(1f, 0.5f);
                btnRt.pivot            = new Vector2(1f, 0.5f);
                btnRt.sizeDelta        = new Vector2(54f, 20f);
                btnRt.anchoredPosition = new Vector2(-4f, 0f);
                btnGo.GetComponent<Image>().color = new Color(0.14f, 0.36f, 0.65f, 0.90f);

                var btnLblGo = new GameObject("Lbl", typeof(RectTransform), typeof(TextMeshProUGUI));
                btnLblGo.transform.SetParent(btnGo.transform, false);
                var btnLblRt = btnLblGo.GetComponent<RectTransform>();
                btnLblRt.anchorMin = Vector2.zero;
                btnLblRt.anchorMax = Vector2.one;
                btnLblRt.offsetMin = Vector2.zero;
                btnLblRt.offsetMax = Vector2.zero;
                var btnLbl = btnLblGo.GetComponent<TMP_Text>();
                btnLbl.text      = isCharted ? "View \u2192" : "Install Chip";
                btnLbl.fontSize  = 9f;
                btnLbl.alignment = TextAlignmentOptions.Center;
                btnLbl.color     = new Color(0.85f, 0.92f, 1f);
                btnLbl.raycastTarget = false;

                var btn       = btnGo.AddComponent<Button>();
                btn.interactable = isCharted;
                if (isCharted)
                {
                    var capSys    = sys;
                    var capIsHome = isThisHome;
                    btn.onClick.AddListener(() => OpenSystemFromSector(capSys, capIsHome));
                }
            }

            if (anyCharted)
                detailTypeLabel.text = sector.CodeOnlyDesignation();

            // Reset scroll to top
            if (_sectorSystemsScrollRect != null)
                _sectorSystemsScrollRect.verticalNormalizedPosition = 1f;
        }

        private static List<string> GetVagueResourceProfile(SectorData sector)
        {
            var scores = new Dictionary<string, float>
            {
                { "Ore", 0f },
                { "Ice", 0f },
                { "Gas", 0f },
                { "Salvage", 0f },
                { "Anomaly", 0f },
            };

            foreach (var code in sector.phenomenonCodes)
            {
                switch (code)
                {
                    case PhenomenonCode.OR: scores["Ore"] += 3f; break;
                    case PhenomenonCode.IC: scores["Ice"] += 3f; break;
                    case PhenomenonCode.GS: scores["Gas"] += 3f; break;
                    case PhenomenonCode.VD: scores["Salvage"] += 2f; break;
                    case PhenomenonCode.NB:
                    case PhenomenonCode.BH:
                    case PhenomenonCode.PL: scores["Anomaly"] += 2f; break;
                }
            }

            switch (sector.modifier)
            {
                case SectorModifier.RichOreDeposit: scores["Ore"] += 3f; break;
                case SectorModifier.IceField: scores["Ice"] += 3f; break;
                case SectorModifier.GasPocket: scores["Gas"] += 3f; break;
                case SectorModifier.SalvageGraveyard:
                case SectorModifier.DerelictStation: scores["Salvage"] += 3f; break;
                case SectorModifier.AncientRuins:
                case SectorModifier.DarkMatterFilament: scores["Anomaly"] += 3f; break;
            }

            var ranked = new List<KeyValuePair<string, float>>(scores);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
            return new List<string> { ranked[0].Key, ranked[1].Key, ranked[2].Key };
        }

        private void OpenSystemFromSector(NeighborSystem sys, bool isHome)
        {
            _viewedSystem = isHome && _sys != null
                ? _sys
                : SolarSystemGenerator.Generate(sys.systemName, sys.seed);
            SwitchLayer(MapLayer.System);

            // SwitchLayer hides the detail panel — re-show it with system summary.
            if (detailPanel == null) return;
            detailPanel.SetActive(true);
            if (_sectorSystemsSection != null) _sectorSystemsSection.SetActive(false);
            if (detailNameLabel    != null) detailNameLabel.text    = sys.systemName;
            if (detailTypeLabel    != null) detailTypeLabel.text    = StarTypeLabel(sys.starType)
                + $"\n{_viewedSystem.bodies.Count} bod{(_viewedSystem.bodies.Count != 1 ? "ies" : "y")} \u00b7 click a body for details";
            if (detailTagsLabel    != null) detailTagsLabel.text    = isHome ? "Your station orbits here" : "";
            if (detailMoonsLabel   != null) detailMoonsLabel.text   = "";
            if (detailStationLabel != null) detailStationLabel.text = "";
            if (_detailViewBtn     != null) _detailViewBtn.gameObject.SetActive(false);
        }

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
            if (_sectorSystemsSection != null) _sectorSystemsSection.SetActive(false);
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
            if (_sectorSystemsSection != null) _sectorSystemsSection.SetActive(false);
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
        private Button        _detailViewBtn;
        // Sector systems scroll section (right panel, shown when a sector is selected)
        private RectTransform _sectorSystemsList;
        private GameObject    _sectorSystemsSection;
        private ScrollRect    _sectorSystemsScrollRect;
        private TMP_Text      _mapHoverLabel;           // shown at map bottom on dot hover
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
            _sectorBoxRegistry.Clear();
            _multiSelectedUids.Clear();
            DestroyBoxSelectOverlay();
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

        private static string PlanetClassLabel(PlanetClass cls) => cls switch
        {
            PlanetClass.T1_BarrenRock    => "Barren Rock",
            PlanetClass.T2_Volcanic      => "Volcanic",
            PlanetClass.T3_Desert        => "Desert World",
            PlanetClass.T4_Tectonic      => "Tectonic World",
            PlanetClass.T5_Oceanic       => "Oceanic World",
            PlanetClass.T6_Terran        => "Terran World",
            PlanetClass.T7_Frozen        => "Frozen World",
            PlanetClass.G1_AmmoniaCloud  => "Ammonia Cloud (Class I)",
            PlanetClass.G2_WaterCloud    => "Water Cloud (Class II)",
            PlanetClass.G3_Cloudless     => "Cloudless (Class III)",
            PlanetClass.G4_AlkaliMetal   => "Alkali Metal (Class IV)",
            PlanetClass.G5_SilicateCloud => "Silicate Cloud (Class V)",
            PlanetClass.I1_IceDwarf      => "Ice Dwarf",
            PlanetClass.I2_CryogenicMoon => "Cryogenic Body",
            PlanetClass.I3_CometaryBody  => "Cometary Body",
            PlanetClass.E1_Chthonian     => "Chthonian (Exotic)",
            PlanetClass.E2_CarbonPlanet  => "Carbon Planet (Exotic)",
            PlanetClass.E3_IronPlanet    => "Iron Planet (Exotic)",
            PlanetClass.E4_HeliumPlanet  => "Helium Planet (Exotic)",
            PlanetClass.E5_RogueBody     => "Rogue Body (Exotic)",
            _                            => cls.ToString(),
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
            SetupTab(ref _tabSystemBtn, ref _tabSystemLbl, headerRt, "⊙  System", 0f,    0.25f, MapLayer.System);
            SetupTab(ref _tabSectorBtn, ref _tabSectorLbl, headerRt, "◈  Sector", 0.25f, 0.50f, MapLayer.Sector);
            // Galaxy tab removed — sector layer now serves as the surrounding-sectors view.

            //   Context label (spans from 50 % to 90 % of header width)
            {
                var rt = MakeLabel(headerRt, "ContextLabel", 10f,
                    TextAlignmentOptions.Center, new Color(0.72f, 0.82f, 1.00f));
                Stretch(rt.rectTransform, 0.50f, 0f, 0.90f, 1f, 8f, 0f, -8f, 0f);
                rt.overflowMode = TextOverflowModes.Ellipsis;
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

            //   Explore container (fills mapArea, used for Sector)
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
                clipRt.gameObject.SetActive(false);  // hidden until Sector layer
                // Store reference via exploring container being the parent
                // (we toggle clipRt, not _exploreWorld directly)
                // Re-wire: keep reference to clip root via _exploreWorld.parent
            }

            // ── Hover label (bottom of map area, shown when hovering a system dot) ───────
            {
                var hl = MakeLabel(_mapAreaRt, "MapHoverLabel", 11f,
                    TextAlignmentOptions.Center, new Color(0.90f, 0.95f, 1.00f, 0.90f));
                hl.rectTransform.anchorMin        = new Vector2(0f, 0f);
                hl.rectTransform.anchorMax        = new Vector2(1f, 0f);
                hl.rectTransform.pivot            = new Vector2(0.5f, 0f);
                hl.rectTransform.sizeDelta        = new Vector2(0f, 30f);
                hl.rectTransform.anchoredPosition = new Vector2(0f, 8f);
                hl.raycastTarget  = false;
                hl.gameObject.SetActive(false);
                _mapHoverLabel = hl;
            }

            // ── Detail sidebar (right 32 %, below header) ─────────────────────
            var detailBgRt = MakeImage(mapPanel.transform, "DetailPanel",
                new Color(0.06f, 0.09f, 0.18f, 0.95f));
            Stretch(detailBgRt, 0.68f, 0f, 1f, 1f, 6f, 0f, 0f, -56f);
            detailPanel = detailBgRt.gameObject;
            detailPanel.SetActive(false);

            float dy = -16f;
            // ── Info Panel title + divider ────────────────────────────────────
            {
                var titleRt = MakeRect(detailBgRt, "InfoPanelTitle");
                titleRt.gameObject.AddComponent<TextMeshProUGUI>();
                titleRt.anchorMin = new Vector2(0f, 1f);
                titleRt.anchorMax = new Vector2(1f, 1f);
                titleRt.pivot     = new Vector2(0.5f, 1f);
                titleRt.sizeDelta        = new Vector2(0f, 20f);
                titleRt.anchoredPosition = new Vector2(0f, -8f);
                var titleTxt = titleRt.GetComponent<TMP_Text>();
                titleTxt.text      = "INFO PANEL";
                titleTxt.fontSize  = 10f;
                titleTxt.alignment = TextAlignmentOptions.Center;
                titleTxt.color     = new Color(0.45f, 0.55f, 0.72f, 0.80f);
                titleTxt.fontStyle = FontStyles.Bold;
                titleTxt.raycastTarget = false;

                var divRt = MakeImage(detailBgRt, "InfoPanelDivider",
                    new Color(0.22f, 0.32f, 0.50f, 0.40f));
                divRt.anchorMin = new Vector2(0f, 1f);
                divRt.anchorMax = new Vector2(1f, 1f);
                divRt.pivot     = new Vector2(0.5f, 1f);
                divRt.sizeDelta        = new Vector2(-16f, 1f);
                divRt.anchoredPosition = new Vector2(0f, -30f);
            }
            dy = -38f; // start labels below title + divider

            TMP_Text AddDetailLabel(string name, float size, bool bold, Color col, float heightOverride = 0f)
            {
                var rt = MakeRect(detailBgRt, name);
                rt.gameObject.AddComponent<TextMeshProUGUI>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                float h      = heightOverride > 0f ? heightOverride : size + 8f;
                rt.sizeDelta        = new Vector2(0f, h);
                rt.anchoredPosition = new Vector2(0f, dy);
                var t = rt.GetComponent<TMP_Text>();
                t.fontSize  = size;
                t.alignment = TextAlignmentOptions.TopLeft;
                t.color     = col;
                t.margin    = new Vector4(14f, 4f, 8f, 0f);
                t.raycastTarget = false;
                if (bold) t.fontStyle = FontStyles.Bold;
                dy -= h + 6f;
                return t;
            }
            detailNameLabel    = AddDetailLabel("BodyName",    18f, true,  new Color(0.92f, 0.96f, 1.00f), 34f);
            detailTypeLabel    = AddDetailLabel("BodyType",    13f, false, new Color(0.62f, 0.72f, 0.90f), 44f);
            detailTagsLabel    = AddDetailLabel("BodyTags",    11f, false, new Color(0.55f, 0.65f, 0.78f), 50f);
            detailMoonsLabel   = AddDetailLabel("BodyMoons",   11f, false, new Color(0.55f, 0.65f, 0.78f), 24f);
            detailStationLabel = AddDetailLabel("BodyStation", 11f, false, new Color(0.30f, 1.00f, 0.55f), 24f);
            // Allow long names / multi-tags to display without clipping
            detailNameLabel.overflowMode    = TextOverflowModes.Ellipsis;
            detailTypeLabel.overflowMode    = TextOverflowModes.Ellipsis;
            detailTagsLabel.textWrappingMode = TextWrappingModes.Normal;
            detailMoonsLabel.overflowMode   = TextOverflowModes.Ellipsis;
            detailStationLabel.overflowMode = TextOverflowModes.Ellipsis;

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

            // ── Sector systems scroll section ─────────────────────────────────
            {
                float sectTop = dy - 16f - 10f;  // below RenamedIndicator (16 px) + gap

                // Container — stretches from sectTop down to near the bottom of the panel
                var secRt = MakeRect(detailBgRt, "SystemsSection");
                _sectorSystemsSection = secRt.gameObject;
                secRt.anchorMin = new Vector2(0f, 0f);
                secRt.anchorMax = new Vector2(1f, 1f);
                secRt.offsetMin = new Vector2(6f, 8f);
                secRt.offsetMax = new Vector2(-6f, sectTop);
                _sectorSystemsSection.SetActive(false);

                // "SYSTEMS" sub-header
                var hdrRt = MakeRect(secRt, "SysListHeader");
                hdrRt.gameObject.AddComponent<TextMeshProUGUI>();
                hdrRt.anchorMin = new Vector2(0f, 1f);
                hdrRt.anchorMax = new Vector2(1f, 1f);
                hdrRt.pivot            = new Vector2(0.5f, 1f);
                hdrRt.sizeDelta        = new Vector2(0f, 18f);
                hdrRt.anchoredPosition = Vector2.zero;
                var hdrTxt = hdrRt.GetComponent<TMP_Text>();
                hdrTxt.text      = "SYSTEMS";
                hdrTxt.fontSize  = 9f;
                hdrTxt.alignment = TextAlignmentOptions.Center;
                hdrTxt.color     = new Color(0.45f, 0.55f, 0.72f, 0.80f);
                hdrTxt.fontStyle = FontStyles.Bold;
                hdrTxt.raycastTarget = false;

                // Divider under header
                var divRt = MakeImage(secRt, "SysDiv", new Color(0.22f, 0.32f, 0.50f, 0.40f));
                divRt.anchorMin        = new Vector2(0f, 1f);
                divRt.anchorMax        = new Vector2(1f, 1f);
                divRt.pivot            = new Vector2(0.5f, 1f);
                divRt.sizeDelta        = new Vector2(0f, 1f);
                divRt.anchoredPosition = new Vector2(0f, -19f);

                // Scroll view (fills below header + divider)
                var scrollGo = new GameObject("SysScrollView",
                    typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
                scrollGo.transform.SetParent(secRt, false);
                var scrollRt = scrollGo.GetComponent<RectTransform>();
                scrollRt.anchorMin = new Vector2(0f, 0f);
                scrollRt.anchorMax = new Vector2(1f, 1f);
                scrollRt.offsetMin = new Vector2(0f, 0f);
                scrollRt.offsetMax = new Vector2(0f, -22f);  // 18 header + 1 div + 3 gap
                _sectorSystemsScrollRect = scrollGo.GetComponent<ScrollRect>();
                _sectorSystemsScrollRect.horizontal       = false;
                _sectorSystemsScrollRect.vertical         = true;
                _sectorSystemsScrollRect.scrollSensitivity = 20f;
                _sectorSystemsScrollRect.movementType     = ScrollRect.MovementType.Clamped;

                // Content – VerticalLayoutGroup drives height
                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(scrollGo.transform, false);
                _sectorSystemsList = contentGo.GetComponent<RectTransform>();
                _sectorSystemsList.anchorMin        = new Vector2(0f, 1f);
                _sectorSystemsList.anchorMax        = new Vector2(1f, 1f);
                _sectorSystemsList.pivot            = new Vector2(0.5f, 1f);
                _sectorSystemsList.sizeDelta        = Vector2.zero;
                _sectorSystemsList.anchoredPosition = Vector2.zero;

                var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
                vlg.childControlHeight    = true;
                vlg.childControlWidth     = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.spacing               = 2f;
                vlg.padding               = new RectOffset(4, 4, 2, 4);

                var csf = contentGo.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                _sectorSystemsScrollRect.content = _sectorSystemsList;
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
