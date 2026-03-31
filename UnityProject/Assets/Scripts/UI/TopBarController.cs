// TopBarController — persistent top bar for the Waystation UI Toolkit HUD.
//
// Activated as part of WaystationHUDController when FeatureFlags.UseUIToolkitHUD is true.
//
// Layout (left to right):
//   [Location name]  [Tick / Day-Night]  |  [Pause][1×][2×][3×]  |  [Alert badge]
//   Alert tray (expands below the bar on badge click)
//
// Dependencies injected after construction via InjectDependencies():
//   • ITopBarGameManager — for IsPaused, SetSpeed, SecondsPerTick
//   • LogEntryBuffer     — for UnreadAlertCount, OnBufferChanged
//   • ViewContextManager — for CurrentContextName, OnContextChanged
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Waystation.UI
{
    /// <summary>
    /// Persistent top bar: location name, time, speed controls, and expandable alert tray.
    /// </summary>
    public class TopBarController : VisualElement
    {
        // ── Speed preset table ────────────────────────────────────────────────
        // (label, secondsPerTick)  — Pause handled separately.
        // 1× uses the GameManager baseline of 0.5s/tick; faster presets halve it.
        private static readonly (string Label, float SecondsPerTick)[] SpeedPresets =
        {
            ("1×", 0.5f),
            ("2×", 0.25f),
            ("3×", 1f / 6f),
        };

        // ── Child elements ────────────────────────────────────────────────────
        private readonly Label         _locationLabel;
        private readonly Label         _timeLabel;
        private readonly Label         _dayNightLabel;
        private readonly Button        _pauseButton;
        private readonly Button[]      _speedButtons;
        private readonly Label         _alertBadge;
        private readonly VisualElement _alertTray;
        private readonly VisualElement _alertTrayList;

        // ── State ─────────────────────────────────────────────────────────────
        private bool           _trayOpen;
        private VisualElement  _clickOutsideRoot;

        // ── Dependencies ──────────────────────────────────────────────────────
        private ITopBarGameManager _gm;
        private LogEntryBuffer     _buffer;
        private ViewContextManager _ctxMgr;

        // ── Constructor ───────────────────────────────────────────────────────

        public TopBarController()
        {
            AddToClassList("ws-top-bar");

            // Load the stylesheet if not already loaded
            var styleSheet = Resources.Load<StyleSheet>("UI/Styles/WaystationComponents");
            if (styleSheet != null && !styleSheets.Contains(styleSheet))
                styleSheets.Add(styleSheet);

            // Apply basic inline styles to ensure visibility
            style.flexDirection = FlexDirection.Column;
            style.alignSelf = Align.Stretch;
            style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            style.paddingBottom = 0;
            // Ensure top bar renders above the side panel's absolute positioning
            style.position = Position.Relative;

            // ── Bar row ───────────────────────────────────────────────────────
            var bar = new VisualElement();
            bar.AddToClassList("ws-top-bar__bar");
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.alignSelf = Align.Stretch;
            bar.style.justifyContent = Justify.FlexStart;
            bar.style.paddingLeft = 14;
            bar.style.paddingRight = 14;
            bar.style.paddingTop = 4;
            bar.style.paddingBottom = 4;
            bar.style.minHeight = 30;
            bar.style.backgroundColor = new Color(0.08f, 0.08f, 0.11f, 1f);
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            Add(bar);

            // Location name
            _locationLabel = new Label("—");
            _locationLabel.AddToClassList("ws-top-bar__location");
            _locationLabel.style.minWidth = 160;
            _locationLabel.style.flexShrink = 0;
            _locationLabel.style.color = new Color(0.9f, 0.9f, 0.95f, 1f);
            _locationLabel.style.fontSize = 16;
            _locationLabel.style.whiteSpace = WhiteSpace.NoWrap;
            bar.Add(_locationLabel);

            // Spacer — pushes the right group to the far right edge
            var spacer = new VisualElement();
            spacer.AddToClassList("ws-top-bar__spacer");
            spacer.style.flexGrow = 1;
            bar.Add(spacer);

            // ── Right group: time + day/night + speed controls ────────────────
            var rightGroup = new VisualElement();
            rightGroup.style.flexDirection = FlexDirection.Row;
            rightGroup.style.alignItems = Align.Center;
            rightGroup.style.flexShrink = 0;
            bar.Add(rightGroup);

            // Time label
            _timeLabel = new Label("Day 1  00:00");
            _timeLabel.AddToClassList("ws-top-bar__time");
            _timeLabel.style.color = new Color(0.7f, 0.7f, 0.75f, 1f);
            _timeLabel.style.fontSize = 14;
            _timeLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _timeLabel.style.marginRight = 16;
            _timeLabel.style.minWidth = 120;
            rightGroup.Add(_timeLabel);

            // Day/Night label
            _dayNightLabel = new Label("Day");
            _dayNightLabel.AddToClassList("ws-top-bar__day-night");
            rightGroup.Add(_dayNightLabel);

            // Speed controls
            var speedGroup = new VisualElement();
            speedGroup.AddToClassList("ws-top-bar__speed-group");
            speedGroup.style.flexDirection = FlexDirection.Row;
            speedGroup.style.alignItems = Align.Center;
            rightGroup.Add(speedGroup);

            _pauseButton = new Button(OnPauseClicked) { text = "⏸" };
            _pauseButton.AddToClassList("ws-top-bar__speed-btn");
            _pauseButton.AddToClassList("ws-top-bar__speed-btn--pause");
            StyleSpeedButton(_pauseButton);
            speedGroup.Add(_pauseButton);

            _speedButtons = new Button[SpeedPresets.Length];
            for (int i = 0; i < SpeedPresets.Length; i++)
            {
                int captured = i;
                var btn = new Button(() => OnSpeedClicked(captured)) { text = SpeedPresets[i].Label };
                btn.AddToClassList("ws-top-bar__speed-btn");
                StyleSpeedButton(btn);
                speedGroup.Add(btn);
                _speedButtons[i] = btn;
            }

            // Alert badge (hidden by default)
            _alertBadge = new Label("0");
            _alertBadge.AddToClassList("ws-top-bar__alert-badge");
            _alertBadge.style.display = DisplayStyle.None;
            _alertBadge.style.minWidth = 20;
            _alertBadge.style.minHeight = 20;
            _alertBadge.style.paddingLeft = 4;
            _alertBadge.style.paddingRight = 4;
            _alertBadge.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f, 1f);
            _alertBadge.style.color = Color.white;
            _alertBadge.style.fontSize = 10;
            _alertBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            _alertBadge.RegisterCallback<ClickEvent>(_ => ToggleTray());
            bar.Add(_alertBadge);

            // ── Alert tray (below bar) ────────────────────────────────────────
            _alertTray = new VisualElement();
            _alertTray.AddToClassList("ws-top-bar__alert-tray");
            _alertTray.style.display = DisplayStyle.None;
            _alertTray.style.flexDirection = FlexDirection.Column;
            _alertTray.style.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1f);
            _alertTray.style.borderBottomWidth = 1;
            _alertTray.style.borderBottomColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            _alertTray.style.maxHeight = 240;
            _alertTray.style.overflow = Overflow.Hidden;
            Add(_alertTray);

            _alertTrayList = new VisualElement();
            _alertTrayList.AddToClassList("ws-top-bar__alert-tray-list");
            _alertTrayList.style.flexDirection = FlexDirection.Column;
            _alertTrayList.style.overflow = Overflow.Scroll;
            _alertTrayList.style.paddingLeft = 8;
            _alertTrayList.style.paddingRight = 8;
            _alertTrayList.style.paddingTop = 8;
            _alertTrayList.style.paddingBottom = 8;
            _alertTray.Add(_alertTrayList);
        }

        // ── Dependency injection ──────────────────────────────────────────────

        /// <summary>
        /// Injects runtime dependencies once the game has finished loading.
        /// </summary>
        public void InjectDependencies(
            ITopBarGameManager gm,
            LogEntryBuffer     buffer,
            ViewContextManager ctxMgr)
        {
            // Detach previous listeners if called more than once.
            Detach();

            _gm     = gm;
            _buffer = buffer;
            _ctxMgr = ctxMgr;

            if (_buffer != null)
                _buffer.OnBufferChanged += OnBufferChanged;

            if (_ctxMgr != null)
            {
                _ctxMgr.OnContextChanged += OnContextChanged;
                _locationLabel.text = _ctxMgr.CurrentContextName;
            }

            // Perform an initial refresh.
            RefreshBadge();
            RefreshSpeedButtons();
        }

        /// <summary>
        /// Called by WaystationHUDController on every GameManager.OnTick.
        /// </summary>
        public void OnTick(StationState station)
        {
            if (station == null) return;

            _timeLabel.text    = TimeSystem.TimeLabel(station);
            _dayNightLabel.text = TimeSystem.IsDayPhase(station) ? "Day" : "Night";

            // Reflect pause / active-speed state each tick.
            RefreshSpeedButtons();
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        /// <summary>Detaches all event listeners.  Call before destroy.</summary>
        public void Detach()
        {
            if (_buffer != null)
                _buffer.OnBufferChanged -= OnBufferChanged;
            if (_ctxMgr != null)
                _ctxMgr.OnContextChanged -= OnContextChanged;
            if (_clickOutsideRoot != null)
            {
                _clickOutsideRoot.UnregisterCallback<ClickEvent>(OnRootClick, TrickleDown.TrickleDown);
                _clickOutsideRoot = null;
            }

            _gm     = null;
            _buffer = null;
            _ctxMgr = null;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnBufferChanged()
        {
            RefreshBadge();
        }

        private void OnContextChanged(string contextName)
        {
            _locationLabel.text = contextName;
        }

        // ── Speed controls ────────────────────────────────────────────────────

        private static void StyleSpeedButton(Button btn)
        {
            btn.style.minWidth = 36;
            btn.style.height = 28;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 6;
            btn.style.paddingRight = 6;
            btn.style.marginRight = 4;
            btn.style.backgroundColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            btn.style.color = new Color(0.85f, 0.85f, 0.9f, 1f);
            btn.style.fontSize = 14;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.borderTopWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderBottomWidth = 1;
            btn.style.borderBottomColor = new Color(0.3f, 0.3f, 0.35f, 1f);
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
        }

        private void OnPauseClicked()
        {
            if (_gm == null) return;
            _gm.IsPaused = !_gm.IsPaused;
            RefreshSpeedButtons();
        }

        private void OnSpeedClicked(int presetIndex)
        {
            if (_gm == null) return;
            float ticksPerSecond = 1f / SpeedPresets[presetIndex].SecondsPerTick;
            _gm.SetSpeed(ticksPerSecond);
            _gm.IsPaused = false;
            RefreshSpeedButtons();
        }

        // ── Alert badge ───────────────────────────────────────────────────────

        private void RefreshBadge()
        {
            int unread = _buffer?.UnreadAlertCount ?? 0;
            _alertBadge.text = unread.ToString();
            _alertBadge.style.display = unread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Alert tray ────────────────────────────────────────────────────────

        private void ToggleTray()
        {
            if (_trayOpen)
                CloseTray();
            else
                OpenTray();
        }

        private void OpenTray()
        {
            _trayOpen = true;
            _alertTray.style.display = DisplayStyle.Flex;
            RebuildTrayList();

            // Mark all as read and refresh badge.
            _buffer?.MarkAllRead();
        }

        private void CloseTray()
        {
            _trayOpen = false;
            _alertTray.style.display = DisplayStyle.None;
        }

        private void RebuildTrayList()
        {
            _alertTrayList.Clear();

            IReadOnlyList<AlertEntry> entries = _buffer?.GetSortedByUrgency()
                                                ?? (IReadOnlyList<AlertEntry>)Array.Empty<AlertEntry>();

            if (entries.Count == 0)
            {
                var empty = new Label("No alerts");
                empty.AddToClassList("ws-top-bar__alert-tray-empty");
                _alertTrayList.Add(empty);
                return;
            }

            foreach (var entry in entries)
            {
                var row = new VisualElement();
                row.AddToClassList("ws-top-bar__alert-tray-row");

                var categoryLabel = new Label(entry.Category.ToString());
                categoryLabel.AddToClassList("ws-top-bar__alert-tray-category");
                row.Add(categoryLabel);

                var msgLabel = new Label(entry.Message);
                msgLabel.AddToClassList("ws-top-bar__alert-tray-message");
                row.Add(msgLabel);

                // Capture for lambda.
                AlertEntry captured = entry;
                row.RegisterCallback<ClickEvent>(_ => OnTrayEntryClicked(captured));

                _alertTrayList.Add(row);
            }
        }

        private void OnTrayEntryClicked(AlertEntry entry)
        {
            // Navigate to the relevant panel.
            // Panel implementations are added in subsequent Work Orders.
            // For now, close the tray; panel routing will be wired when panels are ready.
            Debug.Log($"[TopBarController] Alert entry clicked: [{entry.Category}] {entry.Message}");
            CloseTray();
        }

        // ── Click-outside handler ─────────────────────────────────────────────

        /// <summary>
        /// Register a click-outside handler on the root so the tray closes
        /// when the player clicks anywhere outside the top bar.
        /// Call once after the panel is attached to the hierarchy.
        /// The handler is automatically unregistered when this element is
        /// detached from the panel.
        /// </summary>
        public void RegisterClickOutside(VisualElement root)
        {
            if (root == null) return;

            _clickOutsideRoot = root;
            root.RegisterCallback<ClickEvent>(OnRootClick, TrickleDown.TrickleDown);

            // Auto-unregister when this element leaves the panel hierarchy,
            // preventing stale callbacks if the HUD is rebuilt.
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            if (_clickOutsideRoot != null)
            {
                _clickOutsideRoot.UnregisterCallback<ClickEvent>(OnRootClick, TrickleDown.TrickleDown);
                _clickOutsideRoot = null;
            }
            UnregisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        private void OnRootClick(ClickEvent evt)
        {
            if (!_trayOpen) return;

            // Walk up the target's ancestor chain; if we reach this element the
            // click is inside the top bar — do not close the tray.
            var target = evt.target as VisualElement;
            while (target != null)
            {
                if (target == this) return;
                target = target.parent;
            }

            CloseTray();
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        /// <summary>Current location label text. Exposed for unit tests.</summary>
        public string LocationText    => _locationLabel.text;

        /// <summary>Current time label text. Exposed for unit tests.</summary>
        public string TimeText        => _timeLabel.text;

        /// <summary>Current day/night label text. Exposed for unit tests.</summary>
        public string DayNightText    => _dayNightLabel.text;

        /// <summary>Alert badge display value. Exposed for unit tests.</summary>
        public string BadgeText       => _alertBadge.text;

        /// <summary>Whether the alert badge is visible. Exposed for unit tests.</summary>
        public bool   BadgeVisible    => _alertBadge.style.display == DisplayStyle.Flex;

        /// <summary>Whether the alert tray is open. Exposed for unit tests.</summary>
        public bool   TrayOpen        => _trayOpen;

        /// <summary>
        /// Whether the pause button carries the active CSS class. Exposed for unit tests.
        /// </summary>
        public bool   PauseButtonActive
            => _pauseButton.ClassListContains("ws-top-bar__speed-btn--active");

        /// <summary>
        /// Whether the speed button at <paramref name="index"/> carries the active CSS class.
        /// Exposed for unit tests.
        /// </summary>
        public bool SpeedButtonActive(int index)
            => index >= 0 && index < _speedButtons.Length &&
               _speedButtons[index].ClassListContains("ws-top-bar__speed-btn--active");

        /// <summary>
        /// Refreshes which speed/pause button shows the active CSS class based on the
        /// current <see cref="ITopBarGameManager"/> state.
        /// Called automatically each tick and after every button click; also exposed
        /// so unit tests can trigger a refresh after mutating the stub game-manager.
        /// </summary>
        public void RefreshSpeedButtons()
        {
            if (_gm == null) return;

            bool paused = _gm.IsPaused;
            _pauseButton.EnableInClassList("ws-top-bar__speed-btn--active", paused);

            for (int i = 0; i < SpeedPresets.Length; i++)
            {
                bool active = !paused &&
                              Mathf.Approximately(_gm.SecondsPerTick, SpeedPresets[i].SecondsPerTick);
                _speedButtons[i].EnableInClassList("ws-top-bar__speed-btn--active", active);
            }
        }

        /// <summary>Programmatically toggle the tray (used by unit tests).</summary>
        public void SimulateBadgeClick() => ToggleTray();
    }
}
