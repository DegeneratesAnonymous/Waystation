using UnityEngine;

namespace Waystation.Creator.TileEditor
{
    public static class CanvasClipboard
    {
        public static Color32[] Pixels { get; private set; }
        public static int Width { get; private set; }
        public static int Height { get; private set; }
        public static bool HasContent => Pixels != null && Pixels.Length > 0;

        public static void Copy(Color32[] sourcePixels, int srcCanvasWidth, int x, int y, int w, int h)
        {
            Width = w;
            Height = h;
            Pixels = new Color32[w * h];
            for (int py = 0; py < h; py++)
                for (int px = 0; px < w; px++)
                {
                    int srcIdx = (y + py) * srcCanvasWidth + (x + px);
                    if (srcIdx >= 0 && srcIdx < sourcePixels.Length)
                        Pixels[py * w + px] = sourcePixels[srcIdx];
                }
        }

        public static void Paste(Color32[] destPixels, int destCanvasWidth, int destCanvasHeight, int x, int y)
        {
            if (!HasContent) return;
            for (int py = 0; py < Height; py++)
                for (int px = 0; px < Width; px++)
                {
                    int dx = x + px;
                    int dy = y + py;
                    if (dx < 0 || dx >= destCanvasWidth || dy < 0 || dy >= destCanvasHeight) continue;
                    var src = Pixels[py * Width + px];
                    if (src.a > 0)
                        destPixels[dy * destCanvasWidth + dx] = src;
                }
        }

        public static void Clear()
        {
            Pixels = null;
            Width = 0;
            Height = 0;
        }
    }
}
