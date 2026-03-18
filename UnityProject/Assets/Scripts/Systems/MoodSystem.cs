// MoodSystem — manages the 0–100 MoodScore for every crew NPC.
//
// MoodScore drifts toward 50 at a rate that produces a 75-point drop over
// a 16-tick waking day (starting at 100, ending near 25).  Drift is suspended
// while an NPC is asleep.  On waking, MoodScore is reset to 50 (rested).
//
// Named, time-limited MoodModifiers are stacked additively.  Any system that
// wants to affect crew morale calls MoodSystem.PushModifier().  Duplicate
// eventId+source pairs refresh duration rather than stacking the delta.
//
// Thresholds:
//   ≥ 80  →  Thriving  →  WorkModifier 1.05
//   ≥ 60  →  Content   →  WorkModifier 1.00
//   ≥ 35  →  Okay      →  WorkModifier 0.95
//   ≥ 20  →  Struggling → WorkModifier 0.90
//   < 20  →  Crisis    →  task queue cleared, RecreationalTask assigned
//   ≥ 25  →  (while in Crisis) crisis ends, normal tasks resume
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MoodSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Mood drift rate toward 50 per game tick while awake.</summary>
        /// <remarks>
        /// Chosen so that an NPC starting at 100 reaches ≈ 25 after 16 waking ticks
        /// (75 ÷ 16 ≈ 4.6875).
        /// </remarks>
        public const float DriftRate             = 4.6875f;

        /// <summary>MoodScore set on wake — resting fully restores baseline mood.</summary>
        public const float SleepRecoveryScore    = 50f;

        // Threshold boundaries
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
            // 1. Expire stale modifiers and remove their delta
            ExpireModifiers(npc, currentTick);

            // 2. Drift toward 50 only while awake (isSleeping handled by NPCSystem)
            if (!npc.isSleeping)
                ApplyDrift(npc);

            // 3. Clamp
            npc.moodScore = Mathf.Clamp(npc.moodScore, 0f, 100f);

            // 4. Threshold evaluation → update WorkModifier and crisis flag
            EvaluateThresholds(npc);
        }

        private static void ApplyDrift(NPCInstance npc)
        {
            if (npc.moodScore > 50f)
                npc.moodScore = Mathf.Max(50f, npc.moodScore - DriftRate);
            else if (npc.moodScore < 50f)
                npc.moodScore = Mathf.Min(50f, npc.moodScore + DriftRate);
        }

        private static void ExpireModifiers(NPCInstance npc, int currentTick)
        {
            if (npc.moodModifiers == null) return;
            for (int i = npc.moodModifiers.Count - 1; i >= 0; i--)
            {
                var mod = npc.moodModifiers[i];
                if (mod.expiresAtTick >= 0 && currentTick >= mod.expiresAtTick)
                {
                    // Reverse the delta before removing
                    npc.moodScore -= mod.delta;
                    npc.moodModifiers.RemoveAt(i);
                }
            }
        }

        private void EvaluateThresholds(NPCInstance npc)
        {
            float score = npc.moodScore;
            bool wasCrisis = npc.inCrisis;

            // Crisis entry
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
                npc.inCrisis = false;
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
        /// Resets MoodScore to SleepRecoveryScore (50) so rest is rewarding.
        /// </summary>
        public void OnNPCWakes(NPCInstance npc)
        {
            npc.moodScore = SleepRecoveryScore;
            // Re-evaluate thresholds immediately so WorkModifier is correct from tick 1
            EvaluateThresholds(npc);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Push a named mood modifier onto an NPC.  If a modifier with the same
        /// eventId and source is already active, its duration is refreshed without
        /// stacking the delta a second time.
        /// </summary>
        /// <param name="npc">Target NPC.</param>
        /// <param name="eventId">Human-readable event name (e.g. "harvest_success").</param>
        /// <param name="delta">Mood delta to apply (+/-).  Added immediately to moodScore.</param>
        /// <param name="durationTicks">How many game ticks the modifier persists.  -1 = permanent.</param>
        /// <param name="source">Optional source tag for deduplication (e.g. system name).</param>
        /// <param name="currentTick">Current station tick (used to compute expiry).</param>
        public void PushModifier(NPCInstance npc, string eventId, float delta,
                                 int durationTicks, int currentTick, string source = "")
        {
            if (!Enabled) return;
            if (npc.moodModifiers == null) npc.moodModifiers = new List<MoodModifierRecord>();

            // Check for existing duplicate (same eventId + source still active)
            foreach (var existing in npc.moodModifiers)
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
            npc.moodModifiers.Add(new MoodModifierRecord
            {
                eventId       = eventId,
                delta         = delta,
                expiresAtTick = expiry,
                source        = source
            });
            npc.moodScore = Mathf.Clamp(npc.moodScore + delta, 0f, 100f);

            // Immediately re-evaluate thresholds after a modifier push
            EvaluateThresholds(npc);
        }

        // ── Static helpers ────────────────────────────────────────────────────

        /// <summary>Returns the threshold label for a given MoodScore.</summary>
        public static string GetThresholdLabel(float score)
        {
            if (score >= ThrivingThreshold)   return "Thriving";
            if (score >= ContentThreshold)    return "Content";
            if (score >= OkayThreshold)       return "Okay";
            if (score >= CrisisThreshold)     return "Struggling";
            return "Crisis";
        }

        /// <summary>
        /// Returns the UI bar colour for a given MoodScore.
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
