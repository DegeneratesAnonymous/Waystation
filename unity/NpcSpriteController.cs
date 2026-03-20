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
// Clothing and hair layers use NpcApparel.shader for mask-keyed runtime tinting.
// Body and face layers continue to use the default sprite material (baked).
using UnityEngine;

namespace Waystation.NPC
{
    [DisallowMultipleComponent]
    public class NpcSpriteController : MonoBehaviour
    {
        // ── Max tint slots supported by NpcApparel.shader ─────────────────────
        private const int MaxTintSlots = 8;

        // Shader property IDs (cached for performance)
        private static readonly int PropMaskTex   = Shader.PropertyToID("_MaskTex");
        private static readonly int PropTintColors = Shader.PropertyToID("_TintColors");

        [Header("Atlas Registry")]
        [Tooltip("Assign the NpcAtlasRegistry ScriptableObject here.")]
        public NpcAtlasRegistry registry;

        // DepartmentRegistry is a plain C# class and cannot be serialized by
        // Unity. Inject it at runtime via SetDepartmentRegistry() (e.g. from a
        // manager that owns the registry) rather than assigning it in the Inspector.
        private DepartmentRegistry _departmentRegistry;

        /// <summary>
        /// Injects the DepartmentRegistry used to resolve DeptColour sources.
        /// Call this from a manager before calling Apply() on any NPC that has
        /// DeptColour clothing slots.
        /// </summary>
        public void SetDepartmentRegistry(DepartmentRegistry registry)
        {
            _departmentRegistry = registry;
        }

        /// <summary>Department UID of the NPC owning this controller (set before Apply).</summary>
        [HideInInspector]
        public string npcDepartmentId;

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

        // ── Shared MaterialPropertyBlocks (one per layer renderer) ────────────
        private MaterialPropertyBlock _backBlock;
        private MaterialPropertyBlock _shoesBlock;
        private MaterialPropertyBlock _pantsBlock;
        private MaterialPropertyBlock _shirtBlock;
        private MaterialPropertyBlock _hairBlock;
        private MaterialPropertyBlock _hatBlock;
        private MaterialPropertyBlock _weaponBlock;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            AutoResolveRenderers();
            EnforceSortingOrders();
            InitPropertyBlocks();
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

            // Body and face: baked sprites, no tinting
            AssignSprite(bodyRenderer,   "bodyRenderer",   registry.GetBody(appearance.bodyType, appearance.skinTone));
            AssignSprite(faceRenderer,   "faceRenderer",   registry.GetFace(appearance.faceType));

            // Clothing and hair: neutral masters + mask-keyed tinting
            ApplyTintedLayer(
                backRenderer,    "backRenderer",
                registry.GetBack(appearance.BackItemTypeEnum),
                registry.GetBackMask(appearance.BackItemTypeEnum),
                appearance.backItem,
                ref _backBlock);

            ApplyTintedLayer(
                shoesRenderer,   "shoesRenderer",
                registry.GetShoes(appearance.ShoeTypeEnum),
                registry.GetShoesMask(appearance.ShoeTypeEnum),
                appearance.shoes,
                ref _shoesBlock);

            ApplyTintedLayer(
                pantsRenderer,   "pantsRenderer",
                registry.GetPants(appearance.PantsTypeEnum),
                registry.GetPantsMask(appearance.PantsTypeEnum),
                appearance.pants,
                ref _pantsBlock);

            ApplyTintedLayer(
                shirtRenderer,   "shirtRenderer",
                registry.GetShirt(appearance.ShirtTypeEnum),
                registry.GetShirtMask(appearance.ShirtTypeEnum),
                appearance.shirt,
                ref _shirtBlock);

            ApplyTintedLayer(
                hairRenderer,    "hairRenderer",
                registry.GetHair(appearance.HairStyleEnum),
                registry.GetHairMask(appearance.HairStyleEnum),
                appearance.hair,
                ref _hairBlock);

            ApplyTintedLayer(
                hatRenderer,     "hatRenderer",
                registry.GetHat(appearance.HatTypeEnum),
                registry.GetHatMask(appearance.HatTypeEnum),
                appearance.hat,
                ref _hatBlock);

            ApplyTintedLayer(
                weaponRenderer,  "weaponRenderer",
                registry.GetWeapon(appearance.WeaponTypeEnum),
                registry.GetWeaponMask(appearance.WeaponTypeEnum),
                appearance.weapon,
                ref _weaponBlock);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Applies a neutral-master sprite and sets the companion mask + resolved
        /// tint colours on the renderer via a MaterialPropertyBlock.
        /// </summary>
        private void ApplyTintedLayer(
            SpriteRenderer sr,
            string fieldName,
            Sprite baseSprite,
            Sprite maskSprite,
            ClothingLayerAppearance layer,
            ref MaterialPropertyBlock block)
        {
            if (sr == null)
            {
                Debug.LogError($"[NpcSpriteController] '{fieldName}' is not assigned.", this);
                return;
            }

            sr.sprite = baseSprite;

            if (block == null) block = new MaterialPropertyBlock();

            // Mask texture — always write to avoid stale textures from previous Apply calls.
            // When maskSprite is null (layer has no mask atlas), clear to a black 1×1 texture
            // so the shader sees no mask and passes all pixels through unmodified.
            if (maskSprite != null)
                block.SetTexture(PropMaskTex, maskSprite.texture);
            else
                block.SetTexture(PropMaskTex, Texture2D.blackTexture);

            // Resolve tint colours
            var tints = new Color[MaxTintSlots];
            for (int i = 0; i < MaxTintSlots; i++)
                tints[i] = Color.white; // default: white = multiplicative identity (no tint change)

            if (layer != null)
            {
                for (int i = 0; i < layer.slotColours.Count && i < MaxTintSlots; i++)
                {
                    // A null entry in slotColours is explicitly treated as MaterialDefault
                    // (no tint); white is the multiplicative identity so tints[i] stays white.
                    ColourSource src = layer.slotColours[i];
                    if (src == null || src is ColourSource.MaterialDefault)
                        continue;

                    Color? resolved = src.Resolve(npcDepartmentId, _departmentRegistry);
                    if (resolved.HasValue)
                        tints[i] = resolved.Value;
                }
            }

            block.SetColorArray(PropTintColors, tints);
            sr.SetPropertyBlock(block);
        }

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

        private void InitPropertyBlocks()
        {
            _backBlock   = new MaterialPropertyBlock();
            _shoesBlock  = new MaterialPropertyBlock();
            _pantsBlock  = new MaterialPropertyBlock();
            _shirtBlock  = new MaterialPropertyBlock();
            _hairBlock   = new MaterialPropertyBlock();
            _hatBlock    = new MaterialPropertyBlock();
            _weaponBlock = new MaterialPropertyBlock();
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
