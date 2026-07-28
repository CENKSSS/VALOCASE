using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Audio;

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

        static Sprite _diamondSprite;

        /// <summary>The single canonical premium-currency (diamond) sprite, cached after first load.</summary>
        static Sprite DiamondSprite()
        {
            if (_diamondSprite == null)
                _diamondSprite = Resources.Load<Sprite>("Art/UI/DiamondIcon/Diamond");
            return _diamondSprite;
        }

        /// <summary>Diamond currency icon using the real artwork. Caller positions the returned rect
        /// (anchors/pivot) exactly like any other procedural element.</summary>
        public static RectTransform MakeDiamondIcon(Transform parent, float size)
        {
            var go = new GameObject("DiamondIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite         = DiamondSprite();
            img.preserveAspect = true;
            img.raycastTarget  = false;
            return rt;
        }

        static Sprite _vpSprite;

        /// <summary>The single canonical VP (coin stack) sprite, cached after first load.</summary>
        static Sprite VpSprite()
        {
            if (_vpSprite == null)
                _vpSprite = Resources.Load<Sprite>("Art/UI/VP_Sembol/VP_Symbol");
            return _vpSprite;
        }

        /// <summary>VP currency icon using the real artwork. Caller positions the returned rect
        /// (anchors/pivot) exactly like any other procedural element.</summary>
        public static RectTransform MakeVpIcon(Transform parent, float size)
        {
            var go = new GameObject("VpIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite         = VpSprite();
            img.preserveAspect = true;
            img.raycastTarget  = false;
            return rt;
        }

        static Sprite _circleSprite;
        public static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px  = new Color32[s * s];
            float c = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c, dy = y - c;
                    float a = Mathf.Clamp01(c - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        static Sprite _backButtonSprite;
        public static Sprite BackButtonSprite()
        {
            if (_backButtonSprite == null)
                _backButtonSprite = Resources.Load<Sprite>("Art/UI/BackButton/backbutton");
            return _backButtonSprite;
        }

        // Restyles an existing back control (background image + glyph label) into the
        // real back-button icon. Click handler and position stay with the caller.
        public static void StyleCircleBack(Image bg, TextMeshProUGUI glyph, float diameter)
        {
            diameter *= 1.5f;
            if (bg != null)
            {
                bg.sprite         = BackButtonSprite();
                bg.color          = Color.white;
                bg.type           = Image.Type.Simple;
                bg.preserveAspect = true;
                bg.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                WireButtonClick(bg.GetComponent<Button>());
            }
            if (glyph != null)
                glyph.gameObject.SetActive(false);
        }

        // Shared, safe way to give a real user button a click sound. Idempotent — a
        // marker prevents a second listener if the button is wired again on rebuild.
        // Mute is handled inside SoundManager, so callers never gate on it.
        public static void WireButtonClick(Button btn)
        {
            if (btn == null || btn.GetComponent<ClickSoundMarker>() != null) return;
            btn.gameObject.AddComponent<ClickSoundMarker>();
            btn.onClick.AddListener(() => SoundManager.Instance?.PlayButtonClick());
        }

        // Recursively wires click sound on every Button under a root (e.g. a screen).
        public static void WireButtonClicks(GameObject root)
        {
            if (root == null) return;
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++) WireButtonClick(buttons[i]);
        }
    }
}
