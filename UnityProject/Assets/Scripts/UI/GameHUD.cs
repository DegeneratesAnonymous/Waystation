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
        private enum Tab { None, Build, Crew, Station, Comms, AwayMission, Rooms, Views, Settings }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Build,       "Build"),
            (Tab.Crew,        "Crew"),
            (Tab.Station,     "Station"),
            (Tab.Comms,       "Comms"),
            (Tab.AwayMission, "Away"),
            (Tab.Rooms,       "Rooms"),
            (Tab.Views,       "Views"),
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
        private enum CrewSubPanel { Roster, Work, Departments, Ranks }
        private CrewSubPanel _crewSub = CrewSubPanel.Roster;
        private Vector2      _workScroll;
        private Vector2      _deptScroll;
        // ── Rename flow: uid of department being renamed, text buffer
        private string _renamingDeptUid  = "";
        private string _renameDeptBuffer = "";

        // ── Away Mission panel state ────────────────────────────────────
        private string               _selectedMissionDef  = "";
        private readonly HashSet<string> _selectedMissionCrew = new HashSet<string>();
        private Vector2              _missionScroll;
        private string               _missionMsg = "";

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

            // ── Ctrl+Z — undo last placement ────────────────────────────────────────────────
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.Z)
                && _undoStack.Count > 0 && _gm?.Station != null)
            {
                var toUndo = _undoStack.Pop();
                foreach (var uid in toUndo)
                    _gm.Building.UndoFoundation(_gm.Station, uid);
                _gm.UtilityNetworks.RebuildAll(_gm.Station);
                StationRoomView.Instance?.ForceRefreshFoundations();
            }

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
                // Snapshot all existing UIDs before placing so we can capture
                // auto-placed floors (and any other side-effect foundations) in
                // the undo entry, not just the primary placement UIDs.
                var beforeUids = new System.Collections.Generic.HashSet<string>(
                    _gm.Station.foundations.Keys);

                foreach (var (col, row) in _dragLine)
                {
                    if (!_dragBlocked.Contains((col, row)))
                        _gm.Building.PlaceFoundation(
                            _gm.Station, _ghostBuildableId, col, row, _ghostRotation);
                }

                // Everything that wasn't there before is undoable as one group.
                var allNew = new List<string>();
                foreach (var k in _gm.Station.foundations.Keys)
                    if (!beforeUids.Contains(k)) allNew.Add(k);
                if (allNew.Count > 0) _undoStack.Push(allNew);

                _gm.UtilityNetworks.RebuildAll(_gm.Station);
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

            // All multi-tile sprites use centre pivot; offset the ghost so its centre
            // aligns with the centre of the footprint in tile-centre coordinates.
            float ghostXOff = 0f;
            float ghostYOff = 0f;
            int   ghostTW   = 1, ghostTH = 1;
            if (placing && _gm?.Registry?.Buildables != null &&
                _gm.Registry.Buildables.TryGetValue(_ghostBuildableId, out var ghostDef))
            {
                ghostTW   = Mathf.Max(1, ghostDef.tileWidth);
                ghostTH   = Mathf.Max(1, ghostDef.tileHeight);
                ghostXOff = 0.5f * (ghostTW - 1);
                ghostYOff = 0.5f * (ghostTH - 1);
            }

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
                _ghostPool[i].transform.position = new Vector3(col + ghostXOff, row + ghostYOff, -0.1f);
                _ghostPool[i].transform.rotation = Quaternion.Euler(0f, 0f, _ghostRotation);

                var sr = _ghostPool[i].GetComponent<SpriteRenderer>();
                sr.sprite = spr;
                // Blocked if any tile in the footprint is occupied (for drag-line, use
                // the anchor check; for single placement, scan all footprint tiles).
                bool blocked;
                if (_isDragging && _dragLine.Count > 0)
                {
                    blocked = _dragBlocked.Contains((col, row));
                }
                else
                {
                    blocked = false;
                    for (int dy3 = 0; dy3 < ghostTH && !blocked; dy3++)
                    for (int dx3 = 0; dx3 < ghostTW && !blocked; dx3++)
                        blocked = IsTileOccupied(col + dx3, row + dy3);
                }
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
                        // Single cursor: highlight every tile in the object's footprint
                        int tw = 1, th = 1;
                        if (_gm?.Registry?.Buildables?.TryGetValue(_ghostBuildableId, out var fpDef) == true)
                        {
                            tw = Mathf.Max(1, fpDef.tileWidth);
                            th = Mathf.Max(1, fpDef.tileHeight);
                        }
                        for (int dy2 = 0; dy2 < th; dy2++)
                        for (int dx2 = 0; dx2 < tw; dx2++)
                        {
                            int fc = _ghostTileCol + dx2, fr = _ghostTileRow + dy2;
                            bool tileBlocked = IsTileOccupied(fc, fr);
                            Color edge = tileBlocked
                                ? new Color(1.00f, 0.50f, 0.35f, 0.85f)
                                : new Color(0.50f, 0.88f, 1.00f, 0.80f);
                            DrawTileOverlay(ghostCam, fc, fr, Color.clear, edge);
                        }
                    }

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
                    // Fixed bottom-left position instead of following the cursor
                    GUI.Label(new Rect(12f, Screen.height - 28f, 480f, 20f), hint, _sSub);
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
                Tab.Views       => "Views",
                Tab.Settings    => "Settings",
                _               => "",
            };

            // Close button (top-right of drawer header)
            Color cbPrev = GUI.color;
            GUI.color = new Color(0.55f, 0.60f, 0.70f, 0.85f);
            if (GUI.Button(new Rect(w - 28f, 10f, 22f, 22f), "\u00d7", _sBtnSmall))
                _active = Tab.None;
            GUI.color = cbPrev;

            GUI.Label(new Rect(Pad, 18f, w - 36f, 26f), title, _sHeader);
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
                case Tab.Views:       DrawViews(area, cw, contentH);       break;
                case Tab.Settings:    DrawSettings(area, cw, contentH);    break;
            }
        }

        // ── Build tab ─────────────────────────────────────────────────────────
        private Vector2 _buildScroll;
        private string  _buildInfoOpen     = "";  // buildable id whose info panel is expanded
        private string  _foundSettingsOpen = "";  // foundation uid whose cargo settings are open
        private string  _buildCategoryFilter = ""; // "" = all categories
        private bool    _deconstructMode   = false; // deconstruct-mode: click tile to cancel/demolish
        private bool    _showBuildQueue    = false; // toggle inline build-queue panel

        // ── Build sub-panel navigation ────────────────────────────────────────
        private enum BuildSubPanel { Place, Queue, Rooms }
        private BuildSubPanel _buildSub = BuildSubPanel.Place;

        // ── Room management state (Build > Rooms sub-panel) ───────────────────
        private Vector2 _buildRoomsScroll;
        private Vector2 _buildRoomTypesScroll;
        private bool    _buildRoomsOverlayActive = false;   // synced with sub-panel visibility
        private string  _editingRoomTypeId = "";            // "" = none; id = custom type being edited
        private bool    _creatingNewRoomType = false;
        // Buffers for custom room type editor
        private string  _newRoomTypeName     = "";
        private string  _newRoomTypeWbType   = "";
        private string  _newRoomTypeSkillKey = "";
        private string  _newRoomTypeSkillVal = "1.10";
        // Per-room name edit buffers: key = roomKey, value = text buffer
        private readonly Dictionary<string, string> _roomNameBuffers = new Dictionary<string, string>();
        // Track which rooms have their role picker open
        private string  _buildRoomRolePicker = "";   // roomKey with open picker, "" = none

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

        // Undo stack: each entry is the list of UIDs placed in one placement action.
        private readonly Stack<List<string>> _undoStack = new Stack<List<string>>();

        // True when cursor is over the HUD — used by CameraController to block map scroll/pan
        public static bool IsMouseOverDrawer { get; private set; }

        // True when ghost-placement or deconstruct mode is active — used by StationRoomView
        // to suppress NPC selection while the player is building.
        public static bool InBuildMode { get; private set; }

        private void DrawBuild(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;

            // ── Sub-panel nav (Place | Queue | Rooms) ────────────────────────
            const float NavH   = 28f;
            const float NavPad = 4f;
            float navBw = (w - NavPad * 4f) / 3f;
            float navY  = area.y + NavPad;

            DrawSolid(new Rect(area.x, area.y, w, NavH + NavPad), new Color(0.05f, 0.07f, 0.12f, 0.97f));

            Color navPrev = GUI.color;
            var subPanels = new[] { (BuildSubPanel.Place, "\u2692 Place"), (BuildSubPanel.Queue, "\u2261 Queue"), (BuildSubPanel.Rooms, "\u2b21 Rooms") };
            for (int i = 0; i < subPanels.Length; i++)
            {
                var (panel, label) = subPanels[i];
                bool isActive = _buildSub == panel;
                GUI.color = isActive ? new Color(0.35f, 0.62f, 1.00f, 1f) : new Color(0.55f, 0.60f, 0.70f, 1f);
                if (GUI.Button(new Rect(area.x + NavPad + i * (navBw + NavPad), navY, navBw, NavH - NavPad * 2f),
                               label, _sBtnSmall))
                {
                    _buildSub = panel;
                    if (panel == BuildSubPanel.Rooms) _buildRoomsOverlayActive = true;
                    else _buildRoomsOverlayActive = false;
                }
            }
            GUI.color = navPrev;

            Rect subArea = new Rect(area.x, area.y + NavH + NavPad, w, h - NavH - NavPad);

            if (_buildSub == BuildSubPanel.Place) DrawBuildPlace(subArea, w, h - NavH - NavPad);
            else if (_buildSub == BuildSubPanel.Queue) DrawBuildQueue(subArea, w, h - NavH - NavPad);
            else DrawBuildRooms(subArea, w, h - NavH - NavPad);
        }

        private void DrawBuildPlace(Rect area, float w, float h)
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
                    innerH += 56f;
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

        private void DrawBuildQueue(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s  = _gm.Station;
            float cw = w - 16f;

            float innerH = 24f + (s.foundations.Count == 0 ? 24f : s.foundations.Count * 72f);
            _buildScroll = GUI.BeginScrollView(
                new Rect(area.x, area.y, w, h),
                _buildScroll,
                new Rect(0, 0, cw, Mathf.Max(h, innerH)));

            float y = 4f;
            Section($"Build Queue  ({s.foundations.Count})", cw, ref y);
            if (s.foundations.Count == 0)
            {
                GUI.Label(new Rect(0, y, cw, 18f), "No foundations placed yet.", _sSub);
                y += 22f;
            }
            else
            {
                foreach (var kv in s.foundations)
                    DrawFoundationRow(kv.Value, cw, ref y, s);
            }

            GUI.EndScrollView();
        }

        private void DrawBuildRooms(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s   = _gm.Station;
            float cw = w - 16f;
            Color colPrev = GUI.color;

            // Discover all rooms by flood-filling every floor tile
            var discovered = new Dictionary<string, List<(int c, int r)>>();
            {
                var visited = new System.Collections.Generic.HashSet<(int, int)>();
                foreach (var f in s.foundations.Values)
                {
                    if (f.buildableId.Contains("floor") && !f.buildableId.Contains("wall") && f.status == "complete")
                    {
                        var pos = (f.tileCol, f.tileRow);
                        if (visited.Contains(pos)) continue;
                        string key = FloodFillRoom(f.tileCol, f.tileRow, out var tiles);
                        if (key != null)
                        {
                            foreach (var t in tiles) visited.Add(t);
                            if (!discovered.ContainsKey(key)) discovered[key] = tiles;
                        }
                    }
                }
            }

            // ── Estimate content height ───────────────────────────────────────
            float innerH = 8f;
            innerH += 24f; // section header "Rooms"
            foreach (var kv in discovered)
            {
                innerH += 36f;  // room header
                innerH += 24f;  // name field row
                innerH += 24f;  // role row
                bool hasBonus = false;
                foreach (var bk in s.roomBonusCache.Keys)
                    if (bk.StartsWith(kv.Key + "_")) { hasBonus = true; break; }
                if (hasBonus)
                {
                    foreach (var bkv in s.roomBonusCache)
                        if (bkv.Key.StartsWith(kv.Key + "_"))
                            innerH += 28f + bkv.Value.requirements.Count * 20f + 16f;
                }
                else
                    innerH += 20f;
                innerH += 8f; // gap
            }
            innerH += 24f; // section header "Room Types"
            int typeCount = (_gm.Registry?.RoomTypes?.Count ?? 0) + s.customRoomTypes.Count;
            innerH += typeCount * 28f + 60f; // type rows + new button

            _buildRoomsScroll = GUI.BeginScrollView(
                new Rect(area.x, area.y, w, h),
                _buildRoomsScroll,
                new Rect(0, 0, cw, Mathf.Max(h, innerH)));

            float y = 4f;

            // ── SECTION: Rooms ────────────────────────────────────────────────
            Section("Rooms", cw, ref y);

            if (discovered.Count == 0)
            {
                GUI.color = new Color(0.55f, 0.58f, 0.65f);
                GUI.Label(new Rect(4f, y, cw - 8f, 18f), "No rooms found. Place floors to define rooms.", _sSub);
                GUI.color = colPrev;
                y += 22f;
            }

            foreach (var kv in discovered)
            {
                string roomKey = kv.Key;

                // Look up bonus + role
                s.roomRoles.TryGetValue(roomKey, out string roleId);
                string roleName = "";
                Color  roleColor = new Color(0.40f, 0.40f, 0.45f, 0.40f);
                foreach (var rr in RoomRoles) if (rr.id == roleId) { roleName = rr.label; roleColor = rr.col; break; }

                // Custom name
                if (!_roomNameBuffers.ContainsKey(roomKey))
                    _roomNameBuffers[roomKey] = s.customRoomNames.TryGetValue(roomKey, out var cn) ? cn : "";

                // ── Room header ───────────────────────────────────────────────
                DrawSolid(new Rect(0, y, cw, 32f), new Color(0.10f, 0.14f, 0.22f, 0.85f));
                DrawSolid(new Rect(0, y, 4f, 32f),  new Color(roleColor.r, roleColor.g, roleColor.b, 0.9f));
                string displayRoomName = (s.customRoomNames.TryGetValue(roomKey, out var crn) && !string.IsNullOrEmpty(crn))
                    ? crn : $"Room {roomKey}";
                GUI.Label(new Rect(8f, y + 2f, cw * 0.65f, 16f), displayRoomName, _sLabel);
                if (!string.IsNullOrEmpty(roleName))
                {
                    GUI.color = new Color(roleColor.r, roleColor.g, roleColor.b, 0.85f);
                    GUI.Label(new Rect(8f, y + 18f, cw - 12f, 12f), roleName, _sSub);
                    GUI.color = colPrev;
                }
                // Role change button
                if (GUI.Button(new Rect(cw - 78f, y + 5f, 74f, 20f), "Change Role", _sBtnSmall))
                    _buildRoomRolePicker = _buildRoomRolePicker == roomKey ? "" : roomKey;
                y += 36f;

                // ── Role picker ───────────────────────────────────────────────
                if (_buildRoomRolePicker == roomKey)
                {
                    DrawSolid(new Rect(4f, y, cw - 4f, (RoomRoles.Length + 1) * 22f + 4f), new Color(0.07f, 0.10f, 0.18f, 0.95f));
                    float ry = y + 2f;
                    foreach (var rr in RoomRoles)
                    {
                        bool cur = roleId == rr.id || (string.IsNullOrEmpty(rr.id) && string.IsNullOrEmpty(roleId));
                        GUI.color = cur ? new Color(0.35f, 0.65f, 0.95f) : Color.white;
                        if (GUI.Button(new Rect(8f, ry, cw - 12f, 20f), rr.label, _sBtnSmall))
                        {
                            if (string.IsNullOrEmpty(rr.id)) s.roomRoles.Remove(roomKey);
                            else s.roomRoles[roomKey] = rr.id;
                            _buildRoomRolePicker = "";
                        }
                        GUI.color = colPrev;
                        ry += 22f;
                    }
                    y += (RoomRoles.Length + 1) * 22f + 4f;
                }

                // ── Custom name field ─────────────────────────────────────────
                GUI.Label(new Rect(4f, y + 3f, 42f, 16f), "Name:", _sSub);
                _roomNameBuffers[roomKey] = GUI.TextField(
                    new Rect(50f, y, cw - 120f, 20f),
                    _roomNameBuffers[roomKey] ?? "", 48, _sTextField);
                if (GUI.Button(new Rect(cw - 66f, y, 62f, 20f), "Save", _sBtnSmall))
                {
                    string trimmed = (_roomNameBuffers[roomKey] ?? "").Trim();
                    if (string.IsNullOrEmpty(trimmed)) s.customRoomNames.Remove(roomKey);
                    else s.customRoomNames[roomKey] = trimmed;
                }
                y += 26f;

                // ── Bonus cards ───────────────────────────────────────────────
                bool anyBonus = false;
                foreach (var bkv in s.roomBonusCache)
                {
                    if (!bkv.Key.StartsWith(roomKey + "_")) continue;
                    anyBonus = true;
                    var bs = bkv.Value;

                    float cardH = 28f + bs.requirements.Count * 20f + 16f;
                    DrawSolid(new Rect(4f, y, cw - 4f, cardH), new Color(0.06f, 0.09f, 0.16f, 0.75f));

                    // Header row: type name + status
                    string typeName = bs.displayName ?? bs.workbenchRoomType ?? "Unknown";
                    if (bs.bonusActive)
                    {
                        GUI.color = new Color(0.90f, 0.78f, 0.20f, 1f);
                        GUI.Label(new Rect(10f, y + 4f, cw - 90f, 16f), $"\u2605 {typeName}", _sLabel);
                        GUI.color = new Color(0.85f, 0.72f, 0.18f, 0.9f);
                        GUI.Label(new Rect(cw - 86f, y + 5f, 80f, 14f), "BONUS ACTIVE", _sSub);
                    }
                    else
                    {
                        GUI.color = new Color(0.65f, 0.65f, 0.75f, 1f);
                        GUI.Label(new Rect(10f, y + 4f, cw - 90f, 16f), $"\u25c7 {typeName}", _sLabel);
                        GUI.color = new Color(0.50f, 0.50f, 0.60f, 0.9f);
                        GUI.Label(new Rect(cw - 68f, y + 5f, 62f, 14f), "inactive", _sSub);
                    }
                    GUI.color = colPrev;

                    // Workbench count
                    int cap = 3;
                    if (_gm.Registry?.RoomTypes?.TryGetValue(bs.workbenchRoomType ?? "", out var rtd) == true)
                        cap = rtd.workbenchCap;
                    string wbLabel = bs.workbenchCount > cap
                        ? $"Workbenches: {bs.workbenchCount} (cap {cap} earn bonus)"
                        : $"Workbenches: {bs.workbenchCount}/{cap}";
                    GUI.Label(new Rect(10f, y + 22f, cw - 14f, 14f), wbLabel, _sSub);

                    float ry2 = y + 28f;

                    // Requirements checklist
                    foreach (var req in bs.requirements)
                    {
                        bool met = req.IsMet;
                        GUI.color = met ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.85f, 0.45f, 0.25f);
                        string checkMark = met ? "\u2713" : "\u2717";
                        string reqTxt = met
                            ? $"{checkMark} {req.displayLabel}:  {req.current}/{req.required}"
                            : $"{checkMark} {req.displayLabel}:  {req.current}/{req.required}  (+{req.required - req.current} needed)";
                        GUI.Label(new Rect(10f, ry2, cw - 14f, 16f), reqTxt, _sSub);
                        GUI.color = colPrev;
                        ry2 += 20f;
                    }

                    y += cardH;
                }

                if (!anyBonus)
                {
                    GUI.color = new Color(0.45f, 0.48f, 0.55f, 0.8f);
                    GUI.Label(new Rect(8f, y, cw - 12f, 16f),
                        "No workbench in this room \u2014 place a workbench to define a room type.", _sSub);
                    GUI.color = colPrev;
                    y += 20f;
                }

                y += 8f; // gap
            }

            Divider(cw, ref y);

            // ── SECTION: Room Type Definitions ────────────────────────────────
            Section("Room Type Definitions", cw, ref y);

            // Built-in types (view only)
            if (_gm.Registry?.RoomTypes != null)
            {
                foreach (var rtKv in _gm.Registry.RoomTypes)
                {
                    var rt = rtKv.Value;
                    DrawSolid(new Rect(0, y, cw, 26f), new Color(0.08f, 0.10f, 0.18f, 0.7f));
                    GUI.color = new Color(0.55f, 0.62f, 0.75f);
                    GUI.Label(new Rect(4f, y + 4f, cw * 0.55f, 16f), rt.displayName, _sSub);
                    GUI.color = new Color(0.38f, 0.42f, 0.50f);
                    GUI.Label(new Rect(cw * 0.56f, y + 4f, cw * 0.24f, 16f), "(built-in)", _sSub);
                    GUI.color = colPrev;
                    // Show skill bonus
                    string bonusTxt = "";
                    foreach (var skv in rt.skillBonuses)
                        bonusTxt = $"+{(skv.Value - 1f) * 100f:F0}% {skv.Key}";
                    GUI.Label(new Rect(cw * 0.80f, y + 4f, cw * 0.20f, 16f), bonusTxt, _sSub);
                    y += 28f;
                }
            }

            // Custom types
            RoomTypeDefinition toDelete = null;
            foreach (var ct in s.customRoomTypes)
            {
                DrawSolid(new Rect(0, y, cw, 26f), new Color(0.09f, 0.12f, 0.14f, 0.8f));
                DrawSolid(new Rect(0, y, 3f, 26f), new Color(0.35f, 0.62f, 1.00f, 0.8f));
                GUI.Label(new Rect(6f, y + 4f, cw * 0.55f, 16f), ct.displayName, _sSub);
                GUI.color = new Color(0.35f, 0.62f, 1.00f, 0.8f);
                GUI.Label(new Rect(cw * 0.56f, y + 4f, cw * 0.18f, 16f), "(custom)", _sSub);
                GUI.color = colPrev;
                if (GUI.Button(new Rect(cw - 58f, y + 3f, 54f, 20f), "\u2715 Delete", _sBtnDanger))
                    toDelete = ct;
                y += 28f;
            }
            if (toDelete != null) s.customRoomTypes.Remove(toDelete);

            // ── New custom type creator ────────────────────────────────────────
            y += 4f;
            GUI.color = new Color(0.35f, 0.62f, 1.00f, 0.9f);
            if (!_creatingNewRoomType)
            {
                if (GUI.Button(new Rect(4f, y, cw - 8f, 24f), "+ Define Custom Room Type", _sBtnSmall))
                {
                    _creatingNewRoomType  = true;
                    _newRoomTypeName     = "";
                    _newRoomTypeWbType   = "";
                    _newRoomTypeSkillKey = "engineering";
                    _newRoomTypeSkillVal = "1.10";
                }
                y += 28f;
            }
            else
            {
                GUI.color = colPrev;
                DrawSolid(new Rect(0, y, cw, 100f), new Color(0.07f, 0.10f, 0.16f, 0.9f));
                DrawSolid(new Rect(0, y, cw, 1f), new Color(0.35f, 0.62f, 1.00f, 0.6f));

                float fy = y + 4f;
                GUI.Label(new Rect(4f, fy, 72f, 16f), "Name:", _sSub);
                _newRoomTypeName = GUI.TextField(new Rect(80f, fy, cw - 84f, 20f),
                    _newRoomTypeName, 48, _sTextField);
                fy += 24f;

                GUI.Label(new Rect(4f, fy, 72f, 16f), "Workbench:", _sSub);
                _newRoomTypeWbType = GUI.TextField(new Rect(80f, fy, cw - 84f, 20f),
                    _newRoomTypeWbType, 48, _sTextField);
                fy += 24f;

                GUI.Label(new Rect(4f, fy, 72f, 16f), "Skill:", _sSub);
                _newRoomTypeSkillKey = GUI.TextField(new Rect(80f, fy, (cw - 84f) * 0.55f, 20f),
                    _newRoomTypeSkillKey, 24, _sTextField);
                _newRoomTypeSkillVal = GUI.TextField(new Rect(80f + (cw - 84f) * 0.57f, fy, (cw - 84f) * 0.43f, 20f),
                    _newRoomTypeSkillVal, 8, _sTextField);
                fy += 26f;

                if (GUI.Button(new Rect(4f, fy, (cw - 10f) * 0.5f, 22f), "Create", _sBtnSmall))
                {
                    string trimName = _newRoomTypeName.Trim();
                    string trimWb   = _newRoomTypeWbType.Trim();
                    if (!string.IsNullOrEmpty(trimName) && !string.IsNullOrEmpty(trimWb))
                    {
                        float bonusVal = 1.10f;
                        float.TryParse(_newRoomTypeSkillVal, out bonusVal);
                        var newType = new RoomTypeDefinition
                        {
                            id          = "custom_" + trimWb.Replace(" ", "_").ToLower(),
                            displayName = trimName,
                            isBuiltIn   = false,
                            workbenchCap = 3,
                            requirementsPerWorkbench = new List<RoomFurnitureRequirement>(),
                            skillBonuses = new Dictionary<string, float>(),
                        };
                        if (!string.IsNullOrEmpty(_newRoomTypeSkillKey))
                            newType.skillBonuses[_newRoomTypeSkillKey.Trim()] = bonusVal;
                        s.customRoomTypes.Add(newType);
                        _creatingNewRoomType = false;
                    }
                }
                if (GUI.Button(new Rect(4f + (cw - 10f) * 0.52f, fy, (cw - 10f) * 0.48f, 22f), "Cancel", _sBtnSmall))
                    _creatingNewRoomType = false;

                y += 102f;
            }
            GUI.color = colPrev;

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
                {
                    _gm.Building.CancelFoundation(s, f.uid, refund: true);
                    _gm.UtilityNetworks.RebuildAll(s);
                }
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

            DrawSolid(new Rect(0, y, cw, 56f),
                new Color(0.07f, 0.09f, 0.15f, canPlace ? 0.6f : 0.4f));

            GUI.Label(new Rect(4f, y + 4f, cw - 60f, 18f), defn.displayName, _sLabel);
            GUI.color = prevCol;
            if (GUI.Button(new Rect(cw - 52f, y + 4f, 48f, 18f),
                           infoOpen ? "\u25b2 Info" : "\u25bc Info", _sBtnSmall))
                _buildInfoOpen = infoOpen ? "" : defn.id;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            GUI.Label(new Rect(4f, y + 26f, cw * 0.60f, 14f),
                      $"{CategoryDisplayName(defn.category).Replace("\u2500\u2500 ","").Replace(" \u2500\u2500","")}  \u00b7  {defn.buildTimeTicks}t", _sSub);

            if (!canPlace && reason != null)
            {
                GUI.color = new Color(1f, 0.55f, 0.2f, alpha);
                GUI.Label(new Rect(4f, y + 26f, cw * 0.58f, 18f), $"\u2715 {reason}", _sSub);
                GUI.color = new Color(1f, 1f, 1f, alpha);
            }
            GUI.enabled = canPlace;
            if (GUI.Button(new Rect(cw * 0.62f, y + 33f, cw * 0.38f, 21f), "+ Place", _sBtnSmall))
            {
                _ghostBuildableId = defn.id;
                _ghostRotation    = 0;
                _active           = Tab.None;
            }
            GUI.enabled = true;
            GUI.color   = prevCol;

            y += 56f;

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
            bool hadCancels = false;
            foreach (var kv in s.foundations)
            {
                var f = kv.Value;
                if (f.tileCol == col && f.tileRow == row)
                {
                    if (f.status == "complete")
                        toRemove.Add(f.uid);
                    else
                    {
                        _gm.Building.CancelFoundation(s, f.uid, refund: true);
                        hadCancels = true;
                    }
                }
            }
            foreach (var uid in toRemove)
                s.foundations.Remove(uid);
            // Rebuild networks after any removal: complete foundations may be network
            // members (wires, pipes), and cancelled pending foundations may also have
            // been part of a partial network segment.
            if (toRemove.Count > 0 || hadCancels)
                _gm.UtilityNetworks.RebuildAll(s);
        }

        // ── Crew tab ──────────────────────────────────────────────────────────
        private void DrawCrew(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;

            // ── Sub-panel selector ────────────────────────────────────────────
            const float SubTabH = 28f;
            float subY = area.y;
            float subBw = (w - 12f) / 4f;

            if (GUI.Button(new Rect(area.x,                   subY, subBw, SubTabH),
                           "Roster",  _crewSub == CrewSubPanel.Roster      ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Roster;
            if (GUI.Button(new Rect(area.x + (subBw + 4f),    subY, subBw, SubTabH),
                           "Work",    _crewSub == CrewSubPanel.Work        ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Work;
            if (GUI.Button(new Rect(area.x + (subBw + 4f) * 2f, subY, subBw, SubTabH),
                           "Depts",   _crewSub == CrewSubPanel.Departments ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Departments;
            if (GUI.Button(new Rect(area.x + (subBw + 4f) * 3f, subY, subBw, SubTabH),
                           "Ranks",   _crewSub == CrewSubPanel.Ranks       ? _sTabOn : _sTabOff))
                _crewSub = CrewSubPanel.Ranks;

            Rect subArea = new Rect(area.x, area.y + SubTabH + 6f, w, h - SubTabH - 6f);
            float subH   = h - SubTabH - 6f;

            switch (_crewSub)
            {
                case CrewSubPanel.Roster:      DrawCrewRoster(subArea, w, subH);  break;
                case CrewSubPanel.Work:        DrawCrewWork(subArea, w, subH);    break;
                case CrewSubPanel.Departments: DrawDepartments(subArea, w, subH); break;
                case CrewSubPanel.Ranks:       DrawRanks(subArea, w, subH);       break;
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
                string cls  = (npc.classId ?? "").Replace("class.", "");
                string job  = _gm.Jobs.GetJobLabel(npc);
                string rank = npc.rank switch { 1 => "★ Officer", 2 => "★★ Senior", 3 => "★★★ Command", _ => "" };
                // Name row — rank badge on right when promoted
                GUI.Label(new Rect(0,        y,        w * 0.60f, 20f), npc.name,        _sLabel);
                if (rank.Length > 0)
                {
                    var rPrev = GUI.color; GUI.color = new Color(1.00f, 0.85f, 0.25f);
                    GUI.Label(new Rect(w * 0.62f, y + 2f, w * 0.38f, 16f), rank, _sSub);
                    GUI.color = rPrev;
                }
                else
                {
                    GUI.Label(new Rect(w * 0.62f, y,      w * 0.38f, 18f), npc.MoodLabel(), _sSub);
                }
                GUI.Label(new Rect(0, y + 20f, w, 16f), rank.Length > 0 ? $"{cls}  ·  {npc.MoodLabel()}" : cls, _sSub);
                // Department label
                string deptLabel = "Crewman";
                if (npc.departmentId != null)
                    foreach (var d in _gm.Station.departments)
                        if (d.uid == npc.departmentId) { deptLabel = d.name; break; }
                GUI.Label(new Rect(0, y + 36f, w * 0.60f, 16f), deptLabel, _sSub);
                GUI.Label(new Rect(0, y + 52f, w * 0.60f, 16f), $"Job: {job}", _sSub);
                if (GUI.Button(new Rect(w * 0.62f, y + 50f, w * 0.38f, 18f),
                               "Reassign", _sBtnSmall))
                    _gm.Jobs.InterruptNpc(npc);
                NeedBar("Hunger", GetNeed(npc, "hunger"), w, y + 72f);
                NeedBar("Rest",   GetNeed(npc, "rest"),   w, y + 88f);
                NeedBar("Sleep",  GetNeed(npc, "sleep"),  w, y + 104f);
                if (npc.missionUid != null)
                {
                    var prev2 = GUI.color; GUI.color = new Color(0.4f, 0.8f, 1f);
                    GUI.Label(new Rect(0, y + 120f, w - 14f, 14f), "\u2708 On away mission", _sSub);
                    GUI.color = prev2;
                }
                else if (npc.statusTags.Count > 0 || npc.injuries > 0)
                {
                    string tags = string.Join(", ", npc.statusTags);
                    if (npc.injuries > 0)
                        tags += (tags.Length > 0 ? ", " : "") +
                                $"{npc.injuries} injur{(npc.injuries == 1 ? "y" : "ies")}";
                    var prev2 = GUI.color; GUI.color = ColBarWarn;
                    GUI.Label(new Rect(0, y + 120f, w - 14f, 14f), tags, _sSub);
                    GUI.color = prev2;
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

        // ── Ranks sub-panel ───────────────────────────────────────────────────
        private string[] _rankEditBuffers = null;

        private void DrawRanks(Rect area, float w, float h)
        {
            var s = _gm.Station;

            // Ensure edit buffers are in sync with the station's rankNames list.
            if (_rankEditBuffers == null || _rankEditBuffers.Length != s.rankNames.Count)
                _rankEditBuffers = s.rankNames.ToArray();

            GUI.Label(new Rect(area.x, area.y, w, 18f),
                "Rename the four crew ranks for this station.", _sSub);

            static string Stars(int n) => n == 0 ? "○" : new string('\u2605', n);

            float y = area.y + 26f;
            for (int i = 0; i < s.rankNames.Count; i++)
            {
                DrawSolid(new Rect(area.x, y, w, 36f), new Color(0.10f, 0.12f, 0.20f, 0.85f));

                // Star badge
                Color sc = i == 0 ? new Color(0.50f, 0.55f, 0.65f)
                                  : new Color(1.00f, 0.85f, 0.25f);
                Color prev = GUI.color; GUI.color = sc;
                GUI.Label(new Rect(area.x + 4f, y + 9f, 30f, 18f), Stars(i), _sSub);
                GUI.color = prev;

                // Editable name field
                _rankEditBuffers[i] = GUI.TextField(
                    new Rect(area.x + 34f, y + 8f, w - 96f, 20f),
                    _rankEditBuffers[i] ?? s.rankNames[i], 30, _sTextField);

                // Apply button
                if (GUI.Button(new Rect(area.x + w - 58f, y + 8f, 54f, 20f), "Apply", _sBtnSmall))
                {
                    string trimmed = (_rankEditBuffers[i] ?? "").Trim();
                    if (trimmed.Length > 0)
                        s.rankNames[i] = trimmed;
                    else
                        _rankEditBuffers[i] = s.rankNames[i]; // revert blank
                }

                y += 42f;
            }

            // ── Crew breakdown by rank ─────────────────────────────────────────
            y += 6f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 10f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Current roster by rank:", _sSub); y += 20f;

            int[] counts = new int[s.rankNames.Count];
            foreach (var npc in s.npcs.Values)
                if (npc.IsCrew() && npc.rank >= 0 && npc.rank < counts.Length)
                    counts[npc.rank]++;

            for (int i = 0; i < s.rankNames.Count; i++)
            {
                string label = $"{Stars(i)}  {s.rankNames[i]}:  {counts[i]}";
                GUI.Label(new Rect(area.x + 8f, y, w - 8f, 16f), label, _sSub);
                y += 18f;
            }
        }

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
            if (_gm?.Station == null || _gm.Missions == null)
            {
                GUI.Label(new Rect(area.x, area.y, w, 20f), "Missions not available.", _sSub);
                return;
            }
            var s    = _gm.Station;
            var defs = _gm.Missions.AvailableDefinitions();
            var crew = s.GetCrew();

            float y = area.y;

            // ── Mission picker ──────────────────────────────────────
            GUI.Label(new Rect(area.x, y, w, 18f), "Select mission:", _sSub);
            y += 20f;
            foreach (var def in defs)
            {
                bool sel = _selectedMissionDef == def.id;
                GUI.color = sel ? new Color(0.35f, 0.62f, 1f) : Color.white;
                if (GUI.Button(new Rect(area.x, y, w * 0.70f, 22f), def.displayName, _sBtnSmall))
                {
                    _selectedMissionDef = sel ? "" : def.id;
                    _selectedMissionCrew.Clear();
                    _missionMsg = "";
                }
                GUI.color = Color.white;
                GUI.Label(new Rect(area.x + w * 0.72f, y + 3f, w * 0.28f, 16f),
                          $"{def.durationTicks}t | {def.crewRequired}cr", _sSub);
                y += 24f;
            }

            if (string.IsNullOrEmpty(_selectedMissionDef)) { return; }
            if (!_gm.Registry.Missions.TryGetValue(_selectedMissionDef, out var selDef)) return;

            y += 6f; DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 10f;

            // ── Crew selection ────────────────────────────────────────
            GUI.Label(new Rect(area.x, y, w, 18f),
                $"Select crew ({_selectedMissionCrew.Count}/{selDef.crewRequired} needed):", _sSub);
            y += 20f;

            foreach (var npc in crew)
            {
                bool onMission  = npc.missionUid != null;
                bool selected   = _selectedMissionCrew.Contains(npc.uid);
                GUI.enabled = !onMission;
                GUI.color   = selected ? new Color(0.35f, 0.72f, 0.45f) : onMission ? new Color(0.5f, 0.5f, 0.5f) : Color.white;
                string label = onMission ? $"{npc.name} (away)" : npc.name;
                if (GUI.Button(new Rect(area.x, y, w * 0.75f, 20f), label, _sBtnSmall))
                {
                    if (selected) _selectedMissionCrew.Remove(npc.uid);
                    else          _selectedMissionCrew.Add(npc.uid);
                    _missionMsg = "";
                }
                GUI.color   = Color.white;
                GUI.enabled = true;
                int sk = npc.skills.ContainsKey(selDef.requiredSkill) ? npc.skills[selDef.requiredSkill] : 0;
                GUI.Label(new Rect(area.x + w * 0.78f, y + 2f, w * 0.22f, 16f),
                          $"{selDef.requiredSkill[..Mathf.Min(3, selDef.requiredSkill.Length)]}:{sk}", _sSub);
                y += 22f;
            }

            y += 4f;
            if (!string.IsNullOrEmpty(_missionMsg))
            {
                GUI.color = _missionMsg.StartsWith("!") ? ColBarWarn : ColBarGreen;
                GUI.Label(new Rect(area.x, y, w, 18f), _missionMsg.TrimStart('!'), _sSub);
                GUI.color = Color.white;
                y += 20f;
            }

            GUI.enabled = _selectedMissionCrew.Count >= selDef.crewRequired;
            if (GUI.Button(new Rect(area.x, y, w, 26f), $"\u2708 Dispatch \"{selDef.displayName}\"", _sBtnSmall))
            {
                var crewList = new List<string>(_selectedMissionCrew);
                var (ok, reason, _) = _gm.Missions.DispatchMission(_selectedMissionDef, crewList, s);
                _missionMsg = ok ? $"Dispatched!" : $"!{reason}";
                if (ok) { _selectedMissionCrew.Clear(); _selectedMissionDef = ""; }
            }
            GUI.enabled = true;
            y += 30f;

            // ── Active missions ────────────────────────────────────────
            if (s.missions.Count > 0)
            {
                y += 6f; DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 10f;
                GUI.Label(new Rect(area.x, y, w, 18f), "Active Missions:", _sSub);
                y += 20f;
                foreach (var kv in s.missions)
                {
                    var m = kv.Value;
                    int remaining = Mathf.Max(0, m.endTick - s.tick);
                    string status = m.status == "active" ? $"~{remaining}t left" : m.status;
                    GUI.Label(new Rect(area.x, y, w * 0.70f, 18f), m.displayName, _sLabel);
                    GUI.Label(new Rect(area.x + w * 0.72f, y + 2f, w * 0.28f, 16f), status, _sSub);
                    y += 20f;
                }
            }
        }

        // ── Views tab ───────────────────────────────────────────────────────────
        private void DrawViews(Rect area, float cw, float h)
        {
            var srv = StationRoomView.Instance;
            float y = area.y;

            GUI.Label(new Rect(area.x, y, cw, 18f), "Select an overlay view:", _sSub);
            y += 24f;

            var modes = new[]
            {
                (StationRoomView.ViewMode.Normal,      "Normal",      "Default view"),
                (StationRoomView.ViewMode.Electricity, "Electricity", "Show electric networks"),
                (StationRoomView.ViewMode.Pipes,       "Pipes",       "Show fluid pipes"),
                (StationRoomView.ViewMode.Ducts,       "Ducts",       "Show gas ducts"),
                (StationRoomView.ViewMode.Temperature, "Temperature", "Room temperature heatmap"),
                (StationRoomView.ViewMode.Beauty,      "Beauty",      "Room beauty heatmap"),
                (StationRoomView.ViewMode.Pressurized, "Pressurized", "Pressurisation overlay"),
            };

            StationRoomView.ViewMode cur = srv != null ? StationRoomView.Instance.ActiveViewMode : StationRoomView.ViewMode.Normal;

            foreach (var (mode, label, hint) in modes)
            {
                bool active2 = cur == mode;
                GUI.color = active2 ? new Color(0.35f, 0.62f, 1f) : new Color(0.55f, 0.60f, 0.70f);
                if (GUI.Button(new Rect(area.x, y, cw * 0.50f, 24f), label, _sBtnSmall))
                    srv?.SetViewMode(mode);
                GUI.color = Color.white;
                GUI.Label(new Rect(area.x + cw * 0.53f, y + 4f, cw * 0.47f, 16f), hint, _sSub);
                y += 28f;
            }
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
            float y = area.y;
            GUI.Label(new Rect(area.x, y, w - 8f, 18f),
                "Room overlay active. Rooms are highlighted by role.", _sSub);
            y += 22f;

            // Legend
            foreach (var rr in RoomRoles)
            {
                if (string.IsNullOrEmpty(rr.id)) continue;
                DrawSolid(new Rect(area.x + 4f, y + 2f, 14f, 14f),
                    new Color(rr.col.r, rr.col.g, rr.col.b, 0.85f));
                GUI.Label(new Rect(area.x + 22f, y, w - 28f, 16f), rr.label, _sSub);
                y += 18f;
            }

            y += 10f;
            DrawSolid(new Rect(area.x, y, w - 8f, 1f), new Color(0.25f, 0.35f, 0.55f, 0.5f));
            y += 8f;

            GUI.color = new Color(0.50f, 0.62f, 0.90f, 0.9f);
            GUI.Label(new Rect(area.x, y, w - 8f, 32f),
                "Use  Build \u2192 Rooms  to manage room assignments and room type bonuses.", _sSub);
            GUI.color = Color.white;
            y += 38f;

            if (GUI.Button(new Rect(area.x + 4f, y, w - 16f, 24f), "Open Build > Rooms", _sBtnSmall))
            {
                _active   = Tab.Build;
                _buildSub = BuildSubPanel.Rooms;
            }
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

            // Close button (mirrors the right-drawer × button)
            Color cbPrev = GUI.color;
            GUI.color = new Color(0.55f, 0.60f, 0.70f, 0.85f);
            if (GUI.Button(new Rect(w - 28f, 10f, 22f, 22f), "\u00d7", _sBtnSmall))
                _devDrawerOpen = false;
            GUI.color = cbPrev;

            GUI.Label(new Rect(Pad, 18f, cw - 28f, 26f), "Dev Tools", _sHeader);
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

            // Determine effective footprint size (respects rotation: 90/270 swaps W and H)
            int tw = 1, th = 1;
            if (_ghostBuildableId != null &&
                _gm?.Registry?.Buildables?.TryGetValue(_ghostBuildableId, out var dragDef) == true)
            {
                bool swapped = _ghostRotation == 90 || _ghostRotation == 270;
                tw = Mathf.Max(1, swapped ? dragDef.tileHeight : dragDef.tileWidth);
                th = Mathf.Max(1, swapped ? dragDef.tileWidth  : dragDef.tileHeight);
            }

            if (_dragRect)
            {
                // Rect fill: step anchor grid by tw × th so placements don't overlap
                int cMin = Mathf.Min(_dragStartCol, _ghostTileCol);
                int cMax = Mathf.Max(_dragStartCol, _ghostTileCol);
                int rMin = Mathf.Min(_dragStartRow, _ghostTileRow);
                int rMax = Mathf.Max(_dragStartRow, _ghostTileRow);
                for (int r = rMin; r <= rMax; r += th)
                for (int c = cMin; c <= cMax; c += tw)
                {
                    _dragLine.Add((c, r));
                    bool blocked = false;
                    for (int dy = 0; dy < th && !blocked; dy++)
                    for (int dx = 0; dx < tw && !blocked; dx++)
                        if (IsTileOccupied(c + dx, r + dy)) blocked = true;
                    if (blocked) _dragBlocked.Add((c, r));
                }
            }
            else
            {
                // Line drag: stride Bresenham anchors so they don't overlap footprints
                var raw = BresenhamLine(_dragStartCol, _dragStartRow, _ghostTileCol, _ghostTileRow);
                int step = Mathf.Max(tw, th);  // stride by larger dimension
                for (int i = 0; i < raw.Count; i += step)
                {
                    var (c, r) = raw[i];
                    _dragLine.Add((c, r));
                    bool blocked = false;
                    for (int dy = 0; dy < th && !blocked; dy++)
                    for (int dx = 0; dx < tw && !blocked; dx++)
                        if (IsTileOccupied(c + dx, r + dy)) blocked = true;
                    if (blocked) _dragBlocked.Add((c, r));
                }
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
