// WaystationHUDController — UI Toolkit replacement for GameHUD.
//
// Activated when FeatureFlags.UseUIToolkitHUD is true.
// Self-installs in GameScene via RuntimeInitializeOnLoadMethod (same pattern as GameHUD).
//
// Public API contract (mirrors GameHUD statics so all callers are unchanged):
//   IsMouseOverDrawer  — read by CameraController to block map scroll/pan
//   InBuildMode        — read by StationRoomView to suppress NPC click-selection
//   SelectCrewMember() — called by StationRoomView on crew-dot single-click
//
// Migration status: infrastructure scaffold only.
// Full panel implementations are added panel-by-panel behind this controller
// as described in WO-UI-002.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    public class WaystationHUDController : MonoBehaviour
    {
        // ── Public API (mirrors GameHUD statics) ──────────────────────────────

        /// <summary>
        /// True when the mouse cursor is over any UI Toolkit HUD panel.
        /// Read by CameraController to prevent accidental camera pan/zoom.
        /// </summary>
        public static bool IsMouseOverDrawer { get; private set; }

        /// <summary>
        /// True when ghost-placement or deconstruct mode is active.
        /// Read by StationRoomView to suppress NPC click-selection while building.
        /// </summary>
        public static bool InBuildMode { get; private set; }

        // ── Private state ─────────────────────────────────────────────────────
        private static WaystationHUDController _instance;

        private GameManager _gm;
        private bool        _ready;

        // Placement state — mirrors GameHUD ghost-placement fields.
        private string _ghostBuildableId;
        private int    _ghostRotation;

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
            // Reset pointer state on each scene load to prevent stale counts
            // carried over from a previous session.
            _panelsUnderPointer = 0;
            IsMouseOverDrawer   = false;
            if (FindAnyObjectByType<WaystationHUDController>() != null) return;
            new GameObject("WaystationHUDController").AddComponent<WaystationHUDController>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            _instance = this;
            BuildMenuController.OnBuildItemSelected += OnBuildMenuItemSelected;
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy()
        {
            _instance = null;
            BuildMenuController.OnBuildItemSelected -= OnBuildMenuItemSelected;

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

        // ── Update — mouse-over detection ─────────────────────────────────────
        private void Update()
        {
            // UI Toolkit panels handle pointer events natively; IsMouseOverDrawer is
            // updated here via panel pointer-enter/leave callbacks registered during
            // panel initialisation (implemented per-panel as panels are migrated).
            // Until panels are wired, IsMouseOverDrawer defaults to false so camera
            // controls are not inadvertently blocked.

            // InBuildMode tracks active ghost placement.
            InBuildMode = !string.IsNullOrEmpty(_ghostBuildableId);
        }

        // ── GameManager event handlers ────────────────────────────────────────

        private void OnTick(StationState station)
        {
            // Per-tick panel refresh — panels register their own listeners or are
            // refreshed here as they are migrated.
        }

        private void OnNewEvent(PendingEvent pending)
        {
            // Event modal handling — implemented when the Event Modal panel is migrated.
        }

        private void OnGameLoaded()
        {
            if (_gm?.Station != null) OnTick(_gm.Station);
        }

        // ── Cross-view public API ─────────────────────────────────────────────

        /// <summary>
        /// Called by StationRoomView when a crew sprite is single-clicked.
        /// Opens the Crew Detail panel for the specified NPC.
        /// </summary>
        public static void SelectCrewMember(string npcUid)
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
            IsMouseOverDrawer = _panelsUnderPointer > 0;
        }

        /// <summary>
        /// Notify the controller that the pointer has left a HUD panel.
        /// Call from each panel's PointerLeaveEvent callback.
        /// </summary>
        public static void NotifyPointerLeavePanel()
        {
            _panelsUnderPointer = Mathf.Max(0, _panelsUnderPointer - 1);
            IsMouseOverDrawer = _panelsUnderPointer > 0;
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
            _ghostBuildableId = buildableId;
            _ghostRotation    = 0;
            InBuildMode       = true;
            Debug.Log($"[WaystationHUDController] Beginning ghost placement: {buildableId}");
        }

        /// <summary>Cancels the current ghost placement session.</summary>
        public void CancelPlacement()
        {
            _ghostBuildableId = null;
            _ghostRotation    = 0;
            InBuildMode       = false;
        }
    }
}
