// DiplomatRole — objective queue and dispatch for player-sent diplomats (WO-FAC-009).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>Outcome tier for diplomat missions (worst→best).</summary>
    public enum DiplomatOutcomeTier
    {
        CriticalFailure,  // ship lost, NPC captured/killed
        Failure,          // mission failed, rep penalty, NPC returns
        Partial,          // partial success, small rep gain
        Success,          // full success, rep gain, possible trade bonus
        Exceptional       // bonus outcomes, alliance offers
    }

    /// <summary>Diplomat mission objective.</summary>
    public class DiplomatMission
    {
        public string id;
        public string assignedNpcUid;
        public string assignedShipUid;
        public string targetFactionId;
        public string targetStationId;
        public int    dispatchTick;
        public int    arrivalTick;       // estimated
        public int    returnTick;        // estimated
        public bool   completed;
        public bool   intercepted;
        public DiplomatOutcomeTier outcome;
        public string outcomeFlavorText;
        public float  repChange;         // computed rep delta
        public float  tradeBonus;        // computed trade bonus (credits)
        public Dictionary<string, object> context = new Dictionary<string, object>();
    }

    /// <summary>
    /// Manages diplomat NPC dispatch, travel, and outcome resolution.
    /// Requires an NPC with the "diplomat" trait or assigned to the diplomat job.
    /// </summary>
    public class DiplomatRole
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const int TravelTimePerSector = 24;  // ticks per sector distance
        private const int MissionDurationTicks = 48; // time spent at destination
        private const float BaseSuccessChance = 0.6f;
        private const float InterceptionChancePerSector = 0.05f; // 5% per sector
        private const float MinDiplomatSkill = 0.3f;

        // ── Outcome probability weights per tier ──────────────────────────────
        private static readonly float[] TierWeights = { 0.05f, 0.15f, 0.30f, 0.35f, 0.15f };
        // Modifiers per rep bracket (hostile, neutral, friendly, allied)
        private static readonly float[] RepModifiers = { -0.3f, 0f, 0.15f, 0.3f };

        // ── State ─────────────────────────────────────────────────────────────
        private readonly List<DiplomatMission> _activeMissions = new List<DiplomatMission>();
        private readonly List<DiplomatMission> _completedMissions = new List<DiplomatMission>();

        // ── Dependencies ──────────────────────────────────────────────────────
        private ShipSystem _shipSystem;
        private EventSystem _events;

        public void SetDependencies(ShipSystem shipSystem, EventSystem events)
        {
            _shipSystem = shipSystem;
            _events = events;
        }

        // ── Dispatch ──────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatch a diplomat NPC on a mission to a foreign faction station.
        /// Returns the mission or null if prerequisites aren't met.
        /// </summary>
        public DiplomatMission Dispatch(string npcUid, string shipUid,
            string targetFactionId, string targetStationId, StationState station)
        {
            // Validate NPC exists and is available
            if (!station.npcs.TryGetValue(npcUid, out var npc)) return null;

            // Validate ship exists and is available
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return null;
            if (ship.status != "docked") return null;

            // Calculate travel time
            int sectorDistance = EstimateSectorDistance(station, targetStationId);
            int travelTime = sectorDistance * TravelTimePerSector;

            var mission = new DiplomatMission
            {
                id = $"diplomat_{station.tick}_{npcUid}",
                assignedNpcUid = npcUid,
                assignedShipUid = shipUid,
                targetFactionId = targetFactionId,
                targetStationId = targetStationId,
                dispatchTick = station.tick,
                arrivalTick = station.tick + travelTime,
                returnTick = station.tick + travelTime * 2 + MissionDurationTicks
            };

            // Mark NPC and ship as on mission
            npc.missionUid = mission.id;
            npc.assignedShipUid = shipUid;
            ship.status = "on_mission";

            _activeMissions.Add(mission);
            station.LogEvent($"Diplomat {npc.name} dispatched to {targetFactionId} ({targetStationId})");

            return mission;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Process active diplomat missions. Called on medium tick (Channel 2).
        /// </summary>
        public void Tick(StationState station)
        {
            int tick = station.tick;

            for (int i = _activeMissions.Count - 1; i >= 0; i--)
            {
                var mission = _activeMissions[i];

                // Check for interception during travel
                if (!mission.intercepted && tick < mission.arrivalTick)
                {
                    int sectorDistance = EstimateSectorDistance(station, mission.targetStationId);
                    float interceptionChance = sectorDistance * InterceptionChancePerSector;

                    // Only roll once per medium tick interval
                    if (UnityEngine.Random.value < interceptionChance * 0.1f) // scaled per tick
                    {
                        mission.intercepted = true;
                        HandleInterception(mission, station);
                    }
                    continue;
                }

                // Check for mission completion (return to station)
                if (tick >= mission.returnTick && !mission.completed)
                {
                    ResolveMission(mission, station);
                    _activeMissions.RemoveAt(i);
                    _completedMissions.Add(mission);
                }
            }
        }

        // ── Resolution ───────────────────────────────────────────────────────

        private void ResolveMission(DiplomatMission mission, StationState station)
        {
            mission.completed = true;

            if (mission.intercepted) return; // already handled

            // Determine outcome tier
            float repWithTarget = station.GetFactionRep(mission.targetFactionId);
            int repBracket = repWithTarget <= -25f ? 0 : repWithTarget < 25f ? 1 : repWithTarget < 60f ? 2 : 3;
            float repMod = RepModifiers[repBracket];

            // NPC skill bonus
            float skillBonus = 0f;
            if (station.npcs.TryGetValue(mission.assignedNpcUid, out var npc))
            {
                // Check for diplomat trait or social skill
                if (npc.traits != null && npc.traits.Contains("diplomat"))
                    skillBonus += 0.15f;
            }

            // Roll outcome tier
            mission.outcome = RollOutcomeTier(repMod + skillBonus);

            // Apply outcomes
            switch (mission.outcome)
            {
                case DiplomatOutcomeTier.CriticalFailure:
                    mission.repChange = -15f;
                    mission.tradeBonus = 0f;
                    // Ship may be damaged
                    if (station.ownedShips.TryGetValue(mission.assignedShipUid, out var ship1))
                    {
                        ship1.conditionPct = Mathf.Max(10f, ship1.conditionPct - 40f);
                    }
                    break;

                case DiplomatOutcomeTier.Failure:
                    mission.repChange = -5f;
                    mission.tradeBonus = 0f;
                    break;

                case DiplomatOutcomeTier.Partial:
                    mission.repChange = 3f;
                    mission.tradeBonus = 50f;
                    break;

                case DiplomatOutcomeTier.Success:
                    mission.repChange = 8f;
                    mission.tradeBonus = 200f;
                    break;

                case DiplomatOutcomeTier.Exceptional:
                    mission.repChange = 15f;
                    mission.tradeBonus = 500f;
                    break;
            }

            // Apply rep change
            station.ModifyFactionRep(mission.targetFactionId, mission.repChange);

            // Apply trade bonus
            if (mission.tradeBonus > 0f)
                station.ModifyResource("credits", mission.tradeBonus);

            // Return NPC and ship
            ReturnFromMission(mission, station);

            station.LogEvent($"Diplomat returned from {mission.targetFactionId}: {mission.outcome} (rep {mission.repChange:+0;-0})");

            // Fire event for UI
            _events?.FireReactiveTrigger("diplomat_returned", station,
                new Dictionary<string, object>
                {
                    { "mission_id", mission.id },
                    { "outcome", mission.outcome.ToString() },
                    { "faction_id", mission.targetFactionId }
                });
        }

        private void HandleInterception(DiplomatMission mission, StationState station)
        {
            station.LogEvent($"Diplomat en route to {mission.targetFactionId} was intercepted!");

            // Interception outcome: 60% safe escape, 25% delayed, 15% captured
            float roll = UnityEngine.Random.value;
            if (roll < 0.60f)
            {
                // Safe escape — mission continues but delayed
                mission.intercepted = false;
                mission.arrivalTick += TravelTimePerSector * 2;
                mission.returnTick += TravelTimePerSector * 2;
                station.LogEvent("Diplomat escaped interception. Mission delayed.");
            }
            else if (roll < 0.85f)
            {
                // Mission aborted, diplomat returns damaged
                mission.completed = true;
                mission.outcome = DiplomatOutcomeTier.Failure;
                mission.repChange = -3f;
                station.ModifyFactionRep(mission.targetFactionId, mission.repChange);

                if (station.ownedShips.TryGetValue(mission.assignedShipUid, out var ship))
                    ship.conditionPct = Mathf.Max(20f, ship.conditionPct - 25f);

                ReturnFromMission(mission, station);
                _activeMissions.Remove(mission);
                _completedMissions.Add(mission);
                station.LogEvent("Diplomat forced to abort mission after interception.");
            }
            else
            {
                // Captured — NPC and ship lost
                mission.completed = true;
                mission.outcome = DiplomatOutcomeTier.CriticalFailure;
                mission.repChange = -10f;
                station.ModifyFactionRep(mission.targetFactionId, mission.repChange);

                // Remove ship and NPC
                station.ownedShips.Remove(mission.assignedShipUid);
                if (station.npcs.TryGetValue(mission.assignedNpcUid, out var npc))
                {
                    if (!npc.statusTags.Contains("captured"))
                        npc.statusTags.Add("captured");
                }

                _activeMissions.Remove(mission);
                _completedMissions.Add(mission);
                station.LogEvent($"Diplomat captured during interception! Ship and crew lost.");

                _events?.FireReactiveTrigger("diplomat_captured", station,
                    new Dictionary<string, object>
                    {
                        { "mission_id", mission.id },
                        { "npc_uid", mission.assignedNpcUid }
                    });
            }
        }

        private void ReturnFromMission(DiplomatMission mission, StationState station)
        {
            if (station.npcs.TryGetValue(mission.assignedNpcUid, out var npc))
            {
                npc.missionUid = null;
                npc.assignedShipUid = null;
            }
            if (station.ownedShips.TryGetValue(mission.assignedShipUid, out var ship))
            {
                ship.status = "docked";
            }
        }

        private DiplomatOutcomeTier RollOutcomeTier(float modifier)
        {
            float[] adjusted = new float[TierWeights.Length];
            float total = 0f;

            for (int i = 0; i < TierWeights.Length; i++)
            {
                // Positive modifier shifts weight toward higher tiers
                float shift = modifier * (i - 2f) * 0.1f;
                adjusted[i] = Mathf.Max(0.01f, TierWeights[i] + shift);
                total += adjusted[i];
            }

            float roll = UnityEngine.Random.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < adjusted.Length; i++)
            {
                acc += adjusted[i];
                if (roll <= acc) return (DiplomatOutcomeTier)i;
            }
            return DiplomatOutcomeTier.Partial;
        }

        private int EstimateSectorDistance(StationState station, string targetStationId)
        {
            // Simplified: use hash-based pseudo-distance (1-5 sectors)
            int hash = Math.Abs((targetStationId ?? "").GetHashCode()) % 5 + 1;
            return hash;
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public List<DiplomatMission> GetActiveMissions() => new List<DiplomatMission>(_activeMissions);
        public List<DiplomatMission> GetCompletedMissions() => new List<DiplomatMission>(_completedMissions);

        public DiplomatMission GetMission(string missionId)
        {
            return _activeMissions.FirstOrDefault(m => m.id == missionId)
                ?? _completedMissions.FirstOrDefault(m => m.id == missionId);
        }

        /// <summary>Check if an NPC is eligible to be a diplomat.</summary>
        public static bool IsEligibleDiplomat(NPCInstance npc)
        {
            if (npc == null) return false;
            if (npc.statusTags.Contains("captured") || npc.statusTags.Contains("dead")) return false;
            if (npc.missionUid != null) return false;
            // Check for diplomat trait, social skill, or communications job
            if (npc.traits != null && npc.traits.Contains("diplomat")) return true;
            if (npc.currentJobId == "communications_officer") return true;
            return false;
        }
    }
}
