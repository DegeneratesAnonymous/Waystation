// ClothingTemplate — serialisable record representing a saved clothing design.
//
// A template captures:
//   • the atlas variant selected for each NPC clothing layer
//   • per-slot colour bindings for that variant (explicit, dept, or material-default)
//   • metadata: name, designer, tags, beauty score, revision
//
// ClothingTemplate instances are managed by TemplateLibrary and persisted with
// save data.  They are not ScriptableObjects so they can be serialised to JSON
// without Unity asset infrastructure.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // ── Per-layer appearance ──────────────────────────────────────────────────

    /// <summary>
    /// The appearance state of one NPC clothing layer within a template.
    /// </summary>
    [Serializable]
    public class ClothingLayerAppearance
    {
        // Layer identifier matching the NPC layer name used in NpcSpriteController
        // (e.g. "hair", "hat", "shirt", "pants", "shoes", "back", "weapon").
        public string layerName = "";

        // Whether this layer is enabled (visible) in the template.
        public bool enabled = true;

        // Atlas variant tile ID (e.g. "npc_shirt_uniform") from the atlas JSON.
        // Empty string means "no variant selected" (layer disabled or unset).
        public string variantId = "";

        // One binding per colour_slot declared in the atlas JSON for the selected variant.
        public List<ColourSlotBinding> colourBindings = new List<ColourSlotBinding>();

        public ClothingLayerAppearance() { }

        public ClothingLayerAppearance(string name)
        {
            layerName = name;
        }

        /// <summary>
        /// Returns the ColourSource for a named slot, or MaterialDefault if
        /// no binding exists for that slot.
        /// </summary>
        public ColourSource GetSlotSource(string slotName)
        {
            foreach (var b in colourBindings)
                if (b.slotName == slotName)
                    return b.source;
            return ColourSource.MaterialDefault();
        }

        /// <summary>
        /// Sets (or adds) the ColourSource for the named slot.
        /// </summary>
        public void SetSlotSource(string slotName, ColourSource src)
        {
            foreach (var b in colourBindings)
            {
                if (b.slotName == slotName) { b.source = src; return; }
            }
            colourBindings.Add(new ColourSlotBinding(slotName, src));
        }
    }

    // ── Template ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A complete saved clothing design.  Managed by TemplateLibrary.
    /// </summary>
    [Serializable]
    public class ClothingTemplate
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string templateId      = "";
        public string templateName    = "Untitled Design";
        public string designerName    = "";
        public int    revisionNumber  = 0;
        public bool   importedFlag    = false;   // true when imported from another save

        // ── Tags ──────────────────────────────────────────────────────────────
        public List<string> tags = new List<string>();

        // ── Computed scores (recalculated; not authoritative source-of-truth) ─
        public float beautyScore   = 0f;
        public int   inherentValue = 0;

        // ── Layer appearances (one per supported NPC layer, in render order) ──
        public List<ClothingLayerAppearance> layers = new List<ClothingLayerAppearance>();

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>Creates a blank template with default layers initialised.</summary>
        public static ClothingTemplate CreateBlank(string designerName = "")
        {
            var t = new ClothingTemplate
            {
                templateId   = Guid.NewGuid().ToString("N")[..12],
                designerName = designerName,
            };

            // Initialise one empty layer per NPC slot in render order.
            foreach (string layer in NpcLayerNames)
                t.layers.Add(new ClothingLayerAppearance(layer));

            return t;
        }

        /// <summary>Creates a deep copy with a new templateId and revisionNumber 0.</summary>
        public ClothingTemplate Duplicate()
        {
            var copy = new ClothingTemplate
            {
                templateId      = Guid.NewGuid().ToString("N")[..12],
                templateName    = templateName + " (Copy)",
                designerName    = designerName,
                revisionNumber  = 0,
                importedFlag    = false,
                tags            = new List<string>(tags),
                beautyScore     = beautyScore,
                inherentValue   = inherentValue,
            };

            foreach (var l in layers)
            {
                var lCopy = new ClothingLayerAppearance(l.layerName)
                {
                    enabled   = l.enabled,
                    variantId = l.variantId,
                };
                foreach (var b in l.colourBindings)
                    lCopy.colourBindings.Add(new ColourSlotBinding(b.slotName,
                        new ColourSource { type = b.source.type, hexValue = b.source.hexValue }));
                copy.layers.Add(lCopy);
            }
            return copy;
        }

        /// <summary>
        /// Returns the ClothingLayerAppearance for the named layer, or null.
        /// </summary>
        public ClothingLayerAppearance GetLayer(string name)
        {
            foreach (var l in layers)
                if (l.layerName == name) return l;
            return null;
        }

        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// Canonical NPC layer names in render order (bottom → top).
        /// Must match the child-GameObject names expected by NpcSpriteController.
        /// </summary>
        public static readonly string[] NpcLayerNames =
        {
            "back", "hair", "hat", "shirt", "pants", "shoes", "weapon"
        };
    }
}
