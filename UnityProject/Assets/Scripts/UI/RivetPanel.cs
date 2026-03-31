// RivetPanel.cs
// Custom UI Toolkit VisualElement that renders a panel with four corner
// rivet decorations applied automatically. Uses the .ws-rivet-panel class
// from WaystationComponents.uss for visual styling.
//
// Usage in UXML:
//   <Waystation.UI.RivetPanel>
//     <!-- panel content here -->
//   </Waystation.UI.RivetPanel>
//
// Usage in C#:
//   var panel = new RivetPanel();
//   root.Add(panel);

using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// A VisualElement panel that automatically injects four corner rivet
    /// decorations as child elements, styled via WaystationComponents.uss.
    /// </summary>
    [UxmlElement]
    public partial class RivetPanel : VisualElement
    {
        // ── Rivet child elements ──────────────────────────────────────────
        private readonly VisualElement _rivetTL;
        private readonly VisualElement _rivetTR;
        private readonly VisualElement _rivetBL;
        private readonly VisualElement _rivetBR;

        // Optional bevel highlights
        private VisualElement _bevelHi;
        private VisualElement _bevelLo;

        private bool _showBevel;

        /// <summary>
        /// When true, adds top and bottom bevel highlight strips.
        /// </summary>
        public bool ShowBevel
        {
            get => _showBevel;
            set
            {
                _showBevel = value;
                UpdateBevel();
            }
        }

        // ── Constructor ───────────────────────────────────────────────────
        public RivetPanel()
        {
            AddToClassList("ws-rivet-panel");

            _rivetTL = MakeRivet("ws-rivet-tl");
            _rivetTR = MakeRivet("ws-rivet-tr");
            _rivetBL = MakeRivet("ws-rivet-bl");
            _rivetBR = MakeRivet("ws-rivet-br");

            // Rivets are added first so content is layered above them
            Add(_rivetTL);
            Add(_rivetTR);
            Add(_rivetBL);
            Add(_rivetBR);
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static VisualElement MakeRivet(string positionClass)
        {
            var r = new VisualElement();
            r.AddToClassList("ws-rivet");
            r.AddToClassList(positionClass);
            r.pickingMode = PickingMode.Ignore;
            return r;
        }

        private void UpdateBevel()
        {
            if (_showBevel)
            {
                if (_bevelHi == null)
                {
                    _bevelHi = new VisualElement();
                    _bevelHi.AddToClassList("ws-bevel-hi");
                    _bevelHi.pickingMode = PickingMode.Ignore;
                    Add(_bevelHi);
                }
                if (_bevelLo == null)
                {
                    _bevelLo = new VisualElement();
                    _bevelLo.AddToClassList("ws-bevel-lo");
                    _bevelLo.pickingMode = PickingMode.Ignore;
                    Add(_bevelLo);
                }
            }
            else
            {
                _bevelHi?.RemoveFromHierarchy();
                _bevelLo?.RemoveFromHierarchy();
                _bevelHi = null;
                _bevelLo = null;
            }
        }
    }
}
