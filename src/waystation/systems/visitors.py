"""
Visitor / Docking System — processes ship arrivals and NPC intake.

Responsibilities:
- Generate incoming ships from templates based on regional pressure
- Handle docking decisions (admit / deny / inspect)
- Spawn passenger NPCs for docked ships
- Manage departure timing
- Trigger hostile escalation for denied ships with hostile_if_denied tag
"""

from __future__ import annotations

import logging
import random
from typing import TYPE_CHECKING

from waystation.models.instances import ShipInstance

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import StationState
    from waystation.systems.npcs import NPCSystem
    from waystation.systems.events import EventSystem
    from waystation.models.templates import ShipTemplate

log = logging.getLogger(__name__)


# Ship name generators
_SHIP_PREFIXES = [
    "ISV", "MCV", "RSV", "FSS", "RVS", "DSV", "CRV", "ASV", "STV",
]
_SHIP_NAMES = [
    "Wayward Star", "Iron Margin", "Pale Accord", "Threshold Crossing",
    "Second Dawn", "Drift Signal", "Open Hand", "Cold Meridian",
    "Faded Mark", "Running Tide", "Broken Covenant", "Quiet Passage",
    "Ember Trade", "Scatterlight", "Long Reach", "Veil Runner",
    "Dust Pilgrim", "Low Road", "Amber Frontier", "Signal Lost",
]


def _generate_ship_name() -> str:
    prefix = random.choice(_SHIP_PREFIXES)
    name = random.choice(_SHIP_NAMES)
    return f"{prefix} {name}"


# ---------------------------------------------------------------------------
# Intent mapping from ship role
# ---------------------------------------------------------------------------

ROLE_INTENT_MAP: dict[str, list[tuple[str, float]]] = {
    "trader":    [("trade", 0.85), ("smuggle", 0.15)],
    "refugee":   [("refuge", 0.90), ("transit", 0.10)],
    "inspector": [("inspect", 1.0)],
    "smuggler":  [("smuggle", 0.70), ("trade", 0.30)],
    "raider":    [("raid", 0.80), ("threaten", 0.20)],
    "transport": [("transit", 0.60), ("refuge", 0.40)],
    "patrol":    [("patrol", 1.0)],
}


def _roll_intent(role: str) -> str:
    options = ROLE_INTENT_MAP.get(role, [("unknown", 1.0)])
    intents, weights = zip(*options)
    return random.choices(list(intents), weights=list(weights), k=1)[0]


