using System.IO;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Export
{
    public static class CanvasPersistence
    {
        public static void SaveVariant(string assetDir, int variantIndex, Color32[] pixels, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            tex.SetPixels32(pixels);
            tex.Apply();

            string path = Path.Combine(assetDir, $"variant_{variantIndex}.png");
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            Object.Destroy(tex);
        }

        public static Color32[] LoadVariant(string assetDir, int variantIndex, int width, int height)
        {
            string path = Path.Combine(assetDir, $"variant_{variantIndex}.png");
            if (!File.Exists(path)) return null;

            byte[] data = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, data))
            {
                Object.Destroy(tex);
                return null;
            }

            // Resize if needed
            if (tex.width != width || tex.height != height)
            {
                Object.Destroy(tex);
                return null;
            }

            var pixels = tex.GetPixels32();
            Object.Destroy(tex);
            return pixels;
        }

        public static void SaveThumbnail(string assetDir, Texture2D canvasTexture)
        {
            int thumbSize = 128;
            var thumb = new Texture2D(thumbSize, thumbSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            // Simple nearest-neighbor scale from canvas to thumbnail
            float scaleX = (float)canvasTexture.width / thumbSize;
            float scaleY = (float)canvasTexture.height / thumbSize;
            for (int y = 0; y < thumbSize; y++)
                for (int x = 0; x < thumbSize; x++)
                    thumb.SetPixel(x, y, canvasTexture.GetPixel(
                        Mathf.FloorToInt(x * scaleX),
                        Mathf.FloorToInt(y * scaleY)));

            thumb.Apply();
            string path = Path.Combine(assetDir, "thumbnail.png");
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(thumb));
            Object.Destroy(thumb);
        }
    }
}
