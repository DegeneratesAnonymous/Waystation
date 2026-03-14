// StarfieldView — procedural twinkling star background + occasional shooting stars.
//
// Stars flicker using a dual-frequency sine wave so the brightness pattern is
// irregular rather than a smooth pulse.  Shooting stars spawn at random intervals,
// streak across the field, then vanish.
using UnityEngine;

namespace Waystation.View
{
    public class StarfieldView : MonoBehaviour
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const int   StarCount          = 260;
        private const float FieldHalf          = 55f;
        private const float MinScale           = 0.04f;
        private const float MaxScale           = 0.15f;
        private const float ZDepth             = 8f;

        // Shooting-star timing
        private const float ShootMinInterval   = 20f;   // seconds between events
        private const float ShootMaxInterval   = 60f;
        private const float ShootMinSpeed      = 22f;   // world-units / second
        private const float ShootMaxSpeed      = 48f;

        // ── Per-star data ─────────────────────────────────────────────────────
        private struct StarData
        {
            public SpriteRenderer sr;
            public float          speed;      // primary twinkle frequency
            public float          phase;
            public float          minAlpha;
            public float          maxAlpha;
        }

        private StarData[] _stars;

        // ── Shooting-star state ───────────────────────────────────────────────
        private GameObject    _shootGo;
        private SpriteRenderer _shootSr;
        private Vector3       _shootVel;
        private float         _shootStart;
        private float         _shootEnd;
        private float         _nextShootTime;

        // ── Sprites (shared) ─────────────────────────────────────────────────
        private static Sprite _starSprite;
        private static Sprite _shootSprite;

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
            // Generate shared sprites only once; reuse across scene reloads to
            // avoid leaking textures and wasting startup time.
            if (_starSprite  == null) _starSprite  = MakeStarSprite();
            if (_shootSprite == null) _shootSprite = MakeShootSprite();

            var rng  = new System.Random(7331);
            var root = new GameObject("Stars");

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
                sr.sprite       = _starSprite;
                sr.sortingOrder = -100;

                // Colour tint: warm giant / blue-white / plain white
                float tint = (float)rng.NextDouble();
                sr.color = tint < 0.08f ? new Color(1.00f, 0.80f, 0.55f)  // warm giant
                         : tint < 0.17f ? new Color(0.78f, 0.88f, 1.00f)  // blue-white
                         : Color.white;

                // Flicker params — wide alpha swing, faster speeds than before
                float minA = 0.04f + (float)rng.NextDouble() * 0.28f;
                float maxA = Mathf.Clamp01(minA + 0.55f + (float)rng.NextDouble() * 0.40f);

