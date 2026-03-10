"""
Frontier Waystation — entry point.

Usage:
    python main.py [--seed SEED] [--station-name NAME]

Install dependencies first:
    pip install -r requirements.txt
"""

import argparse
import logging
import sys
from pathlib import Path

# Add src to path so waystation package resolves
sys.path.insert(0, str(Path(__file__).parent / "src"))

from waystation.game import Game


def main() -> None:
    parser = argparse.ArgumentParser(description="Frontier Waystation")
    parser.add_argument("--seed", type=int, default=None, help="RNG seed for reproducibility")
    parser.add_argument("--station-name", default="Waystation Alpha", help="Name for your station")
    parser.add_argument("--log-level", default="WARNING",
                        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
                        help="Logging verbosity")
    args = parser.parse_args()

    logging.basicConfig(
        level=getattr(logging, args.log_level),
        format="%(levelname)s [%(name)s] %(message)s",
    )

    root = Path(__file__).parent
    data_root = root / "data"
    mods_root = root / "mods"

    game = Game(data_root=data_root, mods_root=mods_root, seed=args.seed)
    game.load()
    game.new_game(station_name=args.station_name)
    game.run()


if __name__ == "__main__":
    main()
