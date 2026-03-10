// Trade System — generates trade manifests for docked ships and handles
// player buy/sell interactions.
//
// When a ship with trade or smuggle intent docks, the TradeSystem generates
// a TradeOffer recording what the ship sells/buys and at what prices.
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

            var lines = new List<TradeLine>();

            // Selling lines
            foreach (var resource in sellResources)
            {
                float basePrice = BasePrices.ContainsKey(resource) ? BasePrices[resource] : 5f;
                float markup    = UnityEngine.Random.Range(MinSellMarkup, MaxSellMarkup) * pressureMod;
                float price     = (float)Math.Round(basePrice * markup, 1);
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
                float  price     = (float)Math.Round(basePrice * discount, 1);
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

            float discount      = Mathf.Min(0.15f, Mathf.Max(0f, (negotiationSkill - 3) * 0.02f));
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

            float bonus         = Mathf.Min(0.15f, Mathf.Max(0f, (negotiationSkill - 3) * 0.02f));
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

        // ── Helpers ───────────────────────────────────────────────────────────

        public int BestNegotiatorSkill(StationState station)
        {
            int best = 0;
            foreach (var n in station.GetCrew())
            {
                int skill = n.skills.ContainsKey("negotiation") ? n.skills["negotiation"] : 0;
                if (skill > best) best = skill;
            }
            return best;
        }
    }
}
