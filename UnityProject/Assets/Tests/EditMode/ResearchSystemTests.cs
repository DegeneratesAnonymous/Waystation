using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class ResearchSystemTests
    {
        private GameObject _registryGo;
        private ContentRegistry _registry;
        private ResearchSystem _research;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("ResearchSystemTests_Registry");
            _registry = _registryGo.AddComponent<ContentRegistry>();
            _research = new ResearchSystem(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
        }

        private static StationState MakeStation()
        {
            return new StationState("ResearchTest");
        }

        private static FoundationInstance AddCompleteFoundation(
            StationState station, string buildableId, int cargoCapacity = 0)
        {
            var f = FoundationInstance.Create(buildableId, 0, 0, cargoCapacity: cargoCapacity);
            f.status = "complete";
            station.foundations[f.uid] = f;
            return f;
        }

        private static NPCInstance AddCrewNpc(StationState station, string currentJobId = null)
        {
            var npc = NPCInstance.Create("npc.test", "Researcher", "class.engineering");
            npc.statusTags.Add("crew");
            npc.currentJobId = currentJobId;
            npc.skills["science"] = 5;
            station.npcs[npc.uid] = npc;
            return npc;
        }

        private static ResearchNodeDefinition Node(string id, ResearchBranch branch, int cost,
            params string[] prereqs)
        {
            return new ResearchNodeDefinition
            {
                id = id,
                displayName = id,
                branch = branch,
                subbranch = branch == ResearchBranch.Exploration
                    ? ResearchSubbranch.Astrometrics
                    : ResearchSubbranch.Production,
                pointCost = cost,
                prerequisites = new List<string>(prereqs),
                unlockTags = new List<string> { $"tech.{id.Replace("research.", "").Replace('.', '_')}" }
            };
        }

        [Test]
        public void StationedButNotAssigned_GeneratesNoResearchPoints()
        {
            var station = MakeStation();
            AddCompleteFoundation(station, "buildable.industry_terminal");
            AddCrewNpc(station, currentJobId: null);

            _research.SetPointsPerNpcPerTick(1f);
            _research.Tick(station);

            Assert.AreEqual(0f, station.research.branches[ResearchBranch.Industry].points, 0.0001f);
        }

        [Test]
        public void AssignedResearchJob_GeneratesConfiguredPointsPerTick()
        {
            var station = MakeStation();
            AddCompleteFoundation(station, "buildable.industry_terminal");
            AddCrewNpc(station, "job.research_industry");

            _research.SetPointsPerNpcPerTick(1f);
            _research.Tick(station);

            Assert.AreEqual(1f, station.research.branches[ResearchBranch.Industry].points, 0.0001f);
        }

        [Test]
        public void PrerequisitesMustBeMet_NodeDoesNotUnlockAtThresholdWithoutPrereq()
        {
            var station = MakeStation();
            var gated = Node("research.industry.gated", ResearchBranch.Industry, 1, "research.industry.missing");
            _registry.ResearchNodes[gated.id] = gated;
            station.research.branches[ResearchBranch.Industry].points = 5f;

            _research.Tick(station);

            Assert.IsFalse(station.research.IsUnlocked(gated.id));
        }

        [Test]
        public void NodeUnlock_ProducesDatachipInDataStorageServer()
        {
            var station = MakeStation();
            AddCompleteFoundation(station, "buildable.data_storage_server", cargoCapacity: 4);
            var node = Node("research.industry.unlock_chip", ResearchBranch.Industry, 1);
            _registry.ResearchNodes[node.id] = node;
            station.research.branches[ResearchBranch.Industry].points = 1f;

            _research.Tick(station);

            Assert.AreEqual(1, _research.GetStoredDatachipCount(station));
        }

        [Test]
        public void RemovingDatachip_DeactivatesUnlockTagOnNextTick()
        {
            var station = MakeStation();
            var storage = AddCompleteFoundation(station, "buildable.data_storage_server", cargoCapacity: 4);
            var node = Node("research.industry.tagged", ResearchBranch.Industry, 1);
            node.unlockTags = new List<string> { "tech.tagged_unlock" };
            _registry.ResearchNodes[node.id] = node;
            station.research.branches[ResearchBranch.Industry].points = 1f;

            _research.Tick(station);
            Assert.IsTrue(station.HasTag("tech.tagged_unlock"), "Unlock tag should be active with chip present.");

            storage.cargo.Remove("item.datachip");
            _research.Tick(station);

            Assert.IsFalse(station.HasTag("tech.tagged_unlock"), "Unlock tag should deactivate when chip is removed.");
        }

        [Test]
        public void RelayNodeCopySemantics_SourceRetainsOriginalDatachip()
        {
            var source = MakeStation();
            var destination = MakeStation();
            var sourceStorage = AddCompleteFoundation(source, "buildable.data_storage_server", cargoCapacity: 4);
            AddCompleteFoundation(destination, "buildable.data_storage_server", cargoCapacity: 4);
            AddCompleteFoundation(source, "buildable.relay_node");

            var node = Node("research.industry.relay_copy", ResearchBranch.Industry, 1);
            _registry.ResearchNodes[node.id] = node;

            source.research.branches[ResearchBranch.Industry].unlockedNodeIds.Add(node.id);
            source.research.branches[ResearchBranch.Industry].unlockedNodeOrder.Add(node.id);
            sourceStorage.cargo["item.datachip"] = 1;

            int copied = _research.CopyDatachipsViaRelayNodes(source, destination);

            Assert.AreEqual(1, copied);
            Assert.AreEqual(1, _research.GetStoredDatachipCount(source), "Source chip must be retained.");
            Assert.AreEqual(1, _research.GetStoredDatachipCount(destination), "Destination should receive a copied chip.");
            Assert.IsTrue(destination.research.IsUnlocked(node.id), "Destination should receive copied unlock.");
        }

        [Test]
        public void RelayNodeBranchFilter_ExcludesNonConfiguredBranches()
        {
            var source = MakeStation();
            var destination = MakeStation();
            var sourceStorage = AddCompleteFoundation(source, "buildable.data_storage_server", cargoCapacity: 8);
            AddCompleteFoundation(destination, "buildable.data_storage_server", cargoCapacity: 8);
            var relay = AddCompleteFoundation(source, "buildable.relay_node");

            var industry = Node("research.industry.filtered", ResearchBranch.Industry, 1);
            var exploration = Node("research.exploration.filtered", ResearchBranch.Exploration, 1);
            _registry.ResearchNodes[industry.id] = industry;
            _registry.ResearchNodes[exploration.id] = exploration;

            source.research.branches[ResearchBranch.Industry].unlockedNodeIds.Add(industry.id);
            source.research.branches[ResearchBranch.Industry].unlockedNodeOrder.Add(industry.id);
            source.research.branches[ResearchBranch.Exploration].unlockedNodeIds.Add(exploration.id);
            source.research.branches[ResearchBranch.Exploration].unlockedNodeOrder.Add(exploration.id);
            sourceStorage.cargo["item.datachip"] = 2;

            _research.SetRelayBranchFilter(relay, new[] { ResearchBranch.Industry });
            int copied = _research.CopyDatachipsViaRelayNodes(source, destination);

            Assert.AreEqual(1, copied);
            Assert.IsTrue(destination.research.IsUnlocked(industry.id));
            Assert.IsFalse(destination.research.IsUnlocked(exploration.id));
        }
    }
}
