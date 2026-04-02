// NetworksSubPanelController.cs
// Station → Networks sub-tab panel (UI-009).
//
// Displays:
//   * Overlay toggle buttons — OFF, ELEC, PLUMB, DUCT, FUEL.
//     The active button reflects UtilityNetworkManager.CurrentOverlay.
//     Clicking a button calls UtilityNetworkManager.SetOverlay(mode) so the
//     tile map updates to show that network's overlay (same result as Tab key).
//   * Network health rows — one per network type (Electrical, Plumbing,
//     Ducting, Fuel Lines) showing:
//       • connected node count
//       • severed connection count  (shown in amber when > 0)
//       • status badge              (HEALTHY / DEGRADED / SEVERED)
//     The Electrical row also shows a battery ResourceMeter.
//
// Data is pushed via Refresh(StationState, UtilityNetworkManager).
// Call on mount and again on every OnTick while the panel is active.
// The panel subscribes to UtilityNetworkManager.OnOverlayChanged to keep
// overlay button active states in sync when the overlay changes via Tab key.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController, which is itself gated by that flag).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Station → Networks sub-tab panel. Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class NetworksSubPanelController : VisualElement
    {
        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass        = "ws-networks-panel";
        private const string OverlayStripClass = "ws-networks-panel__overlay-strip";
        private const string OverlayBtnClass   = "ws-networks-panel__overlay-btn";
        private const string OverlayBtnActive  = "ws-networks-panel__overlay-btn--active";
        private const string HealthListClass   = "ws-networks-panel__health-list";
        private const string HealthRowClass    = "ws-networks-panel__health-row";
        private const string HealthNameClass   = "ws-networks-panel__health-name";
        private const string HealthNodesClass  = "ws-networks-panel__health-nodes";
        private const string HealthSevClass    = "ws-networks-panel__health-severed";
        private const string HealthStatusClass = "ws-networks-panel__health-status";

        // ── Network type metadata ──────────────────────────────────────────────

        // (overlay mode, button label, internal network type string, display name)
        private static readonly (OverlayMode mode, string btnLabel, string netType, string displayName)[] s_Networks =
        {
            (OverlayMode.Electrical, "ELEC",  "electric", "Electrical"),
            (OverlayMode.Plumbing,   "PLUMB", "pipe",     "Plumbing"),
            (OverlayMode.Ducting,    "DUCT",  "duct",     "Ducting"),
            (OverlayMode.Fuel,       "FUEL",  "fuel",     "Fuel Lines"),
        };

        // ── Child elements ─────────────────────────────────────────────────────

        private readonly VisualElement _overlayStrip;
        private readonly Label         _summaryLabel;
        private readonly VisualElement _healthList;

        // Overlay button refs keyed by OverlayMode.
        private readonly Dictionary<OverlayMode, Button> _overlayButtons =
            new Dictionary<OverlayMode, Button>();

        // Health row label refs keyed by networkType string.
        private readonly Dictionary<string, (Label nodes, Label severed, Label status)> _healthLabels =
            new Dictionary<string, (Label, Label, Label)>(System.StringComparer.Ordinal);

        // Battery meter displayed below the Electrical health row.
        private readonly ResourceMeter _batteryMeter;

        // ── Manager reference ──────────────────────────────────────────────────

        // Injected by the first Refresh() call; kept so the overlay-change event
        // can be handled even when the overlay changes via Tab key.
        private UtilityNetworkManager _manager;

        // ── Constructor ────────────────────────────────────────────────────────

        public NetworksSubPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // ── Overlay section ────────────────────────────────────────────────
            var overlayLabel = new Label("OVERLAY");
            overlayLabel.AddToClassList("ws-text-acc");
            overlayLabel.style.fontSize                = 10;
            overlayLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            overlayLabel.style.marginBottom            = 4;
            Add(overlayLabel);

            _overlayStrip = new VisualElement();
            _overlayStrip.AddToClassList(OverlayStripClass);
            _overlayStrip.style.flexDirection = FlexDirection.Row;
            _overlayStrip.style.flexWrap      = Wrap.Wrap;
            _overlayStrip.style.paddingBottom = 6;
            _overlayStrip.style.borderBottomWidth = 1;
            _overlayStrip.style.borderBottomColor = new Color(0.09f, 0.12f, 0.17f, 1f);
            _overlayStrip.style.marginBottom  = 12;
            Add(_overlayStrip);

            // "OFF" button first, then one per network type.
            AddOverlayButton(OverlayMode.Off, "OFF");
            foreach (var (mode, label, _, _) in s_Networks)
                AddOverlayButton(mode, label);

            // ── Health section ─────────────────────────────────────────────────
            var healthLabel = new Label("NETWORK HEALTH");
            healthLabel.AddToClassList("ws-text-acc");
            healthLabel.style.fontSize                = 10;
            healthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            healthLabel.style.marginBottom            = 4;
            Add(healthLabel);

            _summaryLabel = new Label("No network data");
            _summaryLabel.style.fontSize    = 9;
            _summaryLabel.style.color       = new Color(0.34f, 0.47f, 0.63f, 1f);
            _summaryLabel.style.marginBottom = 6;
            Add(_summaryLabel);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            _healthList = scroll.contentContainer;
            _healthList.AddToClassList(HealthListClass);
            _healthList.style.flexDirection = FlexDirection.Column;

            // One health row per network type; Electrical row gets an extra battery meter.
            foreach (var (_, _, netType, displayName) in s_Networks)
            {
                var row = BuildHealthRow(netType, displayName,
                    out var nodes, out var severed, out var status);
                _healthLabels[netType] = (nodes, severed, status);
                _healthList.Add(row);

                if (netType == "electric")
                {
                    _batteryMeter = new ResourceMeter(ResourceMeter.ResourceType.Power, "BATTERY");
                    _batteryMeter.style.marginTop    = 4;
                    _batteryMeter.style.marginBottom = 6;
                    _batteryMeter.style.marginLeft   = 6;
                    _batteryMeter.style.marginRight  = 6;
                    _batteryMeter.SetValue(0f);
                    _healthList.Add(_batteryMeter);
                }
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes overlay button active state and network health rows.
        /// Call once on mount and again on every OnTick while the panel is active.
        /// </summary>
        public void Refresh(StationState station, UtilityNetworkManager manager)
        {
            // Wire up (or re-wire) the manager whenever it changes.
            if (manager != _manager)
            {
                if (_manager != null)
                    _manager.OnOverlayChanged -= OnOverlayModeChanged;
                _manager = manager;
                if (_manager != null)
                    _manager.OnOverlayChanged += OnOverlayModeChanged;
            }

            SyncOverlayButtons(_manager?.CurrentOverlay ?? OverlayMode.Off);
            RefreshHealthRows(station, manager);
        }

        /// <summary>
        /// Unsubscribes all event handlers.  Call before the panel is destroyed.
        /// </summary>
        public void Detach()
        {
            if (_manager != null)
            {
                _manager.OnOverlayChanged -= OnOverlayModeChanged;
                _manager = null;
            }
        }

        // ── Overlay buttons ────────────────────────────────────────────────────

        private void AddOverlayButton(OverlayMode mode, string label)
        {
            var btn = new Button();
            btn.AddToClassList(OverlayBtnClass);
            btn.text               = label;
            btn.style.marginRight  = 3;
            btn.style.marginBottom = 3;

            OverlayMode capturedMode = mode;
            btn.RegisterCallback<ClickEvent>(_ => OnOverlayButtonClicked(capturedMode));

            _overlayStrip.Add(btn);
            _overlayButtons[mode] = btn;
        }

        private void OnOverlayButtonClicked(OverlayMode mode)
        {
            _manager?.SetOverlay(mode);
            // SetOverlay is a no-op when mode already matches CurrentOverlay, so
            // OnOverlayChanged won't fire — force a button-state sync regardless.
            SyncOverlayButtons(mode);
        }

        private void OnOverlayModeChanged(OverlayMode mode)
        {
            SyncOverlayButtons(mode);
        }

        private void SyncOverlayButtons(OverlayMode activeMode)
        {
            foreach (var kv in _overlayButtons)
                kv.Value.EnableInClassList(OverlayBtnActive, kv.Key == activeMode);
        }

        // ── Health rows ────────────────────────────────────────────────────────

        private VisualElement BuildHealthRow(string netType, string displayName,
            out Label nodesLabel, out Label severedLabel, out Label statusLabel)
        {
            var row = new VisualElement();
            row.AddToClassList(HealthRowClass);
            row.style.flexDirection     = FlexDirection.Row;
            row.style.justifyContent    = Justify.SpaceBetween;
            row.style.alignItems        = Align.Center;
            row.style.paddingTop        = 5;
            row.style.paddingBottom     = 5;
            row.style.paddingLeft       = 6;
            row.style.paddingRight      = 6;
            row.style.marginBottom      = 2;
            row.style.backgroundColor   = new Color(0.06f, 0.08f, 0.12f, 0.65f);
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.09f, 0.12f, 0.17f, 1f); // border-dark

            var nameLabel = new Label(displayName.ToUpper());
            nameLabel.AddToClassList(HealthNameClass);
            nameLabel.style.flexGrow       = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.fontSize       = 11;
            row.Add(nameLabel);

            nodesLabel = new Label("0 nodes");
            nodesLabel.AddToClassList(HealthNodesClass);
            nodesLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            nodesLabel.style.opacity        = 0.7f;
            nodesLabel.style.minWidth       = 50;
            row.Add(nodesLabel);

            severedLabel = new Label("0 sev");
            severedLabel.AddToClassList(HealthSevClass);
            severedLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            severedLabel.style.minWidth       = 40;
            severedLabel.style.marginLeft     = 4;
            row.Add(severedLabel);

            statusLabel = new Label("HEALTHY");
            statusLabel.AddToClassList(HealthStatusClass);
            statusLabel.style.unityTextAlign          = TextAnchor.MiddleRight;
            statusLabel.style.fontSize      = 9;
            statusLabel.style.paddingLeft   = 5;
            statusLabel.style.paddingRight  = 5;
            statusLabel.style.paddingTop    = 2;
            statusLabel.style.paddingBottom = 2;
            statusLabel.style.marginLeft    = 4;
            ApplyStatusStyle(statusLabel, NetworkHealthStatus.Healthy);
            row.Add(statusLabel);

            return row;
        }

        private void RefreshHealthRows(StationState station, UtilityNetworkManager manager)
        {
            if (station == null || manager == null) return;

            int totalNodes = 0;
            int totalSevered = 0;

            foreach (var (_, _, netType, _) in s_Networks)
            {
                if (!_healthLabels.TryGetValue(netType, out var labels)) continue;

                var health = manager.GetNetworkHealth(station, netType);
                totalNodes += health.ConnectedNodes;
                totalSevered += health.SeveredCount;
                labels.nodes.text   = $"{health.ConnectedNodes} node{(health.ConnectedNodes != 1 ? "s" : "")}";
                labels.severed.text = $"{health.SeveredCount} sev";

                // Amber warning colour on severed count when non-zero.
                labels.severed.style.color = health.SeveredCount > 0
                    ? new Color(0.86f, 0.66f, 0.16f, 1f)  // amber
                    : new Color(0.34f, 0.47f, 0.63f, 1f); // text-mid

                labels.status.text = health.Status switch
                {
                    NetworkHealthStatus.Degraded => "DEGRADED",
                    NetworkHealthStatus.Severed  => "SEVERED",
                    _                            => "HEALTHY",
                };
                ApplyStatusStyle(labels.status, health.Status);

                // Update battery meter for the Electrical row.
                if (netType == "electric" && _batteryMeter != null)
                    _batteryMeter.SetValue(manager.GetBatteryLevel(station));
            }

            _summaryLabel.text = totalSevered > 0
                ? $"{totalNodes} nodes | {totalSevered} severed links"
                : $"{totalNodes} nodes | all links healthy";
        }

        private static void ApplyStatusStyle(Label label, NetworkHealthStatus status)
        {
            switch (status)
            {
                case NetworkHealthStatus.Healthy:
                    label.style.backgroundColor = new Color(0.05f, 0.23f, 0.13f, 0.8f); // green-dim
                    label.style.color           = new Color(0.24f, 0.78f, 0.44f, 1f);   // green
                    break;
                case NetworkHealthStatus.Degraded:
                    label.style.backgroundColor = new Color(0.35f, 0.24f, 0.03f, 0.8f); // amber-dim
                    label.style.color           = new Color(0.86f, 0.66f, 0.16f, 1f);   // amber
                    break;
                case NetworkHealthStatus.Severed:
                    label.style.backgroundColor = new Color(0.35f, 0.06f, 0.06f, 0.8f); // red-dim
                    label.style.color           = new Color(0.88f, 0.22f, 0.22f, 1f);   // red
                    break;
            }
        }
    }
}
