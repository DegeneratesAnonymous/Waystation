using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Fill : TileTool
    {
        public override string Name => "Fill";
        public override string Shortcut => "G";
        public override string Description => "Flood-fill contiguous area";

        public int Tolerance { get; set; } = 0;

        public override void OnPointerDown(PixelCoord coord)
        {
            if (!coord.IsValid(Controller.Canvas.Width, Controller.Canvas.Height)) return;
            var canvas = Controller.Canvas;
            var targetColor = canvas.GetPixel(coord.x, coord.y);
            var fillColor = Controller.ActiveColour;
            if (ColorsMatch(targetColor, fillColor, 0)) return;

            Controller.BeginStroke();
            FloodFill(canvas, coord.x, coord.y, targetColor, fillColor);
            Controller.EndStroke();
        }

        public override void OnPointerDrag(PixelCoord coord) { }
        public override void OnPointerUp(PixelCoord coord) { }

        private void FloodFill(PixelCanvas2D canvas, int startX, int startY, Color32 target, Color32 fill)
        {
            int w = canvas.Width, h = canvas.Height;
            var visited = new bool[w * h];
            var stack = new Stack<int>();
            stack.Push(startY * w + startX);

            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                if (visited[idx]) continue;
                int x = idx % w, y = idx / w;
                if (!ColorsMatch(canvas.GetPixel(x, y), target, Tolerance)) continue;

                visited[idx] = true;
                canvas.SetPixel(x, y, fill);

                if (x > 0) stack.Push(y * w + (x - 1));
                if (x < w - 1) stack.Push(y * w + (x + 1));
                if (y > 0) stack.Push((y - 1) * w + x);
                if (y < h - 1) stack.Push((y + 1) * w + x);
            }
        }

        private static bool ColorsMatch(Color32 a, Color32 b, int tolerance)
        {
            if (tolerance == 0)
                return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            int diff = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
            return diff <= tolerance * 4 * 255 / 100;
        }
    }
}
