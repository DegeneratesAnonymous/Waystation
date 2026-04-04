using System.Collections.Generic;
using Waystation.Creator.TileEditor;

namespace Waystation.Creator.TileEditor.Tools
{
    public abstract class TileTool
    {
        protected TileEditorController Controller;

        public abstract string Name { get; }
        public abstract string Shortcut { get; }
        public abstract string Description { get; }

        public void SetController(TileEditorController controller) { Controller = controller; }

        public virtual void Activate() { }
        public virtual void Deactivate() { }
        public abstract void OnPointerDown(PixelCoord coord);
        public abstract void OnPointerDrag(PixelCoord coord);
        public abstract void OnPointerUp(PixelCoord coord);
        public virtual void OnPointerHover(PixelCoord coord) { }
        public virtual List<ToolOption> GetToolOptions() => null;
    }

    public class ToolOption
    {
        public string id;
        public string label;
        public string type; // "toggle_group" | "toggle" | "slider"
        public string[] values;
        public string defaultValue;
        public string currentValue;
    }
}
