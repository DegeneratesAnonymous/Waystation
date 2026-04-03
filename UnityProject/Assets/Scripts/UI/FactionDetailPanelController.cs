// FactionDetailPanelController.cs
// Faction Detail contextual panel (UI-026).
//
// Slide-in-from-right panel opened from World → Factions list.
// Displays:
//   • Faction name, territory label (type: minor / regional / major), home sector
//   • Government type badge with axis position labels (Power Distribution / Legitimacy)
//   • Stability bar (0–100) with expandable contributing-factor breakdown:
//       economic prosperity, military strength, mood/cohesion, tenure
//   • Reputation meter with tier label and per-change history (last 5 changes)
//   • Active contracts list
//   • Recent faction history log (last 10 events from IFactionHistoryProvider)
//   • Vassal/patron indicator: if CorporateVassal, shows patron faction name
//     and reputation bleed note
//
// Data is pushed via Refresh(factionId, station, factionSystem, factionHistoryProvider).
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
    /// Faction Detail contextual panel.  Extends <see cref="VisualElement"/> so it can be
    /// added directly to the content area as a stacking overlay.
    /// </summary>
    public class FactionDetailPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>Fired when the close button is clicked.</summary>
        public event Action OnCloseRequested;

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass         = "ws-faction-detail-panel";
        private const string HeaderClass        = "ws-faction-detail-panel__header";
        private const string TitleClass         = "ws-faction-detail-panel__title";
        private const string CloseBtnClass      = "ws-faction-detail-panel__close-btn";
        private const string SectionHeaderClass = "ws-faction-detail-panel__section-header";
        private const string BadgeClass         = "ws-faction-detail-panel__badge";
        private const string StabilityBgClass   = "ws-faction-detail-panel__stability-bg";
        private const string StabilityFillClass = "ws-faction-detail-panel__stability-fill";
        private const string RepMeterBgClass    = "ws-faction-detail-panel__rep-meter-bg";
        private const string RepMeterFillClass  = "ws-faction-detail-panel__rep-meter-fill";
        private const string ContractRowClass   = "ws-faction-detail-panel__contract-row";
        private const string HistoryRowClass    = "ws-faction-detail-panel__history-row";
        private const string VassalNoteClass    = "ws-faction-detail-panel__vassal-note";
        private const string FactorRowClass     = "ws-faction-detail-panel__factor-row";

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly ScrollView _scroll;
        private bool                _factorsExpanded = false;

        // Cached data.
        private string                 _factionId;
        private StationState           _station;
        private FactionSystem          _factionSystem;
        private IFactionHistoryProvider _historyProvider;

        // ── Constructor ────────────────────────────────────────────────────────

        public FactionDetailPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection    = FlexDirection.Column;
            style.flexGrow         = 0;
            style.flexShrink       = 0;
            style.width            = 400;
            style.position         = Position.Absolute;
            style.left             = 0;
            style.top              = 0;
            style.bottom           = 32;
            style.backgroundColor  = new Color(0.12f, 0.12f, 0.16f, 0.97f);
            style.borderRightWidth = 1;
            style.borderRightColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

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

            var titleLabel = new Label("Faction Detail");
            titleLabel.name = "faction-title";
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

            // ── Scrollable content area ────────────────────────────────────────
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            Add(_scroll);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the panel with data for the given faction.
        /// Call on mount and again on every relevant tick.
        /// </summary>
        public void Refresh(
            string factionId,
            StationState station,
            FactionSystem factionSystem,
            IFactionHistoryProvider historyProvider)
        {
            _factionId       = factionId;
            _station         = station;
            _factionSystem   = factionSystem;
            _historyProvider = historyProvider;

            // Update header title.
            var title = this.Q<Label>("faction-title");
            if (title != null && _station != null)
            {
                FactionDefinition def = _factionSystem?.GetFaction(_factionId, _station);
                if (def == null && !string.IsNullOrEmpty(_factionId))
                    _station.generatedFactions.TryGetValue(_factionId, out def);
                title.text = def?.displayName ?? _factionId ?? "Faction Detail";
            }

            RebuildContent();
        }

        // ── Content rebuild ────────────────────────────────────────────────────

        private void RebuildContent()
        {
            _scroll.contentContainer.Clear();

            if (_station == null || string.IsNullOrEmpty(_factionId)) return;

            // Resolve faction definition from the system if available,
            // or fall back to station's own generatedFactions for tests/null-system scenarios.
            FactionDefinition def = _factionSystem?.GetFaction(_factionId, _station);
            if (def == null && _station.generatedFactions.TryGetValue(_factionId, out var genDef))
                def = genDef;

            float rep = _factionSystem != null
                ? _factionSystem.GetReputation(_factionId, _station)
                : _station.GetFactionRep(_factionId);

            // ── Name + territory ────────────────────────────────────────────────
            BuildNameSection(def, rep);

            // ── Government type ──────────────────────────────────────────────────
            BuildGovernmentSection(def);

            // ── Vassal / patron indicator ────────────────────────────────────────
            if (def?.governmentType == GovernmentType.CorporateVassal)
                BuildVassalSection(def);

            // ── Stability bar ────────────────────────────────────────────────────
            BuildStabilitySection(def);

            // ── Reputation ──────────────────────────────────────────────────────
            BuildReputationSection(rep);

            // ── Active contracts ─────────────────────────────────────────────────
            BuildContractsSection();

            // ── Faction history log ──────────────────────────────────────────────
            BuildHistorySection();
        }

        // ── Section builders ──────────────────────────────────────────────────

        private void BuildNameSection(FactionDefinition def, float rep)
        {
            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 10;
            section.style.paddingBottom = 6;

            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;
            nameRow.style.marginBottom  = 4;

            var nameLabel = new Label(def?.displayName ?? _factionId ?? "Unknown");
            nameLabel.style.flexGrow        = 1;
            nameLabel.style.fontSize        = 15;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color           = new Color(0.90f, 0.92f, 0.97f, 1f);
            nameRow.Add(nameLabel);

            // Territory type badge (minor / regional / major).
            string typeLabel = CapitaliseFirst(def?.type ?? "minor");
            var typeBadge = MakeBadge(typeLabel, new Color(0.22f, 0.28f, 0.45f, 0.9f));
            nameRow.Add(typeBadge);

            section.Add(nameRow);

            // Home sector (if available).
            if (!string.IsNullOrEmpty(def?.sectorUid))
            {
                var sectorLabel = new Label($"Sector:  {def.sectorUid}");
                sectorLabel.style.fontSize = 11;
                sectorLabel.style.color    = new Color(0.55f, 0.62f, 0.75f, 1f);
                sectorLabel.style.marginBottom = 2;
                section.Add(sectorLabel);
            }

            // Faction description.
            if (!string.IsNullOrEmpty(def?.description))
            {
                var descLabel = new Label(def.description);
                descLabel.style.fontSize   = 11;
                descLabel.style.color      = new Color(0.60f, 0.65f, 0.75f, 1f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginTop  = 4;
                section.Add(descLabel);
            }

            _scroll.contentContainer.Add(section);
            _scroll.contentContainer.Add(MakeDivider());
        }

        private void BuildGovernmentSection(FactionDefinition def)
        {
            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 6;
            section.style.paddingBottom = 6;

            var hdr = MakeSectionHeader("Government");
            section.Add(hdr);

            if (def == null)
            {
                section.Add(MakeDetailRow("—", ""));
                _scroll.contentContainer.Add(section);
                _scroll.contentContainer.Add(MakeDivider());
                return;
            }

            // Government type badge.
            var govRow = new VisualElement();
            govRow.style.flexDirection  = FlexDirection.Row;
            govRow.style.alignItems     = Align.Center;
            govRow.style.marginBottom   = 6;

            var govBadge = MakeBadge(GovernmentTypeLabel(def.governmentType),
                                     new Color(0.25f, 0.30f, 0.50f, 0.9f));
            govBadge.style.marginLeft = 0;
            govRow.Add(govBadge);

            // Succession state.
            if (def.successionState != SuccessionState.Stable)
            {
                var succBadge = MakeBadge(def.successionState.ToString(),
                                          new Color(0.55f, 0.35f, 0.10f, 0.9f));
                govRow.Add(succBadge);
            }

            section.Add(govRow);

            // Axis labels per government type.
            (string powerAxis, string legAxis) = GovernmentAxisLabels(def.governmentType);
            section.Add(MakeDetailRow("Power Distribution", powerAxis));
            section.Add(MakeDetailRow("Legitimacy",         legAxis));

            _scroll.contentContainer.Add(section);
            _scroll.contentContainer.Add(MakeDivider());
        }

        private void BuildVassalSection(FactionDefinition def)
        {
            var section = new VisualElement();
            section.AddToClassList(VassalNoteClass);
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 6;
            section.style.paddingBottom = 6;
            section.style.backgroundColor = new Color(0.28f, 0.20f, 0.10f, 0.5f);
            section.style.marginLeft    = 4;
            section.style.marginRight   = 4;
            section.style.marginBottom  = 4;
            section.style.borderTopLeftRadius     = 4;
            section.style.borderTopRightRadius    = 4;
            section.style.borderBottomLeftRadius  = 4;
            section.style.borderBottomRightRadius = 4;

            var hdr = MakeSectionHeader("Vassalised");
            hdr.style.color = new Color(1.0f, 0.75f, 0.30f, 1f);
            section.Add(hdr);

            string patronName = def.vassalParentFactionId ?? "Unknown";
            if (!string.IsNullOrEmpty(def.vassalParentFactionId))
            {
                FactionDefinition patronDef = null;
                if (_factionSystem != null)
                    patronDef = _factionSystem.GetFaction(def.vassalParentFactionId, _station);
                else if (_station != null &&
                         _station.generatedFactions.TryGetValue(def.vassalParentFactionId, out var genDef))
                    patronDef = genDef;
                if (patronDef != null) patronName = patronDef.displayName ?? def.vassalParentFactionId;
            }

            var patronLabel = new Label($"Patron faction:  {patronName}");
            patronLabel.style.fontSize    = 12;
            patronLabel.style.color       = new Color(0.88f, 0.80f, 0.60f, 1f);
            patronLabel.style.marginBottom = 4;
            section.Add(patronLabel);

            float patronRep = 0f;
            if (_factionSystem != null && !string.IsNullOrEmpty(def.vassalParentFactionId))
                patronRep = _factionSystem.GetReputation(def.vassalParentFactionId, _station);

            var bleedNote = new Label(
                $"Reputation bleed: patron rep ({patronRep:+0;−0}) may influence this faction's " +
                "standing with you.");
            bleedNote.style.fontSize   = 10;
            bleedNote.style.color      = new Color(0.70f, 0.65f, 0.50f, 1f);
            bleedNote.style.whiteSpace = WhiteSpace.Normal;
            section.Add(bleedNote);

            _scroll.contentContainer.Add(section);
            _scroll.contentContainer.Add(MakeDivider());
        }

        private void BuildStabilitySection(FactionDefinition def)
        {
            float stability = def?.stabilityScore ?? 50f;

            // Compute factor breakdown values for the expandable section.
            float economic = 0f, military = 0.5f, populationMood = 0.5f, tenure = 0f;
            if (def != null && _station != null)
            {
                float rep = _factionSystem?.GetReputation(_factionId, _station) ?? _station.GetFactionRep(_factionId);
                economic      = Mathf.Clamp01((rep + 100f) / 200f);
                populationMood = ComputePopulationMood(def);
                tenure         = ComputeTenure(def);
                // military is harder to get without FactionGovernmentSystem; use 0.5 as default.
                military       = 0.5f;
            }

            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 6;
            section.style.paddingBottom = 6;

            var hdrRow = new VisualElement();
            hdrRow.style.flexDirection = FlexDirection.Row;
            hdrRow.style.alignItems    = Align.Center;
            hdrRow.style.marginBottom  = 4;
            hdrRow.style.cursor        = new StyleCursor(StyleKeyword.Auto);

            var hdr = MakeSectionHeader("Stability");
            hdr.style.flexGrow  = 1;
            hdr.style.marginBottom = 0;
            hdrRow.Add(hdr);

            // Stability score label.
            var scoreLabel = new Label($"{stability:F0}/100");
            scoreLabel.style.fontSize      = 12;
            scoreLabel.style.color         = StabilityColor(stability);
            scoreLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            hdrRow.Add(scoreLabel);

            // Toggle expand.
            var expandToggle = new Label(_factorsExpanded ? "▲" : "▼");
            expandToggle.style.fontSize    = 10;
            expandToggle.style.color       = new Color(0.60f, 0.65f, 0.75f, 1f);
            expandToggle.style.marginLeft  = 8;
            hdrRow.Add(expandToggle);

            hdrRow.RegisterCallback<ClickEvent>(_ =>
            {
                _factorsExpanded = !_factorsExpanded;
                RebuildContent();
            });

            section.Add(hdrRow);

            // Stability bar.
            var meterBg = new VisualElement();
            meterBg.AddToClassList(StabilityBgClass);
            meterBg.style.height              = 8;
            meterBg.style.backgroundColor     = new Color(0.15f, 0.18f, 0.25f, 1f);
            meterBg.style.borderTopLeftRadius     = 4;
            meterBg.style.borderTopRightRadius    = 4;
            meterBg.style.borderBottomLeftRadius  = 4;
            meterBg.style.borderBottomRightRadius = 4;
            meterBg.style.overflow = Overflow.Hidden;
            meterBg.style.marginBottom = 4;

            var meterFill = new VisualElement();
            meterFill.AddToClassList(StabilityFillClass);
            meterFill.style.width           = Length.Percent(Mathf.Clamp01(stability / 100f) * 100f);
            meterFill.style.height          = Length.Percent(100);
            meterFill.style.backgroundColor = StabilityColor(stability);
            meterBg.Add(meterFill);
            section.Add(meterBg);

            // Expandable factor breakdown.
            if (_factorsExpanded)
            {
                var factorsSection = new VisualElement();
                factorsSection.style.marginTop    = 4;
                factorsSection.style.marginBottom = 4;
                factorsSection.style.paddingLeft  = 4;
                factorsSection.style.borderLeftWidth = 2;
                factorsSection.style.borderLeftColor = new Color(0.25f, 0.32f, 0.50f, 0.8f);

                AddFactorRow(factorsSection, "Economic Prosperity", economic);
                AddFactorRow(factorsSection, "Military Strength",   military);
                AddFactorRow(factorsSection, "Mood/Cohesion",       populationMood);
                AddFactorRow(factorsSection, "Tenure",              tenure);

                section.Add(factorsSection);
            }

            _scroll.contentContainer.Add(section);
            _scroll.contentContainer.Add(MakeDivider());
        }

        private void AddFactorRow(VisualElement parent, string label, float value)
        {
            var row = new VisualElement();
            row.AddToClassList(FactorRowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4;

            var lbl = new Label(label);
            lbl.style.flexGrow  = 1;
            lbl.style.fontSize  = 11;
            lbl.style.color     = new Color(0.65f, 0.70f, 0.80f, 1f);
            row.Add(lbl);

            // Mini bar.
            var barBg = new VisualElement();
            barBg.style.width             = 60;
            barBg.style.height            = 6;
            barBg.style.backgroundColor   = new Color(0.15f, 0.18f, 0.25f, 1f);
            barBg.style.borderTopLeftRadius     = 3;
            barBg.style.borderTopRightRadius    = 3;
            barBg.style.borderBottomLeftRadius  = 3;
            barBg.style.borderBottomRightRadius = 3;
            barBg.style.overflow          = Overflow.Hidden;
            barBg.style.marginLeft        = 6;

            var barFill = new VisualElement();
            barFill.style.width           = Length.Percent(Mathf.Clamp01(value) * 100f);
            barFill.style.height          = Length.Percent(100);
            barFill.style.backgroundColor = StabilityColor(value * 100f);
            barBg.Add(barFill);
            row.Add(barBg);

            var valLabel = new Label($"{value * 100f:F0}%");
            valLabel.style.fontSize      = 10;
            valLabel.style.color         = new Color(0.70f, 0.73f, 0.83f, 1f);
            valLabel.style.minWidth      = 36;
            valLabel.style.marginLeft    = 6;
            valLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(valLabel);

            parent.Add(row);
        }

        private void BuildReputationSection(float rep)
        {
            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 6;
            section.style.paddingBottom = 6;

            var hdrRow = new VisualElement();
            hdrRow.style.flexDirection = FlexDirection.Row;
            hdrRow.style.alignItems    = Align.Center;
            hdrRow.style.marginBottom  = 4;

            var hdr = MakeSectionHeader("Reputation");
            hdr.style.flexGrow  = 1;
            hdr.style.marginBottom = 0;
            hdrRow.Add(hdr);

            string tier = RepTierLabel(rep);
            var tierLabel = new Label($"{rep:+0;−0;0}  {tier}");
            tierLabel.style.fontSize      = 11;
            tierLabel.style.color         = RepTierColor(tier);
            tierLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            hdrRow.Add(tierLabel);

            section.Add(hdrRow);

            // Rep meter bar.
            var meterBg = new VisualElement();
            meterBg.AddToClassList(RepMeterBgClass);
            meterBg.style.height              = 6;
            meterBg.style.backgroundColor     = new Color(0.15f, 0.18f, 0.25f, 1f);
            meterBg.style.borderTopLeftRadius     = 3;
            meterBg.style.borderTopRightRadius    = 3;
            meterBg.style.borderBottomLeftRadius  = 3;
            meterBg.style.borderBottomRightRadius = 3;
            meterBg.style.overflow = Overflow.Hidden;
            meterBg.style.marginBottom = 6;

            float fillPct = Mathf.Clamp01((rep + 100f) / 200f);
            var meterFill = new VisualElement();
            meterFill.AddToClassList(RepMeterFillClass);
            meterFill.style.width           = Length.Percent(fillPct * 100f);
            meterFill.style.height          = Length.Percent(100);
            meterFill.style.backgroundColor = RepMeterColor(rep);
            meterBg.Add(meterFill);
            section.Add(meterBg);

            // Last 5 reputation changes.
            List<FactionRepChange> repHistory;
            if (_factionSystem != null)
                repHistory = _factionSystem.GetRepHistory(_factionId, _station, 5);
            else if (_station != null &&
                     _station.factionRepHistory.TryGetValue(_factionId, out var log))
                repHistory = log.Count <= 5 ? log : log.GetRange(0, 5);
            else
                repHistory = new List<FactionRepChange>();
            if (repHistory.Count > 0)
            {
                var histHdr = new Label("Recent changes:");
                histHdr.style.fontSize    = 10;
                histHdr.style.color       = new Color(0.55f, 0.60f, 0.70f, 1f);
                histHdr.style.marginBottom = 2;
                section.Add(histHdr);

                foreach (var change in repHistory)
                {
                    bool positive = change.delta >= 0f;
                    var changeRow = new VisualElement();
                    changeRow.style.flexDirection = FlexDirection.Row;
                    changeRow.style.marginBottom  = 2;

                    var tickLabel = new Label($"Tick {change.tick}");
                    tickLabel.style.fontSize  = 10;
                    tickLabel.style.color     = new Color(0.50f, 0.55f, 0.65f, 1f);
                    tickLabel.style.minWidth  = 65;
                    changeRow.Add(tickLabel);

                    var deltaLabel = new Label($"{change.delta:+0.0;−0.0}");
                    deltaLabel.style.fontSize = 10;
                    deltaLabel.style.color    = positive
                        ? new Color(0.25f, 0.80f, 0.35f, 1f)
                        : new Color(0.85f, 0.30f, 0.25f, 1f);
                    deltaLabel.style.minWidth = 40;
                    changeRow.Add(deltaLabel);

                    var resultLabel = new Label($"→ {change.resultingRep:+0;−0}");
                    resultLabel.style.fontSize = 10;
                    resultLabel.style.color    = new Color(0.65f, 0.70f, 0.80f, 1f);
                    changeRow.Add(resultLabel);

                    section.Add(changeRow);
                }
            }

            _scroll.contentContainer.Add(section);
            _scroll.contentContainer.Add(MakeDivider());
        }

        private void BuildContractsSection()
        {
            var contracts = _factionSystem?.GetContracts(_factionId, _station)
                            ?? new List<FactionContract>();

            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 6;
            section.style.paddingBottom = 6;

            section.Add(MakeSectionHeader("Active Contracts"));

            if (contracts.Count == 0)
            {
                var empty = new Label("No active contracts.");
                empty.style.fontSize = 11;
                empty.style.color    = new Color(0.50f, 0.55f, 0.65f, 1f);
                section.Add(empty);
            }
            else
            {
                foreach (var contract in contracts)
                {
                    var row = new VisualElement();
                    row.AddToClassList(ContractRowClass);
                    row.style.paddingTop     = 5;
                    row.style.paddingBottom  = 5;
                    row.style.paddingLeft    = 4;
                    row.style.paddingRight   = 4;
                    row.style.marginBottom   = 4;
                    row.style.backgroundColor = new Color(0.14f, 0.18f, 0.26f, 0.85f);
                    row.style.borderTopLeftRadius     = 3;
                    row.style.borderTopRightRadius    = 3;
                    row.style.borderBottomLeftRadius  = 3;
                    row.style.borderBottomRightRadius = 3;

                    var descLabel = new Label(
                        string.IsNullOrEmpty(contract.description) ? "Contract" : contract.description);
                    descLabel.style.fontSize   = 11;
                    descLabel.style.color      = new Color(0.80f, 0.83f, 0.92f, 1f);
                    descLabel.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(descLabel);

                    var creditLabel = new Label(
                        $"  {contract.creditPerPayment:+0;−0} cr / {contract.paymentIntervalTicks} ticks");
                    creditLabel.style.fontSize = 10;
                    creditLabel.style.color    = new Color(0.55f, 0.78f, 0.55f, 1f);
                    row.Add(creditLabel);

                    section.Add(row);
                }
            }

            _scroll.contentContainer.Add(section);
            _scroll.contentContainer.Add(MakeDivider());
        }

        private void BuildHistorySection()
        {
            var section = new VisualElement();
            section.style.paddingLeft   = 10;
            section.style.paddingRight  = 10;
            section.style.paddingTop    = 6;
            section.style.paddingBottom = 10;

            section.Add(MakeSectionHeader("Faction History"));

            List<HistoricalEvent> history = _historyProvider != null
                ? _historyProvider.GetFactionHistory(_factionId)
                : new List<HistoricalEvent>();

            // Show last 10 events (history is stored oldest-first by FactionHistory).
            int startIdx = Mathf.Max(0, history.Count - 10);
            int shown    = 0;

            for (int i = history.Count - 1; i >= startIdx && shown < 10; i--, shown++)
            {
                var evt  = history[i];
                var row  = new VisualElement();
                row.AddToClassList(HistoryRowClass);
                row.style.flexDirection  = FlexDirection.Column;
                row.style.paddingTop     = 4;
                row.style.paddingBottom  = 4;
                row.style.marginBottom   = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.22f, 0.26f, 0.36f, 0.4f);

                var tickLabel = new Label($"Tick {evt.gameTick}");
                tickLabel.style.fontSize    = 9;
                tickLabel.style.color       = new Color(0.45f, 0.50f, 0.62f, 1f);
                tickLabel.style.marginBottom = 1;
                row.Add(tickLabel);

                var descLabel = new Label(evt.description ?? evt.eventId ?? "—");
                descLabel.style.fontSize   = 11;
                descLabel.style.color      = new Color(0.72f, 0.76f, 0.86f, 1f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                row.Add(descLabel);

                section.Add(row);
            }

            if (shown == 0)
            {
                var empty = new Label("No faction history recorded.");
                empty.style.fontSize = 11;
                empty.style.color    = new Color(0.50f, 0.55f, 0.65f, 1f);
                section.Add(empty);
            }

            _scroll.contentContainer.Add(section);
        }

        // ── Stability factor helpers (public for tests) ──────────────────────

        /// <summary>
        /// Computes the average mood score of known faction members (0–1).
        /// Returns 0.5 when no members are known to the station.
        /// </summary>
        public float ComputePopulationMood(FactionDefinition def)
        {
            if (def == null || _station == null) return 0.5f;
            float moodSum   = 0f;
            int   moodCount = 0;
            foreach (var npcId in def.memberNpcIds)
            {
                if (_station.npcs.TryGetValue(npcId, out var npc))
                {
                    moodSum += npc.moodScore;
                    moodCount++;
                }
            }
            return moodCount > 0 ? Mathf.Clamp01(moodSum / (moodCount * 100f)) : 0.5f;
        }

        /// <summary>
        /// Computes the government tenure factor (0–1).
        /// Returns 0 when the faction has just formed or changed government.
        /// </summary>
        public static float ComputeTenure(FactionDefinition def)
        {
            if (def == null) return 0f;
            const int tenureFullTicks = FactionGovernmentSystem.TenureFullStabilityTicks;
            return Mathf.Clamp01((float)def.governmentTenureTicks / tenureFullTicks);
        }

        // ── Shared element helpers ─────────────────────────────────────────────

        private Label MakeSectionHeader(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList(SectionHeaderClass);
            lbl.style.fontSize    = 10;
            lbl.style.color       = new Color(0.50f, 0.58f, 0.72f, 1f);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginBottom = 5;
            lbl.style.letterSpacing = new StyleLength(new Length(1f, LengthUnit.Pixel));
            return lbl;
        }

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

        private VisualElement MakeDetailRow(string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.marginBottom   = 2;

            var lbl = new Label($"{label}: ");
            lbl.style.fontSize = 11;
            lbl.style.color    = new Color(0.55f, 0.62f, 0.75f, 1f);
            row.Add(lbl);

            var val = new Label(value ?? "—");
            val.style.fontSize = 11;
            val.style.color    = new Color(0.80f, 0.83f, 0.92f, 1f);
            row.Add(val);

            return row;
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

        // ── Government type helpers (public for tests) ───────────────────────

        internal static string GovernmentTypeLabel(GovernmentType govType) =>
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

        /// <summary>
        /// Returns (powerDistribution, legitimacy) axis labels for the given government type.
        /// </summary>
        public static (string powerAxis, string legAxis) GovernmentAxisLabels(GovernmentType govType) =>
            govType switch
            {
                GovernmentType.Democracy      => ("Distributed",       "Consensual"),
                GovernmentType.Republic       => ("Balanced",          "Earned"),
                GovernmentType.Monarchy       => ("Centralised",       "Traditional"),
                GovernmentType.Authoritarian  => ("Centralised",       "Coercive"),
                GovernmentType.CorporateVassal => ("Balanced",         "Economic"),
                GovernmentType.Pirate         => ("Anarchic",          "None"),
                GovernmentType.Theocracy      => ("Centralised",       "Divine Mandate"),
                GovernmentType.Technocracy    => ("Balanced",          "Merit-Based"),
                GovernmentType.FederalCouncil => ("Distributed",       "Accepted Order"),
                _                             => ("—",                 "—"),
            };

        // ── Colour helpers ─────────────────────────────────────────────────────

        internal static string RepTierLabel(float rep)
        {
            if (rep >= 75f)  return "Allied";
            if (rep >= 50f)  return "Friendly";
            if (rep >= 0f)   return "Neutral";
            if (rep >= -50f) return "Unfriendly";
            return "Hostile";
        }

        private static Color RepTierColor(string tier) =>
            tier switch
            {
                "Allied"     => new Color(0.20f, 0.85f, 0.40f, 1f),
                "Friendly"   => new Color(0.50f, 0.80f, 0.30f, 1f),
                "Neutral"    => new Color(0.85f, 0.80f, 0.20f, 1f),
                "Unfriendly" => new Color(0.90f, 0.55f, 0.15f, 1f),
                _            => new Color(0.85f, 0.25f, 0.20f, 1f),
            };

        private static Color RepMeterColor(float rep)
        {
            if (rep >= 75f)  return new Color(0.20f, 0.85f, 0.40f, 1f);
            if (rep >= 50f)  return new Color(0.50f, 0.80f, 0.30f, 1f);
            if (rep >= 0f)   return new Color(0.85f, 0.80f, 0.20f, 1f);
            if (rep >= -50f) return new Color(0.90f, 0.55f, 0.15f, 1f);
            return new Color(0.85f, 0.25f, 0.20f, 1f);
        }

        private static Color StabilityColor(float stability)
        {
            if (stability >= 60f) return new Color(0.20f, 0.85f, 0.40f, 1f);   // stable
            if (stability >= 40f) return new Color(0.85f, 0.80f, 0.20f, 1f);   // medium
            if (stability >= 20f) return new Color(0.90f, 0.55f, 0.15f, 1f);   // low
            return new Color(0.85f, 0.25f, 0.20f, 1f);                          // critical
        }

        private static string CapitaliseFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
