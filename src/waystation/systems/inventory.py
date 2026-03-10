"""
Inventory System — manages item storage in station cargo holds.

Each station module with a non-zero cargo_capacity acts as a cargo hold.
Items are stored in the module's inventory dict (item_id -> quantity).
The system enforces capacity limits, item-type filters, and handles
perishable item decay on each tick.
"""

from __future__ import annotations

import logging
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from waystation.core.registry import ContentRegistry
    from waystation.models.instances import ModuleInstance, StationState

log = logging.getLogger(__name__)


class InventorySystem:
    """Manages cargo hold inventories across all station modules."""

    def __init__(self, registry: "ContentRegistry") -> None:
        self.registry = registry

    # ------------------------------------------------------------------
    # Query helpers
    # ------------------------------------------------------------------

    def get_cargo_holds(self, station: "StationState") -> list["ModuleInstance"]:
        """Return all modules that function as cargo holds (cargo_capacity > 0)."""
        holds = []
        for mod in station.modules.values():
            defn = self.registry.modules.get(mod.definition_id)
            if defn and defn.cargo_capacity > 0:
                holds.append(mod)
        return holds

    def get_capacity_total(self, module: "ModuleInstance") -> int:
        """Total item-unit capacity of a module (from its definition)."""
        defn = self.registry.modules.get(module.definition_id)
        return defn.cargo_capacity if defn else 0

    def get_capacity_used(self, module: "ModuleInstance") -> float:
        """
        Capacity consumed by current inventory.

        Each item unit consumes item.weight capacity units.
        Falls back to weight=1.0 if the item definition is not found.
        """
        total = 0.0
        for item_id, qty in module.inventory.items():
            defn = self.registry.items.get(item_id)
            weight = defn.weight if defn else 1.0
            total += weight * qty
        return total

    def get_capacity_free(self, module: "ModuleInstance") -> float:
        """Remaining capacity in the module."""
        return max(0.0, self.get_capacity_total(module) - self.get_capacity_used(module))

    def can_store_item(self, module: "ModuleInstance", item_id: str, qty: int = 1) -> bool:
        """
        Return True if qty units of item_id can be stored in this module.

        Checks:
        - Module has cargo capacity (is a cargo hold)
        - Item type is allowed by module's cargo_settings filter
        - Sufficient free capacity exists
        """
        cap_total = self.get_capacity_total(module)
        if cap_total <= 0:
            return False

        item_defn = self.registry.items.get(item_id)

        # Check type filter via allows_type()
        if module.cargo_settings and item_defn:
            if not module.cargo_settings.allows_type(item_defn.item_type):
                return False

        weight = item_defn.weight if item_defn else 1.0
        return self.get_capacity_free(module) >= weight * qty

    # ------------------------------------------------------------------
    # Mutation helpers
    # ------------------------------------------------------------------

    def add_item(self, station: "StationState", module_uid: str,
                 item_id: str, qty: int) -> int:
        """
        Add up to qty units of item_id to the specified module.

        Returns the number of units actually added (may be less than qty
        if capacity is insufficient or item type is not allowed).
        """
        if qty <= 0:
            return 0
        module = station.modules.get(module_uid)
        if module is None:
            log.warning("add_item: module '%s' not found", module_uid)
            return 0

        cap_total = self.get_capacity_total(module)
        if cap_total <= 0:
            return 0

        item_defn = self.registry.items.get(item_id)  # single lookup

        # Enforce type filter via allows_type()
        if module.cargo_settings and item_defn:
            if not module.cargo_settings.allows_type(item_defn.item_type):
                return 0

        weight = item_defn.weight if item_defn else 1.0

        free = self.get_capacity_free(module)
        # Enforce reserved capacity: subtract capacity locked for other types
        if item_defn and module.cargo_settings and module.cargo_settings.reserved_by_type:
            cap_locked = sum(
                frac * cap_total
                for res_type, frac in module.cargo_settings.reserved_by_type.items()
                if res_type != item_defn.item_type
            )
            free = max(0.0, free - cap_locked)

        max_addable = int(free / weight) if weight > 0 else qty
        actual = min(qty, max_addable)
        if actual <= 0:
            return 0

        module.inventory[item_id] = module.inventory.get(item_id, 0) + actual
        return actual

    def remove_item(self, station: "StationState", module_uid: str,
                    item_id: str, qty: int) -> int:
        """
        Remove up to qty units of item_id from the specified module.

        Returns the number of units actually removed.
        """
        if qty <= 0:
            return 0
        module = station.modules.get(module_uid)
        if module is None:
            log.warning("remove_item: module '%s' not found", module_uid)
            return 0

        current = module.inventory.get(item_id, 0)
        actual = min(qty, current)
        if actual <= 0:
            return 0

        new_qty = current - actual
        if new_qty == 0:
            module.inventory.pop(item_id, None)
        else:
            module.inventory[item_id] = new_qty
        return actual

    def get_item_count(self, station: "StationState", item_id: str) -> int:
        """Total quantity of item_id across all cargo holds."""
        return sum(
            mod.inventory.get(item_id, 0)
            for mod in self.get_cargo_holds(station)
        )

    def get_total_inventory(self, station: "StationState") -> dict[str, int]:
        """Aggregate inventory across all cargo holds (item_id -> total quantity)."""
        totals: dict[str, int] = {}
        for mod in self.get_cargo_holds(station):
            for item_id, qty in mod.inventory.items():
                totals[item_id] = totals.get(item_id, 0) + qty
        return totals

    def get_station_capacity(self, station: "StationState") -> tuple[float, int]:
        """Return (used, total) capacity across all cargo holds."""
        used = sum(self.get_capacity_used(m) for m in self.get_cargo_holds(station))
        total = sum(self.get_capacity_total(m) for m in self.get_cargo_holds(station))
        return used, total

    # ------------------------------------------------------------------
    # Settings management
    # ------------------------------------------------------------------

    def set_allowed_types(self, station: "StationState", module_uid: str,
                          allowed_types: list[str]) -> None:
        """Configure which item types a cargo hold will accept."""
        from waystation.models.instances import CargoHoldSettings
        module = station.modules.get(module_uid)
        if module is None:
            return
        if module.cargo_settings is None:
            module.cargo_settings = CargoHoldSettings()
        module.cargo_settings.allowed_types = list(allowed_types)

    def allow_everything(self, station: "StationState", module_uid: str) -> None:
        """Remove all type filters — hold accepts all item types."""
        self.set_allowed_types(station, module_uid, [])

    def allow_nothing(self, station: "StationState", module_uid: str) -> None:
        """Block all item types (hold acts as locked/restricted)."""
        # Setting a sentinel value list blocks all items
        self.set_allowed_types(station, module_uid, ["__none__"])

    def set_reserved(self, station: "StationState", module_uid: str,
                     item_type: str, fraction: float) -> None:
        """Reserve a fraction (0.0–1.0) of capacity for a given item type."""
        from waystation.models.instances import CargoHoldSettings
        module = station.modules.get(module_uid)
        if module is None:
            return
        if module.cargo_settings is None:
            module.cargo_settings = CargoHoldSettings()
        fraction = max(0.0, min(1.0, fraction))
        if fraction == 0.0:
            module.cargo_settings.reserved_by_type.pop(item_type, None)
        else:
            module.cargo_settings.reserved_by_type[item_type] = fraction

    # ------------------------------------------------------------------
    # Tick — perishable decay
    # ------------------------------------------------------------------

    def tick(self, station: "StationState") -> None:
        """Handle perishable item decay for all cargo holds."""
        tick = station.tick
        for mod in self.get_cargo_holds(station):
            self._decay_perishables(station, mod, tick)

    def _decay_perishables(self, station: "StationState",
                           module: "ModuleInstance", tick: int) -> None:
        """Decay perishable items by 1 unit per perishable_ticks interval."""
        to_decay: list[tuple[str, int]] = []
        for item_id, qty in module.inventory.items():
            defn = self.registry.items.get(item_id)
            if defn is None or defn.perishable_ticks <= 0:
                continue
            if tick > 0 and tick % defn.perishable_ticks == 0 and qty > 0:
                to_decay.append((item_id, qty))  # capture snapshot of current qty

        for item_id, current_qty in to_decay:
            decay_qty = min(1, current_qty)
            new_qty = current_qty - decay_qty
            if new_qty <= 0:
                module.inventory.pop(item_id, None)
            else:
                module.inventory[item_id] = new_qty
            item_name = (self.registry.items[item_id].display_name
                         if item_id in self.registry.items else item_id)
            station.log_event(
                f"Warning: {decay_qty}× {item_name} in {module.display_name} has perished."
            )
