"""
GameView — pygame top-down station renderer with real-time NPC movement.

Layout:
  +──────────────────────────────────────+────────────────+
  │  TOP BAR  name | time | speed        │                │
  +──────────────────────────────────────│   SIDEBAR      │
  │  space background (nebula + stars)   │  Resources     │
  │   STATION FLOOR PLAN                 │  Crew / Jobs   │
  │   hull → rooms + corridors + icons   │  Ships         │
  │   crew humanoid sprites walking      │  Factions      │
  │   approaching ships (right lane)     │                │
  +──────────────────────────────────────+────────────────+
  │   EVENT CARD  or  LOG FEED  or  BUILD TOOLBAR         │
  +───────────────────────────────────────────────────────+

Controls:
  P / Space  — pause / unpause
  1 / 2 / 4  — set speed multiplier
  B          — toggle build mode
  Click      — select module or event choice
  Q / Esc    — quit (or close build sub-menus)
"""

from __future__ import annotations

import math
import random
from pathlib import Path
from typing import TYPE_CHECKING

import pygame

from waystation.ui import theme as T
from waystation.ui import draw as D
from waystation.systems import time_system
from waystation.systems.events import PendingEvent
from waystation.models.tilemap import TileMap, WALL_MAX_HP
from waystation.models.instances import ModuleInstance

if TYPE_CHECKING:
    from waystation.game import Game
    from waystation.models.instances import NPCInstance


# ── Constants ──────────────────────────────────────────────────────────────────
AUTOSAVE_FILENAME = "autosave.json"


def _wall_hp_color(base: tuple, hp: int) -> tuple:
    """
    Blend a wall base color toward red as hp falls below WALL_MAX_HP.
    Full health returns base unchanged; 0 hp returns a deep red.
    """
    frac = max(0.0, min(1.0, hp / WALL_MAX_HP)) if WALL_MAX_HP > 0 else 1.0
    if frac >= 1.0:
        return base
    damage = (210, 45, 15)
    return (
        int(base[0] + (damage[0] - base[0]) * (1.0 - frac)),
        int(base[1] + (damage[1] - base[1]) * (1.0 - frac)),
        int(base[2] + (damage[2] - base[2]) * (1.0 - frac)),
    )

# ── Fonts ──────────────────────────────────────────────────────────────────────

class Fonts:
    def __init__(self) -> None:
        pygame.font.init()
        mono = pygame.font.match_font("consolas,couriernew,monospace")
        sans = pygame.font.match_font("segoeui,arial,sans")
        self.sm  = pygame.font.Font(mono, T.FONT_SM)
        self.md  = pygame.font.Font(mono, T.FONT_MD)
        self.lg  = pygame.font.Font(sans, T.FONT_LG)
        self.xl  = pygame.font.Font(sans, T.FONT_XL)
        self.hd  = pygame.font.Font(sans, T.FONT_HD)


# ── NPC visual ─────────────────────────────────────────────────────────────────

class NPCDot:
    """Animated dot representing an NPC on the floor plan."""

    WALK_SPEED = 80.0       # pixels per second
    BOB_AMP    = 1.5        # vertical bob amplitude in pixels
    BOB_FREQ   = 2.0        # bobs per second

    def __init__(self, uid: str, start: tuple[float, float], color: tuple) -> None:
        self.uid   = uid
        self.x     = float(start[0])
        self.y     = float(start[1])
        self.tx    = self.x
        self.ty    = self.y
        self.color = color
        self.t     = random.uniform(0, math.pi * 2)   # phase offset for bob

    def set_target(self, pos: tuple[float, float]) -> None:
        self.tx, self.ty = float(pos[0]), float(pos[1])

    def update(self, dt: float) -> None:
        self.t += dt
        dx = self.tx - self.x
        dy = self.ty - self.y
        dist = math.hypot(dx, dy)
        if dist > 1.0:
            step = min(self.WALK_SPEED * dt, dist)
            self.x += dx / dist * step
            self.y += dy / dist * step

    def draw_pos(self) -> tuple[int, int]:
        """Visual position including vertical bob when moving."""
        dx = self.tx - self.x
        dy = self.ty - self.y
        moving = math.hypot(dx, dy) > 2.0
        bob = math.sin(self.t * self.BOB_FREQ * math.pi * 2) * self.BOB_AMP if moving else 0
        return (int(self.x), int(self.y + bob))


# ── Module rect calculator ──────────────────────────────────────────────────────

def _module_rect(col: int, row: int, col_span: int, row_span: int,
                 origin_x: int, origin_y: int) -> pygame.Rect:
    cw, ch, pad = T.CELL_W, T.CELL_H, T.CELL_PAD
    x = origin_x + col * (cw + pad)
    y = origin_y + row * (ch + pad)
    w = col_span * cw + (col_span - 1) * pad
    h = row_span * ch + (row_span - 1) * pad
    return pygame.Rect(x, y, w, h)


# ── Star field ─────────────────────────────────────────────────────────────────

class StarField:
    """Procedural star background with varied sizes, colours, and twinkling."""

    _COLORS  = [
        (200, 210, 255),  # blue-white
        (255, 252, 240),  # warm white
        (180, 195, 235),  # soft blue
        (255, 225, 150),  # yellow-white
        (160, 210, 255),  # icy blue
    ]
    _WEIGHTS = [35, 30, 20, 10, 5]

    def __init__(self, seed: int, count: int = 250) -> None:
        rng = random.Random(seed)
        self.stars: list[tuple] = []
        for _ in range(count):
            x       = rng.randint(0, T.FLOOR_W - 1)
            y       = rng.randint(0, T.FLOOR_H - 1)
            r       = rng.choices([1, 2, 3], weights=[75, 22, 3])[0]
            color   = rng.choices(self._COLORS, weights=self._WEIGHTS)[0]
            phase   = rng.uniform(0, math.pi * 2)
            twinkle = rng.random() < 0.18
            self.stars.append((x, y, color, r, twinkle, phase))

    def draw(self, surf: pygame.Surface, alpha: float) -> None:
        t = pygame.time.get_ticks() / 1000.0
        for x, y, color, r, twinkle, phase in self.stars:
            if twinkle:
                brightness = 0.55 + 0.45 * abs(math.sin(t * 1.8 + phase))
            else:
                brightness = 1.0
            dim   = 1.0 - alpha * 0.65
            final = tuple(int(c * brightness * dim) for c in color)
            pygame.draw.circle(surf, final, (x, y + T.FLOOR_Y), r)


# ── Nebula field ───────────────────────────────────────────────────────────────

class NebulaField:
    """Faint coloured nebula blobs pre-rendered for fast blitting each frame."""

    def __init__(self, seed: int) -> None:
        rng = random.Random(seed + 999)
        nebulae: list[tuple] = []
        for _ in range(5):
            x     = rng.randint(50, T.FLOOR_W - 50)
            y     = rng.randint(20, T.FLOOR_H - 20)
            rx    = rng.randint(70, 190)
            ry    = rng.randint(50, 130)
            color = rng.choice(T.NEBULA_COLORS)
            alpha = rng.randint(18, 32)
            nebulae.append((x, y, rx, ry, color, alpha))
        self._surf = self._prerender(nebulae)

    @staticmethod
    def _prerender(nebulae: list[tuple]) -> pygame.Surface:
        NEBULA_LAYERS = 5
        s = pygame.Surface((T.FLOOR_W, T.FLOOR_H), pygame.SRCALPHA)
        for x, y, rx, ry, color, alpha in nebulae:
            # Concentric ellipses fading outward for a soft-glow effect
            for step in range(NEBULA_LAYERS):
                frac = 1.0 - step / NEBULA_LAYERS
                a    = int(alpha * frac * frac)
                er   = pygame.Rect(
                    int(x - rx * frac),
                    int(y - ry * frac),
                    int(rx * frac * 2),
                    int(ry * frac * 2),
                )
                if er.width > 2 and er.height > 2:
                    pygame.draw.ellipse(s, (*color, a), er)
        return s

    def draw(self, surf: pygame.Surface) -> None:
        surf.blit(self._surf, (T.FLOOR_X, T.FLOOR_Y))


# ── Main GameView ───────────────────────────────────────────────────────────────

