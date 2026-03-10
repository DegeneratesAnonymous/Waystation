"""
Tile map model — the spatial data structure for the station floor plan.

A TileMap is a grid of TileCells that the player populates in build mode.
Contiguous floor-tile regions can be designated as named RoomInstances with
a RoomTypeDefinition.  When a designation is made the backing ModuleInstance
for game simulation may also be created or updated.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import uuid


def _new_uid() -> str:
    return str(uuid.uuid4())[:8]


# Cardinal direction helpers used by TileMap
_OPPOSITE:  dict[str, str]            = {"N": "S", "S": "N", "E": "W", "W": "E"}
_DIR_DELTA: dict[str, tuple[int, int]] = {"N": (0, -1), "S": (0, 1), "E": (1, 0), "W": (-1, 0)}

# ── Wall integrity constants ──────────────────────────────────────────────────

#: Full health value for every newly placed wall tile or wall segment.
WALL_MAX_HP: int = 100

# Per-tick atmosphere / temperature leak rates from a single damaged wall unit.
# A wall at >66 % HP leaks nothing.  33–66 % → small leak.  <33 % → large leak.
_LEAK_SMALL_ATM:   float = 0.001    # atmosphere fraction lost per tick
_LEAK_LARGE_ATM:   float = 0.005    # atmosphere fraction lost per tick
_LEAK_SMALL_TEMP:  float = 0.3      # °C drift toward space per tick
_LEAK_LARGE_TEMP:  float = 1.5      # °C drift toward space per tick
_SPACE_TEMPERATURE: float = -40.0   # ambient space temperature (°C) used as drain target


def _wall_leak_rates(hp: int) -> tuple[float, float]:
    """Return (atm_leak, temp_drift) per tick for a wall at *hp* hit-points."""
    frac = hp / WALL_MAX_HP if WALL_MAX_HP > 0 else 1.0
    if frac > 0.66:
        return 0.0, 0.0
    if frac > 0.33:
        return _LEAK_SMALL_ATM, _LEAK_SMALL_TEMP
    return _LEAK_LARGE_ATM, _LEAK_LARGE_TEMP


# ---------------------------------------------------------------------------
# TileCell — a single cell on the tile grid
# ---------------------------------------------------------------------------

@dataclass
class TileCell:
    """
    One tile on the station tile map.

    tile_type values:
        "empty"  — open space (no floor, no wall)
        "floor"  — pressurised floor tile
        "wall"   — solid wall tile (placed manually; edge-walls are implicit)

    walls dict: "N" | "S" | "E" | "W" -> bool
        True means there is a wall segment on that edge of this floor tile.
        Walls are only meaningful on floor tiles.

    doors dict: "N" | "S" | "E" | "W" -> bool
        True means there is a door opening on that edge.  A door is a passable
        gap in a wall segment — only meaningful when the corresponding wall is
        also True.

    wall_hp — hit-points for a solid wall tile (tile_type == "wall").
        Starts at WALL_MAX_HP.  When reduced to 0 the tile is destroyed.
        >66 % HP: no atmosphere/temperature leak.
        33–66 % HP: small leak.  <33 % HP: large leak.

    wall_segment_hp — per-edge HP for wall segments on a floor tile.
        Indexed by "N"/"S"/"E"/"W".  Only meaningful when walls[side] is True.
        Same HP thresholds and leak rules as wall_hp.

    room_uid — uid of the RoomInstance that has claimed this tile, or None.
    """
    col: int
    row: int
    tile_type: str = "empty"           # "empty" | "floor" | "wall"
    walls: dict[str, bool] = field(default_factory=lambda: {
        "N": False, "S": False, "E": False, "W": False,
    })
    doors: dict[str, bool] = field(default_factory=lambda: {
        "N": False, "S": False, "E": False, "W": False,
    })
    wall_hp: int = WALL_MAX_HP
    wall_segment_hp: dict[str, int] = field(default_factory=lambda: {
        "N": WALL_MAX_HP, "S": WALL_MAX_HP, "E": WALL_MAX_HP, "W": WALL_MAX_HP,
    })
    room_uid: str | None = None

    def to_dict(self) -> dict:
        return {
            "col":             self.col,
            "row":             self.row,
            "tile_type":       self.tile_type,
            "walls":           dict(self.walls),
            "doors":           dict(self.doors),
            "wall_hp":         self.wall_hp,
            "wall_segment_hp": dict(self.wall_segment_hp),
            "room_uid":        self.room_uid,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "TileCell":
        stored_walls = d.get("walls", {})
        walls = {k: bool(stored_walls.get(k, False)) for k in ("N", "S", "E", "W")}
        stored_doors = d.get("doors", {})
        doors = {k: bool(stored_doors.get(k, False)) for k in ("N", "S", "E", "W")}
        stored_seg_hp = d.get("wall_segment_hp", {})
        wall_segment_hp = {
            k: int(stored_seg_hp.get(k, WALL_MAX_HP)) for k in ("N", "S", "E", "W")
        }
        return cls(
            col=int(d["col"]),
            row=int(d["row"]),
            tile_type=d.get("tile_type", "empty"),
            walls=walls,
            doors=doors,
            wall_hp=int(d.get("wall_hp", WALL_MAX_HP)),
            wall_segment_hp=wall_segment_hp,
            room_uid=d.get("room_uid"),
        )


# ---------------------------------------------------------------------------
# RoomInstance — a designated tile area with an assigned type
# ---------------------------------------------------------------------------

@dataclass
class RoomInstance:
    """
    A player-defined room carved out of the tile map.

    uid            — runtime unique identifier.
    name           — display label (auto-generated or player-named).
    room_type_id   — the room.* definition id from the registry, or None if unassigned.
    tile_positions — ordered list of (col, row) tuples that make up this room.
    module_uid     — uid of the backing ModuleInstance in StationState.modules,
                     or None if the room type has no module backing.

    Atmosphere tracking (updated each tick by the room simulation):
        atmosphere  — 0.0 (vacuum) to 1.0 (fully breathable); default 1.0 for
                      newly pressurised rooms.
        temperature — degrees Celsius; default 20.0 °C (comfortable).
        beauty      — 0.0 (bare / ugly) to 100.0 (beautifully decorated);
                      affects crew mood and visitor impressions.
    """
    uid: str
    name: str
    room_type_id: str | None = None
    tile_positions: list[tuple[int, int]] = field(default_factory=list)
    module_uid: str | None = None

    # Room environment
    atmosphere:  float = 1.0    # 0.0 = vacuum → 1.0 = fully breathable
    temperature: float = 20.0   # °C
    beauty:      float = 0.0    # 0–100

    @classmethod
    def create(cls, name: str, tile_positions: list[tuple[int, int]]) -> "RoomInstance":
        return cls(
            uid=_new_uid(),
            name=name,
            tile_positions=list(tile_positions),
        )

    def tile_count(self) -> int:
        return len(self.tile_positions)

    def atmosphere_label(self) -> str:
        if self.atmosphere >= 0.95:
            return "Breathable"
        if self.atmosphere >= 0.5:
            return "Thin"
        if self.atmosphere > 0.0:
            return "Hazardous"
        return "Vacuum"

    def temperature_label(self) -> str:
        if self.temperature < -10:
            return "Freezing"
        if self.temperature < 10:
            return "Cold"
        if self.temperature <= 30:
            return "Comfortable"
        if self.temperature <= 45:
            return "Hot"
        return "Dangerous"

    def beauty_label(self) -> str:
        if self.beauty >= 80:
            return "Luxurious"
        if self.beauty >= 50:
            return "Pleasant"
        if self.beauty >= 20:
            return "Adequate"
        return "Bare"

    def to_dict(self) -> dict:
        return {
            "uid":            self.uid,
            "name":           self.name,
            "room_type_id":   self.room_type_id,
            "tile_positions": [list(pos) for pos in self.tile_positions],
            "module_uid":     self.module_uid,
            "atmosphere":     self.atmosphere,
            "temperature":    self.temperature,
            "beauty":         self.beauty,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "RoomInstance":
        return cls(
            uid=d["uid"],
            name=d["name"],
            room_type_id=d.get("room_type_id"),
            tile_positions=[tuple(pos) for pos in d.get("tile_positions", [])],
            module_uid=d.get("module_uid"),
            atmosphere=float(d.get("atmosphere", 1.0)),
            temperature=float(d.get("temperature", 20.0)),
            beauty=float(d.get("beauty", 0.0)),
        )


# ---------------------------------------------------------------------------
# TileMap — the full station grid
# ---------------------------------------------------------------------------

@dataclass
class TileMap:
    """
    The station's spatial layout stored as a grid of TileCells.

    cols / rows — grid dimensions (number of tiles across / down).
    cells — dict keyed by (col, row) containing only *placed* tiles.
              Missing keys are implicitly "empty" with no walls.
    rooms — dict keyed by room uid containing RoomInstance objects.
    """
    cols: int = 40
    rows: int = 22
    cells: dict[tuple[int, int], TileCell] = field(default_factory=dict)
    rooms: dict[str, RoomInstance] = field(default_factory=dict)

    # ── Tile access ───────────────────────────────────────────────────────

    def get_cell(self, col: int, row: int) -> TileCell:
        """Return the cell at (col, row), creating an empty one if absent."""
        key = (col, row)
        if key not in self.cells:
            self.cells[key] = TileCell(col=col, row=row, tile_type="empty")
        return self.cells[key]

    def set_floor(self, col: int, row: int) -> None:
        """Place a floor tile and automatically manage shared walls."""
        cell = self.get_cell(col, row)
        if cell.tile_type == "floor":
            return
        cell.tile_type = "floor"
        cell.walls = {"N": True, "S": True, "E": True, "W": True}
        self._sync_walls(col, row)

    def set_wall(self, col: int, row: int) -> None:
        """Place a solid wall tile (replaces floor if present)."""
        # Remove this tile from any room that owned it
        self._detach_from_room(col, row)
        cell = self.get_cell(col, row)
        cell.tile_type = "wall"
        cell.walls = {"N": False, "S": False, "E": False, "W": False}
        cell.wall_hp = WALL_MAX_HP      # freshly placed — full health
        cell.room_uid = None
        # Update neighbours that may have shared walls
        for dc, dr, side, opp in (
            (0, -1, "N", "S"), (0, 1, "S", "N"),
            (1,  0, "E", "W"), (-1, 0, "W", "E"),
        ):
            nb = self.cells.get((col + dc, row + dr))
            if nb and nb.tile_type == "floor":
                nb.walls[opp] = True

    def erase(self, col: int, row: int) -> None:
        """Remove a tile entirely, restoring neighbour walls."""
        self._detach_from_room(col, row)
        key = (col, row)
        was_floor = self.cells.get(key) and self.cells[key].tile_type == "floor"
        self.cells.pop(key, None)
        if was_floor:
            # Restore outer walls on neighbours that bordered this tile
            for dc, dr, _side, opp in (
                (0, -1, "N", "S"), (0, 1, "S", "N"),
                (1,  0, "E", "W"), (-1, 0, "W", "E"),
            ):
                nb = self.cells.get((col + dc, row + dr))
                if nb and nb.tile_type == "floor":
                    nb.walls[opp] = True

    def add_wall_segment(self, col: int, row: int, side: str) -> None:
        """Add a wall segment to the given side of a floor tile (full health)."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "floor":
            cell.walls[side] = True
            cell.wall_segment_hp[side] = WALL_MAX_HP  # freshly placed — full health

    def remove_wall_segment(self, col: int, row: int, side: str) -> None:
        """Remove a wall segment from the given side of a floor tile.
        Also removes any door on that side, since a door requires a wall to exist in."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "floor":
            cell.walls[side] = False
            cell.doors[side] = False

    # ── Door management ──────────────────────────────────────────────────

    def toggle_door(self, col: int, row: int, side: str) -> None:
        """
        Toggle a door on the given wall edge of a floor tile.

        If the wall segment does not yet exist, it is auto-created first.
        The door state is mirrored on the neighbour tile's opposing side so
        both sides of the wall are consistent.
        """
        cell = self.cells.get((col, row))
        if cell is None or cell.tile_type != "floor":
            return
        # Ensure wall exists
        if not cell.walls.get(side, False):
            cell.walls[side] = True
        # Toggle door
        current = cell.doors.get(side, False)
        cell.doors[side] = not current
        # Mirror on neighbour
        dc, dr = _DIR_DELTA[side]
        nb = self.cells.get((col + dc, row + dr))
        if nb and nb.tile_type == "floor":
            opp = _OPPOSITE[side]
            if not nb.walls.get(opp, False):
                nb.walls[opp] = True
            nb.doors[opp] = not current

    def remove_door(self, col: int, row: int, side: str) -> None:
        """Remove a door from a wall edge (wall remains)."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "floor":
            cell.doors[side] = False
            dc, dr = _DIR_DELTA[side]
            nb = self.cells.get((col + dc, row + dr))
            if nb and nb.tile_type == "floor":
                nb.doors[_OPPOSITE[side]] = False

    # ── Wall damage & repair ─────────────────────────────────────────────

    def damage_wall(self, col: int, row: int, amount: int) -> bool:
        """
        Apply *amount* damage to the solid wall tile at (col, row).

        Damage is strictly local — only this single tile's HP is reduced.
        If HP reaches 0 the tile is erased from the map.

        Returns True if the wall was destroyed, False otherwise.
        """
        cell = self.cells.get((col, row))
        if cell is None or cell.tile_type != "wall":
            return False
        cell.wall_hp = max(0, cell.wall_hp - amount)
        if cell.wall_hp <= 0:
            self.erase(col, row)
            return True
        return False

    def damage_wall_segment(self, col: int, row: int, side: str, amount: int) -> bool:
        """
        Apply *amount* damage to the wall segment on *side* of the floor tile at
        (col, row).

        Damage is strictly local — only this edge's HP is reduced.
        If HP reaches 0 the segment is removed and the mirrored segment on the
        neighbouring floor tile is cleared too (the physical wall is gone).

        Returns True if the segment was destroyed, False otherwise.
        """
        cell = self.cells.get((col, row))
        if cell is None or cell.tile_type != "floor":
            return False
        if not cell.walls.get(side, False):
            return False
        cell.wall_segment_hp[side] = max(0, cell.wall_segment_hp.get(side, WALL_MAX_HP) - amount)
        if cell.wall_segment_hp[side] <= 0:
            # Remove segment and its door from this tile
            cell.walls[side] = False
            cell.doors[side] = False
            cell.wall_segment_hp[side] = WALL_MAX_HP  # ready for rebuild
            # Clear the mirrored segment on the neighbour
            dc, dr = _DIR_DELTA[side]
            nb = self.cells.get((col + dc, row + dr))
            if nb and nb.tile_type == "floor":
                opp = _OPPOSITE[side]
                nb.walls[opp] = False
                nb.doors[opp] = False
                nb.wall_segment_hp[opp] = WALL_MAX_HP
            return True
        return False

    def repair_wall(self, col: int, row: int, amount: int) -> None:
        """Restore *amount* HP to a solid wall tile (capped at WALL_MAX_HP)."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "wall":
            cell.wall_hp = min(WALL_MAX_HP, cell.wall_hp + amount)

    def repair_wall_segment(self, col: int, row: int, side: str, amount: int) -> None:
        """Restore *amount* HP to a wall segment edge (capped at WALL_MAX_HP)."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "floor" and cell.walls.get(side, False):
            cell.wall_segment_hp[side] = min(
                WALL_MAX_HP, cell.wall_segment_hp.get(side, 0) + amount
            )

    def tick_room_environments(self) -> None:
        """
        Per-tick environment update: atmosphere and temperature leak from damaged
        walls into each RoomInstance.

        For every room, the boundary walls (wall segments on its floor tiles, plus
        adjacent solid wall tiles) are checked.  Each damaged wall contributes a
        leak according to its HP fraction:

            >66 % HP  — no leak
            33–66 % HP — small leak  (_LEAK_SMALL_ATM atmosphere / _LEAK_SMALL_TEMP °C)
            <33 % HP  — large leak   (_LEAK_LARGE_ATM atmosphere / _LEAK_LARGE_TEMP °C)

        Atmosphere drains toward 0.0; temperature drifts toward _SPACE_TEMPERATURE.
        """
        for room in self.rooms.values():
            total_atm_leak  = 0.0
            total_temp_leak = 0.0
            seen_wall_tiles: set[tuple[int, int]] = set()   # avoid counting a shared wall tile twice

            for pos in room.tile_positions:
                cell = self.cells.get(pos)
                if cell is None or cell.tile_type != "floor":
                    continue
                # Wall segments on this floor tile
                for side in ("N", "S", "E", "W"):
                    if not cell.walls.get(side, False):
                        continue
                    hp = cell.wall_segment_hp.get(side, WALL_MAX_HP)
                    da, dt = _wall_leak_rates(hp)
                    total_atm_leak  += da
                    total_temp_leak += dt
                # Adjacent solid wall tiles — each counted at most once per room
                col, row = pos
                for dc, dr in ((0, -1), (0, 1), (1, 0), (-1, 0)):
                    npos = (col + dc, row + dr)
                    if npos in seen_wall_tiles:
                        continue
                    nb = self.cells.get(npos)
                    if nb and nb.tile_type == "wall":
                        seen_wall_tiles.add(npos)
                        da, dt = _wall_leak_rates(nb.wall_hp)
                        total_atm_leak  += da
                        total_temp_leak += dt

            if total_atm_leak > 0.0:
                room.atmosphere = max(0.0, room.atmosphere - total_atm_leak)
            if total_temp_leak > 0.0 and room.temperature > _SPACE_TEMPERATURE:
                room.temperature = max(
                    _SPACE_TEMPERATURE, room.temperature - total_temp_leak
                )

    # ── Room management ──────────────────────────────────────────────────

    def create_room(self, name: str, positions: list[tuple[int, int]]) -> RoomInstance:
        """Create a new room occupying the given floor tiles."""
        room = RoomInstance.create(name, positions)
        self.rooms[room.uid] = room
        for pos in positions:
            cell = self.cells.get(pos)
            if cell:
                cell.room_uid = room.uid
        return room

    def assign_room_type(self, room_uid: str, room_type_id: str,
                         module_uid: str | None = None) -> None:
        """Assign a room type designation (and optional module backing) to a room."""
        room = self.rooms.get(room_uid)
        if room is None:
            raise KeyError(f"Room {room_uid!r} not found in tile map.")
        room.room_type_id = room_type_id
        if module_uid is not None:
            room.module_uid = module_uid

    def delete_room(self, room_uid: str) -> None:
        """Remove a room designation (tiles remain as unassigned floor)."""
        room = self.rooms.pop(room_uid, None)
        if room is None:
            return
        for pos in room.tile_positions:
            cell = self.cells.get(pos)
            if cell:
                cell.room_uid = None

    def get_connected_floor(self, col: int, row: int) -> list[tuple[int, int]]:
        """
        Flood-fill from (col, row) across floor tiles, respecting walls and doors.

        A wall blocks passage unless that wall edge has a door (a door is a
        passable opening in an otherwise solid wall).
        """
        start = (col, row)
        if start not in self.cells or self.cells[start].tile_type != "floor":
            return []
        visited: set[tuple[int, int]] = set()
        queue = [start]
        while queue:
            cur = queue.pop()
            if cur in visited:
                continue
            visited.add(cur)
            cc, cr = cur
            cell = self.cells.get(cur)
            if cell is None or cell.tile_type != "floor":
                continue
            # Try each cardinal direction, respecting walls (but doors are passable)
            for dc, dr, side, _opp in (
                (0, -1, "N", "S"), (0, 1, "S", "N"),
                (1,  0, "E", "W"), (-1, 0, "W", "E"),
            ):
                has_wall = cell.walls.get(side, False)
                has_door = cell.doors.get(side, False)
                if has_wall and not has_door:
                    continue  # solid wall with no door blocks passage
                npos = (cc + dc, cr + dr)
                if npos not in visited:
                    nb = self.cells.get(npos)
                    if nb and nb.tile_type == "floor":
                        queue.append(npos)
        return list(visited)

    def is_region_connected_to_station(self, region: list[tuple[int, int]]) -> bool:
        """
        Return True if the region physically touches any other floor tile on the
        map via tile adjacency (N/S/E/W neighbour), regardless of walls.

        Returns True if there are no other floor tiles (first region placed).
        """
        region_set = set(region)
        outside_floor = {
            pos for pos, c in self.cells.items()
            if c.tile_type == "floor" and pos not in region_set
        }
        if not outside_floor:
            return True  # nothing else exists yet — first placement is always OK
        for col, row in region:
            for dc, dr in ((0, -1), (0, 1), (1, 0), (-1, 0)):
                if (col + dc, row + dr) in outside_floor:
                    return True
        return False

    def is_fully_connected(self) -> bool:
        """
        Return True if all floor tiles form a single tile-adjacent connected
        component (ignoring wall segments — pure spatial connectivity).

        Used to display an "island detected" warning in the build toolbar.
        """
        floor_tiles = {pos for pos, c in self.cells.items() if c.tile_type == "floor"}
        if len(floor_tiles) <= 1:
            return True
        start = next(iter(floor_tiles))
        visited: set[tuple[int, int]] = set()
        queue = [start]
        while queue:
            cur = queue.pop()
            if cur in visited:
                continue
            visited.add(cur)
            col, row = cur
            for dc, dr in ((0, -1), (0, 1), (1, 0), (-1, 0)):
                npos = (col + dc, row + dr)
                if npos in floor_tiles and npos not in visited:
                    queue.append(npos)
        return visited == floor_tiles

    # ── Internal helpers ─────────────────────────────────────────────────

    def _sync_walls(self, col: int, row: int) -> None:
        """After placing a floor tile, remove walls shared with floor neighbours."""
        cell = self.cells[(col, row)]
        for dc, dr, side, opp in (
            (0, -1, "N", "S"), (0, 1, "S", "N"),
            (1,  0, "E", "W"), (-1, 0, "W", "E"),
        ):
            nb = self.cells.get((col + dc, row + dr))
            if nb and nb.tile_type == "floor":
                cell.walls[side] = False
                nb.walls[opp] = False

    def _detach_from_room(self, col: int, row: int) -> None:
        """Remove a tile from its owning room, if any."""
        cell = self.cells.get((col, row))
        if cell is None or cell.room_uid is None:
            return
        room = self.rooms.get(cell.room_uid)
        if room:
            pos = (col, row)
            if pos in room.tile_positions:
                room.tile_positions.remove(pos)
            if not room.tile_positions:
                del self.rooms[room.uid]
        cell.room_uid = None

    # ── Serialisation ─────────────────────────────────────────────────────

    def to_dict(self) -> dict:
        return {
            "cols":  self.cols,
            "rows":  self.rows,
            "cells": [cell.to_dict() for cell in self.cells.values()
                      if cell.tile_type != "empty"],
            "rooms": {uid: room.to_dict() for uid, room in self.rooms.items()},
        }

    @classmethod
    def from_dict(cls, d: dict) -> "TileMap":
        tm = cls(cols=int(d.get("cols", 40)), rows=int(d.get("rows", 22)))
        for cell_data in d.get("cells", []):
            cell = TileCell.from_dict(cell_data)
            tm.cells[(cell.col, cell.row)] = cell
        tm.rooms = {uid: RoomInstance.from_dict(v)
                    for uid, v in d.get("rooms", {}).items()}
        return tm
