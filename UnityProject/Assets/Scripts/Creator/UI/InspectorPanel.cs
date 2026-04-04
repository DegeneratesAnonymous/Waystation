using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.Creator.UI
{
    public class InspectorPanel
    {
        private readonly VisualElement _root;
        private readonly CreatorModeController _controller;

        private VisualElement _variantTabs;
        private VisualElement _propertiesSection;
        private VisualElement _contextPreviewImage;
        private Label _assetTypeLabel;

        public InspectorPanel(VisualElement root, CreatorModeController controller)
        {
            _root = root;
            _controller = controller;
            Bind();
        }

        private void Bind()
        {
            _variantTabs = _root.Q("variant-tabs");
            _propertiesSection = _root.Q("properties-section");
            _contextPreviewImage = _root.Q("context-preview");
            _assetTypeLabel = _root.Q<Label>("asset-type-label");
        }

        public void UpdateForAsset(AssetDefinition def)
        {
            if (def == null) return;

            if (_assetTypeLabel != null)
                _assetTypeLabel.text = def.type.Replace("_", " ").ToUpperInvariant();

            BuildVariantTabs(def);
            BuildProperties(def);
        }

        private void BuildVariantTabs(AssetDefinition def)
        {
            if (_variantTabs == null) return;
            _variantTabs.Clear();

            switch (def.type)
            {
                case "floor_tile":
                    AddVariantTab("Normal", 0);
                    AddVariantTab("Damaged", 1);
                    AddVariantTab("Destroyed", 2);
                    break;
                case "wall_tile":
                    // Wall uses the bitmask navigator instead
                    var bitmaskNav = new VisualElement();
                    bitmaskNav.AddToClassList("bitmask-nav");
                    for (int i = 0; i < 16; i++)
                    {
                        int idx = i;
                        var cell = new Button(() => _controller.Editor.SwitchVariant(idx));
                        cell.text = TileEditor.WallBitmask.BitmaskVariantManager.VariantLabels[i];
                        cell.AddToClassList("bitmask-cell");
                        if (idx == _controller.Editor.ActiveVariantIndex)
                            cell.AddToClassList("bitmask-cell--active");
                        bitmaskNav.Add(cell);
                    }
                    _variantTabs.Add(bitmaskNav);
                    break;
                case "furniture":
                    string[] dirs = { "South", "North", "Side R", "Side L" };
                    for (int i = 0; i < dirs.Length; i++)
                        AddVariantTab(dirs[i], i);
                    break;
            }
        }

        private void AddVariantTab(string label, int index)
        {
            var tab = new Button(() => _controller.Editor.SwitchVariant(index));
            tab.text = label;
            tab.AddToClassList("variant-tab");
            if (index == _controller.Editor.ActiveVariantIndex)
                tab.AddToClassList("variant-tab--active");
            _variantTabs.Add(tab);
        }

        private void BuildProperties(AssetDefinition def)
        {
            if (_propertiesSection == null) return;
            _propertiesSection.Clear();

            // Name field
            var nameField = new TextField("Name");
            nameField.value = def.name;
            nameField.RegisterValueChangedCallback(evt =>
            {
                def.name = evt.newValue;
                _controller.Editor.MarkDirty();
            });
            _propertiesSection.Add(nameField);

            // Placement surface
            var surfaceField = new DropdownField("Surface",
                new System.Collections.Generic.List<string> { "any", "interior", "exterior", "vacuum" },
                def.editor_state?.placement_surface ?? "any");
            surfaceField.RegisterValueChangedCallback(evt =>
            {
                if (def.editor_state != null)
                    def.editor_state.placement_surface = evt.newValue;
            });
            _propertiesSection.Add(surfaceField);

            // Type-specific properties
            if (def.type == "wall_tile")
            {
                var slabField = new SliderInt("South Slab Height", 4, 8);
                slabField.value = Mathf.Clamp(def.editor_state?.south_slab_height ?? 5, 4, 8);
                slabField.RegisterValueChangedCallback(evt =>
                {
                    if (def.editor_state != null)
                        def.editor_state.south_slab_height = evt.newValue;
                });
                _propertiesSection.Add(slabField);

                // Auto-generate button
                var autoGenBtn = new Button(() => AutoGenerateBitmask());
                autoGenBtn.text = "Auto-Generate Bitmask Variants";
                autoGenBtn.AddToClassList("action-btn");
                _propertiesSection.Add(autoGenBtn);
            }

            if (def.type == "furniture")
            {
                BuildFurnitureProperties(def);
            }
        }

        private void BuildFurnitureProperties(AssetDefinition def)
        {
            var fp = def.editor_state?.footprint;

            var widthField = new SliderInt("Footprint W", 1, 4);
            widthField.value = fp?.w ?? 1;
            widthField.RegisterValueChangedCallback(evt =>
            {
                if (def.editor_state == null) return;
                if (def.editor_state.footprint == null)
                    def.editor_state.footprint = new FootprintData();
                def.editor_state.footprint.w = evt.newValue;
            });
            _propertiesSection.Add(widthField);

            var heightField = new SliderInt("Footprint H", 1, 4);
            heightField.value = fp?.h ?? 1;
            heightField.RegisterValueChangedCallback(evt =>
            {
                if (def.editor_state == null) return;
                if (def.editor_state.footprint == null)
                    def.editor_state.footprint = new FootprintData();
                def.editor_state.footprint.h = evt.newValue;
            });
            _propertiesSection.Add(heightField);

            // Rotation toggle
            var rotToggle = new Toggle("Allow Rotation");
            rotToggle.value = def.editor_state?.allow_rotation ?? true;
            rotToggle.RegisterValueChangedCallback(evt =>
            {
                if (def.editor_state != null)
                    def.editor_state.allow_rotation = evt.newValue;
            });
            _propertiesSection.Add(rotToggle);

            // Status LED
            var ledToggle = new Toggle("Status LED");
            ledToggle.value = def.editor_state?.status_led_enabled ?? false;
            ledToggle.RegisterValueChangedCallback(evt =>
            {
                if (def.editor_state != null)
                    def.editor_state.status_led_enabled = evt.newValue;
            });
            _propertiesSection.Add(ledToggle);
        }

        private void AutoGenerateBitmask()
        {
            var gen = _controller.BitmaskGenerator;
            if (gen == null || !gen.CanAutoGenerate())
            {
                Debug.LogWarning("Cannot auto-generate: need variants 0, 3, and 12 drawn.");
                return;
            }
            var variants = gen.Generate();
            gen.ApplyGenerated(variants, overwriteExisting: false);

            // Rebuild tabs
            UpdateForAsset(_controller.CurrentAsset);
        }
    }
}
