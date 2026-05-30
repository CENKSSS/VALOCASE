using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// Shared procedural UGUI builder helpers, mirroring the patterns already used
    /// across ValoCase screens (NewGo / Stretch / MakeTmp). Centralised so the
    /// lobby flow screens stay readable and consistent.
    /// </summary>
    public static class UIBuild
    {
        public static GameObject NewGo(string name, Transform parent, params Type[] comps)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (var c in comps) go.AddComponent(c);
            return go;
        }

        public static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static TextMeshProUGUI MakeTmp(Transform parent, string name, string text,
            float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.fontStyle          = style;
            tmp.color              = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.raycastTarget      = false;
            return tmp;
        }

        /// <summary>Decorative solid image with raycastTarget disabled.</summary>
        public static Image MakeImage(string name, Transform parent, Color color, bool raycast = false)
        {
            var go  = NewGo(name, parent, typeof(Image));
            var img = go.GetComponent<Image>();
            img.color         = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Angled-cut solid panel (top-left corner cut). raycastTarget controllable.</summary>
        public static AngledCutImage MakeAngled(string name, Transform parent, Color color,
            float cutSize, bool raycast = false)
        {
            var go  = NewGo(name, parent, typeof(AngledCutImage));
            var img = go.GetComponent<AngledCutImage>();
            img.color         = color;
            img.CutSize       = cutSize;
            img.raycastTarget = raycast;
            return img;
        }

        public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;
        }

        /// <summary>Top-anchored full-width strip of fixed height.</summary>
        public static void TopStrip(RectTransform rt, float height, float yOffset = 0f)
        {
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, yOffset);
            rt.sizeDelta        = new Vector2(0f, height);
        }

        /// <summary>Bottom-anchored full-width strip of fixed height.</summary>
        public static void BottomStrip(RectTransform rt, float height, float yOffset = 0f)
        {
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, yOffset);
            rt.sizeDelta        = new Vector2(0f, height);
        }

        public static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }
    }
}
