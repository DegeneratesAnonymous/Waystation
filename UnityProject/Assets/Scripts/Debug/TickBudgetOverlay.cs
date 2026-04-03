// TickBudgetOverlay — debug GUI overlay for TickScheduler channel budget usage.
//
// Draws a small panel showing per-channel budget bars and system counts.
// Enable via FeatureFlags.UseTickScheduler and the in-game debug toggle.
using UnityEngine;
using Waystation.Core;

namespace Waystation.Debug
{
    public class TickBudgetOverlay
    {
        private bool _visible;
        private TickScheduler _scheduler;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public void SetScheduler(TickScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        /// <summary>
        /// Call from OnGUI to draw the overlay.
        /// </summary>
        public void DrawOverlay()
        {
            if (!_visible || _scheduler == null) return;

            var snapshot = _scheduler.GetBudgetSnapshot();
            if (snapshot == null || snapshot.Length == 0) return;

            float panelWidth = 300f;
            float lineHeight = 22f;
            float headerHeight = 26f;
            float panelHeight = headerHeight + lineHeight * snapshot.Length + 8f;
            float x = Screen.width - panelWidth - 10f;
            float y = 10f;

            // Background
            GUI.Box(new Rect(x, y, panelWidth, panelHeight), "");

            // Header
            GUI.Label(new Rect(x + 8, y + 4, panelWidth - 16, headerHeight),
                "<b>Tick Budget</b>");

            // Channel rows
            for (int i = 0; i < snapshot.Length; i++)
            {
                var ch = snapshot[i];
                float rowY = y + headerHeight + i * lineHeight;
                float barWidth = panelWidth - 120f;
                float barX = x + 110f;

                // Channel label
                string label = $"{ch.Name} [{ch.SystemsScheduled}s/{ch.SystemsDeferred}d]";
                GUI.Label(new Rect(x + 8, rowY, 100f, lineHeight), label);

                // Budget bar background
                GUI.Box(new Rect(barX, rowY + 2, barWidth, lineHeight - 4), "");

                // Budget bar fill
                float fill = Mathf.Clamp01(ch.UsagePercent);
                Color barColor = fill < 0.7f ? Color.green :
                                 fill < 0.9f ? Color.yellow : Color.red;

                var oldColor = GUI.color;
                GUI.color = barColor;
                GUI.Box(new Rect(barX, rowY + 2, barWidth * fill, lineHeight - 4), "");
                GUI.color = oldColor;

                // Percentage text
                string pctText = $"{ch.BudgetUsedMs:F1}/{ch.BudgetAllocatedMs:F1}ms";
                GUI.Label(new Rect(barX + 4, rowY, barWidth - 8, lineHeight), pctText);
            }
        }
    }
}
