// Inventory System — manages item storage in station cargo holds.
// Each module with a non-zero cargo_capacity acts as a cargo hold.
// Items stored in the module's inventory dict (itemId → quantity).
// Enforces capacity limits, item-type filters, and perishable decay.
//
// Physical container model (INF-004):
// FoundationInstance objects with cargoCapacity > 0 and status "complete" act as
// physical container furniture.  Containers placed in a room designated as
// "cargo_hold" participate in the station inventory aggregate view.
//
// NPC carry capacity:
//   Total = NPCTemplate.pocketCapacity + equipped backpack ItemDefinition.carryCapacity
//
// Commitment Cooldown:
//   When an item is placed in a container the caller may apply a cooldown tag
//   (foundation.commitmentCooldowns[itemId] = expiry tick).  NPC haul evaluation
//   must call IsItemOnCommitmentCooldown() before picking an item as a task target.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class InventorySystem
    {
        private readonly ContentRegistry _registry;

        /// <summary>
        /// Default Commitment Cooldown duration in game ticks.
        /// At the standard tick rate (1 tick = 15 in-game minutes) this equals
        /// 15 in-game hours (~30 real-time seconds at 0.5 s/tick).
        /// </summary>
        public const int DefaultCommitmentCooldownTicks = 60;

        public InventorySystem(ContentRegistry registry) => _registry = registry;

        // ── Module-based query helpers ────────────────────────────────────────

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

        // ── Foundation-based container query helpers ──────────────────────────

        /// <summary>
        /// Returns all complete FoundationInstance objects with cargoCapacity > 0
        /// that are placed in a room designated as "cargo_hold".
        /// These containers appear in the station inventory aggregate view.
        /// </summary>
        public List<FoundationInstance> GetCargoHoldContainers(StationState station)
        {
            var result = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.cargoCapacity <= 0) continue;
                if (!IsInCargoHoldRoom(station, f)) continue;
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// Returns all complete FoundationInstance objects with cargoCapacity > 0,
        /// regardless of room designation.  Used when looking for available storage
        /// for a haul task.
        /// </summary>
        public List<FoundationInstance> GetAllContainers(StationState station)
        {
            var result = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.cargoCapacity <= 0) continue;
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// Returns true when the foundation tile is located in a room with a
        /// "cargo_hold" player-assigned room type.
        /// </summary>
        public bool IsInCargoHoldRoom(StationState station, FoundationInstance foundation)
        {
            string tileKey = $"{foundation.tileCol}_{foundation.tileRow}";
            if (!station.tileToRoomKey.TryGetValue(tileKey, out var roomKey)) return false;
            if (!station.playerRoomTypeAssignments.TryGetValue(roomKey, out var typeId)) return false;
            return typeId == "cargo_hold";
        }

        /// <summary>Weight-based capacity used by a foundation container.</summary>
        public float GetCapacityUsed(FoundationInstance foundation)
        {
            float total = 0f;
            foreach (var kv in foundation.cargo)
            {
                float weight = _registry.Items.TryGetValue(kv.Key, out var item) ? item.weight : 1f;
                total += weight * kv.Value;
            }
            return total;
        }

        /// <summary>Remaining weight capacity of a foundation container.</summary>
        public float GetCapacityFree(FoundationInstance foundation)
            => Mathf.Max(0f, foundation.cargoCapacity - GetCapacityUsed(foundation));

        /// <summary>
        /// Returns true when qty units of itemId can be added to the container
        /// without exceeding weight capacity or violating type filters.
        /// </summary>
        public bool CanStoreItem(FoundationInstance foundation, string itemId, int qty = 1)
        {
            if (foundation.cargoCapacity <= 0) return false;

            _registry.Items.TryGetValue(itemId, out var itemDefn);

            if (foundation.cargoSettings != null && itemDefn != null)
                if (!foundation.cargoSettings.AllowsType(itemDefn.itemType)) return false;

            float weight = itemDefn != null ? itemDefn.weight : 1f;
            return GetCapacityFree(foundation) >= weight * qty;
        }

        // ── Foundation-based mutation helpers ─────────────────────────────────

        /// <summary>
        /// Add up to qty units of itemId to a foundation container, enforcing
        /// weight capacity and type filters.  Returns the number of units added.
        /// A Commitment Cooldown is applied automatically on successful placement.
        /// </summary>
        public int AddItemToContainer(StationState station, string foundationUid,
                                      string itemId, int qty,
                                      int cooldownTicks = DefaultCommitmentCooldownTicks)
        {
            if (qty <= 0) return 0;
            if (!station.foundations.TryGetValue(foundationUid, out var foundation))
            {
                Debug.LogWarning($"[InventorySystem] Foundation '{foundationUid}' not found.");
                return 0;
            }
            if (foundation.status != "complete") return 0;
            if (foundation.cargoCapacity <= 0)   return 0;

            _registry.Items.TryGetValue(itemId, out var itemDefn);

            if (foundation.cargoSettings != null && itemDefn != null)
                if (!foundation.cargoSettings.AllowsType(itemDefn.itemType)) return 0;

            float weight     = itemDefn != null ? itemDefn.weight : 1f;
            float free       = GetCapacityFree(foundation);
            int   maxAddable = weight > 0f ? (int)(free / weight) : qty;
            int   actual     = Mathf.Min(qty, maxAddable);
            if (actual <= 0) return 0;

            foundation.cargo[itemId] = (foundation.cargo.ContainsKey(itemId)
                ? foundation.cargo[itemId] : 0) + actual;

            // Apply Commitment Cooldown so the item is not immediately re-evaluated.
            if (cooldownTicks > 0)
                ApplyCommitmentCooldown(foundation, itemId, station.tick, cooldownTicks);

            return actual;
        }

        /// <summary>
        /// Remove up to qty units of itemId from a foundation container.
        /// Returns the number of units actually removed.
        /// </summary>
        public int RemoveItemFromContainer(StationState station, string foundationUid,
                                           string itemId, int qty)
        {
            if (qty <= 0) return 0;
            if (!station.foundations.TryGetValue(foundationUid, out var foundation))
            {
                Debug.LogWarning($"[InventorySystem] Foundation '{foundationUid}' not found.");
                return 0;
            }

            int current = foundation.cargo.ContainsKey(itemId) ? foundation.cargo[itemId] : 0;
            int actual  = Mathf.Min(qty, current);
            if (actual <= 0) return 0;

            int newQty = current - actual;
            if (newQty == 0)
            {
                foundation.cargo.Remove(itemId);
                foundation.commitmentCooldowns.Remove(itemId);
            }
            else
            {
                foundation.cargo[itemId] = newQty;
            }
            return actual;
        }

        // ── Module-based mutation helpers ─────────────────────────────────────

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

        // ── Aggregate queries (modules + cargo-hold containers) ───────────────

        public int GetItemCount(StationState station, string itemId)
        {
            int total = 0;
            foreach (var mod in GetCargoHolds(station))
                total += mod.inventory.ContainsKey(itemId) ? mod.inventory[itemId] : 0;
            foreach (var f in GetCargoHoldContainers(station))
                total += f.cargo.ContainsKey(itemId) ? f.cargo[itemId] : 0;
            return total;
        }

        /// <summary>
        /// Returns the combined inventory of all module cargo holds AND all
        /// foundation containers that are in a "cargo_hold" designated room.
        /// </summary>
        public Dictionary<string, int> GetTotalInventory(StationState station)
        {
            var totals = new Dictionary<string, int>();
            foreach (var mod in GetCargoHolds(station))
                foreach (var kv in mod.inventory)
                    totals[kv.Key] = (totals.ContainsKey(kv.Key) ? totals[kv.Key] : 0) + kv.Value;
            foreach (var f in GetCargoHoldContainers(station))
                foreach (var kv in f.cargo)
                    totals[kv.Key] = (totals.ContainsKey(kv.Key) ? totals[kv.Key] : 0) + kv.Value;
            return totals;
        }

        /// <summary>
        /// Returns the combined capacity (used kg, total kg) across all module cargo
        /// holds and all foundation containers in "cargo_hold" rooms.
        /// </summary>
        public (float used, int total) GetStationCapacity(StationState station)
        {
            float used = 0f; int total = 0;
            foreach (var mod in GetCargoHolds(station))
            {
                used  += GetCapacityUsed(mod);
                total += GetCapacityTotal(mod);
            }
            foreach (var f in GetCargoHoldContainers(station))
            {
                used  += GetCapacityUsed(f);
                total += f.cargoCapacity;
            }
            return (used, total);
        }

        // ── NPC carry capacity ────────────────────────────────────────────────

        /// <summary>
        /// Computes the total personal carry capacity (kg) for an NPC.
        /// = species pocket capacity from NPC template
        /// + carry_capacity of the item equipped in the "backpack" slot (if any).
        /// Falls back to a default pocket capacity of 10 kg when no template is found.
        /// </summary>
        public float GetNpcCarryCapacity(NPCInstance npc)
        {
            float pocket = 10f;  // fallback
            if (npc.templateId != null &&
                _registry.Npcs.TryGetValue(npc.templateId, out var tmpl))
                pocket = tmpl.pocketCapacity;

            float backpack = 0f;
            if (npc.equippedSlots.TryGetValue("backpack", out var backpackItemId) &&
                backpackItemId != null &&
                _registry.Items.TryGetValue(backpackItemId, out var backpackItem))
                backpack = backpackItem.carryCapacity;

            return pocket + backpack;
        }

        /// <summary>
        /// Returns the total weight (kg) of items currently in the NPC's pocket inventory.
        /// </summary>
        public float GetNpcCarryUsed(NPCInstance npc)
        {
            float total = 0f;
            foreach (var kv in npc.pocketItems)
            {
                float weight = _registry.Items.TryGetValue(kv.Key, out var item) ? item.weight : 1f;
                total += weight * kv.Value;
            }
            return total;
        }

        /// <summary>Returns the remaining carry capacity (kg) available to the NPC.</summary>
        public float GetNpcCarryFree(NPCInstance npc)
            => Mathf.Max(0f, GetNpcCarryCapacity(npc) - GetNpcCarryUsed(npc));

        // ── Commitment Cooldown ───────────────────────────────────────────────

        /// <summary>
        /// Applies a Commitment Cooldown to itemId in the given container.
        /// NPC haul task evaluation must check this before selecting the item.
        /// </summary>
        public void ApplyCommitmentCooldown(FoundationInstance foundation,
                                            string itemId, int currentTick,
                                            int durationTicks = DefaultCommitmentCooldownTicks)
        {
            foundation.commitmentCooldowns[itemId] = currentTick + durationTicks;
        }

        /// <summary>
        /// Returns true when the item is still under its Commitment Cooldown and
        /// must not be re-evaluated as a haul candidate.
        /// </summary>
        public bool IsItemOnCommitmentCooldown(FoundationInstance foundation,
                                               string itemId, int currentTick)
        {
            if (!foundation.commitmentCooldowns.TryGetValue(itemId, out int expiry))
                return false;
            return currentTick < expiry;
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
            => SetAllowedTypes(station, moduleUid, new List<string> { CargoHoldSettings.AllowNoneSentinel });

        public void SetReserved(StationState station, string moduleUid, string itemType, float fraction)
        {
            if (!station.modules.TryGetValue(moduleUid, out var module)) return;
            if (module.cargoSettings == null) module.cargoSettings = new CargoHoldSettings();
            fraction = Mathf.Clamp01(fraction);
            if (fraction == 0f) module.cargoSettings.reservedByType.Remove(itemType);
            else                module.cargoSettings.reservedByType[itemType] = fraction;
        }

        // ── Tick — perishable decay + cooldown maintenance ────────────────────

        public void Tick(StationState station)
        {
            // Decay module-based cargo holds
            foreach (var mod in GetCargoHolds(station))
                DecayPerishables(station, mod, station.tick);

            // Decay foundation-based containers (all containers, not just cargo_hold rooms)
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || f.cargoCapacity <= 0) continue;
                DecayPerishables(station, f, station.tick);
                PruneExpiredCooldowns(f, station.tick);
            }
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

        /// <summary>
        /// Decay perishable items in a foundation container by one unit when the
        /// tick interval has elapsed.  Destroyed items are removed from the container.
        /// </summary>
        private void DecayPerishables(StationState station, FoundationInstance foundation, int tick)
        {
            var toDecay = new List<(string itemId, int qty)>();
            foreach (var kv in foundation.cargo)
            {
                if (!_registry.Items.TryGetValue(kv.Key, out var defn)) continue;
                if (defn.perishableTicks <= 0) continue;
                if (tick > 0 && tick % defn.perishableTicks == 0 && kv.Value > 0)
                    toDecay.Add((kv.Key, kv.Value));
            }

            foreach (var (itemId, currentQty) in toDecay)
            {
                int decayQty = 1;
                int newQty   = currentQty - decayQty;
                if (newQty <= 0)
                {
                    foundation.cargo.Remove(itemId);
                    foundation.commitmentCooldowns.Remove(itemId);
                }
                else
                {
                    foundation.cargo[itemId] = newQty;
                }

                string itemName = _registry.Items.TryGetValue(itemId, out var d) ? d.displayName : itemId;
                station.LogEvent($"Warning: {decayQty}× {itemName} in container at " +
                                 $"({foundation.tileCol},{foundation.tileRow}) has perished.");
            }
        }

        /// <summary>Remove commitment cooldown entries that have already expired.</summary>
        private void PruneExpiredCooldowns(FoundationInstance foundation, int currentTick)
        {
            var expired = new List<string>();
            foreach (var kv in foundation.commitmentCooldowns)
                if (currentTick >= kv.Value) expired.Add(kv.Key);
            foreach (var key in expired)
                foundation.commitmentCooldowns.Remove(key);
        }
    }
}
