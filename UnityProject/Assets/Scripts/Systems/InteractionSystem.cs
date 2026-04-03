// InteractionSystem — unified NPC interaction system (WO-NPC-014).
//
// Replaces ConversationSystem's CHA-only outcome resolution with a five-input
// quality roll. Manages conversation windows, overhearing, first impressions,
// joining, task handoff interactions, and speech bubble state.
//
// Gated by FeatureFlags.UseInteractionSystem.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Outcome tiers ─────────────────────────────────────────────────────
    public enum InteractionTier
    {
        Hostile   = 0,   // quality < -1.5
        Poor      = 1,   // -1.5 to -0.5
        Neutral   = 2,   // -0.5 to 0.5
        Good      = 3,   // 0.5 to 1.5
        Excellent = 4,   // > 1.5
    }

    // ── Speech bubble visual state ────────────────────────────────────────
    public enum SpeechBubbleState
    {
        None,
        Neutral,
        Positive,
        Negative,
    }

    // ── Conversation window ───────────────────────────────────────────────
    public class ConversationWindow
    {
        public List<string> participantUids = new List<string>();   // 2 or 3
        public List<float> qualitySamples = new List<float>();      // max 5
        public int startTick;
        public bool isExpanded;   // true when 3rd NPC joined
        public int nextSampleTick; // next tick to sample quality

        public float RollingAverage()
        {
            if (qualitySamples.Count == 0) return 0f;
            float sum = 0f;
            foreach (var s in qualitySamples) sum += s;
            return sum / qualitySamples.Count;
        }
    }

    public class InteractionSystem
    {
        // ── Constants ─────────────────────────────────────────────────────
        public const int ConversationRange = 3;
        public const int SampleIntervalTicks = 4;
        public const int MaxSamples = 5;
        public const int CooldownMultiplier = 10; // cooldown = samples × 10

        // ── Mood deltas per tier (direct participants) ────────────────────
        private static readonly Dictionary<InteractionTier, (float happy, float calm)> TierMoodDeltas =
            new Dictionary<InteractionTier, (float, float)>
            {
                { InteractionTier.Excellent, (+8f, +5f) },
                { InteractionTier.Good,      (+4f, 0f) },
                { InteractionTier.Neutral,   (0f, 0f) },
                { InteractionTier.Poor,      (0f, -4f) },
                { InteractionTier.Hostile,   (-8f, -6f) },
            };

        // ── Affinity deltas per tier ──────────────────────────────────────
        private static readonly Dictionary<InteractionTier, float> TierAffinityDeltas =
            new Dictionary<InteractionTier, float>
            {
                { InteractionTier.Excellent, +8f },
                { InteractionTier.Good,      +3f },
                { InteractionTier.Neutral,   +1f },
                { InteractionTier.Poor,      -3f },
                { InteractionTier.Hostile,   -8f },
            };

        // ── Overhear mood deltas (half strength) ──────────────────────────
        private static readonly Dictionary<InteractionTier, (float happy, float calm)> OverhearMoodDeltas =
            new Dictionary<InteractionTier, (float, float)>
            {
                { InteractionTier.Excellent, (+4f, 0f) },
                { InteractionTier.Good,      (+2f, 0f) },
                { InteractionTier.Neutral,   (0f,  0f) },
                { InteractionTier.Poor,      (0f,  -2f) },
                { InteractionTier.Hostile,   (-4f, -3f) },
            };

        // ── First impression affinity baselines ───────────────────────────
        private static readonly Dictionary<InteractionTier, float> DirectImpressionAffinity =
            new Dictionary<InteractionTier, float>
            {
                { InteractionTier.Excellent, +25f },
                { InteractionTier.Good,      +12f },
                { InteractionTier.Neutral,     0f },
                { InteractionTier.Poor,      -10f },
                { InteractionTier.Hostile,   -20f },
            };

        private static readonly Dictionary<InteractionTier, float> SecondhandImpressionAffinity =
            new Dictionary<InteractionTier, float>
            {
                { InteractionTier.Excellent, +12f },
                { InteractionTier.Good,       +6f },
                { InteractionTier.Neutral,     0f },
                { InteractionTier.Poor,       -5f },
                { InteractionTier.Hostile,   -10f },
            };

        // ── Task handoff Social skill base quality ────────────────────────
        private static float GetHandoffSocialModifier(int socialLevel)
        {
            if (socialLevel >= 11) return 0.8f;
            if (socialLevel >= 8)  return 0.5f;
            if (socialLevel >= 5)  return 0.2f;
            if (socialLevel >= 3)  return -0.2f;
            return -0.6f;
        }

        // ── Dependencies ──────────────────────────────────────────────────
        private TraitSystem _traits;
        private MoodSystem _mood;
        private FactionSystem _factions;
        private SkillSystem _skills;

        // ── Active conversation windows ───────────────────────────────────
        private readonly List<ConversationWindow> _windows = new List<ConversationWindow>();

        // Per-NPC cooldowns: uid → tick when cooldown expires. Keyed as "uid1:uid2".
        private readonly Dictionary<string, int> _pairCooldowns = new Dictionary<string, int>();

        // Speech bubble state per NPC uid
        private readonly Dictionary<string, SpeechBubbleState> _speechBubbles =
            new Dictionary<string, SpeechBubbleState>();

        // Mood modifier duration for interaction effects
        private const int MoodModifierDuration = 100;

        public InteractionSystem() { }

        public void SetDependencies(TraitSystem traits, MoodSystem mood,
                                     FactionSystem factions, SkillSystem skills)
        {
            _traits = traits;
            _mood = mood;
            _factions = factions;
            _skills = skills;
        }

        // ── Query API ─────────────────────────────────────────────────────

        public SpeechBubbleState GetSpeechBubbleState(string npcUid)
        {
            return _speechBubbles.TryGetValue(npcUid, out var state) ? state : SpeechBubbleState.None;
        }

        public bool IsInConversation(string npcUid)
        {
            foreach (var w in _windows)
                if (w.participantUids.Contains(npcUid)) return true;
            return false;
        }

        // ── Tick ──────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (!FeatureFlags.UseInteractionSystem) return;

            // 1. Process existing windows — sample or close
            ProcessWindows(station);

            // 2. Check for joining (if enabled)
            if (FeatureFlags.UseConversationJoining)
                ProcessJoining(station);

            // 3. Start new conversation windows
            StartNewWindows(station);
        }

        // ── Window processing ─────────────────────────────────────────────

        private void ProcessWindows(StationState station)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var w = _windows[i];

                // Check if participants are still in range
                bool inRange = AreAllInRange(w, station);
                if (!inRange || w.qualitySamples.Count >= MaxSamples)
                {
                    // Close window
                    CloseWindow(w, station);
                    _windows.RemoveAt(i);
                    continue;
                }

                // Sample on schedule
                if (station.tick >= w.nextSampleTick)
                {
                    float sample = SampleQuality(w, station);
                    w.qualitySamples.Add(sample);
                    w.nextSampleTick = station.tick + SampleIntervalTicks;
                    UpdateSpeechBubbles(w);
                }
            }
        }

        private bool AreAllInRange(ConversationWindow w, StationState station)
        {
            // Check all participant pairs are within ConversationRange
            for (int i = 0; i < w.participantUids.Count; i++)
            {
                for (int j = i + 1; j < w.participantUids.Count; j++)
                {
                    if (!station.npcs.TryGetValue(w.participantUids[i], out var npcA)) return false;
                    if (!station.npcs.TryGetValue(w.participantUids[j], out var npcB)) return false;
                    if (SpatialHelpers.TileDistance(npcA.tileCol, npcA.tileRow,
                                                   npcB.tileCol, npcB.tileRow) > ConversationRange)
                        return false;
                }
            }
            return true;
        }

        private float SampleQuality(ConversationWindow w, StationState station)
        {
            if (w.participantUids.Count == 2)
            {
                if (!station.npcs.TryGetValue(w.participantUids[0], out var a)) return 0f;
                if (!station.npcs.TryGetValue(w.participantUids[1], out var b)) return 0f;
                return ComputeQualityRoll(a, b, station);
            }

            // 3-way: average all pairwise rolls
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < w.participantUids.Count; i++)
            {
                for (int j = i + 1; j < w.participantUids.Count; j++)
                {
                    if (!station.npcs.TryGetValue(w.participantUids[i], out var a)) continue;
                    if (!station.npcs.TryGetValue(w.participantUids[j], out var b)) continue;
                    sum += ComputeQualityRoll(a, b, station);
                    count++;
                }
            }
            return count > 0 ? sum / count : 0f;
        }

        // ── Five-input quality roll ───────────────────────────────────────

        public float ComputeQualityRoll(NPCInstance a, NPCInstance b, StationState station)
        {
            // 1. Trait compatibility: -1.0 to +1.0
            float traitCompat = _traits != null ? _traits.GetCompatibility(a, b) : 0f;
            traitCompat = Mathf.Clamp(traitCompat, -1f, 1f);

            // 2. Mood modifiers: each -0.5 to +0.5 (mapped from 0-100 score)
            float moodA = Mathf.Lerp(-0.5f, 0.5f, a.moodScore / 100f);
            float moodB = Mathf.Lerp(-0.5f, 0.5f, b.moodScore / 100f);

            // 3. Social skill modifiers: each 0.0 to +0.5
            int socialA = GetSocialLevel(a);
            int socialB = GetSocialLevel(b);
            float socialModA = Mathf.Clamp(socialA * 0.05f, 0f, 0.5f);
            float socialModB = Mathf.Clamp(socialB * 0.05f, 0f, 0.5f);

            // 4. Relationship modifier: -0.3 to +0.3
            var rec = RelationshipRegistry.Get(station, a.uid, b.uid);
            float relMod = 0f;
            if (rec != null)
                relMod = Mathf.Clamp(rec.affinityScore / 100f * 0.3f, -0.3f, 0.3f);

            // 5. Faction modifier: -0.4 to +0.2
            float factionMod = _factions != null
                ? _factions.GetInteractionWeight(a, b, station)
                : FactionSystem.GetInteractionWeight(a, b);

            return traitCompat + moodA + moodB + socialModA + socialModB + relMod + factionMod;
        }

        private int GetSocialLevel(NPCInstance npc)
        {
            foreach (var si in npc.skillInstances)
                if (si.skillId == "skill_social") return si.Level;
            return 0;
        }

        // ── Tier mapping ──────────────────────────────────────────────────

        public static InteractionTier GetTier(float quality)
        {
            if (quality > 1.5f)  return InteractionTier.Excellent;
            if (quality > 0.5f)  return InteractionTier.Good;
            if (quality > -0.5f) return InteractionTier.Neutral;
            if (quality > -1.5f) return InteractionTier.Poor;
            return InteractionTier.Hostile;
        }

        // ── Window close — apply outcomes ─────────────────────────────────

        private void CloseWindow(ConversationWindow w, StationState station)
        {
            float avgQuality = w.RollingAverage();
            var tier = GetTier(avgQuality);
            int sampleCount = w.qualitySamples.Count;
            int cooldownTicks = sampleCount * CooldownMultiplier;

            // Apply to all participant pairs
            for (int i = 0; i < w.participantUids.Count; i++)
            {
                for (int j = i + 1; j < w.participantUids.Count; j++)
                {
                    if (!station.npcs.TryGetValue(w.participantUids[i], out var npcA)) continue;
                    if (!station.npcs.TryGetValue(w.participantUids[j], out var npcB)) continue;

                    ApplyConversationOutcome(npcA, npcB, tier, station);
                    ApplyFirstImpression(npcA, npcB, tier, station, direct: true);
                }
            }

            // Apply cooldowns
            for (int i = 0; i < w.participantUids.Count; i++)
            {
                for (int j = i + 1; j < w.participantUids.Count; j++)
                {
                    string key = MakePairKey(w.participantUids[i], w.participantUids[j]);
                    _pairCooldowns[key] = station.tick + cooldownTicks;
                }
            }

            // Overhearing
            ProcessOverhearing(w, tier, station);

            // Clear speech bubbles
            foreach (var uid in w.participantUids)
                _speechBubbles.Remove(uid);

            // Log
            LogConversation(w, tier, avgQuality, station);
        }

        private void ApplyConversationOutcome(NPCInstance a, NPCInstance b,
                                               InteractionTier tier, StationState station)
        {
            var (happy, calm) = TierMoodDeltas[tier];
            float affinity = TierAffinityDeltas[tier];

            // Mood modifiers
            if (happy != 0f)
            {
                _mood?.PushModifier(a, $"interaction_{b.uid}", happy,
                    MoodModifierDuration, station.tick, "interaction");
                _mood?.PushModifier(b, $"interaction_{a.uid}", happy,
                    MoodModifierDuration, station.tick, "interaction");
            }
            if (calm != 0f)
            {
                PushStressModifier(a, $"interaction_calm_{b.uid}", calm,
                    MoodModifierDuration, station.tick);
                PushStressModifier(b, $"interaction_calm_{a.uid}", calm,
                    MoodModifierDuration, station.tick);
            }

            // Affinity
            var rec = RelationshipRegistry.GetOrCreate(station, a.uid, b.uid);
            if (rec.firstImpressionSet)
            {
                // Normal interaction — delta
                RelationshipRegistry.ModifyAffinity(station, a.uid, b.uid,
                    affinity, station.tick);
            }
        }

        // ── First impressions ─────────────────────────────────────────────

        private void ApplyFirstImpression(NPCInstance a, NPCInstance b,
                                           InteractionTier tier, StationState station,
                                           bool direct)
        {
            var rec = RelationshipRegistry.GetOrCreate(station, a.uid, b.uid);

            if (direct && !rec.firstImpressionSet)
            {
                float baseline = DirectImpressionAffinity[tier];

                // If secondhand was set, use existing affinity as relationship modifier input
                // (preconceptions colour first meeting) — already factored into quality roll
                rec.affinityScore = Mathf.Clamp(baseline, -100f, 100f);
                rec.lastInteractionTick = station.tick;
                rec.firstImpressionSet = true;
                rec.secondhandImpressionSet = false;
                rec.UpdateTypeFromAffinity();

                station.LogEvent(
                    $"{a.name} met {b.name} for the first time — {tier} · " +
                    $"Relationship started at {baseline:+0;-0;0}");
            }
        }

        private void ApplySecondhandImpression(NPCInstance observer, NPCInstance participant,
                                                InteractionTier tier, StationState station)
        {
            var rec = RelationshipRegistry.GetOrCreate(station, observer.uid, participant.uid);
            if (rec.firstImpressionSet) return; // direct already set
            if (rec.secondhandImpressionSet) return; // already has secondhand

            float baseline = SecondhandImpressionAffinity[tier];
            rec.affinityScore = Mathf.Clamp(baseline, -100f, 100f);
            rec.lastInteractionTick = station.tick;
            rec.secondhandImpressionSet = true;
            rec.UpdateTypeFromAffinity();

            station.LogEvent(
                $"{observer.name} overheard {participant.name} — " +
                $"formed an early impression · Preliminary affinity: {baseline:+0;-0;0}");
        }

        // ── Overhearing ───────────────────────────────────────────────────

        private void ProcessOverhearing(ConversationWindow w, InteractionTier tier,
                                         StationState station)
        {
            // Gather all participants
            var participants = new HashSet<string>(w.participantUids);

            // For each participant, find NPCs within overhear radius
            var overheardNpcs = new HashSet<string>();
            foreach (var uid in w.participantUids)
            {
                if (!station.npcs.TryGetValue(uid, out var participant)) continue;
                int perception = GetPerception(participant);
                int overhearRadius = 3 + (perception / 8);

                var nearby = SpatialHelpers.GetNPCsWithinRadius(participant, overhearRadius, station);
                foreach (var npc in nearby)
                {
                    if (participants.Contains(npc.uid)) continue;
                    overheardNpcs.Add(npc.uid);
                }
            }

            // Apply half-strength mood effects to overhearers
            var (happy, calm) = OverhearMoodDeltas[tier];
            foreach (var uid in overheardNpcs)
            {
                if (!station.npcs.TryGetValue(uid, out var npc)) continue;

                if (happy != 0f)
                    _mood?.PushModifier(npc, $"overheard_{w.participantUids[0]}_{w.participantUids[1]}",
                        happy, MoodModifierDuration, station.tick, "interaction_overheard");
                if (calm != 0f)
                    PushStressModifier(npc, $"overheard_calm_{w.participantUids[0]}",
                        calm, MoodModifierDuration, station.tick);

                // Secondhand impressions
                foreach (var participantUid in w.participantUids)
                {
                    if (!station.npcs.TryGetValue(participantUid, out var participant)) continue;
                    ApplySecondhandImpression(npc, participant, tier, station);
                }
            }
        }

        private static int GetPerception(NPCInstance npc)
        {
            // Perception = WIS + (INT + CHA) / 4
            return npc.abilityScores.WIS + (npc.abilityScores.INT + npc.abilityScores.CHA) / 4;
        }

        // ── Conversation joining ──────────────────────────────────────────

        private void ProcessJoining(StationState station)
        {
            foreach (var w in _windows)
            {
                if (w.participantUids.Count >= 3) continue; // already at max
                if (w.qualitySamples.Count == 0) continue;

                float rollingAvg = w.RollingAverage();
                var currentTier = GetTier(rollingAvg);
                if (currentTier != InteractionTier.Good && currentTier != InteractionTier.Excellent)
                    continue;

                // Find candidate joiners
                foreach (var npc in station.npcs.Values)
                {
                    if (!npc.IsCrew()) continue;
                    if (w.participantUids.Contains(npc.uid)) continue;
                    if (w.participantUids.Count >= 3) break; // safety

                    // Must be within overhear radius of at least one participant
                    bool inRange = false;
                    foreach (var uid in w.participantUids)
                    {
                        if (!station.npcs.TryGetValue(uid, out var p)) continue;
                        int perception = GetPerception(npc);
                        int radius = 3 + (perception / 8);
                        if (SpatialHelpers.TileDistance(npc.tileCol, npc.tileRow,
                                                       p.tileCol, p.tileRow) <= radius)
                        {
                            inRange = true;
                            break;
                        }
                    }
                    if (!inRange) continue;

                    // Must have first_impression_set with at least one participant
                    bool hasRelationship = false;
                    foreach (var uid in w.participantUids)
                    {
                        var rec = RelationshipRegistry.Get(station, npc.uid, uid);
                        if (rec != null && rec.firstImpressionSet)
                        {
                            hasRelationship = true;
                            break;
                        }
                    }
                    if (!hasRelationship) continue;

                    // Must not be on cooldown with either participant
                    bool onCooldown = false;
                    foreach (var uid in w.participantUids)
                    {
                        string key = MakePairKey(npc.uid, uid);
                        if (_pairCooldowns.TryGetValue(key, out int cd) && station.tick < cd)
                        {
                            onCooldown = true;
                            break;
                        }
                    }
                    if (onCooldown) continue;

                    // Join!
                    w.participantUids.Add(npc.uid);
                    w.isExpanded = true;
                    break; // only one joiner per tick
                }
            }
        }

        // ── Start new windows ─────────────────────────────────────────────

        private void StartNewWindows(StationState station)
        {
            var crew = station.GetCrew();
            if (crew.Count < 2) return;

            for (int i = 0; i < crew.Count; i++)
            {
                var a = crew[i];
                if (IsInConversation(a.uid)) continue;
                if (a.missionUid != null) continue;

                for (int j = i + 1; j < crew.Count; j++)
                {
                    var b = crew[j];
                    if (IsInConversation(b.uid)) continue;
                    if (b.missionUid != null) continue;

                    // Range check
                    int dist = SpatialHelpers.TileDistance(a.tileCol, a.tileRow,
                                                          b.tileCol, b.tileRow);
                    if (dist > ConversationRange) continue;

                    // Cooldown check
                    string key = MakePairKey(a.uid, b.uid);
                    if (_pairCooldowns.TryGetValue(key, out int cd) && station.tick < cd)
                        continue;

                    // Start window
                    var w = new ConversationWindow
                    {
                        participantUids = new List<string> { a.uid, b.uid },
                        startTick = station.tick,
                        nextSampleTick = station.tick, // sample immediately
                    };
                    _windows.Add(w);
                    UpdateSpeechBubbles(w);
                    break; // each NPC can only start one window per tick
                }
            }
        }

        // ── Task handoff interaction ──────────────────────────────────────

        /// <summary>
        /// Called by HierarchyDistributor when a lead distributes a task to an NPC.
        /// Immediate outcome, no conversation window.
        /// </summary>
        public void ProcessTaskHandoff(NPCInstance sender, NPCInstance receiver,
                                       StationState station)
        {
            if (!FeatureFlags.UseInteractionSystem) return;

            int socialLevel = GetSocialLevel(sender);
            float baseMod = GetHandoffSocialModifier(socialLevel);

            // Articulation bonus
            if (sender.chosenExpertise.Contains("exp_articulation"))
                baseMod += 0.3f;

            // Mood modifiers
            float moodSender = Mathf.Lerp(-0.5f, 0.5f, sender.moodScore / 100f);
            float moodRecv = Mathf.Lerp(-0.5f, 0.5f, receiver.moodScore / 100f);

            // Relationship modifier
            var rec = RelationshipRegistry.Get(station, sender.uid, receiver.uid);
            float relMod = 0f;
            if (rec != null)
                relMod = Mathf.Clamp(rec.affinityScore / 100f * 0.3f, -0.3f, 0.3f);

            // Faction modifier
            float factionMod = _factions != null
                ? _factions.GetInteractionWeight(sender, receiver, station)
                : 0f;

            float quality = baseMod + moodSender + moodRecv + relMod + factionMod;
            var tier = GetTier(quality);

            // Apply outcome
            var (happy, calm) = TierMoodDeltas[tier];
            float affinity = TierAffinityDeltas[tier];

            if (happy != 0f)
            {
                _mood?.PushModifier(receiver, $"handoff_{sender.uid}",
                    happy, MoodModifierDuration, station.tick, "interaction_handoff");
            }
            if (calm != 0f)
            {
                PushStressModifier(receiver, $"handoff_calm_{sender.uid}",
                    calm, MoodModifierDuration, station.tick);
            }

            RelationshipRegistry.ModifyAffinity(station, sender.uid, receiver.uid,
                affinity, station.tick);

            // Log only Poor or Hostile
            if (tier == InteractionTier.Poor || tier == InteractionTier.Hostile)
            {
                station.LogEvent(
                    $"{sender.name} distributed tasks to {receiver.name} — {tier} · " +
                    $"Mood: {happy:+0;-0;0} · Affinity: {affinity:+0;-0;0} · " +
                    $"Lead Social: level {socialLevel}");
            }
        }

        // ── Collaborative task interaction ────────────────────────────────

        /// <summary>
        /// Called when two NPCs start or complete a collaborative task.
        /// Full five-input quality roll, no cooldown.
        /// </summary>
        public void ProcessCollaborativeInteraction(NPCInstance a, NPCInstance b,
                                                     StationState station)
        {
            if (!FeatureFlags.UseInteractionSystem) return;

            float quality = ComputeQualityRoll(a, b, station);
            var tier = GetTier(quality);

            ApplyConversationOutcome(a, b, tier, station);
            ApplyFirstImpression(a, b, tier, station, direct: true);
        }

        // ── Visitor interaction ───────────────────────────────────────────

        /// <summary>
        /// Called when a visitor NPC enters 3-tile range of a crew NPC.
        /// Full quality roll with heavier faction weight for hostile factions.
        /// Uses normal conversation window lifecycle.
        /// </summary>
        public void StartVisitorConversation(NPCInstance crew, NPCInstance visitor,
                                              StationState station)
        {
            if (!FeatureFlags.UseInteractionSystem) return;
            if (IsInConversation(crew.uid) || IsInConversation(visitor.uid)) return;

            string key = MakePairKey(crew.uid, visitor.uid);
            if (_pairCooldowns.TryGetValue(key, out int cd) && station.tick < cd)
                return;

            var w = new ConversationWindow
            {
                participantUids = new List<string> { crew.uid, visitor.uid },
                startTick = station.tick,
                nextSampleTick = station.tick,
            };
            _windows.Add(w);
            UpdateSpeechBubbles(w);
        }

        // ── Speech bubble management ──────────────────────────────────────

        private void UpdateSpeechBubbles(ConversationWindow w)
        {
            SpeechBubbleState state = SpeechBubbleState.Neutral;
            if (w.qualitySamples.Count > 0)
            {
                float avg = w.RollingAverage();
                var tier = GetTier(avg);
                state = (tier == InteractionTier.Good || tier == InteractionTier.Excellent)
                    ? SpeechBubbleState.Positive
                    : (tier == InteractionTier.Poor || tier == InteractionTier.Hostile)
                        ? SpeechBubbleState.Negative
                        : SpeechBubbleState.Neutral;
            }
            foreach (var uid in w.participantUids)
                _speechBubbles[uid] = state;
        }

        // ── Stress modifier helper ────────────────────────────────────────

        private void PushStressModifier(NPCInstance npc, string eventId, float delta,
                                         int duration, int currentTick)
        {
            // Push to stress modifier list directly (calm/stressed axis)
            if (npc.stressModifiers == null) npc.stressModifiers = new List<MoodModifierRecord>();

            // Dedup by eventId
            for (int i = npc.stressModifiers.Count - 1; i >= 0; i--)
            {
                if (npc.stressModifiers[i].eventId == eventId)
                {
                    npc.stressScore -= npc.stressModifiers[i].delta;
                    npc.stressModifiers.RemoveAt(i);
                }
            }

            npc.stressScore = Mathf.Clamp(npc.stressScore + delta, 0f, 100f);
            npc.stressModifiers.Add(new MoodModifierRecord
            {
                eventId = eventId,
                delta = delta,
                expiresAtTick = currentTick + duration,
                source = "interaction"
            });
        }

        // ── Logging ───────────────────────────────────────────────────────

        private void LogConversation(ConversationWindow w, InteractionTier tier,
                                      float avgQuality, StationState station)
        {
            if (w.participantUids.Count == 2)
            {
                if (!station.npcs.TryGetValue(w.participantUids[0], out var a)) return;
                if (!station.npcs.TryGetValue(w.participantUids[1], out var b)) return;

                var rec = RelationshipRegistry.Get(station, a.uid, b.uid);
                bool isFirstMeeting = rec != null && rec.firstImpressionSet &&
                    station.tick - rec.lastInteractionTick < MaxSamples * SampleIntervalTicks + 5;

                // First impressions are already logged by ApplyFirstImpression

                if (tier == InteractionTier.Good || tier == InteractionTier.Excellent)
                {
                    var (happy, _) = TierMoodDeltas[tier];
                    float aff = TierAffinityDeltas[tier];
                    station.LogEvent(
                        $"{a.name} and {b.name} — conversation · {tier} · " +
                        $"Mood: {happy:+0;-0;0} · Affinity: {aff:+0;-0;0}");
                }
                else if (tier == InteractionTier.Poor || tier == InteractionTier.Hostile)
                {
                    var (happy, calm) = TierMoodDeltas[tier];
                    float aff = TierAffinityDeltas[tier];
                    station.LogEvent(
                        $"{a.name} and {b.name} — conversation · {tier} · " +
                        $"Mood: {happy:+0;-0;0} · Affinity: {aff:+0;-0;0}");
                }
            }
            else if (w.participantUids.Count == 3)
            {
                var names = new List<string>();
                foreach (var uid in w.participantUids)
                    if (station.npcs.TryGetValue(uid, out var n)) names.Add(n.name);
                if (names.Count == 3)
                    station.LogEvent(
                        $"{names[0]}, {names[1]}, and {names[2]} — conversation · {tier}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static string MakePairKey(string uid1, string uid2)
        {
            return string.Compare(uid1, uid2, StringComparison.Ordinal) <= 0
                ? $"{uid1}:{uid2}" : $"{uid2}:{uid1}";
        }
    }
}
