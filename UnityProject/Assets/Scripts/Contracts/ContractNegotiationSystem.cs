// ContractNegotiationSystem — visitor NPC negotiation flow (WO-FAC-008).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ContractNegotiationSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const float BaseAcceptanceChance = 0.7f;
        private const float RepBonusPerPoint     = 0.005f; // +0.5% per rep point above 20
        private const float PriceFlexibility     = 0.15f;  // 15% price flex range

        // ── Dependencies ──────────────────────────────────────────────────────
        private ContractRegistry _registry;
        private FactionEconomySystem _economy;

        public void SetDependencies(ContractRegistry registry, FactionEconomySystem economy)
        {
            _registry = registry;
            _economy = economy;
        }

        // ── Negotiation ───────────────────────────────────────────────────────

        /// <summary>
        /// Propose a standing trade deal via a visiting trader/diplomat NPC.
        /// Returns a pending contract or null if faction won't negotiate.
        /// </summary>
        public Contract ProposeStandingDeal(string factionId, string npcUid,
            string good, int quantity, float price, int intervalDays, StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            var contract = _registry.CreateContract(ContractType.StandingDeal, factionId, npcUid, station.tick);
            contract.standingDealTerms = new StandingDealTerms
            {
                good = good,
                quantityPerShipment = quantity,
                agreedPrice = price,
                shipmentIntervalDays = intervalDays,
                minPlayerBuyFraction = 0.5f
            };
            return contract;
        }

        /// <summary>Propose an ad campaign via a visiting NPC.</summary>
        public Contract ProposeAdCampaign(string factionId, string npcUid,
            string relayStationId, int durationDays, float costPerDay, StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            var contract = _registry.CreateContract(ContractType.AdCampaign, factionId, npcUid, station.tick);
            contract.adCampaignTerms = new AdCampaignTerms
            {
                relayStationId = relayStationId,
                relayStationFaction = factionId,
                durationDays = durationDays,
                costPerDay = costPerDay
            };
            contract.expiryTick = -1; // set on acceptance based on duration
            return contract;
        }

        /// <summary>Propose a relay agreement.</summary>
        public Contract ProposeRelayAgreement(string factionId, string npcUid,
            string relayStationId, RelayCompensationType compType, float amount,
            StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            // Extra prerequisite: prior ad campaign or trade volume
            bool hasPriorTrade = HasPriorTradeHistory(factionId, station);
            if (!hasPriorTrade) return null;

            var contract = _registry.CreateContract(ContractType.RelayAgreement, factionId, npcUid, station.tick);
            contract.relayAgreementTerms = new RelayAgreementTerms
            {
                relayStationId = relayStationId,
                relayStationFaction = factionId,
                compensationType = compType,
                flatFeePerWeek = compType == RelayCompensationType.FlatFee ? amount : 0f,
                revenueSharePercent = compType == RelayCompensationType.RevenueShare ? amount : 0f
            };
            contract.expiryTick = -1; // ongoing
            return contract;
        }

        /// <summary>Propose a supply contract.</summary>
        public Contract ProposeSupplyContract(string factionId, string npcUid,
            string resource, int quantity, int deadlineTick, float payment, StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            var contract = _registry.CreateContract(ContractType.Supply, factionId, npcUid, station.tick);
            contract.supplyTerms = new SupplyContractTerms
            {
                resource = resource,
                quantity = quantity,
                deadlineTick = deadlineTick,
                paymentOnDelivery = payment,
                penaltyOnFailure = payment * 0.25f,
                partialFulfilment = true
            };
            contract.expiryTick = deadlineTick;
            return contract;
        }

        /// <summary>Propose an exclusivity agreement.</summary>
        public Contract ProposeExclusivity(string factionId, string npcUid,
            string good, string excludedFaction, int durationDays,
            float signingBonus, float weeklyPayment, StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            var contract = _registry.CreateContract(ContractType.Exclusivity, factionId, npcUid, station.tick);
            contract.exclusivityTerms = new ExclusivityTerms
            {
                good = good,
                excludedFaction = excludedFaction,
                durationDays = durationDays,
                signingBonus = signingBonus,
                weeklyPayment = weeklyPayment,
                breachPenalty = signingBonus * 2f
            };
            return contract;
        }

        /// <summary>Propose an escort/patrol contract.</summary>
        public Contract ProposeEscort(string factionId, string npcUid,
            string targetShipId, string patrolSector, int durationDays,
            float payment, StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            var contract = _registry.CreateContract(ContractType.Escort, factionId, npcUid, station.tick);
            contract.escortTerms = new EscortContractTerms
            {
                escortTargetShipId = targetShipId,
                patrolSectorId = patrolSector,
                durationDays = durationDays,
                paymentOnCompletion = payment,
                bonusIfNoIncident = payment * 0.2f
            };
            return contract;
        }

        /// <summary>Propose an infrastructure contract.</summary>
        public Contract ProposeInfrastructure(string factionId, string npcUid,
            string capabilityTag, int deadlineTick, float payment, float weeklyBonus,
            StationState station)
        {
            if (!ContractRegistry.CanNegotiate(factionId, station)) return null;

            var contract = _registry.CreateContract(ContractType.Infrastructure, factionId, npcUid, station.tick);
            contract.infrastructureTerms = new InfrastructureContractTerms
            {
                requiredCapabilityTag = capabilityTag,
                deadlineTick = deadlineTick,
                paymentOnCompletion = payment,
                ongoingBonusPerWeek = weeklyBonus
            };
            contract.expiryTick = deadlineTick;
            return contract;
        }

        // ── Counter-Proposal ──────────────────────────────────────────────────

        /// <summary>
        /// Player counters a proposed standing deal with adjusted terms.
        /// NPC acceptance depends on faction economic need and reputation.
        /// </summary>
        public bool CounterPropose(string contractId, float newPrice, int newQuantity,
            StationState station)
        {
            var c = _registry.GetContract(contractId);
            if (c == null || c.status != ContractStatus.Pending) return false;
            if (c.standingDealTerms == null) return false;

            float rep = 0f;
            station.factionReputation?.TryGetValue(c.counterpartyFaction, out rep);

            // Acceptance chance: base + rep bonus, reduced by price deviation
            float priceDeviation = Mathf.Abs(newPrice - c.standingDealTerms.agreedPrice)
                / c.standingDealTerms.agreedPrice;
            float chance = BaseAcceptanceChance + (rep - 20f) * RepBonusPerPoint
                - priceDeviation / PriceFlexibility;
            chance = Mathf.Clamp01(chance);

            if (UnityEngine.Random.value < chance)
            {
                c.standingDealTerms.agreedPrice = newPrice;
                c.standingDealTerms.quantityPerShipment = newQuantity;
                c.LogEvent($"Counter-proposal accepted: price={newPrice}, qty={newQuantity}");
                return true;
            }

            c.LogEvent("Counter-proposal rejected by NPC");
            return false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private bool HasPriorTradeHistory(string factionId, StationState station)
        {
            // Check visit history for prior trades (use shipRole as proxy for faction contact)
            if (station.visitHistory != null && station.visitHistory.Count > 0)
                return true;

            // Check for active ad campaign with this faction
            if (_registry != null)
            {
                foreach (var c in _registry.GetActiveByType(ContractType.AdCampaign))
                {
                    if (c.counterpartyFaction == factionId) return true;
                }
            }

            return false;
        }
    }
}
