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
        public List<string> requiredTags      = new List<string>();
        public List<string> excludedTags      = new List<string>();
        /// <summary>
        /// Reactive trigger names this event responds to.  When the EventSystem fires a
        /// reactive trigger (e.g. "mood_crisis_entry"), all events whose reactiveTriggers
        /// list contains that name are evaluated for eligibility and the best match fires.
        /// Events with reactive triggers can still fire on the normal schedule if their
        /// trigger conditions are met independently.
        /// </summary>
        public List<string>         reactiveTriggers   = new List<string>();
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
            foreach (var t in raw.GetStringList("required_tags"))      e.requiredTags.Add(t);
            foreach (var t in raw.GetStringList("excluded_tags"))      e.excludedTags.Add(t);
            foreach (var t in raw.GetStringList("followup_events"))    e.followupEvents.Add(t);
            foreach (var t in raw.GetStringList("reactive_triggers"))  e.reactiveTriggers.Add(t);
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
        // Base pocket carry capacity (kg) for this NPC type, derived from species physiology.
        // Total carry capacity = pocketCapacity + equipped backpack's carryCapacity.
        public float pocketCapacity = 10f;
        public string schemaVersion = "1";

        // Per-need depletion rate multipliers for this species/template.
        // Keys: "sleep" | "hunger" | "thirst" | "recreation" | "social" | "hygiene"
        // Values: multiplier (1.0 = baseline, >1 depletes faster, <1 depletes slower).
        // Omitted keys default to 1.0.
        public Dictionary<string, float> needDepletionRates = new Dictionary<string, float>();

        public static NPCTemplate FromDict(Dictionary<string, object> raw)
        {
            var t = new NPCTemplate
            {
                id            = raw.GetString("id"),
                baseClass     = raw.GetString("base_class"),
                pocketCapacity = raw.GetFloat("pocket_capacity", 10f),
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
            if (raw.ContainsKey("need_depletion_rates") && raw["need_depletion_rates"] is Dictionary<string, object> ndr)
                foreach (var kv in ndr)
                    t.needDepletionRates[kv.Key] = Convert.ToSingle(kv.Value);
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

        // ── Fleet management fields (EXP-003) ─────────────────────────────
        // Maximum number of NPC crew that can be assigned to this ship type.
        public int   crewCapacity          = 0;
        // Mission type tags this ship role is eligible for.
        // e.g. ["scout", "exploration"] for a scout vessel.
        public List<string> eligibleMissionTypes = new List<string>();
        // Materials required to build this ship at a Shipyard workbench.
        public Dictionary<string, int> buildMaterials = new Dictionary<string, int>();
        // Base ticks to complete construction at a Shipyard.
        public int   buildTimeTicks        = 0;
        // When true, this template is reserved for player fleet use only and must
        // not appear in VisitorSystem's random arrival selection.
        public bool  fleetOnly             = false;

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
                crewCapacity     = raw.GetInt("crew_capacity", 0),
                buildTimeTicks   = raw.GetInt("build_time_ticks", 0),
                fleetOnly        = raw.GetBool("fleet_only", false),
            };
            foreach (var s in raw.GetStringList("faction_restrictions"))    t.factionRestrictions.Add(s);
            foreach (var s in raw.GetStringList("behavior_tags"))           t.behaviorTags.Add(s);
            foreach (var s in raw.GetStringList("resources_wanted"))        t.resourcesWanted.Add(s);
            foreach (var s in raw.GetStringList("eligible_mission_types"))  t.eligibleMissionTypes.Add(s);
            if (raw.ContainsKey("build_materials") &&
                raw["build_materials"] is Dictionary<string, object> mats)
            {
                foreach (var kv in mats)
                    t.buildMaterials[kv.Key] = Convert.ToInt32(kv.Value);
            }
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

    // =========================================================================
    // Trait System — types loaded from data/traits/ and data/trait_pools/
    // =========================================================================

    // -------------------------------------------------------------------------
    // Trait enums — append-only
    // -------------------------------------------------------------------------

    /// <summary>High-level category that groups related traits together.
    /// Used for conflict detection (only same-category traits can conflict)
    /// and for faction trait aggregation.</summary>
    public enum TraitCategory { Psychological, Social, Economic, Physical, Ideological }

    /// <summary>Whether a trait has a positive, negative, or neutral connotation.
    /// Used for display tinting and tension calculation.</summary>
    public enum TraitValence { Positive, Negative, Neutral }

    /// <summary>Which stat or behaviour a TraitEffect modifies.</summary>
    public enum TraitEffectTarget
    {
        MoodModifier,
        WorkSpeedModifier,
        SocialModifier,
        HostilityModifier,
        LoyaltyModifier,
        // Extend as needed — append-only
    }

    /// <summary>Sustained conditions that push pressure into a trait pool bucket.</summary>
    public enum TraitConditionCategory
    {
        ResourceScarcity,
        ResourceAbundance,
        Overcrowding,
        Isolation,
        Danger,
        Stability,
        LowMood,
        HighMood,
        // Life event categories — added by NPC-005
        WitnessDeath,
        SurgeryFailure,
        ExtendedCombat,
        LongTermMentoring,
        SustainedStarvation,
        // Extend as needed — append-only
    }

    // -------------------------------------------------------------------------
    // TraitEffect — a single stat/behaviour modifier applied by a trait
    // -------------------------------------------------------------------------

    [Serializable]
    public struct TraitEffect
    {
        public TraitEffectTarget target;
        public float             magnitude;

        public static TraitEffect FromDict(Dictionary<string, object> raw)
        {
            var targetStr = raw.GetString("target");
            var target = System.Enum.TryParse<TraitEffectTarget>(targetStr, out var t)
                ? t : TraitEffectTarget.MoodModifier;
            return new TraitEffect
            {
                target    = target,
                magnitude = raw.GetFloat("magnitude"),
            };
        }
    }

    // -------------------------------------------------------------------------
    // NpcTraitDefinition — static definition of a single trait
    // -------------------------------------------------------------------------

    [Serializable]
    public class NpcTraitDefinition
    {
        public string        traitId;
        public string        displayName;
        public string        description;
        public TraitCategory category;
        public TraitValence  valence;

        /// <summary>
        /// Fraction of trait strength lost per in-game day.
        /// 0 = no passive decay. Ignored when requiresEventToRemove = true.
        /// </summary>
        public float         decayRatePerDay;

        /// <summary>
        /// When true, passive decay does not apply — removal requires an external
        /// event trigger via TraitSystem.TriggerEventRemoval().
        /// </summary>
        public bool          requiresEventToRemove;

        /// <summary>Trait IDs (same category) that conflict with this one on acquisition.</summary>
        public List<string>      conflictingTraitIds = new List<string>();

        /// <summary>
        /// Named axis shared with conflicting traits.
        /// When a conflict is detected on this axis, both traits are replaced by
        /// conflictDowngradeTarget (if defined) rather than simply being deleted.
        /// </summary>
        public string conflictAxis;

        /// <summary>
        /// Trait ID to add when two traits on the same conflictAxis collide.
        /// If null or empty, the conflict falls back to deleting both traits.
        /// </summary>
        public string conflictDowngradeTarget;

        /// <summary>
        /// When true, this trait can be removed by a completed counselling/therapy session.
        /// External systems should check this flag before calling TriggerEventRemoval().
        /// </summary>
        public bool therapyRemovable;

        /// <summary>
        /// When true, this trait can be removed when a medical recovery milestone is reached
        /// (e.g., all wounds healed).  MedicalTickSystem checks this flag automatically.
        /// </summary>
        public bool medicalRemovable;

        /// <summary>Stat/behaviour modifiers applied while this trait is active.</summary>
        public List<TraitEffect> effects             = new List<TraitEffect>();

        public static NpcTraitDefinition FromDict(Dictionary<string, object> raw)
        {
            var def = new NpcTraitDefinition
            {
                traitId                 = raw.GetString("id"),
                displayName             = raw.GetString("display_name", raw.GetString("id")),
                description             = raw.GetString("description", ""),
                decayRatePerDay         = raw.GetFloat("decay_rate_per_day"),
                requiresEventToRemove   = raw.GetBool("requires_event_to_remove"),
                therapyRemovable        = raw.GetBool("therapy_removable"),
                medicalRemovable        = raw.GetBool("medical_removable"),
                conflictAxis            = raw.GetString("conflict_axis", ""),
                conflictDowngradeTarget = raw.GetString("conflict_downgrade_target", ""),
            };

            var catStr = raw.GetString("category");
            if (System.Enum.TryParse<TraitCategory>(catStr, out var cat)) def.category = cat;

            var valStr = raw.GetString("valence");
            if (System.Enum.TryParse<TraitValence>(valStr, out var val)) def.valence = val;

            foreach (var s in raw.GetStringList("conflicting_trait_ids"))
                def.conflictingTraitIds.Add(s);

            if (raw.ContainsKey("effects") && raw["effects"] is List<object> efxList)
                foreach (var obj in efxList)
                    if (obj is Dictionary<string, object> efxDict)
                        def.effects.Add(TraitEffect.FromDict(efxDict));

            return def;
        }
    }

    // -------------------------------------------------------------------------
    // TraitPool — maps a TraitConditionCategory to a weighted set of traits
    // -------------------------------------------------------------------------

    [Serializable]
    public class WeightedTraitEntry
    {
        public string traitId;
        public float  weight;
    }

    [Serializable]
    public class TraitPoolDefinition
    {
        public string                 poolId;
        public TraitConditionCategory conditionCategory;
        public List<WeightedTraitEntry> entries = new List<WeightedTraitEntry>();

        public static TraitPoolDefinition FromDict(Dictionary<string, object> raw)
        {
            var pool = new TraitPoolDefinition
            {
                poolId = raw.GetString("id"),
            };
            var catStr = raw.GetString("condition_category");
            if (System.Enum.TryParse<TraitConditionCategory>(catStr, out var cat))
                pool.conditionCategory = cat;

            if (raw.ContainsKey("entries") && raw["entries"] is List<object> entryList)
            {
                foreach (var obj in entryList)
                {
                    if (obj is Dictionary<string, object> d)
                        pool.entries.Add(new WeightedTraitEntry
                        {
                            traitId = d.GetString("trait_id"),
                            weight  = d.GetFloat("weight", 1f),
                        });
                }
            }
            return pool;
        }
    }

    // =========================================================================
    // Faction Government — government type enum and succession state
    // =========================================================================

    /// <summary>Determines how an NPCs' trait profiles are aggregated into faction behaviour.</summary>
    public enum GovernmentType
    {
        Democracy,        // Distributed power, high consensual legitimacy  (Social dominant)
        Republic,         // Balanced power, earned legitimacy              (Ideological dominant, low Physical)
        Monarchy,         // Centralised power, traditional legitimacy      (Psychological dominant, low Ideological)
        Authoritarian,    // Centralised power, coercive legitimacy         (Psychological dominant, high Ideological)
        CorporateVassal,  // Balanced power, economic legitimacy            (Economic dominant, low Social)
        Pirate,           // Distributed (anarchic) power, no legitimacy   (Physical dominant, low Social)
        Theocracy,        // Centralised power, ideological divine mandate  (Ideological dominant, high Physical)
        Technocracy,      // Balanced power, merit-based legitimacy         (Economic dominant, high Social)
        FederalCouncil,   // Distributed power, accepted multi-group order  (Physical dominant, high Social)
        // Extend as needed — append-only
    }

    /// <summary>Succession health of a faction's leadership.</summary>
    public enum SuccessionState { Stable, Contested, Vacant }

    // =========================================================================
    // Region Simulation — enums for region and resource models
    // =========================================================================

    /// <summary>Discovery/simulation phase of a region.</summary>
    public enum RegionSimulationState { Undiscovered, OnHorizon, Discovered, FullyMapped }

    /// <summary>Resource types tracked by regional resource history.</summary>
    public enum ResourceType { Food, Water, Power, Medicine, Materials, Space }

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

        // ── Government System fields ─────────────────────────────────────────
        public GovernmentType governmentType         = GovernmentType.Democracy;

        /// <summary>UIDs of all NPC members of this faction.</summary>
        public List<string>   memberNpcIds           = new List<string>();

        /// <summary>UIDs of NPCs holding leadership roles (used by non-democratic aggregation).</summary>
        public List<string>   leaderNpcIds           = new List<string>();

        /// <summary>For CorporateVassal — ID of the parent faction whose leaders apply top-tier weighting.</summary>
        public string         vassalParentFactionId  = null;

        public SuccessionState successionState       = SuccessionState.Stable;

        // ── Stability fields ─────────────────────────────────────────────────

        /// <summary>
        /// Current stability score (0–100). Initialised to 50 on generation; updated
        /// each day by FactionGovernmentSystem based on four inputs:
        /// economic prosperity, military strength, population mood/cohesion, government tenure.
        /// </summary>
        public float stabilityScore = 50f;

        /// <summary>
        /// Number of ticks the current government type has been in power without shifting.
        /// Incremented each day; reset to 0 on any government shift.
        /// </summary>
        public int governmentTenureTicks = 0;

        // ── Procedural Generation fields ─────────────────────────────────────

        /// <summary>
        /// UID of the sector where this faction's territory is centred.
        /// Set by FactionProceduralGenerator when the faction is generated on sector unlock.
        /// Null for static (data-file-loaded) factions that predate procedural generation.
        /// </summary>
        public string sectorUid = null;

        /// <summary>
        /// True if this faction was procedurally generated at runtime rather than loaded
        /// from a data file.  Procedurally-generated factions are stored in
        /// StationState.generatedFactions, not in ContentRegistry.Factions.
        /// </summary>
        public bool isGenerated = false;

        /// <summary>
        /// Disposition override for starting-scenario factions only.
        /// "friendly"   → faction is seeded with a positive reputation (+45).
        /// "unfriendly" → faction is seeded with a negative reputation (-45).
        /// Null for all other factions (reputation starts at 0 and drifts normally).
        /// </summary>
        public string startingDisposition = null;

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

            var govStr = raw.GetString("government_type");
            if (System.Enum.TryParse<GovernmentType>(govStr, out var gov)) f.governmentType = gov;
            foreach (var s in raw.GetStringList("member_npc_ids"))  f.memberNpcIds.Add(s);
            foreach (var s in raw.GetStringList("leader_npc_ids"))  f.leaderNpcIds.Add(s);
            f.vassalParentFactionId = raw.GetString("vassal_parent_faction_id", null);
            var succStr = raw.GetString("succession_state");
            if (System.Enum.TryParse<SuccessionState>(succStr, out var succ)) f.successionState = succ;

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
        // Additional carry capacity (kg) added when this item is equipped in the backpack slot.
        // 0 for most items; positive for bags and packs.
        public float  carryCapacity   = 0f;
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
                carryCapacity   = raw.GetFloat("carry_capacity", 0f),
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
    // Skill and Expertise definitions — loaded from data/skills/ and data/expertise/
    // -------------------------------------------------------------------------

    /// <summary>
    /// Whether a skill uses a single governing ability (Simple) or a composite
    /// multi-term formula (Advanced) for skill checks.
    /// </summary>
    public enum SkillType
    {
        /// <summary>Check = skill_level + governing_ability_mod + need_mod.</summary>
        Simple,
        /// <summary>Check = skill_level + governing_ability_mod + composite_terms_sum + need_mod.</summary>
        Advanced,
    }

    /// <summary>
    /// How a domain skill's two stats are combined for the score formula (NPC-013).
    /// </summary>
    public enum SkillWeight
    {
        /// <summary>score = primary_stat + secondary_stat / 2  (integer division).</summary>
        PrimaryDominant,
        /// <summary>score = (primary_stat + secondary_stat) / 2.</summary>
        EqualWeight,
    }

    /// <summary>
    /// One selectable expertise option within a domain skill's expertise slot (NPC-013).
    /// Embedded directly in <see cref="DomainExpertiseSlotDefinition"/>.
    /// </summary>
    [Serializable]
    public class DomainExpertiseOptionDefinition
    {
        /// <summary>Unique ID added to NPCInstance.chosenExpertise when this option is claimed.</summary>
        public string id;
        /// <summary>Display name shown in the expertise picker UI.</summary>
        public string name;
        /// <summary>Flavour / mechanical description shown in the UI.</summary>
        public string description = "";
        /// <summary>
        /// Task tag strings that are soft-gated by this expertise.
        /// NPCs without this expertise can attempt tasks tagged here but receive SoftLockPenalty.
        /// </summary>
        public List<string> taskTagsUnlocked = new List<string>();

        public static DomainExpertiseOptionDefinition FromDict(Dictionary<string, object> raw)
        {
            var opt = new DomainExpertiseOptionDefinition
            {
                id          = raw.GetString("id"),
                name        = raw.GetString("name", raw.GetString("id")),
                description = raw.GetString("description", ""),
            };
            foreach (var tag in raw.GetStringList("task_tags_unlocked"))
                opt.taskTagsUnlocked.Add(tag);
            return opt;
        }
    }

    /// <summary>
    /// A group of expertise options that unlock at a specific skill level (NPC-013).
    /// Each domain skill embeds one or two of these slots (at level 4 and level 8).
    /// </summary>
    [Serializable]
    public class DomainExpertiseSlotDefinition
    {
        /// <summary>Skill level at which this slot's options become selectable.</summary>
        public int unlockLevel;
        /// <summary>Options the NPC can choose from when this slot unlocks (typically 2–3).</summary>
        public List<DomainExpertiseOptionDefinition> options = new List<DomainExpertiseOptionDefinition>();

        public static DomainExpertiseSlotDefinition FromDict(Dictionary<string, object> raw)
        {
            var slot = new DomainExpertiseSlotDefinition
            {
                unlockLevel = raw.GetInt("unlock_level", 4),
            };
            foreach (var item in raw.GetList("options"))
                slot.options.Add(DomainExpertiseOptionDefinition.FromDict(item.AsStringDict()));
            return slot;
        }
    }

    /// <summary>
    /// One additional term in an Advanced skill's composite check formula.
    /// Each term contributes weight × source_value to the final roll.
    /// </summary>
    [Serializable]
    public class SkillCompositeTermDefinition
    {
        /// <summary>"ability" or "skill".</summary>
        public string termType  = "ability";
        /// <summary>Ability name (STR/DEX/INT/WIS/CHA/END) when termType == "ability".</summary>
        public string ability   = "";
        /// <summary>Skill ID (e.g. "skill.medical") when termType == "skill".</summary>
        public string skillId   = "";
        /// <summary>Multiplier applied to the modifier/level before adding to the roll.</summary>
        public float  weight    = 1.0f;

        public static SkillCompositeTermDefinition FromDict(Dictionary<string, object> raw)
            => new SkillCompositeTermDefinition
            {
                termType = raw.GetString("type", "ability"),
                ability  = raw.GetString("ability", ""),
                skillId  = raw.GetString("skill_id", ""),
                weight   = raw.GetFloat("weight", 1.0f),
            };
    }

    /// <summary>
    /// Bonus type used by ExpertiseBonusDefinition.
    /// </summary>
    public enum ExpertiseBonusType
    {
        WorkSpeed,
        YieldMultiplier,
        XPGain,
        MoodModifier,
        SocialChance,
        ResearchOutput,
    }

    /// <summary>
    /// A single passive bonus granted by an expertise choice.
    /// </summary>
    [Serializable]
    public class ExpertiseBonusDefinition
    {
        /// <summary>Type of bonus applied.</summary>
        public ExpertiseBonusType bonusType;
        /// <summary>Skill this bonus targets (empty = all skills / global).</summary>
        public string targetSkillId = "";
        /// <summary>Bonus magnitude. For multiplicative bonuses, 1.2 = +20%.</summary>
        public float value = 1.0f;

        public static ExpertiseBonusDefinition FromDict(Dictionary<string, object> raw)
        {
            var b = new ExpertiseBonusDefinition
            {
                targetSkillId = raw.GetString("target_skill_id", ""),
                value         = raw.GetFloat("value", 1.0f),
            };
            b.bonusType = Enum.TryParse<ExpertiseBonusType>(raw.GetString("bonus_type"),
                          true, out var bt) ? bt : ExpertiseBonusType.WorkSpeed;
            return b;
        }
    }

    /// <summary>
    /// Static definition of a skill, loaded from data/skills/*.json.
    /// Supports both the legacy flat-skill schema (WO-NPC-004) and the domain-skill
    /// schema introduced in WO-NPC-013 (primary_stat / secondary_stat / weight /
    /// expertise_slots fields).
    /// </summary>
    [Serializable]
    public class SkillDefinition
    {
        public string       skillId;
        public string       displayName;
        public string       description       = "";
        /// <summary>Simple = single governing ability; Advanced = composite multi-term formula.</summary>
        public SkillType    skillType         = SkillType.Simple;
        /// <summary>
        /// Which ability score governs this skill's check modifier.
        /// Valid values: STR / DEX / INT / WIS / CHA / END.
        /// Empty = no modifier.
        /// </summary>
        public string       governingAbility  = "";
        /// <summary>
        /// Additional formula terms for Advanced skills.
        /// Each term adds weight × (ability modifier or skill level) to the check roll.
        /// Empty for Simple skills.
        /// </summary>
        public List<SkillCompositeTermDefinition> compositeTerms = new List<SkillCompositeTermDefinition>();
        /// <summary>Task type strings (from FarmingTaskInstance.taskType / job id) that award XP.</summary>
        public List<string> associatedTaskTypes = new List<string>();
        /// <summary>XP awarded per task completion.</summary>
        public float        xpPerTaskCompletion = 10f;
        /// <summary>XP awarded per active second (for workstation-based skills).</summary>
        public float        xpPerActiveSecond   = 0f;

        // ── Domain-skill fields (WO-NPC-013) ─────────────────────────────────

        /// <summary>
        /// Primary ability stat used in the domain formula: score = primary_stat + secondary_stat / 2.
        /// Valid values: STR / DEX / INT / WIS / CHA / END.  Empty for legacy skills.
        /// </summary>
        public string       primaryStat  = "";
        /// <summary>
        /// Secondary ability stat used in the domain formula.
        /// Valid values: STR / DEX / INT / WIS / CHA / END.  Empty for legacy skills.
        /// </summary>
        public string       secondaryStat = "";
        /// <summary>Formula weight mode for combining primary and secondary stats.</summary>
        public SkillWeight  weight        = SkillWeight.PrimaryDominant;
        /// <summary>
        /// When true, an NPC without proficiency in this skill caps at level 6 and
        /// gains XP at 50% of the normal rate.
        /// </summary>
        public bool         proficiencyRequiredForMaxLevel = false;
        /// <summary>
        /// Expertise slots embedded directly in this skill definition (NPC-013).
        /// Each slot unlocks at a specific skill level (typically 4 and 8).
        /// </summary>
        public List<DomainExpertiseSlotDefinition> domainExpertiseSlots
            = new List<DomainExpertiseSlotDefinition>();

        /// <summary>True when this skill uses the domain-skill schema (NPC-013).</summary>
        public bool IsDomainSkill => !string.IsNullOrEmpty(primaryStat);

        public static SkillDefinition FromDict(Dictionary<string, object> raw)
        {
            var s = new SkillDefinition
            {
                skillId             = raw.GetString("id"),
                displayName         = raw.GetString("display_name",
                                          raw.GetString("name", raw.GetString("id"))),
                description         = raw.GetString("description", ""),
                governingAbility    = raw.GetString("governing_ability", ""),
                xpPerTaskCompletion = raw.GetFloat("xp_per_task_completion", 10f),
                xpPerActiveSecond   = raw.GetFloat("xp_per_active_second",   0f),
                // Domain-skill fields
                primaryStat         = raw.GetString("primary_stat",  ""),
                secondaryStat       = raw.GetString("secondary_stat", ""),
                proficiencyRequiredForMaxLevel =
                    raw.GetBool("proficiency_required_for_max_level", false),
            };
            s.skillType = Enum.TryParse<SkillType>(raw.GetString("skill_type", "Simple"),
                          true, out var st) ? st : SkillType.Simple;
            s.weight = Enum.TryParse<SkillWeight>(
                raw.GetString("weight", "primary_dominant").Replace("_", ""),
                true, out var sw) ? sw : SkillWeight.PrimaryDominant;
            foreach (var t in raw.GetStringList("associated_task_types"))
                s.associatedTaskTypes.Add(t);
            foreach (var item in raw.GetList("composite_terms"))
                s.compositeTerms.Add(SkillCompositeTermDefinition.FromDict(item.AsStringDict()));
            foreach (var item in raw.GetList("expertise_slots"))
                s.domainExpertiseSlots.Add(DomainExpertiseSlotDefinition.FromDict(item.AsStringDict()));
            return s;
        }
    }

    /// <summary>
    /// Static definition of an expertise choice, loaded from data/expertise/*.json.
    /// </summary>
    [Serializable]
    public class ExpertiseDefinition
    {
        public string       expertiseId;
        public string       displayName;
        public string       description   = "";
        public string       flavourText   = "";
        /// <summary>The skill that must be levelled to qualify (skill.skillId).</summary>
        public string       requiredSkillId    = "";
        /// <summary>Minimum level of the required skill to unlock this expertise.</summary>
        public int          requiredSkillLevel = 0;
        /// <summary>Passive bonuses applied while this expertise is active.</summary>
        public List<ExpertiseBonusDefinition> passiveBonuses = new List<ExpertiseBonusDefinition>();
        /// <summary>
        /// Hard-locked task types: NPC without this expertise cannot be assigned these tasks.
        /// </summary>
        public List<string> capabilityUnlocks = new List<string>();
        /// <summary>
        /// Soft-locked task types: NPC without this expertise can attempt these tasks but
        /// receives a performance penalty scalar applied to job duration / output.
        /// </summary>
        public List<string> softCapabilityUnlocks = new List<string>();

        public static ExpertiseDefinition FromDict(Dictionary<string, object> raw)
        {
            var e = new ExpertiseDefinition
            {
                expertiseId        = raw.GetString("id"),
                displayName        = raw.GetString("display_name", raw.GetString("id")),
                description        = raw.GetString("description", ""),
                flavourText        = raw.GetString("flavour_text", ""),
                requiredSkillId    = raw.GetString("required_skill_id", ""),
                requiredSkillLevel = raw.GetInt("required_skill_level", 0),
            };
            foreach (var item in raw.GetList("passive_bonuses"))
                e.passiveBonuses.Add(ExpertiseBonusDefinition.FromDict(item.AsStringDict()));
            foreach (var cap in raw.GetStringList("capability_unlocks"))
                e.capabilityUnlocks.Add(cap);
            foreach (var cap in raw.GetStringList("soft_capability_unlocks"))
                e.softCapabilityUnlocks.Add(cap);
            return e;
        }
    }

    // -------------------------------------------------------------------------
    // Scenario Definition — data-driven starting conditions for a new game.
    // Loaded from StreamingAssets/data/scenarios/*.json
    // -------------------------------------------------------------------------

    [Serializable]
    public class ScenarioDefinition
    {
        public string id;
        public string name;
        public string description;
        public int    difficultyRating;       // 1 (easiest) to 5 (hardest)

        /// <summary>NPC template IDs for the starting crew (in order).</summary>
        public List<string> crewComposition = new List<string>();

        /// <summary>Override starting resource amounts. Keys match StationState.resources keys.</summary>
        public Dictionary<string, float> startingResources = new Dictionary<string, float>();

        /// <summary>Ship template IDs for player-owned ships added at game start (EXP-003).</summary>
        public List<string> startingShips = new List<string>();

        /// <summary>Optional seed override for the station layout RNG. Null = use station-name hash.</summary>
        public int? layoutSeed;

        /// <summary>
        /// Faction disposition preset for the two starting adjacent factions.
        /// "standard" = one friendly, one unfriendly (default).
        /// </summary>
        public string startingFactionDisposition = "standard";

        public static ScenarioDefinition FromDict(Dictionary<string, object> d)
        {
            // Clamp difficulty to the documented range [1, 5] so out-of-range JSON values
            // cannot cause runtime exceptions (e.g. negative string repeat counts in the UI).
            int rawDifficulty = d.GetInt("difficulty_rating", 2);
            int difficulty    = Math.Max(1, Math.Min(5, rawDifficulty));

            var s = new ScenarioDefinition
            {
                id                          = d.GetString("id"),
                name                        = d.GetString("name"),
                description                 = d.GetString("description"),
                difficultyRating            = difficulty,
                startingFactionDisposition  = d.GetString("starting_faction_disposition", "standard"),
            };
            if (d.TryGetValue("layout_seed", out var seedObj) && seedObj != null)
                s.layoutSeed = Convert.ToInt32(seedObj);
            foreach (var npcId in d.GetStringList("crew_composition"))
                s.crewComposition.Add(npcId);
            if (d.TryGetValue("starting_resources", out var resObj) &&
                resObj is Dictionary<string, object> resDict)
            {
                foreach (var kv in resDict)
                    s.startingResources[kv.Key] = Convert.ToSingle(kv.Value);
            }
            foreach (var shipId in d.GetStringList("starting_ships"))
                s.startingShips.Add(shipId);
            return s;
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
    // Research enums and ResearchNodeDefinition
    // -------------------------------------------------------------------------

    public enum ResearchBranch { Industry, Exploration, Diplomacy, Security, Science }

    public enum ResearchSubbranch
    {
        // Industry
        Production, Engineering, Biology,
        // Exploration
        Navigation, Geology, Astrometrics,
        // Diplomacy
        Trade, Relations, Entrepreneurship,
        // Security
        Defence, Intelligence, Command,
        // Science
        Physics, Materials, Xenobiology
    }

    [Serializable]
    public class ResearchNodeDefinition
    {
        public string         id;
        public string         displayName;
        public string         description  = "";
        public ResearchBranch    branch;
        public ResearchSubbranch subbranch;
        public int            pointCost    = 100;
        public List<string>   prerequisites = new List<string>();
        public List<string>   unlockTags    = new List<string>();

        public static ResearchNodeDefinition FromDict(Dictionary<string, object> raw)
        {
            var b = new ResearchNodeDefinition
            {
                id          = raw.GetString("id"),
                displayName = raw.GetString("display_name", raw.GetString("id")),
                description = raw.GetString("description", ""),
                pointCost   = raw.GetInt("point_cost", 100),
            };
            b.branch    = Enum.TryParse<ResearchBranch>(   raw.GetString("branch"),    out var br) ? br : ResearchBranch.Science;
            b.subbranch = Enum.TryParse<ResearchSubbranch>(raw.GetString("subbranch"), out var sr) ? sr : ResearchSubbranch.Physics;
            foreach (var p in raw.GetStringList("prerequisites")) b.prerequisites.Add(p);
            foreach (var t in raw.GetStringList("unlock_tags"))   b.unlockTags.Add(t);
            return b;
        }
    }

    // -------------------------------------------------------------------------
    // Map enums
    // -------------------------------------------------------------------------

    public enum MapViewLevel { System, Sector, Quadrant, Galaxy }

    public enum PoiType { Asteroid, TradePost, AbandonedStation, NebulaPocket }

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
        // When true this container can be picked up and hauled to a new location by NPCs.
        // Non-portable containers (false) are fixed in place once constructed.
        public bool   portable          = false;

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

        // ── Utility network simulation fields ─────────────────────────────────
        // nodeRole: "producer" | "consumer" | "storage" | "conduit" | "isolator" | null
        public string nodeRole             = null;

        // Electrical
        public float  outputWatts          = 0f;   // ElectricalProducer — watts generated per tick
        public float  demandWatts          = 0f;   // ElectricalConsumer — watts consumed per tick
        public float  storageCapacityWh    = 0f;   // ElectricalStorage  — max watt-hours stored

        // Plumbing
        public string fluidType            = null; // "water" | "coolant" | "fuel"
        public float  fluidProducePerTick  = 0f;   // FluidProducer — litres per tick
        public float  fluidDemandPerTick   = 0f;   // FluidConsumer — litres per tick
        public float  fluidStorageCapacity = 0f;   // FluidStorage  — max litres

        // Ducting
        public string gasType              = null; // "oxygen" | "carbon_dioxide" | "nitrogen"
        public float  gasProducePerTick    = 0f;   // GasProducer  — litres-equivalent per tick
        public float  gasDemandPerTick     = 0f;   // GasConsumer  — litres-equivalent per tick
        public float  gasStorageCapacity   = 0f;   // GasStorage   — max litres-equivalent

        // Fuel Lines
        public float  fuelProducePerTick   = 0f;   // FuelProducer — litres per tick
        public float  fuelDemandPerTick    = 0f;   // FuelConsumer — litres per tick
        public float  fuelStorageCapacity  = 0f;   // FuelStorage  — max litres

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
            b.portable              = raw.GetBool("portable", false);
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
            // Utility network simulation
            b.nodeRole             = raw.GetString("node_role", null);
            b.outputWatts          = raw.GetFloat("output_watts", 0f);
            b.demandWatts          = raw.GetFloat("demand_watts", 0f);
            b.storageCapacityWh    = raw.GetFloat("storage_capacity_wh", 0f);
            b.fluidType            = raw.GetString("fluid_type", null);
            b.fluidProducePerTick  = raw.GetFloat("fluid_produce_per_tick", 0f);
            b.fluidDemandPerTick   = raw.GetFloat("fluid_demand_per_tick", 0f);
            b.fluidStorageCapacity = raw.GetFloat("fluid_storage_capacity", 0f);
            b.gasType              = raw.GetString("gas_type", null);
            b.gasProducePerTick    = raw.GetFloat("gas_produce_per_tick", 0f);
            b.gasDemandPerTick     = raw.GetFloat("gas_demand_per_tick", 0f);
            b.gasStorageCapacity   = raw.GetFloat("gas_storage_capacity", 0f);
            b.fuelProducePerTick   = raw.GetFloat("fuel_produce_per_tick", 0f);
            b.fuelDemandPerTick    = raw.GetFloat("fuel_demand_per_tick", 0f);
            b.fuelStorageCapacity  = raw.GetFloat("fuel_storage_capacity", 0f);
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

    // -------------------------------------------------------------------------
    // Trait Lineage System
    // -------------------------------------------------------------------------

    /// <summary>
    /// Defines one position on a lineage axis.
    /// position: -2/-1 = negative tier 2/1; +1/+2 = positive tier 1/2; 0 = neutral (no trait).
    /// </summary>
    [Serializable]
    public class LineageAxisEntry
    {
        public int    position;   // -2, -1, 0, +1, +2
        public string traitId;    // null for position 0
        public string tierName;   // "Iron Stomach", "Hardy", etc.
    }

    /// <summary>
    /// Static definition of a trait lineage loaded from data/npcs/core_trait_lineages.json.
    /// Each lineage defines an axis of related positive/negative traits.
    /// </summary>
    [Serializable]
    public class TraitLineageDefinition
    {
        public string                lineageId;
        public string                displayName;
        /// <summary>Axis entries keyed by position (-2 to +2).</summary>
        public List<LineageAxisEntry> axisEntries   = new List<LineageAxisEntry>();
        /// <summary>Event IDs (stubbed) that push the axis in the positive direction.</summary>
        public List<string>           positiveTriggerEvents = new List<string>();
        /// <summary>Event IDs (stubbed) that push the axis in the negative direction.</summary>
        public List<string>           negativeTriggerEvents = new List<string>();
        /// <summary>Base probability that a qualifying trigger fires a lineage change (0-1).</summary>
        public float                  baseTriggerChance = 0.30f;
        /// <summary>Each point of WIS modifier reduces negative trigger chance by this amount.</summary>
        public float                  wisModifierReductionPerPoint = 0.05f;

        /// <summary>Returns the traitId for a given axis position, or null for neutral.</summary>
        public string GetTraitIdAtPosition(int position)
        {
            if (position == 0) return null;
            foreach (var e in axisEntries)
                if (e.position == position) return e.traitId;
            return null;
        }

        public static TraitLineageDefinition FromDict(Dictionary<string, object> raw)
        {
            var def = new TraitLineageDefinition
            {
                lineageId                  = raw.GetString("id"),
                displayName                = raw.GetString("display_name", raw.GetString("id")),
                baseTriggerChance          = raw.GetFloat("base_trigger_chance", 0.30f),
                wisModifierReductionPerPoint = raw.GetFloat("wis_modifier_reduction_per_point", 0.05f),
            };
            if (raw.ContainsKey("axis_entries") && raw["axis_entries"] is List<object> axis)
            {
                foreach (var aObj in axis)
                {
                    if (aObj is Dictionary<string, object> a)
                    {
                        def.axisEntries.Add(new LineageAxisEntry
                        {
                            position = a.GetInt("position"),
                            traitId  = a.GetString("trait_id", null),
                            tierName = a.GetString("tier_name", ""),
                        });
                    }
                }
            }
            foreach (var s in raw.GetStringList("positive_trigger_events")) def.positiveTriggerEvents.Add(s);
            foreach (var s in raw.GetStringList("negative_trigger_events")) def.negativeTriggerEvents.Add(s);
            return def;
        }
    }

    // -------------------------------------------------------------------------
    // Resource Definition — data-driven configuration for a station resource.
    // Loaded from StreamingAssets/data/resources/*.json.
    // Adding a new resource type requires only a new JSON entry here; no code changes.
    // -------------------------------------------------------------------------

    [Serializable]
    public class ResourceDefinition
    {
        public string id;

        /// <summary>Player alert fires when the resource falls below this amount.</summary>
        public float warningThreshold = 0f;

        /// <summary>Resources will not be produced beyond this passive cap.</summary>
        public float softCap = float.MaxValue;

        /// <summary>
        /// When true, modules that consume this resource enter a degraded state when
        /// the resource hits zero (cascade failure).  Set false for credits.
        /// </summary>
        public bool causesModuleCascade = true;

        /// <summary>
        /// When true, NPCs receive a need-deprivation mood penalty when this resource
        /// hits zero — enforcing the sequence: NPC suffering → module degradation.
        /// </summary>
        public bool causesNpcDeprivation = false;

        /// <summary>
        /// Mood penalty magnitude applied to each crew NPC when this resource is depleted
        /// (used when <see cref="causesNpcDeprivation"/> is true).  Stored as a positive value;
        /// applied as a negative delta to moodScore.  Defined in balance data.
        /// </summary>
        public float npcDeprivationPenalty = 10f;

        /// <summary>
        /// When true, depletion restricts player actions (hire / purchase) rather than
        /// triggering a module cascade.  Intended for credits only.
        /// </summary>
        public bool isCreditResource = false;

        /// <summary>
        /// Maximum production bonus from high morale (e.g. 0.15 = +15 %).
        /// Effective only on the entry with id == "morale_balance".
        /// </summary>
        public float moraleScalarMax = 0.15f;

        /// <summary>
        /// Maximum production penalty from low morale (e.g. -0.15 = −15 %).
        /// Effective only on the entry with id == "morale_balance".
        /// </summary>
        public float moraleScalarMin = -0.15f;

        public static ResourceDefinition FromDict(Dictionary<string, object> raw)
        {
            return new ResourceDefinition
            {
                id                   = raw.GetString("id"),
                warningThreshold     = raw.GetFloat("warning_threshold", 0f),
                softCap              = raw.GetFloat("soft_cap", float.MaxValue),
                causesModuleCascade  = raw.GetBool("causes_module_cascade", true),
                causesNpcDeprivation = raw.GetBool("causes_npc_deprivation", false),
                npcDeprivationPenalty = raw.GetFloat("npc_deprivation_penalty", 10f),
                isCreditResource     = raw.GetBool("is_credit_resource", false),
                moraleScalarMax      = raw.GetFloat("morale_scalar_max", 0.15f),
                moraleScalarMin      = raw.GetFloat("morale_scalar_min", -0.15f),
            };
        }
    }

    // =========================================================================
    // RecipeDefinition — data for a craftable recipe executed at a workbench.
    // Loaded from data/recipes/*.json by ContentRegistry.
    // =========================================================================

    [Serializable]
    public class RecipeDefinition
    {
        public string id;
        public string displayName;
        public string description = "";

        // The BuildableDefinition.workbenchRoomType this recipe requires.
        // e.g. "general_workshop", "refinery", "medical_bay"
        public string requiredWorkbenchType;

        // Station tag (from ResearchSystem) that must be active for this recipe to appear.
        // Empty string means the recipe is always available (no research gate).
        public string unlockTag = "";

        // Input materials consumed when the recipe executes: itemId → quantity.
        public Dictionary<string, int> inputMaterials = new Dictionary<string, int>();

        // Output item and quantity produced on completion.
        public string outputItemId;
        public int    outputQuantity = 1;

        // Base execution time in ticks (modified by crafting skill).
        public int baseTimeTicks = 60;

        // Minimum NPC Crafting skill level required to execute this recipe.
        public int skillRequirement = 0;

        // When true, the output item quality tier scales with the NPC's crafting skill.
        // Applicable for items like medical supplies and exotic components.
        // When false (basic construction materials etc.) quality is always "standard".
        public bool hasQualityTiers = false;

        public static RecipeDefinition FromDict(Dictionary<string, object> raw)
        {
            var r = new RecipeDefinition
            {
                id                   = raw.GetString("id"),
                displayName          = raw.GetString("display_name", raw.GetString("id")),
                description          = raw.GetString("description", ""),
                requiredWorkbenchType = raw.GetString("required_workbench_type", ""),
                unlockTag            = raw.GetString("unlock_tag", ""),
                outputItemId         = raw.GetString("output_item_id"),
                outputQuantity       = raw.GetInt("output_quantity", 1),
                baseTimeTicks        = raw.GetInt("base_time_ticks", 60),
                skillRequirement     = raw.GetInt("skill_requirement", 0),
                hasQualityTiers      = raw.GetBool("has_quality_tiers", false),
            };
            if (raw.ContainsKey("input_materials") &&
                raw["input_materials"] is Dictionary<string, object> im)
            {
                foreach (var kv in im)
                    r.inputMaterials[kv.Key] = Convert.ToInt32(kv.Value);
            }
            return r;
        }
    }

    // =========================================================================
    // Game Balance Config
    // =========================================================================

    /// <summary>
    /// Top-level game balance parameters loaded from
    /// StreamingAssets/data/balance/game_balance.json.
    /// Parsed once at startup by ContentRegistry; consumed by TensionSystem
    /// and other systems that expose configurable tuning values.
    /// </summary>
    public class GameBalanceConfig
    {
        /// <summary>
        /// Number of ticks the player has to intervene once a DepartureRisk NPC
        /// announces intent to leave.  Defaults to 3 in-game days (3 × TicksPerDay).
        /// </summary>
        public int interventionWindowTicks = 1080;

        /// <summary>
        /// Minimum skill-check roll required for a successful intervention attempt.
        /// A roll of (d20 + GetSkillCheckResult(...)) must meet or exceed this DC,
        /// where GetSkillCheckResult(...) includes skill level, ability modifiers,
        /// and any relevant need/composite modifiers.
        /// </summary>
        public int interventionSkillCheckDC = 10;
        /// <summary>
        /// Base research points generated per active researcher per tick before
        /// skill/workbench multipliers are applied.
        /// </summary>
        public float researchPointsPerNpcPerTick = 0.04f;

        public static GameBalanceConfig FromDict(Dictionary<string, object> raw)
        {
            return new GameBalanceConfig
            {
                interventionWindowTicks  = raw.GetInt("intervention_window_ticks",  1080),
                interventionSkillCheckDC = raw.GetInt("intervention_skill_check_dc", 10),
                researchPointsPerNpcPerTick = raw.GetFloat("research_points_per_npc_per_tick", 0.04f),
            };
        }
    }
}
