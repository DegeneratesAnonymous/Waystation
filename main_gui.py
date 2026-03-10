"""
Frontier Waystation — graphical entry point.

Usage:
    python main_gui.py [--seed SEED] [--station-name NAME]

Controls:
    Space      — advance one tick
    A          — toggle auto-advance
    Click      — select module / choose event option
    Q / Escape — quit
"""

import argparse
import logging
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "src"))

from waystation.game import Game
from waystation.ui.game_view import GameView


def main() -> None:
    parser = argparse.ArgumentParser(description="Frontier Waystation (GUI)")
    parser.add_argument("--seed",         type=int, default=None)
    parser.add_argument("--station-name", default="Waystation Alpha")
    parser.add_argument("--log-level",    default="WARNING",
                        choices=["DEBUG", "INFO", "WARNING", "ERROR"])
    args = parser.parse_args()

    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(levelname)s [%(name)s] %(message)s",
    )

    root      = Path(__file__).parent
    data_root = root / "data"
    mods_root = root / "mods"

    game = Game(data_root=data_root, mods_root=mods_root, seed=args.seed)
    game.load()
    game.new_game(station_name=args.station_name)

    view = GameView(game)
    view.run()


if __name__ == "__main__":
    main()
