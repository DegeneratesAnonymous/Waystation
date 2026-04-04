using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public enum MirrorAxis { Horizontal, Vertical, Both }

    public class TileTool_MirrorPaint : TileTool
    {
        public override string Name => "MirrorPaint";
        public override string Shortcut => "H";
        public override string Description => "Paint with axis reflection";

        public MirrorAxis Axis { get; set; } = MirrorAxis.Horizontal;

        private PixelCoord _lastCoord;
        private bool _drawing;

        public override void OnPointerDown(PixelCoord coord)
        {
            Controller.BeginStroke();
            _drawing = true;
            _lastCoord = coord;
            PaintMirrored(coord);
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            if (!_drawing) return;
            BresenhamMirrored(_lastCoord, coord);
            _lastCoord = coord;
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_drawing) return;
            _drawing = false;
            Controller.EndStroke();
        }

        private void PaintMirrored(PixelCoord coord)
        {
            var canvas = Controller.Canvas;
            var color = Controller.ActiveColour;

            canvas.SetPixel(coord.x, coord.y, color);

            if (Axis == MirrorAxis.Horizontal || Axis == MirrorAxis.Both)
                canvas.SetPixel(canvas.Width - 1 - coord.x, coord.y, color);

            if (Axis == MirrorAxis.Vertical || Axis == MirrorAxis.Both)
                canvas.SetPixel(coord.x, canvas.Height - 1 - coord.y, color);

            if (Axis == MirrorAxis.Both)
                canvas.SetPixel(canvas.Width - 1 - coord.x, canvas.Height - 1 - coord.y, color);
        }

        private void BresenhamMirrored(PixelCoord from, PixelCoord to)
        {
            int dx = Mathf.Abs(to.x - from.x), sx = from.x < to.x ? 1 : -1;
            int dy = -Mathf.Abs(to.y - from.y), sy = from.y < to.y ? 1 : -1;
            int err = dx + dy;
            int x = from.x, y = from.y;
            while (true)
            {
                PaintMirrored(new PixelCoord(x, y));
                if (x == to.x && y == to.y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }
    }
}
