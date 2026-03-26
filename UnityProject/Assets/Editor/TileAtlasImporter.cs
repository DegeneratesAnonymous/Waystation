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
///   "runtime_slice": false,        // true → Default texture type, no sprite sub-assets
///   "tiles": [
///     { "id": "tile.grass", "col": 0, "row": 0 },
///     ...
///   ]
/// }
///
/// When "runtime_slice" is omitted or false the texture is imported as Sprite/Multiple
/// and sprite sub-assets are created from the tiles array, enabling
/// Resources.LoadAll&lt;Sprite&gt;() lookups (e.g. generator_atlas, wall_base, wall_overlays).
///
/// Set "runtime_slice": true only for atlases that are loaded exclusively as a raw
/// Texture2D via Resources.Load&lt;Texture2D&gt;() and sliced at runtime with Sprite.Create().
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
        // Pixel-art settings shared by all atlas paths.
        ti.filterMode          = FilterMode.Point;           // no filtering — pixel art
        ti.textureCompression  = TextureImporterCompression.Uncompressed;
        ti.isReadable          = true;   // required for Sprite.Create() calls at runtime
        ti.maxTextureSize      = 8192;
        ti.spritePixelsPerUnit = 64; // 64 px = 1 world unit

        if (manifest.runtime_slice)
        {
            // ── Runtime-slice path ──────────────────────────────────────────────
            // The atlas is loaded as a raw Texture2D at runtime and sliced there
            // via Sprite.Create().  Importing as Default (not Sprite) means
            // Resources.Load<Texture2D> is always satisfied; sprite sub-assets are
            // not created because the caller never uses Resources.LoadAll<Sprite>
            // on this texture.
            ti.textureType = TextureImporterType.Default;
        }
        else
        {
            // ── Sprite/Multiple path (default) ──────────────────────────────────
            // Atlases consumed via Resources.LoadAll<Sprite>(...) need importer-
            // generated sprite sub-assets (e.g. generator_atlas, wall_base,
            // wall_overlays).  Resources.Load<Texture2D> also works on Sprite-type
            // textures, so any atlas that additionally uses Sprite.Create() at
            // runtime is handled correctly either way.
            ti.textureType      = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;

            // ── Calculate total row count for Y-axis flip ───────────────────────
            // The atlas lays out row 0 at the top, but Unity's texture space has
            // Y=0 at the bottom, so rows must be inverted.
            int maxRow = 0;
            foreach (TileEntry tile in manifest.tiles)
                if (tile.row > maxRow) maxRow = tile.row;
            int totalRows = maxRow + 1;

            // ── Pivot ───────────────────────────────────────────────────────────
            Vector2 pivot = manifest.pivot == "center"
                ? new Vector2(0.5f, 0.5f)
                : new Vector2(0f, 0f);  // default: bottom-left

            // ── Build SpriteMetaData slices ─────────────────────────────────────
            var slices = new SpriteMetaData[manifest.tiles.Count];
            for (int i = 0; i < manifest.tiles.Count; i++)
            {
                TileEntry tile = manifest.tiles[i];

                float x = tile.col * manifest.slot_size.w + manifest.padding;
                float y = (totalRows - 1 - tile.row) * manifest.slot_size.h + manifest.padding;

                slices[i] = new SpriteMetaData
                {
                    name      = tile.id,
                    rect      = new Rect(x, y, manifest.tile_size.w, manifest.tile_size.h),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot     = pivot,
                };
            }

            // Assign the slice array; Unity applies it during the current import.
            // SaveAndReimport() is intentionally not called here — doing so inside
            // OnPreprocessTexture would queue a recursive reimport.  Setting
            // spritesheet directly on the importer is sufficient and takes effect
            // for this import pass automatically.
            ti.spritesheet = slices;
        }
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
        /// <summary>
        /// When true the texture is imported as TextureImporterType.Default (no sprite
        /// sub-assets).  Set this only for atlases that are loaded exclusively as a raw
        /// Texture2D at runtime and sliced via Sprite.Create() — never via
        /// Resources.LoadAll&lt;Sprite&gt;().
        /// Defaults to false so all other atlases keep Sprite/Multiple import behaviour.
        /// </summary>
        public bool            runtime_slice; // default: false (C# bool zero-initialises)
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
