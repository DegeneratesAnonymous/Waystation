// WorkbenchPanelController.cs
// Workbench contextual panel (UI-025).
//
// Displays per-workbench inspection across three tabs:
//   Status    — building name and type, HP bar with damage state label,
//               room type bonus being received (if any), assigned NPC name and
//               current action (or "Unassigned").
//   Queue     — current recipe in progress with NPC name, recipe name, time
//               remaining bar; queued recipes list with ▲/▼ reorder and remove
//               button; Add to Queue button that switches to the Recipes tab.
//   Recipes   — all recipes for this workbench type; research-locked recipes
//               shown dim with unlock requirement; each recipe shows name, output
//               item, and per-material availability indicators (green/amber/red);
//               clicking an unlocked recipe enqueues it.
//
// Opened from world workbench click or from Room panel → Contents tab via
//   WaystationHUDController.OpenWorkbenchPanel(foundationUid).
//
// Data is pushed via Refresh(foundationUid, station, registry, crafting, inventory).
// Feature-flagged under FeatureFlags.UseUIToolkitHUD.

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
    /// Workbench contextual panel.  Extends <see cref="VisualElement"/> so it can be
    /// added directly to the content area as a stacking overlay.
    /// </summary>
    public class WorkbenchPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>Fired when the close button is clicked.</summary>
        public event Action OnCloseRequested;

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass         = "ws-workbench-panel";
        private const string HeaderClass        = "ws-workbench-panel__header";
        private const string TitleClass         = "ws-workbench-panel__title";
        private const string CloseBtnClass      = "ws-workbench-panel__close-btn";
        private const string TabContentClass    = "ws-workbench-panel__tab-content";
        private const string SectionHeaderClass = "ws-workbench-panel__section-header";
        private const string EmptyClass         = "ws-workbench-panel__empty";
        private const string QueueRowClass      = "ws-workbench-panel__queue-row";
        private const string RecipeRowClass     = "ws-workbench-panel__recipe-row";
        private const string RecipeLockedClass  = "ws-workbench-panel__recipe-row--locked";
        private const string MatRowClass        = "ws-workbench-panel__mat-row";

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly TabStrip      _tabs;
        private readonly ScrollView    _tabContent;
        private string                 _activeTab = "status";

        // Cached data for tab rebuilds.
        private string          _foundationUid;
        private StationState    _station;
        private ContentRegistry _registry;
        private CraftingSystem  _crafting;
        private InventorySystem _inventory;

        // ── Constructor ────────────────────────────────────────────────────────

        public WorkbenchPanelController()
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

            var titleLabel = new Label("Workbench");
            titleLabel.name = "workbench-title";
            titleLabel.AddToClassList(TitleClass);
            titleLabel.style.flexGrow               = 1;
            titleLabel.style.fontSize               = 15;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);

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
            _tabs.AddTab("STATUS",  "status");
            _tabs.AddTab("QUEUE",   "queue");
            _tabs.AddTab("RECIPES", "recipes");

            Add(_tabs);
            Add(_tabContent);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Gets the currently active tab id.</summary>
        public string ActiveTab => _activeTab;

        /// <summary>
        /// Programmatically selects a tab by id.
        /// Valid ids: "status" | "queue" | "recipes".
        /// </summary>
        public void SelectTab(string tabId) => _tabs?.SelectTab(tabId);

        /// <summary>
        /// Refreshes all tab content with current workbench data.
        /// Call once on mount and again every tick while the panel is visible.
        /// </summary>
        public void Refresh(
            string          foundationUid,
            StationState    station,
            ContentRegistry registry,
            CraftingSystem  crafting,
            InventorySystem inventory)
        {
            _foundationUid = foundationUid;
            _station       = station;
            _registry      = registry;
            _crafting      = crafting;
            _inventory     = inventory;

            // Update title with building name.
            var titleLabel = this.Q<Label>("workbench-title");
            if (titleLabel != null)
                titleLabel.text = ResolveWorkbenchName();

            RebuildActiveTab();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private string ResolveWorkbenchName()
        {
            if (_station == null || string.IsNullOrEmpty(_foundationUid)) return "Workbench";
            if (!_station.foundations.TryGetValue(_foundationUid, out var f)) return "Workbench";
            if (_registry?.Buildables.TryGetValue(f.buildableId, out var def) == true)
                return def.displayName ?? "Workbench";
            return f.buildableId;
        }

        private void OnTabSelected(string tabId)
        {
            _activeTab = tabId;
            RebuildActiveTab();
        }

        private void RebuildActiveTab()
        {
            _tabContent.contentContainer.Clear();

            if (string.IsNullOrEmpty(_foundationUid) || _station == null)
            {
                var empty = MakeEmptyLabel("No workbench selected.");
                empty.style.paddingLeft = 10;
                empty.style.paddingTop  = 10;
                _tabContent.contentContainer.Add(empty);
                return;
            }

            switch (_activeTab)
            {
                case "status":  BuildStatusTab();  break;
                case "queue":   BuildQueueTab();   break;
                case "recipes": BuildRecipesTab(); break;
            }
        }

        // ── Status tab ─────────────────────────────────────────────────────────

        private void BuildStatusTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            if (!_station.foundations.TryGetValue(_foundationUid, out var f))
            {
                root.Add(MakeEmptyLabel("Workbench not found."));
                return;
            }

            BuildableDefinition benchDef = null;
            _registry?.Buildables.TryGetValue(f.buildableId, out benchDef);

            // Building name and type
            AddSectionHeader(root, "WORKBENCH");

            var nameRow = new VisualElement();
            nameRow.style.flexDirection  = FlexDirection.Row;
            nameRow.style.marginBottom   = 4;
            var nameLabel = new Label(benchDef?.displayName ?? f.buildableId);
            nameLabel.style.flexGrow  = 1;
            nameLabel.style.fontSize  = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameRow.Add(nameLabel);

            if (!string.IsNullOrEmpty(benchDef?.workbenchRoomType))
            {
                var typeLabel = new Label(benchDef.workbenchRoomType);
                typeLabel.style.fontSize = 11;
                typeLabel.style.color    = new Color(0.55f, 0.55f, 0.65f);
                typeLabel.style.alignSelf = Align.Center;
                nameRow.Add(typeLabel);
            }
            root.Add(nameRow);

            // HP bar and damage state
            AddSectionHeader(root, "CONDITION");

            float hpFrac = f.maxHealth > 0 ? (float)f.health / f.maxHealth : 1f;
            hpFrac = Mathf.Clamp01(hpFrac);
            var hpColor = hpFrac >= 0.75f ? new Color(0.3f, 0.8f, 0.4f)
                        : hpFrac >= 0.5f  ? new Color(0.9f, 0.75f, 0.3f)
                        : hpFrac >= 0.25f ? new Color(0.9f, 0.5f, 0.2f)
                                          : new Color(0.85f, 0.25f, 0.25f);
            var hpBar = MakeBar(hpFrac, hpColor);
            root.Add(hpBar);

            var damageLabel = new Label($"{f.health} / {f.maxHealth} HP  —  {DeriveDamageState(hpFrac)}");
            damageLabel.style.fontSize    = 11;
            damageLabel.style.color       = new Color(0.6f, 0.6f, 0.65f);
            damageLabel.style.marginTop   = 2;
            damageLabel.style.marginBottom = 6;
            root.Add(damageLabel);

            // Room bonus
            if (f.hasRoomBonus)
            {
                AddSectionHeader(root, "ROOM BONUS");
                var bonusRow = new VisualElement();
                bonusRow.style.flexDirection = FlexDirection.Row;
                bonusRow.style.marginBottom  = 6;

                var bonusTypeLabel = new Label(FormatRoomTypeName(benchDef?.workbenchRoomType));
                bonusTypeLabel.style.flexGrow = 1;
                bonusTypeLabel.style.fontSize = 12;
                bonusRow.Add(bonusTypeLabel);

                var bonusMultLabel = new Label($"×{f.roomBonusMultiplier:F2}");
                bonusMultLabel.style.fontSize = 12;
                bonusMultLabel.style.color    = new Color(0.9f, 0.75f, 0.3f);
                bonusRow.Add(bonusMultLabel);

                root.Add(bonusRow);
            }

            // Assigned NPC
            AddSectionHeader(root, "ASSIGNED NPC");

            var activeEntry = _crafting?.GetActiveEntry(_foundationUid, _station);
            string assignedNpcUid = activeEntry?.assignedNpcUid;

            if (!string.IsNullOrEmpty(assignedNpcUid) &&
                _station.npcs.TryGetValue(assignedNpcUid, out var npc))
            {
                var npcRow = new VisualElement();
                npcRow.style.flexDirection = FlexDirection.Row;
                npcRow.style.marginBottom  = 4;

                var npcName = new Label(npc.name ?? npc.uid);
                npcName.style.flexGrow = 1;
                npcName.style.fontSize = 12;
                npcRow.Add(npcName);

                string action =
                    !string.IsNullOrEmpty(npc.currentTaskId) ? npc.currentTaskId :
                    !string.IsNullOrEmpty(npc.currentJobId)  ? npc.currentJobId  :
                    "Idle";
                var actionLabel = new Label(action);
                actionLabel.style.fontSize = 11;
                actionLabel.style.color    = new Color(0.55f, 0.55f, 0.65f);
                npcRow.Add(actionLabel);

                root.Add(npcRow);
            }
            else
            {
                root.Add(MakeEmptyLabel("Unassigned"));
            }
        }

        // ── Queue tab ──────────────────────────────────────────────────────────

        private void BuildQueueTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            if (_crafting == null)
            {
                root.Add(MakeEmptyLabel("Crafting system unavailable."));
                return;
            }

            var queue = _crafting.GetQueue(_foundationUid, _station);

            // ── In-progress entry ──────────────────────────────────────────────
            AddSectionHeader(root, "IN PROGRESS");

            var activeEntry = queue.Count > 0 ? queue[0] : null;
            if (activeEntry == null ||
                (activeEntry.status != "hauling" && activeEntry.status != "executing"))
            {
                root.Add(MakeEmptyLabel("Nothing in progress."));
            }
            else
            {
                RecipeDefinition activeRecipe = null;
                _registry?.Recipes.TryGetValue(activeEntry.recipeId, out activeRecipe);
                var progressRow = new VisualElement();
                progressRow.style.marginBottom = 8;

                // NPC name
                string npcName = "Unassigned";
                if (!string.IsNullOrEmpty(activeEntry.assignedNpcUid) &&
                    _station.npcs.TryGetValue(activeEntry.assignedNpcUid, out var npc))
                    npcName = npc.name ?? npc.uid;

                var entryHeader = new VisualElement();
                entryHeader.style.flexDirection = FlexDirection.Row;
                entryHeader.style.marginBottom  = 4;

                var recipeName = new Label(activeRecipe?.displayName ?? activeEntry.recipeId);
                recipeName.style.flexGrow = 1;
                recipeName.style.fontSize = 13;
                recipeName.style.unityFontStyleAndWeight = FontStyle.Bold;
                entryHeader.Add(recipeName);

                var npcLabel = new Label(npcName);
                npcLabel.style.fontSize  = 11;
                npcLabel.style.color     = new Color(0.55f, 0.55f, 0.65f);
                npcLabel.style.alignSelf = Align.Center;
                entryHeader.Add(npcLabel);

                progressRow.Add(entryHeader);

                // Time remaining bar: fill = 1 - executionProgress (decreases toward 0)
                float remaining = Mathf.Clamp01(1f - activeEntry.executionProgress);
                var timeBar = MakeBar(remaining, new Color(0.3f, 0.6f, 0.9f));
                progressRow.Add(timeBar);

                var pctLabel = new Label($"{Mathf.RoundToInt(remaining * 100f)}% remaining  [{activeEntry.status}]");
                pctLabel.style.fontSize  = 10;
                pctLabel.style.color     = new Color(0.5f, 0.5f, 0.6f);
                pctLabel.style.marginTop = 2;
                progressRow.Add(pctLabel);

                root.Add(progressRow);
            }

            // ── Queued entries (indices 1+) ────────────────────────────────────
            AddSectionHeader(root, "QUEUED");

            // Collect pending entries: skip the active entry (index 0 if in-progress),
            // but include index 0 if it is still status "queued".
            var pending = new List<WorkbenchQueueEntry>();
            foreach (var entry in queue)
            {
                if (entry.status == "hauling" || entry.status == "executing") continue;
                pending.Add(entry);
            }

            if (pending.Count == 0)
            {
                root.Add(MakeEmptyLabel("No recipes queued."));
            }
            else
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var entry = pending[i];
                    RecipeDefinition recipe = null;
                    _registry?.Recipes.TryGetValue(entry.recipeId, out recipe);

                    var row = new VisualElement();
                    row.AddToClassList(QueueRowClass);
                    row.style.flexDirection  = FlexDirection.Row;
                    row.style.alignItems     = Align.Center;
                    row.style.paddingTop     = 4;
                    row.style.paddingBottom  = 4;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.25f, 0.25f, 0.3f);

                    var recipeNameLabel = new Label(recipe?.displayName ?? entry.recipeId);
                    recipeNameLabel.style.flexGrow = 1;
                    recipeNameLabel.style.fontSize = 12;
                    row.Add(recipeNameLabel);

                    // ▲ button (move up)
                    string capturedEntryUid = entry.uid;
                    if (i > 0)
                    {
                        var upBtn = new Button(() =>
                        {
                            _crafting.MoveInQueue(_foundationUid, capturedEntryUid, -1, _station);
                            RebuildActiveTab();
                        }) { text = "▲" };
                        StyleIconButton(upBtn);
                        row.Add(upBtn);
                    }

                    // ▼ button (move down)
                    if (i < pending.Count - 1)
                    {
                        var downBtn = new Button(() =>
                        {
                            _crafting.MoveInQueue(_foundationUid, capturedEntryUid, +1, _station);
                            RebuildActiveTab();
                        }) { text = "▼" };
                        StyleIconButton(downBtn);
                        row.Add(downBtn);
                    }

                    // ✕ remove button
                    var removeBtn = new Button(() =>
                    {
                        _crafting.RemoveFromQueue(_foundationUid, capturedEntryUid, _station);
                        RebuildActiveTab();
                    }) { text = "✕" };
                    StyleIconButton(removeBtn);
                    removeBtn.style.color = new Color(0.85f, 0.35f, 0.35f);
                    row.Add(removeBtn);

                    root.Add(row);
                }
            }

            // ── Add to Queue button ────────────────────────────────────────────
            var addBtn = new Button(() => SelectTab("recipes")) { text = "+ ADD TO QUEUE" };
            addBtn.style.marginTop      = 12;
            addBtn.style.fontSize       = 12;
            addBtn.style.backgroundColor = new Color(0.2f, 0.4f, 0.6f);
            addBtn.style.color          = Color.white;
            addBtn.style.borderTopWidth    = 0;
            addBtn.style.borderRightWidth  = 0;
            addBtn.style.borderBottomWidth = 0;
            addBtn.style.borderLeftWidth   = 0;
            addBtn.style.paddingTop    = 6;
            addBtn.style.paddingBottom = 6;
            root.Add(addBtn);
        }

        // ── Recipes tab ────────────────────────────────────────────────────────

        private void BuildRecipesTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            if (_crafting == null)
            {
                root.Add(MakeEmptyLabel("Crafting system unavailable."));
                return;
            }

            var allRecipes = _crafting.GetAllRecipesForWorkbench(_foundationUid, _station);

            AddSectionHeader(root, "AVAILABLE RECIPES");

            if (allRecipes.Count == 0)
            {
                root.Add(MakeEmptyLabel("No recipes for this workbench type."));
                return;
            }

            foreach (var recipe in allRecipes)
            {
                bool isLocked = !string.IsNullOrEmpty(recipe.unlockTag) &&
                                !_station.HasTag(recipe.unlockTag);

                var row = new VisualElement();
                row.AddToClassList(RecipeRowClass);
                row.style.flexDirection  = FlexDirection.Column;
                row.style.paddingTop     = 6;
                row.style.paddingBottom  = 6;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.25f, 0.25f, 0.3f);
                row.style.opacity        = isLocked ? 0.45f : 1f;
                // Locked recipes are non-interactive.
                row.pickingMode = isLocked ? PickingMode.Ignore : PickingMode.Position;

                if (isLocked)
                    row.AddToClassList(RecipeLockedClass);

                // Recipe header: name + output
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.marginBottom  = 3;

                var recipeNameLabel = new Label(recipe.displayName ?? recipe.id);
                recipeNameLabel.style.flexGrow  = 1;
                recipeNameLabel.style.fontSize  = 13;
                recipeNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                headerRow.Add(recipeNameLabel);

                // Output item
                string outputDisplay = recipe.outputItemId ?? "?";
                if (_registry?.Items.TryGetValue(recipe.outputItemId ?? "", out var itemDef) == true)
                    outputDisplay = itemDef.displayName ?? recipe.outputItemId;
                if (recipe.outputQuantity > 1) outputDisplay += $" ×{recipe.outputQuantity}";
                var outputLabel = new Label(outputDisplay);
                outputLabel.style.fontSize  = 11;
                outputLabel.style.color     = new Color(0.55f, 0.8f, 0.55f);
                outputLabel.style.alignSelf = Align.Center;
                headerRow.Add(outputLabel);

                row.Add(headerRow);

                if (isLocked)
                {
                    // Show unlock requirement
                    var lockLabel = new Label($"🔒 Requires: {FormatUnlockTag(recipe.unlockTag)}");
                    lockLabel.style.fontSize = 11;
                    lockLabel.style.color    = new Color(0.75f, 0.55f, 0.3f);
                    row.Add(lockLabel);
                }
                else
                {
                    // Material requirements with availability indicators
                    if (recipe.inputMaterials != null && recipe.inputMaterials.Count > 0)
                    {
                        foreach (var kv in recipe.inputMaterials)
                        {
                            var matRow = new VisualElement();
                            matRow.AddToClassList(MatRowClass);
                            matRow.style.flexDirection = FlexDirection.Row;
                            matRow.style.marginTop     = 1;

                            string matName = kv.Key;
                            if (_registry?.Items.TryGetValue(kv.Key, out var matDef) == true)
                                matName = matDef.displayName ?? kv.Key;

                            var matLabel = new Label($"  {matName} ×{kv.Value}");
                            matLabel.style.flexGrow = 1;
                            matLabel.style.fontSize = 11;
                            matRow.Add(matLabel);

                            var status = _inventory?.CheckSingleMaterial(_station, kv.Key, kv.Value)
                                         ?? InventorySystem.MaterialStatus.Missing;
                            var (statusSymbol, statusColor) = MaterialStatusDisplay(status);
                            var statusLabel = new Label(statusSymbol);
                            statusLabel.style.fontSize = 11;
                            statusLabel.style.color    = statusColor;
                            matRow.Add(statusLabel);

                            row.Add(matRow);
                        }
                    }
                    else
                    {
                        var noMatsLabel = new Label("  No materials required.");
                        noMatsLabel.style.fontSize = 11;
                        noMatsLabel.style.color    = new Color(0.5f, 0.5f, 0.6f);
                        row.Add(noMatsLabel);
                    }

                    // Register click to enqueue
                    string capturedRecipeId = recipe.id;
                    row.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (_crafting == null || _station == null) return;
                        var (ok, reason, _) = _crafting.QueueRecipe(_foundationUid, capturedRecipeId, _station);
                        if (!ok)
                            Debug.LogWarning($"[WorkbenchPanel] QueueRecipe failed: {reason}");
                        else
                            SelectTab("queue");
                    });
                }

                root.Add(row);
            }
        }

        // ── Shared helpers ──────────────────────────────────────────────────────

        private void AddSectionHeader(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList(SectionHeaderClass);
            label.style.fontSize     = 10;
            label.style.color        = new Color(0.5f, 0.5f, 0.6f);
            label.style.marginTop    = 8;
            label.style.marginBottom = 4;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(label);
        }

        private VisualElement MakeBar(float fill, Color color)
        {
            var track = new VisualElement();
            track.style.height          = 8;
            track.style.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            track.style.borderTopLeftRadius     = 3;
            track.style.borderTopRightRadius    = 3;
            track.style.borderBottomLeftRadius  = 3;
            track.style.borderBottomRightRadius = 3;
            track.style.overflow    = Overflow.Hidden;
            track.style.marginBottom = 2;

            var fill_ = new VisualElement();
            fill_.style.height  = new StyleLength(new Length(100f, LengthUnit.Percent));
            fill_.style.width   = new StyleLength(new Length(Mathf.Clamp01(fill) * 100f, LengthUnit.Percent));
            fill_.style.backgroundColor = color;
            track.Add(fill_);
            return track;
        }

        private static Label MakeEmptyLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize    = 12;
            label.style.color       = new Color(0.45f, 0.45f, 0.5f);
            label.style.whiteSpace  = WhiteSpace.Normal;
            label.style.marginBottom = 4;
            return label;
        }

        private static string DeriveDamageState(float hpFrac)
        {
            if (hpFrac >= 0.75f) return "Undamaged";
            if (hpFrac >= 0.50f) return "Lightly Damaged";
            if (hpFrac >= 0.25f) return "Damaged";
            if (hpFrac >  0f)    return "Heavily Damaged";
            return "Destroyed";
        }

        private static string FormatRoomTypeName(string roomTypeId)
        {
            if (string.IsNullOrEmpty(roomTypeId)) return "Room Bonus";
            // e.g. "general_workshop" → "General Workshop Bonus"
            var words = roomTypeId.Replace('_', ' ');
            return System.Globalization.CultureInfo.InvariantCulture
                       .TextInfo.ToTitleCase(words) + " Bonus";
        }

        private static string FormatUnlockTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;
            // e.g. "tech.advanced_fabrication" → "Advanced Fabrication"
            int dotIndex = tag.IndexOf('.');
            string name = dotIndex >= 0 ? tag.Substring(dotIndex + 1) : tag;
            name = name.Replace('_', ' ');
            return System.Globalization.CultureInfo.InvariantCulture
                       .TextInfo.ToTitleCase(name);
        }

        private static (string symbol, Color color) MaterialStatusDisplay(
            InventorySystem.MaterialStatus status)
        {
            return status switch
            {
                InventorySystem.MaterialStatus.Sufficient => ("●", new Color(0.3f, 0.8f, 0.4f)),
                InventorySystem.MaterialStatus.Partial    => ("●", new Color(0.9f, 0.75f, 0.3f)),
                _                                         => ("●", new Color(0.85f, 0.25f, 0.25f)),
            };
        }

        private static void StyleIconButton(Button btn)
        {
            btn.style.backgroundColor  = Color.clear;
            btn.style.borderTopWidth   = 0;
            btn.style.borderRightWidth  = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth   = 0;
            btn.style.color            = new Color(0.65f, 0.65f, 0.7f);
            btn.style.fontSize         = 11;
            btn.style.paddingLeft      = 4;
            btn.style.paddingRight     = 4;
        }
    }
}
