// NpcAtlasRegistry — ScriptableObject that holds all NPC Sprite[] arrays
// and exposes typed Get* accessors that map ClothingLayerAppearance to a Sprite.
//
// Column layout per atlas (shader-driven neutral-master system):
//   npc_body   : type-major  col = (int)bodyType * 6 + (int)skinTone  (unchanged)
//   npc_face   : direct      col = (int)faceType                       (unchanged)
//   npc_hair   : direct      col = atlasVariantIndex  (5 neutral masters)
//   npc_hat    : direct      col = atlasVariantIndex  (5 neutral masters)
//   npc_shirt  : direct      col = atlasVariantIndex  (5 neutral masters)
//   npc_pants  : direct      col = atlasVariantIndex  (4 neutral masters)
//   npc_shoes  : direct      col = atlasVariantIndex  (3 neutral masters)
//   npc_back   : direct      col = atlasVariantIndex  (5 neutral masters)
//   npc_weapon : direct      col = atlasVariantIndex  (20 sprites, unchanged count)
using UnityEngine;

namespace Waystation.NPC
{
    [CreateAssetMenu(fileName = "NpcAtlasRegistry", menuName = "Waystation/NPC/AtlasRegistry")]
    public class NpcAtlasRegistry : ScriptableObject
    {
        // ── Raw sprite arrays (populated by NpcAtlasImporter) ─────────────────
        [Tooltip("18 sprites — 3 body types × 6 skin tones, type-major")]
        public Sprite[] bodySprites;

        [Tooltip("4 sprites — neutral, stern, weary, alert")]
        public Sprite[] faceSprites;

        [Tooltip("5 sprites — 5 styles, one neutral master each")]
        public Sprite[] hairSprites;

        [Tooltip("5 sprites — 5 types, one neutral master each")]
        public Sprite[] hatSprites;

        [Tooltip("5 sprites — 5 types, one neutral master each")]
        public Sprite[] shirtSprites;

        [Tooltip("4 sprites — 4 types, one neutral master each")]
        public Sprite[] pantsSprites;

        [Tooltip("3 sprites — 3 types, one neutral master each")]
        public Sprite[] shoeSprites;

        [Tooltip("5 sprites — 5 types, one neutral master each")]
        public Sprite[] backSprites;

        [Tooltip("20 sprites — 8 weapon types + 12 reserved transparent slots")]
        public Sprite[] weaponSprites;

        // ── Mask sprite arrays (one mask per variant, populated by NpcAtlasImporter) ──
        [Tooltip("5 sprites — hair mask atlas, one per style")]
        public Sprite[] hairMaskSprites;

        [Tooltip("5 sprites — hat mask atlas, one per type")]
        public Sprite[] hatMaskSprites;

        [Tooltip("5 sprites — shirt mask atlas, one per type")]
        public Sprite[] shirtMaskSprites;

        [Tooltip("4 sprites — pants mask atlas, one per type")]
        public Sprite[] pantsMaskSprites;

        [Tooltip("3 sprites — shoes mask atlas, one per type")]
        public Sprite[] shoeMaskSprites;

        [Tooltip("5 sprites — back mask atlas, one per type")]
        public Sprite[] backMaskSprites;

        [Tooltip("20 sprites — weapon mask atlas")]
        public Sprite[] weaponMaskSprites;

        // ── Typed accessors — body and face unchanged ─────────────────────────

        /// <summary>Returns the body sprite for the given body type and skin tone.</summary>
        public Sprite GetBody(BodyType type, SkinTone tone)
        {
            int col = (int)type * 6 + (int)tone;
            return SafeGet(bodySprites, col, "body");
        }

        /// <summary>Returns the face sprite for the given expression.</summary>
        public Sprite GetFace(FaceType type)
        {
            return SafeGet(faceSprites, (int)type, "face");
        }

        // ── Clothing/hair accessors — take ClothingLayerAppearance ────────────

        public Sprite GetHair(ClothingLayerAppearance app)     => SafeGet(hairSprites,       app.atlasVariantIndex, "hair");
        public Sprite GetHairMask(ClothingLayerAppearance app) => SafeGet(hairMaskSprites,   app.atlasVariantIndex, "hairMask");

        public Sprite GetHat(ClothingLayerAppearance app)      => SafeGet(hatSprites,        app.atlasVariantIndex, "hat");
        public Sprite GetHatMask(ClothingLayerAppearance app)  => SafeGet(hatMaskSprites,    app.atlasVariantIndex, "hatMask");

        public Sprite GetShirt(ClothingLayerAppearance app)      => SafeGet(shirtSprites,      app.atlasVariantIndex, "shirt");
        public Sprite GetShirtMask(ClothingLayerAppearance app)  => SafeGet(shirtMaskSprites,  app.atlasVariantIndex, "shirtMask");

        public Sprite GetPants(ClothingLayerAppearance app)      => SafeGet(pantsSprites,      app.atlasVariantIndex, "pants");
        public Sprite GetPantsMask(ClothingLayerAppearance app)  => SafeGet(pantsMaskSprites,  app.atlasVariantIndex, "pantsMask");

        public Sprite GetShoes(ClothingLayerAppearance app)      => SafeGet(shoeSprites,       app.atlasVariantIndex, "shoes");
        public Sprite GetShoesMask(ClothingLayerAppearance app)  => SafeGet(shoeMaskSprites,   app.atlasVariantIndex, "shoesMask");

        public Sprite GetBack(ClothingLayerAppearance app)      => SafeGet(backSprites,       app.atlasVariantIndex, "back");
        public Sprite GetBackMask(ClothingLayerAppearance app)  => SafeGet(backMaskSprites,   app.atlasVariantIndex, "backMask");

        public Sprite GetWeapon(ClothingLayerAppearance app)      => SafeGet(weaponSprites,     app.atlasVariantIndex, "weapon");
        public Sprite GetWeaponMask(ClothingLayerAppearance app)  => SafeGet(weaponMaskSprites, app.atlasVariantIndex, "weaponMask");

        // ── Private helpers ───────────────────────────────────────────────────

        private Sprite SafeGet(Sprite[] array, int col, string layerName)
        {
            if (array == null || array.Length == 0)
            {
                Debug.LogError($"[NpcAtlasRegistry] '{layerName}' sprite array is null or empty. " +
                               "Run Waystation → NPC → Import NPC Atlases first.", this);
                return null;
            }
            if (col < 0 || col >= array.Length)
            {
                Debug.LogError($"[NpcAtlasRegistry] '{layerName}' col {col} is out of range " +
                               $"(array length {array.Length}).", this);
                return null;
            }
            return array[col];
        }
    }
}
