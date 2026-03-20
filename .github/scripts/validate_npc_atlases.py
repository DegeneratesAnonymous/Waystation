#!/usr/bin/env python3
"""
validate_npc_atlases.py — CI script that asserts:
  1. All 9 NPC base PNG atlases and 7 mask PNG atlases exist under atlases/
     with the correct pixel dimensions.
  2. All 9 base JSON sidecars and 7 mask JSON sidecars exist, parse without
     error, and contain tiles arrays with the correct lengths, required fields,
     unique IDs, and sequential col values.
  3. Base atlas JSONs contain a mask_atlas field pointing to the companion mask.
  4. Each tile in a base atlas JSON has a colour_slots array (may be empty).
  5. Mask atlas JSONs contain a base_atlas field pointing to the base atlas.

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

# ── Base atlas expected PNG dimensions ───────────────────────────────────────
# npc_body and npc_face are unchanged baked atlases.
# Clothing/hair atlases are neutral-tone masters: 1 variant per type/style.
EXPECTED_DIMENSIONS = {
    "npc_body.png":        (612,  50),  # 18 cols × 34 = 612 (baked)
    "npc_face.png":        (136,  50),  #  4 cols × 34 = 136 (baked)
    "npc_hair.png":        (170,  50),  #  5 cols × 34 = 170
    "npc_hat.png":         (170,  50),  #  5 cols × 34 = 170
    "npc_shirt.png":       (170,  50),  #  5 cols × 34 = 170
    "npc_pants.png":       (136,  50),  #  4 cols × 34 = 136
    "npc_shoes.png":       (102,  50),  #  3 cols × 34 = 102
    "npc_back.png":        (170,  50),  #  5 cols × 34 = 170
    "npc_weapon.png":      (680,  50),  # 20 cols × 34 = 680
}

# ── Mask atlas expected dimensions (same as base) ─────────────────────────────
MASK_EXPECTED_DIMENSIONS = {
    "npc_hair_mask.png":   (170,  50),
    "npc_hat_mask.png":    (170,  50),
    "npc_shirt_mask.png":  (170,  50),
    "npc_pants_mask.png":  (136,  50),
    "npc_shoes_mask.png":  (102,  50),
    "npc_back_mask.png":   (170,  50),
    "npc_weapon_mask.png": (680,  50),
}

# ── Expected tile counts ──────────────────────────────────────────────────────
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

MASK_EXPECTED_TILE_COUNTS = {
    "npc_hair_mask.png":    5,
    "npc_hat_mask.png":     5,
    "npc_shirt_mask.png":   5,
    "npc_pants_mask.png":   4,
    "npc_shoes_mask.png":   3,
    "npc_back_mask.png":    5,
    "npc_weapon_mask.png": 20,
}

# ── Required JSON fields ──────────────────────────────────────────────────────
REQUIRED_JSON_FIELDS = {"atlas", "tile_size", "slot_size", "padding", "pivot",
                        "perspective", "category", "tiles"}

# Base atlases (clothing/hair) additionally require mask_atlas
CLOTHING_ATLASES = {
    "npc_hair.png", "npc_hat.png", "npc_shirt.png", "npc_pants.png",
    "npc_shoes.png", "npc_back.png", "npc_weapon.png",
}

REQUIRED_TILE_FIELDS = {"id", "col", "row", "tags"}

SLOT_W = 34


def check_png_dimensions(dim_map, label=""):
    failures = []
    for filename, (exp_w, exp_h) in dim_map.items():
        png_path = ATLASES_DIR / filename
        if not png_path.exists():
            failures.append(f"MISSING PNG{label}: {png_path}")
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


def check_json_sidecars(tile_counts, is_mask=False):
    failures = []
    for atlas_filename, expected_count in tile_counts.items():
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

        # Required top-level fields
        missing_fields = REQUIRED_JSON_FIELDS - set(data.keys())
        if missing_fields:
            failures.append(
                f"MISSING FIELDS in {json_filename}: {sorted(missing_fields)}"
            )

        # atlas field must match
        if data.get("atlas") != atlas_filename:
            failures.append(
                f"ATLAS MISMATCH in {json_filename}: "
                f"expected atlas={atlas_filename!r}, got {data.get('atlas')!r}"
            )

        # tile_size and slot_size
        if data.get("tile_size") != {"w": 32, "h": 48}:
            failures.append(
                f"TILE_SIZE MISMATCH in {json_filename}: {data.get('tile_size')}"
            )
        if data.get("slot_size") != {"w": 34, "h": 50}:
            failures.append(
                f"SLOT_SIZE MISMATCH in {json_filename}: {data.get('slot_size')}"
            )

        # Base clothing atlases must have mask_atlas reference
        if not is_mask and atlas_filename in CLOTHING_ATLASES:
            if "mask_atlas" not in data:
                failures.append(
                    f"MISSING mask_atlas field in {json_filename}"
                )
            else:
                expected_mask = atlas_filename.replace(".png", "_mask.png")
                if data["mask_atlas"] != expected_mask:
                    failures.append(
                        f"MASK_ATLAS MISMATCH in {json_filename}: "
                        f"expected {expected_mask!r}, got {data['mask_atlas']!r}"
                    )

        # Mask atlas JSONs must have a base_atlas field referencing the base atlas
        if is_mask:
            if "base_atlas" not in data:
                failures.append(
                    f"MISSING base_atlas field in {json_filename}"
                )
            else:
                expected_base = atlas_filename.replace("_mask.png", ".png")
                if data["base_atlas"] != expected_base:
                    failures.append(
                        f"BASE_ATLAS MISMATCH in {json_filename}: "
                        f"expected {expected_base!r}, got {data['base_atlas']!r}"
                    )

        tiles = data.get("tiles", [])
        if len(tiles) != expected_count:
            failures.append(
                f"TILE COUNT MISMATCH {json_filename}: "
                f"expected {expected_count}, got {len(tiles)}"
            )

        seen_ids = set()
        for i, tile in enumerate(tiles):
            missing_tile_fields = REQUIRED_TILE_FIELDS - set(tile.keys())
            if missing_tile_fields:
                failures.append(
                    f"TILE {i} in {json_filename} missing fields: {sorted(missing_tile_fields)}"
                )
                continue

            tile_id = tile["id"]
            if tile_id in seen_ids:
                failures.append(f"DUPLICATE TILE ID in {json_filename}: {tile_id!r}")
            seen_ids.add(tile_id)

            if tile["col"] != i:
                failures.append(
                    f"NON-SEQUENTIAL col in {json_filename} tile {i}: "
                    f"expected col={i}, got col={tile['col']}"
                )

            if tile["row"] != 0:
                failures.append(
                    f"UNEXPECTED row in {json_filename} tile {i}: "
                    f"expected row=0, got row={tile['row']}"
                )

            tags = tile.get("tags", [])
            if not isinstance(tags, list) or len(tags) == 0:
                failures.append(
                    f"EMPTY or INVALID tags in {json_filename} tile {i} ({tile_id!r})"
                )

            # Base clothing atlas tiles must have colour_slots (list, may be empty)
            if not is_mask and atlas_filename in CLOTHING_ATLASES:
                if "colour_slots" not in tile:
                    failures.append(
                        f"MISSING colour_slots in {json_filename} tile {i} ({tile_id!r})"
                    )
                elif not isinstance(tile["colour_slots"], list):
                    failures.append(
                        f"colour_slots is not a list in {json_filename} tile {i} ({tile_id!r})"
                    )

        if not failures or all(json_filename not in f for f in failures):
            print(f"  OK   {json_filename}: {len(tiles)} tiles")

    return failures


def main():
    if not ATLASES_DIR.is_dir():
        print(f"ERROR: atlases/ directory not found at {ATLASES_DIR}")
        sys.exit(1)

    print("=== NPC Base Atlas PNG Dimension Validation ===")
    png_failures = check_png_dimensions(EXPECTED_DIMENSIONS)

    print("\n=== NPC Mask Atlas PNG Dimension Validation ===")
    mask_png_failures = check_png_dimensions(MASK_EXPECTED_DIMENSIONS, " (mask)")

    print("\n=== NPC Base Atlas JSON Schema Validation ===")
    json_failures = check_json_sidecars(EXPECTED_TILE_COUNTS, is_mask=False)

    print("\n=== NPC Mask Atlas JSON Schema Validation ===")
    mask_json_failures = check_json_sidecars(MASK_EXPECTED_TILE_COUNTS, is_mask=True)

    all_failures = png_failures + mask_png_failures + json_failures + mask_json_failures

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
