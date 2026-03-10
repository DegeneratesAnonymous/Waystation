"""
Event System — tag-driven, data-authored event pipeline.

Responsibilities:
  - evaluate which events are eligible given current station state
  - select events by weighted random on a difficulty-scaled schedule
  - apply outcome effects to the station
  - manage cooldowns and event expiry
  - queue follow-up events
  - surface pending events as player choices

No event logic is hardcoded here. Conditions and outcomes are interpreted
from the data definitions in ContentRegistry.
"""

from __future__ import annotations

import logging
import random
from collections import deque
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any, NamedTuple

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import StationState
    from waystation.models.templates import EventDefinition, EventChoice

log = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Difficulty configuration
# ---------------------------------------------------------------------------

class _DifficultyConfig(NamedTuple):
    min_gap: int                  # minimum ticks between random events
    max_gap: int                  # maximum ticks between random events
    hostile_multiplier: float     # weight multiplier applied to hostile events


# With TICKS_PER_DAY=24 these translate to roughly:
#   easy    → 2 events/day   (gap 10–14 ticks)
#   normal  → 3–4 events/day (gap  6–10 ticks)
#   hard    → 5–6 events/day (gap  4–6  ticks)
#   intense → 7–8 events/day (gap  2–4  ticks), heavier hostile bias
DIFFICULTY_SETTINGS: dict[str, _DifficultyConfig] = {
    "easy":    _DifficultyConfig(10, 14, 0.3),
    "normal":  _DifficultyConfig( 6, 10, 1.0),
    "hard":    _DifficultyConfig( 4,  6, 1.5),
    "intense": _DifficultyConfig( 2,  4, 2.0),
}


# ---------------------------------------------------------------------------
# Pending event (awaiting player input or auto-resolution)
# ---------------------------------------------------------------------------

@dataclass
class PendingEvent:
    definition: "EventDefinition"
    context: dict[str, Any] = field(default_factory=dict)  # runtime context data (ship uid, etc.)
    resolved: bool = False
    chosen_choice_id: str | None = None
    # Game tick when this event expires (0 = never expires)
    expires_at: int = 0


# ---------------------------------------------------------------------------
# Effect Resolver
# ---------------------------------------------------------------------------

class EffectResolver:
    """
    Interprets OutcomeEffect records and applies them to the station.
    New effect types can be registered without touching this class.
    """

    def __init__(self) -> None:
        self._handlers: dict[str, Any] = {}
        self._register_defaults()

    def register(self, effect_type: str, handler) -> None:
        self._handlers[effect_type] = handler

    def apply(self, effect, station: "StationState", context: dict) -> None:
        handler = self._handlers.get(effect.type)
        if handler is None:
            log.warning("Unknown effect type '%s' — skipping.", effect.type)
            return
        try:
            handler(effect, station, context)
        except Exception as e:
            log.error("Error applying effect '%s': %s", effect.type, e)

    def _register_defaults(self) -> None:
        self.register("add_resource",    self._fx_add_resource)
        self.register("remove_resource", self._fx_remove_resource)
        self.register("set_tag",         self._fx_set_tag)
        self.register("clear_tag",       self._fx_clear_tag)
        self.register("modify_rep",      self._fx_modify_rep)
        self.register("log_message",     self._fx_log_message)
        self.register("trigger_event",   self._fx_noop)   # handled by EventSystem
        self.register("spawn_npc",       self._fx_noop)   # handled by NPC system hook
        self.register("spawn_ship",      self._fx_noop)   # handled by Ship system hook

    # ------------------------------------------------------------------
    # Default handlers
    # ------------------------------------------------------------------

    @staticmethod
    def _fx_add_resource(effect, station: "StationState", context: dict) -> None:
        amount = float(effect.value or 0)
        station.modify_resource(effect.target, amount)
        station.log_event(f"Resources: +{amount:.0f} {effect.target}")

    @staticmethod
    def _fx_remove_resource(effect, station: "StationState", context: dict) -> None:
        amount = float(effect.value or 0)
        station.modify_resource(effect.target, -amount)
        station.log_event(f"Resources: -{amount:.0f} {effect.target}")

    @staticmethod
    def _fx_set_tag(effect, station: "StationState", context: dict) -> None:
        station.set_tag(effect.target)
        log.debug("Tag set: %s", effect.target)

    @staticmethod
    def _fx_clear_tag(effect, station: "StationState", context: dict) -> None:
        station.clear_tag(effect.target)
        log.debug("Tag cleared: %s", effect.target)

    @staticmethod
    def _fx_modify_rep(effect, station: "StationState", context: dict) -> None:
        faction_id = effect.target
        delta = float(effect.value or 0)
        new_rep = station.modify_faction_rep(faction_id, delta)
        station.log_event(f"Reputation with {faction_id}: {delta:+.0f} (now {new_rep:.0f})")

    @staticmethod
    def _fx_log_message(effect, station: "StationState", context: dict) -> None:
        station.log_event(str(effect.value or ""))

    @staticmethod
    def _fx_noop(effect, station, context) -> None:
        pass  # handled by owning system via event queue


