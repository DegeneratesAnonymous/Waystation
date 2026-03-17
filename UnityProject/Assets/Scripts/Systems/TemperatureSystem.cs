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
//         toward the average. Duct-to-temperature integration is a stub (TODO).
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

        public TemperatureSystem(ContentRegistry registry) => _registry = registry;

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

        private void ProcessVents(StationState station)
        {
            // Passive circulation: equalise temperature between a vent's room and
            // adjacent connected room/tile.  Duct-to-temperature integration: TODO.
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.vent") continue;
                if (!f.isEnergised) continue;

                float myTemp = GetEffectiveTemperature(station, f.tileCol, f.tileRow);
                var offsets  = new[] { (0, 1), (0, -1), (1, 0), (-1, 0) };
                foreach (var (dc, dr) in offsets)
                {
                    float neighbour = GetEffectiveTemperature(station,
                                                               f.tileCol + dc,
                                                               f.tileRow + dr);
                    float diff = neighbour - myTemp;
                    if (Mathf.Abs(diff) < 0.1f) continue;
                    float move = Mathf.Sign(diff) * Mathf.Min(VentEqualiseRate, Mathf.Abs(diff) * 0.5f);
                    PropagateHeat(station, f.tileCol,      f.tileRow,      move);
                    PropagateHeat(station, f.tileCol + dc, f.tileRow + dr, -move);
                }
            }
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
