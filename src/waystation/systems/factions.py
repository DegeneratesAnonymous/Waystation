"""
Faction System — independent faction behaviour simulation.

Factions are not just reputation numbers. They expand, contract, react
to each other, and generate regional pressure that shapes visitor traffic,
trade offers, and threat levels.

First-pass: faction reputation tracking + basic inter-faction dynamics.
"""

from __future__ import annotations

import logging
import random
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import StationState
    from waystation.models.templates import FactionDefinition

log = logging.getLogger(__name__)


def _rep_label(rep: float) -> str:
    if rep >= 75:
        return "Allied"
    if rep >= 40:
        return "Friendly"
    if rep >= 10:
        return "Neutral"
    if rep >= -20:
        return "Cautious"
    if rep >= -50:
        return "Hostile"
    return "Enemy"


class FactionSystem:

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

        # Inter-faction relationship state (faction_id -> faction_id -> -1..1)
        # Initialised from FactionDefinition.relationships; mutates over time.
        self._relationships: dict[str, dict[str, float]] = {}

    def initialize(self, station: "StationState") -> None:
        """
        Seed faction reputations and inter-faction relationships from definitions.
        Call once after registry is loaded.
        """
        for faction_id, faction_def in self.registry.factions.items():
            # Player starts neutral with everyone
            if faction_id not in station.faction_reputation:
                station.faction_reputation[faction_id] = 0.0

            # Load authored inter-faction relationships
            self._relationships[faction_id] = dict(faction_def.relationships)

        log.info("Faction system initialized with %d factions.", len(self.registry.factions))

    def tick(self, station: "StationState") -> None:
        """
        Per-tick faction simulation:
        - Factions with 'aggressive' behaviour may slowly decay player rep
        - Allied factions slowly drift player rep up
        - Inter-faction tensions may shift
        """
        if station.tick % 10 != 0:
            return   # only update every 10 ticks to keep it lightweight

        for faction_id, faction_def in self.registry.factions.items():
            self._tick_faction(faction_id, faction_def, station)

    def _tick_faction(self,
                       faction_id: str,
                       faction_def: "FactionDefinition",
                       station: "StationState") -> None:
        rep = station.get_faction_rep(faction_id)
        tags = faction_def.behavior_tags

        # Aggressive factions slowly erode neutral/positive standing
        if "aggressive" in tags and rep > -20:
            station.modify_faction_rep(faction_id, -0.5)

        # Trade-friendly factions nudge rep up if trading
        if "trades_always" in tags and station.has_tag("active_trading"):
            station.modify_faction_rep(faction_id, 0.3)

        # Faction pressure events (simplified — just log)
        if rep < -60 and "raids_weak_stations" in tags and random.random() < 0.1:
            station.log_event(
                f"Intelligence: {faction_def.display_name} raiding parties reported near sector."
            )

    def get_faction_label(self, station: "StationState", faction_id: str) -> str:
        rep = station.get_faction_rep(faction_id)
        return f"{faction_id}: {rep:+.0f} ({_rep_label(rep)})"

    def get_inter_faction_relationship(self, a: str, b: str) -> float:
        return self._relationships.get(a, {}).get(b, 0.0)

    def faction_summary(self, station: "StationState") -> list[str]:
        lines = []
        for faction_id, faction_def in sorted(self.registry.factions.items()):
            rep = station.get_faction_rep(faction_id)
            lines.append(
                f"  {faction_def.display_name:<28} {rep:+6.0f}  {_rep_label(rep)}"
            )
        return lines
