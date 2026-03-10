"""Visual theme constants for the Frontier Waystation GUI."""

# ── Palette ───────────────────────────────────────────────────────────────────
BG          = (10,  12,  20)
PANEL_BG    = (16,  20,  34)
PANEL_EDGE  = (30,  40,  70)
FLOOR_BG    = (14,  18,  30)    # station interior floor
WALL        = (40,  55,  90)    # room walls / borders
CORRIDOR    = (20,  28,  48)    # connecting corridors
TEXT        = (200, 208, 220)
TEXT_DIM    = (90,  100, 130)
TEXT_BRIGHT = (240, 248, 255)
ACCENT      = (0,   200, 255)
ACCENT_WARM = (255, 170,  50)
DANGER      = (220,  60,  60)
OK          = ( 80, 200, 120)
WARN        = (240, 180,  40)

# ── Night / day tint (overlaid on floor at low alpha) ────────────────────────
NIGHT_TINT  = (10,  15,  40)
DAY_TINT    = (255, 240, 200)

# ── Station hull / superstructure ─────────────────────────────────────────────
HULL_BG     = (18,  26,  50)    # structural panelling behind modules
HULL_EDGE   = (40,  60, 110)    # hull frame / girder lines
HULL_PAD    = 18                # pixels of hull visible around module grid

# ── Nebula background colours ─────────────────────────────────────────────────
NEBULA_COLORS = [
    (15, 25, 80),   # deep blue
    (40,  8, 65),   # purple
    ( 8, 45, 35),   # teal-green
    (50, 15,  8),   # deep red-orange
]

# ── Module colours by category ────────────────────────────────────────────────
MODULE_FLOOR = {
    "utility":    ( 22,  38,  65),
    "dock":       ( 24,  48,  32),
    "hab":        ( 55,  32,  20),
    "production": ( 22,  50,  22),
    "security":   ( 60,  18,  18),
}
MODULE_WALL = {
    "utility":    ( 50,  90, 160),
    "dock":       ( 55, 130,  75),
    "hab":        (130,  75,  45),
    "production": ( 55, 130,  55),
    "security":   (140,  40,  40),
}
MODULE_LABEL = {
    "utility":    ( 80, 140, 220),
    "dock":       ( 80, 190, 110),
    "hab":        (190, 110,  70),
    "production": ( 80, 190,  80),
    "security":   (200,  70,  70),
}

# ── NPC colours ───────────────────────────────────────────────────────────────
CLASS_COLOR = {
    "class.security":    (220,  80,  80),
    "class.engineering": (240, 180,  40),
    "class.operations":  ( 80, 160, 220),
}
VISITOR_COLOR = (170,  90, 210)
HOSTILE_COLOR = (255,  40,  40)

# ── Intent colours ────────────────────────────────────────────────────────────
INTENT_COLOR = {
    "trade":    OK,
    "refuge":   ACCENT,
    "inspect":  ACCENT_WARM,
    "smuggle":  WARN,
    "raid":     DANGER,
    "transit":  TEXT_DIM,
    "threaten": DANGER,
    "patrol":   ACCENT_WARM,
    "unknown":  TEXT_DIM,
}

# ── Screen layout ─────────────────────────────────────────────────────────────
SCREEN_W    = 1280
SCREEN_H    = 800
SIDEBAR_W   = 290
TOP_BAR_H   = 44
LOG_H       = 190

# Floor plan area
FLOOR_X     = 0
FLOOR_Y     = TOP_BAR_H
FLOOR_W     = SCREEN_W - SIDEBAR_W
FLOOR_H     = SCREEN_H - TOP_BAR_H - LOG_H

# ── Module floor-plan layout ──────────────────────────────────────────────────
# Each entry: definition_id -> (grid_col, grid_row, col_span, row_span)
# Grid cells are CELL_W × CELL_H pixels.
CELL_W      = 185
CELL_H      = 150
CELL_PAD    = 6       # corridor gap between cells
GRID_OFFSET_X = 20   # left padding inside floor area
GRID_OFFSET_Y = 20   # top padding inside floor area

FLOOR_LAYOUT: dict[str, tuple[int, int, int, int]] = {
    "module.command_center": (0, 0, 1, 1),
    "module.docking_bay_a":  (1, 0, 1, 1),
    "module.docking_bay_b":  (2, 0, 1, 1),
    "module.power_core":     (3, 0, 1, 1),
    "module.crew_quarters":  (0, 1, 1, 1),
    "module.med_bay":        (1, 1, 1, 1),
    "module.storage_hold":   (2, 1, 1, 1),
    "module.security_post":  (3, 1, 1, 1),
    "module.hydroponics":    (0, 2, 1, 1),
    "module.visitor_lounge": (1, 2, 2, 1),
}

# ── Font sizes ────────────────────────────────────────────────────────────────
FONT_SM  = 12
FONT_MD  = 14
FONT_LG  = 17
FONT_XL  = 22
FONT_HD  = 28

# ── Speed settings ────────────────────────────────────────────────────────────
# Real seconds per game tick at each speed level.
# One full day = TICKS_PER_DAY (24) ticks.
# At x1: 24 × 5.0 = 120 s ≈ 2 min/day  (matches design requirement)
TICK_INTERVAL: dict[int, float] = {
    0: 999.0,   # paused
    1: 5.0,     # x1 normal  — ~2 min per day
    2: 2.5,     # x2         — ~1 min per day
    4: 1.25,    # x4         — ~30 s per day
}
