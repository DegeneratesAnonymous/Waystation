"""
Frontier Waystation — scripted demo.

Runs a full scenario automatically with narration.
Press Enter to advance each beat.
"""

import sys
import io
import time
from pathlib import Path

# Force UTF-8 output on Windows so box-drawing chars work
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

sys.path.insert(0, str(Path(__file__).parent / "src"))

from waystation.game import Game
from waystation.systems.events import PendingEvent

try:
    from colorama import init, Fore, Style
    init(autoreset=True)
    C = True
except ImportError:
    C = False
    class Fore:
        CYAN = YELLOW = GREEN = RED = MAGENTA = WHITE = RESET = ""
    class Style:
        BRIGHT = DIM = RESET_ALL = ""


def hr(char="─", w=70):
    return char * w

def c(color, text):
    return f"{color}{text}{Style.RESET_ALL}" if C else text

def pause(label=""):
    if label:
        print(c(Style.DIM, f"\n  {label}"))
    input(c(Style.DIM, "  [ press Enter to continue ] "))
    print()

def beat(title):
    print()
    print(c(Fore.CYAN + Style.BRIGHT, hr("═")))
    print(c(Fore.CYAN + Style.BRIGHT, f"  {title}"))
    print(c(Fore.CYAN + Style.BRIGHT, hr("═")))

def narrate(text):
    print()
    print(c(Fore.WHITE, f"  {text}"))

def show_resources(station):
    res = "  ".join(
        f"{k}: {c(Fore.GREEN, str(int(v)))}"
        for k, v in sorted(station.resources.items())
    )
    print(f"  Resources   {res}")

def show_log(station, n=6):
    print(c(Style.DIM, "  Station log:"))
    for entry in station.log[:n]:
        print(c(Style.DIM, f"    {entry}"))

def show_event(pending: PendingEvent):
    ev = pending.definition
    print(c(Fore.YELLOW + Style.BRIGHT, f"  EVENT: {ev.title}"))
    print(f"  {ev.description.strip()}")
    if ev.choices:
        print(c(Style.DIM, "  Choices available:"))
        for ch in ev.choices:
            print(c(Style.DIM, f"    [{ch.id}] {ch.label}"))

def resolve(game, pending: PendingEvent, choice_id: str, label: str):
    print(c(Fore.YELLOW, f"  > CHOICE: {label}"))
    if pending.definition.choices:
        choice = next((c for c in pending.definition.choices if c.id == choice_id), None)
        if choice:
            game.event_system.resolve_choice(pending, choice_id, game.station)
    else:
        pending.resolved = True

def tick_silent(game, n):
    for _ in range(n):
        game._tick()

def drain_pending(game):
    """Auto-resolve anything in the queue so it doesn't block."""
    for p in game.event_system.get_pending():
        if not p.resolved:
            if p.definition.choices:
                game.event_system.resolve_choice(
                    p, p.definition.choices[0].id, game.station
                )
            else:
                p.resolved = True


# ─────────────────────────────────────────────────────────────────────────────

