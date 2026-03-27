# Waystation
 
A top-down 2D sci-fi space station builder and colony sim in the tradition of RimWorld and Prison Architect. You manage a deep-space waystation — building its infrastructure, simulating the lives of its crew, navigating faction politics across a procedurally generated galaxy, and expanding your reach one sector at a time.
 
---
 
## Overview
 
Waystation is built around the idea that emergent storytelling comes from simulating systems deeply and letting them collide. Your crew are not units — they are people with histories, relationships, needs, and breaking points. Your station is not a menu — it is a physical place where pipes freeze, fires spread, and a poorly placed wall means a room goes unpressurised. The galaxy around you is not a backdrop — it is a living network of factions, resources, and threats that evolves whether you engage with it or not.
 
There is no hard win condition. There is no countdown. The game ends when you decide it does, or when your station becomes too broken to sustain — but even then, you keep playing.
 
---
 
## Starting Out
 
Every playthrough begins with a scenario selection. Your starting scenario determines your initial crew composition, station layout, available resources, ship complement, and the disposition of the two factions in your starting sector. Each scenario presents a different challenge profile — a well-stocked commercial hub, a salvaged derelict, a military outpost running low on supplies — and seeds the kind of story that will emerge from it.
 
---
 
## Core Features
 
### Station Building
 
Stations are constructed tile-by-tile on a grid. Rooms are defined explicitly by the player through a room assignment menu. As a convenience, the initial room type can be auto-suggested based on the workbenches and furniture already placed inside, but the player makes the final call.
 
Room function is determined by what is placed inside it. A room becomes a Medical Bay when a Surgery Table is installed. A Communications Room needs a Comms Station workbench and may be augmented by a Comms Extender Array for range bonuses. Supporting equipment in the same room as a workbench provides passive bonuses to that workbench's output.
 
Construction is physical — blueprints must be placed, NPCs must be assigned, materials must be present, and time must pass. Construction halts if materials run out mid-job and resumes when restocked. Buildings have HP. Damaged equipment underperforms; destroyed equipment stops functioning entirely.
 
---
 
### Utility Networks
 
Four physical utility networks must be manually routed through the station using tile-map conduits:
 
- **Electrical** — powers all workbenches and equipment; batteries buffer supply against demand
- **Plumbing** — delivers water to sinks, hydroponics, and medical equipment
- **Ducting** — circulates air and regulates temperature between rooms
- **Fuel Lines** — supplies fuel to engines, thrusters, and industrial equipment
 
Only modules connected to a functioning supply network receive that resource. A severed conduit cuts off everything downstream.
 
---
 
### Crew Simulation
 
Every crew member is a fully simulated person.
 
**Acquisition** — Crew join through hiring, rescue, birth, capture, or recruitment from visitors.
 
**Generation** — First-generation NPCs are built from regional data: the species distribution of their home system, sector-wide species weights, and regional resource conditions all influence their starting trait pools and personality profiles. Station-born NPCs derive their trait pools from their parents instead, making lineage a meaningful long-term force.
 
**Species** — A modular species system allows each species to carry distinct trait pools, skill specialisations, personality likelihoods, and different medical and food requirements. Only humans are currently defined; the architecture supports extension.
 
**Skills** — Skills are divided into Simple Skills (single ability score) and Advanced Skills (composite formula). Six ability scores govern all skill resolution: STR, DEX, INT, WIS, CHA, END. Character level is derived from cumulative XP across all skills. Every four levels in a skill, the player chooses an expertise slot unlock that gates access to higher-risk tasks.
 
**Needs** — Eight needs are tracked per NPC: Sleep, Hunger, Thirst, Recreation, Social, Hygiene, Mood, and Health. Depletion rates vary by species and traits. Crisis needs force behaviour changes and accumulate pressure on mood and sanity.
 
**Mood** — Multi-axis mood system influenced by needs, traits, room quality, relationships, and life events. A station morale bar gives a broad reading; individual NPC inspection reveals a full modifier breakdown.
 
**Sanity** — Degrades under prolonged low mood, trauma, and consistently unmet needs. Breakdown triggers erratic behaviour, aggression, and work refusal. Recovery requires counselling, medication, and improved conditions.
 
**Traits** — Gained and lost dynamically through lived experience. Trait conflicts downgrade rather than delete. Parental traits apply probabilistic pressure to next-gen NPC trait pools.
 
**Tension** — Tracks conflict between an NPC's traits and their station conditions. Escalates through Normal, Disgruntled, WorkSlowdown, and DepartureRisk. At DepartureRisk the NPC announces intent to leave and the player has a window to intervene.
 
**Death** — When a crew member dies, nearby crew suffer a mood penalty, close relationships trigger events, and the body must be physically handled and removed from the station.
 
---
 
### Social Simulation
 
**Relationships** — NPCs form pairwise relationships tracked as affinity scores, progressing through Acquaintance, Friend (including Mentor/Student as a sub-type), Rival/Enemy, Lover, Spouse, and Family. Some types are species or role-gated.
 
**Conversations** — Idle NPCs sharing a room converse autonomously. Conversation quality begins with a raw CHA check that either resolves the interaction or opens skill-based follow-up options. Notable conversations appear in the event log.
 
**Proximity** — Friends sharing a space boost each other's mood. Enemies penalise it. Mentors provide a work speed bonus to nearby students.
 
