// StatusPip.cs
// Custom UI Toolkit VisualElement that renders a single LED indicator square
// with on/off/warning/fault colour states.
//
// Usage in UXML:
//   <Waystation.UI.StatusPip state="On" />
//   <Waystation.UI.StatusPip state="Warning" />
//
// Usage in C#:
//   var pip = new StatusPip(StatusPip.State.On);
//   statusBar.Add(pip);

using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// A single 6×6 pixel LED indicator element with colour variants.
    /// </summary>
    [UxmlElement]
    public partial class StatusPip : VisualElement
    {
        // ── Enum ──────────────────────────────────────────────────────────
        public enum State
        {
            Off,
            On,
            Warning,
            Fault,
            Acc,
        }

        // ── Class name constants ──────────────────────────────────────────
        private const string ClassOn      = "ws-status-pip--on";
        private const string ClassWarning = "ws-status-pip--warning";
        private const string ClassOff     = "ws-status-pip--off";
        private const string ClassFault   = "ws-status-pip--fault";
        private const string ClassAcc     = "ws-status-pip--acc";

        // ── Backing field ─────────────────────────────────────────────────
        private State _state = State.Off;

        // ── Property ──────────────────────────────────────────────────────
        public State PipState
        {
            get => _state;
            set
            {
                RemoveFromClassList(ClassOn);
                RemoveFromClassList(ClassWarning);
                RemoveFromClassList(ClassOff);
                RemoveFromClassList(ClassFault);
                RemoveFromClassList(ClassAcc);

                _state = value;

                switch (_state)
                {
                    case State.On:      AddToClassList(ClassOn);      break;
                    case State.Warning: AddToClassList(ClassWarning); break;
                    case State.Fault:   AddToClassList(ClassFault);   break;
                    case State.Acc:     AddToClassList(ClassAcc);     break;
                    default:            AddToClassList(ClassOff);     break;
                }
            }
        }

        // ── Constructor ───────────────────────────────────────────────────
        public StatusPip() : this(State.Off) { }

        public StatusPip(State state = State.Off)
        {
            AddToClassList("ws-status-pip");
            pickingMode = PickingMode.Ignore;
            PipState = state;
        }
    }
}
