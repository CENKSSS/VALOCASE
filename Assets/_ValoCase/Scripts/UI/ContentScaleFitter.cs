using UnityEngine;

namespace ValoCase.UI
{
    /// <summary>
    /// Uniformly scales a fixed-size content child down so it always fits inside
    /// this rect, never up past <see cref="maxScale"/>. Attach to a rect that
    /// stretches the already-safe content area (e.g. a screen filling the shared
    /// Screens host); assign the fixed-design-size child as <see cref="content"/>.
    ///
    /// Used for screens whose layout is authored at fixed positions (no scroll) so
    /// they can never overflow behind the navbars on short or wide aspect ratios.
    /// Recalculates whenever this rect changes — screen size, aspect ratio, Canvas
    /// scale or SafeArea — so the fit holds on every device.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ContentScaleFitter : MonoBehaviour
    {
        [SerializeField] RectTransform content;
        [SerializeField] float designWidth  = 560f;
        [SerializeField] float designHeight  = 1060f;
        [SerializeField] float maxScale      = 1f;

        RectTransform _rt;

        void Awake() => _rt = (RectTransform)transform;

        void OnEnable() => Apply();

        void OnRectTransformDimensionsChange() => Apply();

        void Apply()
        {
            if (content == null) return;
            if (_rt == null) _rt = (RectTransform)transform;

            float w = _rt.rect.width;
            float h = _rt.rect.height;
            if (w < 1f || h < 1f || designWidth < 1f || designHeight < 1f) return;

            float s = Mathf.Min(maxScale, w / designWidth, h / designHeight);
            if (s <= 0f) return;
            content.localScale = new Vector3(s, s, 1f);
        }
    }
}
