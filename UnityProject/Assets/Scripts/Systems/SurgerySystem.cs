// SurgerySystem.cs — Surgery roll, outcome tier mapping, and effect application.
//
// Roll formula (spec Section 12):
//   d20 + Surgery level + DEX modifier + environment modifier + facility modifier
//       + Medical level × 0.5
//
// Outcome tiers:
//   ≥ 22  Critical Success  — wound fully treated, healing rate ×3, no scar
//   16–21 Success           — wound treated, healing rate ×2
//   10–15 Partial Success   — wound partially treated, healing rate ×1.5
//   5–9   Failure           — no benefit, patient takes minor additional damage
//    ≤ 4  Critical Failure  — roll d6 sub-table, surgeon takes −1 sanity
//
// Critical Failure d6 sub-table:
//   1 — Wrong part damaged      (random adjacent part takes 10% damage)
//   2 — Excessive bleeding      (wound bleed rate ×3)
//   3 — Instrument left inside  (infection accumulation set to 100)
//   4 — Nerve damage            (permanent locomotion/manipulation penalty −0.05)
//   5 — Cardiac incident        (heart takes 20% damage)
//   6 — Patient dies            (immediate death)
//
// Feature gate: FEATURE_MEDICAL_SYSTEM (FeatureFlags.MedicalSystem)
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public enum SurgeryOutcome
    {
        CriticalSuccess,  // ≥ 22
        Success,          // 16–21
        PartialSuccess,   // 10–15
        Failure,          // 5–9
        CriticalFailure,  // ≤ 4
    }

    public enum CriticalFailureResult
    {
        WrongPartDamaged    = 1,
        ExcessiveBleeding   = 2,
        InstrumentLeft      = 3,
        NerveDamage         = 4,
        CardiacIncident     = 5,
        PatientDies         = 6,
    }

    public class SurgerySystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const int   CritSuccessThreshold    = 22;
        public const int   SuccessThreshold        = 16;
        public const int   PartialSuccessThreshold = 10;
        public const int   FailureThreshold        =  5;

        // ── Dependencies ──────────────────────────────────────────────────────

        private SanitySystem  _sanity;
        private TraitSystem   _traits;

        public void SetSanitySystem(SanitySystem s)  => _sanity = s;
        public void SetTraitSystem(TraitSystem t)     => _traits = t;

        // ── Main Surgery API ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the facility bonus modifier (+2 flat roll bonus) when at least one
        /// room on the station has an active medical_bay type assignment.  The check is
        /// station-wide rather than per-patient-location because NPCInstances do not
        /// carry a tile-grid position, and a single designated bay benefits all surgery
        /// performed on the station.
        /// Returns 0 when no qualifying active room bonus exists.
        /// </summary>
        public static float GetRoomFacilityBonus(StationState station)
        {
            if (station == null) return 0f;
            foreach (var bs in station.roomBonusCache.Values)
            {
                if (bs.bonusActive && bs.workbenchRoomType == "medical_bay")
                    return 2f; // +2 flat roll bonus for a fully equipped, designated Medical Bay
            }
            return 0f;
        }

        /// <summary>
        /// Performs a surgery roll for a surgeon NPC operating on a wound at a given part.
        /// Returns (outcome, roll, criticalFailureResult) where criticalFailureResult is
        /// null unless outcome is CriticalFailure.
        /// If facilityModifier is not supplied, it is automatically derived from the room
        /// bonus cache: +2 when any active medical_bay room exists on the station, 0 otherwise.
        /// </summary>
        public (SurgeryOutcome outcome, int roll, CriticalFailureResult? cfResult)
            PerformSurgery(NPCInstance surgeon, NPCInstance patient,
                          string targetPartId, Wound targetWound,
                          StationState station,
                          float environmentModifier = 0f,
                          float facilityModifier    = float.NaN)
        {
            if (!FeatureFlags.MedicalSystem) return (SurgeryOutcome.Failure, 0, null);

            // Auto-compute facility modifier from room bonus cache when not explicitly supplied
            if (float.IsNaN(facilityModifier))
                facilityModifier = GetRoomFacilityBonus(station);

            int surgeryLevel = SkillSystem.GetSkillLevel(surgeon, "skill.surgery");
            int medicalLevel = SkillSystem.GetSkillLevel(surgeon, "skill.medical");
            int dexMod       = surgeon.abilityScores.DEXMod;

            int   d20       = Random.Range(1, 21);
            float totalRoll = d20
                            + surgeryLevel
                            + dexMod
                            + environmentModifier
                            + facilityModifier
                            + (medicalLevel * 0.5f);
            int   roll      = Mathf.RoundToInt(totalRoll);

            var outcome = MapRollToOutcome(roll);
            CriticalFailureResult? cfResult = null;

            var profile  = patient.medicalProfile;
            var partDefs = HumanBodyTree.Get().BuildIndex();

            switch (outcome)
            {
                case SurgeryOutcome.CriticalSuccess:
                    ApplyCriticalSuccess(targetWound, profile, targetPartId, partDefs, station);
                    break;
                case SurgeryOutcome.Success:
                    ApplySuccess(targetWound);
                    break;
                case SurgeryOutcome.PartialSuccess:
                    ApplyPartialSuccess(targetWound);
                    break;
                case SurgeryOutcome.Failure:
                    ApplyFailure(patient, targetPartId, profile);
                    // Surgery failure: register condition pressure on the surgeon so that
                    // repeated failures can eventually trigger a fear or anxiety trait.
                    _traits?.RegisterConditionPressure(surgeon, TraitConditionCategory.SurgeryFailure, 2f);
                    break;
                case SurgeryOutcome.CriticalFailure:
                    int d6 = Random.Range(1, 7);
                    cfResult = (CriticalFailureResult)d6;
                    ApplyCriticalFailure(cfResult.Value, patient, surgeon, targetPartId,
                                         targetWound, profile, partDefs, station);
                    // Critical failure: stronger pressure spike on the surgeon.
                    _traits?.RegisterConditionPressure(surgeon, TraitConditionCategory.SurgeryFailure, 4f);
                    break;
            }

            return (outcome, roll, cfResult);
        }

        // ── Outcome mapping ───────────────────────────────────────────────────

        public static SurgeryOutcome MapRollToOutcome(int roll)
        {
            if (roll >= CritSuccessThreshold)    return SurgeryOutcome.CriticalSuccess;
            if (roll >= SuccessThreshold)        return SurgeryOutcome.Success;
            if (roll >= PartialSuccessThreshold) return SurgeryOutcome.PartialSuccess;
            if (roll >= FailureThreshold)        return SurgeryOutcome.Failure;
            return SurgeryOutcome.CriticalFailure;
        }

        // ── Effect application per outcome ────────────────────────────────────

        private static void ApplyCriticalSuccess(Wound wound, MedicalProfile profile,
            string partId, Dictionary<string, BodyPartDefinition> defs, StationState station)
        {
            if (wound == null) return;
            wound.isTreated         = true;
            wound.treatmentQuality  = 1f;
            wound.bleedRatePerTick  = 0f;
            wound.painContribution  = 0f;
            // Accelerate healing: set to near-complete
            wound.healingProgress   = Mathf.Min(wound.healingProgress + 0.4f, 1f);
            station?.LogEvent($"Surgery: Critical Success — wound stabilised.");
        }

        private static void ApplySuccess(Wound wound)
        {
            if (wound == null) return;
            wound.isTreated        = true;
            wound.treatmentQuality = Mathf.Max(wound.treatmentQuality, 0.8f);
            wound.bleedRatePerTick *= 0.5f;
            wound.painContribution *= 0.7f;
            wound.healingProgress  = Mathf.Min(wound.healingProgress + 0.2f, 1f);
        }

        private static void ApplyPartialSuccess(Wound wound)
        {
            if (wound == null) return;
            wound.isTreated        = true;
            wound.treatmentQuality = Mathf.Max(wound.treatmentQuality, 0.5f);
            wound.bleedRatePerTick *= 0.75f;
            wound.painContribution *= 0.85f;
            wound.healingProgress  = Mathf.Min(wound.healingProgress + 0.1f, 1f);
        }

        private static void ApplyFailure(NPCInstance patient, string partId, MedicalProfile profile)
        {
            if (profile == null) return;
            if (profile.TryGetPart(partId, out var part))
                part.health = Mathf.Max(0f, part.health - 5f);
        }

        private void ApplyCriticalFailure(CriticalFailureResult result,
            NPCInstance patient, NPCInstance surgeon,
            string partId, Wound wound, MedicalProfile profile,
            Dictionary<string, BodyPartDefinition> partDefs, StationState station)
        {
            if (profile == null) return;

            string msg = $"Surgery: Critical Failure — ";

            switch (result)
            {
                case CriticalFailureResult.WrongPartDamaged:
                    // Damage a random adjacent part
                    string adjacentId = GetRandomAdjacentPart(partId, partDefs);
                    if (adjacentId != null && profile.TryGetPart(adjacentId, out var adj))
                        adj.health = Mathf.Max(0f, adj.health - 10f);
                    msg += $"wrong part damaged ({adjacentId ?? "unknown"}).";
                    break;

                case CriticalFailureResult.ExcessiveBleeding:
                    if (wound != null) wound.bleedRatePerTick *= 3f;
                    msg += "excessive bleeding.";
                    break;

                case CriticalFailureResult.InstrumentLeft:
                    if (wound != null) wound.infectionAccumulation = 100f;
                    msg += "instrument left inside patient — infection risk critical.";
                    break;

                case CriticalFailureResult.NerveDamage:
                    // Permanent penalty to locomotion and manipulation
                    profile.penalties.locomotionModifier    = Mathf.Max(0f, profile.penalties.locomotionModifier   - 0.05f);
                    profile.penalties.manipulationModifier  = Mathf.Max(0f, profile.penalties.manipulationModifier - 0.05f);
                    profile.penalties.scarPenaltyAccumulator += 0.05f;
                    msg += "nerve damage — permanent functional penalty applied.";
                    break;

                case CriticalFailureResult.CardiacIncident:
                    if (profile.TryGetPart("heart", out var heart))
                        heart.health = Mathf.Max(0f, heart.health - 20f);
                    msg += "cardiac incident — heart damaged.";
                    break;

                case CriticalFailureResult.PatientDies:
                    KillNPC(patient, station);
                    msg += "patient died on the operating table.";
                    break;
            }

            station?.LogEvent(msg);

            // Surgeon sanity hit: −1 for any critical failure, −2 total for PatientDies.
            // Fear lineage pressure applied in all cases.
            int sanityDelta = result == CriticalFailureResult.PatientDies ? -2 : -1;
            _sanity?.AdjustScore(surgeon, sanityDelta, station);
            _traits?.ApplyLineageEvent(surgeon, "lineage.fear", -1, station);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetRandomAdjacentPart(string partId,
            Dictionary<string, BodyPartDefinition> defs)
        {
            if (!defs.TryGetValue(partId, out var def)) return null;

            // Collect parent and children as candidates
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(def.parentId)) candidates.Add(def.parentId);
            foreach (var kv in defs)
                if (kv.Value.parentId == partId)
                    candidates.Add(kv.Key);

            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }

        private static void KillNPC(NPCInstance npc, StationState station)
        {
            // Mark the NPC as dead — downstream systems (NPCSystem) will remove them.
            if (npc == null) return;
            npc.statusTags.Remove("crew");
            npc.statusTags.Remove("visitor");
            npc.statusTags.Add("dead");
            station?.LogEvent($"{npc.name} has died.");
        }
    }
}
