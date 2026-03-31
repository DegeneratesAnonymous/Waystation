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
        private int    _ghostRotation;

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
        // Active Crew sub-tab: "roster" (more to follow in later Work Orders)
        private string                        _crewSubTab     = "roster";
        private VisualElement                 _crewTabRoot;
        private TabStrip                      _crewSubTabs;
        private VisualElement                 _crewSubContent;
        private CrewRosterSubPanelController  _crewRosterPanel;
        // Tick counter for throttling crew roster refreshes (every 5 ticks).
        private int _crewRosterTickCounter;

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
                _sidePanel.OnActiveTabChanged      -= OnSidePanelTabChanged;
                _sidePanel.OnMapFullscreenRequested -= OnSidePanelMapFullscreen;
            }

            if (_stationOverview != null)
                _stationOverview.OnDepartmentRowClicked -= OnOverviewDepartmentClicked;

            if (_buildSubPanel != null)
                _buildSubPanel.OnBuildItemSelected -= OnSubPanelBuildItemSelected;

            if (_roomsSubPanel != null)
                _roomsSubPanel.OnRoomRowClicked -= OnRoomsSubPanelRoomClicked;

            if (_crewRosterPanel != null)
                _crewRosterPanel.OnCrewRowClicked -= OnCrewRosterRowClicked;

            _networksSubPanel?.Detach();

            _inventorySubPanel?.Detach();

            if (_gm?.Rooms != null)
                _gm.Rooms.OnLayoutChanged -= OnRoomLayoutChanged;

            // Unregister the placement-cancel keyboard handler from the root element.
            if (_keyboardRoot != null)
                _keyboardRoot.UnregisterCallback<KeyDownEvent>(
                    OnKeyDownPlacementCancel, TrickleDown.TrickleDown);

            // Only destroy PanelSettings if this controller created it at runtime;
            // if it was found in the scene we don't own it.
            if (_createdPanelSettings && _uiDocument != null && _uiDocument.panelSettings != null)
                Destroy(_uiDocument.panelSettings);

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

            // Assign a default font — ScriptableObject.CreateInstance<PanelSettings>()
            // does not include one, so without this no text renders.
            var defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 12);
            var projectFont = Resources.Load<Font>("Fonts/Quango");
            if (projectFont != null)
                defaultFont = projectFont;
            panelSettings.textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();

            _uiDocument = gameObject.AddComponent<UIDocument>();
            _uiDocument.panelSettings = panelSettings;
            _createdPanelSettings = true;

            // Apply font to the root element so all children inherit it.
            _uiDocument.rootVisualElement.style.unityFontDefinition =
                FontDefinition.FromFont(defaultFont);
            _uiDocument.rootVisualElement.style.fontSize = 14;
            _uiDocument.rootVisualElement.style.color = new Color(0.85f, 0.85f, 0.9f, 1f);

            // Load the shared stylesheet so USS classes work for all panels.
            var sheet = Resources.Load<StyleSheet>("UI/Styles/WaystationComponents");
            if (sheet != null)
                _uiDocument.rootVisualElement.styleSheets.Add(sheet);

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
            // Delegate to the legacy system map open logic for now.
            var systemMap = FindFirstObjectByType<SystemMapController>();
            systemMap?.Open();
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
            }
        }

        private void OnCrewRosterRowClicked(string npcUid)
        {
            SelectCrewMemberInternal(npcUid);
        }

        // ── Event log setup (WO-UI-003) ──────────────────────────────────────

        private void BuildEventLog()
        {
            _eventLog = new EventLogController();
            // Inset from the right by the side-panel tab strip width (56 px) so
            // the log bar doesn't overlap the tab icon column.
            _eventLog.style.right = 56;
            _contentArea.Add(_eventLog);
        }

        // ── Update — sync placement state and mouse-over ──────────────────────
        private void Update()
        {
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
        /// Opens the Crew tab → Roster panel and highlights the selected NPC.
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
