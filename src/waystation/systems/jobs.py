"""
Job System — NPC maintenance and work loops.

Every crew member has a job they work at during the day phase.
At night they switch to rest/eat jobs.
Events can interrupt the current job temporarily.

Job cycle:
  1. NPC finishes or has no job → JobSystem assigns a new one
  2. NPC moves to the target module (handled visually by GameView)
  3. Per tick: apply resource_effects + need_effects + station_effects
  4. job_timer counts down; when 0 → job complete, pick next

Data model:
  JobDefinition (static, loaded from YAML)
  NPCInstance.current_job_id / job_module_uid / job_timer  (runtime)
"""

from __future__ import annotations

import logging
import random
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import NPCInstance, StationState, ModuleInstance
from waystation.systems.time_system import is_day_phase

log = logging.getLogger(__name__)

# Max workbenches allowed per room (enforced when no room type defines its own limit)
_DEFAULT_MAX_WORKBENCHES = 3
# Default workbench speed bonus fraction when room requirements are met
_DEFAULT_WORKBENCH_BONUS = 0.05


# ---------------------------------------------------------------------------
# Job Definition (static template)
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class JobDefinition:
    id: str
    display_name: str
    phase: str                              # "day" | "night" | "any"
    allowed_classes: tuple[str, ...]
    preferred_module_category: str
    fallback_module_category: str
    duration_ticks: int
    skill_used: str | None
    resource_effects: dict[str, float]     # applied to station per tick while active
    need_effects: dict[str, float]         # applied to NPC per tick while active
    station_effects: dict[str, Any]        # special effect tags
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "JobDefinition":
        return cls(
            id=raw["id"],
            display_name=raw.get("display_name", raw["id"]),
            phase=raw.get("phase", "any"),
            allowed_classes=tuple(raw.get("allowed_classes", [])),
            preferred_module_category=raw.get("preferred_module_category", "utility"),
            fallback_module_category=raw.get("fallback_module_category", "utility"),
            duration_ticks=int(raw.get("duration_ticks", 4)),
            skill_used=raw.get("skill_used"),
            resource_effects={k: float(v) for k, v in raw.get("resource_effects", {}).items()},
            need_effects={k: float(v) for k, v in raw.get("need_effects", {}).items()},
            station_effects=raw.get("station_effects", {}),
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# Job Registry (loaded separately from ContentRegistry for simplicity)
# ---------------------------------------------------------------------------

class JobRegistry:
    def __init__(self) -> None:
        self.jobs: dict[str, JobDefinition] = {}

    def load(self, data_root) -> None:
        from pathlib import Path
        import yaml
        folder = Path(data_root) / "jobs"
        if not folder.is_dir():
            log.warning("No jobs folder at %s", folder)
            return
        for f in sorted(folder.glob("*.yaml")):
            try:
                with open(f, "r", encoding="utf-8") as fh:
                    records = yaml.safe_load(fh) or []
                for raw in records:
                    if isinstance(raw, dict) and "id" in raw:
                        self.jobs[raw["id"]] = JobDefinition.from_raw(raw)
            except Exception as e:
                log.error("Error loading job file %s: %s", f, e)
        log.info("Loaded %d job definitions.", len(self.jobs))

    def get(self, job_id: str) -> JobDefinition | None:
        return self.jobs.get(job_id)


# ---------------------------------------------------------------------------
# Job System
# ---------------------------------------------------------------------------

# Jobs that are purely restorative — prefer these when needs are critical
_REST_JOB   = "job.rest"
_EAT_JOB    = "job.eat"

# Priority need thresholds that override normal job assignment
_FOOD_CRITICAL  = 0.25
_SLEEP_CRITICAL = 0.20

# Class → preferred day-phase jobs (in priority order)
_CLASS_DAY_JOBS: dict[str, list[str]] = {
    "class.security":    ["job.guard_post", "job.patrol", "job.contraband_inspection"],
    "class.engineering": ["job.build", "job.module_maintenance", "job.power_management",
                          "job.life_support", "job.haul", "job.refine", "job.craft"],
    "class.operations":  ["job.dock_control", "job.visitor_intake", "job.resource_management",
                          "job.haul"],
}


class JobSystem:

    def __init__(self, job_registry: JobRegistry) -> None:
        self.jobs = job_registry

    # ------------------------------------------------------------------
    # Tick update
    # ------------------------------------------------------------------

    def tick(self, station: "StationState") -> None:
        """Update all crew NPC job states."""
        for npc in station.npcs.values():
            if not npc.is_crew():
                continue
            # NPCs owned by a faction keep their ship-assigned jobs; skip auto-assignment
            if npc.owner_id and npc.owner_id != "player":
                continue
            self._tick_npc(npc, station)

    def _tick_npc(self, npc: "NPCInstance", station: "StationState") -> None:
        # If job interrupted by event, reassign immediately
        if npc.job_interrupted:
            npc.job_interrupted = False
            self._assign_job(npc, station)
            return

        # Needs override: hungry or exhausted → prioritise recovery
        if npc.needs.get("food", 1.0) < _FOOD_CRITICAL:
            if npc.current_job_id != _EAT_JOB:
                self._set_job(npc, _EAT_JOB, station)
                return
        if npc.needs.get("sleep", 1.0) < _SLEEP_CRITICAL:
            if npc.current_job_id != _REST_JOB:
                self._set_job(npc, _REST_JOB, station)
                return

        # Countdown current job
        if npc.current_job_id and npc.job_timer > 0:
            npc.job_timer -= 1
            self._apply_job_effects(npc, station)
            return

        # Job complete (or no job) → pick next
        self._assign_job(npc, station)

    def _assign_job(self, npc: "NPCInstance", station: "StationState") -> None:
        """Choose the next appropriate job for this NPC."""
        day = is_day_phase(station)

        if day:
            candidates = _CLASS_DAY_JOBS.get(npc.class_id, [])
            # Filter to jobs that exist in registry
            candidates = [j for j in candidates if j in self.jobs.jobs]

            # Respect work assignments if set
            work_assign = getattr(station, 'work_assignments', {}).get(npc.uid)
            if work_assign:
                candidates = [j for j in candidates if j in work_assign] or candidates

            if candidates:
                job_id = random.choice(candidates)
                self._set_job(npc, job_id, station)
                # If module not found, wander instead
                if npc.job_module_uid is None:
                    self._set_wander(npc, station)
            else:
                self._set_wander(npc, station)
        else:
            self._set_job(npc, _REST_JOB, station)

    def _set_wander(self, npc: "NPCInstance", station: "StationState") -> None:
        """Assign wander job — NPC roams in pressurized rooms, preferring hab."""
        candidates = [m for m in station.modules.values()
                      if m.active and m.owner_id in (None, "player")]

        def _category_priority(m):
            return {"hab": 0, "utility": 1}.get(m.category, 2)
        candidates.sort(key=_category_priority)
        mod = candidates[0] if candidates else None

        if mod:
            npc.current_job_id = "job.wander"
            npc.job_module_uid = mod.uid
            npc.job_timer      = 2
            npc.location       = mod.definition_id
        else:
            npc.current_job_id = _REST_JOB
            npc.job_module_uid = None
            npc.job_timer      = 4

    def _set_job(self, npc: "NPCInstance", job_id: str,
                 station: "StationState") -> None:
        job = self.jobs.get(job_id)
        if job is None:
            return

        # Find a suitable module (respects NPC ownership)
        module = self._find_module(job, station, npc)
        npc.current_job_id  = job_id
        npc.job_module_uid  = module.uid if module else None
        npc.job_timer       = job.duration_ticks
        if module:
            npc.location = module.definition_id

    def _find_module(self, job: JobDefinition,
                     station: "StationState",
                     npc: "NPCInstance | None" = None) -> "ModuleInstance | None":
        """Find an active module matching the job's preferred category.

        If the NPC has an owner_id (faction ship crew), restrict to modules
        that share the same owner_id so their activities stay on their own ship.
        """
        owner = npc.owner_id if npc else None

        def _owned(m: "ModuleInstance") -> bool:
            if owner and owner != "player":
                return m.owner_id == owner
            return m.owner_id is None or m.owner_id == "player"

        preferred = [
            m for m in station.modules.values()
            if m.active and m.category == job.preferred_module_category and _owned(m)
        ]
        if preferred:
            return random.choice(preferred)
        fallback = [
            m for m in station.modules.values()
            if m.active and m.category == job.fallback_module_category and _owned(m)
        ]
        return random.choice(fallback) if fallback else None

    def _apply_job_effects(self, npc: "NPCInstance",
                           station: "StationState") -> None:
        job = self.jobs.get(npc.current_job_id or "")
        if job is None:
            return

        # Station resource effects
        for res, delta in job.resource_effects.items():
            # Scale by skill if relevant
            skill_level = npc.skills.get(job.skill_used or "", 5)
            scale = 0.5 + skill_level / 10.0   # 0.5–1.5× based on skill 0–10
            # Apply workbench speed bonus for workbench jobs in qualifying rooms
            if job.id in ("job.craft", "job.refine") and npc.job_module_uid:
                bonus = self._workbench_speed_bonus(npc, station)
                scale *= (1.0 + bonus)
            station.modify_resource(res, delta * scale)

        # NPC need effects
        npc.update_needs(job.need_effects)

        # Station special effects
        if "add_tag" in job.station_effects:
            station.set_tag(job.station_effects["add_tag"])
        if "repair_module" in job.station_effects and npc.job_module_uid:
            mod = station.modules.get(npc.job_module_uid or "")
            if mod and mod.damage > 0:
                repair_amt = float(job.station_effects["repair_module"])
                mod.damage = max(0.0, mod.damage - repair_amt)

    def _workbench_speed_bonus(self, npc: "NPCInstance",
                               station: "StationState") -> float:
        """
        Return the workbench speed bonus (0.0–N) for an NPC performing a craft/
        refine job.  Returns _DEFAULT_WORKBENCH_BONUS (0.05) if:
          - The NPC's job module is in a tile-map room
          - That room has a room_type that allows workbenches
          - The room meets its min_beauty requirement
          - The room has at most max_workbenches workbenches present
          - The room has fewer than max_workbenches *at or above the limit*
            (i.e. at least 1 workbench placed, but not over the cap)
        Otherwise 0.0.
        """
        try:
            mod = station.modules.get(npc.job_module_uid or "")
            if mod is None:
                return 0.0

            # Try to find the tile-map room for this module
            tm = station.tile_map
            target_room = None
            for room in tm.rooms.values():
                if room.module_uid == npc.job_module_uid:
                    target_room = room
                    break
            if target_room is None:
                return 0.0

            room_type_id = target_room.room_type_id
            if not room_type_id:
                return 0.0

            # Look up definition (registry not directly available here; use
            # a lightweight duck-type check via station custom types)
            # We return bonus based on beauty check alone when no registry
            # is available; actual registry check is done in building.py.
            # For runtime bonus: if beauty >= 20 we grant the default bonus.
            beauty = target_room.beauty
            if beauty >= 20:
                return _DEFAULT_WORKBENCH_BONUS
        except Exception:
            pass
        return 0.0

    # ------------------------------------------------------------------
    # External interface
    # ------------------------------------------------------------------

    def interrupt_npc(self, npc: "NPCInstance") -> None:
        """Called when an event pulls an NPC off their job."""
        npc.job_interrupted = True

    def get_job_label(self, npc: "NPCInstance") -> str:
        if not npc.current_job_id:
            return "idle"
        job = self.jobs.get(npc.current_job_id)
        return job.display_name if job else npc.current_job_id
