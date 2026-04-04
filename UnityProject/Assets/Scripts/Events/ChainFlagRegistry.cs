// ChainFlagRegistry — persistent flag/counter/timestamp store for event chains (WO-FAC-009).
using System;
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Extended chain-flag store that wraps StationState.chainFlags with counters
    /// and timestamps for multi-step event chains. Used by TriggerEvaluator and the
    /// EventSystem to gate and track chain progression.
    /// </summary>
    public class ChainFlagRegistry
    {
        // ── State ─────────────────────────────────────────────────────────────
        // "flag_id" → counter value (0 = flag exists but count is zero)
        private readonly Dictionary<string, int> _counters = new Dictionary<string, int>();
        // "flag_id" → tick when first set
        private readonly Dictionary<string, int> _timestamps = new Dictionary<string, int>();

        // ── Flag Operations (wraps StationState) ─────────────────────────────

        /// <summary>Set a chain flag and record timestamp.</summary>
        public void Set(string flag, StationState station)
        {
            station.SetChainFlag(flag);
            if (!_timestamps.ContainsKey(flag))
                _timestamps[flag] = station.tick;
            if (!_counters.ContainsKey(flag))
                _counters[flag] = 0;
        }

        /// <summary>Clear a chain flag and remove metadata.</summary>
        public void Clear(string flag, StationState station)
        {
            station.ClearChainFlag(flag);
            _timestamps.Remove(flag);
            _counters.Remove(flag);
        }

        /// <summary>Check if a chain flag is set.</summary>
        public bool IsSet(string flag, StationState station)
            => station.HasChainFlag(flag);

        // ── Counter Operations ───────────────────────────────────────────────

        /// <summary>Increment a counter associated with a flag. Sets flag if not already set.</summary>
        public int Increment(string flag, StationState station, int amount = 1)
        {
            if (!station.HasChainFlag(flag))
                Set(flag, station);
            if (!_counters.ContainsKey(flag))
                _counters[flag] = 0;
            _counters[flag] += amount;
            return _counters[flag];
        }

        /// <summary>Get the counter value for a flag. Returns 0 if not tracked.</summary>
        public int GetCounter(string flag)
            => _counters.TryGetValue(flag, out int val) ? val : 0;

        /// <summary>Reset a counter to zero without clearing the flag.</summary>
        public void ResetCounter(string flag)
        {
            if (_counters.ContainsKey(flag))
                _counters[flag] = 0;
        }

        // ── Timestamp Queries ────────────────────────────────────────────────

        /// <summary>Get the tick when a flag was first set. Returns -1 if never set.</summary>
        public int GetTimestamp(string flag)
            => _timestamps.TryGetValue(flag, out int tick) ? tick : -1;

        /// <summary>Get the number of ticks since a flag was set.</summary>
        public int TicksSinceSet(string flag, int currentTick)
        {
            if (!_timestamps.TryGetValue(flag, out int setTick)) return -1;
            return currentTick - setTick;
        }

        // ── Bulk Operations ──────────────────────────────────────────────────

        /// <summary>Get all tracked flag names.</summary>
        public IEnumerable<string> GetAllFlags() => _timestamps.Keys;

        /// <summary>Get all counters (for serialization).</summary>
        public IReadOnlyDictionary<string, int> AllCounters => _counters;

        /// <summary>Get all timestamps (for serialization).</summary>
        public IReadOnlyDictionary<string, int> AllTimestamps => _timestamps;

        /// <summary>Restore state from save data.</summary>
        public void LoadState(Dictionary<string, int> counters, Dictionary<string, int> timestamps)
        {
            _counters.Clear();
            _timestamps.Clear();
            if (counters != null)
                foreach (var kv in counters) _counters[kv.Key] = kv.Value;
            if (timestamps != null)
                foreach (var kv in timestamps) _timestamps[kv.Key] = kv.Value;
        }
    }
}
