// ContractRegistry — manages all active contracts with weekly tick evaluation (WO-FAC-008).
using System;
using System.Collections.Generic;
using System.Linq;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ContractRegistry
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public static readonly int PendingExpiryTicks = TimeSystem.TicksPerWeek; // 7 days pending before auto-expire
        public const float NegotiationRepThreshold = 20f;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly Dictionary<string, Contract> _contracts = new Dictionary<string, Contract>();
        private int _nextId;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<Contract> OnContractActivated;
        public event Action<Contract> OnContractBreached;
        public event Action<Contract> OnContractCompleted;
        public event Action<Contract> OnContractExpired;

        // ── Contract Management ───────────────────────────────────────────────

        /// <summary>Create a new contract in Pending status.</summary>
        public Contract CreateContract(ContractType type, string factionId, string npcUid, int currentTick = 0)
        {
            var contract = new Contract
            {
                id = $"contract_{_nextId++}",
                type = type,
                counterpartyFaction = factionId,
                negotiatedWithNpc = npcUid,
                status = ContractStatus.Pending,
                startTick = currentTick   // record proposal time for pending-expiry calculations
            };
            _contracts[contract.id] = contract;
            return contract;
        }

        /// <summary>Accept a pending contract. Sets it to Active and records start tick.</summary>
        public bool AcceptContract(string contractId, int currentTick)
        {
            if (!_contracts.TryGetValue(contractId, out var c)) return false;
            if (c.status != ContractStatus.Pending) return false;

            c.status = ContractStatus.Active;
            c.startTick = currentTick;
            c.lastEvaluatedTick = currentTick;
            c.LogEvent($"Contract activated at tick {currentTick}");
            OnContractActivated?.Invoke(c);
            return true;
        }

        /// <summary>Decline a pending contract.</summary>
        public bool DeclineContract(string contractId)
        {
            if (!_contracts.TryGetValue(contractId, out var c)) return false;
            if (c.status != ContractStatus.Pending) return false;
            c.status = ContractStatus.Expired;
            c.LogEvent("Contract declined by player");
            return true;
        }

        /// <summary>Mark a contract as breached.</summary>
        public void BreachContract(string contractId, string reason, int currentTick)
        {
            if (!_contracts.TryGetValue(contractId, out var c)) return;
            c.status = ContractStatus.Breached;
            c.LogEvent($"Breached at tick {currentTick}: {reason}");
            OnContractBreached?.Invoke(c);
        }

        /// <summary>Mark a contract as completed.</summary>
        public void CompleteContract(string contractId, int currentTick)
        {
            if (!_contracts.TryGetValue(contractId, out var c)) return;
            c.status = ContractStatus.Completed;
            c.LogEvent($"Completed at tick {currentTick}");
            OnContractCompleted?.Invoke(c);
        }

        // ── Weekly Tick ───────────────────────────────────────────────────────

        /// <summary>Evaluate all contracts on the weekly tick.</summary>
        public void TickWeekly(StationState station)
        {
            int tick = station.tick;

            foreach (var kv in _contracts.ToArray())
            {
                var c = kv.Value;

                // Expire old pending contracts
                if (c.status == ContractStatus.Pending)
                {
                    if (tick - c.startTick > PendingExpiryTicks)
                    {
                        c.status = ContractStatus.Expired;
                        c.LogEvent($"Expired (unanswered) at tick {tick}");
                        OnContractExpired?.Invoke(c);
                    }
                    continue;
                }

                // Check expiry
                if (c.status == ContractStatus.Active && c.expiryTick > 0 && tick >= c.expiryTick)
                {
                    c.status = ContractStatus.Completed;
                    c.LogEvent($"Contract period ended at tick {tick}");
                    OnContractCompleted?.Invoke(c);
                    continue;
                }

                c.lastEvaluatedTick = tick;
            }
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public Contract GetContract(string contractId)
            => _contracts.TryGetValue(contractId, out var c) ? c : null;

        public List<Contract> GetActive()
            => _contracts.Values.Where(c => c.status == ContractStatus.Active).ToList();

        public List<Contract> GetPending()
            => _contracts.Values.Where(c => c.status == ContractStatus.Pending).ToList();

        public List<Contract> GetByType(ContractType type)
            => _contracts.Values.Where(c => c.type == type).ToList();

        public List<Contract> GetByFaction(string factionId)
            => _contracts.Values.Where(c => c.counterpartyFaction == factionId).ToList();

        public List<Contract> GetActiveByType(ContractType type)
            => _contracts.Values.Where(c => c.status == ContractStatus.Active && c.type == type).ToList();

        public IReadOnlyDictionary<string, Contract> AllContracts => _contracts;

        /// <summary>Check if the player can negotiate contracts with a faction.</summary>
        public static bool CanNegotiate(string factionId, StationState station)
        {
            if (station.factionReputation == null) return false;
            if (!station.factionReputation.TryGetValue(factionId, out float rep)) return false;
            return rep >= NegotiationRepThreshold;
        }
    }
}
