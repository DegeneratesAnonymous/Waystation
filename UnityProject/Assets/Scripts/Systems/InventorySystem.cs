// Inventory System — manages item storage in station cargo holds.
// Each module with a non-zero cargo_capacity acts as a cargo hold.
// Items stored in the module's inventory dict (itemId → quantity).
// Enforces capacity limits, item-type filters, and perishable decay.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class InventorySystem
    {
        private readonly ContentRegistry _registry;

        public InventorySystem(ContentRegistry registry) => _registry = registry;

        // ── Query helpers ─────────────────────────────────────────────────────

        public List<ModuleInstance> GetCargoHolds(StationState station)
        {
            var holds = new List<ModuleInstance>();
            foreach (var mod in station.modules.Values)
                if (_registry.Modules.TryGetValue(mod.definitionId, out var defn) && defn.cargoCapacity > 0)
                    holds.Add(mod);
            return holds;
        }

        public int GetCapacityTotal(ModuleInstance module)
        {
            if (_registry.Modules.TryGetValue(module.definitionId, out var defn)) return defn.cargoCapacity;
            return 0;
        }

        public float GetCapacityUsed(ModuleInstance module)
        {
            float total = 0f;
            foreach (var kv in module.inventory)
            {
                float weight = _registry.Items.TryGetValue(kv.Key, out var item) ? item.weight : 1f;
                total += weight * kv.Value;
            }
            return total;
        }

        public float GetCapacityFree(ModuleInstance module)
            => Mathf.Max(0f, GetCapacityTotal(module) - GetCapacityUsed(module));

        public bool CanStoreItem(ModuleInstance module, string itemId, int qty = 1)
        {
            int capTotal = GetCapacityTotal(module);
            if (capTotal <= 0) return false;

            _registry.Items.TryGetValue(itemId, out var itemDefn);

            if (module.cargoSettings != null && itemDefn != null)
                if (!module.cargoSettings.AllowsType(itemDefn.itemType)) return false;

            float weight = itemDefn != null ? itemDefn.weight : 1f;
            return GetCapacityFree(module) >= weight * qty;
        }

        // ── Mutation helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Add up to qty units of itemId to the specified module.
        /// Returns the number of units actually added.
        /// </summary>
        public int AddItem(StationState station, string moduleUid, string itemId, int qty)
        {
            if (qty <= 0) return 0;
            if (!station.modules.TryGetValue(moduleUid, out var module))
            {
                Debug.LogWarning($"[InventorySystem] Module '{moduleUid}' not found.");
                return 0;
            }

            int capTotal = GetCapacityTotal(module);
            if (capTotal <= 0) return 0;

            _registry.Items.TryGetValue(itemId, out var itemDefn);

            if (module.cargoSettings != null && itemDefn != null)
                if (!module.cargoSettings.AllowsType(itemDefn.itemType)) return 0;

            float weight = itemDefn != null ? itemDefn.weight : 1f;
            float free   = GetCapacityFree(module);

            // Enforce reserved capacity
            if (itemDefn != null && module.cargoSettings?.reservedByType != null)
            {
                float locked = 0f;
                foreach (var kv in module.cargoSettings.reservedByType)
                    if (kv.Key != itemDefn.itemType) locked += kv.Value * capTotal;
                free = Mathf.Max(0f, free - locked);
            }

            int maxAddable = weight > 0f ? (int)(free / weight) : qty;
            int actual     = Mathf.Min(qty, maxAddable);
            if (actual <= 0) return 0;

            module.inventory[itemId] = (module.inventory.ContainsKey(itemId) ? module.inventory[itemId] : 0) + actual;
            return actual;
        }

        /// <summary>
        /// Remove up to qty units of itemId from the specified module.
        /// Returns the number of units actually removed.
        /// </summary>
        public int RemoveItem(StationState station, string moduleUid, string itemId, int qty)
        {
            if (qty <= 0) return 0;
            if (!station.modules.TryGetValue(moduleUid, out var module))
            {
                Debug.LogWarning($"[InventorySystem] Module '{moduleUid}' not found.");
                return 0;
            }

            int current = module.inventory.ContainsKey(itemId) ? module.inventory[itemId] : 0;
            int actual  = Mathf.Min(qty, current);
            if (actual <= 0) return 0;

            int newQty = current - actual;
            if (newQty == 0) module.inventory.Remove(itemId);
            else             module.inventory[itemId] = newQty;
            return actual;
        }

        public int GetItemCount(StationState station, string itemId)
        {
            int total = 0;
            foreach (var mod in GetCargoHolds(station))
                total += mod.inventory.ContainsKey(itemId) ? mod.inventory[itemId] : 0;
            return total;
        }

        public Dictionary<string, int> GetTotalInventory(StationState station)
        {
            var totals = new Dictionary<string, int>();
            foreach (var mod in GetCargoHolds(station))
                foreach (var kv in mod.inventory)
                    totals[kv.Key] = (totals.ContainsKey(kv.Key) ? totals[kv.Key] : 0) + kv.Value;
            return totals;
        }

        public (float used, int total) GetStationCapacity(StationState station)
        {
            float used = 0f; int total = 0;
            foreach (var mod in GetCargoHolds(station))
            {
                used  += GetCapacityUsed(mod);
                total += GetCapacityTotal(mod);
            }
            return (used, total);
        }

        // ── Settings management ───────────────────────────────────────────────

        public void SetAllowedTypes(StationState station, string moduleUid, List<string> allowedTypes)
        {
            if (!station.modules.TryGetValue(moduleUid, out var module)) return;
            if (module.cargoSettings == null) module.cargoSettings = new CargoHoldSettings();
            module.cargoSettings.allowedTypes = new List<string>(allowedTypes);
        }

        public void AllowEverything(StationState station, string moduleUid)
            => SetAllowedTypes(station, moduleUid, new List<string>());

        public void AllowNothing(StationState station, string moduleUid)
            => SetAllowedTypes(station, moduleUid, new List<string> { "__none__" });

        public void SetReserved(StationState station, string moduleUid, string itemType, float fraction)
        {
            if (!station.modules.TryGetValue(moduleUid, out var module)) return;
            if (module.cargoSettings == null) module.cargoSettings = new CargoHoldSettings();
            fraction = Mathf.Clamp01(fraction);
            if (fraction == 0f) module.cargoSettings.reservedByType.Remove(itemType);
            else                module.cargoSettings.reservedByType[itemType] = fraction;
        }

        // ── Tick — perishable decay ───────────────────────────────────────────

        public void Tick(StationState station)
        {
            foreach (var mod in GetCargoHolds(station))
                DecayPerishables(station, mod, station.tick);
        }

        private void DecayPerishables(StationState station, ModuleInstance module, int tick)
        {
            var toDecay = new List<(string itemId, int qty)>();
            foreach (var kv in module.inventory)
            {
                if (!_registry.Items.TryGetValue(kv.Key, out var defn)) continue;
                if (defn.perishableTicks <= 0) continue;
                if (tick > 0 && tick % defn.perishableTicks == 0 && kv.Value > 0)
                    toDecay.Add((kv.Key, kv.Value));
            }

            foreach (var (itemId, currentQty) in toDecay)
            {
                // Decay exactly 1 unit per interval (same as Python prototype min(1, qty)).
                // currentQty is always > 0 here because of the guard above.
                int decayQty = 1;
                int newQty   = currentQty - decayQty;
                if (newQty <= 0) module.inventory.Remove(itemId);
                else             module.inventory[itemId] = newQty;

                string itemName = _registry.Items.TryGetValue(itemId, out var d) ? d.displayName : itemId;
                station.LogEvent($"Warning: {decayQty}× {itemName} in {module.displayName} has perished.");
            }
        }
    }
}
