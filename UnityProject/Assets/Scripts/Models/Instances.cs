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

        // Response options: each entry has "label" and "action" keys, plus
        // payload fields (e.g. "iceQty", "icePrice") set by CommsSystem.
        public List<Dictionary<string, object>> responseOptions =
            new List<Dictionary<string, object>>();

        public static CommMessage Create(string subject, string body,
                                         string senderName, string senderType,
                                         string shipUid, int tick)
        {
            return new CommMessage
            {
                uid        = Guid.NewGuid().ToString("N")[..8],
                subject    = subject,
                body       = body,
                senderName = senderName,
                senderType = senderType,
                shipUid    = shipUid,
                tick       = tick
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
            { "hunger", 1f }, { "rest", 1f }, { "social", 0.5f }, { "safety", 1f }
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
                { "hunger", 1f }, { "rest", 1f }, { "social", 0.5f }, { "safety", 1f }
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
                { "hunger", 1f }, { "rest", 1f }, { "social", 0.5f }, { "safety", 2f }
            };
            float totalWeight  = 4.5f;
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

        // uid of the Engineer NPC currently assigned here, or null
        public string assignedNpcUid;

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
                                                 int maxHealth = 100, float quality = 1.0f)
        {
            return new FoundationInstance
            {
                uid         = Guid.NewGuid().ToString("N")[..8],
                buildableId = buildableId,
                tileCol     = col,
                tileRow     = row,
                maxHealth   = maxHealth,
                health      = maxHealth,
                quality     = quality,
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

        // Communications inbox (most recent first)
        public List<CommMessage> messages = new List<CommMessage>();

        // Work assignments per NPC: npcUid → list of job ids the NPC is allowed.
        // Empty list means all jobs are allowed for that NPC.
        public Dictionary<string, List<string>> workAssignments = new Dictionary<string, List<string>>();

        // Crew departments
        public List<Department> departments = new List<Department>();

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
