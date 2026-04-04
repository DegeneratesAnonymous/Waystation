using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Pencil : TileTool
    {
        public override string Name => "Pencil";
        public override string Shortcut => "P";
        public override string Description => "Draw individual pixels";

        private PixelCoord _lastCoord;
        private bool _drawing;
        public int BrushSize { get; set; } = 1;

        public override void OnPointerDown(PixelCoord coord)
        {
            Controller.BeginStroke();
            _drawing = true;
            _lastCoord = coord;
            PaintAt(coord);
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            if (!_drawing) return;
            BresenhamLine(_lastCoord.x, _lastCoord.y, coord.x, coord.y);
            _lastCoord = coord;
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_drawing) return;
            _drawing = false;
            Controller.EndStroke();
        }

        private void PaintAt(PixelCoord coord)
        {
            int half = BrushSize / 2;
            int offset = (BrushSize % 2 == 0) ? half - 1 : half;
            for (int dy = -offset; dy < BrushSize - offset; dy++)
                for (int dx = -offset; dx < BrushSize - offset; dx++)
                    Controller.Canvas.SetPixel(coord.x + dx, coord.y + dy, Controller.ActiveColour);
        }

        private void BresenhamLine(int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                PaintAt(new PixelCoord(x0, y0));
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
