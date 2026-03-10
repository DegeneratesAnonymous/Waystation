"""
Runtime instance state — mutable entities created from templates.

All instances carry a uid (unique runtime ID) and a template_id that
references the definition they were generated from.
"""

from __future__ import annotations
from dataclasses import dataclass, field
from typing import Any
import uuid


def _new_uid() -> str:
    return str(uuid.uuid4())[:8]


# ---------------------------------------------------------------------------
# NPC Instance
# ---------------------------------------------------------------------------

@dataclass
class NPCInstance:
    uid: str
    template_id: str
    name: str
    class_id: str
    subclass_id: str | None

    # Derived skills (rolled from template ranges at spawn)
    skills: dict[str, int] = field(default_factory=dict)

    # Personality / behaviour traits (drawn from template trait_pool)
    traits: list[str] = field(default_factory=list)

    # Needs — 0.0 (critical) to 1.0 (fully satisfied)
    needs: dict[str, float] = field(default_factory=lambda: {
        "hunger": 1.0,
        "rest": 1.0,
        "social": 0.5,
        "safety": 1.0,
    })

    # -1.0 (miserable) to 1.0 (content)
    mood: float = 0.5

    # Where this NPC is on the station (module definition_id or uid)
    location: str = "commons"

    # ── Job system ──────────────────────────────────────────────────────────
    current_job_id:     str | None = None   # job definition id
    job_module_uid:     str | None = None   # which module they're working in
    job_timer:          int        = 0      # ticks remaining in current job cycle
    job_interrupted:    bool       = False  # True if an event pulled them off job

    # Faction association
    faction_id: str | None = None

    # Legal / residency status tags
    status_tags: list[str] = field(default_factory=list)  # e.g. ["crew", "visitor", "detained"]

    # Arbitrary memory hooks for events to read/write
    memory: dict[str, Any] = field(default_factory=dict)

    # Skill XP accumulation (float; integer part = skill level to apply)
    skill_xp: dict[str, float] = field(default_factory=dict)

    # Injury count — healed over time in med bay
    injuries: int = 0

    @classmethod
    def create(cls,
               template_id: str,
               name: str,
               class_id: str,
               subclass_id: str | None = None) -> "NPCInstance":
        return cls(
            uid=_new_uid(),
            template_id=template_id,
            name=name,
            class_id=class_id,
            subclass_id=subclass_id,
        )

    def is_crew(self) -> bool:
        return "crew" in self.status_tags

    def is_visitor(self) -> bool:
        return "visitor" in self.status_tags

    def update_needs(self, delta: dict[str, float]) -> None:
        for need, change in delta.items():
            self.needs[need] = max(0.0, min(1.0, self.needs.get(need, 0.5) + change))

    def recalculate_mood(self) -> None:
        """Simple mood model: average of needs with safety weighted double."""
        weights = {"hunger": 1.0, "rest": 1.0, "social": 0.5, "safety": 2.0}
        total_weight = sum(weights.values())
        weighted_sum = sum(self.needs.get(n, 0.5) * w for n, w in weights.items())
        # Traits can shift mood
        trait_bonus = sum(
            0.1 if t in ("resilient", "optimistic") else
            -0.1 if t in ("anxious", "bitter") else 0.0
            for t in self.traits
        )
        self.mood = max(-1.0, min(1.0, (weighted_sum / total_weight) * 2 - 1 + trait_bonus))

    def mood_label(self) -> str:
        if self.mood >= 0.6:
            return "content"
        if self.mood >= 0.2:
            return "okay"
        if self.mood >= -0.2:
            return "uneasy"
        if self.mood >= -0.6:
            return "distressed"
        return "miserable"


# ---------------------------------------------------------------------------
# Ship Instance
# ---------------------------------------------------------------------------

@dataclass
class ShipInstance:
    uid: str
    template_id: str
    name: str
    role: str

    faction_id: str | None = None
    intent: str = "unknown"                 # trade / refuge / raid / inspect / transit
    cargo: dict[str, int] = field(default_factory=dict)
    passenger_uids: list[str] = field(default_factory=list)
    threat_level: int = 0
    behavior_tags: list[str] = field(default_factory=list)

    # Docking state
    status: str = "incoming"               # incoming / docked / departing / hostile / destroyed
    docked_at: str | None = None           # module uid
    ticks_docked: int = 0

    @classmethod
    def create(cls,
               template_id: str,
               name: str,
               role: str,
               intent: str = "unknown",
               faction_id: str | None = None,
               threat_level: int = 0) -> "ShipInstance":
        return cls(
            uid=_new_uid(),
            template_id=template_id,
            name=name,
            role=role,
            intent=intent,
            faction_id=faction_id,
            threat_level=threat_level,
        )

    def is_hostile(self) -> bool:
        return self.status == "hostile" or self.intent == "raid"

    def threat_label(self) -> str:
        if self.threat_level == 0:
            return "none"
        if self.threat_level <= 2:
            return "low"
        if self.threat_level <= 5:
            return "moderate"
        if self.threat_level <= 8:
            return "high"
        return "extreme"


