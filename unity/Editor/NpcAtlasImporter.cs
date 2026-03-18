// NpcAtlasImporter — Editor-only tool that auto-slices the 9 NPC PNG atlases
// from Assets/Art/NPCs/ and populates (or creates) an NpcAtlasRegistry asset.
//
// Usage:
//   1. Copy the 9 PNG atlases from atlases/ into Assets/Art/NPCs/ subfolders
//      (or directly into Assets/Art/NPCs/).
//   2. Open the menu Waystation → NPC → Import NPC Atlases.
//   3. The tool slices each texture at slot_size=34×50, assigns sprites to the
//      registry arrays, and saves an NpcAtlasRegistry.asset in Assets/Resources/.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEngine;

namespace Waystation.NPC.Editor
{
    public static class NpcAtlasImporter
    {
        private const int TileW    = 32;
        private const int TileH    = 48;
        private const int SlotW    = 34;
        private const int SlotH    = 50;
        private const int Padding  = 1;
        private const string NpcArtFolder       = "Assets/Art/NPCs";
        private const string RegistryOutputPath = "Assets/Resources/NpcAtlasRegistry.asset";

        // Maps atlas filename → expected sprite count
        private static readonly Dictionary<string, int> AtlasCounts = new Dictionary<string, int>
        {
            { "npc_body.png",   18 },
            { "npc_face.png",    4 },
            { "npc_hair.png",   30 },
            { "npc_hat.png",    25 },
            { "npc_shirt.png",  25 },
            { "npc_pants.png",  20 },
            { "npc_shoes.png",  15 },
            { "npc_back.png",   10 },
            { "npc_weapon.png", 20 },
        };

        [MenuItem("Waystation/NPC/Import NPC Atlases")]
        public static void ImportAtlases()
        {
            // Ensure Assets/Resources directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
                Debug.Log("[NpcAtlasImporter] Created Assets/Resources folder.");
            }

            // Load or create registry asset
            var registry = AssetDatabase.LoadAssetAtPath<NpcAtlasRegistry>(RegistryOutputPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<NpcAtlasRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryOutputPath);
                Debug.Log("[NpcAtlasImporter] Created new NpcAtlasRegistry at " + RegistryOutputPath);
            }

            bool anyError = false;

            var bodySprites   = SliceAtlas("npc_body.png",   AtlasCounts["npc_body.png"],   ref anyError);
            var faceSprites   = SliceAtlas("npc_face.png",   AtlasCounts["npc_face.png"],   ref anyError);
            var hairSprites   = SliceAtlas("npc_hair.png",   AtlasCounts["npc_hair.png"],   ref anyError);
            var hatSprites    = SliceAtlas("npc_hat.png",    AtlasCounts["npc_hat.png"],    ref anyError);
            var shirtSprites  = SliceAtlas("npc_shirt.png",  AtlasCounts["npc_shirt.png"],  ref anyError);
            var pantsSprites  = SliceAtlas("npc_pants.png",  AtlasCounts["npc_pants.png"],  ref anyError);
            var shoeSprites   = SliceAtlas("npc_shoes.png",  AtlasCounts["npc_shoes.png"],  ref anyError);
            var backSprites   = SliceAtlas("npc_back.png",   AtlasCounts["npc_back.png"],   ref anyError);
            var weaponSprites = SliceAtlas("npc_weapon.png", AtlasCounts["npc_weapon.png"], ref anyError);

            if (anyError)
            {
                // Don't overwrite existing registry arrays when an atlas is missing/invalid;
                // fail fast so the developer sees the error without a partially-broken registry.
                Debug.LogError("[NpcAtlasImporter] Import aborted due to errors — " +
                               "registry arrays have NOT been updated. Check the console above.");
                return;
            }

