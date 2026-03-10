"""
Static template definitions — loaded from data files, never mutated at runtime.

Templates are the authoring surface. Instances are the runtime entities.
"""

from __future__ import annotations
from dataclasses import dataclass, field
from typing import Any


# ---------------------------------------------------------------------------
# Shared building blocks
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class SkillRange:
    min: int
    max: int

    @classmethod
    def from_raw(cls, raw: Any) -> "SkillRange":
        if isinstance(raw, list) and len(raw) == 2:
            return cls(min=int(raw[0]), max=int(raw[1]))
        if isinstance(raw, dict):
            return cls(min=int(raw.get("min", 0)), max=int(raw.get("max", 10)))
        return cls(min=0, max=10)


@dataclass(frozen=True)
class ConditionBlock:
    """A single condition that must be true for an event to fire."""
    type: str           # e.g. "tag_present", "resource_below", "faction_rep_above"
    target: str = ""    # what the condition checks against
    value: Any = None   # threshold or comparison value
    negate: bool = False

    @classmethod
    def from_raw(cls, raw: dict) -> "ConditionBlock":
        return cls(
            type=raw["type"],
            target=raw.get("target", ""),
            value=raw.get("value"),
            negate=bool(raw.get("negate", False)),
        )


@dataclass(frozen=True)
class OutcomeEffect:
    """A single effect produced by an event choice or outcome."""
    type: str           # e.g. "add_resource", "spawn_npc", "set_tag", "trigger_event"
    target: str = ""
    value: Any = None
    args: dict = field(default_factory=dict)

    @classmethod
    def from_raw(cls, raw: dict) -> "OutcomeEffect":
        return cls(
            type=raw["type"],
            target=raw.get("target", ""),
            value=raw.get("value"),
            args={k: v for k, v in raw.items() if k not in ("type", "target", "value")},
        )


@dataclass(frozen=True)
class EventChoice:
    id: str
    label: str
    conditions: tuple[ConditionBlock, ...]
    outcomes: tuple[OutcomeEffect, ...]
    followup_event: str | None = None   # event ID to queue after this choice

    @classmethod
    def from_raw(cls, raw: dict) -> "EventChoice":
        return cls(
            id=raw["id"],
            label=raw["label"],
            conditions=tuple(ConditionBlock.from_raw(c) for c in raw.get("conditions", [])),
            outcomes=tuple(OutcomeEffect.from_raw(o) for o in raw.get("outcomes", [])),
            followup_event=raw.get("followup_event"),
        )


