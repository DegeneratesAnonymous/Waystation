"""Low-level drawing helpers."""

from __future__ import annotations
import pygame
from waystation.ui import theme as T


def rect_rounded(surf: pygame.Surface, color, rect: pygame.Rect, radius: int = 8) -> None:
    pygame.draw.rect(surf, color, rect, border_radius=radius)


def rect_outline(surf: pygame.Surface, color, rect: pygame.Rect,
                 width: int = 1, radius: int = 8) -> None:
    pygame.draw.rect(surf, color, rect, width=width, border_radius=radius)


def text(surf: pygame.Surface, font: pygame.font.Font, txt: str,
         pos: tuple, color=T.TEXT, anchor: str = "topleft") -> pygame.Rect:
    surf_txt = font.render(str(txt), True, color)
    r = surf_txt.get_rect(**{anchor: pos})
    surf.blit(surf_txt, r)
    return r


def hbar(surf: pygame.Surface, rect: pygame.Rect,
         value: float, max_value: float,
         fg: tuple, bg: tuple = T.PANEL_EDGE) -> None:
    """Horizontal progress bar."""
    pygame.draw.rect(surf, bg, rect, border_radius=3)
    if max_value > 0:
        filled = rect.copy()
        filled.width = int(rect.width * min(1.0, value / max_value))
        if filled.width > 0:
            pygame.draw.rect(surf, fg, filled, border_radius=3)


def dot(surf: pygame.Surface, color, center: tuple, radius: int = 5,
        outline: tuple | None = None) -> None:
    pygame.draw.circle(surf, color, center, radius)
    if outline:
        pygame.draw.circle(surf, outline, center, radius, 1)


def panel(surf: pygame.Surface, rect: pygame.Rect, radius: int = 6) -> None:
    rect_rounded(surf, T.PANEL_BG, rect, radius)
    rect_outline(surf, T.PANEL_EDGE, rect, 1, radius)


def divider(surf: pygame.Surface, x1: int, y1: int, x2: int, y2: int) -> None:
    pygame.draw.line(surf, T.PANEL_EDGE, (x1, y1), (x2, y2))
