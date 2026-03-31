// LogEntry.cs
// Data model for a single entry in the persistent event log strip (WO-UI-003).
//
// LogCategory maps to the four icon columns in the expanded strip:
//   Alert   — critical crew/station events (red accent)
//   Crew    — NPC state changes, deaths, skill milestones, tension (accent blue)
//   Station — structural, network, farming, resource events (muted green)
//   World   — faction, visitor, asteroid mission, exploration events (muted purple)
//
// Priority in collapsed strip (highest first): Alert > Crew > Station > World.
// This ordering is enforced by the integer values of LogCategory.
using System.Collections.Generic;

namespace Waystation.UI
{
    /// <summary>
    /// Event log category — controls icon, accent colour, and collapsed-strip priority.
    /// Lower int value = higher priority (Alert surfaces first).
    /// </summary>
    public enum LogCategory
    {
        Alert   = 0,   // red — crew deaths, breakdowns, boarding, critical resource failure
        Crew    = 1,   // accent blue — tension, skill, mood, departure, sanity
        Station = 2,   // muted green — farming, network, structural, resource warnings
        World   = 3,   // muted purple — faction, visitor, asteroid, exploration
    }

    /// <summary>
    /// A single entry in the event log strip.
    /// </summary>
    public class LogEntry
    {
        /// <summary>Category governs icon, colour accent, and priority ordering.</summary>
        public LogCategory Category;

        /// <summary>Human-readable body text (NPC/object names highlighted via BoldRanges).</summary>
        public string BodyText;

        /// <summary>
        /// Ranges within <see cref="BodyText"/> that should be rendered in bold/highlighted weight.
        /// Each tuple is (start index, length).
        /// </summary>
        public List<(int start, int length)> BoldRanges;

        /// <summary>The station tick on which this entry was created.</summary>
        public int TickFired;

        /// <summary>
        /// Navigation target type.  One of: "crew", "room", "network", "visitor", "fleet", or null.
        /// </summary>
        public string NavigateTargetType;

        /// <summary>
        /// UID or identifier for the navigation target (NPC uid, room id, fleet ship uid, etc.).
        /// </summary>
        public string NavigateTargetId;

        /// <summary>Human-readable label for the navigate shortcut, e.g. "Open crew panel".</summary>
        public string NavigateLabel;
    }
}
