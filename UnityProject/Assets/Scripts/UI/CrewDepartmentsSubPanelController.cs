// CrewDepartmentsSubPanelController.cs
// Crew → Departments sub-tab panel (UI-012).
//
// Displays:
//   * A scrollable list of departments.  Each row shows:
//       – Colour swatch (primary colour)
//       – Department name (double-click to rename inline)
//       – Crew count
//       – Department Lead NPC name (or "Unassigned")
//       – Colour-edit button (opens an inline HSV picker for primary / accent)
//       – Head appointment dropdown (all NPCs in the dept; "None" clears the role)
//       – Delete button (shows inline confirmation before executing)
//   * A "New Department" button that reveals an inline name input field.
//
// Data is pushed via Refresh(StationState, DepartmentRegistry, DepartmentSystem).
// Mutations (create / rename / delete / colour change / head appointment) are
// applied immediately and Refresh() is called to rebuild the list.
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (mounts inside
// WaystationHUDController which is gated by that flag).

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
    /// Crew → Departments sub-tab panel.
    /// </summary>
    public class CrewDepartmentsSubPanelController : VisualElement
    {
        // ── USS class names ────────────────────────────────────────────────────

        private const string PanelClass          = "ws-crew-dept-panel";
        private const string ToolbarClass        = "ws-crew-dept-panel__toolbar";
        private const string ListClass           = "ws-crew-dept-panel__list";
        private const string RowClass            = "ws-crew-dept-panel__row";
        private const string RowBodyClass        = "ws-crew-dept-panel__row-body";
        private const string SwatchClass         = "ws-crew-dept-panel__swatch";
        private const string NameLabelClass      = "ws-crew-dept-panel__dept-name";
        private const string MetaLabelClass      = "ws-crew-dept-panel__meta-label";
        private const string BtnClass            = "ws-crew-dept-panel__btn";
        private const string BtnDangerClass      = "ws-crew-dept-panel__btn--danger";
        private const string BtnConfirmClass     = "ws-crew-dept-panel__btn--confirm";
        private const string ConfirmRowClass     = "ws-crew-dept-panel__confirm-row";
        private const string InlineInputClass    = "ws-crew-dept-panel__inline-input";
        private const string ColourSectionClass  = "ws-crew-dept-panel__colour-section";
        private const string HsvRowClass         = "ws-crew-dept-panel__hsv-row";
        private const string ColourSwatchLgClass = "ws-crew-dept-panel__colour-swatch-lg";
        private const string EmptyClass          = "ws-crew-dept-panel__empty";
        private const string CreateRowClass      = "ws-crew-dept-panel__create-row";

        // ── State ──────────────────────────────────────────────────────────────

        private StationState       _station;
        private DepartmentRegistry _registry;
        private DepartmentSystem   _deptSystem;

        // uid of the dept whose inline delete confirmation is open
        private string _confirmDeleteUid;
        // uid of the dept whose inline rename field is open
        private string _renameUid;
        // uid of the dept whose inline colour picker is open
        private string _colourPickUid;
        // which channel is being edited: "primary" | "accent"
        private string _colourPickChannel;

        // Transient HSV picker state (reused each time picker opens)
        private float  _pickerH, _pickerS, _pickerV;
        private string _pickerHexInput = "#ffffff";

        // ── Child elements ─────────────────────────────────────────────────────

        private readonly VisualElement _toolbar;
        private readonly VisualElement _deptList;
        private readonly Label         _emptyLabel;
        private readonly VisualElement _createRow;
        private readonly TextField     _createInput;

        // ── Constructor ────────────────────────────────────────────────────────

        public CrewDepartmentsSubPanelController()
        {
            AddToClassList(PanelClass);

            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.paddingLeft   = 8;
            style.paddingRight  = 8;
            style.paddingTop    = 8;
            style.paddingBottom = 8;
            style.overflow      = Overflow.Hidden;

            var sectionLabel = new Label("DEPARTMENT DIRECTORY");
            sectionLabel.style.fontSize = 9;
            sectionLabel.style.color = new Color(0.39f, 0.75f, 1.00f, 1f);
            sectionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            sectionLabel.style.marginBottom = 3;
            Add(sectionLabel);

            // ── Toolbar: "New Department" button ───────────────────────────────
            _toolbar = new VisualElement();
            _toolbar.AddToClassList(ToolbarClass);
            _toolbar.style.flexDirection = FlexDirection.Row;
            _toolbar.style.marginBottom  = 6;
            _toolbar.style.alignItems    = Align.Center;

            var newDeptBtn = new Button(OnNewDeptClicked) { text = "+ New Department" };
            newDeptBtn.AddToClassList(BtnClass);
            _toolbar.Add(newDeptBtn);
            Add(_toolbar);

            // ── Inline create row ──────────────────────────────────────────────
            _createRow = new VisualElement();
            _createRow.AddToClassList(CreateRowClass);
            _createRow.style.flexDirection = FlexDirection.Row;
            _createRow.style.marginBottom  = 6;
            _createRow.style.display       = DisplayStyle.None;

            _createInput = new TextField { label = string.Empty };
            _createInput.AddToClassList(InlineInputClass);
            _createInput.style.flexGrow   = 1;
            _createInput.style.marginRight = 4;
            _createInput.RegisterCallback<KeyDownEvent>(OnCreateInputKeyDown);
            _createRow.Add(_createInput);

            var confirmCreateBtn = new Button(OnConfirmCreate) { text = "✓" };
            confirmCreateBtn.AddToClassList(BtnClass);
            confirmCreateBtn.AddToClassList(BtnConfirmClass);
            _createRow.Add(confirmCreateBtn);

            var cancelCreateBtn = new Button(OnCancelCreate) { text = "✕" };
            cancelCreateBtn.AddToClassList(BtnClass);
            _createRow.Add(cancelCreateBtn);
            Add(_createRow);

            // ── Department list (scrollable) ───────────────────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);

            _deptList = scroll.contentContainer;
            _deptList.AddToClassList(ListClass);
            _deptList.style.flexDirection = FlexDirection.Column;

            _emptyLabel = new Label("No departments defined.");
            _emptyLabel.AddToClassList(EmptyClass);
            _emptyLabel.style.opacity  = 0.5f;
            _emptyLabel.style.marginTop = 8;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the department list from current station state.
        /// Call once on mount and again when the station state changes.
        /// </summary>
        public void Refresh(
            StationState station,
            DepartmentRegistry registry,
            DepartmentSystem deptSystem)
        {
            _station    = station;
            _registry   = registry;
            _deptSystem = deptSystem;

            RebuildList();
        }

        // ── Create department ──────────────────────────────────────────────────

        private void OnNewDeptClicked()
        {
            _createRow.style.display = DisplayStyle.Flex;
            _createInput.value       = string.Empty;
            _createInput.Focus();
        }

        private void OnCreateInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                OnConfirmCreate();
            else if (evt.keyCode == KeyCode.Escape)
                OnCancelCreate();
        }

        private void OnConfirmCreate()
        {
            string name = _createInput.value?.Trim();
            if (!string.IsNullOrEmpty(name) && _station != null && _deptSystem != null)
                _deptSystem.CreateDepartment(name, _station);

            OnCancelCreate();
        }

        private void OnCancelCreate()
        {
            _createRow.style.display = DisplayStyle.None;
            _createInput.value       = string.Empty;
            RebuildList();
        }

        // ── List rebuild ───────────────────────────────────────────────────────

        private void RebuildList()
        {
            _deptList.Clear();

            if (_station == null || _station.departments == null ||
                _station.departments.Count == 0)
            {
                _deptList.Add(_emptyLabel);
                return;
            }

            foreach (var dept in _station.departments)
                _deptList.Add(BuildDeptRow(dept));
        }

        // ── Row builder ────────────────────────────────────────────────────────

        private VisualElement BuildDeptRow(Department dept)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection  = FlexDirection.Column;
            row.style.marginBottom   = 4;
            row.style.paddingLeft    = 2;
            row.style.paddingRight   = 2;
            row.style.paddingTop     = 4;
            row.style.paddingBottom  = 4;
            row.style.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.65f);
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.09f, 0.12f, 0.17f, 1f);

            // ── Main body ──────────────────────────────────────────────────────
            var body = new VisualElement();
            body.AddToClassList(RowBodyClass);
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems    = Align.Center;
            body.style.paddingTop    = 4;
            body.style.paddingBottom = 4;

            // Colour swatch
            var swatch = new VisualElement();
            swatch.AddToClassList(SwatchClass);
            swatch.style.width       = 14;
            swatch.style.height      = 14;
            swatch.style.minWidth    = 14;
            swatch.style.borderTopLeftRadius     = 2;
            swatch.style.borderTopRightRadius    = 2;
            swatch.style.borderBottomLeftRadius  = 2;
            swatch.style.borderBottomRightRadius = 2;
            swatch.style.marginRight = 6;

            var primaryColour = _registry?.GetDeptColour(dept.uid);
            swatch.style.backgroundColor = primaryColour.HasValue
                ? new StyleColor(primaryColour.Value)
                : new StyleColor(new Color(0.4f, 0.4f, 0.4f));
            body.Add(swatch);

            // Name label / inline rename field
            if (_renameUid == dept.uid)
            {
                body.Add(BuildInlineRenameField(dept));
            }
            else
            {
                var nameLabel = new Label(dept.name);
                nameLabel.AddToClassList(NameLabelClass);
                nameLabel.style.flexGrow   = 1;
                nameLabel.style.minWidth   = 0;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount == 2)
                    {
                        _renameUid = dept.uid;
                        RebuildList();
                    }
                });
                body.Add(nameLabel);
            }

            // Colour button
            var colourBtn = new Button(() =>
            {
                if (_colourPickUid == dept.uid)
                {
                    _colourPickUid     = null;
                    _colourPickChannel = null;
                }
                else
                {
                    OpenColourPicker(dept, "primary");
                }
                RebuildList();
            })
            { text = "🎨" };
            colourBtn.AddToClassList(BtnClass);
            colourBtn.style.marginRight = 2;
            body.Add(colourBtn);

            // Delete button / inline confirmation
            if (_confirmDeleteUid == dept.uid)
            {
                body.Add(BuildConfirmDeleteRow(dept));
            }
            else
            {
                var deleteBtn = new Button(() =>
                {
                    _confirmDeleteUid = dept.uid;
                    RebuildList();
                })
                { text = "✕" };
                deleteBtn.AddToClassList(BtnClass);
                deleteBtn.AddToClassList(BtnDangerClass);
                body.Add(deleteBtn);
            }

            row.Add(body);

            // ── Meta row (counts + lead) ───────────────────────────────────
            var metaRow = new VisualElement();
            metaRow.style.flexDirection = FlexDirection.Row;
            metaRow.style.flexWrap      = Wrap.Wrap;
            metaRow.style.alignItems    = Align.Center;
            metaRow.style.paddingLeft   = 20;
            metaRow.style.marginTop     = 2;
            metaRow.style.marginBottom  = 2;

            int crewCount = CountCrewInDept(dept.uid);
            var crewMeta = new Label($"{crewCount} crew");
            crewMeta.AddToClassList(MetaLabelClass);
            crewMeta.style.marginRight = 10;
            crewMeta.style.opacity     = 0.65f;
            metaRow.Add(crewMeta);

            string leadName = GetLeadName(dept);
            var leadMeta = new Label($"Lead: {leadName}");
            leadMeta.AddToClassList(MetaLabelClass);
            leadMeta.style.opacity = 0.75f;
            metaRow.Add(leadMeta);

            row.Add(metaRow);

            // ── Head appointment dropdown ─────────────────────────────────────
            row.Add(BuildHeadDropdownRow(dept));

            // ── Inline colour picker ───────────────────────────────────────────
            if (_colourPickUid == dept.uid)
                row.Add(BuildColourPickerSection(dept));

            return row;
        }

        // ── Inline rename ──────────────────────────────────────────────────────

        private VisualElement BuildInlineRenameField(Department dept)
        {
            var field = new TextField { value = dept.name };
            field.AddToClassList(InlineInputClass);
            field.style.flexGrow   = 1;
            field.style.marginRight = 4;
            field.Focus();

            void CommitRename()
            {
                string newName = field.value?.Trim();
                if (!string.IsNullOrEmpty(newName) && _deptSystem != null)
                    _deptSystem.RenameDepartment(dept.uid, newName, _station);
                _renameUid = null;
                RebuildList();
            }

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CommitRename();
                else if (evt.keyCode == KeyCode.Escape)
                {
                    _renameUid = null;
                    RebuildList();
                }
            });

            var confirmBtn = new Button(CommitRename) { text = "✓" };
            confirmBtn.AddToClassList(BtnClass);
            confirmBtn.AddToClassList(BtnConfirmClass);

            // Return the field wrapped in a horizontal container
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.flexGrow      = 1;
            wrap.style.alignItems    = Align.Center;
            wrap.Add(field);
            wrap.Add(confirmBtn);
            return wrap;
        }

        // ── Inline delete confirmation ─────────────────────────────────────────

        private VisualElement BuildConfirmDeleteRow(Department dept)
        {
            var confirm = new VisualElement();
            confirm.AddToClassList(ConfirmRowClass);
            confirm.style.flexDirection = FlexDirection.Row;
            confirm.style.alignItems    = Align.Center;
            confirm.style.flexGrow      = 1;

            var label = new Label($"Delete \"{dept.name}\"?");
            label.style.flexGrow   = 1;
            label.style.marginRight = 4;
            confirm.Add(label);

            var yesBtn = new Button(() =>
            {
                _deptSystem?.DeleteDepartment(dept.uid, _station);
                _confirmDeleteUid = null;
                _renameUid        = null;
                _colourPickUid    = null;
                RebuildList();
            })
            { text = "Yes" };
            yesBtn.AddToClassList(BtnClass);
            yesBtn.AddToClassList(BtnDangerClass);
            confirm.Add(yesBtn);

            var cancelBtn = new Button(() =>
            {
                _confirmDeleteUid = null;
                RebuildList();
            })
            { text = "Cancel" };
            cancelBtn.AddToClassList(BtnClass);
            confirm.Add(cancelBtn);

            return confirm;
        }

        // ── Head appointment dropdown ──────────────────────────────────────────

        private VisualElement BuildHeadDropdownRow(Department dept)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.flexWrap       = Wrap.Wrap;
            row.style.alignItems     = Align.Center;
            row.style.paddingLeft    = 20;
            row.style.marginBottom   = 2;

            var label = new Label("Lead:");
            label.AddToClassList(MetaLabelClass);
            label.style.marginRight = 6;
            label.style.minWidth    = 36;
            row.Add(label);

            // Build choices: "None" + all NPCs in this department.
            // Disambiguate duplicate NPC names by appending a short uid suffix.
            var choices      = new List<string> { "None" };
            var displayToUid = new Dictionary<string, string>();
            if (_station?.npcs != null)
            {
                // First pass: count how many NPCs share each name
                var nameCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var npc in _station.npcs.Values)
                {
                    if (npc.departmentId != dept.uid) continue;
                    nameCount[npc.name] = nameCount.TryGetValue(npc.name, out int c) ? c + 1 : 1;
                }

                // Second pass: build unique display strings
                foreach (var npc in _station.npcs.Values)
                {
                    if (npc.departmentId != dept.uid) continue;
                    string display = nameCount[npc.name] > 1
                        ? $"{npc.name} ({npc.uid.Substring(0, 4)})"
                        : npc.name;
                    choices.Add(display);
                    displayToUid[display] = npc.uid;
                }
            }

            // Determine the current lead's display string
            string currentLeadDisplay = "None";
            if (!string.IsNullOrEmpty(dept.headNpcUid) &&
                _station?.npcs != null &&
                _station.npcs.TryGetValue(dept.headNpcUid, out var headNpc))
            {
                foreach (var kv in displayToUid)
                {
                    if (kv.Value == dept.headNpcUid) { currentLeadDisplay = kv.Key; break; }
                }
            }

            var dropdown = new DropdownField(choices,
                choices.Contains(currentLeadDisplay) ? currentLeadDisplay : "None");
            dropdown.style.flexGrow  = 1;
            dropdown.style.minWidth  = 140;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (_deptSystem == null || _station == null) return;
                if (evt.newValue == "None")
                {
                    _deptSystem.RemoveHead(dept.uid, _station);
                }
                else if (displayToUid.TryGetValue(evt.newValue, out string npcUid))
                {
                    _deptSystem.AppointHead(dept.uid, npcUid, _station);
                }
                RebuildList();
            });
            row.Add(dropdown);

            return row;
        }

        // ── Inline HSV colour picker ───────────────────────────────────────────

        private void OpenColourPicker(Department dept, string channel)
        {
            _colourPickUid     = dept.uid;
            _colourPickChannel = channel;

            Color current = channel == "accent"
                ? (_registry?.GetDeptSecondaryColour(dept.uid) ?? Color.white)
                : (_registry?.GetDeptColour(dept.uid) ?? Color.white);

            Color.RGBToHSV(current, out _pickerH, out _pickerS, out _pickerV);
            _pickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(current);
        }

        private VisualElement BuildColourPickerSection(Department dept)
        {
            var section = new VisualElement();
            section.AddToClassList(ColourSectionClass);
            section.style.paddingLeft   = 20;
            section.style.paddingBottom = 6;

            // Channel toggle buttons
            var channelRow = new VisualElement();
            channelRow.style.flexDirection = FlexDirection.Row;
            channelRow.style.marginBottom  = 4;

            var primaryBtn = new Button(() =>
            {
                OpenColourPicker(dept, "primary");
                RebuildList();
            })
            { text = "Primary" };
            primaryBtn.AddToClassList(BtnClass);
            if (_colourPickChannel == "primary")
                primaryBtn.AddToClassList(BtnConfirmClass);
            primaryBtn.style.marginRight = 4;
            channelRow.Add(primaryBtn);

            var accentBtn = new Button(() =>
            {
                OpenColourPicker(dept, "accent");
                RebuildList();
            })
            { text = "Accent" };
            accentBtn.AddToClassList(BtnClass);
            if (_colourPickChannel == "accent")
                accentBtn.AddToClassList(BtnConfirmClass);
            channelRow.Add(accentBtn);

            section.Add(channelRow);

            // Preview swatch — captured by slider/hex callbacks so they can
            // update it in-place without rebuilding the entire list.
            Color previewColour = Color.HSVToRGB(_pickerH, _pickerS, _pickerV);
            var previewSwatch = new VisualElement();
            previewSwatch.AddToClassList(ColourSwatchLgClass);
            previewSwatch.style.width           = Length.Percent(100);
            previewSwatch.style.height          = 20;
            previewSwatch.style.marginBottom    = 4;
            previewSwatch.style.borderTopLeftRadius     = 2;
            previewSwatch.style.borderTopRightRadius    = 2;
            previewSwatch.style.borderBottomLeftRadius  = 2;
            previewSwatch.style.borderBottomRightRadius = 2;
            previewSwatch.style.backgroundColor = new StyleColor(previewColour);
            section.Add(previewSwatch);

            // Hex input — declared before sliders so it can be captured in slider lambdas.
            var hexRow = new VisualElement();
            hexRow.AddToClassList(HsvRowClass);
            hexRow.style.flexDirection = FlexDirection.Row;
            hexRow.style.alignItems    = Align.Center;
            hexRow.style.marginBottom  = 3;

            var hexLabel = new Label("Hex");
            hexLabel.style.minWidth    = 22;
            hexLabel.style.marginRight = 4;
            hexRow.Add(hexLabel);

            var hexField = new TextField { value = _pickerHexInput };
            hexField.style.flexGrow = 1;

            // H slider — updates swatch and hex field in-place to avoid GC churn
            // from rebuilding the full department list on every drag tick.
            section.Add(BuildHsvSliderRow("H", _pickerH, 0f, 1f, v =>
            {
                _pickerH = v;
                var newColour = Color.HSVToRGB(_pickerH, _pickerS, _pickerV);
                _pickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(newColour);
                previewSwatch.style.backgroundColor = new StyleColor(newColour);
                hexField.SetValueWithoutNotify(_pickerHexInput);
            }));

            // S slider
            section.Add(BuildHsvSliderRow("S", _pickerS, 0f, 1f, v =>
            {
                _pickerS = v;
                var newColour = Color.HSVToRGB(_pickerH, _pickerS, _pickerV);
                _pickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(newColour);
                previewSwatch.style.backgroundColor = new StyleColor(newColour);
                hexField.SetValueWithoutNotify(_pickerHexInput);
            }));

            // V slider
            section.Add(BuildHsvSliderRow("V", _pickerV, 0f, 1f, v =>
            {
                _pickerV = v;
                var newColour = Color.HSVToRGB(_pickerH, _pickerS, _pickerV);
                _pickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(newColour);
                previewSwatch.style.backgroundColor = new StyleColor(newColour);
                hexField.SetValueWithoutNotify(_pickerHexInput);
            }));

            // Hex input callback — a discrete edit, so a full rebuild is acceptable here.
            hexField.RegisterValueChangedCallback(evt =>
            {
                _pickerHexInput = evt.newValue;
                if (ColorUtility.TryParseHtmlString(evt.newValue, out Color parsed))
                {
                    Color.RGBToHSV(parsed, out _pickerH, out _pickerS, out _pickerV);
                    previewSwatch.style.backgroundColor = new StyleColor(parsed);
                    RebuildList();
                }
            });
            hexRow.Add(hexField);
            section.Add(hexRow);

            // Apply / Close buttons
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 4;

            var applyBtn = new Button(() =>
            {
                CommitColourChange(dept);
                _colourPickUid     = null;
                _colourPickChannel = null;
                RebuildList();
            })
            { text = "Apply" };
            applyBtn.AddToClassList(BtnClass);
            applyBtn.AddToClassList(BtnConfirmClass);
            applyBtn.style.marginRight = 4;
            btnRow.Add(applyBtn);

            var closeBtn = new Button(() =>
            {
                _colourPickUid     = null;
                _colourPickChannel = null;
                RebuildList();
            })
            { text = "Close" };
            closeBtn.AddToClassList(BtnClass);
            btnRow.Add(closeBtn);

            section.Add(btnRow);
            return section;
        }

        private VisualElement BuildHsvSliderRow(
            string labelText, float value, float min, float max, Action<float> onChange)
        {
            var row = new VisualElement();
            row.AddToClassList(HsvRowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 3;

            var label = new Label(labelText);
            label.style.minWidth    = 22;
            label.style.marginRight = 4;
            row.Add(label);

            var slider = new Slider(min, max) { value = value };
            slider.style.flexGrow = 1;
            slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            row.Add(slider);

            return row;
        }

        private void CommitColourChange(Department dept)
        {
            if (_registry == null) return;

            Color colour = Color.HSVToRGB(_pickerH, _pickerS, _pickerV);

            if (_colourPickChannel == "accent")
            {
                _registry.SetDeptSecondaryColour(dept.uid, colour);
            }
            else
            {
                _registry.SetDeptColour(dept.uid, colour);
            }

            // Propagate to UI elements registered under this department
            var primary = _registry.GetDeptColour(dept.uid) ?? colour;
            var accent  = _registry.GetDeptSecondaryColour(dept.uid) ?? primary;
            WaystationTheme.SetDepartmentColour(dept.uid, primary, accent);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private int CountCrewInDept(string deptUid)
        {
            if (_station?.npcs == null) return 0;
            int count = 0;
            foreach (var npc in _station.npcs.Values)
                if (npc.departmentId == deptUid)
                    count++;
            return count;
        }

        private string GetLeadName(Department dept)
        {
            if (string.IsNullOrEmpty(dept.headNpcUid)) return "Unassigned";
            if (_station?.npcs != null &&
                _station.npcs.TryGetValue(dept.headNpcUid, out var npc))
                return npc.name;
            return "Unassigned";
        }
    }
}
