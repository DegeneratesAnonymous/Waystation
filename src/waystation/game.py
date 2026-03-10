"""
Game — orchestrates all systems and runs the main loop.

Initializes the station, loads content, and runs the tick/input cycle.
"""

from __future__ import annotations

import logging
import os
import random
import sys
from pathlib import Path
from typing import Any

from waystation.core.registry import ContentRegistry
from waystation.models.instances import StationState, ModuleInstance
from waystation.systems.events import EventSystem, PendingEvent
from waystation.systems.npcs import NPCSystem
from waystation.systems.resources import ResourceSystem
from waystation.systems.factions import FactionSystem
from waystation.systems.visitors import VisitorSystem
from waystation.systems.jobs import JobSystem, JobRegistry
from waystation.systems import time_system

log = logging.getLogger(__name__)

# Try to use colorama for terminal colour; degrade gracefully if not installed.
try:
    from colorama import init as colorama_init, Fore, Style
    colorama_init(autoreset=True)
    _COLOR = True
except ImportError:
    _COLOR = False
    class Fore:   # type: ignore
        RED = YELLOW = GREEN = CYAN = WHITE = MAGENTA = RESET = ""
    class Style:  # type: ignore
        BRIGHT = DIM = RESET_ALL = ""


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _c(color: str, text: str) -> str:
    if _COLOR:
        return f"{color}{text}{Style.RESET_ALL}"
    return text


def _hr(char: str = "─", width: int = 70) -> str:
    return char * width


def _clear() -> None:
    os.system("cls" if os.name == "nt" else "clear")


# ---------------------------------------------------------------------------
# Game class
# ---------------------------------------------------------------------------

