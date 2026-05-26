using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Factory
{
    /// <summary>
    /// Centralized UI element factory.
    ///
    /// Replaces the duplicated CreateRect / CreateTmp / CreateMenuButton helpers
    /// found in ValoCaseUIBuilder, CaseBattleScreen, EarnVpScreen, UpgradeScreen
    /// and PlayerProfileWidget. Every screen and builder should funnel UI primitive
    /// creation through this class so styling and behaviour stay consistent.
    ///
    /// CONTRACT
    ///   • Pure construction — no game logic, no service access, no GameContext.
    ///   • Returns the most useful concrete type (RectTransform / TextMeshProUGUI / Button).
    ///   • Never sets data fields (text, sprites) beyond defaults passed in.
    /// </summary>
    public static class UIFactory
    {
        // ── RectTransform primitives ──────────────────────────────────────────
        public static RectTransform CreateRect(string name, Transform parent, Vector2 size, bool addImage = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            if (size == Vector2.zero) StretchFull(rt);
            if (addImage) go.AddComponent<Image>();
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Color color, Vector2 size = default)
        {
            var rt  = CreateRect(name, parent, size, addImage: true);
            var img = rt.GetComponent<Image>();
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string text,
            float size, TextAlignmentOptions align,
            Color? color = null, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = text;
            tmp.fontSize      = size;
            tmp.alignment     = align;
            tmp.fontStyle     = style;
            tmp.color         = color ?? new Color(0.92f, 0.96f, 1f, 1f);
            tmp.raycastTarget = false;
            var defaultFont = TryGetTmpDefaultFont();
            if (defaultFont != null) tmp.font = defaultFont;
            return tmp;
        }

        public static Button CreateButton(Transform parent, string name, string label,
            Color bgColor, Vector2 size, Color? labelColor = null, float labelSize = 22f)
        {
            var rt = CreateRect(name, parent, size, addImage: true);
            rt.GetComponent<Image>().color = bgColor;
            var btn = rt.gameObject.AddComponent<Button>();
            ApplyButtonColors(btn);

            var text = CreateText(rt, "Text", label, labelSize, TextAlignmentOptions.Center, labelColor);
            StretchFull(text.rectTransform);
            return btn;
        }

        public static Button CreateGhostButton(Transform parent, string name, string label,
            Color accent, Vector2 size, float labelSize = 14f)
        {
            var rt = CreateRect(name, parent, size, addImage: true);
            rt.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // transparent fill
            var btn = rt.gameObject.AddComponent<Button>();

            var ol = rt.gameObject.AddComponent<Outline>();
            ol.effectColor    = new Color(accent.r, accent.g, accent.b, 0.55f);
            ol.effectDistance = new Vector2(1f, -1f);

            var text = CreateText(rt, "Text", label, labelSize,
                TextAlignmentOptions.Center, accent, FontStyles.Bold);
            text.characterSpacing = 1.5f;
            StretchFull(text.rectTransform);
            return btn;
        }

        public static Outline ApplyOutline(GameObject go, Color color, float distance = 2f, float alpha = 0.55f)
        {
            var ol = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
            ol.effectColor    = new Color(color.r, color.g, color.b, alpha);
            ol.effectDistance = new Vector2(distance, -distance);
            return ol;
        }

        public static CanvasGroup AddCanvasGroup(RectTransform rt, float alpha = 1f, bool interactable = true)
        {
            var cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
            cg.alpha          = alpha;
            cg.interactable   = interactable;
            cg.blocksRaycasts = interactable;
            return cg;
        }

        // ── Anchor helpers ────────────────────────────────────────────────────
        public static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void StretchTop(RectTransform rt, float topInset, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -topInset);
            rt.sizeDelta = new Vector2(0, height);
        }

        public static void StretchBottom(RectTransform rt, float bottomInset, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot     = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, bottomInset);
            rt.sizeDelta = new Vector2(0, height);
        }

        public static void Center(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = size;
        }

        public static void SetAnchors(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot     = pivot;
        }

        // ── Layout helpers ────────────────────────────────────────────────────
        public static VerticalLayoutGroup AddVerticalLayout(Transform parent, int spacing = 0,
            TextAnchor align = TextAnchor.UpperCenter,
            bool controlW = true, bool controlH = false,
            bool forceExpandW = true, bool forceExpandH = false)
        {
            var vlg = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                  = spacing;
            vlg.childAlignment           = align;
            vlg.childControlWidth        = controlW;
            vlg.childControlHeight       = controlH;
            vlg.childForceExpandWidth    = forceExpandW;
            vlg.childForceExpandHeight   = forceExpandH;
            return vlg;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(Transform parent, int spacing = 0,
            TextAnchor align = TextAnchor.MiddleLeft,
            bool controlW = false, bool controlH = false,
            bool forceExpandW = false, bool forceExpandH = false)
        {
            var hlg = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                  = spacing;
            hlg.childAlignment           = align;
            hlg.childControlWidth        = controlW;
            hlg.childControlHeight       = controlH;
            hlg.childForceExpandWidth    = forceExpandW;
            hlg.childForceExpandHeight   = forceExpandH;
            return hlg;
        }

        public static ContentSizeFitter AddVerticalFitter(Transform parent)
        {
            var csf = parent.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            return csf;
        }

        // ── Scroll view (vertical) ────────────────────────────────────────────
        public static ScrollRect CreateVerticalScroll(string name, Transform parent,
            out RectTransform content, Color? bgColor = null)
        {
            var rootRt = CreateRect(name, parent, Vector2.zero, addImage: true);
            rootRt.GetComponent<Image>().color = bgColor ?? new Color(0, 0, 0, 0.18f);

            var sr = rootRt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal   = false;
            sr.vertical     = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateRect("Viewport", rootRt, Vector2.zero, addImage: true);
            viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            StretchFull(viewport);

            var contentRt = CreateRect("Content", viewport, Vector2.zero, addImage: false);
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot     = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;

            sr.viewport = viewport;
            sr.content  = contentRt;

            content = contentRt;
            return sr;
        }

        // ── Button visual state defaults (pinkish-cool neon) ──────────────────
        public static void ApplyButtonColors(Button btn,
            Color? normal = null, Color? highlighted = null,
            Color? pressed = null, Color? disabled = null,
            float fadeDuration = 0.12f)
        {
            var c = btn.colors;
            c.normalColor      = normal      ?? Color.white;
            c.highlightedColor = highlighted ?? new Color(1.15f, 1.15f, 1.18f, 1f);
            c.pressedColor     = pressed     ?? new Color(0.7f,  0.7f,  0.75f, 1f);
            c.selectedColor    = c.highlightedColor;
            c.disabledColor    = disabled    ?? new Color(0.5f,  0.55f, 0.6f,  0.5f);
            c.fadeDuration     = fadeDuration;
            btn.colors = c;
        }

        // ── Anchor-based rect (for screens that build with explicit anchors) ─────
        /// <summary>Creates a RectTransform with explicit anchor/pivot — no size set.</summary>
        public static RectTransform CreateRectAnchored(string name, Transform parent,
            Vector2 aMin, Vector2 aMax, Vector2 pivot, bool addImage = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            if (addImage) go.AddComponent<Image>();
            return rt;
        }

        // ── Border / glow decorators ──────────────────────────────────────────
        /// <summary>Solid border line on the top or bottom edge.</summary>
        public static void AddColoredBorder(RectTransform parent, Color color,
            bool top, float height = 1.5f)
        {
            var rt = CreateRectAnchored(
                top ? "BorderTop" : "BorderBot", parent,
                top ? new Vector2(0, 1) : Vector2.zero,
                top ? Vector2.one       : new Vector2(1, 0),
                top ? new Vector2(0.5f, 1) : new Vector2(0.5f, 0),
                addImage: true);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, height);
            var img = rt.GetComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        /// <summary>Soft glow line on the top or bottom edge.</summary>
        public static void AddGlowLine(RectTransform parent, Color color,
            bool bottom, float height = 1.5f, float alpha = 0.4f)
        {
            var c = color; c.a = alpha;
            var rt = CreateRectAnchored(
                bottom ? "GlowBot" : "GlowTop", parent,
                bottom ? Vector2.zero       : new Vector2(0, 1),
                bottom ? new Vector2(1, 0)  : Vector2.one,
                bottom ? new Vector2(0.5f, 0) : new Vector2(0.5f, 1),
                addImage: true);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, height);
            var img = rt.GetComponent<Image>();
            img.color = c; img.raycastTarget = false;
        }

        /// <summary>Thin vertical glow strip on the left or right side.</summary>
        public static void AddSideGlow(RectTransform parent, Color color,
            bool left, float width = 1.5f)
        {
            var rt = CreateRectAnchored(
                left ? "GlowLeft" : "GlowRight", parent,
                left ? Vector2.zero      : new Vector2(1, 0),
                left ? new Vector2(0, 1) : Vector2.one,
                left ? new Vector2(0, 0.5f) : new Vector2(1, 0.5f),
                addImage: true);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, 0);
            var img = rt.GetComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        // ── Internals ─────────────────────────────────────────────────────────
        static TMP_FontAsset TryGetTmpDefaultFont()
        {
            try { return TMP_Settings.defaultFontAsset; }
            catch (System.NullReferenceException) { return null; }
        }
    }
}
