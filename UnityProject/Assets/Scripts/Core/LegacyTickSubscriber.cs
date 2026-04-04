// LegacyTickSubscriber — compatibility shim for existing GameManager.OnTick subscribers.
//
// Wraps Action delegates as Channel 0 (Immediate) TickRegistrations
// with conservative EstimatedCostMs. Logs a migration warning at startup.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Core
{
    public class LegacyTickSubscriber
    {
        private readonly TickScheduler _scheduler;
        private readonly List<string> _wrappedIds = new List<string>();

        public LegacyTickSubscriber(TickScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        /// <summary>
        /// Wraps a legacy OnTick subscriber as a Channel 0 registration.
        /// </summary>
        public void WrapSubscriber(string systemId, Action callback)
        {
            _scheduler.Register(new TickRegistration
            {
                SystemId = $"legacy_{systemId}",
                PreferredChannel = 0,
                MaxDelayTicks = 1,
                OnTick = callback,
                EstimatedCostMs = 1.0f,
                UseJobSystem = false,
            });
            _wrappedIds.Add(systemId);
            Debug.LogWarning(
                $"[LegacyTickSubscriber] System '{systemId}' is using legacy OnTick — " +
                "migrate to TickScheduler.Register() for load-balanced scheduling.");
        }

        /// <summary>Returns the number of wrapped legacy subscribers.</summary>
        public int WrappedCount => _wrappedIds.Count;

        /// <summary>Returns all wrapped system IDs.</summary>
        public IReadOnlyList<string> WrappedIds => _wrappedIds;
    }
}
