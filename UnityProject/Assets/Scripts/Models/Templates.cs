// Static template definitions — loaded from data files, never mutated at runtime.
// Templates are the authoring surface. Instances are the runtime entities.
using System;
using System.Collections.Generic;

namespace Waystation.Models
{
    // -------------------------------------------------------------------------
    // Shared building blocks
    // -------------------------------------------------------------------------

    [Serializable]
    public class SkillRange
    {
        public int min;
        public int max;

        public static SkillRange FromRaw(object raw)
        {
            if (raw is List<object> list && list.Count == 2)
                return new SkillRange { min = Convert.ToInt32(list[0]), max = Convert.ToInt32(list[1]) };
            if (raw is Dictionary<string, object> dict)
                return new SkillRange
                {
                    min = dict.ContainsKey("min") ? Convert.ToInt32(dict["min"]) : 0,
                    max = dict.ContainsKey("max") ? Convert.ToInt32(dict["max"]) : 10
                };
            return new SkillRange { min = 0, max = 10 };
        }
    }

    [Serializable]
    public class ConditionBlock
    {
        public string type;     // e.g. "tag_present", "resource_below", "faction_rep_above"
        public string target;   // what the condition checks against
        public object value;    // threshold or comparison value
        public bool negate;

        public static ConditionBlock FromDict(Dictionary<string, object> raw) =>
            new ConditionBlock
            {
                type   = raw.GetString("type"),
                target = raw.GetString("target"),
                value  = raw.ContainsKey("value") ? raw["value"] : null,
                negate = raw.GetBool("negate")
            };
    }

    [Serializable]
    public class OutcomeEffect
    {
        public string type;     // e.g. "add_resource", "spawn_npc", "set_tag"
        public string target;
        public object value;
        public Dictionary<string, object> args;

        public static OutcomeEffect FromDict(Dictionary<string, object> raw)
        {
            var args = new Dictionary<string, object>();
            foreach (var kv in raw)
                if (kv.Key != "type" && kv.Key != "target" && kv.Key != "value")
                    args[kv.Key] = kv.Value;
            return new OutcomeEffect
            {
                type   = raw.GetString("type"),
                target = raw.GetString("target"),
                value  = raw.ContainsKey("value") ? raw["value"] : null,
                args   = args
            };
        }
    }

    [Serializable]
    public class EventChoice
    {
        public string id;
        public string label;
        public List<ConditionBlock> conditions = new List<ConditionBlock>();
        public List<OutcomeEffect> outcomes    = new List<OutcomeEffect>();
        public string followupEvent;   // event ID to queue after this choice

        public static EventChoice FromDict(Dictionary<string, object> raw)
        {
            var c = new EventChoice
            {
                id            = raw.GetString("id"),
                label         = raw.GetString("label"),
                followupEvent = raw.GetString("followup_event")
            };
            foreach (var item in raw.GetList("conditions"))
                c.conditions.Add(ConditionBlock.FromDict(item.AsStringDict()));
            foreach (var item in raw.GetList("outcomes"))
                c.outcomes.Add(OutcomeEffect.FromDict(item.AsStringDict()));
            return c;
        }
    }

    // -------------------------------------------------------------------------
    // Event Definition
    // -------------------------------------------------------------------------

    [Serializable]
    public class EventDefinition
    {
        public string id;
        public string category;      // arrival / station / faction / incident / random
        public string title;
        public string description;
        public float  weight      = 1f;
        public int    cooldown    = 0;
        public List<string> requiredTags  = new List<string>();
        public List<string> excludedTags  = new List<string>();
        public List<ConditionBlock> triggerConditions = new List<ConditionBlock>();
        public List<EventChoice>    choices            = new List<EventChoice>();
        public List<OutcomeEffect>  autoOutcomes       = new List<OutcomeEffect>();
        public List<string>         followupEvents     = new List<string>();
        public bool hostile      = false;
        public int  expiresIn    = 0;
        public string schemaVersion = "1";

