// ═══════════════════════════════════════════════════════════════════════════════
// WallAtlasData.cs
// ─────────────────────────────────────────────────────────────────────────────
// Two files in one — split at the marked divider and place each in your project.
//
// FILE 1 → Assets/Scripts/World/WallAtlasData.cs
//   ScriptableObject holding all sprite references and the bitmask→overlay lookup.
//
// FILE 2 → Assets/Scripts/World/WallRenderer.cs
//   MonoBehaviour that reads neighbour state and drives the two SpriteRenderers.
//
// SETUP SUMMARY
// ─────────────────────────────────────────────────────────────────────────────
// 1. Import wall_atlas.png with Sprite Mode = Multiple, Filter = Point,
//    Compression = None, PPU = 64.  The .meta file slices it automatically.
//
// 2. Create a WallAtlasData asset:
//    Right-click in Project → Create → Wall → Wall Atlas Data
//    Drag sprites from wall_atlas into the matching fields.
//
// 3. On each wall GameObject:
//    - Add a SpriteRenderer named "Base"   (sorting layer Wall, order 0)
//    - Add a SpriteRenderer named "Overlay" (sorting layer Wall, order 1)
//    - Add a SpriteRenderer named "Shadow"  (sorting layer Floor, order 10)
//      on a child positioned one tile south: localPosition = (0, -1, 0)
//    - Add WallRenderer, assign the WallAtlasData asset.
//
// BITMASK ENCODING
// ─────────────────────────────────────────────────────────────────────────────
//   bit 3 (8) = North neighbour is interior space
//   bit 2 (4) = South neighbour is interior space   ← triggers face strip
//   bit 1 (2) = East  neighbour is interior space
//   bit 0 (1) = West  neighbour is interior space
//
//   "Interior space" = a floor tile (not another wall) that is inside a room.
//   Exterior open space does NOT count — shadows face outward, not inward.
//
// RENDER ORDER (per tile position)
// ─────────────────────────────────────────────────────────────────────────────
//   1. Shadow sprite   drawn on floor tile to the south  (sorting: Floor/10)
//   2. Base sprite     drawn at wall position            (sorting: Wall/0)
//   3. Overlay sprite  drawn at wall position            (sorting: Wall/1)
// ═══════════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────────────
// FILE 1 — WallAtlasData.cs
// ─────────────────────────────────────────────────────────────────────────────
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


// ─────────────────────────────────────────────────────────────────────────────
// FILE 2 — WallRenderer.cs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Drives the two-layer wall sprite system.
/// Attach to each wall tile GameObject alongside two SpriteRenderers
/// (named "Base" and "Overlay") and one shadow SpriteRenderer on a south child.
/// </summary>
public class WallRenderer : MonoBehaviour
{
    [Header("Atlas Data")]
    public WallAtlasData atlasData;

    [Header("Sprite Renderers")]
    [Tooltip("SpriteRenderer for the opaque base tile.")]
    public SpriteRenderer baseRenderer;

    [Tooltip("SpriteRenderer for the transparent interior overlay.")]
    public SpriteRenderer overlayRenderer;

    [Tooltip("SpriteRenderer on child positioned one tile south (localPos 0,-1,0). Floor sorting layer.")]
    public SpriteRenderer shadowRenderer;

    // ── Interior neighbour flags — set these from your map system ─────────────
    // True = the neighbour in that direction is interior floor (inside a room).
    // False = wall tile, exterior, or map edge.
    [Header("Interior Neighbours (set by map system)")]
    public bool interiorNorth;
    public bool interiorSouth;   // ← triggers face strip + shadow
    public bool interiorEast;
    public bool interiorWest;

    // ── South-exterior flag — shadow only when south neighbour is open/floor ──
    [Tooltip("True when south neighbour is open space (not another wall). Enables drop shadow.")]
    public bool shadowSouth;

    void OnValidate() => Apply();
    void Start()      => Apply();

    /// <summary>
    /// Call this whenever neighbour state changes (e.g. a wall is built or removed).
    /// </summary>
    public void Apply()
    {
        if (atlasData == null || baseRenderer == null) return;

        // Always show base sprite
        baseRenderer.sprite = atlasData.baseSprite;

        // Build bitmask from interior neighbour flags.
        // NOTE: Unity Y is up, but grid row 0 is typically at the top (Y-down).
        // "North" in grid terms (row - 1) = higher Y in world = visually UP on screen.
        // The atlas was authored with S = the face the player sees (bottom of screen).
        // If your grid uses Y-down (row increases downward), swap N↔S here:
        int mask = (interiorSouth ? 8 : 0)   // grid-north  = world +Y = atlas N bit
                 | (interiorNorth ? 4 : 0)   // grid-south  = world -Y = atlas S bit (face strip)
                 | (interiorEast  ? 2 : 0)
                 | (interiorWest  ? 1 : 0);

        // Select overlay (null = no interior faces, hide renderer)
        Sprite overlay = atlasData.GetOverlay(mask);
        if (overlayRenderer != null)
        {
            overlayRenderer.sprite  = overlay;
            overlayRenderer.enabled = overlay != null;
        }

        // Shadow: show on floor tile to south only when south is open exterior
        if (shadowRenderer != null)
        {
            shadowRenderer.sprite  = atlasData.shadowSprite;
            shadowRenderer.enabled = shadowSouth;
        }
    }

    /// <summary>
    /// Convenience method: pass four bools directly from your map neighbour query.
    /// n/s/e/w = whether that grid-direction neighbour is interior floor.
    /// southOpen = whether the grid-NORTH neighbour (visually south on screen) is open exterior.
    /// </summary>
    public void SetNeighbours(bool n, bool s, bool e, bool w, bool southOpen)
    {
        interiorNorth = n;
        interiorSouth = s;
        interiorEast  = e;
        interiorWest  = w;
        // shadowSouth should be true when the visually-bottom face borders open space.
        // In a Y-down grid that is the grid-NORTH neighbour (row - 1).
        // Pass whichever direction is visually south in your coordinate system.
        shadowSouth   = southOpen;
        Apply();
    }
}
