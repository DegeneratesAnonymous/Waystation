// WaystationMainMenuHUD — pure IMGUI main menu with animated warp-speed background.
// Self-injects via RuntimeInitializeOnLoadMethod; requires no Inspector wiring.
// Only renders / acts while the active scene is "MainMenuScene".
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.UI
{
    public class WaystationMainMenuHUD : MonoBehaviour
    {
        // ── Self-bootstrap ────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // Prevent duplicates when restarting in the editor.
            if (FindAnyObjectByType<WaystationMainMenuHUD>() != null) return;
            var go = new GameObject("WaystationMainMenuHUD");
            go.AddComponent<WaystationMainMenuHUD>();
        }

        // ── Warp star data ────────────────────────────────────────────────────
        private struct WarpStar
        {
            public float angle;     // degrees — direction from screen centre
            public float dist;      // current distance from centre (pixels)
            public float speed;     // pixels per second
            public float trailLen;  // base trail length (pixels)
            public float width;     // streak width (pixels)
            public Color col;
        }

        private const int     StarCount = 180;
        private WarpStar[]    _stars;
        private System.Random _rng;

        // ── Menu state ────────────────────────────────────────────────────────
        private enum MenuState { Main, ScenarioSelect, NewGame, Settings }
        private MenuState _state      = MenuState.Main;
        private string    _stationName = "";
        private string    _seedText    = "";

        // ── Scenario selection state ──────────────────────────────────────────
        private string                    _selectedScenarioId = "";
        private List<ScenarioDefinition>  _cachedScenarios;
        private GUIStyle                  _sScenarioBtn, _sScenarioBtnSelected, _sScenarioDesc, _sScenarioDiff;

        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _sTitle, _sBtn, _sBtnDim, _sSub, _sFieldLbl, _sTextField;
        private Texture2D _pixel;
        private bool      _stylesReady;

        // ── Save path ─────────────────────────────────────────────────────────
        private static string SavePath =>
            Path.Combine(Application.persistentDataPath, "waystation_save.json");

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private bool _hasEnteredMainMenu = false;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenuScene")
            {
                _hasEnteredMainMenu = true;
                return;
            }
            // Only self-destruct once we leave MainMenuScene (not on first-load when starting from GameScene)
            if (_hasEnteredMainMenu)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            _rng = new System.Random();
            InitStars();
            _stationName = GenerateStationName();
        }

        private void Update()
        {
            if (_stars == null) return;
            float maxDist = MaxDist();
            for (int i = 0; i < _stars.Length; i++)
            {
                _stars[i].dist += _stars[i].speed * Time.deltaTime;
                if (_stars[i].dist > maxDist)
                    ResetStar(ref _stars[i]);
            }
        }

        // ── IMGUI rendering ───────────────────────────────────────────────────
        private void OnGUI()
        {
            if (SceneManager.GetActiveScene().name != "MainMenuScene") return;
            EnsureStyles();

            float sw = Screen.width, sh = Screen.height;

            // Warp background fills the entire screen
            DrawWarpBackground(sw, sh);

            // Central panel
            float panelW = 380f;
            float panelH = _state == MenuState.NewGame       ? 330f
                         : _state == MenuState.Settings      ? 160f
                         : _state == MenuState.ScenarioSelect ? 480f
                         : 420f;
            float px     = (sw - panelW) * 0.5f;
            float py     = (sh - panelH) * 0.5f - 20f;   // slightly above-centre

            // Backdrop
            FillRect(new Rect(px - 28f, py - 36f, panelW + 56f, panelH + 72f),
                     new Color(0.03f, 0.04f, 0.09f, 0.88f));
            // Top accent line
            FillRect(new Rect(px - 28f, py - 36f, panelW + 56f, 1f),
                     new Color(0.28f, 0.48f, 0.80f, 0.70f));
            // Bottom accent line
            FillRect(new Rect(px - 28f, py + panelH + 36f, panelW + 56f, 1f),
                     new Color(0.18f, 0.32f, 0.58f, 0.40f));

            // Game title
            GUI.Label(new Rect(px, py - 24f, panelW, 56f), "WAYSTATION", _sTitle);

            float y = py + 36f;

            switch (_state)
            {
                case MenuState.Main:           DrawMainButtons(px, ref y, panelW);       break;
                case MenuState.ScenarioSelect: DrawScenarioSelectPanel(px, ref y, panelW); break;
                case MenuState.NewGame:        DrawNewGamePanel(px, ref y, panelW);      break;
                case MenuState.Settings:       DrawSettingsPanel(px, ref y, panelW);     break;
            }

            // Version watermark (bottom-right)
            GUI.Label(new Rect(sw - 120f, sh - 28f, 116f, 20f),
                      $"v{Application.version}", _sSub);
        }

        // ── Main button list ──────────────────────────────────────────────────
        private void DrawMainButtons(float px, ref float y, float pw)
        {
            const float BtnH = 44f, Gap = 8f;

            bool hasSave = File.Exists(SavePath) && new FileInfo(SavePath).Length > 10;

            // Continue Game — dimmed when no save exists
            var prev = GUI.color;
            if (GUI.Button(new Rect(px, y, pw, BtnH),
                           hasSave ? "Continue Game" : "Continue Game  (no save)",
                           hasSave ? _sBtn : _sBtnDim) && hasSave)
                LoadSaveAndPlay();
            y += BtnH + Gap;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "New Game",        _sBtn))
            {
                _state = FeatureFlags.ScenarioSelection ? MenuState.ScenarioSelect : MenuState.NewGame;
            }
            y += BtnH + Gap;

            var prev2 = GUI.color;
            if (GUI.Button(new Rect(px, y, pw, BtnH),
                           hasSave ? "Load Game" : "Load Game  (no save)",
                           hasSave ? _sBtn : _sBtnDim) && hasSave)
                LoadSaveAndPlay();
            y += BtnH + Gap;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "Settings",        _sBtn)) { _state = MenuState.Settings; }
            y += BtnH + Gap;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "Steam Workshop",  _sBtn)) OpenWorkshop();
            y += BtnH + Gap;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "Exit to Desktop", _sBtn)) QuitGame();
        }

        // ── New Game panel ────────────────────────────────────────────────────
        private void DrawNewGamePanel(float px, ref float y, float pw)
        {
            const float FieldH = 30f, LblW = 120f, Gap = 10f, BtnH = 44f;

            GUI.Label(new Rect(px, y, LblW, FieldH), "Station Name", _sFieldLbl);
            _stationName = GUI.TextField(new Rect(px + LblW + 4f, y, pw - LblW - 4f, FieldH),
                                         _stationName, 40, _sTextField);
            y += FieldH + Gap;

            GUI.Label(new Rect(px, y, LblW, FieldH), "Seed  (optional)", _sFieldLbl);
            _seedText = GUI.TextField(new Rect(px + LblW + 4f, y, pw - LblW - 4f, FieldH),
                                      _seedText, 12, _sTextField);
            y += FieldH + Gap * 2f;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "Launch", _sBtn))
            {
                string name = _stationName.Trim().Length > 0 ? _stationName.Trim() : GenerateStationName();
                PlayerPrefs.SetString("pending_station_name", name);
                PlayerPrefs.DeleteKey("load_save");
                if (int.TryParse(_seedText, out int seed)) PlayerPrefs.SetInt("pending_seed", seed);
                else                                        PlayerPrefs.DeleteKey("pending_seed");
                if (FeatureFlags.ScenarioSelection && _selectedScenarioId.Length > 0)
                    PlayerPrefs.SetString("pending_scenario_id", _selectedScenarioId);
                else
                    PlayerPrefs.DeleteKey("pending_scenario_id");
                SceneManager.LoadScene("GameScene");
            }
            y += BtnH + Gap;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "← Back", _sBtn))
                _state = FeatureFlags.ScenarioSelection ? MenuState.ScenarioSelect : MenuState.Main;
        }

        // ── Scenario Selection panel ──────────────────────────────────────────
        private void DrawScenarioSelectPanel(float px, ref float y, float pw)
        {
            const float Gap = 8f, BtnH = 44f;

            EnsureScenarioStyles();

            // Heading
            GUI.Label(new Rect(px, y, pw, 24f), "Choose a Starting Scenario", _sFieldLbl);
            y += 30f;

            var scenarios = GetScenarios();
            if (scenarios == null || scenarios.Count == 0)
            {
                GUI.Label(new Rect(px, y, pw, 24f), "Loading scenarios…", _sSub);
                y += 30f;
            }
            else
            {
                // Each scenario entry is ~110px tall
                const float EntryH = 108f, EntryGap = 6f;

                for (int i = 0; i < scenarios.Count; i++)
                {
                    var sc  = scenarios[i];
                    bool sel = sc.id == _selectedScenarioId;

                    var btnStyle  = sel ? _sScenarioBtnSelected : _sScenarioBtn;
                    var entryRect = new Rect(px, y, pw, EntryH);

                    if (GUI.Button(entryRect, "", btnStyle))
                        _selectedScenarioId = sc.id;

                    // Name row
                    float iy = y + 10f;
                    GUI.Label(new Rect(px + 12f, iy, pw - 80f, 22f), sc.name, _sFieldLbl);

                    // Difficulty stars
                    string stars = new string('★', sc.difficultyRating)
                                 + new string('☆', 5 - sc.difficultyRating);
                    GUI.Label(new Rect(px + pw - 72f, iy, 68f, 22f), stars, _sScenarioDiff);

                    iy += 26f;
                    // Description (word-wrapped)
                    GUI.Label(new Rect(px + 12f, iy, pw - 24f, EntryH - 36f), sc.description, _sScenarioDesc);

                    y += EntryH + EntryGap;
                }
            }

            y += Gap;

            bool hasSelection = _selectedScenarioId.Length > 0;
            var  continueStyle = hasSelection ? _sBtn : _sBtnDim;
            if (GUI.Button(new Rect(px, y, pw, BtnH), "Continue →", continueStyle) && hasSelection)
            {
                _state = MenuState.NewGame;
                if (_stationName.Length == 0) _stationName = GenerateStationName();
            }
            y += BtnH + Gap;

            if (GUI.Button(new Rect(px, y, pw, BtnH), "← Back", _sBtn))
                _state = MenuState.Main;
        }

        // ── Settings panel ────────────────────────────────────────────────────
        private void DrawSettingsPanel(float px, ref float y, float pw)
        {
            const float BtnH = 44f;
            GUI.Label(new Rect(px, y, pw, 28f), "Settings coming soon.", _sSub);
            y += 36f;
            if (GUI.Button(new Rect(px, y, pw, BtnH), "← Back", _sBtn))
                _state = MenuState.Main;
        }

        // ── Actions ───────────────────────────────────────────────────────────
        private static void LoadSaveAndPlay()
        {
            PlayerPrefs.SetInt("load_save", 1);
            SceneManager.LoadScene("GameScene");
        }

        private static void OpenWorkshop()
        {
            // Replace 0 with the real Steam AppID when the game has one.
            Application.OpenURL("https://store.steampowered.com/app/0/workshop/");
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── Warp background ───────────────────────────────────────────────────
        private float MaxDist()
        {
            float hw = Screen.width  * 0.5f;
            float hh = Screen.height * 0.5f;
            return Mathf.Sqrt(hw * hw + hh * hh) + 40f;
        }

        private void InitStars()
        {
            _stars = new WarpStar[StarCount];
            for (int i = 0; i < StarCount; i++)
            {
                ResetStar(ref _stars[i]);
                // Scatter initial positions so the screen isn't empty on frame 1
                _stars[i].dist = (float)(_rng.NextDouble() * MaxDist());
            }
        }

        private void ResetStar(ref WarpStar s)
        {
            s.angle    = (float)(_rng.NextDouble() * 360.0);
            s.dist     = 0f;
            s.speed    = (float)(90.0 + _rng.NextDouble() * 340.0);
            s.trailLen = (float)(5.0  + _rng.NextDouble() * 22.0);
            s.width    = (float)(0.8  + _rng.NextDouble() * 1.6);
            float b    = (float)(0.55 + _rng.NextDouble() * 0.45);
            s.col      = new Color(b * 0.82f, b * 0.91f, b, 1f);
        }

        private void DrawWarpBackground(float sw, float sh)
        {
            // Deep space fill
            FillRect(new Rect(0, 0, sw, sh), new Color(0.01f, 0.01f, 0.04f));

            if (_stars == null || Event.current.type != EventType.Repaint) return;

            float cx = sw * 0.5f;
            float cy = sh * 0.5f;
            Matrix4x4 baseMatrix = GUI.matrix;

            for (int i = 0; i < _stars.Length; i++)
            {
                ref WarpStar s = ref _stars[i];
                if (s.dist < 1f) continue;

                // Fade in over first 60px, fade out near screen edge
                float fadeDist = MaxDist();
                float alpha    = Mathf.Clamp01(s.dist / 60f)
                               * Mathf.Clamp01(1f - (s.dist - fadeDist * 0.7f) / (fadeDist * 0.3f));
                if (alpha < 0.01f) continue;

                // Trail grows with distance (perspective effect)
                float trail = s.trailLen + s.dist * 0.045f;

                // Rotate around screen centre so angle=0 → straight up
                GUIUtility.RotateAroundPivot(s.angle, new Vector2(cx, cy));

                var col = new Color(s.col.r, s.col.g, s.col.b, alpha * s.col.a);
                // Draw streak: tip at dist above centre, trail extends toward centre
                FillRect(new Rect(cx - s.width * 0.5f,
                                  cy - s.dist,
                                  s.width,
                                  trail), col);

                GUI.matrix = baseMatrix;
            }
        }

        // ── Style helpers ─────────────────────────────────────────────────────
        private void EnsureScenarioStyles()
        {
            if (_sScenarioBtn != null) return;
            EnsureStyles();   // ensure base styles are ready first

            _sScenarioBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 13,
                alignment = TextAnchor.UpperLeft,
                wordWrap  = true,
                normal    = { background = MakeTex(new Color(0.06f, 0.09f, 0.18f, 0.90f)) },
                hover     = { background = MakeTex(new Color(0.10f, 0.16f, 0.32f, 0.95f)) },
                active    = { background = MakeTex(new Color(0.14f, 0.22f, 0.44f, 1.00f)) },
                border    = new RectOffset(2, 2, 2, 2),
                padding   = new RectOffset(0, 0, 0, 0),
            };

            _sScenarioBtnSelected = new GUIStyle(_sScenarioBtn)
            {
                normal  = { background = MakeTex(new Color(0.10f, 0.18f, 0.44f, 0.96f)) },
                hover   = { background = MakeTex(new Color(0.14f, 0.24f, 0.52f, 1.00f)) },
            };

            _sScenarioDesc = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 12,
                wordWrap  = true,
                normal    = { textColor = new Color(0.70f, 0.76f, 0.90f) },
            };

            _sScenarioDiff = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleRight,
                normal    = { textColor = new Color(1.00f, 0.82f, 0.30f) },
            };
        }

        /// <summary>
        /// Returns the loaded scenario list.  Reads from ContentRegistry when available;
        /// returns an empty list while the registry is still loading.
        /// </summary>
        private List<ScenarioDefinition> GetScenarios()
        {
            if (_cachedScenarios != null) return _cachedScenarios;
            var reg = GameManager.Instance?.Registry;
            if (reg == null || !reg.IsLoaded) return new List<ScenarioDefinition>();
            _cachedScenarios = new List<ScenarioDefinition>(reg.Scenarios.Values);
            return _cachedScenarios;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();

            _sTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 52,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.88f, 0.94f, 1.00f) },
            };

            _sBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 15,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor   = new Color(0.80f, 0.88f, 1.00f),
                              background  = MakeTex(new Color(0.07f, 0.10f, 0.22f, 0.92f)) },
                hover     = { textColor   = Color.white,
                              background  = MakeTex(new Color(0.14f, 0.22f, 0.46f, 0.96f)) },
                active    = { textColor   = Color.white,
                              background  = MakeTex(new Color(0.20f, 0.34f, 0.60f, 1.00f)) },
                border    = new RectOffset(2, 2, 2, 2),
                padding   = new RectOffset(12, 12, 8, 8),
            };

            _sBtnDim = new GUIStyle(_sBtn)
            {
                normal  = { textColor  = new Color(0.45f, 0.50f, 0.62f, 0.70f),
                            background = MakeTex(new Color(0.05f, 0.07f, 0.15f, 0.70f)) },
                hover   = { textColor  = new Color(0.55f, 0.60f, 0.72f, 0.80f),
                            background = MakeTex(new Color(0.07f, 0.10f, 0.20f, 0.80f)) },
                active  = { textColor  = new Color(0.55f, 0.60f, 0.72f),
                            background = MakeTex(new Color(0.07f, 0.10f, 0.20f, 0.85f)) },
            };

            _sSub = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.40f, 0.46f, 0.60f) },
            };

            _sFieldLbl = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = new Color(0.60f, 0.66f, 0.80f) },
            };

            _sTextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 13,
                normal   = { textColor  = Color.white,
                             background = MakeTex(new Color(0.08f, 0.10f, 0.20f, 1f)) },
                focused  = { textColor  = Color.white,
                             background = MakeTex(new Color(0.11f, 0.14f, 0.28f, 1f)) },
            };
        }

        private void FillRect(Rect r, Color col)
        {
            if (_pixel == null) { _pixel = new Texture2D(1, 1); _pixel.SetPixel(0, 0, Color.white); _pixel.Apply(); }
            var prev  = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(r, _pixel);
            GUI.color = prev;
        }

        private static Texture2D MakeTex(Color col)
        {
            var t = new Texture2D(4, 4);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    t.SetPixel(x, y, col);
            t.Apply();
            return t;
        }

        // ── Station name generator ────────────────────────────────────────────
        private static readonly string[] _adj  =
            { "Iron", "Pale", "Deep", "Drift", "Ember", "Threshold", "Broken", "Silent", "Void", "Amber" };
        private static readonly string[] _noun =
            { "Waystation", "Meridian", "Accord", "Frontier", "Crossing", "Beacon", "Margin", "Outpost" };

        private static string GenerateStationName()
        {
            return $"{_adj[Random.Range(0, _adj.Length)]} {_noun[Random.Range(0, _noun.Length)]}";
        }
    }
}
