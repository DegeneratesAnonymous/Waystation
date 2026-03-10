"""
Combat System — resolves violent engagements aboard and around the station.

Boarding attempts, hostile ship exchanges, and crew defence are all resolved
here using crew skills, ship threat level, and module state.

Resolution model
----------------
* Defender power = sum of security crew combat skills + armed modules
* Attacker power = ship threat_level × 10
* Net power + gaussian jitter → outcome tier

Outcome tiers
-------------
  repelled_clean    — attackers driven off, no casualties, minor parts cost
  repelled_damaged  — attackers driven off, station damage, crew injuries
  partial_defeat    — station looted, crew traumatised, attackers eventually leave
  overrun           — station critically damaged, major resource loss
"""

from __future__ import annotations

import logging
import random
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.models.instances import StationState, NPCInstance, ShipInstance

log = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Outcome dataclass
# ---------------------------------------------------------------------------

@dataclass
class CombatOutcome:
    tier: str                   # repelled_clean / repelled_damaged / partial_defeat / overrun
    narrative: str              # human-readable description
    credits_lost: float = 0.0
    parts_lost: float = 0.0
    food_lost: float = 0.0
    module_damage: float = 0.0  # fractional damage applied to a random dock module
    crew_trauma: float = 0.0    # safety need reduction applied to all crew


# ---------------------------------------------------------------------------
# Outcome templates
# ---------------------------------------------------------------------------

_OUTCOMES: dict[str, list[str]] = {
    "repelled_clean": [
        "Security holds the breach — raiders driven back to their ship. Hull integrity intact.",
        "A sharp, professional defence. The boarding party retreats with heavy losses.",
        "Your security crew closes the breach and locks the airlock. No blood on the decks.",
    ],
    "repelled_damaged": [
        "Boarders repelled after a brutal corridor fight. Two crew injured, docking bay scorched.",
        "The breach is sealed — but not before the docking bay takes hits. Repair crews moving in.",
        "Attackers fall back. The fight leaves blast scarring on the bay walls and one crew member limping.",
    ],
    "partial_defeat": [
        "Raiders push through briefly, grab what they can from storage, then withdraw. Expensive.",
        "The corridor fight goes badly — boarders loot the outer hold before a bulkhead seal forces them off.",
        "Breach contained to Docking Bay A. Raiders strip the cargo bay. Security holds the core.",
    ],
    "overrun": [
        "Overwhelming force — the station is boarded from multiple points. Crew falls back to the command center.",
        "Catastrophic breach. Raiders ransack half the station before departing. The damage will take days to repair.",
        "Security is overwhelmed. The station survives, barely. Everything of value near the docking ring is gone.",
    ],
}


# ---------------------------------------------------------------------------
# Combat System
# ---------------------------------------------------------------------------

