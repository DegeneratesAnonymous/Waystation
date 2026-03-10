// Resource System — tracks per-tick resource production and consumption.
// Each active module applies its resource_effects per tick.
// Warns when resources fall below critical thresholds.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ResourceSystem
    {
        private readonly ContentRegistry _registry;

        // Thresholds that trigger station log warnings
        private static readonly Dictionary<string, float> CriticalThresholds = new Dictionary<string, float>
        {
            { "food",    20f }, { "power",   15f }, { "oxygen",  10f },
            { "parts",    5f }, { "credits", 50f }, { "ice",     30f }
        };

        // Soft caps — resources don't grow beyond these passively
        private static readonly Dictionary<string, float> SoftCaps = new Dictionary<string, float>
        {
            { "food",    500f }, { "power",   500f }, { "oxygen",  500f },
            { "parts",   200f }, { "credits", 100_000f }, { "ice", 500f }
        };

        public ResourceSystem(ContentRegistry registry) => _registry = registry;

        public void Tick(StationState station)
        {
            float moraleMod = MoraleModifier(station);
            ApplyModuleEffects(station, moraleMod);
            CheckThresholds(station);
        }

        /// <summary>
        /// Production efficiency multiplier based on average crew mood.
        /// Ranges from 0.70 (miserable) to 1.15 (content). Neutral mood → 1.0.
        /// </summary>
        public float MoraleModifier(StationState station)
        {
            var crew = station.GetCrew();
            if (crew.Count == 0) return 1f;
            float avg = 0f;
            foreach (var n in crew) avg += n.mood;
            avg /= crew.Count;
            return avg >= 0f ? 1f + avg * 0.15f : 1f + avg * 0.30f;
        }

        private void ApplyModuleEffects(StationState station, float moraleMod)
        {
            foreach (var module in station.modules.Values)
            {
                if (!module.active || module.damage >= 1f) continue;
                if (!_registry.Modules.TryGetValue(module.definitionId, out var defn)) continue;

                float baseEff = 1f - module.damage;
                foreach (var kv in defn.resourceEffects)
                {
                    float delta;
                    if (kv.Value > 0f)
                    {
                        float efficiency  = baseEff * moraleMod;
                        float current     = station.GetResource(kv.Key);
                        float cap         = SoftCaps.ContainsKey(kv.Key) ? SoftCaps[kv.Key] : float.MaxValue;
                        // Clamp to zero so being over-cap never drives a negative production delta.
                        delta = Mathf.Max(0f, Mathf.Min(kv.Value * efficiency, cap - current));
                    }
                    else
                    {
                        delta = kv.Value * baseEff;
                    }
                    station.ModifyResource(kv.Key, delta);
                }
            }
        }

        private void CheckThresholds(StationState station)
        {
            foreach (var kv in CriticalThresholds)
            {
                float amount = station.GetResource(kv.Key);
                if (amount <= 0f)
                {
                    station.LogEvent($"CRITICAL: {kv.Key.ToUpper()} DEPLETED.");
                    if (kv.Key == "oxygen") station.SetTag("oxygen_emergency");
                    else if (kv.Key == "power") station.SetTag("power_failure");
                }
                else if (amount < kv.Value && station.tick % 5 == 0)
                {
                    station.LogEvent($"Warning: {kv.Key} is low ({amount:F0}).");
                }
            }
        }

        public Dictionary<string, string> Summary(StationState station)
        {
            var result = new Dictionary<string, string>();
            foreach (var kv in station.resources)
                result[kv.Key] = kv.Value.ToString("F0");
            return result;
        }
    }
}
