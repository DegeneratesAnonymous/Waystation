// HumanBodyTree.cs — Static factory for the Human body part tree (spec Section 1).
//
// Produces a BodyPartTreeDefinition with 72 body parts covering the full human anatomy.
// Called once during system initialisation; the result is cached on MedicalTickSystem.
//
// Vital rules:
//   InstantDeath        — brain, heart, spine, skull, torso, neck, head
//   PairedOrgan5Ticks   — left_lung / right_lung
//   PairedOrgan192Ticks — left_kidney / right_kidney
//
// Coverage region tags (used for scar lineage tracking):
//   head_region, facial_region, neck_region, torso_region,
//   left_arm_region, right_arm_region, left_leg_region, right_leg_region
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class HumanBodyTree
    {
        private static BodyPartTreeDefinition _cached;

        public static BodyPartTreeDefinition Get()
        {
            if (_cached != null) return _cached;
            _cached = Build();
            return _cached;
        }

        private static BodyPartTreeDefinition Build()
        {
            var tree = new BodyPartTreeDefinition { speciesId = "human" };
            var p = tree.parts;

            // ── Head ─────────────────────────────────────────────────────────
            // head     parent=null      vital=InstantDeath
            p.Add(Def("head",           "Head",              null,       BodySide.None,  VitalRule.InstantDeath,     null,           5f, "head_region",       "neural"));
            p.Add(Def("skull",          "Skull",             "head",     BodySide.None,  VitalRule.InstantDeath,     null,           3f, "head_region"));
            p.Add(Def("brain",          "Brain",             "skull",    BodySide.None,  VitalRule.InstantDeath,     null,           8f, "head_region",       "neural"));
            p.Add(Def("face",           "Face",              "head",     BodySide.None,  VitalRule.None,             null,           1f, "facial_region"));
            p.Add(Def("left_eye",       "Left Eye",          "face",     BodySide.Left,  VitalRule.None,             "right_eye",    1f, "facial_region",     "sight"));
            p.Add(Def("right_eye",      "Right Eye",         "face",     BodySide.Right, VitalRule.None,             "left_eye",     1f, "facial_region",     "sight"));
            p.Add(Def("nose",           "Nose",              "face",     BodySide.None,  VitalRule.None,             null,           0.5f,"facial_region"));
            p.Add(Def("mouth",          "Mouth",             "face",     BodySide.None,  VitalRule.None,             null,           0.5f,"facial_region",    "digestion"));
            p.Add(Def("jaw",            "Jaw",               "mouth",    BodySide.None,  VitalRule.None,             null,           0.5f,"facial_region"));
            p.Add(Def("left_ear",       "Left Ear",          "head",     BodySide.Left,  VitalRule.None,             "right_ear",    0.5f,"facial_region",    "hearing"));
            p.Add(Def("right_ear",      "Right Ear",         "head",     BodySide.Right, VitalRule.None,             "left_ear",     0.5f,"facial_region",    "hearing"));

            // ── Neck ─────────────────────────────────────────────────────────
            p.Add(Def("neck",           "Neck",              "head",     BodySide.None,  VitalRule.InstantDeath,     null,           3f, "neck_region",       "neural", "circulation"));

            // ── Torso ─────────────────────────────────────────────────────────
            p.Add(Def("torso",          "Torso",             null,       BodySide.None,  VitalRule.InstantDeath,     null,           8f, "torso_region"));
            p.Add(Def("spine",          "Spine",             "torso",    BodySide.None,  VitalRule.InstantDeath,     null,           5f, "torso_region",      "neural", "locomotion"));
            p.Add(Def("chest",          "Chest",             "torso",    BodySide.None,  VitalRule.None,             null,           3f, "torso_region"));
            p.Add(Def("left_collarbone","Left Collarbone",   "chest",    BodySide.Left,  VitalRule.None,             null,           1f, "torso_region",      "manipulation"));
            p.Add(Def("right_collarbone","Right Collarbone", "chest",    BodySide.Right, VitalRule.None,             null,           1f, "torso_region",      "manipulation"));
            p.Add(Def("ribs_left",      "Left Ribs",         "chest",    BodySide.Left,  VitalRule.None,             null,           1f, "torso_region"));
            p.Add(Def("ribs_right",     "Right Ribs",        "chest",    BodySide.Right, VitalRule.None,             null,           1f, "torso_region"));

            // Chest organs
            p.Add(Def("heart",          "Heart",             "chest",    BodySide.None,  VitalRule.InstantDeath,     null,           8f, "torso_region",      "circulation"));
            p.Add(Def("left_lung",      "Left Lung",         "chest",    BodySide.Left,  VitalRule.PairedOrgan5Ticks,"right_lung",   4f, "torso_region",      "respiration"));
            p.Add(Def("right_lung",     "Right Lung",        "chest",    BodySide.Right, VitalRule.PairedOrgan5Ticks,"left_lung",    4f, "torso_region",      "respiration"));

            // Shoulder girdles (connects arms)
            p.Add(Def("left_shoulder",  "Left Shoulder",     "torso",    BodySide.Left,  VitalRule.None,             null,           1f, "torso_region",      "manipulation"));
            p.Add(Def("right_shoulder", "Right Shoulder",    "torso",    BodySide.Right, VitalRule.None,             null,           1f, "torso_region",      "manipulation"));

            // Abdomen
            p.Add(Def("abdomen",        "Abdomen",           "torso",    BodySide.None,  VitalRule.None,             null,           3f, "torso_region"));
            p.Add(Def("stomach",        "Stomach",           "abdomen",  BodySide.None,  VitalRule.None,             null,           2f, "torso_region",      "digestion"));
            p.Add(Def("liver",          "Liver",             "abdomen",  BodySide.None,  VitalRule.None,             null,           3f, "torso_region",      "digestion"));
            p.Add(Def("left_kidney",    "Left Kidney",       "abdomen",  BodySide.Left,  VitalRule.PairedOrgan192Ticks,"right_kidney",2f,"torso_region",     "excretion"));
            p.Add(Def("right_kidney",   "Right Kidney",      "abdomen",  BodySide.Right, VitalRule.PairedOrgan192Ticks,"left_kidney", 2f,"torso_region",     "excretion"));
            p.Add(Def("small_intestine","Small Intestine",   "abdomen",  BodySide.None,  VitalRule.None,             null,           2f, "torso_region",      "digestion"));
            p.Add(Def("large_intestine","Large Intestine",   "abdomen",  BodySide.None,  VitalRule.None,             null,           1f, "torso_region",      "digestion"));
            p.Add(Def("gallbladder",    "Gallbladder",       "abdomen",  BodySide.None,  VitalRule.None,             null,           0.5f,"torso_region",     "digestion"));
            p.Add(Def("pancreas",       "Pancreas",          "abdomen",  BodySide.None,  VitalRule.None,             null,           1f, "torso_region",      "digestion"));
            p.Add(Def("bladder",        "Bladder",           "abdomen",  BodySide.None,  VitalRule.None,             null,           0.5f,"torso_region",     "excretion"));

            // Pelvis / hips (connects legs)
            p.Add(Def("pelvis",         "Pelvis",            "torso",    BodySide.None,  VitalRule.None,             null,           2f, "torso_region",      "locomotion"));
            p.Add(Def("left_hip",       "Left Hip",          "pelvis",   BodySide.Left,  VitalRule.None,             null,           1f, "torso_region",      "locomotion"));
            p.Add(Def("right_hip",      "Right Hip",         "pelvis",   BodySide.Right, VitalRule.None,             null,           1f, "torso_region",      "locomotion"));

            // ── Left Arm ──────────────────────────────────────────────────────
            p.Add(Def("left_arm",       "Left Arm",          "left_shoulder",  BodySide.Left,  VitalRule.None, null, 1f, "left_arm_region",  "manipulation"));
            p.Add(Def("left_upper_arm", "Left Upper Arm",    "left_arm",       BodySide.Left,  VitalRule.None, null, 1f, "left_arm_region",  "manipulation"));
            p.Add(Def("left_elbow",     "Left Elbow",        "left_upper_arm", BodySide.Left,  VitalRule.None, null, 0.5f,"left_arm_region", "manipulation"));
            p.Add(Def("left_lower_arm", "Left Lower Arm",    "left_elbow",     BodySide.Left,  VitalRule.None, null, 1f, "left_arm_region",  "manipulation"));
            p.Add(Def("left_wrist",     "Left Wrist",        "left_lower_arm", BodySide.Left,  VitalRule.None, null, 0.5f,"left_arm_region", "manipulation"));
            p.Add(Def("left_hand",      "Left Hand",         "left_wrist",     BodySide.Left,  VitalRule.None, null, 1f, "left_arm_region",  "manipulation"));
            p.Add(Def("left_thumb",     "Left Thumb",        "left_hand",      BodySide.Left,  VitalRule.None, null, 0.3f,"left_arm_region", "manipulation"));
            p.Add(Def("left_index",     "Left Index Finger", "left_hand",      BodySide.Left,  VitalRule.None, null, 0.2f,"left_arm_region", "manipulation"));
            p.Add(Def("left_middle",    "Left Middle Finger","left_hand",      BodySide.Left,  VitalRule.None, null, 0.2f,"left_arm_region", "manipulation"));
            p.Add(Def("left_ring",      "Left Ring Finger",  "left_hand",      BodySide.Left,  VitalRule.None, null, 0.2f,"left_arm_region", "manipulation"));
            p.Add(Def("left_pinky",     "Left Pinky Finger", "left_hand",      BodySide.Left,  VitalRule.None, null, 0.2f,"left_arm_region", "manipulation"));

            // ── Right Arm ─────────────────────────────────────────────────────
            p.Add(Def("right_arm",       "Right Arm",          "right_shoulder",  BodySide.Right, VitalRule.None, null, 1f, "right_arm_region", "manipulation"));
            p.Add(Def("right_upper_arm", "Right Upper Arm",    "right_arm",       BodySide.Right, VitalRule.None, null, 1f, "right_arm_region", "manipulation"));
            p.Add(Def("right_elbow",     "Right Elbow",        "right_upper_arm", BodySide.Right, VitalRule.None, null, 0.5f,"right_arm_region","manipulation"));
            p.Add(Def("right_lower_arm", "Right Lower Arm",    "right_elbow",     BodySide.Right, VitalRule.None, null, 1f, "right_arm_region", "manipulation"));
            p.Add(Def("right_wrist",     "Right Wrist",        "right_lower_arm", BodySide.Right, VitalRule.None, null, 0.5f,"right_arm_region","manipulation"));
            p.Add(Def("right_hand",      "Right Hand",         "right_wrist",     BodySide.Right, VitalRule.None, null, 1f, "right_arm_region", "manipulation"));
            p.Add(Def("right_thumb",     "Right Thumb",        "right_hand",      BodySide.Right, VitalRule.None, null, 0.3f,"right_arm_region","manipulation"));
            p.Add(Def("right_index",     "Right Index Finger", "right_hand",      BodySide.Right, VitalRule.None, null, 0.2f,"right_arm_region","manipulation"));
            p.Add(Def("right_middle",    "Right Middle Finger","right_hand",      BodySide.Right, VitalRule.None, null, 0.2f,"right_arm_region","manipulation"));
            p.Add(Def("right_ring",      "Right Ring Finger",  "right_hand",      BodySide.Right, VitalRule.None, null, 0.2f,"right_arm_region","manipulation"));
            p.Add(Def("right_pinky",     "Right Pinky Finger", "right_hand",      BodySide.Right, VitalRule.None, null, 0.2f,"right_arm_region","manipulation"));

            // ── Left Leg ──────────────────────────────────────────────────────
            p.Add(Def("left_leg",       "Left Leg",         "left_hip",       BodySide.Left,  VitalRule.None, null, 1f, "left_leg_region",  "locomotion"));
            p.Add(Def("left_thigh",     "Left Thigh",       "left_leg",       BodySide.Left,  VitalRule.None, null, 1f, "left_leg_region",  "locomotion"));
            p.Add(Def("left_knee",      "Left Knee",        "left_thigh",     BodySide.Left,  VitalRule.None, null, 0.5f,"left_leg_region",  "locomotion"));
            p.Add(Def("left_lower_leg", "Left Lower Leg",   "left_knee",      BodySide.Left,  VitalRule.None, null, 1f, "left_leg_region",  "locomotion"));
            p.Add(Def("left_ankle",     "Left Ankle",       "left_lower_leg", BodySide.Left,  VitalRule.None, null, 0.5f,"left_leg_region",  "locomotion"));
            p.Add(Def("left_foot",      "Left Foot",        "left_ankle",     BodySide.Left,  VitalRule.None, null, 0.5f,"left_leg_region",  "locomotion"));
            p.Add(Def("left_toes",      "Left Toes",        "left_foot",      BodySide.Left,  VitalRule.None, null, 0.3f,"left_leg_region",  "locomotion"));

            // ── Right Leg ─────────────────────────────────────────────────────
            p.Add(Def("right_leg",       "Right Leg",        "right_hip",       BodySide.Right, VitalRule.None, null, 1f, "right_leg_region", "locomotion"));
            p.Add(Def("right_thigh",     "Right Thigh",      "right_leg",       BodySide.Right, VitalRule.None, null, 1f, "right_leg_region", "locomotion"));
            p.Add(Def("right_knee",      "Right Knee",       "right_thigh",     BodySide.Right, VitalRule.None, null, 0.5f,"right_leg_region", "locomotion"));
            p.Add(Def("right_lower_leg", "Right Lower Leg",  "right_knee",      BodySide.Right, VitalRule.None, null, 1f, "right_leg_region", "locomotion"));
            p.Add(Def("right_ankle",     "Right Ankle",      "right_lower_leg", BodySide.Right, VitalRule.None, null, 0.5f,"right_leg_region", "locomotion"));
            p.Add(Def("right_foot",      "Right Foot",       "right_ankle",     BodySide.Right, VitalRule.None, null, 0.5f,"right_leg_region", "locomotion"));
            p.Add(Def("right_toes",      "Right Toes",       "right_foot",      BodySide.Right, VitalRule.None, null, 0.3f,"right_leg_region", "locomotion"));

            return tree;
        }

        /// <summary>Shorthand factory to reduce noise in the part list above.</summary>
        private static BodyPartDefinition Def(string id, string name, string parent,
            BodySide side, VitalRule vital, string paired, float weight, string coverage,
            params string[] functions)
        {
            return BodyPartDefinition.Create(id, name, parent, side, vital, paired,
                                             weight, coverage, functions);
        }

        // ── Built-in wound type definitions ────────────────────────────────────

        private static Dictionary<WoundType, WoundTypeDefinition> _woundTypes;

        public static Dictionary<WoundType, WoundTypeDefinition> GetWoundTypes()
        {
            if (_woundTypes != null) return _woundTypes;
            _woundTypes = new Dictionary<WoundType, WoundTypeDefinition>
            {
                [WoundType.Laceration] = new WoundTypeDefinition
                {
                    type                    = WoundType.Laceration,
                    bleedRates              = new float[] { 0.3f, 0.6f, 1.2f, 2.5f },
                    infectionChanceModifier = 0.5f,
                    painModifier            = 1.0f,
                    baseScarChance          = 0.30f,
                },
                [WoundType.Puncture] = new WoundTypeDefinition
                {
                    type                    = WoundType.Puncture,
                    bleedRates              = new float[] { 0.2f, 0.5f, 1.5f, 3.0f },
                    infectionChanceModifier = 1.0f,
                    painModifier            = 1.2f,
                    baseScarChance          = 0.25f,
                },
                [WoundType.Gunshot] = new WoundTypeDefinition
                {
                    type                    = WoundType.Gunshot,
                    bleedRates              = new float[] { 0.5f, 1.2f, 2.5f, 5.0f },
                    infectionChanceModifier = 0.8f,
                    painModifier            = 2.0f,
                    baseScarChance          = 0.50f,
                },
                [WoundType.Blunt] = new WoundTypeDefinition
                {
                    type                    = WoundType.Blunt,
                    bleedRates              = new float[] { 0.0f, 0.1f, 0.3f, 0.8f },
                    infectionChanceModifier = 0.2f,
                    painModifier            = 1.5f,
                    baseScarChance          = 0.15f,
                },
                [WoundType.Burn] = new WoundTypeDefinition
                {
                    type                    = WoundType.Burn,
                    bleedRates              = new float[] { 0.0f, 0.2f, 0.5f, 1.0f },
                    infectionChanceModifier = 1.5f,
                    painModifier            = 3.0f,
                    baseScarChance          = 0.60f,
                },
                [WoundType.Fracture] = new WoundTypeDefinition
                {
                    type                    = WoundType.Fracture,
                    bleedRates              = new float[] { 0.0f, 0.0f, 0.2f, 0.5f },
                    infectionChanceModifier = 0.1f,
                    painModifier            = 2.5f,
                    baseScarChance          = 0.10f,
                },
            };
            return _woundTypes;
        }
    }
}
