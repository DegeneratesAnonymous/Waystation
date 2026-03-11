// GameHUD — right-side taskbar with animated slide-out drawer.
//
// Taskbar: 68 px vertical strip flush to the right edge.
// Drawer:  320 px panel that slides out to the left of the taskbar.
//
// Tabs: Build · Crew · Station · Away Mission · Settings
//
// Self-installs via RuntimeInitializeOnLoadMethod; sets DemoBootstrap.HideOverlay
// so the legacy IMGUI stats box is suppressed.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private const float TabW    = 68f;
        private const float DrawerW = 320f;
        private const float Pad     = 12f;
        private const float AnimK   = 12f;

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
        private static readonly Color ColSummaryBg = new Color(0.07f, 0.09f, 0.15f, 0.85f);

        // ── Tab enum ──────────────────────────────────────────────────────────
        private enum Tab { None, Build, Crew, Station, AwayMission, Settings }

        private static readonly (Tab tab, string label)[] Tabs =
        {
            (Tab.Build,       "Build"),
            (Tab.Crew,        "Crew"),
            (Tab.Station,     "Station"),
            (Tab.AwayMission, "Away"),
            (Tab.Settings,    "Settings"),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private GameManager _gm;
        private bool        _ready;
        private Tab         _active;
        private float       _drawerT;

        private Vector2 _crewScroll;
        private Vector2 _stationScroll;
        private string  _inventorySearch = "";
        private Dictionary<string, bool> _cargoFoldouts = new Dictionary<string, bool>();

        // ── Styles (built once on first OnGUI) ────────────────────────────────
        private Texture2D _white;
        private GUIStyle  _sTabOff, _sTabOn;
        private GUIStyle  _sHeader, _sLabel, _sSub;
        private GUIStyle  _sBtnSmall, _sBtnWide, _sBtnDanger;
        private GUIStyle  _sTextField;
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
            StartCoroutine(WaitForGame());
        }

        private void OnDestroy() => DemoBootstrap.HideOverlay = false;

        private IEnumerator WaitForGame()
        {
            while (GameManager.Instance == null ||
                   !GameManager.Instance.IsLoaded ||
                   GameManager.Instance.Station == null)
                yield return null;

            // Suppress the legacy overlay only when we are in a scene with a
            // GameManager (i.e., not in the main menu or other non-game scenes).
            DemoBootstrap.HideOverlay = true;

            _gm    = GameManager.Instance;
            _ready = true;
        }

        // ── Update — drawer animation ─────────────────────────────────────────
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

            // ── Drawer (slides in from right) ─────────────────────────────────
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

            // Pause / resume at bottom
            if (_ready && _gm != null)
            {
                string pl = _gm.IsPaused ? "► Play" : "⏸ Pause";
                Rect   pr = new Rect(tx + 5f, sh - 54f, TabW - 10f, 40f);
                if (GUI.Button(pr, pl, _sTabOff))
                    _gm.IsPaused = !_gm.IsPaused;
            }
        }

        // ── Tab button ────────────────────────────────────────────────────────
        private void DrawTabButton(Tab tab, string label, float x, ref float y)
        {
            bool on = _active == tab;
            Rect r  = new Rect(x + 5f, y, TabW - 10f, 54f);

            if (on)
            {
                DrawSolid(new Rect(x,      y, 3f,         54f), ColAccent);
                DrawSolid(new Rect(x + 3f, y, TabW - 3f,  54f), ColTabHl);
            }

            if (GUI.Button(r, label, on ? _sTabOn : _sTabOff))
                _active = on ? Tab.None : tab;

            y += 58f;
        }

        // ── Drawer root ───────────────────────────────────────────────────────
        private void DrawDrawer(float w, float h)
        {
            string title = _active switch
            {
                Tab.Build       => "Build",
                Tab.Crew        => "Crew",
                Tab.Station     => "Station",
                Tab.AwayMission => "Away Mission",
                Tab.Settings    => "Settings",
                _               => "",
            };

            GUI.Label(new Rect(Pad, 18f, w - Pad * 2f, 26f), title, _sHeader);
            DrawSolid(new Rect(Pad, 50f, w - Pad * 2f, 1f), ColDivider);

            float cw      = w - Pad * 2f;
            float startY  = 58f;
            float contentH = h - startY - 8f;
            Rect  area    = new Rect(Pad, startY, cw, contentH);

            if (!_ready && _active != Tab.Build &&
                           _active != Tab.AwayMission &&
                           _active != Tab.Settings)
            { GUI.Label(area, "Loading...", _sSub); return; }

            switch (_active)
            {
                case Tab.Build:       DrawBuild(area, cw, contentH);       break;
                case Tab.Crew:        DrawCrew(area, cw, contentH);        break;
                case Tab.Station:     DrawStation(area, cw, contentH);     break;
                case Tab.AwayMission: DrawAwayMission(area, cw, contentH); break;
                case Tab.Settings:    DrawSettings(area, cw, contentH);    break;
            }
        }

        // ── Build tab ─────────────────────────────────────────────────────────
        private void DrawBuild(Rect area, float w, float h)
        {
            float y = area.y;

            GUI.Label(new Rect(area.x, y, w, 20f), "Expand Your Station", _sLabel);
            y += 28f;
            GUI.Label(new Rect(area.x, y, w, 80f),
                "Place new rooms, corridors, and modules to grow Frontier Waystation.\n\n" +
                "Build tools are coming in the next update.", _sSub);
        }

        // ── Crew tab ──────────────────────────────────────────────────────────
        private void DrawCrew(Rect area, float w, float h)
        {
            if (_gm?.Station == null) return;
            var crew = _gm.Station.GetCrew();

            // ── Summary strip (fixed above scroll) ────────────────────────────
            const float SumH = 78f;
            DrawSolid(new Rect(area.x, area.y, w, SumH), ColSummaryBg);

            float avgMood    = 0f;
            int   sickCount  = 0;
            int   injCount   = 0;

            foreach (var n in crew)
            {
                avgMood += n.mood;
                if (n.statusTags.Contains("sick")) sickCount++;
                if (n.injuries > 0)                injCount++;
            }
            if (crew.Count > 0) avgMood /= crew.Count;

            float happinessPct = (avgMood + 1f) * 50f;   // -1..1 → 0..100

            float sy = area.y + 7f;

            // Row 1 — headline stats
            GUI.Label(new Rect(area.x + 8f, sy, w - 8f, 16f),
                $"Crew: {crew.Count}   Happiness: {happinessPct:F0}%   Sick: {sickCount}   Injured: {injCount}",
                _sSub);
            sy += 20f;

            // Row 2 — happiness bar
            float bw = w - 16f;
            DrawSolid(new Rect(area.x + 8f, sy, bw, 8f), ColBarBg);
            Color hc = happinessPct >= 60f ? ColBarGreen
                     : happinessPct >= 35f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(area.x + 8f, sy, bw * (happinessPct / 100f), 8f), hc);
            sy += 14f;

            // Row 3 — health summary
            string healthLine = (sickCount == 0 && injCount == 0)
                ? "All crew healthy"
                : $"{sickCount} sick · {injCount} injured  —  check Medical Bay";
            GUI.Label(new Rect(area.x + 8f, sy, w - 8f, 16f), healthLine, _sSub);

            // ── Scrollable crew list ──────────────────────────────────────────
            const float RowH  = 116f;
            float listTop  = area.y + SumH + 6f;
            float listH    = h - SumH - 6f;
            float innerH   = Mathf.Max(listH, crew.Count * RowH);
            Rect  listArea = new Rect(area.x, listTop, w, listH);

            _crewScroll = GUI.BeginScrollView(listArea, _crewScroll,
                          new Rect(0, 0, w - 14f, innerH));

            float y = 0f;
            foreach (var npc in crew)
            {
                string cls  = (npc.classId ?? "").Replace("class.", "");
                string job  = _gm.Jobs.GetJobLabel(npc);

                // Name row
                GUI.Label(new Rect(0,       y,       w * 0.60f, 20f), npc.name,         _sLabel);
                GUI.Label(new Rect(w * 0.62f, y,     w * 0.38f, 18f), npc.MoodLabel(),  _sSub);

                // Class row
                GUI.Label(new Rect(0, y + 20f, w, 16f), cls, _sSub);

                // Job row + Reassign button
                GUI.Label(new Rect(0,        y + 38f, w * 0.66f, 16f), $"Job: {job}",   _sSub);
                if (GUI.Button(new Rect(w * 0.68f, y + 36f, w * 0.32f, 18f),
                               "Reassign", _sBtnSmall))
                    _gm.Jobs.InterruptNpc(npc);

                // Need bars
                NeedBar("Hunger", GetNeed(npc, "hunger"), w, y + 58f);
                NeedBar("Rest",   GetNeed(npc, "rest"),   w, y + 74f);

                // Status tags / injuries
                if (npc.statusTags.Count > 0 || npc.injuries > 0)
                {
                    string tags = string.Join(", ", npc.statusTags);
                    if (npc.injuries > 0)
                        tags += (tags.Length > 0 ? ", " : "") + $"{npc.injuries} injur{(npc.injuries == 1 ? "y" : "ies")}";

                    var prev  = GUI.color;
                    GUI.color = ColBarWarn;
                    GUI.Label(new Rect(0, y + 91f, w - 14f, 14f), tags, _sSub);
                    GUI.color = prev;
                }

                DrawSolid(new Rect(0, y + RowH - 4f, w - 14f, 1f), ColDivider);
                y += RowH;
            }

            if (crew.Count == 0)
                GUI.Label(new Rect(0, 0, w - 14f, 20f), "No crew assigned.", _sSub);

            GUI.EndScrollView();
        }

        private static float GetNeed(NPCInstance npc, string key)
            => npc.needs.TryGetValue(key, out float v) ? v : 1f;

        private void NeedBar(string label, float value, float w, float y)
        {
            float lw = w * 0.30f, bx = w * 0.32f, bw = w * 0.52f, bh = 10f;
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
            var (capUsed, capTotal) = _gm.Inventory.GetStationCapacity(s);
            var cargoHolds = _gm.Inventory.GetCargoHolds(s);

            // Estimate content height for the scroll view's inner rect
            float innerH = 24f + 22f                     // Wealth header + credits
                         + 16f                            // divider gap
                         + 24f + 5 * 22f                 // Resources header + 5 bars
                         + 16f                            // divider
                         + 24f                            // Inventory header
                         + cargoHolds.Count * 200f        // Estimate per cargo hold (expandable)
                         + 16f                            // divider
                         + 24f + s.modules.Count * 24f;  // Modules

            _stationScroll = GUI.BeginScrollView(area, _stationScroll,
                             new Rect(0, 0, w, Mathf.Max(h, innerH)));
            float y = 0f;

            // ── Station Wealth ────────────────────────────────────────────────
            Section("Station Wealth", w, ref y);
            ResourceBar("Credits", s.GetResource("credits"), 5000f, w, ref y);
            Divider(w, ref y);

            // ── Production / Resources ────────────────────────────────────────
            Section("Resources", w, ref y);
            ResourceBar("Food",   s.GetResource("food"),   500f, w, ref y);
            ResourceBar("Power",  s.GetResource("power"),  500f, w, ref y);
            ResourceBar("Oxygen", s.GetResource("oxygen"), 500f, w, ref y);
            ResourceBar("Parts",  s.GetResource("parts"),  200f, w, ref y);
            ResourceBar("Ice",    s.GetResource("ice"),    500f, w, ref y);
            Divider(w, ref y);

            // ── Station Inventory ─────────────────────────────────────────────
            Section($"Cargo Inventory  ({capUsed:F0} / {capTotal} units)", w, ref y);

            if (cargoHolds.Count == 0)
            {
                GUI.Label(new Rect(0, y, w, 18f), "No cargo holds built yet.", _sSub);
                y += 20f;
            }
            else
            {
                // Show each cargo hold as a collapsible section
                foreach (var hold in cargoHolds)
                {
                    DrawCargoHold(hold, w, ref y);
                }
            }

            Divider(w, ref y);

            // ── Module Status ─────────────────────────────────────────────────
            Section("Module Status", w, ref y);
            foreach (var mod in s.modules.Values)
            {
                string status = !mod.active    ? "OFFLINE"
                              : mod.damage > 0f ? $"DMG {mod.damage:P0}"
                              : "OK";
                Color sc = !mod.active    ? ColBarCrit
                         : mod.damage > 0f ? ColBarWarn
                         : ColBarGreen;

                GUI.Label(new Rect(0, y, w * 0.72f, 20f), mod.displayName, _sSub);
                var prev  = GUI.color;
                GUI.color = sc;
                GUI.Label(new Rect(w * 0.74f, y, w * 0.26f, 20f), status, _sSub);
                GUI.color = prev;
                y += 24f;
            }

            GUI.EndScrollView();
        }

        // ── Cargo Hold Detail ─────────────────────────────────────────────────
        private void DrawCargoHold(ModuleInstance hold, float w, ref float y)
        {
            // Initialize foldout state if needed
            if (!_cargoFoldouts.ContainsKey(hold.uid))
                _cargoFoldouts[hold.uid] = false;

            int capTotal = _gm.Inventory.GetCapacityTotal(hold);
            float capUsed = _gm.Inventory.GetCapacityUsed(hold);
            float pct = capTotal > 0 ? capUsed / capTotal : 0f;

            // Header with foldout triangle and capacity bar
            string triangle = _cargoFoldouts[hold.uid] ? "▼" : "▶";
            string headerText = $"{triangle}  {hold.displayName}  —  {capUsed:F0} / {capTotal}";

            // Clickable header
            Rect headerRect = new Rect(0, y, w, 18f);
            if (GUI.Button(headerRect, headerText, _sLabel))
                _cargoFoldouts[hold.uid] = !_cargoFoldouts[hold.uid];

            y += 20f;

            // Capacity bar
            DrawSolid(new Rect(4f, y, w - 8f, 6f), ColBarBg);
            Color capColor = pct >= 0.95f ? ColBarCrit : pct >= 0.75f ? ColBarWarn : ColBarFill;
            DrawSolid(new Rect(4f, y, (w - 8f) * pct, 6f), capColor);
            y += 10f;

            // Settings info (if configured)
            if (hold.cargoSettings != null && hold.cargoSettings.allowedTypes != null && hold.cargoSettings.allowedTypes.Count > 0)
            {
                string allowText = "Allow: " + string.Join(", ", hold.cargoSettings.allowedTypes);
                var prevColor = GUI.color;
                GUI.color = new Color(0.62f, 0.70f, 0.84f, 0.8f);
                GUI.Label(new Rect(4f, y, w - 8f, 14f), allowText, _sSub);
                GUI.color = prevColor;
                y += 16f;
            }

            // If expanded, show item details
            if (_cargoFoldouts[hold.uid])
            {
                if (hold.inventory.Count == 0)
                {
                    GUI.Label(new Rect(8f, y, w - 16f, 16f), "(empty)", _sSub);
                    y += 18f;
                }
                else
                {
                    // Column headers
                    DrawSolid(new Rect(8f, y, w - 16f, 14f), new Color(0.07f, 0.09f, 0.15f, 0.7f));
                    GUI.Label(new Rect(12f, y + 1f, 90f, 12f), "ITEM", _sSub);
                    GUI.Label(new Rect(104f, y + 1f, 60f, 12f), "TYPE", _sSub);
                    GUI.Label(new Rect(166f, y + 1f, 40f, 12f), "QTY", _sSub);
                    GUI.Label(new Rect(208f, y + 1f, 50f, 12f), "VALUE", _sSub);
                    GUI.Label(new Rect(260f, y + 1f, 40f, 12f), "MASS", _sSub);
                    y += 16f;

                    // Sort items by type then name
                    var sortedItems = hold.inventory.ToList();
                    sortedItems.Sort((a, b) =>
                    {
                        var aDefn = _gm.Registry.Items.TryGetValue(a.Key, out var ad) ? ad : null;
                        var bDefn = _gm.Registry.Items.TryGetValue(b.Key, out var bd) ? bd : null;

                        string aType = aDefn?.itemType ?? "";
                        string bType = bDefn?.itemType ?? "";
                        int typeComp = aType.CompareTo(bType);
                        if (typeComp != 0) return typeComp;

                        string aName = aDefn?.displayName ?? a.Key;
                        string bName = bDefn?.displayName ?? b.Key;
                        return aName.CompareTo(bName);
                    });

                    // Item rows
                    foreach (var kv in sortedItems)
                    {
                        string itemId = kv.Key;
                        int qty = kv.Value;

                        var itemDefn = _gm.Registry.Items.TryGetValue(itemId, out var defn) ? defn : null;
                        string name = itemDefn?.displayName ?? itemId;
                        string itype = itemDefn?.itemType ?? "?";
                        float weight = itemDefn?.weight ?? 1f;
                        float value = itemDefn?.value ?? 0f;
                        bool legal = itemDefn?.legal ?? true;
                        bool perishable = itemDefn != null && itemDefn.perishableTicks > 0;

                        // Color by legality and perishability
                        Color nameColor;
                        if (!legal)
                            nameColor = ColBarCrit;  // Red for illegal
                        else if (perishable)
                            nameColor = ColBarWarn;  // Yellow/orange for perishable
                        else
                            nameColor = new Color(0.85f, 0.92f, 1.00f);

                        // Truncate long names
                        if (name.Length > 13)
                            name = name.Substring(0, 12) + "…";
                        if (itype.Length > 8)
                            itype = itype.Substring(0, 7) + "…";

                        var prevColor = GUI.color;
                        GUI.color = nameColor;
                        GUI.Label(new Rect(12f, y, 90f, 14f), name, _sSub);
                        GUI.color = prevColor;

                        GUI.Label(new Rect(104f, y, 60f, 14f), itype, _sSub);
                        GUI.Label(new Rect(166f, y, 40f, 14f), qty.ToString(), _sSub);

                        prevColor = GUI.color;
                        GUI.color = ColAccent;
                        GUI.Label(new Rect(208f, y, 50f, 14f), $"{value * qty:F0}c", _sSub);
                        GUI.color = prevColor;

                        GUI.Label(new Rect(260f, y, 40f, 14f), $"{weight * qty:F1}", _sSub);

                        y += 15f;
                    }
                }
            }

            y += 6f;  // Spacing between cargo holds
        }

        // ── Away Mission tab ──────────────────────────────────────────────────
        private void DrawAwayMission(Rect area, float w, float h)
        {
            float y = area.y;
            GUI.Label(new Rect(area.x, y, w, 20f), "Plan an Expedition", _sLabel);
            y += 28f;
            GUI.Label(new Rect(area.x, y, w, 100f),
                "Send a crew team on mining runs, trade routes, or reconnaissance missions " +
                "beyond the station.\n\nAway mission planning is coming in a future update.",
                _sSub);
        }

        // ── Settings tab ──────────────────────────────────────────────────────
        private void DrawSettings(Rect area, float w, float h)
        {
            float y = area.y;

            // Graphics
            GUI.Label(new Rect(area.x, y, w, 20f), "Graphics", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Graphics settings coming soon.", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Controls
            GUI.Label(new Rect(area.x, y, w, 20f), "Controls", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Scroll wheel — zoom in / out", _sSub); y += 18f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Right-drag — pan camera", _sSub); y += 18f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Space — pause / resume", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Sound
            GUI.Label(new Rect(area.x, y, w, 20f), "Sound", _sLabel); y += 24f;
            GUI.Label(new Rect(area.x, y, w, 16f), "Sound settings coming soon.", _sSub); y += 22f;
            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Save / Load
            GUI.Label(new Rect(area.x, y, w, 20f), "Game", _sLabel); y += 24f;

            if (_ready && _gm != null)
            {
                if (GUI.Button(new Rect(area.x, y, w, 28f), "Save Game", _sBtnWide))
                    _gm.SaveGame();
                y += 34f;

                // Load is not yet implemented — show as disabled stub
                var prevColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                GUI.Button(new Rect(area.x, y, w, 28f), "Load Game  (coming soon)", _sBtnWide);
                GUI.color = prevColor;
                y += 34f;
            }

            DrawSolid(new Rect(area.x, y, w, 1f), ColDivider); y += 14f;

            // Exit
            GUI.Label(new Rect(area.x, y, w, 20f), "Exit", _sLabel); y += 24f;
            if (GUI.Button(new Rect(area.x, y, w, 28f), "Exit to Desktop", _sBtnDanger))
                Application.Quit();
        }

        // ── Drawer helpers ────────────────────────────────────────────────────
        private void Section(string title, float w, ref float y)
        {
            GUI.Label(new Rect(0, y, w, 20f), title, _sLabel);
            y += 24f;
        }

        private void Divider(float w, ref float y)
        {
            y += 6f;
            DrawSolid(new Rect(0, y, w, 1f), ColDivider);
            y += 10f;
        }

        private void ResourceBar(string label, float value, float max, float w, ref float y)
        {
            float pct = Mathf.Clamp01(value / max);
            float lw  = w * 0.34f, bx = w * 0.36f, bw = w * 0.44f;
            GUI.Label(new Rect(0, y, lw, 18f), label, _sSub);
            DrawSolid(new Rect(bx, y + 3f, bw, 10f), ColBarBg);
            Color fc = pct > 0.5f ? ColBarFill : pct > 0.25f ? ColBarWarn : ColBarCrit;
            DrawSolid(new Rect(bx, y + 3f, bw * pct, 10f), fc);
            GUI.Label(new Rect(bx + bw + 4f, y, w - bx - bw - 4f, 18f),
                      value.ToString("F0"), _sSub);
            y += 22f;
        }

        // ── Utilities ─────────────────────────────────────────────────────────
        private string ItemDisplayName(string itemId)
        {
            // Attempt to look up the human-readable name from the registry
            if (_gm?.Registry?.Items != null &&
                _gm.Registry.Items.TryGetValue(itemId, out var defn))
                return defn.displayName;

            // Fallback: humanise the id ("food_ration" → "Food Ration")
            string humanised = itemId.Replace("_", " ").Replace(".", " ");
            if (humanised.Length == 0) return itemId;
            return char.ToUpper(humanised[0]) + humanised.Substring(1);
        }

        private void DrawSolid(Rect r, Color c)
        {
            if (_white == null) return;
            var prev  = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        // ── Style setup ───────────────────────────────────────────────────────
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
                wordWrap = true,
            };

            _sBtnSmall = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.80f, 0.88f, 1.00f) },
            };

            _sBtnWide = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.85f, 0.92f, 1.00f) },
            };

            _sBtnDanger = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1.00f, 0.55f, 0.55f) },
            };

            _sTextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.85f, 0.90f, 1.00f) },
            };
        }
    }
}