# ---------------------------------------------------------------------------
# Module Instance (station room)
# ---------------------------------------------------------------------------

@dataclass
class ModuleInstance:
    uid: str
    definition_id: str
    display_name: str
    category: str
    occupants: list[str] = field(default_factory=list)   # NPC uids
    docked_ship: str | None = None                        # ship uid (for dock modules)
    active: bool = True
    damage: float = 0.0                                   # 0.0 = fine, 1.0 = destroyed

    @classmethod
    def create(cls, definition_id: str, display_name: str, category: str) -> "ModuleInstance":
        return cls(
            uid=_new_uid(),
            definition_id=definition_id,
            display_name=display_name,
            category=category,
        )

    def is_dock(self) -> bool:
        return self.category == "dock"

    def is_available_dock(self) -> bool:
        return self.is_dock() and self.docked_ship is None and self.active


# ---------------------------------------------------------------------------
# Station State
# ---------------------------------------------------------------------------

@dataclass
class StationState:
    """
    All mutable runtime state for a single station.
    """
    name: str
    tick: int = 0

    # Resources tracked per tick
    resources: dict[str, float] = field(default_factory=lambda: {
        "credits": 500.0,
        "food":    100.0,
        "power":   100.0,
        "oxygen":  100.0,
        "parts":    50.0,
        "ice":     200.0,   # raw ice — processed into water/oxygen by life support
    })

    # Entity registries (keyed by uid)
    npcs: dict[str, NPCInstance] = field(default_factory=dict)
    ships: dict[str, ShipInstance] = field(default_factory=dict)
    modules: dict[str, ModuleInstance] = field(default_factory=dict)

    # Faction reputation: faction_id -> -100..100
    faction_reputation: dict[str, float] = field(default_factory=dict)

    # Active state tags on the station (e.g. "under_blockade", "plague_risk")
    active_tags: set[str] = field(default_factory=set)

    # Policy flags (player decisions that shape event pools)
    policy: dict[str, str] = field(default_factory=dict)

    # Cooldown tracker: event_id -> tick it can next fire
    event_cooldowns: dict[str, int] = field(default_factory=dict)

    # Active trade offers keyed by ship uid (populated when trader docks)
    trade_offers: dict[str, Any] = field(default_factory=dict)

    # Log of recent events/messages (most recent first)
    log: list[str] = field(default_factory=list)

    def add_npc(self, npc: NPCInstance) -> None:
        self.npcs[npc.uid] = npc

    def remove_npc(self, uid: str) -> NPCInstance | None:
        return self.npcs.pop(uid, None)

    def add_ship(self, ship: ShipInstance) -> None:
        self.ships[ship.uid] = ship

    def remove_ship(self, uid: str) -> ShipInstance | None:
        return self.ships.pop(uid, None)

    def add_module(self, module: ModuleInstance) -> None:
        self.modules[module.uid] = module

    def get_resource(self, key: str) -> float:
        return self.resources.get(key, 0.0)

    def modify_resource(self, key: str, delta: float) -> float:
        current = self.resources.get(key, 0.0)
        self.resources[key] = max(0.0, current + delta)
        return self.resources[key]

    def set_tag(self, tag: str) -> None:
        self.active_tags.add(tag)

    def clear_tag(self, tag: str) -> None:
        self.active_tags.discard(tag)

    def has_tag(self, tag: str) -> bool:
        return tag in self.active_tags

    def get_crew(self) -> list[NPCInstance]:
        return [n for n in self.npcs.values() if n.is_crew()]

    def get_visitors(self) -> list[NPCInstance]:
        return [n for n in self.npcs.values() if n.is_visitor()]

    def get_docked_ships(self) -> list[ShipInstance]:
        return [s for s in self.ships.values() if s.status == "docked"]

    def get_incoming_ships(self) -> list[ShipInstance]:
        return [s for s in self.ships.values() if s.status == "incoming"]

    def get_available_dock(self) -> ModuleInstance | None:
        for module in self.modules.values():
            if module.is_available_dock():
                return module
        return None

    def log_event(self, message: str) -> None:
        self.log.insert(0, f"[T{self.tick:04d}] {message}")
        if len(self.log) > 200:
            self.log = self.log[:200]

    def get_faction_rep(self, faction_id: str) -> float:
        return self.faction_reputation.get(faction_id, 0.0)

    def modify_faction_rep(self, faction_id: str, delta: float) -> float:
        current = self.faction_reputation.get(faction_id, 0.0)
        self.faction_reputation[faction_id] = max(-100.0, min(100.0, current + delta))
        return self.faction_reputation[faction_id]
