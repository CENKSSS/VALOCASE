using UnityEngine;

namespace ValoCase.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rt;
        Rect _lastSafeArea;

        void Awake() => _rt = GetComponent<RectTransform>();

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
