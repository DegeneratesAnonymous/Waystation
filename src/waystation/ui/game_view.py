"""
GameView — pygame top-down station renderer with real-time NPC movement.

Layout:
  +──────────────────────────────────────+────────────────+
  │  TOP BAR  name | time | speed        │                │
  +──────────────────────────────────────│   SIDEBAR      │
  │                                      │  Resources     │
  │   STATION FLOOR PLAN                 │  Crew / Jobs   │
  │   rooms + corridors                  │  Ships         │
  │   crew figures walking to jobs       │  Factions      │
  │                                      │                │
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
    def __init__(self, seed: int, count: int = 120) -> None:
        rng = random.Random(seed)
        self.stars = [
            (rng.randint(0, T.FLOOR_W - 1),
             rng.randint(0, T.FLOOR_H - 1),
             rng.choice([(180, 190, 220), (140, 150, 180), (220, 220, 255)]),
             rng.randint(1, 2))
            for _ in range(count)
        ]

    def draw(self, surf: pygame.Surface, alpha: float) -> None:
        for x, y, color, r in self.stars:
            dimmed = tuple(int(c * (1 - alpha * 0.6)) for c in color)
            pygame.draw.circle(surf, dimmed, (x, y + T.FLOOR_Y), r)


# ── Main GameView ───────────────────────────────────────────────────────────────

class GameView:

    def __init__(self, game: "Game") -> None:
        self.game = game
        self.s    = game.station

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

        # Stars (visual background)
        self._stars = StarField(game.seed)

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

    # ── Main loop ─────────────────────────────────────────────────────────────

    def run(self) -> None:
        while True:
            dt = self.clock.tick(60) / 1000.0

            for ev in pygame.event.get():
                if ev.type == pygame.QUIT:
                    pygame.quit()
                    return
                elif ev.type == pygame.KEYDOWN:
                    self._on_key(ev)
                elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                    self._on_click(ev.pos)
                elif ev.type == pygame.MOUSEMOTION:
                    self._on_hover(ev.pos)

            self._update(dt)
            self._render()
            pygame.display.flip()

    def _update(self, dt: float) -> None:
        # Tick accumulation
        interval = T.TICK_INTERVAL.get(self._speed, 999.0)
        self._tick_acc += dt
        if self._tick_acc >= interval:
            self._tick_acc -= interval
            if not self.game.event_system.get_pending():
                self._do_tick()
            else:
                # Pause auto-advance when event needs attention
                self._speed = 0

        # Animate dots
        for dot in self._dots.values():
            dot.update(dt)

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
                pygame.event.post(pygame.event.Event(pygame.QUIT))
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

    def _on_click(self, pos: tuple[int, int]) -> None:
        # Speed buttons
        for speed, rect in self._speed_btn_rects().items():
            if rect.collidepoint(pos):
                self._speed = speed
                return

        # Build menu toggle button
        btn_rect = self._build_btn_rect()
        if btn_rect.collidepoint(pos):
            self._build_menu_open = not self._build_menu_open
            self._build_selected_id = None
            return

        # Build menu item clicks
        if self._build_menu_open:
            for item in self._build_menu_item_rects():
                if item["rect"].collidepoint(pos):
                    if self._build_selected_id == item["id"]:
                        # Second click = place order
                        if (self.game.building_system and
                                self.game.building_system.can_afford(
                                    item["id"], self.s)):
                            self.game.building_system.place_order(
                                item["id"], self.s)
                            self._build_menu_open = False
                            self._build_selected_id = None
                    else:
                        self._build_selected_id = item["id"]
                    return
            # Click outside menu → close
            menu_rect = self._build_menu_rect()
            if not menu_rect.collidepoint(pos):
                self._build_menu_open = False
                self._build_selected_id = None
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

    def _build_btn_rect(self) -> pygame.Rect:
        """Rect for the 'BUILD' toggle button in the top bar."""
        bx = T.SCREEN_W - T.SIDEBAR_W - 260 - 80
        return pygame.Rect(bx, 7, 70, 28)

    def _build_menu_rect(self) -> pygame.Rect:
        """Rect for the floating build menu panel."""
        return pygame.Rect(T.FLOOR_X + 10,
                           T.FLOOR_Y + 10,
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

    def _render_floor(self) -> None:
        floor_rect = pygame.Rect(T.FLOOR_X, T.FLOOR_Y, T.FLOOR_W, T.FLOOR_H)
        pygame.draw.rect(self.screen, T.FLOOR_BG, floor_rect)

        alpha = time_system.sky_alpha(self.s)
        self._stars.draw(self.screen, alpha)

        self._render_corridors()

        for uid, mod in self.s.modules.items():
            rect = self._mod_rects.get(uid)
            if rect:
                self._render_room(mod, rect, uid == self._selected_mod)

        self._render_npc_dots()
        self._render_incoming_lane()

    def _render_corridors(self) -> None:
        """Draw thin passages between horizontally/vertically adjacent rooms."""
        rects = list(self._mod_rects.values())
        pad   = T.CELL_PAD

        for i, ra in enumerate(rects):
            for rb in rects[i+1:]:
                # Horizontal adjacency
                if abs(ra.right - rb.left) <= pad + 2 or abs(rb.right - ra.left) <= pad + 2:
                    oy1 = max(ra.top,  rb.top)
                    oy2 = min(ra.bottom, rb.bottom)
                    if oy2 > oy1 + 20:
                        cx = (min(ra.right, rb.right) + max(ra.left, rb.left)) // 2
                        ch_w = pad
                        pygame.draw.rect(self.screen, T.CORRIDOR,
                                         (cx - ch_w//2, oy1, ch_w, oy2 - oy1))

                # Vertical adjacency
                if abs(ra.bottom - rb.top) <= pad + 2 or abs(rb.bottom - ra.top) <= pad + 2:
                    ox1 = max(ra.left,  rb.left)
                    ox2 = min(ra.right, rb.right)
                    if ox2 > ox1 + 20:
                        cy = (min(ra.bottom, rb.bottom) + max(ra.top, rb.top)) // 2
                        cv_h = pad
                        pygame.draw.rect(self.screen, T.CORRIDOR,
                                         (ox1, cy - cv_h//2, ox2 - ox1, cv_h))

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

        # Room grid lines (subtle texture)
        for gy in range(rect.y + 10, rect.bottom, 20):
            pygame.draw.line(self.screen, tuple(max(0, c+5) for c in floor),
                             (rect.x + 4, gy), (rect.right - 4, gy))

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

    def _render_npc_dots(self) -> None:
        mx, my = pygame.mouse.get_pos()
        for uid, dot in self._dots.items():
            npc  = self.s.npcs.get(uid)
            pos  = dot.draw_pos()
            # Clip to floor area
            if not (T.FLOOR_X <= pos[0] < T.FLOOR_X + T.FLOOR_W and
                    T.FLOOR_Y <= pos[1] < T.FLOOR_Y + T.FLOOR_H):
                continue

            # Pulsing ring if working (job active)
            if npc and npc.current_job_id and npc.job_timer > 0:
                t = pygame.time.get_ticks() / 1000.0
                pulse = int(4 + 2 * abs(math.sin(t * 2)))
                pygame.draw.circle(self.screen, (*dot.color, 60), pos,
                                   pulse + 4)

            # Main dot
            D.dot(self.screen, dot.color, pos, 6, (0, 0, 0))

            # Hover tooltip: name + job
            if npc and math.hypot(mx - pos[0], my - pos[1]) < 14:
                job_label = self.game.job_system.get_job_label(npc) if self.game.job_system else ""
                self._draw_tooltip(
                    f"{npc.name}  [{npc.class_id.replace('class.','')}]",
                    f"{job_label}  mood:{npc.mood_label()}",
                    (pos[0] + 10, pos[1] - 10)
                )

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
        """Approaching ships shown as glowing dots on the right edge."""
        incoming = self.s.get_incoming_ships()
        if not incoming:
            return

        lx    = T.FLOOR_X + T.FLOOR_W - 10
        ly    = T.FLOOR_Y + 10
        t     = pygame.time.get_ticks() / 1000.0

        D.text(self.screen, self.fonts.sm, "APPROACH",
               (lx - 4, ly), T.TEXT_DIM, "topright")
        ly += 16

        for i, ship in enumerate(incoming[:6]):
            ic = T.INTENT_COLOR.get(ship.intent, T.TEXT_DIM)
            tc = T.DANGER if ship.threat_level >= 6 else T.WARN if ship.threat_level >= 3 else T.TEXT_DIM
            pulse = int(3 + 2 * abs(math.sin(t * 1.5 + i)))
            dot_x = lx - 6
            dot_y = ly + 4
            pygame.draw.circle(self.screen, ic, (dot_x, dot_y), pulse)
            pygame.draw.circle(self.screen, (*ic, 80), (dot_x, dot_y), pulse + 4)
            D.text(self.screen, self.fonts.sm, ship.name[:18],
                   (lx - 14, ly), ic, "topright")
            ly += 14
            D.text(self.screen, self.fonts.sm,
                   f"{ship.role} / {ship.intent}",
                   (lx - 14, ly), T.TEXT_DIM, "topright")
            ly += 12
            if ship.threat_level > 0:
                D.text(self.screen, self.fonts.sm,
                       f"threat: {ship.threat_label()}",
                       (lx - 14, ly), tc, "topright")
                ly += 12
            ly += 6

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

        # Pending event indicator
        pending = [p for p in self._pending if not p.resolved]
        if pending:
            D.text(self.screen, self.fonts.md,
                   f"!  EVENT WAITING — game paused",
                   (450, 14), T.WARN)

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

        # Build menu toggle button
        brect = self._build_btn_rect()
        b_active = self._build_menu_open
        b_bg = T.BUILD_MENU_EDGE if b_active else T.PANEL_EDGE
        pygame.draw.rect(self.screen, b_bg, brect, border_radius=4)
        pygame.draw.rect(self.screen, T.BUILD_MENU_EDGE, brect, 1, border_radius=4)
        D.text(self.screen, self.fonts.sm, "BUILD [B]",
               brect.center, T.TEXT_BRIGHT if b_active else T.TEXT, "center")

        # Tick counter
        D.text(self.screen, self.fonts.sm, f"Tick {self.s.tick:04d}",
               (T.SCREEN_W - T.SIDEBAR_W - 8, 14), T.TEXT_DIM, "topright")

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
                  T.OK     if any(w in entry for w in ("docked","joined","Trade:","+","Blueprint","completed")) else
                  T.TEXT_DIM)
            D.text(self.screen, self.fonts.sm, entry[:110], (x, y), fc)
            y += 17

    # ── Build menu overlay ────────────────────────────────────────────────────

    def _render_build_menu(self) -> None:
        """Render the full-height build menu panel over the floor plan."""
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
               "Click once to select  ·  Click again to place order  ·  ESC to close",
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
            cost_str = "  ".join(
                f"{int(v)} {r}" for r, v in defn.cost.items()
            )
            D.text(self.screen, self.fonts.sm, f"Cost: {cost_str}",
                   (ir.x + 8, ir.y + 24),
                   T.TEXT if can else T.BUILD_UNAVAILABLE)

            # Build time
            D.text(self.screen, self.fonts.sm,
                   f"{defn.build_time_ticks} ticks",
                   (ir.right - 8, ir.y + 24), T.TEXT_DIM, "topright")

            # "Can't afford" hint
            if not can:
                D.text(self.screen, self.fonts.sm, "Insufficient resources",
                       (ir.x + 8, ir.y + 40), T.DANGER)
            elif sel:
                D.text(self.screen, self.fonts.sm,
                       "Click again to confirm order",
                       (ir.x + 8, ir.y + 40), T.BUILD_SELECTED)
