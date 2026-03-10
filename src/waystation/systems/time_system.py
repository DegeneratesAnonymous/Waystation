"""
Time System — manages the in-game day/night cycle.

TICKS_PER_DAY defines how long a full day is.
Day phase drives NPC job assignment (work vs rest).
"""

from __future__ import annotations
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.models.instances import StationState

TICKS_PER_DAY = 24          # one full day cycle
DAY_START_TICK = 6          # 6:00 — day shift begins
DAY_END_TICK   = 18         # 18:00 — night shift begins


def tick_of_day(station: "StationState") -> int:
    """Return the current tick within the current day (0–23)."""
    return station.tick % TICKS_PER_DAY


def day_number(station: "StationState") -> int:
    return station.tick // TICKS_PER_DAY + 1


def hour_of_day(station: "StationState") -> int:
    """Map tick-of-day to a 24-hour clock hour."""
    return tick_of_day(station)


def time_label(station: "StationState") -> str:
    hour = hour_of_day(station)
    return f"Day {day_number(station)}  {hour:02d}:00"


def is_day_phase(station: "StationState") -> bool:
    return DAY_START_TICK <= tick_of_day(station) < DAY_END_TICK


def is_night_phase(station: "StationState") -> bool:
    return not is_day_phase(station)


def sky_alpha(station: "StationState") -> float:
    """0.0 = full night, 1.0 = full day — for ambient lighting."""
    tod = tick_of_day(station)
    if DAY_START_TICK <= tod < DAY_END_TICK:
        # Daytime: ramp up at dawn, full mid-day, ramp down at dusk
        mid   = (DAY_START_TICK + DAY_END_TICK) / 2
        width = (DAY_END_TICK - DAY_START_TICK) / 2
        return max(0.4, 1.0 - abs(tod - mid) / width * 0.4)
    return 0.25    # night: dim but not pitch black
