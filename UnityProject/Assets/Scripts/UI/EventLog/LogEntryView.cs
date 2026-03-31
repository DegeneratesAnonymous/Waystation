// LogEntryView.cs
// VisualElement subclass for a single row in the expanded event log entry list.
//
// Structure:
//   LogEntryView (.ws-event-log__entry)
//     VisualElement (.ws-event-log__entry-icon + --<category>)
//       Label        (category icon placeholder "●")
//     VisualElement (.ws-event-log__entry-content)
//       VisualElement (flex-row for body + timestamp)
//         Label       (.ws-event-log__entry-body)
//         Label       (.ws-event-log__entry-time)
//       Label         (.ws-event-log__entry-nav, optional)
//
// The navigate shortcut label calls the supplied onNavigate callback when clicked.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Single-row VisualElement for one <see cref="LogEntry"/> in the expanded log list.
    /// </summary>
    internal class LogEntryView : VisualElement
    {
        private readonly Label _bodyLabel;
        private readonly Label _timeLabel;
        private readonly Label _navLabel;
        private readonly VisualElement _iconEl;

        private const string NavigateArrow = "→";

        /// <param name="entry">The data to display.</param>
        /// <param name="currentTick">Current station tick, used for relative timestamp.</param>
        /// <param name="onNavigate">
        /// Called when the navigate shortcut is clicked.
        /// Receives (navigateTargetType, navigateTargetId).
        /// </param>
        public LogEntryView(LogEntry entry, int currentTick,
                            Action<string, string> onNavigate)
        {
            AddToClassList("ws-event-log__entry");

            // ── Icon ──────────────────────────────────────────────────────────
            _iconEl = new VisualElement();
            _iconEl.AddToClassList("ws-event-log__entry-icon");
            _iconEl.AddToClassList(CategoryIconClass(entry.Category));

            var iconPlaceholder = new Label(CategoryIconGlyph(entry.Category));
            iconPlaceholder.style.fontSize = 10;
            iconPlaceholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            _iconEl.Add(iconPlaceholder);

            Add(_iconEl);

            // ── Content column ────────────────────────────────────────────────
            var content = new VisualElement();
            content.AddToClassList("ws-event-log__entry-content");

            // Row: body text + tick timestamp
            var bodyRow = new VisualElement();
            bodyRow.style.flexDirection = FlexDirection.Row;
            bodyRow.style.alignItems = Align.FlexStart;

            _bodyLabel = new Label(entry.BodyText ?? string.Empty);
            _bodyLabel.AddToClassList("ws-event-log__entry-body");
            _bodyLabel.style.flexGrow = 1;
            bodyRow.Add(_bodyLabel);

            _timeLabel = new Label(TickRelativeLabel(entry.TickFired, currentTick));
            _timeLabel.AddToClassList("ws-event-log__entry-time");
            bodyRow.Add(_timeLabel);

            content.Add(bodyRow);

            // Navigate shortcut
            if (!string.IsNullOrEmpty(entry.NavigateTargetType) && onNavigate != null)
            {
                string targetType = entry.NavigateTargetType;
                string targetId   = entry.NavigateTargetId;

                _navLabel = new Label($"{NavigateArrow} {entry.NavigateLabel ?? DefaultNavLabel(targetType)}");
                _navLabel.AddToClassList("ws-event-log__entry-nav");
                _navLabel.RegisterCallback<ClickEvent>(_ =>
                {
                    onNavigate(targetType, targetId);
                });
                content.Add(_navLabel);
            }

            Add(content);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string TickRelativeLabel(int tickFired, int currentTick)
        {
            int delta = currentTick - tickFired;
            if (delta <= 0) return "now";
            return $"t-{delta}";
        }

        private static string CategoryIconClass(LogCategory cat) => cat switch
        {
            LogCategory.Alert   => "ws-event-log__entry-icon--alert",
            LogCategory.Crew    => "ws-event-log__entry-icon--crew",
            LogCategory.Station => "ws-event-log__entry-icon--station",
            LogCategory.World   => "ws-event-log__entry-icon--world",
            _                   => "ws-event-log__entry-icon--world",
        };

        private static string CategoryIconGlyph(LogCategory cat) => cat switch
        {
            LogCategory.Alert   => "!",
            LogCategory.Crew    => "●",
            LogCategory.Station => "■",
            LogCategory.World   => "◎",
            _                   => "●",
        };

        private static string DefaultNavLabel(string targetType) => targetType switch
        {
            "crew"    => "Open crew panel",
            "room"    => "Open room panel",
            "network" => "Open network overlay",
            "visitor" => "Open comms panel",
            "fleet"   => "Open fleet panel",
            _         => "Navigate",
        };
    }
}