        public static EventDefinition FromDict(Dictionary<string, object> raw)
        {
            var e = new EventDefinition
            {
                id            = raw.GetString("id"),
                category      = raw.GetString("category", "random"),
                title         = raw.GetString("title"),
                description   = raw.GetString("description"),
                weight        = raw.GetFloat("weight", 1f),
                cooldown      = raw.GetInt("cooldown"),
                hostile       = raw.GetBool("hostile"),
                expiresIn     = raw.GetInt("expires_in"),
                schemaVersion = raw.GetString("schema_version", "1")
            };
            foreach (var t in raw.GetStringList("required_tags")) e.requiredTags.Add(t);
            foreach (var t in raw.GetStringList("excluded_tags")) e.excludedTags.Add(t);
            foreach (var t in raw.GetStringList("followup_events")) e.followupEvents.Add(t);
            foreach (var item in raw.GetList("trigger_conditions"))
                e.triggerConditions.Add(ConditionBlock.FromDict(item.AsStringDict()));
            foreach (var item in raw.GetList("choices"))
                e.choices.Add(EventChoice.FromDict(item.AsStringDict()));
            foreach (var item in raw.GetList("auto_outcomes"))
                e.autoOutcomes.Add(OutcomeEffect.FromDict(item.AsStringDict()));
            return e;
        }
    }

    // -------------------------------------------------------------------------
    // NPC Template
    // -------------------------------------------------------------------------

    [Serializable]
    public class NPCTemplate
    {
        public string id;
        public string baseClass;
        public List<string>              allowedSubclasses = new List<string>();
        public Dictionary<string, SkillRange> skillRanges = new Dictionary<string, SkillRange>();
        public List<string>              traitPool         = new List<string>();
        public Dictionary<string, float> factionBias       = new Dictionary<string, float>();
        public List<string>              namePool          = new List<string>();
        public List<string>              equipmentPool     = new List<string>();
        public Dictionary<string, object> spawnRules       = new Dictionary<string, object>();
        public string schemaVersion = "1";

        public static NPCTemplate FromDict(Dictionary<string, object> raw)
        {
            var t = new NPCTemplate
            {
                id            = raw.GetString("id"),
                baseClass     = raw.GetString("base_class"),
                schemaVersion = raw.GetString("schema_version", "1")
            };
            foreach (var s in raw.GetStringList("allowed_subclasses"))  t.allowedSubclasses.Add(s);
            foreach (var s in raw.GetStringList("trait_pool"))           t.traitPool.Add(s);
            foreach (var s in raw.GetStringList("name_pool"))            t.namePool.Add(s);
            foreach (var s in raw.GetStringList("equipment_pool"))       t.equipmentPool.Add(s);
            if (raw.ContainsKey("skill_ranges") && raw["skill_ranges"] is Dictionary<string, object> sr)
                foreach (var kv in sr)
                    t.skillRanges[kv.Key] = SkillRange.FromRaw(kv.Value);
            if (raw.ContainsKey("faction_bias") && raw["faction_bias"] is Dictionary<string, object> fb)
                foreach (var kv in fb)
                    t.factionBias[kv.Key] = Convert.ToSingle(kv.Value);
            return t;
        }
    }

    // -------------------------------------------------------------------------
    // Ship Template
    // -------------------------------------------------------------------------

    [Serializable]
    public class ShipTemplate
    {
        public string id;
        public string role;  // trader / refugee / raider / inspector / transport
        public List<string> factionRestrictions = new List<string>();
        public int   cargoCapacity     = 0;
        public int   passengerCapacity = 0;
        public int   threatLevel       = 0;
        public List<string> behaviorTags = new List<string>();
        public string schemaVersion = "1";

        // ── Visitor system fields ──────────────────────────────────────────
        // Social resistance: 1 (easy to hail) – 10 (very reluctant). Crew social
        // skill check must beat socialResistance * 2.
        public int   socialResistance      = 5;
        // Resource types this ship is actively seeking (matches station resource keys)
        public List<string> resourcesWanted = new List<string>();
        // True when the ship is attracted by station entertainment / hab quality
        public bool  hasEntertainmentNeed  = false;
        // Number of visitors spawned when the shuttle docks
        public int   visitorCount          = 2;
        // How long visitors stay once docked (seconds in real-time; converted to ticks)
        public float visitDuration         = 120f;
        // Sprite variant index into npc_ship atlas (0–3)
        public int   spriteVariant         = 0;

