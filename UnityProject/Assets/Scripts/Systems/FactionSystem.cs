// Faction System — independent faction behaviour simulation.
// Factions track inter-faction relationships, react to player rep, and
// generate regional pressure that shapes visitor traffic and threat levels.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class FactionSystem
    {
        private readonly ContentRegistry _registry;

        // Inter-faction relationship state (factionId → factionId → -1..1)
        private readonly Dictionary<string, Dictionary<string, float>> _relationships =
            new Dictionary<string, Dictionary<string, float>>();

        public FactionSystem(ContentRegistry registry) => _registry = registry;

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

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station.tick % 10 != 0) return;   // lightweight: update every 10 ticks

            foreach (var kv in _registry.Factions)
                TickFaction(kv.Key, kv.Value, station);
        }

        private void TickFaction(string factionId, FactionDefinition def, StationState station)
        {
            float rep = station.GetFactionRep(factionId);

            if (def.behaviorTags.Contains("aggressive") && rep > -20f)
                station.ModifyFactionRep(factionId, -0.5f);

            if (def.behaviorTags.Contains("trades_always") && station.HasTag("active_trading"))
                station.ModifyFactionRep(factionId, 0.3f);

            if (rep < -60f && def.behaviorTags.Contains("raids_weak_stations") &&
                UnityEngine.Random.value < 0.1f)
                station.LogEvent($"Intelligence: {def.displayName} raiding parties reported near sector.");
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
            return lines;
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
