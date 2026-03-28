// DataChipIndicator.cs
// Custom UI Toolkit VisualElement that renders a visual representation of
// a Datachip slot with filled/empty/locked states.
//
// Usage in UXML:
//   <Waystation.UI.DataChipIndicator state="Empty" />
//   <Waystation.UI.DataChipIndicator state="Filled" />
//   <Waystation.UI.DataChipIndicator state="Locked" />
//
// Usage in C#:
//   var chip = new DataChipIndicator(DataChipIndicator.ChipState.Empty);
//   chip.ChipState = DataChipIndicator.ChipState.Filled;
//   panel.Add(chip);

using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Visual indicator for a single Datachip slot.
    /// </summary>
    public class DataChipIndicator : VisualElement
    {
        // ── State enum ────────────────────────────────────────────────────
        public enum ChipState { Empty, Filled, Locked }

        private const string ClassFilled = "ws-datachip-indicator--filled";
        private const string ClassLocked = "ws-datachip-indicator--locked";

        // ── UXML factory ──────────────────────────────────────────────────
        public new class UxmlFactory : UxmlFactory<DataChipIndicator, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlEnumAttributeDescription<ChipState> _state =
                new UxmlEnumAttributeDescription<ChipState>
                {
                    name = "state",
                    defaultValue = ChipState.Empty,
                };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var chip = (DataChipIndicator)ve;
                chip.State = _state.GetValueFromBag(bag, cc);
            }
        }

        // ── Child element ─────────────────────────────────────────────────
        private readonly VisualElement _pip;

        // ── Backing field ─────────────────────────────────────────────────
        private ChipState _state = ChipState.Empty;

        // ── Property ──────────────────────────────────────────────────────
        public ChipState State
        {
            get => _state;
            set
            {
                RemoveFromClassList(ClassFilled);
                RemoveFromClassList(ClassLocked);
                _state = value;
                switch (_state)
                {
                    case ChipState.Filled: AddToClassList(ClassFilled); break;
                    case ChipState.Locked: AddToClassList(ClassLocked); break;
                }
            }
        }

        // ── Constructor ───────────────────────────────────────────────────
        public DataChipIndicator(ChipState state = ChipState.Empty)
        {
            AddToClassList("ws-datachip-indicator");

            _pip = new VisualElement();
            _pip.AddToClassList("ws-datachip-indicator__pip");
            _pip.pickingMode = PickingMode.Ignore;
            Add(_pip);

            State = state;
        }
    }
}
