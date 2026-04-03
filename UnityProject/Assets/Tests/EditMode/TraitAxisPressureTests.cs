// TraitAxisPressureTests — EditMode unit tests for WO-NPC-015 TraitSystem 12-axis pressure system.
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class TraitAxisPressureTests
    {
        private TraitSystem _traits;
        private MoodSystem _mood;

        [SetUp]
        public void SetUp()
        {
            _mood = new MoodSystem();
            _traits = new TraitSystem();
            _traits.SetMoodSystem(_mood);

            // Load axis data from embedded JSON strings
            string defsJson = @"{""traits"":[
                {""id"":""trait_honest"",""display_name"":""Honest"",""axis_id"":""honesty"",""stage"":1,""modifiers"":{""persuasion_bonus"":0.05}},
                {""id"":""trait_transparent"",""display_name"":""Transparent"",""axis_id"":""honesty"",""stage"":2,""modifiers"":{""persuasion_bonus"":0.1}},
                {""id"":""trait_radically_honest"",""display_name"":""Radically Honest"",""axis_id"":""honesty"",""stage"":3,""modifiers"":{""persuasion_bonus"":0.15}},
                {""id"":""trait_deceptive"",""display_name"":""Deceptive"",""axis_id"":""honesty"",""stage"":-1,""modifiers"":{""deception_bonus"":0.05}},
                {""id"":""trait_manipulative"",""display_name"":""Manipulative"",""axis_id"":""honesty"",""stage"":-2,""modifiers"":{""deception_bonus"":0.1}},
                {""id"":""trait_pathological_liar"",""display_name"":""Pathological Liar"",""axis_id"":""honesty"",""stage"":-3,""modifiers"":{""deception_bonus"":0.15}}
            ]}";
            string matrixJson = @"{""pairs"":[
                {""axis_a"":""honesty"",""axis_b"":""subterfuge"",""positive_positive"":-0.3,""positive_negative"":0.2,""negative_positive"":0.2,""negative_negative"":-0.1}
            ]}";
            _traits.LoadAxisData(defsJson, matrixJson);
        }

        private NPCInstance MakeNpc(string uid = null)
        {
            var npc = new NPCInstance
            {
                uid = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "TestNpc"
            };
            npc.statusTags.Add("crew");
            var profile = npc.GetOrCreateTraitProfile();
            if (profile.axisStates == null)
                profile.axisStates = new List<AxisState>();
            return npc;
        }

        [Test]
        public void AddPressure_ShiftsStageAtThreshold()
        {
            var npc = MakeNpc();
            // Start at stage 0, threshold to reach +1 is 7 points
            // Add 7 points of positive pressure
            for (int i = 0; i < 7; i++)
                _traits.AddPressure(npc, "honesty", 1f);

            var profile = npc.GetOrCreateTraitProfile();
            var axis = profile.axisStates.Find(a => a.axisId == "honesty");
            Assert.IsNotNull(axis, "Honesty axis should be created");
            Assert.AreEqual(1, axis.currentStage, "Stage should shift to +1 at 7 pressure");
        }

        [Test]
        public void AddPressure_NegativeShiftsDownward()
        {
            var npc = MakeNpc();
            // Add 7 points of negative pressure to shift to -1
            for (int i = 0; i < 7; i++)
                _traits.AddPressure(npc, "honesty", -1f);

            var profile = npc.GetOrCreateTraitProfile();
            var axis = profile.axisStates.Find(a => a.axisId == "honesty");
            Assert.IsNotNull(axis);
            Assert.AreEqual(-1, axis.currentStage, "Stage should shift to -1 at 7 negative pressure");
        }

        [Test]
        public void AddPressure_StageDoesNotExceedBounds()
        {
            var npc = MakeNpc();
            // Push far past highest threshold
            for (int i = 0; i < 100; i++)
                _traits.AddPressure(npc, "honesty", 1f);

            var profile = npc.GetOrCreateTraitProfile();
            var axis = profile.axisStates.Find(a => a.axisId == "honesty");
            Assert.IsNotNull(axis);
            Assert.LessOrEqual(axis.currentStage, 3, "Stage should not exceed +3");
        }

        [Test]
        public void GetCompatibility_ReturnsZeroForNoAxes()
        {
            var a = MakeNpc("npcA");
            var b = MakeNpc("npcB");
            float compat = _traits.GetCompatibility(a, b);
            Assert.AreEqual(0f, compat, 0.001f, "No axes = zero compatibility");
        }

        [Test]
        public void GetCompatibility_ReturnsValueForMatchingAxes()
        {
            var a = MakeNpc("npcA");
            var b = MakeNpc("npcB");
            // Give both NPCs honesty and subterfuge axes
            a.GetOrCreateTraitProfile().axisStates.Add(new AxisState { axisId = "honesty", currentStage = 2 });
            a.GetOrCreateTraitProfile().axisStates.Add(new AxisState { axisId = "subterfuge", currentStage = 1 });
            b.GetOrCreateTraitProfile().axisStates.Add(new AxisState { axisId = "honesty", currentStage = -1 });
            b.GetOrCreateTraitProfile().axisStates.Add(new AxisState { axisId = "subterfuge", currentStage = -1 });
            float compat = _traits.GetCompatibility(a, b);
            Assert.AreNotEqual(0f, compat, "Should return non-zero compatibility for matching axes");
        }

        [Test]
        public void GetTraitModifiers_DefaultWhenNoAxes()
        {
            var npc = MakeNpc();
            var mods = _traits.GetTraitModifiers(npc);
            Assert.AreEqual(0f, mods.persuasion_bonus, "Default modifier should be 0");
        }

        [Test]
        public void GetTraitModifiers_AppliesAxisModifiers()
        {
            var npc = MakeNpc();
            npc.GetOrCreateTraitProfile().axisStates.Add(
                new AxisState { axisId = "honesty", currentStage = 1 });
            var mods = _traits.GetTraitModifiers(npc);
            Assert.Greater(mods.persuasion_bonus, 0f, "Honest +1 should give persuasion bonus");
        }
    }
}
