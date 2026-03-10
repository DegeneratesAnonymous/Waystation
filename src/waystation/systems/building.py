"""
Building System — manages player-ordered construction projects.

Lifecycle of a build order:
  1. Player calls place_order() → BuildOrderInstance created, status="pending"
  2. System finds a free engineer and marks them as assigned (status→"hauling")
  3. Each tick, the engineer carries a portion of required resources from the
     station's stores to the build site (deducted from station, added to
     materials_delivered).  NPC inventory_capacity limits the load per trip,
     and _HAUL_RATE_PER_TICK further caps how much moves in one tick — the
     effective amount per tick is min(inventory_capacity, _HAUL_RATE_PER_TICK,
     remaining_needed, available_on_station).
  4. When all materials are delivered (status→"constructing") the engineer
     works construction cycles until progress reaches 1.0.
  5. On completion the system spawns the new ModuleInstance and logs the event.

Design notes:
- BuildingSystem ticks *before* JobSystem so engineers have build_order_uid
  set when JobSystem assigns jobs.
- Only class.engineering NPCs are used as builders.
- Resource deduction happens incrementally (per-tick) to simulate hauling.
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import (
        BuildOrderInstance, NPCInstance, StationState
    )

log = logging.getLogger(__name__)

# Maximum resource units moved from station to build site per tick.
# Together with NPCInstance.inventory_capacity this determines haul speed:
# per-tick transfer = min(_HAUL_RATE_PER_TICK, npc.inventory_capacity, ...)
_HAUL_RATE_PER_TICK = 5.0

# Fallback build time (ticks) when a buildable definition cannot be found at
# construction time.  Should not occur in normal play; acts as a safe sentinel.
_DEFAULT_BUILD_TIME_TICKS = 50

# Floating-point tolerance for "has this resource been fully delivered?"
_RESOURCE_EPSILON = 0.01


class BuildingSystem:
    """Manages placement and construction of player build orders."""

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

    # ------------------------------------------------------------------
    # Public interface
    # ------------------------------------------------------------------

    def available_buildables(self, station: "StationState") -> list:
        """Return all BuildableDefinitions the player can currently unlock."""
        result = []
        for defn in self.registry.buildables.values():
            if all(station.has_tag(t) for t in defn.required_tags):
                result.append(defn)
        return sorted(result, key=lambda d: d.display_name)

    def can_afford(self, buildable_id: str, station: "StationState") -> bool:
        """Check whether the station has enough resources to start a build."""
        defn = self.registry.buildables.get(buildable_id)
        if defn is None:
            return False
        for res, needed in defn.cost.items():
            if station.get_resource(res) < needed:
                return False
        return True

    def place_order(self, buildable_id: str,
                    station: "StationState") -> "BuildOrderInstance | None":
        """
        Queue a new construction order.  Returns the order on success,
        or None if the buildable is unknown.  Resources are NOT deducted
        yet — hauling happens incrementally during ticks.
        """
        from waystation.models.instances import BuildOrderInstance

        defn = self.registry.buildables.get(buildable_id)
        if defn is None:
            log.warning("place_order: unknown buildable '%s'", buildable_id)
            return None

        order = BuildOrderInstance.create(buildable_id, defn.cost)
        station.add_build_order(order)
        station.log_event(
            f"Blueprint placed: {defn.display_name}  "
            f"(cost: {self._cost_str(defn.cost)})"
        )
        return order

    # ------------------------------------------------------------------
    # Tick
    # ------------------------------------------------------------------

    def tick(self, station: "StationState") -> None:
        """Advance all active build orders by one tick."""
        for order in list(station.build_orders.values()):
            if order.status == "complete":
                continue
            if order.status == "pending":
                self._tick_pending(order, station)
            elif order.status == "hauling":
                self._tick_hauling(order, station)
            elif order.status == "constructing":
                self._tick_constructing(order, station)

    # ------------------------------------------------------------------
    # Internal — per-status tick handlers
    # ------------------------------------------------------------------

    def _tick_pending(self, order: "BuildOrderInstance",
                      station: "StationState") -> None:
        """Try to find a free engineer and start hauling."""
        # Check the station has enough resources to begin delivery
        if not self._station_can_supply(order, station):
            return

        engineer = self._find_free_engineer(station)
        if engineer is None:
            return

        # Assign engineer to this order
        engineer.build_order_uid = order.uid
        order.assigned_npc_uid = engineer.uid
        order.status = "hauling"

        defn = self.registry.buildables.get(order.buildable_id)
        name = defn.display_name if defn else order.buildable_id
        station.log_event(
            f"{engineer.name} begins hauling materials for {name}."
        )

    def _tick_hauling(self, order: "BuildOrderInstance",
                      station: "StationState") -> None:
        """Carry a portion of required resources from station to build site."""
        engineer = station.npcs.get(order.assigned_npc_uid or "")
        if engineer is None or engineer.build_order_uid != order.uid:
            # Engineer was removed or reassigned externally — reset
            order.assigned_npc_uid = None
            order.status = "pending"
            return

        # All materials delivered?
        if order.materials_fulfilled():
            order.status = "constructing"
            defn = self.registry.buildables.get(order.buildable_id)
            name = defn.display_name if defn else order.buildable_id
            station.log_event(
                f"Materials delivered. {engineer.name} begins construction "
                f"of {name}."
            )
            return

        # Carry as many units as the engineer's inventory allows this tick
        remaining_capacity = engineer.inventory_capacity
        for res, total_needed in order.materials_needed.items():
            if remaining_capacity <= 0:
                break
            already_delivered = order.materials_delivered.get(res, 0.0)
            still_needed = total_needed - already_delivered
            if still_needed <= _RESOURCE_EPSILON:
                continue

            # Limited by what's on the station and what the engineer can carry
            available_on_station = station.get_resource(res)
            haul_amount = min(
                still_needed,
                remaining_capacity,
                available_on_station,
                _HAUL_RATE_PER_TICK,
            )
            if haul_amount <= 0:
                continue

            station.modify_resource(res, -haul_amount)
            order.materials_delivered[res] = already_delivered + haul_amount
            remaining_capacity -= haul_amount

    def _tick_constructing(self, order: "BuildOrderInstance",
                           station: "StationState") -> None:
        """Advance construction progress."""
        engineer = station.npcs.get(order.assigned_npc_uid or "")
        if engineer is None or engineer.build_order_uid != order.uid:
            # Re-assign to a free engineer if possible
            order.assigned_npc_uid = None
            new_eng = self._find_free_engineer(station)
            if new_eng:
                new_eng.build_order_uid = order.uid
                order.assigned_npc_uid = new_eng.uid
            return

        # Scale by repair skill — range 0.5× to 1.5×
        repair_skill = engineer.skills.get("repair", 5)
        scale = 0.5 + repair_skill / 10.0

        # Derive increment from the buildable's build_time_ticks so that a
        # skilled engineer finishes faster and a slow one takes longer, but the
        # baseline always reflects the per-buildable design intent.
        defn = self.registry.buildables.get(order.buildable_id)
        build_time = defn.build_time_ticks if defn is not None else _DEFAULT_BUILD_TIME_TICKS
        increment = (1.0 / build_time) * scale
        order.progress = min(1.0, order.progress + increment)

        if order.progress >= 1.0:
            self._complete_order(order, engineer, station)

    def _complete_order(self, order: "BuildOrderInstance",
                        engineer: "NPCInstance",
                        station: "StationState") -> None:
        """Finalise a completed build order and spawn the resulting module."""
        from waystation.models.instances import ModuleInstance

        order.status = "complete"
        engineer.build_order_uid = None

        defn = self.registry.buildables.get(order.buildable_id)
        if defn is None:
            return

        if defn.produces_module_id:
            mod_defn = self.registry.modules.get(defn.produces_module_id)
            if mod_defn:
                new_module = ModuleInstance.create(
                    definition_id=defn.produces_module_id,
                    display_name=mod_defn.display_name,
                    category=mod_defn.category,
                )
                station.add_module(new_module)
                station.log_event(
                    f"{engineer.name} completed {defn.display_name}. "
                    f"New module online: {mod_defn.display_name}."
                )
            else:
                station.log_event(
                    f"{engineer.name} completed {defn.display_name}."
                )
        else:
            station.log_event(
                f"{engineer.name} completed {defn.display_name}."
            )

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _station_can_supply(self, order: "BuildOrderInstance",
                            station: "StationState") -> bool:
        """True if station has enough resources to cover the full build cost."""
        for res, total_needed in order.materials_needed.items():
            already = order.materials_delivered.get(res, 0.0)
            if station.get_resource(res) < (total_needed - already):
                return False
        return True

    def _find_free_engineer(self,
                            station: "StationState") -> "NPCInstance | None":
        """Return the first crew engineer not already on a build order."""
        for npc in station.npcs.values():
            if (npc.is_crew() and
                    npc.class_id == "class.engineering" and
                    npc.build_order_uid is None):
                return npc
        return None

    @staticmethod
    def _cost_str(cost: dict) -> str:
        return ", ".join(f"{int(v)} {r}" for r, v in cost.items())
