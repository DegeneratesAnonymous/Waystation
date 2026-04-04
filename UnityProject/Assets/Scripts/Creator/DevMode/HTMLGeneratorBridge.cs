using System.IO;
using UnityEngine;

namespace Waystation.Creator.DevMode
{
    public static class HTMLGeneratorBridge
    {
        /// Imports a PNG exported from the HTML generator tools.
        /// Expects the file to be in the standard atlas format (66px slots).
        public static TileEditor.Import.ImportResult ImportFromGenerator(string pngPath)
        {
            var wizard = new TileEditor.Import.PNGImportWizard();
            if (!wizard.LoadImage(pngPath))
            {
                return new TileEditor.Import.ImportResult
                {
                    Success = false,
                    Error = "Failed to load PNG from generator"
                };
            }

            // Generator output uses 66px slots with 1px padding
            wizard.GridOffsetX = 1;
            wizard.GridOffsetY = 1;
            wizard.GridSpacingX = 2;
            wizard.GridSpacingY = 2;

            var result = wizard.ExtractTiles();
            wizard.Dispose();
            return result;
        }
    }
}
