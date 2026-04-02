// ShipSystem — manages the player's fleet of owned ships (EXP-003).
//
// Responsibilities:
//   • Ship acquisition: AddShipToFleet (purchase/construction pipelines call this)
//   • Crew assignment: AssignCrew / UnassignCrew; capacity gated by ShipTemplate.crewCapacity
//   • Mission dispatch: DispatchShipMission validates role eligibility and crew presence,
//     sets npc.missionUid on crew so NeedSystem continues depleting their needs during travel
//   • Per-tick resolution: Tick() resolves completed fleet missions and notifies callers
//   • Damage model: ApplyDamage / RepairShip update conditionPct and recalculate ShipDamageState
//   • Destruction: ResolveDestruction rolls each crew NPC as killed / captured / escaped and
//     removes the ship from the fleet
//
// Role eligibility is data-driven via ShipTemplate.eligibleMissionTypes.
// The five canonical roles (scout, mining, combat, transport, diplomatic) and their
// eligible mission type strings are defined in the ship data files.
//
// NPC crew simulation:
//   NPCs assigned to a fleet ship have npc.assignedShipUid set.
//   When the ship is dispatched, npc.missionUid is also set (a fleet-mission uid).
//   NeedSystem checks both fields: if assignedShipUid is set the NPC is on a fleet
//   mission and needs continue depleting (with seeking suppressed, since station
//   facilities are unavailable during travel).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    /// <summary>
    /// Outcome of a crew-member resolution when their ship is destroyed.
    /// </summary>
    public enum CrewDestructionOutcome
    {
        Killed,
        Captured,
        Escaped
    }

    /// <summary>
    /// Per-crew-member result from <see cref="ShipSystem.ResolveDestruction"/>.
    /// </summary>
    public class CrewDestructionResult
    {
        public string                npcUid;
        public CrewDestructionOutcome outcome;
    }

    public class ShipSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// Fleet-mission uid prefix. Combined with a short guid to form a unique mission key.
        /// Stored in npc.missionUid while the NPC is on a fleet mission.
        /// </summary>
        private const string FleetMissionPrefix = "fleet_";

        // Damage-state thresholds (conditionPct ranges)
        public const float DamageThresholdUndamaged = 100f;
        public const float DamageThresholdLight     =  75f;
        public const float DamageThresholdModerate  =  50f;
        public const float DamageThresholdHeavy     =  25f;
        public const float DamageThresholdCritical  =   1f;

        // Crew destruction outcome probabilities (per damage tier, in order: killed, captured, escaped).
        // Sum of probabilities for a tier must be 1.0.
        private static readonly Dictionary<ShipDamageState, (float killed, float captured, float escaped)>
            CrewOutcomeProbabilities = new Dictionary<ShipDamageState, (float, float, float)>
        {
            { ShipDamageState.Undamaged, (0.10f, 0.20f, 0.70f) },
            { ShipDamageState.Light,     (0.15f, 0.25f, 0.60f) },
            { ShipDamageState.Moderate,  (0.25f, 0.35f, 0.40f) },
            { ShipDamageState.Heavy,     (0.40f, 0.35f, 0.25f) },
            { ShipDamageState.Critical,  (0.55f, 0.30f, 0.15f) },
            { ShipDamageState.Destroyed, (0.60f, 0.25f, 0.15f) },
        };

        // ── Dependencies ──────────────────────────────────────────────────────
        private readonly ContentRegistry _registry;

        public ShipSystem(ContentRegistry registry) => _registry = registry;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Add a ship to the player's fleet (called after purchase or construction).
        /// Returns the created OwnedShipInstance, or null on failure (unknown templateId).
        /// </summary>
        public OwnedShipInstance AddShipToFleet(string templateId, string name, StationState station)
        {
            if (!_registry.Ships.TryGetValue(templateId, out var template))
            {
                Debug.LogWarning($"[ShipSystem] AddShipToFleet: unknown templateId '{templateId}'.");
                return null;
            }

            var ship = OwnedShipInstance.Create(templateId, name, template.role);
            station.ownedShips[ship.uid] = ship;
            station.LogEvent($"Ship '{name}' ({template.role}) added to fleet.");
            return ship;
        }

        /// <summary>
        /// Remove a ship from the fleet (player action — e.g. manual deletion).
        /// Clears both <c>assignedShipUid</c> and <c>missionUid</c> on all crew NPCs so
        /// they are no longer treated as being on either a fleet or regular mission.
        /// Does not perform crew-outcome resolution; for in-combat destruction use
        /// <see cref="ResolveDestruction"/> instead.
        /// </summary>
        public void RemoveShipFromFleet(string shipUid, StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return;

            foreach (var crewUid in ship.crewUids)
            {
                if (station.npcs.TryGetValue(crewUid, out var npc))
                {
                    npc.assignedShipUid = null;
                    npc.missionUid      = null;
                }
            }

            station.ownedShips.Remove(shipUid);
        }

        /// <summary>
        /// Assign a list of crew NPCs to a ship.
        /// Returns (ok=true) on success; (ok=false, reason) on validation failure.
        /// The list must be non-null and contain no duplicate UIDs.
        /// If an NPC is currently assigned to a different ship, they are removed from
        /// that ship's crew list before being assigned here.
        /// </summary>
        public (bool ok, string reason) AssignCrew(string shipUid, List<string> crewUids,
                                                    StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship))
                return (false, $"Ship '{shipUid}' not found in fleet.");

            if (crewUids == null)
                return (false, "crewUids list must not be null.");

            // Reject duplicates
            var seen = new HashSet<string>();
            foreach (var uid in crewUids)
            {
                if (!seen.Add(uid))
                    return (false, $"Duplicate crew UID '{uid}' in assignment list.");
            }

            _registry.Ships.TryGetValue(ship.templateId, out var template);
            int capacity = template?.crewCapacity ?? int.MaxValue;

            if (crewUids.Count > capacity)
                return (false, $"Ship capacity is {capacity}; {crewUids.Count} crew requested.");

            // Validate each NPC
            foreach (var uid in crewUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc))
                    return (false, $"NPC '{uid}' not found.");
                if (!npc.IsCrew())
                    return (false, $"{npc.name} is not crew.");
                if (npc.missionUid != null)
                    return (false, $"{npc.name} is on an active mission and cannot be reassigned.");
            }

            // Clear previous assignments on this ship's current crew
            foreach (var prevUid in ship.crewUids)
            {
                if (station.npcs.TryGetValue(prevUid, out var prevNpc) &&
                    prevNpc.assignedShipUid == shipUid)
                    prevNpc.assignedShipUid = null;
            }

            // Apply new assignments
            ship.crewUids.Clear();
            foreach (var uid in crewUids)
            {
                ship.crewUids.Add(uid);
                if (station.npcs.TryGetValue(uid, out var npc))
                {
                    // If this NPC was previously assigned to a different ship,
                    // remove them from that ship's crew list to keep fleet state consistent.
                    if (!string.IsNullOrEmpty(npc.assignedShipUid) && npc.assignedShipUid != shipUid)
                    {
                        if (station.ownedShips.TryGetValue(npc.assignedShipUid, out var prevShip))
                            prevShip.crewUids.Remove(uid);
                    }

                    npc.assignedShipUid = shipUid;
                }
            }

            return (true, null);
        }

        /// <summary>
        /// Remove all crew from a ship and clear their assignedShipUid.
        /// </summary>
        public void UnassignCrew(string shipUid, StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return;

            foreach (var uid in ship.crewUids)
            {
                if (station.npcs.TryGetValue(uid, out var npc) &&
                    npc.assignedShipUid == shipUid)
                    npc.assignedShipUid = null;
            }

            ship.crewUids.Clear();
        }

        /// <summary>
        /// Check whether the ship is eligible for the given mission type.
        /// Role eligibility is data-driven from ShipTemplate.eligibleMissionTypes.
        /// </summary>
        public bool IsEligibleForMission(string shipUid, string missionType, StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return false;
            if (!_registry.Ships.TryGetValue(ship.templateId, out var template)) return false;
            return template.eligibleMissionTypes.Contains(missionType);
        }

        /// <summary>
        /// Validate and dispatch a fleet ship on a mission.
        /// On success, crew NPCs have missionUid set (needs continue depleting during travel).
        /// Returns (ok, reason, missionUid).
        /// </summary>
        public (bool ok, string reason, string missionUid) DispatchShipMission(
            string shipUid, string missionType, int durationTicks, StationState station)
        {
            if (!CanDispatch(shipUid, missionType, station, out string reason))
                return (false, reason, null);

            var ship     = station.ownedShips[shipUid];
            string muid  = FleetMissionPrefix + Guid.NewGuid().ToString("N").Substring(0, 8);

            ship.status          = "on_mission";
            ship.missionUid      = muid;
            ship.missionType     = missionType;
            ship.missionStartTick = station.tick;
            ship.missionEndTick   = station.tick + durationTicks;

            // Lock crew — set missionUid so NeedSystem continues simulation for them
            foreach (var crewUid in ship.crewUids)
            {
                if (station.npcs.TryGetValue(crewUid, out var npc))
                    npc.missionUid = muid;
            }

            station.LogEvent(
                $"Fleet ship '{ship.name}' dispatched on {missionType} mission ({ship.crewUids.Count} crew).");
            return (true, null, muid);
        }

        /// <summary>
        /// Validate whether a ship can be dispatched on a mission.
        /// Returns false and populates <paramref name="reason"/> on failure.
        /// Validates per-crew readiness: each crew UID must resolve to a valid, non-on-mission
        /// crew NPC still assigned to this ship. Invalid UIDs are pruned from the list.
        /// </summary>
        public bool CanDispatch(string shipUid, string missionType, StationState station,
                                 out string reason)
        {
            reason = null;

            if (!station.ownedShips.TryGetValue(shipUid, out var ship))
            {
                reason = $"Ship '{shipUid}' not found in fleet.";
                return false;
            }

            if (ship.status != "docked")
            {
                reason = $"Ship '{ship.name}' is not docked (status: {ship.status}).";
                return false;
            }

            if (ship.damageState == ShipDamageState.Critical ||
                ship.damageState == ShipDamageState.Destroyed)
            {
                reason = $"Ship '{ship.name}' is too damaged to dispatch ({ship.ConditionLabel()}).";
                return false;
            }

            // Prune stale crew UIDs (NPCs that no longer exist or are no longer assigned here),
            // then validate remaining crew are ready to depart.
            ship.crewUids.RemoveAll(uid =>
                !station.npcs.TryGetValue(uid, out var n) || n.assignedShipUid != shipUid);

            if (ship.crewUids.Count == 0)
            {
                reason = $"Ship '{ship.name}' has no crew assigned.";
                return false;
            }

            foreach (var uid in ship.crewUids)
            {
                if (!station.npcs.TryGetValue(uid, out var npc)) continue; // already pruned above
                if (!npc.IsCrew())
                {
                    reason = $"{npc.name} is no longer crew and cannot be dispatched.";
                    return false;
                }
                if (npc.missionUid != null)
                {
                    reason = $"{npc.name} is already on a mission and cannot be dispatched.";
                    return false;
                }
            }

            if (!IsEligibleForMission(shipUid, missionType, station))
            {
                reason = $"Ship '{ship.name}' (role: {ship.role}) is not eligible for '{missionType}' missions.";
                return false;
            }

            return true;
        }

        // ── UI-020 additions ──────────────────────────────────────────────────

        /// <summary>
        /// Fired whenever the fleet list or any owned ship's state changes.
        /// WaystationHUDController subscribes to this to refresh the Fleet panel
        /// without waiting for the next game tick.
        /// </summary>
        public event Action OnFleetChanged;

        /// <summary>
        /// Returns all owned ships as a read-only list, sorted by name.
        /// Returns an empty list when <paramref name="station"/> is null.
        /// </summary>
        public IReadOnlyList<OwnedShipInstance> GetOwnedShips(StationState station)
        {
            if (station == null) return new List<OwnedShipInstance>();

            var ships = new List<OwnedShipInstance>(station.ownedShips.Values);
            ships.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return ships;
        }

        // ── UI-021 additions ──────────────────────────────────────────────────

        /// <summary>
        /// Returns all ship blueprints available for construction.
        /// A blueprint is included when the template has <c>buildTimeTicks &gt; 0</c>
        /// (i.e. it is buildable) and it is fleet-only.
        /// The <paramref name="station"/> is used to check research prerequisites;
        /// pass null to skip the research gate (all blueprints returned as unlocked).
        /// Each entry indicates whether the blueprint is currently buildable or locked.
        /// </summary>
        public IReadOnlyList<(ShipTemplate template, bool locked)> GetAvailableBlueprints(
            StationState station)
        {
            var result = new List<(ShipTemplate, bool)>();
            if (_registry == null) return result;
            foreach (var template in _registry.Ships.Values)
            {
                if (!template.fleetOnly) continue;
                if (template.buildTimeTicks <= 0) continue;

                // When station is null, skip research gating entirely (no blueprints locked).
                bool locked;
                if (station == null)
                {
                    locked = false;
                }
                else
                {
                    locked = !string.IsNullOrEmpty(template.researchPrereq)
                             && !station.HasTag(template.researchPrereq);
                }
                result.Add((template, locked));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Item1.role, b.Item1.role));
            return result;
        }

        /// <summary>
        /// Begin construction of a ship from a blueprint.
        /// Validates that the blueprint exists and is not research-locked.
        /// Deducts build materials from station resources (best-effort; marks
        /// <see cref="ShipConstruction.materialsReady"/> false when any material
        /// is insufficient).
        /// On success, adds a <see cref="ShipConstruction"/> to
        /// <c>station.shipConstructions</c> and fires <see cref="OnFleetChanged"/>.
        /// Returns <c>(ok, reason, construction)</c>.
        /// </summary>
        public (bool ok, string reason, ShipConstruction construction) BeginConstruction(
            string templateId, string shipName, StationState station)
        {
            if (!FeatureFlags.FleetManagement)
                return (false, "Fleet management is disabled.", null);

            if (station == null)
                return (false, "Station is null.", null);

            if (string.IsNullOrEmpty(templateId))
                return (false, "Template ID must not be null or empty.", null);

            if (_registry == null)
                return (false, "Content registry is unavailable.", null);

            if (!_registry.Ships.TryGetValue(templateId, out var template))
                return (false, $"Unknown ship template '{templateId}'.", null);

            if (template.buildTimeTicks <= 0)
                return (false, $"Ship template '{templateId}' is not buildable.", null);

            if (!string.IsNullOrEmpty(template.researchPrereq) &&
                !station.HasTag(template.researchPrereq))
                return (false,
                    $"Research prerequisite '{template.researchPrereq}' not met.", null);

            // Pre-check whether all required materials are available.
            bool materialsReady = true;
            foreach (var kv in template.buildMaterials)
            {
                if (!station.resources.TryGetValue(kv.Key, out float available) ||
                    available < kv.Value)
                {
                    materialsReady = false;
                    break;
                }
            }

            // Deduct available materials (best-effort: deduct what is present).
            foreach (var kv in template.buildMaterials)
            {
                string resource = kv.Key;
                float  required = kv.Value;
                if (station.resources.TryGetValue(resource, out float available))
                    station.resources[resource] = UnityEngine.Mathf.Max(0f, available - required);
            }

            string name = string.IsNullOrWhiteSpace(shipName) ? template.id : shipName;
            var construction = ShipConstruction.Create(
                templateId, name,
                station.tick, station.tick + template.buildTimeTicks,
                materialsReady);

            station.shipConstructions[construction.uid] = construction;
            station.LogEvent(
                $"Construction of '{name}' ({template.role}) started " +
                $"({template.buildTimeTicks} ticks).");

            OnFleetChanged?.Invoke();
            return (true, null, construction);
        }

        /// <summary>
        /// Calculates the repair cost for a ship based on its current condition.
        /// Returns (partsCost, estimatedTicks).
        /// One part is required per 10 % of hull damage; 5 ticks per 1 % of hull damage.
        /// Returns (0, 0) when the ship is null or undamaged.
        /// </summary>
        public static (int partsCost, int estimatedTicks) GetRepairCost(OwnedShipInstance ship)
        {
            if (ship == null || ship.conditionPct >= DamageThresholdUndamaged)
                return (0, 0);

            float damage  = 100f - ship.conditionPct;
            int   parts   = Mathf.CeilToInt(damage / 10f);
            int   ticks   = Mathf.CeilToInt(damage * 5f);
            return (parts, ticks);
        }

        /// <summary>
        /// Begins a repair job on a damaged ship, setting its status to "repairing"
        /// and firing <see cref="OnFleetChanged"/>.
        /// Returns false (with a reason string) if the repair cannot start.
        /// </summary>
        public bool BeginRepair(string shipUid, StationState station, out string reason)
        {
            reason = null;

            if (station == null)
            {
                reason = "Station is null.";
                return false;
            }

            if (string.IsNullOrEmpty(shipUid))
            {
                reason = "Ship UID must not be null or empty.";
                return false;
            }

            if (!station.ownedShips.TryGetValue(shipUid, out var ship))
            {
                reason = $"Ship '{shipUid}' not found in fleet.";
                return false;
            }

            if (ship.damageState == ShipDamageState.Destroyed)
            {
                reason = $"Ship '{ship.name}' is destroyed and cannot be repaired.";
                return false;
            }

            if (ship.conditionPct >= DamageThresholdUndamaged)
            {
                reason = $"Ship '{ship.name}' is not damaged.";
                return false;
            }

            if (ship.status != "docked")
            {
                reason = $"Ship '{ship.name}' must be docked to begin repairs (current status: {ship.status}).";
                return false;
            }

            ship.status = "repairing";
            station.LogEvent($"Repair job started on '{ship.name}' — condition: {ship.conditionPct:F0}%.");
            OnFleetChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Per-tick update: resolve fleet missions that have completed,
        /// and update construction progress.
        /// </summary>
        public void Tick(StationState station)
        {
            if (!FeatureFlags.FleetManagement) return;

            foreach (var ship in station.ownedShips.Values)
            {
                if (ship.status == "on_mission" &&
                    ship.missionUid != null &&
                    station.tick >= ship.missionEndTick)
                {
                    ResolveMission(ship, station);
                }
            }

            // Update construction progress and finalise completed builds.
            var completedConstructions = new List<string>();
            foreach (var kv in station.shipConstructions)
            {
                var c = kv.Value;
                int span = c.endTick - c.startTick;
                c.progressFraction = span <= 0 ? 1f
                    : Mathf.Clamp01((float)(station.tick - c.startTick) / span);

                if (station.tick >= c.endTick)
                    completedConstructions.Add(kv.Key);
            }

            foreach (var uid in completedConstructions)
            {
                var c = station.shipConstructions[uid];
                station.shipConstructions.Remove(uid);
                AddShipToFleet(c.templateId, c.shipName, station);
            }
        }

        /// <summary>
        /// Apply damage to a ship, reducing conditionPct and updating damageState.
        /// </summary>
        public void ApplyDamage(string shipUid, float amount, StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return;
            ship.conditionPct = Mathf.Clamp(ship.conditionPct - amount, 0f, 100f);
            ship.damageState  = ComputeDamageState(ship.conditionPct);

            if (ship.damageState == ShipDamageState.Destroyed)
            {
                ship.status = "destroyed";
                station.LogEvent($"Ship '{ship.name}' has been destroyed!");
            }
            else
            {
                station.LogEvent(
                    $"Ship '{ship.name}' damaged — condition: {ship.conditionPct:F0}% ({ship.ConditionLabel()}).");
            }
        }

        /// <summary>
        /// Repair a ship, increasing conditionPct and updating damageState.
        /// </summary>
        public void RepairShip(string shipUid, float amount, StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return;
            if (ship.damageState == ShipDamageState.Destroyed) return;

            ship.conditionPct = Mathf.Clamp(ship.conditionPct + amount, 0f, 100f);
            ship.damageState  = ComputeDamageState(ship.conditionPct);

            if (ship.conditionPct >= DamageThresholdUndamaged)
            {
                ship.status = "docked";
                station.LogEvent($"Ship '{ship.name}' fully repaired.");
            }
            else
            {
                station.LogEvent(
                    $"Ship '{ship.name}' repaired — condition: {ship.conditionPct:F0}% ({ship.ConditionLabel()}).");
            }
        }

        /// <summary>
        /// Resolve ship destruction: roll crew outcomes and remove the ship from the fleet.
        /// Returns the list of per-crew results for the caller to act on.
        /// Crew NPCs killed are marked dead. Escaped NPCs return to the station.
        /// Captured NPCs have missionUid cleared but remain as crew (at another location).
        /// </summary>
        public List<CrewDestructionResult> ResolveDestruction(string shipUid, StationState station)
        {
            var results = new List<CrewDestructionResult>();

            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return results;

            // Use the ship's current damage state to weight outcome probabilities
            var (killedProb, capturedProb, _) = CrewOutcomeProbabilities.TryGetValue(
                ship.damageState, out var probs)
                ? probs
                : (0.33f, 0.33f, 0.34f);

            foreach (var crewUid in new List<string>(ship.crewUids))
            {
                if (!station.npcs.TryGetValue(crewUid, out var npc))
                {
                    results.Add(new CrewDestructionResult { npcUid = crewUid, outcome = CrewDestructionOutcome.Killed });
                    continue;
                }

                float roll    = UnityEngine.Random.value;
                CrewDestructionOutcome outcome;

                if (roll < killedProb)
                    outcome = CrewDestructionOutcome.Killed;
                else if (roll < killedProb + capturedProb)
                    outcome = CrewDestructionOutcome.Captured;
                else
                    outcome = CrewDestructionOutcome.Escaped;

                results.Add(new CrewDestructionResult { npcUid = crewUid, outcome = outcome });

                // Apply outcome effects
                npc.missionUid    = null;
                npc.assignedShipUid = null;

                switch (outcome)
                {
                    case CrewDestructionOutcome.Killed:
                        npc.statusTags.Add("dead");
                        station.LogEvent($"{npc.name} was killed when '{ship.name}' was destroyed.");
                        break;

                    case CrewDestructionOutcome.Captured:
                        // Captured NPCs are flagged; a future diplomatic mission can recover them.
                        npc.statusTags.Add("captured");
                        station.LogEvent($"{npc.name} was captured after '{ship.name}' was destroyed.");
                        break;

                    case CrewDestructionOutcome.Escaped:
                        // Escaped NPCs return to station (missionUid already cleared above)
                        station.LogEvent($"{npc.name} escaped when '{ship.name}' was destroyed.");
                        break;
                }
            }

            ship.crewUids.Clear();
            ship.status = "destroyed";
            station.ownedShips.Remove(shipUid);
            station.LogEvent($"Ship '{ship.name}' removed from fleet.");

            return results;
        }

        // ── Static helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Compute the ShipDamageState from a conditionPct value.
        /// </summary>
        public static ShipDamageState ComputeDamageState(float conditionPct)
        {
            if (conditionPct <= 0f)                          return ShipDamageState.Destroyed;
            if (conditionPct <  DamageThresholdCritical)     return ShipDamageState.Destroyed; // below 1 %
            if (conditionPct <  DamageThresholdHeavy)        return ShipDamageState.Critical;  // 1–24 %
            if (conditionPct <  DamageThresholdModerate)     return ShipDamageState.Heavy;     // 25–49 %
            if (conditionPct <  DamageThresholdLight)        return ShipDamageState.Moderate;  // 50–74 %
            if (conditionPct <  DamageThresholdUndamaged)    return ShipDamageState.Light;     // 75–99 %
            return ShipDamageState.Undamaged;                                                  // 100 %
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ResolveMission(OwnedShipInstance ship, StationState station)
        {
            string missionType = ship.missionType ?? "unknown";

            // Unlock crew — they return to the station
            foreach (var crewUid in ship.crewUids)
            {
                if (station.npcs.TryGetValue(crewUid, out var npc))
                    npc.missionUid = null;
            }

            ship.status    = "docked";
            ship.missionUid = null;
            ship.missionType = null;

            station.LogEvent(
                $"Fleet ship '{ship.name}' returned from {missionType} mission.");
        }
    }
}