            // All atlases loaded successfully — commit to registry
            registry.bodySprites   = bodySprites;
            registry.faceSprites   = faceSprites;
            registry.hairSprites   = hairSprites;
            registry.hatSprites    = hatSprites;
            registry.shirtSprites  = shirtSprites;
            registry.pantsSprites  = pantsSprites;
            registry.shoeSprites   = shoeSprites;
            registry.backSprites   = backSprites;
            registry.weaponSprites = weaponSprites;

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[NpcAtlasImporter] All NPC atlases imported successfully.");
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private static Sprite[] SliceAtlas(string filename, int expectedCount, ref bool anyError)
        {
            string assetPath = FindAsset(filename);
            if (assetPath == null)
            {
                Debug.LogError($"[NpcAtlasImporter] Could not find {filename} under {NpcArtFolder}. " +
                               "Copy the PNG from atlases/ into the Unity project first.");
                anyError = true;
                return null;
            }

            // Configure import settings
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                Debug.LogError($"[NpcAtlasImporter] No TextureImporter for {assetPath}.");
                anyError = true;
                return null;
            }

            // ── Pixel-art texture settings (aligned with TileAtlasImporter) ──
            importer.textureType        = TextureImporterType.Sprite;
            importer.spriteImportMode   = SpriteImportMode.Multiple;
            importer.filterMode         = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable         = false;
            importer.maxTextureSize     = 8192;
            importer.spritePivot        = new Vector2(0.5f, 0.5f);
            importer.spritePixelsPerUnit = 32f;

            // Build sprite meta-data (one per column, row always 0)
            var metas = new List<SpriteMetaData>();
            for (int col = 0; col < expectedCount; col++)
            {
                // Texture coordinates: origin is bottom-left in Unity
                // Atlas origin is top-left in PNG pixel space, so we flip Y.
                // Each slot is SlotW×SlotH. Content starts at (1,1) within slot.
                // PNG height = SlotH = 50.
                // Unity rect y = atlasHeight - slotY - SlotH
                //              = 50 - 0 - 50 = 0  (for row 0, only row)
                int pxLeft = col * SlotW + Padding;
                int pxBottom = SlotH - Padding - TileH; // = 50 - 1 - 48 = 1
                var meta = new SpriteMetaData
                {
                    name      = BuildSpriteName(filename, col),
                    rect      = new Rect(pxLeft, pxBottom, TileW, TileH),
                    pivot     = new Vector2(0.5f, 0.5f),
                    alignment = (int)SpriteAlignment.Center
                };
                metas.Add(meta);
            }

            importer.spritesheet = metas.ToArray();

            // Always re-import to apply all settings and pick up new sprite rects
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // Load sprites from asset database
            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var sprites = new List<Sprite>();
            foreach (var obj in all)
            {
                if (obj is Sprite s) sprites.Add(s);
            }

            // Sort by column order (sprite name ends with _col)
            sprites.Sort((a, b) =>
            {
                int ca = ExtractColFromName(a.name);
                int cb = ExtractColFromName(b.name);
                return ca.CompareTo(cb);
            });

            if (sprites.Count != expectedCount)
            {
                Debug.LogWarning($"[NpcAtlasImporter] {filename}: expected {expectedCount} sprites, " +
                                 $"got {sprites.Count}.");
                anyError = true;
            }
            else
            {
                Debug.Log($"[NpcAtlasImporter] {filename}: loaded {sprites.Count} sprites OK.");
            }

            return sprites.ToArray();
        }

        private static string FindAsset(string filename)
        {
            // Search recursively under NpcArtFolder
            string[] guids = AssetDatabase.FindAssets(
                Path.GetFileNameWithoutExtension(filename),
                new[] { NpcArtFolder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == filename) return path;
            }
            return null;
        }

        private static string BuildSpriteName(string atlasFilename, int col)
        {
            string stem = Path.GetFileNameWithoutExtension(atlasFilename); // e.g. "npc_body"
            return $"{stem}_col{col:D3}";
        }

        private static int ExtractColFromName(string name)
        {
            int idx = name.LastIndexOf("_col", System.StringComparison.Ordinal);
            if (idx < 0) return 0;
            if (int.TryParse(name.Substring(idx + 4), out int col)) return col;
            return 0;
        }
    }
}
#endif
