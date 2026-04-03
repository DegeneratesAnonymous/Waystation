// CrewMemberPanelController.cs
// Crew Member contextual panel (UI-023).
//
// Displays deep per-NPC inspection across five tabs:
//   Vitals      — mood axes, active modifiers, eight need bars, sanity score.
//   Skills      — character level, simple/advanced skill rows with XP bars and
//                 expertise slot pips; unclaimed pips pulse and open the
//                 Expertise Slot Unlock modal on click.
//   Relationships — affinity list with badge, score, and decay warning; clicking
//                   a row stacks another Crew Member panel.
//   Inventory   — equipped items, pocket items, carry-weight summary bar.
//   History     — backstory block, life-event log, trait list.
//
// Opened from three entry points (world click, Roster, Assignments) via
//   WaystationHUDController.SelectCrewMember(npcUid).
//
// Data is pushed via Refresh(npc, station, registry, skills, inventory, traits).
// Panel slides in from the right edge and stacks with other open panels.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel is mounted by
// WaystationHUDController which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Crew Member contextual panel.  Extends <see cref="VisualElement"/> so it
    /// can be added directly to the content area as a stacking overlay.
    /// </summary>
    public class CrewMemberPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>Fired when the close button is clicked.</summary>
        public event Action OnCloseRequested;

        /// <summary>
        /// Fired when a relationship row is clicked to open that NPC's panel.
        /// Argument is the other NPC's uid.
        /// </summary>
        public event Action<string> OnRelationshipRowClicked;

        /// <summary>
        /// Fired when an expertise slot pip is clicked.
        /// Arguments: npcUid, skillId.
        /// </summary>
        public event Action<string, string> OnExpertiseSlotClicked;

        // ── CSS class names ────────────────────────────────────────────────────

        private const string PanelClass             = "ws-crew-member-panel";
        private const string HeaderClass            = "ws-crew-member-panel__header";
        private const string NameLabelClass         = "ws-crew-member-panel__name";
        private const string CloseBtnClass          = "ws-crew-member-panel__close-btn";
        private const string TabContentClass        = "ws-crew-member-panel__tab-content";
        private const string SectionHeaderClass     = "ws-crew-member-panel__section-header";
        private const string BarBgClass             = "ws-crew-member-panel__bar-bg";
        private const string BarFillClass           = "ws-crew-member-panel__bar-fill";
        private const string NeedRowClass           = "ws-crew-member-panel__need-row";
        private const string NeedLabelClass         = "ws-crew-member-panel__need-label";
        private const string CrisisClass            = "ws-crew-member-panel__crisis-indicator";
        private const string ModRowClass            = "ws-crew-member-panel__mod-row";
        private const string SkillRowClass          = "ws-crew-member-panel__skill-row";
        private const string ExpertisePipClass      = "ws-crew-member-panel__expertise-pip";
        private const string ExpertisePipFilledClass= "ws-crew-member-panel__expertise-pip--filled";
        private const string ExpertisePipPendingClass="ws-crew-member-panel__expertise-pip--pending";
        private const string RelRowClass            = "ws-crew-member-panel__rel-row";
        private const string RelBadgeClass          = "ws-crew-member-panel__rel-badge";
        private const string DecayWarningClass      = "ws-crew-member-panel__decay-warning";
        private const string InvRowClass            = "ws-crew-member-panel__inv-row";
        private const string TraitRowClass          = "ws-crew-member-panel__trait-row";
        private const string HistoryEntryClass      = "ws-crew-member-panel__history-entry";
        private const string EmptyClass             = "ws-crew-member-panel__empty";
        private const string SanityLabelClass       = "ws-crew-member-panel__sanity-label";

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly TabStrip       _tabs;
        private readonly VisualElement  _tabContent;
        private string                  _activeTab = "vitals";

        // Cached data for tab rebuilds
        private NPCInstance             _npc;
        private StationState            _station;
        private ContentRegistry         _registry;
        private SkillSystem             _skillSystem;
        private InventorySystem         _inventory;
        private TraitSystem             _traits;

        // ── Constructor ────────────────────────────────────────────────────────

        public CrewMemberPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection   = FlexDirection.Column;
            style.flexGrow        = 0;
            style.flexShrink      = 0;
            style.width           = 400;
            style.position        = Position.Absolute;
            style.left            = 0;
            style.top             = 0;
            style.bottom          = 32;   // clear the event log strip (32px header)
            style.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.97f);
            style.borderRightWidth = 1;
            style.borderRightColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

            // ── Header ──────────────────────────────────────────────────────────
            var header = new VisualElement();
            header.AddToClassList(HeaderClass);
            header.style.flexDirection   = FlexDirection.Row;
            header.style.alignItems      = Align.Center;
            header.style.paddingLeft     = 10;
            header.style.paddingRight    = 6;
            header.style.paddingTop      = 8;
            header.style.paddingBottom   = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.8f);

            var nameLabel = new Label("—");
            nameLabel.name = "npc-name";
            nameLabel.AddToClassList(NameLabelClass);
            nameLabel.style.flexGrow   = 1;
            nameLabel.style.fontSize   = 15;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(nameLabel);

            var closeBtn = new Button(() => OnCloseRequested?.Invoke()) { text = "✕" };
            closeBtn.AddToClassList(CloseBtnClass);
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.borderTopWidth  = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.color = new Color(0.7f, 0.7f, 0.75f);
            closeBtn.style.fontSize = 14;
            header.Add(closeBtn);

            Add(header);

            // ── Tab content area (must exist before AddTab fires OnTabSelected) ──
            _tabContent = new ScrollView(ScrollViewMode.Vertical);
            _tabContent.AddToClassList(TabContentClass);
            _tabContent.style.flexGrow = 1;
            _tabContent.style.overflow = Overflow.Hidden;

            // ── Tab strip ──────────────────────────────────────────────────────
            _tabs = new TabStrip(TabStrip.Orientation.Horizontal);
            _tabs.OnTabSelected += OnTabSelected;
            _tabs.AddTab("VITALS",    "vitals");
            _tabs.AddTab("SKILLS",    "skills");
            _tabs.AddTab("RELATIONS", "relationships");
            _tabs.AddTab("INVENTORY", "inventory");
            _tabs.AddTab("HISTORY",   "history");
            Add(_tabs);
            Add(_tabContent);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Programmatically selects a tab by id.
        /// Valid ids: "vitals" | "skills" | "relationships" | "inventory" | "history".
        /// </summary>
        public void SelectTab(string tabId) => _tabs?.SelectTab(tabId);

        /// <summary>
        /// Refreshes all tab content with current NPC data.
        /// Call on mount and on each tick while the panel is visible.
        /// </summary>
        public void Refresh(
            NPCInstance npc,
            StationState station,
            ContentRegistry registry,
            SkillSystem skillSystem,
            InventorySystem inventory,
            TraitSystem traits)
        {
            _npc         = npc;
            _station     = station;
            _registry    = registry;
            _skillSystem = skillSystem;
            _inventory   = inventory;
            _traits      = traits;

            // Update the header name (or clear it if no NPC is selected).
            var nameLabel = this.Q<Label>("npc-name");
            if (nameLabel != null) nameLabel.text = npc?.name ?? "—";

            // Rebuild the active tab content (will show empty state if npc is null).
            RebuildActiveTab();
        }

        // ── Tab switching ──────────────────────────────────────────────────────

        private void OnTabSelected(string tabId)
        {
            _activeTab = tabId;
            RebuildActiveTab();
        }

        private void RebuildActiveTab()
        {
            _tabContent.contentContainer.Clear();

            if (_npc == null)
            {
                var empty = MakeEmptyLabel("No crew member selected.");
                empty.style.paddingLeft = 10;
                empty.style.paddingTop  = 10;
                _tabContent.contentContainer.Add(empty);
                return;
            }

            switch (_activeTab)
            {
                case "vitals":        BuildVitalsTab();        break;
                case "skills":        BuildSkillsTab();        break;
                case "relationships": BuildRelationshipsTab(); break;
                case "inventory":     BuildInventoryTab();     break;
                case "history":       BuildHistoryTab();       break;
            }
        }

        // ── Vitals tab ─────────────────────────────────────────────────────────

        private void BuildVitalsTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            // ── Mood axes ──────────────────────────────────────────────────────
            AddSectionHeader(root, "MOOD");

            // Happy / Sad axis
            AddMoodAxisRow(root,
                "Happy/Sad",
                _npc.moodScore,
                MoodSystem.GetThresholdLabel(_npc.moodScore),
                MoodSystem.GetMoodColor(_npc.moodScore));

            // Calm / Stressed axis (inverted label: low = stressed, high = calm)
            var stressColor = _npc.stressScore >= 60f
                ? new Color(0.22f, 0.76f, 0.35f)
                : _npc.stressScore >= 35f
                    ? new Color(0.88f, 0.68f, 0.10f)
                    : new Color(0.86f, 0.26f, 0.26f);
            string stressLabel = _npc.stressScore >= 60f ? "Calm"
                               : _npc.stressScore >= 35f ? "Tense"
                               : "Stressed";
            AddMoodAxisRow(root, "Calm/Stressed", _npc.stressScore, stressLabel, stressColor);

            // ── Active mood modifiers ──────────────────────────────────────────
            var allMods = MoodSystem.GetModifierBreakdown(_npc);
            if (allMods != null && allMods.Count > 0)
            {
                AddSectionHeader(root, "MOOD MODIFIERS");

                var modHeader = new VisualElement();
                modHeader.style.flexDirection = FlexDirection.Row;
                modHeader.style.paddingBottom = 2;
                modHeader.style.marginBottom  = 2;
                modHeader.style.borderBottomWidth = 1;
                modHeader.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.4f);
                AddSmallLabel(modHeader, "Name",     flex: 1f);
                AddSmallLabel(modHeader, "Value",    flex: 0.5f);
                AddSmallLabel(modHeader, "Source",   flex: 0.8f);
                AddSmallLabel(modHeader, "Remaining",flex: 0.8f);
                root.Add(modHeader);

                foreach (var (axis, rec) in allMods)
                {
                    var row = new VisualElement();
                    row.AddToClassList(ModRowClass);
                    row.style.flexDirection  = FlexDirection.Row;
                    row.style.paddingTop     = 2;
                    row.style.paddingBottom  = 2;

                    string axisTag   = axis == MoodAxis.CalmStressed ? " [S]" : "";
                    string valueTxt  = rec.delta >= 0f ? $"+{rec.delta:F0}" : $"{rec.delta:F0}";
                    string remaining = rec.expiresAtTick < 0
                        ? "Perm."
                        : $"{Mathf.Max(0, rec.expiresAtTick - (_station?.tick ?? 0))}t";

                    Color valColor = rec.delta >= 0f
                        ? new Color(0.22f, 0.76f, 0.35f)
                        : new Color(0.86f, 0.26f, 0.26f);

                    AddSmallLabel(row, (rec.eventId ?? "?") + axisTag, flex: 1f);
                    var valLbl = MakeSmallLabel(valueTxt, flex: 0.5f);
                    valLbl.style.color = valColor;
                    row.Add(valLbl);
                    AddSmallLabel(row, rec.source ?? "", flex: 0.8f);
                    AddSmallLabel(row, remaining,         flex: 0.8f);
                    root.Add(row);
                }
            }

            // ── Needs ──────────────────────────────────────────────────────────
            AddSectionHeader(root, "NEEDS");

            // Crisis thresholds: <=10% for Sleep/Thirst/Social (no dedicated inCrisis field),
            // <=HungerMalnourishThr for Hunger, isBurntOut for Recreation,
            // inCrisis field for Hygiene.
            const float LowNeedCrisisThreshold = 10f;
            bool sleepCrisis      = (_npc.sleepNeed?.value      ?? 100f) <= LowNeedCrisisThreshold;
            bool hungerCrisis     = (_npc.hungerNeed?.value     ?? 100f) <= NeedSystem.HungerMalnourishThr;
            bool thirstCrisis     = (_npc.thirstNeed?.value     ?? 100f) <= LowNeedCrisisThreshold;
            bool recreationCrisis = _npc.recreationNeed?.isBurntOut ?? false;
            bool socialCrisis     = (_npc.socialNeed?.value     ?? 50f)  <= LowNeedCrisisThreshold;

            AddNeedBar(root, "Sleep",       _npc.sleepNeed?.value ?? 100f,       sleepCrisis);
            AddNeedBar(root, "Hunger",      _npc.hungerNeed?.value ?? 100f,      hungerCrisis);
            AddNeedBar(root, "Thirst",      _npc.thirstNeed?.value ?? 100f,      thirstCrisis);
            AddNeedBar(root, "Recreation",  _npc.recreationNeed?.value ?? 100f,  recreationCrisis);
            AddNeedBar(root, "Social",      _npc.socialNeed?.value ?? 50f,       socialCrisis);
            AddNeedBar(root, "Hygiene",     _npc.hygieneNeed?.value ?? 100f,     _npc.hygieneNeed?.inCrisis ?? false);

            // Mood (old float axis, 0–1 → 0–100)
            float moodPct = Mathf.Clamp01(_npc.mood) * 100f;
            AddNeedBar(root, "Mood",        moodPct,            false);

            // Health — derived from injuries count (0 = full, higher = worse)
            float healthPct = _npc.injuries <= 0 ? 100f : Mathf.Max(0f, 100f - _npc.injuries * 20f);
            bool healthCrisis = _npc.injuries >= 5;
            AddNeedBar(root, "Health",      healthPct,          healthCrisis);

            // ── Sanity ─────────────────────────────────────────────────────────
            AddSectionHeader(root, "SANITY");

            var sanityRow = new VisualElement();
            sanityRow.style.flexDirection = FlexDirection.Row;
            sanityRow.style.alignItems    = Align.Center;
            sanityRow.style.marginBottom  = 4;

            int sanityScore = _npc.sanity?.score ?? 0;
            bool breakdown  = _npc.sanity?.isInBreakdown ?? false;
            string sanityState = breakdown                   ? "Breakdown"
                              : sanityScore <= -3            ? "At Risk"
                              : "Normal";
            Color sanityColor  = breakdown                   ? new Color(0.86f, 0.26f, 0.26f)
                              : sanityScore <= -3            ? new Color(0.88f, 0.68f, 0.10f)
                              : new Color(0.22f, 0.76f, 0.35f);

            var sanityScoreLbl = new Label($"Score: {sanityScore}");
            sanityScoreLbl.style.flexGrow = 1;
            sanityScoreLbl.style.fontSize = 12;
            sanityRow.Add(sanityScoreLbl);

            var sanityStateLbl = new Label(sanityState);
            sanityStateLbl.AddToClassList(SanityLabelClass);
            sanityStateLbl.style.color    = sanityColor;
            sanityStateLbl.style.fontSize = 12;
            sanityStateLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            sanityRow.Add(sanityStateLbl);

            root.Add(sanityRow);
        }

        private void AddMoodAxisRow(VisualElement parent, string label, float score,
                                     string stateLabel, Color fillColor)
        {
            var row = new VisualElement();
            row.style.marginBottom = 6;

            var labelRow = new VisualElement();
            labelRow.style.flexDirection = FlexDirection.Row;
            labelRow.style.marginBottom  = 2;

            var axisLbl = new Label(label);
            axisLbl.style.flexGrow  = 1;
            axisLbl.style.fontSize  = 11;
            axisLbl.style.color     = new Color(0.65f, 0.65f, 0.7f);
            labelRow.Add(axisLbl);

            var stateLbl = new Label(stateLabel);
            stateLbl.style.fontSize = 11;
            stateLbl.style.color    = fillColor;
            stateLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            labelRow.Add(stateLbl);

            row.Add(labelRow);
            row.Add(MakeBar(score / 100f, fillColor));
            parent.Add(row);
        }

        private void AddNeedBar(VisualElement parent, string needName, float value, bool inCrisis)
        {
            var row = new VisualElement();
            row.AddToClassList(NeedRowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 4;

            var nameLbl = new Label(needName);
            nameLbl.AddToClassList(NeedLabelClass);
            nameLbl.style.width    = 70;
            nameLbl.style.fontSize = 11;
            row.Add(nameLbl);

            float clampedPct = Mathf.Clamp(value, 0f, 100f) / 100f;
            Color fillColor = clampedPct >= 0.5f ? new Color(0.22f, 0.76f, 0.35f)
                            : clampedPct >= 0.2f ? new Color(0.88f, 0.68f, 0.10f)
                            : new Color(0.86f, 0.26f, 0.26f);

            var bar = MakeBar(clampedPct, fillColor, flexible: true);
            row.Add(bar);

            if (inCrisis)
            {
                var crisis = new Label("●");
                crisis.AddToClassList(CrisisClass);
                crisis.style.color      = new Color(0.9f, 0.15f, 0.15f);
                crisis.style.fontSize   = 14;
                crisis.style.marginLeft = 4;
                row.Add(crisis);
            }

            parent.Add(row);
        }

        // ── Skills tab ─────────────────────────────────────────────────────────

        // Stat display order for domain-skill section grouping.
        private static readonly string[] StatGroupOrder = { "STR", "DEX", "INT", "WIS", "CHA", "END" };

        /// <summary>Look up an ability score value by its three-letter abbreviation.</summary>
        private static int GetAbilityScore(AbilityScores scores, string stat)
        {
            switch (stat)
            {
                case "STR": return scores.STR;
                case "DEX": return scores.DEX;
                case "INT": return scores.INT;
                case "WIS": return scores.WIS;
                case "CHA": return scores.CHA;
                case "END": return scores.END;
                default:    return 0;
            }
        }

        private void BuildSkillsTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 8;
            root.style.paddingRight  = 8;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 10;

            // ── Character level + rank header ──────────────────────────────────
            int charLevel = SkillSystem.GetCharacterLevel(_npc);
            string rankName = RankLabel(_npc.rank);

            var charRow = new VisualElement();
            charRow.style.flexShrink        = 0;
            charRow.style.flexDirection     = FlexDirection.Row;
            charRow.style.alignItems        = Align.Center;
            charRow.style.marginBottom      = 6;
            charRow.style.paddingBottom     = 4;
            charRow.style.borderBottomWidth = 1;
            charRow.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.6f);

            var lvlLabel = new Label($"Level {charLevel}");
            lvlLabel.style.flexGrow  = 1;
            lvlLabel.style.fontSize  = 12;
            lvlLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            lvlLabel.style.color = new Color(0.9f, 0.92f, 1f);
            charRow.Add(lvlLabel);

            if (!string.IsNullOrEmpty(rankName))
            {
                var rankLbl = new Label(rankName);
                rankLbl.style.fontSize = 10;
                rankLbl.style.color    = new Color(0.55f, 0.55f, 0.65f);
                rankLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                charRow.Add(rankLbl);
            }
            root.Add(charRow);

            if (_npc.skillInstances == null || _npc.skillInstances.Count == 0)
            {
                var empty = new Label("No skill data.");
                empty.AddToClassList(EmptyClass);
                empty.style.fontSize = 12;
                empty.style.color    = new Color(0.5f, 0.5f, 0.55f);
                root.Add(empty);
                return;
            }

            // ── Group domain skills by primary stat; collect core skills separately ──
            var domainGroups = new Dictionary<string, List<(SkillInstance inst, SkillDefinition def)>>();
            var coreSkills   = new List<(SkillInstance inst, SkillDefinition def)>();

            foreach (var inst in _npc.skillInstances)
            {
                SkillDefinition def;
                if (_registry == null || !_registry.Skills.TryGetValue(inst.skillId, out def))
                    def = new SkillDefinition { skillId = inst.skillId, displayName = inst.skillId };

                if (def.IsDomainSkill && !string.IsNullOrEmpty(def.primaryStat))
                {
                    if (!domainGroups.ContainsKey(def.primaryStat))
                        domainGroups[def.primaryStat] = new List<(SkillInstance, SkillDefinition)>();
                    domainGroups[def.primaryStat].Add((inst, def));
                }
                else
                {
                    coreSkills.Add((inst, def));
                }
            }

            // ── Render domain groups in stat order ─────────────────────────────
            var scores = _npc.abilityScores ?? new AbilityScores();
            foreach (string stat in StatGroupOrder)
            {
                if (!domainGroups.TryGetValue(stat, out var group)) continue;

                int scoreVal = GetAbilityScore(scores, stat);
                int mod      = AbilityScores.GetModifier(scoreVal);
                string modStr = mod >= 0 ? $"+{mod}" : $"{mod}";

                // ── Stat group header (TTRPG style) ────────────────────────────
                var statHeader = new VisualElement();
                statHeader.style.flexShrink        = 0;
                statHeader.style.flexDirection     = FlexDirection.Row;
                statHeader.style.alignItems        = Align.Center;
                statHeader.style.marginTop         = 8;
                statHeader.style.marginBottom      = 3;
                statHeader.style.paddingBottom     = 2;
                statHeader.style.borderBottomWidth = 1;
                statHeader.style.borderBottomColor = new Color(0.35f, 0.4f, 0.55f, 0.5f);

                var statNameLbl = new Label(stat);
                statNameLbl.style.fontSize = 11;
                statNameLbl.style.color    = new Color(0.6f, 0.72f, 0.9f);
                statNameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                statNameLbl.style.marginRight = 6;
                statHeader.Add(statNameLbl);

                var statScoreLbl = new Label($"{scoreVal}");
                statScoreLbl.style.fontSize = 11;
                statScoreLbl.style.color    = new Color(0.85f, 0.88f, 0.95f);
                statScoreLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                statScoreLbl.style.marginRight = 3;
                statHeader.Add(statScoreLbl);

                var statModLbl = new Label($"({modStr})");
                statModLbl.style.fontSize = 9;
                statModLbl.style.color    = mod >= 0
                    ? new Color(0.4f, 0.75f, 0.5f)
                    : new Color(0.85f, 0.45f, 0.4f);
                statHeader.Add(statModLbl);

                // Spacer + skill count
                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                statHeader.Add(spacer);

                var countLbl = new Label($"{group.Count} skill{(group.Count != 1 ? "s" : "")}");
                countLbl.style.fontSize = 9;
                countLbl.style.color    = new Color(0.45f, 0.45f, 0.55f);
                statHeader.Add(countLbl);

                root.Add(statHeader);

                foreach (var (inst, def) in group)
                    root.Add(BuildDomainSkillRow(inst, def));
            }

            // ── Perception section ─────────────────────────────────────────────
            int perceptionValue = SkillSystem.GetPerceptionScore(_npc);

            var perceptionHeader = new VisualElement();
            perceptionHeader.style.flexShrink        = 0;
            perceptionHeader.style.flexDirection     = FlexDirection.Row;
            perceptionHeader.style.alignItems        = Align.Center;
            perceptionHeader.style.marginTop         = 10;
            perceptionHeader.style.marginBottom      = 3;
            perceptionHeader.style.paddingBottom     = 2;
            perceptionHeader.style.borderBottomWidth = 1;
            perceptionHeader.style.borderBottomColor = new Color(0.35f, 0.4f, 0.55f, 0.5f);

            var perceptionTitleLbl = new Label("PERCEPTION");
            perceptionTitleLbl.style.fontSize = 11;
            perceptionTitleLbl.style.color    = new Color(0.6f, 0.72f, 0.9f);
            perceptionTitleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            perceptionTitleLbl.style.flexGrow = 1;
            perceptionHeader.Add(perceptionTitleLbl);

            var perceptionScoreLbl = new Label($"{perceptionValue}");
            perceptionScoreLbl.style.fontSize = 12;
            perceptionScoreLbl.style.color    = new Color(0.85f, 0.88f, 0.95f);
            perceptionScoreLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            perceptionHeader.Add(perceptionScoreLbl);

            root.Add(perceptionHeader);

            // Perception card
            var perceptionCard = new VisualElement();
            perceptionCard.style.flexShrink      = 0;
            perceptionCard.style.backgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.5f);
            perceptionCard.style.borderTopLeftRadius     = 3;
            perceptionCard.style.borderTopRightRadius    = 3;
            perceptionCard.style.borderBottomLeftRadius  = 3;
            perceptionCard.style.borderBottomRightRadius = 3;
            perceptionCard.style.paddingLeft   = 8;
            perceptionCard.style.paddingRight  = 8;
            perceptionCard.style.paddingTop    = 5;
            perceptionCard.style.paddingBottom = 5;

            var perceptionFormulaLbl = new Label("WIS + (INT + CHA) / 4");
            perceptionFormulaLbl.style.fontSize = 9;
            perceptionFormulaLbl.style.color    = new Color(0.5f, 0.5f, 0.6f);
            perceptionCard.Add(perceptionFormulaLbl);

            var perceptionDescLbl = new Label(
                "Passive. Fires on threat detection, contraband scanning, " +
                "social manipulation, and environmental fault checks.");
            perceptionDescLbl.style.fontSize   = 9;
            perceptionDescLbl.style.color      = new Color(0.5f, 0.5f, 0.6f);
            perceptionDescLbl.style.whiteSpace = WhiteSpace.Normal;
            perceptionDescLbl.style.marginTop  = 2;
            perceptionCard.Add(perceptionDescLbl);

            root.Add(perceptionCard);

            // ── Core / non-domain skills section ──────────────────────────────
            if (coreSkills.Count > 0)
            {
                var coreHeader = new VisualElement();
                coreHeader.style.flexShrink        = 0;
                coreHeader.style.flexDirection     = FlexDirection.Row;
                coreHeader.style.alignItems        = Align.Center;
                coreHeader.style.marginTop         = 10;
                coreHeader.style.marginBottom      = 3;
                coreHeader.style.paddingBottom     = 2;
                coreHeader.style.borderBottomWidth = 1;
                coreHeader.style.borderBottomColor = new Color(0.35f, 0.4f, 0.55f, 0.5f);

                var coreTitleLbl = new Label("CORE SKILLS");
                coreTitleLbl.style.fontSize = 11;
                coreTitleLbl.style.color    = new Color(0.6f, 0.72f, 0.9f);
                coreTitleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                coreTitleLbl.style.flexGrow = 1;
                coreHeader.Add(coreTitleLbl);

                var coreCountLbl = new Label($"{coreSkills.Count} skill{(coreSkills.Count != 1 ? "s" : "")}");
                coreCountLbl.style.fontSize = 9;
                coreCountLbl.style.color    = new Color(0.45f, 0.45f, 0.55f);
                coreHeader.Add(coreCountLbl);

                root.Add(coreHeader);

                foreach (var (inst, def) in coreSkills)
                    root.Add(BuildDomainSkillRow(inst, def));
            }
        }

        /// <summary>Build a card-style row for a domain skill.</summary>
        private VisualElement BuildDomainSkillRow(SkillInstance inst, SkillDefinition def)
        {
            // ── Card container ─────────────────────────────────────────────────
            var card = new VisualElement();
            card.AddToClassList(SkillRowClass);
            card.style.flexShrink      = 0;
            card.style.backgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.5f);
            card.style.borderTopLeftRadius     = 3;
            card.style.borderTopRightRadius    = 3;
            card.style.borderBottomLeftRadius  = 3;
            card.style.borderBottomRightRadius = 3;
            card.style.paddingLeft   = 8;
            card.style.paddingRight  = 8;
            card.style.paddingTop    = 5;
            card.style.paddingBottom = 5;
            card.style.marginBottom  = 2;

            bool isProficient = SkillSystem.IsSkillProficient(_npc, inst.skillId);

            // ── Row 1: proficiency pip + skill name + level ────────────────────
            var nameRow = new VisualElement();
            nameRow.style.flexShrink    = 0;
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;

            // Proficiency pip
            var profPip = new VisualElement();
            profPip.style.width  = 8;
            profPip.style.height = 8;
            profPip.style.borderTopLeftRadius     = 4;
            profPip.style.borderTopRightRadius    = 4;
            profPip.style.borderBottomLeftRadius  = 4;
            profPip.style.borderBottomRightRadius = 4;
            profPip.style.marginRight = 6;
            if (isProficient)
            {
                profPip.style.backgroundColor = new Color(0.30f, 0.72f, 0.45f);
                profPip.tooltip = "Proficient";
            }
            else
            {
                profPip.style.backgroundColor = Color.clear;
                profPip.style.borderTopWidth    = 1;
                profPip.style.borderRightWidth  = 1;
                profPip.style.borderBottomWidth = 1;
                profPip.style.borderLeftWidth   = 1;
                profPip.style.borderTopColor    = new Color(0.4f, 0.4f, 0.5f);
                profPip.style.borderRightColor  = new Color(0.4f, 0.4f, 0.5f);
                profPip.style.borderBottomColor = new Color(0.4f, 0.4f, 0.5f);
                profPip.style.borderLeftColor   = new Color(0.4f, 0.4f, 0.5f);
                profPip.tooltip = "Not proficient";
            }
            nameRow.Add(profPip);

            var nameLbl = new Label(def.displayName ?? def.skillId);
            nameLbl.style.flexGrow  = 1;
            nameLbl.style.fontSize  = 11;
            nameLbl.style.color     = new Color(0.88f, 0.9f, 0.96f);
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameRow.Add(nameLbl);

            var levelLbl = new Label($"Lv {inst.Level}");
            levelLbl.style.fontSize = 10;
            levelLbl.style.color    = new Color(0.65f, 0.75f, 0.9f);
            levelLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameRow.Add(levelLbl);

            card.Add(nameRow);

            // ── Row 2: formula + cap (muted detail line) ───────────────────────
            string formulaText = BuildFormulaLabel(def);
            bool showCap = !isProficient && def.proficiencyRequiredForMaxLevel;
            if (!string.IsNullOrEmpty(formulaText) || showCap)
            {
                var detailRow = new VisualElement();
                detailRow.style.flexShrink    = 0;
                detailRow.style.flexDirection = FlexDirection.Row;
                detailRow.style.alignItems    = Align.Center;
                detailRow.style.marginTop     = 1;
                detailRow.style.paddingLeft   = 14; // indented past pip

                if (!string.IsNullOrEmpty(formulaText))
                {
                    var formulaLbl = new Label(formulaText);
                    formulaLbl.style.fontSize  = 9;
                    formulaLbl.style.color     = new Color(0.45f, 0.48f, 0.58f);
                    formulaLbl.style.flexGrow  = 1;
                    detailRow.Add(formulaLbl);
                }

                if (showCap)
                {
                    var capLbl = new Label("Cap: " + SkillSystem.NonProficientLevelCap);
                    capLbl.style.fontSize = 9;
                    capLbl.style.color    = new Color(0.88f, 0.68f, 0.10f);
                    capLbl.tooltip = "Not proficient \u2014 XP at 50% rate, max level " + SkillSystem.NonProficientLevelCap;
                    detailRow.Add(capLbl);
                }

                card.Add(detailRow);
            }

            // ── Row 3: XP progress bar ─────────────────────────────────────────
            float levelFloor = SkillSystem.GetXPForLevel(inst.Level);
            float levelCeil  = SkillSystem.GetXPForLevel(inst.Level + 1);
            float xpRange    = levelCeil - levelFloor;
            float xpProgress = xpRange > 0f ? (inst.currentXP - levelFloor) / xpRange : 0f;

            var barContainer = new VisualElement();
            barContainer.style.flexShrink = 0;
            barContainer.style.marginTop  = 3;
            barContainer.style.paddingLeft = 14;
            barContainer.Add(MakeBar(xpProgress, new Color(0.3f, 0.55f, 0.9f)));
            card.Add(barContainer);

            // ── Row 4+: domain expertise slots ─────────────────────────────────
            if (def.domainExpertiseSlots != null && def.domainExpertiseSlots.Count > 0)
            {
                var slotsContainer = new VisualElement();
                slotsContainer.style.flexShrink  = 0;
                slotsContainer.style.marginTop  = 3;
                slotsContainer.style.paddingLeft = 14;

                foreach (var slotDef in def.domainExpertiseSlots)
                    slotsContainer.Add(BuildDomainExpertiseSlotRow(inst, def, slotDef));

                card.Add(slotsContainer);
            }

            return card;
        }

        /// <summary>
        /// Build a single domain expertise slot row showing the unlock level,
        /// option names, and the current claimed/unclaimed/locked state.
        /// </summary>
        private VisualElement BuildDomainExpertiseSlotRow(
            SkillInstance inst,
            SkillDefinition def,
            DomainExpertiseSlotDefinition slotDef)
        {
            var slotRow = new VisualElement();
            slotRow.style.flexShrink    = 0;
            slotRow.style.flexDirection = FlexDirection.Row;
            slotRow.style.alignItems    = Align.Center;
            slotRow.style.marginTop     = 2;

            // Determine state
            string claimedOptionName = null;
            bool isClaimed = false;
            foreach (var opt in slotDef.options)
            {
                if (_npc.chosenExpertise != null && _npc.chosenExpertise.Contains(opt.id))
                {
                    claimedOptionName = opt.name;
                    isClaimed = true;
                    break;
                }
            }

            bool levelReached = inst.Level >= slotDef.unlockLevel;
            bool isUnclaimedReachable = !isClaimed && levelReached;

            // Pip indicator
            var pip = new VisualElement();
            pip.AddToClassList(ExpertisePipClass);
            pip.style.width  = 8;
            pip.style.height = 8;
            pip.style.borderTopLeftRadius     = 4;
            pip.style.borderTopRightRadius    = 4;
            pip.style.borderBottomLeftRadius  = 4;
            pip.style.borderBottomRightRadius = 4;
            pip.style.marginRight      = 5;
            pip.style.borderTopWidth   = 1;
            pip.style.borderRightWidth = 1;
            pip.style.borderBottomWidth = 1;
            pip.style.borderLeftWidth  = 1;

            if (isClaimed)
            {
                pip.AddToClassList(ExpertisePipFilledClass);
                pip.style.backgroundColor  = new Color(0.3f, 0.55f, 0.9f);
                pip.style.borderTopColor   = new Color(0.4f, 0.65f, 1f);
                pip.style.borderRightColor = new Color(0.4f, 0.65f, 1f);
                pip.style.borderBottomColor = new Color(0.4f, 0.65f, 1f);
                pip.style.borderLeftColor  = new Color(0.4f, 0.65f, 1f);
            }
            else if (isUnclaimedReachable)
            {
                pip.AddToClassList(ExpertisePipPendingClass);
                pip.style.backgroundColor  = new Color(0.9f, 0.65f, 0.1f, 0.4f);
                pip.style.borderTopColor   = new Color(0.9f, 0.7f, 0.2f);
                pip.style.borderRightColor = new Color(0.9f, 0.7f, 0.2f);
                pip.style.borderBottomColor = new Color(0.9f, 0.7f, 0.2f);
                pip.style.borderLeftColor  = new Color(0.9f, 0.7f, 0.2f);

                string capturedSkillId = inst.skillId;
                string capturedNpcUid  = _npc.uid;
                pip.RegisterCallback<ClickEvent>(_ =>
                    OnExpertiseSlotClicked?.Invoke(capturedNpcUid, capturedSkillId));
            }
            else
            {
                pip.style.backgroundColor = Color.clear;
                pip.style.borderTopColor   = new Color(0.3f, 0.3f, 0.38f);
                pip.style.borderRightColor  = new Color(0.3f, 0.3f, 0.38f);
                pip.style.borderBottomColor = new Color(0.3f, 0.3f, 0.38f);
                pip.style.borderLeftColor   = new Color(0.3f, 0.3f, 0.38f);
            }

            slotRow.Add(pip);

            // Slot label
            string slotText;
            Color slotTextColor;
            if (isClaimed)
            {
                slotText = claimedOptionName;
                slotTextColor = new Color(0.84f, 0.90f, 1f);
            }
            else if (isUnclaimedReachable)
            {
                slotText = $"Lv {slotDef.unlockLevel} \u2014 Choose specialisation";
                slotTextColor = new Color(0.9f, 0.7f, 0.2f);
            }
            else
            {
                slotText = $"Lv {slotDef.unlockLevel} \u2014 Locked";
                slotTextColor = new Color(0.4f, 0.4f, 0.48f);
            }

            var slotLabel = new Label(slotText);
            slotLabel.style.fontSize = 10;
            slotLabel.style.color    = slotTextColor;
            slotLabel.style.flexGrow = 1;
            slotRow.Add(slotLabel);

            if (isUnclaimedReachable)
            {
                string capturedSkillId2 = inst.skillId;
                string capturedNpcUid2  = _npc.uid;
                slotRow.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == pip) return;
                    OnExpertiseSlotClicked?.Invoke(capturedNpcUid2, capturedSkillId2);
                });
            }

            return slotRow;
        }

        /// <summary>
        /// Builds a formula label string from a domain skill definition.
        /// PrimaryDominant: "WIS + INT/2"; EqualWeight: "(WIS + INT) / 2".
        /// </summary>
        private static string BuildFormulaLabel(SkillDefinition def)
        {
            if (!def.IsDomainSkill) return null;
            string p = def.primaryStat;
            string s = def.secondaryStat;
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(s)) return p ?? s ?? "";
            return def.weight == SkillWeight.EqualWeight
                ? $"({p} + {s}) / 2"
                : $"{p} + {s}/2";
        }

        // ── Relationships tab ──────────────────────────────────────────────────

        private void BuildRelationshipsTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            if (_station == null || _npc == null)
            {
                root.Add(MakeEmptyLabel("No relationship data."));
                return;
            }

            var relList = new List<(RelationshipRecord rec, string otherUid)>();
            foreach (var rec in _station.relationships.Values)
            {
                if (rec.npcUid1 == _npc.uid)
                    relList.Add((rec, rec.npcUid2));
                else if (rec.npcUid2 == _npc.uid)
                    relList.Add((rec, rec.npcUid1));
            }

            if (relList.Count == 0)
            {
                root.Add(MakeEmptyLabel("No relationships."));
                return;
            }

            // Header row
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingBottom = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.5f);
            header.style.marginBottom = 4;
            AddSmallLabel(header, "NPC",      flex: 1f);
            AddSmallLabel(header, "Type",     flex: 0.8f);
            AddSmallLabel(header, "Affinity", flex: 0.5f);
            root.Add(header);

            int currentTick = _station.tick;

            foreach (var (rec, otherUid) in relList)
            {
                _station.npcs.TryGetValue(otherUid, out var otherNpc);
                string otherName   = otherNpc?.name ?? otherUid;
                string typeLabel   = RelationshipTypeLabel(rec.relationshipType);
                string affinityTxt = $"{rec.affinityScore:F0}";

                // Decay warning: within 24 ticks of the 7-day inactivity threshold
                bool decayWarning = rec.lastInteractionTick >= 0 &&
                    (currentTick - rec.lastInteractionTick) >= (RelationshipRegistry.DecayIntervalTicks - 24);

                var row = new VisualElement();
                row.AddToClassList(RelRowClass);
                row.style.flexDirection  = FlexDirection.Row;
                row.style.alignItems     = Align.Center;
                row.style.paddingTop     = 4;
                row.style.paddingBottom  = 4;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.3f);
                row.style.marginBottom  = 2;

                var nameLbl = new Label(otherName);
                nameLbl.style.flexGrow  = 1;
                nameLbl.style.fontSize  = 12;
                row.Add(nameLbl);

                var badge = new Label(typeLabel);
                badge.AddToClassList(RelBadgeClass);
                badge.style.fontSize      = 10;
                badge.style.paddingLeft   = 4;
                badge.style.paddingRight  = 4;
                badge.style.paddingTop    = 2;
                badge.style.paddingBottom = 2;
                badge.style.borderTopLeftRadius     = 3;
                badge.style.borderTopRightRadius    = 3;
                badge.style.borderBottomLeftRadius  = 3;
                badge.style.borderBottomRightRadius = 3;
                badge.style.backgroundColor = RelationshipBadgeColor(rec.relationshipType);
                badge.style.color = Color.white;
                row.Add(badge);

                var affinityLbl = new Label(affinityTxt);
                affinityLbl.style.fontSize  = 12;
                affinityLbl.style.width     = 36;
                affinityLbl.style.unityTextAlign = TextAnchor.MiddleRight;
                affinityLbl.style.color = rec.affinityScore >= 0f
                    ? new Color(0.22f, 0.76f, 0.35f)
                    : new Color(0.86f, 0.26f, 0.26f);
                row.Add(affinityLbl);

                if (decayWarning)
                {
                    var warn = new Label("⚠");
                    warn.AddToClassList(DecayWarningClass);
                    warn.style.color    = new Color(0.9f, 0.65f, 0.1f);
                    warn.style.fontSize = 12;
                    warn.style.marginLeft = 4;
                    warn.tooltip = "Affinity may decay soon due to inactivity.";
                    row.Add(warn);
                }

                string capturedOtherUid = otherUid;
                row.RegisterCallback<ClickEvent>(_ =>
                    OnRelationshipRowClicked?.Invoke(capturedOtherUid));

                root.Add(row);
            }
        }

        // ── Inventory tab ──────────────────────────────────────────────────────

        private void BuildInventoryTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            // ── Equipped items ─────────────────────────────────────────────────
            AddSectionHeader(root, "EQUIPPED");

            if (_npc.equippedSlots == null || _npc.equippedSlots.Count == 0)
            {
                root.Add(MakeEmptyLabel("Nothing equipped."));
            }
            else
            {
                foreach (var kv in _npc.equippedSlots)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;
                    string itemName   = GetItemDisplayName(kv.Value);
                    float  itemWeight = GetItemWeight(kv.Value);
                    root.Add(BuildItemRow($"[{kv.Key}] {itemName}", itemWeight));
                }
            }

            // ── Pocket / carried items ─────────────────────────────────────────
            AddSectionHeader(root, "CARRIED");

            if (_npc.pocketItems == null || _npc.pocketItems.Count == 0)
            {
                root.Add(MakeEmptyLabel("Nothing carried."));
            }
            else
            {
                foreach (var kv in _npc.pocketItems)
                {
                    if (kv.Value <= 0) continue;
                    string itemName   = GetItemDisplayName(kv.Key);
                    float  itemWeight = GetItemWeight(kv.Key) * kv.Value;
                    string label      = kv.Value > 1 ? $"{itemName} ×{kv.Value}" : itemName;
                    root.Add(BuildItemRow(label, itemWeight));
                }
            }

            // ── Carry weight summary ────────────────────────────────────────────
            AddSectionHeader(root, "CARRY WEIGHT");

            float used     = _inventory != null ? _inventory.GetNpcCarryUsed(_npc) : 0f;
            float capacity = _inventory != null ? _inventory.GetNpcCarryCapacity(_npc) : 10f;
            float fillPct  = capacity > 0f ? Mathf.Clamp01(used / capacity) : 0f;
            Color fillColor = fillPct <= 0.7f ? new Color(0.22f, 0.76f, 0.35f)
                            : fillPct <= 0.9f ? new Color(0.88f, 0.68f, 0.10f)
                            : new Color(0.86f, 0.26f, 0.26f);

            var weightRow = new VisualElement();
            weightRow.style.flexDirection = FlexDirection.Row;
            weightRow.style.alignItems    = Align.Center;
            weightRow.style.marginBottom  = 4;

            var weightLbl = new Label($"{used:F1} / {capacity:F1} kg");
            weightLbl.style.flexGrow  = 1;
            weightLbl.style.fontSize  = 12;
            weightRow.Add(weightLbl);
            root.Add(weightRow);
            root.Add(MakeBar(fillPct, fillColor));
        }

        private VisualElement BuildItemRow(string name, float weight)
        {
            var row = new VisualElement();
            row.AddToClassList(InvRowClass);
            row.style.flexDirection   = FlexDirection.Row;
            row.style.alignItems      = Align.Center;
            row.style.paddingTop      = 2;
            row.style.paddingBottom   = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.25f, 0.25f, 0.3f, 0.3f);

            var nameLbl = new Label(name);
            nameLbl.style.flexGrow  = 1;
            nameLbl.style.fontSize  = 11;
            row.Add(nameLbl);

            var weightLbl = new Label($"{weight:F1} kg");
            weightLbl.style.fontSize = 11;
            weightLbl.style.color    = new Color(0.6f, 0.6f, 0.65f);
            row.Add(weightLbl);

            return row;
        }

        // ── History tab ────────────────────────────────────────────────────────

        private void BuildHistoryTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            // ── Backstory ──────────────────────────────────────────────────────
            AddSectionHeader(root, "BACKSTORY");

            string backstory = _npc.backstory;
            if (!string.IsNullOrWhiteSpace(backstory))
            {
                var bsLabel = new Label(backstory);
                bsLabel.style.fontSize    = 11;
                bsLabel.style.color       = new Color(0.75f, 0.75f, 0.8f);
                bsLabel.style.whiteSpace  = WhiteSpace.Normal;
                bsLabel.style.marginBottom = 6;
                root.Add(bsLabel);
            }
            else
            {
                root.Add(MakeEmptyLabel("No backstory recorded."));
            }

            // ── Life events ────────────────────────────────────────────────────
            AddSectionHeader(root, "LIFE EVENTS");

            if (_station != null)
            {
                string npcName = _npc.name ?? "";
                int shown = 0;
                for (int i = 0; i < _station.log.Count && shown < 30; i++)
                {
                    string entry = _station.log[i];
                    if (!string.IsNullOrEmpty(npcName) && entry.Contains(npcName))
                    {
                        var entryLbl = new Label($"• {entry}");
                        entryLbl.AddToClassList(HistoryEntryClass);
                        entryLbl.style.fontSize   = 11;
                        entryLbl.style.whiteSpace = WhiteSpace.Normal;
                        entryLbl.style.marginBottom = 2;
                        entryLbl.style.color      = new Color(0.65f, 0.65f, 0.7f);
                        root.Add(entryLbl);
                        shown++;
                    }
                }
                if (shown == 0)
                    root.Add(MakeEmptyLabel("No recorded events."));
            }
            else
            {
                root.Add(MakeEmptyLabel("No station data."));
            }

            // ── Traits ─────────────────────────────────────────────────────────
            AddSectionHeader(root, "TRAITS");

            var activeTraits = _npc.traitProfile?.traits;
            if (activeTraits == null || activeTraits.Count == 0)
            {
                root.Add(MakeEmptyLabel("No active traits."));
            }
            else
            {
                foreach (var activeTrait in activeTraits)
                {
                    NpcTraitDefinition def = null;
                    _traits?.TryGetTrait(activeTrait.traitId, out def);

                    string traitName = def?.displayName ?? activeTrait.traitId;
                    string traitDesc = def?.description ?? "";

                    var traitRow = new VisualElement();
                    traitRow.AddToClassList(TraitRowClass);
                    traitRow.style.marginBottom = 6;

                    var traitHeader = new VisualElement();
                    traitHeader.style.flexDirection = FlexDirection.Row;
                    traitHeader.style.alignItems    = Align.Center;

                    var traitNameLbl = new Label(traitName);
                    traitNameLbl.style.flexGrow  = 1;
                    traitNameLbl.style.fontSize  = 12;
                    traitNameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    traitHeader.Add(traitNameLbl);

                    var strengthLbl = new Label($"{activeTrait.strength * 100f:F0}%");
                    strengthLbl.style.fontSize = 11;
                    strengthLbl.style.color    = new Color(0.6f, 0.6f, 0.65f);
                    traitHeader.Add(strengthLbl);

                    traitRow.Add(traitHeader);

                    if (!string.IsNullOrEmpty(traitDesc))
                    {
                        var descLbl = new Label(traitDesc);
                        descLbl.style.fontSize   = 10;
                        descLbl.style.color      = new Color(0.55f, 0.55f, 0.6f);
                        descLbl.style.whiteSpace = WhiteSpace.Normal;
                        traitRow.Add(descLbl);
                    }

                    root.Add(traitRow);
                }
            }
        }

        // ── Shared helpers ─────────────────────────────────────────────────────

        private void AddSectionHeader(VisualElement parent, string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList(SectionHeaderClass);
            lbl.style.fontSize   = 10;
            lbl.style.color      = new Color(0.5f, 0.6f, 0.75f);
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.marginTop    = 12;
            lbl.style.marginBottom = 6;
            lbl.style.paddingBottom = 4;
            lbl.style.borderBottomWidth = 1;
            lbl.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.4f);
            parent.Add(lbl);
        }

        private VisualElement MakeBar(float fill, Color color, bool flexible = false)
        {
            var bg = new VisualElement();
            bg.AddToClassList(BarBgClass);
            bg.style.height           = 4;
            bg.style.backgroundColor  = new Color(0.25f, 0.25f, 0.3f, 0.8f);
            bg.style.borderTopLeftRadius     = 2;
            bg.style.borderTopRightRadius    = 2;
            bg.style.borderBottomLeftRadius  = 2;
            bg.style.borderBottomRightRadius = 2;
            bg.style.overflow = Overflow.Hidden;
            if (flexible)
                bg.style.flexGrow = 1;
            else
                bg.style.marginBottom = 2;

            var fillEl = new VisualElement();
            fillEl.AddToClassList(BarFillClass);
            fillEl.style.height          = Length.Percent(100);
            fillEl.style.width           = Length.Percent(Mathf.Clamp01(fill) * 100f);
            fillEl.style.backgroundColor = color;
            fillEl.style.borderTopLeftRadius     = 2;
            fillEl.style.borderTopRightRadius    = 2;
            fillEl.style.borderBottomLeftRadius  = 2;
            fillEl.style.borderBottomRightRadius = 2;

            bg.Add(fillEl);
            return bg;
        }

        private static Label MakeEmptyLabel(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("ws-crew-member-panel__empty");
            lbl.style.fontSize = 11;
            lbl.style.color    = new Color(0.5f, 0.5f, 0.55f);
            return lbl;
        }

        private static void AddSmallLabel(VisualElement parent, string text, float flex = 1f)
        {
            parent.Add(MakeSmallLabel(text, flex));
        }

        private static Label MakeSmallLabel(string text, float flex = 1f)
        {
            var lbl = new Label(text);
            lbl.style.fontSize = 10;
            lbl.style.color    = new Color(0.55f, 0.55f, 0.6f);
            lbl.style.flexGrow = flex;
            return lbl;
        }

        private string GetItemDisplayName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "—";
            if (_registry != null && _registry.Items.TryGetValue(itemId, out var def))
                return def.displayName ?? itemId;
            return itemId;
        }

        private float GetItemWeight(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0f;
            if (_registry != null && _registry.Items.TryGetValue(itemId, out var def))
                return def.weight;
            return 1f;
        }

        private static string RankLabel(int rank)
        {
            return rank switch
            {
                0 => "Crew",
                1 => "Officer",
                2 => "Senior Officer",
                3 => "Command",
                _ => ""
            };
        }

        private static string RelationshipTypeLabel(RelationshipType type)
        {
            return type switch
            {
                RelationshipType.Acquaintance => "Acquaintance",
                RelationshipType.Friend       => "Friend",
                RelationshipType.Mentor       => "Friend/Mentor",
                RelationshipType.Enemy        => "Rival/Enemy",
                RelationshipType.Lover        => "Lover",
                RelationshipType.Spouse       => "Spouse",
                _                             => "None",
            };
        }

        private static Color RelationshipBadgeColor(RelationshipType type)
        {
            return type switch
            {
                RelationshipType.Friend  => new Color(0.22f, 0.55f, 0.22f, 0.8f),
                RelationshipType.Mentor  => new Color(0.2f,  0.5f,  0.7f,  0.8f),
                RelationshipType.Lover   => new Color(0.7f,  0.25f, 0.45f, 0.8f),
                RelationshipType.Spouse  => new Color(0.75f, 0.3f,  0.5f,  0.8f),
                RelationshipType.Enemy   => new Color(0.65f, 0.15f, 0.15f, 0.8f),
                _                        => new Color(0.3f,  0.3f,  0.35f, 0.8f),
            };
        }
    }
}
