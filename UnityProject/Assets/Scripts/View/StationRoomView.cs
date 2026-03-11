// StationRoomView — visual top-down starting scene.
// Self-installs via RuntimeInitializeOnLoadMethod so no scene YAML wiring is needed.
// Renders a 7×7 tile room with wall borders and one colored circle per crew member.
// Name labels float above each dot in screen space.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.View
{
    public class StationRoomView : MonoBehaviour
    {
        // ── Room layout ───────────────────────────────────────────────────────
        private const int   RoomSize = 7;   // 7×7 tiles
        private const float TileGap  = 0.06f;
        private const float TileFill = 1f - TileGap;

        // Interior slots where crew stand (within 1..5 on each axis)
        private static readonly Vector2Int[] CrewSlots =
        {
            new Vector2Int(2, 3),   // left
            new Vector2Int(3, 3),   // centre
            new Vector2Int(4, 3),   // right
            new Vector2Int(2, 2),
            new Vector2Int(3, 2),
            new Vector2Int(4, 2),
        };

        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color BgColor    = new Color(0.04f, 0.04f, 0.07f);
        private static readonly Color FloorColor = new Color(0.20f, 0.22f, 0.28f);
        private static readonly Color WallColor  = new Color(0.11f, 0.12f, 0.17f);
        private static readonly Color DoorColor  = new Color(0.18f, 0.32f, 0.40f);

        private static readonly Color[] ClassColors =
        {
            new Color(0.30f, 0.65f, 1.00f),   // engineer   — blue
            new Color(0.25f, 0.90f, 0.45f),   // operations — green
            new Color(1.00f, 0.35f, 0.35f),   // security   — red
            new Color(1.00f, 0.80f, 0.20f),   // fallback   — gold
            new Color(0.80f, 0.40f, 1.00f),   // fallback   — purple
        };

        // ── Runtime state ─────────────────────────────────────────────────────
        private GameManager       _gm;
        private List<GameObject>  _dots        = new List<GameObject>();
        private List<NPCInstance> _crew        = new List<NPCInstance>();
        private string[]          _crewLabels  = new string[0];
        private bool              _ready;

        // ── Cached GUI style ──────────────────────────────────────────────────
        private GUIStyle _labelStyle;

        // ── Shared dot sprite (generated once, reused across rebuilds) ────────
        private static Sprite _dotSprite;

        // ── Auto-install ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<StationRoomView>() != null) return;
            new GameObject("StationRoomView").AddComponent<StationRoomView>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start()
        {
            // Defer initialization until a GameManager is present so that
            // we don't reconfigure Camera.main / build a room in non-game scenes
            // (e.g., main menu) that lack a GameManager.
            StartCoroutine(InitializeWhenReady());
        }

        private IEnumerator InitializeWhenReady()
        {
            // Wait until a GameManager exists AND has a fully loaded station.
            // DemoBootstrap creates a GameManager at BeforeSceneLoad time, so
            // checking for the GM alone is not sufficient — we must also wait
            // for IsLoaded + Station before touching Camera.main or building
            // room objects, so that non-game scenes (e.g. main menu) remain
            // untouched.
            while (true)
            {
                _gm = FindAnyObjectByType<GameManager>();
                if (_gm != null && _gm.IsLoaded && _gm.Station != null) break;
                yield return null;
            }

            // Component may have been destroyed/disabled while waiting.
            if (!this || !isActiveAndEnabled) yield break;

            SetupCamera();
            BuildRoom();

            _gm.OnTick += OnTick;
            SpawnCrewDots();
            _ready = true;
        }

        private void OnDestroy()
        {
            if (_gm != null) _gm.OnTick -= OnTick;
        }

        // ── Camera ────────────────────────────────────────────────────────────
        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            cam.orthographic    = true;
            cam.backgroundColor = BgColor;

            // Centre on room; orthographic size = half-height with padding
            float cx = (RoomSize - 1) * 0.5f;
            float cy = (RoomSize - 1) * 0.5f;
            cam.transform.position = new Vector3(cx, cy, -10f);
            cam.orthographicSize   = RoomSize * 0.5f + 1.5f;   // ~5
        }

        // ── Room tiles ────────────────────────────────────────────────────────
        private void BuildRoom()
        {
            var root = new GameObject("Room");
            var floorSpr = MakeSquare(FloorColor, TileFill);
            var wallSpr  = MakeSquare(WallColor,  TileFill);
            var doorSpr  = MakeSquare(DoorColor,  TileFill);

            for (int x = 0; x < RoomSize; x++)
            {
                for (int y = 0; y < RoomSize; y++)
                {
                    bool isWall = (x == 0 || x == RoomSize - 1 ||
                                   y == 0 || y == RoomSize - 1);
                    // Door gap: centre of south wall
                    bool isDoor = isWall && (x == RoomSize / 2 && y == 0);

                    Sprite spr  = isDoor ? doorSpr : (isWall ? wallSpr : floorSpr);
                    int    sort = isWall ? 0 : 1;

                    var tile = new GameObject($"Tile{x},{y}");
                    tile.transform.SetParent(root.transform);
                    tile.transform.localPosition = new Vector3(x, y, 0f);

                    var sr = tile.AddComponent<SpriteRenderer>();
                    sr.sprite       = spr;
                    sr.sortingOrder = sort;
                }
            }
        }

        // ── Crew dots ─────────────────────────────────────────────────────────
        private void SpawnCrewDots()
        {
            foreach (var d in _dots) if (d) Destroy(d);
            _dots.Clear();
            _crew.Clear();

            if (_gm?.Station == null) return;

            var crewList = _gm.Station.GetCrew();

            // Generate the shared sprite once and cache it for all rebuilds.
            if (_dotSprite == null) _dotSprite = MakeCircle(Color.white);

            _crewLabels = new string[crewList.Count];

            for (int i = 0; i < crewList.Count; i++)
            {
                var npc  = crewList[i];
                _crew.Add(npc);

                // Pre-build the label string so OnGUI() reuses it without allocating each frame.
                _crewLabels[i] = $"{npc.name}\n<size=9>{ClassLabel(npc.classId)}</size>";

                Vector2Int slot = i < CrewSlots.Length
                    ? CrewSlots[i]
                    : new Vector2Int(1 + (i % 5), 1 + i / 5);

                Color col = ClassColor(npc.classId, i);

                var go = new GameObject($"Crew_{npc.name}");
                go.transform.position = new Vector3(slot.x, slot.y, -0.2f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = _dotSprite;
                sr.color        = col;
                sr.sortingOrder = 5;

                _dots.Add(go);
            }
        }

        private void OnTick(StationState station)
        {
            if (station.GetCrew().Count != _crew.Count)
                SpawnCrewDots();
        }

        // ── Name labels ───────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!_ready || Camera.main == null) return;

            // Lazy-initialise once so we don't allocate a new GUIStyle each frame.
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter,
                    richText  = true,
                    normal    = { textColor = Color.white },
                };
            }

            for (int i = 0; i < _dots.Count && i < _crew.Count && i < _crewLabels.Length; i++)
            {
                if (!_dots[i]) continue;

                // World-to-screen; flip Y for IMGUI
                Vector3 wp = _dots[i].transform.position + Vector3.up * 0.6f;
                Vector3 sp = Camera.main.WorldToScreenPoint(wp);
                if (sp.z < 0f) continue;

                float gx = sp.x - 65f;
                float gy = Screen.height - sp.y - 10f;

                // Use pre-built label string — no per-frame allocation.
                GUI.Label(new Rect(gx, gy, 130f, 32f), _crewLabels[i], _labelStyle);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static Color ClassColor(string classId, int index)
        {
            if (classId != null)
            {
                if (classId.Contains("engineer"))   return ClassColors[0];
                if (classId.Contains("operations")) return ClassColors[1];
                if (classId.Contains("security"))   return ClassColors[2];
            }
            return ClassColors[index % ClassColors.Length];
        }

        private static string ClassLabel(string classId)
        {
            if (classId == null) return "";
            if (classId.Contains("engineer"))   return "Engineer";
            if (classId.Contains("operations")) return "Operations";
            if (classId.Contains("security"))   return "Security";
            return classId;
        }

        // ── Procedural sprites ────────────────────────────────────────────────
        private static Sprite MakeSquare(Color color, float fill)
        {
            const int res    = 32;
            int       border = Mathf.RoundToInt((1f - fill) * res * 0.5f);

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };

            for (int x = 0; x < res; x++)
                for (int y = 0; y < res; y++)
                {
                    bool edge = x < border || x >= res - border ||
                                y < border || y >= res - border;
                    tex.SetPixel(x, y, edge ? Color.clear : color);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res),
                                 new Vector2(0.5f, 0.5f), res);
        }

        private static Sprite MakeCircle(Color color)
        {
            const int   res    = 64;
            const float radius = res * 0.40f;   // outer edge
            const float inner  = radius * 0.55f; // darker core
            float       center = res * 0.5f;

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float dx = x - center, dy = y - center;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);

                    if (d > radius + 1f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float alpha = Mathf.Clamp01(radius + 0.5f - d); // AA edge
                    Color c = d < inner
                        ? new Color(color.r * 0.65f, color.g * 0.65f, color.b * 0.65f, alpha)
                        : new Color(color.r,          color.g,          color.b,          alpha);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res),
                                 new Vector2(0.5f, 0.5f), res);
        }
    }
}
