// AsteroidMissionSystem — manages asteroid POI missions and resource extraction.
// Works alongside MissionSystem for crew dispatch; handles the asteroid-specific
// simulation including map generation and yield calculation on completion.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class AsteroidMissionSystem
    {
        // How many tiles are sampled for yield calculation (as a fraction).
        private const float YieldFractionPerCrew = 0.10f;

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

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station == null) return;

            var toComplete = new List<string>();
            foreach (var kv in station.asteroidMaps)
                if (kv.Value.status == "active" && station.tick >= kv.Value.endTick)
                    toComplete.Add(kv.Key);

            foreach (var uid in toComplete)
                CompleteAsteroidMission(uid, station);
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

            // Free crew.
            foreach (var npcUid in map.assignedNpcUids)
            {
                if (station.npcs.TryGetValue(npcUid, out var npc))
                    npc.missionUid = null;
            }

            // Complete linked mission instance.
            if (!string.IsNullOrEmpty(map.missionUid) &&
                station.missions.TryGetValue(map.missionUid, out var mission))
            {
                mission.status = "complete";
            }

            station.LogEvent(
                $"Asteroid mining complete. Extracted {oreYield} parts, {iceYield} ice.");
        }
    }
}
