using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// A solid-color UI graphic whose top-left corner is replaced by a diagonal cut.
    /// Implemented as a MaskableGraphic that overrides OnPopulateMesh — the cleanest
    /// way to get sharp angled-corner geometry without sprites or masks.
    ///
    /// The shape is a 5-vertex pentagon:
    ///   - top edge starts at (cutSize, top) instead of (left, top)
    ///   - left edge ends at (left, top - cutSize) instead of (left, top)
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AngledCutImage : MaskableGraphic
    {
        [SerializeField] float cutSize = 10f;

        public float CutSize
        {
            get => cutSize;
            set
            {
                if (Mathf.Approximately(cutSize, value)) return;
                cutSize = Mathf.Max(0f, value);
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect  r   = GetPixelAdjustedRect();
            float left   = r.xMin;
            float right  = r.xMax;
            float bottom = r.yMin;
            float top    = r.yMax;
            float cut    = Mathf.Min(cutSize, Mathf.Min(r.width, r.height));

            Color32 c = color;

            // Pentagon vertices clockwise from the start of the top edge.
            // v0: top edge start (after cut)        v1: top-right
            // v2: bottom-right                       v3: bottom-left
            // v4: left edge top (before cut)
            vh.AddVert(new Vector3(left + cut, top),    c, Vector2.zero);
            vh.AddVert(new Vector3(right,        top),    c, Vector2.zero);
            vh.AddVert(new Vector3(right,        bottom), c, Vector2.zero);
            vh.AddVert(new Vector3(left,         bottom), c, Vector2.zero);
            vh.AddVert(new Vector3(left,         top - cut), c, Vector2.zero);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
            vh.AddTriangle(0, 3, 4);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            cutSize = Mathf.Max(0f, cutSize);
            SetVerticesDirty();
        }
#endif
    }
}
