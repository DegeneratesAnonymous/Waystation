// BeautyScorer — calculates the beauty score for a ClothingTemplate.
//
// Beauty score model:
//   • Base points per enabled layer with a variant assigned.
//   • Bonus for explicit colour usage (demonstrates intentional design).
//   • Harmony bonus when explicit colours on a template form a recognised
//     colour-harmony pattern (complementary, analogous, triadic, etc.).
//   • Penalty for layers left at MaterialDefault on all slots.
//
// Scores are in [0, 100].  The scorer is stateless and safe to call from
// any thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Systems
{
    using Waystation.Models;

    public static class BeautyScorer
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const float BasePerLayer         = 6f;
        private const float ExplicitColourBonus  = 3f;  // per slot with an explicit colour
        private const float DeptColourBonus      = 2f;  // per slot using dept colour
        private const float HarmonyBonus         = 10f;
        private const float AllDefaultPenalty    = 4f;  // per layer where every slot is default

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Calculates and returns a beauty score in [0, 100] for the given template.
        /// Also updates template.beautyScore and template.inherentValue.
        /// </summary>
        public static float Score(ClothingTemplate template)
        {
            if (template == null) return 0f;

            float score = 0f;
            var explicitHues = new List<float>();

            foreach (var layer in template.layers)
            {
                if (!layer.enabled || string.IsNullOrEmpty(layer.variantId)) continue;

                score += BasePerLayer;

                bool allDefault = true;

                foreach (var binding in layer.colourBindings)
                {
                    var src = binding.source;
                    if (src == null) continue;

                    switch (src.type)
                    {
                        case ColourSourceType.Explicit:
                            score += ExplicitColourBonus;
                            allDefault = false;
                            if (src.TryGetExplicit(out Color c))
                            {
                                Color.RGBToHSV(c, out float h, out _, out _);
                                explicitHues.Add(h);
                            }
                            break;

                        case ColourSourceType.DeptColour:
                            score += DeptColourBonus;
                            allDefault = false;
                            break;

                        case ColourSourceType.MaterialDefault:
                            break;
                    }
                }

                if (allDefault && layer.colourBindings.Count > 0)
                    score -= AllDefaultPenalty;
            }

            // Harmony bonus
            if (explicitHues.Count >= 2 && IsHarmonic(explicitHues))
                score += HarmonyBonus;

            float final = Mathf.Clamp(score, 0f, 100f);
            template.beautyScore   = final;
            template.inherentValue = Mathf.RoundToInt(final * 0.5f + template.layers.Count * 2);
            return final;
        }

        // ── Harmony analysis ──────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the supplied hues (each in [0,1]) conform to at least
        /// one of: complementary (±0.5), analogous (all within 0.12), or triadic
        /// (three hues each ~0.33 apart).
        /// </summary>
        private static bool IsHarmonic(List<float> hues)
        {
            if (hues.Count < 2) return false;

            // Complementary: any pair ~0.5 apart on the wheel.
            for (int i = 0; i < hues.Count - 1; i++)
                for (int j = i + 1; j < hues.Count; j++)
                    if (HueDist(hues[i], hues[j]) < 0.08f) return false; // identical — not harmonic

            // Analogous: all hues within a 0.12 arc.
            float min = float.MaxValue, max = float.MinValue;
            foreach (float h in hues) { if (h < min) min = h; if (h > max) max = h; }
            if (max - min <= 0.12f) return true;

            // Complementary pair.
            foreach (float a in hues)
                foreach (float b in hues)
                    if (Math.Abs(a - b) > 0.01f && Math.Abs(HueDist(a, b) - 0.5f) < 0.07f)
                        return true;

            // Triadic: three hues each approximately 1/3 of the wheel apart.
            if (hues.Count >= 3)
            {
                foreach (float a in hues)
                    foreach (float b in hues)
                        foreach (float c in hues)
                        {
                            if (Math.Abs(a - b) < 0.01f || Math.Abs(a - c) < 0.01f) continue;
                            float d1 = HueDist(a, b), d2 = HueDist(b, c), d3 = HueDist(c, a);
                            if (Math.Abs(d1 - 0.333f) < 0.07f &&
                                Math.Abs(d2 - 0.333f) < 0.07f &&
                                Math.Abs(d3 - 0.333f) < 0.07f)
                                return true;
                        }
            }

            return false;
        }

        private static float HueDist(float a, float b)
        {
            float d = Math.Abs(a - b);
            return d > 0.5f ? 1f - d : d;
        }

        // ── Palette generation helpers ────────────────────────────────────────

        /// <summary>
        /// Generates a harmonious random palette of <paramref name="count"/> colours.
        /// The palette conforms to a random harmony rule (analogous, complementary, or triadic).
        /// </summary>
        public static List<Color> RandomHarmonicPalette(int count, System.Random rng = null)
        {
            rng ??= new System.Random();
            float baseHue = (float)rng.NextDouble();
            float sat     = 0.55f + (float)rng.NextDouble() * 0.35f;
            float val     = 0.60f + (float)rng.NextDouble() * 0.30f;

            int rule = rng.Next(3);   // 0=analogous, 1=complementary, 2=triadic
            var hues = new List<float>();

            switch (rule)
            {
                case 0: // analogous
                    for (int i = 0; i < count; i++)
                        hues.Add(Mathf.Repeat(baseHue + i * 0.06f, 1f));
                    break;
                case 1: // complementary
                    for (int i = 0; i < count; i++)
                        hues.Add(Mathf.Repeat(baseHue + (i % 2 == 0 ? 0 : 0.5f) + i * 0.04f, 1f));
                    break;
                case 2: // triadic
                    for (int i = 0; i < count; i++)
                        hues.Add(Mathf.Repeat(baseHue + (i % 3) * 0.333f + i * 0.02f, 1f));
                    break;
            }

            var colours = new List<Color>();
            for (int i = 0; i < count; i++)
                colours.Add(Color.HSVToRGB(hues[i % hues.Count],
                                           sat + (float)rng.NextDouble() * 0.1f - 0.05f,
                                           val + (float)rng.NextDouble() * 0.1f - 0.05f));
            return colours;
        }

        /// <summary>
        /// "Magic Palette" — given the explicit colours already assigned in the template,
        /// infers the dominant harmony rule and fills unassigned slots to complete it.
        /// Returns the list of (slotName, colour) pairs that were assigned.
        /// </summary>
        public static List<(string slotName, Color colour)> MagicPalette(
            ClothingLayerAppearance layer,
            List<AtlasColourSlot>   atlasSlots,
            System.Random           rng = null)
        {
            rng ??= new System.Random();
            var assigned = new List<(string, Color)>();

            // Collect existing explicit hues.
            var existingHues = new List<float>();
            foreach (var slot in atlasSlots)
            {
                var src = layer.GetSlotSource(slot.name);
                if (src.type == ColourSourceType.Explicit && src.TryGetExplicit(out Color ec))
                {
                    Color.RGBToHSV(ec, out float h, out _, out _);
                    existingHues.Add(h);
                }
            }

            if (existingHues.Count == 0) return assigned;

            float baseHue = existingHues[0];
            float sat = 0.60f, val = 0.70f;

            int slotIndex = 0;
            foreach (var slot in atlasSlots)
            {
                var src = layer.GetSlotSource(slot.name);
                if (src.type == ColourSourceType.MaterialDefault)
                {
                    float h = Mathf.Repeat(baseHue + (slotIndex + 1) * 0.5f, 1f);
                    Color c = Color.HSVToRGB(h, sat, val);
                    layer.SetSlotSource(slot.name, ColourSource.Explicit(c));
                    assigned.Add((slot.name, c));
                }
                slotIndex++;
            }

            return assigned;
        }
    }
}
