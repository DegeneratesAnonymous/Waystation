// DockingPolicyEvaluator — per-faction policy + per-ship override resolution (WO-FAC-007).
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public class DockingPolicyEvaluator
    {
        // ── State ─────────────────────────────────────────────────────────────
        /// <summary>Per-faction docking policy. Default: Open for known factions, ApproveRequired for new.</summary>
        private readonly Dictionary<string, DockingPolicy> _factionPolicies
            = new Dictionary<string, DockingPolicy>();

        /// <summary>Per-ship override for the current arrival.</summary>
        private readonly Dictionary<string, DockingOverride> _shipOverrides
            = new Dictionary<string, DockingOverride>();

        /// <summary>Set of factions the player has previously interacted with.</summary>
        private readonly HashSet<string> _knownFactions = new HashSet<string>();

        // ── Policy Configuration ──────────────────────────────────────────────

        public void SetFactionPolicy(string factionId, DockingPolicy policy)
        {
            _factionPolicies[factionId] = policy;
            _knownFactions.Add(factionId);
        }

        public DockingPolicy GetFactionPolicy(string factionId)
        {
            if (_factionPolicies.TryGetValue(factionId, out var p)) return p;
            // New/unknown factions default to ApproveRequired
            if (!_knownFactions.Contains(factionId)) return DockingPolicy.ApproveRequired;
            return DockingPolicy.Open;
        }

        public void SetShipOverride(string shipUid, DockingOverride ov)
        {
            _shipOverrides[shipUid] = ov;
        }

        public void ClearShipOverride(string shipUid) => _shipOverrides.Remove(shipUid);

        // ── Evaluation ────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluate docking policy for an inbound ship. Returns the resolved policy
        /// after applying per-ship overrides on top of per-faction defaults.
        /// </summary>
        public DockingPolicy Evaluate(ShipInstance ship, StationState station)
        {
            // Per-ship override takes priority
            if (_shipOverrides.TryGetValue(ship.uid, out var ov))
            {
                switch (ov)
                {
                    case DockingOverride.Approve:     return DockingPolicy.Open;
                    case DockingOverride.Deny:        return DockingPolicy.Deny;
                    case DockingOverride.Inspect:     return DockingPolicy.Inspect;
                    case DockingOverride.Hold:        return DockingPolicy.Inspect; // dock but hold
                    case DockingOverride.PriorityDock: return DockingPolicy.Open;
                    case DockingOverride.None: break;
                }
            }

            // Fall back to faction policy
            string factionId = ship.factionId ?? "unknown";
            return GetFactionPolicy(factionId);
        }

        /// <summary>Check if a ship has a priority dock override.</summary>
        public bool HasPriorityOverride(string shipUid)
        {
            return _shipOverrides.TryGetValue(shipUid, out var ov)
                && ov == DockingOverride.PriorityDock;
        }

        /// <summary>Check if a ship should be held (docked but crew stays on board).</summary>
        public bool IsHeldOnBoard(string shipUid)
        {
            return _shipOverrides.TryGetValue(shipUid, out var ov)
                && ov == DockingOverride.Hold;
        }

        /// <summary>Mark a faction as known (so future ships default to Open, not ApproveRequired).</summary>
        public void MarkFactionKnown(string factionId)
        {
            _knownFactions.Add(factionId);
            if (!_factionPolicies.ContainsKey(factionId))
                _factionPolicies[factionId] = DockingPolicy.Open;
        }

        public IReadOnlyDictionary<string, DockingPolicy> AllFactionPolicies => _factionPolicies;
    }
}
