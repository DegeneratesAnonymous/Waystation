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

    room_uid — uid of the RoomInstance that has claimed this tile, or None.
    """
    col: int
    row: int
    tile_type: str = "empty"           # "empty" | "floor" | "wall"
    walls: dict[str, bool] = field(default_factory=lambda: {
        "N": False, "S": False, "E": False, "W": False,
    })
    room_uid: str | None = None

    def to_dict(self) -> dict:
        return {
            "col":       self.col,
            "row":       self.row,
            "tile_type": self.tile_type,
            "walls":     dict(self.walls),
            "room_uid":  self.room_uid,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "TileCell":
        default_walls = {"N": False, "S": False, "E": False, "W": False}
        stored_walls = d.get("walls", {})
        walls = {k: bool(stored_walls.get(k, False)) for k in ("N", "S", "E", "W")}
        return cls(
            col=int(d["col"]),
            row=int(d["row"]),
            tile_type=d.get("tile_type", "empty"),
            walls=walls,
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
    """
    uid: str
    name: str
    room_type_id: str | None = None
    tile_positions: list[tuple[int, int]] = field(default_factory=list)
    module_uid: str | None = None

    @classmethod
    def create(cls, name: str, tile_positions: list[tuple[int, int]]) -> "RoomInstance":
        return cls(
            uid=_new_uid(),
            name=name,
            tile_positions=list(tile_positions),
        )

    def tile_count(self) -> int:
        return len(self.tile_positions)

    def to_dict(self) -> dict:
        return {
            "uid":            self.uid,
            "name":           self.name,
            "room_type_id":   self.room_type_id,
            "tile_positions": [list(pos) for pos in self.tile_positions],
            "module_uid":     self.module_uid,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "RoomInstance":
        return cls(
            uid=d["uid"],
            name=d["name"],
            room_type_id=d.get("room_type_id"),
            tile_positions=[tuple(pos) for pos in d.get("tile_positions", [])],
            module_uid=d.get("module_uid"),
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
        """Add a wall segment to the given side of a floor tile."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "floor":
            cell.walls[side] = True

    def remove_wall_segment(self, col: int, row: int, side: str) -> None:
        """Remove a wall segment from the given side of a floor tile."""
        cell = self.cells.get((col, row))
        if cell and cell.tile_type == "floor":
            cell.walls[side] = False

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
        """Flood-fill from (col, row) across floor tiles not blocked by walls."""
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
            # Try each cardinal direction, respecting walls
            for dc, dr, side, _opp in (
                (0, -1, "N", "S"), (0, 1, "S", "N"),
                (1,  0, "E", "W"), (-1, 0, "W", "E"),
            ):
                if cell.walls.get(side, False):
                    continue  # wall blocks passage
                npos = (cc + dc, cr + dr)
                if npos not in visited:
                    nb = self.cells.get(npos)
                    if nb and nb.tile_type == "floor":
                        queue.append(npos)
        return list(visited)

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
