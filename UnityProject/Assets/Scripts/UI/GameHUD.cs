// GameHUD — right-side taskbar with animated slide-out drawer.
//
// Taskbar: 68 px vertical strip flush to right edge.
// Drawer:  320 px panel that slides out to the left of the taskbar.
//
// Tabs: Build · Crew · Station · Comms · Away Mission · Rooms · Settings
//
// Self-installs via RuntimeInitializeOnLoadMethod; sets DemoBootstrap.HideOverlay
// so the legacy IMGUI stats box is suppressed.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Demo;
using Waystation.Models;
using Waystation.Systems;
using Waystation.View;

namespace Waystation.UI
{
    public class GameHUD : MonoBehaviour
    {
        // ── Sizing ────────────────────────────────────────────────────────────
        private const float TabW       = 68f;
        private const float DrawerW    = 360f;
        private const float DevDrawerW = 220f;
        private const float Pad        = 12f;
        private const float AnimK      = 12f;

        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color ColBar      = new Color(0.08f, 0.09f, 0.14f, 0.97f);
        private static readonly Color ColBarEdge  = new Color(0.20f, 0.30f, 0.48f, 1.00f);
        private static readonly Color ColDrawer   = new Color(0.10f, 0.11f, 0.17f, 0.97f);
        private static readonly Color ColDivider  = new Color(0.22f, 0.32f, 0.50f, 0.60f);
        private static readonly Color ColAccent   = new Color(0.35f, 0.62f, 1.00f, 1.00f);
        private static readonly Color ColTabHl    = new Color(0.18f, 0.27f, 0.46f, 1.00f);
        private static readonly Color ColBarBg    = new Color(0.13f, 0.15f, 0.22f, 1.00f);
        private static readonly Color ColBarFill  = new Color(0.24f, 0.56f, 0.86f, 1.00f);
        private static readonly Color ColBarWarn  = new Color(0.88f, 0.68f, 0.10f, 1.00f);
        private static readonly Color ColBarCrit  = new Color(0.86f, 0.26f, 0.26f, 1.00f);
        private static readonly Color ColBarGreen = new Color(0.22f, 0.76f, 0.35f, 1.00f);
        private static readonly Color ColSummaryBg = new Color(0.07f, 0.09f, 0.15f, 0.85f);

        // ── Tab enum ──────────────────────────────────────────────────────────
        private enum Tab { None, Build, Crew, Station, Comms, AwayMission, Rooms, Settings }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Build,       "Build"),
            (Tab.Crew,        "Crew"),
            (Tab.Station,     "Station"),
            (Tab.Comms,       "Comms"),
            (Tab.AwayMission, "Away"),
            (Tab.Rooms,       "Rooms"),
            (Tab.Settings,    "Settings"),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private GameManager _gm;
        private bool        _ready;
        private Tab         _active;
        private float       _drawerT;
        private bool        _devDrawerOpen;
        private float       _devDrawerT;

        private Vector2 _crewScroll;
        private Vector2 _stationScroll;
        private string  _inventorySearch = "";

        // Station Inventory state
        private string  _selectedHoldUid = "";  // uid of the hold whose settings panel is open

        // All recognised item type names (used by the filter checkboxes)
        private static readonly string[] ItemTypes =
            { "Material", "Equipment", "Biological", "Valuables", "Waste" };

        // ── Comms panel state ─────────────────────────────────────────────────
        private enum CommsTab { Unread, Read, All }
        private CommsTab _commsTab       = CommsTab.Unread;
        private string   _selectedMsgUid = "";
        private Vector2  _commsListScroll;
        private Vector2  _commsBodyScroll;

        // ── Crew / Work sub-panel state ───────────────────────────────────────
        private enum CrewSubPanel { Roster, Work, Departments }
        private CrewSubPanel _crewSub = CrewSubPanel.Roster;
        private Vector2      _workScroll;
        private Vector2      _deptScroll;
        // Rename flow: uid of department being renamed, text buffer
        private string _renamingDeptUid  = "";
        private string _renameDeptBuffer = "";

        // Job columns shown in Work Assignment grid
        private static readonly (string id, string label)[] WorkJobCols =
        {
            ("job.haul",                "Haul"),
            ("job.refine",              "Refine"),
            ("job.craft",               "Craft"),
            ("job.guard_post",          "Guard"),
            ("job.patrol",              "Patrol"),
            ("job.build",               "Build"),
            ("job.module_maintenance",  "Maint."),
            ("job.resource_management", "ResMgmt"),
        };

        // ── Dev tool fill amounts ─────────────────────────────────────────────
        private const float DevFillFood    = 500f;
        private const float DevFillPower   = 500f;
        private const float DevFillOxygen  = 500f;
        private const float DevFillParts   = 200f;
        private const float DevFillIce     = 500f;
        private const float DevFillCredits = 5000f;
        private const int   DevSteelPlateAmount = 50;
        private const int   DevWiringAmount     = 20;

        // ── Styles (built once on first OnGUI) ────────────────────────────────
        private Texture2D _white;
        private GUIStyle  _sTabOff, _sTabOn;
        private GUIStyle  _sHeader, _sLabel, _sSub;
        private GUIStyle  _sBtnSmall, _sBtnWide, _sBtnDanger;
        private GUIStyle  _sTextField;
        private bool      _stylesReady;

        // ── Auto-install ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<GameHUD>() != null) return;
            new GameObject("GameHUD").AddComponent<GameHUD>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy()
        {
            DemoBootstrap.HideOverlay = false;
            foreach (var go in _ghostPool) if (go) Destroy(go);
        }

        private IEnumerator WaitForGame()
        {
            while (GameManager.Instance == null ||
                   !GameManager.Instance.IsLoaded ||
                   GameManager.Instance.Station == null)
                yield return null;

            // Suppress the legacy overlay only when we are in a scene with a
            // GameManager (i.e., not in the main menu or other non-game scenes).
            DemoBootstrap.HideOverlay = true;

            _gm    = GameManager.Instance;
            _ready = true;
        }

        // ── Update — drawer animation + ghost input ───────────────────────────
        private void Update()
        {
            UpdateGhostSprites();

            float target = _active != Tab.None ? 1f : 0f;
            _drawerT    = Mathf.Lerp(_drawerT,    target,                  Time.deltaTime * AnimK);
            _devDrawerT = Mathf.Lerp(_devDrawerT, _devDrawerOpen ? 1f : 0f, Time.deltaTime * AnimK);

            // Suppress map scroll/pan while the mouse is over either HUD panel
            bool overRight = Input.mousePosition.x >= Screen.width - TabW - DrawerW * _drawerT;
            bool overLeft  = Input.mousePosition.x <= DevDrawerW * _devDrawerT;
            IsMouseOverDrawer = overRight || overLeft;
            InBuildMode       = _ghostBuildableId != null || _deconstructMode;

            // ── Deconstruct mode ──────────────────────────────────────────────
            if (_deconstructMode && _ghostBuildableId == null)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    _deconstructMode = false;
                    _isDragging      = false;
                    _dragLine.Clear();
                    _dragBlocked.Clear();
                    return;
                }
                var dcam = Camera.main;
                if (dcam != null)
                {
                    Vector3 dworld = dcam.ScreenToWorldPoint(Input.mousePosition);
                    _ghostTileCol = Mathf.RoundToInt(dworld.x);
                    _ghostTileRow = Mathf.RoundToInt(dworld.y);
                }

