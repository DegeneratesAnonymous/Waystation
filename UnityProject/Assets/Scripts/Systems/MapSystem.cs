// MapSystem — manages Points of Interest detection and map view level.
//
// Map view level is derived from research tag unlocks (not stored separately):
//   System = always available
//   Sector = requires tech.map_sector
//
// POI detection range is based on complete "buildable.antenna" foundations.
//   Base range = 500 units; each complete antenna adds +150 units.
// POIs are regenerated when the map is empty or every ~48 ticks (but never
// while asteroid missions are active, to preserve poiUid references).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MapSystem
    {
        private const float BaseRange    = 500f;
        private const float AntennaBonus = 150f;
        private const int ExplorationPointsPerInterstellarAntenna = 1;
        public const int SectorUnlockPointCost = 25;
        private const float GalUnitPerCell = 3.0f;

        // POIs are spread across a 2× detection radius; only those within range
        // are discovered and stored.  This makes discovery meaningful.
        private const float SpreadMultiplier = 2.0f;

        private int _lastGenTick = -1;
        private const int RegenInterval = 48;

        // ── Fullscreen state ──────────────────────────────────────────────────

        /// <summary>
        /// True while the full-screen map mode is active (e.g. triggered by the
        /// Map tab in the side panel). This flag is updated by <see cref="EnterFullscreen"/>
        /// and <see cref="ExitFullscreen"/> and can be consulted by the UI layer as
        /// needed, but fullscreen mode is initiated by UI events (for example,
        /// <c>SidePanelController.OnMapFullscreenRequested</c>), not by polling this value.
        /// </summary>
        public bool IsFullscreenActive { get; private set; }

        /// <summary>
        /// Enters fullscreen map mode. The side panel should collapse when this is
        /// called. Has no effect if fullscreen is already active.
        /// </summary>
        public void EnterFullscreen()
        {
            IsFullscreenActive = true;
        }

        /// <summary>
        /// Exits fullscreen map mode. Has no effect if fullscreen is not active.
        /// </summary>
        public void ExitFullscreen()
        {
            IsFullscreenActive = false;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station == null) return;

            bool empty    = station.pointsOfInterest.Count == 0;
            bool interval = station.tick - _lastGenTick >= RegenInterval;
            if (!empty && !interval) return;

            // Don't regen while asteroid missions are active — poiUid refs must stay valid.
            foreach (var am in station.asteroidMaps.Values)
                if (am.status == "active") return;

            GeneratePois(station);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Current map view level determined by research tags.</summary>
        public MapViewLevel GetMapViewLevel(StationState station)
        {
            if (station == null) return MapViewLevel.System;
            if (station.HasTag("tech.map_sector"))   return MapViewLevel.Sector;
            return MapViewLevel.System;
        }

        public int GetExplorationPointIncomePerTick(StationState station)
        {
            if (station == null) return 0;
            int count = 0;
            foreach (var f in station.foundations.Values)
            {
                if (IsPoweredCompleteFoundation(f, "buildable.interstellar_antenna"))
                    count++;
            }
            return count * ExplorationPointsPerInterstellarAntenna;
        }

        public bool IsSystemCharted(StationState station, int systemSeed)
            => station != null && station.chartedSystemSeeds.Contains(systemSeed);

        public void TickExplorationState(StationState station)
        {
            if (station == null) return;
            station.explorationPoints += GetExplorationPointIncomePerTick(station);
            PruneInvalidChartedSystems(station);
        }

        public bool TryUnlockSector(StationState station, int col, int row)
        {
            if (station == null) return false;
            if (station.explorationPoints < SectorUnlockPointCost) return false;

            float gx = GalaxyGenerator.HomeX + col * GalUnitPerCell;
            float gy = GalaxyGenerator.HomeY + row * GalUnitPerCell;

            foreach (var existing in station.sectors.Values)
            {
                if (Mathf.Approximately(existing.coordinates.x, gx) &&
                    Mathf.Approximately(existing.coordinates.y, gy))
                    return false;
            }

            bool adjacentToKnown = false;
            foreach (var existing in station.sectors.Values)
            {
                int ecol = Mathf.RoundToInt((existing.coordinates.x - GalaxyGenerator.HomeX) / GalUnitPerCell);
                int erow = Mathf.RoundToInt((existing.coordinates.y - GalaxyGenerator.HomeY) / GalUnitPerCell);
                if (Mathf.Abs(ecol - col) + Mathf.Abs(erow - row) == 1)
                {
                    adjacentToKnown = true;
                    break;
                }
            }
            if (!adjacentToKnown) return false;

            station.explorationPoints -= SectorUnlockPointCost;
            var generated = GalaxyGenerator.GenerateSectorAtCoordinates(
                station.galaxySeed, new Vector2(gx, gy), station);
            generated.discoveryState = SectorDiscoveryState.Detected;
            station.sectors[generated.uid] = generated;
            return true;
        }

        /// <summary>Detection range in world units, based on complete antenna count.</summary>
        public float GetDetectionRange(StationState station)
        {
            if (station == null) return BaseRange;
            int antennae = 0;
            foreach (var f in station.foundations.Values)
                if (f.status == "complete" && f.buildableId == "buildable.antenna")
                    antennae++;
            return BaseRange + antennae * AntennaBonus;
        }

        /// <summary>
        /// Regenerate (or refresh) the POI map.  Uses a stable FNV-1a seed derived
        /// from the station name, combined with an era counter (tick / RegenInterval),
        /// so the same station produces consistent POIs within each era.
        /// <para>
        /// Visited POIs (those tied to active or completed missions) are preserved
        /// across regens.  Undiscovered POIs are discarded and replaced.
        /// POIs at distance &gt; range are generated but not stored (they become
        /// visible when detection range increases and a new regen fires).
        /// </para>
        /// </summary>
        public void GeneratePois(StationState station)
        {
            if (station == null) return;

            _lastGenTick = station.tick;

            // Discard undiscovered / unvisited POIs so they can be refreshed.
            // Visited POIs (missions dispatched or completed) are preserved.
            var toRemove = new List<string>();
            foreach (var kv in station.pointsOfInterest)
                if (!kv.Value.visited) toRemove.Add(kv.Key);
            foreach (var k in toRemove)
                station.pointsOfInterest.Remove(k);

            var    level = GetMapViewLevel(station);
            float  range = GetDetectionRange(station);
            // Stable hash avoids runtime-version-dependent string.GetHashCode().
            int    seed  = StableHash(station.stationName);
            var    rng   = new System.Random(seed + station.tick / RegenInterval);

            int count = level switch
            {
                MapViewLevel.System   => rng.Next(1, 4),   // 1-3
                MapViewLevel.Sector   => rng.Next(4, 9),   // 4-8
                _                    => rng.Next(1, 4),
            };

            string[] poiTypes = level switch
            {
                MapViewLevel.System   => new[] { "Asteroid" },
                MapViewLevel.Sector   => new[] { "Asteroid", "TradePost", "AbandonedStation" },
                _                    => new[] { "Asteroid" },
            };

            float spreadRadius = range * SpreadMultiplier;

            for (int i = 0; i < count; i++)
            {
                string type  = poiTypes[rng.Next(poiTypes.Length)];
                float  angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float  dist  = (float)(rng.NextDouble() * spreadRadius);
                float  x     = (float)Math.Cos(angle) * dist;
                float  y     = (float)Math.Sin(angle) * dist;
                // pSeed is consumed here regardless of discovery, keeping the RNG sequence stable.
                int    pSeed = rng.Next(int.MaxValue);
                string name  = GenerateName(type, i, rng);

                // Asteroid yield is always generated to maintain RNG determinism.
                int oreAmt = 0, iceAmt = 0;
                if (type == "Asteroid")
                {
                    oreAmt = rng.Next(20, 120);
                    iceAmt = rng.Next(10, 80);
                }

                // Only discovered (within range) POIs are stored.
                if (dist > range) continue;

                // Deterministic UID from pSeed ensures stable identity across regens.
                string uid = $"poi_{pSeed:x8}";
                // Preserve visited state if the POI was already known.
                if (station.pointsOfInterest.ContainsKey(uid)) continue;

                var poi = PointOfInterest.Create(type, name, x, y, pSeed);
                poi.uid        = uid;
                poi.discovered = true;

                if (type == "Asteroid")
                {
                    poi.resourceYield["item.parts"] = oreAmt;
                    poi.resourceYield["item.ice"]   = iceAmt;
                }

                station.pointsOfInterest[uid] = poi;
            }
        }

        /// <summary>
        /// Reconciles physical exploration-datachip inventory with map chart state.
        /// Pass 1 keeps chips still present in their holder inventory, pass 2 rebinds
        /// moved chips to any remaining holder slot, and only truly lost chips are removed.
        /// Final chart state is then rebuilt from chips installed in powered cartography servers.
        /// </summary>
        private static void PruneInvalidChartedSystems(StationState station)
        {
            var stillInstalled = new HashSet<int>();
            var holderSlots = new Dictionary<string, int>();
            foreach (var f in station.foundations.Values)
            {
                int held = f.cargo.TryGetValue("item.exploration_datachip", out var n) ? n : 0;
                if (held > 0) holderSlots[f.uid] = held;
            }

            // First pass: keep chips whose current holder still has available chip count.
            var unresolved = new List<KeyValuePair<string, ExplorationDatachipInstance>>();
            foreach (var kv in station.explorationDatachips)
            {
                var chip = kv.Value;
                if (!string.IsNullOrEmpty(chip.holderFoundationUid) &&
                    holderSlots.TryGetValue(chip.holderFoundationUid, out int slots) &&
                    slots > 0)
                {
                    holderSlots[chip.holderFoundationUid] = slots - 1;
                }
                else
                {
                    unresolved.Add(kv);
                }
            }

            // Second pass: rebind unresolved chips to any holder slot (chip moved).
            foreach (var kv in unresolved)
            {
                string reassignedHolder = null;
                foreach (var hs in holderSlots)
                {
                    if (hs.Value <= 0) continue;
                    reassignedHolder = hs.Key;
                    holderSlots[hs.Key] = hs.Value - 1;
                    break;
                }

                if (string.IsNullOrEmpty(reassignedHolder))
                {
                    station.explorationDatachips.Remove(kv.Key);
                    continue;
                }

                kv.Value.holderFoundationUid = reassignedHolder;
            }

            foreach (var chip in station.explorationDatachips.Values)
            {
                chip.installedInServer = false;
                if (string.IsNullOrEmpty(chip.holderFoundationUid) ||
                    !station.foundations.TryGetValue(chip.holderFoundationUid, out var holder))
                    continue;

                chip.installedInServer =
                    holder.buildableId == "buildable.cartography_server" &&
                    holder.status == "complete" &&
                    holder.Functionality() > 0f &&
                    holder.isEnergised;

                if (chip.installedInServer)
                    stillInstalled.Add(chip.systemSeed);
            }

            station.chartedSystemSeeds = stillInstalled;
        }

        private static bool IsPoweredCompleteFoundation(FoundationInstance f, string buildableId)
        {
            if (f == null) return false;
            return f.buildableId == buildableId &&
                   f.status == "complete" &&
                   f.Functionality() > 0f &&
                   f.isEnergised;
        }

        /// <summary>All currently discovered POIs.</summary>
        public List<PointOfInterest> GetDiscoveredPois(StationState station)
        {
            var list = new List<PointOfInterest>();
            if (station == null) return list;
            foreach (var poi in station.pointsOfInterest.Values)
                if (poi.discovered) list.Add(poi);
            return list;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// FNV-1a hash over the UTF-16 characters of <paramref name="s"/>.
        /// Unlike <c>string.GetHashCode()</c>, this is stable across all
        /// .NET runtime versions and Unity platforms.
        /// </summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return (int)(hash & int.MaxValue); // ensure non-negative seed
            }
        }

        private static readonly string[][] NameParts =
        {
            new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Theta", "Sigma", "Zeta" },
            new[] { "Prime", "Secundus", "Major", "Minor", "Proxima", "Nova", "Deep" },
        };

        private string GenerateName(string type, int index, System.Random rng)
        {
            string prefix = NameParts[0][rng.Next(NameParts[0].Length)];
            string suffix = NameParts[1][rng.Next(NameParts[1].Length)];
            return type switch
            {
                "Asteroid"         => $"{prefix}-{index + 1:D3}",
                "TradePost"        => $"{prefix} {suffix} Trading Post",
                "AbandonedStation" => $"{prefix} {suffix} Station",
                "NebulaPocket"     => $"{prefix} Nebula",
                _                 => $"{prefix} {index + 1}",
            };
        }

        public bool HasPoweredCompleteBuildable(StationState station, string buildableId)
        {
            if (station == null || string.IsNullOrEmpty(buildableId)) return false;
            foreach (var f in station.foundations.Values)
                if (IsPoweredCompleteFoundation(f, buildableId))
                    return true;
            return false;
        }
    }
}
