// LogEntryBuffer — singleton alert-entry buffer for the HUD alert tray.
//
// Introduced WO-UI-003; extended WO-UI-004 (alert badge + tray).
//
// The buffer holds AlertEntry items produced by game systems.
// The TopBarController badge reads UnreadAlertCount; the alert tray reads
// GetSortedByUrgency() and calls MarkAllRead() when the tray is opened.
using System;
using System.Collections.Generic;

namespace Waystation.Core
{
    // ── Alert category / urgency ordering ────────────────────────────────────
    // Lower int value = higher urgency (displayed first in the alert tray).
    public enum AlertCategory
    {
        Alert           = 0, // Crew / system critical alerts
        Crew            = 1, // Crew status warnings
        Resource        = 2, // Resource shortages / warnings
        Visitors        = 3, // Incoming visitor notifications
        MissionDistress = 4, // Mission distress signals
    }

    // ── Alert entry ───────────────────────────────────────────────────────────
    public class AlertEntry
    {
        public AlertCategory Category { get; }
        public string        Message  { get; }
        public bool          IsRead   { get; internal set; }

        public AlertEntry(AlertCategory category, string message)
        {
            Category = category;
            Message  = message;
            IsRead   = false;
        }
    }

    // ── LogEntryBuffer singleton ──────────────────────────────────────────────
    public class LogEntryBuffer
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static LogEntryBuffer _instance;

        public static LogEntryBuffer Instance
        {
            get
            {
                _instance ??= new LogEntryBuffer();
                return _instance;
            }
        }

        /// <summary>
        /// Replaces the singleton.  Call in test TearDown to restore a clean state.
        /// </summary>
        public static void Reset() => _instance = null;

        // ── Data ──────────────────────────────────────────────────────────────
        private readonly List<AlertEntry> _entries = new List<AlertEntry>();

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired after any mutation: Add, MarkAllRead, or Clear.
        /// Subscribers (e.g. TopBarController badge) should refresh their display.
        /// </summary>
        public event Action OnBufferChanged;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>All entries in insertion order (most-recent first).</summary>
        public IReadOnlyList<AlertEntry> Entries => _entries;

        /// <summary>Number of entries whose <see cref="AlertEntry.IsRead"/> flag is false.</summary>
        public int UnreadAlertCount
        {
            get
            {
                int count = 0;
                foreach (var e in _entries)
                    if (!e.IsRead) count++;
                return count;
            }
        }

        /// <summary>
        /// Adds a new unread entry at the front of the buffer and fires
        /// <see cref="OnBufferChanged"/>.
        /// </summary>
        public void Add(AlertCategory category, string message)
        {
            _entries.Insert(0, new AlertEntry(category, message));
            OnBufferChanged?.Invoke();
        }

        /// <summary>
        /// Marks all entries as read and fires <see cref="OnBufferChanged"/>.
        /// </summary>
        public void MarkAllRead()
        {
            foreach (var e in _entries)
                e.IsRead = true;
            OnBufferChanged?.Invoke();
        }

        /// <summary>
        /// Removes all entries and fires <see cref="OnBufferChanged"/>.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            OnBufferChanged?.Invoke();
        }

        /// <summary>
        /// Returns a snapshot of entries sorted by urgency
        /// (Alert > Crew > Resource > Visitors > MissionDistress).
        /// Insertion order (most-recent first) is preserved within each category.
        /// </summary>
        public IReadOnlyList<AlertEntry> GetSortedByUrgency()
        {
            // Build an index map in O(n) to avoid O(n²) IndexOf calls during sort.
            var indexMap = new Dictionary<AlertEntry, int>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
                indexMap[_entries[i]] = i;

            var sorted = new List<AlertEntry>(_entries);
            sorted.Sort((a, b) =>
            {
                int cat = ((int)a.Category).CompareTo((int)b.Category);
                if (cat != 0) return cat;
                // Preserve original insertion order within each category.
                return indexMap[a].CompareTo(indexMap[b]);
            });
            return sorted;
        }
    }
}
