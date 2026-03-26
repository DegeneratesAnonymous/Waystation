// TileAtlas — procedural pixel-art tile sprites (64×64 px).
//
// Tile types:
//   FLOOR_0 … FLOOR_4  — five floor variants; pick randomly per cell, rotate freely.
//   WALL_0  … WALL_4   — five wall variants;  pick randomly per cell, NO rotation.
//
// Shadow overlays (semi-transparent, composited on top of floor/wall tiles):
//   SHADOW_TOP/RIGHT/BOTTOM/LEFT  — edge: linear gradient DEPTH=18, ALPHA=0.30.
//   SHADOW_IN_TL/TR/BL/BR         — inside corner (concave): radial from wall corner,
//                                   for floor tile with two adjacent card. walls.
//   SHADOW_OUT_TL/TR/BL/BR        — outside corner (convex): gentle radial,
//                                   for floor tile diagonally adjacent to isolated wall.
//
// Door animation:
//   GetDoorHFrames() / GetDoorVFrames() — 5-frame arrays (closed → open).
//   The open gap is transparent — render the floor tile first, composite door on top.
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.View
{
    public static class TileAtlas
    {
        // ── Tile indices ──────────────────────────────────────────────────────
        public const int FLOOR_0 = 0;
        public const int FLOOR_1 = 1;
        public const int FLOOR_2 = 2;
        public const int FLOOR_3 = 3;
        public const int FLOOR_4 = 4;
        public const int WALL_0  = 5;
        public const int WALL_1  = 6;
        public const int WALL_2  = 7;
        public const int WALL_3  = 8;
        public const int WALL_4  = 9;
        // Shadow edge indices (cardinal) — linear gradient from named edge inward
        public const int SHADOW_TOP      = 0;
        public const int SHADOW_RIGHT    = 1;
        public const int SHADOW_BOTTOM   = 2;
        public const int SHADOW_LEFT     = 3;
        // Inside-corner shadows — radial on floor tile where two card. walls meet
        public const int SHADOW_IN_TL    = 4;  // walls: north + west
        public const int SHADOW_IN_TR    = 5;  // walls: north + east
        public const int SHADOW_IN_BL    = 6;  // walls: south + west
        public const int SHADOW_IN_BR    = 7;  // walls: south + east
        // Outside-corner shadows — gentle radial on floor tile diagonally adj. to wall corner
        public const int SHADOW_OUT_TL   = 8;  // wall corner: north-west diagonal
        public const int SHADOW_OUT_TR   = 9;  // wall corner: north-east diagonal
        public const int SHADOW_OUT_BL   = 10; // wall corner: south-west diagonal
        public const int SHADOW_OUT_BR   = 11; // wall corner: south-east diagonal

        private static Sprite[] _cache;        // [0..9]  floor 0-4, wall 0-4
        private static Sprite[] _shadowCache;  // [0..11] 4 edges + 4 inside corners + 4 outside corners
        private static Sprite[] _cabinetCache; // [8]: orientation(V=0..3, H=4..7) × fill(0-3)
        private static Sprite   _batteryCache;  // single sprite, 128×64
        private static Sprite[] _wireCache;     // [16] topology mask 0-15
        private static Sprite[,] _pipeCache;    // [16, 3] mask × state (0=normal,1=pressurized,2=burst)
        private static Sprite[] _ductCache;     // [16] topology mask 0-15
        private static Dictionary<string, Sprite> _iceRefinerCache; // 15 named variants
        private static Sprite[] _bedCache;      // [4] rotation steps (0/90/180/270)
        private static Dictionary<string, Sprite> _generatorCache; // keyed by state id

        // ── Public accessors ──────────────────────────────────────────────────
        public static Sprite GetFloor(int variant)
        {
            EnsureCache();
            return _cache[Mathf.Clamp(variant, 0, 4)];
        }

        public static Sprite GetWall(int variant)
        {
            EnsureCache();
            return _cache[WALL_0 + Mathf.Clamp(variant, 0, 4)];
        }

        public static Sprite GetShadow(int edge)
        {
            if (_shadowCache == null)
            {
                _shadowCache = new Sprite[12];
                for (int i = 0; i < 12; i++) _shadowCache[i] = MakeShadow(i);
            }
            return _shadowCache[Mathf.Clamp(edge, 0, 11)];
        }

        /// Returns the battery bank sprite (128×64 px = 2×1 world units).
        public static Sprite GetBattery()
        {
            if (_batteryCache == null) _batteryCache = MakeBattery128();
            return _batteryCache;
        }

        /// <summary>
        /// Returns the cabinet sprite for the given rotation (0/90/180/270) and fill ratio.
        /// rotation 0/180 → vertical (portrait) sprite.
        /// rotation 90/270 → horizontal (landscape) sprite — a distinct alternate tile.
        /// fillRatio drives the 3 capacity LEDs: 0 pips, 1 pip, 2 pips, or 3 pips lit.
        /// </summary>
        public static Sprite GetCabinet(int rotation, float fillRatio = 1f)
        {
            if (_cabinetCache == null)
            {
                // 8 sprites: indices 0-3 = V variant fill 0-3; indices 4-7 = H variant fill 0-3.
                _cabinetCache = new Sprite[8];
                for (int f = 0; f < 4; f++)
                {
                    _cabinetCache[f]     = MakeCabinet(false, f);
                    _cabinetCache[4 + f] = MakeCabinet(true,  f);
                }
            }
            bool isH  = (rotation == 90 || rotation == 270);
            int  fidx = fillRatio <= 0f ? 0 : fillRatio < 0.34f ? 1 : fillRatio < 0.67f ? 2 : 3;
            return _cabinetCache[(isH ? 4 : 0) + fidx];
        }

        /// Returns the tile sprite used to preview or ghost-render a buildable.
        /// For doors the closed frame is returned; cabinet respects rotation.
        public static Sprite GetPreviewSprite(string buildableId, int rotation = 0)
        {
            if (buildableId.Contains("storage_cabinet")) return GetCabinet(rotation, 0f);
            if (buildableId.Contains("battery"))         return GetBattery();
            if (buildableId.Contains("door"))            return GetDoorHFrames()[0];
            if (buildableId.Contains("wall"))            return GetWallAtlas("straight_ew");
            if (buildableId.Contains("wire"))            return GetWire(0xF);
            if (buildableId.Contains("pipe"))            return GetPipe(0xF, "normal");
            if (buildableId.Contains("duct"))            return GetDuct(0xF);
            if (buildableId.Contains("ice_refiner"))     return GetIceRefiner("standby");
            if (buildableId.Contains("bed"))             return GetBed(0);
            if (buildableId.Contains("generator"))       return GetGenerator("normal");
            return GetFloor(0);
        }

        // ── Wire sprites (electric network) ──────────────────────────────────

        /// <summary>
        /// Returns the wire topology sprite for a 4-bit connection mask
        /// (N=1, E=2, S=4, W=8).
        /// </summary>
        public static Sprite GetWire(int connectionMask)
        {
            if (_wireCache == null)
            {
                _wireCache = new Sprite[16];
                for (int m = 0; m < 16; m++) _wireCache[m] = MakeWire(m);
            }
            return _wireCache[connectionMask & 0xF];
        }

        // ── Pipe sprites (fluid network) ───────────────────────────────────

        /// <summary>
        /// Pipe topology sprite.  state = "normal" | "pressurized" | "burst".
        /// </summary>
        public static Sprite GetPipe(int connectionMask, string state = "normal")
        {
            if (_pipeCache == null)
            {
                _pipeCache = new Sprite[16, 3];
                for (int m = 0; m < 16; m++)
                for (int s = 0; s < 3; s++)
                    _pipeCache[m, s] = MakePipe(m, s);
            }
            int si = state == "pressurized" ? 1 : state == "burst" ? 2 : 0;
            return _pipeCache[connectionMask & 0xF, si];
        }

        // ── Duct sprites (gas network) ─────────────────────────────────────

        /// <summary>
        /// Duct topology sprite for the given 4-bit connection mask.
        /// </summary>
        public static Sprite GetDuct(int connectionMask)
        {
            if (_ductCache == null)
            {
                _ductCache = new Sprite[16];
                for (int m = 0; m < 16; m++) _ductCache[m] = MakeDuct(m);
            }
            return _ductCache[connectionMask & 0xF];
        }

        // ── Ice Refiner sprites (128×64 px) ───────────────────────────────

        private static readonly string[] IceRefinerVariants =
        {
            "standby",
            "refining_0", "refining_1", "refining_2", "refining_3", "refining_4",
            "output_0",   "output_1",   "output_2",   "output_3",   "output_4",
            "damaged_0",  "damaged_1",  "damaged_2",
            "broken"
        };

        /// <summary>
        /// Ice refiner sprite by variant id (see IceRefinerVariants).
        /// Falls back to "standby" for unknown ids.
        /// </summary>
        public static Sprite GetIceRefiner(string variantId)
        {
            if (_iceRefinerCache == null)
            {
                _iceRefinerCache = new Dictionary<string, Sprite>();
                foreach (var v in IceRefinerVariants)
                    _iceRefinerCache[v] = MakeIceRefiner(v);
            }
            if (_iceRefinerCache.TryGetValue(variantId, out var s)) return s;
            return _iceRefinerCache["standby"];
        }

        // ── Bed sprites ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the bed sprite.  rotation is currently always 0 (single orientation).
        /// </summary>
        public static Sprite GetBed(int rotation = 0)
        {
            if (_bedCache == null)
            {
                _bedCache = new Sprite[4];
                for (int r = 0; r < 4; r++) _bedCache[r] = MakeBed(r);
            }
            return _bedCache[Mathf.Clamp(rotation / 90, 0, 3)];
        }

        // ── Generator sprites (128×128 px, 2×2 world units) ──────────────────

        /// <summary>
        /// Returns the generator sprite for the given state.
        /// state: "normal" | "damaged" | "destroyed".
        /// Loaded from the sliced atlas at Resources/Buildables/generator_atlas.
        /// Falls back to "normal" for unknown states.
        /// </summary>
        public static Sprite GetGenerator(string state = "normal")
        {
            if (_generatorCache == null)
            {
                _generatorCache = new Dictionary<string, Sprite>();
                // Resources.LoadAll returns all sprite slices of the atlas by their name.
                // The atlas lives at Assets/Art/Tiles/Resources/Buildables/generator_atlas.png
                // which maps to runtime path "Buildables/generator_atlas".
                Sprite[] slices = Resources.LoadAll<Sprite>("Buildables/generator_atlas");
                foreach (var s in slices)
                    _generatorCache[s.name] = s;
            }
            string key = "prop_generator_" + state;
            if (_generatorCache.TryGetValue(key, out var sprite)) return sprite;
            if (_generatorCache.TryGetValue("prop_generator_normal", out var fallback)) return fallback;
            return null;
        }

        // ── Wall atlas sprites ───────────────────────────────────────────────

        private static Dictionary<string, Sprite> _wallAtlasCache;

        // ── New wall atlas (wall_atlas.png — base + interior overlays) ────────
        // The atlas has 16 sprites:
        //   Col 0  : wall_solid_normal   — 64×64 opaque base tile
        //   Cols 1-15 : wall_solid_int_* — 64×64 transparent overlay, selected by
        //              interior adjacency bitmask  (n=8 | s=4 | e=2 | w=1)
        //
        // Overlay sprites are 64×64 (same footprint as the base).  The perspective
        // face strip (bottom 10px when s=1) is baked inside the 64px tile.  Overlays
        // are placed at the exact same world position as the base tile — no Y offset.
        //
        // wall_shadow_atlas contains a single 64×64 sprite whose top 12px darken the
        // floor tile immediately south of a wall.  Place it at (col, row-1).
        private static bool   _wallSolidAtlasChecked;
        private static Sprite   _wallSolidBase;
        private static Sprite[] _wallInteriorOverlays; // [16]: index = bitmask, [0]=unused
        private static Sprite   _wallShadow;
        // Kept for backward compatibility — overlays no longer need a Y shift.
        public  const  float WallFaceStripH = 0f;

        /// <summary>Returns the opaque 64×64 base wall tile from wall_atlas.</summary>
        public static Sprite GetWallSolidBase()
        {
            EnsureWallSolidAtlas();
            return _wallSolidBase ?? GetWall(0);
        }

        /// <summary>
        /// Returns the 64×64 shadow sprite for the floor tile south of a wall.
        /// Darkening is in the top 12px only.  Place at (col, row-1), sortOrder 11.
        /// </summary>
        public static Sprite GetWallShadow()
        {
            if (_wallShadow != null) return _wallShadow;
            Texture2D tex = Resources.Load<Texture2D>("Walls/wall_shadow_atlas");
            if (tex == null)
            {
                Debug.LogWarning("[TileAtlas] wall_shadow_atlas texture not found at Resources/Walls/wall_shadow_atlas.");
                return null;
            }
            // Single 64×64 tile at the origin of the texture.
            _wallShadow = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f),
                                        new Vector2(0.5f, 0.5f), 64f);
            return _wallShadow;
        }

        /// <summary>
        /// Returns the interior overlay sprite for the given adjacency bitmask.
        /// Bitmask: n=8, s=4, e=2, w=1 — set each bit when that neighbour is floor/interior.
        /// Returns null when bitmask is 0 (no interior neighbours — no overlay needed).
        /// </summary>
        public static Sprite GetWallInteriorOverlay(int bitmask)
        {
            if (bitmask <= 0 || bitmask > 15) return null;
            EnsureWallSolidAtlas();
            return _wallInteriorOverlays?[bitmask];
        }

        private static void EnsureWallSolidAtlas()
        {
            if (_wallSolidAtlasChecked) return;
            _wallSolidAtlasChecked = true;
            _wallInteriorOverlays = new Sprite[16];

            // Load the raw texture rather than relying on the importer having sliced it.
            // Resources.Load<Texture2D> works even when the texture is imported as Sprite type.
            Texture2D tex = Resources.Load<Texture2D>("Walls/wall_atlas");
            if (tex == null)
            {
                Debug.LogWarning("[TileAtlas] wall_atlas texture not found at Resources/Walls/wall_atlas. "
                    + "Walls will use procedural fallback.");
                return;
            }

            // Atlas layout (from wall_atlas.json):
            //   16 columns × 1 row  |  slot 66×66  |  padding 1px  |  tile 64×64
            //   col index = sprite index (0 = base, 1-15 = overlays by bitmask value)
            // In Unity texel space (Y=0 at texture bottom):
            //   x = col * 66 + 1
            //   y = 1   (only one row, one padding pixel from the bottom edge)
            Sprite SliceCol(int col)
                => Sprite.Create(tex,
                                 new Rect(col * 66f + 1f, 1f, 64f, 64f),
                                 new Vector2(0.5f, 0.5f),
                                 64f);

            _wallSolidBase = SliceCol(0);

            // Bitmask → atlas column  (n=8, s=4, e=2, w=1)
            // Column index equals the bitmask value for all 15 overlays.
            var colByMask = new (int mask, int col)[]
            {
                ( 4,  1), ( 8,  2), ( 2,  3), ( 1,  4),
                ( 6,  5), ( 5,  6), (10,  7), ( 9,  8),
                (12,  9), ( 3, 10), (14, 11), (13, 12),
                (11, 13), ( 7, 14), (15, 15),
            };
            foreach (var (mask, col) in colByMask)
                _wallInteriorOverlays[mask] = SliceCol(col);

            Debug.Log($"[TileAtlas] wall_atlas sliced at runtime from texture "
                + $"({tex.width}×{tex.height}). base ok.");
        }

        /// <summary>
        /// Returns a wall sprite from the atlas by connectivity shape and health state.
        /// shape: "straight_ew" | "straight_ns" | "corner_ne" | "corner_nw" | "corner_se" |
        ///        "corner_sw" | "tjunc_n" | "tjunc_s" | "tjunc_e" | "tjunc_w" | "cross" |
        ///        "endcap_n" | "endcap_s" | "endcap_e" | "endcap_w"
        /// state: "normal" | "damaged" | "destroyed"
        /// Falls back to procedural sprite if the atlas is unavailable.
        /// </summary>
        public static Sprite GetWallAtlas(string shapeId, string state = "normal")
        {
            if (_wallAtlasCache == null)
            {
                _wallAtlasCache = new Dictionary<string, Sprite>();

                // Try the primary path under the Art/Tiles/Resources folder.
                Sprite[] slices = Resources.LoadAll<Sprite>("Walls/wall_atlas");

                // Some Unity versions return an empty array for the bare texture name;
                // try loading the texture explicitly then pulling its sub-assets.
                if (slices == null || slices.Length == 0)
                {
                    var tex = Resources.Load<Texture2D>("Walls/wall_atlas");
                    if (tex != null)
                        slices = Resources.LoadAll<Sprite>("Walls/wall_atlas");
                }

                if (slices != null)
                    foreach (var s in slices)
                        _wallAtlasCache[s.name] = s;

                if (_wallAtlasCache.Count == 0)
                    Debug.LogWarning("[TileAtlas] Wall atlas not found at Resources/Walls/wall_atlas" +
                        " — reimport the PNG in Unity or check the Resources folder path." +
                        " Walls will use the procedural flat-block fallback.");
                else
                    Debug.Log($"[TileAtlas] Wall atlas loaded: {_wallAtlasCache.Count} sprites.");
            }
            string key = "wall_" + shapeId + "_" + state;
            if (_wallAtlasCache.TryGetValue(key, out var sprite)) return sprite;
            // Fallback: try normal state of the same shape
            string normalKey = "wall_" + shapeId + "_normal";
            if (_wallAtlasCache.TryGetValue(normalKey, out var normFallback)) return normFallback;
            // Last resort: solid EW straight
            if (_wallAtlasCache.TryGetValue("wall_straight_ew_normal", out var def)) return def;
            return GetWall(0); // procedural fallback if atlas not yet loaded
        }

        // ── Base wall tile (wall_base.png — normal / damaged / destroyed) ────
        private static Dictionary<string, Sprite> _wallBaseCache;

        public static Sprite GetWallBase(string state = "normal")
        {
            if (_wallBaseCache == null)
            {
                _wallBaseCache = new Dictionary<string, Sprite>();
                Sprite[] slices = Resources.LoadAll<Sprite>("Walls/wall_base");
                if (slices != null)
                    foreach (var s in slices) _wallBaseCache[s.name] = s;

                if (_wallBaseCache.Count == 0)
                    Debug.LogWarning("[TileAtlas] wall_base not found at Resources/Walls/wall_base" +
                        " — reimport the PNG or check the Resources path. Using procedural fallback.");
            }
            if (_wallBaseCache.TryGetValue("wall_base_" + state, out var sprite)) return sprite;
            if (_wallBaseCache.TryGetValue("wall_base_normal",   out var norm))   return norm;
            return GetWall(0);
        }

        // ── Wall overlay strips (wall_overlays.png — 9 face combos) ──────────
        // IDs: ov_n · ov_s · ov_e · ov_w · ov_ne · ov_nw · ov_se · ov_sw · ov_cross
        private static Dictionary<string, Sprite> _wallOverlayCache;

        public static Sprite GetWallOverlay(string id)
        {
            if (_wallOverlayCache == null)
            {
                _wallOverlayCache = new Dictionary<string, Sprite>();
                Sprite[] slices = Resources.LoadAll<Sprite>("Walls/wall_overlays");
                if (slices != null)
                    foreach (var s in slices) _wallOverlayCache[s.name] = s;

                if (_wallOverlayCache.Count == 0)
                    Debug.LogWarning("[TileAtlas] wall_overlays not found at Resources/Walls/wall_overlays" +
                        " — reimport the PNG or check the Resources path.");
            }
            _wallOverlayCache.TryGetValue(id, out var sprite);
            return sprite; // null = caller skips
        }

        private static void EnsureCache()
        {
            if (_cache != null) return;
            _cache = new Sprite[10];
            for (int i = 0; i < 5; i++) _cache[i]     = MakeFloor(i);
            for (int i = 0; i < 5; i++) _cache[5 + i] = MakeWall(i);
        }

        // ── HTML-spec wall/door shared palette ──────────────────────────────────
        // ── HTML wall palette (exact match to SpaceStationWall1.html) ───────────
        // Top surface
        static readonly Color32 WTBase  = C("#2d3040");  // tBase
        static readonly Color32 WTLit   = C("#363a4e");  // tLit
        static readonly Color32 WTDeep  = C("#222530");  // tDeep
        static readonly Color32 WTHi    = C("#424858");  // tHi
        static readonly Color32 WTLo    = C("#1c1f2c");  // tLo
        static readonly Color32 WTGrout = C("#1a1d28");  // tGrout
        // Perspective face
        static readonly Color32 WFBase   = C("#383c50"); // fBase
        static readonly Color32 WFLit    = C("#434860"); // fLit
        static readonly Color32 WFDark   = C("#252838"); // fDark
        static readonly Color32 WFShadow = C("#141618"); // fShadow
        static readonly Color32 WFBevel  = C("#4e5470"); // fBevel
        static readonly Color32 WFGrout  = C("#161920"); // fGrout
        // Rivets
        static readonly Color32 WRHi = C("#565e7a");
        static readonly Color32 WRLo = C("#2c3048");
        // Accent LEDs
        static readonly Color32 WAcc  = C("#4880aa");
        static readonly Color32 WAccD = C("#224460");
        static readonly Color32 WAccG = C("#111e30");
        // Variant detail palette — V2–V5
        static readonly Color32 WVScr   = C("#272b3a");
        static readonly Color32 WVScrL  = C("#333748");
        static readonly Color32 WVDeep  = C("#222530");
        static readonly Color32 WVHi    = C("#424858");
        static readonly Color32 WVLo    = C("#1c1f2c");
        static readonly Color32 WVGrout = C("#1a1d28");
        static readonly Color32 WVAcc   = C("#4880aa");
        static readonly Color32 WVGreen = C("#3a6030");
        static readonly Color32 WVRivet = C("#565e7a");

        // Wall direction constants (outer face direction = which world edge has the outer top surface)
        // Usage guide (7×7 room, Y-up coordinates):
        //   row=0 south boundary  → WALL_DIR_S   (outer face south)
        //   row=6 north boundary  → WALL_DIR_N   (outer face north)
        //   col=0 west  boundary  → WALL_DIR_W   (outer face west)
        //   col=6 east  boundary  → WALL_DIR_E   (outer face east)
        public const int WALL_DIR_S     = 0;
        public const int WALL_DIR_N     = 1;
        public const int WALL_DIR_W     = 2;
        public const int WALL_DIR_E     = 3;
        // Convex corner constants (room outer corners)
        public const int WALL_CORNER_SW = 4;  // col=0, row=0
        public const int WALL_CORNER_SE = 5;  // col=6, row=0
        public const int WALL_CORNER_NW = 6;  // col=0, row=6
        public const int WALL_CORNER_NE = 7;  // col=6, row=6

        private static Sprite[][] _wallDirCache;    // [dir 0..3][variant 0..4]
        private static Sprite[][] _wallCornerCache; // [corner 0..3][variant 0..4]

        public static Sprite GetWallDirectional(int dir, int variant = 0)
        {
            if (_wallDirCache == null)
            {
                _wallDirCache = new Sprite[4][];
                for (int d = 0; d < 4; d++)
                {
                    _wallDirCache[d] = new Sprite[5];
                    for (int v = 0; v < 5; v++) _wallDirCache[d][v] = MakeWallDir(d, v);
                }
            }
            return _wallDirCache[dir][Mathf.Clamp(variant, 0, 4)];
        }

        public static Sprite GetWallCorner(int corner, int variant = 0)
        {
            if (_wallCornerCache == null)
            {
                _wallCornerCache = new Sprite[4][];
                for (int ci = 0; ci < 4; ci++)
                {
                    _wallCornerCache[ci] = new Sprite[5];
                    for (int v = 0; v < 5; v++)
                        _wallCornerCache[ci][v] = MakeWallConvex(WALL_CORNER_SW + ci, v);
                }
            }
            return _wallCornerCache[corner - WALL_CORNER_SW][Mathf.Clamp(variant, 0, 4)];
        }

        // ── HTML wall rendering helpers ────────────────────────────────────────
        // Alpha-composite src over dst pixel (source-over).
        static void BlendOver(Color32[] p, int x, int y, Color32 src)
        {
            if ((uint)x >= 64 || (uint)y >= 64) return;
            int     idx = (63 - y) * 64 + x;
            Color32 d   = p[idx];
            float   fa  = src.a / 255f;
            p[idx] = new Color32(
                (byte)(d.r + (src.r - d.r) * fa),
                (byte)(d.g + (src.g - d.g) * fa),
                (byte)(d.b + (src.b - d.b) * fa),
                (byte)Mathf.Min(255, d.a + Mathf.RoundToInt(src.a * (1f - d.a / 255f))));
        }

        // Lerp between two hex colours, returning a Color32.
        static Color32 LerpC32(Color32 a, Color32 b, float t) => new Color32(
            (byte)(a.r + (b.r - a.r) * t),
            (byte)(a.g + (b.g - a.g) * t),
            (byte)(a.b + (b.b - a.b) * t), 255);

        // Deterministic noise overlay (matches HTML noise function).
        static void WNoise(Color32[] p, int x, int y, int w, int h)
        {
            for (int iy = y; iy < y + h; iy += 2)
            for (int ix = x; ix < x + w; ix += 3)
            {
                if ((uint)ix >= 64 || (uint)iy >= 64) continue;
                float v = ((ix * 7 + iy * 13) % 17) / 17f;
                if      (v < 0.13f) BlendOver(p, ix,     iy, new Color32(0,   0,   0,   (byte)(v       * 0.24f * 255f)));
                else if (v > 0.87f) BlendOver(p, ix + 1, iy, new Color32(255, 255, 255, (byte)((1f-v) * 0.15f * 255f)));
            }
        }

        // ── HTML topSurface W×H — exact port of JS topSurface().
        // Single centered recessed panel with uniform 4px grout border — reads
        // identically at 0/90/180/270° (rotationally symmetric).
        static void WTopSurface(Color32[] p, int ox, int oy, int W, int H)
        {
            // Base fill
            Fr(p, ox, oy, W, H, WTBase);
            // Outer bevel — 1px (tHi top+left, tLo bottom+right)
            Fr(p, ox,     oy,     W, 1, WTHi); // top
            Fr(p, ox,     oy,     1, H, WTHi); // left
            Fr(p, ox,     oy+H-1, W, 1, WTLo); // bottom
            Fr(p, ox+W-1, oy,     1, H, WTLo); // right
            // Grout channel — 4px inset, 2px wide, all four sides
            const int g = 4;
            Fr(p, ox+g,     oy+g,     W-g*2, 1, WTGrout); // top row 1
            Fr(p, ox+g,     oy+g+1,   W-g*2, 1, WTGrout); // top row 2
            Fr(p, ox+g,     oy+H-1-g, W-g*2, 1, WTGrout); // bottom row 1
            Fr(p, ox+g,     oy+H-2-g, W-g*2, 1, WTGrout); // bottom row 2
            Fr(p, ox+g,     oy+g,     1, H-g*2, WTGrout); // left col 1
            Fr(p, ox+g+1,   oy+g,     1, H-g*2, WTGrout); // left col 2
            Fr(p, ox+W-1-g, oy+g,     1, H-g*2, WTGrout); // right col 1
            Fr(p, ox+W-2-g, oy+g,     1, H-g*2, WTGrout); // right col 2
            // Inner recessed panel — inset pp=g+2=6 from each edge
            const int pp = g + 2;
            Fr(p, ox+pp,     oy+pp,     W-pp*2, H-pp*2, WTDeep);
            Fr(p, ox+pp,     oy+pp,     W-pp*2, 1,      WTHi); // top bevel
            Fr(p, ox+pp,     oy+pp,     1,      H-pp*2, WTHi); // left bevel
            Fr(p, ox+pp,     oy+H-1-pp, W-pp*2, 1,      WTLo); // bottom bevel
            Fr(p, ox+W-1-pp, oy+pp,     1,      H-pp*2, WTLo); // right bevel
            // Four corner rivets at grout corners (2×2 each)
            foreach (var (cx, cy) in new[]{
                (ox+g-1, oy+g-1), (ox+W-g, oy+g-1),
                (ox+g-1, oy+H-g), (ox+W-g, oy+H-g)
            })
            {
                Fr(p, cx, cy,   2, 1, WRHi);
                Fr(p, cx, cy+1, 2, 1, WRLo);
            }
            // Accent LEDs — one per edge midpoint, just inside grout channel
            int mx = ox + W / 2;
            int my = oy + H / 2;
            Px(p, mx,     oy+g-1, WAcc); // top edge
            Px(p, mx,     oy+H-g, WAcc); // bottom edge
            Px(p, ox+g-1, my,     WAcc); // left edge
            Px(p, ox+W-g, my,     WAcc); // right edge
        }

        // ── HTML southFace — perspective face at the bottom of a H-wall tile.
        // fx/fy = top-left origin of the face region; width always 64, height FH=18.
        static void WSouthFace(Color32[] p, int ox, int fy)
        {
            const int FH = 18;
            // Row 0: full-width bevel highlight
            Fr(p, ox, fy, 64, 1, WFBevel);
            // Rows 1..FH-1: gradient fBase → fShadow
            for (int r = 1; r < FH; r++)
                Fr(p, ox, fy + r, 64, 1, LerpC32(WFBase, WFShadow, r / (float)(FH - 1)));
            // Grout seam at x=31
            int gx = ox + 31;
            Fr(p, gx, fy + 1, 2, FH - 1, WFGrout);
            // Left inset panel
            Fr(p, ox+1, fy+2, gx-ox-2, FH-5, WFLit);
            Fr(p, ox+1, fy+2, 1,       FH-5, WFBevel);
            Fr(p, ox+1, fy+2, gx-ox-2, 1,    WFBevel);
            Fr(p, ox+1, fy+FH-4, gx-ox-2, 1, WFDark);
            // Right inset panel
            Fr(p, gx+2, fy+2, ox+62-(gx+2), FH-5, WFLit);
            Fr(p, gx+2, fy+2, 1,             FH-5, WFBevel);
            Fr(p, gx+2, fy+2, ox+62-(gx+2), 1,    WFBevel);
            Fr(p, gx+2, fy+FH-4, ox+62-(gx+2), 1, WFDark);
            // Rivets
            Fr(p, ox+2, fy+3, 2, 1, WRHi); Fr(p, ox+2, fy+4, 2, 1, WRLo);
            Fr(p, ox+60, fy+3, 2, 1, WRHi); Fr(p, ox+60, fy+4, 2, 1, WRLo);
            // Accent LED strip + two bright LEDs
            Fr(p, ox+5, fy+1, 54, 1, WAccG);
            Px(p, ox+14, fy+1, WAcc); Px(p, ox+15, fy+1, WAcc);
            Px(p, ox+48, fy+1, WAcc); Px(p, ox+49, fy+1, WAcc);
            // Shadow/grout at bottom
            Fr(p, ox, fy+FH-2, 64, 1, WFShadow);
            Fr(p, ox, fy+FH-1, 64, 1, WTGrout);
        }

        // ── HTML eastFace — perspective face at the right of a V-wall tile.
        // fx/oy = top-left origin of the face region; height always 64, width FW=18.
        static void WEastFace(Color32[] p, int fx, int oy)
        {
            const int FW = 18;
            // Col 0: full-height bevel highlight
            Fr(p, fx, oy, 1, 64, WFBevel);
            // Cols 1..FW-1: gradient fBase → fShadow
            for (int c = 1; c < FW; c++)
                for (int y = oy; y < oy + 64; y++)
                    Px(p, fx + c, y, LerpC32(WFBase, WFShadow, c / (float)(FW - 1)));
            // Grout seam at y=31
            int gy = oy + 31;
            Fr(p, fx+1, gy, FW-1, 2, WFGrout);
            // Top inset panel
            Fr(p, fx+2, oy+1, FW-5, gy-oy-2, WFLit);
            Fr(p, fx+2, oy+1, FW-5, 1,       WFBevel);
            Fr(p, fx+2, oy+1, 1,    gy-oy-2, WFBevel);
            Fr(p, fx+FW-4, oy+1, 1, gy-oy-2, WFDark);
            // Bottom inset panel
            Fr(p, fx+2, gy+2, FW-5, oy+62-(gy+2), WFLit);
            Fr(p, fx+2, gy+2, FW-5, 1,             WFBevel);
            Fr(p, fx+2, gy+2, 1,    oy+62-(gy+2), WFBevel);
            Fr(p, fx+FW-4, gy+2, 1, oy+62-(gy+2), WFDark);
            // Rivets
            Fr(p, fx+3, oy+2,  2, 1, WRHi); Fr(p, fx+3, oy+3,  2, 1, WRLo);
            Fr(p, fx+3, oy+60, 2, 1, WRHi); Fr(p, fx+3, oy+61, 2, 1, WRLo);
            // Accent LED strip + two bright LEDs
            for (int y = oy+5; y <= oy+58; y++) Px(p, fx+1, y, WAccG);
            Px(p, fx+1, oy+14, WAcc); Px(p, fx+1, oy+15, WAcc);
            Px(p, fx+1, oy+48, WAcc); Px(p, fx+1, oy+49, WAcc);
            // Shadow/grout at right edge
            for (int y = oy; y < oy+64; y++) Px(p, fx+FW-2, y, WFShadow);
            for (int y = oy; y < oy+64; y++) Px(p, fx+FW-1, y, WTGrout);
        }

        private const int WT = 18; // wall face thickness — matches HTML FH/FW=18

        // ── Door rendering helpers (still used by MakeDoorS / MakeDoorE) ─────
        // Bevelled top-face strip (legacy: used only by door tiles).
        static readonly Color32 WTop      = C("#4a5060");
        static readonly Color32 WTopOuter = C("#2e343e");
        static readonly Color32 WTopInner = C("#5a6070");

        static void WDrawTop(Color32[] p, int x, int y, int w, int h, char outer)
        {
            Fr(p, x, y, w, h, WTop);
            WNoise(p, x, y, w, h);
            switch (outer)
            {
                case 't': Fr(p, x, y,       w, 1, WTopOuter); Fr(p, x, y+h-1,   w, 1, WTopInner); break;
                case 'b': Fr(p, x, y+h-1,   w, 1, WTopOuter); Fr(p, x, y,       w, 1, WTopInner); break;
                case 'l': Fr(p, x, y,       1, h, WTopOuter); Fr(p, x+w-1, y,   1, h, WTopInner); break;
                case 'r': Fr(p, x+w-1, y,   1, h, WTopOuter); Fr(p, x, y,       1, h, WTopInner); break;
            }
        }

        // Horizontal form lines at 34% and 67% (legacy: used only by door panels).
        static void WFormH(Color32[] p, int x, int y, int w, int h)
        {
            foreach (float t in new[] { 0.34f, 0.67f })
            {
                int ly = y + (int)(h * t);
                for (int px = x; px < x + w; px++)
                {
                    BlendOver(p, px, ly,     new Color32(0,   0,   0,   35));
                    BlendOver(p, px, ly + 1, new Color32(255, 255, 255, 13));
                }
            }
        }

        // Transform a top-surface-N coordinate (x, y) to the actual tile position for
        // a given wall direction, then write color c.  All variant detail calls route
        // through here so details rotate with the underlying panel layout.
        // Transform source coords (in N-orientation 64×64 space) to tile pixel position.
        //   N: top surface y=0..45  → identity
        //   S: top surface y=18..63 → shift down by WT
        //   W: top surface x=0..45  → 90° CCW:  (x,y) → (y, 63-x)
        //   E: top surface x=18..63 → 90° CW:   (x,y) → (63-y, x)
        static void PlotVariant(Color32[] p, int x, int y, Color32 c, int dir)
        {
            int ox, oy;
            switch (dir)
            {
                case WALL_DIR_N: ox = x;      oy = y;          break;
                case WALL_DIR_S: ox = x;      oy = y + WT;     break;
                case WALL_DIR_W: ox = y;      oy = 63 - x;     break; // 90° CCW
                case WALL_DIR_E: ox = 63 - y; oy = x;          break; // 90° CW
                default:         ox = x;      oy = y;           break;
            }
            if ((uint)ox >= 64 || (uint)oy >= 64) return;
            Px(p, ox, oy, c);
        }

        // Apply per-variant surface detail overlay (HTML wall spec V2–V5).
        // dir rotates detail coords to match the tile's top-surface region.
        // Default dir=0 (WALL_DIR_N) keeps coords as-is; used for corner/flat tiles.
        static void WApplyVariantDetail(Color32[] p, int v, int dir = 0)
        {
            switch (v)
            {
                case 1: // V2 — diagonal scuffs on left panel
                    for (int i = 0; i < 7; i++)
                    {
                        PlotVariant(p, 8+i, 22+i, WVScr,  dir);
                        PlotVariant(p, 9+i, 22+i, WVScrL, dir);
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        PlotVariant(p, 11+i, 32+i, WVScr,  dir);
                        PlotVariant(p, 12+i, 32+i, WVScrL, dir);
                    }
                    break;
                case 2: // V3 — recessed access hatch on right panel
                    for (int dy = 0; dy < 8; dy++)
                        for (int dx = 0; dx < 10; dx++)
                            PlotVariant(p, 38+dx, 26+dy, WVDeep, dir);
                    for (int dx = 0; dx < 10; dx++) PlotVariant(p, 38+dx, 26,    WVHi,    dir); // top bevel
                    for (int dy = 0; dy < 8;  dy++) PlotVariant(p, 38,    26+dy, WVHi,    dir); // left bevel
                    for (int dx = 0; dx < 10; dx++) PlotVariant(p, 38+dx, 33,    WVLo,    dir); // bottom bevel
                    for (int dy = 0; dy < 8;  dy++) PlotVariant(p, 47,    26+dy, WVLo,    dir); // right bevel
                    for (int dx = 0; dx < 6;  dx++) PlotVariant(p, 40+dx, 28,    WVGrout, dir); // seam A
                    for (int dx = 0; dx < 6;  dx++) PlotVariant(p, 40+dx, 31,    WVGrout, dir); // seam B
                    PlotVariant(p, 39, 27, WVRivet, dir);
                    PlotVariant(p, 46, 27, WVRivet, dir);
                    break;
                case 3: // V4 — corner wear triangle + horizontal wear streaks
                    for (int wy = 0; wy < 5; wy++)
                        for (int wx = 0; wx < 5 - wy; wx++)
                            PlotVariant(p, wx, wy, WVDeep, dir);
                    for (int dx = 0; dx < 20; dx++) PlotVariant(p,  5+dx, 38, WVScr, dir);
                    for (int dx = 0; dx < 14; dx++) PlotVariant(p,  5+dx, 39, WVScr, dir);
                    for (int dx = 0; dx < 23; dx++) PlotVariant(p, 36+dx, 28, WVScr, dir);
                    for (int dx = 0; dx < 17; dx++) PlotVariant(p, 42+dx, 29, WVScr, dir);
                    break;
                case 4: // V5 — indicator lights + vent slots on right panel
                    PlotVariant(p, 8, 10, WVAcc,   dir);
                    PlotVariant(p, 8, 13, WVGreen, dir);
                    PlotVariant(p, 8, 16, WVLo,    dir);
                    for (int s = 0; s < 3; s++)
                    {
                        for (int dx = 0; dx < 19; dx++) PlotVariant(p, 37+dx, 24+s*7, WVGrout, dir);
                        for (int dx = 0; dx < 19; dx++) PlotVariant(p, 37+dx, 25+s*7, WVScrL,  dir);
                    }
                    break;
                // case 0: V1 clean — no overlay
            }
        }

        // Directional straight wall tile (5 variants, v=0..4).
        //   WALL_DIR_N  → top surface (64×46) at y=0..45,  southFace at y=46..63  (floor south/below)
        //   WALL_DIR_S  → southFace at y=0..17,            top surface (64×46) at y=18..63 (floor north/above)
        //   WALL_DIR_W  → top surface (46×64) at x=0..45,  eastFace at x=46..63  (floor east/right)
        //   WALL_DIR_E  → eastFace at x=0..17,             top surface (46×64) at x=18..63 (floor west/left)
        static Sprite MakeWallDir(int dir, int v)
        {
            var p = NewPixels();
            switch (dir)
            {
                case WALL_DIR_N: // floor south (below) — face at bottom pointing toward floor
                    WTopSurface(p, 0, 0,      64, 64 - WT);
                    WSouthFace (p, 0,          64 - WT);
                    break;
                case WALL_DIR_S: // floor north (above) — face at top pointing toward floor
                    WSouthFace (p, 0, 0);
                    WTopSurface(p, 0, WT,     64, 64 - WT);
                    break;
                case WALL_DIR_W: // floor east (right) — face at right pointing toward floor
                    WTopSurface(p, 0, 0,  64 - WT, 64);
                    WEastFace  (p,    64 - WT, 0);
                    break;
                case WALL_DIR_E: // floor west (left) — face at left pointing toward floor
                    WEastFace  (p, 0, 0);
                    WTopSurface(p, WT, 0, 64 - WT, 64);
                    break;
            }
            WApplyVariantDetail(p, v, dir);
            return MakeSprite(p);
        }

        // Convex corner wall tile — full-tile top surface (no perspective face visible).
        static Sprite MakeWallConvex(int corner, int v = 0)
        {
            var p = NewPixels();
            WTopSurface(p, 0, 0, 64, 64);
            WApplyVariantDetail(p, v);
            return MakeSprite(p);
        }

        // Legacy flat wall tile — used by GetWall() for interior placed walls that
        // don't yet have a directional classification.  Now uses the same top-surface
        // rendering as the directional tiles for visual consistency.
        static Sprite MakeWall(int variant)
        {
            var p = NewPixels();
            WTopSurface(p, 0, 0, 64, 64);
            WApplyVariantDetail(p, variant);
            return MakeSprite(p);
        }

        // ── Cabinet palette (CabinetTileSheet.html — chestVFront 'closed' design) ────
        // 3/4 perspective view: lid at top of tile (far edge), front panel at bottom.
        static readonly Color32 ChBodyBase   = C("#3a4152");
        static readonly Color32 ChBodyDk     = C("#292e3a");
        static readonly Color32 ChBodyLt     = C("#4e5768");
        static readonly Color32 ChEdgeDk     = C("#22262f");
        static readonly Color32 ChEdgeLt     = C("#5a6275");
        static readonly Color32 ChLidBase    = C("#424a5a");
        static readonly Color32 ChLidTop     = C("#515b6d");
        static readonly Color32 ChLidSeam    = C("#2a2f3b");
        static readonly Color32 ChPanelIn    = C("#2e3340");
        static readonly Color32 ChPanelBrd   = C("#4a5263");
        static readonly Color32 ChPipDk      = C("#1a4a6a");
        static readonly Color32 ChPipGlow    = C("#3ab8e0");
        static readonly Color32 ChPipOff     = C("#1e2a38");
        static readonly Color32 ChClaspBase  = C("#2e3340");
        static readonly Color32 ChClaspLt    = C("#4a5263");
        static readonly Color32 ChClaspGlow  = C("#2a7a9a");
        static readonly Color32 ChBoltLo     = C("#22262f");
        static readonly Color32 ChBoltHi     = C("#5a6275");

        // ── Cabinet drawing helpers ───────────────────────────────────────────
        // Two distinct tile shapes replace pixel-rotation:
        //   V (vertical / portrait)  — rotation 0 or 180 — lid at top, tall body.
        //   H (horizontal / landscape) — rotation 90 or 270 — lid on left, wide body.
        // fillLevel 0-3 drives the 3 capacity LEDs: 0 = all off, 3 = all on.

        static Sprite MakeCabinet(bool horizontal, int fillLevel)
            => MakeSprite(horizontal ? DrawCabinetH(fillLevel) : DrawCabinetV(fillLevel));

        // ── V variant (portrait, lid at top) ─────────────────────────────────
        static Color32[] DrawCabinetV(int fillLevel)
        {
            var p = NewPixels();

            const int CX = 16, CY = 4, CW = 32, CH = 54, LH = 9;
            const int BY = CY + LH;    // body top    = 13
            const int BH = CH - LH;    // body height = 45

            // Drop shadow
            Fr(p, CX + 3, CY + CH + 1, CW - 2, 1, new Color32(0, 0, 0, 71));
            Fr(p, CX + 5, CY + CH + 3, CW - 6, 1, new Color32(0, 0, 0, 26));

            // Body
            Fr(p, CX,          BY,          CW,     BH,    ChBodyBase);
            Fr(p, CX + 1,      BY,          CW - 2,  1,    ChBodyLt);
            Fr(p, CX,          BY + BH - 5, CW,      5,    ChBodyDk);
            Fr(p, CX + 1,      BY + BH - 5, CW - 2,  1,    ChEdgeLt);
            Fr(p, CX,          BY,          1,       BH,    ChEdgeDk);
            Fr(p, CX + 1,      BY,          1,       BH - 1, ChBodyLt);
            Fr(p, CX + CW - 1, BY,          1,       BH,    ChEdgeDk);
            Fr(p, CX + CW - 2, BY,          1,       BH - 1, ChBodyDk);
            Fr(p, CX,          BY + BH - 1, CW,       1,    ChEdgeDk);

            // Panel
            int px0 = CX + 4, py0 = BY + 6, pw = CW - 8, ph = BH - 16;
            Fr(p, px0,          py0,         pw,  ph,  ChPanelIn);
            Fr(p, px0,          py0,         pw,   1,  ChEdgeDk);
            Fr(p, px0,          py0,          1,  ph,  ChEdgeDk);
            Fr(p, px0 + pw - 1, py0,          1,  ph,  ChPanelBrd);
            Fr(p, px0,          py0 + ph - 1, pw,  1,  ChPanelBrd);

            // 3 vertical capacity LEDs — bottom pip = level 1, top pip = level 3
            int pipX    = px0 + pw / 2 - 1;
            int[] pipYs = { py0 + ph - 8, py0 + ph / 2 - 1, py0 + 5 };
            for (int i = 0; i < 3; i++)
            {
                Fr(p, pipX, pipYs[i], 2, 3, ChPipDk);
                Px(p, pipX, pipYs[i] + 1, i < fillLevel ? ChPipGlow : ChPipOff);
            }

            // Lid
            Fr(p, CX,          CY,          CW,  LH,  ChLidBase);
            Fr(p, CX + 1,      CY,          CW - 2, 1, ChLidTop);
            Fr(p, CX,          CY,          1,   LH,  ChEdgeDk);
            Fr(p, CX + CW - 1, CY,          1,   LH,  ChEdgeDk);
            Fr(p, CX,          CY,          CW,   1,  ChEdgeDk);
            Fr(p, CX,          CY + LH - 1, CW,   1,  ChLidSeam);
            Fr(p, CX,          CY + LH,     CW,   1,  ChEdgeLt);

            // Clasp on lid
            int clx = CX + CW / 2 - 3, cly = CY + 2;
            Fr(p, clx,     cly,     7, 4, ChClaspBase);
            Fr(p, clx + 1, cly,     5, 1, ChClaspLt);
            Fr(p, clx + 1, cly,     1, 4, ChClaspLt);
            Px(p, clx + 3, cly + 1, ChClaspGlow);
            Px(p, clx + 3, cly + 2, ChClaspGlow);

            // Corner bolts
            foreach (var (bx, by2) in new[] {
                (CX + 2, BY + 2), (CX + CW - 4, BY + 2),
                (CX + 2, BY + BH - 4), (CX + CW - 4, BY + BH - 4) })
            {
                Fr(p, bx, by2, 2, 2, ChBoltLo);
                Px(p, bx, by2, ChBoltHi);
            }
            return p;
        }

        // ── H variant (landscape, lid on left) ───────────────────────────────
        static Color32[] DrawCabinetH(int fillLevel)
        {
            var p = NewPixels();

            // Footprint: 54 wide × 32 tall, centred in 64×64 tile.
            const int CX = 4, CY = 16, CW = 54, CH = 32, LW = 9;
            const int BX = CX + LW;   // body left  = 13
            const int BW = CW - LW;   // body width = 45

            // Drop shadow
            Fr(p, CX + 3, CY + CH,     CW - 4, 1, new Color32(0, 0, 0, 71));
            Fr(p, CX + 5, CY + CH + 2, CW - 8, 1, new Color32(0, 0, 0, 26));

            // Body
            Fr(p, BX,          CY,          BW,  CH,  ChBodyBase);
            Fr(p, BX + 1,      CY,          BW - 2, 1, ChBodyLt);
            Fr(p, BX,          CY + CH - 5, BW,   5,  ChBodyDk);
            Fr(p, BX + 1,      CY + CH - 5, BW - 2, 1, ChEdgeLt);
            Fr(p, BX,          CY,          1,   CH,  ChEdgeDk);
            Fr(p, BX + BW - 1, CY,          1,   CH,  ChEdgeDk);
            Fr(p, BX + BW - 2, CY,          1,   CH - 1, ChBodyDk);
            Fr(p, BX,          CY + CH - 1, BW,   1,  ChEdgeDk);

            // Panel
            int px0 = BX + 4, py0 = CY + 4, pw = BW - 8, ph = CH - 10;
            Fr(p, px0,          py0,         pw,  ph,  ChPanelIn);
            Fr(p, px0,          py0,         pw,   1,  ChEdgeDk);
            Fr(p, px0,          py0,          1,  ph,  ChEdgeDk);
            Fr(p, px0 + pw - 1, py0,          1,  ph,  ChPanelBrd);
            Fr(p, px0,          py0 + ph - 1, pw,  1,  ChPanelBrd);

            // 3 horizontal capacity LEDs — left pip = level 1, right pip = level 3
            int pipY    = py0 + ph / 2 - 1;
            int[] pipXs = { px0 + pw / 4 - 1, px0 + pw / 2 - 1, px0 + 3 * pw / 4 - 1 };
            for (int i = 0; i < 3; i++)
            {
                Fr(p, pipXs[i], pipY, 3, 2, ChPipDk);
                Px(p, pipXs[i] + 1, pipY, i < fillLevel ? ChPipGlow : ChPipOff);
            }

            // Lid (left side)
            Fr(p, CX,        CY,          LW,  CH,  ChLidBase);
            Fr(p, CX + 1,    CY,          LW - 2, 1, ChLidTop);
            Fr(p, CX,        CY,          1,   CH,  ChEdgeDk);
            Fr(p, CX,        CY,          LW,   1,  ChEdgeDk);
            Fr(p, CX,        CY + CH - 1, LW,   1,  ChEdgeDk);
            Fr(p, CX + LW - 1, CY,        1,   CH,  ChLidSeam);
            Fr(p, CX + LW,     CY,        1,   CH,  ChEdgeLt);

            // Clasp centred on lid left face
            int clx = CX + 1, cly = CY + CH / 2 - 3;
            Fr(p, clx,     cly,     4, 7, ChClaspBase);
            Fr(p, clx,     cly,     4, 1, ChClaspLt);
            Fr(p, clx,     cly,     1, 7, ChClaspLt);
            Px(p, clx + 1, cly + 3, ChClaspGlow);
            Px(p, clx + 2, cly + 3, ChClaspGlow);

            // Corner bolts on body
            foreach (var (bx, by2) in new[] {
                (BX + 2, CY + 2), (BX + BW - 4, CY + 2),
                (BX + 2, CY + CH - 4), (BX + BW - 4, CY + CH - 4) })
            {
                Fr(p, bx, by2, 2, 2, ChBoltLo);
                Px(p, bx, by2, ChBoltHi);
            }
            return p;
        }

        // Shadow overlay sprites — three families matching the HTML spec (DEPTH=18, ALPHA=0.30).
        //
        // Edge (0-3):          full-width linear gradient from named wall edge inward.
        // Inside corner (4-7): radial gradient from wall-corner point, radius = DEPTH*1.8.
        //                      Applied to a floor tile whose two adjacent cardinals are walls.
        //                      Rounds and deepens the concave corner where edges stack.
        // Outside corner (8-11): gentle radial, radius = DEPTH*1.5, peak = ALPHA*0.6.
        //                      Applied to a floor tile diagonally adjacent to an isolated
        //                      wall corner (both adjacent cardinals are floor).
        static Sprite MakeShadow(int edge)
        {
            var p = NewPixels(); // fully transparent
            const int   DEPTH = 18;
            const float ALPHA = 0.30f;

            if (edge < 4)
            {
                // ── Cardinal edge — linear gradient, full tile width ──────────
                for (int i = 0; i < DEPTH; i++)
                {
                    float   t  = 1f - (float)i / DEPTH;
                    byte    ab = (byte)(ALPHA * t * 255f);
                    Color32 c  = new Color32(0, 0, 0, ab);
                    switch (edge)
                    {
                        case SHADOW_TOP:    Fr(p, 0, i,      64, 1, c); break;
                        case SHADOW_BOTTOM: Fr(p, 0, 63 - i, 64, 1, c); break;
                        case SHADOW_RIGHT:  for (int y = 0; y < 64; y++) Px(p, 63 - i, y, c); break;
                        case SHADOW_LEFT:   for (int y = 0; y < 64; y++) Px(p, i,      y, c); break;
                    }
                }
            }
            else if (edge < 8)
            {
                // ── Inside corner — radial from wall-corner, radius DEPTH*1.8 ─
                // Canvas: y=0 = north (top), y=63 = south (bottom).
                // Corner coords map as: TL=(0,0), TR=(63,0), BL=(0,63), BR=(63,63).
                float R  = DEPTH * 1.8f;
                int   cx = (edge == SHADOW_IN_TL || edge == SHADOW_IN_BL) ? 0 : 63;
                int   cy = (edge == SHADOW_IN_TL || edge == SHADOW_IN_TR) ? 0 : 63;
                for (int x = 0; x < 64; x++)
                for (int y = 0; y < 64; y++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float t    = 1f - dist / R;
                    if (t <= 0f) continue;
                    byte ab = (byte)(ALPHA * t * 255f);
                    Px(p, x, y, new Color32(0, 0, 0, ab));
                }
            }
            else
            {
                // ── Outside corner — gentle radial, radius DEPTH*1.5, peak ALPHA*0.6 ─
                float R      = DEPTH * 1.5f;
                float oAlpha = ALPHA * 0.6f;
                int   cx = (edge == SHADOW_OUT_TL || edge == SHADOW_OUT_BL) ? 0 : 63;
                int   cy = (edge == SHADOW_OUT_TL || edge == SHADOW_OUT_TR) ? 0 : 63;
                for (int x = 0; x < 64; x++)
                for (int y = 0; y < 64; y++)
                {
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float t    = 1f - dist / R;
                    if (t <= 0f) continue;
                    byte ab = (byte)(oAlpha * t * 255f);
                    Px(p, x, y, new Color32(0, 0, 0, ab));
                }
            }
            return MakeSprite(p);
        }

        // ── Floor palette ─────────────────────────────────────────────────────
        static readonly Color32 FSeam    = C("#141a27");
        static readonly Color32 FPanel   = C("#1f2739");
        static readonly Color32 FBorder  = C("#192130");
        static readonly Color32 FScuff   = C("#17202e");
        static readonly Color32 FScuffLt = C("#26303f");

        // ── Floor scuff data ──────────────────────────────────────────────────
        // Last pixel of each mark is scuffLt (lighter tip), rest are scuff (dark body).
        private static readonly (int x, int y, char dir, int len)[][] FloorScuffSets =
        {
            new (int, int, char, int)[0],                                              // F0 — clean
            new[] { (14, 28, 'h', 5), (40, 42, 'h', 4) },                             // F1
            new[] { (38, 16, 'd', 5), (18, 44, 'h', 3) },                             // F2
            new[] { (9,  44, 'h', 5), (36, 22, 'v', 4), (50, 14, 'd', 3) },          // F3
            new[] { (46, 36, 'd', 4), (12, 18, 'h', 4), (28, 50, 'h', 3) },          // F4
        };

        static Sprite MakeFloor(int variant)
        {
            var p = NewPixels();
            // 1px seam border — adjacent tiles form a natural 2px grout line
            Fr(p, 0, 0, 64, 64, FSeam);
            Fr(p, 1, 1, 62, 62, FPanel);
            // Hollow inner border, 1px thick, 5px inset
            const int b = 5;
            Fr(p, b,      b,      54, 1,  FBorder); // top
            Fr(p, b,      62 - b, 54, 1,  FBorder); // bottom
            Fr(p, b,      b,      1,  54, FBorder); // left
            Fr(p, 62 - b, b,      1,  54, FBorder); // right
            foreach (var (sx, sy, dir, len) in FloorScuffSets[variant])
                for (int i = 0; i < len; i++)
                {
                    int cx = (dir == 'h' || dir == 'd') ? sx + i : sx;
                    int cy = (dir == 'v' || dir == 'd') ? sy + i : sy;
                    Px(p, cx, cy, i == len - 1 ? FScuffLt : FScuff);
                }
            return MakeSprite(p);
        }

        // ── HTML-spec door palette (exact match to SpaceStationDoors1.html) ──────
        // Frame / structure
        private static readonly Color32 DFrame      = C("#252838");
        private static readonly Color32 DFrameHi    = C("#363a50");
        private static readonly Color32 DFrameLo    = C("#181a24");
        private static readonly Color32 DPanel      = C("#2a2e40");
        private static readonly Color32 DPanelHi    = C("#3a3e54");
        private static readonly Color32 DPanelLo    = C("#1a1c28");
        private static readonly Color32 DPanelDeep  = C("#1e2030");
        private static readonly Color32 DSeam       = C("#0e0f16");
        private static readonly Color32 DTrack      = C("#161820");
        private static readonly Color32 DVoid       = C("#0c0d12");
        private static readonly Color32 DVoidLit    = C("#111420");
        // Legacy aliases kept so old callers still compile
        private static readonly Color32 DPillar     = C("#252838");
        private static readonly Color32 DPan        = C("#2a2e40");
        // Accent sensor LED — per status
        // Blue  = powered/unlocked   Yellow = locked   Red = unpowered
        private static Color32 DAcc(string status) => status switch
        {
            "locked"    => C("#b8920a"),
            "unpowered" => C("#aa3030"),
            _           => C("#4880aa"),  // powered
        };
        private static Color32 DAccD(string status) => status switch
        {
            "locked"    => C("#6a5006"),
            "unpowered" => C("#601818"),
            _           => C("#224460"),
        };
        private static Color32 DAccG(string status) => status switch
        {
            "locked"    => C("#221a02"),
            "unpowered" => C("#200e0e"),
            _           => C("#111e30"),
        };
        // Kept for any external callers
        private static Color32 DGlowDim(string status)    => DAccD(status);
        private static Color32 DGlowBright(string status) => DAcc(status);

        // ── Door atlas sprites ────────────────────────────────────────────────
        // Loaded via DoorAtlasData ScriptableObject at Resources/Doors/DoorAtlasData.
        // 12 sprites: door_ns_open0..door_ns_open9 (closed→open) + damaged + destroyed.
        // NS door: rotation 0°.  EW door: rotation 90° (handled by DoorRenderer).
        private static Dictionary<string, Sprite> _doorAtlasCache;
        private static bool                       _doorAtlasChecked;

        private static void EnsureDoorAtlas()
        {
            if (_doorAtlasChecked) return;
            _doorAtlasChecked = true;
            _doorAtlasCache = new Dictionary<string, Sprite>();

            // Load sprites via DoorAtlasData ScriptableObject (created by DoorAtlasSetup).
            var doorData = Resources.Load<DoorAtlasData>("Doors/DoorAtlasData");
            if (doorData == null)
            {
                Debug.Log("[TileAtlas] DoorAtlasData not found in Resources/Doors/ "
                    + "— run Tools → Door → Setup Door Atlas, then procedural sprites are used until then.");
                return;
            }

            for (int i = 0; i < doorData.openStages.Length; i++)
                if (doorData.openStages[i] != null)
                    _doorAtlasCache[$"door_ns_open{i}"] = doorData.openStages[i];

            if (doorData.damaged   != null) _doorAtlasCache["door_ns_damaged"]   = doorData.damaged;
            if (doorData.destroyed != null) _doorAtlasCache["door_ns_destroyed"] = doorData.destroyed;

            Debug.Log($"[TileAtlas] Door atlas loaded via DoorAtlasData ({_doorAtlasCache.Count} sprites).");
        }

        private static Dictionary<string, Sprite[]> _doorCache2;

        /// <summary>
        /// Get door animation frames (10 sprites: closed → fully open).
        ///
        /// The door_ns_atlas sprites (and MakeDoorE procedural fallback) are drawn for an NS wall
        /// (wall runs N↔S, passage E↔W, perspective faces on the left and right of the gap).
        ///
        /// The same sprites are used for EW walls — the caller rotates the door GO 90° via
        /// DoorOrientation.EW so the perspective faces land on the north/south sides instead.
        /// No second atlas or alternative sprite is needed.
        ///
        /// isH is accepted for API compatibility and future atlas expansion but does not change
        /// which sprites are returned.
        ///
        /// status:   "powered" | "locked" | "unpowered"
        /// dmgLevel: 0=normal (animated), 1=worn (stuck closed), 2=broken (jammed ~35% open).
        /// </summary>
        public static Sprite[] GetDoorFrames(bool isH, string status = "powered", int dmgLevel = 0)
        {
            if (_doorCache2 == null) _doorCache2 = new Dictionary<string, Sprite[]>();

            EnsureDoorAtlas();

            // Orientation is handled by Transform rotation on the GO, not by selecting different
            // sprites.  Cache key omits isH — same Sprite[] for both wall orientations.
            string key = $"door_{status}_{dmgLevel}";
            if (!_doorCache2.TryGetValue(key, out var frames))
            {
                // 10 frames: open0 (closed) → open9 (fully open), matching the 10-slot atlas.
                frames = new Sprite[10];
                if (dmgLevel == 1)
                {
                    // Damaged — door stuck closed; all frames show the same damaged sprite.
                    _doorAtlasCache.TryGetValue("door_ns_damaged", out var dmg);
                    var s = dmg ?? MakeDoorE(0f, status, 1);
                    for (int i = 0; i < 10; i++) frames[i] = s;
                }
                else if (dmgLevel == 2)
                {
                    // Destroyed — door jammed ~35% open; all frames show the same destroyed sprite.
                    _doorAtlasCache.TryGetValue("door_ns_destroyed", out var dest);
                    var s = dest ?? MakeDoorE(0.35f, status, 2);
                    for (int i = 0; i < 10; i++) frames[i] = s;
                }
                else
                {
                    // 10 atlas frames: door_ns_open0 (closed) → door_ns_open9 (fully open).
                    for (int i = 0; i < 10; i++)
                    {
                        float  frac = i / 9f;
                        string name = $"door_ns_open{i}";
                        frames[i] = (_doorAtlasCache.TryGetValue(name, out var s) && s != null)
                            ? s : MakeDoorE(frac, status, 0);
                    }
                }
                _doorCache2[key] = frames;
            }
            return frames;
        }

        private static Sprite[] _doorHFrames;
        private static Sprite[] _doorVFrames;

        /// Ten animation frames for a door on an EW wall (isH=true; caller rotates GO 90° via DoorOrientation.EW).
        public static Sprite[] GetDoorHFrames() => GetDoorFrames(true);
        /// Ten animation frames for a door on an NS wall (isH=false; GO stays at 0° rotation).
        public static Sprite[] GetDoorVFrames() => GetDoorFrames(false);

        // ── MakeDoorS — NS-wall door (panels slide N↔S, passage runs E-W) ──────
        // Used when isH=false (wall runs N↔S, floor to east or west).
        // open: 0=closed, 1=fully open.
        static Sprite MakeDoorS(float open, string status, int dmgLevel)
        {
            if (dmgLevel == 1) open = 0f;
            // dmgLevel == 2 no longer forces open = 1f; caller passes the desired fraction (0.35f).

            var p = NewPixels();
            Color32 acc  = DAcc(status);
            Color32 accG = DAccG(status);

            // ── Door frame base (wall-flush) ──────────────────────────────────
            Fr(p, 0, 0, 64, 64, WTBase);
            // Outer bevel
            Fr(p, 0,  0,  64, 1,  WTHi); Fr(p, 0, 0,  1, 64, WTHi);
            Fr(p, 0,  63, 64, 1,  WTLo); Fr(p, 63, 0, 1, 64, WTLo);
            // Top/bottom frame rails (y 1..4 and y 59..62)
            Fr(p, 1,  1,  62, 4,  DFrame);
            Fr(p, 1,  59, 62, 4,  DFrame);
            Fr(p, 1,  1,  62, 1,  DFrameHi); Fr(p, 1, 4,  62, 1, DFrameLo);
            Fr(p, 1,  62, 62, 1,  DFrameHi); Fr(p, 1, 59, 62, 1, DFrameLo);
            // Left/right frame posts (x 1..6) and (x 57..62), rows 4..59
            Fr(p, 1,  4,  6,  56, DFrame);
            Fr(p, 57, 4,  6,  56, DFrame);
            Fr(p, 1,  4,  1,  56, DFrameHi); Fr(p, 6,  4, 1,  56, DFrameLo);
            Fr(p, 62, 4,  1,  56, DFrameHi); Fr(p, 57, 4, 1,  56, DFrameLo);
            // Corner rivets
            Fr(p, 2,  2,  2,  1, WRHi); Fr(p, 2,  3,  2,  1, WRLo);
            Fr(p, 59, 2,  2,  1, WRHi); Fr(p, 59, 3,  2,  1, WRLo);
            Fr(p, 2,  60, 2,  1, WRHi); Fr(p, 2,  61, 2,  1, WRLo);
            Fr(p, 59, 60, 2,  1, WRHi); Fr(p, 59, 61, 2,  1, WRLo);
            // Track grooves at post inner edge
            Fr(p, 6,  4,  2,  56, DTrack);
            Fr(p, 56, 4,  2,  56, DTrack);
            // Sensor LED at top rail centre
            Px(p, 31, 2, acc); Px(p, 32, 2, acc);
            Px(p, 31, 61, accG); Px(p, 32, 61, accG);

            // ── Interior region x=8..55  y=5..58 ─────────────────────────────
            const int ix1 = 8, ix2 = 55, iy1 = 5, iy2 = 58;
            const int mid = 31; // centre x (seam between two panels)
            int interiorW = ix2 - ix1 + 1; // 48
            int halfW = (mid - ix1);       // 23 = pixels per panel at full-closed

            int pW = Mathf.Max(0, Mathf.RoundToInt(halfW * (1f - open)));

            if (pW > 0)
            {
                // Left panel: ix1 .. ix1+pW-1
                Fr(p, ix1,         iy1, pW, iy2 - iy1 + 1, DPanel);
                Fr(p, ix1,         iy1, pW, 1, DPanelHi);
                Fr(p, ix1,         iy1, 1,  iy2 - iy1 + 1, DPanelHi);
                Fr(p, ix1,         iy2, pW, 1, DPanelLo);
                Fr(p, ix1 + pW - 1, iy1, 1, iy2 - iy1 + 1, DPanelLo);
                // Left panel inset
                if (pW > 6)
                {
                    Fr(p, ix1 + 3, iy1 + 3, pW - 6, iy2 - iy1 - 5, DPanelDeep);
                    Fr(p, ix1 + 3, iy1 + 3, pW - 6, 1, DPanelHi);
                    Fr(p, ix1 + 3, iy1 + 3, 1, iy2 - iy1 - 5, DPanelHi);
                    Fr(p, ix1 + 3, iy2 - 3, pW - 6, 1, DPanelLo);
                    Fr(p, ix1 + pW - 4, iy1 + 3, 1, iy2 - iy1 - 5, DPanelLo);
                }
                // Left panel rivets
                Fr(p, ix1 + 1, iy1 + 1, 2, 1, WRHi); Fr(p, ix1 + 1, iy1 + 2, 2, 1, WRLo);
                Fr(p, ix1 + 1, iy2 - 2, 2, 1, WRHi); Fr(p, ix1 + 1, iy2 - 1, 2, 1, WRLo);

                // Right panel: ix2-pW+1 .. ix2
                int rpx = ix2 - pW + 1;
                Fr(p, rpx, iy1, pW, iy2 - iy1 + 1, DPanel);
                Fr(p, rpx, iy1, pW, 1, DPanelHi);
                Fr(p, rpx, iy1, 1,  iy2 - iy1 + 1, DPanelHi);
                Fr(p, rpx, iy2, pW, 1, DPanelLo);
                Fr(p, ix2, iy1, 1,  iy2 - iy1 + 1, DPanelLo);
                if (pW > 6)
                {
                    Fr(p, rpx + 3, iy1 + 3, pW - 6, iy2 - iy1 - 5, DPanelDeep);
                    Fr(p, rpx + 3, iy1 + 3, pW - 6, 1, DPanelHi);
                    Fr(p, rpx + 3, iy1 + 3, 1, iy2 - iy1 - 5, DPanelHi);
                    Fr(p, rpx + 3, iy2 - 3, pW - 6, 1, DPanelLo);
                    Fr(p, ix2 - 3, iy1 + 3, 1, iy2 - iy1 - 5, DPanelLo);
                }
                Fr(p, ix2 - 3, iy1 + 1, 2, 1, WRHi); Fr(p, ix2 - 3, iy1 + 2, 2, 1, WRLo);
                Fr(p, ix2 - 3, iy2 - 2, 2, 1, WRHi); Fr(p, ix2 - 3, iy2 - 1, 2, 1, WRLo);
            }

            // Void gap between panels (or full interior when open)
            int gapX1 = ix1 + pW;
            int gapX2 = ix2 - pW;
            if (gapX1 <= gapX2)
            {
                Fr(p, gapX1, iy1, gapX2 - gapX1 + 1, iy2 - iy1 + 1, DVoid);
                // Ambient void gradient
                for (int vy = iy1; vy <= iy2; vy++)
                {
                    float t = (float)(vy - iy1) / (iy2 - iy1);
                    float bright = 1f - Mathf.Abs(t - 0.5f) * 2f;
                    Color32 litC = LerpC32(DVoid, DVoidLit, bright * 0.55f);
                    for (int vx = gapX1 + 1; vx <= gapX2 - 1; vx++)
                        Px(p, vx, vy, litC);
                }
            }

            // Centre seam (always drawn over panels/void)
            Fr(p, mid, iy1, 2, iy2 - iy1 + 1, DSeam);

            // Track rails top+bottom of interior (run full width when partially open)
            if (open > 0f)
            {
                Fr(p, ix1, iy1, interiorW, 1, DTrack);
                Fr(p, ix1, iy1 + 1, interiorW, 1, DTrack);
                Fr(p, ix1, iy2, interiorW, 1, DTrack);
                Fr(p, ix1, iy2 - 1, interiorW, 1, DTrack);
            }

            // Damaged overlay
            if (dmgLevel >= 1)
            {
                // Scorch + cracks on panel leading edges
                byte sc = (byte)(dmgLevel == 1 ? 60 : 120);
                for (int dy = iy1; dy <= iy2; dy++)
                {
                    BlendOver(p, mid - 1, dy, new Color32(0, 0, 0, sc));
                    BlendOver(p, mid + 2, dy, new Color32(0, 0, 0, sc));
                }
                Fr(p, mid - 1, iy1 + (iy2 - iy1) / 2 - 2, 4, 4, C("#0f0e14"));
            }

            return MakeSprite(p);
        }

        // ── MakeDoorE — EW-wall door perspective tile (passage runs N-S, wall runs E-W) ─────
        // Used when isH=true (wall runs E↔W, floor to north or south).
        // Left face (x=0..15) + top surface strip (x=16..47, panels slide N↔S) + right face (x=48..63).
        // open: 0=closed, 1=fully open.  No Transform rotation required — sprite faces EW wall.
        static Sprite MakeDoorE(float open, string status, int dmgLevel)
        {
            if (dmgLevel == 1) open = 0f;
            // dmgLevel == 2 no longer forces open = 1f; caller passes the desired fraction (0.35f).

            var p = NewPixels();
            Color32 acc  = DAcc(status);
            Color32 accG = DAccG(status);

            // Face widths matching HTML: FL=16, FS=32, FR=16
            const int FL = 16, FS = 32, FR = 16;
            const int lx = 0;          // left face left edge
            const int sx = FL;         // top surface left edge  (x=16)
            const int rx = FL + FS;    // right face left edge   (x=48)

            // ── LEFT FACE (x=0..15) ──────────────────────────────────────────
            // Gradient: fBase (bright) at left outer edge → fDark (dark) at inner right edge
            for (int c = 0; c < FL; c++)
            {
                float t = (float)c / (FL - 1);
                Color32 col = LerpC32(WFBase, WFDark, t);
                for (int y = 0; y < 64; y++) Px(p, lx + c, y, col);
            }
            // Outer bevel (left edge)
            for (int y = 0; y < 64; y++) Px(p, lx, y, WRHi);
            // Inner shadow (right edge = doorway opening)
            for (int y = 0; y < 64; y++) Px(p, lx + FL - 1, y, WFShadow);
            // Top/bottom edge lines
            Fr(p, lx, 0,  FL, 1, WTHi);
            Fr(p, lx, 63, FL, 1, WTGrout);
            // Frame posts (horizontal bands near top/bottom of left face)
            Fr(p, lx + 2, 2,  FL - 4, 5, DFrame);
            Fr(p, lx + 2, 2,  FL - 4, 1, DFrameHi);
            Fr(p, lx + 2, 6,  FL - 4, 1, DFrameLo);
            Fr(p, lx + 2, 57, FL - 4, 5, DFrame);
            Fr(p, lx + 2, 57, FL - 4, 1, DFrameHi);
            Fr(p, lx + 2, 61, FL - 4, 1, DFrameLo);
            // Panel body — two halves split at seam (y=31..32)
            Fr(p, lx + 2, 7,  FL - 4, 24, DPanel);
            Fr(p, lx + 2, 33, FL - 4, 24, DPanel);
            Fr(p, lx + 2, 31, FL - 4,  2, DSeam);
            // Inner (right) edge bevel of panels — doorway-facing = brighter
            for (int y = 7;  y <= 30; y++) Px(p, lx + FL - 3, y, DPanelHi);
            for (int y = 33; y <= 56; y++) Px(p, lx + FL - 3, y, DPanelHi);
            // Sensor LED at inner edge, seam height
            Px(p, lx + FL - 3, 31, acc); Px(p, lx + FL - 3, 32, acc);
            // Corner rivets at frame post corners
            Fr(p, lx + 3, 3,  2, 1, WRHi); Fr(p, lx + 3, 4,  2, 1, WRLo);
            Fr(p, lx + FL - 5, 3,  2, 1, WRHi); Fr(p, lx + FL - 5, 4,  2, 1, WRLo);
            Fr(p, lx + 3, 58, 2, 1, WRHi); Fr(p, lx + 3, 59, 2, 1, WRLo);
            Fr(p, lx + FL - 5, 58, 2, 1, WRHi); Fr(p, lx + FL - 5, 59, 2, 1, WRLo);

            // ── RIGHT FACE (x=48..63) — mirror of left face ──────────────────
            // Gradient: fDark (dark) at inner left edge → fBase (bright) at outer right edge
            for (int c = 0; c < FR; c++)
            {
                float t = (float)c / (FR - 1);
                Color32 col = LerpC32(WFDark, WFBase, t);
                for (int y = 0; y < 64; y++) Px(p, rx + c, y, col);
            }
            // Inner shadow (left edge = doorway opening)
            for (int y = 0; y < 64; y++) Px(p, rx, y, WFShadow);
            // Outer bevel (right edge)
            for (int y = 0; y < 64; y++) Px(p, rx + FR - 1, y, WRHi);
            // Top/bottom edge lines
            Fr(p, rx, 0,  FR, 1, WTHi);
            Fr(p, rx, 63, FR, 1, WTGrout);
            // Frame posts
            Fr(p, rx + 2, 2,  FR - 4, 5, DFrame);
            Fr(p, rx + 2, 2,  FR - 4, 1, DFrameLo);  // mirrored bevel
            Fr(p, rx + 2, 6,  FR - 4, 1, DFrameHi);
            Fr(p, rx + 2, 57, FR - 4, 5, DFrame);
            Fr(p, rx + 2, 57, FR - 4, 1, DFrameLo);
            Fr(p, rx + 2, 61, FR - 4, 1, DFrameHi);
            // Panel body
            Fr(p, rx + 2, 7,  FR - 4, 24, DPanel);
            Fr(p, rx + 2, 33, FR - 4, 24, DPanel);
            Fr(p, rx + 2, 31, FR - 4,  2, DSeam);
            // Inner (left) edge bevel — doorway-facing
            for (int y = 7;  y <= 30; y++) Px(p, rx + 2, y, DPanelLo);
            for (int y = 33; y <= 56; y++) Px(p, rx + 2, y, DPanelLo);
            // Sensor LED at inner edge, seam height
            Px(p, rx + 2, 31, acc); Px(p, rx + 2, 32, acc);
            // Corner rivets
            Fr(p, rx + 3, 3,  2, 1, WRHi); Fr(p, rx + 3, 4,  2, 1, WRLo);
            Fr(p, rx + FR - 5, 3,  2, 1, WRHi); Fr(p, rx + FR - 5, 4,  2, 1, WRLo);
            Fr(p, rx + 3, 58, 2, 1, WRHi); Fr(p, rx + 3, 59, 2, 1, WRLo);
            Fr(p, rx + FR - 5, 58, 2, 1, WRHi); Fr(p, rx + FR - 5, 59, 2, 1, WRLo);

            // ── TOP SURFACE STRIP (x=16..47, width=32) ───────────────────────
            // Base wall top surface
            Fr(p, sx, 0,  FS, 64, WTBase);
            Fr(p, sx, 0,  FS, 1,  WTHi);   // top bevel
            Fr(p, sx, 63, FS, 1,  WTLo);   // bottom bevel
            // Frame rails — top (y=1..5) and bottom (y=58..62)
            Fr(p, sx, 1,  FS, 5, DFrame);
            Fr(p, sx, 1,  FS, 1, DFrameHi); Fr(p, sx, 5, FS, 1, DFrameLo);
            Fr(p, sx, 58, FS, 5, DFrame);
            Fr(p, sx, 58, FS, 1, DFrameHi); Fr(p, sx, 62, FS, 1, DFrameLo);
            // Sensor LED at top rail centre
            Px(p, sx + FS / 2, 3, acc);

            // Interior region: x=sx..sx+FS-1, y=6..57
            const int iy1e = 6, iy2e = 57;
            int halfHe  = (31 - iy1e); // 25 — pixels per panel at full-closed
            int pHe     = Mathf.Max(0, Mathf.RoundToInt(halfHe * (1f - open)));

            if (pHe > 0)
            {
                // Top panel: iy1e .. iy1e+pHe-1
                Fr(p, sx, iy1e,       FS, pHe, DPanel);
                Fr(p, sx, iy1e,       FS, 1,   DPanelHi);
                Fr(p, sx, iy1e+pHe-1, FS, 1,   DPanelLo);
                // Bottom panel: iy2e-pHe+1 .. iy2e
                int bpye = iy2e - pHe + 1;
                Fr(p, sx, bpye, FS, pHe, DPanel);
                Fr(p, sx, bpye, FS, 1,   DPanelHi);
                Fr(p, sx, iy2e, FS, 1,   DPanelLo);
                // Track rails at panel outer edges (when partially open)
                if (open > 0f)
                {
                    Fr(p, sx, iy1e,       FS, 1, DTrack);
                    Fr(p, sx, iy1e + 1,   FS, 1, DTrack);
                    Fr(p, sx, iy2e,       FS, 1, DTrack);
                    Fr(p, sx, iy2e - 1,   FS, 1, DTrack);
                }
            }

            // Void gap between panels (or full interior when fully open)
            int gapYe1 = iy1e + pHe;
            int gapYe2 = iy2e - pHe;
            if (gapYe1 <= gapYe2)
            {
                Fr(p, sx, gapYe1, FS, gapYe2 - gapYe1 + 1, DVoid);
                // Ambient horizontal gradient in void — brighter at x-centre of strip
                for (int vy = gapYe1 + 1; vy <= gapYe2 - 1; vy++)
                {
                    for (int vx = sx + 1; vx < sx + FS - 1; vx++)
                    {
                        float t = (float)(vx - sx) / (FS - 1);
                        float bright = 1f - Mathf.Abs(t - 0.5f) * 2f;
                        Px(p, vx, vy, LerpC32(DVoid, DVoidLit, bright * 0.55f));
                    }
                }
                // Track rails at gap edges
                Fr(p, sx, gapYe1, FS, 1, DTrack);
                Fr(p, sx, gapYe2, FS, 1, DTrack);
                if (gapYe2 - gapYe1 > 1)
                {
                    Fr(p, sx, gapYe1 + 1, FS, 1, DTrack);
                    Fr(p, sx, gapYe2 - 1, FS, 1, DTrack);
                }
            }

            // Centre seam (always drawn)
            Fr(p, sx, 31, FS, 2, DSeam);

            // Damaged overlay
            if (dmgLevel >= 1)
            {
                byte sc = (byte)(dmgLevel == 1 ? 60 : 120);
                for (int dx = sx; dx < sx + FS; dx++)
                {
                    BlendOver(p, dx, 30, new Color32(0, 0, 0, sc));
                    BlendOver(p, dx, 33, new Color32(0, 0, 0, sc));
                }
                Fr(p, sx + FS / 2 - 2, 30, 4, 4, C("#0f0e14"));
                // Crack lines on face panels at inner edges
                Fr(p, lx + FL - 4, 29, 3, 5, C("#0f0e14"));
                Fr(p, rx + 1,       29, 3, 5, C("#0f0e14"));
            }

            return MakeSprite(p);
        }

        // ── Wire Make method ─────────────────────────────────────────────────────────
        // Spec: WireTilesheet.html  — 4px body C0=30..C1=33, transparent bg, blue node

        // Wire palette (WireTilesheet.html)
        static readonly Color32 WRCore   = C("#2a3048");  // wireCore
        static readonly Color32 WRMid    = C("#363c54");  // wireMid
        static readonly Color32 WRHigh   = C("#4e5470");  // wireHi
        static readonly Color32 WRShd    = C("#141820");  // wireShadow
        static readonly Color32 WRGrout  = C("#1a1d28");  // wireGrout
        static readonly Color32 WRNBase  = C("#383c50");  // nodeBase
        static readonly Color32 WRNHi    = C("#4e5470");  // nodeHi
        static readonly Color32 WRNLo    = C("#1c1f2c");  // nodeLo
        static readonly Color32 WRNGt    = C("#141618");  // nodeGrout
        static readonly Color32 WRNAcc   = C("#4880aa");  // nodeAcc (blue)
        static readonly Color32 WRNAccG  = C("#111e30");  // nodeAccG

        // Horizontal wire arm: grout+body+shadow rows 29..34 spanning x0..x1
        static void WireHA(Color32[] p, int x0, int x1)
        {
            int w = x1 - x0 + 1;
            Fr(p, x0, 29, w, 1, WRGrout);
            Fr(p, x0, 30, w, 1, WRHigh);
            Fr(p, x0, 31, w, 1, WRMid);
            Fr(p, x0, 32, w, 1, WRCore);
            Fr(p, x0, 33, w, 1, WRShd);
            Fr(p, x0, 34, w, 1, WRShd);
        }

        // Vertical wire arm: grout+body+shadow cols 29..34 spanning y0..y1
        static void WireVA(Color32[] p, int y0, int y1)
        {
            int h = y1 - y0 + 1;
            Fr(p, 29, y0, 1, h, WRGrout);
            Fr(p, 30, y0, 1, h, WRHigh);
            Fr(p, 31, y0, 1, h, WRMid);
            Fr(p, 32, y0, 1, h, WRCore);
            Fr(p, 33, y0, 1, h, WRShd);
            Fr(p, 34, y0, 1, h, WRShd);
        }

        // Wire connector node centred at MX=31, MY=31
        static void WireND(Color32[] p)
        {
            Fr(p, 27, 27, 9, 9, WRNGt);   // outer grout 9×9
            Fr(p, 28, 28, 7, 7, WRNBase); // base 7×7
            Fr(p, 28, 28, 7, 1, WRNHi);   // top bevel
            Fr(p, 28, 28, 1, 7, WRNHi);   // left bevel
            Fr(p, 28, 34, 7, 1, WRNLo);   // bottom bevel
            Fr(p, 34, 28, 1, 7, WRNLo);   // right bevel
            Fr(p, 30, 30, 3, 3, WRNGt);   // inner recess 3×3
            Px(p, 31, 31, WRNAcc);         // blue accent dot
            Px(p, 30, 31, WRNAccG);  Px(p, 32, 31, WRNAccG);
            Px(p, 31, 30, WRNAccG);  Px(p, 31, 32, WRNAccG);
        }

        static Sprite MakeWire(int mask)
        {
            var p = NewPixels();
            bool N = (mask & 1) != 0, E = (mask & 2) != 0,
                 S = (mask & 4) != 0, W = (mask & 8) != 0;
            if (N) WireVA(p,  0, 31);
            if (S) WireVA(p, 31, 63);
            if (E) WireHA(p, 31, 63);
            if (W) WireHA(p,  0, 31);
            WireND(p);
            return MakeSprite(p);
        }

        // ── Pipe Make method ──────────────────────────────────────────────────────────

        static readonly Color32 PPBase     = C("#465a70");
        static readonly Color32 PPDark     = C("#2d3d50");
        static readonly Color32 PPHi       = C("#5878a0");
        static readonly Color32 PPAmber    = C("#c8a030");
        static readonly Color32 PPRed      = C("#c04020");
        static readonly Color32 PPFlange   = C("#384858");
        static readonly Color32 PPFlangeHi = C("#506070");

        static Sprite MakePipe(int mask, int stateIdx)
        {
            var p = NewPixels();
            const int C1 = 28, ARW = 8;
            bool N = (mask & 1) != 0, E = (mask & 2) != 0,
                 S = (mask & 4) != 0, W = (mask & 8) != 0;

            if (N) { Fr(p, C1, 0,  ARW, 28, PPDark); Fr(p, C1+1, 0, 6, 28, PPBase); Fr(p, C1+2, 0, 4, 28, PPHi); }
            if (S) { Fr(p, C1, 36, ARW, 28, PPDark); Fr(p, C1+1, 36, 6, 28, PPBase); Fr(p, C1+2, 36, 4, 28, PPHi); }
            if (E) { Fr(p, 36, C1, 28, ARW, PPDark); Fr(p, 36, C1+1, 28, 6, PPBase); Fr(p, 36, C1+2, 28, 4, PPHi); }
            if (W) { Fr(p, 0,  C1, 28, ARW, PPDark); Fr(p, 0,  C1+1, 28, 6, PPBase); Fr(p, 0,  C1+2, 28, 4, PPHi); }

            Fr(p, C1-2, C1-2, 12, 12, PPFlange);
            Fr(p, C1-1, C1-1, 10, 10, PPFlangeHi);
            Fr(p, C1,   C1,    8,  8, PPBase);
            Fr(p, C1+1, C1+1,  6,  6, PPHi);

            if (stateIdx == 1)
            {
                if (N || S) Fr(p, C1+3, 0, 2, 64, PPAmber);
                if (E || W) Fr(p, 0, C1+3, 64, 2, PPAmber);
                Fr(p, C1+3, C1+3, 2, 2, PPAmber);
            }
            else if (stateIdx == 2)
            {
                Fr(p, C1+1, C1+1, 6, 6, PPRed);
            }
            return MakeSprite(p);
        }

        // ── Duct Make method ─────────────────────────────────────────────────────────
        // Spec: DuctTilesheet.html — 14px body D0=25..D1=34 + 4px south face D2=38

        // Duct palette (DuctTilesheet.html)
        static readonly Color32 DTTop    = C("#3a3e52");
        static readonly Color32 DTTopHi  = C("#4e5268");
        static readonly Color32 DTTopMd  = C("#343848");
        static readonly Color32 DTTopLo  = C("#282c3c");
        static readonly Color32 DTFace   = C("#1e2230");
        static readonly Color32 DTFaceH  = C("#282c3c");
        static readonly Color32 DTFaceL  = C("#10121a");
        static readonly Color32 DTFlng   = C("#2e3244");
        static readonly Color32 DTFlngH  = C("#424660");
        static readonly Color32 DTFlngL  = C("#181a26");
        static readonly Color32 DTFlgGt  = C("#141618");
        static readonly Color32 DTColl   = C("#383c50");
        static readonly Color32 DTCollH  = C("#4e5270");
        static readonly Color32 DTCollL  = C("#1c1e2c");
        static readonly Color32 DTCollG  = C("#0e0f16");
        static readonly Color32 DTCollI  = C("#242838");
        static readonly Color32 DTRvHi   = C("#565e7a");
        static readonly Color32 DTRvLo   = C("#2c3048");

        // Horizontal duct arm: top face y=25..34, south perspective face y=35..38
        static void DuctHA(Color32[] p, int x0, int x1)
        {
            int w = x1 - x0 + 1;
            Fr(p, x0, 25, w, 10, DTTopMd);  // base fill
            Fr(p, x0, 25, w,  1, DTTopHi);  // y=25 north highlight
            Fr(p, x0, 26, w,  1, DTTop);    // y=26
            Fr(p, x0, 28, w,  1, DTTop);    // y=28 sheen stripe
            Fr(p, x0, 33, w,  1, DTTopLo);  // y=33 shadow
            Fr(p, x0, 34, w,  1, DTTopLo);  // y=34 shadow
            Fr(p, x0, 35, w,  1, DTFaceH);  // south face top edge
            Fr(p, x0, 36, w,  2, DTFace);   // south face fill
            Fr(p, x0, 38, w,  1, DTFaceL);  // south face bottom
        }

        // Vertical duct arm: west-facing top face x=25..34, east face x=35..38
        static void DuctVA(Color32[] p, int y0, int y1)
        {
            int h = y1 - y0 + 1;
            Fr(p, 25, y0, 10, h, DTTopMd);
            Fr(p, 25, y0,  1, h, DTTopHi);
            Fr(p, 26, y0,  1, h, DTTop);
            Fr(p, 28, y0,  1, h, DTTop);    // sheen stripe
            Fr(p, 33, y0,  1, h, DTTopLo);
            Fr(p, 34, y0,  1, h, DTTopLo);
            Fr(p, 35, y0,  1, h, DTFaceH);
            Fr(p, 36, y0,  2, h, DTFace);
            Fr(p, 38, y0,  1, h, DTFaceL);
        }

        // Duct collar (junction box) centred at tile centre 31,31
        static void DuctColl(Color32[] p)
        {
            Fr(p, 22, 22, 18, 18, DTFlgGt);   // outer grout 18×18
            Fr(p, 23, 23, 16, 16, DTFlng);    // flange plate 16×16
            Fr(p, 23, 23, 16,  1, DTFlngH);   // top bevel
            Fr(p, 23, 23,  1, 16, DTFlngH);   // left bevel
            Fr(p, 23, 38, 16,  1, DTFlngL);   // bottom bevel
            Fr(p, 38, 23,  1, 16, DTFlngL);   // right bevel
            Fr(p, 25, 25, 12, 12, DTColl);    // inner collar 12×12
            Fr(p, 25, 25, 12,  1, DTCollH);   Fr(p, 25, 25,  1, 12, DTCollH);
            Fr(p, 25, 36, 12,  1, DTCollL);   Fr(p, 36, 25,  1, 12, DTCollL);
            Fr(p, 27, 27,  8,  8, DTCollI);   // inset 8×8
            Fr(p, 23, 23, 2, 1, DTRvHi); Fr(p, 23, 24, 2, 1, DTRvLo);  // rivet TL
            Fr(p, 37, 23, 2, 1, DTRvHi); Fr(p, 37, 24, 2, 1, DTRvLo);  // rivet TR
            Fr(p, 23, 37, 2, 1, DTRvHi); Fr(p, 23, 38, 2, 1, DTRvLo);  // rivet BL
            Fr(p, 37, 37, 2, 1, DTRvHi); Fr(p, 37, 38, 2, 1, DTRvLo);  // rivet BR
        }

        static Sprite MakeDuct(int mask)
        {
            var p = NewPixels();
            bool N = (mask & 1) != 0, E = (mask & 2) != 0,
                 S = (mask & 4) != 0, W = (mask & 8) != 0;
            if (N) DuctVA(p,  0, 31);
            if (S) DuctVA(p, 31, 63);
            if (E) DuctHA(p, 31, 63);
            if (W) DuctHA(p,  0, 31);
            DuctColl(p);
            return MakeSprite(p);
        }

        // ── Ice Refiner Make method (128×64) ─────────────────────────────────────────

        static readonly Color32 IRBase   = C("#2d3040");
        static readonly Color32 IRHi     = C("#3a3e56");
        static readonly Color32 IRDark   = C("#1e2030");
        static readonly Color32 IRCyan   = C("#30b8c8");
        static readonly Color32 IRBlue   = C("#4880aa");
        static readonly Color32 IRAmber  = C("#c8b030");
        static readonly Color32 IRRed    = C("#d03020");
        static readonly Color32 IRRivet  = C("#505870");
        static readonly Color32 IREdge   = C("#1a1c28");

        static Sprite MakeIceRefiner(string variantId)
        {
            var p = new Color32[128 * 64];
            void Fill128(int x, int y, int w, int h, Color32 c)
            {
                for (int py = y; py < y + h; py++)
                for (int px = x; px < x + w; px++)
                    if ((uint)px < 128 && (uint)py < 64)
                        p[(63 - py) * 128 + px] = c;
            }
            void Dot128(int x, int y, Color32 c)
            {
                if ((uint)x < 128 && (uint)y < 64)
                    p[(63 - y) * 128 + x] = c;
            }

            bool isRefining = variantId.StartsWith("refining_");
            bool isOutput   = variantId.StartsWith("output_");
            bool isDamaged  = variantId.StartsWith("damaged_");
            bool isBroken   = variantId == "broken";

            int phase = 0;
            if (isRefining && variantId.Length > 9 && int.TryParse(variantId.Substring(9), out int rp)) phase = rp;
            if (isOutput   && variantId.Length > 7 && int.TryParse(variantId.Substring(7), out int op)) phase = op;
            if (isDamaged  && variantId.Length > 8 && int.TryParse(variantId.Substring(8), out int dp)) phase = dp;

            // Housing
            Fill128(0, 0, 128, 64, IREdge);
            Fill128(1, 1, 126, 62, IRBase);
            Fill128(2, 1, 124, 1, IRHi);
            Fill128(1, 2, 1, 60, IRHi);

            // Hopper (x 3..49)
            Fill128(3, 4, 47, 55, IRDark);
            Fill128(4, 5, 45, 53, IRBase);
            Fill128(5, 6, 43, 6, IRHi);

            Color32 hopperFill = isBroken  ? IRRed :
                                 isDamaged ? LerpC32(IRBase, IRRed, 0.4f) :
                                 isRefining ? LerpC32(IRBlue, IRCyan, phase / 4f) : IRBlue;
            Fill128(7, 20, 36, 20, hopperFill);
            Fill128(7, 20, 36, 1, IRHi);
            Fill128(7, 20, 1, 20, IRHi);

            // Compressor (x 50..89)
            Fill128(52, 4, 38, 55, IRDark);
            Fill128(53, 5, 36, 53, IRBase);
            int pistonY = isRefining ? 15 + phase * 4 : 15;
            Fill128(58, pistonY, 26, 10, IRHi);
            Fill128(60, pistonY + 1, 22, 8, isRefining ? IRAmber : IRBase);

            // Output (x 90..111)
            Fill128(91, 4, 20, 55, IRDark);
            Fill128(92, 5, 18, 53, IRBase);
            Color32 outputLed = isOutput ? LerpC32(IRCyan, IRBlue, phase / 4f) :
                                isBroken ? IRRed : IRBlue;
            Fill128(95, 25, 12, 12, outputLed);
            Fill128(96, 26, 10, 1, IRHi);

            // Terminal (x 112..127)
            Fill128(113, 4, 14, 55, IRDark);
            Fill128(114, 5, 12, 53, IRBase);
            Color32 statusLed = isBroken ? IRRed : isDamaged ? IRAmber :
                                isRefining ? IRCyan : isOutput ? IRBlue : IRHi;
            for (int l = 0; l < 5; l++)
                Fill128(116, 10 + l * 10, 5, 5, statusLed);

            // Rivets
            foreach (var (rx, ry) in new[] { (2,2),(2,61),(125,2),(125,61),(49,2),(49,61),(89,2),(89,61),(111,2),(111,61) })
                Dot128(rx, ry, IRRivet);

            return MakeSprite128(p);
        }

        // ── Bed Make method ──────────────────────────────────────────────────────────
        // Spec: BedTilesheet.html — 128×64 landscape, headboard west, footboard east

        // Bed palette (BedTilesheet.html)
        static readonly Color32 BFrGt  = C("#181614");  // frameGrout
        static readonly Color32 BFrBs  = C("#32302a");  // frameBase
        static readonly Color32 BFrHi  = C("#484438");  // frameHi
        static readonly Color32 BFrLo  = C("#1e1c18");  // frameLo
        static readonly Color32 BFrPn  = C("#2a2824");  // framePanel
        static readonly Color32 BFrPHi = C("#3c3830");  // framePanelHi
        static readonly Color32 BLgBs  = C("#2a2820");  // legBase
        static readonly Color32 BLgHi  = C("#3e3c30");  // legHi
        static readonly Color32 BMtBs  = C("#2e3040");  // mattBase
        static readonly Color32 BMtEd  = C("#383c50");  // mattEdge
        static readonly Color32 BMtSm  = C("#222430");  // mattSeam
        static readonly Color32 BBlBs  = C("#343038");  // blankBase
        static readonly Color32 BBlHi  = C("#484450");  // blankHi
        static readonly Color32 BBlLo  = C("#201e28");  // blankLo
        static readonly Color32 BBlMd  = C("#3c3844");  // blankMid
        static readonly Color32 BBlTk  = C("#1e1c24");  // blankTuck
        static readonly Color32 BBlWk  = C("#2c2a34");  // blankWrinkle
        static readonly Color32 BPlBs  = C("#3c3c44");  // pillowBase
        static readonly Color32 BPlHi  = C("#505058");  // pillowHi
        static readonly Color32 BPlLo  = C("#282830");  // pillowLo
        static readonly Color32 BPlSm  = C("#2a2a32");  // pillowSeam
        static readonly Color32 BPlCs  = C("#343440");  // pillowCase
        static readonly Color32 BLed   = C("#4880aa");  // acc blue LED
        static readonly Color32 BLedG  = C("#111e30");  // accG blue LED glow

        static Sprite MakeBed(int rotationStep)
        {
            var p = new Color32[128 * 64];
            void Fill(int x, int y, int w, int h, Color32 c)
            {
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int bx = x + dx, by = y + dy;
                    if ((uint)bx < 128 && (uint)by < 64)
                        p[(63 - by) * 128 + bx] = c;
                }
            }
            void Dot(int x, int y, Color32 c)
            {
                if ((uint)x < 128 && (uint)y < 64)
                    p[(63 - y) * 128 + x] = c;
            }
            // x zones: headboard 0..9 | pillow 10..40 | blanket 41..117 | footboard 118..127
            // y zones: north_rail 0..3 | mattress 4..41 | south_rail 42..45 | south_face 46..63

            // ── North rail (y=0..3) ───────────────────────────────────────────────────
            Fill(  0,  0, 128,  4, BFrBs);
            Fill(  0,  0, 128,  1, BFrHi);  // top highlight
            Fill(  0,  3, 128,  1, BFrGt);  // bottom grout

            // ── South rail (y=42..45) ─────────────────────────────────────────────────
            Fill(  0, 42, 128,  4, BFrBs);
            Fill(  0, 42, 128,  1, BFrGt);  // top grout
            Fill(  0, 45, 128,  1, BFrLo);  // bottom shadow

            // ── South face (y=46..63, FH=18) ──────────────────────────────────────────
            Fill(  0, 46, 128, 18, BFrPn);
            Fill(  0, 46, 128,  1, BFrBs);  // lip at top
            Fill(  0, 63, 128,  1, BFrGt);  // bottom grout
            Fill(  0, 46,   1, 18, BFrHi);  // left edge
            Fill(127, 46,   1, 18, BFrLo);  // right shadow
            Fill(  4, 48, 120, 14, BFrPHi); // recessed panel outer
            Fill(  5, 49, 118, 12, BFrPn);  // recessed panel inner
            // Leg stubs
            Fill(  2, 59,  4,  4, BLgBs); Fill(  2, 59,  4,  1, BLgHi);
            Fill(122, 59,  4,  4, BLgBs); Fill(122, 59,  4,  1, BLgHi);

            // ── Headboard (x=0..9, y=0..45) ──────────────────────────────────────────
            Fill(  0,  0, 10, 46, BFrBs);
            Fill(  0,  0,  1, 46, BFrHi);  // west outer highlight
            Fill(  9,  0,  1, 46, BFrGt);  // east inner grout
            Fill(  1,  5,  7, 35, BFrPHi); // panel outer
            Fill(  2,  6,  5, 33, BFrPn);  // panel inner
            Fill(  3,  7,  3, 31, BFrPHi); // panel inset
            Dot(4, 21, BLed);  Dot(5, 21, BLedG);  // blue status LED
            Dot(4, 22, BLedG); Dot(5, 22, BLedG);

            // ── Footboard (x=118..127, y=0..45) ──────────────────────────────────────
            Fill(118,  0, 10, 46, BFrBs);
            Fill(118,  0,  1, 46, BFrGt);  // west inner grout
            Fill(127,  0,  1, 46, BFrHi);  // east outer highlight
            Fill(119,  5,  7, 35, BFrPHi);
            Fill(120,  6,  5, 33, BFrPn);

            // ── Mattress (x=10..117, y=4..41) ────────────────────────────────────────
            Fill( 10,  4, 108, 38, BMtBs);
            Fill( 10,  4, 108,  1, BMtEd); // north edge highlight
            Fill( 10,  4,   1, 38, BMtEd); // west edge highlight
            Fill( 10, 22, 108,  1, BMtSm); // horizontal seam

            // ── Pillow (x=12..38, y=7..36) ───────────────────────────────────────────
            Fill( 12,  7, 27, 30, BPlCs);  // pillowCase
            Fill( 13,  8, 25, 28, BPlBs);  // pillow body
            Fill( 13,  8, 25,  1, BPlHi);  // top highlight
            Fill( 13,  8,  1, 28, BPlHi);  // left highlight
            Fill( 13, 35, 25,  1, BPlLo);  // bottom shadow
            Fill( 37,  8,  1, 28, BPlLo);  // right shadow
            Fill( 14, 21, 23,  1, BPlSm);  // seam

            // ── Blanket (x=41..117, y=4..41) ─────────────────────────────────────────
            Fill( 41,  4,  77, 38, BBlBs);
            Fill( 41,  4,  77,  1, BBlHi);  // top cuff
            Fill( 41,  4,   1, 38, BBlHi);  // left cuff edge
            Fill( 41, 41,  77,  1, BBlLo);  // bottom shadow
            Fill(117,  4,   1, 38, BBlTk);  // right tuck
            Fill( 42,  6,  75,  1, BBlMd);  // fold band
            Fill( 60,  5,   1, 36, BBlWk);  // wrinkle
            Fill( 80,  5,   1, 36, BBlWk);  // wrinkle
            Fill(100,  5,   1, 36, BBlWk);  // wrinkle

            return MakeSprite128(p);
        }

        // ── Low-level pixel helpers ───────────────────────────────────────────

        static Color32[] NewPixels() => new Color32[64 * 64];

        // Clear rect — sets pixels to fully transparent.
        static void Cl(Color32[] p, int x, int y, int w, int h)
        {
            Color32 t = default;
            for (int py = y; py < y + h; py++)
                for (int px = x; px < x + w; px++)
                    p[(63 - py) * 64 + px] = t;
        }

        // Fill rect — with y-flip so canvas y=0 (top) maps to Unity y=63 (top of sprite).
        static void Fr(Color32[] p, int x, int y, int w, int h, Color32 c)
        {
            for (int py = y; py < y + h; py++)
                for (int px = x; px < x + w; px++)
                    p[(63 - py) * 64 + px] = c;
        }

        // Single pixel — same y-flip.
        static void Px(Color32[] p, int x, int y, Color32 c) =>
            p[(63 - y) * 64 + x] = c;

        static Sprite MakeSprite(Color32[] pixels)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex,
                new Rect(0, 0, 64, 64),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 64);
        }

        // 128×64 battery bank sprite — 2 world units wide, 1 tall (64 PPU).
        static Sprite MakeBattery128()
        {
            // Spec: BatteryTilesheet.html  — 128×64, 3-box: left cell | right cell | terminal
            var p = new Color32[128 * 64];
            void Fill(int x, int y, int w, int h, Color32 c)
            {
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int bx = x + dx, by = y + dy;
                    if ((uint)bx < 128 && (uint)by < 64)
                        p[(63 - by) * 128 + bx] = c;
                }
            }
            void Dot(int x, int y, Color32 c)
            {
                if ((uint)x < 128 && (uint)y < 64)
                    p[(63 - y) * 128 + x] = c;
            }

            // Spec palette (BatteryTilesheet.html)
            var tBase   = C("#2d3040");  var tHi    = C("#424858");
            var tLo     = C("#1c1f2c");  var tGrout = C("#1a1d28");
            var fBase   = C("#383c50");  var fLit   = C("#434860");
            var fDark   = C("#252838");  var fBevel = C("#4e5470");
            var rHi     = C("#565e7a");  var rLo    = C("#2c3048");
            var acc     = C("#4880aa");  var accG   = C("#111e30");
            var accD    = C("#1a3a60");
            var cellFil = C("#303445");  var ventSl = C("#111520");
            var finHi   = C("#363b50");
            var termPos = C("#c8a830");  var termPHi= C("#e8c848");  var termPLo= C("#7a6418");
            var barBlue = C("#4890c8");  var barDim = C("#112038");
            var stGreen = C("#30c848");  var stGrGlo= C("#106018");  var stGrDim= C("#081c0a");

            const int FH = 18, TS = 46;  // south face height, top surface height

            // ── Outer housing ──────────────────────────────────────────────────────────
            Fill(  0,  0, 128, 64, tGrout);
            Fill(  1,  1, 126, 62, tBase);
            Fill(  2,  1, 124,  1, tHi);    // top edge highlight
            Fill(  1,  2,   1, 60, tHi);    // left edge highlight
            Fill(126,  2,   1, 60, tLo);    // right edge shadow
            Fill(  2, 62, 124,  1, tLo);    // bottom edge shadow

            // ── South face (y=TS..63) ──────────────────────────────────────────────────
            Fill(  2, TS, 124, FH, fDark);
            Fill(  2, TS, 124,  1, fBase);  // lip
            // 5-pip charge bar (all lit = full charge)
            for (int l = 0; l < 5; l++)
                Fill(8 + l * 11, TS + 5, 8, 6, barBlue);
            for (int l = 0; l < 5; l++)
                Dot(12 + l * 11, TS + 8, LerpC32(barBlue, new Color32(255,255,255,255), 0.3f));

            // ── Box 1: Left cell (x=4..59) ────────────────────────────────────────────
            const int DIV = 63;
            Fill(4, 4, DIV - 8, TS - 8, fDark);
            Fill(5, 5, DIV - 10, TS - 10, cellFil);
            Fill(5, 5, DIV - 10,  1, fLit);   // bevel top
            Fill(5, 5,  1, TS - 10, fLit);    // bevel left
            Fill(5, TS - 6, DIV - 10, 1, tLo);  // bevel bottom
            Fill(DIV - 5, 5, 1, TS - 10, tLo);  // bevel right
            // Vent slots
            for (int v = 0; v < 3; v++) Fill(8 + v * 14, 7, 8, 4, ventSl);
            // Blue status LED (top-right of box 1)
            int lx1 = DIV - 12, ly1 = 7;
            Fill(lx1-1, ly1-1, 6, 1, accG);  Fill(lx1-1, ly1+4, 6, 1, accG);
            Fill(lx1-1, ly1-1, 1, 6, accG);  Fill(lx1+4, ly1-1, 1, 6, accG);
            Fill(lx1, ly1, 4, 4, accD);
            Dot(lx1, ly1, acc);  Dot(lx1+1, ly1, LerpC32(acc, accD, 0.4f));
            Dot(lx1, ly1+1, LerpC32(acc, accD, 0.4f));  Dot(lx1+1, ly1+1, accD);
            // Rivets
            Fill(4, 4, 2, 1, rHi); Fill(4, 5, 2, 1, rLo);
            Fill(DIV - 6, 4, 2, 1, rHi); Fill(DIV - 6, 5, 2, 1, rLo);

            // ── Divider (x=63..64) ────────────────────────────────────────────────────
            Fill(DIV, 4, 2, TS - 4, tGrout);
            Fill(DIV + 1, 5, 1, TS - 6, tHi);

            // ── Box 2: Right cell (x=66..97) ──────────────────────────────────────────
            const int B2X0 = 66, B2W = 30;
            Fill(B2X0, 4, B2W, TS - 8, fDark);
            Fill(B2X0 + 1, 5, B2W - 2, TS - 10, cellFil);
            Fill(B2X0 + 1, 5, B2W - 2, 1, fLit);
            Fill(B2X0 + 1, 5, 1, TS - 10, fLit);
            Fill(B2X0 + 1, TS - 6, B2W - 2, 1, tLo);
            Fill(B2X0 + B2W - 1, 5, 1, TS - 10, tLo);
            for (int v = 0; v < 2; v++) Fill(B2X0 + 4 + v * 13, 7, 8, 4, ventSl);  // vents
            Fill(B2X0, TS / 2 - 1, B2W, 2, finHi);  // separator fin

            // ── Box 3: Terminal block (x=99..126) ────────────────────────────────────
            const int B3X0 = 99;
            Fill(B3X0,   2, 27, 60, fDark);
            Fill(B3X0+1, 3, 25, 58, fBase);
            Fill(B3X0+1, 3, 25,  1, fBevel);  // top highlight
            Fill(B3X0+1, 3,  1, 58, fBevel);  // left highlight
            Fill(B3X0+25, 3, 1, 58, tLo);     // right shadow
            Fill(B3X0+1, 60, 25, 1, tLo);     // bottom shadow
            // Positive terminal post
            Fill(B3X0 + 5,  6, 14,  6, termPos);
            Fill(B3X0 + 6,  6, 12,  1, termPHi);
            Fill(B3X0 + 5,  6,  1,  6, termPHi);
            Fill(B3X0 + 18, 6,  1,  6, termPLo);
            Fill(B3X0 + 6, 11, 12,  1, termPLo);
            // Status LED — green (connected + drawing power)
            int lx3 = B3X0 + 10, ly3 = TS / 2 - 2;
            Fill(lx3-1, ly3-1, 6, 1, stGrDim); Fill(lx3-1, ly3+4, 6, 1, stGrDim);
            Fill(lx3-1, ly3-1, 1, 6, stGrDim); Fill(lx3+4, ly3-1, 1, 6, stGrDim);
            Fill(lx3, ly3, 4, 4, stGrGlo);
            Dot(lx3,   ly3,   stGreen);
            Dot(lx3+1, ly3,   LerpC32(stGreen, stGrGlo, 0.4f));
            Dot(lx3,   ly3+1, LerpC32(stGreen, stGrGlo, 0.4f));
            Dot(lx3+1, ly3+1, stGrGlo);
            // Rivets
            Fill(B3X0+1, 3, 2, 1, rHi); Fill(B3X0+1, 4, 2, 1, rLo);
            Fill(B3X0+23, 3, 2, 1, rHi); Fill(B3X0+23, 4, 2, 1, rLo);

            return MakeSprite128(p);
        }

        /// Creates a 128×64 sprite at 64 PPU → 2 world units wide, 1 tall.
        static Sprite MakeSprite128(Color32[] pixels)
        {
            var tex = new Texture2D(128, 64, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex,
                new Rect(0, 0, 128, 64),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 64);
        }

        static Color32 C(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                return (Color32)c;
            return new Color32(255, 0, 255, 255);
        }
    }
}
