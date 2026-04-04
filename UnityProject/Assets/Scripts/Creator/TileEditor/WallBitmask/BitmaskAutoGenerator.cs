using System;
using UnityEngine;

namespace Waystation.Creator.TileEditor.WallBitmask
{
    public class BitmaskAutoGenerator
    {
        private readonly TileEditorController _controller;

        public BitmaskAutoGenerator(TileEditorController controller)
        {
            _controller = controller;
        }

        /// Returns true if we have the minimum key variants to auto-generate.
        /// Needs at least variant 0 (None), 3 (NS), and 12 (EW).
        public bool CanAutoGenerate()
        {
            return _controller.HasVariantContent(0) &&
                   _controller.HasVariantContent(3) &&
                   _controller.HasVariantContent(12);
        }

        /// Generates all 16 bitmask variants from the 3 key variants.
        /// Returns array of 16 Color32[] arrays (one per variant).
        public Color32[][] Generate()
        {
            if (!CanAutoGenerate()) return null;

            int w = _controller.Canvas.Width;
            int h = _controller.Canvas.Height;

            var nonePixels = _controller.GetVariantPixels(0);
            var nsPixels = _controller.GetVariantPixels(3);
            var ewPixels = _controller.GetVariantPixels(12);

            var nArm = ArmSegmentExtractor.ExtractNorthArm(nsPixels, w, h);
            var sArm = ArmSegmentExtractor.ExtractSouthArm(nsPixels, w, h);
            var eArm = ArmSegmentExtractor.ExtractEastArm(ewPixels, w, h);
            var wArm = ArmSegmentExtractor.ExtractWestArm(ewPixels, w, h);

            var results = new Color32[16][];

            for (int mask = 0; mask < 16; mask++)
            {
                var dest = new Color32[w * h];

                // Start with base (none) variant
                Array.Copy(nonePixels, dest, nonePixels.Length);

                // Composite arms
                ArmSegmentExtractor.CompositeArms(dest, w, h, nArm, sArm, eArm, wArm, mask);

                // Resolve junctions (vertical priority)
                JunctionResolver.ResolveJunction(dest, w, h, nsPixels, ewPixels, mask);

                results[mask] = dest;
            }

            return results;
        }

        /// Applies generated variants to the controller, optionally skipping variants that already have content.
        public void ApplyGenerated(Color32[][] variants, bool overwriteExisting = false)
        {
            if (variants == null || variants.Length != 16) return;

            for (int i = 0; i < 16; i++)
            {
                if (!overwriteExisting && _controller.HasVariantContent(i)) continue;
                _controller.SetVariantPixels(i, variants[i]);
            }
        }
    }
}
