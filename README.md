# Waystation
 
A top-down 2D sci-fi space station builder and colony sim in the tradition of RimWorld and Prison Architect. You manage a deep-space waystation — building its infrastructure, simulating the lives of its crew, navigating faction politics across a procedurally generated galaxy, and expanding your reach one sector at a time.
 
---
 
## Overview
 
Waystation is built around the idea that emergent storytelling comes from simulating systems deeply and letting them collide. Your crew are not units — they are people with histories, relationships, needs, and breaking points. Your station is not a menu — it is a physical place where pipes freeze, fires spread, and a poorly placed wall means a room goes unpressurised. The galaxy around you is not a backdrop — it is a living network of factions, resources, and threats that evolves whether you engage with it or not.
 

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
 
## Core Features
 
### Station Building
 
Stations are constructed tile-by-tile on a grid. Rooms are defined explicitly by the player through a room assignment menu. As a convenience, the initial room type can be auto-suggested based on the workbenches and furniture already placed inside, but the player makes the final assignment. Room function matters: a Communications Room needs a Comms Station workbench and may be augmented with a Comms Extender Array for increased range. A Medical Bay needs a Surgery Table. A Hydroponics Bay needs planter tiles, grow lights, and a water connection.
 
Construction is physical — NPCs must be assigned to build, and construction halts if materials run out mid-job. Blueprints must be placed before work begins.
 
Buildings have HP and damage states. Damaged equipment underperforms. Destroyed equipment stops functioning entirely.
 
---
 
### Utility Networks
 
Four physical networks run through your station, each requiring the player to manually route conduits through the tile map:
 
- **Electrical** — powers all workbenches and equipment. Batteries buffer supply against demand.
- **Plumbing** — delivers water to sinks, hydroponics, and medical equipment.
- **Ducting** — circulates air and regulates temperature between rooms.
- **Fuel Lines** — supplies fuel to engines, thrusters, and industrial equipment.
 
Only modules connected to a functioning network receive supply. A severed pipe means a dark room. A broken duct means a cold one. The `NetworkSystem` manages Union-Find graph traversal for connectivity, and the `UtilityNetworkManager` orchestrates per-tick supply/demand resolution across all four types.
 
---
 
### Crew Simulation
 
Crew are the heart of Waystation. Every NPC is a fully simulated person.
 
**Generation** — NPCs are generated from regional data: the species distribution of the system they originate from, the resource scarcity of their home region, and their faction background all influence their starting trait pool, skill tendencies, and personality profile. Station-born NPCs (Next Gen) derive their trait pools from their parents instead.
 
**Acquisition** — Crew can join your station through hiring, rescue, birth, capture, or recruitment from visiting ships.
 
**Skills** — Skills are organised under six ability score branches (STR, DEX, INT, WIS, CHA, END). The full list of skills is still being finalised. Skills level via a sqrt XP curve with a daily soft cap. A crew member also has an overall character level derived from cumulative XP across all skills, which acts as a broad measure of their experience. Every four skill levels, the player chooses an expertise slot unlock — granting access to higher-risk tasks in that skill. Dangerous tasks are hard-locked without the relevant expertise; less dangerous tasks impose a performance penalty.
 
XP accrues through on-the-job practice, reading research materials, and mentoring from higher-skilled crewmates.
 
**Needs** — Eight needs are tracked per NPC: Sleep, Hunger, Thirst, Recreation, Social, Hygiene, Mood, and Health. Depletion rates vary by species and traits. When a need hits crisis threshold, the NPC will abandon their current job to seek satisfaction, and their mood takes a sustained hit.
 
**Mood** — Mood operates on multiple axes (e.g. happy/sad, calm/stressed) and is influenced by needs, traits, room quality, meals, relationships, completed tasks, and witnessed events. A station-wide morale bar gives the player a broad pulse; inspecting an individual NPC reveals a full breakdown of active mood modifiers.
 
**Sanity** — A per-NPC sanity score bounded by WIS modifier (ceiling) and −10 (floor) degrades under prolonged low mood, traumatic events (surgery failures, crew deaths, combat), and consistently unmet needs. Breakdown (≤ −5) triggers erratic behaviour, aggression, work refusal, stat penalties, or self-harm, and halts passive recovery until counselling intervention. Sanity can recover through counselling, time, improved conditions, and medication.
 
**Traits** — NPCs carry traits that apply passive modifiers to skill XP gain, work speed, and mood. Traits are gained and lost dynamically through life events — a crew member who witnesses enough death may develop a trauma trait; a skilled mentor relationship may unlock a positive one. Trait conflicts are downgraded, not deleted.
 