                _stars[i] = new StarData
                {
                    sr       = sr,
                    speed    = 1.5f + (float)rng.NextDouble() * 3.5f,   // 1.5 – 5.0 rad/s
                    phase    = (float)(rng.NextDouble() * System.Math.PI * 2.0),
                    minAlpha = minA,
                    maxAlpha = maxA,
                };
            }

            // First shooting star after a brief startup delay
            _nextShootTime = Time.time + Random.Range(4f, 12f);
        }

        private void OnDestroy()
        {
            if (_shootGo != null) Destroy(_shootGo);
        }

        // ── Update ────────────────────────────────────────────────────────────
        private void Update()
        {
            float t = Time.time;

            // Dual-frequency twinkle — irregular flicker
            for (int i = 0; i < _stars.Length; i++)
            {
                float spd  = _stars[i].speed;
                float ph   = _stars[i].phase;
                float w1   = Mathf.Sin(t * spd        + ph);
                float w2   = Mathf.Sin(t * spd * 2.3f + ph * 1.7f);
                float wave = (w1 * 0.65f + w2 * 0.35f) * 0.5f + 0.5f;  // 0..1

                float alpha = Mathf.Lerp(_stars[i].minAlpha, _stars[i].maxAlpha, wave);
                Color c = _stars[i].sr.color;
                _stars[i].sr.color = new Color(c.r, c.g, c.b, alpha);
            }

            // Shooting star
            if (_shootGo != null)
                TickShootingStar(t);
            else if (t >= _nextShootTime)
                SpawnShootingStar(t);
        }

        // ── Shooting star ─────────────────────────────────────────────────────
        private void SpawnShootingStar(float t)
        {
            // Direction: uniformly random angle (any direction)
            float angleDeg = Random.Range(0f, 360f);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            float speed    = Random.Range(ShootMinSpeed, ShootMaxSpeed);
            _shootVel = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0f) * speed;

            // Spawn somewhere in the central area so we see it cross the screen
            float sx = Random.Range(-FieldHalf * 0.5f, FieldHalf * 0.5f);
            float sy = Random.Range(-FieldHalf * 0.5f, FieldHalf * 0.5f);

            _shootStart = t;
            _shootEnd   = t + (FieldHalf * 1.8f) / speed;   // time to cross ~1.8× field width

            _shootGo = new GameObject("ShootingStar");
            _shootGo.transform.position   = new Vector3(sx, sy, ZDepth - 0.5f);
            _shootGo.transform.localScale = new Vector3(1.6f, 0.32f, 1f);  // elongated streak

            // Orient in direction of travel (sprite head faces right in local space)
            _shootGo.transform.rotation =
                Quaternion.Euler(0f, 0f, angleDeg);

            _shootSr = _shootGo.AddComponent<SpriteRenderer>();
            _shootSr.sprite       = _shootSprite;
            _shootSr.sortingOrder = -98;
            _shootSr.color        = new Color(1f, 1f, 1f, 0f);
        }

        private void TickShootingStar(float t)
        {
            _shootGo.transform.position += _shootVel * Time.deltaTime;

            float total     = _shootEnd - _shootStart;
            float elapsed   = t - _shootStart;
            float remaining = _shootEnd - t;

            // Fade in first 12 %, full brightness in middle, fade out last 18 %
            float fadeInEnd  = total * 0.12f;
            float fadeOutBeg = total * 0.82f;

            float alpha;
            if (elapsed < fadeInEnd)
                alpha = elapsed / fadeInEnd;
            else if (elapsed > fadeOutBeg)
                alpha = remaining / (total * 0.18f);
            else
                alpha = 1f;

            _shootSr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

            if (remaining <= 0f)
            {
                Destroy(_shootGo);
                _shootGo = null;
                _nextShootTime = t + Random.Range(ShootMinInterval, ShootMaxInterval);
            }
        }

        // ── Procedural sprites ────────────────────────────────────────────────

        // Soft-circle for background stars
        private static Sprite MakeStarSprite()
        {
            const int   res    = 16;
            const float radius = res * 0.42f;
            float       center = res * 0.5f;

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };

            for (int x = 0; x < res; x++)
                for (int y = 0; y < res; y++)
                {
                    float dx = x + 0.5f - center, dy = y + 0.5f - center;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);
                    float a  = Mathf.Clamp01((radius - d) / (radius * 0.5f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res),
                                 new Vector2(0.5f, 0.5f), res);
        }

        // Horizontal streak: transparent at left (tail), bright at right (head)
        // PPU = 16 so base world size = 4 × 0.25; scaled to ~6.4 × 0.08 world units
        private static Sprite MakeShootSprite()
        {
            const int sw = 64, sh = 4;
            var tex = new Texture2D(sw, sh, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };

            for (int x = 0; x < sw; x++)
                for (int y = 0; y < sh; y++)
                {
                    float tx  = x / (float)(sw - 1);          // 0 = tail, 1 = head
                    float ty  = (y + 0.5f) / sh;
                    float aX  = tx * tx * tx;                  // cubic — sharp bright head
                    float aY  = 1f - Mathf.Abs(ty * 2f - 1f); // soft vertical edges
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, aX * aY));
                }
            tex.Apply();
            // Pivot at head (right-centre) so streak trails behind the moving point
            return Sprite.Create(tex, new Rect(0, 0, sw, sh),
                                 new Vector2(1f, 0.5f), 16f);
        }
    }
}
