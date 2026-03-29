// MissionSystem — dispatches and resolves away missions for crew members.
// Missions are created from MissionDefinition data, assigned to a list of crew,
// and resolved when their endTick is reached by rolling against the crew's
// relevant skill scores.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MissionSystem
    {
        private readonly ContentRegistry _registry;
        private const string ExplorationDatachipItemId = "item.exploration_datachip";

        public MissionSystem(ContentRegistry registry) => _registry = registry;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Attempt to dispatch a mission.  All validation is run first; on success
        /// a MissionInstance is created, crew are locked, and the instance is added
        /// to station.missions.
        /// </summary>
        public (bool ok, string reason, MissionInstance mission) DispatchMission(
            string defId, List<string> crewUids, StationState station)
        {
            if (!CanDispatch(defId, crewUids, station, out string reason))
                return (false, reason, null);

            var def     = _registry.Missions[defId];
            var mission = MissionInstance.Create(defId, def.missionType, def.displayName,
                                                 station.tick, def.durationTicks);
            // Assign reward template — resolved on completion
            mission.rewards = new Dictionary<string, float>(def.rewardsOnSuccess);

            foreach (var uid in crewUids)
            {
                mission.crewUids.Add(uid);
                if (station.npcs.TryGetValue(uid, out var npc))
                    npc.missionUid = mission.uid;
            }

            station.missions[mission.uid] = mission;
            station.LogEvent($"Mission '{def.displayName}' dispatched ({crewUids.Count} crew).");
            return (true, null, mission);
        }

        /// <summary>
        /// Validate whether a mission can be dispatched right now.
        /// Returns false and populates <paramref name="reason"/> on failure.
        /// </summary>
        public bool CanDispatch(string defId, List<string> crewUids,
                                 StationState station, out string reason)
        {
            reason = null;
            if (!_registry.Missions.TryGetValue(defId, out var def))
            {
                reason = $"Unknown mission definition '{defId}'.";
                return false;
            }
            if (crewUids == null || crewUids.Count < def.crewRequired)
            {
                reason = $"Mission requires {def.crewRequired} crew; {crewUids?.Count ?? 0} provided.";
                return false;
            }
            foreach (var uid in crewUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc))
                {
                    reason = $"NPC '{uid}' not found.";
                    return false;
                }
                if (!npc.IsCrew())
                {
                    reason = $"{npc.name} is not crew.";
                    return false;
                }
                if (npc.missionUid != null)
                {
                    reason = $"{npc.name} is already on a mission.";
                    return false;
                }
            }
            // Skill check: at least one crew member must meet the requirement
            if (!string.IsNullOrEmpty(def.requiredSkill) && def.requiredSkillLevel > 0)
            {
                bool anyQualified = false;
                foreach (var uid in crewUids)
                {
                    if (!station.npcs.TryGetValue(uid, out var npc)) continue;
                    int skill = npc.skills.ContainsKey(def.requiredSkill)
                                ? npc.skills[def.requiredSkill] : 0;
                    if (skill >= def.requiredSkillLevel) { anyQualified = true; break; }
                }
                if (!anyQualified)
                {
                    reason = $"No crew member has {def.requiredSkill} ≥ {def.requiredSkillLevel}.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// All mission definitions available for dispatch.
        /// </summary>
        public List<MissionDefinition> AvailableDefinitions()
        {
            var list = new List<MissionDefinition>(_registry.Missions.Values);
            return list;
        }

        /// <summary>
        /// Per-tick update: resolve any missions whose endTick has been reached.
        /// </summary>
        public void Tick(StationState station)
        {
            var toResolve = new List<string>();
            foreach (var kv in station.missions)
                if (kv.Value.status == "active" && station.tick >= kv.Value.endTick)
                    toResolve.Add(kv.Key);

            foreach (var uid in toResolve)
                ResolveMission(uid, station);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ResolveMission(string missionUid, StationState station)
        {
            if (!station.missions.TryGetValue(missionUid, out var mission)) return;
            if (!_registry.Missions.TryGetValue(mission.definitionId, out var def)) return;

            // Compute crew's average skill score for the required skill
            float skillScore = ComputeCrewSkillScore(mission.crewUids, def.requiredSkill, station);
            // Scale success chance by skill (clamp to reasonable range)
            float adjustedChance = Mathf.Clamp01(def.successChanceBase * (0.5f + skillScore * 0.1f));

            bool success = UnityEngine.Random.value <= adjustedChance;
            var  rewards = success ? def.rewardsOnSuccess : def.rewardsOnPartial;

            // Apply rewards to station resources
            foreach (var kv in rewards)
                station.ModifyResource(kv.Key, kv.Value);

            // Capitalise result label for log
            string resultLabel = success ? "Success" : "Partial success";
            mission.status = success ? "complete" : "partial";
            station.LogEvent($"Mission '{def.displayName}' returned. {resultLabel}.");

            if (success)
                TryProduceExplorationDatachip(mission, station);

            // Unlock crew
            foreach (var crewUid in mission.crewUids)
            {
                if (station.npcs.TryGetValue(crewUid, out var npc))
                    npc.missionUid = null;
            }
        }

        private void TryProduceExplorationDatachip(MissionInstance mission, StationState station)
        {
            if (mission == null || station == null) return;
            if (!string.Equals(mission.missionType, "scout", StringComparison.OrdinalIgnoreCase)) return;
            if (mission.targetSystemSeed == 0) return;

            FoundationInstance cartographyStation = null;
            FoundationInstance chipHolder = null;

            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.Functionality() <= 0f) continue;
                if (f.buildableId == "buildable.cartography_station")
                    cartographyStation = f;
                if (chipHolder == null && f.cargoCapacity > 0)
                    chipHolder = f;
            }

            if (cartographyStation == null || !cartographyStation.isEnergised || chipHolder == null) return;

            int current = chipHolder.cargo.TryGetValue(ExplorationDatachipItemId, out var n) ? n : 0;
            if (current >= chipHolder.cargoCapacity) return;

            chipHolder.cargo[ExplorationDatachipItemId] = current + 1;
            var chip = ExplorationDatachipInstance.Create(mission.targetSystemSeed, mission.targetSystemName);
            chip.holderFoundationUid = chipHolder.uid;
            station.explorationDatachips[chip.uid] = chip;
            station.LogEvent($"Exploration Datachip created: {chip.systemName}");
        }

        private float ComputeCrewSkillScore(List<string> crewUids, string skill, StationState station)
        {
            if (string.IsNullOrEmpty(skill) || crewUids.Count == 0) return 5f;
            float total = 0f;
            int   count = 0;
            foreach (var uid in crewUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc)) continue;
                total += npc.skills.ContainsKey(skill) ? npc.skills[skill] : 0;
                count++;
            }
            return count > 0 ? total / count : 0f;
        }
    }
}
