// FleetSubPanelController.cs
// Fleet tab panel (UI-020).
//
// Displays:
//   1. Ship Overview list — all owned ships with name, role badge, status indicator,
//      crew count, and a condition bar (HP %).
//   2. Ship Detail sub-panel — opens below the list when a ship row is clicked.
//      Shows: ship info (type, role, capacity, condition), crew assignment list
//      (assigned NPCs with add/remove capability), mission history log (last 10
//      fleet events for this ship), and a Repair button for damaged ships.
//
// Status derivation from OwnedShipInstance:
//   "destroyed" or damageState=Destroyed → Destroyed  (dim red)
//   damageState=Critical                → In Distress (bright red)
//   status="on_mission"                 → On Mission  (blue)
//   status="repairing"                  → Damaged     (amber, being repaired)
//   status="docked" + any damage        → Damaged     (amber)
//   status="docked" + Undamaged         → Docked      (neutral)
//   status="departing"                  → Departing   (blue-grey)
//
// Call Refresh(StationState, ShipSystem) to sync with live data.
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
    /// Fleet tab panel.  Extends <see cref="VisualElement"/> so it can be added
    /// directly to the side-panel drawer.
    /// </summary>
    public class FleetSubPanelController : VisualElement
    {
        // ── Events ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks a ship row.
        /// Argument is the ship uid; use it to navigate or highlight the ship in-world.
        /// </summary>
        public event Action<string> OnShipRowClicked;

        /// <summary>
        /// Fired when the player clicks the Rescue Dispatch button on an In Distress ship.
        /// Argument is the ship uid; WaystationHUDController should open the Dispatch sub-tab.
        /// </summary>
        public Action<string> OnRescueDispatch;

        // ── Display-status constants ─────────────────────────────────────────────

        public const string StatusDocked     = "Docked";
        public const string StatusDeparting  = "Departing";
        public const string StatusOnMission  = "On Mission";
        public const string StatusInDistress = "In Distress";
        public const string StatusDamaged    = "Damaged";
        public const string StatusDestroyed  = "Destroyed";

        // ── USS class names ───────────────────────────────────────────────────────

        private const string PanelClass         = "ws-fleet-panel";
        private const string SectionHeaderClass = "ws-fleet-panel__section-header";
        private const string RowClass           = "ws-fleet-panel__ship-row";
        private const string RowSelectedClass   = "ws-fleet-panel__ship-row--selected";
        private const string BadgeClass         = "ws-fleet-panel__role-badge";
        private const string StatusDotClass     = "ws-fleet-panel__status-dot";
        private const string CondBarBgClass     = "ws-fleet-panel__cond-bar-bg";
        private const string CondBarFillClass   = "ws-fleet-panel__cond-bar-fill";
        private const string DetailClass        = "ws-fleet-panel__detail";
        private const string DetailHeaderClass  = "ws-fleet-panel__detail-header";
        private const string CrewRowClass       = "ws-fleet-panel__crew-row";
        private const string HistoryRowClass    = "ws-fleet-panel__history-row";
        private const string ActionBtnClass     = "ws-fleet-panel__action-btn";
        private const string EmptyClass         = "ws-fleet-panel__empty";

        // ── Internal state ────────────────────────────────────────────────────────

        private readonly ScrollView    _listScroll;
        private readonly VisualElement _listRoot;
        private readonly VisualElement _detailRoot;

        private StationState _station;
        private ShipSystem   _ships;
        private string       _selectedShipUid;

        // ── Constructor ───────────────────────────────────────────────────────────

        public FleetSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // ── Ship list (top area, fills available space) ───────────────────────
            _listScroll = new ScrollView(ScrollViewMode.Vertical);
            _listScroll.style.flexGrow  = 1;
            _listScroll.style.minHeight = 60;
            Add(_listScroll);

            _listRoot = _listScroll.contentContainer;
            _listRoot.style.flexDirection = FlexDirection.Column;

            // ── Ship detail sub-panel (bottom area, hidden until a ship is selected)
            _detailRoot = new VisualElement();
            _detailRoot.AddToClassList(DetailClass);
            _detailRoot.style.flexDirection  = FlexDirection.Column;
            _detailRoot.style.flexGrow       = 0;
            _detailRoot.style.maxHeight      = Length.Percent(55);
            _detailRoot.style.overflow       = Overflow.Hidden;
            _detailRoot.style.borderTopWidth = 1;
            _detailRoot.style.borderTopColor = new Color(0.18f, 0.24f, 0.34f, 1f);
            _detailRoot.style.paddingTop     = 6;
            _detailRoot.style.marginTop      = 4;
            _detailRoot.style.display        = DisplayStyle.None;
            Add(_detailRoot);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the fleet panel from live station data.
        /// Call on mount and on every relevant tick.
        /// </summary>
        public void Refresh(StationState station, ShipSystem ships)
        {
            _station = station;
            _ships   = ships;
            RebuildList();

            // Keep detail in sync if a ship is still selected.
            if (_selectedShipUid != null &&
                station?.ownedShips?.ContainsKey(_selectedShipUid) == true)
            {
                RebuildDetail(station.ownedShips[_selectedShipUid]);
            }
            else
            {
                _selectedShipUid = null;
                _detailRoot.style.display = DisplayStyle.None;
            }
        }

        // ── Ship list rebuild ─────────────────────────────────────────────────────

        private void RebuildList()
        {
            _listRoot.Clear();

            _listRoot.Add(BuildSectionHeader("Owned Ships",
                new Color(0.55f, 0.70f, 0.90f, 1f)));

            if (_station == null || _station.ownedShips.Count == 0)
            {
                var empty = new Label("No ships in fleet.");
                empty.AddToClassList(EmptyClass);
                empty.style.color      = new Color(0.55f, 0.60f, 0.70f, 1f);
                empty.style.fontSize   = 10;
                empty.style.paddingTop = 4;
                _listRoot.Add(empty);
                return;
            }

            var ownedShips = _ships?.GetOwnedShips(_station)
                ?? new List<OwnedShipInstance>(_station.ownedShips.Values);

            foreach (var ship in ownedShips)
                _listRoot.Add(BuildShipRow(ship));
        }

        private VisualElement BuildShipRow(OwnedShipInstance ship)
        {
            string displayStatus = DeriveDisplayStatus(ship);
            Color  statusColor   = StatusColor(displayStatus);

            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection  = FlexDirection.Column;
            row.style.paddingTop     = 5;
            row.style.paddingBottom  = 5;
            row.style.paddingLeft    = 4;
            row.style.paddingRight   = 4;
            row.style.marginBottom   = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.15f, 0.20f, 0.28f, 1f);
            row.style.cursor         = new StyleCursor(StyleKeyword.Auto);

            bool selected = ship.uid == _selectedShipUid;
            if (selected)
                row.style.backgroundColor = new Color(0.12f, 0.17f, 0.26f, 1f);

            // ── Top row: status dot, name, role badge ─────────────────────────────
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems    = Align.Center;
            topRow.style.marginBottom  = 3;
            row.Add(topRow);

            // Status dot
            var dot = new VisualElement();
            dot.AddToClassList(StatusDotClass);
            dot.style.width           = 8;
            dot.style.height          = 8;
            dot.style.borderTopLeftRadius     = 4;
            dot.style.borderTopRightRadius    = 4;
            dot.style.borderBottomLeftRadius  = 4;
            dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = statusColor;
            dot.style.marginRight     = 6;
            dot.style.flexShrink      = 0;
            topRow.Add(dot);

            // Ship name
            var nameLabel = new Label(ship.name ?? "Unknown");
            nameLabel.AddToClassList("ws-fleet-panel__ship-name");
            nameLabel.style.fontSize      = 11;
            nameLabel.style.color         = new Color(0.88f, 0.88f, 0.92f, 1f);
            nameLabel.style.flexGrow      = 1;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            topRow.Add(nameLabel);

            // Role badge
            var badge = new Label(RoleBadgeLabel(ship.role));
            badge.AddToClassList(BadgeClass);
            badge.style.fontSize         = 9;
            badge.style.color            = new Color(0.34f, 0.47f, 0.63f, 1f);
            badge.style.paddingLeft      = 3;
            badge.style.paddingRight     = 3;
            badge.style.paddingTop       = 1;
            badge.style.paddingBottom    = 1;
            badge.style.marginLeft       = 4;
            badge.style.borderTopLeftRadius     = 2;
            badge.style.borderTopRightRadius    = 2;
            badge.style.borderBottomLeftRadius  = 2;
            badge.style.borderBottomRightRadius = 2;
            badge.style.backgroundColor  = new Color(0.10f, 0.14f, 0.22f, 1f);
            topRow.Add(badge);

            // ── Bottom row: status label, crew count, condition bar ───────────────
            var bottomRow = new VisualElement();
            bottomRow.style.flexDirection = FlexDirection.Row;
            bottomRow.style.alignItems    = Align.Center;
            row.Add(bottomRow);

            // Status text
            var statusLabel = new Label(displayStatus);
            statusLabel.style.fontSize = 10;
            statusLabel.style.color    = statusColor;
            statusLabel.style.width    = 68;
            statusLabel.style.flexShrink = 0;
            bottomRow.Add(statusLabel);

            // Crew count
            var crewLabel = new Label($"Crew: {ship.crewUids.Count}");
            crewLabel.style.fontSize   = 10;
            crewLabel.style.color      = new Color(0.55f, 0.65f, 0.75f, 1f);
            crewLabel.style.marginLeft = 4;
            crewLabel.style.marginRight = 6;
            crewLabel.style.flexShrink = 0;
            bottomRow.Add(crewLabel);

            // Condition bar
            var condBarBg = new VisualElement();
            condBarBg.AddToClassList(CondBarBgClass);
            condBarBg.style.flexGrow        = 1;
            condBarBg.style.height          = 5;
            condBarBg.style.backgroundColor = new Color(0.12f, 0.16f, 0.22f, 1f);
            condBarBg.style.borderTopLeftRadius     = 2;
            condBarBg.style.borderTopRightRadius    = 2;
            condBarBg.style.borderBottomLeftRadius  = 2;
            condBarBg.style.borderBottomRightRadius = 2;
            condBarBg.style.overflow        = Overflow.Hidden;
            bottomRow.Add(condBarBg);

            float condPct = Mathf.Clamp(ship.conditionPct, 0f, 100f);
            var condFill = new VisualElement();
            condFill.AddToClassList(CondBarFillClass);
            condFill.style.height          = Length.Percent(100);
            condFill.style.width           = Length.Percent(condPct);
            condFill.style.backgroundColor = ConditionBarColor(condPct);
            condBarBg.Add(condFill);

            // Condition percent label
            var condPctLabel = new Label($"{condPct:F0}%");
            condPctLabel.style.fontSize   = 9;
            condPctLabel.style.color      = new Color(0.55f, 0.65f, 0.75f, 1f);
            condPctLabel.style.marginLeft = 4;
            condPctLabel.style.flexShrink = 0;
            bottomRow.Add(condPctLabel);

            // ── In Distress: rescue dispatch button ───────────────────────────────
            if (displayStatus == StatusInDistress)
            {
                var rescueRow = new VisualElement();
                rescueRow.style.flexDirection = FlexDirection.Row;
                rescueRow.style.marginTop     = 3;
                row.Add(rescueRow);

                var rescueBtn = new Button();
                rescueBtn.text = "⚠ Dispatch Rescue";
                rescueBtn.AddToClassList(ActionBtnClass);
                ApplyActionButtonStyle(rescueBtn, new Color(0.80f, 0.20f, 0.20f, 1f));
                string capturedUid = ship.uid;
                rescueBtn.RegisterCallback<ClickEvent>(_ =>
                {
                    OnRescueDispatch?.Invoke(capturedUid);
                });
                rescueRow.Add(rescueBtn);
            }

            // ── Row click → open detail ───────────────────────────────────────────
            string capturedShipUid = ship.uid;
            row.RegisterCallback<ClickEvent>(_ => SelectShip(capturedShipUid));
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (capturedShipUid != _selectedShipUid)
                    row.style.backgroundColor = new Color(0.10f, 0.14f, 0.22f, 0.5f);
            });
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (capturedShipUid != _selectedShipUid)
                    row.style.backgroundColor = StyleKeyword.Null;
            });

            return row;
        }

        private void SelectShip(string shipUid)
        {
            _selectedShipUid = shipUid;
            OnShipRowClicked?.Invoke(shipUid);

            if (_station?.ownedShips?.TryGetValue(shipUid, out var ship) == true)
            {
                RebuildDetail(ship);
                _detailRoot.style.display = DisplayStyle.Flex;
            }

            // Rebuild the list so row selection highlight updates.
            RebuildList();
        }

        // ── Ship detail rebuild ───────────────────────────────────────────────────

        private void RebuildDetail(OwnedShipInstance ship)
        {
            _detailRoot.Clear();

            if (ship == null) return;

            // ── Header: "SHIP DETAIL — <ship name>" ──────────────────────────────
            var header = new VisualElement();
            header.style.flexDirection  = FlexDirection.Row;
            header.style.alignItems     = Align.Center;
            header.style.marginBottom   = 4;
            _detailRoot.Add(header);

            var titleLabel = new Label($"◆ {ship.name}");
            titleLabel.AddToClassList(DetailHeaderClass);
            titleLabel.style.fontSize   = 11;
            titleLabel.style.color      = new Color(0.55f, 0.80f, 1.00f, 1f);
            titleLabel.style.flexGrow   = 1;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(titleLabel);

            var closeBtn = new Button { text = "✕" };
            closeBtn.style.width              = 20;
            closeBtn.style.height             = 20;
            closeBtn.style.fontSize           = 10;
            closeBtn.style.color              = new Color(0.55f, 0.60f, 0.70f, 1f);
            closeBtn.style.backgroundColor    = StyleKeyword.Null;
            closeBtn.style.borderTopWidth     = 0;
            closeBtn.style.borderRightWidth   = 0;
            closeBtn.style.borderBottomWidth  = 0;
            closeBtn.style.borderLeftWidth    = 0;
            closeBtn.RegisterCallback<ClickEvent>(_ =>
            {
                _selectedShipUid = null;
                _detailRoot.style.display = DisplayStyle.None;
                RebuildList();
            });
            header.Add(closeBtn);

            // ── Detail scroll area ────────────────────────────────────────────────
            var detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailScroll.style.flexGrow = 1;
            _detailRoot.Add(detailScroll);

            var content = detailScroll.contentContainer;
            content.style.flexDirection = FlexDirection.Column;

            // ── Ship Info section ─────────────────────────────────────────────────
            content.Add(BuildDetailSectionHeader("Ship Info",
                new Color(0.55f, 0.80f, 1.00f, 1f)));

            string displayStatus = DeriveDisplayStatus(ship);
            Color  statusColor   = StatusColor(displayStatus);

            AddInfoRow(content, "Role",      RoleBadgeLabel(ship.role));
            AddInfoRow(content, "Status",    displayStatus, statusColor);
            AddInfoRow(content, "Condition", $"{ship.conditionPct:F0}% ({ship.ConditionLabel()})");
            AddInfoRow(content, "Crew",      $"{ship.crewUids.Count} assigned");

            // ── Crew Assignments section ──────────────────────────────────────────
            content.Add(BuildDetailSectionHeader("Crew Assignments",
                new Color(0.60f, 0.90f, 0.55f, 1f)));

            if (_station != null && ship.crewUids.Count > 0)
            {
                foreach (var crewUid in ship.crewUids)
                {
                    if (!_station.npcs.TryGetValue(crewUid, out var npc)) continue;

                    var crewRow = new VisualElement();
                    crewRow.AddToClassList(CrewRowClass);
                    crewRow.style.flexDirection = FlexDirection.Row;
                    crewRow.style.alignItems    = Align.Center;
                    crewRow.style.paddingTop    = 2;
                    crewRow.style.paddingBottom = 2;
                    content.Add(crewRow);

                    var crewName = new Label(npc.name ?? crewUid);
                    crewName.style.flexGrow  = 1;
                    crewName.style.fontSize  = 10;
                    crewName.style.color     = new Color(0.75f, 0.80f, 0.88f, 1f);
                    crewRow.Add(crewName);

                    var crewClass = new Label(npc.classId ?? "");
                    crewClass.style.fontSize  = 9;
                    crewClass.style.color     = new Color(0.45f, 0.55f, 0.65f, 1f);
                    crewClass.style.marginLeft = 4;
                    crewRow.Add(crewClass);

                    var removeBtn = new Button { text = "−" };
                    removeBtn.style.fontSize         = 10;
                    removeBtn.style.width            = 18;
                    removeBtn.style.height           = 18;
                    removeBtn.style.marginLeft       = 6;
                    removeBtn.style.backgroundColor  = new Color(0.25f, 0.12f, 0.12f, 1f);
                    removeBtn.style.color            = new Color(0.85f, 0.35f, 0.35f, 1f);
                    removeBtn.style.borderTopWidth    = 0;
                    removeBtn.style.borderRightWidth  = 0;
                    removeBtn.style.borderBottomWidth = 0;
                    removeBtn.style.borderLeftWidth   = 0;
                    string capturedCrewUid  = crewUid;
                    string capturedShipUid  = ship.uid;
                    removeBtn.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (_station == null || _ships == null) return;
                        // Remove just this NPC: rebuild crew list without them.
                        if (_station.ownedShips.TryGetValue(capturedShipUid, out var s))
                        {
                            var remaining = new List<string>(s.crewUids);
                            remaining.Remove(capturedCrewUid);
                            _ships.AssignCrew(capturedShipUid, remaining, _station);
                            if (_station.ownedShips.TryGetValue(capturedShipUid, out var updated))
                                RebuildDetail(updated);
                        }
                    });
                    crewRow.Add(removeBtn);
                }
            }
            else
            {
                var noCrew = new Label("No crew assigned.");
                noCrew.AddToClassList(EmptyClass);
                noCrew.style.color      = new Color(0.45f, 0.50f, 0.60f, 1f);
                noCrew.style.fontSize   = 10;
                noCrew.style.paddingTop = 2;
                content.Add(noCrew);
            }

            // ── Mission History section ───────────────────────────────────────────
            content.Add(BuildDetailSectionHeader("Mission History (last 10)",
                new Color(0.75f, 0.70f, 0.55f, 1f)));

            var history = GetMissionHistory(ship, 10);
            if (history.Count == 0)
            {
                var noHistory = new Label("No mission history.");
                noHistory.AddToClassList(EmptyClass);
                noHistory.style.color      = new Color(0.45f, 0.50f, 0.60f, 1f);
                noHistory.style.fontSize   = 10;
                noHistory.style.paddingTop = 2;
                content.Add(noHistory);
            }
            else
            {
                foreach (var entry in history)
                {
                    var histRow = new Label(entry);
                    histRow.AddToClassList(HistoryRowClass);
                    histRow.style.fontSize      = 9;
                    histRow.style.color         = new Color(0.55f, 0.60f, 0.68f, 1f);
                    histRow.style.paddingTop    = 1;
                    histRow.style.paddingBottom = 1;
                    histRow.style.whiteSpace    = WhiteSpace.Normal;
                    content.Add(histRow);
                }
            }

            // ── Repair button (if docked, damaged, and not destroyed) ─────────────
            if (ship.damageState != ShipDamageState.Destroyed &&
                ship.conditionPct < ShipSystem.DamageThresholdUndamaged &&
                ship.status == "docked")
            {
                content.Add(BuildDetailSectionHeader("Repair",
                    new Color(0.90f, 0.65f, 0.30f, 1f)));

                var (parts, ticks) = ShipSystem.GetRepairCost(ship);

                var repairInfo = new Label($"Cost: {parts} parts  |  Est. time: {ticks} ticks");
                repairInfo.style.fontSize   = 10;
                repairInfo.style.color      = new Color(0.75f, 0.70f, 0.55f, 1f);
                repairInfo.style.paddingTop = 3;
                content.Add(repairInfo);

                var repairBtn = new Button { text = $"Begin Repair ({parts} parts)" };
                repairBtn.AddToClassList(ActionBtnClass);
                ApplyActionButtonStyle(repairBtn, new Color(0.90f, 0.65f, 0.25f, 1f));
                repairBtn.style.marginTop = 4;
                string capturedUid = ship.uid;
                repairBtn.RegisterCallback<ClickEvent>(e =>
                {
                    if (_station == null || _ships == null) return;
                    _ships.BeginRepair(capturedUid, _station, out _);
                    if (_station.ownedShips.TryGetValue(capturedUid, out var updated))
                        RebuildDetail(updated);
                    RebuildList();
                });
                content.Add(repairBtn);
            }
            else if (ship.status == "repairing")
            {
                content.Add(BuildDetailSectionHeader("Repair",
                    new Color(0.90f, 0.65f, 0.30f, 1f)));
                var repairing = new Label("Repair in progress…");
                repairing.style.fontSize   = 10;
                repairing.style.color      = new Color(0.90f, 0.65f, 0.30f, 1f);
                repairing.style.paddingTop = 3;
                content.Add(repairing);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Derives the UI display-status string from a ship's runtime state.
        /// </summary>
        public static string DeriveDisplayStatus(OwnedShipInstance ship)
        {
            if (ship == null) return StatusDocked;

            if (ship.status == "destroyed" || ship.damageState == ShipDamageState.Destroyed)
                return StatusDestroyed;

            if (ship.damageState == ShipDamageState.Critical)
                return StatusInDistress;

            if (ship.status == "on_mission")
                return StatusOnMission;

            if (ship.status == "departing")
                return StatusDeparting;

            if (ship.status == "repairing")
                return StatusDamaged;

            if (ship.damageState != ShipDamageState.Undamaged)
                return StatusDamaged;

            return StatusDocked;
        }

        /// <summary>
        /// Returns the UI colour for a given display status string.
        /// </summary>
        public static Color StatusColor(string displayStatus) => displayStatus switch
        {
            StatusDocked     => new Color(0.55f, 0.70f, 0.85f, 1f), // neutral blue-grey
            StatusDeparting  => new Color(0.40f, 0.65f, 0.85f, 1f), // lighter blue
            StatusOnMission  => new Color(0.20f, 0.55f, 0.90f, 1f), // blue
            StatusInDistress => new Color(0.90f, 0.22f, 0.22f, 1f), // bright red
            StatusDamaged    => new Color(0.90f, 0.65f, 0.25f, 1f), // amber
            StatusDestroyed  => new Color(0.50f, 0.20f, 0.20f, 1f), // dim red
            _                => new Color(0.55f, 0.70f, 0.85f, 1f),
        };

        /// <summary>Returns a human-readable label for a ship role string.</summary>
        public static string RoleBadgeLabel(string role)
        {
            if (string.IsNullOrEmpty(role)) return "Unknown";
            return role switch
            {
                "scout"      => "Scout",
                "mining"     => "Mining",
                "combat"     => "Combat",
                "transport"  => "Transport",
                "diplomatic" => "Diplomatic",
                "trader"     => "Trader",
                "patrol"     => "Patrol",
                _            => role,
            };
        }

        /// <summary>
        /// Returns the last <paramref name="count"/> station log entries that mention
        /// the ship by name and relate to fleet operations.
        /// station.log is newest-first and capped at 200 entries by StationState.LogEvent,
        /// so this scan is always bounded.
        /// </summary>
        private List<string> GetMissionHistory(OwnedShipInstance ship, int count)
        {
            var result = new List<string>();
            if (_station == null || ship == null) return result;

            // Guard: an empty name would match every log entry.
            if (string.IsNullOrEmpty(ship.name)) return result;

            string shipNameLower = ship.name.ToLowerInvariant();
            int scanned = 0;
            // station.log is capped at 200; scan at most 200 entries.
            const int MaxScan = 200;

            foreach (var entry in _station.log)
            {
                if (result.Count >= count || scanned >= MaxScan) break;
                scanned++;

                string entryLower = (entry ?? "").ToLowerInvariant();
                if (entryLower.Contains(shipNameLower) && IsFleetLogEntry(entry))
                    result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Returns true when the log message relates to a fleet operation.
        /// </summary>
        public static bool IsFleetLogEntry(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            string m = message.ToLowerInvariant();
            return m.Contains("fleet") || m.Contains("dispatch") || m.Contains("mission")
                || m.Contains("repair") || m.Contains("crew") || m.Contains("damage")
                || m.Contains("destroyed") || m.Contains("returned") || m.Contains("added to fleet");
        }

        private static Color ConditionBarColor(float condPct)
        {
            if (condPct >= 75f) return new Color(0.25f, 0.75f, 0.35f, 1f); // green
            if (condPct >= 25f) return new Color(0.90f, 0.65f, 0.25f, 1f); // amber
            return new Color(0.85f, 0.22f, 0.22f, 1f);                      // red
        }

        private VisualElement BuildSectionHeader(string title, Color color)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.fontSize                = 10;
            header.style.color                   = color;
            header.style.paddingTop              = 6;
            header.style.paddingBottom           = 3;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            return header;
        }

        private VisualElement BuildDetailSectionHeader(string title, Color color)
        {
            var header = new Label(title.ToUpper());
            header.style.fontSize                = 9;
            header.style.color                   = color;
            header.style.paddingTop              = 5;
            header.style.paddingBottom           = 2;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            return header;
        }

        private void AddInfoRow(VisualElement parent, string label, string value,
                                Color? valueColor = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingTop    = 1;
            row.style.paddingBottom = 1;
            parent.Add(row);

            var lbl = new Label(label + ":");
            lbl.style.fontSize   = 10;
            lbl.style.color      = new Color(0.50f, 0.58f, 0.68f, 1f);
            lbl.style.width      = 68;
            lbl.style.flexShrink = 0;
            row.Add(lbl);

            var val = new Label(value ?? "—");
            val.style.fontSize = 10;
            val.style.color    = valueColor ?? new Color(0.82f, 0.85f, 0.90f, 1f);
            val.style.flexGrow = 1;
            row.Add(val);
        }

        private static void ApplyActionButtonStyle(Button btn, Color accentColor)
        {
            btn.style.fontSize           = 10;
            btn.style.color              = accentColor;
            btn.style.backgroundColor   = new Color(
                accentColor.r * 0.15f, accentColor.g * 0.15f, accentColor.b * 0.15f, 1f);
            btn.style.borderTopColor     = accentColor;
            btn.style.borderRightColor   = accentColor;
            btn.style.borderBottomColor  = accentColor;
            btn.style.borderLeftColor    = accentColor;
            btn.style.borderTopWidth     = 1;
            btn.style.borderRightWidth   = 1;
            btn.style.borderBottomWidth  = 1;
            btn.style.borderLeftWidth    = 1;
            btn.style.paddingLeft        = 6;
            btn.style.paddingRight       = 6;
            btn.style.paddingTop         = 2;
            btn.style.paddingBottom      = 2;
            btn.style.borderTopLeftRadius     = 2;
            btn.style.borderTopRightRadius    = 2;
            btn.style.borderBottomLeftRadius  = 2;
            btn.style.borderBottomRightRadius = 2;
        }
    }
}
