// WaystationHUDController — UI Toolkit replacement for GameHUD.
//
// Activated when FeatureFlags.UseUIToolkitHUD is true.
// Self-installs in GameScene via RuntimeInitializeOnLoadMethod (same pattern as GameHUD).
//
// Callers read HUD state exclusively from GameHUD statics (IsMouseOverDrawer, InBuildMode,
// SelectCrewMember) regardless of which HUD path is active — this controller writes to
// those statics when UseUIToolkitHUD is true so CameraController and StationRoomView
// require no changes.
//
// Migration status: top bar (WO-UI-004) and side panel shell (WO-UI-005) active.
// Full panel implementations are added panel-by-panel behind this controller
// as described in WO-UI-002 onwards.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    public class WaystationHUDController : MonoBehaviour
    {
        // ── Private state ─────────────────────────────────────────────────────
        private static WaystationHUDController _instance;

        private GameManager _gm;
        private bool        _ready;

        // Placement state — mirrors GameHUD ghost-placement fields.
        private string _ghostBuildableId;
#pragma warning disable CS0414
        private int    _ghostRotation;
#pragma warning restore CS0414

        // Side panel (WO-UI-005)
        private SidePanelController _sidePanel;
        private VisualElement       _contentArea;

        // Station tab sub-panel (UI-007)
        // Active Station sub-tab: "overview" | "build" | "rooms" | "networks" | "inventory"
        private string                   _stationSubTab   = "overview";
        private VisualElement            _stationTabRoot;
        private TabStrip                 _stationSubTabs;
        private VisualElement            _stationSubContent;
        private BuildSubPanelController  _buildSubPanel;

        // Station → Rooms sub-panel (UI-008)
        private RoomsSubPanelController  _roomsSubPanel;

        // Station → Networks sub-panel (UI-009)
        private NetworksSubPanelController _networksSubPanel;

        // Station → Inventory sub-panel (UI-010)
        private InventorySubPanelController _inventorySubPanel;

        // Station overview panel (UI-006)
        private StationOverviewController _stationOverview;

        // Crew tab (UI-011)
        // Active Crew sub-tab: "roster" | "departments" | "assignments"
        private string                              _crewSubTab     = "roster";
        private VisualElement                       _crewTabRoot;
        private TabStrip                            _crewSubTabs;
        private VisualElement                       _crewSubContent;
        private CrewRosterSubPanelController        _crewRosterPanel;
        private CrewDepartmentsSubPanelController   _crewDepartmentsPanel;
        // Crew → Assignments sub-panel (UI-013).
        private CrewAssignmentsSubPanelController   _crewAssignmentsPanel;
        // Crew → Schedules sub-panel (UI-014).
        private CrewSchedulesSubPanelController     _crewSchedulesPanel;
        // Tick counter for throttling crew roster refreshes (every 5 ticks).
        private int _crewRosterTickCounter;
        // Tick counter for throttling crew departments refreshes (every 5 ticks).
        private int _crewDepartmentsTickCounter;
        // Tick counter for throttling crew assignments refreshes (every 5 ticks).
        private int _crewAssignmentsTickCounter;
        // Tick counter for throttling crew schedules refreshes (every 5 ticks).
        private int _crewSchedulesTickCounter;

        // World tab (UI-015)
        // Active World sub-tab: "factions" | "visitors"
        private string                       _worldSubTab    = "factions";
        private VisualElement                _worldTabRoot;
        private TabStrip                     _worldSubTabs;
        private VisualElement                _worldSubContent;
        // World → Factions sub-panel (UI-015).
        private FactionsSubPanelController   _factionsSubPanel;
        // Tick counter for throttling factions refreshes (every 5 ticks).
        private int _factionsTickCounter;
        // World → Visitors sub-panel (UI-016).
        private VisitorsSubPanelController   _visitorsSubPanel;
        // Tick counter for throttling visitors refreshes (every 5 ticks).
        private int _visitorsTickCounter;
        // World → Trade sub-panel (UI-017).
        private TradeSubPanelController      _tradeSubPanel;
        // Tick counter for throttling trade refreshes (every 5 ticks).
        private int _tradeTickCounter;

        // Research tab (UI-018)
        private VisualElement                 _researchTabRoot;
        private ResearchSubPanelController    _researchSubPanel;
        // Tick counter for throttling research refreshes (every 5 ticks).
        private int _researchTickCounter;

        // Map fullscreen overlay (UI-019)
        private MapSubPanelController _mapSubPanel;
        // Tick counter for throttling map refreshes (every 5 ticks).
        private int _mapTickCounter;

        // Fleet tab (UI-020)
        private VisualElement           _fleetTabRoot;
        private FleetSubPanelController _fleetSubPanel;
        // Tick counter for throttling fleet refreshes (every 5 ticks).
        private int _fleetTickCounter;

        // Top bar (WO-UI-004)
        private TopBarController _topBar;

        // Event log strip (WO-UI-003)
        private EventLogController _eventLog;

        // Shared UIDocument created on demand for all UI Toolkit panels.
        private UIDocument _uiDocument;

        // Root element on which the placement-cancel keyboard handler is registered.
        // Stored so we can unregister it in OnDestroy.
        private VisualElement _keyboardRoot;

        // True only when EnsureUIDocument() created the PanelSettings at runtime
        // (i.e., no pre-existing UIDocument was found in the scene).  Only destroy
        // it in OnDestroy when we own it, to avoid destroying shared scene assets.
        private bool _createdPanelSettings;

        // ScriptableObjects created at runtime alongside PanelSettings.
        // Kept as fields so OnDestroy can clean them up to prevent leaks.
        private ThemeStyleSheet  _runtimeTheme;
        private PanelTextSettings _runtimeTextSettings;

        // ── Auto-install ──────────────────────────────────────────────────────
        // Fires once at startup; re-registers on every scene load so the controller
        // is (re)created whenever GameScene becomes active.
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
            if (!FeatureFlags.UseUIToolkitHUD) return;   // GameHUD handles this scene
            // Reset shared GameHUD statics on each scene load to prevent stale state
            // carried over from a previous session.
            _panelsUnderPointer          = 0;
            GameHUD.IsMouseOverDrawer    = false;
            GameHUD.InBuildMode          = false;
            if (FindAnyObjectByType<WaystationHUDController>() != null) return;
            new GameObject("WaystationHUDController").AddComponent<WaystationHUDController>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            _instance = this;
            BuildMenuController.OnBuildItemSelected += OnBuildMenuItemSelected;
            BuildTopBar();
            BuildSidePanel();
            if (FeatureFlags.UseEventLogStrip)
                BuildEventLog();
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy()
        {
            _instance = null;
            BuildMenuController.OnBuildItemSelected -= OnBuildMenuItemSelected;

            _topBar?.Detach();

            // Unsubscribe from side-panel events to prevent callbacks firing on a
            // destroyed controller during scene teardown or reload.
            if (_sidePanel != null)
            {
                _sidePanel.OnActiveTabChanged       -= OnSidePanelTabChanged;
                _sidePanel.OnMapFullscreenRequested -= OnSidePanelMapFullscreen;
                _sidePanel.OnMapFullscreenExited    -= OnSidePanelMapFullscreenExited;
            }

            if (_mapSubPanel != null)
            {
                _mapSubPanel.OnCloseRequested -= OnMapSubPanelCloseRequested;
                _mapSubPanel.OnSectorUnlocked -= OnMapSubPanelSectorUnlocked;
            }

            if (_stationOverview != null)
                _stationOverview.OnDepartmentRowClicked -= OnOverviewDepartmentClicked;

            if (_buildSubPanel != null)
                _buildSubPanel.OnBuildItemSelected -= OnSubPanelBuildItemSelected;

            if (_roomsSubPanel != null)
                _roomsSubPanel.OnRoomRowClicked -= OnRoomsSubPanelRoomClicked;

            if (_crewRosterPanel != null)
                _crewRosterPanel.OnCrewRowClicked -= OnCrewRosterRowClicked;

            if (_crewAssignmentsPanel != null)
                _crewAssignmentsPanel.OnCrewRowClicked -= OnCrewAssignmentsRowClicked;

            _networksSubPanel?.Detach();

            _inventorySubPanel?.Detach();

            if (_gm?.Rooms != null)
                _gm.Rooms.OnLayoutChanged -= OnRoomLayoutChanged;

            if (_fleetSubPanel != null)
                _fleetSubPanel.OnShipRowClicked -= OnFleetShipRowClicked;

            if (_gm?.Fleet != null)
                _gm.Fleet.OnFleetChanged -= OnFleetChanged;

            if (_gm?.Factions != null)
                _gm.Factions.OnFactionRepThresholdCrossed -= OnFactionRepThresholdCrossed;

            // Unregister the placement-cancel keyboard handler from the root element.
            if (_keyboardRoot != null)
                _keyboardRoot.UnregisterCallback<KeyDownEvent>(
                    OnKeyDownPlacementCancel, TrickleDown.TrickleDown);

            // Only destroy PanelSettings if this controller created it at runtime;
            // if it was found in the scene we don't own it.
            if (_createdPanelSettings && _uiDocument != null && _uiDocument.panelSettings != null)
                Destroy(_uiDocument.panelSettings);

            // Destroy the companion ScriptableObjects created alongside PanelSettings.
            if (_runtimeTheme       != null) Destroy(_runtimeTheme);
            if (_runtimeTextSettings != null) Destroy(_runtimeTextSettings);

            if (_gm != null)
            {
                _gm.OnTick       -= OnTick;
                _gm.OnNewEvent   -= OnNewEvent;
                _gm.OnGameLoaded -= OnGameLoaded;
            }
        }

        private IEnumerator WaitForGame()
        {
            while (GameManager.Instance == null ||
                   !GameManager.Instance.IsLoaded ||
                   GameManager.Instance.Station == null)
                yield return null;

            _gm    = GameManager.Instance;
            _ready = true;

            _gm.OnTick       += OnTick;
            _gm.OnNewEvent   += OnNewEvent;
            _gm.OnGameLoaded += OnGameLoaded;

            // Initial refresh once the game state is available.
            OnGameLoaded();
        }

        // ── UIDocument provisioning ─────────────────────────────────────────

        /// <summary>
        /// Returns the UIDocument used by all HUD panels, creating one on this
        /// GameObject if none exists in the scene.
        /// </summary>
        private UIDocument EnsureUIDocument()
        {
            if (_uiDocument != null) return _uiDocument;

            _uiDocument = GetComponent<UIDocument>() ?? FindFirstObjectByType<UIDocument>();
            if (_uiDocument != null) return _uiDocument;

            // No UIDocument exists anywhere — create one on this GameObject.
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1280, 720);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;

            // Assign an empty ThemeStyleSheet to satisfy Unity's requirement and
            // prevent the "No Theme Style Sheet" panic that breaks var() processing.
            _runtimeTheme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            panelSettings.themeStyleSheet = _runtimeTheme;

            // Assign a default font — ScriptableObject.CreateInstance<PanelSettings>()
            // does not include one, so without this no text renders.
            var defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 12);
            var projectFont = Resources.Load<Font>("Fonts/Quango");
            if (projectFont != null)
                defaultFont = projectFont;
            _runtimeTextSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            panelSettings.textSettings = _runtimeTextSettings;

            _uiDocument = gameObject.AddComponent<UIDocument>();
            _uiDocument.panelSettings = panelSettings;
            _createdPanelSettings = true;

            // Apply font to the root element so all children inherit it.
            _uiDocument.rootVisualElement.style.unityFontDefinition =
                FontDefinition.FromFont(defaultFont);
            _uiDocument.rootVisualElement.style.fontSize = 14;
            _uiDocument.rootVisualElement.style.color = new Color(0.85f, 0.85f, 0.9f, 1f);

            // Load all stylesheets in dependency order so USS classes and
            // var(--ws-*) custom properties work for all panels.
            // Variables must be first so subsequent sheets can resolve var() refs.
            string[] sheetPaths = {
                "UI/Styles/WaystationVariables",
                "UI/Styles/WaystationComponents",
                "UI/Styles/Shared",
                "UI/Styles/Typography",
            };
            foreach (var path in sheetPaths)
            {
                var sheet = Resources.Load<StyleSheet>(path);
                if (sheet != null)
                    _uiDocument.rootVisualElement.styleSheets.Add(sheet);
                else
                    Debug.LogWarning($"[WaystationHUDController] StyleSheet not found in Resources: {path}");
            }

            Debug.Log("[WaystationHUDController] Created UIDocument + PanelSettings on HUD GameObject.");
            return _uiDocument;
        }

        /// <summary>
        /// Ensures the UIDocument root fills the screen and can host
        /// absolutely-positioned children.
        /// </summary>
        private void ConfigureRoot(VisualElement root)
        {
            root.style.width = Length.Percent(100);
            root.style.height = Length.Percent(100);
            root.style.flexDirection = FlexDirection.Column;
            root.style.alignItems = Align.Stretch;
        }

        // ── Top bar setup (WO-UI-004) ──────────────────────────────────────────

        private void BuildTopBar()
        {
            var doc = EnsureUIDocument();
            ConfigureRoot(doc.rootVisualElement);

            _topBar = new TopBarController();
            _topBar.RegisterClickOutside(doc.rootVisualElement);

            // Insert before the content area so it renders at the top.
            doc.rootVisualElement.Insert(0, _topBar);
        }

        // ── Side panel setup (WO-UI-005) ──────────────────────────────────────

        private void BuildSidePanel()
        {
            var doc = EnsureUIDocument();

            // Content area fills the remaining space below the top bar.
            // The side panel is absolutely positioned inside this container
            // so it automatically sits below the top bar without hardcoded offsets.
            _contentArea = new VisualElement();
            _contentArea.style.flexGrow = 1;
            _contentArea.style.position = Position.Relative;
            doc.rootVisualElement.Add(_contentArea);

            // MapSystem is not yet available at Start() time — WaitForGame() will
            // inject it once the game has finished loading (see InjectMapSystem call
            // in OnGameLoaded).
            _sidePanel = new SidePanelController();

            // Hook up the Map fullscreen callback to open SystemMapController
            _sidePanel.OnMapFullscreenRequested += OnSidePanelMapFullscreen;

            // Hook up the Map fullscreen exit callback to hide the map panel.
            _sidePanel.OnMapFullscreenExited += OnSidePanelMapFullscreenExited;

            // Mount / unmount panel content when the active tab changes.
            _sidePanel.OnActiveTabChanged += OnSidePanelTabChanged;

            // Register placement-cancel Escape handler BEFORE the side panel's own
            // keyboard handler so an active ghost placement is cancelled first.
            _keyboardRoot = doc.rootVisualElement;
            _keyboardRoot.RegisterCallback<KeyDownEvent>(
                OnKeyDownPlacementCancel, TrickleDown.TrickleDown);

            // Register keyboard handler so Escape key works for side panel
            _sidePanel.RegisterKeyboard(doc.rootVisualElement);

            _contentArea.Add(_sidePanel);
        }

        /// <summary>
        /// Intercepts the Escape key when ghost placement is active and cancels
        /// it before the side-panel's own Escape handler fires.
        /// </summary>
        private void OnKeyDownPlacementCancel(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            if (string.IsNullOrEmpty(_ghostBuildableId)) return;

            CancelPlacement();
            evt.StopPropagation();
        }

        private void OnSidePanelMapFullscreen()
        {
            // Show the UI Toolkit map overlay (UI-019).
            if (_mapSubPanel == null)
            {
                _mapSubPanel = new MapSubPanelController();
                _mapSubPanel.OnCloseRequested  += OnMapSubPanelCloseRequested;
                _mapSubPanel.OnSectorUnlocked  += OnMapSubPanelSectorUnlocked;
            }

            // Refresh with current game state before showing.
            if (_gm?.Station != null)
                _mapSubPanel.Refresh(_gm.Station, _gm?.Map);

            // Mount the overlay into the content area (above the side panel).
            if (_mapSubPanel.parent == null)
                _contentArea.Add(_mapSubPanel);

            // Also open the legacy uGUI system map layer for orbital animation.
            var systemMap = FindFirstObjectByType<SystemMapController>();
            systemMap?.Open();
        }

        private void OnMapSubPanelCloseRequested()
        {
            // Let SidePanelController exit fullscreen (fires OnMapFullscreenExited).
            _sidePanel?.HandleEscapeKey();
        }

        /// <summary>
        /// Called after <see cref="MapSystem.TryUnlockSector"/> succeeds.
        /// Applies the same post-unlock side effects as the legacy
        /// <see cref="SystemMapController"/> path, including
        /// <see cref="FactionSystem.OnSectorUnlocked"/>.
        /// </summary>
        private void OnMapSubPanelSectorUnlocked(SectorData sector)
        {
            if (_gm?.Station == null || sector == null) return;
            _gm.Factions?.OnSectorUnlocked(sector, _gm.Station);
        }

        private void OnSidePanelMapFullscreenExited()
        {
            // Hide and unmount the map overlay.
            if (_mapSubPanel != null && _mapSubPanel.parent != null)
                _contentArea.Remove(_mapSubPanel);

            // Close the legacy uGUI system map layer.
            var systemMap = FindFirstObjectByType<SystemMapController>();
            if (systemMap != null && SystemMapController.IsOpen)
                systemMap.Close();
        }

        private void OnSidePanelTabChanged(SidePanelController.Tab? tab)
        {
            // Clear all drawer content before mounting the selected tab's panel.
            // This prevents content from stacking when multiple tabs are implemented.
            _sidePanel.DrawerContentRoot.Clear();

            if (tab == SidePanelController.Tab.Station)
            {
                MountStationPanel();
            }
            else if (tab == SidePanelController.Tab.Crew)
            {
                MountCrewPanel();
            }
            else if (tab == SidePanelController.Tab.World)
            {
                MountWorldPanel();
            }
            else if (tab == SidePanelController.Tab.Research)
            {
                MountResearchPanel();
            }
            else if (tab == SidePanelController.Tab.Fleet)
            {
                MountFleetPanel();
            }
        }

        // ── Station tab mount / sub-tab switching ─────────────────────────────

        /// <summary>
        /// Creates (once) and mounts the Station tab root with its Overview and
        /// Build sub-tabs.  Re-mounts the previously active sub-tab if the panel
        /// was unmounted and remounted.
        /// </summary>
        private void MountStationPanel()
        {
            // Lazily build the shared Station tab container (sub-tab strip + content area).
            if (_stationTabRoot == null)
            {
                _stationTabRoot = new VisualElement();
                _stationTabRoot.style.flexDirection = FlexDirection.Column;
                _stationTabRoot.style.flexGrow      = 1;
                _stationTabRoot.style.height        = Length.Percent(100);

                // Create the content area BEFORE setting up the tab strip, so the
                // first-tab auto-selection during AddTab() can safely populate it.
                _stationSubContent = new VisualElement();
                _stationSubContent.style.flexGrow = 1;
                _stationSubContent.style.overflow = Overflow.Hidden;

                _stationSubTabs = new TabStrip(TabStrip.Orientation.Horizontal);
                // Subscribe OnTabSelected BEFORE AddTab() — the first AddTab() call
                // triggers SelectTab() immediately which fires OnTabSelected.
                _stationSubTabs.OnTabSelected += OnStationSubTabSelected;
                _stationSubTabs.AddTab("OVERVIEW",   "overview");
                _stationSubTabs.AddTab("BUILD",      "build");
                _stationSubTabs.AddTab("ROOMS",      "rooms");
                _stationSubTabs.AddTab("NETWORKS",   "networks");
                _stationSubTabs.AddTab("INVENTORY",  "inventory");

                _stationTabRoot.Add(_stationSubTabs);
                _stationTabRoot.Add(_stationSubContent);
            }

            _sidePanel.DrawerContentRoot.Add(_stationTabRoot);

            // Re-select the active sub-tab to restore content after the drawer was
            // reopened (DrawerContentRoot.Clear() removes child elements).
            OnStationSubTabSelected(_stationSubTab);
        }

        private void OnStationSubTabSelected(string subTabId)
        {
            _stationSubTab = subTabId;
            _stationSubContent?.Clear();

            if (_stationSubContent == null) return;

            switch (subTabId)
            {
                case "overview":
                    if (_stationOverview == null)
                    {
                        _stationOverview = new StationOverviewController();
                        _stationOverview.OnDepartmentRowClicked += OnOverviewDepartmentClicked;
                    }
                    _stationOverview.style.flexGrow = 1;
                    _stationOverview.style.height   = Length.Percent(100);
                    _stationSubContent.Add(_stationOverview);

                    if (_gm?.Station != null)
                        _stationOverview.Refresh(_gm.Station, _gm.Resources);
                    break;

                case "build":
                    if (_buildSubPanel == null)
                    {
                        _buildSubPanel = new BuildSubPanelController();
                        _buildSubPanel.OnBuildItemSelected += OnSubPanelBuildItemSelected;
                    }
                    _buildSubPanel.style.flexGrow = 1;
                    _buildSubPanel.style.height   = Length.Percent(100);
                    _stationSubContent.Add(_buildSubPanel);

                    if (_gm?.Station != null)
                        _buildSubPanel.Refresh(_gm.Station, _gm?.Building, _gm?.Inventory, _gm?.Registry);
                    break;

                case "rooms":
                    if (_roomsSubPanel == null)
                    {
                        _roomsSubPanel = new RoomsSubPanelController();
                        _roomsSubPanel.OnRoomRowClicked += OnRoomsSubPanelRoomClicked;
                    }
                    _roomsSubPanel.style.flexGrow = 1;
                    _roomsSubPanel.style.height   = Length.Percent(100);
                    _stationSubContent.Add(_roomsSubPanel);

                    if (_gm?.Station != null)
                        _roomsSubPanel.Refresh(_gm.Station, _gm?.Rooms, _gm?.Registry);
                    break;

                case "networks":
                    if (_networksSubPanel == null)
                        _networksSubPanel = new NetworksSubPanelController();
                    _networksSubPanel.style.flexGrow = 1;
                    _networksSubPanel.style.height   = Length.Percent(100);
                    _stationSubContent.Add(_networksSubPanel);

                    if (_gm?.Station != null)
                        _networksSubPanel.Refresh(_gm.Station, _gm?.UtilityNetworks);
                    break;

                case "inventory":
                    if (_inventorySubPanel == null)
                        _inventorySubPanel = new InventorySubPanelController();
                    _inventorySubPanel.style.flexGrow = 1;
                    _inventorySubPanel.style.height   = Length.Percent(100);
                    _stationSubContent.Add(_inventorySubPanel);

                    if (_gm?.Station != null)
                        _inventorySubPanel.Refresh(_gm.Station, _gm?.Inventory);
                    break;
            }
        }

        private void OnSubPanelBuildItemSelected(string categoryId, string buildableId)
        {
            if (!FeatureFlags.UseUIToolkitHUD) return;
            if (!_ready || string.IsNullOrEmpty(buildableId)) return;

            if (_gm?.Building == null || !_gm.Building.BeginPlacement(buildableId))
            {
                Debug.LogWarning($"[WaystationHUDController] Build sub-panel: cannot begin placement for '{buildableId}'.");
                return;
            }

            _ghostBuildableId   = buildableId;
            _ghostRotation      = 0;
            GameHUD.InBuildMode = true;
            Debug.Log($"[WaystationHUDController] Build sub-panel: beginning ghost placement: {buildableId}");
        }

        private void OnOverviewDepartmentClicked(string deptUid)
        {
            // Navigate to Crew → Departments for the selected department.
            // Full implementation deferred until the Crew sub-tab is migrated.
            Debug.Log($"[WaystationHUDController] Navigate to Crew→Departments: {deptUid}");
        }

        private void OnRoomsSubPanelRoomClicked(string roomId)
        {
            OpenRoomPanel(roomId);
        }

        // ── Crew tab mount / sub-tab switching (UI-011) ───────────────────────

        /// <summary>
        /// Creates (once) and mounts the Crew tab root with its Roster sub-tab.
        /// Re-mounts the previously active sub-tab if the panel was unmounted and
        /// remounted.
        /// </summary>
        private void MountCrewPanel()
        {
            if (_crewTabRoot == null)
            {
                _crewTabRoot = new VisualElement();
                _crewTabRoot.style.flexDirection = FlexDirection.Column;
                _crewTabRoot.style.flexGrow      = 1;
                _crewTabRoot.style.height        = Length.Percent(100);

                // Create the content area BEFORE setting up the tab strip so the
                // first-tab auto-selection can safely populate it.
                _crewSubContent = new VisualElement();
                _crewSubContent.style.flexGrow = 1;
                _crewSubContent.style.overflow = Overflow.Hidden;

                _crewSubTabs = new TabStrip(TabStrip.Orientation.Horizontal);
                _crewSubTabs.OnTabSelected += OnCrewSubTabSelected;
                _crewSubTabs.AddTab("ROSTER", "roster");
                _crewSubTabs.AddTab("DEPARTMENTS", "departments");
                _crewSubTabs.AddTab("ASSIGNMENTS", "assignments");
                _crewSubTabs.AddTab("SCHEDULES", "schedules");

                _crewTabRoot.Add(_crewSubTabs);
                _crewTabRoot.Add(_crewSubContent);
            }

            _sidePanel.DrawerContentRoot.Add(_crewTabRoot);

            // Re-select the active sub-tab to restore content after the drawer was
            // reopened (DrawerContentRoot.Clear() removes child elements).
            OnCrewSubTabSelected(_crewSubTab);
        }

        private void OnCrewSubTabSelected(string subTabId)
        {
            _crewSubTab = subTabId;
            _crewSubContent?.Clear();

            if (_crewSubContent == null) return;

            switch (subTabId)
            {
                case "roster":
                    if (_crewRosterPanel == null)
                    {
                        _crewRosterPanel = new CrewRosterSubPanelController();
                        _crewRosterPanel.OnCrewRowClicked += OnCrewRosterRowClicked;
                    }
                    _crewRosterPanel.style.flexGrow = 1;
                    _crewRosterPanel.style.height   = Length.Percent(100);
                    _crewSubContent.Add(_crewRosterPanel);

                    if (_gm?.Station != null)
                        _crewRosterPanel.Refresh(_gm.Station, _gm?.DeptRegistry);
                    break;

                case "departments":
                    if (_crewDepartmentsPanel == null)
                        _crewDepartmentsPanel = new CrewDepartmentsSubPanelController();
                    _crewDepartmentsPanel.style.flexGrow = 1;
                    _crewDepartmentsPanel.style.height   = Length.Percent(100);
                    _crewSubContent.Add(_crewDepartmentsPanel);

                    if (_gm?.Station != null)
                        _crewDepartmentsPanel.Refresh(
                            _gm.Station, _gm?.DeptRegistry, _gm?.Departments);
                    break;

                case "assignments":
                    if (_crewAssignmentsPanel == null)
                    {
                        _crewAssignmentsPanel = new CrewAssignmentsSubPanelController();
                        _crewAssignmentsPanel.OnCrewRowClicked += OnCrewAssignmentsRowClicked;
                    }
                    _crewAssignmentsPanel.style.flexGrow = 1;
                    _crewAssignmentsPanel.style.height   = Length.Percent(100);
                    _crewSubContent.Add(_crewAssignmentsPanel);

                    if (_gm?.Station != null)
                        _crewAssignmentsPanel.Refresh(_gm.Station, _gm?.Jobs);
                    break;

                case "schedules":
                    if (_crewSchedulesPanel == null)
                        _crewSchedulesPanel = new CrewSchedulesSubPanelController();
                    _crewSchedulesPanel.style.flexGrow = 1;
                    _crewSchedulesPanel.style.height   = Length.Percent(100);
                    _crewSubContent.Add(_crewSchedulesPanel);

                    if (_gm?.Station != null)
                        _crewSchedulesPanel.Refresh(_gm.Station, _gm?.Jobs);
                    break;
            }
        }

        private void OnCrewRosterRowClicked(string npcUid)
        {
            SelectCrewMemberInternal(npcUid);
        }

        private void OnCrewAssignmentsRowClicked(string npcUid)
        {
            SelectCrewMemberInternal(npcUid);
        }

        // ── World tab mount / sub-tab switching (UI-015) ──────────────────────

        /// <summary>
        /// Creates (once) and mounts the World tab root with its Factions sub-tab.
        /// Re-mounts the previously active sub-tab if the panel was unmounted and
        /// remounted.
        /// </summary>
        private void MountWorldPanel()
        {
            if (_worldTabRoot == null)
            {
                _worldTabRoot = new VisualElement();
                _worldTabRoot.style.flexDirection = FlexDirection.Column;
                _worldTabRoot.style.flexGrow      = 1;
                _worldTabRoot.style.height        = Length.Percent(100);

                // Create the content area BEFORE setting up the tab strip so the
                // first-tab auto-selection can safely populate it.
                _worldSubContent = new VisualElement();
                _worldSubContent.style.flexGrow = 1;
                _worldSubContent.style.overflow = Overflow.Hidden;

                _worldSubTabs = new TabStrip(TabStrip.Orientation.Horizontal);
                _worldSubTabs.OnTabSelected += OnWorldSubTabSelected;
                _worldSubTabs.AddTab("FACTIONS", "factions");
                _worldSubTabs.AddTab("VISITORS", "visitors");
                _worldSubTabs.AddTab("TRADE",    "trade");

                _worldTabRoot.Add(_worldSubTabs);
                _worldTabRoot.Add(_worldSubContent);
            }

            _sidePanel.DrawerContentRoot.Add(_worldTabRoot);

            // Re-select the active sub-tab to restore content after the drawer was
            // reopened (DrawerContentRoot.Clear() removes child elements).
            OnWorldSubTabSelected(_worldSubTab);
        }

        private void OnWorldSubTabSelected(string subTabId)
        {
            _worldSubTab = subTabId;
            _worldSubContent?.Clear();

            if (_worldSubContent == null) return;

            switch (subTabId)
            {
                case "factions":
                    if (_factionsSubPanel == null)
                    {
                        _factionsSubPanel = new FactionsSubPanelController();
                        _factionsSubPanel.OnFactionRowClicked += OnFactionRowClicked;
                    }
                    _factionsSubPanel.style.flexGrow = 1;
                    _factionsSubPanel.style.height   = Length.Percent(100);
                    _worldSubContent.Add(_factionsSubPanel);

                    if (_gm?.Station != null)
                        _factionsSubPanel.Refresh(_gm.Station, _gm?.Factions);
                    break;

                case "visitors":
                    if (_visitorsSubPanel == null)
                    {
                        _visitorsSubPanel = new VisitorsSubPanelController();
                        _visitorsSubPanel.OnShipRowClicked += OnVisitorShipRowClicked;
                        _visitorsSubPanel.OnGrantDocking    = shipId => _gm?.Visitors?.GrantDocking(shipId, _gm.Station);
                        _visitorsSubPanel.OnDenyDocking     = shipId => _gm?.Visitors?.DenyDocking(shipId, _gm.Station);
                        _visitorsSubPanel.OnNegotiateDocking = shipId => _gm?.Visitors?.NegotiateDocking(shipId, _gm.Station);
                    }
                    _visitorsSubPanel.style.flexGrow = 1;
                    _visitorsSubPanel.style.height   = Length.Percent(100);
                    _worldSubContent.Add(_visitorsSubPanel);

                    if (_gm?.Station != null)
                        _visitorsSubPanel.Refresh(_gm.Station, _gm?.Visitors);
                    break;

                case "trade":
                    if (_tradeSubPanel == null)
                        _tradeSubPanel = new TradeSubPanelController();
                    _tradeSubPanel.style.flexGrow = 1;
                    _tradeSubPanel.style.height   = Length.Percent(100);
                    _worldSubContent.Add(_tradeSubPanel);

                    if (_gm?.Station != null)
                        _tradeSubPanel.Refresh(_gm.Station, _gm?.Trade);
                    break;
            }
        }

        private void OnFactionRowClicked(string factionId)
        {
            // Faction Detail panel will be implemented in a separate Work Order (WO-UI-026).
            Debug.Log($"[WaystationHUDController] OpenFactionDetail: {factionId}");
        }

        private void OnVisitorShipRowClicked(string shipUid)
        {
            // Visiting Ship contextual panel will be implemented in a separate Work Order.
            // For now, log the selection so callers have feedback.
            Debug.Log($"[WaystationHUDController] OpenVisitorShipPanel: {shipUid}");
        }

        private void OnFactionRepThresholdCrossed(string factionId, float oldRep, float newRep)
        {
            // Refresh the factions panel immediately when a reputation threshold is crossed
            // so the tier label and meter update without waiting for the next tick.
            bool worldTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.World;
            if (_factionsSubPanel != null && worldTabActive && _worldSubTab == "factions" &&
                _gm?.Station != null)
            {
                _factionsSubPanel.Refresh(_gm.Station, _gm?.Factions);
            }
        }

        // ── Research tab mount / sub-tab switching (UI-018) ───────────────────

        /// <summary>
        /// Creates (once) and mounts the Research tab root with its five branch sub-tabs.
        /// Re-mounts the previously active sub-tab if the panel was unmounted and
        /// remounted.
        /// </summary>
        private void MountResearchPanel()
        {
            if (_researchTabRoot == null)
            {
                _researchTabRoot = new VisualElement();
                _researchTabRoot.style.flexDirection = FlexDirection.Column;
                _researchTabRoot.style.flexGrow      = 1;
                _researchTabRoot.style.height        = Length.Percent(100);
                _researchTabRoot.style.overflow      = Overflow.Hidden;

                // The research sub-panel handles its own internal branch tabs so
                // we mount it once here and drive it via Refresh().
                _researchSubPanel = new ResearchSubPanelController();
                _researchSubPanel.style.flexGrow = 1;
                _researchSubPanel.style.height   = Length.Percent(100);
                _researchTabRoot.Add(_researchSubPanel);
            }

            _sidePanel.DrawerContentRoot.Add(_researchTabRoot);

            // Refresh immediately with current game state.
            if (_gm?.Station != null)
                _researchSubPanel.Refresh(_gm.Station, _gm?.Research);
        }

        private void BuildEventLog()
        {
            _eventLog = new EventLogController();
            // Inset from the right by the side-panel tab strip width (56 px) so
            // the log bar doesn't overlap the tab icon column.
            _eventLog.style.right = 52; // matches tab strip width
            _contentArea.Add(_eventLog);
        }

        // ── Fleet tab mount (UI-020) ──────────────────────────────────────────

        /// <summary>
        /// Creates (once) and mounts the Fleet tab root.
        /// Re-mounts if the panel was unmounted and remounted.
        /// </summary>
        private void MountFleetPanel()
        {
            if (_fleetTabRoot == null)
            {
                _fleetTabRoot = new VisualElement();
                _fleetTabRoot.style.flexDirection = FlexDirection.Column;
                _fleetTabRoot.style.flexGrow      = 1;
                _fleetTabRoot.style.height        = Length.Percent(100);
                _fleetTabRoot.style.overflow      = Overflow.Hidden;

                _fleetSubPanel = new FleetSubPanelController();
                _fleetSubPanel.style.flexGrow = 1;
                _fleetSubPanel.style.height   = Length.Percent(100);
                _fleetSubPanel.OnShipRowClicked += OnFleetShipRowClicked;
                _fleetSubPanel.OnRescueDispatch  = OnFleetRescueDispatch;
                _fleetTabRoot.Add(_fleetSubPanel);
            }

            _sidePanel.DrawerContentRoot.Add(_fleetTabRoot);

            if (_gm?.Station != null)
                _fleetSubPanel.Refresh(_gm.Station, _gm?.Fleet);
        }

        private void OnFleetShipRowClicked(string shipUid)
        {
            // Ship Detail sub-panel is shown inline by FleetSubPanelController.
            // Log for diagnostic purposes.
            Debug.Log($"[WaystationHUDController] FleetShipRowClicked: {shipUid}");
        }

        private void OnFleetRescueDispatch(string shipUid)
        {
            // Dispatch sub-tab (WO-UI-021) is out of scope for this Work Order.
            // Log for now so the callback path can be verified.
            Debug.Log($"[WaystationHUDController] FleetRescueDispatch: {shipUid}");
        }

        private void OnFleetChanged()
        {
            bool fleetTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Fleet;
            if (_fleetSubPanel != null && fleetTabActive && _gm?.Station != null)
                _fleetSubPanel.Refresh(_gm.Station, _gm?.Fleet);
        }

        // ── Update — sync placement state and mouse-over ──────────────────────
        private void Update()
        {
            // Space bar toggles pause (fallback path if the UI button fails to register).
            if (_ready && _gm != null && Input.GetKeyDown(KeyCode.Space))
            {
                _gm.IsPaused = !_gm.IsPaused;
                _topBar?.RefreshSpeedButtons();
            }

            // Keep the shared GameHUD.InBuildMode in sync with the ghost placement state.
            GameHUD.InBuildMode = !string.IsNullOrEmpty(_ghostBuildableId);

            // Keep IsMouseOverDrawer in sync: OR the side panel, event log strip,
            // and any other registered HUD panel pointer states.
            bool sidePanelOver = _sidePanel != null && _sidePanel.IsMouseOverPanel;
            bool eventLogOver  = _eventLog  != null && _eventLog.IsMouseOverStrip;
            GameHUD.IsMouseOverDrawer = sidePanelOver || eventLogOver || _panelsUnderPointer > 0;
        }

        // ── GameManager event handlers ────────────────────────────────────────

        private void OnTick(StationState station)
        {
            _topBar?.OnTick(station);
            _eventLog?.OnTick(station?.tick ?? 0);

            bool stationTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Station;

            // Refresh the station overview whenever it is mounted.
            if (_stationOverview != null && stationTabActive && _stationSubTab == "overview")
                _stationOverview.Refresh(station, _gm?.Resources);

            // Refresh the build queue whenever the Build sub-tab is mounted.
            if (_buildSubPanel != null && stationTabActive && _stationSubTab == "build")
                _buildSubPanel.Refresh(station, _gm?.Building, _gm?.Inventory, _gm?.Registry);

            // Refresh the rooms list whenever the Rooms sub-tab is mounted.
            if (_roomsSubPanel != null && stationTabActive && _stationSubTab == "rooms")
                _roomsSubPanel.Refresh(station, _gm?.Rooms, _gm?.Registry);

            // Refresh the networks panel whenever the Networks sub-tab is mounted.
            if (_networksSubPanel != null && stationTabActive && _stationSubTab == "networks")
                _networksSubPanel.Refresh(station, _gm?.UtilityNetworks);

            // Refresh the inventory panel whenever the Inventory sub-tab is mounted.
            if (_inventorySubPanel != null && stationTabActive && _stationSubTab == "inventory")
                _inventorySubPanel.Refresh(station, _gm?.Inventory);

            // Refresh the crew roster panel every 5 ticks to avoid GC churn.
            bool crewTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Crew;
            if (_crewRosterPanel != null && crewTabActive && _crewSubTab == "roster")
            {
                _crewRosterTickCounter++;
                if (_crewRosterTickCounter >= 5)
                {
                    _crewRosterTickCounter = 0;
                    _crewRosterPanel.Refresh(station, _gm?.DeptRegistry);
                }
            }

            // Refresh the departments panel every 5 ticks to avoid GC churn.
            if (_crewDepartmentsPanel != null && crewTabActive && _crewSubTab == "departments")
            {
                _crewDepartmentsTickCounter++;
                if (_crewDepartmentsTickCounter >= 5)
                {
                    _crewDepartmentsTickCounter = 0;
                    _crewDepartmentsPanel.Refresh(station, _gm?.DeptRegistry, _gm?.Departments);
                }
            }

            // Refresh the assignments panel every 5 ticks to avoid GC churn.
            if (_crewAssignmentsPanel != null && crewTabActive && _crewSubTab == "assignments")
            {
                _crewAssignmentsTickCounter++;
                if (_crewAssignmentsTickCounter >= 5)
                {
                    _crewAssignmentsTickCounter = 0;
                    _crewAssignmentsPanel.Refresh(station, _gm?.Jobs);
                }
            }

            // Refresh the schedules panel every 5 ticks to avoid GC churn.
            if (_crewSchedulesPanel != null && crewTabActive && _crewSubTab == "schedules")
            {
                _crewSchedulesTickCounter++;
                if (_crewSchedulesTickCounter >= 5)
                {
                    _crewSchedulesTickCounter = 0;
                    _crewSchedulesPanel.Refresh(station, _gm?.Jobs);
                }
            }

            // Refresh the factions panel every 5 ticks to avoid GC churn.
            bool worldTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.World;
            if (_factionsSubPanel != null && worldTabActive && _worldSubTab == "factions")
            {
                _factionsTickCounter++;
                if (_factionsTickCounter >= 5)
                {
                    _factionsTickCounter = 0;
                    _factionsSubPanel.Refresh(station, _gm?.Factions);
                }
            }

            // Refresh the visitors panel every 5 ticks to avoid GC churn.
            if (_visitorsSubPanel != null && worldTabActive && _worldSubTab == "visitors")
            {
                _visitorsTickCounter++;
                if (_visitorsTickCounter >= 5)
                {
                    _visitorsTickCounter = 0;
                    _visitorsSubPanel.Refresh(station, _gm?.Visitors);
                }
            }

            // Refresh the trade panel every 5 ticks to avoid GC churn.
            if (_tradeSubPanel != null && worldTabActive && _worldSubTab == "trade")
            {
                _tradeTickCounter++;
                if (_tradeTickCounter >= 5)
                {
                    _tradeTickCounter = 0;
                    _tradeSubPanel.Refresh(station, _gm?.Trade);
                }
            }

            // Refresh the research panel every 5 ticks to avoid GC churn.
            bool researchTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Research;
            if (_researchSubPanel != null && researchTabActive)
            {
                _researchTickCounter++;
                if (_researchTickCounter >= 5)
                {
                    _researchTickCounter = 0;
                    _researchSubPanel.Refresh(station, _gm?.Research);
                }
            }

            // Refresh the map panel every 5 ticks while it is mounted (map fullscreen active).
            if (_mapSubPanel != null && _mapSubPanel.parent != null && _gm?.Map?.IsFullscreenActive == true)
            {
                _mapTickCounter++;
                if (_mapTickCounter >= 5)
                {
                    _mapTickCounter = 0;
                    _mapSubPanel.Refresh(station, _gm?.Map);
                }
            }

            // Refresh the fleet panel every 5 ticks to avoid GC churn.
            bool fleetTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Fleet;
            if (_fleetSubPanel != null && fleetTabActive)
            {
                _fleetTickCounter++;
                if (_fleetTickCounter >= 5)
                {
                    _fleetTickCounter = 0;
                    _fleetSubPanel.Refresh(station, _gm?.Fleet);
                }
            }
        }

        private void OnNewEvent(PendingEvent pending)
        {
            if (pending?.definition == null) return;
            // Map the event definition to a log category and add to the buffer.
            // EventLogController auto-subscribes to EventLogBuffer.OnBufferChanged
            // so no manual call to OnBufferChanged is needed here.
            var category = pending.definition.hostile ? LogCategory.Alert : LogCategory.World;
            EventLogBuffer.Instance.Add(category, pending.definition.description ?? pending.definition.id);
        }

        private void OnGameLoaded()
        {
            // Inject the live MapSystem now that the game has finished loading.
            // BuildSidePanel() runs before WaitForGame() completes, so MapSystem
            // was null at construction time.
            _sidePanel?.InjectMapSystem(_gm?.Map);

            // Subscribe to RoomSystem.OnLayoutChanged so the Rooms sub-panel
            // refreshes whenever a layout rebuild completes (wall/door placement,
            // workbench completion, etc.) without waiting for the next OnTick.
            // Unsubscribe first to ensure idempotency across multiple OnGameLoaded
            // calls (bootstrap, new game, load game).
            if (_gm?.Rooms != null)
            {
                _gm.Rooms.OnLayoutChanged -= OnRoomLayoutChanged;
                _gm.Rooms.OnLayoutChanged += OnRoomLayoutChanged;
            }

            // Subscribe to ShipSystem.OnFleetChanged so the Fleet panel refreshes
            // immediately when fleet state changes (ship dispatched, repair started, etc.).
            // Unsubscribe first to guard against double-subscription across
            // multiple OnGameLoaded calls.
            if (_gm?.Fleet != null)
            {
                _gm.Fleet.OnFleetChanged -= OnFleetChanged;
                _gm.Fleet.OnFleetChanged += OnFleetChanged;
            }

            // Subscribe to FactionSystem.OnFactionRepThresholdCrossed so the
            // Factions panel refreshes immediately when a rep band changes.
            // Unsubscribe first to guard against double-subscription across
            // multiple OnGameLoaded calls.
            if (_gm?.Factions != null)
            {
                _gm.Factions.OnFactionRepThresholdCrossed -= OnFactionRepThresholdCrossed;
                _gm.Factions.OnFactionRepThresholdCrossed += OnFactionRepThresholdCrossed;
            }

            // Set the initial view context to the station name and inject dependencies
            // into the top bar once the game state is available.
            if (_gm?.Station != null)
            {
                ViewContextManager.Instance.SetContext(_gm.Station.stationName);
                _topBar?.InjectDependencies(_gm, LogEntryBuffer.Instance, ViewContextManager.Instance);

                // Wire navigation callbacks into the event log strip.
                // The navigation methods (SelectCrewMemberInternal, etc.) are already
                // defined on this class — pass static-compatible lambdas.
                _eventLog?.InjectNavigationCallbacks(
                    selectCrewMember:   uid => SelectCrewMemberInternal(uid),
                    openRoomPanel:      id  => OpenRoomPanel(id),
                    openNetworkOverlay: ()  => OpenNetworkOverlay(),
                    openVisitorPanel:   id  => OpenVisitorPanel(id),
                    openFleetPanel:     uid => OpenFleetPanel(uid));

                OnTick(_gm.Station);
            }
        }

        private void OnRoomLayoutChanged()
        {
            bool stationTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Station;
            if (_roomsSubPanel != null && stationTabActive && _stationSubTab == "rooms" &&
                _gm?.Station != null)
            {
                _roomsSubPanel.Refresh(_gm.Station, _gm.Rooms, _gm?.Registry);
            }
        }

        // ── Cross-view public API ─────────────────────────────────────────────

        /// <summary>
        /// Called by GameHUD.SelectCrewMember when UseUIToolkitHUD is true.
        /// Ensures the Crew tab is open so the roster is visible.
        /// Crew Detail contextual panel navigation will be implemented in a future Work Order.
        /// </summary>
        internal static void SelectCrewMemberInternal(string npcUid)
        {
            if (_instance == null || string.IsNullOrEmpty(npcUid)) return;

            // Ensure the Crew tab is open and the Roster sub-tab is active.
            if (_instance._sidePanel?.ActiveTab != SidePanelController.Tab.Crew)
                _instance._sidePanel?.ActivateTab(SidePanelController.Tab.Crew);

            // Crew Detail panel will be implemented in a future Work Order.
            // For now, log the selection so callers have feedback.
            Debug.Log($"[WaystationHUDController] SelectCrewMember: {npcUid}");
        }

        private static void OpenRoomPanel(string roomId)
        {
            if (_instance == null || string.IsNullOrEmpty(roomId)) return;
            Debug.Log($"[WaystationHUDController] OpenRoomPanel: {roomId}");
        }

        private static void OpenNetworkOverlay()
        {
            if (_instance == null) return;
            Debug.Log("[WaystationHUDController] OpenNetworkOverlay");
        }

        private static void OpenVisitorPanel(string visitorId)
        {
            if (_instance == null) return;
            Debug.Log($"[WaystationHUDController] OpenVisitorPanel: {visitorId}");
        }

        private static void OpenFleetPanel(string shipUid)
        {
            if (_instance == null) return;

            // Ensure the Fleet tab is open.
            if (_instance._sidePanel?.ActiveTab != SidePanelController.Tab.Fleet)
                _instance._sidePanel?.ActivateTab(SidePanelController.Tab.Fleet);

            Debug.Log($"[WaystationHUDController] OpenFleetPanel: {shipUid}");
        }

        // ── Mouse-over helpers (called from panel pointer callbacks) ──────────

        // Reference count: number of HUD panels currently under the pointer.
        // Using a count rather than a simple bool prevents one panel's PointerLeave
        // from clearing the flag while the pointer is still over a sibling panel.
        private static int _panelsUnderPointer;

        /// <summary>
        /// Notify the controller that the pointer has entered a HUD panel.
        /// Call from each panel's PointerEnterEvent callback.
        /// </summary>
        public static void NotifyPointerEnterPanel()
        {
            _panelsUnderPointer++;
            GameHUD.IsMouseOverDrawer = _panelsUnderPointer > 0;
        }

        /// <summary>
        /// Notify the controller that the pointer has left a HUD panel.
        /// Call from each panel's PointerLeaveEvent callback.
        /// </summary>
        public static void NotifyPointerLeavePanel()
        {
            _panelsUnderPointer = Mathf.Max(0, _panelsUnderPointer - 1);
            GameHUD.IsMouseOverDrawer = _panelsUnderPointer > 0;
        }

        // ── Build placement ───────────────────────────────────────────────────

        private void OnBuildMenuItemSelected(string categoryId, string buildableId)
        {
            if (!FeatureFlags.UseUIToolkitHUD) return;
            if (!_ready || string.IsNullOrEmpty(buildableId)) return;
            if (_gm?.Registry?.Buildables == null) return;
            if (!_gm.Registry.Buildables.ContainsKey(buildableId))
            {
                Debug.LogWarning($"[WaystationHUDController] Build item '{buildableId}' not found in registry.");
                return;
            }

            // Wire into the placement system so the ghost-placement renderer and
            // any other consumers can read BuildingSystem.PendingPlacementId.
            _gm.Building.BeginPlacement(buildableId);

            _ghostBuildableId     = buildableId;
            _ghostRotation        = 0;
            GameHUD.InBuildMode   = true;
            Debug.Log($"[WaystationHUDController] Beginning ghost placement: {buildableId}");
        }

        /// <summary>Cancels the current ghost placement session.</summary>
        public void CancelPlacement()
        {
            _gm?.Building?.EndPlacement();
            _ghostBuildableId   = null;
            _ghostRotation      = 0;
            GameHUD.InBuildMode = false;
        }
    }
}
