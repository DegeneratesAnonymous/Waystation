"""
NPC System — procedural generation and runtime management of NPC instances.

Generates NPCInstance objects from NPCTemplate definitions.
Handles per-tick needs decay and mood recalculation.
"""

from __future__ import annotations

import logging
import random
from typing import TYPE_CHECKING

from waystation.models.instances import NPCInstance

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import StationState
    from waystation.models.templates import NPCTemplate

log = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Name generation (fallback if template has no name_pool)
# ---------------------------------------------------------------------------

_FIRST_NAMES = [
    "Aiko", "Brecht", "Cael", "Dara", "Ezra", "Fenn", "Gira", "Holt",
    "Idris", "Jura", "Kael", "Lira", "Moro", "Naya", "Oren", "Petra",
    "Quinn", "Rael", "Siva", "Thorn", "Ulva", "Vena", "Wren", "Xan",
    "Yola", "Zeph", "Asha", "Brix", "Cova", "Deln",
]

_SURNAMES = [
    "Vance", "Orel", "Dusk", "Harlow", "Mira", "Crane", "Forde", "Sable",
    "Thane", "Nori", "Crest", "Weld", "Pell", "Strix", "Vael", "Oryn",
]


def _generate_name(template: "NPCTemplate") -> str:
    if template.name_pool:
        return random.choice(template.name_pool)
    first = random.choice(_FIRST_NAMES)
    last = random.choice(_SURNAMES)
    return f"{first} {last}"


def _roll_skills(template: "NPCTemplate") -> dict[str, int]:
    skills: dict[str, int] = {}
    for skill_name, skill_range in template.skill_ranges.items():
        skills[skill_name] = random.randint(skill_range.min, skill_range.max)
    return skills


def _pick_traits(template: "NPCTemplate", count: int = 2) -> list[str]:
    pool = list(template.trait_pool)
    if not pool:
        return []
    return random.sample(pool, min(count, len(pool)))


def _pick_subclass(template: "NPCTemplate") -> str | None:
    if not template.allowed_subclasses:
        return None
    return random.choice(template.allowed_subclasses)


def _pick_faction(template: "NPCTemplate") -> str | None:
    if not template.faction_bias:
        return None
    # Weighted selection from faction_bias dict
    factions = list(template.faction_bias.keys())
    weights = [template.faction_bias[f] for f in factions]
    return random.choices(factions, weights=weights, k=1)[0]


# ---------------------------------------------------------------------------
# NPC Factory
# ---------------------------------------------------------------------------

