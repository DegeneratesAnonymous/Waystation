// NpcAppearance — ScriptableObject holding the full visual description of one NPC.
// Assign this asset to NpcSpriteController to render the NPC's layered sprite.
//
// Body and face layers retain baked index fields (unchanged from PR 39).
// All clothing and hair layers use ClothingLayerAppearance, which stores a
// variant index into the neutral-master atlas plus a list of per-slot ColourSources
// resolved at render time by the shader-driven tinting system.
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
    /// Hair style. Maps directly to npc_hair.png column (one neutral master per style).
    /// </summary>
    public enum HairStyle { Short = 0, Long = 1, Medium = 2, Buzz = 3, Ponytail = 4 }

    /// <summary>
    /// Hat type. Maps directly to npc_hat.png column (one neutral master per type).
    /// </summary>
    public enum HatType { Cap = 0, Helmet = 1, Beret = 2, Visor = 3, None = 4 }

    /// <summary>Shirt style. Maps directly to npc_shirt.png column (one neutral master per type).</summary>
    public enum ShirtType { Tshirt = 0, Collar = 1, Uniform = 2, Vest = 3, Tank = 4 }

    /// <summary>Pant style. Maps directly to npc_pants.png column (one neutral master per type).</summary>
    public enum PantsType { Casual = 0, Cargo = 1, Uniform = 2, Shorts = 3 }

    /// <summary>Shoe style. Maps directly to npc_shoes.png column (one neutral master per type).</summary>
    public enum ShoeType { Boots = 0, Sneakers = 1, Formal = 2 }

    /// <summary>Back item type. Maps directly to npc_back.png column (one neutral master per type).</summary>
    public enum BackItemType { None = 0, Backpack = 1, Quiver = 2, Jetpack = 3, Shield = 4 }

    /// <summary>Weapon type. Maps directly to npc_weapon.png column (0-7; 8-19 reserved).</summary>
    public enum WeaponType { None = 0, Pistol = 1, Rifle = 2, Shotgun = 3, Knife = 4, Bat = 5, Wrench = 6, ShieldWeapon = 7 }

    // ── ScriptableObject ──────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "NewNpcAppearance", menuName = "Waystation/NPC/Appearance")]
    public class NpcAppearance : ScriptableObject
    {
        // ── Body & Face — baked, unchanged from PR 39 ─────────────────────────

        [Header("Body (baked)")]
        public BodyType bodyType = BodyType.Average;
        public SkinTone skinTone = SkinTone.Fair;

        [Header("Face (baked)")]
        public FaceType faceType = FaceType.Neutral;

        // ── Clothing & Hair — shader-tinted neutral masters ───────────────────

        [Header("Hair")]
        public ClothingLayerAppearance hair = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)HairStyle.Short
        };

        [Header("Hat")]
        public ClothingLayerAppearance hat = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)HatType.None
        };

        [Header("Shirt")]
        public ClothingLayerAppearance shirt = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)ShirtType.Tshirt
        };

        [Header("Pants")]
        public ClothingLayerAppearance pants = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)PantsType.Casual
        };

        [Header("Shoes")]
        public ClothingLayerAppearance shoes = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)ShoeType.Boots
        };

        [Header("Back Item")]
        public ClothingLayerAppearance backItem = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)BackItemType.None
        };

        [Header("Weapon")]
        public ClothingLayerAppearance weapon = new ClothingLayerAppearance
        {
            atlasVariantIndex = (int)WeaponType.None
        };

        // ── Convenience typed accessors ───────────────────────────────────────

        /// <summary>Hair style derived from the layer appearance variant index.</summary>
        public HairStyle HairStyleEnum
        {
            get => (HairStyle)hair.atlasVariantIndex;
            set => hair.atlasVariantIndex = (int)value;
        }

        /// <summary>Hat type derived from the layer appearance variant index.</summary>
        public HatType HatTypeEnum
        {
            get => (HatType)hat.atlasVariantIndex;
            set => hat.atlasVariantIndex = (int)value;
        }

        /// <summary>Shirt type derived from the layer appearance variant index.</summary>
        public ShirtType ShirtTypeEnum
        {
            get => (ShirtType)shirt.atlasVariantIndex;
            set => shirt.atlasVariantIndex = (int)value;
        }

        /// <summary>Pants type derived from the layer appearance variant index.</summary>
        public PantsType PantsTypeEnum
        {
            get => (PantsType)pants.atlasVariantIndex;
            set => pants.atlasVariantIndex = (int)value;
        }

        /// <summary>Shoe type derived from the layer appearance variant index.</summary>
        public ShoeType ShoeTypeEnum
        {
            get => (ShoeType)shoes.atlasVariantIndex;
            set => shoes.atlasVariantIndex = (int)value;
        }

        /// <summary>Back item type derived from the layer appearance variant index.</summary>
        public BackItemType BackItemTypeEnum
        {
            get => (BackItemType)backItem.atlasVariantIndex;
            set => backItem.atlasVariantIndex = (int)value;
        }

        /// <summary>Weapon type derived from the layer appearance variant index.</summary>
        public WeaponType WeaponTypeEnum
        {
            get => (WeaponType)weapon.atlasVariantIndex;
            set => weapon.atlasVariantIndex = (int)value;
        }
    }
}
