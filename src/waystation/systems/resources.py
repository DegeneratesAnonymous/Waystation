"""
Resource System — tracks per-tick resource production and consumption.

Resources are simple floats on StationState. This system applies
module-level deltas and warns when resources are critically low.
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import StationState

log = logging.getLogger(__name__)


# Thresholds that trigger station log warnings
CRITICAL_THRESHOLDS: dict[str, float] = {
    "food":    20.0,
    "power":   15.0,
    "oxygen":  10.0,
    "parts":    5.0,
    "credits": 50.0,
    "ice":     30.0,
}

# Soft caps — resources don't grow beyond these passively
SOFT_CAPS: dict[str, float] = {
    "food":    500.0,
    "power":   500.0,
    "oxygen":  500.0,
    "parts":   200.0,
    "credits": 100_000.0,
    "ice":     500.0,
}


class ResourceSystem:

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

    def tick(self, station: "StationState") -> None:
        """Apply all module resource deltas and check thresholds."""
        morale_mod = self._morale_modifier(station)
        self._apply_module_effects(station, morale_mod)
        self._check_thresholds(station)

    def morale_modifier(self, station: "StationState") -> float:
        """
        Public accessor for the production efficiency multiplier based on crew mood.

        Ranges from 0.70 (miserable crew) to 1.15 (content crew).
        Neutral mood (0.0) = 1.0 efficiency.
        """
        return self._morale_modifier(station)

    @staticmethod
    def _morale_modifier(station: "StationState") -> float:
        """
        Returns a production efficiency multiplier based on average crew mood.

        Ranges from 0.70 (miserable crew) to 1.15 (content crew).
        Neutral mood (0.0) = 1.0 efficiency.
        """
        crew = station.get_crew()
        if not crew:
            return 1.0
        avg_mood = sum(n.mood for n in crew) / len(crew)
        # Linear interpolation: mood -1 → 0.70, mood 0 → 1.0, mood 1 → 1.15
        if avg_mood >= 0:
            return 1.0 + avg_mood * 0.15
        return 1.0 + avg_mood * 0.30   # steeper penalty below neutral

    def _apply_module_effects(self, station: "StationState", morale_mod: float = 1.0) -> None:
        """Each active module applies its resource_effects per tick."""
        for module in station.modules.values():
            if not module.active or module.damage >= 1.0:
                continue
            definition = self.registry.modules.get(module.definition_id)
            if definition is None:
                continue
            # Base efficiency scales with module damage
            base_efficiency = 1.0 - module.damage
            for resource, delta in definition.resource_effects.items():
                if delta > 0:
                    # Production: boosted by both structural integrity and crew morale
                    efficiency = base_efficiency * morale_mod
                    effective_delta = delta * efficiency
                    current = station.get_resource(resource)
                    cap = SOFT_CAPS.get(resource, float("inf"))
                    effective_delta = min(effective_delta, cap - current)
                else:
                    # Consumption: only reduced by structural damage (morale doesn't affect it)
                    effective_delta = delta * base_efficiency
                station.modify_resource(resource, effective_delta)

    def _check_thresholds(self, station: "StationState") -> None:
        for resource, threshold in CRITICAL_THRESHOLDS.items():
            amount = station.get_resource(resource)
            if amount <= 0.0:
                station.log_event(f"CRITICAL: {resource.upper()} DEPLETED.")
                if resource == "oxygen":
                    station.set_tag("oxygen_emergency")
                elif resource == "power":
                    station.set_tag("power_failure")
            elif amount < threshold:
                if station.tick % 5 == 0:   # rate-limit repeated warnings
                    station.log_event(f"Warning: {resource} is low ({amount:.0f}).")

    def summary(self, station: "StationState") -> dict[str, str]:
        """Human-readable resource summary."""
        return {
            k: f"{v:.0f}" for k, v in sorted(station.resources.items())
        }