**Tension** — When player decisions conflict with an NPC's traits, or when station conditions deteriorate, that NPC accumulates tension. Tension escalates through four stages: Normal → Disgruntled → WorkSlowdown → DepartureRisk. At DepartureRisk, the NPC announces their intent to leave and the player has a window to intervene. The game issues an alert when tension reaches a critical threshold.
 
---
 
### Social Simulation
 
**Relationships** — NPCs form pairwise relationships that progress through Acquaintance → Friend → Rival/Enemy → Lover → Spouse, with Family relationships possible for station-born NPCs. Some relationship types are species or role-gated. Relationships decay slowly without regular contact. Marriages can trigger at affinity ≥ 60 with Lover status.
 
**Conversations** — Idle NPCs sharing a room will autonomously converse. Outcomes are weighted by current relationship type and push mood modifiers and affinity changes on both participants. Conversations are visible on the tile map via speech indicators, and notable conversations generate event log entries.
 
**Proximity** — Friends sharing a module provide a passive mood boost to each other. Enemies sharing a module impose a mutual mood penalty. Mentors provide a work speed bonus to nearby students.
 
---
 
### Medical System
 
Waystation features a full body-part-based injury simulation across ~70 body part nodes. Wounds bleed, infections accumulate and roll, diseases progress, and pain derives from the sum of active injuries. Vital part destruction causes death.
 
Any NPC can perform medical procedures on another, including wound treatment and bandaging, surgery, amputation, prosthetic and implant fitting, disease treatment, and psychological treatment. The quality of care required depends on the condition — minor wounds need bandaging and rest; complex injuries require surgery at a proper medical workbench.
 
Surgery uses a d20 roll formula (Surgery level + DEX + Medical × 0.5) mapped to five outcome tiers from Critical Success to Critical Failure. Critical Failure triggers a d6 sub-table with results ranging from wrong-part damage to patient death. A failed surgery imposes sanity penalties on the surgeon.
 
Recovery depends on condition severity — some injuries resolve with bed rest, others require ongoing medication and regular medical attention.
 
---
 
### Farming
 
Hydroponic planter tiles support four crop categories: food, medicinal plants, industrial materials, and exotic/luxury goods. Any room with a planter tile can support crops. Growth rate and yield are affected by light level, water supply, temperature, and NPC tending frequency.
 
NPCs perform sow, harvest, and tend tasks autonomously when assigned to a farming role.
 
---
 
### Temperature & Atmosphere
 
Temperature is simulated per-tile. Heaters and coolers scale their power draw dynamically toward a target temperature. Vents equalise adjacent rooms via the ducting network. External space provides a fixed cold baseline.
 
Temperature affects NPC comfort and mood, crop growth rate, equipment malfunction risk, fire spread rate, and vacuum exposure in the event of a hull breach.
 
---
 
### Factions
 
Factions are procedurally generated based on regional conditions, resource availability, and population density. Minor factions control a single system; major factions have expanded across multiple systems. Government type emerges from aggregated NPC trait profiles in the faction's population and shapes how that faction behaves diplomatically, economically, and militarily. The full taxonomy of government types is still being developed — the intent is a robust category system tied directly to the trait system rather than a fixed list of named archetypes.
 
At game start, two factions operate in nearby systems of your starting sector — one friendly, one unfriendly — and both will interact regularly with your station. As you expand into new sectors, new factions generate according to the density of existing factions and the resources available in the area.
 
**Reputation** (−100 to +100) with each faction affects trade prices and access, visitor ship frequency, event eligibility, faction-exclusive technology and items, and hostile escalation and raid frequency.
 
Factions engage in autonomous inter-faction diplomacy — wars, alliances, and territorial changes can occur without player involvement.
 
---
 
### Visitors
 
Ships arrive at your station with procedurally assigned intent. Visitor roles include Traders, Refugees, Inspectors/Tax Collectors, Raiders/Pirates, Diplomats, Smugglers, Passersby, and Medical Emergency vessels. Some arrivals are handled automatically based on your faction policy; others prompt the player for a docking decision. What happens on denial depends on the visitor type — a trader leaves, a raider may escalate.
 
Visitor NPCs physically board the station, move to relevant areas, and interact with crew and facilities. Trade with docked ships uses a full manifest system with negotiated pricing (modified by crew skill, supply/demand, and faction reputation) and supports standing buy/sell orders for automation.
 
---
 
### Research
 
Research is organised as a node graph across five branches: Military, Economics, Sciences, Diplomacy, and Exploration. NPCs assigned to branch terminals actively generate research points. When prerequisite nodes are met, unlocks are stored as Datachips in Data Storage Servers.
 
