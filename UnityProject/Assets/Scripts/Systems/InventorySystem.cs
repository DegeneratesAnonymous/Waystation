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
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Cargo inventory aggregate data (UI-010) ───────────────────────────────

    /// <summary>
    /// One row in the station inventory aggregate view.
    /// Holds totals across all cargo-hold containers plus a per-container breakdown.
    /// </summary>
    public class CargoItemRow
    {
        public string itemId;
        public string displayName;
        /// <summary>Item type string (e.g. "Material", "Equipment"). Used as the category badge / filter key.</summary>
        public string itemType;
        public int    totalQuantity;
        public float  totalWeight;
        /// <summary>Per-unit weight (kg) cached on first creation to avoid repeated registry lookups.</summary>
        internal float unitWeight;
        public List<CargoContainerEntry> containers = new List<CargoContainerEntry>();
    }

    /// <summary>
    /// One container's contribution to a <see cref="CargoItemRow"/> in the expanded detail view.
    /// </summary>
    public class CargoContainerEntry
    {
        public string foundationUid;
        /// <summary>
        /// Human-readable location label for the container:
        /// - a custom room name when available,
        /// - otherwise formatted as "Room {roomKey}",
        /// - or a tileKey-based label when no room is associated.
        /// </summary>
        public string roomName;
        public int    quantity;
    }

    public class InventorySystem
    {
        private readonly ContentRegistry _registry;

        /// <summary>
        /// Default Commitment Cooldown duration in game ticks.
        /// At the standard tick rate (1 tick = 15 in-game minutes) this equals
        /// 15 in-game hours (~30 real-time seconds at 0.5 s/tick).
        /// </summary>
        public const int DefaultCommitmentCooldownTicks = 60;

        /// <summary>
        /// Fired whenever items are added to or removed from any cargo-hold container
        /// or module.  Subscribe in the Inventory sub-panel to trigger a live refresh.
        /// </summary>
        public event Action OnContentsChanged;

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

        /// <summary>
        /// Item-count capacity used by a foundation container.
        /// This interprets foundation.cargoCapacity as a maximum number of item
        /// units (sum of all stack quantities), consistent with ResearchSystem,
        /// FarmingSystem, and FoundationInstance.CargoItemCount().
        /// </summary>
        public float GetCapacityUsed(FoundationInstance foundation)
        {
            float total = 0f;
            foreach (var kv in foundation.cargo)
                total += kv.Value;
            return total;
        }

        /// <summary>
        /// Remaining item-count capacity of a foundation container.
        /// Returns how many additional item units can be stored before reaching
        /// foundation.cargoCapacity.
        /// </summary>
        public float GetCapacityFree(FoundationInstance foundation)
            => Mathf.Max(0f, foundation.cargoCapacity - GetCapacityUsed(foundation));

        /// <summary>
        /// Returns true when qty units of itemId can be added to the container
        /// without exceeding item-count capacity or violating type filters.
        /// </summary>
        public bool CanStoreItem(FoundationInstance foundation, string itemId, int qty = 1)
        {
            if (foundation.cargoCapacity <= 0) return false;

            _registry.Items.TryGetValue(itemId, out var itemDefn);

            if (foundation.cargoSettings != null && itemDefn != null)
                if (!foundation.cargoSettings.AllowsType(itemDefn.itemType)) return false;

            return GetCapacityFree(foundation) >= qty;
        }

        // ── Foundation-based mutation helpers ─────────────────────────────────

        /// <summary>
        /// Add up to qty units of itemId to a foundation container, enforcing
        /// item-count capacity and type filters.  Returns the number of units added.
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

            int free       = (int)GetCapacityFree(foundation);
            int actual     = Mathf.Min(qty, free);
            if (actual <= 0) return 0;

            foundation.cargo[itemId] = (foundation.cargo.ContainsKey(itemId)
                ? foundation.cargo[itemId] : 0) + actual;

            // Apply Commitment Cooldown so the item is not immediately re-evaluated.
            if (cooldownTicks > 0)
                ApplyCommitmentCooldown(foundation, itemId, station.tick, cooldownTicks);

            OnContentsChanged?.Invoke();
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
            OnContentsChanged?.Invoke();
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
            OnContentsChanged?.Invoke();
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
            OnContentsChanged?.Invoke();
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

        // ── Material availability ─────────────────────────────────────────────

        /// <summary>
        /// Availability of required materials for a buildable or recipe.
        /// </summary>
        public enum MaterialStatus
        {
            /// <summary>All required materials are available in station storage.</summary>
            Sufficient,
            /// <summary>Some required materials are present but not all quantities are met.</summary>
            Partial,
            /// <summary>None of the required materials are present in station storage.</summary>
            Missing,
        }

        /// <summary>
        /// Checks whether station storage holds enough materials to build
        /// <paramref name="buildableId"/>.
        /// Returns <see cref="MaterialStatus.Sufficient"/> when all required quantities
        /// are met (including when the buildable has no material requirements), 
        /// <see cref="MaterialStatus.Partial"/> when at least one item is
        /// present but at least one requirement is not fully met, and
        /// <see cref="MaterialStatus.Missing"/> when none of the required items are
        /// available at all.
        /// </summary>
        public MaterialStatus CheckMaterials(StationState station, string buildableId)
        {
            if (!_registry.Buildables.TryGetValue(buildableId, out var defn))
                return MaterialStatus.Missing;

            if (defn.requiredMaterials == null || defn.requiredMaterials.Count == 0)
                return MaterialStatus.Sufficient;

            int metCount  = 0;
            int totalReqs = defn.requiredMaterials.Count;
            bool anyPresent = false;

            foreach (var kv in defn.requiredMaterials)
            {
                int have = GetItemCount(station, kv.Key);
                if (have >= kv.Value)
                    metCount++;
                else if (have > 0)
                    anyPresent = true;
            }

            if (metCount == totalReqs)
                return MaterialStatus.Sufficient;
            if (metCount > 0 || anyPresent)
                return MaterialStatus.Partial;
            return MaterialStatus.Missing;
        }

        /// <summary>
        /// Checks whether station storage holds enough input materials for a recipe.
        /// Returns <see cref="MaterialStatus.Sufficient"/> when all quantities are met
        /// (including when the recipe has no input materials),
        /// <see cref="MaterialStatus.Partial"/> when at least one item is present but
        /// at least one requirement is not fully met, and
        /// <see cref="MaterialStatus.Missing"/> when none of the required items are available.
        /// </summary>
        public MaterialStatus CheckRecipeMaterials(StationState station, RecipeDefinition recipe)
        {
            if (recipe == null) return MaterialStatus.Missing;
            if (recipe.inputMaterials == null || recipe.inputMaterials.Count == 0)
                return MaterialStatus.Sufficient;

            int  metCount   = 0;
            int  totalReqs  = recipe.inputMaterials.Count;
            bool anyPresent = false;

            foreach (var kv in recipe.inputMaterials)
            {
                int have = GetItemCount(station, kv.Key);
                if (have >= kv.Value)
                    metCount++;
                else if (have > 0)
                    anyPresent = true;
            }

            if (metCount == totalReqs)   return MaterialStatus.Sufficient;
            if (metCount > 0 || anyPresent) return MaterialStatus.Partial;
            return MaterialStatus.Missing;
        }

        /// <summary>
        /// Returns the <see cref="MaterialStatus"/> for a single item requirement.
        /// </summary>
        public MaterialStatus CheckSingleMaterial(StationState station, string itemId, int required)
        {
            int have = GetItemCount(station, itemId);
            if (have >= required) return MaterialStatus.Sufficient;
            if (have > 0)         return MaterialStatus.Partial;
            return MaterialStatus.Missing;
        }

        /// <summary>
        /// Returns a dictionary of item IDs mapped to the shortfall quantity for
        /// each material required by <paramref name="buildableId"/> that is not fully
        /// covered by current station storage.  An empty dictionary means all
        /// materials are available.  Returns an empty dictionary when the buildable
        /// is unknown or has no required materials.
        /// </summary>
        public Dictionary<string, int> GetMissingMaterials(StationState station, string buildableId)
        {
            var missing = new Dictionary<string, int>();
            if (!_registry.Buildables.TryGetValue(buildableId, out var defn))
                return missing;
            if (defn.requiredMaterials == null)
                return missing;

            foreach (var kv in defn.requiredMaterials)
            {
                int have = GetItemCount(station, kv.Key);
                if (have < kv.Value)
                    missing[kv.Key] = kv.Value - have;
            }
            return missing;
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
        /// Returns one <see cref="CargoItemRow"/> per unique itemId found across all
        /// cargo-hold containers (foundations in "cargo_hold" rooms).
        /// Each row aggregates quantities for that itemId and includes a per-container
        /// breakdown for the expanded detail view.
        /// Used by the Station → Inventory sub-panel (UI-010).
        /// </summary>
        public List<CargoItemRow> GetCargoHoldContents(StationState station)
        {
            if (station == null) return new List<CargoItemRow>();

            // itemId → row (accumulated across all containers)
            var rowMap = new Dictionary<string, CargoItemRow>(StringComparer.Ordinal);

            foreach (var foundation in GetCargoHoldContainers(station))
            {
                if (foundation.cargo.Count == 0) continue;

                // Determine the room name for this container once.
                string tileKey = $"{foundation.tileCol}_{foundation.tileRow}";
                string roomKey = station.tileToRoomKey.TryGetValue(tileKey, out var rk) ? rk : tileKey;
                string roomName = station.customRoomNames.TryGetValue(roomKey, out var cn) && !string.IsNullOrEmpty(cn)
                    ? cn
                    : $"Room {roomKey}";

                foreach (var kv in foundation.cargo)
                {
                    if (kv.Value <= 0) continue;

                    string itemId = kv.Key;
                    if (!rowMap.TryGetValue(itemId, out var row))
                    {
                        // First time we encounter this itemId — do the registry lookup once
                        // and cache everything on the row for subsequent containers.
                        _registry.Items.TryGetValue(itemId, out var itemDefn);
                        row = new CargoItemRow
                        {
                            itemId      = itemId,
                            displayName = itemDefn?.displayName ?? itemId,
                            itemType    = itemDefn?.itemType ?? "Unknown",
                            unitWeight  = itemDefn?.weight ?? 1f,
                        };
                        rowMap[itemId] = row;
                    }

                    row.totalQuantity += kv.Value;
                    row.totalWeight   += row.unitWeight * kv.Value;
                    row.containers.Add(new CargoContainerEntry
                    {
                        foundationUid = foundation.uid,
                        roomName      = roomName,
                        quantity      = kv.Value,
                    });
                }
            }

            var result = new List<CargoItemRow>(rowMap.Values);
            return result;
        }

        /// <summary>
        /// Returns the combined capacity across all module cargo holds and all
        /// foundation containers in "cargo_hold" rooms.
        /// Note: module holds use weight (kg) for 'used'; foundation containers
        /// use item-count for 'used'.  Total is similarly mixed — callers that need
        /// a precise fill ratio should query each source separately.
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

        // ── Hauling destination search ────────────────────────────────────────

        /// <summary>
        /// Returns storage foundations (cargoCapacity &gt; 0, status "complete") within
        /// <paramref name="radius"/> tiles (Manhattan distance) of the given origin tile.
        /// Foundations with any item under a Commitment Cooldown are still included as
        /// destinations — the cooldown applies to item re-evaluation, not to placing new items.
        /// Results are sorted by distance (closest first).
        /// </summary>
        public List<FoundationInstance> FindHaulCandidates(
            int originCol, int originRow, int radius,
            StationState station)
        {
            var result = new List<FoundationInstance>();
            foreach (var foundation in station.foundations.Values)
            {
                if (foundation.cargoCapacity <= 0) continue;
                if (foundation.status != "complete") continue;
                int dist = UnityEngine.Mathf.Abs(foundation.tileCol - originCol)
                         + UnityEngine.Mathf.Abs(foundation.tileRow - originRow);
                if (dist > radius) continue;
                result.Add(foundation);
            }
            // Sort by proximity
            result.Sort((a, b) =>
            {
                int dA = UnityEngine.Mathf.Abs(a.tileCol - originCol)
                       + UnityEngine.Mathf.Abs(a.tileRow - originRow);
                int dB = UnityEngine.Mathf.Abs(b.tileCol - originCol)
                       + UnityEngine.Mathf.Abs(b.tileRow - originRow);
                return dA.CompareTo(dB);
            });
            return result;
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
