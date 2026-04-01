// Time System — manages the in-game day/night cycle.
// TICKS_PER_DAY defines how long a full day is.
// Day phase drives NPC job assignment (work vs rest).
using Waystation.Models;

namespace Waystation.Systems
{
    public static class TimeSystem
    {
        public const int TicksPerHour     = 4;             // 4 × 15 min = 1 hour
        public const int TicksPerDay      = 24 * TicksPerHour; // 24-hour day (96 ticks)
        public const int DayStartTick     = 6  * TicksPerHour;  // 06:00
        public const int DayEndTick       = 18 * TicksPerHour;  // 18:00

        public static int TickOfDay(StationState station) => station.tick % TicksPerDay;
        public static int DayNumber(StationState station) => station.tick / TicksPerDay + 1;
        public static int HourOfDay(StationState station) => TickOfDay(station) / TicksPerHour;
        public static int MinuteOfHour(StationState station) => (TickOfDay(station) % TicksPerHour) * 15;

        public static string TimeLabel(StationState station)
            => $"Day {DayNumber(station)}  {HourOfDay(station):D2}:{MinuteOfHour(station):D2}";

        public static bool IsDayPhase(StationState station)
        {
            int t = TickOfDay(station);
            return t >= DayStartTick && t < DayEndTick;
        }

        public static bool IsNightPhase(StationState station) => !IsDayPhase(station);

        /// <summary>0.0 = full night, 1.0 = full day — for ambient lighting.</summary>
        public static float SkyAlpha(StationState station)
        {
            int tod = TickOfDay(station);
            if (tod >= DayStartTick && tod < DayEndTick)
            {
                float mid   = (DayStartTick + DayEndTick) / 2f;
                float width = (DayEndTick - DayStartTick) / 2f;
                return UnityEngine.Mathf.Max(0.4f, 1f - UnityEngine.Mathf.Abs(tod - mid) / width * 0.4f);
            }
            return 0.25f;
        }
    }
}
