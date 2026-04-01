// CrewSchedulesSubPanelController.cs
// Crew → Schedules sub-tab panel (UI-014).
//
// Displays a 24-slot per-NPC schedule grid where each column corresponds to
// one in-game hour (0–23).  NPC rows are virtualised via ListView so only
// visible rows are in the DOM — keeps the grid fast even at 50+ crew.
//
// Day/night dividers appear at tick 6 (day start) and tick 21 (night start).
//
// Slot types and colours:
//   Work       — blue   (hours 6–17 default, per NPCInstance.InitDefaultSchedule)
//   Rest       — dark   (hours 18–23, 0–5 default)
//   Recreation — teal
//
// Interactions:
//   • Clicking a slot cycles: Work → Rest → Recreation → Work
//   • Dragging across cells on the same row sets all to the drag-started type
//   • Checkbox on each row for multi-select;
//     template dropdown → Apply calls JobSystem.ApplyTemplate for all selected NPCs
//
// Data is pushed via Refresh(StationState, JobSystem).
// Call on mount and again on every GameManager.OnTick (throttled every 5 ticks
// in WaystationHUDController to avoid GC churn).
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
    /// Crew → Schedules sub-tab panel.  Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class CrewSchedulesSubPanelController : VisualElement
    {
        // ── Grid constants ────────────────────────────────────────────────────

        private const int   TickCount      = 24;
        /// <summary>Tick index at which day starts (used for the day/night divider).</summary>
        public  const int   DayStartTick   = 6;
        /// <summary>Tick index at which night starts (used for the day/night divider).</summary>
        public  const int   NightStartTick = 21;

        // Left NPC-info column (checkbox + portrait + name): fixed width in px.
        public  const float LeftColWidth  = 140f;
        // Width of each 24 tick cells.
        public  const float CellWidth     = 14f;
        // Fixed row height used by the ListView for virtualisation.
        public  const float RowHeight     = 28f;

        // ── Slot colours ──────────────────────────────────────────────────────

        public  static readonly Color SlotWorkColor       = new Color(0.18f, 0.42f, 0.80f, 1f);
        public  static readonly Color SlotRestColor       = new Color(0.13f, 0.15f, 0.20f, 1f);
        public  static readonly Color SlotRecreationColor = new Color(0.10f, 0.52f, 0.52f, 1f);

        // Accent colour used on the left border of day/night divider cells.
        public  static readonly Color DividerColor     = new Color(0.80f, 0.70f, 0.30f, 0.9f);
        private  static readonly Color NormalBorderColor = new Color(0.25f, 0.28f, 0.35f, 0.3f);

        // ── USS class names ───────────────────────────────────────────────────

        public  const string PanelClass     = "ws-crew-schedules-panel";
        public  const string HeaderRowClass = "ws-crew-schedules-panel__header";
        public  const string TickLabelClass = "ws-crew-schedules-panel__tick-label";
        public  const string ToolbarClass   = "ws-crew-schedules-panel__toolbar";
        public  const string RowClass       = "ws-crew-schedules-panel__npc-row";
        public  const string CheckboxClass  = "ws-crew-schedules-panel__checkbox";
        public  const string PortraitClass  = "ws-crew-schedules-panel__portrait";
        public  const string NameClass      = "ws-crew-schedules-panel__npc-name";
        public  const string SlotClass      = "ws-crew-schedules-panel__slot";

        // ── Templates ─────────────────────────────────────────────────────────

        /// <summary>
        /// Available schedule template names shown in the dropdown.
        /// </summary>
        public static readonly string[] Templates = { "Day Worker", "Night Worker", "Custom" };

        // ── State ──────────────────────────────────────────────────────────────

        private StationState      _station;
        private JobSystem         _jobSystem;

        private readonly List<NPCInstance> _visibleNpcs = new List<NPCInstance>();
        private readonly HashSet<string>   _selected    =
            new HashSet<string>(StringComparer.Ordinal);

        // Drag-paint state: set while the player holds the mouse button down and
        // moves across slot cells on the same NPC row.
        private bool         _isDragging;
        private string       _dragNpcUid;
        private ScheduleSlot _dragSlotType;

        private string _selectedTemplate = "Day Worker";

        // ── UI references ─────────────────────────────────────────────────────

        private ListView _listView;

        // ── Constructor ───────────────────────────────────────────────────────

        public CrewSchedulesSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            BuildToolbar();
            BuildHeaderRow();
            BuildListView();

            // End any active drag when the pointer is released anywhere on the panel.
            RegisterCallback<PointerUpEvent>(_ => _isDragging = false);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the schedule grid from the latest station state.
        /// Call once on mount and again on every OnTick (throttled every 5 ticks).
        /// Works even when <paramref name="jobSystem"/> is null — slot mutations are
        /// silently skipped in that case.
        /// </summary>
        public void Refresh(StationState station, JobSystem jobSystem)
        {
            if (station == null) return;

            _station   = station;
            _jobSystem = jobSystem;

            _visibleNpcs.Clear();
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.npcSchedule == null || npc.npcSchedule.Length != TickCount)
                    npc.InitDefaultSchedule();
                _visibleNpcs.Add(npc);
            }

            // Deterministic sort: name then uid (matches Assignments panel ordering).
            _visibleNpcs.Sort((a, b) =>
            {
                int c = string.Compare(a.name, b.name, StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.uid, b.uid, StringComparison.Ordinal);
            });

            _listView.itemsSource = _visibleNpcs;
            _listView.RefreshItems();
        }

        // ── Slot cycling ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the next slot type in the cycle:
        /// Work → Rest → Recreation → Work.
        /// </summary>
        public static ScheduleSlot CycleSlot(ScheduleSlot current)
        {
            switch (current)
            {
                case ScheduleSlot.Work:       return ScheduleSlot.Rest;
                case ScheduleSlot.Rest:       return ScheduleSlot.Recreation;
                case ScheduleSlot.Recreation: return ScheduleSlot.Work;
                default:                      return ScheduleSlot.Work;
            }
        }

        // ── Private builders ───────────────────────────────────────────────────

        private void BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList(ToolbarClass);
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems    = Align.Center;
            toolbar.style.paddingBottom = 6;
            toolbar.style.paddingTop    = 2;

            var templateLabel = new Label("Template:");
            templateLabel.style.fontSize    = 11;
            templateLabel.style.color       = new Color(0.70f, 0.75f, 0.85f, 1f);
            templateLabel.style.marginRight = 4;
            toolbar.Add(templateLabel);

            var dropdown = new DropdownField(new List<string>(Templates), 0);
            dropdown.style.width       = 110;
            dropdown.style.marginRight = 8;
            dropdown.RegisterValueChangedCallback(evt => _selectedTemplate = evt.newValue);
            toolbar.Add(dropdown);

            var applyBtn = new Button(OnApplyTemplate) { text = "Apply" };
            applyBtn.style.paddingLeft   = 8;
            applyBtn.style.paddingRight  = 8;
            applyBtn.style.paddingTop    = 3;
            applyBtn.style.paddingBottom = 3;
            toolbar.Add(applyBtn);

            Add(toolbar);
        }

        private void BuildHeaderRow()
        {
            var header = new VisualElement();
            header.name = "schedule-header";
            header.AddToClassList(HeaderRowClass);
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.marginBottom  = 2;

            // Blank spacer aligned with the left NPC-info column.
            var blank = new VisualElement();
            blank.style.width     = LeftColWidth;
            blank.style.flexShrink = 0;
            header.Add(blank);

            for (int t = 0; t < TickCount; t++)
            {
                bool isDivider = (t == DayStartTick || t == NightStartTick);

                var cell = new VisualElement();
                cell.AddToClassList(TickLabelClass);
                cell.style.width      = CellWidth;
                cell.style.flexShrink = 0;
                cell.style.alignItems = Align.Center;

                if (isDivider)
                {
                    cell.style.borderLeftWidth = 2;
                    cell.style.borderLeftColor = DividerColor;
                }

                // Show the tick number at every 3rd tick and always at dividers.
                if (t % 3 == 0 || isDivider)
                {
                    var lbl = new Label(t.ToString());
                    lbl.style.fontSize       = 8;
                    lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                    lbl.style.color          = isDivider
                        ? new Color(0.90f, 0.80f, 0.35f, 1f)
                        : new Color(0.55f, 0.60f, 0.70f, 1f);
                    cell.Add(lbl);
                }

                header.Add(cell);
            }

            Add(header);
        }

        private void BuildListView()
        {
            _listView = new ListView
            {
                makeItem             = MakeRow,
                bindItem             = BindRow,
                itemsSource          = _visibleNpcs,
                fixedItemHeight      = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType        = SelectionType.None,
            };
            _listView.style.flexGrow = 1;
            Add(_listView);
        }

        // ── Row factory (called by ListView) ──────────────────────────────────

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.height        = RowHeight;
            row.style.overflow      = Overflow.Hidden;

            // ── Left NPC-info column ─────────────────────────────────────────
            var infoCol = new VisualElement();
            infoCol.style.width        = LeftColWidth;
            infoCol.style.flexShrink   = 0;
            infoCol.style.flexDirection = FlexDirection.Row;
            infoCol.style.alignItems    = Align.Center;

            var checkbox = new Toggle();
            checkbox.AddToClassList(CheckboxClass);
            checkbox.style.marginRight = 4;
            checkbox.style.marginLeft  = 0;
            // Register the checkbox callback once here; BindRow updates userData with
            // the current NPC uid so the handler always reads the correct target.
            checkbox.RegisterValueChangedCallback(OnCheckboxChanged);
            infoCol.Add(checkbox);

            var portrait = new Label("??");
            portrait.AddToClassList(PortraitClass);
            portrait.style.width                    = 22;
            portrait.style.height                   = 22;
            portrait.style.flexShrink               = 0;
            portrait.style.borderTopLeftRadius      = 11;
            portrait.style.borderTopRightRadius     = 11;
            portrait.style.borderBottomLeftRadius   = 11;
            portrait.style.borderBottomRightRadius  = 11;
            portrait.style.backgroundColor          = new Color(0.22f, 0.28f, 0.38f, 1f);
            portrait.style.unityTextAlign           = TextAnchor.MiddleCenter;
            portrait.style.fontSize                 = 8;
            portrait.style.color                    = new Color(0.9f, 0.9f, 0.95f, 1f);
            portrait.style.marginRight              = 4;
            infoCol.Add(portrait);

            var nameLabel = new Label("—");
            nameLabel.AddToClassList(NameClass);
            nameLabel.style.fontSize       = 11;
            nameLabel.style.flexGrow       = 1;
            nameLabel.style.overflow       = Overflow.Hidden;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            infoCol.Add(nameLabel);

            row.Add(infoCol); // row[0]

            // ── Slot cells (row[1..24]) ──────────────────────────────────────
            for (int t = 0; t < TickCount; t++)
            {
                int  capturedTick = t;
                bool isDivider    = (t == DayStartTick || t == NightStartTick);

                var cell = new VisualElement();
                cell.AddToClassList(SlotClass);
                cell.style.width        = CellWidth;
                cell.style.height       = RowHeight - 4;
                cell.style.flexShrink   = 0;
                cell.style.marginTop    = 2;
                cell.style.marginBottom = 2;

                if (isDivider)
                {
                    cell.style.borderLeftWidth = 2;
                    cell.style.borderLeftColor = DividerColor;
                }
                else
                {
                    cell.style.borderLeftWidth = 1;
                    cell.style.borderLeftColor = NormalBorderColor;
                }

                // ── Click / drag-paint ────────────────────────────────────────
                cell.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    var npcUid = GetRowNpcUid(cell);
                    if (string.IsNullOrEmpty(npcUid)) return;

                    var npc = FindNpc(npcUid);
                    if (npc?.npcSchedule == null) return;

                    var nextSlot  = CycleSlot(npc.npcSchedule[capturedTick]);
                    _dragSlotType = nextSlot;
                    _dragNpcUid   = npcUid;
                    _isDragging   = true;

                    // Capture the pointer so PointerUpEvent is guaranteed to fire
                    // on this element even when the pointer is released outside it.
                    cell.CapturePointer(evt.pointerId);

                    ApplySlot(npcUid, capturedTick, nextSlot, cell);
                    evt.StopPropagation();
                });

                cell.RegisterCallback<PointerUpEvent>(evt =>
                {
                    _isDragging = false;
                    if (cell.HasPointerCapture(evt.pointerId))
                        cell.ReleasePointer(evt.pointerId);
                });

                cell.RegisterCallback<PointerCaptureOutEvent>(_ => _isDragging = false);

                cell.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (!_isDragging) return;
                    var npcUid = GetRowNpcUid(cell);
                    if (string.IsNullOrEmpty(npcUid) || npcUid != _dragNpcUid) return;

                    ApplySlot(npcUid, capturedTick, _dragSlotType, cell);
                });

                row.Add(cell);
            }

            // Releasing the pointer anywhere on the row also ends the drag.
            row.RegisterCallback<PointerUpEvent>(_ => _isDragging = false);

            return row;
        }

        // ── Row binding (called by ListView on scroll / item recycling) ────────

        private void BindRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleNpcs.Count) return;

            var npc = _visibleNpcs[index];
            element.userData = npc.uid;

            // ── Left column ──────────────────────────────────────────────────
            var infoCol   = element[0];
            var checkbox  = infoCol[0] as Toggle;
            var portrait  = infoCol[1] as Label;
            var nameLabel = infoCol[2] as Label;

            if (checkbox != null)
            {
                checkbox.userData = npc.uid;
                checkbox.SetValueWithoutNotify(_selected.Contains(npc.uid));
                // Callback was registered once in MakeRow; only userData is updated here.
            }

            if (portrait  != null) portrait.text  = GetInitials(npc.name);
            if (nameLabel != null) nameLabel.text  = npc.name;

            // ── Slot cells (element[1..24]) ──────────────────────────────────
            for (int t = 0; t < TickCount; t++)
            {
                var cell = element[t + 1];
                UpdateCellColor(cell, npc.npcSchedule != null
                    ? npc.npcSchedule[t]
                    : ScheduleSlot.Work);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void ApplySlot(string npcUid, int tick, ScheduleSlot slot, VisualElement cell)
        {
            var npc = FindNpc(npcUid);
            if (npc?.npcSchedule == null) return;

            npc.npcSchedule[tick] = slot;
            UpdateCellColor(cell, slot);
            _jobSystem?.SetSlot(npcUid, tick, slot, _station);
        }

        private void OnApplyTemplate()
        {
            if (_selected.Count == 0 || _jobSystem == null || _station == null) return;
            if (_selectedTemplate == "Custom") return; // no-op for Custom template

            var uids = new string[_selected.Count];
            _selected.CopyTo(uids);
            _jobSystem.ApplyTemplate(uids, _selectedTemplate, _station);
            _listView.RefreshItems();
        }

        private void OnCheckboxChanged(ChangeEvent<bool> evt)
        {
            var toggle = evt.target as Toggle;
            var uid    = toggle?.userData as string;
            if (string.IsNullOrEmpty(uid)) return;

            if (evt.newValue)
                _selected.Add(uid);
            else
                _selected.Remove(uid);
        }

        private NPCInstance FindNpc(string uid)
        {
            if (_station == null || uid == null) return null;
            _station.npcs.TryGetValue(uid, out var npc);
            return npc;
        }

        /// <summary>Reads the NPC uid stored on the nearest ancestor row element.</summary>
        private static string GetRowNpcUid(VisualElement cell)
        {
            var row = cell.GetFirstAncestorWithClass(RowClass);
            return row?.userData as string;
        }

        private static void UpdateCellColor(VisualElement cell, ScheduleSlot slot)
        {
            switch (slot)
            {
                case ScheduleSlot.Work:       cell.style.backgroundColor = SlotWorkColor;       break;
                case ScheduleSlot.Rest:       cell.style.backgroundColor = SlotRestColor;       break;
                case ScheduleSlot.Recreation: cell.style.backgroundColor = SlotRecreationColor; break;
            }
        }

        private static readonly char[] SpaceSeparator = { ' ' };

        private static string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Trim().Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
            return (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpper();
        }
    }
}
