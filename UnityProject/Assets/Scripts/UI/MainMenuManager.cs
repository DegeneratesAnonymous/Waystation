// Main Menu Manager — drives the main menu scene using Unity UI (uGUI + TextMeshPro).
// Handles new game creation, load game, settings, and quit.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Waystation.Core;

namespace Waystation.UI
{
    public class MainMenuManager : MonoBehaviour
    {
        // ── Inspector references ──────────────────────────────────────────────
        [Header("Panels")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject newGamePanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject loadingPanel;

        [Header("New Game Fields")]
        [SerializeField] private TMP_InputField stationNameInput;
        [SerializeField] private TMP_InputField seedInput;
        [SerializeField] private TMP_Dropdown   difficultyDropdown;

        [Header("Main Menu Buttons")]
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button loadGameButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Header("New Game Buttons")]
        [SerializeField] private Button startNewGameButton;
        [SerializeField] private Button backFromNewGameButton;

        [Header("Settings Buttons")]
        [SerializeField] private Button backFromSettingsButton;

        [Header("Settings Sliders")]
        [SerializeField] private Slider tickSpeedSlider;
        [SerializeField] private TMP_Text tickSpeedLabel;

        [Header("Version Label")]
        [SerializeField] private TMP_Text versionLabel;

        [Header("Scene Names")]
        [SerializeField] private string gameSceneName = "GameScene";

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            if (versionLabel != null) versionLabel.text = $"v{Application.version}";

            // Button callbacks
            if (newGameButton)         newGameButton.onClick.AddListener(ShowNewGamePanel);
            if (loadGameButton)        loadGameButton.onClick.AddListener(OnLoadGame);
            if (settingsButton)        settingsButton.onClick.AddListener(ShowSettingsPanel);
            if (quitButton)            quitButton.onClick.AddListener(OnQuit);
            if (startNewGameButton)    startNewGameButton.onClick.AddListener(OnStartNewGame);
            if (backFromNewGameButton) backFromNewGameButton.onClick.AddListener(ShowMainPanel);
            if (backFromSettingsButton)backFromSettingsButton.onClick.AddListener(ShowMainPanel);
            if (tickSpeedSlider)       tickSpeedSlider.onValueChanged.AddListener(UpdateTickSpeedLabel);

            PopulateDifficultyDropdown();
            ShowMainPanel();
        }

        // ── Panel navigation ──────────────────────────────────────────────────

        private void ShowMainPanel()
        {
            SetActivePanel(mainPanel);
        }

        private void ShowNewGamePanel()
        {
            if (stationNameInput != null)
                stationNameInput.text = GenerateDefaultStationName();
            SetActivePanel(newGamePanel);
        }

        private void ShowSettingsPanel()
        {
            SetActivePanel(settingsPanel);
        }

        private void SetActivePanel(GameObject target)
        {
            GameObject[] all = { mainPanel, newGamePanel, settingsPanel, loadingPanel };
            foreach (var p in all)
                if (p != null) p.SetActive(p == target);
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void OnStartNewGame()
        {
            string name = stationNameInput != null && stationNameInput.text.Length > 0
                          ? stationNameInput.text.Trim()
                          : GenerateDefaultStationName();
            int?   seed = null;
            if (seedInput != null && int.TryParse(seedInput.text, out int parsed))
                seed = parsed;

            string diff = GetSelectedDifficulty();
            PlayerPrefs.SetString("pending_station_name", name);
            PlayerPrefs.SetString("pending_difficulty",   diff);
            if (seed.HasValue) PlayerPrefs.SetInt("pending_seed", seed.Value);
            else               PlayerPrefs.DeleteKey("pending_seed");

            SetActivePanel(loadingPanel);
            StartCoroutine(LoadGameScene());
        }

        private void OnLoadGame()
        {
            // In the current implementation saves are loaded automatically
            // from GameManager in the game scene.
            PlayerPrefs.SetString("pending_station_name", "");
            PlayerPrefs.SetString("pending_load", "1");
            SetActivePanel(loadingPanel);
            StartCoroutine(LoadGameScene());
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void UpdateTickSpeedLabel(float value)
        {
            if (tickSpeedLabel != null)
                tickSpeedLabel.text = $"Tick Speed: {value:F1}×";
            PlayerPrefs.SetFloat("tick_speed_multiplier", value);
        }

        // ── Dropdown population ───────────────────────────────────────────────

        private void PopulateDifficultyDropdown()
        {
            if (difficultyDropdown == null) return;
            difficultyDropdown.ClearOptions();
            difficultyDropdown.AddOptions(new List<string> { "Easy", "Normal", "Hard", "Intense" });
            difficultyDropdown.value = 1;   // default Normal
        }

        private string GetSelectedDifficulty()
        {
            if (difficultyDropdown == null) return "normal";
            string[] opts = { "easy", "normal", "hard", "intense" };
            int idx = difficultyDropdown.value;
            return idx >= 0 && idx < opts.Length ? opts[idx] : "normal";
        }

        // ── Scene loading ────────────────────────────────────────────────────

        private IEnumerator LoadGameScene()
        {
            yield return new WaitForSeconds(0.3f);
            SceneManager.LoadScene(gameSceneName);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static readonly string[] StationAdjectives =
            { "Iron", "Pale", "Deep", "Drift", "Ember", "Threshold", "Broken", "Silent" };
        private static readonly string[] StationNouns =
            { "Waystation", "Meridian", "Accord", "Frontier", "Crossing", "Beacon", "Margin" };

        private static string GenerateDefaultStationName()
        {
            string adj  = StationAdjectives[UnityEngine.Random.Range(0, StationAdjectives.Length)];
            string noun = StationNouns[UnityEngine.Random.Range(0, StationNouns.Length)];
            return $"{adj} {noun}";
        }
    }
}
