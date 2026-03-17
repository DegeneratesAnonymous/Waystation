// Content Registry — central loader for all data-driven content.
// Reads JSON files from StreamingAssets/data/ (and mods/).
// Validates, indexes by ID, and exposes content to game systems.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Core
{
    public class ContentRegistry : MonoBehaviour
    {
        // ── Public content tables ────────────────────────────────────────────
        public Dictionary<string, EventDefinition>  Events     { get; private set; } = new Dictionary<string, EventDefinition>();
        public Dictionary<string, NPCTemplate>      Npcs       { get; private set; } = new Dictionary<string, NPCTemplate>();
        public Dictionary<string, ShipTemplate>     Ships      { get; private set; } = new Dictionary<string, ShipTemplate>();
        public Dictionary<string, ClassDefinition>  Classes    { get; private set; } = new Dictionary<string, ClassDefinition>();
        public Dictionary<string, FactionDefinition>Factions   { get; private set; } = new Dictionary<string, FactionDefinition>();
        public Dictionary<string, ModuleDefinition> Modules    { get; private set; } = new Dictionary<string, ModuleDefinition>();
        public Dictionary<string, ItemDefinition>   Items      { get; private set; } = new Dictionary<string, ItemDefinition>();
        public Dictionary<string, JobDefinition>    Jobs       { get; private set; } = new Dictionary<string, JobDefinition>();
        public Dictionary<string, BuildableDefinition> Buildables { get; private set; } = new Dictionary<string, BuildableDefinition>();
        public Dictionary<string, MissionDefinition>Missions   { get; private set; } = new Dictionary<string, MissionDefinition>();
        public Dictionary<string, RoomTypeDefinition> RoomTypes  { get; private set; } = new Dictionary<string, RoomTypeDefinition>();
        public Dictionary<string, ResearchNodeDefinition> ResearchNodes { get; private set; } = new Dictionary<string, ResearchNodeDefinition>();

        public bool   IsLoaded  { get; private set; }
        public int    ErrorCount => _errors.Count;

        private readonly List<string> _errors = new List<string>();

        // ── Singleton ────────────────────────────────────────────────────────
        public static ContentRegistry Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ── Public load entry point ──────────────────────────────────────────

        /// <summary>Load all core data from StreamingAssets/data/.</summary>
        public IEnumerator LoadCoreAsync()
        {
            string dataRoot = Path.Combine(Application.streamingAssetsPath, "data");
            yield return StartCoroutine(LoadFolder(dataRoot, "events",     LoadEvent));
            yield return StartCoroutine(LoadFolder(dataRoot, "npcs",       LoadNpc));
            yield return StartCoroutine(LoadFolder(dataRoot, "ships",      LoadShip));
            yield return StartCoroutine(LoadFolder(dataRoot, "classes",    LoadClass));
            yield return StartCoroutine(LoadFolder(dataRoot, "factions",   LoadFaction));
            yield return StartCoroutine(LoadFolder(dataRoot, "modules",    LoadModule));
            yield return StartCoroutine(LoadFolder(dataRoot, "items",      LoadItem));
            yield return StartCoroutine(LoadFolder(dataRoot, "jobs",       LoadJob));
            yield return StartCoroutine(LoadFolder(dataRoot, "buildables", LoadBuildable));
            yield return StartCoroutine(LoadFolder(dataRoot, "missions",   LoadMission));
            yield return StartCoroutine(LoadFolder(dataRoot, "rooms",      LoadRoomType));
            yield return StartCoroutine(LoadFolder(dataRoot, "research",   LoadResearchNode));
            IsLoaded = true;
            Debug.Log($"[ContentRegistry] Loaded — events:{Events.Count} npcs:{Npcs.Count} " +
                      $"ships:{Ships.Count} classes:{Classes.Count} factions:{Factions.Count} " +
                      $"modules:{Modules.Count} items:{Items.Count} jobs:{Jobs.Count} " +
                      $"buildables:{Buildables.Count} missions:{Missions.Count} " +
                      $"roomTypes:{RoomTypes.Count} researchNodes:{ResearchNodes.Count}");
        }

        // ── Folder loader ────────────────────────────────────────────────────

        private IEnumerator LoadFolder(string root, string folder,
                                        Action<Dictionary<string, object>> register)
        {
            string path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) yield break;

            foreach (string file in Directory.GetFiles(path, "*.json"))
            {
                string json = File.ReadAllText(file);
                // Use MiniJSON for all parsing — JsonUtility cannot handle
                // arbitrary object graphs (Dictionary<string,object> etc.).
                var list = MiniJSON.Json.Deserialize(json) as List<object>;
                if (list == null) continue;
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object> dict)
                    {
                        try { register(dict); }
                        catch (Exception ex)
                        {
                            string id = dict.ContainsKey("id") ? dict["id"].ToString() : "?";
                            _errors.Add($"[{folder}/{id}] {ex.Message}");
                            Debug.LogError($"[ContentRegistry] Error loading {folder}/{id}: {ex.Message}");
                        }
                    }
                }
                yield return null;
            }
        }

        // ── Per-type registrars ──────────────────────────────────────────────

        private void LoadEvent  (Dictionary<string, object> d) => Events  [d.GetString("id")] = EventDefinition  .FromDict(d);
        private void LoadNpc    (Dictionary<string, object> d) => Npcs    [d.GetString("id")] = NPCTemplate      .FromDict(d);
        private void LoadShip   (Dictionary<string, object> d) => Ships   [d.GetString("id")] = ShipTemplate     .FromDict(d);
        private void LoadClass  (Dictionary<string, object> d) => Classes [d.GetString("id")] = ClassDefinition  .FromDict(d);
        private void LoadFaction(Dictionary<string, object> d) => Factions[d.GetString("id")] = FactionDefinition.FromDict(d);
        private void LoadModule (Dictionary<string, object> d) => Modules [d.GetString("id")] = ModuleDefinition .FromDict(d);
        private void LoadItem     (Dictionary<string, object> d) => Items     [d.GetString("id")] = ItemDefinition     .FromDict(d);
        private void LoadJob      (Dictionary<string, object> d) => Jobs      [d.GetString("id")] = JobDefinition      .FromDict(d);
        private void LoadBuildable(Dictionary<string, object> d) => Buildables[d.GetString("id")] = BuildableDefinition.FromDict(d);
        private void LoadMission  (Dictionary<string, object> d) => Missions  [d.GetString("id")] = MissionDefinition  .FromDict(d);
        private void LoadResearchNode(Dictionary<string, object> d) => ResearchNodes[d.GetString("id")] = ResearchNodeDefinition.FromDict(d);
        private void LoadRoomType(Dictionary<string, object> d)
        {
            var rt = new RoomTypeDefinition
            {
                id           = d.GetString("id"),
                displayName  = d.GetString("display_name", d.GetString("id")),
                isBuiltIn    = true,
                workbenchCap = d.GetInt("workbench_cap", 3),
            };
            if (d.ContainsKey("requirements_per_workbench") &&
                d["requirements_per_workbench"] is List<object> reqs)
            {
                foreach (var reqObj in reqs)
                {
                    if (reqObj is Dictionary<string, object> r)
                    {
                        rt.requirementsPerWorkbench.Add(new RoomFurnitureRequirement
                        {
                            buildableIdOrTag  = r.GetString("buildable_id_or_tag"),
                            countPerWorkbench = r.GetInt("count", 1),
                            displayLabel      = r.GetString("display_label",
                                               r.GetString("buildable_id_or_tag")),
                        });
                    }
                }
            }
            if (d.ContainsKey("skill_bonuses") &&
                d["skill_bonuses"] is Dictionary<string, object> sb)
            {
                foreach (var kv in sb)
                    rt.skillBonuses[kv.Key] = Convert.ToSingle(kv.Value);
            }
            RoomTypes[rt.id] = rt;
        }

        // ── Diagnostics ──────────────────────────────────────────────────────

        public string Summary() =>
            $"events:{Events.Count} npcs:{Npcs.Count} ships:{Ships.Count} " +
            $"classes:{Classes.Count} factions:{Factions.Count} modules:{Modules.Count} " +
            $"items:{Items.Count} jobs:{Jobs.Count} buildables:{Buildables.Count} " +
            $"researchNodes:{ResearchNodes.Count}" +
            (_errors.Count > 0 ? $" | errors:{_errors.Count}" : "");

        public IReadOnlyList<string> Errors() => _errors;
    }
}
