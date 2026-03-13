// StationRoomView — top-down station visual renderer.
//
// Renders three layers (back to front):
//   1. Floor / wall tiles     — sortOrder 0
//   2. Shadow overlays        — sortOrder 1  (fade gradient where floor meets wall)
//   3. Door tiles             — sortOrder 2  (transparent gap shows floor beneath)
//   Crew dots                 — sortOrder 5
//
// Wall tiles: flat 64×64 block, 5 variants, assigned deterministically per cell.
//   Do NOT rotate — scuff marks are directional.
// Floor tiles: 64×64 panel, 5 variants repeated with random rotation per cell.
//   1px seam on all sides → adjacent tiles form a 2px grout line naturally.
// Shadow overlays: composited on floor cells adjacent to walls (one per direction).
// Doors: floor tile placed first, then door sprite on top.
//   Each door opens independently when an NPC is one tile away (Manhattan dist ≤ 1).
//   Door animates open on approach, closes when the NPC moves away.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.View
{
    public class StationRoomView : MonoBehaviour
    {
        // ── Room dimensions ───────────────────────────────────────────────────
        private const int RoomCols = 7;
        private const int RoomRows = 7;
        // Interior tile range (the floor area inside the wall border)
        private const int IntMinC = 1, IntMaxC = RoomCols - 2;
        private const int IntMinR = 1, IntMaxR = RoomRows - 2;

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color BgColor = new Color(0.04f, 0.04f, 0.07f);

        // Interior slots where crew stand
        private static readonly Vector2Int[] CrewSlots =
        {
            new Vector2Int(2, 3),
            new Vector2Int(3, 3),
            new Vector2Int(4, 3),
            new Vector2Int(2, 2),
            new Vector2Int(3, 2),
            new Vector2Int(4, 2),
        };

        private static readonly Color[] ClassColors =
        {
            new Color(0.30f, 0.65f, 1.00f),   // engineer  — blue
            new Color(0.25f, 0.90f, 0.45f),   // sciences  — cyan-green
            new Color(1.00f, 0.35f, 0.35f),   // security  — red
            new Color(1.00f, 0.80f, 0.20f),   // fallback  — gold
            new Color(0.80f, 0.40f, 1.00f),   // fallback  — purple
        };

        // ── Runtime state ─────────────────────────────────────────────────────
        private GameManager       _gm;
        private List<GameObject>  _dots       = new List<GameObject>();
        private List<NPCInstance> _crew       = new List<NPCInstance>();
        private string[]          _crewLabels = new string[0];
        private bool              _ready;
        private GUIStyle          _labelStyle;
        private static Sprite     _dotSprite;
        // ── Door animation ────────────────────────────────────────────────────
        // Each door opens independently when an NPC is one tile away (Manhattan ≤ 1).
        private const float DoorFrameTime = 0.13f; // 130 ms per step — matches spec

        private class DoorEntry
        {
            public SpriteRenderer sr;
            public Sprite[]       frames;
            public int            col, row;
            public int            frameIdx; // 0 = closed, 4 = fully open
            public float          timer;
            // Shadow SpriteRenderers on this door tile (sortOrder 3). Shown only when closed.
            public readonly List<SpriteRenderer> shadows = new List<SpriteRenderer>();
        }
        private readonly List<DoorEntry> _doorEntries = new List<DoorEntry>();
        // Floor variant cache: (col,row) → 0..4 assigned during BuildRoom / RebuildFoundationTiles
        private readonly Dictionary<(int, int), int> _floorVariants
            = new Dictionary<(int, int), int>();

        // Foundation tile GameObjects keyed by foundation uid
        private readonly Dictionary<string, GameObject>        _foundTiles   = new Dictionary<string, GameObject>();
        // Extra per-foundation GOs: floor-under-door tiles and shadow overlays
        private readonly Dictionary<string, List<GameObject>> _foundExtras  = new Dictionary<string, List<GameObject>>();
        private GameObject _foundRoot;

        // ── Auto-install ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<StationRoomView>() != null) return;
            new GameObject("StationRoomView").AddComponent<StationRoomView>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Start() => StartCoroutine(InitializeWhenReady());

        private IEnumerator InitializeWhenReady()
        {
            while (true)
            {
                _gm = FindAnyObjectByType<GameManager>();
                if (_gm != null && _gm.IsLoaded && _gm.Station != null) break;
                yield return null;
            }

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

        private void Update()
        {
            if (!_ready || _doorEntries.Count == 0) return;
            for (int i = _doorEntries.Count - 1; i >= 0; i--)
            {
                var e = _doorEntries[i];
                if (!e.sr) { _doorEntries.RemoveAt(i); continue; }

                // Open when any crew dot is within 1 tile (Manhattan distance).
                bool npcNear = false;
                foreach (var dot in _dots)
                {
                    if (!dot) continue;
                    Vector3 dp = dot.transform.position;
                    if (Mathf.Abs(Mathf.RoundToInt(dp.x) - e.col) +
                        Mathf.Abs(Mathf.RoundToInt(dp.y) - e.row) <= 1)
                    { npcNear = true; break; }
                }

                e.timer += Time.deltaTime;
                while (e.timer >= DoorFrameTime)
                {
                    e.timer -= DoorFrameTime;
                    if (npcNear  && e.frameIdx < 4) e.frameIdx++;
                    else if (!npcNear && e.frameIdx > 0) e.frameIdx--;
                }
                e.sr.sprite = e.frames[e.frameIdx];
                // Hide door shadows while the door is opening/open
                bool closed = e.frameIdx == 0;
                foreach (var sh in e.shadows) if (sh) sh.enabled = closed;
            }
        }

        // ── Camera ────────────────────────────────────────────────────────────
        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            cam.orthographic    = true;
            cam.backgroundColor = BgColor;

            float cx = (RoomCols - 1) * 0.5f;
            float cy = (RoomRows - 1) * 0.5f;
            cam.transform.position = new Vector3(cx, cy, -10f);
            cam.orthographicSize   = RoomCols * 0.5f + 1.5f;
        }

        // ── Starter room construction ─────────────────────────────────────────
        private void BuildRoom()
        {
            var root = new GameObject("Room");
            _foundRoot = new GameObject("Foundations");

            for (int c = 0; c < RoomCols; c++)
            for (int r = 0; r < RoomRows; r++)
            {
                bool isWall     = c == 0 || c == RoomCols - 1 || r == 0 || r == RoomRows - 1;
                bool isDoor     = r == 0 && c == RoomCols / 2;  // centre of south wall
                bool isInterior = c >= IntMinC && c <= IntMaxC && r >= IntMinR && r <= IntMaxR;

                if (isDoor)
                {
                    // Floor tile first (transparent gap reveals it)
                    PlaceTile(root.transform, c, r, TileAtlas.GetFloor(PickFloorVariant(c, r)),
                        FloorRotation(c, r), sortOrder: 0);
                    // Door H — south-wall door connects interior (north) to outside
                    Sprite[] frames = TileAtlas.GetDoorHFrames();
                    var doorGO = PlaceTile(root.transform, c, r, frames[0], 0f, sortOrder: 2);
                    var dEntry = new DoorEntry
                    {
                        sr = doorGO.GetComponent<SpriteRenderer>(),
                        frames = frames, col = c, row = r
                    };
                    AddShadowsForDoor(c, r, root.transform, dEntry);
                    _doorEntries.Add(dEntry);
                }
                else if (isInterior)
                {
                    int variant = PickFloorVariant(c, r);
                    _floorVariants[(c, r)] = variant;
                    PlaceTile(root.transform, c, r, TileAtlas.GetFloor(variant),
                        FloorRotation(c, r), sortOrder: 0);
                }
                else // wall (corner, edge — all the same flat-top tile, no rotation)
                {
                    PlaceTile(root.transform, c, r,
                        TileAtlas.GetWall(PickWallVariant(c, r)), 0f, sortOrder: 0);
                }
            }

            // Shadow overlays (sortOrder 1) on every interior floor cell adjacent to a wall
            for (int c = IntMinC; c <= IntMaxC; c++)
            for (int r = IntMinR; r <= IntMaxR; r++)
                AddShadowsForFloor(c, r, root.transform);

            // Shadow overlays (sortOrder 1) on wall tiles adjacent to interior floor
            bool doorCol = true; // set per cell below
            for (int c = 0; c < RoomCols; c++)
            for (int r = 0; r < RoomRows; r++)
            {
                bool isWallTile = c == 0 || c == RoomCols - 1 || r == 0 || r == RoomRows - 1;
                bool isDoorTile = r == 0 && c == RoomCols / 2;
                if (isWallTile && !isDoorTile)
                    AddShadowsForWall(c, r, root.transform);
            }
        }

        // ── Foundation tile rendering ─────────────────────────────────────────
        private void RebuildFoundationTiles()
        {
            if (_gm?.Station == null || _foundRoot == null) return;
            var foundations = _gm.Station.foundations;

            // Remove GOs for deleted foundations
            _doorEntries.RemoveAll(e => !e.sr);
            var dead = new List<string>();
            foreach (var uid in _foundTiles.Keys)
                if (!foundations.ContainsKey(uid)) dead.Add(uid);

            foreach (var uid in dead)
            {
                if (_foundTiles[uid]) Destroy(_foundTiles[uid]);
                _foundTiles.Remove(uid);
                if (_foundExtras.TryGetValue(uid, out var extras))
                {
                    foreach (var e in extras) if (e) Destroy(e);
                    _foundExtras.Remove(uid);
                }
            }

            // Create GOs for new foundations
            foreach (var kv in foundations)
            {
                if (_foundTiles.ContainsKey(kv.Key)) continue;

                var  f       = kv.Value;
                bool isFloor = f.buildableId.Contains("floor");
                bool isWall  = f.buildableId.Contains("wall");
                bool isDoor  = f.buildableId.Contains("door");

                if (isFloor)
                {
                    int variant = PickFloorVariant(f.tileCol, f.tileRow);
                    _floorVariants[(f.tileCol, f.tileRow)] = variant;
                    var go = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        TileAtlas.GetFloor(variant), FloorRotation(f.tileCol, f.tileRow),
                        sortOrder: 0);
                    _foundTiles[kv.Key] = go;

                    var shadowList = new List<GameObject>();
                    AddShadowsForFloor(f.tileCol, f.tileRow, _foundRoot.transform, shadowList);
                    if (shadowList.Count > 0) _foundExtras[kv.Key] = shadowList;
                }
                else if (isWall)
                {
                    var go = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        TileAtlas.GetWall(PickWallVariant(f.tileCol, f.tileRow)), 0f,
                        sortOrder: 0);
                    _foundTiles[kv.Key] = go;

                    var shadowList = new List<GameObject>();
                    AddShadowsForWall(f.tileCol, f.tileRow, _foundRoot.transform, shadowList);
                    if (shadowList.Count > 0) _foundExtras[kv.Key] = shadowList;
                }
                else if (isDoor)
                {
                    bool isH = ClassifyIsDoorH(f.tileCol, f.tileRow);
                    Sprite[] frames = isH ? TileAtlas.GetDoorHFrames() : TileAtlas.GetDoorVFrames();

                    // Floor beneath door (transparent gap shows it)
                    var floorGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol, f.tileRow)),
                        FloorRotation(f.tileCol, f.tileRow), sortOrder: 0);

                    // Door sprite on top
                    var doorGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        frames[0], 0f, sortOrder: 2);
                    var fEntry = new DoorEntry
                    {
                        sr = doorGO.GetComponent<SpriteRenderer>(),
                        frames = frames, col = f.tileCol, row = f.tileRow
                    };
                    AddShadowsForDoor(f.tileCol, f.tileRow, _foundRoot.transform, fEntry);
                    _doorEntries.Add(fEntry);

                    _foundTiles[kv.Key]  = doorGO;
                    _foundExtras[kv.Key] = new List<GameObject> { floorGO };
                }
                else
                {
                    var go = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        MakeSolidSquare(new Color(0.22f, 0.25f, 0.32f)), 0f, sortOrder: 0);
                    _foundTiles[kv.Key] = go;
                }
            }
        }

        // ── Tile adjacency helpers ────────────────────────────────────────────

        /// True if (col,row) is a floor tile — starter room interior OR a placed floor foundation.
        private bool IsFloorTile(int col, int row)
        {
            if (col >= IntMinC && col <= IntMaxC &&
                row >= IntMinR && row <= IntMaxR) return true;

            if (_gm?.Station == null) return false;

            foreach (var f in _gm.Station.foundations.Values)
                if (f.tileCol == col && f.tileRow == row &&
                    f.buildableId.Contains("floor")) return true;

            return false;
        }

        // Deterministic wall variant (0–4) by position hash — no rotation.
        private static int PickWallVariant(int col, int row)
        {
            int h = unchecked(col * 104729 ^ row * 7919);
            return ((h % 5) + 5) % 5;
        }

        // Add shadow overlays on a WALL tile at edges that face a floor tile.
        private void AddShadowsForWall(int col, int row, Transform parent,
            List<GameObject> collector = null)
        {
            if (IsFloorTile(col,     row + 1)) AddShadowOverlay(col, row, TileAtlas.SHADOW_TOP,    parent, collector);
            if (IsFloorTile(col,     row - 1)) AddShadowOverlay(col, row, TileAtlas.SHADOW_BOTTOM, parent, collector);
            if (IsFloorTile(col + 1, row    )) AddShadowOverlay(col, row, TileAtlas.SHADOW_RIGHT,  parent, collector);
            if (IsFloorTile(col - 1, row    )) AddShadowOverlay(col, row, TileAtlas.SHADOW_LEFT,   parent, collector);
        }

        // Add shadow overlays on a DOOR tile at edges that face non-floor tiles.
        // Shadows go at sortOrder 3 (above door panels) and are tracked in entry.shadows
        // so they can be toggled off when the door opens.
        private void AddShadowsForDoor(int col, int row, Transform parent, DoorEntry entry)
        {
            int[]          edges = { TileAtlas.SHADOW_TOP, TileAtlas.SHADOW_BOTTOM,
                                     TileAtlas.SHADOW_RIGHT, TileAtlas.SHADOW_LEFT };
            (int dc, int dr)[] dirs = { (0, 1), (0, -1), (1, 0), (-1, 0) };
            for (int i = 0; i < 4; i++)
            {
                if (IsFloorTile(col + dirs[i].dc, row + dirs[i].dr)) continue;
                var go  = new GameObject($"DoorShadow{edges[i]}_{col},{row}");
                go.transform.SetParent(parent);
                go.transform.localPosition = new Vector3(col, row, 0f);
                var sr  = go.AddComponent<SpriteRenderer>();
                sr.sprite       = TileAtlas.GetShadow(edges[i]);
                sr.sortingOrder = 3; // above door panels (2)
                entry.shadows.Add(sr);
            }
        }

        // Add shadow overlay GOs for a floor tile based on non-floor adjacency.
        private void AddShadowsForFloor(int col, int row, Transform parent,
            List<GameObject> collector = null)
        {
            bool nFloor = IsFloorTile(col,     row + 1);
            bool sFloor = IsFloorTile(col,     row - 1);
            bool eFloor = IsFloorTile(col + 1, row    );
            bool wFloor = IsFloorTile(col - 1, row    );

            // Cardinal shadows
            if (!nFloor) AddShadowOverlay(col, row, TileAtlas.SHADOW_TOP,    parent, collector);
            if (!sFloor) AddShadowOverlay(col, row, TileAtlas.SHADOW_BOTTOM, parent, collector);
            if (!eFloor) AddShadowOverlay(col, row, TileAtlas.SHADOW_RIGHT,  parent, collector);
            if (!wFloor) AddShadowOverlay(col, row, TileAtlas.SHADOW_LEFT,   parent, collector);

            // Diagonal corner shadows — only when the corner is a wall but neither
            // adjacent cardinal side is already shadowed (avoids double-darkening).
            if (!IsFloorTile(col - 1, row + 1) && nFloor && wFloor)
                AddShadowOverlay(col, row, TileAtlas.SHADOW_TL, parent, collector);
            if (!IsFloorTile(col + 1, row + 1) && nFloor && eFloor)
                AddShadowOverlay(col, row, TileAtlas.SHADOW_TR, parent, collector);
            if (!IsFloorTile(col - 1, row - 1) && sFloor && wFloor)
                AddShadowOverlay(col, row, TileAtlas.SHADOW_BL, parent, collector);
            if (!IsFloorTile(col + 1, row - 1) && sFloor && eFloor)
                AddShadowOverlay(col, row, TileAtlas.SHADOW_BR, parent, collector);
        }

        private void AddShadowOverlay(int col, int row, int edge, Transform parent,
            List<GameObject> collector)
        {
            var go = new GameObject($"Shadow{edge}_{col},{row}");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(col, row, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TileAtlas.GetShadow(edge);
            sr.sortingOrder = 1; // above floor (0), below door (2)
            collector?.Add(go);
        }

        // Creates a tile GO with a SpriteRenderer, parented and positioned.
        private static GameObject PlaceTile(Transform parent, int col, int row,
            Sprite spr, float rotZ, int sortOrder)
        {
            var go = new GameObject($"Tile{col},{row}");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(col, row, 0f);
            go.transform.rotation      = Quaternion.Euler(0f, 0f, rotZ);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = spr;
            sr.sortingOrder = sortOrder;
            return go;
        }

        // Returns true if the given door tile should use the horizontal variant
        // (passage runs N↔S, panels slide left↔right).
        private bool ClassifyIsDoorH(int col, int row)
        {
            return IsFloorTile(col, row + 1) || IsFloorTile(col, row - 1);
        }

        // ── Floor variant / rotation ──────────────────────────────────────────

        /// Picks a floor variant (0–4) guaranteed to differ from already-assigned
        /// cardinal neighbours.  Deterministic for stable per-position appearance.
        private int PickFloorVariant(int col, int row)
        {
            int hash = unchecked(col * 7919 + row * 104729);

            var used = new HashSet<int>();
            if (_floorVariants.TryGetValue((col,     row + 1), out int v)) used.Add(v);
            if (_floorVariants.TryGetValue((col,     row - 1), out     v)) used.Add(v);
            if (_floorVariants.TryGetValue((col + 1, row    ), out     v)) used.Add(v);
            if (_floorVariants.TryGetValue((col - 1, row    ), out     v)) used.Add(v);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                int candidate = ((hash + attempt) % 5 + 5) % 5;
                if (!used.Contains(candidate)) return candidate;
            }
            return ((hash % 5) + 5) % 5;
        }

        private static float FloorRotation(int col, int row)
        {
            int h = unchecked(col * 104729 ^ row * 7919);
            return ((h % 4 + 4) % 4) * 90f;
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
            if (station.GetCrew().Count != _crew.Count) SpawnCrewDots();
            RebuildFoundationTiles();
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
                if (classId.Contains("engineer")) return ClassColors[0];
                if (classId.Contains("sciences")) return ClassColors[1];
                if (classId.Contains("security")) return ClassColors[2];
            }
            return ClassColors[index % ClassColors.Length];
        }

        private static string ClassLabel(string classId)
        {
            if (classId == null) return "";
            if (classId.Contains("engineer")) return "Engineer";
            if (classId.Contains("sciences")) return "Sciences";
            if (classId.Contains("security")) return "Security";
            return classId;
        }

        // Flat solid-colour 64×64 sprite (door, unknown buildables).
        private static Sprite MakeSolidSquare(Color color)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            var pix = new Color32[64 * 64];
            Color32 c32 = (Color32)color;
            for (int i = 0; i < pix.Length; i++) pix[i] = c32;
            tex.SetPixels32(pix);
            tex.Apply();
            return Sprite.Create(tex,
                new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), pixelsPerUnit: 64);
        }

        // ── Procedural sprites ────────────────────────────────────────────────
        private static Sprite MakeCircle(Color color)
        {
            const int   res    = 64;
            const float radius = res * 0.40f;
            const float inner  = radius * 0.55f;
            float       center = res * 0.5f;

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };

            for (int x = 0; x < res; x++)
            for (int y = 0; y < res; y++)
            {
                float dx = x - center, dy = y - center;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);

                if (d > radius + 1f) { tex.SetPixel(x, y, Color.clear); continue; }

                float alpha = Mathf.Clamp01(radius + 0.5f - d);
                Color c = d < inner
                    ? new Color(color.r * 0.65f, color.g * 0.65f, color.b * 0.65f, alpha)
                    : new Color(color.r,          color.g,          color.b,          alpha);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return Sprite.Create(tex,
                new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), pixelsPerUnit: res);
        }
    }
}
