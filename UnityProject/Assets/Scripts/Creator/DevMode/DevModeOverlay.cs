using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.DevMode
{
    public class DevModeOverlay
    {
        public bool ShowPaletteKeys { get; set; }
        public bool ShowAtlasCoordinates { get; set; }
        public bool ShowSidecarEditor { get; set; }

        public bool IsDevModeEnabled => CreatorSettings.DevMode;

        public string GetPaletteKeyLabel(int swatchIndex)
        {
            if (!ShowPaletteKeys) return null;
            if (swatchIndex < 0 || swatchIndex >= TileEditor.Palette.ColourPalette.SharedSwatches.Length)
                return null;
            return TileEditor.Palette.ColourPalette.SharedSwatches[swatchIndex].key;
        }

        public string GetAtlasCoordinateLabel(int col, int row)
        {
            if (!ShowAtlasCoordinates) return null;
            int x = col * 66 + 1;
            int y = row * 66 + 1;
            return $"({x},{y})";
        }
    }
}
