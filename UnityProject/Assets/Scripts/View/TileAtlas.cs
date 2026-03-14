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
            if (buildableId.Contains("storage_cabinet")) return GetCabinet(rotation);
            if (buildableId.Contains("battery"))         return GetBattery();
            if (buildableId.Contains("door"))            return GetDoorHFrames()[0];
            if (buildableId.Contains("wall"))            return GetWall(0);
            return GetFloor(0);
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

        private static Dictionary<string, Sprite[]> _doorCache2;

        /// Get door animation frames.
        /// isH=true  → doorS style: panels slide W↔E, passage runs N-S  (use for H walls).
        /// isH=false → doorE style: panels slide N↔S, passage runs E-W  (use for V walls).
        /// status: "powered" | "locked" | "unpowered".
        /// dmgLevel: 0=normal (animated), 1=worn (closed, faded glow), 2=broken (stuck open).
        public static Sprite[] GetDoorFrames(bool isH, string status = "powered", int dmgLevel = 0)
        {
            if (_doorCache2 == null) _doorCache2 = new Dictionary<string, Sprite[]>();
            string key = $"{(isH ? 'H' : 'V')}_{status}_{dmgLevel}";
            if (!_doorCache2.TryGetValue(key, out var frames))
            {
                frames = new Sprite[5];
                float[] amts = { 0f, 0.25f, 0.5f, 0.75f, 1.0f };
                for (int i = 0; i < 5; i++)
                    frames[i] = isH ? MakeDoorS(amts[i], status, dmgLevel)
                                    : MakeDoorE(amts[i], status, dmgLevel);
                _doorCache2[key] = frames;
            }
            return frames;
        }

        private static Sprite[] _doorHFrames;
        private static Sprite[] _doorVFrames;

        /// Five animation frames for a horizontal door (panels slide W↔E).
        public static Sprite[] GetDoorHFrames() => GetDoorFrames(true);
        /// Five animation frames for a vertical door (panels slide N↔S).
        public static Sprite[] GetDoorVFrames() => GetDoorFrames(false);

        // ── MakeDoorS — horizontal door (panels slide W↔E, passage runs N-S) ──────
        // Matches HTML topClosed / topOpening / topOpen layout exactly.
        // open: 0=closed, 1=fully open.
        static Sprite MakeDoorS(float open, string status, int dmgLevel)
        {
            if (dmgLevel == 1) open = 0f;
            if (dmgLevel == 2) open = 1f;

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

        // ── MakeDoorE — vertical door perspective tile (passage runs N-S, wall runs E-W) ─
        // Matches HTML mkPersp layout exactly:
        //   [LEFT FACE FL=16px][TOP SURFACE FS=32px][RIGHT FACE FR=16px] = 64px
        // Left face  (x=0..15):  bright outer-left → dark inner-right, door panel content
        // Top surface (x=16..47): single recessed-panel wall top + frame rails top+bottom,
        //                          panels slide N↔S (y-axis), void gap grows as door opens
        // Right face (x=48..63): dark inner-left → bright outer-right (mirror of left face)
        static Sprite MakeDoorE(float open, string status, int dmgLevel)
        {
            if (dmgLevel == 1) open = 0f;
            if (dmgLevel == 2) open = 1f;

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
            var p = new Color32[128 * 64];
            void Fill(int x, int y, int w, int h, Color32 c)
            {
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    p[(63 - (y + dy)) * 128 + (x + dx)] = c;
            }
            void Dot(int x, int y, Color32 c) => p[(63 - y) * 128 + x] = c;

            var housing = C("#2b3040");
            var edgeLt  = C("#424b5c");
            var edgeDk  = C("#181c26");
            var cell    = C("#1c2820");
            var cellLt  = C("#273221");
            var cellDk  = C("#111915");
            var term    = C("#b08020");
            var termHi  = C("#d4a030");
            var ledOn   = C("#18d050");
            var vent    = C("#141820");
            var div     = C("#1c2028");
            var pipGlow = new Color32(40, 255, 120, 255);

            // ── Outer housing ────────────────────────────────────────────────
            Fill(0,   0, 128, 64, edgeDk);
            Fill(1,   1, 126, 62, housing);
            Fill(2,   1, 124,  1, edgeLt);   // top highlight
            Fill(1,   2,   1, 60, edgeLt);   // left highlight
            Fill(126, 2,   1, 60, edgeDk);   // right shadow
            Fill(2,  62, 124,  1, edgeDk);   // bottom shadow

            // ── Left cell bay (x:4–60, y:4–58) ──────────────────────────────
            Fill(4,  4, 57, 55, cell);
            Fill(5,  4, 55,  1, cellLt);     // top highlight
            Fill(4,  5,  1, 53, cellLt);     // left highlight
            Fill(60, 5,  1, 53, cellDk);     // right shadow
            Fill(5, 58, 55,  1, cellDk);     // bottom shadow

            // ── Right cell bay (x:67–123, y:4–58) ───────────────────────────
            Fill(67,  4, 57, 55, cell);
            Fill(68,  4, 55,  1, cellLt);
            Fill(67,  5,  1, 53, cellLt);
            Fill(123, 5,  1, 53, cellDk);
            Fill(68, 58, 55,  1, cellDk);

            // ── Centre divider ───────────────────────────────────────────────
            Fill(61, 4, 6, 55, div);
            Fill(62, 4, 1, 55, edgeLt);      // divider left highlight

            // ── Vent slots ───────────────────────────────────────────────────
            for (int v = 0; v < 5; v++)
            {
                Fill( 8 + v * 10, 7, 4, 5, vent);
                Fill(71 + v * 10, 7, 4, 5, vent);
            }

            // ── LED charge indicators (5 per bay) ────────────────────────────
            for (int l = 0; l < 5; l++)
            {
                int lxL = 9  + l * 10;
                int lxR = 72 + l * 10;
                Fill(lxL, 51, 5, 3, ledOn);
                Dot(lxL + 2, 52, pipGlow);
                Fill(lxR, 51, 5, 3, ledOn);
                Dot(lxR + 2, 52, pipGlow);
            }

            // ── Terminal connectors (top) ─────────────────────────────────────
            int[] termXs = { 12, 44, 75, 107 };
            foreach (int tx in termXs)
            {
                Fill(tx, 2, 8, 3, term);
                Fill(tx + 1, 2, 6, 1, termHi);
            }

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
