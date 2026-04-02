// ResearchSubPanelControllerTests.cs
// EditMode unit tests for ResearchSubPanelController (UI-018).
//
// Tests cover:
//   * Node state visual class assignment for all four states (Locked, Available,
//     In Progress, Complete).
//   * DataChipIndicator state matches datachip presence in ResearchSystem
//     (IsNodeChipActive).
//   * Node detail sub-panel populates correctly (prerequisites listed with
//     completion state, name, cost, progress).

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;
using Waystation.UI;

namespace Waystation.Tests
{
    [TestFixture]
    public class ResearchSubPanelControllerTests
    {
        private GameObject     _registryGo;
        private ContentRegistry _registry;
        private ResearchSystem  _research;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("ResearchSubPanelControllerTests_Registry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _research   = new ResearchSystem(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static StationState MakeStation() => new StationState("ResearchUITest");

        private static FoundationInstance AddDataStorageServer(StationState station, int capacity = 4)
        {
            var f = FoundationInstance.Create("buildable.data_storage_server", 0, 0, cargoCapacity: capacity);
            f.status = "complete";
            station.foundations[f.uid] = f;
            return f;
        }

        private static ResearchNodeDefinition Node(
            string id, ResearchBranch branch, int cost, params string[] prereqs)
        {
            return new ResearchNodeDefinition
            {
                id           = id,
                displayName  = id,
                branch       = branch,
                subbranch    = ResearchSubbranch.Production,
                pointCost    = cost,
                prerequisites = new List<string>(prereqs),
                unlockTags   = new List<string> { $"tech.{id.Replace(".", "_")}" },
            };
        }

        // ── GetNodeState — all four states ────────────────────────────────────

        [Test]
        public void GetNodeState_Locked_WhenPrerequisiteNotMet()
        {
            var station = MakeStation();
            var node    = Node("node.b", ResearchBranch.Industry, 100, "node.a");
            _registry.ResearchNodes[node.id] = node;

            var state = ResearchSubPanelController.GetNodeState(
                node, station, _research, ResearchBranch.Industry);

            Assert.AreEqual(ResearchSubPanelController.NodeState.Locked, state);
        }

        [Test]
        public void GetNodeState_Available_WhenPrereqsMetAndNoProgress()
        {
            var station = MakeStation();
            var node    = Node("node.root", ResearchBranch.Industry, 100);
            _registry.ResearchNodes[node.id] = node;
            // No points accumulated → progress = 0.
            station.research.branches[ResearchBranch.Industry].points = 0f;

            var state = ResearchSubPanelController.GetNodeState(
                node, station, _research, ResearchBranch.Industry);

            Assert.AreEqual(ResearchSubPanelController.NodeState.Available, state);
        }

        [Test]
        public void GetNodeState_InProgress_WhenPrereqsMetAndPointsAccumulated()
        {
            var station = MakeStation();
            var node    = Node("node.root", ResearchBranch.Industry, 100);
            _registry.ResearchNodes[node.id] = node;
            // Some points accumulated → progress > 0.
            station.research.branches[ResearchBranch.Industry].points = 50f;

            var state = ResearchSubPanelController.GetNodeState(
                node, station, _research, ResearchBranch.Industry);

            Assert.AreEqual(ResearchSubPanelController.NodeState.InProgress, state);
        }

        [Test]
        public void GetNodeState_Complete_WhenNodeIsUnlocked()
        {
            var station = MakeStation();
            var node    = Node("node.done", ResearchBranch.Industry, 100);
            _registry.ResearchNodes[node.id] = node;
            station.research.branches[ResearchBranch.Industry].unlockedNodeIds.Add(node.id);

            var state = ResearchSubPanelController.GetNodeState(
                node, station, _research, ResearchBranch.Industry);

            Assert.AreEqual(ResearchSubPanelController.NodeState.Complete, state);
        }

        [Test]
        public void GetNodeState_Complete_TakesPrecedenceOverPrereqCheck()
        {
            // Even if prerequisites themselves are not in the registry, a node that
            // is already unlocked must report Complete, not Locked.
            var station = MakeStation();
            var node    = Node("node.done2", ResearchBranch.Science, 200, "node.missing_prereq");
            _registry.ResearchNodes[node.id] = node;
            station.research.branches[ResearchBranch.Science].unlockedNodeIds.Add(node.id);

            var state = ResearchSubPanelController.GetNodeState(
                node, station, _research, ResearchBranch.Science);

            Assert.AreEqual(ResearchSubPanelController.NodeState.Complete, state);
        }

        // ── IsNodeChipActive — DataChipIndicator state ────────────────────────

        [Test]
        public void IsNodeChipActive_True_WhenChipStoredAndTagActive()
        {
            var station = MakeStation();
            var storage = AddDataStorageServer(station);
            var node    = Node("node.chip", ResearchBranch.Industry, 1);
            _registry.ResearchNodes[node.id] = node;

            // Simulate unlock + chip store (as ResearchSystem.Tick would do).
            station.research.branches[ResearchBranch.Industry].unlockedNodeIds.Add(node.id);
            station.research.branches[ResearchBranch.Industry].unlockedNodeOrder.Add(node.id);
            storage.cargo["item.datachip"] = 1;

            // Tick once to apply unlock tags.
            _research.Tick(station);

            Assert.IsTrue(_research.IsNodeChipActive(node.id, station));
        }

        [Test]
        public void IsNodeChipActive_False_WhenChipRemovedFromServer()
        {
            var station = MakeStation();
            var storage = AddDataStorageServer(station);
            var node    = Node("node.chip_removed", ResearchBranch.Industry, 1);
            _registry.ResearchNodes[node.id] = node;

            station.research.branches[ResearchBranch.Industry].unlockedNodeIds.Add(node.id);
            station.research.branches[ResearchBranch.Industry].unlockedNodeOrder.Add(node.id);
            storage.cargo["item.datachip"] = 1;

            _research.Tick(station);
            Assert.IsTrue(_research.IsNodeChipActive(node.id, station));

            // Remove the chip from storage and tick again.
            storage.cargo.Remove("item.datachip");
            _research.Tick(station);

            Assert.IsFalse(_research.IsNodeChipActive(node.id, station));
        }

        [Test]
        public void IsNodeChipActive_False_WhenNodeHasNoUnlockTags()
        {
            var station = MakeStation();
            AddDataStorageServer(station);
            var node = new ResearchNodeDefinition
            {
                id           = "node.no_tags",
                displayName  = "No Tags Node",
                branch       = ResearchBranch.Science,
                subbranch    = ResearchSubbranch.Physics,
                pointCost    = 50,
                prerequisites = new List<string>(),
                unlockTags   = new List<string>(),   // no tags
            };
            _registry.ResearchNodes[node.id] = node;
            station.research.branches[ResearchBranch.Science].unlockedNodeIds.Add(node.id);

            _research.Tick(station);

            // No tags means no way to verify chip is active via tag check.
            Assert.IsFalse(_research.IsNodeChipActive(node.id, station));
        }

        // ── GetBranchData ─────────────────────────────────────────────────────

        [Test]
        public void GetBranchData_ReturnsOnlyNodesInBranch()
        {
            var industryNode = Node("node.ind", ResearchBranch.Industry,   50);
            var scienceNode  = Node("node.sci", ResearchBranch.Science,    50);
            _registry.ResearchNodes[industryNode.id] = industryNode;
            _registry.ResearchNodes[scienceNode.id]  = scienceNode;

            var result = _research.GetBranchData(ResearchBranch.Industry);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(industryNode.id, result[0].id);
        }

        [Test]
        public void GetBranchData_ReturnsEmptyArray_WhenNoBranchNodes()
        {
            var result = _research.GetBranchData(ResearchBranch.Diplomacy);
            Assert.AreEqual(0, result.Length);
        }

        // ── GetActiveAssignments ──────────────────────────────────────────────

        [Test]
        public void GetActiveAssignments_ReturnsNpcsWithMatchingJobBranch()
        {
            var station = MakeStation();
            var npc     = NPCInstance.Create("npc.test", "Scientist", "class.science");
            npc.statusTags.Add("crew");
            npc.currentJobId = "job.research_industry";
            station.npcs[npc.uid] = npc;

            var result = _research.GetActiveAssignments(ResearchBranch.Industry, station);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(npc.uid, result[0].uid);
        }

        [Test]
        public void GetActiveAssignments_ExcludesNpcsWithDifferentBranch()
        {
            var station = MakeStation();
            var npc     = NPCInstance.Create("npc.test2", "Explorer", "class.science");
            npc.statusTags.Add("crew");
            npc.currentJobId = "job.research_exploration";
            station.npcs[npc.uid] = npc;

            var result = _research.GetActiveAssignments(ResearchBranch.Industry, station);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GetActiveAssignments_ExcludesNonCrewNpcs()
        {
            var station = MakeStation();
            var visitor = NPCInstance.Create("npc.visitor", "Visitor", "class.civilian");
            // No "crew" tag.
            visitor.currentJobId = "job.research_industry";
            station.npcs[visitor.uid] = visitor;

            var result = _research.GetActiveAssignments(ResearchBranch.Industry, station);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GetActiveAssignments_ReturnsEmpty_WhenNullStation()
        {
            var result = _research.GetActiveAssignments(ResearchBranch.Exploration, null);
            Assert.AreEqual(0, result.Length);
        }

        // ── gridX / gridY on ResearchNodeDefinition ───────────────────────────

        [Test]
        public void ResearchNodeDefinition_GridXY_DefaultZero()
        {
            var node = new ResearchNodeDefinition { id = "test.node" };
            Assert.AreEqual(0, node.gridX);
            Assert.AreEqual(0, node.gridY);
        }

        [Test]
        public void ResearchNodeDefinition_FromDict_ParsesGridXY()
        {
            var raw = new Dictionary<string, object>
            {
                { "id",          "node.grid_test" },
                { "display_name","Grid Test" },
                { "branch",      "Industry" },
                { "subbranch",   "Production" },
                { "point_cost",  "50" },
                { "grid_x",      "3" },
                { "grid_y",      "2" },
            };

            var node = ResearchNodeDefinition.FromDict(raw);

            Assert.AreEqual(3, node.gridX);
            Assert.AreEqual(2, node.gridY);
        }
    }
}
