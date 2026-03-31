// SkillSystemDomainTests — EditMode unit tests for NPC-013 domain skill system.
//
// Validates:
//   • Domain skill score formula: primary_dominant = primary_stat + secondary_stat / 2
//   • Domain skill score formula: equal_weight = (primary_stat + secondary_stat) / 2
//   • Acceptance criteria: WIS 14 + INT 10 → Medical score = 19  (14 + 10/2)
//   • Perception derived stat: WIS + (INT + CHA) / 4
//   • Acceptance criteria: WIS 14, INT 12, CHA 8 → Perception = 19  (14 + (12+8)/4)
//   • Perception contested check: perceptionScore > opposingRoll → true (detected)
//   • Perception contested check: perceptionScore = opposingRoll → false (not detected)
//   • Perception contested check: perceptionScore < opposingRoll → false (not detected)
//   • Non-proficient XP: accrues at 50% rate (NonProficientXPRate)
//   • Non-proficient level cap: XP stops at level 6 (NonProficientLevelCap)
//   • Proficient skills: no XP penalty, no level cap
//   • Proficiency slot count: 2 + INT modifier
//   • Expertise slot unlock prompt fires at level 4 and level 8
//   • ChooseDomainExpertise: adds option ID to chosenExpertise and registers soft capability
//   • ChooseDomainExpertise: rejects unknown skill
//   • ChooseDomainExpertise: rejects insufficient skill level
//   • ChooseDomainExpertise: rejects already-chosen option
//   • GetDomainSkillScore: returns 0 for legacy (non-domain) skills
//   • SkillDefinition.IsDomainSkill: true when primary_stat is set
//   • UseNewSkillSystem = false: proficiency effects do not apply (backward compat)
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class SkillDomainTestHelpers
    {
        /// <summary>
        /// Builds a minimal ContentRegistry MonoBehaviour with a single domain skill registered.
        /// </summary>
        public static (ContentRegistry registry, GameObject go) MakeRegistryWithDomainSkill(
            SkillDefinition skill)
        {
            var go       = new GameObject("DomainSkillTestRegistry");
            var registry = go.AddComponent<ContentRegistry>();
            registry.Skills[skill.skillId] = skill;
            return (registry, go);
        }

        /// <summary>Builds a ContentRegistry with a legacy (non-domain) skill.</summary>
        public static (ContentRegistry registry, GameObject go) MakeRegistryWithLegacySkill(
            SkillDefinition skill)
        {
            var go       = new GameObject("LegacySkillTestRegistry");
            var registry = go.AddComponent<ContentRegistry>();
            registry.Skills[skill.skillId] = skill;
            return (registry, go);
        }

        /// <summary>
        /// Creates a minimal domain SkillDefinition for Medical using the NPC-013 schema.
        /// Medical: WIS (primary) + INT (secondary), primary_dominant.
        /// </summary>
        public static SkillDefinition MakeMedicalSkill()
        {
            var skill = new SkillDefinition
            {
                skillId      = "skill_medical",
                displayName  = "Medical",
                primaryStat  = "WIS",
                secondaryStat = "INT",
                weight       = SkillWeight.PrimaryDominant,
                proficiencyRequiredForMaxLevel = true,
            };
            // Add expertise slots mirroring the schema example in the issue
            var slot4 = new DomainExpertiseSlotDefinition { unlockLevel = 4 };
            slot4.options.Add(new DomainExpertiseOptionDefinition
            {
                id          = "exp_diagnosis",
                name        = "Diagnosis",
                description = "Unlocks medical assessment tasks.",
                taskTagsUnlocked = new List<string> { "diagnosis" },
            });
            slot4.options.Add(new DomainExpertiseOptionDefinition
            {
                id          = "exp_treatment",
                name        = "Treatment",
                description = "Unlocks treatment tasks.",
                taskTagsUnlocked = new List<string> { "treatment" },
            });
            slot4.options.Add(new DomainExpertiseOptionDefinition
            {
                id          = "exp_psychology",
                name        = "Psychology",
                description = "Unlocks counselling tasks.",
                taskTagsUnlocked = new List<string> { "psychology" },
            });
            var slot8 = new DomainExpertiseSlotDefinition { unlockLevel = 8 };
            slot8.options.Add(new DomainExpertiseOptionDefinition
            {
                id          = "exp_surgery",
                name        = "Surgery",
                description = "Unlocks surgical procedures.",
                taskTagsUnlocked = new List<string> { "surgery" },
            });
            slot8.options.Add(new DomainExpertiseOptionDefinition
            {
                id          = "exp_prosthetics",
                name        = "Prosthetics",
                description = "Unlocks implant fitting.",
                taskTagsUnlocked = new List<string> { "prosthetics" },
            });
            skill.domainExpertiseSlots.Add(slot4);
            skill.domainExpertiseSlots.Add(slot8);
            return skill;
        }

        /// <summary>Creates a legacy skill definition (no primary/secondary stats).</summary>
        public static SkillDefinition MakeLegacySkill()
            => new SkillDefinition
            {
                skillId          = "skill.farming",
                displayName      = "Farming",
                skillType        = SkillType.Simple,
                governingAbility = "WIS",
            };

        /// <summary>
        /// Creates an NPC with the given raw ability scores.
        /// STR, DEX, INT, WIS, CHA, END all default to 8.
        /// </summary>
        public static NPCInstance MakeNpc(
            string uid  = null,
            int str  = 8,
            int dex  = 8,
            int inte = 8,
            int wis  = 8,
            int cha  = 8,
            int end_ = 8)
        {
            var npc = new NPCInstance
            {
                uid  = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "TestNPC",
            };
            npc.abilityScores.STR = str;
            npc.abilityScores.DEX = dex;
            npc.abilityScores.INT = inte;
            npc.abilityScores.WIS = wis;
            npc.abilityScores.CHA = cha;
            npc.abilityScores.END = end_;
            return npc;
        }

        /// <summary>Sets a skill instance XP to reach exactly the given level.</summary>
        public static void SetSkillLevel(NPCInstance npc, string skillId, int level)
        {
            foreach (var inst in npc.skillInstances)
            {
                if (inst.skillId == skillId)
                {
                    inst.currentXP = level * level * 100f;
                    return;
                }
            }
            npc.skillInstances.Add(new SkillInstance
            {
                skillId   = skillId,
                currentXP = level * level * 100f,
            });
        }

        public static StationState MakeStation(int tick = 0)
        {
            var s = new StationState("DomainTestStation") { tick = tick };
            return s;
        }
    }

    // ── Domain skill score formula tests ─────────────────────────────────────

    [TestFixture]
    public class DomainSkillScoreTests
    {
        private GameObject     _go;
        private ContentRegistry _registry;
        private SkillSystem    _skillSystem;

        [SetUp]
        public void SetUp()
        {
            (_registry, _go) = SkillDomainTestHelpers.MakeRegistryWithDomainSkill(
                SkillDomainTestHelpers.MakeMedicalSkill());
            _skillSystem = new SkillSystem(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Medical_PrimaryDominant_WIS14_INT10_Returns19()
        {
            // Acceptance criterion: WIS 14 + INT 10 → Medical = 14 + (10/2) = 19
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 14, inte: 10);
            int score = _skillSystem.GetDomainSkillScore(npc, "skill_medical");
            Assert.AreEqual(19, score,
                "Medical (WIS 14, INT 10) should yield 14 + 10/2 = 19.");
        }

        [Test]
        public void Medical_PrimaryDominant_MinStats_Returns12()
        {
            // All stats at minimum (8): score = 8 + 8/2 = 12
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 8, inte: 8);
            int score = _skillSystem.GetDomainSkillScore(npc, "skill_medical");
            Assert.AreEqual(12, score,
                "Medical (WIS 8, INT 8) should yield 8 + 8/2 = 12.");
        }

        [Test]
        public void Medical_PrimaryDominant_MaxStats_Returns30()
        {
            // Max stats (20, 20): score = 20 + 20/2 = 30
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 20, inte: 20);
            int score = _skillSystem.GetDomainSkillScore(npc, "skill_medical");
            Assert.AreEqual(30, score,
                "Medical (WIS 20, INT 20) should yield 20 + 20/2 = 30.");
        }

        [Test]
        public void EqualWeight_ReturnsAverageOfTwoStats()
        {
            // Create an equal-weight variant of the skill
            var skill = new SkillDefinition
            {
                skillId       = "skill_equal_test",
                displayName   = "EqualTest",
                primaryStat   = "WIS",
                secondaryStat = "INT",
                weight        = SkillWeight.EqualWeight,
                proficiencyRequiredForMaxLevel = false,
            };
            var go2       = new GameObject("EqualWeightTestRegistry");
            var registry2 = go2.AddComponent<ContentRegistry>();
            registry2.Skills[skill.skillId] = skill;
            var sys2 = new SkillSystem(registry2);

            var npc = SkillDomainTestHelpers.MakeNpc(wis: 14, inte: 10);
            int score = sys2.GetDomainSkillScore(npc, "skill_equal_test");
            Object.DestroyImmediate(go2);

            Assert.AreEqual(12, score,
                "EqualWeight (WIS 14, INT 10) should yield (14 + 10) / 2 = 12.");
        }

        [Test]
        public void LegacySkill_GetDomainSkillScore_ReturnsZero()
        {
            var legacyGo       = new GameObject("LegacyTestReg");
            var legacyRegistry = legacyGo.AddComponent<ContentRegistry>();
            legacyRegistry.Skills["skill.farming"] = SkillDomainTestHelpers.MakeLegacySkill();
            var legacySys = new SkillSystem(legacyRegistry);

            var npc   = SkillDomainTestHelpers.MakeNpc(wis: 14);
            int score = legacySys.GetDomainSkillScore(npc, "skill.farming");
            Object.DestroyImmediate(legacyGo);

            Assert.AreEqual(0, score,
                "Legacy skills have no primary_stat; GetDomainSkillScore should return 0.");
        }

        [Test]
        public void IsDomainSkill_TrueWhenPrimaryStatSet()
        {
            var domainSkill = SkillDomainTestHelpers.MakeMedicalSkill();
            Assert.IsTrue(domainSkill.IsDomainSkill,
                "Skills with a non-empty primary_stat should be identified as domain skills.");
        }

        [Test]
        public void IsDomainSkill_FalseForLegacySkill()
        {
            var legacySkill = SkillDomainTestHelpers.MakeLegacySkill();
            Assert.IsFalse(legacySkill.IsDomainSkill,
                "Legacy skills without primary_stat should not be identified as domain skills.");
        }
    }

    // ── Perception derived stat tests ─────────────────────────────────────────

    [TestFixture]
    public class PerceptionDerivedStatTests
    {
        [Test]
        public void Perception_WIS14_INT12_CHA8_Returns19()
        {
            // Acceptance criterion: WIS 14, INT 12, CHA 8 → 14 + (12+8)/4 = 14 + 5 = 19
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 14, inte: 12, cha: 8);
            int score = SkillSystem.GetPerceptionScore(npc);
            Assert.AreEqual(19, score,
                "WIS 14, INT 12, CHA 8 → Perception = 14 + (12+8)/4 = 19.");
        }

        [Test]
        public void Perception_AllMinStats_Returns12()
        {
            // WIS 8, INT 8, CHA 8 → 8 + (8+8)/4 = 8 + 4 = 12
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 8, inte: 8, cha: 8);
            int score = SkillSystem.GetPerceptionScore(npc);
            Assert.AreEqual(12, score,
                "WIS 8, INT 8, CHA 8 → Perception = 8 + (8+8)/4 = 12.");
        }

        [Test]
        public void Perception_AllMaxStats_Returns30()
        {
            // WIS 20, INT 20, CHA 20 → 20 + (20+20)/4 = 20 + 10 = 30
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 20, inte: 20, cha: 20);
            int score = SkillSystem.GetPerceptionScore(npc);
            Assert.AreEqual(30, score,
                "WIS 20, INT 20, CHA 20 → Perception = 20 + (20+20)/4 = 30.");
        }

        [Test]
        public void Perception_IntegerDivision_RoundsDown()
        {
            // WIS 10, INT 11, CHA 8 → 10 + (11+8)/4 = 10 + 4 (floor of 4.75) = 14
            var npc = SkillDomainTestHelpers.MakeNpc(wis: 10, inte: 11, cha: 8);
            int score = SkillSystem.GetPerceptionScore(npc);
            Assert.AreEqual(14, score,
                "Perception uses integer division; (11+8)/4 = 4 (floors 4.75).");
        }
    }

    // ── Perception contested check tests ──────────────────────────────────────

    [TestFixture]
    public class PerceptionContestedCheckTests
    {
        [Test]
        public void ContestedCheck_PerceptionGreaterThanRoll_ReturnsTrue()
        {
            // Acceptance: Perception 18 vs opposing roll 15 → detects
            bool detected = SkillSystem.PerceptionContestedCheck(18, 15);
            Assert.IsTrue(detected,
                "Perception (18) > opposing roll (15) should detect.");
        }

        [Test]
        public void ContestedCheck_PerceptionLessThanRoll_ReturnsFalse()
        {
            // Acceptance: Perception 18 vs opposing roll 22 → not detected
            bool detected = SkillSystem.PerceptionContestedCheck(18, 22);
            Assert.IsFalse(detected,
                "Perception (18) < opposing roll (22) should not detect.");
        }

        [Test]
        public void ContestedCheck_PerceptionEqualToRoll_ReturnsFalse()
        {
            // Tied result: NPC does not detect (binary, no partial detection)
            bool detected = SkillSystem.PerceptionContestedCheck(15, 15);
            Assert.IsFalse(detected,
                "Perception equal to opposing roll should not detect (no tie-break in favour of detector).");
        }

        [Test]
        public void ContestedCheck_Perception0_AlwaysReturnsFalse()
        {
            bool detected = SkillSystem.PerceptionContestedCheck(0, 0);
            Assert.IsFalse(detected,
                "Perception 0 vs roll 0 should not detect.");
        }
    }

    // ── Proficiency slot count tests ──────────────────────────────────────────

    [TestFixture]
    public class ProficiencySlotCountTests
    {
        [Test]
        public void ProficiencySlots_INT14_Returns3()
        {
            // INT 14 → modifier +1 (code: 12-14 → +1) → 2 + 1 = 3 slots
            var npc = SkillDomainTestHelpers.MakeNpc(inte: 14);
            int slots = SkillSystem.GetProficiencySlotCount(npc);
            Assert.AreEqual(3, slots,
                "INT 14 → modifier +1 → 2 + 1 = 3 proficiency slots.");
        }

        [Test]
        public void ProficiencySlots_INT16_Returns4()
        {
            // INT 16 → modifier +2 (from code: 15-17 → +2)
            var npc = SkillDomainTestHelpers.MakeNpc(inte: 16);
            int slots = SkillSystem.GetProficiencySlotCount(npc);
            Assert.AreEqual(4, slots,
                "INT 16 → modifier +2 → 2 + 2 = 4 proficiency slots.");
        }

        [Test]
        public void ProficiencySlots_INT8_Returns2()
        {
            // INT 8 → modifier 0 → 2 + 0 = 2 base slots
            var npc = SkillDomainTestHelpers.MakeNpc(inte: 8);
            int slots = SkillSystem.GetProficiencySlotCount(npc);
            Assert.AreEqual(2, slots,
                "INT 8 → modifier 0 → 2 base proficiency slots.");
        }

        [Test]
        public void ProficiencySlots_INT5_Returns1()
        {
            // INT 5 → modifier -1 (from code: 5-7 → -1) → 2 + (-1) = 1
            var npc = SkillDomainTestHelpers.MakeNpc(inte: 5);
            int slots = SkillSystem.GetProficiencySlotCount(npc);
            Assert.AreEqual(1, slots,
                "INT 5 → modifier -1 → 2 - 1 = 1 proficiency slot.");
        }

        [Test]
        public void IsSkillProficient_ReturnsTrue_WhenSkillInProficiencyList()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds.Add("skill_medical");
            Assert.IsTrue(SkillSystem.IsSkillProficient(npc, "skill_medical"),
                "NPC should be proficient in skill_medical after adding to proficiencySkillIds.");
        }

        [Test]
        public void IsSkillProficient_ReturnsFalse_WhenSkillNotInList()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            Assert.IsFalse(SkillSystem.IsSkillProficient(npc, "skill_medical"),
                "NPC should not be proficient when skill is absent from proficiencySkillIds.");
        }

        [Test]
        public void IsSkillProficient_NullList_ReturnsFalse()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds = null;
            Assert.IsFalse(SkillSystem.IsSkillProficient(npc, "skill_medical"),
                "Null proficiencySkillIds should return false without throwing.");
        }
    }

    // ── Proficiency XP and level cap tests ───────────────────────────────────

    [TestFixture]
    public class ProficiencyXPTests
    {
        private GameObject      _go;
        private ContentRegistry _registry;
        private SkillSystem     _skillSystem;
        private StationState    _station;
        private bool            _originalFlag;

        [SetUp]
        public void SetUp()
        {
            _originalFlag = FeatureFlags.UseNewSkillSystem;
            FeatureFlags.UseNewSkillSystem = true;

            (_registry, _go) = SkillDomainTestHelpers.MakeRegistryWithDomainSkill(
                SkillDomainTestHelpers.MakeMedicalSkill());
            _skillSystem = new SkillSystem(_registry);
            _station     = SkillDomainTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.UseNewSkillSystem = _originalFlag;
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void NonProficient_XPAccruesAtHalfRate()
        {
            var npc  = SkillDomainTestHelpers.MakeNpc();
            // NPC is not proficient (proficiencySkillIds is empty by default)
            _skillSystem.AwardSkillXP(npc, "skill_medical", 100f, _station);

            var inst = SkillSystem.GetSkillInstance(npc, "skill_medical");
            Assert.IsNotNull(inst, "SkillInstance should have been created.");
            Assert.AreEqual(50f, inst.currentXP, 0.001f,
                "Non-proficient NPC should receive XP at 50% rate (100 * 0.5 = 50).");
        }

        [Test]
        public void Proficient_XPAccruesAtFullRate()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds.Add("skill_medical");

            _skillSystem.AwardSkillXP(npc, "skill_medical", 100f, _station);

            var inst = SkillSystem.GetSkillInstance(npc, "skill_medical");
            Assert.IsNotNull(inst);
            Assert.AreEqual(100f, inst.currentXP, 0.001f,
                "Proficient NPC should receive XP at the full rate (100).");
        }

        [Test]
        public void NonProficient_XPStopsAtLevelCap()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            // Set skill to exactly the cap level
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical",
                SkillSystem.NonProficientLevelCap);

            float xpAtCap = SkillSystem.GetSkillInstance(npc, "skill_medical")?.currentXP ?? 0f;

            // Attempt to award more XP — should be blocked
            _skillSystem.AwardSkillXP(npc, "skill_medical", 500f, _station);

            var inst = SkillSystem.GetSkillInstance(npc, "skill_medical");
            Assert.AreEqual(xpAtCap, inst.currentXP, 0.001f,
                "Non-proficient NPC at level cap should not receive additional XP.");
        }

        [Test]
        public void Proficient_LevelNotCappedAt6()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds.Add("skill_medical");
            // Set to level cap and add more XP — proficient NPC should not be capped
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical",
                SkillSystem.NonProficientLevelCap);

            _skillSystem.AwardSkillXP(npc, "skill_medical", 5000f, _station);

            var inst = SkillSystem.GetSkillInstance(npc, "skill_medical");
            Assert.Greater(inst.Level, SkillSystem.NonProficientLevelCap,
                "Proficient NPC should be able to exceed the non-proficient level cap of 6.");
        }

        [Test]
        public void NonProficient_LargeXPAward_ClampsAtCapLevel()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            // NPC at level 5 (below cap of 6) — large award should not jump past level 6
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 5);

            // Award enough that, even at 50%, the raw amount would push past level 7
            _skillSystem.AwardSkillXP(npc, "skill_medical", 10000f, _station);

            var inst = SkillSystem.GetSkillInstance(npc, "skill_medical");
            float xpCapForLevel6 = SkillSystem.GetXPForLevel(SkillSystem.NonProficientLevelCap);
            Assert.LessOrEqual(inst.currentXP, xpCapForLevel6,
                "Non-proficient NPC's XP should be clamped to the level 6 threshold even after a large award.");
            Assert.AreEqual(6, inst.Level,
                "Non-proficient NPC should be exactly at level 6 after a large XP award.");
        }

        [Test]
        public void UseNewSkillSystem_False_DomainSkillsIgnoredEntirely()
        {
            FeatureFlags.UseNewSkillSystem = false;
            var npc = SkillDomainTestHelpers.MakeNpc();

            // When the flag is off, domain skills should be completely silenced —
            // no XP should accrue regardless of proficiency.
            _skillSystem.AwardSkillXP(npc, "skill_medical", 100f, _station);

            var inst = SkillSystem.GetSkillInstance(npc, "skill_medical");
            // GetOrCreate creates the instance but XP should remain 0
            float xp = inst?.currentXP ?? 0f;
            Assert.AreEqual(0f, xp, 0.001f,
                "With UseNewSkillSystem = false, domain skills should receive no XP (schema isolation).");
        }
    }

    // ── Expertise slot unlock prompt tests ────────────────────────────────────

    [TestFixture]
    public class ExpertiseSlotUnlockTests
    {
        private GameObject      _go;
        private ContentRegistry _registry;
        private SkillSystem     _skillSystem;
        private StationState    _station;
        private bool            _originalFlag;

        [SetUp]
        public void SetUp()
        {
            _originalFlag = FeatureFlags.UseNewSkillSystem;
            FeatureFlags.UseNewSkillSystem = true;

            (_registry, _go) = SkillDomainTestHelpers.MakeRegistryWithDomainSkill(
                SkillDomainTestHelpers.MakeMedicalSkill());
            _skillSystem = new SkillSystem(_registry);
            _station     = SkillDomainTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.UseNewSkillSystem = _originalFlag;
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void SlotUnlockPrompt_FiresAtLevel4()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds.Add("skill_medical");
            // Start at level 3
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 3);

            bool slotEarned = false;
            _skillSystem.OnSlotEarned += (_, skillId, lvl) =>
            {
                if (skillId == "skill_medical" && lvl == 4) slotEarned = true;
            };

            // Award enough XP to cross level 4 (400 XP for level 4, currently at 900 for lvl 3)
            // Level 3 XP = 900; Level 4 XP = 1600; need 700 more
            _skillSystem.AwardSkillXP(npc, "skill_medical", 800f, _station);

            Assert.IsTrue(slotEarned,
                "OnSlotEarned should fire when skill level crosses 4.");
        }

        [Test]
        public void SlotUnlockPrompt_FiresAtLevel8()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds.Add("skill_medical");
            // Start at level 7
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 7);

            bool slotEarned = false;
            _skillSystem.OnSlotEarned += (_, skillId, lvl) =>
            {
                if (skillId == "skill_medical" && lvl == 8) slotEarned = true;
            };

            // Level 7 XP = 4900; Level 8 XP = 6400; need ≥1500 applied XP.
            // Daily soft cap: first 500 at 100%, remainder at 70%.
            // Awarding 2500: 500 + (2000 * 0.70) = 500 + 1400 = 1900 applied → 4900+1900=6800 > 6400 ✓
            _skillSystem.AwardSkillXP(npc, "skill_medical", 2500f, _station);

            Assert.IsTrue(slotEarned,
                "OnSlotEarned should fire when skill level crosses 8.");
        }

        [Test]
        public void PendingExpertiseSkillIds_PopulatedOnSlotUnlock()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            npc.proficiencySkillIds.Add("skill_medical");
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 3);

            _skillSystem.AwardSkillXP(npc, "skill_medical", 800f, _station);

            Assert.IsNotNull(npc.pendingExpertiseSkillIds);
            Assert.Contains("skill_medical", npc.pendingExpertiseSkillIds,
                "skill_medical should be in pendingExpertiseSkillIds after slot unlock at level 4.");
        }
    }

    // ── ChooseDomainExpertise tests ───────────────────────────────────────────

    [TestFixture]
    public class ChooseDomainExpertiseTests
    {
        private GameObject      _go;
        private ContentRegistry _registry;
        private SkillSystem     _skillSystem;
        private StationState    _station;
        private bool            _originalFlag;

        [SetUp]
        public void SetUp()
        {
            _originalFlag = FeatureFlags.UseNewSkillSystem;
            FeatureFlags.UseNewSkillSystem = true;

            (_registry, _go) = SkillDomainTestHelpers.MakeRegistryWithDomainSkill(
                SkillDomainTestHelpers.MakeMedicalSkill());
            _skillSystem = new SkillSystem(_registry);
            _station     = SkillDomainTestHelpers.MakeStation();

            // Clear any previously registered capabilities to keep tests isolated
            TaskEligibilityResolver.ClearCapabilities();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.UseNewSkillSystem = _originalFlag;
            TaskEligibilityResolver.ClearCapabilities();
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void ChooseDomainExpertise_AddsOptionIdToChosenExpertise()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);

            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                "exp_diagnosis", _station);

            Assert.IsTrue(ok, $"ChooseDomainExpertise should succeed. Msg: {msg}");
            Assert.Contains("exp_diagnosis", npc.chosenExpertise,
                "exp_diagnosis should be in chosenExpertise after being selected.");
        }

        [Test]
        public void ChooseDomainExpertise_RegistersSoftCapabilityForTaskTag()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);

            _skillSystem.ChooseDomainExpertise(npc, "skill_medical", "exp_diagnosis", _station);

            Assert.IsTrue(TaskEligibilityResolver.IsSoftLocked("diagnosis"),
                "'diagnosis' task tag should be registered as a soft-locked capability after choosing exp_diagnosis.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_UnknownSkill()
        {
            var npc   = SkillDomainTestHelpers.MakeNpc();
            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill_unknown",
                                                                "exp_diagnosis", _station);
            Assert.IsFalse(ok, "Should fail for an unknown skill ID.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_UnknownOption()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);
            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                "exp_unknown_option", _station);
            Assert.IsFalse(ok, "Should fail for an unknown expertise option ID.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_InsufficientLevel()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            // skill_medical at level 3, but exp_surgery requires level 8
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 3);
            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                "exp_surgery", _station);
            Assert.IsFalse(ok, "exp_surgery requires level 8; should fail at level 3.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_AlreadyChosen()
        {
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);
            _skillSystem.ChooseDomainExpertise(npc, "skill_medical", "exp_diagnosis", _station);

            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                "exp_diagnosis", _station);
            Assert.IsFalse(ok, "Choosing the same option twice should fail.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_WhenFlagOff()
        {
            FeatureFlags.UseNewSkillSystem = false;
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);

            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                "exp_diagnosis", _station);
            Assert.IsFalse(ok, "ChooseDomainExpertise should fail when UseNewSkillSystem is off.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_LegacySkill()
        {
            // Register a legacy (non-domain) skill and try to choose a domain option for it
            _registry.Skills["skill.farming"] = SkillDomainTestHelpers.MakeLegacySkill();
            var npc = SkillDomainTestHelpers.MakeNpc();

            var (ok, msg) = _skillSystem.ChooseDomainExpertise(npc, "skill.farming",
                                                                "exp_any", _station);
            Assert.IsFalse(ok, "ChooseDomainExpertise should fail for legacy (non-domain) skills.");
        }

        [Test]
        public void ChooseDomainExpertise_Fails_DuplicateSlotLevel()
        {
            // NPC at level 4, chooses exp_diagnosis from the level-4 slot.
            // Attempting to choose another level-4 option (exp_treatment) should be rejected.
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);
            var (ok1, _) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                               "exp_diagnosis", _station);
            Assert.IsTrue(ok1, "First level-4 choice should succeed.");

            var (ok2, msg2) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                   "exp_treatment", _station);
            Assert.IsFalse(ok2,
                "Choosing a second option from the same level-4 slot should fail. Msg: " + msg2);
        }

        [Test]
        public void ChooseDomainExpertise_Fails_NoUnspentSlots()
        {
            // NPC at level 4 in skill_medical: has 1 slot earned (floor(4/4) = 1).
            // Fill the only available slot, then try to claim a second slot's option.
            // The level-8 slot option requires level 8, so use the level-4 slot first,
            // then advance to 8 and check the unspent slot count.
            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 4);
            // Claim the level-4 slot (uses the 1 available slot)
            var (ok1, _) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                               "exp_diagnosis", _station);
            Assert.IsTrue(ok1);

            // Advance to level 8 — now 2 slots earned but only 1 was unspent after
            // the first choice. Floor(8/4)=2 earned, 1 used = 1 unspent.
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 8);
            var (ok8, _) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                               "exp_surgery", _station);
            Assert.IsTrue(ok8, "Level-8 option should succeed with 1 unspent slot remaining.");

            // Now unspent = 0. Any further claim should be rejected.
            // Reset skill to 8 so level check passes; the slot is full.
            var (ok_extra, msg_extra) = _skillSystem.ChooseDomainExpertise(npc, "skill_medical",
                                                                            "exp_prosthetics", _station);
            Assert.IsFalse(ok_extra,
                "Should fail when all expertise slots are used. Msg: " + msg_extra);
        }

        [Test]
        public void TaskEligibilityResolver_SurgeryTask_SoftGatedWithoutExpertise()
        {
            // Register exp_surgery as a soft capability for "surgery" tasks
            _skillSystem.RegisterAllCapabilities();

            var npc = SkillDomainTestHelpers.MakeNpc();
            // NPC has no expertise

            float penalty = TaskEligibilityResolver.GetPerformancePenalty(npc, "surgery");
            Assert.Less(penalty, 1.0f,
                "Without exp_surgery, 'surgery' task should have a performance penalty (soft-gated).");
        }

        [Test]
        public void TaskEligibilityResolver_SurgeryTask_NoPenaltyWithExpertise()
        {
            _skillSystem.RegisterAllCapabilities();

            var npc = SkillDomainTestHelpers.MakeNpc();
            SkillDomainTestHelpers.SetSkillLevel(npc, "skill_medical", 8);
            // Claim surgery expertise
            _skillSystem.ChooseDomainExpertise(npc, "skill_medical", "exp_surgery", _station);

            float penalty = TaskEligibilityResolver.GetPerformancePenalty(npc, "surgery");
            Assert.AreEqual(1.0f, penalty, 0.001f,
                "With exp_surgery chosen, 'surgery' task should have no performance penalty.");
        }
    }

    // ── SkillDefinition.FromDict domain-schema parsing tests ──────────────────

    [TestFixture]
    public class SkillDefinitionDomainParsingTests
    {
        [Test]
        public void FromDict_ParsesPrimaryStatAndSecondaryStat()
        {
            var raw = new Dictionary<string, object>
            {
                { "id",            "skill_medical" },
                { "name",          "Medical" },
                { "primary_stat",  "WIS" },
                { "secondary_stat","INT" },
                { "weight",        "primary_dominant" },
                { "proficiency_required_for_max_level", true },
                { "associated_task_types", new List<object>() },
                { "composite_terms",       new List<object>() },
                { "expertise_slots",       new List<object>() },
            };

            var skill = SkillDefinition.FromDict(raw);

            Assert.AreEqual("skill_medical", skill.skillId);
            Assert.AreEqual("Medical",       skill.displayName);
            Assert.AreEqual("WIS",           skill.primaryStat);
            Assert.AreEqual("INT",           skill.secondaryStat);
            Assert.AreEqual(SkillWeight.PrimaryDominant, skill.weight);
            Assert.IsTrue(skill.proficiencyRequiredForMaxLevel);
            Assert.IsTrue(skill.IsDomainSkill);
        }

        [Test]
        public void FromDict_ParsesEqualWeightVariant()
        {
            var raw = new Dictionary<string, object>
            {
                { "id",            "skill_equal" },
                { "primary_stat",  "INT" },
                { "secondary_stat","WIS" },
                { "weight",        "equal_weight" },
                { "associated_task_types", new List<object>() },
                { "composite_terms",       new List<object>() },
                { "expertise_slots",       new List<object>() },
            };

            var skill = SkillDefinition.FromDict(raw);
            Assert.AreEqual(SkillWeight.EqualWeight, skill.weight);
        }

        [Test]
        public void FromDict_ParsesEmbeddedExpertiseSlots()
        {
            var optionRaw = new Dictionary<string, object>
            {
                { "id",                "exp_surgery" },
                { "name",              "Surgery" },
                { "description",       "Unlocks surgical procedures." },
                { "task_tags_unlocked", new List<object> { "surgery" } },
            };
            var slotRaw = new Dictionary<string, object>
            {
                { "unlock_level", 8 },
                { "options", new List<object> { optionRaw } },
            };
            var raw = new Dictionary<string, object>
            {
                { "id",            "skill_medical" },
                { "primary_stat",  "WIS" },
                { "secondary_stat","INT" },
                { "associated_task_types", new List<object>() },
                { "composite_terms",       new List<object>() },
                { "expertise_slots", new List<object> { slotRaw } },
            };

            var skill = SkillDefinition.FromDict(raw);

            Assert.AreEqual(1, skill.domainExpertiseSlots.Count,
                "One expertise slot should be parsed.");
            Assert.AreEqual(8, skill.domainExpertiseSlots[0].unlockLevel);
            Assert.AreEqual(1, skill.domainExpertiseSlots[0].options.Count);
            Assert.AreEqual("exp_surgery", skill.domainExpertiseSlots[0].options[0].id);
            Assert.Contains("surgery",
                skill.domainExpertiseSlots[0].options[0].taskTagsUnlocked,
                "'surgery' should be in task_tags_unlocked for exp_surgery.");
        }

        [Test]
        public void FromDict_LegacySkill_PrimaryStatEmpty_NotDomainSkill()
        {
            var raw = new Dictionary<string, object>
            {
                { "id",                    "skill.farming" },
                { "display_name",          "Farming" },
                { "skill_type",            "Simple" },
                { "governing_ability",     "WIS" },
                { "associated_task_types", new List<object> { "sow", "harvest" } },
                { "composite_terms",       new List<object>() },
                { "expertise_slots",       new List<object>() },
            };

            var skill = SkillDefinition.FromDict(raw);
            Assert.IsFalse(skill.IsDomainSkill,
                "Legacy skills with no primary_stat should not be domain skills.");
            Assert.AreEqual("", skill.primaryStat);
        }
    }
}
