// TickRegistration — data struct for registering a system with the TickScheduler.
using System;

namespace Waystation.Core
{
    public class TickRegistration
    {
        /// <summary>Unique identifier for this system (e.g. "personality_drift").</summary>
        public string SystemId;

        /// <summary>Preferred channel 0–4 (Immediate, Fast, Medium, Slow, Weekly).</summary>
        public int PreferredChannel;

        /// <summary>Maximum ticks this system can be delayed beyond its cadence before forced execution.</summary>
        public int MaxDelayTicks;

        /// <summary>Callback to execute when the system's tick fires.</summary>
        public Action OnTick;

        /// <summary>Declared cost hint in milliseconds for budget tracking.</summary>
        public float EstimatedCostMs = 1f;

        /// <summary>If true, dispatch on a worker thread via Unity Job System.</summary>
        public bool UseJobSystem;

        // ── Runtime state (managed by TickScheduler) ──────────────────────
        /// <summary>Tick at which this system last executed.</summary>
        internal int LastExecutedTick;

        /// <summary>Calibrated actual cost (updated over time if calibration is enabled).</summary>
        internal float CalibratedCostMs;

        /// <summary>Number of samples collected for cost calibration.</summary>
        internal int CalibrationSamples;
    }
}
