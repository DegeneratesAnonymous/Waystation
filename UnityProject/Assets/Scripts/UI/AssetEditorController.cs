// AssetEditorController — full-screen IMGUI asset editor.
//
// Accessible from:
//   • Designer menu → "New Asset" or "Open in Editor" from Template Library
//   • Dev Tools debug menu → "Asset Editor"
//
// Layout:
//   Left sidebar  (200 px) — asset tree / layer list
//   Central canvas          — preview + drawing surface
//   Right sidebar (240 px)  — properties / behaviour wiring
//   Top tab strip           — Clothing · Furniture · Tile · Animation
//
// All four mode panels share the same palette infrastructure (HSV picker,
// colour source picker) and canvas zoom/pan state.
//
// Usage:
//   Call AssetEditorController.Open() / AssetEditorController.OpenWithTemplate()
//   from elsewhere in the UI.  The editor renders on top of everything else
//   while _open is true.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.UI
{
    using Waystation.Models;
    using Waystation.Systems;

    public class AssetEditorController : MonoBehaviour
    {
        // ── Singleton / lifecycle ─────────────────────────────────────────────
        private static AssetEditorController _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            var go = new GameObject("[AssetEditor]") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AssetEditorController>();
        }

        // ── Open API ──────────────────────────────────────────────────────────

        public static void Open()                               => _instance?.OpenInternal(null, null);
        public static void OpenWithTemplate(ClothingTemplate t) => _instance?.OpenInternal(t, null);
        public static void OpenWithFurniture(FurnitureDefinition f) => _instance?.OpenInternal(null, f);
        public static bool IsOpen => _instance?._open ?? false;

        // ── Editor mode ───────────────────────────────────────────────────────

        private enum EditorMode { Clothing, Furniture, Tile, Animation }

        // ── State ─────────────────────────────────────────────────────────────
        private bool         _open;
        private EditorMode   _mode         = EditorMode.Clothing;
        private bool         _dirty;

        // Clothing mode
        private ClothingTemplate _clothingTemplate;
        private int              _selectedLayerIdx = 0;

        // Furniture mode
        private FurnitureDefinition _furnitureDef;
        private Vector2Int          _selectedCell    = Vector2Int.zero;
        private FurniturePerspective _furniturePerspective = FurniturePerspective.Horizontal;

        // Shared canvas
        private float  _zoom           = 8f;
        private Vector2 _panOffset     = Vector2.zero;
        private bool    _gridVisible   = true;

        // Undo / redo (stores JSON snapshots)
        private readonly List<string> _undoStack  = new List<string>();
        private readonly List<string> _redoStack  = new List<string>();
        private const    int          MaxUndoSteps = 50;

        // Inline HSV picker state
        private bool   _pickerOpen;
        private string _pickerSlotKey;          // "layerIdx:slotName" key
        private ColourSource _pickerSource;
        private float  _pickerH, _pickerS, _pickerV;
        private string _pickerHexInput = "";
        private Action<ColourSource> _pickerCallback;

        // Discard confirmation
        private bool _confirmDiscardOpen;

        // Canvas scroll (clothing mode layer list)
        private Vector2 _layerScroll;
        // Canvas scroll (furniture behaviour panel)
        private Vector2 _behaviourScroll;

        // Drawing tools (pixel mode)
        private enum DrawTool { Pencil, Eraser, Fill, Eyedropper }
        private DrawTool _drawTool   = DrawTool.Pencil;
        private Color    _drawColour = Color.white;

        // ── Sizing constants ──────────────────────────────────────────────────
        private const float SidebarL  = 200f;
        private const float SidebarR  = 260f;
        private const float TabH      = 34f;
        private const float Pad       = 10f;
        private const int   FontSize  = 10;
        private const int   FontSizeH = 14;

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color ColBg        = new Color(0.08f, 0.09f, 0.14f, 0.98f);
        private static readonly Color ColSide      = new Color(0.10f, 0.12f, 0.18f, 0.98f);
        private static readonly Color ColCanvas    = new Color(0.14f, 0.16f, 0.24f, 0.98f);
        private static readonly Color ColTabOn     = new Color(0.18f, 0.28f, 0.48f, 1.00f);
        private static readonly Color ColTabOff    = new Color(0.11f, 0.13f, 0.20f, 1.00f);
        private static readonly Color ColAccent    = new Color(0.35f, 0.62f, 1.00f, 1.00f);
        private static readonly Color ColDivider   = new Color(0.22f, 0.32f, 0.50f, 0.50f);
        private static readonly Color ColDanger    = new Color(0.86f, 0.26f, 0.26f, 1.00f);
        private static readonly Color ColSuccess   = new Color(0.22f, 0.76f, 0.35f, 1.00f);

        // ── Styles (lazily initialised) ───────────────────────────────────────
        private bool      _stylesReady;
        private GUIStyle  _sHeader, _sLabel, _sSub;
        private GUIStyle  _sBtn, _sBtnDanger;
        private GUIStyle  _sTabOn, _sTabOff;
        private GUIStyle  _sTextField;
        private Texture2D _white;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            // Full-screen background
            DrawSolid(new Rect(0, 0, sw, sh), ColBg);

            // Tab strip
            DrawTabStrip(sw);

            // Layout areas below tab strip
            float contentY = TabH;
            float contentH = sh - TabH;

            Rect leftArea   = new Rect(0, contentY, SidebarL, contentH);
            Rect rightArea  = new Rect(sw - SidebarR, contentY, SidebarR, contentH);
            Rect canvasArea = new Rect(SidebarL, contentY, sw - SidebarL - SidebarR, contentH);

            DrawSolid(leftArea,  ColSide);
            DrawSolid(rightArea, ColSide);
            DrawSolid(canvasArea, ColCanvas);

            // Vertical dividers
            DrawSolid(new Rect(SidebarL - 1f, contentY, 1f, contentH), ColDivider);
            DrawSolid(new Rect(sw - SidebarR, contentY, 1f, contentH), ColDivider);

            switch (_mode)
            {
                case EditorMode.Clothing:  DrawClothingMode(leftArea, canvasArea, rightArea); break;
                case EditorMode.Furniture: DrawFurnitureMode(leftArea, canvasArea, rightArea); break;
                case EditorMode.Tile:      DrawTileMode(leftArea, canvasArea, rightArea); break;
                case EditorMode.Animation: DrawAnimationMode(leftArea, canvasArea, rightArea); break;
            }

            // Inline HSV picker overlay
            if (_pickerOpen) DrawHsvPicker(sw, sh);

            // Discard confirmation
            if (_confirmDiscardOpen) DrawConfirmDiscard(sw, sh);

            // Toolbar (top-right corner: Undo / Redo / Grid / Zoom / Close)
            DrawToolbar(sw);
        }

        // ── Open / close ──────────────────────────────────────────────────────

        private void OpenInternal(ClothingTemplate template, FurnitureDefinition furniture)
        {
            _open         = true;
            _pickerOpen   = false;
            _confirmDiscardOpen = false;
            _dirty        = false;

            if (template != null)
            {
                _clothingTemplate = template;
                _mode = EditorMode.Clothing;
            }
            else if (furniture != null)
            {
                _furnitureDef = furniture;
                _mode = EditorMode.Furniture;
            }
            else
            {
                _clothingTemplate = ClothingTemplate.CreateBlank();
                _mode = EditorMode.Clothing;
            }

            _undoStack.Clear();
            _redoStack.Clear();
            PushUndo();
        }

        private void TryClose()
        {
            if (_dirty) { _confirmDiscardOpen = true; return; }
            _open = false;
        }

        // ── Tab strip ─────────────────────────────────────────────────────────

        private void DrawTabStrip(float sw)
        {
            DrawSolid(new Rect(0, 0, sw, TabH), new Color(0.07f, 0.09f, 0.15f, 1f));

            var modes = new[] {
                (EditorMode.Clothing,  "Clothing"),
                (EditorMode.Furniture, "Furniture"),
                (EditorMode.Tile,      "Tile"),
                (EditorMode.Animation, "Animation"),
            };

            float tabW = 110f;
            for (int i = 0; i < modes.Length; i++)
            {
                var (m, label) = modes[i];
                bool on = _mode == m;
                DrawSolid(new Rect(i * tabW, 0, tabW, TabH), on ? ColTabOn : ColTabOff);
                if (GUI.Button(new Rect(i * tabW, 0, tabW, TabH), label, on ? _sTabOn : _sTabOff))
                    _mode = m;
            }
        }

        // ── Top-right toolbar ─────────────────────────────────────────────────

        private void DrawToolbar(float sw)
        {
            float x = sw - 4 * 60f - 30f;
            float y = 4f;
            float bw = 55f, bh = 26f;

            GUI.color = _undoStack.Count > 1 ? ColAccent : new Color(0.4f, 0.4f, 0.4f);
            if (GUI.Button(new Rect(x, y, bw, bh), "⟲ Undo", _sBtn)) Undo();
            GUI.color = _redoStack.Count > 0 ? ColAccent : new Color(0.4f, 0.4f, 0.4f);
            if (GUI.Button(new Rect(x + 60f, y, bw, bh), "⟳ Redo", _sBtn)) Redo();
            GUI.color = _gridVisible ? ColSuccess : new Color(0.55f, 0.60f, 0.70f);
            if (GUI.Button(new Rect(x + 120f, y, bw, bh), "# Grid", _sBtn)) _gridVisible = !_gridVisible;
            GUI.color = Color.white;

            // Zoom selector
            float[] zoomLevels = { 1f, 2f, 4f, 8f, 16f };
            int zi = Array.IndexOf(zoomLevels, _zoom);
            if (GUI.Button(new Rect(x + 180f, y, bw, bh), $"{(int)_zoom}×", _sBtn))
                _zoom = zoomLevels[(zi + 1) % zoomLevels.Length];

            // Close button
            GUI.color = ColDanger;
            if (GUI.Button(new Rect(sw - 28f, y, 24f, bh), "✕", _sBtn))
                TryClose();
            GUI.color = Color.white;
        }

        // ── Clothing mode ─────────────────────────────────────────────────────

        private void DrawClothingMode(Rect left, Rect canvas, Rect right)
        {
            if (_clothingTemplate == null) _clothingTemplate = ClothingTemplate.CreateBlank();

            DrawClothingLayerList(left);
            DrawClothingCanvas(canvas);
            DrawClothingProperties(right);
        }

        private void DrawClothingLayerList(Rect area)
        {
            float y = area.y + Pad;
            float cw = area.width - Pad * 2f;
            float x = area.x + Pad;

            GUI.Label(new Rect(x, y, cw, 20f), "Layers", _sHeader);
            y += 24f;

            DrawSolid(new Rect(x, y, cw, 1f), ColDivider); y += 8f;

            // Calculate inner height for scroll
            float rowH = 56f;
            float innerH = _clothingTemplate.layers.Count * rowH + 10f;
            innerH = Mathf.Max(innerH, area.height - (y - area.y) - 10f);

            _layerScroll = GUI.BeginScrollView(
                new Rect(area.x, y, area.width, area.height - (y - area.y) - 10f),
                _layerScroll,
                new Rect(0, 0, cw, innerH));

            float sy = 0f;
            var layers = _clothingTemplate.layers;
            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                bool selected = (li == _selectedLayerIdx);

                // Row background
                Color rowBg = selected
                    ? new Color(0.16f, 0.22f, 0.38f, 1f)
                    : new Color(0.10f, 0.12f, 0.20f, 0.80f);
                DrawSolid(new Rect(0, sy, cw, rowH - 2f), rowBg);

                // Layer name + visibility toggle
                Color prevC = GUI.color;
                GUI.color = layer.enabled ? ColAccent : new Color(0.45f, 0.50f, 0.60f);
                if (GUI.Button(new Rect(2f, sy + 4f, 18f, 18f), layer.enabled ? "●" : "○", _sBtn))
                {
                    layer.enabled = !layer.enabled;
                    MarkDirty();
                }
                GUI.color = prevC;

                GUI.Label(new Rect(24f, sy + 4f, cw - 28f, 18f), layer.layerName, _sLabel);

                // Variant label (truncated)
                string varLabel = string.IsNullOrEmpty(layer.variantId) ? "(none)" : layer.variantId;
                GUI.Label(new Rect(4f, sy + 22f, cw - 8f, 14f), varLabel, _sSub);

                // Click to select
                if (GUI.Button(new Rect(0, sy, cw, rowH - 2f), "", GUIStyle.none))
                    _selectedLayerIdx = li;

                sy += rowH;
            }

            GUI.EndScrollView();
        }

        private void DrawClothingCanvas(Rect area)
        {
            // Neutral grey mannequin placeholder
            float cx = area.x + area.width * 0.5f;
            float cy = area.y + area.height * 0.5f;
            float mw = 64f * _zoom;
            float mh = 64f * _zoom;
            DrawSolid(new Rect(cx - mw * 0.5f + _panOffset.x,
                               cy - mh * 0.5f + _panOffset.y, mw, mh),
                      new Color(0.20f, 0.24f, 0.34f, 1f));

            // Grid overlay
            if (_gridVisible) DrawGridOverlay(area, 64f);

            // Hint label
            GUI.Label(new Rect(area.x + 8f, area.y + area.height - 24f, area.width - 16f, 18f),
                      "Middle-click drag: pan  •  Scroll: zoom  •  Select layer to edit",
                      _sSub);

            // Handle zoom (scroll) and pan (middle mouse) from keyboard shortcuts
            HandleCanvasInput(area);
        }

        private void DrawClothingProperties(Rect area)
        {
            if (_clothingTemplate == null) return;

            float y  = area.y + Pad;
            float cw = area.width - Pad * 2f;
            float x  = area.x + Pad;

            GUI.Label(new Rect(x, y, cw, 20f), "Properties", _sHeader);
            y += 26f;

            // Template name
            GUI.Label(new Rect(x, y, cw, 14f), "Name", _sLabel);
            y += 16f;
            string newName = GUI.TextField(new Rect(x, y, cw, 22f),
                _clothingTemplate.templateName, 64, _sTextField);
            if (newName != _clothingTemplate.templateName)
            { _clothingTemplate.templateName = newName; MarkDirty(); }
            y += 28f;

            // Designer name
            GUI.Label(new Rect(x, y, cw, 14f), "Designer", _sLabel);
            y += 16f;
            string newDesigner = GUI.TextField(new Rect(x, y, cw, 22f),
                _clothingTemplate.designerName, 64, _sTextField);
            if (newDesigner != _clothingTemplate.designerName)
            { _clothingTemplate.designerName = newDesigner; MarkDirty(); }
            y += 28f;

            DrawSolid(new Rect(x, y, cw, 1f), ColDivider); y += 10f;

            // Beauty score
            float beauty = BeautyScorer.Score(_clothingTemplate);
            GUI.Label(new Rect(x, y, cw * 0.5f, 18f), "Beauty", _sLabel);
            GUI.Label(new Rect(x + cw * 0.5f, y, cw * 0.5f, 18f),
                      $"{beauty:F1}", _sSub);
            y += 22f;

            GUI.Label(new Rect(x, y, cw * 0.5f, 18f), "Value", _sLabel);
            GUI.Label(new Rect(x + cw * 0.5f, y, cw * 0.5f, 18f),
                      $"{_clothingTemplate.inherentValue}", _sSub);
            y += 26f;

            DrawSolid(new Rect(x, y, cw, 1f), ColDivider); y += 10f;

            // Selected layer colour bindings
            if (_selectedLayerIdx >= 0 && _selectedLayerIdx < _clothingTemplate.layers.Count)
            {
                var layer = _clothingTemplate.layers[_selectedLayerIdx];
                GUI.Label(new Rect(x, y, cw, 18f), $"Layer: {layer.layerName}", _sLabel);
                y += 22f;

                // Variant field
                GUI.Label(new Rect(x, y, cw, 14f), "Variant ID", _sLabel);
                y += 16f;
                string newVariant = GUI.TextField(new Rect(x, y, cw, 22f),
                    layer.variantId, 64, _sTextField);
                if (newVariant != layer.variantId) { layer.variantId = newVariant; MarkDirty(); }
                y += 28f;

                // Palette buttons
                float hw = (cw - 4f) * 0.5f;
                GUI.color = new Color(0.35f, 0.62f, 1.00f);
                if (GUI.Button(new Rect(x, y, hw, 24f), "Random Palette", _sBtn))
                    ApplyRandomPalette(layer);
                GUI.color = new Color(0.82f, 0.55f, 1.00f);
                if (GUI.Button(new Rect(x + hw + 4f, y, hw, 24f), "Magic Palette", _sBtn))
                    ApplyMagicPalette(layer);
                GUI.color = Color.white;
                y += 30f;

                DrawSolid(new Rect(x, y, cw, 1f), ColDivider); y += 8f;

                // Colour slot bindings
                GUI.Label(new Rect(x, y, cw, 14f), "Colour Slots", _sLabel);
                y += 18f;

                if (layer.colourBindings.Count == 0)
                {
                    GUI.Label(new Rect(x, y, cw, 14f), "(no slots — set Variant ID first)", _sSub);
                    y += 18f;
                }

                foreach (var binding in layer.colourBindings)
                {
                    GUI.Label(new Rect(x, y, cw * 0.38f, 18f), binding.slotName, _sSub);

                    // Colour swatch / source button
                    Color swatchC = ResolveSwatchColour(binding.source);
                    Color prevC = GUI.color;
                    GUI.color = swatchC;
                    if (GUI.Button(new Rect(x + cw * 0.40f, y, cw * 0.58f, 18f),
                                   binding.source.DisplayLabel(), _sBtn))
                    {
                        OpenPicker(binding.source, src => { binding.source = src; MarkDirty(); });
                    }
                    GUI.color = prevC;
                    y += 22f;
                }
            }

            y += 8f;
            DrawSolid(new Rect(x, y, cw, 1f), ColDivider); y += 10f;

            // Save buttons
            float sw2 = (cw - 4f) * 0.5f;
            GUI.color = ColSuccess;
            if (GUI.Button(new Rect(x, y, sw2, 26f), "Save to Library", _sBtn))
                SaveToLibrary();
            GUI.color = ColAccent;
            if (GUI.Button(new Rect(x + sw2 + 4f, y, sw2, 26f), "Save as New", _sBtn))
                SaveAsNew();
            GUI.color = Color.white;
        }

        // ── Furniture mode ────────────────────────────────────────────────────

        private void DrawFurnitureMode(Rect left, Rect canvas, Rect right)
        {
            if (_furnitureDef == null) _furnitureDef = FurnitureDefinition.CreateBlank();

            DrawFootprintEditor(left);
            DrawFurnitureCanvas(canvas);
            DrawFurnitureBehaviourPanel(right);
        }

        private void DrawFootprintEditor(Rect area)
        {
            float y  = area.y + Pad;
            float cw = area.width - Pad * 2f;
            float x  = area.x + Pad;

            GUI.Label(new Rect(x, y, cw, 20f), "Footprint", _sHeader);
            y += 26f;

            var def = _furnitureDef;
            float cellPx = 28f;

            // Compute bounds of occupied cells
            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (var c in def.occupiedCells)
            {
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }

            // Draw grid cells
            for (int gy = minY; gy <= maxY; gy++)
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    var cell = new Vector2Int(gx, gy);
                    bool occ = def.IsOccupied(cell);
                    bool origin = (cell == def.originCell);
                    bool sel = (cell == _selectedCell);

                    float px = x + (gx - minX) * (cellPx + 2f);
                    float py = y + (gy - minY) * (cellPx + 2f);

                    Color cellCol = origin ? new Color(0.40f, 0.70f, 0.40f)
                                  : occ    ? new Color(0.30f, 0.45f, 0.70f)
                                  :          new Color(0.12f, 0.14f, 0.20f);
                    if (sel) cellCol = new Color(cellCol.r + 0.15f, cellCol.g + 0.15f, cellCol.b + 0.15f);

                    DrawSolid(new Rect(px, py, cellPx, cellPx), cellCol);

                    if (occ && GUI.Button(new Rect(px, py, cellPx, cellPx), "", GUIStyle.none))
                        _selectedCell = cell;

                    if (origin)
                    {
                        var prev = GUI.color; GUI.color = Color.white;
                        GUI.Label(new Rect(px + 2f, py + 6f, cellPx - 4f, 16f), "O", _sSub);
                        GUI.color = prev;
                    }
                }
            }

            y += (maxY - minY + 1) * (cellPx + 2f) + 10f;

            // Footprint info
            GUI.Label(new Rect(x, y, cw, 14f),
                      $"Cells: {def.occupiedCells.Count}  Origin: {def.originCell}", _sSub);
            y += 18f;

            // Expand / contract controls
            float bw = (cw - 6f) / 4f;
            if (GUI.Button(new Rect(x,               y, bw, 22f), "↑", _sBtn)) ExpandFootprint(0, -1);
            if (GUI.Button(new Rect(x + bw + 2f,     y, bw, 22f), "↓", _sBtn)) ExpandFootprint(0,  1);
            if (GUI.Button(new Rect(x + (bw + 2f)*2, y, bw, 22f), "←", _sBtn)) ExpandFootprint(-1, 0);
            if (GUI.Button(new Rect(x + (bw + 2f)*3, y, bw, 22f), "→", _sBtn)) ExpandFootprint( 1, 0);
            y += 28f;

            // Perspective toggle
            GUI.color = _furniturePerspective == FurniturePerspective.Horizontal ? ColAccent
                                                                                  : ColTabOff;
            if (GUI.Button(new Rect(x, y, cw * 0.5f - 2f, 22f), "Horizontal", _sBtn))
                _furniturePerspective = FurniturePerspective.Horizontal;
            GUI.color = _furniturePerspective == FurniturePerspective.Vertical ? ColAccent
                                                                                : ColTabOff;
            if (GUI.Button(new Rect(x + cw * 0.5f + 2f, y, cw * 0.5f - 2f, 22f), "Vertical", _sBtn))
                _furniturePerspective = FurniturePerspective.Vertical;
            GUI.color = Color.white;
            y += 28f;

            // Interaction points header
            GUI.Label(new Rect(x, y, cw, 18f), "Interaction Points", _sLabel);
            y += 22f;
            if (GUI.Button(new Rect(x, y, cw, 22f), "+ Add Point", _sBtn))
            {
                def.interactionPoints.Add(new InteractionPoint
                {
                    pointId       = Guid.NewGuid().ToString("N")[..6],
                    localPosition = Vector2.zero,
                    approachDirection = ApproachDirection.Any,
                    interactionType = "use"
                });
                MarkDirty();
            }
            y += 26f;

            foreach (var pt in def.interactionPoints)
            {
                GUI.Label(new Rect(x, y, cw - 50f, 14f),
                          $"[{pt.pointId}] {pt.interactionType} ({pt.approachDirection})", _sSub);
                y += 16f;
            }
        }

        private void DrawFurnitureCanvas(Rect area)
        {
            // Top/south face surface placeholder for selected cell
            float cx = area.x + area.width * 0.5f;
            float cy = area.y + area.height * 0.5f;
            float tw = 64f * _zoom;

            DrawSolid(new Rect(cx - tw * 0.5f + _panOffset.x,
                               cy - tw * 0.5f + _panOffset.y, tw, tw),
                      new Color(0.18f, 0.22f, 0.30f, 1f));

            if (_gridVisible) DrawGridOverlay(area, 64f);

            var def  = _furnitureDef;
            var surf = def.GetOrCreateSurface(_selectedCell, _furniturePerspective);

            string faceLabel = $"Cell {_selectedCell}  ·  {_furniturePerspective}";
            GUI.Label(new Rect(area.x + Pad, area.y + Pad, area.width - Pad * 2f, 18f), faceLabel, _sLabel);

            // Draw / Upload tabs
            float tabY = area.y + area.height - 44f;
            float tabW = 80f;
            GUI.color = ColAccent;
            GUI.Button(new Rect(area.x + Pad, tabY, tabW, 26f), "Draw", _sBtn);
            GUI.Button(new Rect(area.x + Pad + tabW + 4f, tabY, tabW, 26f), "Upload PNG", _sBtn);
            GUI.color = Color.white;

            HandleCanvasInput(area);
        }

        private void DrawFurnitureBehaviourPanel(Rect area)
        {
            float y  = area.y + Pad;
            float cw = area.width - Pad * 2f;
            float x  = area.x + Pad;

            GUI.Label(new Rect(x, y, cw, 20f), "Behaviour Wiring", _sHeader);
            y += 26f;

            if (_furnitureDef == null) return;
            var b = _furnitureDef.behaviourComponents;

            _behaviourScroll = GUI.BeginScrollView(
                new Rect(area.x, y, area.width, area.height - (y - area.y) - 10f),
                _behaviourScroll,
                new Rect(0, 0, cw, 900f));

            float sy = 0f;

            sy = BehaviourAccordion(ref b.power.enabled, "Power", cw, sy, s =>
            {
                s = FloatField("Power Draw (W)", ref b.power.powerDraw, cw, s);
                s = IntField  ("Priority",       ref b.power.powerPriority, cw, s);
                return s;
            });

            sy = BehaviourAccordion(ref b.lightEmitter.enabled, "Light Emitter", cw, sy, s =>
            {
                s = FloatField("Radius",    ref b.lightEmitter.lightRadius,    cw, s);
                s = FloatField("Intensity", ref b.lightEmitter.lightIntensity, cw, s);
                s = ColourField("Colour",   ref b.lightEmitter.lightColour,    cw, s);
                return s;
            });

            sy = BehaviourAccordion(ref b.lightRequirement.enabled, "Light Requirement", cw, sy, s =>
            {
                s = FloatField("Min Lux", ref b.lightRequirement.minLuxRequired, cw, s);
                return s;
            });

            sy = BehaviourAccordion(ref b.storage.enabled, "Storage", cw, sy, s =>
            {
                s = IntField("Capacity", ref b.storage.storageCapacity, cw, s);
                return s;
            });

            sy = BehaviourAccordion(ref b.environmentReq.enabled, "Environment", cw, sy, s =>
            {
                s = FloatField("Min Temp (°C)", ref b.environmentReq.minTemperature, cw, s);
                s = FloatField("Max Temp (°C)", ref b.environmentReq.maxTemperature, cw, s);
                s = BoolField("Requires Atm.", ref b.environmentReq.requiresAtmosphere, cw, s);
                return s;
            });

            sy = BehaviourAccordion(ref b.inputRequirement.enabled, "Input Requirement", cw, sy, s =>
            {
                s = StringField("Category",  ref b.inputRequirement.inputItemCategory,  cw, s);
                s = FloatField ("Rate / sec", ref b.inputRequirement.inputRatePerSecond, cw, s);
                return s;
            });

            sy = BehaviourAccordion(ref b.processingOutput.enabled, "Processing Output", cw, sy, s =>
            {
                s = StringField("Category",   ref b.processingOutput.outputItemCategory,  cw, s);
                s = FloatField ("Rate / sec",  ref b.processingOutput.outputRatePerSecond, cw, s);
                return s;
            });

            // TODO: stub additional behaviour components here.

            GUI.EndScrollView();
        }

        // ── Tile mode ─────────────────────────────────────────────────────────

        private void DrawTileMode(Rect left, Rect canvas, Rect right)
        {
            float y = left.y + Pad;
            float cw = left.width - Pad * 2f;
            GUI.Label(new Rect(left.x + Pad, y, cw, 20f), "Tile Editor", _sHeader);
            y += 30f;
            GUI.Label(new Rect(left.x + Pad, y, cw, 14f), "64×64 tile authoring", _sSub);

            DrawTileCanvas(canvas);

            // Right: drawing tools
            float rx = right.x + Pad;
            float ry = right.y + Pad;
            float rcw = right.width - Pad * 2f;
            GUI.Label(new Rect(rx, ry, rcw, 20f), "Draw Tools", _sHeader);
            ry += 26f;

            DrawToolButton(DrawTool.Pencil,     "✏ Pencil",    rx, ref ry, rcw);
            DrawToolButton(DrawTool.Eraser,     "◻ Eraser",    rx, ref ry, rcw);
            DrawToolButton(DrawTool.Fill,       "▣ Fill",      rx, ref ry, rcw);
            DrawToolButton(DrawTool.Eyedropper, "✦ Eyedrop",   rx, ref ry, rcw);

            ry += 10f;
            DrawSolid(new Rect(rx, ry, rcw, 1f), ColDivider); ry += 8f;

            // Draw colour swatch
            GUI.Label(new Rect(rx, ry, rcw, 14f), "Draw Colour", _sLabel); ry += 16f;
            Color prev = GUI.color; GUI.color = _drawColour;
            if (GUI.Button(new Rect(rx, ry, rcw, 28f), "  ", _sBtn))
                OpenPicker(ColourSource.Explicit(_drawColour),
                           src => { if (src.TryGetExplicit(out Color c)) _drawColour = c; });
            GUI.color = prev;
        }

        private void DrawTileCanvas(Rect area)
        {
            float cx = area.x + area.width  * 0.5f;
            float cy = area.y + area.height * 0.5f;
            float tw = 64f * _zoom;
            DrawSolid(new Rect(cx - tw * 0.5f + _panOffset.x,
                               cy - tw * 0.5f + _panOffset.y, tw, tw),
                      new Color(0.16f, 0.20f, 0.28f, 1f));
            if (_gridVisible) DrawGridOverlay(area, 64f);
            HandleCanvasInput(area);
        }

        private void DrawToolButton(DrawTool tool, string label, float x, ref float y, float w)
        {
            bool active = _drawTool == tool;
            var prev = GUI.color;
            GUI.color = active ? ColAccent : new Color(0.55f, 0.60f, 0.70f);
            if (GUI.Button(new Rect(x, y, w, 26f), label, _sBtn)) _drawTool = tool;
            GUI.color = prev;
            y += 30f;
        }

        // ── Animation mode ────────────────────────────────────────────────────

        private void DrawAnimationMode(Rect left, Rect canvas, Rect right)
        {
            float y = left.y + Pad;
            float cw = left.width - Pad * 2f;
            float x = left.x + Pad;
            GUI.Label(new Rect(x, y, cw, 20f), "Frames", _sHeader);
            y += 26f;
            GUI.Label(new Rect(x, y, cw, 14f), "(frame list goes here)", _sSub);
            y += 18f;
            if (GUI.Button(new Rect(x, y, cw, 24f), "+ Add Frame", _sBtn)) { }
            y += 28f;
            if (GUI.Button(new Rect(x, y, cw, 24f), "Import Spritesheet…", _sBtn)) { }

            // Canvas
            DrawTileCanvas(canvas);

            // Right: trigger bindings
            float rx = right.x + Pad;
            float ry = right.y + Pad;
            float rcw = right.width - Pad * 2f;
            GUI.Label(new Rect(rx, ry, rcw, 20f), "Trigger Bindings", _sHeader);
            ry += 26f;

            if (_furnitureDef != null)
            {
                foreach (var tb in _furnitureDef.animationTriggers)
                {
                    GUI.Label(new Rect(rx, ry, rcw * 0.5f, 14f), tb.condition.ToString(), _sSub);
                    GUI.Label(new Rect(rx + rcw * 0.5f, ry, rcw * 0.5f, 14f), tb.clipRef, _sSub);
                    ry += 18f;
                }
            }

            if (GUI.Button(new Rect(rx, ry, rcw, 24f), "+ Add Trigger", _sBtn))
            {
                if (_furnitureDef != null)
                {
                    _furnitureDef.animationTriggers.Add(new AnimationTriggerBinding());
                    MarkDirty();
                }
            }
        }

        // ── Inline HSV Picker ─────────────────────────────────────────────────

        private void OpenPicker(ColourSource src, Action<ColourSource> callback)
        {
            _pickerOpen   = true;
            _pickerSource = src == null ? ColourSource.MaterialDefault() : src;
            _pickerCallback = callback;

            Color c = Color.white;
            if (_pickerSource.TryGetExplicit(out Color ec)) c = ec;
            Color.RGBToHSV(c, out _pickerH, out _pickerS, out _pickerV);
            _pickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        private void DrawHsvPicker(float sw, float sh)
        {
            float pw = 320f, ph = 320f;
            float px = (sw - pw) * 0.5f, py = (sh - ph) * 0.5f;

            // Backdrop
            DrawSolid(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.55f));
            DrawSolid(new Rect(px, py, pw, ph), ColSide);
            DrawSolid(new Rect(px, py, pw, 1f), ColAccent);
            DrawSolid(new Rect(px, py + ph - 1f, pw, 1f), ColAccent);
            DrawSolid(new Rect(px, py, 1f, ph), ColAccent);
            DrawSolid(new Rect(px + pw - 1f, py, 1f, ph), ColAccent);

            float ox = px + Pad, oy = py + Pad;
            float cw = pw - Pad * 2f;

            GUI.Label(new Rect(ox, oy, cw, 20f), "Colour Source", _sHeader);
            oy += 26f;

            // Source type buttons
            float bw = (cw - 8f) / 3f;
            GUI_SourceTypeBtn("Explicit",  ColourSourceType.Explicit,        ox,              oy, bw);
            GUI_SourceTypeBtn("Dept",      ColourSourceType.DeptColour,      ox + bw + 4f,    oy, bw);
            GUI_SourceTypeBtn("Default",   ColourSourceType.MaterialDefault, ox + (bw + 4f)*2,oy, bw);
            oy += 30f;

            if (_pickerSource.type == ColourSourceType.Explicit)
            {
                // H slider
                GUI.Label(new Rect(ox, oy, 18f, 18f), "H", _sSub);
                _pickerH = GUI.HorizontalSlider(new Rect(ox + 22f, oy + 6f, cw - 26f, 12f), _pickerH, 0f, 1f);
                oy += 22f;

                // S slider
                GUI.Label(new Rect(ox, oy, 18f, 18f), "S", _sSub);
                _pickerS = GUI.HorizontalSlider(new Rect(ox + 22f, oy + 6f, cw - 26f, 12f), _pickerS, 0f, 1f);
                oy += 22f;

                // V slider
                GUI.Label(new Rect(ox, oy, 18f, 18f), "V", _sSub);
                _pickerV = GUI.HorizontalSlider(new Rect(ox + 22f, oy + 6f, cw - 26f, 12f), _pickerV, 0f, 1f);
                oy += 22f;

                // Hex input
                GUI.Label(new Rect(ox, oy, 30f, 18f), "Hex", _sSub);
                Color previewC = Color.HSVToRGB(_pickerH, _pickerS, _pickerV);
                string newHex = GUI.TextField(new Rect(ox + 34f, oy, cw - 80f, 22f),
                    _pickerHexInput, 9, _sTextField);
                if (newHex != _pickerHexInput)
                {
                    _pickerHexInput = newHex;
                    if (ColorUtility.TryParseHtmlString(newHex, out Color hc))
                    { Color.RGBToHSV(hc, out _pickerH, out _pickerS, out _pickerV); }
                }
                else
                {
                    _pickerHexInput = "#" + ColorUtility.ToHtmlStringRGB(previewC);
                }
                oy += 28f;

                // Preview swatch
                Color prev = GUI.color; GUI.color = previewC;
                DrawSolid(new Rect(ox, oy, cw, 28f), previewC);
                GUI.color = prev;
                oy += 34f;

                // Confirm / Cancel
                GUI.color = ColSuccess;
                if (GUI.Button(new Rect(ox, oy, (cw - 4f) * 0.5f, 28f), "Confirm", _sBtn))
                {
                    _pickerSource = ColourSource.Explicit(previewC);
                    _pickerCallback?.Invoke(_pickerSource);
                    _pickerOpen = false;
                }
                GUI.color = new Color(0.55f, 0.60f, 0.70f);
                if (GUI.Button(new Rect(ox + (cw - 4f) * 0.5f + 4f, oy, (cw - 4f) * 0.5f, 28f), "Cancel", _sBtn))
                    _pickerOpen = false;
                GUI.color = Color.white;
            }
            else
            {
                string desc = _pickerSource.type == ColourSourceType.DeptColour
                    ? "Colour resolved at runtime from this NPC's\ndepartment primary colour."
                    : "No tint applied — atlas master tone shows through.";
                GUI.Label(new Rect(ox, oy, cw, 50f), desc, _sSub);
                oy += 56f;

                GUI.color = ColSuccess;
                if (GUI.Button(new Rect(ox, oy, cw, 28f), "Confirm", _sBtn))
                {
                    _pickerCallback?.Invoke(_pickerSource);
                    _pickerOpen = false;
                }
                GUI.color = Color.white;
            }
        }

        private void GUI_SourceTypeBtn(string label, ColourSourceType t, float x, float y, float w)
        {
            bool on = _pickerSource.type == t;
            Color prev = GUI.color;
            GUI.color = on ? ColAccent : new Color(0.35f, 0.40f, 0.55f);
            if (GUI.Button(new Rect(x, y, w, 24f), label, _sBtn))
            {
                _pickerSource = t switch
                {
                    ColourSourceType.Explicit        => ColourSource.Explicit(
                                                            Color.HSVToRGB(_pickerH, _pickerS, _pickerV)),
                    ColourSourceType.DeptColour      => ColourSource.DeptColour(),
                    ColourSourceType.MaterialDefault => ColourSource.MaterialDefault(),
                    _                                => ColourSource.MaterialDefault(),
                };
            }
            GUI.color = prev;
        }

        // ── Discard confirmation ──────────────────────────────────────────────

        private void DrawConfirmDiscard(float sw, float sh)
        {
            float dw = 340f, dh = 140f;
            float dx = (sw - dw) * 0.5f, dy = (sh - dh) * 0.5f;
            DrawSolid(new Rect(0, 0, sw, sh), new Color(0, 0, 0, 0.45f));
            DrawSolid(new Rect(dx, dy, dw, dh), ColSide);

            GUI.Label(new Rect(dx + Pad, dy + Pad, dw - Pad * 2f, 22f), "Discard unsaved changes?", _sHeader);
            GUI.Label(new Rect(dx + Pad, dy + 38f, dw - Pad * 2f, 30f),
                      "Any unsaved edits will be lost.", _sSub);

            float bw = (dw - Pad * 3f) * 0.5f;
            GUI.color = ColDanger;
            if (GUI.Button(new Rect(dx + Pad, dy + 90f, bw, 28f), "Discard & Close", _sBtn))
            { _confirmDiscardOpen = false; _open = false; }
            GUI.color = new Color(0.55f, 0.60f, 0.70f);
            if (GUI.Button(new Rect(dx + Pad * 2f + bw, dy + 90f, bw, 28f), "Keep Editing", _sBtn))
                _confirmDiscardOpen = false;
            GUI.color = Color.white;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void DrawGridOverlay(Rect area, float tileWorldPx)
        {
            float gridStep = tileWorldPx * _zoom;
            float cx = area.x + area.width  * 0.5f + _panOffset.x;
            float cy = area.y + area.height * 0.5f + _panOffset.y;

            Color gc = new Color(1f, 1f, 1f, 0.06f);
            for (float gx = cx % gridStep; gx < area.x + area.width; gx += gridStep)
                DrawSolid(new Rect(gx, area.y, 1f, area.height), gc);
            for (float gy = cy % gridStep; gy < area.y + area.height; gy += gridStep)
                DrawSolid(new Rect(area.x, gy, area.width, 1f), gc);
        }

        private void HandleCanvasInput(Rect area)
        {
            if (!area.Contains(Event.current.mousePosition)) return;

            // Zoom via scroll wheel
            if (Event.current.type == EventType.ScrollWheel)
            {
                float[] zl = { 1f, 2f, 4f, 8f, 16f };
                int zi = Array.IndexOf(zl, _zoom);
                zi = Event.current.delta.y < 0f
                    ? Mathf.Clamp(zi + 1, 0, zl.Length - 1)
                    : Mathf.Clamp(zi - 1, 0, zl.Length - 1);
                _zoom = zl[zi];
                Event.current.Use();
            }

            // Pan via middle-mouse drag
            if (Event.current.type == EventType.MouseDrag && Event.current.button == 2)
            {
                _panOffset += Event.current.delta;
                Event.current.Use();
            }
        }

        private Color ResolveSwatchColour(ColourSource src)
        {
            if (src == null) return Color.grey;
            if (src.type == ColourSourceType.Explicit && src.TryGetExplicit(out Color c)) return c;
            if (src.type == ColourSourceType.DeptColour) return new Color(0.60f, 0.80f, 1.00f);
            return new Color(0.50f, 0.50f, 0.50f);
        }

        private void ExpandFootprint(int dx, int dy)
        {
            if (_furnitureDef == null) return;
            // Add a new cell adjacent to the furthest cell in the direction
            var cells = _furnitureDef.occupiedCells;
            Vector2Int newCell = Vector2Int.zero;
            int best = int.MinValue;
            foreach (var c in cells)
            {
                int score = c.x * dx + c.y * dy;
                if (score > best) { best = score; newCell = c + new Vector2Int(dx, dy); }
            }
            if (!cells.Contains(newCell))
            {
                cells.Add(newCell);
                MarkDirty();
            }
        }

        private void ApplyRandomPalette(ClothingLayerAppearance layer)
        {
            if (layer == null || layer.colourBindings.Count == 0) return;
            var colours = BeautyScorer.RandomHarmonicPalette(layer.colourBindings.Count);
            for (int i = 0; i < layer.colourBindings.Count; i++)
                layer.colourBindings[i].source = ColourSource.Explicit(colours[i]);
            MarkDirty();
        }

        private void ApplyMagicPalette(ClothingLayerAppearance layer)
        {
            if (layer == null) return;
            // Build a stub AtlasColourSlot list from the existing bindings
            var stubSlots = new List<AtlasColourSlot>();
            foreach (var b in layer.colourBindings)
                stubSlots.Add(new AtlasColourSlot { name = b.slotName });
            BeautyScorer.MagicPalette(layer, stubSlots);
            MarkDirty();
        }

        // ── Library actions ───────────────────────────────────────────────────

        private void SaveToLibrary()
        {
            if (_clothingTemplate == null) return;
            BeautyScorer.Score(_clothingTemplate);
            TemplateLibrary.Instance.Add(_clothingTemplate);
            _dirty = false;
        }

        private void SaveAsNew()
        {
            if (_clothingTemplate == null) return;
            var copy = _clothingTemplate.Duplicate();
            BeautyScorer.Score(copy);
            TemplateLibrary.Instance.Add(copy);
            _clothingTemplate = copy;
            _dirty = false;
        }

        // ── Undo / Redo ───────────────────────────────────────────────────────

        private void PushUndo()
        {
            string snap = _mode == EditorMode.Clothing
                ? JsonUtility.ToJson(_clothingTemplate)
                : JsonUtility.ToJson(_furnitureDef);

            _undoStack.Add(snap);
            if (_undoStack.Count > MaxUndoSteps) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count < 2) return;
            string current = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(current);
            Restore(_undoStack[^1]);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            string snap = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(snap);
            Restore(snap);
        }

        private void Restore(string json)
        {
            if (_mode == EditorMode.Clothing)
                JsonUtility.FromJsonOverwrite(json, _clothingTemplate);
            else
                JsonUtility.FromJsonOverwrite(json, _furnitureDef);
        }

        private void MarkDirty()
        {
            if (!_dirty) { _dirty = true; }
            PushUndo();
        }

        // ── Behaviour accordion helpers ───────────────────────────────────────

        private float BehaviourAccordion(ref bool enabled, string label, float cw, float sy,
                                          Func<float, float> drawBody)
        {
            Color prev = GUI.color;
            GUI.color = enabled ? ColAccent : new Color(0.45f, 0.50f, 0.62f);
            if (GUI.Button(new Rect(0f, sy, cw, 22f), $"{(enabled ? "▼" : "▶")} {label}", _sBtn))
                enabled = !enabled;
            GUI.color = prev;
            sy += 26f;

            if (enabled)
                sy = drawBody?.Invoke(sy) ?? sy;

            return sy;
        }

        private float FloatField(string label, ref float val, float cw, float sy)
        {
            GUI.Label(new Rect(0f, sy, cw * 0.52f, 18f), label, _sSub);
            string s = GUI.TextField(new Rect(cw * 0.54f, sy, cw * 0.44f, 20f),
                                     val.ToString("F2"), 10, _sTextField);
            if (float.TryParse(s, out float f)) val = f;
            return sy + 24f;
        }

        private float IntField(string label, ref int val, float cw, float sy)
        {
            GUI.Label(new Rect(0f, sy, cw * 0.52f, 18f), label, _sSub);
            string s = GUI.TextField(new Rect(cw * 0.54f, sy, cw * 0.44f, 20f),
                                     val.ToString(), 6, _sTextField);
            if (int.TryParse(s, out int i)) val = i;
            return sy + 24f;
        }

        private float StringField(string label, ref string val, float cw, float sy)
        {
            GUI.Label(new Rect(0f, sy, cw * 0.52f, 18f), label, _sSub);
            val = GUI.TextField(new Rect(cw * 0.54f, sy, cw * 0.44f, 20f), val, 64, _sTextField);
            return sy + 24f;
        }

        private float BoolField(string label, ref bool val, float cw, float sy)
        {
            GUI.Label(new Rect(0f, sy, cw * 0.70f, 18f), label, _sSub);
            val = GUI.Toggle(new Rect(cw * 0.72f, sy, cw * 0.26f, 18f), val, "");
            return sy + 22f;
        }

        private float ColourField(string label, ref Color val, float cw, float sy)
        {
            GUI.Label(new Rect(0f, sy, cw * 0.52f, 18f), label, _sSub);
            Color prev = GUI.color; GUI.color = val;
            Color captured = val;
            if (GUI.Button(new Rect(cw * 0.54f, sy, cw * 0.44f, 20f), "  ", _sBtn))
            {
                OpenPicker(ColourSource.Explicit(captured), src =>
                {
                    if (src.TryGetExplicit(out Color c)) captured = c;
                });
            }
            val = captured;
            GUI.color = prev;
            return sy + 24f;
        }

        // ── Drawing helpers ───────────────────────────────────────────────────

        private void DrawSolid(Rect r, Color c)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        // ── Style initialisation ──────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();

            _sHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize  = FontSizeH,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white },
            };
            _sLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize  = FontSize,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.85f, 0.92f, 1.00f) },
            };
            _sSub = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize,
                wordWrap = true,
                normal   = { textColor = new Color(0.62f, 0.70f, 0.84f) },
            };
            _sBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize  = FontSize,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.85f, 0.92f, 1.00f) },
            };
            _sBtnDanger = new GUIStyle(_sBtn)
            {
                normal = { textColor = new Color(1.00f, 0.55f, 0.55f) },
            };
            _sTabOn = new GUIStyle(_sBtn)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white },
            };
            _sTabOff = new GUIStyle(_sBtn)
            {
                normal = { textColor = new Color(0.65f, 0.72f, 0.88f) },
            };
            _sTextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = FontSize,
                normal   = { textColor = new Color(0.85f, 0.90f, 1.00f) },
            };
        }
    }
}
