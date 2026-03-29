// EconomySystem tests — EditMode unit tests.
// Validates: docking fee application, contract payment timing, exempt roles,
// and credit balance query.
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class EconomySystemDockingFeeTests
    {
        private EconomySystem _economy;

        [SetUp]
        public void SetUp()
        {
            _economy = new EconomySystem();
            FeatureFlags.EconomySystem = true;
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.EconomySystem = true;
        }

        private static StationState MakeStation(float credits = 500f)
        {
            var s = new StationState("EconomyTest");
            s.resources["credits"] = credits;
            s.resources["food"]    = 100f;
            s.resources["power"]   = 100f;
            s.resources["oxygen"]  = 100f;
            s.resources["parts"]   = 50f;
            s.resources["ice"]     = 200f;
            s.resources["fuel"]    = 50f;
            return s;
        }

        private static ShipInstance AddDockedShip(StationState station, string role = "trader",
                                                   string factionId = null)
        {
            var ship = ShipInstance.Create("ship.test", "Test Ship", role, "trade", factionId);
            ship.status = "docked";
            station.ships[ship.uid] = ship;
            return ship;
        }

        [Test]
        public void DockingFee_TraderShip_CreditAdded()
        {
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "trader");

            _economy.Tick(station);

            Assert.AreEqual(500f + EconomySystem.DockingFeeBase, station.GetResource("credits"), 0.1f,
                "Docking fee should be added to credits when a trader docks.");
        }

        [Test]
        public void DockingFee_OnlyChargedOnce()
        {
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "trader");

            _economy.Tick(station);
            _economy.Tick(station); // second tick — same ship still docked

            Assert.AreEqual(500f + EconomySystem.DockingFeeBase, station.GetResource("credits"), 0.1f,
                "Docking fee should only be charged once per visit.");
        }

        [Test]
        public void DockingFee_RefugeeShip_NoFee()
        {
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "refugee");

            _economy.Tick(station);

            Assert.AreEqual(500f, station.GetResource("credits"), 0.1f,
                "Refugee ships are exempt from docking fees.");
        }

        [Test]
        public void DockingFee_PatrolShip_NoFee()
        {
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "patrol");

            _economy.Tick(station);

            Assert.AreEqual(500f, station.GetResource("credits"), 0.1f,
                "Patrol ships are exempt from docking fees.");
        }

        [Test]
        public void DockingFee_InspectorShip_NoFee()
        {
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "inspector");

            _economy.Tick(station);

            Assert.AreEqual(500f, station.GetResource("credits"), 0.1f,
                "Inspector ships are exempt from docking fees.");
        }

        [Test]
        public void DockingFee_FeatureFlagOff_NoCreditAdded()
        {
            FeatureFlags.EconomySystem = false;
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "trader");

            _economy.Tick(station);

            Assert.AreEqual(500f, station.GetResource("credits"), 0.1f,
                "No docking fee when EconomySystem feature flag is off.");
        }

        [Test]
        public void DockingFee_MultipleShips_EachCharged()
        {
            var station = MakeStation(credits: 500f);
            AddDockedShip(station, "trader");
            AddDockedShip(station, "smuggler");

            _economy.Tick(station);

            Assert.AreEqual(500f + EconomySystem.DockingFeeBase * 2f,
                station.GetResource("credits"), 0.1f,
                "Each non-exempt docked ship should be charged the docking fee.");
        }

        [Test]
        public void GetCreditBalance_ReturnsCorrectBalance()
        {
            var station = MakeStation(credits: 750f);
            Assert.AreEqual(750f, _economy.GetCreditBalance(station), 0.1f);
        }
    }

    [TestFixture]
    public class EconomySystemContractTests
    {
        private EconomySystem _economy;

        [SetUp]
        public void SetUp()
        {
            _economy = new EconomySystem();
            FeatureFlags.EconomySystem = true;
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.EconomySystem = true;
        }

        private static StationState MakeStation(float credits = 500f, int tick = 0)
        {
            var s = new StationState("ContractTest");
            s.tick             = tick;
            s.resources["credits"] = credits;
            s.resources["food"]    = 100f;
            s.resources["power"]   = 100f;
            s.resources["oxygen"]  = 100f;
            s.resources["parts"]   = 50f;
            s.resources["ice"]     = 200f;
            s.resources["fuel"]    = 50f;
            return s;
        }

        [Test]
        public void Contract_PaymentOnInterval_CreditAdded()
        {
            var station = MakeStation(credits: 500f, tick: 100);
            station.factionContracts["contract.1"] = new FactionContract
            {
                contractId            = "contract.1",
                factionId             = "faction.test",
                creditPerPayment      = 200f,
                paymentIntervalTicks  = 48,
                lastPaymentTick       = 0,    // last paid at tick 0, now at tick 100 → overdue
            };

            _economy.Tick(station);

            Assert.AreEqual(500f + 200f, station.GetResource("credits"), 0.1f,
                "Contract payment should be added when interval has elapsed.");
        }

        [Test]
        public void Contract_BeforeInterval_NoCreditAdded()
        {
            var station = MakeStation(credits: 500f, tick: 20);
            station.factionContracts["contract.1"] = new FactionContract
            {
                contractId            = "contract.1",
                factionId             = "faction.test",
                creditPerPayment      = 200f,
                paymentIntervalTicks  = 48,
                lastPaymentTick       = 10,   // only 10 ticks since last payment
            };

            _economy.Tick(station);

            Assert.AreEqual(500f, station.GetResource("credits"), 0.1f,
                "No contract payment before the interval elapses.");
        }

        [Test]
        public void Contract_PaymentUpdatesLastPaymentTick()
        {
            var station = MakeStation(credits: 500f, tick: 100);
            var contract = new FactionContract
            {
                contractId            = "contract.1",
                factionId             = "faction.test",
                creditPerPayment      = 200f,
                paymentIntervalTicks  = 48,
                lastPaymentTick       = 0,
            };
            station.factionContracts["contract.1"] = contract;

            _economy.Tick(station);

            Assert.AreEqual(100, contract.lastPaymentTick,
                "lastPaymentTick should update to current tick after payment.");
        }

        [Test]
        public void Contract_MultipleContracts_AllPaid()
        {
            var station = MakeStation(credits: 500f, tick: 100);
            station.factionContracts["contract.a"] = new FactionContract
            {
                contractId = "contract.a", creditPerPayment = 100f,
                paymentIntervalTicks = 48, lastPaymentTick = 0
            };
            station.factionContracts["contract.b"] = new FactionContract
            {
                contractId = "contract.b", creditPerPayment = 150f,
                paymentIntervalTicks = 48, lastPaymentTick = 0
            };

            _economy.Tick(station);

            Assert.AreEqual(500f + 100f + 150f, station.GetResource("credits"), 0.1f,
                "All due contracts should pay out in the same tick.");
        }

        [Test]
        public void Contract_FeatureFlagOff_NoCreditAdded()
        {
            FeatureFlags.EconomySystem = false;
            var station = MakeStation(credits: 500f, tick: 100);
            station.factionContracts["contract.1"] = new FactionContract
            {
                contractId            = "contract.1",
                creditPerPayment      = 200f,
                paymentIntervalTicks  = 48,
                lastPaymentTick       = 0,
            };

            _economy.Tick(station);

            Assert.AreEqual(500f, station.GetResource("credits"), 0.1f,
                "No contract payment when EconomySystem flag is off.");
        }
    }
}
