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
        /// Remove a ship from the fleet (also used internally on destruction).
        /// Clears assignedShipUid on all crew NPCs.
        /// </summary>
        public void RemoveShipFromFleet(string shipUid, StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship)) return;

            foreach (var crewUid in ship.crewUids)
            {
                if (station.npcs.TryGetValue(crewUid, out var npc))
                    npc.assignedShipUid = null;
            }

            station.ownedShips.Remove(shipUid);
        }

        /// <summary>
        /// Assign a list of crew NPCs to a ship.
        /// Returns (ok=true) on success; (ok=false, reason) on validation failure.
        /// </summary>
        public (bool ok, string reason) AssignCrew(string shipUid, List<string> crewUids,
                                                    StationState station)
        {
            if (!station.ownedShips.TryGetValue(shipUid, out var ship))
                return (false, $"Ship '{shipUid}' not found in fleet.");

            _registry.Ships.TryGetValue(ship.templateId, out var template);
            int capacity = template?.crewCapacity ?? int.MaxValue;

            // Count existing crew not in the new list (we're re-assigning, not adding)
            int totalAfter = crewUids?.Count ?? 0;
            if (totalAfter > capacity)
                return (false, $"Ship capacity is {capacity}; {totalAfter} crew requested.");

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

            // Clear previous assignments on this ship's crew
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
                    npc.assignedShipUid = shipUid;
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

            if (ship.crewUids.Count == 0)
            {
                reason = $"Ship '{ship.name}' has no crew assigned.";
                return false;
            }

            if (!IsEligibleForMission(shipUid, missionType, station))
            {
                reason = $"Ship '{ship.name}' (role: {ship.role}) is not eligible for '{missionType}' missions.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Per-tick update: resolve fleet missions that have completed.
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
