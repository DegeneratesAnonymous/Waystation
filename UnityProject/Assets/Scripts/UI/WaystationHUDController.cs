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
// Migration status: top bar (WO-UI-004), side panel shell (WO-UI-005), and persistent
// event log strip (WO-UI-003) active.
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

        // Top bar (WO-UI-004)
        private TopBarController _topBar;

        // Event log strip (WO-UI-003)
        private EventLogController _eventLog;

        // Shared UIDocument created on demand for all UI Toolkit panels.
        private UIDocument _uiDocument;

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
                BuildEventLogStrip();
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy()
        {
            _instance = null;
            BuildMenuController.OnBuildItemSelected -= OnBuildMenuItemSelected;

            _topBar?.Detach();

            // Only destroy PanelSettings if this controller created it at runtime;
            // if it was found in the scene we don't own it.
            if (_createdPanelSettings && _uiDocument != null && _uiDocument.panelSettings != null)
                Destroy(_uiDocument.panelSettings);

            if (_gm != null)
            {
                _gm.OnTick       -= OnTick;
                _gm.OnNewEvent   -= OnNewEvent;
                _gm.OnGameLoaded -= OnGameLoaded;

                // Detach event log system event wiring
                DetachEventLogWiring(_gm);
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

            // Register keyboard handler so Escape key works
            _sidePanel.RegisterKeyboard(doc.rootVisualElement);

            _contentArea.Add(_sidePanel);
        }

        private void OnSidePanelMapFullscreen()
        {
            // Delegate to the legacy system map open logic for now.
            var systemMap = FindFirstObjectByType<SystemMapController>();
            systemMap?.Open();
        }

        // ── Event log strip setup (WO-UI-003) ─────────────────────────────────

        private void BuildEventLogStrip()
        {
            var doc = EnsureUIDocument();

            _eventLog = new EventLogController();

            // Subscribe to buffer changes so the strip updates when new entries arrive.
            EventLogBuffer.Instance.OnBufferChanged += OnEventLogBufferChanged;

            // Inject navigation callbacks
            _eventLog.InjectNavigationCallbacks(
                selectCrewMember:  uid => SelectCrewMemberInternal(uid),
                openRoomPanel:     id  => OpenRoomPanel(id),
                openNetworkOverlay: () => OpenNetworkOverlay(),
                openVisitorPanel:  id  => OpenVisitorPanel(id),
                openFleetPanel:    uid => OpenFleetPanel(uid));

            // The strip is added to the root so it is visible in map fullscreen mode.
            // It uses position:absolute + bottom:0 so it stays anchored to the screen
            // bottom regardless of tab or panel state.
            doc.rootVisualElement.Add(_eventLog);
        }

        private void OnEventLogBufferChanged()
        {
            _eventLog?.OnBufferChanged();
        }

        // ── Update — sync placement state and mouse-over ──────────────────────
        private void Update()
        {
            // Keep the shared GameHUD.InBuildMode in sync with the ghost placement state.
            GameHUD.InBuildMode = !string.IsNullOrEmpty(_ghostBuildableId);

            // Keep IsMouseOverDrawer in sync: OR the side panel's pointer state
            // with the event log strip's pointer state and any other registered HUD panels.
            bool sidePanelOver = _sidePanel != null && _sidePanel.IsMouseOverPanel;
            bool eventLogOver  = _eventLog  != null && _eventLog.IsMouseOverStrip;
            GameHUD.IsMouseOverDrawer = sidePanelOver || eventLogOver || _panelsUnderPointer > 0;
        }

        // ── GameManager event handlers ────────────────────────────────────────

        private void OnTick(StationState station)
        {
            _topBar?.OnTick(station);
            _eventLog?.OnTick(station.tick);
            // Per-tick panel refresh — panels register their own listeners or are
            // refreshed here as they are migrated.
        }

        private void OnNewEvent(PendingEvent pending)
        {
            // Event modal handling — implemented when the Event Modal panel is migrated.
        }

        private void OnGameLoaded()
        {
            // Inject the live MapSystem now that the game has finished loading.
            // BuildSidePanel() runs before WaitForGame() completes, so MapSystem
            // was null at construction time.
            _sidePanel?.InjectMapSystem(_gm?.Map);

            // Set the initial view context to the station name and inject dependencies
            // into the top bar once the game state is available.
            if (_gm?.Station != null)
            {
                ViewContextManager.Instance.SetContext(_gm.Station.stationName);
                _topBar?.InjectDependencies(_gm, LogEntryBuffer.Instance, ViewContextManager.Instance);
                OnTick(_gm.Station);
            }

            // Wire event log subscriptions now that all systems are live.
            if (FeatureFlags.UseEventLogStrip && _gm != null)
                AttachEventLogWiring(_gm);
        }

        // ── Event log system wiring ───────────────────────────────────────────
        // Each lambda captures _gm via the outer scope.  All lambdas are stored so
        // they can be unsubscribed in DetachEventLogWiring on destroy.

        // Stored delegates for clean unsubscription
        private Action<NPCInstance, TensionStage> _onTensionStageChanged;
        private Action<NPCInstance, int>           _onDepartureAnnounced;
        private Action<NPCInstance>                _onNpcDeparted;
        private Action<NPCInstance>                _onNpcEnteredCrisis;
        private Action<NPCInstance, string, int>   _onSlotEarned;
        private Action<NPCInstance, int>           _onCharacterLevelUp;
        private Action<string>                     _onResourceDepleted;
        private Action<string, float, float>       _onFactionRepThresholdCrossed;
        private Action<string, string, RelationshipType> _onRelationshipMilestone;

        private void AttachEventLogWiring(GameManager gm)
        {
            var buf = EventLogBuffer.Instance;

            // TensionSystem — DepartureRisk escalation
            // The tick is read at event-fire time (not at registration time) so each
            // lambda captures gm to access gm.Station?.tick when the event fires.
            _onTensionStageChanged = (npc, stage) =>
            {
                if (stage == TensionStage.DepartureRisk)
                    buf.Add(LogCategory.Crew,
                        $"{npc.name} has reached DepartureRisk — tension critical",
                        gm.Station?.tick ?? 0,
                        "crew", npc.uid, "Open crew panel");
            };
            gm.Tension.OnTensionStageChanged += _onTensionStageChanged;

            // TensionSystem — departure announced
            _onDepartureAnnounced = (npc, deadline) =>
                buf.Add(LogCategory.Alert,
                    $"{npc.name} has announced intent to leave the station",
                    gm.Station?.tick ?? 0,
                    "crew", npc.uid, "Open crew panel");
            gm.Tension.OnDepartureAnnounced += _onDepartureAnnounced;

            // TensionSystem — NPC departed
            _onNpcDeparted = npc =>
                buf.Add(LogCategory.Crew,
                    $"{npc.name} has left the station",
                    gm.Station?.tick ?? 0);
            gm.Tension.OnNpcDeparted += _onNpcDeparted;

            // MoodSystem — NPC breakdown
            _onNpcEnteredCrisis = npc =>
                buf.Add(LogCategory.Alert,
                    $"{npc.name} is in breakdown — counselling required",
                    gm.Station?.tick ?? 0,
                    "crew", npc.uid, "Open crew panel");
            gm.Mood.OnNpcEnteredCrisis += _onNpcEnteredCrisis;

            // SkillSystem — expertise slot earned
            _onSlotEarned = (npc, skillId, skillLevel) =>
            {
                string skillName = gm.Registry?.Skills != null &&
                                   gm.Registry.Skills.TryGetValue(skillId, out var sdef)
                                   ? sdef.displayName : skillId;
                buf.Add(LogCategory.Crew,
                    $"{npc.name} — {skillName} skill reached level {skillLevel}, expertise slot available",
                    gm.Station?.tick ?? 0,
                    "crew", npc.uid, "Open crew panel");
            };
            gm.Skills.OnSlotEarned += _onSlotEarned;

            // SkillSystem — character level-up
            _onCharacterLevelUp = (npc, level) =>
                buf.Add(LogCategory.Crew,
                    $"{npc.name} reached character level {level}",
                    gm.Station?.tick ?? 0,
                    "crew", npc.uid, "Open crew panel");
            gm.Skills.OnCharacterLevelUp += _onCharacterLevelUp;

            // ResourceSystem — resource depleted
            _onResourceDepleted = resourceId =>
                buf.Add(LogCategory.Alert,
                    $"{FormatResourceName(resourceId)} supply below warning threshold",
                    gm.Station?.tick ?? 0);
            gm.Resources.OnResourceDepleted += _onResourceDepleted;

            // FactionSystem — rep threshold crossed
            _onFactionRepThresholdCrossed = (factionId, oldRep, newRep) =>
            {
                string dir = newRep < oldRep ? "dropped below hostile threshold" : "improved above threshold";
                buf.Add(LogCategory.World,
                    $"Reputation with {factionId} has {dir}",
                    gm.Station?.tick ?? 0);
            };
            gm.Factions.OnFactionRepThresholdCrossed += _onFactionRepThresholdCrossed;

            // RelationshipRegistry — milestone (static event)
            _onRelationshipMilestone = (uid1, uid2, relType) =>
            {
                string name1 = gm.Station?.npcs != null &&
                               gm.Station.npcs.TryGetValue(uid1, out var n1) ? n1.name : uid1;
                string name2 = gm.Station?.npcs != null &&
                               gm.Station.npcs.TryGetValue(uid2, out var n2) ? n2.name : uid2;
                buf.Add(LogCategory.Crew,
                    $"{name1} and {name2} formed a {relType} bond",
                    gm.Station?.tick ?? 0,
                    "crew", uid1, "Open crew panel");
            };
            RelationshipRegistry.OnRelationshipMilestoneReached += _onRelationshipMilestone;
        }

        private void DetachEventLogWiring(GameManager gm)
        {
            if (_onTensionStageChanged != null)
            {
                gm.Tension.OnTensionStageChanged -= _onTensionStageChanged;
                gm.Tension.OnDepartureAnnounced  -= _onDepartureAnnounced;
                gm.Tension.OnNpcDeparted         -= _onNpcDeparted;
                gm.Mood.OnNpcEnteredCrisis        -= _onNpcEnteredCrisis;
                gm.Skills.OnSlotEarned            -= _onSlotEarned;
                gm.Skills.OnCharacterLevelUp      -= _onCharacterLevelUp;
                gm.Resources.OnResourceDepleted   -= _onResourceDepleted;
                gm.Factions.OnFactionRepThresholdCrossed -= _onFactionRepThresholdCrossed;
                RelationshipRegistry.OnRelationshipMilestoneReached -= _onRelationshipMilestone;
            }

            EventLogBuffer.Instance.OnBufferChanged -= OnEventLogBufferChanged;
        }

        private static string FormatResourceName(string resourceId) => resourceId switch
        {
            "oxygen"  => "Oxygen",
            "power"   => "Power",
            "food"    => "Food",
            "parts"   => "Parts",
            "credits" => "Credits",
            _         => resourceId,
        };

        // ── Cross-view public API ─────────────────────────────────────────────

        /// <summary>
        /// Called by GameHUD.SelectCrewMember when UseUIToolkitHUD is true.
        /// Opens the Crew Detail panel for the specified NPC.
        /// </summary>
        internal static void SelectCrewMemberInternal(string npcUid)
        {
            if (_instance == null || string.IsNullOrEmpty(npcUid)) return;
            // Crew Detail panel open — implemented when Crew sub-tab is migrated.
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
            _ghostBuildableId     = buildableId;
            _ghostRotation        = 0;
            GameHUD.InBuildMode   = true;
            Debug.Log($"[WaystationHUDController] Beginning ghost placement: {buildableId}");
        }

        /// <summary>Cancels the current ghost placement session.</summary>
        public void CancelPlacement()
        {
            _ghostBuildableId   = null;
            _ghostRotation      = 0;
            GameHUD.InBuildMode = false;
        }
    }
}