        public static ShipTemplate FromDict(Dictionary<string, object> raw)
        {
            var t = new ShipTemplate
            {
                id               = raw.GetString("id"),
                role             = raw.GetString("role"),
                cargoCapacity    = raw.GetInt("cargo_capacity"),
                passengerCapacity= raw.GetInt("passenger_capacity"),
                threatLevel      = raw.GetInt("threat_level"),
                schemaVersion    = raw.GetString("schema_version", "1"),
                socialResistance = raw.GetInt("social_resistance", 5),
                hasEntertainmentNeed = raw.GetBool("has_entertainment_need", false),
                visitorCount     = raw.GetInt("visitor_count", 2),
                visitDuration    = raw.GetFloat("visit_duration", 120f),
                spriteVariant    = raw.GetInt("sprite_variant", 0),
            };
            foreach (var s in raw.GetStringList("faction_restrictions")) t.factionRestrictions.Add(s);
            foreach (var s in raw.GetStringList("behavior_tags"))         t.behaviorTags.Add(s);
            foreach (var s in raw.GetStringList("resources_wanted"))      t.resourcesWanted.Add(s);
            return t;
        }
    }

    // -------------------------------------------------------------------------
    // Class Definition
    // -------------------------------------------------------------------------

    [Serializable]
    public class ClassDefinition
    {
        public string id;
        public string parent;
        public string displayName;
        public string description;
        public Dictionary<string, float> modifiers  = new Dictionary<string, float>();
        public List<string>              allowedJobs = new List<string>();
        public List<string>              unlockHooks = new List<string>();
        public string schemaVersion = "1";

        public static ClassDefinition FromDict(Dictionary<string, object> raw)
        {
            var c = new ClassDefinition
            {
                id            = raw.GetString("id"),
                parent        = raw.GetString("parent"),
                displayName   = raw.GetString("display_name", raw.GetString("id")),
                description   = raw.GetString("description"),
                schemaVersion = raw.GetString("schema_version", "1")
            };
            foreach (var s in raw.GetStringList("allowed_jobs")) c.allowedJobs.Add(s);
            foreach (var s in raw.GetStringList("unlock_hooks"))  c.unlockHooks.Add(s);
            if (raw.ContainsKey("modifiers") && raw["modifiers"] is Dictionary<string, object> mods)
                foreach (var kv in mods)
                    c.modifiers[kv.Key] = Convert.ToSingle(kv.Value);
            return c;
        }
    }

    // -------------------------------------------------------------------------
    // Faction Definition
    // -------------------------------------------------------------------------

    [Serializable]
    public class FactionDefinition
    {
        public string id;
        public string displayName;
        public string type        = "minor";   // major / regional / minor
        public string description;
        public List<string> ideologyTags  = new List<string>();
        public List<string> behaviorTags  = new List<string>();
        public Dictionary<string, float>  relationships    = new Dictionary<string, float>();
        public Dictionary<string, object> diplomacyProfile = new Dictionary<string, object>();
        public Dictionary<string, object> economicProfile  = new Dictionary<string, object>();
        public string schemaVersion = "1";

        public static FactionDefinition FromDict(Dictionary<string, object> raw)
        {
            var f = new FactionDefinition
            {
                id            = raw.GetString("id"),
                displayName   = raw.GetString("display_name", raw.GetString("id")),
                type          = raw.GetString("type", "minor"),
                description   = raw.GetString("description"),
                schemaVersion = raw.GetString("schema_version", "1")
            };
            foreach (var s in raw.GetStringList("ideology_tags")) f.ideologyTags.Add(s);
            foreach (var s in raw.GetStringList("behavior_tags"))  f.behaviorTags.Add(s);
            if (raw.ContainsKey("relationships") && raw["relationships"] is Dictionary<string, object> rels)
                foreach (var kv in rels)
                    f.relationships[kv.Key] = Convert.ToSingle(kv.Value);
            return f;
        }
    }

    // -------------------------------------------------------------------------
    // Item Definition
    // -------------------------------------------------------------------------

