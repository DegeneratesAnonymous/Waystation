using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_MagicWand : TileTool
    {
        public override string Name => "MagicWand";
        public override string Shortcut => "W";
        public override string Description => "Select pixels by colour";

        public int Tolerance { get; set; } = 0;
        public bool Contiguous { get; set; } = true;
        public bool HasSelection { get; private set; }
        public HashSet<int> SelectedPixels { get; } = new HashSet<int>();

        public override void Deactivate()
        {
            ClearSelection();
        }

        public override void OnPointerDown(PixelCoord coord)
        {
            ClearSelection();
            if (!coord.IsValid(Controller.Canvas.Width, Controller.Canvas.Height)) return;

            var canvas = Controller.Canvas;
            var targetColor = canvas.GetPixel(coord.x, coord.y);

            if (Contiguous)
                FloodSelect(canvas, coord.x, coord.y, targetColor);
            else
                GlobalSelect(canvas, targetColor);

            HasSelection = SelectedPixels.Count > 0;
        }

        public override void OnPointerDrag(PixelCoord coord) { }
        public override void OnPointerUp(PixelCoord coord) { }

        private void FloodSelect(PixelCanvas2D canvas, int startX, int startY, Color32 target)
        {
            int w = canvas.Width, h = canvas.Height;
            var visited = new bool[w * h];
            var stack = new Stack<int>();
            stack.Push(startY * w + startX);

            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                if (visited[idx]) continue;
                visited[idx] = true;

                int x = idx % w, y = idx / w;
                if (!ColorsMatch(canvas.GetPixel(x, y), target)) continue;

                SelectedPixels.Add(idx);

                if (x > 0) stack.Push(y * w + (x - 1));
                if (x < w - 1) stack.Push(y * w + (x + 1));
                if (y > 0) stack.Push((y - 1) * w + x);
                if (y < h - 1) stack.Push((y + 1) * w + x);
            }
        }

        private void GlobalSelect(PixelCanvas2D canvas, Color32 target)
        {
            int total = canvas.Width * canvas.Height;
            for (int i = 0; i < total; i++)
            {
                int x = i % canvas.Width, y = i / canvas.Width;
                if (ColorsMatch(canvas.GetPixel(x, y), target))
                    SelectedPixels.Add(i);
            }
        }

        private bool ColorsMatch(Color32 a, Color32 b)
        {
            if (Tolerance == 0)
                return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            int diff = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
            return diff <= Tolerance * 4 * 255 / 100;
        }

        public void ClearSelection()
        {
            HasSelection = false;
            SelectedPixels.Clear();
        }
    }
}