class VisitorSystem:

    # How many ticks a docked ship stays before departing (range)
    DOCKED_DURATION_RANGE = (3, 12)

    def __init__(self,
                 registry: "ContentRegistry",
                 npc_system: "NPCSystem",
                 event_system: "EventSystem") -> None:
        self.registry = registry
        self.npc_system = npc_system
        self.event_system = event_system

    def tick(self, station: "StationState") -> None:
        """Main per-tick update."""
        self._process_incoming(station)
        self._tick_docked(station)

    # ------------------------------------------------------------------
    # Arrival generation
    # ------------------------------------------------------------------

    def _process_incoming(self, station: "StationState") -> None:
        """Decide if a ship arrives this tick and auto-handle intake if docks are available."""
        if not self._should_generate_arrival(station):
            return

        ship = self._generate_ship(station)
        if ship is None:
            return

        station.add_ship(ship)
        station.log_event(
            f"Incoming: {ship.name} ({ship.role}, intent={ship.intent}, "
            f"threat={ship.threat_label()})"
        )

        # Queue a docking event so the player can respond
        self.event_system.queue_event("event.arrival_generic", context={"ship_uid": ship.uid})

    def _should_generate_arrival(self, station: "StationState") -> bool:
        """Simple probability model — one arrival every ~5 ticks on average."""
        base_prob = 0.20
        # More traffic if station has a trade tag
        if station.has_tag("active_trading"):
            base_prob += 0.10
        # Fewer arrivals if under blockade
        if station.has_tag("under_blockade"):
            base_prob -= 0.15
        return random.random() < max(0.02, base_prob)

    def _generate_ship(self, station: "StationState") -> ShipInstance | None:
        if not self.registry.ships:
            return None

        # Weight ship selection by threat relevance
        templates = list(self.registry.ships.values())
        # Bias away from raiders unless station is in a dangerous region
        dangerous = station.has_tag("dangerous_region")
        weights = []
        for t in templates:
            w = 1.0
            if t.role == "raider":
                w = 2.0 if dangerous else 0.3
            elif t.role == "inspector":
                w = 0.5   # inspectors are rare
            weights.append(w)

        template = random.choices(templates, weights=weights, k=1)[0]
        faction_id = self._pick_faction_for_ship(template, station)
        intent = _roll_intent(template.role)

        return ShipInstance.create(
            template_id=template.id,
            name=_generate_ship_name(),
            role=template.role,
            intent=intent,
            faction_id=faction_id,
            threat_level=template.threat_level,
        )

    def _pick_faction_for_ship(self, template, station: "StationState") -> str | None:
        if template.faction_restrictions:
            return random.choice(template.faction_restrictions)
        if self.registry.factions:
            return random.choice(list(self.registry.factions.keys()))
        return None

    # ------------------------------------------------------------------
    # Docking decisions
    # ------------------------------------------------------------------

    def admit_ship(self, ship_uid: str, station: "StationState") -> bool:
        """
        Admit an incoming ship.
        Returns True if docking succeeded.
        """
        ship = station.ships.get(ship_uid)
        if ship is None or ship.status != "incoming":
            return False

        dock = station.get_available_dock()
        if dock is None:
            station.log_event(f"No dock available for {ship.name} — ship queuing.")
            return False

        ship.status = "docked"
        ship.docked_at = dock.uid
        dock.docked_ship = ship_uid
        station.log_event(f"{ship.name} docked at {dock.display_name}.")

        # Spawn any passengers
        self._spawn_passengers(ship, station)

        # Trade ships bring credits
        if ship.intent == "trade":
            trade_value = random.randint(50, 300)
            station.modify_resource("credits", trade_value)
            station.log_event(f"Trade: +{trade_value} credits from {ship.name}.")

        # Refugee ships may strain food
        if ship.intent == "refuge":
            station.log_event(f"Refugees aboard {ship.name} request food and shelter.")

        return True

    def deny_ship(self, ship_uid: str, station: "StationState") -> None:
        """
        Deny entry to an incoming ship.
        Hostile-tagged ships may escalate.
        """
        ship = station.ships.get(ship_uid)
        if ship is None:
            return

        template = self.registry.ships.get(ship.template_id)
        will_escalate = (
            template is not None and
            "hostile_if_denied" in template.behavior_tags and
            ship.intent in ("raid", "threaten")
        )

        if will_escalate:
            ship.status = "hostile"
            ship.intent = "raid"
            station.log_event(f"{ship.name} denied — ship turning hostile!")
            self.event_system.queue_event("event.hostile_ship", context={"ship_uid": ship_uid})
        else:
            station.log_event(f"{ship.name} denied entry — ship departing.")
            station.remove_ship(ship_uid)

        # Faction rep impact
        if ship.faction_id:
            station.modify_faction_rep(ship.faction_id, -5.0)

    def depart_ship(self, ship_uid: str, station: "StationState") -> None:
        ship = station.ships.get(ship_uid)
        if ship is None:
            return

        # Free dock
        if ship.docked_at:
            dock = station.modules.get(ship.docked_at)
            if dock:
                dock.docked_ship = None

        # Remove any passengers still aboard
        for npc_uid in list(ship.passenger_uids):
            station.remove_npc(npc_uid)

        station.log_event(f"{ship.name} departed.")
        station.remove_ship(ship_uid)

    # ------------------------------------------------------------------
    # Docked ship tick
    # ------------------------------------------------------------------

    def _tick_docked(self, station: "StationState") -> None:
        for ship in list(station.get_docked_ships()):
            ship.ticks_docked += 1
            stay = random.randint(*self.DOCKED_DURATION_RANGE)
            if ship.ticks_docked >= stay:
                self.depart_ship(ship.uid, station)

    # ------------------------------------------------------------------
    # Passenger spawning
    # ------------------------------------------------------------------

    def _spawn_passengers(self, ship: ShipInstance, station: "StationState") -> None:
        template = self.registry.ships.get(ship.template_id)
        if template is None:
            return

        max_pax = template.passenger_capacity
        if max_pax <= 0:
            return

        count = random.randint(0, max_pax)
        npc_template_id = self._pick_visitor_template(ship)
        if npc_template_id is None:
            return

        for _ in range(count):
            npc = self.npc_system.spawn_visitor(npc_template_id, faction_id=ship.faction_id)
            if npc:
                npc.location = ship.docked_at or "docking_bay"
                station.add_npc(npc)
                ship.passenger_uids.append(npc.uid)

        if count > 0:
            log.debug("Spawned %d passenger(s) from %s", count, ship.name)

    def _pick_visitor_template(self, ship: ShipInstance) -> str | None:
        """Map ship role to a visitor NPC template."""
        role_to_template: dict[str, str] = {
            "trader":    "npc.trader",
            "refugee":   "npc.refugee",
            "inspector": "npc.inspector",
            "smuggler":  "npc.smuggler",
            "raider":    "npc.raider",
            "transport": "npc.refugee",
        }
        template_id = role_to_template.get(ship.role)
        # Verify it exists
        if template_id and template_id in self.registry.npcs:
            return template_id
        # Fall back to any available visitor template
        for npc_id in self.registry.npcs:
            if npc_id.startswith("npc."):
                return npc_id
        return None
