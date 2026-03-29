// EconomySystem — FAC-005: full credit economy cycle.
//
// Responsibilities:
//   - Apply docking fees when ships arrive (once per visit)
//   - Process faction contract periodic payments
//   - Expose GetCreditBalance as a convenience wrapper over ResourceSystem
//
// Supply/demand pricing and the Persuasion price modifier live in TradeSystem;
// EconomySystem does not duplicate that logic.
//
// Feature gate: FeatureFlags.EconomySystem
//   When false, Tick() is a no-op and credits revert to a flat ResourceSystem
//   resource with no market dynamics.
using System.Collections.Generic;
using Waystation.Core;
using Waystation.Models;
namespace Waystation.Systems
{
    public class EconomySystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Flat docking fee applied to credits when a non-exempt ship arrives.</summary>
        public const float DockingFeeBase = 50f;

        // Roles that are exempt from the docking fee (humanitarian / authority)
        private static readonly HashSet<string> DockingFeeExemptRoles =
            new HashSet<string> { "refugee", "patrol", "inspector" };

        // ── Internal state ────────────────────────────────────────────────────

        // UIDs of ships that have already been charged a docking fee this visit.
        // Entries are removed when the ship is no longer docked, allowing a future
        // visit by the same ship to be charged again.
        private readonly HashSet<string> _chargedShips = new HashSet<string>();

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (!FeatureFlags.EconomySystem) return;

            ProcessDockingFees(station);
            ProcessContracts(station);
            CleanupChargedShips(station);
        }

        // ── Docking fees ──────────────────────────────────────────────────────

        private void ProcessDockingFees(StationState station)
        {
            foreach (var ship in station.GetDockedShips())
            {
                if (_chargedShips.Contains(ship.uid)) continue;
                _chargedShips.Add(ship.uid);

                if (DockingFeeExemptRoles.Contains(ship.role)) continue;

                station.ModifyResource("credits", DockingFeeBase);
                station.LogEvent($"Docking fee from {ship.name}: +{DockingFeeBase:F0} credits.");
            }
        }

        // ── Faction contracts ─────────────────────────────────────────────────

        private void ProcessContracts(StationState station)
        {
            foreach (var contract in station.factionContracts.Values)
            {
                if (station.tick - contract.lastPaymentTick < contract.paymentIntervalTicks)
                    continue;

                contract.lastPaymentTick = station.tick;
                station.ModifyResource("credits", contract.creditPerPayment);
                station.LogEvent(
                    $"Faction contract [{contract.contractId}] payment: " +
                    $"+{contract.creditPerPayment:F0} credits.");
            }
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void CleanupChargedShips(StationState station)
        {
            var dockedUids = new HashSet<string>();
            foreach (var ship in station.GetDockedShips()) dockedUids.Add(ship.uid);
            _chargedShips.RemoveWhere(uid => !dockedUids.Contains(uid));
        }

        // ── Query helpers ─────────────────────────────────────────────────────

        /// <summary>Returns the current credit balance for the station HUD.</summary>
        public float GetCreditBalance(StationState station) => station.GetResource("credits");
    }
}
