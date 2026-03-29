// Resource System — tracks per-tick resource production and consumption.
// Each active module applies its resource_effects per tick.
// Cascade failure: depletion degrades dependent modules independently.
// Morale scaling: station-wide mood score modulates production output.
// Credits: depletion restricts player actions only; no module cascade fires.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ResourceSystem
    {
        private readonly IRegistryAccess _registry;

        // Mood system — injected to push NPC deprivation penalties before module cascade.
        private MoodSystem _mood;

        // ── Morale scalar bounds (loaded from balance data; defaults applied until data loads) ──
        private float _moraleScalarMax = 0.15f;
        private float _moraleScalarMin = -0.15f;

        // ── Warning threshold crossing state ─────────────────────────────────
        // Tracks which resources were previously below their warning threshold or depleted,
        // so warnings only fire on state transitions rather than every tick.
        private readonly HashSet<string> _prevBelowThreshold = new HashSet<string>();
        private readonly HashSet<string> _prevDepleted       = new HashSet<string>();

        /// <summary>
        /// Fired once when a resource first hits zero (transitions from above 0 to depleted).
        /// Payload is the resource ID.  Not fired on subsequent ticks while already depleted.
        /// </summary>
        public event Action<string> OnResourceDepleted;

        // ── Fallback balance data used when ContentRegistry has not yet loaded resources ──
        // These mirror core_resources.json and keep the system functional during initialisation.
        // Cached as a static readonly to avoid per-tick allocation before registry data loads.
        private static readonly Dictionary<string, float> FallbackSoftCaps =
            new Dictionary<string, float>
            {
                { "power",   500f }, { "food",  500f }, { "oxygen",  500f },
                { "ice",     500f }, { "fuel",  200f }, { "parts",   200f },
                { "credits", 100_000f }
            };

        private static readonly Dictionary<string, ResourceDefinition> FallbackDefinitions =
            BuildFallbackDefinitions();

        public ResourceSystem(IRegistryAccess registry) => _registry = registry;

        public void SetMoodSystem(MoodSystem mood) => _mood = mood;

        // ── Main tick ─────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            CacheMoraleScalar();
            float moraleMod = MoraleModifier(station);
            ApplyModuleEffects(station, moraleMod);
            // Sequence enforced: NPC need deprivation fires before module cascade.
            EvaluateDeprivation(station);
            CheckWarningThresholds(station);
        }

        // ── Morale modifier ───────────────────────────────────────────────────

        /// <summary>
        /// Production efficiency multiplier based on station-wide average mood score.
        /// moodScore 100 (max) → +moraleScalarMax.
        /// moodScore  50 (neutral) → 0 % change.
        /// moodScore   0 (min) → moraleScalarMin.
        /// Scalar range and bounds are defined in balance data (core_resources.json).
        /// </summary>
        public float MoraleModifier(StationState station)
        {
            var crew = station.GetCrew();
            if (crew.Count == 0) return 1f;
            float avg = 0f;
            foreach (var n in crew) avg += n.moodScore;
            avg /= crew.Count;

            // Normalise to [-1, +1]: (avg - 50) / 50
            float normalised = (avg - 50f) / 50f;
            // Apply asymmetric scalars to match the design spec (max and min may differ in magnitude)
            float scalar = normalised >= 0f ? normalised * _moraleScalarMax
                                            : normalised * Mathf.Abs(_moraleScalarMin);
            return 1f + scalar;
        }

        // ── Module effects ────────────────────────────────────────────────────

        private void ApplyModuleEffects(StationState station, float moraleMod)
        {
            foreach (var module in station.modules.Values)
            {
                if (!module.active || module.damage >= 1f) continue;
                // Skip modules that are offline due to resource deprivation.
                if (module.IsResourceDeprived) continue;
                if (!_registry.Modules.TryGetValue(module.definitionId, out var defn)) continue;

                float baseEff = 1f - module.damage;
                foreach (var kv in defn.resourceEffects)
                {
                    float delta;
                    if (kv.Value > 0f)
                    {
                        float efficiency = baseEff * moraleMod;
                        float current    = station.GetResource(kv.Key);
                        float cap        = GetSoftCap(kv.Key);
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

        // ── Cascade failure + credits special case ────────────────────────────

        /// <summary>
        /// Evaluates every tracked resource for depletion.
        /// Sequence per depleted resource:
        ///   1. If it causes NPC deprivation → push a mood penalty before cascade.
        ///   2. If it is a credit resource → restrict player actions only (no cascade).
        ///   3. Otherwise if it causes module cascade → degrade dependent modules.
        /// When a resource recovers above zero the restrictions / degradation are lifted.
        /// The system is extensible: any resource key in module resource_effects is handled
        /// without code changes — only the balance data entry needs adding.
        /// </summary>
        private void EvaluateDeprivation(StationState station)
        {
            // Collect the set of resource definitions to evaluate.
            // If registry has loaded data, use it; otherwise fall back to the cached set.
            var toEvaluate = GetResourceDefinitions();

            foreach (var resDef in toEvaluate.Values)
            {
                // Skip meta-entries (e.g. morale_balance) that are not actual resources.
                if (!station.resources.ContainsKey(resDef.id)) continue;

                float amount = station.GetResource(resDef.id);
                bool  depleted = amount <= 0f;

                if (depleted)
                {
                    // Fire depletion event on first crossing (transition: not depleted → depleted).
                    if (!_prevDepleted.Contains(resDef.id))
                        OnResourceDepleted?.Invoke(resDef.id);

                    // Step 1: NPC need deprivation (enforces sequence: NPCs suffer first).
                    if (resDef.causesNpcDeprivation)
                        ApplyNpcDeprivation(station, resDef.id);

                    // Step 2: Credits → restrict player actions, no module cascade.
                    if (resDef.isCreditResource)
                    {
                        if (!station.IsActionRestricted("hire"))
                        {
                            station.RestrictAction("hire");
                            station.RestrictAction("purchase");
                            station.LogEvent("Credits depleted: hiring and purchasing suspended.");
                        }
                        continue;
                    }

                    // Step 3: Module cascade.
                    if (resDef.causesModuleCascade)
                        ApplyModuleCascade(station, resDef.id);
                }
                else
                {
                    // Resource recovered — lift any restrictions it caused.
                    if (resDef.isCreditResource)
                    {
                        station.UnrestrictAction("hire");
                        station.UnrestrictAction("purchase");
                    }
                    if (resDef.causesModuleCascade)
                        RestoreModulesFromCascade(station, resDef.id);
                }
            }
        }

        /// <summary>
        /// Applies a mood penalty to all crew NPCs to represent suffering from resource
        /// deprivation.  Uses MoodSystem when available (PushModifier dedupes by eventId+source,
        /// so re-pushing refreshes the duration without stacking); falls back to direct
        /// moodScore adjustment so the system works in isolation (tests / early boot).
        /// The fallback path is also idempotent within a single deprivation event because
        /// moodScore is clamped and the NPC can only go to 0.
        /// Called before module cascade to enforce the design sequence.
        /// Penalty magnitude is defined per-resource in balance data (npc_deprivation_penalty).
        /// </summary>
        private void ApplyNpcDeprivation(StationState station, string resourceId)
        {
            float magnitude = 10f;  // fallback if resource def not available
            if (GetResourceDefinitions().TryGetValue(resourceId, out var def))
                magnitude = def.npcDeprivationPenalty;

            float penalty = -Mathf.Abs(magnitude);  // always negative
            string eventId = $"resource_deprived_{resourceId}";

            foreach (var npc in station.GetCrew())
            {
                if (_mood != null)
                {
                    // PushModifier dedupes by (eventId, source): refreshes duration, never stacks.
                    _mood.PushModifier(npc, eventId, penalty, durationTicks: 3,
                                       currentTick: station.tick, source: "resource_system");
                }
                else
                {
                    // Fallback: direct delta. Clamped, so harmless on repeated calls.
                    npc.moodScore = Mathf.Clamp(npc.moodScore + penalty, 0f, 100f);
                }
            }

            station.LogEvent($"Station {resourceId} depleted — crew suffering.");
        }

        /// <summary>
        /// Marks every module that has a negative resource_effect for <paramref name="resourceId"/>
        /// as deprived by that resource.  Modules enter degraded state independently.
        /// </summary>
        private void ApplyModuleCascade(StationState station, string resourceId)
        {
            foreach (var module in station.modules.Values)
            {
                if (module.resourceDeprived.Contains(resourceId)) continue;
                if (!_registry.Modules.TryGetValue(module.definitionId, out var defn)) continue;
                if (defn.resourceEffects.TryGetValue(resourceId, out float effect) && effect < 0f)
                {
                    module.resourceDeprived.Add(resourceId);
                    station.LogEvent(
                        $"{module.displayName} offline — {resourceId} supply cut (independent cascade).");
                }
            }
        }

        /// <summary>
        /// Clears the resource deprivation flag from all modules for the given resource,
        /// allowing them to resume operation when supply is restored.
        /// </summary>
        private void RestoreModulesFromCascade(StationState station, string resourceId)
        {
            foreach (var module in station.modules.Values)
            {
                if (module.resourceDeprived.Remove(resourceId))
                    station.LogEvent($"{module.displayName} restored — {resourceId} supply recovered.");
            }
        }

        // ── Warning thresholds ────────────────────────────────────────────────

        /// <summary>
        /// Fires a log event only when a resource crosses a threshold boundary
        /// (i.e. transitions from above to below, or from non-zero to zero).
        /// Subsequent ticks while the resource remains in the same state are silent.
        /// </summary>
        private void CheckWarningThresholds(StationState station)
        {
            var definitions = GetResourceDefinitions();
            foreach (var kv in station.resources)
            {
                float threshold = GetWarningThreshold(kv.Key, definitions);
                if (threshold <= 0f) continue;  // no threshold defined

                float amount    = kv.Value;
                bool  nowDepleted      = amount <= 0f;
                bool  nowBelowThreshold = amount > 0f && amount < threshold;

                bool wasDepleted      = _prevDepleted.Contains(kv.Key);
                bool wasBelowThreshold = _prevBelowThreshold.Contains(kv.Key);

                // Fire CRITICAL only on the first tick the resource hits zero.
                if (nowDepleted && !wasDepleted)
                    station.LogEvent($"CRITICAL: {kv.Key.ToUpper()} DEPLETED.");

                // Fire low-resource warning only on the transition into the warning band.
                if (nowBelowThreshold && !wasBelowThreshold)
                    station.LogEvent($"Warning: {kv.Key} is low ({amount:F0}).");

                // Update state tracking.
                if (nowDepleted) _prevDepleted.Add(kv.Key);
                else             _prevDepleted.Remove(kv.Key);

                if (nowBelowThreshold) _prevBelowThreshold.Add(kv.Key);
                else                   _prevBelowThreshold.Remove(kv.Key);
            }
        }

        // ── Public helpers ────────────────────────────────────────────────────

        public Dictionary<string, string> Summary(StationState station)
        {
            var result = new Dictionary<string, string>();
            foreach (var kv in station.resources)
                result[kv.Key] = kv.Value.ToString("F0");
            return result;
        }

        /// <summary>
        /// Clears the warning threshold crossing state so that crossing events
        /// fire correctly on the first tick of a new or loaded game session.
        /// Call this from GameManager.NewGame() and GameManager.LoadGame().
        /// </summary>
        public void ResetWarningState()
        {
            _prevDepleted.Clear();
            _prevBelowThreshold.Clear();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void CacheMoraleScalar()
        {
            if (_registry.Resources.TryGetValue("morale_balance", out var mb))
            {
                _moraleScalarMax = mb.moraleScalarMax;
                _moraleScalarMin = mb.moraleScalarMin;
            }
        }

        private Dictionary<string, ResourceDefinition> GetResourceDefinitions()
        {
            return _registry.Resources.Count > 0
                ? _registry.Resources
                : FallbackDefinitions;
        }

        private static Dictionary<string, ResourceDefinition> BuildFallbackDefinitions()
        {
            return new Dictionary<string, ResourceDefinition>
            {
                { "power",   new ResourceDefinition { id = "power",   warningThreshold = 15f,  softCap = 500f,       causesModuleCascade = true } },
                { "food",    new ResourceDefinition { id = "food",    warningThreshold = 20f,  softCap = 500f,       causesModuleCascade = true,  causesNpcDeprivation = true,  npcDeprivationPenalty = 10f } },
                { "oxygen",  new ResourceDefinition { id = "oxygen",  warningThreshold = 10f,  softCap = 500f,       causesModuleCascade = true,  causesNpcDeprivation = true,  npcDeprivationPenalty = 25f } },
                { "ice",     new ResourceDefinition { id = "ice",     warningThreshold = 30f,  softCap = 500f,       causesModuleCascade = false, causesNpcDeprivation = true,  npcDeprivationPenalty = 10f } },
                { "fuel",    new ResourceDefinition { id = "fuel",    warningThreshold = 10f,  softCap = 200f,       causesModuleCascade = true } },
                { "parts",   new ResourceDefinition { id = "parts",   warningThreshold = 5f,   softCap = 200f,       causesModuleCascade = false } },
                { "credits", new ResourceDefinition { id = "credits", warningThreshold = 50f,  softCap = 100_000f,   isCreditResource = true } },
            };
        }

        private float GetSoftCap(string resourceId)
        {
            if (_registry.Resources.TryGetValue(resourceId, out var def)) return def.softCap;
            return FallbackSoftCaps.TryGetValue(resourceId, out float cap) ? cap : float.MaxValue;
        }

        private static float GetWarningThreshold(string resourceId,
            Dictionary<string, ResourceDefinition> definitions)
        {
            return definitions.TryGetValue(resourceId, out var def) ? def.warningThreshold : 0f;
        }
    }
}
