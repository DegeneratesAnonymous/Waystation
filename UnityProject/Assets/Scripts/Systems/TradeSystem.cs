// Trade System — generates trade manifests for docked ships and handles
// player buy/sell interactions.
//
// When a ship with trade or smuggle intent docks, the TradeSystem generates
// a TradeOffer recording what the ship sells/buys and at what prices.
//
// Price formula (per trade line):
//   finalPrice = basePrice × supplyDemandModifier × reputationModifier
//
// supplyDemandModifier:
//   Shortage (station stock < ShortageThreshold): × ShortagePremiumFactor (>1) on sell lines
//   Surplus  (station stock > SurplusThreshold):  × SurplusDiscountFactor (<1) on buy lines
//   Neutral: × 1.0
//
// reputationModifier:
//   Sell lines (player buys):  1.0 − (rep/100) × RepMaxModifier  (lower price at high rep)
//   Buy lines  (player sells): 1.0 + (rep/100) × RepMaxModifier  (higher payout at high rep)
//
// Persuasion modifier (applied during PlayerBuy / PlayerSell transactions):
//   Gated by FeatureFlags.TradePersuasionModifier.
//   Uses the best crew member's skill.persuasion level; max ±15% per transaction.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Trade line item ───────────────────────────────────────────────────────

    public class TradeLine
    {
        public string resource;
        public float  pricePerUnit;
        public float  available;    // > 0 means ship is selling, < 0 means ship wants to buy

        public bool IsSelling => available > 0f;
        public bool IsBuying  => available < 0f;
    }

    // ── Trade Offer ───────────────────────────────────────────────────────────

    public class TradeOffer
    {
        public string              shipUid;
        public string              shipName;
        public List<TradeLine>     lines   = new List<TradeLine>();
        public Dictionary<string, float> traded = new Dictionary<string, float>();

        public List<TradeLine> GetSellLines()
        {
            var r = new List<TradeLine>();
            foreach (var l in lines) if (l.IsSelling) r.Add(l);
            return r;
        }

        public List<TradeLine> GetBuyLines()
        {
            var r = new List<TradeLine>();
            foreach (var l in lines) if (l.IsBuying) r.Add(l);
            return r;
        }

        public TradeLine GetLine(string resource)
        {
            foreach (var l in lines) if (l.resource == resource) return l;
            return null;
        }
    }

    // ── Trade System ─────────────────────────────────────────────────────────

    public class TradeSystem
    {
        private readonly ContentRegistry _registry;

        private static readonly Dictionary<string, float> BasePrices = new Dictionary<string, float>
        {
            { "food", 4f }, { "parts", 8f }, { "oxygen", 3f }, { "ice", 2f }, { "power", 5f }
        };

        private const float MinSellMarkup  = 1.00f;
        private const float MaxSellMarkup  = 1.35f;
        private const float MinBuyDiscount = 0.55f;
        private const float MaxBuyDiscount = 0.85f;

        // ── Supply/demand constants ───────────────────────────────────────────

        /// <summary>Station stock below this level triggers shortage pricing.</summary>
        public const float ShortageThreshold   = 50f;
        /// <summary>Station stock above this level triggers surplus pricing.</summary>
        public const float SurplusThreshold    = 200f;
        /// <summary>Price multiplier applied to sell lines when station stock is short.</summary>
        public const float ShortagePremiumFactor = 1.25f;
        /// <summary>Price multiplier applied to buy lines when station stock is surplus.</summary>
        public const float SurplusDiscountFactor = 0.85f;

        // ── Reputation modifier constant ──────────────────────────────────────

        /// <summary>Maximum fraction of base price added/removed by faction reputation.</summary>
        public const float RepMaxModifier = 0.15f;

        // ── Persuasion modifier constant ──────────────────────────────────────

        public const string PersuasionSkillId = "skill.persuasion";

        private static readonly Dictionary<string, List<string>> RoleSells =
            new Dictionary<string, List<string>>
        {
            { "trader",    new List<string> { "food", "parts", "ice" } },
            { "smuggler",  new List<string> { "parts", "food" } },
            { "transport", new List<string> { "food", "ice" } },
            { "refugee",   new List<string>() },
            { "inspector", new List<string>() },
            { "raider",    new List<string>() },
            { "patrol",    new List<string>() }
        };

        public TradeSystem(ContentRegistry registry) => _registry = registry;

        // ── Supply/demand modifier ────────────────────────────────────────────

        /// <summary>
        /// Returns the supply/demand price modifier for <paramref name="resource"/>.
        /// <para>
        /// For sell lines (ship selling to player): if station has a shortage of this
        /// resource the ship charges a premium (returned multiplier &gt; 1).
        /// For buy lines (ship buying from player): if station has a surplus the ship
        /// pays a discount (returned multiplier &lt; 1).
        /// </para>
        /// Returns 1.0 when <see cref="FeatureFlags.EconomySystem"/> is false.
        /// </summary>
        /// <param name="resource">Resource key.</param>
        /// <param name="isSellLine">True when the ship is selling (player buys).</param>
        /// <param name="station">Current station state.</param>
        public static float SupplyDemandModifier(string resource, bool isSellLine, StationState station)
        {
            if (!FeatureFlags.EconomySystem) return 1f;

            float stock = station.GetResource(resource);
            if (isSellLine && stock < ShortageThreshold)
                return ShortagePremiumFactor;
            if (!isSellLine && stock > SurplusThreshold)
                return SurplusDiscountFactor;
            return 1f;
        }

        // ── Reputation modifier ───────────────────────────────────────────────

        /// <summary>
        /// Returns the reputation-based price modifier for a trade line.
        /// <para>
        /// Higher reputation with the ship's faction improves trade terms:
        /// sell lines get cheaper (multiplier &lt; 1) and buy lines pay more
        /// (multiplier &gt; 1).
        /// </para>
        /// </summary>
        /// <param name="factionId">Faction the ship belongs to.</param>
        /// <param name="isSellLine">True when the ship is selling (player buys).</param>
        /// <param name="station">Current station state.</param>
        public static float ReputationModifier(string factionId, bool isSellLine, StationState station)
        {
            if (string.IsNullOrEmpty(factionId)) return 1f;

            float rep = station.GetFactionRep(factionId);        // −100 … +100
            float factor = (rep / 100f) * RepMaxModifier;        // −0.15 … +0.15
            // Sell line: player pays; high rep → lower price → subtract factor.
            // Buy line:  player receives; high rep → better payout → add factor.
            return isSellLine ? (1f - factor) : (1f + factor);
        }

        // ── Offer generation ──────────────────────────────────────────────────

        public TradeOffer GenerateOffer(ShipInstance ship, StationState station)
        {
            if (!_registry.Ships.TryGetValue(ship.templateId, out var template)) return null;

            var nonTradingRoles = new HashSet<string> { "refugee", "inspector", "raider", "patrol" };
            if (nonTradingRoles.Contains(ship.role)) return null;

            if (!RoleSells.TryGetValue(ship.role, out var sellResources) || sellResources.Count == 0)
                return null;

            // Market pressure: extra traders already docked raise prices slightly
            int nTraders = 0;
            foreach (var s in station.GetDockedShips())
                if (s.uid != ship.uid && (s.role == "trader" || s.role == "smuggler" || s.role == "transport"))
                    nTraders++;
            float pressureMod = 1f + nTraders * 0.05f;

            float repSellMod = ReputationModifier(ship.factionId, isSellLine: true,  station);
            float repBuyMod  = ReputationModifier(ship.factionId, isSellLine: false, station);

            var lines = new List<TradeLine>();

            // Selling lines
            foreach (var resource in sellResources)
            {
                float basePrice = BasePrices.ContainsKey(resource) ? BasePrices[resource] : 5f;
                float markup    = UnityEngine.Random.Range(MinSellMarkup, MaxSellMarkup) * pressureMod;
                float sdMod     = SupplyDemandModifier(resource, isSellLine: true, station);
                float price     = (float)Math.Round(basePrice * markup * sdMod * repSellMod, 1);
                int   capacity  = template.cargoCapacity > 0 ? template.cargoCapacity : 20;
                int   amount    = UnityEngine.Random.Range(Mathf.Max(5, capacity / 4), capacity + 1);
                lines.Add(new TradeLine { resource = resource, pricePerUnit = price, available = amount });
            }

            // Buying lines — things the ship doesn't carry
            var buyPool = new List<string>();
            foreach (var r in BasePrices.Keys)
                if (!sellResources.Contains(r)) buyPool.Add(r);

            int nBuy = Mathf.Min(2, buyPool.Count);
            for (int i = buyPool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (buyPool[i], buyPool[j]) = (buyPool[j], buyPool[i]);
            }
            for (int i = 0; i < nBuy; i++)
            {
                string resource = buyPool[i];
                float  basePrice = BasePrices.ContainsKey(resource) ? BasePrices[resource] : 5f;
                float  discount  = UnityEngine.Random.Range(MinBuyDiscount, MaxBuyDiscount);
                float  sdMod     = SupplyDemandModifier(resource, isSellLine: false, station);
                float  price     = (float)Math.Round(basePrice * discount * sdMod * repBuyMod, 1);
                int    want      = UnityEngine.Random.Range(10, 41);
                lines.Add(new TradeLine { resource = resource, pricePerUnit = price, available = -want });
            }

            return new TradeOffer { shipUid = ship.uid, shipName = ship.name, lines = lines };
        }

        // ── Transaction execution ─────────────────────────────────────────────

        public (bool success, string message) PlayerBuy(TradeOffer offer, string resource,
                                                         float amount, StationState station,
                                                         int negotiationSkill = 3)
        {
            if (amount <= 0f)               return (false, "Amount must be greater than zero.");
            var line = offer.GetLine(resource);
            if (line == null || !line.IsSelling) return (false, $"{resource} is not available from this ship.");
            if (amount > line.available)         return (false, $"Only {line.available:F0} units available.");

            float discount      = FeatureFlags.TradePersuasionModifier
                ? Mathf.Min(0.15f, Mathf.Max(0f, (negotiationSkill - 3) * 0.02f))
                : 0f;
            float effectivePrice= line.pricePerUnit * (1f - discount);
            float totalCost     = effectivePrice * amount;

            if (station.GetResource("credits") < totalCost)
                return (false, $"Insufficient credits (need {totalCost:F0}, have {station.GetResource("credits"):F0}).");

            station.ModifyResource("credits", -totalCost);
            station.ModifyResource(resource,  amount);
            line.available -= amount;
            offer.traded[resource] = (offer.traded.ContainsKey(resource) ? offer.traded[resource] : 0f) + amount;

            string msg = $"Purchased {amount:F0} {resource} for {totalCost:F0} credits" +
                         (discount > 0f ? $" (negotiated {discount * 100f:F0}% off)" : "") + ".";
            station.LogEvent($"Trade: {msg}");
            return (true, msg);
        }

        public (bool success, string message) PlayerSell(TradeOffer offer, string resource,
                                                          float amount, StationState station,
                                                          int negotiationSkill = 3)
        {
            if (amount <= 0f)              return (false, "Amount must be greater than zero.");
            var line = offer.GetLine(resource);
            if (line == null || !line.IsBuying) return (false, $"This ship is not buying {resource}.");
            float want = Mathf.Abs(line.available);
            if (amount > want)                  return (false, $"Ship only wants {want:F0} units.");
            if (station.GetResource(resource) < amount)
                return (false, $"Insufficient {resource} on station.");

            float bonus         = FeatureFlags.TradePersuasionModifier
                ? Mathf.Min(0.15f, Mathf.Max(0f, (negotiationSkill - 3) * 0.02f))
                : 0f;
            float effectivePrice= line.pricePerUnit * (1f + bonus);
            float totalIncome   = effectivePrice * amount;

            station.ModifyResource(resource, -amount);
            station.ModifyResource("credits", totalIncome);
            line.available += amount;
            offer.traded[resource] = (offer.traded.ContainsKey(resource) ? offer.traded[resource] : 0f) + amount;

            string msg = $"Sold {amount:F0} {resource} for {totalIncome:F0} credits" +
                         (bonus > 0f ? $" (negotiated {bonus * 100f:F0}% premium)" : "") + ".";
            station.LogEvent($"Trade: {msg}");
            return (true, msg);
        }

        // ── Standing order execution ──────────────────────────────────────────

        /// <summary>
        /// Evaluates all standing buy and sell orders against <paramref name="offer"/>
        /// and executes any that match.  Called automatically from
        /// <see cref="VisitorSystem.AdmitShip"/> when
        /// <see cref="FeatureFlags.TradeStandingOrders"/> is enabled.
        /// Both manual transactions and standing orders may fire on the same offer
        /// without conflict — standing orders only consume the portion they need.
        /// </summary>
        /// <returns>Informational log messages for each executed order.</returns>
        public List<string> ExecuteStandingOrders(ShipInstance ship, TradeOffer offer,
                                                   StationState station)
        {
            var messages = new List<string>();
            if (!FeatureFlags.TradeStandingOrders) return messages;

            // Standing buy orders: station buys items FROM the ship
            foreach (var order in station.standingBuyOrders)
            {
                var line = offer.GetLine(order.resource);
                if (line == null || !line.IsSelling)         continue;
                if (line.pricePerUnit > order.limitPrice)    continue;

                float available  = line.available;
                float toBuy      = Mathf.Min(order.amount, available);
                if (toBuy <= 0f)                             continue;

                float totalCost  = line.pricePerUnit * toBuy;
                if (station.GetResource("credits") < totalCost) continue;

                station.ModifyResource("credits",    -totalCost);
                station.ModifyResource(order.resource, toBuy);
                line.available -= toBuy;
                offer.traded[order.resource] =
                    (offer.traded.ContainsKey(order.resource) ? offer.traded[order.resource] : 0f) + toBuy;

                string msg = $"Auto-bought {toBuy:F0} {order.resource} @ {line.pricePerUnit:F1}/unit " +
                             $"from {ship.name} for {totalCost:F0} credits.";
                station.LogEvent($"Standing order: {msg}");
                messages.Add(msg);
            }

            // Standing sell orders: station sells items TO the ship
            foreach (var order in station.standingSellOrders)
            {
                var line = offer.GetLine(order.resource);
                if (line == null || !line.IsBuying)          continue;
                if (line.pricePerUnit < order.limitPrice)    continue;

                float want       = Mathf.Abs(line.available);
                float toSell     = Mathf.Min(order.amount, want);
                toSell           = Mathf.Min(toSell, station.GetResource(order.resource));
                if (toSell <= 0f)                            continue;

                float totalIncome = line.pricePerUnit * toSell;
                station.ModifyResource(order.resource, -toSell);
                station.ModifyResource("credits",       totalIncome);
                line.available += toSell;
                offer.traded[order.resource] =
                    (offer.traded.ContainsKey(order.resource) ? offer.traded[order.resource] : 0f) + toSell;

                string msg = $"Auto-sold {toSell:F0} {order.resource} @ {line.pricePerUnit:F1}/unit " +
                             $"to {ship.name} for {totalIncome:F0} credits.";
                station.LogEvent($"Standing order: {msg}");
                messages.Add(msg);
            }

            return messages;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the highest <c>skill.persuasion</c> level among all current crew members.
        /// Falls back to the legacy <c>negotiation</c> key in <see cref="NPCInstance.skills"/>
        /// for backward compatibility with saves that pre-date the SkillSystem migration.
        /// </summary>
        public int BestNegotiatorSkill(StationState station)
        {
            int best = 0;
            foreach (var n in station.GetCrew())
            {
                int skill = SkillSystem.GetSkillLevel(n, PersuasionSkillId);
                if (skill == 0 && n.skills.ContainsKey("negotiation"))
                    skill = n.skills["negotiation"];
                if (skill > best) best = skill;
            }
            return best;
        }
    }
}
