using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Creator.TileEditor.Tools;

namespace Waystation.Creator.TileEditor
{
    public class TileEditorController
    {
        private readonly Dictionary<string, TileTool> _tools = new Dictionary<string, TileTool>();
        private readonly Dictionary<int, CanvasUndoManager> _undoManagers = new Dictionary<int, CanvasUndoManager>();

        public PixelCanvas2D Canvas { get; private set; }
        public TileTool ActiveTool { get; private set; }
        public Color32 ActiveColour { get; set; } = new Color32(138, 176, 208, 255); // --ws-text-base
        public Color32 BackgroundColour { get; set; } = new Color32(0, 0, 0, 0);
        public int ActiveVariantIndex { get; private set; }
        public AssetDefinition Asset { get; private set; }
        public bool IsDirty { get; set; }

        // Variant pixel data storage (for multi-variant assets like walls)
        private readonly Dictionary<int, Color32[]> _variantData = new Dictionary<int, Color32[]>();

        public event Action OnToolChanged;
        public event Action OnVariantChanged;
        public event Action OnCanvasChanged;
        public event Action OnDirtyStateChanged;

        public TileEditorController()
        {
            Canvas = new PixelCanvas2D();
        }

        public void RegisterTool(TileTool tool)
        {
            _tools[tool.Name.ToLowerInvariant()] = tool;
            tool.SetController(this);
        }

        public void SwitchTool(string toolName)
        {
            toolName = toolName.ToLowerInvariant();
            if (!_tools.TryGetValue(toolName, out var tool)) return;
            ActiveTool?.Deactivate();
            ActiveTool = tool;
            ActiveTool.Activate();
            OnToolChanged?.Invoke();
        }

        public void OpenAsset(AssetDefinition def)
        {
            Asset = def;
            ActiveVariantIndex = 0;
            _variantData.Clear();
            _undoManagers.Clear();

            int w = 64, h = 64;
            if (def.type == "furniture" && def.editor_state?.footprint != null)
            {
                w = def.editor_state.footprint.w * 64;
                h = def.editor_state.footprint.h * 64;
            }

            Canvas.Resize(w, h);
            GetOrCreateUndoManager(0);
            IsDirty = false;
        }

        public CanvasUndoManager GetActiveUndoManager()
        {
            return GetOrCreateUndoManager(ActiveVariantIndex);
        }

        public CanvasUndoManager GetOrCreateUndoManager(int variantIndex)
        {
            if (!_undoManagers.TryGetValue(variantIndex, out var mgr))
            {
                mgr = new CanvasUndoManager();
                _undoManagers[variantIndex] = mgr;
            }
            return mgr;
        }

        public void SwitchVariant(int newIndex)
        {
            if (newIndex == ActiveVariantIndex) return;

            // Save current variant's pixel data
            _variantData[ActiveVariantIndex] = Canvas.ClonePixels();

            // Load target variant's pixel data (or blank)
            ActiveVariantIndex = newIndex;
            if (_variantData.TryGetValue(newIndex, out var data))
                Canvas.LoadPixels(data);
            else
                Canvas.Clear();

            OnVariantChanged?.Invoke();
        }

        public Color32[] GetVariantPixels(int variantIndex)
        {
            if (variantIndex == ActiveVariantIndex)
                return Canvas.ClonePixels();
            return _variantData.TryGetValue(variantIndex, out var data) ? data : null;
        }

        public void SetVariantPixels(int variantIndex, Color32[] pixels)
        {
            if (variantIndex == ActiveVariantIndex)
                Canvas.LoadPixels(pixels);
            else
                _variantData[variantIndex] = pixels;
        }

        public bool HasVariantContent(int variantIndex)
        {
            Color32[] pixels;
            if (variantIndex == ActiveVariantIndex)
                pixels = Canvas.Pixels;
            else if (!_variantData.TryGetValue(variantIndex, out pixels))
                return false;

            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 0) return true;
            return false;
        }

        public void Undo()
        {
            var mgr = GetActiveUndoManager();
            if (mgr.Undo(Canvas.Pixels))
            {
                Canvas.ApplyChanges();
                MarkDirty();
                NotifyCanvasChanged();
            }
        }

        public void Redo()
        {
            var mgr = GetActiveUndoManager();
            if (mgr.Redo(Canvas.Pixels))
            {
                Canvas.ApplyChanges();
                MarkDirty();
                NotifyCanvasChanged();
            }
        }

        public void BeginStroke()
        {
            GetActiveUndoManager().BeginAction(Canvas.Pixels);
        }

        public void EndStroke()
        {
            GetActiveUndoManager().CommitAction(Canvas.Pixels);
            Canvas.ApplyChanges();
            MarkDirty();
            NotifyCanvasChanged();
        }

        public void SwapColours()
        {
            var temp = ActiveColour;
            ActiveColour = BackgroundColour;
            BackgroundColour = temp;
        }

        public void NotifyCanvasChanged()
        {
            OnCanvasChanged?.Invoke();
        }

        public void MarkDirty()
        {
            if (!IsDirty)
            {
                IsDirty = true;
                OnDirtyStateChanged?.Invoke();
            }
        }

        public void ClearDirty()
        {
            if (IsDirty)
            {
                IsDirty = false;
                OnDirtyStateChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            Canvas?.Dispose();
        }
    }
}
