// ShipyardSubPanelController.cs
// Fleet → Shipyard sub-tab panel (UI-021).
//
// Displays:
//   1. Blueprint list   — available ship blueprints with role, material cost, and
//                         build time.  Locked blueprints (research gate not met) are
//                         shown as non-interactive with the required research tag.
//   2. Build queue      — ships currently under construction with a progress bar
//                         and material-status indicator.
//   3. Build button     — calls ShipSystem.BeginConstruction(blueprintId).
//
// Call Refresh(StationState, ShipSystem) to sync with live data.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController which is itself gated by that flag).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Fleet → Shipyard sub-tab panel.
    /// </summary>
    public class ShipyardSubPanelController : VisualElement
    {
        // ── USS class names ───────────────────────────────────────────────────

        private const string PanelClass         = "ws-shipyard-panel";
        private const string SectionHeaderClass = "ws-shipyard-panel__section-header";
        private const string RowClass           = "ws-shipyard-panel__row";
        private const string RowSelectedClass   = "ws-shipyard-panel__row--selected";
        private const string LockedRowClass     = "ws-shipyard-panel__row--locked";
        private const string ActionBtnClass     = "ws-shipyard-panel__action-btn";
        private const string ProgressBarClass   = "ws-shipyard-panel__progress-bar";
        private const string EmptyClass         = "ws-shipyard-panel__empty";

        // ── Internal state ────────────────────────────────────────────────────

        private readonly ScrollView    _scroll;
        private readonly VisualElement _listRoot;

        private StationState _station;
        private ShipSystem   _fleet;

        // Currently selected blueprint template id.
        private string _selectedTemplateId;

        // Pending ship name for a build (populated from a text field).
        private string _pendingShipName = "";

        // ── Constructor ───────────────────────────────────────────────────────

        public ShipyardSubPanelController()
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

        // ── Public API ────────────────────────────────────────────────────────

        public void Refresh(StationState station, ShipSystem fleet)
        {
            _station = station;
            _fleet   = fleet;
            Rebuild();
        }

        // ── Private: rebuild the full UI ──────────────────────────────────────

        private void Rebuild()
        {
            _listRoot.Clear();

            if (_station == null || _fleet == null)
            {
                AddEmpty("No shipyard data available.");
                return;
            }

            // ── 1. Blueprint list ─────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("BLUEPRINTS"));
            BuildBlueprintSection();

            // ── 2. Build queue ────────────────────────────────────────────────
            _listRoot.Add(BuildSectionHeader("BUILD QUEUE"));
            BuildQueueSection();

            // ── 3. Build button ───────────────────────────────────────────────
            BuildConstructionControls();
        }

        // ── Blueprint section ─────────────────────────────────────────────────

        private void BuildBlueprintSection()
        {
            var blueprints = _fleet.GetAvailableBlueprints(_station);

            if (blueprints == null || blueprints.Count == 0)
            {
                AddEmpty("No blueprints available.");
                return;
            }

            // Auto-clear stale selection.
            bool selectionValid = false;
            foreach (var (tmpl, _) in blueprints)
                if (tmpl.id == _selectedTemplateId) { selectionValid = true; break; }
            if (!selectionValid) _selectedTemplateId = null;

            foreach (var (template, locked) in blueprints)
            {
                var tmplCopy = template;

                var row = new VisualElement();
                row.AddToClassList(RowClass);
                if (locked) row.AddToClassList(LockedRowClass);

                row.style.flexDirection = FlexDirection.Column;
                row.style.paddingTop    = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft   = 6;
                row.style.paddingRight  = 6;
                row.style.marginBottom  = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);

                bool selected = !locked && template.id == _selectedTemplateId;
                row.style.backgroundColor = selected
                    ? new Color(0.15f, 0.25f, 0.45f, 1f)
                    : locked
                        ? new Color(0.12f, 0.12f, 0.15f, 0.5f)
                        : Color.clear;

                // ── Header row: role badge + name ─────────────────────────────
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems    = Align.Center;

                var roleLabel = new Label(template.role.ToUpperInvariant());
                roleLabel.style.color      = locked
                    ? new Color(0.45f, 0.45f, 0.50f, 1f)
                    : new Color(0.50f, 0.65f, 0.90f, 1f);
                roleLabel.style.fontSize   = 10;
                roleLabel.style.marginRight = 6;

                var idLabel = new Label(template.id);
                idLabel.style.flexGrow = 1;
                idLabel.style.color    = locked
                    ? new Color(0.45f, 0.45f, 0.50f, 1f)
                    : selected ? Color.white : new Color(0.75f, 0.80f, 0.90f, 1f);
                idLabel.style.fontSize = 11;

                if (locked)
                {
                    var lockBadge = new Label("🔒");
                    lockBadge.style.fontSize = 10;
                    lockBadge.style.marginLeft = 4;
                    headerRow.Add(roleLabel);
                    headerRow.Add(idLabel);
                    headerRow.Add(lockBadge);
                }
                else
                {
                    headerRow.Add(roleLabel);
                    headerRow.Add(idLabel);
                }

                row.Add(headerRow);

                // ── Detail row: materials + build time ────────────────────────
                var detailRow = new VisualElement();
                detailRow.style.flexDirection = FlexDirection.Row;
                detailRow.style.flexWrap      = Wrap.Wrap;
                detailRow.style.paddingTop    = 2;

                // Materials
                if (template.buildMaterials != null && template.buildMaterials.Count > 0)
                {
                    foreach (var kv in template.buildMaterials)
                    {
                        float have = _station.resources.TryGetValue(kv.Key, out float r) ? r : 0f;
                        bool  enough = have >= kv.Value;
                        var matLabel = new Label($"{kv.Key}:{kv.Value}");
                        matLabel.style.color     = locked ? new Color(0.40f, 0.40f, 0.45f, 1f)
                            : enough ? new Color(0.40f, 0.75f, 0.40f, 1f)
                                     : new Color(0.80f, 0.35f, 0.25f, 1f);
                        matLabel.style.fontSize  = 10;
                        matLabel.style.marginRight = 6;
                        detailRow.Add(matLabel);
                    }
                }

                // Build time
                var timeLabel = new Label($"⏱ {template.buildTimeTicks} ticks");
                timeLabel.style.color    = locked
                    ? new Color(0.40f, 0.40f, 0.45f, 1f)
                    : new Color(0.55f, 0.60f, 0.70f, 1f);
                timeLabel.style.fontSize = 10;
                detailRow.Add(timeLabel);
                row.Add(detailRow);

                // Research requirement note.
                if (locked && !string.IsNullOrEmpty(template.researchPrereq))
                {
                    var reqLabel = new Label($"Requires: {template.researchPrereq}");
                    reqLabel.style.color    = new Color(0.70f, 0.45f, 0.15f, 1f);
                    reqLabel.style.fontSize = 10;
                    reqLabel.style.paddingTop = 2;
                    row.Add(reqLabel);
                }

                if (!locked)
                {
                    row.RegisterCallback<ClickEvent>(_ =>
                    {
                        _selectedTemplateId = (tmplCopy.id == _selectedTemplateId) ? null : tmplCopy.id;
                        Rebuild();
                    });
                }

                _listRoot.Add(row);
            }
        }

        // ── Build queue section ───────────────────────────────────────────────

        private void BuildQueueSection()
        {
            if (_station.shipConstructions == null || _station.shipConstructions.Count == 0)
            {
                AddEmpty("No ships under construction.");
                return;
            }

            foreach (var kv in _station.shipConstructions)
            {
                var c   = kv.Value;
                var row = new VisualElement();
                row.AddToClassList(RowClass);
                row.style.flexDirection  = FlexDirection.Column;
                row.style.paddingTop     = 4;
                row.style.paddingBottom  = 4;
                row.style.paddingLeft    = 6;
                row.style.paddingRight   = 6;
                row.style.marginBottom   = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.24f, 0.34f, 1f);

                // ── Name row ──────────────────────────────────────────────────
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems    = Align.Center;

                var nameLabel = new Label(c.shipName);
                nameLabel.style.flexGrow  = 1;
                nameLabel.style.color     = new Color(0.75f, 0.80f, 0.90f, 1f);
                nameLabel.style.fontSize  = 11;

                var pctLabel = new Label($"{c.progressFraction * 100f:F0}%");
                pctLabel.style.color    = new Color(0.50f, 0.65f, 0.90f, 1f);
                pctLabel.style.fontSize = 10;

                headerRow.Add(nameLabel);
                headerRow.Add(pctLabel);
                row.Add(headerRow);

                // ── Progress bar ──────────────────────────────────────────────
                var barOuter = new VisualElement();
                barOuter.AddToClassList(ProgressBarClass);
                barOuter.style.height          = 6;
                barOuter.style.marginTop       = 3;
                barOuter.style.backgroundColor = new Color(0.15f, 0.18f, 0.22f, 1f);
                barOuter.style.borderTopLeftRadius     = 3;
                barOuter.style.borderTopRightRadius    = 3;
                barOuter.style.borderBottomLeftRadius  = 3;
                barOuter.style.borderBottomRightRadius = 3;
                barOuter.style.overflow = Overflow.Hidden;

                var barInner = new VisualElement();
                barInner.style.height     = Length.Percent(100);
                barInner.style.width      = Length.Percent(c.progressFraction * 100f);
                barInner.style.backgroundColor = new Color(0.20f, 0.55f, 0.90f, 1f);
                barOuter.Add(barInner);
                row.Add(barOuter);

                // ── Material warning ──────────────────────────────────────────
                if (!c.materialsReady)
                {
                    var warnLabel = new Label("⚠ Insufficient materials at start");
                    warnLabel.style.color    = new Color(0.80f, 0.45f, 0.15f, 1f);
                    warnLabel.style.fontSize = 10;
                    warnLabel.style.paddingTop = 2;
                    row.Add(warnLabel);
                }

                _listRoot.Add(row);
            }
        }

        // ── Construction controls ─────────────────────────────────────────────

        private void BuildConstructionControls()
        {
            if (string.IsNullOrEmpty(_selectedTemplateId)) return;

            var spacer = new VisualElement();
            spacer.style.height = 8;
            _listRoot.Add(spacer);

            // Ship name text field.
            var nameField = new TextField("Ship name:");
            nameField.value = string.IsNullOrEmpty(_pendingShipName)
                ? DefaultShipName(_selectedTemplateId)
                : _pendingShipName;
            nameField.style.marginBottom = 4;
            nameField.RegisterValueChangedCallback(evt => _pendingShipName = evt.newValue);
            _listRoot.Add(nameField);

            // Validate materials.
            bool canBuild = CanBuildSelected(out string blockReason);

            if (!string.IsNullOrEmpty(blockReason))
            {
                var reasonLabel = new Label(blockReason);
                reasonLabel.style.color    = new Color(0.80f, 0.45f, 0.15f, 1f);
                reasonLabel.style.fontSize = 10;
                reasonLabel.style.paddingBottom = 4;
                _listRoot.Add(reasonLabel);
            }

            var btn = new Button();
            btn.AddToClassList(ActionBtnClass);
            btn.text = "BEGIN CONSTRUCTION";
            btn.SetEnabled(canBuild);
            btn.style.paddingTop    = 6;
            btn.style.paddingBottom = 6;
            btn.style.backgroundColor = canBuild
                ? new Color(0.15f, 0.40f, 0.65f, 1f)
                : new Color(0.25f, 0.25f, 0.30f, 1f);
            btn.style.color    = Color.white;
            btn.style.fontSize = 12;

            btn.clicked += OnBeginConstruction;
            _listRoot.Add(btn);
        }

        private bool CanBuildSelected(out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(_selectedTemplateId))
            {
                reason = "No blueprint selected.";
                return false;
            }

            if (_fleet == null)
            {
                reason = "Fleet system unavailable.";
                return false;
            }

            // Check the blueprint via GetAvailableBlueprints to avoid accessing private fields.
            var blueprints = _fleet.GetAvailableBlueprints(_station);
            foreach (var (template, locked) in blueprints)
            {
                if (template.id != _selectedTemplateId) continue;
                if (locked)
                {
                    reason = string.IsNullOrEmpty(template.researchPrereq)
                        ? "Blueprint is locked."
                        : $"Research required: {template.researchPrereq}";
                    return false;
                }
                return true;
            }

            reason = $"Blueprint '{_selectedTemplateId}' not found.";
            return false;
        }

        private void OnBeginConstruction()
        {
            if (string.IsNullOrEmpty(_selectedTemplateId)) return;

            string shipName = string.IsNullOrWhiteSpace(_pendingShipName)
                ? DefaultShipName(_selectedTemplateId)
                : _pendingShipName;

            var (ok, reason, _) = _fleet.BeginConstruction(_selectedTemplateId, shipName, _station);

            if (!ok)
            {
                Debug.LogWarning($"[ShipyardSubPanel] BeginConstruction failed: {reason}");
            }
            else
            {
                _selectedTemplateId = null;
                _pendingShipName    = "";
                Rebuild();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string DefaultShipName(string templateId)
            => templateId.Replace("ship.", "").Replace("_", " ");

        private void AddEmpty(string text)
        {
            var label = new Label(text);
            label.AddToClassList(EmptyClass);
            label.style.color     = new Color(0.45f, 0.50f, 0.60f, 1f);
            label.style.fontSize  = 10;
            label.style.paddingTop    = 4;
            label.style.paddingBottom = 4;
            label.style.paddingLeft   = 4;
            _listRoot.Add(label);
        }

        private VisualElement BuildSectionHeader(string title)
        {
            var header = new Label(title);
            header.AddToClassList(SectionHeaderClass);
            header.style.color       = new Color(0.50f, 0.65f, 0.90f, 1f);
            header.style.fontSize    = 10;
            header.style.paddingTop  = 6;
            header.style.paddingBottom = 2;
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            return header;
        }
    }
}
