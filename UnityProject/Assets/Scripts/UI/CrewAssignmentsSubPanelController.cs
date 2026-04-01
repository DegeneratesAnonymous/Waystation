// CrewAssignmentsSubPanelController.cs
// Crew → Assignments sub-tab panel (UI-013).
//
// Displays all current NPC task assignments grouped by task type.
// Task types (in order): Construction, Farming, Research, Medical,
//                        Hauling, Security, Recreation, Idle.
//
// Each group is a collapsible section whose header shows the task-type label
// and the NPC count.  Groups with zero members are hidden entirely.
// Idle and Recreation groups are collapsed by default (noise reduction).
//
// Each NPC row within a group shows:
//   • portrait circle (initials fallback)
//   • name label
//   • specific task description (e.g. "Guard Post in security_wing")
//
// Clicking an NPC row fires OnCrewRowClicked(npcUid) so the HUD controller
// can call SelectCrewMember(npcUid).
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
    /// Crew → Assignments sub-tab panel.  Extends <see cref="VisualElement"/> so
    /// it can be added directly to the side-panel drawer.
    /// </summary>
    public class CrewAssignmentsSubPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks an NPC row.
        /// Argument is the NPC uid to pass to SelectCrewMember.
        /// </summary>
        public event Action<string> OnCrewRowClicked;

        // ── Task type display order ────────────────────────────────────────────

        /// <summary>
        /// Canonical task-type labels in display order.
        /// </summary>
        public static readonly string[] TaskTypeOrder =
        {
            "Construction",
            "Farming",
            "Research",
            "Medical",
            "Hauling",
            "Security",
            "Recreation",
            "Idle",
        };

        /// <summary>
        /// Task types that are collapsed when the panel first renders.
        /// </summary>
        private static readonly HashSet<string> DefaultCollapsed =
            new HashSet<string>(StringComparer.Ordinal) { "Recreation", "Idle" };

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass         = "ws-crew-assignments-panel";
        private const string GroupClass         = "ws-crew-assignments-panel__group";
        private const string GroupHeaderClass   = "ws-crew-assignments-panel__group-header";
        private const string GroupLabelClass    = "ws-crew-assignments-panel__group-label";
        private const string GroupCountClass    = "ws-crew-assignments-panel__group-count";
        private const string GroupBodyClass     = "ws-crew-assignments-panel__group-body";
        private const string RowClass           = "ws-crew-assignments-panel__npc-row";
        private const string PortraitClass      = "ws-crew-assignments-panel__portrait";
        private const string NameClass          = "ws-crew-assignments-panel__npc-name";
        private const string TaskDescClass      = "ws-crew-assignments-panel__task-desc";

        // ── Inner class: one collapsible group ────────────────────────────────

        /// <summary>
        /// Represents one collapsible task-type group in the Assignments panel.
        /// </summary>
        private sealed class TaskGroup
        {
            public readonly string        TaskType;
            public readonly VisualElement Container;   // outer wrapper (hidden when 0 members)
            public readonly Label         CountLabel;  // e.g. "(3)"
            public readonly VisualElement Body;        // NPC rows live here
            public          bool          IsExpanded;

            internal TaskGroup(string taskType, bool startExpanded)
            {
                TaskType    = taskType;
                IsExpanded  = startExpanded;

                // ── Outer container ──────────────────────────────────────────
                Container = new VisualElement();
                Container.AddToClassList(GroupClass);
                Container.style.marginBottom = 4;

                // ── Header row ───────────────────────────────────────────────
                var header = new VisualElement();
                header.AddToClassList(GroupHeaderClass);
                header.style.flexDirection  = FlexDirection.Row;
                header.style.alignItems     = Align.Center;
                header.style.paddingTop     = 5;
                header.style.paddingBottom  = 5;
                header.style.paddingLeft    = 6;
                header.style.paddingRight   = 6;
                header.style.backgroundColor = new Color(0.18f, 0.22f, 0.30f, 0.9f);
                header.style.borderBottomWidth = 1;
                header.style.borderBottomColor = new Color(0.3f, 0.35f, 0.45f, 0.6f);
                // Pointer change to indicate interactivity
                header.style.cursor = new StyleCursor(MouseCursor.Link);

                var label = new Label(taskType.ToUpper());
                label.AddToClassList(GroupLabelClass);
                label.style.flexGrow       = 1;
                label.style.fontSize       = 11;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color          = new Color(0.8f, 0.85f, 0.95f, 1f);
                header.Add(label);

                CountLabel = new Label("(0)");
                CountLabel.AddToClassList(GroupCountClass);
                CountLabel.style.fontSize  = 10;
                CountLabel.style.color     = new Color(0.6f, 0.65f, 0.75f, 1f);
                CountLabel.style.marginRight = 6;
                header.Add(CountLabel);

                var chevron = new Label(startExpanded ? "▾" : "▸");
                chevron.name = "chevron";
                chevron.style.fontSize  = 10;
                chevron.style.color     = new Color(0.6f, 0.65f, 0.75f, 1f);
                header.Add(chevron);

                // ── Body (NPC rows) ──────────────────────────────────────────
                Body = new VisualElement();
                Body.AddToClassList(GroupBodyClass);
                Body.style.flexDirection = FlexDirection.Column;
                Body.style.display       = startExpanded ? DisplayStyle.Flex : DisplayStyle.None;

                // Wire up header click to toggle collapse
                header.RegisterCallback<ClickEvent>(_ => Toggle());

                Container.Add(header);
                Container.Add(Body);
            }

            /// <summary>Toggles the expanded/collapsed state of this group.</summary>
            public void Toggle()
            {
                IsExpanded = !IsExpanded;
                Body.style.display = IsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                var chevron = Container.Q<Label>("chevron");
                if (chevron != null)
                    chevron.text = IsExpanded ? "▾" : "▸";
            }

            /// <summary>
            /// Updates the header count badge and shows/hides the entire group.
            /// </summary>
            public void SetCount(int count)
            {
                CountLabel.text = $"({count})";
                Container.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── State ──────────────────────────────────────────────────────────────

        private readonly Dictionary<string, TaskGroup> _groups =
            new Dictionary<string, TaskGroup>(StringComparer.Ordinal);

        private StationState _station;
        private JobSystem    _jobSystem;

        // ── Constructor ────────────────────────────────────────────────────────

        public CrewAssignmentsSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // Wrap all groups in a scrollable container.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            var scrollContent = scroll.contentContainer;
            scrollContent.style.flexDirection = FlexDirection.Column;

            foreach (var taskType in TaskTypeOrder)
            {
                bool startExpanded = !DefaultCollapsed.Contains(taskType);
                var group = new TaskGroup(taskType, startExpanded);
                // Hide initially — SetCount() will show it when populated.
                group.Container.style.display = DisplayStyle.None;
                _groups[taskType] = group;
                scrollContent.Add(group.Container);
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the assignments view from the latest station state.
        /// Call once on mount and again on every OnTick (throttled every 5 ticks).
        /// Grouping is derived directly from NPC job ids via the static
        /// <see cref="JobSystem.ClassifyTaskType"/> helper so the panel works
        /// even when <paramref name="jobSystem"/> is null (task descriptions
        /// fall back to a cleaned job-id label in that case).
        /// </summary>
        public void Refresh(StationState station, JobSystem jobSystem)
        {
            if (station == null) return;

            _station   = station;
            _jobSystem = jobSystem;

            // Group NPCs by task type using the static ClassifyTaskType helper.
            // This does not require a live JobSystem instance.
            var assignments = new Dictionary<string, List<NPCInstance>>(StringComparer.Ordinal);
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                string taskType = JobSystem.ClassifyTaskType(npc.currentJobId);
                if (!assignments.TryGetValue(taskType, out var bucket))
                {
                    bucket = new List<NPCInstance>();
                    assignments[taskType] = bucket;
                }
                bucket.Add(npc);
            }

            foreach (var taskType in TaskTypeOrder)
            {
                assignments.TryGetValue(taskType, out var npcs);

                // Sort NPCs within each group by name then uid for a stable, deterministic order.
                npcs?.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.name, b.name, StringComparison.Ordinal);
                    return cmp != 0 ? cmp : string.Compare(a.uid, b.uid, StringComparison.Ordinal);
                });

                int count = npcs?.Count ?? 0;

                var group = _groups[taskType];
                group.SetCount(count);

                group.Body.Clear();
                if (count > 0)
                {
                    foreach (var npc in npcs)
                        group.Body.Add(BuildNpcRow(npc, station, jobSystem));
                }
            }
        }

        // ── Row builder ────────────────────────────────────────────────────────

        private VisualElement BuildNpcRow(NPCInstance npc, StationState station, JobSystem jobSystem)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.paddingTop     = 4;
            row.style.paddingBottom  = 4;
            row.style.paddingLeft    = 6;
            row.style.paddingRight   = 6;
            row.style.marginBottom   = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.25f, 0.28f, 0.35f, 0.4f);

            row.userData = npc.uid;

            // ── Portrait (initials fallback) ─────────────────────────────────
            var portrait = new Label(GetInitials(npc.name));
            portrait.AddToClassList(PortraitClass);
            portrait.style.width     = 24;
            portrait.style.height    = 24;
            portrait.style.flexShrink = 0;
            portrait.style.borderTopLeftRadius     = 12;
            portrait.style.borderTopRightRadius    = 12;
            portrait.style.borderBottomLeftRadius  = 12;
            portrait.style.borderBottomRightRadius = 12;
            portrait.style.backgroundColor  = new Color(0.22f, 0.28f, 0.38f, 1f);
            portrait.style.unityTextAlign   = TextAnchor.MiddleCenter;
            portrait.style.fontSize         = 9;
            portrait.style.color            = new Color(0.9f, 0.9f, 0.95f, 1f);
            portrait.style.marginRight      = 6;
            row.Add(portrait);

            // ── Name + task description ──────────────────────────────────────
            var stack = new VisualElement();
            stack.style.flexDirection = FlexDirection.Column;
            stack.style.flexGrow      = 1;

            var nameLabel = new Label(npc.name);
            nameLabel.AddToClassList(NameClass);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.fontSize       = 12;

            string desc = jobSystem != null
                ? jobSystem.GetTaskDescription(npc, station)
                : GetActivityFallback(npc);
            var descLabel = new Label(desc);
            descLabel.AddToClassList(TaskDescClass);
            descLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            descLabel.style.fontSize       = 9;
            descLabel.style.opacity        = 0.6f;

            stack.Add(nameLabel);
            stack.Add(descLabel);
            row.Add(stack);

            // ── Click handler ────────────────────────────────────────────────
            string capturedUid = npc.uid;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                Debug.Log($"[CrewAssignmentsPanel] NPC row clicked: {capturedUid}");
                OnCrewRowClicked?.Invoke(capturedUid);
            });

            return row;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Trim().Split(
                new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
            return (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpper();
        }

        private static string GetActivityFallback(NPCInstance npc)
        {
            if (npc.isSleeping) return "Sleeping";
            if (npc.missionUid != null) return "On Mission";
            if (!string.IsNullOrEmpty(npc.currentJobId))
                return npc.currentJobId.Replace("job.", "").Replace("_", " ");
            return "Idle";
        }
    }
}
