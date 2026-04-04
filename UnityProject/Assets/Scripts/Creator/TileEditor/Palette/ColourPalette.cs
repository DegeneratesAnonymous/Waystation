using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Palette
{
    [System.Serializable]
    public struct PaletteSwatch
    {
        public string key;
        public string label;
        public Color32 colour;

        public PaletteSwatch(string key, string label, byte r, byte g, byte b)
        {
            this.key = key;
            this.label = label;
            this.colour = new Color32(r, g, b, 255);
        }
    }

    public class ColourPalette
    {
        public static readonly PaletteSwatch[] SharedSwatches = new PaletteSwatch[]
        {
            new PaletteSwatch("tBase",    "Tile Base",        0x2d, 0x30, 0x40),
            new PaletteSwatch("tDeep",    "Tile Deep",        0x22, 0x25, 0x30),
            new PaletteSwatch("tHi",      "Tile Hi",          0x42, 0x48, 0x58),
            new PaletteSwatch("tLo",      "Tile Lo",          0x1c, 0x1f, 0x2c),
            new PaletteSwatch("tGrout",   "Tile Grout",       0x1a, 0x1d, 0x28),
            new PaletteSwatch("fBase",    "Frame Base",       0x38, 0x3c, 0x50),
            new PaletteSwatch("fLit",     "Frame Lit",        0x43, 0x48, 0x60),
            new PaletteSwatch("fDark",    "Frame Dark",       0x25, 0x28, 0x38),
            new PaletteSwatch("fShadow",  "Frame Shadow",     0x14, 0x16, 0x18),
            new PaletteSwatch("fBevel",   "Frame Bevel",      0x4e, 0x54, 0x70),
            new PaletteSwatch("fGrout",   "Frame Grout",      0x16, 0x19, 0x20),
            new PaletteSwatch("rHi",      "Rivet Hi",         0x56, 0x5e, 0x7a),
            new PaletteSwatch("rLo",      "Rivet Lo",         0x2c, 0x30, 0x48),
            new PaletteSwatch("acc",      "Accent",           0x48, 0x80, 0xaa),
            new PaletteSwatch("accG",     "Accent Glow",      0x11, 0x1e, 0x30),
            new PaletteSwatch("accD",     "Accent Dark",      0x1a, 0x3a, 0x60),
            new PaletteSwatch("stBlue",   "Status Blue",      0x48, 0x80, 0xaa),
            new PaletteSwatch("stAmber",  "Status Amber",     0xc8, 0xb0, 0x30),
            new PaletteSwatch("stRed",    "Status Red",       0xc8, 0x30, 0x30),
            new PaletteSwatch("stGreen",  "Status Green",     0x30, 0xa0, 0x50),
            new PaletteSwatch("dmgCrack", "Damage Crack",     0x0f, 0x0e, 0x14),
            new PaletteSwatch("dmgDark",  "Damage Dark",      0x1a, 0x18, 0x20),
            new PaletteSwatch("dmgSpall", "Damage Spall",     0x2e, 0x28, 0x38),
            new PaletteSwatch("dmgScorch","Damage Scorch",    0x1c, 0x18, 0x1e),
            new PaletteSwatch("dstVoid",  "Destroyed Void",   0x09, 0x0a, 0x0d),
            new PaletteSwatch("dstRubble","Destroyed Rubble", 0x20, 0x1c, 0x2a),
        };

        public const int MaxRecentColours = 8;
        private readonly List<Color32> _recentColours = new List<Color32>();
        private readonly List<Creator.CustomSwatch> _customSwatches;

        public IReadOnlyList<Color32> RecentColours => _recentColours;

        public event Action OnPaletteChanged;

        public ColourPalette(List<Creator.CustomSwatch> customSwatches = null)
        {
            _customSwatches = customSwatches ?? new List<Creator.CustomSwatch>();
        }

        public void UseColour(Color32 colour)
        {
            // Add to front of recents, remove dups
            for (int i = _recentColours.Count - 1; i >= 0; i--)
            {
                var c = _recentColours[i];
                if (c.r == colour.r && c.g == colour.g && c.b == colour.b && c.a == colour.a)
                    _recentColours.RemoveAt(i);
            }
            _recentColours.Insert(0, colour);
            while (_recentColours.Count > MaxRecentColours)
                _recentColours.RemoveAt(_recentColours.Count - 1);
            OnPaletteChanged?.Invoke();
        }

        public Color32 GetSwatchColour(int index)
        {
            if (index >= 0 && index < SharedSwatches.Length)
                return SharedSwatches[index].colour;
            return new Color32(0, 0, 0, 255);
        }

        public static Color32 HexToColour(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return new Color32(r, g, b, 255);
            }
            return new Color32(0, 0, 0, 255);
        }

        public static string ColourToHex(Color32 c)
        {
            return $"#{c.r:X2}{c.g:X2}{c.b:X2}";
        }
    }
}
