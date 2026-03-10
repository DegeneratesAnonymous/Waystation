"""
Trade System — generates trade manifests for docked ships and handles
player buy/sell interactions.

Design
------
When a ship with trade or smuggle intent docks, the TradeSystem generates
a TradeOffer that records:
  - what the ship is selling (resources + prices)
  - what the ship wants to buy (resources + prices)

Prices are anchored to a base table and modified by faction, negotiation
skill, and market pressure (how many traders are docked simultaneously).

A player uses the 'trade <ship_index_or_uid>' command to see the offer and
execute transactions one resource at a time.
"""

from __future__ import annotations

import logging
import random
from dataclasses import dataclass, field
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.models.instances import StationState, ShipInstance, NPCInstance
    from waystation.models.templates import ShipTemplate, FactionDefinition
    from waystation.core.registry import ContentRegistry

log = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Base price table (credits per unit)
# ---------------------------------------------------------------------------

BASE_PRICES: dict[str, float] = {
    "food":    4.0,
    "parts":   8.0,
    "oxygen":  3.0,
    "ice":     2.0,
    "power":   5.0,   # stored power cells / fuel
}

# Markup range for ships selling goods (they profit on resale)
MIN_SELL_MARKUP = 1.0
MAX_SELL_MARKUP = 1.35

# Discount range for ships buying goods (they profit on resale elsewhere)
MIN_BUY_DISCOUNT = 0.55
MAX_BUY_DISCOUNT = 0.85

# What different ship roles tend to sell/buy
ROLE_SELLS: dict[str, list[str]] = {
    "trader":    ["food", "parts", "ice"],
    "smuggler":  ["parts", "food"],
    "transport": ["food", "ice"],
    "refugee":   [],              # refugees have nothing to sell
    "inspector": [],
    "raider":    [],
    "patrol":    [],
}

ROLE_WANTS: dict[str, list[str]] = {
    "trader":    ["credits"],     # traders just want payment; handled as buying resources
    "smuggler":  ["credits"],
    "transport": ["credits"],
    "refugee":   ["food", "parts"],
    "inspector": [],
    "raider":    [],
    "patrol":    [],
}


# ---------------------------------------------------------------------------
# Trade line item
# ---------------------------------------------------------------------------

@dataclass
class TradeLine:
    resource: str
    price_per_unit: float   # credits per unit
    available: float        # units ship is willing to sell (positive) or buy (negative)

    @property
    def is_selling(self) -> bool:
        return self.available > 0

    @property
    def is_buying(self) -> bool:
        return self.available < 0


# ---------------------------------------------------------------------------
# Trade Offer (attached to a docked ship)
# ---------------------------------------------------------------------------

@dataclass
class TradeOffer:
    ship_uid: str
    ship_name: str
    lines: list[TradeLine] = field(default_factory=list)
    # Track how much of each resource has been traded in this session
    traded: dict[str, float] = field(default_factory=dict)

    def get_sell_lines(self) -> list[TradeLine]:
        """Lines where the ship is selling (player can buy)."""
        return [l for l in self.lines if l.is_selling]

    def get_buy_lines(self) -> list[TradeLine]:
        """Lines where the ship is buying (player can sell)."""
        return [l for l in self.lines if l.is_buying]

    def get_line(self, resource: str) -> TradeLine | None:
        return next((l for l in self.lines if l.resource == resource), None)


# ---------------------------------------------------------------------------
# Trade System
# ---------------------------------------------------------------------------