**Mentoring** — Mentor/Student bonds form automatically when a high-skill NPC repeatedly works alongside a lower-skill one. Mentoring XP quality scales with the teacher's skill level, Communication skill, relationship affinity, and current mood.
 
---
 
### Departments and Ranks
 
Departments are fully player-defined. The player creates, names, renames, and removes departments freely. Every job is assigned to a department. Department Heads can be appointed to automate department-level decisions including away mission dispatch.
 
NPC rank is derived from overall character level. Higher-ranked crew unlock access to leadership roles and department head appointments.
 
---
 
### Medical System
 
Full body-part-based injury and illness simulation across approximately 70 body part nodes. Per-tick pipeline covers bleeding, infection, disease progression, wound healing, pain derivation, consciousness, vital part death checks, functional penalties, and scar evaluation.
 
Any NPC can perform medical procedures: wound treatment, surgery, amputation, prosthetics, disease treatment, and psychological therapy. Surgery uses a d20 roll mapped to five outcome tiers from Critical Success to Critical Failure, with a d6 sub-table on the worst results.
 
---
 
### Farming
 
Planter tiles support four crop categories: food, medicinal plants, industrial materials, and exotic/luxury goods. Growth depends on light, water, temperature, and tending frequency. Neglected crops accumulate risk of blight (spreads between planters) and pest infestation (requires NPC intervention). Layout matters — tightly packed planters are efficient but vulnerable to spreading blight.
 
---
 
### Temperature and Atmosphere
 
Per-tile temperature simulation. Heaters and coolers scale dynamically toward target temperatures. Vents equalise adjacent rooms through the ducting network. Temperature affects NPC comfort, crop yield, equipment malfunction risk, fire spread, and vacuum exposure from hull breaches.
 
---
 
### Factions
 
Factions are procedurally generated from regional conditions and resource availability. Government type emerges from aggregated NPC trait profiles via a voting system across nine defined types: Republic, Technocracy, Oligarchy, Theocracy, Dictatorship, Corporate State, Warlord State, Pirate Collective, and Vassalized.
 
Reputation with each faction affects trade access, visitor frequency, event eligibility, exclusive tech and items, and raid thresholds. Factions simulate autonomous diplomacy. Governments can shift during play through population drift, internal crisis, or external pressure.
 
---
 
### Visitors
 
Ships arrive with procedurally assigned intent: Trader, Refugee, Inspector/Tax Collector, Raider/Pirate, Diplomat, Smuggler, Medical Emergency, or Passerby. Visitor crew physically board the station, move through it, and interact with crew and facilities.
 
---
 
### Fleet Management
 
The player owns and manages a fleet of ships acquired through purchase or construction. Ship roles include Scout/Exploration, Mining/Resource Extraction, Combat/Defence, Transport/Cargo Hauling, and Diplomatic Courier. Ships crew with NPCs who carry their full simulation with them on missions.
 
---
 
### Research
 
A node graph across five branches: Military, Economics, Sciences, Diplomacy, and Exploration. Completed nodes produce Datachips stored in Data Storage Servers. Research unlocks new buildables, crafting recipes, diplomacy options, ship and visitor access, skill cap increases, and map range expansion.
 
---
 
### Exploration and the Galaxy
 
Two map views: System and Sector. The galaxy generates endlessly as the player expands. Full system data requires a scout ship crewed with a cartographer who produces an Exploration Datachip. Lose the chip, lose the data — unless the player has researched and built backup capability.
 
---
 
### Crafting
 
Workbench-based recipe execution. Recipes are unlocked through research and executed at the relevant workbench by assigned NPCs. Recipe quality and speed scale with the NPC's Crafting/Manufacturing skill.
 
---
 
### Economy
 
Credits flow from trade, faction contracts, mission yields, visitor fees, and sale of manufactured goods. Supply and demand influence prices; faction reputation modifies trade terms.
 
---
 
### Events
 
Data-driven pipeline firing on schedules and reactively to system state. Events carry eligibility conditions, weighted selection, cooldowns, branching choices, and cascading outcomes surfaced through the station event log. Events chain explicitly across time.
 
---
 
### Jobs and Schedules
 
Players set department priorities; NPCs self-assign within those parameters. Every NPC has a fully customisable personal schedule. Work speed scales with mood. Crisis NPCs are auto-reassigned to recreational jobs.
 
---
 
### Items and Inventory
 
Physical container-based storage. Inventory is weight-based. NPCs carry items in pockets and bags. Food decays. Item categories span Raw Resources through to wearables, consumables, and datachips.
 
---
 
## Systems at a Glance
 
| System | Status |
|---|---|
| EventSystem | |
| FactionSystem | |
| FactionGovernmentSystem | |
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
| ShipSystem | |
| CraftingSystem | |
| EconomySystem | |
| DepartmentSystem | |
| RoomSystem | |
| Horizon Simulation | |
| Save / Load | |
 
---
 
## Roadmap
 
- Horizon Simulation — procedural region ticking, faction activity beyond player visibility, region discovery
- Species System — non-human species with distinct traits, medical needs, and food requirements
- Multi-Station Founding — establish and manage multiple waystations across sectors
- Planetary Surfaces — ground-side missions and surface operations
- Designer / Asset Editor — in-game editor for clothing, furniture, tiles, and animations
- Save / Load — full game state serialisation and load
 
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
