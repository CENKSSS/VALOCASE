using UnityEngine;

namespace ValoCase.UI
{
    /// <summary>
    /// Shared content root between the navbars. Insets itself from the top by the
    /// TopProfileBar height and from the bottom by the BottomNavBar height so every
    /// screen parented here renders only inside the usable area.
    ///
    /// The inset uses the navbars' ACTUAL runtime RectTransform heights when they can
    /// be found (so a safe-area-padded navbar still reserves the right amount), falling
    /// back to the fixed <see cref="TopProfileBar.Height"/> / <see cref="BottomNavBar.Height"/>
    /// constants before the navbars exist.
    ///
    /// Re-applies whenever the rect changes — screen size, orientation, aspect ratio or
    /// SafeArea — so content never drifts under the navbars.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ScreenContentFitter : MonoBehaviour
    {
        /// <summary>Shared inner margin between a screen's content and the safe-area
        /// edges (the navbars). Single source of truth so screens don't hardcode
        /// their own edge gaps.</summary>
        public const float ContentPadding = 16f;

        RectTransform _rt;
        RectTransform _topBar;
        RectTransform _bottomBar;

        void Awake() => _rt = (RectTransform)transform;

        void OnEnable()
        {
            ResolveNavbars();
            Apply();
        }

        void OnRectTransformDimensionsChange() => Apply();

        void ResolveNavbars()
        {
            if (_topBar == null)
            {
                var top = Object.FindObjectOfType<TopProfileBar>(true);
                if (top != null) _topBar = (RectTransform)top.transform;
            }
            if (_bottomBar == null)
            {
                var bottom = Object.FindObjectOfType<BottomNavBar>(true);
                if (bottom != null) _bottomBar = (RectTransform)bottom.transform;
            }
        }

        void Apply()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            if (_topBar == null || _bottomBar == null) ResolveNavbars();

            float topH = _topBar != null && _topBar.rect.height > 1f
                ? _topBar.rect.height : TopProfileBar.Height;
            float bottomH = _bottomBar != null && _bottomBar.rect.height > 1f
                ? _bottomBar.rect.height : BottomNavBar.Height;

            _rt.anchorMin = Vector2.zero;
            _rt.anchorMax = Vector2.one;
            _rt.offsetMin = new Vector2(0f, bottomH);
            _rt.offsetMax = new Vector2(0f, -topH);
        }
    }
}
