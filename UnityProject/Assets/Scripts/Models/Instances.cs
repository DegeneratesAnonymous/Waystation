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
    // Schedule Slot — per-hour activity state for per-NPC custom schedules
    // -------------------------------------------------------------------------

    /// <summary>
    /// What an NPC is allowed to do during a given hour of the day.
    /// Work   — normal job assignment applies.
    /// Rest   — NPC is assigned the rest job; no productive work.
    /// Recreation — NPC is assigned a recreational task; no productive work.
    /// </summary>
    public enum ScheduleSlot
    {
        Work,
        Rest,
        Recreation
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

        // Optional department colour used by the shader-driven NPC tinting system.
        // Null means no colour is configured; DeptColour sources on NPC clothing
        // will fall back to MaterialDefault (no tint) when this is null.
        // Stored as a nullable RGB hex string (e.g. "#4880aa") for JSON round-trip
        // compatibility; resolved to a Unity Color at render time via DepartmentRegistry.
        public string colourHex = null;

        // Optional accent (secondary) colour — nullable, same rules as colourHex.
        public string secondaryColourHex = null;

        public static Department Create(string uid, string name, List<string> allowedJobs = null)
        {
            return new Department
            {
                uid         = uid,
                name        = name,
                allowedJobs = allowedJobs ?? new List<string>()
            };
        }

        /// <summary>
        /// Returns the department primary colour as a nullable UnityEngine.Color.
        /// Returns null when <see cref="colourHex"/> is unset or invalid.
        /// </summary>
        public UnityEngine.Color? GetColour()
        {
            if (string.IsNullOrEmpty(colourHex)) return null;
            if (UnityEngine.ColorUtility.TryParseHtmlString(colourHex, out UnityEngine.Color c))
                return c;
            return null;
        }

        /// <summary>
        /// Returns the department secondary (accent) colour as a nullable UnityEngine.Color.
        /// Returns null when <see cref="secondaryColourHex"/> is unset or invalid.
        /// </summary>
        public UnityEngine.Color? GetSecondaryColour()
        {
            if (string.IsNullOrEmpty(secondaryColourHex)) return null;
            if (UnityEngine.ColorUtility.TryParseHtmlString(secondaryColourHex, out UnityEngine.Color c))
                return c;
            return null;
        }
    }


    // -------------------------------------------------------------------------
    // Mood Axis — the two independent mood dimensions tracked per NPC
    // -------------------------------------------------------------------------

    /// <summary>
    /// The two independent mood axes.  Each axis has its own modifier list,
    /// deduplication, and drift-toward-50 behaviour.
    /// </summary>
    public enum MoodAxis
    {
        /// <summary>Happy/Sad axis (0–100).  Drives crisis detection and station morale.</summary>
        HappySad,
        /// <summary>Calm/Stressed axis (0–100).  Independent of crisis; feeds into SanitySystem.</summary>
        CalmStressed
    }

    // -------------------------------------------------------------------------
    // Mood Modifier — a named, time-limited delta applied to an NPC's MoodScore
    // -------------------------------------------------------------------------

    [Serializable]
    public class MoodModifierRecord
    {
        // Human-readable event identifier (e.g. "harvest_success", "proximity_friend")
        public string eventId;
        // The mood delta applied while this modifier is active (+/- 0–100 scale)
        public float  delta;
        // Game tick at which this modifier expires; -1 = permanent
        public int    expiresAtTick;
        // Optional source identifier used for deduplication (same eventId+source = refresh)
        public string source;
    }

    // -------------------------------------------------------------------------
    // Relationship Type — the categorical type of a bond between two NPCs
    // -------------------------------------------------------------------------

    public enum RelationshipType
    {
        None,           // Strangers (AffinityScore near 0)
        Acquaintance,   // AffinityScore ≥ 5
        Friend,         // AffinityScore ≥ 20
        Enemy,          // AffinityScore ≤ -5
        Lover,          // AffinityScore ≥ 40
        Spouse          // AffinityScore ≥ 60, approved by player
    }

    // -------------------------------------------------------------------------
    // Relationship Record — the bond between an ordered pair of NPCs
    // -------------------------------------------------------------------------

    [Serializable]
    public class RelationshipRecord
    {
        // The two NPC uids that form this relationship (npcUid1 < npcUid2 lexically)
        public string npcUid1;
        public string npcUid2;
        // Continuous affinity score: positive = friendly, negative = hostile
        public float  affinityScore = 0f;
        // Categorical type derived from affinityScore (or elevated by player action)
        public RelationshipType relationshipType = RelationshipType.None;
        // Game tick of last social interaction — used for decay
        public int    lastInteractionTick = 0;
        // True once a marriage event has been approved (suppresses further prompts)
        public bool   married = false;
        // True when a pending marriage event has been sent to the player notification queue
        public bool   marriageEventPending = false;
        // Tick at which marriage event was last fired (for re-fire interval)
        public int    lastMarriageEventTick = -1;

        /// <summary>
        /// Returns a canonical string key for a pair, using lexicographic ordering
        /// so (A,B) and (B,A) always resolve to the same record.
        /// </summary>
        public static string MakeKey(string uid1, string uid2)
        {
            return string.Compare(uid1, uid2, StringComparison.Ordinal) <= 0
                ? $"{uid1}:{uid2}"
                : $"{uid2}:{uid1}";
        }

        /// <summary>
        /// Derives RelationshipType from the current affinityScore.
        /// Spouse is preserved if already set (it requires player approval to set).
        /// </summary>
        public void UpdateTypeFromAffinity()
        {
            if (relationshipType == RelationshipType.Spouse) return;
            if      (affinityScore >=  40f) relationshipType = RelationshipType.Lover;
            else if (affinityScore >=  20f) relationshipType = RelationshipType.Friend;
            else if (affinityScore >=   5f) relationshipType = RelationshipType.Acquaintance;
            else if (affinityScore <=  -5f) relationshipType = RelationshipType.Enemy;
            else                            relationshipType = RelationshipType.None;
        }
    }

    // -------------------------------------------------------------------------
    // SkillInstance — runtime per-NPC per-skill state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runtime XP and level state for one skill on one NPC.
    /// Serialised into StationData as skillId + currentXP; level is derived on load.
    /// </summary>
    [Serializable]
    public class SkillInstance
    {
        /// <summary>Matches SkillDefinition.skillId.</summary>
        public string skillId;

        /// <summary>Cumulative XP in this skill. Never decreases.</summary>
        public float currentXP = 0f;

        /// <summary>
        /// Derived skill level: floor(sqrt(currentXP / 100)).
        /// Range 0–20.
        /// </summary>
        public int Level => Mathf.Clamp(Mathf.FloorToInt(Mathf.Sqrt(currentXP / 100f)), 0, 20);

        // ── Daily soft cap (diminishing returns above 500 XP/day) ─────────────

        /// <summary>XP accumulated in the current in-game day for this skill.</summary>
        public float dailyXPAccumulated = 0f;

        /// <summary>In-game day index when dailyXPAccumulated was last reset.</summary>
        public int   dailyXPDay = -1;

        public static SkillInstance Create(string skillId) =>
            new SkillInstance { skillId = skillId };
    }

    // -------------------------------------------------------------------------
    // NPC Trait Runtime State
    // -------------------------------------------------------------------------

    /// <summary>Stage of player-NPC tension — progresses as player actions conflict with NPC traits.</summary>
    public enum TensionStage { Normal, Disgruntled, WorkSlowdown, DepartureRisk }

    /// <summary>Player action types used for tension calculation — append-only.</summary>
    public enum PlayerActionType
    {
        Micromanage,
        ResourceRestriction,
        ForcedOvertime,
        SocialInteraction,
        ResourceProvisioning,
        // Extend as needed — append-only
    }

    /// <summary>A single active trait on an NPC, with its current strength.</summary>
    [Serializable]
    public class ActiveTrait
    {
        /// <summary>References NpcTraitDefinition.traitId.</summary>
        public string traitId;

        /// <summary>Current strength: 0–1. Trait is removed when this reaches 0.</summary>
        public float  strength = 1f;

        /// <summary>Game tick when this trait was acquired.</summary>
        public int    acquisitionTick;
    }

    /// <summary>Per-NPC trait state: active traits plus sustained condition pressure.</summary>
    [Serializable]
    public class NpcTraitProfile
    {
        /// <summary>Active traits on this NPC.</summary>
        public List<ActiveTrait> traits = new List<ActiveTrait>();

        /// <summary>
        /// Accumulated condition pressure per category (dimensionless units per day).
        /// Pressure is reset after a trait fires from that category.
        /// </summary>
        public Dictionary<string, float> conditionPressure = new Dictionary<string, float>();

        /// <summary>Current tension score — accumulates from conflicting player actions.</summary>
        public float tensionScore = 0f;

        /// <summary>Current tension stage derived from tensionScore.</summary>
        public TensionStage tensionStage = TensionStage.Normal;

        // ── Lineage tracking ─────────────────────────────────────────────────
        /// <summary>
        /// Current position on each trait lineage axis.
        /// Key = lineageId; Value = axis position: -2=negative tier 2, -1=negative tier 1,
        /// 0=neutral, +1=positive tier 1, +2=positive tier 2.
        /// </summary>
        public Dictionary<string, int> lineagePositions = new Dictionary<string, int>();

        /// <summary>Per-lineage cooldown: key = lineageId, value = in-game tick when cooldown expires.</summary>
        public Dictionary<string, int> lineageCooldownEndTick = new Dictionary<string, int>();
    }

    // -------------------------------------------------------------------------
    // Faction Government Runtime State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cached result of a faction trait aggregation calculation.
    /// Produced by FactionGovernmentSystem.FactionAggregator.
    /// </summary>
    [Serializable]
    public class FactionTraitAggregate
    {
        /// <summary>Averaged trait magnitudes per TraitCategory name.</summary>
        public Dictionary<string, float> categoryScores  = new Dictionary<string, float>();

        /// <summary>Summed effect magnitudes across the relevant NPC pool, keyed by TraitEffectTarget name.</summary>
        public Dictionary<string, float> aggregateEffects = new Dictionary<string, float>();

        /// <summary>Government type used during this calculation (for cache invalidation).</summary>
        public GovernmentType sourceGovernmentType;

        /// <summary>Game tick when this aggregate was last calculated.</summary>
        public int calculatedAtTick = -1;
    }

    // -------------------------------------------------------------------------
    // Region & Resource History Runtime State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rolling 30-day resource history for a region.
    /// Used to bias NPC trait generation at spawn time.
    /// </summary>
    [Serializable]
    public class RegionResourceHistory
    {
        private const int WindowDays = 30;

        /// <summary>
        /// Daily resource amounts per ResourceType name.
        /// Oldest entries are dropped when the window exceeds 30 days.
        /// </summary>
        public Dictionary<string, List<float>> dailyAmounts =
            new Dictionary<string, List<float>>();

        /// <summary>Baseline amount used to compute scarcity scores (per resource type).</summary>
        public Dictionary<string, float> baselines =
            new Dictionary<string, float>();

        /// <summary>Records a new daily amount for the given resource type, pruning old entries.</summary>
        public void RecordDailyAmount(ResourceType resource, float amount)
        {
            string key = resource.ToString();
            if (!dailyAmounts.ContainsKey(key))
                dailyAmounts[key] = new List<float>();
            dailyAmounts[key].Add(amount);
            while (dailyAmounts[key].Count > WindowDays)
                dailyAmounts[key].RemoveAt(0);
        }

        /// <summary>Returns the rolling average for the resource, or 0 if no data.</summary>
        public float GetAverageAmount(ResourceType resource)
        {
            string key = resource.ToString();
            if (!dailyAmounts.TryGetValue(key, out var list) || list.Count == 0) return 0f;
            float sum = 0f;
            foreach (var v in list) sum += v;
            return sum / list.Count;
        }

        /// <summary>
        /// Normalised scarcity score 0–1: 0 = abundant, 1 = critically scarce.
        /// Computed relative to the configured baseline for that resource.
        /// </summary>
        public float GetScarcityScore(ResourceType resource)
        {
            string key = resource.ToString();
            float baseline = baselines.TryGetValue(key, out var b) ? b : 100f;
            if (baseline <= 0f) return 0f;
            float avg = GetAverageAmount(resource);
            return Mathf.Clamp01(1f - avg / baseline);
        }

        /// <summary>Composite pressure across all tracked resources.</summary>
        public float GetOverallResourcePressure()
        {
            float total = 0f;
            int   count = 0;
            foreach (ResourceType rt in System.Enum.GetValues(typeof(ResourceType)))
            {
                string key = rt.ToString();
                if (!dailyAmounts.ContainsKey(key) || dailyAmounts[key].Count == 0) continue;
                total += GetScarcityScore(rt);
                count++;
            }
            return count > 0 ? total / count : 0f;
        }
    }

    /// <summary>
    /// Represents a single region stub for the Horizon Simulation interface.
    /// All simulation fields beyond resource history are stubbed with TODO markers
    /// for the Horizon Simulation work order.
    /// </summary>
    [Serializable]
    public class RegionData
    {
        public string                regionId;
        public string                displayName;
        public RegionResourceHistory resourceHistory     = new RegionResourceHistory();
        public List<string>          factionIds          = new List<string>();
        public RegionSimulationState simulationState     = RegionSimulationState.Undiscovered;

        // TODO: Add Horizon Simulation fields (population density, conflict level, etc.)
        //       when the Horizon Simulation work order is implemented.
    }

    // -------------------------------------------------------------------------
    // Horizon Simulation — HistoricalEvent stub
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stub data class for recording faction-level historical events.
    /// Used by IFactionHistoryProvider.
    /// </summary>
    [Serializable]
    public class HistoricalEvent
    {
        public string   eventId;
        public string   description;
        public int      gameTick;
        public string[] involvedFactionIds;
    }

    // -------------------------------------------------------------------------
    // Ability Scores
    // -------------------------------------------------------------------------

    /// <summary>The six core ability scores governing NPC capability and skill checks.</summary>
    [Serializable]
    public class AbilityScores
    {
        public int STR = 8;   // Strength:  Melee, Hauling, Construction
        public int DEX = 8;   // Dexterity: Aiming, Shooting, Surgery, Piloting
        public int INT = 8;   // Intellect: Research, Crafting, Electronics
        public int WIS = 8;   // Wisdom:    Medical, Plants, Awareness; governs trait resistance
        public int CHA = 8;   // Charisma:  Bartering, Leading, Negotiating
        public int END = 8;   // Endurance: Fortitude, Discipline; vitals decay rate

        /// <summary>Modifier for a given score: 1-4→-2, 5-7→-1, 8-11→0, 12-14→+1, 15-17→+2, 18-20→+3.</summary>
        public static int GetModifier(int score)
        {
            if (score <= 4)  return -2;
            if (score <= 7)  return -1;
            if (score <= 11) return  0;
            if (score <= 14) return  1;
            if (score <= 17) return  2;
            return 3;
        }

        public int STRMod => GetModifier(STR);
        public int DEXMod => GetModifier(DEX);
        public int INTMod => GetModifier(INT);
        public int WISMod => GetModifier(WIS);
        public int CHAMod => GetModifier(CHA);
        public int ENDMod => GetModifier(END);
    }

    // -------------------------------------------------------------------------
    // Life Stage
    // -------------------------------------------------------------------------

    public enum LifeStage { Baby, Child, Adult }

    // -------------------------------------------------------------------------
    // Need Profiles
    // -------------------------------------------------------------------------

    [Serializable]
    public class SleepNeedProfile
    {
        public float value          = 100f;  // 0-100
        public bool  isSeeking      = false;
        public string assignedBedId = null;
        // Ticks of consecutive rest above 90% used to determine well-rested bonus eligibility
        public int   wellRestedTicks = 0;
    }

    [Serializable]
    public class HungerNeedProfile
    {
        public float value                  = 100f;  // 0-100
        public bool  isSeeking              = false;
        // Cumulative ticks spent below 10% — triggers malnourishment
        public int   nourishmentDebtTicks   = 0;
        // Ticks spent above 60% while malnourished — clears malnourishment after 3 in-game days
        public int   nourishmentRecoveryTicks = 0;
        public bool  isMalnourished         = false;
        // Days spent at 0% hunger — starvation timeline
        public int   starvationDayCount     = 0;
    }

    [Serializable]
    public class ThirstNeedProfile
    {
        public float value              = 100f;  // 0-100
        public bool  isSeeking          = false;
        // Days spent at 0% thirst — dehydration timeline
        public int   dehydrationDayCount = 0;
    }

    [Serializable]
    public class RecreationNeedProfile
    {
        public float value      = 100f;  // 0-100
        public bool  isBurntOut = false;
    }

    [Serializable]
    public class SocialNeedProfile
    {
        public float value              = 50f;   // 0-100 (starts mid-range)
        public bool  isReclusive        = false; // if true need is Solitude (inverted)
        public int   lastInteractionTick = -1;
    }

    [Serializable]
    public class HygieneNeedProfile
    {
        public float value      = 100f;  // 0-100
        public bool  isSeeking  = false;
        // True when Hygiene is currently at or below the crisis threshold used for mood and social penalties
        public bool  inCrisis   = false;
    }

    // -------------------------------------------------------------------------
    // Sanity Profile
    // -------------------------------------------------------------------------

    [Serializable]
    public class SanityProfile
    {
        /// <summary>Signed sanity score. Floor -10, ceiling = WIS modifier.</summary>
        public int   score                  = 0;
        /// <summary>Derived from WIS modifier at profile creation. Recalculated if WIS changes.</summary>
        public int   ceiling                = 0;
        /// <summary>Running sum of moodScore values in the current 24-tick day cycle.</summary>
        public float dailyMoodAccumulator   = 0f;
        /// <summary>Number of mood samples accumulated this day cycle.</summary>
        public int   dailyMoodSampleCount   = 0;
        /// <summary>Set to true if any need reached 0% this day cycle.</summary>
        public bool  needDepletedThisCycle  = false;
        /// <summary>Count of needs above 50% for the entire day cycle (max 3).</summary>
        public int   needsAbove50Count      = 0;
        public bool  isInBreakdown          = false;
        public bool  requiresIntervention   = false;
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

        // Per-NPC 24-slot schedule (index = hour 0–23).
        // null means "use default day/night split" (hours 6–17 = Work, rest = Rest).
        // Players may override individual slots via the schedule editor.
        public ScheduleSlot[] npcSchedule = null;

        /// <summary>
        /// Returns the schedule slot for the given hour (0–23), using the per-NPC
        /// custom schedule when set, or the station default day/night split otherwise.
        /// <paramref name="hourOfDay"/> is clamped to 0–23 to guard against
        /// out-of-range values from TimeSystem.HourOfDay.
        /// </summary>
        public ScheduleSlot GetScheduleSlot(int hourOfDay)
        {
            hourOfDay = Math.Max(0, Math.Min(23, hourOfDay));
            if (npcSchedule != null && npcSchedule.Length == 24)
                return npcSchedule[hourOfDay];
            // Default: Work during day hours (06:00–17:59), Rest otherwise
            return (hourOfDay >= 6 && hourOfDay < 18) ? ScheduleSlot.Work : ScheduleSlot.Rest;
        }

        /// <summary>
        /// Initialises the per-NPC schedule array from the station default day/night
        /// split.  Hours 6–17 become Work, all others become Rest.
        /// Call this once to create a schedule the player can then customise.
        /// </summary>
        public void InitDefaultSchedule()
        {
            npcSchedule = new ScheduleSlot[24];
            for (int h = 0; h < 24; h++)
                npcSchedule[h] = (h >= 6 && h < 18) ? ScheduleSlot.Work : ScheduleSlot.Rest;
        }

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

        // Social skill used for hailing ships (1–10, default 1).
        // Migrated to SkillInstance for skill.social on first load.
        public int socialSkill = 1;

        // ── Skill & Expertise System ──────────────────────────────────────────
        // Serialised: list of SkillInstance entries (one per defined skill).
        // Missing entries default to currentXP = 0 (level 0).
        public List<SkillInstance> skillInstances = new List<SkillInstance>();

        // IDs of chosen expertise (ExpertiseDefinition.expertiseId).
        // An NPC can hold up to ExpertiseSlotCount entries simultaneously.
        public List<string> chosenExpertise = new List<string>();

        // Multiplicative modifier applied on top of workModifier from expertise passive bonuses.
        // Evaluated by SkillSystem.RebuildExpertiseModifier(); default 1.0.
        public float expertiseModifier = 1.0f;

        // Skills whose level-up just crossed a multiple of 4, pending an expertise slot choice.
        // Each entry represents one unresolved expertise prompt for that skill.
        // Cleared as the player selects expertise from the prompted panel.
        public List<string> pendingExpertiseSkillIds = new List<string>();

        // Pathfinding state — managed by AntennaSystem/ShipVisitStateMachine
        // Tile position target when actively walking
        public int  pathTargetCol   = -1;
        public int  pathTargetRow   = -1;
        public bool isPathing       = false;
        // ID of the current NPCTask in progress (null = idle)
        public string currentTaskId = null;
        // ── Mood & Relationships (MoodSystem) ─────────────────────────────────
        // MoodScore (happy/sad axis): 0–100. 50 is baseline. Drifts toward 50 over waking hours.
        // Separate from the needs-based `mood` float which drives the existing label.
        public float moodScore             = 50f;
        // StressScore (calm/stressed axis): 0–100. 50 is baseline. 100 = very calm, 0 = very stressed.
        // Independent of moodScore; does not trigger crisis state. Feeds into SanitySystem.
        public float stressScore           = 50f;
        // Multiplier applied to job duration (higher mood = faster work).
        // 1.05 = Thriving, 1.0 = Content, 0.95 = Struggling. Set by MoodSystem.
        public float workModifier          = 1.0f;
        // True when MoodScore has dropped below the crisis threshold (< 20).
        // While in crisis the NPC abandons work and takes recreational tasks.
        public bool  inCrisis              = false;
        // Active timed mood modifiers for the happy/sad axis (named deltas with expiry ticks)
        public List<MoodModifierRecord> moodModifiers = new List<MoodModifierRecord>();
        // Active timed mood modifiers for the calm/stressed axis
        public List<MoodModifierRecord> stressModifiers = new List<MoodModifierRecord>();
        // Game tick of the last conversation this NPC completed (60-tick cooldown)
        public int   lastConversationTick  = -99;

        // ── Ability Scores ────────────────────────────────────────────────────
        public AbilityScores abilityScores     = new AbilityScores();
        /// <summary>Unspent ability score points from level milestones (every 4 levels = +2 points).</summary>
        public int           abilityScorePoints = 0;

        // ── Life Stage ────────────────────────────────────────────────────────
        public LifeStage lifeStage  = LifeStage.Adult;
        public int       ageDays    = 0;
        public string    motherId   = null;
        public string    fatherId   = null;
        public List<string> siblingIds = new List<string>();

        // ── Trait Slots ───────────────────────────────────────────────────────
        /// <summary>Maximum number of general traits this NPC can hold (grows with life stage + level).</summary>
        public int traitSlots = 3;

        // ── Needs (new structured profiles) ──────────────────────────────────
        public SleepNeedProfile      sleepNeed      = new SleepNeedProfile();
        public HungerNeedProfile     hungerNeed     = new HungerNeedProfile();
        public ThirstNeedProfile     thirstNeed     = new ThirstNeedProfile();
        public RecreationNeedProfile recreationNeed = new RecreationNeedProfile();
        public SocialNeedProfile     socialNeed     = new SocialNeedProfile();
        public HygieneNeedProfile    hygieneNeed    = new HygieneNeedProfile();

        // ── Species depletion rate modifiers ──────────────────────────────────
        // Keyed by need name (e.g. "sleep", "hygiene"), value is a multiplier.
        // Populated at spawn from NPCTemplate.needDepletionRates.
        // Null or empty = all multipliers are 1.0 (no species modifier).
        public Dictionary<string, float> needDepletionRates = null;

        // ── Sanity ────────────────────────────────────────────────────────────
        public SanityProfile sanity = null;

        public SanityProfile GetOrCreateSanity()
        {
            if (sanity == null)
            {
                int wisMod = AbilityScores.GetModifier(abilityScores.WIS);
                sanity = new SanityProfile { score = wisMod, ceiling = wisMod };
            }
            return sanity;
        }

        // ── Trait Profile (NPC Traits system) ────────────────────────────────
        // Nullable — existing NPCs without a profile are treated as having no traits.
        // Initialised lazily by TraitSystem on first interaction.
        public NpcTraitProfile traitProfile = null;

        // Multiplicative work speed modifier applied by active traits (default 1.0).
        // Stacks multiplicatively with workModifier (MoodSystem) and expertiseModifier (SkillSystem).
        // Evaluated by TraitSystem.ApplyTraitEffects() after any trait change.
        public float traitWorkModifier = 1.0f;

        // Multiplicative work speed modifier applied by tension stage (default 1.0).
        // Set to WorkSlowdownModifier at WorkSlowdown/DepartureRisk; reset to 1.0 at Normal/Disgruntled.
        // Stacks multiplicatively with traitWorkModifier and workModifier.
        public float tensionWorkModifier = 1.0f;

        // ── Personal Inventory ────────────────────────────────────────────────
        /// <summary>Items worn / held in named equipment slots (slot name → itemId).
        /// Example slots: "weapon", "armour", "tool", "accessory".</summary>
        public Dictionary<string, string> equippedSlots = new Dictionary<string, string>();

        /// <summary>Items carried in pockets / bags (itemId → quantity).</summary>
        public Dictionary<string, int> pocketItems = new Dictionary<string, int>();

        // ── Medical Profile ───────────────────────────────────────────────────
        /// <summary>
        /// Body-part-based medical state for this NPC.
        /// Null until MedicalTickSystem.EnsureProfile() is called.
        /// Null-safe: all medical feature code checks FeatureFlags.MedicalSystem
        /// and/or null-checks this field before touching it.
        /// </summary>
        public MedicalProfile medicalProfile = null;

        /// <summary>Returns the trait profile, initialising it lazily if needed.</summary>
        public NpcTraitProfile GetOrCreateTraitProfile()
        {
            if (traitProfile == null) traitProfile = new NpcTraitProfile();
            return traitProfile;
        }

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

        /// <summary>Syncs the legacy -1..1 mood float from the 0-100 moodScore.</summary>
        public void RecalculateMood()
        {
            mood = Mathf.Clamp((moodScore / 100f) * 2f - 1f, -1f, 1f);
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

    /// <summary>
    /// State machine for the full ship visit lifecycle.
    /// OutOfRange → InRange → Passing | Inbound → Docked → Departing → OutOfRange
    /// </summary>
    public enum ShipVisitState
    {
        OutOfRange,   // beyond antenna detection radius
        InRange,      // detected — not yet committed
        Passing,      // drifting through, no reason to stop
        Inbound,      // committed to docking; shuttle in transit
        Docked,       // shuttle landed, visitors aboard
        Departing,    // visit timer expired; shuttle returning to ship
    }

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

        // Docking state (legacy — kept for backward compatibility)
        public string status    = "incoming";  // incoming / docked / departing / hostile / destroyed
        public string dockedAt;
        public int    ticksDocked = 0;
        // Set when the ship docks; used by VisitorSystem to avoid re-rolling departure each tick.
        public int    plannedDepartureTick = -1;

        // ── Visitor-system state ──────────────────────────────────────────
        // Full visit lifecycle state (replaces/mirrors the legacy string status)
        public ShipVisitState visitState = ShipVisitState.OutOfRange;

        // World position outside station (simulated — no Unity Transform)
        public float worldX = 0f;
        public float worldY = 0f;
        // Drift target position while in range
        public float driftTargetX = 0f;
        public float driftTargetY = 0f;

        // Tick at which the ship entered antenna range
        public int   inRangeSinceTick = -1;

        // Shuttle currently dispatched from this ship (uid of ShuttleInstance)
        public string shuttleUid = null;

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

        /// <summary>Human-readable display status for the Communications Menu.</summary>
        public string VisitStateLabel()
        {
            switch (visitState)
            {
                case ShipVisitState.OutOfRange:  return "Out of Range";
                case ShipVisitState.InRange:     return "In Range";
                case ShipVisitState.Passing:     return "Passing";
                case ShipVisitState.Inbound:     return "Inbound";
                case ShipVisitState.Docked:      return "Docked";
                case ShipVisitState.Departing:   return "Departing";
                default:                         return "Unknown";
            }
        }
    }

    // -------------------------------------------------------------------------
    // Shuttle Instance — spawned by a ShipInstance when transitioning to Inbound
    // -------------------------------------------------------------------------

    [Serializable]
    public class ShuttleInstance
    {
        public string uid;
        public string shipUid;          // parent ship
        public string landingPadUid;    // target ShuttleLandingPadInstance uid
        public List<string> visitorNpcUids = new List<string>();

        // Visitor count captured at departure time (before visitorNpcUids is cleared)
        public int peakVisitorCount = 0;

        // Animation state: "inbound" | "docked" | "departing"
        public string state = "inbound";

        // Simulated world position (mirrors parent ship at spawn, then moves toward pad)
        public float worldX = 0f;
        public float worldY = 0f;

        // Target tile coordinates (landing pad tile)
        public int targetCol = 0;
        public int targetRow = 0;

        public static ShuttleInstance Create(string shipUid, string landingPadUid,
                                             int targetCol, int targetRow,
                                             float startX, float startY)
        {
            return new ShuttleInstance
            {
                uid          = Guid.NewGuid().ToString("N").Substring(0, 8),
                shipUid      = shipUid,
                landingPadUid = landingPadUid,
                targetCol    = targetCol,
                targetRow    = targetRow,
                worldX       = startX,
                worldY       = startY,
            };
        }
    }

    // -------------------------------------------------------------------------
    // Shuttle Landing Pad — runtime state for a landing pad foundation
    // -------------------------------------------------------------------------

    [Serializable]
    public class ShuttleLandingPadState
    {
        public string foundationUid;  // uid of the FoundationInstance for this pad
        public string occupiedByShuttleUid = null;  // null = vacant; set at dispatch time to reserve
        // UIDs of ships waiting for this pad to free up
        public List<string> waitingShipUids = new List<string>();

        public bool IsOccupied => occupiedByShuttleUid != null;
    }

    // -------------------------------------------------------------------------
    // Hail Cooldown — tracks when a player may re-attempt hailing a ship
    // -------------------------------------------------------------------------

    [Serializable]
    public class HailCooldownRecord
    {
        public string shipUid;
        public int    cooldownUntilTick;  // player may re-hail at/after this tick
    }

    // -------------------------------------------------------------------------
    // Ship Visit Record — appended to visitHistory for analytics
    // -------------------------------------------------------------------------

    [Serializable]
    public class ShipVisitRecord
    {
        public string shipUid;
        public string shipName;
        public string shipRole;
        public int    arrivedTick;
        public int    departedTick;
        public int    visitorCount;
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

        /// <summary>
        /// Resources currently depriving this module.  Non-empty → module is in a resource-
        /// deprived degraded state (all effects suspended) independently of other modules.
        /// Populated and cleared by ResourceSystem during cascade evaluation.
        /// </summary>
        public HashSet<string> resourceDeprived = new HashSet<string>();

        /// <summary>True when at least one required resource has been cut off.</summary>
        public bool IsResourceDeprived => resourceDeprived.Count > 0;

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

        // ── Repair pipeline ──────────────────────────────────────────────────
        // Set by BuildingSystem when health reaches 0 on a complete foundation.
        // Cleared when repair finishes and health is restored to maxHealth.
        public bool                      pendingRepair            = false;
        // uid of the Engineer NPC assigned to this repair task, or null.
        public string                    repairAssignedNpcUid     = null;
        // Materials already moved to the repair site: item_id → qty.
        public Dictionary<string, int>   repairHauledMaterials    = new Dictionary<string, int>();
        // 0.0 → 1.0; reaches 1.0 when repair work is done.
        public float                     repairProgress           = 0f;

        // Cargo storage — mirrors ModuleInstance capability for placed storage objects.
        // cargoCapacity > 0 means this foundation acts as a cargo hold.
        public int                       cargoCapacity = 0;
        public CargoHoldSettings         cargoSettings;
        public Dictionary<string, int>   cargo         = new Dictionary<string, int>();

        // Commitment Cooldown — runtime-only haul lock applied when items are placed.
        // itemId → game tick at which the cooldown expires (NPC haul tasks skip items
        // whose entry here has not yet expired). Not serialised to saves; cleared on load.
        public Dictionary<string, int>   commitmentCooldowns = new Dictionary<string, int>();

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
        // Fuel Lines: true when fuel network has enough stored fuel for this consumer
        public bool  isFuelSupplied   = false;

        // Storage nodes: persisted amounts (serialised in StationData)
        public float storedEnergy     = 0f;  // Battery — watt-hours currently stored
        public float storedFluid      = 0f;  // Water Tank / Fluid Tank — litres stored
        public float storedGas        = 0f;  // Gas Tank — litres-equivalent stored
        public float storedFuel       = 0f;  // Fuel Tank — litres stored

        // Isolator state: true = open (allows connectivity), false = closed (splits network)
        public bool  isolatorOpen     = true;

        // ── Farming / climate fields ─────────────────────────────────────────
        // ── Farming / climate fields ─────────────────────────────────────────
        // Hydroponics Planter Tile state (only used when buildableId == "buildable.hydroponics_planter")
        public string cropId          = null;  // assigned CropDataDefinition.id; null = unassigned
        public int    growthStage     = 0;     // 0=empty, 1=seedling, 2=established, 3=mature
        public float  growthProgress  = 0f;   // 0–1 within current stage
        public float  cropDamage      = 0f;   // 0–1; plant destroyed at 1.0

        // Runtime sensor values updated each tick by FarmingSystem/TemperatureSystem
        public float  lightLevel      = 0f;   // set by GrowLight above (0 = dark)
        public float  tileTemperature = 20f;  // °C from TemperatureSystem
        public bool   isWatered       = false; // set by FarmingSystem from pipe adjacency
        // Heater / Cooler target temperature (player-configurable via inspect menu)
        public float  targetTemperature = 20f;

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
        public string       roomKey;               // "minCol_minRow" canonical key
        public string       workbenchRoomType;     // player-assigned room type id
        public string       displayName;           // human-readable name from RoomTypeDefinition
        public bool         bonusActive;           // true when all requirements met
        public int          workbenchCount;        // number of workbenches of the assigned type
        public string       autoSuggestedRoomType; // dominant workbench type (non-binding hint)
        public List<string> workbenchUids = new List<string>();
        public List<RoomRequirementProgress> requirements = new List<RoomRequirementProgress>();
    }

    // -------------------------------------------------------------------------
    // Body Instance — a corpse left on the station map after an NPC dies.
    // Created by DeathHandlingSystem when an NPC's death tick fires.
    // Status lifecycle: spawned → haul_pending → hauling → removed
    // -------------------------------------------------------------------------

    [Serializable]
    public class BodyInstance
    {
        public string uid;
        public string npcUid;       // source NPC uid (for reference)
        public string npcName;      // cached display name (NPC is "dead" by this point)

        // Tile-grid position where the NPC died
        public int tileCol;
        public int tileRow;

        // Module location at time of death (used for proximity checks)
        public string location;

        // Game tick when the body was spawned
        public int spawnedAtTick;

        // Haul task state
        public bool   haulTaskGenerated = false;   // true once a haul task has been issued
        public bool   haulBlocked       = false;   // true when no disposal tile is designated
        public string haulerNpcUid      = null;    // UID of the NPC currently hauling this body
        public int    haulJobTimer      = 0;       // countdown ticks remaining on the haul task

        // Escalation step — tracks the current penalty tier to avoid redundant remove+push
        public int escalationStep = 0;

        public static BodyInstance Create(NPCInstance npc, int tick,
                                          int? deathTileCol = null, int? deathTileRow = null)
        {
            // Prefer explicitly provided death-tile coordinates from the calling system.
            // Falls back to pathTargetCol/Row (the NPC's last movement destination), then (0,0).
            int resolvedCol = deathTileCol ?? (npc.pathTargetCol >= 0 ? npc.pathTargetCol : 0);
            int resolvedRow = deathTileRow ?? (npc.pathTargetRow >= 0 ? npc.pathTargetRow : 0);

            return new BodyInstance
            {
                uid           = Guid.NewGuid().ToString("N").Substring(0, 8),
                npcUid        = npc.uid,
                npcName       = npc.name,
                tileCol       = resolvedCol,
                tileRow       = resolvedRow,
                location      = npc.location,
                spawnedAtTick = tick,
            };
        }
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
    // ResearchBranchState / ResearchState
    // -------------------------------------------------------------------------

    [Serializable]
    public class ResearchBranchState
    {
        public float          points          = 0f;
        public HashSet<string> unlockedNodeIds = new HashSet<string>();

        public static ResearchBranchState Create() => new ResearchBranchState();
    }

    [Serializable]
    public class ResearchState
    {
        public Dictionary<ResearchBranch, ResearchBranchState> branches =
            new Dictionary<ResearchBranch, ResearchBranchState>();

        // Datachips produced by completed research but not yet housed in a
        // Data Storage Server (no capacity available at the time of unlock).
        // Each represents one homeless chip awaiting storage.
        public int pendingDatachips = 0;

        public float TotalPoints(ResearchBranch branch)
            => branches.TryGetValue(branch, out var s) ? s.points : 0f;

        public bool IsUnlocked(string nodeId)
        {
            foreach (var b in branches.Values)
                if (b.unlockedNodeIds.Contains(nodeId)) return true;
            return false;
        }

        public static ResearchState Create()
        {
            var rs = new ResearchState();
            foreach (ResearchBranch b in Enum.GetValues(typeof(ResearchBranch)))
                rs.branches[b] = ResearchBranchState.Create();
            return rs;
        }
    }

    // -------------------------------------------------------------------------
    // PointOfInterest
    // -------------------------------------------------------------------------

    [Serializable]
    public class PointOfInterest
    {
        public string uid;
        public string poiType;      // "Asteroid" | "TradePost" | "AbandonedStation" | "NebulaPocket"
        public string displayName;
        public float  posX;
        public float  posY;
        public bool   discovered;
        public bool   visited;
        public Dictionary<string, int> resourceYield = new Dictionary<string, int>();
        public int    seed;

        public static PointOfInterest Create(string type, string name,
                                             float x, float y, int seed)
        {
            return new PointOfInterest
            {
                uid         = Guid.NewGuid().ToString("N")[..8],
                poiType     = type,
                displayName = name,
                posX        = x,
                posY        = y,
                seed        = seed,
            };
        }
    }

    // -------------------------------------------------------------------------
    // AsteroidMapState
    // -------------------------------------------------------------------------

    // Tile values stored in AsteroidMapState.tiles (byte[]).
    public enum AsteroidTile : byte { Empty = 0, Rock = 1, Ore = 2, Ice = 3, Wall = 4 }

    [Serializable]
    public class AsteroidMapState
    {
        public string uid;
        public string poiUid;
        public int    width;
        public int    height;
        public int    seed;
        public byte[] tiles;     // width × height, row-major
        public Dictionary<string, int> extractedResources = new Dictionary<string, int>();
        public List<string>            assignedNpcUids    = new List<string>();
        public string                  missionUid;
        public string                  status    = "active";  // "active" | "complete"
        public int                     startTick;
        public int                     endTick;

        public byte GetTile(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return (byte)AsteroidTile.Wall;
            return tiles[y * width + x];
        }

        public void SetTile(int x, int y, byte val)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            tiles[y * width + x] = val;
        }

        public static AsteroidMapState Create(string poiUid, string missionUid,
                                              int seed, int width, int height,
                                              int startTick, int durationTicks)
        {
            return new AsteroidMapState
            {
                uid         = Guid.NewGuid().ToString("N")[..8],
                poiUid      = poiUid,
                missionUid  = missionUid,
                seed        = seed,
                width       = width,
                height      = height,
                tiles       = new byte[width * height],
                status      = "active",
                startTick   = startTick,
                endTick     = startTick + durationTicks,
            };
        }
    }

    // -------------------------------------------------------------------------
    // FarmingTaskInstance — a pending or active farming task for an NPC.
    // Types: "sow" | "harvest" | "tend"
    // Status lifecycle: "pending" → "in_progress" → "complete"
    // -------------------------------------------------------------------------

    // FarmingTaskInstance — a pending or active farming task for an NPC.
    // Types: "sow" | "harvest" | "tend"
    // Status lifecycle: "pending" → "in_progress" → "complete"
    // -------------------------------------------------------------------------

    [Serializable]
    public class FarmingTaskInstance
    {
        public string uid;
        public string taskType;         // "sow" | "harvest" | "tend"
        public string planterUid;       // target FoundationInstance uid
        public string cropId;           // crop to sow (sow tasks only; null for harvest/tend)
        public string assignedNpcUid;   // null = unclaimed
        public string status;           // "pending" | "in_progress" | "complete"
        public int    progressTicks;    // ticks remaining until task completes (counts down)

        public static FarmingTaskInstance Create(string type, string planterUid,
                                                  string cropId = null, int ticks = 30)
        {
            return new FarmingTaskInstance
            {
                uid          = Guid.NewGuid().ToString("N")[..8],
                taskType     = type,
                planterUid   = planterUid,
                cropId       = cropId,
                status       = "pending",
                progressTicks = ticks,
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
            { "oxygen",  100f }, { "parts",  50f }, { "ice", 200f },
            { "fuel",     50f }
        };

        // Entity registries (keyed by uid)
        public Dictionary<string, NPCInstance>    npcs    = new Dictionary<string, NPCInstance>();
        public Dictionary<string, ShipInstance>   ships   = new Dictionary<string, ShipInstance>();
        public Dictionary<string, ModuleInstance> modules = new Dictionary<string, ModuleInstance>();

        // Faction reputation: factionId -> -100..100
        public Dictionary<string, float>  factionReputation = new Dictionary<string, float>();

        // Active state tags on the station
        public HashSet<string>            activeTags  = new HashSet<string>();

        /// <summary>
        /// Player actions blocked by resource depletion (e.g. "hire", "purchase" when credits = 0).
        /// Populated and cleared by ResourceSystem; UI and action handlers check this set.
        /// </summary>
        public HashSet<string>            playerActionRestrictions = new HashSet<string>();

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

        // Player-assigned room type per room (key = canonical "col_row" room key, value = RoomTypeDefinition.id).
        // Only rooms with an entry here participate in the bonus system.
        // Serialised with save data.
        public Dictionary<string, string> playerRoomTypeAssignments = new Dictionary<string, string>();

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

        // NPC relationship records: keyed by RelationshipRecord.MakeKey(uid1, uid2)
        public Dictionary<string, RelationshipRecord> relationships = new Dictionary<string, RelationshipRecord>();

        // Pending marriage event notifications waiting for player acknowledgement.
        // Each entry is a pair key (RelationshipRecord.MakeKey) of two NPCs.
        public List<string> pendingMarriageEvents = new List<string>();

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

        // Research progression
        public ResearchState research = null;

        // Solar system — procedurally generated once per new game, stable across saves.
        // Populated by SolarSystemGenerator.Generate() in GameManager.NewGame().
        public SolarSystemState solarSystem = null;

        // Map — points of interest
        public Dictionary<string, PointOfInterest> pointsOfInterest = new Dictionary<string, PointOfInterest>();

        // Asteroid away-mission maps
        public Dictionary<string, AsteroidMapState> asteroidMaps = new Dictionary<string, AsteroidMapState>();

        // Log of recent events / messages (most recent first)
        public List<string>               log            = new List<string>();

        // ── Farming system ───────────────────────────────────────────────────
        // Pending and in-progress farming tasks for NPC workers.
        public List<FarmingTaskInstance> farmingTasks = new List<FarmingTaskInstance>();

        // ── Death handling system ────────────────────────────────────────────
        // Bodies on the station map, keyed by BodyInstance.uid.
        public Dictionary<string, BodyInstance> bodies = new Dictionary<string, BodyInstance>();

        // Designated disposal tile for body hauling.
        // Defaults to (0,0) so haul tasks can proceed before the player explicitly
        // designates a site.  Set via the player designation action when a specific
        // tile is chosen.
        public bool disposalTileDesignated = true;
        public int  disposalTileCol        = 0;
        public int  disposalTileRow        = 0;

        // Per-room temperature (°C), keyed by canonical "minCol_minRow" room key.
        // Populated and updated by TemperatureSystem. Default = 20°C when absent.
        public Dictionary<string, float> roomTemperatures = new Dictionary<string, float>();

        // Per-tile temperature (°C), keyed by "col_row". Used for tiles outside sealed rooms.
        public Dictionary<string, float> tileTemperatures = new Dictionary<string, float>();

        // Reverse mapping: tile "col_row" → canonical room key. Populated by RoomSystem.
        // Runtime only — not serialised into saves.
        public Dictionary<string, string> tileToRoomKey = new Dictionary<string, string>();

        // ── Galaxy / Sector state ───────────────────────────────────────────

        // Seed used to generate the galaxy layout. Stored here so it can be serialised
        // with the save and passed back to GalaxyGenerator if the sector list is absent
        // (forward-compatibility for saves created before sector generation shipped).
        public int galaxySeed = 0;

        // All sectors in the galaxy, keyed by SectorData.uid.
        // Populated by GalaxyGenerator.Generate() in GameManager.NewGame().
        // On load: populated directly from SaveData; GalaxyGenerator is NOT re-run.
        public Dictionary<string, SectorData> sectors = new Dictionary<string, SectorData>();

        // ── Visitor / Shuttle system state ──────────────────────────────────

        // Append-only visit history for analytics (not loaded back into runtime state)
        public List<ShipVisitRecord>      visitHistory   = new List<ShipVisitRecord>();

        // Active shuttles (keyed by shuttle uid)
        public Dictionary<string, ShuttleInstance>  shuttles     = new Dictionary<string, ShuttleInstance>();
        // Landing pad states (keyed by foundation uid of the landing pad)
        public Dictionary<string, ShuttleLandingPadState> landingPads  = new Dictionary<string, ShuttleLandingPadState>();
        // Hail cooldowns per ship uid (player must wait before re-hailing)
        public List<HailCooldownRecord>   hailCooldowns  = new List<HailCooldownRecord>();

        // ── Player faction branding ──────────────────────────────────────────
        // Hex colour strings used for faction-owned sector borders on the galaxy map.
        public string playerFactionColor          = "#FFD700";
        public string playerFactionColorSecondary = "#0D2540";

        // ── Region Simulation state ──────────────────────────────────────────
        // Region data keyed by regionId. Stub — populated by Horizon Simulation work order.
        public Dictionary<string, RegionData> regions = new Dictionary<string, RegionData>();

        // ── Faction Government state ─────────────────────────────────────────
        // Cached aggregates per factionId; rebuilt by FactionGovernmentSystem when stale.
        public Dictionary<string, FactionTraitAggregate> factionAggregates =
            new Dictionary<string, FactionTraitAggregate>();

        public StationState(string name)
        {
            stationName = name;
            resources = new Dictionary<string, float>
            {
                { "credits", 500f }, { "food", 100f }, { "power", 100f },
                { "oxygen",  100f }, { "parts",  50f }, { "ice", 200f },
                { "fuel",     50f }
            };
            research = ResearchState.Create();
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

        public void   RestrictAction(string action)   => playerActionRestrictions.Add(action);
        public void   UnrestrictAction(string action) => playerActionRestrictions.Remove(action);
        public bool   IsActionRestricted(string action) => playerActionRestrictions.Contains(action);

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
            foreach (var s in ships.Values) if (s.status == "docked" ||
                s.visitState == ShipVisitState.Docked) list.Add(s);
            return list;
        }

        public List<ShipInstance> GetIncomingShips()
        {
            var list = new List<ShipInstance>();
            foreach (var s in ships.Values) if (s.status == "incoming" ||
                s.visitState == ShipVisitState.Inbound) list.Add(s);
            return list;
        }

        public List<ShipInstance> GetInRangeShips()
        {
            var list = new List<ShipInstance>();
            foreach (var s in ships.Values)
                if (s.visitState == ShipVisitState.InRange  ||
                    s.visitState == ShipVisitState.Passing  ||
                    s.visitState == ShipVisitState.Inbound  ||
                    s.visitState == ShipVisitState.Docked   ||
                    s.visitState == ShipVisitState.Departing)
                    list.Add(s);
            return list;
        }

        public ModuleInstance GetAvailableDock()
        {
            foreach (var m in modules.Values)
                if (m.IsAvailableDock()) return m;
            return null;
        }

        // -- Landing pad helpers ─────────────────────────────────────────────

        /// <summary>Count complete foundations with buildableId == "buildable.shuttle_landing_pad".</summary>
        public int CountLandingPads()
        {
            int count = 0;
            foreach (var f in foundations.Values)
                if (f.buildableId == "buildable.shuttle_landing_pad" && f.status == "complete")
                    count++;
            return count;
        }

        /// <summary>
        /// Returns the first unoccupied landing pad, or null if all are occupied or none exist.
        /// </summary>
        public ShuttleLandingPadState GetFreeLandingPad()
        {
            foreach (var f in foundations.Values)
            {
                if (f.buildableId != "buildable.shuttle_landing_pad" || f.status != "complete")
                    continue;
                if (!landingPads.TryGetValue(f.uid, out var pad))
                {
                    // Auto-register pad state on first query
                    pad = new ShuttleLandingPadState { foundationUid = f.uid };
                    landingPads[f.uid] = pad;
                }
                if (!pad.IsOccupied) return pad;
            }
            return null;
        }

        /// <summary>
        /// Returns true if a sealed room containing at least one Shuttle Landing Pad exists.
        /// For this work order, any complete landing pad foundation is treated as being in a
        /// valid Hangar (full room-sealing check is left to RoomSystem in a future pass).
        /// </summary>
        public bool HasFunctionalHangar() => CountLandingPads() > 0;

        /// <summary>Count complete foundations with a given buildableId.</summary>
        public int GetBuildableCount(string buildableId)
        {
            int count = 0;
            foreach (var f in foundations.Values)
                if (f.buildableId == buildableId && f.status == "complete")
                    count++;
            return count;
        }

        // -- Hail cooldown helpers ─────────────────────────────────────────────

        /// <summary>True if the player is still in the hail cooldown for the given ship.</summary>
        public bool IsHailOnCooldown(string shipUid)
        {
            foreach (var rec in hailCooldowns)
                if (rec.shipUid == shipUid && tick < rec.cooldownUntilTick)
                    return true;
            return false;
        }

        /// <summary>Remaining ticks until the hail cooldown for a ship expires.</summary>
        public int HailCooldownRemaining(string shipUid)
        {
            foreach (var rec in hailCooldowns)
                if (rec.shipUid == shipUid && tick < rec.cooldownUntilTick)
                    return rec.cooldownUntilTick - tick;
            return 0;
        }

        /// <summary>Set or refresh a 60-tick hail cooldown for the given ship.</summary>
        public void SetHailCooldown(string shipUid, int durationTicks = 60)
        {
            for (int i = 0; i < hailCooldowns.Count; i++)
            {
                if (hailCooldowns[i].shipUid == shipUid)
                {
                    hailCooldowns[i].cooldownUntilTick = tick + durationTicks;
                    return;
                }
            }
            hailCooldowns.Add(new HailCooldownRecord
            {
                shipUid            = shipUid,
                cooldownUntilTick  = tick + durationTicks,
            });
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
