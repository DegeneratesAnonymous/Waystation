// MedicalRuntime.cs — Runtime state classes for the medical system.
//
// These are the mutable per-NPC data structures that live inside MedicalProfile.
// MedicalProfile is attached to NPCInstance as a nullable field — existing NPCs
// without it are treated as fully healthy (the feature flag guards all processing).
//
// Class hierarchy:
//   MedicalProfile  — top-level, attached to NPCInstance
//     BodyPart[]    — one per part in the species body tree
//       Wound[]     — active wounds on this part
//         Scar[]    — permanent scars left after wounds heal
//       ActiveDisease[] — diseases affecting this part
//   FunctionalPenaltyProfile — derived penalty set, written each tick by MedicalTickSystem
//
// Feature gate: FEATURE_MEDICAL_SYSTEM (FeatureFlags.MedicalSystem)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // ── Wound ─────────────────────────────────────────────────────────────────

    [Serializable]
    public class Wound
    {
        /// <summary>Unique ID within the BodyPart's wound list.</summary>
        public string uid;

        public WoundType     type;
        public WoundSeverity severity;

        /// <summary>Has this wound been cleaned / dressed by a TreatmentAction?</summary>
        public bool isTreated;

        /// <summary>Blood volume % drained per tick.  Derived from WoundTypeDefinition.</summary>
        public float bleedRatePerTick;

        /// <summary>Pain contribution in % points per tick while active.</summary>
        public float painContribution;

        /// <summary>Healing progress 0–1.  Reaches 1 when wound fully heals.</summary>
        public float healingProgress;

        /// <summary>Accumulated infection chance (0–100).  Rolls every 12 ticks when untreated.</summary>
        public float infectionAccumulation;

        /// <summary>True once infection has been triggered on this wound.</summary>
        public bool isInfected;

        /// <summary>Tick this wound was created.</summary>
        public int createdAtTick;

        /// <summary>
        /// Quality of the most recent treatment action (0–1).
        /// Used for scar chance calculation.
        /// </summary>
        public float treatmentQuality;

        /// <summary>Healing rate multiplier applied on top of the base treated rate.</summary>
        /// <remarks>
        /// Default 1f. SetFracture raises this to 1.5f–2.0f based on treatment quality.
        /// </remarks>
        public float healingRateMultiplier = 1f;

        public static Wound Create(WoundType type, WoundSeverity severity,
                                   float bleedRate, float painContrib, int currentTick)
        {
            return new Wound
            {
                uid                  = Guid.NewGuid().ToString("N")[..6],
                type                 = type,
                severity             = severity,
                bleedRatePerTick     = bleedRate,
                painContribution     = painContrib,
                healingProgress      = 0f,
                infectionAccumulation = 0f,
                createdAtTick        = currentTick,
            };
        }
    }

    // ── Scar ──────────────────────────────────────────────────────────────────

    [Serializable]
    public class Scar
    {
        public string uid;
        public WoundType     sourceWoundType;
        public WoundSeverity sourceSeverity;
        /// <summary>Part id where this scar formed.</summary>
        public string        partId;
        /// <summary>Coverage region of the part (for region-based lineage tracking).</summary>
        public string        coverageTag;
        /// <summary>Permanent functional penalty magnitude applied to FunctionalPenaltyProfile.</summary>
        public float         functionalPenalty;

        public static Scar Create(WoundType woundType, WoundSeverity severity,
                                  string partId, string coverageTag)
        {
            float penalty = 0.01f * (int)severity; // minor=0.01, moderate=0.02, severe=0.03, critical=0.04
            return new Scar
            {
                uid             = Guid.NewGuid().ToString("N")[..6],
                sourceWoundType = woundType,
                sourceSeverity  = severity,
                partId          = partId,
                coverageTag     = coverageTag,
                functionalPenalty = penalty,
            };
        }
    }

    // ── ActiveDisease ─────────────────────────────────────────────────────────

    [Serializable]
    public class ActiveDisease
    {
        public string uid;
        public string diseaseId;
        public string displayName;
        /// <summary>Current stage index into DiseaseDefinition.stages.</summary>
        public int    currentStage;
        /// <summary>Ticks spent in the current stage.</summary>
        public int    ticksInStage;
        /// <summary>Part ids affected (copied from DiseaseDefinition at creation).</summary>
        public List<string> affectedPartIds = new List<string>();
        /// <summary>Whether the disease is chronic (regresses to stage 0 instead of clearing).</summary>
        public bool isChronic;
        /// <summary>Tick at which immunity expires (0 = no immunity tracking).</summary>
        public int immunityExpiresAtTick;

        public static ActiveDisease Create(DiseaseDefinition def, int currentTick)
        {
            var d = new ActiveDisease
            {
                uid          = Guid.NewGuid().ToString("N")[..6],
                diseaseId    = def.diseaseId,
                displayName  = def.displayName,
                currentStage = 0,
                ticksInStage = 0,
                isChronic    = def.isChronic,
                immunityExpiresAtTick = def.immunityDurationTicks > 0
                    ? currentTick + def.immunityDurationTicks : 0,
            };
            foreach (var p in def.affectedPartIds) d.affectedPartIds.Add(p);
            return d;
        }
    }

    // ── BodyPart ──────────────────────────────────────────────────────────────

    [Serializable]
    public class BodyPart
    {
        /// <summary>Matches BodyPartDefinition.partId.</summary>
        public string partId;

        /// <summary>Current health 0–100.  100 = fully healthy, 0 = destroyed.</summary>
        public float health = 100f;

        /// <summary>True when this part has been amputated.</summary>
        public bool isAmputated;

        /// <summary>Active wounds on this part.</summary>
        public List<Wound> wounds = new List<Wound>();

        /// <summary>Active diseases on this part.</summary>
        public List<ActiveDisease> diseases = new List<ActiveDisease>();

        /// <summary>Permanent scars on this part.</summary>
        public List<Scar> scars = new List<Scar>();

        public static BodyPart Create(string partId)
            => new BodyPart { partId = partId };
    }

    // ── FunctionalPenaltyProfile ──────────────────────────────────────────────

    /// <summary>
    /// Derived functional penalties written each tick by MedicalTickSystem.
    /// Values are 0–1 multipliers where 0 = fully incapacitated, 1 = fully functional.
    /// Consumed by movement, skill check, and job systems.
    /// </summary>
    [Serializable]
    public class FunctionalPenaltyProfile
    {
        /// <summary>Move speed multiplier (legs, knees, ankles, feet, hips, spine).</summary>
        public float locomotionModifier    = 1f;
        /// <summary>Hand/arm manipulation multiplier (arms, hands, fingers, wrists, shoulders).</summary>
        public float manipulationModifier  = 1f;
        /// <summary>Vision multiplier (eyes).</summary>
        public float sightModifier         = 1f;
        /// <summary>Hearing multiplier (ears).</summary>
        public float hearingModifier       = 1f;
        /// <summary>Respiratory function multiplier (lungs).</summary>
        public float respirationModifier   = 1f;
        /// <summary>Cardiac function multiplier (heart).</summary>
        public float circulationModifier   = 1f;
        /// <summary>Digestive/metabolic multiplier (stomach, liver, etc.).</summary>
        public float digestionModifier     = 1f;
        /// <summary>General organ function (kidneys, etc.).</summary>
        public float organFunctionModifier = 1f;

        /// <summary>
        /// Permanent scar penalty accumulator — stacks with part-health-based penalties.
        /// </summary>
        public float scarPenaltyAccumulator = 0f;

        public void Reset()
        {
            locomotionModifier    = 1f;
            manipulationModifier  = 1f;
            sightModifier         = 1f;
            hearingModifier       = 1f;
            respirationModifier   = 1f;
            circulationModifier   = 1f;
            digestionModifier     = 1f;
            organFunctionModifier = 1f;
            // scarPenaltyAccumulator is NOT reset — it is permanent
        }
    }

    // ── MedicalProfile ────────────────────────────────────────────────────────

    /// <summary>
    /// Top-level medical runtime state for one NPC.
    /// Attached to NPCInstance.medicalProfile (null = medical system not initialised for this NPC).
    /// </summary>
    [Serializable]
    public class MedicalProfile
    {
        // ── Blood ─────────────────────────────────────────────────────────────

        /// <summary>Blood volume 0–100%.  Reaches 0 = NPC dies from blood loss.</summary>
        public float bloodVolume = 100f;

        /// <summary>Natural blood recovery rate per tick (when bloodVolume > 0).</summary>
        /// <remarks>At 0.1%/tick × 96 ticks/day ≈ 9.6% per in-game day; full recovery in ~10 days.</remarks>
        public const float BloodRecoveryPerTick = 0.1f;

        // ── Pain & Consciousness ──────────────────────────────────────────────

        /// <summary>Derived total pain 0–100.  Written by PainSystem each tick.</summary>
        public float pain;

        /// <summary>Derived consciousness 0–100.  Written by ConsciousnessSystem each tick.</summary>
        public float consciousness = 100f;

        /// <summary>True while consciousness is at 0 (NPC is unconscious).</summary>
        public bool isUnconscious;

        // ── Paired organ death timers ─────────────────────────────────────────

        /// <summary>Ticks remaining on the "both lungs destroyed" death timer. 0 = not active.</summary>
        public int lungDeathTicksRemaining;

        /// <summary>Ticks remaining on the "both kidneys destroyed" death timer. 0 = not active.</summary>
        public int kidneyDeathTicksRemaining;

        // ── Species ───────────────────────────────────────────────────────────

        /// <summary>Species tree ID used to build the body part map.</summary>
        public string speciesId = "human";

        // ── Body Parts ────────────────────────────────────────────────────────

        /// <summary>One BodyPart entry per part in the species tree, keyed by partId.</summary>
        public Dictionary<string, BodyPart> parts = new Dictionary<string, BodyPart>();

        // ── Functional Penalties ──────────────────────────────────────────────

        public FunctionalPenaltyProfile penalties = new FunctionalPenaltyProfile();

        // ── Mood Modifier Tracking ────────────────────────────────────────────

        // (No per-tick cache needed — mood modifiers are pushed every tick and
        //  refreshed via MoodSystem.PushModifier dedup by eventId+source.)

        // ── Analgesic (painkiller) state ──────────────────────────────────────

        /// <summary>Current analgesic strength 0–1. Applied as a pain reduction multiplier in PainSystem.</summary>
        public float analgesicStrength = 0f;
        /// <summary>Ticks remaining for the current analgesic dose. Decremented each tick; clears at 0.</summary>
        public int   analgesicDurationTicks = 0;

        // ── Antibiotic state ──────────────────────────────────────────────────

        /// <summary>Current antibiotic strength 0–1. Applied in TickDiseases to slow/regress infection stages.</summary>
        public float antibioticsStrength = 0f;
        /// <summary>Ticks remaining for the current antibiotic course. Decremented each tick; clears at 0.</summary>
        public int   antibioticsDurationTicks = 0;

        // ── Factory ───────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises a MedicalProfile using the supplied body part tree definition.
        /// </summary>
        public static MedicalProfile Create(BodyPartTreeDefinition tree)
        {
            var profile = new MedicalProfile { speciesId = tree.speciesId };
            foreach (var def in tree.parts)
                profile.parts[def.partId] = BodyPart.Create(def.partId);
            return profile;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public BodyPart GetPart(string partId)
        {
            parts.TryGetValue(partId, out var p);
            return p;
        }

        public bool TryGetPart(string partId, out BodyPart part)
            => parts.TryGetValue(partId, out part);

        /// <summary>Total number of wounds across all body parts.</summary>
        public int TotalWoundCount()
        {
            int n = 0;
            foreach (var p in parts.Values)
                n += p.wounds.Count;
            return n;
        }

        /// <summary>Count of scars on parts whose coverageTag matches the given region tag.</summary>
        public int ScarCountInRegion(string coverageTag,
                                     Dictionary<string, BodyPartDefinition> partIndex)
        {
            int n = 0;
            foreach (var kv in parts)
            {
                if (!partIndex.TryGetValue(kv.Key, out var def)) continue;
                if (def.coverageTag != coverageTag) continue;
                n += kv.Value.scars.Count;
            }
            return n;
        }

        /// <summary>Cumulative scar count for facial_region.</summary>
        public int FacialScarCount(Dictionary<string, BodyPartDefinition> partIndex)
            => ScarCountInRegion("facial_region", partIndex);
    }
}
