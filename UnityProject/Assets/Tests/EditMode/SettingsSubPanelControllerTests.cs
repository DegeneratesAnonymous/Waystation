// SettingsSubPanelControllerTests.cs
// EditMode unit tests for SettingsSubPanelController (UI-022).
//
// Tests cover:
//   * Refresh with null station/gm/registry does not throw
//   * Sub-tab navigation via ClickEvent
//   * Save to occupied slot gates behind confirmation prompt (via ClickEvent + file)
//   * Load slot gates behind confirmation prompt (via ClickEvent + file)
//   * Delete slot gates behind confirmation prompt (via ClickEvent + file)
//   * Cancelling a pending action clears the gate (via ClickEvent on CANCEL button)
//   * SaveSlotCount / AutosaveSlotIndex constants meet spec

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

    // ── Sub-tab navigation ─────────────────────────────────────────────────────

    [TestFixture]
    internal class SettingsSubTabNavigationTests
    {
        private SettingsSubPanelController _panel;

        private static readonly System.Reflection.FieldInfo FieldActiveSubTab =
            typeof(SettingsSubPanelController).GetField(
                "_activeSubTab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            _panel = new SettingsSubPanelController();
            Assert.IsNotNull(FieldActiveSubTab, "_activeSubTab field must exist");
        }

        private void ClickTab(string label)
        {
            var btn = _panel.Query<Button>().Where(b => b.text == label).First();
            Assert.IsNotNull(btn, $"Tab button '{label}' must exist in the panel.");
            using var evt = ClickEvent.GetPooled();
            evt.target = btn;
            btn.SendEvent(evt);
        }

        [Test]
        public void DefaultActiveTab_IsGame()
        {
            _panel.Refresh(null, null, null);
            string active = (string)FieldActiveSubTab.GetValue(_panel);
            Assert.AreEqual("game", active, "Panel must start on the GAME sub-tab.");
        }

        [Test]
        public void ClickSaveLoadTab_ActivatesSaveLoadSubTab()
        {
            _panel.Refresh(null, null, null);
            ClickTab("SAVE/LOAD");
            string active = (string)FieldActiveSubTab.GetValue(_panel);
            Assert.AreEqual("saveload", active, "Clicking SAVE/LOAD tab must activate saveload sub-tab.");
        }

        [Test]
        public void ClickScenariosTab_ActivatesScenariosSubTab()
        {
            _panel.Refresh(null, null, null);
            ClickTab("SCENARIOS");
            string active = (string)FieldActiveSubTab.GetValue(_panel);
            Assert.AreEqual("scenarios", active, "Clicking SCENARIOS tab must activate scenarios sub-tab.");
        }

        [Test]
        public void ClickGameTab_ReturnsToGameSubTab()
        {
            _panel.Refresh(null, null, null);
            ClickTab("SCENARIOS");
            ClickTab("GAME");
            string active = (string)FieldActiveSubTab.GetValue(_panel);
            Assert.AreEqual("game", active, "Clicking GAME tab must restore game sub-tab.");
        }
    }

    // ── Confirmation gate tests via ClickEvent ─────────────────────────────────
    //
    // These tests write a real save file to Application.persistentDataPath (slot 1)
    // and create a minimal GameManager component so the Save/Load panel renders with
    // an occupied slot, then exercise the button handlers via ClickEvent.

    [TestFixture]
    internal class SettingsConfirmationGateTests
    {
        private static readonly System.Reflection.FieldInfo FieldPendingAction =
            typeof(SettingsSubPanelController).GetField(
                "_pendingAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static readonly System.Reflection.FieldInfo FieldPendingSlot =
            typeof(SettingsSubPanelController).GetField(
                "_pendingSlotIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // ConfirmAction enum — mirrors the private enum in SettingsSubPanelController.
        private enum ConfirmAction { None, Save, Load, Delete, MainMenu }

        private string _slotPath;
        private GameObject _gmGo;
        private GameManager _gm;
        private SettingsSubPanelController _panel;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(FieldPendingAction,  "_pendingAction field must exist");
            Assert.IsNotNull(FieldPendingSlot,    "_pendingSlotIndex field must exist");

            // Write a minimal valid save file so slot 1 appears occupied.
            _slotPath = Path.Combine(
                Application.persistentDataPath, "waystation_save_slot1.json");
            var fakeData = new Dictionary<string, object>
            {
                { "station_name", "GateTestStation" },
                { "tick",         99 },
                { "saved_at",     DateTime.UtcNow.Ticks.ToString() },
            };
            File.WriteAllText(_slotPath, MiniJSON.Json.Serialize(fakeData));

            // Create a minimal GameManager so GetSaveSlotInfo can read the file.
            _gmGo  = new GameObject("GM_GateTest");
            _gm    = _gmGo.AddComponent<GameManager>();

            // Build and refresh the panel on the SAVE/LOAD sub-tab.
            _panel = new SettingsSubPanelController();
            _panel.Refresh(new StationState("GateTestStation"), _gm, null);

            // Navigate to the SAVE/LOAD sub-tab via ClickEvent.
            var tabBtn = _panel.Query<Button>().Where(b => b.text == "SAVE/LOAD").First();
            Assert.IsNotNull(tabBtn, "SAVE/LOAD tab button must exist.");
            using var tabEvt = ClickEvent.GetPooled();
            tabEvt.target = tabBtn;
            tabBtn.SendEvent(tabEvt);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_slotPath)) File.Delete(_slotPath);
            if (_gmGo != null) Object.DestroyImmediate(_gmGo);
        }

        private void Click(Button btn)
        {
            using var e = ClickEvent.GetPooled();
            e.target = btn;
            btn.SendEvent(e);
        }

        private int PendingAction => (int)FieldPendingAction.GetValue(_panel);
        private int PendingSlot   => (int)FieldPendingSlot.GetValue(_panel);

        // ── Save overwrite gate ────────────────────────────────────────────────

        [Test]
        public void ClickSave_OnOccupiedSlot_EntersSavePending()
        {
            // Slot 1 has a file, so the first SAVE button should gate.
            var saveBtn = _panel.Query<Button>().Where(b => b.text == "SAVE").First();
            Assert.IsNotNull(saveBtn, "SAVE button must exist for slot 1.");

            Click(saveBtn);

            Assert.AreEqual((int)ConfirmAction.Save, PendingAction,
                "Clicking SAVE on an occupied slot must enter Save pending state.");
            Assert.AreEqual(1, PendingSlot,
                "Pending slot index must be 1.");
        }

        [Test]
        public void ClickSave_OccupiedSlot_ThenCancel_ClearsPending()
        {
            var saveBtn = _panel.Query<Button>().Where(b => b.text == "SAVE").First();
            Click(saveBtn); // enters pending, rebuilds UI

            // After rebuild the confirmation row is visible; find CANCEL.
            var cancelBtn = _panel.Query<Button>().Where(b => b.text == "CANCEL").First();
            Assert.IsNotNull(cancelBtn, "CANCEL button must appear in confirmation row.");
            Click(cancelBtn);

            Assert.AreEqual((int)ConfirmAction.None, PendingAction,
                "Pressing CANCEL must clear the pending action.");
            Assert.AreEqual(-1, PendingSlot,
                "Pending slot must reset to -1 after cancel.");
        }

        // ── Load gate ─────────────────────────────────────────────────────────

        [Test]
        public void ClickLoad_OnOccupiedSlot_EntersLoadPending()
        {
            // The LOAD button for slot 1 should be enabled (file exists).
            var loadBtn = _panel.Query<Button>().Where(b => b.text == "LOAD").First();
            Assert.IsNotNull(loadBtn, "LOAD button must exist.");
            Assert.IsTrue(loadBtn.enabledSelf, "LOAD must be enabled for occupied slot 1.");

            Click(loadBtn);

            Assert.AreEqual((int)ConfirmAction.Load, PendingAction,
                "Clicking LOAD on an occupied slot must enter Load pending state.");
            Assert.AreEqual(1, PendingSlot,
                "Pending slot index must be 1.");
        }

        // ── Delete gate ────────────────────────────────────────────────────────

        [Test]
        public void ClickDelete_OnOccupiedSlot_EntersDeletePending()
        {
            // DELETE button only appears when slot is occupied.
            var deleteBtn = _panel.Query<Button>().Where(b => b.text == "DELETE").First();
            Assert.IsNotNull(deleteBtn, "DELETE button must exist for occupied slot 1.");

            Click(deleteBtn);

            Assert.AreEqual((int)ConfirmAction.Delete, PendingAction,
                "Clicking DELETE on an occupied slot must enter Delete pending state.");
            Assert.AreEqual(1, PendingSlot,
                "Pending slot index must be 1.");
        }

        // ── Initial state ──────────────────────────────────────────────────────

        [Test]
        public void InitialState_NoPendingAction()
        {
            // After navigating to save/load, no action should be pending.
            Assert.AreEqual((int)ConfirmAction.None, PendingAction,
                "Panel must have no pending action on first render.");
            Assert.AreEqual(-1, PendingSlot,
                "Panel must have no pending slot on first render.");
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
            Assert.IsTrue(GameManager.AutosaveSlotIndex < 1,
                "Autosave slot (0) must be below the manual save range (1..).");
        }
    }
}

