using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Lasso : TileTool
    {
        public override string Name => "Lasso";
        public override string Shortcut => "";
        public override string Description => "Freeform selection";

        private readonly List<PixelCoord> _path = new List<PixelCoord>();
        private bool _drawing;
        public bool HasSelection { get; private set; }
        public HashSet<int> SelectedPixels { get; } = new HashSet<int>();

        public override void Deactivate()
        {
            ClearSelection();
        }

        public override void OnPointerDown(PixelCoord coord)
        {
            ClearSelection();
            _path.Clear();
            _path.Add(coord);
            _drawing = true;
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            if (!_drawing) return;
            if (_path.Count == 0 || _path[_path.Count - 1].x != coord.x || _path[_path.Count - 1].y != coord.y)
                _path.Add(coord);
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_drawing) return;
            _drawing = false;
            if (_path.Count < 3) return;

            // Close path and determine interior pixels via scanline fill
            FillSelection();
            HasSelection = SelectedPixels.Count > 0;
        }

        private void FillSelection()
        {
            SelectedPixels.Clear();
            int w = Controller.Canvas.Width, h = Controller.Canvas.Height;

            // Determine bounding box
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var p in _path)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            // Point-in-polygon via ray casting for each pixel in bounding box
            for (int y = Math.Max(0, minY); y <= Math.Min(h - 1, maxY); y++)
            {
                for (int x = Math.Max(0, minX); x <= Math.Min(w - 1, maxX); x++)
                {
                    if (PointInPolygon(x, y))
                        SelectedPixels.Add(y * w + x);
                }
            }
        }

        private bool PointInPolygon(int px, int py)
        {
            bool inside = false;
            int n = _path.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = _path[i].x, yi = _path[i].y;
                float xj = _path[j].x, yj = _path[j].y;
                if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        public void ClearSelection()
        {
            HasSelection = false;
            SelectedPixels.Clear();
            _path.Clear();
        }
    }
}
