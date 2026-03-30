// AsteroidMissionSystem — manages asteroid POI missions and resource extraction.
// Works alongside MissionSystem for crew dispatch; handles the asteroid-specific
// simulation including map generation, real-time failure-state evaluation, and
// yield calculation on completion.
//
// EXP-004 additions:
//   • Four failure states: abandonment (manual retreat), autonomous abort
//     (crew viability), distress signal (mid-mission crisis), total loss
//     (catastrophic event or unresponded distress window).
//   • Public API for player interactions: IssueRetreatOrder, RespondToDistressSignal,
//     TriggerDistressSignal, TriggerCatastrophicLoss.
//   • Per-tick crew viability evaluation via ComputeViabilityScore.
//   • Partial yield (PartialYieldMultiplier) applied on abort/rescue outcomes.
//   • All new failure-state checks are gated by FeatureFlags.AsteroidMissions.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class AsteroidMissionSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Fraction of resource tiles extracted per crew member on full completion.</summary>
        private const float YieldFractionPerCrew = 0.10f;

        /// <summary>
        /// Crew viability score below which the NPCs initiate an autonomous abort.
        /// Viability is a 0.0–1.0 composite of mood, injuries, and threat level.
        /// </summary>
        public const float ViabilityAbortThreshold = 0.30f;

        /// <summary>Default rescue dispatch window in ticks (player must respond before expiry).</summary>
        public const int DistressWindowDefaultTicks = 120;

        /// <summary>Yield multiplier applied when the mission ends in a partial-yield outcome.</summary>
        public const float PartialYieldMultiplier = 0.50f;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatch an asteroid mining mission to the given POI.
        /// Creates a <see cref="MissionInstance"/> (type "asteroid_mining"), generates
        /// the procedural map, locks the crew, and registers everything with the station.
        /// </summary>
        public (bool ok, string reason, AsteroidMapState map) DispatchAsteroidMission(
            string poiUid, List<string> crewUids, StationState station,
            int durationTicks = 480)
        {
            if (!station.pointsOfInterest.TryGetValue(poiUid, out var poi))
                return (false, $"POI '{poiUid}' not found.", null);
            if (poi.poiType != "Asteroid")
                return (false, "Only Asteroid POIs support mining missions.", null);
            if (crewUids == null || crewUids.Count == 0)
                return (false, "No crew specified for mission.", null);

            // Validate crew.
            foreach (var uid in crewUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc))
                    return (false, $"NPC '{uid}' not found.", null);
                if (!npc.IsCrew())
                    return (false, $"{npc.name} is not crew.", null);
                if (npc.missionUid != null)
                    return (false, $"{npc.name} is already on a mission.", null);
            }

            // Create a lightweight mission entry.
            var mission = MissionInstance.Create(
                "mission.asteroid_mining", "asteroid_mining", "Asteroid Mining",
                station.tick, durationTicks);
            foreach (var uid in crewUids)
                mission.crewUids.Add(uid);

            station.missions[mission.uid] = mission;

            // Generate the asteroid map.
            int seed = poi.seed ^ station.tick;
            var asteroidMap = AsteroidMapGenerator.Generate(
                poiUid, mission.uid, seed,
                startTick: station.tick, durationTicks: durationTicks);

            station.asteroidMaps[asteroidMap.uid] = asteroidMap;

            // Lock crew.
            foreach (var uid in crewUids)
            {
                if (station.npcs.TryGetValue(uid, out var npc))
                {
                    npc.missionUid = mission.uid;
                    asteroidMap.assignedNpcUids.Add(uid);
                }
            }

            poi.visited = true;
            station.LogEvent($"Asteroid mining mission dispatched to {poi.displayName} " +
                             $"({crewUids.Count} crew, {durationTicks} ticks).");

            return (true, null, asteroidMap);
        }

        /// <summary>
        /// Issues a manual retreat order for the given mission map.
        /// The crew will begin returning to the ship; the mission resolves on the next
        /// tick with partial yield (outcome: "abandonment").
        /// </summary>
        public (bool ok, string reason) IssueRetreatOrder(string mapUid, StationState station)
        {
            if (station == null)                                          return (false, "Station is null.");
            if (!station.asteroidMaps.TryGetValue(mapUid, out var map))  return (false, $"Map '{mapUid}' not found.");
            if (map.status != "active")                                   return (false, $"Mission is not active (status: {map.status}).");

            map.retreatOrdered = true;
            station.LogEvent("Retreat order issued. Crew is returning to ship.");
            return (true, null);
        }

        /// <summary>
        /// Fires a distress signal for the given mission.  The player has
        /// <paramref name="windowTicks"/> ticks to respond before the mission
        /// escalates to total loss.
        /// No-op if a distress signal is already active.
        /// </summary>
        public (bool ok, string reason) TriggerDistressSignal(
            string mapUid, StationState station,
            int windowTicks = DistressWindowDefaultTicks)
        {
            if (station == null)                                          return (false, "Station is null.");
            if (!station.asteroidMaps.TryGetValue(mapUid, out var map))  return (false, $"Map '{mapUid}' not found.");
            if (map.status != "active")                                   return (false, "Mission is not active.");
            if (map.distressSignalActive)                                 return (false, "Distress signal already active.");

            map.distressSignalActive     = true;
            map.distressWindowExpiryTick = station.tick + windowTicks;
            station.LogEvent($"[DISTRESS SIGNAL] Mission crew in crisis! Rescue window: {windowTicks} ticks.");
            return (true, null);
        }

        /// <summary>
        /// Player response to an active distress signal.  Marks rescue as dispatched;
        /// the mission resolves on the next tick with partial yield (outcome: "rescued").
        /// Returns <c>(false, reason)</c> when there is no active distress signal.
        /// </summary>
        public (bool ok, string reason) RespondToDistressSignal(string mapUid, StationState station)
        {
            if (station == null)                                          return (false, "Station is null.");
            if (!station.asteroidMaps.TryGetValue(mapUid, out var map))  return (false, $"Map '{mapUid}' not found.");
            if (!map.distressSignalActive)                                return (false, "No active distress signal.");
            if (map.rescueDispatched)                                     return (false, "Rescue already dispatched.");

            map.rescueDispatched     = true;
            map.distressSignalActive = false;
            station.LogEvent("Rescue dispatched. Distress signal acknowledged — crew recovering.");
            return (true, null);
        }

        /// <summary>
        /// Immediately resolves the mission as a catastrophic total loss.
        /// No distress signal is generated; the mission ends with no yield.
        /// </summary>
        public (bool ok, string reason) TriggerCatastrophicLoss(string mapUid, StationState station)
        {
            if (station == null)                                          return (false, "Station is null.");
            if (!station.asteroidMaps.TryGetValue(mapUid, out var map))  return (false, $"Map '{mapUid}' not found.");
            if (map.status != "active")                                   return (false, "Mission is not active.");

            ResolveTotalLoss(map, station, "total_loss");
            return (true, null);
        }

        /// <summary>
        /// Computes the crew viability score for the given mission (0.0–1.0).
        /// Score is the average per-NPC composite of mood, injuries, and threat level.
        /// An NPC in <c>inCrisis</c> contributes 0.0 regardless of other factors.
        /// Returns 1.0 when there are no assignable NPCs (safe default).
        /// </summary>
        public float ComputeViabilityScore(AsteroidMapState map, StationState station)
        {
            if (map == null || station == null) return 1f;
            if (map.assignedNpcUids.Count == 0) return 1f;

            float total = 0f;
            int   count = 0;
            foreach (var uid in map.assignedNpcUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc)) continue;

                if (npc.inCrisis) { total += 0f; count++; continue; }

                float moraleFactor = npc.moodScore / 100f;
                float injuryFactor = Mathf.Clamp01(1f - npc.injuries * 0.20f);
                float threatFactor = 1f - Mathf.Clamp01(map.threatLevel);

                total += 0.40f * moraleFactor
                       + 0.40f * injuryFactor
                       + 0.20f * threatFactor;
                count++;
            }
            return count == 0 ? 1f : total / count;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Per-tick update.  When <see cref="FeatureFlags.AsteroidMissions"/> is true,
        /// evaluates all four failure states before falling through to normal completion.
        /// When the flag is false only time-based completion runs (pre-EXP-004 behaviour).
        /// </summary>
        public void Tick(StationState station)
        {
            if (station == null) return;

            // Snapshot active maps to avoid mutating the dictionary during iteration.
            var active = new List<(string uid, AsteroidMapState map)>();
            foreach (var kv in station.asteroidMaps)
                if (kv.Value.status == "active")
                    active.Add((kv.Key, kv.Value));

            foreach (var (uid, map) in active)
            {
                if (map.status != "active") continue; // changed mid-loop

                if (FeatureFlags.AsteroidMissions)
                {
                    // 1. Manual retreat → abandonment (partial yield)
                    if (map.retreatOrdered)
                    {
                        ResolveWithPartialYield(map, station, "abandonment");
                        continue;
                    }

                    // 2. Rescue already dispatched → crew rescued (partial yield)
                    if (map.rescueDispatched)
                    {
                        ResolveWithPartialYield(map, station, "rescued");
                        continue;
                    }

                    // 3. Active distress window expired without rescue → total loss
                    if (map.distressSignalActive &&
                        station.tick >= map.distressWindowExpiryTick)
                    {
                        ResolveTotalLoss(map, station, "total_loss");
                        continue;
                    }

                    // 4. NPC autonomous abort — viability below threshold
                    //    (skip while distress signal is pending; let the window resolve first)
                    if (!map.distressSignalActive)
                    {
                        float viability = ComputeViabilityScore(map, station);
                        if (viability < ViabilityAbortThreshold)
                        {
                            ResolveWithPartialYield(map, station, "autonomous_abort");
                            continue;
                        }
                    }
                }

                // 5. Normal time-based completion → full yield
                if (station.tick >= map.endTick)
                    CompleteAsteroidMission(uid, station);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Return a single row of tile values from the map for UI rendering.</summary>
        public int[] GetTileRow(AsteroidMapState map, int y)
        {
            if (map == null || y < 0 || y >= map.height) return Array.Empty<int>();
            var row = new int[map.width];
            for (int x = 0; x < map.width; x++)
                row[x] = map.GetTile(x, y);
            return row;
        }

        // ── Private resolution helpers ────────────────────────────────────────

        private void CompleteAsteroidMission(string mapUid, StationState station)
        {
            if (!station.asteroidMaps.TryGetValue(mapUid, out var map)) return;

            // Count ore and ice tiles.
            int oreTiles = 0, iceTiles = 0;
            foreach (byte t in map.tiles)
            {
                if (t == (byte)AsteroidTile.Ore) oreTiles++;
                else if (t == (byte)AsteroidTile.Ice) iceTiles++;
            }

            int crewCount = Mathf.Max(1, map.assignedNpcUids.Count);
            int oreYield  = Mathf.RoundToInt(oreTiles * YieldFractionPerCrew * crewCount);
            int iceYield  = Mathf.RoundToInt(iceTiles * YieldFractionPerCrew * crewCount);

            // Add resources.
            // Ore tiles yield parts (refined in the shuttle en-route); ice tiles yield raw ice.
            station.ModifyResource("parts", oreYield);
            station.ModifyResource("ice",   iceYield);

            map.extractedResources["parts"] = oreYield;
            map.extractedResources["ice"]   = iceYield;
            map.status = "complete";

            FreeCrew(map, station);
            FinaliseMission(map, station, "complete");

            station.LogEvent(
                $"Asteroid mining complete. Extracted {oreYield} parts, {iceYield} ice.");
        }

        /// <summary>
        /// Resolves the mission with partial yield (PartialYieldMultiplier applied).
        /// Used for abandonment, autonomous abort, and rescue outcomes.
        /// </summary>
        private void ResolveWithPartialYield(
            AsteroidMapState map, StationState station, string outcome)
        {
            int oreTiles = 0, iceTiles = 0;
            foreach (byte t in map.tiles)
            {
                if (t == (byte)AsteroidTile.Ore) oreTiles++;
                else if (t == (byte)AsteroidTile.Ice) iceTiles++;
            }

            int crewCount = Mathf.Max(1, map.assignedNpcUids.Count);
            int oreYield  = Mathf.RoundToInt(oreTiles * YieldFractionPerCrew * crewCount * PartialYieldMultiplier);
            int iceYield  = Mathf.RoundToInt(iceTiles * YieldFractionPerCrew * crewCount * PartialYieldMultiplier);

            station.ModifyResource("parts", oreYield);
            station.ModifyResource("ice",   iceYield);

            map.extractedResources["parts"] = oreYield;
            map.extractedResources["ice"]   = iceYield;
            map.status = outcome;

            FreeCrew(map, station);
            FinaliseMission(map, station, outcome);

            station.LogEvent(
                $"Mission ended ({outcome}). Recovered {oreYield} parts, {iceYield} ice.");
        }

        /// <summary>
        /// Resolves the mission as a total loss: no resources are recovered,
        /// crew is freed but the mission is marked lost.
        /// </summary>
        private void ResolveTotalLoss(
            AsteroidMapState map, StationState station, string outcome)
        {
            map.extractedResources.Clear();
            map.status = outcome;

            FreeCrew(map, station);
            FinaliseMission(map, station, "total_loss");

            station.LogEvent($"Mission total loss! All resources lost ({outcome}).");
        }

        /// <summary>Unlocks every assigned NPC's missionUid.</summary>
        private static void FreeCrew(AsteroidMapState map, StationState station)
        {
            foreach (var npcUid in map.assignedNpcUids)
                if (station.npcs.TryGetValue(npcUid, out var npc))
                    npc.missionUid = null;
        }

        /// <summary>Updates the linked MissionInstance status.</summary>
        private static void FinaliseMission(
            AsteroidMapState map, StationState station, string missionStatus)
        {
            if (!string.IsNullOrEmpty(map.missionUid) &&
                station.missions.TryGetValue(map.missionUid, out var mission))
            {
                mission.status = missionStatus;
            }
        }
    }
}
