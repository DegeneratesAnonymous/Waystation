"""
Comms System — handles passing ship communications and radio messages.

Every ~2 in-game days (48 ticks at 24 ticks/day), there is a 30% chance
a trade ship will pass by and send a communication to the station.
"""

from __future__ import annotations

import logging
import random
import uuid
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.models.instances import StationState

from waystation.models.instances import CommMessage, ShipInstance

log = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# Ticks per in-game day
TICKS_PER_DAY = 24

# How often to check for a passing ship (in ticks)
_PASSING_SHIP_CHECK_INTERVAL = TICKS_PER_DAY * 2   # every 2 days

# Probability a ship actually passes on that day
_PASSING_SHIP_CHANCE = 0.30

# Ship names for passing trade ships
_TRADE_SHIP_PREFIXES = ["ISV", "MCV", "RTV", "FSS", "CTV"]
_TRADE_SHIP_NAMES = [
    "Ice Runner", "Frost Haul", "Cold Meridian", "Cryogenic Dawn",
    "Glacier Express", "Frozen Passage", "Arctic Reach", "Ice Break",
    "Crystal Wake", "Polar Drift",
]

# Ice quantity range per trade ship
_ICE_QTY_MIN = 50
_ICE_QTY_MAX = 200

# Price range for ice (credits per unit)
_ICE_PRICE_MIN = 1.5
_ICE_PRICE_MAX = 3.5


def _generate_trade_ship_name() -> str:
    prefix = random.choice(_TRADE_SHIP_PREFIXES)
    name   = random.choice(_TRADE_SHIP_NAMES)
    return f"{prefix} {name}"


