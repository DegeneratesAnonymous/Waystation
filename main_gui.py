"""
Frontier Waystation — graphical entry point.

Usage:
    python main_gui.py [--saves-dir PATH] [--log-level LEVEL]

Controls (in-game):
    Space / P  — pause / unpause
    1 / 2 / 4  — set speed multiplier
    Ctrl+S     — quick-save
    Q / Escape — save and return to main menu
"""

import argparse
import logging
import sys
from pathlib import Path

import pygame

sys.path.insert(0, str(Path(__file__).parent / "src"))

from waystation.game import Game
from waystation.ui.game_view import GameView
from waystation.ui.main_menu import MainMenuView


def main() -> None:
    parser = argparse.ArgumentParser(description="Frontier Waystation (GUI)")
    parser.add_argument("--saves-dir",  default=None,
                        help="Directory to store save files (default: <game_root>/saves)")
    parser.add_argument("--log-level",  default="WARNING",
                        choices=["DEBUG", "INFO", "WARNING", "ERROR"])
    args = parser.parse_args()

    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(levelname)s [%(name)s] %(message)s",
    )

    root      = Path(__file__).parent
    data_root = root / "data"
    mods_root = root / "mods"
    saves_dir = Path(args.saves_dir) if args.saves_dir else root / "saves"

    pygame.init()
    screen = pygame.display.set_mode((1280, 800))

    # ── Pre-load content registry once so the menu opens instantly ──────────
    # (We share a single Game loader across sessions so we don't reload
    #  the YAML data files on every new/load game.)
    loader = Game(data_root=data_root, mods_root=mods_root)
    loader.load()

    while True:
        # ── Main Menu ────────────────────────────────────────────────────────
        menu = MainMenuView(data_root, mods_root, saves_dir, screen=screen)
        action = menu.run()

        if action[0] == "quit":
            break

        # ── Build / restore game from menu selection ──────────────────────
        if action[0] == "new_game":
            station_name, seed = action[1], action[2]
            game = Game(data_root=data_root, mods_root=mods_root, seed=seed)
            game.load()
            game.new_game(station_name=station_name)

        elif action[0] == "load_game":
            save_path = action[1]
            game = Game(data_root=data_root, mods_root=mods_root)
            game.load()
            try:
                game.load_saved_game(save_path)
            except Exception as exc:
                logging.error("Failed to load save '%s': %s", save_path, exc)
                continue  # return to main menu
        else:
            continue

        # ── Run the game view ─────────────────────────────────────────────
        view   = GameView(game, saves_dir=saves_dir)
        result = view.run()

        if result == "quit":
            break
        # result == "menu" → loop back to main menu

    pygame.quit()


if __name__ == "__main__":
    main()
