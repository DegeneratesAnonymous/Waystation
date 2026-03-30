using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class TemperatureSystemTests
    {
        private GameObject _registryGo;
        private ContentRegistry _registry;
        private NetworkSystem _networks;
        private TemperatureSystem _temperature;
        private UtilityNetworkManager _utility;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("TemperatureSystemTests_Registry");
            _registry = _registryGo.AddComponent<ContentRegistry>();

            _registry.Buildables["buildable.vent"] = new BuildableDefinition
            {
                id = "buildable.vent",
                networkType = "electric",
                nodeRole = "consumer",
                demandWatts = 2f
            };
            _registry.Buildables["buildable.duct"] = new BuildableDefinition
            {
                id = "buildable.duct",
                networkType = "duct",
                nodeRole = "conduit"
            };
            _registry.Buildables["buildable.wire"] = new BuildableDefinition
            {
                id = "buildable.wire",
                networkType = "electric",
                nodeRole = "conduit"
            };
            _registry.Buildables["buildable.reactor"] = new BuildableDefinition
            {
                id = "buildable.reactor",
                networkType = "electric",
                nodeRole = "producer",
                outputWatts = 50f
            };
            _registry.Buildables["buildable.heater"] = new BuildableDefinition
            {
                id = "buildable.heater",
                networkType = "electric",
                nodeRole = "consumer",
                demandWatts = 10f
            };

            _networks = new NetworkSystem(_registry);
            _temperature = new TemperatureSystem(_registry, _networks);
            _utility = new UtilityNetworkManager(_registry, _networks);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void Tick_VentWithoutDuctConnection_DoesNotTransferTemperature()
        {
            var station = MakeTwoRoomStation();
            station.roomTemperatures["A"] = 0f;
            station.roomTemperatures["B"] = 40f;

            AddFoundation(station, "buildable.vent", 0, 0, energised: true);
            AddFoundation(station, "buildable.duct", 0, 0); // no duct at neighbour tile

            _networks.RebuildNetworks(station);
            _temperature.Tick(station);

            Assert.AreEqual(0f, station.roomTemperatures["A"], 0.001f);
            Assert.AreEqual(40f, station.roomTemperatures["B"], 0.001f);
        }

        [Test]
        public void Tick_VentWithConnectedDuct_EqualisesAtExpectedRate()
        {
            var station = MakeTwoRoomStation();
            station.roomTemperatures["A"] = 0f;
            station.roomTemperatures["B"] = 40f;

            AddFoundation(station, "buildable.vent", 0, 0, energised: true);
            AddFoundation(station, "buildable.duct", 0, 0);
            AddFoundation(station, "buildable.duct", 1, 0);

            _networks.RebuildNetworks(station);
            _temperature.Tick(station);

            Assert.AreEqual(0.5f, station.roomTemperatures["A"], 0.001f);
            Assert.AreEqual(39.5f, station.roomTemperatures["B"], 0.001f);
        }

        [Test]
        public void Tick_DuctSevered_StopsEqualisationOnNextTick()
        {
            var station = MakeTwoRoomStation();
            station.roomTemperatures["A"] = 0f;
            station.roomTemperatures["B"] = 40f;

            AddFoundation(station, "buildable.vent", 0, 0, energised: true);
            AddFoundation(station, "buildable.duct", 0, 0);
            var neighbourDuct = AddFoundation(station, "buildable.duct", 1, 0);

            _networks.RebuildNetworks(station);
            _temperature.Tick(station);

            float afterConnectedA = station.roomTemperatures["A"];
            float afterConnectedB = station.roomTemperatures["B"];

            station.foundations.Remove(neighbourDuct.uid);
            _networks.RebuildNetworks(station);
            _temperature.Tick(station);

            Assert.AreEqual(afterConnectedA, station.roomTemperatures["A"], 0.001f);
            Assert.AreEqual(afterConnectedB, station.roomTemperatures["B"], 0.001f);
        }

        [Test]
        public void Tick_MultipleVentConnections_IncreasesEqualisationRate()
        {
            var single = MakeTwoRoomStation();
            single.roomTemperatures["A"] = 0f;
            single.roomTemperatures["B"] = 40f;
            AddFoundation(single, "buildable.vent", 0, 0, energised: true);
            AddFoundation(single, "buildable.duct", 0, 0);
            AddFoundation(single, "buildable.duct", 1, 0);
            _networks.RebuildNetworks(single);
            _temperature.Tick(single);
            float singleDelta = single.roomTemperatures["A"];

            var multi = new StationState("MultiVent");
            multi.roomTemperatures["A"] = 0f;
            multi.roomTemperatures["B"] = 40f;
            AssignRoom(multi, 0, 0, "A");
            AssignRoom(multi, 0, 1, "A");
            AssignRoom(multi, 1, 0, "B");
            AssignRoom(multi, 1, 1, "B");

            AddFoundation(multi, "buildable.vent", 0, 0, energised: true);
            AddFoundation(multi, "buildable.vent", 0, 1, energised: true);
            AddFoundation(multi, "buildable.duct", 0, 0);
            AddFoundation(multi, "buildable.duct", 1, 0);
            AddFoundation(multi, "buildable.duct", 0, 1);
            AddFoundation(multi, "buildable.duct", 1, 1);

            _networks.RebuildNetworks(multi);
            _temperature.Tick(multi);
            float multiDelta = multi.roomTemperatures["A"];

            Assert.Greater(multiDelta, singleDelta);
            Assert.AreEqual(1.0f, multiDelta, 0.001f);
        }

        [Test]
        public void Tick_HeaterWithSufficientElectricalSupply_IsEnergisedAndHeatsRoom()
        {
            var station = new StationState("HeaterPower");
            station.roomTemperatures["A"] = 10f;
            AssignRoom(station, 0, 0, "A");

            AddFoundation(station, "buildable.reactor", 2, 0);
            AddFoundation(station, "buildable.wire", 1, 0);
            var heater = AddFoundation(station, "buildable.heater", 0, 0);
            heater.targetTemperature = 20f;

            _networks.RebuildNetworks(station);
            _utility.Tick(station);
            _temperature.Tick(station);

            Assert.IsTrue(heater.isEnergised);
            Assert.AreEqual(11f, station.roomTemperatures["A"], 0.001f);

            var heaterNet = _networks.GetNetwork(station, heater.uid);
            Assert.NotNull(heaterNet);
            Assert.AreEqual(10f, heaterNet.totalDemand, 0.001f);
        }

        private static StationState MakeTwoRoomStation()
        {
            var station = new StationState("TwoRooms");
            AssignRoom(station, 0, 0, "A");
            AssignRoom(station, 1, 0, "B");
            return station;
        }

        private static void AssignRoom(StationState station, int col, int row, string roomKey)
        {
            station.tileToRoomKey[$"{col}_{row}"] = roomKey;
        }

        private static FoundationInstance AddFoundation(
            StationState station, string buildableId, int col, int row, bool energised = false)
        {
            var f = FoundationInstance.Create(buildableId, col, row);
            f.status = "complete";
            f.isEnergised = energised;
            station.foundations[f.uid] = f;
            return f;
        }
    }
}
