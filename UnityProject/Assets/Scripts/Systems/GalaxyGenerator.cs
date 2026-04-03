// GalaxyGenerator — generates all sector data for a new game from a seed.
//
// Called once in GameManager.NewGame(); output written to StationState.sectors.
// On save/load: GalaxyGenerator is NOT re-run — sector data is loaded from SaveData.
//
// Generation steps:
//   1. Place home sector at (22, 51) with designation "GSC-NB 22.51 The Cradle".
//   2. Place remaining sectors via seeded Poisson disc sampling (min dist = 2.5 units).
//   3. Assign SurveyPrefix per coordinate rules (with 8 % ANC override).
//   4. Assign PhenomenonCodes (1 Primary + 0–2 Resource/Hazard, no incompatible pairs).
//   5. Generate ProperName from word pools with weighted pattern distribution.
//   6. Detect collisions within quadrants and append roman numeral suffixes.
//
// Feature flags:
//   GalaxyGenerator.Enabled                — skip galaxy generation when false
//   GalaxyGenerator.PhenomenonInfluenceEnabled — disable DK/ResourceBias side-effects
//   GalaxyGenerator.ProperNameGenerationEnabled — emit code+coord only when false
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class GalaxyGenerator
    {
        // ── Feature flags ──────────────────────────────────────────────────────

        /// <summary>When false, NewGame() skips sector generation entirely.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// When false, DK range reduction and ResourceBias writing are skipped.
        /// Codes are still generated and displayed.
        /// </summary>
        public static bool PhenomenonInfluenceEnabled { get; set; } = true;

        /// <summary>
        /// When false, sectors receive no proper name — only code + coordinate.
        /// </summary>
        public static bool ProperNameGenerationEnabled { get; set; } = true;

        // ── Generation constants ───────────────────────────────────────────────

        // Number of sectors to generate per galaxy (excluding home).
        private const int SectorCount = 80;

        // Galaxy coordinate space: 0–99.9 per axis.
        private const float CoordMin = 0f;
        private const float CoordMax = 99.9f;

        // Poisson disc minimum distance between any two sector centres.
        public const float PoissonMinDist = 2.5f;

        // Maximum iterations before falling back to grid placement (avoids infinite loop).
        private const int PoissonMaxAttempts = 10_000;

        // Adjacency threshold: sectors within this distance are considered neighbours.
        public const float NeighborThreshold = 5.0f;

        // Home sector canonical coordinates.
        public const float HomeX = 22f;
        public const float HomeY = 51f;

        // ANC prefix probability (approximately 8 % of sectors).
        private const float AncProbability = 0.08f;

        // ── Proper name word pools (Modifier + Noun, Proper + Noun, Modifier + Proper) ──

        private static readonly string[] PoolModifiers =
        {
            "Ashen", "Hollow", "Drifting", "Pale", "Sunken", "Fractured", "Silent",
            "Burning", "Frozen", "Forsaken", "Wandering", "Shattered", "Dim", "Veiled",
            "Ruined", "Ancient", "Bleeding", "Collapsed", "Eternal", "Fading",
            "Haunted", "Jagged", "Molten", "Nameless", "Obsidian", "Phantom",
            "Radiant", "Scarred", "Tideless", "Umbral", "Vacant", "Waning",
            "Abyssal", "Blighted", "Corroded", "Desolate", "Errant", "Feral",
            "Glacial", "Inert", "Kinetic", "Liminal", "Murky", "Necrotic",
            "Ruptured", "Spectral", "Tidal", "Volatile", "Withered", "Boreal",
            "Celestial", "Dusk", "Elysian", "Hadean", "Infernal", "Jovian",
            "Lunar", "Promethean", "Relic", "Stygian", "Titanic", "Voidborn",
            "Amber", "Basalt", "Crimson", "Dust", "Ember", "Flint",
            "Hoarfrost", "Iron", "Lacuna", "Mire", "Sundered", "Bleached",
            "Scorched", "Vast", "Bitter", "Sunless", "Deep", "Far", "Cold",
        };

        private static readonly string[] PoolNouns =
        {
            "Reach", "Expanse", "Belt", "Silence", "Ruin", "Threshold", "Passage",
            "Front", "Claim", "Dominion", "March", "Nebula", "Drift", "Corridor",
            "Void", "Hollow", "Terminus", "Wake", "Remnant", "Scar", "Cradle",
            "Gate", "Wound", "Shelf", "Veil", "Frontier", "Breach", "Shroud",
            "Rift", "Basin", "Verge", "Field", "Accord", "Trench", "Horizon",
            "Lament", "Column", "Pact", "Crucible", "Chain", "Forge", "Archive",
            "Vigil", "Swell", "Ledger", "Aegis", "Grave", "Covenant", "Atoll",
            "Wreckage", "Sovereign", "Dirge", "Mantle", "Conquest", "Sanctum",
            "Pyre", "Mandate", "Collapse", "Enclave", "Tide", "Fracture",
            "Legacy", "Exile", "Approach", "Boundary", "Current", "Edge",
            "Gap", "Haunt", "Inlet", "Junction", "Keep", "Margin", "Node",
            "Orbit", "Perimeter", "Quarter", "Range", "Span", "Transit", "Undertow",
        };

        // Phoneme pools for proper-name syllables
        private static readonly string[] PhonemeC1 =
        {
            "Cor", "Vel", "Sael", "Aer", "Vor", "Ely", "Cal", "Thal", "Sor", "Vaen",
            "Mer", "Ael", "Dor", "Iven", "Sol", "Cer", "Nael", "Ryn", "Oth", "Evan",
        };
        private static readonly string[] PhonemeV =
            { "a", "ae", "o", "oe", "ei", "au", "ue", "ia", "io", "ea" };
        private static readonly string[] PhonemeC2 =
        {
            "thr", "vyn", "lys", "ryn", "ven", "nor", "eth", "sar", "lon", "mir",
            "del", "than", "ral", "wen", "sor",
        };
        private static readonly string[] PhonemeE =
        {
            "is", "yn", "ael", "on", "ara", "iel", "orn", "ari", "oel", "yne",
            "ath", "ori", "eyn", "aen",
        };

        // Roman numerals for collision suffix.
        private static readonly string[] RomanNumerals =
            { "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        // ── Incompatible code pairs ────────────────────────────────────────────

        private static readonly (PhenomenonCode, PhenomenonCode)[] IncompatiblePairs =
        {
            (PhenomenonCode.VD, PhenomenonCode.OR),
            (PhenomenonCode.VD, PhenomenonCode.IC),
            (PhenomenonCode.VD, PhenomenonCode.GS),
            (PhenomenonCode.BH, PhenomenonCode.GI),
            (PhenomenonCode.PL, PhenomenonCode.DW),
        };

        // ── Primary / Resource / Hazard pools ─────────────────────────────────

        private static readonly PhenomenonCode[] PrimaryPool =
        {
            PhenomenonCode.NB, PhenomenonCode.PL, PhenomenonCode.BH,
            PhenomenonCode.DW, PhenomenonCode.GI, PhenomenonCode.MS,
        };

        private static readonly PhenomenonCode[] ResourcePool =
        {
            PhenomenonCode.OR, PhenomenonCode.IC, PhenomenonCode.GS, PhenomenonCode.VD,
        };

        private static readonly PhenomenonCode[] HazardPool =
        {
            PhenomenonCode.RD, PhenomenonCode.GV, PhenomenonCode.DK, PhenomenonCode.ST,
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Generate all sectors for a new game and populate
        /// <paramref name="station"/>.sectors and station.galaxySeed.
        /// </summary>
        public static void Generate(int seed, StationState station)
        {
            if (!Enabled) return;

            station.galaxySeed = seed;
            station.sectors.Clear();

            var rng = new Random(seed);

            // ── Step 1: place home sector ─────────────────────────────────────
            var homeCoords = new Vector2(HomeX, HomeY);
            var homeCodes  = new List<PhenomenonCode> { PhenomenonCode.NB };
            var homeData   = SectorData.Create(
                uid:         $"sector_{seed:x8}_0",
                coordinates: homeCoords,
                prefix:      SurveyPrefix.GSC,
                codes:       homeCodes,
                properName:  "The Cradle");
            homeData.discoveryState = SectorDiscoveryState.Visited;
            SectorGenerator.PopulateSectorFields(homeData, seed);
            station.sectors[homeData.uid] = homeData;

            // ── Step 2: Poisson disc placement for remaining sectors ───────────
            var positions = new List<Vector2> { homeCoords };
            int attempts  = 0;
            int index     = 1;

            while (positions.Count < SectorCount + 1 && attempts < PoissonMaxAttempts)
            {
                attempts++;
                float x = (float)(rng.NextDouble() * (CoordMax - CoordMin) + CoordMin);
                float y = (float)(rng.NextDouble() * (CoordMax - CoordMin) + CoordMin);
                var candidate = new Vector2(x, y);

                if (!IsFarEnoughFromAll(candidate, positions)) continue;

                // ── Assign survey prefix ──────────────────────────────────────
                var prefix = AssignPrefix(x, rng);

                // ── Assign phenomenon codes ───────────────────────────────────
                var codes = AssignPhenomenonCodes(rng);

                // ── Generate proper name ──────────────────────────────────────
                string name = ProperNameGenerationEnabled
                    ? GenerateProperName(rng)
                    : "";

                var sectorData = SectorData.Create(
                    uid:         $"sector_{seed:x8}_{index}",
                    coordinates: candidate,
                    prefix:      prefix,
                    codes:       codes,
                    properName:  name);
                sectorData.systemDensity = AssignSystemDensity(rng);
                sectorData.modifier      = AssignModifier(rng);
                SectorGenerator.PopulateSectorFields(sectorData, seed);

                positions.Add(candidate);
                station.sectors[sectorData.uid] = sectorData;
                index++;
            }

            if (attempts >= PoissonMaxAttempts)
                Debug.LogWarning("[GalaxyGenerator] Poisson disc hit max attempts; " +
                                 $"generated {positions.Count - 1}/{SectorCount} sectors.");

            // Grid fallback: if Poisson disc still underfilled the quota, place remaining
            // sectors on a uniform grid (skipping cells that violate the min-distance rule).
            if (positions.Count < SectorCount + 1)
            {
                float gridSide = Mathf.Ceil(Mathf.Sqrt(SectorCount - (positions.Count - 1)));
                float step     = (CoordMax - CoordMin) / gridSide;
                for (float gy = CoordMin + step * 0.5f; gy < CoordMax && positions.Count < SectorCount + 1; gy += step)
                {
                    for (float gx = CoordMin + step * 0.5f; gx < CoordMax && positions.Count < SectorCount + 1; gx += step)
                    {
                        var candidate = new Vector2(gx, gy);
                        if (!IsFarEnoughFromAll(candidate, positions)) continue;
                        var prefix     = AssignPrefix(gx, rng);
                        var codes      = AssignPhenomenonCodes(rng);
                        string name    = ProperNameGenerationEnabled ? GenerateProperName(rng) : "";
                        var sectorData = SectorData.Create(
                            uid:         $"sector_{seed:x8}_{index}",
                            coordinates: candidate,
                            prefix:      prefix,
                            codes:       codes,
                            properName:  name);
                        sectorData.systemDensity = AssignSystemDensity(rng);
                        sectorData.modifier      = AssignModifier(rng);
                        SectorGenerator.PopulateSectorFields(sectorData, seed);
                        positions.Add(candidate);
                        station.sectors[sectorData.uid] = sectorData;
                        index++;
                    }
                }
                Debug.LogWarning("[GalaxyGenerator] Grid fallback finished; " +
                                 $"total sectors placed: {positions.Count - 1}/{SectorCount}.");
            }

            // ── Step 3: Handle quadrant name collisions ───────────────────────
            if (ProperNameGenerationEnabled)
                ResolveNameCollisions(station);
        }

        /// <summary>
        /// Generate a single sector at explicit coordinates for interstellar unlocks.
        /// </summary>
        public static SectorData GenerateSectorAtCoordinates(int seed, Vector2 coordinates, StationState station)
        {
            int localSeed = seed ^ StableHash($"{coordinates.x:F2},{coordinates.y:F2}");
            var rng = new Random(localSeed);

            var prefix = AssignPrefix(coordinates.x, rng);
            var codes  = AssignPhenomenonCodes(rng);
            string name = ProperNameGenerationEnabled ? GenerateProperName(rng) : "";

            var sectorData = SectorData.Create(
                uid:         $"sector_{seed:x8}_{StableHash($"{coordinates.x:F2}_{coordinates.y:F2}"):x8}",
                coordinates: coordinates,
                prefix:      prefix,
                codes:       codes,
                properName:  name);
            sectorData.systemDensity = AssignSystemDensity(rng);
            sectorData.modifier      = AssignModifier(rng);
            SectorGenerator.PopulateSectorFields(sectorData, seed);
            return sectorData;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Assigns a <see cref="SystemDensity"/> tier using weighted random selection.
        /// Weights: Sparse 15 %, Low 30 %, Standard 40 %, High 15 %.
        /// </summary>
        private static SystemDensity AssignSystemDensity(Random rng)
        {
            double roll = rng.NextDouble();
            if (roll < 0.15) return SystemDensity.Sparse;
            if (roll < 0.45) return SystemDensity.Low;
            if (roll < 0.85) return SystemDensity.Standard;
            return SystemDensity.High;
        }

        // All modifier values except None, used for random selection.
        private static readonly SectorModifier[] ModifierPool =
        {
            SectorModifier.Nebula, SectorModifier.AsteroidBelt, SectorModifier.DustCloud,
            SectorModifier.PlanetaryRingDebris, SectorModifier.CometaryTail, SectorModifier.AccretionDisk,
            SectorModifier.PulsarWash, SectorModifier.MagnetarField, SectorModifier.GravitationalLens,
            SectorModifier.GravityWell, SectorModifier.TidalShearZone, SectorModifier.CosmicRaySurge,
            SectorModifier.RadiationBelt, SectorModifier.DarkMatterFilament, SectorModifier.FrameDraggingAnomaly,
            SectorModifier.GravitationalTimeDilation, SectorModifier.EinsteinRosenRemnant,
            SectorModifier.QuantumFoamPocket, SectorModifier.HawkingRadiationZone,
            SectorModifier.RichOreDeposit, SectorModifier.IceField, SectorModifier.GasPocket,
            SectorModifier.SalvageGraveyard, SectorModifier.DerelictStation, SectorModifier.AncientRuins,
            SectorModifier.BiologicalBloom,
            SectorModifier.ContestedSpace, SectorModifier.ExclusionZone,
            SectorModifier.QuarantineSeal, SectorModifier.PatrolRoute,
        };

        /// <summary>
        /// 40 % chance of a random modifier; otherwise None.
        /// </summary>
        private static SectorModifier AssignModifier(Random rng)
        {
            if (rng.NextDouble() >= 0.40) return SectorModifier.None;
            return ModifierPool[rng.Next(ModifierPool.Length)];
        }

        private static bool IsFarEnoughFromAll(Vector2 candidate, List<Vector2> existing)
        {
            float minSq = PoissonMinDist * PoissonMinDist;
            foreach (var pos in existing)
            {
                float dx = candidate.x - pos.x;
                float dy = candidate.y - pos.y;
                if (dx * dx + dy * dy < minSq) return false;
            }
            return true;
        }

        private static SurveyPrefix AssignPrefix(float x, Random rng)
        {
            // ANC is assigned procedurally to ~8 % of sectors regardless of position.
            if (rng.NextDouble() < AncProbability) return SurveyPrefix.ANC;

            if (x < 40f)  return SurveyPrefix.GSC;
            if (x <= 85f) return SurveyPrefix.FRN;  // 40 ≤ x ≤ 85 treated as frontier

            // Outer fringe (x > 85): stellar density decreases linearly with distance.
            // Sectors in denser patches retain FRN; sparse void regions become UNK.
            // densityFactor approaches 0 as x approaches CoordMax (99.9).
            float densityFactor = Mathf.Clamp01(1f - (x - 85f) / 15f);
            return rng.NextDouble() < densityFactor ? SurveyPrefix.FRN : SurveyPrefix.UNK;
        }

        private static List<PhenomenonCode> AssignPhenomenonCodes(Random rng)
        {
            var codes = new List<PhenomenonCode>();

            // ── Primary (exactly 1) ───────────────────────────────────────────
            codes.Add(PrimaryPool[rng.Next(PrimaryPool.Length)]);

            // ── Resource & Hazard (0–2 additional) ───────────────────────────
            // Combine both pools and draw without replacement.
            var secondary = new List<PhenomenonCode>();
            secondary.AddRange(ResourcePool);
            secondary.AddRange(HazardPool);

            int additionalCount = rng.Next(3);  // 0, 1, or 2
            var shuffled = ShuffleCopy(secondary, rng);
            foreach (var candidate in shuffled)
            {
                if (codes.Count >= 1 + additionalCount) break;
                if (!IsCompatible(codes, candidate)) continue;
                codes.Add(candidate);
            }

            return codes;
        }

        private static List<T> ShuffleCopy<T>(List<T> source, Random rng)
        {
            var copy = new List<T>(source);
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            return copy;
        }

        private static bool IsCompatible(List<PhenomenonCode> existing, PhenomenonCode candidate)
        {
            foreach (var (a, b) in IncompatiblePairs)
            {
                if ((existing.Contains(a) && candidate == b) ||
                    (existing.Contains(b) && candidate == a))
                    return false;
            }
            return true;
        }

        private static string GenerateProperName(Random rng)
        {
            double roll = rng.NextDouble();

            if (roll < 0.40)
            {
                // "The [Modifier] [Noun]"
                string m = PoolModifiers[rng.Next(PoolModifiers.Length)];
                string n = PoolNouns[rng.Next(PoolNouns.Length)];
                return $"The {m} {n}";
            }
            else if (roll < 0.60)
            {
                // "[Modifier] [Noun]"
                string m = PoolModifiers[rng.Next(PoolModifiers.Length)];
                string n = PoolNouns[rng.Next(PoolNouns.Length)];
                return $"{m} {n}";
            }
            else if (roll < 0.75)
            {
                // "The [Noun]"
                string n = PoolNouns[rng.Next(PoolNouns.Length)];
                return $"The {n}";
            }
            else if (roll < 0.88)
            {
                // "[ProperName] [Noun]" — Variant 3: C1+V+C2+E
                string proper = BuildProperName(rng);
                string n = PoolNouns[rng.Next(PoolNouns.Length)];
                return $"{proper} {n}";
            }
            else
            {
                // "[Modifier] [ProperName]" — exotic
                string m = PoolModifiers[rng.Next(PoolModifiers.Length)];
                string proper = BuildProperName(rng);
                return $"{m} {proper}";
            }
        }

        /// <summary>Generates a single proper-name word from the phoneme pools.</summary>
        private static string BuildProperName(Random rng)
        {
            int variant = rng.Next(4);
            string result;
            switch (variant)
            {
                case 0: // C1 + E
                    result = PhonemeC1[rng.Next(PhonemeC1.Length)] +
                             PhonemeE[rng.Next(PhonemeE.Length)];
                    break;
                case 1: // C1 + V + E
                    result = PhonemeC1[rng.Next(PhonemeC1.Length)] +
                             PhonemeV[rng.Next(PhonemeV.Length)] +
                             PhonemeE[rng.Next(PhonemeE.Length)];
                    break;
                case 2: // C1 + V + C2 + E
                    result = PhonemeC1[rng.Next(PhonemeC1.Length)] +
                             PhonemeV[rng.Next(PhonemeV.Length)] +
                             PhonemeC2[rng.Next(PhonemeC2.Length)] +
                             PhonemeE[rng.Next(PhonemeE.Length)];
                    break;
                default: // C1 + C2 + V + E
                    result = PhonemeC1[rng.Next(PhonemeC1.Length)] +
                             PhonemeC2[rng.Next(PhonemeC2.Length)] +
                             PhonemeV[rng.Next(PhonemeV.Length)] +
                             PhonemeE[rng.Next(PhonemeE.Length)];
                    break;
            }
            // Capitalise first letter
            return char.ToUpper(result[0]) + result[1..];
        }

        /// <summary>
        /// For sectors sharing a proper name within the same quadrant,
        /// append roman numeral suffixes starting at II to the duplicates.
        /// Cross-quadrant duplicates are intentionally allowed.
        /// </summary>
        private static void ResolveNameCollisions(StationState station)
        {
            // Group sectors by "quadrantKey:properName" key.
            var groups = new Dictionary<string, List<SectorData>>();
            foreach (var s in station.sectors.Values)
            {
                // The Cradle is unique by design — never suffix the home sector.
                if (string.IsNullOrEmpty(s.properName) || s.properName == "The Cradle") continue;
                string key = $"{s.QuadrantKey()}:{s.properName}";
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<SectorData>();
                list.Add(s);
            }

            foreach (var list in groups.Values)
            {
                if (list.Count <= 1) continue;
                // First entry keeps its name; subsequent entries get suffixes.
                for (int i = 1; i < list.Count; i++)
                {
                    string suffix = i <= RomanNumerals.Length
                        ? RomanNumerals[i - 1]
                        : (i + 1).ToString();
                    list[i].properName = $"{list[i].properName} {suffix}";
                }
            }
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return (int)(hash & int.MaxValue);
            }
        }
    }
}
