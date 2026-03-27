using UnityEngine;

[CreateAssetMenu(menuName = "Wall/Wall Atlas Data", fileName = "WallAtlasData")]
public class WallAtlasData : ScriptableObject
{
    [Header("Base tile (layer 0) — opaque, direction-agnostic")]
    public Sprite baseSprite;               // wall_solid_normal  col 0

    [Header("Shadow tile — drawn one tile south on the floor")]
    public Sprite shadowSprite;             // wall_shadow        (shadow atlas)

    [Header("Interior overlays (layer 1) — transparent, bitmask-selected)")]
    // Array indexed directly by bitmask value 1..15.
    // Index 0 is unused (no interior faces = no overlay).
    // Populate in the Inspector in bitmask order:
    //   [1]  = int_w      W
    //   [2]  = int_e      E
    //   [3]  = int_ew     E+W
    //   [4]  = int_s      S          ← face strip present
    //   [5]  = int_sw     S+W        ← face strip present
    //   [6]  = int_se     S+E        ← face strip present
    //   [7]  = int_sew    S+E+W      ← face strip present
    //   [8]  = int_n      N
    //   [9]  = int_nw     N+W
    //  [10]  = int_ne     N+E
    //  [11]  = int_new    N+E+W
    //  [12]  = int_ns     N+S        ← face strip present
    //  [13]  = int_nsw    N+S+W      ← face strip present
    //  [14]  = int_nse    N+S+E      ← face strip present
    //  [15]  = int_nsew   N+S+E+W    ← face strip present
    [Tooltip("Size must be 16. Index = bitmask value. Index 0 unused (leave null).")]
    public Sprite[] overlaySprites = new Sprite[16];

    /// <summary>
    /// Returns the correct overlay sprite for a given interior-neighbour bitmask,
    /// or null if no overlay is needed (bitmask == 0).
    /// </summary>
    public Sprite GetOverlay(int bitmask)
    {
        if (bitmask <= 0 || bitmask >= overlaySprites.Length) return null;
        return overlaySprites[bitmask];
    }

    /// <summary>
    /// Returns true when the south face strip should be visible,
    /// i.e. bit 2 (S) is set in the bitmask.
    /// </summary>
    public static bool HasSouthInterior(int bitmask) => (bitmask & 4) != 0;
}