class TradeSystem:

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

    # ------------------------------------------------------------------
    # Offer generation
    # ------------------------------------------------------------------

    def generate_offer(
        self,
        ship: "ShipInstance",
        station: "StationState",
    ) -> TradeOffer | None:
        """
        Generate a TradeOffer for a freshly docked ship.
        Returns None if the ship role doesn't trade.
        """
        template = self.registry.ships.get(ship.template_id)
        if template is None:
            return None

        # Non-trading roles (no manifests)
        non_trading_roles = {"refugee", "inspector", "raider", "patrol"}
        if ship.role in non_trading_roles:
            return None

        sell_resources = ROLE_SELLS.get(ship.role, [])
        if not sell_resources:
            return None

        # Market pressure: if 2+ traders already docked, prices rise slightly
        n_traders = sum(
            1 for s in station.get_docked_ships()
            if s.uid != ship.uid and s.role in ("trader", "smuggler", "transport")
        )
        pressure_mod = 1.0 + (n_traders * 0.05)

        lines: list[TradeLine] = []

        # Selling lines
        for resource in sell_resources:
            base = BASE_PRICES.get(resource, 5.0)
            # Ships sell at a markup (they want profit)
            markup = random.uniform(MIN_SELL_MARKUP, MAX_SELL_MARKUP) * pressure_mod
            price = round(base * markup, 1)
            # Amount available scales with cargo capacity
            capacity = template.cargo_capacity or 20
            amount = random.randint(max(5, capacity // 4), capacity)
            lines.append(TradeLine(resource=resource, price_per_unit=price, available=float(amount)))

        # Buying lines (negative available means the ship wants to buy)
        # Ships want to buy what they don't have and can resell
        buy_candidates = [r for r in BASE_PRICES if r not in sell_resources]
        n_buy = min(2, len(buy_candidates))
        for resource in random.sample(buy_candidates, n_buy):
            base = BASE_PRICES.get(resource, 5.0)
            # Ships buy at a discount (they want to make money on resale)
            discount = random.uniform(MIN_BUY_DISCOUNT, MAX_BUY_DISCOUNT)
            price = round(base * discount, 1)
            # How much they want
            want_amount = random.randint(10, 40)
            lines.append(TradeLine(resource=resource, price_per_unit=price, available=-float(want_amount)))

        return TradeOffer(ship_uid=ship.uid, ship_name=ship.name, lines=lines)

    # ------------------------------------------------------------------
    # Transaction execution
    # ------------------------------------------------------------------

    def player_buy(
        self,
        offer: TradeOffer,
        resource: str,
        amount: float,
        station: "StationState",
        negotiation_skill: int = 3,
    ) -> tuple[bool, str]:
        """
        Player buys *amount* units of *resource* from the ship.
        Returns (success, message).
        """
        line = offer.get_line(resource)
        if line is None or not line.is_selling:
            return False, f"{resource} is not available from this ship."
        if amount > line.available:
            return False, f"Only {line.available:.0f} units available."

        # Negotiation skill slightly reduces price (each point above 3 → 2% discount, cap 15%)
        skill_discount = min(0.15, max(0.0, (negotiation_skill - 3) * 0.02))
        effective_price = line.price_per_unit * (1.0 - skill_discount)
        total_cost = effective_price * amount

        if station.get_resource("credits") < total_cost:
            return False, f"Insufficient credits (need {total_cost:.0f}, have {station.get_resource('credits'):.0f})."

        station.modify_resource("credits", -total_cost)
        station.modify_resource(resource, amount)
        line.available -= amount
        offer.traded[resource] = offer.traded.get(resource, 0.0) + amount

        msg = (
            f"Purchased {amount:.0f} {resource} for {total_cost:.0f} credits"
            + (f" (negotiated {skill_discount*100:.0f}% off)" if skill_discount > 0 else "")
            + "."
        )
        station.log_event(f"Trade: {msg}")
        return True, msg

    def player_sell(
        self,
        offer: TradeOffer,
        resource: str,
        amount: float,
        station: "StationState",
        negotiation_skill: int = 3,
    ) -> tuple[bool, str]:
        """
        Player sells *amount* units of *resource* to the ship.
        Returns (success, message).
        """
        line = offer.get_line(resource)
        if line is None or not line.is_buying:
            return False, f"This ship is not buying {resource}."
        want = abs(line.available)
        if amount > want:
            return False, f"Ship only wants {want:.0f} units."
        if station.get_resource(resource) < amount:
            return False, f"Insufficient {resource} on station."

        # Negotiation skill slightly increases sell price
        skill_bonus = min(0.15, max(0.0, (negotiation_skill - 3) * 0.02))
        effective_price = line.price_per_unit * (1.0 + skill_bonus)
        total_income = effective_price * amount

        station.modify_resource(resource, -amount)
        station.modify_resource("credits", total_income)
        line.available += amount   # available is negative; moving toward 0
        offer.traded[resource] = offer.traded.get(resource, 0.0) + amount

        msg = (
            f"Sold {amount:.0f} {resource} for {total_income:.0f} credits"
            + (f" (negotiated {skill_bonus*100:.0f}% premium)" if skill_bonus > 0 else "")
            + "."
        )
        station.log_event(f"Trade: {msg}")
        return True, msg

    # ------------------------------------------------------------------
    # Best negotiator query
    # ------------------------------------------------------------------

    def best_negotiator_skill(self, station: "StationState") -> int:
        """Return the highest negotiation skill among on-duty crew."""
        crew = station.get_crew()
        if not crew:
            return 0
        return max(n.skills.get("negotiation", 0) for n in crew)
