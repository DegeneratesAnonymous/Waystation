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

        private static readonly string[] Numerals =
            { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        // ── Phoneme pools for planet names ────────────────────────────────────

        private static readonly string[] PlanetV1 =
        {
            "Ar", "Vel", "Sol", "Cor", "Aer", "Vor", "Kel", "Nal",
            "Tyr", "Ish", "Orn", "El", "Mer", "Tar", "Syl", "Dal",
        };
        private static readonly string[] PlanetL  =
        {
            "a", "e", "i", "o", "u", "ae", "io", "ei", "ou", "au",
        };
        private static readonly string[] PlanetV2 =
        {
            "dar", "lon", "ris", "ven", "nar", "than", "sol", "mir",
            "thal", "von", "sar", "del", "ran", "wyn", "aen",
        };
        private static readonly string[] PlanetE  =
        {
            "is", "yn", "ael", "on", "ara", "iel", "orn", "ari",
            "ath", "ori", "eyn", "aen",
        };

        /// <summary>
        /// Generates a soft-phoneme planet name using V1 + L + V2 + E.
        /// </summary>
        private static string PickPlanetName(Random rng)
        {
            string raw = PlanetV1[rng.Next(PlanetV1.Length)]
                       + PlanetL [rng.Next(PlanetL.Length)]
                       + PlanetV2[rng.Next(PlanetV2.Length)]
                       + PlanetE [rng.Next(PlanetE.Length)];
            return char.ToUpper(raw[0]) + raw[1..];
        }

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
            state.systemName  = $"{PickPlanetName(rng)} System";

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
                    planet = MakeRockyPlanet(i, state.systemName, radius, phase, zone, rng);
                else if (zone < 0.65f)
                    planet = rng.NextDouble() < 0.5
                        ? MakeRockyPlanet(i, state.systemName, radius, phase, zone, rng)
                        : MakeGasGiant  (i, state.systemName, radius, phase, zone, rng);
                else
                    planet = rng.NextDouble() < 0.4
                        ? MakeGasGiant (i, state.systemName, radius, phase, zone, rng)
                        : MakeIcePlanet(i, state.systemName, radius, phase, zone, rng);

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
                                                  float radius, float phase, float zone, Random rng)
        {
            // Assign planet class: inner hot zone → T2/T3, mid → T4/T5, outer → T6/T7;
            // small chance of exotic (E-class) anywhere.
            PlanetClass pClass;
            if (rng.NextDouble() < 0.06)
            {
                var exotics = new[] { PlanetClass.E1_Chthonian, PlanetClass.E2_CarbonPlanet,
                                      PlanetClass.E3_IronPlanet, PlanetClass.E4_HeliumPlanet,
                                      PlanetClass.E5_RogueBody };
                pClass = exotics[rng.Next(exotics.Length)];
            }
            else if (zone < 0.20f)
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.T2_Volcanic    : PlanetClass.T1_BarrenRock;
            else if (zone < 0.40f)
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.T3_Desert      : PlanetClass.T4_Tectonic;
            else if (zone < 0.60f)
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.T5_Oceanic     : PlanetClass.T6_Terran;
            else
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.T7_Frozen      : PlanetClass.T4_Tectonic;

            var p = new SolarBody
            {
                name          = PickPlanetName(rng),
                bodyType      = BodyType.RockyPlanet,
                planetClass   = pClass,
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
                                               float radius, float phase, float zone, Random rng)
        {
            // Zone-based gas giant class:
            // inner hot → G4/G5 (alkali metal / silicate cloud)
            // mid       → G2/G3 (water cloud / cloudless)
            // outer     → G1    (ammonia cloud)
            PlanetClass pClass;
            if (zone < 0.35f)
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.G4_AlkaliMetal   : PlanetClass.G5_SilicateCloud;
            else if (zone < 0.65f)
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.G2_WaterCloud    : PlanetClass.G3_Cloudless;
            else
                pClass = rng.NextDouble() < 0.7 ? PlanetClass.G1_AmmoniaCloud  : PlanetClass.G2_WaterCloud;

            var p = new SolarBody
            {
                name          = PickPlanetName(rng),
                bodyType      = BodyType.GasGiant,
                planetClass   = pClass,
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
                                                float radius, float phase, float zone, Random rng)
        {
            // Outer zone → I3 (cometary); mid-outer → I1/I2
            PlanetClass pClass;
            if (zone > 0.85f)
                pClass = PlanetClass.I3_CometaryBody;
            else if (zone > 0.65f)
                pClass = rng.NextDouble() < 0.5 ? PlanetClass.I1_IceDwarf : PlanetClass.I2_CryogenicMoon;
            else
                pClass = PlanetClass.I1_IceDwarf;

            var p = new SolarBody
            {
                name          = PickPlanetName(rng),
                bodyType      = BodyType.IcePlanet,
                planetClass   = pClass,
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
        /// Keplerian period scaled so that r = 1 AU (Earth-like orbit) completes
        /// one revolution in exactly 365 in-game days (365 × TimeSystem.TicksPerDay ticks).
        /// </summary>
        private static float OrbitalPeriod(float r)
            => 365f * TimeSystem.TicksPerDay * (float)Math.Pow(Math.Max(r, 0.01), 1.5);

        private static string BaseName(string systemName)
            => systemName.Replace(" System", "");

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
                        string sysName = $"{PickPlanetName(rng)} System";

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
