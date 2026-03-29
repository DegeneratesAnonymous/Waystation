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
// Departure execution additionally gated by FeatureFlags.NpcDeparture.
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
        /// Probability per day that a DepartureRisk NPC triggers a departure
        /// announcement (if none is already pending).
        /// </summary>
        public float DepartureAttemptChancePerDay = 0.1f;

        /// <summary>
        /// Number of ticks the player has to intervene after a departure announcement.
        /// Loaded from balance data (game_balance.json) by GameManager; defaults to
        /// 3 in-game days (3 × TicksPerDay = 1080 ticks).
        /// </summary>
        public int InterventionWindowTicks = 1080;

        /// <summary>
        /// Minimum skill-check result required for a successful intervention.
        /// Loaded from balance data by GameManager; defaults to 10.
        /// </summary>
        public int InterventionSkillCheckDC = 10;

        // ── Dependencies ─────────────────────────────────────────────────────
        private readonly TraitSystem _traits;
        private MoodSystem  _mood;
        private SkillSystem _skills;

        // ── Internal constants ────────────────────────────────────────────────

        /// <summary>Number of faces on the intervention skill-check die.</summary>
        private const int SkillCheckDieFaces = 20;

        /// <summary>
        /// Tension score applied after a successful intervention: just above
        /// <see cref="DisgruntledThreshold"/> so the NPC is visibly disgruntled
        /// but not at risk of departing again immediately.
        /// </summary>
        private const float PostInterventionTensionScore = 31f;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when an NPC's tension stage changes. Payload: (npc, newStage).</summary>
        public event Action<NPCInstance, TensionStage> OnTensionStageChanged;

        /// <summary>
        /// Fired when a DepartureRisk NPC announces intent to leave.
        /// Payload: (npc, interventionDeadlineTick).
        /// Subscribe in GameManager / UI to surface the player alert.
        /// </summary>
        public event Action<NPCInstance, int> OnDepartureAnnounced;

        /// <summary>
        /// Fired when an NPC physically departs (after failed/expired intervention).
        /// Payload: npc (already removed from active roster, added to departedNpcs).
        /// </summary>
        public event Action<NPCInstance> OnNpcDeparted;

        // ── Constructor ──────────────────────────────────────────────────────

        public TensionSystem(TraitSystem traits) => _traits = traits;

        public void SetMoodSystem(MoodSystem mood)   => _mood   = mood;
        public void SetSkillSystem(SkillSystem skills) => _skills = skills;

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

            // Process departure windows (checked every daily tick regardless of decay)
            if (FeatureFlags.NpcDeparture)
                ProcessDepartureWindows(station);
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

        /// <summary>
        /// Attempt a player intervention on a DepartureRisk NPC.
        /// Performs a skill roll using <paramref name="skillId"/> against
        /// <see cref="InterventionSkillCheckDC"/>.
        /// On success: tension drops to Disgruntled range and departure is cancelled.
        /// On failure: tension is unchanged; departure will proceed at deadline.
        /// </summary>
        /// <returns>True if the intervention was successful.</returns>
        public bool AttemptIntervention(NPCInstance npc, string skillId, StationState station)
        {
            if (!FeatureFlags.NpcDeparture) return false;
            if (npc.traitProfile?.departure == null || !npc.traitProfile.departure.announced)
                return false;

            // Resolve skill check: d20 roll + skill modifier vs DC
            int dieRoll = UnityEngine.Random.Range(1, SkillCheckDieFaces + 1);
            int roll = _skills != null
                ? _skills.GetSkillCheckResult(npc, skillId) + dieRoll
                : dieRoll;

            bool success = roll >= InterventionSkillCheckDC;

            if (success)
            {
                // Reset tension to just above Disgruntled threshold
                npc.traitProfile.tensionScore = PostInterventionTensionScore;
                UpdateTensionStage(npc, station);
                // Cancel departure
                npc.traitProfile.departure = null;
                station.LogEvent($"✓ Intervention succeeded: {npc.name} has agreed to stay (roll {roll} vs DC {InterventionSkillCheckDC}).");
            }
            else
            {
                station.LogEvent($"✗ Intervention failed: {npc.name} remains set on leaving (roll {roll} vs DC {InterventionSkillCheckDC}).");
            }

            return success;
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
            bool isHarmfulAction = actionType == PlayerActionType.ForcedOvertime  ||
                                   actionType == PlayerActionType.Micromanage      ||
                                   actionType == PlayerActionType.ResourceRestriction;

            bool isPositiveAction = actionType == PlayerActionType.SocialInteraction ||
                                    actionType == PlayerActionType.ResourceProvisioning;

            // Negative traits amplify tension from harmful actions.
            if (isHarmfulAction && trait.valence == TraitValence.Negative)
                return true;

            // Positive/neutral traits amplify tension reduction from positive actions.
            if (isPositiveAction && trait.valence != TraitValence.Negative)
                return true;

            return false;
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

            // Cancel a pending departure if tension has improved below DepartureRisk
            if (newStage < TensionStage.DepartureRisk && profile.departure != null)
            {
                profile.departure = null;
                station.LogEvent($"{npc.name}'s morale has improved; departure cancelled.");
            }

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
                    npc.tensionWorkModifier = 1.0f;   // no work penalty yet
                    break;

                case TensionStage.WorkSlowdown:
                    _mood?.PushModifier(npc, "tension_slowdown", -10f,
                                        TimeSystem.TicksPerDay, station.tick, "tension");
                    // Apply configured work speed penalty
                    npc.tensionWorkModifier = WorkSlowdownModifier;
                    break;

                case TensionStage.DepartureRisk:
                    _mood?.PushModifier(npc, "tension_departure_risk", -15f,
                                        TimeSystem.TicksPerDay, station.tick, "tension");
                    npc.tensionWorkModifier = WorkSlowdownModifier;   // same penalty as WorkSlowdown

                    if (FeatureFlags.NpcDeparture)
                        EvaluateDepartureAnnouncement(npc, station);
                    else
                        station.LogEvent($"⚠ {npc.name} is considering leaving the station.");
                    break;

                case TensionStage.Normal:
                default:
                    npc.tensionWorkModifier = 1.0f;
                    break;
            }
        }

        // ── Departure announcement ────────────────────────────────────────────

        private void EvaluateDepartureAnnouncement(NPCInstance npc, StationState station)
        {
            var profile = npc.traitProfile;

            // Only announce once; if already announced, the window ticker handles the rest.
            if (profile.departure != null && profile.departure.announced) return;

            // Random daily chance to trigger announcement
            if (UnityEngine.Random.value >= DepartureAttemptChancePerDay) return;

            int deadline = station.tick + InterventionWindowTicks;
            profile.departure = new DepartureAnnouncementState
            {
                announced               = true,
                announcedAtTick         = station.tick,
                interventionDeadlineTick = deadline,
            };

            station.LogEvent($"⚠ {npc.name} has announced intent to leave the station.");
            OnDepartureAnnounced?.Invoke(npc, deadline);
        }

        // ── Departure window processing ───────────────────────────────────────

        private void ProcessDepartureWindows(StationState station)
        {
            // Collect NPCs whose intervention window has expired this tick.
            var departing = new List<NPCInstance>();
            foreach (var npc in station.npcs.Values)
            {
                var dep = npc.traitProfile?.departure;
                if (dep == null || !dep.announced) continue;
                if (station.tick >= dep.interventionDeadlineTick)
                    departing.Add(npc);
            }

            foreach (var npc in departing)
                ExecuteDeparture(npc, station);
        }

        // ── Departure execution ───────────────────────────────────────────────

        private void ExecuteDeparture(NPCInstance npc, StationState station)
        {
            // Record exit position before removing so the NPC's last known location is meaningful.
            // Departure is instantaneous: pathTargetCol/Row are set for record-keeping only —
            // the NPC is removed from the active roster this tick and cannot actually pathfind.
            // (Full physical movement sequence would require a "departure in progress" state
            //  driven by NPCSystem, which is out of scope for NPC-007.)
            MoveToExit(npc, station);

            // Remove from active roster
            station.RemoveNpc(npc.uid);

            // Add to departed pool with full state preserved
            var record = new DepartedNpcRecord
            {
                departedAtTick        = station.tick,
                reason                = "tension",
                eligibleForReinjection = true,
                npc                   = npc,
            };
            station.departedNpcs[npc.uid] = record;

            station.LogEvent($"{npc.name} has departed the station.");
            OnNpcDeparted?.Invoke(npc);
        }

        private static void MoveToExit(NPCInstance npc, StationState station)
        {
            // Priority 1: a docked ship's occupied landing pad.
            // Priority 2: any free (unoccupied) landing pad on the station.
            // Priority 3: station origin (0,0) as a fallback exit tile.
            ShipInstance boardingShip = null;
            foreach (var ship in station.ships.Values)
            {
                if (ship.status == "docked" || ship.visitState == ShipVisitState.Docked)
                {
                    boardingShip = ship;
                    break;
                }
            }

            if (boardingShip != null && boardingShip.shuttleUid != null &&
                station.shuttles.TryGetValue(boardingShip.shuttleUid, out var shuttle) &&
                station.landingPads.TryGetValue(shuttle.landingPadUid, out var occupiedPad) &&
                station.foundations.TryGetValue(occupiedPad.foundationUid, out var dockedFoundation))
            {
                npc.pathTargetCol = dockedFoundation.tileCol;
                npc.pathTargetRow = dockedFoundation.tileRow;
                return;
            }

            // No docked ship — try any free landing pad
            var freePad = station.GetFreeLandingPad();
            if (freePad != null && station.foundations.TryGetValue(freePad.foundationUid, out var padFoundation))
            {
                npc.pathTargetCol = padFoundation.tileCol;
                npc.pathTargetRow = padFoundation.tileRow;
                return;
            }

            // No landing pad available — move to station origin (designated exit tile)
            npc.pathTargetCol = 0;
            npc.pathTargetRow = 0;
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
