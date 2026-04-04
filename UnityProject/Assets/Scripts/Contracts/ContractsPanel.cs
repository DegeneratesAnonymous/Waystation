// ContractsPanel — panel controller for the contracts management UI (WO-FAC-008).
using System.Collections.Generic;
using System.Linq;

namespace Waystation.Systems
{
    /// <summary>
    /// Data contract for the Contracts panel UI. Groups contracts by type,
    /// provides filtered views and detail data for the selected contract.
    /// </summary>
    public class ContractsPanel
    {
        private ContractRegistry _registry;

        public ContractsPanel(ContractRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>Get contracts grouped by type with counts.</summary>
        public Dictionary<ContractType, List<Contract>> GetGroupedContracts(
            ContractStatus? statusFilter = null)
        {
            var groups = new Dictionary<ContractType, List<Contract>>();

            foreach (var c in _registry.AllContracts.Values)
            {
                if (statusFilter.HasValue && c.status != statusFilter.Value)
                    continue;

                if (!groups.ContainsKey(c.type))
                    groups[c.type] = new List<Contract>();
                groups[c.type].Add(c);
            }

            return groups;
        }

        /// <summary>Get count of active contracts.</summary>
        public int ActiveCount => _registry.GetActive().Count;

        /// <summary>Get count of pending contracts.</summary>
        public int PendingCount => _registry.GetPending().Count;

        /// <summary>Get count of expired/completed contracts (recent).</summary>
        public int CompletedCount => _registry.AllContracts.Values
            .Count(c => c.status == ContractStatus.Completed || c.status == ContractStatus.Expired);

        /// <summary>Get detail data for a specific contract.</summary>
        public ContractDetailData GetDetail(string contractId)
        {
            var c = _registry.GetContract(contractId);
            if (c == null) return null;

            return new ContractDetailData
            {
                contractId = c.id,
                type = c.type,
                status = c.status,
                factionId = c.counterpartyFaction,
                negotiatedWith = c.negotiatedWithNpc,
                startTick = c.startTick,
                expiryTick = c.expiryTick,
                eventLog = c.eventLog,
                contract = c
            };
        }

        public class ContractDetailData
        {
            public string contractId;
            public ContractType type;
            public ContractStatus status;
            public string factionId;
            public string negotiatedWith;
            public int startTick;
            public int expiryTick;
            public List<string> eventLog;
            public Contract contract;
        }
    }
}
