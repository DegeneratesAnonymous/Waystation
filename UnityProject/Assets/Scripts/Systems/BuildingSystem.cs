// Building System — manages build foundations and Engineer NPC construction tasks.
//
// Workflow:
//   1. Player calls PlaceFoundation() to create a FoundationInstance on the station.
//   2. Tick() detects "awaiting_haul" foundations; an idle Engineer NPC hauls the
//      required materials from station cargo holds to the build site.
//   3. Once all materials are present the foundation transitions to "constructing"
//      and the engineer spends buildTimeTicks advancing buildProgress to 1.0.
//   4. On completion the foundation is marked "complete" and the engineer is freed.
//
// Health & functionality rules (applied once built — see FoundationInstance.Functionality):
//   100–75 % HP  →  1.0 (full function)
//   75–50 % HP   →  linearly degraded
//   < 50 % HP    →  0.0 (non-functional; still pulls power if applicable)
//   0 % HP       →  0.0 (destroyed)
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
        /// When true, foundations skip the haul phase and complete instantly
        /// without consuming any materials.  Toggled via the in-game Dev Tools button.
        /// </summary>
        public static bool DevMode = false;

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
        /// </summary>
        public bool CancelFoundation(StationState station, string foundationUid,
                                      bool refund = true)
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
            station.LogEvent($"Foundation cancelled: {name}.");
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
            if (foundation.status != "complete")
            {
                CancelFoundation(station, foundationUid, refund: true);
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

            if (pending.Count == 0) return;

            // Compute the best active workbench room-bonus multiplier once per tick
            // so TickConstructing doesn't scan all foundations for every pending build.
            float roomBonusMultiplier = 1f;
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete" || !f.hasRoomBonus || f.roomBonusMultiplier <= roomBonusMultiplier)
                    continue;
                if (_registry.Buildables.TryGetValue(f.buildableId, out var wb) && wb.workbenchRoomType != null)
                    roomBonusMultiplier = f.roomBonusMultiplier;
            }

            // Track which engineer UIDs have been claimed this tick.
            // An engineer already assigned to *this* foundation may still work on it.
            var usedEngineerUids = new HashSet<string>();
            var idleJobs         = new HashSet<string> { null, "job.rest", "job.eat", BuildJobId };

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

            // Gather what's still missing from station cargo holds
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

                if (available < still) continue;  // Not enough stock; wait

                // Deduct from cargo holds
                int remaining = still;
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

                foundation.hauledMaterials[itemId] = qtyNeeded;
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

            if (foundation.MaterialsComplete(required))
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

        private static void CompleteFoundation(FoundationInstance foundation,
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
