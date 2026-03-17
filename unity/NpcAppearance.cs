// NpcAppearance — ScriptableObject holding the full visual description of one NPC.
// Assign this asset to NpcSpriteController to render the NPC's layered sprite.
using UnityEngine;

namespace Waystation.NPC
{
    // ── Enums ─────────────────────────────────────────────────────────────────

    /// <summary>Body silhouette type. Maps to the major axis of npc_body.png (col / 6).</summary>
    public enum BodyType { Slim = 0, Average = 1, Stocky = 2 }

    /// <summary>Skin tone preset. Maps to the minor axis of npc_body.png (col % 6).</summary>
    public enum SkinTone { Pale = 0, Fair = 1, Medium = 2, Olive = 3, Brown = 4, Dark = 5 }

    /// <summary>Facial expression. Maps directly to npc_face.png column.</summary>
    public enum FaceType { Neutral = 0, Stern = 1, Weary = 2, Alert = 3 }

    /// <summary>
    /// Hair style. Maps to the major axis of npc_hair.png (col / 6).
    /// Long=1 so that GetHair(HairStyle.Long, 0).rect.x == 6*34+1.
    /// </summary>
    public enum HairStyle { Short = 0, Long = 1, Medium = 2, Buzz = 3, Ponytail = 4 }

    /// <summary>
    /// Hat type. Maps to the minor axis of npc_hat.png under colour-major layout
    /// (col = colorIndex * 5 + hatType). Helmet=1 so that
    /// GetHat(HatType.Helmet, 0).rect.x == 1*34+1.
    /// </summary>
    public enum HatType { Cap = 0, Helmet = 1, Beret = 2, Visor = 3, None = 4 }

    /// <summary>Shirt style. Maps to the major axis of npc_shirt.png (col / 5).</summary>
    public enum ShirtType { Tshirt = 0, Collar = 1, Uniform = 2, Vest = 3, Tank = 4 }

    /// <summary>Pant style. Maps to the major axis of npc_pants.png (col / 5).</summary>
    public enum PantsType { Casual = 0, Cargo = 1, Uniform = 2, Shorts = 3 }

    /// <summary>Shoe style. Maps to the major axis of npc_shoes.png (col / 5).</summary>
    public enum ShoeType { Boots = 0, Sneakers = 1, Formal = 2 }

    /// <summary>Back item type. Maps to the major axis of npc_back.png (col / 2).</summary>
    public enum BackItemType { None = 0, Backpack = 1, Quiver = 2, Jetpack = 3, Shield = 4 }

    /// <summary>Weapon type. Maps directly to npc_weapon.png column (0-7; 8-19 reserved).</summary>
    public enum WeaponType { None = 0, Pistol = 1, Rifle = 2, Shotgun = 3, Knife = 4, Bat = 5, Wrench = 6, ShieldWeapon = 7 }

    // ── ScriptableObject ──────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewNpcAppearance", menuName = "Waystation/NPC/Appearance")]
    public class NpcAppearance : ScriptableObject
    {
        [Header("Body")]
        public BodyType bodyType   = BodyType.Average;
        public SkinTone skinTone   = SkinTone.Fair;

        [Header("Face")]
        public FaceType faceType   = FaceType.Neutral;

        [Header("Hair")]
        public HairStyle hairStyle = HairStyle.Short;
        /// <summary>0-5 — matches HAIR_COLORS order: blonde, auburn, brown, black, grey, white.</summary>
        [Range(0, 5)] public int hairColor = 0;

        [Header("Hat")]
        public HatType hatType     = HatType.None;
        /// <summary>0-4 — matches EQUIP_COLORS_5 order: grey, blue, green, red, brown.</summary>
        [Range(0, 4)] public int hatColor  = 0;

        [Header("Shirt")]
        public ShirtType shirtType = ShirtType.Tshirt;
        [Range(0, 4)] public int shirtColor = 0;

        [Header("Pants")]
        public PantsType pantsType = PantsType.Casual;
        [Range(0, 4)] public int pantsColor = 0;

        [Header("Shoes")]
        public ShoeType shoeType   = ShoeType.Boots;
        [Range(0, 4)] public int shoeColor  = 0;

        [Header("Back Item")]
        public BackItemType backItemType = BackItemType.None;
        /// <summary>0-1 — matches EQUIP_COLORS_2 order: grey, blue.</summary>
        [Range(0, 1)] public int backItemColor = 0;

        [Header("Weapon")]
        public WeaponType weaponType = WeaponType.None;
    }
}
