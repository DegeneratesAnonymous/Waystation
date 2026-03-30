// ViewContextManager — tracks whichever location or vessel the player is currently viewing.
//
// Introduced WO-UI-004 (Top Bar and Alert Tray).
//
// Game systems or scene controllers call SetContext to update the display name;
// the TopBarController subscribes to OnContextChanged to update its location label.
//
// Typical flow:
//   • On game load: ViewContextManager.Instance.SetContext(station.stationName)
//   • On switching to a ship: ViewContextManager.Instance.SetContext(ship.name)
//   • On returning to station: ViewContextManager.Instance.SetContext(station.stationName)
using System;

namespace Waystation.Core
{
    public class ViewContextManager
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static ViewContextManager _instance;

        public static ViewContextManager Instance
        {
            get
            {
                _instance ??= new ViewContextManager();
                return _instance;
            }
        }

        /// <summary>
        /// Replaces the singleton.  Call in test TearDown to restore a clean state.
        /// </summary>
        public static void Reset() => _instance = null;

        // ── State ─────────────────────────────────────────────────────────────
        private string _currentContextName = string.Empty;

        /// <summary>The display name of the currently-viewed location or vessel.</summary>
        public string CurrentContextName => _currentContextName;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired whenever the context changes.  Argument is the new display name.
        /// </summary>
        public event Action<string> OnContextChanged;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Updates the current context name and fires <see cref="OnContextChanged"/>
        /// if the name actually changed.
        /// </summary>
        public void SetContext(string contextName)
        {
            if (_currentContextName == contextName) return;
            _currentContextName = contextName ?? string.Empty;
            OnContextChanged?.Invoke(_currentContextName);
        }
    }
}
