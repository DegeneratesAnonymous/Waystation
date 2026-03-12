"""
Building System — manages build foundations and Engineer NPC construction tasks.

Workflow:
  1. Player places a Foundation via the Build Menu.
  2. An idle Engineer NPC detects the pending foundation and hauls the
     required materials from station cargo holds to the build site.
  3. Once all materials are present the engineer spends build_time_ticks
     constructing the item.
  4. On completion the tile map is updated and the foundation is marked
     "complete".

Health & functionality rules (applied once built):
  100–75 % HP  →  1.0  (full function)
  75–50 % HP   →  linearly degraded
  <50 % HP     →  0.0  (non-functional; still pulls power if applicable)
  0 % HP       →  0.0  (destroyed)
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import (
        FoundationInstance,
        NPCInstance,
        StationState,
    )
    from waystation.models.templates import BuildableDefinition

log = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

#: Fallback build time when a definition omits build_time_ticks.
_DEFAULT_BUILD_TIME_TICKS: int = 50

#: Job ID used while an engineer is actively hauling or constructing.
_BUILD_JOB_ID: str = "job.build"

#: Engineer class ID used for idle-engineer detection.
_ENGINEER_CLASS: str = "class.engineering"


# ---------------------------------------------------------------------------
# BuildingSystem
# ---------------------------------------------------------------------------

class BuildingSystem:
    """
    Manages all active FoundationInstances and coordinates Engineer NPC
    construction tasks.
    """

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def place_foundation(
        self,
        station: "StationState",
        buildable_id: str,
        tile_position: tuple[int, int],
    ) -> "FoundationInstance | None":
        """
        Place a new Foundation on the tile map at *tile_position*.

        Returns the created :class:`FoundationInstance`, or ``None`` if
        *buildable_id* is unknown.
        """
        from waystation.models.instances import FoundationInstance

        defn = self.registry.buildables.get(buildable_id)
        if defn is None:
            log.error("Unknown buildable '%s'", buildable_id)
            return None

        foundation = FoundationInstance.create(
            buildable_id=buildable_id,
            tile_position=tile_position,
            max_health=defn.max_health,
            quality=defn.build_quality,
        )
        station.foundations[foundation.uid] = foundation
        station.log_event(
            f"Foundation placed: {defn.display_name} at tile {tile_position}."
        )
        log.debug("Foundation %s placed at %s", foundation.uid, tile_position)
        return foundation

    def cancel_foundation(
        self,
        station: "StationState",
        foundation_uid: str,
        refund: bool = True,
    ) -> bool:
        """
        Cancel a pending foundation and optionally refund hauled materials.

        Returns True if the foundation was found and removed.
        """
        foundation = station.foundations.get(foundation_uid)
        if foundation is None:
            return False
        if foundation.status == "complete":
            return False

        defn = self.registry.buildables.get(foundation.buildable_id)

        # Refund any materials already hauled
        if refund and defn is not None:
            for item_id, qty in foundation.hauled_materials.items():
                # Return to first available cargo hold
                for mod in station.modules.values():
                    if mod.cargo_settings is None and mod.inventory is not None:
                        mod.inventory[item_id] = mod.inventory.get(item_id, 0) + qty
                        break

        # Release assigned NPC
        if foundation.assigned_npc_uid:
            npc = station.npcs.get(foundation.assigned_npc_uid)
            if npc and npc.current_job_id == _BUILD_JOB_ID:
                npc.current_job_id = None

        del station.foundations[foundation_uid]
        name = defn.display_name if defn else foundation.buildable_id
        station.log_event(f"Foundation cancelled: {name}.")
        return True

    def tick(self, station: "StationState") -> None:
        """
        Advance all active foundations by one tick.

        * Foundations in **awaiting_haul** have materials gathered.
        * Foundations in **constructing** have their progress advanced.
        """
        pending = [
            f for f in station.foundations.values()
            if f.status in ("awaiting_haul", "constructing")
        ]
        if not pending:
            return

        idle_engineers = self._find_idle_engineers(station)

        for foundation in pending:
            defn = self.registry.buildables.get(foundation.buildable_id)
            if defn is None:
                continue
            if foundation.status == "awaiting_haul":
                self._tick_awaiting_haul(foundation, defn, station, idle_engineers)
            elif foundation.status == "constructing":
                self._tick_constructing(foundation, defn, station, idle_engineers)

    # ------------------------------------------------------------------
    # Internals
    # ------------------------------------------------------------------

    def _find_idle_engineers(
        self, station: "StationState"
    ) -> list["NPCInstance"]:
        """
        Return player-owned Engineer crew who are available for build tasks
        (idle, resting, eating, or already on a build job for this foundation).
        """
        idle_jobs = {None, "job.rest", "job.eat", _BUILD_JOB_ID}
        return [
            npc
            for npc in station.npcs.values()
            if (
                npc.is_crew()
                and npc.class_id == _ENGINEER_CLASS
                and (npc.owner_id is None or npc.owner_id == "player")
                and npc.current_job_id in idle_jobs
            )
        ]

    def _tick_awaiting_haul(
        self,
        foundation: "FoundationInstance",
        defn: "BuildableDefinition",
        station: "StationState",
        idle_engineers: list["NPCInstance"],
    ) -> None:
        """Attempt to haul missing materials; transition to constructing when ready."""
        required = defn.required_materials

        # No materials required — go straight to constructing
        if not required:
            foundation.status = "constructing"
            return

        # Already have everything
        if foundation.materials_complete(required):
            foundation.status = "constructing"
            return

        # Attempt to gather what's still needed from station cargo holds
        hauled_something = False
        for item_id, qty_needed in required.items():
            already = foundation.hauled_materials.get(item_id, 0)
            still_needed = qty_needed - already
            if still_needed <= 0:
                continue

            available = sum(
                mod.inventory.get(item_id, 0)
                for mod in station.modules.values()
            )
            if available < still_needed:
                continue  # Not enough in stock; wait

            # Deduct from cargo holds
            remaining = still_needed
            for mod in station.modules.values():
                have = mod.inventory.get(item_id, 0)
                if have <= 0:
                    continue
                used = min(have, remaining)
                mod.inventory[item_id] -= used
                if mod.inventory[item_id] == 0:
                    del mod.inventory[item_id]
                remaining -= used
                if remaining <= 0:
                    break

            foundation.hauled_materials[item_id] = qty_needed
            hauled_something = True

        # Assign an engineer to haul (visual / log feedback only)
        if hauled_something and idle_engineers and foundation.assigned_npc_uid is None:
            eng = idle_engineers[0]
            eng.current_job_id = _BUILD_JOB_ID
            foundation.assigned_npc_uid = eng.uid
            station.log_event(
                f"{eng.name} hauls materials for {defn.display_name}."
            )

        if foundation.materials_complete(required):
            foundation.status = "constructing"

    def _tick_constructing(
        self,
        foundation: "FoundationInstance",
        defn: "BuildableDefinition",
        station: "StationState",
        idle_engineers: list["NPCInstance"],
    ) -> None:
        """Advance build progress; complete when progress reaches 1.0."""
        # Ensure an engineer is assigned
        assigned: "NPCInstance | None" = None
        if foundation.assigned_npc_uid:
            assigned = station.npcs.get(foundation.assigned_npc_uid)

        if assigned is None and idle_engineers:
            assigned = idle_engineers[0]
            foundation.assigned_npc_uid = assigned.uid

        if assigned is None:
            return  # Nobody to build — wait

        # Mark the engineer as actively building
        assigned.current_job_id = _BUILD_JOB_ID

        # Progress rate: 1 / build_time_ticks, scaled by engineer skill
        build_time = defn.build_time_ticks if defn.build_time_ticks > 0 else _DEFAULT_BUILD_TIME_TICKS
        skill_level = assigned.skills.get("technical", 5)
        skill_scale = 0.5 + skill_level / 10.0   # 0.5× at skill 0, 1.5× at skill 10
        increment = (1.0 / build_time) * skill_scale

        foundation.build_progress = min(1.0, foundation.build_progress + increment)

        if foundation.build_progress >= 1.0:
            self._complete_foundation(foundation, defn, station, assigned)

    def _complete_foundation(
        self,
        foundation: "FoundationInstance",
        defn: "BuildableDefinition",
        station: "StationState",
        npc: "NPCInstance | None",
    ) -> None:
        """
        Finalise construction: apply tile map changes and mark complete.

        The tile change applied depends on the buildable ID:
          buildable.wall          → solid wall tile
          buildable.floor         → floor tile
          buildable.door          → floor tile + N-edge wall with door
          buildable.storage_crate → floor tile (object placed on floor)
        """
        col, row = foundation.tile_position
        tm = station.tile_map
        bid = foundation.buildable_id

        if bid == "buildable.wall":
            tm.set_wall(col, row)
        elif bid == "buildable.floor":
            tm.set_floor(col, row)
        elif bid == "buildable.door":
            # Place a floor tile with a doorway on the North edge
            tm.set_floor(col, row)
            tm.add_wall_segment(col, row, "N")
            tm.toggle_door(col, row, "N")
        else:
            # Generic objects sit on a floor tile
            tm.set_floor(col, row)

        foundation.status = "complete"
        foundation.build_progress = 1.0

        # Release the engineer
        if npc is not None:
            npc.current_job_id = None
            foundation.assigned_npc_uid = None

        station.log_event(
            f"{defn.display_name} construction complete at {foundation.tile_position} "
            f"(quality: {foundation.quality})."
        )
        log.info(
            "Foundation %s (%s) completed at %s by %s",
            foundation.uid,
            defn.display_name,
            foundation.tile_position,
            npc.name if npc else "unknown",
        )
