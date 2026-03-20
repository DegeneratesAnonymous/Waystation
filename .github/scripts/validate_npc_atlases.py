#!/usr/bin/env python3
"""
validate_npc_atlases.py — CI script that asserts:
  1. All 9 base NPC PNG atlases exist under atlases/ with the correct pixel dimensions.
  2. All 7 mask PNG atlases exist with the correct pixel dimensions.
  3. All 9 JSON sidecars exist, parse without error, and contain tiles arrays with the
     correct lengths, required fields, unique IDs, sequential col values, and colour_slots.
  4. JSON files for clothing/hair atlases include a mask_atlas field.

Run from the repository root:
    python3 .github/scripts/validate_npc_atlases.py

Exits with code 0 on success, code 1 on any failure.
"""

import json
import os
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("ERROR: Pillow not installed — run: pip install Pillow")
    sys.exit(1)

ATLASES_DIR = Path(__file__).resolve().parent.parent.parent / "atlases"

# Expected PNG dimensions (width, height) keyed by filename
EXPECTED_DIMENSIONS = {
    "npc_body.png":   (612,  50),   # unchanged
    "npc_face.png":   (136,  50),   # unchanged
    "npc_hair.png":   (170,  50),   # 5 tiles × 34px
    "npc_hat.png":    (170,  50),   # 5 tiles × 34px
    "npc_shirt.png":  (170,  50),   # 5 tiles × 34px
    "npc_pants.png":  (136,  50),   # 4 tiles × 34px
    "npc_shoes.png":  (102,  50),   # 3 tiles × 34px
    "npc_back.png":   (170,  50),   # 5 tiles × 34px
    "npc_weapon.png": (680,  50),   # 20 tiles (unchanged)
}

# Expected tile (variant) counts keyed by atlas filename
EXPECTED_TILE_COUNTS = {
    "npc_body.png":   18,
    "npc_face.png":    4,
    "npc_hair.png":    5,
    "npc_hat.png":     5,
    "npc_shirt.png":   5,
    "npc_pants.png":   4,
    "npc_shoes.png":   3,
    "npc_back.png":    5,
    "npc_weapon.png": 20,
}

# Mask atlas expected dimensions
MASK_ATLASES = {
    "npc_hair_mask.png":   (170, 50),
    "npc_hat_mask.png":    (170, 50),
    "npc_shirt_mask.png":  (170, 50),
    "npc_pants_mask.png":  (136, 50),
    "npc_shoes_mask.png":  (102, 50),
    "npc_back_mask.png":   (170, 50),
    "npc_weapon_mask.png": (680, 50),
}

# Clothing/hair atlases that must include a mask_atlas field in their JSON
ATLASES_WITH_MASK = {
    "npc_hair.png", "npc_shirt.png", "npc_pants.png", "npc_shoes.png",
    "npc_hat.png",  "npc_back.png",  "npc_weapon.png",
}

# Required JSON top-level fields
REQUIRED_JSON_FIELDS = {"atlas", "tile_size", "slot_size", "padding", "pivot",
                        "perspective", "category", "tiles"}

# Required per-tile fields
REQUIRED_TILE_FIELDS = {"id", "col", "row", "tags"}

SLOT_W = 34  # expected slot width


def check_png_dimensions():
    failures = []
    for filename, (exp_w, exp_h) in EXPECTED_DIMENSIONS.items():
        png_path = ATLASES_DIR / filename
        if not png_path.exists():
            failures.append(f"MISSING PNG: {png_path}")
            continue
        try:
            with Image.open(png_path) as img:
                w, h = img.size
            if (w, h) != (exp_w, exp_h):
                failures.append(
                    f"DIMENSION MISMATCH {filename}: expected {exp_w}×{exp_h}, got {w}×{h}"
                )
            else:
                print(f"  OK   {filename}: {exp_w}×{exp_h}")
        except Exception as e:
            failures.append(f"PNG READ ERROR {filename}: {e}")
    return failures


def check_mask_png_dimensions():
    failures = []
    for filename, (exp_w, exp_h) in MASK_ATLASES.items():
        png_path = ATLASES_DIR / filename
        if not png_path.exists():
            failures.append(f"MISSING MASK PNG: {png_path}")
            continue
        try:
            with Image.open(png_path) as img:
                w, h = img.size
            if (w, h) != (exp_w, exp_h):
                failures.append(
                    f"DIMENSION MISMATCH {filename}: expected {exp_w}×{exp_h}, got {w}×{h}"
                )
            else:
                print(f"  OK   {filename}: {exp_w}×{exp_h}")
        except Exception as e:
            failures.append(f"MASK PNG READ ERROR {filename}: {e}")
    return failures


