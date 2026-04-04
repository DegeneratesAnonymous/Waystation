using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Rectangle : TileTool
    {
        public override string Name => "Rectangle";
        public override string Shortcut => "R";
        public override string Description => "Draw a rectangle";

        private PixelCoord _startCoord;
        private bool _drawing;
        public bool FillMode { get; set; } = false;
        public int CornerRadius { get; set; } = 0;
        public bool ConstrainSquare { get; set; }

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

            var end = ConstrainSquare ? ConstrainToSquare(_startCoord, coord) : coord;
            DrawRect(_startCoord, end);
            Controller.EndStroke();
        }

        private void DrawRect(PixelCoord a, PixelCoord b)
        {
            int x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x);
            int y0 = Math.Min(a.y, b.y), y1 = Math.Max(a.y, b.y);
            var color = Controller.ActiveColour;

            if (FillMode)
            {
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        Controller.Canvas.SetPixel(x, y, color);
            }
            else
            {
                for (int x = x0; x <= x1; x++)
                {
                    Controller.Canvas.SetPixel(x, y0, color);
                    Controller.Canvas.SetPixel(x, y1, color);
                }
                for (int y = y0; y <= y1; y++)
                {
                    Controller.Canvas.SetPixel(x0, y, color);
                    Controller.Canvas.SetPixel(x1, y, color);
                }
            }
        }

        private static PixelCoord ConstrainToSquare(PixelCoord start, PixelCoord end)
        {
            int dx = end.x - start.x;
            int dy = end.y - start.y;
            int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new PixelCoord(
                start.x + size * Math.Sign(dx),
                start.y + size * Math.Sign(dy));
        }
    }
}
