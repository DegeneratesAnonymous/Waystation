using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class MapSystemExplorationPipelineTests
    {
        private static FoundationInstance AddFoundation(
            StationState station, string buildableId, int cargoCapacity = 0, bool energised = true)
        {
            var f = FoundationInstance.Create(buildableId, 0, 0, cargoCapacity: cargoCapacity);
            f.status = "complete";
            f.isEnergised = energised;
            station.foundations[f.uid] = f;
            return f;
        }

        [Test]
        public void InterstellarAntenna_GeneratesExplorationPointsPerTick()
        {
            var map = new MapSystem();
            var station = new StationState("EXP-EP");
            AddFoundation(station, "buildable.interstellar_antenna");

            map.TickExplorationState(station);

            Assert.AreEqual(1, station.explorationPoints);
        }

        [Test]
        public void TryUnlockSector_SpendsPoints_AndGeneratesAdjacentSector()
        {
            var map = new MapSystem();
            var station = new StationState("EXP-Unlock");
            station.galaxySeed = 12345;
            station.explorationPoints = MapSystem.SectorUnlockPointCost;

            var home = SectorData.Create(
                "sector_home",
                new Vector2(GalaxyGenerator.HomeX, GalaxyGenerator.HomeY),
                SurveyPrefix.GSC,
                new List<PhenomenonCode> { PhenomenonCode.MS },
                "Home");
            home.discoveryState = SectorDiscoveryState.Visited;
            station.sectors[home.uid] = home;

            bool ok = map.TryUnlockSector(station, 1, 0);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, station.explorationPoints);
            Assert.AreEqual(2, station.sectors.Count);
        }

        [Test]
        public void ScoutMission_WithCartographyStation_ProducesExplorationDatachip()
        {
            var go = new GameObject("registry");
            var registry = go.AddComponent<ContentRegistry>();
            registry.Missions["mission.scout"] = new MissionDefinition
            {
                id = "mission.scout",
                displayName = "Scout Survey",
                missionType = "scout",
                durationTicks = 1,
                crewRequired = 1,
                requiredSkill = "science",
                requiredSkillLevel = 1,
                successChanceBase = 1f,
            };
            var missions = new MissionSystem(registry);
            var station = new StationState("EXP-Scout");
            AddFoundation(station, "buildable.cartography_station");
            var holder = AddFoundation(station, "buildable.storage_cabinet", cargoCapacity: 8);

            var crew = NPCInstance.Create("npc.test", "Scout", "class.scientist");
            crew.statusTags.Add("crew");
            crew.skills["science"] = 5;
            station.npcs[crew.uid] = crew;

            var (ok, _, mission) = missions.DispatchMission("mission.scout", new List<string> { crew.uid }, station);
            Assert.IsTrue(ok);
            mission.targetSystemSeed = 777;
            mission.targetSystemName = "Survey Target";

            station.tick = mission.endTick;
            missions.Tick(station);

            Assert.AreEqual(1, station.explorationDatachips.Count);
            Assert.AreEqual(1, holder.cargo["item.exploration_datachip"]);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ScoutMission_UsesContainerWithFreeTotalCapacity()
        {
            var go = new GameObject("registry");
            var registry = go.AddComponent<ContentRegistry>();
            registry.Items["item.exploration_datachip"] = new ItemDefinition
            {
                id = "item.exploration_datachip",
                item_type = "Valuables",
            };
            registry.Missions["mission.scout"] = new MissionDefinition
            {
                id = "mission.scout",
                displayName = "Scout Survey",
                missionType = "scout",
                durationTicks = 1,
                crewRequired = 1,
                requiredSkill = "science",
                requiredSkillLevel = 1,
                successChanceBase = 1f,
            };
            var missions = new MissionSystem(registry);
            var station = new StationState("EXP-Cap");
            AddFoundation(station, "buildable.cartography_station");
            var fullHolder = AddFoundation(station, "buildable.storage_cabinet", cargoCapacity: 1);
            var freeHolder = AddFoundation(station, "buildable.storage_cabinet", cargoCapacity: 2);
            fullHolder.cargo["item.parts"] = 1;

            var crew = NPCInstance.Create("npc.test", "Scout", "class.scientist");
            crew.statusTags.Add("crew");
            crew.skills["science"] = 5;
            station.npcs[crew.uid] = crew;

            var (ok, _, mission) = missions.DispatchMission("mission.scout", new List<string> { crew.uid }, station);
            Assert.IsTrue(ok);
            mission.targetSystemSeed = 701;
            mission.targetSystemName = "Capacity Target";

            station.tick = mission.endTick;
            missions.Tick(station);

            Assert.IsFalse(fullHolder.cargo.ContainsKey("item.exploration_datachip"));
            Assert.AreEqual(1, freeHolder.cargo["item.exploration_datachip"]);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ScoutMission_WithoutCartographyStation_DoesNotProduceExplorationDatachip()
        {
            var go = new GameObject("registry");
            var registry = go.AddComponent<ContentRegistry>();
            registry.Missions["mission.scout"] = new MissionDefinition
            {
                id = "mission.scout",
                displayName = "Scout Survey",
                missionType = "scout",
                durationTicks = 1,
                crewRequired = 1,
                requiredSkill = "science",
                requiredSkillLevel = 1,
                successChanceBase = 1f,
            };
            var missions = new MissionSystem(registry);
            var station = new StationState("EXP-NoCart");
            AddFoundation(station, "buildable.storage_cabinet", cargoCapacity: 8);

            var crew = NPCInstance.Create("npc.test", "Scout", "class.scientist");
            crew.statusTags.Add("crew");
            crew.skills["science"] = 5;
            station.npcs[crew.uid] = crew;

            var (ok, _, mission) = missions.DispatchMission("mission.scout", new List<string> { crew.uid }, station);
            Assert.IsTrue(ok);
            mission.targetSystemSeed = 888;
            mission.targetSystemName = "Survey Target";

            station.tick = mission.endTick;
            missions.Tick(station);

            Assert.AreEqual(0, station.explorationDatachips.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void DestroyedDatachip_RemovesChartedSystemOnNextMapTick()
        {
            var map = new MapSystem();
            var station = new StationState("EXP-Prune");
            var server = AddFoundation(station, "buildable.cartography_server", cargoCapacity: 4);
            server.cargo["item.exploration_datachip"] = 1;

            var chip = ExplorationDatachipInstance.Create(909, "Known System");
            chip.holderFoundationUid = server.uid;
            station.explorationDatachips[chip.uid] = chip;

            map.TickExplorationState(station);
            Assert.IsTrue(station.chartedSystemSeeds.Contains(909));

            server.cargo["item.exploration_datachip"] = 0;
            map.TickExplorationState(station);

            Assert.IsFalse(station.chartedSystemSeeds.Contains(909));
        }

        [Test]
        public void MovedDatachip_RebindsToNewHolder_AndKeepsChartedState()
        {
            var map = new MapSystem();
            var station = new StationState("EXP-Move");
            var server = AddFoundation(station, "buildable.cartography_server", cargoCapacity: 4);
            var cabinet = AddFoundation(station, "buildable.storage_cabinet", cargoCapacity: 4);
            server.cargo["item.exploration_datachip"] = 1;

            var chip = ExplorationDatachipInstance.Create(505, "Moved System");
            chip.holderFoundationUid = server.uid;
            station.explorationDatachips[chip.uid] = chip;

            map.TickExplorationState(station);
            Assert.IsTrue(station.chartedSystemSeeds.Contains(505));

            server.cargo["item.exploration_datachip"] = 0;
            cabinet.cargo["item.exploration_datachip"] = 1;
            map.TickExplorationState(station);

            Assert.IsTrue(station.explorationDatachips.ContainsKey(chip.uid));
            Assert.AreEqual(cabinet.uid, station.explorationDatachips[chip.uid].holderFoundationUid);
            Assert.IsFalse(station.chartedSystemSeeds.Contains(505), "Not installed in cartography server anymore.");
        }
    }
}
