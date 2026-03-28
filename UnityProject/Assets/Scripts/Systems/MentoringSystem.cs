// MentoringSystem — NPC-008: Mentor/Student bond formation and mentoring XP.
//
// Tracks co-working ticks for NPC pairs sharing a room.  Automatically forms a
// Mentor/Student bond (stored as RelationshipType.Mentor, a Friend sub-type) when:
//   - One NPC's highest skill level is >= MentorMinSkillLevel (8)
//   - That level exceeds the other NPC's highest skill by >= SkillLevelGapRequired (3)
//   - The pair's RelationshipType is already Friend or better (affinity >= 20)
//   - The pair accumulates >= CoWorkingTicksThreshold ticks of sharing a room
//
// Mentoring XP multiplier applied to student skill XP when the mentor is present:
//   multiplier = 1 + (skillFactor×SkillWeight + commFactor×CommWeight
//                     + affinFactor×AffinityWeight) × moodMultiplier
//   where:
//     skillFactor    = mentorHighestSkillLevel / MaxSkillLevel
//     commFactor     = mentorCommunicationSkillLevel / MaxSkillLevel
//     affinFactor    = Clamp(affinityScore / 100, 0, 1)
//     moodMultiplier = Clamp(mentorMoodScore / 50, 0, 2)
//
// Proximity work speed bonus applies automatically: RelationshipType.Mentor is
// treated as a Friend sub-type in ProximitySystem.ApplyProximityEffect.
//
// Bond decay: handled in RelationshipRegistry.DecayAll — if the pair has not
// co-worked for >= 7 in-game days (DecayIntervalTicks), the Mentor designation
// is cleared and the type reverts to the affinity-derived type (Friend or lower).
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MentoringSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// Minimum highest-skill level a potential mentor must have.
        /// Defined in balance data (NPC-008).
        /// </summary>
        public const int MentorMinSkillLevel = 8;

        /// <summary>
        /// The mentor's highest skill must exceed the student's highest skill by
        /// at least this many levels for a bond to form.
        /// </summary>
        public const int SkillLevelGapRequired = 3;

        /// <summary>
        /// Co-working ticks required to form a Mentor/Student bond.
        /// Corresponds to ~4 in-game days of continuous co-working (4 × 24 = 96 ≈ 100).
        /// </summary>
        public const int CoWorkingTicksThreshold = 100;

        // XP multiplier weights (see file header for formula)
        private const float SkillWeight    = 0.5f;
        private const float CommWeight     = 0.3f;
        private const float AffinityWeight = 0.2f;
        private const int   MaxSkillLevel  = 20;

        /// <summary>
        /// Skill ID used for the Communication component of the XP multiplier.
        /// Maps to 'skill.social' in core_skills.json.
        /// </summary>
        public const string CommunicationSkillId = "skill.social";

        // ── Feature flag ──────────────────────────────────────────────────────

        /// <summary>Set false to disable bond formation and XP multiplier without removing existing bonds.</summary>
        public bool Enabled = true;

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once per game tick.  Scans room-sharing NPC pairs, increments
        /// co-working ticks, and forms Mentor/Student bonds at threshold.
        /// </summary>
        public void Tick(StationState station)
        {
            if (!Enabled) return;

            var crew = station.GetCrew();
            if (crew.Count < 2) return;

            // Group crew by location so we only compare same-room pairs.
            var byRoom = new Dictionary<string, List<NPCInstance>>();
            foreach (var npc in crew)
            {
                if (npc.missionUid != null) continue;
                if (string.IsNullOrEmpty(npc.location)) continue;
                if (!byRoom.TryGetValue(npc.location, out var list))
                    byRoom[npc.location] = list = new List<NPCInstance>();
                list.Add(npc);
            }

            foreach (var kvp in byRoom)
            {
                var occupants = kvp.Value;
                if (occupants.Count < 2) continue;

                for (int i = 0; i < occupants.Count; i++)
                {
                    for (int j = i + 1; j < occupants.Count; j++)
                    {
                        ProcessPair(station, occupants[i], occupants[j]);
                    }
                }
            }
        }

        // ── XP Multiplier ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the mentoring XP multiplier for <paramref name="student"/>.
        /// Returns 1.0 if:
        ///   • no Mentor/Student bond exists where this NPC is the student, OR
        ///   • the mentor is not currently in the same room.
        /// </summary>
        public float GetMentoringXPMultiplier(NPCInstance student, StationState station)
        {
            if (!Enabled || student == null) return 1f;

            foreach (var rec in station.relationships.Values)
            {
                if (rec.relationshipType != RelationshipType.Mentor) continue;
                if (rec.mentorUid == null) continue;
                if (rec.mentorUid == student.uid) continue;   // this NPC is the mentor

                // Confirm student is a party to this record.
                bool studentInBond = rec.npcUid1 == student.uid || rec.npcUid2 == student.uid;
                if (!studentInBond) continue;

                // Mentor must be present in the same room.
                if (!station.npcs.TryGetValue(rec.mentorUid, out var mentor)) continue;
                if (string.IsNullOrEmpty(mentor.location) ||
                    mentor.location != student.location) continue;

                return ComputeMultiplier(mentor, rec.affinityScore);
            }

            return 1f;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void ProcessPair(StationState station, NPCInstance a, NPCInstance b)
        {
            var rec = RelationshipRegistry.GetOrCreate(station, a.uid, b.uid);

            // Increment co-working ticks and refresh timestamps.
            rec.coWorkingTicks++;
            rec.lastCoWorkingTick   = station.tick;
            rec.lastInteractionTick = station.tick;

            // Bond already formed — nothing more to do this tick.
            if (rec.relationshipType == RelationshipType.Mentor) return;

            // Bond can only form between Friends or better.
            if (rec.affinityScore < 20f) return;

            // Threshold not yet reached.
            if (rec.coWorkingTicks < CoWorkingTicksThreshold) return;

            // Determine who qualifies as mentor (higher skilled, above min threshold,
            // and sufficiently above the other NPC's skill level).
            int maxA = GetHighestSkillLevel(a);
            int maxB = GetHighestSkillLevel(b);

            NPCInstance mentor  = null;
            NPCInstance student = null;

            if (maxA >= MentorMinSkillLevel && (maxA - maxB) >= SkillLevelGapRequired)
            {
                mentor  = a;
                student = b;
            }
            else if (maxB >= MentorMinSkillLevel && (maxB - maxA) >= SkillLevelGapRequired)
            {
                mentor  = b;
                student = a;
            }

            if (mentor == null) return;

            // Form the bond.
            rec.relationshipType = RelationshipType.Mentor;
            rec.mentorUid        = mentor.uid;

            station.LogEvent(
                $"{mentor.name} has taken {student.name} under their wing as a mentor.");
        }

        /// <summary>Returns the highest current skill level across all skill instances.</summary>
        internal static int GetHighestSkillLevel(NPCInstance npc)
        {
            int max = 0;
            if (npc.skillInstances == null) return 0;
            foreach (var inst in npc.skillInstances)
                if (inst.Level > max) max = inst.Level;
            return max;
        }

        private static float ComputeMultiplier(NPCInstance mentor, float affinityScore)
        {
            int   skillLevel  = GetHighestSkillLevel(mentor);
            int   commLevel   = SkillSystem.GetSkillLevel(mentor, CommunicationSkillId);
            float skillFactor = (float)skillLevel / MaxSkillLevel;
            float commFactor  = (float)commLevel  / MaxSkillLevel;
            float affinFactor = Mathf.Clamp01(affinityScore / 100f);

            float bonus = skillFactor * SkillWeight
                        + commFactor  * CommWeight
                        + affinFactor * AffinityWeight;

            // Mood multiplier: 1.0 at baseline (50), 0 at floor, 2.0 at ceiling.
            // In crisis (moodScore < 20) the multiplier is < 0.4, visibly reducing the bonus.
            float moodMultiplier = Mathf.Clamp(mentor.moodScore / 50f, 0f, 2f);

            return 1f + bonus * moodMultiplier;
        }
    }
}
