// CategoryStripe.cs
// Custom UI Toolkit VisualElement that renders a 3px coloured left-edge
// stripe used on crew and department list items.
//
// Usage in UXML:
//   <Waystation.UI.CategoryStripe category="structure" />
//   <Waystation.UI.CategoryStripe />  <!-- colour set via style.backgroundColor -->
//
// Usage in C#:
//   var stripe = new CategoryStripe(CategoryStripe.Category.Electrical);
//   row.Add(stripe);

using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// A 3px vertical stripe used as a left-edge accent on list item rows.
    /// Colour can be set by category name or directly by Color value.
    /// Implements IDepartmentColoured for runtime department colour injection.
    /// </summary>
    public class CategoryStripe : VisualElement, IDepartmentColoured
    {
        // ── Enum ──────────────────────────────────────────────────────────
        public enum Category
        {
            None,
            Structure,
            Electrical,
            Objects,
            Production,
            Plumbing,
            Security,
        }

        // ── UXML factory ──────────────────────────────────────────────────
        public new class UxmlFactory : UxmlFactory<CategoryStripe, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlEnumAttributeDescription<Category> _category =
                new UxmlEnumAttributeDescription<Category>
                {
                    name = "category",
                    defaultValue = Category.None,
                };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var stripe = (CategoryStripe)ve;
                stripe.StripeCategory = _category.GetValueFromBag(bag, cc);
            }
        }

        // ── Backing field ─────────────────────────────────────────────────
        private Category _category = Category.None;

        // ── Property ──────────────────────────────────────────────────────
        public Category StripeCategory
        {
            get => _category;
            set
            {
                if (_category != Category.None)
                    RemoveFromClassList("ws-category-stripe--" + _category.ToString().ToLower());

                _category = value;

                if (_category != Category.None)
                    AddToClassList("ws-category-stripe--" + _category.ToString().ToLower());
            }
        }

        /// <summary>
        /// Overrides the category colour with an explicit Color value.
        /// When set, category-class-based colour is no longer applied.
        /// </summary>
        public Color StripeColor
        {
            set => style.backgroundColor = value;
        }

        // ── Constructor ───────────────────────────────────────────────────
        public CategoryStripe(Category category = Category.None)
        {
            AddToClassList("ws-category-stripe");
            pickingMode = PickingMode.Ignore;
            StripeCategory = category;
        }

        // ── IDepartmentColoured ───────────────────────────────────────────
        void IDepartmentColoured.ApplyDepartmentColours(Color primary, Color accent)
        {
            style.backgroundColor = primary;
        }
    }
}
