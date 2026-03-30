// FactionHistory — full implementation of IFactionHistoryProvider.
//
// Persists faction-level historical events in memory and returns the
// recorded history for any faction.  Registered in GameManager.InitSystems()
// as part of the Horizon Simulation, replacing FactionHistoryStub.
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Full implementation of IFactionHistoryProvider.
    /// Events recorded via <see cref="RecordFactionEvent"/> are persisted
    /// in-memory and returned by <see cref="GetFactionHistory"/>.
    /// Each call to <see cref="GetFactionHistory"/> returns a defensive copy
    /// so callers cannot corrupt the internal store.
    /// </summary>
    public class FactionHistory : IFactionHistoryProvider
    {
        private readonly Dictionary<string, List<HistoricalEvent>> _history =
            new Dictionary<string, List<HistoricalEvent>>();

        /// <summary>
        /// Returns a copy of the recorded history for the given faction.
        /// Returns an empty list if no events have been recorded for that faction
        /// or when <paramref name="factionId"/> is null or empty.
        /// </summary>
        public List<HistoricalEvent> GetFactionHistory(string factionId)
        {
            if (string.IsNullOrEmpty(factionId))
                return new List<HistoricalEvent>();

            if (_history.TryGetValue(factionId, out var list))
                return new List<HistoricalEvent>(list);
            return new List<HistoricalEvent>();
        }

        /// <summary>
        /// Persists a faction-level event. Ignored when
        /// <paramref name="factionId"/> is null/empty or <paramref name="evt"/> is null.
        /// </summary>
        public void RecordFactionEvent(string factionId, HistoricalEvent evt)
        {
            if (string.IsNullOrEmpty(factionId) || evt == null) return;
            if (!_history.ContainsKey(factionId))
                _history[factionId] = new List<HistoricalEvent>();
            _history[factionId].Add(evt);
        }
    }
}