    [Serializable]
    public class ItemDefinition
    {
        public string id;
        public string displayName;
        public string itemType;     // Material / Equipment / Biological / Valuables / Waste
        public string subtype;
        public string description;
        public float  weight          = 1f;
        public int    stackSize       = 100;
        public float  value           = 1f;
        public int    perishableTicks = 0;
        public string quality         = "standard";
        public bool   legal           = true;
        public List<string> tags      = new List<string>();
        public Dictionary<string, float> buildCost = new Dictionary<string, float>();
        public string schemaVersion = "1";

        public static ItemDefinition FromDict(Dictionary<string, object> raw)
        {
            var item = new ItemDefinition
            {
                id              = raw.GetString("id"),
                displayName     = raw.GetString("display_name", raw.GetString("id")),
                itemType        = raw.GetString("item_type", "Material"),
                subtype         = raw.GetString("subtype"),
                description     = raw.GetString("description"),
                weight          = raw.GetFloat("weight", 1f),
                stackSize       = raw.GetInt("stack_size", 100),
                value           = raw.GetFloat("value", 1f),
                perishableTicks = raw.GetInt("perishable_ticks"),
                quality         = raw.GetString("quality", "standard"),
                legal           = raw.GetBool("legal", true),
                schemaVersion   = raw.GetString("schema_version", "1")
            };
            foreach (var s in raw.GetStringList("tags")) item.tags.Add(s);
            if (raw.ContainsKey("build_cost") && raw["build_cost"] is Dictionary<string, object> bc)
                foreach (var kv in bc)
                    item.buildCost[kv.Key] = Convert.ToSingle(kv.Value);
            return item;
        }
    }

    // -------------------------------------------------------------------------
    // Module Definition
    // -------------------------------------------------------------------------

    [Serializable]
    public class ModuleDefinition
    {
        public string id;
        public string displayName;
        public string category    = "utility"; // utility / dock / hab / production / security / cargo
        public string description;
        public Dictionary<string, float> resourceEffects = new Dictionary<string, float>();
        public int    capacity       = 0;
        public int    cargoCapacity  = 0;
        public List<string> tags               = new List<string>();
        public List<string> unlockConditions   = new List<string>();
        public string schemaVersion = "1";

        public static ModuleDefinition FromDict(Dictionary<string, object> raw)
        {
            var m = new ModuleDefinition
            {
                id              = raw.GetString("id"),
                displayName     = raw.GetString("display_name", raw.GetString("id")),
                category        = raw.GetString("category", "utility"),
                description     = raw.GetString("description"),
                capacity        = raw.GetInt("capacity"),
                cargoCapacity   = raw.GetInt("cargo_capacity"),
                schemaVersion   = raw.GetString("schema_version", "1")
            };
            foreach (var s in raw.GetStringList("tags"))               m.tags.Add(s);
            foreach (var s in raw.GetStringList("unlock_conditions"))  m.unlockConditions.Add(s);
            if (raw.ContainsKey("resource_effects") && raw["resource_effects"] is Dictionary<string, object> re)
                foreach (var kv in re)
                    m.resourceEffects[kv.Key] = Convert.ToSingle(kv.Value);
            return m;
        }
    }

    // -------------------------------------------------------------------------
    // Job Definition
    // -------------------------------------------------------------------------

    [Serializable]
    public class JobDefinition
    {
        public string id;
        public string displayName;
        public string phase                  = "any"; // "day" | "night" | "any"
        public List<string> allowedClasses   = new List<string>();
        public string preferredModuleCategory = "utility";
        public string fallbackModuleCategory  = "utility";
        public int    durationTicks           = 4;
        public string skillUsed;
        public Dictionary<string, float> resourceEffects = new Dictionary<string, float>();
        public Dictionary<string, float> needEffects     = new Dictionary<string, float>();
        public Dictionary<string, object> stationEffects = new Dictionary<string, object>();
        public string schemaVersion = "1";

