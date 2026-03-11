// GameHUD — right-side taskbar with animated slide-out drawer.
//
// Taskbar: 64 px vertical strip flush to the right edge.
// Drawer:  300 px panel that slides out to the left of the taskbar when a tab
//          is active. Closes (slides back) when the same tab is clicked again.
//
// Tabs: Crew · Station · Ships · Log
//
// Self-installs via RuntimeInitializeOnLoadMethod; sets DemoBootstrap.HideOverlay
// so the legacy IMGUI stats box is suppressed.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Demo;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    public class GameHUD : MonoBehaviour
    {
        // ── Sizing ────────────────────────────────────────────────────────────
        private const float TabW    = 64f;
        private const float DrawerW = 300f;
        private const float Pad     = 12f;
        private const float AnimK   = 12f;   // lerp speed (higher = snappier)

        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color ColBar      = new Color(0.08f, 0.09f, 0.14f, 0.97f);
        private static readonly Color ColBarEdge  = new Color(0.20f, 0.30f, 0.48f, 1.00f);
        private static readonly Color ColDrawer   = new Color(0.10f, 0.11f, 0.17f, 0.97f);
        private static readonly Color ColDivider  = new Color(0.22f, 0.32f, 0.50f, 0.60f);
        private static readonly Color ColAccent   = new Color(0.35f, 0.62f, 1.00f, 1.00f);
        private static readonly Color ColTabHl    = new Color(0.18f, 0.27f, 0.46f, 1.00f);
        private static readonly Color ColBarBg    = new Color(0.13f, 0.15f, 0.22f, 1.00f);
        private static readonly Color ColBarFill  = new Color(0.24f, 0.56f, 0.86f, 1.00f);
        private static readonly Color ColBarWarn  = new Color(0.88f, 0.68f, 0.10f, 1.00f);
        private static readonly Color ColBarCrit  = new Color(0.86f, 0.26f, 0.26f, 1.00f);
        private static readonly Color ColBarGreen = new Color(0.22f, 0.76f, 0.35f, 1.00f);

        // ── Tab enum ──────────────────────────────────────────────────────────
        private enum Tab { None, Crew, Station, Ships, Log }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Crew,    "Crew"),
            (Tab.Station, "Stat."),
            (Tab.Ships,   "Ships"),
            (Tab.Log,     "Log"),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private GameManager _gm;
        private bool        _ready;
        private Tab         _active;
        private float       _drawerT;   // 0 = closed, 1 = fully open

        private Vector2 _crewScroll;
        private Vector2 _stationScroll;
        private Vector2 _shipsScroll;
        private Vector2 _logScroll;

        // ── Styles (built once on first OnGUI) ────────────────────────────────
        private Texture2D _white;
        private GUIStyle  _sTabOff, _sTabOn;
        private GUIStyle  _sHeader, _sLabel, _sSub;
        private GUIStyle  _sBtnSmall;
        private bool      _stylesReady;

        // ── Auto-install ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<GameHUD>() != null) return;
            new GameObject("GameHUD").AddComponent<GameHUD>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            DemoBootstrap.HideOverlay = true;
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy() => DemoBootstrap.HideOverlay = false;

        private IEnumerator WaitForGame()
        {
            while (GameManager.Instance == null ||
                   !GameManager.Instance.IsLoaded ||
                   GameManager.Instance.Station == null)
                yield return null;

            _gm   = GameManager.Instance;
            _ready = true;
        }

        // ── Update — drive drawer animation ───────────────────────────────────
        private void Update()
        {
            float target = _active != Tab.None ? 1f : 0f;
            _drawerT = Mathf.Lerp(_drawerT, target, Time.deltaTime * AnimK);
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            // ── Drawer (behind taskbar, slides left) ──────────────────────────
            if (_drawerT > 0.004f)
            {
                float dx = sw - TabW - DrawerW * _drawerT;
                DrawSolid(new Rect(dx, 0, DrawerW, sh), ColDrawer);
                DrawSolid(new Rect(dx, 0, 1f, sh), ColBarEdge);

                GUI.BeginGroup(new Rect(dx, 0, DrawerW, sh));
                DrawDrawer(DrawerW, sh);
                GUI.EndGroup();
            }

            // ── Taskbar ───────────────────────────────────────────────────────
            float tx = sw - TabW;
            DrawSolid(new Rect(tx, 0, TabW, sh), ColBar);
            DrawSolid(new Rect(tx, 0, 1f, sh), ColBarEdge);

            float ty = 20f;
            foreach (var (tab, label) in Tabs)
                DrawTabButton(tab, label, tx, ref ty);

            // Pause / resume button at bottom
            if (_ready && _gm != null)
            {
                string pl = _gm.IsPaused ? "► Play" : "⏸";
                Rect   pr = new Rect(tx + 6f, sh - 54f, TabW - 12f, 40f);
                if (GUI.Button(pr, pl, _sTabOff))
                    _gm.IsPaused = !_gm.IsPaused;
            }
        }

        // ── Tab button ────────────────────────────────────────────────────────
        private void DrawTabButton(Tab tab, string label, float x, ref float y)
        {
            bool   on = _active == tab;
            Rect   r  = new Rect(x + 5f, y, TabW - 10f, 54f);

            if (on)
            {
                DrawSolid(new Rect(x,       y, 3f,  54f), ColAccent);   // left accent
                DrawSolid(new Rect(x + 3f,  y, TabW - 3f, 54f), ColTabHl);
            }

            if (GUI.Button(r, label, on ? _sTabOn : _sTabOff))
                _active = on ? Tab.None : tab;   // toggle

            y += 58f;
        }

        // ── Drawer root ───────────────────────────────────────────────────────
        private void DrawDrawer(float w, float h)
        {
            // Header
            string title = _active.ToString();
            GUI.Label(new Rect(Pad, 18f, w - Pad * 2f, 26f), title, _sHeader);
            DrawSolid(new Rect(Pad, 50f, w - Pad * 2f, 1f), ColDivider);

            float  cw      = w - Pad * 2f;
            float  startY  = 58f;
            float  contentH = h - startY - 8f;
            Rect   area    = new Rect(Pad, startY, cw, contentH);

            if (!_ready) { GUI.Label(area, "Loading...", _sSub); return; }

            switch (_active)
            {
                case Tab.Crew:    DrawCrew(area, cw, contentH);    break;
                case Tab.Station: DrawStation(area, cw, contentH); break;
                case Tab.Ships:   DrawShips(area, cw, contentH);   break;
                case Tab.Log:     DrawLog(area, cw, contentH);     break;
            }
        }

        // ── Crew tab ──────────────────────────────────────────────────────────
        private void DrawCrew(Rect area, float w, float h)
        {
            var crew = _gm.Station.GetCrew();
            float innerH = crew.Count * 100f;

            _crewScroll = GUI.BeginScrollView(area, _crewScroll,
                          new Rect(0, 0, w, Mathf.Max(h, innerH)));

            float y = 0f;
            foreach (var npc in crew)
            {
                // Name + class
                string cls = (npc.classId ?? "").Replace("class.", "");
                GUI.Label(new Rect(0, y,      w,       20f), npc.name,              _sLabel);
                GUI.Label(new Rect(0, y + 20f, w * 0.5f, 17f), cls,                _sSub);
                GUI.Label(new Rect(w * 0.5f, y + 20f, w * 0.5f, 17f),
                          npc.MoodLabel(), _sSub);

                string job = _gm.Jobs.GetJobLabel(npc);
                GUI.Label(new Rect(0, y + 37f, w, 17f), $"Job: {job}",             _sSub);

                // Need bars
                NeedBar("Hunger", GetNeed(npc, "hunger"), w, y + 56f);
                NeedBar("Rest",   GetNeed(npc, "rest"),   w, y + 72f);

                DrawSolid(new Rect(0, y + 94f, w, 1f), ColDivider);
                y += 100f;
            }

            GUI.EndScrollView();
        }

        private static float GetNeed(NPCInstance npc, string key)
            => npc.needs.TryGetValue(key, out float v) ? v : 1f;

        private void NeedBar(string label, float value, float w, float y)
        {
            float lw = w * 0.36f, bx = w * 0.38f, bw = w * 0.50f, bh = 10f;
            GUI.Label(new Rect(0, y, lw, bh + 2f), label, _sSub);
            DrawSolid(new Rect(bx, y + 1f, bw, bh - 2f), ColBarBg);
            Color fc = value > 0.5f ? ColBarGreen : value > 0.25f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(bx, y + 1f, bw * value, bh - 2f), fc);
            GUI.Label(new Rect(bx + bw + 4f, y, 30f, bh + 2f),
                      Mathf.RoundToInt(value * 100f) + "%", _sSub);
        }

        // ── Station tab ───────────────────────────────────────────────────────
        private void DrawStation(Rect area, float w, float h)
        {
            var s = _gm.Station;
            float innerH = 24f + 6 * 22f + 16f + 24f + s.modules.Count * 24f;

            _stationScroll = GUI.BeginScrollView(area, _stationScroll,
                             new Rect(0, 0, w, Mathf.Max(h, innerH)));

            float y = 0f;

            // Time
            GUI.Label(new Rect(0, y, w, 20f),
                TimeSystem.TimeLabel(s) + $"   (tick {s.tick})", _sSub);
            y += 26f;

            // Resources
            ResourceBar("Credits", s.GetResource("credits"), 5000f, w, ref y);
            ResourceBar("Food",    s.GetResource("food"),     500f,  w, ref y);
            ResourceBar("Power",   s.GetResource("power"),    500f,  w, ref y);
            ResourceBar("Oxygen",  s.GetResource("oxygen"),   500f,  w, ref y);
            ResourceBar("Parts",   s.GetResource("parts"),    200f,  w, ref y);
            ResourceBar("Ice",     s.GetResource("ice"),      500f,  w, ref y);

            y += 8f;
            DrawSolid(new Rect(0, y, w, 1f), ColDivider); y += 10f;
            GUI.Label(new Rect(0, y, w, 20f), "Modules", _sLabel); y += 24f;

            foreach (var mod in s.modules.Values)
            {
                string status = !mod.active    ? "OFFLINE"
                              : mod.damage > 0f ? $"DMG {mod.damage:P0}"
                              : "OK";
                Color sc = !mod.active    ? ColBarCrit
                         : mod.damage > 0f ? ColBarWarn
                         : ColBarGreen;

                GUI.Label(new Rect(0, y, w * 0.72f, 20f), mod.displayName, _sSub);

                var prev = GUI.color;
                GUI.color = sc;
                GUI.Label(new Rect(w * 0.74f, y, w * 0.26f, 20f), status, _sSub);
                GUI.color = prev;

                y += 24f;
            }

            GUI.EndScrollView();
        }

        private void ResourceBar(string label, float value, float max,
                                 float w, ref float y)
        {
            float pct = Mathf.Clamp01(value / max);
            float lw  = w * 0.36f, bx = w * 0.38f, bw = w * 0.44f;
            GUI.Label(new Rect(0, y, lw, 18f), label, _sSub);
            DrawSolid(new Rect(bx, y + 3f, bw, 10f), ColBarBg);
            DrawSolid(new Rect(bx, y + 3f, bw * pct, 10f), ColBarFill);
            GUI.Label(new Rect(bx + bw + 4f, y, w - bx - bw - 4f, 18f),
                      value.ToString("F0"), _sSub);
            y += 22f;
        }

        // ── Ships tab ─────────────────────────────────────────────────────────
        private void DrawShips(Rect area, float w, float h)
        {
            var incoming = _gm.Station.GetIncomingShips();
            var docked   = _gm.Station.GetDockedShips();
            float innerH = 24f + incoming.Count * 70f + 16f + 24f + docked.Count * 48f;

            _shipsScroll = GUI.BeginScrollView(area, _shipsScroll,
                           new Rect(0, 0, w, Mathf.Max(h, innerH)));

            float y = 0f;

            // Incoming
            GUI.Label(new Rect(0, y, w, 20f), $"Incoming  ({incoming.Count})", _sLabel);
            y += 24f;

            foreach (var ship in incoming)
            {
                GUI.Label(new Rect(0, y, w, 18f), ship.name, _sSub); y += 18f;
                GUI.Label(new Rect(0, y, w, 16f),
                    $"{ship.role}  ·  {ship.intent}  ·  threat: {ship.ThreatLabel()}",
                    _sSub); y += 18f;

                float bw2 = (w - 6f) * 0.5f;
                if (GUI.Button(new Rect(0,        y, bw2, 24f), "Admit", _sBtnSmall))
                    _gm.AdmitShip(ship.uid);
                if (GUI.Button(new Rect(bw2 + 6f, y, bw2, 24f), "Deny",  _sBtnSmall))
                    _gm.DenyShip(ship.uid);
                y += 30f;
            }

            if (incoming.Count == 0)
            { GUI.Label(new Rect(0, y, w, 18f), "None", _sSub); y += 22f; }

            y += 8f;
            DrawSolid(new Rect(0, y, w, 1f), ColDivider); y += 10f;

            // Docked
            GUI.Label(new Rect(0, y, w, 20f), $"Docked  ({docked.Count})", _sLabel);
            y += 24f;

            foreach (var ship in docked)
            {
                GUI.Label(new Rect(0, y, w, 18f), ship.name, _sSub); y += 18f;
                GUI.Label(new Rect(0, y, w, 16f),
                    $"{ship.role}  ·  {ship.intent}  ·  T+{ship.ticksDocked}",
                    _sSub); y += 18f;
                DrawSolid(new Rect(0, y, w, 1f), ColDivider); y += 12f;
            }

            if (docked.Count == 0)
                GUI.Label(new Rect(0, y, w, 18f), "None", _sSub);

            GUI.EndScrollView();
        }

        // ── Log tab ───────────────────────────────────────────────────────────
        private void DrawLog(Rect area, float w, float h)
        {
            var log    = _gm.Station.log;
            float lh   = 17f;
            float innerH = Mathf.Max(h, log.Count * lh);

            _logScroll = GUI.BeginScrollView(area, _logScroll,
                         new Rect(0, 0, w, innerH));

            for (int i = 0; i < log.Count; i++)
                GUI.Label(new Rect(0, i * lh, w, lh), log[i], _sSub);

            GUI.EndScrollView();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void DrawSolid(Rect r, Color c)
        {
            if (_white == null) return;
            var prev  = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();

            _sTabOff = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.70f, 0.78f, 0.92f), background = null },
                hover     = { textColor = Color.white,                     background = null },
                active    = { textColor = Color.white,                     background = null },
            };
            _sTabOff.normal.background  = null;
            _sTabOff.focused.background = null;

            _sTabOn = new GUIStyle(_sTabOff)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white, background = null },
            };

            _sHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 16,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white },
            };

            _sLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.85f, 0.92f, 1.00f) },
            };

            _sSub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.62f, 0.70f, 0.84f) },
            };

            _sBtnSmall = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.80f, 0.88f, 1.00f) },
            };
        }
    }
}
