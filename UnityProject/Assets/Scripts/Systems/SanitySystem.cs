// SanitySystem — evaluates NPC mental health on a daily boundary.
//
// Scoring model (per spec):
//   • Ceiling = WIS modifier of the NPC (can be negative or positive)
//   • Floor   = -10
//   • +1 sanity per day when average mood ≥ 34 AND ≥ 3 needs above 50
//   • -1 sanity per day when any need was fully depleted OR average mood ≤ 0
//   • -1 sanity per day when isInBreakdown and no intervention
//
// Breakdown threshold: sanity ≤ -5
// Recovery: sanity rising above 0 clears breakdown flag
// MoodSystem calls AccumulateMood() every tick.
// GameManager calls Tick() on the daily boundary (tick % 96 == 0).
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class SanitySystem
    {
        // ── Main daily tick ───────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            bool isDayBoundary = station.tick % 96 == 0;
            if (!isDayBoundary) return;

            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                var san = npc.GetOrCreateSanity();

                // Sync ceiling to current WIS modifier (WIS can change from trait events)
                san.ceiling = AbilityScores.GetModifier(npc.abilityScores.WIS);

                TickDay(npc, san, station);
            }
        }

        private static void TickDay(NPCInstance npc, SanityProfile san, StationState station)
        {
            float averageMood = san.dailyMoodSampleCount > 0
                ? san.dailyMoodAccumulator / san.dailyMoodSampleCount
                : 50f;

            bool positiveDay = averageMood >= 34f && san.needsAbove50Count >= 3;
            bool negativeDay = san.needDepletedThisCycle || averageMood <= 0f;

            if (positiveDay && san.score < san.ceiling)
                san.score++;
            else if (negativeDay && san.score > -10)
                san.score--;

            // Breakdown state
            if (!san.isInBreakdown && san.score <= -5)
            {
                san.isInBreakdown = true;
                station.LogEvent($"⚠ {npc.name} is having a mental breakdown.");
            }
            else if (san.isInBreakdown && san.score > 0)
            {
                san.isInBreakdown        = false;
                san.requiresIntervention = false;
            }

            // Breakdown persists → additional -1/day unless counselled
            if (san.isInBreakdown && !san.requiresIntervention && san.score > -10)
            {
                san.score--;
            }

            // Reset daily accumulators
            san.dailyMoodAccumulator   = 0f;
            san.dailyMoodSampleCount   = 0;
            san.needDepletedThisCycle  = false;
            san.needsAbove50Count      = 0;
        }

        // ── Called by MoodSystem every tick ──────────────────────────────────

        public static void AccumulateMood(NPCInstance npc, float moodScore)
        {
            var san = npc.sanity;
            if (san == null) return;
            san.dailyMoodAccumulator += moodScore;
            san.dailyMoodSampleCount++;
        }

        // ── Called by NeedSystem on daily boundary per NPC ───────────────────

        public void RegisterNeedDepleted(NPCInstance npc, StationState station)
        {
            var san = npc.GetOrCreateSanity();
            san.needDepletedThisCycle = true;
        }

        public void RegisterNeedsSatisfied(NPCInstance npc, int countAbove50, StationState station)
        {
            var san = npc.GetOrCreateSanity();
            san.needsAbove50Count = Mathf.Max(san.needsAbove50Count, countAbove50);
        }

        // ── Counselling intervention (stub — wired by future EventSystem) ─────

        /// <summary>Marks that a counselling interaction has taken place, halting passive breakdown drain.</summary>
        public static void RegisterIntervention(NPCInstance npc)
        {
            var san = npc.sanity;
            if (san == null) return;
            san.requiresIntervention = false;
        }
    }
}