        public static JobDefinition FromDict(Dictionary<string, object> raw)
        {
            var j = new JobDefinition
            {
                id                      = raw.GetString("id"),
                displayName             = raw.GetString("display_name", raw.GetString("id")),
                phase                   = raw.GetString("phase", "any"),
                preferredModuleCategory = raw.GetString("preferred_module_category", "utility"),
                fallbackModuleCategory  = raw.GetString("fallback_module_category",  "utility"),
                durationTicks           = raw.GetInt("duration_ticks", 4),
                skillUsed               = raw.GetString("skill_used"),
                schemaVersion           = raw.GetString("schema_version", "1")
            };
            foreach (var s in raw.GetStringList("allowed_classes")) j.allowedClasses.Add(s);
            if (raw.ContainsKey("resource_effects") && raw["resource_effects"] is Dictionary<string, object> re)
                foreach (var kv in re)
                    j.resourceEffects[kv.Key] = Convert.ToSingle(kv.Value);
            if (raw.ContainsKey("need_effects") && raw["need_effects"] is Dictionary<string, object> ne)
                foreach (var kv in ne)
                    j.needEffects[kv.Key] = Convert.ToSingle(kv.Value);
            if (raw.ContainsKey("station_effects") && raw["station_effects"] is Dictionary<string, object> se)
                foreach (var kv in se)
                    j.stationEffects[kv.Key] = kv.Value;
            return j;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers — dictionary extension methods used by all From* factory methods
    // -------------------------------------------------------------------------

    public static class DictExtensions
    {
        public static string GetString(this Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return v.ToString();
            return fallback;
        }

        public static int GetInt(this Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return fallback;
        }

        public static float GetFloat(this Dictionary<string, object> d, string key, float fallback = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToSingle(v);
            return fallback;
        }

        public static bool GetBool(this Dictionary<string, object> d, string key, bool fallback = false)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToBoolean(v);
            return fallback;
        }

        public static List<object> GetList(this Dictionary<string, object> d, string key)
        {
            if (d.TryGetValue(key, out var v) && v is List<object> list)
                return list;
            return new List<object>();
        }

        public static List<string> GetStringList(this Dictionary<string, object> d, string key)
        {
            var result = new List<string>();
            foreach (var item in d.GetList(key))
                result.Add(item?.ToString() ?? "");
            return result;
        }

        public static Dictionary<string, object> AsStringDict(this object obj)
        {
            if (obj is Dictionary<string, object> d) return d;
            return new Dictionary<string, object>();
        }
    }

    // -------------------------------------------------------------------------
    // BuildableDefinition — static data for a placeable construction item.
    // Loaded from data/buildables/*.json by ContentRegistry.
    // -------------------------------------------------------------------------

    [Serializable]
    public class BuildableDefinition
    {
        public string id;
        public string displayName;
        public string description       = "";
        public string category          = "object";   // "structure" | "object"
        public int    buildTimeTicks    = 50;
        public float  buildQuality      = 1.0f;
        public int    size              = 1;
        public int    maxHealth         = 100;
        public int    cargoCapacity     = 0;          // > 0 for storage objects (e.g. cabinet)

        // Tile layer: 1=floor, 2=object/furniture, 3=large object, 4=structural barrier.
        // Loaded from optional "layer" key in YAML; defaults to 1 for "structure" category, 2 otherwise.
        public int    tileLayer         = 1;
        // Multi-tile footprint (default 1×1).
        public int    tileWidth         = 1;
        public int    tileHeight        = 1;

        // item_id → quantity required before construction can begin
        public Dictionary<string, int> requiredMaterials = new Dictionary<string, int>();

        // Station tags that must be active before this buildable can be placed
        public List<string> requiredTags = new List<string>();

        // skill_id → minimum level required (e.g. "engineering" → 2)
        public Dictionary<string, int> requiredSkills = new Dictionary<string, int>();

        // Beauty points this object contributes to its room (0 = none)
        public int  beautyScore  = 0;
        // Optional tag placing this object in a furniture category for room bonus matching.
        // Values: "seating" | "lighting" | "storage" | "surface" | "decor" | null
        public string furnitureTag = null;

        // If true, this object counts toward the room's workbench limit (max 3 per room)
        public bool isWorkbench  = false;