class GameView:

    def __init__(self, game: "Game", saves_dir: "Path | None" = None,
                 auto_save: bool = True) -> None:
        self.game = game
        self.s    = game.station
        self._saves_dir = saves_dir
        self._auto_save = auto_save

        pygame.init()
        self.screen = pygame.display.set_mode((T.SCREEN_W, T.SCREEN_H))
        pygame.display.set_caption(f"Frontier Waystation — {self.s.name}")
        self.clock  = pygame.time.Clock()
        self.fonts  = Fonts()

        # Speed / time
        self._speed    = 1      # 0=paused, 1, 2, 4
        self._tick_acc = 0.0    # accumulated real seconds

        # Pending events queue
        self._pending: list[PendingEvent] = []

        # Floor plan origin (where the grid starts inside the floor area)
        self._ox = T.FLOOR_X + T.GRID_OFFSET_X
        self._oy = T.FLOOR_Y + T.GRID_OFFSET_Y

        # module uid -> pygame.Rect (computed once, updated if modules change)
        self._mod_rects: dict[str, pygame.Rect] = {}
        # module uid -> list of NPC slot positions
        self._mod_slots: dict[str, list[tuple[float, float]]] = {}
        # Cached hull bounding rect (recomputed only in _rebuild_layout)
        self._hull_rect: pygame.Rect | None = None

        # NPC dots
        self._dots: dict[str, NPCDot] = {}

        # UI state
        self._selected_mod: str | None = None
        self._event_btns:  list[dict]  = []
        self._hovered_btn: int | None  = None
        self._log_scroll               = 0

        # Stars and space background
        self._stars  = StarField(game.seed)
        self._nebula = NebulaField(game.seed)

        # Return signal: "menu" when user exits back to the main menu
        self._return_signal: str | None = None

        # Save feedback message (shown briefly after saving)
        self._save_msg: str = ""
        self._save_msg_timer: float = 0.0

        # ── Build mode state ──────────────────────────────────────────────
        self._build_mode: bool = False
        # Current tool: "floor" | "wall_add" | "wall_remove" | "erase" | "assign" | "place"
        self._build_tool: str = "floor"
        # Mouse drag tracking (None when not dragging)
        self._build_drag_start: tuple[int, int] | None = None
        self._build_last_cell:  tuple[int, int] | None = None
        self._build_dragging: bool = False
        # Hovered tile (col, row) in build mode
        self._build_hover: tuple[int, int] | None = None
        # Wall-tool drag: which side to toggle
        self._build_wall_side: str = "N"
        # Area-assign popup state
        self._assign_popup: bool = False
        self._assign_region: list[tuple[int, int]] = []
        self._assign_scroll: int = 0
        # Build-mode message (feedback)
        self._build_msg: str = ""
        self._build_msg_timer: float = 0.0
        # Place-buildable popup state
        self._place_popup: bool = False
        self._place_scroll: int = 0
        self._place_selected: str | None = None  # buildable_id chosen from popup

        # ── Overlay panels ────────────────────────────────────────────────
        # None | "comms" | "work" | "departments"
        self._active_panel: str | None = None

        # Comms panel state
        self._comms_tab: str = "unread"      # "unread" | "read" | "all"
        self._comms_selected_uid: str | None = None
        self._comms_scroll: int = 0
        self._comms_reply_hovered: int | None = None

        # Work panel state
        self._work_scroll: int = 0

        # Departments panel state
        self._dept_scroll: int = 0
        self._dept_renaming_uid: str | None = None   # uid being renamed
        self._dept_rename_text: str = ""

        # Rendered rect caches (set during render, used for click detection)
        self._comms_btn_rect: pygame.Rect | None = None
        self._work_btn_rect: pygame.Rect | None = None
        self._comms_close_rect: pygame.Rect | None = None
        self._comms_tab_rects: list = []
        self._comms_msg_rects: list = []
        self._comms_reply_rects: list = []
        self._work_close_rect: pygame.Rect | None = None
        self._work_dept_btn_rect: pygame.Rect | None = None
        self._work_cell_rects: list = []
        self._work_col_headers: list = []
        self._dept_close_rect: pygame.Rect | None = None
        self._dept_back_btn: pygame.Rect | None = None
        self._dept_row_rects: list = []
        self._dept_rename_rects: list = []
        self._dept_new_btn: pygame.Rect | None = None

        self._rebuild_layout()
        self._sync_dots()

    # ── Layout ────────────────────────────────────────────────────────────────

    def _rebuild_layout(self) -> None:
        self._mod_rects.clear()
        self._mod_slots.clear()

        for uid, mod in self.s.modules.items():
            layout = T.FLOOR_LAYOUT.get(mod.definition_id)
            if layout:
                col, row, cs, rs = layout
                rect = _module_rect(col, row, cs, rs, self._ox, self._oy)
            else:
                # Auto-place unmapped modules in a fallback row
                idx = sum(1 for m in self.s.modules.values()
                          if T.FLOOR_LAYOUT.get(m.definition_id) is None
                          and list(self.s.modules.keys()).index(m.uid) <
                              list(self.s.modules.keys()).index(uid))
                rect = _module_rect(idx % 4, 3 + idx // 4, 1, 1, self._ox, self._oy)

            self._mod_rects[uid] = rect
            self._mod_slots[uid] = self._compute_slots(rect)

        # Cache hull bounds so _render_station_hull() doesn't recompute every frame
        if self._mod_rects:
            pad   = T.HULL_PAD
            rects = list(self._mod_rects.values())
            self._hull_rect = pygame.Rect(
                min(r.left   for r in rects) - pad,
                min(r.top    for r in rects) - pad,
                max(r.right  for r in rects) + pad - (min(r.left for r in rects) - pad),
                max(r.bottom for r in rects) + pad - (min(r.top  for r in rects) - pad),
            )
        else:
            self._hull_rect = None

    def _compute_slots(self, rect: pygame.Rect) -> list[tuple[float, float]]:
        """Return evenly distributed NPC standing positions inside a room."""
        slots = []
        margin = 22
        cols = max(1, (rect.width  - margin * 2) // 24)
        rows = max(1, (rect.height - margin * 2) // 24)
        for r in range(rows):
            for c in range(cols):
                x = rect.x + margin + c * 24 + 8
                y = rect.y + margin + r * 24 + 35  # below room title
                slots.append((float(x), float(y)))
        return slots

    # ── NPC dot management ────────────────────────────────────────────────────

    def _sync_dots(self) -> None:
        """Create/remove dots to match live NPC list, then update targets."""
        live = set(self.s.npcs.keys())

        for uid in list(self._dots.keys()):
            if uid not in live:
                del self._dots[uid]

        for uid, npc in self.s.npcs.items():
            if uid not in self._dots:
                color = T.CLASS_COLOR.get(npc.class_id,
                        T.VISITOR_COLOR if npc.is_visitor() else T.TEXT)
                start = self._offscreen_pos()
                self._dots[uid] = NPCDot(uid, start, color)

        self._update_dot_targets()

    def _offscreen_pos(self) -> tuple[float, float]:
        return (float(T.SCREEN_W + 100), float(T.SCREEN_H // 2))

    def _update_dot_targets(self) -> None:
        """Assign each dot's target position based on NPC's job module."""
        slot_idx: dict[str, int] = {}

        for uid, npc in self.s.npcs.items():
            dot = self._dots.get(uid)
            if dot is None:
                continue

            # Hostile visitors go to dock area
            if "hostile" in (npc.status_tags or []):
                dot.color = T.HOSTILE_COLOR

            # Find target module
            target_uid = npc.job_module_uid or self._default_module_for(npc)
            if target_uid is None:
                dot.set_target(self._offscreen_pos())
                continue

            slots = self._mod_slots.get(target_uid, [])
            idx   = slot_idx.get(target_uid, 0)
            slot_idx[target_uid] = idx + 1
            if idx < len(slots):
                dot.set_target(slots[idx])
            elif slots:
                # Overflow: cluster near last slot with slight offset
                sx, sy = slots[-1]
                dot.set_target((sx + (idx - len(slots) + 1) * 14, sy))

    def _default_module_for(self, npc: "NPCInstance") -> str | None:
        """Fallback module for NPCs without a job assignment yet."""
        pref_cat = {
            "class.security":    "security",
            "class.engineering": "utility",
            "class.operations":  "dock",
        }.get(npc.class_id, "hab")

        for uid, mod in self.s.modules.items():
            if mod.category == pref_cat:
                return uid
        if self.s.modules:
            return next(iter(self.s.modules))
        return None

    # NPC sprite rendering constants
    _PULSE_ALPHA     = 45   # transparency of the working-pulse ring
    _BODY_DARKEN     = 45   # how much darker the body is vs. class colour
    _HEAD_HIGHLIGHT  = 90   # brightness added to the head highlight

    def run(self) -> str:
        """Run the game loop; returns 'menu' or 'quit' when the player exits."""
        while True:
            dt = self.clock.tick(60) / 1000.0

            for ev in pygame.event.get():
                if ev.type == pygame.QUIT:
                    return "quit"
                elif ev.type == pygame.KEYDOWN:
                    self._on_key(ev)
                elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                    if self._build_mode:
                        self._build_mousedown(ev.pos)
                    else:
                        self._on_click(ev.pos)
                elif ev.type == pygame.MOUSEBUTTONUP and ev.button == 1:
                    if self._build_mode:
                        self._build_mouseup(ev.pos)
                elif ev.type == pygame.MOUSEMOTION:
                    if self._build_mode:
                        self._build_mousemove(ev.pos)
                    else:
                        self._on_hover(ev.pos)

            if self._return_signal:
                return self._return_signal

            self._update(dt)
            self._render()
            pygame.display.flip()

    def _update(self, dt: float) -> None:
        # Tick accumulation
        interval = T.TICK_INTERVAL.get(self._speed, 999.0)
        self._tick_acc += dt
        if self._tick_acc >= interval:
            self._tick_acc -= interval
            hostile_pending = any(
                not p.resolved and p.definition.hostile
                for p in self.game.event_system.get_pending()
            )
            if hostile_pending:
                # Only hostile events pause the game
                self._speed = 0
            else:
                self._do_tick()

        # Animate dots
        for dot in self._dots.values():
            dot.update(dt)

        # Save message timer
        if self._save_msg_timer > 0:
            self._save_msg_timer -= dt
            if self._save_msg_timer <= 0:
                self._save_msg = ""

        # Build message timer
        if self._build_msg_timer > 0:
            self._build_msg_timer -= dt
            if self._build_msg_timer <= 0:
                self._build_msg = ""

    def _do_tick(self) -> None:
        self.game._tick()
        new = [p for p in self.game.event_system.get_pending()
               if p not in self._pending]
        self._pending.extend(new)
        self._pending = [p for p in self._pending if not p.resolved]
        self._sync_dots()
        self._rebuild_layout()
        if self._pending:
            self._build_event_buttons()

    # ── Input ─────────────────────────────────────────────────────────────────

    def _on_key(self, ev: pygame.event.Event) -> None:
        if self._build_mode:
            if ev.key == pygame.K_ESCAPE:
                if self._place_popup:
                    self._place_popup = False
                elif self._assign_popup:
                    self._assign_popup = False
                else:
                    self._build_mode = False
                return
            if ev.key == pygame.K_b:
                self._build_mode = False
                return
            # Allow scroll in assign popup
            if self._assign_popup:
                if ev.key == pygame.K_UP:
                    self._assign_scroll = max(0, self._assign_scroll - 1)
                elif ev.key == pygame.K_DOWN:
                    self._assign_scroll = min(
                        max(0, len(self.game.registry.room_types) - 7),
                        self._assign_scroll + 1,
                    )
            # Allow scroll in place popup
            elif self._place_popup:
                if ev.key == pygame.K_UP:
                    self._place_scroll = max(0, self._place_scroll - 1)
                elif ev.key == pygame.K_DOWN:
                    self._place_scroll = min(
                        max(0, len(self.game.registry.buildables) - 7),
                        self._place_scroll + 1,
                    )
            return  # consume all other keys in build mode

        # Department rename text input
        if self._active_panel == "departments" and self._dept_renaming_uid:
            if ev.key == pygame.K_RETURN:
                new_name = self._dept_rename_text.strip()
                if new_name:
                    for dept in getattr(self.s, 'departments', []):
                        if dept.uid == self._dept_renaming_uid:
                            dept.name = new_name
                            break
                self._dept_renaming_uid = None
                self._dept_rename_text = ""
            elif ev.key == pygame.K_BACKSPACE:
                self._dept_rename_text = self._dept_rename_text[:-1]
            elif ev.unicode and ev.unicode.isprintable():
                if len(self._dept_rename_text) < 30:
                    self._dept_rename_text += ev.unicode
            return

        # Close overlay panels with Escape
        if ev.key == pygame.K_ESCAPE and self._active_panel is not None:
            if self._active_panel == "departments" and self._dept_renaming_uid:
                self._dept_renaming_uid = None
                self._dept_rename_text = ""
            else:
                self._active_panel = None
            return

        # Work panel scroll
        if self._active_panel == "work":
            crew_count = len(self.s.get_crew())
            if ev.key == pygame.K_UP:
                self._work_scroll = max(0, self._work_scroll - 1)
            elif ev.key == pygame.K_DOWN:
                self._work_scroll = min(max(0, crew_count - 12), self._work_scroll + 1)
            return

        if ev.key in (pygame.K_p, pygame.K_SPACE):
            self._speed = 0 if self._speed != 0 else 1
        elif ev.key == pygame.K_1:
            self._speed = 1
        elif ev.key == pygame.K_2:
            self._speed = 2
        elif ev.key == pygame.K_4:
            self._speed = 4
        elif ev.key == pygame.K_b:
            self._build_mode = True
        elif ev.key in (pygame.K_q, pygame.K_ESCAPE):
            self._do_save_and_menu()
        elif ev.key == pygame.K_s and (pygame.key.get_mods() & pygame.KMOD_CTRL):
            self._do_save()
        elif ev.key == pygame.K_UP:
            self._log_scroll = max(0, self._log_scroll - 1)
        elif ev.key == pygame.K_DOWN:
            self._log_scroll = min(len(self.s.log) - 1, self._log_scroll + 1)

    def _do_save(self) -> None:
        """Save game to the default slot if a saves directory is configured."""
        if self._saves_dir and self.game.station:
            slot = Path(self._saves_dir) / AUTOSAVE_FILENAME
            try:
                self.game.save_game(slot)
                self._save_msg = "Saved  \u2713"
                self._save_msg_timer = 2.5
            except Exception as exc:
                self._save_msg = f"Save failed: {exc}"
                self._save_msg_timer = 3.0

    def _do_save_and_menu(self) -> None:
        """Save the game (if auto-save is enabled) and signal a return to the main menu."""
        if self._auto_save:
            self._do_save()
        self._return_signal = "menu"

    def _on_click(self, pos: tuple[int, int]) -> None:
        # ── Overlay panels take priority when open ──────────────────────────
        if self._active_panel == "comms":
            if getattr(self, '_comms_close_rect', None) and self._comms_close_rect.collidepoint(pos):
                self._active_panel = None
                return
            for tab_id, tab_rect in getattr(self, '_comms_tab_rects', []):
                if tab_rect.collidepoint(pos):
                    self._comms_tab = tab_id
                    self._comms_scroll = 0
                    self._comms_selected_uid = None
                    return
            for msg_uid, msg_rect in getattr(self, '_comms_msg_rects', []):
                if msg_rect.collidepoint(pos):
                    self._comms_selected_uid = msg_uid
                    for m in getattr(self.s, 'messages', []):
                        if m.uid == msg_uid:
                            m.read = True
                            break
                    return
            for idx, opt, btn_rect in getattr(self, '_comms_reply_rects', []):
                if btn_rect.collidepoint(pos):
                    action = opt.get("action", "")
                    sel_msg = None
                    for m in getattr(self.s, 'messages', []):
                        if m.uid == self._comms_selected_uid:
                            sel_msg = m
                            break
                    if sel_msg and self.game.comms_system:
                        self.game.comms_system.reply_to_message(
                            self.s, sel_msg, action, opt)
                    return
            return  # Block clicks through to game when panel is open

        if self._active_panel == "work":
            if getattr(self, '_work_close_rect', None) and self._work_close_rect.collidepoint(pos):
                self._active_panel = None
                return
            if getattr(self, '_work_dept_btn_rect', None) and self._work_dept_btn_rect.collidepoint(pos):
                self._active_panel = "departments"
                return
            work_assignments = getattr(self.s, 'work_assignments', {})
            for npc_uid, jid, cell_rect in getattr(self, '_work_cell_rects', []):
                if cell_rect.collidepoint(pos):
                    current = list(work_assignments.get(npc_uid, []))
                    if jid in current:
                        current.remove(jid)
                    else:
                        current.append(jid)
                    self.s.work_assignments[npc_uid] = current
                    return
            return

        if self._active_panel == "departments":
            if getattr(self, '_dept_close_rect', None) and self._dept_close_rect.collidepoint(pos):
                self._active_panel = None
                self._dept_renaming_uid = None
                return
            if getattr(self, '_dept_back_btn', None) and self._dept_back_btn.collidepoint(pos):
                self._active_panel = "work"
                self._dept_renaming_uid = None
                return
            for dept_uid, ren_rect in getattr(self, '_dept_rename_rects', []):
                if ren_rect.collidepoint(pos):
                    self._dept_renaming_uid = dept_uid
                    for dept in getattr(self.s, 'departments', []):
                        if dept.uid == dept_uid:
                            self._dept_rename_text = dept.name
                            break
                    return
            if getattr(self, '_dept_new_btn', None) and self._dept_new_btn.collidepoint(pos):
                import uuid
                new_dept_uid = "dept." + str(uuid.uuid4())[:8]
                from waystation.models.instances import Department
                new_dept = Department(uid=new_dept_uid, name="New Department",
                                      allowed_jobs=["job.haul"])
                self.s.departments.append(new_dept)
                self._dept_renaming_uid = new_dept_uid
                self._dept_rename_text = "New Department"
                return
            return

        # Top-bar buttons are always active
        # BUILD button
        if self._build_btn_rect().collidepoint(pos):
            self._build_mode = not self._build_mode
            self._assign_popup = False
            return

        # Save button
        if self._save_btn_rect().collidepoint(pos):
            self._do_save()
            return

        # Menu button
        if self._menu_btn_rect().collidepoint(pos):
            self._do_save_and_menu()
            return

        # Speed buttons (only when not in build mode)
        if not self._build_mode:
            for speed, rect in self._speed_btn_rects().items():
                if rect.collidepoint(pos):
                    self._speed = speed
                    return

            # Event buttons
            for btn in self._event_btns:
                if btn["rect"].collidepoint(pos):
                    p = btn["pending"]
                    self.game.event_system.resolve_choice(p, btn["choice_id"], self.s)
                    self._pending = [x for x in self._pending if not x.resolved]
                    self._build_event_buttons()
                    if self._speed == 0:
                        self._speed = 1   # resume after choice
                    return

            # Module selection
            for uid, rect in self._mod_rects.items():
                if rect.collidepoint(pos):
                    self._selected_mod = uid if self._selected_mod != uid else None
                    return
            # Comms button
            if getattr(self, '_comms_btn_rect', None) and self._comms_btn_rect.collidepoint(pos):
                self._active_panel = None if self._active_panel == "comms" else "comms"
                return
            # Work button
            if getattr(self, '_work_btn_rect', None) and self._work_btn_rect.collidepoint(pos):
                self._active_panel = None if self._active_panel == "work" else "work"
                return
        else:
            # Build mode click handling
            self._build_on_click(pos)

    def _on_hover(self, pos: tuple[int, int]) -> None:
        self._hovered_btn = None
        if self._build_mode:
            self._build_hover = self._pixel_to_tile(pos)
        else:
            for btn in self._event_btns:
                if btn["rect"].collidepoint(pos):
                    self._hovered_btn = id(btn)

    def _speed_btn_rects(self) -> dict[int, pygame.Rect]:
        bx = T.SCREEN_W - T.SIDEBAR_W - 260
        by = 7
        bw, bh, gap = 52, 28, 4
        return {
            0: pygame.Rect(bx,               by, bw, bh),
            1: pygame.Rect(bx + bw + gap,     by, bw, bh),
            2: pygame.Rect(bx + (bw+gap)*2,   by, bw, bh),
            4: pygame.Rect(bx + (bw+gap)*3,   by, bw, bh),
        }

    def _save_btn_rect(self) -> pygame.Rect:
        return pygame.Rect(T.SCREEN_W - T.SIDEBAR_W - 338, 7, 70, 28)

    def _menu_btn_rect(self) -> pygame.Rect:
        return pygame.Rect(T.SCREEN_W - T.SIDEBAR_W - 414, 7, 70, 28)

    def _build_btn_rect(self) -> pygame.Rect:
        return pygame.Rect(T.SCREEN_W - T.SIDEBAR_W - 494, 7, 72, 28)

    # ── Build mode — coordinate helpers ──────────────────────────────────────

    def _pixel_to_tile(self, pos: tuple[int, int]) -> tuple[int, int] | None:
        """Convert a screen pixel position to a tile (col, row), or None if outside grid."""
        px, py = pos
        # Tile grid starts at FLOOR_X, FLOOR_Y
        col = (px - T.FLOOR_X) // T.TILE_W
        row = (py - T.FLOOR_Y) // T.TILE_H
        if 0 <= col < T.TILE_COLS and 0 <= row < T.TILE_ROWS:
            return (col, row)
        return None

    def _tile_rect(self, col: int, row: int) -> pygame.Rect:
        x = T.FLOOR_X + col * T.TILE_W
        y = T.FLOOR_Y + row * T.TILE_H
        return pygame.Rect(x, y, T.TILE_W, T.TILE_H)

    # ── Build mode — mouse events ─────────────────────────────────────────────

    def _build_mousedown(self, pos: tuple[int, int]) -> None:
        """Handle mouse-button-down in build mode."""
        # Check toolbar buttons first (in the log panel area)
        if self._handle_build_toolbar_click(pos):
            return
        # If place popup is open, check if a buildable was clicked
        if self._place_popup:
            self._handle_place_popup_click(pos)
            return
        # If assign popup is open, check if a room type was clicked
        if self._assign_popup:
            self._handle_assign_popup_click(pos)
            return
        # Start drawing/erasing on the tile grid
        tile = self._pixel_to_tile(pos)
        if tile is None:
            return
        self._build_drag_start = tile
        self._build_last_cell  = tile
        self._build_dragging   = True
        self._apply_build_tool(tile)

    def _build_mouseup(self, pos: tuple[int, int]) -> None:
        """Handle mouse-button-up in build mode — end drag."""
        self._build_dragging = False
        self._build_drag_start = None
        self._build_last_cell = None

    def _build_mousemove(self, pos: tuple[int, int]) -> None:
        """Handle mouse motion in build mode (hover + drag painting)."""
        self._build_hover = self._pixel_to_tile(pos)
        if self._build_dragging:
            tile = self._pixel_to_tile(pos)
            if tile and tile != self._build_last_cell:
                self._build_last_cell = tile
                self._apply_build_tool(tile)

    def _build_on_click(self, pos: tuple[int, int]) -> None:
        """Handle a full click (down + up on same tile) in build mode."""
        if self._handle_build_toolbar_click(pos):
            return
        if self._place_popup:
            self._handle_place_popup_click(pos)
        elif self._assign_popup:
            self._handle_assign_popup_click(pos)

    def _apply_build_tool(self, tile: tuple[int, int]) -> None:
        """Apply the current build tool to (col, row)."""
        if self._assign_popup or self._place_popup:
            return  # Don't paint while popup is open
        col, row = tile
        tm = self.s.tile_map
        if self._build_tool == "floor":
            tm.set_floor(col, row)
        elif self._build_tool == "wall_tile":
            tm.set_wall(col, row)
        elif self._build_tool == "erase":
            tm.erase(col, row)
        elif self._build_tool == "wall_add":
            tm.add_wall_segment(col, row, self._build_wall_side)
        elif self._build_tool == "wall_remove":
            tm.remove_wall_segment(col, row, self._build_wall_side)
        elif self._build_tool == "door":
            tm.toggle_door(col, row, self._build_wall_side)
        elif self._build_tool == "place" and self._place_selected:
            self._do_place_foundation(tile)

    # ── Build mode — toolbar buttons ──────────────────────────────────────────

    _BUILD_TOOLS = [
        ("floor",       "FLOOR"),
        ("wall_tile",   "WALL TILE"),
        ("wall_add",    "ADD WALL EDGE"),
        ("wall_remove", "DEL WALL EDGE"),
        ("door",        "DOOR"),
        ("erase",       "ERASE"),
        ("assign",      "ASSIGN AREA"),
        ("place",       "PLACE"),
    ]
    _WALL_SIDES = ["N", "E", "S", "W"]

    def _build_toolbar_rects(self) -> list[tuple[str, str, pygame.Rect]]:
        """Return list of (tool_id, label, rect) for the build toolbar."""
        log_y   = T.SCREEN_H - T.LOG_H
        bw, bh  = 118, 28
        gap     = 6
        start_x = T.FLOOR_X + 8
        y       = log_y + 10
        rects = []
        x = start_x
        for tool_id, label in self._BUILD_TOOLS:
            rects.append((tool_id, label, pygame.Rect(x, y, bw, bh)))
            x += bw + gap
        return rects

    def _build_wall_side_rects(self) -> list[tuple[str, pygame.Rect]]:
        """Wall-side selector buttons (N/E/S/W) shown when wall tool is active."""
        log_y = T.SCREEN_H - T.LOG_H
        bw, bh = 36, 22
        gap = 4
        x = T.FLOOR_X + 8
        y = log_y + 46
        rects = []
        for side in self._WALL_SIDES:
            rects.append((side, pygame.Rect(x, y, bw, bh)))
            x += bw + gap
        return rects

    def _handle_build_toolbar_click(self, pos: tuple[int, int]) -> bool:
        """Handle click on a build toolbar button. Returns True if consumed."""
        for tool_id, _label, rect in self._build_toolbar_rects():
            if rect.collidepoint(pos):
                if tool_id == "assign":
                    self._start_assign_flow(pos)
                elif tool_id == "place":
                    self._build_tool = "place"
                    self._place_popup = True
                    self._assign_popup = False
                else:
                    self._build_tool = tool_id
                    self._assign_popup = False
                    self._place_popup = False
                return True
        # Wall/door side selector (shown for wall_add, wall_remove, door tools)
        if self._build_tool in ("wall_add", "wall_remove", "door"):
            for side, rect in self._build_wall_side_rects():
                if rect.collidepoint(pos):
                    self._build_wall_side = side
                    return True
        return False

    # ── Build mode — area assignment ─────────────────────────────────────────

    def _start_assign_flow(self, _pos: tuple[int, int]) -> None:
        """
        Start the area-assign flow. If a region of floor tiles is hovered,
        flood-fill from hover position to collect the region.
        Else open the popup centred on the grid — the user can then click a
        floor tile to select the region.
        """
        tm = self.s.tile_map
        if self._build_hover:
            region = tm.get_connected_floor(*self._build_hover)
            if region:
                self._assign_region  = region
                self._assign_popup   = True
                self._assign_scroll  = 0
                self._build_tool     = "assign"
                return
        self._build_msg = "Hover over a floor area then click ASSIGN AREA"
        self._build_msg_timer = 3.0
        self._build_tool = "assign"

    def _handle_assign_popup_click(self, pos: tuple[int, int]) -> None:
        """Handle click inside the room-type assignment popup."""
        popup_rect = self._assign_popup_rect()
        if not popup_rect.collidepoint(pos):
            # Click outside popup — check if the player clicked a floor tile
            tile = self._pixel_to_tile(pos)
            if tile:
                tm = self.s.tile_map
                region = tm.get_connected_floor(*tile)
                if region:
                    self._assign_region = region
                    self._assign_scroll = 0
            return

        # Click inside popup — hit-test each room-type row
        room_types = sorted(self.game.registry.room_types.values(),
                            key=lambda rt: rt.display_name)
        visible = room_types[self._assign_scroll: self._assign_scroll + 7]
        item_h = 28
        content_y = popup_rect.y + 34
        for i, rt in enumerate(visible):
            row_rect = pygame.Rect(popup_rect.x + 4, content_y + i * item_h,
                                   popup_rect.width - 8, item_h - 2)
            if row_rect.collidepoint(pos):
                self._do_assign_room(rt.id)
                return

        # Close button (top-right "X")
        close_rect = pygame.Rect(popup_rect.right - 26, popup_rect.y + 4, 22, 22)
        if close_rect.collidepoint(pos):
            self._assign_popup = False
            self._assign_region = []

    def _assign_popup_rect(self) -> pygame.Rect:
        """Return the pygame.Rect for the assign popup panel."""
        pw, ph = 420, 240
        cx = T.FLOOR_X + (T.FLOOR_W - pw) // 2
        cy = T.FLOOR_Y + (T.FLOOR_H - ph) // 2
        return pygame.Rect(cx, cy, pw, ph)

    def _check_requirements(self, room_type_id: str,
                             tile_count: int) -> tuple[bool, str]:
        """
        Return (can_assign: bool, reason: str).
        Checks min_tiles, resource_requirements, and component_requirements.
        """
        rt = self.game.registry.room_types.get(room_type_id)
        if rt is None:
            return False, "Unknown room type."
        if tile_count < rt.min_tiles:
            return False, f"Need ≥ {rt.min_tiles} floor tiles (have {tile_count})."
        for res, amount in rt.resource_requirements.items():
            if self.s.get_resource(res) < amount:
                return False, f"Need {amount:.0f} {res} (have {self.s.get_resource(res):.0f})."
        for item_id, qty in rt.component_requirements.items():
            # Check total across all cargo holds
            total = sum(
                mod.inventory.get(item_id, 0)
                for mod in self.s.modules.values()
            )
            if total < qty:
                item_name = (self.game.registry.items[item_id].display_name
                             if item_id in self.game.registry.items else item_id)
                return False, f"Need {qty}× {item_name} (have {total})."
        return True, ""

    def _do_assign_room(self, room_type_id: str) -> None:
        """Perform the room assignment after all checks pass."""
        rt = self.game.registry.room_types.get(room_type_id)
        if rt is None:
            return
        region  = self._assign_region
        ok, reason = self._check_requirements(room_type_id, len(region))
        if not ok:
            self._build_msg       = f"Cannot assign: {reason}"
            self._build_msg_timer = 3.0
            return

        # Connectivity check — no island rooms
        tm = self.s.tile_map
        if not tm.is_region_connected_to_station(region):
            self._build_msg = (
                "Cannot assign: Room is isolated — connect it to the station first."
            )
            self._build_msg_timer = 4.0
            return

        # Consume resources
        for res, amount in rt.resource_requirements.items():
            self.s.modify_resource(res, -amount)

        # Consume components from the first cargo hold that has them
        for item_id, qty_needed in rt.component_requirements.items():
            remaining = qty_needed
            for mod in self.s.modules.values():
                have = mod.inventory.get(item_id, 0)
                if have > 0:
                    used = min(have, remaining)
                    mod.inventory[item_id] -= used
                    if mod.inventory[item_id] == 0:
                        del mod.inventory[item_id]
                    remaining -= used
                if remaining <= 0:
                    break

        # Find or create a RoomInstance for this region, then assign type
        tm  = self.s.tile_map
        # Check if all tiles already belong to the same room
        room_uids = {tm.cells[pos].room_uid
                     for pos in region
                     if pos in tm.cells and tm.cells[pos].room_uid}
        if len(room_uids) == 1:
            room_uid = next(iter(room_uids))
        else:
            # Merge into a new room (removes any existing room references)
            for ruid in room_uids:
                tm.delete_room(ruid)
            name = f"{rt.display_name} {len(tm.rooms) + 1}"
            room = tm.create_room(name, region)
            room_uid = room.uid

        # Create or update the backing ModuleInstance if room type maps to a module
        module_uid = None
        if rt.module_id:
            module_def = self.game.registry.modules.get(rt.module_id)
            if module_def:
                module = ModuleInstance.create(
                    definition_id=rt.module_id,
                    display_name=tm.rooms[room_uid].name,
                    category=module_def.category,
                )
                self.s.add_module(module)
                module_uid = module.uid
                self._rebuild_layout()
                self._sync_dots()

        tm.assign_room_type(room_uid, room_type_id, module_uid)

        self._assign_popup  = False
        self._assign_region = []
        self._build_msg       = f"Assigned: {rt.display_name}"
        self._build_msg_timer = 2.5
        self.s.log_event(f"Build: {tm.rooms[room_uid].name} designated as {rt.display_name}.")

    # ── Build mode — place buildable popup ────────────────────────────────────

    def _place_popup_rect(self) -> pygame.Rect:
        """Return the pygame.Rect for the place-buildable popup panel."""
        pw, ph = 480, 300
        cx = T.FLOOR_X + (T.FLOOR_W - pw) // 2
        cy = T.FLOOR_Y + (T.FLOOR_H - ph) // 2
        return pygame.Rect(cx, cy, pw, ph)

    def _handle_place_popup_click(self, pos: tuple[int, int]) -> None:
        """Handle click inside the place-buildable selection popup."""
        popup_rect = self._place_popup_rect()
        if not popup_rect.collidepoint(pos):
            # Click outside popup on the tile grid → place selected buildable
            if self._place_selected:
                tile = self._pixel_to_tile(pos)
                if tile:
                    self._do_place_foundation(tile)
            return

        # Close button (top-right "X")
        close_rect = pygame.Rect(popup_rect.right - 26, popup_rect.y + 4, 22, 22)
        if close_rect.collidepoint(pos):
            self._place_popup = False
            return

        # Buildable rows — only show items whose required_tags are all active
        buildables = sorted(
            (
                b for b in self.game.registry.buildables.values()
                if all(self.s.has_tag(t) for t in b.required_tags)
            ),
            key=lambda b: b.display_name,
        )
        visible = buildables[self._place_scroll: self._place_scroll + 7]
        item_h = 42
        content_y = popup_rect.y + 38
        for i, defn in enumerate(visible):
            row_rect = pygame.Rect(
                popup_rect.x + 4, content_y + i * item_h,
                popup_rect.width - 8, item_h - 2,
            )
            if row_rect.collidepoint(pos):
                self._place_selected = defn.id
                self._place_popup = False
                self._build_msg = (
                    f"Selected: {defn.display_name} — click a tile to place"
                )
                self._build_msg_timer = 4.0
                return

    def _do_place_foundation(self, tile: tuple[int, int]) -> None:
        """Place a foundation at the given tile using the selected buildable."""
        if not self._place_selected:
            return
        building_sys = self.game.building_system
        if building_sys is None:
            return
        # Check if there's already a foundation on this tile
        for f in self.s.foundations.values():
            if f.tile_position == tile and f.status != "complete":
                self._build_msg = "A foundation already exists here."
                self._build_msg_timer = 2.5
                return
        building_sys.place_foundation(self.s, self._place_selected, tile)
        defn = self.game.registry.buildables.get(self._place_selected)
        name = defn.display_name if defn else self._place_selected
        self._build_msg = f"Foundation placed: {name} at {tile}"
        self._build_msg_timer = 2.5

    def _render_place_popup(self) -> None:
        """Render the buildable selection popup over the build grid."""
        popup = self._place_popup_rect()
        D.rect_rounded(self.screen, T.PANEL_BG, popup, 8)
        D.rect_outline(self.screen, T.ACCENT_WARM, popup, 2, 8)

        x = popup.x + 10
        y = popup.y + 8
        D.text(self.screen, self.fonts.md, "SELECT ITEM TO BUILD",
               (x, y), T.ACCENT_WARM)

        # Close button
        close_r = pygame.Rect(popup.right - 26, popup.y + 4, 22, 22)
        pygame.draw.rect(self.screen, T.DANGER, close_r, border_radius=4)
        D.text(self.screen, self.fonts.sm, "✕", close_r.center, T.TEXT_BRIGHT, "center")

        y += 26
        pygame.draw.line(self.screen, T.PANEL_EDGE,
                         (popup.x + 4, y), (popup.right - 4, y))
        y += 4

        buildables = sorted(
            (
                b for b in self.game.registry.buildables.values()
                if all(self.s.has_tag(t) for t in b.required_tags)
            ),
            key=lambda b: b.display_name,
        )
        visible = buildables[self._place_scroll: self._place_scroll + 7]
        item_h = 42

        mx, my = pygame.mouse.get_pos()
        for i, defn in enumerate(visible):
            row_rect = pygame.Rect(popup.x + 4, y + i * item_h,
                                   popup.width - 8, item_h - 2)
            is_hover    = row_rect.collidepoint(mx, my)
            is_selected = (defn.id == self._place_selected)
            bg = (T.BUILD_BTN_ACTIVE if is_selected
                  else T.BUILD_BTN_HOVER if is_hover
                  else T.BUILD_BTN_BG)
            pygame.draw.rect(self.screen, bg, row_rect, border_radius=3)

            # Row line 1: Name + category + build time
            cat_label  = f"[{defn.category}]"
            time_label = f"⏱{defn.build_time_ticks}t  sz:{defn.size}"
            D.text(self.screen, self.fonts.sm, defn.display_name[:20],
                   (row_rect.x + 6, row_rect.y + 6), T.TEXT_BRIGHT)
            D.text(self.screen, self.fonts.sm, cat_label,
                   (row_rect.x + 158, row_rect.y + 6), T.TEXT_DIM)
            D.text(self.screen, self.fonts.sm, time_label,
                   (row_rect.right - 90, row_rect.y + 6), T.ACCENT)

            # Row line 2: Material cost
            if defn.required_materials:
                mat_parts = []
                for iid, qty in defn.required_materials.items():
                    item_name = (self.game.registry.items[iid].display_name
                                 if iid in self.game.registry.items else iid)
                    mat_parts.append(f"{qty}× {item_name}")
                mats = "Needs: " + ", ".join(mat_parts)
            else:
                mats = "No materials required"
            D.text(self.screen, self.fonts.sm, mats[:52],
                   (row_rect.x + 6, row_rect.y + 22), T.TEXT_DIM)

        # Scroll hint
        if len(buildables) > 7:
            lo = self._place_scroll + 1
            hi = min(self._place_scroll + 7, len(buildables))
            hint = f"↑↓ scroll  {lo}–{hi}/{len(buildables)}"
            D.text(self.screen, self.fonts.sm, hint,
                   (popup.x + 8, popup.bottom - 20), T.TEXT_DIM)

        # Hint: show selected item or instruction
        if self._place_selected:
            sel_defn = self.game.registry.buildables.get(self._place_selected)
            if sel_defn:
                D.text(self.screen, self.fonts.sm,
                       f"✓ {sel_defn.display_name} — close popup, then click a tile to place",
                       (popup.x + 8, popup.bottom - 36), T.OK)
        else:
            D.text(self.screen, self.fonts.sm,
                   "Click a row to select an item, then click a tile to place it.",
                   (popup.x + 8, popup.bottom - 36), T.TEXT_DIM)

    def _render_foundations(self) -> None:
        """Render active foundation markers on the tile grid."""
        for foundation in self.s.foundations.values():
            if foundation.status == "complete":
                continue
            col, row = foundation.tile_position
            rect = self._tile_rect(col, row)
            defn = self.game.registry.buildables.get(foundation.buildable_id)

            # Draw dashed outline for the foundation
            dash_surf = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
            if foundation.status == "awaiting_haul":
                # Orange outline — waiting for materials
                outline_c = (255, 160, 40, 180)
            else:
                # Green outline — actively being built
                outline_c = (60, 220, 100, 180)
            dash_surf.fill((0, 0, 0, 0))
            pygame.draw.rect(dash_surf, outline_c,
                             (0, 0, rect.width, rect.height), 2)
            self.screen.blit(dash_surf, rect.topleft)

            # Progress bar (shown when constructing)
            if foundation.status == "constructing" and foundation.build_progress > 0:
                bar_h = 4
                bar_w = int(rect.width * foundation.build_progress)
                pygame.draw.rect(self.screen, (60, 220, 100),
                                 (rect.x, rect.bottom - bar_h, bar_w, bar_h))

            # Label
            label = defn.display_name[:8] if defn else foundation.buildable_id[:8]
            D.text(self.screen, self.fonts.sm, label,
                   rect.center, T.TEXT_BRIGHT, "center")



    def _render_build_mode(self) -> None:
        """Render the tile grid in build mode (replaces the normal floor plan)."""
        floor_rect = pygame.Rect(T.FLOOR_X, T.FLOOR_Y, T.FLOOR_W, T.FLOOR_H)
        self.screen.fill(T.BUILD_EMPTY_BG, floor_rect)

        # Draw faint grid guide lines
        for c in range(T.TILE_COLS + 1):
            x = T.FLOOR_X + c * T.TILE_W
            pygame.draw.line(self.screen, T.BUILD_GRID_LINE,
                             (x, T.FLOOR_Y), (x, T.FLOOR_Y + T.TILE_ROWS * T.TILE_H))
        for r in range(T.TILE_ROWS + 1):
            y = T.FLOOR_Y + r * T.TILE_H
            pygame.draw.line(self.screen, T.BUILD_GRID_LINE,
                             (T.FLOOR_X, y), (T.FLOOR_X + T.TILE_COLS * T.TILE_W, y))

        tm = self.s.tile_map

        # Determine drag highlight set
        drag_set: set[tuple[int, int]] = set()

        # Draw all placed tiles
        for (col, row), cell in tm.cells.items():
            if cell.tile_type == "empty":
                continue
            rect = self._tile_rect(col, row)

            if cell.tile_type == "floor":
                # Room tint overlay
                room_color = T.BUILD_FLOOR
                if cell.room_uid:
                    room = tm.rooms.get(cell.room_uid)
                    if room and room.room_type_id:
                        rt = self.game.registry.room_types.get(room.room_type_id)
                        if rt:
                            tint = T.BUILD_ROOM_TINTS.get(rt.category, T.BUILD_FLOOR)
                            room_color = tint

                # Alternating checker for readability
                alt = ((col + row) % 2 == 0)
                base = room_color if not alt else tuple(max(0, c - 8) for c in room_color)
                pygame.draw.rect(self.screen, base, rect)

                # Draw wall segments (thick lines on tile edges); doors = coloured gap
                seg_base = T.BUILD_WALL_LINE
                door_color = T.BUILD_DOOR
                for side, (x1, y1, x2, y2) in {
                    "N": (rect.x,        rect.y,          rect.right - 1,  rect.y),
                    "S": (rect.x,        rect.bottom - 1, rect.right - 1,  rect.bottom - 1),
                    "W": (rect.x,        rect.y,          rect.x,          rect.bottom - 1),
                    "E": (rect.right - 1, rect.y,          rect.right - 1,  rect.bottom - 1),
                }.items():
                    if not cell.walls.get(side, False):
                        continue
                    seg_hp = cell.wall_segment_hp.get(side, WALL_MAX_HP)
                    wc = _wall_hp_color(seg_base, seg_hp)
                    if cell.doors.get(side, False):
                        # Door: draw two short wall stubs with a coloured gap in centre
                        mid_x = (x1 + x2) // 2
                        mid_y = (y1 + y2) // 2
                        gap = 5  # pixels either side of centre
                        if side in ("N", "S"):
                            pygame.draw.line(self.screen, wc, (x1, y1), (mid_x - gap, y1), 2)
                            pygame.draw.line(self.screen, wc, (mid_x + gap, y1), (x2, y1), 2)
                            pygame.draw.line(self.screen, door_color, (mid_x - gap, y1), (mid_x + gap, y1), 2)
                        else:
                            pygame.draw.line(self.screen, wc, (x1, y1), (x1, mid_y - gap), 2)
                            pygame.draw.line(self.screen, wc, (x1, mid_y + gap), (x1, y2), 2)
                            pygame.draw.line(self.screen, door_color, (x1, mid_y - gap), (x1, mid_y + gap), 2)
                    else:
                        pygame.draw.line(self.screen, wc, (x1, y1), (x2, y2), 2)

            elif cell.tile_type == "wall":
                wall_fill = _wall_hp_color(T.BUILD_WALL_LINE, cell.wall_hp)
                pygame.draw.rect(self.screen, wall_fill, rect)
                wall_edge = tuple(min(255, c + 30) for c in wall_fill)
                pygame.draw.rect(self.screen, wall_edge, rect, 1)

        # Room name labels + atmosphere stats
        for room in tm.rooms.values():
            if not room.tile_positions:
                continue
            avg_c = sum(p[0] for p in room.tile_positions) // len(room.tile_positions)
            avg_r = sum(p[1] for p in room.tile_positions) // len(room.tile_positions)
            label_x = T.FLOOR_X + avg_c * T.TILE_W + T.TILE_W // 2
            label_y = T.FLOOR_Y + avg_r * T.TILE_H + T.TILE_H // 2
            if not (T.FLOOR_X <= label_x < T.FLOOR_X + T.FLOOR_W and
                    T.FLOOR_Y <= label_y < T.FLOOR_Y + T.FLOOR_H):
                continue
            rt = (self.game.registry.room_types.get(room.room_type_id)
                  if room.room_type_id else None)
            label = rt.display_name if rt else room.name
            D.text(self.screen, self.fonts.sm, label,
                   (label_x, label_y - 8), T.TEXT_BRIGHT, "center")
            # Atmosphere / temperature / beauty micro-stats
            atm_c = (T.OK if room.atmosphere >= 0.95 else
                     T.WARN if room.atmosphere >= 0.5 else T.DANGER)
            stats = (f"{room.atmosphere:.0%} atm  "
                     f"{room.temperature:.0f}°C  "
                     f"✦{room.beauty:.0f}")
            D.text(self.screen, self.fonts.sm, stats,
                   (label_x, label_y + 4), atm_c, "center")

        # Draw assign-region highlight
        if self._assign_region:
            for pos in self._assign_region:
                r = self._tile_rect(*pos)
                hl_surf = pygame.Surface((r.width, r.height), pygame.SRCALPHA)
                hl_surf.fill((80, 140, 255, 80))
                self.screen.blit(hl_surf, r.topleft)
                pygame.draw.rect(self.screen, (80, 140, 255), r, 1)

        # Draw foundation markers
        self._render_foundations()

        # Draw hover highlight
        if self._build_hover and not self._assign_popup and not self._place_popup:
            h = self._build_hover
            if 0 <= h[0] < T.TILE_COLS and 0 <= h[1] < T.TILE_ROWS:
                r = self._tile_rect(*h)
                if self._build_tool == "erase":
                    hl_c = (*T.BUILD_ERASE, 80)
                elif self._build_tool == "assign":
                    hl_c = (80, 140, 255, 80)
                elif self._build_tool == "place" and self._place_selected:
                    hl_c = (255, 160, 40, 80)
                else:
                    hl_c = (*T.BUILD_HOVER, 80)
                hl_surf = pygame.Surface((r.width, r.height), pygame.SRCALPHA)
                hl_surf.fill(hl_c)
                self.screen.blit(hl_surf, r.topleft)

        # Assign popup overlay
        if self._assign_popup:
            self._render_assign_popup()

        # Place buildable popup overlay
        if self._place_popup:
            self._render_place_popup()

    def _render_assign_popup(self) -> None:
        """Render the room-type assignment popup over the build grid."""
        popup = self._assign_popup_rect()
        D.rect_rounded(self.screen, T.PANEL_BG, popup, 8)
        D.rect_outline(self.screen, T.ACCENT, popup, 2, 8)

        x = popup.x + 10
        y = popup.y + 8
        tile_count = len(self._assign_region)
        D.text(self.screen, self.fonts.md, f"ASSIGN ROOM TYPE  ({tile_count} tiles)",
               (x, y), T.ACCENT)

        # Close button
        close_r = pygame.Rect(popup.right - 26, popup.y + 4, 22, 22)
        pygame.draw.rect(self.screen, T.DANGER, close_r, border_radius=4)
        D.text(self.screen, self.fonts.sm, "✕", close_r.center, T.TEXT_BRIGHT, "center")

        y += 24
        pygame.draw.line(self.screen, T.PANEL_EDGE,
                         (popup.x + 4, y), (popup.right - 4, y))
        y += 4

        room_types = sorted(self.game.registry.room_types.values(),
                            key=lambda rt: rt.display_name)
        visible = room_types[self._assign_scroll: self._assign_scroll + 7]
        item_h  = 28

        for i, rt in enumerate(visible):
            row_rect = pygame.Rect(popup.x + 4, y + i * item_h, popup.width - 8, item_h - 2)
            mx, my = pygame.mouse.get_pos()
            is_hover = row_rect.collidepoint(mx, my)

            ok, _reason = self._check_requirements(rt.id, tile_count)

            bg = T.BUILD_BTN_HOVER if is_hover else (T.BUILD_BTN_BG if ok else (30, 22, 22))
            pygame.draw.rect(self.screen, bg, row_rect, border_radius=3)

            req_c = T.BUILD_ASSIGN_OK if ok else T.BUILD_ASSIGN_FAIL
            D.text(self.screen, self.fonts.sm, rt.display_name[:28],
                   (row_rect.x + 6, row_rect.y + 7), T.TEXT if ok else T.TEXT_DIM)
            min_label = f"≥{rt.min_tiles}t"
            D.text(self.screen, self.fonts.sm, min_label,
                   (row_rect.right - 50, row_rect.y + 7), req_c)
            ok_label = "✓" if ok else "✗"
            D.text(self.screen, self.fonts.sm, ok_label,
                   (row_rect.right - 16, row_rect.y + 7), req_c)

        # Scroll hint
        if len(room_types) > 7:
            hint = f"↑↓ scroll  {self._assign_scroll + 1}–{min(self._assign_scroll + 7, len(room_types))}/{len(room_types)}"
            D.text(self.screen, self.fonts.sm, hint,
                   (popup.x + 8, popup.bottom - 18), T.TEXT_DIM)

    def _render_build_toolbar(self) -> None:
        """Render the build toolbar in the log panel area."""
        log_rect = pygame.Rect(0, T.SCREEN_H - T.LOG_H,
                               T.SCREEN_W - T.SIDEBAR_W, T.LOG_H)
        D.panel(self.screen, log_rect, 0)
        pygame.draw.line(self.screen, T.PANEL_EDGE,
                         (0, T.SCREEN_H - T.LOG_H),
                         (T.SCREEN_W - T.SIDEBAR_W, T.SCREEN_H - T.LOG_H))

        mx, my = pygame.mouse.get_pos()

        for tool_id, label, rect in self._build_toolbar_rects():
            active = (self._build_tool == tool_id)
            is_hov = rect.collidepoint(mx, my)
            bg = T.BUILD_BTN_ACTIVE if active else (T.BUILD_BTN_HOVER if is_hov else T.BUILD_BTN_BG)
            pygame.draw.rect(self.screen, bg, rect, border_radius=4)
            edge_c = T.OK if active else T.ACCENT
            pygame.draw.rect(self.screen, edge_c, rect, 1, border_radius=4)
            D.text(self.screen, self.fonts.sm, label, rect.center,
                   T.TEXT_BRIGHT if active else T.TEXT, "center")

        # Wall/door side selector (for wall_add, wall_remove, door tools)
        if self._build_tool in ("wall_add", "wall_remove", "door"):
            log_y = T.SCREEN_H - T.LOG_H
            side_label = "Door edge:" if self._build_tool == "door" else "Edge:"
            D.text(self.screen, self.fonts.sm, side_label,
                   (T.FLOOR_X + 8, log_y + 50), T.TEXT_DIM)
            for side, rect in self._build_wall_side_rects():
                active = (self._build_wall_side == side)
                is_hov = rect.collidepoint(mx, my)
                bg = T.BUILD_BTN_ACTIVE if active else (T.BUILD_BTN_HOVER if is_hov else T.BUILD_BTN_BG)
                pygame.draw.rect(self.screen, bg, rect, border_radius=3)
                D.text(self.screen, self.fonts.sm, side, rect.center,
                       T.TEXT_BRIGHT, "center")

        # Tile count / connectivity status
        tm = self.s.tile_map
        floor_count = sum(1 for c in tm.cells.values() if c.tile_type == "floor")
        room_count  = len(tm.rooms)
        connected   = tm.is_fully_connected()
        conn_label  = "" if connected or floor_count < 2 else "  ⚠ ISLAND DETECTED"
        conn_color  = T.TEXT_DIM if connected else T.DANGER
        hint = f"  Floor tiles: {floor_count}  |  Rooms defined: {room_count}{conn_label}"
        D.text(self.screen, self.fonts.sm, hint,
               (T.FLOOR_X + 8, T.SCREEN_H - 22), conn_color)

        # Keyboard hints
        D.text(self.screen, self.fonts.sm, "B / ESC = exit build mode",
               (T.FLOOR_X + 8, T.SCREEN_H - 38), T.TEXT_DIM)

        # Feedback message
        if self._build_msg:
            msg_c = (T.BUILD_ASSIGN_OK if "Assigned" in self._build_msg
                     else T.BUILD_ASSIGN_FAIL)
            D.text(self.screen, self.fonts.sm, self._build_msg,
                   (T.FLOOR_X + T.FLOOR_W // 2, T.SCREEN_H - 22), msg_c, "center")

    # ── Build mode ends — back to normal render flow ──────────────────────────

    def _build_event_buttons(self) -> None:
        self._event_btns = []
        pending = [p for p in self._pending if not p.resolved]
        if not pending:
            return
        p   = pending[0]
        ev  = p.definition
        bw  = 210
        bh  = 32
        gap = 8
        x   = T.FLOOR_X + 12
        y   = T.SCREEN_H - T.LOG_H + 60
        for choice in ev.choices:
            rect = pygame.Rect(x, y, bw, bh)
            self._event_btns.append({
                "rect": rect, "choice_id": choice.id,
                "label": choice.label, "pending": p,
            })
            x += bw + gap
            if x + bw > T.SCREEN_W - T.SIDEBAR_W - 10:
                x  = T.FLOOR_X + 12
                y += bh + gap

    # ── Render ─────────────────────────────────────────────────────────────────

    def _render(self) -> None:
        self.screen.fill(T.BG)
        if self._build_mode:
            self._render_build_mode()
        else:
            self._render_floor()
        self._render_top_bar()
        self._render_sidebar()
        if not self._build_mode:
            self._render_log_panel()
        else:
            self._render_build_toolbar()

        # Overlay panels (drawn on top of everything)
        if self._active_panel == "comms":
            self._render_comms_panel()
        elif self._active_panel == "work":
            self._render_work_panel()
        elif self._active_panel == "departments":
            self._render_departments_panel()

    # ── Floor plan ────────────────────────────────────────────────────────────

    def _render_station_hull(self) -> None:
        """Draw the station outer superstructure / hull behind all modules."""
        hull = self._hull_rect
        if hull is None:
            return

        # Main hull body
        pygame.draw.rect(self.screen, T.HULL_BG, hull, border_radius=12)

        # Structural spine lines (cross-braces)
        mid_x = hull.centerx
        mid_y = hull.centery
        pygame.draw.line(self.screen, T.HULL_EDGE,
                         (hull.left + 6,  mid_y), (hull.right - 6, mid_y), 2)
        pygame.draw.line(self.screen, T.HULL_EDGE,
                         (mid_x, hull.top + 6),   (mid_x, hull.bottom - 6), 2)

        # Hull frame border
        pygame.draw.rect(self.screen, T.HULL_EDGE, hull, 2, border_radius=12)

        # Corner reinforcement brackets
        bsize = 8
        for (bx, by) in [
            (hull.left + 4,       hull.top + 4),
            (hull.right - 4 - bsize, hull.top + 4),
            (hull.right - 4 - bsize, hull.bottom - 4 - bsize),
            (hull.left + 4,       hull.bottom - 4 - bsize),
        ]:
            pygame.draw.rect(self.screen, T.HULL_EDGE,
                             pygame.Rect(bx, by, bsize, bsize), 1)

    def _render_floor(self) -> None:
        floor_rect = pygame.Rect(T.FLOOR_X, T.FLOOR_Y, T.FLOOR_W, T.FLOOR_H)
        pygame.draw.rect(self.screen, T.FLOOR_BG, floor_rect)

        # Space atmosphere — nebula blobs first, then stars on top
        self._nebula.draw(self.screen)
        alpha = time_system.sky_alpha(self.s)
        self._stars.draw(self.screen, alpha)

        # Station structure
        self._render_station_hull()
        self._render_corridors()

        for uid, mod in self.s.modules.items():
            rect = self._mod_rects.get(uid)
            if rect:
                self._render_room(mod, rect, uid == self._selected_mod)

        self._render_npc_dots()
        self._render_incoming_lane()

    def _render_corridors(self) -> None:
        """Draw connecting passages between horizontally/vertically adjacent rooms."""
        rects = list(self._mod_rects.values())
        pad   = T.CELL_PAD

        for i, ra in enumerate(rects):
            for rb in rects[i+1:]:
                # Horizontal adjacency
                if abs(ra.right - rb.left) <= pad + 2 or abs(rb.right - ra.left) <= pad + 2:
                    oy1 = max(ra.top,  rb.top)
                    oy2 = min(ra.bottom, rb.bottom)
                    if oy2 > oy1 + 20:
                        cx   = (min(ra.right, rb.right) + max(ra.left, rb.left)) // 2
                        ch_w = max(pad, 6)
                        # Corridor body
                        pygame.draw.rect(self.screen, T.CORRIDOR,
                                         (cx - ch_w//2, oy1, ch_w, oy2 - oy1))
                        # Centre guide line
                        pygame.draw.line(self.screen,
                                         tuple(min(255, c + 12) for c in T.CORRIDOR),
                                         (cx, oy1 + 2), (cx, oy2 - 2), 1)
                        # Edge highlights
                        edge_c = tuple(min(255, c + 20) for c in T.CORRIDOR)
                        pygame.draw.line(self.screen, edge_c,
                                         (cx - ch_w//2, oy1), (cx - ch_w//2, oy2), 1)
                        pygame.draw.line(self.screen, edge_c,
                                         (cx + ch_w//2 - 1, oy1), (cx + ch_w//2 - 1, oy2), 1)

                # Vertical adjacency
                if abs(ra.bottom - rb.top) <= pad + 2 or abs(rb.bottom - ra.top) <= pad + 2:
                    ox1 = max(ra.left,  rb.left)
                    ox2 = min(ra.right, rb.right)
                    if ox2 > ox1 + 20:
                        cy   = (min(ra.bottom, rb.bottom) + max(ra.top, rb.top)) // 2
                        cv_h = max(pad, 6)
                        # Corridor body
                        pygame.draw.rect(self.screen, T.CORRIDOR,
                                         (ox1, cy - cv_h//2, ox2 - ox1, cv_h))
                        # Centre guide line
                        pygame.draw.line(self.screen,
                                         tuple(min(255, c + 12) for c in T.CORRIDOR),
                                         (ox1 + 2, cy), (ox2 - 2, cy), 1)
                        # Edge highlights
                        edge_c = tuple(min(255, c + 20) for c in T.CORRIDOR)
                        pygame.draw.line(self.screen, edge_c,
                                         (ox1, cy - cv_h//2), (ox2, cy - cv_h//2), 1)
                        pygame.draw.line(self.screen, edge_c,
                                         (ox1, cy + cv_h//2 - 1), (ox2, cy + cv_h//2 - 1), 1)

    def _render_room(self, mod: "ModuleInstance", rect: pygame.Rect,
                     selected: bool) -> None:
        cat   = mod.category
        floor = T.MODULE_FLOOR.get(cat, T.MODULE_FLOOR["utility"])
        wall  = T.MODULE_WALL.get(cat,  T.MODULE_WALL["utility"])
        label = T.MODULE_LABEL.get(cat, T.MODULE_LABEL["utility"])

        # Damage desaturates the room
        if mod.damage > 0:
            d = mod.damage
            floor = tuple(int(c * (1 - d * 0.5)) for c in floor)
            wall  = tuple(int(c * (1 - d * 0.5)) for c in wall)

        # Floor fill
        pygame.draw.rect(self.screen, floor, rect, border_radius=6)

        # Inner shadow — thin darker border to create depth
        inner = rect.inflate(-4, -4)
        pygame.draw.rect(self.screen, tuple(max(0, c-10) for c in floor), inner, border_radius=4)

        # Room grid / floor-tile lines (subtle texture)
        h_line_color = tuple(min(255, c + 6) for c in floor)
        v_line_color = tuple(min(255, c + 4) for c in floor)
        for gy in range(rect.y + 10, rect.bottom, 20):
            pygame.draw.line(self.screen, h_line_color,
                             (rect.x + 4, gy), (rect.right - 4, gy))
        for gx in range(rect.x + 10, rect.right, 20):
            pygame.draw.line(self.screen, v_line_color,
                             (gx, rect.y + 4), (gx, rect.bottom - 4))

        # Wall border
        border_w = 3 if selected else 2
        border_c = T.ACCENT if selected else wall
        pygame.draw.rect(self.screen, border_c, rect, border_w, border_radius=6)

        # Offline overlay
        if not mod.active:
            s = pygame.Surface(rect.size, pygame.SRCALPHA)
            s.fill((0, 0, 0, 120))
            self.screen.blit(s, rect.topleft)

        # Title
        D.text(self.screen, self.fonts.md,
               mod.display_name, (rect.x + 8, rect.y + 7), T.TEXT_BRIGHT)

        # Category sub-label
        D.text(self.screen, self.fonts.sm,
               cat.upper(), (rect.x + 8, rect.y + 22), label)

        # Module-type icon (centre of room, below title area)
        icon_x = rect.centerx
        icon_y = rect.centery + 12
        self._draw_module_icon(mod.definition_id, icon_x, icon_y, label)

        # Dock ship status
        if mod.is_dock():
            if mod.docked_ship:
                ship = self.s.ships.get(mod.docked_ship)
                name = ship.name[:20] if ship else "DOCKED"
                D.text(self.screen, self.fonts.sm, name,
                       (rect.right - 8, rect.y + 7), T.OK, "topright")
                if ship:
                    intent_c = T.INTENT_COLOR.get(ship.intent, T.TEXT_DIM)
                    D.text(self.screen, self.fonts.sm, ship.intent,
                           (rect.right - 8, rect.y + 20), intent_c, "topright")
            else:
                D.text(self.screen, self.fonts.sm, "OPEN",
                       (rect.right - 8, rect.y + 7), T.TEXT_DIM, "topright")

        # Damage badge
        if mod.damage >= 0.01:
            D.text(self.screen, self.fonts.sm, f"DMG {mod.damage:.0%}",
                   (rect.right - 8, rect.y + 7), T.DANGER, "topright")

        # Cargo capacity badge (shown on cargo holds)
        if self.game.inventory_system:
            defn = self.game.registry.modules.get(mod.definition_id)
            if defn and defn.cargo_capacity > 0:
                used = self.game.inventory_system.get_capacity_used(mod)
                cap  = defn.cargo_capacity
                pct  = used / cap if cap > 0 else 0.0
                cap_c = (T.DANGER if pct >= 0.95 else
                         T.WARN   if pct >= 0.75 else T.OK)
                cap_str = f"{used:.0f}/{cap}"
                # Offset below damage badge if damage is visible to avoid overlap
                cap_y = rect.y + 20 if mod.damage >= 0.01 else rect.y + 7
                D.text(self.screen, self.fonts.sm, cap_str,
                       (rect.right - 8, cap_y), cap_c, "topright")
                # Capacity bar across bottom of room
                bar_rect = pygame.Rect(rect.x + 4, rect.bottom - 8, rect.width - 8, 4)
                D.hbar(self.screen, bar_rect, used, cap, cap_c)

        # Resource delta summary (bottom of tile, above capacity bar if present)
        defn = self.game.registry.modules.get(mod.definition_id)
        if defn and defn.resource_effects:
            fx_str = "  ".join(
                f"{'+'if d>=0 else ''}{d:.0f}{r[:3]}"
                for r, d in list(defn.resource_effects.items())[:3]
            )
            D.text(self.screen, self.fonts.sm, fx_str,
                   (rect.x + 8, rect.bottom - 16), T.TEXT_DIM)

    def _draw_module_icon(self, definition_id: str, cx: int, cy: int,
                          color: tuple) -> None:
        """Draw a small symbolic icon for the module type."""
        mid = definition_id.lower()
        surf = self.screen

        if "command" in mid:
            # Crosshair / targeting reticle
            pygame.draw.circle(surf, color, (cx, cy), 9, 1)
            pygame.draw.circle(surf, color, (cx, cy), 3)
            pygame.draw.line(surf, color, (cx - 13, cy), (cx - 10, cy), 1)
            pygame.draw.line(surf, color, (cx + 10, cy), (cx + 13, cy), 1)
            pygame.draw.line(surf, color, (cx, cy - 13), (cx, cy - 10), 1)
            pygame.draw.line(surf, color, (cx, cy + 10), (cx, cy + 13), 1)

        elif "dock" in mid:
            # Arrow pointing left (ship entering)
            pts = [(cx + 10, cy - 5), (cx + 2, cy - 5),
                   (cx + 2, cy - 9),  (cx - 10, cy),
                   (cx + 2, cy + 9),  (cx + 2, cy + 5),
                   (cx + 10, cy + 5)]
            pygame.draw.polygon(surf, color, pts, 1)

        elif "power" in mid:
            # Lightning bolt
            pts = [(cx + 3, cy - 11), (cx - 4,  cy - 1),
                   (cx + 1, cy -  1), (cx - 3,  cy + 11),
                   (cx + 4, cy +  1), (cx - 1,  cy + 1)]
            pygame.draw.polygon(surf, color, pts)

        elif "med" in mid:
            # Medical cross
            pygame.draw.rect(surf, color, pygame.Rect(cx - 2, cy - 9, 4, 18))
            pygame.draw.rect(surf, color, pygame.Rect(cx - 9, cy - 2, 18, 4))

        elif "security" in mid:
            # Shield outline
            pts = [(cx, cy - 10), (cx + 9, cy - 6),
                   (cx + 9, cy + 2), (cx,    cy + 10),
                   (cx - 9, cy + 2), (cx - 9, cy - 6)]
            pygame.draw.polygon(surf, color, pts, 1)
            pygame.draw.line(surf, color, (cx, cy - 6), (cx, cy + 5), 1)

        elif "quarters" in mid or "hab" in mid:
            # Bed outline
            pygame.draw.rect(surf, color, pygame.Rect(cx - 9, cy - 4, 18, 9), 1)
            pygame.draw.rect(surf, color, pygame.Rect(cx - 9, cy - 7, 5, 11), 1)

        elif "hydro" in mid:
            # Stylised plant: stem + two leaves
            pygame.draw.line(surf, color, (cx, cy + 9), (cx, cy - 4), 2)
            pygame.draw.arc(surf, color,
                            pygame.Rect(cx - 9, cy - 9, 12, 10),
                            0, math.pi, 2)
            pygame.draw.arc(surf, color,
                            pygame.Rect(cx - 3, cy - 6, 12, 10),
                            math.pi, math.pi * 2, 2)

        elif "storage" in mid:
            # Crate / box
            pygame.draw.rect(surf, color, pygame.Rect(cx - 8, cy - 7, 16, 14), 1)
            pygame.draw.line(surf, color, (cx - 8, cy), (cx + 8, cy), 1)
            pygame.draw.line(surf, color, (cx, cy - 7), (cx, cy), 1)

        elif "cargo" in mid:
            # Stacked crates icon (two boxes)
            pygame.draw.rect(surf, color, pygame.Rect(cx - 9, cy - 2, 10, 9), 1)
            pygame.draw.rect(surf, color, pygame.Rect(cx - 1, cy - 8, 10, 9), 1)
            pygame.draw.line(surf, color, (cx - 9, cy + 2), (cx + 1, cy + 2), 1)
            pygame.draw.line(surf, color, (cx + 4, cy - 8), (cx + 4, cy + 1), 1)

        elif "lounge" in mid or "visitor" in mid:
            # Two small person silhouettes
            for ox in (-5, 5):
                pygame.draw.circle(surf, color, (cx + ox, cy - 5), 3)
                pygame.draw.ellipse(surf, color,
                                    pygame.Rect(cx + ox - 4, cy - 1, 8, 6))

        else:
            # Generic: small gear ring
            pygame.draw.circle(surf, color, (cx, cy), 7, 2)
            pygame.draw.circle(surf, color, (cx, cy), 3)

    def _render_npc_dots(self) -> None:
        mx, my = pygame.mouse.get_pos()
        for uid, dot in self._dots.items():
            npc  = self.s.npcs.get(uid)
            pos  = dot.draw_pos()
            # Clip to floor area
            if not (T.FLOOR_X <= pos[0] < T.FLOOR_X + T.FLOOR_W and
                    T.FLOOR_Y <= pos[1] < T.FLOOR_Y + T.FLOOR_H):
                continue

            working = bool(npc and npc.current_job_id and npc.job_timer > 0)

            # Working pulse ring (drawn beneath the sprite)
            if working:
                t = pygame.time.get_ticks() / 1000.0
                pulse = int(10 + 4 * abs(math.sin(t * 2)))
                pygame.draw.circle(self.screen, (*dot.color[:3], self._PULSE_ALPHA),
                                   pos, pulse + 4)

            # Humanoid sprite
            self._draw_npc_sprite(pos, dot.color, working)

            # Hover tooltip: name + job
            if npc and math.hypot(mx - pos[0], my - pos[1]) < 14:
                job_label = self.game.job_system.get_job_label(npc) if self.game.job_system else ""
                self._draw_tooltip(
                    f"{npc.name}  [{npc.class_id.replace('class.','')}]",
                    f"{job_label}  mood:{npc.mood_label()}",
                    (pos[0] + 12, pos[1] - 14)
                )

    def _draw_npc_sprite(self, pos: tuple[int, int], color: tuple,
                         working: bool) -> None:
        """Draw a small top-down humanoid figure (head + body).

        If `working` is True, a small activity indicator is drawn above the
        head to show the NPC is actively carrying out a job.
        """
        cx, cy = pos
        surf   = self.screen

        # Darken colour for body (torso in shade)
        body_color = tuple(max(0, c - self._BODY_DARKEN) for c in color[:3])

        # Body (slightly wider oval below head)
        pygame.draw.ellipse(surf, body_color,
                            pygame.Rect(cx - 5, cy, 10, 7))

        # Head (circle, above body)
        pygame.draw.circle(surf, color, (cx, cy - 4), 5)

        # Head outline for definition
        pygame.draw.circle(surf, (0, 0, 0), (cx, cy - 4), 5, 1)

        # Bright highlight on head (top-left)
        hi = tuple(min(255, c + self._HEAD_HIGHLIGHT) for c in color[:3])
        pygame.draw.circle(surf, hi, (cx - 1, cy - 5), 2)

        # Activity indicator: small diamond above the head while working
        if working:
            dy = cy - 14
            diamond = [(cx, dy - 3), (cx + 3, dy), (cx, dy + 3), (cx - 3, dy)]
            pygame.draw.polygon(surf, T.OK, diamond)

    def _draw_tooltip(self, line1: str, line2: str, pos: tuple) -> None:
        tw = max(self.fonts.sm.size(line1)[0], self.fonts.sm.size(line2)[0]) + 12
        th = 34
        tx = min(pos[0], T.SCREEN_W - tw - 4)
        ty = max(0, pos[1] - th)
        bg = pygame.Rect(tx, ty, tw, th)
        D.rect_rounded(self.screen, T.PANEL_BG, bg, 4)
        D.rect_outline(self.screen, T.PANEL_EDGE, bg, 1, 4)
        D.text(self.screen, self.fonts.sm, line1, (tx + 6, ty + 4), T.TEXT_BRIGHT)
        D.text(self.screen, self.fonts.sm, line2, (tx + 6, ty + 18), T.TEXT_DIM)

    def _render_incoming_lane(self) -> None:
        """Approaching ships shown as silhouettes in the right approach corridor."""
        incoming = self.s.get_incoming_ships()
        if not incoming:
            return

        t   = pygame.time.get_ticks() / 1000.0

        # Approach corridor — right strip of the floor area
        lane_x  = T.FLOOR_X + T.FLOOR_W - 185
        lane_x2 = T.FLOOR_X + T.FLOOR_W - 8
        lane_y  = T.FLOOR_Y + 12

        # Lane header
        D.text(self.screen, self.fonts.sm, "APPROACH",
               (lane_x, lane_y), T.ACCENT_WARM)
        lane_y += 18

        # Subtle separator
        pygame.draw.line(self.screen, T.HULL_EDGE,
                         (lane_x, lane_y), (lane_x2, lane_y), 1)
        lane_y += 6

        for i, ship in enumerate(incoming[:5]):
            ic = T.INTENT_COLOR.get(ship.intent, T.TEXT_DIM)
            tc = (T.DANGER if ship.threat_level >= 6 else
                  T.WARN   if ship.threat_level >= 3 else T.TEXT_DIM)

            # Animate ship x position (gentle approach drift)
            drift    = abs(math.sin(t * 0.35 + i * 1.4))
            ship_x   = int(lane_x2 - 18 - drift * 28)
            ship_y   = lane_y + 10

            # Approach trail
            trail_x0 = lane_x2
            trail_c  = tuple(c // 5 for c in ic[:3])
            pygame.draw.line(self.screen, trail_c,
                             (ship_x + 14, ship_y), (trail_x0, ship_y), 1)

            # Ship silhouette (simple polygon pointing left)
            self._draw_ship_shape(ship_x, ship_y, ic)

            # Engine glow (rightmost point of the ship)
            glow_t   = 0.6 + 0.4 * abs(math.sin(t * 3.0 + i))
            glow_c   = tuple(int(c * glow_t) for c in ic[:3])
            pygame.draw.circle(self.screen, glow_c, (ship_x + 14, ship_y), 3)

            # Labels
            D.text(self.screen, self.fonts.sm, ship.name[:16],
                   (ship_x - 4, ship_y - 10), ic, "topright")
            D.text(self.screen, self.fonts.sm,
                   f"{ship.role} / {ship.intent}",
                   (ship_x - 4, ship_y + 8), T.TEXT_DIM, "topright")
            if ship.threat_level > 0:
                D.text(self.screen, self.fonts.sm,
                       f"threat {ship.threat_label()}",
                       (ship_x - 4, ship_y + 20), tc, "topright")
                lane_y += 38
            else:
                lane_y += 32
            lane_y += 8

    def _draw_ship_shape(self, cx: int, cy: int, color: tuple) -> None:
        """Draw a simple spacecraft silhouette pointing left (toward the station)."""
        # Main hull — elongated diamond pointing left
        hull_pts = [
            (cx - 14, cy),           # nose (left)
            (cx -  2, cy - 6),       # top-front
            (cx + 14, cy - 3),       # top-rear
            (cx + 14, cy + 3),       # bottom-rear
            (cx -  2, cy + 6),       # bottom-front
        ]
        pygame.draw.polygon(self.screen, color, hull_pts)
        pygame.draw.polygon(self.screen, (0, 0, 0), hull_pts, 1)

        # Wing fin (top)
        wing_top = [
            (cx + 4, cy - 5),
            (cx + 14, cy - 10),
            (cx + 14, cy - 3),
        ]
        wing_c = tuple(max(0, c - 40) for c in color[:3])
        pygame.draw.polygon(self.screen, wing_c, wing_top)

        # Wing fin (bottom)
        wing_bot = [
            (cx + 4,  cy + 5),
            (cx + 14, cy + 10),
            (cx + 14, cy + 3),
        ]
        pygame.draw.polygon(self.screen, wing_c, wing_bot)

    # ── Top bar ───────────────────────────────────────────────────────────────

    def _render_top_bar(self) -> None:
        bar = pygame.Rect(0, 0, T.SCREEN_W, T.TOP_BAR_H)
        pygame.draw.rect(self.screen, T.PANEL_BG, bar)
        pygame.draw.line(self.screen, T.PANEL_EDGE,
                         (0, T.TOP_BAR_H), (T.SCREEN_W, T.TOP_BAR_H))

        D.text(self.screen, self.fonts.xl, self.s.name,
               (12, 10), T.ACCENT, "topleft")

        time_str = time_system.time_label(self.s)
        D.text(self.screen, self.fonts.md, time_str, (260, 14), T.TEXT_DIM)

        # Pending event notification button (pulsing)
        self._render_event_notification()

        # Speed buttons
        speed_labels = {0: "PAUSE", 1: "x1", 2: "x2", 4: "x4"}
        for speed, rect in self._speed_btn_rects().items():
            is_active = self._speed == speed
            bg = T.OK if is_active else T.PANEL_EDGE
            pygame.draw.rect(self.screen, bg, rect, border_radius=4)
            pygame.draw.rect(self.screen, T.PANEL_EDGE if not is_active else T.OK,
                             rect, 1, border_radius=4)
            D.text(self.screen, self.fonts.sm, speed_labels[speed],
                   rect.center,
                   T.BG if is_active else T.TEXT, "center")

        # Save button
        save_rect = self._save_btn_rect()
        mx, my = pygame.mouse.get_pos()
        save_hov = save_rect.collidepoint(mx, my)
        save_bg  = T.ACCENT_WARM if save_hov else T.PANEL_EDGE
        pygame.draw.rect(self.screen, save_bg, save_rect, border_radius=4)
        pygame.draw.rect(self.screen, T.ACCENT_WARM, save_rect, 1, border_radius=4)
        D.text(self.screen, self.fonts.sm, "SAVE",
               save_rect.center, T.BG if save_hov else T.TEXT, "center")

        # Menu button
        menu_rect = self._menu_btn_rect()
        menu_hov  = menu_rect.collidepoint(mx, my)
        menu_bg   = T.ACCENT if menu_hov else T.PANEL_EDGE
        pygame.draw.rect(self.screen, menu_bg, menu_rect, border_radius=4)
        pygame.draw.rect(self.screen, T.ACCENT, menu_rect, 1, border_radius=4)
        D.text(self.screen, self.fonts.sm, "MENU",
               menu_rect.center, T.BG if menu_hov else T.TEXT, "center")

        # Build mode button
        build_rect = self._build_btn_rect()
        build_hov  = build_rect.collidepoint(mx, my)
        build_active = self._build_mode
        build_bg = T.OK if build_active else (T.BUILD_BTN_HOVER if build_hov else T.BUILD_BTN_BG)
        pygame.draw.rect(self.screen, build_bg, build_rect, border_radius=4)
        edge_c = T.OK if build_active else T.ACCENT
        pygame.draw.rect(self.screen, edge_c, build_rect, 1, border_radius=4)
        D.text(self.screen, self.fonts.sm, "BUILD [B]",
               build_rect.center, T.BG if build_active else T.TEXT, "center")

        # Save feedback message
        if self._save_msg:
            D.text(self.screen, self.fonts.sm, self._save_msg,
                   (save_rect.left, save_rect.bottom + 2), T.OK)

        # Tick counter
        D.text(self.screen, self.fonts.sm, f"Tick {self.s.tick:04d}",
               (T.SCREEN_W - T.SIDEBAR_W - 8, 14), T.TEXT_DIM, "topright")

    def _render_event_notification(self) -> None:
        """Pulsing notification button in the top bar when an event is available."""
        pending = [p for p in self._pending if not p.resolved]
        if not pending:
            return

        p = pending[0]
        is_hostile = p.definition.hostile
        base_color = T.DANGER if is_hostile else T.WARN

        t = pygame.time.get_ticks() / 1000.0
        pulse = abs(math.sin(t * (3.0 if is_hostile else 2.0)))

        # Position the button so it never overlaps the speed buttons on the right.
        _MAX_NOTIFICATION_W = 310
        btn_x = 440
        btn_y = 7
        btn_h = 29
        padding = 8
        speed_rects = list(self._speed_btn_rects().values())
        if speed_rects:
            speed_left = min(r.left for r in speed_rects)
            btn_w = min(_MAX_NOTIFICATION_W, speed_left - btn_x - padding)
        else:
            btn_w = _MAX_NOTIFICATION_W
        if btn_w <= 0:
            return
        btn_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)

        # Pulsing translucent background
        bg_alpha = int(50 + 90 * pulse)
        bg_surf = pygame.Surface((btn_rect.width, btn_rect.height), pygame.SRCALPHA)
        bg_surf.fill((*base_color, bg_alpha))
        self.screen.blit(bg_surf, btn_rect.topleft)

        # Pulsing border (thickens at peak)
        border_w = 1 + int(2 * pulse)
        pygame.draw.rect(self.screen, base_color, btn_rect, border_w, border_radius=4)

        # Label — event title truncated, with count if multiple queued
        count_str = f"  (+{len(pending) - 1})" if len(pending) > 1 else ""
        label = f"!  {p.definition.title[:30]}{count_str}"
        D.text(self.screen, self.fonts.md, label, btn_rect.center, T.TEXT_BRIGHT, "center")

    # ── Sidebar ───────────────────────────────────────────────────────────────

    def _render_sidebar(self) -> None:
        sx = T.SCREEN_W - T.SIDEBAR_W
        rect = pygame.Rect(sx, 0, T.SIDEBAR_W, T.SCREEN_H)
        D.panel(self.screen, rect, 0)
        pygame.draw.line(self.screen, T.PANEL_EDGE, (sx, 0), (sx, T.SCREEN_H))

        x  = sx + 10
        rw = T.SIDEBAR_W - 20
        y  = T.TOP_BAR_H + 8

        # ── Resources ──
        D.text(self.screen, self.fonts.lg, "RESOURCES", (x, y), T.ACCENT)
        y += 22

        CAPS  = {"credits":10000,"food":500,"power":500,"oxygen":500,"parts":200,"ice":500}
        WARNS = {"credits":100,  "food":40, "power":20, "oxygen":20, "parts":10, "ice":40}

        for res, val in sorted(self.s.resources.items()):
            cap  = CAPS.get(res, 500)
            warn = WARNS.get(res, 20)
            fc   = T.DANGER if val <= 0 else T.WARN if val < warn else T.OK
            D.text(self.screen, self.fonts.sm, res.upper(), (x, y), T.TEXT_DIM)
            D.text(self.screen, self.fonts.sm, f"{val:.0f}", (x+rw, y), fc, "topright")
            y += 13
            D.hbar(self.screen, pygame.Rect(x, y, rw, 5), val, cap, fc)
            y += 10

        y += 6
        D.divider(self.screen, x, y, x+rw, y)
        y += 8

        # ── Crew + jobs ──
        D.text(self.screen, self.fonts.lg, "CREW", (x, y), T.ACCENT)
        # Work button
        work_btn_rect = pygame.Rect(x + rw - 70, y - 2, 70, 20)
        work_active = self._active_panel == "work"
        D.rect_rounded(self.screen, T.BUILD_BTN_ACTIVE if work_active else T.BUILD_BTN_BG,
                       work_btn_rect, 4)
        D.rect_outline(self.screen, T.PANEL_EDGE, work_btn_rect, 1, 4)
        D.text(self.screen, self.fonts.sm, "WORK", work_btn_rect.center, T.TEXT, "center")
        self._work_btn_rect = work_btn_rect
        y += 22

        for npc in self.s.get_crew()[:8]:
            dc = T.CLASS_COLOR.get(npc.class_id, T.TEXT)
            D.dot(self.screen, dc, (x+6, y+6), 5)
            mc = T.OK if npc.mood > 0.2 else T.WARN if npc.mood > -0.2 else T.DANGER
            D.text(self.screen, self.fonts.sm, npc.name[:14], (x+16, y), T.TEXT)
            D.text(self.screen, self.fonts.sm, npc.mood_label(), (x+rw, y), mc, "topright")
            y += 14
            # Job label
            job_label = (self.game.job_system.get_job_label(npc)
                         if self.game.job_system else "—")
            D.text(self.screen, self.fonts.sm, f"  {job_label}",
                   (x+16, y), T.TEXT_DIM)
            y += 14

        visitors = self.s.get_visitors()
        if visitors:
            D.text(self.screen, self.fonts.sm,
                   f"Visitors: {len(visitors)}", (x, y), T.TEXT_DIM)
            y += 14

        y += 4
        D.divider(self.screen, x, y, x+rw, y)
        y += 8

        # ── Docked ships ──
        docked = self.s.get_docked_ships()
        if docked:
            D.text(self.screen, self.fonts.lg, "DOCKED", (x, y), T.ACCENT)
            y += 20
            for ship in docked[:3]:
                ic = T.INTENT_COLOR.get(ship.intent, T.TEXT_DIM)
                D.text(self.screen, self.fonts.sm, ship.name[:22], (x, y), ic)
                y += 13
                D.text(self.screen, self.fonts.sm,
                       f"  {ship.role} · {(ship.faction_id or 'unknown').replace('faction.','')}",
                       (x, y), T.TEXT_DIM)
                y += 15
            y += 4
            D.divider(self.screen, x, y, x+rw, y)
            y += 8

        # ── Factions ──
        D.text(self.screen, self.fonts.lg, "FACTIONS", (x, y), T.ACCENT)
        y += 20

        for fid, defn in sorted(self.game.registry.factions.items()):
            rep = self.s.get_faction_rep(fid)
            fc  = (T.OK if rep >= 40 else T.ACCENT if rep >= 10
                   else T.TEXT if rep >= -20 else T.WARN if rep >= -50 else T.DANGER)
            D.text(self.screen, self.fonts.sm,
                   defn.display_name[:18], (x, y), T.TEXT_DIM)
            D.text(self.screen, self.fonts.sm, f"{rep:+.0f}", (x+rw, y), fc, "topright")
            y += 12
            D.hbar(self.screen, pygame.Rect(x, y, rw, 4), rep+100, 200, fc)
            y += 10

        # ── Active tags ──
        if self.s.active_tags:
            y += 4
            D.divider(self.screen, x, y, x+rw, y)
            y += 8
            for tag in sorted(self.s.active_tags):
                D.text(self.screen, self.fonts.sm, f"[{tag}]", (x, y), T.WARN)
                y += 13

        # ── Comms button ──
        y += 8
        unread = getattr(self.s, 'unread_message_count', lambda: 0)()
        comms_active = self._active_panel == "comms"
        flash = bool(unread > 0 and int(pygame.time.get_ticks() / 400) % 2 == 0)
        btn_col = T.WARN if flash else (T.BUILD_BTN_ACTIVE if comms_active else T.BUILD_BTN_BG)
        comms_btn_rect = pygame.Rect(x, y, rw, 26)
        D.rect_rounded(self.screen, btn_col, comms_btn_rect, 5)
        D.rect_outline(self.screen, T.PANEL_EDGE, comms_btn_rect, 1, 5)
        label = f"COMMS  [{unread} unread]" if unread else "COMMS"
        D.text(self.screen, self.fonts.sm, label, comms_btn_rect.center, T.TEXT, "center")
        self._comms_btn_rect = comms_btn_rect

    # ── Log / event panel ─────────────────────────────────────────────────────

    def _render_log_panel(self) -> None:
        rect = pygame.Rect(0, T.SCREEN_H - T.LOG_H, T.SCREEN_W - T.SIDEBAR_W, T.LOG_H)
        D.panel(self.screen, rect, 0)
        pygame.draw.line(self.screen, T.PANEL_EDGE,
                         (0, T.SCREEN_H - T.LOG_H),
                         (T.SCREEN_W - T.SIDEBAR_W, T.SCREEN_H - T.LOG_H))

        pending = [p for p in self._pending if not p.resolved]
        if pending:
            self._render_event_card(pending[0], rect)
        elif self._selected_mod and self._is_cargo_hold(self._selected_mod):
            self._render_cargo_panel(self._selected_mod, rect)
        else:
            self._render_log_feed(rect)

    # ── Overlay panels ────────────────────────────────────────────────────────

    def _render_comms_panel(self) -> None:
        """Draw the communications inbox as a full-screen overlay drawer."""
        overlay = pygame.Surface((T.SCREEN_W, T.SCREEN_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 160))
        self.screen.blit(overlay, (0, 0))

        PW, PH = 820, 580
        px = (T.SCREEN_W - PW) // 2
        py = (T.SCREEN_H - PH) // 2
        panel_rect = pygame.Rect(px, py, PW, PH)
        D.panel(self.screen, panel_rect, 10)

        D.text(self.screen, self.fonts.xl, "COMMUNICATIONS", (px + 16, py + 14), T.ACCENT)

        close_rect = pygame.Rect(px + PW - 38, py + 10, 28, 28)
        D.rect_rounded(self.screen, (80, 30, 30), close_rect, 4)
        D.text(self.screen, self.fonts.md, "\u2715", close_rect.center, T.DANGER, "center")
        self._comms_close_rect = close_rect

        tabs = [("unread", "Unread"), ("read", "Read"), ("all", "All")]
        tab_y = py + 50
        tab_x = px + 16
        self._comms_tab_rects = []
        for tab_id, tab_label in tabs:
            tw = 90
            tab_rect = pygame.Rect(tab_x, tab_y, tw, 24)
            active = self._comms_tab == tab_id
            D.rect_rounded(self.screen,
                           T.BUILD_BTN_ACTIVE if active else T.BUILD_BTN_BG,
                           tab_rect, 4)
            D.rect_outline(self.screen, T.PANEL_EDGE, tab_rect, 1, 4)
            D.text(self.screen, self.fonts.sm, tab_label,
                   tab_rect.center, T.TEXT if active else T.TEXT_DIM, "center")
            self._comms_tab_rects.append((tab_id, tab_rect))
            tab_x += tw + 6

        LIST_W = 280
        list_rect = pygame.Rect(px + 10, tab_y + 32, LIST_W, PH - 100)
        D.rect_rounded(self.screen, T.FLOOR_BG, list_rect, 4)
        D.rect_outline(self.screen, T.PANEL_EDGE, list_rect, 1, 4)

        msgs = getattr(self.s, 'messages', [])
        if self._comms_tab == "unread":
            visible_msgs = [m for m in msgs if not m.read]
        elif self._comms_tab == "read":
            visible_msgs = [m for m in msgs if m.read]
        else:
            visible_msgs = list(msgs)

        self._comms_msg_rects = []

        my = list_rect.y + 6
        max_visible = 12
        start = self._comms_scroll
        for i, msg in enumerate(visible_msgs[start:start + max_visible]):
            item_rect = pygame.Rect(list_rect.x + 4, my, LIST_W - 8, 38)
            is_selected = self._comms_selected_uid == msg.uid
            is_unread = not msg.read
            bg_col = T.BUILD_BTN_ACTIVE if is_selected else (
                (30, 42, 72) if is_unread else T.BUILD_BTN_BG
            )
            D.rect_rounded(self.screen, bg_col, item_rect, 3)
            if is_unread:
                D.dot(self.screen, T.WARN, (item_rect.x + 10, item_rect.centery), 4)
            subj = msg.subject[:28] if len(msg.subject) > 28 else msg.subject
            D.text(self.screen, self.fonts.sm, subj,
                   (item_rect.x + 20, item_rect.y + 6), T.TEXT_BRIGHT if is_unread else T.TEXT)
            D.text(self.screen, self.fonts.sm, msg.sender_name[:22],
                   (item_rect.x + 20, item_rect.y + 20), T.TEXT_DIM)
            self._comms_msg_rects.append((msg.uid, item_rect))
            my += 42

        if not visible_msgs:
            D.text(self.screen, self.fonts.sm, "No messages",
                   (list_rect.centerx, list_rect.y + 30), T.TEXT_DIM, "midtop")

        detail_x = px + LIST_W + 20
        detail_w  = PW - LIST_W - 30
        detail_rect = pygame.Rect(detail_x, tab_y + 32, detail_w, PH - 100)
        D.rect_rounded(self.screen, T.FLOOR_BG, detail_rect, 4)
        D.rect_outline(self.screen, T.PANEL_EDGE, detail_rect, 1, 4)

        selected_msg = None
        if self._comms_selected_uid:
            for m in msgs:
                if m.uid == self._comms_selected_uid:
                    selected_msg = m
                    break

        if selected_msg is None and visible_msgs:
            selected_msg = visible_msgs[0]
            self._comms_selected_uid = selected_msg.uid

        if selected_msg:
            dy = detail_rect.y + 12
            dx = detail_rect.x + 12
            dw = detail_w - 24

            D.text(self.screen, self.fonts.md, selected_msg.subject,
                   (dx, dy), T.TEXT_BRIGHT)
            dy += 22
            D.text(self.screen, self.fonts.sm,
                   f"From: {selected_msg.sender_name}",
                   (dx, dy), T.ACCENT)
            dy += 18
            D.divider(self.screen, dx, dy, dx + dw, dy)
            dy += 8

            words = selected_msg.body.split()
            line, lines = [], []
            for w in words:
                test = " ".join(line + [w])
                if self.fonts.sm.size(test)[0] > dw:
                    lines.append(" ".join(line))
                    line = [w]
                else:
                    line.append(w)
            if line:
                lines.append(" ".join(line))

            max_body_lines = 8
            for ln in lines[:max_body_lines]:
                D.text(self.screen, self.fonts.sm, ln, (dx, dy), T.TEXT)
                dy += 16

            if selected_msg.replied is None:
                dy = detail_rect.bottom - 20 - len(selected_msg.response_options) * 36
                D.divider(self.screen, dx, dy - 8, dx + dw, dy - 8)
                self._comms_reply_rects = []
                for idx, opt in enumerate(selected_msg.response_options):
                    btn_rect = pygame.Rect(dx, dy, dw, 28)
                    D.rect_rounded(self.screen, T.BUILD_BTN_BG, btn_rect, 5)
                    D.rect_outline(self.screen, T.ACCENT, btn_rect, 1, 5)
                    D.text(self.screen, self.fonts.sm, opt["label"],
                           btn_rect.center, T.TEXT_BRIGHT, "center")
                    self._comms_reply_rects.append((idx, opt, btn_rect))
                    dy += 34
            else:
                dy = detail_rect.bottom - 40
                D.text(self.screen, self.fonts.sm,
                       f"Replied: {selected_msg.replied}",
                       (dx, dy), T.TEXT_DIM)
        else:
            D.text(self.screen, self.fonts.sm, "Select a message to read",
                   detail_rect.center, T.TEXT_DIM, "center")
            self._comms_reply_rects = []

    def _render_work_panel(self) -> None:
        """Draw the crew work assignment panel."""
        overlay = pygame.Surface((T.SCREEN_W, T.SCREEN_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 160))
        self.screen.blit(overlay, (0, 0))

        PW, PH = 860, 560
        px = (T.SCREEN_W - PW) // 2
        py = (T.SCREEN_H - PH) // 2
        panel_rect = pygame.Rect(px, py, PW, PH)
        D.panel(self.screen, panel_rect, 10)

        D.text(self.screen, self.fonts.xl, "WORK ASSIGNMENTS", (px + 16, py + 14), T.ACCENT)

        dept_btn = pygame.Rect(px + PW - 180, py + 10, 140, 28)
        D.rect_rounded(self.screen, T.BUILD_BTN_BG, dept_btn, 5)
        D.rect_outline(self.screen, T.PANEL_EDGE, dept_btn, 1, 5)
        D.text(self.screen, self.fonts.sm, "Departments", dept_btn.center, T.ACCENT, "center")
        self._work_dept_btn_rect = dept_btn

        close_rect = pygame.Rect(px + PW - 38, py + 10, 28, 28)
        D.rect_rounded(self.screen, (80, 30, 30), close_rect, 4)
        D.text(self.screen, self.fonts.md, "\u2715", close_rect.center, T.DANGER, "center")
        self._work_close_rect = close_rect

        JOB_IDS = [
            ("job.haul",    "Haul"),
            ("job.refine",  "Refine"),
            ("job.craft",   "Craft"),
            ("job.guard_post", "Guard"),
            ("job.patrol",  "Patrol"),
            ("job.build",   "Build"),
            ("job.module_maintenance", "Maint."),
            ("job.resource_management", "Res.Mgmt"),
        ]

        HEADER_Y = py + 54
        NAME_W   = 140
        COL_W    = 68
        ROW_H    = 32
        start_x  = px + 16

        self._work_col_headers = []
        for ci, (jid, jlabel) in enumerate(JOB_IDS):
            hx = start_x + NAME_W + ci * COL_W
            col_rect = pygame.Rect(hx, HEADER_Y, COL_W - 2, 24)
            D.rect_rounded(self.screen, T.BUILD_BTN_BG, col_rect, 3)
            D.text(self.screen, self.fonts.sm, jlabel,
                   col_rect.center, T.ACCENT, "center")
            self._work_col_headers.append((jid, col_rect))

        crew = self.s.get_crew()
        work_assignments = getattr(self.s, 'work_assignments', {})

        ROW_Y = HEADER_Y + 28
        self._work_cell_rects = []

        for ri, npc in enumerate(crew[self._work_scroll:self._work_scroll + 12]):
            ry = ROW_Y + ri * ROW_H
            bg_col = (22, 30, 55) if ri % 2 == 0 else (18, 24, 44)
            pygame.draw.rect(self.screen, bg_col,
                             pygame.Rect(start_x, ry, PW - 32, ROW_H - 2))

            dc = T.CLASS_COLOR.get(npc.class_id, T.TEXT)
            D.dot(self.screen, dc, (start_x + 8, ry + ROW_H // 2), 5)
            D.text(self.screen, self.fonts.sm, npc.name[:16],
                   (start_x + 18, ry + (ROW_H - 12) // 2), T.TEXT)

            npc_jobs = set(work_assignments.get(npc.uid, []))
            for ci, (jid, _) in enumerate(JOB_IDS):
                cx = start_x + NAME_W + ci * COL_W
                cell_rect = pygame.Rect(cx + 4, ry + 4, COL_W - 10, ROW_H - 8)
                enabled = len(npc_jobs) == 0 or jid in npc_jobs
                col = T.OK if enabled else T.BUILD_BTN_BG
                D.rect_rounded(self.screen, col, cell_rect, 3)
                D.text(self.screen, self.fonts.sm,
                       "\u2713" if enabled else "\u2014",
                       cell_rect.center, T.TEXT_BRIGHT if enabled else T.TEXT_DIM, "center")
                self._work_cell_rects.append((npc.uid, jid, cell_rect))

        if len(crew) > 12:
            D.text(self.screen, self.fonts.sm,
                   f"\u2191\u2193 scroll  ({self._work_scroll + 1}-{min(self._work_scroll + 12, len(crew))}/{len(crew)})",
                   (px + PW - 16, py + PH - 22), T.TEXT_DIM, "topright")

    def _render_departments_panel(self) -> None:
        """Draw the departments management panel."""
        overlay = pygame.Surface((T.SCREEN_W, T.SCREEN_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 160))
        self.screen.blit(overlay, (0, 0))

        PW, PH = 700, 520
        px = (T.SCREEN_W - PW) // 2
        py = (T.SCREEN_H - PH) // 2
        panel_rect = pygame.Rect(px, py, PW, PH)
        D.panel(self.screen, panel_rect, 10)

        D.text(self.screen, self.fonts.xl, "DEPARTMENTS", (px + 16, py + 14), T.ACCENT)

        close_rect = pygame.Rect(px + PW - 38, py + 10, 28, 28)
        D.rect_rounded(self.screen, (80, 30, 30), close_rect, 4)
        D.text(self.screen, self.fonts.md, "\u2715", close_rect.center, T.DANGER, "center")
        self._dept_close_rect = close_rect

        back_btn = pygame.Rect(px + PW - 180, py + 10, 130, 28)
        D.rect_rounded(self.screen, T.BUILD_BTN_BG, back_btn, 5)
        D.rect_outline(self.screen, T.PANEL_EDGE, back_btn, 1, 5)
        D.text(self.screen, self.fonts.sm, "\u2190 Work", back_btn.center, T.TEXT, "center")
        self._dept_back_btn = back_btn

        departments = getattr(self.s, 'departments', [])

        ROW_H = 44
        dy = py + 56
        self._dept_row_rects = []
        self._dept_rename_rects = []

        for dept in departments:
            row_rect = pygame.Rect(px + 10, dy, PW - 20, ROW_H - 4)
            bg_col = (35, 50, 90) if self._dept_renaming_uid == dept.uid else T.BUILD_BTN_BG
            D.rect_rounded(self.screen, bg_col, row_rect, 5)
            D.rect_outline(self.screen, T.PANEL_EDGE, row_rect, 1, 5)

            if self._dept_renaming_uid == dept.uid:
                D.text(self.screen, self.fonts.md,
                       f"Name: {self._dept_rename_text}\u2587",
                       (row_rect.x + 12, row_rect.y + 12), T.TEXT_BRIGHT)
                D.text(self.screen, self.fonts.sm, "Enter to confirm  Esc to cancel",
                       (row_rect.x + 12, row_rect.y + 28), T.TEXT_DIM)
            else:
                D.text(self.screen, self.fonts.lg, dept.name,
                       (row_rect.x + 12, row_rect.y + 10), T.TEXT_BRIGHT)
                jobs_preview = ", ".join(j.replace("job.", "") for j in dept.allowed_jobs[:5])
                if len(dept.allowed_jobs) > 5:
                    jobs_preview += f" +{len(dept.allowed_jobs)-5}"
                D.text(self.screen, self.fonts.sm, jobs_preview,
                       (row_rect.x + 12, row_rect.y + 28), T.TEXT_DIM)

                ren_btn = pygame.Rect(row_rect.right - 82, row_rect.y + 10, 70, 24)
                D.rect_rounded(self.screen, T.BUILD_BTN_BG, ren_btn, 4)
                D.rect_outline(self.screen, T.PANEL_EDGE, ren_btn, 1, 4)
                D.text(self.screen, self.fonts.sm, "Rename", ren_btn.center, T.TEXT, "center")
                self._dept_rename_rects.append((dept.uid, ren_btn))

            self._dept_row_rects.append((dept.uid, row_rect))
            dy += ROW_H + 4

        new_btn = pygame.Rect(px + 10, dy + 8, 180, 30)
        D.rect_rounded(self.screen, T.BUILD_BTN_BG, new_btn, 5)
        D.rect_outline(self.screen, T.OK, new_btn, 1, 5)
        D.text(self.screen, self.fonts.sm, "+ New Department",
               new_btn.center, T.OK, "center")
        self._dept_new_btn = new_btn

    def _is_cargo_hold(self, module_uid: str) -> bool:
        """Return True if the module is a cargo hold (has cargo_capacity > 0)."""
        mod = self.s.modules.get(module_uid)
        if mod is None:
            return False
        defn = self.game.registry.modules.get(mod.definition_id)
        return defn is not None and defn.cargo_capacity > 0

    def _render_cargo_panel(self, module_uid: str, rect: pygame.Rect) -> None:
        """Render the cargo hold detail panel in the log area."""
        mod  = self.s.modules.get(module_uid)
        if mod is None:
            return
        defn = self.game.registry.modules.get(mod.definition_id)
        inv_sys = self.game.inventory_system

        x  = rect.x + 12
        y  = rect.y + 8
        rw = rect.width - 24

        cap_total = defn.cargo_capacity if defn else 0
        cap_used  = inv_sys.get_capacity_used(mod) if inv_sys else 0.0
        pct       = cap_used / cap_total if cap_total > 0 else 0.0

        # Header
        header = f"CARGO  —  {mod.display_name}"
        D.text(self.screen, self.fonts.lg, header, (x, y), T.ACCENT_WARM)

        # Settings info
        settings_label = "Allow: ALL"
        if mod.cargo_settings and mod.cargo_settings.allowed_types:
            allowed = mod.cargo_settings.allowed_types
            if allowed == ["__none__"]:
                settings_label = "Allow: NONE"
            else:
                settings_label = "Allow: " + ", ".join(allowed)
        D.text(self.screen, self.fonts.sm, settings_label,
               (x + rw, y + 4), T.TEXT_DIM, "topright")
        y += 22

        # Capacity bar
        cap_c = (T.DANGER if pct >= 0.95 else T.WARN if pct >= 0.75 else T.OK)
        bar_rect = pygame.Rect(x, y, rw, 6)
        D.hbar(self.screen, bar_rect, cap_used, cap_total, cap_c)
        y += 9
        D.text(self.screen, self.fonts.sm,
               f"Capacity:  {cap_used:.0f} / {cap_total}  ({pct:.0%} full)",
               (x, y), cap_c)
        y += 18

        if not mod.inventory:
            D.text(self.screen, self.fonts.sm, "(empty)", (x, y), T.TEXT_DIM)
            return

        # Column headers
        D.text(self.screen, self.fonts.sm, "ITEM", (x, y), T.TEXT_DIM)
        D.text(self.screen, self.fonts.sm, "TYPE", (x + 200, y), T.TEXT_DIM)
        D.text(self.screen, self.fonts.sm, "QTY", (x + 340, y), T.TEXT_DIM)
        D.text(self.screen, self.fonts.sm, "VALUE", (x + 400, y), T.TEXT_DIM)
        D.text(self.screen, self.fonts.sm, "MASS", (x + 480, y), T.TEXT_DIM)
        y += 14

        pygame.draw.line(self.screen, T.PANEL_EDGE, (x, y), (x + rw, y), 1)
        y += 4

        # Item rows — sort by item_type then display_name
        items = sorted(
            mod.inventory.items(),
            key=lambda kv: (
                (self.game.registry.items[kv[0]].item_type
                 if kv[0] in self.game.registry.items else ""),
                (self.game.registry.items[kv[0]].display_name
                 if kv[0] in self.game.registry.items else kv[0]),
            )
        )

        for item_id, qty in items:
            if y > rect.bottom - 16:
                D.text(self.screen, self.fonts.sm, "…", (x, y), T.TEXT_DIM)
                break
            item_defn = self.game.registry.items.get(item_id)
            name   = item_defn.display_name if item_defn else item_id
            itype  = item_defn.item_type    if item_defn else "?"
            weight = item_defn.weight       if item_defn else 1.0
            value  = item_defn.value        if item_defn else 0.0
            legal  = item_defn.legal        if item_defn else True

            # Colour by legality and perishability
            if not legal:
                name_c = T.DANGER
            elif item_defn and item_defn.perishable_ticks > 0:
                name_c = T.ACCENT_WARM
            else:
                name_c = T.TEXT

            type_c = T.TEXT_DIM
            D.text(self.screen, self.fonts.sm, name[:26], (x, y), name_c)
            D.text(self.screen, self.fonts.sm, itype[:14], (x + 200, y), type_c)
            D.text(self.screen, self.fonts.sm, str(qty), (x + 340, y), T.TEXT)
            D.text(self.screen, self.fonts.sm, f"{value * qty:.0f}c",
                   (x + 400, y), T.ACCENT_WARM)
            D.text(self.screen, self.fonts.sm, f"{weight * qty:.1f}",
                   (x + 480, y), T.TEXT_DIM)
            y += 14

    def _render_event_card(self, p: PendingEvent, rect: pygame.Rect) -> None:
        ev = p.definition
        x  = rect.x + 12
        y  = rect.y + 10
        rw = rect.width - 24

        # Header
        D.text(self.screen, self.fonts.lg, f"!  {ev.title}", (x, y), T.WARN)
        y += 22

        # Description (word-wrapped)
        words = ev.description.strip().split()
        line, lines = [], []
        for w in words:
            test = " ".join(line + [w])
            if self.fonts.sm.size(test)[0] > rw - 230:
                lines.append(" ".join(line))
                line = [w]
            else:
                line.append(w)
        if line:
            lines.append(" ".join(line))
        for ln in lines[:3]:
            D.text(self.screen, self.fonts.sm, ln, (x, y), T.TEXT)
            y += 15

        # Rebuild buttons if needed
        if not self._event_btns and ev.choices:
            self._build_event_buttons()

        for btn in self._event_btns:
            hov = self._hovered_btn == id(btn)
            bg  = T.ACCENT if hov else T.PANEL_BG
            pygame.draw.rect(self.screen, bg, btn["rect"], border_radius=4)
            pygame.draw.rect(self.screen, T.ACCENT, btn["rect"], 1, border_radius=4)
            label = btn["label"][:35]
            D.text(self.screen, self.fonts.sm, label, btn["rect"].center,
                   T.BG if hov else T.TEXT_BRIGHT, "center")

    def _render_log_feed(self, rect: pygame.Rect) -> None:
        x = rect.x + 12
        y = rect.y + 8
        D.text(self.screen, self.fonts.sm,
               "STATION LOG    P=pause  1/2/4=speed  ↑↓=scroll",
               (x, y), T.TEXT_DIM)
        y += 17

        for entry in self.s.log[self._log_scroll: self._log_scroll + 9]:
            fc = (T.DANGER if "CRITICAL" in entry else
                  T.WARN   if "Warning"  in entry else
                  T.OK     if any(w in entry for w in ("docked","joined","Trade:","+")) else
                  T.TEXT_DIM)
            D.text(self.screen, self.fonts.sm, entry[:110], (x, y), fc)
            y += 17
