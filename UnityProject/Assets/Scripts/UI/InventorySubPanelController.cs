// InventorySubPanelController.cs
// Station → Inventory sub-tab panel (UI-010).
//
// Displays:
//   * Filter chip strip — "ALL" chip plus one chip per distinct item category
//                         (itemType).  Clicking a chip filters the item list.
//   * Sort strip        — buttons to sort by QUANTITY, WEIGHT, or CATEGORY.
//   * Item list         — scrollable list; each row shows:
//                           • item display name
//                           • category badge (itemType)
//                           • total quantity
//                           • total weight
//                         Clicking a row expands it inline to show which containers
//                         hold the item and in which rooms.
//
// Data is pushed via Refresh(StationState, InventorySystem, ContentRegistry).
// Call on mount and again on every OnTick while the panel is active.
// The panel also subscribes to InventorySystem.OnContentsChanged for live updates
// whenever items are added or removed from containers.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController, which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Station → Inventory sub-tab panel.  Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class InventorySubPanelController : VisualElement
    {
        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass        = "ws-inventory-panel";
        private const string FilterStripClass  = "ws-inventory-panel__filter-strip";
        private const string FilterBtnClass    = "ws-inventory-panel__filter-btn";
        private const string FilterBtnActive   = "ws-inventory-panel__filter-btn--active";
        private const string SortStripClass    = "ws-inventory-panel__sort-strip";
        private const string SortBtnClass      = "ws-inventory-panel__sort-btn";
        private const string SortBtnActive     = "ws-inventory-panel__sort-btn--active";
        private const string ListClass         = "ws-inventory-panel__item-list";
        private const string RowClass          = "ws-inventory-panel__item-row";
        private const string RowHeaderClass    = "ws-inventory-panel__item-row-header";
        private const string RowNameClass      = "ws-inventory-panel__item-name";
        private const string RowBadgeClass     = "ws-inventory-panel__item-badge";
        private const string RowQtyClass       = "ws-inventory-panel__item-qty";
        private const string RowWeightClass    = "ws-inventory-panel__item-weight";
        private const string DetailClass       = "ws-inventory-panel__item-detail";
        private const string DetailRowClass    = "ws-inventory-panel__detail-row";
        private const string EmptyClass        = "ws-inventory-panel__empty";

        private const string AllFilterKey = "__all__";

        // ── Sort mode ──────────────────────────────────────────────────────────

        public enum SortMode { Quantity, Weight, Category }

        // ── Child elements ─────────────────────────────────────────────────────

        private readonly VisualElement _filterStrip;
        private readonly VisualElement _sortStrip;
        private readonly Label         _summaryLabel;
        private readonly VisualElement _columnHeader;
        private readonly VisualElement _itemList;
        private readonly Label         _emptyLabel;

        // ── State ──────────────────────────────────────────────────────────────

        private string   _activeFilter;
        private SortMode _sortMode = SortMode.Quantity;

        private List<CargoItemRow> _rows = new List<CargoItemRow>();

        // Tracks which item rows are currently expanded.
        private readonly HashSet<string> _expandedItemIds = new HashSet<string>(StringComparer.Ordinal);

        private readonly Dictionary<string, Button> _filterButtons =
            new Dictionary<string, Button>(StringComparer.Ordinal);

        private readonly Dictionary<SortMode, Button> _sortButtons =
            new Dictionary<SortMode, Button>();

        // Held references used to re-subscribe on Refresh when the system changes.
        private InventorySystem _inventorySystem;
        private StationState    _station;

        // ── Constructor ────────────────────────────────────────────────────────

        public InventorySubPanelController()
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
            var filterLabel = new Label("CATEGORY FILTER");
            filterLabel.style.fontSize = 9;
            filterLabel.style.color = new Color(0.39f, 0.75f, 1.00f, 1f);
            filterLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            filterLabel.style.marginBottom = 3;
            Add(filterLabel);

            _filterStrip = new VisualElement();
            _filterStrip.AddToClassList(FilterStripClass);
            _filterStrip.style.flexDirection = FlexDirection.Row;
            _filterStrip.style.flexWrap      = Wrap.Wrap;
            _filterStrip.style.marginBottom  = 4;
            Add(_filterStrip);

            // ── Sort strip ─────────────────────────────────────────────────────
            var sortLabel = new Label("SORT");
            sortLabel.style.fontSize = 9;
            sortLabel.style.color = new Color(0.39f, 0.75f, 1.00f, 1f);
            sortLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sortLabel.style.marginBottom = 3;
            Add(sortLabel);

            _sortStrip = new VisualElement();
            _sortStrip.AddToClassList(SortStripClass);
            _sortStrip.style.flexDirection = FlexDirection.Row;
            _sortStrip.style.marginBottom  = 6;
            Add(_sortStrip);

            AddSortButton(SortMode.Quantity, "QTY");
            AddSortButton(SortMode.Weight,   "WEIGHT");
            AddSortButton(SortMode.Category, "CATEGORY");
            UpdateSortButtonStyles();

            _summaryLabel = new Label("0 item types");
            _summaryLabel.style.fontSize    = 9;
            _summaryLabel.style.color       = new Color(0.34f, 0.47f, 0.63f, 1f);
            _summaryLabel.style.marginBottom = 4;
            Add(_summaryLabel);

            _columnHeader = BuildColumnHeader();
            Add(_columnHeader);

            // ── Item list (scrollable) ─────────────────────────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            _itemList = scroll.contentContainer;
            _itemList.AddToClassList(ListClass);
            _itemList.style.flexDirection = FlexDirection.Column;

            _emptyLabel = new Label("No items in cargo hold containers.");
            _emptyLabel.AddToClassList(EmptyClass);
            _emptyLabel.style.opacity   = 0.5f;
            _emptyLabel.style.marginTop = 8;
            _itemList.Add(_emptyLabel);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the filter chips, sort buttons, and item rows from the latest
        /// station state.  Call once on mount and again on every OnTick while the
        /// panel is active, or in response to <see cref="InventorySystem.OnContentsChanged"/>.
        /// </summary>
        public void Refresh(StationState station, InventorySystem inventory)
        {
            // Wire up (or re-wire) the OnContentsChanged event.
            if (inventory != _inventorySystem)
            {
                if (_inventorySystem != null)
                    _inventorySystem.OnContentsChanged -= OnInventoryContentsChanged;
                _inventorySystem = inventory;
                if (_inventorySystem != null)
                    _inventorySystem.OnContentsChanged += OnInventoryContentsChanged;
            }

            _station = station;

            if (station == null || inventory == null)
            {
                _rows = new List<CargoItemRow>();
                RebuildFilterStrip();
                RebuildItemList();
                return;
            }

            _rows = inventory.GetCargoHoldContents(station);
            SortRows();
            RebuildFilterStrip();
            RebuildItemList();
        }

        /// <summary>
        /// Unsubscribes all event handlers.  Call before the panel is destroyed.
        /// </summary>
        public void Detach()
        {
            if (_inventorySystem != null)
            {
                _inventorySystem.OnContentsChanged -= OnInventoryContentsChanged;
                _inventorySystem = null;
            }
        }

        // ── Event: inventory contents changed ─────────────────────────────────

        private void OnInventoryContentsChanged()
        {
            if (_station == null || _inventorySystem == null) return;
            _rows = _inventorySystem.GetCargoHoldContents(_station);
            SortRows();
            RebuildFilterStrip();
            RebuildItemList();
        }

        // ── Filter strip ───────────────────────────────────────────────────────

        private void RebuildFilterStrip()
        {
            _filterStrip.Clear();
            _filterButtons.Clear();

            AddFilterChip(null, "ALL");

            var seenCategories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in _rows)
            {
                if (string.IsNullOrEmpty(row.itemType)) continue;
                if (!seenCategories.Add(row.itemType)) continue;
                AddFilterChip(row.itemType, row.itemType.ToUpper());
            }

            // Ensure the active filter is still valid.
            if (_activeFilter != null && !seenCategories.Contains(_activeFilter))
                _activeFilter = null;

            UpdateFilterButtonStyles();
        }

        private void AddFilterChip(string category, string label)
        {
            var btn = new Button();
            btn.AddToClassList(FilterBtnClass);
            btn.text               = label;
            btn.style.marginRight  = 3;
            btn.style.marginBottom = 3;

            string capturedCategory = category;
            btn.RegisterCallback<ClickEvent>(_ => OnFilterChipClicked(capturedCategory));
            _filterStrip.Add(btn);

            string key = category ?? AllFilterKey;
            _filterButtons[key] = btn;
        }

        private void OnFilterChipClicked(string category)
        {
            _activeFilter = category;
            UpdateFilterButtonStyles();
            ApplyFilter();
        }

        private void UpdateFilterButtonStyles()
        {
            foreach (var kv in _filterButtons)
            {
                bool isActive = (kv.Key == AllFilterKey) ? (_activeFilter == null)
                                                         : (kv.Key == _activeFilter);
                kv.Value.EnableInClassList(FilterBtnActive, isActive);
            }
        }

        // ── Sort strip ─────────────────────────────────────────────────────────

        private void AddSortButton(SortMode mode, string label)
        {
            var btn = new Button();
            btn.AddToClassList(SortBtnClass);
            btn.text               = label;
            btn.style.marginRight  = 3;

            SortMode capturedMode = mode;
            btn.RegisterCallback<ClickEvent>(_ => OnSortButtonClicked(capturedMode));
            _sortStrip.Add(btn);
            _sortButtons[mode] = btn;
        }

        private void OnSortButtonClicked(SortMode mode)
        {
            _sortMode = mode;
            UpdateSortButtonStyles();
            SortRows();
            RebuildItemList();
        }

        private void UpdateSortButtonStyles()
        {
            foreach (var kv in _sortButtons)
                kv.Value.EnableInClassList(SortBtnActive, kv.Key == _sortMode);
        }

        private void SortRows()
        {
            switch (_sortMode)
            {
                case SortMode.Weight:
                    _rows.Sort((a, b) =>
                    {
                        int c = b.totalWeight.CompareTo(a.totalWeight);
                        return c != 0 ? c : StringComparer.Ordinal.Compare(a.itemId, b.itemId);
                    });
                    break;
                case SortMode.Category:
                    _rows.Sort((a, b) =>
                    {
                        int c = StringComparer.Ordinal.Compare(a.itemType, b.itemType);
                        return c != 0 ? c : StringComparer.Ordinal.Compare(a.displayName, b.displayName);
                    });
                    break;
                default: // Quantity (descending)
                    _rows.Sort((a, b) =>
                    {
                        int c = b.totalQuantity.CompareTo(a.totalQuantity);
                        return c != 0 ? c : StringComparer.Ordinal.Compare(a.itemId, b.itemId);
                    });
                    break;
            }
        }

        // ── Item list ──────────────────────────────────────────────────────────

        private void RebuildItemList()
        {
            _itemList.Clear();

            if (_rows.Count == 0)
            {
                _itemList.Add(_emptyLabel);
                return;
            }

            foreach (var row in _rows)
                _itemList.Add(BuildItemRow(row));

            ApplyFilter();
        }

        private VisualElement BuildItemRow(CargoItemRow data)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList(RowClass);
            wrapper.userData = data.itemType;  // used by ApplyFilter
            wrapper.style.flexDirection  = FlexDirection.Column;
            wrapper.style.marginBottom   = 2;
            wrapper.style.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.65f);
            wrapper.style.borderBottomWidth = 1;
            wrapper.style.borderBottomColor = new Color(0.3f, 0.3f, 0.35f, 0.5f);

            // ── Row header ─────────────────────────────────────────────────────
            var header = new VisualElement();
            header.AddToClassList(RowHeaderClass);
            header.style.flexDirection  = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems     = Align.Center;
            header.style.paddingTop     = 5;
            header.style.paddingBottom  = 5;
            header.style.paddingLeft    = 6;
            header.style.paddingRight   = 6;
            wrapper.Add(header);

            // Name
            var nameLabel = new Label(data.displayName);
            nameLabel.AddToClassList(RowNameClass);
            nameLabel.style.flexGrow       = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(nameLabel);

            // Category badge
            var badge = new Label((data.itemType ?? "Unknown").ToUpper());
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
            badge.style.backgroundColor = new Color(0.2f, 0.45f, 0.6f, 0.6f);
            badge.style.color           = new Color(0.8f, 0.92f, 1f, 1f);
            header.Add(badge);

            // Quantity
            var qtyLabel = new Label($"×{data.totalQuantity}");
            qtyLabel.AddToClassList(RowQtyClass);
            qtyLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            qtyLabel.style.minWidth       = 40;
            qtyLabel.style.marginLeft     = 4;
            header.Add(qtyLabel);

            // Weight
            var weightLabel = new Label($"{data.totalWeight:F1}kg");
            weightLabel.AddToClassList(RowWeightClass);
            weightLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            weightLabel.style.opacity        = 0.6f;
            weightLabel.style.minWidth       = 48;
            weightLabel.style.marginLeft     = 4;
            header.Add(weightLabel);

            // ── Detail section (hidden by default) ────────────────────────────
            var detail = new VisualElement();
            detail.AddToClassList(DetailClass);
            detail.style.paddingLeft   = 16;
            detail.style.paddingRight  = 6;
            detail.style.paddingBottom = 6;
            detail.style.display = _expandedItemIds.Contains(data.itemId)
                ? DisplayStyle.Flex : DisplayStyle.None;
            wrapper.Add(detail);

            foreach (var entry in data.containers)
            {
                var detailRow = new VisualElement();
                detailRow.AddToClassList(DetailRowClass);
                detailRow.style.flexDirection  = FlexDirection.Row;
                detailRow.style.justifyContent = Justify.SpaceBetween;
                detailRow.style.paddingTop     = 2;
                detailRow.style.paddingBottom  = 2;

                var roomLabel = new Label(entry.roomName);
                roomLabel.style.flexGrow       = 1;
                roomLabel.style.opacity        = 0.75f;
                roomLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                detailRow.Add(roomLabel);

                var containerQtyLabel = new Label($"×{entry.quantity}");
                containerQtyLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                containerQtyLabel.style.opacity        = 0.75f;
                containerQtyLabel.style.minWidth       = 40;
                detailRow.Add(containerQtyLabel);

                detail.Add(detailRow);
            }

            // ── Click: toggle expanded detail ──────────────────────────────────
            string capturedItemId = data.itemId;
            header.RegisterCallback<ClickEvent>(_ =>
            {
                if (_expandedItemIds.Contains(capturedItemId))
                    _expandedItemIds.Remove(capturedItemId);
                else
                    _expandedItemIds.Add(capturedItemId);

                detail.style.display = _expandedItemIds.Contains(capturedItemId)
                    ? DisplayStyle.Flex : DisplayStyle.None;
            });

            header.RegisterCallback<PointerEnterEvent>(_ =>
                wrapper.style.backgroundColor = new Color(0.09f, 0.12f, 0.18f, 0.9f));
            header.RegisterCallback<PointerLeaveEvent>(_ =>
                wrapper.style.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.65f));

            return wrapper;
        }

        private VisualElement BuildColumnHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection  = FlexDirection.Row;
            header.style.alignItems     = Align.Center;
            header.style.paddingLeft    = 6;
            header.style.paddingRight   = 6;
            header.style.paddingBottom  = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.09f, 0.12f, 0.17f, 1f);
            header.style.marginBottom   = 2;

            var name = new Label("ITEM");
            name.style.flexGrow = 1;
            name.style.fontSize = 9;
            name.style.color    = new Color(0.39f, 0.75f, 1.00f, 1f);
            header.Add(name);

            var type = new Label("TYPE");
            type.style.fontSize   = 9;
            type.style.color      = new Color(0.39f, 0.75f, 1.00f, 1f);
            type.style.minWidth   = 64;
            type.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(type);

            var qty = new Label("QTY");
            qty.style.fontSize    = 9;
            qty.style.color       = new Color(0.39f, 0.75f, 1.00f, 1f);
            qty.style.minWidth    = 40;
            qty.style.unityTextAlign = TextAnchor.MiddleRight;
            header.Add(qty);

            var weight = new Label("WEIGHT");
            weight.style.fontSize    = 9;
            weight.style.color       = new Color(0.39f, 0.75f, 1.00f, 1f);
            weight.style.minWidth    = 56;
            weight.style.unityTextAlign = TextAnchor.MiddleRight;
            header.Add(weight);

            return header;
        }

        private void ApplyFilter()
        {
            int visibleCount = 0;
            int totalQty = 0;
            foreach (var child in _itemList.Children())
            {
                if (!child.ClassListContains(RowClass)) continue;

                bool visible = _activeFilter == null ||
                               (child.userData is string cat && cat == _activeFilter);
                child.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (!visible) continue;
                visibleCount++;
                if (child is VisualElement row && row.childCount > 0)
                {
                    // quantity label is on header row index 2 when layout is unchanged
                    var header = row[0];
                    if (header != null && header.childCount > 2 && header[2] is Label qtyLabel)
                    {
                        string txt = qtyLabel.text?.Replace("×", "");
                        if (int.TryParse(txt, out int q)) totalQty += q;
                    }
                }
            }

            _summaryLabel.text = _activeFilter == null
                ? $"{visibleCount} item types  |  {totalQty} total units"
                : $"{visibleCount} filtered types  |  {totalQty} total units";
        }
    }
}
