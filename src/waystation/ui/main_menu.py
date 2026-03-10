"""
MainMenuView — pygame main menu for Frontier Waystation.

Provides:
  - New Game  (with station-name input dialog)
  - Load Game (lists save files)
  - Settings  (adjustable options panel)
  - Tutorial  (work-in-progress placeholder)
  - Exit

``run()`` returns a tuple describing what the caller should do next:
  ("new_game",  station_name: str, seed: int | None)
  ("load_game", save_path: Path)
  ("quit",)
"""

from __future__ import annotations

import datetime
import math
import os
import random
from pathlib import Path
from typing import Callable

import pygame

from waystation.ui import theme as T
from waystation.ui import draw as D

# ── Timing / animation constants ───────────────────────────────────────────────
_STAR_COUNT  = 160
_FADE_SPEED  = 3.0   # alpha units per second (0-255)

# ── Menu states ────────────────────────────────────────────────────────────────
_ST_MAIN     = "main"
_ST_NEW_GAME = "new_game"
_ST_LOAD     = "load"
_ST_SETTINGS = "settings"
_ST_TUTORIAL = "tutorial"


# ── Helpers ───────────────────────────────────────────────────────────────────

def _lerp_color(a: tuple, b: tuple, t: float) -> tuple:
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


class _StarField:
    """Parallax star field for background animation."""

    def __init__(self, seed: int, w: int, h: int, count: int = _STAR_COUNT) -> None:
        rng = random.Random(seed)
        self.w, self.h = w, h
        self.stars = [
            {
                "x":     rng.uniform(0, w),
                "y":     rng.uniform(0, h),
                "speed": rng.uniform(4, 20),
                "r":     rng.randint(1, 2),
                "color": rng.choice([
                    (200, 210, 230), (160, 170, 200), (230, 230, 255),
                    (255, 240, 180), (180, 220, 255),
                ]),
            }
            for _ in range(count)
        ]

    def update(self, dt: float) -> None:
        for s in self.stars:
            s["y"] += s["speed"] * dt
            if s["y"] > self.h:
                s["y"] -= self.h

    def draw(self, surf: pygame.Surface) -> None:
        for s in self.stars:
            pygame.draw.circle(surf, s["color"], (int(s["x"]), int(s["y"])), s["r"])


class _Button:
    """Single menu button with hover / active state."""

    H  = 54
    W  = 360

    def __init__(self, label: str, rect: pygame.Rect,
                 enabled: bool = True,
                 sublabel: str = "") -> None:
        self.label    = label
        self.sublabel = sublabel
        self.rect     = rect
        self.enabled  = enabled
        self._hovered = False

    def set_hover(self, pos: tuple[int, int]) -> None:
        self._hovered = self.rect.collidepoint(pos) and self.enabled

    def is_clicked(self, pos: tuple[int, int]) -> bool:
        return self.rect.collidepoint(pos) and self.enabled

    def draw(self, surf: pygame.Surface, fonts: "_Fonts", t: float) -> None:
        pulse = 0.5 + 0.5 * math.sin(t * 2.0)
        if self._hovered and self.enabled:
            border_c = _lerp_color(T.ACCENT, T.ACCENT_WARM, pulse * 0.4)
            bg_c     = tuple(min(255, int(c * 1.4)) for c in T.PANEL_BG)
        elif not self.enabled:
            border_c = T.TEXT_DIM
            bg_c     = T.PANEL_BG
        else:
            border_c = T.PANEL_EDGE
            bg_c     = T.PANEL_BG

        pygame.draw.rect(surf, bg_c,     self.rect, border_radius=8)
        pygame.draw.rect(surf, border_c, self.rect, 2, border_radius=8)

        label_c = T.TEXT_BRIGHT if (self._hovered and self.enabled) else (
                  T.TEXT_DIM   if not self.enabled else T.TEXT)
        D.text(surf, fonts.lg, self.label, self.rect.center, label_c, "center")

        if self.sublabel:
            sub_y = self.rect.centery + 14
            D.text(surf, fonts.sm, self.sublabel,
                   (self.rect.centerx, sub_y), T.TEXT_DIM, "center")


