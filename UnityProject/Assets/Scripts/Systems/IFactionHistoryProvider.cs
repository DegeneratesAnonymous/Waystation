// IFactionHistoryProvider — interface for recording and querying faction history.
//
// The Horizon Simulation work order will provide a full implementation.
// The stub returns empty history and logs TODO markers.
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Interface for accessing and recording faction-level historical events.
    /// Defined here for use by trait/faction systems; implemented by the
    /// Horizon Simulation work order.
    /// </summary>
    public interface IFactionHistoryProvider
    {
        /// <summary>Returns the recorded history for a faction (empty list in stub).</summary>
        List<HistoricalEvent> GetFactionHistory(string factionId);

        /// <summary>Records a faction-level event.</summary>
        void RecordFactionEvent(string factionId, HistoricalEvent evt);
    }
}
