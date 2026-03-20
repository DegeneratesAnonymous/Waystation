// NpcSpriteController — MonoBehaviour that applies an NpcAppearance to the
// 9 base layer SpriteRenderers + 7 mask SpriteRenderers that make up a layered NPC sprite stack.
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
//   BackMask / ShoesMask / PantsMask / ShirtMask / HairMask / HatMask / WeaponMask
// (order in hierarchy does not matter — sortingOrder drives draw order)
//
// Mask renderers must use a material assigned in the Inspector that references NpcApparel.shader.
// TODO: apply shader tints via NpcApparelTinter
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

        [Header("Mask Renderers (assign NpcApparel.shader material in Inspector)")]
        [Tooltip("Mask renderer for the back item layer (sortingOrder 10).")]
        public SpriteRenderer backMaskRenderer;

        [Tooltip("Mask renderer for the shoes layer (sortingOrder 12).")]
        public SpriteRenderer shoesMaskRenderer;

        [Tooltip("Mask renderer for the pants layer (sortingOrder 13).")]
        public SpriteRenderer pantsMaskRenderer;

        [Tooltip("Mask renderer for the shirt layer (sortingOrder 14).")]
        public SpriteRenderer shirtMaskRenderer;

        [Tooltip("Mask renderer for the hair layer (sortingOrder 16).")]
        public SpriteRenderer hairMaskRenderer;

        [Tooltip("Mask renderer for the hat layer (sortingOrder 17).")]
        public SpriteRenderer hatMaskRenderer;

        [Tooltip("Mask renderer for the weapon layer (sortingOrder 18).")]
        public SpriteRenderer weaponMaskRenderer;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            AutoResolveRenderers();
            EnforceSortingOrders();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ValidateRendererReferences();
        }
