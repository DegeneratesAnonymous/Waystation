// TileAtlas — procedural pixel-art tile sprites (64×64 px).
//
// Tile types:
//   FLOOR_0 … FLOOR_4  — five floor variants; pick randomly per cell, rotate freely.
//   WALL_0  … WALL_4   — five wall variants;  pick randomly per cell, NO rotation.
//
// Shadow overlays (semi-transparent, composited on top of floor tiles):
//   SHADOW_TOP / RIGHT / BOTTOM / LEFT — 16px alpha-fade from the named edge.
//
// Door animation:
//   GetDoorHFrames() / GetDoorVFrames() — 5-frame arrays (closed → open).
//   The open gap is transparent — render the floor tile first, composite door on top.
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
        // Shadow edge indices (cardinal)
        public const int SHADOW_TOP    = 0;
        public const int SHADOW_RIGHT  = 1;
        public const int SHADOW_BOTTOM = 2;
        public const int SHADOW_LEFT   = 3;
        // Shadow corner indices (diagonal)
        public const int SHADOW_TL     = 4; // top-left
        public const int SHADOW_TR     = 5; // top-right
        public const int SHADOW_BL     = 6; // bottom-left
        public const int SHADOW_BR     = 7; // bottom-right

        private static Sprite[] _cache;       // [0..9]  floor 0-4, wall 0-4
        private static Sprite[] _shadowCache; // [0..7]  4 edges + 4 corners

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
                _shadowCache = new Sprite[8];
                for (int i = 0; i < 8; i++) _shadowCache[i] = MakeShadow(i);
            }
            return _shadowCache[Mathf.Clamp(edge, 0, 7)];
        }

        private static void EnsureCache()
        {
            if (_cache != null) return;
            _cache = new Sprite[10];
            for (int i = 0; i < 5; i++) _cache[i]     = MakeFloor(i);
            for (int i = 0; i < 5; i++) _cache[5 + i] = MakeWall(i);
        }

        // ── Wall palette ──────────────────────────────────────────────────────
        static readonly Color32 WBase    = C("#4e5563");
        static readonly Color32 WSeamDk  = C("#363c48");
        static readonly Color32 WSeamLt  = C("#5c6370");
        static readonly Color32 WScuffDk = C("#3e4450");
        static readonly Color32 WScuffLt = C("#5a6070");

        private static readonly (int x, int y, char dir, int len)[][] WallScuffSets =
        {
            new (int, int, char, int)[0],                                              // W0 — clean
            new[] { (14, 28, 'h', 5) },                                                // W1
            new[] { (38, 18, 'd', 4), (12, 48, 'h', 3) },                             // W2
            new[] { (10, 40, 'h', 4), (44, 22, 'h', 3) },                             // W3
            new[] { (20, 22, 'd', 3), (36, 46, 'h', 4) },                             // W4
        };

        // Full 64×64 solid wall tile.  Five scuff variants; do NOT rotate.
        // Top and left edges carry a 2px seam (dark + light) to read as a lit corner.
        static Sprite MakeWall(int variant)
        {
            var p = NewPixels();
            Fr(p, 0, 0, 64, 64, WBase);
            // Top seam
            Fr(p, 0, 0, 64, 1, WSeamDk);
            Fr(p, 0, 1, 64, 1, WSeamLt);
            // Left seam
            Fr(p, 0, 0, 1, 64, WSeamDk);
            Fr(p, 1, 0, 1, 64, WSeamLt);
            foreach (var (sx, sy, dir, len) in WallScuffSets[variant])
                for (int i = 0; i < len; i++)
                {
                    int cx = (dir == 'h' || dir == 'd') ? sx + i : sx;
                    int cy = (dir == 'v' || dir == 'd') ? sy + i : sy;
                    if (cx < 2 || cy < 2) continue; // preserve seam
                    Px(p, cx,     cy, WScuffDk);
                    Px(p, cx + 1, cy, WScuffLt);
                }
            return MakeSprite(p);
        }

        // Semi-transparent shadow overlay — 16px alpha-gradient from the named edge or corner.
        // Composited on top of floor tiles that are adjacent to a wall.
        static Sprite MakeShadow(int edge)
        {
            var p = NewPixels(); // starts fully transparent
            const int   DEPTH = 16;
            const float MAX_A = 0.52f;

            if (edge < 4)
            {
                // Cardinal edge — full-width gradient from one side
                for (int i = 0; i < DEPTH; i++)
                {
                    float   t  = 1f - (float)i / DEPTH;
                    byte    ab = (byte)(MAX_A * t * 255f);
                    Color32 c  = new Color32(0, 0, 0, ab);
                    switch (edge)
                    {
                        case SHADOW_TOP:    Fr(p, 0, i,      64, 1,  c); break;
                        case SHADOW_BOTTOM: Fr(p, 0, 63 - i, 64, 1,  c); break;
                        case SHADOW_RIGHT:  for (int y = 0; y < 64; y++) Px(p, 63 - i, y, c); break;
                        case SHADOW_LEFT:   for (int y = 0; y < 64; y++) Px(p, i,      y, c); break;
                    }
                }
            }
            else
            {
                // Diagonal corner — Chebyshev-distance fade from the nearest tile corner.
                // Canvas coords: y=0 = TOP, y=63 = BOTTOM, x=0 = LEFT, x=63 = RIGHT.
                const float CORNER_A = MAX_A * 0.65f;
                for (int ix = 0; ix < DEPTH; ix++)
                for (int iy = 0; iy < DEPTH; iy++)
                {
                    float t = 1f - (float)Mathf.Max(ix, iy) / DEPTH;
                    if (t <= 0f) continue;
                    byte    ab = (byte)(CORNER_A * t * 255f);
                    Color32 c  = new Color32(0, 0, 0, ab);
                    switch (edge)
                    {
                        case SHADOW_TL: Px(p, ix,      iy,      c); break;
                        case SHADOW_TR: Px(p, 63 - ix, iy,      c); break;
                        case SHADOW_BL: Px(p, ix,      63 - iy, c); break;
                        case SHADOW_BR: Px(p, 63 - ix, 63 - iy, c); break;
                    }
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

        // ── Door tile generators ──────────────────────────────────────────────
        // 8px transparent margin on sides perpendicular to travel direction.
        // 4px bevelled frame on sides parallel to travel direction.
        // Transparent gap reveals the floor tile rendered beneath.
        static readonly Color32 DFrameBg    = C("#2e333e");
        static readonly Color32 DFrameEdge  = C("#454d5c");
        static readonly Color32 DFrameLip   = C("#5a6272");
        static readonly Color32 DFrameInner = C("#272c36");
        static readonly Color32 DPanel      = C("#3d4555");
        static readonly Color32 DTrack      = C("#141a27");
        static readonly Color32 DGlow       = C("#2a7a9a");

        private static Sprite[] _doorHFrames;
        private static Sprite[] _doorVFrames;

        /// Five animation frames for a horizontal door (panels slide left↔right).
        /// Frame 0 = closed, frame 4 = fully open.
        public static Sprite[] GetDoorHFrames()
        {
            if (_doorHFrames != null) return _doorHFrames;
            _doorHFrames = new Sprite[5];
            float[] amts = { 0f, 0.25f, 0.5f, 0.75f, 1.0f };
            for (int i = 0; i < 5; i++) _doorHFrames[i] = MakeDoorH(amts[i]);
            return _doorHFrames;
        }

        /// Five animation frames for a vertical door (panels slide up↔down).
        public static Sprite[] GetDoorVFrames()
        {
            if (_doorVFrames != null) return _doorVFrames;
            _doorVFrames = new Sprite[5];
            float[] amts = { 0f, 0.25f, 0.5f, 0.75f, 1.0f };
            for (int i = 0; i < 5; i++) _doorVFrames[i] = MakeDoorV(amts[i]);
            return _doorVFrames;
        }

        // Horizontal door: 8px transparent top/bottom margin; 4px side-frames on left+right.
        // Panels slide left↔right within the 56px interior (x=4..59).
        // Track is horizontal, interior-width only. Gap clears to transparent.
        static Sprite MakeDoorH(float openAmt)
        {
            const int MARGIN = 8, FRAME = 4;
            int y0 = MARGIN, h = 64 - MARGIN * 2; // active zone: y=8, h=48

            var p = NewPixels(); // fully transparent by default

            // Left side frame (4 cols, active zone height)
            Fr(p, 0,  y0, 1, h, DFrameEdge);
            Fr(p, 1,  y0, 1, h, DFrameLip);
            Fr(p, 2,  y0, 1, h, DFrameInner);
            Fr(p, 3,  y0, 1, h, DFrameBg);
            // Right side frame
            Fr(p, 63, y0, 1, h, DFrameEdge);
            Fr(p, 62, y0, 1, h, DFrameLip);
            Fr(p, 61, y0, 1, h, DFrameInner);
            Fr(p, 60, y0, 1, h, DFrameBg);
            // Interior background (x=4..59, w=56)
            int activeW = 64 - FRAME * 2;  // 56
            Fr(p, FRAME, y0, activeW, h, DFrameBg);
            // Horizontal track (interior width, centre of active zone)
            Fr(p, FRAME, y0 + h / 2 - 1, activeW, 2, DTrack);

            int maxW = activeW / 2;  // 28
            int panW = Mathf.RoundToInt(maxW - (maxW - 3) * openAmt); // 28→3

            // Left panel (starts at FRAME)
            Fr(p, FRAME, y0, panW, h, DPanel);
            if (panW > 2) Fr(p, FRAME + panW - 1, y0 + 1, 1, h - 2, DGlow);
            // Right panel
            int rpx = FRAME + activeW - panW; // 60 - panW
            Fr(p, rpx, y0, panW, h, DPanel);
            if (panW > 2) Fr(p, rpx, y0 + 1, 1, h - 2, DGlow);

            // Transparent gap with teal edge fringe
            if (openAmt > 0f)
            {
                int gapX = FRAME + panW, gapW = rpx - gapX;
                if (gapW > 0)
                {
                    Cl(p, gapX, y0, gapW, h);
                    for (int i = 0; i < Mathf.Min(4, gapW); i++)
                    {
                        byte    ab = (byte)(0.13f * (4 - i) / 4f * 255f);
                        Color32 gc = new Color32(42, 122, 154, ab);
                        for (int y = y0; y < y0 + h; y++)
                        {
                            Px(p, gapX + i,            y, gc);
                            Px(p, gapX + gapW - 1 - i, y, gc);
                        }
                    }
                }
            }
            return MakeSprite(p);
        }

        // Vertical door: 8px transparent left/right margin; 4px top/bottom frames.
        // Panels slide up↔down within the 56px interior (y=4..59).
        // Track is vertical, interior-height only. Gap clears to transparent.
        static Sprite MakeDoorV(float openAmt)
        {
            const int MARGIN = 8, FRAME = 4;
            int x0 = MARGIN, w = 64 - MARGIN * 2; // active zone: x=8, w=48

            var p = NewPixels(); // fully transparent by default

            // Top frame (4 rows, active zone width)
            Fr(p, x0, 0,  w, 1, DFrameEdge);
            Fr(p, x0, 1,  w, 1, DFrameLip);
            Fr(p, x0, 2,  w, 1, DFrameInner);
            Fr(p, x0, 3,  w, 1, DFrameBg);
            // Bottom frame
            Fr(p, x0, 63, w, 1, DFrameEdge);
            Fr(p, x0, 62, w, 1, DFrameLip);
            Fr(p, x0, 61, w, 1, DFrameInner);
            Fr(p, x0, 60, w, 1, DFrameBg);
            // Interior background (y=4..59, h=56)
            int activeH = 64 - FRAME * 2; // 56
            Fr(p, x0, FRAME, w, activeH, DFrameBg);
            // Vertical track (interior height, centre of active zone)
            Fr(p, x0 + w / 2 - 1, FRAME, 2, activeH, DTrack);

            int maxH = activeH / 2; // 28
            int panH = Mathf.RoundToInt(maxH - (maxH - 3) * openAmt); // 28→3

            // Top panel
            Fr(p, x0, FRAME, w, panH, DPanel);
            if (panH > 2) Fr(p, x0 + 1, FRAME + panH - 1, w - 2, 1, DGlow);
            // Bottom panel
            int bpy = FRAME + activeH - panH; // 60 - panH
            Fr(p, x0, bpy, w, panH, DPanel);
            if (panH > 2) Fr(p, x0 + 1, bpy, w - 2, 1, DGlow);

            // Transparent gap with teal edge fringe
            if (openAmt > 0f)
            {
                int gapY = FRAME + panH, gapH = bpy - gapY;
                if (gapH > 0)
                {
                    Cl(p, x0, gapY, w, gapH);
                    for (int i = 0; i < Mathf.Min(4, gapH); i++)
                    {
                        byte    ab = (byte)(0.13f * (4 - i) / 4f * 255f);
                        Color32 gc = new Color32(42, 122, 154, ab);
                        for (int x = x0; x < x0 + w; x++)
                        {
                            Px(p, x, gapY + i,            gc);
                            Px(p, x, gapY + gapH - 1 - i, gc);
                        }
                    }
                }
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

        static Color32 C(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                return (Color32)c;
            return new Color32(255, 0, 255, 255);
        }
    }
}