Research unlocks new buildable workbenches and supporting equipment, crafting recipes, faction diplomacy options, ship and visitor access, NPC skill cap increases, and map range expansion.
 
Relay Nodes allow knowledge sharing between stations using copy semantics with per-branch filter configuration.
 
---
 
### Exploration & The Galaxy
 
**Map System** — The map is a dedicated screen showing two levels of zoom: System view and Sector view. As the player researches and builds antenna infrastructure, more of the surrounding sector grid becomes accessible. Sectors generate endlessly as the player expands outward — the galaxy has no hard boundary, just an ever-receding horizon.
 
**Exploration Pipeline:**
1. A basic antenna grants System-level visibility.
2. A Sector Antenna (researched and built) reveals vague resource data for nearby systems — the three most abundant resources, nothing more.
3. A scout ship dispatched to a system, crewed with an NPC working a Cartography Station workbench, produces an **Exploration Datachip** tied to that system. Installing the chip in a Cartography Server populates full system data on the map. If the chip is lost or destroyed, so is that knowledge.
4. An Interstellar Antenna (further research) grants Exploration Points used to unlock new sectors on a grid. Each unlocked sector still requires a scout ship to populate its data.
 
**POI Types** — Asteroids, derelicts, anomalies, and enemy outposts can appear as Points of Interest. The system is designed to be modular with Workshop support.
 
**Sector Naming** — Sectors follow the convention `[Survey prefix]-[Phenomenon codes] [XX.YY coordinate] "Proper Name"` (e.g. `GSC-NB·OR 22.51 "The Cradle"`), generated procedurally at runtime.
 
**Galaxy Generation** — 80 sectors are generated via seeded Poisson disc sampling in a 100×100 coordinate space, with survey prefixes, phenomenon codes, proper names, and discovery states (Uncharted / Detected / Visited).
 
---
 
### Away Missions
 
Players can manually dispatch crews to Points of Interest, or automate dispatch by assigning Department Heads who will organise away missions independently. Away missions play out as real-time tile-based missions the player can observe. Risks include crew injury or death, equipment damage, mission failure, hostile ambush, and anomaly discovery.
 
Asteroid mining missions generate procedural tile maps and calculate mineral yield on crew return based on crew skill and equipment.
 
---
 
### Events
 
The event system is a data-driven pipeline that fires both on a scheduled tick basis and reactively in response to system state changes — faction reputation shifts, NPC mood crises, station resource thresholds, and more. Events support branching narratives, eligibility conditions, weighted selection, cooldowns, and player choices. Choices are surfaced through the station event log/feed. Events can chain explicitly — a player decision today may trigger a follow-up event days later.
 
---
 
### Jobs & Schedules
 
Players set department priorities and NPCs self-assign tasks within those parameters. Each NPC has a fully customisable personal schedule — the day/night split (work ticks 6–20, rest ticks 21–5) is a default, not a constraint. Critical stations can run 24-hour operations with the right crew rotation.
 
Job assignment within a role is weighted by NPC skill level and department assignment. Mood crises auto-reassign affected NPCs to recreational jobs. Work output scales with the NPC's current mood score.
 
---
 
### Items & Inventory
 
Items are stored in physical containers — furniture objects placed in rooms. Cargo containers in designated cargo holds appear in the station's inventory view. Some containers can be hauled by NPCs; crew with backpacks or bags can carry additional items beyond pocket capacity.
 
Inventory is weight-based, not slot-based. Food and organics are perishable and decay over time.
 
Item categories: Raw Resources, Refined Resources, Exotic Resources, Components, Advanced Components, Exotic Components, Furniture, Workbenches, and Items (wearables, consumables, datachips, and other carried objects).
 
---
 
## Systems at a Glance
 
| System | Status |
|---|---|
| EventSystem | |
| FactionSystem | |
| VisitorSystem | |
| NPCSystem | |
| NeedSystem | |
| SanitySystem | |
| SkillSystem | |
| JobSystem | |
| TradeSystem | |
| InventorySystem | |
| ResourceSystem | |
| BuildingSystem | |
| CombatSystem | |
| TimeSystem | |
| NetworkSystem | |
| UtilityNetworkManager | |
| ResearchSystem | |
| MapSystem | |
| AsteroidMissionSystem | |
| FarmingSystem | |
| TemperatureSystem | |
| MoodSystem | |
| RelationshipRegistry | |
| ConversationSystem | |
| ProximitySystem | |
| TraitSystem | |
| TensionSystem | |
| MedicalTickSystem | |
| SurgerySystem | |
| FactionGovernmentSystem | |
| Horizon Simulation | |
| Save / Load | |
 
