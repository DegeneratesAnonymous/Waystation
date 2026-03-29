// MedicalSystemTests.cs — EditMode unit tests for NPC-006:
//   • Fortitude trait pain reduction in PainSystem.Derive()
//   • GetTreeForSpecies() registry dispatch (human, undefined species, registered species)
//   • Integration: full pain derivation pipeline with Fortitude under wound conditions

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static class MedicalTestHelpers
    {
        /// <summary>
        /// Creates and initialises a MedicalTickSystem ready for testing.
        /// </summary>
        public static MedicalTickSystem MakeSystem()
        {
            var sys = new MedicalTickSystem();
            sys.Initialise();
            return sys;
        }

        /// <summary>
        /// Creates an NPC with a medical profile, a single wound on the torso,
        /// and no bleed so blood volume stays at 100%.
        /// Returns the NPC; the station is also populated with it.
        /// </summary>
        public static NPCInstance MakeNpcWithWound(StationState station, float painContrib,
                                                    bool fortitude = false)
        {
            var npc = new NPCInstance
            {
                uid  = $"npc_{System.Guid.NewGuid():N}",
                name = "TestNPC",
            };
            npc.statusTags.Add("crew");
            station.npcs[npc.uid] = npc;

            // Give the NPC a medical profile via a fresh system so the profile is correctly
            // initialised from the human tree.
            var sys = MakeSystem();
            sys.EnsureProfile(npc);

            // Add a single wound to the torso with no bleed.
            var part = npc.medicalProfile.GetPart("torso");
            Assert.IsNotNull(part, "torso part must exist in the human body tree");
            var wound = Wound.Create(WoundType.Blunt, WoundSeverity.Minor,
                                     bleedRate: 0f, painContrib: painContrib, currentTick: 0);
            // Mark as treated: slower infection accumulation with a higher DC.
            // Infection rolls are also avoided because MakeStation() sets tick=1,
            // which is not a multiple of InfectionRollInterval (12).
            // bleedRate=0 keeps blood volume constant throughout the test.
            wound.isTreated = true;
            part.wounds.Add(wound);

            if (fortitude)
            {
                npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait
                {
                    traitId        = "trait.fortitude",
                    strength       = 1f,
                    acquisitionTick = 0,
                });
            }

            return npc;
        }

        /// <summary>Creates a minimal StationState at tick=1 (avoids infection roll at tick%12==0).</summary>
        public static StationState MakeStation()
        {
            return new StationState("TestStation") { tick = 1 };
        }
    }

    // ── Unit: Fortitude trait ─────────────────────────────────────────────────

    [TestFixture]
    public class PainSystemFortitudeTests
    {
        /// <summary>
        /// Given NPC has the Fortitude trait, derived pain must be exactly 10% lower.
        /// </summary>
        [Test]
        public void Derive_WithFortitude_ReducesPainByTenPercent()
        {
            float painContrib = 40f;
            var station = MedicalTestHelpers.MakeStation();
            var npc     = MedicalTestHelpers.MakeNpcWithWound(station, painContrib, fortitude: true);
            var sys     = MedicalTestHelpers.MakeSystem();

            sys.Tick(station);

            // With default ENDMod=0 and no analgesic, only Fortitude reduction applies: 40 * 0.9 = 36
            Assert.AreEqual(painContrib * 0.9f, npc.medicalProfile.pain, 0.01f,
                "Fortitude trait should reduce pain by exactly 10%.");
        }

        /// <summary>
        /// Given NPC does NOT have the Fortitude trait, no reduction is applied.
        /// </summary>
        [Test]
        public void Derive_WithoutFortitude_DoesNotReducePain()
        {
            float painContrib = 40f;
            var station = MedicalTestHelpers.MakeStation();
            var npc     = MedicalTestHelpers.MakeNpcWithWound(station, painContrib, fortitude: false);
            var sys     = MedicalTestHelpers.MakeSystem();

            sys.Tick(station);

            // With default ENDMod=0 and no analgesic, raw pain is unchanged.
            Assert.AreEqual(painContrib, npc.medicalProfile.pain, 0.01f,
                "Absent Fortitude trait should not alter derived pain.");
        }

        /// <summary>
        /// Integration: multiple wounds, NPC has Fortitude — combined pain reduced by 10%.
        /// </summary>
        [Test]
        public void Derive_WithFortitude_MultipleWounds_ReducesCombinedPain()
        {
            float painContrib = 20f;
            var station = MedicalTestHelpers.MakeStation();
            var npc     = MedicalTestHelpers.MakeNpcWithWound(station, painContrib, fortitude: true);
            var sys     = MedicalTestHelpers.MakeSystem();

            // Add a second wound (also bleed-free) to a different part.
            var part2 = npc.medicalProfile.GetPart("left_arm");
            Assert.IsNotNull(part2, "left_arm part must exist in the human body tree");
            var wound2 = Wound.Create(WoundType.Laceration, WoundSeverity.Minor,
                                      bleedRate: 0f, painContrib: painContrib, currentTick: 0);
            wound2.isTreated = true;
            part2.wounds.Add(wound2);

            sys.Tick(station);

            float expectedPain = (painContrib + painContrib) * 0.9f;
            Assert.AreEqual(expectedPain, npc.medicalProfile.pain, 0.01f,
                "Fortitude should reduce total pain from all wounds by 10%.");
        }
    }

    // ── Unit: GetTreeForSpecies via EnsureProfile ─────────────────────────────

    [TestFixture]
    public class GetTreeForSpeciesTests
    {
        /// <summary>
        /// Given species "human", EnsureProfile must create a profile whose speciesId is "human".
        /// </summary>
        [Test]
        public void EnsureProfile_HumanSpecies_ReturnsHumanTree()
        {
            var sys = MedicalTestHelpers.MakeSystem();
            var npc = new NPCInstance { uid = "test_human", species = "human" };

            sys.EnsureProfile(npc);

            Assert.IsNotNull(npc.medicalProfile, "medicalProfile must be created");
            Assert.AreEqual("human", npc.medicalProfile.speciesId,
                "Profile created for a human NPC must use the human body tree.");
        }

        /// <summary>
        /// Given an undefined species, EnsureProfile falls back to the human tree
        /// and logs a warning. The resulting profile must still be usable.
        /// </summary>
        [Test]
        public void EnsureProfile_UndefinedSpecies_FallsBackToHumanTree()
        {
            var sys = MedicalTestHelpers.MakeSystem();
            var npc = new NPCInstance { uid = "test_alien", species = "alien_undefined" };

            // Expect a warning but no exception.
            LogAssert.Expect(LogType.Warning,
                "[MedicalTickSystem] No body tree registered for species 'alien_undefined'; falling back to human tree.");

            sys.EnsureProfile(npc);

            Assert.IsNotNull(npc.medicalProfile,
                "A fallback profile must be created even for undefined species.");
            Assert.AreEqual("human", npc.medicalProfile.speciesId,
                "Fallback for undefined species must use the human body tree.");
        }

        /// <summary>
        /// Given a tree registered for a new species via RegisterSpeciesTree(),
        /// EnsureProfile returns that tree (not the human tree).
        /// </summary>
        [Test]
        public void EnsureProfile_RegisteredSpecies_ReturnsCorrectTree()
        {
            var sys = MedicalTestHelpers.MakeSystem();

            // Build a minimal stub tree for a fictional species.
            var stubTree = new BodyPartTreeDefinition { speciesId = "stub_species" };
            stubTree.parts.Add(new BodyPartDefinition
            {
                partId      = "core",
                displayName = "Core",
                vitalRule   = VitalRule.InstantDeath,
                healthWeight = 10f,
            });
            sys.RegisterSpeciesTree("stub_species", stubTree);

            var npc = new NPCInstance { uid = "test_stub", species = "stub_species" };
            sys.EnsureProfile(npc);

            Assert.IsNotNull(npc.medicalProfile, "medicalProfile must be created");
            Assert.AreEqual("stub_species", npc.medicalProfile.speciesId,
                "RegisterSpeciesTree should make GetTreeForSpecies() return the registered tree.");
            Assert.IsTrue(npc.medicalProfile.parts.ContainsKey("core"),
                "The registered tree's parts must appear in the resulting profile.");
        }

        /// <summary>
        /// RegisterSpeciesTree with null tree or empty id is a no-op and does not throw.
        /// </summary>
        [Test]
        public void RegisterSpeciesTree_NullOrEmpty_IsNoOp()
        {
            var sys = MedicalTestHelpers.MakeSystem();
            Assert.DoesNotThrow(() => sys.RegisterSpeciesTree(null, null));
            Assert.DoesNotThrow(() => sys.RegisterSpeciesTree("", new BodyPartTreeDefinition()));
            Assert.DoesNotThrow(() => sys.RegisterSpeciesTree("valid_id", null));
        }

        /// <summary>
        /// RegisterSpeciesTree normalizes tree.speciesId to match the registry key when they differ.
        /// </summary>
        [Test]
        public void RegisterSpeciesTree_MismatchedSpeciesId_NormalizesToKey()
        {
            var sys = MedicalTestHelpers.MakeSystem();
            var tree = new BodyPartTreeDefinition { speciesId = "wrong_id" };

            LogAssert.Expect(LogType.Warning,
                "[MedicalTickSystem] RegisterSpeciesTree species mismatch: key='correct_id' tree.speciesId='wrong_id'. Normalizing to key.");

            sys.RegisterSpeciesTree("correct_id", tree);

            Assert.AreEqual("correct_id", tree.speciesId,
                "tree.speciesId should be normalized to the registry key after registration.");
        }

        /// <summary>
        /// RegisterSpeciesTree sets tree.speciesId when the tree's speciesId is empty.
        /// </summary>
        [Test]
        public void RegisterSpeciesTree_EmptyTreeSpeciesId_PopulatesFromKey()
        {
            var sys = MedicalTestHelpers.MakeSystem();
            var tree = new BodyPartTreeDefinition { speciesId = "" };

            sys.RegisterSpeciesTree("my_species", tree);

            Assert.AreEqual("my_species", tree.speciesId,
                "tree.speciesId should be set from the registry key when it is empty.");
        }
    }
}
