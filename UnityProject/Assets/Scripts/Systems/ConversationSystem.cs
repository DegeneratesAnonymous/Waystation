// ConversationSystem — autonomous NPC social interactions.
//
// Each tick the system scans idle crew members.  When two idle NPCs share the
// same module and neither is on cooldown, one initiates a conversation.  After
// ConversationDurationTicks the outcome is rolled, mood modifiers are applied,
// and both NPCs' affinity scores are updated.
//
// Outcome probability varies by relationship type:
//   Enemy   — 70 % negative
//   Stranger/Acquaintance — 50 % positive
//   Friend  — 70 % positive
//   Lover / Spouse — 85 % positive
//
// Conversation cooldown: 60 ticks per NPC.
// ConversationSystem.Enabled: set false to disable without removing other mood systems.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ConversationSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>How long a single conversation takes in ticks.</summary>
        public const int ConversationDurationTicks = 2;

        /// <summary>Minimum ticks between conversations for the same NPC.</summary>
        public const int ConversationCooldownTicks = 60;

        // Affinity deltas applied per conversation outcome
        private const float AffinityPositive = 5f;
        private const float AffinityNegative = -4f;

        // Mood modifier magnitudes
        private const float MoodPositive   = 6f;
        private const float MoodNegative   = -5f;
        private const int   MoodDuration   = 24;   // ticks

        // Feature flag
        public bool Enabled = true;

        // Scan interval — run conversation matching only every N ticks to avoid
        // CPU spikes on large crews (can be tuned without changing outcomes).
        public int ScanIntervalTicks = 1;

        // ── Internal tracking ─────────────────────────────────────────────────
        // Track in-progress conversations: NPC uid → partner uid
        private readonly Dictionary<string, string> _activeConversations =
            new Dictionary<string, string>();
        // Track remaining ticks for each in-progress conversation: NPC uid → ticks left
        private readonly Dictionary<string, int>    _conversationTimer =
            new Dictionary<string, int>();

        // Shared Random for deterministic rolling (seeded externally if needed)
        private readonly System.Random _rng;

        public ConversationSystem(int? seed = null)
        {
            _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station, MoodSystem mood, RelationshipRegistry relationships)
        {
            if (!Enabled) return;
            if (station.tick % ScanIntervalTicks != 0) return;

            var crew = station.GetCrew();

            // Age in-progress conversations; resolve completed ones
            ResolveConversations(crew, station, mood, relationships);

            // Start new conversations
            StartNewConversations(crew, station, mood, relationships);
        }

        // ── Resolution ────────────────────────────────────────────────────────

        private void ResolveConversations(List<NPCInstance> crew, StationState station,
                                           MoodSystem mood, RelationshipRegistry rels)
        {
            // Snapshot keys to avoid modifying the dictionary during iteration
            var keys = new List<string>(_conversationTimer.Keys);
            var completed = new List<string>();

            foreach (var key in keys)
            {
                if (!_conversationTimer.ContainsKey(key)) continue;
                _conversationTimer[key]--;
                if (_conversationTimer[key] <= 0)
                    completed.Add(key);
            }

            foreach (var initiatorUid in completed)
            {
                if (!_activeConversations.TryGetValue(initiatorUid, out string partnerUid))
                    continue;

                _activeConversations.Remove(initiatorUid);
                _activeConversations.Remove(partnerUid);
                _conversationTimer.Remove(initiatorUid);
                _conversationTimer.Remove(partnerUid);

                if (!station.npcs.TryGetValue(initiatorUid, out var initiator)) continue;
                if (!station.npcs.TryGetValue(partnerUid,   out var partner))   continue;

                // Record cooldowns
                initiator.lastConversationTick = station.tick;
                partner.lastConversationTick   = station.tick;

                // Roll outcome
                bool positive = RollOutcome(station, initiatorUid, partnerUid);
                ApplyOutcome(initiator, partner, positive, station, mood, rels);
            }
        }

        // ── Initiation ────────────────────────────────────────────────────────

        private void StartNewConversations(List<NPCInstance> crew, StationState station,
                                            MoodSystem mood, RelationshipRegistry rels)
        {
            // Build a set of NPCs already in conversations this tick
            var busy = new HashSet<string>(_activeConversations.Keys);

            for (int i = 0; i < crew.Count; i++)
            {
                var a = crew[i];
                if (!IsEligible(a, station, busy)) continue;

                for (int j = i + 1; j < crew.Count; j++)
                {
                    var b = crew[j];
                    if (!IsEligible(b, station, busy)) continue;

                    // Proximity: same module (location matches)
                    if (string.IsNullOrEmpty(a.location) || a.location != b.location) continue;

                    // Start conversation — mark both busy
                    busy.Add(a.uid);
                    busy.Add(b.uid);

                    _activeConversations[a.uid] = b.uid;
                    _activeConversations[b.uid] = a.uid;
                    _conversationTimer[a.uid]   = ConversationDurationTicks;
                    _conversationTimer[b.uid]   = ConversationDurationTicks;

                    break; // a has found a partner; move to next initiator
                }
            }
        }

        private bool IsEligible(NPCInstance npc, StationState station, HashSet<string> busy)
        {
            if (busy.Contains(npc.uid)) return false;
            if (npc.missionUid != null) return false;      // on away mission
            if (npc.inCrisis) return false;                // in crisis (isolating)
            if (!IsIdle(npc)) return false;                // busy with a productive job
            if (station.tick - npc.lastConversationTick < ConversationCooldownTicks) return false;
            return true;
        }

        private static bool IsIdle(NPCInstance npc)
        {
            if (npc.currentJobId == null)            return true;
            if (npc.currentJobId == "job.wander")    return true;
            if (npc.currentJobId == "job.rest")      return true;
            if (npc.currentJobId == "job.recreate")  return true;
            return false;
        }

        // ── Outcome ───────────────────────────────────────────────────────────

        private bool RollOutcome(StationState station, string uid1, string uid2)
        {
            var rec = RelationshipRegistry.Get(station, uid1, uid2);
            float positiveChance = GetPositiveChance(rec?.relationshipType ?? RelationshipType.None);
            return (float)_rng.NextDouble() < positiveChance;
        }

        private static float GetPositiveChance(RelationshipType type) => type switch
        {
            RelationshipType.Enemy    => 0.30f,
            RelationshipType.None     => 0.50f,
            RelationshipType.Acquaintance => 0.60f,
            RelationshipType.Friend   => 0.70f,
            RelationshipType.Lover    => 0.85f,
            RelationshipType.Spouse   => 0.90f,
            _                         => 0.50f
        };

        private void ApplyOutcome(NPCInstance initiator, NPCInstance partner,
                                   bool positive, StationState station,
                                   MoodSystem mood, RelationshipRegistry rels)
        {
            float moodDelta    = positive ? MoodPositive  : MoodNegative;
            float affinityDelta = positive ? AffinityPositive : AffinityNegative;
            string eventId     = positive ? "conversation_positive" : "conversation_negative";

            // Mood modifiers
            mood?.PushModifier(initiator, eventId, moodDelta, MoodDuration, station.tick, "conversation");
            mood?.PushModifier(partner,   eventId, moodDelta, MoodDuration, station.tick, "conversation");

            // Affinity update
            RelationshipRegistry.ModifyAffinity(station, initiator.uid, partner.uid, affinityDelta, station.tick);
        }
    }
}
