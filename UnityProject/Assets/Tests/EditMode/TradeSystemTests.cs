// TradeSystem tests — EditMode unit tests.
// Validates: price calculation modifiers, standing order execution, and
// full buy/sell transaction paths.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static class TradeTestHelpers
    {
        /// <summary>Creates a station with default resource levels.</summary>
        public static StationState MakeStation(float credits = 1000f, float food = 100f,
                                               float parts = 50f, float ice = 200f,
                                               float power = 100f, float oxygen = 100f)
        {
            var s = new StationState("TradeTest");
            s.resources["credits"] = credits;
            s.resources["food"]    = food;
            s.resources["parts"]   = parts;
            s.resources["ice"]     = ice;
            s.resources["power"]   = power;
            s.resources["oxygen"]  = oxygen;
            s.resources["fuel"]    = 50f;
            return s;
        }

        /// <summary>Creates a basic trade offer with one sell line and one buy line.</summary>
        public static TradeOffer MakeOffer(string shipUid = "ship1",
                                           string resource = "food",
                                           float sellPrice = 5f,
                                           float buyPrice  = 3f,
                                           float available = 20f,
                                           float wants     = 10f)
        {
            var offer = new TradeOffer { shipUid = shipUid, shipName = "Test Ship" };
            offer.lines.Add(new TradeLine { resource = resource, pricePerUnit = sellPrice, available = available });
            offer.lines.Add(new TradeLine { resource = "parts",  pricePerUnit = buyPrice,  available = -wants  });
            return offer;
        }

        public static ShipInstance MakeShip(string role = "trader", string factionId = null)
        {
            return ShipInstance.Create("ship.freighter", "Test Ship", role, "trade", factionId);
        }
    }

    // ── Supply/demand modifier tests ──────────────────────────────────────────

    [TestFixture]
    public class SupplyDemandModifierTests
    {
        [SetUp]
        public void SetUp()
        {
            FeatureFlags.EconomySystem = true;
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.EconomySystem = true;
        }

        [Test]
        public void SellLine_Shortage_ReturnsShortagePremium()
        {
            var station = TradeTestHelpers.MakeStation(food: TradeSystem.ShortageThreshold - 1f);
            float mod = TradeSystem.SupplyDemandModifier("food", isSellLine: true, station);
            Assert.AreEqual(TradeSystem.ShortagePremiumFactor, mod, 0.001f,
                "Sell line with shortage should apply premium.");
        }

        [Test]
        public void SellLine_Surplus_ReturnsNeutral()
        {
            var station = TradeTestHelpers.MakeStation(food: TradeSystem.SurplusThreshold + 1f);
            float mod = TradeSystem.SupplyDemandModifier("food", isSellLine: true, station);
            Assert.AreEqual(1f, mod, 0.001f,
                "Sell line with surplus stock should return neutral modifier.");
        }

        [Test]
        public void SellLine_Neutral_ReturnsOne()
        {
            var station = TradeTestHelpers.MakeStation(food: 100f);
            float mod = TradeSystem.SupplyDemandModifier("food", isSellLine: true, station);
            Assert.AreEqual(1f, mod, 0.001f,
                "Sell line at neutral stock level should return 1.0.");
        }

        [Test]
        public void BuyLine_Surplus_ReturnsSurplusDiscount()
        {
            var station = TradeTestHelpers.MakeStation(food: TradeSystem.SurplusThreshold + 1f);
            float mod = TradeSystem.SupplyDemandModifier("food", isSellLine: false, station);
            Assert.AreEqual(TradeSystem.SurplusDiscountFactor, mod, 0.001f,
                "Buy line with surplus stock should apply discount.");
        }

        [Test]
        public void BuyLine_Shortage_ReturnsNeutral()
        {
            var station = TradeTestHelpers.MakeStation(food: TradeSystem.ShortageThreshold - 1f);
            float mod = TradeSystem.SupplyDemandModifier("food", isSellLine: false, station);
            Assert.AreEqual(1f, mod, 0.001f,
                "Buy line with shortage stock should return neutral modifier.");
        }

        [Test]
        public void FeatureFlagOff_AlwaysReturnsOne()
        {
            FeatureFlags.EconomySystem = false;
            var station = TradeTestHelpers.MakeStation(food: 0f);
            float mod = TradeSystem.SupplyDemandModifier("food", isSellLine: true, station);
            Assert.AreEqual(1f, mod, 0.001f,
                "Supply/demand modifier should be 1.0 when EconomySystem flag is off.");
        }
    }

    // ── Reputation modifier tests ─────────────────────────────────────────────

    [TestFixture]
    public class ReputationModifierTests
    {
        [Test]
        public void SellLine_MaxPositiveRep_PriceIsLower()
        {
            var station = TradeTestHelpers.MakeStation();
            station.factionReputation["faction.a"] = 100f;
            float mod = TradeSystem.ReputationModifier("faction.a", isSellLine: true, station);
            Assert.Less(mod, 1f, "High rep should lower sell-line price (player buys cheaper).");
            Assert.AreEqual(1f - TradeSystem.RepMaxModifier, mod, 0.001f);
        }

        [Test]
        public void SellLine_MaxNegativeRep_PriceIsHigher()
        {
            var station = TradeTestHelpers.MakeStation();
            station.factionReputation["faction.a"] = -100f;
            float mod = TradeSystem.ReputationModifier("faction.a", isSellLine: true, station);
            Assert.Greater(mod, 1f, "Low rep should raise sell-line price (player pays more).");
            Assert.AreEqual(1f + TradeSystem.RepMaxModifier, mod, 0.001f);
        }

        [Test]
        public void SellLine_NeutralRep_ReturnsOne()
        {
            var station = TradeTestHelpers.MakeStation();
            station.factionReputation["faction.a"] = 0f;
            float mod = TradeSystem.ReputationModifier("faction.a", isSellLine: true, station);
            Assert.AreEqual(1f, mod, 0.001f, "Zero rep should give neutral modifier.");
        }

        [Test]
        public void BuyLine_MaxPositiveRep_PayoutIsHigher()
        {
            var station = TradeTestHelpers.MakeStation();
            station.factionReputation["faction.a"] = 100f;
            float mod = TradeSystem.ReputationModifier("faction.a", isSellLine: false, station);
            Assert.Greater(mod, 1f, "High rep should raise buy-line payout (player earns more).");
            Assert.AreEqual(1f + TradeSystem.RepMaxModifier, mod, 0.001f);
        }

        [Test]
        public void BuyLine_NullFaction_ReturnsOne()
        {
            var station = TradeTestHelpers.MakeStation();
            float mod = TradeSystem.ReputationModifier(null, isSellLine: false, station);
            Assert.AreEqual(1f, mod, 0.001f, "Null factionId should give neutral modifier.");
        }

        [Test]
        public void BuyLine_UnknownFaction_ReturnsOne()
        {
            var station = TradeTestHelpers.MakeStation();
            float mod = TradeSystem.ReputationModifier("faction.unknown", isSellLine: false, station);
            Assert.AreEqual(1f, mod, 0.001f, "Unknown faction (rep=0) should give neutral modifier.");
        }
    }

    // ── PlayerBuy persuasion modifier tests ───────────────────────────────────

    [TestFixture]
    public class PlayerBuyPersuasionTests
    {
        private ContentRegistry _registry;
        private GameObject      _registryGo;
        private TradeSystem     _trade;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("TradeTestRegistry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _trade      = new TradeSystem(_registry);
            FeatureFlags.TradePersuasionModifier = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
            FeatureFlags.TradePersuasionModifier = true;
        }

        [Test]
        public void PlayerBuy_NoPersuasion_PaysFullPrice()
        {
            var station = TradeTestHelpers.MakeStation(credits: 1000f);
            var offer   = TradeTestHelpers.MakeOffer(sellPrice: 5f, available: 10f);

            var (ok, _) = _trade.PlayerBuy(offer, "food", 10f, station, negotiationSkill: 3);

            Assert.IsTrue(ok);
            Assert.AreEqual(1000f - 50f, station.GetResource("credits"), 0.1f,
                "No persuasion bonus: 10 × 5.0 = 50 credits deducted.");
        }

        [Test]
        public void PlayerBuy_HighPersuasion_PaysLess()
        {
            var station = TradeTestHelpers.MakeStation(credits: 1000f);
            var offer   = TradeTestHelpers.MakeOffer(sellPrice: 5f, available: 10f);

            // skill 11 → discount = min(0.15, (11-3)*0.02) = 0.15 = 15%
            var (ok, _) = _trade.PlayerBuy(offer, "food", 10f, station, negotiationSkill: 11);

            Assert.IsTrue(ok);
            float expected = 1000f - (5f * 0.85f * 10f);
            Assert.AreEqual(expected, station.GetResource("credits"), 0.1f,
                "High persuasion: 15% discount should apply.");
        }

        [Test]
        public void PlayerBuy_PersuasionFlagOff_PaysFullPrice()
        {
            FeatureFlags.TradePersuasionModifier = false;
            var station = TradeTestHelpers.MakeStation(credits: 1000f);
            var offer   = TradeTestHelpers.MakeOffer(sellPrice: 5f, available: 10f);

            var (ok, _) = _trade.PlayerBuy(offer, "food", 10f, station, negotiationSkill: 11);

            Assert.IsTrue(ok);
            Assert.AreEqual(1000f - 50f, station.GetResource("credits"), 0.1f,
                "Flag off: no discount applied regardless of skill.");
        }
    }

    // ── PlayerSell persuasion modifier tests ──────────────────────────────────

    [TestFixture]
    public class PlayerSellPersuasionTests
    {
        private ContentRegistry _registry;
        private GameObject      _registryGo;
        private TradeSystem     _trade;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("TradeSellTestRegistry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _trade      = new TradeSystem(_registry);
            FeatureFlags.TradePersuasionModifier = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
            FeatureFlags.TradePersuasionModifier = true;
        }

        [Test]
        public void PlayerSell_NoPersuasion_ReceivesBasePrice()
        {
            var station = TradeTestHelpers.MakeStation(credits: 0f, parts: 100f);
            // offer: ship buys parts at 3.0/unit
            var offer = new TradeOffer { shipUid = "s1", shipName = "Seller" };
            offer.lines.Add(new TradeLine { resource = "parts", pricePerUnit = 3f, available = -20f });

            var (ok, _) = _trade.PlayerSell(offer, "parts", 10f, station, negotiationSkill: 3);

            Assert.IsTrue(ok);
            Assert.AreEqual(30f, station.GetResource("credits"), 0.1f,
                "No persuasion: 10 × 3.0 = 30 credits received.");
        }

        [Test]
        public void PlayerSell_HighPersuasion_ReceivesMore()
        {
            var station = TradeTestHelpers.MakeStation(credits: 0f, parts: 100f);
            var offer = new TradeOffer { shipUid = "s1", shipName = "Seller" };
            offer.lines.Add(new TradeLine { resource = "parts", pricePerUnit = 3f, available = -20f });

            // skill 11 → bonus = 0.15
            var (ok, _) = _trade.PlayerSell(offer, "parts", 10f, station, negotiationSkill: 11);

            Assert.IsTrue(ok);
            float expected = 3f * 1.15f * 10f;
            Assert.AreEqual(expected, station.GetResource("credits"), 0.1f,
                "High persuasion: 15% premium should apply.");
        }
    }

    // ── Standing order tests ──────────────────────────────────────────────────

    [TestFixture]
    public class StandingOrderTests
    {
        private ContentRegistry _registry;
        private GameObject      _registryGo;
        private TradeSystem     _trade;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("TradeStandingTestRegistry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();
            _trade      = new TradeSystem(_registry);
            FeatureFlags.TradeStandingOrders = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
            FeatureFlags.TradeStandingOrders = true;
        }

        [Test]
        public void StandingBuyOrder_PriceWithinLimit_ExecutesAutomatically()
        {
            var station = TradeTestHelpers.MakeStation(credits: 1000f, food: 0f);
            station.standingBuyOrders.Add(new StandingOrder
            {
                resource   = "food",
                limitPrice = 6f,   // max price willing to pay
                amount     = 10f
            });
            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "food", pricePerUnit = 5f, available = 20f });

            var msgs = _trade.ExecuteStandingOrders(ship, offer, station);

            Assert.AreEqual(1, msgs.Count, "One standing buy order should fire.");
            Assert.AreEqual(1000f - 50f, station.GetResource("credits"), 0.1f,
                "10 units × 5.0/unit = 50 credits deducted.");
            Assert.AreEqual(10f, station.GetResource("food"), 0.1f,
                "10 food units should be added to station inventory.");
        }

        [Test]
        public void StandingBuyOrder_PriceExceedsLimit_DoesNotExecute()
        {
            var station = TradeTestHelpers.MakeStation(credits: 1000f, food: 0f);
            station.standingBuyOrders.Add(new StandingOrder
            {
                resource   = "food",
                limitPrice = 4f,   // ship asks 5 > limit 4
                amount     = 10f
            });
            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "food", pricePerUnit = 5f, available = 20f });

            var msgs = _trade.ExecuteStandingOrders(ship, offer, station);

            Assert.AreEqual(0, msgs.Count, "Order should not execute when price exceeds limit.");
            Assert.AreEqual(1000f, station.GetResource("credits"), 0.1f);
        }

        [Test]
        public void StandingBuyOrder_InsufficientCredits_DoesNotExecute()
        {
            var station = TradeTestHelpers.MakeStation(credits: 10f, food: 0f);
            station.standingBuyOrders.Add(new StandingOrder
            {
                resource   = "food",
                limitPrice = 6f,
                amount     = 10f
            });
            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "food", pricePerUnit = 5f, available = 20f });

            var msgs = _trade.ExecuteStandingOrders(ship, offer, station);

            Assert.AreEqual(0, msgs.Count, "Order should not execute when credits insufficient.");
        }

        [Test]
        public void StandingSellOrder_PriceAtOrAboveMin_ExecutesAutomatically()
        {
            var station = TradeTestHelpers.MakeStation(credits: 0f, parts: 50f);
            station.standingSellOrders.Add(new StandingOrder
            {
                resource   = "parts",
                limitPrice = 3f,   // min acceptable price
                amount     = 10f
            });
            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "parts", pricePerUnit = 4f, available = -20f });

            var msgs = _trade.ExecuteStandingOrders(ship, offer, station);

            Assert.AreEqual(1, msgs.Count, "One standing sell order should fire.");
            Assert.AreEqual(40f, station.GetResource("credits"), 0.1f,
                "10 parts × 4.0/unit = 40 credits received.");
            Assert.AreEqual(40f, station.GetResource("parts"), 0.1f,
                "10 parts should be removed from station inventory.");
        }

        [Test]
        public void StandingSellOrder_PriceBelowMin_DoesNotExecute()
        {
            var station = TradeTestHelpers.MakeStation(credits: 0f, parts: 50f);
            station.standingSellOrders.Add(new StandingOrder
            {
                resource   = "parts",
                limitPrice = 5f,   // ship pays 4 < min 5
                amount     = 10f
            });
            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "parts", pricePerUnit = 4f, available = -20f });

            var msgs = _trade.ExecuteStandingOrders(ship, offer, station);

            Assert.AreEqual(0, msgs.Count, "Order should not execute when price is below minimum.");
        }

        [Test]
        public void StandingAndManualOrders_BothExecuteWithoutConflict()
        {
            var station = TradeTestHelpers.MakeStation(credits: 1000f, food: 50f);
            station.standingBuyOrders.Add(new StandingOrder
            {
                resource   = "food",
                limitPrice = 6f,
                amount     = 5f     // auto-buy 5 units
            });

            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "food", pricePerUnit = 5f, available = 20f });

            // Execute standing order first (auto)
            _trade.ExecuteStandingOrders(ship, offer, station);

            // Then manual purchase of 5 more units
            var (ok, _) = _trade.PlayerBuy(offer, "food", 5f, station, negotiationSkill: 3);

            Assert.IsTrue(ok, "Manual purchase should succeed after standing order.");
            Assert.AreEqual(10f, station.GetResource("food") - 50f, 0.1f,
                "Station should have gained 10 food total (5 auto + 5 manual).");
            Assert.AreEqual(1000f - 25f - 25f, station.GetResource("credits"), 0.1f,
                "Total cost: 10 × 5 = 50 credits.");
        }

        [Test]
        public void StandingOrders_FeatureFlagOff_DoNotExecute()
        {
            FeatureFlags.TradeStandingOrders = false;
            var station = TradeTestHelpers.MakeStation(credits: 1000f, food: 0f);
            station.standingBuyOrders.Add(new StandingOrder
            {
                resource   = "food",
                limitPrice = 6f,
                amount     = 10f
            });
            var ship  = TradeTestHelpers.MakeShip();
            var offer = new TradeOffer { shipUid = ship.uid, shipName = ship.name };
            offer.lines.Add(new TradeLine { resource = "food", pricePerUnit = 5f, available = 20f });

            var msgs = _trade.ExecuteStandingOrders(ship, offer, station);

            Assert.AreEqual(0, msgs.Count, "No standing orders should execute when flag is off.");
            Assert.AreEqual(1000f, station.GetResource("credits"), 0.1f);
        }
    }
}
