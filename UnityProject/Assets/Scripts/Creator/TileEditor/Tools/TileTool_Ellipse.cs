using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Ellipse : TileTool
    {
        public override string Name => "Ellipse";
        public override string Shortcut => "O";
        public override string Description => "Draw an ellipse or circle";

        private PixelCoord _startCoord;
        private bool _drawing;
        public bool FillMode { get; set; } = false;
        public bool ConstrainCircle { get; set; }

        public override void OnPointerDown(PixelCoord coord)
        {
            _startCoord = coord;
            _drawing = true;
            Controller.BeginStroke();
        }

        public override void OnPointerDrag(PixelCoord coord) { }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_drawing) return;
            _drawing = false;
            if (ConstrainCircle)
            {
                int dx = coord.x - _startCoord.x;
                int dy = coord.y - _startCoord.y;
                int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                coord = new PixelCoord(_startCoord.x + size * Math.Sign(dx), _startCoord.y + size * Math.Sign(dy));
            }
            DrawEllipse(_startCoord, coord);
            Controller.EndStroke();
        }

        private void DrawEllipse(PixelCoord a, PixelCoord b)
        {
            int x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x);
            int y0 = Math.Min(a.y, b.y), y1 = Math.Max(a.y, b.y);
            float cx = (x0 + x1) / 2f, cy = (y0 + y1) / 2f;
            float rx = (x1 - x0) / 2f, ry = (y1 - y0) / 2f;
            if (rx < 0.5f || ry < 0.5f) return;

            var color = Controller.ActiveColour;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cx) / rx;
                    float dy = (y - cy) / ry;
                    float dist = dx * dx + dy * dy;
                    if (FillMode)
                    {
                        if (dist <= 1f)
                            Controller.Canvas.SetPixel(x, y, color);
                    }
                    else
                    {
                        if (dist <= 1f && dist >= (1f - 2f / Math.Max(rx, ry)))
                            Controller.Canvas.SetPixel(x, y, color);
                    }
                }
            }
        }
    }
}