class Game:

    def __init__(self, data_root: Path, mods_root: Path, seed: int | None = None) -> None:
        self.data_root = data_root
        self.mods_root = mods_root
        self.seed = seed or random.randint(0, 999_999)

        random.seed(self.seed)

        self.registry = ContentRegistry()
        self.station: StationState | None = None

        # Systems (created after registry is loaded)
        self.event_system: EventSystem | None = None
        self.npc_system: NPCSystem | None = None
        self.resource_system: ResourceSystem | None = None
        self.faction_system: FactionSystem | None = None
        self.visitor_system: VisitorSystem | None = None
        self.job_registry   = JobRegistry()
        self.job_system: JobSystem | None = None

        self._running = False
        self._pending_events: list[PendingEvent] = []

    # ------------------------------------------------------------------
    # Initialization
    # ------------------------------------------------------------------

    def load(self) -> None:
        """Load all content from disk."""
        self.registry.load_core(self.data_root)
        self.registry.load_mods(self.mods_root)
        self.job_registry.load(self.data_root)

        errors = self.registry.errors()
        if errors:
            print(_c(Fore.YELLOW, f"\n{len(errors)} content error(s) during load:"))
            for e in errors:
                print(_c(Fore.YELLOW, f"  {e}"))

        print(f"\nContent loaded (seed={self.seed}):\n{self.registry.summary()}")

    def new_game(self, station_name: str = "Waystation Alpha") -> None:
        """Create a fresh station and all runtime systems."""
        self.station = StationState(name=station_name)

        # Systems
        self.npc_system      = NPCSystem(self.registry)
        self.resource_system = ResourceSystem(self.registry)
        self.event_system    = EventSystem(self.registry)
        self.faction_system  = FactionSystem(self.registry)
        self.visitor_system  = VisitorSystem(
            self.registry, self.npc_system, self.event_system
        )
        self.job_system = JobSystem(self.job_registry)

        # Wire event effect handlers from other systems
        self.event_system.register_effect_handler(
            "spawn_npc", self._effect_spawn_npc
        )

        # Initialize faction relationships
        self.faction_system.initialize(self.station)

        # Build starter station layout
        self._build_starter_station()

        # Spawn starter crew
        self._spawn_starter_crew()

        print(f"\n{_c(Fore.CYAN, 'Station online:')} {self.station.name}")
        print(f"Crew: {len(self.station.get_crew())} | "
              f"Modules: {len(self.station.modules)}")

    # ------------------------------------------------------------------
    # Starter setup
    # ------------------------------------------------------------------

    def _build_starter_station(self) -> None:
        """Create the initial set of modules."""
        starter_modules = [
            ("module.command_center",  "Command Center",  "utility"),
            ("module.docking_bay_a",   "Docking Bay A",   "dock"),
            ("module.docking_bay_b",   "Docking Bay B",   "dock"),
            ("module.crew_quarters",   "Crew Quarters",   "hab"),
            ("module.med_bay",         "Medical Bay",     "utility"),
            ("module.storage_hold",    "Storage Hold",    "utility"),
            ("module.power_core",      "Power Core",      "utility"),
        ]

        for definition_id, name, category in starter_modules:
            # Use definition from registry if it exists, otherwise stub
            module = ModuleInstance.create(
                definition_id=definition_id,
                display_name=name,
                category=category,
            )
            self.station.add_module(module)

        # Set starter resource levels
        self.station.resources["power"]   = 100.0
        self.station.resources["food"]    = 150.0
        self.station.resources["oxygen"]  = 200.0
        self.station.resources["credits"] = 500.0
        self.station.resources["parts"]   =  60.0
        self.station.resources["ice"]     = 200.0

    def _spawn_starter_crew(self) -> None:
        """Spawn the initial crew from templates."""
        starter = [
            ("npc.security_officer", "crew"),
            ("npc.engineer",         "crew"),
            ("npc.operations",       "crew"),
            ("npc.security_officer", "crew"),
            ("npc.engineer",         "crew"),
        ]

        for template_id, role in starter:
            npc = self.npc_system.spawn_crew_member(template_id)
            if npc:
                self.station.add_npc(npc)
                self.station.log_event(f"{npc.name} ({npc.class_id}) joins the crew.")
            else:
                log.warning("Could not spawn starter crew member from '%s'", template_id)

    # ------------------------------------------------------------------
    # Effect handlers (registered into EventSystem)
    # ------------------------------------------------------------------

    def _effect_spawn_npc(self, effect, station: StationState, context: dict) -> None:
        template_id = str(effect.target or effect.value or "")
        if not template_id:
            return
        npc = self.npc_system.spawn_visitor(template_id)
        if npc:
            station.add_npc(npc)
            station.log_event(f"A new arrival: {npc.name}.")

    # ------------------------------------------------------------------
    # Main loop
    # ------------------------------------------------------------------

    def run(self) -> None:
        """Start the interactive game loop."""
        self._running = True
        print(_c(Fore.GREEN, "\n=== FRONTIER WAYSTATION ==="))
        print("Type 'help' for commands.\n")

        while self._running:
            try:
                self._display_status()
                self._handle_pending_events()
                cmd = input(_c(Fore.CYAN, "\n> ")).strip().lower()
                self._handle_command(cmd)
            except (KeyboardInterrupt, EOFError):
                print("\nExiting.")
                break

    def _tick(self) -> None:
        """Advance one game tick."""
        assert self.station is not None
        self.station.tick += 1

        self.resource_system.tick(self.station)
        self.job_system.tick(self.station)
        self.npc_system.tick(self.station)
        self.faction_system.tick(self.station)
        self.visitor_system.tick(self.station)

        new_events = self.event_system.tick(self.station)
        self._pending_events.extend(new_events)

    # ------------------------------------------------------------------
    # Display
    # ------------------------------------------------------------------

    def _display_status(self) -> None:
        s = self.station
        assert s is not None

        print()
        print(_hr())
        print(
            _c(Fore.CYAN + Style.BRIGHT, f" {s.name}")
            + _c(Style.DIM, f"  |  Tick {s.tick:04d}  |  Seed {self.seed}")
        )
        print(_hr())

        # Resources
        res = "  ".join(
            f"{k}: {_c(Fore.GREEN, str(int(v)))}"
            for k, v in sorted(s.resources.items())
        )
        print(f" Resources  {res}")

        # Crew & visitors
        crew = s.get_crew()
        visitors = s.get_visitors()
        incoming = s.get_incoming_ships()
        docked = s.get_docked_ships()

        avg_mood = self.npc_system.average_crew_mood(s)
        mood_str = (
            _c(Fore.GREEN, "content") if avg_mood > 0.2 else
            _c(Fore.YELLOW, "uneasy") if avg_mood > -0.2 else
            _c(Fore.RED, "distressed")
        )
        print(
            f" Crew: {len(crew)} (mood: {mood_str})  "
            f"Visitors: {len(visitors)}  "
            f"Docked ships: {len(docked)}  "
            f"Incoming: {_c(Fore.YELLOW, str(len(incoming)))}"
        )

        # Tags
        if s.active_tags:
            tags_str = "  ".join(_c(Fore.YELLOW, f"[{t}]") for t in sorted(s.active_tags))
            print(f" Tags: {tags_str}")

        # Recent log
        print()
        print(_c(Style.DIM, " Recent activity:"))
        for entry in s.log[:5]:
            print(_c(Style.DIM, f"  {entry}"))

        # Pending player events
        pending = self.event_system.get_pending()
        if pending:
            print()
            print(_c(Fore.YELLOW, f" {len(pending)} event(s) awaiting your response  (type 'events')"))

    def _handle_pending_events(self) -> None:
        # Auto-clear resolved events
        self._pending_events = [p for p in self._pending_events if not p.resolved]

    # ------------------------------------------------------------------
    # Command handling
    # ------------------------------------------------------------------

    def _handle_command(self, cmd: str) -> None:
        parts = cmd.split()
        verb = parts[0] if parts else ""

        match verb:
            case "tick" | "t":
                count = int(parts[1]) if len(parts) > 1 else 1
                for _ in range(count):
                    self._tick()
                print(f"Ticked {count}x (now tick {self.station.tick})")

            case "auto" | "a":
                count = int(parts[1]) if len(parts) > 1 else 10
                print(f"Running {count} ticks...")
                for _ in range(count):
                    self._tick()
                    if self.event_system.get_pending():
                        print("  Event requires attention — stopping.")
                        break

            case "events" | "e":
                self._show_events()

            case "crew" | "c":
                self._show_crew()

            case "ships" | "s":
                self._show_ships()

            case "factions" | "f":
                self._show_factions()

            case "admit":
                self._cmd_admit(parts)

            case "deny":
                self._cmd_deny(parts)

            case "log" | "l":
                count = int(parts[1]) if len(parts) > 1 else 20
                for entry in self.station.log[:count]:
                    print(f"  {entry}")

            case "modules" | "m":
                self._show_modules()

            case "help" | "h" | "?":
                self._show_help()

            case "quit" | "exit" | "q":
                self._running = False

            case "":
                pass   # just redisplay

            case _:
                print(f"Unknown command '{cmd}'. Type 'help'.")

    # ------------------------------------------------------------------
    # Sub-commands
    # ------------------------------------------------------------------

    def _show_events(self) -> None:
        pending = self.event_system.get_pending()
        if not pending:
            print("No events pending.")
            return

        for i, p in enumerate(pending):
            ev = p.definition
            print()
            print(_c(Fore.YELLOW + Style.BRIGHT, f"  EVENT [{i}]: {ev.title}"))
            print(f"  {ev.description}")
            if ev.choices:
                print("  Choices:")
                for choice in ev.choices:
                    print(f"    [{choice.id}] {choice.label}")
                choice_id = input("  Your choice (or 'skip'): ").strip()
                if choice_id and choice_id != "skip":
                    self.event_system.resolve_choice(p, choice_id, self.station)

    def _show_crew(self) -> None:
        crew = self.station.get_crew()
        if not crew:
            print("No crew.")
            return
        print(f"\n  {'Name':<20} {'Class':<18} {'Mood':<12} {'Needs'}")
        print("  " + "─" * 65)
        for npc in crew:
            needs_str = " ".join(
                f"{k}:{v:.0%}" for k, v in list(npc.needs.items())[:2]
            )
            print(
                f"  {npc.name:<20} {npc.class_id:<18} "
                f"{npc.mood_label():<12} {needs_str}"
            )

    def _show_ships(self) -> None:
        ships = list(self.station.ships.values())
        if not ships:
            print("No ships tracked.")
            return
        print(f"\n  {'Name':<28} {'Role':<12} {'Intent':<10} {'Status':<12} {'Threat'}")
        print("  " + "─" * 72)
        for ship in ships:
            color = Fore.RED if ship.is_hostile() else (
                Fore.YELLOW if ship.status == "incoming" else Fore.WHITE
            )
            print(_c(color,
                f"  {ship.name:<28} {ship.role:<12} {ship.intent:<10} "
                f"{ship.status:<12} {ship.threat_label()}"
            ))

    def _show_factions(self) -> None:
        print()
        for line in self.faction_system.faction_summary(self.station):
            print(line)

    def _show_modules(self) -> None:
        print(f"\n  {'Module':<24} {'Category':<12} {'Status'}")
        print("  " + "─" * 50)
        for module in self.station.modules.values():
            status = "OFFLINE" if not module.active else (
                f"DAMAGED {module.damage:.0%}" if module.damage > 0.0 else "OK"
            )
            dock_str = f" [ship: {module.docked_ship}]" if module.docked_ship else ""
            print(f"  {module.display_name:<24} {module.category:<12} {status}{dock_str}")

    def _cmd_admit(self, parts: list[str]) -> None:
        incoming = self.station.get_incoming_ships()
        if not incoming:
            print("No incoming ships.")
            return
        # admit <index or uid>
        if len(parts) > 1:
            target = parts[1]
            ship = next((s for s in incoming if s.uid == target or str(incoming.index(s)) == target), None)
        else:
            ship = incoming[0]
        if ship:
            self.visitor_system.admit_ship(ship.uid, self.station)
        else:
            print("Ship not found.")

    def _cmd_deny(self, parts: list[str]) -> None:
        incoming = self.station.get_incoming_ships()
        if not incoming:
            print("No incoming ships.")
            return
        if len(parts) > 1:
            target = parts[1]
            ship = next((s for s in incoming if s.uid == target or str(incoming.index(s)) == target), None)
        else:
            ship = incoming[0]
        if ship:
            self.visitor_system.deny_ship(ship.uid, self.station)
        else:
            print("Ship not found.")

    def _show_help(self) -> None:
        print("""
  COMMANDS
  ─────────────────────────────────────────────────────
  tick [n]      — Advance n ticks (default 1)
  auto [n]      — Run n ticks, pausing on events
  events        — Show and respond to pending events
  crew          — Show crew roster and status
  ships         — Show tracked ships
  factions      — Show faction reputation
  modules       — Show station modules
  admit [n]     — Admit incoming ship (by index or uid)
  deny  [n]     — Deny incoming ship
  log [n]       — Show last n log entries (default 20)
  help          — Show this help
  quit          — Exit game
""")
