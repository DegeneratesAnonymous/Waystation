// SolarSystemGenerator — produces a deterministic SolarSystemState from a station name seed.
//
// Call Generate() once in GameManager.NewGame() and store the result on Station.solarSystem.
// The same station name always produces the same solar system (stable FNV-1a hash, same
// as MapSystem), so saves / loads are consistent without needing to serialise the full tree.
using System;
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class SolarSystemGenerator
    {
        // ── Name tables ───────────────────────────────────────────────────────

        private static readonly string[] Prefixes =
        {
            "Ara", "Cor", "Dal", "Eth", "Fyr", "Gal", "Hel", "Ixo",
            "Jur", "Kel", "Lyr", "Myr", "Nox", "Orv", "Pel", "Quo",
            "Ret", "Sol", "Tar", "Ura", "Vex", "Wyr", "Xan", "Yel"
        };

        private static readonly string[] Suffixes =
        {
            "ius", "ara", "on", "ix", "us", "en", "or", "yx",
            "ith", "an", "el", "os", "ax", "ur", "ek", "yn"
        };

        private static readonly string[] Numerals =
            { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        // ── Star definitions: (type, colorHex, relativeSize, minPlanets, maxPlanets) ──

        private static readonly (StarType type, string color, float size, int minP, int maxP)[] StarDefs =
        {
            (StarType.RedDwarf,       "#FF6644", 0.55f, 2, 5),
            (StarType.YellowDwarf,    "#FFDD88", 0.90f, 3, 7),
            (StarType.BlueGiant,      "#88CCFF", 1.40f, 4, 8),
            (StarType.OrangeSubgiant, "#FF9955", 1.10f, 3, 6),
            (StarType.WhiteDwarf,     "#DDEEFF", 0.40f, 1, 4),
        };

        // ── Colour palettes ───────────────────────────────────────────────────

        private static readonly string[] RockyColors =
            { "#CC8866", "#BBAA99", "#AA9977", "#997755", "#BB9977" };

        private static readonly string[] GasColors   =
            { "#CCAA66", "#AABB88", "#99BBCC", "#7799AA", "#BBBB88" };

        private static readonly string[] IceColors   =
            { "#AACCDD", "#99BBCC", "#88AACC", "#7799BB", "#BBDDEE" };

        private static readonly string[] MoonColors  =
            { "#998877", "#AAAAAA", "#887766", "#BBBBAA" };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Generate a solar system deterministically from <paramref name="stationName"/>.
        /// Pass <paramref name="seedOverride"/> to force a specific seed (e.g. from the
        /// new-game seed input field).
        /// </summary>
        public static SolarSystemState Generate(string stationName, int? seedOverride = null)
        {
            int seed = seedOverride.HasValue ? seedOverride.Value : StableHash(stationName);
            var rng  = new Random(seed);

            var state = new SolarSystemState { seed = seed };

            // ── Star ──────────────────────────────────────────────────────────
            var starDef       = StarDefs[rng.Next(StarDefs.Length)];
            state.starType    = starDef.type;
            state.starColorHex = starDef.color;
            state.starSize    = starDef.size;
            state.systemName  = $"{PickName(rng)} System";

            // ── Body count & layout ───────────────────────────────────────────
            int planetCount = rng.Next(starDef.minP, starDef.maxP + 1);

            // One asteroid belt, placed somewhere in the middle third of the system.
            int beltSlot = rng.Next(Math.Max(1, planetCount / 3),
                                    Math.Max(2, (planetCount * 2) / 3));

            // Station orbit: which slot is the station closest to?
            state.stationOrbitIndex = rng.Next(0, planetCount);

            // First orbit starts at 1.5–2.2 AU-equivalents from the star.
            float baseOrbit = 1.5f + (float)rng.NextDouble() * 0.7f;

            for (int i = 0; i < planetCount; i++)
            {
                // Each successive orbit is 1.2–2.0 AU wider than the last
                // (loosely inspired by the Titius–Bode law).
                float radius = baseOrbit + i * (1.2f + (float)rng.NextDouble() * 0.8f);
                float phase  = (float)(rng.NextDouble() * Math.PI * 2.0);
                bool  isStation = (state.stationOrbitIndex == i);

                if (i == beltSlot)
                {
                    state.bodies.Add(MakeAsteroidBelt(state.systemName, radius, phase, isStation, rng));
                    continue;
                }

                // Zone fraction (0 = innermost, 1 = outermost) determines body type.
                float zone = (float)i / planetCount;
                SolarBody planet;

                if (zone < 0.35f)
                    planet = MakeRockyPlanet(i, state.systemName, radius, phase, rng);
                else if (zone < 0.65f)
                    planet = rng.NextDouble() < 0.5
                        ? MakeRockyPlanet(i, state.systemName, radius, phase, rng)
                        : MakeGasGiant  (i, state.systemName, radius, phase, rng);
                else
                    planet = rng.NextDouble() < 0.4
                        ? MakeGasGiant (i, state.systemName, radius, phase, rng)
                        : MakeIcePlanet(i, state.systemName, radius, phase, rng);

                planet.stationIsHere = isStation;

                // Moons — gas giants can have up to 4, rocky/ice up to 1.
                int maxMoons  = planet.bodyType == BodyType.GasGiant ? 4 : 1;
                int moonCount = rng.Next(0, maxMoons + 1);
                for (int m = 0; m < moonCount; m++)
                    planet.moons.Add(MakeMoon(planet, m, rng));

                state.bodies.Add(planet);
            }

            return state;
        }

        // ── Body factories ────────────────────────────────────────────────────

        private static SolarBody MakeRockyPlanet(int index, string sysName,
                                                  float radius, float phase, Random rng)
        {
            var p = new SolarBody
            {
                name          = $"{BaseName(sysName)} {Numerals[index % Numerals.Length]}",
                bodyType      = BodyType.RockyPlanet,
                orbitalRadius = radius,
                orbitalPeriod = OrbitalPeriod(radius),
                initialPhase  = phase,
                size          = 0.35f + (float)rng.NextDouble() * 0.45f,
                colorHex      = RockyColors[rng.Next(RockyColors.Length)],
                hasRings      = false,
            };
            if (rng.NextDouble() < 0.25) p.tags.Add("habitable");
            if (rng.NextDouble() < 0.35) p.tags.Add("rich_ore");
            if (rng.NextDouble() < 0.20) p.tags.Add("ancient_ruins");
            return p;
        }

        private static SolarBody MakeGasGiant(int index, string sysName,
                                               float radius, float phase, Random rng)
        {
            var p = new SolarBody
            {
                name          = $"{BaseName(sysName)} {Numerals[index % Numerals.Length]}",
                bodyType      = BodyType.GasGiant,
                orbitalRadius = radius,
                orbitalPeriod = OrbitalPeriod(radius),
                initialPhase  = phase,
                size          = 0.75f + (float)rng.NextDouble() * 0.65f,
                colorHex      = GasColors[rng.Next(GasColors.Length)],
                hasRings      = rng.NextDouble() < 0.30,
            };
            if (rng.NextDouble() < 0.55) p.tags.Add("gas_harvest");
            if (rng.NextDouble() < 0.30) p.tags.Add("storm_activity");
            return p;
        }

        private static SolarBody MakeIcePlanet(int index, string sysName,
                                                float radius, float phase, Random rng)
        {
            var p = new SolarBody
            {
                name          = $"{BaseName(sysName)} {Numerals[index % Numerals.Length]}",
                bodyType      = BodyType.IcePlanet,
                orbitalRadius = radius,
                orbitalPeriod = OrbitalPeriod(radius),
                initialPhase  = phase,
                size          = 0.30f + (float)rng.NextDouble() * 0.50f,
                colorHex      = IceColors[rng.Next(IceColors.Length)],
                hasRings      = rng.NextDouble() < 0.15,
            };
            if (rng.NextDouble() < 0.60) p.tags.Add("ice_deposits");
            if (rng.NextDouble() < 0.20) p.tags.Add("subsurface_ocean");
            return p;
        }

        private static SolarBody MakeAsteroidBelt(string sysName, float radius,
                                                   float phase, bool isStation, Random rng)
        {
            var belt = new SolarBody
            {
                name          = $"{BaseName(sysName)} Belt",
                bodyType      = BodyType.AsteroidBelt,
                orbitalRadius = radius,
                orbitalPeriod = OrbitalPeriod(radius),
                initialPhase  = phase,
                size          = 0f,          // rendered as a wide ring, not a dot
                colorHex      = "#887755",
                stationIsHere = isStation,
            };
            belt.tags.Add("rich_ore");
            if (rng.NextDouble() < 0.45) belt.tags.Add("ice_deposits");
            return belt;
        }

        private static SolarBody MakeMoon(SolarBody parent, int index, Random rng)
        {
            return new SolarBody
            {
                name          = $"{parent.name}-{(char)('a' + index)}",
                bodyType      = BodyType.RockyPlanet,
                orbitalRadius = parent.orbitalRadius, // moon inherits parent's orbit slot
                orbitalPeriod = OrbitalPeriod(parent.orbitalRadius * 0.18f),
                initialPhase  = (float)(rng.NextDouble() * Math.PI * 2.0),
                size          = parent.size * (0.12f + (float)rng.NextDouble() * 0.20f),
                colorHex      = MoonColors[rng.Next(MoonColors.Length)],
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Simplified Keplerian period: T ∝ r^1.5, scaled so r=1 gives 200 ticks.
        /// </summary>
        private static float OrbitalPeriod(float r)
            => 200f * (float)Math.Pow(Math.Max(r, 0.01), 1.5);

        private static string BaseName(string systemName)
            => systemName.Replace(" System", "");

        private static string PickName(Random rng)
            => Prefixes[rng.Next(Prefixes.Length)] + Suffixes[rng.Next(Suffixes.Length)];

        /// <summary>
        /// FNV-1a hash — stable across .NET runtime versions, matching MapSystem.
        /// </summary>
        [Obsolete("Use StableHash(string) directly.")]
        public static int StableHashPublic(string s) => StableHash(s);

        public static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in s) { hash ^= c; hash *= 16777619u; }
                return (int)(hash & int.MaxValue);
            }
        }

        // ── Neighbour generation (Sector / Galaxy maps) ───────────────────────

        private const int ChunkLY = 80;  // size of each galaxy chunk in light-years

        /// <summary>
        /// Procedurally generate neighbour star systems within <paramref name="radiusLY"/> light-years
        /// of the home system.  Results are deterministic: same globalSeed always produces the
        /// same neighbourhood.  Call with larger radii as the player pans the Galaxy Map.
        /// </summary>
        public static List<NeighborSystem> GenerateNeighbors(
            int globalSeed, float radiusLY,
            UnityEngine.Vector2 centreOffsetLY = default)
        {
            int cx0 = UnityEngine.Mathf.FloorToInt((centreOffsetLY.x - radiusLY) / ChunkLY);
            int cx1 = UnityEngine.Mathf.FloorToInt((centreOffsetLY.x + radiusLY) / ChunkLY);
            int cy0 = UnityEngine.Mathf.FloorToInt((centreOffsetLY.y - radiusLY) / ChunkLY);
            int cy1 = UnityEngine.Mathf.FloorToInt((centreOffsetLY.y + radiusLY) / ChunkLY);

            var result = new List<NeighborSystem>();
            float r2 = radiusLY * radiusLY;

            for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
            {
                bool isHomeChunk = (cx == 0 && cy == 0 &&
                    centreOffsetLY == UnityEngine.Vector2.zero);

                unchecked
                {
                    int chunkSeed = (int)((uint)(cx * 73856093) ^ (uint)(cy * 19349663)
                                         ^ (uint)globalSeed);
                    var rng = new Random(chunkSeed);
                    int count = rng.Next(1, 4);

                    for (int i = 0; i < count; i++)
                    {
                        float x = cx * ChunkLY + (float)rng.NextDouble() * ChunkLY;
                        float y = cy * ChunkLY + (float)rng.NextDouble() * ChunkLY;

                        float dx = x - centreOffsetLY.x;
                        float dy = y - centreOffsetLY.y;

                        // Consume RNG unconditionally so results are stable regardless of
                        // which candidates pass the distance filter.
                        var starDef    = StarDefs[rng.Next(StarDefs.Length)];
                        int sysSeed    = (int)((uint)chunkSeed ^ (uint)(i * 2654435769u));
                        string sysName = $"{PickName(rng)} System";

                        if (isHomeChunk && dx * dx + dy * dy < 4f) continue;
                        if (dx * dx + dy * dy > r2) continue;

                        result.Add(new NeighborSystem
                        {
                            systemName   = sysName,
                            seed         = sysSeed,
                            positionLY   = new UnityEngine.Vector2(x, y),
                            starType     = starDef.type,
                            starColorHex = starDef.color,
                            starSize     = starDef.size,
                        });
                    }
                }
            }
            return result;
        }
    }
}
