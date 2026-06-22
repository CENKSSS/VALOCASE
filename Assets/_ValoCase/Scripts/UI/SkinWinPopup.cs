using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services;

namespace ValoCase.UI
{
    /// <summary>
    /// Full-screen loot-reveal popup. Shows after upgrade success or case opening.
    /// Theme (background, glow, badge, button) auto-tints based on skin rarity.
    ///
    /// TWO sources of instances:
    ///   1. Builder adds it to PF_UICanvas → Awake fires at scene load.
    ///   2. SkinWinPopup.EnsureExists() builds it from scratch at runtime
    ///      with zero prefab dependency.
    ///
    /// ALWAYS call EnsureExists() (not Instance) so the fallback kicks in.
    /// </summary>
    public sealed class SkinWinPopup : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static SkinWinPopup Instance { get; private set; }

        // ── Inspector / builder refs (also wired in runtime fallback) ─────────
        [SerializeField] CanvasGroup     canvasGroup;
        [SerializeField] Image           overlay;
        [SerializeField] RectTransform   card;
        [SerializeField] Image           rarityBg;
        [SerializeField] Image           diagonalGlow1;
        [SerializeField] Image           diagonalGlow2;
        [SerializeField] Image           topAccentLine;
        [SerializeField] TextMeshProUGUI skinNameLabel;
        [SerializeField] TextMeshProUGUI vpLabel;
        [SerializeField] Image           skinIconImage;
        [SerializeField] Image           rarityBadgeBg;
        [SerializeField] TextMeshProUGUI rarityLabel;
        [SerializeField] TextMeshProUGUI categoryLabel;
        [SerializeField] Button          confirmButton;
        [SerializeField] Image           confirmButtonBg;

        // ── Rarity fallback palette ───────────────────────────────────────────
        static readonly Dictionary<SkinRarity, Color> RarityColors = new()
        {
            { SkinRarity.Select,    new Color(0.50f, 0.62f, 0.76f, 1f) },
            { SkinRarity.Deluxe,    new Color(0.00f, 0.58f, 1.00f, 1f) },
            { SkinRarity.Premium,   new Color(0.65f, 0.13f, 0.98f, 1f) },
            { SkinRarity.Exclusive, new Color(0.86f, 0.16f, 0.26f, 1f) },
            { SkinRarity.Ultra,     new Color(1.00f, 0.63f, 0.00f, 1f) },
            { SkinRarity.Melee,     new Color(1.00f, 0.82f, 0.29f, 1f) },
        };

        Action    _onConfirm;
        Coroutine _animCo;

        // ─────────────────────────────────────────────────────────────────────
        // LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Debug.Log("[DEBUG][POPUP] Awake triggered — instance set on " + gameObject.name);

            // Confirm button may or may not be wired at this point:
            //  • Prefab path → wired by builder
            //  • Runtime fallback path → wired after RuntimeBuildPopup() finishes
            if (confirmButton != null)
                confirmButton.onClick.AddListener(OnConfirmClicked);

