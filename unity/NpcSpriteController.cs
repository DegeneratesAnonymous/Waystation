// NpcSpriteController — MonoBehaviour that applies an NpcAppearance to the
// 9 child SpriteRenderer components that make up a layered NPC sprite stack.
//
// Sorting layer  : "NPCs"
// SortingOrder assignment (higher = rendered on top):
//   10  Back item    (renders behind body)
//   11  Body
//   12  Shoes
//   13  Pants
//   14  Shirt
//   15  Face
//   16  Hair
//   17  Hat
//   18  Weapon
//
// Child hierarchy expected under the NPC root GameObject:
//   Back / Body / Shoes / Pants / Shirt / Face / Hair / Hat / Weapon
// (order in hierarchy does not matter — sortingOrder drives draw order)
using UnityEngine;

namespace Waystation.NPC
{
    [DisallowMultipleComponent]
    public class NpcSpriteController : MonoBehaviour
    {
        [Header("Atlas Registry")]
        [Tooltip("Assign the NpcAtlasRegistry ScriptableObject here.")]
        public NpcAtlasRegistry registry;

        [Header("Layer Renderers")]
        [Tooltip("SpriteRenderer for the back item layer (sortingOrder 10).")]
        public SpriteRenderer backRenderer;

        [Tooltip("SpriteRenderer for the body layer (sortingOrder 11).")]
        public SpriteRenderer bodyRenderer;

        [Tooltip("SpriteRenderer for the shoes layer (sortingOrder 12).")]
        public SpriteRenderer shoesRenderer;

        [Tooltip("SpriteRenderer for the pants layer (sortingOrder 13).")]
        public SpriteRenderer pantsRenderer;

        [Tooltip("SpriteRenderer for the shirt layer (sortingOrder 14).")]
        public SpriteRenderer shirtRenderer;

        [Tooltip("SpriteRenderer for the face layer (sortingOrder 15).")]
        public SpriteRenderer faceRenderer;

        [Tooltip("SpriteRenderer for the hair layer (sortingOrder 16).")]
        public SpriteRenderer hairRenderer;

        [Tooltip("SpriteRenderer for the hat layer (sortingOrder 17).")]
        public SpriteRenderer hatRenderer;

        [Tooltip("SpriteRenderer for the weapon layer (sortingOrder 18).")]
        public SpriteRenderer weaponRenderer;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            EnforceSortingOrders();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Applies the given appearance to all 9 layer SpriteRenderers in a single call.
        /// Safe to call multiple times; each call replaces the previous appearance.
        /// </summary>
        public void Apply(NpcAppearance appearance)
        {
            if (appearance == null)
            {
                Debug.LogWarning("[NpcSpriteController] Apply called with null appearance.", this);
                return;
            }

            if (registry == null)
            {
                Debug.LogError("[NpcSpriteController] NpcAtlasRegistry is not assigned.", this);
                return;
            }

            backRenderer.sprite    = registry.GetBack(appearance.backItemType, appearance.backItemColor);
            bodyRenderer.sprite    = registry.GetBody(appearance.bodyType, appearance.skinTone);
            shoesRenderer.sprite   = registry.GetShoes(appearance.shoeType, appearance.shoeColor);
            pantsRenderer.sprite   = registry.GetPants(appearance.pantsType, appearance.pantsColor);
            shirtRenderer.sprite   = registry.GetShirt(appearance.shirtType, appearance.shirtColor);
            faceRenderer.sprite    = registry.GetFace(appearance.faceType);
            hairRenderer.sprite    = registry.GetHair(appearance.hairStyle, appearance.hairColor);
            hatRenderer.sprite     = registry.GetHat(appearance.hatType, appearance.hatColor);
            weaponRenderer.sprite  = registry.GetWeapon(appearance.weaponType);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Programmatically sets the sorting layer and order on every renderer.
        /// Call this when building an NPC prefab at runtime if the renderers
        /// have not been pre-configured in the Inspector.
        /// </summary>
        public void EnforceSortingOrders()
        {
            const string Layer = "NPCs";

            SetRenderer(backRenderer,   Layer, 10);
            SetRenderer(bodyRenderer,   Layer, 11);
            SetRenderer(shoesRenderer,  Layer, 12);
            SetRenderer(pantsRenderer,  Layer, 13);
            SetRenderer(shirtRenderer,  Layer, 14);
            SetRenderer(faceRenderer,   Layer, 15);
            SetRenderer(hairRenderer,   Layer, 16);
            SetRenderer(hatRenderer,    Layer, 17);
            SetRenderer(weaponRenderer, Layer, 18);
        }

        private static void SetRenderer(SpriteRenderer sr, string layerName, int order)
        {
            if (sr == null) return;
            sr.sortingLayerName = layerName;
            sr.sortingOrder     = order;
        }
    }
}
