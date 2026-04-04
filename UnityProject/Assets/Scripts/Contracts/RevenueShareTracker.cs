// RevenueShareTracker — relay chain attribution for revenue share deals (WO-FAC-008).
using System.Collections.Generic;

namespace Waystation.Systems
{
    /// <summary>
    /// Tracks trades attributed to relay stations via broadcast chain annotations.
    /// Revenue share relay agreements use this data to calculate compensation.
    /// </summary>
    public class RevenueShareTracker
    {
        // ── State ─────────────────────────────────────────────────────────────
        /// <summary>Relay station ID → list of credited trade amounts.</summary>
        private readonly Dictionary<string, List<float>> _revenueByRelay
            = new Dictionary<string, List<float>>();

        /// <summary>Relay station ID → cumulative revenue this period.</summary>
        private readonly Dictionary<string, float> _periodRevenue
            = new Dictionary<string, float>();

        // ── Attribution ───────────────────────────────────────────────────────

        /// <summary>
        /// Attribute a completed trade to all relay stations in the broadcast chain.
        /// </summary>
        public void AttributeTrade(TradeResult result)
        {
            if (result == null || result.relayChain == null || result.relayChain.Count == 0)
                return;

            float totalRevenue = result.creditsEarned + result.creditsSpent;
            if (totalRevenue <= 0f) return;

            foreach (string relayId in result.relayChain)
            {
                if (!_revenueByRelay.ContainsKey(relayId))
                    _revenueByRelay[relayId] = new List<float>();
                _revenueByRelay[relayId].Add(totalRevenue);

                if (!_periodRevenue.ContainsKey(relayId))
                    _periodRevenue[relayId] = 0f;
                _periodRevenue[relayId] += totalRevenue;
            }
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>Get accumulated revenue credited to a relay station this period.</summary>
        public float GetRevenueShare(string relayStationId, float sharePercent)
        {
            if (!_periodRevenue.TryGetValue(relayStationId, out float total))
                return 0f;
            return total * (sharePercent / 100f);
        }

        /// <summary>Get total revenue attributed to a relay station.</summary>
        public float GetTotalRevenue(string relayStationId)
        {
            return _periodRevenue.TryGetValue(relayStationId, out float total) ? total : 0f;
        }

        // ── Period Management ─────────────────────────────────────────────────

        /// <summary>Reset period revenue (called on weekly tick after disbursement).</summary>
        public void ResetPeriod()
        {
            _periodRevenue.Clear();
        }

        /// <summary>Disburse revenue share payments for all active relay agreements.</summary>
        public float DisburseRevenueShare(string relayStationId, float sharePercent)
        {
            float payment = GetRevenueShare(relayStationId, sharePercent);
            // Reset this relay's tracked revenue after disbursement
            _periodRevenue.Remove(relayStationId);
            return payment;
        }
    }
}
