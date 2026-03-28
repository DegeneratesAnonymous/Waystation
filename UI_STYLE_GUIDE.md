# Waystation UI Style Guide

Industrial terminal aesthetic for a space-station management game.
All UI is built with Unity UI Toolkit (USS / UXML).

---

## Quick Reference

When styling elements, **always use USS variables** from `WaystationVariables.uss`
rather than hardcoded hex or `rgb()` values.

```uss
@import url('Styles/WaystationVariables.uss');
@import url('Styles/WaystationComponents.uss');

.my-panel {
    background-color: var(--ws-bg-panel);   /* ✓ correct */
    /* background-color: #131720;           ✗ never hardcode */
}
```

---

## Colour Palette

### Backgrounds

| Name       | USS Variable      | Hex Value | RGB            | Usage                                   |
|------------|-------------------|-----------|----------------|-----------------------------------------|
| bg-void    | `--ws-bg-void`    | `#060709` | 6, 7, 9        | Scene background, behind all panels     |
| bg-deep    | `--ws-bg-deep`    | `#0a0c11` | 10, 12, 17     | Sub-panel insets, scroll areas          |
| bg-base    | `--ws-bg-base`    | `#0e1118` | 14, 17, 24     | Buttons, status bars, insets            |
| bg-panel   | `--ws-bg-panel`   | `#131720` | 19, 23, 32     | **Primary panel surface**               |
| bg-raised  | `--ws-bg-raised`  | `#181d28` | 24, 29, 40     | Header backgrounds, elevated zones      |
| bg-hover   | `--ws-bg-hover`   | `#1e2535` | 30, 37, 53     | Hover / focus fill                      |

### Borders

| Name        | USS Variable       | Hex Value | RGB          | Usage                                    |
|-------------|--------------------|-----------|--------------|------------------------------------------|
| border-dark | `--ws-border-dark` | `#1a1f2c` | 26, 31, 44   | Subtle dividers, grout lines             |
| border-mid  | `--ws-border-mid`  | `#242b3a` | 36, 43, 58   | Default panel border                     |
| border-lit  | `--ws-border-lit`  | `#2e3850` | 46, 56, 80   | Active, focused, selected borders        |

### Bevel

| Name     | USS Variable    | Hex Value | RGB         | Usage                       |
|----------|-----------------|-----------|-------------|-----------------------------|
| bevel-hi | `--ws-bevel-hi` | `#2a3348` | 42, 51, 72  | Top-left edge highlight     |
| bevel-lo | `--ws-bevel-lo` | `#080a0f` | 8, 10, 15   | Bottom-right edge shadow    |

### Text

| Name       | USS Variable       | Hex Value | RGB             | Usage                                   |
|------------|--------------------|-----------|-----------------|-----------------------------------------|
| text-dim   | `--ws-text-dim`    | `#2e3d52` | 46, 61, 82      | Hints, placeholders, inactive labels    |
| text-mid   | `--ws-text-mid`    | `#4a6080` | 74, 96, 128     | Secondary text, counts, arrows          |
| text-base  | `--ws-text-base`   | `#6a8aaa` | 106, 138, 170   | Default body text, button labels        |
| text-bright| `--ws-text-bright` | `#8aaac8` | 138, 170, 200   | Hover state text                        |
| text-head  | `--ws-text-head`   | `#a8c0d8` | 168, 192, 216   | Panel titles, selected items, headings  |

### Accent & Status

| Name       | USS Variable       | Hex Value | RGB             | Usage                                   |
|------------|--------------------|-----------|-----------------|-----------------------------------------|
| acc        | `--ws-acc`         | `#4880aa` | 72, 128, 170    | Primary blue — LEDs, borders, icons     |
| acc-bright | `--ws-acc-bright`  | `#60a0cc` | 96, 160, 204    | Active/selected text                    |
| amber      | `--ws-amber`       | `#c8a030` | 200, 160, 48    | Warnings, costs, badges                 |
| red        | `--ws-red`         | `#c03030` | 192, 48, 48     | Danger, close button, fault             |
| green      | `--ws-green`       | `#30a050` | 48, 160, 80     | Online, ready, confirmed                |
| cyan       | `--ws-cyan`        | `#30c8b8` | 48, 200, 184    | Oxygen, life-support indicators         |

