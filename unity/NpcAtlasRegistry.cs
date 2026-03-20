// NpcAtlasRegistry — ScriptableObject that holds all NPC Sprite[] arrays
// and exposes typed Get* accessors that map enum value to a Sprite.
//
// Each clothing/hair atlas is now a neutral-tone master (one column per
// style/type). Body and face atlases remain baked (unchanged from PR 39).
//
// Column layout per atlas:
//   npc_body    : type-major  col = (int)bodyType * 6 + (int)skinTone  [18 sprites — baked]
//   npc_face    : direct      col = (int)faceType                       [ 4 sprites — baked]
//   npc_hair    : direct      col = (int)hairStyle                      [ 5 sprites — neutral master]
//   npc_hat     : direct      col = (int)hatType                        [ 5 sprites — neutral master]
//   npc_shirt   : direct      col = (int)shirtType                      [ 5 sprites — neutral master]
//   npc_pants   : direct      col = (int)pantsType                      [ 4 sprites — neutral master]
//   npc_shoes   : direct      col = (int)shoeType                       [ 3 sprites — neutral master]
//   npc_back    : direct      col = (int)backType                       [ 5 sprites — neutral master]
//   npc_weapon  : direct      col = (int)weaponType                     [20 sprites — neutral master]
//
// Each clothing/hair atlas also ships a companion mask atlas (_mask) of equal
// dimensions that encodes recolourable regions as distinct flat colours.
using UnityEngine;

namespace Waystation.NPC
{
    [CreateAssetMenu(fileName = "NpcAtlasRegistry", menuName = "Waystation/NPC/AtlasRegistry")]
    public class NpcAtlasRegistry : ScriptableObject
    {
        // ── Base sprite arrays ────────────────────────────────────────────────

        [Tooltip("18 sprites — 3 body types × 6 skin tones, type-major (baked)")]
        public Sprite[] bodySprites;

        [Tooltip("4 sprites — neutral, stern, weary, alert (baked)")]
        public Sprite[] faceSprites;

        [Tooltip("5 sprites — one neutral master per hair style")]
        public Sprite[] hairSprites;

        [Tooltip("5 sprites — one neutral master per hat type")]
        public Sprite[] hatSprites;

        [Tooltip("5 sprites — one neutral master per shirt type")]
        public Sprite[] shirtSprites;

        [Tooltip("4 sprites — one neutral master per pants type")]
        public Sprite[] pantsSprites;

        [Tooltip("3 sprites — one neutral master per shoe type")]
        public Sprite[] shoeSprites;

        [Tooltip("5 sprites — one neutral master per back-item type")]
        public Sprite[] backSprites;

        [Tooltip("20 sprites — 8 weapon types + 12 reserved transparent slots (neutral master)")]
        public Sprite[] weaponSprites;

        // ── Mask sprite arrays (companion atlases for shader tinting) ─────────

        [Tooltip("5 mask sprites for hair (same dimensions as hairSprites)")]
        public Sprite[] hairMaskSprites;

        [Tooltip("5 mask sprites for hats")]
        public Sprite[] hatMaskSprites;

        [Tooltip("5 mask sprites for shirts")]
        public Sprite[] shirtMaskSprites;

        [Tooltip("4 mask sprites for pants")]
        public Sprite[] pantsMaskSprites;

        [Tooltip("3 mask sprites for shoes")]
        public Sprite[] shoeMaskSprites;

        [Tooltip("5 mask sprites for back items")]
        public Sprite[] backMaskSprites;

        [Tooltip("20 mask sprites for weapons")]
        public Sprite[] weaponMaskSprites;

        // ── Typed accessors — base sprites ────────────────────────────────────

        /// <summary>Returns the body sprite for the given body type and skin tone (baked).</summary>
        public Sprite GetBody(BodyType type, SkinTone tone)
        {
            int col = (int)type * 6 + (int)tone;
            return SafeGet(bodySprites, col, "body");
        }

        /// <summary>Returns the face sprite for the given expression (baked).</summary>
        public Sprite GetFace(FaceType type)
        {
            return SafeGet(faceSprites, (int)type, "face");
        }

        /// <summary>Returns the neutral-master hair sprite for the given style.</summary>
        public Sprite GetHair(HairStyle style)
        {
            return SafeGet(hairSprites, (int)style, "hair");
        }

        /// <summary>Returns the neutral-master hat sprite for the given hat type.</summary>
        public Sprite GetHat(HatType type)
        {
            return SafeGet(hatSprites, (int)type, "hat");
        }

        /// <summary>Returns the neutral-master shirt sprite for the given shirt type.</summary>
        public Sprite GetShirt(ShirtType type)
        {
            return SafeGet(shirtSprites, (int)type, "shirt");
        }

        /// <summary>Returns the neutral-master pants sprite for the given pants type.</summary>
        public Sprite GetPants(PantsType type)
        {
            return SafeGet(pantsSprites, (int)type, "pants");
        }

        /// <summary>Returns the neutral-master shoes sprite for the given shoe type.</summary>
        public Sprite GetShoes(ShoeType type)
        {
            return SafeGet(shoeSprites, (int)type, "shoes");
        }

        /// <summary>Returns the neutral-master back-item sprite for the given back type.</summary>
        public Sprite GetBack(BackItemType type)
        {
            return SafeGet(backSprites, (int)type, "back");
        }

        /// <summary>Returns the neutral-master weapon sprite. Direct index: col = (int)weaponType.</summary>
        public Sprite GetWeapon(WeaponType type)
        {
            return SafeGet(weaponSprites, (int)type, "weapon");
        }

        // ── Typed accessors — mask sprites ────────────────────────────────────

        /// <summary>Returns the hair mask sprite for the given style.</summary>
        public Sprite GetHairMask(HairStyle style)
        {
            return SafeGet(hairMaskSprites, (int)style, "hair_mask");
        }

        /// <summary>Returns the hat mask sprite for the given hat type.</summary>
        public Sprite GetHatMask(HatType type)
        {
            return SafeGet(hatMaskSprites, (int)type, "hat_mask");
        }

        /// <summary>Returns the shirt mask sprite for the given shirt type.</summary>
        public Sprite GetShirtMask(ShirtType type)
        {
            return SafeGet(shirtMaskSprites, (int)type, "shirt_mask");
        }

        /// <summary>Returns the pants mask sprite for the given pants type.</summary>
        public Sprite GetPantsMask(PantsType type)
        {
            return SafeGet(pantsMaskSprites, (int)type, "pants_mask");
        }

        /// <summary>Returns the shoes mask sprite for the given shoe type.</summary>
        public Sprite GetShoesMask(ShoeType type)
        {
            return SafeGet(shoeMaskSprites, (int)type, "shoes_mask");
        }

        /// <summary>Returns the back-item mask sprite for the given back type.</summary>
        public Sprite GetBackMask(BackItemType type)
        {
            return SafeGet(backMaskSprites, (int)type, "back_mask");
        }

        /// <summary>Returns the weapon mask sprite for the given weapon type.</summary>
        public Sprite GetWeaponMask(WeaponType type)
        {
            return SafeGet(weaponMaskSprites, (int)type, "weapon_mask");
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
