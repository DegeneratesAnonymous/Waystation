// DockingQueue — holding pattern queue with priority ordering and abandonment (WO-FAC-007).
using System.Collections.Generic;
using System.Linq;
using Waystation.Models;

namespace Waystation.Systems
{
    public class DockingQueue
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public const int DefaultAbandonmentTicks = 72; // 3 days (24 ticks/day)

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<QueuedShip> _queue = new List<QueuedShip>();
        private int _abandonmentThreshold = DefaultAbandonmentTicks;

        public struct QueuedShip
        {
            public string shipUid;
            public int    enqueuedTick;
            public bool   priority;
        }

        // ── Configuration ─────────────────────────────────────────────────────

        public void SetAbandonmentThreshold(int ticks) => _abandonmentThreshold = ticks;

        // ── Queue Operations ──────────────────────────────────────────────────

        /// <summary>Add a ship to the queue.</summary>
        public void Enqueue(string shipUid, int currentTick, bool priority = false)
        {
            // Prevent duplicates
            if (_queue.Any(q => q.shipUid == shipUid)) return;

            var entry = new QueuedShip
            {
                shipUid = shipUid,
                enqueuedTick = currentTick,
                priority = priority
            };

            if (priority)
            {
                // Insert at front (after other priority entries)
                int insertIdx = 0;
                while (insertIdx < _queue.Count && _queue[insertIdx].priority)
                    insertIdx++;
                _queue.Insert(insertIdx, entry);
            }
            else
            {
                _queue.Add(entry);
            }
        }

        /// <summary>Remove and return the next ship in the queue.</summary>
        public string Dequeue()
        {
            if (_queue.Count == 0) return null;
            string uid = _queue[0].shipUid;
            _queue.RemoveAt(0);
            return uid;
        }

        /// <summary>Peek at the next ship without removing.</summary>
        public string Peek() => _queue.Count > 0 ? _queue[0].shipUid : null;

        /// <summary>Remove a specific ship from the queue.</summary>
        public bool Remove(string shipUid)
        {
            int idx = _queue.FindIndex(q => q.shipUid == shipUid);
            if (idx < 0) return false;
            _queue.RemoveAt(idx);
            return true;
        }

        /// <summary>Jump a ship to the priority section (e.g., medical emergency).</summary>
        public void PriorityJump(string shipUid, int currentTick)
        {
            Remove(shipUid);
            Enqueue(shipUid, currentTick, priority: true);
        }

        // ── Abandonment ───────────────────────────────────────────────────────

        /// <summary>
        /// Check for ships that have waited beyond the abandonment threshold.
        /// Returns the UIDs of ships that should depart.
        /// </summary>
        public List<string> CheckAbandonment(int currentTick)
        {
            var abandoned = new List<string>();
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (!_queue[i].priority &&
                    (currentTick - _queue[i].enqueuedTick) > _abandonmentThreshold)
                {
                    abandoned.Add(_queue[i].shipUid);
                    _queue.RemoveAt(i);
                }
            }
            return abandoned;
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public int Count => _queue.Count;
        public bool IsEmpty => _queue.Count == 0;

        /// <summary>Get queue position (0-based) for a ship. Returns -1 if not queued.</summary>
        public int GetPosition(string shipUid)
        {
            return _queue.FindIndex(q => q.shipUid == shipUid);
        }

        /// <summary>Estimated ticks until a queued ship reaches the front.</summary>
        public int EstimateWait(string shipUid, int avgDockingDuration)
        {
            int pos = GetPosition(shipUid);
            if (pos < 0) return 0;
            return pos * avgDockingDuration;
        }

        public IReadOnlyList<QueuedShip> Queue => _queue;
    }
}