# ---------------------------------------------------------------------------
# Event Definition
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class EventDefinition:
    """
    A data-authored event definition.

    Loaded from /data/events/*.yaml or a mod's events folder.
    """
    id: str
    category: str                           # arrival / station / faction / incident / random
    title: str
    description: str
    weight: float = 1.0                     # relative spawn weight
    cooldown: int = 0                       # ticks before this event can fire again
    required_tags: tuple[str, ...] = ()     # station must have ALL of these
    excluded_tags: tuple[str, ...] = ()     # station must have NONE of these
    trigger_conditions: tuple[ConditionBlock, ...] = ()
    choices: tuple[EventChoice, ...] = ()
    # Fallback outcomes if there are no choices (auto-resolved events)
    auto_outcomes: tuple[OutcomeEffect, ...] = ()
    followup_events: tuple[str, ...] = ()   # additional events queued after resolution
    # If True the event pauses the game until the player responds
    hostile: bool = False
    # Non-zero: event expires and is skipped after this many ticks
    expires_in: int = 0
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "EventDefinition":
        return cls(
            id=raw["id"],
            category=raw.get("category", "random"),
            title=raw["title"],
            description=raw.get("description", ""),
            weight=float(raw.get("weight", 1.0)),
            cooldown=int(raw.get("cooldown", 0)),
            required_tags=tuple(raw.get("required_tags", [])),
            excluded_tags=tuple(raw.get("excluded_tags", [])),
            trigger_conditions=tuple(
                ConditionBlock.from_raw(c) for c in raw.get("trigger_conditions", [])
            ),
            choices=tuple(EventChoice.from_raw(c) for c in raw.get("choices", [])),
            auto_outcomes=tuple(OutcomeEffect.from_raw(o) for o in raw.get("auto_outcomes", [])),
            followup_events=tuple(raw.get("followup_events", [])),
            hostile=bool(raw.get("hostile", False)),
            expires_in=int(raw.get("expires_in", 0)),
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# NPC Template
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class NPCTemplate:
    """
    Template for procedurally generating NPC instances.
    """
    id: str
    base_class: str
    allowed_subclasses: tuple[str, ...] = ()
    skill_ranges: dict[str, SkillRange] = field(default_factory=dict)
    trait_pool: tuple[str, ...] = ()
    aspirations_pool: tuple[str, ...] = ()  # pool of possible NPC goals/ambitions
    faction_bias: dict[str, float] = field(default_factory=dict)
    name_pool: tuple[str, ...] = ()     # optional curated names
    equipment_pool: tuple[str, ...] = ()
    spawn_rules: dict[str, Any] = field(default_factory=dict)
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "NPCTemplate":
        skill_ranges: dict[str, SkillRange] = {
            skill: SkillRange.from_raw(rng)
            for skill, rng in raw.get("skill_ranges", {}).items()
        }
        return cls(
            id=raw["id"],
            base_class=raw["base_class"],
            allowed_subclasses=tuple(raw.get("allowed_subclasses", [])),
            skill_ranges=skill_ranges,
            trait_pool=tuple(raw.get("trait_pool", [])),
            aspirations_pool=tuple(raw.get("aspirations_pool", [])),
            faction_bias={k: float(v) for k, v in raw.get("faction_bias", {}).items()},
            name_pool=tuple(raw.get("name_pool", [])),
            equipment_pool=tuple(raw.get("equipment_pool", [])),
            spawn_rules=raw.get("spawn_rules", {}),
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# Ship Template
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class ShipTemplate:
    id: str
    role: str                               # trader / refugee / raider / inspector / transport
    faction_restrictions: tuple[str, ...] = ()   # empty = any faction
    cargo_capacity: int = 0
    passenger_capacity: int = 0
    threat_level: int = 0                   # 0 = harmless, 10 = warship
    behavior_tags: tuple[str, ...] = ()     # e.g. "smuggler", "patrol", "hostile_if_denied"
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "ShipTemplate":
        return cls(
            id=raw["id"],
            role=raw["role"],
            faction_restrictions=tuple(raw.get("faction_restrictions", [])),
            cargo_capacity=int(raw.get("cargo_capacity", 0)),
            passenger_capacity=int(raw.get("passenger_capacity", 0)),
            threat_level=int(raw.get("threat_level", 0)),
            behavior_tags=tuple(raw.get("behavior_tags", [])),
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# Class Definition
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class ClassDefinition:
    id: str
    parent: str | None = None               # subclass relationship
    display_name: str = ""
    description: str = ""
    modifiers: dict[str, float] = field(default_factory=dict)
    allowed_jobs: tuple[str, ...] = ()
    unlock_hooks: tuple[str, ...] = ()      # what unlocks this class
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "ClassDefinition":
        return cls(
            id=raw["id"],
            parent=raw.get("parent"),
            display_name=raw.get("display_name", raw["id"]),
            description=raw.get("description", ""),
            modifiers={k: float(v) for k, v in raw.get("modifiers", {}).items()},
            allowed_jobs=tuple(raw.get("allowed_jobs", [])),
            unlock_hooks=tuple(raw.get("unlock_hooks", [])),
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# Faction Definition
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class FactionDefinition:
    id: str
    display_name: str
    type: str = "minor"                     # major / regional / minor
    description: str = ""
    ideology_tags: tuple[str, ...] = ()     # e.g. "authoritarian", "mercantile", "militarist"
    diplomacy_profile: dict[str, Any] = field(default_factory=dict)
    economic_profile: dict[str, Any] = field(default_factory=dict)
    behavior_tags: tuple[str, ...] = ()     # e.g. "raids_weak_stations", "trades_always"
    relationships: dict[str, float] = field(default_factory=dict)  # faction_id -> -1..1
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "FactionDefinition":
        return cls(
            id=raw["id"],
            display_name=raw.get("display_name", raw["id"]),
            type=raw.get("type", "minor"),
            description=raw.get("description", ""),
            ideology_tags=tuple(raw.get("ideology_tags", [])),
            diplomacy_profile=raw.get("diplomacy_profile", {}),
            economic_profile=raw.get("economic_profile", {}),
            behavior_tags=tuple(raw.get("behavior_tags", [])),
            relationships={k: float(v) for k, v in raw.get("relationships", {}).items()},
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# Item Definition
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class ItemDefinition:
    """
    Static definition for a single item type.

    Loaded from /data/items/*.yaml or a mod's items folder.
    """
    id: str
    display_name: str
    item_type: str                          # Material / Equipment / Biological / Valuables / Waste
    subtype: str = ""                       # e.g. Raw Materials, Refined Materials, Food, Currency
    description: str = ""
    weight: float = 1.0                     # cargo capacity units consumed per item unit
    stack_size: int = 100                   # max units per stack slot
    value: float = 1.0                      # credits per unit
    perishable_ticks: int = 0              # 0 = not perishable; ticks before item decays
    quality: str = "standard"              # poor / standard / fine / rare / exotic
    legal: bool = True
    tags: tuple[str, ...] = ()             # flexible metadata: luxury, volatile, medical, etc.
    build_cost: dict[str, float] = field(default_factory=dict)  # resource cost to produce
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "ItemDefinition":
        return cls(
            id=raw["id"],
            display_name=raw.get("display_name", raw["id"]),
            item_type=raw.get("item_type", "Material"),
            subtype=raw.get("subtype", ""),
            description=raw.get("description", ""),
            weight=float(raw.get("weight", 1.0)),
            stack_size=int(raw.get("stack_size", 100)),
            value=float(raw.get("value", 1.0)),
            perishable_ticks=int(raw.get("perishable_ticks", 0)),
            quality=raw.get("quality", "standard"),
            legal=bool(raw.get("legal", True)),
            tags=tuple(raw.get("tags", [])),
            build_cost={k: float(v) for k, v in raw.get("build_cost", {}).items()},
            schema_version=str(raw.get("schema_version", "1")),
        )


# ---------------------------------------------------------------------------
# Module Definition (station room/structure)
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class ModuleDefinition:
    id: str
    display_name: str
    category: str = "utility"              # utility / dock / hab / production / security / cargo
    description: str = ""
    resource_effects: dict[str, float] = field(default_factory=dict)  # per-tick deltas
    capacity: int = 0                       # crew/visitor slots
    cargo_capacity: int = 0                 # item units this module can store (0 = not a cargo hold)
    tags: tuple[str, ...] = ()
    unlock_conditions: tuple[str, ...] = ()
    schema_version: str = "1"

    @classmethod
    def from_raw(cls, raw: dict) -> "ModuleDefinition":
        return cls(
            id=raw["id"],
            display_name=raw.get("display_name", raw["id"]),
            category=raw.get("category", "utility"),
            description=raw.get("description", ""),
            resource_effects={k: float(v) for k, v in raw.get("resource_effects", {}).items()},
            capacity=int(raw.get("capacity", 0)),
            cargo_capacity=int(raw.get("cargo_capacity", 0)),
            tags=tuple(raw.get("tags", [])),
            unlock_conditions=tuple(raw.get("unlock_conditions", [])),
            schema_version=str(raw.get("schema_version", "1")),
        )
