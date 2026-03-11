// StarfieldView — procedural twinkling star background.
// Spawns world-space star sprites in a large field behind the station.
// Each star independently oscillates its brightness for a subtle twinkle.
// Stars are fixed in world space so the field shifts gently as you pan,
// giving a mild parallax cue.
using UnityEngine;

namespace Waystation.View
{
    public class StarfieldView : MonoBehaviour
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const int   StarCount   = 260;
        private const float FieldHalf   = 55f;    // world-unit half-extent of field
        private const float MinScale    = 0.04f;
        private const float MaxScale    = 0.15f;
        private const float ZDepth      = 8f;     // behind everything (camera at z=-10)

        // ── Per-star data ─────────────────────────────────────────────────────
        private struct StarData
        {
            public SpriteRenderer sr;
            public float          speed;
            public float          phase;
            public float          minAlpha;
            public float          maxAlpha;
        }

        private StarData[] _stars;

        // ── Auto-install ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<StarfieldView>() != null) return;
            new GameObject("StarfieldView").AddComponent<StarfieldView>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            var rng  = new System.Random(7331);
            var root = new GameObject("Stars");
            var spr  = MakeStarSprite();

            _stars = new StarData[StarCount];

            for (int i = 0; i < StarCount; i++)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * FieldHalf;
                float y = (float)(rng.NextDouble() * 2.0 - 1.0) * FieldHalf;
                float s = MinScale + (float)rng.NextDouble() * (MaxScale - MinScale);

                var go = new GameObject($"Star{i}");
                go.transform.SetParent(root.transform);
                go.transform.position   = new Vector3(x, y, ZDepth);
                go.transform.localScale = new Vector3(s, s, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = spr;
                sr.sortingOrder = -100;

                // Slight warm/cool tint variation — most stars are blue-white
                float tint = (float)rng.NextDouble();
                Color baseColor;
                if (tint < 0.08f)
                    baseColor = new Color(1.00f, 0.80f, 0.60f); // warm giant
                else if (tint < 0.15f)
                    baseColor = new Color(0.80f, 0.90f, 1.00f); // blue-white
                else
                    baseColor = Color.white;
                sr.color = baseColor;

                // Twinkle parameters — vary speed and depth of fade
                float minA = 0.25f + (float)rng.NextDouble() * 0.35f;
                float maxA = minA  + 0.30f + (float)rng.NextDouble() * 0.35f;

                _stars[i] = new StarData
                {
                    sr       = sr,
                    speed    = 0.30f + (float)rng.NextDouble() * 1.40f,
                    phase    = (float)(rng.NextDouble() * System.Math.PI * 2.0),
                    minAlpha = minA,
                    maxAlpha = Mathf.Clamp01(maxA),
                };
            }
        }

        // ── Update — animate twinkle ──────────────────────────────────────────
        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < _stars.Length; i++)
            {
                float wave  = 0.5f + 0.5f * Mathf.Sin(t * _stars[i].speed + _stars[i].phase);
                float alpha = Mathf.Lerp(_stars[i].minAlpha, _stars[i].maxAlpha, wave);

                Color c = _stars[i].sr.color;
                _stars[i].sr.color = new Color(c.r, c.g, c.b, alpha);
            }
        }

        // ── Procedural soft-circle sprite ─────────────────────────────────────
        private static Sprite MakeStarSprite()
        {
            const int   res    = 16;
            const float radius = res * 0.42f;
            float       center = res * 0.5f;

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);
                    float a  = Mathf.Clamp01((radius - d) / (radius * 0.5f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a)); // squared = brighter core
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res),
                                 new Vector2(0.5f, 0.5f), res);
        }
    }
}
