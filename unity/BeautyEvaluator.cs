// BeautyEvaluator — hooks for evaluating colour harmony in a clothing/hair
// appearance. Used by the beauty scoring system to reward visually coherent
// NPC outfits.
//
// Evaluation model:
//   • Monochromatic harmony : all resolved colours share the same hue (within
//     a tolerance), varying only in saturation and value.  Score: high.
//   • Analogous harmony     : resolved colours span ≤60° on the hue wheel.
//                             Score: medium-high.
//   • Complementary harmony : two hue groups roughly opposite on the wheel
//                             (±30° of 180° apart).  Score: medium.
//   • Neutral outfit        : no slots resolved to explicit/dept colours.
//                             Score: 0 (no penalty, no bonus).
//   • Clashing              : colours that don't fit the above.  Score: small
//                             negative penalty.
//
// BeautyEvaluator.Evaluate() returns a float delta suitable for passing into
// the existing MoodSystem.PushModifier API or a standalone beauty score tally.
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.NPC
{
    public static class BeautyEvaluator
    {
        // ── Configurable thresholds ───────────────────────────────────────────

        /// <summary>Hue tolerance (degrees) for monochromatic classification.</summary>
        public const float MonoHueTolerance = 15f;

        /// <summary>Hue spread (degrees) for analogous classification.</summary>
        public const float AnalogousHueSpread = 60f;

        /// <summary>Hue tolerance (degrees) around a complementary pair midpoint.</summary>
        public const float ComplementaryTolerance = 30f;

        /// <summary>
        /// Minimum saturation (0-1) for a colour to be considered chromatic rather
        /// than achromatic (grey/white/black). Greys contribute no hue information to
        /// harmony evaluation.  0.15 corresponds to a faint tint; anything below is
        /// treated as neutral.
        /// </summary>
        private const float AchromaticSaturationThreshold = 0.15f;

        // ── Score constants ───────────────────────────────────────────────────

        public const float ScoreMonochromatic  =  3f;
        public const float ScoreAnalogous      =  2f;
        public const float ScoreComplementary  =  1f;
        public const float ScoreNeutral        =  0f;
        public const float ScoreClashing       = -1f;

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Evaluates the colour harmony of an NPC appearance and returns a
        /// beauty score delta.
        /// </summary>
        /// <param name="appearance">The NPC appearance to evaluate.</param>
        /// <param name="departmentId">The NPC's department UID (may be null).</param>
        /// <param name="deptRegistry">
        /// Department registry for resolving DeptColour sources (may be null).
        /// </param>
        /// <returns>
        /// A float beauty score delta: positive = harmonious, negative = clashing,
        /// zero = neutral outfit.
        /// </returns>
        public static float Evaluate(
            NpcAppearance appearance,
            string departmentId,
            DepartmentRegistry deptRegistry)
        {
            if (appearance == null) return ScoreNeutral;

            var resolvedColours = CollectResolvedColours(appearance, departmentId, deptRegistry);

            if (resolvedColours.Count == 0)
                return ScoreNeutral;

            return ClassifyHarmony(resolvedColours);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Collects all non-null resolved colours from all clothing/hair colour
        /// slots in the appearance.
        /// </summary>
        private static List<Color> CollectResolvedColours(
            NpcAppearance appearance,
            string departmentId,
            DepartmentRegistry deptRegistry)
        {
            var colours = new List<Color>();

            CollectFromLayer(appearance.hair,     departmentId, deptRegistry, colours);
            CollectFromLayer(appearance.hat,      departmentId, deptRegistry, colours);
            CollectFromLayer(appearance.shirt,    departmentId, deptRegistry, colours);
            CollectFromLayer(appearance.pants,    departmentId, deptRegistry, colours);
            CollectFromLayer(appearance.shoes,    departmentId, deptRegistry, colours);
            CollectFromLayer(appearance.backItem, departmentId, deptRegistry, colours);
            CollectFromLayer(appearance.weapon,   departmentId, deptRegistry, colours);

            return colours;
        }

        private static void CollectFromLayer(
            ClothingLayerAppearance layer,
            string departmentId,
            DepartmentRegistry deptRegistry,
            List<Color> output)
        {
            if (layer == null) return;
            foreach (var src in layer.slotColours)
            {
                if (src == null) continue;
                Color? resolved = src.Resolve(departmentId, deptRegistry);
                if (resolved.HasValue)
                    output.Add(resolved.Value);
            }
        }

        /// <summary>
        /// Classifies a list of resolved colours into a harmony category and
        /// returns the corresponding score delta.
        /// </summary>
        private static float ClassifyHarmony(List<Color> colours)
        {
            // Extract HSV hues for chromatic colours (skip near-achromatic)
            var hues = new List<float>();
            foreach (var c in colours)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (s > AchromaticSaturationThreshold) // skip greys/whites
                    hues.Add(h * 360f);
            }

            if (hues.Count == 0)
                return ScoreNeutral; // all achromatic — neutral

            if (hues.Count == 1)
                return ScoreMonochromatic; // single chromatic hue — trivially monochromatic

            float minHue = hues[0], maxHue = hues[0];
            foreach (float h in hues)
            {
                if (h < minHue) minHue = h;
                if (h > maxHue) maxHue = h;
            }
            float spread = maxHue - minHue;

            // Wrap-around: e.g. hues at 350° and 10° have spread=340° raw, but
            // the actual wrap distance is 20°.
            float wrapSpread = 360f - spread;
            float effectiveSpread = Mathf.Min(spread, wrapSpread);

            if (effectiveSpread <= MonoHueTolerance)
                return ScoreMonochromatic;

            if (effectiveSpread <= AnalogousHueSpread)
                return ScoreAnalogous;

            // Complementary check: two groups roughly opposite (±ComplementaryTolerance around 180°)
            if (IsComplementary(hues))
                return ScoreComplementary;

            return ScoreClashing;
        }

        /// <summary>
        /// Returns true if the hue set can be split into two complementary groups
        /// (centroids ~180° apart, each group spread ≤ ComplementaryTolerance).
        /// </summary>
        private static bool IsComplementary(List<float> hues)
        {
            if (hues.Count < 2) return false;

            // Try each hue as a reference and look for a complementary partner group.
            foreach (float anchor in hues)
            {
                float targetMin = (anchor + 180f - ComplementaryTolerance) % 360f;
                float targetMax = (anchor + 180f + ComplementaryTolerance) % 360f;
                int   inTarget  = 0;
                foreach (float h in hues)
                {
                    if (HueInRange(h, targetMin, targetMax))
                        inTarget++;
                }
                // At least one hue must be in the complementary band and one in the anchor band.
                if (inTarget > 0 && inTarget < hues.Count)
                    return true;
            }
            return false;
        }

        private static bool HueInRange(float h, float min, float max)
        {
            if (min <= max)
                return h >= min && h <= max;
            // Wraps around 360°
            return h >= min || h <= max;
        }
    }
}
