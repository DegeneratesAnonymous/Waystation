// SaveSlotInfo.cs
// Lightweight metadata record returned by GameManager.GetSaveSlotInfo().
// Used by SettingsSubPanelController to populate the Save/Load slot list.
using System;

namespace Waystation.Core
{
    /// <summary>
    /// Read-only snapshot of metadata stored in a save slot file.
    /// Returned by <see cref="GameManager.GetSaveSlotInfo"/>.
    /// </summary>
    public class SaveSlotInfo
    {
        /// <summary>Slot index (0 = autosave; 1–<see cref="GameManager.SaveSlotCount"/> = manual).</summary>
        public int      slotIndex;

        /// <summary>Station name recorded when the game was saved.</summary>
        public string   stationName;

        /// <summary>Game tick at which the save was written.</summary>
        public int      tick;

        /// <summary>
        /// UTC wall-clock time the file was written, or null if not recorded in the file.
        /// </summary>
        public DateTime? savedAt;
    }
}
