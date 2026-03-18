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
using Waystation.Systems;
using Waystation.UI;

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
        private readonly Dictionary<int, Vector2Int> _dotTile   = new Dictionary<int, Vector2Int>();
        private readonly Dictionary<int, Vector3>    _dotTarget = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float>      _dotWanderAt = new Dictionary<int, float>(); // Time.time when dot next steps
        private readonly HashSet<Vector2Int>         _dotClaimed  = new HashSet<Vector2Int>();     // reused each frame, avoids GC alloc
        private const float DotMoveSpeed        = 4f;   // world units per second
        private const float DotStepMinInterval  = 0.6f; // seconds between steps when moving freely
        private const float DotStepMaxInterval  = 1.4f;
        private const float DotBlockedMinInterval = 1.0f; // longer pause when all neighbours are occupied
        private const float DotBlockedMaxInterval = 2.0f;

        // ── NPC selection & context menu ──────────────────────────────────────
        private readonly HashSet<int> _selectedDots      = new HashSet<int>();
        private bool    _isDragSelecting;
        private Vector2 _dragSelStart;            // screen space
        private Rect    _dragSelRect;
        private bool    _showContextMenu;
        private Vector2 _contextMenuScreen;
        private int     _ctxTileCol, _ctxTileRow;
        private GUIStyle _ctxBoxStyle, _ctxBtnStyle;
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
            // Foundation tracking for status/damage refreshes (null = built-in starter door)
            public string foundationUid;
            public bool   isH;
            public int    dmgLevel;    // 0=normal, 1=worn, 2=broken
            public string doorStatus;  // last rendered door status ("powered"/"locked"/"unpowered")
        }
        private readonly List<DoorEntry> _doorEntries = new List<DoorEntry>();
        // Floor variant cache: (col,row) → 0..4 assigned during BuildRoom / RebuildFoundationTiles
        private readonly Dictionary<(int, int), int> _floorVariants
            = new Dictionary<(int, int), int>();
        // Wall variant cache: (col,row) → 0..4; assignment is adjacency-constrained so
        // no two touching walls share the same variant.
        private readonly Dictionary<(int, int), int> _wallVariants
            = new Dictionary<(int, int), int>();

        // Foundation tile GameObjects keyed by foundation uid
        private readonly Dictionary<string, GameObject>        _foundTiles   = new Dictionary<string, GameObject>();
        // Extra per-foundation GOs: floor-under-door tiles (NOT shadows — those are in _shadowsAt)
        private readonly Dictionary<string, List<GameObject>> _foundExtras  = new Dictionary<string, List<GameObject>>();
        private GameObject _foundRoot;
        private GameObject _roomRoot;
        // Position-keyed wall-tile SpriteRenderers so RefreshTile can update their sprites.
        private readonly Dictionary<(int,int), SpriteRenderer>    _tileAt    = new Dictionary<(int,int), SpriteRenderer>();
        // All shadow GOs per grid position — destroyed and rebuilt when a neighbor changes.
        private readonly Dictionary<(int,int), List<GameObject>>   _shadowsAt = new Dictionary<(int,int), List<GameObject>>();
        // Foundation uid → (col,row) so we know the position when a foundation is removed.
        private readonly Dictionary<string, (int,int)>             _foundPos  = new Dictionary<string, (int,int)>();

        // ── View mode ──────────────────────────────────────────────────
        public enum ViewMode { Normal, Pipes, Ducts, Electricity, Temperature, Beauty, Pressurized }
        private ViewMode _viewMode = ViewMode.Normal;
        public ViewMode ActiveViewMode => _viewMode;
        public static StationRoomView Instance { get; private set; }

        // ── Overlay tint colours ───────────────────────────────────────
        // Electrical overlay: amber = powered, dark orange = underpowered, grey = isolated
        private static readonly Color TintElecPowered    = new Color(1.00f, 0.75f, 0.10f); // bright amber
        private static readonly Color TintElecUnpowered  = new Color(0.70f, 0.25f, 0.05f); // dark orange
        private static readonly Color TintElecIsolated   = new Color(0.35f, 0.30f, 0.20f); // dim brown
        // Plumbing overlay: blue = has fluid, grey = empty
        private static readonly Color TintPipeFlowing    = new Color(0.20f, 0.55f, 1.00f); // bright blue
        private static readonly Color TintPipeEmpty      = new Color(0.25f, 0.28f, 0.40f); // steel grey
        // Ducting overlay: teal = has gas, grey = empty
        private static readonly Color TintDuctFlowing    = new Color(0.10f, 0.85f, 0.72f); // bright teal
        private static readonly Color TintDuctEmpty      = new Color(0.22f, 0.35f, 0.35f); // dim teal-grey
        // Non-relevant network tile (dimmed when a different overlay is active)
        private static readonly Color TintDimmed         = new Color(0.20f, 0.22f, 0.25f); // near-black

        /// <summary>Called by GameHUD buttons to switch the active view overlay.</summary>
        public void SetViewMode(ViewMode mode)
        {
            _viewMode = mode;
            UpdateNetworkVisibility();
            UpdateNetworkOverlay();
        }

        // Tile cycling: left-click the same tile repeatedly to cycle through stacked foundations.
        private int _lastClickCol = -1, _lastClickRow = -1;
        private int _tileLayerIndex = 0;

        // Context panel (bottom-left drawer) — the foundation or NPC dot the player last clicked.
        private FoundationInstance _ctxFoundation = null;
        private GUIStyle _ctxPanelStyle, _ctxHeaderStyle, _ctxValueStyle, _ctxCtaStyle;
        // Door access editor sub-state
        private bool   _doorAccessEditing       = false;
        private string _doorAccessInput_Species = "";
        private string _doorAccessInput_Dept    = "";

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

            Instance = this;
            SetupCamera();
            BuildRoom();

            _gm.OnTick += OnTick;
            if (_gm.UtilityNetworks != null)
                _gm.UtilityNetworks.OnNetworkChanged += _ => UpdateNetworkOverlay();
            SpawnCrewDots();
            _ready = true;
        }

        private void OnDestroy()
        {
            if (_gm != null) _gm.OnTick -= OnTick;
        }

        private void Update()
        {
            if (!_ready) return;

            // ── Animate crew dots toward their movement targets ────────────────
            for (int i = 0; i < _dots.Count; i++)
            {
                if (!_dots[i]) continue;
                if (!_dotTarget.TryGetValue(i, out Vector3 tgt)) continue;
                Vector3 cur = _dots[i].transform.position;
                if (Vector3.SqrMagnitude(cur - tgt) < 0.0004f)
                {
                    _dots[i].transform.position = tgt;
                    _dotTarget.Remove(i);
                }
                else
                {
                    _dots[i].transform.position =
                        Vector3.MoveTowards(cur, tgt, DotMoveSpeed * Time.deltaTime);
                }
            }

            // ── Continuous NPC dot wandering (real-time, between ticks) ──────────
            // Each dot picks a new adjacent interior tile on its own timer so the
            // station feels lively at any game speed.
            _dotClaimed.Clear();
            for (int k = 0; k < _dots.Count; k++)
                if (_dotTile.TryGetValue(k, out Vector2Int ct)) _dotClaimed.Add(ct);

            for (int i = 0; i < _dots.Count; i++)
            {
                if (!_dots[i]) continue;
                if (_dotTarget.ContainsKey(i)) continue;  // still mid-move

                float nextStep = _dotWanderAt.TryGetValue(i, out float ns) ? ns : 0f;
                if (Time.time < nextStep) continue;

                _dotTile.TryGetValue(i, out Vector2Int cur2);
                int col2 = cur2.x, row2 = cur2.y;

                var dirs = new Vector2Int[]
                {
                    new Vector2Int( 1,  0),
                    new Vector2Int(-1,  0),
                    new Vector2Int( 0,  1),
                    new Vector2Int( 0, -1),
                };
                for (int d = 3; d > 0; d--)
                {
                    int j2 = UnityEngine.Random.Range(0, d + 1);
                    Vector2Int tmp2 = dirs[d]; dirs[d] = dirs[j2]; dirs[j2] = tmp2;
                }

                bool stepped = false;
                foreach (var dir in dirs)
                {
                    int nc2 = col2 + dir.x, nr2 = row2 + dir.y;
                    var cand2 = new Vector2Int(nc2, nr2);
                    if (IsPassable(nc2, nr2) && !_dotClaimed.Contains(cand2))
                    {
                        _dotClaimed.Remove(cur2);
                        _dotClaimed.Add(cand2);
                        _dotTile[i]   = cand2;
                        _dotTarget[i] = new Vector3(nc2, nr2, -0.2f);
                        stepped = true;
                        break;
                    }
                }

                // Schedule next step: shorter interval if moved, longer if all neighbours were blocked
                _dotWanderAt[i] = Time.time + UnityEngine.Random.Range(
                    stepped ? DotStepMinInterval    : DotBlockedMinInterval,
                    stepped ? DotStepMaxInterval    : DotBlockedMaxInterval);
            }

            // ── NPC drag-selection ─────────────────────────────────────────────
            bool hudBusy = GameHUD.IsMouseOverDrawer || GameHUD.InBuildMode;
            if (!hudBusy)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    _showContextMenu  = false;
                    _isDragSelecting  = true;
                    _dragSelStart     = Input.mousePosition;
                    _dragSelRect      = new Rect();

                    // Tile cycling: identify clicked tile and cycle context
                    var cam0 = Camera.main;
                    if (cam0 != null)
                    {
                        Vector3 w0   = cam0.ScreenToWorldPoint(Input.mousePosition);
                        int clickCol = Mathf.RoundToInt(w0.x);
                        int clickRow = Mathf.RoundToInt(w0.y);
                        if (clickCol == _lastClickCol && clickRow == _lastClickRow)
                            _tileLayerIndex++;
                        else
                        {
                            _lastClickCol   = clickCol;
                            _lastClickRow   = clickRow;
                            _tileLayerIndex = 0;
                        }
                        SelectContextFoundation(clickCol, clickRow);
                    }
                }
                if (_isDragSelecting && Input.GetMouseButton(0))
                {
                    Vector2 cur = Input.mousePosition;
                    float x = Mathf.Min(cur.x, _dragSelStart.x);
                    float y = Mathf.Min(Screen.height - cur.y, Screen.height - _dragSelStart.y);
                    float w = Mathf.Abs(cur.x - _dragSelStart.x);
                    float h = Mathf.Abs(cur.y - _dragSelStart.y);
                    _dragSelRect = new Rect(x, y, w, h);
                }
                if (_isDragSelecting && Input.GetMouseButtonUp(0))
                {
                    _isDragSelecting = false;
                    CommitDragSelection();
                    _dragSelRect = new Rect();
                }

                if (Input.GetMouseButtonDown(1) && _selectedDots.Count > 0)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
                        _ctxTileCol       = Mathf.RoundToInt(world.x);
                        _ctxTileRow       = Mathf.RoundToInt(world.y);
                        _contextMenuScreen = new Vector2(
                            Input.mousePosition.x,
                            Screen.height - Input.mousePosition.y);
                        _showContextMenu  = true;
                    }
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    _showContextMenu = false;
                }
            }

            // ── Door animation ────────────────────────────────────────────────
            for (int i = _doorEntries.Count - 1; i >= 0; i--)
            {
                var e = _doorEntries[i];
                if (!e.sr) { _doorEntries.RemoveAt(i); continue; }

                // Open when any crew dot is within 1 tile (Manhattan distance)
                // AND the corresponding NPC can pass through this door.
                bool npcNear = false;
                bool holdOpen = false;   // hoisted so void-gate below can read it
                if (_gm?.Station != null)
                {
                    string doorUid = e.foundationUid;
                    FoundationInstance doorFnd = null;
                    if (doorUid != null)
                        _gm.Station.foundations.TryGetValue(doorUid, out doorFnd);

                    holdOpen = doorFnd != null && doorFnd.doorHoldOpen;
                    if (holdOpen)
                    {
                        npcNear = true;  // always "open"
                    }
                    else
                    {
                        for (int di = 0; di < _dots.Count && !npcNear; di++)
                        {
                            var dot = _dots[di];
                            if (!dot) continue;
                            Vector3 dp = dot.transform.position;
                            if (Mathf.Abs(Mathf.RoundToInt(dp.x) - e.col) +
                                Mathf.Abs(Mathf.RoundToInt(dp.y) - e.row) > 1) continue;

                            // Check access policy if one is set
                            if (doorFnd?.accessPolicy != null && !doorFnd.accessPolicy.allowAll)
                            {
                                if (di < _crew.Count && doorFnd.accessPolicy.NpcCanPass(_crew[di]))
                                    npcNear = true;
                            }
                            else
                            {
                                npcNear = true;
                            }
                        }
                    }
                }

                // Doors facing void/space must never auto-open (regardless of NPC proximity
                // or hold-open flag — only explicit holdOpen bypasses this for salvage ops).
                if (npcNear && !holdOpen && IsDoorAdjacentToVoid(e.col, e.row))
                    npcNear = false;

                e.timer += Time.deltaTime;
                while (e.timer >= DoorFrameTime)
                {
                    e.timer -= DoorFrameTime;
                    if (npcNear  && e.frameIdx < 4) e.frameIdx++;
                    else if (!npcNear && e.frameIdx > 0) e.frameIdx--;
                }
                // Refresh frames if foundation health changed damage level
                if (e.foundationUid != null && _gm?.Station?.foundations != null
                    && _gm.Station.foundations.TryGetValue(e.foundationUid, out var fd))
                {
                    int newDmg = fd.health >= 75 ? 0 : fd.health >= 50 ? 1 : 2;
                    string dStatus = fd.doorStatus ?? "powered";
                    if (newDmg != e.dmgLevel || dStatus != e.doorStatus)
                    {
                        e.dmgLevel   = newDmg;
                        e.doorStatus = dStatus;
                        e.frames     = TileAtlas.GetDoorFrames(e.isH, dStatus, newDmg);
                        e.frameIdx = Mathf.Clamp(e.frameIdx, 0, e.frames.Length - 1);
                    }
                }

                e.sr.sprite = e.frames[e.frameIdx];
                // Hide door shadows while the door is opening/open
                bool closed = e.frameIdx == 0;
                foreach (var sh in e.shadows) if (sh) sh.enabled = closed;
            }

            // ── Construction tinting — unbuilt foundations show blue ghost tint ─
            if (_gm?.Station != null)
            {
                foreach (var kv in _gm.Station.foundations)
                {
                    if (!_foundTiles.TryGetValue(kv.Key, out var go) || !go) continue;
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    sr.color = kv.Value.status == "complete"
                        ? Color.white
                        : new Color(0.45f, 0.80f, 1.00f, 0.55f);
                }
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
            _roomRoot  = new GameObject("Room");
            var root   = _roomRoot;
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
                        FloorRotation(c, r), sortOrder: 10);
                    // Door H — south-wall door connects interior (north) to outside
                    Sprite[] frames = TileAtlas.GetDoorHFrames();
                    var doorGO = PlaceTile(root.transform, c, r, frames[0], 0f, sortOrder: 40);
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
                        FloorRotation(c, r), sortOrder: 10);
                }
                else // wall (corner, edge — directional tile selected by position)
                {
                    var wallGO = PlaceTile(root.transform, c, r, GetWallSprite(c, r), GetWallRotation(c, r), sortOrder: 40);
                    _tileAt[(c, r)] = wallGO.GetComponent<SpriteRenderer>();
                }
            }

            // Shadow overlays (sortOrder 1) on every interior floor cell adjacent to a wall
            for (int c = IntMinC; c <= IntMaxC; c++)
            for (int r = IntMinR; r <= IntMaxR; r++)
            {
                var sl = new List<GameObject>();
                AddShadowsForFloor(c, r, root.transform, sl);
                if (sl.Count > 0) _shadowsAt[(c, r)] = sl;
            }

            // Shadow overlays (sortOrder 1) on wall tiles adjacent to interior floor
            for (int c = 0; c < RoomCols; c++)
            for (int r = 0; r < RoomRows; r++)
            {
                bool isWallTile = c == 0 || c == RoomCols - 1 || r == 0 || r == RoomRows - 1;
                bool isDoorTile = r == 0 && c == RoomCols / 2;
                if (isWallTile && !isDoorTile)
                {
                    var sl = new List<GameObject>();
                    AddShadowsForWall(c, r, root.transform, sl);
                    if (sl.Count > 0) _shadowsAt[(c, r)] = sl;
                }
            }
        }

        // ── Foundation tile rendering ─────────────────────────────────────────

        /// <summary>
        /// Immediately syncs rendered GOs with station.foundations without waiting
        /// for the next game tick.  Called after Ctrl+Z undo so removed foundations
        /// disappear from the view straight away.
        /// </summary>
        public void ForceRefreshFoundations() => RebuildFoundationTiles();

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
                bool hasPos = _foundPos.TryGetValue(uid, out var pos);
                if (_foundTiles[uid]) Destroy(_foundTiles[uid]);
                _foundTiles.Remove(uid);
                if (hasPos) { _tileAt.Remove(pos); _wallVariants.Remove(pos); }
                _foundPos.Remove(uid);

                if (_foundExtras.TryGetValue(uid, out var extras))
                {
                    foreach (var e in extras) if (e) Destroy(e);
                    _foundExtras.Remove(uid);
                }

                // Destroy per-position shadows and refresh vacated cell's neighbors.
                if (hasPos)
                {
                    if (_shadowsAt.TryGetValue(pos, out var deadSh))
                    {
                        foreach (var s in deadSh) if (s) Destroy(s);
                        _shadowsAt.Remove(pos);
                    }
                    RefreshAdjacentTiles(pos.Item1, pos.Item2);
                    RefreshNetworkNeighbors(pos.Item1, pos.Item2);
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
                        sortOrder: 10);
                    _foundTiles[kv.Key] = go;
                    _foundPos[kv.Key]   = (f.tileCol, f.tileRow);

                    var shadowList = new List<GameObject>();
                    AddShadowsForFloor(f.tileCol, f.tileRow, _foundRoot.transform, shadowList);
                    if (shadowList.Count > 0) _shadowsAt[(f.tileCol, f.tileRow)] = shadowList;
                    RefreshAdjacentTiles(f.tileCol, f.tileRow);
                }
                else if (isWall)
                {
                    var go = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        GetWallSprite(f.tileCol, f.tileRow, f), GetWallRotation(f.tileCol, f.tileRow), sortOrder: 40);
                    _foundTiles[kv.Key] = go;
                    _tileAt[(f.tileCol, f.tileRow)] = go.GetComponent<SpriteRenderer>();
                    _foundPos[kv.Key]   = (f.tileCol, f.tileRow);

                    var shadowList = new List<GameObject>();
                    AddShadowsForWall(f.tileCol, f.tileRow, _foundRoot.transform, shadowList);
                    if (shadowList.Count > 0) _shadowsAt[(f.tileCol, f.tileRow)] = shadowList;
                    RefreshAdjacentTiles(f.tileCol, f.tileRow);
                }
                else if (isDoor)
                {
                    bool isH = ClassifyIsDoorH(f.tileCol, f.tileRow);
                    string dStatus = f.doorStatus ?? "powered";
                    Sprite[] frames = TileAtlas.GetDoorFrames(isH, dStatus, 0);

                    // Floor beneath door (transparent gap shows it)
                    var floorGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol, f.tileRow)),
                        FloorRotation(f.tileCol, f.tileRow), sortOrder: 10);

                    // Door sprite on top
                    var doorGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        frames[0], 0f, sortOrder: 40);
                    var fEntry = new DoorEntry
                    {
                        sr = doorGO.GetComponent<SpriteRenderer>(),
                        frames = frames, col = f.tileCol, row = f.tileRow,
                        foundationUid = kv.Key, isH = isH
                    };
                    AddShadowsForDoor(f.tileCol, f.tileRow, _foundRoot.transform, fEntry);
                    _doorEntries.Add(fEntry);

                    _foundTiles[kv.Key]  = doorGO;
                    _foundExtras[kv.Key] = new List<GameObject> { floorGO };
                }
                else if (f.buildableId.Contains("storage_cabinet"))
                {
                    // Cabinet sits on a floor tile shown beneath it.
                    var floorGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol, f.tileRow)),
                        FloorRotation(f.tileCol, f.tileRow), sortOrder: 10);

                    var cabinetGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        TileAtlas.GetCabinet(f.tileRotation, f.CargoFillRatio()), 0f, sortOrder: 20);

                    _foundTiles[kv.Key]  = cabinetGO;
                    _foundExtras[kv.Key] = new List<GameObject> { floorGO };
                }
                else if (f.buildableId.Contains("battery"))
                {
                    // 128-px sprite = 2 world units wide; anchor at (col+0.5, row).
                    // Pre-place floor tiles under both subtiles.
                    var floorA = PlaceTile(_foundRoot.transform, f.tileCol,     f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol,     f.tileRow)),
                        FloorRotation(f.tileCol,     f.tileRow), sortOrder: 10);
                    var floorB = PlaceTile(_foundRoot.transform, f.tileCol + 1, f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol + 1, f.tileRow)),
                        FloorRotation(f.tileCol + 1, f.tileRow), sortOrder: 10);

                    var battGO = new GameObject($"Battery_{f.uid}");
                    battGO.transform.SetParent(_foundRoot.transform, false);
                    battGO.transform.localPosition = new Vector3(f.tileCol + 0.5f, f.tileRow, 0f);
                    var sr = battGO.AddComponent<SpriteRenderer>();
                    sr.sprite       = TileAtlas.GetBattery();
                    sr.sortingOrder = 30; // layer 3 = large objects

                    _foundTiles[kv.Key]  = battGO;
                    _foundExtras[kv.Key] = new List<GameObject> { floorA, floorB };
                }
                else if (f.buildableId == "buildable.bed")
                {
                    // 128×64 sprite spans two tiles — place at col+0.5 like battery/ice_refiner
                    var floorA = PlaceTile(_foundRoot.transform, f.tileCol,     f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol, f.tileRow)),
                        FloorRotation(f.tileCol, f.tileRow), sortOrder: 10);
                    var floorB = PlaceTile(_foundRoot.transform, f.tileCol + 1, f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol + 1, f.tileRow)),
                        FloorRotation(f.tileCol + 1, f.tileRow), sortOrder: 10);
                    var bedGO = new GameObject($"Bed_{f.uid}");
                    bedGO.transform.SetParent(_foundRoot.transform, false);
                    bedGO.transform.localPosition = new Vector3(f.tileCol + 0.5f, f.tileRow, 0f);
                    var srBed = bedGO.AddComponent<SpriteRenderer>();
                    srBed.sprite       = TileAtlas.GetBed(0);
                    srBed.sortingOrder = 20;
                    _foundTiles[kv.Key]  = bedGO;
                    _foundExtras[kv.Key] = new List<GameObject> { floorA, floorB };
                }
                else if (f.buildableId == "buildable.wire"
                      || f.buildableId == "buildable.pipe"
                      || f.buildableId == "buildable.duct"
                      || f.buildableId == "buildable.switch"
                      || f.buildableId == "buildable.valve"
                      || f.buildableId == "buildable.breaker")
                {
                    Sprite netSprite = GetNetworkSprite(f);
                    var netGO = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        netSprite, 0f, sortOrder: f.isUnderWall ? 9 : 15);
                    // Hide under-wall network tiles in Normal view mode
                    if (f.isUnderWall && _viewMode == ViewMode.Normal)
                        netGO.SetActive(false);
                    _foundTiles[kv.Key] = netGO;
                    // Update adjacent network tiles so they redraw with the new connection mask.
                    RefreshNetworkNeighbors(f.tileCol, f.tileRow);
                }
                else if (f.buildableId == "buildable.generator")
                {
                    // 128×128 px sprite @ 64 PPU = 2×2 world units.
                    // Place floor tiles under all four subtiles, then the generator on top.
                    string genState = f.health <= 0 ? "destroyed"
                                    : f.health < f.maxHealth * 0.5f ? "damaged"
                                    : "normal";
                    var floorGOs = new List<GameObject>();
                    for (int dc = 0; dc < 2; dc++)
                    for (int dr = 0; dr < 2; dr++)
                    {
                        floorGOs.Add(PlaceTile(_foundRoot.transform, f.tileCol + dc, f.tileRow + dr,
                            TileAtlas.GetFloor(PickFloorVariant(f.tileCol + dc, f.tileRow + dr)),
                            FloorRotation(f.tileCol + dc, f.tileRow + dr), sortOrder: 10));
                    }
                    var genGO = new GameObject($"Generator_{f.uid}");
                    genGO.transform.SetParent(_foundRoot.transform, false);
                    // Centre pivot: position at the centre of the 2×2 footprint (col+0.5, row+0.5).
                    genGO.transform.localPosition = new Vector3(f.tileCol + 0.5f, f.tileRow + 0.5f, 0f);
                    var srGen = genGO.AddComponent<SpriteRenderer>();
                    srGen.sprite       = TileAtlas.GetGenerator(genState);
                    srGen.sortingOrder = 30;
                    _foundTiles[kv.Key]  = genGO;
                    _foundExtras[kv.Key] = floorGOs;
                }
                else if (f.buildableId == "buildable.ice_refiner")
                {
                    // 2-tile-wide 128×64 sprite anchored at col+0.5
                    var floorA = PlaceTile(_foundRoot.transform, f.tileCol,     f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol,     f.tileRow)),
                        FloorRotation(f.tileCol,     f.tileRow), sortOrder: 10);
                    var floorB = PlaceTile(_foundRoot.transform, f.tileCol + 1, f.tileRow,
                        TileAtlas.GetFloor(PickFloorVariant(f.tileCol + 1, f.tileRow)),
                        FloorRotation(f.tileCol + 1, f.tileRow), sortOrder: 10);

                    var refGO = new GameObject($"IceRefiner_{f.uid}");
                    refGO.transform.SetParent(_foundRoot.transform, false);
                    refGO.transform.localPosition = new Vector3(f.tileCol + 0.5f, f.tileRow, 0f);
                    var sr2 = refGO.AddComponent<SpriteRenderer>();
                    sr2.sprite       = TileAtlas.GetIceRefiner(f.operatingState ?? "standby");
                    sr2.sortingOrder = 30;

                    _foundTiles[kv.Key]  = refGO;
                    _foundExtras[kv.Key] = new List<GameObject> { floorA, floorB };
                }
                else
                {
                    var go = PlaceTile(_foundRoot.transform, f.tileCol, f.tileRow,
                        MakeSolidSquare(new Color(0.22f, 0.25f, 0.32f)), 0f, sortOrder: 20);
                    _foundTiles[kv.Key] = go;
                }
            }
        }

        // ── Tile adjacency helpers ────────────────────────────────────────────

        // ── View mode helpers ─────────────────────────────────────────────────

        private void UpdateNetworkVisibility()
        {
            if (_gm?.Station == null) return;
            foreach (var kv in _foundTiles)
            {
                if (!_gm.Station.foundations.TryGetValue(kv.Key, out var f)) continue;
                if (f.isUnderWall && kv.Value != null)
                    kv.Value.SetActive(_viewMode != ViewMode.Normal);
            }
        }

        /// <summary>
        /// Apply per-network colour tinting to all wire / pipe / duct tiles based on
        /// the current overlay mode and each network's live supply state.
        /// Called after every tick and whenever the overlay mode changes.
        /// In Normal mode all tiles are reset to white (no tint).
        /// </summary>
        private void UpdateNetworkOverlay()
        {
            if (_gm?.Station == null) return;
            foreach (var kv in _foundTiles)
            {
                if (!_gm.Station.foundations.TryGetValue(kv.Key, out var f)) continue;
                if (kv.Value == null) continue;
                var sr = kv.Value.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                sr.color = GetNetworkTileColor(f);
            }
        }

        /// <summary>
        /// Returns the tint colour a network tile should show in the current overlay
        /// mode.  Non-network foundations and tiles not relevant to the active overlay
        /// are returned as white (no tint).
        /// </summary>
        private Color GetNetworkTileColor(FoundationInstance f)
        {
            if (_viewMode == ViewMode.Normal) return Color.white;
            if (_gm?.Station == null)         return Color.white;

            // Classify the foundation by its network role
            string tileNetType = f.buildableId switch
            {
                "buildable.wire"    or "buildable.switch"  => "electric",
                "buildable.pipe"    or "buildable.valve"   => "pipe",
                "buildable.duct"    or "buildable.breaker" => "duct",
                _ => null
            };

            if (tileNetType == null) return Color.white; // not a network conduit tile

            // Look up the network this tile belongs to
            NetworkInstance net = null;
            if (f.networkId != null)
                _gm.Station.networks.TryGetValue(f.networkId, out net);

            switch (_viewMode)
            {
                case ViewMode.Electricity:
                    if (tileNetType != "electric") return TintDimmed;
                    if (net == null)               return TintElecIsolated;
                    {
                        Color baseColor = net.totalDemand <= 0f || net.totalSupply + net.storedEnergy >= net.totalDemand
                            ? TintElecPowered
                            : TintElecUnpowered;
                        return ApplyNetworkHue(net, baseColor);
                    }

                case ViewMode.Pipes:
                    if (tileNetType != "pipe") return TintDimmed;
                    if (net == null)           return TintPipeEmpty;
                    {
                        Color baseColor = net.contentAmount > 0f ? TintPipeFlowing : TintPipeEmpty;
                        return ApplyNetworkHue(net, baseColor);
                    }

                case ViewMode.Ducts:
                    if (tileNetType != "duct") return TintDimmed;
                    if (net == null)           return TintDuctEmpty;
                    {
                        Color baseColor = net.contentAmount > 0f ? TintDuctFlowing : TintDuctEmpty;
                        return ApplyNetworkHue(net, baseColor);
                    }

                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// Shifts the hue of <paramref name="baseColor"/> by a small, deterministic
        /// amount derived from the network UID so that adjacent sub-networks render
        /// in visually distinct shades within the same overlay.
        /// </summary>
        private static Color ApplyNetworkHue(NetworkInstance net, Color baseColor)
        {
            // Use the low 8 bits of the UID's hash to get a ±15° hue shift.
            int hash     = net.uid.GetHashCode();
            float offset = ((hash & 0xFF) / 255f - 0.5f) * 0.083f; // ≈ ±15° / 360°
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            h = (h + offset + 1f) % 1f;
            return Color.HSVToRGB(h, s, v);
        }

        private Sprite GetNetworkSprite(FoundationInstance f)
        {
            if (_gm?.Networks == null) return TileAtlas.GetWire(0);
            string netType = f.buildableId switch
            {
                "buildable.wire"    or "buildable.switch"  => "electric",
                "buildable.pipe"    or "buildable.valve"   => "pipe",
                "buildable.duct"    or "buildable.breaker" => "duct",
                _ => "electric",
            };
            int mask = _gm.Networks.GetConnectionMask(_gm.Station, f.tileCol, f.tileRow, netType);
            return netType switch
            {
                "electric" => TileAtlas.GetWire(mask),
                "pipe"     => TileAtlas.GetPipe(mask, "normal"),
                _          => TileAtlas.GetDuct(mask),
            };
        }

        private bool IsIsolator(FoundationInstance f)
            => f.buildableId is "buildable.switch" or "buildable.valve" or "buildable.breaker";

        private static string GetIsolatorLabel(string buildableId) => buildableId switch
        {
            "buildable.switch"  => "Switch",
            "buildable.valve"   => "Valve",
            "buildable.breaker" => "Breaker",
            _                   => "Isolator",
        };

        private void SelectContextFoundation(int col, int row)
        {
            if (_gm?.Station == null) { _ctxFoundation = null; return; }
            var fList = new List<FoundationInstance>();
            foreach (var f in _gm.Station.foundations.Values)
                if (f.tileCol == col && f.tileRow == row) fList.Add(f);
            if (fList.Count == 0) { _ctxFoundation = null; return; }
            fList.Sort((a, b) => a.tileLayer.CompareTo(b.tileLayer));
            _ctxFoundation = fList[_tileLayerIndex % fList.Count];
        }

        /// <summary>
        /// True when an NPC dot can step onto (col, row).
        /// Respects placed walls (block), placed doors (pass unless locked), the
        /// built-in south-wall boundary door, and any floor/object foundations.
        /// Works for tiles both inside AND outside the starter 7×7 room bounds so
        /// that NPCs can navigate into rooms built beyond the starting area.
        /// </summary>
        private bool IsPassable(int col, int row)
        {
            // Hard safety: don't wander into infinity
            if (col < -64 || col > 64 || row < -64 || row > 64) return false;

            // Built-in south-wall boundary door is always passable (not in foundations)
            if (row == 0 && col == RoomCols / 2) return true;

            // Inspect all foundations at this position
            if (_gm?.Station?.foundations != null)
            {
                bool anyFound        = false;
                bool hasBlockingWall = false;
                bool hasDoor         = false;
                bool doorLocked      = false;

                foreach (var f in _gm.Station.foundations.Values)
                {
                    if (f.tileCol != col || f.tileRow != row) continue;
                    anyFound = true;
                    if (f.tileLayer == 1 && f.buildableId.Contains("wall"))
                        hasBlockingWall = true;
                    if (f.buildableId.Contains("door"))
                    {
                        hasDoor    = true;
                        bool locked  = (f.doorStatus ?? "powered") == "locked";
                        bool holdOpen = f.doorHoldOpen;
                        // A door with a restrictive access policy blocks random wander
                        // (we have no NPC context here; treat restriction = wall for wanderers).
                        bool restricted = f.accessPolicy != null && !f.accessPolicy.allowAll;
                        doorLocked = locked || (!holdOpen && restricted);
                    }
                }

                if (anyFound)
                {
                    if (hasDoor)
                    {
                        if (doorLocked) return false;
                        // Don't wander through doors that face void/unpressurized space.
                        if (IsDoorAdjacentToVoid(col, row)) return false;
                        return true;
                    }
                    if (hasBlockingWall) return false;
                    return true;  // floor or furniture – always walkable
                }
            }

            // No foundations here: passable only if inside the starter room interior
            return col >= IntMinC && col <= IntMaxC &&
                   row >= IntMinR && row <= IntMaxR;
        }

        /// True if (col,row) is passable floor.
        /// Placed wall foundations override the interior range — a wall on an interior
        /// cell is NOT floor.  Everything else (floor, door, cabinet, objects) is.
        private bool IsFloorTile(int col, int row)
        {
            // With the layer system, multiple foundations can share one tile (e.g. a floor at
            // layer 0 beneath a wall at layer 1).  We must check ALL foundations at this
            // position: a layer-1 structure that is a wall (not a door) blocks traversal.
            if (_gm?.Station != null)
            {
                bool anyFound = false;
                bool hasWall  = false;
                foreach (var f in _gm.Station.foundations.Values)
                {
                    if (f.tileCol != col || f.tileRow != row) continue;
                    anyFound = true;
                    if (f.tileLayer == 1 && f.buildableId.Contains("wall"))
                    { hasWall = true; break; }
                }
                if (anyFound) return !hasWall;
            }

            // Starter room interior — no overriding foundation present.
            if (col >= IntMinC && col <= IntMaxC &&
                row >= IntMinR && row <= IntMaxR) return true;

            return false;
        }

        /// True if (col,row) is within the room grid (0..RoomCols-1, 0..RoomRows-1).
        private static bool IsInBounds(int col, int row)
            => col >= 0 && col < RoomCols && row >= 0 && row < RoomRows;

        // Pick a wall variant (0–4) for (col,row) that does not match any of the 8 neighbors.
        // Result is cached so the same tile always returns the same variant unless cleared.
        // Uses a position-seeded shuffle so the layout is stable across same-order rebuilds.
        private int PickWallVariant(int col, int row)
        {
            if (_wallVariants.TryGetValue((col, row), out var cached)) return cached;

            // Collect variants already assigned to the 8 neighbors.
            var used = new HashSet<int>();
            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
            {
                if (dc == 0 && dr == 0) continue;
                if (_wallVariants.TryGetValue((col + dc, row + dr), out var nv)) used.Add(nv);
            }

            // Position-seeded Fisher-Yates shuffle of [0,1,2,3,4].
            var rnd   = new System.Random(unchecked(col * 104729 ^ row * 7919));
            var order = new[] { 0, 1, 2, 3, 4 };
            for (int i = 4; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            int variant = order[0]; // fallback if all 5 somehow used
            foreach (int candidate in order)
                if (!used.Contains(candidate)) { variant = candidate; break; }

            _wallVariants[(col, row)] = variant;
            return variant;
        }

        // Select the correct directional or corner wall sprite based on which neighboring
        // cells are floor.  Works for both static room boundaries and placed interior walls.
        //
        // Direction convention — outer (beveled top) face points AWAY from the floor side:
        //   floor to North  →  WALL_DIR_S  (outer face south)
        //   floor to South  →  WALL_DIR_N  (outer face north)
        //   floor to East   →  WALL_DIR_W  (outer face west)
        //   floor to West   →  WALL_DIR_E  (outer face east)
        //
        // Convex corners (no cardinal floor, one diagonal floor — the room-interior diagonal):
        //   NE diagonal floor  →  WALL_CORNER_SW  (exterior SW corner of room)
        //   NW diagonal floor  →  WALL_CORNER_SE
        //   SE diagonal floor  →  WALL_CORNER_NW
        //   SW diagonal floor  →  WALL_CORNER_NE
        //
        // foundation: when non-null, the health state is used to select normal/damaged/destroyed.
        //             Boundary room walls (no foundation) always use "normal".
        private Sprite GetWallSprite(int col, int row, FoundationInstance foundation = null)
        {
            string state = "normal";
            if (foundation != null && foundation.maxHealth > 0)
            {
                float pct = (float)foundation.health / foundation.maxHealth;
                state = foundation.health <= 0 ? "destroyed"
                      : pct < 0.5f             ? "damaged"
                      :                          "normal";
            }
            // All wall tiles use the base sprite; floor-adjacent face strips are
            // composited on top by AddShadowsForWall.
            return TileAtlas.GetWallBase(state);
        }

        // Returns the Z rotation (degrees) for a wall tile so its slab (perspective front
        // face) always points toward the room interior / adjacent floor.
        //   0°   — slab faces south  (north boundary walls, floor beneath)
        //   180° — slab faces north  (south boundary walls)
        //   +90° — slab faces east   (west boundary walls, CCW rotation)
        //   -90° — slab faces west   (east boundary walls, CW rotation)
        private float GetWallRotation(int col, int row)
        {
            bool northBoundary = row == RoomRows - 1;
            bool southBoundary = row == 0;
            bool eastBoundary  = col == RoomCols - 1;
            bool westBoundary  = col == 0;

            // Non-corner boundary walls: rotate so slab faces interior.
            if (northBoundary && !eastBoundary && !westBoundary) return   0f;
            if (southBoundary && !eastBoundary && !westBoundary) return 180f;
            if (eastBoundary  && !northBoundary && !southBoundary) return -90f;
            if (westBoundary  && !northBoundary && !southBoundary) return  90f;

            // Corner tiles and interior placed walls: pick slab direction from floor neighbors.
            if (IsFloorTile(col,     row - 1)) return   0f; // floor south → slab south
            if (IsFloorTile(col,     row + 1)) return 180f; // floor north → slab north
            if (IsFloorTile(col + 1, row    )) return  90f; // floor east  → slab east
            if (IsFloorTile(col - 1, row    )) return -90f; // floor west  → slab west
            return 0f;
        }

        // Update the SpriteRenderer sprite for any wire/pipe/duct foundation at the given tile.
        private void RefreshNetworkTileAt(int col, int row)
        {
            if (_gm?.Station == null) return;
            foreach (var f in _gm.Station.foundations.Values)
            {
                if (f.tileCol != col || f.tileRow != row) continue;
                if (f.buildableId != "buildable.wire"    &&
                    f.buildableId != "buildable.pipe"    &&
                    f.buildableId != "buildable.duct"    &&
                    f.buildableId != "buildable.switch"  &&
                    f.buildableId != "buildable.valve"   &&
                    f.buildableId != "buildable.breaker") continue;
                if (!_foundTiles.TryGetValue(f.uid, out var go) || go == null) continue;
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = GetNetworkSprite(f);
                break;
            }
        }

        // Refresh the 4 cardinal-neighbor network tiles after a tile is placed or removed.
        private void RefreshNetworkNeighbors(int col, int row)
        {
            RefreshNetworkTileAt(col,     row + 1);
            RefreshNetworkTileAt(col + 1, row    );
            RefreshNetworkTileAt(col,     row - 1);
            RefreshNetworkTileAt(col - 1, row    );
        }

        // Re-evaluate the sprite and shadow overlays for every cell in the 3×3 neighborhood.
        // Call this after any tile is added or removed so neighbors auto-update.
        private void RefreshAdjacentTiles(int col, int row)
        {
            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
                RefreshTile(col + dc, row + dr);
        }

        // Re-evaluate one tile's wall sprite (if applicable) and its shadow overlays.
        private void RefreshTile(int col, int row)
        {
            if (!IsInBounds(col, row)) return;

            bool isWall  = IsWallAtPos(col, row);
            bool isFloor = !isWall && IsFloorTile(col, row);

            // Clear cached variant so the tile re-picks against its current neighbors.
            if (isWall) _wallVariants.Remove((col, row));

            // Update wall sprite via the cached SpriteRenderer.
            if (isWall && _tileAt.TryGetValue((col, row), out var wallSr) && wallSr)
            {
                // Look up any placed wall foundation at this position so GetWallSprite
                // can select the correct health state (normal/damaged/destroyed).
                FoundationInstance wallFoundation = null;
                if (_gm?.Station?.foundations != null)
                    foreach (var f in _gm.Station.foundations.Values)
                        if (f.tileCol == col && f.tileRow == row && f.buildableId.Contains("wall"))
                        { wallFoundation = f; break; }
                wallSr.sprite = GetWallSprite(col, row, wallFoundation);
            }

            // Destroy existing shadows at this position.
            if (_shadowsAt.TryGetValue((col, row), out var oldSh))
            {
                foreach (var go in oldSh) if (go) Destroy(go);
                _shadowsAt.Remove((col, row));
            }

            // Rebuild shadows.  Parent to _foundRoot (dynamic layer) so they clean up properly.
            if (_foundRoot == null) return;
            var newShadows = new List<GameObject>();
            if      (isWall)  AddShadowsForWall (col, row, _foundRoot.transform, newShadows);
            else if (isFloor) AddShadowsForFloor(col, row, _foundRoot.transform, newShadows);
            if (newShadows.Count > 0) _shadowsAt[(col, row)] = newShadows;
        }

        // True if (col,row) is occupied by a wall — either the static room boundary or
        // a placed wall foundation.
        private bool IsWallAtPos(int col, int row)
        {
            bool boundary = col == 0 || col == RoomCols - 1 || row == 0 || row == RoomRows - 1;
            bool isDoor   = row == 0 && col == RoomCols / 2;
            if (boundary && !isDoor) return true;
            if (_gm?.Station?.foundations == null) return false;
            foreach (var f in _gm.Station.foundations.Values)
                if (f.tileCol == col && f.tileRow == row && f.buildableId.Contains("wall"))
                    return true;
            return false;
        }

        /// <summary>
        /// Returns true when the tile is part of a pressurized, habitable area:
        /// the starter room interior (always pressurized) or a tile that carries a
        /// completed non-wall foundation (floor/furniture/object).
        /// Void tiles and tiles outside any built structure return false.
        /// </summary>
        private bool IsTilePressurized(int col, int row)
        {
            // Starter room interior is implicitly pressurized (no foundations required).
            if (col >= IntMinC && col <= IntMaxC && row >= IntMinR && row <= IntMaxR) return true;
            // Any completed non-wall foundation at this position belongs to a built room.
            if (_gm?.Station?.foundations == null) return false;
            foreach (var f in _gm.Station.foundations.Values)
            {
                if (f.tileCol != col || f.tileRow != row) continue;
                if (f.status == "complete" && !f.buildableId.Contains("wall")) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true when any non-wall cardinal neighbor of the given door tile is
        /// not pressurized (i.e. the door faces open space on at least one side).
        /// </summary>
        private bool IsDoorAdjacentToVoid(int col, int row)
        {
            (int dc, int dr)[] offsets = { (0, 1), (0, -1), (1, 0), (-1, 0) };
            foreach (var (dc, dr) in offsets)
            {
                int nc = col + dc, nr = row + dr;
                if (IsWallAtPos(nc, nr)) continue;          // wall is not "void"
                if (!IsTilePressurized(nc, nr)) return true; // non-wall & unpressurized = void
            }
            return false;
        }

        // Add wall_overlays.png face strips on a WALL tile for every face that borders floor.
        // Uses combined sprites (ov_ne, ov_nw, ov_se, ov_sw, ov_cross) where available for
        // clean corner joins, stacking singles for any remaining uncovered faces.
        private void AddShadowsForWall(int col, int row, Transform parent,
            List<GameObject> collector = null)
        {
            const int wallOverlayOrder = 41;

            // Boundary corner tiles have no cardinal floor neighbors but their diagonal IS
            // interior — apply the matching two-face corner overlay so the strips from the
            // adjacent straight wall runs connect cleanly across the corner joint.
            bool isBoundaryCorner = (col == 0 || col == RoomCols - 1)
                                 && (row == 0 || row == RoomRows - 1);
            if (isBoundaryCorner)
            {
                // Room corners — art top = game South, so the inner-corner filler
                // sprite names map based on where the pocket appears in the image:
                //   SW room corner (0,0)           → S+E faces → ov_corner_tr (top-right pocket)
                //   SE room corner (RoomCols-1, 0) → S+W faces → ov_corner_tl (top-left pocket)
                //   NW room corner (0, RoomRows-1) → N+E faces → ov_corner_br (bot-right pocket)
                //   NE room corner (both max)      → N+W faces → ov_corner_bl (bot-left pocket)
                string cornerId = (col == 0            && row == 0           ) ? "ov_corner_tr"
                                : (col == RoomCols - 1 && row == 0           ) ? "ov_corner_tl"
                                : (col == 0            && row == RoomRows - 1) ? "ov_corner_br"
                                :                                                "ov_corner_bl";
                PlaceWallOverlay(cornerId, col, row, parent, collector, wallOverlayOrder);
                return;
            }

            bool n = IsFloorTile(col,     row + 1);
            bool s = IsFloorTile(col,     row - 1);
            bool e = IsFloorTile(col + 1, row    );
            bool w = IsFloorTile(col - 1, row    );

            if (!n && !s && !e && !w) return;

            foreach (string id in WallOverlayIds(n, s, e, w))
                PlaceWallOverlay(id, col, row, parent, collector, wallOverlayOrder);
        }

        private void PlaceWallOverlay(string id, int col, int row, Transform parent,
            List<GameObject> collector, int sortOrder)
        {
            var spr = TileAtlas.GetWallOverlay(id);
            if (spr == null) return;
            var go = new GameObject($"WallOverlay{id}_{col},{row}");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(col, row, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = spr;
            sr.sortingOrder = sortOrder;
            collector?.Add(go);
        }

        // Returns the minimal set of overlay IDs needed to cover the given floor-adjacent faces.
        //
        // Art coordinate note: the tile image has game-South at the top (perspective slab face)
        // and game-North at the bottom.  HTML sprite names therefore map inversely:
        //   ov_n  (HTML top)     → game-South face strip
        //   ov_s  (HTML bottom)  → game-North face strip
        //   ov_ne (HTML top-right)  → game S+E corner
        //   ov_nw (HTML top-left)   → game S+W corner
        //   ov_se (HTML bot-right)  → game N+E corner
        //   ov_sw (HTML bot-left)   → game N+W corner
        //   ov_corner_tr (top-right pocket)  → inner filler for game S+E join
        //   ov_corner_tl (top-left  pocket)  → inner filler for game S+W join
        //   ov_corner_br (bot-right pocket)  → inner filler for game N+E join
        //   ov_corner_bl (bot-left  pocket)  → inner filler for game N+W join
        private static IEnumerable<string> WallOverlayIds(bool n, bool s, bool e, bool w)
        {
            // All four
            if (n && s && e && w) { yield return "ov_cross"; yield break; }
            // Clean two-face corners (face strip + inner corner filler)
            if (s && e && !n && !w) { yield return "ov_ne"; yield return "ov_corner_tr"; yield break; }
            if (s && w && !n && !e) { yield return "ov_nw"; yield return "ov_corner_tl"; yield break; }
            if (n && e && !s && !w) { yield return "ov_se"; yield return "ov_corner_br"; yield break; }
            if (n && w && !s && !e) { yield return "ov_sw"; yield return "ov_corner_bl"; yield break; }
            // Three-face
            if (s && n && e) { yield return "ov_ne"; yield return "ov_s"; yield break; }
            if (s && n && w) { yield return "ov_nw"; yield return "ov_s"; yield break; }
            if (s && e && w) { yield return "ov_ne"; yield return "ov_w"; yield break; }
            if (n && e && w) { yield return "ov_se"; yield return "ov_w"; yield break; }
            // Opposite pairs
            if (n && s) { yield return "ov_s"; yield return "ov_n"; yield break; }
            if (e && w) { yield return "ov_e"; yield return "ov_w"; yield break; }
            // Singles: ov_n is the game-South strip; ov_s is the game-North strip
            if (s) yield return "ov_n";
            if (n) yield return "ov_s";
            if (e) yield return "ov_e";
            if (w) yield return "ov_w";
        }

        // Add shadow overlays on a DOOR tile at edges that face wall tiles (in-bounds, non-floor).
        // Out-of-bounds edges (void/outside) are skipped — void doesn't cast shadows.
        // Shadows go at sortOrder 3 (above door panels) and are tracked in entry.shadows
        // so they can be toggled off when the door opens.
        private void AddShadowsForDoor(int col, int row, Transform parent, DoorEntry entry)
        {
            int[]          edges = { TileAtlas.SHADOW_TOP, TileAtlas.SHADOW_BOTTOM,
                                     TileAtlas.SHADOW_RIGHT, TileAtlas.SHADOW_LEFT };
            (int dc, int dr)[] dirs = { (0, 1), (0, -1), (1, 0), (-1, 0) };
            for (int i = 0; i < 4; i++)
            {
                int nc = col + dirs[i].dc, nr = row + dirs[i].dr;
                // Skip: neighbor is floor (open side) or out of room bounds (void)
                if (IsFloorTile(nc, nr)) continue;
                if (!IsInBounds(nc, nr)) continue;
                var go  = new GameObject($"DoorShadow{edges[i]}_{col},{row}");
                go.transform.SetParent(parent);
                go.transform.localPosition = new Vector3(col, row, 0f);
                var sr  = go.AddComponent<SpriteRenderer>();
                sr.sprite       = TileAtlas.GetShadow(edges[i]);
                sr.sortingOrder = 41; // above door panels (40)
                entry.shadows.Add(sr);
            }
        }

        // Add shadow overlay GOs for a floor tile based on non-floor adjacency.
        // Add shadow overlays on a FLOOR tile using the three-family shadow system:
        //   Edge shadows    — cardinal neighbours that are walls cast a linear gradient inward.
        //   Inside corners  — where two adjacent cardinal walls meet, a radial deepens the
        //                     concave corner (replaces stacking of two edge shadows).
        //   Outside corners — when only the diagonal neighbour is a wall (both cardinals floor),
        //                     a gentle radial marks the floor tile beside the wall corner.
        private void AddShadowsForFloor(int col, int row, Transform parent,
            List<GameObject> collector = null)
        {
            bool nWall = !IsFloorTile(col,     row + 1);
            bool sWall = !IsFloorTile(col,     row - 1);
            bool eWall = !IsFloorTile(col + 1, row    );
            bool wWall = !IsFloorTile(col - 1, row    );

            // ── Cardinal edge shadows ─────────────────────────────────────────
            if (nWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_TOP,    parent, collector);
            if (sWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_BOTTOM, parent, collector);
            if (eWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_RIGHT,  parent, collector);
            if (wWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_LEFT,   parent, collector);

            // ── Corner shadows ────────────────────────────────────────────────
            // Inside corner: both adjacent cardinals are walls → concave corner radial.
            // Outside corner: both adjacent cardinals are floor, diagonal is wall → convex radial.
            bool nwWall = !IsFloorTile(col - 1, row + 1);
            bool neWall = !IsFloorTile(col + 1, row + 1);
            bool swWall = !IsFloorTile(col - 1, row - 1);
            bool seWall = !IsFloorTile(col + 1, row - 1);

            if (nWall && wWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_IN_TL,  parent, collector);
            if (nWall && eWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_IN_TR,  parent, collector);
            if (sWall && wWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_IN_BL,  parent, collector);
            if (sWall && eWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_IN_BR,  parent, collector);

            if (!nWall && !wWall && nwWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_OUT_TL, parent, collector);
            if (!nWall && !eWall && neWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_OUT_TR, parent, collector);
            if (!sWall && !wWall && swWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_OUT_BL, parent, collector);
            if (!sWall && !eWall && seWall) AddShadowOverlay(col, row, TileAtlas.SHADOW_OUT_BR, parent, collector);
        }

        private void AddShadowOverlay(int col, int row, int edge, Transform parent,
            List<GameObject> collector, int sortOrder = 11)
        {
            var go = new GameObject($"Shadow{edge}_{col},{row}");
            go.transform.SetParent(parent);
            go.transform.localPosition = new Vector3(col, row, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TileAtlas.GetShadow(edge);
            sr.sortingOrder = sortOrder;
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
            _dotTile.Clear();
            _dotTarget.Clear();
            _dotWanderAt.Clear();
            _dotClaimed.Clear();
            _selectedDots.Clear();

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
                sr.sortingOrder = 20;

                _dotTile[i] = slot;
                _dots.Add(go);
            }
        }

        private void OnTick(StationState station)
        {
            if (station.GetCrew().Count != _crew.Count) SpawnCrewDots();
            RebuildFoundationTiles();
            // Re-apply network overlay tinting every tick so supply-state changes
            // (isEnergised, isFluidSupplied, isGasSupplied) are reflected visually.
            if (_viewMode != ViewMode.Normal)
                UpdateNetworkOverlay();
        }

        // ── Drag-selection helpers ────────────────────────────────────────────

        private void CommitDragSelection()
        {
            _selectedDots.Clear();
            if (_dragSelRect.width < 2f && _dragSelRect.height < 2f)
            {
                // Point-click: pick the nearest dot within 30 screen px
                float best = 30f * 30f;
                int   pick = -1;
                for (int i = 0; i < _dots.Count; i++)
                {
                    if (!_dots[i]) continue;
                    Vector3 sp = Camera.main != null
                        ? Camera.main.WorldToScreenPoint(_dots[i].transform.position)
                        : Vector3.zero;
                    float dx = sp.x - _dragSelStart.x;
                    float dy = sp.y - _dragSelStart.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; pick = i; }
                }
                if (pick >= 0) _selectedDots.Add(pick);
                return;
            }

            // Rect-drag: select all dots whose screen position falls inside
            for (int i = 0; i < _dots.Count; i++)
            {
                if (!_dots[i]) continue;
                Vector3 sp = Camera.main != null
                    ? Camera.main.WorldToScreenPoint(_dots[i].transform.position)
                    : Vector3.zero;
                float gy = Screen.height - sp.y;
                if (_dragSelRect.Contains(new Vector2(sp.x, gy)))
                    _selectedDots.Add(i);
            }
        }

        private void MoveSelectedToTile(int tileCol, int tileRow)
        {
            // Candidate positions: target first, then surrounding tiles
            var positions = new List<Vector2Int>
            {
                new Vector2Int(tileCol, tileRow),
                new Vector2Int(tileCol-1, tileRow), new Vector2Int(tileCol+1, tileRow),
                new Vector2Int(tileCol, tileRow-1), new Vector2Int(tileCol, tileRow+1),
                new Vector2Int(tileCol-1, tileRow-1), new Vector2Int(tileCol+1, tileRow-1),
                new Vector2Int(tileCol-1, tileRow+1), new Vector2Int(tileCol+1, tileRow+1),
            };

            // Tiles occupied by non-selected dots (they stay put)
            var occupied = new HashSet<Vector2Int>();
            for (int i = 0; i < _dots.Count; i++)
                if (!_selectedDots.Contains(i) && _dotTile.TryGetValue(i, out var t))
                    occupied.Add(t);

            var available = new Queue<Vector2Int>();
            foreach (var p in positions)
                if (p.x >= IntMinC && p.x <= IntMaxC &&
                    p.y >= IntMinR && p.y <= IntMaxR &&
                    !occupied.Contains(p))
                    available.Enqueue(p);

            foreach (int i in _selectedDots)
            {
                if (available.Count == 0) break;
                var dest = available.Dequeue();
                occupied.Add(dest);
                _dotTile[i]   = dest;
                _dotTarget[i] = new Vector3(dest.x, dest.y, -0.2f);
            }
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
                // Draw a highlight ring around selected dots.
                if (_selectedDots.Contains(i))
                {
                    Vector3 dotSp = Camera.main.WorldToScreenPoint(_dots[i].transform.position);
                    float rx = dotSp.x - 10f;
                    float ry = Screen.height - dotSp.y - 10f;
                    GUI.color = new Color(1f, 0.88f, 0.25f, 0.85f);
                    GUI.Box(new Rect(rx, ry, 20f, 20f), GUIContent.none);
                    GUI.color = Color.white;
                }
                GUI.Label(new Rect(gx, gy, 130f, 32f), _crewLabels[i], _labelStyle);
            }

            // ── Drag-selection rectangle ──────────────────────────────────────
            if (_isDragSelecting && (_dragSelRect.width > 2f || _dragSelRect.height > 2f))
            {
                GUI.color = new Color(0.35f, 0.75f, 1f, 0.15f);
                GUI.DrawTexture(_dragSelRect, Texture2D.whiteTexture);
                GUI.color = new Color(0.35f, 0.75f, 1f, 0.70f);
                GUI.Box(_dragSelRect, GUIContent.none);
                GUI.color = Color.white;
            }

            // ── Right-click context menu ──────────────────────────────────────
            if (_showContextMenu && _selectedDots.Count > 0)
            {
                if (_ctxBoxStyle == null)
                {
                    _ctxBoxStyle = new GUIStyle(GUI.skin.box)
                    {
                        padding    = new RectOffset(0, 0, 4, 4),
                        margin     = new RectOffset(0, 0, 0, 0),
                        normal     = { background = MakeSolidTexture(new Color(0.08f, 0.10f, 0.16f, 0.96f)) },
                    };
                    _ctxBtnStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontSize  = 11,
                        alignment = TextAnchor.MiddleLeft,
                        padding   = new RectOffset(10, 10, 5, 5),
                        normal    = { textColor = new Color(0.85f, 0.90f, 1.00f) },
                        hover     = { textColor = Color.white,
                                      background = MakeSolidTexture(new Color(0.25f, 0.45f, 0.80f, 0.85f)) },
                    };
                }

                // Work out what is at the clicked tile
                bool isInterior = _ctxTileCol >= IntMinC && _ctxTileCol <= IntMaxC &&
                                  _ctxTileRow >= IntMinR && _ctxTileRow <= IntMaxR;

                string workLabel = null;
                if (_gm?.Station != null)
                {
                    foreach (var kv in _gm.Station.foundations)
                    {
                        if (kv.Value.tileCol == _ctxTileCol && kv.Value.tileRow == _ctxTileRow
                            && kv.Value.status != "complete")
                        {
                            workLabel = $"Construct: {kv.Value.buildableId.Split('.')[^1]}";
                            break;
                        }
                    }
                }

                int btnH  = 28, menuW = 170;
                int rows  = isInterior ? (workLabel != null ? 3 : 2) : 1;
                float mx  = Mathf.Clamp(_contextMenuScreen.x, 0, Screen.width  - menuW - 4);
                float my  = Mathf.Clamp(_contextMenuScreen.y, 0, Screen.height - rows * btnH - 8);
                var   box = new Rect(mx, my, menuW, rows * btnH + 8);

                GUI.Box(box, GUIContent.none, _ctxBoxStyle);
                GUI.BeginGroup(box);
                int by = 4;
                if (isInterior)
                {
                    if (GUI.Button(new Rect(1, by, menuW - 2, btnH), "\u25b6 Move here", _ctxBtnStyle))
                    {
                        MoveSelectedToTile(_ctxTileCol, _ctxTileRow);
                        _showContextMenu = false;
                    }
                    by += btnH;
                }
                if (workLabel != null)
                {
                    if (GUI.Button(new Rect(1, by, menuW - 2, btnH), $"\u2692 {workLabel}", _ctxBtnStyle))
                    {
                        AssignSelectedToConstruction(_ctxTileCol, _ctxTileRow);
                        _showContextMenu = false;
                    }
                    by += btnH;
                }
                if (GUI.Button(new Rect(1, by, menuW - 2, btnH), "\u2715 Cancel", _ctxBtnStyle))
                    _showContextMenu = false;
                GUI.EndGroup();

                // Close on Escape
                if (Event.current.type == EventType.KeyDown &&
                    Event.current.keyCode == KeyCode.Escape)
                    _showContextMenu = false;
            }

            // ── Context drawer (bottom-left panel) ────────────────────────────
            DrawContextDrawer();
        }

        private static readonly string[] _rankLabels = { "Crew", "Officer", "Senior", "Command" };

        private void DrawContextDrawer()
        {
            // Lazy-init styles
            if (_ctxPanelStyle == null)
            {
                _ctxPanelStyle = new GUIStyle(GUI.skin.box)
                {
                    normal = { background = MakeSolidTexture(new Color(0.07f, 0.09f, 0.14f, 0.93f)) },
                    padding = new RectOffset(8, 8, 6, 6),
                };
                _ctxHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 12,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = new Color(0.85f, 0.90f, 1f) },
                };
                _ctxValueStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    normal   = { textColor = new Color(0.65f, 0.72f, 0.88f) },
                };
                _ctxCtaStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = Color.white },
                };
            }

            bool hasNpcSel   = _selectedDots.Count > 0 && _crew.Count > 0;
            bool hasFoundCtx = _ctxFoundation != null && _gm?.Station != null &&
                               _gm.Station.foundations.ContainsKey(_ctxFoundation.uid);

            if (!hasNpcSel && !hasFoundCtx) { _doorAccessEditing = false; return; }

            bool isDoor = hasFoundCtx && _ctxFoundation.buildableId.Contains("door") &&
                          _ctxFoundation.status == "complete";
            if (!isDoor) _doorAccessEditing = false;

            // Fetch network inspection data once so it can inform height + drawing.
            NetworkInspectionData netInspect = null;
            if (hasFoundCtx && _ctxFoundation.networkId != null && _gm?.UtilityNetworks != null)
                netInspect = _gm.UtilityNetworks.GetInspectionData(_gm.Station, _ctxFoundation.uid);

            // Compute dynamic panel height so it expands upward for door controls or extra NPC lines.
            float PH = 150f;
            if (isDoor)
            {
                PH += 8f  +  // separator space
                      22f +  // Hold Open toggle
                      22f +  // Allow All toggle
                      26f;   // Manage Access button
                if (_doorAccessEditing)
                {
                    var pol         = _ctxFoundation.accessPolicy;
                    int speciesRows = pol != null ? pol.allowedSpecies.Count       : 0;
                    int deptRows    = pol != null ? pol.allowedDepartmentIds.Count : 0;
                    PH += 22f                          // section header
                        + 18f + 18f                    // min rank label + slider
                        + 18f + speciesRows * 20f + 22f  // species label + chips + input
                        + 18f + deptRows    * 20f + 22f  // dept label + chips + input
                        + 22f                          // faction row
                        + 8f;                          // bottom padding
                }
            }

            // Expand height for network member list (producers, consumers, storage).
            const int MaxMembersShown = 6;
            if (netInspect != null && netInspect.Members.Count > 0)
            {
                int shown = Mathf.Min(netInspect.Members.Count, MaxMembersShown);
                PH += 18f          // "Members:" header
                    + shown * 14f; // one row per member
                if (netInspect.Members.Count > MaxMembersShown)
                    PH += 12f;     // "+N more…" overflow line
            }

            const float PW = 290f;
            float px = 10f, py = Screen.height - PH - 10f;
            var panelRect = new Rect(px, py, PW, PH);
            GUI.Box(panelRect, GUIContent.none, _ctxPanelStyle);
            GUI.BeginGroup(panelRect);

            float y = 6f;
            if (hasFoundCtx)
            {
                var f     = _ctxFoundation;
                string bName = f.buildableId.Contains('.') ? f.buildableId.Split('.')[^1] : f.buildableId;
                GUI.Label(new Rect(8, y, PW - 16, 18), $"\u25a3  {bName}  [{f.status}]", _ctxHeaderStyle);
                y += 20;
                GUI.Label(new Rect(8, y, PW - 16, 16), $"HP: {f.health}/{f.maxHealth}  |  Layer: {f.tileLayer}", _ctxValueStyle);
                y += 18;
                if (f.networkId != null && _gm.Station.networks.TryGetValue(f.networkId, out var net))
                {
                    string content = net.contentType != null ? $"  [{net.contentType}]" : "";
                    GUI.Label(new Rect(8, y, PW - 16, 16), $"Network: {net.networkType}{content}  ({net.memberUids.Count} tiles)", _ctxValueStyle);
                    y += 18;
                    // ── Utility network inspection data ───────────────────────
                    if (net.networkType == "electric")
                    {
                        GUI.Label(new Rect(8, y, PW - 16, 16),
                            $"Supply: {net.totalSupply:F0}W  Demand: {net.totalDemand:F0}W", _ctxValueStyle);
                        y += 16;
                        GUI.Label(new Rect(8, y, PW - 16, 16),
                            $"Battery: {net.storedEnergy:F0}/{net.storageCapacity:F0} Wh", _ctxValueStyle);
                        y += 18;
                    }
                    else
                    {
                        GUI.Label(new Rect(8, y, PW - 16, 16),
                            $"Stored: {net.contentAmount:F0}/{net.contentCapacity:F0} L", _ctxValueStyle);
                        y += 18;
                    }
                    // ── Member list (producers, consumers, storage) ───────────
                    if (netInspect != null && netInspect.Members.Count > 0)
                    {
                        GUI.Label(new Rect(8, y, PW - 16, 16), "Members:", _ctxValueStyle);
                        y += 16;
                        int shown = Mathf.Min(netInspect.Members.Count, MaxMembersShown);
                        for (int mi = 0; mi < shown; mi++)
                        {
                            var m        = netInspect.Members[mi];
                            string bName = m.BuildableId.Contains('.') ? m.BuildableId.Split('.')[^1] : m.BuildableId;
                            string stat  = m.Role switch
                            {
                                "producer" => net.networkType == "electric"
                                    ? $"+{m.OutputWatts:F0}W"
                                    : "+prod",
                                "consumer" => net.networkType == "electric"
                                    ? $"-{m.DemandWatts:F0}W {(m.IsEnergised ? "\u2713" : "\u2717")}"
                                    : $"{(m.IsSupplied ? "\u2713" : "\u2717")}",
                                "storage"  => net.networkType == "electric"
                                    ? $"{m.StoredAmount:F0} Wh"
                                    : $"{m.StoredAmount:F0} L",
                                _          => m.Role,
                            };
                            GUI.Label(new Rect(16, y, PW - 24, 13),
                                $"\u2022 {bName}  [{m.Role}]  {stat}", _ctxValueStyle);
                            y += 14;
                        }
                        if (netInspect.Members.Count > MaxMembersShown)
                        {
                            GUI.Label(new Rect(16, y, PW - 24, 12),
                                $"+ {netInspect.Members.Count - MaxMembersShown} more\u2026", _ctxValueStyle);
                            y += 12;
                        }
                    }
                }
                // ── Isolator toggle ───────────────────────────────────────────
                if (f.networkId != null && IsIsolator(f))
                {
                    string state = f.isolatorOpen ? "Open" : "Closed";
                    if (GUI.Button(new Rect(8, y, PW - 16, 20), $"Toggle {GetIsolatorLabel(f.buildableId)} [{state}]", _ctxCtaStyle))
                    {
                        _gm.UtilityNetworks.ToggleIsolator(_gm.Station, f.uid);
                    }
                    y += 26;
                }
                if (f.buildableId == "buildable.ice_refiner")
                {
                    string opState = f.operatingState ?? "standby";
                    GUI.Label(new Rect(8, y, PW - 16, 16), $"State: {opState}", _ctxValueStyle);
                }

                // ── Door access controls ─────────────────────────────────────
                if (isDoor)
                {
                    y += 8;

                    // Hold Open toggle
                    bool newHold = GUI.Toggle(new Rect(8, y, PW - 16, 18), f.doorHoldOpen, " Hold Open");
                    if (newHold != f.doorHoldOpen) f.doorHoldOpen = newHold;
                    y += 22;

                    // Allow All toggle
                    bool curAllow = (f.accessPolicy == null) || f.accessPolicy.allowAll;
                    bool newAllow = GUI.Toggle(new Rect(8, y, PW - 16, 18), curAllow, " Allow All (no restrictions)");
                    if (newAllow != curAllow)
                    {
                        if (f.accessPolicy == null) f.accessPolicy = new DoorAccessPolicy();
                        f.accessPolicy.allowAll = newAllow;
                    }
                    y += 22;

                    // Manage Access button
                    string manageLabel = _doorAccessEditing ? "\u25b2 Close Access Editor" : "\u25bc Manage Access\u2026";
                    if (GUI.Button(new Rect(8, y, PW - 16, 20), manageLabel, _ctxCtaStyle))
                        _doorAccessEditing = !_doorAccessEditing;
                    y += 26;

                    if (_doorAccessEditing)
                        DrawDoorAccessEditor(f, PW, ref y);
                }
            }
            else if (hasNpcSel)
            {
                // Show first selected NPC
                int di = -1;
                foreach (int x2 in _selectedDots) { di = x2; break; }
                if (di >= 0 && di < _crew.Count)
                {
                    var npc = _crew[di];
                    string deptLabel = npc.departmentId != null ? npc.departmentId : "Crewman";
                    if (npc.departmentId != null && _gm?.Station != null)
                    {
                        foreach (var d in _gm.Station.departments)
                            if (d.uid == npc.departmentId) { deptLabel = d.name; break; }
                    }
                    GUI.Label(new Rect(8, y, PW - 16, 18), $"\u25c9  {npc.name}  \u2014  {deptLabel}", _ctxHeaderStyle);
                    y += 20;
                    string rankStr = npc.rank switch { 1 => "\u2605 Officer", 2 => "\u2605\u2605 Senior", 3 => "\u2605\u2605\u2605 Command", _ => "Crew" };
                    npc.needs.TryGetValue("sleep", out float sv);
                    float sleepVal  = npc.isSleeping ? 1f : (sv > 0f ? sv : 1f);
                    string sleepTxt = npc.isSleeping ? "sleeping" : sleepVal.ToString("P0");
                    string missionTxt = npc.missionUid != null ? "on mission" : "available";
                    GUI.Label(new Rect(8, y, PW - 16, 16), $"Rank: {rankStr}  |  Mood: {npc.MoodLabel()}  |  {missionTxt}", _ctxValueStyle);
                    y += 18;
                    GUI.Label(new Rect(8, y, PW - 16, 16), $"Sleep: {sleepTxt}  |  Injuries: {npc.injuries}", _ctxValueStyle);
                }
            }

            GUI.EndGroup();
        }

        /// Draws the access rule editor inside the already-open GUI.BeginGroup panel.
        private void DrawDoorAccessEditor(FoundationInstance f, float panelWidth, ref float y)
        {
            if (f.accessPolicy == null) f.accessPolicy = new DoorAccessPolicy();
            var pol      = f.accessPolicy;
            float innerW = panelWidth - 16f;

            // Section header
            GUI.Label(new Rect(8, y, innerW, 18), "\u2014 Access Rules \u2014", _ctxHeaderStyle);
            y += 22;

            // Min Rank — slider 0..3
            string rLabel = (pol.minRank >= 0 && pol.minRank < _rankLabels.Length)
                            ? _rankLabels[pol.minRank] : pol.minRank.ToString();
            GUI.Label(new Rect(8, y, innerW, 16), $"Min Rank: {rLabel}", _ctxValueStyle);
            y += 18;
            pol.minRank = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(8, y, innerW, 14), pol.minRank, 0, 3));
            y += 18;

            // ── Allowed Species ──────────────────────────────────────────────
            GUI.Label(new Rect(8, y, innerW, 16), "Allowed Species (empty = any):", _ctxValueStyle);
            y += 18;
            for (int i = pol.allowedSpecies.Count - 1; i >= 0; i--)
            {
                GUI.Label(new Rect(10, y, innerW - 30, 18), pol.allowedSpecies[i], _ctxValueStyle);
                if (GUI.Button(new Rect(innerW - 18, y, 22, 18), "\u00d7", _ctxCtaStyle))
                    pol.allowedSpecies.RemoveAt(i);
                y += 20;
            }
            _doorAccessInput_Species = GUI.TextField(new Rect(8, y, innerW - 32, 18), _doorAccessInput_Species ?? "");
            if (GUI.Button(new Rect(innerW - 20, y, 28, 18), "+", _ctxCtaStyle))
            {
                string s = (_doorAccessInput_Species ?? "").Trim().ToLower();
                if (s.Length > 0 && !pol.allowedSpecies.Contains(s))
                {
                    pol.allowedSpecies.Add(s);
                    _doorAccessInput_Species = "";
                }
            }
            y += 22;

            // ── Allowed Departments ──────────────────────────────────────────
            GUI.Label(new Rect(8, y, innerW, 16), "Allowed Departments (empty = any):", _ctxValueStyle);
            y += 18;
            for (int i = pol.allowedDepartmentIds.Count - 1; i >= 0; i--)
            {
                // Resolve dept name if possible
                string dName = pol.allowedDepartmentIds[i];
                if (_gm?.Station != null)
                    foreach (var dept in _gm.Station.departments)
                        if (dept.uid == pol.allowedDepartmentIds[i]) { dName = dept.name; break; }
                GUI.Label(new Rect(10, y, innerW - 30, 18), dName, _ctxValueStyle);
                if (GUI.Button(new Rect(innerW - 18, y, 22, 18), "\u00d7", _ctxCtaStyle))
                    pol.allowedDepartmentIds.RemoveAt(i);
                y += 20;
            }
            _doorAccessInput_Dept = GUI.TextField(new Rect(8, y, innerW - 32, 18), _doorAccessInput_Dept ?? "");
            if (GUI.Button(new Rect(innerW - 20, y, 28, 18), "+", _ctxCtaStyle))
            {
                string s = (_doorAccessInput_Dept ?? "").Trim();
                if (s.Length > 0 && !pol.allowedDepartmentIds.Contains(s))
                {
                    pol.allowedDepartmentIds.Add(s);
                    _doorAccessInput_Dept = "";
                }
            }
            y += 22;

            // ── Required Faction ─────────────────────────────────────────────
            float halfW = (innerW - 8f) * 0.45f;
            GUI.Label(new Rect(8, y, halfW, 16), "Faction ID:", _ctxValueStyle);
            string newFac = GUI.TextField(new Rect(8 + halfW + 4, y, innerW - halfW - 12, 18),
                                          pol.requiredFactionId ?? "");
            pol.requiredFactionId = newFac.Length == 0 ? null : newFac;
            y += 22;
        }

        private void AssignSelectedToConstruction(int col, int row)
        {
            if (_gm?.Station == null) return;
            foreach (var kv in _gm.Station.foundations)
            {
                var f = kv.Value;
                if (f.tileCol != col || f.tileRow != row) continue;
                if (f.status == "complete") continue;
                // Assign first idle selected crew member to this foundation
                foreach (int di in _selectedDots)
                {
                    if (di >= _crew.Count) continue;
                    var npc = _crew[di];
                    npc.currentJobId  = "job.build";
                    npc.jobModuleUid  = f.uid;
                    f.assignedNpcUid  = npc.uid;
                    MoveSelectedToTile(col, row);
                    break;
                }
                break;
            }
        }

        private static Texture2D MakeSolidTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
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
