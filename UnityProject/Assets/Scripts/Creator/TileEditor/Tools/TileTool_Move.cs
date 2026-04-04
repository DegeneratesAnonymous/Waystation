using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Move : TileTool
    {
        public override string Name => "Move";
        public override string Shortcut => "M";
        public override string Description => "Move selection or canvas content";

        private PixelCoord _dragStart;
        private bool _dragging;
        private Color32[] _moveBuffer;
        private int _moveW, _moveH, _moveX, _moveY;

        public override void OnPointerDown(PixelCoord coord)
        {
            _dragStart = coord;
            _dragging = true;

            if (_moveBuffer == null)
            {
                // Capture entire canvas content for move
                Controller.BeginStroke();
                var canvas = Controller.Canvas;
                _moveW = canvas.Width;
                _moveH = canvas.Height;
                _moveX = 0;
                _moveY = 0;
                _moveBuffer = canvas.ClonePixels();
            }
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            if (!_dragging || _moveBuffer == null) return;

            int dx = coord.x - _dragStart.x;
            int dy = coord.y - _dragStart.y;
            _moveX += dx;
            _moveY += dy;
            _dragStart = coord;

            // Redraw canvas with offset
            var canvas = Controller.Canvas;
            var transparent = new Color32(0, 0, 0, 0);
            for (int y = 0; y < canvas.Height; y++)
                for (int x = 0; x < canvas.Width; x++)
                    canvas.SetPixel(x, y, transparent);

            for (int y = 0; y < _moveH; y++)
                for (int x = 0; x < _moveW; x++)
                {
                    var px = _moveBuffer[y * _moveW + x];
                    if (px.a > 0)
                        canvas.SetPixel(x + _moveX, y + _moveY, px);
                }
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_moveBuffer != null)
            {
                _moveBuffer = null;
                Controller.EndStroke();
            }
        }

        public override void Deactivate()
        {
            if (_moveBuffer != null)
            {
                Controller.EndStroke();
                _moveBuffer = null;
            }
        }

        public void Nudge(int dx, int dy)
        {
            if (_moveBuffer == null)
            {
                Controller.BeginStroke();
                _moveBuffer = Controller.Canvas.ClonePixels();
                _moveW = Controller.Canvas.Width;
                _moveH = Controller.Canvas.Height;
                _moveX = 0;
                _moveY = 0;
            }

            _moveX += dx;
            _moveY += dy;

            var canvas = Controller.Canvas;
            var transparent = new Color32(0, 0, 0, 0);
            for (int y = 0; y < canvas.Height; y++)
                for (int x = 0; x < canvas.Width; x++)
                    canvas.SetPixel(x, y, transparent);

            for (int y = 0; y < _moveH; y++)
                for (int x = 0; x < _moveW; x++)
                {
                    var px = _moveBuffer[y * _moveW + x];
                    if (px.a > 0)
                        canvas.SetPixel(x + _moveX, y + _moveY, px);
                }

            Controller.EndStroke();
            _moveBuffer = null;
        }
    }
}
