// RoomPanelController.cs
// Room contextual panel (UI-024).
//
// Displays per-room inspection across four tabs:
//   Overview     — editable room name, type badge with active bonus description,
//                  list of NPCs currently in the room and their activities,
//                  aggregate atmosphere mood colour indicator.
//   Assign Type  — all registered room types with bonus descriptions;
//                  auto-suggest highlighted; Confirm button calls RoomSystem.AssignRoomType.
//   Contents     — list of all furniture and workbenches placed in the room;
//                  clicking a row fires OnWorkbenchRowClicked.
//   Networks     — connection status per network type (Electrical, Plumbing, Ducting, Fuel);
//                  current and target temperature; temperature source name.
//
// Opened from world room tile click or from Station → Rooms list via
//   WaystationHUDController.OpenRoomPanel(roomId).
//
// Data is pushed via Refresh(roomKey, station, registry, rooms, building, networks).
// Feature-flagged under FeatureFlags.UseUIToolkitHUD.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Room contextual panel.  Extends <see cref="VisualElement"/> so it can be
    /// added directly to the content area as a stacking overlay.
    /// </summary>
    public class RoomPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>Fired when the close button is clicked.</summary>
        public event Action OnCloseRequested;

        /// <summary>
        /// Fired when a workbench/furniture row in the Contents tab is clicked.
        /// Argument is the foundation uid.
        /// </summary>
        public event Action<string> OnWorkbenchRowClicked;

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass          = "ws-room-panel";
        private const string HeaderClass         = "ws-room-panel__header";
        private const string NameFieldClass      = "ws-room-panel__name-field";
        private const string CloseBtnClass       = "ws-room-panel__close-btn";
        private const string TabContentClass     = "ws-room-panel__tab-content";
        private const string SectionHeaderClass  = "ws-room-panel__section-header";
        private const string BadgeClass          = "ws-room-panel__badge";
        private const string BadgeActiveClass    = "ws-room-panel__badge--active";
        private const string EmptyClass          = "ws-room-panel__empty";
        private const string TypeRowClass        = "ws-room-panel__type-row";
        private const string TypeRowSelectedClass= "ws-room-panel__type-row--selected";
        private const string TypeRowSuggestClass = "ws-room-panel__type-row--suggested";
        private const string ContentRowClass     = "ws-room-panel__content-row";
        private const string NetworkRowClass     = "ws-room-panel__network-row";
        private const string StatusConnectedClass= "ws-room-panel__status--connected";
        private const string StatusSeveredClass  = "ws-room-panel__status--severed";
        private const string StatusNoneClass     = "ws-room-panel__status--none";
        private const string AtmoBarClass        = "ws-room-panel__atmo-bar";
        private const string NpcRowClass         = "ws-room-panel__npc-row";

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly TabStrip       _tabs;
        private readonly VisualElement  _tabContent;
        private string                  _activeTab = "overview";

        // Cached data for tab rebuilds.
        private string                  _roomKey;
        private StationState            _station;
        private ContentRegistry         _registry;
        private RoomSystem              _rooms;
        private BuildingSystem          _building;
        private UtilityNetworkManager   _networks;

        // Pending type selection in Assign Type tab (before Confirm is clicked).
        private string _pendingTypeId;

        // ── Constructor ────────────────────────────────────────────────────────

        public RoomPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection   = FlexDirection.Column;
            style.flexGrow        = 0;
            style.flexShrink      = 0;
            style.width           = 320;
            style.position        = Position.Absolute;
            style.right           = 0;
            style.top             = 0;
            style.bottom          = 0;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.97f);
            style.borderLeftWidth = 1;
            style.borderLeftColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

            // ── Header ──────────────────────────────────────────────────────────
            var header = new VisualElement();
            header.AddToClassList(HeaderClass);
            header.style.flexDirection     = FlexDirection.Row;
            header.style.alignItems        = Align.Center;
            header.style.paddingLeft       = 10;
            header.style.paddingRight      = 6;
            header.style.paddingTop        = 8;
            header.style.paddingBottom     = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

            var nameField = new TextField();
            nameField.name = "room-name-field";
            nameField.AddToClassList(NameFieldClass);
            nameField.style.flexGrow = 1;
            nameField.style.fontSize = 15;
            nameField.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameField.RegisterValueChangedCallback(OnNameFieldChanged);
            header.Add(nameField);

            var closeBtn = new Button(() => OnCloseRequested?.Invoke()) { text = "✕" };
            closeBtn.AddToClassList(CloseBtnClass);
            closeBtn.style.backgroundColor  = Color.clear;
            closeBtn.style.borderTopWidth   = 0;
            closeBtn.style.borderRightWidth  = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth   = 0;
            closeBtn.style.color             = new Color(0.7f, 0.7f, 0.75f);
            closeBtn.style.fontSize          = 14;
            header.Add(closeBtn);

            Add(header);

            // ── Tab content area ───────────────────────────────────────────────
            // Construct _tabContent BEFORE AddTab calls to avoid a null ref:
            // TabStrip.AddTab auto-selects the first tab and immediately fires
            // OnTabSelected → RebuildActiveTab, which writes into _tabContent.
            _tabContent = new ScrollView(ScrollViewMode.Vertical);
            _tabContent.AddToClassList(TabContentClass);
            _tabContent.style.flexGrow = 1;
            _tabContent.style.overflow = Overflow.Hidden;

            // ── Tab strip ──────────────────────────────────────────────────────
            _tabs = new TabStrip(TabStrip.Orientation.Horizontal);
            _tabs.OnTabSelected += OnTabSelected;
            _tabs.AddTab("OVERVIEW",     "overview");
            _tabs.AddTab("ASSIGN TYPE",  "assign_type");
            _tabs.AddTab("CONTENTS",     "contents");
            _tabs.AddTab("NETWORKS",     "networks");

            // Add in visual order: tab strip above the content area.
            Add(_tabs);
            Add(_tabContent);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Programmatically selects a tab by id.
        /// Valid ids: "overview" | "assign_type" | "contents" | "networks".
        /// </summary>
        public void SelectTab(string tabId) => _tabs?.SelectTab(tabId);

        /// <summary>
        /// Refreshes all tab content with current room data.
        /// Call once on mount and again every N ticks while the panel is visible.
        /// </summary>
        public void Refresh(
            string                roomKey,
            StationState          station,
            ContentRegistry       registry,
            RoomSystem            rooms,
            BuildingSystem        building,
            UtilityNetworkManager networks)
        {
            _roomKey  = roomKey;
            _station  = station;
            _registry = registry;
            _rooms    = rooms;
            _building = building;
            _networks = networks;

            // Update header name field.
            var nameField = this.Q<TextField>("room-name-field");
            if (nameField != null)
            {
                string displayName = ResolveDisplayName();
                // Only update when the field is not focused to avoid clobbering in-progress edits.
                bool isFocused = nameField.panel?.focusController?.focusedElement == nameField;
                if (!isFocused)
                    nameField.SetValueWithoutNotify(displayName);
            }

            RebuildActiveTab();
        }

        // ── Tab switching ──────────────────────────────────────────────────────

        private void OnTabSelected(string tabId)
        {
            _activeTab    = tabId;
            _pendingTypeId = null;  // reset pending selection on tab switch
            RebuildActiveTab();
        }

        private void RebuildActiveTab()
        {
            _tabContent.contentContainer.Clear();

            if (string.IsNullOrEmpty(_roomKey) || _station == null)
            {
                var empty = MakeEmptyLabel("No room selected.");
                empty.style.paddingLeft = 10;
                empty.style.paddingTop  = 10;
                _tabContent.contentContainer.Add(empty);
                return;
            }

            switch (_activeTab)
            {
                case "overview":    BuildOverviewTab();    break;
                case "assign_type": BuildAssignTypeTab();  break;
                case "contents":    BuildContentsTab();    break;
                case "networks":    BuildNetworksTab();    break;
            }
        }

        // ── Overview tab ───────────────────────────────────────────────────────

        private void BuildOverviewTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            // ── Type badge ─────────────────────────────────────────────────────
            AddSectionHeader(root, "ROOM TYPE");

            _station.playerRoomTypeAssignments.TryGetValue(_roomKey, out string assignedTypeId);

            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.alignItems    = Align.Center;
            badgeRow.style.marginBottom  = 8;

            var typeBadge = new Label();
            typeBadge.name = "type-badge";
            typeBadge.AddToClassList(BadgeClass);
            typeBadge.style.paddingLeft   = 6;
            typeBadge.style.paddingRight  = 6;
            typeBadge.style.paddingTop    = 2;
            typeBadge.style.paddingBottom = 2;
            typeBadge.style.borderTopLeftRadius     = 3;
            typeBadge.style.borderTopRightRadius    = 3;
            typeBadge.style.borderBottomLeftRadius  = 3;
            typeBadge.style.borderBottomRightRadius = 3;
            typeBadge.style.fontSize = 11;
            typeBadge.style.marginRight = 6;

            if (string.IsNullOrEmpty(assignedTypeId))
            {
                typeBadge.text = "UNASSIGNED";
                typeBadge.style.backgroundColor = new Color(0.3f, 0.3f, 0.35f);
                typeBadge.style.color           = new Color(0.55f, 0.55f, 0.6f);
            }
            else
            {
                string typeName = ResolveTypeName(assignedTypeId);
                typeBadge.text = typeName?.ToUpper() ?? assignedTypeId.ToUpper();
                typeBadge.AddToClassList(BadgeActiveClass);
                typeBadge.style.backgroundColor = new Color(0.18f, 0.45f, 0.62f);
                typeBadge.style.color           = new Color(0.85f, 0.92f, 1.0f);
            }
            badgeRow.Add(typeBadge);
            root.Add(badgeRow);

            // ── Active bonus description ───────────────────────────────────────
            if (!string.IsNullOrEmpty(assignedTypeId))
            {
                var typeDef = ResolveTypeDef(assignedTypeId);
                bool bonusActive = false;
                if (_station.roomBonusCache.TryGetValue(_roomKey, out var bonusState))
                    bonusActive = bonusState.bonusActive;

                if (typeDef != null && bonusActive)
                {
                    var bonusLabel = new Label();
                    bonusLabel.text = BuildBonusDescription(typeDef);
                    bonusLabel.style.fontSize     = 11;
                    bonusLabel.style.color        = new Color(0.4f, 0.85f, 0.55f);
                    bonusLabel.style.marginBottom = 8;
                    bonusLabel.style.whiteSpace   = WhiteSpace.Normal;
                    root.Add(bonusLabel);
                }
                else if (typeDef != null)
                {
                    var inactiveLabel = new Label("Bonus inactive — requirements not met.");
                    inactiveLabel.style.fontSize     = 11;
                    inactiveLabel.style.color        = new Color(0.6f, 0.6f, 0.5f);
                    inactiveLabel.style.marginBottom = 8;
                    inactiveLabel.style.whiteSpace   = WhiteSpace.Normal;
                    root.Add(inactiveLabel);
                }
            }

            // ── Atmosphere (aggregate mood) ────────────────────────────────────
            AddSectionHeader(root, "ATMOSPHERE");

            var npcList = GetNpcsInRoom();
            if (npcList.Count == 0)
            {
                root.Add(MakeEmptyLabel("No crew in this room."));
            }
            else
            {
                float avgMood = npcList.Average(n => n.moodScore);
                Color atmoColor = MoodSystem.GetMoodColor(avgMood);
                string moodLabel = MoodSystem.GetThresholdLabel(avgMood);

                var atmoRow = new VisualElement();
                atmoRow.style.flexDirection = FlexDirection.Row;
                atmoRow.style.alignItems    = Align.Center;
                atmoRow.style.marginBottom  = 8;

                var atmoBar = new VisualElement();
                atmoBar.AddToClassList(AtmoBarClass);
                atmoBar.style.width           = 12;
                atmoBar.style.height          = 12;
                atmoBar.style.borderTopLeftRadius     = 3;
                atmoBar.style.borderTopRightRadius    = 3;
                atmoBar.style.borderBottomLeftRadius  = 3;
                atmoBar.style.borderBottomRightRadius = 3;
                atmoBar.style.backgroundColor = atmoColor;
                atmoBar.style.marginRight     = 6;
                atmoRow.Add(atmoBar);

                var atmoLabel = new Label($"{moodLabel} ({avgMood:F0}/100)");
                atmoLabel.style.fontSize = 12;
                atmoRow.Add(atmoLabel);
                root.Add(atmoRow);
            }

            // ── NPC list ───────────────────────────────────────────────────────
            AddSectionHeader(root, "OCCUPANTS");

            if (npcList.Count == 0)
            {
                root.Add(MakeEmptyLabel("No occupants."));
            }
            else
            {
                foreach (var npc in npcList)
                {
                    var row = new VisualElement();
                    row.AddToClassList(NpcRowClass);
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.paddingTop    = 2;
                    row.style.paddingBottom = 2;

                    var nameLabel = new Label(npc.name ?? npc.uid);
                    nameLabel.style.flexGrow  = 1;
                    nameLabel.style.fontSize  = 12;
                    row.Add(nameLabel);

                    string activity = string.IsNullOrEmpty(npc.currentTaskId) ? "Idle" : npc.currentTaskId;
                    var actLabel = new Label(activity);
                    actLabel.style.fontSize = 11;
                    actLabel.style.color    = new Color(0.55f, 0.55f, 0.65f);
                    row.Add(actLabel);

                    root.Add(row);
                }
            }
        }

        // ── Assign Type tab ────────────────────────────────────────────────────

        private void BuildAssignTypeTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            // Collect all available room types (built-in from registry + custom).
            var allTypes = new List<RoomTypeDefinition>();
            if (_registry?.RoomTypes != null)
                allTypes.AddRange(_registry.RoomTypes.Values);
            foreach (var ct in _station.customRoomTypes)
                if (!allTypes.Any(t => t.id == ct.id))
                    allTypes.Add(ct);
            allTypes.Sort((a, b) => string.Compare(a.displayName, b.displayName,
                                                    StringComparison.OrdinalIgnoreCase));

            string autoSuggest  = _rooms?.GetAutoSuggest(_station, _roomKey);
            _station.playerRoomTypeAssignments.TryGetValue(_roomKey, out string currentTypeId);

            // Initialise pending selection to current assignment on first render.
            if (_pendingTypeId == null && !string.IsNullOrEmpty(currentTypeId))
                _pendingTypeId = currentTypeId;

            if (allTypes.Count == 0)
            {
                root.Add(MakeEmptyLabel("No room types defined."));
                return;
            }

            AddSectionHeader(root, "SELECT ROOM TYPE");
            if (!string.IsNullOrEmpty(autoSuggest))
            {
                string suggestName = allTypes.FirstOrDefault(t => t.id == autoSuggest)?.displayName
                                     ?? autoSuggest;
                var hintLabel = new Label($"Suggested: {suggestName} (based on workbenches)");
                hintLabel.style.fontSize     = 11;
                hintLabel.style.color        = new Color(0.9f, 0.75f, 0.3f);
                hintLabel.style.marginBottom = 6;
                hintLabel.style.whiteSpace   = WhiteSpace.Normal;
                root.Add(hintLabel);
            }

            // Type list (scrolls within the ScrollView).
            foreach (var typeDef in allTypes)
            {
                string typeId    = typeDef.id;
                bool isCurrent   = typeId == currentTypeId;
                bool isSelected  = typeId == _pendingTypeId;
                bool isSuggested = typeId == autoSuggest;

                var row = new VisualElement();
                row.name = $"type-row-{typeId}";
                row.AddToClassList(TypeRowClass);
                row.style.paddingLeft   = 8;
                row.style.paddingRight  = 8;
                row.style.paddingTop    = 6;
                row.style.paddingBottom = 6;
                row.style.marginBottom  = 4;
                row.style.borderTopLeftRadius     = 4;
                row.style.borderTopRightRadius    = 4;
                row.style.borderBottomLeftRadius  = 4;
                row.style.borderBottomRightRadius = 4;
                row.style.backgroundColor = isSelected
                    ? new Color(0.18f, 0.45f, 0.62f, 0.45f)
                    : isSuggested
                        ? new Color(0.55f, 0.45f, 0.1f, 0.3f)
                        : new Color(0.18f, 0.18f, 0.22f);

                if (isSelected)  row.AddToClassList(TypeRowSelectedClass);
                if (isSuggested) row.AddToClassList(TypeRowSuggestClass);

                var nameLabel = new Label(typeDef.displayName ?? typeId);
                nameLabel.style.fontSize = 13;
                if (isCurrent)
                    nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(nameLabel);

                string bonusDesc = BuildBonusDescription(typeDef);
                if (!string.IsNullOrEmpty(bonusDesc))
                {
                    var descLabel = new Label(bonusDesc);
                    descLabel.style.fontSize   = 11;
                    descLabel.style.color      = new Color(0.6f, 0.6f, 0.7f);
                    descLabel.style.whiteSpace = WhiteSpace.Normal;
                    descLabel.style.marginTop  = 2;
                    row.Add(descLabel);
                }

                if (isSuggested)
                {
                    var suggestTag = new Label("✦ Suggested");
                    suggestTag.style.fontSize  = 10;
                    suggestTag.style.color     = new Color(0.9f, 0.75f, 0.3f);
                    suggestTag.style.marginTop = 2;
                    row.Add(suggestTag);
                }

                // Capture typeId in closure.
                string capturedId = typeId;
                row.RegisterCallback<ClickEvent>(_ => SelectPendingType(capturedId));
                root.Add(row);
            }

            // Option to remove current assignment.
            var clearRow = new VisualElement();
            clearRow.AddToClassList(TypeRowClass);
            clearRow.style.paddingLeft   = 8;
            clearRow.style.paddingRight  = 8;
            clearRow.style.paddingTop    = 6;
            clearRow.style.paddingBottom = 6;
            clearRow.style.marginBottom  = 4;
            clearRow.style.borderTopLeftRadius     = 4;
            clearRow.style.borderTopRightRadius    = 4;
            clearRow.style.borderBottomLeftRadius  = 4;
            clearRow.style.borderBottomRightRadius = 4;
            clearRow.style.backgroundColor = _pendingTypeId == null
                ? new Color(0.18f, 0.45f, 0.62f, 0.45f)
                : new Color(0.18f, 0.18f, 0.22f);

            var clearLabel = new Label("No Type (Unassigned)");
            clearLabel.style.fontSize = 13;
            if (currentTypeId == null)
                clearLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            clearRow.Add(clearLabel);
            clearRow.RegisterCallback<ClickEvent>(_ => SelectPendingType(null));
            root.Add(clearRow);

            // ── Confirm button ─────────────────────────────────────────────────
            var confirmBtn = new Button(OnConfirmTypeAssignment) { text = "CONFIRM" };
            confirmBtn.style.marginTop     = 10;
            confirmBtn.style.paddingTop    = 6;
            confirmBtn.style.paddingBottom = 6;
            confirmBtn.style.backgroundColor = new Color(0.2f, 0.55f, 0.3f);
            confirmBtn.style.color           = new Color(0.85f, 0.95f, 0.85f);
            confirmBtn.style.borderTopWidth   = 0;
            confirmBtn.style.borderRightWidth  = 0;
            confirmBtn.style.borderBottomWidth = 0;
            confirmBtn.style.borderLeftWidth   = 0;
            confirmBtn.style.borderTopLeftRadius     = 4;
            confirmBtn.style.borderTopRightRadius    = 4;
            confirmBtn.style.borderBottomLeftRadius  = 4;
            confirmBtn.style.borderBottomRightRadius = 4;
            root.Add(confirmBtn);
        }

        private void SelectPendingType(string typeId)
        {
            _pendingTypeId = typeId;
            RebuildActiveTab();  // re-render to reflect selection highlight
        }

        private void OnConfirmTypeAssignment()
        {
            if (_rooms == null || _station == null || string.IsNullOrEmpty(_roomKey)) return;
            _rooms.AssignRoomType(_station, _roomKey, _pendingTypeId);
            // Switch to Overview so the badge update is visible immediately.
            // SelectTab fires OnTabSelected → sets _activeTab → RebuildActiveTab.
            _tabs?.SelectTab("overview");
        }

        // ── Contents tab ───────────────────────────────────────────────────────

        private void BuildContentsTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            var contents = _building?.GetRoomContents(_station, _roomKey)
                           ?? new List<FoundationInstance>();

            AddSectionHeader(root, "FURNITURE & WORKBENCHES");

            if (contents.Count == 0)
            {
                root.Add(MakeEmptyLabel("No furniture or workbenches in this room."));
                return;
            }

            foreach (var f in contents)
            {
                string displayName = f.buildableId;
                if (_registry?.Buildables != null &&
                    _registry.Buildables.TryGetValue(f.buildableId, out var def))
                    displayName = def.displayName ?? f.buildableId;

                var row = new VisualElement();
                row.AddToClassList(ContentRowClass);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.paddingTop    = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft   = 4;
                row.style.paddingRight  = 4;
                row.style.marginBottom  = 2;
                row.style.borderTopLeftRadius     = 3;
                row.style.borderTopRightRadius    = 3;
                row.style.borderBottomLeftRadius  = 3;
                row.style.borderBottomRightRadius = 3;
                row.style.backgroundColor = new Color(0.18f, 0.18f, 0.22f);

                var nameLabel = new Label(displayName);
                nameLabel.style.flexGrow  = 1;
                nameLabel.style.fontSize  = 12;
                row.Add(nameLabel);

                if (f.hasRoomBonus)
                {
                    var bonusPip = new Label("★");
                    bonusPip.style.color    = new Color(0.9f, 0.75f, 0.3f);
                    bonusPip.style.fontSize = 11;
                    row.Add(bonusPip);
                }

                // Clicking a workbench row fires the event so HUD can open the
                // Workbench contextual panel.
                string capturedUid = f.uid;
                row.RegisterCallback<ClickEvent>(_ =>
                    OnWorkbenchRowClicked?.Invoke(capturedUid));

                root.Add(row);
            }
        }

        // ── Networks tab ───────────────────────────────────────────────────────

        private void BuildNetworksTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            // ── Network connectivity ───────────────────────────────────────────
            AddSectionHeader(root, "NETWORK CONNECTIONS");

            var connectivity = _networks?.GetRoomConnectivity(_station, _roomKey)
                               ?? BuildNotConnectedList();

            var netLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["electric"] = "Electrical",
                ["pipe"]     = "Plumbing",
                ["duct"]     = "Ducting",
                ["fuel"]     = "Fuel",
            };

            foreach (var info in connectivity)
            {
                netLabels.TryGetValue(info.NetworkType, out string label);
                label ??= info.NetworkType;

                var row = new VisualElement();
                row.AddToClassList(NetworkRowClass);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.paddingTop    = 3;
                row.style.paddingBottom = 3;
                row.style.marginBottom  = 2;

                var typeLabel = new Label(label);
                typeLabel.style.flexGrow  = 1;
                typeLabel.style.fontSize  = 12;
                row.Add(typeLabel);

                string statusText;
                Color  statusColor;
                string statusClass;

                switch (info.Status)
                {
                    case RoomNetworkStatus.Connected:
                        statusText  = "Connected";
                        statusColor = new Color(0.22f, 0.76f, 0.35f);
                        statusClass = StatusConnectedClass;
                        break;
                    case RoomNetworkStatus.Severed:
                        statusText  = "Severed";
                        statusColor = new Color(0.88f, 0.38f, 0.18f);
                        statusClass = StatusSeveredClass;
                        break;
                    default:
                        statusText  = "Not Connected";
                        statusColor = new Color(0.5f, 0.5f, 0.55f);
                        statusClass = StatusNoneClass;
                        break;
                }

                var statusLabel = new Label(statusText);
                statusLabel.AddToClassList(statusClass);
                statusLabel.style.fontSize = 11;
                statusLabel.style.color    = statusColor;
                row.Add(statusLabel);

                root.Add(row);
            }

            // ── Temperature ────────────────────────────────────────────────────
            AddSectionHeader(root, "TEMPERATURE");

            float currentTemp = TemperatureSystem.GetRoomTemperature(_station, _roomKey);

            var tempRow = new VisualElement();
            tempRow.style.flexDirection = FlexDirection.Row;
            tempRow.style.alignItems    = Align.Center;
            tempRow.style.marginBottom  = 4;

            var tempLabel = new Label("Current:");
            tempLabel.style.fontSize  = 12;
            tempLabel.style.flexGrow  = 1;
            tempRow.Add(tempLabel);

            var tempValue = new Label($"{currentTemp:F1} °C");
            tempValue.style.fontSize = 12;
            tempRow.Add(tempValue);
            root.Add(tempRow);

            // Find the temperature source (heater/cooler) in this room.
            string tempSourceName   = null;
            float? targetTemp       = null;
            FindTemperatureSource(out tempSourceName, out targetTemp);

            if (targetTemp.HasValue)
            {
                var targetRow = new VisualElement();
                targetRow.style.flexDirection = FlexDirection.Row;
                targetRow.style.alignItems    = Align.Center;
                targetRow.style.marginBottom  = 2;

                var targetLabel = new Label("Target:");
                targetLabel.style.fontSize = 12;
                targetLabel.style.flexGrow = 1;
                targetRow.Add(targetLabel);

                var targetValue = new Label($"{targetTemp.Value:F1} °C");
                targetValue.style.fontSize = 12;
                targetRow.Add(targetValue);
                root.Add(targetRow);
            }

            if (!string.IsNullOrEmpty(tempSourceName))
            {
                var sourceRow = new VisualElement();
                sourceRow.style.flexDirection = FlexDirection.Row;
                sourceRow.style.alignItems    = Align.Center;

                var srcLabel = new Label("Source:");
                srcLabel.style.fontSize = 12;
                srcLabel.style.flexGrow = 1;
                sourceRow.Add(srcLabel);

                var srcValue = new Label(tempSourceName);
                srcValue.style.fontSize = 11;
                srcValue.style.color    = new Color(0.6f, 0.6f, 0.7f);
                sourceRow.Add(srcValue);
                root.Add(sourceRow);
            }
            else
            {
                var noSourceLabel = new Label("No heater/cooler in room.");
                noSourceLabel.style.fontSize = 11;
                noSourceLabel.style.color    = new Color(0.5f, 0.5f, 0.55f);
                root.Add(noSourceLabel);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private string ResolveDisplayName()
        {
            if (string.IsNullOrEmpty(_roomKey) || _station == null) return "—";
            _station.customRoomNames.TryGetValue(_roomKey, out string custom);
            return !string.IsNullOrEmpty(custom) ? custom : $"Room {_roomKey}";
        }

        private string ResolveTypeName(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            if (_registry?.RoomTypes != null &&
                _registry.RoomTypes.TryGetValue(typeId, out var rt))
                return rt.displayName;
            var custom = _station?.customRoomTypes.FirstOrDefault(t => t.id == typeId);
            return custom?.displayName ?? typeId;
        }

        private RoomTypeDefinition ResolveTypeDef(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            if (_registry?.RoomTypes != null &&
                _registry.RoomTypes.TryGetValue(typeId, out var rt))
                return rt;
            return _station?.customRoomTypes.FirstOrDefault(t => t.id == typeId);
        }

        private static string BuildBonusDescription(RoomTypeDefinition typeDef)
        {
            if (typeDef?.skillBonuses == null || typeDef.skillBonuses.Count == 0)
                return null;

            var parts = new List<string>();
            foreach (var kv in typeDef.skillBonuses)
                parts.Add($"+{(kv.Value - 1f) * 100f:F0}% {kv.Key}");
            return string.Join(", ", parts);
        }

        private List<NPCInstance> GetNpcsInRoom()
        {
            if (_station == null || string.IsNullOrEmpty(_roomKey))
                return new List<NPCInstance>();

            // Collect tile keys belonging to this room.
            var roomTileKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in _station.tileToRoomKey)
                if (kv.Value == _roomKey)
                    roomTileKeys.Add(kv.Key);

            var result = new List<NPCInstance>();
            foreach (var npc in _station.npcs.Values)
            {
                if (string.IsNullOrEmpty(npc.location)) continue;
                if (roomTileKeys.Contains(npc.location))
                    result.Add(npc);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private void FindTemperatureSource(out string sourceName, out float? targetTemp)
        {
            sourceName = null;
            targetTemp = null;

            if (_station == null || string.IsNullOrEmpty(_roomKey)) return;

            var roomTileKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in _station.tileToRoomKey)
                if (kv.Value == _roomKey)
                    roomTileKeys.Add(kv.Key);

            foreach (var f in _station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.buildableId != "buildable.heater" && f.buildableId != "buildable.cooler") continue;

                string tileKey = $"{f.tileCol}_{f.tileRow}";
                if (!roomTileKeys.Contains(tileKey)) continue;

                targetTemp = f.targetTemperature;

                // Resolve display name from registry.
                if (_registry?.Buildables != null &&
                    _registry.Buildables.TryGetValue(f.buildableId, out var def))
                    sourceName = def.displayName ?? f.buildableId;
                else
                    sourceName = f.buildableId;

                return;  // use the first heater/cooler found
            }
        }

        private static List<RoomNetworkInfo> BuildNotConnectedList()
        {
            var types = new[] { "electric", "pipe", "duct", "fuel" };
            var list  = new List<RoomNetworkInfo>(types.Length);
            foreach (var t in types)
                list.Add(new RoomNetworkInfo { NetworkType = t, Status = RoomNetworkStatus.NotConnected });
            return list;
        }

        private void OnNameFieldChanged(ChangeEvent<string> evt)
        {
            if (_station == null || string.IsNullOrEmpty(_roomKey)) return;
            string newName = (evt.newValue ?? "").Trim();
            if (string.IsNullOrEmpty(newName))
                _station.customRoomNames.Remove(_roomKey);
            else
                _station.customRoomNames[_roomKey] = newName;
        }

        private void AddSectionHeader(VisualElement parent, string title)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.fontSize     = 10;
            header.style.color        = new Color(0.5f, 0.5f, 0.65f);
            header.style.marginTop    = 8;
            header.style.marginBottom = 4;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(header);
        }

        private Label MakeEmptyLabel(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList(EmptyClass);
            lbl.style.opacity   = 0.5f;
            lbl.style.fontSize  = 12;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            return lbl;
        }
    }
}
