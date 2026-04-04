// EconomyModels — data structures for the demand-driven faction economy (WO-FAC-006).
using System.Collections.Generic;

namespace Waystation.Systems
{
    /// <summary>Dictionary wrapper mapping resource IDs to float quantities.</summary>
    public class ResourceMap : Dictionary<string, float>
    {
        public ResourceMap() : base() { }
        public ResourceMap(IDictionary<string, float> source) : base(source) { }

        public float Get(string key) => TryGetValue(key, out float v) ? v : 0f;
        public new void Add(string key, float amount)
        {
            if (ContainsKey(key)) this[key] += amount;
            else this[key] = amount;
        }
        public void Subtract(string key, float amount)
        {
            if (ContainsKey(key)) this[key] = System.Math.Max(0f, this[key] - amount);
        }
    }

    /// <summary>Dictionary wrapper mapping resource IDs to price values.</summary>
    public class PriceMap : Dictionary<string, float>
    {
        public PriceMap() : base() { }
        public PriceMap(IDictionary<string, float> source) : base(source) { }

        public float Get(string key) => TryGetValue(key, out float v) ? v : 0f;
    }

    /// <summary>
    /// Aggregate economy profile for a single faction, updated on the weekly tick.
    /// Not a full citizen simulation — aggregate supply/demand values that drift.
    /// </summary>
    public class FactionEconomyProfile
    {
        public string      factionId;
        public ResourceMap production   = new ResourceMap();   // units/week
        public ResourceMap consumption  = new ResourceMap();   // units/week
        public ResourceMap stockpile    = new ResourceMap();   // current holdings
        public ResourceMap deficit      = new ResourceMap();   // shortfall
        public ResourceMap surplus      = new ResourceMap();   // excess above buffer
        public PriceMap    buyPrices    = new PriceMap();      // what they'll pay
        public PriceMap    sellPrices   = new PriceMap();      // what they'll sell for
        public float       economicHealth = 1.0f;              // 0.0–1.0
        public int         traderFleetSize  = 3;
        public int         tradersInTransit = 0;

        /// <summary>Buffer threshold = 4 weeks of consumption per resource.</summary>
        public const float BufferWeeks = 4f;
    }

    /// <summary>Single cargo entry on a trader manifest — goods to sell.</summary>
    public struct CargoEntry
    {
        public string resourceId;
        public int    quantity;
        public float  askPrice;
    }

    /// <summary>Single want-list entry — goods a trader wants to buy.</summary>
    public struct WantEntry
    {
        public string resourceId;
        public int    quantity;
        public float  maxBid;
    }

    /// <summary>
    /// Manifest generated on dispatch: what the trader brings, wants, and can spend.
    /// </summary>
    public class TraderManifest
    {
        public List<CargoEntry> cargoOutbound = new List<CargoEntry>();
        public List<WantEntry>  wantList      = new List<WantEntry>();
        public float            creditReserve = 500f;
        public float            minBuyMargin  = 0.15f;
        public float            maxSellDiscount = 0.85f;
    }

    /// <summary>
    /// Price broadcast packet sent by a station on the daily tick.
    /// Cached by receiving stations; stale after 7 days (168 ticks at 24/day).
    /// </summary>
    public class PriceBroadcast
    {
        public string       stationId;
        public string       factionId;
        public int          timestamp;           // tick when sent
        public PriceMap     buyPrices  = new PriceMap();
        public PriceMap     sellPrices = new PriceMap();
        public List<string> surplusFlags = new List<string>();
        public List<string> deficitFlags = new List<string>();
        public int          dockingBaysAvailable;
        public float        reputationMinimum;
        public List<string> relayChain = new List<string>();  // station IDs relayed through
        public List<StationQuestEntry> pendingQuests = new List<StationQuestEntry>();

        /// <summary>Broadcasts older than this many ticks are stale.</summary>
        public const int StalenessThresholdTicks = 168; // 7 days × 24 ticks/day
        /// <summary>Max relay hops before a broadcast is too stale to re-relay.</summary>
        public const int MaxRelayHops = 2;

        public bool IsStale(int currentTick) => (currentTick - timestamp) > StalenessThresholdTicks;
    }

    /// <summary>Lightweight quest entry injected into broadcast packets.</summary>
    public class StationQuestEntry
    {
        public string questId;
        public string questType;         // import, export, supply, infrastructure, escort, diplomatic
        public string factionId;
        public string summary;
        public string fullTerms;         // null unless tier 3 visibility
        public int    expiryTick;
        public string rewardPreview;
        public int    requiredTier = 1;  // 1/2/3 — visibility gating by CommArray tier
        // Quest fulfilment tracking (used by StationQuestSystem)
        public string resource;
        public int    quantity;
        public float  pricePerUnit;
        public int    quantityFulfilled;
        public bool   fulfilled;
        public int    createdTick;
    }

    /// <summary>Route leg for multi-stop trader routing.</summary>
    public struct RouteLeg
    {
        public string stationId;
        public string factionId;
        public float  expectedMargin;
        public float  fuelCost;
    }

    /// <summary>Result of a trade execution step.</summary>
    public class TradeResult
    {
        public string traderShipUid;
        public string factionId;
        public List<string> relayChain = new List<string>();
        public Dictionary<string, int>   goodsBought = new Dictionary<string, int>();
        public Dictionary<string, int>   goodsSold   = new Dictionary<string, int>();
        public float creditsSpent;
        public float creditsEarned;
        public bool  partial;
    }
}
