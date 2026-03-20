// ClothingLayerAppearance — describes a single clothing/hair layer under the shader tinting system.
using System;
using System.Collections.Generic;

namespace Waystation.NPC
{
    /// <summary>
    /// Holds the visual configuration for one NPC clothing or hair layer.
    /// atlasVariantIndex selects the neutral-tone master sprite; slotColours provide
    /// per-channel tints that the NpcApparel shader applies at runtime.
    /// </summary>
    [Serializable]
    public class ClothingLayerAppearance
    {
        /// <summary>
        /// Selects the neutral-tone master sprite from the atlas (maps to style/type enum cast).
        /// </summary>
        public int atlasVariantIndex = 0;

        /// <summary>
        /// One ColourSource per colour slot defined in the atlas JSON for this variant.
        /// Order matches the colour_slots array in the atlas JSON.
        /// </summary>
        public List<ColourSource> slotColours = new List<ColourSource>();
    }
}
