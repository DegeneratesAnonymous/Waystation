// RelationshipRegistry — manages all pairwise NPC relationships.
//
// Relationships are stored in StationState.relationships so they are part of
// the save data.  This class provides mutation helpers and the per-tick decay
// logic so it integrates cleanly with the GameManager tick loop.
//
// Affinity thresholds (see RelationshipRecord.UpdateTypeFromAffinity):
//   ≥  60  → Lover      (may trigger marriage event)
//   ≥  40  → Lover
//   ≥  20  → Friend
//   ≥   5  → Acquaintance
//   ≤  -5  → Enemy
//   other  → None
//
// Decay: if two NPCs have not interacted for ≥ 7 in-game days (7 × 24 = 168 ticks),
// affinityScore moves 1 point toward 0 per decay tick.
// DecayEnabled can be toggled without affecting any other logic.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class RelationshipRegistry
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>7 in-game days in ticks before affinity starts decaying.</summary>
        public const int DecayIntervalTicks = 7 * TimeSystem.TicksPerDay;

        /// <summary>
        /// How many ticks pass between marriage event re-fires when not yet approved.
        /// 3 in-game days.
        /// </summary>
        public const int MarriageEventRecheckTicks = 3 * TimeSystem.TicksPerDay;

        public bool DecayEnabled = true;

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once per game tick.  Runs affinity decay and checks marriage events.
        /// </summary>
        public void Tick(StationState station, MoodSystem moodSystem)
        {
            DecayAll(station);
            CheckMarriageEvents(station, moodSystem);
        }

        // ── Affinity helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the relationship record for a pair, creating a fresh one if absent.
        /// Keys are always stored in lexicographic order so (A,B) == (B,A).
        /// </summary>
        public static RelationshipRecord GetOrCreate(StationState station, string uid1, string uid2)
        {
            string key = RelationshipRecord.MakeKey(uid1, uid2);
            if (!station.relationships.TryGetValue(key, out var rec))
            {
                rec = new RelationshipRecord { npcUid1 = uid1, npcUid2 = uid2 };
                station.relationships[key] = rec;
            }
            return rec;
        }

        /// <summary>
        /// Returns the existing record for a pair, or null if none exists.
        /// </summary>
        public static RelationshipRecord Get(StationState station, string uid1, string uid2)
        {
            string key = RelationshipRecord.MakeKey(uid1, uid2);
            station.relationships.TryGetValue(key, out var rec);
            return rec;
        }

        /// <summary>
        /// Modifies the affinity between two NPCs by delta and updates the type.
        /// </summary>
        public static RelationshipRecord ModifyAffinity(StationState station,
                                                         string uid1, string uid2,
                                                         float delta, int currentTick)
        {
            var rec = GetOrCreate(station, uid1, uid2);
            rec.affinityScore          = Mathf.Clamp(rec.affinityScore + delta, -100f, 100f);
            rec.lastInteractionTick    = currentTick;
            rec.UpdateTypeFromAffinity();
            return rec;
        }

        // ── Decay ─────────────────────────────────────────────────────────────

        private void DecayAll(StationState station)
        {
            if (!DecayEnabled) return;

            foreach (var rec in station.relationships.Values)
            {
                // ── Mentor bond 7-day co-working inactivity check ─────────────
                // If a Mentor/Student bond exists and the pair has not co-worked for
                // >= DecayIntervalTicks, clear the Mentor designation and re-derive
                // the type from affinity (drops back to Friend or lower).
                if (rec.relationshipType == RelationshipType.Mentor &&
                    rec.lastCoWorkingTick >= 0)
                {
                    int ticksSinceCoWork = station.tick - rec.lastCoWorkingTick;
                    if (ticksSinceCoWork >= DecayIntervalTicks)
                    {
                        rec.ClearMentorBond();

                        if (station.npcs.TryGetValue(rec.npcUid1, out var n1) &&
                            station.npcs.TryGetValue(rec.npcUid2, out var n2))
                        {
                            station.LogEvent(
                                $"{n1.name} and {n2.name}'s mentor/student bond has lapsed.");
                        }
                    }
                }

                if (rec.affinityScore == 0f) continue;

                int ticksSinceInteraction = station.tick - rec.lastInteractionTick;
                if (ticksSinceInteraction < DecayIntervalTicks) continue;

                // Move 1 point toward 0 and advance the interaction tick so decay
                // fires at most once per interval (not every tick after the interval).
                if (rec.affinityScore > 0f)
                    rec.affinityScore = Mathf.Max(0f, rec.affinityScore - 1f);
                else
                    rec.affinityScore = Mathf.Min(0f, rec.affinityScore + 1f);

                // Advance by one interval so the next decay fires in another 7 days
                rec.lastInteractionTick += DecayIntervalTicks;

                rec.UpdateTypeFromAffinity();
            }
        }

        // ── Marriage events ───────────────────────────────────────────────────

        private void CheckMarriageEvents(StationState station, MoodSystem moodSystem)
        {
            foreach (var rec in station.relationships.Values)
            {
                if (rec.married) continue;
                if (rec.relationshipType != RelationshipType.Lover) continue;
                if (rec.affinityScore < 60f) continue;

                // Determine whether to (re-)fire the marriage prompt
                bool shouldFire = !rec.marriageEventPending &&
                                  (rec.lastMarriageEventTick < 0 ||
                                   station.tick - rec.lastMarriageEventTick >= MarriageEventRecheckTicks);

                if (!shouldFire) continue;

                // Check the pair hasn't already been added to pending
                string key = RelationshipRecord.MakeKey(rec.npcUid1, rec.npcUid2);
                if (station.pendingMarriageEvents.Contains(key)) continue;

                station.pendingMarriageEvents.Add(key);
                rec.marriageEventPending  = true;
                rec.lastMarriageEventTick = station.tick;

                // Log to station
                if (station.npcs.TryGetValue(rec.npcUid1, out var npc1) &&
                    station.npcs.TryGetValue(rec.npcUid2, out var npc2))
                {
                    station.LogEvent(
                        $"{npc1.name} and {npc2.name} wish to be married. " +
                        "Check the Crew → Relationships panel to approve.");
                }
            }
        }

        // ── Player action: approve / dismiss marriage ─────────────────────────

        /// <summary>
        /// Called when the player approves a marriage event.
        /// Sets RelationshipType to Spouse and removes the pending notification.
        /// </summary>
        public static void ApproveMarriage(StationState station, string uid1, string uid2,
                                            MoodSystem moodSystem, int currentTick)
        {
            string key = RelationshipRecord.MakeKey(uid1, uid2);
            station.pendingMarriageEvents.Remove(key);

            var rec = GetOrCreate(station, uid1, uid2);
            rec.married               = true;
            rec.marriageEventPending  = false;
            rec.relationshipType      = RelationshipType.Spouse;

            // Wedding mood boost for both parties
            if (station.npcs.TryGetValue(uid1, out var n1))
                moodSystem?.PushModifier(n1, "wedding_boost", 20f, 72, currentTick, "relationship");
            if (station.npcs.TryGetValue(uid2, out var n2))
                moodSystem?.PushModifier(n2, "wedding_boost", 20f, 72, currentTick, "relationship");

            station.LogEvent($"{(station.npcs.TryGetValue(uid1, out var na) ? na.name : uid1)} " +
                             $"and {(station.npcs.TryGetValue(uid2, out var nb) ? nb.name : uid2)} " +
                             "are now married.");
        }

        /// <summary>Dismisses a pending marriage prompt without approving it.</summary>
        public static void DismissMarriage(StationState station, string uid1, string uid2)
        {
            string key = RelationshipRecord.MakeKey(uid1, uid2);
            station.pendingMarriageEvents.Remove(key);

            var rec = Get(station, uid1, uid2);
            if (rec != null) rec.marriageEventPending = false;
        }
    }
}
