using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Palette
{
    public class ColourEditorPopup
    {
        public float Hue { get; set; }
        public float Saturation { get; set; }
        public float Value { get; set; }
        public byte Alpha { get; set; } = 255;
        public string HexInput { get; set; }

        public event Action<Color32> OnColourConfirmed;
        public event Action OnCancelled;

        public Color32 CurrentColour
        {
            get
            {
                Color c = Color.HSVToRGB(Hue, Saturation, Value);
                return new Color32(
                    (byte)(c.r * 255), (byte)(c.g * 255),
                    (byte)(c.b * 255), Alpha);
            }
        }

        public void SetFromColour(Color32 colour)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            Hue = h;
            Saturation = s;
            Value = v;
            Alpha = colour.a;
            HexInput = ColourPalette.ColourToHex(colour);
        }

        public void SetFromHex(string hex)
        {
            var c = ColourPalette.HexToColour(hex);
            SetFromColour(c);
        }

        public void Confirm()
        {
            OnColourConfirmed?.Invoke(CurrentColour);
        }

        public void Cancel()
        {
            OnCancelled?.Invoke();
        }
    }
}
