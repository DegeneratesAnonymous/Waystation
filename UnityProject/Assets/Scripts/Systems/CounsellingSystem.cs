// CounsellingSystem — NPC-003: Counsellor-role NPCs provide therapy to crew in breakdown.
//
// Outcome formula:
//   d20 + WIS modifier + floor(CHA / 2) + Persuasion skill level
//       + Communication (skill.social) skill level + relationship affinity modifier
//
// Affinity modifier: Clamp(round(affinityScore / AffinityModDivisor), −3, +3)
//   Divisor = 20.  At affinity ±60 the modifier reaches ±3.
//
// Outcome tiers (modelled on SurgerySystem — the closest analogue):
//   ≥ 20  Critical Success — RegisterIntervention + OnCounsellingComplete (trait removal) + log
//   ≥ 12  Success          — RegisterIntervention + OnCounsellingComplete
//   ≥  7  Partial Success  — RegisterIntervention only (no trait removal)
//   ≥  3  Failure          — no recovery; CooldownTicksOnFailure cooldown on patient
//   ≤  2  Critical Failure — no recovery; CooldownTicksOnCritFailure cooldown on patient
//
// Session lifecycle (mirrors DeathHandlingSystem haul pattern):
//   1. Tick() detects breakdown NPCs without an active counsellor.
//   2. An eligible idle Counsellor-class NPC is selected and assigned job.counselling.
//   3. CounsellingSystem owns a per-session tick counter; it refreshes the counsellor's
//      job timer every tick so JobSystem does not re-assign them mid-session.
//   4. If the counsellor's job changes (needs crisis, hunger, etc.) the session is
//      abandoned and re-queued after a short retry gap.
//   5. On session expiry, PerformCounsellingRoll() fires and outcome is applied.
//
// Feature gate: FeatureFlags.NpcCounselling
//
// Integration:
//   • SanitySystem.RegisterIntervention(patient)         — halts breakdown drain
//   • TraitSystem.OnCounsellingComplete(patient, station) — removes therapy-removable traits
//   • SkillSystem.AwardSkillXP(counsellor, ...)          — XP for Persuasion + Social
//   • EventSystem (via station.LogEvent)                 — outcome notification
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Outcome enum ──────────────────────────────────────────────────────────

    public enum CounsellingOutcome
    {
        CriticalSuccess,
        Success,
        PartialSuccess,
        Failure,
        CriticalFailure
    }

    // ── Counselling System ────────────────────────────────────────────────────

    public class CounsellingSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Roll total at or above this → Critical Success.</summary>
        public const int CritSuccessThreshold    = 20;
        /// <summary>Roll total at or above this (but below CritSuccess) → Success.</summary>
        public const int SuccessThreshold        = 12;
        /// <summary>Roll total at or above this (but below Success) → Partial Success.</summary>
        public const int PartialSuccessThreshold =  7;
        /// <summary>Roll total at or above this (but below PartialSuccess) → Failure.</summary>
        public const int FailureThreshold        =  3;

        /// <summary>Session length in game ticks. Default = 3 in-game days.</summary>
        public int SessionDurationTicks = 3 * TimeSystem.TicksPerDay;

        /// <summary>Ticks the counsellor's job timer is refreshed to each tick during a session.</summary>
        public const int JobTimerRefresh = 12;

        /// <summary>Per-patient cooldown after a failed session (2 in-game days).</summary>
        public int CooldownTicksOnFailure = 2 * TimeSystem.TicksPerDay;

        /// <summary>Per-patient cooldown after a critical-failure session (4 in-game days).</summary>
        public int CooldownTicksOnCritFailure = 4 * TimeSystem.TicksPerDay;

        // ── Skill / class IDs ─────────────────────────────────────────────────

        public const string PersuasionSkillId    = "skill.persuasion";
        public const string CommunicationSkillId = "skill.social";
        public const string CounsellingJobId     = "job.counselling";
        public const string CounsellorClassId    = "class.counsellor";

        /// <summary>Divisor for relationship affinity → modifier. ±60 affinity → ±3 modifier.</summary>
        private const float AffinityModDivisor = 20f;
        private const int   AffinityModCap     = 3;

        /// <summary>XP awarded to the counsellor's Persuasion skill on session completion.</summary>
        public const float PersuasionXPPerSession = 50f;

        /// <summary>XP awarded to the counsellor's Social skill on session completion.</summary>
        public const float SocialXPPerSession     = 30f;

        // ── Session tracking ──────────────────────────────────────────────────

        private class ActiveSession
        {
            public string counsellorUid;
            public string patientUid;
            public int    remainingTicks;
        }

        // counsellorUid → active session
        private readonly Dictionary<string, ActiveSession> _activeSessions =
            new Dictionary<string, ActiveSession>();

        // patientUid → tick at which the cooldown expires (failed sessions only)
        private readonly Dictionary<string, int> _patientCooldowns =
            new Dictionary<string, int>();

        // ── Dependencies ──────────────────────────────────────────────────────

        private TraitSystem   _traits;
        private SkillSystem   _skills;

        public void SetTraitSystem(TraitSystem t)  => _traits = t;
        public void SetSkillSystem(SkillSystem s)  => _skills = s;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when a counselling session completes (any outcome).
        /// Payload: (counsellor, patient, outcome, roll).
        /// </summary>
        public event Action<NPCInstance, NPCInstance, CounsellingOutcome, int> OnSessionComplete;

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once per game tick from GameManager.AdvanceTick().
        /// Advances active session timers, resolves completed sessions, and
        /// assigns new counselling tasks to idle Counsellor-class NPCs.
        /// </summary>
        public void Tick(StationState station)
        {
            if (!FeatureFlags.NpcCounselling) return;
            if (station == null) return;

            TickActiveSessions(station);
            AssignNewSessions(station);
        }

        // ── Active session tick ───────────────────────────────────────────────

        private void TickActiveSessions(StationState station)
        {
            var toResolve = new List<string>();

            foreach (var kv in _activeSessions)
            {
                var session = kv.Value;

                if (!station.npcs.TryGetValue(session.counsellorUid, out var counsellor))
                {
                    // Counsellor removed from station — abandon silently.
                    toResolve.Add(kv.Key);
                    continue;
                }

                // If the counsellor abandoned the counselling job (crisis, hunger, etc.),
                // the session is interrupted — no outcome roll, re-queue the patient later.
                if (counsellor.currentJobId != CounsellingJobId)
                {
                    toResolve.Add(kv.Key);
                    continue;
                }

                // Refresh job timer to prevent JobSystem from re-assigning the counsellor.
                if (counsellor.jobTimer < JobTimerRefresh)
                    counsellor.jobTimer = JobTimerRefresh;

                session.remainingTicks--;
                if (session.remainingTicks <= 0)
                    toResolve.Add(kv.Key);
            }

            foreach (var key in toResolve)
            {
                if (!_activeSessions.TryGetValue(key, out var session))
                    continue;

                _activeSessions.Remove(key);

                if (!station.npcs.TryGetValue(session.counsellorUid, out var counsellor))
                    continue;

                // Clear counsellor job — let JobSystem pick the next task.
                if (counsellor.currentJobId == CounsellingJobId)
                {
                    counsellor.currentJobId = null;
                    counsellor.jobTimer     = 0;
                }

                // Only roll if the session ran its full course.
                if (session.remainingTicks > 0) continue;

                if (!station.npcs.TryGetValue(session.patientUid, out var patient))
                    continue;

                var (outcome, roll) = PerformCounsellingRoll(counsellor, patient, station);
                ApplyOutcome(counsellor, patient, outcome, roll, station);
            }
        }

        // ── New session assignment ─────────────────────────────────────────────

        private void AssignNewSessions(StationState station)
        {
            // Build set of patients already being counselled.
            var assignedPatients = new HashSet<string>();
            foreach (var s in _activeSessions.Values)
                assignedPatients.Add(s.patientUid);

            // Collect idle counsellors (not in a session, not in crisis).
            var idleCounsellors = new List<NPCInstance>();
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.classId != CounsellorClassId) continue;
                if (npc.inCrisis) continue;
                if (npc.statusTags.Contains("dead")) continue;
                if (npc.missionUid != null) continue;
                if (_activeSessions.ContainsKey(npc.uid)) continue;
                idleCounsellors.Add(npc);
            }

            if (idleCounsellors.Count == 0) return;

            int counsellorIndex = 0;
            foreach (var npc in station.npcs.Values)
            {
                if (counsellorIndex >= idleCounsellors.Count) break;
                if (!npc.IsCrew()) continue;
                if (npc.statusTags.Contains("dead")) continue;

                var san = npc.sanity;
                if (san == null || !san.isInBreakdown) continue;
                if (assignedPatients.Contains(npc.uid)) continue;

                // Respect per-patient failure cooldown.
                if (_patientCooldowns.TryGetValue(npc.uid, out int cooldownExpiry) &&
                    station.tick < cooldownExpiry) continue;

                BeginSession(idleCounsellors[counsellorIndex++], npc, station);
                assignedPatients.Add(npc.uid);
            }
        }

        // ── Session begin ──────────────────────────────────────────────────────

        private void BeginSession(NPCInstance counsellor, NPCInstance patient,
                                  StationState station)
        {
            counsellor.currentJobId = CounsellingJobId;
            counsellor.jobTimer     = JobTimerRefresh;

            var session = new ActiveSession
            {
                counsellorUid  = counsellor.uid,
                patientUid     = patient.uid,
                remainingTicks = SessionDurationTicks
            };
            _activeSessions[counsellor.uid] = session;

            station.LogEvent(
                $"{counsellor.name} has started a counselling session with {patient.name}.");
        }

        // ── Outcome roll ──────────────────────────────────────────────────────

        /// <summary>
        /// Performs a counselling outcome roll for a counsellor/patient pair.
        /// Formula: d20 + WIS modifier + floor(CHA / 2) + Persuasion level
        ///               + Communication level + affinity modifier.
        /// </summary>
        public (CounsellingOutcome outcome, int roll) PerformCounsellingRoll(
            NPCInstance counsellor, NPCInstance patient, StationState station)
        {
            int d20             = UnityEngine.Random.Range(1, 21);
            int wisMod          = counsellor.abilityScores.WISMod;
            int chaHalf         = Mathf.FloorToInt(counsellor.abilityScores.CHA / 2f);
            int persuasionLevel = SkillSystem.GetSkillLevel(counsellor, PersuasionSkillId);
            int commLevel       = SkillSystem.GetSkillLevel(counsellor, CommunicationSkillId);
            int affinityMod     = GetAffinityModifier(counsellor, patient, station);

            int total   = d20 + wisMod + chaHalf + persuasionLevel + commLevel + affinityMod;
            var outcome = MapRollToOutcome(total);

            return (outcome, total);
        }

        /// <summary>Maps a roll total to a <see cref="CounsellingOutcome"/> tier.</summary>
        public static CounsellingOutcome MapRollToOutcome(int roll)
        {
            if (roll >= CritSuccessThreshold)    return CounsellingOutcome.CriticalSuccess;
            if (roll >= SuccessThreshold)        return CounsellingOutcome.Success;
            if (roll >= PartialSuccessThreshold) return CounsellingOutcome.PartialSuccess;
            if (roll >= FailureThreshold)        return CounsellingOutcome.Failure;
            return CounsellingOutcome.CriticalFailure;
        }

        // ── Affinity modifier ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the affinity modifier for a counsellor/patient pair.
        /// Computed as Clamp(round(affinityScore / AffinityModDivisor), −3, +3).
        /// Returns 0 when no relationship record exists.
        /// </summary>
        public static int GetAffinityModifier(NPCInstance counsellor, NPCInstance patient,
                                              StationState station)
        {
            var rec = RelationshipRegistry.Get(station, counsellor.uid, patient.uid);
            if (rec == null) return 0;
            return Mathf.Clamp(
                Mathf.RoundToInt(rec.affinityScore / AffinityModDivisor),
                -AffinityModCap, AffinityModCap);
        }

        // ── Apply outcome ─────────────────────────────────────────────────────

        private void ApplyOutcome(NPCInstance counsellor, NPCInstance patient,
                                  CounsellingOutcome outcome, int roll, StationState station)
        {
            bool patientInBreakdown = patient.sanity?.isInBreakdown ?? false;

            switch (outcome)
            {
                case CounsellingOutcome.CriticalSuccess:
                    if (patientInBreakdown)
                    {
                        SanitySystem.RegisterIntervention(patient);
                        _traits?.OnCounsellingComplete(patient, station);
                    }
                    station.LogEvent(
                        $"✅ Critical counselling success: {counsellor.name} fully restored {patient.name}'s mental stability.");
                    break;

                case CounsellingOutcome.Success:
                    if (patientInBreakdown)
                    {
                        SanitySystem.RegisterIntervention(patient);
                        _traits?.OnCounsellingComplete(patient, station);
                    }
                    station.LogEvent(
                        $"✅ Counselling succeeded: {counsellor.name} helped {patient.name} regain composure.");
                    break;

                case CounsellingOutcome.PartialSuccess:
                    if (patientInBreakdown)
                        SanitySystem.RegisterIntervention(patient);
                    station.LogEvent(
                        $"⚠ Partial counselling result: {counsellor.name} stabilised {patient.name} but could not resolve underlying issues.");
                    break;

                case CounsellingOutcome.Failure:
                    _patientCooldowns[patient.uid] = station.tick + CooldownTicksOnFailure;
                    station.LogEvent(
                        $"❌ Counselling failed: {counsellor.name} could not reach {patient.name}. A new attempt can be made after the cooldown.");
                    break;

                case CounsellingOutcome.CriticalFailure:
                    _patientCooldowns[patient.uid] = station.tick + CooldownTicksOnCritFailure;
                    station.LogEvent(
                        $"❌ Critical counselling failure: {counsellor.name}'s session with {patient.name} was counterproductive. Extended cooldown applied.");
                    break;
            }

            // Award skill XP to the counsellor on every completed session.
            _skills?.AwardSkillXP(counsellor, PersuasionSkillId,    PersuasionXPPerSession, station);
            _skills?.AwardSkillXP(counsellor, CommunicationSkillId, SocialXPPerSession,     station);

            // Fire the session-complete event for UI / downstream listeners.
            OnSessionComplete?.Invoke(counsellor, patient, outcome, roll);

            station.LogEvent(
                $"[Counselling] {counsellor.name} → {patient.name} | roll={roll} | outcome={outcome}");
        }
    }
}