# ---------------------------------------------------------------------------
# Condition Evaluator
# ---------------------------------------------------------------------------

class ConditionEvaluator:
    """Evaluates ConditionBlock records against live station state."""

    def check_all(self, conditions, station: "StationState", context: dict) -> bool:
        return all(self._check(c, station, context) for c in conditions)

    def _check(self, condition, station: "StationState", context: dict) -> bool:
        result = self._evaluate(condition, station, context)
        return (not result) if condition.negate else result

    def _evaluate(self, condition, station: "StationState", context: dict) -> bool:
        ctype = condition.type

        if ctype == "tag_present":
            return station.has_tag(condition.target)

        if ctype == "resource_above":
            return station.get_resource(condition.target) > float(condition.value or 0)

        if ctype == "resource_below":
            return station.get_resource(condition.target) < float(condition.value or 0)

        if ctype == "faction_rep_above":
            return station.get_faction_rep(condition.target) > float(condition.value or 0)

        if ctype == "faction_rep_below":
            return station.get_faction_rep(condition.target) < float(condition.value or 0)

        if ctype == "crew_count_above":
            return len(station.get_crew()) > int(condition.value or 0)

        if ctype == "visitor_count_above":
            return len(station.get_visitors()) > int(condition.value or 0)

        if ctype == "docked_ships_above":
            return len(station.get_docked_ships()) > int(condition.value or 0)

        if ctype == "tick_above":
            return station.tick > int(condition.value or 0)

        if ctype == "policy_is":
            return station.policy.get(condition.target) == str(condition.value or "")

        if ctype == "always":
            return True

        log.warning("Unknown condition type '%s'", ctype)
        return False


# ---------------------------------------------------------------------------
# Event System
# ---------------------------------------------------------------------------

