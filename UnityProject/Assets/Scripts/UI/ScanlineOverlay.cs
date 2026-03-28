// ScanlineOverlay.cs
// Custom UI Toolkit VisualElement that renders a scanline texture overlay
// over a panel background, contributing to the industrial terminal aesthetic.
//
// The base USS class (.ws-scanline-overlay) provides a semi-transparent dark
// tint. For the full scanline line effect, assign a tileable scanline texture
// to the BackgroundTexture property from C#.
//
// Usage in UXML:
//   <Waystation.UI.ScanlineOverlay />
//
// Usage in C#:
//   var overlay = new ScanlineOverlay();
//   overlay.BackgroundTexture = myTexture;   // optional
//   panel.Add(overlay);

using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// A full-bleed overlay element that simulates CRT scanlines over a panel.
    /// Pointer events are ignored so underlying elements remain interactive.
    /// </summary>
    public class ScanlineOverlay : VisualElement
    {
        // ── UXML factory ──────────────────────────────────────────────────
        public new class UxmlFactory : UxmlFactory<ScanlineOverlay, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _opacity =
                new UxmlFloatAttributeDescription { name = "overlay-opacity", defaultValue = 0.06f };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var overlay = (ScanlineOverlay)ve;
                overlay.OverlayOpacity = _opacity.GetValueFromBag(bag, cc);
            }
        }

        // ── Backing fields ────────────────────────────────────────────────
        private Texture2D _backgroundTexture;
        private float _overlayOpacity = 0.06f;

        // ── Properties ────────────────────────────────────────────────────

        /// <summary>
        /// Optional tileable scanline texture. When set, it is displayed as the
        /// element background in stretch-to-fill mode.
        /// </summary>
        public Texture2D BackgroundTexture
        {
            get => _backgroundTexture;
            set
            {
                _backgroundTexture = value;
                style.backgroundImage = new StyleBackground(value);
            }
        }

        /// <summary>
        /// Alpha multiplier for the overlay (0–1). Defaults to 0.06.
        /// </summary>
        public float OverlayOpacity
        {
            get => _overlayOpacity;
            set
            {
                _overlayOpacity = Mathf.Clamp01(value);
                style.opacity = _overlayOpacity;
            }
        }

        // ── Constructor ───────────────────────────────────────────────────
        public ScanlineOverlay()
        {
            AddToClassList("ws-scanline-overlay");
            pickingMode = PickingMode.Ignore;
        }
    }
}
