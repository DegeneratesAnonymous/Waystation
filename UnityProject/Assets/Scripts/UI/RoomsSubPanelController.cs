// RoomsSubPanelController.cs
// Station → Rooms sub-tab panel (UI-008).
//
// Displays:
//   * Filter chip strip — "ALL" chip plus one chip per distinct assigned room type;
//                         clicking a chip filters the room list to that type.
//   * Room list          — scrollable list showing, for each room:
//                            • room name (custom or fallback)
//                            • type badge (coloured label, or dim "Unassigned")
//                            • NPC count
//                          Clicking a row fires OnRoomRowClicked so the HUD
//                          controller can call OpenRoomPanel(roomId).
//
// Data is pushed via Refresh(StationState, RoomSystem, ContentRegistry).
// Call on mount and again on every OnTick while the panel is active.
// The panel also auto-refreshes when RoomSystem.OnLayoutChanged fires.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController, which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Station → Rooms sub-tab panel. Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class RoomsSubPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks a room row.
        /// Argument is the room key (roomId) to pass to OpenRoomPanel.
        /// </summary>
        public event Action<string> OnRoomRowClicked;

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass      = "ws-rooms-panel";
        private const string FilterStripClass = "ws-rooms-panel__filter-strip";
        private const string FilterBtnClass   = "ws-rooms-panel__filter-btn";
        private const string FilterBtnActive  = "ws-rooms-panel__filter-btn--active";
        private const string ListClass        = "ws-rooms-panel__room-list";
        private const string RowClass         = "ws-rooms-panel__room-row";
        private const string RowNameClass     = "ws-rooms-panel__room-name";
        private const string RowBadgeClass    = "ws-rooms-panel__room-badge";
        private const string RowNpcClass      = "ws-rooms-panel__room-npc";
        private const string EmptyClass       = "ws-rooms-panel__empty";

        // Sentinel key used in _filterButtons for the "All" chip (active filter = null).
        private const string AllFilterKey = "__all__";

        // ── Child elements ─────────────────────────────────────────────────────

        private readonly VisualElement _filterStrip;
        private readonly VisualElement _roomList;
        private readonly Label         _emptyLabel;

        // ── State ──────────────────────────────────────────────────────────────

        // Currently active type-filter; null means "All".
        private string _activeFilter;

        // Latest rooms snapshot; rebuilt in Refresh().
        private List<RoomInfo> _rooms = new List<RoomInfo>();

        // Filter button references keyed by type id; AllFilterKey sentinel = "All" button.
        private readonly Dictionary<string, Button> _filterButtons =
            new Dictionary<string, Button>(StringComparer.Ordinal);

        // ── Constructor ────────────────────────────────────────────────────────

        public RoomsSubPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // ── Filter chip strip ──────────────────────────────────────────────
            _filterStrip = new VisualElement();
            _filterStrip.AddToClassList(FilterStripClass);
            _filterStrip.style.flexDirection = FlexDirection.Row;
            _filterStrip.style.flexWrap      = Wrap.Wrap;
            _filterStrip.style.marginBottom  = 6;
            Add(_filterStrip);

            // ── Room list (scrollable) ─────────────────────────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            _roomList = scroll.contentContainer;
            _roomList.AddToClassList(ListClass);
            _roomList.style.flexDirection = FlexDirection.Column;

            _emptyLabel = new Label("No rooms defined. Place floors to create rooms.");
            _emptyLabel.AddToClassList(EmptyClass);
            _emptyLabel.style.opacity  = 0.5f;
            _emptyLabel.style.marginTop = 8;
            _roomList.Add(_emptyLabel);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the filter chips and room rows from the latest station state.
        /// Call once on mount and again on every OnTick while the panel is active,
        /// or in response to <see cref="RoomSystem.OnLayoutChanged"/>.
        /// </summary>
        public void Refresh(StationState station, RoomSystem rooms, ContentRegistry registry)
        {
            if (station == null || rooms == null) return;

            _rooms = rooms.GetAllRooms(station, registry);
            RebuildFilterStrip();
            RebuildRoomList();
        }

        // ── Filter strip ───────────────────────────────────────────────────────

        private void RebuildFilterStrip()
        {
            _filterStrip.Clear();
            _filterButtons.Clear();

            // Always start with an "ALL" chip.
            AddFilterChip(null, "ALL");

            // One chip per distinct type currently assigned to at least one room.
            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var room in _rooms)
            {
                if (string.IsNullOrEmpty(room.assignedTypeId)) continue;
                if (!seenTypes.Add(room.assignedTypeId)) continue;

                string label = !string.IsNullOrEmpty(room.typeName)
                    ? room.typeName.ToUpper()
                    : room.assignedTypeId.ToUpper();
                AddFilterChip(room.assignedTypeId, label);
            }

            // Ensure a valid chip remains selected after the type set changes.
            if (_activeFilter != null && !seenTypes.Contains(_activeFilter))
                _activeFilter = null;

            UpdateFilterButtonStyles();
        }

        private void AddFilterChip(string typeId, string label)
        {
            var btn = new Button();
            btn.AddToClassList(FilterBtnClass);
            btn.text              = label;
            btn.style.marginRight  = 3;
            btn.style.marginBottom = 3;

            string capturedId = typeId;
            btn.RegisterCallback<ClickEvent>(_ => OnFilterChipClicked(capturedId));
            _filterStrip.Add(btn);

            // Key: use AllFilterKey for the "All" chip to avoid conflicts with type IDs.
            string key = typeId ?? AllFilterKey;
            _filterButtons[key] = btn;
        }

        private void OnFilterChipClicked(string typeId)
        {
            _activeFilter = typeId;
            UpdateFilterButtonStyles();
            ApplyFilter();
        }

        private void UpdateFilterButtonStyles()
        {
            foreach (var kv in _filterButtons)
            {
                // Active when the stored key matches the current filter:
                // AllFilterKey → active when no type filter is selected.
                // type id key  → active when it equals _activeFilter.
                bool isActive = (kv.Key == AllFilterKey) ? (_activeFilter == null)
                                                         : (kv.Key == _activeFilter);
                kv.Value.EnableInClassList(FilterBtnActive, isActive);
            }
        }

        // ── Room list ──────────────────────────────────────────────────────────

        private void RebuildRoomList()
        {
            _roomList.Clear();

            if (_rooms.Count == 0)
            {
                _roomList.Add(_emptyLabel);
                return;
            }

            foreach (var room in _rooms)
            {
                var row = BuildRoomRow(room);
                _roomList.Add(row);
            }

            ApplyFilter();
        }

        private VisualElement BuildRoomRow(RoomInfo room)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection     = FlexDirection.Row;
            row.style.justifyContent    = Justify.SpaceBetween;
            row.style.alignItems        = Align.Center;
            row.style.paddingTop        = 5;
            row.style.paddingBottom     = 5;
            row.style.paddingLeft       = 6;
            row.style.paddingRight      = 6;
            row.style.marginBottom      = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.35f, 0.5f);

            // Store the type id on the row so ApplyFilter can use it.
            row.userData = room.assignedTypeId;

            // Room name
            var nameLabel = new Label(room.displayName);
            nameLabel.AddToClassList(RowNameClass);
            nameLabel.style.flexGrow       = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            // Type badge
            bool assigned = !string.IsNullOrEmpty(room.assignedTypeId);
            string badgeText = assigned ? room.typeName ?? room.assignedTypeId : "unassigned";
            var badge = new Label(badgeText.ToUpper());
            badge.AddToClassList(RowBadgeClass);
            badge.style.fontSize      = 9;
            badge.style.paddingLeft   = 4;
            badge.style.paddingRight  = 4;
            badge.style.paddingTop    = 2;
            badge.style.paddingBottom = 2;
            badge.style.marginLeft    = 4;
            badge.style.marginRight   = 4;
            badge.style.borderTopLeftRadius     = 3;
            badge.style.borderTopRightRadius    = 3;
            badge.style.borderBottomLeftRadius  = 3;
            badge.style.borderBottomRightRadius = 3;

            if (assigned)
            {
                badge.style.backgroundColor = new Color(0.2f, 0.45f, 0.6f, 0.6f);
                badge.style.color           = new Color(0.8f, 0.92f, 1f, 1f);
            }
            else
            {
                badge.style.backgroundColor = new Color(0.3f, 0.3f, 0.32f, 0.35f);
                badge.style.color           = new Color(0.6f, 0.6f, 0.65f, 0.7f);
            }

            // NPC count
            string npcText = room.npcCount > 0
                ? $"{room.npcCount} NPC{(room.npcCount != 1 ? "s" : "")}"
                : "";
            var npcLabel = new Label(npcText);
            npcLabel.AddToClassList(RowNpcClass);
            npcLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            npcLabel.style.opacity        = 0.6f;
            npcLabel.style.minWidth       = 36;

            row.Add(nameLabel);
            row.Add(badge);
            row.Add(npcLabel);

            // Click handler
            string capturedRoomKey = room.roomKey;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                Debug.Log($"[RoomsSubPanel] Room row clicked: {capturedRoomKey}");
                OnRoomRowClicked?.Invoke(capturedRoomKey);
            });

            return row;
        }

        private void ApplyFilter()
        {
            foreach (var child in _roomList.Children())
            {
                // Skip non-row elements (e.g. the empty label).
                if (!child.ClassListContains(RowClass)) continue;

                bool visible = _activeFilter == null ||
                               (child.userData is string typeId && typeId == _activeFilter);
                child.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
