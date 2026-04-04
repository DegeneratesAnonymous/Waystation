using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Creator.TileEditor;
using Waystation.Creator.TileEditor.Palette;

namespace Waystation.Creator.UI
{
    public class PalettePanel
    {
        private readonly VisualElement _root;
        private readonly TileEditorController _editor;
        private readonly ColourPalette _palette;
        private readonly DevMode.DevModeOverlay _devOverlay;

        private VisualElement _swatchGrid;
        private VisualElement _recentRow;
        private VisualElement _fgSwatch;
        private VisualElement _bgSwatch;

        public PalettePanel(VisualElement root, TileEditorController editor,
            ColourPalette palette, DevMode.DevModeOverlay devOverlay = null)
        {
            _root = root;
            _editor = editor;
            _palette = palette;
            _devOverlay = devOverlay;
            Bind();
            Refresh();

            _palette.OnPaletteChanged += RefreshRecents;
        }

        private void Bind()
        {
            _swatchGrid = _root.Q("swatch-grid");
            _recentRow = _root.Q("recent-colours");
            _fgSwatch = _root.Q("fg-swatch");
            _bgSwatch = _root.Q("bg-swatch");
        }

        public void Refresh()
        {
            BuildSwatchGrid();
            RefreshRecents();
            UpdateActiveColourDisplay();
        }

        private void BuildSwatchGrid()
        {
            if (_swatchGrid == null) return;
            _swatchGrid.Clear();

            for (int i = 0; i < ColourPalette.SharedSwatches.Length; i++)
            {
                int idx = i;
                var swatch = ColourPalette.SharedSwatches[i];
                var btn = new Button(() => SelectColour(swatch.colour));
                btn.AddToClassList("swatch-btn");
                btn.style.backgroundColor = new StyleColor(swatch.colour);
                btn.tooltip = swatch.label;

                // Dev mode: show key label
                if (_devOverlay != null && _devOverlay.ShowPaletteKeys)
                {
                    var keyLabel = new Label(swatch.key);
                    keyLabel.AddToClassList("swatch-key-label");
                    btn.Add(keyLabel);
                }

                _swatchGrid.Add(btn);
            }
        }

        private void RefreshRecents()
        {
            if (_recentRow == null) return;
            _recentRow.Clear();

            foreach (var colour in _palette.RecentColours)
            {
                var c = colour;
                var btn = new Button(() => SelectColour(c));
                btn.AddToClassList("swatch-btn");
                btn.AddToClassList("swatch-btn--recent");
                btn.style.backgroundColor = new StyleColor(c);
                _recentRow.Add(btn);
            }
        }

        private void SelectColour(Color32 colour)
        {
            _editor.ActiveColour = colour;
            _palette.UseColour(colour);
            UpdateActiveColourDisplay();
        }

        private void UpdateActiveColourDisplay()
        {
            if (_fgSwatch != null)
                _fgSwatch.style.backgroundColor = new StyleColor(_editor.ActiveColour);
            if (_bgSwatch != null)
                _bgSwatch.style.backgroundColor = new StyleColor(_editor.BackgroundColour);
        }
    }
}
