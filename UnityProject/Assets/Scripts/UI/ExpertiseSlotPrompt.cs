// ExpertiseSlotPrompt.cs
// Custom UI Toolkit VisualElement — specialised modal for the expertise slot
// unlock choice UI (referenced in WO-NPC-004).
//
// When the player unlocks an expertise slot, this prompt presents a list of
// available expertise options. The player selects one and confirms.
//
// Usage in C#:
//   var prompt = new ExpertiseSlotPrompt();
//   prompt.AddExpertiseOption("opt-1", "COMBAT", "Melee and ranged combat.");
//   prompt.AddExpertiseOption("opt-2", "ENGINEERING", "Construction and repair.");
//   prompt.OnConfirmed += optionId => ApplyExpertise(optionId);
//   root.Add(prompt);
//   prompt.Show();

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Modal prompt for expertise slot unlock choice. Extends ModalOverlay with
    /// expertise-specific list UI.
    /// </summary>
    public class ExpertiseSlotPrompt : ModalOverlay
    {
        // ── Expertise option record ───────────────────────────────────────
        private readonly struct ExpertiseOption
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string Description;
            public readonly VisualElement Element;

            public ExpertiseOption(string id, string name, string desc, VisualElement el)
            {
                Id = id; Name = name; Description = desc; Element = el;
            }
        }

        // ── Fields ────────────────────────────────────────────────────────
        private readonly VisualElement _slotList;
        private readonly List<ExpertiseOption> _options = new List<ExpertiseOption>();
        private string _selectedId;
        private Button _confirmButton;
        private Button _cancelButton;

        private readonly Label _npcPortrait;
        private readonly Label _npcNameLabel;
        private readonly Label _skillInfoLabel;
        private readonly Label _queueInfoLabel;

        private bool _mandatoryMode;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>
        /// Fired when the player confirms a selection. Argument is the option id.
        /// </summary>
        public event Action<string> OnConfirmed;

        // ── Constructor ───────────────────────────────────────────────────
        public ExpertiseSlotPrompt()
        {
            Title = "CHOOSE EXPERTISE";

            // NPC header row
            var npcRow = new VisualElement();
            npcRow.AddToClassList("ws-expertise-slot-prompt__npc-row");

            _npcPortrait = new Label("?");
            _npcPortrait.AddToClassList("ws-expertise-slot-prompt__npc-portrait");

            _npcNameLabel = new Label();
            _npcNameLabel.AddToClassList("ws-expertise-slot-prompt__npc-name");

            npcRow.Add(_npcPortrait);
            npcRow.Add(_npcNameLabel);
            BodyContent.Add(npcRow);

            // Skill info
            _skillInfoLabel = new Label();
            _skillInfoLabel.AddToClassList("ws-expertise-slot-prompt__skill-info");
            BodyContent.Add(_skillInfoLabel);

            // Queue depth
            _queueInfoLabel = new Label();
            _queueInfoLabel.AddToClassList("ws-expertise-slot-prompt__queue-info");
            _queueInfoLabel.style.display = DisplayStyle.None;
            BodyContent.Add(_queueInfoLabel);

            // Slot list in the body
            _slotList = new VisualElement();
            _slotList.AddToClassList("ws-expertise-slot-prompt__slot-list");
            BodyContent.Add(_slotList);

            // Cost label
            var costRow = new VisualElement();
            costRow.style.flexDirection = FlexDirection.Row;
            costRow.style.justifyContent = Justify.FlexEnd;

            var costLabel = new Label("1 SLOT POINT");
            costLabel.AddToClassList("ws-expertise-slot-prompt__cost");
            costRow.Add(costLabel);
            BodyContent.Add(costRow);

            // Footer buttons
            _confirmButton = AddFooterButton("CONFIRM", OnConfirmClicked, isPrimary: true);
            _confirmButton.SetEnabled(false);
            _cancelButton = AddFooterButton("CANCEL", Hide);
        }

        // ── Properties ─────────────────────────────────────────────────────

        /// <summary>Sets the NPC name and updates the initials circle.</summary>
        public string NpcName
        {
            set
            {
                _npcNameLabel.text = value;
                _npcPortrait.text = GetInitials(value);
            }
        }

        /// <summary>Sets the skill info text (e.g. "Farming reached level 8").</summary>
        public string SkillInfo
        {
            set => _skillInfoLabel.text = value;
        }

        /// <summary>Sets the queue depth text. Null or empty hides the label.</summary>
        public string QueueInfo
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _queueInfoLabel.style.display = DisplayStyle.None;
                }
                else
                {
                    _queueInfoLabel.text = value;
                    _queueInfoLabel.style.display = DisplayStyle.Flex;
                }
            }
        }

        /// <summary>
        /// When true, the player cannot dismiss the prompt without choosing.
        /// Hides the cancel button and disables backdrop/close-button dismissal.
        /// </summary>
        public bool MandatoryMode
        {
            get => _mandatoryMode;
            set
            {
                _mandatoryMode = value;
                BackdropCloseEnabled = !value;
                CloseButtonVisible = !value;
                _cancelButton.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Adds an expertise option to the prompt list.
        /// </summary>
        public void AddExpertiseOption(string id, string name, string description)
        {
            var slot = new VisualElement();
            slot.AddToClassList("ws-expertise-slot-prompt__slot");

            var icon = new VisualElement();
            icon.AddToClassList("ws-expertise-slot-prompt__slot-icon");

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("ws-expertise-slot-prompt__slot-name");

            var descLabel = new Label(description);
            descLabel.AddToClassList("ws-expertise-slot-prompt__slot-desc");

            slot.Add(icon);
            slot.Add(nameLabel);
            slot.Add(descLabel);

            var capturedId = id;
            slot.RegisterCallback<ClickEvent>(_ => SelectOption(capturedId));

            _slotList.Add(slot);
            _options.Add(new ExpertiseOption(id, name, description, slot));
        }

        /// <summary>Removes all options and resets selection.</summary>
        public void ClearOptions()
        {
            _options.Clear();
            _slotList.Clear();
            _selectedId = null;
            _confirmButton?.SetEnabled(false);
        }

        // ── Private ───────────────────────────────────────────────────────
        private void SelectOption(string id)
        {
            _selectedId = id;
            foreach (var opt in _options)
            {
                opt.Element.EnableInClassList(
                    "ws-expertise-slot-prompt__slot--selected",
                    opt.Id == id);
            }
            _confirmButton?.SetEnabled(true);
        }

        private void OnConfirmClicked()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            OnConfirmed?.Invoke(_selectedId);
            Hide();
        }

        /// <summary>
        /// Returns the initials for a given name (first letter of first and last
        /// parts). Returns "?" for null/empty input.
        /// </summary>
        public static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][0].ToString().ToUpperInvariant();

            return (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpperInvariant();
        }
    }
}
