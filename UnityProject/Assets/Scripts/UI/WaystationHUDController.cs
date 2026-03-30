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

        // Top bar (WO-UI-004)
        private TopBarController _topBar;

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
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy()
        {
            _instance = null;
            BuildMenuController.OnBuildItemSelected -= OnBuildMenuItemSelected;

            _topBar?.Detach();

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

        // ── Top bar setup (WO-UI-004) ──────────────────────────────────────────

        private void BuildTopBar()
        {
            var doc = GetComponent<UIDocument>() ?? FindFirstObjectByType<UIDocument>();
            if (doc == null)
            {
                Debug.LogWarning("[WaystationHUDController] No UIDocument found — top bar will not render.");
                return;
            }

            _topBar = new TopBarController();
            _topBar.RegisterClickOutside(doc.rootVisualElement);

            // Insert before the side panel so it renders at the top.
            doc.rootVisualElement.Insert(0, _topBar);
        }

        // ── Side panel setup (WO-UI-005) ──────────────────────────────────────

        private void BuildSidePanel()
        {
            // Locate the UIDocument that hosts the HUD panels.
            // The document is expected to be present in the scene (set up as a
            // prefab or scene object); a missing document is silently skipped so
            // the rest of the controller still boots cleanly.
            var doc = GetComponent<UIDocument>() ?? FindFirstObjectByType<UIDocument>();
            if (doc == null)
            {
                Debug.LogWarning("[WaystationHUDController] No UIDocument found — side panel will not render.");
                return;
            }

            // MapSystem is not yet available at Start() time — WaitForGame() will
            // inject it once the game has finished loading (see InjectMapSystem call
            // in OnGameLoaded).
            _sidePanel = new SidePanelController();

            // Hook up the Map fullscreen callback to open SystemMapController
            _sidePanel.OnMapFullscreenRequested += OnSidePanelMapFullscreen;

            // Register keyboard handler so Escape key works
            _sidePanel.RegisterKeyboard(doc.rootVisualElement);

            doc.rootVisualElement.Add(_sidePanel);
        }

        private void OnSidePanelMapFullscreen()
        {
            // Delegate to the legacy system map open logic for now.
            var systemMap = FindFirstObjectByType<SystemMapController>();
            systemMap?.Open();
        }

        // ── Update — sync placement state and mouse-over ──────────────────────
        private void Update()
        {
            // Keep the shared GameHUD.InBuildMode in sync with the ghost placement state.
            GameHUD.InBuildMode = !string.IsNullOrEmpty(_ghostBuildableId);

            // Keep IsMouseOverDrawer in sync: OR the side panel's pointer state
            // with any other registered HUD panels.
            bool sidePanelOver = _sidePanel != null && _sidePanel.IsMouseOverPanel;
            GameHUD.IsMouseOverDrawer = sidePanelOver || _panelsUnderPointer > 0;
        }

        // ── GameManager event handlers ────────────────────────────────────────

        private void OnTick(StationState station)
        {
            _topBar?.OnTick(station);
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
        }

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
