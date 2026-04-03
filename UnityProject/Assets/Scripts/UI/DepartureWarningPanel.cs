using System;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Non-modal HUD panel that warns the player an NPC intends to depart.
    /// Shows a live countdown bar and an Intervene button. Does NOT pause the game.
    /// </summary>
    public class DepartureWarningPanel : VisualElement
    {
        private readonly Label _npcNameLabel;
        private readonly Label _tensionLabel;
        private readonly VisualElement _countdownBarFill;
        private readonly Label _countdownText;
        private readonly Button _interveneBtn;
        private readonly Button _dismissBtn;
        private readonly Label _outcomeLabel;

        public string NpcUid { get; set; }
        public int DeadlineTick { get; set; }
        public int AnnouncedAtTick { get; set; }

        public event Action OnInterveneClicked;
        public event Action OnDismissClicked;

        public DepartureWarningPanel()
        {
            AddToClassList("ws-departure-warning");

            // Header row: NPC name + tension stage
            var headerRow = new VisualElement();
            headerRow.AddToClassList("ws-departure-warning__header");

            _npcNameLabel = new Label();
            _npcNameLabel.AddToClassList("ws-departure-warning__npc-name");
            headerRow.Add(_npcNameLabel);

            _tensionLabel = new Label();
            _tensionLabel.AddToClassList("ws-departure-warning__tension-label");
            headerRow.Add(_tensionLabel);

            Add(headerRow);

            // Countdown bar
            var countdownRow = new VisualElement();
            countdownRow.AddToClassList("ws-departure-warning__countdown-row");

            var barTrack = new VisualElement();
            barTrack.AddToClassList("ws-departure-warning__bar-track");

            _countdownBarFill = new VisualElement();
            _countdownBarFill.AddToClassList("ws-departure-warning__bar-fill");
            barTrack.Add(_countdownBarFill);

            countdownRow.Add(barTrack);

            _countdownText = new Label();
            _countdownText.AddToClassList("ws-departure-warning__countdown-text");
            countdownRow.Add(_countdownText);

            Add(countdownRow);

            // Outcome label (hidden by default)
            _outcomeLabel = new Label();
            _outcomeLabel.AddToClassList("ws-departure-warning__outcome");
            _outcomeLabel.style.display = DisplayStyle.None;
            Add(_outcomeLabel);

            // Button row
            var btnRow = new VisualElement();
            btnRow.AddToClassList("ws-departure-warning__btn-row");

            _interveneBtn = new Button(() => OnInterveneClicked?.Invoke());
            _interveneBtn.AddToClassList("ws-btn");
            _interveneBtn.text = "INTERVENE";
            btnRow.Add(_interveneBtn);

            _dismissBtn = new Button(() => OnDismissClicked?.Invoke());
            _dismissBtn.AddToClassList("ws-btn");
            _dismissBtn.AddToClassList("ws-btn--subtle");
            _dismissBtn.text = "DISMISS";
            btnRow.Add(_dismissBtn);

            Add(btnRow);
        }

        public string NpcName
        {
            get => _npcNameLabel.text;
            set => _npcNameLabel.text = value;
        }

        public void SetTensionStage(TensionStage stage)
        {
            _tensionLabel.text = TensionSystem.GetTensionStageLabel(stage);
            var color = TensionSystem.GetTensionStageColor(stage);
            _tensionLabel.style.color = color;
        }

        /// <summary>
        /// Update the countdown bar and text label based on current tick.
        /// </summary>
        public void UpdateCountdown(int currentTick)
        {
            int totalWindow = DeadlineTick - AnnouncedAtTick;
            int remaining = Mathf.Max(0, DeadlineTick - currentTick);

            float pct = totalWindow > 0 ? (float)remaining / totalWindow : 0f;
            _countdownBarFill.style.width = Length.Percent(pct * 100f);

            // Convert remaining ticks to in-game days/hours for display
            int remainingHours = remaining / TimeSystem.TicksPerHour;
            int days = remainingHours / 24;
            int hours = remainingHours % 24;

            _countdownText.text = days > 0 ? $"{days}d {hours}h" : $"{hours}h";

            // Colour the bar based on remaining percentage
            if (pct <= 0.25f)
                _countdownBarFill.style.backgroundColor = new Color(0.86f, 0.26f, 0.26f);
            else if (pct <= 0.5f)
                _countdownBarFill.style.backgroundColor = new Color(0.88f, 0.68f, 0.10f);
            else
                _countdownBarFill.style.backgroundColor = new Color(0.30f, 0.72f, 0.45f);
        }

        /// <summary>
        /// Show intervention outcome and disable buttons.
        /// </summary>
        public void ShowOutcome(bool success, string message)
        {
            _interveneBtn.SetEnabled(false);
            _dismissBtn.text = "OK";
            _outcomeLabel.text = message;
            _outcomeLabel.style.display = DisplayStyle.Flex;
            _outcomeLabel.style.color = success
                ? new Color(0.30f, 0.85f, 0.45f)
                : new Color(0.86f, 0.26f, 0.26f);
        }
    }
}
