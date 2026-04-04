using UnityEngine;

namespace Waystation.Creator.TileEditor.Preview
{
    public class RoomPreviewRenderer
    {
        public const int RoomCols = 8;
        public const int RoomRows = 6;
        public const int TileSize = 64;

        private Texture2D _previewTexture;
        public Texture2D PreviewTexture => _previewTexture;
        public bool IsOpen { get; set; }

        private readonly Color32 _bgColor = new Color32(0x09, 0x0a, 0x0d, 255); // dstVoid

        public RoomPreviewRenderer()
        {
            int w = RoomCols * TileSize;
            int h = RoomRows * TileSize;
            _previewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        public void UpdatePreview(Color32[] floorPixels, Color32[] wallPixels, int tileW, int tileH)
        {
            int w = RoomCols * TileSize;
            int h = RoomRows * TileSize;
            var pixels = new Color32[w * h];

            // Fill background
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = _bgColor;

            // Tile the floor across the room
            if (floorPixels != null)
            {
                for (int ry = 1; ry < RoomRows - 1; ry++)
                {
                    for (int rx = 1; rx < RoomCols - 1; rx++)
                    {
                        BlitTile(pixels, w, rx * TileSize, ry * TileSize, floorPixels, tileW, tileH);
                    }
                }
            }

            // Place walls around the edges
            if (wallPixels != null)
            {
                for (int rx = 0; rx < RoomCols; rx++)
                {
                    BlitTile(pixels, w, rx * TileSize, 0, wallPixels, tileW, tileH);
                    BlitTile(pixels, w, rx * TileSize, (RoomRows - 1) * TileSize, wallPixels, tileW, tileH);
                }
                for (int ry = 1; ry < RoomRows - 1; ry++)
                {
                    BlitTile(pixels, w, 0, ry * TileSize, wallPixels, tileW, tileH);
                    BlitTile(pixels, w, (RoomCols - 1) * TileSize, ry * TileSize, wallPixels, tileW, tileH);
                }
            }

            _previewTexture.SetPixels32(pixels);
            _previewTexture.Apply();
        }

        private static void BlitTile(Color32[] dest, int destW, int destX, int destY,
            Color32[] tile, int tileW, int tileH)
        {
            for (int y = 0; y < tileH; y++)
            {
                for (int x = 0; x < tileW; x++)
                {
                    int dx = destX + x;
                    int dy = destY + y;
                    if (dx >= 0 && dx < destW && dy >= 0 && dy < destW) // basic safety
                    {
                        int srcIdx = y * tileW + x;
                        int dstIdx = dy * destW + dx;
                        if (srcIdx < tile.Length && dstIdx < dest.Length)
                        {
                            var px = tile[srcIdx];
                            if (px.a > 0)
                                dest[dstIdx] = px;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_previewTexture != null)
                Object.Destroy(_previewTexture);
        }
    }
}