#endif

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

            AssignSprite(backRenderer,   "backRenderer",   registry.GetBack(appearance.back));
            AssignSprite(bodyRenderer,   "bodyRenderer",   registry.GetBody(appearance.bodyType, appearance.skinTone));
            AssignSprite(shoesRenderer,  "shoesRenderer",  registry.GetShoes(appearance.shoes));
            AssignSprite(pantsRenderer,  "pantsRenderer",  registry.GetPants(appearance.pants));
            AssignSprite(shirtRenderer,  "shirtRenderer",  registry.GetShirt(appearance.shirt));
            AssignSprite(faceRenderer,   "faceRenderer",   registry.GetFace(appearance.faceType));
            AssignSprite(hairRenderer,   "hairRenderer",   registry.GetHair(appearance.hair));
            AssignSprite(hatRenderer,    "hatRenderer",    registry.GetHat(appearance.hat));
            AssignSprite(weaponRenderer, "weaponRenderer", registry.GetWeapon(appearance.weapon));

            // Assign mask sprites (material with NpcApparel.shader must be set in Inspector)
            AssignSprite(backMaskRenderer,   "backMaskRenderer",   registry.GetBackMask(appearance.back));
            AssignSprite(shoesMaskRenderer,  "shoesMaskRenderer",  registry.GetShoesMask(appearance.shoes));
            AssignSprite(pantsMaskRenderer,  "pantsMaskRenderer",  registry.GetPantsMask(appearance.pants));
            AssignSprite(shirtMaskRenderer,  "shirtMaskRenderer",  registry.GetShirtMask(appearance.shirt));
            AssignSprite(hairMaskRenderer,   "hairMaskRenderer",   registry.GetHairMask(appearance.hair));
            AssignSprite(hatMaskRenderer,    "hatMaskRenderer",    registry.GetHatMask(appearance.hat));
            AssignSprite(weaponMaskRenderer, "weaponMaskRenderer", registry.GetWeaponMask(appearance.weapon));
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

            // Mask renderers share the same sorting order as their base layer
            SetRenderer(backMaskRenderer,   Layer, 10);
            SetRenderer(shoesMaskRenderer,  Layer, 12);
            SetRenderer(pantsMaskRenderer,  Layer, 13);
            SetRenderer(shirtMaskRenderer,  Layer, 14);
            SetRenderer(hairMaskRenderer,   Layer, 16);
            SetRenderer(hatMaskRenderer,    Layer, 17);
            SetRenderer(weaponMaskRenderer, Layer, 18);
        }

        /// <summary>
        /// Attempts to resolve any unassigned renderer fields by searching for
        /// a child GameObject whose name matches the expected layer name.
        /// This allows the component to self-configure when added to a prefab.
        /// </summary>
        private void AutoResolveRenderers()
        {
            backRenderer   = backRenderer   != null ? backRenderer   : FindChildRenderer("Back");
            bodyRenderer   = bodyRenderer   != null ? bodyRenderer   : FindChildRenderer("Body");
            shoesRenderer  = shoesRenderer  != null ? shoesRenderer  : FindChildRenderer("Shoes");
            pantsRenderer  = pantsRenderer  != null ? pantsRenderer  : FindChildRenderer("Pants");
            shirtRenderer  = shirtRenderer  != null ? shirtRenderer  : FindChildRenderer("Shirt");
            faceRenderer   = faceRenderer   != null ? faceRenderer   : FindChildRenderer("Face");
            hairRenderer   = hairRenderer   != null ? hairRenderer   : FindChildRenderer("Hair");
            hatRenderer    = hatRenderer    != null ? hatRenderer    : FindChildRenderer("Hat");
            weaponRenderer = weaponRenderer != null ? weaponRenderer : FindChildRenderer("Weapon");

            backMaskRenderer   = backMaskRenderer   != null ? backMaskRenderer   : FindChildRenderer("BackMask");
            shoesMaskRenderer  = shoesMaskRenderer  != null ? shoesMaskRenderer  : FindChildRenderer("ShoesMask");
            pantsMaskRenderer  = pantsMaskRenderer  != null ? pantsMaskRenderer  : FindChildRenderer("PantsMask");
            shirtMaskRenderer  = shirtMaskRenderer  != null ? shirtMaskRenderer  : FindChildRenderer("ShirtMask");
            hairMaskRenderer   = hairMaskRenderer   != null ? hairMaskRenderer   : FindChildRenderer("HairMask");
            hatMaskRenderer    = hatMaskRenderer    != null ? hatMaskRenderer    : FindChildRenderer("HatMask");
            weaponMaskRenderer = weaponMaskRenderer != null ? weaponMaskRenderer : FindChildRenderer("WeaponMask");
        }

        private SpriteRenderer FindChildRenderer(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        private void AssignSprite(SpriteRenderer sr, string fieldName, Sprite sprite)
        {
            if (sr == null)
            {
                Debug.LogError($"[NpcSpriteController] '{fieldName}' is not assigned. " +
                               "Assign it in the Inspector or add a child GameObject with the matching name.", this);
                return;
            }
            sr.sprite = sprite;
        }

        private void ValidateRendererReferences()
        {
            CheckRenderer(backRenderer,   "backRenderer");
            CheckRenderer(bodyRenderer,   "bodyRenderer");
            CheckRenderer(shoesRenderer,  "shoesRenderer");
            CheckRenderer(pantsRenderer,  "pantsRenderer");
            CheckRenderer(shirtRenderer,  "shirtRenderer");
            CheckRenderer(faceRenderer,   "faceRenderer");
            CheckRenderer(hairRenderer,   "hairRenderer");
            CheckRenderer(hatRenderer,    "hatRenderer");
            CheckRenderer(weaponRenderer, "weaponRenderer");

            CheckRenderer(backMaskRenderer,   "backMaskRenderer");
            CheckRenderer(shoesMaskRenderer,  "shoesMaskRenderer");
            CheckRenderer(pantsMaskRenderer,  "pantsMaskRenderer");
            CheckRenderer(shirtMaskRenderer,  "shirtMaskRenderer");
            CheckRenderer(hairMaskRenderer,   "hairMaskRenderer");
            CheckRenderer(hatMaskRenderer,    "hatMaskRenderer");
            CheckRenderer(weaponMaskRenderer, "weaponMaskRenderer");
        }

        private void CheckRenderer(SpriteRenderer sr, string fieldName)
        {
            if (sr == null)
                Debug.LogWarning($"[NpcSpriteController] '{fieldName}' is not assigned on '{name}'.", this);
        }

        private static void SetRenderer(SpriteRenderer sr, string layerName, int order)
        {
            if (sr == null) return;
            sr.sortingLayerName = layerName;
            sr.sortingOrder     = order;
        }
    }
}
