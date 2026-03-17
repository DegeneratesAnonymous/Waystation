// Runtime instance state — mutable entities created from templates.
// All instances carry a uid (unique runtime ID) and a templateId referencing
// the definition they were generated from.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // -------------------------------------------------------------------------
    // Comm Message — a radio transmission received from a passing or docked ship
    // -------------------------------------------------------------------------

    [Serializable]
    public class CommMessage
    {
        public string uid;
        public string subject;
        public string body;
        public string senderName;
        public string senderType;   // "trade_ship" | "faction" | "system"
        public string shipUid;      // uid of the ship that sent this, if any
        public bool   read     = false;
        public int    tick     = 0;
        public string replied  = null; // action key chosen, null if not yet replied

        // Tick at which this message auto-expires if still unread/unreplied.
        // -1 means no expiry (used for quest messages). Trade messages are given
        // expiresAtTick = tick + 24 (1 in-game day) at creation time.
        public int expiresAtTick = -1;

        // Response options: each entry has "label" and "action" keys, plus
        // payload fields (e.g. "iceQty", "icePrice") set by CommsSystem.
        public List<Dictionary<string, object>> responseOptions =
            new List<Dictionary<string, object>>();

        public bool IsExpired(int currentTick)
            => expiresAtTick >= 0 && currentTick >= expiresAtTick && replied == null;

        public static CommMessage Create(string subject, string body,
                                         string senderName, string senderType,
                                         string shipUid, int tick,
                                         int expiresAtTick = -1)
        {
            return new CommMessage
            {
                uid          = Guid.NewGuid().ToString("N")[..8],
                subject      = subject,
                body         = body,
                senderName   = senderName,
                senderType   = senderType,
                shipUid      = shipUid,
                tick         = tick,
                expiresAtTick = expiresAtTick
            };
        }
    }

    // -------------------------------------------------------------------------
    // Department — a named crew department grouping jobs together
    // -------------------------------------------------------------------------

    [Serializable]
    public class Department
    {
        public string       uid;
        public string       name;
        public List<string> allowedJobs = new List<string>();

        public static Department Create(string uid, string name, List<string> allowedJobs = null)
        {
            return new Department
            {
                uid         = uid,
                name        = name,
                allowedJobs = allowedJobs ?? new List<string>()
            };
        }
    }


    // -------------------------------------------------------------------------
    // NPC Instance
    // -------------------------------------------------------------------------

    [Serializable]
    public class NPCInstance
    {
        public string uid;
        public string templateId;
        public string name;
        public string classId;
        public string subclassId;

        // Derived skills (rolled from template ranges at spawn)
        public Dictionary<string, int>   skills   = new Dictionary<string, int>();
        public List<string>              traits   = new List<string>();

        // Needs — 0.0 (critical) to 1.0 (fully satisfied)
        public Dictionary<string, float> needs    = new Dictionary<string, float>
        {
            { "hunger", 1f }, { "rest", 1f }, { "social", 0.5f }, { "safety", 1f }, { "sleep", 1f }
        };

        // -1.0 (miserable) to 1.0 (content)
        public float mood = 0.5f;

        // Where this NPC is on the station (module definitionId or uid)
        public string location = "commons";

        // Job system
        public string currentJobId;
        public string jobModuleUid;
        public int    jobTimer       = 0;
        public bool   jobInterrupted = false;

        public string       factionId;
        public List<string> statusTags = new List<string>();

        // Skill XP accumulation (float; integer part = skill level to apply)
        public Dictionary<string, float> skillXp   = new Dictionary<string, float>();

        // Injury count — healed over time in med bay
        public int injuries = 0;

        // Arbitrary memory hooks for events to read/write
        public Dictionary<string, object> memory = new Dictionary<string, object>();

        // Species / rank — used for door access control
        public string species = "human";      // e.g. "human", "alien"
        public int    rank    = 0;            // 0=crew, 1=officer, 2=senior, 3=command

        // Department / mission / sleep assignment
        public string departmentId  = null;   // single dept uid; null = Crewman (unassigned)
        public string sleepBedUid   = null;   // uid of claimed bed foundation
        public bool   isSleeping    = false;
        public string missionUid    = null;   // null when not on an away mission

        public static NPCInstance Create(string templateId, string name,
                                         string classId, string subclassId = null)
        {
            var npc = new NPCInstance
            {
                uid        = Guid.NewGuid().ToString("N").Substring(0, 8),
                templateId = templateId,
                name       = name,
                classId    = classId,
                subclassId = subclassId
            };
            npc.needs = new Dictionary<string, float>
            {
                { "hunger", 1f }, { "rest", 1f }, { "social", 0.5f }, { "safety", 1f }, { "sleep", 1f }
            };
            return npc;
        }

        public bool IsCrew()    => statusTags.Contains("crew");
        public bool IsVisitor() => statusTags.Contains("visitor");

        public void UpdateNeeds(Dictionary<string, float> delta)
        {
            foreach (var kv in delta)
            {
                float current = needs.ContainsKey(kv.Key) ? needs[kv.Key] : 0.5f;
                needs[kv.Key] = Mathf.Clamp01(current + kv.Value);
            }
        }

        public void RecalculateMood()
        {
            var weights = new Dictionary<string, float>
            {
                { "hunger", 1f }, { "rest", 1f }, { "social", 0.5f }, { "safety", 2f }, { "sleep", 1f }
            };
            float totalWeight  = 5.5f;
            float weightedSum  = 0f;
            foreach (var kv in weights)
                weightedSum += (needs.ContainsKey(kv.Key) ? needs[kv.Key] : 0.5f) * kv.Value;

            float traitBonus = 0f;
            foreach (var t in traits)
            {
                if (t == "resilient" || t == "optimistic") traitBonus += 0.1f;
                else if (t == "anxious"   || t == "bitter") traitBonus -= 0.1f;
            }
            mood = Mathf.Clamp((weightedSum / totalWeight) * 2f - 1f + traitBonus, -1f, 1f);
        }

        public string MoodLabel()
        {
            if (mood >= 0.6f)  return "content";
            if (mood >= 0.2f)  return "okay";
            if (mood >= -0.2f) return "uneasy";
            if (mood >= -0.6f) return "distressed";
            return "miserable";
        }
    }

    // -------------------------------------------------------------------------
    // Ship Instance
    // -------------------------------------------------------------------------

    [Serializable]
    public class ShipInstance
    {
        public string uid;
        public string templateId;
        public string name;
        public string role;

        public string              factionId;
        public string              intent        = "unknown";
        public Dictionary<string, int>   cargo  = new Dictionary<string, int>();
        public List<string>        passengerUids = new List<string>();
        public int                 threatLevel   = 0;
        public List<string>        behaviorTags  = new List<string>();

        // Docking state
        public string status    = "incoming";  // incoming / docked / departing / hostile / destroyed
        public string dockedAt;
        public int    ticksDocked = 0;
        // Set when the ship docks; used by VisitorSystem to avoid re-rolling departure each tick.
        public int    plannedDepartureTick = -1;

        public static ShipInstance Create(string templateId, string name, string role,
                                          string intent = "unknown", string factionId = null,
                                          int threatLevel = 0)
        {
            return new ShipInstance
            {
                uid         = Guid.NewGuid().ToString("N").Substring(0, 8),
                templateId  = templateId,
                name        = name,
                role        = role,
                intent      = intent,
                factionId   = factionId,
                threatLevel = threatLevel
            };
        }

        public bool IsHostile() => status == "hostile" || intent == "raid";

        public string ThreatLabel()
        {
            if (threatLevel == 0) return "none";
            if (threatLevel <= 2) return "low";
            if (threatLevel <= 5) return "moderate";
            if (threatLevel <= 8) return "high";
            return "extreme";
        }
    }

    // -------------------------------------------------------------------------
    // Cargo Hold Settings
    // -------------------------------------------------------------------------

    [Serializable]
    public class CargoHoldSettings
    {
        /// <summary>
        /// Sentinel value used in allowedTypes to represent "allow nothing".
        /// An empty allowedTypes list means allow everything; a list containing
        /// only this token blocks all item types.
        /// </summary>
        public const string AllowNoneSentinel = "__none__";

        public List<string>              allowedTypes    = new List<string>();
        public Dictionary<string, float> reservedByType = new Dictionary<string, float>();
        public int                       priority        = 0;

        public bool AllowsType(string itemType)
        {
            return allowedTypes.Count == 0 || allowedTypes.Contains(itemType);
        }
    }

    // -------------------------------------------------------------------------
    // Module Instance
    // -------------------------------------------------------------------------

    [Serializable]
    public class ModuleInstance
    {
        public string uid;
        public string definitionId;
        public string displayName;
        public string category;

        public List<string>          occupants   = new List<string>();   // NPC uids
        public string                dockedShip;                          // ship uid
        public bool                  active       = true;
        public float                 damage       = 0f;                   // 0 = fine, 1 = destroyed

        public Dictionary<string, int> inventory  = new Dictionary<string, int>();
        public CargoHoldSettings       cargoSettings;                     // null = not a cargo hold

        public static ModuleInstance Create(string definitionId, string displayName, string category)
        {
            return new ModuleInstance
            {
                uid          = Guid.NewGuid().ToString("N").Substring(0, 8),
                definitionId = definitionId,
                displayName  = displayName,
                category     = category
            };
        }

        public bool IsDock()          => category == "dock";
        public bool IsAvailableDock() => IsDock() && dockedShip == null && active;
    }

    // -------------------------------------------------------------------------
    // Foundation Instance — a partially-built construction site placed by the
    // player and completed by an Engineer NPC.
    // Status lifecycle: "awaiting_haul" → "constructing" → "complete"
    // -------------------------------------------------------------------------

    [Serializable]
    public class FoundationInstance
    {
        public string uid;
        public string buildableId;

        // Tile-grid position (column, row)
        public int tileCol;
        public int tileRow;

        // Rotation in degrees (0 / 90 / 180 / 270) — used for rotatable objects (e.g. cabinet)
        public int tileRotation = 0;

        // "awaiting_haul" | "constructing" | "complete"
        public string status = "awaiting_haul";

        // Materials already moved to the build site: item_id → qty
        public Dictionary<string, int> hauledMaterials = new Dictionary<string, int>();

        // 0.0 → 1.0; reaches 1.0 when construction is done
        public float buildProgress = 0f;

        // Health model (applied once complete)
        public int   maxHealth = 100;
        public int   health    = 100;
        public float quality   = 1.0f;

        // Door status — only relevant when buildableId contains "door".
        // "powered" | "locked" | "unpowered"
        public string doorStatus = "powered";

        // Door hold-open: door stays fully open regardless of NPC proximity.
        public bool doorHoldOpen = false;

        // Door access policy — null means allow all powered NPCs.
        public DoorAccessPolicy accessPolicy = null;

        // uid of the Engineer NPC currently assigned here, or null
        public string assignedNpcUid;

        // Cargo storage — mirrors ModuleInstance capability for placed storage objects.
        // cargoCapacity > 0 means this foundation acts as a cargo hold.
        public int                       cargoCapacity = 0;
        public CargoHoldSettings         cargoSettings;
        public Dictionary<string, int>   cargo         = new Dictionary<string, int>();

        /// Total number of item units currently stored (unweighted).
        public int CargoItemCount()
        {
            int n = 0;
            foreach (var v in cargo.Values) n += v;
            return n;
        }

        /// Fill fraction 0–1 based on item count vs capacity.
        public float CargoFillRatio()
            => cargoCapacity <= 0 ? 0f : UnityEngine.Mathf.Clamp01((float)CargoItemCount() / cargoCapacity);

        // Tile layer set by BuildingSystem.PlaceFoundation():
        // 1=floor, 2=object/furniture, 3=large object, 4=structural barrier.
        public int tileLayer  = 1;
        // Multi-tile footprint (set by BuildingSystem from BuildableDefinition).
        public int tileWidth  = 1;
        public int tileHeight = 1;

        // Network connectivity: uid of the NetworkInstance this foundation belongs to.
        public string networkId      = null;
        // True when a wire/pipe/duct is placed beneath a wall — hidden in Normal view.
        public bool   isUnderWall    = false;
        // Visual operating state for machines ("standby"|"active"|"damaged"|"broken").
        public string operatingState = "standby";

        // ── Utility network simulation state (set each tick by UtilityNetworkManager) ──
        // Electrical: true when network supply+storage >= demand
        public bool  isEnergised      = false;
        // Plumbing: true when fluid network has enough stored volume for this consumer
        public bool  isFluidSupplied  = false;
        // Ducting: true when gas network has enough stored volume for this consumer
        public bool  isGasSupplied    = false;

        // Storage nodes: persisted amounts (serialised in StationData)
        public float storedEnergy     = 0f;  // Battery — watt-hours currently stored
        public float storedFluid      = 0f;  // Water Tank / Fluid Tank — litres stored
        public float storedGas        = 0f;  // Gas Tank — litres-equivalent stored

        // Isolator state: true = open (allows connectivity), false = closed (splits network)
        public bool  isolatorOpen     = true;

        // Room bonus — set by RoomSystem each tick.
        // hasRoomBonus is true when this workbench is in a fully-qualified bonus room.
        // roomBonusMultiplier is the skill output multiplier while the bonus is active.
        // (runtime only — not included in hand-rolled save dictionaries)
        public bool  hasRoomBonus        = false;
        public float roomBonusMultiplier = 1.0f;

        /// <summary>
        /// Functionality based on current HP:
        ///   100–75 %  →  1.0  (full)
        ///   75–50 %   →  linearly degraded (1% worse per 1% HP below 75)
        ///   &lt; 50 %    →  0.0  (non-functional)
        /// </summary>
        public float Functionality()
        {
            if (maxHealth == 0) return 0f;
            float pct = (float)health / maxHealth;
            if (pct >= 0.75f) return 1.0f;
            if (pct >= 0.50f) return (pct - 0.50f) / 0.25f;
            return 0f;
        }

        public bool MaterialsComplete(Dictionary<string, int> required)
        {
            foreach (var kv in required)
            {
                int have = hauledMaterials.ContainsKey(kv.Key) ? hauledMaterials[kv.Key] : 0;
                if (have < kv.Value) return false;
            }
            return true;
        }

        public static FoundationInstance Create(string buildableId, int col, int row,
                                                 int maxHealth = 100, float quality = 1.0f,
                                                 int rotation = 0, int cargoCapacity = 0)
        {
            return new FoundationInstance
            {
                uid           = Guid.NewGuid().ToString("N")[..8],
                buildableId   = buildableId,
                tileCol       = col,
                tileRow       = row,
                maxHealth     = maxHealth,
                health        = maxHealth,
                quality       = quality,
                tileRotation  = rotation,
                cargoCapacity = cargoCapacity,
            };
        }
    }

    // -------------------------------------------------------------------------
    // DoorAccessPolicy — who may pass through a door freely.
    // The door opens for an NPC only when NpcCanPass() returns true.
    // -------------------------------------------------------------------------

    [Serializable]
    public class DoorAccessPolicy
    {
        // If true, the door is completely unrestricted (anyone walks through).
        public bool allowAll = true;

        // Allowed species (empty = no species restriction).
        public List<string> allowedSpecies       = new List<string>();
        // Allowed department uids (empty = no dept restriction).
        public List<string> allowedDepartmentIds = new List<string>();
        // Minimum rank required (0 = no rank restriction).
        public int minRank = 0;
        // Optional faction gate: npcFactionId must match AND rep >= minFactionRep.
        public string requiredFactionId  = null;
        public float  minFactionRep      = 0f;

        /// Returns true when the NPC meets at least one non-empty configured rule
        /// (or allowAll is true, or all rule lists are empty).
        public bool NpcCanPass(NPCInstance npc)
        {
            if (allowAll) return true;

            // Rank gates apply to all NPCs regardless of other rules.
            if (npc.rank < minRank) return false;

            bool speciesOk  = allowedSpecies.Count == 0      || allowedSpecies.Contains(npc.species ?? "human");
            bool deptOk     = allowedDepartmentIds.Count == 0 || allowedDepartmentIds.Contains(npc.departmentId ?? "");
            bool factionOk  = requiredFactionId == null       || npc.factionId == requiredFactionId;

            return speciesOk && deptOk && factionOk;
        }
    }

    // -------------------------------------------------------------------------
    // RoomFurnitureRequirement — one slot in a room type's per-workbench checklist.
    // A room needs (countPerWorkbench × workbenchCount) matching items.
    // buildableIdOrTag: exact buildable id (e.g. "buildable.chair")
    //   OR a tag prefixed with "tag:" (e.g. "tag:lighting") matched via furnitureTag.
    // -------------------------------------------------------------------------
    [Serializable]
    public class RoomFurnitureRequirement
    {
        public string buildableIdOrTag;   // "buildable.chair" OR "tag:lighting"
        public int    countPerWorkbench;  // multiply by # workbenches for total needed
        public string displayLabel;       // shown in UI: "Chair", "Overhead Light"
    }

    // -------------------------------------------------------------------------
    // RoomTypeDefinition — authored definition for a room type.
    // Built-in types are loaded from core_room_types.json (isBuiltIn = true).
    // Custom types are created by the player and stored in StationState.customRoomTypes.
    // -------------------------------------------------------------------------
    [Serializable]
    public class RoomTypeDefinition
    {
        public string id;                // matches workbenchRoomType on BuildableDefinition
        public string displayName;
        public bool   isBuiltIn   = true;  // false for player-created custom types
        public int    workbenchCap = 3;    // max workbenches that earn the bonus
        public List<RoomFurnitureRequirement> requirementsPerWorkbench = new List<RoomFurnitureRequirement>();
        public Dictionary<string, float>      skillBonuses             = new Dictionary<string, float>();
    }

    // -------------------------------------------------------------------------
    // RoomRequirementProgress — runtime progress for one furniture slot.
    // -------------------------------------------------------------------------
    public class RoomRequirementProgress
    {
        public string displayLabel;
        public int    current;
        public int    required;
        public bool   IsMet => current >= required;
    }

    // -------------------------------------------------------------------------
    // RoomBonusState — computed by RoomSystem, stored in StationState.roomBonusCache.
    // Runtime only (not serialised).
    // -------------------------------------------------------------------------
    public class RoomBonusState
    {
        public string       roomKey;            // "minCol_minRow" canonical key
        public string       workbenchRoomType;  // workbenchRoomType of the dominant workbench
        public string       displayName;        // human-readable name from RoomTypeDefinition
        public bool         bonusActive;        // true when all requirements met
        public int          workbenchCount;     // number of workbenches of the dominant type
        public List<string> workbenchUids = new List<string>();
        public List<RoomRequirementProgress> requirements = new List<RoomRequirementProgress>();
    }

    // -------------------------------------------------------------------------
    // Network Instance — a connected graph of wire/pipe/duct foundations
    // -------------------------------------------------------------------------

    [Serializable]
    public class NetworkInstance
    {
        public string       uid;
        public string       networkType;     // "electric" | "pipe" | "duct"
        public string       contentType;     // resource id for pipe/duct; null for electric
        public float        contentAmount;   // current stored amount
        public float        contentCapacity; // max storage in this network
        public List<string> memberUids = new List<string>(); // foundation uids

        // ── Simulation state (updated each tick by UtilityNetworkManager) ────
        public float totalSupply    = 0f;  // watts (electric) or litres/tick (fluid/gas) produced
        public float totalDemand    = 0f;  // watts (electric) or litres/tick (fluid/gas) consumed
        public float storedEnergy   = 0f;  // watt-hours stored across all batteries on this net
        public float storageCapacity = 0f; // total storage capacity across all storage nodes

        public static NetworkInstance Create(string type, string content = null)
        {
            return new NetworkInstance
            {
                uid         = Guid.NewGuid().ToString("N")[..8],
                networkType = type,
                contentType = content,
            };
        }
    }

    // -------------------------------------------------------------------------
    // Mission Instance — an active away mission
    // -------------------------------------------------------------------------

    [Serializable]
    public class MissionInstance
    {
        public string       uid;
        public string       missionType;   // "mining" | "trade" | "patrol"
        public string       displayName;
        public string       definitionId;  // references MissionDefinition.id
        public List<string> crewUids = new List<string>();
        public int          startTick;
        public int          endTick;
        // "active" | "complete" | "failed"
        public string       status = "active";
        public Dictionary<string, float> rewards = new Dictionary<string, float>();

        public static MissionInstance Create(string defId, string type, string name,
                                             int start, int duration)
        {
            return new MissionInstance
            {
                uid          = Guid.NewGuid().ToString("N")[..8],
                definitionId = defId,
                missionType  = type,
                displayName  = name,
                status       = "active",
                startTick    = start,
                endTick      = start + duration,
            };
        }
    }

    // -------------------------------------------------------------------------
    // Station State
    // -------------------------------------------------------------------------

    [Serializable]
    public class StationState
    {
        public string stationName;
        public int    tick = 0;

        // Resources tracked per tick
        public Dictionary<string, float> resources = new Dictionary<string, float>
        {
            { "credits", 500f }, { "food", 100f }, { "power", 100f },
            { "oxygen",  100f }, { "parts",  50f }, { "ice", 200f }
        };

        // Entity registries (keyed by uid)
        public Dictionary<string, NPCInstance>    npcs    = new Dictionary<string, NPCInstance>();
        public Dictionary<string, ShipInstance>   ships   = new Dictionary<string, ShipInstance>();
        public Dictionary<string, ModuleInstance> modules = new Dictionary<string, ModuleInstance>();

        // Faction reputation: factionId -> -100..100
        public Dictionary<string, float>  factionReputation = new Dictionary<string, float>();

        // Active state tags on the station
        public HashSet<string>            activeTags  = new HashSet<string>();

        // Policy flags (player decisions)
        public Dictionary<string, string> policy      = new Dictionary<string, string>();

        // Cooldown tracker: eventId -> tick it can next fire
        public Dictionary<string, int>    eventCooldowns = new Dictionary<string, int>();

        // Active trade offers keyed by ship uid
        public Dictionary<string, object> tradeOffers    = new Dictionary<string, object>();

        // Active build foundations keyed by uid
        public Dictionary<string, FoundationInstance> foundations = new Dictionary<string, FoundationInstance>();

        // Room role designations: canonical floor key "col_row" → role label
        // (key = "minCol_minRow" of the connected floor-tile set that forms the room)
        public Dictionary<string, string> roomRoles = new Dictionary<string, string>();

        // Runtime room bonus cache — rebuilt by RoomSystem.Tick, NOT serialised.
        // (not included in hand-rolled save dictionaries in GameManager.SaveGame)
        public Dictionary<string, RoomBonusState> roomBonusCache = new Dictionary<string, RoomBonusState>();
        // Player-created custom room type definitions (built-ins come from ContentRegistry).
        public List<RoomTypeDefinition>   customRoomTypes = new List<RoomTypeDefinition>();
        // Player-assigned display name per room (key = canonical "col_row" room key).
        public Dictionary<string, string> customRoomNames = new Dictionary<string, string>();

        // Communications inbox (most recent first)
        public List<CommMessage> messages = new List<CommMessage>();

        // Work assignments per NPC: npcUid → list of job ids the NPC is allowed.
        // Empty list means all jobs are allowed for that NPC.
        public Dictionary<string, List<string>> workAssignments = new Dictionary<string, List<string>>();

        // Crew departments
        public List<Department> departments = new List<Department>();

        // Rank name overrides — index corresponds to rank int (0–3).
        // Defaults: Crew, Officer, Senior Officer, Command.
        // Players can rename these per-station.
        public List<string> rankNames = new List<string>
            { "Crew", "Officer", "Senior Officer", "Command" };

        /// Returns the display name for the given rank index, falling back gracefully.
        public string GetRankName(int rank)
        {
            if (rank >= 0 && rank < rankNames.Count) return rankNames[rank];
            return rank == 0 ? "Crew" : $"Rank {rank}";
        }

        // Infrastructure networks (electric, pipe, duct)
        public Dictionary<string, NetworkInstance>  networks = new Dictionary<string, NetworkInstance>();

        // Active away missions
        public Dictionary<string, MissionInstance>  missions = new Dictionary<string, MissionInstance>();

        // Log of recent events / messages (most recent first)
        public List<string>               log            = new List<string>();

        public StationState(string name)
        {
            stationName = name;
            resources = new Dictionary<string, float>
            {
                { "credits", 500f }, { "food", 100f }, { "power", 100f },
                { "oxygen",  100f }, { "parts",  50f }, { "ice", 200f }
            };
            InitDefaultDepartments();
        }

        private void InitDefaultDepartments()
        {
            departments = new List<Department>
            {
                Department.Create("dept.engineering", "Engineering",
                    new List<string> { "job.build", "job.module_maintenance",
                                       "job.power_management", "job.life_support",
                                       "job.haul", "job.refine", "job.craft" }),
                Department.Create("dept.sciences", "Sciences",
                    new List<string> { "job.refine", "job.craft", "job.resource_management" }),
                Department.Create("dept.security", "Security",
                    new List<string> { "job.guard_post", "job.patrol",
                                       "job.contraband_inspection" }),
            };
        }

        // -- Entity management -----------------------------------------------

        public void AddNpc(NPCInstance npc)    => npcs[npc.uid]    = npc;
        public void AddShip(ShipInstance ship) => ships[ship.uid]  = ship;
        public void AddModule(ModuleInstance m)=> modules[m.uid]   = m;

        public NPCInstance  RemoveNpc(string uid)  { npcs.TryGetValue(uid, out var n); npcs.Remove(uid);   return n; }
        public ShipInstance RemoveShip(string uid) { ships.TryGetValue(uid, out var s); ships.Remove(uid); return s; }

        // -- Resource helpers ------------------------------------------------

        public float GetResource(string key) => resources.ContainsKey(key) ? resources[key] : 0f;

        public float ModifyResource(string key, float delta)
        {
            float current = GetResource(key);
            resources[key] = Mathf.Max(0f, current + delta);
            return resources[key];
        }

        // -- Tag helpers -----------------------------------------------------

        public void   SetTag(string tag)     => activeTags.Add(tag);
        public void   ClearTag(string tag)   => activeTags.Remove(tag);
        public bool   HasTag(string tag)     => activeTags.Contains(tag);

        // -- Faction rep helpers ---------------------------------------------

        public float GetFactionRep(string factionId)
            => factionReputation.ContainsKey(factionId) ? factionReputation[factionId] : 0f;

        public float ModifyFactionRep(string factionId, float delta)
        {
            float current = GetFactionRep(factionId);
            factionReputation[factionId] = Mathf.Clamp(current + delta, -100f, 100f);
            return factionReputation[factionId];
        }

        // -- Queries ----------------------------------------------------------

        public List<NPCInstance>  GetCrew()
        {
            var list = new List<NPCInstance>();
            foreach (var n in npcs.Values) if (n.IsCrew())    list.Add(n);
            return list;
        }

        public List<NPCInstance>  GetVisitors()
        {
            var list = new List<NPCInstance>();
            foreach (var n in npcs.Values) if (n.IsVisitor()) list.Add(n);
            return list;
        }

        public List<ShipInstance> GetDockedShips()
        {
            var list = new List<ShipInstance>();
            foreach (var s in ships.Values) if (s.status == "docked")    list.Add(s);
            return list;
        }

        public List<ShipInstance> GetIncomingShips()
        {
            var list = new List<ShipInstance>();
            foreach (var s in ships.Values) if (s.status == "incoming")  list.Add(s);
            return list;
        }

        public ModuleInstance GetAvailableDock()
        {
            foreach (var m in modules.Values)
                if (m.IsAvailableDock()) return m;
            return null;
        }

        // -- Logging ----------------------------------------------------------

        public void AddMessage(CommMessage msg)
        {
            messages.Insert(0, msg);
            if (messages.Count > 100) messages.RemoveRange(100, messages.Count - 100);
        }

        public int UnreadMessageCount()
        {
            int count = 0;
            foreach (var m in messages) if (!m.read) count++;
            return count;
        }

        public void LogEvent(string message)
        {
            log.Insert(0, $"[T{tick:D4}] {message}");
            if (log.Count > 200) log.RemoveRange(200, log.Count - 200);
        }
    }
}
