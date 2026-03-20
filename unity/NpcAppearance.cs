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
    /// Hair style. atlasVariantIndex in ClothingLayerAppearance maps directly to this enum cast
    /// (col = (int)style — one neutral master per style).
    /// </summary>
    public enum HairStyle { Short = 0, Long = 1, Medium = 2, Buzz = 3, Ponytail = 4 }

    /// <summary>Hat type. atlasVariantIndex maps directly to this enum cast.</summary>
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
        public ClothingLayerAppearance hair = new ClothingLayerAppearance();

        [Header("Hat")]
        public ClothingLayerAppearance hat = new ClothingLayerAppearance();

        [Header("Shirt")]
        public ClothingLayerAppearance shirt = new ClothingLayerAppearance();

        [Header("Pants")]
        public ClothingLayerAppearance pants = new ClothingLayerAppearance();

        [Header("Shoes")]
        public ClothingLayerAppearance shoes = new ClothingLayerAppearance();

        [Header("Back Item")]
        public ClothingLayerAppearance back = new ClothingLayerAppearance();

        [Header("Weapon")]
        public ClothingLayerAppearance weapon = new ClothingLayerAppearance();
    }
}
