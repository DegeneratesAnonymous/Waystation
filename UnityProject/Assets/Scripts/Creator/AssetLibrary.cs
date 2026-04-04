using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Waystation.Creator
{
    public class AssetLibrary
    {
        private readonly string _basePath;
        private readonly List<AssetDefinition> _assets = new List<AssetDefinition>();

        public IReadOnlyList<AssetDefinition> Assets => _assets;

        public event Action OnLibraryChanged;

        public AssetLibrary()
        {
            _basePath = Path.Combine(Application.persistentDataPath, "CreatorAssets");
            if (!Directory.Exists(_basePath))
                Directory.CreateDirectory(_basePath);
        }

        public void LoadAll()
        {
            _assets.Clear();
            if (!Directory.Exists(_basePath)) return;

            foreach (var dir in Directory.GetDirectories(_basePath))
            {
                string defPath = Path.Combine(dir, "asset_definition.json");
                if (!File.Exists(defPath)) continue;
                try
                {
                    string json = File.ReadAllText(defPath);
                    var def = AssetDefinitionSerializer.Deserialize(json);
                    if (def != null)
                        _assets.Add(def);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AssetLibrary] Failed to load asset at {dir}: {e.Message}");
                }
            }
            OnLibraryChanged?.Invoke();
        }

        public AssetDefinition Create(string type, string name = null)
        {
            var def = new AssetDefinition
            {
                id = Guid.NewGuid().ToString(),
                name = name ?? GetDefaultName(type),
                type = type,
                created = DateTime.UtcNow.ToString("o"),
                modified = DateTime.UtcNow.ToString("o")
            };

            string dir = Path.Combine(_basePath, def.id);
            Directory.CreateDirectory(dir);
            Save(def);
            _assets.Add(def);
            OnLibraryChanged?.Invoke();
            return def;
        }

        public void Save(AssetDefinition def)
        {
            string dir = Path.Combine(_basePath, def.id);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string json = AssetDefinitionSerializer.Serialize(def);
            File.WriteAllText(Path.Combine(dir, "asset_definition.json"), json);
        }

        public AssetDefinition Duplicate(AssetDefinition source)
        {
            var dup = AssetDefinitionSerializer.Deserialize(
                AssetDefinitionSerializer.Serialize(source));
            dup.id = Guid.NewGuid().ToString();
            dup.name = source.name + " Copy";
            dup.created = DateTime.UtcNow.ToString("o");
            dup.workshop_id = null;

            string srcDir = Path.Combine(_basePath, source.id);
            string dstDir = Path.Combine(_basePath, dup.id);
            Directory.CreateDirectory(dstDir);

            // Copy all files except asset_definition.json (we'll write our own)
            if (Directory.Exists(srcDir))
            {
                foreach (var file in Directory.GetFiles(srcDir))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName != "asset_definition.json")
                        File.Copy(file, Path.Combine(dstDir, fileName));
                }
            }

            Save(dup);
            _assets.Add(dup);
            OnLibraryChanged?.Invoke();
            return dup;
        }

        public void Delete(AssetDefinition def)
        {
            string dir = Path.Combine(_basePath, def.id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            _assets.Remove(def);
            OnLibraryChanged?.Invoke();
        }

        public void Rename(AssetDefinition def, string newName)
        {
            def.name = newName;
            Save(def);
            OnLibraryChanged?.Invoke();
        }

        public string GetAssetDirectory(AssetDefinition def)
        {
            return Path.Combine(_basePath, def.id);
        }

        public string GetAssetDirectory(string assetId)
        {
            return Path.Combine(_basePath, assetId);
        }

        public List<AssetDefinition> Search(string query, string typeFilter = null, string sortBy = null)
        {
            var results = _assets.AsEnumerable();

            if (!string.IsNullOrEmpty(typeFilter) && typeFilter != "all")
                results = results.Where(a => a.type == typeFilter);

            if (!string.IsNullOrEmpty(query))
            {
                string q = query.ToLowerInvariant();
                results = results.Where(a =>
                    (a.name != null && a.name.ToLowerInvariant().Contains(q)) ||
                    (a.tags != null && a.tags.Any(t => t.ToLowerInvariant().Contains(q))));
            }

            sortBy = sortBy ?? CreatorSettings.LibrarySort;
            results = sortBy == "name"
                ? results.OrderBy(a => a.name ?? "")
                : results.OrderByDescending(a => a.modified ?? "");

            return results.ToList();
        }

        private string GetDefaultName(string type)
        {
            switch (type)
            {
                case "floor_tile": return "Untitled Floor";
                case "wall_tile": return "Untitled Wall";
                case "furniture": return "Untitled Furniture";
                default: return "Untitled";
            }
        }
    }
}
