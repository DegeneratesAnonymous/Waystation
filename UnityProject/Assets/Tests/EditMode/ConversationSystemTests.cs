// ConversationSystemTests — EditMode unit tests for NPC-011 ConversationSystem.
//
// Validates:
//   • Raw CHA check produces correct quality tier at low, mid, and high CHA values
//   • Skill follow-up selection logic for Persuasion, Intimidation, and Deception
//   • Mood modifier and affinity change applied to both participants per quality tier
//   • Conversation cooldown enforcement prevents re-conversation within cooldown window
//   • Notable event log entries fire on CriticalFail, skill follow-up, and large affinity swings
//   • Speech bubble query methods (IsConversing / GetActiveConversations) reflect live state
//   • Full conversation pipeline: idle trigger → CHA check → outcome → event log
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class ConversationTestHelpers
    {
        public static StationState MakeStation(int tick = 200)
        {
            var s = new StationState { stationName = "ConvTestStation", tick = tick };
            return s;
        }

        /// <summary>
        /// Creates an idle crew NPC placed in the given location with the specified
        /// CHA score and skill levels.
        /// </summary>
        public static NPCInstance MakeCrewNpc(
            string uid           = null,
            string location      = "hub",
            int    cha           = 10,
            int    persuasion    = 0,
            int    intimidation  = 0,
            int    deception     = 0)
        {
            var npc = new NPCInstance
            {
                uid      = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name     = "NPC_" + (uid ?? "x"),
                location = location,
                lastConversationTick = -999,   // well past any cooldown
            };
            npc.statusTags.Add("crew");
            npc.abilityScores.CHA = cha;

            if (persuasion > 0)    SetSkill(npc, "skill.persuasion",   persuasion);
            if (intimidation > 0)  SetSkill(npc, "skill.intimidation", intimidation);
            if (deception > 0)     SetSkill(npc, "skill.deception",    deception);

            return npc;
        }

        private static void SetSkill(NPCInstance npc, string skillId, int level)
        {
            // level = floor(sqrt(xp/100)) → xp = level^2 * 100
            npc.skillInstances.Add(new SkillInstance
            {
                skillId   = skillId,
                currentXP = (float)(level * level * 100),
            });
        }

        /// <summary>Creates a ConversationSystem seeded so rolls are deterministic.</summary>
        public static ConversationSystem MakeSystem(int seed = 42)
            => new ConversationSystem(seed);
    }

    // ── CHA quality tier tests ────────────────────────────────────────────────

    [TestFixture]
    public class ChaQualityTierTests
    {
        private ConversationSystem _sys;

        [SetUp]
        public void SetUp() => _sys = ConversationTestHelpers.MakeSystem();

        /// <summary>
        /// Verifies that a very low CHA NPC (CHA 3, modifier -2) can produce
        /// a CriticalFail result on some d20 rolls.
        /// With CHA 3 the total is d20 - 2, spanning -1 to 18, which keeps the
        /// NPC in the lower tiers frequently enough that CriticalFail should occur.
        /// The test exercises this by repeatedly calling RollChaQuality for a CHA 3
        /// NPC and asserting that CriticalFail is observed at least once.
        /// </summary>

        // Direct tier verification by manipulating the NPC's CHA score and
        // using RollChaQuality with a controlled state.  Because the internal
        // d20 roll is random we test the tier boundaries by exercising
        // multiple rolls and verifying that the expected tiers are reachable.

        [Test]
        public void LowChaNpc_CanProduceCriticalFail()
        {
            // CHA 3 → modifier = -2 (score ≤ 4 → mod = -2).
            // d20 rolls 1–4 produce total ≤ 2 → CriticalFail (4/20 = 20% per roll).
            var npc = ConversationTestHelpers.MakeCrewNpc(cha: 3);
            bool foundCritical = false;
            for (int i = 0; i < 200 && !foundCritical; i++)
                if (_sys.RollChaQuality(npc) == ConversationQuality.CriticalFail)
                    foundCritical = true;
            Assert.IsTrue(foundCritical,
                "CHA 3 NPC should produce CriticalFail on some d20 rolls.");
        }

        [Test]
        public void HighChaNpc_CanProduceHighQuality()
        {
            // CHA 20 → modifier = +3 (score > 17 → mod = +3).
            // d20 rolls 12–20 give total 15–23 → High (9/20 = 45% per roll).
            var npc = ConversationTestHelpers.MakeCrewNpc(cha: 20);
            bool foundHigh = false;
            for (int i = 0; i < 50 && !foundHigh; i++)
                if (_sys.RollChaQuality(npc) == ConversationQuality.High)
                    foundHigh = true;
            Assert.IsTrue(foundHigh, "CHA 20 NPC should produce High quality on some rolls.");
        }

        [Test]
        public void MidChaNpc_ProducesMidOrNeighboringTiers()
        {
            // CHA 10 → modifier = 0.  Rolls 8–14 are Mid, 3–7 are Low,
            // ≥15 are High, ≤2 are CriticalFail — all reachable.
            var npc = ConversationTestHelpers.MakeCrewNpc(cha: 10);
            bool foundMid = false;
            for (int i = 0; i < 200 && !foundMid; i++)
                if (_sys.RollChaQuality(npc) == ConversationQuality.Mid)
                    foundMid = true;
            Assert.IsTrue(foundMid, "CHA 10 NPC should produce Mid quality on some rolls.");
        }

        [Test]
        public void QualityLadder_BoundaryValues_CorrectTier()
        {
            // Use a CHA that keeps modifier = 0 (CHA 10) and verify tier assignments
            // by constructing NPCs with extreme CHA values to force specific totals.

            // CHA 1 → modifier = -2 (score ≤ 4 → mod = -2).
            // d20 rolls 1–4 give total ≤ 2 → CriticalFail; rolls 5–9 give 3–7 → Low.
            // d20=20 gives total 18 → High (not a fixed 15 as in exactly-15-CHA scenario).
            // We can't directly set the d20, so we verify via large samples.
            var lowCha  = ConversationTestHelpers.MakeCrewNpc(cha: 1);
            var highCha = ConversationTestHelpers.MakeCrewNpc(cha: 20);

            bool foundLow = false;
            for (int i = 0; i < 500 && !foundLow; i++)
                if (_sys.RollChaQuality(lowCha) == ConversationQuality.Low)
                    foundLow = true;
            Assert.IsTrue(foundLow, "CHA 1 NPC should produce Low tier on some rolls.");

            bool foundHighForHighCha = false;
            for (int i = 0; i < 50 && !foundHighForHighCha; i++)
                if (_sys.RollChaQuality(highCha) == ConversationQuality.High)
                    foundHighForHighCha = true;
            Assert.IsTrue(foundHighForHighCha, "CHA 20 NPC should reach High tier.");
        }
    }

    // ── Follow-up skill selection tests ──────────────────────────────────────

    [TestFixture]
    public class FollowUpSkillSelectionTests
    {
        [Test]
        public void SociableTrait_SelectsPersuasion()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc();
            npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.sociable" });
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Persuasion, skill);
        }

        [Test]
        public void DistrustfulTrait_SelectsDeception()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc();
            npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.distrustful" });
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Deception, skill);
        }

        [Test]
        public void VigilantTrait_SelectsIntimidation()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc();
            npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.vigilant" });
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Intimidation, skill);
        }

        [Test]
        public void HardenedTrait_SelectsIntimidation()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc();
            npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.hardened" });
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Intimidation, skill);
        }

        [Test]
        public void CynicalTrait_SelectsDeception()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc();
            npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.cynical" });
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Deception, skill);
        }

        [Test]
        public void NoTraits_HighestSkillChosen_Intimidation()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc(intimidation: 5, persuasion: 2, deception: 1);
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Intimidation, skill);
        }

        [Test]
        public void NoTraits_HighestSkillChosen_Deception()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc(deception: 7, persuasion: 3, intimidation: 4);
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Deception, skill);
        }

        [Test]
        public void NoTraitsNoSkills_DefaultsToPersuasion()
        {
            var npc = ConversationTestHelpers.MakeCrewNpc();
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Persuasion, skill);
        }

        [Test]
        public void IntimidationTrait_TakesPriorityOverDeceptionSkill()
        {
            // Trait-based selection takes precedence over skill-level fallback.
            var npc = ConversationTestHelpers.MakeCrewNpc(deception: 10, intimidation: 1);
            npc.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.vigilant" });
            var skill = ConversationSystem.SelectFollowUpSkill(npc);
            Assert.AreEqual(ConversationFollowUpSkill.Intimidation, skill);
        }
    }

    // ── Affinity and mood outcome tests ──────────────────────────────────────

    [TestFixture]
    public class ConversationOutcomeTests
    {
        private StationState       _station;
        private NPCInstance        _a;
        private NPCInstance        _b;

        [SetUp]
        public void SetUp()
        {
            _station = ConversationTestHelpers.MakeStation();
            _a = ConversationTestHelpers.MakeCrewNpc(uid: "npc_a", cha: 20); // always High
            _b = ConversationTestHelpers.MakeCrewNpc(uid: "npc_b", cha: 10);
            _station.AddNpc(_a);
            _station.AddNpc(_b);
        }

        [Test]
        public void CriticalFail_DecreasesAffinityByExpectedAmount()
        {
            var sys = new ConversationSystem(seed: 0);
            _a.abilityScores.CHA = 1;   // mod -2; ~20% chance of CriticalFail per roll

            // Drive many full conversation cycles looking for a tick where the
            // affinity delta exactly equals AffinityCriticalFail (-8).
            float lastAffinity = RelationshipRegistry.Get(_station, _a.uid, _b.uid)?.affinityScore ?? 0f;
            bool criticalDeltaObserved = false;

            for (int pass = 0; pass < 200 && !criticalDeltaObserved; pass++)
            {
                _station.tick = 200 + pass * ConversationSystem.ConversationCooldownTicks * 2;
                _a.lastConversationTick = -999;
                _b.lastConversationTick = -999;

                // Advance through enough ticks to start and resolve one conversation
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);

                float currentAffinity = RelationshipRegistry.Get(_station, _a.uid, _b.uid)?.affinityScore ?? lastAffinity;
                float delta = currentAffinity - lastAffinity;

                // A CriticalFail applies exactly AffinityCriticalFail (no other modifier can land
                // on this exact value from the same pipeline step)
                if (System.Math.Abs(delta - ConversationSystem.AffinityCriticalFail) < 0.001f)
                    criticalDeltaObserved = true;

                lastAffinity = currentAffinity;
            }

            Assert.IsTrue(criticalDeltaObserved,
                "Expected at least one CriticalFail producing an affinity delta equal to " +
                $"ConversationSystem.AffinityCriticalFail ({ConversationSystem.AffinityCriticalFail}).");
        }

        [Test]
        public void FullPipeline_PositiveOutcome_IncreasesAffinity()
        {
            // CHA 20 NPC should produce High quality very frequently.
            // Run many full tick cycles until affinity between the pair has increased.
            var sys = new ConversationSystem(seed: 7);
            _a.abilityScores.CHA = 20;
            _b.abilityScores.CHA = 20;

            float initialAffinity = RelationshipRegistry.Get(_station, _a.uid, _b.uid)?.affinityScore ?? 0f;

            // Advance tick far past cooldown and run multiple conversations.
            for (int pass = 0; pass < 20; pass++)
            {
                _station.tick = 200 + pass * ConversationSystem.ConversationCooldownTicks * 2;
                _a.lastConversationTick = -999;
                _b.lastConversationTick = -999;

                // Two ticks to start + resolve a conversation
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
            }

            float finalAffinity = RelationshipRegistry.Get(_station, _a.uid, _b.uid)?.affinityScore ?? 0f;
            Assert.Greater(finalAffinity, initialAffinity,
                "High-CHA NPCs should accumulate positive affinity over multiple conversations.");
        }

        [Test]
        public void CriticalFail_AppliesNegativeAffinity_OverMultiplePipelines()
        {
            var sys = new ConversationSystem(seed: 99);
            // Force CHA so low that CriticalFail is common
            _a.abilityScores.CHA = 1;

            float initialAffinity = RelationshipRegistry.Get(_station, _a.uid, _b.uid)?.affinityScore ?? 0f;

            for (int pass = 0; pass < 30; pass++)
            {
                _station.tick = 200 + pass * ConversationSystem.ConversationCooldownTicks * 2;
                _a.lastConversationTick = -999;
                _b.lastConversationTick = -999;

                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
            }

            float finalAffinity = RelationshipRegistry.Get(_station, _a.uid, _b.uid)?.affinityScore ?? 0f;
            Assert.Less(finalAffinity, initialAffinity,
                "Very low-CHA NPC should drive affinity negative over repeated conversations.");
        }
    }

    // ── Cooldown enforcement tests ────────────────────────────────────────────

    [TestFixture]
    public class ConversationCooldownTests
    {
        private StationState _station;
        private NPCInstance  _a;
        private NPCInstance  _b;

        [SetUp]
        public void SetUp()
        {
            _station = ConversationTestHelpers.MakeStation(tick: 200);
            _a = ConversationTestHelpers.MakeCrewNpc(uid: "npc_a");
            _b = ConversationTestHelpers.MakeCrewNpc(uid: "npc_b");
            _station.AddNpc(_a);
            _station.AddNpc(_b);
        }

        [Test]
        public void NpcsCannotConverse_WhileCooldownActive()
        {
            var sys = new ConversationSystem(seed: 1);

            // Record conversation tick manually to simulate a recent conversation.
            _a.lastConversationTick = _station.tick - 10;  // 10 ticks ago < 60 cooldown
            _b.lastConversationTick = _station.tick - 10;

            // Neither should be in an active conversation after tick
            sys.Tick(_station, null, null);
            Assert.IsFalse(sys.IsConversing(_a.uid),
                "NPC A should not be conversing while within cooldown.");
            Assert.IsFalse(sys.IsConversing(_b.uid),
                "NPC B should not be conversing while within cooldown.");
        }

        [Test]
        public void NpcsCan_Converse_OnceCooldownExpires()
        {
            var sys = new ConversationSystem(seed: 2);

            // Place last conversation well before the cooldown window.
            _a.lastConversationTick = _station.tick - ConversationSystem.ConversationCooldownTicks - 1;
            _b.lastConversationTick = _station.tick - ConversationSystem.ConversationCooldownTicks - 1;

            sys.Tick(_station, null, null);
            // Either both are conversing or neither — the pair should have been matched
            bool aBusy = sys.IsConversing(_a.uid);
            bool bBusy = sys.IsConversing(_b.uid);
            Assert.IsTrue(aBusy && bBusy,
                "Both NPCs should be matched into a conversation once cooldown has expired.");
        }

        [Test]
        public void AfterConversationResolves_CooldownIsEnforced()
        {
            var sys = new ConversationSystem(seed: 3);

            _a.lastConversationTick = -999;
            _b.lastConversationTick = -999;

            // Tick 1: start conversation
            sys.Tick(_station, null, null);

            // Advance ticks to resolve the conversation
            for (int t = 0; t <= ConversationSystem.ConversationDurationTicks + 1; t++)
            {
                _station.tick++;
                sys.Tick(_station, null, null);
            }

            // Immediately after resolution, lastConversationTick should be set,
            // so neither can start another conversation for 60 ticks.
            sys.Tick(_station, null, null);
            Assert.IsFalse(sys.IsConversing(_a.uid),
                "NPC A should be on cooldown immediately after conversation resolution.");
        }
    }

    // ── Event log entry tests ─────────────────────────────────────────────────

    [TestFixture]
    public class ConversationEventLogTests
    {
        private StationState _station;
        private NPCInstance  _a;
        private NPCInstance  _b;

        [SetUp]
        public void SetUp()
        {
            _station = ConversationTestHelpers.MakeStation(tick: 200);
            _a = ConversationTestHelpers.MakeCrewNpc(uid: "npc_a");
            _b = ConversationTestHelpers.MakeCrewNpc(uid: "npc_b");
            _station.AddNpc(_a);
            _station.AddNpc(_b);
        }

        [Test]
        public void CriticalFail_AlwaysLogsNotableEvent()
        {
            // Use a CHA 1 NPC to generate CriticalFails frequently
            _a.abilityScores.CHA = 1;
            var sys = new ConversationSystem(seed: 55);

            int logCountBefore = _station.log.Count;
            bool critFound = false;

            for (int pass = 0; pass < 50 && !critFound; pass++)
            {
                _station.tick = 200 + pass * (ConversationSystem.ConversationCooldownTicks * 2);
                _a.lastConversationTick = -999;
                _b.lastConversationTick = -999;

                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);

                // Check if a critical fail was logged
                for (int i = logCountBefore; i < _station.log.Count; i++)
                {
                    if (_station.log[i].Contains("critical"))
                    {
                        critFound = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(critFound,
                "A CriticalFail conversation should produce an event log entry containing 'critical'.");
        }

        [Test]
        public void SkillFollowUp_ProducesEventLogEntry()
        {
            // CHA 20 NPC with sociable trait to guarantee Persuasion follow-up on High rolls
            _a.abilityScores.CHA = 20;
            _a.GetOrCreateTraitProfile().traits.Add(new ActiveTrait { traitId = "trait.sociable" });
            // High persuasion skill to guarantee success
            _a.skillInstances.Add(new SkillInstance { skillId = "skill.persuasion", currentXP = 10000f });

            var sys = new ConversationSystem(seed: 77);

            int logCountBefore = _station.log.Count;
            bool logFound = false;

            for (int pass = 0; pass < 20 && !logFound; pass++)
            {
                _station.tick = 200 + pass * (ConversationSystem.ConversationCooldownTicks * 2);
                _a.lastConversationTick = -999;
                _b.lastConversationTick = -999;

                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);
                _station.tick++;
                sys.Tick(_station, null, null);

                for (int i = logCountBefore; i < _station.log.Count; i++)
                {
                    if (_station.log[i].Contains("Persuasion") ||
                        _station.log[i].Contains("persuasion"))
                    {
                        logFound = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(logFound,
                "A High CHA conversation with a skill follow-up should produce an event log entry.");
        }
    }

    // ── Speech bubble indicator tests ─────────────────────────────────────────

    [TestFixture]
    public class SpeechBubbleIndicatorTests
    {
        private StationState _station;
        private NPCInstance  _a;
        private NPCInstance  _b;

        [SetUp]
        public void SetUp()
        {
            _station = ConversationTestHelpers.MakeStation(tick: 200);
            _a = ConversationTestHelpers.MakeCrewNpc(uid: "npc_a");
            _b = ConversationTestHelpers.MakeCrewNpc(uid: "npc_b");
            _station.AddNpc(_a);
            _station.AddNpc(_b);
        }

        [Test]
        public void IsConversing_ReturnsFalse_WhenNotInConversation()
        {
            var sys = new ConversationSystem(seed: 1);
            Assert.IsFalse(sys.IsConversing(_a.uid));
            Assert.IsFalse(sys.IsConversing(_b.uid));
        }

        [Test]
        public void IsConversing_ReturnsTrue_ForBothParticipants_WhenConversationActive()
        {
            var sys = new ConversationSystem(seed: 2);

            _a.lastConversationTick = -999;
            _b.lastConversationTick = -999;

            sys.Tick(_station, null, null);

            Assert.IsTrue(sys.IsConversing(_a.uid),
                "NPC A should be marked as conversing immediately after conversation starts.");
            Assert.IsTrue(sys.IsConversing(_b.uid),
                "NPC B should be marked as conversing immediately after conversation starts.");
        }

        [Test]
        public void IsConversing_ReturnsFalse_AfterConversationResolves()
        {
            var sys = new ConversationSystem(seed: 3);

            _a.lastConversationTick = -999;
            _b.lastConversationTick = -999;

            sys.Tick(_station, null, null);
            Assert.IsTrue(sys.IsConversing(_a.uid), "Should be conversing after start.");

            // Advance ticks to resolve
            for (int t = 0; t <= ConversationSystem.ConversationDurationTicks + 1; t++)
            {
                _station.tick++;
                sys.Tick(_station, null, null);
            }

            Assert.IsFalse(sys.IsConversing(_a.uid),
                "NPC A should no longer be marked as conversing after resolution.");
            Assert.IsFalse(sys.IsConversing(_b.uid),
                "NPC B should no longer be marked as conversing after resolution.");
        }

        [Test]
        public void GetActiveConversations_ContainsBothParticipants_WhenActive()
        {
            var sys = new ConversationSystem(seed: 4);

            _a.lastConversationTick = -999;
            _b.lastConversationTick = -999;

            sys.Tick(_station, null, null);

            var active = sys.GetActiveConversations();
            Assert.IsTrue(active.ContainsKey(_a.uid),
                "Active conversations dictionary should contain NPC A's uid.");
            Assert.IsTrue(active.ContainsKey(_b.uid),
                "Active conversations dictionary should contain NPC B's uid.");
            Assert.AreEqual(_b.uid, active[_a.uid],
                "NPC A's conversation partner should be NPC B.");
            Assert.AreEqual(_a.uid, active[_b.uid],
                "NPC B's conversation partner should be NPC A.");
        }
    }
}
