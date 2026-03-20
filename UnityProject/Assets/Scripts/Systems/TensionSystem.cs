// TensionSystem — tracks and progresses player-NPC tension based on how
// player actions conflict with NPC trait effects.
//
// Tension accumulates when player actions conflict with an NPC's active traits.
// It decays passively over time during conflict-free periods.
//
// Stage thresholds (default):
//   Normal      :   0 – 29
//   Disgruntled :  30 – 59
//   WorkSlowdown:  60 – 89
//   DepartureRisk: 90+
//
// Gated by FeatureFlags.NpcTraits.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class TensionSystem
    {
        // ── Constants / configurable thresholds ──────────────────────────────

        public float DisgruntledThreshold   = 30f;
        public float WorkSlowdownThreshold  = 60f;
        public float DepartureRiskThreshold = 90f;

        /// <summary>Tension decay per day during conflict-free periods.</summary>
        public float PassiveDecayPerDay     = 2f;

        /// <summary>Applied to NPC work modifier when at WorkSlowdown stage.</summary>
        public float WorkSlowdownModifier   = 0.85f;

        /// <summary>
        /// Probability per day that a DepartureRisk NPC attempts to leave.
        /// TODO: Wire departure attempt into crew/roster system when available.
        /// </summary>
        public float DepartureAttemptChancePerDay = 0.1f;

        // ── Dependency ───────────────────────────────────────────────────────
        private readonly TraitSystem _traits;
        private MoodSystem _mood;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when an NPC's tension stage changes. Payload: (npc, newStage).</summary>
        public event Action<NPCInstance, TensionStage> OnTensionStageChanged;

        // ── Constructor ──────────────────────────────────────────────────────

        public TensionSystem(TraitSystem traits) => _traits = traits;

        public void SetMoodSystem(MoodSystem mood) => _mood = mood;

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>Called once per game tick from GameManager.AdvanceTick.</summary>
        public void Tick(StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            if (station.tick % TimeSystem.TicksPerDay != 0) return; // once per day

            foreach (var npc in station.npcs.Values)
            {
                if (npc.traitProfile == null) continue;
                ApplyPassiveDecay(npc, station);
                ApplyStageEffects(npc, station);
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a player action against an NPC, calculating a tension delta
        /// based on how the action conflicts with the NPC's active trait effects.
        /// </summary>
        public void RegisterPlayerAction(NPCInstance npc, PlayerActionType actionType,
                                          StationState station)
        {
            if (!FeatureFlags.NpcTraits) return;
            var profile = npc.GetOrCreateTraitProfile();

            float delta = CalculateTensionDelta(npc, actionType);
            if (delta == 0f) return;

            profile.tensionScore = Mathf.Clamp(profile.tensionScore + delta, 0f, 100f);
            UpdateTensionStage(npc, station);
        }

        // ── Tension delta calculation ─────────────────────────────────────────

        private float CalculateTensionDelta(NPCInstance npc, PlayerActionType actionType)
        {
            // Base delta from action type
            float baseDelta = actionType switch
            {
                PlayerActionType.Micromanage          => 3f,
                PlayerActionType.ResourceRestriction  => 5f,
                PlayerActionType.ForcedOvertime       => 8f,
                PlayerActionType.SocialInteraction    => -3f,   // positive interaction reduces tension
                PlayerActionType.ResourceProvisioning => -5f,
                _ => 0f,
            };

            // Scale by number of active negative traits that are relevant to this action
            float traitMultiplier = 1f;
            if (npc.traitProfile != null)
            {
                foreach (var active in npc.traitProfile.traits)
                {
                    if (!_traits.TryGetTrait(active.traitId, out var def)) continue;
                    bool conflicts = TraitConflictsWithAction(def, actionType);
                    if (conflicts)
                        traitMultiplier += 0.25f * active.strength;
                }
            }

            return baseDelta * traitMultiplier;
        }

        private static bool TraitConflictsWithAction(NpcTraitDefinition trait,
                                                      PlayerActionType actionType)
        {
            // Negative traits amplify tension from harmful actions;
            // Positive traits amplify tension reduction from positive actions.
            return trait.valence == TraitValence.Negative &&
                   (actionType == PlayerActionType.ForcedOvertime  ||
                    actionType == PlayerActionType.Micromanage      ||
                    actionType == PlayerActionType.ResourceRestriction);
        }

        // ── Passive decay ─────────────────────────────────────────────────────

        private void ApplyPassiveDecay(NPCInstance npc, StationState station)
        {
            var profile = npc.traitProfile;
            if (profile.tensionScore <= 0f) return;
            profile.tensionScore = Mathf.Max(0f, profile.tensionScore - PassiveDecayPerDay);
            UpdateTensionStage(npc, station);
        }

        // ── Stage management ──────────────────────────────────────────────────

        private void UpdateTensionStage(NPCInstance npc, StationState station)
        {
            var profile = npc.traitProfile;
            TensionStage newStage = ComputeStage(profile.tensionScore);
            if (newStage == profile.tensionStage) return;

            TensionStage oldStage = profile.tensionStage;
            profile.tensionStage = newStage;
            OnTensionStageChanged?.Invoke(npc, newStage);

            // Log stage transitions
            if (newStage > oldStage)
                station.LogEvent($"{npc.name}'s morale has deteriorated: {newStage}.");
            else
                station.LogEvent($"{npc.name}'s morale has improved: {newStage}.");
        }

        private TensionStage ComputeStage(float score)
        {
            if (score >= DepartureRiskThreshold) return TensionStage.DepartureRisk;
            if (score >= WorkSlowdownThreshold)  return TensionStage.WorkSlowdown;
            if (score >= DisgruntledThreshold)   return TensionStage.Disgruntled;
            return TensionStage.Normal;
        }

        private void ApplyStageEffects(NPCInstance npc, StationState station)
        {
            var profile = npc.traitProfile;

            switch (profile.tensionStage)
            {
                case TensionStage.Disgruntled:
                    // Mood penalty applied daily
                    _mood?.PushModifier(npc, "tension_disgruntled", -5f,
                                        TimeSystem.TicksPerDay, station.tick, "tension");
                    break;

                case TensionStage.WorkSlowdown:
                    _mood?.PushModifier(npc, "tension_slowdown", -10f,
                                        TimeSystem.TicksPerDay, station.tick, "tension");
                    break;

                case TensionStage.DepartureRisk:
                    _mood?.PushModifier(npc, "tension_departure_risk", -15f,
                                        TimeSystem.TicksPerDay, station.tick, "tension");
                    // Evaluate departure attempt
                    if (UnityEngine.Random.value < DepartureAttemptChancePerDay)
                    {
                        // TODO: Trigger departure attempt via crew/roster system
                        // when that system is implemented.
                        station.LogEvent($"⚠ {npc.name} is considering leaving the station.");
                    }
                    break;

                case TensionStage.Normal:
                default:
                    break;
            }
        }

        // ── Queries ────────────────────────────────────────────────────────────

        public TensionStage GetTensionStage(NPCInstance npc)
            => npc.traitProfile?.tensionStage ?? TensionStage.Normal;

        public float GetTensionScore(NPCInstance npc)
            => npc.traitProfile?.tensionScore ?? 0f;

        /// <summary>Returns a short label for use in the Crew Menu.</summary>
        public static string GetTensionStageLabel(TensionStage stage) => stage switch
        {
            TensionStage.Disgruntled   => "Disgruntled",
            TensionStage.WorkSlowdown  => "Work Slowdown",
            TensionStage.DepartureRisk => "⚠ Departure Risk",
            _                          => "",
        };

        /// <summary>Returns the badge colour for a tension stage.</summary>
        public static Color GetTensionStageColor(TensionStage stage) => stage switch
        {
            TensionStage.Disgruntled   => new Color(0.88f, 0.68f, 0.10f),
            TensionStage.WorkSlowdown  => new Color(0.88f, 0.45f, 0.10f),
            TensionStage.DepartureRisk => new Color(0.86f, 0.26f, 0.26f),
            _                          => Color.clear,
        };
    }
}
