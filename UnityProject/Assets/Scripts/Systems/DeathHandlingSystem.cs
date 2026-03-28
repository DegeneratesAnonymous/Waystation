// DeathHandlingSystem — physical death consequences when a crew member dies.
//
// Death is a meaningful event with social, logistical, and environmental
// consequences.  When an NPC dies this system:
//   1. Spawns a BodyInstance at the death tile with the correct location tag.
//   2. Injects a named proximity mood modifier to all NPCs in the same module.
//   3. Fires relationship-specific grief modifiers for Friend, Lover, Spouse,
//      and Family (mother/father/sibling) relationships.
//   4. Generates a body haul task — assigned to an eligible NPC or blocked
//      when no disposal tile is designated.
//   5. Each tick: applies an escalating mood penalty to NPCs near an unhandled
//      body once the configurable threshold is exceeded.
//   6. On haul completion: removes the body and clears all body-related
//      mood modifiers from every NPC.
//
// Feature gate: FeatureFlags.NpcDeathHandling
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class DeathHandlingSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// One-time mood penalty pushed to NPCs in the same module at the moment
        /// a body spawns ("witnessed a death").  Duration is in ticks.
        /// 72 = TimeSystem.TicksPerDay / 5  (~⅕ in-game day)
        /// </summary>
        public const float WitnessedDeathPenalty         = -10f;
        public const int   WitnessedDeathDurationTicks   =  72;

        /// <summary>
        /// Per-tick proximity penalty pushed while a body is in the same module.
        /// Duration is short (3 ticks) so it expires quickly when the NPC moves away.
        /// </summary>
        public const float BodyPresentPenalty            = -5f;
        public const int   BodyPresentDurationTicks      =  3;

        /// <summary>
        /// Grief modifier for close-relationship NPCs on the death tick.
        /// 240 = TimeSystem.TicksPerDay * 2 / 3  (~⅔ in-game day)
        /// </summary>
        public const float FriendDeathPenalty            = -15f;
        public const float LoverDeathPenalty             = -20f;
        public const float SpouseDeathPenalty            = -25f;
        public const float FamilyDeathPenalty            = -20f;
        public const int   GriefDurationTicks            = 240;

        /// <summary>
        /// Ticks after body spawns before an escalating penalty begins.
        /// Configurable at runtime (e.g. via debug/difficulty settings).
        /// Default 48 = TimeSystem.TicksPerDay / 8  (~⅛ in-game day)
        /// </summary>
        public static int UnhandledEscalationThresholdTicks = 48;

        /// <summary>Additional mood delta added per escalation step.</summary>
        public const float EscalatingPenaltyPerStep  = -5f;

        /// <summary>
        /// Ticks between each escalation step.
        /// 24 = TimeSystem.TicksPerDay / 15  (~1/15 in-game day)
        /// </summary>
        public const int   EscalationStepIntervalTicks = 24;

        /// <summary>
        /// Maximum number of escalation steps (caps the escalating penalty).
        /// At 5 steps × -5f the penalty reaches -25f over the base -5f = -30f total.
        /// </summary>
        public const int   MaxEscalationSteps = 5;

        /// <summary>Ticks a haul assignment is held before JobSystem may re-claim the NPC.</summary>
        public const int   HaulJobTimerRefresh       = 10;

        /// <summary>Total ticks for an NPC to complete a body haul task.</summary>
        public const int   HaulBodyDurationTicks     = 20;

        // ── Dependencies ──────────────────────────────────────────────────────

        private MoodSystem _mood;

        public void SetMoodSystem(MoodSystem m) => _mood = m;

        // ── On NPC Death ──────────────────────────────────────────────────────

        /// <summary>
        /// Called when an NPC dies.  Spawns a body, applies immediate mood
        /// consequences, fires relationship grief events, and queues a haul task.
        /// </summary>
        public void OnNPCDied(NPCInstance npc, StationState station)
        {
            if (!FeatureFlags.NpcDeathHandling) return;
            if (npc == null || station == null) return;

            // 1. Spawn body at death tile.
            var body = BodyInstance.Create(npc, station.tick);
            station.bodies[body.uid] = body;
            station.LogEvent($"A body has been left where {npc.name} died.");

            // 2. Witnessed-death mood penalty to all nearby NPCs.
            ApplyWitnessedDeathMood(body, station);

            // 3. Close-relationship grief modifier.
            FireRelationshipDeathEvents(npc, station);

            // 4. Generate haul task.
            GenerateHaulTask(body, station);
        }

        // ── Per-Tick ──────────────────────────────────────────────────────────

        /// <summary>
        /// Advances haul timers, escalates mood penalties for unhandled bodies,
        /// refreshes per-tick proximity modifiers, and removes completed bodies.
        /// </summary>
        public void Tick(StationState station)
        {
            if (!FeatureFlags.NpcDeathHandling) return;
            if (station == null) return;

            var toRemove = new List<string>();

            foreach (var kv in station.bodies)
            {
                var body = kv.Value;

                if (body.haulerNpcUid != null)
                {
                    // Advance haul and check for completion.
                    TickHaul(body, station, toRemove);
                }
                else
                {
                    // Try (re-)assigning a haul task if the disposal tile was just designated.
                    if (body.haulBlocked && station.disposalTileDesignated)
                    {
                        body.haulBlocked = false;
                        body.haulTaskGenerated = false;
                    }

                    if (!body.haulTaskGenerated)
                        GenerateHaulTask(body, station);

                    // Per-tick proximity modifier (refreshes while NPCs are nearby).
                    TickBodyPresenceMood(body, station);

                    // Escalating penalty for long-unhandled bodies.
                    TickEscalation(body, station);
                }
            }

            foreach (var uid in toRemove)
                station.bodies.Remove(uid);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ApplyWitnessedDeathMood(BodyInstance body, StationState station)
        {
            if (_mood == null) return;
            foreach (var npc in station.npcs.Values)
            {
                if (npc.statusTags.Contains("dead")) continue;
                if (!npc.IsCrew()) continue;
                if (npc.location != body.location) continue;

                _mood.PushModifier(npc, WitnessedDeathEventId(body.uid),
                                   WitnessedDeathPenalty,
                                   WitnessedDeathDurationTicks,
                                   station.tick, "death_handling");
            }
        }

        private void FireRelationshipDeathEvents(NPCInstance deceased, StationState station)
        {
            foreach (var other in station.npcs.Values)
            {
                if (other.uid == deceased.uid) continue;
                if (other.statusTags.Contains("dead")) continue;
                if (!other.IsCrew()) continue;

                float moodDelta = 0f;
                string eventId  = null;

                // Check relationship record for Friend, Lover, Spouse.
                var rec = RelationshipRegistry.Get(station, deceased.uid, other.uid);
                if (rec != null)
                {
                    switch (rec.relationshipType)
                    {
                        case RelationshipType.Friend:
                            moodDelta = FriendDeathPenalty;
                            eventId   = $"death_of_friend_{deceased.uid}";
                            break;
                        case RelationshipType.Lover:
                            moodDelta = LoverDeathPenalty;
                            eventId   = $"death_of_lover_{deceased.uid}";
                            break;
                        case RelationshipType.Spouse:
                            moodDelta = SpouseDeathPenalty;
                            eventId   = $"death_of_spouse_{deceased.uid}";
                            break;
                    }
                }

                // Check biological family (mother, father, sibling) if not already covered.
                if (eventId == null && IsFamily(deceased, other))
                {
                    moodDelta = FamilyDeathPenalty;
                    eventId   = $"death_of_family_{deceased.uid}";
                }

                if (eventId != null && _mood != null)
                {
                    _mood.PushModifier(other, eventId, moodDelta,
                                       GriefDurationTicks, station.tick, "death_handling");
                    station.LogEvent($"{other.name} is grieving the death of {deceased.name}.");
                }
            }
        }

        private static bool IsFamily(NPCInstance deceased, NPCInstance other)
        {
            // deceased is parent of other
            if (!string.IsNullOrEmpty(other.motherId) && other.motherId == deceased.uid) return true;
            if (!string.IsNullOrEmpty(other.fatherId) && other.fatherId == deceased.uid) return true;
            // other is parent of deceased
            if (!string.IsNullOrEmpty(deceased.motherId) && deceased.motherId == other.uid) return true;
            if (!string.IsNullOrEmpty(deceased.fatherId) && deceased.fatherId == other.uid) return true;
            // siblings
            if (deceased.siblingIds != null && deceased.siblingIds.Contains(other.uid)) return true;
            if (other.siblingIds    != null && other.siblingIds.Contains(deceased.uid)) return true;
            return false;
        }

        private void GenerateHaulTask(BodyInstance body, StationState station)
        {
            body.haulTaskGenerated = true;

            if (!station.disposalTileDesignated)
            {
                body.haulBlocked = true;
                station.LogEvent(
                    $"No disposal site is designated — body haul task for {body.npcName} is blocked. " +
                    "Designate a disposal tile to proceed.");
                return;
            }

            // Find an eligible crew member to haul the body.
            var hauler = FindHauler(station, body);
            if (hauler == null)
            {
                // No eligible NPC available right now.  Reset haulTaskGenerated so
                // the next Tick() call retries assignment until a hauler is found.
                body.haulTaskGenerated = false;
                return;
            }

            AssignHaulJob(hauler, body, station);
        }

        private static NPCInstance FindHauler(StationState station, BodyInstance body)
        {
            // Prefer a crew NPC that is not in crisis, not on a mission, and not
            // already hauling another body.  STR/END/INT passive preference is
            // implemented by choosing the highest combined ability score.
            NPCInstance best      = null;
            int         bestScore = int.MinValue;

            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.statusTags.Contains("dead")) continue;
                if (npc.inCrisis) continue;
                if (npc.missionUid != null) continue;
                if (npc.currentJobId == "job.haul_body") continue;  // already hauling

                // STR + END + INT composite (reflects hauling aptitude per NPC-001 spec:
                // STR for physical load, END for sustained effort, INT for route planning).
                int score = npc.abilityScores.STR + npc.abilityScores.END + npc.abilityScores.INT;
                if (score > bestScore) { bestScore = score; best = npc; }
            }

            return best;
        }

        private static void AssignHaulJob(NPCInstance npc, BodyInstance body, StationState station)
        {
            body.haulerNpcUid = npc.uid;
            body.haulJobTimer = HaulBodyDurationTicks;

            npc.currentJobId  = "job.haul_body";
            npc.jobModuleUid  = null;
            npc.jobTimer      = HaulJobTimerRefresh;

            station.LogEvent(
                $"{npc.name} is hauling the body of {body.npcName} to the disposal site.");
        }

        private void TickHaul(BodyInstance body, StationState station, List<string> toRemove)
        {
            // Check hauler is still alive.
            if (!station.npcs.TryGetValue(body.haulerNpcUid, out var hauler) ||
                hauler.statusTags.Contains("dead"))
            {
                // Hauler lost — reset for next assignment.
                body.haulerNpcUid      = null;
                body.haulJobTimer      = 0;
                body.haulTaskGenerated = false;
                return;
            }

            // Abort if JobSystem interrupted the hauler (e.g. hunger, rest, crisis).
            // The NPC will resume when next assigned; haulTaskGenerated reset triggers retry.
            if (hauler.currentJobId != "job.haul_body")
            {
                body.haulerNpcUid      = null;
                body.haulJobTimer      = 0;
                body.haulTaskGenerated = false;
                return;
            }

            // Refresh job timer to keep JobSystem from reassigning the NPC.
            if (hauler.jobTimer < HaulJobTimerRefresh)
                hauler.jobTimer = HaulJobTimerRefresh;

            body.haulJobTimer--;
            if (body.haulJobTimer > 0) return;

            // Haul complete — free the NPC and remove the body.
            hauler.currentJobId = null;
            hauler.jobTimer     = 0;

            station.LogEvent($"{hauler.name} has disposed of {body.npcName}'s body.");

            RemoveBodyModifiers(body, station);
            toRemove.Add(body.uid);
        }

        private void TickBodyPresenceMood(BodyInstance body, StationState station)
        {
            if (_mood == null) return;
            foreach (var npc in station.npcs.Values)
            {
                if (npc.statusTags.Contains("dead")) continue;
                if (!npc.IsCrew()) continue;
                if (npc.location != body.location) continue;

                _mood.PushModifier(npc, BodyPresentEventId(body.uid),
                                   BodyPresentPenalty,
                                   BodyPresentDurationTicks,
                                   station.tick, "death_handling");
            }
        }

        private void TickEscalation(BodyInstance body, StationState station)
        {
            int age = station.tick - body.spawnedAtTick;
            if (age < UnhandledEscalationThresholdTicks) return;

            int newStep = Mathf.Min(
                (age - UnhandledEscalationThresholdTicks) / EscalationStepIntervalTicks + 1,
                MaxEscalationSteps);

            bool stepChanged = newStep != body.escalationStep;
            if (stepChanged)
                body.escalationStep = newStep;

            float delta = EscalatingPenaltyPerStep * body.escalationStep;

            if (_mood == null) return;
            foreach (var npc in station.npcs.Values)
            {
                if (npc.statusTags.Contains("dead")) continue;
                if (!npc.IsCrew()) continue;
                if (npc.location != body.location) continue;

                string eventId = UnhandledBodyEventId(body.uid);
                // When step increases, remove the old modifier first so that the new magnitude
                // takes effect.  PushModifier only refreshes duration for an existing
                // (eventId, source) pair — it does not update the delta.
                if (stepChanged)
                    _mood.RemoveModifier(npc, eventId, "death_handling");
                // Re-push every tick (even when step is unchanged) so the modifier does not
                // expire while the body remains, and so NPCs entering the module are
                // immediately affected on the next tick.
                _mood.PushModifier(npc, eventId, delta,
                                   EscalationStepIntervalTicks,
                                   station.tick, "death_handling");
            }
        }

        private void RemoveBodyModifiers(BodyInstance body, StationState station)
        {
            if (_mood == null) return;
            foreach (var npc in station.npcs.Values)
            {
                if (npc.statusTags.Contains("dead")) continue;
                _mood.RemoveModifier(npc, WitnessedDeathEventId(body.uid), "death_handling");
                _mood.RemoveModifier(npc, BodyPresentEventId(body.uid),    "death_handling");
                _mood.RemoveModifier(npc, UnhandledBodyEventId(body.uid),  "death_handling");
            }
        }

        // ── Event ID helpers ──────────────────────────────────────────────────

        private static string WitnessedDeathEventId(string bodyUid)  => $"witnessed_death_{bodyUid}";
        private static string BodyPresentEventId(string bodyUid)      => $"body_present_{bodyUid}";
        private static string UnhandledBodyEventId(string bodyUid)    => $"unhandled_body_{bodyUid}";
    }
}
