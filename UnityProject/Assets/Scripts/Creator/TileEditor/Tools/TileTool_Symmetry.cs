using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Symmetry : TileTool
    {
        public override string Name => "Symmetry";
        public override string Shortcut => "";
        public override string Description => "Paint with rotational symmetry";

        public int Folds { get; set; } = 2; // 2 or 4

        private PixelCoord _lastCoord;
        private bool _drawing;

        public override void OnPointerDown(PixelCoord coord)
        {
            Controller.BeginStroke();
            _drawing = true;
            _lastCoord = coord;
            PaintSymmetric(coord);
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            if (!_drawing) return;
            BresenhamSymmetric(_lastCoord, coord);
            _lastCoord = coord;
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_drawing) return;
            _drawing = false;
            Controller.EndStroke();
        }

        private void PaintSymmetric(PixelCoord coord)
        {
            var canvas = Controller.Canvas;
            var color = Controller.ActiveColour;
            float cx = (canvas.Width - 1) / 2f;
            float cy = (canvas.Height - 1) / 2f;

            canvas.SetPixel(coord.x, coord.y, color);

            // 180 degree rotation
            int rx = Mathf.RoundToInt(2 * cx - coord.x);
            int ry = Mathf.RoundToInt(2 * cy - coord.y);
            canvas.SetPixel(rx, ry, color);

            if (Folds == 4)
            {
                // 90 degree rotations
                float dx = coord.x - cx;
                float dy = coord.y - cy;
                // 90 CW: (dy, -dx)
                canvas.SetPixel(Mathf.RoundToInt(cx + dy), Mathf.RoundToInt(cy - dx), color);
                // 270 CW: (-dy, dx)
                canvas.SetPixel(Mathf.RoundToInt(cx - dy), Mathf.RoundToInt(cy + dx), color);
            }
        }

        private void BresenhamSymmetric(PixelCoord from, PixelCoord to)
        {
            int dx = Mathf.Abs(to.x - from.x), sx = from.x < to.x ? 1 : -1;
            int dy = -Mathf.Abs(to.y - from.y), sy = from.y < to.y ? 1 : -1;
            int err = dx + dy;
            int x = from.x, y = from.y;
            while (true)
            {
                PaintSymmetric(new PixelCoord(x, y));
                if (x == to.x && y == to.y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }
    }
}
