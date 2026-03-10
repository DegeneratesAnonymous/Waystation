// Game View Controller — in-game HUD and interaction layer.
// Observes GameManager events and updates the Unity UI panels accordingly.
// Handles player input for pausing, speed, docking decisions, and event choices.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    public class GameViewController : MonoBehaviour
    {
        // ── Inspector references ──────────────────────────────────────────────
        [Header("Status Bar")]
        [SerializeField] private TMP_Text stationNameLabel;
        [SerializeField] private TMP_Text tickLabel;
        [SerializeField] private TMP_Text creditsLabel;
        [SerializeField] private TMP_Text foodLabel;
        [SerializeField] private TMP_Text powerLabel;
        [SerializeField] private TMP_Text oxygenLabel;
        [SerializeField] private TMP_Text partsLabel;

        [Header("Speed Controls")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button speed1Button;
        [SerializeField] private Button speed2Button;
        [SerializeField] private Button speed4Button;
        [SerializeField] private TMP_Text pauseButtonLabel;

        [Header("Event Panel")]
        [SerializeField] private GameObject eventPanel;
        [SerializeField] private TMP_Text   eventTitleLabel;
        [SerializeField] private TMP_Text   eventDescLabel;
        [SerializeField] private Transform  choiceButtonContainer;
        [SerializeField] private Button     choiceButtonPrefab;

        [Header("Log Panel")]
        [SerializeField] private ScrollRect logScrollRect;
        [SerializeField] private TMP_Text   logText;
        [SerializeField] private int        maxLogLines = 50;

        [Header("Crew Panel")]
        [SerializeField] private Transform  crewListContainer;
        [SerializeField] private TMP_Text   crewEntryPrefab;

        [Header("Ships Panel")]
        [SerializeField] private Transform  shipListContainer;
        [SerializeField] private Button     shipEntryPrefab;

        [Header("Modules Panel")]
        [SerializeField] private Transform  moduleListContainer;
        [SerializeField] private TMP_Text   moduleEntryPrefab;

        [Header("Navigation")]
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button saveButton;

        [Header("Scene Names")]
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";

        // ── Private state ─────────────────────────────────────────────────────
        private GameManager   _gm;
        private PendingEvent  _activeEvent;
        private List<Button>  _spawnedChoiceButtons = new List<Button>();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            _gm = GameManager.Instance;
            if (_gm == null)
            {
                Debug.LogError("[GameViewController] GameManager not found!");
                return;
            }

            // Subscribe to GameManager events
            _gm.OnTick      += OnTick;
            _gm.OnNewEvent  += OnNewEvent;
            _gm.OnGameLoaded+= OnGameLoaded;

            // Button callbacks
            if (pauseButton)     pauseButton.onClick.AddListener(TogglePause);
            if (speed1Button)    speed1Button.onClick.AddListener(() => SetSpeed(1f));
            if (speed2Button)    speed2Button.onClick.AddListener(() => SetSpeed(2f));
            if (speed4Button)    speed4Button.onClick.AddListener(() => SetSpeed(4f));
            if (mainMenuButton)  mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            if (saveButton)      saveButton.onClick.AddListener(() => _gm.SaveGame());

            // Hide event panel initially
            if (eventPanel) eventPanel.SetActive(false);

            // Start new game from pending PlayerPrefs (set by MainMenuManager)
            StartCoroutine(StartGameFromPrefs());
        }

        private void OnDestroy()
        {
            if (_gm == null) return;
            _gm.OnTick       -= OnTick;
            _gm.OnNewEvent   -= OnNewEvent;
            _gm.OnGameLoaded -= OnGameLoaded;
        }

        // ── Game initialisation ───────────────────────────────────────────────

        private IEnumerator StartGameFromPrefs()
        {
            // Wait for GameManager to finish loading content
            while (_gm == null || !_gm.IsLoaded)
                yield return null;

            string name = PlayerPrefs.GetString("pending_station_name", "Waystation Alpha");
            string diff = PlayerPrefs.GetString("pending_difficulty",   "normal");
            int?   seed = PlayerPrefs.HasKey("pending_seed")
                          ? (int?)PlayerPrefs.GetInt("pending_seed")
                          : null;

            if (!string.IsNullOrEmpty(name))
                _gm.NewGame(name, seed);

            float speedMult = PlayerPrefs.GetFloat("tick_speed_multiplier", 1f);
            SetSpeed(speedMult);
        }

        // ── Tick update ───────────────────────────────────────────────────────

        private void OnTick(StationState station)
        {
            RefreshStatusBar(station);
            RefreshLog(station);
            RefreshCrewPanel(station);
            RefreshShipsPanel(station);
            RefreshModulesPanel(station);
            UpdatePauseLabel();
        }

        private void OnGameLoaded()
        {
            if (_gm.Station != null) OnTick(_gm.Station);
        }

        // ── Status bar ────────────────────────────────────────────────────────

        private void RefreshStatusBar(StationState s)
        {
            if (stationNameLabel) stationNameLabel.text = s.stationName;
            if (tickLabel)        tickLabel.text  = TimeSystem.TimeLabel(s);
            if (creditsLabel)     creditsLabel.text = $"¢{s.GetResource("credits"):F0}";
            if (foodLabel)        foodLabel.text    = $"Food: {s.GetResource("food"):F0}";
            if (powerLabel)       powerLabel.text   = $"Power: {s.GetResource("power"):F0}";
            if (oxygenLabel)      oxygenLabel.text  = $"O₂: {s.GetResource("oxygen"):F0}";
            if (partsLabel)       partsLabel.text   = $"Parts: {s.GetResource("parts"):F0}";
        }

        // ── Log panel ────────────────────────────────────────────────────────

        private void RefreshLog(StationState s)
        {
            if (logText == null) return;
            int count = Mathf.Min(maxLogLines, s.log.Count);
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++) lines.AppendLine(s.log[i]);
            logText.text = lines.ToString();

            // Scroll to top (most recent entry)
            if (logScrollRect != null)
                logScrollRect.verticalNormalizedPosition = 1f;
        }

        // ── Panel refresh throttling ──────────────────────────────────────────
        // Crew/ships/modules panels rebuild every N ticks to avoid per-frame
        // destroy-and-recreate overhead that causes flickering and GC pressure.
        private const int PanelRefreshInterval = 5;

        // ── Crew panel ────────────────────────────────────────────────────────

        private void RefreshCrewPanel(StationState s)
        {
            if (crewListContainer == null || crewEntryPrefab == null) return;
            if (s.tick % PanelRefreshInterval != 0) return;

            foreach (Transform child in crewListContainer)
                Destroy(child.gameObject);

            foreach (var npc in s.GetCrew())
            {
                var entry = Instantiate(crewEntryPrefab, crewListContainer);
                string jobLabel = _gm.Jobs.GetJobLabel(npc);
                entry.text = $"{npc.name}  [{npc.classId}]  {npc.MoodLabel()}  Job: {jobLabel}" +
                             (npc.injuries > 0 ? $"  ⚕{npc.injuries}" : "");
            }
        }

        // ── Ships panel ───────────────────────────────────────────────────────

        private void RefreshShipsPanel(StationState s)
        {
            if (shipListContainer == null || shipEntryPrefab == null) return;
            if (s.tick % PanelRefreshInterval != 0) return;

            foreach (Transform child in shipListContainer)
                Destroy(child.gameObject);

            foreach (var ship in s.ships.Values)
            {
                var btn = Instantiate(shipEntryPrefab, shipListContainer);
                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = $"{ship.name}  [{ship.role}]  {ship.status}  threat={ship.ThreatLabel()}";

                // Clicking an incoming ship admits it
                string uid = ship.uid;
                if (ship.status == "incoming")
                    btn.onClick.AddListener(() => OnShipClicked(uid));
            }
        }

        private void OnShipClicked(string shipUid)
        {
            bool ok = _gm.AdmitShip(shipUid);
            if (!ok) Debug.Log($"[GameViewController] Could not admit ship {shipUid}");
        }

        // ── Modules panel ─────────────────────────────────────────────────────

        private void RefreshModulesPanel(StationState s)
        {
            if (moduleListContainer == null || moduleEntryPrefab == null) return;
            if (s.tick % PanelRefreshInterval != 0) return;

            foreach (Transform child in moduleListContainer)
                Destroy(child.gameObject);

            foreach (var mod in s.modules.Values)
            {
                var entry = Instantiate(moduleEntryPrefab, moduleListContainer);
                string status = !mod.active ? "OFFLINE" :
                                mod.damage > 0f ? $"DMG {mod.damage:P0}" : "OK";
                entry.text = $"{mod.displayName}  [{mod.category}]  {status}";
            }
        }

        // ── Event panel ───────────────────────────────────────────────────────

        private void OnNewEvent(PendingEvent pending)
        {
            if (pending == null || pending.definition == null) return;

            _activeEvent = pending;

            if (eventPanel)       eventPanel.SetActive(true);
            if (eventTitleLabel)  eventTitleLabel.text = pending.definition.title;
            if (eventDescLabel)   eventDescLabel.text  = pending.definition.description;

            // Clear old choice buttons
            foreach (var btn in _spawnedChoiceButtons) if (btn) Destroy(btn.gameObject);
            _spawnedChoiceButtons.Clear();

            // Spawn choice buttons
            if (choiceButtonContainer != null && choiceButtonPrefab != null &&
                pending.definition.choices.Count > 0)
            {
                foreach (var choice in pending.definition.choices)
                {
                    var btn = Instantiate(choiceButtonPrefab, choiceButtonContainer);
                    var label = btn.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = choice.label;
                    string cid = choice.id;
                    btn.onClick.AddListener(() => OnChoiceSelected(cid));
                    _spawnedChoiceButtons.Add(btn);
                }
            }
            else
            {
                // Auto-resolve events with no choices: add a single "Continue" button
                if (choiceButtonContainer != null && choiceButtonPrefab != null)
                {
                    var btn = Instantiate(choiceButtonPrefab, choiceButtonContainer);
                    var label = btn.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = "Continue";
                    btn.onClick.AddListener(DismissEventPanel);
                    _spawnedChoiceButtons.Add(btn);
                }
            }
        }

        private void OnChoiceSelected(string choiceId)
        {
            if (_activeEvent == null || _gm.Station == null) return;
            _gm.ResolveEventChoice(_activeEvent, choiceId);
            DismissEventPanel();
        }

        private void DismissEventPanel()
        {
            _activeEvent = null;
            if (eventPanel) eventPanel.SetActive(false);
            _gm.IsPaused = false;
        }

        // ── Speed & pause controls ────────────────────────────────────────────

        private void TogglePause()
        {
            _gm.IsPaused = !_gm.IsPaused;
            UpdatePauseLabel();
        }

        private void SetSpeed(float multiplier)
        {
            // multiplier of 1× = 0.5s/tick; 2× = 0.25s/tick; 4× = 0.125s/tick
            _gm.SetSpeed(2f * multiplier);
        }

        private void UpdatePauseLabel()
        {
            if (pauseButtonLabel) pauseButtonLabel.text = _gm.IsPaused ? "▶" : "⏸";
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void ReturnToMainMenu()
        {
            _gm.SaveGame();
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
