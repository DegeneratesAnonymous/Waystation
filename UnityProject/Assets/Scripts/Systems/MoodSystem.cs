// MoodSystem — manages two independent 0–100 mood axes for every crew NPC.
//
// Axes:
//   Happy/Sad (moodScore):    0 = miserable, 100 = elated.  Drives crisis detection
//                              and station morale.  Drifts toward 50 each waking tick.
//   Calm/Stressed (stressScore): 0 = very stressed, 100 = very calm.  Independent of
//                              crisis.  Feeds into SanitySystem alongside moodScore.
//                              Drifts toward 50 each waking tick.
//
// Both axes use the same drift rate (DriftRate) and recovery on wake (50).
//
// Named, time-limited MoodModifiers are stacked additively per axis.
// Any system that wants to affect a specific axis calls
//   MoodSystem.PushModifier(..., MoodAxis.HappySad)   or
//   MoodSystem.PushModifier(..., MoodAxis.CalmStressed).
// The axis-less overload defaults to HappySad for backward compatibility.
// Duplicate eventId+source pairs refresh duration rather than stacking the delta.
//
// Thresholds (happy/sad axis only):
//   ≥ 80  →  Thriving  →  WorkModifier 1.05
//   ≥ 60  →  Content   →  WorkModifier 1.00
//   ≥ 35  →  Okay      →  WorkModifier 0.95
//   ≥ 20  →  Struggling → WorkModifier 0.90
//   < 20  →  Crisis    →  task queue cleared, RecreationalTask assigned
//   ≥ 25  →  (while in Crisis) crisis ends, normal tasks resume
//
// Crisis state is driven by happy/sad axis only.  The calm/stressed axis
// does not independently trigger crisis.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MoodSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Mood drift rate toward 50 per game tick while awake (both axes).</summary>
        /// <remarks>
        /// Chosen so that an NPC starting at 100 reaches ≈ 25 after 16 waking ticks
        /// (75 ÷ 16 ≈ 4.6875).
        /// </remarks>
        public const float DriftRate             = 4.6875f;

        /// <summary>Both axis scores set on wake — resting fully restores baseline mood.</summary>
        public const float SleepRecoveryScore    = 50f;

        // Threshold boundaries (happy/sad axis)
        public const float ThrivingThreshold     = 80f;
        public const float ContentThreshold      = 60f;
        public const float OkayThreshold         = 35f;
        public const float CrisisThreshold       = 20f;
        public const float CrisisRecoveryThreshold = 25f;

        // WorkModifier values per mood band
        public const float WorkModThriving       = 1.05f;
        public const float WorkModContent        = 1.00f;
        public const float WorkModOkay           = 0.95f;
        public const float WorkModStruggling     = 0.90f;

        // Feature flag — set to false to freeze all mood processing
        public bool Enabled = true;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when an NPC's MoodScore first falls below CrisisThreshold.
        /// Payload: the NPC that entered crisis.
        /// </summary>
        public event Action<NPCInstance> OnNpcEnteredCrisis;

        /// <summary>
        /// Fired when an NPC recovers from crisis (MoodScore ≥ CrisisRecoveryThreshold).
        /// </summary>
        public event Action<NPCInstance> OnNpcRecoveredFromCrisis;

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once per game tick (GameManager.AdvanceTick).
        /// Processes drift, modifier expiry, and threshold evaluation for all crew.
        /// </summary>
        public void Tick(StationState station)
        {
            if (!Enabled) return;

            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                // NPCs on away missions keep their mood but skip social simulation
                TickMood(npc, station.tick);
            }
        }

        private void TickMood(NPCInstance npc, int currentTick)
        {
            // ── Happy/Sad axis ────────────────────────────────────────────────

            // 1. Expire stale modifiers and remove their delta
            ExpireModifiers(npc.moodModifiers, ref npc.moodScore, currentTick);

            // 2. Drift toward 50 only while awake (isSleeping handled by NPCSystem)
            if (!npc.isSleeping)
                ApplyDrift(ref npc.moodScore);

            // 3. Clamp
            npc.moodScore = Mathf.Clamp(npc.moodScore, 0f, 100f);

            // 4. Threshold evaluation → update WorkModifier and crisis flag (happy/sad only)
            EvaluateThresholds(npc);

            // 5. Sync legacy -1..1 mood float from moodScore
            npc.RecalculateMood();

            // ── Calm/Stressed axis ────────────────────────────────────────────

            // Initialise list defensively (saves from null if data migrated from old save)
            if (npc.stressModifiers == null) npc.stressModifiers = new List<MoodModifierRecord>();

            ExpireModifiers(npc.stressModifiers, ref npc.stressScore, currentTick);

            if (!npc.isSleeping)
                ApplyDrift(ref npc.stressScore);

            npc.stressScore = Mathf.Clamp(npc.stressScore, 0f, 100f);

            // ── Feed daily mood accumulator for SanitySystem (both axes) ─────
            SanitySystem.AccumulateMood(npc, npc.moodScore, npc.stressScore);
        }

        private static void ApplyDrift(ref float score)
        {
            if (score > 50f)
                score = Mathf.Max(50f, score - DriftRate);
            else if (score < 50f)
                score = Mathf.Min(50f, score + DriftRate);
        }

        private static void ExpireModifiers(List<MoodModifierRecord> modifiers,
                                             ref float score, int currentTick)
        {
            if (modifiers == null) return;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                var mod = modifiers[i];
                if (mod.expiresAtTick >= 0 && currentTick >= mod.expiresAtTick)
                {
                    // Reverse the delta before removing
                    score -= mod.delta;
                    modifiers.RemoveAt(i);
                }
            }
        }

        private void EvaluateThresholds(NPCInstance npc)
        {
            float score = npc.moodScore;

            // Crisis entry (happy/sad axis only)
            if (score < CrisisThreshold && !npc.inCrisis)
            {
                npc.inCrisis      = true;
                npc.workModifier  = WorkModStruggling;
                npc.jobInterrupted = true;     // force task reassignment → RecreationalTask
                OnNpcEnteredCrisis?.Invoke(npc);
            }
            // Crisis recovery
            else if (npc.inCrisis && score >= CrisisRecoveryThreshold)
            {
                npc.inCrisis       = false;
                npc.jobInterrupted = true;   // force immediate return to normal assignment
                OnNpcRecoveredFromCrisis?.Invoke(npc);
            }

            // WorkModifier bands (only when not in crisis)
            if (!npc.inCrisis)
            {
                if      (score >= ThrivingThreshold) npc.workModifier = WorkModThriving;
                else if (score >= ContentThreshold)  npc.workModifier = WorkModContent;
                else if (score >= OkayThreshold)     npc.workModifier = WorkModOkay;
                else                                 npc.workModifier = WorkModStruggling;
            }
        }

        // ── Sleep / Wake hooks ────────────────────────────────────────────────

        /// <summary>Called by NPCSystem when an NPC transitions to isSleeping = true.</summary>
        public void OnNPCSleeps(NPCInstance npc)
        {
            // Mood drift is already suppressed inside TickMood while isSleeping == true.
            // No explicit action needed here; the hook exists for external subscribers.
        }

        /// <summary>
        /// Called by NPCSystem when an NPC wakes up (isSleeping becomes false).
        /// Resets both axes to SleepRecoveryScore (50) then re-applies any still-active
        /// modifiers so that (a) modifiers pushed on or before wake (e.g. NeedSystem's
        /// "well_rested" boost) are preserved and (b) expiring modifiers later reverse
        /// only the delta that was re-applied here, preventing double-reversal.
        /// </summary>
        public void OnNPCWakes(NPCInstance npc)
        {
            npc.moodScore   = RecomputeFromModifiers(npc.moodModifiers,   SleepRecoveryScore);
            npc.stressScore = RecomputeFromModifiers(npc.stressModifiers, SleepRecoveryScore);
            EvaluateThresholds(npc);
        }

        /// <summary>
        /// Returns baseline + sum of all active modifier deltas, clamped to [0, 100].
        /// Used by OnNPCWakes to keep modifier lists consistent with the reset score.
        /// </summary>
        private static float RecomputeFromModifiers(List<MoodModifierRecord> modifiers, float baseline)
        {
            float score = baseline;
            if (modifiers != null)
                foreach (var m in modifiers)
                    score += m.delta;
            return Mathf.Clamp(score, 0f, 100f);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Push a named mood modifier onto an NPC for the specified axis.
        /// If a modifier with the same eventId and source is already active on that axis,
        /// its duration is refreshed without stacking the delta a second time.
        /// </summary>
        /// <param name="npc">Target NPC.</param>
        /// <param name="eventId">Human-readable event name (e.g. "harvest_success").</param>
        /// <param name="delta">Mood delta to apply (+/-).  Added immediately to the axis score.</param>
        /// <param name="durationTicks">How many game ticks the modifier persists.  -1 = permanent.</param>
        /// <param name="currentTick">Current station tick (used to compute expiry).</param>
        /// <param name="axis">Which mood axis to target.</param>
        /// <param name="source">Optional source tag for deduplication (e.g. system name).</param>
        public void PushModifier(NPCInstance npc, string eventId, float delta,
                                 int durationTicks, int currentTick,
                                 MoodAxis axis, string source = "")
        {
            if (!Enabled) return;

            List<MoodModifierRecord> modifiers;
            if (axis == MoodAxis.CalmStressed)
            {
                if (npc.stressModifiers == null) npc.stressModifiers = new List<MoodModifierRecord>();
                modifiers = npc.stressModifiers;
            }
            else
            {
                if (npc.moodModifiers == null) npc.moodModifiers = new List<MoodModifierRecord>();
                modifiers = npc.moodModifiers;
            }

            // Check for existing duplicate (same eventId + source still active)
            foreach (var existing in modifiers)
            {
                if (existing.eventId == eventId && existing.source == source)
                {
                    // Refresh duration only — do not add delta again
                    existing.expiresAtTick = durationTicks < 0 ? -1 : currentTick + durationTicks;
                    return;
                }
            }

            // New modifier: apply delta immediately and record the entry
            int expiry = durationTicks < 0 ? -1 : currentTick + durationTicks;
            modifiers.Add(new MoodModifierRecord
            {
                eventId       = eventId,
                delta         = delta,
                expiresAtTick = expiry,
                source        = source
            });

            if (axis == MoodAxis.CalmStressed)
                npc.stressScore = Mathf.Clamp(npc.stressScore + delta, 0f, 100f);
            else
            {
                npc.moodScore = Mathf.Clamp(npc.moodScore + delta, 0f, 100f);
                // Immediately re-evaluate thresholds after a modifier push to happy/sad axis
                EvaluateThresholds(npc);
            }
        }

        /// <summary>
        /// Push a named mood modifier onto the happy/sad axis (backward-compatible overload).
        /// </summary>
        public void PushModifier(NPCInstance npc, string eventId, float delta,
                                 int durationTicks, int currentTick, string source = "")
        {
            PushModifier(npc, eventId, delta, durationTicks, currentTick, MoodAxis.HappySad, source);
        }

        /// <summary>
        /// Immediately reverses and removes the mood modifier with the given eventId and source
        /// on the specified axis.  No-op if no such modifier exists.
        /// </summary>
        public void RemoveModifier(NPCInstance npc, string eventId, MoodAxis axis, string source = "")
        {
            List<MoodModifierRecord> modifiers = axis == MoodAxis.CalmStressed
                ? npc.stressModifiers : npc.moodModifiers;

            if (modifiers == null) return;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                var mod = modifiers[i];
                if (mod.eventId == eventId && mod.source == source)
                {
                    if (axis == MoodAxis.CalmStressed)
                        npc.stressScore = Mathf.Clamp(npc.stressScore - mod.delta, 0f, 100f);
                    else
                    {
                        npc.moodScore = Mathf.Clamp(npc.moodScore - mod.delta, 0f, 100f);
                        EvaluateThresholds(npc);
                    }
                    modifiers.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Immediately reverses and removes a mood modifier on the happy/sad axis
        /// (backward-compatible overload).
        /// </summary>
        public void RemoveModifier(NPCInstance npc, string eventId, string source = "")
        {
            RemoveModifier(npc, eventId, MoodAxis.HappySad, source);
        }

        // ── Station-wide aggregate ────────────────────────────────────────────

        /// <summary>
        /// Returns the station-wide morale score (0–100): the mean happy/sad score
        /// across all active crew members.  Returns 50 when no crew are present.
        /// This value feeds into ResourceSystem production scaling (INF-006).
        /// </summary>
        public static float GetStationMorale(StationState station)
        {
            float sum  = 0f;
            int   count = 0;
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                sum += npc.moodScore;
                count++;
            }
            return count == 0 ? 50f : sum / count;
        }

        // ── Per-NPC modifier breakdown ────────────────────────────────────────

        /// <summary>
        /// Returns a snapshot of all active modifiers on both axes for the given NPC,
        /// each annotated with the axis it belongs to.
        /// Suitable for UI inspector display.
        /// </summary>
        public static List<(MoodAxis axis, MoodModifierRecord record)> GetModifierBreakdown(
            NPCInstance npc)
        {
            var result = new List<(MoodAxis, MoodModifierRecord)>();
            if (npc.moodModifiers != null)
                foreach (var m in npc.moodModifiers)
                    result.Add((MoodAxis.HappySad, m));
            if (npc.stressModifiers != null)
                foreach (var m in npc.stressModifiers)
                    result.Add((MoodAxis.CalmStressed, m));
            return result;
        }

        // ── Static helpers ────────────────────────────────────────────────────

        /// <summary>Returns the threshold label for a given MoodScore (happy/sad axis).</summary>
        public static string GetThresholdLabel(float score)
        {
            if (score >= ThrivingThreshold)   return "Thriving";
            if (score >= ContentThreshold)    return "Content";
            if (score >= OkayThreshold)       return "Okay";
            if (score >= CrisisThreshold)     return "Struggling";
            return "Crisis";
        }

        /// <summary>
        /// Returns the UI bar colour for a given MoodScore (happy/sad axis).
        /// Green ≥ 60, yellow ≥ 35, red otherwise.
        /// </summary>
        public static Color GetMoodColor(float score)
        {
            if (score >= ContentThreshold)    return new Color(0.22f, 0.76f, 0.35f);
            if (score >= CrisisThreshold)     return new Color(0.88f, 0.68f, 0.10f);
            return new Color(0.86f, 0.26f, 0.26f);
        }
    }
}
