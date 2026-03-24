// MedicalDefinitions.cs — Static template definitions for the medical system.
//
// These classes represent the data/authoring layer for the medical system:
//   BodyPartDefinition    — a single body part in the species tree
//   BodyPartTreeDefinition — the ordered list of parts for a species
//   WoundTypeDefinition   — wound type with bleed rates and modifiers per severity
//   DiseaseDefinition     — disease stages, transmission, immunity, etc.
//
// In a full Unity asset-pipeline integration these would inherit from ScriptableObject
// so designers can create/edit them without code changes. For now they are plain C#
// data classes loaded by HumanBodyTree (hardcoded) or ContentRegistry (future JSON).
//
// Feature gate: FEATURE_MEDICAL_SYSTEM (FeatureFlags.MedicalSystem)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // ── Enumerations ──────────────────────────────────────────────────────────

    /// <summary>Rules applied when a body part reaches 0% health.</summary>
    public enum VitalRule
    {
        /// <summary>No vital consequence — part is incapacitated but death does not follow.</summary>
        None,
        /// <summary>NPC dies immediately on destruction.</summary>
        InstantDeath,
        /// <summary>Paired organ rule — when both organs in the pair reach 0%, death follows in 5 ticks.</summary>
        PairedOrgan5Ticks,
        /// <summary>Paired organ rule — when both organs in the pair reach 0%, death follows in 192 ticks (48 h).</summary>
        PairedOrgan192Ticks,
    }

    /// <summary>Which side of the body this part belongs to (for mirrored pairs).</summary>
    public enum BodySide { None, Left, Right }

    /// <summary>Severity tier of a wound.</summary>
    public enum WoundSeverity { Minor = 1, Moderate = 2, Severe = 3, Critical = 4 }

    /// <summary>Wound category — determines which bleed rate table to use.</summary>
    public enum WoundType
    {
        Laceration,  // cut / slash
        Puncture,    // stab / spike
        Gunshot,     // ballistic entry + exit
        Blunt,       // bludgeon / bruise (may fracture)
        Burn,        // thermal / chemical
        Fracture,    // bone break — minimal direct bleeding
    }

    /// <summary>How a disease spreads between NPCs.</summary>
    public enum DiseaseTransmission { None, Contact, Droplet, Vector, Bloodborne, Airborne }

    // ── BodyPartDefinition ────────────────────────────────────────────────────

    /// <summary>
    /// Defines one node in a species body-part tree.
    /// TODO: Convert to ScriptableObject when the Unity asset pipeline is wired.
    /// </summary>
    [Serializable]
    public class BodyPartDefinition
    {
        /// <summary>Unique identifier within the tree, e.g. "brain", "left_hand".</summary>
        public string partId;

        /// <summary>Human-readable name.</summary>
        public string displayName;

        /// <summary>Part id of the parent in the tree, or null for roots.</summary>
        public string parentId;

        /// <summary>Which side of the body (None for centre-line parts).</summary>
        public BodySide side;

        /// <summary>What happens when this part reaches 0% health.</summary>
        public VitalRule vitalRule;

        /// <summary>
        /// The part id of the paired organ (used with PairedOrgan* vital rules).
        /// e.g. "left_lung" has pairedPartId = "right_lung".
        /// </summary>
        public string pairedPartId;

        /// <summary>
        /// Weight factor used when computing whole-body health score from part health.
        /// Higher = this part contributes more to overall health.
        /// </summary>
        public float healthWeight = 1f;

        /// <summary>
        /// Functional tags describing what the part contributes.
        /// e.g. "locomotion", "manipulation", "sight", "hearing", "respiration", "circulation",
        ///      "digestion", "excretion", "neural".
        /// </summary>
        public List<string> functionTags = new List<string>();

        /// <summary>
        /// Coverage region tag grouping the part into a body region.
        /// e.g. "head_region", "torso_region", "left_arm_region", "facial_region".
        /// </summary>
        public string coverageTag;

        public static BodyPartDefinition Create(string partId, string displayName,
            string parentId, BodySide side, VitalRule vitalRule,
            string pairedPartId, float healthWeight, string coverageTag,
            params string[] functionTags)
        {
            var def = new BodyPartDefinition
            {
                partId       = partId,
                displayName  = displayName,
                parentId     = parentId,
                side         = side,
                vitalRule    = vitalRule,
                pairedPartId = pairedPartId,
                healthWeight = healthWeight,
                coverageTag  = coverageTag,
            };
            foreach (var t in functionTags) def.functionTags.Add(t);
            return def;
        }
    }

    // ── BodyPartTreeDefinition ────────────────────────────────────────────────

    /// <summary>
    /// Ordered list of BodyPartDefinitions defining the full body tree for a species.
    /// TODO: Convert to ScriptableObject.
    /// </summary>
    [Serializable]
    public class BodyPartTreeDefinition
    {
        public string speciesId;
        public List<BodyPartDefinition> parts = new List<BodyPartDefinition>();

        public Dictionary<string, BodyPartDefinition> BuildIndex()
        {
            var index = new Dictionary<string, BodyPartDefinition>(parts.Count);
            foreach (var p in parts)
                if (!string.IsNullOrEmpty(p.partId))
                    index[p.partId] = p;
            return index;
        }
    }

    // ── WoundTypeDefinition ────────────────────────────────────────────────────

    /// <summary>
    /// Per-wound-type configuration including bleed rates per severity tier.
    /// bleedRates array is indexed by (WoundSeverity - 1): [Minor, Moderate, Severe, Critical].
    /// TODO: Convert to ScriptableObject.
    /// </summary>
    [Serializable]
    public class WoundTypeDefinition
    {
        public WoundType type;

        /// <summary>
        /// Blood volume lost per tick for each severity tier.
        /// Index 0 = Minor, 1 = Moderate, 2 = Severe, 3 = Critical.
        /// (Blood volume is 0–100%; these are % points per tick.)
        /// </summary>
        public float[] bleedRates = new float[4];

        /// <summary>Additive modifier to infection base chance per tick.</summary>
        public float infectionChanceModifier;

        /// <summary>Additive modifier to pain contribution per point of severity.</summary>
        public float painModifier;

        /// <summary>Base scar chance on wound full-heal (0–1).</summary>
        public float baseScarChance;

        /// <summary>Returns the bleed rate for a given severity (clamped to valid range).</summary>
        public float GetBleedRate(WoundSeverity severity)
        {
            int idx = Mathf.Clamp((int)severity - 1, 0, 3);
            return bleedRates[idx];
        }
    }

    // ── DiseaseStageDefinition ─────────────────────────────────────────────────

    /// <summary>One stage of a disease's progression.</summary>
    [Serializable]
    public class DiseaseStageDefinition
    {
        public string stageName;
        /// <summary>Ticks the NPC remains in this stage before advancing to the next.</summary>
        public int    durationTicks;
        /// <summary>Pain contribution per tick while in this stage.</summary>
        public float  painPerTick;
        /// <summary>Blood volume drain per tick (0 for non-infectious diseases).</summary>
        public float  bloodDrainPerTick;
        /// <summary>Mood delta applied as a named modifier while in this stage.</summary>
        public float  moodModifier;
    }

    // ── DiseaseDefinition ─────────────────────────────────────────────────────

    /// <summary>
    /// Static definition of a disease or infection.
    /// TODO: Convert to ScriptableObject.
    /// </summary>
    [Serializable]
    public class DiseaseDefinition
    {
        public string               diseaseId;
        public string               displayName;
        /// <summary>Part IDs that this disease primarily affects (empty = systemic).</summary>
        public List<string>         affectedPartIds = new List<string>();
        public List<DiseaseStageDefinition> stages  = new List<DiseaseStageDefinition>();
        public DiseaseTransmission  transmission;
        /// <summary>Ticks of immunity after clearing the disease (0 = no immunity).</summary>
        public int                  immunityDurationTicks;
        /// <summary>Chronic diseases regress only to stage 0 rather than clearing.</summary>
        public bool                 isChronic;

        // ── Built-in disease catalog ──────────────────────────────────────────

        public static DiseaseDefinition WoundInfection()
        {
            return new DiseaseDefinition
            {
                diseaseId   = "disease.wound_infection",
                displayName = "Wound Infection",
                transmission = DiseaseTransmission.None,
                immunityDurationTicks = 0,
                stages = new List<DiseaseStageDefinition>
                {
                    new DiseaseStageDefinition { stageName="Incubating",   durationTicks=36,  painPerTick=0f,    bloodDrainPerTick=0f,    moodModifier=-2f },
                    new DiseaseStageDefinition { stageName="Mild",         durationTicks=48,  painPerTick=2f,    bloodDrainPerTick=0.1f,  moodModifier=-5f },
                    new DiseaseStageDefinition { stageName="Moderate",     durationTicks=48,  painPerTick=5f,    bloodDrainPerTick=0.2f,  moodModifier=-8f },
                    new DiseaseStageDefinition { stageName="Severe",       durationTicks=48,  painPerTick=10f,   bloodDrainPerTick=0.5f,  moodModifier=-12f },
                    new DiseaseStageDefinition { stageName="Septic",       durationTicks=24,  painPerTick=20f,   bloodDrainPerTick=1.5f,  moodModifier=-20f },
                }
            };
        }
    }
}
