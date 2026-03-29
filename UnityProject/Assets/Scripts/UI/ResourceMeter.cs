// ResourceMeter.cs
// Custom UI Toolkit VisualElement that renders a horizontal fill bar with
// a label, value text, and resource-type colour variant.
//
// Supported resource types and their fill colours:
//   Food     → green  (#30a050)
//   Power    → amber  (#c8a030)
//   Oxygen   → cyan   (#30c8b8)
//   Parts    → blue   (#4880aa)
//   Credits  → blue   (#4880aa)
//
// Usage in UXML:
//   <Waystation.UI.ResourceMeter resource="Food" label="FOOD" value="0.75" />
//
// Usage in C#:
//   var meter = new ResourceMeter(ResourceMeter.ResourceType.Food, "FOOD");
//   meter.SetValue(0.75f);  // 75% fill
//   panel.Add(meter);

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Horizontal resource fill bar with label, value text, and colour variant.
    /// </summary>
    public class ResourceMeter : VisualElement
    {
        // ── Resource type enum ────────────────────────────────────────────
        public enum ResourceType
        {
            Generic,
            Food,
            Power,
            Oxygen,
            Parts,
            Credits,
        }

        // ── UXML factory ──────────────────────────────────────────────────
        public new class UxmlFactory : UxmlFactory<ResourceMeter, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlEnumAttributeDescription<ResourceType> _resource =
                new UxmlEnumAttributeDescription<ResourceType>
                {
                    name = "resource",
                    defaultValue = ResourceType.Generic,
                };

            private readonly UxmlStringAttributeDescription _label =
                new UxmlStringAttributeDescription { name = "label", defaultValue = "" };

            private readonly UxmlFloatAttributeDescription _value =
                new UxmlFloatAttributeDescription { name = "value", defaultValue = 0f };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var meter = (ResourceMeter)ve;
                meter.Resource = _resource.GetValueFromBag(bag, cc);
                meter.LabelText = _label.GetValueFromBag(bag, cc);
                meter.SetValue(_value.GetValueFromBag(bag, cc));
            }
        }

        // ── Child elements ────────────────────────────────────────────────
        private readonly Label _labelElement;
        private readonly Label _valueElement;
        private readonly VisualElement _track;
        private readonly VisualElement _fill;

        // ── Backing fields ────────────────────────────────────────────────
        private ResourceType _resource = ResourceType.Generic;
        private float _value;

        // ── Properties ────────────────────────────────────────────────────
        public ResourceType Resource
        {
            get => _resource;
            set
            {
                if (_resource != ResourceType.Generic)
                    RemoveFromClassList("ws-resource-meter--" + _resource.ToString().ToLower());

                _resource = value;

                if (_resource != ResourceType.Generic)
                    AddToClassList("ws-resource-meter--" + _resource.ToString().ToLower());
            }
        }

        public string LabelText
        {
            get => _labelElement.text;
            set => _labelElement.text = value;
        }

        /// <summary>Current fill value, clamped to [0, 1].</summary>
        public float Value => _value;

        // ── Constructor ───────────────────────────────────────────────────
        public ResourceMeter() : this(ResourceType.Generic, "") { }

        public ResourceMeter(ResourceType resource = ResourceType.Generic, string label = "")
        {
            AddToClassList("ws-resource-meter");

            // Header row: label + value
            var header = new VisualElement();
            header.AddToClassList("ws-resource-meter__header");

            _labelElement = new Label(label);
            _labelElement.AddToClassList("ws-resource-meter__label");

            _valueElement = new Label("0%");
            _valueElement.AddToClassList("ws-resource-meter__value");

            header.Add(_labelElement);
            header.Add(_valueElement);

            // Track + fill
            _track = new VisualElement();
            _track.AddToClassList("ws-resource-meter__track");

            _fill = new VisualElement();
            _fill.AddToClassList("ws-resource-meter__fill");
            _track.Add(_fill);

            Add(header);
            Add(_track);

            Resource = resource;
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Sets the fill percentage. <paramref name="normalised"/> must be in [0, 1].
        /// </summary>
        public void SetValue(float normalised)
        {
            _value = Mathf.Clamp01(normalised);
            _fill.style.width = new StyleLength(new Length(_value * 100f, LengthUnit.Percent));
            _valueElement.text = Mathf.RoundToInt(_value * 100f) + "%";
        }
    }
}
