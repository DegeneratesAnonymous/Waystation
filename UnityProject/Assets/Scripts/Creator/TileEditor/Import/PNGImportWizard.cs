using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Import
{
    public class ImportResult
    {
        public bool Success;
        public List<Color32[]> Tiles = new List<Color32[]>();
        public int TileWidth = 64;
        public int TileHeight = 64;
        public int Columns;
        public int Rows;
        public string Error;
    }

    public class PNGImportWizard
    {
        public Texture2D SourceImage { get; private set; }
        public int DetectedTileW { get; private set; } = 64;
        public int DetectedTileH { get; private set; } = 64;
        public int GridOffsetX { get; set; }
        public int GridOffsetY { get; set; }
        public int GridSpacingX { get; set; }
        public int GridSpacingY { get; set; }

        public bool LoadImage(string path)
        {
            if (!File.Exists(path)) return false;
            byte[] data = File.ReadAllBytes(path);
            SourceImage = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!SourceImage.LoadImage(data)) return false;

            // Detect tile size from image dimensions
            DetectTileSize();
            return true;
        }

        public bool LoadImage(byte[] pngData)
        {
            SourceImage = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!SourceImage.LoadImage(pngData)) return false;
            DetectTileSize();
            return true;
        }

        private void DetectTileSize()
        {
            // If image is exactly 64xN or Nx64, assume 64px tiles
            if (SourceImage.width % 64 == 0 && SourceImage.height % 64 == 0)
            {
                DetectedTileW = 64;
                DetectedTileH = 64;
            }
            else if (SourceImage.width % 66 == 0 && SourceImage.height % 66 == 0)
            {
                // Padded format (66px slots)
                DetectedTileW = 64;
                DetectedTileH = 64;
                GridSpacingX = 2;
                GridSpacingY = 2;
                GridOffsetX = 1;
                GridOffsetY = 1;
            }
            else if (SourceImage.width % 32 == 0 && SourceImage.height % 32 == 0)
            {
                DetectedTileW = 32;
                DetectedTileH = 32;
            }
            else if (SourceImage.width % 16 == 0 && SourceImage.height % 16 == 0)
            {
                DetectedTileW = 16;
                DetectedTileH = 16;
            }
        }

        public ImportResult ExtractTiles()
        {
            var result = new ImportResult
            {
                TileWidth = DetectedTileW,
                TileHeight = DetectedTileH
            };

            if (SourceImage == null)
            {
                result.Error = "No image loaded";
                return result;
            }

            int stepX = DetectedTileW + GridSpacingX;
            int stepY = DetectedTileH + GridSpacingY;

            result.Columns = (SourceImage.width - GridOffsetX + GridSpacingX) / stepX;
            result.Rows = (SourceImage.height - GridOffsetY + GridSpacingY) / stepY;

            if (result.Columns <= 0 || result.Rows <= 0)
            {
                result.Error = "Could not detect valid tiles in image";
                return result;
            }

            for (int row = 0; row < result.Rows; row++)
            {
                for (int col = 0; col < result.Columns; col++)
                {
                    int srcX = GridOffsetX + col * stepX;
                    int srcY = GridOffsetY + row * stepY;
                    var tile = new Color32[DetectedTileW * DetectedTileH];

                    for (int y = 0; y < DetectedTileH; y++)
                    {
                        for (int x = 0; x < DetectedTileW; x++)
                        {
                            int imgY = SourceImage.height - 1 - (srcY + y); // flip Y
                            if (imgY >= 0 && imgY < SourceImage.height && srcX + x < SourceImage.width)
                                tile[y * DetectedTileW + x] = SourceImage.GetPixel(srcX + x, imgY);
                        }
                    }

                    result.Tiles.Add(tile);
                }
            }

            result.Success = result.Tiles.Count > 0;
            return result;
        }

        public void Dispose()
        {
            if (SourceImage != null)
                UnityEngine.Object.Destroy(SourceImage);
        }
    }
}
