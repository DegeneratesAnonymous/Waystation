using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Automatically slices any texture inside Assets/Art/Tiles/ that has a matching
/// .json sidecar file, applying pixel-art import settings and a custom sprite layout.
///
/// JSON sidecar format:
/// {
///   "tile_size":  { "w": 16, "h": 16 },
///   "slot_size":  { "w": 16, "h": 16 },
///   "padding":    0,
///   "pivot":      "bottom-left",   // or "center"
///   "tiles": [
///     { "id": "tile.grass", "col": 0, "row": 0 },
///     ...
///   ]
/// }
/// </summary>
public class TileAtlasImporter : AssetPostprocessor
{
    private const string TileFolder = "Assets/Art/Tiles/";

    void OnPreprocessTexture()
    {
        // Only process textures inside the designated tile folder.
        if (!assetPath.StartsWith(TileFolder))
            return;

        // Look for a .json sidecar with the same base filename.
        string jsonAssetPath = Path.ChangeExtension(assetPath, ".json");
        string jsonFullPath  = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", jsonAssetPath));

        if (!File.Exists(jsonFullPath))
            return;

        string jsonText;
        TileAtlasManifest manifest;
        try
        {
            jsonText = File.ReadAllText(jsonFullPath);
            manifest = JsonUtility.FromJson<TileAtlasManifest>(jsonText);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TileAtlasImporter] Failed to read or parse JSON sidecar" +
                $" '{jsonAssetPath}' for texture '{assetPath}': {ex.Message}");
            return;
        }

        if (manifest == null || manifest.tiles == null || manifest.tiles.Count == 0)
            return;

        if (manifest.tile_size == null || manifest.tile_size.w <= 0 || manifest.tile_size.h <= 0)
        {
            Debug.LogError($"[TileAtlasImporter] '{jsonAssetPath}' has missing or invalid" +
                " 'tile_size' (w/h must be > 0). Skipping atlas slice.");
            return;
        }

        if (manifest.slot_size == null || manifest.slot_size.w <= 0 || manifest.slot_size.h <= 0)
        {
            Debug.LogError($"[TileAtlasImporter] '{jsonAssetPath}' has missing or invalid" +
                " 'slot_size' (w/h must be > 0). Skipping atlas slice.");
            return;
        }

        TextureImporter ti = (TextureImporter)assetImporter;

        // ── Texture import settings ────────────────────────────────────────────
        // Use Default (not Sprite) so Resources.Load<Texture2D> is guaranteed to
        // return the texture at runtime.  Sprite.Create() slices it at runtime instead.
        ti.textureType          = TextureImporterType.Default;
        ti.filterMode           = FilterMode.Point;           // no filtering — pixel art
        ti.textureCompression   = TextureImporterCompression.Uncompressed;
        ti.isReadable           = true;   // required for Sprite.Create at runtime
        ti.maxTextureSize       = 8192;
        ti.spritePixelsPerUnit  = 64; // 64 px = 1 world unit, matching TileAtlas procedural sprites

        // Sprite rects are intentionally NOT set here.
        // The wall atlas (and any atlas loaded via TileAtlas.cs) is sliced at runtime
        // using Sprite.Create() from the raw Texture2D, so the importer only needs to
        // ensure the texture has correct pixel-art settings (Point filter, PPU=64, etc.).
        // Attempting to call ISpriteEditorDataProvider.Apply() inside OnPreprocessTexture
        // is unreliable in Unity 6 and was causing the atlas to appear unsliced at runtime.
    }

    // ── JSON manifest ──────────────────────────────────────────────────────────
    // JsonUtility requires explicit [Serializable] classes; it cannot deserialise
    // anonymous types or top-level arrays, so all data lives under TileAtlasManifest.

    [System.Serializable]
    class TileAtlasManifest
    {
        public TileSize        tile_size;
        public TileSize        slot_size;
        public int             padding;
        public string          pivot;
        public List<TileEntry> tiles;
    }

    [System.Serializable]
    class TileSize
    {
        public int w;
        public int h;
    }

    [System.Serializable]
    class TileEntry
    {
        public string id;
        public int    col;
        public int    row;
        public int    w;   // 0 = use manifest tile_size.w
        public int    h;   // 0 = use manifest tile_size.h
    }
}
