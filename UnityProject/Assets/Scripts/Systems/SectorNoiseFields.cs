// SectorNoiseFields — five independent continuous noise fields sampled at sector
// grid coordinates.  Each field uses a different seed offset derived from the
// world seed so they are statistically independent.
//
// Uses a self-contained Simplex 2D implementation to avoid grid artefacts
// that Mathf.PerlinNoise exhibits at integer boundaries.

using UnityEngine;

namespace Waystation.Systems
{
    public static class SectorNoiseFields
    {
        // Seed offsets for each field — ensures statistical independence.
        private const int OffsetDensity         = 0;
        private const int OffsetResources       = 1000;
        private const int OffsetHazard          = 2000;
        private const int OffsetFactionPressure = 3000;
        private const int OffsetStellarAge      = 4000;

        // Frequency / scale of the noise — controls how quickly fields change
        // across grid cells.  Lower = smoother, broader regions.
        private const float NoiseScale = 0.12f;

        /// <summary>Sample a single named field at grid coordinates.</summary>
        public static float Sample(NoiseField field, int gridX, int gridY, int worldSeed)
        {
            int offset = field switch
            {
                NoiseField.Density         => OffsetDensity,
                NoiseField.Resources       => OffsetResources,
                NoiseField.Hazard          => OffsetHazard,
                NoiseField.FactionPressure => OffsetFactionPressure,
                NoiseField.StellarAge      => OffsetStellarAge,
                _ => 0,
            };
            return SampleRaw(gridX, gridY, worldSeed + offset);
        }

        /// <summary>Sample all five fields at once.</summary>
        public static Models.SectorNoiseValues SampleAll(int gridX, int gridY, int worldSeed)
        {
            return new Models.SectorNoiseValues
            {
                density         = SampleRaw(gridX, gridY, worldSeed + OffsetDensity),
                resources       = SampleRaw(gridX, gridY, worldSeed + OffsetResources),
                hazard          = SampleRaw(gridX, gridY, worldSeed + OffsetHazard),
                factionPressure = SampleRaw(gridX, gridY, worldSeed + OffsetFactionPressure),
                stellarAge      = SampleRaw(gridX, gridY, worldSeed + OffsetStellarAge),
            };
        }

        /// <summary>
        /// Returns a [0..1] noise value for grid position using Simplex 2D.
        /// Two octaves are blended (weight 0.6 / 0.4) for natural variation.
        /// </summary>
        private static float SampleRaw(int gridX, int gridY, int seed)
        {
            // Convert seed to a large but stable offset so different seeds
            // produce uncorrelated noise landscapes.
            float sx = seed * 0.7123f;
            float sy = seed * 0.3917f;

            float x = gridX * NoiseScale + sx;
            float y = gridY * NoiseScale + sy;

            // Two-octave blend.
            float n1 = Simplex2D(x, y);
            float n2 = Simplex2D(x * 2.17f + 5.3f, y * 2.17f + 7.1f);
            float raw = n1 * 0.6f + n2 * 0.4f;

            // Simplex2D returns roughly [-1..1]; remap to [0..1].
            return Mathf.Clamp01(raw * 0.5f + 0.5f);
        }

        // ── Simplex 2D noise (self-contained) ─────────────────────────────────
        // Adapted from the public-domain reference by Stefan Gustavson.

        private static readonly int[] Perm = GeneratePermutation();

        private static int[] GeneratePermutation()
        {
            int[] p = {
                151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,
                140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,
                247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,
                57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,
                74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,
                60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,
                65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,
                200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,
                52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,
                207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,
                119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,
                129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,
                218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,
                81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,
                184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,
                222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180,
            };
            int[] perm = new int[512];
            for (int i = 0; i < 512; i++)
                perm[i] = p[i & 255];
            return perm;
        }

        // Gradient vectors for 2D simplex (12 directions).
        private static readonly float[][] Grad2 =
        {
            new[]{ 1f, 1f}, new[]{-1f, 1f}, new[]{ 1f,-1f}, new[]{-1f,-1f},
            new[]{ 1f, 0f}, new[]{-1f, 0f}, new[]{ 0f, 1f}, new[]{ 0f,-1f},
            new[]{ 1f, 1f}, new[]{-1f, 1f}, new[]{ 1f,-1f}, new[]{-1f,-1f},
        };

        private const float F2 = 0.3660254037844386f;  // (sqrt(3)-1)/2
        private const float G2 = 0.2113248654051871f;  // (3-sqrt(3))/6

        private static float Simplex2D(float xin, float yin)
        {
            float s = (xin + yin) * F2;
            int i = FastFloor(xin + s);
            int j = FastFloor(yin + s);
            float t = (i + j) * G2;
            float x0 = xin - (i - t);
            float y0 = yin - (j - t);

            int i1, j1;
            if (x0 > y0) { i1 = 1; j1 = 0; }
            else          { i1 = 0; j1 = 1; }

            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;

            int ii = i & 255;
            int jj = j & 255;

            float n0 = Contribution(ii,      jj,      x0, y0);
            float n1 = Contribution(ii + i1, jj + j1, x1, y1);
            float n2 = Contribution(ii + 1,  jj + 1,  x2, y2);

            return 70f * (n0 + n1 + n2);
        }

        private static float Contribution(int ix, int iy, float x, float y)
        {
            float t = 0.5f - x * x - y * y;
            if (t < 0f) return 0f;
            t *= t;
            int gi = Perm[(ix + Perm[iy & 255]) & 255] % 12;
            return t * t * (Grad2[gi][0] * x + Grad2[gi][1] * y);
        }

        private static int FastFloor(float x) => x >= 0 ? (int)x : (int)x - 1;
    }

    public enum NoiseField
    {
        Density,
        Resources,
        Hazard,
        FactionPressure,
        StellarAge,
    }
}
