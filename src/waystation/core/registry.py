"""
Content Registry — central loader for all data-driven content.

Reads YAML files from /data/ and any enabled /mods/ folders.
Validates, indexes by ID, and exposes content to game systems.

Design rules:
- Core data is always loaded first.
- Mods are additive; they can add new IDs or override existing ones.
- Bad content produces clear errors but does not crash unless critical.
- All content is keyed by stable string IDs.
"""

from __future__ import annotations

import json
import logging
import os
from pathlib import Path
from typing import Any, TypeVar, Type

import yaml

from waystation.models.templates import (
    EventDefinition,
    NPCTemplate,
    ShipTemplate,
    ClassDefinition,
    FactionDefinition,
    ModuleDefinition,
    BuildableDefinition,
)

log = logging.getLogger(__name__)

T = TypeVar("T")

# ---------------------------------------------------------------------------
# Loader helpers
# ---------------------------------------------------------------------------

def _load_yaml_file(path: Path) -> list[dict]:
    """Load a YAML file and always return a list of dicts."""
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = yaml.safe_load(f)
        if data is None:
            return []
        if isinstance(data, dict):
            # Single-document files can be bare dicts
            return [data]
        if isinstance(data, list):
            return data
        log.warning("Unexpected YAML structure in %s — skipping.", path)
        return []
    except yaml.YAMLError as e:
        log.error("YAML parse error in %s: %s", path, e)
        return []
    except OSError as e:
        log.error("Cannot read %s: %s", path, e)
        return []


def _load_json_file(path: Path) -> dict:
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (json.JSONDecodeError, OSError) as e:
        log.error("Cannot read %s: %s", path, e)
        return {}


def _load_folder(folder: Path) -> list[dict]:
    """Load all YAML files from a folder, returning a flat list of records."""
    records: list[dict] = []
    if not folder.is_dir():
        return records
    for file in sorted(folder.glob("*.yaml")):
        records.extend(_load_yaml_file(file))
    for file in sorted(folder.glob("*.yml")):
        records.extend(_load_yaml_file(file))
    return records


# ---------------------------------------------------------------------------
# Registry
# ---------------------------------------------------------------------------

