// Time System — manages the in-game day/night cycle.
// TICKS_PER_DAY defines how long a full day is.
// Day phase drives NPC job assignment (work vs rest).
using Waystation.Models;

namespace Waystation.Systems
{
    public static class TimeSystem
    {
        public const int TicksPerDay  = 24;
        public const int DayStartTick = 6;   // 06:00 — day shift begins
        public const int DayEndTick   = 18;  // 18:00 — night shift begins

        public static int TickOfDay(StationState station) => station.tick % TicksPerDay;
        public static int DayNumber(StationState station) => station.tick / TicksPerDay + 1;
        public static int HourOfDay(StationState station) => TickOfDay(station);

        public static string TimeLabel(StationState station)
            => $"Day {DayNumber(station)}  {HourOfDay(station):D2}:00";

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
