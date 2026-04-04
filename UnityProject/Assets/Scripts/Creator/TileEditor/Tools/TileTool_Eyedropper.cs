namespace Waystation.Creator.TileEditor.Tools
{
    public class TileTool_Eyedropper : TileTool
    {
        public override string Name => "Eyedropper";
        public override string Shortcut => "I";
        public override string Description => "Pick colour from canvas";

        public override void OnPointerDown(PixelCoord coord)
        {
            if (coord.IsValid(Controller.Canvas.Width, Controller.Canvas.Height))
                Controller.ActiveColour = Controller.Canvas.GetPixel(coord.x, coord.y);
        }

        public override void OnPointerDrag(PixelCoord coord) { }
        public override void OnPointerUp(PixelCoord coord) { }
    }
}
