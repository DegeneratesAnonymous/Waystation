using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    internal class VisitorStubRegistry : IRegistryAccess
    {
        public Dictionary<string, ModuleDefinition> Modules { get; } = new();
        public Dictionary<string, ResourceDefinition> Resources { get; } = new();
        public Dictionary<string, EventDefinition> Events { get; } = new();

        public Dictionary<string, NPCTemplate> Npcs { get; } = new();
        public Dictionary<string, ShipTemplate> Ships { get; } = new();
        public Dictionary<string, FactionDefinition> Factions { get; } = new();
        public Dictionary<string, ItemDefinition> Items { get; } = new();
        public Dictionary<string, BuildableDefinition> Buildables { get; } = new();
    }

    [TestFixture]
    public class VisitorRoleRoutingTests
    {
        [TestCase("trader", "cargo_hold")]
        [TestCase("diplomat", "comms_room")]
        [TestCase("medical_emergency", "medical_bay")]
        [TestCase("inspector", "cargo_hold")]
        [TestCase("refugee", "common_area")]
        [TestCase("smuggler", "cargo_hold")]
        [TestCase("passerby", "hangar")]
        [TestCase("raider", "hangar")]
        public void RoleMapsToExpectedRoomType(string role, string expectedRoomType)
        {
            Assert.AreEqual(expectedRoomType, VisitorSystem.GetRoomTypeForRole(role));
        }
    }

    [TestFixture]
    public class InspectorScanTaskTests
    {
        private ContentRegistry _registry;
        private EventSystem _events;
        private StationState _station;
        private GameObject _registryGo;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("VisitorTestRegistry");
            _registry = _registryGo.AddComponent<ContentRegistry>();
            _registry.Items["item.contraband_stims"] = new ItemDefinition
            {
                id = "item.contraband_stims",
                legal = false,
                tags = new List<string> { "contraband" }
            };

            var registry = new EventStubRegistry();
            registry.Events["event.contraband_found"] = new EventDefinition
            {
                id = "event.contraband_found",
                title = "Contraband",
                weight = 0f
            };
            _events = new EventSystem(registry, "normal");

            _station = new StationState("VisitorScanTest");
            _station.resources["credits"] = 1000f;

            // One cargo-hold room tile with one contraband container
            _station.tileToRoomKey["5_5"] = "5_5";
            _station.playerRoomTypeAssignments["5_5"] = "cargo_hold";
            var container = FoundationInstance.Create("buildable.storage_crate", 5, 5, cargoCapacity: 20);
            container.status = "complete";
            container.cargo["item.contraband_stims"] = 2;
            _station.foundations[container.uid] = container;

            var ship = ShipInstance.Create("ship.authority_cutter", "Authority", "inspector", "inspect", "faction.stellar_authority", 2);
            ship.status = "docked";
            _station.ships[ship.uid] = ship;
        }

        [TearDown]
        public void TearDown()
        {
            if (_registryGo != null)
                Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void ScanDetectsContraband_WhenWisdomAndSkillStrong()
        {
            var inspector = NPCInstance.Create("npc.inspector", "Inspector", "class.security");
            inspector.abilityScores.WIS = 18;
            inspector.skills["investigation"] = 9;
            inspector.memory["visitor_ship_uid"] = GetFirstShipUid();
            _station.AddNpc(inspector);
            _station.ships[GetFirstShipUid()].passengerUids.Add(inspector.uid);

            var task = new InspectorScanTask(_registry, _events, null, 4, 200f, -10f);
            task.Tick(inspector, _station);

            Assert.AreEqual(NPCTaskStatus.Succeeded, task.Status);
            Assert.IsTrue(inspector.memory.TryGetValue("inspector_scan_detected", out var detectedObj) &&
                          detectedObj is bool detected && detected);
            Assert.Less(_station.GetResource("credits"), 1000f);
        }

        [Test]
        public void ScanMissesContraband_WhenConcealmentVeryHigh()
        {
            var inspector = NPCInstance.Create("npc.inspector", "Inspector", "class.security");
            inspector.abilityScores.WIS = 8;
            inspector.skills["investigation"] = 0;
            inspector.memory["visitor_ship_uid"] = GetFirstShipUid();
            _station.AddNpc(inspector);

            var smuggler = NPCInstance.Create("npc.smuggler", "Smuggler", "class.trader");
            smuggler.skills["deception"] = 50;
            _station.AddNpc(smuggler);

            var ship = _station.ships[GetFirstShipUid()];
            ship.passengerUids.Add(inspector.uid);
            ship.passengerUids.Add(smuggler.uid);

            var task = new InspectorScanTask(_registry, _events, null, 12, 200f, -10f);
            task.Tick(inspector, _station);

            Assert.AreEqual(NPCTaskStatus.Succeeded, task.Status);
            Assert.IsTrue(inspector.memory.TryGetValue("inspector_scan_detected", out var detectedObj) &&
                          detectedObj is bool detected && !detected);
            Assert.AreEqual(1000f, _station.GetResource("credits"), 0.01f);
        }

        private string GetFirstShipUid()
        {
            foreach (var s in _station.ships.Values) return s.uid;
            return "";
        }
    }

    [TestFixture]
    public class VisitorDenyEscalationTests
    {
        private GameObject _registryGo;

        [TearDown]
        public void TearDown()
        {
            if (_registryGo != null)
                Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void DenyRaiderEscalatesToHostileAndQueuesBoarding()
        {
            _registryGo = new GameObject("VisitorDenyRegistry");
            var registry = _registryGo.AddComponent<ContentRegistry>();
            registry.Ships["ship.raider_corvette"] = new ShipTemplate
            {
                id = "ship.raider_corvette",
                role = "raider",
                behaviorTags = new List<string> { "hostile_if_denied" }
            };

            var eventRegistry = new EventStubRegistry();
            eventRegistry.Events["event.hostile_ship"] = new EventDefinition
            {
                id = "event.hostile_ship",
                title = "Hostile",
                weight = 0f
            };
            var events = new EventSystem(eventRegistry, "normal");
            var visitors = new VisitorSystem(registry, null, events, null, null, null);

            var station = new StationState("DenyTest");
            var ship = ShipInstance.Create("ship.raider_corvette", "Raider", "raider", "raid", "faction.scavenger_clans", 6);
            ship.status = "incoming";
            station.AddShip(ship);

            visitors.DenyShip(ship.uid, station);

            Assert.IsTrue(station.ships.TryGetValue(ship.uid, out var updated));
            Assert.AreEqual("hostile", updated.status);
            Assert.AreEqual("raid", updated.intent);
            Assert.Contains("hostile_task_queue", updated.behaviorTags);
        }
    }
}
