// BroadcastNetwork — information layer connecting station economies (WO-FAC-006).
// Sends and receives price broadcasts; manages staleness and relay chain.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class BroadcastNetwork
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public const int BroadcastIntervalTicks = 24;   // daily
        public const int StalenessThreshold     = 168;  // 7 days × 24 ticks/day
        public const int MaxRelayHops           = 2;

        // CommArray tier → sector range
        private static readonly int[] TierRange = { 0, 1, 3, 7 };

        // ── State ─────────────────────────────────────────────────────────────
        /// <summary>Cached broadcasts keyed by origin stationId.</summary>
        private readonly Dictionary<string, PriceBroadcast> _broadcasts
            = new Dictionary<string, PriceBroadcast>();

        /// <summary>Active relay agreements: relay stationId → player station.</summary>
        private readonly HashSet<string> _activeRelays = new HashSet<string>();

        private int _lastBroadcastTick;

        // ── Broadcast Sending ─────────────────────────────────────────────────

        /// <summary>Send the player station's price broadcast on the daily tick.</summary>
        public void SendPlayerBroadcast(StationState station, PriceMap playerBuyPrices,
            PriceMap playerSellPrices, ResourceMap playerSurplus, ResourceMap playerDeficit)
        {
            if (station.tick - _lastBroadcastTick < BroadcastIntervalTicks
                && _lastBroadcastTick > 0)
                return;

            _lastBroadcastTick = station.tick;

            var broadcast = new PriceBroadcast
            {
                stationId = "player_station",
                factionId = "player",
                timestamp = station.tick,
                dockingBaysAvailable = CountDockingBays(station),
                reputationMinimum = -20f
            };

            // Only include resources with player-set prices
            foreach (var kv in playerBuyPrices)
                if (kv.Value > 0f) broadcast.buyPrices[kv.Key] = kv.Value;
            foreach (var kv in playerSellPrices)
                if (kv.Value > 0f) broadcast.sellPrices[kv.Key] = kv.Value;

            foreach (var kv in playerSurplus)
                if (kv.Value > 0f) broadcast.surplusFlags.Add(kv.Key);
            foreach (var kv in playerDeficit)
                if (kv.Value > 0f) broadcast.deficitFlags.Add(kv.Key);

            _broadcasts["player_station"] = broadcast;
        }

        /// <summary>Receive and cache a broadcast from an NPC station.</summary>
        public void ReceiveBroadcast(PriceBroadcast broadcast)
        {
            if (broadcast == null) return;
            _broadcasts[broadcast.stationId] = broadcast;
        }

        /// <summary>Relay a broadcast from a relay station. Adds relay chain annotation.</summary>
        public bool RelayBroadcast(PriceBroadcast original, string relayStationId)
        {
            if (original == null) return false;
            if (original.relayChain.Count >= MaxRelayHops) return false;

            var relayed = new PriceBroadcast
            {
                stationId = original.stationId,
                factionId = original.factionId,
                timestamp = original.timestamp,
                buyPrices = new PriceMap(original.buyPrices),
                sellPrices = new PriceMap(original.sellPrices),
                surplusFlags = new List<string>(original.surplusFlags),
                deficitFlags = new List<string>(original.deficitFlags),
                dockingBaysAvailable = original.dockingBaysAvailable,
                reputationMinimum = original.reputationMinimum,
                relayChain = new List<string>(original.relayChain),
                pendingQuests = new List<StationQuestEntry>(original.pendingQuests)
            };
            relayed.relayChain.Add(relayStationId);

            _broadcasts[original.stationId] = relayed;
            return true;
        }

        // ── Staleness Management ──────────────────────────────────────────────

        /// <summary>Remove broadcasts older than 7 days.</summary>
        public void PurgeStale(int currentTick)
        {
            var stale = new List<string>();
            foreach (var kv in _broadcasts)
            {
                if (kv.Value.IsStale(currentTick))
                    stale.Add(kv.Key);
            }
            foreach (var key in stale)
                _broadcasts.Remove(key);
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>Get all non-stale broadcasts known to a faction.</summary>
        public List<PriceBroadcast> GetKnownBroadcasts(string factionId, int currentTick)
        {
            var results = new List<PriceBroadcast>();
            foreach (var kv in _broadcasts)
            {
                if (!kv.Value.IsStale(currentTick))
                    results.Add(kv.Value);
            }
            return results;
        }

        /// <summary>Get a specific station's broadcast if available and fresh.</summary>
        public PriceBroadcast GetBroadcast(string stationId, int currentTick)
        {
            if (_broadcasts.TryGetValue(stationId, out var b) && !b.IsStale(currentTick))
                return b;
            return null;
        }

        /// <summary>Get the CommArray tier for the player station (1/2/3).</summary>
        public static int GetCommArrayTier(StationState station)
        {
            if (station.activeTags == null) return 1;
            if (station.activeTags.Contains("tech.comms_tier_3")) return 3;
            if (station.activeTags.Contains("tech.comms_tier_2")) return 2;
            return 1;
        }

        /// <summary>Get broadcast range in sectors for the player station.</summary>
        public static int GetBroadcastRange(StationState station)
        {
            int tier = GetCommArrayTier(station);
            return tier >= 0 && tier < TierRange.Length ? TierRange[tier] : 1;
        }

        /// <summary>Check if a station is within broadcast range of the player (Chebyshev distance).</summary>
        public static bool IsInBroadcastRange(StationState station, int targetSectorCol, int targetSectorRow)
        {
            // Player station is at sector (0,0) by convention
            int range = GetBroadcastRange(station);
            return Math.Abs(targetSectorCol) <= range && Math.Abs(targetSectorRow) <= range;
        }

        // ── Relay Agreement Management ────────────────────────────────────────

        public void RegisterRelay(string relayStationId) => _activeRelays.Add(relayStationId);
        public void RemoveRelay(string relayStationId) => _activeRelays.Remove(relayStationId);
        public IReadOnlyCollection<string> ActiveRelays => _activeRelays;

        // ── Daily Tick ────────────────────────────────────────────────────────

        /// <summary>Called on the daily cycle (Channel 2). Purges stale broadcasts.</summary>
        public void TickDaily(int currentTick)
        {
            PurgeStale(currentTick);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private int CountDockingBays(StationState station)
        {
            int count = 0;
            if (station.landingPads != null)
                count = station.landingPads.Count;
            return count;
        }

        public IReadOnlyDictionary<string, PriceBroadcast> AllBroadcasts => _broadcasts;
    }
}
