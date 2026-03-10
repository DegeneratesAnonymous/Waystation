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
  │   EVENT CARD  or  LOG FEED                            │
  +───────────────────────────────────────────────────────+

Controls:
  P / Space  — pause / unpause
  1 / 2 / 4  — set speed multiplier
  Click      — select module or event choice
  Q / Esc    — quit
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

if TYPE_CHECKING:
    from waystation.game import Game
    from waystation.models.instances import (
        ModuleInstance, NPCInstance, BuildOrderInstance
    )
    from waystation.models.templates import BuildableDefinition


# ── Constants ──────────────────────────────────────────────────────────────────
AUTOSAVE_FILENAME = "autosave.json"

# Keywords that give a log entry a green OK colour
_LOG_OK_KEYWORDS = ("docked", "joined", "Trade:", "+", "Blueprint", "completed")

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

        # ── Build menu state ────────────────────────────────────────────────
        self._build_menu_open: bool = False
        self._build_selected_id: str | None = None   # highlighted buildable id
        self._build_menu_scroll: int = 0

        # Stars and space background
        self._stars  = StarField(game.seed)
        self._nebula = NebulaField(game.seed)

        # Return signal: "menu" when user exits back to the main menu
        self._return_signal: str | None = None

        # Save feedback message (shown briefly after saving)
        self._save_msg: str = ""
        self._save_msg_timer: float = 0.0

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
                    self._on_click(ev.pos)
                elif ev.type == pygame.MOUSEMOTION:
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
        if ev.key in (pygame.K_p, pygame.K_SPACE):
            self._speed = 0 if self._speed != 0 else 1
        elif ev.key == pygame.K_1:
            self._speed = 1
        elif ev.key == pygame.K_2:
            self._speed = 2
        elif ev.key == pygame.K_4:
            self._speed = 4
        elif ev.key in (pygame.K_q, pygame.K_ESCAPE):
            if self._build_menu_open:
                self._build_menu_open = False
            else:
                self._do_save_and_menu()
        elif ev.key == pygame.K_s and (pygame.key.get_mods() & pygame.KMOD_CTRL):
            self._do_save()
        elif ev.key == pygame.K_b:
            self._build_menu_open = not self._build_menu_open
            self._build_selected_id = None
        elif ev.key == pygame.K_UP:
            if self._build_menu_open:
                self._build_menu_scroll = max(0, self._build_menu_scroll - 1)
            else:
                self._log_scroll = max(0, self._log_scroll - 1)
        elif ev.key == pygame.K_DOWN:
            if self._build_menu_open:
                self._build_menu_scroll += 1
            else:
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
        # Save button
        if self._save_btn_rect().collidepoint(pos):
            self._do_save()
            return

        # Menu button
        if self._menu_btn_rect().collidepoint(pos):
            self._do_save_and_menu()
            return

        # Build menu toggle button
        if self._build_btn_rect().collidepoint(pos):
            self._build_menu_open = not self._build_menu_open
            self._build_selected_id = None
            return

        # Build menu item clicks (must check before speed buttons to consume clicks inside panel)
        if self._build_menu_open:
            for item in self._build_menu_item_rects():
                if item["rect"].collidepoint(pos):
                    if self._build_selected_id == item["id"]:
                        # Second click = place order
                        if (self.game.building_system and
                                self.game.building_system.can_afford(
                                    item["id"], self.s)):
                            self.game.building_system.place_order(item["id"], self.s)
                            self._build_menu_open = False
                            self._build_selected_id = None
                    else:
                        self._build_selected_id = item["id"]
                    return
            # Click outside menu → close
            if not self._build_menu_rect().collidepoint(pos):
                self._build_menu_open = False
                self._build_selected_id = None
            return

        # Speed buttons
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

    def _on_hover(self, pos: tuple[int, int]) -> None:
        self._hovered_btn = None
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
        """Rect for the BUILD toggle button — sits to the left of SAVE/MENU."""
        return pygame.Rect(T.SCREEN_W - T.SIDEBAR_W - 496, 7, 76, 28)

    def _build_menu_rect(self) -> pygame.Rect:
        """Rect for the floating build menu panel."""
        return pygame.Rect(T.FLOOR_X + 10, T.FLOOR_Y + 10,
                           460, T.FLOOR_H - 20)

    def _build_menu_item_rects(self) -> list[dict]:
        """Return list of dicts {id, rect, defn} for visible build menu rows."""
        if not self.game.building_system:
            return []
        items = []
        buildables = self.game.building_system.available_buildables(self.s)
        rect = self._build_menu_rect()
        iy = rect.y + 56   # below header
        ih = 60
        gap = 4
        visible_start = self._build_menu_scroll
        for i, defn in enumerate(buildables):
            if i < visible_start:
                continue
            r = pygame.Rect(rect.x + 8, iy, rect.width - 16, ih)
            if iy + ih > rect.bottom - 8:
                break
            items.append({"id": defn.id, "rect": r, "defn": defn})
            iy += ih + gap
        return items

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
        self._render_floor()
        self._render_top_bar()
        self._render_sidebar()
        self._render_log_panel()
        if self._build_menu_open:
            self._render_build_menu()

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

        # Resource delta summary (bottom of tile)
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

        # Build menu toggle button
        brect = self._build_btn_rect()
        b_active = self._build_menu_open
        b_hov = brect.collidepoint(mx, my)
        b_bg = T.BUILD_MENU_EDGE if b_active else (T.PANEL_EDGE if not b_hov else (30, 60, 120))
        pygame.draw.rect(self.screen, b_bg, brect, border_radius=4)
        pygame.draw.rect(self.screen, T.BUILD_MENU_EDGE, brect, 1, border_radius=4)
        D.text(self.screen, self.fonts.sm, "BUILD [B]",
               brect.center, T.TEXT_BRIGHT if b_active else T.TEXT, "center")

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
        y += 20

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

        # ── Active build orders ──
        active_orders = self.s.get_active_build_orders()
        if active_orders and y + 20 < T.SCREEN_H - 10:
            y += 4
            D.divider(self.screen, x, y, x+rw, y)
            y += 8
            D.text(self.screen, self.fonts.lg, "CONSTRUCTION", (x, y), T.BUILD_MENU_EDGE)
            y += 20
            for order in active_orders[:4]:
                if y + 28 > T.SCREEN_H - 10:
                    break
                defn = self.game.registry.buildables.get(order.buildable_id)
                name = defn.display_name[:16] if defn else order.buildable_id[:16]
                D.text(self.screen, self.fonts.sm, name, (x, y), T.TEXT)
                D.text(self.screen, self.fonts.sm,
                       order.status.upper(), (x+rw, y),
                       T.WARN if order.status == "hauling" else
                       T.OK if order.status == "constructing" else T.TEXT_DIM,
                       "topright")
                y += 14
                bar_rect = pygame.Rect(x, y, rw, 5)
                frac = (order.delivery_fraction() if order.status == "hauling"
                        else order.progress)
                D.hbar(self.screen, bar_rect, frac, 1.0, T.BUILD_PROGRESS_FG,
                       T.BUILD_PROGRESS_BG)
                y += 10

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
        else:
            self._render_log_feed(rect)

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
               "STATION LOG    P=pause  1/2/4=speed  B=build menu  ↑↓=scroll",
               (x, y), T.TEXT_DIM)
        y += 17

        for entry in self.s.log[self._log_scroll: self._log_scroll + 9]:
            fc = (T.DANGER if "CRITICAL" in entry else
                  T.WARN   if "Warning"  in entry else
                  T.OK     if any(w in entry for w in _LOG_OK_KEYWORDS) else
                  T.TEXT_DIM)
            D.text(self.screen, self.fonts.sm, entry[:110], (x, y), fc)
            y += 17

    # ── Build menu overlay ────────────────────────────────────────────────────

    def _render_build_menu(self) -> None:
        """Render the floating build menu panel over the floor plan."""
        rect = self._build_menu_rect()

        # Translucent background
        overlay = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
        overlay.fill((*T.BUILD_MENU_BG, 230))
        self.screen.blit(overlay, rect.topleft)
        pygame.draw.rect(self.screen, T.BUILD_MENU_EDGE, rect, 2, border_radius=6)

        x  = rect.x + 12
        rw = rect.width - 24
        y  = rect.y + 10

        # Header
        D.text(self.screen, self.fonts.hd, "BUILD MENU", (x, y), T.BUILD_SELECTED)
        y += 34
        D.text(self.screen, self.fonts.sm,
               "Click once to select  ·  Click again to place order  ·  ESC/B to close",
               (x, y), T.TEXT_DIM)
        y += 16
        pygame.draw.line(self.screen, T.BUILD_MENU_EDGE,
                         (rect.x, y), (rect.right, y))
        y += 8

        if not self.game.building_system:
            return

        for item in self._build_menu_item_rects():
            defn   = item["defn"]
            ir     = item["rect"]
            can    = self.game.building_system.can_afford(defn.id, self.s)
            sel    = self._build_selected_id == defn.id

            # Row background
            row_bg = (T.BUILD_SELECTED[0]//4,
                      T.BUILD_SELECTED[1]//4,
                      T.BUILD_SELECTED[2]//4) if sel else T.BUILD_MENU_BG
            pygame.draw.rect(self.screen, row_bg, ir, border_radius=4)
            border_c = T.BUILD_SELECTED if sel else (
                T.BUILD_AFFORDABLE if can else T.BUILD_MENU_EDGE)
            pygame.draw.rect(self.screen, border_c, ir, 1, border_radius=4)

            # Name & category badge
            name_c = T.BUILD_SELECTED if sel else (
                T.BUILD_AFFORDABLE if can else T.BUILD_UNAVAILABLE)
            D.text(self.screen, self.fonts.md, defn.display_name,
                   (ir.x + 8, ir.y + 6), name_c)
            D.text(self.screen, self.fonts.sm,
                   f"[{defn.category.upper()}]",
                   (ir.right - 8, ir.y + 6), T.TEXT_DIM, "topright")

            # Cost line
            cost_str = "  ".join(f"{int(v)} {r}" for r, v in defn.cost.items())
            D.text(self.screen, self.fonts.sm, f"Cost: {cost_str}",
                   (ir.x + 8, ir.y + 24),
                   T.TEXT if can else T.BUILD_UNAVAILABLE)

            # Build time
            D.text(self.screen, self.fonts.sm,
                   f"{defn.build_time_ticks} ticks",
                   (ir.right - 8, ir.y + 24), T.TEXT_DIM, "topright")

            # Affordability / confirm hint
            if not can:
                D.text(self.screen, self.fonts.sm, "Insufficient resources",
                       (ir.x + 8, ir.y + 40), T.DANGER)
            elif sel:
                D.text(self.screen, self.fonts.sm,
                       "Click again to confirm order",
                       (ir.x + 8, ir.y + 40), T.BUILD_SELECTED)
