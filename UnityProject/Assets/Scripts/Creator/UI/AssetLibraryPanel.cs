using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Waystation.Creator.UI
{
    public class AssetLibraryPanel
    {
        private readonly VisualElement _root;
        private readonly AssetLibrary _library;
        private readonly System.Action<AssetDefinition> _onOpenAsset;
        private readonly List<Texture2D> _thumbnailTextures = new List<Texture2D>();

        private TextField _searchField;
        private VisualElement _assetGrid;
        private string _typeFilter = "all";
        private string _searchQuery = "";

        public AssetLibraryPanel(VisualElement root, AssetLibrary library, System.Action<AssetDefinition> onOpenAsset)
        {
            _root = root;
            _library = library;
            _onOpenAsset = onOpenAsset;
            Bind();
            Refresh();

            _library.OnLibraryChanged += Refresh;
        }

        private void Bind()
        {
            _searchField = _root.Q<TextField>("library-search");
            _assetGrid = _root.Q("asset-grid");

            _searchField?.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = evt.newValue;
                Refresh();
            });

            // Type filter buttons
            BindFilterButton("filter-all", "all");
            BindFilterButton("filter-floor", "floor_tile");
            BindFilterButton("filter-wall", "wall_tile");
            BindFilterButton("filter-furniture", "furniture");
        }

        private void BindFilterButton(string btnName, string filterValue)
        {
            var btn = _root.Q<Button>(btnName);
            btn?.RegisterCallback<ClickEvent>(_ =>
            {
                _typeFilter = filterValue;
                Refresh();
            });
        }

        public void Refresh()
        {
            if (_assetGrid == null) return;
            _assetGrid.Clear();

            // Destroy previously cached thumbnails
            foreach (var tex in _thumbnailTextures)
                if (tex != null) Object.Destroy(tex);
            _thumbnailTextures.Clear();

            var assets = _library.Search(_searchQuery, _typeFilter);

            foreach (var def in assets)
            {
                var card = CreateAssetCard(def);
                _assetGrid.Add(card);
            }
        }

        private VisualElement CreateAssetCard(AssetDefinition def)
        {
            var card = new VisualElement();
            card.AddToClassList("asset-card");

            var thumb = new VisualElement();
            thumb.AddToClassList("asset-thumb");

            // Load thumbnail if available
            string thumbPath = System.IO.Path.Combine(
                _library.GetAssetDirectory(def), "thumbnail.png");
            if (System.IO.File.Exists(thumbPath))
            {
                var data = System.IO.File.ReadAllBytes(thumbPath);
                var tex = new Texture2D(2, 2);
                tex.LoadImage(data);
                thumb.style.backgroundImage = new StyleBackground(tex);
                _thumbnailTextures.Add(tex);
            }

            var nameLabel = new Label(def.name);
            nameLabel.AddToClassList("asset-name");

            var typeLabel = new Label(def.type.Replace("_", " ").ToUpperInvariant());
            typeLabel.AddToClassList("asset-type");

            card.Add(thumb);
            card.Add(nameLabel);
            card.Add(typeLabel);

            card.RegisterCallback<ClickEvent>(_ => _onOpenAsset?.Invoke(def));

            // Context menu
            card.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                evt.menu.AppendAction("Duplicate", _ => { _library.Duplicate(def); });
                evt.menu.AppendAction("Delete", _ => { _library.Delete(def); });
                evt.menu.AppendAction("Rename", _ =>
                {
                    // Prompt would be handled by modal
                });
            });

            return card;
        }
    }
}
