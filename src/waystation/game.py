"""
Game — orchestrates all systems and runs the main loop.

Initializes the station, loads content, and runs the tick/input cycle.
"""

from __future__ import annotations

import json
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
from waystation.systems.combat import CombatSystem
from waystation.systems.trade import TradeSystem
from waystation.systems.inventory import InventorySystem
from waystation.systems.building import BuildingSystem
from waystation.systems.comms import CommsSystem
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
# Constants
# ---------------------------------------------------------------------------

# Cost to recruit a visiting NPC as crew
RECRUIT_COST = 150.0

# Parts required for an emergency module repair
REPAIR_PARTS_COST = 10.0

# How much damage is removed per emergency repair action
REPAIR_DAMAGE_AMOUNT = 0.25

# Station policies: key -> (valid_values, description)
STATION_POLICIES: dict[str, tuple[list[str], str]] = {
    "visitor_policy": (["open", "inspect", "restrict"], "How new visitors are processed"),
    "refugee_policy": (["accept", "evaluate", "deny"],  "Response to refugee ships"),
    "trade_stance":   (["active", "passive", "closed"],  "Trade engagement level"),
    "security_level": (["minimal", "standard", "high"],  "Station security posture"),
}


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

    def __init__(self, data_root: Path, mods_root: Path, seed: int | None = None,
                 registry: "ContentRegistry | None" = None,
                 job_registry: "JobRegistry | None" = None) -> None:
        self.data_root = data_root
        self.mods_root = mods_root
        self.seed = seed or random.randint(0, 999_999)

        random.seed(self.seed)

        self.registry     = registry     or ContentRegistry()
        self.job_registry = job_registry or JobRegistry()
        self.station: StationState | None = None

        # Systems (created after registry is loaded)
        self.event_system: EventSystem | None = None
        self.npc_system: NPCSystem | None = None
        self.resource_system: ResourceSystem | None = None
        self.faction_system: FactionSystem | None = None
        self.visitor_system: VisitorSystem | None = None
        self.job_system: JobSystem | None = None
        self.combat_system: CombatSystem | None = None
        self.trade_system: TradeSystem | None = None
        self.inventory_system: InventorySystem | None = None
        self.building_system: BuildingSystem | None = None
        self.comms_system: CommsSystem | None = None

        self._running = False
        self._pending_events: list[PendingEvent] = []

    # ------------------------------------------------------------------
    # Initialization
    # ------------------------------------------------------------------

    def load(self) -> None:
        """Load all content from disk (no-op if registry was injected pre-loaded)."""
        if self.registry.is_loaded():
            return
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
        self.combat_system   = CombatSystem()
        self.trade_system    = TradeSystem(self.registry)
        self.inventory_system = InventorySystem(self.registry)
        self.building_system  = BuildingSystem(self.registry)
        self.comms_system     = CommsSystem()
        self.visitor_system  = VisitorSystem(
            self.registry, self.npc_system, self.event_system,
            trade_system=self.trade_system,
        )
        self.job_system = JobSystem(self.job_registry)

        # Wire event effect handlers from other systems
        self.event_system.register_effect_handler(
            "spawn_npc", self._effect_spawn_npc
        )
        self.event_system.register_effect_handler(
            "resolve_boarding", self._effect_resolve_boarding
        )

        # Initialize faction relationships
        self.faction_system.initialize(self.station)

        # Build starter station layout
        self._build_starter_station()

        # Spawn starter crew
        self._spawn_starter_crew()

        # Seed starter inventory
        self._seed_starter_inventory()

        # Initialize default departments if none set
        if not self.station.departments:
            from waystation.models.instances import Department, _DEFAULT_DEPARTMENTS
            self.station.departments = [Department.from_dict(d) for d in _DEFAULT_DEPARTMENTS]

        print(f"\n{_c(Fore.CYAN, 'Station online:')} {self.station.name}")
        print(f"Crew: {len(self.station.get_crew())} | "
              f"Modules: {len(self.station.modules)}")

    # ------------------------------------------------------------------
    # Save / Load
    # ------------------------------------------------------------------

    def save_game(self, save_path: Path) -> None:
        """Serialize the current game state to a JSON file."""
        assert self.station is not None, "No active game to save."
        save_path = Path(save_path)
        save_path.parent.mkdir(parents=True, exist_ok=True)
        data = {
            "version": 1,
            "seed":    self.seed,
            "station": self.station.to_dict(),
        }
        with open(save_path, "w", encoding="utf-8") as fh:
            json.dump(data, fh, indent=2)
        log.info("Game saved to %s", save_path)

    def load_saved_game(self, save_path: Path) -> None:
        """Restore game state from a JSON save file."""
        save_path = Path(save_path)
        with open(save_path, encoding="utf-8") as fh:
            data = json.load(fh)

        self.seed = data.get("seed", self.seed)
        random.seed(self.seed)

        self.station = StationState.from_dict(data["station"])

        # Re-create all runtime systems (they are stateless relative to station)
        self.npc_system       = NPCSystem(self.registry)
        self.resource_system  = ResourceSystem(self.registry)
        self.event_system     = EventSystem(self.registry)
        self.faction_system   = FactionSystem(self.registry)
        self.combat_system    = CombatSystem()
        self.trade_system     = TradeSystem(self.registry)
        self.inventory_system = InventorySystem(self.registry)
        self.building_system  = BuildingSystem(self.registry)
        self.comms_system     = CommsSystem()
        self.visitor_system   = VisitorSystem(
            self.registry, self.npc_system, self.event_system,
            trade_system=self.trade_system,
        )
        self.job_system = JobSystem(self.job_registry)

        self.event_system.register_effect_handler(
            "spawn_npc", self._effect_spawn_npc
        )
        self.event_system.register_effect_handler(
            "resolve_boarding", self._effect_resolve_boarding
        )

        # Restore inter-faction relationship data from definitions
        self.faction_system.initialize(self.station)

        # Initialize default departments if none saved
        if not self.station.departments:
            from waystation.models.instances import Department, _DEFAULT_DEPARTMENTS
            self.station.departments = [Department.from_dict(dep) for dep in _DEFAULT_DEPARTMENTS]

        log.info("Game loaded from %s (tick %d)", save_path, self.station.tick)
        print(f"\n{_c(Fore.CYAN, 'Station restored:')} {self.station.name}  "
              f"(tick {self.station.tick})")

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
        self.station.resources["water"]   = 150.0

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

    def _seed_starter_inventory(self) -> None:
        """Populate the starter storage hold with a basic starting inventory."""
        assert self.station is not None
        assert self.inventory_system is not None

        # Find the storage hold module
        storage_uid = next(
            (uid for uid, mod in self.station.modules.items()
             if mod.definition_id == "module.storage_hold"),
            None,
        )
        if storage_uid is None:
            return

        starter_items = [
            ("item.ration_pack",    30),
            ("item.food_ration",    20),
            ("item.steel_plate",    50),  # enough for several structure buildables
            ("item.circuit_board",   5),
            ("item.pressure_seal",  10),
            ("item.med_compounds",   5),
            ("item.ammunition_basic", 20),
            ("item.ice",            50),
            ("item.water",          30),
        ]
        for item_id, qty in starter_items:
            if item_id in self.registry.items:
                self.inventory_system.add_item(self.station, storage_uid, item_id, qty)

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

    def _effect_resolve_boarding(self, effect, station: StationState, context: dict) -> None:
        """Resolve a boarding combat when triggered by an event effect."""
        ship_uid = context.get("ship_uid", "")
        ship = station.ships.get(ship_uid)
        if ship is None:
            log.warning("resolve_boarding: ship '%s' not found", ship_uid)
            return
        assert self.combat_system is not None
        outcome = self.combat_system.resolve_boarding(station, ship)
        station.log_event(f"Combat outcome ({outcome.tier}): {outcome.narrative}")
        if outcome.tier in ("repelled_clean", "repelled_damaged"):
            station.log_event(f"{ship.name} withdraws after failed boarding attempt.")
            self.visitor_system.depart_ship(ship.uid, station)
        station.clear_tag("boarding_alert")

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
        self.inventory_system.tick(self.station)
        self.building_system.tick(self.station)
        if self.comms_system:
            self.comms_system.tick(self.station)

        new_events = self.event_system.tick(self.station)
        self._pending_events.extend(new_events)

        # Apply atmosphere/temperature leaks from damaged walls to rooms
        self.station.tile_map.tick_room_environments()

    # ------------------------------------------------------------------
    # Display
    # ------------------------------------------------------------------

    def _display_status(self) -> None:
        s = self.station
        assert s is not None

        time_str = time_system.time_label(s)

        print()
        print(_hr())
        print(
            _c(Fore.CYAN + Style.BRIGHT, f" {s.name}")
            + _c(Style.DIM, f"  |  {time_str}  |  Tick {s.tick:04d}  |  Seed {self.seed}")
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
        morale_mod = self.resource_system.morale_modifier(s)
        mood_str = (
            _c(Fore.GREEN, "content") if avg_mood > 0.2 else
            _c(Fore.YELLOW, "uneasy") if avg_mood > -0.2 else
            _c(Fore.RED, "distressed")
        )
        eff_str = _c(
            Fore.GREEN if morale_mod >= 1.0 else Fore.YELLOW if morale_mod >= 0.85 else Fore.RED,
            f"{morale_mod:.0%}"
        )
        defence_str = _c(Fore.CYAN, self.combat_system.security_strength_label(s))

        print(
            f" Crew: {len(crew)} (mood: {mood_str}, eff: {eff_str})  "
            f"Defence: {defence_str}  "
            f"Visitors: {len(visitors)}  "
            f"Docked: {len(docked)}  "
            f"Incoming: {_c(Fore.YELLOW, str(len(incoming)))}"
        )

        # Trade offers active
        if s.trade_offers:
            offer_names = ", ".join(
                o.ship_name for o in s.trade_offers.values()
            )
            print(f" {_c(Fore.CYAN, 'Trade offers:')} {offer_names}  (use 'trade' to browse)")

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

            case "trade" | "tr":
                self._cmd_trade(parts)

            case "recruit":
                self._cmd_recruit(parts)

            case "repair":
                self._cmd_repair(parts)

            case "policy" | "pol":
                self._cmd_policy(parts)

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
        print(f"\n  {'Name':<20} {'Class':<18} {'Mood':<12} {'Job':<22} {'Key Skills'}")
        print("  " + "─" * 85)
        for npc in crew:
            job_label = self.job_system.get_job_label(npc)
            top_skills = sorted(npc.skills.items(), key=lambda x: -x[1])[:3]
            skills_str = " ".join(f"{k[:4]}:{v}" for k, v in top_skills)
            inj = f" [{npc.injuries}inj]" if npc.injuries else ""
            print(
                f"  {npc.name:<20} {npc.class_id:<18} "
                f"{npc.mood_label():<12} {job_label:<22} {skills_str}{inj}"
            )

    def _show_ships(self) -> None:
        ships = list(self.station.ships.values())
        if not ships:
            print("No ships tracked.")
            return
        print(f"\n  {'#':<4} {'Name':<28} {'Role':<12} {'Intent':<10} {'Status':<12} {'Threat'}")
        print("  " + "─" * 78)
        for i, ship in enumerate(ships):
            color = Fore.RED if ship.is_hostile() else (
                Fore.YELLOW if ship.status == "incoming" else Fore.WHITE
            )
            trade_indicator = " [trade]" if ship.uid in self.station.trade_offers else ""
            print(_c(color,
                f"  {i:<4} {ship.name:<28} {ship.role:<12} {ship.intent:<10} "
                f"{ship.status:<12} {ship.threat_label()}{trade_indicator}"
            ))

    def _show_factions(self) -> None:
        print()
        for line in self.faction_system.faction_summary(self.station):
            print(line)

    def _show_modules(self) -> None:
        print(f"\n  {'Module':<24} {'Category':<12} {'Damage':<10} {'Status'}")
        print("  " + "─" * 60)
        for module in self.station.modules.values():
            if not module.active:
                status_str = _c(Fore.RED, "OFFLINE")
            elif module.damage >= 0.5:
                status_str = _c(Fore.RED, f"DAMAGED {module.damage:.0%}")
            elif module.damage > 0.0:
                status_str = _c(Fore.YELLOW, f"DAMAGED {module.damage:.0%}")
            else:
                status_str = _c(Fore.GREEN, "OK")
            dock_str = f" [ship: {module.docked_ship}]" if module.docked_ship else ""
            dmg_bar = f"{module.damage:.0%}" if module.damage else "—"
            print(f"  {module.display_name:<24} {module.category:<12} {dmg_bar:<10} {status_str}{dock_str}")

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

    def _cmd_trade(self, parts: list[str]) -> None:
        """Browse and execute trades with a docked ship."""
        offers = self.station.trade_offers
        if not offers:
            print("No trade offers available. Admit a trading ship first.")
            return

        # Select offer
        offer_list = list(offers.values())
        if len(parts) > 1:
            try:
                idx = int(parts[1])
                offer = offer_list[idx]
            except (ValueError, IndexError):
                # Try by ship uid
                offer = offers.get(parts[1])
            if offer is None:
                print(f"No trade offer for '{parts[1]}'.")
                return
        elif len(offer_list) == 1:
            offer = offer_list[0]
        else:
            print("Multiple trade offers — specify index:")
            for i, o in enumerate(offer_list):
                print(f"  [{i}] {o.ship_name}")
            return

        best_skill = self.trade_system.best_negotiator_skill(self.station)
        print(f"\n  Trade with {_c(Fore.CYAN, offer.ship_name)}  "
              f"(negotiation skill: {best_skill})\n")

        sell_lines = offer.get_sell_lines()
        buy_lines = offer.get_buy_lines()

        if sell_lines:
            print(f"  {'SELLING':<20} {'Price/unit':<14} {'Available'}")
            print("  " + "─" * 45)
            for line in sell_lines:
                print(f"  {line.resource:<20} {line.price_per_unit:<14.1f} {line.available:.0f}")

        if buy_lines:
            print()
            print(f"  {'BUYING':<20} {'Price/unit':<14} {'Wants'}")
            print("  " + "─" * 45)
            for line in buy_lines:
                print(f"  {line.resource:<20} {line.price_per_unit:<14.1f} {abs(line.available):.0f}")

        print(f"\n  Your credits: {self.station.get_resource('credits'):.0f}")
        print("  Commands: 'buy <resource> <amount>'  'sell <resource> <amount>'  'done'")

        while True:
            try:
                sub = input(_c(Fore.CYAN, "  trade> ")).strip().lower().split()
            except (KeyboardInterrupt, EOFError):
                break
            if not sub or sub[0] in ("done", "exit", "quit", "back"):
                break
            if len(sub) < 3 or sub[0] not in ("buy", "sell"):
                print("  Usage: buy/sell <resource> <amount>")
                continue
            try:
                amount = float(sub[2])
            except ValueError:
                print("  Amount must be a number.")
                continue
            if sub[0] == "buy":
                ok, msg = self.trade_system.player_buy(
                    offer, sub[1], amount, self.station, best_skill
                )
            else:
                ok, msg = self.trade_system.player_sell(
                    offer, sub[1], amount, self.station, best_skill
                )
            color = Fore.GREEN if ok else Fore.RED
            print(_c(color, f"  {msg}"))
            print(f"  Credits now: {self.station.get_resource('credits'):.0f}")

    def _cmd_recruit(self, parts: list[str]) -> None:
        """Recruit a visitor as crew (costs credits)."""
        visitors = self.station.get_visitors()
        if not visitors:
            print("No visitors on station to recruit.")
            return

        if len(parts) < 2:
            print(f"\n  {'#':<4} {'Name':<20} {'Class':<18} {'Faction'}")
            print("  " + "─" * 55)
            for i, v in enumerate(visitors):
                print(f"  {i:<4} {v.name:<20} {v.class_id:<18} {v.faction_id or '—'}")
            print(f"\n  Cost: {RECRUIT_COST:.0f} credits. Usage: recruit <index>")
            return

        try:
            idx = int(parts[1])
            npc = visitors[idx]
        except (ValueError, IndexError):
            print("Invalid visitor index.")
            return

        if self.station.get_resource("credits") < RECRUIT_COST:
            print(f"Not enough credits (need {RECRUIT_COST:.0f}).")
            return

        self.station.modify_resource("credits", -RECRUIT_COST)
        npc.status_tags = [t for t in npc.status_tags if t != "visitor"]
        npc.status_tags.append("crew")
        self.station.log_event(
            f"{npc.name} recruited as crew member ({npc.class_id}). "
            f"Cost: {RECRUIT_COST:.0f} credits."
        )
        print(_c(Fore.GREEN, f"  {npc.name} joins the crew!"))

    def _cmd_repair(self, parts: list[str]) -> None:
        """Directly repair a damaged module using parts."""
        damaged = [m for m in self.station.modules.values() if m.damage > 0.0]
        if not damaged:
            print("No damaged modules.")
            return

        if len(parts) < 2:
            print(f"\n  {'#':<4} {'Module':<24} {'Damage'}")
            print("  " + "─" * 40)
            for i, m in enumerate(damaged):
                print(f"  {i:<4} {m.display_name:<24} {m.damage:.0%}")
            print(f"\n  Cost: {REPAIR_PARTS_COST:.0f} parts per repair. Usage: repair <index>")
            return

        try:
            idx = int(parts[1])
            module = damaged[idx]
        except (ValueError, IndexError):
            print("Invalid module index.")
            return

        if self.station.get_resource("parts") < REPAIR_PARTS_COST:
            print(f"Not enough parts (need {REPAIR_PARTS_COST:.0f}).")
            return

        self.station.modify_resource("parts", -REPAIR_PARTS_COST)
        old_damage = module.damage
        module.damage = max(0.0, module.damage - REPAIR_DAMAGE_AMOUNT)
        if module.damage == 0.0 and not module.active:
            module.active = True
        self.station.log_event(
            f"Emergency repair: {module.display_name} {old_damage:.0%} → {module.damage:.0%}."
        )
        print(_c(Fore.GREEN, f"  Repaired {module.display_name}: damage {old_damage:.0%} → {module.damage:.0%}"))

    def _cmd_policy(self, parts: list[str]) -> None:
        """View or set station policies."""
        if len(parts) == 1:
            # Show all policies
            print(f"\n  {'Policy':<22} {'Value':<12} {'Description'}")
            print("  " + "─" * 65)
            for key, (options, desc) in STATION_POLICIES.items():
                current = self.station.policy.get(key, options[0])
                print(f"  {key:<22} {_c(Fore.CYAN, current):<12} {desc}")
            print(f"\n  Usage: policy set <key> <value>")
            return

        if len(parts) >= 4 and parts[1] == "set":
            key = parts[2]
            value = parts[3]
            if key not in STATION_POLICIES:
                print(f"Unknown policy key '{key}'. Valid: {', '.join(STATION_POLICIES)}")
                return
            valid_values = STATION_POLICIES[key][0]
            if value not in valid_values:
                print(f"Invalid value '{value}' for {key}. Valid: {', '.join(valid_values)}")
                return
            old = self.station.policy.get(key, valid_values[0])
            self.station.policy[key] = value
            # Apply tag effects based on policy
            self._apply_policy_effects(key, value, old)
            self.station.log_event(f"Policy '{key}' changed: {old} → {value}.")
            print(_c(Fore.GREEN, f"  Policy set: {key} = {value}"))
        else:
            print("Usage: policy [set <key> <value>]")

    def _apply_policy_effects(self, key: str, value: str, old_value: str) -> None:
        """Apply station tag changes when policies change."""
        s = self.station
        if key == "trade_stance":
            s.clear_tag("active_trading")
            if value == "active":
                s.set_tag("active_trading")
        elif key == "security_level":
            s.clear_tag("station_guarded")
            if value == "high":
                s.set_tag("station_guarded")
        elif key == "visitor_policy":
            s.clear_tag("inspection_in_progress")
            if value == "inspect":
                s.set_tag("inspection_in_progress")

    def _show_help(self) -> None:
        print("""
  COMMANDS
  ─────────────────────────────────────────────────────────────
  tick [n]             — Advance n ticks (default 1)
  auto [n]             — Run n ticks, pausing on events
  events               — Show and respond to pending events
  crew                 — Show crew roster and status
  ships                — Show tracked ships
  factions             — Show faction reputation
  modules              — Show station modules
  admit [n]            — Admit incoming ship (by index or uid)
  deny  [n]            — Deny incoming ship
  trade [n]            — Browse/execute trades with a docked ship
  recruit [n]          — Recruit a visitor as crew (150 credits)
  repair [n]           — Repair a damaged module (10 parts)
  policy               — View station policies
  policy set <k> <v>   — Set a policy value
  log [n]              — Show last n log entries (default 20)
  help                 — Show this help
  quit                 — Exit game
""")
