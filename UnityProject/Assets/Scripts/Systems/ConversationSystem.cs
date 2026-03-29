// ConversationSystem — autonomous NPC social interactions.
//
// Each tick the system scans idle crew members.  When two idle NPCs share the
// same module and neither is on cooldown, one initiates a conversation.  After
// ConversationDurationTicks the CHA check and outcome are resolved, mood
// modifiers are applied, and both NPCs' affinity scores are updated.
//
// Conversation pipeline (NPC-011):
//   1. Raw CHA check (d20 + CHA modifier) determines quality tier:
//        ≤ 2   → CriticalFail  (terminal: amplified negative, relationship damage)
//        3–7   → Low           (negative affinity)
//        8–14  → Mid           (neutral/slight positive via relationship weighting)
//        ≥ 15  → High          (positive + opens skill follow-up options)
//   2. On High: initiating NPC selects a follow-up skill (Persuasion / Intimidation /
//      Deception) based on trait profile, then rolls d20 + skill level vs DC 10.
//   3. Mood modifier and affinity change applied to both participants.
//   4. Notable conversations are entered in the event log (CriticalFail, follow-up
//      used, or affinity swing above the notable threshold).
//   5. Speech bubble state exposed via IsConversing() / GetActiveConversations()
//      for tile-map rendering.
//
// Outcome probability for Mid tier varies by relationship type:
//   Enemy   — 30 % positive
//   Stranger/Acquaintance — 50–60 % positive
//   Friend  — 70 % positive
//   Lover / Spouse — 85–90 % positive
//
// Conversation cooldown: 60 ticks per NPC (blocks conversations with any partner).
// ConversationSystem.Enabled: set false to disable without removing other mood systems.
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Quality tier produced by the raw CHA check ────────────────────────────

    public enum ConversationQuality
    {
        CriticalFail = 0,   // d20 + CHA modifier ≤ 2  — terminal outcome
        Low          = 1,   // 3–7                     — negative outcome
        Mid          = 2,   // 8–14                    — relationship-weighted outcome
        High         = 3,   // ≥ 15                    — positive + skill follow-up available
    }

    // ── Skill chosen for the follow-up (High CHA only) ────────────────────────

    public enum ConversationFollowUpSkill
    {
        None         = 0,
        Persuasion   = 1,
        Intimidation = 2,
        Deception    = 3,
    }

    public class ConversationSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>How long a single conversation takes in ticks.</summary>
        public const int ConversationDurationTicks = 2;

        /// <summary>Minimum ticks between conversations for the same NPC.</summary>
        public const int ConversationCooldownTicks = 60;

        // ── CHA check thresholds (d20 + CHA modifier) ─────────────────────────
        public const int ChaHighThreshold       = 15;   // ≥ 15 → High
        public const int ChaMidThreshold        = 8;    // 8–14 → Mid
        public const int ChaLowThreshold        = 3;    // 3–7  → Low
        // ≤ 2 → CriticalFail

        // ── Skill follow-up DC ────────────────────────────────────────────────
        public const int FollowUpDC = 10;

        // ── Affinity deltas per quality tier ──────────────────────────────────
        public const float AffinityCriticalFail       = -8f;
        public const float AffinityLow                = -4f;
        public const float AffinityMid                =  2f;   // base for relationship-weighted Mid
        public const float AffinityHigh               =  5f;
        public const float AffinityFollowUpSuccess    =  5f;   // bonus on top of High
        public const float AffinityFollowUpFail       =  0f;   // no extra on failed follow-up

        // Minimum |affinityDelta| to qualify as a notable event log entry.
        public const float NotableAffinityThreshold = 7f;

        // ── Mood modifier magnitudes ───────────────────────────────────────────
        private const float MoodPositive   = 6f;
        private const float MoodNegative   = -5f;
        private const float MoodCritical   = -8f;   // amplified for CriticalFail
        private const int   MoodDuration   = TimeSystem.TicksPerDay;   // 1 in-game day

        // ── Feature flag ──────────────────────────────────────────────────────
        public bool Enabled = true;

        // Scan interval — run conversation matching only every N ticks to avoid
        // CPU spikes on large crews (can be tuned without changing outcomes).
        public int ScanIntervalTicks = 1;

        // ── Internal tracking ─────────────────────────────────────────────────
        // Track in-progress conversations: NPC uid → partner uid (both sides stored)
        private readonly Dictionary<string, string> _activeConversations =
            new Dictionary<string, string>();
        // Track remaining ticks for each in-progress conversation: initiator uid → ticks left
        private readonly Dictionary<string, int>    _conversationTimer =
            new Dictionary<string, int>();

        // Shared Random for deterministic rolling (seeded externally if needed)
        private readonly System.Random _rng;

        // Skill system reference for awarding Social XP
        private SkillSystem _skillSystem;

        public ConversationSystem(int? seed = null)
        {
            _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        /// <summary>Wire up SkillSystem after construction (called from GameManager).</summary>
        public void SetSkillSystem(SkillSystem skillSystem) => _skillSystem = skillSystem;

        // ── Speech bubble query API ───────────────────────────────────────────

        /// <summary>
        /// Returns true if the NPC with the given uid is currently in an active
        /// conversation.  Used by tile-map rendering to show a speech bubble indicator.
        /// </summary>
        public bool IsConversing(string uid) => _activeConversations.ContainsKey(uid);

        /// <summary>
        /// Returns a snapshot of all active conversations as a read-only dictionary of
        /// participant-uid → partner-uid pairs (both directions are present).
        /// Used by tile-map rendering to position speech bubble indicators.
        /// </summary>
        public IReadOnlyDictionary<string, string> GetActiveConversations()
            => new Dictionary<string, string>(_activeConversations);

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

                // ── Step 1: Raw CHA check ──────────────────────────────────────
                ConversationQuality quality = RollChaQuality(initiator);

                // ── Step 2: Skill follow-up (High only) ───────────────────────
                ConversationFollowUpSkill followUp = ConversationFollowUpSkill.None;
                bool followUpSuccess = false;
                if (quality == ConversationQuality.High)
                {
                    followUp = SelectFollowUpSkill(initiator);
                    if (followUp != ConversationFollowUpSkill.None)
                        followUpSuccess = RollFollowUpOutcome(initiator, followUp);
                }

                // ── Step 3: Apply outcome ─────────────────────────────────────
                ApplyOutcome(initiator, partner, quality, followUp, followUpSuccess,
                             station, mood, rels);
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
                    // Partner entry in timer is intentionally omitted: only the
                    // initiator slot drives the timer so each pair resolves once.

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

        // ── CHA check ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rolls d20 + CHA modifier for the initiating NPC and returns the
        /// resulting <see cref="ConversationQuality"/> tier.
        /// </summary>
        public ConversationQuality RollChaQuality(NPCInstance initiator)
        {
            int d20    = _rng.Next(1, 21);   // 1–20 inclusive
            int chaMod = AbilityScores.GetModifier(initiator.abilityScores.CHA);
            int total  = d20 + chaMod;

            if (total <  ChaLowThreshold)  return ConversationQuality.CriticalFail;   // < 3 (≤ 2)
            if (total <  ChaMidThreshold)  return ConversationQuality.Low;            // 3–7
            if (total <  ChaHighThreshold) return ConversationQuality.Mid;            // 8–14
            return ConversationQuality.High;                                           // ≥ 15
        }

        // ── Follow-up skill selection ─────────────────────────────────────────

        private static readonly string[] PersuasionTraits    = { "trait.sociable", "trait.idealistic" };
        private static readonly string[] DeceptionTraits     = { "trait.distrustful", "trait.cynical" };
        private static readonly string[] IntimidationTraits  = { "trait.vigilant", "trait.hardened" };

        /// <summary>
        /// Selects the follow-up skill the initiating NPC will use on a High CHA roll.
        /// Selection priority: trait-driven personality → highest owned skill level → Persuasion.
        /// </summary>
        public static ConversationFollowUpSkill SelectFollowUpSkill(NPCInstance initiator)
        {
            var profile = initiator.traitProfile;
            if (profile != null)
            {
                foreach (var active in profile.traits)
                {
                    foreach (var t in IntimidationTraits)
                        if (active.traitId == t) return ConversationFollowUpSkill.Intimidation;
                }
                foreach (var active in profile.traits)
                {
                    foreach (var t in DeceptionTraits)
                        if (active.traitId == t) return ConversationFollowUpSkill.Deception;
                }
                foreach (var active in profile.traits)
                {
                    foreach (var t in PersuasionTraits)
                        if (active.traitId == t) return ConversationFollowUpSkill.Persuasion;
                }
            }

            // No decisive trait — pick whichever social skill is highest.
            int persuasion   = SkillSystem.GetSkillLevel(initiator, "skill.persuasion");
            int intimidation = SkillSystem.GetSkillLevel(initiator, "skill.intimidation");
            int deception    = SkillSystem.GetSkillLevel(initiator, "skill.deception");

            if (intimidation > persuasion && intimidation > deception)
                return ConversationFollowUpSkill.Intimidation;
            if (deception > persuasion && deception > intimidation)
                return ConversationFollowUpSkill.Deception;
            return ConversationFollowUpSkill.Persuasion;
        }

        // ── Follow-up outcome roll ────────────────────────────────────────────

        /// <summary>
        /// Rolls d20 + relevant skill level for the initiating NPC vs <see cref="FollowUpDC"/>.
        /// Returns true on success (total ≥ DC).
        /// </summary>
        public bool RollFollowUpOutcome(NPCInstance initiator, ConversationFollowUpSkill skill)
        {
            string skillId = skill switch
            {
                ConversationFollowUpSkill.Intimidation => "skill.intimidation",
                ConversationFollowUpSkill.Deception    => "skill.deception",
                _                                      => "skill.persuasion",
            };
            int d20        = _rng.Next(1, 21);
            int skillLevel = SkillSystem.GetSkillLevel(initiator, skillId);
            return (d20 + skillLevel) >= FollowUpDC;
        }

        // ── Outcome application ───────────────────────────────────────────────

        private static float GetPositiveChance(RelationshipType type) => type switch
        {
            RelationshipType.Enemy        => 0.30f,
            RelationshipType.None         => 0.50f,
            RelationshipType.Acquaintance => 0.60f,
            RelationshipType.Friend       => 0.70f,
            RelationshipType.Lover        => 0.85f,
            RelationshipType.Spouse       => 0.90f,
            _                             => 0.50f
        };

        private void ApplyOutcome(NPCInstance initiator, NPCInstance partner,
                                   ConversationQuality quality,
                                   ConversationFollowUpSkill followUp, bool followUpSuccess,
                                   StationState station,
                                   MoodSystem mood, RelationshipRegistry rels)
        {
            float affinityDelta;
            float moodDelta;
            string moodEventId;
            bool notable = false;

            switch (quality)
            {
                case ConversationQuality.CriticalFail:
                    affinityDelta = AffinityCriticalFail;
                    moodDelta     = MoodCritical;
                    moodEventId   = "conversation_critical_fail";
                    notable       = true;   // always log critical fails
                    break;

                case ConversationQuality.Low:
                    affinityDelta = AffinityLow;
                    moodDelta     = MoodNegative;
                    moodEventId   = "conversation_negative";
                    break;

                case ConversationQuality.Mid:
                {
                    // Relationship-weighted for Mid tier (preserves existing behaviour)
                    var rec = RelationshipRegistry.Get(station, initiator.uid, partner.uid);
                    float posChance = GetPositiveChance(rec?.relationshipType ?? RelationshipType.None);
                    bool  positive  = (float)_rng.NextDouble() < posChance;
                    affinityDelta   = positive ? AffinityMid : AffinityLow;
                    moodDelta       = positive ? MoodPositive : MoodNegative;
                    moodEventId     = positive ? "conversation_positive" : "conversation_negative";
                    break;
                }

                default:   // High
                    affinityDelta = AffinityHigh;
                    moodDelta     = MoodPositive;
                    moodEventId   = "conversation_positive";

                    if (followUp != ConversationFollowUpSkill.None)
                    {
                        notable = true;   // skill follow-up always worth logging
                        if (followUpSuccess)
                            affinityDelta += AffinityFollowUpSuccess;
                        // On failed follow-up the base High affinity still applies
                    }
                    break;
            }

            // ── Mood modifiers (both participants) ────────────────────────────
            mood?.PushModifier(initiator, moodEventId, moodDelta, MoodDuration, station.tick, "conversation");
            mood?.PushModifier(partner,   moodEventId, moodDelta, MoodDuration, station.tick, "conversation");

            // ── Affinity update ───────────────────────────────────────────────
            RelationshipRegistry.ModifyAffinity(station, initiator.uid, partner.uid, affinityDelta, station.tick);

            // Notable if the affinity swing is large regardless of quality tier
            if (System.Math.Abs(affinityDelta) >= NotableAffinityThreshold)
                notable = true;

            // ── Event log for notable conversations ───────────────────────────
            if (notable)
            {
                string detail = quality switch
                {
                    ConversationQuality.CriticalFail =>
                        $"{initiator.name} had a critical conversation failure with {partner.name} " +
                        $"(affinity {affinityDelta:+0;-0}).",
                    ConversationQuality.High when followUp != ConversationFollowUpSkill.None =>
                        $"{initiator.name} used {followUp} with {partner.name}: " +
                        $"{(followUpSuccess ? "success" : "failed")} " +
                        $"(affinity {affinityDelta:+0;-0}).",
                    _ =>
                        $"{initiator.name} and {partner.name}: notable conversation " +
                        $"(affinity {affinityDelta:+0;-0})."
                };
                station.LogEvent(detail);
            }

            // ── Social skill XP on positive outcome ───────────────────────────
            if (affinityDelta > 0 && _skillSystem != null)
            {
                _skillSystem.AwardXP(initiator, "conversation_positive", station);
                _skillSystem.AwardXP(partner,   "conversation_positive", station);
            }
        }
    }
}
