using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.Creator.UI
{
    public class TemplateEntry
    {
        public string name;
        public string type;
        public string description;
        public string resourcePath; // Resources/ path to template PNG
    }

    public class TemplateBrowser
    {
        private readonly VisualElement _root;
        private readonly System.Action<TemplateEntry> _onSelectTemplate;
        private VisualElement _templateGrid;

        public static readonly List<TemplateEntry> ShippedTemplates = new List<TemplateEntry>
        {
            new TemplateEntry { name = "Blank Floor", type = "floor_tile", description = "Empty 64×64 floor tile" },
            new TemplateEntry { name = "Basic Metal Floor", type = "floor_tile", description = "Simple metal floor with panel lines" },
            new TemplateEntry { name = "Grated Floor", type = "floor_tile", description = "Floor with grate pattern" },
            new TemplateEntry { name = "Plated Floor", type = "floor_tile", description = "Armour-plated floor tile" },
            new TemplateEntry { name = "Blank Wall", type = "wall_tile", description = "Empty wall tile with 3 key variants" },
            new TemplateEntry { name = "Basic Bulkhead", type = "wall_tile", description = "Standard ship bulkhead wall" },
            new TemplateEntry { name = "Windowed Wall", type = "wall_tile", description = "Wall with porthole window" },
            new TemplateEntry { name = "Reinforced Wall", type = "wall_tile", description = "Heavy reinforced wall" },
            new TemplateEntry { name = "Blank Furniture 1×1", type = "furniture", description = "Single-tile furniture" },
            new TemplateEntry { name = "Console 2×1", type = "furniture", description = "Two-tile console station" },
            new TemplateEntry { name = "Table 2×2", type = "furniture", description = "Four-tile table" },
            new TemplateEntry { name = "Machine 2×3", type = "furniture", description = "Large industrial machine" },
        };

        public TemplateBrowser(VisualElement root, System.Action<TemplateEntry> onSelect)
        {
            _root = root;
            _onSelectTemplate = onSelect;
            _templateGrid = root.Q("template-grid");
            Refresh();
        }

        public void Refresh()
        {
            if (_templateGrid == null) return;
            _templateGrid.Clear();

            foreach (var template in ShippedTemplates)
            {
                var card = CreateTemplateCard(template);
                _templateGrid.Add(card);
            }
        }

        private VisualElement CreateTemplateCard(TemplateEntry entry)
        {
            var card = new VisualElement();
            card.AddToClassList("template-card");

            var icon = new VisualElement();
            icon.AddToClassList("template-icon");

            // Load preview texture if available
            if (!string.IsNullOrEmpty(entry.resourcePath))
            {
                var tex = Resources.Load<Texture2D>(entry.resourcePath);
                if (tex != null)
                    icon.style.backgroundImage = new StyleBackground(tex);
            }

            var nameLabel = new Label(entry.name);
            nameLabel.AddToClassList("template-name");

            var descLabel = new Label(entry.description);
            descLabel.AddToClassList("template-desc");

            card.Add(icon);
            card.Add(nameLabel);
            card.Add(descLabel);

            card.RegisterCallback<ClickEvent>(_ => _onSelectTemplate?.Invoke(entry));

            return card;
        }
    }
}