def check_json_sidecars():
    failures = []
    for atlas_filename, expected_count in EXPECTED_TILE_COUNTS.items():
        json_filename = atlas_filename.replace(".png", ".json")
        json_path = ATLASES_DIR / json_filename
        if not json_path.exists():
            failures.append(f"MISSING JSON: {json_path}")
            continue

        try:
            with open(json_path, "r") as f:
                data = json.load(f)
        except json.JSONDecodeError as e:
            failures.append(f"JSON PARSE ERROR {json_filename}: {e}")
            continue

        # Check required top-level fields
        missing_fields = REQUIRED_JSON_FIELDS - set(data.keys())
        if missing_fields:
            failures.append(
                f"MISSING FIELDS in {json_filename}: {sorted(missing_fields)}"
            )

        # Check mask_atlas present for clothing/hair atlases
        if atlas_filename in ATLASES_WITH_MASK:
            if "mask_atlas" not in data:
                failures.append(
                    f"MISSING mask_atlas field in {json_filename}"
                )

        # Check atlas filename matches
        if data.get("atlas") != atlas_filename:
            failures.append(
                f"ATLAS MISMATCH in {json_filename}: "
                f"expected atlas={atlas_filename!r}, got {data.get('atlas')!r}"
            )

        # Check tile_size and slot_size values
        if data.get("tile_size") != {"w": 32, "h": 48}:
            failures.append(
                f"TILE_SIZE MISMATCH in {json_filename}: {data.get('tile_size')}"
            )
        if data.get("slot_size") != {"w": 34, "h": 50}:
            failures.append(
                f"SLOT_SIZE MISMATCH in {json_filename}: {data.get('slot_size')}"
            )

        tiles = data.get("tiles", [])
        if len(tiles) != expected_count:
            failures.append(
                f"TILE COUNT MISMATCH {json_filename}: "
                f"expected {expected_count}, got {len(tiles)}"
            )

        # Per-tile validation
        seen_ids = set()
        for i, tile in enumerate(tiles):
            missing_tile_fields = REQUIRED_TILE_FIELDS - set(tile.keys())
            if missing_tile_fields:
                failures.append(
                    f"TILE {i} in {json_filename} missing fields: {sorted(missing_tile_fields)}"
                )
                continue

            # Unique IDs
            tile_id = tile["id"]
            if tile_id in seen_ids:
                failures.append(f"DUPLICATE TILE ID in {json_filename}: {tile_id!r}")
            seen_ids.add(tile_id)

            # Sequential col values (0-based)
            if tile["col"] != i:
                failures.append(
                    f"NON-SEQUENTIAL col in {json_filename} tile {i}: "
                    f"expected col={i}, got col={tile['col']}"
                )

            # row must always be 0
            if tile["row"] != 0:
                failures.append(
                    f"UNEXPECTED row in {json_filename} tile {i}: "
                    f"expected row=0, got row={tile['row']}"
                )

            # tags must be a non-empty list
            tags = tile.get("tags", [])
            if not isinstance(tags, list) or len(tags) == 0:
                failures.append(
                    f"EMPTY or INVALID tags in {json_filename} tile {i} ({tile_id!r})"
                )

            # colour_slots must be present and be a list for clothing/hair atlases
            if atlas_filename in ATLASES_WITH_MASK:
                if "colour_slots" not in tile:
                    failures.append(
                        f"MISSING colour_slots in {json_filename} tile {i} ({tile_id!r})"
                    )
                else:
                    slots = tile["colour_slots"]
                    if not isinstance(slots, list):
                        failures.append(
                            f"INVALID colour_slots (not a list) in {json_filename} tile {i} ({tile_id!r})"
                        )
                    else:
                        for si, slot in enumerate(slots):
                            if not isinstance(slot.get("name"), str):
                                failures.append(
                                    f"colour_slots[{si}] missing 'name' in {json_filename} tile {i}"
                                )
                            mc = slot.get("mask_colour", "")
                            if not isinstance(mc, str) or not mc.startswith("#"):
                                failures.append(
                                    f"colour_slots[{si}] invalid 'mask_colour' in {json_filename} tile {i}"
                                )

        if not failures or all(json_filename not in f for f in failures):
            print(f"  OK   {json_filename}: {len(tiles)} tiles")

    return failures


def main():
    if not ATLASES_DIR.is_dir():
        print(f"ERROR: atlases/ directory not found at {ATLASES_DIR}")
        sys.exit(1)

    print("=== NPC Atlas Base PNG Dimension Validation ===")
    png_failures = check_png_dimensions()

    print("\n=== NPC Atlas Mask PNG Dimension Validation ===")
    mask_failures = check_mask_png_dimensions()

    print("\n=== NPC Atlas JSON Schema Validation ===")
    json_failures = check_json_sidecars()

    all_failures = png_failures + mask_failures + json_failures

    if all_failures:
        print(f"\n{'='*50}")
        print(f"VALIDATION FAILED — {len(all_failures)} error(s):")
        for f in all_failures:
            print(f"  ✗ {f}")
        sys.exit(1)
    else:
        print(f"\n{'='*50}")
        print("All NPC atlas validations passed ✓")
        sys.exit(0)


if __name__ == "__main__":
    main()
