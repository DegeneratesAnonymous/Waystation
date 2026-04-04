using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Line : TileTool
    {
        public override string Name => "Line";
        public override string Shortcut => "L";
        public override string Description => "Draw a straight line";

        private PixelCoord _startCoord;
        private bool _drawing;
        public int Thickness { get; set; } = 1;
        public bool ConstrainAngle { get; set; }

        public override void OnPointerDown(PixelCoord coord)
        {
            _startCoord = coord;
            _drawing = true;
            Controller.BeginStroke();
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            // Preview handled by UI overlay
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_drawing) return;
            _drawing = false;

            var end = ConstrainAngle ? ConstrainTo45(_startCoord, coord) : coord;
            BresenhamLine(_startCoord.x, _startCoord.y, end.x, end.y);
            Controller.EndStroke();
        }

        private void BresenhamLine(int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Controller.Canvas.SetPixel(x0, y0, Controller.ActiveColour);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public static PixelCoord ConstrainTo45(PixelCoord start, PixelCoord end)
        {
            int dx = end.x - start.x;
            int dy = end.y - start.y;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(angle / 45f) * 45f;
            float rad = snapped * Mathf.Deg2Rad;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            return new PixelCoord(
                start.x + Mathf.RoundToInt(Mathf.Cos(rad) * length),
                start.y + Mathf.RoundToInt(Mathf.Sin(rad) * length));
        }
    }
}