            gameObject.SetActive(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ─────────────────────────────────────────────────────────────────────

        public static SkinWinPopup EnsureExists()
        {
            if (Instance != null)
            {
                Debug.Log("[DEBUG][POPUP] EnsureExists — Instance present: " + Instance.gameObject.name);
                return Instance;
            }
            Debug.LogWarning("[DEBUG][POPUP] EnsureExists — Instance null, building runtime fallback");
            return RuntimeBuildPopup();
        }

        public void Show(SkinDefinitionSO skin, Action onConfirm = null)
        {
            Debug.Log("[DEBUG][POPUP] Show triggered — skin=" + (skin != null ? skin.SkinName : "NULL"));

            _onConfirm = onConfirm;
            EnsureOutsideClose();
            ApplyRarityTheme(skin);
            PopulateContent(skin);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();  // ensure rendered on top
            SetCG(1f, true);

            Debug.Log("[DEBUG][POPUP] active=" + gameObject.activeSelf
                    + "  alpha=" + (canvasGroup != null ? canvasGroup.alpha.ToString("F2") : "NULL"));

            if (_animCo != null) StopCoroutine(_animCo);
            _animCo = StartCoroutine(PopInAnimation());
        }

        public void Hide()
        {
            if (_animCo != null) { StopCoroutine(_animCo); _animCo = null; }
            SetCG(0f, false);
            gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // RUNTIME BUILDER — builds the entire UI hierarchy from code.
        // Uses safe `new GameObject(name, typeof(RectTransform), typeof(Image))`
        // construction so RectTransform is created with the GameObject (never null).
        // ─────────────────────────────────────────────────────────────────────

        static SkinWinPopup RuntimeBuildPopup()
        {
            // Locate parent: prefer SafeArea, fallback to highest sortingOrder canvas
            Transform parent = null;
            var safeArea = GameObject.Find("SafeArea");
            if (safeArea != null)
            {
                parent = safeArea.transform;
                Debug.Log("[DEBUG][POPUP] RuntimeBuildPopup attaching to SafeArea (root=" + safeArea.transform.root.name + ")");
            }
            else
            {
                Canvas best = null;
                int bestOrder = int.MinValue;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                    if (c.isRootCanvas && c.sortingOrder > bestOrder) { bestOrder = c.sortingOrder; best = c; }
                if (best != null)
                {
                    parent = best.transform;
                    Debug.Log("[DEBUG][POPUP] RuntimeBuildPopup attaching to Canvas " + best.name);
                }
            }
            if (parent == null)
            {
                Debug.LogError("[DEBUG][POPUP] NO Canvas in scene — cannot create popup");
                return null;
            }

            // ── Root overlay (full screen, dark, blocks raycasts) ─────────────
            var rootGo = new GameObject("SkinWinPopup",
                typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.SetAsLastSibling();
            var rootRt = (RectTransform)rootGo.transform;
            Stretch(rootRt);
            var rootImg = rootGo.GetComponent<Image>();
            rootImg.color = new Color(0f, 0f, 0f, 0.88f);
            rootImg.raycastTarget = true;
            var cg = rootGo.GetComponent<CanvasGroup>();

            // ── Card (600 × 540, centered) ────────────────────────────────────
            var cardGo = new GameObject("Card",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            cardGo.transform.SetParent(rootGo.transform, false);
            var cardRt = (RectTransform)cardGo.transform;
            Center(cardRt, Vector2.zero, new Vector2(520f, 500f));
            var cardImg = cardGo.GetComponent<Image>();
            cardImg.color = new Color(0.051f, 0.063f, 0.125f, 1f); // #0D1020
            cardImg.raycastTarget = false;
            // Mobile: red neon border around the win card.
            var cardOutline = cardGo.AddComponent<Outline>();
            cardOutline.effectColor    = new Color(1f, 0.275f, 0.333f, 0.9f); // #FF4655
            cardOutline.effectDistance = new Vector2(2f, -2f);

            // ── Rarity wash (full card) ───────────────────────────────────────
            var rarityBgImg = MakeImage("RarityBg", cardRt, stretch: true);
            rarityBgImg.color = new Color(0.86f, 0.16f, 0.26f, 0.40f);
            rarityBgImg.raycastTarget = false;

            // ── Diagonal light beams ──────────────────────────────────────────
            var g1Img = MakeImage("DiagGlow1", cardRt);
            Center((RectTransform)g1Img.transform, new Vector2(-70f, 30f), new Vector2(220f, 900f));
            g1Img.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            g1Img.color = new Color(1f, 1f, 1f, 0.14f);
            g1Img.raycastTarget = false;

            var g2Img = MakeImage("DiagGlow2", cardRt);
            Center((RectTransform)g2Img.transform, new Vector2(80f, 30f), new Vector2(110f, 900f));
            g2Img.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            g2Img.color = new Color(1f, 1f, 1f, 0.07f);
            g2Img.raycastTarget = false;

            // ── Top accent strip ──────────────────────────────────────────────
            var accentImg = MakeImage("TopAccent", cardRt);
            var accentRt = (RectTransform)accentImg.transform;
            accentRt.anchorMin = new Vector2(0f, 1f); accentRt.anchorMax = new Vector2(1f, 1f);
            accentRt.pivot = new Vector2(0.5f, 1f);
            accentRt.anchoredPosition = Vector2.zero; accentRt.sizeDelta = new Vector2(0f, 5f);
            accentImg.color = new Color(1f, 0.275f, 0.333f, 1f);
            accentImg.raycastTarget = false;

            // ── Skin name (large, bold, white) ────────────────────────────────
            var nameTmp = MakeText("SkinName", cardRt, "Skin Adi", 28f, FontStyles.Bold,
                                   new Color(0.925f, 0.910f, 0.882f, 1f));
            Center((RectTransform)nameTmp.transform, new Vector2(0f, 196f), new Vector2(540f, 56f));
            nameTmp.enableWordWrapping = true;

            // ── VP label (bright green) ───────────────────────────────────────
            var vpTmp = MakeText("VpLabel", cardRt, "1,775 VP", 22f, FontStyles.Bold,
                                 new Color(0.30f, 1.00f, 0.45f, 1f));
            Center((RectTransform)vpTmp.transform, new Vector2(0f, 148f), new Vector2(400f, 38f));

            // ── Skin icon ─────────────────────────────────────────────────────
            var iconImg = MakeImage("SkinIcon", cardRt);
            Center((RectTransform)iconImg.transform, new Vector2(0f, 22f), new Vector2(340f, 220f));
            iconImg.color = Color.white;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            // ── Rarity badge pill ─────────────────────────────────────────────
            var badgeBgImg = MakeImage("RarityBadge", cardRt);
            Center((RectTransform)badgeBgImg.transform, new Vector2(0f, -112f), new Vector2(170f, 34f));
            badgeBgImg.color = new Color(0.86f, 0.16f, 0.26f, 0.75f);
            badgeBgImg.raycastTarget = false;

            var rarityTmp = MakeText("RarityLabel", (RectTransform)badgeBgImg.transform,
                                     "EXCLUSIVE", 15f, FontStyles.Bold, Color.white);
            Stretch((RectTransform)rarityTmp.transform);

            // ── Category label ────────────────────────────────────────────────
            var catTmp = MakeText("CategoryLabel", cardRt, "VANDAL", 15f, FontStyles.Normal,
                                  new Color(0.75f, 0.80f, 0.85f, 1f));
            Center((RectTransform)catTmp.transform, new Vector2(0f, -154f), new Vector2(400f, 28f));

            // ── Confirm button ────────────────────────────────────────────────
            var btnGo = new GameObject("ConfirmButton",
                typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(cardRt, false);
            var btnRt = (RectTransform)btnGo.transform;
            Center(btnRt, new Vector2(0f, -215f), new Vector2(380f, 70f));
            var btnBgImg = btnGo.GetComponent<Image>();
            btnBgImg.color = new Color(0.50f, 0.09f, 0.16f, 1f);
            btnBgImg.raycastTarget = true;
            var confirmBtn = btnGo.GetComponent<Button>();
            var colors = confirmBtn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.pressedColor     = new Color(0.60f, 0.60f, 0.60f);
            confirmBtn.colors = colors;

            var btnTmp = MakeText("Label", btnRt, "TAMAM", 20f, FontStyles.Bold,
                                  new Color(0.925f, 0.910f, 0.882f, 1f));
            var btnLblRt = (RectTransform)btnTmp.transform;
            Stretch(btnLblRt);
            btnLblRt.offsetMin = new Vector2(8f, 4f);
            btnLblRt.offsetMax = new Vector2(-8f, -4f);

            // ── Add SkinWinPopup component LAST so Awake fires after we're ready ──
            // (We still wire fields below; Awake only sets Instance + hides root.)
            var comp = rootGo.AddComponent<SkinWinPopup>();

            // ── Wire private fields (same class = full access) ────────────────
            comp.canvasGroup     = cg;
            comp.overlay         = rootImg;
            comp.card            = cardRt;
            comp.rarityBg        = rarityBgImg;
            comp.diagonalGlow1   = g1Img;
            comp.diagonalGlow2   = g2Img;
            comp.topAccentLine   = accentImg;
            comp.skinNameLabel   = nameTmp;
            comp.vpLabel         = vpTmp;
            comp.skinIconImage   = iconImg;
            comp.rarityBadgeBg   = badgeBgImg;
            comp.rarityLabel     = rarityTmp;
            comp.categoryLabel   = catTmp;
            comp.confirmButton   = confirmBtn;
            comp.confirmButtonBg = btnBgImg;

            // Register confirm listener AFTER wiring (Awake's listener add was skipped because confirmButton was null)
            confirmBtn.onClick.RemoveAllListeners();
            confirmBtn.onClick.AddListener(comp.OnConfirmClicked);

            Debug.Log("[DEBUG][POPUP] RuntimeBuildPopup complete — all fields wired");
            return comp;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        bool _outsideCloseWired;
        void EnsureOutsideClose()
        {
            if (_outsideCloseWired) return;
            _outsideCloseWired = true;

            if (card != null)
            {
                var cardImg = card.GetComponent<Image>();
                if (cardImg != null) cardImg.raycastTarget = true;
                // Absorbs taps inside the panel so they don't bubble to the overlay close.
                if (card.GetComponent<Button>() == null)
                    card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            }
            if (overlay != null)
            {
                var btn = overlay.GetComponent<Button>();
                if (btn == null) btn = overlay.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.RemoveListener(OnConfirmClicked);
                btn.onClick.AddListener(OnConfirmClicked);
            }
        }

        void OnConfirmClicked()
        {
            Debug.Log("[DEBUG][POPUP] Confirm clicked");
            Hide();
            var cb = _onConfirm;
            _onConfirm = null;
            cb?.Invoke();
        }

        void ApplyRarityTheme(SkinDefinitionSO skin)
        {
            var rc = skin != null ? ResolveRarityColor(skin) : new Color(0.4f, 0.4f, 0.4f);
            SetColor(rarityBg,       rc, 0.40f);
            SetColor(diagonalGlow1,  rc, 0.14f);
            SetColor(diagonalGlow2,  rc, 0.07f);
            SetColor(topAccentLine,  rc, 1.00f);
            SetColor(rarityBadgeBg,  rc, 0.75f);
            if (confirmButtonBg != null)
                confirmButtonBg.color = new Color(rc.r * 0.55f, rc.g * 0.55f, rc.b * 0.55f, 1f);
            if (rarityLabel != null) rarityLabel.color = rc;
        }

        void PopulateContent(SkinDefinitionSO skin)
        {
            if (skin == null) return;

            if (skinIconImage != null)
            {
                skinIconImage.sprite  = skin.Icon;
                skinIconImage.enabled = skin.Icon != null;
                skinIconImage.color   = Color.white;
            }
            if (vpLabel != null) vpLabel.text = $"{skin.VpValue:N0} VP";

            if (skinNameLabel != null)
            {
                skinNameLabel.gameObject.SetActive(true);
                skinNameLabel.text = skin.SkinName;
            }

            // Rarity and weapon-type labels (and the rarity badge pill) stay hidden.
            if (rarityLabel   != null) rarityLabel.gameObject.SetActive(false);
            if (rarityBadgeBg != null) rarityBadgeBg.gameObject.SetActive(false);
            if (categoryLabel != null) categoryLabel.gameObject.SetActive(false);

            if (skinIconImage != null) SetAnchoredY(skinIconImage.rectTransform, 90f);
            if (skinNameLabel != null) SetAnchoredY(skinNameLabel.rectTransform, -66f);
            if (vpLabel       != null) SetAnchoredY(vpLabel.rectTransform, -124f);
        }

        static void SetAnchoredY(RectTransform rt, float y)
        {
            var p = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(p.x, y);
        }

        // Mobile: simple, quick fade + subtle scale (0.94 → 1.00). No overshoot.
        IEnumerator PopInAnimation()
        {
            if (card == null) yield break;

            card.localScale = new Vector3(0.94f, 0.94f, 1f);
            SetCG(0f, false);

            const float dur = 0.20f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float s = Mathf.Lerp(0.94f, 1f, p);
                card.localScale = new Vector3(s, s, 1f);
                if (canvasGroup != null) canvasGroup.alpha = p;
                yield return null;
            }

            card.localScale = Vector3.one;
            SetCG(1f, true);
            _animCo = null;
        }

        static Color ResolveRarityColor(SkinDefinitionSO skin)
        {
            var ctx = GameContext.Instance;
            if (ctx?.RarityVisuals != null && ctx.RarityVisuals.TryGet(skin.Rarity, out var e))
                return e.primaryColor;
            return RarityColors.TryGetValue(skin.Rarity, out var c) ? c : new Color(0.45f, 0.45f, 0.45f);
        }

        static void SetColor(Image img, Color baseColor, float alpha)
        {
            if (img == null) return;
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        void SetCG(float alpha, bool interactive)
        {
            if (canvasGroup == null) return;
            canvasGroup.alpha          = alpha;
            canvasGroup.interactable   = interactive;
            canvasGroup.blocksRaycasts = interactive;
        }

        // ── UI construction helpers (safe RectTransform-first pattern) ────────

        static Image MakeImage(string name, Transform parent, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            if (stretch) Stretch((RectTransform)go.transform);
            return go.GetComponent<Image>();
        }

        static TextMeshProUGUI MakeText(string name, Transform parent, string text,
                                        float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = text;
            tmp.fontSize      = size;
            tmp.fontStyle     = style;
            tmp.color         = color;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
        }
    }
}
