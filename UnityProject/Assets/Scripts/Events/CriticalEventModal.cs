// CriticalEventModal — simulation-pause + full-screen alert for critical events (WO-FAC-009).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Manages the critical event modal display. When a critical event fires,
    /// this pauses the simulation and presents a full-screen alert requiring player response.
    /// </summary>
    public class CriticalEventModal
    {
        // ── State ─────────────────────────────────────────────────────────────
        private bool _isActive;
        private string _currentEventId;
        private PendingEvent _currentEvent;
        private Dictionary<string, object> _currentContext;

        /// <summary>True when a critical modal is displayed and sim is paused.</summary>
        public bool IsActive => _isActive;

        /// <summary>The currently displayed event ID.</summary>
        public string CurrentEventId => _currentEventId;

        /// <summary>The pending event being shown.</summary>
        public PendingEvent CurrentEvent => _currentEvent;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when the modal opens. UI should listen to display.</summary>
        public event Action<string, PendingEvent, Dictionary<string, object>> OnModalOpened;

        /// <summary>Fired when the player dismisses the modal.</summary>
        public event Action<string> OnModalClosed;

        /// <summary>Fired to request simulation pause.</summary>
        public event Action<bool> OnPauseRequested;

        // ── Show / Dismiss ───────────────────────────────────────────────────

        /// <summary>
        /// Show the critical event modal. Pauses simulation.
        /// </summary>
        public void Show(string eventId, PendingEvent pending, Dictionary<string, object> context)
        {
            if (_isActive)
            {
                Debug.LogWarning($"[CriticalEventModal] Already showing '{_currentEventId}', queueing '{eventId}'");
                return;
            }

            _isActive = true;
            _currentEventId = eventId;
            _currentEvent = pending;
            _currentContext = context;

            // Request simulation pause
            OnPauseRequested?.Invoke(true);

            // Notify UI
            OnModalOpened?.Invoke(eventId, pending, context);
        }

        /// <summary>
        /// Dismiss the current modal and resume simulation.
        /// </summary>
        public void Dismiss()
        {
            if (!_isActive) return;

            string eventId = _currentEventId;
            _isActive = false;
            _currentEventId = null;
            _currentEvent = null;
            _currentContext = null;

            OnPauseRequested?.Invoke(false);
            OnModalClosed?.Invoke(eventId);
        }

        /// <summary>
        /// Get display data for the current modal.
        /// </summary>
        public ModalDisplayData GetDisplayData()
        {
            if (!_isActive || _currentEvent == null) return null;

            return new ModalDisplayData
            {
                eventId = _currentEventId,
                title = _currentEvent.definition?.title ?? "ALERT",
                description = _currentEvent.definition?.description ?? "",
                isHostile = _currentEvent.definition?.hostile ?? false,
                choices = _currentEvent.definition?.choices ?? new List<EventChoice>(),
                context = _currentContext
            };
        }

        public class ModalDisplayData
        {
            public string eventId;
            public string title;
            public string description;
            public bool isHostile;
            public List<EventChoice> choices;
            public Dictionary<string, object> context;
        }
    }
}
