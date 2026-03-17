// MapSystem — manages Points of Interest detection and map view level.
//
// Map view level is derived from research tag unlocks (not stored separately):
//   System   = always available
//   Sector   = requires tech.map_sector
//   Quadrant = requires tech.map_quadrant
//   Galaxy   = requires tech.map_galaxy
//
// POI detection range is based on complete "buildable.antenna" foundations.
//   Base range = 500 units; each complete antenna adds +150 units.
// POIs are regenerated when the map is empty or every ~48 ticks.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MapSystem
    {
        private readonly ContentRegistry _registry;

        private const float BaseRange    = 500f;
        private const float AntennaBonus = 150f;

        private int _lastGenTick = -1;
        private const int RegenInterval = 48;

        public MapSystem(ContentRegistry registry) => _registry = registry;

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station == null) return;

            bool empty    = station.pointsOfInterest.Count == 0;
            bool interval = station.tick - _lastGenTick >= RegenInterval;

            if (empty || interval)
                GeneratePois(station);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Current map view level determined by research tags.</summary>
        public MapViewLevel GetMapViewLevel(StationState station)
        {
            if (station == null) return MapViewLevel.System;
            if (station.HasTag("tech.map_galaxy"))   return MapViewLevel.Galaxy;
            if (station.HasTag("tech.map_quadrant")) return MapViewLevel.Quadrant;
            if (station.HasTag("tech.map_sector"))   return MapViewLevel.Sector;
            return MapViewLevel.System;
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
        /// Regenerate (or refresh) the POI map.  The layout is seeded from the station
        /// name hash combined with an era counter (tick / RegenInterval) so that the same
        /// station produces a consistent set of POIs within each era but refreshes them
        /// every ~48 ticks as the player's map view level expands.
        /// </summary>
        public void GeneratePois(StationState station)
        {
            if (station == null) return;

            _lastGenTick = station.tick;
            station.pointsOfInterest.Clear();

            var    level = GetMapViewLevel(station);
            float  range = GetDetectionRange(station);
            int    seed  = Math.Abs(station.stationName.GetHashCode());
            var    rng   = new System.Random(seed + station.tick / RegenInterval);

            int count = level switch
            {
                MapViewLevel.System   => rng.Next(1, 4),   // 1-3
                MapViewLevel.Sector   => rng.Next(4, 9),   // 4-8
                MapViewLevel.Quadrant => rng.Next(8, 16),  // 8-15
                MapViewLevel.Galaxy   => rng.Next(15, 31), // 15-30
                _                    => rng.Next(1, 4),
            };

            string[] poiTypes = level switch
            {
                MapViewLevel.System   => new[] { "Asteroid" },
                MapViewLevel.Sector   => new[] { "Asteroid", "TradePost", "AbandonedStation" },
                MapViewLevel.Quadrant => new[] { "Asteroid", "TradePost", "AbandonedStation", "NebulaPocket" },
                MapViewLevel.Galaxy   => new[] { "Asteroid", "TradePost", "AbandonedStation", "NebulaPocket" },
                _                    => new[] { "Asteroid" },
            };

            for (int i = 0; i < count; i++)
            {
                string type  = poiTypes[rng.Next(poiTypes.Length)];
                float  angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float  dist  = (float)(rng.NextDouble() * range);
                float  x     = (float)Math.Cos(angle) * dist;
                float  y     = (float)Math.Sin(angle) * dist;
                int    pSeed = rng.Next(int.MaxValue);
                string name  = GenerateName(type, i, rng);

                var poi = PointOfInterest.Create(type, name, x, y, pSeed);
                poi.discovered = dist <= range;

                // Assign resource yield for asteroids.
                if (type == "Asteroid")
                {
                    int oreAmt  = rng.Next(20, 120);
                    int iceAmt  = rng.Next(10, 80);
                    poi.resourceYield["item.parts"] = oreAmt;
                    poi.resourceYield["item.ice"]   = iceAmt;
                }

                if (poi.discovered)
                    station.pointsOfInterest[poi.uid] = poi;
            }
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
    }
}
