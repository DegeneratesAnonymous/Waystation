// CounsellingSystemTests — EditMode unit tests for NPC-003 counselling system.
//
// Validates:
//   • MapRollToOutcome() returns the correct tier for all five threshold boundaries
//   • GetAffinityModifier() returns clamped modifier at zero, positive, negative, and extreme affinity
//   • RegisterIntervention() sets requiresIntervention = true, halting the breakdown drain
//   • RegisterIntervention() on null sanity profile does not throw
//   • SanitySystem breakdown drain stops when requiresIntervention == true
//   • SanitySystem breakdown drain continues when requiresIntervention == false
//   • CounsellingSystem.Tick() assigns a counselling task to an idle counsellor when breakdown NPC present
//   • CounsellingSystem.Tick() does not assign when no breakdown NPC present
//   • CounsellingSystem.Tick() does not assign when counsellor is in crisis
//   • CounsellingSystem.Tick() respects per-patient failure cooldown
//   • CounsellingSystem feature flag = false prevents all task assignment
//   • OnSessionComplete event fires with correct arguments on full session resolution
//   • TraitSystem.OnCounsellingComplete() removes therapy-removable traits after counselling
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class CounsellingTestHelpers
    {
        public static StationState MakeStation(int tick = 0)
        {
            var s = new StationState("CounsellingTestStation");
            s.tick = tick;
            return s;
        }

        /// <summary>Creates a crew NPC tagged as a counsellor.</summary>
        public static NPCInstance MakeCounsellorNpc(string uid = null,
                                                    int wisScore = 14,
                                                    int chaScore = 14,
                                                    int persuasionLevel = 3,
                                                    int socialLevel = 2)
        {
            var npc = new NPCInstance
            {
                uid    = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name   = "Counsellor",
                classId = CounsellingSystem.CounsellorClassId,
            };
            npc.statusTags.Add("crew");
            npc.abilityScores.WIS = wisScore;
            npc.abilityScores.CHA = chaScore;
            SetSkillLevel(npc, CounsellingSystem.PersuasionSkillId, persuasionLevel);
            SetSkillLevel(npc, CounsellingSystem.CommunicationSkillId, socialLevel);
            return npc;
        }

        /// <summary>Creates a crew NPC in breakdown.</summary>
        public static NPCInstance MakeBreakdownNpc(string uid = null)
        {
            var npc = new NPCInstance
            {
                uid  = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "BreakdownPatient",
            };
            npc.statusTags.Add("crew");
            var san = npc.GetOrCreateSanity();
            san.score          = -6;
            san.isInBreakdown  = true;
            return npc;
        }

        /// <summary>Sets the XP for an NPC skill to reach the given level.</summary>
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

        /// <summary>Creates a CounsellingSystem with short session duration for test speed.</summary>
        public static CounsellingSystem MakeCounsellingSystem()
        {
            var cs = new CounsellingSystem();
            // Use 1 tick sessions so tests don't have to run hundreds of Tick() calls.
            cs.SessionDurationTicks = 1;
            cs.CooldownTicksOnFailure    = 5;
            cs.CooldownTicksOnCritFailure = 10;
            return cs;
        }
    }

    // ── MapRollToOutcome tests ─────────────────────────────────────────────────

    [TestFixture]
    public class CounsellingRollMappingTests
    {
        [Test]
        public void Roll_AtCritSuccessThreshold_ReturnsCriticalSuccess()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.CritSuccessThreshold);
            Assert.AreEqual(CounsellingOutcome.CriticalSuccess, outcome);
        }

        [Test]
        public void Roll_AboveCritSuccessThreshold_ReturnsCriticalSuccess()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.CritSuccessThreshold + 5);
            Assert.AreEqual(CounsellingOutcome.CriticalSuccess, outcome);
        }

        [Test]
        public void Roll_AtSuccessThreshold_ReturnsSuccess()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.SuccessThreshold);
            Assert.AreEqual(CounsellingOutcome.Success, outcome);
        }

        [Test]
        public void Roll_JustBelowCritSuccess_ReturnsSuccess()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.CritSuccessThreshold - 1);
            Assert.AreEqual(CounsellingOutcome.Success, outcome);
        }

        [Test]
        public void Roll_AtPartialSuccessThreshold_ReturnsPartialSuccess()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.PartialSuccessThreshold);
            Assert.AreEqual(CounsellingOutcome.PartialSuccess, outcome);
        }

        [Test]
        public void Roll_AtFailureThreshold_ReturnsFailure()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.FailureThreshold);
            Assert.AreEqual(CounsellingOutcome.Failure, outcome);
        }

        [Test]
        public void Roll_BelowFailureThreshold_ReturnsCriticalFailure()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(CounsellingSystem.FailureThreshold - 1);
            Assert.AreEqual(CounsellingOutcome.CriticalFailure, outcome);
        }

        [Test]
        public void Roll_Zero_ReturnsCriticalFailure()
        {
            var outcome = CounsellingSystem.MapRollToOutcome(0);
            Assert.AreEqual(CounsellingOutcome.CriticalFailure, outcome);
        }
    }

    // ── Affinity modifier tests ───────────────────────────────────────────────

    [TestFixture]
    public class AffinityModifierTests
    {
        private StationState _station;
        private NPCInstance  _counsellor;
        private NPCInstance  _patient;

        [SetUp]
        public void SetUp()
        {
            _station    = CounsellingTestHelpers.MakeStation();
            _counsellor = CounsellingTestHelpers.MakeCounsellorNpc();
            _patient    = CounsellingTestHelpers.MakeBreakdownNpc();
            _station.AddNpc(_counsellor);
            _station.AddNpc(_patient);
        }

        [Test]
        public void NoRelationship_ReturnsZero()
        {
            int mod = CounsellingSystem.GetAffinityModifier(_counsellor, _patient, _station);
            Assert.AreEqual(0, mod);
        }

        [Test]
        public void PositiveAffinity40_ReturnsPositiveModifier()
        {
            var rec = RelationshipRegistry.GetOrCreate(_station, _counsellor.uid, _patient.uid);
            rec.affinityScore = 40f;

            int mod = CounsellingSystem.GetAffinityModifier(_counsellor, _patient, _station);
            Assert.Greater(mod, 0, "Positive affinity should yield a positive modifier.");
        }

        [Test]
        public void NegativeAffinity_ReturnsNegativeModifier()
        {
            var rec = RelationshipRegistry.GetOrCreate(_station, _counsellor.uid, _patient.uid);
            rec.affinityScore = -40f;

            int mod = CounsellingSystem.GetAffinityModifier(_counsellor, _patient, _station);
            Assert.Less(mod, 0, "Negative affinity should yield a negative modifier.");
        }

        [Test]
        public void ExtremeAffinity_ClampsAtThree()
        {
            var rec = RelationshipRegistry.GetOrCreate(_station, _counsellor.uid, _patient.uid);
            rec.affinityScore = 1000f;

            int mod = CounsellingSystem.GetAffinityModifier(_counsellor, _patient, _station);
            Assert.AreEqual(3, mod, "Affinity modifier should be capped at +3.");
        }

        [Test]
        public void ExtremeNegativeAffinity_ClampsAtMinusThree()
        {
            var rec = RelationshipRegistry.GetOrCreate(_station, _counsellor.uid, _patient.uid);
            rec.affinityScore = -1000f;

            int mod = CounsellingSystem.GetAffinityModifier(_counsellor, _patient, _station);
            Assert.AreEqual(-3, mod, "Affinity modifier should be capped at -3.");
        }
    }

    // ── RegisterIntervention tests ────────────────────────────────────────────

    [TestFixture]
    public class RegisterInterventionTests
    {
        [Test]
        public void RegisterIntervention_SetsRequiresIntervention_True()
        {
            var npc = CounsellingTestHelpers.MakeBreakdownNpc();
            var san = npc.GetOrCreateSanity();
            san.requiresIntervention = false;

            SanitySystem.RegisterIntervention(npc);

            Assert.IsTrue(san.requiresIntervention,
                "RegisterIntervention must set requiresIntervention = true to halt drain.");
        }

        [Test]
        public void RegisterIntervention_NullSanity_DoesNotThrow()
        {
            var npc = new NPCInstance { uid = "x", name = "X" };
            npc.statusTags.Add("crew");
            // sanity is null — must not throw
            Assert.DoesNotThrow(() => SanitySystem.RegisterIntervention(npc));
        }
    }

    // ── SanitySystem breakdown drain tests ───────────────────────────────────

    [TestFixture]
    public class SanityBreakdownDrainTests
    {
        private SanitySystem  _sanity;
        private StationState  _station;
        private NPCInstance   _npc;

        [SetUp]
        public void SetUp()
        {
            _sanity  = new SanitySystem();
            _station = CounsellingTestHelpers.MakeStation();
            _npc     = CounsellingTestHelpers.MakeBreakdownNpc();
            _station.AddNpc(_npc);
        }

        [Test]
        public void Breakdown_WithNoIntervention_DecreasesScoreEachDay()
        {
            var san = _npc.GetOrCreateSanity();
            san.requiresIntervention = false;
            san.score                = -6;
            san.isInBreakdown        = true;
            san.ceiling              = 0;

            // Tick on a daily boundary — tick 96.
            _station.tick = 96;
            _sanity.Tick(_station);

            Assert.Less(san.score, -6,
                "Score should decrease each day during breakdown with no intervention.");
        }

        [Test]
        public void Breakdown_AfterIntervention_ScoreDoesNotDrainFurther()
        {
            var san = _npc.GetOrCreateSanity();
            san.requiresIntervention = false;
            san.score                = -6;
            san.isInBreakdown        = true;
            san.ceiling              = 0;

            SanitySystem.RegisterIntervention(_npc);

            int scoreBefore = san.score;
            _station.tick = 96;
            _sanity.Tick(_station);

            // Score must NOT decrease below the pre-tick value (drain is halted by intervention).
            Assert.GreaterOrEqual(san.score, scoreBefore,
                "After intervention, breakdown drain should be halted (requiresIntervention=true).");
            // More precisely: ensure the drain path (isInBreakdown && !requiresIntervention) is blocked.
            Assert.IsTrue(san.requiresIntervention,
                "requiresIntervention should remain true after Tick when breakdown persists.");
        }
    }

    // ── Session assignment tests ──────────────────────────────────────────────

    [TestFixture]
    public class CounsellingSessionAssignmentTests
    {
        private CounsellingSystem _counselling;
        private StationState      _station;
        private NPCInstance       _counsellor;
        private NPCInstance       _patient;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.NpcCounselling = true;

            _counselling = CounsellingTestHelpers.MakeCounsellingSystem();
            _station     = CounsellingTestHelpers.MakeStation();
            _counsellor  = CounsellingTestHelpers.MakeCounsellorNpc();
            _patient     = CounsellingTestHelpers.MakeBreakdownNpc();

            _station.AddNpc(_counsellor);
            _station.AddNpc(_patient);
        }

        [Test]
        public void Tick_BreakdownNpcPresent_CounsellorAssignedCounsellingJob()
        {
            _counselling.Tick(_station);

            Assert.AreEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "Counsellor should be assigned job.counselling when a breakdown NPC is present.");
        }

        [Test]
        public void Tick_NoBreakdownNpc_CounsellorNotAssigned()
        {
            _patient.sanity.isInBreakdown = false;

            _counselling.Tick(_station);

            Assert.AreNotEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "Counsellor should not be assigned when no breakdown NPC is present.");
        }

        [Test]
        public void Tick_CounsellorInCrisis_NotAssigned()
        {
            _counsellor.inCrisis = true;

            _counselling.Tick(_station);

            Assert.AreNotEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "Counsellor in crisis must not be assigned a counselling session.");
        }

        [Test]
        public void Tick_SessionInterrupted_PatientReeligibleImmediately()
        {
            // Tick 1: assign session.
            _counselling.Tick(_station);
            Assert.AreEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "First tick should assign the counsellor.");

            // Simulate counsellor abandoning the job (e.g., crisis or hunger override).
            _counsellor.currentJobId = null;

            // No roll is made on interruption — patient gets no cooldown.
            // Tick 2: patient should be immediately eligible for a new session.
            _counselling.Tick(_station);
            Assert.AreEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "After session interruption (no roll), patient should be immediately re-assignable.");
        }

        [Test]
        public void Tick_PatientOnCooldown_BlocksReassignment()
        {
            // Inject a cooldown that expires in the far future.
            _counselling.InjectPatientCooldownForTest(_patient.uid, _station.tick + 100);

            _counselling.Tick(_station);

            Assert.AreNotEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "Counselling session must not start while the patient has an active failure cooldown.");
        }

        [Test]
        public void FeatureFlag_Disabled_PreventsCounsellorAssignment()
        {
            FeatureFlags.NpcCounselling = false;
            try
            {
                _counselling.Tick(_station);
                Assert.AreNotEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                    "Counselling job must not be assigned when NpcCounselling feature flag is off.");
            }
            finally
            {
                FeatureFlags.NpcCounselling = true;
            }
        }
    }

    // ── Session completion and event tests ────────────────────────────────────

    [TestFixture]
    public class CounsellingSessionCompletionTests
    {
        private CounsellingSystem _counselling;
        private StationState      _station;
        private NPCInstance       _counsellor;
        private NPCInstance       _patient;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.NpcCounselling = true;

            _counselling = CounsellingTestHelpers.MakeCounsellingSystem();
            _station     = CounsellingTestHelpers.MakeStation();
            _counsellor  = CounsellingTestHelpers.MakeCounsellorNpc();
            _patient     = CounsellingTestHelpers.MakeBreakdownNpc();

            _station.AddNpc(_counsellor);
            _station.AddNpc(_patient);
        }

        [Test]
        public void OnSessionComplete_EventFires_AfterSessionRuns()
        {
            bool eventFired = false;
            _counselling.OnSessionComplete += (c, p, outcome, roll) => { eventFired = true; };

            // Tick 1: assign session (duration = 1 tick).
            _counselling.Tick(_station);
            // Tick 2: session timer hits 0 → resolution fires.
            _counselling.Tick(_station);

            Assert.IsTrue(eventFired, "OnSessionComplete must fire after the session duration elapses.");
        }

        [Test]
        public void OnSessionComplete_EventPayload_HasCorrectNpcs()
        {
            NPCInstance firedCounsellor = null;
            NPCInstance firedPatient    = null;
            _counselling.OnSessionComplete += (c, p, outcome, roll) =>
            {
                firedCounsellor = c;
                firedPatient    = p;
            };

            _counselling.Tick(_station);
            _counselling.Tick(_station);

            Assert.AreEqual(_counsellor.uid, firedCounsellor?.uid,
                "Event counsellor UID must match the assigned counsellor.");
            Assert.AreEqual(_patient.uid, firedPatient?.uid,
                "Event patient UID must match the breakdown patient.");
        }

        [Test]
        public void CounsellorJobCleared_AfterSessionResolves()
        {
            _counselling.Tick(_station);
            // After assignment, job should be set.
            Assert.AreEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId);

            // Clear breakdown flag so AssignNewSessions does not immediately re-assign
            // the counsellor on the same tick the session resolves.
            _patient.sanity.isInBreakdown = false;

            _counselling.Tick(_station);
            // After resolution, job should be cleared so JobSystem can reassign.
            Assert.AreNotEqual(CounsellingSystem.CounsellingJobId, _counsellor.currentJobId,
                "Counsellor job should be cleared after the session resolves.");
        }
    }

    // ── TriggerEventRemoval integration tests ────────────────────────────────

    [TestFixture]
    public class CounsellingTraitRemovalTests
    {
        [Test]
        public void SuccessfulCounselling_CallsOnCounsellingComplete_RemovesTherapyRemovableTrait()
        {
            var station    = CounsellingTestHelpers.MakeStation();
            var counsellor = CounsellingTestHelpers.MakeCounsellorNpc();
            var patient    = CounsellingTestHelpers.MakeBreakdownNpc();
            station.AddNpc(counsellor);
            station.AddNpc(patient);

            // Give patient a therapy-removable trait.
            var traitDef = new NpcTraitDefinition
            {
                traitId          = "trait.test_therapy_trait",
                displayName      = "Test Therapy Trait",
                therapyRemovable = true,
                valence          = TraitValence.Negative,
                requiresEventToRemove = true,
                effects          = new List<TraitEffect>(),
            };

            var traitSystem = new TraitSystem();
            traitSystem.RegisterTrait(traitDef);

            var profile = patient.GetOrCreateTraitProfile();
            profile.traits.Add(new ActiveTrait
            {
                traitId        = "trait.test_therapy_trait",
                acquisitionTick = 0,
            });

            // Directly invoke TraitSystem's counselling completion handler to model a successful session.
            traitSystem.OnCounsellingComplete(patient, station);

            Assert.AreEqual(0, profile.traits.Count,
                "Therapy-removable trait should be removed after counselling completes successfully.");
        }
    }
}
