using UnityEngine;
using UnityEngine.UI;

namespace Waystation.View
{
    /// <summary>
    /// Lightweight UGUI graphic that renders a solid right-angle triangle that
    /// fills its RectTransform. The right-angle sits at the nominated corner;
    /// the hypotenuse runs diagonally across to the opposite corner.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("Waystation/UI/Corner Triangle")]
    public class CornerTriangle : Graphic
    {
        public enum Corner { BottomLeft = 0, BottomRight = 1, TopLeft = 2, TopRight = 3 }

        public Corner corner = Corner.BottomLeft;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect    r  = rectTransform.rect;
            Vector2 bl = new Vector2(r.xMin, r.yMin);
            Vector2 br = new Vector2(r.xMax, r.yMin);
            Vector2 tl = new Vector2(r.xMin, r.yMax);
            Vector2 tr = new Vector2(r.xMax, r.yMax);

            // v0 = right-angle vertex (the named corner)
            // v1, v2 = the two adjacent corners that form the hypotenuse
            Vector2 v0, v1, v2;
            switch (corner)
            {
                case Corner.BottomLeft:  v0 = bl; v1 = br; v2 = tl; break;
                case Corner.BottomRight: v0 = br; v1 = tr; v2 = bl; break;
                case Corner.TopLeft:     v0 = tl; v1 = bl; v2 = tr; break;
                default:                 v0 = tr; v1 = tl; v2 = br; break; // TopRight
            }

            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;

            vert.position = v0; vh.AddVert(vert);
            vert.position = v1; vh.AddVert(vert);
            vert.position = v2; vh.AddVert(vert);

            vh.AddTriangle(0, 1, 2);
        }
    }
}
