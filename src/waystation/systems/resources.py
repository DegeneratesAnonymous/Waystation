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
        self._apply_module_effects(station)
        self._check_thresholds(station)

    def _apply_module_effects(self, station: "StationState") -> None:
        """Each active module applies its resource_effects per tick."""
        for module in station.modules.values():
            if not module.active or module.damage >= 1.0:
                continue
            definition = self.registry.modules.get(module.definition_id)
            if definition is None:
                continue
            # Efficiency scales with module damage
            efficiency = 1.0 - module.damage
            for resource, delta in definition.resource_effects.items():
                effective_delta = delta * efficiency
                current = station.get_resource(resource)
                cap = SOFT_CAPS.get(resource, float("inf"))
                # Only apply positive deltas up to cap
                if effective_delta > 0:
                    effective_delta = min(effective_delta, cap - current)
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
