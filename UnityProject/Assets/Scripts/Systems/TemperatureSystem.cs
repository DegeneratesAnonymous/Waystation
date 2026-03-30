// TemperatureSystem — manages per-room and per-tile temperature values.
// Heaters raise temperature toward their target; Coolers lower it.
// Propagates heat within sealed rooms using PropagateHeat().
// Hydroponics Planter Tiles read temperature from this system each tick.
//
// Heater: raises room temperature at HeatingRate (1 °C/tick) up to TargetTemperature.
//         Dynamic power draw: MaxWatts at full delta, StandbyWatts when at target.
// Cooler: lowers room temperature at CoolingRate (1 °C/tick) down to TargetTemperature.
//         Same dynamic power model as Heater.
// Vent:   passive circulation — equalises temperature between adjacent rooms
//         toward the average through connected duct networks.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class TemperatureSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public const float DefaultTemperature = 20f;   // °C when no explicit value set
        private const float HeatingRate       = 1f;    // °C per tick at full output
        private const float CoolingRate       = 1f;    // °C per tick at full output
        private const float VentEqualiseRate  = 0.5f;  // °C pulled toward neighbour average

        // ── Dynamic power constants (Heater) ──────────────────────────────────
        public const float HeaterMaxWatts     = 150f;
        public const float HeaterStandbyWatts =   5f;
        // ── Dynamic power constants (Cooler) ──────────────────────────────────
        public const float CoolerMaxWatts     = 200f;
        public const float CoolerStandbyWatts =   5f;

        private readonly ContentRegistry _registry;
        private readonly NetworkSystem _networks;

        public TemperatureSystem(ContentRegistry registry, NetworkSystem networks = null)
        {
            _registry = registry;
            _networks = networks ?? new NetworkSystem(registry);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            ProcessHeaters(station);
            ProcessCoolers(station);
            ProcessVents(station);
            UpdatePlanterTemperatures(station);
        }

        /// <summary>
        /// Propagate a temperature delta (positive = heat, negative = cool) to the
        /// room or tile at the given position. When inside a sealed room, the entire
        /// room's temperature changes; otherwise the individual tile changes.
        /// </summary>
        public void PropagateHeat(StationState station, int col, int row, float deltaTemp)
        {
            string tileKey = $"{col}_{row}";
            if (station.tileToRoomKey.TryGetValue(tileKey, out var roomKey))
            {
                float current = GetRoomTemperature(station, roomKey);
                station.roomTemperatures[roomKey] = current + deltaTemp;
            }
            else
            {
                float current = GetTileTemperature(station, col, row);
                station.tileTemperatures[tileKey] = current + deltaTemp;
            }
        }

        public static float GetRoomTemperature(StationState station, string roomKey)
        {
            if (roomKey != null && station.roomTemperatures.TryGetValue(roomKey, out float t))
                return t;
            return DefaultTemperature;
        }

        public static float GetTileTemperature(StationState station, int col, int row)
        {
            string key = $"{col}_{row}";
            if (station.tileTemperatures.TryGetValue(key, out float t))
                return t;
            return DefaultTemperature;
        }

        /// <summary>
        /// Returns the effective temperature at (col, row): room temperature if the
        /// tile is inside a sealed room, per-tile temperature otherwise.
        /// </summary>
        public static float GetEffectiveTemperature(StationState station, int col, int row)
        {
            string tileKey = $"{col}_{row}";
            if (station.tileToRoomKey.TryGetValue(tileKey, out var roomKey))
                return GetRoomTemperature(station, roomKey);
            return GetTileTemperature(station, col, row);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ProcessHeaters(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.heater") continue;
                if (!f.isEnergised) continue;

                float current = GetEffectiveTemperature(station, f.tileCol, f.tileRow);
                float target  = f.targetTemperature;

                if (current >= target) continue;  // already at or above target

                float delta = Mathf.Min(HeatingRate, target - current);
                PropagateHeat(station, f.tileCol, f.tileRow, delta);
            }
        }

        private void ProcessCoolers(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.cooler") continue;
                if (!f.isEnergised) continue;

                float current = GetEffectiveTemperature(station, f.tileCol, f.tileRow);
                float target  = f.targetTemperature;

                if (current <= target) continue;  // already at or below target

                float delta = Mathf.Max(-CoolingRate, target - current);
                PropagateHeat(station, f.tileCol, f.tileRow, delta);
            }
        }

        private static readonly (int dc, int dr)[] VentOffsets =
            { (0, 1), (0, -1), (1, 0), (-1, 0) };

        private void ProcessVents(StationState station)
        {
            // Passive circulation: equalise temperature between adjacent thermal nodes,
            // but only when vent-adjacent tiles are connected through duct networks.
            var ductsByTile = BuildDuctLookup(station);
            var ventPairCounts = new Dictionary<(string a, string b), int>();

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.vent") continue;
                if (!f.isEnergised) continue;
                if (f.status != "complete") continue;

                foreach (var (dc, dr) in VentOffsets)
                {
                    int nc = f.tileCol + dc;
                    int nr = f.tileRow + dr;

                    if (!AreTilesDuctConnected(station, ductsByTile, f.tileCol, f.tileRow, nc, nr))
                        continue;

                    string a = GetThermalNodeKey(station, f.tileCol, f.tileRow);
                    string b = GetThermalNodeKey(station, nc, nr);
                    if (a == b) continue;
                    if (string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
                    var key = (a, b);
                    ventPairCounts[key] = ventPairCounts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            foreach (var kv in ventPairCounts)
            {
                float aTemp = GetThermalNodeTemperature(station, kv.Key.a);
                float bTemp = GetThermalNodeTemperature(station, kv.Key.b);
                float diff = bTemp - aTemp;
                if (Mathf.Abs(diff) < 0.1f) continue;

                float maxRate = VentEqualiseRate * kv.Value;
                float move = Mathf.Sign(diff) * Mathf.Min(maxRate, Mathf.Abs(diff) * 0.5f);
                ApplyThermalNodeDelta(station, kv.Key.a, move);
                ApplyThermalNodeDelta(station, kv.Key.b, -move);
            }
        }

        private Dictionary<(int, int), List<FoundationInstance>> BuildDuctLookup(StationState station)
        {
            var lookup = new Dictionary<(int, int), List<FoundationInstance>>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (!IsDuctFoundation(f)) continue;
                var key = (f.tileCol, f.tileRow);
                if (!lookup.TryGetValue(key, out var list))
                    lookup[key] = list = new List<FoundationInstance>();
                list.Add(f);
            }
            return lookup;
        }

        private bool IsDuctFoundation(FoundationInstance f)
        {
            if (!(_registry?.Buildables.TryGetValue(f.buildableId, out var def) == true)) return false;
            return def.networkType == "duct";
        }

        private bool AreTilesDuctConnected(
            StationState station,
            Dictionary<(int, int), List<FoundationInstance>> ductsByTile,
            int aCol, int aRow, int bCol, int bRow)
        {
            if (!ductsByTile.TryGetValue((aCol, aRow), out var aDucts)) return false;
            if (!ductsByTile.TryGetValue((bCol, bRow), out var bDucts)) return false;

            foreach (var a in aDucts)
            {
                var aNet = _networks.GetNetwork(station, a.uid);
                if (aNet == null) continue;
                foreach (var b in bDucts)
                {
                    var bNet = _networks.GetNetwork(station, b.uid);
                    if (bNet == null) continue;
                    if (aNet.uid == bNet.uid) return true;
                }
            }
            return false;
        }

        private static string GetThermalNodeKey(StationState station, int col, int row)
        {
            string tileKey = $"{col}_{row}";
            if (station.tileToRoomKey.TryGetValue(tileKey, out var roomKey))
                return $"room:{roomKey}";
            return $"tile:{tileKey}";
        }

        private static float GetThermalNodeTemperature(StationState station, string nodeKey)
        {
            if (nodeKey.StartsWith("room:"))
                return GetRoomTemperature(station, nodeKey.Substring(5));
            var (col, row) = ParseTileKey(nodeKey);
            return GetTileTemperature(station, col, row);
        }

        private static void ApplyThermalNodeDelta(StationState station, string nodeKey, float delta)
        {
            if (nodeKey.StartsWith("room:"))
            {
                string roomKey = nodeKey.Substring(5);
                station.roomTemperatures[roomKey] = GetRoomTemperature(station, roomKey) + delta;
                return;
            }

            var (col, row) = ParseTileKey(nodeKey);
            string tileKey = $"{col}_{row}";
            station.tileTemperatures[tileKey] = GetTileTemperature(station, col, row) + delta;
        }

        private static (int col, int row) ParseTileKey(string nodeKey)
        {
            string tile = nodeKey.Substring(5);
            int split = tile.IndexOf('_');
            int col = int.Parse(tile.Substring(0, split));
            int row = int.Parse(tile.Substring(split + 1));
            return (col, row);
        }

        /// Update tileTemperature on every Hydroponics Planter Tile from the system.
        private static void UpdatePlanterTemperatures(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;
                f.tileTemperature = GetEffectiveTemperature(station, f.tileCol, f.tileRow);
            }
        }

        /// <summary>
        /// Compute the dynamic power draw of a Heater or Cooler based on current
        /// temperature delta from its target.  Returns MaxWatts when at full output,
        /// StandbyWatts when already at target.
        /// </summary>
        public static float GetClimateDevicePowerDraw(FoundationInstance f, StationState station)
        {
            float current = GetEffectiveTemperature(station, f.tileCol, f.tileRow);
            float delta   = Mathf.Abs(current - f.targetTemperature);
            float maxDelta = 10f;  // delta at which MaxWatts applies

            if (f.buildableId == "buildable.heater")
            {
                if (delta <= 0f) return HeaterStandbyWatts;
                float ratio = Mathf.Clamp01(delta / maxDelta);
                return Mathf.Lerp(HeaterStandbyWatts, HeaterMaxWatts, ratio);
            }
            if (f.buildableId == "buildable.cooler")
            {
                if (delta <= 0f) return CoolerStandbyWatts;
                float ratio = Mathf.Clamp01(delta / maxDelta);
                return Mathf.Lerp(CoolerStandbyWatts, CoolerMaxWatts, ratio);
            }
            return 0f;
        }
    }
}