class _Fonts:
    def __init__(self) -> None:
        pygame.font.init()
        mono = pygame.font.match_font("consolas,couriernew,monospace")
        sans = pygame.font.match_font("segoeui,arial,sans")
        self.sm  = pygame.font.Font(mono, T.FONT_SM)
        self.md  = pygame.font.Font(mono, T.FONT_MD)
        self.lg  = pygame.font.Font(sans, T.FONT_LG)
        self.xl  = pygame.font.Font(sans, T.FONT_XL)
        self.hd  = pygame.font.Font(sans, T.FONT_HD)
        self.ttl = pygame.font.Font(sans, 48)


# ── Main class ─────────────────────────────────────────────────────────────────

class MainMenuView:
    """
    Pygame main menu.  Call ``run()``; it blocks until the user makes a
    selection and returns a tuple that describes the chosen action.
    """

    _DEFAULT_STATION = "Waystation Alpha"
    _MAX_NAME_LEN    = 32

    def __init__(self,
                 data_root: Path,
                 mods_root: Path,
                 saves_dir: Path,
                 screen: "pygame.Surface | None" = None) -> None:
        self._data_root = Path(data_root)
        self._mods_root = Path(mods_root)
        self._saves_dir = Path(saves_dir)
        self._saves_dir.mkdir(parents=True, exist_ok=True)

        if not pygame.get_init():
            pygame.init()

        if screen is None:
            self._screen = pygame.display.set_mode((T.SCREEN_W, T.SCREEN_H))
            pygame.display.set_caption("Frontier Waystation")
        else:
            self._screen = screen

        self._clock  = pygame.time.Clock()
        self._fonts  = _Fonts()
        self._stars  = _StarField(42, T.SCREEN_W, T.SCREEN_H)

        self._state  = _ST_MAIN
        self._t      = 0.0     # elapsed time for animations

        # New-game dialog state
        self._ng_name     = self._DEFAULT_STATION
        self._ng_cursor   = len(self._ng_name)
        self._ng_seed_str = ""   # empty → random

        # Settings state (persisted in memory only for this session)
        self._settings: dict[str, object] = {
            "default_name": self._DEFAULT_STATION,
            "auto_save":    True,
        }
        self._settings_cursor = 0   # which setting row is selected

        # Load-game dialog state
        self._load_saves:  list[Path] = []
        self._load_cursor: int        = 0

        # Tutorial page state
        self._tut_page = 0

        # Status / feedback message
        self._status_msg = ""

        self._build_main_buttons()

    # ── Button layout ─────────────────────────────────────────────────────────

    def _build_main_buttons(self) -> None:
        cx = T.SCREEN_W // 2
        bw = _Button.W
        bh = _Button.H
        gap = 14
        # 5 buttons; center them vertically in the lower 2/3 of the screen
        total_h = 5 * bh + 4 * gap
        start_y = (T.SCREEN_H - total_h) // 2 + 80  # slightly below centre

        self._main_btns: list[_Button] = []
        labels = [
            ("New Game",   True,  ""),
            ("Load Game",  True,  ""),
            ("Settings",   True,  ""),
            ("Tutorial",   True,  "Work in Progress"),
            ("Exit",       True,  ""),
        ]
        for i, (label, enabled, sub) in enumerate(labels):
            rect = pygame.Rect(cx - bw // 2,
                               start_y + i * (bh + gap),
                               bw, bh)
            self._main_btns.append(_Button(label, rect, enabled, sub))

    # ── Save file helpers ──────────────────────────────────────────────────────

    def _refresh_saves(self) -> None:
        self._load_saves = sorted(
            self._saves_dir.glob("*.json"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        self._load_cursor = 0

    # ── Main entry point ──────────────────────────────────────────────────────

    def run(self) -> tuple:
        """Block until the user makes a selection; return an action tuple."""
        while True:
            dt = self._clock.tick(60) / 1000.0
            self._t += dt
            self._stars.update(dt)

            for ev in pygame.event.get():
                if ev.type == pygame.QUIT:
                    return ("quit",)
                elif ev.type == pygame.KEYDOWN:
                    result = self._on_key(ev)
                    if result is not None:
                        return result
                elif ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
                    result = self._on_click(ev.pos)
                    if result is not None:
                        return result
                elif ev.type == pygame.MOUSEMOTION:
                    self._on_hover(ev.pos)

            self._render()
            pygame.display.flip()

    # ── Input handlers ─────────────────────────────────────────────────────────

    def _on_hover(self, pos: tuple[int, int]) -> None:
        if self._state == _ST_MAIN:
            for btn in self._main_btns:
                btn.set_hover(pos)

    def _on_click(self, pos: tuple[int, int]) -> tuple | None:
        if self._state == _ST_MAIN:
            return self._click_main(pos)
        if self._state == _ST_NEW_GAME:
            return self._click_new_game(pos)
        if self._state == _ST_LOAD:
            return self._click_load(pos)
        if self._state == _ST_SETTINGS:
            self._click_settings(pos)
        if self._state == _ST_TUTORIAL:
            self._click_tutorial(pos)
        return None

    def _on_key(self, ev: pygame.event.Event) -> tuple | None:
        if self._state == _ST_MAIN:
            if ev.key == pygame.K_ESCAPE:
                return ("quit",)
        elif self._state == _ST_NEW_GAME:
            return self._key_new_game(ev)
        elif self._state == _ST_LOAD:
            return self._key_load(ev)
        elif self._state == _ST_SETTINGS:
            self._key_settings(ev)
        elif self._state == _ST_TUTORIAL:
            if ev.key == pygame.K_ESCAPE:
                self._state = _ST_MAIN
        return None

    # ── Main panel clicks ──────────────────────────────────────────────────────

    def _click_main(self, pos: tuple[int, int]) -> tuple | None:
        for btn in self._main_btns:
            if btn.is_clicked(pos):
                match btn.label:
                    case "New Game":
                        self._ng_name     = self._settings.get(
                            "default_name", self._DEFAULT_STATION)
                        self._ng_cursor   = len(self._ng_name)
                        self._ng_seed_str = ""
                        self._state       = _ST_NEW_GAME
                    case "Load Game":
                        self._refresh_saves()
                        self._state = _ST_LOAD
                    case "Settings":
                        self._state = _ST_SETTINGS
                    case "Tutorial":
                        self._tut_page = 0
                        self._state    = _ST_TUTORIAL
                    case "Exit":
                        return ("quit",)
        return None

    # ── New game dialog ────────────────────────────────────────────────────────

    def _key_new_game(self, ev: pygame.event.Event) -> tuple | None:
        if ev.key == pygame.K_ESCAPE:
            self._state = _ST_MAIN
            return None
        if ev.key == pygame.K_RETURN:
            name = self._ng_name.strip() or self._DEFAULT_STATION
            seed = _parse_seed(self._ng_seed_str)
            return ("new_game", name, seed)
        if ev.key == pygame.K_TAB:
            # Switch focus between name field and seed field; simple toggle
            # here we track focus via _ng_focus flag
            self._ng_focus = not getattr(self, "_ng_focus", True)
            return None
        # Determine which field has focus
        focus_name = getattr(self, "_ng_focus", True)
        if focus_name:
            self._ng_name, self._ng_cursor = _text_input(
                ev, self._ng_name, self._ng_cursor, self._MAX_NAME_LEN)
        else:
            raw, cur = _text_input(ev, self._ng_seed_str, len(self._ng_seed_str), 10)
            # Keep a minus sign only at position 0; allow digits everywhere else
            self._ng_seed_str = ("-" if raw.startswith("-") else "") + "".join(
                c for c in raw if c.isdigit()
            )
        return None

    def _click_new_game(self, pos: tuple[int, int]) -> tuple | None:
        # Confirm button
        if self._ng_confirm_rect().collidepoint(pos):
            name = self._ng_name.strip() or self._DEFAULT_STATION
            seed = _parse_seed(self._ng_seed_str)
            return ("new_game", name, seed)
        # Back button
        if self._ng_back_rect().collidepoint(pos):
            self._state = _ST_MAIN
        # Click on name field
        if self._ng_name_rect().collidepoint(pos):
            self._ng_focus = True
        # Click on seed field
        if self._ng_seed_rect().collidepoint(pos):
            self._ng_focus = False
        return None

    # ── Load dialog ────────────────────────────────────────────────────────────

    def _key_load(self, ev: pygame.event.Event) -> tuple | None:
        if ev.key == pygame.K_ESCAPE:
            self._state = _ST_MAIN
            return None
        if ev.key == pygame.K_UP:
            self._load_cursor = max(0, self._load_cursor - 1)
        elif ev.key == pygame.K_DOWN:
            self._load_cursor = min(len(self._load_saves) - 1,
                                    self._load_cursor + 1)
        elif ev.key == pygame.K_RETURN and self._load_saves:
            return ("load_game", self._load_saves[self._load_cursor])
        elif ev.key == pygame.K_DELETE and self._load_saves:
            # Delete highlighted save
            target = self._load_saves[self._load_cursor]
            try:
                target.unlink()
            except OSError:
                pass
            self._refresh_saves()
        return None

    def _click_load(self, pos: tuple[int, int]) -> tuple | None:
        if self._load_back_rect().collidepoint(pos):
            self._state = _ST_MAIN
            return None
        for i, save_rect in enumerate(self._load_save_rects()):
            if save_rect.collidepoint(pos):
                if i < len(self._load_saves):
                    if i == self._load_cursor:
                        # Double-click logic: just load on second click
                        return ("load_game", self._load_saves[i])
                    self._load_cursor = i
        return None

    # ── Settings ───────────────────────────────────────────────────────────────

    def _key_settings(self, ev: pygame.event.Event) -> None:
        if ev.key == pygame.K_ESCAPE:
            self._state = _ST_MAIN

    def _click_settings(self, pos: tuple[int, int]) -> None:
        if self._settings_back_rect().collidepoint(pos):
            self._state = _ST_MAIN
            return
        # Toggle auto-save
        if self._settings_autosave_rect().collidepoint(pos):
            self._settings["auto_save"] = not self._settings.get("auto_save", True)

    # ── Tutorial ───────────────────────────────────────────────────────────────

    def _click_tutorial(self, pos: tuple[int, int]) -> None:
        bk = self._tut_back_rect()
        nx = self._tut_next_rect()
        if bk.collidepoint(pos):
            if self._tut_page > 0:
                self._tut_page -= 1
            else:
                self._state = _ST_MAIN
        elif nx.collidepoint(pos):
            if self._tut_page < len(_TUTORIAL_PAGES) - 1:
                self._tut_page += 1

    # ── Rendering ─────────────────────────────────────────────────────────────

    def _render(self) -> None:
        self._screen.fill(T.BG)
        self._stars.draw(self._screen)
        self._render_title()

        if self._state == _ST_MAIN:
            self._render_main()
        elif self._state == _ST_NEW_GAME:
            self._render_new_game()
        elif self._state == _ST_LOAD:
            self._render_load()
        elif self._state == _ST_SETTINGS:
            self._render_settings()
        elif self._state == _ST_TUTORIAL:
            self._render_tutorial()

        if self._status_msg:
            D.text(self._screen, self._fonts.sm, self._status_msg,
                   (T.SCREEN_W // 2, T.SCREEN_H - 30), T.WARN, "center")

    def _render_title(self) -> None:
        cx = T.SCREEN_W // 2

        # Glow effect behind title
        pulse = 0.5 + 0.5 * math.sin(self._t * 0.8)
        glow_alpha = int(30 + 20 * pulse)
        glow_surf = pygame.Surface((600, 80), pygame.SRCALPHA)
        glow_surf.fill((0, 180, 255, glow_alpha))
        self._screen.blit(glow_surf, (cx - 300, 40))

        D.text(self._screen, self._fonts.ttl, "FRONTIER WAYSTATION",
               (cx, 55), T.ACCENT, "center")
        D.text(self._screen, self._fonts.md, "Space Station Management",
               (cx, 108), T.TEXT_DIM, "center")

    def _render_main(self) -> None:
        for btn in self._main_btns:
            btn.draw(self._screen, self._fonts, self._t)

        D.text(self._screen, self._fonts.sm,
               "Esc to quit",
               (T.SCREEN_W // 2, T.SCREEN_H - 18), T.TEXT_DIM, "center")

    # ── New-game dialog rendering ──────────────────────────────────────────────

    def _render_new_game(self) -> None:
        self._render_panel(420, 300, "NEW GAME")
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2

        # Station name label + field
        D.text(self._screen, self._fonts.md, "Station Name",
               (cx - 180, py + 60), T.TEXT_DIM)
        focus_name = getattr(self, "_ng_focus", True)
        self._render_text_field(
            self._ng_name_rect(),
            self._ng_name,
            self._ng_cursor,
            active=focus_name,
        )

        # Seed label + field
        D.text(self._screen, self._fonts.md, "Seed (blank = random)",
               (cx - 180, py + 130), T.TEXT_DIM)
        self._render_text_field(
            self._ng_seed_rect(),
            self._ng_seed_str,
            len(self._ng_seed_str),
            active=not focus_name,
        )

        D.text(self._screen, self._fonts.sm, "Tab to switch fields · Enter to confirm · Esc to cancel",
               (cx, py + 200), T.TEXT_DIM, "center")

        # Confirm button
        self._render_dialog_btn(self._ng_confirm_rect(), "Start Game", T.OK)
        # Back button
        self._render_dialog_btn(self._ng_back_rect(), "Back", T.TEXT_DIM)

    def _ng_name_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2
        return pygame.Rect(cx - 180, py + 80, 360, 32)

    def _ng_seed_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2
        return pygame.Rect(cx - 180, py + 150, 200, 32)

    def _ng_confirm_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2
        return pygame.Rect(cx - 100, py + 245, 200, 40)

    def _ng_back_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2
        return pygame.Rect(cx + 110, py + 245, 80, 40)

    # ── Load-game dialog rendering ─────────────────────────────────────────────

    def _render_load(self) -> None:
        self._render_panel(560, 380, "LOAD GAME")
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 380) // 2

        if not self._load_saves:
            D.text(self._screen, self._fonts.md, "No save files found.",
                   (cx, py + 100), T.TEXT_DIM, "center")
            D.text(self._screen, self._fonts.sm, "Start a new game to create a save file.",
                   (cx, py + 120), T.TEXT_DIM, "center")
        else:
            D.text(self._screen, self._fonts.sm,
                   "↑/↓ to select  ·  Enter to load  ·  Del to delete",
                   (cx, py + 50), T.TEXT_DIM, "center")
            for i, rect in enumerate(self._load_save_rects()):
                if i >= len(self._load_saves):
                    break
                sp = self._load_saves[i]
                selected = (i == self._load_cursor)
                bg = T.PANEL_EDGE if selected else T.PANEL_BG
                pygame.draw.rect(self._screen, bg, rect, border_radius=4)
                pygame.draw.rect(self._screen, T.ACCENT if selected else T.PANEL_EDGE,
                                 rect, 1, border_radius=4)
                # Save file name and modification time
                mtime = datetime.datetime.fromtimestamp(sp.stat().st_mtime)
                label = f"{sp.stem}"
                sub   = mtime.strftime("%Y-%m-%d  %H:%M")
                D.text(self._screen, self._fonts.md, label,
                       (rect.x + 10, rect.y + 6),
                       T.TEXT_BRIGHT if selected else T.TEXT)
                D.text(self._screen, self._fonts.sm, sub,
                       (rect.right - 10, rect.y + 6),
                       T.TEXT_DIM, "topright")

        self._render_dialog_btn(self._load_back_rect(), "Back", T.TEXT_DIM)

    def _load_save_rects(self) -> list[pygame.Rect]:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 380) // 2
        rects = []
        for i in range(min(7, len(self._load_saves))):
            rects.append(pygame.Rect(cx - 250, py + 70 + i * 38, 500, 34))
        return rects

    def _load_back_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 380) // 2
        return pygame.Rect(cx - 60, py + 340, 120, 36)

    # ── Settings dialog rendering ──────────────────────────────────────────────

    def _render_settings(self) -> None:
        self._render_panel(480, 300, "SETTINGS")
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2

        y = py + 60

        # Auto-save toggle
        as_rect = self._settings_autosave_rect()
        as_on   = bool(self._settings.get("auto_save", True))
        pygame.draw.rect(self._screen, T.PANEL_EDGE, as_rect, border_radius=4)
        pygame.draw.rect(self._screen, T.ACCENT, as_rect, 1, border_radius=4)
        D.text(self._screen, self._fonts.md, "Auto-save on exit",
               (as_rect.x - 10, as_rect.centery), T.TEXT, "midright")
        D.text(self._screen, self._fonts.md, "ON" if as_on else "OFF",
               as_rect.center, T.OK if as_on else T.DANGER, "center")

        y += 80

        D.text(self._screen, self._fonts.sm,
               "More settings coming soon…",
               (cx, y), T.TEXT_DIM, "center")

        D.text(self._screen, self._fonts.sm,
               "Esc or Back to return",
               (cx, py + 250), T.TEXT_DIM, "center")

        self._render_dialog_btn(self._settings_back_rect(), "Back", T.TEXT_DIM)

    def _settings_autosave_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2
        return pygame.Rect(cx + 60, py + 70, 68, 30)

    def _settings_back_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 300) // 2
        return pygame.Rect(cx - 60, py + 255, 120, 36)

    # ── Tutorial rendering ─────────────────────────────────────────────────────

    def _render_tutorial(self) -> None:
        self._render_panel(700, 440, "TUTORIAL  (Work in Progress)")
        cx   = T.SCREEN_W // 2
        py   = (T.SCREEN_H - 440) // 2
        page = _TUTORIAL_PAGES[self._tut_page]

        D.text(self._screen, self._fonts.lg, page["title"],
               (cx, py + 55), T.ACCENT, "center")

        y = py + 90
        for line in page["body"]:
            D.text(self._screen, self._fonts.sm, line, (cx, y), T.TEXT, "center")
            y += 20

        # Page indicator
        D.text(self._screen, self._fonts.sm,
               f"Page {self._tut_page + 1} / {len(_TUTORIAL_PAGES)}",
               (cx, py + 390), T.TEXT_DIM, "center")

        # Navigation
        bk_rect = self._tut_back_rect()
        nx_rect = self._tut_next_rect()
        bk_lbl  = "Back" if self._tut_page > 0 else "Main Menu"
        nx_lbl  = "Next" if self._tut_page < len(_TUTORIAL_PAGES) - 1 else ""
        self._render_dialog_btn(bk_rect, bk_lbl, T.TEXT_DIM)
        if nx_lbl:
            self._render_dialog_btn(nx_rect, nx_lbl, T.ACCENT)

    def _tut_back_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 440) // 2
        return pygame.Rect(cx - 220, py + 405, 120, 36)

    def _tut_next_rect(self) -> pygame.Rect:
        cx = T.SCREEN_W // 2
        py = (T.SCREEN_H - 440) // 2
        return pygame.Rect(cx + 100, py + 405, 120, 36)

    # ── Shared rendering helpers ───────────────────────────────────────────────

    def _render_panel(self, w: int, h: int, title: str) -> None:
        """Draw a centered modal panel with a title."""
        cx = T.SCREEN_W // 2
        cy = T.SCREEN_H // 2
        rect = pygame.Rect(cx - w // 2, cy - h // 2, w, h)

        # Drop shadow
        shadow = rect.move(4, 4)
        shadow_surf = pygame.Surface((shadow.w, shadow.h), pygame.SRCALPHA)
        shadow_surf.fill((0, 0, 0, 80))
        self._screen.blit(shadow_surf, shadow.topleft)

        # Panel background
        pygame.draw.rect(self._screen, T.PANEL_BG, rect, border_radius=10)
        pygame.draw.rect(self._screen, T.ACCENT,   rect, 1, border_radius=10)

        # Title bar
        title_rect = pygame.Rect(rect.x, rect.y, rect.w, 42)
        pygame.draw.rect(self._screen, T.PANEL_EDGE, title_rect,
                         border_top_left_radius=10, border_top_right_radius=10)
        D.text(self._screen, self._fonts.lg, title,
               (cx, rect.y + 20), T.ACCENT, "center")

    def _render_text_field(self, rect: pygame.Rect, text: str,
                            cursor: int, active: bool) -> None:
        """Draw a text-input field with optional blinking cursor."""
        bg = tuple(min(255, c + 12) for c in T.PANEL_BG) if active else T.PANEL_BG
        border = T.ACCENT if active else T.PANEL_EDGE
        pygame.draw.rect(self._screen, bg,     rect, border_radius=4)
        pygame.draw.rect(self._screen, border, rect, 1, border_radius=4)
        D.text(self._screen, self._fonts.md, text, (rect.x + 6, rect.y + 6), T.TEXT)
        # Blinking cursor
        if active and int(self._t * 2) % 2 == 0:
            pre_w = self._fonts.md.size(text[:cursor])[0]
            cx_   = rect.x + 6 + pre_w
            pygame.draw.line(self._screen, T.TEXT_BRIGHT,
                             (cx_, rect.y + 5), (cx_, rect.bottom - 5), 1)

    def _render_dialog_btn(self, rect: pygame.Rect, label: str,
                            color: tuple) -> None:
        mx, my = pygame.mouse.get_pos()
        hov    = rect.collidepoint(mx, my)
        bg     = tuple(min(255, int(c * 1.4)) for c in T.PANEL_BG) if hov else T.PANEL_BG
        pygame.draw.rect(self._screen, bg,    rect, border_radius=6)
        pygame.draw.rect(self._screen, color, rect, 1, border_radius=6)
        D.text(self._screen, self._fonts.md, label, rect.center,
               T.TEXT_BRIGHT if hov else color, "center")


# ── Text-input helper ──────────────────────────────────────────────────────────

def _text_input(ev: pygame.event.Event,
                text: str, cursor: int,
                max_len: int) -> tuple[str, int]:
    """Handle a KEYDOWN event for a simple text field; return (new_text, new_cursor)."""
    if ev.key == pygame.K_BACKSPACE:
        if cursor > 0:
            text   = text[:cursor - 1] + text[cursor:]
            cursor -= 1
    elif ev.key == pygame.K_DELETE:
        if cursor < len(text):
            text = text[:cursor] + text[cursor + 1:]
    elif ev.key == pygame.K_LEFT:
        cursor = max(0, cursor - 1)
    elif ev.key == pygame.K_RIGHT:
        cursor = min(len(text), cursor + 1)
    elif ev.key == pygame.K_HOME:
        cursor = 0
    elif ev.key == pygame.K_END:
        cursor = len(text)
    elif ev.unicode and ev.unicode.isprintable() and len(text) < max_len:
        text   = text[:cursor] + ev.unicode + text[cursor:]
        cursor += 1
    return text, cursor


def _parse_seed(seed_str: str) -> int | None:
    """Parse an optional integer seed string; return None if blank or invalid."""
    s = seed_str.strip()
    if not s:
        return None
    try:
        return int(s)
    except ValueError:
        return None


# ── Tutorial content ──────────────────────────────────────────────────────────

_TUTORIAL_PAGES: list[dict] = [
    {
        "title": "Welcome to Frontier Waystation",
        "body": [
            "You are the commander of a deep-space waystation.",
            "Ships visit to trade, seek refuge, or cause trouble.",
            "",
            "Your job: keep the station running, the crew happy,",
            "and the credits flowing.",
            "",
            "This tutorial will walk you through the basics.",
        ],
    },
    {
        "title": "The Station Floor",
        "body": [
            "The large area shows your station modules.",
            "Each room has a function: docking bays, crew quarters,",
            "medical bay, power core, and more.",
            "",
            "Click a module to select it and see its details.",
            "",
            "Animated dots represent your crew walking to their jobs.",
        ],
    },
    {
        "title": "Resources",
        "body": [
            "The sidebar shows your current resource levels:",
            "Credits · Food · Power · Oxygen · Parts · Ice",
            "",
            "Resources change every tick based on your modules",
            "and any events that occur.",
            "",
            "Watch the bars — red means critical shortage!",
        ],
    },
    {
        "title": "Speed Controls",
        "body": [
            "Use the speed buttons at the top to control time:",
            "  PAUSE — freeze the simulation",
            "  x1 / x2 / x4 — run at increasing speeds",
            "",
            "Keyboard shortcuts:",
            "  Space / P — toggle pause",
            "  1 / 2 / 4 — set speed",
            "  Ctrl+S    — save game",
            "  Esc / Q   — save and return to this menu",
        ],
    },
    {
        "title": "Events",
        "body": [
            "Events pop up in the log panel at the bottom.",
            "Some events require a decision — the game pauses",
            "and presents you with choices.",
            "",
            "Choose wisely: your decisions affect faction",
            "relationships, resources, and crew morale.",
            "",
            "More tutorial content coming soon — good luck!",
        ],
    },
]
