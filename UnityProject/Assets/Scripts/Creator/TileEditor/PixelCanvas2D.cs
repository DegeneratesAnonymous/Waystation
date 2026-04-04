using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor
{
    public struct PixelCoord
    {
        public int x;
        public int y;
        public PixelCoord(int x, int y) { this.x = x; this.y = y; }
        public static PixelCoord Invalid => new PixelCoord(-1, -1);
        public bool IsValid(int width, int height) => x >= 0 && x < width && y >= 0 && y < height;
        public override string ToString() => $"({x}, {y})";
    }

    public class PixelCanvas2D
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Color32[] Pixels { get; private set; }
        public Texture2D Texture { get; private set; }

        public float Zoom { get; set; } = 8f;
        public Vector2 PanOffset { get; set; } = Vector2.zero;
        public bool ShowPixelGrid { get; set; } = true;
        public bool PixelGridAutoHide { get; set; } = true;
        public bool ShowTileBoundaryGrid { get; set; } = true;

        public const float MinZoom = 2f;
        public const float MaxZoom = 32f;
        public const int TileSizePx = 64;

        public bool IsDirty { get; private set; }

        public PixelCanvas2D(int width = 64, int height = 64)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Clear();
        }

        public Color32 GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return new Color32(0, 0, 0, 0);
            return Pixels[y * Width + x];
        }

        public void SetPixel(int x, int y, Color32 color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            Pixels[y * Width + x] = color;
            IsDirty = true;
        }

        public void ApplyChanges()
        {
            Texture.SetPixels32(Pixels);
            Texture.Apply();
            IsDirty = false;
        }

        public void Clear()
        {
            for (int i = 0; i < Pixels.Length; i++)
                Pixels[i] = new Color32(0, 0, 0, 0);
            ApplyChanges();
        }

        public void Resize(int newWidth, int newHeight)
        {
            var newPixels = new Color32[newWidth * newHeight];
            int copyW = Math.Min(Width, newWidth);
            int copyH = Math.Min(Height, newHeight);
            for (int y = 0; y < copyH; y++)
                for (int x = 0; x < copyW; x++)
                    newPixels[y * newWidth + x] = Pixels[y * Width + x];

            Width = newWidth;
            Height = newHeight;
            Pixels = newPixels;

            if (Texture != null)
                UnityEngine.Object.Destroy(Texture);
            Texture = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            ApplyChanges();
        }

        public void ZoomAt(float delta, Vector2 cursorCanvasPos)
        {
            float oldZoom = Zoom;
            Zoom = Mathf.Clamp(Zoom + delta, MinZoom, MaxZoom);
            if (Math.Abs(Zoom - oldZoom) > 0.001f)
            {
                float ratio = Zoom / oldZoom;
                PanOffset = cursorCanvasPos - (cursorCanvasPos - PanOffset) * ratio;
            }
        }

        public void SnapToFit(float viewportWidth, float viewportHeight, float margin = 16f)
        {
            float scaleX = (viewportWidth - margin * 2) / Width;
            float scaleY = (viewportHeight - margin * 2) / Height;
            Zoom = Mathf.Clamp(Mathf.Min(scaleX, scaleY), MinZoom, MaxZoom);
            PanOffset = new Vector2(
                (viewportWidth - Width * Zoom) / 2f,
                (viewportHeight - Height * Zoom) / 2f);
        }

        public PixelCoord ViewportToPixel(Vector2 viewportPos)
        {
            float px = (viewportPos.x - PanOffset.x) / Zoom;
            float py = (viewportPos.y - PanOffset.y) / Zoom;
            return new PixelCoord(Mathf.FloorToInt(px), Mathf.FloorToInt(py));
        }

        public bool ShouldShowPixelGrid()
        {
            if (!ShowPixelGrid) return false;
            if (PixelGridAutoHide && Zoom < 6f) return false;
            return true;
        }

        public bool ShouldShowTileBoundary()
        {
            return ShowTileBoundaryGrid && (Width > TileSizePx || Height > TileSizePx);
        }

        public Color32[] ClonePixels()
        {
            var copy = new Color32[Pixels.Length];
            Array.Copy(Pixels, copy, Pixels.Length);
            return copy;
        }

        public void LoadPixels(Color32[] data)
        {
            if (data.Length != Pixels.Length) return;
            Array.Copy(data, Pixels, Pixels.Length);
            ApplyChanges();
        }

        public void Dispose()
        {
            if (Texture != null)
                UnityEngine.Object.Destroy(Texture);
        }
    }
}