class NPCSystem:

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

    def spawn_from_template(self,
                             template_id: str,
                             status_tags: list[str] | None = None,
                             overrides: dict | None = None) -> NPCInstance | None:
        """
        Generate a new NPCInstance from the named template.

        status_tags: e.g. ["crew"] or ["visitor"]
        overrides: dict of field values to force-set after generation
        """
        template = self.registry.npcs.get(template_id)
        if template is None:
            log.error("Unknown NPC template '%s'", template_id)
            return None

        npc = NPCInstance.create(
            template_id=template_id,
            name=_generate_name(template),
            class_id=template.base_class,
            subclass_id=_pick_subclass(template),
        )

        npc.skills = _roll_skills(template)
        npc.traits = _pick_traits(template)
        npc.faction_id = _pick_faction(template)
        npc.status_tags = list(status_tags or [])

        if overrides:
            for attr, value in overrides.items():
                if hasattr(npc, attr):
                    setattr(npc, attr, value)

        npc.recalculate_mood()
        log.debug("Spawned NPC %s (%s) from template %s", npc.name, npc.uid, template_id)
        return npc

    def spawn_crew_member(self, template_id: str) -> NPCInstance | None:
        return self.spawn_from_template(template_id, status_tags=["crew"])

    def spawn_visitor(self, template_id: str, faction_id: str | None = None) -> NPCInstance | None:
        overrides = {"faction_id": faction_id} if faction_id else None
        return self.spawn_from_template(template_id, status_tags=["visitor"], overrides=overrides)

    # ------------------------------------------------------------------
    # Per-tick update
    # ------------------------------------------------------------------

    NEEDS_DECAY_RATE: dict[str, float] = {
        "hunger": -0.04,
        "rest":   -0.03,
        "social": -0.01,
        "safety":  0.00,   # safety is driven by events, not passive decay
    }

    # Skill XP gained per tick while working a relevant job
    _SKILL_XP_PER_TICK = 0.008
    # XP needed to advance one skill level (accumulated in skill_xp float)
    _XP_PER_LEVEL = 1.0
    # Social need recovery constants
    _MAX_PASSIVE_SOCIAL_RECOVERY = 0.02
    _SOCIAL_RECOVERY_PER_PERSON = 0.005
    # Chance per tick that an injured NPC heals one injury in med bay
    _INJURY_RECOVERY_CHANCE = 0.05

    def tick(self, station: "StationState") -> None:
        """Update all NPCs: needs decay, mood recalculation, distress events."""
        crew_count = len(station.get_crew())
        for npc in station.npcs.values():
            self._tick_npc(npc, station, crew_count)

    def _tick_npc(self, npc: NPCInstance, station: "StationState", crew_count: int = 1) -> None:
        # Decay needs
        npc.update_needs(self.NEEDS_DECAY_RATE)

        # Crew have food provided if station has food
        if npc.is_crew() or npc.is_visitor():
            if station.get_resource("food") > 0:
                npc.update_needs({"hunger": 0.06})   # fed
                station.modify_resource("food", -0.5)
            if station.get_resource("oxygen") > 0:
                pass  # oxygen is ambient; no per-NPC tick cost here

        # Social need recovers slightly when there are multiple people around
        # More crew/visitors = more social interaction
        social_recovery = min(
            self._MAX_PASSIVE_SOCIAL_RECOVERY,
            (crew_count - 1) * self._SOCIAL_RECOVERY_PER_PERSON
        )
        if social_recovery > 0:
            npc.update_needs({"social": social_recovery})

        # Visitor lounge boosts social for nearby NPCs
        if any(
            m.active and "visitor_lounge" in m.definition_id
            for m in station.modules.values()
        ):
            npc.update_needs({"social": 0.01})

        # Injured NPCs slowly recover in med bay
        if npc.injuries > 0 and any(
            m.active and m.category == "utility" and "med_bay" in m.definition_id
            for m in station.modules.values()
        ):
            if random.random() < self._INJURY_RECOVERY_CHANCE:
                npc.injuries = max(0, npc.injuries - 1)

        npc.recalculate_mood()

        # Skill progression: if the NPC is working a job, accumulate XP in the relevant skill
        if npc.is_crew() and npc.current_job_id:
            self._try_advance_skill(npc)

        # Distress logging (sampled to avoid log spam)
        if npc.needs["hunger"] < 0.2 and random.random() < 0.1:
            station.log_event(f"{npc.name} is starving.")
        if npc.needs["rest"] < 0.1 and random.random() < 0.1:
            station.log_event(f"{npc.name} is exhausted.")

    # ------------------------------------------------------------------
    # Convenience queries
    # ------------------------------------------------------------------

    def _try_advance_skill(self, npc: NPCInstance) -> None:
        """Accumulate XP for the job's primary skill; advance when threshold reached."""
        # Map each job to the skill it trains
        _JOB_SKILL_MAP: dict[str, str] = {
            "job.guard_post":            "combat",
            "job.patrol":                "perception",
            "job.contraband_inspection": "investigation",
            "job.module_maintenance":    "repair",
            "job.power_management":      "technical",
            "job.life_support":          "technical",
            "job.dock_control":          "coordination",
            "job.resource_management":   "logistics",
            "job.visitor_intake":        "negotiation",
        }
        skill = _JOB_SKILL_MAP.get(npc.current_job_id or "", "")
        if not skill:
            return
        current_level = npc.skills.get(skill, 0)
        if current_level >= 10:
            return  # already maxed

        xp = npc.skill_xp.get(skill, 0.0) + self._SKILL_XP_PER_TICK
        if xp >= self._XP_PER_LEVEL:
            npc.skills[skill] = current_level + 1
            xp -= self._XP_PER_LEVEL
            log.debug("%s improved %s to %d", npc.name, skill, npc.skills[skill])
        npc.skill_xp[skill] = xp

    def get_crew_with_skill(self,
                             station: "StationState",
                             skill: str,
                             min_level: int = 1) -> list[NPCInstance]:
        return [
            npc for npc in station.get_crew()
            if npc.skills.get(skill, 0) >= min_level
        ]

    def get_npc_by_class(self,
                          station: "StationState",
                          class_id: str) -> list[NPCInstance]:
        return [npc for npc in station.npcs.values() if npc.class_id == class_id]

    def average_crew_mood(self, station: "StationState") -> float:
        crew = station.get_crew()
        if not crew:
            return 0.0
        return sum(n.mood for n in crew) / len(crew)
