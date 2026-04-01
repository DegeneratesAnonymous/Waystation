// VisitorsSubPanelController.cs
// World → Visitors sub-tab panel (UI-016).
//
// Displays:
//   1. Pending Decisions — ships awaiting Grant / Deny / Negotiate (highlighted, at top)
//   2. Docked section — ship name, faction, role badge, NPC count; clicking a row opens
//      the Visiting Ship contextual panel (via OnShipRowClicked).
//   3. Incoming section — ship name (if known), faction (if known), role badge
//   4. Comms log — last 20 visitor-related event entries with timestamps; read-only
//
// Call Refresh(StationState, VisitorSystem) to sync with live data.
// Docking decisions wire through the OnGrantDocking / OnDenyDocking / OnNegotiateDocking
// callbacks, which WaystationHUDController connects to the live VisitorSystem.
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
    /// World → Visitors sub-tab panel.  Extends <see cref="VisualElement"/> so it can
    /// be added directly to the side-panel drawer.
    /// </summary>
    public class VisitorsSubPanelController : VisualElement
    {
        // ── Events ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks a docked ship row.
        /// Argument is the ship uid; use it to open the Visiting Ship contextual panel.
        /// </summary>
        public event Action<string> OnShipRowClicked;

        // ── USS class names ──────────────────────────────────────────────────────

        private const string PanelClass         = "ws-visitors-panel";
        private const string SectionHeaderClass = "ws-visitors-panel__section-header";
        private const string RowClass           = "ws-visitors-panel__ship-row";
        private const string PendingRowClass    = "ws-visitors-panel__ship-row--pending";
        private const string BadgeClass         = "ws-visitors-panel__role-badge";
        private const string NameClass          = "ws-visitors-panel__ship-name";
        private const string DetailClass        = "ws-visitors-panel__ship-detail";
        private const string ActionBarClass     = "ws-visitors-panel__action-bar";
        private const string ActionBtnClass     = "ws-visitors-panel__action-btn";
        private const string CommsRowClass      = "ws-visitors-panel__comms-row";

        // ── Internal state ───────────────────────────────────────────────────────

        private readonly ScrollView    _scroll;
        private readonly VisualElement _listRoot;

        private StationState  _station;
        private VisitorSystem _visitorSystem;

        // ── Docking decision callbacks ────────────────────────────────────────────
        // WaystationHUDController wires these to the live VisitorSystem methods.

        /// <summary>Called when the player clicks Grant for a pending ship.</summary>
        public Action<string> OnGrantDocking;
        /// <summary>Called when the player clicks Deny for a pending ship.</summary>
        public Action<string> OnDenyDocking;
        /// <summary>Called when the player clicks Negotiate for a pending ship.</summary>
        public Action<string> OnNegotiateDocking;

        // ── Constructor ──────────────────────────────────────────────────────────

        public VisitorsSubPanelController()
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

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the visitors panel from live station data.
        /// Call on mount and again on every relevant tick.
        /// </summary>
        public void Refresh(StationState station, VisitorSystem visitorSystem)
        {
            _station       = station;
            _visitorSystem = visitorSystem;
            RebuildList();
        }

        // ── Role badge label (public so tests can access it) ────────────────────

        /// <summary>Returns a short human-readable label for the given ship role.</summary>
        public static string RoleBadgeLabel(string role)
        {
            if (string.IsNullOrEmpty(role)) return "Unknown";
            return role switch
            {
                "trader"    => "Trader",
                "refugee"   => "Refugee",
                "inspector" => "Inspector",
                "smuggler"  => "Smuggler",
                "raider"    => "Raider",
                "transport" => "Transport",
                "patrol"    => "Patrol",
                _           => role,
            };
        }

        // ── Internal: list rebuild ───────────────────────────────────────────────

        private void RebuildList()
        {
            _listRoot.Clear();

            if (_station == null) return;

            // Collect ship lists.
            var docked   = _station.GetDockedShips();
            var incoming = _station.GetIncomingShips();

            // Pending decisions — ships awaiting player approval.
            // These come from VisitorSystem.PendingDecisions (ship UIDs).
            var pendingShips = new List<ShipInstance>();
            if (_visitorSystem != null)
            {
                foreach (var uid in _visitorSystem.PendingDecisions)
                {
                    if (_station.ships.TryGetValue(uid, out var s))
                        pendingShips.Add(s);
                }
            }

            bool anyContent = false;

            // 1. ── Pending decisions ──────────────────────────────────────────
            if (pendingShips.Count > 0)
            {
                _listRoot.Add(BuildSectionHeader("⚠ Pending Decisions", new Color(0.95f, 0.80f, 0.25f, 1f)));
                foreach (var ship in pendingShips)
                    _listRoot.Add(BuildPendingShipRow(ship));
                anyContent = true;
            }

            // 2. ── Docked ships ───────────────────────────────────────────────
            if (docked.Count > 0)
            {
                _listRoot.Add(BuildSectionHeader("Docked", new Color(0.75f, 0.85f, 1.00f, 1f)));
                foreach (var ship in docked)
                    _listRoot.Add(BuildDockedShipRow(ship));
                anyContent = true;
            }

            // 3. ── Incoming ships ─────────────────────────────────────────────
            if (incoming.Count > 0)
            {
                _listRoot.Add(BuildSectionHeader("Incoming", new Color(0.75f, 0.90f, 0.80f, 1f)));
                foreach (var ship in incoming)
                    _listRoot.Add(BuildIncomingShipRow(ship));
                anyContent = true;
            }

            if (!anyContent)
            {
                var empty = new Label("No ships in range.");
                empty.style.color          = new Color(0.6f, 0.65f, 0.75f, 1f);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.paddingTop     = 20;
                empty.style.fontSize       = 11;
                _listRoot.Add(empty);
            }

            // 4. ── Comms log ──────────────────────────────────────────────────
            _listRoot.Add(BuildCommsLog());
        }

        // ── Section header ───────────────────────────────────────────────────────

        private VisualElement BuildSectionHeader(string title, Color color)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.fontSize              = 10;
            header.style.color                 = color;
            header.style.paddingTop            = 8;
            header.style.paddingBottom         = 3;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            return header;
        }

        // ── Pending decision row (with Grant / Deny / Negotiate buttons) ─────────

        private VisualElement BuildPendingShipRow(ShipInstance ship)
        {
            var row = BuildBaseShipRow(ship);
            row.AddToClassList(PendingRowClass);
            row.style.backgroundColor = new Color(0.30f, 0.24f, 0.12f, 0.95f);
            row.style.borderLeftWidth  = 3;
            row.style.borderLeftColor  = new Color(0.95f, 0.80f, 0.25f, 1f);

            // Action buttons
            var actionBar = new VisualElement();
            actionBar.AddToClassList(ActionBarClass);
            actionBar.style.flexDirection = FlexDirection.Row;
            actionBar.style.marginTop     = 6;

            string capturedId = ship.uid;

            actionBar.Add(BuildActionButton("Grant",     new Color(0.25f, 0.70f, 0.35f, 1f),
                () => OnGrantDocking?.Invoke(capturedId)));
            actionBar.Add(BuildActionButton("Deny",      new Color(0.75f, 0.25f, 0.20f, 1f),
                () => OnDenyDocking?.Invoke(capturedId)));
            actionBar.Add(BuildActionButton("Negotiate", new Color(0.30f, 0.55f, 0.80f, 1f),
                () => OnNegotiateDocking?.Invoke(capturedId)));

            row.Add(actionBar);
            return row;
        }

        private VisualElement BuildActionButton(string labelText, Color bgColor, Action onClick)
        {
            var btn = new Label(labelText);
            btn.AddToClassList(ActionBtnClass);
            btn.style.fontSize           = 10;
            btn.style.paddingTop         = 3;
            btn.style.paddingBottom      = 3;
            btn.style.paddingLeft        = 10;
            btn.style.paddingRight       = 10;
            btn.style.marginRight        = 5;
            btn.style.backgroundColor    = bgColor;
            btn.style.color              = new Color(1f, 1f, 1f, 1f);
            btn.style.unityTextAlign     = TextAnchor.MiddleCenter;
            btn.style.borderTopLeftRadius     = 3;
            btn.style.borderTopRightRadius    = 3;
            btn.style.borderBottomLeftRadius  = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.cursor             = new StyleCursor(MouseCursor.Link);
            btn.focusable                = true;

            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            btn.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                {
                    onClick?.Invoke();
                    evt.StopPropagation();
                }
            });
            return btn;
        }

        // ── Docked ship row (clickable — opens Visiting Ship panel) ─────────────

        private VisualElement BuildDockedShipRow(ShipInstance ship)
        {
            var row = BuildBaseShipRow(ship);

            // Passenger count
            if (ship.passengerUids.Count > 0)
            {
                var npcLabel = new Label($"{ship.passengerUids.Count} visitor{(ship.passengerUids.Count == 1 ? "" : "s")} aboard");
                npcLabel.AddToClassList(DetailClass);
                npcLabel.style.fontSize      = 9;
                npcLabel.style.color         = new Color(0.60f, 0.70f, 0.80f, 1f);
                npcLabel.style.paddingTop    = 2;
                row.Add(npcLabel);
            }

            // Time remaining label (ticks until planned departure)
            if (ship.plannedDepartureTick > 0 && _station != null)
            {
                int remaining = ship.plannedDepartureTick - _station.tick;
                if (remaining > 0)
                {
                    var timeLabel = new Label($"Departs in {remaining} tick{(remaining == 1 ? "" : "s")}");
                    timeLabel.AddToClassList(DetailClass);
                    timeLabel.style.fontSize      = 9;
                    timeLabel.style.color         = new Color(0.55f, 0.65f, 0.75f, 1f);
                    timeLabel.style.paddingTop    = 1;
                    row.Add(timeLabel);
                }
            }

            // Click opens Visiting Ship contextual panel
            string capturedId = ship.uid;
            row.style.cursor = new StyleCursor(MouseCursor.Link);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                Debug.Log($"[VisitorsPanel] Ship row clicked: {capturedId}");
                OnShipRowClicked?.Invoke(capturedId);
            });

            return row;
        }

        // ── Incoming ship row (non-interactive) ──────────────────────────────────

        private VisualElement BuildIncomingShipRow(ShipInstance ship)
        {
            return BuildBaseShipRow(ship);
        }

        // ── Shared row base ──────────────────────────────────────────────────────

        private VisualElement BuildBaseShipRow(ShipInstance ship)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection  = FlexDirection.Column;
            row.style.paddingTop     = 7;
            row.style.paddingBottom  = 7;
            row.style.paddingLeft    = 8;
            row.style.paddingRight   = 8;
            row.style.marginBottom   = 4;
            row.style.backgroundColor   = new Color(0.16f, 0.20f, 0.27f, 0.9f);
            row.style.borderTopLeftRadius     = 4;
            row.style.borderTopRightRadius    = 4;
            row.style.borderBottomLeftRadius  = 4;
            row.style.borderBottomRightRadius = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.25f, 0.30f, 0.40f, 0.5f);

            // Top line: name + role badge
            var topLine = new VisualElement();
            topLine.style.flexDirection = FlexDirection.Row;
            topLine.style.alignItems    = Align.Center;

            string displayName = string.IsNullOrEmpty(ship.name) ? "Unknown" : ship.name;
            var nameLabel = new Label(displayName);
            nameLabel.AddToClassList(NameClass);
            nameLabel.style.flexGrow               = 1;
            nameLabel.style.fontSize               = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color                  = new Color(0.90f, 0.92f, 0.97f, 1f);
            topLine.Add(nameLabel);

            // Role badge
            string roleText = RoleBadgeLabel(ship.role);
            var badge = new Label(roleText);
            badge.AddToClassList(BadgeClass);
            badge.style.fontSize         = 9;
            badge.style.paddingTop       = 2;
            badge.style.paddingBottom    = 2;
            badge.style.paddingLeft      = 5;
            badge.style.paddingRight     = 5;
            badge.style.marginLeft       = 6;
            badge.style.backgroundColor  = RoleBadgeColor(ship.role);
            badge.style.borderTopLeftRadius     = 3;
            badge.style.borderTopRightRadius    = 3;
            badge.style.borderBottomLeftRadius  = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.color            = new Color(1f, 1f, 1f, 1f);
            badge.style.unityTextAlign   = TextAnchor.MiddleCenter;
            topLine.Add(badge);

            row.Add(topLine);

            // Faction detail line
            if (!string.IsNullOrEmpty(ship.factionId))
            {
                var factionLabel = new Label(ship.factionId);
                factionLabel.AddToClassList(DetailClass);
                factionLabel.style.fontSize   = 10;
                factionLabel.style.color      = new Color(0.65f, 0.72f, 0.85f, 1f);
                factionLabel.style.paddingTop = 2;
                row.Add(factionLabel);
            }

            return row;
        }

        // ── Comms log section ────────────────────────────────────────────────────

        private VisualElement BuildCommsLog()
        {
            var section = new VisualElement();
            section.style.marginTop = 12;

            section.Add(BuildSectionHeader("Comms Log", new Color(0.65f, 0.72f, 0.85f, 1f)));

            if (_station == null || _station.log.Count == 0)
            {
                var empty = new Label("No visitor events recorded.");
                empty.style.fontSize      = 10;
                empty.style.color         = new Color(0.50f, 0.55f, 0.65f, 1f);
                empty.style.paddingTop    = 4;
                section.Add(empty);
                return section;
            }

            int shown = 0;
            foreach (var entry in _station.log)
            {
                if (shown >= 20) break;
                if (!IsVisitorLogEntry(entry)) continue;

                var row = new Label(entry);
                row.AddToClassList(CommsRowClass);
                row.style.fontSize        = 10;
                row.style.color           = new Color(0.60f, 0.68f, 0.78f, 1f);
                row.style.paddingTop      = 2;
                row.style.paddingBottom   = 2;
                row.style.whiteSpace      = WhiteSpace.Normal;
                section.Add(row);
                shown++;
            }

            if (shown == 0)
            {
                var empty = new Label("No visitor events recorded.");
                empty.style.fontSize      = 10;
                empty.style.color         = new Color(0.50f, 0.55f, 0.65f, 1f);
                empty.style.paddingTop    = 4;
                section.Add(empty);
            }

            return section;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when a log entry is visitor-related and should appear in the
        /// comms log section.
        /// </summary>
        public static bool IsVisitorLogEntry(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return false;
            // Case-insensitive scan for common visitor-system keywords.
            string lower = entry.ToLowerInvariant();
            return lower.Contains("incoming")
                || lower.Contains("docked")
                || lower.Contains("dock")
                || lower.Contains("denied")
                || lower.Contains("depart")
                || lower.Contains("trade offer")
                || lower.Contains("inbound")
                || lower.Contains("refugee")
                || lower.Contains("inspection patrol")
                || lower.Contains("hostile")
                || lower.Contains("negotiat")
                || lower.Contains("queuing");
        }

        private static Color RoleBadgeColor(string role) =>
            role switch
            {
                "raider"    => new Color(0.75f, 0.22f, 0.18f, 1f),
                "inspector" => new Color(0.20f, 0.55f, 0.80f, 1f),
                "smuggler"  => new Color(0.60f, 0.35f, 0.70f, 1f),
                "trader"    => new Color(0.25f, 0.55f, 0.35f, 1f),
                "refugee"   => new Color(0.50f, 0.45f, 0.30f, 1f),
                "patrol"    => new Color(0.30f, 0.50f, 0.70f, 1f),
                _           => new Color(0.30f, 0.35f, 0.45f, 1f),
            };
    }
}
