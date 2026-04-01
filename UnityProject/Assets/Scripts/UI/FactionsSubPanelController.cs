// FactionsSubPanelController.cs
// World → Factions sub-tab panel (UI-015).
//
// Displays all known factions with:
//   • Name label
//   • Government type badge (e.g. "Republic", "Pirate")
//   • Reputation meter (−100 to +100) filled bar + tier label
//   • Active faction-contract count
//
// Filter chips:  All | Hostile | Neutral | Friendly
//   All      — shows every faction
//   Hostile  — rep < 0
//   Neutral  — 0 ≤ rep < 50
//   Friendly — rep ≥ 50 (positive / allied)
//
// Reputation tiers (used for tier label and meter colour):
//   rep < −50   → Hostile     (red)
//   −50 ≤ rep < 0  → Unfriendly (orange)
//   0 ≤ rep < 50   → Neutral    (yellow)
//   50 ≤ rep < 75  → Friendly   (light green)
//   rep ≥ 75    → Allied     (bright green)
//
// Clicking a faction row fires OnFactionRowClicked(factionId).
// Call Refresh(StationState, FactionSystem) to sync with live data.
// Subscribe to FactionSystem.OnFactionRepThresholdCrossed and call Refresh
// whenever it fires so the list stays current without polling.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController, which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// World → Factions sub-tab panel.  Extends <see cref="VisualElement"/> so it can
    /// be added directly to the side-panel drawer.
    /// </summary>
    public class FactionsSubPanelController : VisualElement
    {
        // ── Events ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks a faction row.
        /// Argument is the faction id; use it to open the Faction Detail panel.
        /// </summary>
        public event Action<string> OnFactionRowClicked;

        // ── USS class names ──────────────────────────────────────────────────────

        private const string PanelClass       = "ws-factions-panel";
        private const string FilterBarClass   = "ws-factions-panel__filter-bar";
        private const string FilterChipClass  = "ws-factions-panel__filter-chip";
        private const string FilterActiveClass = "ws-factions-panel__filter-chip--active";
        private const string RowClass         = "ws-factions-panel__faction-row";
        private const string BadgeClass       = "ws-factions-panel__gov-badge";
        private const string NameClass        = "ws-factions-panel__faction-name";
        private const string MeterBgClass     = "ws-factions-panel__rep-meter-bg";
        private const string MeterFillClass   = "ws-factions-panel__rep-meter-fill";
        private const string TierLabelClass   = "ws-factions-panel__tier-label";
        private const string ContractsClass   = "ws-factions-panel__contracts";

        // ── Filter state ─────────────────────────────────────────────────────────

        /// <summary>Active filter key: "all" | "hostile" | "neutral" | "friendly".</summary>
        public string ActiveFilter { get; private set; } = "all";

        // ── Internal state ───────────────────────────────────────────────────────

        private readonly ScrollView _scroll;
        private readonly VisualElement _listRoot;
        private readonly Dictionary<string, Label> _filterChips =
            new Dictionary<string, Label>(StringComparer.Ordinal);

        private StationState  _station;
        private FactionSystem _factionSystem;

        // ── Constructor ──────────────────────────────────────────────────────────

        public FactionsSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // ── Filter chips bar ────────────────────────────────────────────────
            var filterBar = new VisualElement();
            filterBar.AddToClassList(FilterBarClass);
            filterBar.style.flexDirection  = FlexDirection.Row;
            filterBar.style.flexWrap       = Wrap.NoWrap;
            filterBar.style.marginBottom   = 8;

            foreach (var (key, label) in new[] {
                ("all",      "All"),
                ("hostile",  "Hostile"),
                ("neutral",  "Neutral"),
                ("friendly", "Friendly"),
            })
            {
                var chip = BuildFilterChip(key, label);
                _filterChips[key] = chip;
                filterBar.Add(chip);
            }
            Add(filterBar);

            // ── Scrollable list ─────────────────────────────────────────────────
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            Add(_scroll);

            _listRoot = _scroll.contentContainer;
            _listRoot.style.flexDirection = FlexDirection.Column;

            ApplyFilterHighlight();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the faction list from live station data.
        /// Call on mount and again whenever <see cref="FactionSystem.OnFactionRepThresholdCrossed"/> fires.
        /// </summary>
        public void Refresh(StationState station, FactionSystem factionSystem)
        {
            _station       = station;
            _factionSystem = factionSystem;
            RebuildList();
        }

        // ── Tier helpers (public so tests can access them) ───────────────────────

        /// <summary>
        /// Returns the reputation tier label for the given rep value.
        /// Boundaries: −50, 0, 50, 75.
        /// </summary>
        public static string RepTierLabel(float rep)
        {
            if (rep >= 75f)  return "Allied";
            if (rep >= 50f)  return "Friendly";
            if (rep >= 0f)   return "Neutral";
            if (rep >= -50f) return "Unfriendly";
            return "Hostile";
        }

        /// <summary>
        /// Returns the filter bucket for the given rep value.
        /// hostile = rep below 0 · neutral = 0 to 49 · friendly = 50 and above.
        /// </summary>
        public static string RepTierFilter(float rep)
        {
            if (rep >= 50f) return "friendly";
            if (rep >= 0f)  return "neutral";
            return "hostile";
        }

        // ── Internal: list rebuild ───────────────────────────────────────────────

        private void RebuildList()
        {
            _listRoot.Clear();

            if (_station == null) return;

            // When a FactionSystem is available, use it to merge registry + generated factions.
            // When it is null (e.g. in tests), fall back to generated factions only.
            Dictionary<string, FactionDefinition> allFactions = _factionSystem != null
                ? _factionSystem.GetAllFactions(_station)
                : FactionSystem.MergeAllFactions(
                      new Dictionary<string, FactionDefinition>(), _station);

            // Sort by display name for a stable, deterministic order.
            var sorted = new List<KeyValuePair<string, FactionDefinition>>(allFactions);
            sorted.Sort((a, b) =>
                string.Compare(
                    a.Value.displayName ?? a.Key,
                    b.Value.displayName ?? b.Key,
                    StringComparison.OrdinalIgnoreCase));

            bool anyVisible = false;
            foreach (var kv in sorted)
            {
                float rep = _station.GetFactionRep(kv.Key);

                // Apply active filter.
                if (ActiveFilter != "all")
                {
                    string bucket = RepTierFilter(rep);
                    if (bucket != ActiveFilter) continue;
                }

                _listRoot.Add(BuildFactionRow(kv.Key, kv.Value, rep));
                anyVisible = true;
            }

            if (!anyVisible)
            {
                var empty = new Label("No factions match the current filter.");
                empty.style.color          = new Color(0.6f, 0.65f, 0.75f, 1f);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.paddingTop     = 20;
                empty.style.fontSize       = 11;
                _listRoot.Add(empty);
            }
        }

        // ── Row builder ──────────────────────────────────────────────────────────

        private VisualElement BuildFactionRow(
            string factionId, FactionDefinition def, float rep)
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
            row.style.cursor = new StyleCursor(MouseCursor.Link);

            // ── Top line: name + government badge + contracts ─────────────────
            var topLine = new VisualElement();
            topLine.style.flexDirection = FlexDirection.Row;
            topLine.style.alignItems    = Align.Center;
            topLine.style.marginBottom  = 5;

            var nameLabel = new Label(def.displayName ?? factionId);
            nameLabel.AddToClassList(NameClass);
            nameLabel.style.flexGrow        = 1;
            nameLabel.style.fontSize        = 13;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color           = new Color(0.90f, 0.92f, 0.97f, 1f);
            topLine.Add(nameLabel);

            // Government badge
            var badge = new Label(GovernmentTypeLabel(def.governmentType));
            badge.AddToClassList(BadgeClass);
            badge.style.fontSize         = 9;
            badge.style.paddingTop       = 2;
            badge.style.paddingBottom    = 2;
            badge.style.paddingLeft      = 5;
            badge.style.paddingRight     = 5;
            badge.style.marginLeft       = 6;
            badge.style.backgroundColor  = new Color(0.25f, 0.30f, 0.42f, 0.9f);
            badge.style.borderTopLeftRadius     = 3;
            badge.style.borderTopRightRadius    = 3;
            badge.style.borderBottomLeftRadius  = 3;
            badge.style.borderBottomRightRadius = 3;
            badge.style.color            = new Color(0.75f, 0.80f, 0.95f, 1f);
            badge.style.unityTextAlign   = TextAnchor.MiddleCenter;
            topLine.Add(badge);

            // Active contracts count
            int contracts = CountActiveContracts(factionId);
            if (contracts > 0)
            {
                var contractsLabel = new Label($"{contracts} contract{(contracts == 1 ? "" : "s")}");
                contractsLabel.AddToClassList(ContractsClass);
                contractsLabel.style.fontSize      = 9;
                contractsLabel.style.paddingLeft   = 8;
                contractsLabel.style.color         = new Color(0.65f, 0.80f, 0.65f, 1f);
                contractsLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                topLine.Add(contractsLabel);
            }

            row.Add(topLine);

            // ── Bottom line: reputation meter + tier label ────────────────────
            var meterRow = new VisualElement();
            meterRow.style.flexDirection = FlexDirection.Row;
            meterRow.style.alignItems    = Align.Center;

            var meterBg = new VisualElement();
            meterBg.AddToClassList(MeterBgClass);
            meterBg.style.flexGrow            = 1;
            meterBg.style.height              = 6;
            meterBg.style.backgroundColor     = new Color(0.15f, 0.18f, 0.25f, 1f);
            meterBg.style.borderTopLeftRadius     = 3;
            meterBg.style.borderTopRightRadius    = 3;
            meterBg.style.borderBottomLeftRadius  = 3;
            meterBg.style.borderBottomRightRadius = 3;
            meterBg.style.overflow = Overflow.Hidden;

            float fillPct = Mathf.Clamp01((rep + 100f) / 200f);
            var meterFill = new VisualElement();
            meterFill.AddToClassList(MeterFillClass);
            meterFill.style.width             = Length.Percent(fillPct * 100f);
            meterFill.style.height            = Length.Percent(100);
            meterFill.style.backgroundColor   = RepMeterColor(rep);
            meterBg.Add(meterFill);

            meterRow.Add(meterBg);

            string tier = RepTierLabel(rep);
            var tierLabel = new Label($"  {rep:+0;−0;0}  {tier}");
            tierLabel.AddToClassList(TierLabelClass);
            tierLabel.style.fontSize       = 10;
            tierLabel.style.color          = RepTierColor(tier);
            tierLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            tierLabel.style.minWidth       = 110;
            meterRow.Add(tierLabel);

            row.Add(meterRow);

            // ── Click handler ─────────────────────────────────────────────────
            string capturedId = factionId;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                Debug.Log($"[FactionsPanel] Faction row clicked: {capturedId}");
                OnFactionRowClicked?.Invoke(capturedId);
            });

            return row;
        }

        // ── Filter chip builder ──────────────────────────────────────────────────

        private Label BuildFilterChip(string filterKey, string labelText)
        {
            var chip = new Label(labelText);
            chip.AddToClassList(FilterChipClass);
            chip.style.fontSize      = 10;
            chip.style.paddingTop    = 3;
            chip.style.paddingBottom = 3;
            chip.style.paddingLeft   = 8;
            chip.style.paddingRight  = 8;
            chip.style.marginRight   = 5;
            chip.style.borderTopLeftRadius     = 10;
            chip.style.borderTopRightRadius    = 10;
            chip.style.borderBottomLeftRadius  = 10;
            chip.style.borderBottomRightRadius = 10;
            chip.style.unityTextAlign = TextAnchor.MiddleCenter;
            chip.style.cursor         = new StyleCursor(MouseCursor.Link);
            chip.focusable            = true;

            string capturedKey = filterKey;
            chip.RegisterCallback<ClickEvent>(_ => SetFilter(capturedKey));
            chip.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                {
                    SetFilter(capturedKey);
                    evt.StopPropagation();
                }
            });

            return chip;
        }

        private void SetFilter(string filterKey)
        {
            if (ActiveFilter == filterKey) return;
            ActiveFilter = filterKey;
            ApplyFilterHighlight();
            RebuildList();
        }

        private void ApplyFilterHighlight()
        {
            foreach (var kv in _filterChips)
            {
                bool active = kv.Key == ActiveFilter;
                kv.Value.EnableInClassList(FilterActiveClass, active);
                kv.Value.style.backgroundColor = active
                    ? new Color(0.30f, 0.40f, 0.60f, 1f)
                    : new Color(0.20f, 0.24f, 0.32f, 0.8f);
                kv.Value.style.color = active
                    ? new Color(1.0f, 1.0f, 1.0f, 1f)
                    : new Color(0.70f, 0.75f, 0.85f, 1f);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private int CountActiveContracts(string factionId)
        {
            if (_station == null || string.IsNullOrEmpty(factionId)) return 0;
            int count = 0;
            foreach (var kv in _station.factionContracts)
            {
                if (kv.Value != null &&
                    string.Equals(kv.Value.factionId, factionId, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static string GovernmentTypeLabel(GovernmentType govType) =>
            govType switch
            {
                GovernmentType.Democracy      => "Democracy",
                GovernmentType.Republic       => "Republic",
                GovernmentType.Monarchy       => "Monarchy",
                GovernmentType.Authoritarian  => "Authoritarian",
                GovernmentType.CorporateVassal => "Corp. Vassal",
                GovernmentType.Pirate         => "Pirate",
                GovernmentType.Theocracy      => "Theocracy",
                GovernmentType.Technocracy    => "Technocracy",
                GovernmentType.FederalCouncil => "Fed. Council",
                _                             => govType.ToString(),
            };

        private static Color RepMeterColor(float rep)
        {
            if (rep >= 75f)  return new Color(0.20f, 0.85f, 0.40f, 1f);   // bright green — Allied
            if (rep >= 50f)  return new Color(0.50f, 0.80f, 0.30f, 1f);   // light green — Friendly
            if (rep >= 0f)   return new Color(0.85f, 0.80f, 0.20f, 1f);   // yellow — Neutral
            if (rep >= -50f) return new Color(0.90f, 0.55f, 0.15f, 1f);   // orange — Unfriendly
            return new Color(0.85f, 0.25f, 0.20f, 1f);                     // red — Hostile
        }

        private static Color RepTierColor(string tier) =>
            tier switch
            {
                "Allied"      => new Color(0.20f, 0.85f, 0.40f, 1f),
                "Friendly"    => new Color(0.50f, 0.80f, 0.30f, 1f),
                "Neutral"     => new Color(0.85f, 0.80f, 0.20f, 1f),
                "Unfriendly"  => new Color(0.90f, 0.55f, 0.15f, 1f),
                _             => new Color(0.85f, 0.25f, 0.20f, 1f),       // Hostile
            };
    }
}
