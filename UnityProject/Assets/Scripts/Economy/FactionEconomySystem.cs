// FactionEconomySystem — weekly tick update for all faction economy profiles (WO-FAC-006).
// Runs on TickScheduler Channel 4 (Weekly).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class FactionEconomySystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const float DeficitPriceScale  = 0.8f;
        private const float SurplusPriceScale  = 0.6f;
        private const float WarConsumptionBoost = 0.4f;     // +40% military consumption
        private const float WarHealthDecay      = 0.15f;    // per week of war
        private const float LowMoodHealthDecay  = 0.05f;    // per week below mood 30
        private const float CorporateHealthBonus = 0.1f;
        private const float WarlordHealthPenalty = 0.1f;
        private const float MinEconomicHealth    = 0.1f;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly Dictionary<string, FactionEconomyProfile> _profiles
            = new Dictionary<string, FactionEconomyProfile>();
        private PriceMap _basePrices = new PriceMap();
        private bool _initialised;

        // ── Resource IDs ──────────────────────────────────────────────────────
        private static readonly string[] CoreResources =
            { "food", "parts", "oxygen", "ice", "fuel", "power", "credits" };

        // ── Init ──────────────────────────────────────────────────────────────

        public void LoadBasePrices(string json)
        {
            var root = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            if (root == null) return;
            if (root.TryGetValue("base_prices", out var bp) && bp is Dictionary<string, object> prices)
            {
                foreach (var kv in prices)
                    _basePrices[kv.Key] = Convert.ToSingle(kv.Value);
            }
        }

        /// <summary>Initialise economy profiles for all known factions.</summary>
        public void Initialise(StationState station)
        {
            if (_initialised) return;
            _initialised = true;

            foreach (var kv in station.factionReputation)
            {
                if (!_profiles.ContainsKey(kv.Key))
                    _profiles[kv.Key] = SeedProfile(kv.Key);
            }
            // Also check generatedFactions
            if (station.generatedFactions != null)
            {
                foreach (var kv in station.generatedFactions)
                {
                    if (!_profiles.ContainsKey(kv.Key))
                        _profiles[kv.Key] = SeedProfile(kv.Key);
                }
            }
        }

        private FactionEconomyProfile SeedProfile(string factionId)
        {
            var p = new FactionEconomyProfile { factionId = factionId };
            // Seed with neutral values based on base prices
            var rng = new System.Random(factionId.GetHashCode());
            foreach (var res in CoreResources)
            {
                if (res == "credits") continue; // factions don't produce/consume credits as a resource
                float baseProd = 20f + rng.Next(0, 30);
                float baseCons = 15f + rng.Next(0, 35);
                p.production[res]  = baseProd;
                p.consumption[res] = baseCons;
                p.stockpile[res]   = baseProd * 8f; // ~2 months buffer to start
            }
            p.traderFleetSize = 2 + rng.Next(0, 4);
            p.economicHealth  = 0.7f + (float)rng.NextDouble() * 0.3f;
            RecalculateDeficitSurplus(p);
            RecalculatePrices(p);
            return p;
        }

        // ── Weekly Tick ───────────────────────────────────────────────────────

        /// <summary>Called once per weekly tick (Channel 4). Updates all faction profiles.</summary>
        public void TickWeekly(StationState station)
        {
            Initialise(station);

            foreach (var kv in _profiles)
                TickFaction(kv.Value, station);

            RefreshPlayerProfile(station);
        }

        private void TickFaction(FactionEconomyProfile p, StationState station)
        {
            // 1. Add production to stockpile
            foreach (var kv in p.production)
                p.stockpile.Add(kv.Key, kv.Value);

            // 2. Subtract consumption from stockpile (floor at 0; shortfall → deficit)
            foreach (var kv in p.consumption)
                p.stockpile.Subtract(kv.Key, kv.Value);

            // 3. Apply faction condition modifiers to economic health
            ApplyConditionModifiers(p, station);

            // 4. Recalculate deficit/surplus
            RecalculateDeficitSurplus(p);

            // 5. Recalculate buy/sell prices
            RecalculatePrices(p);
        }

        private void RecalculateDeficitSurplus(FactionEconomyProfile p)
        {
            p.deficit.Clear();
            p.surplus.Clear();

            foreach (var res in CoreResources)
            {
                if (res == "credits") continue;
                float buffer = p.consumption.Get(res) * FactionEconomyProfile.BufferWeeks;
                float stock  = p.stockpile.Get(res);

                if (stock < buffer)
                    p.deficit[res] = buffer - stock;
                else
                    p.surplus[res] = stock - buffer;
            }
        }

        private void RecalculatePrices(FactionEconomyProfile p)
        {
            p.buyPrices.Clear();
            p.sellPrices.Clear();

            foreach (var res in CoreResources)
            {
                if (res == "credits") continue;
                float basePrice = _basePrices.Get(res);
                if (basePrice <= 0f) basePrice = 10f; // fallback

                float weeklyConsumption = p.consumption.Get(res);
                float deficitVal = p.deficit.Get(res);
                float surplusVal = p.surplus.Get(res);
                float stockpileVal = p.stockpile.Get(res);

                // DeficitRatio = Deficit / (Consumption × 4), clamped 0–1
                float deficitRatio = weeklyConsumption > 0f
                    ? Mathf.Clamp01(deficitVal / (weeklyConsumption * FactionEconomyProfile.BufferWeeks))
                    : 0f;

                // SurplusRatio = Surplus / Stockpile, clamped 0–1
                float surplusRatio = stockpileVal > 0f
                    ? Mathf.Clamp01(surplusVal / stockpileVal)
                    : 0f;

                // BuyPrice = Base × (1 + DeficitRatio × 0.8) × (2.0 - Health)
                p.buyPrices[res] = basePrice
                    * (1f + deficitRatio * DeficitPriceScale)
                    * (2f - p.economicHealth);

                // SellPrice = Base × (1 - SurplusRatio × 0.6) × Health
                p.sellPrices[res] = basePrice
                    * (1f - surplusRatio * SurplusPriceScale)
                    * p.economicHealth;
            }
        }

        private void ApplyConditionModifiers(FactionEconomyProfile p, StationState station)
        {
            // War: consumption of military resources +40%, health −0.15/week
            // (Check chain flags for war state)
            string warFlag = $"faction_war_{p.factionId}_active";
            if (station.chainFlags != null && station.chainFlags.TryGetValue(warFlag, out bool atWar) && atWar)
            {
                p.consumption["parts"] = p.consumption.Get("parts") * (1f + WarConsumptionBoost);
                p.consumption["fuel"]  = p.consumption.Get("fuel")  * (1f + WarConsumptionBoost);
                p.economicHealth = Mathf.Max(MinEconomicHealth, p.economicHealth - WarHealthDecay);
            }

            // Government type bonuses
            if (station.generatedFactions != null &&
                station.generatedFactions.TryGetValue(p.factionId, out var fdef))
            {
                if (fdef.ideologyTags != null)
                {
                    if (fdef.ideologyTags.Contains("mercantile"))
                        p.economicHealth = Mathf.Min(1f, p.economicHealth + CorporateHealthBonus);
                    if (fdef.ideologyTags.Contains("nomadic"))
                        p.economicHealth = Mathf.Max(MinEconomicHealth, p.economicHealth - WarlordHealthPenalty);
                }
            }

            // Clamp health
            p.economicHealth = Mathf.Clamp(p.economicHealth, MinEconomicHealth, 1f);
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public FactionEconomyProfile GetProfile(string factionId)
        {
            if (_profiles.TryGetValue(factionId, out var p)) return p;
            // Create a default profile on demand
            var newProfile = SeedProfile(factionId);
            _profiles[factionId] = newProfile;
            return newProfile;
        }

        /// <summary>Build a player station economy profile from current station state.</summary>
        public FactionEconomyProfile GetPlayerProfile(StationState station)
        {
            var p = new FactionEconomyProfile { factionId = "player" };
            // Stockpile from station resources
            foreach (var kv in station.resources)
            {
                if (kv.Key == "credits") continue;
                p.stockpile[kv.Key] = kv.Value;
            }
            // Production and consumption would come from ResourceSystem module effects
            // For now, estimate from stockpile changes (simplified)
            p.economicHealth = 1f;
            RecalculateDeficitSurplus(p);
            RecalculatePrices(p);
            return p;
        }

        public IEnumerable<FactionEconomyProfile> AllProfiles => _profiles.Values;
        public PriceMap BasePrices => _basePrices;

        // Cached player profile from last weekly tick
        private FactionEconomyProfile _cachedPlayerProfile;

        /// <summary>Get the player's economy profile (cached from last TickWeekly).</summary>
        public FactionEconomyProfile GetPlayerProfile() => _cachedPlayerProfile;

        /// <summary>Update the cached player profile (called during TickWeekly).</summary>
        public void RefreshPlayerProfile(StationState station)
        {
            _cachedPlayerProfile = GetPlayerProfile(station);
        }

        /// <summary>Load condition modifiers from JSON (currently informational — modifiers are hardcoded).</summary>
        public void LoadConditionModifiers(string json)
        {
            // Condition modifiers (war, disasters, etc.) are applied in ApplyConditionModifiers.
            // This JSON provides data-authoring for future extensibility; accepted but not
            // dynamically parsed yet.
            Debug.Log("[FactionEconomySystem] Condition modifiers loaded.");
        }
    }
}
