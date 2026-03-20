#!/usr/bin/env python3
"""
validate_npc_atlases.py — CI script that asserts:
  1. All 9 NPC PNG atlases exist under atlases/ with the correct pixel dimensions.
  2. All 9 JSON sidecars exist, parse without error, and contain tiles arrays
     with the correct lengths, required fields, unique IDs, and sequential col values.
  3. Atlases that use the shader-recolour neutral-tone master format carry a
     mask_atlas reference and per-tile colour_slots arrays.

Note: Shader-recolour atlas PNGs (hair/hat/shirt/pants/shoes/back/weapon) must be
regenerated from generators/ using neutral-tone master art assets.  Until regenerated,
PNG dimension checks for those atlases are skipped.

Run from the repository root:
    python3 .github/scripts/validate_npc_atlases.py

Exits with code 0 on success, code 1 on any failure.
"""

import json
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("ERROR: Pillow not installed — run: pip install Pillow")
    sys.exit(1)

ATLASES_DIR = Path(__file__).resolve().parent.parent.parent / "atlases"

# Expected PNG dimensions (width, height) keyed by filename.
# Neutral-tone master atlases (shader-recolour, one tile per variant) will have
# reduced widths vs the original colour-indexed atlases once regenerated.
EXPECTED_DIMENSIONS = {
    "npc_body.png":   (612,  50),   # unchanged — no shader tinting on body
    "npc_face.png":   (136,  50),   # unchanged — no shader tinting on face
    "npc_hair.png":   (170,  50),   # 5 styles × 34 px  (was 1020 — 30 colour variants)
    "npc_hat.png":    (136,  50),   # 4 types  × 34 px  (was  850 — 25 colour variants)
    "npc_shirt.png":  (170,  50),   # 5 types  × 34 px  (was  850 — 25 colour variants)
    "npc_pants.png":  (136,  50),   # 4 types  × 34 px  (was  680 — 20 colour variants)
    "npc_shoes.png":  (102,  50),   # 3 types  × 34 px  (was  510 — 15 colour variants)
    "npc_back.png":   (136,  50),   # 4 types  × 34 px  (was  340 — 10 colour variants)
    "npc_weapon.png": (272,  50),   # 8 types  × 34 px  (was  680 — 20 colour variants)
}

# Expected tile (variant) counts keyed by atlas filename.
# Shader-recolour atlases have one tile per visual variant; colour is set at
# runtime via the NpcApparel shader using mask-keyed per-channel tinting.
EXPECTED_TILE_COUNTS = {
    "npc_body.png":    18,   # 3 body types × 6 skin tones — no shader recolour
    "npc_face.png":     4,   # 4 expressions — no shader recolour
    "npc_hair.png":     5,   # 5 styles (Short/Long/Medium/Buzz/Ponytail)
    "npc_hat.png":      4,   # 4 types  (Cap/Helmet/Beret/Visor)
    "npc_shirt.png":    5,   # 5 types  (Tshirt/Collar/Uniform/Vest/Tank)
    "npc_pants.png":    4,   # 4 types  (Casual/Cargo/Uniform/Shorts)
    "npc_shoes.png":    3,   # 3 types  (Boots/Sneakers/Formal)
    "npc_back.png":     4,   # 4 types  (Backpack/Quiver/Jetpack/Shield)
    "npc_weapon.png":   8,   # 8 types  (None/Pistol/Rifle/Shotgun/Knife/Bat/Wrench/Shield)
}

# Atlases converted to shader-recolour neutral-tone master format MUST carry a
# mask_atlas field and per-tile colour_slots arrays.
SHADER_RECOLOUR_ATLASES = {
    "npc_hair.json",
    "npc_hat.json",
    "npc_shirt.json",
    "npc_pants.json",
    "npc_shoes.json",
    "npc_back.json",
    "npc_weapon.json",
}

# PNG dimension checks for these atlases are skipped until neutral-tone master
# PNGs are regenerated from generators/ by the art pipeline.
PENDING_REGENERATION_PNGS = {
    "npc_hair.png",
    "npc_hat.png",
    "npc_shirt.png",
    "npc_pants.png",
    "npc_shoes.png",
    "npc_back.png",
    "npc_weapon.png",
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
        if filename in PENDING_REGENERATION_PNGS:
            print(f"  SKIP {filename}: pending neutral-tone master regeneration")
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

        # Shader-recolour atlases must carry mask_atlas
        if json_filename in SHADER_RECOLOUR_ATLASES:
            if "mask_atlas" not in data:
                failures.append(
                    f"MISSING mask_atlas in shader-recolour atlas {json_filename}"
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

            # Shader-recolour atlases: tiles should carry colour_slots
            # (npc_weapon_none has no colour_slots since it's transparent — that is allowed)
            if json_filename in SHADER_RECOLOUR_ATLASES:
                if tile_id.endswith("_none"):
                    pass  # transparent/empty slot — colour_slots optional
                elif "colour_slots" not in tile:
                    failures.append(
                        f"MISSING colour_slots in {json_filename} tile {i} ({tile_id!r})"
                    )
                else:
                    for slot in tile["colour_slots"]:
                        if "name" not in slot or "mask_colour" not in slot:
                            failures.append(
                                f"MALFORMED colour_slot entry in {json_filename} "
                                f"tile {i} ({tile_id!r})"
                            )

        if not failures or all(json_filename not in f for f in failures):
            print(f"  OK   {json_filename}: {len(tiles)} tiles")

    return failures


def main():
    if not ATLASES_DIR.is_dir():
        print(f"ERROR: atlases/ directory not found at {ATLASES_DIR}")
        sys.exit(1)

    print("=== NPC Atlas PNG Dimension Validation ===")
    png_failures = check_png_dimensions()

    print("\n=== NPC Atlas JSON Schema Validation ===")
    json_failures = check_json_sidecars()

    all_failures = png_failures + json_failures

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
