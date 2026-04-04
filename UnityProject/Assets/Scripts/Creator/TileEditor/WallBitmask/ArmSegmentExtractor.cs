using UnityEngine;

namespace Waystation.Creator.TileEditor.WallBitmask
{
    public static class ArmSegmentExtractor
    {
        /// Extracts the north arm segment from a full tile (top half of vertical centerline)
        public static Color32[] ExtractNorthArm(Color32[] tilePixels, int w, int h)
        {
            int halfH = h / 2;
            var arm = new Color32[w * halfH];
            for (int y = 0; y < halfH; y++)
                for (int x = 0; x < w; x++)
                    arm[y * w + x] = tilePixels[y * w + x];
            return arm;
        }

        /// Extracts the south arm segment (bottom half)
        public static Color32[] ExtractSouthArm(Color32[] tilePixels, int w, int h)
        {
            int halfH = h / 2;
            var arm = new Color32[w * halfH];
            for (int y = halfH; y < h; y++)
                for (int x = 0; x < w; x++)
                    arm[(y - halfH) * w + x] = tilePixels[y * w + x];
            return arm;
        }

        /// Extracts the east arm segment (right half)
        public static Color32[] ExtractEastArm(Color32[] tilePixels, int w, int h)
        {
            int halfW = w / 2;
            var arm = new Color32[halfW * h];
            for (int y = 0; y < h; y++)
                for (int x = halfW; x < w; x++)
                    arm[y * halfW + (x - halfW)] = tilePixels[y * w + x];
            return arm;
        }

        /// Extracts the west arm segment (left half)
        public static Color32[] ExtractWestArm(Color32[] tilePixels, int w, int h)
        {
            int halfW = w / 2;
            var arm = new Color32[halfW * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < halfW; x++)
                    arm[y * halfW + x] = tilePixels[y * w + x];
            return arm;
        }

        /// Composites arms onto a blank tile based on bitmask
        public static void CompositeArms(
            Color32[] dest, int w, int h,
            Color32[] northArm, Color32[] southArm,
            Color32[] eastArm, Color32[] westArm,
            int bitmask)
        {
            int halfW = w / 2;
            int halfH = h / 2;

            if ((bitmask & 1) != 0 && northArm != null) // North
            {
                for (int y = 0; y < halfH; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var px = northArm[y * w + x];
                        if (px.a > 0) dest[(y + halfH) * w + x] = px;
                    }
            }

            if ((bitmask & 2) != 0 && southArm != null) // South
            {
                for (int y = 0; y < halfH; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var px = southArm[y * w + x];
                        if (px.a > 0) dest[y * w + x] = px;
                    }
            }

            if ((bitmask & 4) != 0 && eastArm != null) // East
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < halfW; x++)
                    {
                        var px = eastArm[y * halfW + x];
                        if (px.a > 0) dest[y * w + (x + halfW)] = px;
                    }
            }

            if ((bitmask & 8) != 0 && westArm != null) // West
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < halfW; x++)
                    {
                        var px = westArm[y * halfW + x];
                        if (px.a > 0) dest[y * w + x] = px;
                    }
            }
        }
    }
}
