// TriggerEvaluator — routes 4 event categories onto correct TickScheduler channels (WO-FAC-009).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Urgency tier for events. Determines how the event is presented and whether
    /// the simulation pauses.
    /// </summary>
    public enum EventUrgency
    {
        Standard,      // normal event log / panel notification
        TimeSensitive, // flashing alert, limited response window
        Critical       // simulation pause + full-screen modal
    }

    /// <summary>
    /// Evaluates trigger conditions across 4 event categories on appropriate
    /// TickScheduler channels and routes events to the EventSystem with urgency.
    /// </summary>
    public class TriggerEvaluator
    {
        // ── Categories & Channels ─────────────────────────────────────────────
        // Resource & infrastructure: Channel 0 (immediate / every tick)
        // Faction diplomacy:         Channel 2 (medium / 10-tick)
        // Visitor & trade:           Channel 0 (immediate)
        // Random / scheduled:        Channel 4 (weekly)

        private EventSystem _events;
        private ChainFlagRegistry _chainFlags;
        private FactionEconomySystem _economy;
        private ContractRegistry _contracts;

        public void SetDependencies(EventSystem events, ChainFlagRegistry chainFlags,
            FactionEconomySystem economy, ContractRegistry contracts)
        {
            _events = events;
            _chainFlags = chainFlags;
            _economy = economy;
            _contracts = contracts;
        }

        // ── Channel 0: Resource & Infrastructure (every tick) ─────────────────

        /// <summary>
        /// Check for resource crisis events. Called on every tick via Channel 0.
        /// </summary>
        public void EvaluateResourceTriggers(StationState station)
        {
            // Oxygen critical
            if (station.GetResource("oxygen") <= 0f)
            {
                if (!station.HasChainFlag("resource_oxygen_depleted_fired"))
                {
                    _chainFlags?.Set("resource_oxygen_depleted_fired", station);
                    FireWithUrgency("resource_oxygen_depleted", EventUrgency.Critical, station);
                }
            }
            else
            {
                // Condition cleared — allow re-fire next time it occurs
                _chainFlags?.Clear("resource_oxygen_depleted_fired", station);

                if (station.GetResource("oxygen") < 50f)
                {
                    if (!station.HasChainFlag("resource_oxygen_low_fired"))
                    {
                        _chainFlags?.Set("resource_oxygen_low_fired", station);
                        FireWithUrgency("resource_oxygen_low", EventUrgency.TimeSensitive, station);
                    }
                }
                else
                {
                    _chainFlags?.Clear("resource_oxygen_low_fired", station);
                }
            }

            // Food critical
            if (station.GetResource("food") <= 0f)
            {
                if (!station.HasChainFlag("resource_food_depleted_fired"))
                {
                    _chainFlags?.Set("resource_food_depleted_fired", station);
                    FireWithUrgency("resource_food_depleted", EventUrgency.Critical, station);
                }
            }
            else
            {
                _chainFlags?.Clear("resource_food_depleted_fired", station);

                if (station.GetResource("food") < 30f)
                {
                    if (!station.HasChainFlag("resource_food_low_fired"))
                    {
                        _chainFlags?.Set("resource_food_low_fired", station);
                        FireWithUrgency("resource_food_low", EventUrgency.TimeSensitive, station);
                    }
                }
                else
                {
                    _chainFlags?.Clear("resource_food_low_fired", station);
                }
            }

            // Power failure
            if (station.GetResource("power") <= 0f)
            {
                if (!station.HasChainFlag("resource_power_failure_fired"))
                {
                    _chainFlags?.Set("resource_power_failure_fired", station);
                    FireWithUrgency("resource_power_failure", EventUrgency.Critical, station);
                }
            }
            else
            {
                _chainFlags?.Clear("resource_power_failure_fired", station);
            }

            // Hull breach (infrastructure)
            if (station.HasTag("hull_breach_active"))
            {
                if (!station.HasChainFlag("infrastructure_hull_breach_fired"))
                {
                    _chainFlags?.Set("infrastructure_hull_breach_fired", station);
                    FireWithUrgency("infrastructure_hull_breach", EventUrgency.Critical, station);
                }
            }
            else
            {
                _chainFlags?.Clear("infrastructure_hull_breach_fired", station);
            }

            // Fire
            if (station.HasTag("fire_active"))
            {
                if (!station.HasChainFlag("infrastructure_fire_fired"))
                {
                    _chainFlags?.Set("infrastructure_fire_fired", station);
                    FireWithUrgency("infrastructure_fire", EventUrgency.Critical, station);
                }
            }
            else
            {
                _chainFlags?.Clear("infrastructure_fire_fired", station);
            }
        }

        // ── Channel 0: Visitor & Trade Triggers ──────────────────────────────

        /// <summary>
        /// Evaluate visitor-related triggers. Called on every tick via Channel 0.
        /// </summary>
        public void EvaluateVisitorTriggers(StationState station)
        {
            // Raider approach
            foreach (var ship in station.GetDockedShips())
            {
                if (ship.intent == "raider" && !station.HasChainFlag($"raider_alert_{ship.uid}"))
                {
                    _chainFlags?.Set($"raider_alert_{ship.uid}", station);
                    FireWithUrgency("visitor_raider_approach", EventUrgency.TimeSensitive, station,
                        new Dictionary<string, object> { { "ship_uid", ship.uid } });
                }
            }

            // Smuggler detection (requires security scan)
            if (station.HasTag("security.scanner"))
            {
                foreach (var ship in station.GetDockedShips())
                {
                    if (ship.intent == "smuggler" && !station.HasChainFlag($"smuggler_detected_{ship.uid}"))
                    {
                        _chainFlags?.Set($"smuggler_detected_{ship.uid}", station);
                        float chance = 0.3f + (station.HasTag("security.scanner_advanced") ? 0.3f : 0f);
                        if (UnityEngine.Random.value < chance)
                        {
                            FireWithUrgency("visitor_smuggler_detected", EventUrgency.TimeSensitive, station,
                                new Dictionary<string, object> { { "ship_uid", ship.uid } });
                        }
                    }
                }
            }
        }

        // ── Channel 2: Faction Diplomacy Triggers (10-tick) ──────────────────

        /// <summary>
        /// Evaluate faction-related event triggers. Called on medium ticks via Channel 2.
        /// </summary>
        public void EvaluateFactionTriggers(StationState station)
        {
            if (station.factionReputation == null) return;

            foreach (var kv in station.factionReputation)
            {
                string factionId = kv.Key;
                float rep = kv.Value;

                // Faction hostility threshold
                if (rep <= -50f && !station.HasChainFlag($"faction_hostile_{factionId}"))
                {
                    _chainFlags?.Set($"faction_hostile_{factionId}", station);
                    FireWithUrgency("faction_hostility_declared", EventUrgency.TimeSensitive, station,
                        new Dictionary<string, object> { { "faction_id", factionId } });
                }

                // Faction alliance threshold
                if (rep >= 75f && !station.HasChainFlag($"faction_allied_{factionId}"))
                {
                    _chainFlags?.Set($"faction_allied_{factionId}", station);
                    FireWithUrgency("faction_alliance_offered", EventUrgency.Standard, station,
                        new Dictionary<string, object> { { "faction_id", factionId } });
                }

                // Economic distress (if economy system available)
                if (_economy != null)
                {
                    var profile = _economy.GetProfile(factionId);
                    if (profile != null && profile.economicHealth < 0.3f
                        && !station.HasChainFlag($"faction_distress_{factionId}"))
                    {
                        _chainFlags?.Set($"faction_distress_{factionId}", station);
                        _events?.FireReactiveTrigger("faction_economic_distress", station,
                            new Dictionary<string, object> { { "faction_id", factionId } });
                    }
                }
            }

            // Contract breach consequences
            if (_contracts != null)
            {
                foreach (var c in _contracts.GetByType(ContractType.Exclusivity))
                {
                    if (c.status == ContractStatus.Breached
                        && !station.HasChainFlag($"contract_breach_{c.id}"))
                    {
                        _chainFlags?.Set($"contract_breach_{c.id}", station);
                        FireWithUrgency("contract_breach_consequence", EventUrgency.TimeSensitive, station,
                            new Dictionary<string, object> { { "contract_id", c.id },
                                { "faction_id", c.counterpartyFaction } });
                    }
                }
            }
        }

        // ── Channel 4: Scheduled/Random (weekly) ────────────────────────────

        /// <summary>
        /// Evaluate weekly random/scheduled event triggers. Called via Channel 4.
        /// The main EventSystem.Tick() already handles random event scheduling;
        /// this handles periodic condition-based triggers.
        /// </summary>
        public void EvaluateScheduledTriggers(StationState station)
        {
            // Population growth milestone
            int crewCount = station.GetCrew().Count;
            if (crewCount >= 20 && !station.HasChainFlag("milestone_crew_20"))
            {
                _chainFlags?.Set("milestone_crew_20", station);
                FireWithUrgency("milestone_population", EventUrgency.Standard, station,
                    new Dictionary<string, object> { { "milestone", 20 } });
            }
            if (crewCount >= 50 && !station.HasChainFlag("milestone_crew_50"))
            {
                _chainFlags?.Set("milestone_crew_50", station);
                FireWithUrgency("milestone_population", EventUrgency.Standard, station,
                    new Dictionary<string, object> { { "milestone", 50 } });
            }

            // Trade volume milestone
            float totalTrades = _chainFlags?.GetCounter("total_trade_count") ?? 0;
            if (totalTrades >= 100 && !station.HasChainFlag("milestone_trades_100"))
            {
                _chainFlags?.Set("milestone_trades_100", station);
                FireWithUrgency("milestone_trade_volume", EventUrgency.Standard, station);
            }
        }

        // ── Urgency Routing ──────────────────────────────────────────────────

        /// <summary>Fired when a critical event requires simulation pause.</summary>
        public event Action<string, Dictionary<string, object>> OnCriticalEvent;

        /// <summary>Fired when a time-sensitive event needs player attention.</summary>
        public event Action<string, Dictionary<string, object>> OnTimeSensitiveEvent;

        private void FireWithUrgency(string eventId, EventUrgency urgency, StationState station,
            Dictionary<string, object> context = null)
        {
            context = context ?? new Dictionary<string, object>();
            context["_urgency"] = urgency.ToString();

            switch (urgency)
            {
                case EventUrgency.Critical:
                    _events?.QueueEvent(eventId, context);
                    OnCriticalEvent?.Invoke(eventId, context);
                    break;

                case EventUrgency.TimeSensitive:
                    _events?.QueueEvent(eventId, context);
                    OnTimeSensitiveEvent?.Invoke(eventId, context);
                    break;

                default:
                    _events?.QueueEvent(eventId, context);
                    break;
            }
        }
    }
}
