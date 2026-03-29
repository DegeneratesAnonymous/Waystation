// FactionProceduralGenerator — procedural faction generation on sector unlock.
//
// Factions are generated when a sector transitions from Uncharted → Detected or Visited.
// Generation probability is modulated by:
//   • Regional resource profile (PhenomenonCodes: resource codes raise prob, hazard codes lower it)
//   • Adjacent faction density (saturation penalty per existing nearby faction)
//   • System density of the sector (denser sectors are more likely to host factions)
//
// Government type is derived from the dominant trait category of the faction's initial NPC pool
// using FactionProceduralGenerator.DeriveGovernmentTypeFromAggregate().
//
// Trait gravity: government type is stored on the faction and passed as a bias parameter
// to NPCSystem.SpawnWithGovernmentBias() for all NPCs associated with that faction.
//
// Starting scenario: InitializeStartingFactions() plants exactly two factions in adjacent
// sectors — one friendly (rep +45) and one unfriendly (rep -45) — regardless of the
// FactionProceduralGeneration feature flag.
//
// Gated by FeatureFlags.FactionProceduralGeneration (starting factions always seeded).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class FactionProceduralGenerator
    {
        // ── Generation probability constants ──────────────────────────────────

        /// <summary>Base probability of a faction generating for any given sector unlock.</summary>
        public const float BaseGenerationProbability = 0.35f;

        /// <summary>Added to base probability per resource phenomenon code present (OR/IC/GS).</summary>
        public const float ResourceCodeBonus = 0.15f;

        /// <summary>Subtracted from probability per hazard phenomenon code present (RD/GV/DK/ST).</summary>
        public const float HazardCodePenalty = 0.10f;

        /// <summary>Added to probability when the sector has a resource-flavoured SectorModifier.</summary>
        public const float ResourceModifierBonus = 0.10f;

        /// <summary>Subtracted for each faction already present in adjacent sectors (density saturation).</summary>
        public const float AdjacentFactionDensityPenalty = 0.12f;

        /// <summary>Maximum number of adjacent factions considered for the density penalty.</summary>
        public const int MaxAdjacentFactionsCounted = 3;

        /// <summary>Bonus to probability when the sector has SystemDensity.High.</summary>
        public const float HighDensityBonus = 0.10f;

        // ── Starting scenario constants ───────────────────────────────────────

        /// <summary>Reputation seeded for the starting friendly faction.</summary>
        public const float StartingFriendlyRep = 45f;

        /// <summary>Reputation seeded for the starting unfriendly faction.</summary>
        public const float StartingUnfriendlyRep = -45f;

        /// <summary>Number of NPCs spawned in each generated faction's initial pool.</summary>
        public const int InitialNpcPoolSize = 5;

        // ── Government type shift threshold ───────────────────────────────────

        /// <summary>
        /// Minimum change in dominant-category score required to trigger a government
        /// type recalculation.  Compared against the previously stored category scores.
        /// </summary>
        public const float GovernmentShiftThreshold = 0.30f;

        // ── Government type → preferred trait categories (trait gravity) ──────

        private static readonly Dictionary<GovernmentType, List<TraitCategory>> GovTraitAffinity =
            new Dictionary<GovernmentType, List<TraitCategory>>
        {
            { GovernmentType.Democracy,       new List<TraitCategory> { TraitCategory.Social,       TraitCategory.Ideological  } },
            { GovernmentType.Republic,        new List<TraitCategory> { TraitCategory.Ideological,  TraitCategory.Social       } },
            { GovernmentType.Monarchy,        new List<TraitCategory> { TraitCategory.Psychological, TraitCategory.Ideological } },
            { GovernmentType.Authoritarian,   new List<TraitCategory> { TraitCategory.Psychological, TraitCategory.Physical    } },
            { GovernmentType.CorporateVassal, new List<TraitCategory> { TraitCategory.Economic,     TraitCategory.Social       } },
            { GovernmentType.Pirate,          new List<TraitCategory> { TraitCategory.Physical,     TraitCategory.Psychological} },
        };

        // ── Faction name word pools ───────────────────────────────────────────

        private static readonly string[] NameAdjectives =
        {
            "Iron", "Crimson", "Pale", "Silent", "Burning", "Void", "Amber", "Ashen",
            "Ember", "Hollow", "Shattered", "Deep", "Far", "Cold", "Ancient",
            "Fractured", "Wandering", "Eternal", "Obsidian", "Radiant",
            "United", "Free", "New", "Inner", "Outer",
        };

        private static readonly string[] NameBodies =
        {
            "Assembly", "Compact", "Collective", "Union", "Coalition", "Order",
            "Syndicate", "Front", "League", "Pact", "Alliance", "Domain",
            "Accord", "Consortium", "Authority", "Directorate", "Enclave",
            "Federation", "Covenant", "Conclave", "Sovereignty", "Charter",
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the generation probability for a sector unlock, taking into account
        /// the sector's resource profile, hazard codes, system density modifier, and the
        /// density of existing factions in adjacent sectors.
        /// </summary>
        /// <param name="sector">The sector that was just unlocked.</param>
        /// <param name="adjacentSectors">Sectors within NeighborThreshold of <paramref name="sector"/>.</param>
        /// <param name="allFactions">Combined registry + generated factions.</param>
        public static float ComputeGenerationProbability(
            SectorData sector,
            IEnumerable<SectorData> adjacentSectors,
            Dictionary<string, FactionDefinition> allFactions)
        {
            float prob = BaseGenerationProbability;

            // Resource codes raise probability
            foreach (var code in sector.phenomenonCodes)
            {
                if (code == PhenomenonCode.OR || code == PhenomenonCode.IC || code == PhenomenonCode.GS)
                    prob += ResourceCodeBonus;
                else if (code == PhenomenonCode.VD)
                    prob -= ResourceCodeBonus * 0.5f; // void slightly suppresses
            }

            // Hazard codes lower probability
            foreach (var code in sector.phenomenonCodes)
            {
                if (code == PhenomenonCode.RD || code == PhenomenonCode.GV ||
                    code == PhenomenonCode.DK || code == PhenomenonCode.ST)
                    prob -= HazardCodePenalty;
            }

            // Resource-flavoured modifier gives a bonus
            if (sector.modifier == SectorModifier.RichOreDeposit ||
                sector.modifier == SectorModifier.IceField        ||
                sector.modifier == SectorModifier.GasPocket       ||
                sector.modifier == SectorModifier.SalvageGraveyard)
                prob += ResourceModifierBonus;

            // High system density bonus
            if (sector.systemDensity == SystemDensity.High)
                prob += HighDensityBonus;
            else if (sector.systemDensity == SystemDensity.Sparse)
                prob -= HighDensityBonus * 0.5f;

            // Adjacent faction density penalty
            int adjacentFactionCount = 0;
            foreach (var adj in adjacentSectors)
            {
                if (adjacentFactionCount >= MaxAdjacentFactionsCounted) break;
                adjacentFactionCount += adj.factionIds.Count;
            }
            prob -= Mathf.Min(adjacentFactionCount, MaxAdjacentFactionsCounted) * AdjacentFactionDensityPenalty;

            return Mathf.Clamp01(prob);
        }

        /// <summary>
        /// Rolls for faction generation on a sector unlock.
        /// If the roll succeeds, creates a faction and registers it in
        /// <paramref name="station"/>.generatedFactions and the sector's factionIds list.
        /// Returns true if a faction was generated.
        /// </summary>
        public static bool TryGenerateFactionForSector(
            SectorData sector,
            StationState station,
            Dictionary<string, FactionDefinition> allFactions,
            NPCSystem npcSystem,
            TraitSystem traitSystem,
            Random rng)
        {
            if (!FeatureFlags.FactionProceduralGeneration) return false;
            if (sector == null) return false;

            var adjacent = GetAdjacentSectors(sector, station.sectors);
            float prob   = ComputeGenerationProbability(sector, adjacent, allFactions);

            if ((float)rng.NextDouble() > prob) return false;

            var faction = GenerateFaction(sector, station, allFactions, npcSystem, traitSystem, rng,
                startingDisposition: null);
            RegisterFaction(faction, sector, station);
            Debug.Log($"[FactionProceduralGenerator] Generated faction '{faction.displayName}' " +
                      $"({faction.governmentType}) in sector {sector.uid}.");
            return true;
        }

        /// <summary>
        /// Seeds the starting scenario with exactly two factions in sectors adjacent to
        /// the home sector — one friendly and one unfriendly.  Called from GameManager.NewGame()
        /// after GalaxyGenerator.Generate() completes.
        /// This always runs regardless of the FactionProceduralGeneration feature flag.
        /// </summary>
        public static void InitializeStartingFactions(
            StationState station,
            Dictionary<string, FactionDefinition> registryFactions,
            NPCSystem npcSystem,
            TraitSystem traitSystem,
            Random rng)
        {
            // Find the home (starting) sector
            SectorData home = null;
            foreach (var s in station.sectors.Values)
            {
                if (Mathf.Approximately(s.coordinates.x, GalaxyGenerator.HomeX) &&
                    Mathf.Approximately(s.coordinates.y, GalaxyGenerator.HomeY))
                {
                    home = s;
                    break;
                }
            }

            if (home == null)
            {
                Debug.LogWarning("[FactionProceduralGenerator] Home sector not found in station.sectors; " +
                                 "starting factions not seeded. This indicates an initialisation order " +
                                 "issue — ensure GalaxyGenerator.Generate() completes before calling " +
                                 "InitializeStartingFactions().");
                return;
            }

            // Find sectors adjacent to home, sorted by distance
            var adjacent = GetAdjacentSectors(home, station.sectors);
            adjacent.Sort((a, b) =>
                Vector2.Distance(a.coordinates, home.coordinates)
                    .CompareTo(Vector2.Distance(b.coordinates, home.coordinates)));

            if (adjacent.Count < 2)
            {
                Debug.LogWarning($"[FactionProceduralGenerator] Only {adjacent.Count} adjacent sector(s) " +
                                 "to home — fewer starting factions than expected.");
            }

            // Seed starting factions
            var allFactions = MergeWithGenerated(registryFactions, station);

            string[] dispositions = { "friendly", "unfriendly" };
            for (int i = 0; i < dispositions.Length && i < adjacent.Count; i++)
            {
                var targetSector = adjacent[i];
                var faction = GenerateFaction(targetSector, station, allFactions, npcSystem, traitSystem, rng,
                    startingDisposition: dispositions[i]);
                RegisterFaction(faction, targetSector, station);

                // Apply starting reputation
                float rep = dispositions[i] == "friendly" ? StartingFriendlyRep : StartingUnfriendlyRep;
                station.factionReputation[faction.id] = rep;

                Debug.Log($"[FactionProceduralGenerator] Starting faction '{faction.displayName}' " +
                          $"({faction.governmentType}) seeded as {dispositions[i]} in sector {targetSector.uid}.");
            }
        }

        /// <summary>
        /// Derives the most appropriate government type for a faction based on the dominant
        /// trait category in its aggregated NPC pool.
        /// </summary>
        public static GovernmentType DeriveGovernmentTypeFromAggregate(FactionTraitAggregate aggregate)
        {
            if (aggregate == null || aggregate.categoryScores.Count == 0)
                return GovernmentType.Democracy;

            // Find the dominant trait category by score
            string dominant = null;
            float  maxScore = -1f;
            foreach (var kv in aggregate.categoryScores)
            {
                if (kv.Value > maxScore)
                {
                    maxScore  = kv.Value;
                    dominant  = kv.Key;
                }
            }

            if (dominant == null) return GovernmentType.Democracy;

            // Secondary-category tiebreaker for Psychological dominant
            if (dominant == TraitCategory.Psychological.ToString())
            {
                float ideological = aggregate.categoryScores.TryGetValue(
                    TraitCategory.Ideological.ToString(), out var v) ? v : 0f;
                return ideological > 0.3f ? GovernmentType.Authoritarian : GovernmentType.Monarchy;
            }

            if (dominant == TraitCategory.Social.ToString())      return GovernmentType.Democracy;
            if (dominant == TraitCategory.Ideological.ToString()) return GovernmentType.Republic;
            if (dominant == TraitCategory.Economic.ToString())    return GovernmentType.CorporateVassal;
            if (dominant == TraitCategory.Physical.ToString())    return GovernmentType.Pirate;

            return GovernmentType.Democracy;
        }

        /// <summary>
        /// Returns the list of preferred trait categories for NPCs generated under the given
        /// government type (used by NPCSystem.SpawnWithGovernmentBias for trait gravity).
        /// Returns a defensive copy — callers may not mutate the internal affinity map.
        /// </summary>
        public static List<TraitCategory> GetGovTraitAffinity(GovernmentType govType)
        {
            if (GovTraitAffinity.TryGetValue(govType, out var cats))
                return new List<TraitCategory>(cats);
            return new List<TraitCategory>();
        }

        /// <summary>
        /// Returns all sectors within GalaxyGenerator.NeighborThreshold distance of
        /// <paramref name="sector"/>, excluding the sector itself.
        /// </summary>
        public static List<SectorData> GetAdjacentSectors(SectorData sector,
                                                           Dictionary<string, SectorData> allSectors)
        {
            var result = new List<SectorData>();
            foreach (var s in allSectors.Values)
            {
                if (s.uid == sector.uid) continue;
                if (Vector2.Distance(s.coordinates, sector.coordinates) <= GalaxyGenerator.NeighborThreshold)
                    result.Add(s);
            }
            return result;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a new <see cref="FactionDefinition"/> for <paramref name="sector"/>.
        /// Generates a name, spawns an initial NPC pool to determine trait distribution,
        /// and derives a government type from that distribution.
        /// When <paramref name="npcSystem"/> or <paramref name="traitSystem"/> is null the
        /// faction is still created with a default government type (Democracy) — used in
        /// tests and when systems are not yet fully initialised.
        /// </summary>
        private static FactionDefinition GenerateFaction(
            SectorData sector,
            StationState station,
            Dictionary<string, FactionDefinition> allFactions,
            NPCSystem npcSystem,
            TraitSystem traitSystem,
            Random rng,
            string startingDisposition)
        {
            string factionId = $"faction.gen.{sector.uid.Replace("sector_", "")}_{rng.Next(0, 10000):D4}";

            var faction = new FactionDefinition
            {
                id                  = factionId,
                displayName         = GenerateFactionName(rng),
                type                = "minor",
                description         = "",
                isGenerated         = true,
                sectorUid           = sector.uid,
                startingDisposition = startingDisposition,
                governmentType      = GovernmentType.Democracy, // will be derived below
            };

            // ── Spawn initial NPC pool for trait aggregation ──────────────────
            // NPCs are generated with a neutral (pre-government) bias so the first
            // government type emerges from the raw regional population.
            // They are kept in a temporary lookup and are NOT added to station.npcs —
            // station.npcs is the on-station population used by NeedSystem, JobSystem,
            // and other live-simulation systems.  Adding faction-population NPCs there
            // would skew population counts, job assignment, and social need calculations.
            // Skip entirely if npcSystem is unavailable (e.g. in unit tests).
            var factionNpcs = new Dictionary<string, NPCInstance>();
            if (npcSystem != null)
            {
                var npcTemplateKeys = new List<string>(npcSystem.AvailableTemplateIds);
                int npcCount        = InitialNpcPoolSize;

                for (int i = 0; i < npcCount && npcTemplateKeys.Count > 0; i++)
                {
                    string templateId = npcTemplateKeys[rng.Next(0, npcTemplateKeys.Count)];
                    var    npc        = npcSystem.SpawnWithGovernmentBias(templateId, null);
                    if (npc == null) continue;

                    npc.factionId = factionId;
                    faction.memberNpcIds.Add(npc.uid);
                    factionNpcs[npc.uid] = npc;
                }
            }

            // ── Derive government type from initial trait distribution ─────────
            if (traitSystem != null)
            {
                var tempAllFactions = MergeWithGenerated(allFactions, station);
                tempAllFactions[faction.id] = faction;

                // Aggregate against the temporary faction NPC pool merged with any
                // existing on-station NPCs (so the aggregator can find members by UID).
                var npcsForAggregation = new Dictionary<string, NPCInstance>(station.npcs);
                foreach (var kv in factionNpcs)
                    npcsForAggregation[kv.Key] = kv.Value;

                var aggregate = FactionAggregator.Calculate(faction, npcsForAggregation,
                                                             traitSystem, tempAllFactions,
                                                             station.tick);
                faction.governmentType = DeriveGovernmentTypeFromAggregate(aggregate);
                if (aggregate != null)
                    station.factionAggregates[factionId] = aggregate;
            }

            return faction;
        }

        /// <summary>Registers a generated faction in StationState and its sector.</summary>
        private static void RegisterFaction(FactionDefinition faction, SectorData sector,
                                             StationState station)
        {
            station.generatedFactions[faction.id] = faction;
            if (!station.factionReputation.ContainsKey(faction.id))
                station.factionReputation[faction.id] = 0f;
            if (!sector.factionIds.Contains(faction.id))
                sector.factionIds.Add(faction.id);
        }

        /// <summary>
        /// Returns a merged dictionary of registry factions and generated factions,
        /// with generated factions taking precedence.
        /// </summary>
        private static Dictionary<string, FactionDefinition> MergeWithGenerated(
            Dictionary<string, FactionDefinition> registryFactions, StationState station)
        {
            var all = new Dictionary<string, FactionDefinition>(registryFactions);
            foreach (var kv in station.generatedFactions)
                all[kv.Key] = kv.Value;
            return all;
        }

        /// <summary>Generates a procedural faction name from the name word pools.</summary>
        private static string GenerateFactionName(Random rng)
        {
            string adj  = NameAdjectives[rng.Next(0, NameAdjectives.Length)];
            string body = NameBodies    [rng.Next(0, NameBodies.Length)];
            // 40 % chance of "The …" prefix
            bool addThe = rng.NextDouble() < 0.4;
            return addThe ? $"The {adj} {body}" : $"{adj} {body}";
        }
    }
}
