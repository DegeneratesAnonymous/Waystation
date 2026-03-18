// NpcAtlasRegistry — ScriptableObject that holds all NPC Sprite[] arrays
// and exposes typed Get* accessors that map enum + colour-index to a Sprite.
//
// Column layout per atlas:
//   npc_body   : type-major  col = (int)bodyType * 6 + (int)skinTone
//   npc_face   : direct      col = (int)faceType
//   npc_hair   : style-major col = (int)hairStyle * 6 + colorIndex
//   npc_hat    : color-major  col = colorIndex * 5 + (int)hatType   ← ensures Helmet,0 → col 1
//   npc_shirt  : type-major  col = (int)shirtType * 5 + colorIndex
//   npc_pants  : type-major  col = (int)pantsType * 5 + colorIndex
//   npc_shoes  : type-major  col = (int)shoeType * 5 + colorIndex
//   npc_back   : type-major  col = (int)backType * 2 + colorIndex
//   npc_weapon : direct      col = (int)weaponType
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

        [Tooltip("30 sprites — 5 styles × 6 colours, style-major")]
        public Sprite[] hairSprites;

        [Tooltip("25 sprites — 5 types × 5 colors, color-major")]
        public Sprite[] hatSprites;

        [Tooltip("25 sprites — 5 types × 5 colours, type-major")]
        public Sprite[] shirtSprites;

        [Tooltip("20 sprites — 4 types × 5 colours, type-major")]
        public Sprite[] pantsSprites;

        [Tooltip("15 sprites — 3 types × 5 colours, type-major")]
        public Sprite[] shoeSprites;

        [Tooltip("10 sprites — 5 types × 2 colours, type-major")]
        public Sprite[] backSprites;

        [Tooltip("20 sprites — 8 weapon types + 12 reserved transparent slots")]
        public Sprite[] weaponSprites;

        // ── Typed accessors ───────────────────────────────────────────────────

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

        /// <summary>
        /// Returns the hair sprite.
        /// Layout is style-major: col = (int)style * 6 + colorIndex.
        /// HairStyle.Long=1, colorIndex=0 → col 6, rect.x = 6*34+1.
        /// </summary>
        public Sprite GetHair(HairStyle style, int colorIndex)
        {
            int col = (int)style * 6 + colorIndex;
            return SafeGet(hairSprites, col, "hair");
        }

        /// <summary>
        /// Returns the hat sprite.
        /// Layout is color-major: col = colorIndex * 5 + (int)type.
        /// HatType.Helmet=1, colorIndex=0 → col 1, rect.x = 1*34+1.
        /// </summary>
        public Sprite GetHat(HatType type, int colorIndex)
        {
            int col = colorIndex * 5 + (int)type;
            return SafeGet(hatSprites, col, "hat");
        }

        /// <summary>Returns the shirt sprite. Layout is type-major: col = type*5 + colorIndex.</summary>
        public Sprite GetShirt(ShirtType type, int colorIndex)
        {
            int col = (int)type * 5 + colorIndex;
            return SafeGet(shirtSprites, col, "shirt");
        }

        /// <summary>Returns the pants sprite. Layout is type-major: col = type*5 + colorIndex.</summary>
        public Sprite GetPants(PantsType type, int colorIndex)
        {
            int col = (int)type * 5 + colorIndex;
            return SafeGet(pantsSprites, col, "pants");
        }

        /// <summary>Returns the shoes sprite. Layout is type-major: col = type*5 + colorIndex.</summary>
        public Sprite GetShoes(ShoeType type, int colorIndex)
        {
            int col = (int)type * 5 + colorIndex;
            return SafeGet(shoeSprites, col, "shoes");
        }

        /// <summary>Returns the back-item sprite. Layout is type-major: col = type*2 + colorIndex.</summary>
        public Sprite GetBack(BackItemType type, int colorIndex)
        {
            int col = (int)type * 2 + colorIndex;
            return SafeGet(backSprites, col, "back");
        }

        /// <summary>Returns the weapon sprite. Direct index: col = (int)weaponType.</summary>
        public Sprite GetWeapon(WeaponType type)
        {
            return SafeGet(weaponSprites, (int)type, "weapon");
        }

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
