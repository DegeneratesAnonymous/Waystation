# Waystation — Unity Project

This directory contains the Unity 6 (6000.0 LTS) project for **Frontier Waystation**.

## Opening the Project

1. Install **Unity Hub** and Unity version **6000.0.40f1** (or later 6000.x release).
2. In Unity Hub, click **Add → Add project from disk** and select this `UnityProject/` folder.
3. Unity will import all assets and generate the `Library/` cache — this takes a few minutes on first open.
4. **Create the scenes** — no `.unity` scene files are committed to the repository (visual/audio assets and scene layouts are out of scope for this PR). Create two empty scenes in the Editor and save them as:
   - `Assets/Scenes/MainMenuScene.unity`
   - `Assets/Scenes/GameScene.unity`
5. In each scene, add a `GameObject` and attach the relevant manager script:
   - **MainMenuScene** → `MainMenuManager` MonoBehaviour (plus uGUI canvas with the wired-up buttons and input fields described below).
   - **GameScene** → `GameManager` + `ContentRegistry` + `GameViewController` MonoBehaviours.
6. Add both scenes to **File → Build Settings** (drag them into the Scenes In Build list).
7. Press **▶ Play** while `GameScene` is open to run the simulation in the Editor.

> **Quick-start tip:** To test the game loop without building a full UI, add `GameManager` and `ContentRegistry` to an empty scene, call `GameManager.NewGame("Test Station")` from a small bootstrap script, and inspect `GameManager.Station` in the Inspector each tick.

## Project Structure

```
UnityProject/
├── Assets/
│   ├── Scenes/                         # NOT committed — create MainMenuScene and GameScene in-editor
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── GameManager.cs          # Singleton orchestrator — owns all systems
│   │   │   ├── ContentRegistry.cs      # Data loader (reads StreamingAssets JSON)
│   │   │   └── MiniJSON.cs             # Bundled lightweight JSON parser (MIT)
│   │   ├── Models/
│   │   │   ├── Templates.cs            # Static definition types (EventDefinition, NPCTemplate …)
│   │   │   └── Instances.cs            # Runtime instance types (StationState, NPCInstance …)
│   │   ├── Systems/
│   │   │   ├── TimeSystem.cs           # Day/night cycle
│   │   │   ├── ResourceSystem.cs       # Per-tick resource production & consumption
│   │   │   ├── NPCSystem.cs            # NPC generation, needs decay, skill XP
│   │   │   ├── JobSystem.cs            # NPC work-loop assignment
│   │   │   ├── FactionSystem.cs        # Faction reputation & inter-faction dynamics
│   │   │   ├── CombatSystem.cs         # Boarding resolution (power-based + Gaussian jitter)
│   │   │   ├── TradeSystem.cs          # Trade offer generation & buy/sell execution
│   │   │   ├── EventSystem.cs          # Data-driven event pipeline (conditions → outcomes)
│   │   │   ├── InventorySystem.cs      # Cargo hold management & perishable decay
│   │   │   └── VisitorSystem.cs        # Ship arrivals, docking decisions, contraband checks
│   │   └── UI/
│   │       ├── MainMenuManager.cs      # Main menu scene controller
│   │       └── GameViewController.cs  # In-game HUD — status bar, event panel, log
│   └── StreamingAssets/
│       └── data/                       # JSON data files (loaded at runtime)
│           ├── events/core_events.json
│           ├── factions/core_factions.json
│           ├── modules/core_modules.json
│           ├── items/core_items.json
│           ├── npcs/core_npc_templates.json
│           ├── ships/core_ships.json
│           ├── classes/core_classes.json
│           └── jobs/core_jobs.json
├── Packages/
│   └── manifest.json                   # Unity package dependencies (URP, TMP, Input System …)
└── ProjectSettings/                    # Unity project configuration (checked-in)
```

## Architecture

The game follows the same architecture as the Python prototype:

| Layer | Description |
|---|---|
| **Models** | Pure C# data classes — `Templates` (static definitions) and `Instances` (runtime state). No Unity dependencies. |
| **Systems** | Plain C# classes that operate on `StationState`. Each system has a `Tick(StationState)` method called every game tick. |
| **Core** | `GameManager` (MonoBehaviour singleton) owns all systems and drives the tick loop. `ContentRegistry` (MonoBehaviour) loads JSON data asynchronously on startup. |
| **UI** | MonoBehaviours (`MainMenuManager`, `GameViewController`) react to `GameManager` events and update uGUI elements. |

## Game Systems

| System | Description |
|---|---|
| `TimeSystem` | Manages the 24-tick day/night cycle. Day phase = work shift; night = rest. |
| `ResourceSystem` | Applies module `resource_effects` per tick; morale scales production efficiency. |
| `NPCSystem` | Spawns NPCs from templates; drives needs decay, social recovery, skill XP. |
| `JobSystem` | Assigns day/night jobs to crew based on class; applies resource and need effects. |
| `FactionSystem` | Tracks player↔faction and inter-faction relationships; aggressive factions decay rep. |
| `CombatSystem` | Resolves boarding: defender power vs attacker power + Gaussian jitter → outcome tier. |
| `TradeSystem` | Generates trade manifests for docked ships; handles buy/sell with negotiation skill modifier. |
| `EventSystem` | Data-driven pipeline: eligibility → weighted selection → condition checks → outcome effects. |
| `InventorySystem` | Manages cargo holds: capacity limits, type filters, reserved fractions, perishable decay. |
| `VisitorSystem` | Generates incoming ships, manages docking/denial, spawns passengers, contraband checks. |

## Adding Content

All game content is data-driven. To add new content:

1. Create or edit a JSON file in `Assets/StreamingAssets/data/<type>/`.
2. Follow the same schema as the existing entries in that folder.
3. Press **▶ Play** — the registry reloads on every play session.

No code changes are required for new events, factions, modules, items, ships, NPC templates, or jobs.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| Universal Render Pipeline | 17.0.3 | Rendering |
| TextMeshPro | 3.0.9 | UI text |
| Input System | 1.11.2 | Input handling |
| Timeline | 1.8.7 | Cutscene/animation |
| Unity UI (uGUI) | 2.0.0 | HUD widgets |

## Performance Notes

- All game systems are plain C# — no `MonoBehaviour` overhead on the hot path.
- The tick loop runs at a configurable rate (default 2 ticks/second) decoupled from render framerate.
- For large simulations, systems can be moved to Unity's Job System (Burst-compiled) without changing the `StationState` data model.
