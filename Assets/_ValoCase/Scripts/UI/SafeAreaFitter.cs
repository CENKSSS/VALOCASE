using UnityEngine;

namespace ValoCase.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rt;
        Rect _lastSafeArea;

        void Awake()
        {
            _rt = GetComponent<RectTransform>();
            EnsureContentFitter();
        }

        // The shared "Screens" content host must be inset between the navbars by a
        // ScreenContentFitter. Older built canvases ship without it, which lets every
        // screen render full-height under the navbars — attach it here at runtime so the
        // bound is guaranteed regardless of how the canvas prefab was authored.
        void EnsureContentFitter()
        {
            var screens = transform.Find("Screens") as RectTransform;
            if (screens != null && screens.GetComponent<ScreenContentFitter>() == null)
                screens.gameObject.AddComponent<ScreenContentFitter>();
        }

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.safeArea != _lastSafeArea) Apply();
        }

        void Apply()
        {
            _lastSafeArea = Screen.safeArea;
            var anchorMin = _lastSafeArea.position;
            var anchorMax = _lastSafeArea.position + _lastSafeArea.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;
            _rt.anchorMin = anchorMin;
            _rt.anchorMax = anchorMax;
        }
    }
}
