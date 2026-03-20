// NpcAtlasImporter — Editor-only tool that auto-slices the 9 NPC PNG atlases
// and their 7 companion mask atlases from Assets/Art/NPCs/ and populates (or
// creates) an NpcAtlasRegistry asset.
//
// Usage:
//   1. Copy base and mask PNGs from atlases/ into Assets/Art/NPCs/.
//   2. Open the menu Waystation → NPC → Import NPC Atlases.
//   3. The tool slices each texture at slot_size=34×50, assigns sprites to the
//      registry arrays, and saves an NpcAtlasRegistry.asset in Assets/Resources/.
//
// Atlas layout after the Art Design System pt 2 update:
//   npc_body.png   : 18 sprites — 3 body types × 6 skin tones (baked, unchanged)
//   npc_face.png   :  4 sprites — 4 expressions (baked, unchanged)
//   npc_hair.png   :  5 sprites — one neutral master per hair style
//   npc_hat.png    :  5 sprites — one neutral master per hat type
//   npc_shirt.png  :  5 sprites — one neutral master per shirt type
//   npc_pants.png  :  4 sprites — one neutral master per pants type
//   npc_shoes.png  :  3 sprites — one neutral master per shoe type
//   npc_back.png   :  5 sprites — one neutral master per back-item type
//   npc_weapon.png : 20 sprites — 8 weapon types + 12 reserved (neutral master)
//
// Each clothing/hair atlas has a companion mask atlas (*_mask.png) with the
// same sprite count encoding recolourable regions as flat distinct colours.
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
            { "npc_body.png",         18 },
            { "npc_face.png",          4 },
            { "npc_hair.png",          5 },
            { "npc_hat.png",           5 },
            { "npc_shirt.png",         5 },
            { "npc_pants.png",         4 },
            { "npc_shoes.png",         3 },
            { "npc_back.png",          5 },
            { "npc_weapon.png",       20 },
            // Mask atlases — same counts as their base counterparts
            { "npc_hair_mask.png",     5 },
            { "npc_hat_mask.png",      5 },
            { "npc_shirt_mask.png",    5 },
            { "npc_pants_mask.png",    4 },
            { "npc_shoes_mask.png",    3 },
            { "npc_back_mask.png",     5 },
            { "npc_weapon_mask.png",  20 },
        };

        // Maps mask atlas filename → expected sprite count
        private static readonly Dictionary<string, int> MaskAtlasCounts = new Dictionary<string, int>
        {
            { "npc_hair_mask.png",    5 },
            { "npc_hat_mask.png",     5 },
            { "npc_shirt_mask.png",   5 },
            { "npc_pants_mask.png",   4 },
            { "npc_shoes_mask.png",   3 },
            { "npc_back_mask.png",    5 },
            { "npc_weapon_mask.png", 20 },
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

            // ── Base atlases ──────────────────────────────────────────────────
            var bodySprites    = SliceAtlas("npc_body.png",        AtlasCounts["npc_body.png"],        ref anyError);
            var faceSprites    = SliceAtlas("npc_face.png",        AtlasCounts["npc_face.png"],        ref anyError);
            var hairSprites    = SliceAtlas("npc_hair.png",        AtlasCounts["npc_hair.png"],        ref anyError);
            var hatSprites     = SliceAtlas("npc_hat.png",         AtlasCounts["npc_hat.png"],         ref anyError);
            var shirtSprites   = SliceAtlas("npc_shirt.png",       AtlasCounts["npc_shirt.png"],       ref anyError);
            var pantsSprites   = SliceAtlas("npc_pants.png",       AtlasCounts["npc_pants.png"],       ref anyError);
            var shoeSprites    = SliceAtlas("npc_shoes.png",       AtlasCounts["npc_shoes.png"],       ref anyError);
            var backSprites    = SliceAtlas("npc_back.png",        AtlasCounts["npc_back.png"],        ref anyError);
            var weaponSprites  = SliceAtlas("npc_weapon.png",      AtlasCounts["npc_weapon.png"],      ref anyError);

            // ── Mask atlases ──────────────────────────────────────────────────
            var hairMaskSprites   = SliceAtlas("npc_hair_mask.png",   AtlasCounts["npc_hair_mask.png"],   ref anyError);
            var hatMaskSprites    = SliceAtlas("npc_hat_mask.png",    AtlasCounts["npc_hat_mask.png"],    ref anyError);
            var shirtMaskSprites  = SliceAtlas("npc_shirt_mask.png",  AtlasCounts["npc_shirt_mask.png"],  ref anyError);
            var pantsMaskSprites  = SliceAtlas("npc_pants_mask.png",  AtlasCounts["npc_pants_mask.png"],  ref anyError);
            var shoeMaskSprites   = SliceAtlas("npc_shoes_mask.png",  AtlasCounts["npc_shoes_mask.png"],  ref anyError);
            var backMaskSprites   = SliceAtlas("npc_back_mask.png",   AtlasCounts["npc_back_mask.png"],   ref anyError);
            var weaponMaskSprites = SliceAtlas("npc_weapon_mask.png", AtlasCounts["npc_weapon_mask.png"], ref anyError);

            // Mask atlases
            var hairMaskSprites   = SliceAtlas("npc_hair_mask.png",   MaskAtlasCounts["npc_hair_mask.png"],   ref anyError);
            var hatMaskSprites    = SliceAtlas("npc_hat_mask.png",    MaskAtlasCounts["npc_hat_mask.png"],    ref anyError);
            var shirtMaskSprites  = SliceAtlas("npc_shirt_mask.png",  MaskAtlasCounts["npc_shirt_mask.png"],  ref anyError);
            var pantsMaskSprites  = SliceAtlas("npc_pants_mask.png",  MaskAtlasCounts["npc_pants_mask.png"],  ref anyError);
            var shoesMaskSprites  = SliceAtlas("npc_shoes_mask.png",  MaskAtlasCounts["npc_shoes_mask.png"],  ref anyError);
            var backMaskSprites   = SliceAtlas("npc_back_mask.png",   MaskAtlasCounts["npc_back_mask.png"],   ref anyError);
            var weaponMaskSprites = SliceAtlas("npc_weapon_mask.png", MaskAtlasCounts["npc_weapon_mask.png"], ref anyError);

            if (anyError)
            {
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

            registry.hairMaskSprites   = hairMaskSprites;
            registry.hatMaskSprites    = hatMaskSprites;
            registry.shirtMaskSprites  = shirtMaskSprites;
            registry.pantsMaskSprites  = pantsMaskSprites;
            registry.shoeMaskSprites   = shoeMaskSprites;
            registry.backMaskSprites   = backMaskSprites;
            registry.weaponMaskSprites = weaponMaskSprites;

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
                int pxLeft   = col * SlotW + Padding;
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
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var sprites = new List<Sprite>();
            foreach (var obj in all)
            {
                if (obj is Sprite s) sprites.Add(s);
            }

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
            string stem = Path.GetFileNameWithoutExtension(atlasFilename);
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
