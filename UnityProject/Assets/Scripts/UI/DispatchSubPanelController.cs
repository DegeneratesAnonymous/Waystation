// DispatchSubPanelController.cs
// Fleet → Dispatch sub-tab panel (UI-021).
//
// Displays:
//   1. Mission type selector  — role-eligible mission types for the selected ship.
//   2. Ship selector          — owned ships eligible for the selected mission type.
//   3. Target POI selector    — known POIs from MapSystem; shows type, distance,
//                               and last-visited tick.
//   4. Crew confirmation      — current crew on the selected ship; warning when
//                               the ship has no crew assigned.
//   5. Confirm Dispatch btn   — calls ShipSystem.DispatchShipMission for generic
//                               missions, or AsteroidMissionSystem.DispatchAsteroidMission
//                               when a POI is selected.
//
// Call Refresh(StationState, ShipSystem, MapSystem, AsteroidMissionSystem)
// to sync with live data.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Fleet → Dispatch sub-tab panel.
    /// </summary>
    public class DispatchSubPanelController : VisualElement
    {
        // ── USS class names ───────────────────────────────────────────────────

        private const string PanelClass         = "ws-dispatch-panel";
        private const string SectionHeaderClass = "ws-dispatch-panel__section-header";
        private const string RowClass           = "ws-dispatch-panel__row";
        private const string RowSelectedClass   = "ws-dispatch-panel__row--selected";
        private const string ActionBtnClass     = "ws-dispatch-panel__action-btn";
        private const string WarningClass       = "ws-dispatch-panel__warning";
        private const string EmptyClass         = "ws-dispatch-panel__empty";

        // ── Internal state ────────────────────────────────────────────────────

        private readonly ScrollView    _scroll;
        private readonly VisualElement _listRoot;

        private StationState         _station;
        private ShipSystem           _fleet;
        private MapSystem            _map;
        private AsteroidMissionSystem _asteroidMissions;

        // Current selections
        private string _selectedMissionType;
        private string _selectedShipUid;
        private string _selectedPoiUid;

        // ── Constructor ───────────────────────────────────────────────────────

        public DispatchSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            Add(_scroll);

            _listRoot = _scroll.contentContainer;
            _listRoot.style.flexDirection = FlexDirection.Column;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the panel with current station, fleet, map, and mission data.
        /// Pass a non-null <paramref name="preselectedShipUid"/> to open the panel
        /// with a specific ship already selected (e.g. from the Overview sub-tab).
        /// </summary>
        public void Refresh(StationState station, ShipSystem fleet,
                            MapSystem map, AsteroidMissionSystem asteroidMissions,
                            string preselectedShipUid = null)
        {
            _station          = station;
            _fleet            = fleet;
            _map              = map;
            _asteroidMissions = asteroidMissions;

            if (!string.IsNullOrEmpty(preselectedShipUid))
                _selectedShipUid = preselectedShipUid;

            Rebuild();
        }

        // ── Private: rebuild the full UI ──────────────────────────────────────

        private void Rebuild()
        {
            _listRoot.Clear();

            if (_station == null || _fleet == null)
            {
                AddEmpty("No fleet data available.");
                return;
            }

            // ── 1. Mission type selector ──────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("MISSION TYPE"));
            BuildMissionTypeSection();

            // ── 2. Ship selector ──────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("SELECT SHIP"));
            BuildShipSection();

            // ── 3. POI selector ───────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("TARGET POI"));
            BuildPoiSection();

            // ── 4. Crew confirmation ──────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("CREW"));
            BuildCrewSection();

            // ── 5. Confirm Dispatch button ────────────────────────────────────
            BuildDispatchButton();
        }

        // ── Mission type section ──────────────────────────────────────────────

        private void BuildMissionTypeSection()
        {
            // Derive eligible mission types from the selected ship, or a fallback set.
            var missionTypes = GetEligibleMissionTypes();

            if (missionTypes.Count == 0)
            {
                AddEmpty("Select a ship to see mission types.");
                return;
            }

            // Auto-select first type if current selection is no longer valid.
            if (!missionTypes.Contains(_selectedMissionType))
                _selectedMissionType = missionTypes[0];

            foreach (var mt in missionTypes)
            {
                var row    = new VisualElement();
                var mtCopy = mt;
                row.AddToClassList(RowClass);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.paddingTop    = 3;
                row.style.paddingBottom = 3;
                row.style.paddingLeft   = 4;
                row.style.paddingRight  = 4;
                row.style.marginBottom  = 2;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);

                bool selected = mt == _selectedMissionType;
                row.style.backgroundColor = selected
                    ? new Color(0.15f, 0.25f, 0.45f, 1f)
                    : Color.clear;

                var label = new Label(mt.ToUpperInvariant());
                label.style.color    = selected ? Color.white : new Color(0.75f, 0.80f, 0.90f, 1f);
                label.style.fontSize = 11;
                row.Add(label);

                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedMissionType = mtCopy;
                    Rebuild();
                });
                _listRoot.Add(row);
            }
        }

        private List<string> GetEligibleMissionTypes()
        {
            if (string.IsNullOrEmpty(_selectedShipUid) ||
                !_station.ownedShips.TryGetValue(_selectedShipUid, out var ship))
            {
                // No ship selected — show a merged list of all eligible types from all ships.
                var allTypes = new HashSet<string>();
                foreach (var s in _station.ownedShips.Values)
                    if (s.status == "docked")
                        foreach (var mt in GetShipMissionTypes(s))
                            allTypes.Add(mt);

                var sorted = new List<string>(allTypes);
                sorted.Sort();
                return sorted;
            }

            return GetShipMissionTypes(ship);
        }

        private List<string> GetShipMissionTypes(OwnedShipInstance ship)
        {
            if (_fleet == null) return new List<string>();

            var types = new List<string>();
            if (ship.templateId != null)
            {
                // Enumerate canonical mission types and check eligibility for this ship directly.
                foreach (var mt in AllKnownMissionTypes())
                    if (_fleet.IsEligibleForMission(ship.uid, mt, _station))
                        types.Add(mt);
            }

            if (types.Count == 0)
            {
                // Fallback: role-based defaults.
                types.AddRange(ship.role switch
                {
                    "scout"      => new[] { "scout", "exploration" },
                    "mining"     => new[] { "mining", "asteroid" },
                    "combat"     => new[] { "combat", "defence", "patrol" },
                    "transport"  => new[] { "transport", "cargo", "hauling" },
                    "diplomatic" => new[] { "diplomatic", "courier", "trade" },
                    _            => new[] { ship.role },
                });
            }

            return types;
        }

        private static string[] AllKnownMissionTypes() => new[]
        {
            "scout", "exploration",
            "mining", "asteroid",
            "combat", "defence", "patrol",
            "transport", "cargo", "hauling",
            "diplomatic", "courier", "trade",
        };

        // ── Ship section ──────────────────────────────────────────────────────

        private void BuildShipSection()
        {
            var eligibleShips = GetShipsEligibleForMissionType(_selectedMissionType);

            if (eligibleShips.Count == 0)
            {
                string reason = string.IsNullOrEmpty(_selectedMissionType)
                    ? "Select a mission type first."
                    : $"No docked ships eligible for '{_selectedMissionType}'.";
                AddEmpty(reason);
                return;
            }

            // Auto-select first ship if current selection is no longer eligible.
            if (string.IsNullOrEmpty(_selectedShipUid) ||
                !eligibleShips.Exists(s => s.uid == _selectedShipUid))
            {
                _selectedShipUid = eligibleShips[0].uid;
            }

            foreach (var ship in eligibleShips)
            {
                var shipCopy = ship;
                var row      = new VisualElement();
                row.AddToClassList(RowClass);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.paddingTop    = 3;
                row.style.paddingBottom = 3;
                row.style.paddingLeft   = 4;
                row.style.paddingRight  = 4;
                row.style.marginBottom  = 2;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);

                bool selected = ship.uid == _selectedShipUid;
                row.style.backgroundColor = selected
                    ? new Color(0.15f, 0.25f, 0.45f, 1f)
                    : Color.clear;

                var name = new Label(ship.name);
                name.style.flexGrow  = 1;
                name.style.color     = selected ? Color.white : new Color(0.75f, 0.80f, 0.90f, 1f);
                name.style.fontSize  = 11;

                var role = new Label(ship.role.ToUpperInvariant());
                role.style.color    = new Color(0.50f, 0.65f, 0.90f, 1f);
                role.style.fontSize = 10;

                var crew = new Label($"{ship.crewUids.Count} crew");
                crew.style.color    = new Color(0.45f, 0.55f, 0.65f, 1f);
                crew.style.fontSize = 10;
                crew.style.marginLeft = 6;

                row.Add(name);
                row.Add(role);
                row.Add(crew);

                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedShipUid = shipCopy.uid;
                    Rebuild();
                });
                _listRoot.Add(row);
            }
        }

        private List<OwnedShipInstance> GetShipsEligibleForMissionType(string missionType)
        {
            var result = new List<OwnedShipInstance>();
            if (_fleet == null) return result;

            foreach (var ship in _fleet.GetOwnedShips(_station))
            {
                if (ship.status != "docked") continue;

                if (string.IsNullOrEmpty(missionType) ||
                    _fleet.IsEligibleForMission(ship.uid, missionType, _station))
                {
                    result.Add(ship);
                }
            }
            return result;
        }

        // ── POI section ───────────────────────────────────────────────────────

        private void BuildPoiSection()
        {
            if (_map == null || _station == null)
            {
                AddEmpty("Map data unavailable.");
                return;
            }

            var pois = _map.GetDiscoveredPois(_station);
            if (pois == null || pois.Count == 0)
            {
                AddEmpty("No discovered POIs.");
                return;
            }

            // Auto-clear invalid selection.
            bool poiStillValid = false;
            foreach (var p in pois)
                if (p.uid == _selectedPoiUid) { poiStillValid = true; break; }
            if (!poiStillValid) _selectedPoiUid = null;

            foreach (var poi in pois)
            {
                var poiCopy = poi;
                var row     = new VisualElement();
                row.AddToClassList(RowClass);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.paddingTop    = 3;
                row.style.paddingBottom = 3;
                row.style.paddingLeft   = 4;
                row.style.paddingRight  = 4;
                row.style.marginBottom  = 2;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);

                bool selected = poi.uid == _selectedPoiUid;
                row.style.backgroundColor = selected
                    ? new Color(0.15f, 0.25f, 0.45f, 1f)
                    : Color.clear;

                var nameLabel = new Label(poi.displayName ?? poi.uid);
                nameLabel.style.flexGrow  = 1;
                nameLabel.style.color     = selected ? Color.white : new Color(0.75f, 0.80f, 0.90f, 1f);
                nameLabel.style.fontSize  = 11;

                var typeLabel = new Label(poi.poiType ?? "Unknown");
                typeLabel.style.color    = new Color(0.50f, 0.75f, 0.60f, 1f);
                typeLabel.style.fontSize = 10;

                // Distance (Euclidean from origin; station has no position in data model).
                float dist = Mathf.Sqrt(poi.posX * poi.posX + poi.posY * poi.posY);
                var distLabel = new Label($"{dist:F0} u");
                distLabel.style.color     = new Color(0.45f, 0.55f, 0.65f, 1f);
                distLabel.style.fontSize  = 10;
                distLabel.style.marginLeft = 6;

                // Last visited indicator.
                string visitedText = poi.visited ? "visited" : "unvisited";
                var visitedLabel = new Label(visitedText);
                visitedLabel.style.color     = poi.visited
                    ? new Color(0.40f, 0.65f, 0.40f, 1f)
                    : new Color(0.60f, 0.60f, 0.60f, 1f);
                visitedLabel.style.fontSize  = 10;
                visitedLabel.style.marginLeft = 6;

                row.Add(nameLabel);
                row.Add(typeLabel);
                row.Add(distLabel);
                row.Add(visitedLabel);

                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedPoiUid = (poiCopy.uid == _selectedPoiUid) ? null : poiCopy.uid;
                    Rebuild();
                });
                _listRoot.Add(row);
            }
        }

        // ── Crew section ──────────────────────────────────────────────────────

        private void BuildCrewSection()
        {
            if (string.IsNullOrEmpty(_selectedShipUid) ||
                !_station.ownedShips.TryGetValue(_selectedShipUid, out var ship))
            {
                AddEmpty("Select a ship to see crew.");
                return;
            }

            if (ship.crewUids.Count == 0)
            {
                var warn = new Label("⚠ No crew assigned — dispatch not possible.");
                warn.AddToClassList(WarningClass);
                warn.style.color     = new Color(0.90f, 0.60f, 0.15f, 1f);
                warn.style.fontSize  = 11;
                warn.style.paddingTop = 4;
                _listRoot.Add(warn);
                return;
            }

            foreach (var crewUid in ship.crewUids)
            {
                string displayName = crewUid;
                if (_station.npcs.TryGetValue(crewUid, out var npc))
                    displayName = npc.name;

                var row = new Label($"• {displayName}");
                row.AddToClassList(RowClass);
                row.style.color     = new Color(0.75f, 0.80f, 0.90f, 1f);
                row.style.fontSize  = 11;
                row.style.paddingTop    = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft   = 4;
                _listRoot.Add(row);
            }
        }

        // ── Dispatch button ───────────────────────────────────────────────────

        private void BuildDispatchButton()
        {
            var spacer = new VisualElement();
            spacer.style.height = 8;
            _listRoot.Add(spacer);

            bool canDispatch = CanDispatchNow(out string blockReason);

            var btn = new Button();
            btn.AddToClassList(ActionBtnClass);
            btn.text = "CONFIRM DISPATCH";
            btn.SetEnabled(canDispatch);
            btn.style.marginTop    = 4;
            btn.style.paddingTop   = 6;
            btn.style.paddingBottom = 6;
            btn.style.backgroundColor = canDispatch
                ? new Color(0.15f, 0.40f, 0.65f, 1f)
                : new Color(0.25f, 0.25f, 0.30f, 1f);
            btn.style.color    = Color.white;
            btn.style.fontSize = 12;

            if (!canDispatch && !string.IsNullOrEmpty(blockReason))
            {
                var reasonLabel = new Label(blockReason);
                reasonLabel.AddToClassList(WarningClass);
                reasonLabel.style.color     = new Color(0.80f, 0.45f, 0.15f, 1f);
                reasonLabel.style.fontSize  = 10;
                reasonLabel.style.paddingTop = 2;
                _listRoot.Add(reasonLabel);
            }

            btn.clicked += OnConfirmDispatch;
            _listRoot.Add(btn);
        }

        private bool CanDispatchNow(out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(_selectedShipUid))
            {
                reason = "No ship selected.";
                return false;
            }

            if (!_station.ownedShips.TryGetValue(_selectedShipUid, out var ship))
            {
                reason = "Selected ship not found.";
                return false;
            }

            if (ship.crewUids.Count == 0)
            {
                reason = "Ship has no crew assigned.";
                return false;
            }

            if (ship.status != "docked")
            {
                reason = $"Ship is not docked (status: {ship.status}).";
                return false;
            }

            if (string.IsNullOrEmpty(_selectedMissionType))
            {
                reason = "No mission type selected.";
                return false;
            }

            return true;
        }

        private void OnConfirmDispatch()
        {
            if (!CanDispatchNow(out _)) return;

            bool poiSelected = !string.IsNullOrEmpty(_selectedPoiUid);

            // Determine whether the selected POI is an Asteroid — the mission system
            // that should be used depends on the POI type, not just the mission type string.
            bool poiIsAsteroid = poiSelected
                && _station.pointsOfInterest.TryGetValue(_selectedPoiUid, out var selectedPoi)
                && selectedPoi.poiType == "Asteroid";

            // Dispatch flow:
            //   - Asteroid POI missions: AsteroidMissionSystem handles crew locking,
            //     mission creation, and map generation in a single call.  The owned ship's
            //     status is then updated manually so the Overview reflects "On Mission".
            //   - All other missions: ShipSystem.DispatchShipMission handles ship
            //     status, crew locking, and mission lifecycle.
            if (poiIsAsteroid && _asteroidMissions != null)
            {
                if (!_station.ownedShips.TryGetValue(_selectedShipUid, out var shipRef))
                {
                    Debug.LogWarning("[DispatchSubPanel] Selected ship not found for asteroid dispatch.");
                    return;
                }

                var crewList = new List<string>(shipRef.crewUids);
                var (asteroidOk, asteroidReason, asteroidMap) =
                    _asteroidMissions.DispatchAsteroidMission(
                        _selectedPoiUid, crewList, _station);

                if (!asteroidOk)
                {
                    Debug.LogWarning($"[DispatchSubPanel] Asteroid dispatch failed: {asteroidReason}");
                    return;
                }

                // Update the owned ship's status so the Overview shows "On Mission".
                shipRef.status           = "on_mission";
                shipRef.missionUid       = asteroidMap?.missionUid;
                shipRef.missionType      = _selectedMissionType;
                shipRef.missionStartTick = _station.tick;
                shipRef.missionEndTick   = asteroidMap?.endTick
                                           ?? (_station.tick + DefaultDurationTicks(_selectedMissionType));
            }
            else
            {
                // Generic missions (including non-POI asteroid/mining missions) use ShipSystem.
                var (ok, reason, _) = _fleet.DispatchShipMission(
                    _selectedShipUid, _selectedMissionType,
                    DefaultDurationTicks(_selectedMissionType), _station);

                if (!ok)
                {
                    Debug.LogWarning($"[DispatchSubPanel] Dispatch failed: {reason}");
                    return;
                }
            }

            // Reset selections after successful dispatch.
            _selectedShipUid = null;
            _selectedPoiUid  = null;
            Rebuild();
        }

        private static int DefaultDurationTicks(string missionType) => missionType switch
        {
            "scout"      => 240,
            "exploration"=> 480,
            "mining"     => 480,
            "asteroid"   => 480,
            "combat"     => 360,
            "defence"    => 360,
            "patrol"     => 240,
            "transport"  => 360,
            "cargo"      => 360,
            "hauling"    => 360,
            "diplomatic" => 480,
            "courier"    => 240,
            "trade"      => 360,
            _            => 240,
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AddEmpty(string text)
        {
            var label = new Label(text);
            label.AddToClassList(EmptyClass);
            label.style.color     = new Color(0.45f, 0.50f, 0.60f, 1f);
            label.style.fontSize  = 10;
            label.style.paddingTop    = 4;
            label.style.paddingBottom = 4;
            label.style.paddingLeft   = 4;
            _listRoot.Add(label);
        }

        private VisualElement BuildSectionHeader(string title)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.color       = new Color(0.50f, 0.65f, 0.90f, 1f);
            header.style.fontSize    = 10;
            header.style.paddingTop  = 6;
            header.style.paddingBottom = 2;
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            return header;
        }
    }
}
