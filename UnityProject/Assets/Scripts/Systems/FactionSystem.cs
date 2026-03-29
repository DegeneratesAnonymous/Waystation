// Faction System — independent faction behaviour simulation.
// Factions track inter-faction relationships, react to player rep, and
// generate regional pressure that shapes visitor traffic and threat levels.
//
// Procedural faction generation:
//   FactionSystem.OnSectorUnlocked() fires FactionProceduralGenerator.TryGenerateFactionForSector()
//   which rolls for a new faction based on the sector's resource profile and adjacent faction density.
//   Generated factions are stored in StationState.generatedFactions (not in ContentRegistry.Factions).
//
// Starting factions:
//   FactionSystem.InitializeStartingFactions() plants exactly two factions (one friendly,
//   one unfriendly) in sectors adjacent to the home sector.  Called from GameManager.NewGame().
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Random = System.Random;

namespace Waystation.Systems
{
    public class FactionSystem
    {
        private readonly ContentRegistry _registry;
        private NPCSystem _npcSystem;
        private TraitSystem _traitSystem;

        // Inter-faction relationship state (factionId → factionId → -1..1)
        private readonly Dictionary<string, Dictionary<string, float>> _relationships =
            new Dictionary<string, Dictionary<string, float>>();

        // Previous rep snapshots used to detect threshold crossings between ticks.
        private readonly Dictionary<string, float> _prevRep = new Dictionary<string, float>();

        /// <summary>
        /// Fired when a faction's reputation crosses a significant threshold boundary.
        /// Arguments: factionId, oldRep, newRep.
        /// Threshold boundaries: -50, -20, 10, 40, 75 (matching RepLabel band edges).
        /// </summary>
        public event Action<string, float, float> OnFactionRepThresholdCrossed;

        // Threshold values that define boundary crossings (sorted ascending).
        private static readonly float[] RepThresholds = { -50f, -20f, 10f, 40f, 75f };

        public FactionSystem(ContentRegistry registry) => _registry = registry;

        /// <summary>Injects system dependencies required for procedural generation.</summary>
        public void SetSystems(NPCSystem npcSystem, TraitSystem traitSystem)
        {
            _npcSystem   = npcSystem;
            _traitSystem = traitSystem;
        }

        // ── Initialise ────────────────────────────────────────────────────────

        /// <summary>
        /// Seed faction reputations and inter-faction relationships.
        /// Call once after registry is loaded.
        /// </summary>
        public void Initialize(StationState station)
        {
            foreach (var kv in _registry.Factions)
            {
                if (!station.factionReputation.ContainsKey(kv.Key))
                    station.factionReputation[kv.Key] = 0f;

                _relationships[kv.Key] = new Dictionary<string, float>(kv.Value.relationships);
            }
            Debug.Log($"[FactionSystem] Initialized with {_registry.Factions.Count} factions.");
        }

        /// <summary>
        /// Seeds the starting scenario factions: exactly two procedurally-generated factions
        /// are placed in sectors adjacent to the home sector — one friendly, one unfriendly.
        /// Must be called after GalaxyGenerator.Generate() so adjacent sectors exist.
        /// </summary>
        public void InitializeStartingFactions(StationState station, Random rng)
        {
            if (_npcSystem == null || _traitSystem == null)
            {
                Debug.LogWarning("[FactionSystem] InitializeStartingFactions called before SetSystems — " +
                                 "starting factions not seeded.");
                return;
            }
            FactionProceduralGenerator.InitializeStartingFactions(
                station, _registry.Factions, _npcSystem, _traitSystem, rng);
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station.tick % 10 != 0) return;   // lightweight: update every 10 ticks

            foreach (var kv in _registry.Factions)
                TickFaction(kv.Key, kv.Value, station);

            // Also tick generated factions
            foreach (var kv in station.generatedFactions)
                TickFaction(kv.Key, kv.Value, station);
        }

