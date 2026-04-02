// SettingsSubPanelController.cs
// Settings tab panel (UI-022).
//
// Displays three sub-tabs:
//   1. Game        — Tick speed slider (1–3×), autosave interval selector,
//                    graphics/sound placeholders, Return to Main Menu button.
//   2. Save/Load   — Autosave slot (read-only) + 5 manual slots with save,
//                    load, and delete.  All destructive actions require a
//                    confirmation prompt.
//   3. Scenarios   — Current scenario card + full scenario catalogue; New Game
//                    button routes to scenario selection.
//
// Call Refresh(StationState, GameManager, ContentRegistry) to sync with live data.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController which is itself gated by that flag).

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Settings side-panel tab (UI-022) — Game, Save/Load, and Scenarios sub-tabs.
    /// </summary>
    public class SettingsSubPanelController : VisualElement
    {
        // ── USS class names ───────────────────────────────────────────────────

        private const string PanelClass         = "ws-settings-panel";
        private const string SectionHeaderClass = "ws-settings-panel__section-header";
        private const string RowClass           = "ws-settings-panel__row";
        private const string ActionBtnClass     = "ws-settings-panel__action-btn";
        private const string DangerBtnClass     = "ws-settings-panel__danger-btn";
        private const string EmptyClass         = "ws-settings-panel__empty";

        // ── Sub-tab identifiers ───────────────────────────────────────────────

        private const string SubTabGame      = "game";
        private const string SubTabSaveLoad  = "saveload";
        private const string SubTabScenarios = "scenarios";
        private const string SubTabDev       = "dev";

        // ── Internal state ────────────────────────────────────────────────────

        private readonly TabStrip      _subTabs;
        private readonly VisualElement _subContent;

        private string           _activeSubTab = SubTabGame;

        private StationState     _station;
        private GameManager      _gm;
        private ContentRegistry  _registry;

        // Pending confirmation state for destructive actions.
        private enum ConfirmAction { None, Save, Load, Delete, MainMenu }
        private ConfirmAction _pendingAction     = ConfirmAction.None;
        private int           _pendingSlotIndex  = -1;

        // ── Constructor ───────────────────────────────────────────────────────

        public SettingsSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.height        = Length.Percent(100);
            style.overflow      = Overflow.Hidden;

            // Sub-tab strip
            _subContent = new VisualElement();
            _subContent.style.flexGrow = 1;
            _subContent.style.overflow = Overflow.Hidden;

            _subTabs = new TabStrip(TabStrip.Orientation.Horizontal);
            _subTabs.OnTabSelected += OnSubTabSelected;
            _subTabs.AddTab("GAME",      SubTabGame);
            _subTabs.AddTab("SAVE/LOAD", SubTabSaveLoad);
            _subTabs.AddTab("SCENARIOS", SubTabScenarios);
            _subTabs.AddTab("DEV",       SubTabDev);

            Add(_subTabs);
            Add(_subContent);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the panel with the latest game state.
        /// Call on mount and on significant state changes.
        /// </summary>
        public void Refresh(StationState station, GameManager gm, ContentRegistry registry)
        {
            _station  = station;
            _gm       = gm;
            _registry = registry;

            RebuildSubTab();
        }

        // ── Sub-tab switching ─────────────────────────────────────────────────

        private void OnSubTabSelected(string subTabId)
        {
            _activeSubTab    = subTabId;
            _pendingAction   = ConfirmAction.None;
            _pendingSlotIndex = -1;
            RebuildSubTab();
        }

        private void RebuildSubTab()
        {
            _subContent.Clear();

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _subContent.Add(scroll);

            var root = scroll.contentContainer;
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft   = 8;
            root.style.paddingRight  = 8;
            root.style.paddingTop    = 8;
            root.style.paddingBottom = 8;

            switch (_activeSubTab)
            {
                case SubTabGame:      BuildGameTab(root);      break;
                case SubTabSaveLoad:  BuildSaveLoadTab(root);  break;
                case SubTabScenarios: BuildScenariosTab(root); break;
                case SubTabDev:       BuildDevTab(root);       break;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ── Dev sub-tab ──────────────────────────────────────────────────────
        // ═════════════════════════════════════════════════════════════════════

        private void BuildDevTab(VisualElement root)
        {
            root.Add(BuildSectionHeader("RUNTIME TOOLS"));

            bool freeBuild = BuildingSystem.DevMode;
            var freeBuildBtn = BuildActionButton(
                freeBuild ? "FREE BUILD: ON" : "FREE BUILD: OFF",
                danger: false
            );
            freeBuildBtn.clicked += () =>
            {
                BuildingSystem.DevMode = !BuildingSystem.DevMode;
                RebuildSubTab();
            };
            root.Add(freeBuildBtn);

            bool telescopeMode = SystemMapController.TelescopeMode;
            var telescopeBtn = BuildActionButton(
                telescopeMode ? "TELESCOPE MODE: ON" : "TELESCOPE MODE: OFF",
                danger: false
            );
            telescopeBtn.clicked += () =>
            {
                SystemMapController.TelescopeMode = !SystemMapController.TelescopeMode;
                RebuildSubTab();
            };
            root.Add(telescopeBtn);

            var tradeShipBtn = BuildActionButton("CALL TRADE SHIP", danger: false);
            tradeShipBtn.clicked += () =>
            {
                if (_gm?.Visitors != null && _station != null)
                    _gm.Visitors.SpawnTradeShip(_station);
            };
            root.Add(tradeShipBtn);

            root.Add(BuildSectionHeader("SPAWN"));

            var spawnCrewBtn = BuildActionButton("SPAWN CREW NPC", danger: false);
            spawnCrewBtn.clicked += () =>
            {
                if (_gm?.Npcs == null || _station == null) return;
                string templateId = PickNpcTemplate(preferCrew: true);
                if (string.IsNullOrEmpty(templateId)) return;
                var npc = _gm.Npcs.SpawnCrewMember(templateId);
                if (npc != null) _station.AddNpc(npc);
            };
            root.Add(spawnCrewBtn);

            var spawnVisitorBtn = BuildActionButton("SPAWN VISITOR NPC", danger: false);
            spawnVisitorBtn.clicked += () =>
            {
                if (_gm?.Npcs == null || _station == null) return;
                string templateId = PickNpcTemplate(preferCrew: false);
                if (string.IsNullOrEmpty(templateId)) return;
                var npc = _gm.Npcs.SpawnVisitor(templateId);
                if (npc != null) _station.AddNpc(npc);
            };
            root.Add(spawnVisitorBtn);

            root.Add(BuildSectionHeader("EVENTS"));

            var queueEventBtn = BuildActionButton("QUEUE RANDOM EVENT", danger: false);
            queueEventBtn.clicked += () =>
            {
                if (_gm?.Events == null || _registry?.Events == null || _registry.Events.Count == 0)
                    return;

                var eventIds = new List<string>(_registry.Events.Keys);
                eventIds.Sort(StringComparer.Ordinal);
                string eventId = eventIds[UnityEngine.Random.Range(0, eventIds.Count)];
                _gm.Events.QueueEvent(eventId);
                _station?.LogEvent($"[DEV] Queued event: {eventId}");
            };
            root.Add(queueEventBtn);

            var tip = BuildEmptyLabel(
                "Tip: Telescope Mode bypasses map research and sector unlock cost gates. " +
                "Use Map tab to view and expand sectors immediately."
            );
            tip.style.marginTop = 6;
            root.Add(tip);
        }

        private string PickNpcTemplate(bool preferCrew)
        {
            if (_gm?.Npcs == null) return null;

            var templates = _gm.Npcs.AvailableTemplateIds?.ToList();
            if (templates == null || templates.Count == 0) return null;

            templates.Sort(StringComparer.Ordinal);
            string preferredPrefix = preferCrew ? "npc.crew" : "npc.visitor";

            foreach (string id in templates)
            {
                if (id.StartsWith(preferredPrefix, StringComparison.OrdinalIgnoreCase))
                    return id;
            }

            return templates[0];
        }

        // ═════════════════════════════════════════════════════════════════════
        // ── Game sub-tab ──────────────────────────────────────────────────────
        // ═════════════════════════════════════════════════════════════════════

        private void BuildGameTab(VisualElement root)
        {
            // ── Tick speed ────────────────────────────────────────────────────
            root.Add(BuildSectionHeader("TICK SPEED"));

            // Read current speed multiplier from PlayerPrefs (matches GameViewController convention).
            // 1× = 0.5 s/tick; 2× = 0.25 s/tick; 3× = ~0.167 s/tick.
            float currentMultiplier = PlayerPrefs.GetFloat("tick_speed_multiplier", 1f);
            currentMultiplier = Mathf.Clamp(Mathf.Round(currentMultiplier), 1f, 3f);

            var speedRow = new VisualElement();
            speedRow.style.flexDirection = FlexDirection.Row;
            speedRow.style.alignItems    = Align.Center;
            speedRow.style.marginBottom  = 8;

            var speedLabel = new Label($"Speed: {currentMultiplier:0}×");
            speedLabel.style.color    = new Color(0.80f, 0.85f, 0.95f, 1f);
            speedLabel.style.fontSize = 12;
            speedLabel.style.width    = 80;

            var slider = new Slider(1f, 3f);
            slider.value     = currentMultiplier;
            slider.style.flexGrow = 1;

            slider.RegisterValueChangedCallback(evt =>
            {
                float snapped = Mathf.Round(evt.newValue);
                snapped = Mathf.Clamp(snapped, 1f, 3f);
                speedLabel.text = $"Speed: {snapped:0}×";
                // Match GameViewController convention: SetSpeed(2f * multiplier)
                _gm?.SetSpeed(2f * snapped);
                PlayerPrefs.SetFloat("tick_speed_multiplier", snapped);
            });

            speedRow.Add(speedLabel);
            speedRow.Add(slider);
            root.Add(speedRow);

            // ── Autosave interval ─────────────────────────────────────────────
            root.Add(BuildSectionHeader("AUTOSAVE INTERVAL"));

            int currentInterval = _gm?.AutosaveIntervalTicks ?? 120;
            var intervalOptions = new[]
            {
                ("Off",           0),
                ("Every 100 ticks", 100),
                ("Every 500 ticks", 500),
                ("Every 1000 ticks", 1000),
            };

            foreach (var (label, ticks) in intervalOptions)
            {
                bool isSelected = currentInterval == ticks;
                var optRow = new VisualElement();
                optRow.style.flexDirection  = FlexDirection.Row;
                optRow.style.alignItems     = Align.Center;
                optRow.style.marginBottom   = 4;
                optRow.style.paddingLeft    = 4;
                optRow.style.paddingRight   = 4;
                optRow.style.paddingTop     = 4;
                optRow.style.paddingBottom  = 4;
                optRow.style.backgroundColor = isSelected
                    ? new Color(0.15f, 0.25f, 0.45f, 1f)
                    : Color.clear;
                optRow.style.borderBottomWidth = 1;
                optRow.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);

                var dot = new Label(isSelected ? "●" : "○");
                dot.style.color     = isSelected ? new Color(0.39f, 0.75f, 1.00f, 1f) : new Color(0.45f, 0.50f, 0.60f, 1f);
                dot.style.fontSize  = 10;
                dot.style.marginRight = 6;

                var optLabel = new Label(label);
                optLabel.style.color    = new Color(0.80f, 0.85f, 0.95f, 1f);
                optLabel.style.fontSize = 12;
                optLabel.style.flexGrow = 1;

                optRow.Add(dot);
                optRow.Add(optLabel);

                int capturedTicks = ticks;
                optRow.RegisterCallback<ClickEvent>(_ =>
                {
                    _gm?.SetAutosaveInterval(capturedTicks);
                    RebuildSubTab();
                });

                root.Add(optRow);
            }

            // ── Graphics ──────────────────────────────────────────────────────
            root.Add(BuildSectionHeader("GRAPHICS"));
            root.Add(BuildComingSoonLabel());

            // ── Sound ─────────────────────────────────────────────────────────
            root.Add(BuildSectionHeader("SOUND"));
            root.Add(BuildComingSoonLabel());

            // ── Return to main menu ────────────────────────────────────────────
            root.Add(BuildSectionHeader("SESSION"));

            if (_pendingAction == ConfirmAction.MainMenu)
            {
                root.Add(BuildConfirmRow(
                    "Return to main menu? Unsaved progress will be lost.",
                    onConfirm: () => SceneManager.LoadScene("MainMenuScene"),
                    onCancel: () => { _pendingAction = ConfirmAction.None; RebuildSubTab(); }
                ));
            }
            else
            {
                var menuBtn = BuildActionButton("RETURN TO MAIN MENU", danger: true);
                menuBtn.clicked += () =>
                {
                    _pendingAction = ConfirmAction.MainMenu;
                    RebuildSubTab();
                };
                root.Add(menuBtn);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ── Save/Load sub-tab ─────────────────────────────────────────────────
        // ═════════════════════════════════════════════════════════════════════

        private void BuildSaveLoadTab(VisualElement root)
        {
            if (_gm == null)
            {
                root.Add(BuildEmptyLabel("Game not loaded."));
                return;
            }

            // ── Autosave slot (read-only) ─────────────────────────────────────
            root.Add(BuildSectionHeader("AUTOSAVE"));
            BuildSaveSlotRow(root, GameManager.AutosaveSlotIndex, readOnly: true);

            // ── Manual save slots ─────────────────────────────────────────────
            root.Add(BuildSectionHeader("SAVE SLOTS"));
            for (int i = 1; i <= GameManager.SaveSlotCount; i++)
                BuildSaveSlotRow(root, i, readOnly: false);

            // ── New Game button ───────────────────────────────────────────────
            root.Add(BuildSectionHeader("NEW GAME"));
            var newGameBtn = BuildActionButton("NEW GAME");
            newGameBtn.clicked += () => SceneManager.LoadScene("MainMenuScene");
            root.Add(newGameBtn);
        }

        private void BuildSaveSlotRow(VisualElement root, int slotIndex, bool readOnly)
        {
            var info = _gm.GetSaveSlotInfo(slotIndex);
            bool isEmpty = info == null;
            bool isAutosave = slotIndex == GameManager.AutosaveSlotIndex;

            // ── Confirmation overlay for this slot ────────────────────────────
            if (_pendingSlotIndex == slotIndex)
            {
                string confirmMsg = _pendingAction switch
                {
                    ConfirmAction.Save   => $"Overwrite slot {slotIndex}? This cannot be undone.",
                    ConfirmAction.Load   => $"Load slot {slotIndex}? Current unsaved progress will be lost.",
                    ConfirmAction.Delete => $"Delete slot {slotIndex}? This cannot be undone.",
                    _                   => "Are you sure?",
                };
                root.Add(BuildConfirmRow(
                    confirmMsg,
                    onConfirm: () => ExecutePendingSlotAction(slotIndex),
                    onCancel:  () => { _pendingAction = ConfirmAction.None; _pendingSlotIndex = -1; RebuildSubTab(); }
                ));
                return;
            }

            // ── Slot card ─────────────────────────────────────────────────────
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Column;
            card.style.paddingTop    = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft   = 6;
            card.style.paddingRight  = 6;
            card.style.marginBottom  = 6;
            card.style.borderBottomWidth = 1;
            card.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);
            card.style.backgroundColor   = new Color(0.10f, 0.14f, 0.22f, 0.6f);

            // Header row: slot label + metadata
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems    = Align.Center;
            headerRow.style.marginBottom  = 4;

            string slotLabel = isAutosave ? "AUTOSAVE" : $"SLOT {slotIndex}";
            var slotLbl = new Label(slotLabel);
            slotLbl.style.color    = new Color(0.50f, 0.65f, 0.90f, 1f);
            slotLbl.style.fontSize = 10;
            slotLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            slotLbl.style.width    = 70;

            var metaLbl = new Label(isEmpty ? "— Empty —" : BuildSlotMetaText(info));
            metaLbl.style.color    = isEmpty
                ? new Color(0.40f, 0.45f, 0.55f, 1f)
                : new Color(0.75f, 0.80f, 0.90f, 1f);
            metaLbl.style.fontSize = 11;
            metaLbl.style.flexGrow = 1;

            headerRow.Add(slotLbl);
            headerRow.Add(metaLbl);
            card.Add(headerRow);

            // Timestamp row
            if (!isEmpty && info.savedAt.HasValue)
            {
                var tsLbl = new Label(info.savedAt.Value.ToLocalTime().ToString("g"));
                tsLbl.style.color    = new Color(0.45f, 0.50f, 0.60f, 1f);
                tsLbl.style.fontSize = 10;
                tsLbl.style.paddingLeft = 70;
                card.Add(tsLbl);
            }

            // Button row
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 4;

            if (!readOnly)
            {
                // SAVE button — gates behind confirmation when slot is occupied.
                var saveBtn = BuildActionButton("SAVE");
                saveBtn.style.marginRight = 4;
                saveBtn.clicked += () =>
                {
                    if (!isEmpty)
                    {
                        // Confirm overwrite
                        _pendingAction    = ConfirmAction.Save;
                        _pendingSlotIndex = slotIndex;
                        RebuildSubTab();
                    }
                    else
                    {
                        _gm.SaveGame(slotIndex);
                        RebuildSubTab();
                    }
                };
                btnRow.Add(saveBtn);
            }

            // LOAD button — always gates behind confirmation to avoid accidental state loss.
            var loadBtn = BuildActionButton("LOAD");
            loadBtn.SetEnabled(!isEmpty);
            loadBtn.style.marginRight = 4;
            if (!isEmpty)
            {
                loadBtn.clicked += () =>
                {
                    _pendingAction    = ConfirmAction.Load;
                    _pendingSlotIndex = slotIndex;
                    RebuildSubTab();
                };
            }
            btnRow.Add(loadBtn);

            // DELETE button (not shown for autosave)
            if (!readOnly && !isEmpty)
            {
                var delBtn = BuildActionButton("DELETE", danger: true);
                delBtn.clicked += () =>
                {
                    _pendingAction    = ConfirmAction.Delete;
                    _pendingSlotIndex = slotIndex;
                    RebuildSubTab();
                };
                btnRow.Add(delBtn);
            }

            card.Add(btnRow);

            // Active-missions warning banner — only relevant when slot has data and can be loaded.
            if (!readOnly && !isEmpty && HasActiveMissions())
            {
                var warnLbl = new Label("⚠ Active missions — loading will interrupt them.");
                warnLbl.style.color    = new Color(0.85f, 0.65f, 0.15f, 1f);
                warnLbl.style.fontSize = 10;
                warnLbl.style.paddingTop = 2;
                card.Add(warnLbl);
            }

            root.Add(card);
        }

        private void ExecutePendingSlotAction(int slotIndex)
        {
            switch (_pendingAction)
            {
                case ConfirmAction.Save:
                    _gm.SaveGame(slotIndex);
                    break;
                case ConfirmAction.Load:
                    _gm.LoadGame(slotIndex);
                    break;
                case ConfirmAction.Delete:
                    _gm.DeleteSaveSlot(slotIndex);
                    break;
            }
            _pendingAction    = ConfirmAction.None;
            _pendingSlotIndex = -1;
            RebuildSubTab();
        }

        private bool HasActiveMissions()
        {
            if (_gm?.Fleet == null || _station == null) return false;
            foreach (var ship in _gm.Fleet.GetOwnedShips(_station))
            {
                if (ship.status == "on_mission") return true;
            }
            return false;
        }

        private static string BuildSlotMetaText(SaveSlotInfo info)
        {
            string station = string.IsNullOrEmpty(info.stationName) ? "Unknown" : info.stationName;
            return $"{station}  ·  tick {info.tick}";
        }

        // ═════════════════════════════════════════════════════════════════════
        // ── Scenarios sub-tab ─────────────────────────────────────════════════
        // ═════════════════════════════════════════════════════════════════════

        private void BuildScenariosTab(VisualElement root)
        {
            // ── Current scenario card ─────────────────────────────────────────
            root.Add(BuildSectionHeader("CURRENT SCENARIO"));

            string currentScenarioId = _gm?.ActiveScenarioId;
            ScenarioDefinition currentScenario = null;
            if (!string.IsNullOrEmpty(currentScenarioId) &&
                _registry?.Scenarios != null &&
                _registry.Scenarios.TryGetValue(currentScenarioId, out var cs))
            {
                currentScenario = cs;
            }

            if (currentScenario != null)
                root.Add(BuildScenarioCard(currentScenario, isCurrent: true));
            else
                root.Add(BuildEmptyLabel("No scenario active."));

            // ── All available scenarios ────────────────────────────────────────
            root.Add(BuildSectionHeader("ALL SCENARIOS"));

            var scenarios = GetSortedScenarios();
            if (scenarios.Count == 0)
            {
                root.Add(BuildEmptyLabel(_registry == null || !_registry.IsLoaded
                    ? "Loading scenarios…"
                    : "No scenarios found."));
            }
            else
            {
                foreach (var scenario in scenarios)
                    root.Add(BuildScenarioCard(scenario, isCurrent: scenario.id == currentScenarioId));
            }

            // ── New Game button ────────────────────────────────────────────────
            root.Add(BuildSectionHeader("NEW GAME"));
            var newGameBtn = BuildActionButton("NEW GAME");
            newGameBtn.clicked += () => SceneManager.LoadScene("MainMenuScene");
            root.Add(newGameBtn);
        }

        private VisualElement BuildScenarioCard(ScenarioDefinition scenario, bool isCurrent)
        {
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Column;
            card.style.paddingTop    = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft   = 6;
            card.style.paddingRight  = 6;
            card.style.marginBottom  = 6;
            card.style.borderBottomWidth = 1;
            card.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);
            card.style.backgroundColor   = isCurrent
                ? new Color(0.10f, 0.18f, 0.30f, 0.8f)
                : new Color(0.09f, 0.12f, 0.18f, 0.5f);

            // Name row
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;
            nameRow.style.marginBottom  = 4;

            var nameLabel = new Label(scenario.name ?? scenario.id);
            nameLabel.style.color    = isCurrent ? new Color(0.39f, 0.75f, 1.00f, 1f) : new Color(0.80f, 0.85f, 0.95f, 1f);
            nameLabel.style.fontSize = 12;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexGrow = 1;

            // Difficulty stars
            var diffLabel = new Label(BuildDifficultyStars(scenario.difficultyRating));
            diffLabel.style.color    = DifficultyColor(scenario.difficultyRating);
            diffLabel.style.fontSize = 11;

            nameRow.Add(nameLabel);
            nameRow.Add(diffLabel);
            card.Add(nameRow);

            // Description
            if (!string.IsNullOrEmpty(scenario.description))
            {
                var descLabel = new Label(scenario.description);
                descLabel.style.color          = new Color(0.65f, 0.70f, 0.80f, 1f);
                descLabel.style.fontSize       = 11;
                descLabel.style.whiteSpace     = WhiteSpace.Normal;
                descLabel.style.marginBottom   = 4;
                card.Add(descLabel);
            }

            // Starting conditions summary
            var condLabel = new Label(BuildStartingConditionsSummary(scenario));
            condLabel.style.color    = new Color(0.50f, 0.60f, 0.75f, 1f);
            condLabel.style.fontSize = 10;
            card.Add(condLabel);

            return card;
        }

        private static string BuildDifficultyStars(int rating)
        {
            int clamped = Mathf.Clamp(rating, 1, 5);
            return new string('★', clamped) + new string('☆', 5 - clamped);
        }

        private static Color DifficultyColor(int rating)
        {
            return rating switch
            {
                1 => new Color(0.40f, 0.75f, 0.40f, 1f),
                2 => new Color(0.55f, 0.75f, 0.35f, 1f),
                3 => new Color(0.85f, 0.75f, 0.20f, 1f),
                4 => new Color(0.85f, 0.50f, 0.15f, 1f),
                _ => new Color(0.85f, 0.25f, 0.25f, 1f),
            };
        }

        private static string BuildStartingConditionsSummary(ScenarioDefinition scenario)
        {
            var parts = new List<string>();

            if (scenario.crewComposition != null && scenario.crewComposition.Count > 0)
                parts.Add($"Crew: {scenario.crewComposition.Count}");

            if (scenario.startingShips != null && scenario.startingShips.Count > 0)
                parts.Add($"Ships: {scenario.startingShips.Count}");

            if (scenario.startingResources != null && scenario.startingResources.Count > 0)
            {
                var resources = new List<string>();
                foreach (var kv in scenario.startingResources)
                    resources.Add($"{kv.Key}:{kv.Value:0}");
                parts.Add(string.Join(", ", resources));
            }

            return parts.Count > 0 ? string.Join("  ·  ", parts) : "Standard starting conditions";
        }

        private List<ScenarioDefinition> GetSortedScenarios()
        {
            if (_registry?.Scenarios == null) return new List<ScenarioDefinition>();
            var list = new List<ScenarioDefinition>(_registry.Scenarios.Values);
            list.Sort((a, b) =>
            {
                int cmp = a.difficultyRating.CompareTo(b.difficultyRating);
                return cmp != 0 ? cmp : string.Compare(a.name, b.name, StringComparison.Ordinal);
            });
            return list;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ── Shared helpers ────────────────────────────────────────────────────
        // ═════════════════════════════════════════════════════════════════════

        private static Label BuildSectionHeader(string title)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.color       = new Color(0.50f, 0.65f, 0.90f, 1f);
            header.style.fontSize    = 10;
            header.style.paddingTop  = 6;
            header.style.paddingBottom = 2;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            return header;
        }

        private static Label BuildEmptyLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList(EmptyClass);
            label.style.color     = new Color(0.45f, 0.50f, 0.60f, 1f);
            label.style.fontSize  = 10;
            label.style.paddingTop    = 4;
            label.style.paddingBottom = 4;
            return label;
        }

        private static Label BuildComingSoonLabel()
        {
            var label = new Label("Coming soon");
            label.style.color     = new Color(0.40f, 0.45f, 0.55f, 1f);
            label.style.fontSize  = 11;
            label.style.paddingTop    = 4;
            label.style.paddingBottom = 8;
            label.style.paddingLeft   = 4;
            return label;
        }

        private static Button BuildActionButton(string text, bool danger = false)
        {
            var btn = new Button { text = text };
            btn.AddToClassList(danger ? DangerBtnClass : ActionBtnClass);
            btn.style.paddingTop    = 5;
            btn.style.paddingBottom = 5;
            btn.style.paddingLeft   = 8;
            btn.style.paddingRight  = 8;
            btn.style.marginBottom  = 4;
            btn.style.backgroundColor = danger
                ? new Color(0.45f, 0.12f, 0.12f, 1f)
                : new Color(0.15f, 0.25f, 0.45f, 1f);
            btn.style.color    = Color.white;
            btn.style.fontSize = 11;
            return btn;
        }

        private static VisualElement BuildConfirmRow(string message, Action onConfirm, Action onCancel)
        {
            var container = new VisualElement();
            container.style.flexDirection  = FlexDirection.Column;
            container.style.marginBottom   = 8;
            container.style.paddingTop     = 6;
            container.style.paddingBottom  = 6;
            container.style.paddingLeft    = 6;
            container.style.paddingRight   = 6;
            container.style.backgroundColor = new Color(0.25f, 0.15f, 0.10f, 0.8f);
            container.style.borderBottomWidth = 1;
            container.style.borderBottomColor = new Color(0.50f, 0.30f, 0.15f, 1f);

            var msgLabel = new Label(message);
            msgLabel.style.color     = new Color(0.95f, 0.80f, 0.50f, 1f);
            msgLabel.style.fontSize  = 11;
            msgLabel.style.whiteSpace = WhiteSpace.Normal;
            msgLabel.style.marginBottom = 6;
            container.Add(msgLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;

            var confirmBtn = BuildActionButton("CONFIRM", danger: true);
            confirmBtn.style.marginRight = 6;
            confirmBtn.clicked += onConfirm;

            var cancelBtn = BuildActionButton("CANCEL");
            cancelBtn.clicked += onCancel;

            btnRow.Add(confirmBtn);
            btnRow.Add(cancelBtn);
            container.Add(btnRow);

            return container;
        }
    }
}
