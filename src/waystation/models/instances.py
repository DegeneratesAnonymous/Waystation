"""
Runtime instance state — mutable entities created from templates.

All instances carry a uid (unique runtime ID) and a template_id that
references the definition they were generated from.
"""

from __future__ import annotations
from dataclasses import dataclass, field
from typing import Any
import uuid

from waystation.models.tilemap import TileMap


def _new_uid() -> str:
    return str(uuid.uuid4())[:8]


# ---------------------------------------------------------------------------
# NPC Instance
# ---------------------------------------------------------------------------

# Legacy need keys from saves written before the rename (hunger→food, rest→sleep,
# social→recreation). Kept at module level to avoid per-call dict construction.
_NPC_LEGACY_NEED_KEYS: dict[str, str] = {
    "hunger": "food",
    "rest":   "sleep",
    "social": "recreation",
}

# Default NPC needs merged into any loaded save so new keys are always present.
_NPC_DEFAULT_NEEDS: dict[str, float] = {
    "oxygen":      1.0,
    "temperature": 1.0,
    "food":        1.0,
    "thirst":      1.0,
    "sleep":       1.0,
    "bathroom":    1.0,
    "recreation":  0.5,
    "safety":      1.0,
}

@dataclass
class NPCInstance:
    uid: str
    template_id: str
    name: str
    class_id: str
    subclass_id: str | None

    # Biological identity
    species: str = "human"                  # e.g. "human", "synth", "xeno"

    # Derived skills (rolled from template ranges at spawn)
    skills: dict[str, int] = field(default_factory=dict)

    # Personality / behaviour traits (drawn from template trait_pool) — 3 traits
    traits: list[str] = field(default_factory=list)

    # Aspirations — personal goals and ambitions
    aspirations: list[str] = field(default_factory=list)

    # Needs — 0.0 (critical) to 1.0 (fully satisfied)
    needs: dict[str, float] = field(default_factory=lambda: {
        "oxygen":      1.0,
        "temperature": 1.0,
        "food":        1.0,
        "thirst":      1.0,
        "sleep":       1.0,
        "bathroom":    1.0,
        "recreation":  0.5,
        "safety":      1.0,
    })

    # -1.0 (miserable) to 1.0 (content)
    mood: float = 0.5

    # ── Combat stats ────────────────────────────────────────────────────────
    # health: current / max hit points
    health: int = 100
    max_health: int = 100
    # armor: flat damage reduction per hit (0 = no protection)
    armor: int = 0
    # speed: action/movement speed multiplier (base 10; higher = faster)
    speed: int = 10

    # Where this NPC is on the station (module definition_id or uid)
    location: str = "commons"

    # ── Job system ──────────────────────────────────────────────────────────
    current_job_id:     str | None = None   # job definition id
    job_module_uid:     str | None = None   # which module they're working in
    job_timer:          int        = 0      # ticks remaining in current job cycle
    job_interrupted:    bool       = False  # True if an event pulled them off job

    # Faction association
    faction_id: str | None = None

    # Ownership — None / "player" = player-owned; faction_id or ship_uid = belongs to that entity
    owner_id: str | None = None

    # Relationship scores with other NPCs keyed by NPC uid (-1.0 hostile to 1.0 close)
    relationships: dict[str, float] = field(default_factory=dict)

    # Legal / residency status tags
    status_tags: list[str] = field(default_factory=list)  # e.g. ["crew", "visitor", "detained"]

    # Personal inventory — items the NPC is currently carrying (item_id → quantity)
    # Used for hauling tasks, personal effects, and equipment carried on their person
    inventory: dict[str, int] = field(default_factory=dict)

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
        """Mood model: weighted average of needs; critical needs (oxygen, safety) count most."""
        weights = {
            "oxygen":      3.0,
            "safety":      2.0,
            "temperature": 2.0,
            "food":        1.5,
            "thirst":      1.5,
            "sleep":       1.0,
            "bathroom":    0.8,
            "recreation":  0.5,
        }
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

    def to_dict(self) -> dict:
        return {
            "uid":            self.uid,
            "template_id":    self.template_id,
            "name":           self.name,
            "class_id":       self.class_id,
            "subclass_id":    self.subclass_id,
            "species":        self.species,
            "skills":         dict(self.skills),
            "traits":         list(self.traits),
            "aspirations":    list(self.aspirations),
            "needs":          dict(self.needs),
            "mood":           self.mood,
            "health":         self.health,
            "max_health":     self.max_health,
            "armor":          self.armor,
            "speed":          self.speed,
            "location":       self.location,
            "current_job_id": self.current_job_id,
            "job_module_uid": self.job_module_uid,
            "job_timer":      self.job_timer,
            "job_interrupted": self.job_interrupted,
            "faction_id":     self.faction_id,
            "owner_id":       self.owner_id,
            "relationships":  dict(self.relationships),
            "status_tags":    list(self.status_tags),
            "inventory":      dict(self.inventory),
            "memory":         dict(self.memory),
        }

    @classmethod
    def from_dict(cls, d: dict) -> "NPCInstance":
        # Build the needs dict: start from defaults, then overlay saved values.
        # Legacy keys (pre-rename) are translated so older saves load cleanly.
        raw_needs: dict[str, float] = d.get("needs", {})
        merged_needs = dict(_NPC_DEFAULT_NEEDS)
        for key, val in raw_needs.items():
            canonical = _NPC_LEGACY_NEED_KEYS.get(key, key)
            if canonical in merged_needs:
                merged_needs[canonical] = val

        return cls(
            uid=d["uid"],
            template_id=d["template_id"],
            name=d["name"],
            class_id=d["class_id"],
            subclass_id=d.get("subclass_id"),
            species=d.get("species", "human"),
            skills=d.get("skills", {}),
            traits=d.get("traits", []),
            aspirations=d.get("aspirations", []),
            needs=merged_needs,
            mood=d.get("mood", 0.5),
            health=d.get("health", 100),
            max_health=d.get("max_health", 100),
            armor=d.get("armor", 0),
            speed=d.get("speed", 10),
            location=d.get("location", "commons"),
            current_job_id=d.get("current_job_id"),
            job_module_uid=d.get("job_module_uid"),
            job_timer=d.get("job_timer", 0),
            job_interrupted=d.get("job_interrupted", False),
            faction_id=d.get("faction_id"),
            owner_id=d.get("owner_id"),
            relationships=d.get("relationships", {}),
            status_tags=d.get("status_tags", []),
            inventory=d.get("inventory", {}),
            memory=d.get("memory", {}),
        )


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

    # Ownership — None = unaffiliated; "player" = player-owned; faction_id = faction ship
    owner_id: str | None = None

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

    def to_dict(self) -> dict:
        return {
            "uid":             self.uid,
            "template_id":     self.template_id,
            "name":            self.name,
            "role":            self.role,
            "faction_id":      self.faction_id,
            "intent":          self.intent,
            "cargo":           dict(self.cargo),
            "passenger_uids":  list(self.passenger_uids),
            "threat_level":    self.threat_level,
            "behavior_tags":   list(self.behavior_tags),
            "owner_id":        self.owner_id,
            "status":          self.status,
            "docked_at":       self.docked_at,
            "ticks_docked":    self.ticks_docked,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "ShipInstance":
        return cls(
            uid=d["uid"],
            template_id=d["template_id"],
            name=d["name"],
            role=d["role"],
            faction_id=d.get("faction_id"),
            intent=d.get("intent", "unknown"),
            cargo=d.get("cargo", {}),
            passenger_uids=d.get("passenger_uids", []),
            threat_level=d.get("threat_level", 0),
            behavior_tags=d.get("behavior_tags", []),
            owner_id=d.get("owner_id"),
            status=d.get("status", "incoming"),
            docked_at=d.get("docked_at"),
            ticks_docked=d.get("ticks_docked", 0),
        )


# ---------------------------------------------------------------------------
# Cargo Hold Settings (per-module inventory configuration)
# ---------------------------------------------------------------------------

@dataclass
class CargoHoldSettings:
    """
    Player-configurable settings for a cargo hold module.

    allowed_types: item types this hold may accept (empty list = allow everything)
    reserved_by_type: fraction of capacity reserved per item type (0.0–1.0)
    priority: higher priority holds are filled first
    """
    allowed_types: list[str] = field(default_factory=list)
    reserved_by_type: dict[str, float] = field(default_factory=dict)
    priority: int = 0

    def allows_type(self, item_type: str) -> bool:
        """Return True if this hold accepts the given item type."""
        return not self.allowed_types or item_type in self.allowed_types

    def to_dict(self) -> dict:
        return {
            "allowed_types":    list(self.allowed_types),
            "reserved_by_type": dict(self.reserved_by_type),
            "priority":         self.priority,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "CargoHoldSettings":
        return cls(
            allowed_types=list(d.get("allowed_types", [])),
            reserved_by_type=dict(d.get("reserved_by_type", {})),
            priority=int(d.get("priority", 0)),
        )


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

    # Ownership — None = station-owned (player); faction_id or ship_uid = belongs to that entity
    owner_id: str | None = None

    # Inventory: item_id -> quantity stored in this module
    inventory: dict[str, int] = field(default_factory=dict)
    # Cargo hold configuration (None if this module is not a cargo hold)
    cargo_settings: CargoHoldSettings | None = None

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

    def to_dict(self) -> dict:
        return {
            "uid":          self.uid,
            "definition_id": self.definition_id,
            "display_name": self.display_name,
            "category":     self.category,
            "occupants":    list(self.occupants),
            "docked_ship":  self.docked_ship,
            "active":       self.active,
            "damage":       self.damage,
            "owner_id":     self.owner_id,
            "inventory":    dict(self.inventory),
            "cargo_settings": self.cargo_settings.to_dict() if self.cargo_settings else None,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "ModuleInstance":
        cs_raw = d.get("cargo_settings")
        return cls(
            uid=d["uid"],
            definition_id=d["definition_id"],
            display_name=d["display_name"],
            category=d["category"],
            occupants=d.get("occupants", []),
            docked_ship=d.get("docked_ship"),
            active=d.get("active", True),
            damage=d.get("damage", 0.0),
            owner_id=d.get("owner_id"),
            inventory=d.get("inventory", {}),
            cargo_settings=CargoHoldSettings.from_dict(cs_raw) if cs_raw else None,
        )


# ---------------------------------------------------------------------------
# Station State
# ---------------------------------------------------------------------------

# Default station resources merged into any loaded save so new resource keys
# (e.g. "water", "ice") are always present. Kept at module level to avoid
# per-call dict construction in from_dict.
_STATION_DEFAULT_RESOURCES: dict[str, float] = {
    "credits": 500.0,
    "food":    100.0,
    "power":   100.0,
    "oxygen":  100.0,
    "parts":    50.0,
    "ice":     200.0,
    "water":   150.0,
}

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
        "water":   150.0,   # potable water — refined from ice or recycled
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

    # Tile map — the spatial floor-plan built in build mode
    tile_map: TileMap = field(default_factory=TileMap)

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

    def to_dict(self) -> dict:
        return {
            "name":               self.name,
            "tick":               self.tick,
            "resources":          dict(self.resources),
            "npcs":               {uid: npc.to_dict()  for uid, npc  in self.npcs.items()},
            "ships":              {uid: ship.to_dict() for uid, ship in self.ships.items()},
            "modules":            {uid: mod.to_dict()  for uid, mod  in self.modules.items()},
            "faction_reputation": dict(self.faction_reputation),
            "active_tags":        list(self.active_tags),
            "policy":             dict(self.policy),
            "event_cooldowns":    dict(self.event_cooldowns),
            "log":                list(self.log),
            "tile_map":           self.tile_map.to_dict(),
        }

    @classmethod
    def from_dict(cls, d: dict) -> "StationState":
        obj = cls(name=d["name"])
        obj.tick               = d.get("tick", 0)
        # Merge saved resources into defaults so new keys (e.g. "water", "ice")
        # are always present even in saves written before they were added.
        merged_resources = dict(_STATION_DEFAULT_RESOURCES)
        merged_resources.update(d.get("resources", {}))
        obj.resources          = merged_resources
        obj.npcs               = {uid: NPCInstance.from_dict(v)   for uid, v in d.get("npcs",    {}).items()}
        obj.ships              = {uid: ShipInstance.from_dict(v)   for uid, v in d.get("ships",   {}).items()}
        obj.modules            = {uid: ModuleInstance.from_dict(v) for uid, v in d.get("modules", {}).items()}
        obj.faction_reputation = d.get("faction_reputation", {})
        obj.active_tags        = set(d.get("active_tags", []))
        obj.policy             = d.get("policy", {})
        obj.event_cooldowns    = d.get("event_cooldowns", {})
        obj.log                = d.get("log", [])
        tile_map_data          = d.get("tile_map")
        obj.tile_map           = TileMap.from_dict(tile_map_data) if tile_map_data else TileMap()
        return obj
