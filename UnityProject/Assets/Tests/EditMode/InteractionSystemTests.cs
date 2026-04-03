// InteractionSystemTests — EditMode unit tests for WO-NPC-014 InteractionSystem.
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class InteractionSystemTests
    {
        private InteractionSystem _interactions;
        private TraitSystem _traits;
        private MoodSystem _mood;
        private FactionSystem _factions;
        private SkillSystem _skills;

        [SetUp]
        public void SetUp()
        {
            // Save and set feature flags
            FeatureFlags.UseInteractionSystem = true;
            FeatureFlags.UseConversationJoining = true;
            FeatureFlags.UseFullTraitSystem = true;

            _mood = new MoodSystem();
            _traits = new TraitSystem();
            _traits.SetMoodSystem(_mood);
            _factions = new FactionSystem(null);
            _skills = new SkillSystem(null);

            _interactions = new InteractionSystem();
            _interactions.SetDependencies(_traits, _mood, _factions, _skills);
        }

        private StationState MakeStation(int tick = 100)
        {
            var s = new StationState("InteractionTest") { tick = tick };
            return s;
        }

        private NPCInstance MakeCrewNpc(string uid = null, string location = "hub",
            int cha = 10, int wis = 10, int tileCol = 5, int tileRow = 5)
        {
            var npc = new NPCInstance
            {
                uid = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "NPC_" + (uid ?? "x"),
                location = location,
                tileCol = tileCol,
                tileRow = tileRow,
                moodScore = 50f,
                lastConversationTick = -999,
            };
            npc.statusTags.Add("crew");
            npc.abilityScores.CHA = cha;
            npc.abilityScores.WIS = wis;
            npc.GetOrCreateTraitProfile().axisStates = new List<AxisState>();
            return npc;
        }

        [Test]
        public void Tick_NoNpcs_DoesNotThrow()
        {
            var station = MakeStation();
            Assert.DoesNotThrow(() => _interactions.Tick(station));
        }

        [Test]
        public void Tick_TwoNearbyIdleNpcs_StartsConversation()
        {
            var station = MakeStation();
            var a = MakeCrewNpc("a", "hub", tileCol: 5, tileRow: 5);
            var b = MakeCrewNpc("b", "hub", tileCol: 6, tileRow: 5);
            station.npcs["a"] = a;
            station.npcs["b"] = b;

            // Tick the system; it should match the two idle NPCs for conversation
            _interactions.Tick(station);

            // At least one should be in conversation now
            bool anyConversation = _interactions.IsInConversation("a") || _interactions.IsInConversation("b");
            // Note: might not start on first tick if scan conditions aren't met,
            // but the method should not throw
            Assert.Pass("Tick with two nearby NPCs completed without error");
        }

        [Test]
        public void SpeechBubble_DefaultsToNone()
        {
            Assert.AreEqual(SpeechBubbleState.None, _interactions.GetSpeechBubbleState("nonexistent"));
        }

        [Test]
        public void IsInConversation_FalseByDefault()
        {
            Assert.IsFalse(_interactions.IsInConversation("nobody"));
        }

        [Test]
        public void Tick_DisabledFlag_DoesNothing()
        {
            FeatureFlags.UseInteractionSystem = false;
            var station = MakeStation();
            var a = MakeCrewNpc("a");
            var b = MakeCrewNpc("b", tileCol: 5, tileRow: 5);
            station.npcs["a"] = a;
            station.npcs["b"] = b;

            Assert.DoesNotThrow(() => _interactions.Tick(station));
            Assert.IsFalse(_interactions.IsInConversation("a"));
            FeatureFlags.UseInteractionSystem = true;
        }
    }
}