def main():
    print()
    print(c(Fore.CYAN + Style.BRIGHT, hr("═")))
    print(c(Fore.CYAN + Style.BRIGHT, "  FRONTIER WAYSTATION — Interactive Demo"))
    print(c(Fore.CYAN + Style.BRIGHT, hr("═")))
    narrate("You are the commander of a waystation on the edge of rebuilt space.")
    narrate("Civilization is still recovering from the collapse of the old empire.")
    narrate("Everyone has to pass through you.")
    pause("Starting up...")

    # ── BOOT ─────────────────────────────────────────────────────────────────
    beat("BOOT — Loading station systems")

    game = Game(
        data_root=Path(__file__).parent / "data",
        mods_root=Path(__file__).parent / "mods",
        seed=7331,
    )
    game.load()
    game.new_game("Waystation Kael")

    s = game.station
    print()
    print(c(Fore.CYAN, f"  Station:  {s.name}"))
    print(c(Fore.CYAN, f"  Crew:     {len(s.get_crew())} officers"))
    print(c(Fore.CYAN, f"  Modules:  {len(s.modules)} online"))
    print(c(Fore.CYAN, f"  Seed:     {game.seed}  (same seed = identical run)"))
    print()
    show_resources(s)

    pause()

    # ── CREW ROSTER ──────────────────────────────────────────────────────────
    beat("CREW ROSTER — Who runs this station")
    narrate("Each crew member is procedurally generated from a class template.")
    narrate("Skills are rolled from ranges. Traits are drawn from a pool.")
    print()
    print(f"  {'Name':<22} {'Class':<22} {'Skills (top 2)'}")
    print("  " + hr("─", 65))
    for npc in s.get_crew():
        top = sorted(npc.skills.items(), key=lambda x: -x[1])[:2]
        skill_str = "  ".join(f"{k}:{v}" for k, v in top)
        traits = ", ".join(npc.traits[:2])
        print(f"  {npc.name:<22} {npc.class_id:<22} {skill_str}   [{traits}]")

    pause()

    # ── FIRST SHIP ARRIVAL ───────────────────────────────────────────────────
    beat("TICK 1–5 — First ships approach")
    narrate("Time advances. The sector is not empty.")
    narrate("Ships are generated from weighted templates with faction and intent assignment.")

    tick_silent(game, 5)
    drain_pending(game)

    print()
    ships = list(s.ships.values())
    if ships:
        for ship in ships:
            color = Fore.RED if ship.is_hostile() else (
                Fore.YELLOW if ship.status == "incoming" else Fore.GREEN
            )
            print(c(color,
                f"  {ship.name:<30} {ship.role:<12} intent={ship.intent:<10} "
                f"threat={ship.threat_label()}  faction={ship.faction_id or 'none'}"
            ))
    else:
        print("  (no ships yet — quiet sector)")

    print()
    show_log(s, 5)
    pause()

    # ── ADMIT A TRADER ───────────────────────────────────────────────────────
    beat("DOCKING — Admitting a trade vessel")
    narrate("A freighter requests docking clearance. Admitting traders brings credits.")
    narrate("Denying them costs faction reputation. Demanding inspection takes time.")

    # Force a trader ship for the demo
    from waystation.models.instances import ShipInstance
    demo_trader = ShipInstance.create(
        template_id="ship.light_freighter",
        name="MCV Iron Margin",
        role="trader",
        intent="trade",
        faction_id="faction.merchant_league",
        threat_level=1,
    )
    s.add_ship(demo_trader)
    s.log_event("Incoming: MCV Iron Margin (trader, intent=trade, threat=low)")

    print()
    print(c(Fore.YELLOW, f"  INCOMING: MCV Iron Margin"))
    print(c(Style.DIM,   f"  Role: trader | Intent: trade | Faction: Merchant League"))
    print(c(Style.DIM,   f"  Threat: low"))

    pause("Decision: ADMIT")

    credits_before = s.get_resource("credits")
    success = game.visitor_system.admit_ship(demo_trader.uid, s)
    credits_after = s.get_resource("credits")

    print()
    if success:
        print(c(Fore.GREEN, "  Docking clearance granted. MCV Iron Margin is berthed."))
        print(c(Fore.GREEN, f"  Trade revenue: +{credits_after - credits_before:.0f} credits"))
    show_log(s, 4)
    pause()

    # ── FACTION EVENT ────────────────────────────────────────────────────────
    beat("FACTION PRESSURE — Imperial Inspection Demand")
    narrate("The Imperial Remnant still claims authority over registered stations.")
    narrate("An inspection cutter has arrived with a formal compliance order.")
    narrate("How you respond shapes your reputation — and which events you see next.")

    from waystation.models.templates import (
        EventDefinition, EventChoice, OutcomeEffect, ConditionBlock
    )
    # Pull the event directly from the registry
    ev = game.registry.events.get("event.imperial_inspection_demand")
    if ev:
        pending = PendingEvent(definition=ev)
        print()
        show_event(pending)
        pause("Decision: COMPLY FULLY")
        resolve(game, pending, "comply",
                "Comply fully — open all decks to inspection")
        show_log(s, 4)
        print()
        rep = s.get_faction_rep("faction.imperial_remnant")
        print(c(Fore.CYAN,
            f"  Imperial Remnant reputation: {rep:+.0f} "
            f"({'Friendly' if rep > 10 else 'Neutral'})"
        ))
    pause()

    # ── REFUGEE CRISIS ───────────────────────────────────────────────────────
    beat("CRISIS — Refugee Convoy Requesting Asylum")
    narrate("Seven ships. Overcrowded. Low on fuel. Fleeing a Raider blockade.")
    narrate("This is a moral and logistical test. There is no clean answer.")
    narrate("Granting asylum costs food. Refusing costs reputation — and something harder to name.")

    ev2 = game.registry.events.get("event.refugee_wave")
    if ev2:
        pending2 = PendingEvent(definition=ev2)
        print()
        show_event(pending2)
        food_before = s.get_resource("food")
        pause("Decision: GRANT FULL ASYLUM")
        resolve(game, pending2, "grant_asylum",
                "Grant full asylum — open the docks")
        food_after = s.get_resource("food")
        print()
        print(c(Fore.YELLOW, f"  Food consumed: {food_before - food_after:.0f} units"))
        rep_rf = s.get_faction_rep("faction.refugee_fleets")
        rep_fc = s.get_faction_rep("faction.frontier_collective")
        print(c(Fore.GREEN, f"  Refugee Fleets reputation:    {rep_rf:+.0f}"))
        print(c(Fore.GREEN, f"  Frontier Collective reputation: {rep_fc:+.0f}"))
        show_log(s, 5)
    pause()

    # ── HOSTILE INCIDENT ─────────────────────────────────────────────────────
    beat("INCIDENT — Hostile Raider Vessel")
    narrate("A Raider corvette denied entry has gone hostile. Weapons active.")
    narrate("Three choices: negotiate, pay tribute, or fight.")
    narrate("Each shapes your standing with the Raider Clans — and your crew's safety.")

    s.set_tag("boarding_alert")
    ev3 = game.registry.events.get("event.hostile_ship")
    if ev3:
        pending3 = PendingEvent(definition=ev3)
        print()
        show_event(pending3)
        pause("Decision: DEFEND — Alert security, repel boarders")

        # Chain to boarding attempt
        game.event_system.resolve_choice(pending3, "defend", s)
        # Now resolve the boarding attempt
        s.set_tag("boarding_alert")
        ev4 = game.registry.events.get("event.boarding_attempt")
        if ev4:
            pending4 = PendingEvent(definition=ev4)
            print()
            print(c(Fore.RED + Style.BRIGHT, f"  FOLLOWUP EVENT: {ev4.title}"))
            print(f"  {ev4.description.strip()}")
            pause("Decision: FIGHT — Hold the line")
            resolve(game, pending4, "fight", "Hold the line — security engages")

        show_log(s, 6)
    pause()

    # ── RESOURCE PRESSURE ────────────────────────────────────────────────────
    beat("TICK 6–20 — Running the station over time")
    narrate("Time passes. Crew need feeding. Modules consume power.")
    narrate("The station lives or dies on whether you can keep the basics flowing.")

    tick_silent(game, 15)
    drain_pending(game)

    print()
    show_resources(s)
    print()

    # Crew mood
    avg = game.npc_system.average_crew_mood(s)
    mood_label = "content" if avg > 0.2 else "uneasy" if avg > -0.2 else "distressed"
    mood_color = Fore.GREEN if avg > 0.2 else Fore.YELLOW if avg > -0.2 else Fore.RED
    print(f"  Crew mood:  {c(mood_color, mood_label)}  (avg {avg:.2f})")
    print()

    print(f"  {'Name':<22} hunger   rest    mood")
    print("  " + hr("─", 50))
    for npc in s.get_crew():
        h = npc.needs.get("hunger", 1.0)
        r = npc.needs.get("rest", 1.0)
        hc = Fore.GREEN if h > 0.5 else Fore.YELLOW if h > 0.2 else Fore.RED
        rc = Fore.GREEN if r > 0.5 else Fore.YELLOW if r > 0.2 else Fore.RED
        print(
            f"  {npc.name:<22} {c(hc, f'{h:.0%}'):<18} {c(rc, f'{r:.0%}'):<18} {npc.mood_label()}"
        )

    show_log(s, 6)
    pause()

    # ── MOD CONTENT ──────────────────────────────────────────────────────────
    beat("MODDING — Example mod event fires")
    narrate("A mod in /mods/example_mod/ added a new event to the pool.")
    narrate("It loaded automatically alongside core content.")
    narrate("Modders drop files in a folder. That's the entire workflow.")

    ev5 = game.registry.events.get("event.example_mod_mysterious_signal")
    if ev5:
        pending5 = PendingEvent(definition=ev5)
        print()
        print(c(Fore.MAGENTA, f"  Source: mods/example_mod/events/example_event.yaml"))
        show_event(pending5)
        pause("Decision: INVESTIGATE")
        resolve(game, pending5, "investigate",
                "Analyze the signal — deploy comms crew")
        show_log(s, 3)
    pause()

    # ── FINAL STATUS ─────────────────────────────────────────────────────────
    beat("END STATE — Waystation Kael after 20 ticks")

    print()
    print(c(Fore.CYAN, f"  Station: {s.name}  |  Tick {s.tick}"))
    print()
    show_resources(s)
    print()

    print(c(Style.DIM, "  Faction standing:"))
    for fid, faction_def in sorted(game.registry.factions.items()):
        rep = s.get_faction_rep(fid)
        label = (
            "Allied" if rep >= 75 else "Friendly" if rep >= 40 else
            "Neutral" if rep >= 10 else "Cautious" if rep >= -20 else
            "Hostile" if rep >= -50 else "Enemy"
        )
        color = (
            Fore.GREEN if rep >= 40 else
            Fore.WHITE if rep >= -20 else
            Fore.RED
        )
        print(c(color, f"    {faction_def.display_name:<30} {rep:+6.0f}  {label}"))

    print()
    print(c(Style.DIM, "  Active station tags:"))
    for tag in sorted(s.active_tags):
        print(c(Fore.YELLOW, f"    [{tag}]"))

    pause()

    # ── WRAP ─────────────────────────────────────────────────────────────────
    beat("WHAT THIS DEMONSTRATES")
    print("""
  Content pipeline
    All events, factions, NPCs, ships, and modules live in YAML files.
    No event logic is hardcoded. Drop a file in a folder — it loads.

  Template / instance split
    Definitions are read-only. Runtime entities (crew, ships) are instances
    generated from templates with rolled stats and drawn traits.

  Faction system
    Six authored factions with relationships. Reputation shifts based on
    choices. Factions generate different visitor traffic and event pools.

  Event system
    Tag-based eligibility. Weighted selection. Cooldowns. Chained followups.
    Player choices drive outcomes that reshape future event availability.

  NPC simulation
    Needs decay per tick. Mood calculated from needs + traits.
    Skills rolled from class template ranges.

  Modding foundation
    /mods/<mod_id>/mod.json + same folder structure as /data/.
    Core content and mods use identical schemas. Same seed = identical run.
""")
    print(c(Fore.CYAN + Style.BRIGHT, "  Run it yourself:  python main.py --seed 7331"))
    print(c(Fore.CYAN + Style.BRIGHT, "  Different run:    python main.py"))
    print()


if __name__ == "__main__":
    main()
