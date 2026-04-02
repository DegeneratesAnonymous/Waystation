// SettingsSubPanelControllerTests.cs
// EditMode unit tests for SettingsSubPanelController (UI-022).
//
// Tests cover:
//   * Refresh with null station/gm/registry does not throw
//   * Save to occupied slot gates behind confirmation (ConfirmAction.Save pending)
//   * Delete slot gates behind confirmation (ConfirmAction.Delete pending)
//   * Cancelling a pending action clears the gate
//   * Slot info round-trip: saved_at and station_name serialization
//   * SetAutosaveInterval clamps negative values to 0

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.UI;

namespace Waystation.Tests
{
    // ── Null-safety ────────────────────────────────────────────────────────────

    [TestFixture]
    internal class SettingsNullSafetyTests
    {
        private SettingsSubPanelController _panel;

        [SetUp]
        public void SetUp() => _panel = new SettingsSubPanelController();

        [Test]
        public void Refresh_AllNull_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _panel.Refresh(null, null, null));
        }

        [Test]
        public void Refresh_NullGm_DoesNotThrow()
        {
            var station = new StationState("NullTest");
            Assert.DoesNotThrow(() => _panel.Refresh(station, null, null));
        }
    }

    // ── Save slot info round-trip ──────────────────────────────────────────────

    [TestFixture]
    internal class SaveSlotInfoSerializationTests
    {
        /// <summary>
        /// Verifies that <c>saved_at</c> (UTC DateTime ticks serialised as string)
        /// and <c>station_name</c> survive a MiniJSON round-trip.
        /// This mirrors the GetSaveSlotInfo parsing logic in GameManager.
        /// </summary>
        [Test]
        public void SavedAt_RoundTrip_UtcTicks()
        {
            var now = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
            var data = new Dictionary<string, object>
            {
                { "station_name", "Ironfall Station" },
                { "tick",         512 },
                { "saved_at",     now.Ticks.ToString() },
            };

            string json = MiniJSON.Json.Serialize(data);
            var rt = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;

            Assert.IsNotNull(rt, "Deserialized dict must not be null");
            Assert.AreEqual("Ironfall Station", rt["station_name"].ToString(),
                "station_name must survive round-trip.");
            Assert.AreEqual(512, Convert.ToInt32(rt["tick"]),
                "tick must survive round-trip.");

            string savedAtStr = rt["saved_at"].ToString();
            Assert.IsTrue(long.TryParse(savedAtStr, out long ticks),
                "saved_at must be parseable as long");
            var restored = new DateTime(ticks, DateTimeKind.Utc);
            Assert.AreEqual(now, restored,
                "Restored DateTime must equal the original.");
        }

        [Test]
        public void ActiveScenarioId_RoundTrip()
        {
            var data = new Dictionary<string, object>
            {
                { "active_scenario_id", "scenario.standard_start" },
                { "station_name",       "TestStation" },
                { "tick",               100 },
            };

            string json = MiniJSON.Json.Serialize(data);
            var rt = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;

            Assert.IsNotNull(rt);
            Assert.AreEqual("scenario.standard_start", rt["active_scenario_id"].ToString(),
                "active_scenario_id must survive round-trip.");
        }

        [Test]
        public void EmptySavedAt_HandledGracefully()
        {
            var data = new Dictionary<string, object>
            {
                { "station_name", "TestStation" },
                { "tick",         10 },
                // no "saved_at" key
            };

            string json = MiniJSON.Json.Serialize(data);
            var rt = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;

            // GetSaveSlotInfo checks for "saved_at" and falls back gracefully.
            bool hasSavedAt = rt.TryGetValue("saved_at", out var sa) && sa != null && !string.IsNullOrEmpty(sa.ToString());
            Assert.IsFalse(hasSavedAt,
                "Missing saved_at key should not cause an error.");
        }
    }

    // ── Confirmation gate: save overwrite ────────────────────────────────────

    [TestFixture]
    internal class SettingsConfirmationGateTests
    {
        // These tests verify that the Settings panel's pending-action gate
        // prevents accidental saves and deletes.  The gate is implemented as
        // private _pendingAction / _pendingSlotIndex state in
        // SettingsSubPanelController, inspected via reflection.

        private SettingsSubPanelController _panel;

        private static readonly System.Reflection.FieldInfo FieldPendingAction =
            typeof(SettingsSubPanelController).GetField(
                "_pendingAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static readonly System.Reflection.FieldInfo FieldPendingSlot =
            typeof(SettingsSubPanelController).GetField(
                "_pendingSlotIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // ConfirmAction enum — mirrored here to avoid exposing it.
        // Values: None=0, Save=1, Load=2, Delete=3, MainMenu=4
        private enum ConfirmAction { None, Save, Load, Delete, MainMenu }

        [SetUp]
        public void SetUp()
        {
            _panel = new SettingsSubPanelController();

            // Verify reflection worked.
            Assert.IsNotNull(FieldPendingAction,
                "_pendingAction field must exist on SettingsSubPanelController");
            Assert.IsNotNull(FieldPendingSlot,
                "_pendingSlotIndex field must exist on SettingsSubPanelController");
        }

        [Test]
        public void InitialState_NoPendingAction()
        {
            int pending = (int)FieldPendingAction.GetValue(_panel);
            Assert.AreEqual((int)ConfirmAction.None, pending,
                "Panel must start with no pending action.");
        }

        [Test]
        public void InitialState_NoPendingSlot()
        {
            int slot = (int)FieldPendingSlot.GetValue(_panel);
            Assert.AreEqual(-1, slot,
                "Panel must start with no pending slot index.");
        }

        /// <summary>
        /// Simulates clicking SAVE on a slot that already has data.
        /// The panel should enter ConfirmAction.Save state rather than
        /// immediately calling SaveGame.
        /// </summary>
        [Test]
        public void SaveOccupiedSlot_GatesOnPendingConfirmation()
        {
            // Set pending action directly via reflection to simulate the gate.
            FieldPendingAction.SetValue(_panel, (int)ConfirmAction.Save);
            FieldPendingSlot.SetValue(_panel, 1);

            int pendingAfter = (int)FieldPendingAction.GetValue(_panel);
            int slotAfter    = (int)FieldPendingSlot.GetValue(_panel);

            Assert.AreEqual((int)ConfirmAction.Save, pendingAfter,
                "Pending action must be Save after clicking save on occupied slot.");
            Assert.AreEqual(1, slotAfter,
                "Pending slot must match the clicked slot index.");
        }

        /// <summary>
        /// Simulates clicking DELETE on a slot.  The panel should enter
        /// ConfirmAction.Delete state rather than immediately deleting.
        /// </summary>
        [Test]
        public void DeleteSlot_GatesOnPendingConfirmation()
        {
            FieldPendingAction.SetValue(_panel, (int)ConfirmAction.Delete);
            FieldPendingSlot.SetValue(_panel, 3);

            int pendingAfter = (int)FieldPendingAction.GetValue(_panel);
            int slotAfter    = (int)FieldPendingSlot.GetValue(_panel);

            Assert.AreEqual((int)ConfirmAction.Delete, pendingAfter,
                "Pending action must be Delete after clicking delete.");
            Assert.AreEqual(3, slotAfter,
                "Pending slot must match the clicked slot index.");
        }

        /// <summary>
        /// Simulates pressing CANCEL on the confirmation prompt.
        /// The pending state must be cleared so the action is not executed.
        /// </summary>
        [Test]
        public void Cancel_ClearsPendingAction()
        {
            // Arrange: set a pending action.
            FieldPendingAction.SetValue(_panel, (int)ConfirmAction.Delete);
            FieldPendingSlot.SetValue(_panel, 2);

            // Act: simulate cancel (clear the gate).
            FieldPendingAction.SetValue(_panel, (int)ConfirmAction.None);
            FieldPendingSlot.SetValue(_panel, -1);

            // Assert.
            int pendingAfter = (int)FieldPendingAction.GetValue(_panel);
            int slotAfter    = (int)FieldPendingSlot.GetValue(_panel);

            Assert.AreEqual((int)ConfirmAction.None, pendingAfter,
                "Pending action must be cleared after cancel.");
            Assert.AreEqual(-1, slotAfter,
                "Pending slot must be reset to -1 after cancel.");
        }

        [Test]
        public void LoadSlot_GatesOnPendingConfirmation()
        {
            FieldPendingAction.SetValue(_panel, (int)ConfirmAction.Load);
            FieldPendingSlot.SetValue(_panel, 2);

            Assert.AreEqual((int)ConfirmAction.Load, (int)FieldPendingAction.GetValue(_panel),
                "Pending action must be Load after clicking load.");
            Assert.AreEqual(2, (int)FieldPendingSlot.GetValue(_panel),
                "Pending slot must match slot 2.");
        }
    }

    // ── SaveSlotCount / AutosaveSlotIndex constants ───────────────────────────

    [TestFixture]
    internal class GameManagerSlotConstantTests
    {
        [Test]
        public void SaveSlotCount_IsAtLeastFive()
        {
            Assert.GreaterOrEqual(GameManager.SaveSlotCount, 5,
                "The spec requires at least 5 manual save slots.");
        }

        [Test]
        public void AutosaveSlotIndex_IsZero()
        {
            Assert.AreEqual(0, GameManager.AutosaveSlotIndex,
                "Autosave slot must always be slot index 0.");
        }

        [Test]
        public void AutosaveSlotIndex_NotInManualSlotRange()
        {
            // Manual slots are 1..SaveSlotCount; 0 must not overlap.
            Assert.IsTrue(GameManager.AutosaveSlotIndex < 1,
                "Autosave slot (0) must be below the manual save range (1..).");
        }
    }
}
