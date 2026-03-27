using UnityEngine;

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

    // ── Interior neighbour flags — set these from your map system ────────────
    // True = the neighbour in that direction is interior floor (inside a room).
    // False = wall tile, exterior, or map edge.
    [Header("Interior Neighbours (set by map system)")]
    public bool interiorNorth;
    public bool interiorSouth;   // ← triggers face strip + shadow
    public bool interiorEast;
    public bool interiorWest;

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

        baseRenderer.sprite = atlasData.baseSprite;

        int mask = (interiorNorth ? 8 : 0)
                 | (interiorSouth ? 4 : 0)
                 | (interiorEast  ? 2 : 0)
                 | (interiorWest  ? 1 : 0);

        Sprite overlay = atlasData.GetOverlay(mask);
        if (overlayRenderer != null)
        {
            overlayRenderer.sprite  = overlay;
            overlayRenderer.enabled = overlay != null;
        }

        if (shadowRenderer != null)
        {
            shadowRenderer.sprite  = atlasData.shadowSprite;
            shadowRenderer.enabled = shadowSouth;
        }
    }

    /// <summary>
    /// Convenience method: pass four bools directly from your map neighbour query.
    /// n/s/e/w = whether that grid-direction neighbour is interior floor.
    /// southOpen = whether the visually-bottom face borders open exterior.
    /// </summary>
    public void SetNeighbours(bool n, bool s, bool e, bool w, bool southOpen)
    {
        interiorNorth = n;
        interiorSouth = s;
        interiorEast  = e;
        interiorWest  = w;
        shadowSouth   = southOpen;
        Apply();
    }
}
