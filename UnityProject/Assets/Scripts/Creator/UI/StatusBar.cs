using UnityEngine.UIElements;
using Waystation.Creator.TileEditor;

namespace Waystation.Creator.UI
{
    public class StatusBar
    {
        private readonly VisualElement _root;
        private readonly TileEditorController _editor;

        private Label _coordLabel;
        private Label _zoomLabel;
        private Label _toolLabel;
        private Label _sizeLabel;
        private Label _variantLabel;

        public StatusBar(VisualElement root, TileEditorController editor)
        {
            _root = root;
            _editor = editor;
            Bind();
        }

        private void Bind()
        {
            _coordLabel = _root.Q<Label>("status-coord");
            _zoomLabel = _root.Q<Label>("status-zoom");
            _toolLabel = _root.Q<Label>("status-tool");
            _sizeLabel = _root.Q<Label>("status-size");
            _variantLabel = _root.Q<Label>("status-variant");
        }

        public void UpdateCoord(PixelCoord coord)
        {
            if (_coordLabel != null)
                _coordLabel.text = coord.IsValid(_editor.Canvas.Width, _editor.Canvas.Height)
                    ? $"X:{coord.x} Y:{coord.y}" : "";
        }

        public void UpdateZoom()
        {
            if (_zoomLabel != null)
                _zoomLabel.text = $"{_editor.Canvas.Zoom:F0}x";
        }

        public void UpdateTool()
        {
            if (_toolLabel != null)
                _toolLabel.text = _editor.ActiveTool?.Name ?? "";
        }

        public void UpdateSize()
        {
            if (_sizeLabel != null)
                _sizeLabel.text = $"{_editor.Canvas.Width}×{_editor.Canvas.Height}";
        }

        public void UpdateVariant()
        {
            if (_variantLabel != null)
                _variantLabel.text = $"Variant {_editor.ActiveVariantIndex}";
        }
    }
}
