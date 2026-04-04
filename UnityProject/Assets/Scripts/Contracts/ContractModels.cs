// ContractModels — data structures for the contracts system (WO-FAC-008).
using System.Collections.Generic;

namespace Waystation.Systems
{
    // ── Contract Enums ────────────────────────────────────────────────────
    public enum ContractType
    {
        StandingDeal,
        AdCampaign,
        RelayAgreement,
        Supply,
        Exclusivity,
        Escort,
        Infrastructure
    }

    public enum ContractStatus
    {
        Pending,     // proposed, not yet accepted
        Active,
        Breached,
        Completed,
        Expired
    }

    public enum RelayCompensationType
    {
        FlatFee,
        RevenueShare,
        GoodsExchange
    }

    // ── Core Contract ─────────────────────────────────────────────────────
    public class Contract
    {
        public string         id;
        public ContractType   type;
        public string         counterpartyFaction;
        public string         negotiatedWithNpc;
        public ContractStatus status = ContractStatus.Pending;
        public int            startTick;
        public int            expiryTick = -1;       // -1 = no expiry
        public int            lastEvaluatedTick;
        public List<string>   eventLog = new List<string>();

        // Type-specific terms (only one is non-null based on type)
        public StandingDealTerms      standingDealTerms;
        public AdCampaignTerms        adCampaignTerms;
        public RelayAgreementTerms    relayAgreementTerms;
        public SupplyContractTerms    supplyTerms;
        public ExclusivityTerms       exclusivityTerms;
        public EscortContractTerms    escortTerms;
        public InfrastructureContractTerms infrastructureTerms;

        public void LogEvent(string entry) => eventLog.Add(entry);
    }

    // ── Term Structs ──────────────────────────────────────────────────────

    public class StandingDealTerms
    {
        public string good;
        public int    quantityPerShipment;
        public float  agreedPrice;
        public int    shipmentIntervalDays;
        public float  minPlayerBuyFraction = 0.5f;
        public bool   playerSuppliesReturn;
        public string returnGood;
        public int    returnQuantity;
        public int    lastShipmentTick;
    }

    public class AdCampaignTerms
    {
        public string relayStationId;       // NPC station broadcasting on player's behalf
        public string relayStationFaction;
        public int    durationDays;
        public float  costPerDay;
        public bool   paidUpfront;
        public int    lastPaymentTick;
    }

    public class RelayAgreementTerms
    {
        public string relayStationId;
        public string relayStationFaction;
        public RelayCompensationType compensationType;
        public float  flatFeePerWeek;
        public float  revenueSharePercent;
        public string goodsExchangeResource;
        public int    goodsExchangeQuantityPerWeek;
        public float  goodsExchangePrice;
        public int    noticePeriodDays = 7;
        public int    terminationRequestTick = -1;
    }

    public class SupplyContractTerms
    {
        public string resource;
        public int    quantity;
        public int    deadlineTick;
        public float  paymentOnDelivery;
        public float  penaltyOnFailure;
        public bool   partialFulfilment;
        public int    quantityDelivered;
    }

    public class ExclusivityTerms
    {
        public string good;
        public string excludedFaction;
        public int    durationDays;
        public float  signingBonus;
        public float  weeklyPayment;
        public float  breachPenalty;
    }

    public class EscortContractTerms
    {
        public string escortTargetShipId;    // null for patrol contracts
        public string patrolSectorId;        // for patrol contracts
        public int    durationDays;
        public float  paymentOnCompletion;
        public float  bonusIfNoIncident;
        public List<string> threatTypes = new List<string>();
        public string assignedPlayerShipUid;
    }

    public class InfrastructureContractTerms
    {
        public string requiredCapabilityTag;  // e.g., "medical_surgery"
        public int    deadlineTick;
        public float  paymentOnCompletion;
        public float  ongoingBonusPerWeek;
        public bool   requiresMinCapacity;
        public int    minCapacityThreshold;
        public bool   capabilityVerified;
    }
}