class CommsSystem:
    """Manages radio communications and passing ship encounters."""

    def __init__(self) -> None:
        self._last_check_tick: int = 0

    def tick(self, station: "StationState") -> None:
        """Called each game tick. Checks for passing ships on schedule."""
        tick = station.tick

        # Only check every _PASSING_SHIP_CHECK_INTERVAL ticks
        if tick - self._last_check_tick < _PASSING_SHIP_CHECK_INTERVAL:
            return

        self._last_check_tick = tick
        self._check_passing_ships(station)

    def _check_passing_ships(self, station: "StationState") -> None:
        """Roll for a passing trade ship sending a message."""
        if random.random() > _PASSING_SHIP_CHANCE:
            return

        ship_name = _generate_trade_ship_name()
        ice_qty   = random.randint(_ICE_QTY_MIN, _ICE_QTY_MAX)
        ice_price = round(random.uniform(_ICE_PRICE_MIN, _ICE_PRICE_MAX), 2)

        # Create a passing ship entry (not docked, just passing by)
        ship_uid = str(uuid.uuid4())[:8]
        ship = ShipInstance(
            uid=ship_uid,
            template_id="ship.trade_vessel",
            name=ship_name,
            role="trader",
            intent="trade",
            cargo={"item.ice": ice_qty},
            status="incoming",
        )
        station.add_ship(ship)

        # Tag it as a passing ship (not seeking to dock)
        ship.behavior_tags = ["passing_by"]

        # Build message
        body = (
            f"Greetings from {ship_name}. We are a trade vessel currently on a "
            f"transit route and have {ice_qty} units of Ice available for sale at "
            f"{ice_price:.2f} credits/unit. "
            f"Total cost: {ice_qty * ice_price:.0f} credits. "
            f"If interested, we can dispatch a shuttle immediately. "
            f"Reply to arrange a delivery."
        )

        msg = CommMessage(
            uid=str(uuid.uuid4())[:8],
            subject=f"Trade offer from {ship_name}: Ice ×{ice_qty}",
            body=body,
            sender_name=ship_name,
            sender_type="trade_ship",
            ship_uid=ship_uid,
            tick=station.tick,
            response_options=[
                {"label": "Sure, let's trade",   "action": "accept_trade",
                 "ice_qty": ice_qty, "ice_price": ice_price, "ship_uid": ship_uid},
                {"label": "No thank you",         "action": "decline",
                 "ship_uid": ship_uid},
                {"label": "Come back later",      "action": "later",
                 "ship_uid": ship_uid},
            ],
        )
        station.add_message(msg)
        station.log_event(f"Incoming transmission from {ship_name}")
        log.info("Passing trade ship %s sent comms message (ice×%d @ %.2f)",
                 ship_name, ice_qty, ice_price)

    def accept_trade(self, station: "StationState", msg: CommMessage,
                     option: dict) -> None:
        """Player accepts the trade. Triggers shuttle delivery of ice."""
        ship_uid  = option.get("ship_uid") or msg.ship_uid
        ice_qty   = int(option.get("ice_qty", 0))
        ice_price = float(option.get("ice_price", 2.0))
        total_cost = round(ice_qty * ice_price, 2)

        # Check if player has enough credits
        credits = station.get_resource("credits")
        if credits < total_cost:
            station.log_event(
                f"Insufficient credits to buy Ice×{ice_qty} "
                f"(need {total_cost:.0f}, have {credits:.0f})"
            )
            return

        # Deduct credits
        station.modify_resource("credits", -total_cost)

        # Deliver ice to the hangar (or first available cargo module)
        self._deliver_goods(station, {"item.ice": ice_qty})

        # Remove passing ship
        if ship_uid and ship_uid in station.ships:
            station.remove_ship(ship_uid)

        station.log_event(
            f"Trade accepted: Ice×{ice_qty} delivered for {total_cost:.0f} credits"
        )
        log.info("Trade accepted: ice×%d, cost=%.0f", ice_qty, total_cost)

    def decline_trade(self, station: "StationState", msg: CommMessage,
                      option: dict) -> None:
        """Player declines the trade."""
        ship_uid = option.get("ship_uid") or msg.ship_uid
        if ship_uid and ship_uid in station.ships:
            station.remove_ship(ship_uid)
        station.log_event(f"Declined trade offer from {msg.sender_name}")

    def later_trade(self, station: "StationState", msg: CommMessage,
                    option: dict) -> None:
        """Player asks the ship to come back later — ship leaves, no penalty."""
        ship_uid = option.get("ship_uid") or msg.ship_uid
        if ship_uid and ship_uid in station.ships:
            station.remove_ship(ship_uid)
        station.log_event(
            f"Asked {msg.sender_name} to come back later. They acknowledged."
        )

    def _deliver_goods(self, station: "StationState",
                       cargo: dict[str, int]) -> None:
        """Deliver goods to the station's hangar or best cargo module."""
        # Find a hangar module first, then any cargo module
        target_mod = None
        for mod in station.modules.values():
            if mod.category == "hangar":
                target_mod = mod
                break
        if target_mod is None:
            for mod in station.modules.values():
                if mod.category == "cargo":
                    target_mod = mod
                    break
        if target_mod is None:
            # Fallback: use storage_hold or any utility module
            for mod in station.modules.values():
                if "storage" in mod.definition_id or mod.category == "utility":
                    target_mod = mod
                    break

        if target_mod is None:
            # Last resort: add directly to station resources
            for item_id, qty in cargo.items():
                if item_id == "item.ice":
                    station.modify_resource("ice", qty)
                else:
                    log.warning("No module found to deliver %s×%d; item dropped.", item_id, qty)
            station.log_event("Goods delivered directly to station stores.")
            return

        # Add to target module's inventory
        for item_id, qty in cargo.items():
            current = target_mod.inventory.get(item_id, 0)
            target_mod.inventory[item_id] = current + qty

        station.log_event(
            f"Shuttle delivered goods to {target_mod.display_name}. "
            f"NPCs will haul to storage."
        )

    def reply_to_message(self, station: "StationState", msg: CommMessage,
                         action: str, option: dict) -> None:
        """Dispatch the appropriate action for a message reply."""
        msg.read    = True
        msg.replied = action

        if action == "accept_trade":
            self.accept_trade(station, msg, option)
        elif action == "decline":
            self.decline_trade(station, msg, option)
        elif action == "later":
            self.later_trade(station, msg, option)
        else:
            log.warning("Unknown comms action: %s", action)