        // ── Room bonus / workbench typing ──────────────────────────────────────
        // When non-null, this object is a typed workbench that can grant a room bonus.
        // e.g. "engineering_workshop", "medical_bay", "research_lab"
        public string workbenchRoomType     = null;
        // Minimum summed beautyScore of OTHER (non-workbench) objects in the room.
        public int    workbenchBeautyReq    = 0;
        // Minimum count of non-workbench, non-floor, non-wall foundations in the room.
        public int    workbenchFurnitureReq = 0;
        // Per-skill multipliers applied to NPCs using workbenches in a qualifying room.
        // e.g. { "technical": 1.25 } means +25% to technical skill output.
        public Dictionary<string, float> workbenchSkillBonuses = null;

        // Network / power properties
        public bool   underWallAllowed = false;   // wire/pipe/duct can pass under walls
        public string networkType      = null;    // "electric" | "pipe" | "duct" | null
        public bool   requiresPower    = false;   // consumes from electric network
        public float  powerDraw        = 0f;      // watts when active
        public float  idlePowerDraw    = 0f;      // watts when standby

        public static BuildableDefinition FromDict(Dictionary<string, object> raw)
        {
            var b = new BuildableDefinition
            {
                id             = raw.GetString("id"),
                displayName    = raw.GetString("display_name", raw.GetString("id")),
                description    = raw.GetString("description", ""),
                category       = raw.GetString("category", "object"),
                buildTimeTicks = raw.GetInt("build_time_ticks", 50),
                buildQuality   = raw.GetFloat("build_quality", 1.0f),
                size           = raw.GetInt("size", 1),
                maxHealth      = raw.GetInt("max_health", 100),
                cargoCapacity  = raw.GetInt("cargo_capacity", 0),
                beautyScore    = raw.GetInt("beauty_score", 0),
                isWorkbench    = raw.GetBool("is_workbench", false),
            };
            b.furnitureTag          = raw.GetString("furniture_tag", null);
            b.workbenchRoomType     = raw.GetString("workbench_room_type", null);
            b.workbenchBeautyReq    = raw.GetInt("workbench_beauty_req", 0);
            b.workbenchFurnitureReq = raw.GetInt("workbench_furniture_req", 0);
            if (raw.ContainsKey("workbench_skill_bonuses") &&
                raw["workbench_skill_bonuses"] is Dictionary<string, object> wsb)
            {
                b.workbenchSkillBonuses = new Dictionary<string, float>();
                foreach (var kv in wsb)
                    b.workbenchSkillBonuses[kv.Key] = Convert.ToSingle(kv.Value);
            }
            b.tileLayer  = raw.GetInt("layer",       b.category == "structure" ? 1 : 2);
            b.tileWidth  = raw.GetInt("tile_width",  1);
            b.tileHeight = raw.GetInt("tile_height", 1);
            b.underWallAllowed = raw.GetBool("under_wall_allowed", false);
            b.networkType      = raw.GetString("network_type", null);
            b.requiresPower    = raw.GetBool("requires_power", false);
            b.powerDraw        = raw.GetFloat("power_draw", 0f);
            b.idlePowerDraw    = raw.GetFloat("idle_power_draw", 0f);
            foreach (var tag in raw.GetStringList("required_tags"))
                b.requiredTags.Add(tag);
            if (raw.ContainsKey("required_materials") &&
                raw["required_materials"] is Dictionary<string, object> mats)
            {
                foreach (var kv in mats)
                    b.requiredMaterials[kv.Key] = Convert.ToInt32(kv.Value);
            }
            if (raw.ContainsKey("required_skills") &&
                raw["required_skills"] is Dictionary<string, object> skills)
            {
                foreach (var kv in skills)
                    b.requiredSkills[kv.Key] = Convert.ToInt32(kv.Value);
            }
            return b;
        }
    }

    // -------------------------------------------------------------------------
    // MissionDefinition — static data for an away mission type.
    // Loaded from data/missions/*.json by ContentRegistry.
    // -------------------------------------------------------------------------

