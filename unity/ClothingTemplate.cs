// ClothingLayerAppearance — describes how a single clothing layer should be
// rendered: which atlas variant (style/type) and how each colour slot is filled.
//
// ClothingTemplate — a serialisable, sellable design that describes a full
// clothing configuration. Stored in the economy and assigned to NPCs by
// clothing designers.
using System;
using System.Collections.Generic;

namespace Waystation.NPC
{
    // ── ClothingLayerAppearance ───────────────────────────────────────────────

    /// <summary>
    /// Replaces the old atlas-column-index approach.
    /// <para>
    /// <see cref="atlasVariantIndex"/> selects the shape/style tile from the
    /// neutral-master atlas (0-based, one column per style/type).
    /// </para>
    /// <para>
    /// <see cref="slotColours"/> has one entry per colour slot defined in the
    /// atlas JSON <c>colour_slots</c> array for this variant; order matches
    /// that array.
    /// </para>
    /// </summary>
    [Serializable]
    public class ClothingLayerAppearance
    {
        /// <summary>
        /// Which base shape/style variant to use (0-based column in the
        /// neutral-master atlas).
        /// </summary>
        public int atlasVariantIndex;

        /// <summary>
        /// Colour sources for each recolourable slot on this layer.
        /// Order matches the <c>colour_slots</c> array in the atlas JSON.
        /// May be shorter than the defined slot count — missing entries are
        /// treated as <see cref="ColourSource.MaterialDefault"/>.
        /// </summary>
        public List<ColourSource> slotColours = new List<ColourSource>();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the ColourSource for slot <paramref name="index"/>,
        /// or MaterialDefault when the index is out of range.
        /// </summary>
        public ColourSource GetSlot(int index)
        {
            if (index < 0 || index >= slotColours.Count)
                return ColourSource.Default();
            return slotColours[index] ?? ColourSource.Default();
        }
    }

    // ── ClothingTemplate ─────────────────────────────────────────────────────

    /// <summary>
    /// A serialisable, sellable clothing design created by a designer NPC.
    /// Stores a per-layer appearance definition that can be applied to any NPC.
    /// </summary>
    [Serializable]
    public class ClothingTemplate
    {
        /// <summary>Unique identifier for this template (e.g. "tmpl_abc123").</summary>
        public string templateId;

        /// <summary>Entity UID of the designer NPC who created this template.</summary>
        public string designerEntityId;

        /// <summary>
        /// Per-layer appearance data. Keys are layer identifiers matching
        /// <see cref="NpcAppearance"/> layer field names
        /// (e.g. "hair", "hat", "shirt", "pants", "shoes", "back", "weapon").
        /// </summary>
        public Dictionary<string, ClothingLayerAppearance> layerAppearances
            = new Dictionary<string, ClothingLayerAppearance>();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the appearance for the given layer, or null if not defined.
        /// </summary>
        public ClothingLayerAppearance GetLayer(string layerKey)
        {
            return layerAppearances.TryGetValue(layerKey, out var la) ? la : null;
        }

        /// <summary>
        /// Sets or replaces the appearance for the given layer.
        /// </summary>
        public void SetLayer(string layerKey, ClothingLayerAppearance appearance)
        {
            layerAppearances[layerKey] = appearance;
        }
    }
}
