using UnityEngine;

namespace Waystation.Creator.TileEditor.Preview
{
    public class ContextPreviewRenderer
    {
        private Texture2D _previewTexture;
        private readonly int _tileSize;
        private readonly int _gridSize;

        public Texture2D PreviewTexture => _previewTexture;

        public ContextPreviewRenderer(int tileSize = 64, int gridSize = 3)
        {
            _tileSize = tileSize;
            _gridSize = gridSize;
            int total = tileSize * gridSize;
            _previewTexture = new Texture2D(total, total, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        public void UpdatePreview(Color32[] tilePixels, int tileW, int tileH)
        {
            int total = _tileSize * _gridSize;
            var pixels = new Color32[total * total];

            // Tile the active tile across the 3x3 grid
            for (int gy = 0; gy < _gridSize; gy++)
            {
                for (int gx = 0; gx < _gridSize; gx++)
                {
                    for (int y = 0; y < _tileSize && y < tileH; y++)
                    {
                        for (int x = 0; x < _tileSize && x < tileW; x++)
                        {
                            int srcIdx = y * tileW + x;
                            int dstX = gx * _tileSize + x;
                            int dstY = gy * _tileSize + y;
                            if (srcIdx < tilePixels.Length)
                                pixels[dstY * total + dstX] = tilePixels[srcIdx];
                        }
                    }
                }
            }

            _previewTexture.SetPixels32(pixels);
            _previewTexture.Apply();
        }

        public void Dispose()
        {
            if (_previewTexture != null)
                Object.Destroy(_previewTexture);
        }
    }
}