    [Serializable]
    public class MissionDefinition
    {
        public string id;
        public string displayName;
        public string description      = "";
        public string missionType;        // "mining" | "trade" | "patrol"
        public int    durationTicks     = 480;
        public int    crewRequired      = 1;
        public string requiredSkill     = null;
        public int    requiredSkillLevel = 1;
        public float  successChanceBase = 0.7f;   // 0.0–1.0
        public Dictionary<string, float> rewardsOnSuccess = new Dictionary<string, float>();
        public Dictionary<string, float> rewardsOnPartial = new Dictionary<string, float>();

        public static MissionDefinition FromDict(Dictionary<string, object> raw)
        {
            var m = new MissionDefinition
            {
                id                = raw.GetString("id"),
                displayName       = raw.GetString("display_name", raw.GetString("id")),
                description       = raw.GetString("description", ""),
                missionType       = raw.GetString("mission_type", "mining"),
                durationTicks     = raw.GetInt("duration_ticks", 480),
                crewRequired      = raw.GetInt("crew_required", 1),
                requiredSkill     = raw.GetString("required_skill", null),
                requiredSkillLevel = raw.GetInt("required_skill_level", 1),
                successChanceBase  = raw.GetFloat("success_chance_base", 0.7f),
            };
            if (raw.ContainsKey("rewards_on_success") &&
                raw["rewards_on_success"] is Dictionary<string, object> rs)
                foreach (var kv in rs)
                    m.rewardsOnSuccess[kv.Key] = Convert.ToSingle(kv.Value);
            if (raw.ContainsKey("rewards_on_partial") &&
                raw["rewards_on_partial"] is Dictionary<string, object> rp)
                foreach (var kv in rp)
                    m.rewardsOnPartial[kv.Key] = Convert.ToSingle(kv.Value);
            return m;
        }
    }

    // -------------------------------------------------------------------------
    // CropDataDefinition — static data for a growable crop type.
    // Loaded from data/crops/*.json by ContentRegistry.
    // -------------------------------------------------------------------------

    [Serializable]
    public class CropDataDefinition
    {
        public string id;
        public string cropName;
        public string seedItemId;
        public string harvestItemId;
        public int    harvestQtyMin         = 1;
        public int    harvestQtyMax         = 3;
        public float  growthTimePerStage    = 60f;   // seconds at ideal conditions
        public float  idealTempMin          = 18f;
        public float  idealTempMax          = 28f;
        public float  acceptableTempMin     = 10f;
        public float  acceptableTempMax     = 35f;
        public float  idealLightMin         = 0.6f;
        public float  idealLightMax         = 1.0f;
        public float  acceptableLightMin    = 0.3f;
        public float  acceptableLightMax    = 1.0f;
        public bool   requiresWater         = true;
        public float  damagePerSecond       = 5f;    // % damage/tick under critical conditions

        public static CropDataDefinition FromDict(Dictionary<string, object> raw)
        {
            return new CropDataDefinition
            {
                id                 = raw.GetString("id"),
                cropName           = raw.GetString("crop_name", raw.GetString("id")),
                seedItemId         = raw.GetString("seed_item_id"),
                harvestItemId      = raw.GetString("harvest_item_id"),
                harvestQtyMin      = raw.GetInt("harvest_qty_min", 1),
                harvestQtyMax      = raw.GetInt("harvest_qty_max", 3),
                growthTimePerStage = raw.GetFloat("growth_time_per_stage", 60f),
                idealTempMin       = raw.GetFloat("ideal_temp_min", 18f),
                idealTempMax       = raw.GetFloat("ideal_temp_max", 28f),
                acceptableTempMin  = raw.GetFloat("acceptable_temp_min", 10f),
                acceptableTempMax  = raw.GetFloat("acceptable_temp_max", 35f),
                idealLightMin      = raw.GetFloat("ideal_light_min", 0.6f),
                idealLightMax      = raw.GetFloat("ideal_light_max", 1.0f),
                acceptableLightMin = raw.GetFloat("acceptable_light_min", 0.3f),
                acceptableLightMax = raw.GetFloat("acceptable_light_max", 1.0f),
                requiresWater      = raw.GetBool("requires_water", true),
                damagePerSecond    = raw.GetFloat("damage_per_second", 5f),
            };
        }
    }
}
