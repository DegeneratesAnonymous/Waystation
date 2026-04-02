// VisitingShipPanelController.cs
// Visiting Ship contextual panel (UI-026).
//
// Displays per-ship inspection across three tabs:
//   Info     — ship name, faction name + reputation indicator, role badge, time
//              remaining at dock, brief faction description.
//   Crew     — list of visitor NPCs (passengerUids); name and current activity
//              (In hangar / At shop / In medical bay / Wandering).  Clicking a
//              row shows a read-only visitor NPC card (name, faction, role).
//   Docking  — docking status; Grant / Deny / Negotiate buttons when a decision
//              is pending; "Open trade manifest" button for Trader ships that
//              have a trade offer; "Request departure" button when docked.
//
// Opened from World → Visitors list via
//   WaystationHUDController.OpenVisitingShipPanelInternal(shipUid).
//
// Data is pushed via Refresh(shipUid, station, visitorSystem, factionSystem).
// Feature-flagged under FeatureFlags.UseUIToolkitHUD.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Visiting Ship contextual panel.  Extends <see cref="VisualElement"/> so it can be
    /// added directly to the content area as a stacking overlay.
    /// </summary>
    public class VisitingShipPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>Fired when the close button is clicked.</summary>
        public event Action OnCloseRequested;

        /// <summary>
        /// Fired when the player clicks "Open trade manifest".
        /// Argument is the ship uid.
        /// </summary>
        public event Action<string> OnOpenTradeManifest;

        /// <summary>Fired when the player clicks Grant for this ship.</summary>
        public event Action<string> OnGrantDocking;

        /// <summary>Fired when the player clicks Deny for this ship.</summary>
        public event Action<string> OnDenyDocking;

        /// <summary>Fired when the player clicks Negotiate for this ship.</summary>
        public event Action<string> OnNegotiateDocking;

        /// <summary>Fired when the player clicks "Request departure".</summary>
        public event Action<string> OnRequestDeparture;

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass      = "ws-visiting-ship-panel";
        private const string HeaderClass     = "ws-visiting-ship-panel__header";
        private const string TitleClass      = "ws-visiting-ship-panel__title";
        private const string CloseBtnClass   = "ws-visiting-ship-panel__close-btn";
        private const string TabContentClass = "ws-visiting-ship-panel__tab-content";
        private const string SectionClass    = "ws-visiting-ship-panel__section";
        private const string BadgeClass      = "ws-visiting-ship-panel__badge";
        private const string NpcRowClass     = "ws-visiting-ship-panel__npc-row";
        private const string NpcCardClass    = "ws-visiting-ship-panel__npc-card";
        private const string ActionBtnClass  = "ws-visiting-ship-panel__action-btn";
        private const string RepMeterBgClass = "ws-visiting-ship-panel__rep-meter-bg";
        private const string RepMeterFillClass = "ws-visiting-ship-panel__rep-meter-fill";

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly TabStrip       _tabs;
        private readonly VisualElement  _tabContent;
        private string                  _activeTab = "info";

        // Cached data for tab rebuilds.
        private string          _shipUid;
        private StationState    _station;
        private VisitorSystem   _visitorSystem;
        private FactionSystem   _factionSystem;

        // Expanded visitor NPC card uid (null = none expanded).
        private string _expandedNpcUid;

        // ── Constructor ────────────────────────────────────────────────────────

        public VisitingShipPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection   = FlexDirection.Column;
            style.flexGrow        = 0;
            style.flexShrink      = 0;
            style.width           = 320;
            style.position        = Position.Absolute;
            style.right           = 0;
            style.top             = 0;
            style.bottom          = 0;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.97f);
            style.borderLeftWidth = 1;
            style.borderLeftColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

            // ── Header ──────────────────────────────────────────────────────────
            var header = new VisualElement();
            header.AddToClassList(HeaderClass);
            header.style.flexDirection     = FlexDirection.Row;
            header.style.alignItems        = Align.Center;
            header.style.paddingLeft       = 10;
            header.style.paddingRight      = 6;
            header.style.paddingTop        = 8;
            header.style.paddingBottom     = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

            var titleLabel = new Label("Visiting Ship");
            titleLabel.name = "ship-title";
            titleLabel.AddToClassList(TitleClass);
            titleLabel.style.flexGrow   = 1;
            titleLabel.style.fontSize   = 15;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color      = new Color(0.90f, 0.92f, 0.97f, 1f);
            header.Add(titleLabel);

            var closeBtn = new Button(() => OnCloseRequested?.Invoke()) { text = "✕" };
            closeBtn.AddToClassList(CloseBtnClass);
            closeBtn.style.backgroundColor  = Color.clear;
            closeBtn.style.borderTopWidth   = 0;
            closeBtn.style.borderRightWidth  = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth   = 0;
            closeBtn.style.color             = new Color(0.7f, 0.7f, 0.75f);
            closeBtn.style.fontSize          = 14;
            header.Add(closeBtn);

            Add(header);

            // ── Tab content area — create BEFORE AddTab calls ──────────────────
            _tabContent = new ScrollView(ScrollViewMode.Vertical);
            _tabContent.AddToClassList(TabContentClass);
            _tabContent.style.flexGrow = 1;
            _tabContent.style.overflow = Overflow.Hidden;

            // ── Tab strip ──────────────────────────────────────────────────────
            _tabs = new TabStrip(TabStrip.Orientation.Horizontal);
            _tabs.OnTabSelected += OnTabSelected;
            _tabs.AddTab("INFO",    "info");
            _tabs.AddTab("CREW",    "crew");
            _tabs.AddTab("DOCKING", "docking");

            Add(_tabs);
            Add(_tabContent);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the panel with new data.
        /// Call on mount and again on every relevant tick.
        /// </summary>
        public void Refresh(
            string shipUid,
            StationState station,
            VisitorSystem visitorSystem,
            FactionSystem factionSystem)
        {
            _shipUid       = shipUid;
            _station       = station;
            _visitorSystem = visitorSystem;
            _factionSystem = factionSystem;

            // Update the header title.
            var title = this.Q<Label>("ship-title");
            if (title != null && _station != null &&
                _station.ships.TryGetValue(_shipUid ?? "", out var ship))
                title.text = ship.name ?? "Visiting Ship";

            RebuildActiveTab();
        }

        // ── Role badge label (public for tests) ──────────────────────────────

        /// <summary>Returns the display label for a ship role string.</summary>
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

        // ── Activity label (public for tests) ────────────────────────────────

        /// <summary>
        /// Maps an NPC location string to a visitor activity label.
        /// </summary>
        public static string ActivityLabel(string location)
        {
            if (string.IsNullOrEmpty(location)) return "Wandering";
            var loc = location.ToLowerInvariant();
            if (loc.Contains("hangar") || loc.Contains("dock"))  return "In hangar";
            if (loc.Contains("shop")   || loc.Contains("trade")) return "At shop";
            if (loc.Contains("med"))                              return "In medical bay";
            return "Wandering";
        }

        // ── Tab selection ─────────────────────────────────────────────────────

        private void OnTabSelected(string tabId)
        {
            _activeTab = tabId;
            RebuildActiveTab();
        }

        private void RebuildActiveTab()
        {
            _tabContent.Clear();

            if (_station == null || string.IsNullOrEmpty(_shipUid)) return;

            if (!_station.ships.TryGetValue(_shipUid, out var ship))
            {
                // Ship is no longer in the station state (departed or removed).
                var missingLabel = new Label("Ship not found. It may have departed.");
                missingLabel.style.paddingLeft    = 10;
                missingLabel.style.paddingTop     = 20;
                missingLabel.style.fontSize       = 12;
                missingLabel.style.color          = new Color(0.55f, 0.60f, 0.70f, 1f);
                missingLabel.style.whiteSpace     = WhiteSpace.Normal;
                missingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                _tabContent.Add(missingLabel);
                return;
            }

            switch (_activeTab)
            {
                case "info":    BuildInfoTab(ship);    break;
                case "crew":    BuildCrewTab(ship);    break;
                case "docking": BuildDockingTab(ship); break;
            }
        }

        // ── Info tab ──────────────────────────────────────────────────────────

        private void BuildInfoTab(ShipInstance ship)
        {
            var scroll = _tabContent;

            // ── Ship name + role badge ──────────────────────────────────────────
            var nameRow = new VisualElement();
            nameRow.style.flexDirection  = FlexDirection.Row;
            nameRow.style.alignItems     = Align.Center;
            nameRow.style.paddingTop     = 10;
            nameRow.style.paddingLeft    = 10;
            nameRow.style.paddingRight   = 10;
            nameRow.style.marginBottom   = 6;

            var nameLabel = new Label(ship.name ?? "Unknown Ship");
            nameLabel.style.flexGrow        = 1;
            nameLabel.style.fontSize        = 14;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color           = new Color(0.90f, 0.92f, 0.97f, 1f);
            nameRow.Add(nameLabel);

            var roleBadge = MakeBadge(RoleBadgeLabel(ship.role), new Color(0.25f, 0.32f, 0.50f, 0.9f));
            nameRow.Add(roleBadge);
            scroll.Add(nameRow);

            // ── Faction name + rep meter ────────────────────────────────────────
            string factionId = ship.factionId;
            FactionDefinition factionDef = null;
            float rep = 0f;
            if (!string.IsNullOrEmpty(factionId) && _factionSystem != null)
            {
                factionDef = _factionSystem.GetFaction(factionId, _station);
                rep = _factionSystem.GetReputation(factionId, _station);
            }

            var factionRow = new VisualElement();
            factionRow.style.flexDirection = FlexDirection.Row;
            factionRow.style.alignItems    = Align.Center;
            factionRow.style.paddingLeft   = 10;
            factionRow.style.paddingRight  = 10;
            factionRow.style.marginBottom  = 4;

            string factionName = factionDef?.displayName ?? factionId ?? "Unknown Faction";
            var factionLabel = new Label(factionName);
            factionLabel.style.flexGrow  = 1;
            factionLabel.style.fontSize  = 12;
            factionLabel.style.color     = new Color(0.70f, 0.75f, 0.85f, 1f);
            factionRow.Add(factionLabel);

            // Rep tier badge
            string repTier = RepTierLabel(rep);
            var repBadge = MakeBadge(repTier, RepTierBadgeColor(repTier));
            factionRow.Add(repBadge);

            scroll.Add(factionRow);

            // Rep meter bar
            if (!string.IsNullOrEmpty(factionId))
            {
                var meterRow = new VisualElement();
                meterRow.style.paddingLeft    = 10;
                meterRow.style.paddingRight   = 10;
                meterRow.style.marginBottom   = 10;

                var meterBg = new VisualElement();
                meterBg.AddToClassList(RepMeterBgClass);
                meterBg.style.height              = 5;
                meterBg.style.backgroundColor     = new Color(0.15f, 0.18f, 0.25f, 1f);
                meterBg.style.borderTopLeftRadius     = 3;
                meterBg.style.borderTopRightRadius    = 3;
                meterBg.style.borderBottomLeftRadius  = 3;
                meterBg.style.borderBottomRightRadius = 3;
                meterBg.style.overflow = Overflow.Hidden;

                float fillPct = Mathf.Clamp01((rep + 100f) / 200f);
                var meterFill = new VisualElement();
                meterFill.AddToClassList(RepMeterFillClass);
                meterFill.style.width           = Length.Percent(fillPct * 100f);
                meterFill.style.height          = Length.Percent(100);
                meterFill.style.backgroundColor = RepMeterColor(rep);
                meterBg.Add(meterFill);
                meterRow.Add(meterBg);
                scroll.Add(meterRow);
            }

            // ── Divider ────────────────────────────────────────────────────────
            scroll.Add(MakeDivider());

            // ── Status / time remaining ─────────────────────────────────────────
            var statusSection = new VisualElement();
            statusSection.style.paddingLeft   = 10;
            statusSection.style.paddingRight  = 10;
            statusSection.style.paddingTop    = 6;
            statusSection.style.paddingBottom = 6;

            var statusLabel = new Label($"Status:  {CapitaliseFirst(ship.status)}");
            statusLabel.style.fontSize = 12;
            statusLabel.style.color    = new Color(0.75f, 0.78f, 0.88f, 1f);
            statusSection.Add(statusLabel);

            var intentLabel = new Label($"Intent:  {CapitaliseFirst(ship.intent)}");
            intentLabel.style.fontSize = 12;
            intentLabel.style.color    = new Color(0.75f, 0.78f, 0.88f, 1f);
            statusSection.Add(intentLabel);

            // Time remaining at dock (only when docked and departure planned).
            if (ship.status == "docked" && ship.plannedDepartureTick > 0)
            {
                int ticksLeft = Mathf.Max(0, ship.plannedDepartureTick - _station.tick);
                var timeLabel = new Label($"Departs in:  {ticksLeft} tick{(ticksLeft == 1 ? "" : "s")}");
                timeLabel.style.fontSize = 12;
                timeLabel.style.color    = new Color(0.75f, 0.78f, 0.88f, 1f);
                statusSection.Add(timeLabel);
            }

            var threatLabel = new Label($"Threat:  {CapitaliseFirst(ship.ThreatLabel())}");
            threatLabel.style.fontSize = 12;
            threatLabel.style.color    = ThreatColor(ship.ThreatLabel());
            statusSection.Add(threatLabel);

            scroll.Add(statusSection);

            // ── Faction description ─────────────────────────────────────────────
            if (!string.IsNullOrEmpty(factionDef?.description))
            {
                scroll.Add(MakeDivider());
                var descSection = new VisualElement();
                descSection.style.paddingLeft   = 10;
                descSection.style.paddingRight  = 10;
                descSection.style.paddingTop    = 6;
                descSection.style.paddingBottom = 10;

                var descLabel = new Label(factionDef.description);
                descLabel.style.fontSize    = 11;
                descLabel.style.color       = new Color(0.60f, 0.65f, 0.75f, 1f);
                descLabel.style.whiteSpace  = WhiteSpace.Normal;
                descSection.Add(descLabel);
                scroll.Add(descSection);
            }
        }

        // ── Crew tab ──────────────────────────────────────────────────────────

        private void BuildCrewTab(ShipInstance ship)
        {
            if (ship.passengerUids == null || ship.passengerUids.Count == 0)
            {
                var empty = new Label("No visitor crew aboard.");
                empty.style.paddingLeft    = 10;
                empty.style.paddingTop     = 20;
                empty.style.fontSize       = 12;
                empty.style.color          = new Color(0.55f, 0.60f, 0.70f, 1f);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                _tabContent.Add(empty);
                return;
            }

            foreach (string npcUid in ship.passengerUids)
            {
                _station.npcs.TryGetValue(npcUid, out var npc);
                string npcName     = npc?.name ?? "Unknown";
                string activity    = ActivityLabel(npc?.location);

                var row = new VisualElement();
                row.AddToClassList(NpcRowClass);
                row.style.flexDirection  = FlexDirection.Column;
                row.style.paddingTop     = 6;
                row.style.paddingBottom  = 6;
                row.style.paddingLeft    = 10;
                row.style.paddingRight   = 10;
                row.style.marginBottom   = 2;
                row.style.cursor         = new StyleCursor(StyleKeyword.Auto);

                var topLine = new VisualElement();
                topLine.style.flexDirection = FlexDirection.Row;
                topLine.style.alignItems    = Align.Center;

                var npcNameLabel = new Label(npcName);
                npcNameLabel.style.flexGrow = 1;
                npcNameLabel.style.fontSize = 12;
                npcNameLabel.style.color    = new Color(0.85f, 0.88f, 0.95f, 1f);
                topLine.Add(npcNameLabel);

                var actLabel = new Label(activity);
                actLabel.style.fontSize       = 10;
                actLabel.style.color          = new Color(0.55f, 0.65f, 0.75f, 1f);
                actLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                topLine.Add(actLabel);

                row.Add(topLine);

                // Expanded NPC card (toggled on click).
                string capturedUid = npcUid;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _expandedNpcUid = (_expandedNpcUid == capturedUid) ? null : capturedUid;
                    RebuildActiveTab();
                });

                _tabContent.Add(row);

                if (_expandedNpcUid == npcUid && npc != null)
                {
                    var card = BuildNpcCard(npc);
                    _tabContent.Add(card);
                }
            }
        }

        private VisualElement BuildNpcCard(NPCInstance npc)
        {
            var card = new VisualElement();
            card.AddToClassList(NpcCardClass);
            card.style.marginLeft        = 10;
            card.style.marginRight       = 10;
            card.style.marginBottom      = 6;
            card.style.paddingTop        = 8;
            card.style.paddingBottom     = 8;
            card.style.paddingLeft       = 10;
            card.style.paddingRight      = 10;
            card.style.backgroundColor   = new Color(0.10f, 0.13f, 0.20f, 0.95f);
            card.style.borderTopLeftRadius     = 4;
            card.style.borderTopRightRadius    = 4;
            card.style.borderBottomLeftRadius  = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.borderTopWidth    = 1;
            card.style.borderTopColor    = new Color(0.25f, 0.30f, 0.45f, 0.6f);

            var nameLabel = new Label(npc.name ?? "Unknown");
            nameLabel.style.fontSize = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color    = new Color(0.90f, 0.92f, 0.97f, 1f);
            nameLabel.style.marginBottom = 4;
            card.Add(nameLabel);

            string factionName = npc.factionId ?? "—";
            if (_factionSystem != null && !string.IsNullOrEmpty(npc.factionId))
            {
                var def = _factionSystem.GetFaction(npc.factionId, _station);
                if (def != null) factionName = def.displayName ?? npc.factionId;
            }

            AddCardRow(card, "Faction", factionName);
            AddCardRow(card, "Role",    npc.classId ?? "Visitor");
            AddCardRow(card, "Status",  "Visitor (read-only)");

            return card;
        }

        private static void AddCardRow(VisualElement card, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label($"{label}: ");
            lbl.style.fontSize = 11;
            lbl.style.color    = new Color(0.55f, 0.62f, 0.75f, 1f);
            row.Add(lbl);

            var val = new Label(value ?? "—");
            val.style.fontSize = 11;
            val.style.color    = new Color(0.80f, 0.83f, 0.92f, 1f);
            row.Add(val);

            card.Add(row);
        }

        // ── Docking tab ───────────────────────────────────────────────────────

        private void BuildDockingTab(ShipInstance ship)
        {
            bool isPending = _visitorSystem != null &&
                             _visitorSystem.PendingDecisions.Contains(ship.uid);

            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 10;
            section.style.paddingBottom = 10;

            // ── Docking status ──────────────────────────────────────────────────
            var statusLabel = new Label($"Docking status:  {CapitaliseFirst(ship.status)}");
            statusLabel.style.fontSize    = 13;
            statusLabel.style.color       = new Color(0.85f, 0.88f, 0.95f, 1f);
            statusLabel.style.marginBottom = 8;
            section.Add(statusLabel);

            // ── Decision buttons (pending) ──────────────────────────────────────
            if (isPending)
            {
                var pendingLabel = new Label("Decision required:");
                pendingLabel.style.fontSize    = 11;
                pendingLabel.style.color       = new Color(1.0f, 0.85f, 0.30f, 1f);
                pendingLabel.style.marginBottom = 6;
                section.Add(pendingLabel);

                var btnRow = new VisualElement();
                btnRow.style.flexDirection = FlexDirection.Row;
                btnRow.style.flexWrap      = Wrap.NoWrap;
                btnRow.style.marginBottom  = 10;

                var grantBtn = MakeActionButton("Grant",     new Color(0.20f, 0.55f, 0.25f, 1f));
                var denyBtn  = MakeActionButton("Deny",      new Color(0.60f, 0.20f, 0.20f, 1f));
                var negBtn   = MakeActionButton("Negotiate", new Color(0.25f, 0.35f, 0.60f, 1f));

                string capturedUid = ship.uid;
                grantBtn.RegisterCallback<ClickEvent>(_ => OnGrantDocking?.Invoke(capturedUid));
                denyBtn.RegisterCallback<ClickEvent>(  _ => OnDenyDocking?.Invoke(capturedUid));
                negBtn.RegisterCallback<ClickEvent>(   _ => OnNegotiateDocking?.Invoke(capturedUid));

                btnRow.Add(grantBtn);
                btnRow.Add(denyBtn);
                btnRow.Add(negBtn);
                section.Add(btnRow);
            }

            // ── Trade manifest button (Trader ships with a trade offer) ─────────
            bool hasTradeOffer = _station?.tradeOffers.ContainsKey(ship.uid) == true;
            if (ship.role == "trader" && hasTradeOffer)
            {
                var tradeBtn = MakeActionButton("Open trade manifest", new Color(0.25f, 0.45f, 0.70f, 1f));
                tradeBtn.name = "btn-trade-manifest";
                tradeBtn.style.marginBottom = 6;
                string capturedUid = ship.uid;
                tradeBtn.RegisterCallback<ClickEvent>(_ => OnOpenTradeManifest?.Invoke(capturedUid));
                section.Add(tradeBtn);
            }

            // ── Request departure button (docked ships) ─────────────────────────
            if (ship.status == "docked")
            {
                var departBtn = MakeActionButton("Request departure", new Color(0.40f, 0.40f, 0.55f, 1f));
                departBtn.name = "btn-request-departure";
                departBtn.style.marginBottom = 6;
                string capturedUid = ship.uid;
                departBtn.RegisterCallback<ClickEvent>(_ => OnRequestDeparture?.Invoke(capturedUid));
                section.Add(departBtn);
            }

            // ── Contraband scan summary (if Inspector is docked) ────────────────
            bool isInspector = ship.role == "inspector";
            bool inspectionActive = _station?.HasTag("inspection_in_progress") == true;
            if (isInspector && inspectionActive)
            {
                section.Add(MakeDivider());
                var inspLabel = new Label("Inspection in progress.");
                inspLabel.style.fontSize    = 11;
                inspLabel.style.color       = new Color(0.90f, 0.75f, 0.25f, 1f);
                inspLabel.style.marginTop   = 6;
                section.Add(inspLabel);

                var contrabandNote = new Label(
                    $"Detection chance: {VisitorSystem.ContrabandDetectionChance * 100f:F0}%  " +
                    $"Difficulty: {VisitorSystem.ContrabandBaseDifficulty}");
                contrabandNote.style.fontSize = 11;
                contrabandNote.style.color    = new Color(0.65f, 0.68f, 0.78f, 1f);
                contrabandNote.style.marginTop = 2;
                section.Add(contrabandNote);
            }

            _tabContent.Add(section);
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        private Label MakeBadge(string text, Color bgColor)
        {
            var badge = new Label(text);
            badge.AddToClassList(BadgeClass);
            badge.style.fontSize         = 9;
            badge.style.paddingTop       = 2;
            badge.style.paddingBottom    = 2;
            badge.style.paddingLeft      = 5;
            badge.style.paddingRight     = 5;
            badge.style.marginLeft       = 6;
            badge.style.backgroundColor  = bgColor;
            badge.style.borderTopLeftRadius     = 3;
            badge.style.borderTopRightRadius    = 3;
            badge.style.borderBottomLeftRadius  = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.color            = new Color(0.92f, 0.94f, 1.0f, 1f);
            badge.style.unityTextAlign   = TextAnchor.MiddleCenter;
            return badge;
        }

        private Button MakeActionButton(string text, Color bgColor)
        {
            var btn = new Button { text = text };
            btn.AddToClassList(ActionBtnClass);
            btn.style.backgroundColor    = bgColor;
            btn.style.color              = new Color(0.92f, 0.95f, 1.0f, 1f);
            btn.style.fontSize           = 11;
            btn.style.paddingTop         = 5;
            btn.style.paddingBottom      = 5;
            btn.style.paddingLeft        = 10;
            btn.style.paddingRight       = 10;
            btn.style.marginRight        = 6;
            btn.style.borderTopWidth     = 0;
            btn.style.borderRightWidth   = 0;
            btn.style.borderBottomWidth  = 0;
            btn.style.borderLeftWidth    = 0;
            btn.style.borderTopLeftRadius     = 4;
            btn.style.borderTopRightRadius    = 4;
            btn.style.borderBottomLeftRadius  = 4;
            btn.style.borderBottomRightRadius = 4;
            btn.style.cursor             = new StyleCursor(StyleKeyword.Auto);
            return btn;
        }

        private static VisualElement MakeDivider()
        {
            var div = new VisualElement();
            div.style.height          = 1;
            div.style.backgroundColor = new Color(0.25f, 0.28f, 0.38f, 0.5f);
            div.style.marginTop       = 4;
            div.style.marginBottom    = 4;
            div.style.marginLeft      = 10;
            div.style.marginRight     = 10;
            return div;
        }

        private static string CapitaliseFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        // ── Colour helpers ────────────────────────────────────────────────────

        internal static string RepTierLabel(float rep)
        {
            if (rep >= 75f)  return "Allied";
            if (rep >= 50f)  return "Friendly";
            if (rep >= 0f)   return "Neutral";
            if (rep >= -50f) return "Unfriendly";
            return "Hostile";
        }

        private static Color RepTierBadgeColor(string tier) =>
            tier switch
            {
                "Allied"     => new Color(0.12f, 0.50f, 0.22f, 0.9f),
                "Friendly"   => new Color(0.30f, 0.50f, 0.18f, 0.9f),
                "Neutral"    => new Color(0.48f, 0.43f, 0.10f, 0.9f),
                "Unfriendly" => new Color(0.50f, 0.28f, 0.08f, 0.9f),
                _            => new Color(0.50f, 0.12f, 0.10f, 0.9f), // Hostile
            };

        private static Color RepMeterColor(float rep)
        {
            if (rep >= 75f)  return new Color(0.20f, 0.85f, 0.40f, 1f);
            if (rep >= 50f)  return new Color(0.50f, 0.80f, 0.30f, 1f);
            if (rep >= 0f)   return new Color(0.85f, 0.80f, 0.20f, 1f);
            if (rep >= -50f) return new Color(0.90f, 0.55f, 0.15f, 1f);
            return new Color(0.85f, 0.25f, 0.20f, 1f);
        }

        private static Color ThreatColor(string label) =>
            label switch
            {
                "none"     => new Color(0.55f, 0.60f, 0.70f, 1f),
                "low"      => new Color(0.75f, 0.78f, 0.50f, 1f),
                "moderate" => new Color(0.90f, 0.65f, 0.20f, 1f),
                "high"     => new Color(0.90f, 0.35f, 0.20f, 1f),
                _          => new Color(0.90f, 0.15f, 0.15f, 1f),
            };
    }
}
