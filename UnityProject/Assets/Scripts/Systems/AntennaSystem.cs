// Antenna System — manages ship detection, antenna stat calculation, and ship
// lifecycle around the player's station.
//
// Antenna base stats: range = 500, maxShips = 3.
// Each AstrometricsLab on station adds +150 range and +2 max ships.
// If no powered Antenna exists the Communications Menu shows "No Antenna installed".
//
// Ship spawn / drift:
//   - On each tick, up to MaxVisibleShips ships are maintained in InRange/Passing state.
//   - Ships drift on a patrol path while in range.
//   - If the Antenna loses power, in-range ships gradually drift away (not instant vanish).
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class AntennaSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        public const float BaseRange    = 500f;
        public const int   BaseMaxShips = 3;
        public const float LabRangeBonus   = 150f;
        public const int   LabShipBonus    =   2;

        // Simulated world bounds for ship patrol positions
        private const float WorldHalfExtent = 600f;

        // Ticks between spawning a new in-range ship (when below cap)
        private const int SpawnIntervalTicks = 8;

        // Drift speed (world units per tick)
        private const float DriftSpeed = 4f;

        // Ticks before an out-of-power ship fully drifts away
        private const int PowerLossDriftTicks = 30;

        private readonly ContentRegistry _registry;
        private readonly NPCSystem       _npcSystem;

        public AntennaSystem(ContentRegistry registry, NPCSystem npcSystem)
        {
            _registry  = registry;
            _npcSystem = npcSystem;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Calculate current antenna stats based on powered Antenna foundations and
        /// the number of AstrometricsLab foundations on the station.
        /// Returns (0, 0) if no powered Antenna exists.
        /// </summary>
        public (float range, int maxShips) GetAntennaStats(StationState station)
        {
            if (!HasPoweredAntenna(station)) return (0f, 0);

            int labCount  = station.GetBuildableCount("buildable.astrometrics_lab");
            float range   = BaseRange   + labCount * LabRangeBonus;
            int   maxShips= BaseMaxShips + labCount * LabShipBonus;
            return (range, maxShips);
        }

        /// <summary>True if at least one complete, powered Antenna foundation exists.</summary>
        public bool HasPoweredAntenna(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId == "buildable.antenna" &&
                    f.status      == "complete"          &&
                    f.Functionality() > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Main tick: spawn and manage in-range ships.
        /// </summary>
        public void Tick(StationState station)
        {
            var (range, maxShips) = GetAntennaStats(station);

            if (range <= 0f)
            {
                // Antenna off — let all currently-tracked ships drift away
                DriftShipsAway(station);
                return;
            }

            // Spawn new ships if below cap
            if (station.tick % SpawnIntervalTicks == 0)
            {
                int current = CountInRangeShips(station);
                if (current < maxShips)
                    TrySpawnShip(station);
            }

            // Tick drift for each in-range ship
            foreach (var ship in new List<ShipInstance>(station.ships.Values))
            {
                if (ship.visitState == ShipVisitState.OutOfRange) continue;
                TickShipDrift(ship, station);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private int CountInRangeShips(StationState station)
        {
            int count = 0;
            foreach (var s in station.ships.Values)
                if (s.visitState != ShipVisitState.OutOfRange) count++;
            return count;
        }

        private void TrySpawnShip(StationState station)
        {
            if (_registry.Ships.Count == 0) return;

            // Pick a random ship template
            var templates = new List<ShipTemplate>(_registry.Ships.Values);
            var template  = templates[UnityEngine.Random.Range(0, templates.Count)];

            // Spawn in a random position on the edge of the antenna range
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float radius = BaseRange * 0.8f;
            float wx = Mathf.Cos(angle) * radius;
            float wy = Mathf.Sin(angle) * radius;

            string factionId = null;
            if (template.factionRestrictions.Count > 0)
                factionId = template.factionRestrictions[
                    UnityEngine.Random.Range(0, template.factionRestrictions.Count)];
            else if (_registry.Factions.Count > 0)
            {
                var keys = new List<string>(_registry.Factions.Keys);
                factionId = keys[UnityEngine.Random.Range(0, keys.Count)];
            }

            var ship = ShipInstance.Create(template.id,
                GenerateShipName(), template.role, "transit", factionId, template.threatLevel);
            ship.visitState    = ShipVisitState.InRange;
            ship.status        = "incoming";
            ship.worldX        = wx;
            ship.worldY        = wy;
            ship.inRangeSinceTick = station.tick;
            AssignNewDriftTarget(ship);

            station.AddShip(ship);
            station.LogEvent($"Ship detected: {ship.name} ({ship.role}) — {ship.VisitStateLabel()}");
            Debug.Log($"[AntennaSystem] Spawned {ship.name} at ({wx:F0},{wy:F0}) visitState={ship.visitState}");

            // Immediately evaluate whether this ship has reason to self-dock
            EvaluateSelfDock(ship, template, station);
        }

        private void EvaluateSelfDock(ShipInstance ship, ShipTemplate template, StationState station)
        {
            if (!station.HasFunctionalHangar()) return;
            if (ship.visitState != ShipVisitState.InRange) return;

            // Check if station has any resource the ship wants
            bool wantsResource = false;
            foreach (var res in template.resourcesWanted)
                if (station.GetResource(res) > 0f) { wantsResource = true; break; }

            // Check entertainment need
            bool wantsEntertainment = template.hasEntertainmentNeed;

            if (wantsResource || wantsEntertainment)
            {
                ship.visitState = ShipVisitState.Inbound;
                ship.status     = "incoming";
                station.LogEvent($"{ship.name} spotted something of interest — heading to dock.");
                Debug.Log($"[AntennaSystem] {ship.name} self-docking (wantsResource={wantsResource}, wantsEnt={wantsEntertainment})");
            }
            else
            {
                ship.visitState = ShipVisitState.Passing;
                ship.status     = "incoming";
            }
        }

        private void TickShipDrift(ShipInstance ship, StationState station)
        {
            // Move toward drift target
            float dx = ship.driftTargetX - ship.worldX;
            float dy = ship.driftTargetY - ship.worldY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < 1f)
                AssignNewDriftTarget(ship);
            else
            {
                float step = Mathf.Min(DriftSpeed, dist);
                ship.worldX += (dx / dist) * step;
                ship.worldY += (dy / dist) * step;
            }
        }

        private void DriftShipsAway(StationState station)
        {
            foreach (var ship in new List<ShipInstance>(station.ships.Values))
            {
                if (ship.visitState == ShipVisitState.OutOfRange) continue;
                if (ship.visitState == ShipVisitState.Docked    ||
                    ship.visitState == ShipVisitState.Departing) continue; // shuttle system handles

                // Move away from station center
                float dist = Mathf.Sqrt(ship.worldX * ship.worldX + ship.worldY * ship.worldY);
                if (dist < 1f) { ship.worldX += 1f; dist = 1f; }
                float step = DriftSpeed * 1.5f;
                ship.worldX += (ship.worldX / dist) * step;
                ship.worldY += (ship.worldY / dist) * step;

                if (dist > WorldHalfExtent)
                {
                    ship.visitState = ShipVisitState.OutOfRange;
                    station.RemoveShip(ship.uid);
                    station.LogEvent($"{ship.name} drifted out of range (antenna offline).");
                }
            }
        }

        private void AssignNewDriftTarget(ShipInstance ship)
        {
            float angle  = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float radius = UnityEngine.Random.Range(200f, 450f);
            ship.driftTargetX = Mathf.Cos(angle) * radius;
            ship.driftTargetY = Mathf.Sin(angle) * radius;
        }

        private static readonly string[] _prefixes =
            { "ISV", "MCV", "RSV", "FSS", "RVS", "DSV", "CRV", "ASV", "STV" };
        private static readonly string[] _names =
        {
            "Wayward Star","Iron Margin","Pale Accord","Threshold Crossing",
            "Second Dawn","Drift Signal","Open Hand","Cold Meridian",
            "Faded Mark","Running Tide","Broken Covenant","Quiet Passage",
            "Ember Trade","Scatterlight","Long Reach","Veil Runner",
            "Dust Pilgrim","Low Road","Amber Frontier","Signal Lost"
        };

        private static string GenerateShipName()
            => $"{_prefixes[UnityEngine.Random.Range(0, _prefixes.Length)]} " +
               $"{_names[UnityEngine.Random.Range(0, _names.Length)]}";
    }
}
