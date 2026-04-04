using UnityEngine;

namespace Waystation.Creator.TileEditor.WallBitmask
{
    public static class JunctionResolver
    {
        /// Resolves the center junction pixel where arms overlap.
        /// Vertical priority: vertical arm pixels override horizontal arm pixels at the junction.
        public static void ResolveJunction(
            Color32[] dest, int w, int h,
            Color32[] nsPixels, Color32[] ewPixels,
            int bitmask)
        {
            int halfW = w / 2;
            int halfH = h / 2;
            int junctionSize = Mathf.Max(halfW, halfH);

            // Junction is the center overlap region
            int jx0 = halfW / 2;
            int jy0 = halfH / 2;
            int jx1 = w - halfW / 2;
            int jy1 = h - halfH / 2;

            bool hasVertical = (bitmask & 3) != 0; // N or S
            bool hasHorizontal = (bitmask & 12) != 0; // E or W

            if (hasVertical && hasHorizontal)
            {
                // In overlap zone, vertical gets priority
                for (int y = jy0; y < jy1; y++)
                {
                    for (int x = jx0; x < jx1; x++)
                    {
                        Color32 vPx = new Color32(0, 0, 0, 0);
                        if (nsPixels != null && y * w + x < nsPixels.Length)
                            vPx = nsPixels[y * w + x];

                        if (vPx.a > 0)
                            dest[y * w + x] = vPx;
                    }
                }
            }
        }
    }
}
