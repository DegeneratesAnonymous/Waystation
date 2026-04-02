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

            // ── Tab strip ──────────────────────────────────────────────────────
            _tabs = new TabStrip(TabStrip.Orientation.Horizontal);
            _tabs.OnTabSelected += OnTabSelected;
            _tabs.AddTab("VITALS",    "vitals");
            _tabs.AddTab("SKILLS",    "skills");
            _tabs.AddTab("RELATIONS", "relationships");
            _tabs.AddTab("INVENTORY", "inventory");
            _tabs.AddTab("HISTORY",   "history");
            Add(_tabs);

            // ── Tab content area ───────────────────────────────────────────────
            _tabContent = new ScrollView(ScrollViewMode.Vertical);
            _tabContent.AddToClassList(TabContentClass);
            _tabContent.style.flexGrow = 1;
            _tabContent.style.overflow = Overflow.Hidden;
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

            if (npc == null) return;

            // Update the header name.
            var nameLabel = this.Q<Label>("npc-name");
            if (nameLabel != null) nameLabel.text = npc.name ?? "Unknown";

            // Rebuild the active tab content.
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

            if (_npc == null) return;

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

            AddNeedBar(root, "Sleep",       _npc.sleepNeed?.value ?? 100f,       false);
            AddNeedBar(root, "Hunger",      _npc.hungerNeed?.value ?? 100f,      false);
            AddNeedBar(root, "Thirst",      _npc.thirstNeed?.value ?? 100f,      false);
            AddNeedBar(root, "Recreation",  _npc.recreationNeed?.value ?? 100f,  false);
            AddNeedBar(root, "Social",      _npc.socialNeed?.value ?? 50f,       false);
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

        private void BuildSkillsTab()
        {
            var root = _tabContent.contentContainer;
            root.style.paddingLeft   = 10;
            root.style.paddingRight  = 10;
            root.style.paddingTop    = 10;
            root.style.paddingBottom = 10;

            int charLevel = SkillSystem.GetCharacterLevel(_npc);
            string rankName = RankLabel(_npc.rank);

            var charRow = new VisualElement();
            charRow.style.flexDirection = FlexDirection.Row;
            charRow.style.marginBottom  = 10;

            var lvlLabel = new Label($"Character Level {charLevel}");
            lvlLabel.style.flexGrow  = 1;
            lvlLabel.style.fontSize  = 13;
            lvlLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            charRow.Add(lvlLabel);

            if (!string.IsNullOrEmpty(rankName))
            {
                var rankLbl = new Label(rankName);
                rankLbl.style.fontSize = 12;
                rankLbl.style.color    = new Color(0.65f, 0.65f, 0.75f);
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

            bool addedSimpleHeader   = false;
            bool addedAdvancedHeader = false;

            foreach (var inst in _npc.skillInstances)
            {
                if (_registry == null || !_registry.Skills.TryGetValue(inst.skillId, out var def))
                {
                    def = new SkillDefinition { skillId = inst.skillId, displayName = inst.skillId };
                }

                bool isAdvanced = def.skillType == SkillType.Advanced || def.IsDomainSkill;

                if (!isAdvanced && !addedSimpleHeader)
                {
                    AddSectionHeader(root, "SIMPLE SKILLS");
                    addedSimpleHeader = true;
                }
                else if (isAdvanced && !addedAdvancedHeader)
                {
                    AddSectionHeader(root, "ADVANCED SKILLS");
                    addedAdvancedHeader = true;
                }

                root.Add(BuildSkillRow(inst, def));
            }
        }

        private VisualElement BuildSkillRow(SkillInstance inst, SkillDefinition def)
        {
            var row = new VisualElement();
            row.AddToClassList(SkillRowClass);
            row.style.marginBottom = 6;

            // Top line: name + level
            var topLine = new VisualElement();
            topLine.style.flexDirection = FlexDirection.Row;
            topLine.style.alignItems    = Align.Center;
            topLine.style.marginBottom  = 2;

            var nameLbl = new Label(def.displayName ?? def.skillId);
            nameLbl.style.flexGrow  = 1;
            nameLbl.style.fontSize  = 12;
            topLine.Add(nameLbl);

            var levelLbl = new Label($"Lv {inst.Level}");
            levelLbl.style.fontSize = 11;
            levelLbl.style.color    = new Color(0.65f, 0.75f, 0.9f);
            topLine.Add(levelLbl);

            row.Add(topLine);

            // XP bar
            float levelFloor = SkillSystem.GetXPForLevel(inst.Level);
            float levelCeil  = SkillSystem.GetXPForLevel(inst.Level + 1);
            float xpRange    = levelCeil - levelFloor;
            float xpProgress = xpRange > 0f ? (inst.currentXP - levelFloor) / xpRange : 0f;

            row.Add(MakeBar(xpProgress, new Color(0.3f, 0.55f, 0.9f)));

            // Formula label for advanced skills
            bool isAdvanced = def.skillType == SkillType.Advanced || def.IsDomainSkill;
            if (isAdvanced && !string.IsNullOrEmpty(def.governingAbility))
            {
                var formulaLbl = new Label($"Formula: {def.governingAbility}");
                formulaLbl.style.fontSize = 10;
                formulaLbl.style.color    = new Color(0.5f, 0.5f, 0.6f);
                formulaLbl.style.marginTop = 1;
                row.Add(formulaLbl);
            }

            // Expertise slot pips
            int slotsEarned  = inst.Level / SkillSystem.SlotEverySkillLevels;
            int slotsChosen  = 0;
            if (_npc.chosenExpertise != null && _registry?.Expertises != null)
            {
                foreach (var expId in _npc.chosenExpertise)
                {
                    if (_registry.Expertises.TryGetValue(expId, out var expDef) &&
                        expDef.requiredSkillId == inst.skillId)
                        slotsChosen++;
                }
            }
            bool hasPendingSlot = _npc.pendingExpertiseSkillIds != null &&
                                  _npc.pendingExpertiseSkillIds.Contains(inst.skillId);

            if (slotsEarned > 0 || hasPendingSlot)
            {
                var pipRow = new VisualElement();
                pipRow.style.flexDirection = FlexDirection.Row;
                pipRow.style.marginTop     = 3;

                int totalPips = hasPendingSlot ? Mathf.Max(slotsEarned, slotsChosen + 1) : slotsEarned;
                for (int i = 0; i < totalPips; i++)
                {
                    bool filled  = i < slotsChosen;
                    bool pending = !filled && hasPendingSlot && i == slotsChosen;

                    var pip = new VisualElement();
                    pip.AddToClassList(ExpertisePipClass);

                    pip.style.width            = 12;
                    pip.style.height           = 12;
                    pip.style.borderTopLeftRadius     = 6;
                    pip.style.borderTopRightRadius    = 6;
                    pip.style.borderBottomLeftRadius  = 6;
                    pip.style.borderBottomRightRadius = 6;
                    pip.style.marginRight      = 4;
                    pip.style.borderTopWidth   = 1;
                    pip.style.borderRightWidth = 1;
                    pip.style.borderBottomWidth = 1;
                    pip.style.borderLeftWidth  = 1;

                    if (filled)
                    {
                        pip.AddToClassList(ExpertisePipFilledClass);
                        pip.style.backgroundColor  = new Color(0.3f, 0.55f, 0.9f);
                        pip.style.borderTopColor   = new Color(0.4f, 0.65f, 1f);
                        pip.style.borderRightColor = new Color(0.4f, 0.65f, 1f);
                        pip.style.borderBottomColor = new Color(0.4f, 0.65f, 1f);
                        pip.style.borderLeftColor  = new Color(0.4f, 0.65f, 1f);
                    }
                    else if (pending)
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
                        pip.style.borderTopColor   = new Color(0.35f, 0.35f, 0.4f);
                        pip.style.borderRightColor  = new Color(0.35f, 0.35f, 0.4f);
                        pip.style.borderBottomColor = new Color(0.35f, 0.35f, 0.4f);
                        pip.style.borderLeftColor   = new Color(0.35f, 0.35f, 0.4f);
                    }

                    pipRow.Add(pip);
                }

                row.Add(pipRow);

                // Clicking the skill row with a pending pip fires the modal
                if (hasPendingSlot)
                {
                    string capturedSkillId = inst.skillId;
                    string capturedNpcUid  = _npc.uid;
                    row.RegisterCallback<ClickEvent>(_ =>
                        OnExpertiseSlotClicked?.Invoke(capturedNpcUid, capturedSkillId));
                }
            }

            return row;
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
            lbl.style.marginTop    = 8;
            lbl.style.marginBottom = 4;
            lbl.style.borderBottomWidth = 1;
            lbl.style.borderBottomColor = new Color(0.3f, 0.3f, 0.4f, 0.4f);
            parent.Add(lbl);
        }

        private VisualElement MakeBar(float fill, Color color, bool flexible = false)
        {
            var bg = new VisualElement();
            bg.AddToClassList(BarBgClass);
            bg.style.height           = 6;
            bg.style.backgroundColor  = new Color(0.25f, 0.25f, 0.3f, 0.8f);
            bg.style.borderTopLeftRadius     = 3;
            bg.style.borderTopRightRadius    = 3;
            bg.style.borderBottomLeftRadius  = 3;
            bg.style.borderBottomRightRadius = 3;
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
            fillEl.style.borderTopLeftRadius     = 3;
            fillEl.style.borderTopRightRadius    = 3;
            fillEl.style.borderBottomLeftRadius  = 3;
            fillEl.style.borderBottomRightRadius = 3;

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
