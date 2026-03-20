// AppearanceBeautyScorer — static scoring hooks for the colour harmony system.
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.NPC
{
    /// <summary>
    /// Provides heuristic beauty scoring for NPC clothing colour palettes.
    /// </summary>
    public static class AppearanceBeautyScorer
    {
        /// <summary>
        /// Scores how harmoniously the resolved slot colours across all layers work together.
        /// <list type="bullet">
        ///   <item>All colours within 30 hue degrees of each other → +5.0 (analogous harmony)</item>
        ///   <item>Any two colours complementary (~180° hue difference) → +3.0</item>
        ///   <item>Otherwise → 0.0</item>
        /// </list>
        /// </summary>
        public static float ScoreColourHarmony(
            ClothingLayerAppearance[] layers,
            DepartmentRegistry registry,
            string departmentId)
        {
            var resolved = ResolveAll(layers, registry, departmentId);
            if (resolved.Count < 2) return 0f;

            var hues = new List<float>();
            foreach (var c in resolved)
            {
                Color.RGBToHSV(c, out float h, out _, out _);
                hues.Add(h * 360f);
            }

            // Check analogous (all within 30°)
            bool analogous = true;
            for (int i = 1; i < hues.Count; i++)
            {
                if (HueDiff(hues[0], hues[i]) > 30f) { analogous = false; break; }
            }
            if (analogous) return 5f;

            // Check for any complementary pair (~180°)
            for (int i = 0; i < hues.Count; i++)
                for (int j = i + 1; j < hues.Count; j++)
                    if (Mathf.Abs(HueDiff(hues[i], hues[j]) - 180f) < 20f)
                        return 3f;

            return 0f;
        }

        /// <summary>
        /// Bonus for using DeptColour sources whose department colour is actually configured.
        /// Returns +2.0 per layer where at least one DeptColour slot resolves successfully.
        /// </summary>
        public static float ScoreDeptAlignment(
            ClothingLayerAppearance[] layers,
            DepartmentRegistry registry,
            string departmentId)
        {
            if (layers == null) return 0f;
            float score = 0f;
            foreach (var layer in layers)
            {
                if (layer?.slotColours == null) continue;
                foreach (var src in layer.slotColours)
                {
                    if (src is DeptColour && src.Resolve(registry, departmentId) != null)
                    {
                        score += 2f;
                        break; // one DeptColour hit per layer
                    }
                }
            }
            return score;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static List<Color> ResolveAll(
            ClothingLayerAppearance[] layers,
            DepartmentRegistry registry,
            string departmentId)
        {
            var result = new List<Color>();
            if (layers == null) return result;
            foreach (var layer in layers)
            {
                if (layer?.slotColours == null) continue;
                foreach (var src in layer.slotColours)
                {
                    var c = src?.Resolve(registry, departmentId);
                    if (c.HasValue) result.Add(c.Value);
                }
            }
            return result;
        }

        private static float HueDiff(float a, float b)
        {
            float diff = Mathf.Abs(a - b) % 360f;
            return diff > 180f ? 360f - diff : diff;
        }
    }
}
