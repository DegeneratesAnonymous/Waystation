// TradeExecutor — transaction resolution against manifests and standing orders (WO-FAC-007).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class TradeExecutor
    {
        // ── Dependencies ──────────────────────────────────────────────────────
        private TradeSystem _tradeSystem;

        public void SetTradeSystem(TradeSystem ts) => _tradeSystem = ts;

        // ── Trade Execution ───────────────────────────────────────────────────

        /// <summary>
        /// Execute trade between a visitor ship's manifest and the station's standing orders.
        /// Returns a TradeResult with all goods exchanged and credits transferred.
        /// </summary>
        public TradeResult ExecuteTrade(ShipInstance ship, TraderManifest manifest,
            StationState station, List<string> relayChain = null)
        {
            var result = new TradeResult
            {
                traderShipUid = ship.uid,
                factionId = ship.factionId,
                relayChain = relayChain ?? new List<string>()
            };

            // 1. Trader sells to station (trader's outbound cargo → station buys)
            foreach (var cargo in manifest.cargoOutbound)
            {
                float stationCredits = station.GetResource("credits");
                if (stationCredits <= 0f) break;

                // Check if station has a buy price set (or auto-set from standing orders)
                float stationBuyPrice = GetStationBuyPrice(cargo.resourceId, station);
                if (stationBuyPrice <= 0f) continue;

                // Check margin: station's buy price must be >= trader's ask price × maxSellDiscount
                if (stationBuyPrice < cargo.askPrice * manifest.maxSellDiscount)
                    continue;

                int qty = cargo.quantity;
                float totalCost = qty * stationBuyPrice;

                // Cap by station credits
                if (totalCost > stationCredits)
                {
                    qty = Mathf.FloorToInt(stationCredits / stationBuyPrice);
                    totalCost = qty * stationBuyPrice;
                    result.partial = true;
                }
                if (qty <= 0) continue;

                // Execute: station pays credits, receives goods
                station.ModifyResource("credits", -totalCost);
                station.ModifyResource(cargo.resourceId, qty);
                result.goodsBought[cargo.resourceId] = qty;
                result.creditsSpent += totalCost;
            }

            // 2. Trader buys from station (trader's want list → station sells)
            foreach (var want in manifest.wantList)
            {
                float available = station.GetResource(want.resourceId);
                if (available <= 0f) continue;

                float stationSellPrice = GetStationSellPrice(want.resourceId, station);
                if (stationSellPrice <= 0f) continue;

                // Check margin: trader's max bid must be >= station's sell price × minBuyMargin
                float minAcceptable = stationSellPrice * (1f + manifest.minBuyMargin);
                if (want.maxBid < minAcceptable) continue;

                int qty = Mathf.Min(want.quantity, Mathf.FloorToInt(available));

                // Cap by trader's credit reserve
                float totalRevenue = qty * stationSellPrice;
                if (totalRevenue > manifest.creditReserve)
                {
                    qty = Mathf.FloorToInt(manifest.creditReserve / stationSellPrice);
                    totalRevenue = qty * stationSellPrice;
                    result.partial = true;
                }
                if (qty <= 0) continue;

                // Execute: station receives credits, ships goods
                station.ModifyResource(want.resourceId, -qty);
                station.ModifyResource("credits", totalRevenue);
                manifest.creditReserve -= totalRevenue;
                result.goodsSold[want.resourceId] = qty;
                result.creditsEarned += totalRevenue;
            }

            return result;
        }

        // ── Price Queries ─────────────────────────────────────────────────────

        private float GetStationBuyPrice(string resourceId, StationState station)
        {
            // If trade system has standing orders, use those prices
            // Otherwise, use TradeSystem base prices with modifiers
            if (_tradeSystem != null)
            {
                // Use the existing price logic from TradeSystem
                float basePrice = GetBasePrice(resourceId);
                float sdMod = TradeSystem.SupplyDemandModifier(resourceId, false, station);
                return basePrice * sdMod;
            }
            return GetBasePrice(resourceId);
        }

        private float GetStationSellPrice(string resourceId, StationState station)
        {
            if (_tradeSystem != null)
            {
                float basePrice = GetBasePrice(resourceId);
                float sdMod = TradeSystem.SupplyDemandModifier(resourceId, true, station);
                return basePrice * sdMod;
            }
            return GetBasePrice(resourceId) * 1.2f;
        }

        private static float GetBasePrice(string resourceId)
        {
            switch (resourceId)
            {
                case "food":   return 4f;
                case "parts":  return 8f;
                case "oxygen": return 3f;
                case "ice":    return 2f;
                case "fuel":   return 6f;
                case "power":  return 5f;
                default:       return 10f;
            }
        }
    }
}
