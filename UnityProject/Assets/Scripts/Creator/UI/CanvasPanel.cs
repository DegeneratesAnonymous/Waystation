using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Creator.TileEditor;

namespace Waystation.Creator.UI
{
    public class CanvasPanel
    {
        private readonly VisualElement _root;
        private readonly TileEditorController _editor;
        private readonly VisualElement _canvasImage;
        private bool _isPanning;
        private Vector2 _panStart;

        public CanvasPanel(VisualElement root, TileEditorController editor)
        {
            _root = root;
            _editor = editor;
            _canvasImage = root.Q("canvas-image");
            Bind();
        }

        private void Bind()
        {
            if (_canvasImage == null) return;

            _canvasImage.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _canvasImage.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _canvasImage.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _canvasImage.RegisterCallback<WheelEvent>(OnWheel);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 1) // Middle/right for pan
            {
                _isPanning = true;
                _panStart = evt.localPosition;
                _canvasImage.CapturePointer(evt.pointerId);
                return;
            }

            if (evt.button == 0 && _editor.ActiveTool != null)
            {
                var coord = _editor.Canvas.ViewportToPixel(evt.localPosition);
                _editor.ActiveTool.OnPointerDown(coord);
                _canvasImage.CapturePointer(evt.pointerId);
                UpdateCanvasDisplay();
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_isPanning)
            {
                Vector2 delta = (Vector2)evt.localPosition - _panStart;
                _editor.Canvas.PanOffset += delta;
                _panStart = evt.localPosition;
                UpdateCanvasDisplay();
                return;
            }

            if (_canvasImage.HasPointerCapture(evt.pointerId) && _editor.ActiveTool != null)
            {
                var coord = _editor.Canvas.ViewportToPixel(evt.localPosition);
                _editor.ActiveTool.OnPointerDrag(coord);
                UpdateCanvasDisplay();
            }
            else if (_editor.ActiveTool != null)
            {
                var coord = _editor.Canvas.ViewportToPixel(evt.localPosition);
                _editor.ActiveTool.OnPointerHover(coord);
            }

            UpdateStatusBar(evt.localPosition);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_isPanning && evt.button == 1)
            {
                _isPanning = false;
                _canvasImage.ReleasePointer(evt.pointerId);
                return;
            }

            if (evt.button == 0 && _editor.ActiveTool != null)
            {
                var coord = _editor.Canvas.ViewportToPixel(evt.localPosition);
                _editor.ActiveTool.OnPointerUp(coord);
                _canvasImage.ReleasePointer(evt.pointerId);
                UpdateCanvasDisplay();
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            float delta = -evt.delta.y * 2f;
            _editor.Canvas.ZoomAt(delta, evt.localMousePosition);
            UpdateCanvasDisplay();
            evt.StopPropagation();
        }

        private void UpdateCanvasDisplay()
        {
            if (_editor.Canvas.IsDirty)
                _editor.Canvas.ApplyChanges();

            if (_canvasImage != null)
                _canvasImage.style.backgroundImage = new StyleBackground(_editor.Canvas.Texture);
        }

        private void UpdateStatusBar(Vector2 viewportPos)
        {
            var coord = _editor.Canvas.ViewportToPixel(viewportPos);
            var statusLabel = _root.Q<Label>("status-coord");
            if (statusLabel != null)
                statusLabel.text = coord.IsValid(_editor.Canvas.Width, _editor.Canvas.Height)
                    ? $"X:{coord.x} Y:{coord.y}"
                    : "";

            var zoomLabel = _root.Q<Label>("status-zoom");
            if (zoomLabel != null)
                zoomLabel.text = $"{_editor.Canvas.Zoom:F0}x";
        }
    }
}
