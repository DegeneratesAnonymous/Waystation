// EventLogController.cs
// Persistent event log strip anchored to the bottom of the HUD (WO-UI-003).
//
// Structure:
//   EventLogController  (ws-event-log, position: absolute, bottom: 0, full-width)
//     Header row        (ws-event-log__header, 32px, always visible)
//       Alert accent    (ws-event-log__alert-accent, visible on Alert entries)
//       Category icon   (ws-event-log__cat-icon)
//       Preview text    (ws-event-log__preview-text, truncated single line)
//       Chevron         (ws-event-log__chevron, rotates on expand)
//     Body              (ws-event-log__body, max-height transitions 0 ↔ 280px)
//       Filter chips    (ws-event-log__filter-row)
//       Entry list      (ws-event-log__entry-list, scrollable)
//         Tick label    (ws-event-log__tick-label, per-tick group header)
//         LogEntryView × n
//
// IsMouseOverStrip: true while the pointer is over the strip (collapsed or expanded).
// WaystationHUDController reads this in Update() to set GameHUD.IsMouseOverDrawer.
//
// Navigation: clicking a navigate shortcut calls the supplied navigation callbacks
// and collapses the strip.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Persistent bottom strip showing the event log.
    /// Collapsed: 32px header.  Expanded: header + up to 280px scrollable entry list.
    /// </summary>
    public class EventLogController : VisualElement
    {
        // ── Layout constants ──────────────────────────────────────────────────
        private const int BodyMaxHeight    = 280; // total expanded body height in px
        private const int FilterRowHeight  =  40; // approximate height of filter chip row
        private const int EntryListMaxHeight = BodyMaxHeight - FilterRowHeight;
        private static readonly (string Label, LogCategory? Category)[] FilterDefs =
        {
            ("ALL",     null),
            ("CREW",    LogCategory.Crew),
            ("STATION", LogCategory.Station),
            ("WORLD",   LogCategory.World),
            ("ALERTS",  LogCategory.Alert),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private bool         _expanded;
        private LogCategory? _activeFilter;  // null = All
        private int          _currentTick;
        private int          _pointerCount;

        // ── Child elements ────────────────────────────────────────────────────
        private readonly VisualElement _header;
        private readonly VisualElement _alertAccent;
        private readonly VisualElement _catIconEl;
        private readonly Label         _previewLabel;
        private readonly Label         _chevronLabel;
        private readonly VisualElement _body;
        private readonly VisualElement _filterRow;
        private readonly ScrollView    _entryList;
        private readonly Button[]      _filterButtons;

        // ── Dependencies (navigation callbacks) ──────────────────────────────
        private Action<string>         _selectCrewMember;   // (npcUid)
        private Action<string>         _openRoomPanel;      // (roomId)
        private Action                 _openNetworkOverlay;
        private Action<string>         _openVisitorPanel;   // (visitorId)
        private Action<string>         _openFleetPanel;     // (shipUid)

        // ── Public properties ─────────────────────────────────────────────────

        /// <summary>True while the pointer is anywhere over the strip.</summary>
        public bool IsMouseOverStrip => _pointerCount > 0;

        /// <summary>True when the strip is currently in expanded state.</summary>
        public bool IsExpanded => _expanded;

        /// <summary>The currently active category filter, or null for All.</summary>
        public LogCategory? ActiveFilter => _activeFilter;

        // ── Constructor ───────────────────────────────────────────────────────

        public EventLogController()
        {
            AddToClassList("ws-event-log");

            // Anchor to the bottom of the parent container (full width)
            style.position = Position.Absolute;
            style.left     = 0;
            style.right    = 0;
            style.bottom   = 0;
            style.flexDirection = FlexDirection.Column;

            // ── Load stylesheet ───────────────────────────────────────────────
            var sheet = Resources.Load<StyleSheet>("UI/EventLog/EventLogStrip");
            if (sheet != null)
                styleSheets.Add(sheet);

            // ── Pointer tracking ──────────────────────────────────────────────
            RegisterCallback<PointerEnterEvent>(_ => _pointerCount++);
            RegisterCallback<PointerLeaveEvent>(_ => _pointerCount = Mathf.Max(0, _pointerCount - 1));

            // ── Header row ────────────────────────────────────────────────────
            _header = new VisualElement();
            _header.AddToClassList("ws-event-log__header");
            _header.style.flexDirection = FlexDirection.Row;
            _header.style.alignItems    = Align.Center;
            _header.style.height        = 32;
            _header.style.paddingLeft   = 8;
            _header.style.paddingRight  = 8;
            _header.style.backgroundColor = new Color(0.14f, 0.16f, 0.22f, 1f);
            _header.style.borderTopWidth  = 1;
            _header.style.borderTopColor  = new Color(0.22f, 0.26f, 0.36f, 1f);
            _header.RegisterCallback<ClickEvent>(_ => Toggle());
            Add(_header);

            // Alert accent (left edge, visible on Alert entries)
            _alertAccent = new VisualElement();
            _alertAccent.AddToClassList("ws-event-log__alert-accent");
            _alertAccent.style.width     = 3;
            _alertAccent.style.alignSelf = Align.Stretch;
            _alertAccent.style.backgroundColor = new Color(0.75f, 0.18f, 0.18f, 1f);
            _alertAccent.style.display   = DisplayStyle.None;
            _alertAccent.style.marginRight = 6;
            _header.Add(_alertAccent);

            // Category icon
            _catIconEl = new VisualElement();
            _catIconEl.AddToClassList("ws-event-log__cat-icon");
            _catIconEl.style.width  = 16;
            _catIconEl.style.height = 16;
            _catIconEl.style.alignItems     = Align.Center;
            _catIconEl.style.justifyContent = Justify.Center;
            _catIconEl.style.flexShrink     = 0;
            _catIconEl.style.marginRight    = 6;
            _header.Add(_catIconEl);

            // Preview text
            _previewLabel = new Label("—");
            _previewLabel.AddToClassList("ws-event-log__preview-text");
            _previewLabel.style.flexGrow = 1;
            _previewLabel.style.fontSize = 8;
            _previewLabel.style.color    = new Color(0.65f, 0.82f, 1f, 0.75f);
            _previewLabel.style.overflow = Overflow.Hidden;
            _header.Add(_previewLabel);

            // Chevron
            _chevronLabel = new Label("▲");
            _chevronLabel.AddToClassList("ws-event-log__chevron");
            _chevronLabel.style.fontSize    = 8;
            _chevronLabel.style.color       = new Color(0.46f, 0.60f, 0.78f, 1f);
            _chevronLabel.style.flexShrink  = 0;
            _chevronLabel.style.marginLeft  = 6;
            _header.Add(_chevronLabel);

            // ── Body (collapsed by default: max-height 0, opacity 0) ──────────
            _body = new VisualElement();
            _body.AddToClassList("ws-event-log__body");
            _body.style.overflow          = Overflow.Hidden;
            _body.style.maxHeight         = 0;
            _body.style.opacity           = 0;
            _body.style.backgroundColor   = new Color(0.11f, 0.13f, 0.18f, 1f);
            _body.style.borderTopWidth    = 1;
            _body.style.borderTopColor    = new Color(0.16f, 0.19f, 0.26f, 1f);
            Add(_body);

            // ── Filter chips row ──────────────────────────────────────────────
            _filterRow = new VisualElement();
            _filterRow.AddToClassList("ws-event-log__filter-row");
            _filterRow.style.flexDirection  = FlexDirection.Row;
            _filterRow.style.alignItems     = Align.Center;
            _filterRow.style.paddingLeft    = 8;
            _filterRow.style.paddingRight   = 8;
            _filterRow.style.paddingTop     = 4;
            _filterRow.style.paddingBottom  = 4;
            _filterRow.style.backgroundColor = new Color(0.14f, 0.16f, 0.22f, 1f);
            _filterRow.style.borderBottomWidth = 1;
            _filterRow.style.borderBottomColor = new Color(0.16f, 0.19f, 0.26f, 1f);
            _body.Add(_filterRow);

            _filterButtons = new Button[FilterDefs.Length];
            for (int i = 0; i < FilterDefs.Length; i++)
            {
                int captured = i;
                var btn = new Button();
                btn.AddToClassList("ws-event-log__filter-chip");
                btn.text = FilterDefs[i].Label;
                btn.style.fontSize      = 7;
                btn.style.paddingLeft   = 5;
                btn.style.paddingRight  = 5;
                btn.style.paddingTop    = 2;
                btn.style.paddingBottom = 2;
                btn.style.marginRight   = 4;
                btn.style.borderTopWidth    = 1;
                btn.style.borderBottomWidth = 1;
                btn.style.borderLeftWidth   = 1;
                btn.style.borderRightWidth  = 1;
                btn.style.borderTopLeftRadius     = 0;
                btn.style.borderTopRightRadius    = 0;
                btn.style.borderBottomLeftRadius  = 0;
                btn.style.borderBottomRightRadius = 0;
                btn.RegisterCallback<ClickEvent>(_ => SetFilter(FilterDefs[captured].Category));
                _filterRow.Add(btn);
                _filterButtons[i] = btn;
            }

            // ── Entry list ────────────────────────────────────────────────────
            _entryList = new ScrollView(ScrollViewMode.Vertical);
            _entryList.AddToClassList("ws-event-log__entry-list");
            _entryList.style.flexGrow = 1;
            _entryList.style.maxHeight = EntryListMaxHeight;
            _body.Add(_entryList);

            // Initial visual state
            RefreshFilterButtons();
        }

        // ── Dependency injection ──────────────────────────────────────────────

        /// <summary>
        /// Injects navigation callbacks.  Call once the game has loaded.
        /// </summary>
        public void InjectNavigationCallbacks(
            Action<string> selectCrewMember,
            Action<string> openRoomPanel,
            Action openNetworkOverlay,
            Action<string> openVisitorPanel,
            Action<string> openFleetPanel)
        {
            _selectCrewMember  = selectCrewMember;
            _openRoomPanel     = openRoomPanel;
            _openNetworkOverlay = openNetworkOverlay;
            _openVisitorPanel  = openVisitorPanel;
            _openFleetPanel    = openFleetPanel;
        }

        // ── Tick update ───────────────────────────────────────────────────────

        /// <summary>
        /// Updates the current tick (used for relative timestamps in entry rows).
        /// Called by WaystationHUDController on each game tick.
        /// </summary>
        public void OnTick(int tick)
        {
            _currentTick = tick;
        }

        // ── Buffer subscription ───────────────────────────────────────────────

        /// <summary>
        /// Called when <see cref="EventLogBuffer.OnBufferChanged"/> fires.
        /// Refreshes the collapsed preview and, if expanded, rebuilds the entry list.
        /// </summary>
        public void OnBufferChanged()
        {
            RefreshCollapsedPreview();
            if (_expanded)
                RebuildEntryList();
        }

        // ── Expand / collapse ─────────────────────────────────────────────────

        /// <summary>Toggles between expanded and collapsed state.</summary>
        public void Toggle() => SetExpanded(!_expanded);

        /// <summary>Sets the expanded/collapsed state explicitly.</summary>
        public void SetExpanded(bool expanded)
        {
            _expanded = expanded;

            if (expanded)
            {
                _body.style.maxHeight = 280;
                _body.style.opacity   = 1;
                _body.EnableInClassList("ws-event-log__body--open", true);
                _chevronLabel.text = "▼";
                _chevronLabel.EnableInClassList("ws-event-log__chevron--open", true);
                RebuildEntryList();
            }
            else
            {
                _body.style.maxHeight = 0;
                _body.style.opacity   = 0;
                _body.EnableInClassList("ws-event-log__body--open", false);
                _chevronLabel.text = "▲";
                _chevronLabel.EnableInClassList("ws-event-log__chevron--open", false);
            }
        }

        // ── Filter ────────────────────────────────────────────────────────────

        /// <summary>Activates the given category filter (null = All).</summary>
        public void SetFilter(LogCategory? category)
        {
            _activeFilter = category;
            RefreshFilterButtons();
            if (_expanded)
                RebuildEntryList();
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        /// <summary>Returns the text shown in the collapsed preview label.</summary>
        internal string PreviewText => _previewLabel.text;

        /// <summary>Returns the number of entry rows currently in the list.</summary>
        internal int VisibleEntryCount
        {
            get
            {
                int count = 0;
                foreach (var child in _entryList.contentContainer.Children())
                    if (child is LogEntryView) count++;
                return count;
            }
        }

        /// <summary>Simulates a pointer-enter event for test purposes.</summary>
        internal void SimulatePointerEnter() => _pointerCount++;

        /// <summary>Simulates a pointer-leave event for test purposes.</summary>
        internal void SimulatePointerLeave() => _pointerCount = Mathf.Max(0, _pointerCount - 1);

        // ── Private helpers ───────────────────────────────────────────────────

        private void RefreshCollapsedPreview()
        {
            var buf   = EventLogBuffer.Instance;
            var entry = buf.GetCollapsedEntry();

            if (entry == null)
            {
                _previewLabel.text = "No recent events.";
                _alertAccent.style.display = DisplayStyle.None;
                SetCatIcon(null);
                return;
            }

            _previewLabel.text = entry.BodyText ?? string.Empty;

            // Alert accent
            bool isAlert = entry.Category == LogCategory.Alert;
            _alertAccent.style.display = isAlert ? DisplayStyle.Flex : DisplayStyle.None;
            _header.EnableInClassList("ws-event-log__header--alert", isAlert);

            SetCatIcon(entry.Category);
        }

        private void SetCatIcon(LogCategory? cat)
        {
            // Remove previous icon child
            _catIconEl.Clear();
            if (cat == null) return;

            string glyph = cat.Value switch
            {
                LogCategory.Alert   => "!",
                LogCategory.Crew    => "●",
                LogCategory.Station => "■",
                LogCategory.World   => "◎",
                _                   => "●",
            };
            Color colour = cat.Value switch
            {
                LogCategory.Alert   => new Color(0.75f, 0.18f, 0.18f, 1f),
                LogCategory.Crew    => new Color(0.28f, 0.50f, 0.67f, 1f),
                LogCategory.Station => new Color(0.18f, 0.60f, 0.32f, 1f),
                LogCategory.World   => new Color(0.45f, 0.28f, 0.70f, 1f),
                _                   => new Color(0.65f, 0.65f, 0.75f, 1f),
            };

            var lbl = new Label(glyph);
            lbl.style.fontSize          = 10;
            lbl.style.color             = colour;
            lbl.style.unityTextAlign    = TextAnchor.MiddleCenter;
            _catIconEl.Add(lbl);
        }

        private void RebuildEntryList()
        {
            _entryList.contentContainer.Clear();

            var entries = EventLogBuffer.Instance.GetFiltered(_activeFilter);
            if (entries.Count == 0)
            {
                var emptyLabel = new Label("No entries.");
                emptyLabel.style.color    = new Color(0.46f, 0.60f, 0.78f, 0.5f);
                emptyLabel.style.fontSize = 8;
                emptyLabel.style.paddingLeft = 8;
                emptyLabel.style.paddingTop  = 8;
                _entryList.contentContainer.Add(emptyLabel);
                return;
            }

            // Group entries by tick
            int lastTick = -1;
            foreach (var entry in entries)
            {
                if (entry.TickFired != lastTick)
                {
                    var tickLabel = new Label($"Tick {entry.TickFired:N0}");
                    tickLabel.AddToClassList("ws-event-log__tick-label");
                    tickLabel.style.color       = new Color(0.29f, 0.38f, 0.50f, 1f);
                    tickLabel.style.fontSize    = 7;
                    tickLabel.style.paddingLeft = 8;
                    tickLabel.style.paddingTop  = 4;
                    _entryList.contentContainer.Add(tickLabel);
                    lastTick = entry.TickFired;
                }

                var view = new LogEntryView(entry, _currentTick, OnNavigate);
                _entryList.contentContainer.Add(view);
            }
        }

        private void RefreshFilterButtons()
        {
            for (int i = 0; i < FilterDefs.Length; i++)
            {
                bool active = FilterDefs[i].Category == _activeFilter;
                _filterButtons[i].EnableInClassList("ws-event-log__filter-chip--active", active);

                // Inline active/inactive colours (fallback when USS not loaded)
                if (active)
                {
                    _filterButtons[i].style.color = new Color(0.38f, 0.63f, 0.80f, 1f);
                    _filterButtons[i].style.borderTopColor    = new Color(0.28f, 0.50f, 0.67f, 1f);
                    _filterButtons[i].style.borderBottomColor = new Color(0.28f, 0.50f, 0.67f, 1f);
                    _filterButtons[i].style.borderLeftColor   = new Color(0.28f, 0.50f, 0.67f, 1f);
                    _filterButtons[i].style.borderRightColor  = new Color(0.28f, 0.50f, 0.67f, 1f);
                    _filterButtons[i].style.backgroundColor   = new Color(0.12f, 0.20f, 0.32f, 1f);
                }
                else
                {
                    _filterButtons[i].style.color = new Color(0.29f, 0.38f, 0.50f, 1f);
                    _filterButtons[i].style.borderTopColor    = new Color(0.14f, 0.17f, 0.22f, 1f);
                    _filterButtons[i].style.borderBottomColor = new Color(0.14f, 0.17f, 0.22f, 1f);
                    _filterButtons[i].style.borderLeftColor   = new Color(0.14f, 0.17f, 0.22f, 1f);
                    _filterButtons[i].style.borderRightColor  = new Color(0.14f, 0.17f, 0.22f, 1f);
                    _filterButtons[i].style.backgroundColor   = new Color(0.11f, 0.13f, 0.18f, 1f);
                }
            }
        }

        private void OnNavigate(string targetType, string targetId)
        {
            // Execute the navigation callback
            switch (targetType)
            {
                case "crew":
                    _selectCrewMember?.Invoke(targetId);
                    break;
                case "room":
                    _openRoomPanel?.Invoke(targetId);
                    break;
                case "network":
                    _openNetworkOverlay?.Invoke();
                    break;
                case "visitor":
                    _openVisitorPanel?.Invoke(targetId);
                    break;
                case "fleet":
                    _openFleetPanel?.Invoke(targetId);
                    break;
            }

            // Collapse the strip after navigation
            SetExpanded(false);
        }
    }
}