---
 
## Roadmap Highlights
 
- **Horizon Simulation** — procedural region ticking, faction activity simulation beyond player visibility, and region discovery
- **Multi-Station Founding** — establish and manage multiple waystations across sectors
- **Planetary Surfaces** — surface missions and ground-side operations
- **Species System** — modular non-human species with distinct traits, skill specialisations, medical needs, and food requirements
- **Designer / Asset Editor** — full in-game editor for clothing, furniture, tiles, and animations with per-save template library
 
---

All game content lives in JSON files under `UnityProject/Assets/StreamingAssets/data/` — zero hardcoded game data.

```
data/
├── buildables/    # Constructable station structures, terminals, and utility network nodes
├── classes/       # NPC class attribute and skill ranges
├── crops/         # Crop definitions (growth stages, light/temperature requirements, yield)
├── events/        # Event definitions (faction, arrival, incident, crisis)
├── expertise/     # NPC expertise and specialisation pools (skill-gated passive bonuses and capability unlocks)
├── factions/      # Faction definitions, diplomacy profiles, behavior tags
├── items/         # Tradeable items (weapons, food, medicine, contraband, …)
├── jobs/          # Job definitions with resource and need effects
├── missions/      # Away-mission templates (mining runs, trade routes, patrol circuits)
├── modules/       # Station module types with resource effects and capacity
├── npcs/          # NPC class templates (skills, traits, starting equipment)
│   └── lineages/  # NPC trait lineage definitions (condition-based lineage pressure rules)
├── research/      # Research node definitions (branch, prerequisites, cost, unlock tags)
├── rooms/         # Room type definitions (workbench bonuses, greenhouse climate rules)
├── ships/         # Ship templates (role, cargo, threat level)
├── skills/        # NPC skill definitions (id, display name, governing attribute) — 27 skills
├── trait_pools/   # Trait pool definitions controlling condition-based trait acquisition
└── traits/        # NPC trait definitions (positive/negative status effects and modifiers)
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

Clothing and hair atlases use **neutral-tone master atlases** accompanied by companion `_mask.png` files. The `NpcApparel.shader` performs mask-keyed tinting at runtime using the `FEATURE_SHADER_RECOLOUR` keyword — each atlas JSON includes `colour_slots` and `mask_atlas` fields describing which mask regions map to which colour slot. Department colour bindings (`ColourSource.DeptColour`) resolve live via `DepartmentRegistry` so the entire crew can be recoloured without re-baking sprites.

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
├── atlases/               # Pre-generated NPC sprite PNG atlases + JSON sidecars (incl. _mask.png files)
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
│   │   │   ├── Core/          # GameManager, ContentRegistry, FeatureFlags, MiniJSON
│   │   │   ├── Models/        # Templates (definitions) and Instances (runtime state)
│   │   │   │                  # Includes MedicalDefinitions, MedicalRuntime, SolarSystemModels, SectorData
│   │   │   ├── Systems/       # One file per game system (50+ systems)
│   │   │   │                  # Includes MedicalTickSystem, SurgerySystem, HumanBodyTree,
│   │   │   │                  # TreatmentActions, SanitySystem, SkillSystem, NeedSystem,
│   │   │   │                  # SolarSystemGenerator, GalaxyGenerator, BeautyScorer,
│   │   │   │                  # DepartmentRegistry, TaskEligibilityResolver, VisitorSystem
│   │   │   ├── UI/            # GameHUD (IMGUI), SystemMapController, AssetEditorController,
│   │   │   │                  # BuildMenuController, OverlayModeController, MainMenuManager
│   │   │   ├── View/          # StationRoomView, TileAtlas, StarfieldView, UIRing, CameraController
│   │   │   └── World/         # World-level controllers (if present)
│   │   └── StreamingAssets/
│   │       └── data/          # JSON game content (see Data-Driven Architecture)
│   └── Packages/              # Unity package manifest
├── docs/                      # Development workflow guides and agent prompts
└── learning-data/             # Reference documents for AI development agents
```

## Development Workflow

This project uses a multi-agent AI development workflow. See [`docs/DEV_WORKFLOW.md`](docs/DEV_WORKFLOW.md) for the full guide and [`docs/dev-agents/`](docs/dev-agents/README.md) for copy-paste agent role prompts.


## Contributing

Pull requests are welcome. For larger changes, please open a **Work Order** issue first (use the issue template) to discuss scope and approach.

---

## License

[MIT](LICENSE)
