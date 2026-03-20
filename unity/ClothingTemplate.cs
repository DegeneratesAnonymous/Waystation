// ClothingTemplate — a reusable clothing configuration that can be applied to NPCs.
using System;
using System.Collections.Generic;

namespace Waystation.NPC
{
    /// <summary>
    /// A named clothing template defining per-layer appearances and a cached beauty score.
    /// layers maps layer name ("hair","hat","shirt","pants","shoes","back","weapon") to appearance.
    /// </summary>
    [Serializable]
    public class ClothingTemplate
    {
        public string templateId;
        public string designerEntityId;
        public string displayName;

        /// <summary>
        /// Per-layer appearance keyed by layer name:
        /// "hair", "hat", "shirt", "pants", "shoes", "back", "weapon".
        /// </summary>
        public Dictionary<string, ClothingLayerAppearance> layers =
            new Dictionary<string, ClothingLayerAppearance>();

        /// <summary>Cached/computed beauty score for this template.</summary>
        public float beautyScore = 0f;
    }
}
