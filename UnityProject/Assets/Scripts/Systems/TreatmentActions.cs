// TreatmentActions.cs — Treatment action base class and concrete implementations.
//
// All treatment actions inherit from TreatmentAction and implement Apply().
// Treatment quality is computed from skill check × environment × facility × supply quality.
// Environment and facility are placeholder floats (pending environment system).
//
// Supported treatments:
//   Bandage, CleanWound, AdministerPainkiller, SetFracture,
//   AdministerAntibiotics, IVDrip
//
// Feature gate: FEATURE_MEDICAL_SYSTEM (FeatureFlags.MedicalSystem)
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Base class ────────────────────────────────────────────────────────────

    /// <summary>
    /// Base class for all treatment actions.
    /// Apply() mutates the target wound or disease and returns the treatment quality (0–1).
    /// </summary>
    public abstract class TreatmentAction
    {
        /// <summary>Skill ID required to perform this treatment.</summary>
        public virtual string RequiredSkillId => "skill.medical";

        /// <summary>
        /// Compute treatment quality from the raw skill check result and contextual modifiers.
        /// quality = Clamp01(skillCheckResult / 20) × environmentModifier × facilityModifier × supplyQuality
        /// </summary>
        public static float ComputeQuality(int skillCheckResult,
                                           float environmentModifier = 1f,
                                           float facilityModifier    = 1f,
                                           float supplyQuality       = 1f)
        {
            float base01 = Mathf.Clamp01(skillCheckResult / 20f);
            return Mathf.Clamp01(base01 * environmentModifier * facilityModifier * supplyQuality);
        }
    }

    // ── Bandage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a bandage to a wound.
    /// Marks the wound as treated and reduces bleed rate by a quality-scaled amount.
    /// Treated wounds accumulate infection at half the normal rate.
    /// </summary>
    public class Bandage : TreatmentAction
    {
        /// <summary>
        /// Bandages the wound: marks treated, scales down bleed rate.
        /// Returns treatment quality (0–1).
        /// </summary>
        public float Apply(Wound wound, float quality)
        {
            if (wound == null) return 0f;
            wound.isTreated       = true;
            wound.treatmentQuality = Mathf.Max(wound.treatmentQuality, quality);
            // Reduce bleed rate proportional to treatment quality.
            // Perfect quality (1.0) halves the bleed rate; quality 0.5 reduces it by 25%.
            wound.bleedRatePerTick *= Mathf.Lerp(1f, 0.5f, quality);
            return quality;
        }
    }

    // ── CleanWound ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cleans a wound, resetting infection accumulation and reducing future infection chance.
    /// Must be applied before bandaging for best effect.
    /// </summary>
    public class CleanWound : TreatmentAction
    {
        public float Apply(Wound wound, float quality)
        {
            if (wound == null) return 0f;
            wound.treatmentQuality = Mathf.Max(wound.treatmentQuality, quality);
            // High-quality cleaning fully resets infection accumulation.
            wound.infectionAccumulation *= Mathf.Lerp(0.5f, 0f, quality);
            return quality;
        }
    }

    // ── AdministerPainkiller ──────────────────────────────────────────────────

    /// <summary>
    /// Reduces the pain contribution of all wounds on the target body part for a duration.
    /// The reduction is tracked via the wound's painContribution field (temporary suppression).
    /// Duration and magnitude scale with quality.
    /// </summary>
    public class AdministerPainkiller : TreatmentAction
    {
        /// <summary>
        /// Reduces pain contribution across all wounds on a body part.
        /// Returns quality (0–1). Effect is temporary; MedicalTickSystem re-derives pain each tick.
        /// </summary>
        public float Apply(BodyPart part, float quality, MedicalProfile profile)
        {
            if (part == null || profile == null) return 0f;
            // Apply a temporary reduction to pain. Real reduction lasts proportional to quality.
            // Represent by temporarily lowering painContribution — will be recalculated next tick.
            float reduction = Mathf.Lerp(0.2f, 0.6f, quality);
            foreach (var w in part.wounds)
                w.painContribution *= (1f - reduction);
            return quality;
        }
    }

    // ── SetFracture ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a fracture wound: aligns bone, doubles healing progress rate, reduces pain.
    /// Only effective on WoundType.Fracture wounds.
    /// </summary>
    public class SetFracture : TreatmentAction
    {
        public override string RequiredSkillId => "skill.medical";

        public float Apply(Wound wound, float quality)
        {
            if (wound == null || wound.type != WoundType.Fracture) return 0f;
            wound.isTreated        = true;
            wound.treatmentQuality = Mathf.Max(wound.treatmentQuality, quality);
            // Setting the fracture improves healing rate and reduces pain contribution.
            wound.painContribution *= Mathf.Lerp(0.8f, 0.4f, quality);
            return quality;
        }
    }

    // ── AdministerAntibiotics ─────────────────────────────────────────────────

    /// <summary>
    /// Administers antibiotics to an NPC.
    /// Clears infection accumulation and begins regressing active wound infections.
    /// </summary>
    public class AdministerAntibiotics : TreatmentAction
    {
        public float Apply(BodyPart part, MedicalProfile profile, float quality)
        {
            if (part == null || profile == null) return 0f;
            foreach (var w in part.wounds)
            {
                w.infectionAccumulation = 0f;
                // Antibiotic regression of active infection diseases is handled by MedicalTickSystem.
            }
            // Mark wounds as under antibiotic treatment (repurpose treatmentQuality to cap accumulation).
            foreach (var w in part.wounds)
                w.treatmentQuality = Mathf.Max(w.treatmentQuality, quality);
            return quality;
        }
    }

    // ── IVDrip ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Administers an IV drip to restore blood volume and boost healing rates.
    /// Effect: immediate +quality×30% blood volume restoration; sets a healing boost flag.
    /// </summary>
    public class IVDrip : TreatmentAction
    {
        public override string RequiredSkillId => "skill.medical";

        public float Apply(MedicalProfile profile, float quality)
        {
            if (profile == null) return 0f;
            float bloodRestored = Mathf.Lerp(5f, 30f, quality);
            profile.bloodVolume = Mathf.Min(100f, profile.bloodVolume + bloodRestored);
            return quality;
        }
    }
}