                if (Input.GetMouseButtonDown(0) && !IsMouseOverDrawer)
                {
                    _isDragging   = true;
                    _dragRect     = true; // deconstruct always selects a rectangle
                    _dragStartCol = _ghostTileCol;
                    _dragStartRow = _ghostTileRow;
                }
                if (_isDragging)
                    RebuildDeconDragLine();
                if (Input.GetMouseButtonUp(0) && _isDragging)
                {
                    foreach (var (col, row) in _dragLine)
                        DeconstructTile(col, row);
                    _isDragging = false;
                    _dragLine.Clear();
                    _dragBlocked.Clear();
                }
                return;
            }

            // ── Ghost placement ───────────────────────────────────────────────
            if (_ghostBuildableId == null) return;

            if (Input.GetKeyDown(KeyCode.Q)) _ghostRotation = (_ghostRotation + 270) % 360;
            if (Input.GetKeyDown(KeyCode.E)) _ghostRotation = (_ghostRotation +  90) % 360;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _ghostBuildableId = null;
                _isDragging = false;
                _dragLine.Clear();
                _dragBlocked.Clear();
                return;
            }

            // Always track cursor tile
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
                _ghostTileCol = Mathf.RoundToInt(world.x);
                _ghostTileRow = Mathf.RoundToInt(world.y);
            }

            // Begin drag on mouse-down (hold Shift for rectangle fill)
            if (Input.GetMouseButtonDown(0) && !IsMouseOverDrawer && _gm?.Station != null)
            {
                _isDragging   = true;
                _dragRect     = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                _dragStartCol = _ghostTileCol;
                _dragStartRow = _ghostTileRow;
            }

            // Rebuild the preview line every frame while dragging
            if (_isDragging)
                RebuildDragLine();

            // Commit on mouse-up: place all valid tiles, then keep ghost active
            if (Input.GetMouseButtonUp(0) && _isDragging)
            {
                foreach (var (col, row) in _dragLine)
                {
                    if (!_dragBlocked.Contains((col, row)))
                        _gm.Building.PlaceFoundation(
                            _gm.Station, _ghostBuildableId, col, row, _ghostRotation);
                }
                _isDragging = false;
                _dragLine.Clear();
                _dragBlocked.Clear();
            }
        }

        // Maintains a pool of world-space SpriteRenderers showing the actual buildable sprite
        // with a blue ghost tint while the player is placing.  Uses previous-frame drag state
        // (called at the top of Update before drag rebuild) — lag is imperceptible at 60 fps.
        private void UpdateGhostSprites()
        {
            bool placing = _ghostBuildableId != null && !_deconstructMode;

            // Build tile list from previous-frame state
            var tiles = new List<(int col, int row)>();
            if (placing)
            {
                if (_isDragging && _dragLine.Count > 0)
                    tiles.AddRange(_dragLine);
                else
                    tiles.Add((_ghostTileCol, _ghostTileRow));
            }

            Sprite spr = placing ? TileAtlas.GetPreviewSprite(_ghostBuildableId, _ghostRotation) : null;

            // Grow pool on demand
            while (_ghostPool.Count < tiles.Count)
            {
                var go = new GameObject("GhostTileSprite");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 50; // above all tile layers
                _ghostPool.Add(go);
            }

            // Update / deactivate each slot
            for (int i = 0; i < _ghostPool.Count; i++)
            {
                if (i >= tiles.Count) { _ghostPool[i].SetActive(false); continue; }

                var (col, row) = tiles[i];
                _ghostPool[i].SetActive(true);
                _ghostPool[i].transform.position = new Vector3(col, row, -0.1f);
                _ghostPool[i].transform.rotation = Quaternion.Euler(0f, 0f, _ghostRotation);

                var sr = _ghostPool[i].GetComponent<SpriteRenderer>();
                sr.sprite = spr;
                bool blocked = _dragBlocked.Contains((col, row));
                sr.color   = blocked
                    ? new Color(1.00f, 0.30f, 0.20f, 0.60f)
                    : new Color(0.45f, 0.80f, 1.00f, 0.60f);
            }
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            // ── Dev Tools button (top-left) — toggles side drawer ───────────────
            bool devDrawer = _devDrawerOpen;
            GUI.color = devDrawer ? new Color(1.00f, 0.78f, 0.20f, 1f) : new Color(0.55f, 0.60f, 0.70f, 0.85f);
            if (GUI.Button(new Rect(6f, 6f, 90f, 22f), devDrawer ? "⚡ Dev ◀" : "⚡ Dev Tools", _sBtnSmall))
                _devDrawerOpen = !devDrawer;
            GUI.color = Color.white;
            if (Waystation.Systems.BuildingSystem.DevMode)
            {
                GUI.color = new Color(1f, 0.92f, 0.35f, 0.85f);
                GUI.Label(new Rect(100f, 8f, 200f, 18f), "Free Build  ON", _sSub);
                GUI.color = Color.white;
            }

            // ── Dev drawer (slides out from left edge) ────────────────────────
            if (_devDrawerT > 0.004f)
            {
                float ddx = DevDrawerW * (_devDrawerT - 1f);  // 0 = fully open; negative = sliding
                DrawSolid(new Rect(ddx, 0, DevDrawerW, sh), ColDrawer);
                DrawSolid(new Rect(ddx + DevDrawerW - 1f, 0, 1f, sh), ColBarEdge);
                GUI.BeginGroup(new Rect(ddx, 0, DevDrawerW, sh));
                DrawDevPanel(DevDrawerW, sh);
                GUI.EndGroup();
            }

            // ── Drawer (slides in from right) ─────────────────────────────────
            if (_drawerT > 0.004f)
            {
                float dx = sw - TabW - DrawerW * _drawerT;
                DrawSolid(new Rect(dx, 0, DrawerW, sh), ColDrawer);
                DrawSolid(new Rect(dx, 0, 1f, sh), ColBarEdge);

                GUI.BeginGroup(new Rect(dx, 0, DrawerW, sh));
                DrawDrawer(DrawerW, sh);
                GUI.EndGroup();
            }

            // ── Taskbar ───────────────────────────────────────────────────────
            float tx = sw - TabW;
            DrawSolid(new Rect(tx, 0, TabW, sh), ColBar);
            DrawSolid(new Rect(tx, 0, 1f, sh), ColBarEdge);

            float ty = 20f;
            foreach (var (tab, label) in Tabs)
                DrawTabButton(tab, label, tx, ref ty);

            // Pause / resume at bottom
            if (_ready && _gm != null)
            {
                string pl = _gm.IsPaused ? "► Play" : "⏸ Pause";
                Rect   pr = new Rect(tx + 5f, sh - 54f, TabW - 10f, 40f);
                if (GUI.Button(pr, pl, _sTabOff))
                    _gm.IsPaused = !_gm.IsPaused;
            }

            // ── Foundation tile overlays (placed but incomplete) ─────────────
            if (_white != null && _gm?.Station != null)
            {
                var overlayCam = Camera.main;
                if (overlayCam != null)
                {
                    foreach (var kv in _gm.Station.foundations)
                    {
                        var f = kv.Value;
                        if (f.status == "complete") continue;

                        // Yellow outline if materials arrived / constructing; blue-grey if waiting.
                        // Fill is transparent — foundation sprite is tinted by StationRoomView.
                        bool hasResources = f.hauledMaterials.Count > 0 ||
                                            f.status == "constructing";
                        Color fill = Color.clear;
                        Color edge  = hasResources
                                      ? new Color(1.00f, 0.90f, 0.20f, 0.75f)
                                      : new Color(0.55f, 0.70f, 0.90f, 0.55f);

                        DrawTileOverlay(overlayCam, f.tileCol, f.tileRow, fill, edge);
                    }
                }
            }

            // ── Rooms tab overlay ─────────────────────────────────────────────
            if (_active == Tab.Rooms && _white != null)
            {
                var roomCam = Camera.main;
                if (roomCam != null) DrawRoomsOverlay(roomCam);
            }

            // ── Deconstruct mode overlays ─────────────────────────────────────
            if (_deconstructMode && _isDragging && _dragLine.Count > 0 && _white != null && _gm?.Station != null)
            {
                var dCam = Camera.main;
                if (dCam != null)
                {
                    foreach (var (col, row) in _dragLine)
                    {
                        bool hasTarget = !_dragBlocked.Contains((col, row));
                        Color fill = hasTarget ? new Color(1f, 0.20f, 0.15f, 0.55f)
                                               : new Color(0.5f, 0.5f, 0.5f, 0.15f);
                        Color edge = hasTarget ? new Color(1f, 0.45f, 0.35f, 0.95f)
                                               : new Color(0.6f, 0.6f, 0.6f, 0.35f);
                        DrawTileOverlay(dCam, col, row, fill, edge);
                    }
                }
            }

            // ── Cursor ghost overlay ──────────────────────────────────────────
            if (_ghostBuildableId != null && _white != null)
            {
                var ghostCam = Camera.main;
                if (ghostCam != null)
                {
                    if (_isDragging && _dragLine.Count > 0)
                    {
                        // Edge outline only — sprite is rendered by UpdateGhostSprites
                        foreach (var (col, row) in _dragLine)
                        {
                            bool blocked = _dragBlocked.Contains((col, row));
                            Color edge = blocked
                                ? new Color(1.00f, 0.50f, 0.35f, 0.85f)
                                : new Color(0.50f, 0.88f, 1.00f, 0.80f);
                            DrawTileOverlay(ghostCam, col, row, Color.clear, edge);
                        }
                    }
                    else
                    {
                        // Single cursor tile — edge outline only
                        Color edge = new Color(0.50f, 0.88f, 1.00f, 0.80f);
                        DrawTileOverlay(ghostCam, _ghostTileCol, _ghostTileRow, Color.clear, edge);
                    }

                    // Label anchored to the cursor tile
                    Vector3 sp     = ghostCam.WorldToScreenPoint(new Vector3(_ghostTileCol, _ghostTileRow, 0f));
                    Vector3 spNext = ghostCam.WorldToScreenPoint(new Vector3(_ghostTileCol + 1, _ghostTileRow, 0f));
                    float   pxSize = Mathf.Abs(spNext.x - sp.x);
                    float   guiX   = sp.x - pxSize * 0.5f;
                    float   guiY   = Screen.height - sp.y - pxSize * 0.5f;

                    string bName = (_gm?.Registry?.Buildables != null &&
                                    _gm.Registry.Buildables.TryGetValue(_ghostBuildableId, out var gd))
                                   ? gd.displayName : _ghostBuildableId;

                    string hint;
                    if (_isDragging)
                    {
                        int valid = _dragLine.Count - _dragBlocked.Count;
                        string shape = _dragRect ? "rect" : "line";
                        hint = $"{bName}  ·  {valid} tile{(valid == 1 ? "" : "s")} ({shape})  ·  release to place";
                    }
                    else
                    {
                        hint = $"{bName}  {_ghostRotation}°  ·  Q ↺  E ↻  drag=line  Shift+drag=rect  ·  RMB cancel";
                    }
                    GUI.Label(new Rect(guiX - 80f, guiY - 22f, 350f, 18f), hint, _sSub);
                }
            }
        }

        // ── Tab button ────────────────────────────────────────────────────────
        private void DrawTabButton(Tab tab, string label, float x, ref float y)
        {
            bool on = _active == tab;
            Rect r  = new Rect(x + 5f, y, TabW - 10f, 54f);

            // Flash the Comms button when there are unread messages
            string displayLabel = label;
            if (tab == Tab.Comms && _ready && _gm?.Station != null)
            {
                int unread = _gm.Station.UnreadMessageCount();
                if (unread > 0)
                {
                    // Flash by toggling the dot character on a half-second cycle
                    bool flash = (Time.realtimeSinceStartup % 1.0f) < 0.5f;
                    displayLabel = flash ? $"Comms\n● {unread}" : $"Comms\n○ {unread}";
                }
            }

            if (on)
            {
                DrawSolid(new Rect(x,      y, 3f,         54f), ColAccent);
                DrawSolid(new Rect(x + 3f, y, TabW - 3f,  54f), ColTabHl);
            }

            if (GUI.Button(r, displayLabel, on ? _sTabOn : _sTabOff))
                _active = on ? Tab.None : tab;

            y += 58f;
        }

        // ── Drawer root ───────────────────────────────────────────────────────
        private void DrawDrawer(float w, float h)
        {
            string title = _active switch
            {
                Tab.Build       => "Build",
                Tab.Crew        => "Crew",
                Tab.Station     => "Station",
                Tab.Comms       => "Comms",
                Tab.AwayMission => "Away Mission",
                Tab.Rooms       => "Room Designations",
                Tab.Settings    => "Settings",
                _               => "",
            };

            GUI.Label(new Rect(Pad, 18f, w - Pad * 2f, 26f), title, _sHeader);
            DrawSolid(new Rect(Pad, 50f, w - Pad * 2f, 1f), ColDivider);

            float cw      = w - Pad * 2f;
            float startY  = 58f;
            float contentH = h - startY - 8f;
            Rect  area    = new Rect(Pad, startY, cw, contentH);

            if (!_ready && _active != Tab.Build &&
                           _active != Tab.AwayMission &&
                           _active != Tab.Settings)
            { GUI.Label(area, "Loading...", _sSub); return; }

            switch (_active)
            {
                case Tab.Build:       DrawBuild(area, cw, contentH);       break;
                case Tab.Crew:        DrawCrew(area, cw, contentH);        break;
                case Tab.Station:     DrawStation(area, cw, contentH);     break;
                case Tab.Comms:       DrawComms(area, cw, contentH);       break;
                case Tab.AwayMission: DrawAwayMission(area, cw, contentH); break;
                case Tab.Rooms:       DrawRooms(area, cw, contentH);       break;
                case Tab.Settings:    DrawSettings(area, cw, contentH);    break;
            }
        }

        // ── Build tab ─────────────────────────────────────────────────────────
        private Vector2 _buildScroll;
        private string  _buildInfoOpen    = "";  // buildable id whose info panel is expanded
        private string  _foundSettingsOpen = ""; // foundation uid whose cargo settings are open
        private string  _buildCategoryFilter = ""; // "" = all categories
        private bool    _deconstructMode = false; // deconstruct-mode: click tile to cancel/demolish
        private bool    _showBuildQueue  = false; // toggle inline build-queue panel

        // ── Ghost placement ───────────────────────────────────────────────────
        private string _ghostBuildableId = null;
        private int    _ghostRotation    = 0;    // 0 / 90 / 180 / 270
        private int    _ghostTileCol     = 0;
        private int    _ghostTileRow     = 0;

        // Drag-to-place state
        private bool   _isDragging    = false;
        private bool   _dragRect      = false;   // true = fill rectangle, false = line
        private int    _dragStartCol  = 0;
        private int    _dragStartRow  = 0;
        private readonly System.Collections.Generic.List<(int col, int row)>    _dragLine    = new System.Collections.Generic.List<(int col, int row)>();
        private readonly System.Collections.Generic.HashSet<(int col, int row)> _dragBlocked = new System.Collections.Generic.HashSet<(int col, int row)>();

        // World-space SpriteRenderer GOs that render the actual buildable tile as a ghost.
        private readonly List<GameObject> _ghostPool = new List<GameObject>();

        // True when cursor is over the HUD — used by CameraController to block map scroll/pan
        public static bool IsMouseOverDrawer { get; private set; }

        // True when ghost-placement or deconstruct mode is active — used by StationRoomView
        // to suppress NPC selection while the player is building.
        public static bool InBuildMode { get; private set; }

        private void DrawBuild(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s       = _gm.Station;
            var catalog = _gm.Registry.Buildables;
            var active  = s.foundations;

            float cw = w - 16f;

            // ── Toolbar strip ────────────────────────────────────────────────
            const float TBH = 30f;
            DrawSolid(new Rect(area.x, area.y, w, TBH), new Color(0.05f, 0.07f, 0.12f, 0.9f));
            Color tbPrev = GUI.color;
            GUI.color = _deconstructMode ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.7f);
            if (GUI.Button(new Rect(area.x + 4f, area.y + 4f, 110f, 22f), "\u26CF Deconstruct", _sBtnSmall))
                _deconstructMode = !_deconstructMode;
            GUI.color = _showBuildQueue ? new Color(0.4f, 0.7f, 0.9f) : new Color(0.6f, 0.6f, 0.7f);
            if (GUI.Button(new Rect(area.x + 120f, area.y + 4f, 110f, 22f),
                           $"\u2261 Queue ({active.Count})", _sBtnSmall))
                _showBuildQueue = !_showBuildQueue;
            GUI.color = tbPrev;

            float tbH = TBH;
            if (_deconstructMode)
            {
                DrawSolid(new Rect(area.x, area.y + TBH, w, 18f), new Color(0.4f, 0.1f, 0.1f, 0.5f));
                GUI.color = new Color(1f, 0.6f, 0.6f);
                GUI.Label(new Rect(area.x + 6f, area.y + TBH + 2f, w - 12f, 14f),
                          "Click a tile to deconstruct it.  RMB / Esc to cancel.", _sSub);
                GUI.color = tbPrev;
                tbH += 18f;
            }

            // ── Rooms toggle button ───────────────────────────────────────
            bool roomsActive = _active == Tab.Rooms;
            GUI.color = roomsActive ? new Color(0.45f, 0.80f, 0.50f, 1f) : new Color(0.55f, 0.60f, 0.70f, 1f);
            if (GUI.Button(new Rect(area.x + 234f, area.y + 4f, w - 238f, 22f), "▦ Rooms", _sBtnSmall))
                _active = roomsActive ? Tab.Build : Tab.Rooms;
            GUI.color = tbPrev;

            // ── Group catalogue by category ───────────────────────────────────
            // Category filter buttons
            string[] catFilters = { "", "structure", "electrical", "object",
                                    "production", "plumbing", "security" };
            string[] catLabels  = { "All", "Structure", "Electrical", "Objects",
                                    "Production", "Plumbing", "Security" };
            // Two rows of category buttons (4 in first row, 3 in second)
            const float CatBtnH = 22f;
            DrawSolid(new Rect(area.x, area.y + tbH, w, (CatBtnH + 2f) * 2f + 4f), new Color(0.04f, 0.06f, 0.11f, 0.9f));
            int[] rowSplit = { 4, 3 };
            int ci2 = 0;
            for (int row2 = 0; row2 < 2; row2++)
            {
                int count = rowSplit[row2];
                float bw2 = (w - 8f) / count;
                float bx2 = area.x + 4f;
                for (int col2 = 0; col2 < count; col2++, ci2++)
                {
                    bool isActive = _buildCategoryFilter == catFilters[ci2];
                    GUI.color = isActive ? new Color(0.35f, 0.60f, 0.90f, 1f) : new Color(0.55f, 0.60f, 0.70f, 1f);
                    if (GUI.Button(new Rect(bx2, area.y + tbH + 2f + row2 * (CatBtnH + 2f), bw2 - 2f, CatBtnH - 2f),
                                   catLabels[ci2], _sBtnSmall))
                        _buildCategoryFilter = catFilters[ci2];
                    bx2 += bw2;
                }
            }
            GUI.color = tbPrev;
            tbH += (CatBtnH + 2f) * 2f + 4f;

            var byCategory = new SortedDictionary<string, List<BuildableDefinition>>();
            foreach (var kv in catalog)
            {
                string cat = string.IsNullOrEmpty(kv.Value.category) ? "other" : kv.Value.category;
                if (!string.IsNullOrEmpty(_buildCategoryFilter) && cat != _buildCategoryFilter) continue;
                if (!byCategory.ContainsKey(cat))
                    byCategory[cat] = new List<BuildableDefinition>();
                byCategory[cat].Add(kv.Value);
            }

            // ── Estimate scroll content height ────────────────────────────────
            float innerH = 8f;
            if (_showBuildQueue)
            {
                innerH += 24f;
                innerH += active.Count == 0 ? 20f : active.Count * 72f;
                innerH += 10f;
            }
            innerH += 24f;
            foreach (var catGroup in byCategory)
            {
                innerH += 24f;
                foreach (var defn in catGroup.Value)
                {
                    innerH += 90f;
                    if (_buildInfoOpen == defn.id)
                    {
                        float ht = 16f;
                        if (!string.IsNullOrEmpty(defn.description))
                            ht += _sSub.CalcHeight(new GUIContent(defn.description), cw - 12f) + 14f;
                        ht += 22f + (defn.requiredMaterials.Count == 0 ? 18f
                                     : defn.requiredMaterials.Count * 18f);
                        ht += 22f + (defn.requiredSkills.Count == 0 ? 18f
                                     : defn.requiredSkills.Count * 18f);
                        if (defn.requiredTags.Count > 0)
                            ht += 22f + defn.requiredTags.Count * 18f;
                        ht += 12f;
                        innerH += ht;
                    }
                }
            }

            Rect scrollArea = new Rect(area.x, area.y + tbH, w, h - tbH);
            _buildScroll = GUI.BeginScrollView(scrollArea, _buildScroll,
                           new Rect(0, 0, cw, Mathf.Max(h - tbH, innerH)));
            float y = 0f;

            // ── Build Queue (optional) ────────────────────────────────────────
            if (_showBuildQueue)
            {
                Section($"Build Queue ({active.Count})", cw, ref y);
                if (active.Count == 0)
                {
                    GUI.Label(new Rect(0, y, cw, 18f), "No foundations placed yet.", _sSub);
                    y += 20f;
                }
                else
                {
                    foreach (var kv in active)
                        DrawFoundationRow(kv.Value, cw, ref y, s);
                }
                Divider(cw, ref y);
            }

            // ── Catalogue ────────────────────────────────────────────────────
            Section("Catalogue", cw, ref y);

            if (catalog.Count == 0)
            {
                GUI.Label(new Rect(0, y, cw, 18f), "No buildables loaded.", _sSub);
                y += 20f;
            }

            foreach (var catGroup in byCategory)
            {
                DrawSolid(new Rect(0, y, cw, 20f), new Color(0.04f, 0.06f, 0.12f, 0.8f));
                GUI.Label(new Rect(4f, y + 3f, cw - 8f, 14f), CategoryDisplayName(catGroup.Key), _sSub);
                y += 22f;

                foreach (var defn in catGroup.Value)
                    DrawCatalogCard(defn, cw, ref y, s);
            }

            GUI.EndScrollView();
        }

        private void DrawFoundationRow(FoundationInstance f, float cw, ref float y, StationState s)
        {
            bool   hasDefn     = _gm.Registry.Buildables.TryGetValue(f.buildableId, out var fDefn);
            string fname       = hasDefn ? fDefn.displayName : f.buildableId;
            bool   isCabinet   = f.buildableId.Contains("storage_cabinet");
            bool   settingsOpen = isCabinet && f.status == "complete" && _foundSettingsOpen == f.uid;

            DrawSolid(new Rect(0, y, cw, 64f), new Color(0.07f, 0.09f, 0.15f, 0.6f));
            GUI.Label(new Rect(4f, y + 2f, cw * 0.65f, 18f), fname, _sLabel);

            string statusLabel = f.status switch
            {
                "awaiting_haul" => "Awaiting materials",
                "constructing"  => $"Building  {f.buildProgress * 100f:F0}%",
                "complete"      => "Complete",
                _               => f.status
            };
            GUI.Label(new Rect(4f, y + 20f, cw - 8f, 14f), statusLabel, _sSub);

            if (f.status == "constructing" || f.status == "awaiting_haul")
            {
                float pct = f.status == "constructing" ? f.buildProgress : 0f;
                DrawSolid(new Rect(4f, y + 36f, cw - 8f, 6f),  new Color(0.15f, 0.15f, 0.2f));
                DrawSolid(new Rect(4f, y + 36f, (cw - 8f) * pct, 6f), ColBarFill);
            }

            if (f.status == "awaiting_haul" && hasDefn && fDefn.requiredMaterials.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var m in fDefn.requiredMaterials)
                {
                    int have = f.hauledMaterials.ContainsKey(m.Key) ? f.hauledMaterials[m.Key] : 0;
                    sb.Append($"{ItemDisplayName(m.Key)} {have}/{m.Value}  ");
                }
                GUI.Label(new Rect(4f, y + 44f, cw - 8f, 14f), sb.ToString(), _sSub);
            }

            if (f.status != "complete")
            {
                if (GUI.Button(new Rect(cw * 0.68f, y + 2f, cw * 0.32f, 17f), "Cancel", _sBtnDanger))
                    _gm.Building.CancelFoundation(s, f.uid, refund: true);
            }
            else if (isCabinet)
            {
                // Toggle settings panel
                string btnLabel = settingsOpen ? "\u25b2 Config" : "\u25bc Config";
                if (GUI.Button(new Rect(cw * 0.68f, y + 2f, cw * 0.32f, 17f), btnLabel, _sBtnSmall))
                    _foundSettingsOpen = settingsOpen ? "" : f.uid;

                // Rotation reminder label
                GUI.Label(new Rect(4f, y + 38f, cw - 8f, 14f),
                    $"{f.cargoCapacity} items  ·  {f.tileRotation}° rotation", _sSub);
            }

            y += 70f;

            // ── Cargo settings panel (cabinet only, complete, expanded) ───────
            if (settingsOpen)
            {
                if (f.cargoSettings == null) f.cargoSettings = new CargoHoldSettings();

                DrawSolid(new Rect(0, y, cw, 1f), ColDivider);
                y += 6f;
                GUI.Label(new Rect(4f, y, cw - 8f, 16f), "Allowed Item Types", _sLabel);
                y += 20f;

                bool allowAll  = f.cargoSettings.allowedTypes.Count == 0;
                bool allowNone = f.cargoSettings.allowedTypes.Count == 1 &&
                                 f.cargoSettings.allowedTypes[0] == CargoHoldSettings.AllowNoneSentinel;

                GUI.Label(new Rect(8f, y, cw * 0.78f, 16f), "No restrictions (allow all)", _sSub);
                bool newAllowAll = GUI.Toggle(new Rect(cw * 0.82f, y, 16f, 16f), allowAll, "");
                if (newAllowAll != allowAll)
                    f.cargoSettings.allowedTypes = newAllowAll
                        ? new System.Collections.Generic.List<string>()
                        : new System.Collections.Generic.List<string>(ItemTypes);
                y += 20f;

                foreach (var type in ItemTypes)
                {
                    bool wasAllowed = allowAll || (!allowNone && f.cargoSettings.allowedTypes.Contains(type));
                    GUI.Label(new Rect(12f, y, cw * 0.78f, 16f), type, _sSub);
                    bool nowAllowed = GUI.Toggle(new Rect(cw * 0.82f, y, 16f, 16f), wasAllowed, "");
                    if (nowAllowed != wasAllowed && !allowAll)
                    {
                        if (allowNone) f.cargoSettings.allowedTypes.Clear();
                        if (nowAllowed) { if (!f.cargoSettings.allowedTypes.Contains(type)) f.cargoSettings.allowedTypes.Add(type); }
                        else f.cargoSettings.allowedTypes.Remove(type);
                    }
                    y += 20f;
                }

                DrawSolid(new Rect(0, y, cw, 1f), ColDivider);
                y += 6f;
            }
        }

        private void DrawCatalogCard(BuildableDefinition defn, float cw, ref float y, StationState s)
        {
            bool   canPlace = _gm.Building.CanPlace(s, defn.id, out string reason);
            bool   infoOpen = _buildInfoOpen == defn.id;
            float  alpha    = canPlace ? 1f : 0.45f;
            Color  prevCol  = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            DrawSolid(new Rect(0, y, cw, 84f),
                new Color(0.07f, 0.09f, 0.15f, canPlace ? 0.6f : 0.4f));

            GUI.Label(new Rect(4f, y + 4f, cw - 60f, 18f), defn.displayName, _sLabel);
            GUI.color = prevCol;
            if (GUI.Button(new Rect(cw - 52f, y + 4f, 48f, 18f),
                           infoOpen ? "\u25b2 Info" : "\u25bc Info", _sBtnSmall))
                _buildInfoOpen = infoOpen ? "" : defn.id;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            GUI.Label(new Rect(4f, y + 26f, cw * 0.70f, 14f),
                      $"{CategoryDisplayName(defn.category).Replace("\u2500\u2500 ","").Replace(" \u2500\u2500","")}  \u00b7  {defn.buildTimeTicks} ticks", _sSub);

            if (defn.requiredMaterials.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var m in defn.requiredMaterials)
                    sb.Append($"{ItemDisplayName(m.Key)} \u00d7{m.Value}  ");
                GUI.Label(new Rect(4f, y + 42f, cw - 8f, 14f), sb.ToString(), _sSub);
            }
            else
            {
                GUI.Label(new Rect(4f, y + 42f, cw - 8f, 14f), "No materials required.", _sSub);
            }

            if (!canPlace && reason != null)
            {
                GUI.color = new Color(1f, 0.55f, 0.2f, alpha);
                GUI.Label(new Rect(4f, y + 61f, cw * 0.58f, 18f), $"\u2715 {reason}", _sSub);
                GUI.color = new Color(1f, 1f, 1f, alpha);
            }
            GUI.enabled = canPlace;
            if (GUI.Button(new Rect(cw * 0.60f, y + 59f, cw * 0.40f, 22f), "+ Place", _sBtnSmall))
            {
                _ghostBuildableId = defn.id;
                _ghostRotation    = 0;
                _active           = Tab.None;
            }
            GUI.enabled = true;
            GUI.color   = prevCol;

            y += 90f;

            if (infoOpen)
            {
                DrawSolid(new Rect(0, y, cw, 1f), ColDivider);
                DrawSolid(new Rect(0, y + 1f, cw, 1000f), new Color(0.04f, 0.06f, 0.12f, 0.8f));

                float iy = y + 8f;

                if (!string.IsNullOrEmpty(defn.description))
                {
                    float descH = _sSub.CalcHeight(new GUIContent(defn.description), cw - 12f);
                    GUI.Label(new Rect(6f, iy, cw - 12f, descH), defn.description, _sSub);
                    iy += descH + 8f;
                    DrawSolid(new Rect(6f, iy, cw - 12f, 1f), new Color(1f, 1f, 1f, 0.05f));
                    iy += 6f;
                }

                GUI.Label(new Rect(6f, iy, cw - 12f, 18f), "Required Materials", _sLabel);
                iy += 22f;
                if (defn.requiredMaterials.Count == 0)
                {
                    GUI.Label(new Rect(14f, iy, cw - 20f, 14f), "None", _sSub);
                    iy += 18f;
                }
                else
                {
                    foreach (var m in defn.requiredMaterials)
                    {
                        GUI.Label(new Rect(14f, iy, cw - 20f, 14f),
                                  $"\u2022 {ItemDisplayName(m.Key)}  \u00d7{m.Value}", _sSub);
                        iy += 18f;
                    }
                }

                iy += 4f;

                GUI.Label(new Rect(6f, iy, cw - 12f, 18f), "Skills Required", _sLabel);
                iy += 22f;
                if (defn.requiredSkills.Count == 0)
                {
                    GUI.Label(new Rect(14f, iy, cw - 20f, 14f), "None", _sSub);
                    iy += 18f;
                }
                else
                {
                    foreach (var sk in defn.requiredSkills)
                    {
                        bool met = false;
                        foreach (var npc in s.GetCrew())
                            if (npc.skills.TryGetValue(sk.Key, out int lvl) && lvl >= sk.Value)
                            { met = true; break; }

                        Color skillCol = met ? new Color(0.4f, 0.9f, 0.4f) : ColBarCrit;
                        var pc = GUI.color;
                        GUI.color = skillCol;
                        GUI.Label(new Rect(14f, iy, 18f, 14f), met ? "\u2713" : "\u2715", _sSub);
                        GUI.color = prevCol;
                        GUI.Label(new Rect(32f, iy, cw - 38f, 14f),
                                  $"{sk.Key}  (level {sk.Value}+)", _sSub);
                        iy += 18f;
                    }
                }

                if (defn.requiredTags.Count > 0)
                {
                    iy += 4f;
                    GUI.Label(new Rect(6f, iy, cw - 12f, 18f), "Station Requirements", _sLabel);
                    iy += 22f;
                    foreach (var tag in defn.requiredTags)
                    {
                        bool met = s.HasTag(tag);
                        Color tagCol = met ? new Color(0.4f, 0.9f, 0.4f) : ColBarCrit;
                        var pc = GUI.color;
                        GUI.color = tagCol;
                        GUI.Label(new Rect(14f, iy, 18f, 14f), met ? "\u2713" : "\u2715", _sSub);
                        GUI.color = prevCol;
                        GUI.Label(new Rect(32f, iy, cw - 38f, 14f), tag, _sSub);
                        iy += 18f;
                    }
                }

                iy += 8f;
                DrawSolid(new Rect(0, iy, cw, 1f), ColDivider);
                y = iy + 4f;
            }
        }

        private static string CategoryDisplayName(string cat) => cat switch
        {
            "structure"  => "\u2500\u2500 Structure \u2500\u2500",
            "object"     => "\u2500\u2500 Objects \u2500\u2500",
            "electrical" => "\u2500\u2500 Electrical \u2500\u2500",
            "production" => "\u2500\u2500 Production \u2500\u2500",
            "plumbing"   => "\u2500\u2500 Plumbing \u2500\u2500",
            "security"   => "\u2500\u2500 Security \u2500\u2500",
            _            => $"\u2500\u2500 {char.ToUpper(cat[0]) + cat.Substring(1)} \u2500\u2500"
        };

        private void DeconstructTile(int col, int row)
        {
            var s        = _gm.Station;
            var toRemove = new List<string>();
            foreach (var kv in s.foundations)
            {
                var f = kv.Value;
                if (f.tileCol == col && f.tileRow == row)
                {
                    if (f.status == "complete")
                        toRemove.Add(f.uid);
                    else
                        _gm.Building.CancelFoundation(s, f.uid, refund: true);
                }
            }
            foreach (var uid in toRemove)
                s.foundations.Remove(uid);
        }

        // ── Crew tab ──────────────────────────────────────────────────────────
        private void DrawCrew(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;

            // ── Sub-panel selector ────────────────────────────────────────────
            const float SubTabH = 28f;
            float subY = area.y;
            float subBw = (w - 8f) / 3f;

            if (GUI.Button(new Rect(area.x,            subY, subBw, SubTabH),
                           "Roster",      _crewSub == CrewSubPanel.Roster      ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Roster;
            if (GUI.Button(new Rect(area.x + subBw + 4f, subY, subBw, SubTabH),
                           "Work",        _crewSub == CrewSubPanel.Work        ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Work;
            if (GUI.Button(new Rect(area.x + (subBw + 4f) * 2f, subY, subBw, SubTabH),
                           "Departments", _crewSub == CrewSubPanel.Departments ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Departments;

            Rect subArea = new Rect(area.x, area.y + SubTabH + 6f, w, h - SubTabH - 6f);
            float subH   = h - SubTabH - 6f;

            switch (_crewSub)
            {
                case CrewSubPanel.Roster:      DrawCrewRoster(subArea, w, subH);  break;
                case CrewSubPanel.Work:        DrawCrewWork(subArea, w, subH);    break;
                case CrewSubPanel.Departments: DrawDepartments(subArea, w, subH); break;
            }
        }

        private void DrawCrewRoster(Rect area, float w, float h)
        {
            var crew = _gm.Station.GetCrew();

            // ── Summary strip ─────────────────────────────────────────────────
            const float SumH = 78f;
            DrawSolid(new Rect(area.x, area.y, w, SumH), ColSummaryBg);

            float avgMood  = 0f;
            int   sickCount = 0, injCount = 0;
            foreach (var n in crew)
            {
                avgMood += n.mood;
                if (n.statusTags.Contains("sick")) sickCount++;
                if (n.injuries > 0)                injCount++;
            }
            if (crew.Count > 0) avgMood /= crew.Count;
            float happinessPct = (avgMood + 1f) * 50f;

            float sy = area.y + 7f;
            GUI.Label(new Rect(area.x + 8f, sy, w - 8f, 16f),
                $"Crew: {crew.Count}   Happiness: {happinessPct:F0}%   Sick: {sickCount}   Injured: {injCount}",
                _sSub);
            sy += 20f;
            float bw = w - 16f;
            DrawSolid(new Rect(area.x + 8f, sy, bw, 8f), ColBarBg);
            Color hc = happinessPct >= 60f ? ColBarGreen
                     : happinessPct >= 35f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(area.x + 8f, sy, bw * (happinessPct / 100f), 8f), hc);
            sy += 14f;
            string healthLine = (sickCount == 0 && injCount == 0)
                ? "All crew healthy"
                : $"{sickCount} sick · {injCount} injured  —  check Medical Bay";
            GUI.Label(new Rect(area.x + 8f, sy, w - 8f, 16f), healthLine, _sSub);

            // ── Scrollable crew list ──────────────────────────────────────────
            const float RowH  = 116f;
            float listTop  = area.y + SumH + 6f;
            float listH    = h - SumH - 6f;
            float innerH   = Mathf.Max(listH, crew.Count * RowH);
            _crewScroll = GUI.BeginScrollView(new Rect(area.x, listTop, w, listH),
                          _crewScroll, new Rect(0, 0, w - 14f, innerH));
            float y = 0f;
            foreach (var npc in crew)
            {
                string cls = (npc.classId ?? "").Replace("class.", "");
                string job = _gm.Jobs.GetJobLabel(npc);
                GUI.Label(new Rect(0,        y,       w * 0.60f, 20f), npc.name,        _sLabel);
                GUI.Label(new Rect(w * 0.62f, y,      w * 0.38f, 18f), npc.MoodLabel(), _sSub);
                GUI.Label(new Rect(0, y + 20f, w, 16f), cls, _sSub);
                GUI.Label(new Rect(0,        y + 38f, w * 0.66f, 16f), $"Job: {job}",   _sSub);
                if (GUI.Button(new Rect(w * 0.68f, y + 36f, w * 0.32f, 18f),
                               "Reassign", _sBtnSmall))
                    _gm.Jobs.InterruptNpc(npc);
                NeedBar("Hunger", GetNeed(npc, "hunger"), w, y + 58f);
                NeedBar("Rest",   GetNeed(npc, "rest"),   w, y + 74f);
                if (npc.statusTags.Count > 0 || npc.injuries > 0)
                {
                    string tags = string.Join(", ", npc.statusTags);
                    if (npc.injuries > 0)
                        tags += (tags.Length > 0 ? ", " : "") +
                                $"{npc.injuries} injur{(npc.injuries == 1 ? "y" : "ies")}";
                    var prev = GUI.color; GUI.color = ColBarWarn;
                    GUI.Label(new Rect(0, y + 91f, w - 14f, 14f), tags, _sSub);
                    GUI.color = prev;
                }
                DrawSolid(new Rect(0, y + RowH - 4f, w - 14f, 1f), ColDivider);
                y += RowH;
            }
            if (crew.Count == 0)
                GUI.Label(new Rect(0, 0, w - 14f, 20f), "No crew assigned.", _sSub);
            GUI.EndScrollView();
        }

        // ── Work Assignment grid ──────────────────────────────────────────────
        private void DrawCrewWork(Rect area, float w, float h)
        {
            var s    = _gm.Station;
            var crew = s.GetCrew();

            GUI.Label(new Rect(area.x, area.y, w, 18f),
                "Toggle cells to allow/restrict jobs per crew member.", _sSub);

            const float HeaderH = 38f;
            const float RowH    = 26f;
            const float NameW   = 80f;
            float colW = (w - NameW - 14f) / WorkJobCols.Length;

            // Header row
            float hx = area.x + NameW;
            for (int ci = 0; ci < WorkJobCols.Length; ci++)
            {
                GUI.Label(new Rect(hx + ci * colW, area.y + 22f, colW, 16f),
                          WorkJobCols[ci].label, _sSub);
            }

            float listTop = area.y + HeaderH;
            float innerH  = Mathf.Max(h - HeaderH, crew.Count * RowH);
            _workScroll = GUI.BeginScrollView(
                new Rect(area.x, listTop, w, h - HeaderH),
                _workScroll, new Rect(0, 0, w - 14f, innerH));

            float y = 0f;
            foreach (var npc in crew)
            {
                // Ensure entry exists
                if (!s.workAssignments.ContainsKey(npc.uid))
                    s.workAssignments[npc.uid] = new List<string>();
                var allowed = s.workAssignments[npc.uid];

                // NPC name
                GUI.Label(new Rect(0, y + 5f, NameW - 4f, 16f),
                          npc.name.Length > 9 ? npc.name[..9] : npc.name, _sSub);

                for (int ci = 0; ci < WorkJobCols.Length; ci++)
                {
                    string jid     = WorkJobCols[ci].id;
                    bool   enabled = allowed.Count == 0 || allowed.Contains(jid);
                    Rect   cell    = new Rect(NameW + ci * colW, y + 3f, colW - 2f, RowH - 6f);

                    var prev = GUI.color;
                    GUI.color = enabled ? ColBarGreen : new Color(0.25f, 0.28f, 0.38f, 1f);
                    if (GUI.Button(cell, enabled ? "✓" : "—", _sBtnSmall))
                    {
                        // Toggle
                        if (allowed.Count == 0)
                        {
                            // Currently all allowed → restrict all except this one
                            // becomes: add all *other* jobs
                            foreach (var (id2, _) in WorkJobCols)
                                if (id2 != jid && !allowed.Contains(id2))
                                    allowed.Add(id2);
                        }
                        else if (allowed.Contains(jid))
                        {
                            allowed.Remove(jid);
                            // If empty now, means all restricted — keep as-is (user
                            // can re-enable). An NPC with no allowed jobs will wander.
                        }
                        else
                        {
                            allowed.Add(jid);
                            // If every job is now allowed, clear for "all" shorthand
                            bool allAllowed = true;
                            foreach (var (id2, _) in WorkJobCols)
                                if (!allowed.Contains(id2)) { allAllowed = false; break; }
                            if (allAllowed) allowed.Clear();
                        }
                    }
                    GUI.color = prev;
                }

                DrawSolid(new Rect(0, y + RowH - 2f, w - 14f, 1f), ColDivider);
                y += RowH;
            }
            if (crew.Count == 0)
                GUI.Label(new Rect(0, 0, w - 14f, 20f), "No crew to assign.", _sSub);
            GUI.EndScrollView();
        }

        // ── Departments panel ─────────────────────────────────────────────────
        private void DrawDepartments(Rect area, float w, float h)
        {
            var s    = _gm.Station;
            var deps = s.departments;

            GUI.Label(new Rect(area.x, area.y, w, 18f),
                "Departments group crew and jobs. Rename or create custom ones.", _sSub);

            // "+ New" button
            if (GUI.Button(new Rect(area.x, area.y + 22f, w, 24f), "+ New Department", _sBtnWide))
            {
                var nd = Department.Create(
                    $"dept.custom_{System.Guid.NewGuid().ToString("N")[..4]}",
                    "New Department");
                s.departments.Add(nd);
                _renamingDeptUid  = nd.uid;
                _renameDeptBuffer = nd.name;
            }

            float listTop = area.y + 52f;
            const float RowH = 48f;
            float innerH = Mathf.Max(h - 52f, deps.Count * RowH);
            _deptScroll = GUI.BeginScrollView(
                new Rect(area.x, listTop, w, h - 52f),
                _deptScroll, new Rect(0, 0, w - 14f, innerH));

            float y = 0f;
            for (int di = 0; di < deps.Count; di++)
            {
                var dept = deps[di];
                DrawSolid(new Rect(0, y, w - 14f, RowH - 4f),
                          new Color(0.10f, 0.12f, 0.20f, 0.85f));

                if (_renamingDeptUid == dept.uid)
                {
                    // Inline rename field
                    _renameDeptBuffer = GUI.TextField(
                        new Rect(4f, y + 6f, w - 80f, 20f),
                        _renameDeptBuffer, 30, _sTextField);

                    if (GUI.Button(new Rect(w - 72f, y + 6f, 34f, 20f), "OK", _sBtnSmall))
                    {
                        string trimmed = _renameDeptBuffer.Trim();
                        if (trimmed.Length > 0) dept.name = trimmed;
                        _renamingDeptUid = "";
                    }
                    if (GUI.Button(new Rect(w - 34f, y + 6f, 24f, 20f), "✕", _sBtnSmall))
                        _renamingDeptUid = "";
                }
                else
                {
                    GUI.Label(new Rect(4f, y + 4f,  w * 0.6f, 18f), dept.name, _sLabel);

                    // Rename button
                    if (GUI.Button(new Rect(w * 0.65f, y + 4f, 46f, 18f), "Rename", _sBtnSmall))
                    {
                        _renamingDeptUid  = dept.uid;
                        _renameDeptBuffer = dept.name;
                    }

                    // Jobs preview
                    string preview = dept.allowedJobs.Count > 0
                        ? string.Join(", ", dept.allowedJobs.ConvertAll(j => j.Replace("job.", "")))
                        : "(all jobs)";
                    if (preview.Length > 38) preview = preview[..35] + "…";
                    GUI.Label(new Rect(4f, y + 26f, w - 8f, 14f), preview, _sSub);
                }

                y += RowH;
            }
            if (deps.Count == 0)
                GUI.Label(new Rect(0, 0, w - 14f, 20f), "No departments defined.", _sSub);
            GUI.EndScrollView();
        }

        private static float GetNeed(NPCInstance npc, string key)
            => npc.needs.TryGetValue(key, out float v) ? v : 1f;

        private void NeedBar(string label, float value, float w, float y)
        {
            float lw = w * 0.30f, bx = w * 0.32f, bw = w * 0.52f, bh = 10f;
            GUI.Label(new Rect(0, y, lw, bh + 2f), label, _sSub);
            DrawSolid(new Rect(bx, y + 1f, bw, bh - 2f), ColBarBg);
            Color fc = value > 0.5f ? ColBarGreen : value > 0.25f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(bx, y + 1f, bw * value, bh - 2f), fc);
            GUI.Label(new Rect(bx + bw + 4f, y, 30f, bh + 2f),
                      Mathf.RoundToInt(value * 100f) + "%", _sSub);
        }

        // ── Comms tab ─────────────────────────────────────────────────────────
        private void DrawComms(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s = _gm.Station;

            // ── Tab selector ──────────────────────────────────────────────────
            const float TabH = 26f;
            float tbw = (w - 8f) / 3f;
            if (GUI.Button(new Rect(area.x,               area.y, tbw, TabH),
                           "Unread", _commsTab == CommsTab.Unread ? _sTabOn : _sTabOff))
            { _commsTab = CommsTab.Unread; _selectedMsgUid = ""; }
            if (GUI.Button(new Rect(area.x + tbw + 4f,    area.y, tbw, TabH),
                           "Read",   _commsTab == CommsTab.Read   ? _sTabOn : _sTabOff))
            { _commsTab = CommsTab.Read; _selectedMsgUid = ""; }
            if (GUI.Button(new Rect(area.x + (tbw + 4f)*2f, area.y, tbw, TabH),
                           "All",    _commsTab == CommsTab.All    ? _sTabOn : _sTabOff))
            { _commsTab = CommsTab.All; _selectedMsgUid = ""; }

            // Filter messages
            var msgs = new List<CommMessage>();
            foreach (var m in s.messages)
            {
                bool include = _commsTab switch
                {
                    CommsTab.Unread => !m.read,
                    CommsTab.Read   => m.read,
                    _               => true
                };
                if (include) msgs.Add(m);
            }

            // ── Layout: list on left, detail on right ─────────────────────────
            const float ListW = 140f;
            float listTop  = area.y + TabH + 6f;
            float listH    = h - TabH - 6f;
            const float ListRowH = 40f;
            float innerH = Mathf.Max(listH, msgs.Count * ListRowH);

            _commsListScroll = GUI.BeginScrollView(
                new Rect(area.x, listTop, ListW, listH),
                _commsListScroll, new Rect(0, 0, ListW - 14f, innerH));

            float ly = 0f;
            foreach (var msg in msgs)
            {
                bool sel = _selectedMsgUid == msg.uid;
                DrawSolid(new Rect(0, ly, ListW - 14f, ListRowH - 3f),
                          sel ? ColTabHl : (msg.read ? new Color(0.09f, 0.10f, 0.16f, 0.9f)
                                                     : new Color(0.12f, 0.15f, 0.25f, 0.95f)));
                if (!msg.read)
                {
                    var prev = GUI.color; GUI.color = ColBarWarn;
                    GUI.Label(new Rect(2f, ly + 2f, 8f, 8f), "●", _sSub);
                    GUI.color = prev;
                }
                string subj = msg.subject.Length > 18 ? msg.subject[..15] + "…" : msg.subject;
                GUI.Label(new Rect(10f, ly + 3f,  ListW - 24f, 14f), subj,          _sSub);

                // Show expiry countdown or sender name
                string rowSub;
                if (msg.expiresAtTick >= 0 && msg.replied == null && _gm?.Station != null)
                {
                    int ticksLeft = msg.expiresAtTick - _gm.Station.tick;
                    if (ticksLeft <= 0)
                        rowSub = "Expired";
                    else if (ticksLeft <= 6)
                        rowSub = $"Expires in {ticksLeft}t";
                    else
                        rowSub = msg.senderName;
                }
                else rowSub = msg.senderName;

                // Colour the sub-line red when expiry is close (<= 6 ticks)
                bool expiring = msg.expiresAtTick >= 0 && msg.replied == null &&
                                _gm?.Station != null &&
                                (msg.expiresAtTick - _gm.Station.tick) <= 6;
                var prevSub = GUI.color;
                if (expiring) GUI.color = ColBarCrit;
                GUI.Label(new Rect(10f, ly + 20f, ListW - 24f, 14f), rowSub, _sSub);
                GUI.color = prevSub;

                if (GUI.Button(new Rect(0, ly, ListW - 14f, ListRowH - 3f), "", GUIStyle.none))
                {
                    _selectedMsgUid = msg.uid;
                    msg.read = true;
                }
                DrawSolid(new Rect(0, ly + ListRowH - 4f, ListW - 14f, 1f), ColDivider);
                ly += ListRowH;
            }
            if (msgs.Count == 0)
                GUI.Label(new Rect(0, 0, ListW - 14f, 20f), "No messages.", _sSub);
            GUI.EndScrollView();

            // ── Detail pane ───────────────────────────────────────────────────
            float detX = area.x + ListW + 6f;
            float detW = w - ListW - 6f;
            DrawSolid(new Rect(detX, listTop, 1f, listH), ColDivider);

            CommMessage sel_msg = null;
            foreach (var m in s.messages)
                if (m.uid == _selectedMsgUid) { sel_msg = m; break; }

            if (sel_msg == null && msgs.Count > 0)
            {
                sel_msg = msgs[0];
                _selectedMsgUid = sel_msg.uid;
                sel_msg.read = true;
            }

            if (sel_msg != null)
            {
                float dy = listTop;
                float dw = detW - 8f;

                GUI.Label(new Rect(detX + 4f, dy, dw, 20f), sel_msg.subject, _sLabel);
                dy += 22f;
                GUI.Label(new Rect(detX + 4f, dy, dw, 16f),
                          $"From: {sel_msg.senderName}  ·  T{sel_msg.tick:D4}", _sSub);
                dy += 20f;

                // Expiry warning (only for unreplied messages with a finite TTL)
                if (sel_msg.expiresAtTick >= 0 && sel_msg.replied == null)
                {
                    int ticksLeft = sel_msg.expiresAtTick - s.tick;
                    string expiryStr = ticksLeft > 0
                        ? $"⏱ Expires in {ticksLeft} tick{(ticksLeft == 1 ? "" : "s")} (~{ticksLeft / 24f:F1} day{(ticksLeft / 24f == 1.0f ? "" : "s")})"
                        : "⏱ Expired — no longer actionable";
                    var prev = GUI.color;
                    GUI.color = ticksLeft <= 6 ? ColBarCrit : ColBarWarn;
                    GUI.Label(new Rect(detX + 4f, dy, dw, 16f), expiryStr, _sSub);
                    GUI.color = prev;
                    dy += 18f;
                }

                DrawSolid(new Rect(detX + 4f, dy, dw, 1f), ColDivider);
                dy += 8f;

                // Body (scrollable)
                float replyH   = sel_msg.replied == null ? sel_msg.responseOptions.Count * 30f + 8f : 24f;
                float bodyH    = listH - (dy - listTop) - replyH - 10f;
                float bodyInner = bodyH + 80f; // give some scroll room

                _commsBodyScroll = GUI.BeginScrollView(
                    new Rect(detX + 4f, dy, dw, bodyH),
                    _commsBodyScroll, new Rect(0, 0, dw - 14f, bodyInner));
                GUI.Label(new Rect(0, 0, dw - 14f, bodyInner), sel_msg.body, _sSub);
                GUI.EndScrollView();

                dy += bodyH + 6f;
                DrawSolid(new Rect(detX + 4f, dy, dw, 1f), ColDivider);
                dy += 6f;

                // Reply buttons
                if (sel_msg.replied == null)
                {
                    foreach (var opt in sel_msg.responseOptions)
                    {
                        string btnLabel = opt.ContainsKey("label") ? opt["label"].ToString() : "Reply";
                        if (GUI.Button(new Rect(detX + 4f, dy, dw, 24f), btnLabel, _sBtnWide))
                            _gm.Comms.ReplyToMessage(sel_msg, opt, s);
                        dy += 28f;
                    }
                }
                else
                {
                    var prev = GUI.color; GUI.color = new Color(0.5f, 0.55f, 0.65f, 1f);
                    GUI.Label(new Rect(detX + 4f, dy, dw, 20f),
                              $"Replied: {sel_msg.replied}", _sSub);
                    GUI.color = prev;
                }
            }
            else
            {
                GUI.Label(new Rect(detX + 4f, listTop + h * 0.4f, detW - 8f, 20f),
                          "Select a message to read.", _sSub);
            }
        }


        private void DrawStation(Rect area, float w, float h)
        {
            var s     = _gm.Station;
            var holds = _gm.Inventory.GetCargoHolds(s);
            var (capUsed, capTotal) = _gm.Inventory.GetStationCapacity(s);

            // Dynamic content height estimate
            float holdRows  = 0f;
            foreach (var hold in holds)
            {
                holdRows += 52f;  // header row + capacity bar + items-header row
                int itemCount = hold.inventory.Count;
                holdRows += Mathf.Max(18f, itemCount * 18f);
                if (_selectedHoldUid == hold.uid)
                    holdRows += 28f + ItemTypes.Length * 22f + ItemTypes.Length * 28f + 36f;  // settings panel
                holdRows += 8f;   // bottom gap between holds
            }
            if (holds.Count == 0) holdRows = 22f;

            float innerH = 24f + 22f               // Wealth header + credits
                         + 16f                      // divider
                         + 24f + 5 * 22f           // Resources header + 5 bars
                         + 16f                      // divider
                         + 24f + 28f               // Station Inventory header + search
                         + holdRows + 14f           // per-hold rows
                         + 16f                      // divider
                         + 24f + s.modules.Count * 24f;  // Module Status

            _stationScroll = GUI.BeginScrollView(area, _stationScroll,
                             new Rect(0, 0, w, Mathf.Max(h, innerH)));
            float y = 0f;

            // ── Station Wealth ────────────────────────────────────────────────
            Section("Station Wealth", w, ref y);
            ResourceBar("Credits", s.GetResource("credits"), 5000f, w, ref y);
            Divider(w, ref y);

            // ── Production / Resources ────────────────────────────────────────
            Section("Resources", w, ref y);
            ResourceBar("Food",   s.GetResource("food"),   500f, w, ref y);
            ResourceBar("Power",  s.GetResource("power"),  500f, w, ref y);
            ResourceBar("Oxygen", s.GetResource("oxygen"), 500f, w, ref y);
            ResourceBar("Parts",  s.GetResource("parts"),  200f, w, ref y);
            ResourceBar("Ice",    s.GetResource("ice"),    500f, w, ref y);
            Divider(w, ref y);

            // ── Station Inventory ─────────────────────────────────────────────
            Section($"Station Inventory  ({capUsed:F0} / {capTotal} units)", w, ref y);

            // Overall capacity bar
            if (capTotal > 0)
            {
                float pct = Mathf.Clamp01(capUsed / capTotal);
                DrawSolid(new Rect(0, y + 3f, w, 10f), ColBarBg);
                Color fc = pct < 0.75f ? ColBarFill : pct < 0.90f ? ColBarWarn : ColBarCrit;
                DrawSolid(new Rect(0, y + 3f, w * pct, 10f), fc);
                GUI.Label(new Rect(w * 0.78f, y, w * 0.22f, 16f),
                          $"{pct * 100f:F0}%", _sSub);
                y += 18f;
            }

            // Search field
            GUI.Label(new Rect(0, y + 2f, 36f, 17f), "Find:", _sSub);
            _inventorySearch = GUI.TextField(new Rect(40f, y + 1f, w - 40f, 18f),
                                             _inventorySearch, _sTextField);
            y += 24f;

            string filter = _inventorySearch.Trim();

            if (holds.Count == 0)
            {
                GUI.Label(new Rect(0, y, w, 18f), "No cargo holds built.", _sSub);
                y += 22f;
            }

            // Per-hold rows
            foreach (var hold in holds)
            {
                DrawCargoHoldRow(hold, w, ref y, filter, s);
            }

            Divider(w, ref y);

            // ── Module Status ─────────────────────────────────────────────────
            Section("Module Status", w, ref y);
            foreach (var mod in s.modules.Values)
            {
                string status = !mod.active    ? "OFFLINE"
                              : mod.damage > 0f ? $"DMG {mod.damage:P0}"
                              : "OK";
                Color sc = !mod.active    ? ColBarCrit
                         : mod.damage > 0f ? ColBarWarn
                         : ColBarGreen;

                GUI.Label(new Rect(0, y, w * 0.72f, 20f), mod.displayName, _sSub);
                var prev  = GUI.color;
                GUI.color = sc;
                GUI.Label(new Rect(w * 0.74f, y, w * 0.26f, 20f), status, _sSub);
                GUI.color = prev;
                y += 24f;
            }

            GUI.EndScrollView();
        }

        // ── Per-hold row ──────────────────────────────────────────────────────
        private void DrawCargoHoldRow(ModuleInstance hold, float w, ref float y,
                                      string filter, StationState s)
        {
            float usedW  = _gm.Inventory.GetCapacityUsed(hold);
            float totalW = _gm.Inventory.GetCapacityTotal(hold);
            bool  isSel  = _selectedHoldUid == hold.uid;

            // ── Hold header line ──────────────────────────────────────────────
            GUI.Label(new Rect(0, y, w * 0.60f, 18f), hold.displayName, _sLabel);

            // Configure / Close toggle button
            string cfgLabel = isSel ? "▲ Close" : "⚙ Config";
            if (GUI.Button(new Rect(w * 0.62f, y, w * 0.38f, 17f), cfgLabel, _sBtnSmall))
                _selectedHoldUid = isSel ? "" : hold.uid;
            y += 20f;

            // ── Capacity bar ──────────────────────────────────────────────────
            {
                string capLabel = totalW > 0 ? $"{usedW:F0} / {totalW} units" : "No capacity";
                GUI.Label(new Rect(0, y, w * 0.50f, 14f), capLabel, _sSub);

                if (totalW > 0)
                {
                    float pct = Mathf.Clamp01(usedW / totalW);
                    float bx  = w * 0.52f, bw = w * 0.48f;
                    DrawSolid(new Rect(bx, y + 2f, bw, 8f), ColBarBg);
                    Color fc = pct < 0.75f ? ColBarFill : pct < 0.90f ? ColBarWarn : ColBarCrit;
                    DrawSolid(new Rect(bx, y + 2f, bw * pct, 8f), fc);
                }
                y += 16f;
            }

            // ── Filter type badges ────────────────────────────────────────────
            if (hold.cargoSettings != null && hold.cargoSettings.allowedTypes.Count > 0)
            {
                string badgeText;
                if (hold.cargoSettings.allowedTypes.Count == 1 &&
                    hold.cargoSettings.allowedTypes[0] == CargoHoldSettings.AllowNoneSentinel)
                    badgeText = "Filter: (nothing allowed)";
                else
                    badgeText = "Filter: " + string.Join(", ", hold.cargoSettings.allowedTypes);
                GUI.Label(new Rect(0, y, w, 14f), badgeText, _sSub);
                y += 16f;
            }

            // ── Items in this hold ────────────────────────────────────────────
            int shownItems = 0;
            foreach (var kv in hold.inventory)
            {
                string id      = kv.Key;
                string display = ItemDisplayName(id);

                if (filter.Length > 0 &&
                    id.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    display.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                GUI.Label(new Rect(8f,  y, w * 0.68f, 16f), display, _sSub);
                GUI.Label(new Rect(w * 0.72f, y, w * 0.28f, 16f), $"×{kv.Value}", _sSub);
                y += 18f;
                shownItems++;
            }
            if (shownItems == 0)
            {
                if (hold.inventory.Count == 0)
                {
                    GUI.Label(new Rect(8f, y, w, 14f), "Empty", _sSub);
                    y += 16f;
                }
                else if (filter.Length > 0)
                {
                    GUI.Label(new Rect(8f, y, w, 14f), "No matching items.", _sSub);
                    y += 16f;
                }
            }

            // ── Settings panel (shown when selected) ──────────────────────────
            if (isSel)
                DrawCargoHoldSettings(hold, w, ref y, s);

            // Divider between holds
            DrawSolid(new Rect(0, y, w, 1f), ColDivider);
            y += 8f;
        }

        // ── Cargo Hold Settings Panel ─────────────────────────────────────────
        private void DrawCargoHoldSettings(ModuleInstance hold, float w, ref float y,
                                           StationState s)
        {
            // Ensure settings object exists
            if (hold.cargoSettings == null)
                hold.cargoSettings = new CargoHoldSettings();

            DrawSolid(new Rect(0, y, w, 1f), ColDivider);
            y += 8f;

            // ── Quick actions ─────────────────────────────────────────────────
            GUI.Label(new Rect(0, y, w, 16f), "Quick Actions", _sLabel);
            y += 20f;

            if (GUI.Button(new Rect(0, y, w * 0.48f, 22f), "Allow Everything", _sBtnSmall))
                _gm.Inventory.AllowEverything(s, hold.uid);
            if (GUI.Button(new Rect(w * 0.52f, y, w * 0.48f, 22f), "Allow Nothing", _sBtnSmall))
                _gm.Inventory.AllowNothing(s, hold.uid);
            y += 28f;

            // ── Type filter checkboxes ────────────────────────────────────────
            GUI.Label(new Rect(0, y, w, 16f), "Allowed Item Types", _sLabel);
            y += 20f;

            bool allowAll = hold.cargoSettings.allowedTypes.Count == 0;
            // "allowAll" is also false when set to [AllowNoneSentinel] (allow nothing)
            bool allowNone = hold.cargoSettings.allowedTypes.Count == 1 &&
                             hold.cargoSettings.allowedTypes[0] == CargoHoldSettings.AllowNoneSentinel;
            GUI.Label(new Rect(0, y, w * 0.78f, 16f), "No restrictions (allow all types)", _sSub);
            bool newAllowAll = GUI.Toggle(new Rect(w * 0.82f, y, 16f, 16f), allowAll, "");
            if (newAllowAll != allowAll)
            {
                if (newAllowAll)
                    _gm.Inventory.AllowEverything(s, hold.uid);
                else
                    _gm.Inventory.SetAllowedTypes(s, hold.uid,
                        new System.Collections.Generic.List<string>(ItemTypes));
            }
            y += 20f;

            foreach (var type in ItemTypes)
            {
                bool wasAllowed = allowAll || (!allowNone && hold.cargoSettings.allowedTypes.Contains(type));
                GUI.Label(new Rect(8f, y, w * 0.78f, 16f), type, _sSub);
                bool nowAllowed = GUI.Toggle(new Rect(w * 0.82f, y, 16f, 16f), wasAllowed, "");
                if (nowAllowed != wasAllowed && !allowAll)
                {
                    // If previously "allow nothing", start from a clean list
                    var current = allowNone
                        ? new System.Collections.Generic.List<string>()
                        : new System.Collections.Generic.List<string>(hold.cargoSettings.allowedTypes);
                    // allowedTypes is a whitelist: present = allowed, absent = blocked
                    if (nowAllowed) { if (!current.Contains(type)) current.Add(type); }
                    else            { current.Remove(type); }
                    _gm.Inventory.SetAllowedTypes(s, hold.uid, current);
                }
                y += 20f;
            }

            // ── Reserved capacity sliders ─────────────────────────────────────
            GUI.Label(new Rect(0, y, w, 16f), "Reserved Capacity by Type", _sLabel);
            y += 20f;

            foreach (var type in ItemTypes)
            {
                hold.cargoSettings.reservedByType.TryGetValue(type, out float current);
                GUI.Label(new Rect(8f, y, w * 0.34f, 16f), type, _sSub);
                float newVal = GUI.HorizontalSlider(
                    new Rect(w * 0.36f, y + 4f, w * 0.48f, 10f), current, 0f, 1f);
                if (!Mathf.Approximately(newVal, current))
                    _gm.Inventory.SetReserved(s, hold.uid, type, newVal);
                GUI.Label(new Rect(w * 0.86f, y, w * 0.14f, 16f),
                          $"{newVal * 100f:F0}%", _sSub);
                y += 22f;
            }

            // ── Priority ──────────────────────────────────────────────────────
            const int MaxPriority = 10;
            GUI.Label(new Rect(0, y, w * 0.50f, 16f),
                      $"Priority: {hold.cargoSettings.priority}", _sSub);
            if (GUI.Button(new Rect(w * 0.52f, y, w * 0.22f, 17f), "▲", _sBtnSmall))
                hold.cargoSettings.priority = Mathf.Min(MaxPriority, hold.cargoSettings.priority + 1);
            if (GUI.Button(new Rect(w * 0.76f, y, w * 0.22f, 17f), "▼", _sBtnSmall))
                hold.cargoSettings.priority = Mathf.Max(0, hold.cargoSettings.priority - 1);
            y += 24f;
        }

        // ── Away Mission tab ──────────────────────────────────────────────────
        private void DrawAwayMission(Rect area, float w, float h)
        {
            float y = area.y;
            GUI.Label(new Rect(area.x, y, w, 20f), "Plan an Expedition", _sLabel);
            y += 28f;
            GUI.Label(new Rect(area.x, y, w, 100f),
                "Send a crew team on mining runs, trade routes, or reconnaissance missions " +
                "beyond the station.\n\nAway mission planning is coming in a future update.",
                _sSub);
        }

        // ── Rooms tab ─────────────────────────────────────────────────────────
        private Vector2 _roomsScroll;
        private string  _roomHoveredKey  = null;  // canonical key of flood-filled hovered room
        private string  _roomSelectedKey = null;  // canonical key of room for role picker
        private bool    _roomPickerOpen  = false;

        private static readonly (string id, string label, Color col)[] RoomRoles =
        {
            ("",              "Unassigned",    new Color(0.40f, 0.40f, 0.45f, 0.40f)),
            ("hallway",       "Hallway",       new Color(0.35f, 0.50f, 0.65f, 0.50f)),
            ("cargo_hold",    "Cargo Hold",    new Color(0.75f, 0.58f, 0.25f, 0.50f)),
            ("medical_bay",   "Medical Bay",   new Color(0.25f, 0.68f, 0.38f, 0.50f)),
            ("engineering",   "Engineering",   new Color(0.78f, 0.50f, 0.20f, 0.50f)),
            ("command",       "Command",       new Color(0.28f, 0.45f, 0.80f, 0.50f)),
            ("crew_quarters", "Crew Quarters", new Color(0.60f, 0.32f, 0.72f, 0.50f)),
            ("recreation",    "Recreation",    new Color(0.80f, 0.35f, 0.55f, 0.50f)),
            ("security",      "Security",      new Color(0.78f, 0.24f, 0.24f, 0.50f)),
            ("cafeteria",     "Cafeteria",     new Color(0.75f, 0.75f, 0.22f, 0.50f)),
        };

        // BFS flood-fill from (startCol, startRow) over floor tiles, returning their canonical key.
        // Returns null if start tile is not a floor.
        private string FloodFillRoom(int startCol, int startRow, out List<(int c, int r)> tiles)
        {
            tiles = new List<(int, int)>();
            if (_gm?.Station == null) return null;
            var founds = _gm.Station.foundations;

            // Determine which tiles are "floor" — same logic as StationRoomView.IsFloorTile
            bool IsFloor(int c, int r)
            {
                string key = $"{c}_{r}";
                if (founds.TryGetValue(key, out var fd))
                    return fd.buildableId.Contains("floor") && !fd.buildableId.Contains("wall");
                return c >= 1 && c <= 5 && r >= 1 && r <= 5; // default room interior
            }

            if (!IsFloor(startCol, startRow)) return null;

            var visited = new System.Collections.Generic.HashSet<(int,int)>();
            var queue   = new System.Collections.Generic.Queue<(int,int)>();
            queue.Enqueue((startCol, startRow));
            visited.Add((startCol, startRow));

            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                tiles.Add((c, r));
                foreach (var (dc, dr) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                {
                    var nb = (c+dc, r+dr);
                    if (!visited.Contains(nb) && IsFloor(nb.Item1, nb.Item2))
                    { visited.Add(nb); queue.Enqueue(nb); }
                }
            }

            if (tiles.Count == 0) return null;
            int minC = int.MaxValue, minR = int.MaxValue;
            foreach (var (c, r) in tiles) { if (c < minC) minC = c; if (r < minR) minR = r; }
            return $"{minC}_{minR}";
        }

        private static Color RoleColor(string roleId)
        {
            foreach (var rr in RoomRoles) if (rr.id == roleId) return rr.col;
            return RoomRoles[0].col;
        }

        private void DrawRooms(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var roomRoles = _gm.Station.roomRoles;

            // Update hovered room from mouse position
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 wp = cam.ScreenToWorldPoint(new Vector3(
                    Input.mousePosition.x, Input.mousePosition.y, 0f));
                int hc = Mathf.RoundToInt(wp.x), hr = Mathf.RoundToInt(wp.y);
                _roomHoveredKey = FloodFillRoom(hc, hr, out _);
                if (Input.GetMouseButtonDown(0) && _roomHoveredKey != null)
                {
                    _roomSelectedKey = _roomHoveredKey;
                    _roomPickerOpen  = true;
                }
            }

            float y = area.y;
            GUI.Label(new Rect(area.x, y, w, 18f),
                "Click a room on the map to assign a role.", _sSub);
            y += 22f;

            // Role picker popup
            if (_roomPickerOpen && _roomSelectedKey != null)
            {
                DrawSolid(new Rect(area.x, y, w, 22f), new Color(0.18f, 0.28f, 0.46f, 0.9f));
                GUI.Label(new Rect(area.x + 4f, y + 3f, w - 8f, 16f),
                    $"Room {_roomSelectedKey}  — assign role:", _sSub);
                y += 24f;

                foreach (var rr in RoomRoles)
                {
                    bool cur = roomRoles.TryGetValue(_roomSelectedKey, out var cr) && cr == rr.id
                               || (string.IsNullOrEmpty(rr.id) && !roomRoles.ContainsKey(_roomSelectedKey));
                    GUI.color = cur ? new Color(0.35f, 0.65f, 0.95f) : Color.white;
                    if (GUI.Button(new Rect(area.x + 4f, y, w - 8f, 20f), rr.label, _sBtnSmall))
                    {
                        if (string.IsNullOrEmpty(rr.id)) roomRoles.Remove(_roomSelectedKey);
                        else roomRoles[_roomSelectedKey] = rr.id;
                        _roomPickerOpen = false;
                    }
                    GUI.color = Color.white;
                    y += 22f;
                }

                if (GUI.Button(new Rect(area.x + 4f, y, w - 8f, 20f), "Cancel", _sBtnSmall))
                    _roomPickerOpen = false;
                y += 26f;
            }

            // List designated rooms
            if (roomRoles.Count == 0)
            {
                GUI.Label(new Rect(area.x, y, w, 18f), "No rooms designated yet.", _sSub);
                return;
            }
            GUI.Label(new Rect(area.x, y, w, 18f), "Designated rooms:", _sSub);
            y += 22f;

            _roomsScroll = GUI.BeginScrollView(new Rect(area.x, y, w, h - (y - area.y)),
                _roomsScroll, new Rect(0, 0, w - 16f, roomRoles.Count * 22f + 4f));
            float sy = 0f;
            foreach (var kv in roomRoles)
            {
                string rname = "Unknown";
                foreach (var rr in RoomRoles) if (rr.id == kv.Value) { rname = rr.label; break; }
                GUI.Label(new Rect(4f, sy, w - 60f, 18f), $"Room {kv.Key}", _sSub);
                GUI.Label(new Rect(w - 100f, sy, 96f, 18f), rname, _sSub);
                sy += 22f;
            }
            GUI.EndScrollView();
        }

        // overlay rendering for Rooms tab (called from OnGUI world-space section)
        private void DrawRoomsOverlay(Camera cam)
        {
            if (_gm?.Station == null) return;
            var roomRoles = _gm.Station.roomRoles;

            // Draw designated room fills
            foreach (var kv in roomRoles)
            {
                var parts = kv.Key.Split('_');
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0], out int sc) || !int.TryParse(parts[1], out int sr)) continue;
                FloodFillRoom(sc, sr, out var rtiles);
                Color rc = RoleColor(kv.Value);
                Color edge = new Color(rc.r, rc.g, rc.b, Mathf.Min(1f, rc.a + 0.25f));
                foreach (var (c, r) in rtiles)
                    DrawTileOverlay(cam, c, r, rc, edge);
            }

            // Hover highlight (re-flood-fill to get tile list)
            if (_roomHoveredKey != null)
            {
                var hparts = _roomHoveredKey.Split('_');
                if (hparts.Length >= 2
                    && int.TryParse(hparts[0], out int hsc)
                    && int.TryParse(hparts[1], out int hsr))
                {
                    FloodFillRoom(hsc, hsr, out var htiles);
                    foreach (var (c, r) in htiles)
                        DrawTileOverlay(cam, c, r, new Color(1f,1f,1f,0.10f), new Color(1f,1f,1f,0.50f));
                }
            }
        }

        // ── Dev panel (left drawer) ───────────────────────────────────────────
        private void DrawDevPanel(float w, float h)
        {
            float cw = w - Pad * 2f;
            GUI.Label(new Rect(Pad, 18f, cw, 26f), "Dev Tools", _sHeader);
            DrawSolid(new Rect(Pad, 50f, cw, 1f), ColDivider);

            float y = 62f;

            // Free Build toggle
            bool devMode = Waystation.Systems.BuildingSystem.DevMode;
            GUI.color = devMode
                ? new Color(1.00f, 0.78f, 0.20f, 1f)
                : new Color(0.55f, 0.60f, 0.70f, 0.95f);
            if (GUI.Button(new Rect(Pad, y, cw, 28f),
                           devMode ? "⚡ Free Build  ON" : "⚡ Free Build  OFF", _sBtnWide))
                Waystation.Systems.BuildingSystem.DevMode = !devMode;
            GUI.color = Color.white;
            y += 34f;

            DrawSolid(new Rect(Pad, y, cw, 1f), ColDivider); y += 14f;

            // Ships section
            GUI.Label(new Rect(Pad, y, cw, 18f), "Ships", _sLabel); y += 24f;
            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(Pad, y, cw, 28f), "\u25b6 Call Trade Ship", _sBtnWide))
                    _gm.Visitors.SpawnTradeShip(_gm.Station);
                y += 34f;
            }

            DrawSolid(new Rect(Pad, y, cw, 1f), ColDivider); y += 14f;

            // Resources section
            GUI.Label(new Rect(Pad, y, cw, 18f), "Resources", _sLabel); y += 24f;
            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(Pad, y, cw, 28f), "Fill Station Resources", _sBtnWide))
                {
                    _gm.Station.ModifyResource("food",    DevFillFood);
                    _gm.Station.ModifyResource("power",   DevFillPower);
                    _gm.Station.ModifyResource("oxygen",  DevFillOxygen);
                    _gm.Station.ModifyResource("parts",   DevFillParts);
                    _gm.Station.ModifyResource("ice",     DevFillIce);
                    _gm.Station.ModifyResource("credits", DevFillCredits);
                }
                y += 34f;

                if (GUI.Button(new Rect(Pad, y, cw, 28f), "Add Build Materials", _sBtnWide))
                {
                    // Add to the first cargo hold that accepts materials
                    foreach (var hold in _gm.Inventory.GetCargoHolds(_gm.Station))
                    {
                        int added = _gm.Inventory.AddItem(_gm.Station, hold.uid, "item.steel_plate", DevSteelPlateAmount);
                        if (added > 0)
                        {
                            _gm.Inventory.AddItem(_gm.Station, hold.uid, "item.wiring", DevWiringAmount);
                            break;
                        }
                    }
                }
                y += 34f;
            }
        }

        // ── Settings tab ─────────────────────────────────────────────────────
        private void DrawSettings(Rect area, float w, float h)
        {
            float y = area.y;

            // Graphics
            GUI.Label(new Rect(area.x, y, w, 20f), "Graphics", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Graphics settings coming soon.", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Controls
            GUI.Label(new Rect(area.x, y, w, 20f), "Controls", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Scroll wheel — zoom in / out", _sSub); y += 18f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Right-drag — pan camera", _sSub); y += 18f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Space — pause / resume", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Sound
            GUI.Label(new Rect(area.x, y, w, 20f), "Sound", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Sound settings coming soon.", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Save / Load
            GUI.Label(new Rect(area.x, y, w, 20f), "Game", _sLabel); y += 24f;

            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(area.x, y, w, 28f), "Save Game", _sBtnWide))
                    _gm.SaveGame();
                y += 34f;

                // Load is not yet implemented — show as disabled stub
                var prevColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                GUI.Button(new Rect(area.x, y, w, 28f), "Load Game  (coming soon)", _sBtnWide);
                GUI.color = prevColor;
                y += 34f;
            }

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Exit
            GUI.Label(new Rect(area.x, y, w, 20f), "Exit", _sLabel); y += 24f;
            if (GUI.Button(new Rect(area.x, y, w, 28f), "Exit to Desktop", _sBtnDanger))
                Application.Quit();
        }

        // ── Drawer helpers ────────────────────────────────────────────────────
        private void Section(string title, float w, ref float y)
        {
            GUI.Label(new Rect(0, y, w, 20f), title, _sLabel);
            y += 24f;
        }

        private void Divider(float w, ref float y)
        {
            y += 6f;
            DrawSolid(new Rect(0, y, w, 1f), ColDivider);
            y += 10f;
        }

        private void ResourceBar(string label, float value, float max, float w, ref float y)
        {
            float pct = Mathf.Clamp01(value / max);
            float lw  = w * 0.34f, bx = w * 0.36f, bw = w * 0.44f;
            GUI.Label(new Rect(0, y, lw, 18f), label, _sSub);
            DrawSolid(new Rect(bx, y + 3f, bw, 10f), ColBarBg);
            Color fc = pct > 0.5f ? ColBarFill : pct > 0.25f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(bx, y + 3f, bw * pct, 10f), fc);
            GUI.Label(new Rect(bx + bw + 4f, y, w - bx - bw - 4f, 18f),
                      value.ToString("F0"), _sSub);
            y += 22f;
        }

        // ── Utilities ─────────────────────────────────────────────────────────
        private string ItemDisplayName(string itemId)
        {
            // Attempt to look up the human-readable name from the registry
            if (_gm?.Registry?.Items != null &&
                _gm.Registry.Items.TryGetValue(itemId, out var defn))
                return defn.displayName;

            // Fallback: strip namespace prefix then title-case ("item.steel_plate" → "Steel Plate")
            string seg = itemId;
            int lastDot = seg.LastIndexOf('.');
            if (lastDot >= 0) seg = seg.Substring(lastDot + 1);
            string humanised = seg.Replace("_", " ").Trim();
            if (humanised.Length == 0) return itemId;
            var words = humanised.Split(' ');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0)
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        private void DrawSolid(Rect r, Color c)
        {
            if (_white == null) return;
            var prev  = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        // ── Drag-line helpers ─────────────────────────────────────────────────
        private void RebuildDragLine()
        {
            _dragLine.Clear();
            _dragBlocked.Clear();
            var tiles = _dragRect
                ? RectTiles(_dragStartCol, _dragStartRow, _ghostTileCol, _ghostTileRow)
                : BresenhamLine(_dragStartCol, _dragStartRow, _ghostTileCol, _ghostTileRow);
            foreach (var tile in tiles)
            {
                _dragLine.Add(tile);
                if (IsTileOccupied(tile.col, tile.row))
                    _dragBlocked.Add((tile.col, tile.row));
            }
        }

        // Deconstruct-mode drag: "blocked" = tile with no foundation (nothing to demolish).
        private void RebuildDeconDragLine()
        {
            _dragLine.Clear();
            _dragBlocked.Clear();
            var tiles = _dragRect
                ? RectTiles(_dragStartCol, _dragStartRow, _ghostTileCol, _ghostTileRow)
                : BresenhamLine(_dragStartCol, _dragStartRow, _ghostTileCol, _ghostTileRow);
            foreach (var tile in tiles)
            {
                _dragLine.Add(tile);
                if (!IsTileOccupied(tile.col, tile.row))
                    _dragBlocked.Add((tile.col, tile.row));
            }
        }

        private static System.Collections.Generic.List<(int col, int row)> RectTiles(
            int x0, int y0, int x1, int y1)
        {
            var list = new System.Collections.Generic.List<(int, int)>();
            int cMin = Mathf.Min(x0, x1), cMax = Mathf.Max(x0, x1);
            int rMin = Mathf.Min(y0, y1), rMax = Mathf.Max(y0, y1);
            for (int r = rMin; r <= rMax; r++)
                for (int c = cMin; c <= cMax; c++)
                    list.Add((c, r));
            return list;
        }

        private bool IsTileOccupied(int col, int row)
        {
            if (_gm?.Station == null) return false;
            foreach (var f in _gm.Station.foundations.Values)
                if (f.tileCol == col && f.tileRow == row) return true;
            return false;
        }

        private static List<(int col, int row)> BresenhamLine(int x0, int y0, int x1, int y1)
        {
            var line = new List<(int, int)>();
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            for (;;)
            {
                line.Add((x0, y0));
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
            return line;
        }

        private void DrawTileOverlay(Camera cam, int col, int row, Color fill, Color outline)
        {
            if (_white == null) return;
            Vector3 world  = new Vector3(col, row, 0f);
            Vector3 sp     = cam.WorldToScreenPoint(world);
            Vector3 spNext = cam.WorldToScreenPoint(world + Vector3.right);
            float   px     = Mathf.Abs(spNext.x - sp.x);
            float   gx     = sp.x    - px * 0.5f;
            float   gy     = Screen.height - sp.y - px * 0.5f;
            float   b      = Mathf.Max(2f, px * 0.03f);   // border width, min 2px

            var prev = GUI.color;
            GUI.color = fill;
            GUI.DrawTexture(new Rect(gx, gy, px, px), _white);
            GUI.color = outline;
            GUI.DrawTexture(new Rect(gx,          gy,          px, b),  _white);
            GUI.DrawTexture(new Rect(gx,          gy + px - b, px, b),  _white);
            GUI.DrawTexture(new Rect(gx,          gy,          b,  px), _white);
            GUI.DrawTexture(new Rect(gx + px - b, gy,          b,  px), _white);
            GUI.color = prev;
        }

        // ── Style setup ───────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            _sTabOff = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.70f, 0.78f, 0.92f), background = null },
                hover     = { textColor = Color.white,                     background = null },
                active    = { textColor = Color.white,                     background = null },
            };
            _sTabOff.normal.background  = null;
            _sTabOff.focused.background = null;

            _sTabOn = new GUIStyle(_sTabOff)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white, background = null },
            };

            _sHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 16,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white },
            };

            _sLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.85f, 0.92f, 1.00f) },
            };

            _sSub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.62f, 0.70f, 0.84f) },
                wordWrap = true,
            };

            _sBtnSmall = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.80f, 0.88f, 1.00f) },
            };

            _sBtnWide = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.85f, 0.92f, 1.00f) },
            };

            _sBtnDanger = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1.00f, 0.55f, 0.55f) },
            };

            _sTextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.85f, 0.90f, 1.00f) },
            };
        }
    }
}
