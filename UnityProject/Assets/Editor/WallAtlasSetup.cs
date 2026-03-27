using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility: Tools → Wall → Setup Wall Atlas
///
/// Forces correct import settings on the wall atlas PNGs so TileAtlas can load
/// them at runtime via Resources.Load and slice sprites with Sprite.Create().
///
/// Run this once after adding or updating any wall PNG, or whenever Unity
/// reimports them with wrong settings.
/// </summary>
public static class WallAtlasSetup
{
    // ── Update these paths if you ever move the atlases ───────────────────────
    // These must point to the Resources/Walls folder so that the textures
    // loaded at runtime via Resources.Load<Texture2D>("Walls/wall_atlas") etc.
    // receive the correct import settings.
    private const string WALL_ATLAS_PATH        = "Assets/Art/Tiles/Resources/Walls/wall_atlas.png";
    private const string WALL_SHADOW_ATLAS_PATH = "Assets/Art/Tiles/Resources/Walls/wall_shadow_atlas.png";
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Wall/Setup Wall Atlas")]
    public static void SetupWallAtlas()
    {
        bool anyChange = false;
        anyChange |= ConfigureAtlas(WALL_ATLAS_PATH);
        anyChange |= ConfigureAtlas(WALL_SHADOW_ATLAS_PATH);

        if (!anyChange)
            Debug.Log("[WallAtlasSetup] All wall atlas import settings were already correct.");
    }

    private static bool ConfigureAtlas(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null)
        {
            Debug.LogWarning($"[WallAtlasSetup] PNG not found at '{path}'. " +
                "Make sure the file exists at Assets/Art/Tiles/Resources/Walls/.");
            return false;
        }

        bool changed = false;

        // TileAtlas loads these as raw Texture2D and slices sprites at runtime
        // via Sprite.Create(), so the import type must be Default (not Sprite).
        if (ti.textureType != TextureImporterType.Default)
            { ti.textureType = TextureImporterType.Default; changed = true; }

        if (ti.filterMode != FilterMode.Point)
            { ti.filterMode = FilterMode.Point; changed = true; }

        if (ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }

        if (!ti.isReadable)
            { ti.isReadable = true; changed = true; }

        if ((int)ti.spritePixelsPerUnit != 64)
            { ti.spritePixelsPerUnit = 64; changed = true; }

        if (changed)
        {
            EditorUtility.SetDirty(ti);
            ti.SaveAndReimport();
            Debug.Log($"[WallAtlasSetup] Import settings updated — {path} reimported.");
        }
        else
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[WallAtlasSetup] Settings already correct — {path} reimported.");
        }

        return changed;
    }
}
