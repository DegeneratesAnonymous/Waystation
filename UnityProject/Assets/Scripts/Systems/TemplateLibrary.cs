// TemplateLibrary — manages the per-save collection of ClothingTemplate entries.
//
// The library is held in memory and persisted as part of save data.  It is
// accessed via a static singleton pattern (initialised by GameManager or
// DemoBootstrap at session start).
//
// Features:
//   • Flat list of ClothingTemplate with search by name or tag.
//   • Add / Duplicate / Delete with confirmation guard.
//   • Export selected templates to JSON string.
//   • Import from JSON string; collisions on templateId resolved by generating
//     a new ID and setting importedFlag = true.
//   • Templates persist indefinitely — no consumption or expiry.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Waystation.Systems
{
    using Waystation.Models;

    public class TemplateLibrary
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static TemplateLibrary _instance;

        public static TemplateLibrary Instance
        {
            get
            {
                _instance ??= new TemplateLibrary();
                return _instance;
            }
        }

        /// <summary>Replaces the singleton with a pre-populated instance (used by save/load).</summary>
        public static void SetInstance(TemplateLibrary lib) => _instance = lib;

        // ── Data ──────────────────────────────────────────────────────────────
        private readonly List<ClothingTemplate> _templates = new List<ClothingTemplate>();

        // ── Read access ───────────────────────────────────────────────────────

        /// <summary>Returns a snapshot list of all templates (in insertion order).</summary>
        public IReadOnlyList<ClothingTemplate> All => _templates;

        /// <summary>
        /// Returns all templates whose name or tags contain <paramref name="query"/>
        /// (case-insensitive).  Returns all when query is null or empty.
        /// </summary>
        public IEnumerable<ClothingTemplate> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return _templates;
            string q = query.Trim().ToLowerInvariant();
            return _templates.Where(t =>
                t.templateName.ToLowerInvariant().Contains(q) ||
                t.tags.Any(tag => tag.ToLowerInvariant().Contains(q)));
        }

        /// <summary>Returns the template with the given ID, or null.</summary>
        public ClothingTemplate Find(string templateId)
            => _templates.FirstOrDefault(t => t.templateId == templateId);

        // ── Mutation ──────────────────────────────────────────────────────────

        /// <summary>Adds a template to the library.  No-op if already present.</summary>
        public void Add(ClothingTemplate template)
        {
            if (template == null) return;
            if (_templates.Any(t => t.templateId == template.templateId)) return;
            _templates.Add(template);
        }

        /// <summary>
        /// Creates a copy of the template with a new ID and revisionNumber = 0,
        /// adds it to the library, and returns it.
        /// </summary>
        public ClothingTemplate Duplicate(string templateId)
        {
            var src = Find(templateId);
            if (src == null) return null;
            var copy = src.Duplicate();
            _templates.Add(copy);
            return copy;
        }

        /// <summary>Removes the template with the given ID.  Returns false if not found.</summary>
        public bool Delete(string templateId)
        {
            int idx = _templates.FindIndex(t => t.templateId == templateId);
            if (idx < 0) return false;
            _templates.RemoveAt(idx);
            return true;
        }

        // ── Import / Export ───────────────────────────────────────────────────

        /// <summary>
        /// Serialises the given templates (or all templates if ids is empty) to a
        /// JSON array string.
        /// </summary>
        public string Export(IEnumerable<string> ids = null)
        {
            List<ClothingTemplate> toExport;
            if (ids == null)
            {
                toExport = new List<ClothingTemplate>(_templates);
            }
            else
            {
                var idSet = new HashSet<string>(ids);
                toExport = _templates.Where(t => idSet.Contains(t.templateId)).ToList();
            }

            // Serialise using Unity's built-in JsonUtility via a wrapper.
            var wrapper = new TemplateListWrapper { templates = toExport };
            return JsonUtility.ToJson(wrapper, prettyPrint: true);
        }

        /// <summary>
        /// Deserialises templates from a JSON string produced by Export.
        /// Handles templateId collisions by generating a new ID and flagging as imported.
        /// Returns the list of templates that were successfully added.
        /// </summary>
        public List<ClothingTemplate> Import(string json)
        {
            var added = new List<ClothingTemplate>();
            if (string.IsNullOrEmpty(json)) return added;

            TemplateListWrapper wrapper;
            try
            {
                wrapper = JsonUtility.FromJson<TemplateListWrapper>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TemplateLibrary] Import failed: {e.Message}");
                return added;
            }

            if (wrapper?.templates == null) return added;

            foreach (var t in wrapper.templates)
            {
                if (t == null) continue;

                // Resolve ID collision.
                if (_templates.Any(existing => existing.templateId == t.templateId))
                {
                    t.templateId = Guid.NewGuid().ToString("N")[..12];
                    t.importedFlag = true;
                }

                _templates.Add(t);
                added.Add(t);
            }

            return added;
        }

        // ── Serialisation wrapper ─────────────────────────────────────────────

        [Serializable]
        private class TemplateListWrapper
        {
            public List<ClothingTemplate> templates;
        }
    }
}
