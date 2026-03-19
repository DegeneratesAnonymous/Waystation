// UIRing — a lightweight custom UI Graphic that draws an orbit ring (annulus)
// using Unity's VertexHelper mesh. No external sprites or shaders needed.
//
// Usage: add this component to a RectTransform inside a Canvas. Set Color,
// Thickness, and Segments via the Inspector or from code (call SetVerticesDirty()
// after changing thickness/segments at runtime).
using UnityEngine;
using UnityEngine.UI;

namespace Waystation.View
{
    [AddComponentMenu("Waystation/UI Ring")]
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIRing : Graphic
    {
        [Tooltip("Ring width as a fraction of the outer radius (0.01–0.5).")]
        [Range(0.01f, 0.5f)]
        public float thickness = 0.04f;

        [Tooltip("Number of quad segments used to approximate the circle.")]
        [Range(16, 128)]
        public int segments = 64;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            float outerR = Mathf.Min(rectTransform.rect.width,
                                     rectTransform.rect.height) * 0.5f;
            float innerR = outerR * (1f - Mathf.Clamp(thickness, 0.01f, 0.99f));

            int segs = Mathf.Max(segments, 8);

            for (int i = 0; i < segs; i++)
            {
                float a0 = Mathf.PI * 2f *  i      / segs;
                float a1 = Mathf.PI * 2f * (i + 1) / segs;

                Vector2 outerA = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * outerR;
                Vector2 outerB = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * outerR;
                Vector2 innerA = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * innerR;
                Vector2 innerB = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * innerR;

                int vi = vh.currentVertCount;
                vh.AddVert(innerA, color, Vector2.zero);
                vh.AddVert(outerA, color, Vector2.zero);
                vh.AddVert(outerB, color, Vector2.zero);
                vh.AddVert(innerB, color, Vector2.zero);

                vh.AddTriangle(vi,     vi + 1, vi + 2);
                vh.AddTriangle(vi,     vi + 2, vi + 3);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
