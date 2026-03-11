// DemoBootstrap -- self-bootstrapping demo overlay.
// Uses RuntimeInitializeOnLoadMethod(BeforeSceneLoad) to create the Bootstrap
// GameObject (ContentRegistry + GameManager + DemoBootstrap) before any scene
// objects load. No scene YAML references or GUIDs required.
// Press Space to pause/unpause.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Demo
{
    public class DemoBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (GameManager.Instance != null) return;
            var go = new GameObject("Bootstrap");
            go.AddComponent<ContentRegistry>();
            go.AddComponent<GameManager>();
            go.AddComponent<DemoBootstrap>();
        }

        private const string StationName = "Frontier Waystation";
        private const int    Seed        = 42;
        private const int    MaxLog      = 18;

        private GameManager  _gm;
        private List<string> _log     = new List<string>();
        private bool         _started;

        private void Start()
        {
            _gm = GameManager.Instance;
            if (_gm == null)
            {
                Debug.LogError("[DemoBootstrap] GameManager not found!");
                return;
            }
            _gm.OnLogMessage += OnLogLine;
            _gm.OnGameLoaded += OnGameLoaded;
            if (_gm.IsLoaded && _gm.Station == null && !_started)
                LaunchNewGame();
        }

        private void OnDestroy()
        {
            if (_gm == null) return;
            _gm.OnLogMessage -= OnLogLine;
            _gm.OnGameLoaded -= OnGameLoaded;
        }

        private void OnGameLoaded()
        {
            if (!_started && _gm != null && _gm.Station == null)
                LaunchNewGame();
        }

        private void LaunchNewGame()
        {
            _started = true;
            _gm.NewGame(StationName, Seed);
        }

        private void OnLogLine(string msg)
        {
            _log.Insert(0, msg);
            if (_log.Count > MaxLog)
                _log.RemoveRange(MaxLog, _log.Count - MaxLog);
        }

        private void Update()
        {
            if (_gm != null && Input.GetKeyDown(KeyCode.Space))
                _gm.IsPaused = !_gm.IsPaused;
        }

        private void OnGUI()
        {
            if (_gm == null)
            {
                GUI.Label(new Rect(10, 10, 400, 24), "[DemoBootstrap] Waiting for GameManager...");
                return;
            }
            if (!_gm.IsLoaded || _gm.Station == null)
            {
                GUI.Label(new Rect(10, 10, 400, 24), "[DemoBootstrap] Loading content...");
                return;
            }

            StationState s = _gm.Station;

            float x = 10, y = 10, w = 360;
            GUI.Box(new Rect(x, y, w, 210), "  Waystation Demo");
            y += 26;
            Row(ref y, x + 8, w - 16, "Station   " + s.stationName);
            Row(ref y, x + 8, w - 16, "Tick      " + s.tick + "   Day " + (s.tick / 24 + 1) + "   Hour " + (s.tick % 24));
            Row(ref y, x + 8, w - 16, "Credits   " + s.GetResource("credits").ToString("F0"));
            Row(ref y, x + 8, w - 16, "Food      " + s.GetResource("food").ToString("F0") + "   Power   " + s.GetResource("power").ToString("F0"));
            Row(ref y, x + 8, w - 16, "Oxygen    " + s.GetResource("oxygen").ToString("F0") + "   Parts   " + s.GetResource("parts").ToString("F0"));
            Row(ref y, x + 8, w - 16, "Crew      " + s.GetCrew().Count + "   Docked   " + s.GetDockedShips().Count);
            Row(ref y, x + 8, w - 16, "Modules   " + s.modules.Count);
            y += 4;
            Row(ref y, x + 8, w - 16, _gm.IsPaused ? "PAUSED  (Space = resume)" : "RUNNING (Space = pause)");

            float ly = 240;
            float lh = MaxLog * 20 + 28;
            GUI.Box(new Rect(x, ly, 520, lh), "  Event Log");
            ly += 26;
            foreach (string line in _log)
            {
                GUI.Label(new Rect(x + 8, ly, 510, 20), line);
                ly += 20;
            }
        }

        private static void Row(ref float y, float x, float w, string text)
        {
            GUI.Label(new Rect(x, y, w, 20), text);
            y += 22;
        }
    }
}
