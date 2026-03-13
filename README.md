# Waystation

A sci-fi space station management game in early development. You command a frontier waystation on the edge of rebuilt space — everyone passes through you, and every decision has consequences.

> *Traders, refugees, military inspectors, raiders, scientists — your choices shape station survival, crew morale, and faction relationships.*

---

## Overview

**Waystation** is a narrative-driven strategy game inspired by *Papers, Please* and *FTL*. There is no global campaign or explicit win condition — the game generates emergent stories through data-driven events and player choices.

- Admit or deny incoming ships based on faction standing, contraband checks, and available docking
- Respond to faction demands, crises, and random incidents — every choice ripples outward
- Manage crew needs (hunger, rest, morale) and assign them to day/night shift jobs
- Balance seven station resources (power, food, oxygen, credits, parts, ice, water)
- Trade with docked ships, negotiate prices, and manage cargo holds
- Survive boarding actions and repair hull damage
- Expand your station by constructing new modules

---

## Quick Start

### Prerequisites

- Python 3.10+
- [pygame-ce](https://pypi.org/project/pygame-ce/) (installed via requirements)

```bash
git clone https://github.com/DegeneratesAnonymous/Waystation.git
cd Waystation
pip install -r requirements.txt
```

### Run the interactive demo *(recommended for first time)*

```bash
python demo.py
```

Runs a scripted 20-tick scenario with narration. Press **Enter** to advance each beat. Uses a fixed seed for reproducibility.

### Run the GUI game

```bash
python main_gui.py
```

| Keybind | Action |
|---|---|
| `Space` / `P` | Pause / unpause |
| `1` / `2` / `4` | Set time multiplier |
| `Ctrl+S` | Quick-save |
| `Q` / `Esc` | Return to main menu |

Optional flags: `--saves-dir PATH`, `--log-level {DEBUG,INFO,WARNING,ERROR}`

### Run the CLI game

```bash
python main.py [--seed SEED] [--station-name NAME] [--log-level LEVEL]
```

---

## Game Systems

| System | Description |
|---|---|
| **EventSystem** | Data-driven event pipeline with branching narratives, eligibility conditions, weighted selection, cooldowns, and player choices that trigger cascading outcomes |
| **FactionSystem** | Six major factions with per-faction reputation (−100 → +100), inter-faction diplomacy, and behavior tags that shape which ships and events appear |
| **VisitorSystem** | Procedural ship arrivals, docking decisions, contraband checks, and faction-based traffic |
| **NPCSystem** | Crew generation from class templates; skills, traits, needs (hunger/rest/social), mood calculation, and skill XP |
| **JobSystem** | Day/night shift assignment with per-job resource and crew-need effects |
| **TradeSystem** | Trade manifests for docked ships, price negotiation (crew skill modifies prices), and cargo execution |
| **InventorySystem** | Per-module cargo holds with capacity limits, type filters, reserved fractions, and perishable decay |
| **ResourceSystem** | Per-tick production and consumption across seven resources; morale scaling |
| **BuildingSystem** | Tile-based procedural station layout, module placement, and wall damage/repair |
| **CombatSystem** | Boarding resolution (attacker vs. defender power + variance), outcome tiers, and crew losses |
| **TimeSystem** | 24-tick day/night cycle (day ticks 6–20: work; night ticks 21–6: rest) |

---

## Factions

| Faction | Profile |
|---|---|
| **Imperial Remnant** | Sends inspectors; demands compliance; hostile to refugees |
| **Merchant League** | Primary trade partner; sends freighters and traders |
| **Raider Clans** | Sends hostiles at low standing; can be appeased or opposed |
| **Archive Order** | Sends scientists; values knowledge and rare items |
| **Refugee Fleets** | Sends colony ships seeking asylum; costs food but grants rep |
| **Frontier Collective** | Independent frontier allies; balanced relationship |

---

## Data-Driven Architecture

All game content lives in YAML files under `data/` — zero hardcoded game data.

```
data/
├── events/        # 50+ event definitions (faction, arrival, incident, crisis)
├── factions/      # Faction definitions, diplomacy profiles, behavior tags
├── modules/       # Station module types with resource effects and capacity
├── npcs/          # NPC class templates (skills, traits, starting equipment)
├── ships/         # Ship templates (role, cargo, threat level)
├── classes/       # NPC class attribute and skill ranges
├── jobs/          # Job definitions with resource and need effects
├── items/         # Tradeable items (weapons, food, medicine, contraband, …)
├── buildables/    # Constructable station structures
└── room_types/    # Room templates for procedural station generation
```

The **ContentRegistry** validates all files against JSON Schema at startup and indexes every definition by ID. **Templates** (static, read-only) are cleanly separated from **Instances** (runtime, mutable, JSON-serializable), enabling a full save/load system.

---

## Modding

Drop YAML files into `mods/<your_mod>/` using the same folder structure as `data/`. Mods are loaded after core content and can add or override any definition — no code changes required.

```
mods/
└── example_mod/
    ├── mod.json              # Manifest (id, display_name, version, load_order, …)
    └── events/
        └── my_event.yaml     # New event using the standard schema
```

The `mods/example_mod/` directory included in this repo demonstrates the full manifest and event format.

---

## Project Structure

```
Waystation/
├── data/                    # All YAML game content (see Data-Driven Architecture)
├── docs/                    # Development workflow guides and agent prompts
├── learning-data/           # Reference documents for AI development agents
├── mods/
│   └── example_mod/         # Reference mod showing the manifest and YAML format
├── src/
│   └── waystation/
│       ├── core/
│       │   ├── game.py              # Game orchestrator and tick loop
│       │   └── registry.py          # ContentRegistry — loads and validates YAML
│       ├── models/
│       │   ├── templates.py         # Static definition types (EventDefinition, etc.)
│       │   ├── instances.py         # Runtime instance types (StationState, NPCInstance, …)
│       │   └── tilemap.py           # Procedural tile-based floor-plan generation
│       ├── systems/                 # One file per game system (see Game Systems)
│       └── ui/
│           ├── game_view.py         # Pygame HUD (floor plan, crew, events, sidebar)
│           └── main_menu.py         # Main menu (new game, load game, settings)
├── UnityProject/            # Unity 6 C# port (architecture complete; scenes TBD)
├── demo.py                  # Scripted 20-tick demo (recommended first run)
├── main.py                  # CLI entry point
├── main_gui.py              # Pygame GUI entry point
└── requirements.txt
```

---

## Unity Port *(in progress)*

A Unity 6 (6000.0 LTS) C# port mirrors the Python architecture — the same Template/Instance data model, the same system-per-file layout, and the same data-driven YAML content (converted to JSON under `UnityProject/Assets/StreamingAssets/data/`). Core systems and UI scaffolding are complete; visual assets and scene wiring are pending.

**Setup:**
1. Install **Unity 6000.0.40f1** (or later 6000.x LTS) via Unity Hub
2. Open `UnityProject/` in Unity Hub
3. Create `Assets/Scenes/MainMenuScene.unity` and `Assets/Scenes/GameScene.unity`
4. Wire `MainMenuManager`, `GameManager`, `ContentRegistry`, and `GameViewController` GameObjects
5. Add both scenes to **File → Build Settings** and press **Play**

---

## Development Workflow

This project uses a multi-agent AI development workflow. See [`docs/DEV_WORKFLOW.md`](docs/DEV_WORKFLOW.md) for the full guide and [`docs/dev-agents/`](docs/dev-agents/README.md) for copy-paste agent role prompts.

---

## Contributing

Pull requests are welcome. For larger changes, please open a **Work Order** issue first (use the issue template) to discuss scope and approach.

---

## License

[MIT](LICENSE)
