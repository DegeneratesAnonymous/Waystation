// ============================================================
// DoorAtlasSetup.cs
// Assets/Editor/DoorAtlasSetup.cs
//
// Run once after importing the PNG:
//   Unity menu → Tools → Door → Setup Door Atlas
//
// What it does:
//   1. Sets Point filter, no compression, PPU=64, RGBA, Clamp on the PNG
//   2. Slices all 12 sprites with exact pixel rects
//   3. Creates/updates the DoorAtlasData ScriptableObject
//   4. Creates Door_Open.anim and Door_Close.anim animation clips
// ============================================================
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public static class DoorAtlasSetup
{
    // ── Adjust these paths to match where you put the files ───────────────
    const string PNG_PATH        = "Assets/Art/Tiles/Doors/door_ns_atlas.png";
    const string DATA_ASSET_PATH = "Assets/Art/Tiles/Doors/DoorAtlasData.asset";
    const string ANIM_DIR        = "Assets/Animations/Doors";
    const string ANIM_OPEN_PATH  = "Assets/Animations/Doors/Door_Open.anim";
    const string ANIM_CLOSE_PATH = "Assets/Animations/Doors/Door_Close.anim";

    // ── Atlas constants — must match the PNG exactly ───────────────────────
    const int T       = 64;
    const int SLOT    = 66;   // tile + 1px padding each side
    const int PAD     = 1;
    const int ATLAS_H = 66;
    const int N_OPEN  = 10;   // cols 0-9
    const int N_TOTAL = 12;   // 10 open + damaged + destroyed

    static readonly string[] NAMES = {
        "door_ns_open0",    // col  0 — 0%   closed
        "door_ns_open1",    // col  1 — 11%
        "door_ns_open2",    // col  2 — 22%
        "door_ns_open3",    // col  3 — 33%
        "door_ns_open4",    // col  4 — 44%
        "door_ns_open5",    // col  5 — 56%
        "door_ns_open6",    // col  6 — 67%
        "door_ns_open7",    // col  7 — 78%
        "door_ns_open8",    // col  8 — 89%
        "door_ns_open9",    // col  9 — 100% open
        "door_ns_damaged",  // col 10
        "door_ns_destroyed",// col 11
    };

    [MenuItem("Tools/Door/Setup Door Atlas")]
    public static void Run()
    {
        if (!File.Exists(Path.GetFullPath(PNG_PATH)))
        {
            Debug.LogError($"[DoorAtlasSetup] PNG not found at '{PNG_PATH}'.\n" +
                           "Place door_ns_atlas.png there, then re-run this tool.");
            return;
        }

        Step1_ConfigureTexture();
        Step2_SliceSprites();
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Step3_PopulateData();
        Step4_CreateAnimClips();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[DoorAtlasSetup] Done. Check " + DATA_ASSET_PATH + " and " + ANIM_DIR);
    }

    // ── Step 1: import settings ────────────────────────────────────────────
    static void Step1_ConfigureTexture()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(PNG_PATH);
        importer.textureType         = TextureImporterType.Sprite;
        importer.spriteImportMode    = SpriteImportMode.Multiple;
        importer.filterMode          = FilterMode.Point;
        importer.mipmapEnabled       = false;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = 64;
        importer.textureCompression  = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize      = 2048;
        importer.wrapMode            = TextureWrapMode.Clamp;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name               = "DefaultTexturePlatform",
            overridden         = true,
            textureCompression = TextureImporterCompression.Uncompressed,
            maxTextureSize     = 2048,
        });
        importer.SaveAndReimport();
        Debug.Log("[DoorAtlasSetup] Step 1: texture import settings applied.");
    }

    // ── Step 2: sprite slicing ─────────────────────────────────────────────
    static void Step2_SliceSprites()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(PNG_PATH);
        var meta     = new SpriteMetaData[N_TOTAL];

        for (int col = 0; col < N_TOTAL; col++)
        {
            // X: left edge of this tile's slot + 1px padding
            int xLeft = col * SLOT + PAD;

            // Unity sprite rect Y is measured from the BOTTOM of the texture.
            // ATLAS_H=66, PAD=1, T=64 → y_bottom = 66 - 1 - 64 = 1
            int yBottom = ATLAS_H - PAD - T;  // always 1

            meta[col] = new SpriteMetaData
            {
                name      = NAMES[col],
                rect      = new Rect(xLeft, yBottom, T, T),
                alignment = (int)SpriteAlignment.Center,
                pivot     = new Vector2(0.5f, 0.5f),
            };
        }

        importer.spritesheet = meta;
        importer.SaveAndReimport();
        Debug.Log($"[DoorAtlasSetup] Step 2: sliced {N_TOTAL} sprites.");
    }

    // ── Step 3: populate ScriptableObject ─────────────────────────────────
    static void Step3_PopulateData()
    {
        var spriteMap = new Dictionary<string, Sprite>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(PNG_PATH))
            if (obj is Sprite s) spriteMap[s.name] = s;

        if (spriteMap.Count == 0)
        {
            Debug.LogError("[DoorAtlasSetup] Step 3: no sprites found — did Step 2 succeed?");
            return;
        }

        var data = AssetDatabase.LoadAssetAtPath<DoorAtlasData>(DATA_ASSET_PATH);
        if (data == null)
        {
            EnsureDir(DATA_ASSET_PATH);
            data = ScriptableObject.CreateInstance<DoorAtlasData>();
            AssetDatabase.CreateAsset(data, DATA_ASSET_PATH);
        }

        data.openStages = new Sprite[N_OPEN];
        for (int i = 0; i < N_OPEN; i++)
            spriteMap.TryGetValue(NAMES[i], out data.openStages[i]);

        spriteMap.TryGetValue("door_ns_damaged",   out data.damaged);
        spriteMap.TryGetValue("door_ns_destroyed", out data.destroyed);

        EditorUtility.SetDirty(data);
        Debug.Log("[DoorAtlasSetup] Step 3: DoorAtlasData populated.");
    }

    // ── Step 4: animation clips ────────────────────────────────────────────
    static void Step4_CreateAnimClips()
    {
        var spriteMap = new Dictionary<string, Sprite>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(PNG_PATH))
            if (obj is Sprite s) spriteMap[s.name] = s;

        var openSprites = new Sprite[N_OPEN];
        for (int i = 0; i < N_OPEN; i++)
            spriteMap.TryGetValue(NAMES[i], out openSprites[i]);

        // 12 fps → 10 frames = 0.833s total (snappy, matches game feel)
        const float FPS        = 12f;
        const float FRAME_TIME = 1f / FPS;

        // Open: open0 → open9
        SaveClip(MakeClip("Door_Open", openSprites, FRAME_TIME, loop: false), ANIM_OPEN_PATH);

        // Close: open9 → open0
        var rev = new Sprite[N_OPEN];
        for (int i = 0; i < N_OPEN; i++) rev[i] = openSprites[N_OPEN - 1 - i];
        SaveClip(MakeClip("Door_Close", rev, FRAME_TIME, loop: false), ANIM_CLOSE_PATH);

        Debug.Log($"[DoorAtlasSetup] Step 4: animation clips created ({FPS}fps, {N_OPEN} frames each).");
    }

    static AnimationClip MakeClip(string name, Sprite[] frames, float frameTime, bool loop)
    {
        var clip    = new AnimationClip { name = name, frameRate = 1f / frameTime };
        var binding = new EditorCurveBinding
        {
            type         = typeof(SpriteRenderer),
            path         = "",
            propertyName = "m_Sprite",
        };
        var keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i * frameTime, value = frames[i] };

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static void SaveClip(AnimationClip clip, string path)
    {
        EnsureDir(path);
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); }
        else AssetDatabase.CreateAsset(clip, path);
    }

    static void EnsureDir(string assetPath)
    {
        string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(dir)) return;
        string[] parts = dir.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
