// Building System — manages build foundations and Engineer NPC construction tasks.
//
// Workflow:
//   1. Player calls PlaceFoundation() to create a FoundationInstance on the station.
//   2. Tick() detects "awaiting_haul" foundations; an idle Engineer NPC hauls the
//      required materials from station cargo holds to the build site.
//      Partial haul is allowed — construction starts as soon as any materials arrive
//      and progresses only as far as the proportion of materials on-site allows.
//      When the material ceiling is reached, construction reverts to "awaiting_haul"
//      until more stock is available, then resumes automatically.
//   3. Once all materials are present the foundation transitions to "constructing"
//      and the engineer spends buildTimeTicks advancing buildProgress to 1.0.
//   4. On completion the foundation is marked "complete" and the engineer is freed.
//
// Health & functionality rules (applied once built — see FoundationInstance.Functionality):
//   100–75 % HP  →  1.0 (full function)
//   75–50 % HP   →  linearly degraded
//   < 50 % HP    →  0.0 (non-functional; still pulls power if applicable)
//   0 % HP       →  0.0 (destroyed)
//
// Damage & repair:
//   Call DamageFoundation() to apply HP damage to a complete foundation.
//   When HP reaches 0, pendingRepair is set and the repair pipeline runs:
//   an engineer hauls the required materials and restores HP to maxHealth.
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class BuildingSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const string BuildJobId         = "job.build";
        private const string RepairJobId        = "job.repair";
        private const string EngineerClassId    = "class.engineering";

        /// <summary>
        /// Ticks to set job_timer each tick BuildingSystem holds an engineer.
        /// This keeps JobSystem (which runs before BuildingSystem) from
        /// reassigning the worker before construction finishes.  The value of 5
        /// gives JobSystem a small window while still allowing prompt reassignment
        /// once the foundation is complete and the timer is no longer refreshed.
        /// </summary>
        private const int BuildJobTimerTicks = 5;

        private const int DefaultBuildTimeTicks = 50;

        private readonly ContentRegistry _registry;

        /// <summary>
        /// Set to true when a foundation whose buildable has a networkType transitions
        /// to "complete".  GameManager checks this after Tick() and calls
        /// UtilityNetworks.RebuildAll() so the new tile joins its network immediately.
        /// Cleared by GameManager after the rebuild.
        /// </summary>
        public bool NetworkRebuildNeeded { get; private set; }

        /// <summary>Called by GameManager after it has triggered a network rebuild.</summary>
        public void ClearNetworkRebuildFlag() => NetworkRebuildNeeded = false;

        /// <summary>
        /// Set to true when a workbench or structural foundation (wall/door) transitions
        /// to "complete".  GameManager checks this after Tick() and calls
        /// Rooms.RebuildBonusCache() so the bonus cache is current within the same tick.
        /// Cleared by GameManager after the rebuild.
        /// </summary>
        public bool RoomRebuildNeeded { get; private set; }

        /// <summary>Called by GameManager after it has triggered a room bonus rebuild.</summary>
        public void ClearRoomRebuildFlag() => RoomRebuildNeeded = false;

        /// <summary>
        /// When true, foundations skip the haul phase and complete instantly
        /// without consuming any materials.  Toggled via the in-game Dev Tools button,
        /// or forced on when FeatureFlags.ConstructionPipeline is false.
        /// </summary>
        public static bool DevMode
        {
            get => _devMode || !FeatureFlags.ConstructionPipeline;
            set => _devMode = value;
        }
        private static bool _devMode = false;

        public BuildingSystem(ContentRegistry registry) => _registry = registry;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Place a new foundation at the given tile position.
        /// Returns the created instance, or null if the buildable is unknown or
        /// the station is missing a required tag.
        /// </summary>
        public FoundationInstance PlaceFoundation(StationState station,
                                                   string buildableId,
                                                   int col, int row,
                                                   int rotation = 0)
        {
            if (!_registry.Buildables.TryGetValue(buildableId, out var defn))
            {
                Debug.LogError($"[BuildingSystem] Unknown buildable '{buildableId}'");
                return null;
            }

            // Enforce required_tags at the system level so bypasses via direct API
            // calls are blocked in the same way the UI is.
            foreach (var tag in defn.requiredTags)
            {
                if (!station.HasTag(tag))
                {
                    Debug.LogWarning(
                        $"[BuildingSystem] Cannot place '{buildableId}': " +
                        $"station missing required tag '{tag}'.");
                    return null;
                }
            }

            var foundation = FoundationInstance.Create(buildableId, col, row,
                                                        defn.maxHealth, defn.buildQuality,
                                                        rotation, defn.cargoCapacity);
            // ── Layer rules ──────────────────────────────────────────────────────
            int targetLayer = defn.tileLayer;
            int tileW       = defn.tileWidth;
            int tileH       = defn.tileHeight;

            // 1. Cancel or remove any same-layer foundation overlapping the new footprint.
            //    Check the full footprint of each existing foundation (tileWidth × tileHeight)
            //    against the full footprint of the new placement so multi-tile objects don't
            //    slip through on non-origin tiles.
            var conflictUids = new System.Collections.Generic.List<string>();
            foreach (var existing in station.foundations.Values)
            {
                if (existing.tileLayer != targetLayer) continue;
                // Two axis-aligned rectangles overlap when neither is fully outside the other.
                bool overlaps =
                    existing.tileCol < col + tileW && existing.tileCol + existing.tileWidth  > col &&
                    existing.tileRow < row + tileH && existing.tileRow + existing.tileHeight > row;
                if (overlaps) conflictUids.Add(existing.uid);
            }
            foreach (var uid in conflictUids)
                if (!CancelFoundation(station, uid, refund: false))
                    station.foundations.Remove(uid);

            // 2. Auto-place floor (layer 1) beneath any object/structural placement (layer ≥ 2).
            if (targetLayer >= 2 && _registry.Buildables.ContainsKey("buildable.floor"))
            {
                for (int dc = 0; dc < tileW; dc++)
                for (int dr = 0; dr < tileH; dr++)
                {
                    int tc = col + dc, tr = row + dr;
                    bool floorPresent = false;
                    foreach (var f in station.foundations.Values)
                        if (f.tileCol == tc && f.tileRow == tr && f.tileLayer == 1)
                        { floorPresent = true; break; }
                    if (!floorPresent)
                        PlaceFoundation(station, "buildable.floor", tc, tr, 0);
                }
            }

            foundation.tileLayer  = targetLayer;
            foundation.tileWidth  = tileW;
            foundation.tileHeight = tileH;
            station.foundations[foundation.uid] = foundation;
            station.LogEvent($"Foundation placed: {defn.displayName} at tile ({col},{row}).");
            Debug.Log($"[BuildingSystem] Foundation {foundation.uid} placed at ({col},{row}).");
            return foundation;
        }

        /// <summary>
        /// Cancel a pending foundation and optionally refund hauled materials.
        /// Returns true if the foundation was found and removed.
        /// An optional <paramref name="logMessage"/> overrides the default "Foundation cancelled: …"
        /// event-log entry — pass a non-null string to substitute a custom message.
        /// </summary>
        public bool CancelFoundation(StationState station, string foundationUid,
                                      bool refund = true, string logMessage = null)
        {
            if (!station.foundations.TryGetValue(foundationUid, out var foundation))
                return false;
            if (foundation.status == "complete")
                return false;

            _registry.Buildables.TryGetValue(foundation.buildableId, out var defn);

            // Refund any materials already hauled to the site
            if (refund && defn != null)
            {
                foreach (var kv in foundation.hauledMaterials)
                {
                    foreach (var mod in station.modules.Values)
                    {
                        if (mod.inventory != null)
                        {
                            int have = mod.inventory.ContainsKey(kv.Key) ? mod.inventory[kv.Key] : 0;
                            mod.inventory[kv.Key] = have + kv.Value;
                            break;
                        }
                    }
                }
            }

            // Release assigned engineer
            if (foundation.assignedNpcUid != null &&
                station.npcs.TryGetValue(foundation.assignedNpcUid, out var npc) &&
                npc.currentJobId == BuildJobId)
            {
                npc.currentJobId = null;
            }

            station.foundations.Remove(foundationUid);
            string name = defn != null ? defn.displayName : foundation.buildableId;
            station.LogEvent(logMessage ?? $"Foundation cancelled: {name}.");
            return true;
        }

        /// <summary>
        /// Removes a foundation as part of a Ctrl+Z undo operation.
        /// For incomplete foundations, delegates to CancelFoundation (refund: true) so
        /// any already-hauled materials are returned to a module inventory.
        /// For complete foundations the build cost is sunk, so the foundation is removed
        /// directly without a refund (bypassing the complete-guard in CancelFoundation).
        /// </summary>
        public void UndoFoundation(StationState station, string foundationUid)
        {
            if (!station.foundations.TryGetValue(foundationUid, out var foundation))
                return;

            // Incomplete: reuse CancelFoundation to refund any hauled materials.
            // Pass an undo-specific log message so the event log is consistent with
            // the "Undo: removed …" entry used for complete foundations.
            if (foundation.status != "complete")
            {
                _registry.Buildables.TryGetValue(foundation.buildableId, out var incompleteDefn);
                string incompleteName = incompleteDefn != null ? incompleteDefn.displayName : foundation.buildableId;
                CancelFoundation(station, foundationUid, refund: true,
                    logMessage: $"Undo: removed {incompleteName}.");
                return;
            }

            // Complete: bypass the complete-guard and remove directly (no refund).
            if (foundation.assignedNpcUid != null &&
                station.npcs.TryGetValue(foundation.assignedNpcUid, out var npc) &&
                npc.currentJobId == BuildJobId)
            {
                npc.currentJobId = null;
            }

            station.foundations.Remove(foundationUid);
            _registry.Buildables.TryGetValue(foundation.buildableId, out var defn);
            string name = defn != null ? defn.displayName : foundation.buildableId;
            station.LogEvent($"Undo: removed {name}.");
        }

        /// <summary>
        /// Check whether a buildable can be placed on the station right now.
        /// Validates required_tags and required_skills (at least one crew member
        /// must meet every skill requirement).
        /// Returns false and populates <paramref name="reason"/> when blocked.
        /// </summary>
        public bool CanPlace(StationState station, string buildableId, out string reason)
        {
            if (!_registry.Buildables.TryGetValue(buildableId, out var defn))
            {
                reason = "Unknown buildable.";
                return false;
            }

            // Check required station tags
            foreach (var tag in defn.requiredTags)
            {
                if (!station.HasTag(tag))
                {
                    reason = $"Station missing tag: {tag}";
                    return false;
                }
            }

            // Check required crew skills — for each skill, at least one crew
            // member must have a skill level ≥ the required level.
            foreach (var req in defn.requiredSkills)
            {
                bool met = false;
                foreach (var npc in station.GetCrew())
                {
                    if (npc.skills.TryGetValue(req.Key, out int lvl) && lvl >= req.Value)
                    {
                        met = true;
                        break;
                    }
                }
                if (!met)
                {
                    reason = $"Need crew with {req.Key} ≥ {req.Value}";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advance all active foundations by one tick.  Each engineer NPC is only
        /// assigned to one foundation per tick.
        /// </summary>
        public void Tick(StationState station)
        {
            // Collect foundations still in progress
            var pending = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
                if (f.status == "awaiting_haul" || f.status == "constructing")
                    pending.Add(f);

            // Compute the best active workbench room-bonus multiplier once per tick
            // so TickConstructing doesn't scan all foundations for every pending build.
            // Multiplied by Functionality() so damaged workbenches give reduced bonuses.
            float roomBonusMultiplier = 1f;
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || !f.hasRoomBonus || f.roomBonusMultiplier <= roomBonusMultiplier)
                    continue;
                if (_registry.Buildables.TryGetValue(f.buildableId, out var wb) && wb.workbenchRoomType != null)
                {
                    float effective = f.roomBonusMultiplier * f.Functionality();
                    if (effective > roomBonusMultiplier)
                        roomBonusMultiplier = effective;
                }
            }

            // Track which engineer UIDs have been claimed this tick.
            // An engineer already assigned to *this* foundation may still work on it.
            var usedEngineerUids = new HashSet<string>();
            var idleJobs         = new HashSet<string> { null, "job.rest", "job.eat", BuildJobId, RepairJobId };

            foreach (var foundation in pending)
            {
                if (!_registry.Buildables.TryGetValue(foundation.buildableId, out var defn))
                    continue;

                string assignedUid = foundation.assignedNpcUid;

                // Build a per-foundation pool that excludes engineers claimed by
                // other foundations this tick.
                var idle = new List<NPCInstance>();
                foreach (var npc in station.npcs.Values)
                {
                    if (!npc.IsCrew()) continue;
                    if (npc.classId != EngineerClassId) continue;
                    if (!idleJobs.Contains(npc.currentJobId)) continue;
                    if (usedEngineerUids.Contains(npc.uid) &&
                        (assignedUid == null || npc.uid != assignedUid)) continue;
                    idle.Add(npc);
                }

                if (foundation.status == "awaiting_haul")
                    TickAwaitingHaul(foundation, defn, station, idle);
                else if (foundation.status == "constructing")
                    TickConstructing(foundation, defn, station, idle, roomBonusMultiplier);

                // Mark the assigned engineer as used for the rest of this tick.
                if (foundation.assignedNpcUid != null)
                    usedEngineerUids.Add(foundation.assignedNpcUid);
            }

            // ── Repair pipeline ───────────────────────────────────────────────
            // Process complete foundations flagged for repair. Each uses a separate
            // engineer pool entry (repairAssignedNpcUid) so repairs and new builds
            // don't contend for the same worker.
            var repairJobs = new HashSet<string> { null, "job.rest", "job.eat", BuildJobId, RepairJobId };
            foreach (var foundation in station.foundations.Values)
            {
                if (foundation.status != "complete" || !foundation.pendingRepair) continue;
                if (foundation.health >= foundation.maxHealth)
                {
                    // Repair already at full HP — clear state without logging again.
                    foundation.pendingRepair         = false;
                    foundation.repairProgress        = 0f;
                    foundation.repairAssignedNpcUid  = null;
                    foundation.repairHauledMaterials.Clear();
                    continue;
                }

                if (!_registry.Buildables.TryGetValue(foundation.buildableId, out var repairDefn))
                    continue;

                string repairAssigned = foundation.repairAssignedNpcUid;
                var repairIdle = new List<NPCInstance>();
                foreach (var npc in station.npcs.Values)
                {
                    if (!npc.IsCrew()) continue;
                    if (npc.classId != EngineerClassId) continue;
                    if (!repairJobs.Contains(npc.currentJobId)) continue;
                    if (usedEngineerUids.Contains(npc.uid) &&
                        (repairAssigned == null || npc.uid != repairAssigned)) continue;
                    repairIdle.Add(npc);
                }

                bool repairMaterialsReady = MaterialsComplete(
                    foundation.repairHauledMaterials, repairDefn.requiredMaterials);

                if (!repairMaterialsReady)
                    TickRepairHaul(foundation, repairDefn, station, repairIdle);
                else
                    TickRepairing(foundation, repairDefn, station, repairIdle, roomBonusMultiplier);

                if (foundation.repairAssignedNpcUid != null)
                    usedEngineerUids.Add(foundation.repairAssignedNpcUid);
            }
        }

        // ── Public damage / repair API ────────────────────────────────────────

        /// <summary>
        /// Apply <paramref name="damage"/> HP to a complete foundation.
        /// When HP drops to 0 the building is flagged as destroyed and a repair task
        /// is queued automatically.  Has no effect on incomplete foundations.
        /// </summary>
        public void DamageFoundation(StationState station, string foundationUid, int damage)
        {
            if (!station.foundations.TryGetValue(foundationUid, out var foundation)) return;
            if (foundation.status != "complete") return;
            if (damage <= 0) return;

            foundation.health = Mathf.Max(0, foundation.health - damage);

            float func = foundation.Functionality();
            if (foundation.health == 0)
                foundation.operatingState = "broken";
            else if (func < 1f)
                foundation.operatingState = "damaged";

            if (foundation.health == 0 && !foundation.pendingRepair)
            {
                foundation.pendingRepair = true;
                foundation.repairProgress = 0f;
                foundation.repairHauledMaterials.Clear();
                _registry.Buildables.TryGetValue(foundation.buildableId, out var defn);
                string name = defn != null ? defn.displayName : foundation.buildableId;
                station.LogEvent(
                    $"{name} at ({foundation.tileCol},{foundation.tileRow}) destroyed — repair required.");
            }
        }



        // ── Private helpers ───────────────────────────────────────────────────

        private void TickAwaitingHaul(FoundationInstance foundation,
                                       BuildableDefinition defn,
                                       StationState station,
                                       List<NPCInstance> idle)
        {
            var required = defn.requiredMaterials;

            // Dev mode — skip haul entirely, jump straight to constructing.
            if (DevMode)
            {
                foundation.status = "constructing";
                return;
            }

            // No materials needed — skip straight to constructing
            if (required == null || required.Count == 0)
            {
                foundation.status = "constructing";
                return;
            }

            if (foundation.MaterialsComplete(required))
            {
                foundation.status = "constructing";
                return;
            }

            // Gather what's still missing from station cargo holds.
            // Partial haul is allowed: haul whatever stock is available right now
            // so construction can begin immediately and halt only when the materials
            // on-site ceiling is reached (see TickConstructing).
            bool hauledSomething = false;
            foreach (var kv in required)
            {
                string itemId    = kv.Key;
                int    qtyNeeded = kv.Value;
                int    already   = foundation.hauledMaterials.ContainsKey(itemId)
                                   ? foundation.hauledMaterials[itemId] : 0;
                int    still     = qtyNeeded - already;
                if (still <= 0) continue;

                // Count available stock across all modules
                int available = 0;
                foreach (var mod in station.modules.Values)
                    if (mod.inventory != null && mod.inventory.ContainsKey(itemId))
                        available += mod.inventory[itemId];

                if (available <= 0) continue;  // Nothing in stock for this item; skip

                // Haul as much as is available, up to the remaining need.
                int toHaul = Mathf.Min(available, still);

                // Deduct from cargo holds
                int remaining = toHaul;
                foreach (var mod in station.modules.Values)
                {
                    if (mod.inventory == null) continue;
                    if (!mod.inventory.ContainsKey(itemId)) continue;
                    int have = mod.inventory[itemId];
                    if (have <= 0) continue;
                    int used = Mathf.Min(have, remaining);
                    mod.inventory[itemId] -= used;
                    if (mod.inventory[itemId] == 0)
                        mod.inventory.Remove(itemId);
                    remaining -= used;
                    if (remaining <= 0) break;
                }

                foundation.hauledMaterials[itemId] = already + (toHaul - remaining);
                hauledSomething = true;
            }

            // Assign an engineer on first haul
            if (hauledSomething && idle.Count > 0 && foundation.assignedNpcUid == null)
            {
                var eng = idle[0];
                eng.currentJobId     = BuildJobId;
                eng.jobTimer         = BuildJobTimerTicks;
                foundation.assignedNpcUid = eng.uid;
                station.LogEvent($"{eng.name} hauls materials for {defn.displayName}.");
            }

            // Refresh timer to keep JobSystem from reassigning the engineer
            RefreshEngineerTimer(foundation, idle);

            // Transition to constructing once any materials are on-site.
            // TickConstructing will impose a materials-ratio ceiling on progress.
            bool anyOnSite = false;
            foreach (var kv in required)
            {
                if (foundation.hauledMaterials.ContainsKey(kv.Key) &&
                    foundation.hauledMaterials[kv.Key] > 0)
                { anyOnSite = true; break; }
            }

            if (anyOnSite)
                foundation.status = "constructing";
        }

        private void TickConstructing(FoundationInstance foundation,
                                       BuildableDefinition defn,
                                       StationState station,
                                       List<NPCInstance> idle,
                                       float roomBonusMultiplier)
        {
            // Dev mode — complete instantly, no engineer needed.
            if (DevMode)
            {
                foundation.buildProgress = 1f;
                CompleteFoundation(foundation, defn, station, null);
                return;
            }

            // Material-ceiling check: construction can only advance as far as the
            // fraction of required materials that have been hauled.  When progress
            // reaches the ceiling with materials still missing, revert to awaiting_haul
            // so more stock can be fetched before work continues.
            var required = defn.requiredMaterials;
            if (required != null && required.Count > 0)
            {
                int totalRequired = 0, totalHauled = 0;
                foreach (var kv in required)
                {
                    totalRequired += kv.Value;
                    int hauled = foundation.hauledMaterials.ContainsKey(kv.Key)
                                 ? foundation.hauledMaterials[kv.Key] : 0;
                    totalHauled += Mathf.Min(hauled, kv.Value);
                }
                float materialsRatio = totalRequired > 0
                    ? (float)totalHauled / totalRequired : 1f;

                if (materialsRatio < 1f &&
                    foundation.buildProgress >= materialsRatio - 0.0001f)
                {
                    // All hauled materials have been consumed.  Halt construction
                    // and go back to haul phase to fetch the remaining items.
                    foundation.status = "awaiting_haul";
                    station.LogEvent(
                        $"{defn.displayName} construction halted — waiting for materials.");
                    return;
                }
            }

            // Ensure an engineer is assigned
            NPCInstance assigned = null;
            if (foundation.assignedNpcUid != null)
                station.npcs.TryGetValue(foundation.assignedNpcUid, out assigned);

            if (assigned == null && idle.Count > 0)
            {
                assigned = idle[0];
                foundation.assignedNpcUid = assigned.uid;
            }

            if (assigned == null) return;  // Nobody available — wait

            // Lock the engineer onto this job and refresh timer every tick
            assigned.currentJobId = BuildJobId;
            RefreshEngineerTimer(foundation, idle);

            // Advance progress: 1/buildTimeTicks per tick, scaled by technical skill
            // and any room bonus from a qualifying workbench (pre-computed in Tick()).
            int buildTime  = defn.buildTimeTicks > 0 ? defn.buildTimeTicks : DefaultBuildTimeTicks;
            int skillLevel = assigned.skills.ContainsKey("technical") ? assigned.skills["technical"] : 5;
            float skillScale = 0.5f + skillLevel / 10f;  // 0.5× at 0, 1.5× at 10

            if (roomBonusMultiplier > 1f)
                skillScale *= roomBonusMultiplier;

            float increment  = (1f / buildTime) * skillScale;

            foundation.buildProgress = Mathf.Min(1f, foundation.buildProgress + increment);

            if (foundation.buildProgress >= 1f)
                CompleteFoundation(foundation, defn, station, assigned);
        }

        // ── Repair helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Haul repair materials from station cargo holds to the repair site.
        /// Uses the same required_materials as the original build.
        /// </summary>
        private void TickRepairHaul(FoundationInstance foundation,
                                     BuildableDefinition defn,
                                     StationState station,
                                     List<NPCInstance> idle)
        {
            if (DevMode)
            {
                // Skip haul in dev mode; restore HP directly.
                foundation.repairHauledMaterials.Clear();
                foreach (var kv in defn.requiredMaterials)
                    foundation.repairHauledMaterials[kv.Key] = kv.Value;
                return;
            }

            var required = defn.requiredMaterials;
            if (required == null || required.Count == 0) return;

            bool hauledSomething = false;
            foreach (var kv in required)
            {
                string itemId    = kv.Key;
                int    qtyNeeded = kv.Value;
                int    already   = foundation.repairHauledMaterials.ContainsKey(itemId)
                                   ? foundation.repairHauledMaterials[itemId] : 0;
                int    still     = qtyNeeded - already;
                if (still <= 0) continue;

                int available = 0;
                foreach (var mod in station.modules.Values)
                    if (mod.inventory != null && mod.inventory.ContainsKey(itemId))
                        available += mod.inventory[itemId];

                if (available <= 0) continue;

                int toHaul    = Mathf.Min(available, still);
                int remaining = toHaul;
                foreach (var mod in station.modules.Values)
                {
                    if (mod.inventory == null || !mod.inventory.ContainsKey(itemId)) continue;
                    int have = mod.inventory[itemId];
                    if (have <= 0) continue;
                    int used = Mathf.Min(have, remaining);
                    mod.inventory[itemId] -= used;
                    if (mod.inventory[itemId] == 0) mod.inventory.Remove(itemId);
                    remaining -= used;
                    if (remaining <= 0) break;
                }

                foundation.repairHauledMaterials[itemId] = already + (toHaul - remaining);
                hauledSomething = true;
            }

            if (hauledSomething && idle.Count > 0 && foundation.repairAssignedNpcUid == null)
            {
                var eng = idle[0];
                eng.currentJobId                = RepairJobId;
                eng.jobTimer                    = BuildJobTimerTicks;
                foundation.repairAssignedNpcUid = eng.uid;
                station.LogEvent($"{eng.name} hauls materials to repair {defn.displayName}.");
            }

            RefreshRepairEngineerTimer(foundation, idle);
        }

        /// <summary>
        /// Engineer repairs the foundation, advancing repairProgress toward 1.0
        /// and restoring HP proportionally.
        /// </summary>
        private void TickRepairing(FoundationInstance foundation,
                                    BuildableDefinition defn,
                                    StationState station,
                                    List<NPCInstance> idle,
                                    float roomBonusMultiplier)
        {
            if (DevMode)
            {
                foundation.health            = foundation.maxHealth;
                foundation.repairProgress    = 1f;
                foundation.pendingRepair     = false;
                foundation.repairAssignedNpcUid = null;
                foundation.repairHauledMaterials.Clear();
                foundation.operatingState    = "standby";
                _registry.Buildables.TryGetValue(foundation.buildableId, out var dv);
                station.LogEvent($"{(dv != null ? dv.displayName : foundation.buildableId)} repair complete.");
                return;
            }

            NPCInstance assigned = null;
            if (foundation.repairAssignedNpcUid != null)
                station.npcs.TryGetValue(foundation.repairAssignedNpcUid, out assigned);

            if (assigned == null && idle.Count > 0)
            {
                assigned = idle[0];
                foundation.repairAssignedNpcUid = assigned.uid;
            }

            if (assigned == null) return;

            assigned.currentJobId = RepairJobId;
            RefreshRepairEngineerTimer(foundation, idle);

            int buildTime  = defn.buildTimeTicks > 0 ? defn.buildTimeTicks : DefaultBuildTimeTicks;
            int skillLevel = assigned.skills.ContainsKey("technical") ? assigned.skills["technical"] : 5;
            float skillScale = 0.5f + skillLevel / 10f;
            if (roomBonusMultiplier > 1f) skillScale *= roomBonusMultiplier;

            float increment = (1f / buildTime) * skillScale;
            foundation.repairProgress = Mathf.Min(1f, foundation.repairProgress + increment);

            // Restore HP proportionally as repair advances
            foundation.health = Mathf.RoundToInt(foundation.maxHealth * foundation.repairProgress);

            if (foundation.repairProgress >= 1f)
            {
                foundation.health            = foundation.maxHealth;
                foundation.pendingRepair     = false;
                foundation.repairProgress    = 0f;
                foundation.operatingState    = "standby";
                foundation.repairAssignedNpcUid = null;
                foundation.repairHauledMaterials.Clear();

                if (assigned != null) assigned.currentJobId = null;

                _registry.Buildables.TryGetValue(foundation.buildableId, out var defnR);
                string name = defnR != null ? defnR.displayName : foundation.buildableId;
                station.LogEvent(
                    $"{name} repair complete at ({foundation.tileCol},{foundation.tileRow}).");
                if (defnR?.workbenchRoomType != null) RoomRebuildNeeded = true;
            }
        }

        private static bool MaterialsComplete(Dictionary<string, int> hauled,
                                               Dictionary<string, int> required)
        {
            if (required == null || required.Count == 0) return true;
            foreach (var kv in required)
            {
                int have = hauled != null && hauled.ContainsKey(kv.Key) ? hauled[kv.Key] : 0;
                if (have < kv.Value) return false;
            }
            return true;
        }

        /// <summary>
        /// Set job_timer = BuildJobTimerTicks on the assigned engineer (if present in
        /// the idle pool) so JobSystem does not reassign them mid-construction.
        /// </summary>
        private static void RefreshEngineerTimer(FoundationInstance foundation,
                                                  List<NPCInstance> idle)
        {
            if (foundation.assignedNpcUid == null) return;
            foreach (var npc in idle)
            {
                if (npc.uid == foundation.assignedNpcUid)
                {
                    npc.jobTimer = BuildJobTimerTicks;
                    return;
                }
            }
        }

        /// <summary>
        /// Set job_timer = BuildJobTimerTicks on the assigned repair engineer (if present
        /// in the idle pool) so JobSystem does not reassign them mid-repair.
        /// </summary>
        private static void RefreshRepairEngineerTimer(FoundationInstance foundation,
                                                        List<NPCInstance> idle)
        {
            if (foundation.repairAssignedNpcUid == null) return;
            foreach (var npc in idle)
            {
                if (npc.uid == foundation.repairAssignedNpcUid)
                {
                    npc.jobTimer = BuildJobTimerTicks;
                    return;
                }
            }
        }

        private void CompleteFoundation(FoundationInstance foundation,
                                        BuildableDefinition defn,
                                        StationState station,
                                        NPCInstance npc)
        {
            foundation.status        = "complete";
            foundation.buildProgress = 1f;

            // Release the engineer
            if (npc != null)
            {
                npc.currentJobId          = null;
                foundation.assignedNpcUid = null;
            }

            // Signal that utility networks need a rebuild so this tile joins its network.
            if (defn.networkType != null)
                NetworkRebuildNeeded = true;

            // Signal a room bonus cache rebuild when a workbench or structural element
            // (category=="structure", tileLayer==1) completes — these are the layout
            // changes that affect room connectivity. Using defn.category rather than
            // a substring match avoids false positives like "buildable.wall_art".
            if (defn.workbenchRoomType != null ||
                (defn.category == "structure" && defn.tileLayer == 1 &&
                 (foundation.buildableId.Contains("wall") ||
                  foundation.buildableId.Contains("door"))))
                RoomRebuildNeeded = true;

            station.LogEvent(
                $"{defn.displayName} construction complete at " +
                $"({foundation.tileCol},{foundation.tileRow}) " +
                $"(quality: {foundation.quality:F2}).");
            Debug.Log(
                $"[BuildingSystem] Foundation {foundation.uid} ({defn.displayName}) " +
                $"completed at ({foundation.tileCol},{foundation.tileRow}) " +
                $"by {(npc != null ? npc.name : "unknown")}.");
        }
    }
}
