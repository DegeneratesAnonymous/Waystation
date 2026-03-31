// CrewRosterSubPanelController.cs
// Crew → Roster sub-tab panel (UI-011).
//
// Displays:
//   * Filter controls — department dropdown, mood state (Normal / At Risk / Crisis),
//                       health state (Healthy / Injured / Critical).
//   * Sort controls   — by name, character level, mood, department.
//   * Crew list       — scrollable list; each row shows:
//                           • portrait circle (initials fallback)
//                           • name
//                           • department colour stripe (CategoryStripe left-edge)
//                           • current activity text
//                           • mood indicator dots (moodScore, stressScore axes)
//                           • health state indicator
//                         Clicking a row fires OnCrewRowClicked so the HUD
//                         controller can call SelectCrewMember(npcUid).
//
// Data is pushed via Refresh(StationState, DepartmentRegistry).
// Call on mount and again on every GameManager.OnTick (throttled every 5 ticks
// in WaystationHUDController to avoid GC churn).
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
    /// Crew → Roster sub-tab panel.  Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class CrewRosterSubPanelController : VisualElement
    {
        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when the player clicks a crew row.
        /// Argument is the NPC uid to pass to SelectCrewMember.
        /// </summary>
        public event Action<string> OnCrewRowClicked;

        // ── Mood / health filter enums ─────────────────────────────────────────

        public enum MoodFilter   { All, Normal, AtRisk, Crisis }
        public enum HealthFilter { All, Healthy, Injured, Critical }
        public enum SortMode     { Name, Level, Mood, Department }

        // ── Mood / health thresholds ───────────────────────────────────────────

        // moodScore is 0-100 (50 = baseline).  Crisis < 20 (driven by inCrisis flag).
        // AtRisk = not in crisis but moodScore < 30.
        private const float MoodAtRiskThreshold = 30f;

        // injuries: 0 = healthy, 1-2 = injured, 3+ = critical.
        private const int InjuryCriticalThreshold = 3;

        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass        = "ws-crew-roster-panel";
        private const string FilterRowClass    = "ws-crew-roster-panel__filter-row";
        private const string FilterLabelClass  = "ws-crew-roster-panel__filter-label";
        private const string SortStripClass    = "ws-crew-roster-panel__sort-strip";
        private const string SortBtnClass      = "ws-crew-roster-panel__sort-btn";
        private const string SortBtnActive     = "ws-crew-roster-panel__sort-btn--active";
        private const string ListClass         = "ws-crew-roster-panel__crew-list";
        private const string RowClass          = "ws-crew-roster-panel__crew-row";
        private const string PortraitClass     = "ws-crew-roster-panel__portrait";
        private const string NameClass         = "ws-crew-roster-panel__crew-name";
        private const string ActivityClass     = "ws-crew-roster-panel__activity";
        private const string MoodDotsClass     = "ws-crew-roster-panel__mood-dots";
        private const string HealthPipClass    = "ws-crew-roster-panel__health-pip";
        private const string EmptyClass        = "ws-crew-roster-panel__empty";

        // ── Child elements ─────────────────────────────────────────────────────

        private readonly VisualElement _filterRow;
        private readonly DropdownField _deptDropdown;
        private readonly DropdownField _moodDropdown;
        private readonly DropdownField _healthDropdown;
        private readonly VisualElement _sortStrip;
        private readonly VisualElement _crewList;
        private readonly Label         _emptyLabel;

        // Sort button map
        private readonly Dictionary<SortMode, Button> _sortButtons =
            new Dictionary<SortMode, Button>();

        // ── State ──────────────────────────────────────────────────────────────

        private string       _activeDeptFilter = null;   // null = All departments
        private MoodFilter   _moodFilter        = MoodFilter.All;
        private HealthFilter _healthFilter      = HealthFilter.All;
        private SortMode     _sortMode          = SortMode.Name;

        // Latest snapshot; rebuilt in Refresh().
        private List<NPCInstance>     _crew       = new List<NPCInstance>();
        private List<Department>      _departments = new List<Department>();
        private DepartmentRegistry    _deptRegistry;

        // ── Constructor ────────────────────────────────────────────────────────

        public CrewRosterSubPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            // ── Filter row ─────────────────────────────────────────────────────
            _filterRow = new VisualElement();
            _filterRow.AddToClassList(FilterRowClass);
            _filterRow.style.flexDirection  = FlexDirection.Row;
            _filterRow.style.flexWrap       = Wrap.Wrap;
            _filterRow.style.marginBottom   = 6;
            _filterRow.style.alignItems     = Align.Center;

            // Department dropdown
            _deptDropdown = new DropdownField();
            _deptDropdown.AddToClassList(FilterLabelClass);
            _deptDropdown.style.minWidth   = 90;
            _deptDropdown.style.marginRight = 4;
            _deptDropdown.style.marginBottom = 3;
            _deptDropdown.RegisterValueChangedCallback(OnDeptDropdownChanged);
            _filterRow.Add(_deptDropdown);

            // Mood dropdown
            _moodDropdown = new DropdownField("Mood",
                new List<string> { "All Moods", "Normal", "At Risk", "Crisis" }, 0);
            _moodDropdown.AddToClassList(FilterLabelClass);
            _moodDropdown.style.minWidth    = 80;
            _moodDropdown.style.marginRight = 4;
            _moodDropdown.style.marginBottom = 3;
            _moodDropdown.RegisterValueChangedCallback(_ => OnMoodDropdownChanged());
            _filterRow.Add(_moodDropdown);

            // Health dropdown
            _healthDropdown = new DropdownField("Health",
                new List<string> { "All Health", "Healthy", "Injured", "Critical" }, 0);
            _healthDropdown.AddToClassList(FilterLabelClass);
            _healthDropdown.style.minWidth    = 80;
            _healthDropdown.style.marginRight = 4;
            _healthDropdown.style.marginBottom = 3;
            _healthDropdown.RegisterValueChangedCallback(_ => OnHealthDropdownChanged());
            _filterRow.Add(_healthDropdown);

            Add(_filterRow);

            // ── Sort strip ─────────────────────────────────────────────────────
            _sortStrip = new VisualElement();
            _sortStrip.AddToClassList(SortStripClass);
            _sortStrip.style.flexDirection = FlexDirection.Row;
            _sortStrip.style.marginBottom  = 6;

            foreach (SortMode mode in Enum.GetValues(typeof(SortMode)))
            {
                var btn = new Button();
                btn.AddToClassList(SortBtnClass);
                btn.text              = mode.ToString().ToUpper();
                btn.style.marginRight = 2;
                SortMode captured = mode;
                btn.RegisterCallback<ClickEvent>(_ => OnSortButtonClicked(captured));
                _sortStrip.Add(btn);
                _sortButtons[mode] = btn;
            }
            UpdateSortButtonStyles();
            Add(_sortStrip);

            // ── Crew list (scrollable) ─────────────────────────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            _crewList = scroll.contentContainer;
            _crewList.AddToClassList(ListClass);
            _crewList.style.flexDirection = FlexDirection.Column;

            _emptyLabel = new Label("No crew found.");
            _emptyLabel.AddToClassList(EmptyClass);
            _emptyLabel.style.opacity  = 0.5f;
            _emptyLabel.style.marginTop = 8;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the crew list from the latest station state.
        /// Call once on mount and again on every OnTick (throttled every 5 ticks).
        /// </summary>
        public void Refresh(StationState station, DepartmentRegistry deptRegistry)
        {
            if (station == null) return;

            _departments  = station.departments ?? new List<Department>();
            _deptRegistry = deptRegistry;
            _crew         = station.GetCrew();

            RebuildDeptDropdown();
            RebuildCrewList();
        }

        // ── Department dropdown ────────────────────────────────────────────────

        private void RebuildDeptDropdown()
        {
            var choices = new List<string> { "All Depts" };
            foreach (var dept in _departments)
                choices.Add(dept.name);

            // Preserve the current selection if it still exists.
            string prevValue = _deptDropdown.value;
            _deptDropdown.choices = choices;
            _deptDropdown.value = choices.Contains(prevValue) ? prevValue : choices[0];

            // Sync internal filter state with the displayed value.
            _activeDeptFilter = _deptDropdown.value == "All Depts" ? null : _deptDropdown.value;
        }

        private void OnDeptDropdownChanged(ChangeEvent<string> evt)
        {
            _activeDeptFilter = (evt.newValue == "All Depts") ? null : evt.newValue;
            ApplyFilterAndSort();
        }

        // ── Mood / health dropdowns ────────────────────────────────────────────

        private void OnMoodDropdownChanged()
        {
            _moodFilter = _moodDropdown.value switch
            {
                "Normal"   => MoodFilter.Normal,
                "At Risk"  => MoodFilter.AtRisk,
                "Crisis"   => MoodFilter.Crisis,
                _          => MoodFilter.All,
            };
            ApplyFilterAndSort();
        }

        private void OnHealthDropdownChanged()
        {
            _healthFilter = _healthDropdown.value switch
            {
                "Healthy"  => HealthFilter.Healthy,
                "Injured"  => HealthFilter.Injured,
                "Critical" => HealthFilter.Critical,
                _          => HealthFilter.All,
            };
            ApplyFilterAndSort();
        }

        // ── Sort ───────────────────────────────────────────────────────────────

        private void OnSortButtonClicked(SortMode mode)
        {
            _sortMode = mode;
            UpdateSortButtonStyles();
            ApplyFilterAndSort();
        }

        private void UpdateSortButtonStyles()
        {
            foreach (var kv in _sortButtons)
                kv.Value.EnableInClassList(SortBtnActive, kv.Key == _sortMode);
        }

        // ── Crew list ──────────────────────────────────────────────────────────

        private void RebuildCrewList()
        {
            ApplyFilterAndSort();
        }

        private VisualElement BuildCrewRow(NPCInstance npc)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection  = FlexDirection.Row;
            row.style.alignItems     = Align.Center;
            row.style.paddingTop     = 4;
            row.style.paddingBottom  = 4;
            row.style.paddingRight   = 6;
            row.style.marginBottom   = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.35f, 0.5f);

            // Store uid for click
            row.userData = npc.uid;

            // ── Department colour stripe (left edge) ─────────────────────────
            var stripe = new CategoryStripe();
            var deptColor = GetDeptColor(npc.departmentId);
            if (deptColor.HasValue)
                stripe.StripeColor = deptColor.Value;
            else
                stripe.StripeColor = new Color(0.35f, 0.35f, 0.4f, 0.6f);
            row.Add(stripe);

            // ── Portrait circle ──────────────────────────────────────────────
            var portrait = new Label(GetInitials(npc.name));
            portrait.AddToClassList(PortraitClass);
            portrait.style.width              = 28;
            portrait.style.height             = 28;
            portrait.style.flexShrink         = 0;
            portrait.style.borderTopLeftRadius     = 14;
            portrait.style.borderTopRightRadius    = 14;
            portrait.style.borderBottomLeftRadius  = 14;
            portrait.style.borderBottomRightRadius = 14;
            portrait.style.backgroundColor    = GetPortraitColor(npc.departmentId);
            portrait.style.unityTextAlign     = TextAnchor.MiddleCenter;
            portrait.style.fontSize           = 10;
            portrait.style.color              = new Color(0.9f, 0.9f, 0.95f, 1f);
            portrait.style.marginLeft         = 4;
            portrait.style.marginRight        = 6;
            row.Add(portrait);

            // ── Name + activity (stacked) ────────────────────────────────────
            var nameStack = new VisualElement();
            nameStack.style.flexDirection = FlexDirection.Column;
            nameStack.style.flexGrow      = 1;

            var nameLabel = new Label(npc.name);
            nameLabel.AddToClassList(NameClass);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.fontSize       = 12;

            var activityLabel = new Label(GetActivityText(npc));
            activityLabel.AddToClassList(ActivityClass);
            activityLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            activityLabel.style.fontSize       = 9;
            activityLabel.style.opacity        = 0.6f;

            nameStack.Add(nameLabel);
            nameStack.Add(activityLabel);
            row.Add(nameStack);

            // ── Mood dots ────────────────────────────────────────────────────
            var moodDots = new VisualElement();
            moodDots.AddToClassList(MoodDotsClass);
            moodDots.style.flexDirection = FlexDirection.Column;
            moodDots.style.alignItems    = Align.Center;
            moodDots.style.marginRight   = 6;
            moodDots.style.width         = 10;

            // Happy/sad dot (moodScore 0-100; green=high, red=low)
            var happyDot = BuildMoodDot(GetMoodDotColor(npc.moodScore));
            // Calm/stressed dot (stressScore 0-100; green=high/calm, orange=low/stressed)
            var calmDot  = BuildMoodDot(GetStressDotColor(npc.stressScore));

            moodDots.Add(happyDot);
            moodDots.Add(calmDot);
            row.Add(moodDots);

            // ── Health pip ───────────────────────────────────────────────────
            var healthPip = new VisualElement();
            healthPip.AddToClassList(HealthPipClass);
            healthPip.style.width  = 8;
            healthPip.style.height = 8;
            healthPip.style.flexShrink = 0;
            healthPip.style.borderTopLeftRadius     = 4;
            healthPip.style.borderTopRightRadius    = 4;
            healthPip.style.borderBottomLeftRadius  = 4;
            healthPip.style.borderBottomRightRadius = 4;
            healthPip.style.backgroundColor = GetHealthColor(npc.injuries);
            row.Add(healthPip);

            // ── Click handler ────────────────────────────────────────────────
            string capturedUid = npc.uid;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                Debug.Log($"[CrewRosterPanel] Crew row clicked: {capturedUid}");
                OnCrewRowClicked?.Invoke(capturedUid);
            });

            return row;
        }

        // ── Filter + sort ──────────────────────────────────────────────────────

        private void ApplyFilterAndSort()
        {
            // Build a filtered and sorted snapshot from the data list.
            var filtered = new List<NPCInstance>();
            foreach (var npc in _crew)
            {
                if (PassesFilters(npc))
                    filtered.Add(npc);
            }

            SortNpcs(filtered);

            // Rebuild the container from the sorted/filtered list.
            _crewList.Clear();
            if (filtered.Count == 0)
            {
                _crewList.Add(_emptyLabel);
                return;
            }

            foreach (var npc in filtered)
                _crewList.Add(BuildCrewRow(npc));
        }

        private bool PassesFilters(NPCInstance npc)
        {
            // Department filter
            if (_activeDeptFilter != null)
            {
                var dept = _departments.Find(d => d.name == _activeDeptFilter);
                if (dept == null || npc.departmentId != dept.uid) return false;
            }

            // Mood filter
            if (_moodFilter != MoodFilter.All)
            {
                if (GetMoodState(npc) != _moodFilter) return false;
            }

            // Health filter
            if (_healthFilter != HealthFilter.All)
            {
                if (GetHealthState(npc.injuries) != _healthFilter) return false;
            }

            return true;
        }

        private void SortNpcs(List<NPCInstance> list)
        {
            switch (_sortMode)
            {
                case SortMode.Level:
                    list.Sort((a, b) =>
                    {
                        int cmp = GetCharLevel(b) - GetCharLevel(a);  // descending
                        return cmp != 0 ? cmp : string.Compare(a.name, b.name, StringComparison.Ordinal);
                    });
                    break;
                case SortMode.Mood:
                    list.Sort((a, b) =>
                    {
                        int cmp = b.moodScore.CompareTo(a.moodScore);  // descending
                        return cmp != 0 ? cmp : string.Compare(a.name, b.name, StringComparison.Ordinal);
                    });
                    break;
                case SortMode.Department:
                    list.Sort((a, b) =>
                    {
                        int cmp = string.Compare(GetDeptName(a.departmentId),
                                                 GetDeptName(b.departmentId),
                                                 StringComparison.Ordinal);
                        return cmp != 0 ? cmp : string.Compare(a.name, b.name, StringComparison.Ordinal);
                    });
                    break;
                default:  // Name (ascending)
                    list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                    break;
            }
        }

        // ── State helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Classifies a crew member's mood state for filtering.
        /// Crisis = inCrisis flag; AtRisk = moodScore below threshold; Normal otherwise.
        /// </summary>
        public static MoodFilter GetMoodState(NPCInstance npc)
        {
            if (npc.inCrisis)                          return MoodFilter.Crisis;
            if (npc.moodScore < MoodAtRiskThreshold)   return MoodFilter.AtRisk;
            return MoodFilter.Normal;
        }

        /// <summary>
        /// Classifies a crew member's health state for filtering.
        /// </summary>
        public static HealthFilter GetHealthState(int injuries)
        {
            if (injuries >= InjuryCriticalThreshold) return HealthFilter.Critical;
            if (injuries > 0)                        return HealthFilter.Injured;
            return HealthFilter.Healthy;
        }

        // ── Visual helpers ─────────────────────────────────────────────────────

        private static VisualElement BuildMoodDot(Color color)
        {
            var dot = new VisualElement();
            dot.style.width  = 6;
            dot.style.height = 6;
            dot.style.borderTopLeftRadius     = 3;
            dot.style.borderTopRightRadius    = 3;
            dot.style.borderBottomLeftRadius  = 3;
            dot.style.borderBottomRightRadius = 3;
            dot.style.backgroundColor  = color;
            dot.style.marginBottom     = 2;
            return dot;
        }

        private static Color GetMoodDotColor(float moodScore)
        {
            // moodScore 0-100 (50=baseline). Green at 80+, yellow at 40-80, red below 40.
            if (moodScore >= 80f) return new Color(0.3f, 0.85f, 0.4f, 1f);
            if (moodScore >= 40f) return new Color(0.9f, 0.75f, 0.2f, 1f);
            return new Color(0.9f, 0.3f, 0.25f, 1f);
        }

        private static Color GetStressDotColor(float stressScore)
        {
            // stressScore 0-100 (50=baseline). 100=calm/green, 0=stressed/orange.
            if (stressScore >= 70f) return new Color(0.3f, 0.7f, 0.85f, 1f);
            if (stressScore >= 35f) return new Color(0.9f, 0.65f, 0.2f, 1f);
            return new Color(0.85f, 0.4f, 0.2f, 1f);
        }

        private static Color GetHealthColor(int injuries)
        {
            if (injuries >= InjuryCriticalThreshold) return new Color(0.9f, 0.25f, 0.2f, 1f);
            if (injuries > 0)                        return new Color(0.9f, 0.65f, 0.2f, 1f);
            return new Color(0.3f, 0.85f, 0.4f, 1f);
        }

        private Color? GetDeptColor(string deptUid)
        {
            if (_deptRegistry != null && !string.IsNullOrEmpty(deptUid))
                return _deptRegistry.GetDeptColour(deptUid);
            // Fallback: read directly from the Department object.
            if (!string.IsNullOrEmpty(deptUid))
                return _departments.Find(d => d.uid == deptUid)?.GetColour();
            return null;
        }

        private Color GetPortraitColor(string deptUid)
        {
            var c = GetDeptColor(deptUid);
            if (c.HasValue)
                return new Color(c.Value.r * 0.4f, c.Value.g * 0.4f, c.Value.b * 0.4f, 1f);
            return new Color(0.22f, 0.28f, 0.38f, 1f);
        }

        private string GetDeptName(string deptUid)
        {
            if (string.IsNullOrEmpty(deptUid)) return "";
            return _departments.Find(d => d.uid == deptUid)?.name ?? "";
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][0].ToString().ToUpper();
            return (parts[0][0].ToString() + parts[parts.Length - 1][0].ToString()).ToUpper();
        }

        private static string GetActivityText(NPCInstance npc)
        {
            if (npc.isSleeping) return "Sleeping";
            if (npc.missionUid != null) return "On mission";
            if (!string.IsNullOrEmpty(npc.currentTaskId))
                return npc.currentTaskId.Replace("task.", "").Replace("_", " ");
            if (!string.IsNullOrEmpty(npc.currentJobId))
                return npc.currentJobId.Replace("job.", "").Replace("_", " ");
            return "Idle";
        }

        private static int GetCharLevel(NPCInstance npc)
        {
            return SkillSystem.GetCharacterLevel(npc);
        }
    }
}
