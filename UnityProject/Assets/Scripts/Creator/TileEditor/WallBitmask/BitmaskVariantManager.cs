using System;

namespace Waystation.Creator.TileEditor.WallBitmask
{
    [Flags]
    public enum BitmaskDirection
    {
        None  = 0,
        North = 1,
        South = 2,
        East  = 4,
        West  = 8
    }

    public static class BitmaskVariantManager
    {
        public static readonly string[] VariantLabels = new string[]
        {
            "0: None",    "1: N",     "2: S",     "3: NS",
            "4: E",       "5: NE",    "6: SE",    "7: NSE",
            "8: W",       "9: NW",   "10: SW",   "11: NSW",
           "12: EW",     "13: NEW",  "14: SEW",  "15: NSEW"
        };

        public static int GetOppositeVariant(int variant)
        {
            int result = 0;
            if ((variant & 1) != 0) result |= 2; // N -> S
            if ((variant & 2) != 0) result |= 1; // S -> N
            if ((variant & 4) != 0) result |= 8; // E -> W
            if ((variant & 8) != 0) result |= 4; // W -> E
            return result;
        }

        public static BitmaskDirection GetDirections(int variant)
        {
            return (BitmaskDirection)variant;
        }

        public static bool HasDirection(int variant, BitmaskDirection dir)
        {
            return ((BitmaskDirection)variant & dir) != 0;
        }

        /// Returns the 4x4 grid position (col, row) for a variant index
        public static (int col, int row) GetGridPosition(int variant)
        {
            return (variant % 4, variant / 4);
        }

        /// Returns the variant index for a grid position
        public static int GetVariantFromGrid(int col, int row)
        {
            return row * 4 + col;
        }
    }
}
