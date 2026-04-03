// EventLogBuffer.cs
// Singleton ring-buffer for the persistent event log strip (WO-UI-003).
//
// Stores up to MaxEntries (200) LogEntry items.  When the limit is exceeded,
// the oldest entry is dropped from the tail.  Entries are stored most-recent first.
//
// GetCollapsedEntry() returns the highest-priority entry (lowest LogCategory int value),
// breaking ties by insertion order so the most recent alert surfaces first.
//
// GetFiltered(category) returns entries matching the given category, or all entries
// when category is null.
//
// OnBufferChanged fires after any mutation so subscribed controllers can refresh.
using System;
using System.Collections.Generic;

namespace Waystation.UI
{
    /// <summary>
    /// Singleton ring-buffer holding up to <see cref="MaxEntries"/> event log entries.
    /// </summary>
    public class EventLogBuffer
    {
        // ── Singleton ─────────────────────────────────────────────────────────

        private static EventLogBuffer _instance;

        /// <summary>Lazily-created singleton instance.</summary>
        public static EventLogBuffer Instance
        {
            get
            {
                _instance ??= new EventLogBuffer();
                return _instance;
            }
        }

        /// <summary>
        /// Replaces the singleton with a clean instance.
        /// Call in test TearDown to restore a clean state between tests.
        /// </summary>
        public static void Reset() => _instance = null;

        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Maximum number of entries retained.  Oldest entry dropped on overflow.</summary>
        public const int MaxEntries = 200;

        // ── Data ──────────────────────────────────────────────────────────────

        private readonly List<LogEntry> _entries = new List<LogEntry>();

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired after any mutation (Add, Clear).
        /// Subscribers (e.g. EventLogController) should refresh their display.
        /// </summary>
        public event Action OnBufferChanged;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>All entries in insertion order (most-recent first).</summary>
        public IReadOnlyList<LogEntry> Entries => _entries;

        /// <summary>
        /// Adds a new entry at the front of the buffer.
        /// If the buffer already contains <see cref="MaxEntries"/> entries, the oldest
        /// (last) entry is removed first.  Fires <see cref="OnBufferChanged"/> after
        /// the mutation.
        /// </summary>
        public void Add(LogEntry entry)
        {
            if (entry == null) return;
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(_entries.Count - 1);
            OnBufferChanged?.Invoke();
        }

        /// <summary>
        /// Convenience overload: constructs a minimal <see cref="LogEntry"/> and adds it.
        /// </summary>
        public void Add(LogCategory category, string bodyText, int tickFired = 0,
                        string navigateTargetType = null, string navigateTargetId = null,
                        string navigateLabel = null)
        {
            Add(new LogEntry
            {
                Category           = category,
                BodyText           = bodyText,
                TickFired          = tickFired,
                NavigateTargetType = navigateTargetType,
                NavigateTargetId   = navigateTargetId,
                NavigateLabel      = navigateLabel,
            });
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
        /// Removes the entry at the given index and fires <see cref="OnBufferChanged"/>.
        /// No-op if the index is out of range.
        /// </summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            _entries.RemoveAt(index);
            OnBufferChanged?.Invoke();
        }

        /// <summary>
        /// Returns the single entry that should be displayed in the collapsed strip.
        /// Selects the highest-priority entry (lowest <see cref="LogCategory"/> int value),
        /// preserving insertion order (most-recent first) within each category.
        /// Returns null when the buffer is empty.
        /// </summary>
        public LogEntry GetCollapsedEntry()
        {
            if (_entries.Count == 0) return null;

            LogEntry best = _entries[0];
            for (int i = 1; i < _entries.Count; i++)
            {
                if ((int)_entries[i].Category < (int)best.Category)
                    best = _entries[i];
            }
            return best;
        }

        /// <summary>
        /// Returns a snapshot of entries optionally filtered by <paramref name="category"/>.
        /// Pass null to return all entries.  Entries are returned most-recent first.
        /// </summary>
        public IReadOnlyList<LogEntry> GetFiltered(LogCategory? category = null)
        {
            if (category == null)
                return new List<LogEntry>(_entries);

            var result = new List<LogEntry>();
            foreach (var e in _entries)
                if (e.Category == category.Value)
                    result.Add(e);
            return result;
        }
    }
}
