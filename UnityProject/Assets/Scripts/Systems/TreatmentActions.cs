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
    /// Administers a painkiller to an NPC.
    /// Sets a temporary analgesic state on the MedicalProfile that PainSystem reads each tick
    /// to suppress pain derivation. Does NOT permanently modify wound painContribution.
    /// </summary>
    public class AdministerPainkiller : TreatmentAction
    {
        /// <summary>Base duration of painkiller effect in ticks. (96 ticks = 1 in-game day; 16 ticks = 4 in-game hours).</summary>
        public const int BaseDurationTicks = 16;

        /// <summary>
        /// Sets an analgesic state on the MedicalProfile. Pain suppression is applied
        /// in PainSystem.Derive each tick for the duration of the effect.
        /// Returns quality (0–1).
        /// </summary>
        public float Apply(MedicalProfile profile, float quality)
        {
            if (profile == null) return 0f;
            // Higher quality = stronger and longer-lasting analgesic effect
            profile.analgesicStrength      = Mathf.Lerp(0.2f, 0.7f, quality);
            profile.analgesicDurationTicks = Mathf.RoundToInt(BaseDurationTicks * Mathf.Lerp(0.5f, 1.5f, quality));
            return quality;
        }
    }

    // ── SetFracture ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a fracture wound: aligns bone, applies a healing-rate multiplier
    /// (1.5× at average quality → 2× at perfect quality), and reduces pain.
    /// Only effective on WoundType.Fracture wounds.
    /// </summary>
    public class SetFracture : TreatmentAction
    {
        public override string RequiredSkillId => "skill.medical";

        public float Apply(Wound wound, float quality)
        {
            if (wound == null || wound.type != WoundType.Fracture) return 0f;
            wound.isTreated             = true;
            wound.treatmentQuality      = Mathf.Max(wound.treatmentQuality, quality);
            // Set bone multiplies healing rate: 1.5× at quality=0, 2.0× at quality=1.
            wound.healingRateMultiplier = Mathf.Lerp(1.5f, 2.0f, quality);
            // Also reduces pain contribution from the injury.
            wound.painContribution     *= Mathf.Lerp(0.8f, 0.4f, quality);
            return quality;
        }
    }

    // ── AdministerAntibiotics ─────────────────────────────────────────────────

    /// <summary>
    /// Administers antibiotics to an NPC.
    /// Clears infection accumulation on wounds and sets an antibiotic course state on the
    /// MedicalProfile. MedicalTickSystem.TickDiseases reads this state to slow infection
    /// progression and eventually regress active disease stages.
    /// </summary>
    public class AdministerAntibiotics : TreatmentAction
    {
        /// <summary>Base duration of an antibiotic course in ticks. (96 ticks = 1 in-game day; 192 ticks = 2 in-game days).</summary>
        public const int BaseCourseTicks = 192;

        public float Apply(BodyPart part, MedicalProfile profile, float quality)
        {
            if (part == null || profile == null) return 0f;

            // Clear infection accumulation on all wounds in the treated part
            foreach (var w in part.wounds)
                w.infectionAccumulation = 0f;

            // Set antibiotic course state: MedicalTickSystem.TickDiseases reads this
            // to slow disease stage progression and regress stages over the course duration.
            profile.antibioticsStrength      = Mathf.Lerp(0.4f, 1.0f, quality);
            profile.antibioticsDurationTicks = Mathf.RoundToInt(BaseCourseTicks * Mathf.Lerp(0.7f, 1.3f, quality));
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
