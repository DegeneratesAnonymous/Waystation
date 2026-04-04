// StationQuestSystem — quest generation, broadcast injection, expiry (WO-FAC-009).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Generates station quests based on deficits/surpluses and broadcasts them
    /// to attract traders. Quests expire and refresh on weekly ticks.
    /// </summary>
    public class StationQuestSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const int MaxActiveQuests = 5;
        private static readonly int QuestDurationTicks = TimeSystem.TicksPerDay * 14; // 14 days
        private const float MinDeficitForQuest = 0.3f;
        private const float MinSurplusForQuest = 0.4f;
        private const float QuestPriceBonus = 0.25f; // 25% price bonus for quest items
        private static readonly int RefreshCooldownTicks = TimeSystem.TicksPerDay * 7; // 7 days between refresh attempts

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<StationQuestEntry> _activeQuests = new List<StationQuestEntry>();
        private readonly List<StationQuestEntry> _completedQuests = new List<StationQuestEntry>();
        private int _nextQuestId;
        private int _lastRefreshTick;

        // ── Dependencies ──────────────────────────────────────────────────────
        private FactionEconomySystem _economy;
        private BroadcastNetwork _broadcastNetwork;

        /// <summary>Fired when a new quest is generated.</summary>
        public event Action<StationQuestEntry> OnQuestGenerated;
        /// <summary>Fired when a quest is fulfilled.</summary>
        public event Action<StationQuestEntry> OnQuestFulfilled;

        public void SetDependencies(FactionEconomySystem economy, BroadcastNetwork broadcastNetwork)
        {
            _economy = economy;
            _broadcastNetwork = broadcastNetwork;
        }

        // ── Weekly Tick ───────────────────────────────────────────────────────

        /// <summary>Generate and manage quests on the weekly tick.</summary>
        public void TickWeekly(StationState station)
        {
            int tick = station.tick;

            // Expire old quests
            for (int i = _activeQuests.Count - 1; i >= 0; i--)
            {
                if (tick >= _activeQuests[i].expiryTick)
                {
                    var expired = _activeQuests[i];
                    expired.fulfilled = false;
                    _completedQuests.Add(expired);
                    _activeQuests.RemoveAt(i);
                }
            }

            // Generate new quests if below cap and cooldown elapsed
            if (_activeQuests.Count < MaxActiveQuests &&
                tick - _lastRefreshTick >= RefreshCooldownTicks)
            {
                GenerateQuests(station, tick);
                _lastRefreshTick = tick;
            }

            // Inject active quests into broadcast
            InjectQuestsIntoBroadcast(station);
        }

        // ── Quest Generation ─────────────────────────────────────────────────

        private void GenerateQuests(StationState station, int tick)
        {
            var profile = _economy?.GetPlayerProfile();
            if (profile == null) return;

            int slotsAvailable = MaxActiveQuests - _activeQuests.Count;

            // Generate import quests from deficits
            foreach (var kv in profile.deficit)
            {
                if (slotsAvailable <= 0) break;
                if (kv.Value < MinDeficitForQuest) continue;

                // Don't duplicate existing quests for same resource
                if (_activeQuests.Any(q => q.resource == kv.Key && q.questType == "import"))
                    continue;

                int quantity = Mathf.CeilToInt(kv.Value * 100f); // scale deficit ratio to units
                float basePrice = _economy.BasePrices.ContainsKey(kv.Key) ? _economy.BasePrices[kv.Key] : 5f;
                float questPrice = basePrice * (1f + QuestPriceBonus);

                var quest = new StationQuestEntry
                {
                    questId = $"quest_{_nextQuestId++}",
                    questType = "import",
                    resource = kv.Key,
                    quantity = quantity,
                    pricePerUnit = questPrice,
                    expiryTick = tick + QuestDurationTicks,
                    createdTick = tick
                };
                _activeQuests.Add(quest);
                slotsAvailable--;
                OnQuestGenerated?.Invoke(quest);
            }

            // Generate export quests from surpluses
            foreach (var kv in profile.surplus)
            {
                if (slotsAvailable <= 0) break;
                if (kv.Value < MinSurplusForQuest) continue;

                if (_activeQuests.Any(q => q.resource == kv.Key && q.questType == "export"))
                    continue;

                int quantity = Mathf.CeilToInt(kv.Value * 80f);
                float basePrice = _economy.BasePrices.ContainsKey(kv.Key) ? _economy.BasePrices[kv.Key] : 5f;
                float questPrice = basePrice * (1f - QuestPriceBonus * 0.5f); // discount for exports

                var quest = new StationQuestEntry
                {
                    questId = $"quest_{_nextQuestId++}",
                    questType = "export",
                    resource = kv.Key,
                    quantity = quantity,
                    pricePerUnit = questPrice,
                    expiryTick = tick + QuestDurationTicks,
                    createdTick = tick
                };
                _activeQuests.Add(quest);
                slotsAvailable--;
                OnQuestGenerated?.Invoke(quest);
            }
        }

        // ── Quest Fulfilment ─────────────────────────────────────────────────

        /// <summary>
        /// Record partial or full fulfilment of a quest via a trade transaction.
        /// Returns the amount actually fulfilled.
        /// </summary>
        public int FulfilQuest(string questId, int amount)
        {
            var quest = _activeQuests.FirstOrDefault(q => q.questId == questId);
            if (quest == null) return 0;

            int remaining = quest.quantity - quest.quantityFulfilled;
            int fulfilled = Mathf.Min(amount, remaining);
            quest.quantityFulfilled += fulfilled;

            if (quest.quantityFulfilled >= quest.quantity)
            {
                quest.fulfilled = true;
                _activeQuests.Remove(quest);
                _completedQuests.Add(quest);
                OnQuestFulfilled?.Invoke(quest);
            }

            return fulfilled;
        }

        // ── Broadcast Integration ────────────────────────────────────────────

        private void InjectQuestsIntoBroadcast(StationState station)
        {
            if (_broadcastNetwork == null) return;

            var pendingQuests = new List<StationQuestEntry>();
            foreach (var quest in _activeQuests)
            {
                if (!quest.fulfilled && quest.quantityFulfilled < quest.quantity)
                    pendingQuests.Add(quest);
            }

            _broadcastNetwork.UpdatePlayerQuestBroadcast(pendingQuests);
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public List<StationQuestEntry> GetActive() => new List<StationQuestEntry>(_activeQuests);
        public List<StationQuestEntry> GetCompleted() => new List<StationQuestEntry>(_completedQuests);

        public StationQuestEntry GetQuest(string questId)
            => _activeQuests.FirstOrDefault(q => q.questId == questId);

        /// <summary>
        /// Get quests for a specific resource (used by TradeExecutor to auto-match).
        /// </summary>
        public StationQuestEntry GetQuestForResource(string resource, string questType)
            => _activeQuests.FirstOrDefault(q => q.resource == resource
                && q.questType == questType && !q.fulfilled);
    }
}
