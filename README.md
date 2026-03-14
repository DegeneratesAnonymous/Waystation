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

- **Unity Hub** with **Unity 6000.0.40f1** (or later 6000.x LTS)

### Opening the project

1. Clone the repository and open **Unity Hub**
2. Click **Add → Add project from disk** and select the `UnityProject/` folder
3. Open the project in Unity 6000.0.40f1 or newer

### Running in the editor

1. Create `Assets/Scenes/MainMenuScene.unity` and `Assets/Scenes/GameScene.unity`
2. Wire `MainMenuManager`, `GameManager`, `ContentRegistry`, and `GameViewController` GameObjects in each scene
3. Add both scenes to **File → Build Settings**
4. Press **Play**

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

All game content lives in JSON files under `UnityProject/Assets/StreamingAssets/data/` — zero hardcoded game data.

```
data/
├── buildables/    # Constructable station structures and furniture
├── classes/       # NPC class attribute and skill ranges
├── events/        # Event definitions (faction, arrival, incident, crisis)
├── factions/      # Faction definitions, diplomacy profiles, behavior tags
├── items/         # Tradeable items (weapons, food, medicine, contraband, …)
├── jobs/          # Job definitions with resource and need effects
├── modules/       # Station module types with resource effects and capacity
├── npcs/          # NPC class templates (skills, traits, starting equipment)
└── ships/         # Ship templates (role, cargo, threat level)
```

The **ContentRegistry** loads all JSON files at startup and indexes every definition by ID. **Templates** (static, read-only) are cleanly separated from **Instances** (runtime, mutable, JSON-serializable), enabling a full save/load system.

---

## Modding

Game content can be extended by adding JSON files to `UnityProject/Assets/StreamingAssets/data/` using the same folder structure and schema as the core definitions. New entries are picked up automatically by ContentRegistry at startup.

---

## Project Structure

```
Waystation/
├── UnityProject/
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── Core/          # GameManager, ContentRegistry, MiniJSON
│   │   │   ├── Models/        # Templates (definitions) and Instances (runtime state)
│   │   │   ├── Systems/       # One file per game system (BuildingSystem, CommsSystem, …)
│   │   │   ├── UI/            # GameHUD (IMGUI overlay with all tabs)
│   │   │   └── View/          # StationRoomView, TileAtlas, StarfieldView
│   │   └── StreamingAssets/
│   │       └── data/          # JSON game content (see Data-Driven Architecture)
│   └── Packages/              # Unity package manifest
├── docs/                      # Development workflow guides and agent prompts
└── learning-data/             # Reference documents for AI development agents
```

## Development Workflow

This project uses a multi-agent AI development workflow. See [`docs/DEV_WORKFLOW.md`](docs/DEV_WORKFLOW.md) for the full guide and [`docs/dev-agents/`](docs/dev-agents/README.md) for copy-paste agent role prompts.

---

## Contributing

Pull requests are welcome. For larger changes, please open a **Work Order** issue first (use the issue template) to discuss scope and approach.

---

## License

[MIT](LICENSE)