        private void TickFaction(string factionId, FactionDefinition def, StationState station)
        {
            float oldRep = station.GetFactionRep(factionId);
            bool hasSnapshot = _prevRep.TryGetValue(factionId, out float snapRep);
            if (!hasSnapshot) snapRep = oldRep;

            if (def.behaviorTags.Contains("aggressive") && oldRep > -20f)
                station.ModifyFactionRep(factionId, -0.5f);

            if (def.behaviorTags.Contains("trades_always") && station.HasTag("active_trading"))
                station.ModifyFactionRep(factionId, 0.3f);

            if (oldRep < -60f && def.behaviorTags.Contains("raids_weak_stations") &&
                UnityEngine.Random.value < 0.1f)
                station.LogEvent($"Intelligence: {def.displayName} raiding parties reported near sector.");

            float newRep = station.GetFactionRep(factionId);
            if (CrossedThreshold(snapRep, newRep))
                OnFactionRepThresholdCrossed?.Invoke(factionId, snapRep, newRep);
            _prevRep[factionId] = newRep;
        }

        /// <summary>Returns true if the rep moved from one threshold band to another.</summary>
        private static bool CrossedThreshold(float oldRep, float newRep)
        {
            if (Mathf.Approximately(oldRep, newRep)) return false;
            foreach (float t in RepThresholds)
            {
                bool oldBelow = oldRep < t;
                bool newBelow = newRep < t;
                if (oldBelow != newBelow) return true;
            }
            return false;
        }

        // ── Sector unlock hook ─────────────────────────────────────────────────

        /// <summary>
        /// Called when a sector transitions from Uncharted to Detected or Visited.
        /// Rolls for faction generation based on the sector's resource profile and
        /// adjacent faction density.  Requires <see cref="SetSystems"/> to have been
        /// called; silently skips otherwise.
        /// </summary>
        public void OnSectorUnlocked(SectorData sector, StationState station, Random rng = null)
        {
            if (_npcSystem == null || _traitSystem == null) return;

            if (rng == null)
                rng = new Random(station.galaxySeed ^ station.tick);

            var allFactions = GetAllFactions(station);
            FactionProceduralGenerator.TryGenerateFactionForSector(
                sector, station, allFactions, _npcSystem, _traitSystem, rng);
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public string GetFactionLabel(StationState station, string factionId)
        {
            float rep = station.GetFactionRep(factionId);
            return $"{factionId}: {rep:+0;-0} ({RepLabel(rep)})";
        }

        public float GetInterFactionRelationship(string a, string b)
            => (_relationships.TryGetValue(a, out var inner) && inner.TryGetValue(b, out var v))
               ? v : 0f;

        public List<string> FactionSummary(StationState station)
        {
            var lines = new List<string>();
            var sorted = new List<KeyValuePair<string, FactionDefinition>>(_registry.Factions);
            sorted.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.Ordinal));
            foreach (var kv in sorted)
            {
                float rep = station.GetFactionRep(kv.Key);
                lines.Add($"  {kv.Value.displayName,-28} {rep:+0;-0;0,6}  {RepLabel(rep)}");
            }

            // Include generated factions
            var genSorted = new List<KeyValuePair<string, FactionDefinition>>(station.generatedFactions);
            genSorted.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.Ordinal));
            foreach (var kv in genSorted)
            {
                float rep = station.GetFactionRep(kv.Key);
                lines.Add($"  {kv.Value.displayName,-28} {rep:+0;-0;0,6}  {RepLabel(rep)}  [gen]");
            }
            return lines;
        }

        /// <summary>
        /// Returns all faction definitions: registry-loaded and procedurally generated,
        /// with generated definitions taking precedence on ID collision (should not occur).
        /// </summary>
        public Dictionary<string, FactionDefinition> GetAllFactions(StationState station)
            => MergeAllFactions(_registry.Factions, station);

        /// <summary>
        /// Merges <paramref name="registryFactions"/> with <paramref name="station"/>.generatedFactions.
        /// Generated factions take precedence on ID collision.
        /// Exposed as a static helper so tests can exercise the merge logic without
        /// constructing a <see cref="ContentRegistry"/> MonoBehaviour.
        /// </summary>
        public static Dictionary<string, FactionDefinition> MergeAllFactions(
            Dictionary<string, FactionDefinition> registryFactions, StationState station)
        {
            var all = new Dictionary<string, FactionDefinition>(registryFactions);
            foreach (var kv in station.generatedFactions)
                all[kv.Key] = kv.Value;
            return all;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string RepLabel(float rep)
        {
            if (rep >= 75f)  return "Allied";
            if (rep >= 40f)  return "Friendly";
            if (rep >= 10f)  return "Neutral";
            if (rep >= -20f) return "Cautious";
            if (rep >= -50f) return "Hostile";
            return "Enemy";
        }
    }
}