class CombatSystem:

    # Gaussian jitter standard deviation — controls how unpredictable fights are
    COMBAT_VARIANCE = 8.0
    # Security crew power scaling
    SECURITY_DEFAULT_SKILL = 2
    SECURITY_POWER_MULTIPLIER = 2.5
    NON_SECURITY_DEFAULT_SKILL = 1
    NON_SECURITY_POWER_MULTIPLIER = 1.0
    # Bonus when a guard is actively posted
    GUARD_POST_BONUS = 10.0
    # Bonus per active security module
    SECURITY_MODULE_BONUS = 5.0

    def resolve_boarding(
        self,
        station: "StationState",
        ship: "ShipInstance",
    ) -> CombatOutcome:
        """
        Resolve a boarding attempt.

        Calculates defender and attacker power, rolls outcome, applies
        resource and module effects, and returns an outcome record.
        """
        defender_power = self._calculate_defender_power(station)
        attacker_power = ship.threat_level * 10

        # Gaussian jitter — keeps outcomes uncertain
        roll = random.gauss(0, self.COMBAT_VARIANCE)
        net = defender_power - attacker_power + roll

        tier = self._net_to_tier(net)
        outcome = self._build_outcome(tier, ship, station)

        self._apply_outcome(outcome, station)
        log.info(
            "Combat resolved: ship=%s threat=%d def_power=%.1f net=%.1f tier=%s",
            ship.name, ship.threat_level, defender_power, net, tier,
        )
        return outcome

    # ------------------------------------------------------------------
    # Power calculation
    # ------------------------------------------------------------------

    def _calculate_defender_power(self, station: "StationState") -> float:
        crew = station.get_crew()
        if not crew:
            return 0.0

        security = [n for n in crew if n.class_id in ("class.security",)]
        non_security = [n for n in crew if n not in security]

        # Security crew contribute full combat skill; others contribute half
        power = sum(
            n.skills.get("combat", self.SECURITY_DEFAULT_SKILL) * self.SECURITY_POWER_MULTIPLIER
            for n in security
        )
        power += sum(
            n.skills.get("combat", self.NON_SECURITY_DEFAULT_SKILL) * self.NON_SECURITY_POWER_MULTIPLIER
            for n in non_security
        )

        # Bonus if station_guarded tag is set (guard on post)
        if station.has_tag("station_guarded"):
            power += self.GUARD_POST_BONUS

        # Security post modules add passive defence
        for module in station.modules.values():
            if module.active and module.category == "security" and module.damage < 0.5:
                power += self.SECURITY_MODULE_BONUS

        return power

    # ------------------------------------------------------------------
    # Tier mapping
    # ------------------------------------------------------------------

    @staticmethod
    def _net_to_tier(net: float) -> str:
        if net >= 12:
            return "repelled_clean"
        if net >= 2:
            return "repelled_damaged"
        if net >= -8:
            return "partial_defeat"
        return "overrun"

    # ------------------------------------------------------------------
    # Outcome construction
    # ------------------------------------------------------------------

    def _build_outcome(
        self,
        tier: str,
        ship: "ShipInstance",
        station: "StationState",
    ) -> CombatOutcome:
        narrative = random.choice(_OUTCOMES[tier])

        # Scale losses by threat level for more dangerous ships
        threat_scale = max(1.0, ship.threat_level / 5.0)

        if tier == "repelled_clean":
            return CombatOutcome(
                tier=tier, narrative=narrative,
                parts_lost=5.0 * threat_scale,
                crew_trauma=0.05,
            )
        if tier == "repelled_damaged":
            return CombatOutcome(
                tier=tier, narrative=narrative,
                parts_lost=15.0 * threat_scale,
                module_damage=0.15,
                crew_trauma=0.15,
            )
        if tier == "partial_defeat":
            return CombatOutcome(
                tier=tier, narrative=narrative,
                credits_lost=100.0 * threat_scale,
                food_lost=20.0 * threat_scale,
                parts_lost=10.0 * threat_scale,
                module_damage=0.25,
                crew_trauma=0.25,
            )
        # overrun
        return CombatOutcome(
            tier=tier, narrative=narrative,
            credits_lost=250.0 * threat_scale,
            food_lost=50.0 * threat_scale,
            parts_lost=25.0 * threat_scale,
            module_damage=0.45,
            crew_trauma=0.4,
        )

    # ------------------------------------------------------------------
    # Apply effects
    # ------------------------------------------------------------------

    def _apply_outcome(self, outcome: CombatOutcome, station: "StationState") -> None:
        if outcome.credits_lost:
            station.modify_resource("credits", -outcome.credits_lost)
        if outcome.parts_lost:
            station.modify_resource("parts", -outcome.parts_lost)
        if outcome.food_lost:
            station.modify_resource("food", -outcome.food_lost)

        # Apply module damage to a random dock module
        if outcome.module_damage > 0:
            dock_modules = [
                m for m in station.modules.values()
                if m.category == "dock" and m.active
            ]
            if dock_modules:
                target = random.choice(dock_modules)
                target.damage = min(1.0, target.damage + outcome.module_damage)
                if target.damage >= 1.0:
                    target.active = False
                    station.log_event(f"{target.display_name} destroyed in combat!")

        # Apply crew trauma (reduce safety need)
        if outcome.crew_trauma > 0:
            for npc in station.get_crew():
                npc.update_needs({"safety": -outcome.crew_trauma})
                npc.recalculate_mood()

    # ------------------------------------------------------------------
    # Convenience
    # ------------------------------------------------------------------

    def security_strength_label(self, station: "StationState") -> str:
        """Human-readable station defence strength."""
        power = self._calculate_defender_power(station)
        if power >= 50:
            return "formidable"
        if power >= 25:
            return "capable"
        if power >= 10:
            return "limited"
        return "minimal"
