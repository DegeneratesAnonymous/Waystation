// ColourSource — discriminated union that describes how a single colour slot on
// a clothing/furniture atlas variant should be resolved at render time.
//
// Three source kinds:
//   Explicit        — a free RGBA value chosen directly by the player.
//   DeptColour      — resolved at runtime via DepartmentRegistry using the
//                     wearing NPC's department.  Falls back to MaterialDefault
//                     when no department colour is configured.
//   MaterialDefault — shader receives (1,1,1,1); the atlas master tone shows through.
//
// ColourSlotBinding ties a named slot (as defined in the atlas JSON) to a source.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // ── Enum ──────────────────────────────────────────────────────────────────

    public enum ColourSourceType
    {
        Explicit,
        DeptColour,
        MaterialDefault,
    }

    // ── Colour source ─────────────────────────────────────────────────────────

    [Serializable]
    public class ColourSource
    {
        public ColourSourceType type = ColourSourceType.MaterialDefault;

        // Used when type == Explicit.  Stored as "#rrggbbaa" for JSON round-trip.
        public string hexValue = "#ffffffff";

        // ── Factories ─────────────────────────────────────────────────────────

        public static ColourSource Explicit(Color c)
            => new ColourSource { type = ColourSourceType.Explicit,
                                  hexValue = "#" + ColorUtility.ToHtmlStringRGBA(c) };

        public static ColourSource Explicit(string hex)
            => new ColourSource { type = ColourSourceType.Explicit, hexValue = hex };

        public static ColourSource DeptColour()
            => new ColourSource { type = ColourSourceType.DeptColour };

        public static ColourSource MaterialDefault()
            => new ColourSource { type = ColourSourceType.MaterialDefault };

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the source carries an explicit colour that can be
        /// parsed into a UnityEngine.Color without runtime context.
        /// </summary>
        public bool TryGetExplicit(out Color colour)
        {
            if (type == ColourSourceType.Explicit &&
                !string.IsNullOrEmpty(hexValue) &&
                ColorUtility.TryParseHtmlString(hexValue, out colour))
                return true;
            colour = Color.white;
            return false;
        }

        /// <summary>Returns a display-friendly description string for the UI.</summary>
        public string DisplayLabel()
        {
            return type switch
            {
                ColourSourceType.Explicit        => hexValue ?? "#ffffff",
                ColourSourceType.DeptColour      => "Dept Colour",
                ColourSourceType.MaterialDefault => "Material Default",
                _                                => "Unknown",
            };
        }
    }

    // ── Colour slot binding ───────────────────────────────────────────────────

    /// <summary>
    /// Maps a named atlas colour slot to a ColourSource.
    /// The slot name must match one of the "colour_slots[].name" entries in the
    /// atlas JSON for the selected variant.
    /// </summary>
    [Serializable]
    public class ColourSlotBinding
    {
        /// <summary>Slot name as declared in the atlas JSON (e.g. "primary", "trim").</summary>
        public string slotName = "";

        public ColourSource source = ColourSource.MaterialDefault();

        public ColourSlotBinding() { }

        public ColourSlotBinding(string name, ColourSource src)
        {
            slotName = name;
            source   = src;
        }
    }

    // ── Atlas colour slot definition (loaded from JSON) ───────────────────────

    /// <summary>
    /// Read-only description of one colour slot as declared in an atlas JSON tile.
    /// </summary>
    [Serializable]
    public class AtlasColourSlot
    {
        /// <summary>Human-readable slot name (e.g. "primary", "secondary", "trim").</summary>
        public string name = "";

        /// <summary>
        /// The mask channel colour this slot maps to in the _mask texture
        /// (e.g. "#ff0000" = red channel).
        /// </summary>
        public string maskColour = "";

        public static AtlasColourSlot FromDict(Dictionary<string, object> d)
        {
            return new AtlasColourSlot
            {
                name       = d.ContainsKey("name")        ? d["name"].ToString()         : "",
                maskColour = d.ContainsKey("mask_colour") ? d["mask_colour"].ToString()  : "",
            };
        }
    }
}
