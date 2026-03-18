// AsteroidMapGenerator — generates procedural asteroid maps using seeded
// cellular-automata smoothing.  Uses System.Random (not UnityEngine.Random)
// for determinism and thread-safety.
using System;
using Waystation.Models;

namespace Waystation.Systems
{
    public static class AsteroidMapGenerator
    {
        private const int MaxWidth  = 64;
        private const int MaxHeight = 64;

        // Initial fill percentages (out of 100)
        private const int PctRock  = 45;
        private const int PctOre   =  8;
        private const int PctIce   =  5;
        // Remaining percentage is empty.

        // Cellular-automata passes
        private const int SmoothPasses = 3;
        // A cell becomes rock if 5 or more of its 8 neighbours are rock.
        private const int RockThreshold = 5;

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Generate a new asteroid map.  Width and height are clamped to 64 each.
        /// The map borders are always filled with Wall tiles.
        /// </summary>
        public static AsteroidMapState Generate(
            string poiUid, string missionUid, int seed,
            int width = 48, int height = 48,
            int startTick = 0, int durationTicks = 480)
        {
            width  = Math.Min(width,  MaxWidth);
            height = Math.Min(height, MaxHeight);

            var map = AsteroidMapState.Create(poiUid, missionUid, seed,
                                              width, height, startTick, durationTicks);
            var rng = new System.Random(seed);

            // ── Phase 1: random fill ──────────────────────────────────────────
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width;  x++)
            {
                // Border is always wall.
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    map.SetTile(x, y, (byte)AsteroidTile.Wall);
                    continue;
                }

                int roll = rng.Next(100);
                byte tile;
                if      (roll < PctRock)             tile = (byte)AsteroidTile.Rock;
                else if (roll < PctRock + PctOre)    tile = (byte)AsteroidTile.Ore;
                else if (roll < PctRock + PctOre + PctIce) tile = (byte)AsteroidTile.Ice;
                else                                 tile = (byte)AsteroidTile.Empty;
                map.SetTile(x, y, tile);
            }

            // ── Phase 2: cellular-automata smoothing ──────────────────────────
            var buf = new byte[width * height];
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                // Copy current state into the read buffer.
                Array.Copy(map.tiles, buf, map.tiles.Length);

                for (int y = 1; y < height - 1; y++)
                for (int x = 1; x < width  - 1; x++)
                {
                    int  rockNeighbours = CountRockNeighbours(buf, x, y, width, height);
                    byte current = buf[y * width + x];

                    if (rockNeighbours >= RockThreshold)
                    {
                        // Cell becomes (or stays) rock.
                        map.SetTile(x, y, (byte)AsteroidTile.Rock);
                    }
                    else
                    {
                        // Cell opens up — preserve ore/ice only when surrounded by rock.
                        if (current == (byte)AsteroidTile.Ore ||
                            current == (byte)AsteroidTile.Ice)
                        {
                            // Keep special tile if at least 3 rock neighbours (embedded vein).
                            map.SetTile(x, y, rockNeighbours >= 3 ? current
                                                                   : (byte)AsteroidTile.Empty);
                        }
                        else
                        {
                            map.SetTile(x, y, (byte)AsteroidTile.Empty);
                        }
                    }
                }
            }

            // ── Phase 3: ensure borders remain walls ──────────────────────────
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width;  x++)
            {
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    map.SetTile(x, y, (byte)AsteroidTile.Wall);
            }

            return map;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static int CountRockNeighbours(byte[] buf, int cx, int cy,
                                               int width, int height)
        {
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                {
                    count++; // treat out-of-bounds as rock
                    continue;
                }
                byte t = buf[ny * width + nx];
                if (t == (byte)AsteroidTile.Rock  ||
                    t == (byte)AsteroidTile.Wall)
                    count++;
            }
            return count;
        }
    }
}
