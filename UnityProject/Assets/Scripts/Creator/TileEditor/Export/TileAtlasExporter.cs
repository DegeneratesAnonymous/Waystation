using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Export
{
    public static class TileAtlasExporter
    {
        public const int TileSize = 64;
        public const int SlotSize = 66;
        public const int Padding = 1;

        public static Texture2D ExportFloorAtlas(Color32[] normalPixels, Color32[] damagedPixels, Color32[] destroyedPixels)
        {
            int cols = 3;
            int atlasW = cols * SlotSize;
            int atlasH = SlotSize;
            var atlas = CreateAtlasTexture(atlasW, atlasH);

            BlitTile(atlas, normalPixels, 0, 0, TileSize, TileSize);
            if (damagedPixels != null) BlitTile(atlas, damagedPixels, 1, 0, TileSize, TileSize);
            if (destroyedPixels != null) BlitTile(atlas, destroyedPixels, 2, 0, TileSize, TileSize);

            atlas.Apply();
            return atlas;
        }

        public static Texture2D ExportWallAtlas(Func<int, Color32[]> getVariantPixels)
        {
            int cols = 16;
            int atlasW = cols * SlotSize;
            int atlasH = SlotSize;
            var atlas = CreateAtlasTexture(atlasW, atlasH);

            for (int v = 0; v < 16; v++)
            {
                var pixels = getVariantPixels(v);
                if (pixels != null)
                    BlitTile(atlas, pixels, v, 0, TileSize, TileSize);
            }

            atlas.Apply();
            return atlas;
        }

        public static Texture2D ExportFurnitureAtlas(
            int footprintW, int footprintH,
            int directionCount, int stateCount,
            Func<int, int, int, Color32[]> getCellPixels) // (direction, state, cell) -> pixels
        {
            int cellsPerTile = footprintW * footprintH;
            int cols = cellsPerTile * stateCount;
            int rows = directionCount;
            int atlasW = cols * SlotSize;
            int atlasH = rows * SlotSize;
            var atlas = CreateAtlasTexture(atlasW, atlasH);

            for (int dir = 0; dir < directionCount; dir++)
            {
                for (int state = 0; state < stateCount; state++)
                {
                    for (int cell = 0; cell < cellsPerTile; cell++)
                    {
                        var pixels = getCellPixels(dir, state, cell);
                        int col = state * cellsPerTile + cell;
                        if (pixels != null)
                            BlitTile(atlas, pixels, col, dir, TileSize, TileSize);
                    }
                }
            }

            atlas.Apply();
            return atlas;
        }

        public static byte[] EncodeAtlasToPNG(Texture2D atlas)
        {
            return ImageConversion.EncodeToPNG(atlas);
        }

        private static Texture2D CreateAtlasTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            // Initialize transparent
            var clear = new Color32[w * h];
            tex.SetPixels32(clear);
            return tex;
        }

        private static void BlitTile(Texture2D atlas, Color32[] tilePixels, int col, int row, int tileW, int tileH)
        {
            int startX = col * SlotSize + Padding;
            // Atlas Y is flipped (bottom-up in Unity textures)
            int atlasRows = atlas.height / SlotSize;
            int startY = (atlasRows - 1 - row) * SlotSize + Padding;

            for (int y = 0; y < tileH; y++)
            {
                for (int x = 0; x < tileW; x++)
                {
                    int srcIdx = y * tileW + x;
                    if (srcIdx < tilePixels.Length)
                        atlas.SetPixel(startX + x, startY + y, tilePixels[srcIdx]);
                }
            }
        }
    }
}