### Category Accents

Category accents are used on stripe and icon tint elements **only**.
Do not use them on text or backgrounds.

| Category   | USS Variable            | Hex Value | RGB             |
|------------|-------------------------|-----------|-----------------|
| Structure  | `--ws-cat-structure`    | `#c8a030` | 200, 160, 48    |
| Electrical | `--ws-cat-electrical`   | `#4880aa` | 72, 128, 170    |
| Objects    | `--ws-cat-objects`      | `#30a878` | 48, 168, 120    |
| Production | `--ws-cat-production`   | `#c06828` | 192, 104, 40    |
| Plumbing   | `--ws-cat-plumbing`     | `#5088b8` | 80, 136, 184    |
| Security   | `--ws-cat-security`     | `#c03858` | 192, 56, 88     |

### Resource Colours

| Resource | USS Variable               | Colour   | Hex       |
|----------|----------------------------|----------|-----------|
| Food     | `--ws-resource-food`       | Green    | `#30a050` |
| Power    | `--ws-resource-power`      | Amber    | `#c8a030` |
| Oxygen   | `--ws-resource-oxygen`     | Cyan     | `#30c8b8` |
| Parts    | `--ws-resource-parts`      | Blue     | `#4880aa` |
| Credits  | `--ws-resource-credits`    | Blue     | `#4880aa` |

---

## Typography

### Font Stack

| Variable        | Font                      | Weight | Usage                                    |
|-----------------|---------------------------|--------|------------------------------------------|
| `--ws-font-ui`  | Barlow Condensed          | 700    | Panel titles, category names, buttons    |
| `--ws-font-mono`| Share Tech Mono           | 400    | Status text, small labels, badges, meta  |

#### Font Setup

Place font files in `Assets/UI/Fonts/` and create Unity `UnityFont` assets:

```
Assets/UI/Fonts/
  BarlowCondensed-Bold.ttf          → BarlowCondensed-Bold.asset
  BarlowCondensed-SemiBold.ttf      → BarlowCondensed-SemiBold.asset
  ShareTechMono-Regular.ttf         → ShareTechMono.asset
```

Reference in USS once assets are created:

```uss
.my-title {
    -unity-font-definition: url('../Fonts/BarlowCondensed-Bold.asset');
}
.my-mono-label {
    -unity-font-definition: url('../Fonts/ShareTechMono.asset');
}
```

### Type Scale

| USS Variable      | Size  | Font         | Letter Spacing | Usage                           |
|-------------------|-------|--------------|----------------|---------------------------------|
| `--ws-fs-title`   | 15px  | Barlow 700   | `--ws-ls-title` (2.4px)  | Panel titles             |
| `--ws-fs-cat-name`| 13px  | Barlow 600   | `--ws-ls-cat-name` (1.0px) | Category names         |
| `--ws-fs-btn`     | 9px   | Share Tech   | `--ws-ls-btn` (1.1px)    | Button labels            |
| `--ws-fs-section` | 7px   | Share Tech   | `--ws-ls-section` (2.0px)| Section headers          |
| `--ws-fs-subtitle`| 8px   | Share Tech   | `--ws-ls-subtitle` (1.5px)| Subtitles, sub-labels   |
| `--ws-fs-meta`    | 8px   | Share Tech   | `--ws-ls-meta` (0.5px)   | Counts, secondary meta   |
| `--ws-fs-badge`   | 8px   | Barlow 700   | —              | Numeric badges                  |

**Size guard:** Never use font sizes outside the range **7px – 15px**.

**All text is uppercase.** Enforce in C# via `element.text = text.ToUpper()` since
`text-transform` is not supported in USS 2022.

---

## Spacing

