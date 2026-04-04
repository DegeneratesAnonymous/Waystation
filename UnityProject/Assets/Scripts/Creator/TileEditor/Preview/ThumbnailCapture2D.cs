using UnityEngine;

namespace Waystation.Creator.TileEditor.Preview
{
    public static class ThumbnailCapture2D
    {
        public const int ThumbnailSize = 128;

        public static Texture2D Capture(PixelCanvas2D canvas)
        {
            var thumb = new Texture2D(ThumbnailSize, ThumbnailSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            float scaleX = (float)canvas.Width / ThumbnailSize;
            float scaleY = (float)canvas.Height / ThumbnailSize;

            for (int y = 0; y < ThumbnailSize; y++)
            {
                for (int x = 0; x < ThumbnailSize; x++)
                {
                    int sx = Mathf.FloorToInt(x * scaleX);
                    int sy = Mathf.FloorToInt(y * scaleY);
                    thumb.SetPixel(x, y, canvas.GetPixel(sx, sy));
                }
            }

            thumb.Apply();
            return thumb;
        }

        public static byte[] CaptureAsPNG(PixelCanvas2D canvas)
        {
            var thumb = Capture(canvas);
            byte[] png = ImageConversion.EncodeToPNG(thumb);
            Object.Destroy(thumb);
            return png;
        }
    }
}
