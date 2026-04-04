// TraderDispatchSystem — evaluates dispatch conditions and generates trade routes (WO-FAC-006).
// Runs on TickScheduler Channel 4 (Weekly), after FactionEconomySystem.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class TraderDispatchSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const float MinMarginThreshold = 0.15f;   // 15% above cost+fuel
        private const float FuelCostPerSector  = 5f;
        private const int   MaxRouteStops      = 3;
        private const float HostileRepThreshold = -50f;

        // ── Dependencies ──────────────────────────────────────────────────────
        private FactionEconomySystem _economy;
        private BroadcastNetwork     _broadcast;

        // ── Pending dispatches (consumed by VisitorSystem) ────────────────────
        private readonly List<PendingDispatch> _pendingDispatches = new List<PendingDispatch>();

        public struct PendingDispatch
        {
            public string          factionId;
            public string          destinationStationId;
            public TraderManifest  manifest;
            public List<RouteLeg>  route;
            public int             dispatchTick;
        }

        // ── Init ──────────────────────────────────────────────────────────────

        public void SetDependencies(FactionEconomySystem economy, BroadcastNetwork broadcast)
        {
            _economy   = economy;
            _broadcast = broadcast;
        }

        // ── Weekly Tick ───────────────────────────────────────────────────────

        /// <summary>Evaluate dispatch conditions for all factions.</summary>
        public void EvaluateDispatches(StationState station)
        {
            if (_economy == null || _broadcast == null) return;

            _pendingDispatches.Clear();

            foreach (var profile in _economy.AllProfiles)
            {
                EvaluateFaction(profile, station);
            }
        }

        private void EvaluateFaction(FactionEconomyProfile profile, StationState station)
        {
            // 1. Must have available trader ships
            int available = profile.traderFleetSize - profile.tradersInTransit;
            if (available <= 0) return;

            // 2. Get known broadcasts
            var broadcasts = _broadcast.GetKnownBroadcasts(profile.factionId, station.tick);
            if (broadcasts.Count == 0) return;

            // 3. For each reachable destination, evaluate margin
            foreach (var dest in broadcasts)
            {
                // Skip own station
                if (dest.factionId == profile.factionId) continue;

                // 5. Reputation check — not Hostile
                float rep = 0f;
                if (station.factionReputation != null)
                    station.factionReputation.TryGetValue(profile.factionId, out rep);
                // For NPC-to-NPC trade evaluation we use a simplified rep check
                // For player station, use actual rep
                if (dest.stationId == "player_station" && rep < HostileRepThreshold)
                    continue;

                // 3. Check margin on surplus goods vs destination buy prices
                var route = CalculateRoute(profile, dest, broadcasts);
                if (route == null || route.Count == 0) continue;

                float totalMargin = 0f;
                foreach (var leg in route)
                    totalMargin += leg.expectedMargin - leg.fuelCost;

                if (totalMargin <= 0f) continue;

                // Generate manifest and queue dispatch
                var manifest = GenerateManifest(profile, dest);
                if (manifest.cargoOutbound.Count == 0 && manifest.wantList.Count == 0)
                    continue;

                _pendingDispatches.Add(new PendingDispatch
                {
                    factionId = profile.factionId,
                    destinationStationId = dest.stationId,
                    manifest = manifest,
                    route = route,
                    dispatchTick = station.tick
                });

                profile.tradersInTransit++;
                available--;
                if (available <= 0) break;
            }
        }

        // ── Route Calculation ─────────────────────────────────────────────────

        /// <summary>
        /// Greedy best-first search for up to 3 stops maximising total margin.
        /// </summary>
        public List<RouteLeg> CalculateRoute(FactionEconomyProfile profile,
            PriceBroadcast primaryDest, List<PriceBroadcast> allBroadcasts)
        {
            var route = new List<RouteLeg>();

            // Primary leg
            float primaryMargin = CalculateMargin(profile, primaryDest);
            float primaryFuel = FuelCostPerSector; // simplified: 1 sector assumed
            route.Add(new RouteLeg
            {
                stationId = primaryDest.stationId,
                factionId = primaryDest.factionId,
                expectedMargin = primaryMargin,
                fuelCost = primaryFuel
            });

            if (primaryMargin - primaryFuel <= 0f)
                return null; // Not worth the trip

            // Try adding a second stop (greedy)
            if (allBroadcasts.Count > 1)
            {
                float bestSecondMargin = 0f;
                PriceBroadcast bestSecond = null;

                foreach (var b in allBroadcasts)
                {
                    if (b.stationId == primaryDest.stationId) continue;
                    if (b.factionId == profile.factionId) continue;

                    float margin = CalculateMargin(profile, b);
                    float fuel = FuelCostPerSector;
                    if (margin - fuel > bestSecondMargin)
                    {
                        bestSecondMargin = margin - fuel;
                        bestSecond = b;
                    }
                }

                if (bestSecond != null && bestSecondMargin > 0f)
                {
                    route.Add(new RouteLeg
                    {
                        stationId = bestSecond.stationId,
                        factionId = bestSecond.factionId,
                        expectedMargin = bestSecondMargin + FuelCostPerSector,
                        fuelCost = FuelCostPerSector
                    });
                }
            }

            return route;
        }

        private float CalculateMargin(FactionEconomyProfile seller, PriceBroadcast buyer)
        {
            float totalMargin = 0f;

            // Check surplus goods we can sell at the destination
            foreach (var kv in seller.surplus)
            {
                if (kv.Value <= 0f) continue;
                float ourSellPrice = seller.sellPrices.Get(kv.Key);
                float theirBuyPrice = buyer.buyPrices.Get(kv.Key);

                if (theirBuyPrice <= 0f) continue;
                float margin = (theirBuyPrice - ourSellPrice) / ourSellPrice;
                if (margin > MinMarginThreshold)
                    totalMargin += margin * Mathf.Min(kv.Value, 50f); // cap per-resource contribution
            }

            // Check deficit goods we can buy at the destination
            foreach (var kv in seller.deficit)
            {
                if (kv.Value <= 0f) continue;
                float ourBuyPrice = seller.buyPrices.Get(kv.Key);
                float theirSellPrice = buyer.sellPrices.Get(kv.Key);

                if (theirSellPrice <= 0f || ourBuyPrice <= 0f) continue;
                float margin = (ourBuyPrice - theirSellPrice) / theirSellPrice;
                if (margin > MinMarginThreshold)
                    totalMargin += margin * Mathf.Min(kv.Value, 50f);
            }

            return totalMargin;
        }

        // ── Manifest Generation ───────────────────────────────────────────────

        /// <summary>Generate a trade manifest from faction surplus/deficit vs destination.</summary>
        public TraderManifest GenerateManifest(FactionEconomyProfile profile, PriceBroadcast dest)
        {
            var manifest = new TraderManifest();
            manifest.creditReserve = Mathf.Max(200f, profile.economicHealth * 1000f);

            // Outbound cargo from surplus
            foreach (var kv in profile.surplus)
            {
                if (kv.Value <= 5f) continue;
                float destBuyPrice = dest.buyPrices.Get(kv.Key);
                if (destBuyPrice <= 0f) continue;

                int qty = Mathf.Min((int)kv.Value, 100);
                manifest.cargoOutbound.Add(new CargoEntry
                {
                    resourceId = kv.Key,
                    quantity = qty,
                    askPrice = profile.sellPrices.Get(kv.Key)
                });
            }

            // Want list from deficit
            foreach (var kv in profile.deficit)
            {
                if (kv.Value <= 5f) continue;
                float destSellPrice = dest.sellPrices.Get(kv.Key);
                if (destSellPrice <= 0f) continue;

                int qty = Mathf.Min((int)kv.Value, 80);
                manifest.wantList.Add(new WantEntry
                {
                    resourceId = kv.Key,
                    quantity = qty,
                    maxBid = profile.buyPrices.Get(kv.Key)
                });
            }

            return manifest;
        }

        // ── Public Accessors ──────────────────────────────────────────────────

        /// <summary>Consume pending dispatches (called by VisitorSystem to spawn ships).</summary>
        public List<PendingDispatch> ConsumePendingDispatches()
        {
            var result = new List<PendingDispatch>(_pendingDispatches);
            _pendingDispatches.Clear();
            return result;
        }

        /// <summary>Notify that a trader has returned (decrement in-transit count).</summary>
        public void OnTraderReturned(string factionId)
        {
            if (_economy == null) return;
            var profile = _economy.GetProfile(factionId);
            if (profile != null)
                profile.tradersInTransit = Math.Max(0, profile.tradersInTransit - 1);
        }
    }
}
