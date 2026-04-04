// ContractBreachDetector — weekly breach condition checking (WO-FAC-008).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ContractBreachDetector
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const float StandingDealRepPenalty    = -5f;
        private const float StandingDealCreditPenalty = -15f;
        private const int   StandingDealSuspensionTicks = 336; // 14 days
        private const int   CreditGracePeriodTicks   = 168;    // 7 days
        private const float RelayRepSuspendThreshold = 0f;     // Neutral
        private const int   RelayRecoveryWindowTicks = 336;    // 14 days

        // ── Dependencies ──────────────────────────────────────────────────────
        private ContractRegistry _registry;
        private BroadcastNetwork _broadcastNetwork;

        /// <summary>Fired when a breach is detected. (Contract, reason)</summary>
        public event Action<Contract, string> OnBreachDetected;

        public void SetDependencies(ContractRegistry registry, BroadcastNetwork broadcastNetwork)
        {
            _registry = registry;
            _broadcastNetwork = broadcastNetwork;
        }

        // ── Weekly Check ──────────────────────────────────────────────────────

        /// <summary>Check all active contracts for breach conditions.</summary>
        public void CheckBreaches(StationState station)
        {
            int tick = station.tick;

            foreach (var contract in _registry.GetActive())
            {
                switch (contract.type)
                {
                    case ContractType.StandingDeal:
                        CheckStandingDealBreach(contract, station, tick);
                        break;
                    case ContractType.RelayAgreement:
                        CheckRelayAgreementBreach(contract, station, tick);
                        break;
                    case ContractType.Supply:
                        CheckSupplyContractBreach(contract, station, tick);
                        break;
                    case ContractType.Exclusivity:
                        // Exclusivity breach is checked in real-time via transaction logging
                        // (handled by TradeExecutor integration)
                        break;
                    case ContractType.Infrastructure:
                        CheckInfrastructureBreach(contract, station, tick);
                        break;
                    case ContractType.AdCampaign:
                        CheckAdCampaignPayment(contract, station, tick);
                        break;
                }
            }
        }

        // ── Standing Deal Breach ──────────────────────────────────────────────

        private void CheckStandingDealBreach(Contract c, StationState station, int tick)
        {
            var terms = c.standingDealTerms;
            if (terms == null) return;

            int intervalTicks = terms.shipmentIntervalDays * 24; // days → ticks
            if (tick - terms.lastShipmentTick < intervalTicks) return;

            // Shipment is due — check if player fulfilled their buy obligation
            // (simplified: breach if insufficient credits)
            float requiredCredits = terms.agreedPrice * terms.quantityPerShipment * terms.minPlayerBuyFraction;
            if (station.GetResource("credits") < requiredCredits)
            {
                // Credit grace period: 7 days
                int ticksSinceShipment = tick - terms.lastShipmentTick;
                if (ticksSinceShipment > intervalTicks + CreditGracePeriodTicks)
                {
                    BreachContract(c, "Player unable to pay — deal terminated", station, StandingDealCreditPenalty);
                    return;
                }
            }

            // Update last shipment tick
            terms.lastShipmentTick = tick;
        }

        // ── Relay Agreement Breach ────────────────────────────────────────────

        private void CheckRelayAgreementBreach(Contract c, StationState station, int tick)
        {
            var terms = c.relayAgreementTerms;
            if (terms == null) return;

            // Check reputation threshold
            float rep = 0f;
            station.factionReputation?.TryGetValue(c.counterpartyFaction, out rep);

            if (rep < RelayRepSuspendThreshold)
            {
                // Auto-suspend
                c.LogEvent($"Relay suspended — reputation dropped below 0 (current: {rep:F0})");

                // Check if recovery window has expired
                if (terms.terminationRequestTick > 0 &&
                    tick - terms.terminationRequestTick > RelayRecoveryWindowTicks)
                {
                    _registry.BreachContract(c.id, "Reputation recovery window expired", tick);
                    _broadcastNetwork?.RemoveRelay(terms.relayStationId);
                    OnBreachDetected?.Invoke(c, "Relay terminated — reputation too low for too long");
                }
                else if (terms.terminationRequestTick <= 0)
                {
                    terms.terminationRequestTick = tick;
                }
            }
            else
            {
                // Reputation recovered — reset termination window
                if (terms.terminationRequestTick > 0)
                {
                    terms.terminationRequestTick = -1;
                    c.LogEvent("Relay resumed — reputation recovered");
                }

                // Check compensation payment
                CheckRelayCompensation(c, terms, station, tick);
            }
        }

        private void CheckRelayCompensation(Contract c, RelayAgreementTerms terms,
            StationState station, int tick)
        {
            switch (terms.compensationType)
            {
                case RelayCompensationType.FlatFee:
                    float credits = station.GetResource("credits");
                    if (credits >= terms.flatFeePerWeek)
                    {
                        station.ModifyResource("credits", -terms.flatFeePerWeek);
                        c.LogEvent($"Relay fee paid: {terms.flatFeePerWeek:F0} credits");
                    }
                    else
                    {
                        BreachContract(c, "Unable to pay relay fee", station, -5f);
                    }
                    break;

                case RelayCompensationType.GoodsExchange:
                    float available = station.GetResource(terms.goodsExchangeResource);
                    if (available >= terms.goodsExchangeQuantityPerWeek)
                    {
                        station.ModifyResource(terms.goodsExchangeResource,
                            -terms.goodsExchangeQuantityPerWeek);
                        c.LogEvent($"Relay goods paid: {terms.goodsExchangeQuantityPerWeek} {terms.goodsExchangeResource}");
                    }
                    else
                    {
                        BreachContract(c, $"Unable to supply {terms.goodsExchangeResource} for relay", station, -5f);
                    }
                    break;

                case RelayCompensationType.RevenueShare:
                    // Revenue share is tracked passively by RevenueShareTracker
                    break;
            }
        }

        // ── Supply Contract Breach ────────────────────────────────────────────

        private void CheckSupplyContractBreach(Contract c, StationState station, int tick)
        {
            var terms = c.supplyTerms;
            if (terms == null) return;

            // Check deadline
            if (tick >= terms.deadlineTick)
            {
                if (terms.quantityDelivered >= terms.quantity)
                {
                    // Fully fulfilled
                    station.ModifyResource("credits", terms.paymentOnDelivery);
                    _registry.CompleteContract(c.id, tick);
                    c.LogEvent($"Supply contract fulfilled: {terms.quantityDelivered}/{terms.quantity}");
                }
                else if (terms.partialFulfilment && terms.quantityDelivered > 0)
                {
                    // Partial fulfilment
                    float ratio = (float)terms.quantityDelivered / terms.quantity;
                    float payment = terms.paymentOnDelivery * ratio;
                    float repPenalty = terms.penaltyOnFailure * (1f - ratio);
                    station.ModifyResource("credits", payment);
                    ApplyRepPenalty(c.counterpartyFaction, -repPenalty, station);
                    _registry.CompleteContract(c.id, tick);
                    c.LogEvent($"Supply contract partially fulfilled: {terms.quantityDelivered}/{terms.quantity} ({ratio:P0})");
                }
                else
                {
                    // Failed
                    station.ModifyResource("credits", -terms.penaltyOnFailure);
                    ApplyRepPenalty(c.counterpartyFaction, -10f, station);
                    _registry.BreachContract(c.id, "Deadline passed without fulfilment", tick);
                    OnBreachDetected?.Invoke(c, "Supply contract failed");
                }
            }
        }

        // ── Infrastructure Breach ─────────────────────────────────────────────

        private void CheckInfrastructureBreach(Contract c, StationState station, int tick)
        {
            var terms = c.infrastructureTerms;
            if (terms == null) return;

            // Check if capability is still operational
            bool operational = station.activeTags != null &&
                station.activeTags.Contains(terms.requiredCapabilityTag);

            if (terms.capabilityVerified && !operational)
            {
                // Capability went offline — stop weekly bonus
                c.LogEvent("Capability offline — weekly bonus suspended");
                terms.capabilityVerified = false;
            }
            else if (!terms.capabilityVerified && operational)
            {
                // Capability came online (or first verification)
                if (!terms.capabilityVerified && tick <= terms.deadlineTick)
                {
                    terms.capabilityVerified = true;
                    station.ModifyResource("credits", terms.paymentOnCompletion);
                    c.LogEvent($"Capability verified! Payment: {terms.paymentOnCompletion:F0} credits");
                }
                else
                {
                    terms.capabilityVerified = true;
                }
            }

            // Pay weekly bonus if operational
            if (terms.capabilityVerified && operational && terms.ongoingBonusPerWeek > 0f)
            {
                station.ModifyResource("credits", terms.ongoingBonusPerWeek);
                c.LogEvent($"Weekly bonus: {terms.ongoingBonusPerWeek:F0} credits");
            }

            // Check deadline for non-verified
            if (!terms.capabilityVerified && tick >= terms.deadlineTick)
            {
                _registry.BreachContract(c.id, "Capability not built by deadline", tick);
                OnBreachDetected?.Invoke(c, "Infrastructure contract deadline missed");
            }
        }

        // ── Ad Campaign Payment ───────────────────────────────────────────────

        private void CheckAdCampaignPayment(Contract c, StationState station, int tick)
        {
            var terms = c.adCampaignTerms;
            if (terms == null) return;

            if (!terms.paidUpfront)
            {
                // Weekly payment
                float weeklyPayment = terms.costPerDay * 7f;
                if (station.GetResource("credits") >= weeklyPayment)
                {
                    station.ModifyResource("credits", -weeklyPayment);
                    terms.lastPaymentTick = tick;
                    c.LogEvent($"Ad campaign weekly payment: {weeklyPayment:F0} credits");
                }
                else
                {
                    _registry.BreachContract(c.id, "Unable to pay ad campaign fee", tick);
                    OnBreachDetected?.Invoke(c, "Ad campaign payment failed");
                }
            }
        }

        // ── Exclusivity Breach (called externally on transaction) ─────────────

        /// <summary>
        /// Check if a transaction with a given faction violates any active exclusivity agreement.
        /// Call from TradeExecutor when a sale is completed.
        /// </summary>
        public void CheckExclusivityBreach(string resourceId, string buyerFaction,
            StationState station, int tick)
        {
            foreach (var c in _registry.GetActiveByType(ContractType.Exclusivity))
            {
                var terms = c.exclusivityTerms;
                if (terms == null) continue;

                if (terms.good == resourceId && terms.excludedFaction == buyerFaction)
                {
                    // Breach!
                    station.ModifyResource("credits", -terms.breachPenalty);
                    _registry.BreachContract(c.id, $"Player sold {resourceId} to excluded faction {buyerFaction}", tick);
                    OnBreachDetected?.Invoke(c, "Exclusivity agreement breached");
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void BreachContract(Contract c, string reason, StationState station, float repPenalty)
        {
            _registry.BreachContract(c.id, reason, station.tick);
            ApplyRepPenalty(c.counterpartyFaction, repPenalty, station);
            OnBreachDetected?.Invoke(c, reason);
        }

        private void ApplyRepPenalty(string factionId, float delta, StationState station)
        {
            if (station.factionReputation == null) return;
            if (station.factionReputation.TryGetValue(factionId, out float rep))
                station.factionReputation[factionId] = Mathf.Clamp(rep + delta, -100f, 100f);
        }
    }
}
