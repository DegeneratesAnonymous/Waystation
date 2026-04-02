// BuildSubPanelController.cs
// Station → Build sub-tab panel (UI-007).
//
// Displays:
//   * Category strip  — one button per build category; clicking selects the category
//                       and populates the item list below.
//   * Item list       — scrollable list of buildable items in the selected category,
//                       showing name and required-materials cost.  Clicking an item
//                       fires OnBuildItemSelected so the HUD controller can begin
//                       ghost placement.
//   * Queue section   — live list of foundations currently in the construction
//                       pipeline, showing the buildable name, the assigned NPC,
//                       a progress bar, and a material-availability indicator.
//
// Data is pushed via Refresh(StationState, BuildingSystem, InventorySystem,
// ContentRegistry). Call this once on construction and again on every OnTick.
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
    /// Station → Build sub-tab panel. Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class BuildSubPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks a build item.
        /// Arguments: categoryId, buildableId.
        /// Subscribe from WaystationHUDController to begin ghost placement.
        /// </summary>
        public event Action<string, string> OnBuildItemSelected;

        // ── Category data ──────────────────────────────────────────────────────

        private static readonly (string id, string label)[] CategoryDefs =
        {
            ("structure",  "STRUCTURE"),
            ("electrical", "ELECTRICAL"),
            ("objects",    "OBJECTS"),
            ("production", "PRODUCTION"),
            ("plumbing",   "PLUMBING"),
            ("security",   "SECURITY"),
        };

        // Maps category id → list of (buildableId, display name, cost label)
        private static readonly Dictionary<string, List<(string buildableId, string name, string cost)>> CategoryItems =
            new Dictionary<string, List<(string, string, string)>>
            {
                ["structure"]  = new List<(string, string, string)>
                {
                    ("buildable.wall",   "Wall",   "40 Fe"),
                    ("buildable.floor",  "Floor",  "20 Fe"),
                    ("buildable.door",   "Door",   "80 Fe"),
                    ("",                 "Window", "60 Fe"),
                    ("",                 "Column", "30 Fe"),
                },
                ["electrical"] = new List<(string, string, string)>
                {
                    ("buildable.wire",           "Wire",      "10 Fe"),
                    ("buildable.generator",      "Generator", "200 Fe"),
                    ("buildable.battery",        "Battery",   "120 Fe"),
                    ("buildable.switch",         "Switch",    "40 Fe"),
                    ("buildable.overhead_light", "Light",     "30 Fe"),
                },
                ["objects"]    = new List<(string, string, string)>
                {
                    ("buildable.bed",              "Bed",     "80 Fe"),
                    ("buildable.storage_cabinet",  "Locker",  "60 Fe"),
                    ("buildable.access_terminal",  "Console", "150 Fe"),
                    ("buildable.table",            "Table",   "40 Fe"),
                    ("buildable.chair",            "Chair",   "20 Fe"),
                },
                ["production"] = new List<(string, string, string)>
                {
                    ("buildable.refinery_bench",     "Refinery",   "400 Fe"),
                    ("buildable.workbench",          "Fabricator", "300 Fe"),
                    ("",                             "Assembler",  "250 Fe"),
                    ("buildable.industrial_furnace", "Smelter",    "350 Fe"),
                },
                ["plumbing"]   = new List<(string, string, string)>
                {
                    ("buildable.pipe",       "Pipe",   "15 Fe"),
                    ("",                     "Pump",   "80 Fe"),
                    ("buildable.water_tank", "Tank",   "100 Fe"),
                    ("buildable.valve",      "Valve",  "40 Fe"),
                    ("",                     "Filter", "60 Fe"),
                },
                ["security"]   = new List<(string, string, string)>
                {
                    ("", "Turret",        "300 Fe"),
                    ("", "Camera",        "80 Fe"),
                    ("", "Door Lock",     "60 Fe"),
                    ("", "Motion Sensor", "50 Fe"),
                },
            };

        // ── Child elements ─────────────────────────────────────────────────────

        private readonly VisualElement _catStrip;
        private readonly VisualElement _itemList;
        private readonly VisualElement _queueSection;
        private readonly Label         _queueEmptyLabel;
        private readonly VisualElement _hoverDetailCard;
        private readonly Label         _hoverTitleLabel;
        private readonly Label         _hoverBodyLabel;

        // ── Per-category button references ─────────────────────────────────────
        private readonly Dictionary<string, Button> _catButtons =
            new Dictionary<string, Button>(StringComparer.Ordinal);

        // ── Per-queue-entry row references (keyed by foundation uid) ───────────
        private readonly Dictionary<string, QueueRow> _queueRows =
            new Dictionary<string, QueueRow>(StringComparer.Ordinal);

        // ── State ──────────────────────────────────────────────────────────────
        private string _activeCategory;
        private ContentRegistry _registry;

        // ── Constructor ────────────────────────────────────────────────────────

        public BuildSubPanelController()
        {
            AddToClassList("ws-build-panel");

            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // ── Category strip ─────────────────────────────────────────────────
            _catStrip = new VisualElement();
            _catStrip.AddToClassList("ws-build-panel__cat-strip");
            _catStrip.style.flexDirection  = FlexDirection.Row;
            _catStrip.style.flexWrap       = Wrap.Wrap;
            _catStrip.style.marginBottom   = 6;
            Add(_catStrip);

            foreach (var (id, label) in CategoryDefs)
            {
                var btn = new Button();
                btn.AddToClassList("ws-build-panel__cat-btn");
                btn.text = label;
                btn.style.flexGrow    = 1;
                btn.style.flexBasis   = 0;
                btn.style.minWidth    = 94;
                btn.style.minHeight   = 30;
                btn.style.marginRight = 2;
                btn.style.marginBottom = 2;
                btn.style.paddingTop = 5;
                btn.style.paddingBottom = 5;
                btn.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.95f);
                btn.style.borderTopWidth = 1;
                btn.style.borderRightWidth = 1;
                btn.style.borderBottomWidth = 1;
                btn.style.borderLeftWidth = 1;
                btn.style.borderTopColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                btn.style.borderRightColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                btn.style.borderBottomColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                btn.style.borderLeftColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                string catId = id;  // capture for lambda
                btn.RegisterCallback<ClickEvent>(_ => OnCategoryClicked(catId));
                btn.RegisterCallback<PointerEnterEvent>(_ =>
                    btn.style.backgroundColor = new Color(0.14f, 0.20f, 0.30f, 0.96f));
                btn.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    bool isActive = _activeCategory == catId;
                    btn.style.backgroundColor = isActive
                        ? new Color(0.12f, 0.24f, 0.38f, 0.98f)
                        : new Color(0.10f, 0.14f, 0.20f, 0.95f);
                });
                _catStrip.Add(btn);
                _catButtons[id] = btn;
            }

            // ── Item list ──────────────────────────────────────────────────────
            var itemScroll = new ScrollView(ScrollViewMode.Vertical);
            itemScroll.style.flexShrink  = 0;
            itemScroll.style.maxHeight   = 180;
            itemScroll.style.marginBottom = 8;
            Add(itemScroll);

            _itemList = itemScroll.contentContainer;
            _itemList.AddToClassList("ws-build-panel__item-list");
            _itemList.style.flexDirection = FlexDirection.Column;

            // Hover detail card (appears after a 2s hover over a buildable row).
            _hoverDetailCard = new VisualElement();
            _hoverDetailCard.style.position = Position.Absolute;
            _hoverDetailCard.style.right = 8;
            _hoverDetailCard.style.top = 56;
            _hoverDetailCard.style.width = 260;
            _hoverDetailCard.style.paddingLeft = 8;
            _hoverDetailCard.style.paddingRight = 8;
            _hoverDetailCard.style.paddingTop = 8;
            _hoverDetailCard.style.paddingBottom = 8;
            _hoverDetailCard.style.backgroundColor = new Color(0.05f, 0.08f, 0.13f, 0.96f);
            _hoverDetailCard.style.borderTopWidth = 1;
            _hoverDetailCard.style.borderRightWidth = 1;
            _hoverDetailCard.style.borderBottomWidth = 1;
            _hoverDetailCard.style.borderLeftWidth = 1;
            _hoverDetailCard.style.borderTopColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _hoverDetailCard.style.borderRightColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _hoverDetailCard.style.borderBottomColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _hoverDetailCard.style.borderLeftColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _hoverDetailCard.style.display = DisplayStyle.None;

            _hoverTitleLabel = new Label("BUILDABLE");
            _hoverTitleLabel.style.fontSize = 10;
            _hoverTitleLabel.style.color = new Color(0.39f, 0.75f, 1.00f, 1f);
            _hoverTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hoverTitleLabel.style.marginBottom = 4;
            _hoverDetailCard.Add(_hoverTitleLabel);

            _hoverBodyLabel = new Label("");
            _hoverBodyLabel.style.fontSize = 10;
            _hoverBodyLabel.style.color = new Color(0.78f, 0.84f, 0.94f, 1f);
            _hoverBodyLabel.style.whiteSpace = WhiteSpace.Normal;
            _hoverDetailCard.Add(_hoverBodyLabel);
            Add(_hoverDetailCard);

            // ── Queue section ──────────────────────────────────────────────────
            var queueHeader = BuildSectionHeader("CONSTRUCTION QUEUE");
            Add(queueHeader);

            var queueScroll = new ScrollView(ScrollViewMode.Vertical);
            queueScroll.style.flexGrow = 1;
            Add(queueScroll);

            _queueSection = queueScroll.contentContainer;
            _queueSection.AddToClassList("ws-build-panel__queue");
            _queueSection.style.flexDirection = FlexDirection.Column;

            _queueEmptyLabel = new Label("No active construction");
            _queueEmptyLabel.AddToClassList("ws-build-panel__empty");
            _queueEmptyLabel.style.opacity = 0.5f;
            _queueEmptyLabel.style.marginTop = 4;
            _queueSection.Add(_queueEmptyLabel);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the queue section with the latest construction state.
        /// Call on every <c>GameManager.OnTick</c> while this panel is mounted.
        /// </summary>
        public void Refresh(StationState station, BuildingSystem building,
                            InventorySystem inventory, ContentRegistry registry)
        {
            _registry = registry;
            if (station == null || building == null) return;
            RefreshQueue(station, building, inventory, registry);
        }

        // ── Category click ─────────────────────────────────────────────────────

        private void OnCategoryClicked(string categoryId)
        {
            // Deselect all buttons
            foreach (var kv in _catButtons)
            {
                kv.Value.EnableInClassList("ws-build-panel__cat-btn--active", false);
                kv.Value.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.95f);
                kv.Value.style.borderTopColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                kv.Value.style.borderRightColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                kv.Value.style.borderBottomColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                kv.Value.style.borderLeftColor = new Color(0.16f, 0.22f, 0.32f, 1f);
            }

            if (_activeCategory == categoryId)
            {
                // Toggle off — collapse item list
                _activeCategory = null;
                _itemList.Clear();
                HideBuildableHoverCard();
                return;
            }

            _activeCategory = categoryId;
            _catButtons[categoryId].EnableInClassList("ws-build-panel__cat-btn--active", true);
            _catButtons[categoryId].style.backgroundColor = new Color(0.12f, 0.24f, 0.38f, 0.98f);
            _catButtons[categoryId].style.borderTopColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _catButtons[categoryId].style.borderRightColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _catButtons[categoryId].style.borderBottomColor = new Color(0.23f, 0.37f, 0.53f, 1f);
            _catButtons[categoryId].style.borderLeftColor = new Color(0.23f, 0.37f, 0.53f, 1f);

            PopulateItems(categoryId);
            HideBuildableHoverCard();
        }

        private void PopulateItems(string categoryId)
        {
            _itemList.Clear();
            HideBuildableHoverCard();
            if (!CategoryItems.TryGetValue(categoryId, out var items)) return;

            foreach (var (buildableId, name, cost) in items)
            {
                var row = new VisualElement();
                row.AddToClassList("ws-build-panel__item-row");
                row.style.flexDirection  = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.alignItems     = Align.Center;
                row.style.minHeight      = 30;
                row.style.paddingTop     = 3;
                row.style.paddingBottom  = 3;
                row.style.paddingLeft    = 4;
                row.style.paddingRight   = 4;
                row.style.marginBottom      = 2;
                row.style.backgroundColor   = new Color(0.10f, 0.14f, 0.20f, 0.92f);
                row.style.borderTopWidth    = 1;
                row.style.borderRightWidth  = 1;
                row.style.borderBottomWidth = 1;
                row.style.borderLeftWidth   = 1;
                row.style.borderTopColor    = new Color(0.16f, 0.22f, 0.32f, 1f);
                row.style.borderRightColor  = new Color(0.16f, 0.22f, 0.32f, 1f);
                row.style.borderBottomColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                row.style.borderLeftColor   = new Color(0.16f, 0.22f, 0.32f, 1f);

                var nameLabel = new Label(name.ToUpper());
                nameLabel.AddToClassList("ws-build-panel__item-name");
                nameLabel.style.flexGrow = 1;
                nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

                var costLabel = new Label(cost);
                costLabel.AddToClassList("ws-build-panel__item-cost");
                costLabel.style.opacity = 0.7f;
                costLabel.style.unityTextAlign = TextAnchor.MiddleRight;

                row.Add(nameLabel);
                row.Add(costLabel);

                // Only selectable when it has a registered buildable id
                if (!string.IsNullOrEmpty(buildableId))
                {
                    string capturedBuildableId = buildableId;
                    string capturedCategoryId  = categoryId;
                    bool hoverActive = false;
                    IVisualElementScheduledItem hoverTimer = null;

                    row.RegisterCallback<ClickEvent>(_ =>
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.Log($"[BuildSubPanel] Selected: {capturedCategoryId}/{capturedBuildableId}");
#endif
                        OnBuildItemSelected?.Invoke(capturedCategoryId, capturedBuildableId);
                    });

                    row.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        row.style.backgroundColor = new Color(0.14f, 0.20f, 0.30f, 0.96f);
                        row.style.borderTopColor = new Color(0.23f, 0.37f, 0.53f, 1f);
                        row.style.borderRightColor = new Color(0.23f, 0.37f, 0.53f, 1f);
                        row.style.borderBottomColor = new Color(0.23f, 0.37f, 0.53f, 1f);
                        row.style.borderLeftColor = new Color(0.23f, 0.37f, 0.53f, 1f);

                        hoverActive = true;
                        hoverTimer?.Pause();
                        hoverTimer = row.schedule.Execute(() =>
                        {
                            if (!hoverActive) return;
                            ShowBuildableHoverCard(capturedBuildableId, name, cost);
                        }).StartingIn(2000);
                    });
                    row.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        row.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.92f);
                        row.style.borderTopColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                        row.style.borderRightColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                        row.style.borderBottomColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                        row.style.borderLeftColor = new Color(0.16f, 0.22f, 0.32f, 1f);
                        hoverActive = false;
                        hoverTimer?.Pause();
                        HideBuildableHoverCard();
                    });
                }
                else
                {
                    row.style.opacity = 0.4f;
                }

                _itemList.Add(row);
            }
        }

        private void ShowBuildableHoverCard(string buildableId, string fallbackName, string cost)
        {
            string title = fallbackName.ToUpper();
            string body = $"Cost: {cost}";

            if (_registry != null && _registry.Buildables.TryGetValue(buildableId, out var defn))
            {
                title = (defn.displayName ?? fallbackName).ToUpper();
                string desc = string.IsNullOrWhiteSpace(defn.description)
                    ? "No description available."
                    : defn.description;

                string tags = (defn.requiredTags != null && defn.requiredTags.Count > 0)
                    ? string.Join(", ", defn.requiredTags)
                    : "None";

                body =
                    $"{desc}\n\n" +
                    $"Build Time: {defn.buildTimeTicks} ticks\n" +
                    $"Max Health: {defn.maxHealth}\n" +
                    $"Network: {(string.IsNullOrEmpty(defn.networkType) ? "None" : defn.networkType)}\n" +
                    $"Required Tags: {tags}\n" +
                    $"Cost: {cost}";
            }

            _hoverTitleLabel.text = title;
            _hoverBodyLabel.text = body;
            _hoverDetailCard.style.display = DisplayStyle.Flex;
        }

        private void HideBuildableHoverCard()
        {
            _hoverDetailCard.style.display = DisplayStyle.None;
        }

        // ── Queue refresh ──────────────────────────────────────────────────────

        private void RefreshQueue(StationState station, BuildingSystem building,
                                  InventorySystem inventory, ContentRegistry registry)
        {
            var active = building.GetQueue(station);

            // Build a HashSet of active UIDs for O(1) membership checks.
            var activeUids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in active)
                activeUids.Add(f.uid);

            // Remove rows for foundations no longer in the queue.
            var toRemove = new List<string>();
            foreach (var uid in _queueRows.Keys)
                if (!activeUids.Contains(uid))
                    toRemove.Add(uid);
            foreach (var uid in toRemove)
            {
                _queueRows[uid].Root.RemoveFromHierarchy();
                _queueRows.Remove(uid);
            }

            // Add or update rows for active foundations
            foreach (var f in active)
            {
                string displayName = f.buildableId;
                if (registry != null && registry.Buildables.TryGetValue(f.buildableId, out var defn))
                    displayName = defn.displayName ?? f.buildableId;

                string assigneeName = "—";
                if (!string.IsNullOrEmpty(f.assignedNpcUid) &&
                    station.npcs.TryGetValue(f.assignedNpcUid, out var npc))
                    assigneeName = npc.name;

                var matStatus = inventory != null
                    ? inventory.CheckMaterials(station, f.buildableId)
                    : InventorySystem.MaterialStatus.Missing;

                Dictionary<string, int> missingMats = null;
                if (matStatus != InventorySystem.MaterialStatus.Sufficient && inventory != null)
                    missingMats = inventory.GetMissingMaterials(station, f.buildableId);

                if (!_queueRows.TryGetValue(f.uid, out var row))
                {
                    row = new QueueRow();
                    _queueSection.Add(row.Root);
                    _queueRows[f.uid] = row;
                }

                row.Update(displayName, assigneeName, f.buildProgress, matStatus, missingMats);
            }

            _queueEmptyLabel.style.display = _queueRows.Count == 0
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Section header helper ──────────────────────────────────────────────

        private static Label BuildSectionHeader(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("ws-build-panel__section-header");
            lbl.AddToClassList("ws-text-acc");
            lbl.style.fontSize                = 10;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginBottom            = 4;
            lbl.style.marginTop               = 8;
            return lbl;
        }

        // ── Queue row ──────────────────────────────────────────────────────────

        /// <summary>One row in the construction queue display.</summary>
        private sealed class QueueRow
        {
            public readonly VisualElement Root;

            private readonly Label         _nameLabel;
            private readonly Label         _assigneeLabel;
            private readonly VisualElement _progressTrack;
            private readonly VisualElement _progressFill;
            private readonly StatusPip     _materialPip;
            private readonly Label         _materialDetail;

            public QueueRow()
            {
                Root = new VisualElement();
                Root.AddToClassList("ws-build-panel__queue-row");
                Root.style.flexDirection  = FlexDirection.Column;
                Root.style.marginBottom   = 6;
                Root.style.paddingTop     = 4;
                Root.style.paddingBottom  = 4;
                Root.style.paddingLeft    = 6;
                Root.style.paddingRight   = 6;
                Root.style.borderLeftWidth = 2;
                Root.style.borderLeftColor = new Color(0.24f, 0.55f, 0.86f, 1f); // acc
                Root.style.backgroundColor = new Color(0.09f, 0.12f, 0.17f, 0.8f); // bg-deep

                // ── Top row: name + material pip ──
                var topRow = new VisualElement();
                topRow.style.flexDirection  = FlexDirection.Row;
                topRow.style.justifyContent = Justify.SpaceBetween;
                topRow.style.marginBottom   = 2;
                Root.Add(topRow);

                _nameLabel = new Label();
                _nameLabel.AddToClassList("ws-build-panel__queue-name");
                _nameLabel.style.flexGrow = 1;
                _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                topRow.Add(_nameLabel);

                _materialPip = new StatusPip();
                _materialPip.style.marginTop   = 2;
                _materialPip.style.marginLeft  = 4;
                topRow.Add(_materialPip);

                // ── Assignee label ──
                _assigneeLabel = new Label();
                _assigneeLabel.AddToClassList("ws-build-panel__queue-assignee");
                _assigneeLabel.style.opacity  = 0.65f;
                _assigneeLabel.style.fontSize = 10;
                _assigneeLabel.style.marginBottom = 3;
                Root.Add(_assigneeLabel);

                // ── Progress bar ──
                _progressTrack = new VisualElement();
                _progressTrack.AddToClassList("ws-build-panel__progress-track");
                _progressTrack.style.height          = 6;
                _progressTrack.style.backgroundColor = new Color(0.05f, 0.07f, 0.10f, 1f); // bg-deep
                _progressTrack.style.overflow        = Overflow.Hidden;
                Root.Add(_progressTrack);

                _progressFill = new VisualElement();
                _progressFill.AddToClassList("ws-build-panel__progress-fill");
                _progressFill.style.height          = 6;
                _progressFill.style.backgroundColor = new Color(0.24f, 0.78f, 0.44f, 1f); // green
                _progressTrack.Add(_progressFill);

                // ── Missing materials detail (hidden when sufficient) ──
                _materialDetail = new Label();
                _materialDetail.AddToClassList("ws-build-panel__queue-missing");
                _materialDetail.style.fontSize  = 9;
                _materialDetail.style.color     = new Color(0.88f, 0.22f, 0.22f, 1f); // red
                _materialDetail.style.marginTop = 2;
                _materialDetail.style.display   = DisplayStyle.None;
                _materialDetail.style.whiteSpace = WhiteSpace.Normal;
                Root.Add(_materialDetail);
            }

            /// <summary>
            /// Updates the row's visual state to reflect current construction data.
            /// </summary>
            public void Update(string name, string assignee, float progress,
                               InventorySystem.MaterialStatus matStatus,
                               Dictionary<string, int> missingMaterials)
            {
                _nameLabel.text     = name.ToUpper();
                _assigneeLabel.text = $"Assigned: {assignee}";

                // Progress bar width
                _progressFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);

                // Material status pip
                switch (matStatus)
                {
                    case InventorySystem.MaterialStatus.Sufficient:
                        _materialPip.PipState = StatusPip.State.On;
                        break;
                    case InventorySystem.MaterialStatus.Partial:
                        _materialPip.PipState = StatusPip.State.Warning;
                        break;
                    default:
                        _materialPip.PipState = StatusPip.State.Fault;
                        break;
                }

                // Missing material detail text
                if (matStatus != InventorySystem.MaterialStatus.Sufficient &&
                    missingMaterials != null && missingMaterials.Count > 0)
                {
                    var parts = new System.Text.StringBuilder();
                    parts.Append("MISSING: ");
                    bool first = true;
                    foreach (var kv in missingMaterials)
                    {
                        if (!first) parts.Append(", ");
                        parts.Append($"{kv.Key} ×{kv.Value}");
                        first = false;
                    }
                    _materialDetail.text    = parts.ToString();
                    _materialDetail.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _materialDetail.style.display = DisplayStyle.None;
                }
            }
        }
    }
}
