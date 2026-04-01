// StationOverviewController.cs
// Station -> Overview sub-panel (UI-006).
//
// Displays:
//   * Resource section  -- one ResourceMeter row per tracked resource, with
//                          per-tick delta and warning/depleted colour state.
//   * Room bonus section -- active room type bonuses from StationState.roomBonusCache.
//   * Station condition  -- uptime counter (ticks) and overall damage indicator.
//   * Department summary -- crew count and head-assignment status per department;
//                           clicking a row fires OnDepartmentRowClicked(deptUid).
//
// Data is pushed in via Refresh(StationState, ResourceSystem). Call this once on
// construction and again on every OnTick to keep the panel live.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel only mounts inside
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
    /// Station -> Overview sub-panel. Extends <see cref="VisualElement"/> so it
    /// can be added directly to the side-panel drawer.
    /// </summary>
    public class StationOverviewController : VisualElement
    {
        // -- Events -----------------------------------------------------------

        /// <summary>
        /// Fired when the user clicks a department row.
        /// Argument is the department uid; the caller should navigate to
        /// Crew -> Departments for that department.
        /// </summary>
        public event Action<string> OnDepartmentRowClicked;

        // -- Section containers -----------------------------------------------

        private readonly VisualElement _resourceSection;
        private readonly VisualElement _roomBonusSection;
        private readonly VisualElement _deptSection;

        // -- Per-resource meter rows (keyed by resource id) -------------------

        private readonly Dictionary<string, ResourceMeterRow> _resourceRows =
            new Dictionary<string, ResourceMeterRow>();

        // Stores previous-tick resource values so we can compute per-tick delta.
        private readonly Dictionary<string, float> _prevValues =
            new Dictionary<string, float>();

        // -- Room bonus rows (keyed by roomKey) --------------------------------

        private readonly Dictionary<string, RoomBonusRow> _roomBonusRows =
            new Dictionary<string, RoomBonusRow>();

        // Cached empty-state label so we don't recreate it every tick.
        private Label _roomBonusEmptyLabel;

        // -- Department rows (keyed by dept uid) ------------------------------

        private readonly Dictionary<string, DeptRow> _deptRows =
            new Dictionary<string, DeptRow>();

        // Cached empty-state label.
        private Label _deptEmptyLabel;

        // -- Station condition labels -----------------------------------------

        private readonly Label _uptimeLabel;
        private readonly Label _damageLabel;

        // -- Constructor ------------------------------------------------------

        public StationOverviewController()
        {
            AddToClassList("ws-station-overview");

            style.flexDirection   = FlexDirection.Column;
            style.flexGrow        = 1;
            style.paddingLeft     = 8;
            style.paddingRight    = 8;
            style.paddingTop      = 8;
            style.paddingBottom   = 8;
            style.overflow        = Overflow.Hidden;

            // Scrollable content wrapper
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            var content = scroll.contentContainer;
            content.style.flexDirection = FlexDirection.Column;

            // -- Resources ----------------------------------------------------
            _resourceSection = BuildSection(content, "RESOURCES");

            // -- Room bonuses -------------------------------------------------
            _roomBonusSection = BuildSection(content, "ROOM BONUSES");

            // -- Station condition --------------------------------------------
            var condSection = BuildSection(content, "STATION CONDITION");

            _uptimeLabel = BuildInfoRow(condSection, "UPTIME");
            _damageLabel = BuildInfoRow(condSection, "DAMAGE STATE");

            // -- Department summary -------------------------------------------
            _deptSection = BuildSection(content, "DEPARTMENTS");
        }

        // -- Public API -------------------------------------------------------

        /// <summary>
        /// Refreshes all sections using the latest <paramref name="station"/> state.
        /// <paramref name="resources"/> is used to resolve soft caps and warning
        /// thresholds when available; pass null to use built-in defaults for
        /// normalisation (500f soft cap, no warning threshold).
        /// </summary>
        public void Refresh(StationState station, ResourceSystem resources = null)
        {
            if (station == null) return;

            RefreshResources(station, resources);
            RefreshRoomBonuses(station);
            RefreshCondition(station);
            RefreshDepartments(station);
        }

        // -- Resource section -------------------------------------------------

        private void RefreshResources(StationState station, ResourceSystem resources)
        {
            // Synchronise rows: add missing, remove stale.
            var currentKeys = new HashSet<string>(station.resources.Keys);

            // Remove rows for resources no longer tracked.
            var toRemove = new List<string>();
            foreach (var key in _resourceRows.Keys)
                if (!currentKeys.Contains(key))
                    toRemove.Add(key);
            foreach (var key in toRemove)
            {
                _resourceRows[key].Root.RemoveFromHierarchy();
                _resourceRows.Remove(key);
                _prevValues.Remove(key);
            }

            // Add rows for newly tracked resources; update existing rows.
            foreach (var kv in station.resources)
            {
                string id    = kv.Key;
                float  value = kv.Value;

                float softCap   = resources != null ? resources.GetResourceSoftCap(id)         : 500f;
                float threshold = resources != null ? resources.GetResourceWarningThreshold(id) : 0f;

                // Compute per-tick rate. For the first tick of a resource, treat the
                // current value as the baseline and report a zero rate.
                bool  hadPrev = _prevValues.TryGetValue(id, out float prev);
                float rate    = hadPrev ? (value - prev) : 0f;
                _prevValues[id] = value;

                bool isWarning  = threshold > 0f && value > 0f && value < threshold;
                bool isDepleted = value <= 0f;

                if (!_resourceRows.TryGetValue(id, out var row))
                {
                    row = new ResourceMeterRow(id);
                    _resourceSection.Add(row.Root);
                    _resourceRows[id] = row;
                }

                row.Update(value, softCap, rate, isWarning, isDepleted);
            }
        }

        // -- Room bonus section -----------------------------------------------

        private void RefreshRoomBonuses(StationState station)
        {
            // Build the set of currently active bonus keys.
            var activeKeys = new HashSet<string>();
            foreach (var bs in station.roomBonusCache.Values)
            {
                if (!bs.bonusActive) continue;
                string key = bs.roomKey ?? bs.workbenchRoomType ?? bs.displayName;
                if (!string.IsNullOrEmpty(key))
                    activeKeys.Add(key);
            }

            bool anyActive = activeKeys.Count > 0;

            // Remove rows for bonuses no longer active.
            var toRemove = new List<string>();
            foreach (var key in _roomBonusRows.Keys)
                if (!activeKeys.Contains(key))
                    toRemove.Add(key);
            foreach (var key in toRemove)
            {
                _roomBonusRows[key].Root.RemoveFromHierarchy();
                _roomBonusRows.Remove(key);
            }

            if (anyActive)
            {
                // Hide the empty-state label when there are active bonuses.
                _roomBonusEmptyLabel?.RemoveFromHierarchy();

                // Add or update rows for active bonuses.
                foreach (var bs in station.roomBonusCache.Values)
                {
                    if (!bs.bonusActive) continue;
                    string key = bs.roomKey ?? bs.workbenchRoomType ?? bs.displayName;
                    if (string.IsNullOrEmpty(key)) continue;

                    string nameText = bs.displayName ?? bs.workbenchRoomType ?? bs.roomKey;
                    string typeText = bs.workbenchRoomType ?? "-";

                    if (!_roomBonusRows.TryGetValue(key, out var row))
                    {
                        row = new RoomBonusRow();
                        _roomBonusRows[key] = row;
                        _roomBonusSection.Add(row.Root);
                    }

                    row.Update(nameText, typeText);
                }
            }
            else
            {
                // No active bonuses -- show (and cache) the empty-state label.
                if (_roomBonusEmptyLabel == null)
                {
                    _roomBonusEmptyLabel = new Label("No active room bonuses.");
                    _roomBonusEmptyLabel.AddToClassList("ws-station-overview__empty");
                    _roomBonusEmptyLabel.AddToClassList("ws-text-dim");
                    _roomBonusEmptyLabel.style.fontSize = 11;
                    _roomBonusEmptyLabel.style.opacity = 0.5f;
                }

                if (_roomBonusEmptyLabel.parent != _roomBonusSection)
                    _roomBonusSection.Add(_roomBonusEmptyLabel);
            }
        }

        // -- Station condition section ----------------------------------------

        private void RefreshCondition(StationState station)
        {
            _uptimeLabel.text = station.tick.ToString("N0") + " ticks";

            // Overall damage: average module damage across all modules.
            float totalDamage = 0f;
            int   moduleCount = 0;
            foreach (var mod in station.modules.Values)
            {
                totalDamage += mod.damage;
                moduleCount++;
            }

            string damageText;
            Color  damageColor;

            if (moduleCount == 0)
            {
                damageText  = "No damage";
                damageColor = new Color(0.24f, 0.78f, 0.44f, 1f);   // green
            }
            else
            {
                float avg = totalDamage / moduleCount;
                if (avg <= 0f)
                {
                    damageText  = "Nominal";
                    damageColor = new Color(0.24f, 0.78f, 0.44f, 1f); // green
                }
                else if (avg < 0.3f)
                {
                    damageText  = $"Minor damage ({avg:P0})";
                    damageColor = new Color(0.86f, 0.66f, 0.16f, 1f); // amber
                }
                else if (avg < 0.7f)
                {
                    damageText  = $"Moderate damage ({avg:P0})";
                    damageColor = new Color(0.86f, 0.47f, 0.13f, 1f); // orange
                }
                else
                {
                    damageText  = $"Critical damage ({avg:P0})";
                    damageColor = new Color(0.88f, 0.22f, 0.22f, 1f); // red
                }
            }

            _damageLabel.text        = damageText;
            _damageLabel.style.color = damageColor;
        }

        // -- Department summary section ---------------------------------------

        private void RefreshDepartments(StationState station)
        {
            if (station.departments == null || station.departments.Count == 0)
            {
                // Remove any existing dept rows.
                foreach (var dr in _deptRows.Values)
                    dr.Root.RemoveFromHierarchy();
                _deptRows.Clear();

                // Show empty-state label.
                if (_deptEmptyLabel == null)
                {
                    _deptEmptyLabel = new Label("No departments defined.");
                    _deptEmptyLabel.AddToClassList("ws-station-overview__empty");
                    _deptEmptyLabel.AddToClassList("ws-text-dim");
                    _deptEmptyLabel.style.fontSize = 11;
                    _deptEmptyLabel.style.opacity = 0.5f;
                }

                if (_deptEmptyLabel.parent != _deptSection)
                    _deptSection.Add(_deptEmptyLabel);
                return;
            }

            // When there are departments, hide the empty label.
            _deptEmptyLabel?.RemoveFromHierarchy();

            // Build set of current dept uids.
            var currentUids = new HashSet<string>();
            foreach (var dept in station.departments)
                currentUids.Add(dept.uid);

            // Remove rows for departments that no longer exist.
            var toRemove = new List<string>();
            foreach (var uid in _deptRows.Keys)
                if (!currentUids.Contains(uid))
                    toRemove.Add(uid);
            foreach (var uid in toRemove)
            {
                _deptRows[uid].Root.RemoveFromHierarchy();
                _deptRows.Remove(uid);
            }

            // Pre-compute crew counts per department.
            var crewCounts = new Dictionary<string, int>();
            foreach (var npc in station.GetCrew())
            {
                string deptId = npc.departmentId ?? "";
                crewCounts.TryGetValue(deptId, out int count);
                crewCounts[deptId] = count + 1;
            }

            foreach (var dept in station.departments)
            {
                crewCounts.TryGetValue(dept.uid, out int deptCrew);
                bool hasHead = !string.IsNullOrEmpty(dept.headNpcUid);

                if (!_deptRows.TryGetValue(dept.uid, out var row))
                {
                    string capturedUid = dept.uid;
                    row = new DeptRow(capturedUid, dept.name ?? dept.uid,
                                     () => OnDepartmentRowClicked?.Invoke(capturedUid));
                    _deptRows[dept.uid] = row;
                    _deptSection.Add(row.Root);
                }

                row.Update(dept.name ?? dept.uid, deptCrew, hasHead);
            }
        }

        // -- Layout helpers ---------------------------------------------------

        /// <summary>Builds a labelled section header and returns a child container.</summary>
        private static VisualElement BuildSection(VisualElement parent, string title)
        {
            var header = new Label(title);
            header.AddToClassList("ws-station-overview__section-header");
            header.AddToClassList("ws-text-acc");
            header.style.fontSize                = 10;
            header.style.marginTop               = 12;
            header.style.marginBottom            = 4;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(header);

            var container = new VisualElement();
            container.AddToClassList("ws-station-overview__section");
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom  = 4;
            parent.Add(container);

            return container;
        }

        /// <summary>
        /// Builds a key/value info row and returns the value <see cref="Label"/>
        /// so the caller can update its text.
        /// </summary>
        private static Label BuildInfoRow(VisualElement parent, string key)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("ws-text-mid");
            keyLabel.style.fontSize       = 11;
            keyLabel.style.flexGrow       = 1;
            keyLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            var valLabel = new Label("-");
            valLabel.AddToClassList("ws-text-bright");
            valLabel.style.fontSize       = 11;
            valLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            row.Add(keyLabel);
            row.Add(valLabel);
            parent.Add(row);

            return valLabel;
        }

        // -- Inner type: ResourceMeterRow -------------------------------------

        /// <summary>
        /// One resource row: a ResourceMeter fill bar plus an absolute-value label
        /// and a per-tick rate indicator.
        /// </summary>
        private sealed class ResourceMeterRow
        {
            public readonly VisualElement Root;

            private readonly ResourceMeter _meter;
            private readonly Label         _valueLabel;
            private readonly Label         _rateLabel;

            public ResourceMeterRow(string resourceId)
            {
                Root = new VisualElement();
                Root.AddToClassList("ws-station-overview__resource-row");
                Root.style.flexDirection = FlexDirection.Column;
                Root.style.marginBottom  = 6;

                // Header row: label text + absolute amount + rate delta
                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.marginBottom  = 2;

                var idLabel = new Label(resourceId.ToUpperInvariant());
                idLabel.AddToClassList("ws-text-base");
                idLabel.style.flexGrow       = 1;
                idLabel.style.fontSize       = 11;
                idLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

                _valueLabel = new Label("-");
                _valueLabel.AddToClassList("ws-station-overview__resource-value");
                _valueLabel.AddToClassList("ws-text-bright");
                _valueLabel.style.fontSize       = 11;
                _valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                _valueLabel.style.minWidth       = 48;

                _rateLabel = new Label("");
                _rateLabel.AddToClassList("ws-station-overview__resource-rate");
                _rateLabel.style.fontSize       = 11;
                _rateLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                _rateLabel.style.minWidth       = 44;
                _rateLabel.style.marginLeft     = 4;

                header.Add(idLabel);
                header.Add(_valueLabel);
                header.Add(_rateLabel);

                var resType = IdToResourceType(resourceId);
                _meter = new ResourceMeter(resType, "");
                _meter.LabelText = "";

                Root.Add(header);
                Root.Add(_meter);
            }

            public void Update(float value, float softCap, float rate,
                               bool isWarning, bool isDepleted)
            {
                float normalised = softCap > 0f ? Mathf.Clamp01(value / softCap) : 0f;
                _meter.SetValue(normalised);

                _valueLabel.text = value.ToString("F0");

                // Rate label: zero -> blank; positive -> green "+N"; negative -> red "N"
                if (Mathf.Approximately(rate, 0f))
                {
                    _rateLabel.text        = "";
                    _rateLabel.style.color = new Color(0.34f, 0.47f, 0.63f, 1f); // text-mid
                }
                else if (rate > 0f)
                {
                    _rateLabel.text        = "+" + rate.ToString("F1");
                    _rateLabel.style.color = new Color(0.24f, 0.78f, 0.44f, 1f); // green
                }
                else
                {
                    _rateLabel.text        = rate.ToString("F1");
                    _rateLabel.style.color = new Color(0.88f, 0.22f, 0.22f, 1f); // red
                }

                // Warning / depleted tint on the row background.
                if (isDepleted)
                    Root.style.backgroundColor = new Color(0.35f, 0.06f, 0.06f, 0.4f);
                else if (isWarning)
                    Root.style.backgroundColor = new Color(0.35f, 0.24f, 0.03f, 0.4f);
                else
                    Root.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            private static ResourceMeter.ResourceType IdToResourceType(string id) =>
                id switch
                {
                    "food"    => ResourceMeter.ResourceType.Food,
                    "power"   => ResourceMeter.ResourceType.Power,
                    "oxygen"  => ResourceMeter.ResourceType.Oxygen,
                    "parts"   => ResourceMeter.ResourceType.Parts,
                    "credits" => ResourceMeter.ResourceType.Credits,
                    _         => ResourceMeter.ResourceType.Generic,
                };
        }

        // -- Inner type: RoomBonusRow -----------------------------------------

        /// <summary>
        /// One room-bonus row with cached VisualElements so it can be updated
        /// in place without recreating elements on every tick.
        /// </summary>
        private sealed class RoomBonusRow
        {
            public readonly VisualElement Root;
            private readonly Label _nameLabel;
            private readonly Label _typeLabel;

            public RoomBonusRow()
            {
                Root = new VisualElement();
                Root.AddToClassList("ws-station-overview__bonus-row");
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.marginBottom  = 4;
                Root.style.paddingTop    = 3;
                Root.style.paddingBottom = 3;

                _nameLabel = new Label();
                _nameLabel.AddToClassList("ws-station-overview__bonus-name");
                _nameLabel.AddToClassList("ws-text-bright");
                _nameLabel.style.flexGrow = 1;
                _nameLabel.style.fontSize = 11;

                _typeLabel = new Label();
                _typeLabel.AddToClassList("ws-station-overview__bonus-type");
                _typeLabel.AddToClassList("ws-text-green");
                _typeLabel.style.fontSize       = 11;
                _typeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                _typeLabel.style.minWidth       = 80;

                Root.Add(_nameLabel);
                Root.Add(_typeLabel);
            }

            public void Update(string nameText, string typeText)
            {
                _nameLabel.text = nameText;
                _typeLabel.text = typeText;
            }
        }

        // -- Inner type: DeptRow ----------------------------------------------

        /// <summary>
        /// One department summary row with cached VisualElements updated in place
        /// each tick to reflect the latest crew count and head-assignment state.
        /// </summary>
        private sealed class DeptRow
        {
            public readonly VisualElement Root;
            private readonly Label _nameLabel;
            private readonly Label _crewLabel;
            private readonly Label _headLabel;

            public DeptRow(string uid, string initialName, Action onClick)
            {
                var btn = new Button();
                btn.AddToClassList("ws-station-overview__dept-row");
                btn.style.flexDirection  = FlexDirection.Row;
                btn.style.alignItems     = Align.Center;
                btn.style.paddingTop     = 5;
                btn.style.paddingBottom  = 5;
                btn.style.paddingLeft    = 6;
                btn.style.paddingRight   = 6;
                btn.style.marginBottom   = 2;
                btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
                Root = btn;

                _nameLabel = new Label(initialName);
                _nameLabel.AddToClassList("ws-station-overview__dept-name");
                _nameLabel.AddToClassList("ws-text-bright");
                _nameLabel.style.flexGrow       = 1;
                _nameLabel.style.fontSize       = 11;
                _nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

                _crewLabel = new Label();
                _crewLabel.AddToClassList("ws-station-overview__dept-crew");
                _crewLabel.AddToClassList("ws-text-mid");
                _crewLabel.style.fontSize       = 11;
                _crewLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                _crewLabel.style.minWidth       = 56;

                _headLabel = new Label();
                _headLabel.AddToClassList("ws-station-overview__dept-head");
                _headLabel.style.fontSize       = 11;
                _headLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                _headLabel.style.minWidth       = 60;
                _headLabel.style.marginLeft     = 6;

                btn.Add(_nameLabel);
                btn.Add(_crewLabel);
                btn.Add(_headLabel);
            }

            public void Update(string name, int crewCount, bool hasHead)
            {
                _nameLabel.text  = name;
                _crewLabel.text  = crewCount + " crew";
                _headLabel.text  = hasHead ? "\u2713 HEAD" : "\u2014 HEAD";
                _headLabel.style.color = hasHead
                    ? new Color(0.24f, 0.78f, 0.44f, 1f)  // green
                    : new Color(0.60f, 0.30f, 0.30f, 1f); // muted red
            }
        }
    }
}
