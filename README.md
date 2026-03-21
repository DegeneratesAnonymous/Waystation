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
- Research technology across three branches (Military, Economics, Sciences) and dispatch crews on asteroid mining away missions
- Wire up electrical, plumbing, and ducting utility networks — every powered buildable must be connected to a working supply
- Grow food and resources in greenhouse modules through the hydroponics farming system
- Watch crew morale, friendships, rivalries, and relationships evolve through proximity and conversation

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
| **NetworkSystem** | Union-Find graph management for electrical, plumbing, and ducting networks; per-tick supply/demand — producers charge batteries, consumers are energised or starved; filters to completed conduits only |
| **UtilityNetworkManager** | Orchestrates network rebuilds when the station layout changes; dispatches per-tick supply/demand resolution across all three network types |
| **ResearchSystem** | Accumulates research points from crew stationed at branch terminals (Military, Economics, Sciences); auto-unlocks nodes when prerequisites are met; produces Datachips stored in Data Storage Servers |
| **MapSystem** | Procedural Points of Interest (asteroids, derelicts, anomalies) within detection range; range expands with Antenna buildables; map resolution (System → Sector → Quadrant → Galaxy) unlocked via research tags |
| **AsteroidMissionSystem** | Dispatches crews on multi-tick asteroid mining away missions; generates procedural asteroid tile maps; calculates mineral yield on crew return |
| **FarmingSystem** | Hydroponics planter tile lifecycle (sow → grow → harvest); NPC sow/harvest/tend task generation; growth rate gated on light level, water supply, and ambient temperature |
| **TemperatureSystem** | Per-room and per-tile temperature simulation; heaters/coolers dynamically scale power draw toward a target; vents equalise adjacent rooms; planter tiles read temperature each tick |
| **MoodSystem** | 0–100 mood score per crew member drifting downward during waking hours and resetting on sleep; named time-limited modifiers stack additively; crisis state (< 20) clears the task queue until mood recovers |
| **RelationshipRegistry** | Pairwise NPC affinity scores (None → Acquaintance → Friend → Lover → Spouse); slow decay after 7-day inactivity; affinity thresholds trigger relationship-type changes |
| **ConversationSystem** | Idle co-module NPCs autonomously converse; outcome probability weighted by current relationship type; updates affinity and pushes mood modifiers on both participants |
| **ProximitySystem** | Friends sharing a module receive a passive mood boost each tick; enemies share a mood penalty; modifier expires naturally after NPCs separate |

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
├── buildables/    # Constructable station structures, terminals, and utility network nodes
├── classes/       # NPC class attribute and skill ranges
├── crops/         # Crop definitions (growth stages, light/temperature requirements, yield)
├── events/        # Event definitions (faction, arrival, incident, crisis)
├── factions/      # Faction definitions, diplomacy profiles, behavior tags
├── items/         # Tradeable items (weapons, food, medicine, contraband, …)
├── jobs/          # Job definitions with resource and need effects
├── modules/       # Station module types with resource effects and capacity
├── npcs/          # NPC class templates (skills, traits, starting equipment)
├── research/      # Research node definitions (branch, prerequisites, cost, unlock tags)
├── rooms/         # Room type definitions (workbench bonuses, greenhouse climate rules)
└── ships/         # Ship templates (role, cargo, threat level)
```

The **ContentRegistry** loads all JSON files at startup and indexes every definition by ID. **Templates** (static, read-only) are cleanly separated from **Instances** (runtime, mutable, JSON-serializable), enabling a full save/load system.

---

## Modding

Game content can be extended by adding JSON files to `UnityProject/Assets/StreamingAssets/data/` using the same folder structure and schema as the core definitions. New entries are picked up automatically by ContentRegistry at startup.

---

## NPC Sprite System

Waystation uses a **layered NPC sprite system** inspired by RimWorld and Prison Architect. Each NPC is rendered by compositing 9 independent transparent PNG atlas layers via Unity `SpriteRenderer` child hierarchies, enabling runtime-swappable equipment and skin tones without re-exporting art.

### Sprite Atlases

All 9 atlases live in `atlases/`. Each sprite is **32×48 px** of content inside a **34×50 px** slot (1 px transparent padding). The atlases ship in the repository pre-generated; use the HTML generators to regenerate them if needed.

| Atlas | Dimensions | Variants | Layout |
|---|---|---|---|
| `npc_body.png` | 612×50 | 18 (3 types × 6 skin tones) | type-major |
| `npc_face.png` | 136×50 | 4 expressions | sequential |
| `npc_hair.png` | 1020×50 | 30 (5 styles × 6 colours) | style-major |
| `npc_hat.png` | 850×50 | 25 (5 types × 5 colours) | colour-major |
| `npc_shirt.png` | 850×50 | 25 (5 types × 5 colours) | type-major |
| `npc_pants.png` | 680×50 | 20 (4 types × 5 colours) | type-major |
| `npc_shoes.png` | 510×50 | 15 (3 types × 5 colours) | type-major |
| `npc_back.png` | 340×50 | 10 (5 types × 2 colours) | type-major |
| `npc_weapon.png` | 680×50 | 20 (8 weapons + 12 reserved) | sequential |

Each atlas has a matching JSON sidecar (`atlases/*.json`) following the project schema.

### HTML Generators

Source generators live in `generators/`. Open any `.html` file in a browser to preview the sprites and download a fresh PNG + JSON pair. Use `generators/npc_composite_test.html` to preview all 9 layers composited together.

### Unity Integration

Scripts live in `unity/` (copy into your Unity project's `Assets/Scripts/NPC/` folder):

| Script | Type | Purpose |
|---|---|---|
| `NpcAppearance.cs` | ScriptableObject | Stores the visual description of one NPC (body type, skin tone, hair, equipment, etc.) |
| `NpcAtlasRegistry.cs` | ScriptableObject | Holds `Sprite[]` arrays for each layer; exposes `GetBody`, `GetFace`, `GetHair`, `GetHat`, `GetShirt`, `GetPants`, `GetShoes`, `GetBack`, `GetWeapon` accessors |
| `NpcSpriteController.cs` | MonoBehaviour | Applies an `NpcAppearance` to 9 child `SpriteRenderer`s in a single `Apply()` call |
| `Editor/NpcAtlasImporter.cs` | Editor tool | Auto-slices atlas PNGs and populates `NpcAtlasRegistry`; accessible at **Waystation → NPC → Import NPC Atlases** |

#### Sorting Layer Setup

1. Open **Edit → Project Settings → Tags and Layers**
2. Add a sorting layer named **`NPCs`** (above the default tile layers)
3. The `NpcSpriteController` assigns `sortingOrder` values 10–18 automatically

#### sortingOrder Assignments

| Order | Layer |
|---|---|
| 10 | Back item (rendered behind body) |
| 11 | Body |
| 12 | Shoes |
| 13 | Pants |
| 14 | Shirt |
| 15 | Face |
| 16 | Hair |
| 17 | Hat |
| 18 | Weapon |

#### Import Steps

1. Copy the 9 PNG atlases from `atlases/` into `Assets/Art/NPCs/` in the Unity project
2. Open **Waystation → NPC → Import NPC Atlases** in the menu bar
3. The tool slices each atlas at 34×50 slots and populates `Assets/Resources/NpcAtlasRegistry.asset`
4. Assign the registry to each `NpcSpriteController` in the Inspector

#### Unity Import Settings (manual fallback)

- Sprite Mode: **Multiple**
- Filter Mode: **Point (no filter)**
- Pixels Per Unit: **32**
- Alpha Is Transparency: **checked**

---

## Project Structure

```
Waystation/
├── atlases/               # Pre-generated NPC sprite PNG atlases + JSON sidecars
├── generators/            # Browser-based HTML sprite generators (open in browser to re-export)
│   └── npc_composite_test.html  # Visual QA: composite all 9 layers
├── unity/                 # Unity C# scripts (copy into Assets/Scripts/NPC/)
│   ├── NpcAppearance.cs
│   ├── NpcAtlasRegistry.cs
│   ├── NpcSpriteController.cs
│   └── Editor/NpcAtlasImporter.cs
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

## Stubbed Systems

The following systems are defined in the codebase but are not yet fully implemented. They are registered and wired up so that dependent systems compile and run against a stable interface; a dedicated work order is expected for each before the feature is playable.

| System | File(s) | Status | Notes |
|---|---|---|---|
| **Horizon Simulation** | `RegionSimulationStub.cs`, `IRegionSimulation.cs` | No-op stub | `RegionSimulationStub` implements `IRegionSimulation` with empty methods (all log TODO markers). Registered in `GameManager.InitSystems()` as the active implementation. Intended to be replaced by a full Horizon Simulation work order that adds procedural region ticking, horizon expansion, and region discovery. |
| **Faction History** | `RegionSimulationStub.cs` (`FactionHistoryStub` class), `IFactionHistoryProvider.cs` | No-op stub | `FactionHistoryStub` (co-located in `RegionSimulationStub.cs`) implements `IFactionHistoryProvider`; `GetFactionHistory` always returns an empty list and `RecordFactionEvent` does not persist. Registered alongside `RegionSimulationStub` and intended for replacement in the same Horizon Simulation work order. |
| **Visitor NPC Shop & Wander Behaviour** | `NPCTaskQueue.cs` | Immediate-succeed stub | `ShopVisitTask` and `IdleInHangarTask` both exist as named task types but complete immediately without executing any behaviour. Visitor NPCs are spawned and walk to the landing pad (via `CommunicationsTask`), then stand in place. A future Visitor Behaviour work order is expected to wire these tasks to shop modules and wander waypoints. |
| **Faction Government — Pirate Region Mechanics** | `FactionGovernmentSystem.cs` | Partial stub | Pirate faction government aggregation returns `null` instead of a resolved leader; only individual NPC resolution is implemented. Full pirate-region mechanics are deferred to a follow-on work order. |
| **Faction Government — Leader Succession** | `FactionGovernmentSystem.cs` | Partial stub | `SuccessionEvaluator` selects a candidate pool but the auto-selection and promotion logic is not executed; the method exits early with a TODO comment. Deferred to a succession-condition work order. |
| **NPC Tension — Crew Departure** | `TensionSystem.cs` | Partial stub | `TensionSystem` calculates per-NPC departure risk and pushes a mood penalty, but the actual departure attempt is not triggered; the TODO comment notes it requires a crew/roster system to be implemented first. |
| **Trait System — Medical/Therapy Event Removal** | `TraitSystem.cs` | Partial stub | `TriggerEventRemoval()` is defined on the trait system but has no wired triggers; the TODO comment notes that medical/therapy system hooks are required before trait-removal events can fire. |
| **Farming — Fertiliser & Pruning Mechanics** | `FarmingSystem.cs` | Partial stub | The NPC `TendTask` completes without any gameplay effect. A TODO comment marks it as a stub pending fertiliser and pruning mechanics. |
| **Temperature — Duct Integration** | `TemperatureSystem.cs` | Partial stub | Vent processing equalises temperature between adjacent rooms, but integration with the ducting utility network (i.e. duct-driven airflow affecting temperature) is marked as a TODO stub. |
| **Horizon — Resource Flow Recording** | `RegionSystem.cs` | Partial stub | `RegionSystem` has a hook to record resource flows per region, but the actual recording is a TODO pending Horizon Simulation providing real flow data. |
| **Save / Load Game** | `GameHUD.cs`, `MainMenuManager.cs` | UI stub | The **Load Game** button exists in both the main menu and the in-game settings panel but is disabled and labelled *"coming soon"*. Save serialises station resources; load is not yet implemented. |
| **Graphics Settings** | `GameHUD.cs` | UI stub | The Graphics settings panel displays *"Graphics settings coming soon."* with no controls. |
| **Sound Settings** | `GameHUD.cs` | UI stub | The Sound settings panel displays *"Sound settings coming soon."* with no controls. |
| **Template Library — File Picker** | `GameHUD.cs` | UI stub | The **Import** button in the Template Library logs a TODO message instead of opening a file dialog. |
| **Multi-Station Founding** | `AntennaSystem.cs`, `SystemMapController.cs` | Future work | `AntennaSystem` explicitly limits antenna-range bonuses to the home sector because multi-station founding is out of scope; `SystemMapController` contains a matching note. Full multi-station support is deferred to a future work order. |
| **Galaxy Outer-Fringe Survey Density** | `GalaxyGenerator.cs` | Partial stub | Sectors beyond the outer fringe (x > 85) return a `SurveyPrefix.UNK` (Unknown) designation because the density-threshold algorithm that would classify them is not yet implemented. |

---

## Contributing

Pull requests are welcome. For larger changes, please open a **Work Order** issue first (use the issue template) to discuss scope and approach.

---

## License

[MIT](LICENSE)