| USS Variable    | Value | Usage                                    |
|-----------------|-------|------------------------------------------|
| `--ws-space-xs` | 4px   | Tight internal padding, gap between pips |
| `--ws-space-sm` | 6px   | Default gap in flex rows, button padding |
| `--ws-space-md` | 8px   | Section padding, item spacing            |
| `--ws-space-lg` | 12px  | Panel header padding                     |
| `--ws-space-xl` | 16px  | Large section separation                 |

---

## Borders & Radii

- **Border radius:** `0` everywhere. No rounded corners — ever.
  USS variable: `--ws-radius: 0`.
- **Panel border:** 1px solid `--ws-border-mid`.
- **Bevel:** top-left highlight strip 1px `--ws-bevel-hi`, bottom-right shadow 1px `--ws-bevel-lo`.

---

## Animation

| USS Variable        | Duration | Usage                           |
|---------------------|----------|---------------------------------|
| `--ws-anim-fast`    | 0.10s    | Quick hover state transitions   |
| `--ws-anim-normal`  | 0.15s    | Default button/border fade      |
| `--ws-anim-slow`    | 0.22s    | Panel expand/collapse, fills    |

---

## Component Patterns

All reusable components are defined in `WaystationComponents.uss` and backed by
C# `VisualElement` subclasses in `Assets/Scripts/UI/`.

### RivetPanel

**C# class:** `Waystation.UI.RivetPanel`
**USS class:** `.ws-rivet-panel`

A panel with four 6×6 pixel corner rivet decorations. Use as the root element
for any primary panel container.

```xml
<Waystation.UI.RivetPanel>
    <!-- content -->
</Waystation.UI.RivetPanel>
```

Rivet elements are `ws-rivet ws-rivet-tl/tr/bl/br` and are injected by the C# class.
Optional bevel strips: set `show-bevel="true"` in UXML or `ShowBevel = true` in C#.

### ScanlineOverlay

**C# class:** `Waystation.UI.ScanlineOverlay`
**USS class:** `.ws-scanline-overlay`

Full-bleed overlay applied over panel backgrounds. Set a tileable scanline texture
via `BackgroundTexture` property. Has `picking-mode: ignore` so it does not block
interaction.

### CategoryStripe

**C# class:** `Waystation.UI.CategoryStripe`
**USS classes:** `.ws-category-stripe`, `.ws-category-stripe--{category}`

3px vertical left-edge stripe. Apply on list item rows:

```xml
<Waystation.UI.CategoryStripe category="Structure" />
```

For department-coloured stripes, omit the `category` attribute and register with
`WaystationTheme.RegisterDepartmentElement()` — the stripe will receive
`borderLeftColor` injections on colour change.

### StatusPip

**C# class:** `Waystation.UI.StatusPip`
**USS classes:** `.ws-status-pip`, `.ws-status-pip--{on|off|warning|fault|acc}`

6×6 LED indicator square. Available states: `On`, `Off`, `Warning`, `Fault`, `Acc`.

```xml
<Waystation.UI.StatusPip state="On" />
```

### ResourceMeter

**C# class:** `Waystation.UI.ResourceMeter`
**USS classes:** `.ws-resource-meter`, `.ws-resource-meter--{food|power|oxygen|parts|credits}`

Horizontal fill bar with header label and percentage value.

```xml
<Waystation.UI.ResourceMeter resource="Food" label="FOOD" value="0.75" />
```

From C#:
```csharp
var meter = new ResourceMeter(ResourceMeter.ResourceType.Food, "FOOD");
meter.SetValue(0.75f);   // 75% fill, green colour
```

### TabStrip

**C# class:** `Waystation.UI.TabStrip`
**USS classes:** `.ws-tab-strip`, `.ws-tab-strip--vertical`

Horizontal or vertical tab row. Tabs are added at runtime:

```csharp
var tabs = new TabStrip(TabStrip.Orientation.Horizontal);
tabs.AddTab("OVERVIEW", "overview");
tabs.AddTab("CREW", "crew");
tabs.OnTabSelected += id => ShowPanel(id);
```

