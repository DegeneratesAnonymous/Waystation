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
using System.Collections.Generic;
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

        // Fleet tab (UI-020 / UI-021)
        // Active Fleet sub-tab: "overview" | "dispatch" | "shipyard"
        private string                      _fleetSubTab    = "overview";
        private VisualElement               _fleetTabRoot;
        private TabStrip                    _fleetSubTabs;
        private VisualElement               _fleetSubContent;
        private FleetSubPanelController     _fleetSubPanel;
        private DispatchSubPanelController  _dispatchSubPanel;
        private ShipyardSubPanelController  _shipyardSubPanel;
        // Tick counter for throttling fleet refreshes (every 5 ticks).
        private int _fleetTickCounter;
        // Tick counter for throttling dispatch refreshes (every 5 ticks).
        private int _dispatchTickCounter;
        // Tick counter for throttling shipyard refreshes (every 5 ticks).
        private int _shipyardTickCounter;

        // Settings tab (UI-022)
        private SettingsSubPanelController _settingsPanel;

        // Crew Member contextual panel (UI-023)
        private CrewMemberPanelController _crewMemberPanel;
        // Tick counter for throttling crew member panel refreshes (every 5 ticks).
        private int _crewMemberTickCounter;
        // Stack of open crew member panel NPC uids (for stacking behaviour).
        // Only the top-most panel (last in list) is shown; others are held in memory.
        private readonly List<string> _crewMemberStack = new List<string>();

        // Room contextual panel (UI-024)
        private RoomPanelController _roomPanel;
        // Room key currently shown in the room panel, or null when closed.
        private string _roomPanelRoomId;
        // Tick counter for throttling room panel refreshes (every 5 ticks).
        private int _roomPanelTickCounter;

        // Visiting Ship contextual panel (UI-026)
        private VisitingShipPanelController _visitingShipPanel;
        // Ship uid currently shown in the visiting ship panel, or null when closed.
        private string _visitingShipPanelUid;
        // Tick counter for throttling visiting ship panel refreshes (every 5 ticks).
        private int _visitingShipTickCounter;

        // Faction Detail contextual panel (UI-026)
        private FactionDetailPanelController _factionDetailPanel;
        // Faction id currently shown in the faction detail panel, or null when closed.
        private string _factionDetailPanelId;
        // Tick counter for throttling faction detail panel refreshes (every 5 ticks).
        private int _factionDetailTickCounter;
        // Workbench contextual panel (UI-025)
        private WorkbenchPanelController _workbenchPanel;
        // Foundation uid currently shown in the workbench panel, or null when closed.
        private string _workbenchPanelFoundationUid;
        // Tick counter for throttling workbench panel refreshes (5 ticks when Queue
        // tab is not active; every tick when the Queue tab is active so the progress
        // bar decrements smoothly).
        private int _workbenchPanelTickCounter;

        // Top bar (WO-UI-004)
        private TopBarController _topBar;

        // Event log strip (WO-UI-003)
        private EventLogController _eventLog;
        private bool _wasPausedLastFrame;
        private DrawerPanel _eventInfoDrawer;
        private Label _eventInfoTitleLabel;
        private Label _eventInfoMetaLabel;
        private Label _eventInfoBodyLabel;
        private Label _eventInfoNavLabel;

        // Event / decision modal (UI-027)
        private ModalOverlay _eventDecisionModal;
        private bool _eventModalPauseCaptured;
        private bool _eventModalPriorPaused;
        private float _eventModalPriorTicksPerSecond = 1f;

        // Expertise slot unlock prompt queue (UI-028)
        private struct ExpertisePromptEntry
        {
            public NPCInstance npc;
            public string skillId;
            public int skillLevel;
        }
        private readonly Queue<ExpertisePromptEntry> _expertisePromptQueue = new Queue<ExpertisePromptEntry>();
        private ExpertiseSlotPrompt _activeExpertisePrompt;
        private bool _expertisePauseCaptured;
        private bool _expertisePriorPaused;
        private float _expertisePriorTicksPerSecond = 1f;

        // Departure warning panel queue (UI-030)
        private struct DepartureWarningEntry
        {
            public NPCInstance npc;
            public int deadlineTick;
            public int announcedAtTick;
        }
        private readonly Queue<DepartureWarningEntry> _departureWarningQueue = new Queue<DepartureWarningEntry>();
        private DepartureWarningPanel _activeDepartureWarning;
        private int _departureOutcomeDismissCountdown = -1;

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

            if (_crewMemberPanel != null)
            {
                _crewMemberPanel.OnCloseRequested        -= OnCrewMemberPanelClosed;
                _crewMemberPanel.OnRelationshipRowClicked -= OnCrewMemberRelationshipRowClicked;
                _crewMemberPanel.OnExpertiseSlotClicked  -= OnCrewMemberExpertiseSlotClicked;
            }

            if (_roomPanel != null)
            {
                _roomPanel.OnCloseRequested      -= OnRoomPanelClosed;
                _roomPanel.OnWorkbenchRowClicked -= OnRoomPanelWorkbenchClicked;
            }

            if (_visitingShipPanel != null)
            {
                _visitingShipPanel.OnCloseRequested   -= OnVisitingShipPanelClosed;
                _visitingShipPanel.OnGrantDocking      -= OnVisitingShipGrantDocking;
                _visitingShipPanel.OnDenyDocking       -= OnVisitingShipDenyDocking;
                _visitingShipPanel.OnNegotiateDocking  -= OnVisitingShipNegotiateDocking;
                _visitingShipPanel.OnRequestDeparture  -= OnVisitingShipRequestDeparture;
                _visitingShipPanel.OnOpenTradeManifest -= OnVisitingShipOpenTradeManifest;
            }

            if (_factionDetailPanel != null)
                _factionDetailPanel.OnCloseRequested -= OnFactionDetailPanelClosed;
            if (_workbenchPanel != null)
                _workbenchPanel.OnCloseRequested -= OnWorkbenchPanelClosed;

            _networksSubPanel?.Detach();

            _inventorySubPanel?.Detach();

            if (_gm?.Rooms != null)
                _gm.Rooms.OnLayoutChanged -= OnRoomLayoutChanged;

            if (_fleetSubPanel != null)
                _fleetSubPanel.OnShipRowClicked -= OnFleetShipRowClicked;

            if (_fleetSubTabs != null)
                _fleetSubTabs.OnTabSelected -= OnFleetSubTabSelected;

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
                _gm.OnDepartureWarning -= OnDepartureWarning;
                if (_gm.Skills != null)
                    _gm.Skills.OnSlotEarned -= OnExpertiseSlotEarned;
            }

            if (_eventLog != null)
            {
                _eventLog.OnNotificationClicked -= OnEventNotificationClicked;
                _eventLog.OnEntryClicked -= OnEventNotificationClicked;
                _eventLog.UnregisterCallback<ClickEvent>(OnEventLogStripFallbackClick);
            }

            RemoveEventDecisionModal(restoreSpeed: false);
            RemoveActiveExpertisePrompt(restoreSpeed: false);
            _expertisePromptQueue.Clear();
            RemoveActiveDepartureWarning();
            _departureWarningQueue.Clear();
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
            if (_gm.Skills != null)
                _gm.Skills.OnSlotEarned += OnExpertiseSlotEarned;
            _gm.OnDepartureWarning += OnDepartureWarning;

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
            // Research is rendered as a fullscreen overlay (like Map). Remove it
            // whenever any other tab (or no tab) becomes active.
            if (tab != SidePanelController.Tab.Research &&
                _researchTabRoot != null && _researchTabRoot.parent != null)
            {
                _contentArea.Remove(_researchTabRoot);
            }

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
            else if (tab == SidePanelController.Tab.Settings)
            {
                MountSettingsPanel();
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
            OpenFactionDetailPanelInternal(factionId);
        }

        private void OnVisitorShipRowClicked(string shipUid)
        {
            OpenVisitingShipPanelInternal(shipUid);
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
                _researchTabRoot.style.position      = Position.Absolute;
                _researchTabRoot.style.top           = 0;
                _researchTabRoot.style.left          = 0;
                _researchTabRoot.style.right         = 52; // keep side icon strip visible
                _researchTabRoot.style.bottom        = 32; // keep event-log strip visible
                _researchTabRoot.style.flexDirection = FlexDirection.Column;
                _researchTabRoot.style.overflow      = Overflow.Hidden;
                _researchTabRoot.style.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 0.97f);
                _researchTabRoot.style.borderLeftWidth = 1;
                _researchTabRoot.style.borderLeftColor = new Color(0.13f, 0.17f, 0.25f, 1f);

                // The research sub-panel handles its own internal branch tabs so
                // we mount it once here and drive it via Refresh().
                _researchSubPanel = new ResearchSubPanelController();
                _researchSubPanel.style.flexGrow = 1;
                _researchSubPanel.style.height   = Length.Percent(100);
                _researchTabRoot.Add(_researchSubPanel);
            }

            if (_researchTabRoot.parent == null)
                _contentArea.Add(_researchTabRoot);

            // Refresh immediately with current game state.
            if (_gm?.Station != null)
                _researchSubPanel.Refresh(_gm.Station, _gm?.Research);
        }

        private void BuildEventLog()
        {
            _eventLog = new EventLogController();
            _eventLog.OnNotificationClicked += OnEventNotificationClicked;
            _eventLog.OnEntryClicked += OnEventNotificationClicked;
            // Fallback path: when collapsed, any click on the strip opens details.
            _eventLog.RegisterCallback<ClickEvent>(OnEventLogStripFallbackClick);
            _contentArea.Add(_eventLog);

            EnsureEventInfoDrawer();
        }

        private void EnsureEventInfoDrawer()
        {
            if (_eventInfoDrawer != null) return;

            _eventInfoDrawer = new DrawerPanel(DrawerPanel.Direction.Horizontal);
            _eventInfoDrawer.style.position = Position.Absolute;
            _eventInfoDrawer.style.left = 0;
            _eventInfoDrawer.style.top = 32;      // keep top bar visible
            _eventInfoDrawer.style.bottom = 32;   // keep bottom event strip visible
            _eventInfoDrawer.style.width = 320;
            _eventInfoDrawer.style.maxWidth = 0;
            _eventInfoDrawer.style.display = DisplayStyle.Flex;
            _eventInfoDrawer.style.backgroundColor = new Color(0.06f, 0.09f, 0.14f, 0.98f);
            _eventInfoDrawer.style.borderRightWidth = 1;
            _eventInfoDrawer.style.borderRightColor = new Color(0.15f, 0.22f, 0.32f, 1f);
            _eventInfoDrawer.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 10;
            header.style.paddingRight = 6;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.15f, 0.22f, 0.32f, 1f);
            header.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 1f);

            _eventInfoTitleLabel = new Label("EVENT DETAILS");
            _eventInfoTitleLabel.style.flexGrow = 1;
            _eventInfoTitleLabel.style.color = new Color(0.70f, 0.84f, 1f, 1f);
            _eventInfoTitleLabel.style.fontSize = 12;
            _eventInfoTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(_eventInfoTitleLabel);

            var closeBtn = new Button(() => _eventInfoDrawer.Close()) { text = "✕" };
            closeBtn.style.width = 20;
            closeBtn.style.height = 20;
            closeBtn.style.backgroundColor = StyleKeyword.Null;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.color = new Color(0.46f, 0.60f, 0.78f, 0.95f);
            closeBtn.style.fontSize = 10;
            header.Add(closeBtn);

            _eventInfoDrawer.Add(header);

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1;
            content.style.paddingLeft = 10;
            content.style.paddingRight = 10;
            content.style.paddingTop = 8;
            content.style.paddingBottom = 8;

            _eventInfoMetaLabel = new Label();
            _eventInfoMetaLabel.style.color = new Color(0.50f, 0.66f, 0.84f, 0.95f);
            _eventInfoMetaLabel.style.fontSize = 9;
            _eventInfoMetaLabel.style.marginBottom = 6;
            content.Add(_eventInfoMetaLabel);

            _eventInfoBodyLabel = new Label();
            _eventInfoBodyLabel.style.whiteSpace = WhiteSpace.Normal;
            _eventInfoBodyLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            _eventInfoBodyLabel.style.color = new Color(0.84f, 0.90f, 1f, 1f);
            _eventInfoBodyLabel.style.fontSize = 11;
            _eventInfoBodyLabel.style.marginBottom = 8;
            content.Add(_eventInfoBodyLabel);

            _eventInfoNavLabel = new Label();
            _eventInfoNavLabel.style.whiteSpace = WhiteSpace.Normal;
            _eventInfoNavLabel.style.color = new Color(0.44f, 0.76f, 1f, 1f);
            _eventInfoNavLabel.style.fontSize = 9;
            _eventInfoNavLabel.style.display = DisplayStyle.None;
            content.Add(_eventInfoNavLabel);

            _eventInfoDrawer.Add(content);
            _contentArea.Add(_eventInfoDrawer);
        }

        private void OnEventLogStripFallbackClick(ClickEvent evt)
        {
            if (_eventLog == null || _eventLog.IsExpanded) return;
            var entry = _eventLog.CurrentPreviewEntry ?? EventLogBuffer.Instance.GetCollapsedEntry();
            if (entry == null) return;
            OnEventNotificationClicked(entry);
        }

        private void OnEventNotificationClicked(LogEntry entry)
        {
            if (entry == null) return;

            EnsureEventInfoDrawer();

            string cat = entry.Category switch
            {
                LogCategory.Alert => "ALERT",
                LogCategory.Crew => "CREW",
                LogCategory.Station => "STATION",
                LogCategory.World => "WORLD",
                _ => "EVENT",
            };

            _eventInfoTitleLabel.text = $"{cat} EVENT";
            _eventInfoMetaLabel.text = $"Tick {entry.TickFired:N0}";
            _eventInfoBodyLabel.text = entry.BodyText ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(entry.NavigateLabel))
            {
                _eventInfoNavLabel.text = $"Related action: {entry.NavigateLabel}";
                _eventInfoNavLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _eventInfoNavLabel.style.display = DisplayStyle.None;
                _eventInfoNavLabel.text = string.Empty;
            }

            _eventInfoDrawer.Open();
            _eventInfoDrawer.style.maxWidth = 360;
            _eventInfoDrawer.style.visibility = Visibility.Visible;
            _eventInfoDrawer.style.opacity = 1;
            _eventInfoDrawer.BringToFront();
        }

        // ── Fleet tab mount (UI-020 / UI-021) ────────────────────────────────

        /// <summary>
        /// Creates (once) and mounts the Fleet tab root with its Overview, Dispatch,
        /// and Shipyard sub-tabs.  Re-mounts the previously active sub-tab if the
        /// panel was unmounted and remounted.
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

                // Create content area BEFORE tab strip so the first AddTab auto-select
                // can safely populate it.
                _fleetSubContent = new VisualElement();
                _fleetSubContent.style.flexGrow = 1;
                _fleetSubContent.style.overflow = Overflow.Hidden;

                _fleetSubTabs = new TabStrip(TabStrip.Orientation.Horizontal);
                _fleetSubTabs.OnTabSelected += OnFleetSubTabSelected;
                _fleetSubTabs.AddTab("OVERVIEW",  "overview");
                _fleetSubTabs.AddTab("DISPATCH",  "dispatch");
                _fleetSubTabs.AddTab("SHIPYARD",  "shipyard");

                _fleetTabRoot.Add(_fleetSubTabs);
                _fleetTabRoot.Add(_fleetSubContent);
            }

            _sidePanel.DrawerContentRoot.Add(_fleetTabRoot);

            // Re-select the active sub-tab to restore content after the drawer was
            // reopened (DrawerContentRoot.Clear() removes child elements).
            OnFleetSubTabSelected(_fleetSubTab);
        }

        private void OnFleetSubTabSelected(string subTabId)
        {
            _fleetSubTab = subTabId;
            _fleetSubContent?.Clear();

            if (_fleetSubContent == null) return;

            switch (subTabId)
            {
                case "overview":
                    if (_fleetSubPanel == null)
                    {
                        _fleetSubPanel = new FleetSubPanelController();
                        _fleetSubPanel.OnShipRowClicked += OnFleetShipRowClicked;
                        _fleetSubPanel.OnRescueDispatch  = OnFleetRescueDispatch;
                    }
                    _fleetSubPanel.style.flexGrow = 1;
                    _fleetSubPanel.style.height   = Length.Percent(100);
                    _fleetSubContent.Add(_fleetSubPanel);

                    if (_gm?.Station != null)
                        _fleetSubPanel.Refresh(_gm.Station, _gm?.Fleet);
                    break;

                case "dispatch":
                    if (_dispatchSubPanel == null)
                        _dispatchSubPanel = new DispatchSubPanelController();
                    _dispatchSubPanel.style.flexGrow = 1;
                    _dispatchSubPanel.style.height   = Length.Percent(100);
                    _fleetSubContent.Add(_dispatchSubPanel);

                    if (_gm?.Station != null)
                        _dispatchSubPanel.Refresh(
                            _gm.Station, _gm?.Fleet, _gm?.Map, _gm?.AsteroidMissions);
                    break;

                case "shipyard":
                    if (_shipyardSubPanel == null)
                        _shipyardSubPanel = new ShipyardSubPanelController();
                    _shipyardSubPanel.style.flexGrow = 1;
                    _shipyardSubPanel.style.height   = Length.Percent(100);
                    _fleetSubContent.Add(_shipyardSubPanel);

                    if (_gm?.Station != null)
                        _shipyardSubPanel.Refresh(_gm.Station, _gm?.Fleet);
                    break;
            }
        }

        private void OnFleetShipRowClicked(string shipUid)
        {
            // Ship Detail sub-panel is shown inline by FleetSubPanelController.
            // Log for diagnostic purposes.
            Debug.Log($"[WaystationHUDController] FleetShipRowClicked: {shipUid}");
        }

        private void OnFleetRescueDispatch(string shipUid)
        {
            // Switch to the Dispatch sub-tab pre-selecting the distressed ship.
            _fleetSubTab = "dispatch";
            _fleetSubTabs?.SelectTab("dispatch");
            if (_dispatchSubPanel != null && _gm?.Station != null)
            {
                _dispatchSubPanel.Refresh(
                    _gm.Station, _gm?.Fleet, _gm?.Map, _gm?.AsteroidMissions,
                    preselectedShipUid: shipUid);
            }
        }

        private void OnFleetChanged()
        {
            bool fleetTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Fleet;
            if (!fleetTabActive || _gm?.Station == null) return;

            if (_fleetSubPanel != null && _fleetSubTab == "overview")
                _fleetSubPanel.Refresh(_gm.Station, _gm?.Fleet);

            if (_dispatchSubPanel != null && _fleetSubTab == "dispatch")
                _dispatchSubPanel.Refresh(
                    _gm.Station, _gm?.Fleet, _gm?.Map, _gm?.AsteroidMissions);

            if (_shipyardSubPanel != null && _fleetSubTab == "shipyard")
                _shipyardSubPanel.Refresh(_gm.Station, _gm?.Fleet);
        }

        // ── Settings tab mount (UI-022) ───────────────────────────────────────

        /// <summary>
        /// Creates (once) and mounts the Settings tab panel.
        /// Re-mounts and refreshes with current state after drawer re-open.
        /// </summary>
        private void MountSettingsPanel()
        {
            if (_settingsPanel == null)
                _settingsPanel = new SettingsSubPanelController();

            _settingsPanel.style.flexGrow = 1;
            _settingsPanel.style.height   = Length.Percent(100);
            _sidePanel.DrawerContentRoot.Add(_settingsPanel);

            // Refresh immediately with current game state.
            _settingsPanel.Refresh(_gm?.Station, _gm, _gm?.Registry);
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

            if (_ready && _gm != null)
            {
                // Keep pause state in sync, but do not forcibly dismiss modal overlays when
                // leaving pause. Mandatory prompts (for example event/skill decisions) may
                // intentionally hold the game in a paused state and must remain visible until
                // their owning flow resolves them.
                _wasPausedLastFrame = _gm.IsPaused;
            }

            // Keep the shared GameHUD.InBuildMode in sync with the ghost placement state.
            GameHUD.InBuildMode = !string.IsNullOrEmpty(_ghostBuildableId);

            // Keep IsMouseOverDrawer in sync: OR the side panel, event log strip,
            // and any other registered HUD panel pointer states.
            bool sidePanelOver = _sidePanel != null && _sidePanel.IsMouseOverPanel;
            bool eventLogOver  = _eventLog  != null && _eventLog.IsMouseOverStrip;
            GameHUD.IsMouseOverDrawer = sidePanelOver || eventLogOver || _panelsUnderPointer > 0;

            // Push tile selection context text from StationRoomView into the event strip.
            _eventLog?.SetContextText(Waystation.View.StationRoomView.TileContextText);
        }

        // ── GameManager event handlers ────────────────────────────────────────

        private void OnTick(StationState station)
        {
            _topBar?.OnTick(station);
            _eventLog?.OnTick(station?.tick ?? 0);
            UpdateDepartureWarningOnTick(station);

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

            // Refresh the fleet panels every 5 ticks to avoid GC churn.
            bool fleetTabActive = _sidePanel?.ActiveTab == SidePanelController.Tab.Fleet;
            if (_fleetSubPanel != null && fleetTabActive && _fleetSubTab == "overview")
            {
                _fleetTickCounter++;
                if (_fleetTickCounter >= 5)
                {
                    _fleetTickCounter = 0;
                    _fleetSubPanel.Refresh(station, _gm?.Fleet);
                }
            }

            if (_dispatchSubPanel != null && fleetTabActive && _fleetSubTab == "dispatch")
            {
                _dispatchTickCounter++;
                if (_dispatchTickCounter >= 5)
                {
                    _dispatchTickCounter = 0;
                    _dispatchSubPanel.Refresh(
                        station, _gm?.Fleet, _gm?.Map, _gm?.AsteroidMissions);
                }
            }

            if (_shipyardSubPanel != null && fleetTabActive && _fleetSubTab == "shipyard")
            {
                _shipyardTickCounter++;
                if (_shipyardTickCounter >= 5)
                {
                    _shipyardTickCounter = 0;
                    _shipyardSubPanel.Refresh(station, _gm?.Fleet);
                }
            }

            // Refresh the crew member panel every 5 ticks while it is mounted.
            if (_crewMemberPanel != null && _crewMemberPanel.parent != null &&
                _crewMemberStack.Count > 0)
            {
                _crewMemberTickCounter++;
                if (_crewMemberTickCounter >= 5)
                {
                    _crewMemberTickCounter = 0;
                    RefreshCrewMemberPanel(_crewMemberStack[_crewMemberStack.Count - 1]);
                }
            }

            // Refresh the room panel every 5 ticks while it is mounted.
            if (_roomPanel != null && _roomPanel.parent != null &&
                !string.IsNullOrEmpty(_roomPanelRoomId))
            {
                _roomPanelTickCounter++;
                if (_roomPanelTickCounter >= 5)
                {
                    _roomPanelTickCounter = 0;
                    RefreshRoomPanel(_roomPanelRoomId);
                }
            }

            // Refresh the visiting ship panel every 5 ticks while it is mounted.
            if (_visitingShipPanel != null && _visitingShipPanel.parent != null &&
                !string.IsNullOrEmpty(_visitingShipPanelUid))
            {
                _visitingShipTickCounter++;
                if (_visitingShipTickCounter >= 5)
                {
                    _visitingShipTickCounter = 0;
                    RefreshVisitingShipPanel(_visitingShipPanelUid);
                }
            }

            // Refresh the faction detail panel every 5 ticks while it is mounted.
            if (_factionDetailPanel != null && _factionDetailPanel.parent != null &&
                !string.IsNullOrEmpty(_factionDetailPanelId))
            {
                _factionDetailTickCounter++;
                if (_factionDetailTickCounter >= 5)
                {
                    _factionDetailTickCounter = 0;
                    RefreshFactionDetailPanel(_factionDetailPanelId);
                }
            }

            // Refresh the workbench panel while it is mounted.
            // When the Queue tab is active, refresh every tick so the progress bar
            // decrements smoothly.  For other tabs, throttle to every 5 ticks to
            // avoid rebuilding the recipe/status tree unnecessarily.
            if (_workbenchPanel != null && _workbenchPanel.parent != null &&
                !string.IsNullOrEmpty(_workbenchPanelFoundationUid))
            {
                bool queueTabActive = _workbenchPanel.ActiveTab == "queue";
                if (queueTabActive)
                {
                    _workbenchPanelTickCounter = 0;
                    RefreshWorkbenchPanel(_workbenchPanelFoundationUid);
                }
                else
                {
                    _workbenchPanelTickCounter++;
                    if (_workbenchPanelTickCounter >= 5)
                    {
                        _workbenchPanelTickCounter = 0;
                        RefreshWorkbenchPanel(_workbenchPanelFoundationUid);
                    }
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
            EventLogBuffer.Instance.Add(
                category,
                pending.definition.description ?? pending.definition.id,
                tickFired: _gm?.Station?.tick ?? 0);

            ShowEventDecisionModal(pending);
        }

        private void ShowEventDecisionModal(PendingEvent pending)
        {
            if (_gm == null || _contentArea == null || pending?.definition == null) return;

            if (!_eventModalPauseCaptured)
            {
                _eventModalPriorPaused = _gm.IsPaused;
                _eventModalPriorTicksPerSecond = _gm.SecondsPerTick > 0.0001f
                    ? 1f / _gm.SecondsPerTick
                    : 1f;
                _eventModalPauseCaptured = true;
            }

            // Force pause while the event modal is open.
            _gm.IsPaused = true;
            _topBar?.RefreshSpeedButtons();

            RemoveEventDecisionModal(restoreSpeed: false);

            var modal = new ModalOverlay();
            modal.AddToClassList("ws-modal-overlay--top-left");
            modal.Title = pending.definition.title ?? "EVENT";
            modal.BackdropCloseEnabled = false;
            modal.CloseButtonVisible = false;

            // Chain indicator badge.
            if (IsChainEvent(pending))
            {
                var chainBadge = new Label("Part of a chain");
                chainBadge.style.alignSelf = Align.FlexStart;
                chainBadge.style.paddingLeft = 6;
                chainBadge.style.paddingRight = 6;
                chainBadge.style.paddingTop = 2;
                chainBadge.style.paddingBottom = 2;
                chainBadge.style.marginBottom = 8;
                chainBadge.style.borderTopLeftRadius = 3;
                chainBadge.style.borderTopRightRadius = 3;
                chainBadge.style.borderBottomLeftRadius = 3;
                chainBadge.style.borderBottomRightRadius = 3;
                chainBadge.style.backgroundColor = new Color(0.21f, 0.30f, 0.45f, 0.9f);
                chainBadge.style.color = new Color(0.86f, 0.92f, 1.0f, 1f);
                chainBadge.style.fontSize = 10;
                chainBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
                modal.BodyContent.Add(chainBadge);
            }

            // Description block (scrollable for long text).
            var descriptionScroll = new ScrollView(ScrollViewMode.Vertical);
            descriptionScroll.style.maxHeight = 260;
            descriptionScroll.style.marginBottom = 10;
            var desc = new Label(pending.definition.description ?? string.Empty);
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.unityTextAlign = TextAnchor.UpperLeft;
            desc.style.fontSize = 13;
            desc.style.color = new Color(0.84f, 0.90f, 1f, 1f);
            descriptionScroll.Add(desc);
            modal.BodyContent.Add(descriptionScroll);

            // Required choices (1-4).
            var choicesRoot = new VisualElement();
            choicesRoot.style.flexDirection = FlexDirection.Column;

            int count = pending.definition.choices?.Count ?? 0;
            int renderCount = Mathf.Min(4, count);
            for (int i = 0; i < renderCount; i++)
            {
                int choiceIndex = i;
                var choice = pending.definition.choices[choiceIndex];
                string choiceLabel = string.IsNullOrWhiteSpace(choice.label)
                    ? $"Choice {choiceIndex + 1}"
                    : choice.label;

                var btn = new Button(() => OnEventChoiceClicked(pending, choiceIndex))
                {
                    text = choiceLabel,
                };
                btn.AddToClassList("ws-btn");
                btn.style.unityTextAlign = TextAnchor.MiddleLeft;
                btn.style.whiteSpace = WhiteSpace.Normal;
                btn.style.paddingLeft = 10;
                btn.style.paddingRight = 10;
                btn.style.paddingTop = 8;
                btn.style.paddingBottom = 8;
                if (i > 0) btn.style.marginTop = 6;
                choicesRoot.Add(btn);
            }

            // Some events are informational and contain no explicit choices.
            // Keep the flow dismissible so the pause overlay cannot get stuck.
            if (renderCount == 0)
            {
                var continueBtn = new Button(() => RemoveEventDecisionModal(restoreSpeed: true))
                {
                    text = "CONTINUE",
                };
                continueBtn.AddToClassList("ws-btn");
                continueBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                continueBtn.style.paddingLeft = 10;
                continueBtn.style.paddingRight = 10;
                continueBtn.style.paddingTop = 8;
                continueBtn.style.paddingBottom = 8;
                choicesRoot.Add(continueBtn);
            }

            modal.BodyContent.Add(choicesRoot);

            _contentArea.Add(modal);
            _eventDecisionModal = modal;
            _eventDecisionModal.Show();
        }

        private void OnEventChoiceClicked(PendingEvent pending, int choiceIndex)
        {
            if (_gm?.Events == null || _gm?.Station == null || pending?.definition == null) return;
            if (choiceIndex < 0 || choiceIndex >= pending.definition.choices.Count) return;

            string choiceId = pending.definition.choices[choiceIndex].id;
            _gm.Events.ResolveChoice(pending, choiceId, _gm.Station);
            RemoveEventDecisionModal(restoreSpeed: true);
        }

        private void RemoveEventDecisionModal(bool restoreSpeed)
        {
            if (_eventDecisionModal != null)
            {
                if (_eventDecisionModal.parent == _contentArea)
                    _contentArea.Remove(_eventDecisionModal);
                _eventDecisionModal = null;
            }

            if (restoreSpeed && _gm != null && _eventModalPauseCaptured)
            {
                _gm.SetSpeed(_eventModalPriorTicksPerSecond);
                _gm.IsPaused = _eventModalPriorPaused;
                _topBar?.RefreshSpeedButtons();
                _eventModalPauseCaptured = false;
            }

            // Drain any queued expertise prompts that were deferred while the
            // event decision modal was active (UI-028).
            if (_expertisePromptQueue.Count > 0 && _activeExpertisePrompt == null)
                ShowNextExpertisePrompt();
        }

        private bool IsChainEvent(PendingEvent pending)
        {
            if (pending == null) return false;

            // Preferred signal from event context if present.
            if (pending.context != null)
            {
                if (TryContextBool(pending.context, "part_of_chain")) return true;
                if (TryContextBool(pending.context, "is_chain")) return true;
                if (TryContextBool(pending.context, "chain")) return true;
            }

            // Fallback: event requires any chain flag in trigger conditions.
            if (pending.definition?.triggerConditions != null && _gm?.Station != null)
            {
                foreach (var cond in pending.definition.triggerConditions)
                {
                    if (cond == null) continue;
                    if (cond.type != "chain_flag_set") continue;
                    if (_gm.Station.HasChainFlag(cond.target)) return true;
                }
            }

            return false;
        }

        private static bool TryContextBool(Dictionary<string, object> ctx, string key)
        {
            if (ctx == null || string.IsNullOrEmpty(key)) return false;
            if (!ctx.TryGetValue(key, out var raw) || raw == null) return false;

            if (raw is bool b) return b;
            if (raw is string s && bool.TryParse(s, out var parsed)) return parsed;

            try
            {
                return Convert.ToBoolean(raw);
            }
            catch
            {
                return false;
            }
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

            // Also refresh the room panel if it is open.
            if (_roomPanel != null && _roomPanel.parent != null &&
                !string.IsNullOrEmpty(_roomPanelRoomId) && _gm?.Station != null)
            {
                RefreshRoomPanel(_roomPanelRoomId);
            }
        }

        // ── Cross-view public API ─────────────────────────────────────────────

        /// <summary>
        /// Called by GameHUD.SelectCrewMember when UseUIToolkitHUD is true.
        /// Opens the Crew Member contextual panel for the given NPC uid.
        /// Ensures the Crew tab is open so the roster remains visible behind the panel.
        /// </summary>
        internal static void SelectCrewMemberInternal(string npcUid)
        {
            if (_instance == null || string.IsNullOrEmpty(npcUid)) return;

            // Ensure the Crew tab is open so the roster is visible.
            if (_instance._sidePanel?.ActiveTab != SidePanelController.Tab.Crew)
                _instance._sidePanel?.ActivateTab(SidePanelController.Tab.Crew);

            _instance.OpenCrewMemberPanel(npcUid);
        }

        private static void OpenRoomPanel(string roomId)
        {
            if (_instance == null || string.IsNullOrEmpty(roomId)) return;
            _instance.OpenRoomPanelInternal(roomId);
        }

        // ── Room contextual panel (UI-024) ────────────────────────────────────

        /// <summary>
        /// Opens or refreshes the Room contextual panel for the given room key.
        /// Switches to the Station tab so the Rooms list remains visible.
        /// </summary>
        private void OpenRoomPanelInternal(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            _roomPanelRoomId = roomId;

            // Ensure the Station tab is open so the rooms list is visible behind the panel.
            if (_sidePanel?.ActiveTab != SidePanelController.Tab.Station)
                _sidePanel?.ActivateTab(SidePanelController.Tab.Station);

            MountRoomPanel();
        }

        private void MountRoomPanel()
        {
            if (string.IsNullOrEmpty(_roomPanelRoomId)) return;

            if (_roomPanel == null)
            {
                _roomPanel = new RoomPanelController();
                _roomPanel.OnCloseRequested    += OnRoomPanelClosed;
                _roomPanel.OnWorkbenchRowClicked += OnRoomPanelWorkbenchClicked;
            }

            if (_roomPanel.parent == null)
                _contentArea.Add(_roomPanel);

            RefreshRoomPanel(_roomPanelRoomId);
        }

        private void RefreshRoomPanel(string roomId)
        {
            if (_roomPanel == null || _gm?.Station == null) return;
            _roomPanel.Refresh(
                roomId,
                _gm.Station,
                _gm.Registry,
                _gm.Rooms,
                _gm.Building,
                _gm.UtilityNetworks);
        }

        private void OnRoomPanelClosed()
        {
            _roomPanelRoomId = null;
            if (_roomPanel?.parent != null)
                _contentArea.Remove(_roomPanel);
        }

        private void OnRoomPanelWorkbenchClicked(string foundationUid)
        {
            OpenWorkbenchPanelInternal(foundationUid);
        }

        // ── Workbench contextual panel (UI-025) ───────────────────────────────

        /// <summary>
        /// Opens or refreshes the Workbench contextual panel for the given foundation uid.
        /// </summary>
        private void OpenWorkbenchPanelInternal(string foundationUid)
        {
            if (string.IsNullOrEmpty(foundationUid)) return;
            _workbenchPanelFoundationUid = foundationUid;
            MountWorkbenchPanel();
        }

        private void MountWorkbenchPanel()
        {
            if (string.IsNullOrEmpty(_workbenchPanelFoundationUid)) return;

            if (_workbenchPanel == null)
            {
                _workbenchPanel = new WorkbenchPanelController();
                _workbenchPanel.OnCloseRequested += OnWorkbenchPanelClosed;
            }

            if (_workbenchPanel.parent == null)
                _contentArea.Add(_workbenchPanel);

            RefreshWorkbenchPanel(_workbenchPanelFoundationUid);
        }

        private void RefreshWorkbenchPanel(string foundationUid)
        {
            if (_workbenchPanel == null || _gm?.Station == null) return;
            _workbenchPanel.Refresh(
                foundationUid,
                _gm.Station,
                _gm.Registry,
                _gm.Crafting,
                _gm.Inventory);
        }

        private void OnWorkbenchPanelClosed()
        {
            _workbenchPanelFoundationUid = null;
            if (_workbenchPanel?.parent != null)
                _contentArea.Remove(_workbenchPanel);
        }

        // ── Visiting Ship contextual panel (UI-026) ───────────────────────────

        /// <summary>
        /// Opens or refreshes the Visiting Ship contextual panel for the given ship uid.
        /// Ensures the World → Visitors tab is visible behind the panel.
        /// </summary>
        private void OpenVisitingShipPanelInternal(string shipUid)
        {
            if (string.IsNullOrEmpty(shipUid)) return;

            _visitingShipPanelUid = shipUid;

            // Ensure the World tab is open so the visitors list is visible behind the panel.
            if (_sidePanel?.ActiveTab != SidePanelController.Tab.World)
                _sidePanel?.ActivateTab(SidePanelController.Tab.World);

            MountVisitingShipPanel();
        }

        private void MountVisitingShipPanel()
        {
            if (string.IsNullOrEmpty(_visitingShipPanelUid)) return;

            if (_visitingShipPanel == null)
            {
                _visitingShipPanel = new VisitingShipPanelController();
                _visitingShipPanel.OnCloseRequested   += OnVisitingShipPanelClosed;
                _visitingShipPanel.OnGrantDocking      += OnVisitingShipGrantDocking;
                _visitingShipPanel.OnDenyDocking       += OnVisitingShipDenyDocking;
                _visitingShipPanel.OnNegotiateDocking  += OnVisitingShipNegotiateDocking;
                _visitingShipPanel.OnRequestDeparture  += OnVisitingShipRequestDeparture;
                _visitingShipPanel.OnOpenTradeManifest += OnVisitingShipOpenTradeManifest;
            }

            if (_visitingShipPanel.parent == null)
                _contentArea.Add(_visitingShipPanel);

            RefreshVisitingShipPanel(_visitingShipPanelUid);
        }

        private void RefreshVisitingShipPanel(string shipUid)
        {
            if (_visitingShipPanel == null || _gm?.Station == null) return;
            _visitingShipPanel.Refresh(shipUid, _gm.Station, _gm.Visitors, _gm.Factions);
        }

        private void OnVisitingShipPanelClosed()
        {
            _visitingShipPanelUid = null;
            if (_visitingShipPanel?.parent != null)
                _contentArea.Remove(_visitingShipPanel);
        }

        private void OnVisitingShipGrantDocking(string shipUid)
        {
            _gm?.Visitors?.GrantDocking(shipUid, _gm.Station);
            if (!string.IsNullOrEmpty(_visitingShipPanelUid))
                RefreshVisitingShipPanel(_visitingShipPanelUid);
            // Refresh the visitors list too.
            if (_visitorsSubPanel != null && _gm?.Station != null)
                _visitorsSubPanel.Refresh(_gm.Station, _gm?.Visitors);
        }

        private void OnVisitingShipDenyDocking(string shipUid)
        {
            _gm?.Visitors?.DenyDocking(shipUid, _gm.Station);
            // Ship may have been removed — close the panel.
            OnVisitingShipPanelClosed();
            if (_visitorsSubPanel != null && _gm?.Station != null)
                _visitorsSubPanel.Refresh(_gm.Station, _gm?.Visitors);
        }

        private void OnVisitingShipNegotiateDocking(string shipUid)
        {
            _gm?.Visitors?.NegotiateDocking(shipUid, _gm.Station);
            if (!string.IsNullOrEmpty(_visitingShipPanelUid))
                RefreshVisitingShipPanel(_visitingShipPanelUid);
            if (_visitorsSubPanel != null && _gm?.Station != null)
                _visitorsSubPanel.Refresh(_gm.Station, _gm?.Visitors);
        }

        private void OnVisitingShipRequestDeparture(string shipUid)
        {
            _gm?.Visitors?.DepartShip(shipUid, _gm.Station);
            OnVisitingShipPanelClosed();
            if (_visitorsSubPanel != null && _gm?.Station != null)
                _visitorsSubPanel.Refresh(_gm.Station, _gm?.Visitors);
        }

        private void OnVisitingShipOpenTradeManifest(string shipUid)
        {
            // Trade Negotiation modal is event-driven — log for now.
            Debug.Log($"[WaystationHUDController] OpenTradeManifest: {shipUid}");
        }

        // ── Faction Detail contextual panel (UI-026) ──────────────────────────

        /// <summary>
        /// Opens or refreshes the Faction Detail contextual panel for the given faction id.
        /// Ensures the World → Factions tab is visible behind the panel.
        /// </summary>
        private void OpenFactionDetailPanelInternal(string factionId)
        {
            if (string.IsNullOrEmpty(factionId)) return;

            _factionDetailPanelId = factionId;

            // Ensure the World tab is open so the factions list is visible behind the panel.
            if (_sidePanel?.ActiveTab != SidePanelController.Tab.World)
                _sidePanel?.ActivateTab(SidePanelController.Tab.World);

            MountFactionDetailPanel();
        }

        private void MountFactionDetailPanel()
        {
            if (string.IsNullOrEmpty(_factionDetailPanelId)) return;

            if (_factionDetailPanel == null)
            {
                _factionDetailPanel = new FactionDetailPanelController();
                _factionDetailPanel.OnCloseRequested += OnFactionDetailPanelClosed;
            }

            if (_factionDetailPanel.parent == null)
                _contentArea.Add(_factionDetailPanel);

            RefreshFactionDetailPanel(_factionDetailPanelId);
        }

        private void RefreshFactionDetailPanel(string factionId)
        {
            if (_factionDetailPanel == null || _gm?.Station == null) return;
            _factionDetailPanel.Refresh(factionId, _gm.Station, _gm.Factions, _gm.FactionHistory);
        }

        private void OnFactionDetailPanelClosed()
        {
            _factionDetailPanelId = null;
            if (_factionDetailPanel?.parent != null)
                _contentArea.Remove(_factionDetailPanel);
        }

        // ── Crew Member contextual panel (UI-023) ─────────────────────────────

        /// <summary>
        /// Opens or refreshes the Crew Member contextual panel for the given NPC uid.
        /// Stacks: if the NPC is not already the top of the stack, pushes it.
        /// </summary>
        private void OpenCrewMemberPanel(string npcUid)
        {
            if (string.IsNullOrEmpty(npcUid)) return;

            // Push to stack (avoid duplicates adjacent to each other).
            if (_crewMemberStack.Count == 0 || _crewMemberStack[_crewMemberStack.Count - 1] != npcUid)
                _crewMemberStack.Add(npcUid);

            MountCrewMemberPanel();
        }

        /// <summary>
        /// Creates (if needed) and mounts the crew member panel, then refreshes it
        /// with the NPC uid at the top of the stack.
        /// </summary>
        private void MountCrewMemberPanel()
        {
            if (_crewMemberStack.Count == 0) return;
            string npcUid = _crewMemberStack[_crewMemberStack.Count - 1];

            if (_crewMemberPanel == null)
            {
                _crewMemberPanel = new CrewMemberPanelController();
                _crewMemberPanel.OnCloseRequested        += OnCrewMemberPanelClosed;
                _crewMemberPanel.OnRelationshipRowClicked += OnCrewMemberRelationshipRowClicked;
                _crewMemberPanel.OnExpertiseSlotClicked  += OnCrewMemberExpertiseSlotClicked;
            }

            if (_crewMemberPanel.parent == null)
                _contentArea.Add(_crewMemberPanel);

            RefreshCrewMemberPanel(npcUid);
        }

        private void RefreshCrewMemberPanel(string npcUid)
        {
            if (_crewMemberPanel == null || _gm?.Station == null) return;

            _gm.Station.npcs.TryGetValue(npcUid, out var npc);
            _crewMemberPanel.Refresh(
                npc,
                _gm.Station,
                _gm.Registry,
                _gm.Skills,
                _gm.Inventory,
                _gm.Traits);
        }

        private void OnCrewMemberPanelClosed()
        {
            if (_crewMemberStack.Count > 0)
                _crewMemberStack.RemoveAt(_crewMemberStack.Count - 1);

            if (_crewMemberStack.Count > 0)
            {
                // Re-show the previous NPC in the stack.
                RefreshCrewMemberPanel(_crewMemberStack[_crewMemberStack.Count - 1]);
            }
            else
            {
                // No more stacked panels — unmount.
                if (_crewMemberPanel?.parent != null)
                    _contentArea.Remove(_crewMemberPanel);
            }
        }

        private void OnCrewMemberRelationshipRowClicked(string otherNpcUid)
        {
            OpenCrewMemberPanel(otherNpcUid);
        }

        private void OnCrewMemberExpertiseSlotClicked(string npcUid, string skillId)
        {
            if (_gm?.Station == null || !_gm.Station.npcs.TryGetValue(npcUid, out var npc)) return;

            // Build and show the expertise slot unlock modal.
            var prompt = new ExpertiseSlotPrompt();

            var selectableExpertise = _gm.Skills?.GetSelectableExpertise(npc, showAll: false)
                ?? new List<ExpertiseDefinition>();

            // Filter to expertise relevant to this skill (or unbound expertise if skill is empty).
            foreach (var expDef in selectableExpertise)
            {
                if (string.IsNullOrEmpty(expDef.requiredSkillId) || expDef.requiredSkillId == skillId)
                    prompt.AddExpertiseOption(expDef.expertiseId, expDef.displayName ?? expDef.expertiseId,
                        expDef.description ?? "");
            }

            prompt.OnConfirmed += expertiseId =>
            {
                _gm.Skills?.ChooseExpertise(npc, expertiseId, _gm.Station);
                // Refresh the panel to reflect the new choice.
                RefreshCrewMemberPanel(npcUid);

                // Remove the prompt from the content area after confirmation
                // to avoid leaking UI elements and lingering event handlers.
                if (prompt.parent == _contentArea)
                    _contentArea.Remove(prompt);
            };

            // Also clean up when the prompt is hidden (e.g., on cancel/backdrop click).
            prompt.OnHidden += () =>
            {
                if (prompt.parent == _contentArea)
                    _contentArea.Remove(prompt);
            };

            _contentArea.Add(prompt);
            prompt.Show();
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

        // ── Expertise Slot Unlock Prompt (UI-028) ────────────────────────────

        private void OnExpertiseSlotEarned(NPCInstance npc, string skillId, int skillLevel)
        {
            _expertisePromptQueue.Enqueue(new ExpertisePromptEntry
            {
                npc = npc,
                skillId = skillId,
                skillLevel = skillLevel,
            });

            // Only show immediately if no modal is currently blocking.
            if (_activeExpertisePrompt == null && _eventDecisionModal == null)
                ShowNextExpertisePrompt();
        }

        /// <summary>
        /// Dequeues the next expertise prompt entry and shows it.
        /// Called after each prompt is resolved and after event decision modal
        /// dismissal if the queue is non-empty.
        /// </summary>
        private void ShowNextExpertisePrompt()
        {
            if (_expertisePromptQueue.Count == 0) return;
            if (_gm == null || _contentArea == null) return;

            // Defer if an event decision modal is currently active.
            if (_eventDecisionModal != null) return;

            var entry = _expertisePromptQueue.Dequeue();

            // Capture prior pause state once (before first prompt in a batch).
            if (!_expertisePauseCaptured)
            {
                _expertisePriorPaused = _gm.IsPaused;
                _expertisePriorTicksPerSecond = _gm.SecondsPerTick > 0.0001f
                    ? 1f / _gm.SecondsPerTick
                    : 1f;
                _expertisePauseCaptured = true;
            }

            _gm.IsPaused = true;
            _topBar?.RefreshSpeedButtons();

            // Remove any stale prompt.
            RemoveActiveExpertisePrompt(restoreSpeed: false);

            var prompt = new ExpertiseSlotPrompt();
            prompt.MandatoryMode = true;
            prompt.NpcName = entry.npc?.name ?? "Unknown";

            // Resolve skill display name.
            string skillDisplayName = entry.skillId;
            SkillDefinition skillDef = null;
            if (_gm.Registry?.Skills != null &&
                _gm.Registry.Skills.TryGetValue(entry.skillId, out skillDef))
            {
                skillDisplayName = skillDef.displayName ?? entry.skillId;
            }
            prompt.SkillInfo = $"{skillDisplayName} reached level {entry.skillLevel}";

            // Queue depth indicator: "1 of N" where N = this prompt + remaining.
            int totalPending = _expertisePromptQueue.Count + 1;
            prompt.QueueInfo = totalPending > 1
                ? $"1 of {totalPending} pending"
                : null;

            // Populate expertise options based on skill type.
            bool isDomain = skillDef != null && skillDef.IsDomainSkill;
            if (isDomain && skillDef.domainExpertiseSlots != null)
            {
                // Domain path: show options from the slot matching this level.
                foreach (var slot in skillDef.domainExpertiseSlots)
                {
                    if (slot.unlockLevel > entry.skillLevel) continue;
                    foreach (var opt in slot.options)
                    {
                        if (entry.npc.chosenExpertise.Contains(opt.id)) continue;
                        prompt.AddExpertiseOption(opt.id, opt.name ?? opt.id,
                            opt.description ?? "");
                    }
                }
            }
            else
            {
                // Legacy path: show all selectable expertise for this skill.
                var selectable = _gm.Skills?.GetSelectableExpertise(entry.npc, showAll: false)
                    ?? new List<ExpertiseDefinition>();
                foreach (var expDef in selectable)
                {
                    if (string.IsNullOrEmpty(expDef.requiredSkillId) ||
                        expDef.requiredSkillId == entry.skillId)
                    {
                        prompt.AddExpertiseOption(expDef.expertiseId,
                            expDef.displayName ?? expDef.expertiseId,
                            expDef.description ?? "");
                    }
                }
            }

            var capturedEntry = entry;
            prompt.OnConfirmed += expertiseId =>
                OnExpertisePromptConfirmed(capturedEntry, expertiseId, isDomain);

            _contentArea.Add(prompt);
            _activeExpertisePrompt = prompt;
            prompt.Show();
        }

        private void OnExpertisePromptConfirmed(ExpertisePromptEntry entry,
                                                 string expertiseId, bool isDomain)
        {
            if (_gm?.Skills != null && _gm?.Station != null && entry.npc != null)
            {
                if (isDomain)
                    _gm.Skills.ChooseDomainExpertise(entry.npc, entry.skillId,
                        expertiseId, _gm.Station);
                else
                    _gm.Skills.ChooseExpertise(entry.npc, expertiseId, _gm.Station);
            }

            RemoveActiveExpertisePrompt(restoreSpeed: _expertisePromptQueue.Count == 0);

            // Show next queued prompt if any remain.
            if (_expertisePromptQueue.Count > 0)
                ShowNextExpertisePrompt();
        }

        private void RemoveActiveExpertisePrompt(bool restoreSpeed)
        {
            if (_activeExpertisePrompt != null)
            {
                if (_activeExpertisePrompt.parent == _contentArea)
                    _contentArea.Remove(_activeExpertisePrompt);
                _activeExpertisePrompt = null;
            }

            if (restoreSpeed && _gm != null && _expertisePauseCaptured)
            {
                _gm.SetSpeed(_expertisePriorTicksPerSecond);
                _gm.IsPaused = _expertisePriorPaused;
                _topBar?.RefreshSpeedButtons();
                _expertisePauseCaptured = false;
            }
        }

        // ── Departure Warning Panel (UI-030) ────────────────────────────────

        private void OnDepartureWarning(NPCInstance npc, int deadlineTick)
        {
            int announcedAt = npc.traitProfile?.departure?.announcedAtTick
                              ?? (_gm?.Station?.tick ?? 0);

            var entry = new DepartureWarningEntry
            {
                npc = npc,
                deadlineTick = deadlineTick,
                announcedAtTick = announcedAt,
            };

            if (_activeDepartureWarning != null)
            {
                _departureWarningQueue.Enqueue(entry);
                return;
            }

            ShowDepartureWarning(entry);
        }

        private void ShowDepartureWarning(DepartureWarningEntry entry)
        {
            if (_contentArea == null || _gm == null) return;

            var panel = new DepartureWarningPanel();
            panel.NpcUid = entry.npc.uid;
            panel.NpcName = entry.npc.name ?? "Unknown";
            panel.DeadlineTick = entry.deadlineTick;
            panel.AnnouncedAtTick = entry.announcedAtTick;
            panel.SetTensionStage(_gm.Tension.GetTensionStage(entry.npc));

            int currentTick = _gm.Station?.tick ?? 0;
            panel.UpdateCountdown(currentTick);

            panel.OnInterveneClicked += () => OnDepartureInterveneClicked(entry);
            panel.OnDismissClicked += () => DismissDepartureWarning();

            _contentArea.Add(panel);
            _activeDepartureWarning = panel;
            _departureOutcomeDismissCountdown = -1;
        }

        private void OnDepartureInterveneClicked(DepartureWarningEntry entry)
        {
            if (_gm == null || _activeDepartureWarning == null) return;

            var (ok, msg) = _gm.AttemptDepartureIntervention(entry.npc.uid);
            _activeDepartureWarning.ShowOutcome(ok, msg);

            // Auto-dismiss after ~8 ticks (≈2 in-game hours) so the panel
            // clears itself without requiring player action.
            _departureOutcomeDismissCountdown = 8;
        }

        private void DismissDepartureWarning()
        {
            RemoveActiveDepartureWarning();
            if (_departureWarningQueue.Count > 0)
                ShowDepartureWarning(_departureWarningQueue.Dequeue());
        }

        private void RemoveActiveDepartureWarning()
        {
            if (_activeDepartureWarning != null)
            {
                if (_activeDepartureWarning.parent == _contentArea)
                    _contentArea.Remove(_activeDepartureWarning);
                _activeDepartureWarning = null;
                _departureOutcomeDismissCountdown = -1;
            }
        }

        private void UpdateDepartureWarningOnTick(StationState station)
        {
            if (_activeDepartureWarning == null) return;

            int currentTick = station?.tick ?? 0;
            _activeDepartureWarning.UpdateCountdown(currentTick);

            // If the deadline has passed and no outcome shown, auto-dismiss.
            if (currentTick >= _activeDepartureWarning.DeadlineTick &&
                _departureOutcomeDismissCountdown < 0)
            {
                DismissDepartureWarning();
                return;
            }

            // Auto-dismiss countdown after showing outcome.
            if (_departureOutcomeDismissCountdown > 0)
            {
                _departureOutcomeDismissCountdown--;
            }
            else if (_departureOutcomeDismissCountdown == 0)
            {
                DismissDepartureWarning();
            }
        }
    }
}