class ContentRegistry:
    """
    Central store for all static content definitions.

    Access via registry.events["event.id"], registry.npcs["npc.id"], etc.
    """

    REQUIRED_FIELDS: dict[str, list[str]] = {
        "events":     ["id", "category", "title"],
        "npcs":       ["id", "base_class"],
        "ships":      ["id", "role"],
        "classes":    ["id"],
        "factions":   ["id", "display_name"],
        "modules":    ["id", "display_name"],
        "buildables": ["id", "display_name"],
    }

    def __init__(self) -> None:
        self.events:     dict[str, EventDefinition]    = {}
        self.npcs:       dict[str, NPCTemplate]        = {}
        self.ships:      dict[str, ShipTemplate]       = {}
        self.classes:    dict[str, ClassDefinition]    = {}
        self.factions:   dict[str, FactionDefinition]  = {}
        self.modules:    dict[str, ModuleDefinition]   = {}
        self.buildables: dict[str, BuildableDefinition] = {}

        self._errors: list[str] = []
        self._loaded_mods: list[str] = []
        self._loaded: bool = False

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def load_core(self, data_root: Path) -> None:
        """Load all core content from the /data directory."""
        log.info("Loading core content from %s", data_root)
        self._load_content_folder(data_root / "events",     "events")
        self._load_content_folder(data_root / "npcs",       "npcs")
        self._load_content_folder(data_root / "ships",      "ships")
        self._load_content_folder(data_root / "classes",    "classes")
        self._load_content_folder(data_root / "factions",   "factions")
        self._load_content_folder(data_root / "modules",    "modules")
        self._load_content_folder(data_root / "buildables", "buildables")
        self._loaded = True
        log.info(
            "Core load complete — events:%d npcs:%d ships:%d classes:%d "
            "factions:%d modules:%d buildables:%d",
            len(self.events), len(self.npcs), len(self.ships),
            len(self.classes), len(self.factions), len(self.modules),
            len(self.buildables),
        )

    def load_mods(self, mods_root: Path) -> None:
        """Load all enabled mods from the /mods directory in order."""
        if not mods_root.is_dir():
            return
        for mod_folder in sorted(mods_root.iterdir()):
            meta_path = mod_folder / "mod.json"
            if not meta_path.exists():
                continue
            meta = _load_json_file(meta_path)
            if not meta.get("enabled", True):
                log.info("Mod %s is disabled — skipping.", mod_folder.name)
                continue
            mod_id = meta.get("id", mod_folder.name)
            schema_ver = meta.get("schema_version", "1")
            log.info("Loading mod: %s (schema_version=%s)", mod_id, schema_ver)
            self._load_mod_folder(mod_folder, mod_id)
            self._loaded_mods.append(mod_id)

    def summary(self) -> str:
        lines = [
            f"  events:     {len(self.events)}",
            f"  npcs:       {len(self.npcs)}",
            f"  ships:      {len(self.ships)}",
            f"  classes:    {len(self.classes)}",
            f"  factions:   {len(self.factions)}",
            f"  modules:    {len(self.modules)}",
            f"  buildables: {len(self.buildables)}",
        ]
        if self._loaded_mods:
            lines.append(f"  mods: {', '.join(self._loaded_mods)}")
        if self._errors:
            lines.append(f"  errors: {len(self._errors)}")
        return "\n".join(lines)

    def errors(self) -> list[str]:
        return list(self._errors)

    def is_loaded(self) -> bool:
        """Return True if core content has been fully loaded into this registry."""
        return self._loaded

    # ------------------------------------------------------------------
    # Internal loading
    # ------------------------------------------------------------------

    def _load_content_folder(self, folder: Path, content_type: str) -> None:
        records = _load_folder(folder)
        for raw in records:
            self._register(raw, content_type, source=str(folder))

    def _load_mod_folder(self, mod_folder: Path, mod_id: str) -> None:
        for content_type in ("events", "npcs", "ships", "classes", "factions",
                             "modules", "buildables"):
            folder = mod_folder / content_type
            records = _load_folder(folder)
            for raw in records:
                self._register(raw, content_type, source=f"mod:{mod_id}")

    def _register(self, raw: dict, content_type: str, source: str) -> None:
        if not isinstance(raw, dict):
            self._error(source, content_type, "?", "record is not a dict")
            return

        record_id = raw.get("id", "")
        if not record_id:
            self._error(source, content_type, "?", "missing required field 'id'")
            return

        # Check required fields
        required = self.REQUIRED_FIELDS.get(content_type, ["id"])
        for field_name in required:
            if field_name not in raw:
                self._error(source, content_type, record_id,
                            f"missing required field '{field_name}'")
                return

        try:
            if content_type == "events":
                self.events[record_id] = EventDefinition.from_raw(raw)
            elif content_type == "npcs":
                self.npcs[record_id] = NPCTemplate.from_raw(raw)
            elif content_type == "ships":
                self.ships[record_id] = ShipTemplate.from_raw(raw)
            elif content_type == "classes":
                self.classes[record_id] = ClassDefinition.from_raw(raw)
            elif content_type == "factions":
                self.factions[record_id] = FactionDefinition.from_raw(raw)
            elif content_type == "modules":
                self.modules[record_id] = ModuleDefinition.from_raw(raw)
            elif content_type == "buildables":
                self.buildables[record_id] = BuildableDefinition.from_raw(raw)
        except Exception as e:
            self._error(source, content_type, record_id, str(e))

    def _error(self, source: str, content_type: str, record_id: str, message: str) -> None:
        msg = f"[{source}] {content_type}/{record_id}: {message}"
        self._errors.append(msg)
        log.error(msg)