class EventSystem:
    """
    Manages the full lifecycle of events: selection → presentation → resolution.

    ``difficulty`` controls how often events fire and how frequently hostile
    events are selected.  Valid values: ``"easy"``, ``"normal"``, ``"hard"``,
    ``"intense"``.
    """

    def __init__(self, registry: "ContentRegistry",
                 difficulty: str = "normal") -> None:
        self.registry = registry
        self.resolver = EffectResolver()
        self.evaluator = ConditionEvaluator()

        self._difficulty = difficulty
        if difficulty not in DIFFICULTY_SETTINGS:
            log.warning(
                "Unknown difficulty '%s' — falling back to 'normal'. "
                "Valid values: %s",
                difficulty, list(DIFFICULTY_SETTINGS),
            )
            self._difficulty = "normal"

        # Queue of events pending player interaction
        self._pending: deque[PendingEvent] = deque()

        # Queue of event IDs to fire next tick (from followups / effects)
        self._followup_queue: deque[tuple[str, dict]] = deque()

        # Tick at which the next random event may fire (scheduling)
        self._next_event_tick: int = random.randint(2, 5)

    # ------------------------------------------------------------------
    # Registration hook (other systems register effect handlers here)
    # ------------------------------------------------------------------

    def register_effect_handler(self, effect_type: str, handler) -> None:
        self.resolver.register(effect_type, handler)

    # ------------------------------------------------------------------
    # Tick update
    # ------------------------------------------------------------------

    def tick(self, station: "StationState") -> list[PendingEvent]:
        """
        Called once per game tick.
        Returns new events that need player attention.
        """
        new_events: list[PendingEvent] = []

        # Fire any queued followup events
        while self._followup_queue:
            event_id, context = self._followup_queue.popleft()
            ev = self.registry.events.get(event_id)
            if ev:
                pending = PendingEvent(
                    definition=ev,
                    context=context,
                    expires_at=(station.tick + ev.expires_in) if ev.expires_in > 0 else 0,
                )
                self._pending.append(pending)
                new_events.append(pending)

        # Expire timed-out events (non-hostile only; hostile events never expire)
        for p in self._pending:
            if (not p.resolved
                    and p.expires_at > 0
                    and station.tick >= p.expires_at
                    and not p.definition.hostile):
                station.log_event(f"EVENT MISSED: {p.definition.title}")
                p.resolved = True
                log.debug("Event '%s' expired at tick %d", p.definition.id, station.tick)

        # Don't schedule new random events while a hostile event awaits the player
        hostile_pending = any(
            not p.resolved and p.definition.hostile for p in self._pending
        )
        if hostile_pending:
            return new_events

        # Time-based scheduling: only fire when the scheduled tick is reached
        if station.tick < self._next_event_tick:
            return new_events

        # Attempt to fire a random eligible event this tick
        candidate = self._select_event(station)
        if candidate:
            expires_at = (station.tick + candidate.expires_in) if candidate.expires_in > 0 else 0
            pending = PendingEvent(definition=candidate, expires_at=expires_at)
            self._pending.append(pending)
            new_events.append(pending)

            # Auto-resolve if no choices
            if not candidate.choices:
                self._auto_resolve(pending, station)

        # Schedule the next random event after a difficulty-appropriate gap
        cfg = DIFFICULTY_SETTINGS.get(self._difficulty, DIFFICULTY_SETTINGS["normal"])
        self._next_event_tick = station.tick + random.randint(cfg.min_gap, cfg.max_gap)

        return new_events

    # ------------------------------------------------------------------
    # Selection
    # ------------------------------------------------------------------

    def _select_event(self, station: "StationState") -> "EventDefinition | None":
        cfg = DIFFICULTY_SETTINGS.get(self._difficulty, DIFFICULTY_SETTINGS["normal"])
        eligible = []
        weights = []

        for ev in self.registry.events.values():
            if ev.weight <= 0:
                continue   # weight-0 events are queue-only, never randomly selected
            if not self._is_eligible(ev, station):
                continue
            w = ev.weight * (cfg.hostile_multiplier if ev.hostile else 1.0)
            eligible.append(ev)
            weights.append(w)

        if not eligible:
            return None

        chosen = random.choices(eligible, weights=weights, k=1)[0]
        return chosen

    def _is_eligible(self, ev: "EventDefinition", station: "StationState") -> bool:
        # Cooldown check
        ready_at = station.event_cooldowns.get(ev.id, 0)
        if station.tick < ready_at:
            return False

        # Tag checks
        for tag in ev.required_tags:
            if not station.has_tag(tag):
                return False
        for tag in ev.excluded_tags:
            if station.has_tag(tag):
                return False

        # Trigger conditions
        if not self.evaluator.check_all(ev.trigger_conditions, station, {}):
            return False

        return True

    # ------------------------------------------------------------------
    # Resolution
    # ------------------------------------------------------------------

    def resolve_choice(self,
                       pending: PendingEvent,
                       choice_id: str,
                       station: "StationState") -> None:
        """Player has selected a choice — apply its outcomes."""
        ev = pending.definition
        choice = next((c for c in ev.choices if c.id == choice_id), None)
        if choice is None:
            log.warning("Choice '%s' not found in event '%s'", choice_id, ev.id)
            return

        # Check choice conditions
        if not self.evaluator.check_all(choice.conditions, station, pending.context):
            station.log_event(f"Choice '{choice.label}' is not available.")
            return

        self._apply_outcomes(list(choice.outcomes), station, pending.context)

        if choice.followup_event:
            self._followup_queue.append((choice.followup_event, pending.context))

        self._finish_event(ev, pending, station)

    def _auto_resolve(self, pending: PendingEvent, station: "StationState") -> None:
        """Auto-resolve an event that has no player choices."""
        ev = pending.definition
        self._apply_outcomes(list(ev.auto_outcomes), station, pending.context)
        for followup_id in ev.followup_events:
            self._followup_queue.append((followup_id, pending.context))
        self._finish_event(ev, pending, station)

    def _apply_outcomes(self,
                        outcomes: list,
                        station: "StationState",
                        context: dict) -> None:
        for effect in outcomes:
            if effect.type == "trigger_event":
                event_id = str(effect.value or effect.target)
                self._followup_queue.append((event_id, context))
            else:
                self.resolver.apply(effect, station, context)

    def _finish_event(self,
                      ev: "EventDefinition",
                      pending: PendingEvent,
                      station: "StationState") -> None:
        pending.resolved = True
        if ev.cooldown > 0:
            station.event_cooldowns[ev.id] = station.tick + ev.cooldown

    # ------------------------------------------------------------------
    # Queue inspection
    # ------------------------------------------------------------------

    def get_pending(self) -> list[PendingEvent]:
        return [p for p in self._pending if not p.resolved]

    def queue_event(self, event_id: str, context: dict | None = None) -> None:
        """External systems can push a specific event."""
        self._followup_queue.append((event_id, context or {}))
