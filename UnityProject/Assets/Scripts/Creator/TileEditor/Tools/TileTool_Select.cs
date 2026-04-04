using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Select : TileTool
    {
        public override string Name => "Select";
        public override string Shortcut => "S";
        public override string Description => "Select a rectangular region";

        private PixelCoord _startCoord;
        private bool _selecting;

        public bool HasSelection { get; private set; }
        public int SelectionX { get; private set; }
        public int SelectionY { get; private set; }
        public int SelectionW { get; private set; }
        public int SelectionH { get; private set; }

        public override void Deactivate()
        {
            ClearSelection();
        }

        public override void OnPointerDown(PixelCoord coord)
        {
            if (HasSelection && IsInsideSelection(coord))
            {
                // Start moving selection
                // (movement handled by UI)
                return;
            }

            ClearSelection();
            _startCoord = coord;
            _selecting = true;
        }

        public override void OnPointerDrag(PixelCoord coord)
        {
            if (!_selecting) return;
            UpdateSelection(_startCoord, coord);
        }

        public override void OnPointerUp(PixelCoord coord)
        {
            if (!_selecting) return;
            _selecting = false;
            UpdateSelection(_startCoord, coord);
            if (SelectionW <= 0 || SelectionH <= 0)
                ClearSelection();
        }

        private void UpdateSelection(PixelCoord a, PixelCoord b)
        {
            SelectionX = Math.Min(a.x, b.x);
            SelectionY = Math.Min(a.y, b.y);
            SelectionW = Math.Abs(b.x - a.x) + 1;
            SelectionH = Math.Abs(b.y - a.y) + 1;
            HasSelection = true;
        }

        public bool IsInsideSelection(PixelCoord coord)
        {
            return HasSelection &&
                   coord.x >= SelectionX && coord.x < SelectionX + SelectionW &&
                   coord.y >= SelectionY && coord.y < SelectionY + SelectionH;
        }

        public void CopySelection()
        {
            if (!HasSelection) return;
            CanvasClipboard.Copy(Controller.Canvas.Pixels, Controller.Canvas.Width,
                SelectionX, SelectionY, SelectionW, SelectionH);
        }

        public void CutSelection()
        {
            if (!HasSelection) return;
            CopySelection();
            Controller.BeginStroke();
            ClearPixelsInSelection();
            Controller.EndStroke();
        }

        public void PasteFromClipboard()
        {
            if (!CanvasClipboard.HasContent) return;
            Controller.BeginStroke();
            int cx = (Controller.Canvas.Width - CanvasClipboard.Width) / 2;
            int cy = (Controller.Canvas.Height - CanvasClipboard.Height) / 2;
            CanvasClipboard.Paste(Controller.Canvas.Pixels, Controller.Canvas.Width, Controller.Canvas.Height, cx, cy);
            Controller.EndStroke();
        }

        public void DeleteSelection()
        {
            if (!HasSelection) return;
            Controller.BeginStroke();
            ClearPixelsInSelection();
            Controller.EndStroke();
        }

        public void FlipHorizontal()
        {
            if (!HasSelection) return;
            Controller.BeginStroke();
            var canvas = Controller.Canvas;
            for (int y = SelectionY; y < SelectionY + SelectionH; y++)
            {
                for (int i = 0; i < SelectionW / 2; i++)
                {
                    int lx = SelectionX + i;
                    int rx = SelectionX + SelectionW - 1 - i;
                    var temp = canvas.GetPixel(lx, y);
                    canvas.SetPixel(lx, y, canvas.GetPixel(rx, y));
                    canvas.SetPixel(rx, y, temp);
                }
            }
            Controller.EndStroke();
        }

        public void FlipVertical()
        {
            if (!HasSelection) return;
            Controller.BeginStroke();
            var canvas = Controller.Canvas;
            for (int x = SelectionX; x < SelectionX + SelectionW; x++)
            {
                for (int i = 0; i < SelectionH / 2; i++)
                {
                    int ty = SelectionY + i;
                    int by = SelectionY + SelectionH - 1 - i;
                    var temp = canvas.GetPixel(x, ty);
                    canvas.SetPixel(x, ty, canvas.GetPixel(x, by));
                    canvas.SetPixel(x, by, temp);
                }
            }
            Controller.EndStroke();
        }

        public void ClearSelection()
        {
            HasSelection = false;
            SelectionX = SelectionY = SelectionW = SelectionH = 0;
        }

        private void ClearPixelsInSelection()
        {
            var transparent = new Color32(0, 0, 0, 0);
            for (int y = SelectionY; y < SelectionY + SelectionH; y++)
                for (int x = SelectionX; x < SelectionX + SelectionW; x++)
                    Controller.Canvas.SetPixel(x, y, transparent);
        }
    }
}