### DrawerPanel

**C# class:** `Waystation.UI.DrawerPanel`
**USS classes:** `.ws-drawer-panel`, `.ws-drawer-panel--open`, `.ws-drawer-panel--horizontal`

Sliding panel animated via USS `max-height`/`max-width` transitions. Starts closed
(display none, no input). Call `Open()` / `Close()` / `Toggle()`:

```csharp
var drawer = new DrawerPanel(DrawerPanel.Direction.Vertical);
drawer.Open();   // animates open, enables input
drawer.Close();  // animates closed, disables input
```

Default open `max-height` is 600px. Override in panel USS:

```uss
.my-context .ws-drawer-panel--open { max-height: 250px; }
```

### ModalOverlay

**C# class:** `Waystation.UI.ModalOverlay`
**USS classes:** `.ws-modal-overlay`, `.ws-modal-overlay--visible`

Full-screen darkened backdrop with centred content panel. Blocks all pointer
input to underlying elements. Clicking the backdrop closes the modal (configurable).

```csharp
var modal = new ModalOverlay();
modal.Title = "CONFIRM";
modal.BodyContent.Add(new Label("Proceed?"));
modal.AddFooterButton("YES", () => { DoThing(); modal.Hide(); }, isPrimary: true);
modal.AddFooterButton("NO", modal.Hide);
root.Add(modal);
modal.Show();
```

### ExpertiseSlotPrompt

**C# class:** `Waystation.UI.ExpertiseSlotPrompt`

Specialised modal for the expertise slot unlock choice (WO-NPC-004). Extends
`ModalOverlay` with a selectable expertise option list.

```csharp
var prompt = new ExpertiseSlotPrompt();
prompt.AddExpertiseOption("combat", "COMBAT", "Melee and ranged attacks.");
prompt.AddExpertiseOption("eng",    "ENGINEERING", "Construction and repair.");
prompt.OnConfirmed += id => ApplyExpertise(npc, id);
root.Add(prompt);
prompt.Show();
```

### DataChipIndicator

**C# class:** `Waystation.UI.DataChipIndicator`
**USS classes:** `.ws-datachip-indicator`, `.ws-datachip-indicator--{filled|locked}`

18×24 pixel Datachip slot indicator. States: `Empty`, `Filled`, `Locked`.

```xml
<Waystation.UI.DataChipIndicator state="Filled" />
```

---

## Runtime Department Colours

Department colours are injected at runtime via `WaystationTheme` without USS
recompilation. Wire up to `DepartmentRegistry.OnDeptColourChanged`:

```csharp
departmentRegistry.OnDeptColourChanged += deptId =>
{
    var primary = departmentRegistry.GetDeptColour(deptId);
    var accent  = departmentRegistry.GetDeptSecondaryColour(deptId);
    if (primary.HasValue)
        WaystationTheme.SetDepartmentColour(deptId,
            primary.Value, accent ?? primary.Value);
};
```

Register department-scoped elements (e.g. category stripes on crew list items):

```csharp
WaystationTheme.RegisterDepartmentElement(crewMember.deptId, crewRow.stripe);
```

Elements implementing `IDepartmentColoured` receive a typed callback. All others
receive their `borderLeftColor` updated to the department primary colour.

---

## USS Import Order

Every panel-level USS file must import in this order:

```uss
@import url('../../UI/Styles/WaystationVariables.uss');
@import url('../../UI/Styles/WaystationComponents.uss');

/* Panel-specific overrides below — use var() only */
```

---

## Rules at a Glance

| Rule                                                          | Enforcement          |
|---------------------------------------------------------------|----------------------|
| No hardcoded hex or `rgb()` values in panel USS               | Code review          |
| No `border-radius` > 0                                        | `* { border-radius: 0; }` in reset |
| Font sizes 7px–15px only                                      | Code review          |
| All text uppercase                                            | C# `.ToUpper()`      |
| No animation beyond USS transitions                           | No custom animators  |
