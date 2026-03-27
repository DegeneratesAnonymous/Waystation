// GameHUD — right-side taskbar with animated slide-out drawer.
//
// Taskbar: 68 px vertical strip flush to right edge.
// Drawer:  320 px panel that slides out to the left of the taskbar.
//
// Tabs: Designer · Crew · Station · Comms · Away Mission · Rooms · Settings
//
// Self-installs via RuntimeInitializeOnLoadMethod; sets DemoBootstrap.HideOverlay
// so the legacy IMGUI stats box is suppressed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private const float TabW       = 100f;  // wide enough for 8-char labels at ~10px/char
        private const float DrawerW    = 400f;
        private const float SubDrawerW = 280f;   // secondary detail panel (slides left of main drawer)
        private const float DevDrawerW = 220f;
        private const float Pad        = 12f;
        private const float AnimK      = 12f;

        // ── Font & layout scale ───────────────────────────────────────────────
        // Change FontSize here to resize the whole UI. Everything derives from it.
        private const int   FontSize    = 12;   // body text (px) — use multiples of 2 for crispest rendering
        private const int   FontSizeHdr = 15;   // panel / section headers — style guide max
        private static float LineH  => FontSize + 10f;   // one text row: glyph + descenders + gap (22f)
        private static float BtnH   => FontSize + 16f;   // standard button height (28f)
        private static float CharW  => FontSize * 1.0f;  // approx char width for label-width math (12f)
        private static float LblH      => LineH - 4f;        // standard label height (18f @ FontSize 12)
        private static float SmH       => LineH - 6f;        // small label height (16f @ FontSize 12)
        private static float AvatarSz  => LineH + 8f;        // avatar square side (30f @ FontSize 12)
        private static float CrewCardH => LineH * 2f;        // compact crew-card height (44f @ FontSize 12)
        private static float DeptHdrH  => LineH + 10f;       // dept header height (32f @ FontSize 12)
        private static float NpcHdrH   => LineH * 2f + 14f;  // NPC detail sticky header height (58f @ FontSize 12)
        private static float NpcTabH   => LineH + 8f;        // NPC detail sub-tab bar height (30f @ FontSize 12)
        private static float WrkHdrH   => LineH * 2f + 4f;   // work grid header height (48f @ FontSize 12)
        private static float WrkRowH   => BtnH;              // work grid row height (28f @ FontSize 12)

        // ── Palette ───────────────────────────────────────────────────────────
        // ── Base backgrounds & borders ────────────────────────────────────────
        private static readonly Color ColBar       = new Color(0.055f, 0.067f, 0.094f, 0.97f); // bg-base    #0e1118
        private static readonly Color ColBarEdge   = new Color(0.141f, 0.169f, 0.227f, 1.00f); // border-mid #242b3a
        private static readonly Color ColDrawer    = new Color(0.075f, 0.090f, 0.125f, 0.97f); // bg-panel   #131720
        private static readonly Color ColSubDrawer = new Color(0.039f, 0.047f, 0.067f, 0.97f); // bg-deep    #0a0c11
        private static readonly Color ColDivider   = new Color(0.102f, 0.122f, 0.173f, 0.60f); // border-dark #1a1f2c
        private static readonly Color ColAccent    = new Color(0.282f, 0.502f, 0.667f, 1.00f); // acc        #4880aa
        private static readonly Color ColTabHl     = new Color(0.118f, 0.145f, 0.208f, 1.00f); // bg-hover   #1e2535
        private static readonly Color ColBarBg     = new Color(0.094f, 0.114f, 0.157f, 1.00f); // bg-raised  #181d28
        private static readonly Color ColBarFill   = new Color(0.376f, 0.627f, 0.800f, 1.00f); // acc-bright #60a0cc
        private static readonly Color ColBarWarn   = new Color(0.784f, 0.627f, 0.188f, 1.00f); // amber      #c8a030
        private static readonly Color ColBarCrit   = new Color(0.753f, 0.188f, 0.188f, 1.00f); // red        #c03030
        private static readonly Color ColBarGreen  = new Color(0.188f, 0.627f, 0.314f, 1.00f); // green      #30a050
        private static readonly Color ColSummaryBg = new Color(0.039f, 0.047f, 0.067f, 0.85f); // bg-deep 85%
        // ── Bevel & lit-border tokens ─────────────────────────────────────────
        private static readonly Color ColBevelHi   = new Color(0.165f, 0.200f, 0.282f, 1.00f); // #2a3348
        private static readonly Color ColBevelLo   = new Color(0.031f, 0.039f, 0.059f, 1.00f); // #080a0f
        private static readonly Color ColBorderLit = new Color(0.180f, 0.220f, 0.314f, 1.00f); // #2e3850
        // ── Text hierarchy ────────────────────────────────────────────────────
        private static readonly Color ColTextDim    = new Color(0.180f, 0.239f, 0.322f, 1.00f); // #2e3d52
        private static readonly Color ColTextMid    = new Color(0.290f, 0.376f, 0.502f, 1.00f); // #4a6080
        private static readonly Color ColTextBase   = new Color(0.416f, 0.541f, 0.667f, 1.00f); // #6a8aaa
        private static readonly Color ColTextBright = new Color(0.541f, 0.667f, 0.784f, 1.00f); // #8aaac8
        private static readonly Color ColTextHead   = new Color(0.659f, 0.753f, 0.847f, 1.00f); // #a8c0d8
        private static readonly Color ColAccBright  = new Color(0.376f, 0.627f, 0.800f, 1.00f); // #60a0cc

        // ── Resource / credits colour mapping ────────────────────────────────
        private static readonly Color ColResFood   = new Color(0.19f, 0.63f, 0.31f, 1.00f);
        private static readonly Color ColResPower  = new Color(0.78f, 0.69f, 0.18f, 1.00f);
        private static readonly Color ColResOxygen = new Color(0.19f, 0.66f, 0.78f, 1.00f);
        private static readonly Color ColResParts  = new Color(0.28f, 0.50f, 0.67f, 1.00f);
        private static readonly Color ColResIce    = new Color(0.56f, 0.78f, 0.88f, 1.00f);
        private static readonly Color ColCredits   = new Color(0.28f, 0.50f, 0.67f, 1.00f);
        private const float CreditsWarnFloor    = 500f;
        private const float OxygenCritThreshold = 10f;

        // ── Tab enum ──────────────────────────────────────────────────────────
        private enum Tab { None, Build, Crew, Station, Comms, AwayMission, Rooms, Research, Map, Views, Settings }

        // ── Station panel sub-tabs ────────────────────────────────────────────
        private enum StationSubTab { Overview, Build, Rooms, Crew, Comms }
        private StationSubTab _stationSub = StationSubTab.Overview;

        // ── Crew detail internal tab bar ──────────────────────────────────────
        private enum CrewDetailTab { Vitals, Skills, Relationships, Inventory }
        private CrewDetailTab _crewDetailTab = CrewDetailTab.Vitals;

        // ── Sub-drawer panel enum ─────────────────────────────────────────────
        // Which content to show in the secondary slide-out detail panel.
        private enum SubPanel { None, CrewDetail, HoldSettings, ResearchDetail, ModuleDetail, StationSettings,
                                  StationBuild, StationRooms, StationCrew, StationComms }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Station,  "STATION"),
            (Tab.Research, "RESEARCH"),
            (Tab.Map,      "MAP"),
            (Tab.Views,    "VIEWS"),
            (Tab.Settings, "SETTINGS"),
        };

        // ── Map sub-panel ─────────────────────────────────────────────────────
        private enum MapSubPanel { Map, Away }
        private MapSubPanel _mapSub = MapSubPanel.Map;

        // ── State ─────────────────────────────────────────────────────────────
        private GameManager _gm;
        private bool        _ready;
        private Tab         _active;
        private float       _drawerT;
        private SubPanel    _subActive  = SubPanel.None;
        private float       _subDrawerT = 0f;
        private string      _subItemUid = "";  // uid of item displayed in sub-drawer
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
        private enum CommsTab { Unread, Read, All, Ships }
        private CommsTab _commsTab       = CommsTab.Unread;
        private string   _selectedMsgUid = "";
        private Vector2  _commsListScroll;
        private Vector2  _commsBodyScroll;
        // Ships sub-tab state
        private Vector2  _commsShipListScroll;
        private string   _hailToastMsg   = "";
        private float    _hailToastTimer = 0f;

        // ── Crew / Work sub-panel state ───────────────────────────────────────
        private enum CrewSubPanel { Crew, Work, Ranks, Relationships }
        private CrewSubPanel _crewSub = CrewSubPanel.Crew;
        private Vector2      _workScroll;
        private Vector2      _deptScroll;
        // ── Rename flow: uid of department being renamed, text buffer
        private string _renamingDeptUid  = "";
        private string _renameDeptBuffer = "";
        // ── Crew detail view ──────────────────────────────────────────────────
        private string  _crewDetailNpcUid = "";   // non-empty = showing detail page for this NPC
        private Vector2 _crewListScroll;
        private bool    _crewDeptConfigMode = false; // true = dept rename/delete controls visible

        // ── Department colour picker state ────────────────────────────────────
        // Uid of dept whose inline colour picker is currently open; "" = none.
        private string  _deptPickerUid      = "";
        // "primary" or "secondary" — which swatch is being edited.
        private string  _deptPickerChannel  = "primary";
        private float   _deptPickerH        = 0f;
        private float   _deptPickerS        = 0f;
        private float   _deptPickerV        = 0.8f;
        private string  _deptPickerHexInput = "#ffffff";
        // deptUid → set of job ids that are BLOCKED for that dept (empty/absent = all jobs allowed)
        private Dictionary<string, HashSet<string>> _deptJobBlockList = new();
        // deptUid → collapsed state (empty depts auto-collapse)
        private HashSet<string> _deptCollapsed = new();
        // deptUid whose (...) settings popover is open
        private string _deptSettingsOpen = "";

        // ── Skills UI constants ───────────────────────────────────────────────
        private const int MaxNpcNameDisplayLength    = 9;
        private const int ExpertiseDescShortMaxChars = 57;   // card view (max 60 visible)
        private const int ExpertiseDescLongMaxChars  = 67;   // selection panel (max 70 visible)
        private string  _skillsSelectedNpcUid  = "";
        private bool    _expertisePanelOpen     = false;
        private string  _swapTargetExpertiseId  = "";   // expertise being replaced (empty = spend slot)
        private Vector2 _expertisePanelScroll;
        private Vector2 _skillsScroll;
        private float   _crewDetailOverlayH = 420f;   // updated each frame; drives overlay box height

        // ── Relationships sub-panel state ─────────────────────────────────────
        private Vector2 _relScroll;

        // ── Placement auto-collapse / restore state ────────────────────────
        private Tab      _prePlacementTab = Tab.None;
        private SubPanel _prePlacementSub = SubPanel.None;

        // ── Station panel sub-tab / header state ──────────────────────────────
        private bool   _stationNameEditing  = false;
        private string _stationNameBuffer   = "";
        private string _stationBuildCat     = "";   // selected build category in Build sub-tab
        // Hover-popup for Rooms sub-tab (200ms delay)
        private string _stationRoomHoverKey  = "";
        private float  _stationRoomHoverTime = 0f;

        // ── Away Mission panel state ────────────────────────────────────
        private string               _selectedMissionDef  = "";
        private readonly HashSet<string> _selectedMissionCrew = new HashSet<string>();
        private Vector2              _missionScroll;
        private string               _missionMsg = "";

        // ── Research tab state ────────────────────────────────────────────────
        private Vector2        _researchScroll;  // kept for compat
        private ResearchBranch _researchTreeBranch = ResearchBranch.Industry;
        private Vector2        _researchTreeScroll;
        private string         _selectedResearchNodeId = "";  // id of selected node in fullscreen tree

        // ── Map tab state ─────────────────────────────────────────────────────
        private Vector2              _mapScroll;
        private SystemMapController  _systemMap;
        private string               _selectedPoiUid  = "";
        private readonly HashSet<string> _selectedMapCrew = new HashSet<string>();
        private string               _mapMissionMsg   = "";

        // Job columns shown in Work Assignment grid
        private static readonly (string id, string label)[] WorkJobCols =
        {
            ("job.haul",                "Ha"),
            ("job.refine",              "Rf"),
            ("job.craft",               "Cr"),
            ("job.guard_post",          "Gd"),
            ("job.patrol",              "Pt"),
            ("job.build",               "Bl"),
            ("job.module_maintenance",  "Mn"),
            ("job.resource_management", "Mg"),
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
        private GUIStyle  _sHeader, _sLabel, _sSub, _sDescr;
        private GUIStyle  _sBtnSmall, _sBtnWide, _sBtnDanger;
        private GUIStyle  _sTextField;
        private GUIStyle  _sHint;       // 8px text-dim — panel subtitle + status bar hints
        private GUIStyle  _sIconAcc;    // 13px acc-blue — panel title icon
        private bool      _stylesReady;
        private Font      _gameFont;

        // ── Auto-install ──────────────────────────────────────────────────────
        // RuntimeInitializeOnLoadMethod fires only once at startup (the first scene).
        // We subscribe to sceneLoaded so GameHUD is (re)created every time GameScene
        // becomes active — including navigating there from the main menu.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnAnySceneLoaded;
            SceneManager.sceneLoaded += OnAnySceneLoaded;
            OnAnySceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "GameScene") return;
            if (FindAnyObjectByType<GameHUD>() != null) return;
            new GameObject("GameHUD").AddComponent<GameHUD>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            _instance = this;
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
            // CrewDetail is rendered as a bottom-left overlay, not in the sub-drawer slot
            float subTarget = (_subActive != SubPanel.None && _subActive != SubPanel.CrewDetail) ? 1f : 0f;
            _subDrawerT = Mathf.Lerp(_subDrawerT, subTarget, Time.deltaTime * AnimK);

            // Suppress map scroll/pan while the mouse is over either HUD panel
            bool overRight = Input.mousePosition.x >= Screen.width - TabW - DrawerW * _drawerT - SubDrawerW * _subDrawerT;
            bool overLeft  = Input.mousePosition.x <= DevDrawerW * _devDrawerT;
            const float CdOverlayW = SubDrawerW + 10f;   // bottom-left crew detail overlay
            bool overCrewDetail = _subActive == SubPanel.CrewDetail
                && Input.mousePosition.x <= CdOverlayW
                && Input.mousePosition.y <= _crewDetailOverlayH + 10f;
            IsMouseOverDrawer = overRight || overLeft || overCrewDetail;
            InBuildMode       = _ghostBuildableId != null || _deconstructMode;

            // Tick the hail toast timer in Update so it decays at real-time speed
            // (OnGUI can fire multiple times per frame; Update fires exactly once)
            if (_hailToastTimer > 0f) _hailToastTimer -= Time.deltaTime;

            // ── Ctrl+Z — undo last placement ────────────────────────────────────────────────
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.Z)
                && _undoStack.Count > 0 && _gm?.Station != null)
            {
                var toUndo = _undoStack.Pop();
                foreach (var uid in toUndo)
                    _gm.Building.UndoFoundation(_gm.Station, uid);
                _gm.UtilityNetworks.RebuildAll(_gm.Station);
                _gm.Rooms.RebuildBonusCache(_gm.Station);
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
                    _gm.Rooms.RebuildBonusCache(_gm.Station);
                    StationRoomView.Instance?.ForceRefreshFoundations();
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
                // Restore drawers that were open before placement started
                if (_prePlacementTab != Tab.None)
                {
                    _active          = _prePlacementTab;
                    _prePlacementTab = Tab.None;
                }
                if (_prePlacementSub != SubPanel.None)
                {
                    SubPanel restoreSub  = _prePlacementSub;
                    _prePlacementSub     = SubPanel.None;
                    OpenSub(restoreSub, "");
                    // Restore the sub-tab visual selection
                    _stationSub = restoreSub switch
                    {
                        SubPanel.StationBuild => StationSubTab.Build,
                        SubPanel.StationRooms => StationSubTab.Rooms,
                        SubPanel.StationCrew  => StationSubTab.Crew,
                        SubPanel.StationComms => StationSubTab.Comms,
                        _                     => StationSubTab.Overview,
                    };
                }
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
                _gm.Rooms.RebuildBonusCache(_gm.Station);
                StationRoomView.Instance?.ForceRefreshFoundations();
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
            if (_gameFont != null) GUI.skin.font = _gameFont;

            float sw = Screen.width;
            float sh = Screen.height;

            // ── Dev Tools button (top-left) — toggles side drawer ───────────────
            bool devDrawer = _devDrawerOpen;
            GUI.color = devDrawer ? new Color(1.00f, 0.78f, 0.20f, 1f) : ColTextBase;
            if (GUI.Button(new Rect(6f, 6f, 90f, 22f), devDrawer ? "⚡ DEV ◀" : "⚡ DEV TOOLS", _sBtnSmall))
                _devDrawerOpen = !devDrawer;
            GUI.color = Color.white;
            if (Waystation.Systems.BuildingSystem.DevMode)
            {
                GUI.color = new Color(1f, 0.92f, 0.35f, 0.85f);
                GUI.Label(new Rect(100f, 8f, 200f, 18f), "FREE BUILD  ON", _sSub);
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

            // ── Sub-drawer (slides in just left of main drawer) ─────────────────
            // (CrewDetail is excluded — it renders as a bottom-left overlay instead)
            // Drawn BEFORE the main drawer so the main drawer paints over the overlapping
            // portion while the sub-drawer is mid-animation, giving a "slide from behind" look.
            bool researchFullscreen = _active == Tab.Research && _ready && _gm?.Station != null;
            if (_subDrawerT > 0.004f && !researchFullscreen && _subActive != SubPanel.CrewDetail)
            {
                float mainDx = sw - TabW - DrawerW * _drawerT;
                float sdx    = mainDx - SubDrawerW * _subDrawerT;
                DrawSolid(new Rect(sdx, 0, SubDrawerW, sh), ColSubDrawer);
                DrawSolid(new Rect(sdx, 0, 1f, sh), ColBarEdge);

                GUI.BeginGroup(new Rect(sdx, 0, SubDrawerW, sh));
                DrawSubDrawer(SubDrawerW, sh);
                GUI.EndGroup();
            }

            // ── Drawer (slides in from right) — hidden when Research fullscreen is active
            if (_drawerT > 0.004f && !researchFullscreen)
            {
                float dx = sw - TabW - DrawerW * _drawerT;
                DrawSolid(new Rect(dx, 0, DrawerW, sh), ColDrawer);
                DrawSolid(new Rect(dx, 0, 1f, sh), ColBarEdge);

                GUI.BeginGroup(new Rect(dx, 0, DrawerW, sh));
                DrawDrawer(DrawerW, sh);
                GUI.EndGroup();
            }

            // ── Crew Member detail overlay (bottom-left) ─────────────────────
            if (_subActive == SubPanel.CrewDetail
                && !string.IsNullOrEmpty(_subItemUid)
                && _gm?.Station?.npcs.TryGetValue(_subItemUid, out var cdNpc) == true)
            {
                const float CDW = SubDrawerW;
                float CDH = Mathf.Min(_crewDetailOverlayH, sh - 20f);
                float cdx = 10f;
                float cdy = sh - CDH - 10f;
                DrawSolid(new Rect(cdx, cdy, CDW, CDH), ColSubDrawer);
                DrawSolid(new Rect(cdx, cdy, 1f, CDH), ColBarEdge);
                DrawSolid(new Rect(cdx + CDW - 1f, cdy, 1f, CDH), ColBarEdge);
                DrawSolid(new Rect(cdx, cdy, CDW, 1f), ColBarEdge);
                DrawSolid(new Rect(cdx, cdy + CDH - 1f, CDW, 1f), ColBarEdge);

                GUI.BeginGroup(new Rect(cdx, cdy, CDW, CDH));
                DrawSubDrawer(CDW, CDH);
                GUI.EndGroup();
            }

            // ── Full-screen Research tree (covers the drawer area) ────────────
            if (researchFullscreen)
            {
                float rpw = sw - TabW;
                DrawSolid(new Rect(0, 0, rpw, sh), ColSubDrawer);
                DrawResearchFullscreen(rpw, sh);
            }

            // ── Taskbar ───────────────────────────────────────────────────────
            float tx = sw - TabW;
            DrawSolid(new Rect(tx, 0, TabW, sh), ColBar);
            DrawSolid(new Rect(tx, 0, 1f, sh), ColBarEdge);

            float ty = 20f;
            foreach (var (tab, label) in Tabs)
                DrawTabButton(tab, label, tx, ref ty);

            // Pause / resume at bottom — compact, styled like tab buttons
            if (_ready && _gm != null)
            {
                string pl      = _gm.IsPaused ? "► PLAY" : "⏸ PAUSE";
                bool   paused  = _gm.IsPaused;
                Rect   pr      = new Rect(tx, sh - 23f, TabW, 23f);
                DrawSolid(new Rect(tx, sh - 23f, TabW, 1f), ColDivider); // top border
                DrawSolid(pr, paused ? ColBarGreen : ColBar);             // bg: green when paused
                if (paused) DrawSolid(new Rect(tx, sh - 23f, 3f, 23f), ColBarGreen); // left stripe
                GUI.color = paused ? Color.white : ColTextBright;
                if (GUI.Button(pr, pl, _sTabOff))
                    _gm.IsPaused = !_gm.IsPaused;
                GUI.color = Color.white;
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

            // ── Rooms tab overlay (triggered from Build → Rooms sub-panel) ────────
            if (_active == Tab.Build && _buildSub == BuildSubPanel.Rooms && _white != null)
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
            const float TabBtnH = 22f;
            bool on = _active == tab;
            Rect r  = new Rect(x, y, TabW, TabBtnH);

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

            // Background for all buttons — selected gets bg-hover, inactive gets bg-base
            DrawSolid(r, on ? ColTabHl : ColBar);
            if (on) DrawSolid(new Rect(x, y, 3f, TabBtnH), ColAccent);  // left accent stripe
            DrawSolid(new Rect(x, y + TabBtnH, TabW, 1f), ColDivider);  // bottom separator

            GUI.color = on ? ColTextHead : ColTextBase;
            if (GUI.Button(r, displayLabel, on ? _sTabOn : _sTabOff))
            {
                bool wasOff = !on;
                if (tab == Tab.Map)
                {
                    // Map button toggles the full-screen overlay; no IMGUI drawer.
                    _active = Tab.None;
                    if (SystemMapController.IsOpen)
                        _systemMap?.Close();
                    else
                        TryOpenSystemMap();
                }
                else
                {
                    var nextTab = on ? Tab.None : tab;
                    if (nextTab != _active)
                        CloseSub();
                    _active = nextTab;
                }
            }

            GUI.color = Color.white;
            y += TabBtnH + 1f;
        }

        private void TryOpenSystemMap()
        {
            if (_systemMap == null)
                _systemMap = UnityEngine.Object.FindFirstObjectByType<SystemMapController>();
            _systemMap?.Open();
        }

        // ── Sub-drawer helpers ────────────────────────────────────────────────
        private void OpenSub(SubPanel which, string uid = "")
        {
            _subActive  = which;
            _subItemUid = uid;
        }

        private void CloseSub()
        {
            _subActive  = SubPanel.None;
            _subItemUid = "";
        }

        // ── Sub-drawer root ───────────────────────────────────────────────────
        private void DrawSubDrawer(float w, float h)
        {
            string title = _subActive switch
            {
                SubPanel.CrewDetail      => (_gm?.Station != null && !string.IsNullOrEmpty(_subItemUid)
                                             && _gm.Station.npcs.TryGetValue(_subItemUid, out var _cdTitleNpc))
                                             ? _cdTitleNpc.name.ToUpper() : "CREW MEMBER",
                SubPanel.HoldSettings    => "HOLD SETTINGS",
                SubPanel.ResearchDetail  => "RESEARCH NODE",
                SubPanel.ModuleDetail    => "MODULE DETAIL",
                SubPanel.StationSettings => "STATION SETTINGS",
                SubPanel.StationBuild    => "BUILD",
                SubPanel.StationRooms    => "ROOMS",
                SubPanel.StationCrew     => "CREW",
                SubPanel.StationComms    => _gm?.Station != null && HasUnreadComms(_gm.Station) ? "COMMS \u25cf" : "COMMS",
                _                        => "",
            };

            var (icon, subtitle) = SubMeta(_subActive);
            bool hasUnread = _subActive == SubPanel.StationComms
                             && _gm?.Station != null && HasUnreadComms(_gm.Station);

            float startY   = DrawPanelChrome(w, h, icon, title, subtitle,
                                              CloseSub, title, ledGreen: true, ledAmber: hasUnread);

            float cw       = w - Pad * 2f;
            float contentH = h - startY - PanelStatH - 8f;
            Rect  area     = new Rect(Pad, startY, cw, contentH);

            switch (_subActive)
            {
                case SubPanel.CrewDetail:
                    if (!string.IsNullOrEmpty(_subItemUid) &&
                        _gm?.Station?.npcs.TryGetValue(_subItemUid, out var detailNpc) == true)
                        DrawCrewDetail(area, cw, contentH, detailNpc);
                    else
                        CloseSub();
                    break;

                case SubPanel.HoldSettings:
                    if (!string.IsNullOrEmpty(_subItemUid) && _gm?.Station != null)
                    {
                        var hold = _gm.Station.modules.TryGetValue(_subItemUid, out var h2) ? h2 : null;
                        if (hold != null)
                        {
                            float subY = 0f;
                            DrawCargoHoldSettings(hold, cw, ref subY, _gm.Station);
                        }
                        else CloseSub();
                    }
                    break;

                case SubPanel.ResearchDetail:
                    DrawResearchNodeDetail(area, cw, contentH);
                    break;

                case SubPanel.ModuleDetail:
                    DrawModuleDetail(area, cw, contentH);
                    break;

                case SubPanel.StationSettings:
                    GUI.Label(area, "Station settings placeholder.", _sSub);
                    break;

                case SubPanel.StationBuild:
                    DrawStationBuild(area, cw, contentH);
                    break;

                case SubPanel.StationRooms:
                    DrawStationRooms(area, cw, contentH);
                    break;

                case SubPanel.StationCrew:
                    DrawCrew(area, cw, contentH);
                    break;

                case SubPanel.StationComms:
                    DrawComms(area, cw, contentH);
                    break;
            }
        }

        // ── Drawer root ───────────────────────────────────────────────────────
        private void DrawDrawer(float w, float h)
        {
            string title = _active switch
            {
                Tab.Build       => "DESIGNER",
                Tab.Crew        => "CREW",
                Tab.Station     => (_gm?.Station?.stationName is { Length: > 0 } sn ? sn : "STATION").ToUpper(),
                Tab.Comms       => "COMMS",
                Tab.AwayMission => "AWAY MISSION",
                Tab.Rooms       => "ROOMS",
                Tab.Research    => "RESEARCH",
                Tab.Map         => "MAP",
                Tab.Views       => "VIEWS",
                Tab.Settings    => "SETTINGS",
                _               => "",
            };

            var (icon, subtitle) = TabMeta(_active);
            string hint = _ready && _gm?.Station != null
                ? $"{title} \u00b7 {_gm.Station.npcs.Count} CREW"  // · separator
                : title;

            float startY  = DrawPanelChrome(w, h, icon, title, subtitle,
                                             () => _active = Tab.None, hint);

            float cw      = w - Pad * 2f;
            float contentH = h - startY - PanelStatH - 8f;
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
                case Tab.Research:    DrawResearch(area, cw, contentH);    break;
                case Tab.Map:         DrawMap(area, cw, contentH);         break;
                case Tab.Views:       DrawViews(area, cw, contentH);       break;
                case Tab.Settings:    DrawSettings(area, cw, contentH);    break;
            }
        }

        // ── Designer tab ──────────────────────────────────────────────────────
        private Vector2 _buildScroll;
        private string  _buildInfoOpen     = "";  // buildable id whose info panel is expanded (unused, kept for compat)
        private string  _buildHoverItem    = "";  // defn.id hovered this frame
        private Rect    _buildHoverContentRect;   // content-space rect of the hovered tile
        private string  _foundSettingsOpen = "";  // foundation uid whose cargo settings are open
        private string  _buildCategoryFilter = ""; // "" = all categories
        private bool    _deconstructMode   = false; // deconstruct-mode: click tile to cancel/demolish
        private bool    _showBuildQueue    = false; // toggle inline build-queue panel

        // ── Designer sub-panel navigation ─────────────────────────────────────
        private enum BuildSubPanel { Place, Queue, Rooms, TemplateLibrary }
        private BuildSubPanel _buildSub = BuildSubPanel.Place;

        // ── Template Library state ────────────────────────────────────────────
        private Vector2 _templateLibScroll;
        private string  _templateLibSearch    = "";
        private string  _templateLibSelected  = "";  // templateId of selected entry
        private bool    _templateLibConfirmDelete = false;

        // ── Room management state (Build > Rooms sub-panel) ───────────────────
        private Vector2 _buildRoomsScroll;
        private Vector2 _buildRoomTypesScroll;

        private bool    _creatingNewRoomType = false;
        // Buffers for custom room type editor
        private string  _newRoomTypeName     = "";
        private string  _newRoomTypeWbType   = "";
        private string  _newRoomTypeSkillKey = "";
        private string  _newRoomTypeSkillVal = "1.10";
        // Per-room name edit buffers: key = roomKey, value = text buffer
        private readonly Dictionary<string, string> _roomNameBuffers = new Dictionary<string, string>();
        // Track which rooms have their role picker open (legacy visual role)
        private string  _buildRoomRolePicker = "";   // roomKey with open picker, "" = none
        // Track which rooms have their room-type assignment picker open
        private string  _buildRoomTypePicker = "";   // roomKey with open type picker, "" = none

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

        // Singleton reference for cross-view calls (e.g. StationRoomView dot clicks).
        private static GameHUD _instance;

        /// <summary>Called by StationRoomView when a crew dot is single-clicked.</summary>
        public static void SelectCrewMember(string npcUid)
        {
            if (_instance == null || string.IsNullOrEmpty(npcUid)) return;
            if (_instance._active != Tab.Station)
                _instance._active = Tab.Station;
            _instance.OpenSub(SubPanel.CrewDetail, npcUid);
        }

        private void DrawBuild(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;

            // Sub-panel nav (Place | Queue | Rooms | Template Library)
            const float NavH   = 36f;
            const float NavPad = 4f;
            float navBw = (w - NavPad * 5f) / 4f;
            float navY  = area.y + NavPad;

            DrawSolid(new Rect(area.x, area.y, w, NavH + NavPad), ColSubDrawer);

            Color navPrev = GUI.color;
            var subPanels = new[]
            {
                (BuildSubPanel.Place,           "\u2692 Place"),
                (BuildSubPanel.Queue,           "\u2261 Queue"),
                (BuildSubPanel.Rooms,           "\u2b21 Rooms"),
                (BuildSubPanel.TemplateLibrary, "\u2605 Templates"),
            };
            for (int i = 0; i < subPanels.Length; i++)
            {
                var (panel, label) = subPanels[i];
                bool isActive = _buildSub == panel;
                GUI.color = isActive ? ColAccent : ColTextBase;
                if (GUI.Button(new Rect(area.x + NavPad + i * (navBw + NavPad), navY, navBw, NavH - NavPad * 2f),
                               label, _sBtnSmall))
                {
                    _buildSub = panel;
                }
            }
            GUI.color = navPrev;

            Rect subArea = new Rect(area.x, area.y + NavH + NavPad, w, h - NavH - NavPad);

            if (_buildSub == BuildSubPanel.Place)                DrawBuildPlace(subArea, w, h - NavH - NavPad);
            else if (_buildSub == BuildSubPanel.Queue)           DrawBuildQueue(subArea, w, h - NavH - NavPad);
            else if (_buildSub == BuildSubPanel.Rooms)           DrawBuildRooms(subArea, w, h - NavH - NavPad);
            else                                                 DrawTemplateLibrary(subArea, w, h - NavH - NavPad);
        }

        private void DrawBuildPlace(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s       = _gm.Station;
            var catalog = _gm.Registry.Buildables;
            var active  = s.foundations;

            float cw = w - 16f;
            _buildHoverItem = "";  // reset hover; tiles will re-set it if mouse is over them

            // ── Toolbar strip ────────────────────────────────────────────────
            const float TBH = 30f;
            DrawSolid(new Rect(area.x, area.y, w, TBH), ColSubDrawer);
            Color tbPrev = GUI.color;
            GUI.color = _deconstructMode ? ColBarCrit : ColTextBase;
            if (GUI.Button(new Rect(area.x + 4f, area.y + 4f, 110f, 22f), "\u26CF Deconstruct", _sBtnSmall))
                _deconstructMode = !_deconstructMode;
            GUI.color = _showBuildQueue ? ColBarFill : ColTextBase;
            if (GUI.Button(new Rect(area.x + 120f, area.y + 4f, 110f, 22f),
                           $"\u2261 Queue ({active.Count})", _sBtnSmall))
                _showBuildQueue = !_showBuildQueue;
            GUI.color = tbPrev;

            float tbH = TBH;

            if (_deconstructMode)
            {
                DrawSolid(new Rect(area.x, area.y + TBH, w, 18f), new Color(ColBarCrit.r, ColBarCrit.g, ColBarCrit.b, 0.25f));
                GUI.color = new Color(1f, 0.55f, 0.55f);
                GUI.Label(new Rect(area.x + 6f, area.y + TBH + 2f, w - 12f, LineH),
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
            DrawSolid(new Rect(area.x, area.y + tbH, w, (CatBtnH + 2f) * 2f + 4f), ColSubDrawer);
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
                    GUI.color = isActive ? ColAccent : ColTextBase;
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
            const int   CatCols = 4;
            const float TileGap = 3f;
            const float TileH   = 28f;
            float tileW = (cw - TileGap * (CatCols - 1)) / CatCols;
            innerH += 24f;
            foreach (var catGroup in byCategory)
            {
                innerH += 26f;
                int tileRows = (catGroup.Value.Count + CatCols - 1) / CatCols;
                innerH += tileRows * (TileH + 2f);
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
                    GUI.Label(new Rect(0, y, cw, LineH), "No foundations placed yet.", _sSub);
                    y += LineH + 2f;
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
                GUI.Label(new Rect(0, y, cw, LineH), "No buildables loaded.", _sSub);
                y += LineH + 2f;
            }

            foreach (var catGroup in byCategory)
            {
                DrawSolid(new Rect(0, y, cw, 22f), ColBarBg);
                GUI.Label(new Rect(4f, y + 3f, cw - 8f, LineH), CategoryDisplayName(catGroup.Key), _sSub);
                y += 24f;

                var catItems = catGroup.Value;
                for (int i = 0; i < catItems.Count; i += CatCols)
                {
                    for (int c = 0; c < CatCols && i + c < catItems.Count; c++)
                        DrawCatalogTile(catItems[i + c],
                            new Rect(c * (tileW + TileGap), y, tileW, TileH),
                            s, scrollArea, _buildScroll);
                    y += TileH + 2f;
                }
            }

            GUI.EndScrollView();

            // ── Hover tooltip overlay (drawn on top, outside scroll view) ─────
            if (!string.IsNullOrEmpty(_buildHoverItem) &&
                _gm.Registry.Buildables.TryGetValue(_buildHoverItem, out var hDef))
                DrawCatalogTooltip(hDef, scrollArea, _buildScroll, cw, s);
        }

        private void DrawBuildQueue(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s  = _gm.Station;
            float cw = w - 16f;

            float innerH = 28f + (s.foundations.Count == 0 ? 24f : s.foundations.Count * 78f);
            _buildScroll = GUI.BeginScrollView(
                new Rect(area.x, area.y, w, h),
                _buildScroll,
                new Rect(0, 0, cw, Mathf.Max(h, innerH)));

            float y = 4f;
            Section($"Build Queue  ({s.foundations.Count})", cw, ref y);
            if (s.foundations.Count == 0)
            {
                GUI.Label(new Rect(0, y, cw, LineH), "No foundations placed yet.", _sSub);
                y += LineH + 4f;
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

            // Collect all available room types (built-in + custom)
            var allRoomTypes = new List<RoomTypeDefinition>();
            if (_gm.Registry?.RoomTypes != null)
                foreach (var rtKv in _gm.Registry.RoomTypes)
                    allRoomTypes.Add(rtKv.Value);
            foreach (var ct in s.customRoomTypes)
                allRoomTypes.Add(ct);

            // ── Estimate content height ───────────────────────────────────────
            float innerH = 8f;
            innerH += 24f; // section header "Rooms"
            foreach (var kv in discovered)
            {
                string roomKey = kv.Key;
                innerH += 36f;  // room header
                innerH += 24f;  // name field row
                innerH += 24f;  // role row
                innerH += 26f;  // room type assignment row

                bool typePOpen = _buildRoomTypePicker == roomKey;
                if (typePOpen)
                    innerH += (allRoomTypes.Count + 1) * 22f + 8f; // type picker dropdown

                // Bonus card
                if (s.roomBonusCache.TryGetValue(roomKey, out var bsCached) && bsCached.workbenchRoomType != null)
                    innerH += 28f + bsCached.requirements.Count * 20f + 16f;
                else
                    innerH += 20f; // "no assignment" hint

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
                GUI.color = ColTextBase;
                GUI.Label(new Rect(4f, y, cw - 8f, 18f), "No rooms found. Place floors to define rooms.", _sSub);
                GUI.color = colPrev;
                y += 22f;
            }

            foreach (var kv in discovered)
            {
                string roomKey = kv.Key;

                // Look up visual role + player type assignment
                s.roomRoles.TryGetValue(roomKey, out string roleId);
                string roleName  = "";
                Color  roleColor = new Color(0.40f, 0.40f, 0.45f, 0.40f);
                foreach (var rr in RoomRoles) if (rr.id == roleId) { roleName = rr.label; roleColor = rr.col; break; }

                s.playerRoomTypeAssignments.TryGetValue(roomKey, out string assignedTypeId);
                s.roomBonusCache.TryGetValue(roomKey, out var bonusState);
                string autoSuggest = bonusState?.autoSuggestedRoomType;

                // Custom name
                if (!_roomNameBuffers.ContainsKey(roomKey))
                    _roomNameBuffers[roomKey] = s.customRoomNames.TryGetValue(roomKey, out var cn) ? cn : "";

                // ── Room header ───────────────────────────────────────────────
                DrawSolid(new Rect(0, y, cw, 32f), ColBarBg);
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
                {
                    _buildRoomRolePicker = _buildRoomRolePicker == roomKey ? "" : roomKey;
                    _buildRoomTypePicker = "";
                }
                y += 36f;

                // ── Role picker ───────────────────────────────────────────────
                if (_buildRoomRolePicker == roomKey)
                {
                    DrawSolid(new Rect(4f, y, cw - 4f, (RoomRoles.Length + 1) * 22f + 4f), ColDrawer);
                    float ry = y + 2f;
                    foreach (var rr in RoomRoles)
                    {
                        bool cur = roleId == rr.id || (string.IsNullOrEmpty(rr.id) && string.IsNullOrEmpty(roleId));
                        GUI.color = cur ? ColAccent : ColTextBright;
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

                // ── Room type assignment row ──────────────────────────────────
                {
                    string assignedLabel = "Unassigned";
                    if (!string.IsNullOrEmpty(assignedTypeId))
                    {
                        assignedLabel = assignedTypeId;
                        RoomTypeDefinition rtLookup = null;
                        _gm.Registry?.RoomTypes?.TryGetValue(assignedTypeId, out rtLookup);
                        if (rtLookup == null)
                            rtLookup = s.customRoomTypes.FirstOrDefault(t => t.id == assignedTypeId);
                        if (rtLookup != null) assignedLabel = rtLookup.displayName;
                    }

                    GUI.color = ColTextMid;
                    GUI.Label(new Rect(4f, y + 4f, 58f, 16f), "Room Type:", _sSub);
                    GUI.color = string.IsNullOrEmpty(assignedTypeId) ? ColTextMid : ColTextBright;
                    GUI.Label(new Rect(64f, y + 4f, cw - 160f, 16f), assignedLabel, _sSub);
                    GUI.color = colPrev;

                    float btnX = cw - 88f;
                    if (GUI.Button(new Rect(btnX, y, 84f, 22f),
                        _buildRoomTypePicker == roomKey ? "\u25b2 Close" : "\u25bc Assign", _sBtnSmall))
                    {
                        _buildRoomTypePicker = _buildRoomTypePicker == roomKey ? "" : roomKey;
                        _buildRoomRolePicker = "";
                    }
                    y += 26f;

                    // ── Type picker dropdown ──────────────────────────────────
                    if (_buildRoomTypePicker == roomKey)
                    {
                        float pickerH = (allRoomTypes.Count + 1) * 22f + 8f;
                        DrawSolid(new Rect(4f, y, cw - 4f, pickerH), ColDrawer);
                        float ty = y + 4f;

                        // Auto-suggest hint
                        if (!string.IsNullOrEmpty(autoSuggest))
                        {
                            string suggestName = autoSuggest;
                            RoomTypeDefinition suggestDef = null;
                            _gm.Registry?.RoomTypes?.TryGetValue(autoSuggest, out suggestDef);
                            if (suggestDef == null)
                                suggestDef = s.customRoomTypes.FirstOrDefault(t => t.id == autoSuggest);
                            if (suggestDef != null) suggestName = suggestDef.displayName;

                            GUI.color = new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.75f);
                            GUI.Label(new Rect(10f, ty, cw - 14f, 16f),
                                $"\u2605 Suggested: {suggestName}", _sSub);
                            GUI.color = colPrev;
                            ty += 18f;
                        }

                        // "No assignment" option
                        {
                            bool cur = string.IsNullOrEmpty(assignedTypeId);
                            GUI.color = cur ? ColAccent : ColTextBase;
                            if (GUI.Button(new Rect(8f, ty, cw - 12f, 20f), "— Unassigned (no bonus)", _sBtnSmall))
                            {
                                _gm.Rooms.AssignRoomType(s, roomKey, null);
                                _buildRoomTypePicker = "";
                            }
                            GUI.color = colPrev;
                            ty += 22f;
                        }

                        // Each available type
                        foreach (var rt in allRoomTypes)
                        {
                            bool cur = assignedTypeId == rt.id;
                            GUI.color = cur ? ColAccent : ColTextBright;

                            // Build bonus description — accumulate all skill bonuses
                            var bonusParts = new System.Text.StringBuilder();
                            foreach (var skv in rt.skillBonuses)
                            {
                                if (bonusParts.Length > 0) bonusParts.Append(", ");
                                bonusParts.Append($"+{(skv.Value - 1f) * 100f:F0}% {skv.Key}");
                            }
                            string bonusDesc = bonusParts.ToString();
                            string btnLabel = string.IsNullOrEmpty(bonusDesc)
                                ? rt.displayName
                                : $"{rt.displayName}  [{bonusDesc}]";

                            if (GUI.Button(new Rect(8f, ty, cw - 12f, 20f), btnLabel, _sBtnSmall))
                            {
                                _gm.Rooms.AssignRoomType(s, roomKey, rt.id);
                                _buildRoomTypePicker = "";
                            }
                            GUI.color = colPrev;
                            ty += 22f;
                        }

                        y += pickerH;
                    }
                }

                // ── Bonus card (only shown when a type is assigned) ───────────
                if (bonusState != null && !string.IsNullOrEmpty(bonusState.workbenchRoomType))
                {
                    float cardH = 28f + bonusState.requirements.Count * 20f + 16f;
                    DrawSolid(new Rect(4f, y, cw - 4f, cardH),
                        new Color(ColBarBg.r, ColBarBg.g, ColBarBg.b, 0.75f));

                    // Header row: type name + status
                    string typeName = bonusState.displayName ?? bonusState.workbenchRoomType;
                    if (bonusState.bonusActive)
                    {
                        GUI.color = ColBarWarn;
                        GUI.Label(new Rect(10f, y + 4f, cw - 90f, 16f), $"\u2605 {typeName}", _sLabel);
                        GUI.color = new Color(ColBarWarn.r, ColBarWarn.g, ColBarWarn.b, 0.9f);
                        GUI.Label(new Rect(cw - 86f, y + 5f, 80f, LineH), "BONUS ACTIVE", _sSub);
                    }
                    else
                    {
                        GUI.color = ColTextBase;
                        GUI.Label(new Rect(10f, y + 4f, cw - 90f, 16f), $"\u25c7 {typeName}", _sLabel);
                        GUI.color = ColTextMid;
                        GUI.Label(new Rect(cw - 68f, y + 5f, 62f, LineH), "inactive", _sSub);
                    }
                    GUI.color = colPrev;

                    // Workbench count
                    int cap = 3;
                    if (_gm.Registry?.RoomTypes?.TryGetValue(bonusState.workbenchRoomType ?? "", out var rtd) == true)
                        cap = rtd.workbenchCap;
                    string wbLabel = bonusState.workbenchCount > cap
                        ? $"Workbenches: {bonusState.workbenchCount} (cap {cap} earn bonus)"
                        : $"Workbenches: {bonusState.workbenchCount}/{cap}";
                    GUI.Label(new Rect(10f, y + 22f, cw - 14f, LineH), wbLabel, _sSub);

                    float ry2 = y + 28f;

                    // Requirements checklist
                    foreach (var req in bonusState.requirements)
                    {
                        bool met = req.IsMet;
                        GUI.color = met ? ColBarGreen : ColBarCrit;
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
                else
                {
                    GUI.color = ColTextMid;
                    string hint = string.IsNullOrEmpty(autoSuggest)
                        ? "No workbench in this room \u2014 place a workbench, then assign a room type."
                        : "Assign a room type above to enable workbench bonuses.";
                    GUI.Label(new Rect(8f, y, cw - 12f, 16f), hint, _sSub);
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
                    DrawSolid(new Rect(0, y, cw, 26f), ColBarBg);
                    GUI.color = ColTextBase;
                    GUI.Label(new Rect(4f, y + 4f, cw * 0.55f, 16f), rt.displayName, _sSub);
                    GUI.color = ColTextMid;
                    GUI.Label(new Rect(cw * 0.56f, y + 4f, cw * 0.24f, 16f), "(built-in)", _sSub);
                    GUI.color = colPrev;
                    // Show skill bonuses (accumulate all entries)
                    var bonusParts2 = new System.Text.StringBuilder();
                    foreach (var skv in rt.skillBonuses)
                    {
                        if (bonusParts2.Length > 0) bonusParts2.Append(", ");
                        bonusParts2.Append($"+{(skv.Value - 1f) * 100f:F0}% {skv.Key}");
                    }
                    GUI.Label(new Rect(cw * 0.80f, y + 4f, cw * 0.20f, 16f), bonusParts2.ToString(), _sSub);
                    y += 28f;
                }
            }

            // Custom types
            RoomTypeDefinition toDelete = null;
            foreach (var ct in s.customRoomTypes)
            {
                DrawSolid(new Rect(0, y, cw, 26f), ColBarBg);
                DrawSolid(new Rect(0, y, 3f, 26f), new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.8f));
                GUI.Label(new Rect(6f, y + 4f, cw * 0.55f, 16f), ct.displayName, _sSub);
                GUI.color = new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.8f);
                GUI.Label(new Rect(cw * 0.56f, y + 4f, cw * 0.18f, 16f), "(custom)", _sSub);
                GUI.color = colPrev;
                if (GUI.Button(new Rect(cw - 58f, y + 3f, 54f, 20f), "\u2715 Delete", _sBtnDanger))
                    toDelete = ct;
                y += 28f;
            }
            if (toDelete != null) s.customRoomTypes.Remove(toDelete);

            // ── New custom type creator ────────────────────────────────────────
            y += 4f;
            GUI.color = ColAccent;
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
                DrawSolid(new Rect(0, y, cw, 100f), ColBarBg);
                DrawSolid(new Rect(0, y, cw, 1f), new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.6f));

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

        // ── Template Library sub-panel ────────────────────────────────────────

        private void DrawTemplateLibrary(Rect area, float w, float h)
        {
            var lib = Waystation.Systems.TemplateLibrary.Instance;

            float y  = area.y;
            float cw = w;

            // ── Search bar + action buttons ───────────────────────────────────
            const float ToolH = 38f;
            DrawSolid(new Rect(area.x, y, cw, ToolH), ColSubDrawer);

            float searchW = cw - 180f - Pad * 3f;
            GUI.Label(new Rect(area.x + Pad, y + 10f, 40f, LineH), "Search", _sSub);
            _templateLibSearch = GUI.TextField(
                new Rect(area.x + Pad + 44f, y + 9f, searchW, BtnH),
                _templateLibSearch, 64, _sTextField);

            // "New Asset" button → opens blank Asset Editor
            Color btnPrev = GUI.color;
            GUI.color = ColAccent;
            if (GUI.Button(new Rect(area.x + Pad + 44f + searchW + Pad, y + 8f, 82f, BtnH),
                           "\u2605 New Asset", _sBtnSmall))
                Waystation.UI.AssetEditorController.Open();

            // "Import" button
            GUI.color = ColBarGreen;
            if (GUI.Button(new Rect(area.x + cw - Pad - 78f, y + 8f, 74f, BtnH),
                           "Import…", _sBtnSmall))
            {
                // NOTE: file-dialog integration is platform-specific;
                // in-game import is triggered by pasting JSON into a
                // clipboard listener (future enhancement).
                // For now we stub the action with a log.
                Debug.Log("[TemplateLibrary] Import: paste JSON here (TODO: file picker)");
            }
            GUI.color = btnPrev;
            y += ToolH + 2f;

            // ── Template list ─────────────────────────────────────────────────
            var entries = _templateLibSearch.Length > 0
                ? new System.Collections.Generic.List<Waystation.Models.ClothingTemplate>(
                      lib.Search(_templateLibSearch))
                : new System.Collections.Generic.List<Waystation.Models.ClothingTemplate>(lib.All);

            const float EntryH = 80f;
            float innerH = entries.Count * (EntryH + 4f) + 8f;
            innerH = Mathf.Max(innerH, h - (y - area.y) - 4f);

            _templateLibScroll = GUI.BeginScrollView(
                new Rect(area.x, y, cw, h - (y - area.y) - 4f),
                _templateLibScroll,
                new Rect(0, 0, cw - 22f, innerH));

            float lw = cw - 22f;
            float ey = 0f;
            foreach (var tmpl in entries)
            {
                bool selected = (_templateLibSelected == tmpl.templateId);
                DrawSolid(new Rect(0, ey, lw, EntryH - 2f),
                          selected ? ColTabHl
                                   : ColBarBg);

                // Thumbnail placeholder (32×32 grey square)
                const float ThumbW = 40f;
                DrawSolid(new Rect(4f, ey + 4f, ThumbW, EntryH - 10f), ColBevelHi);
                GUI.Label(new Rect(4f, ey + 14f, ThumbW, 20f), "👗",
                          new GUIStyle(_sSub) { alignment = TextAnchor.MiddleCenter, fontSize = FontSizeHdr });

                // Template info
                float tx = ThumbW + 10f;
                float tw2 = lw - tx - 160f;
                GUI.Label(new Rect(tx, ey + 4f, tw2, LineH), tmpl.templateName, _sLabel);
                GUI.Label(new Rect(tx, ey + 24f, tw2, LineH),
                          $"by {(string.IsNullOrEmpty(tmpl.designerName) ? "Unknown" : tmpl.designerName)}",
                          _sSub);
                GUI.Label(new Rect(tx, ey + 44f, tw2, LineH),
                          string.Join(", ", tmpl.tags), _sSub);

                // Beauty badge
                GUI.color = new Color(1f, 0.85f, 0.25f);
                GUI.Label(new Rect(lw - 160f, ey + 4f, 60f, LineH),
                          $"★ {tmpl.beautyScore:F0}", _sSub);
                GUI.color = btnPrev;

                // Action buttons: Open / Duplicate / Delete / Export
                float ax = lw - 154f;
                float ay = ey + 24f;
                float abw = 72f;

                if (GUI.Button(new Rect(ax, ay, abw, 20f), "Open Editor", _sBtnSmall))
                {
                    _templateLibSelected = tmpl.templateId;
                    Waystation.UI.AssetEditorController.OpenWithTemplate(tmpl);
                }
                if (GUI.Button(new Rect(ax + abw + 4f, ay, 72f, 20f), "Duplicate", _sBtnSmall))
                {
                    lib.Duplicate(tmpl.templateId);
                }
                ay += 24f;

                GUI.color = new Color(1.00f, 0.55f, 0.55f);
                if (GUI.Button(new Rect(ax, ay, abw, 20f), "✕ Delete", _sBtnSmall))
                {
                    if (_templateLibConfirmDelete && _templateLibSelected == tmpl.templateId)
                    {
                        lib.Delete(tmpl.templateId);
                        if (_templateLibSelected == tmpl.templateId) _templateLibSelected = "";
                        _templateLibConfirmDelete = false;
                    }
                    else
                    {
                        _templateLibSelected      = tmpl.templateId;
                        _templateLibConfirmDelete = true;
                    }
                }
                GUI.color = ColBarGreen;
                if (GUI.Button(new Rect(ax + abw + 4f, ay, 72f, 20f), "Export", _sBtnSmall))
                {
                    string json = lib.Export(
                        new System.Collections.Generic.List<string> { tmpl.templateId });
                    GUIUtility.systemCopyBuffer = json;
                    Debug.Log("[TemplateLibrary] Exported to clipboard: " + tmpl.templateName);
                }
                GUI.color = btnPrev;

                // Confirm-delete banner
                if (_templateLibConfirmDelete && _templateLibSelected == tmpl.templateId)
                {
                    GUI.color = new Color(ColBarCrit.r, ColBarCrit.g, ColBarCrit.b, 0.90f);
                    GUI.Label(new Rect(0f, ey, lw, EntryH - 2f), "  Click ✕ Delete again to confirm.", _sSub);
                    GUI.color = btnPrev;
                }

                // Click anywhere on entry to select
                if (GUI.Button(new Rect(0, ey, ThumbW + 10f + lw * 0.40f, EntryH - 2f),
                               "", GUIStyle.none))
                {
                    _templateLibSelected      = tmpl.templateId;
                    _templateLibConfirmDelete = false;
                }

                ey += EntryH + 4f;
            }

            if (entries.Count == 0)
                GUI.Label(new Rect(Pad, 8f, lw - Pad * 2f, 18f),
                          "No templates found. Use 'New Asset' to create one.", _sSub);

            GUI.EndScrollView();
        }

        private void DrawFoundationRow(FoundationInstance f, float cw, ref float y, StationState s)
        {
            bool   hasDefn     = _gm.Registry.Buildables.TryGetValue(f.buildableId, out var fDefn);
            string fname       = hasDefn ? fDefn.displayName : f.buildableId;
            bool   isCabinet   = f.buildableId.Contains("storage_cabinet");
            bool   isPlanter   = f.buildableId == "buildable.hydroponics_planter";
            bool   settingsOpen = (isCabinet || isPlanter) && f.status == "complete" && _foundSettingsOpen == f.uid;

            DrawSolid(new Rect(0, y, cw, 76f), new Color(ColBarBg.r, ColBarBg.g, ColBarBg.b, 0.6f));
            GUI.Label(new Rect(4f, y + 2f, cw * 0.65f, LineH), fname, _sLabel);

            string statusLabel = f.status switch
            {
                "awaiting_haul" => "Awaiting materials",
                "constructing"  => $"Building  {f.buildProgress * 100f:F0}%",
                "complete"      => "Complete",
                _               => f.status
            };
            GUI.Label(new Rect(4f, y + 20f, cw - 8f, LineH), statusLabel, _sSub);

            if (f.status == "constructing" || f.status == "awaiting_haul")
            {
                float pct = f.status == "constructing" ? f.buildProgress : 0f;
                DrawSolid(new Rect(4f, y + 46f, cw - 8f, 6f), ColBarBg);
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
                GUI.Label(new Rect(4f, y + 44f, cw - 8f, LineH), sb.ToString(), _sSub);
            }

            if (f.status != "complete")
            {
                if (GUI.Button(new Rect(cw * 0.68f, y + 2f, cw * 0.32f, 22f), "Cancel", _sBtnDanger))
                {
                    _gm.Building.CancelFoundation(s, f.uid, refund: true);
                    _gm.UtilityNetworks.RebuildAll(s);
                }
            }
            else if (isCabinet)
            {
                // Toggle settings panel
                string btnLabel = settingsOpen ? "\u25b2 Config" : "\u25bc Config";
                if (GUI.Button(new Rect(cw * 0.68f, y + 2f, cw * 0.32f, 22f), btnLabel, _sBtnSmall))
                    _foundSettingsOpen = settingsOpen ? "" : f.uid;

                // Rotation reminder label
                GUI.Label(new Rect(4f, y + 38f, cw - 8f, LineH),
                    $"{f.cargoCapacity} items  ·  {f.tileRotation}° rotation", _sSub);
            }
            else if (isPlanter)
            {
                // Toggle planter inspect panel
                string btnLabel = settingsOpen ? "\u25b2 Inspect" : "\u25bc Inspect";
                if (GUI.Button(new Rect(cw * 0.68f, y + 2f, cw * 0.32f, 22f), btnLabel, _sBtnSmall))
                    _foundSettingsOpen = settingsOpen ? "" : f.uid;

                // Show crop stage summary inline
                string stageLabel = f.growthStage switch
                {
                    0 => f.cropId != null ? $"Empty — {f.cropId}" : "Empty",
                    1 => $"Seedling ({f.growthProgress * 100f:F0}%)",
                    2 => $"Established ({f.growthProgress * 100f:F0}%)",
                    3 => "Mature — ready to harvest",
                    _ => "Unknown"
                };
                GUI.Label(new Rect(4f, y + 38f, cw - 8f, LineH), stageLabel, _sSub);
            }

            y += 78f;

            // ── Cargo settings panel (cabinet only, complete, expanded) ───────
            if (settingsOpen && isCabinet)
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

            // ── Planter inspect panel (hydroponics planter, complete, expanded) ─
            if (isPlanter && settingsOpen)
            {
                DrawSolid(new Rect(0, y, cw, 1f), ColDivider);
                y += 6f;
                GUI.Label(new Rect(4f, y, cw - 8f, 16f), "Planter Status", _sLabel);
                y += 20f;

                // Crop assignment
                string cropDisplay = f.cropId != null ? f.cropId : "(none)";
                GUI.Label(new Rect(4f, y, cw * 0.45f, 16f), "Crop:", _sSub);
                GUI.Label(new Rect(cw * 0.48f, y, cw * 0.52f, 16f), cropDisplay, _sSub);
                y += 18f;

                // Crop picker — only when empty
                if (f.growthStage == 0 && _gm.Registry.Crops.Count > 0)
                {
                    GUI.Label(new Rect(4f, y, cw - 8f, LineH), "Assign crop:", _sSub);
                    y += 16f;
                    foreach (var kv in _gm.Registry.Crops)
                    {
                        bool selected = f.cropId == kv.Key;
                        Color prev = GUI.color;
                        if (selected) GUI.color = ColBarGreen;
                        if (GUI.Button(new Rect(8f, y, cw - 16f, 18f), kv.Value.cropName, _sBtnSmall))
                            f.cropId = selected ? null : kv.Key;
                        GUI.color = prev;
                        y += 20f;
                    }
                }

                // Conditions
                y += 4f;
                GUI.Label(new Rect(4f, y, cw - 8f, 16f), "Conditions", _sLabel);
                y += 18f;

                // Water
                string waterLabel = f.isWatered ? "\u2713 Watered" : "\u2715 No Water";
                GUI.color = f.isWatered ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.4f, 0.3f);
                GUI.Label(new Rect(8f, y, cw - 16f, 16f), waterLabel, _sSub);
                GUI.color = Color.white;
                y += 18f;

                // Light
                string lightLabel = $"Light: {f.lightLevel:F1}";
                _gm.Registry.Crops.TryGetValue(f.cropId ?? "", out var inspCrop);
                if (inspCrop != null)
                {
                    var lt = FarmingSystem.EvalLight(f.lightLevel, inspCrop);
                    GUI.color = lt == FarmingSystem.ConditionTier.Ideal      ? ColBarGreen
                              : lt == FarmingSystem.ConditionTier.Acceptable ? new Color(1f, 0.85f, 0.2f)
                              : new Color(1f, 0.4f, 0.3f);
                    lightLabel += lt == FarmingSystem.ConditionTier.Ideal      ? " (Ideal)"
                                : lt == FarmingSystem.ConditionTier.Acceptable ? " (OK)"
                                : " (Critical)";
                }
                GUI.Label(new Rect(8f, y, cw - 16f, 16f), lightLabel, _sSub);
                GUI.color = Color.white;
                y += 18f;

                // Temperature
                string tempLabel = $"Temp: {f.tileTemperature:F1} \u00b0C";
                if (inspCrop != null)
                {
                    var tt = FarmingSystem.EvalTemperature(f.tileTemperature, inspCrop);
                    GUI.color = tt == FarmingSystem.ConditionTier.Ideal      ? ColBarGreen
                              : tt == FarmingSystem.ConditionTier.Acceptable ? new Color(1f, 0.85f, 0.2f)
                              : new Color(1f, 0.4f, 0.3f);
                    tempLabel += tt == FarmingSystem.ConditionTier.Ideal      ? " (Ideal)"
                               : tt == FarmingSystem.ConditionTier.Acceptable ? " (OK)"
                               : " (Critical)";
                }
                GUI.Label(new Rect(8f, y, cw - 16f, 16f), tempLabel, _sSub);
                GUI.color = Color.white;
                y += 18f;

                // Damage
                if (f.cropDamage > 0f)
                {
                    GUI.color = new Color(1f, 0.4f, 0.3f);
                    GUI.Label(new Rect(8f, y, cw - 16f, 16f),
                        $"Damage: {f.cropDamage * 100f:F0}%", _sSub);
                    GUI.color = Color.white;
                    y += 18f;
                }

                DrawSolid(new Rect(0, y, cw, 1f), ColDivider);
                y += 6f;
            }
        }

        // ── Catalog tile (compact name button in the 4-column grid) ──────────
        private void DrawCatalogTile(BuildableDefinition defn, Rect tile, StationState s,
                                     Rect scrollArea, Vector2 scroll)
        {
            bool  canPlace = _gm.Building.CanPlace(s, defn.id, out _);
            bool  hovered  = tile.Contains(Event.current.mousePosition);
            Color prevCol  = GUI.color;

            if (hovered)
            {
                _buildHoverItem        = defn.id;
                _buildHoverContentRect = tile;
            }

            Color bg = hovered
                ? ColTabHl
                : new Color(ColBarBg.r, ColBarBg.g, ColBarBg.b, canPlace ? 0.75f : 0.40f);
            DrawSolid(tile, bg);
            if (hovered)
                DrawSolid(new Rect(tile.x, tile.y, 2f, tile.height), ColAccent);

            GUI.color = canPlace
                ? (hovered ? ColTextHead : ColTextBright)
                : ColTextMid;
            GUI.Label(
                new Rect(tile.x + 4f, tile.y + (tile.height - LineH) * 0.5f, tile.width - 8f, LineH),
                defn.displayName, _sSub);
            GUI.color = prevCol;

            if (GUI.Button(tile, "", GUIStyle.none) && canPlace)
            {
                _ghostBuildableId    = defn.id;
                _ghostRotation       = 0;
                // Save and collapse drawers so the station grid is unobstructed
                _prePlacementTab     = _active;
                _prePlacementSub     = _subActive;
                _active              = Tab.None;
                CloseSub();
            }
        }

        // ── Catalog hover tooltip (drawn after EndScrollView, overlaid on top) ─
        private void DrawCatalogTooltip(BuildableDefinition defn, Rect scrollArea, Vector2 scroll,
                                        float cw, StationState s)
        {
            Color prevCol = GUI.color;

            // Convert content-space tile rect to GUI space
            float tileGuiY = scrollArea.y + _buildHoverContentRect.y - scroll.y;

            // Measure total tooltip height
            float th = 8f + LineH;   // top pad + name
            if (!string.IsNullOrEmpty(defn.description))
                th += _sDescr.CalcHeight(new GUIContent(defn.description), cw - 16f) + 8f;
            th += 4f + LineH;        // divider + build-time line
            th += LineH + (defn.requiredMaterials.Count == 0 ? LineH
                           : defn.requiredMaterials.Count * 18f);
            th += LineH + (defn.requiredSkills.Count == 0 ? LineH
                           : defn.requiredSkills.Count * 18f);
            if (defn.requiredTags.Count > 0)
                th += LineH + defn.requiredTags.Count * 18f;
            th += 10f;               // bottom pad

            // Position below the hovered tile; flip above if near bottom
            float tx = scrollArea.x + 8f;
            float tw = cw;
            float ty = tileGuiY + _buildHoverContentRect.height + 4f;
            if (ty + th > scrollArea.yMax - 4f)
                ty = tileGuiY - th - 4f;
            ty = Mathf.Clamp(ty, scrollArea.y + 4f, scrollArea.yMax - th - 4f);

            // Background: thin accent border + dark fill + left accent bar
            DrawSolid(new Rect(tx - 2f, ty - 2f, tw + 4f, th + 4f), ColBorderLit);
            DrawSolid(new Rect(tx, ty, tw, th), ColSubDrawer);
            DrawSolid(new Rect(tx, ty, 2f, th), ColAccent);

            float iy = ty + 8f;

            // Full (untruncated) name
            GUI.Label(new Rect(tx + 6f, iy, tw - 12f, LineH), defn.displayName, _sLabel);
            iy += LineH;

            // Description
            if (!string.IsNullOrEmpty(defn.description))
            {
                float dh = _sDescr.CalcHeight(new GUIContent(defn.description), tw - 16f);
                GUI.Label(new Rect(tx + 6f, iy, tw - 12f, dh), defn.description, _sDescr);
                iy += dh + 8f;
            }

            DrawSolid(new Rect(tx + 6f, iy, tw - 12f, 1f), new Color(1f, 1f, 1f, 0.06f));
            iy += 4f;

            // Build time + category
            string catShort = defn.category.Length > 0
                ? char.ToUpper(defn.category[0]) + defn.category.Substring(1) : "Other";
            GUI.color = ColTextBase;
            GUI.Label(new Rect(tx + 6f, iy, tw - 12f, LineH),
                      $"Build time: {defn.buildTimeTicks}t  \u00b7  {catShort}", _sSub);
            GUI.color = prevCol;
            iy += LineH + 4f;

            // Materials
            GUI.Label(new Rect(tx + 6f, iy, tw - 12f, LineH), "Materials", _sLabel);
            iy += LineH;
            if (defn.requiredMaterials.Count == 0)
            {
                GUI.color = ColTextMid;
                GUI.Label(new Rect(tx + 14f, iy, tw - 20f, LineH), "None", _sSub);
                GUI.color = prevCol;
                iy += LineH;
            }
            else
            {
                foreach (var m in defn.requiredMaterials)
                {
                    GUI.Label(new Rect(tx + 14f, iy, tw - 20f, LineH),
                              $"\u2022 {ItemDisplayName(m.Key)}  \u00d7{m.Value}", _sSub);
                    iy += 18f;
                }
            }

            // Skills
            GUI.Label(new Rect(tx + 6f, iy, tw - 12f, LineH), "Skills", _sLabel);
            iy += LineH;
            if (defn.requiredSkills.Count == 0)
            {
                GUI.color = ColTextMid;
                GUI.Label(new Rect(tx + 14f, iy, tw - 20f, LineH), "None", _sSub);
                GUI.color = prevCol;
                iy += LineH;
            }
            else
            {
                foreach (var sk in defn.requiredSkills)
                {
                    bool met = false;
                    foreach (var npc in s.GetCrew())
                        if (npc.skills.TryGetValue(sk.Key, out int lvl) && lvl >= sk.Value)
                        { met = true; break; }
                    Color skCol = met ? ColBarGreen : ColBarCrit;
                    var pc = GUI.color;
                    GUI.color = skCol;
                    GUI.Label(new Rect(tx + 14f, iy, 16f, LineH), met ? "\u2713" : "\u2715", _sSub);
                    GUI.color = prevCol;
                    GUI.Label(new Rect(tx + 30f, iy, tw - 36f, LineH),
                              $"{sk.Key}  (level {sk.Value}+)", _sSub);
                    iy += 18f;
                }
            }

            // Station requirements
            if (defn.requiredTags.Count > 0)
            {
                GUI.Label(new Rect(tx + 6f, iy, tw - 12f, LineH), "Station Requirements", _sLabel);
                iy += LineH;
                foreach (var tag in defn.requiredTags)
                {
                    bool met = s.HasTag(tag);
                    Color tagCol = met ? ColBarGreen : ColBarCrit;
                    var pc = GUI.color;
                    GUI.color = tagCol;
                    GUI.Label(new Rect(tx + 14f, iy, 16f, LineH), met ? "\u2713" : "\u2715", _sSub);
                    GUI.color = prevCol;
                    GUI.Label(new Rect(tx + 30f, iy, tw - 36f, LineH), tag, _sSub);
                    iy += 18f;
                }
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
            const float SubTabH = 22f;
            float subY  = area.y;
            float tabW  = Mathf.Floor(w / 3f);
            (string lbl, CrewSubPanel panel)[] crewTabs =
            {
                ("CREW",  CrewSubPanel.Crew),
                ("WORK",  CrewSubPanel.Work),
                ("RANKS", CrewSubPanel.Ranks),
            };
            // Tab row background + bottom border
            DrawSolid(new Rect(area.x, subY, w, SubTabH), ColBar);
            DrawSolid(new Rect(area.x, subY + SubTabH - 1f, w, 1f), ColDivider);
            for (int ti = 0; ti < crewTabs.Length; ti++)
            {
                float tx = area.x + ti * tabW;
                bool  on = _crewSub == crewTabs[ti].panel;
                DrawSolid(new Rect(tx, subY, tabW - 1f, SubTabH - 1f), on ? ColTabHl : ColBar);
                if (on)
                    DrawSolid(new Rect(tx, subY + SubTabH - 3f, tabW - 1f, 2f), ColAccent);
                if (ti > 0)
                    DrawSolid(new Rect(tx, subY, 1f, SubTabH - 1f), ColDivider);
                GUI.color = on ? ColTextHead : ColTextBase;
                if (GUI.Button(new Rect(tx, subY, tabW - 1f, SubTabH - 1f),
                               crewTabs[ti].lbl, on ? _sTabOn : _sTabOff))
                    _crewSub = crewTabs[ti].panel;
                GUI.color = Color.white;
            }

            Rect subArea = new Rect(area.x, area.y + SubTabH + 6f, w, h - SubTabH - 6f);
            float subH   = h - SubTabH - 6f;

            switch (_crewSub)
            {
                case CrewSubPanel.Crew:  DrawCrewList(subArea, w, subH);  break;
                case CrewSubPanel.Work:  DrawCrewWork(subArea, w, subH);  break;
                case CrewSubPanel.Ranks: DrawRanks(subArea, w, subH);     break;
            }
        }

        // ── Crew list (departments + roster merged) ───────────────────────────
        // Compact cards grouped by department. Clicking a card opens the detail view.
        // Department headers have inline rename / add / remove controls.
        private void DrawCrewList(Rect area, float w, float h)
        {
            var s    = _gm.Station;
            var crew = s.GetCrew();

            // ── Summary strip + toolbar ───────────────────────────────────────
            float avgMood = 0f; int sickC = 0, injC = 0;
            foreach (var n in crew)
            {
                avgMood += n.moodScore;
                if (n.statusTags.Contains("sick")) sickC++;
                if (n.injuries > 0) injC++;
            }
            if (crew.Count > 0) avgMood /= crew.Count;

            // Stat chip row: [Crew N] [Mood N%] [Sick N] [Injured N]
            const float ChipH  = 24f;
            const float ChipGap = 4f;
            float SumH = ChipH + 14f + BtnH + 14f;
            DrawSolid(new Rect(area.x, area.y, w, SumH), ColSummaryBg);
            float sy = area.y + 6f;
            {
                var chipPrev = GUI.color;
                float chipW  = (w - 16f - ChipGap * 3f) / 4f;
                // Crew count chip
                DrawSolid(new Rect(area.x + 8f, sy, chipW, ChipH), ColBarBg);
                GUI.color = ColTextBright;
                GUI.Label(new Rect(area.x + 10f, sy + 4f, chipW - 4f, 16f),
                          $"Crew: {crew.Count}", _sSub);
                // Mood chip
                Color moodChipCol = avgMood >= 70f ? ColBarGreen : avgMood >= 40f ? ColBarWarn : ColBarCrit;
                float cx1 = area.x + 8f + chipW + ChipGap;
                DrawSolid(new Rect(cx1, sy, chipW, ChipH), ColBarBg);
                GUI.color = moodChipCol;
                GUI.Label(new Rect(cx1 + 2f, sy + 4f, chipW - 4f, 16f),
                          $"Mood {avgMood:F0}%", _sSub);
                // Sick chip
                float cx2 = cx1 + chipW + ChipGap;
                DrawSolid(new Rect(cx2, sy, chipW, ChipH), ColBarBg);
                GUI.color = sickC > 0 ? ColBarCrit : ColTextBase;
                GUI.Label(new Rect(cx2 + 2f, sy + 4f, chipW - 4f, 16f),
                          $"Sick: {sickC}", _sSub);
                // Injured chip
                float cx3 = cx2 + chipW + ChipGap;
                DrawSolid(new Rect(cx3, sy, chipW, ChipH), ColBarBg);
                GUI.color = injC > 0 ? ColBarWarn : ColTextBase;
                GUI.Label(new Rect(cx3 + 2f, sy + 4f, chipW - 4f, 16f),
                          $"Inj: {injC}", _sSub);
                GUI.color = chipPrev;
            }
            sy += ChipH + 4f;
            // Mood labelled bar
            {
                float bw = w - 16f;
                Color moodBarCol = avgMood >= 70f ? ColBarGreen : avgMood >= 40f ? ColBarWarn : ColBarCrit;
                DrawSolid(new Rect(area.x + 8f, sy, bw, 8f), ColBarBg);
                DrawSolid(new Rect(area.x + 8f, sy, bw * (avgMood / 100f), 8f), moodBarCol);
            }
            sy += 12f;

            // Toolbar row — add more buttons here freely; widths auto-distribute
            Color cfgCol = _crewDeptConfigMode
                ? ColAccent
                : ColTextBase;
            sy = ButtonRow(area.x + 8f, sy, w - 16f, 0f,
                ("⚙ Config Depts", cfgCol, () => _crewDeptConfigMode = !_crewDeptConfigMode),
                ("+ New Dept", null, () =>
                {
                    var nd = Department.Create(
                        $"dept.custom_{System.Guid.NewGuid().ToString("N")[..4]}",
                        "New Department");
                    s.departments.Add(nd);
                    _renamingDeptUid    = nd.uid;
                    _renameDeptBuffer   = nd.name;
                    _crewDeptConfigMode = true;
                }));

            // ── Scrollable body ───────────────────────────────────────────────
            float CardH   = CrewCardH;    // compact single-row card height
            float DeptHdH = DeptHdrH;     // slimmer dept header (no colour swatches inline)

            // Group crew by dept (null dept → "Unassigned")
            var depts      = s.departments;
            var unassigned = crew.FindAll(n => n.departmentId == null
                || !depts.Exists(d => d.uid == n.departmentId));

            // Auto-collapse empty depts if not already decided by user
            foreach (var dept in depts)
            {
                int cnt = crew.FindAll(n => n.departmentId == dept.uid).Count;
                if (cnt == 0 && !_deptCollapsed.Contains(dept.uid + "_manual"))
                    _deptCollapsed.Add(dept.uid);
            }

            // Calculate virtual scroll height
            float innerH = 8f;
            foreach (var dept in depts)
            {
                int  cnt       = crew.FindAll(n => n.departmentId == dept.uid).Count;
                bool collapsed = _deptCollapsed.Contains(dept.uid);
                innerH += DeptHdH + (collapsed ? 0f : cnt * CardH);
                // settings popover
                if (_deptSettingsOpen == dept.uid) innerH += 70f;
            }
            if (unassigned.Count > 0) innerH += DeptHdH + unassigned.Count * CardH;
            innerH = Mathf.Max(innerH, h - SumH - 4f);

            float listTop = area.y + SumH + 4f;
            float listH   = h - SumH - 4f;
            _crewListScroll = GUI.BeginScrollView(
                new Rect(area.x, listTop, w, listH),
                _crewListScroll,
                new Rect(0, 0, w - 22f, innerH));

            float y = 0f;
            float cw = w - 22f;

            // ── Helper: draw one compact crew card ────────────────────────────
            void DrawCrewCard(NPCInstance npc)
            {
                string rankLbl  = npc.rank switch { 1 => "★", 2 => "★★", 3 => "★★★", _ => "" };
                string taskLbl  = npc.missionUid != null ? "✈ Away"
                                : string.IsNullOrEmpty(npc.currentJobId) ? "Idle"
                                : _gm.Jobs.GetJobLabel(npc);
                float  mood     = npc.moodScore;
                float  health01 = npc.injuries > 0
                    ? Mathf.Clamp01(1f - npc.injuries * 0.2f)
                    : (npc.statusTags.Contains("sick") ? 0.5f : 1f);

                bool  crisis = npc.inCrisis;
                Color cardBg = crisis
                    ? new Color(0.22f, 0.06f, 0.06f, 0.92f)
                    : new Color(ColBarBg.r, ColBarBg.g, ColBarBg.b, 0.90f);
                DrawSolid(new Rect(2f, y, cw - 2f, CardH - 2f), cardBg);

                // Avatar initials square — tinted with department colour
                float AW = AvatarSz - 2f;
                var npcDept = depts.Find(d => d.uid == npc.departmentId);
                Color avatarBg = npcDept?.GetColour() ?? ColBevelHi;
                DrawSolid(new Rect(4f, y + 6f, AW, AW), avatarBg);
                string initials = npc.name.Length >= 2
                    ? $"{npc.name[0]}{npc.name[npc.name.LastIndexOf(' ') + 1]}"
                    : npc.name[..1];
                GUI.Label(new Rect(4f, y + 10f, AW, LblH),
                          initials, new GUIStyle(_sSub) { fontSize = FontSize, alignment = TextAnchor.MiddleCenter });

                // Name + rank stars
                float nx = AW + 8f;
                float nw = cw * 0.36f;
                var   rk = GUI.color;
                GUI.Label(new Rect(nx, y + 5f, nw, LblH), npc.name, _sSub);
                GUI.color = npc.rank > 0 ? ColBarWarn : ColTextDim;
                GUI.Label(new Rect(nx, y + LineH + 2f, nw, SmH), rankLbl.Length > 0 ? rankLbl : taskLbl, _sSub);
                GUI.color = rk;

                // Mood bar (no label)
                float bx2 = nx + nw + 4f;
                float bw2  = 64f;
                Color moodCol = MoodSystem.GetMoodColor(mood);
                DrawSolid(new Rect(bx2, y + 12f, bw2, 7f), ColBarBg);
                DrawSolid(new Rect(bx2, y + 12f, bw2 * (mood / 100f), 7f), moodCol);

                // Health bar (no label)
                float hbx = bx2 + bw2 + 6f;
                float hbw = cw - hbx - 4f;
                Color hCol = health01 > 0.6f ? ColBarGreen : health01 > 0.3f ? ColBarWarn : ColBarCrit;
                DrawSolid(new Rect(hbx, y + 12f, hbw, 7f), ColBarBg);
                DrawSolid(new Rect(hbx, y + 12f, hbw * health01, 7f), hCol);

                // Crisis / tension badge
                if (crisis)
                {
                    GUI.color = ColBarCrit;
                    GUI.Label(new Rect(cw - 50f, y + 5f, 48f, SmH), "⚠ Crisis", _sSub);
                    GUI.color = rk;
                }
                else if (FeatureFlags.NpcTraits && npc.traitProfile != null
                         && npc.traitProfile.tensionStage != TensionStage.Normal)
                {
                    GUI.color = TensionSystem.GetTensionStageColor(npc.traitProfile.tensionStage);
                    string tLbl = npc.traitProfile.tensionStage == TensionStage.DepartureRisk
                        ? "⚠ Leave" : "⚠ Tense";
                    GUI.Label(new Rect(cw - 50f, y + 5f, 48f, SmH), tLbl, _sSub);
                    GUI.color = rk;
                }

                // Full-row click → crew detail
                if (GUI.Button(new Rect(0f, y, cw, CardH - 2f), "", GUIStyle.none))
                    OpenSub(SubPanel.CrewDetail, npc.uid);

                y += CardH;
            }

            // ── Draw departments ──────────────────────────────────────────────
            for (int di = 0; di < depts.Count; di++)
            {
                var dept     = depts[di];
                var deptCrew = crew.FindAll(n => n.departmentId == dept.uid);

                // Department header
                DrawSolid(new Rect(0f, y, cw, DeptHdH - 2f), ColBarBg);

                if (_crewDeptConfigMode && _renamingDeptUid == dept.uid)
                {
                    _renameDeptBuffer = GUI.TextField(
                        new Rect(4f, y + 4f, cw - 74f, 20f), _renameDeptBuffer, 30, _sTextField);
                    if (GUI.Button(new Rect(cw - 68f, y + 4f, 32f, 20f), "OK", _sBtnSmall))
                    {
                        string t = _renameDeptBuffer.Trim();
                        if (t.Length > 0) dept.name = t;
                        _renamingDeptUid = "";
                    }
                    if (GUI.Button(new Rect(cw - 34f, y + 4f, 28f, 20f), "✕", _sBtnSmall))
                        _renamingDeptUid = "";
                }
                else if (_crewDeptConfigMode)
                {
                    // Config mode: show name + Rename + Delete
                    GUI.Label(new Rect(6f, y + 5f, cw * 0.48f, 18f),
                              $"{dept.name}  ({deptCrew.Count})", _sLabel);
                    if (GUI.Button(new Rect(cw * 0.52f, y + 4f, 48f, 20f), "Rename", _sBtnSmall))
                    {
                        _renamingDeptUid  = dept.uid;
                        _renameDeptBuffer = dept.name;
                    }
                    bool canRemove = deptCrew.Count == 0;
                    var  rmPrev   = GUI.color;
                    if (!canRemove) GUI.color = ColTextDim;
                    if (GUI.Button(new Rect(cw * 0.52f + 52f, y + 4f, 42f, 20f), "✕ Del", _sBtnSmall)
                        && canRemove)
                    {
                        s.departments.RemoveAt(di);
                        di--;
                        y += DeptHdH;
                        continue;
                    }
                    GUI.color = rmPrev;
                }
                else
                {
                    var scPrev = GUI.color;

                    // Chevron expand/collapse
                    bool collapsed = _deptCollapsed.Contains(dept.uid);
                    string chevron = collapsed ? "▶" : "▼";
                    GUI.color = ColTextBase;
                    if (GUI.Button(new Rect(2f, y + 7f, 18f, 16f), chevron, _sBtnSmall))
                    {
                        if (collapsed)
                        {
                            _deptCollapsed.Remove(dept.uid);
                            _deptCollapsed.Add(dept.uid + "_manual");
                        }
                        else
                        {
                            _deptCollapsed.Add(dept.uid);
                            _deptCollapsed.Remove(dept.uid + "_manual");
                        }
                    }
                    GUI.color = scPrev;

                    // Name + count
                    GUI.Label(new Rect(22f, y + 7f, cw - 60f, 16f),
                              $"{dept.name}  ({deptCrew.Count})", _sLabel);

                    // (...) settings popover toggle
                    bool settingsOpen = _deptSettingsOpen == dept.uid;
                    GUI.color = settingsOpen ? ColAccent : ColTextBase;
                    if (GUI.Button(new Rect(cw - 28f, y + 6f, 24f, 18f), "···", _sBtnSmall))
                        _deptSettingsOpen = settingsOpen ? "" : dept.uid;
                    GUI.color = scPrev;

                    // Settings popover
                    if (settingsOpen)
                    {
                        float py2   = y + DeptHdH;
                        float pickH = 76f;
                        DrawSolid(new Rect(0f, py2, cw, pickH), ColBarBg);
                        DrawSolid(new Rect(0f, py2, cw, 1f), ColDivider);

                        // Colour swatches row
                        Color? primC  = dept.GetColour();
                        Color  swPrim = primC ?? ColBevelHi;
                        Color? secC   = dept.GetSecondaryColour();
                        Color  swSec  = secC  ?? ColBevelHi;
                        GUI.color = ColTextBase;
                        GUI.Label(new Rect(4f, py2 + 6f, 44f, 14f), "Colours:", _sSub);
                        GUI.color = swPrim;
                        bool primClick = GUI.Button(new Rect(52f, py2 + 5f, 16f, 16f), "■", _sBtnSmall);
                        GUI.color = scPrev;
                        GUI.Label(new Rect(70f, py2 + 6f, 30f, 14f), "Dept", _sSub);
                        GUI.color = swSec;
                        bool secClick  = GUI.Button(new Rect(106f, py2 + 5f, 16f, 16f), "■", _sBtnSmall);
                        GUI.color = scPrev;
                        GUI.Label(new Rect(124f, py2 + 6f, 44f, 14f), "Accent", _sSub);
                        if (primClick) OpenDeptPicker(dept.uid, "primary");
                        if (secClick)  OpenDeptPicker(dept.uid, "secondary");

                        // Job toggles row
                        GUI.color = ColTextMid;
                        GUI.Label(new Rect(4f, py2 + 28f, 28f, 14f), "Jobs:", _sSub);
                        GUI.color = scPrev;
                        float jBtnW = (cw - 36f - (WorkJobCols.Length - 1) * 2f) / WorkJobCols.Length;
                        for (int ji = 0; ji < WorkJobCols.Length; ji++)
                        {
                            string jid2    = WorkJobCols[ji].id;
                            bool   blocked = _deptJobBlockList.TryGetValue(dept.uid, out var blSet)
                                             && blSet.Contains(jid2);
                            float  jx      = 34f + ji * (jBtnW + 2f);
                            GUI.color = blocked ? ColTextDim : ColBarGreen;
                            if (GUI.Button(new Rect(jx, py2 + 26f, jBtnW, 18f), WorkJobCols[ji].label, _sBtnSmall))
                            {
                                if (!_deptJobBlockList.ContainsKey(dept.uid))
                                    _deptJobBlockList[dept.uid] = new HashSet<string>();
                                if (blocked) _deptJobBlockList[dept.uid].Remove(jid2);
                                else         _deptJobBlockList[dept.uid].Add(jid2);
                            }
                        }
                        GUI.color = scPrev;
                        y += pickH;
                    }

                    // ── Inline colour picker for this department ──────────────
                    if (_deptPickerUid == dept.uid)
                    {
                        float py2   = y + DeptHdH;
                        float pickH = 110f;
                        DrawSolid(new Rect(0f, py2, cw, pickH), new Color(0.08f, 0.10f, 0.18f, 1f));
                        DrawSolid(new Rect(0f, py2, cw, 1f), ColDivider);

                        string chanLabel = _deptPickerChannel == "primary" ? "Primary" : "Accent";
                        GUI.Label(new Rect(4f, py2 + 4f, cw - 8f, 14f),
                                  $"{chanLabel} Colour", _sLabel);

                        float sx = 4f, slW = cw - 56f;
                        GUI.Label(new Rect(sx, py2 + 20f, 14f, 12f), "H", _sSub);
                        _deptPickerH = GUI.HorizontalSlider(new Rect(sx + 16f, py2 + 23f, slW, 10f), _deptPickerH, 0f, 1f);
                        GUI.Label(new Rect(sx, py2 + 34f, 14f, 12f), "S", _sSub);
                        _deptPickerS = GUI.HorizontalSlider(new Rect(sx + 16f, py2 + 37f, slW, 10f), _deptPickerS, 0f, 1f);
                        GUI.Label(new Rect(sx, py2 + 48f, 14f, 12f), "V", _sSub);
                        _deptPickerV = GUI.HorizontalSlider(new Rect(sx + 16f, py2 + 51f, slW, 10f), _deptPickerV, 0f, 1f);

                        // Hex input
                        Color previewCol = Color.HSVToRGB(_deptPickerH, _deptPickerS, _deptPickerV);
                        GUI.Label(new Rect(sx, py2 + 64f, 24f, 14f), "Hex", _sSub);
                        string newHex = GUI.TextField(new Rect(sx + 28f, py2 + 62f, 70f, 18f),
                                                      _deptPickerHexInput, 9, _sTextField);
                        if (newHex != _deptPickerHexInput)
                        {
                            _deptPickerHexInput = newHex;
                            if (ColorUtility.TryParseHtmlString(newHex, out Color parsedHc))
                                Color.RGBToHSV(parsedHc, out _deptPickerH, out _deptPickerS, out _deptPickerV);
                        }
                        else
                        {
                            _deptPickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(previewCol);
                        }

                        // Preview swatch
                        var pcp = GUI.color; GUI.color = previewCol;
                        DrawSolid(new Rect(sx + 102f, py2 + 62f, 20f, 18f), previewCol);
                        GUI.color = pcp;

                        // Confirm / Clear / Cancel buttons
                        float btnY = py2 + 84f;
                        float bbw = (cw - 12f) / 3f;
                        GUI.color = new Color(0.22f, 0.76f, 0.35f);
                        if (GUI.Button(new Rect(sx, btnY, bbw, 20f), "Confirm", _sBtnSmall))
                        {
                            ApplyDeptPickerColour(dept, previewCol);
                            _deptPickerUid = "";
                        }
                        GUI.color = new Color(0.55f, 0.60f, 0.70f);
                        if (GUI.Button(new Rect(sx + bbw + 4f, btnY, bbw, 20f), "Clear", _sBtnSmall))
                        {
                            ClearDeptPickerColour(dept);
                            _deptPickerUid = "";
                        }
                        GUI.color = new Color(0.55f, 0.60f, 0.70f);
                        if (GUI.Button(new Rect(sx + (bbw + 4f) * 2f, btnY, bbw, 20f), "Cancel", _sBtnSmall))
                            _deptPickerUid = "";
                        GUI.color = scPrev;

                        // Reserve the inline picker height in the layout
                        y += pickH;
                    }
                }
                y += DeptHdH;

                if (!_deptCollapsed.Contains(dept.uid))
                    foreach (var npc in deptCrew)
                        DrawCrewCard(npc);
            }

            // ── Unassigned ────────────────────────────────────────────────────
            if (unassigned.Count > 0)
            {
                DrawSolid(new Rect(0f, y, cw, DeptHdH - 2f), new Color(0.12f, 0.15f, 0.25f, 1f));
                var uaPrev = GUI.color;
                GUI.color = new Color(0.90f, 0.68f, 0.22f);
                GUI.Label(new Rect(6f, y + 7f, cw - 8f, 16f),
                          $"⚠ Unassigned  ({unassigned.Count})", _sLabel);
                GUI.color = uaPrev;
                y += DeptHdH;
                foreach (var npc in unassigned)
                    DrawCrewCard(npc);
            }

            if (crew.Count == 0 && depts.Count == 0)
                GUI.Label(new Rect(4f, y, cw, 20f), "No crew assigned.", _sSub);

            GUI.EndScrollView();
        }

        // ── NPC Detail view ───────────────────────────────────────────────────
        // Shows full stats, skills, expertise, medical for one NPC.
        private void DrawCrewDetail(Rect area, float w, float h, NPCInstance npc)
        {
            var s = _gm.Station;

            // ── Resolve dept / rank / class / age ────────────────────────────
            Department dept = null;
            foreach (var d in s.departments)
                if (d.uid == npc.departmentId) { dept = d; break; }
            string deptName = dept?.name ?? "Unassigned";
            string cls      = (npc.classId ?? "").Replace("class.", "");
            string rankLbl  = npc.rank switch { 1 => "Officer", 2 => "Senior Officer", 3 => "Command", _ => "Crew" };
            int    ageYears = npc.ageDays / 365;

            // ── XP / level data ───────────────────────────────────────────────
            int   charLevel     = SkillSystem.GetCharacterLevel(npc);
            float totalXP       = SkillSystem.GetTotalXP(npc);
            int   curThreshold  = (charLevel / SkillSystem.SlotEveryNLevels)
                                  * SkillSystem.SlotEveryNLevels
                                  * SkillSystem.CharLevelDivisor;
            int   nextThreshold = (charLevel / SkillSystem.SlotEveryNLevels + 1)
                                  * SkillSystem.SlotEveryNLevels
                                  * SkillSystem.CharLevelDivisor;
            float xpPct         = Mathf.Clamp01((totalXP - curThreshold)
                                  / Mathf.Max(1f, nextThreshold - curThreshold));
            int   slotCount     = SkillSystem.GetExpertiseSlotCount(npc);
            int   unspent       = SkillSystem.GetUnspentSlots(npc);

            var   prevC = GUI.color;
            float cw    = w - 8f;

            // ═══ STICKY HEADER ══════════════════════════════════════════════
            float HdrH = NpcHdrH;
            DrawSolid(new Rect(area.x, area.y, w, HdrH), new Color(0.09f, 0.11f, 0.18f, 1f));
            DrawSolid(new Rect(area.x, area.y + HdrH - 1f, w, 1f), ColDivider);

            // ── Bio header: avatar chip + dept/rank/level/age ─────────────────
            float AW = AvatarSz;
            Color avatarBg = dept?.GetColour() ?? new Color(0.20f, 0.24f, 0.36f, 1f);
            DrawSolid(new Rect(area.x + 4f, area.y + (HdrH - AW) * 0.5f, AW, AW), avatarBg);
            string initials = npc.name.Length >= 2
                ? $"{npc.name[0]}{npc.name[npc.name.LastIndexOf(' ') + 1]}"
                : npc.name[..1];
            GUI.Label(new Rect(area.x + 4f, area.y + (HdrH - AW) * 0.5f + 2f, AW, LineH + 2f),
                      initials, new GUIStyle(_sSub) { fontSize = FontSize, alignment = TextAnchor.MiddleCenter });

            float nx     = area.x + AW + 10f;
            float nxW    = w - AW - 78f;

            // Row 1: dept · rank (brightened dept colour for legibility on dark bg)
            GUI.color = Color.Lerp(avatarBg, Color.white, 0.55f);
            GUI.Label(new Rect(nx, area.y + 8f, nxW, LblH), $"{deptName}  ·  {rankLbl}", _sSub);
            GUI.color = prevC;

            // Row 2: class · species · level · age
            string speciesStr = !string.IsNullOrEmpty(npc.species) ? npc.species : "human";
            GUI.color = new Color(0.55f, 0.60f, 0.70f);
            GUI.Label(new Rect(nx, area.y + LineH + 4f, nxW, LblH),
                      $"{cls}  ·  {speciesStr}  ·  Lv {charLevel}  ·  Age {ageYears}y", _sSub);
            GUI.color = prevC;

            // XP bar (4px strip at bottom of header)
            DrawSolid(new Rect(nx, area.y + HdrH - 12f, nxW, 4f), ColBarBg);
            DrawSolid(new Rect(nx, area.y + HdrH - 12f, nxW * xpPct, 4f), ColAccent);



            _skillsSelectedNpcUid = npc.uid;

            // Expertise-selection panel overrides scrollable body
            if (_expertisePanelOpen)
            {
                DrawExpertiseSelectionPanel(
                    new Rect(area.x, area.y + HdrH + 2f, w, h - HdrH - 2f),
                    w, h - HdrH - 2f, npc);
                return;
            }

            // ═══ CREW DETAIL SUB-TAB BAR ════════════════════════════════
            const float CdTabH = 22f;
            float cdTabY = area.y + HdrH + 2f;
            DrawSolid(new Rect(area.x, cdTabY, w, CdTabH), new Color(0.09f, 0.11f, 0.18f, 1f));
            DrawSolid(new Rect(area.x, cdTabY + CdTabH - 1f, w, 1f), ColDivider);
            (CrewDetailTab t, string l)[] cdTabs =
            {
                (CrewDetailTab.Vitals,        "Vitals"),
                (CrewDetailTab.Skills,        "Skills"),
                (CrewDetailTab.Relationships, "Rels"),
                (CrewDetailTab.Inventory,     "Inv"),
            };
            float cdTW = Mathf.Floor(w / cdTabs.Length);
            for (int cti = 0; cti < cdTabs.Length; cti++)
            {
                float ctx = area.x + cti * cdTW;
                bool  con = _crewDetailTab == cdTabs[cti].t;
                if (con)
                {
                    DrawSolid(new Rect(ctx, cdTabY, cdTW, CdTabH - 1f),
                              new Color(0.12f, 0.18f, 0.30f, 1f));
                    DrawSolid(new Rect(ctx, cdTabY + CdTabH - 3f, cdTW, 2f), ColAccent);
                }
                if (GUI.Button(new Rect(ctx, cdTabY, cdTW, CdTabH - 1f),
                               cdTabs[cti].l, con ? _sTabOn : _sTabOff))
                    _crewDetailTab = cdTabs[cti].t;
            }

            // ═══ FLAT BODY (no scrollbar — panel grows to fit content) ════════════
            float bodyStartY = area.y + HdrH + 2f + CdTabH;
            GUI.BeginGroup(new Rect(area.x, bodyStartY, w, 9999f));
            float y = 4f;

            // ── TAB: VITALS ───────────────────────────────────────────────────
            if (_crewDetailTab == CrewDetailTab.Vitals)
            {
                float colW2 = (cw - 8f) * 0.5f;
                float barW2 = colW2 - CharW * 5f - 4f;   // column width minus label and gap
                float barWF = cw - CharW * 12f;   // bar width; remaining space for right badge/value

                void VBar(float cx, string lbl, float val, Color col)
                {
                    GUI.color = ColTextBase;
                    GUI.Label(new Rect(cx, y + 2f, CharW * 5f, LblH), lbl, _sSub);
                    GUI.color = prevC;
                    DrawSolid(new Rect(cx + CharW * 5f + 2f, y + 6f, barW2, 6f), ColBarBg);
                    DrawSolid(new Rect(cx + CharW * 5f + 2f, y + 6f, barW2 * Mathf.Clamp01(val), 6f), col);
                }

                void NBar(string lbl, float val, Color col, string badge = null)
                {
                    float lblW = CharW * 5.5f;
                    GUI.color = ColTextBase;
                    GUI.Label(new Rect(2f, y + 2f, lblW, LblH), lbl, _sSub);
                    GUI.color = prevC;
                    DrawSolid(new Rect(lblW + 4f, y + 6f, barWF, 6f), ColBarBg);
                    DrawSolid(new Rect(lblW + 4f, y + 6f, barWF * Mathf.Clamp01(val), 6f), col);
                    string rightLbl = badge ?? $"{val * 100f:F0}%";
                    GUI.color = badge != null ? ColBarWarn : ColTextBase;
                    float rw = cw - lblW - 4f - barWF - 4f;
                    GUI.Label(new Rect(lblW + 4f + barWF + 4f, y + 2f, rw, LblH), rightLbl, _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }

                // --- Mood + Health (2-column) --------------------------------
                int   inj   = npc.injuries;
                bool  sick  = npc.statusTags.Contains("sick");
                float hp01  = inj > 0 ? Mathf.Clamp01(1f - inj * 0.2f) : (sick ? 0.5f : 1f);
                Color moodC = MoodSystem.GetMoodColor(npc.moodScore);
                Color hpC   = hp01 > 0.6f ? ColBarGreen : hp01 > 0.3f ? ColBarWarn : ColBarCrit;
                VBar(2f,          "Mood",   npc.moodScore / 100f, moodC);
                VBar(colW2 + 6f,  "Health", hp01,                 hpC);
                y += LblH;

                // --- Needs section ------------------------------------------
                GUI.color = ColAccent;
                GUI.Label(new Rect(2f, y + 2f, 60f, LblH), "Needs", _sSub);
                GUI.color = prevC;
                y += LblH;

                float sleepV = npc.sleepNeed      != null ? npc.sleepNeed.value      / 100f : GetNeed(npc, "sleep");
                float hungV  = npc.hungerNeed     != null ? npc.hungerNeed.value     / 100f : GetNeed(npc, "hunger");
                float thirV  = npc.thirstNeed     != null ? npc.thirstNeed.value     / 100f : 1f;
                float recreV = npc.recreationNeed != null ? npc.recreationNeed.value / 100f : 1f;
                float socV   = npc.socialNeed     != null ? npc.socialNeed.value     / 100f : GetNeed(npc, "social");

                string sleepBadge = npc.sleepNeed      != null && npc.sleepNeed.isSeeking        ? "Seeking"   : null;
                string hungBadge  = npc.hungerNeed     != null && npc.hungerNeed.isMalnourished  ? "Malnour."  : null;
                string burntBadge = npc.recreationNeed != null && npc.recreationNeed.isBurntOut  ? "Burnt Out" : null;
                string reclBadge  = npc.socialNeed     != null && npc.socialNeed.isReclusive     ? "Reclusive" : null;

                NBar("Sleep",      sleepV, sleepV < 0.30f ? ColBarCrit : sleepV < 0.60f ? ColBarWarn : ColBarGreen, sleepBadge);
                NBar("Hunger",     hungV,  hungV  < 0.30f ? ColBarCrit : hungV  < 0.60f ? ColBarWarn : ColBarGreen, hungBadge);
                NBar("Thirst",     thirV,  thirV  < 0.30f ? ColBarCrit : thirV  < 0.60f ? ColBarWarn : ColBarGreen);
                NBar("Recreation", recreV, recreV < 0.30f ? ColBarCrit : recreV < 0.60f ? ColBarWarn : ColBarGreen, burntBadge);
                NBar("Social",     socV,   socV   < 0.30f ? ColBarCrit : socV   < 0.60f ? ColBarWarn : ColBarGreen, reclBadge);
                y += 6f;

                // --- Ability Scores -----------------------------------------
                GUI.color = ColAccent;
                GUI.Label(new Rect(2f, y + 2f, cw, LblH), "Ability Scores", _sSub);
                GUI.color = prevC;
                y += LblH;

                var   ab       = npc.abilityScores;
                float abilColW = cw / 3f;
                void AbilCol(float cx2, string abbr, int score, int modVal)
                {
                    string modStr = modVal >= 0 ? $"+{modVal}" : $"{modVal}";
                    Color  modC   = modVal > 0 ? ColBarGreen
                                  : modVal < 0 ? ColBarCrit
                                  : new Color(0.5f, 0.55f, 0.65f, 1f);
                    GUI.color = ColTextBase;
                    GUI.Label(new Rect(cx2, y + 2f, 28f, LblH), abbr, _sSub);
                    GUI.color = modC;
                    GUI.Label(new Rect(cx2 + 28f, y + 2f, abilColW - 30f, LblH), $"{score}({modStr})", _sSub);
                    GUI.color = prevC;
                }
                AbilCol(0f,             "STR", ab.STR, ab.STRMod);
                AbilCol(abilColW,       "DEX", ab.DEX, ab.DEXMod);
                AbilCol(abilColW * 2f,  "INT", ab.INT, ab.INTMod);
                y += LineH;
                AbilCol(0f,             "WIS", ab.WIS, ab.WISMod);
                AbilCol(abilColW,       "CHA", ab.CHA, ab.CHAMod);
                AbilCol(abilColW * 2f,  "END", ab.END, ab.ENDMod);
                y += LineH;
                y += 6f;

                // --- Sanity -------------------------------------------------
                var san = npc.sanity;
                if (san != null)
                {
                    float lo     = -10f;
                    float range  = Mathf.Max(1f, san.ceiling - lo);
                    float sanPct = Mathf.Clamp01((san.score - lo) / range);
                    Color sanC   = san.isInBreakdown ? ColBarCrit
                                 : san.score < 0    ? ColBarWarn
                                 :                   ColBarGreen;
                    float sanLblW = CharW * 5.5f;
                    GUI.color = ColTextBase;
                    GUI.Label(new Rect(2f, y + 2f, sanLblW, LblH), "Sanity", _sSub);
                    GUI.color = prevC;
                    DrawSolid(new Rect(sanLblW + 4f, y + 6f, barWF, 6f), ColBarBg);
                    DrawSolid(new Rect(sanLblW + 4f, y + 6f, barWF * sanPct, 6f), sanC);
                    GUI.color = san.isInBreakdown ? ColBarCrit : ColTextBase;
                    float sanRW = cw - sanLblW - 4f - barWF - 4f;
                    GUI.Label(new Rect(sanLblW + 4f + barWF + 4f, y + 2f, sanRW, LblH),
                              san.isInBreakdown ? "⚠ Break" : $"{san.score}/{san.ceiling}", _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }

                y += 4f;

                // --- Task ---------------------------------------------------
                string jobLbl = npc.missionUid != null ? "✈ Away" : _gm.Jobs.GetJobLabel(npc);
                GUI.color = ColTextBase;
                GUI.Label(new Rect(2f, y + 2f, CharW * 5f, LblH), "Task:", _sSub);
                GUI.color = prevC;
                GUI.Label(new Rect(CharW * 5f + 4f, y + 2f, cw - CharW * 5f - 6f, LblH), jobLbl, _sSub);
                y += LineH;

                // --- Status badges ------------------------------------------
                if (npc.inCrisis || sick)
                {
                    string st = "";
                    if (npc.inCrisis) st += "⚠ Crisis  ";
                    if (sick)         st += "Sick";
                    GUI.color = ColBarWarn;
                    GUI.Label(new Rect(2f, y + 2f, cw, LblH), st.Trim(), _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }

                // --- Trait tension ------------------------------------------
                if (FeatureFlags.NpcTraits && npc.traitProfile != null
                    && npc.traitProfile.tensionStage != TensionStage.Normal)
                {
                    Color stC = TensionSystem.GetTensionStageColor(npc.traitProfile.tensionStage);
                    DrawSolid(new Rect(2f, y, cw - 2f, LblH),
                              new Color(stC.r, stC.g, stC.b, 0.18f));
                    GUI.color = stC;
                    GUI.Label(new Rect(4f, y, cw, LineH),
                              TensionSystem.GetTensionStageLabel(npc.traitProfile.tensionStage), _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }
                y += 4f;
            }

            // ── TAB: SKILLS ───────────────────────────────────────────────────
            if (_crewDetailTab == CrewDetailTab.Skills)
            {
                float halfW = (cw - 6f) * 0.5f;
                int   col2  = 0;

                foreach (var skillDef in _gm.Registry.Skills.Values.OrderBy(sd => sd.skillId))
                {
                    var   inst        = SkillSystem.GetSkillInstance(npc, skillDef.skillId);
                    int   level       = inst?.Level ?? 0;
                    float xp2         = inst?.currentXP ?? 0f;
                    float floorXP     = SkillSystem.GetXPForLevel(level);
                    float ceilXP      = SkillSystem.GetXPForLevel(level + 1);
                    float withinLevel = Mathf.Clamp01(ceilXP > floorXP
                        ? (xp2 - floorXP) / (ceilXP - floorXP) : 1f);
                    bool  hasLevel    = level > 0;

                    float cx2 = col2 == 0 ? 2f : 2f + halfW + 4f;
                    GUI.color = hasLevel ? Color.white : new Color(0.38f, 0.42f, 0.52f);
                    GUI.Label(new Rect(cx2, y + 1f, 18f, LblH), $"{level}",
                              new GUIStyle(_sSub) { fontSize = FontSize });
                    GUI.color = hasLevel ? new Color(0.72f, 0.78f, 0.92f) : new Color(0.38f, 0.42f, 0.52f);
                    GUI.Label(new Rect(cx2 + 20f, y + 2f, halfW - 54f, SmH),
                              skillDef.displayName, _sSub);
                    GUI.color = prevC;
                    Color sbcol = hasLevel ? ColAccent : new Color(0.20f, 0.23f, 0.34f);
                    float sbx2  = cx2 + halfW - 32f;
                    DrawSolid(new Rect(sbx2, y + 5f, 28f, 5f), ColBarBg);
                    DrawSolid(new Rect(sbx2, y + 5f, 28f * withinLevel, 5f), sbcol);

                    if (col2 == 1) { y += SmH; col2 = 0; }
                    else col2 = 1;
                }
                if (col2 == 1) y += SmH;
                y += 4f;
            }

            // ── EXPERTISE (folded into Skills tab) ──────────────────────────
            if (_crewDetailTab == CrewDetailTab.Skills)
            {
                DrawSolid(new Rect(0, y, cw, 1f), ColDivider); y += 8f;
                GUI.color = new Color(0.42f, 0.48f, 0.58f);
                GUI.Label(new Rect(2f, y, cw, SmH), "Expertise", _sLabel);
                GUI.color = prevC;
                y += LblH;
                if (npc.chosenExpertise.Count == 0)
                {
                    GUI.color = new Color(0.40f, 0.44f, 0.54f);
                    GUI.Label(new Rect(4f, y + 2f, cw, SmH), "None chosen.", _sSub);
                    GUI.color = prevC;
                    y += LblH;
                }
                else
                {
                    float cardH = LineH * 2f + 6f;
                    foreach (var eid in npc.chosenExpertise)
                    {
                        if (!_gm.Registry.Expertises.TryGetValue(eid, out var exp)) continue;
                        DrawSolid(new Rect(2f, y, cw - 4f, cardH), new Color(0.12f, 0.15f, 0.22f, 0.9f));
                        GUI.Label(new Rect(6f, y + 2f, cw * 0.65f, LineH), exp.displayName, _sSub);
                        if (GUI.Button(new Rect(cw - 66f, y + 2f, 58f, LineH), "Replace", _sBtnSmall))
                        {
                            _swapTargetExpertiseId = eid;
                            _expertisePanelOpen    = true;
                        }
                        float r2y = y + 2f + LineH + 2f;
                        GUI.Label(new Rect(6f, r2y, cw - 72f, LineH),
                                  exp.description.Length > ExpertiseDescShortMaxChars + 3
                                      ? exp.description.Substring(0, ExpertiseDescShortMaxChars) + "..."
                                      : exp.description, _sSub);
                        y += cardH + 4f;
                    }
                }

                if (slotCount == 0)
                {
                    GUI.color = new Color(0.40f, 0.44f, 0.54f);
                    GUI.Label(new Rect(4f, y + 2f, cw, SmH), "Expertise slots unlock at Lv 5", _sSub);
                    GUI.color = prevC;
                    y += LblH;
                }
                else if (unspent > 0)
                {
                    if (GUI.Button(new Rect(4f, y, cw - 6f, 22f),
                                   $"Spend Expertise Slot ({unspent} available)", _sBtnWide))
                    {
                        _swapTargetExpertiseId = "";
                        _expertisePanelOpen    = true;
                    }
                    y += 26f;
                }
                y += 4f;
            }

            // ── TAB: RELATIONSHIPS ────────────────────────────────────────────
            if (_crewDetailTab == CrewDetailTab.Relationships)
            {
                var myRels = new System.Collections.Generic.List<RelationshipRecord>();
                foreach (var rec in s.relationships.Values)
                {
                    if (rec.npcUid1 != npc.uid && rec.npcUid2 != npc.uid) continue;
                    if (rec.relationshipType == RelationshipType.None && rec.affinityScore == 0f) continue;
                    if (!s.npcs.ContainsKey(rec.npcUid1) || !s.npcs.ContainsKey(rec.npcUid2)) continue;
                    myRels.Add(rec);
                }

                if (myRels.Count == 0)
                {
                    GUI.color = new Color(0.40f, 0.44f, 0.54f);
                    GUI.Label(new Rect(4f, y + 2f, cw, SmH), "No notable relationships yet.", _sSub);
                    GUI.color = prevC;
                    y += LblH;
                }
                else
                {
                    foreach (var rec in myRels)
                    {
                        string otherId = rec.npcUid1 == npc.uid ? rec.npcUid2 : rec.npcUid1;
                        if (!s.npcs.TryGetValue(otherId, out var other)) continue;

                        Department otherDept = null;
                        foreach (var d in s.departments)
                            if (d.uid == other.departmentId) { otherDept = d; break; }
                        Color chipBg = otherDept?.GetColour() ?? new Color(0.20f, 0.24f, 0.36f, 1f);
                        string otherInits = other.name.Length >= 2
                            ? $"{other.name[0]}{other.name[other.name.LastIndexOf(' ') + 1]}"
                            : other.name[..1];

                        DrawSolid(new Rect(2f, y + 3f, 24f, 24f), chipBg);
                        GUI.Label(new Rect(2f, y + 7f, 24f, LblH),
                                  otherInits, new GUIStyle(_sSub) { fontSize = FontSize - 2, alignment = TextAnchor.MiddleCenter });

                        GUI.Label(new Rect(30f, y + 4f, cw * 0.35f, SmH), other.name, _sSub);

                        Color typeColor = rec.relationshipType switch
                        {
                            RelationshipType.Friend => ColBarGreen,
                            RelationshipType.Lover  => new Color(1f, 0.45f, 0.72f),
                            RelationshipType.Spouse => new Color(1f, 0.62f, 0.90f),
                            RelationshipType.Enemy  => ColBarCrit,
                            _                       => ColAccent
                        };
                        GUI.color = typeColor;
                        GUI.Label(new Rect(30f + cw * 0.35f, y + 4f, 64f, SmH),
                                  rec.relationshipType.ToString(), _sSub);
                        GUI.color = prevC;

                        // ±100 affinity bar centred on midpoint
                        float bx2  = 30f + cw * 0.35f + 66f;
                        float bfw  = cw - bx2 - 32f;
                        float mid2 = bx2 + bfw * 0.5f;
                        DrawSolid(new Rect(bx2, y + 10f, bfw, 5f), ColBarBg);
                        DrawSolid(new Rect(mid2 - 1f, y + 8f, 2f, 9f), new Color(1f, 1f, 1f, 0.12f));
                        Color bCol2 = rec.affinityScore >= 30f ? ColBarGreen
                                    : rec.affinityScore <= -30f ? ColBarCrit
                                    : new Color(0.38f, 0.42f, 0.52f);
                        if (rec.affinityScore >= 0f)
                            DrawSolid(new Rect(mid2, y + 10f,
                                               bfw * 0.5f * (rec.affinityScore / 100f), 5f), bCol2);
                        else
                            DrawSolid(new Rect(mid2 + bfw * 0.5f * (rec.affinityScore / 100f), y + 10f,
                                               bfw * 0.5f * (-rec.affinityScore / 100f), 5f), bCol2);

                        GUI.color = typeColor;
                        GUI.Label(new Rect(cw - 30f, y + 4f, 28f, SmH),
                                  $"{rec.affinityScore:+0;-0;0}", _sSub);
                        GUI.color = prevC;
                        y += 32f;
                    }
                }
                y += 4f;
            }

            // ── TRAITS (in Relationships tab) ────────────────────────────────
            if (_crewDetailTab == CrewDetailTab.Relationships &&
                FeatureFlags.NpcTraits && npc.traitProfile != null &&
                npc.traitProfile.traits.Count > 0)
            {
                DrawSolid(new Rect(0, y, cw, 20f), new Color(0.10f, 0.12f, 0.22f, 1f));
                GUI.Label(new Rect(18f, y + 3f, cw - 18f, SmH), "Traits", _sLabel);
                y += LineH;

                var byCategory = new System.Collections.Generic.SortedDictionary<string,
                    System.Collections.Generic.List<ActiveTrait>>();
                foreach (var active in npc.traitProfile.traits)
                {
                    NpcTraitDefinition def = null;
                    _gm.Traits?.TryGetTrait(active.traitId, out def);
                    string catKey = def != null ? def.category.ToString() : "Unknown";
                    if (!byCategory.ContainsKey(catKey))
                        byCategory[catKey] = new System.Collections.Generic.List<ActiveTrait>();
                    byCategory[catKey].Add(active);
                }

                foreach (var catKv in byCategory)
                {
                    GUI.color = ColAccent;
                    GUI.Label(new Rect(4f, y, cw, SmH), catKv.Key, _sSub);
                    GUI.color = prevC;
                    y += SmH;

                    foreach (var active in catKv.Value)
                    {
                        NpcTraitDefinition def2 = null;
                        _gm.Traits?.TryGetTrait(active.traitId, out def2);
                        if (def2 == null) continue;

                        Color valC = def2.valence switch
                        {
                            TraitValence.Positive => ColBarGreen,
                            TraitValence.Negative => ColBarCrit,
                            _                     => ColBarWarn,
                        };
                        string valSym = def2.valence switch
                        {
                            TraitValence.Positive => "▲",
                            TraitValence.Negative => "▼",
                            _                     => "●",
                        };
                        GUI.color = valC;
                        GUI.Label(new Rect(8f, y, SmH, SmH), valSym, _sSub);
                        GUI.color = prevC;
                        GUI.Label(new Rect(22f, y, cw - 62f, SmH), def2.displayName, _sSub);
                        float bx3 = 22f + cw - 60f + 2f;
                        DrawSolid(new Rect(bx3, y + 4f, 36f, 6f), ColBarBg);
                        DrawSolid(new Rect(bx3, y + 4f, 36f * active.strength, 6f), valC);
                        y += SmH;
                    }
                }
            }

            // ── TAB: INVENTORY ─────────────────────────────────────────────────
            if (_crewDetailTab == CrewDetailTab.Inventory)
            {
                // ── Equipped section ────────────────────────────────
                DrawSolid(new Rect(0, y, cw, SmH + 4f), new Color(0.10f, 0.12f, 0.22f, 1f));
                GUI.color = ColAccent;
                GUI.Label(new Rect(4f, y + 2f, cw, SmH), "Equipped", _sLabel);
                GUI.color = prevC;
                y += SmH + 6f;

                bool anyEquipped = npc.equippedSlots != null && npc.equippedSlots.Count > 0;
                if (anyEquipped)
                {
                    foreach (var kv in npc.equippedSlots)
                    {
                        string slotName  = kv.Key;
                        string itemId    = kv.Value;
                        string itemName  = _gm.Registry.Items.TryGetValue(itemId, out var def)
                                         ? def.displayName : itemId;
                        float  nameW     = cw - CharW * 8f;
                        GUI.color = ColTextBase;
                        GUI.Label(new Rect(4f,      y + 2f, CharW * 7f, LblH), slotName,  _sSub);
                        GUI.color = prevC;
                        GUI.Label(new Rect(CharW * 8f, y + 2f, nameW,      LblH), itemName, _sSub);
                        y += LineH;
                    }
                }
                else
                {
                    GUI.color = new Color(0.38f, 0.42f, 0.52f);
                    GUI.Label(new Rect(4f, y + 2f, cw, LblH), "Nothing equipped.", _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }

                y += 6f;

                // ── Carried section ────────────────────────────────
                DrawSolid(new Rect(0, y, cw, SmH + 4f), new Color(0.10f, 0.12f, 0.22f, 1f));
                GUI.color = ColAccent;
                GUI.Label(new Rect(4f, y + 2f, cw, SmH), "Carried", _sLabel);
                GUI.color = prevC;
                y += SmH + 6f;

                bool anyCarried = npc.pocketItems != null && npc.pocketItems.Count > 0;
                if (anyCarried)
                {
                    foreach (var kv in npc.pocketItems)
                    {
                        string itemId   = kv.Key;
                        int    qty      = kv.Value;
                        string itemName = _gm.Registry.Items.TryGetValue(itemId, out var def2)
                                        ? def2.displayName : itemId;
                        string qtyStr   = qty > 1 ? $"×{qty}" : "";
                        float  nameW    = cw - CharW * 5f;
                        GUI.Label(new Rect(4f,      y + 2f, nameW,      LblH), itemName, _sSub);
                        GUI.color = ColTextBase;
                        GUI.Label(new Rect(cw - CharW * 4f, y + 2f, CharW * 4f, LblH), qtyStr, _sSub);
                        GUI.color = prevC;
                        y += LineH;
                    }
                }
                else
                {
                    GUI.color = new Color(0.38f, 0.42f, 0.52f);
                    GUI.Label(new Rect(4f, y + 2f, cw, LblH), "Pockets empty.", _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }

                y += 4f;
            }

            GUI.EndGroup();
            _crewDetailOverlayH = bodyStartY + y + 12f;
        }

        /// <summary>
        /// Draws a single-section mood bar: label row (threshold + score/work-mod) plus fill bar.
        /// Fixed height: 22px (14 label + 8 bar). No dynamic modifier list.
        /// </summary>
        private void DrawMoodBar(NPCInstance npc, float w, float y)
        {
            float  score    = npc.moodScore;
            string thresh   = MoodSystem.GetThresholdLabel(score);
            Color  barColor = MoodSystem.GetMoodColor(score);
            float  bw       = w - 14f;

            // Crisis flash overlay
            if (npc.inCrisis)
            {
                var cp = GUI.color; GUI.color = new Color(0.86f, 0.26f, 0.26f, 0.20f);
                DrawSolid(new Rect(0, y - 2f, bw, 22f), GUI.color);
                GUI.color = cp;
            }

            // Label row: "Mood" | threshold (colored) | score% OR work modifier (right)
            GUI.Label(new Rect(0, y, 38f, 16f), "Mood", _sSub);
            var prev = GUI.color; GUI.color = barColor;
            GUI.Label(new Rect(40f, y, 72f, 16f), npc.inCrisis ? "\u26a0 Crisis" : thresh, _sSub);
            GUI.color = prev;
            if (System.Math.Abs(npc.workModifier - 1.0f) > 0.005f)
            {
                string wm = npc.workModifier > 1f
                    ? $"+{(npc.workModifier - 1f) * 100f:F0}% work"
                    : $"-{(1f - npc.workModifier) * 100f:F0}% work";
                var wmPrev = GUI.color;
                GUI.color = npc.workModifier > 1f ? ColBarGreen : ColBarWarn;
                GUI.Label(new Rect(bw - 60f, y, 60f, 16f), wm, _sSub);
                GUI.color = wmPrev;
            }
            else
            {
                GUI.Label(new Rect(bw - 30f, y, 30f, 16f), $"{score:F0}%", _sSub);
            }

            // Fill bar
            DrawSolid(new Rect(0, y + LineH, bw, 8f), ColBarBg);
            DrawSolid(new Rect(0, y + LineH, bw * (score / 100f), 8f), barColor);
        }

        // ── Work Assignment grid ──────────────────────────────────────────────
        private void DrawCrewWork(Rect area, float w, float h)
        {
            var s    = _gm.Station;
            var crew = s.GetCrew();

            float HdrH  = WrkHdrH;
            float RowH  = WrkRowH;
            const float CellW = 22f;   // compact toggle-cell width
            float NameW = CharW * 12f;
            float colW  = (w - NameW - 14f) / WorkJobCols.Length;

            // Fixed header: Name column label + col abbreviations + per-column bulk toggles
            DrawSolid(new Rect(area.x, area.y, w, HdrH), new Color(0.08f, 0.10f, 0.18f, 1f));
            var prevC = GUI.color;
            GUI.color = new Color(0.42f, 0.46f, 0.56f);
            GUI.Label(new Rect(area.x + 2f, area.y + 2f, NameW - 4f, SmH), "Name", _sSub);
            GUI.color = prevC;

            float hx = area.x + NameW;
            for (int ci = 0; ci < WorkJobCols.Length; ci++)
            {
                float cx = hx + ci * colW + (colW - CellW) * 0.5f;
                GUI.Label(new Rect(cx, area.y + 2f, CellW, SmH), WorkJobCols[ci].label, _sSub);

                // Bulk column toggle in header
                bool colAllOn = crew.Count == 0 || crew.All(n =>
                {
                    var al = s.workAssignments.TryGetValue(n.uid, out var v) ? v : null;
                    return al == null || al.Count == 0 || al.Contains(WorkJobCols[ci].id);
                });
                GUI.color = colAllOn ? ColBarGreen : new Color(0.28f, 0.30f, 0.38f);
                if (GUI.Button(new Rect(cx, area.y + 20f, CellW, 20f), colAllOn ? "✓" : "—", _sBtnSmall))
                {
                    foreach (var npc2 in crew)
                    {
                        if (!s.workAssignments.ContainsKey(npc2.uid))
                            s.workAssignments[npc2.uid] = new List<string>();
                        var al2     = s.workAssignments[npc2.uid];
                        string jid2 = WorkJobCols[ci].id;
                        if (colAllOn)
                        {
                            if (al2.Count == 0)
                                foreach (var (id3, _) in WorkJobCols)
                                    if (id3 != jid2) al2.Add(id3);
                            else
                                al2.Remove(jid2);
                        }
                        else
                        {
                            if (!al2.Contains(jid2)) al2.Add(jid2);
                            bool allOk = WorkJobCols.All(j => al2.Contains(j.id));
                            if (allOk) al2.Clear();
                        }
                    }
                }
                GUI.color = prevC;
            }

            float listTop = area.y + HdrH;
            float innerH  = Mathf.Max(h - HdrH, (crew.Count + 1) * RowH + 4f);
            _workScroll = GUI.BeginScrollView(
                new Rect(area.x, listTop, w, h - HdrH),
                _workScroll, new Rect(0, 0, w - 14f, innerH));

            float y = 0f;

            // ── "All" bulk-toggle row (top of scroll) ────────────────────────
            DrawSolid(new Rect(0, y, w - 14f, RowH), new Color(0.10f, 0.12f, 0.20f, 1f));
            GUI.color = new Color(0.55f, 0.60f, 0.72f);
            GUI.Label(new Rect(2f, y + (RowH - SmH) * 0.5f, NameW - 4f, SmH), "All", _sSub);
            GUI.color = prevC;
            for (int ci = 0; ci < WorkJobCols.Length; ci++)
            {
                float cx   = NameW + ci * colW + (colW - CellW) * 0.5f;
                string jid = WorkJobCols[ci].id;
                bool allOn = crew.Count == 0 || crew.All(n =>
                {
                    var al = s.workAssignments.TryGetValue(n.uid, out var v) ? v : null;
                    return al == null || al.Count == 0 || al.Contains(jid);
                });
                GUI.color = allOn ? ColBarGreen : new Color(0.28f, 0.30f, 0.38f);
                if (GUI.Button(new Rect(cx, y + 3f, CellW, RowH - 6f), allOn ? "✓" : "—", _sBtnSmall))
                {
                    foreach (var npc2 in crew)
                    {
                        if (!s.workAssignments.ContainsKey(npc2.uid))
                            s.workAssignments[npc2.uid] = new List<string>();
                        var al2 = s.workAssignments[npc2.uid];
                        if (allOn)
                        {
                            if (al2.Count == 0)
                                foreach (var (id3, _) in WorkJobCols)
                                    if (id3 != jid) al2.Add(id3);
                            else
                                al2.Remove(jid);
                        }
                        else
                        {
                            if (!al2.Contains(jid)) al2.Add(jid);
                            bool allOk = WorkJobCols.All(j => al2.Contains(j.id));
                            if (allOk) al2.Clear();
                        }
                    }
                }
                GUI.color = prevC;
            }
            DrawSolid(new Rect(0, y + RowH - 1f, w - 14f, 1f), ColDivider);
            y += RowH;

            // ── Per-crew rows ─────────────────────────────────────────────────
            foreach (var npc in crew)
            {
                if (!s.workAssignments.ContainsKey(npc.uid))
                    s.workAssignments[npc.uid] = new List<string>();
                var allowed = s.workAssignments[npc.uid];

                bool rowHover = new Rect(0, y, w - 14f, RowH).Contains(Event.current.mousePosition);
                if (rowHover) DrawSolid(new Rect(0, y, w - 14f, RowH), new Color(0.10f, 0.13f, 0.22f));

                GUI.Label(new Rect(2f, y + (RowH - SmH) * 0.5f, NameW - 4f, SmH),
                          npc.name.Length > 11 ? npc.name[..11] : npc.name, _sSub);

                for (int ci = 0; ci < WorkJobCols.Length; ci++)
                {
                    string jid     = WorkJobCols[ci].id;
                    bool   enabled = allowed.Count == 0 || allowed.Contains(jid);
                    float  cx      = NameW + ci * colW + (colW - CellW) * 0.5f;
                    Rect   cell    = new Rect(cx, y + 3f, CellW, RowH - 6f);   // ~22×20 px

                    GUI.color = enabled ? ColBarGreen : new Color(0.25f, 0.28f, 0.38f, 1f);
                    if (GUI.Button(cell, enabled ? "✓" : "—", _sBtnSmall))
                    {
                        if (allowed.Count == 0)
                            foreach (var (id2, _) in WorkJobCols)
                                if (id2 != jid) allowed.Add(id2);
                        else if (allowed.Contains(jid))
                            allowed.Remove(jid);
                        else
                        {
                            allowed.Add(jid);
                            if (WorkJobCols.All(j => allowed.Contains(j.id))) allowed.Clear();
                        }
                    }
                    GUI.color = prevC;
                }

                DrawSolid(new Rect(0, y + RowH - 1f, w - 14f, 1f), ColDivider);
                y += RowH;
            }
            if (crew.Count == 0)
            {
                GUI.color = new Color(0.40f, 0.44f, 0.54f);
                GUI.Label(new Rect(0, 4f, w - 14f, 20f), "No crew to assign.", _sSub);
                GUI.color = prevC;
            }
            GUI.EndScrollView();
        }

        private static float GetNeed(NPCInstance npc, string key)
            => npc.needs.TryGetValue(key, out float v) ? v : 1f;

        // ── Ranks sub-panel ───────────────────────────────────────────────────
        private string[] _rankEditBuffers = null;

        private void DrawRanks(Rect area, float w, float h)
        {
            var s = _gm.Station;

            if (_rankEditBuffers == null || _rankEditBuffers.Length != s.rankNames.Count)
                _rankEditBuffers = s.rankNames.ToArray();

            static string Stars(int n) => n == 0 ? "○" : new string('\u2605', n);

            float y    = area.y + 4f;
            bool  dirty = false;
            for (int i = 0; i < s.rankNames.Count; i++)
            {
                DrawSolid(new Rect(area.x, y, w, 36f), new Color(0.10f, 0.12f, 0.20f, 0.85f));

                // Star badge — amber for ranked, muted for unranked
                Color sc = i == 0 ? ColTextMid
                                  : new Color(0.78f, 0.69f, 0.18f);
                Color prev = GUI.color; GUI.color = sc;
                GUI.Label(new Rect(area.x + 4f, y + 9f, 32f, 18f),
                          Stars(i), new GUIStyle(_sSub) { fontSize = FontSizeHdr - 4 });
                GUI.color = prev;

                bool isEdited = (_rankEditBuffers[i] ?? "") != (s.rankNames[i] ?? "");
                // Left-border accent when the name has been edited but not yet saved
                if (isEdited)
                    DrawSolid(new Rect(area.x, y, 2f, 36f), new Color(0.28f, 0.53f, 0.67f));
                GUI.color = isEdited ? new Color(0.88f, 0.66f, 0.18f) : Color.white;
                _rankEditBuffers[i] = GUI.TextField(
                    new Rect(area.x + 38f, y + 8f, w - 44f, 20f),
                    _rankEditBuffers[i] ?? s.rankNames[i], 30, _sTextField);
                GUI.color = prev;
                if (isEdited) dirty = true;
                y += 40f;
            }

            if (dirty)
            {
                GUI.color = new Color(0.88f, 0.66f, 0.18f);
                if (GUI.Button(new Rect(area.x + w - 120f, y, 116f, 22f), "Save Rank Names", _sBtnSmall))
                    for (int i = 0; i < s.rankNames.Count; i++)
                    {
                        string t = (_rankEditBuffers[i] ?? "").Trim();
                        if (t.Length > 0) s.rankNames[i] = t;
                        else _rankEditBuffers[i] = s.rankNames[i];
                    }
                GUI.color = Color.white;
                y += 28f;
            }

            // ── Divider ───────────────────────────────────────────────────────
            y += 6f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 10f;

            // ── Roster bar chart ──────────────────────────────────────────────
            GUI.Label(new Rect(area.x + 4f, y, w - 8f, LineH), "Roster by rank", _sLabel);
            y += LineH + 4f;

            int[] counts = new int[s.rankNames.Count];
            foreach (var npc in s.npcs.Values)
                if (npc.IsCrew() && npc.rank >= 0 && npc.rank < counts.Length)
                    counts[npc.rank]++;
            int totalCrew = 0;
            foreach (int c in counts) totalCrew += c;

            float barMaxW = w - 160f;
            var   rprev   = GUI.color;
            for (int i = 0; i < s.rankNames.Count; i++)
            {
                Color sc2 = i == 0 ? ColTextMid : new Color(0.78f, 0.69f, 0.18f);
                GUI.color = sc2;
                GUI.Label(new Rect(area.x + 4f, y + 1f, 28f, LblH),
                          Stars(i), new GUIStyle(_sSub) { fontSize = FontSize });
                GUI.color = rprev;
                GUI.Label(new Rect(area.x + 34f, y + 1f, 90f, 16f), s.rankNames[i], _sSub);
                float fillW = totalCrew > 0 ? barMaxW * (counts[i] / (float)totalCrew) : 0f;
                DrawSolid(new Rect(area.x + 130f, y + 4f, barMaxW, 8f), ColBarBg);
                if (fillW > 0f) DrawSolid(new Rect(area.x + 130f, y + 4f, fillW, 8f), sc2);
                GUI.Label(new Rect(area.x + 132f + barMaxW, y + 1f, 24f, 16f),
                          counts[i].ToString(), _sSub);
                y += 20f;
            }
        }

        private void NeedBar(string label, float value, float w, float y)
        {
            float lw = w * 0.30f, bx = w * 0.32f, bw = w * 0.52f;
            GUI.Label(new Rect(0, y, lw, LineH), label, _sSub);
            DrawSolid(new Rect(bx, y + LineH * 0.25f, bw, 8f), ColBarBg);
            Color fc = value > 0.5f ? ColBarGreen : value > 0.25f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(bx, y + LineH * 0.25f, bw * value, 8f), fc);
            GUI.Label(new Rect(bx + bw + 4f, y, 30f, LineH),
                      Mathf.RoundToInt(value * 100f) + "%", _sSub);
        }

        // ── Social / Relationships sub-panel ──────────────────────────────────
        private void DrawRelationships(Rect area, float w, float h)
        {
            var s = _gm.Station;

            // ── Pending marriage events ───────────────────────────────────────
            if (s.pendingMarriageEvents.Count > 0)
            {
                float my = area.y + 4f;
                DrawSolid(new Rect(area.x, my - 2f, w, 2f), new Color(0.86f, 0.26f, 0.26f));
                GUI.Label(new Rect(area.x + 4f, my, w - 8f, LineH),
                          "♥ Marriage Proposals", _sHeader);
                my += LineH + 4f;

                var toRemove = new List<string>();
                foreach (var key in new List<string>(s.pendingMarriageEvents))
                {
                    if (!s.relationships.TryGetValue(key, out var rec)) { toRemove.Add(key); continue; }
                    string n1 = s.npcs.TryGetValue(rec.npcUid1, out var npc1) ? npc1.name : rec.npcUid1;
                    string n2 = s.npcs.TryGetValue(rec.npcUid2, out var npc2) ? npc2.name : rec.npcUid2;
                    GUI.Label(new Rect(area.x + 2f, my, w - 144f, LineH),
                              $"{n1} & {n2}", _sLabel);
                    if (GUI.Button(new Rect(area.x + w - 140f, my, 72f, BtnH - 4f),
                                   "Allow", _sBtnSmall))
                    {
                        RelationshipRegistry.ApproveMarriage(
                            s, rec.npcUid1, rec.npcUid2, _gm.Mood, s.tick);
                        toRemove.Add(key);
                    }
                    if (GUI.Button(new Rect(area.x + w - 64f, my, 62f, BtnH - 4f),
                                   "Deny", _sBtnSmall))
                    {
                        RelationshipRegistry.DismissMarriage(s, rec.npcUid1, rec.npcUid2);
                        toRemove.Add(key);
                    }
                    my += LineH + 6f;
                }
                foreach (var k in toRemove) s.pendingMarriageEvents.Remove(k);

                DrawSolid(new Rect(area.x, my, w, 1f), ColDivider);
                area = new Rect(area.x, my + 6f, w, h - (my - area.y) - 8f);
                h    = area.height;
            }

            // ── All relationship pairs (grouped by sentiment) ─────────────────
            var allies  = new List<RelationshipRecord>();
            var neutral = new List<RelationshipRecord>();
            var rivals  = new List<RelationshipRecord>();
            foreach (var rec in s.relationships.Values)
            {
                if (!s.npcs.ContainsKey(rec.npcUid1) || !s.npcs.ContainsKey(rec.npcUid2)) continue;
                if (rec.relationshipType == RelationshipType.None && rec.affinityScore == 0f) continue;
                if (rec.affinityScore >= 20f ||
                    rec.relationshipType == RelationshipType.Friend ||
                    rec.relationshipType == RelationshipType.Lover  ||
                    rec.relationshipType == RelationshipType.Spouse)
                    allies.Add(rec);
                else if (rec.affinityScore <= -20f ||
                         rec.relationshipType == RelationshipType.Enemy)
                    rivals.Add(rec);
                else
                    neutral.Add(rec);
            }

            const float RelRowH = 28f;
            float relInnerH = Mathf.Max(h,
                (allies.Count + rivals.Count + neutral.Count) * RelRowH +
                (allies.Count  > 0 ? 22f : 0f) +
                (neutral.Count > 0 ? 22f : 0f) +
                (rivals.Count  > 0 ? 22f : 0f) + 8f);
            _relScroll = GUI.BeginScrollView(
                new Rect(area.x, area.y, w, h),
                _relScroll, new Rect(0, 0, w - 14f, relInnerH));

            float y = 0f;

            void RelSection(string title, List<RelationshipRecord> list, Color headerCol)
            {
                if (list.Count == 0) return;
                DrawSolid(new Rect(0, y, w - 14f, 20f),
                          new Color(headerCol.r * 0.20f, headerCol.g * 0.20f, headerCol.b * 0.20f, 1f));
                var pc0 = GUI.color; GUI.color = headerCol;
                GUI.Label(new Rect(4f, y + 2f, w - 18f, 16f), title, _sLabel);
                GUI.color = pc0;
                y += 22f;

                foreach (var rec in list)
                {
                    if (!s.npcs.TryGetValue(rec.npcUid1, out var a) ||
                        !s.npcs.TryGetValue(rec.npcUid2, out var b)) continue;

                    string typeLabel = rec.relationshipType.ToString();
                    Color  typeColor = rec.relationshipType switch
                    {
                        RelationshipType.Friend => ColBarGreen,
                        RelationshipType.Lover  => new Color(1f, 0.45f, 0.72f),
                        RelationshipType.Spouse => new Color(1f, 0.62f, 0.90f),
                        RelationshipType.Enemy  => ColBarCrit,
                        _                       => ColAccent
                    };

                    GUI.Label(new Rect(4f, y + 4f, w * 0.40f - 14f, 20f),
                              $"{a.name} & {b.name}", _sSub);
                    var pc2 = GUI.color; GUI.color = typeColor;
                    GUI.Label(new Rect(w * 0.40f, y + 4f, 58f, 20f), typeLabel, _sSub);
                    GUI.color = pc2;

                    // Affinity bar: centre = 0, left = negative, right = positive
                    float bx   = w * 0.40f + 60f;
                    float bfw  = w - bx - 38f - 14f;
                    float mid  = bx + bfw * 0.5f;
                    DrawSolid(new Rect(bx, y + 10f, bfw, 6f), ColBarBg);
                    Color barCol = rec.affinityScore >= 0f ? ColBarGreen : ColBarCrit;
                    if (rec.affinityScore >= 0f)
                        DrawSolid(new Rect(mid, y + 10f,
                                          bfw * 0.5f * (rec.affinityScore / 100f), 6f), barCol);
                    else
                        DrawSolid(new Rect(mid + bfw * 0.5f * (rec.affinityScore / 100f), y + 10f,
                                          bfw * 0.5f * (-rec.affinityScore / 100f), 6f), barCol);
                    DrawSolid(new Rect(mid - 1f, y + 8f, 2f, 10f), new Color(1f, 1f, 1f, 0.15f));

                    GUI.color = typeColor;
                    GUI.Label(new Rect(w - 36f - 14f, y + 4f, 34f, 20f),
                              $"{rec.affinityScore:+0;-0;0}", _sSub);
                    GUI.color = pc2;

                    y += RelRowH;
                }
            }

            RelSection("Allies",  allies,  ColBarGreen);
            RelSection("Neutral", neutral, ColTextBase);
            RelSection("Rivals",  rivals,  ColBarCrit);

            if (allies.Count == 0 && neutral.Count == 0 && rivals.Count == 0)
            {
                GUI.color = new Color(0.40f, 0.44f, 0.54f);
                GUI.Label(new Rect(0, 4f, w - 14f, 20f), "No notable relationships yet.", _sSub);
                GUI.color = Color.white;
            }

            GUI.EndScrollView();
        }

        // ── Comms tab ─────────────────────────────────────────────────────────
        private void DrawComms(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s = _gm.Station;

            // ── Tab selector (4 tabs) ─────────────────────────────────────────
            const float TabH = 22f;
            float tbw = Mathf.Floor(w / 4f);
            (string lbl, CommsTab ct, bool clearMsg)[] cTabs =
            {
                ("UNREAD", CommsTab.Unread, true),
                ("READ",   CommsTab.Read,   true),
                ("ALL",    CommsTab.All,    true),
                ("SHIPS",  CommsTab.Ships,  false),
            };
            DrawSolid(new Rect(area.x, area.y, w, TabH), ColBar);
            DrawSolid(new Rect(area.x, area.y + TabH - 1f, w, 1f), ColDivider);
            for (int ci = 0; ci < cTabs.Length; ci++)
            {
                float ctx = area.x + ci * tbw;
                bool  con = _commsTab == cTabs[ci].ct;
                DrawSolid(new Rect(ctx, area.y, tbw - 1f, TabH - 1f), con ? ColTabHl : ColBar);
                if (con) DrawSolid(new Rect(ctx, area.y + TabH - 3f, tbw - 1f, 2f), ColAccent);
                if (ci > 0) DrawSolid(new Rect(ctx, area.y, 1f, TabH - 1f), ColDivider);
                GUI.color = con ? ColTextHead : ColTextBase;
                if (GUI.Button(new Rect(ctx, area.y, tbw - 1f, TabH - 1f),
                               cTabs[ci].lbl, con ? _sTabOn : _sTabOff))
                {
                    _commsTab = cTabs[ci].ct;
                    if (cTabs[ci].clearMsg) _selectedMsgUid = "";
                }
                GUI.color = Color.white;
            }

            if (_commsTab == CommsTab.Ships)
            {
                DrawCommsShips(area, w, h, TabH, s);
                return;
            }

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
            float ListRowH = LineH * 2f + 8f;
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
                GUI.Label(new Rect(10f, ly + 3f, ListW - 24f, LineH), subj, _sSub);

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
                GUI.Label(new Rect(10f, ly + 3f + LineH, ListW - 24f, LineH), rowSub, _sSub);
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
                float bodyInner = Mathf.Max(bodyH,
                    _sDescr.CalcHeight(new GUIContent(sel_msg.body), dw - 14f)) + 8f;

                _commsBodyScroll = GUI.BeginScrollView(
                    new Rect(detX + 4f, dy, dw, bodyH),
                    _commsBodyScroll, new Rect(0, 0, dw - 14f, bodyInner));
                GUI.Label(new Rect(0, 0, dw - 14f, bodyInner), sel_msg.body, _sDescr);
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

        // ── Ships sub-tab ─────────────────────────────────────────────────────
        private void DrawCommsShips(Rect area, float w, float h, float tabH, StationState s)
        {
            float Pad   = 6f;
            float y     = area.y + tabH + Pad;
            float dw    = w - Pad * 2f;

            // Antenna status header
            bool hasAntenna = _gm?.Antenna?.HasPoweredAntenna(s) ?? false;
            if (!hasAntenna)
            {
                var prev = GUI.color;
                GUI.color = new Color(0.8f, 0.5f, 0.2f, 1f);
                GUI.Label(new Rect(area.x + Pad, y, dw, 20f),
                          "No Antenna installed — place and power an Antenna Array.", _sSub);
                GUI.color = prev;
                return;
            }

            var (range, maxShips) = _gm.Antenna.GetAntennaStats(s);
            GUI.Label(new Rect(area.x + Pad, y, dw, 16f),
                      $"Antenna: range {range:F0} u  |  cap {maxShips} ships", _sSub);
            y += 18f;

            bool hasHangar = s.HasFunctionalHangar();
            if (!hasHangar)
            {
                var prev2 = GUI.color;
                GUI.color = new Color(0.8f, 0.5f, 0.2f, 1f);
                GUI.Label(new Rect(area.x + Pad, y, dw, 16f),
                          "⚠ No functional Hangar (place Shuttle Landing Pad).", _sSub);
                GUI.color = prev2;
            }
            y += 20f;

            // Toast notification for hail results
            if (_hailToastTimer > 0f)
            {
                DrawSolid(new Rect(area.x + Pad, y, dw, 22f),
                          new Color(0.1f, 0.25f, 0.15f, 0.95f));
                GUI.Label(new Rect(area.x + Pad + 4f, y + 3f, dw - 8f, 16f),
                          _hailToastMsg, _sSub);
                y += 26f;
            }

            // Ship list
            var ships = s.GetInRangeShips();
            if (ships.Count == 0)
            {
                GUI.Label(new Rect(area.x + Pad, y, dw, 20f), "No ships in range.", _sSub);
                return;
            }

            const float RowH = 40f;
            float listH  = h - (y - area.y) - Pad;
            float innerH = Mathf.Max(listH, ships.Count * RowH);

            _commsShipListScroll = GUI.BeginScrollView(
                new Rect(area.x, y, w, listH),
                _commsShipListScroll,
                new Rect(0, 0, w - 14f, innerH));

            float ly = 0f;
            foreach (var ship in ships)
            {
                DrawSolid(new Rect(0, ly, w - 14f, RowH - 3f),
                          new Color(0.09f, 0.10f, 0.16f, 0.9f));

                // Ship name
                GUI.Label(new Rect(Pad, ly + 3f, dw * 0.55f, LineH), ship.name, _sSub);

                // Status label
                string statusTxt = ship.VisitStateLabel();
                GUI.Label(new Rect(dw * 0.55f + Pad, ly + 3f, dw * 0.25f, LineH), statusTxt, _sSub);

                // Call button
                float btnX  = dw * 0.80f + Pad;
                float btnW  = dw * 0.19f;
                bool  onCooldown   = s.IsHailOnCooldown(ship.uid);
                bool  alreadyBusy  = ship.visitState == ShipVisitState.Inbound   ||
                                     ship.visitState == ShipVisitState.Docked     ||
                                     ship.visitState == ShipVisitState.Departing;
                bool  btnEnabled   = hasHangar && !onCooldown && !alreadyBusy;

                string btnLabel;
                if (alreadyBusy)
                    btnLabel = statusTxt;
                else if (onCooldown)
                    btnLabel = $"Wait {s.HailCooldownRemaining(ship.uid)} ticks";
                else if (!hasHangar)
                    btnLabel = "No Hangar";
                else
                    btnLabel = "Call";

                if (onCooldown || alreadyBusy || !hasHangar)
                {
                    var prev3 = GUI.color;
                    GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                    GUI.Label(new Rect(btnX, ly + 12f, btnW, 16f), btnLabel, _sSub);
                    GUI.color = prev3;
                }
                else if (GUI.Button(new Rect(btnX, ly + 10f, btnW, 20f), btnLabel, _sBtnWide))
                {
                    string result = _gm.TryHailShip(ship.uid);
                    _hailToastMsg   = result;
                    _hailToastTimer = 4f;
                }

                DrawSolid(new Rect(0, ly + RowH - 4f, w - 14f, 1f), ColDivider);
                ly += RowH;
            }
            GUI.EndScrollView();
        }


        private void DrawStation(Rect area, float w, float h)
        {
            var s    = _gm.Station;
            var prev = GUI.color;

            // ═══ STATION NAME HEADER (40px) ═════════════════════════════════
            const float HdrH  = 32f;
            const float TabH2 = 22f;
            DrawSolid(new Rect(area.x, area.y, w, HdrH), new Color(0.12f, 0.15f, 0.22f, 1f));

            // [<] cycle arrow — disabled (single station)
            GUI.color = new Color(1f, 1f, 1f, 0.20f);
            GUI.Button(new Rect(area.x + 4f, area.y + 8f, 22f, 24f), "◀", _sBtnSmall);
            GUI.color = prev;

            // Station name (centred, editable on double-click)
            Rect nameRect = new Rect(area.x + 28f, area.y + 2f, w - 56f, 36f);
            if (_stationNameEditing)
            {
                GUI.SetNextControlName("StationNameField");
                string edited = GUI.TextField(nameRect, _stationNameBuffer, 40, _sTextField);
                _stationNameBuffer = edited;
                if (Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.Escape))
                {
                    if (Event.current.keyCode == KeyCode.Return && _stationNameBuffer.Trim().Length > 0)
                        s.stationName = _stationNameBuffer.Trim();
                    _stationNameEditing = false;
                }
                // Click outside → commit
                if (Event.current.type == EventType.MouseDown && !nameRect.Contains(Event.current.mousePosition))
                {
                    if (_stationNameBuffer.Trim().Length > 0) s.stationName = _stationNameBuffer.Trim();
                    _stationNameEditing = false;
                }
            }
            else
            {
                string displayName = s.stationName is { Length: > 0 } sn ? sn : "Unnamed Station";
                GUI.Label(nameRect, displayName,
                          new GUIStyle(_sLabel) { alignment = TextAnchor.MiddleCenter });
                // Double-click to edit
                if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2
                    && nameRect.Contains(Event.current.mousePosition))
                {
                    _stationNameEditing = true;
                    _stationNameBuffer  = s.stationName ?? "";
                }
            }

            // [>] cycle arrow — disabled
            GUI.color = new Color(1f, 1f, 1f, 0.20f);
            GUI.Button(new Rect(area.x + w - 26f, area.y + 8f, 22f, 24f), "▶", _sBtnSmall);
            GUI.color = prev;

            // ═══ SUB-TAB BAR (28px) ═════════════════════════════════════════
            float tabBarY = area.y + HdrH;
            DrawSolid(new Rect(area.x, tabBarY, w, TabH2), new Color(0.09f, 0.11f, 0.18f, 1f));
            DrawSolid(new Rect(area.x, tabBarY + TabH2 - 1f, w, 1f), ColDivider);

            (StationSubTab tab, string lbl)[] stTabs =
            {
                (StationSubTab.Overview, "OVERVIEW"),
                (StationSubTab.Build,    "BUILD"),
                (StationSubTab.Rooms,    "ROOMS"),
                (StationSubTab.Crew,     "CREW"),
                (StationSubTab.Comms,    HasUnreadComms(s) ? "COMMS \u25cf" : "COMMS"),
            };
            float stTabW = Mathf.Floor(w / stTabs.Length);
            for (int ti = 0; ti < stTabs.Length; ti++)
            {
                float tx2 = area.x + ti * stTabW;
                bool  on  = _stationSub == stTabs[ti].tab;
                if (on)
                {
                    DrawSolid(new Rect(tx2, tabBarY, stTabW, TabH2 - 1f),
                              new Color(0.12f, 0.18f, 0.30f, 1f));
                    DrawSolid(new Rect(tx2, tabBarY + TabH2 - 3f, stTabW, 2f), ColAccent);
                }
                if (GUI.Button(new Rect(tx2, tabBarY, stTabW, TabH2 - 1f),
                               stTabs[ti].lbl, on ? _sTabOn : _sTabOff))
                {
                    SubPanel sp = stTabs[ti].tab switch
                    {
                        StationSubTab.Build  => SubPanel.StationBuild,
                        StationSubTab.Rooms  => SubPanel.StationRooms,
                        StationSubTab.Crew   => SubPanel.StationCrew,
                        StationSubTab.Comms  => SubPanel.StationComms,
                        _                    => SubPanel.None,
                    };
                    if (sp == SubPanel.None) // Overview — close any open station sub-drawer
                    {
                        _stationSub = StationSubTab.Overview;
                        if (_subActive == SubPanel.StationBuild || _subActive == SubPanel.StationRooms ||
                            _subActive == SubPanel.StationCrew  || _subActive == SubPanel.StationComms)
                            CloseSub();
                    }
                    else if (_subActive == sp) // toggle off
                    {
                        CloseSub();
                        _stationSub = StationSubTab.Overview;
                    }
                    else
                    {
                        _stationSub = stTabs[ti].tab;
                        OpenSub(sp, "");
                    }
                }
            }

            // Sync: if sub-drawer was closed externally (e.g. × button), revert visual selection
            {
                SubPanel expectedSub = _stationSub switch
                {
                    StationSubTab.Build  => SubPanel.StationBuild,
                    StationSubTab.Rooms  => SubPanel.StationRooms,
                    StationSubTab.Crew   => SubPanel.StationCrew,
                    StationSubTab.Comms  => SubPanel.StationComms,
                    _                    => SubPanel.None,
                };
                if (_stationSub != StationSubTab.Overview && _subActive != expectedSub)
                    _stationSub = StationSubTab.Overview;
            }

            // ═══ CONTENT AREA ════════════════════════════════════════════════
            float bodyY = tabBarY + TabH2;
            float bodyH = h - HdrH - TabH2;
            Rect  body  = new Rect(area.x, bodyY, w, bodyH);

            // Overview content is always shown; Build/Rooms/Crew/Comms render in sub-drawers
            DrawStationOverview(body, w, bodyH);
        }

        private bool HasUnreadComms(StationState s)
            => s != null && s.UnreadMessageCount() > 0;

        private void DrawStationOverview(Rect area, float w, float h)
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
            Section("WEALTH", w, ref y);
            float credits = s.GetResource("credits");
            ResourceBarC("CREDITS", credits, 5000f, w, ref y,
                         credits < CreditsWarnFloor ? ColBarCrit : ColCredits);
            Divider(w, ref y);

            // ── Production / Resources ────────────────────────────────────────
            Section("RESOURCES", w, ref y);
            ResourceBarC("FOOD",   s.GetResource("food"),   500f, w, ref y, ColResFood);
            ResourceBarC("POWER",  s.GetResource("power"),  500f, w, ref y, ColResPower);
            float oxygen = s.GetResource("oxygen");
            ResourceBarC("OXYGEN", oxygen, 500f, w, ref y,
                         oxygen < OxygenCritThreshold ? ColBarCrit : ColResOxygen);
            ResourceBarC("PARTS",  s.GetResource("parts"),  200f, w, ref y, ColResParts);
            ResourceBarC("ICE",    s.GetResource("ice"),    500f, w, ref y, ColResIce);

            // O₂ critical banner (blinks at 0.5 Hz)
            if (oxygen < OxygenCritThreshold)
            {
                float alpha = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * Mathf.PI));
                var prevCol = GUI.color;
                GUI.color = new Color(ColBarCrit.r, ColBarCrit.g, ColBarCrit.b, alpha);
                DrawSolid(new Rect(0, y, w, 20f), new Color(0.6f, 0f, 0f, 0.25f * alpha));
                GUI.Label(new Rect(0, y, w, 20f), "\u26a0 O\u2082 CRITICAL",
                          new GUIStyle(_sSub) { alignment = TextAnchor.MiddleCenter });
                GUI.color = prevCol;
                y += 24f;
            }

            Divider(w, ref y);

            // ── Station Inventory ─────────────────────────────────────────────
            Section($"INVENTORY  \u00b7  {capUsed:F0}/{capTotal}", w, ref y);

            // Overall capacity bar
            if (capTotal > 0)
            {
                float pct = Mathf.Clamp01(capUsed / capTotal);
                DrawSolid(new Rect(0, y + 5f, w, 6f), ColSubDrawer);        /* bg-deep track */
                Color fc = pct < 0.75f ? ColBarFill : pct < 0.90f ? ColBarWarn : ColBarCrit;
                DrawSolid(new Rect(0, y + 5f, w * pct, 6f), fc);
                GUI.Label(new Rect(w * 0.78f, y, w * 0.22f, 16f),
                          $"{pct * 100f:F0}%", _sSub);
                y += 18f;
            }

            // Search field + Config button
            float configBtnW = 70f;
            GUI.color = ColTextDim;
            GUI.Label(new Rect(0, y + 2f, 36f, 17f), "FIND:", _sSub);
            GUI.color = Color.white;
            _inventorySearch = GUI.TextField(new Rect(40f, y + 1f, w - 40f - configBtnW - 4f, 18f),
                                             _inventorySearch, _sTextField);
            // Styled Config button — draw bg-base fill + border before the label
            Rect cfgR = new Rect(w - configBtnW, y, configBtnW, 20f);
            DrawSolid(cfgR, ColBar);
            DrawSolid(new Rect(cfgR.x, cfgR.y, cfgR.width, 1f),           ColBarEdge);
            DrawSolid(new Rect(cfgR.x, cfgR.yMax - 1f, cfgR.width, 1f),   ColBarEdge);
            DrawSolid(new Rect(cfgR.x, cfgR.y, 1f, cfgR.height),          ColBarEdge);
            DrawSolid(new Rect(cfgR.xMax - 1f, cfgR.y, 1f, cfgR.height),  ColBarEdge);
            GUI.color = ColTextBase;
            if (GUI.Button(cfgR, "\u2699 CONFIG", _sBtnSmall))
                OpenSub(SubPanel.StationSettings, "");
            GUI.color = Color.white;
            y += 24f;

            string filter = _inventorySearch.Trim();

            if (holds.Count == 0)
            {
                GUI.Label(new Rect(0, y, w, 18f), "NO CARGO HOLDS BUILT.", _sSub);
                y += 22f;
            }

            // Per-hold rows
            foreach (var hold in holds)
            {
                DrawCargoHoldRow(hold, w, ref y, filter, s);
            }

            Divider(w, ref y);

            // ── Module Status ─────────────────────────────────────────────────
            Section("MODULES", w, ref y);
            foreach (var mod in s.modules.Values)
            {
                string status = !mod.active    ? "OFFLINE"
                              : mod.damage > 0f ? $"DMG {mod.damage:P0}"
                              : "OK";
                Color dotCol = !mod.active    ? ColBarCrit
                             : mod.damage > 0f ? ColBarWarn
                             : ColBarGreen;

                bool modSel = _subActive == SubPanel.ModuleDetail && _subItemUid == mod.uid;
                if (modSel)
                    DrawSolid(new Rect(0, y, 2f, 20f), ColAccent);

                // Coloured status dot
                var prevModCol = GUI.color;
                GUI.color = dotCol;
                GUI.Label(new Rect(4f, y, 14f, 20f), "\u25cf", _sSub);
                GUI.color = prevModCol;
                GUI.Label(new Rect(18f, y, w * 0.68f - 18f, 20f), mod.displayName, _sSub);

                GUI.Label(new Rect(w * 0.70f, y, w * 0.22f, 20f), status, _sSub);

                // ▶ jump to module
                if (GUI.Button(new Rect(w * 0.92f, y, w * 0.08f, 18f), "\u25b6", _sBtnSmall))
                    OpenSub(SubPanel.ModuleDetail, mod.uid);

                y += 24f;
            }

            GUI.EndScrollView();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Station → Build tab
        // ─────────────────────────────────────────────────────────────────────
        private Vector2 _stationBuildScroll;
        private Vector2 _stationBuildObjScroll;

        private void DrawStationBuild(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s       = _gm.Station;
            var catalog = _gm.Registry.Buildables;
            var prv     = GUI.color;

            // ── Top action bar (32px) ────────────────────────────────────────
            const float TbH = 32f;
            DrawSolid(new Rect(area.x, area.y, w, TbH), new Color(0.05f, 0.07f, 0.12f, 0.95f));

            GUI.color = _deconstructMode ? new Color(0.9f, 0.3f, 0.3f) : new Color(0.6f, 0.6f, 0.7f);
            if (GUI.Button(new Rect(area.x + 4f, area.y + 5f, 110f, 22f), "\u26CF Deconstruct", _sBtnSmall))
                _deconstructMode = !_deconstructMode;

            int queueCount = s.foundations.Count;
            GUI.color = queueCount > 0 ? new Color(0.4f, 0.7f, 0.9f) : new Color(0.5f, 0.55f, 0.6f);
            GUI.Button(new Rect(area.x + 120f, area.y + 5f, 110f, 22f),
                       $"\u2261 Queue ({queueCount})", _sBtnSmall);
            GUI.color = prv;

            float bodyY = area.y + TbH;
            float bodyH = h - TbH;

            // ── Category list (always visible, 26px rows) ────────────────────
            (string id, string icon, string label, Color accent)[] cats =
            {
                ("structure",  "\u25a1", "STRUCTURE",  new Color(0.784f, 0.627f, 0.188f, 1f)),
                ("electrical", "\u26a1", "ELECTRICAL", new Color(0.282f, 0.502f, 0.667f, 1f)),
                ("object",     "\u25c8", "OBJECTS",    new Color(0.188f, 0.659f, 0.471f, 1f)),
                ("production", "\u229b", "PRODUCTION", new Color(0.753f, 0.408f, 0.157f, 1f)),
                ("plumbing",   "\u2300", "PLUMBING",   new Color(0.314f, 0.533f, 0.722f, 1f)),
                ("security",   "\u25ce", "SECURITY",   new Color(0.753f, 0.220f, 0.345f, 1f)),
            };
            const float CatRowH = 26f;
            float catH = cats.Length * CatRowH;

            for (int ci = 0; ci < cats.Length; ci++)
            {
                float ry  = bodyY + ci * CatRowH;
                bool  sel = _stationBuildCat == cats[ci].id;
                Color ac  = cats[ci].accent;

                // Row background
                DrawSolid(new Rect(area.x, ry, w, CatRowH - 1f),
                          sel ? ColTabHl : ColBar);
                // Bottom separator
                DrawSolid(new Rect(area.x, ry + CatRowH - 1f, w, 1f), ColDivider);
                // Left accent stripe (3px, category colour, 60% opacity unselected / 100% selected)
                DrawSolid(new Rect(area.x, ry, 3f, CatRowH - 1f),
                          new Color(ac.r, ac.g, ac.b, sel ? 1f : 0.6f));
                // Bottom accent when selected
                if (sel) DrawSolid(new Rect(area.x + 3f, ry + CatRowH - 3f, w - 3f, 2f), ColAccent);
                // Icon (category colour)
                GUI.color = new Color(ac.r, ac.g, ac.b, sel ? 1f : 0.7f);
                GUI.Label(new Rect(area.x + 6f, ry, 20f, CatRowH - 1f),
                          cats[ci].icon, new GUIStyle(_sSub) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
                // Label
                GUI.color = sel ? ColTextHead : ColTextBright;
                GUI.Label(new Rect(area.x + 28f, ry, w - 36f, CatRowH - 1f),
                          cats[ci].label, new GUIStyle(_sSub) { alignment = TextAnchor.MiddleLeft, fontSize = FontSize });
                GUI.color = Color.white;
                // Transparent click target
                if (GUI.Button(new Rect(area.x, ry, w, CatRowH - 1f), "", GUIStyle.none))
                    _stationBuildCat = sel ? "" : cats[ci].id;
                DrawSolid(new Rect(area.x, ry + CatRowH - 1f, w, 1f), ColDivider);
            }

            // ── Object list (2-col grid, scrollable) ─────────────────────────
            float objAreaY = bodyY + catH + 4f;
            float objAreaH = area.y + h - objAreaY;
            if (objAreaH < 10f) return;

            Rect objArea = new Rect(area.x, objAreaY, w, objAreaH);

            // Filter catalogue to selected category
            var filtered = new List<BuildableDefinition>();
            foreach (var kv in catalog)
            {
                string cat = string.IsNullOrEmpty(kv.Value.category) ? "other" : kv.Value.category;
                if (!string.IsNullOrEmpty(_stationBuildCat) && cat != _stationBuildCat) continue;
                filtered.Add(kv.Value);
            }

            if (string.IsNullOrEmpty(_stationBuildCat))
            {
                GUI.Label(new Rect(area.x + 8f, objAreaY + 6f, w - 16f, 18f),
                          "Select a category above to browse items.", _sSub);
                return;
            }

            const int   Cols    = 2;
            const float TGap    = 3f;
            const float TH      = 28f;
            float cw2  = w - 16f;
            float tileW = (cw2 - TGap * (Cols - 1)) / Cols;

            int rows = (filtered.Count + Cols - 1) / Cols;
            float innerH = rows * (TH + 2f) + 8f;

            _stationBuildScroll = GUI.BeginScrollView(objArea, _stationBuildScroll,
                                  new Rect(0, 0, cw2, Mathf.Max(objAreaH, innerH)));
            _buildHoverItem = "";
            float ty = 4f;
            for (int i = 0; i < filtered.Count; i += Cols)
            {
                for (int c = 0; c < Cols && i + c < filtered.Count; c++)
                    DrawCatalogTile(filtered[i + c],
                        new Rect(c * (tileW + TGap), ty, tileW, TH),
                        s, objArea, _stationBuildScroll);
                ty += TH + 2f;
            }
            GUI.EndScrollView();

            // Hover tooltip (drawn outside scroll view)
            if (!string.IsNullOrEmpty(_buildHoverItem) &&
                _gm.Registry.Buildables.TryGetValue(_buildHoverItem, out var hDef))
                DrawCatalogTooltip(hDef, objArea, _stationBuildScroll, cw2, s);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Station → Rooms tab
        // ─────────────────────────────────────────────────────────────────────
        private Vector2 _stationRoomsScroll;

        private void DrawStationRooms(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var s   = _gm.Station;
            var prv = GUI.color;

            // Discover rooms (reuse same flood-fill logic as DrawBuildRooms)
            var discoveredRooms = new List<(string key, List<(int c, int r)> tiles)>();
            {
                var visited = new System.Collections.Generic.HashSet<(int, int)>();
                foreach (var f in s.foundations.Values)
                {
                    if (f.buildableId.Contains("floor") && !f.buildableId.Contains("wall")
                        && f.status == "complete")
                    {
                        var pos = (f.tileCol, f.tileRow);
                        if (visited.Contains(pos)) continue;
                        string rKey = FloodFillRoom(f.tileCol, f.tileRow, out var tiles);
                        if (rKey != null)
                        {
                            foreach (var t in tiles) visited.Add(t);
                            discoveredRooms.Add((rKey, tiles));
                        }
                    }
                }
            }

            // ── Bottom action bar (26px, fixed) ──────────────────────────────
            const float BtnBarH = 30f;
            float listH = h - BtnBarH;
            float bBarY = area.y + listH;
            DrawSolid(new Rect(area.x, bBarY, w, BtnBarH), new Color(0.05f, 0.07f, 0.12f, 0.95f));
            DrawSolid(new Rect(area.x, bBarY, w, 1f), ColDivider);
            float btnHalf = (w - 12f) * 0.5f;
            if (GUI.Button(new Rect(area.x + 4f,            bBarY + 4f, btnHalf, 22f),
                           "Assign Room", _sBtnSmall))
                OpenSub(SubPanel.StationSettings, "rooms_assign");
            if (GUI.Button(new Rect(area.x + 4f + btnHalf + 4f, bBarY + 4f, btnHalf, 22f),
                           "Config Rooms", _sBtnSmall))
                OpenSub(SubPanel.StationSettings, "rooms_config");

            // ── Room list (scrollable) ────────────────────────────────────────
            Rect listArea = new Rect(area.x, area.y, w, listH);
            const float RowH = 36f;
            float cw = w - 16f;
            float innerH = discoveredRooms.Count > 0 ? discoveredRooms.Count * RowH + 8f : 40f;

            _stationRoomsScroll = GUI.BeginScrollView(listArea, _stationRoomsScroll,
                                  new Rect(0, 0, cw, Mathf.Max(listH, innerH)));
            float y = 4f;

            if (discoveredRooms.Count == 0)
            {
                GUI.Label(new Rect(0, y, cw, 18f), "No rooms discovered yet.", _sSub);
                y += 22f;
            }

            string hoverKeyThisFrame = "";
            foreach (var (roomKey, tiles) in discoveredRooms)
            {
                // Room name
                string roomName = (s.customRoomNames.TryGetValue(roomKey, out var cn) && !string.IsNullOrEmpty(cn))
                                  ? cn
                                  : $"Room {roomKey.Split('_')[0]}";

                // Requirements status from roomBonusCache (keyed by roomKey)
                bool hasBonus = false;
                bool allMet   = true;
                if (s.roomBonusCache.TryGetValue(roomKey, out var roomBonusDot))
                {
                    hasBonus = roomBonusDot.workbenchRoomType != null;
                    if (!roomBonusDot.bonusActive) allMet = false;
                }
                Color reqDotCol = !hasBonus    ? new Color(0.4f, 0.4f, 0.5f)
                                : allMet        ? ColBarGreen
                                : new Color(0.85f, 0.55f, 0.15f);

                // Row background
                float rowX = 0f;
                DrawSolid(new Rect(rowX, y, cw, RowH - 2f), new Color(0.09f, 0.12f, 0.20f, 0.6f));

                // Room name
                GUI.Label(new Rect(rowX + 6f, y + 9f, cw * 0.60f, 18f), roomName, _sSub);

                // Pressurised dot (neutral — pressurized state not cached in model yet)
                var pDotPrev = GUI.color;
                GUI.color = new Color(0.4f, 0.4f, 0.5f, 0.85f);
                GUI.Label(new Rect(rowX + cw * 0.62f, y + 9f, 14f, 18f), "\u25cf", _sSub);
                GUI.color = pDotPrev;

                // Requirements dot
                pDotPrev = GUI.color;
                GUI.color = reqDotCol;
                GUI.Label(new Rect(rowX + cw * 0.72f, y + 9f, 14f, 18f), "\u25cf", _sSub);
                GUI.color = pDotPrev;

                // ▶ jump button
                if (GUI.Button(new Rect(rowX + cw - 26f, y + 7f, 22f, 22f), "\u25b6", _sBtnSmall))
                    _stationSub = StationSubTab.Rooms; // already on Rooms — could navigate map

                DrawSolid(new Rect(rowX, y + RowH - 2f, cw, 1f), ColDivider);

                // Hover tracking
                Rect rowRect = new Rect(rowX, y, cw, RowH - 2f);
                if (rowRect.Contains(Event.current.mousePosition))
                {
                    hoverKeyThisFrame = roomKey;
                    if (_stationRoomHoverKey != roomKey)
                        _stationRoomHoverTime = Time.realtimeSinceStartup;
                }

                y += RowH;
            }

            GUI.EndScrollView();

            // ── Hover popup (drawn on top, outside scroll) ───────────────────
            // Update hover tracking
            if (hoverKeyThisFrame != _stationRoomHoverKey)
            {
                _stationRoomHoverKey  = hoverKeyThisFrame;
                _stationRoomHoverTime = Time.realtimeSinceStartup;
            }

            bool showPopup = !string.IsNullOrEmpty(_stationRoomHoverKey)
                             && (Time.realtimeSinceStartup - _stationRoomHoverTime) >= 2.0f;
            if (showPopup && s.roomBonusCache.Count > 0)
            {
                // Collect requirements for this room (now keyed by roomKey)
                var reqLines = new System.Text.StringBuilder();
                if (s.roomBonusCache.TryGetValue(_stationRoomHoverKey, out var popupBs)
                    && popupBs.workbenchRoomType != null)
                {
                    string tname = popupBs.displayName ?? popupBs.workbenchRoomType;
                    reqLines.AppendLine(tname + (popupBs.bonusActive ? " \u2605" : ""));
                    foreach (var req in popupBs.requirements)
                        reqLines.AppendLine($"  {(req.IsMet ? "\u2713" : "\u2717")} {req.displayLabel}: {req.current}/{req.required}");
                }
                string popupText = reqLines.Length > 0 ? reqLines.ToString().TrimEnd() : "No room type assigned.";
                float popW = 200f;
                float lineCount = popupText.Split('\n').Length;
                float popH = lineCount * 16f + 12f;
                float popX = area.x + w - popW - 4f;
                float popY = area.y + 40f;
                DrawSolid(new Rect(popX, popY, popW, popH), new Color(0.07f, 0.09f, 0.14f, 0.97f));
                DrawSolid(new Rect(popX,          popY,           popW, 1f), ColDivider);
                DrawSolid(new Rect(popX,          popY + popH - 1f, popW, 1f), ColDivider);
                DrawSolid(new Rect(popX,          popY,           1f, popH), ColDivider);
                DrawSolid(new Rect(popX + popW - 1f, popY,        1f, popH), ColDivider);
                GUI.Label(new Rect(popX + 6f, popY + 6f, popW - 12f, popH - 12f),
                          popupText, _sSub);
            }
        }

        // ── Per-hold row ──────────────────────────────────────────────────────
        private void DrawCargoHoldRow(ModuleInstance hold, float w, ref float y,
                                      string filter, StationState s)
        {
            float usedW  = _gm.Inventory.GetCapacityUsed(hold);
            float totalW = _gm.Inventory.GetCapacityTotal(hold);
            bool  isSel  = _subActive == SubPanel.HoldSettings && _subItemUid == hold.uid;

            // ── Hold header line ──────────────────────────────────────────────
            GUI.Label(new Rect(0, y, w * 0.60f, 18f), hold.displayName, _sLabel);

            // Configure button -> opens sub-drawer
            string cfgLabel = isSel ? "▲ Open" : "⚙ Config";
            if (GUI.Button(new Rect(w * 0.62f, y, w * 0.38f, 17f), cfgLabel, _sBtnSmall))
            {
                if (isSel) CloseSub();
                else        OpenSub(SubPanel.HoldSettings, hold.uid);
            }
            y += 20f;

            // ── Capacity bar ──────────────────────────────────────────────────
            {
                string capLabel = totalW > 0 ? $"{usedW:F0} / {totalW} units" : "No capacity";
                GUI.Label(new Rect(0, y, w * 0.50f, LineH), capLabel, _sSub);

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
                GUI.Label(new Rect(0, y, w, LineH), badgeText, _sSub);
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
                    GUI.Label(new Rect(8f, y, w, LineH), "Empty", _sSub);
                    y += 16f;
                }
                else if (filter.Length > 0)
                {
                    GUI.Label(new Rect(8f, y, w, LineH), "No matching items.", _sSub);
                    y += 16f;
                }
            }

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
            ("greenhouse",    "Greenhouse",    new Color(0.30f, 0.72f, 0.30f, 0.50f)),
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
                "Use  Designer \u2192 Rooms  to manage room assignments and room type bonuses.", _sSub);
            GUI.color = Color.white;
            y += 38f;

            if (GUI.Button(new Rect(area.x + 4f, y, w - 16f, 24f), "Open Designer > Rooms", _sBtnSmall))
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
            float startY = DrawPanelChrome(w, h, "\u26a1", "DEV TOOLS", "DEVELOPER TOOLS",
                                           () => _devDrawerOpen = false, "DEV · RUNTIME TOOLS");

            float cw = w - Pad * 2f;
            float y  = startY;

            // Asset Editor button (debug-only entry point — no access restrictions)
            GUI.color = new Color(0.82f, 0.55f, 1.00f, 1f);
            if (GUI.Button(new Rect(Pad, y, cw, BtnH), "✦ Asset Editor", _sBtnWide))
            {
                Waystation.UI.AssetEditorController.Open();
                _devDrawerOpen = false;
            }
            GUI.color = Color.white;
            y += BtnH + 8f;

            DrawSolid(new Rect(Pad, y, cw, 1f), ColDivider); y += 14f;

            // Free Build toggle
            bool devMode = Waystation.Systems.BuildingSystem.DevMode;
            GUI.color = devMode
                ? new Color(1.00f, 0.78f, 0.20f, 1f)
                : new Color(0.55f, 0.60f, 0.70f, 0.95f);
            if (GUI.Button(new Rect(Pad, y, cw, BtnH),
                           devMode ? "⚡ Free Build  ON" : "⚡ Free Build  OFF", _sBtnWide))
                Waystation.Systems.BuildingSystem.DevMode = !devMode;
            GUI.color = Color.white;
            y += BtnH + 8f;

            // Telescope Mode toggle (bypass map layer research / equipment requirements)
            bool teleMode = Waystation.UI.SystemMapController.TelescopeMode;
            GUI.color = teleMode
                ? new Color(0.40f, 0.82f, 1.00f, 1f)
                : new Color(0.55f, 0.60f, 0.70f, 0.95f);
            if (GUI.Button(new Rect(Pad, y, cw, BtnH),
                           teleMode ? "🔭 Telescope Mode  ON" : "🔭 Telescope Mode  OFF", _sBtnWide))
                Waystation.UI.SystemMapController.TelescopeMode = !teleMode;
            GUI.color = Color.white;
            y += BtnH + 8f;

            DrawSolid(new Rect(Pad, y, cw, 1f), ColDivider); y += 14f;

            // Ships section
            GUI.Label(new Rect(Pad, y, cw, LineH), "Ships", _sLabel); y += 24f;
            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(Pad, y, cw, BtnH), "\u25b6 Call Trade Ship", _sBtnWide))
                    _gm.Visitors.SpawnTradeShip(_gm.Station);
                y += 34f;
            }

            DrawSolid(new Rect(Pad, y, cw, 1f), ColDivider); y += 14f;

            // Research section
            GUI.Label(new Rect(Pad, y, cw, 18f), "Research", _sLabel); y += 24f;
            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(Pad, y, cw, BtnH), "Unlock All Research", _sBtnWide))
                {
                    foreach (var kv in _gm.Registry.ResearchNodes)
                    {
                        var node   = kv.Value;
                        var bState = _gm.Station.research.branches[node.branch];
                        if (bState.unlockedNodeIds.Contains(node.id)) continue;
                        bState.unlockedNodeIds.Add(node.id);
                        foreach (var tag in node.unlockTags)
                            _gm.Station.SetTag(tag);
                    }
                }
                y += 34f;
            }

            DrawSolid(new Rect(Pad, y, cw, 1f), ColDivider); y += 14f;

            // Resources section
            GUI.Label(new Rect(Pad, y, cw, 18f), "Resources", _sLabel); y += 24f;
            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(Pad, y, cw, BtnH), "Fill Station Resources", _sBtnWide))
                {
                    _gm.Station.ModifyResource("food",    DevFillFood);
                    _gm.Station.ModifyResource("power",   DevFillPower);
                    _gm.Station.ModifyResource("oxygen",  DevFillOxygen);
                    _gm.Station.ModifyResource("parts",   DevFillParts);
                    _gm.Station.ModifyResource("ice",     DevFillIce);
                    _gm.Station.ModifyResource("credits", DevFillCredits);
                }
                y += 34f;

                if (GUI.Button(new Rect(Pad, y, cw, BtnH), "Add Build Materials", _sBtnWide))
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
            GUI.Label(new Rect(area.x, y, w, LineH), "Graphics", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, LineH), "Graphics settings coming soon.", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Controls
            GUI.Label(new Rect(area.x, y, w, LineH), "Controls", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, LineH), "Scroll wheel — zoom in / out", _sSub); y += 18f;
            GUI.Label(new Rect(area.x, y, w, LineH), "Right-drag — pan camera", _sSub); y += 18f;
            GUI.Label(new Rect(area.x, y, w, LineH), "Space — pause / resume", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Sound
            GUI.Label(new Rect(area.x, y, w, LineH), "Sound", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, LineH), "Sound settings coming soon.", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Save / Load
            GUI.Label(new Rect(area.x, y, w, LineH), "Game", _sLabel); y += 24f;

            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(area.x, y, w, BtnH), "Save Game", _sBtnWide))
                    _gm.SaveGame();
                y += 34f;

                // Load is not yet implemented — show as disabled stub
                var prevColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                GUI.Button(new Rect(area.x, y, w, BtnH), "Load Game  (coming soon)", _sBtnWide);
                GUI.color = prevColor;
                y += 34f;
            }

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Exit
            GUI.Label(new Rect(area.x, y, w, LineH), "Exit", _sLabel); y += 24f;
            if (GUI.Button(new Rect(area.x, y, w, BtnH), "Exit to Desktop", _sBtnDanger))
                Application.Quit();
        }

        // ── Drawer helpers ────────────────────────────────────────────────────
        private void Section(string title, float w, ref float y)
        {
            // Style-guide section-label: dim 8px text left, 1px border-dark line filling right
            float lh    = LineH - 4f;
            float textW = w * 0.5f;
            GUI.Label(new Rect(0, y, textW, lh), title.ToUpper(), _sHint);
            DrawSolid(new Rect(textW + 6f, y + lh * 0.5f, w - textW - 6f, 1f), ColDivider);
            y += lh + 8f;
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
            GUI.Label(new Rect(0, y, lw, LineH), label, _sSub);
            DrawSolid(new Rect(bx, y + 5f, bw, 6f), ColSubDrawer);   /* bg-deep track per style guide */
            Color fc = pct > 0.5f ? ColBarFill : pct > 0.25f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(bx, y + 5f, bw * pct, 6f), fc);
            GUI.Label(new Rect(bx + bw + 4f, y, w - bx - bw - 4f, LineH),
                      value.ToString("F0"), _sSub);
            y += LineH + 4f;
        }

        /// <summary>ResourceBar with a fixed fill colour (no pct-based colour switch).</summary>
        private void ResourceBarC(string label, float value, float max, float w, ref float y, Color fill)
        {
            float pct = max > 0f ? Mathf.Clamp01(value / max) : 0f;
            float lw  = w * 0.34f, bx = w * 0.36f, bw = w * 0.44f;
            GUI.Label(new Rect(0, y, lw, LineH), label, _sSub);
            DrawSolid(new Rect(bx, y + 5f, bw, 6f), ColSubDrawer);   /* bg-deep track per style guide */
            DrawSolid(new Rect(bx, y + 5f, bw * pct, 6f), fill);
            GUI.Label(new Rect(bx + bw + 4f, y, w - bx - bw - 4f, LineH),
                      value.ToString("F0"), _sSub);
            y += LineH + 4f;
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

        // ── Panel chrome ──────────────────────────────────────────────────────
        // Style-guide constants
        private const float PanelHdrH  = 54f;   // header zone height
        private const float PanelStatH = 24f;   // status bar height
        private const int   RivetPx    = 6;     // rivet square side (px)

        // Tab → (icon, subtitle) lookup
        private static (string icon, string sub) TabMeta(Tab tab) => tab switch
        {
            Tab.Station     => ("\u2b21", "STATION OVERVIEW"),   // ⬡
            Tab.Build       => ("\u2b21", "DESIGNER"),           // ⬡
            Tab.Crew        => ("\u25cf", "CREW MANAGEMENT"),    // ●
            Tab.Comms       => ("\u2261", "COMMUNICATIONS"),     // ≡
            Tab.AwayMission => ("\u25b6", "AWAY MISSIONS"),      // ►
            Tab.Rooms       => ("\u25a1", "ROOM DESIGNATIONS"),  // □
            Tab.Research    => ("\u229b", "RESEARCH TREE"),      // ⊛
            Tab.Map         => ("\u2295", "NAVIGATION"),         // ⊕
            Tab.Views       => ("\u25c8", "DISPLAY SETTINGS"),   // ◈
            Tab.Settings    => ("\u25ce", "SYSTEM SETTINGS"),    // ◎
            _               => ("\u25a1", ""),
        };

        // SubPanel → (icon, subtitle) lookup
        private static (string icon, string sub) SubMeta(SubPanel sp) => sp switch
        {
            SubPanel.CrewDetail      => ("\u25cf", "CREW MEMBER"),    // ●
            SubPanel.HoldSettings    => ("\u25c8", "CARGO HOLD"),     // ◈
            SubPanel.ResearchDetail  => ("\u229b", "RESEARCH NODE"),  // ⊛
            SubPanel.ModuleDetail    => ("\u25a1", "MODULE INFO"),    // □
            SubPanel.StationSettings => ("\u25ce", "SETTINGS"),       // ◎
            SubPanel.StationBuild    => ("\u2b21", "BUILD"),          // ⬡
            SubPanel.StationRooms    => ("\u25a1", "ROOMS"),          // □
            SubPanel.StationCrew     => ("\u25cf", "CREW"),           // ●
            SubPanel.StationComms    => ("\u2261", "COMMS"),          // ≡
            _                        => ("\u25a1", ""),
        };

        /// <summary>
        /// Draws the full industrial-terminal chrome for a panel:
        ///   • bg-raised header zone (icon · title · subtitle · close button)
        ///   • border-dark header separator
        ///   • four corner rivets
        ///   • bg-base status bar with hint text and LED indicators
        /// Returns the Y coordinate where panel content should begin.
        /// </summary>
        private float DrawPanelChrome(float w, float h,
            string icon, string title, string subtitle,
            System.Action onClose,
            string statusHint,
            bool ledGreen = true, bool ledAmber = false)
        {
            EnsureStyles();

            // ── Header zone (bg-raised, 0 → PanelHdrH) ───────────────────────
            DrawSolid(new Rect(0, 0, w, PanelHdrH), ColBarBg);               // bg-raised fill
            DrawSolid(new Rect(0, PanelHdrH, w, 1f), ColDivider);            // header bottom border

            // Icon
            GUI.Label(new Rect(Pad, 11f, 20f, 20f), icon, _sIconAcc);

            // Title (text-head, 15px bold)
            GUI.color = ColTextHead;
            GUI.Label(new Rect(Pad + 22f, 7f, w - Pad * 2f - 50f, 20f), title.ToUpper(), _sHeader);
            GUI.color = Color.white;

            // Subtitle (text-dim, 8px)
            GUI.Label(new Rect(Pad + 22f, 28f, w - Pad * 2f - 50f, 14f), subtitle, _sHint);

            // Close button — bg-base normally, red (#c03030) on hover
            Rect cbR     = new Rect(w - Pad - 20f, PanelHdrH * 0.5f - 10f, 20f, 20f);
            bool cbHover = cbR.Contains(Event.current.mousePosition);
            DrawSolid(cbR, cbHover ? ColBarCrit : ColBar);                   // fill
            DrawSolid(new Rect(cbR.x,        cbR.y,        cbR.width, 1f),  ColBarEdge); // top
            DrawSolid(new Rect(cbR.x,        cbR.yMax - 1f, cbR.width, 1f), ColBarEdge); // bottom
            DrawSolid(new Rect(cbR.x,        cbR.y,        1f, cbR.height), ColBarEdge); // left
            DrawSolid(new Rect(cbR.xMax - 1f, cbR.y,       1f, cbR.height), ColBarEdge); // right
            GUI.color = cbHover ? Color.white : ColTextMid;
            if (GUI.Button(cbR, "\u2715", GUIStyle.none))  // ✕ transparent button
                onClose?.Invoke();
            GUI.color = Color.white;

            // ── Four corner rivets (bevel-hi squares) ────────────────────────
            DrawSolid(new Rect(4f,       4f,      RivetPx, RivetPx), ColBevelHi);
            DrawSolid(new Rect(w - 10f,  4f,      RivetPx, RivetPx), ColBevelHi);
            DrawSolid(new Rect(4f,      h - 10f,  RivetPx, RivetPx), ColBevelHi);
            DrawSolid(new Rect(w - 10f, h - 10f,  RivetPx, RivetPx), ColBevelHi);

            // ── Status bar (bg-base, at panel bottom) ────────────────────────
            float sbY = h - PanelStatH;
            DrawSolid(new Rect(0, sbY, w, PanelStatH), ColBar);              // bg-base fill
            DrawSolid(new Rect(0, sbY, w, 1f), ColDivider);                  // top border

            // Hint text (8px, text-dim, uppercase)
            GUI.Label(new Rect(Pad, sbY + 5f, w * 0.66f, 14f), statusHint.ToUpper(), _sHint);

            // LEDs — right-aligned: green · amber · dark
            float lx = w - Pad - RivetPx * 3 - 5f * 2;
            float ly = sbY + (PanelStatH - RivetPx) * 0.5f;
            DrawSolid(new Rect(lx, ly, RivetPx, RivetPx),
                      ledGreen ? ColBarGreen : ColTextDim);                   // green
            lx += RivetPx + 5f;
            DrawSolid(new Rect(lx, ly, RivetPx, RivetPx),
                      new Color(ColBarWarn.r, ColBarWarn.g, ColBarWarn.b,
                                ledAmber ? 0.70f : 0.25f));                  // amber
            lx += RivetPx + 5f;
            DrawSolid(new Rect(lx, ly, RivetPx, RivetPx), ColTextDim);      // dark (inactive)

            // ── Return first content Y ────────────────────────────────────────
            return PanelHdrH + 6f;   // content starts 6px below header separator
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

        // ── Research tab ──────────────────────────────────────────────────────
        private void DrawResearch(Rect area, float w, float h)
        {
            // Rendered as a fullscreen overlay in OnGUI — nothing to draw here.
        }

        // ── Branch color helper ───────────────────────────────────────────────
        private static Color ResearchBranchColor(ResearchBranch b) => b switch
        {
            ResearchBranch.Industry    => new Color(0.88f, 0.60f, 0.18f),
            ResearchBranch.Exploration => new Color(0.18f, 0.78f, 0.65f),
            ResearchBranch.Diplomacy   => new Color(0.70f, 0.38f, 0.88f),
            ResearchBranch.Security    => new Color(0.88f, 0.30f, 0.30f),
            _                          => new Color(0.35f, 0.62f, 1.00f),  // Science
        };

        // ── Full-screen Research tree ─────────────────────────────────────────
        private void DrawResearchFullscreen(float pw, float ph)
        {
            const float InfoBarH   = 30f;
            const float BranchTabH = 36f;
            const float DetailW    = 250f;
            const float NodeW      = 158f;
            const float NodeH      = 46f;
            const float ColStride  = NodeW + 46f;
            const float RowStride  = NodeH + 12f;
            const float TreePad    = 18f;

            if (_gm?.Station == null || _gm.Research == null || _gm.Registry == null) return;
            var s = _gm.Station;
            if (s.research == null) return;

            var branches = new[]
            {
                ResearchBranch.Industry,  ResearchBranch.Exploration,
                ResearchBranch.Diplomacy, ResearchBranch.Security, ResearchBranch.Science
            };
            Color branchCol = ResearchBranchColor(_researchTreeBranch);

            // ── Info bar ──────────────────────────────────────────────────────
            DrawSolid(new Rect(0, 0, pw, InfoBarH), new Color(0.05f, 0.07f, 0.12f, 1f));
            {
                int   chips   = _gm.Research.GetStoredDatachipCount(s);
                int   chipCap = _gm.Research.GetDatachipCapacity(s);
                int   pending = s.research.pendingDatachips;
                float pts     = s.research.branches.TryGetValue(_researchTreeBranch, out var bst)
                                ? bst.points : 0f;
                var   prev    = GUI.color;
                GUI.color = chipCap > 0 ? ColBarGreen : ColBarWarn;
                GUI.Label(new Rect(8f, 6f, 160f, 18f),
                          chipCap > 0 ? $"\ud83d\udcbe  {chips} / {chipCap}" : "\ud83d\udcbe  \u2014", _sSub);
                GUI.color = new Color(0.55f, 0.62f, 0.78f);
                GUI.Label(new Rect(174f, 6f, 170f, 18f),
                          $"Branch pts: {pts:F0}", _sSub);
                if (pending > 0)
                {
                    GUI.color = ColBarWarn;
                    GUI.Label(new Rect(350f, 6f, 160f, 18f), $"\u26a0 {pending} pending", _sSub);
                }
                GUI.color = prev;
            }

            float iy = InfoBarH;

            // ── Branch tabs ────────────────────────────────────────────────────
            DrawSolid(new Rect(0, iy, pw, BranchTabH), new Color(0.06f, 0.08f, 0.14f, 1f));
            float tW = pw / branches.Length;
            for (int i = 0; i < branches.Length; i++)
            {
                bool  on  = _researchTreeBranch == branches[i];
                Color bc  = ResearchBranchColor(branches[i]);
                float tx  = i * tW;
                if (on)
                {
                    DrawSolid(new Rect(tx, iy, tW, BranchTabH),
                              new Color(bc.r * 0.20f, bc.g * 0.20f, bc.b * 0.20f, 1f));
                    DrawSolid(new Rect(tx, iy, tW, 2f), bc);
                }
                var prevC = GUI.color;
                GUI.color = on ? bc : ColTextBase;
                if (GUI.Button(new Rect(tx + 1f, iy, tW - 2f, BranchTabH),
                               branches[i].ToString(), _sBtnSmall))
                {
                    _researchTreeBranch    = branches[i];
                    _selectedResearchNodeId = "";
                }
                GUI.color = prevC;
            }
            iy += BranchTabH;
            DrawSolid(new Rect(0, iy, pw, 1f),
                      new Color(branchCol.r * 0.30f, branchCol.g * 0.30f, branchCol.b * 0.30f, 1f));
            iy += 1f;

            // ── Build node positions for this branch ───────────────────────────
            var allNodes = _gm.Registry.ResearchNodes.Values
                              .Where(n => n.branch == _researchTreeBranch)
                              .ToList();

            // Topological depth via relaxation
            var depths = new Dictionary<string, int>();
            foreach (var n in allNodes) depths[n.id] = 0;
            for (int pass = 0; pass < allNodes.Count; pass++)
                foreach (var n in allNodes)
                {
                    int d = 0;
                    foreach (var p in n.prerequisites)
                        if (depths.TryGetValue(p, out int pd)) d = Mathf.Max(d, pd + 1);
                    depths[n.id] = d;
                }

            var byDepth = new SortedDictionary<int, List<ResearchNodeDefinition>>();
            foreach (var n in allNodes)
            {
                int d = depths[n.id];
                if (!byDepth.ContainsKey(d)) byDepth[d] = new List<ResearchNodeDefinition>();
                byDepth[d].Add(n);
            }
            foreach (var kv in byDepth)
                kv.Value.Sort((a, b2) =>
                {
                    int c = a.subbranch.CompareTo(b2.subbranch);
                    return c != 0 ? c : string.Compare(a.displayName, b2.displayName,
                                                        System.StringComparison.Ordinal);
                });

            var nodePos = new Dictionary<string, Vector2>();
            foreach (var kv in byDepth)
            {
                float cx = TreePad + kv.Key * ColStride;
                for (int ri = 0; ri < kv.Value.Count; ri++)
                    nodePos[kv.Value[ri].id] = new Vector2(cx, TreePad + ri * RowStride);
            }

            int   maxDepth  = byDepth.Count > 0 ? byDepth.Keys.Max() : 0;
            int   maxRows   = byDepth.Count > 0 ? byDepth.Values.Max(v => v.Count) : 0;
            float contentW  = TreePad * 2f + (maxDepth + 1) * ColStride;
            float contentH  = TreePad * 2f + maxRows * RowStride;

            bool  hasDetail = !string.IsNullOrEmpty(_selectedResearchNodeId) &&
                              _gm.Registry.ResearchNodes.ContainsKey(_selectedResearchNodeId);
            float canvasW   = hasDetail ? pw - DetailW : pw;
            float canvasTop = iy;
            float canvasH   = ph - canvasTop;

            // ── Scrollable 2D tree canvas ─────────────────────────────────────
            _researchTreeScroll = GUI.BeginScrollView(
                new Rect(0, canvasTop, canvasW, canvasH),
                _researchTreeScroll,
                new Rect(0, 0, Mathf.Max(canvasW, contentW), Mathf.Max(canvasH, contentH)));

            // Connector lines (draw behind nodes)
            foreach (var n in allNodes)
            {
                if (!nodePos.TryGetValue(n.id, out var np)) continue;
                float nMidY = np.y + NodeH * 0.5f;
                foreach (var prereqId in n.prerequisites)
                {
                    if (!nodePos.TryGetValue(prereqId, out var pp)) continue;
                    float pMidY  = pp.y + NodeH * 0.5f;
                    float pRight = pp.x + NodeW;
                    float nLeft  = np.x;
                    float midX   = pRight + (nLeft - pRight) * 0.5f;
                    Color lc     = new Color(branchCol.r, branchCol.g, branchCol.b, 0.28f);
                    DrawSolid(new Rect(pRight, pMidY - 1f, midX - pRight, 2f), lc);
                    if (Mathf.Abs(nMidY - pMidY) > 2f)
                    {
                        float vTop = Mathf.Min(nMidY, pMidY);
                        DrawSolid(new Rect(midX - 1f, vTop, 2f, Mathf.Abs(nMidY - pMidY)), lc);
                    }
                    DrawSolid(new Rect(midX, nMidY - 1f, nLeft - midX, 2f), lc);
                }
            }

            // Nodes
            foreach (var n in allNodes)
            {
                if (!nodePos.TryGetValue(n.id, out var np)) continue;
                Rect nr         = new Rect(np.x, np.y, NodeW, NodeH);
                bool isUnlocked = s.research.IsUnlocked(n.id);
                bool prereqsMet = n.prerequisites.All(p => s.research.IsUnlocked(p));
                bool isAvail    = !isUnlocked && prereqsMet;
                bool isLocked   = !isUnlocked && !prereqsMet;
                bool isSelected = _selectedResearchNodeId == n.id;
                float alpha     = isLocked ? 0.40f : 1f;

                DrawSolid(nr, isSelected
                    ? new Color(0.12f, 0.18f, 0.35f, 1f)
                    : new Color(0.08f, 0.11f, 0.19f, isLocked ? 0.50f : 0.82f));

                // Top border when selected
                if (isSelected)
                    DrawSolid(new Rect(nr.x, nr.y, nr.width, 1f), branchCol);

                // Left status bar
                Color statCol = isUnlocked ? ColBarGreen
                              : isAvail    ? branchCol
                              : new Color(0.28f, 0.30f, 0.38f);
                DrawSolid(new Rect(nr.x, nr.y, 3f, nr.height), statCol);

                var prevC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(nr.x + 6f, nr.y + 5f, nr.width - 10f, LineH),
                          n.displayName, _sSub);
                GUI.color = new Color(0.44f, 0.50f, 0.64f, alpha);
                GUI.Label(new Rect(nr.x + 6f, nr.y + NodeH - LineH - 3f, nr.width - 10f, LineH),
                          $"{n.subbranch}  \u00b7  {n.pointCost}pt", _sSub);

                if (isUnlocked)
                {
                    GUI.color = ColBarGreen;
                    GUI.Label(new Rect(nr.xMax - 18f, nr.y + 5f, 16f, 16f), "\u2713", _sSub);
                }
                else if (isLocked)
                {
                    GUI.color = new Color(0.38f, 0.40f, 0.48f, 0.7f);
                    GUI.Label(new Rect(nr.xMax - 18f, nr.y + 5f, 16f, 16f), "\ud83d\udd12", _sSub);
                }
                GUI.color = prevC;

                if (GUI.Button(nr, "", GUIStyle.none))
                    _selectedResearchNodeId = isSelected ? "" : n.id;
            }

            GUI.EndScrollView();

            // ── Detail side panel ─────────────────────────────────────────────
            if (hasDetail &&
                _gm.Registry.ResearchNodes.TryGetValue(_selectedResearchNodeId, out var sel))
            {
                float dpx = pw - DetailW;
                DrawSolid(new Rect(dpx, canvasTop, 1f, canvasH),
                          new Color(branchCol.r * 0.35f, branchCol.g * 0.35f, branchCol.b * 0.35f, 1f));
                DrawSolid(new Rect(dpx + 1f, canvasTop, DetailW - 1f, canvasH),
                          new Color(0.05f, 0.07f, 0.14f, 0.98f));
                DrawResearchDetail_Inline(dpx + 8f, canvasTop + 8f,
                                          DetailW - 16f, canvasH - 16f,
                                          sel, branchCol, s);
            }
        }

        // ── Inline research detail panel ──────────────────────────────────────
        private void DrawResearchDetail_Inline(float x, float y, float w, float h,
            ResearchNodeDefinition node, Color branchCol, StationState s)
        {
            if (!s.research.branches.TryGetValue(node.branch, out var bState)) return;
            var prevC = GUI.color;

            // Dismiss button
            GUI.color = ColTextBase;
            if (GUI.Button(new Rect(x + w - 20f, y, 20f, 18f), "\u00d7", _sBtnSmall))
                _selectedResearchNodeId = "";
            GUI.color = prevC;

            // Name
            GUI.Label(new Rect(x, y, w - 24f, LineH + 4f), node.displayName, _sHeader);
            y += LineH + 8f;

            // Branch / subbranch badge
            GUI.color = branchCol;
            GUI.Label(new Rect(x, y, w, LineH),
                      $"{node.branch}  \u00b7  {node.subbranch}", _sSub);
            GUI.color = prevC;
            y += LineH + 4f;

            DrawSolid(new Rect(x, y, w, 1f), ColDivider); y += 8f;

            // Description
            if (!string.IsNullOrEmpty(node.description))
            {
                float dh = _sDescr.CalcHeight(new GUIContent(node.description), w);
                GUI.Label(new Rect(x, y, w, dh), node.description, _sDescr);
                y += dh + 8f;
            }

            DrawSolid(new Rect(x, y, w, 1f), ColDivider); y += 8f;

            // Branch points progress
            float pct  = node.pointCost > 0 ? Mathf.Clamp01(bState.points / (float)node.pointCost) : 1f;
            GUI.Label(new Rect(x, y, w, LineH),
                      $"Points: {bState.points:F0} / {node.pointCost}", _sSub);
            y += LineH + 2f;
            DrawSolid(new Rect(x, y, w, 8f), ColBarBg);
            Color barCol = pct >= 1f ? ColBarGreen : ColBarFill;
            DrawSolid(new Rect(x, y, w * pct, 8f), barCol);
            y += 16f;

            // Prerequisites
            if (node.prerequisites.Count > 0)
            {
                y += 6f;
                GUI.Label(new Rect(x, y, w, LineH), "Requires:", _sLabel);
                y += LineH;
                foreach (var prereqId in node.prerequisites)
                {
                    bool   met   = s.research.IsUnlocked(prereqId);
                    string pname = _gm.Registry.ResearchNodes
                                       .TryGetValue(prereqId, out var pn)
                                   ? pn.displayName : prereqId;
                    GUI.color = met ? ColBarGreen : ColBarCrit;
                    GUI.Label(new Rect(x + 6f, y, w - 6f, LineH),
                              (met ? "\u2713 " : "\u2715 ") + pname, _sSub);
                    GUI.color = prevC;
                    y += LineH;
                }
            }

            // Unlock tags
            if (node.unlockTags.Count > 0)
            {
                y += 6f;
                GUI.Label(new Rect(x, y, w, LineH), "Unlocks:", _sLabel);
                y += LineH;
                foreach (var tag in node.unlockTags)
                {
                    GUI.Label(new Rect(x + 6f, y, w - 6f, LineH), $"\u2022 {tag}", _sSub);
                    y += LineH;
                }
            }
        }

        // ── Research node detail (shown in sub-drawer) ────────────────────────
        private void DrawResearchNodeDetail(Rect area, float w, float h)
        {
            if (_gm?.Station == null || _gm.Research == null) return;
            var s = _gm.Station;

            if (!_gm.Registry.ResearchNodes.TryGetValue(_selectedResearchNodeId, out var node))
            { GUI.Label(area, "Node not found.", _sSub); return; }

            var bState = s.research?.branches != null &&
                         s.research.branches.TryGetValue(node.branch, out var bs) ? bs : null;

            float y = area.y;

            // Name
            GUI.Label(new Rect(area.x, y, w, LineH + 4f), node.displayName, _sHeader);
            y += LineH + 8f;

            // Branch + cost
            GUI.Label(new Rect(area.x, y, w * 0.55f, LineH), node.branch.ToString(), _sSub);
            GUI.Label(new Rect(area.x + w * 0.58f, y, w * 0.42f, LineH),
                      $"{node.pointCost} pts", _sSub);
            y += LineH + 4f;

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 8f;

            // Description (word-wrapped as multiple lines)
            GUI.Label(new Rect(area.x, y, w, h - (y - area.y) > 60f ? h - (y - area.y) - 60f : 60f),
                      node.description, _sSub);
            y += Mathf.Min(h * 0.35f, 80f);

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 8f;

            // Progress bar
            if (bState != null)
            {
                float pct = node.pointCost > 0 ? Mathf.Clamp01(bState.points / node.pointCost) : 1f;
                GUI.Label(new Rect(area.x, y, w, LineH),
                          $"Branch points: {bState.points:F0} / {node.pointCost}", _sSub);
                y += LineH + 2f;
                DrawSolid(new Rect(area.x, y + 4f, w, 10f), ColBarBg);
                Color barCol = pct >= 1f ? ColBarGreen : ColBarFill;
                DrawSolid(new Rect(area.x, y + 4f, w * pct, 10f), barCol);
                y += 20f;
            }

            // Prerequisites
            if (node.prerequisites.Count > 0)
            {
                y += 8f;
                GUI.Label(new Rect(area.x, y, w, LineH), "Requires:", _sSub); y += LineH;
                foreach (var prereqId in node.prerequisites)
                {
                    bool met = s.research?.IsUnlocked(prereqId) ?? false;
                    string prereqName = _gm.Registry.ResearchNodes.TryGetValue(prereqId, out var pn) ? pn.displayName : prereqId;
                    var prev = GUI.color;
                    GUI.color = met ? ColBarGreen : ColBarCrit;
                    GUI.Label(new Rect(area.x + 8f, y, w - 8f, LineH),
                              (met ? "✓ " : "✗ ") + prereqName, _sSub);
                    GUI.color = prev;
                    y += LineH;
                }
            }

            // Unlock tags
            if (node.unlockTags.Count > 0)
            {
                y += 8f;
                GUI.Label(new Rect(area.x, y, w, LineH), "Unlocks:", _sSub); y += LineH;
                foreach (var tag in node.unlockTags)
                {
                    GUI.Label(new Rect(area.x + 8f, y, w - 8f, LineH), tag, _sSub);
                    y += LineH;
                }
            }
        }

        // ── Module detail (shown in sub-drawer) ───────────────────────────────
        private void DrawModuleDetail(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            if (!_gm.Station.modules.TryGetValue(_subItemUid, out var mod))
            { GUI.Label(area, "Module not found.", _sSub); CloseSub(); return; }

            float y = area.y;

            // Name header
            GUI.Label(new Rect(area.x, y, w, LineH + 4f), mod.displayName, _sHeader);
            y += LineH + 8f;

            // Status badge
            string status = !mod.active    ? "OFFLINE"
                          : mod.damage > 0f ? $"Damaged ({mod.damage:P0})"
                          : "Operational";
            Color sc = !mod.active    ? ColBarCrit
                     : mod.damage > 0f ? ColBarWarn
                     : ColBarGreen;
            var prevC = GUI.color; GUI.color = sc;
            GUI.Label(new Rect(area.x, y, w, LineH), status, _sLabel);
            GUI.color = prevC;
            y += LineH + 4f;

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 8f;

            // Health bar (100% - damage)
            float health = Mathf.Clamp01(1f - mod.damage);
            GUI.Label(new Rect(area.x, y, w * 0.40f, LineH), "Integrity", _sSub);
            DrawSolid(new Rect(area.x + w * 0.42f, y + 5f, w * 0.58f, 8f), ColBarBg);
            Color hc = health > 0.6f ? ColBarGreen : health > 0.3f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(area.x + w * 0.42f, y + 5f, w * 0.58f * health, 8f), hc);
            y += LineH + 4f;

            // Module UID (useful for debugging)
            GUI.Label(new Rect(area.x, y, w, LineH), $"UID: {mod.uid}", _sSub);
            y += LineH + 4f;

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 8f;

            // Active toggle
            string toggleLabel = mod.active ? "⏸ Deactivate" : "▶ Activate";
            Color toggleCol = mod.active ? new Color(0.88f, 0.68f, 0.10f) : ColBarGreen;
            var ptc = GUI.color; GUI.color = toggleCol;
            if (GUI.Button(new Rect(area.x, y, w, BtnH), toggleLabel, _sBtnWide))
                mod.active = !mod.active;
            GUI.color = ptc;
            y += BtnH + 6f;

            // Repair (if damaged)
            if (mod.damage > 0f)
            {
                var prc = GUI.color; GUI.color = ColBarGreen;
                if (GUI.Button(new Rect(area.x, y, w, BtnH), "🔧 Repair (instant)", _sBtnWide))
                    mod.damage = 0f;
                GUI.color = prc;
            }
        }

        // ── Map tab ───────────────────────────────────────────────────────────
        private void DrawMap(Rect area, float w, float h)
        {
            if (_gm?.Station == null)
            { GUI.Label(new Rect(area.x, area.y, w, 20f), "Map not available.", _sSub); return; }

            // ── Sub-panel nav (Map | Away) ────────────────────────────────────
            const float MapNavH   = 36f;
            const float MapNavPad = 4f;
            float mapNavBw = (w - MapNavPad * 3f) / 2f;
            DrawSolid(new Rect(area.x, area.y, w, MapNavH + MapNavPad), new Color(0.05f, 0.07f, 0.12f, 0.97f));
            Color mapNavPrev = GUI.color;
            var mapSubPanels = new[] { (MapSubPanel.Map, "\u2b21 Map"), (MapSubPanel.Away, "\u2708 Away") };
            for (int i = 0; i < mapSubPanels.Length; i++)
            {
                var (ms, msl) = mapSubPanels[i];
                bool msActive = _mapSub == ms;
                GUI.color = msActive ? new Color(0.35f, 0.62f, 1.00f, 1f) : new Color(0.55f, 0.60f, 0.70f, 1f);
                if (GUI.Button(new Rect(area.x + MapNavPad + i * (mapNavBw + MapNavPad), area.y + MapNavPad, mapNavBw, MapNavH - MapNavPad * 2f), msl, _sBtnSmall))
                    _mapSub = ms;
            }
            GUI.color = mapNavPrev;

            Rect mapSubArea = new Rect(area.x, area.y + MapNavH + MapNavPad, w, h - MapNavH - MapNavPad);

            if (_mapSub == MapSubPanel.Away)
            { DrawAwayMission(mapSubArea, w, h - MapNavH - MapNavPad); return; }

            // ── Map sub-panel ─────────────────────────────────────────────────
            area = mapSubArea;
            if (_gm.Map == null)
            { GUI.Label(new Rect(area.x, area.y, w, 20f), "Map not available.", _sSub); return; }

            var s     = _gm.Station;
            var level = _gm.Map.GetMapViewLevel(s);
            var pois  = _gm.Map.GetDiscoveredPois(s);

            float y = area.y;

            // Map view level + range
            string lvlLabel = level switch
            {
                MapViewLevel.System   => "System View",
                MapViewLevel.Sector   => "Sector View",
                MapViewLevel.Quadrant => "Quadrant View",
                MapViewLevel.Galaxy   => "Galaxy View",
                _                    => "System View",
            };
            GUI.Label(new Rect(area.x, y, w, 20f), $"Map: {lvlLabel}", _sLabel);
            y += 22f;
            float range = _gm.Map.GetDetectionRange(s);
            GUI.Label(new Rect(area.x, y, w, 16f),
                      $"Detection range: {range:F0} u  |  {pois.Count} POIs discovered", _sSub);
            y += 20f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider);
            y += 8f;

            // ── Active asteroid missions ──────────────────────────────────────
            bool hasActiveMissions = false;
            foreach (var kv in s.asteroidMaps)
                if (kv.Value.status == "active") hasActiveMissions = true;

            if (hasActiveMissions)
            {
                GUI.Label(new Rect(area.x, y, w, 16f), "Active Mining Missions:", _sLabel);
                y += 20f;
                foreach (var kv in s.asteroidMaps)
                {
                    var am = kv.Value;
                    if (am.status != "active") continue;
                    int remaining = Mathf.Max(0, am.endTick - s.tick);
                    s.pointsOfInterest.TryGetValue(am.poiUid, out var poi2);
                    string poiName = poi2 != null ? poi2.displayName : am.poiUid;
                    DrawSolid(new Rect(area.x, y, w, 28f), new Color(0.09f, 0.12f, 0.20f, 0.9f));
                    GUI.Label(new Rect(area.x + 4f, y + 2f, w * 0.65f, 16f), poiName, _sSub);
                    GUI.Label(new Rect(area.x + w * 0.67f, y + 4f, w * 0.33f, LineH),
                              $"{remaining}t left", _sSub);
                    y += 32f;
                }
                DrawSolid(new Rect(area.x, y, w, 1f), ColDivider);
                y += 8f;
            }

            // ── POI list ──────────────────────────────────────────────────────
            if (pois.Count == 0)
            { GUI.Label(new Rect(area.x, y, w, 18f), "No POIs discovered yet.", _sSub); return; }

            float listH = h - (y - area.y) - 4f;
            float innerH2 = 0f;
            foreach (var poi in pois)
                innerH2 += ComputePoiRowHeight(poi, s);
            innerH2 = Mathf.Max(innerH2, listH);

            _mapScroll = GUI.BeginScrollView(
                new Rect(area.x, y, w, listH),
                _mapScroll, new Rect(0, 0, w - 14f, innerH2));

            float ly = 0f;
            foreach (var poi in pois)
            {
                bool sel = _selectedPoiUid == poi.uid;
                float rowH   = ComputePoiRowHeight(poi, s);
                float startLy = ly;
                DrawSolid(new Rect(0, ly, w - 14f, rowH - 4f),
                          sel ? ColTabHl : new Color(0.09f, 0.11f, 0.18f, 0.9f));

                // Type icon
                string icon = poi.poiType switch
                {
                    "Asteroid"         => "★",
                    "TradePost"        => "◆",
                    "AbandonedStation" => "◉",
                    "NebulaPocket"     => "◈",
                    _                 => "·",
                };
                GUI.Label(new Rect(4f, ly + 4f, 18f, 18f), icon, _sSub);
                GUI.Label(new Rect(22f, ly + 4f, w * 0.65f - 36f, 18f), poi.displayName, _sLabel);
                GUI.Label(new Rect(w * 0.68f, ly + 6f, w * 0.32f - 14f, LineH),
                          poi.poiType, _sSub);

                // Distance
                float dist = Mathf.Sqrt(poi.posX * poi.posX + poi.posY * poi.posY);
                GUI.Label(new Rect(22f, ly + 24f, w * 0.65f - 36f, LineH),
                          $"Dist: {dist:F0} u", _sSub);

                // Select / expand
                if (GUI.Button(new Rect(w * 0.72f, ly + 22f, w * 0.28f - 14f, 18f),
                               sel ? "▲ Close" : "▼ Detail", _sBtnSmall))
                {
                    _selectedPoiUid = sel ? "" : poi.uid;
                    _selectedMapCrew.Clear();
                    _mapMissionMsg = "";
                }

                // Expanded panel — asteroid mining dispatch
                if (sel && poi.poiType == "Asteroid")
                {
                    ly += 44f;
                    // Yield preview
                    string yieldStr = "";
                    foreach (var kv in poi.resourceYield) yieldStr += $"{kv.Key.Replace("item.","")}×{kv.Value} ";
                    GUI.Label(new Rect(4f, ly, w - 18f, LineH), $"Yield: {yieldStr.Trim()}", _sSub);
                    ly += 16f;

                    // Check if already on a mission
                    bool alreadyDispatched = false;
                    foreach (var am in s.asteroidMaps.Values)
                        if (am.poiUid == poi.uid && am.status == "active") { alreadyDispatched = true; break; }

                    if (alreadyDispatched)
                    {
                        var prev4 = GUI.color; GUI.color = ColBarWarn;
                        GUI.Label(new Rect(4f, ly, w - 18f, 16f), "Mission in progress.", _sSub);
                        GUI.color = prev4;
                        ly += 18f;
                    }
                    else
                    {
                        // Crew picker
                        var crew = s.GetCrew();
                        foreach (var npc in crew)
                        {
                            if (npc.missionUid != null) continue;
                            bool npcSel = _selectedMapCrew.Contains(npc.uid);
                            var prevN = GUI.color;
                            GUI.color = npcSel ? ColBarGreen : new Color(0.55f, 0.60f, 0.70f);
                            if (GUI.Button(new Rect(4f, ly, w - 18f, 16f),
                                           (npcSel ? "✓ " : "  ") + npc.name, _sBtnSmall))
                            {
                                if (npcSel) _selectedMapCrew.Remove(npc.uid);
                                else        _selectedMapCrew.Add(npc.uid);
                                _mapMissionMsg = "";
                            }
                            GUI.color = prevN;
                            ly += 18f;
                        }

                        if (_selectedMapCrew.Count > 0)
                        {
                            if (GUI.Button(new Rect(4f, ly, w - 18f, 22f), "Send Mining Team", _sBtnWide))
                            {
                                var result = _gm.AsteroidMissions.DispatchAsteroidMission(
                                    poi.uid, new List<string>(_selectedMapCrew), s);
                                _mapMissionMsg = result.ok ? "Mission dispatched!" : result.reason;
                                if (result.ok) _selectedMapCrew.Clear();
                            }
                            ly += 24f;
                        }
                        if (!string.IsNullOrEmpty(_mapMissionMsg))
                        {
                            GUI.Label(new Rect(4f, ly, w - 18f, LineH), _mapMissionMsg, _sSub);
                            ly += 16f;
                        }
                    }
                    // Snap to the pre-computed row boundary for clean alignment.
                    ly = startLy + rowH;
                }
                else
                {
                    ly += rowH;
                }
            }
            GUI.EndScrollView();
        }

        /// <summary>
        /// Computes the actual pixel height for a POI row, accounting for the variable
        /// number of crew lines and optional controls in the expanded asteroid panel.
        /// </summary>
        private float ComputePoiRowHeight(PointOfInterest poi, StationState s)
        {
            if (_selectedPoiUid != poi.uid || poi.poiType != "Asteroid")
                return 52f;

            // 44px header section + 16px yield line
            float h = 44f + 16f;

            bool alreadyDispatched = false;
            foreach (var am in s.asteroidMaps.Values)
                if (am.poiUid == poi.uid && am.status == "active") { alreadyDispatched = true; break; }

            if (alreadyDispatched)
            {
                h += 18f; // "Mission in progress." label
            }
            else
            {
                var crew = s.GetCrew();
                foreach (var npc in crew)
                    if (npc.missionUid == null) h += 18f; // one row per available crew member
                if (_selectedMapCrew.Count > 0) h += 24f; // "Send Mining Team" button
                if (!string.IsNullOrEmpty(_mapMissionMsg)) h += 16f; // feedback message
            }

            return h + 4f; // 4px gap between rows
        }

        // ── Style setup ───────────────────────────────────────────────────────
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            // GUIStyle() base (NOT GUI.skin.button) — zero Unity skin background, zero bleed-through
            _sTabOff = new GUIStyle()
            {
                fontSize  = FontSize,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(4, 4, 3, 3),
                normal    = { textColor = ColTextBright },
                hover     = { textColor = ColTextHead },
                active    = { textColor = ColTextHead },
            };

            _sTabOn = new GUIStyle(_sTabOff)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = ColTextHead, background = null },
            };

            _sHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize  = FontSizeHdr,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = ColTextHead },
            };

            _sLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize  = FontSize,
                fontStyle = FontStyle.Bold,
                wordWrap  = false,
                normal    = { textColor = ColTextBright },
            };

            _sSub = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize,
                wordWrap = false,
                normal   = { textColor = ColTextBase },
            };

            _sBtnSmall = new GUIStyle()
            {
                fontSize  = FontSize,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(2, 2, 2, 2),
                normal    = { textColor = Color.white },
                hover     = { textColor = Color.white },
                active    = { textColor = Color.white },
            };

            _sBtnWide = new GUIStyle()
            {
                fontSize  = FontSize,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(2, 2, 2, 2),
                normal    = { textColor = Color.white },
                hover     = { textColor = Color.white },
                active    = { textColor = Color.white },
            };

            _sBtnDanger = new GUIStyle()
            {
                fontSize  = FontSize,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(2, 2, 2, 2),
                normal    = { textColor = ColBarCrit },
                hover     = { textColor = new Color(0.90f, 0.30f, 0.30f, 1f) },
                active    = { textColor = new Color(1.00f, 0.40f, 0.40f, 1f) },
            };

            _sTextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = FontSize,
                normal   = { textColor = ColTextHead },  /* --text-head #a8c0d8 */
            };

            // Wrapping variant of _sSub — used for multi-line descriptions and message bodies.
            _sDescr = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize,
                wordWrap = true,
                normal   = { textColor = ColTextMid },
            };

            // 8px hint text — panel subtitle line and status bar hints
            _sHint = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 8,
                wordWrap  = false,
                normal    = { textColor = ColTextDim },
                alignment = TextAnchor.MiddleLeft,
            };

            // 13px icon label — panel title icon (acc blue)
            _sIconAcc = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                wordWrap  = false,
                normal    = { textColor = ColAccent },
                alignment = TextAnchor.MiddleCenter,
            };

            _gameFont = Resources.Load<Font>("Fonts/Quango");
            if (_gameFont != null)
            {
                _sTabOff.font    = _gameFont;
                _sTabOn.font     = _gameFont;
                _sHeader.font    = _gameFont;
                _sLabel.font     = _gameFont;
                _sSub.font       = _gameFont;
                _sDescr.font     = _gameFont;
                _sBtnSmall.font  = _gameFont;
                _sBtnWide.font   = _gameFont;
                _sBtnDanger.font = _gameFont;
                _sTextField.font = _gameFont;
                _sHint.font      = _gameFont;
                _sIconAcc.font   = _gameFont;
            }
        }

        // ── Layout helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Draws a row of evenly-spaced buttons and advances sy by BtnH + gap.
        /// Add or remove entries freely — widths recalculate automatically.
        /// Each entry: (label, color override or null, action).
        /// Returns the new sy after the row.
        /// </summary>
        private float ButtonRow(float x, float sy, float rowW, float gap,
            params (string label, Color? tint, System.Action onClick)[] buttons)
        {
            float spacing = 4f;
            float bw = (rowW - spacing * (buttons.Length - 1)) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, tint, onClick) = buttons[i];
                var prev = GUI.color;
                if (tint.HasValue) GUI.color = tint.Value;
                if (GUI.Button(new Rect(x + i * (bw + spacing), sy, bw, BtnH), label, _sBtnSmall))
                    onClick?.Invoke();
                GUI.color = prev;
            }
            return sy + BtnH + gap;
        }

        // ── Skills Sub-Panel ──────────────────────────────────────────────────

        /// <summary>
        /// Skills tab: NPC selector on the left, skill details on the right.
        /// Shows character level, total XP, expertise slots, skill list with XP bars,
        /// and chosen expertise cards.
        /// </summary>
        private void DrawCrewSkills(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            if (_gm.Skills == null)
            {
                GUI.Label(new Rect(area.x + 8f, area.y + 8f, w, 18f),
                          "Skill system not initialised.", _sSub);
                return;
            }

            var crew = _gm.Station.GetCrew();
            if (crew.Count == 0)
            {
                GUI.Label(new Rect(area.x + 8f, area.y + 8f, w, 18f), "No crew.", _sSub);
                return;
            }

            // Ensure we have a selected NPC
            if (string.IsNullOrEmpty(_skillsSelectedNpcUid) ||
                !_gm.Station.npcs.ContainsKey(_skillsSelectedNpcUid))
                _skillsSelectedNpcUid = crew[0].uid;

            // ── NPC selector strip ────────────────────────────────────────────
            const float SelectorH = 24f;
            float sx = area.x;
            float sy = area.y;
            float btnW = w / Mathf.Max(1, crew.Count);
            foreach (var n in crew)
            {
                bool sel = n.uid == _skillsSelectedNpcUid;
                if (GUI.Button(new Rect(sx, sy, btnW - 1f, SelectorH),
                               n.name,
                               sel ? _sTabOn : _sTabOff))
                {
                    _skillsSelectedNpcUid  = n.uid;
                    _expertisePanelOpen    = false;
                }
                sx += btnW;
            }

            // ── Detail area ───────────────────────────────────────────────────
            if (!_gm.Station.npcs.TryGetValue(_skillsSelectedNpcUid, out var npc)) return;

            Rect detailRect = new Rect(area.x, area.y + SelectorH + 4f, w, h - SelectorH - 4f);
            float detailH   = h - SelectorH - 4f;

            if (_expertisePanelOpen)
            {
                DrawExpertiseSelectionPanel(detailRect, w, detailH, npc);
                return;
            }

            DrawNpcSkillDetail(detailRect, w, detailH, npc);
        }

        private void DrawNpcSkillDetail(Rect area, float w, float h, NPCInstance npc)
        {
            var skillSys = _gm.Skills;
            int charLevel   = SkillSystem.GetCharacterLevel(npc);
            int slotCount   = SkillSystem.GetExpertiseSlotCount(npc);
            int unspent     = SkillSystem.GetUnspentSlots(npc);
            float totalXP   = SkillSystem.GetTotalXP(npc);
            int nextThreshold = (charLevel / SkillSystem.SlotEveryNLevels + 1)
                                * SkillSystem.SlotEveryNLevels
                                * SkillSystem.CharLevelDivisor;

            float innerH = Mathf.Max(h, 600f);
            _skillsScroll = GUI.BeginScrollView(
                new Rect(area.x, area.y, w, h),
                _skillsScroll,
                new Rect(0, 0, w - 14f, innerH));

            float y = 4f;

            // ── Character level summary ───────────────────────────────────────
            float summaryH = 4f + LineH * 2f + 4f + 8f + LineH + 4f;
            DrawSolid(new Rect(0, y, w - 14f, summaryH), ColSummaryBg);
            GUI.Label(new Rect(4f, y + 2f, 80f, 28f),
                      $"Lv {charLevel}", _sHeader);
            GUI.Label(new Rect(84f, y + 4f, w - 100f, LineH),
                      $"Character Level", _sSub);
            GUI.Label(new Rect(84f, y + 4f + LineH, w - 100f, LineH),
                      $"Total XP: {totalXP:F0}   Slots: {slotCount}   Unspent: {unspent}", _sSub);
            // XP bar toward next slot — progress within the current interval [currentThreshold, nextThreshold]
            int currentThreshold = (charLevel / SkillSystem.SlotEveryNLevels)
                                   * SkillSystem.SlotEveryNLevels
                                   * SkillSystem.CharLevelDivisor;
            float xpPct = Mathf.Clamp01((totalXP - currentThreshold)
                          / Mathf.Max(1, nextThreshold - currentThreshold));
            float barW  = w - 18f;
            float xpBarY = y + 4f + LineH * 2f + 4f;
            DrawSolid(new Rect(4f, xpBarY, barW, 8f), ColBarBg);
            DrawSolid(new Rect(4f, xpBarY, barW * xpPct, 8f), ColAccent);
            // Label drawn AFTER bar so it renders on top
            GUI.Label(new Rect(4f, xpBarY + 10f, barW, LineH),
                      $"  {totalXP:F0} / {nextThreshold} XP  ·  next slot: Lv {(charLevel / SkillSystem.SlotEveryNLevels + 1) * SkillSystem.SlotEveryNLevels}",
                      _sSub);
            y += summaryH + 4f;

            // ── Skill list ────────────────────────────────────────────────────
            GUI.Label(new Rect(4f, y, w - 14f, 16f), "Skills", _sLabel);
            y += 18f;

            foreach (var skillDef in _gm.Registry.Skills.Values
                         .OrderBy(s => s.skillId))
            {
                var inst  = SkillSystem.GetSkillInstance(npc, skillDef.skillId);
                int level = inst?.Level ?? 0;
                float xp  = inst?.currentXP ?? 0f;

                // XP within current level band
                float levelFloorXP = SkillSystem.GetXPForLevel(level);
                float levelCeilXP  = SkillSystem.GetXPForLevel(level + 1);
                float withinLevel  = Mathf.Clamp01(levelCeilXP > levelFloorXP
                    ? (xp - levelFloorXP) / (levelCeilXP - levelFloorXP)
                    : 1f);

                // Skill name | L{level} | XP bar — level label drawn before bar so it isn't covered
                float cw2     = w - 14f;
                float snameW  = CharW * 14f;   // fits "Engineering" (11 ch) + buffer
                float slevW   = CharW * 4f;
                float sbarX   = snameW + slevW + 8f;
                GUI.Label(new Rect(4f, y, snameW, LineH), skillDef.displayName, _sSub);
                GUI.Label(new Rect(snameW + 4f, y, slevW, LineH), $"L{level}", _sSub);

                // XP bar (starts after level badge, stays within content boundary)
                float bx = sbarX, bw = cw2 - sbarX - 2f;
                DrawSolid(new Rect(bx, y + LineH * 0.25f, bw, 8f), ColBarBg);
                Color barCol = level >= 15 ? new Color(0.92f, 0.75f, 0.20f) :
                               level >= 8  ? ColBarGreen : ColBarFill;
                DrawSolid(new Rect(bx, y + LineH * 0.25f, bw * withinLevel, 8f), barCol);

                y += LineH;
            }
            y += 6f;

            // ── Chosen expertise cards ────────────────────────────────────────
            GUI.Label(new Rect(4f, y, w - 14f, 16f), "Expertise", _sLabel);
            y += 18f;

            if (npc.chosenExpertise.Count == 0)
            {
                GUI.Label(new Rect(4f, y, w - 14f, LineH), "None chosen.", _sSub);
                y += LineH + 2f;
            }
            else
            {
                float chosenCardH = LineH * 2f + 6f;
                foreach (var eid in npc.chosenExpertise)
                {
                    if (!_gm.Registry.Expertises.TryGetValue(eid, out var exp)) continue;
                    DrawSolid(new Rect(2f, y, w - 16f, chosenCardH), new Color(0.12f, 0.15f, 0.22f, 0.9f));
                    GUI.Label(new Rect(6f, y + 2f, w * 0.65f, LineH), exp.displayName, _sSub);

                    // Replace button
                    if (GUI.Button(new Rect(w - 74f, y + 2f, 58f, LineH), "Replace", _sBtnSmall))
                    {
                        _swapTargetExpertiseId = eid;
                        _expertisePanelOpen    = true;
                    }
                    float row2Y = y + 2f + LineH + 2f;
                    if (!string.IsNullOrEmpty(exp.requiredSkillId))
                    {
                        GUI.Label(new Rect(w - 74f, row2Y, 68f, LineH),
                                  $"Req {exp.requiredSkillId.Replace("skill.", "")} L{exp.requiredSkillLevel}",
                                  _sSub);
                    }
                    GUI.Label(new Rect(6f, row2Y, w - 80f, LineH),
                              exp.description.Length > ExpertiseDescShortMaxChars + 3
                                  ? exp.description.Substring(0, ExpertiseDescShortMaxChars) + "..."
                                  : exp.description,
                              _sSub);
                    y += chosenCardH + 4f;
                }
            }
            y += 4f;

            // ── Spend slot button ─────────────────────────────────────────────
            bool canSpend = unspent > 0;
            var  prevCol  = GUI.color;
            if (!canSpend) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            if (GUI.Button(new Rect(4f, y, w - 18f, 22f),
                           canSpend ? $"Spend Expertise Slot ({unspent} available)" : "No Slots Available",
                           _sBtnWide) && canSpend)
            {
                _swapTargetExpertiseId = "";
                _expertisePanelOpen    = true;
            }
            GUI.color = prevCol;

            GUI.EndScrollView();
        }

        private void DrawExpertiseSelectionPanel(Rect area, float w, float h, NPCInstance npc)
        {
            bool isSwap = !string.IsNullOrEmpty(_swapTargetExpertiseId);
            string title = isSwap ? "Select Replacement Expertise" : "Select Expertise";
            GUI.Label(new Rect(area.x + 4f, area.y, w - 8f, 18f), title, _sLabel);

            if (isSwap && _gm.Registry.Expertises.TryGetValue(_swapTargetExpertiseId, out var swapDef))
            {
                var wp = GUI.color; GUI.color = ColBarWarn;
                GUI.Label(new Rect(area.x + 4f, area.y + LineH + 2f, w - 8f, LineH),
                          $"Replacing: {swapDef.displayName}", _sSub);
                GUI.color = wp;
            }

            float hdrH = LineH * 2f + BtnH + 10f;
            if (GUI.Button(new Rect(area.x + 4f, area.y + LineH * 2f + 4f, 60f, BtnH), "← Back", _sBtnSmall))
            {
                _expertisePanelOpen = false;
                return;
            }

            float listTop = area.y + hdrH;
            float listH   = h - hdrH;
            var allExp    = _gm.Registry.Expertises.Values;
            float expCardH = LineH * 2f + BtnH + 14f;
            float innerH  = Mathf.Max(listH, allExp.Count * expCardH);

            _expertisePanelScroll = GUI.BeginScrollView(
                new Rect(area.x, listTop, w, listH),
                _expertisePanelScroll,
                new Rect(0, 0, w - 14f, innerH));

            float y = 0f;
            foreach (var exp in allExp)
            {
                // Skip already-chosen (unless it's the one being swapped out)
                bool isChosenAlready = npc.chosenExpertise.Contains(exp.expertiseId) &&
                                       exp.expertiseId != _swapTargetExpertiseId;
                if (isChosenAlready) continue;

                // Check qualification
                bool qualified = string.IsNullOrEmpty(exp.requiredSkillId)
                    || SkillSystem.GetSkillLevel(npc, exp.requiredSkillId) >= exp.requiredSkillLevel;

                DrawSolid(new Rect(2f, y, w - 16f, expCardH),
                          qualified ? new Color(0.12f, 0.15f, 0.22f, 0.9f)
                                    : new Color(0.08f, 0.09f, 0.14f, 0.9f));

                var prev = GUI.color;
                if (!qualified) GUI.color = new Color(0.5f, 0.5f, 0.5f);
                GUI.Label(new Rect(6f, y + 2f, w * 0.65f, LineH), exp.displayName, _sSub);

                if (!string.IsNullOrEmpty(exp.requiredSkillId))
                {
                    string reqText = qualified
                        ? $"Req {exp.requiredSkillId.Replace("skill.", "")} L{exp.requiredSkillLevel} ✓"
                        : $"Requires {exp.requiredSkillId.Replace("skill.", "")} level {exp.requiredSkillLevel}";
                    GUI.Label(new Rect(w * 0.66f, y + 2f, w * 0.34f - 4f, LineH), reqText, _sSub);
                }

                GUI.Label(new Rect(6f, y + 2f + LineH + 2f, w - 20f, LineH),
                          exp.description.Length > ExpertiseDescLongMaxChars + 3
                              ? exp.description.Substring(0, ExpertiseDescLongMaxChars) + "..."
                              : exp.description,
                          _sSub);
                GUI.color = prev;

                if (qualified)
                {
                    float selBtnY = y + 2f + LineH * 2f + 4f;
                    if (GUI.Button(new Rect(6f, selBtnY, 60f, BtnH), "Select", _sBtnSmall))
                    {
                        (bool ok, string msg) result;
                        if (isSwap)
                            result = _gm.Skills.SwapExpertise(npc, _swapTargetExpertiseId,
                                                              exp.expertiseId, _gm.Station);
                        else
                            result = _gm.Skills.ChooseExpertise(npc, exp.expertiseId, _gm.Station);

                        if (result.ok)
                        {
                            _expertisePanelOpen = false;
                        }
                        else
                        {
                            _gm.Station.LogEvent($"[Skills] {result.msg}");
                        }
                    }
                }

                y += expCardH + 4f;
            }

            GUI.EndScrollView();
        }

        // ── Department colour picker helpers ──────────────────────────────────

        private void OpenDeptPicker(string deptUid, string channel)
        {
            if (_deptPickerUid == deptUid && _deptPickerChannel == channel)
            {
                // Toggle off
                _deptPickerUid = "";
                return;
            }

            _deptPickerUid     = deptUid;
            _deptPickerChannel = channel;

            // Seed the picker with the existing colour if any
            var dept = _gm?.Station?.departments?.Find(d => d.uid == deptUid);
            Color? existing = channel == "secondary" ? dept?.GetSecondaryColour() : dept?.GetColour();
            Color seed = existing ?? Color.white;
            Color.RGBToHSV(seed, out _deptPickerH, out _deptPickerS, out _deptPickerV);
            _deptPickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(seed);
        }

        private void ApplyDeptPickerColour(Department dept, Color colour)
        {
            if (dept == null) return;
            string hex = "#" + ColorUtility.ToHtmlStringRGB(colour);
            if (_deptPickerChannel == "secondary")
                dept.secondaryColourHex = hex;
            else
                dept.colourHex = hex;
        }

        private void ClearDeptPickerColour(Department dept)
        {
            if (dept == null) return;
            if (_deptPickerChannel == "secondary")
                dept.secondaryColourHex = null;
            else
                dept.colourHex = null;
        }
    }
}
