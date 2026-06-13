using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Systems;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Tools hub screen — links to VP Kazan, Çark, and Görevler.
    ///
    /// VP KAZAN row navigates to the existing EarnVpScreen.
    /// ÇARK and GÖREVLER show a Coming Soon overlay (dismissible).
    /// All UI built procedurally in BuildOnce() — no Inspector setup needed.
    /// </summary>
    public sealed class ToolsScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;

        // ── Palette (Valorant dark navy + neon red, matches BottomNavBar) ─────
        static readonly Color CardBg    = new Color(0.040f, 0.065f, 0.125f, 1f);
        static readonly Color ActiveRed = new Color(1f, 0.122f, 0.224f, 1f);
        static readonly Color AccentVP  = new Color(0.902f, 0.816f, 0.435f, 1f);   // gold
        static readonly Color AccentWhl = new Color(0.68f,  0.08f,  1.00f, 1f);   // purple
        static readonly Color AccentMsn = new Color(0.00f,  0.90f,  1.00f, 1f);   // cyan
        static readonly Color TextBright= new Color(0.925f, 0.910f, 0.882f, 1f);
        static readonly Color TextDim   = new Color(1f, 1f, 1f, 0.38f);

        bool            _built;
        ScreenType      _prevScreen;
        GameObject      _backBtnGo;
        GameObject      _comingSoonPanel;
        TextMeshProUGUI _comingSoonTitle;
        MissionSystem   _missionSystem;
        MissionsScreen  _missionsPanel;

        public void Inject(MissionSystem system)
        {
            _missionSystem = system;
            if (_built) EnsureMissionsPanel();
        }

        void EnsureMissionsPanel()
        {
            if (_missionsPanel != null || _missionSystem == null) return;
            var rt    = (RectTransform)transform;
            var msnGo = new GameObject("MissionsPanel", typeof(RectTransform));
            msnGo.transform.SetParent(rt, false);
            var mRt   = (RectTransform)msnGo.transform;
            mRt.anchorMin = Vector2.zero; mRt.anchorMax = Vector2.one;
            mRt.offsetMin = mRt.offsetMax = Vector2.zero;
            _missionsPanel = msnGo.AddComponent<MissionsScreen>();
            _missionsPanel.Init(_missionSystem);
            msnGo.SetActive(false);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            // Builder-wired back button: reuse with correct previous-screen navigation
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
        }

        void OnBackClicked()
        {
            var targetScreen = (_prevScreen != ScreenType.Tools &&
                                _prevScreen != ScreenType.Settings &&
                                _prevScreen != ScreenType.EarnVp)
                ? _prevScreen
                : ScreenType.CaseOpening;   // fallback → CASES
            navigator?.Navigate(targetScreen);
        }

        protected override void OnShown()
        {
            _prevScreen = navigator?.PreviousScreen ?? ScreenType.CaseOpening;
            BuildOnce();
            // Make sure overlays are hidden when re-entering
            if (_comingSoonPanel != null) _comingSoonPanel.SetActive(false);
            _missionsPanel?.Hide();
            // Re-assert back button on top (handles re-entry after ComingSoon was shown)
            EnsureBackButtonOnTop();
        }

        // ── Build (runs once on first show) ───────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            // Shared section background (cover image, aspect preserved)
            FullscreenBackground.AttachShared(gameObject);

            // Content area — between TopProfileBar (115 px) and BottomNavBar (110 px)
            const float topPad  = 115f;
            const float botPad  = 110f;
            const float sidePad =  24f;

            var content = NewGo("Content", rt, typeof(VerticalLayoutGroup));
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = new Vector2(sidePad, botPad);
            cRt.offsetMax = new Vector2(-sidePad, -topPad);

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.spacing               = 14f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 10, 8);

            // Section header
            var hdr = MakeTmp(content.transform, "Header", "TOOLS",
                20f, FontStyles.Bold, TextBright);
            hdr.characterSpacing = 5f;
            hdr.alignment = TextAlignmentOptions.Left;
            hdr.gameObject.AddComponent<LayoutElement>().minHeight = 34f;

            // Thin divider under header
            var div = NewGo("Div", content.transform, typeof(Image), typeof(LayoutElement));
            div.GetComponent<Image>().color = new Color(1f, 0.122f, 0.224f, 0.20f);
            div.GetComponent<LayoutElement>().minHeight = 1f;

            // Small spacer
            var sp0 = NewGo("Sp0", content.transform, typeof(LayoutElement));
            sp0.GetComponent<LayoutElement>().minHeight = 10f;

            // ── Three rows ────────────────────────────────────────────────────
            BuildRow(content.transform, "VP KAZAN", AccentVP, () =>
            {
                navigator?.Navigate(ScreenType.EarnVp);
            });

            BuildRow(content.transform, "CARK", AccentWhl, () =>
            {
                ShowComingSoon("CARK");
            });

            BuildRow(content.transform, "GOREVLER", AccentMsn, () =>
            {
                if (_missionsPanel != null) _missionsPanel.Show();
                else ShowComingSoon("GOREVLER");
            });

            // Coming Soon overlay (hidden until needed)
            BuildComingSoonPanel(rt);

            // Missions overlay panel (full-screen, shown on GOREVLER tap)
            EnsureMissionsPanel();

            // ── BACK button — built last so it renders on top of all content ──
            BuildBackButton(rt);
        }

        // ── Tappable row card ──────────────────────────────────────────────────
        void BuildRow(Transform parent, string label, Color accent, System.Action onClick)
        {
            var rowGo = NewGo("Row_" + label, parent,
                typeof(Image), typeof(Button), typeof(Outline), typeof(LayoutElement),
                typeof(EventTrigger));
            rowGo.GetComponent<LayoutElement>().minHeight = 80f;

            var rowImg = rowGo.GetComponent<Image>();
            rowImg.color = CardBg;

            var ol = rowGo.GetComponent<Outline>();
            ol.effectColor    = new Color(accent.r, accent.g, accent.b, 0.28f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = rowGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());

            // Left accent bar
            var barGo = NewGo("Bar", rowGo.transform, typeof(Image));
            var bRt = (RectTransform)barGo.transform;
            bRt.anchorMin        = new Vector2(0f, 0f);
            bRt.anchorMax        = new Vector2(0f, 1f);
            bRt.pivot            = new Vector2(0f, 0.5f);
            bRt.anchoredPosition = Vector2.zero;
            bRt.sizeDelta        = new Vector2(3.5f, 0f);
            barGo.GetComponent<Image>().color        = accent;
            barGo.GetComponent<Image>().raycastTarget = false;

            // Label
            var lbl = MakeTmp(rowGo.transform, "Lbl", label, 17f, FontStyles.Bold, TextBright);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lRt = lbl.rectTransform;
            lRt.anchorMin        = new Vector2(0f, 0f);
            lRt.anchorMax        = new Vector2(0.8f, 1f);
            lRt.offsetMin        = new Vector2(20f, 0f);
            lRt.offsetMax        = Vector2.zero;
            lbl.raycastTarget    = false;

            // Chevron arrow
            var arrow = MakeTmp(rowGo.transform, "Arrow", ">", 20f, FontStyles.Bold,
                new Color(accent.r, accent.g, accent.b, 0.55f));
            arrow.alignment = TextAlignmentOptions.MidlineRight;
            var aRt = arrow.rectTransform;
            aRt.anchorMin        = new Vector2(1f, 0f);
            aRt.anchorMax        = new Vector2(1f, 1f);
            aRt.pivot            = new Vector2(1f, 0.5f);
            aRt.anchoredPosition = new Vector2(-16f, 0f);
            aRt.sizeDelta        = new Vector2(30f, 0f);
            arrow.raycastTarget  = false;

            // Hover tint via EventTrigger
            var et  = rowGo.GetComponent<EventTrigger>();
            var img = rowImg;
            AddPE(et, EventTriggerType.PointerEnter,
                _ => img.color = new Color(accent.r * 0.18f, accent.g * 0.18f, accent.b * 0.18f, 1f));
            AddPE(et, EventTriggerType.PointerExit,
                _ => img.color = CardBg);
        }

        // ── BACK button (top-left, below TopProfileBar) ───────────────────────
        void BuildBackButton(RectTransform screenRt)
        {
            if (_backBtnGo != null)
            {
                EnsureBackButtonOnTop();
                return;
            }
            // Serialized back button already covers the behavior — skip procedural one.
            if (backButton != null) return;

            _backBtnGo = new GameObject("BackBtn",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            _backBtnGo.transform.SetParent(screenRt, false);

            var bRt = (RectTransform)_backBtnGo.transform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(0f, 1f);
            bRt.pivot            = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(28f, -88f);
            bRt.sizeDelta        = new Vector2(96f, 36f);

            _backBtnGo.GetComponent<Image>().color = new Color(0.031f, 0.055f, 0.102f, 0.97f);

            var ol = _backBtnGo.GetComponent<Outline>();
            ol.effectColor    = new Color(1f, 0.122f, 0.224f, 0.80f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var lbl = MakeTmp(_backBtnGo.transform, "Lbl", "BACK",
                12f, FontStyles.Bold, Color.white);
            lbl.alignment = TextAlignmentOptions.Center;
            var lRt = lbl.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = lRt.offsetMax = Vector2.zero;

            var btn = _backBtnGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnBackClicked);

            EnsureBackButtonOnTop();
        }

        void EnsureBackButtonOnTop()
        {
            if (_backBtnGo == null) return;
            _backBtnGo.SetActive(true);
            _backBtnGo.transform.SetParent((RectTransform)transform, false);

            // Re-apply position in case anything shifted
            var bRt = (RectTransform)_backBtnGo.transform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(0f, 1f);
            bRt.pivot            = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(28f, -88f);
            bRt.sizeDelta        = new Vector2(96f, 36f);

            _backBtnGo.transform.SetAsLastSibling();
        }

        // ── Full-screen Coming Soon overlay ───────────────────────────────────
        void BuildComingSoonPanel(RectTransform screenRt)
        {
            _comingSoonPanel = NewGo("ComingSoon", screenRt, typeof(Image), typeof(Button));
            Stretch(_comingSoonPanel);
            _comingSoonPanel.GetComponent<Image>().color = new Color(0.016f, 0.025f, 0.055f, 0.96f);

            // Tap anywhere to dismiss
            var dismissBtn = _comingSoonPanel.GetComponent<Button>();
            dismissBtn.transition = Selectable.Transition.None;
            dismissBtn.onClick.AddListener(() => _comingSoonPanel.SetActive(false));

            // Title (set dynamically)
            _comingSoonTitle = MakeTmp(_comingSoonPanel.transform, "CSTitle", "",
                30f, FontStyles.Bold, ActiveRed);
            _comingSoonTitle.alignment       = TextAlignmentOptions.Center;
            _comingSoonTitle.characterSpacing = 5f;
            _comingSoonTitle.raycastTarget   = false;
            var tRt = _comingSoonTitle.rectTransform;
            tRt.anchorMin        = new Vector2(0f, 0.5f);
            tRt.anchorMax        = new Vector2(1f, 0.5f);
            tRt.pivot            = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = new Vector2(0f, 32f);
            tRt.sizeDelta        = new Vector2(0f, 44f);

            // Sub label
            var sub = MakeTmp(_comingSoonPanel.transform, "CSSub", "Coming Soon",
                18f, FontStyles.Normal, TextDim);
            sub.alignment     = TextAlignmentOptions.Center;
            sub.raycastTarget = false;
            var sRt = sub.rectTransform;
            sRt.anchorMin        = new Vector2(0f, 0.5f);
            sRt.anchorMax        = new Vector2(1f, 0.5f);
            sRt.pivot            = new Vector2(0.5f, 0.5f);
            sRt.anchoredPosition = new Vector2(0f, -24f);
            sRt.sizeDelta        = new Vector2(0f, 32f);

            // Dismiss hint
            var hint = MakeTmp(_comingSoonPanel.transform, "Hint", "[ tap to close ]",
                10f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.20f));
            hint.alignment     = TextAlignmentOptions.Center;
            hint.raycastTarget = false;
            var hRt = hint.rectTransform;
            hRt.anchorMin        = new Vector2(0f, 0f);
            hRt.anchorMax        = new Vector2(1f, 0f);
            hRt.pivot            = new Vector2(0.5f, 0f);
            hRt.anchoredPosition = new Vector2(0f, 120f);
            hRt.sizeDelta        = new Vector2(0f, 24f);

            _comingSoonPanel.SetActive(false);
        }

        void ShowComingSoon(string title)
        {
            if (_comingSoonPanel == null) return;
            _comingSoonTitle.text = title;
            _comingSoonPanel.SetActive(true);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        static GameObject NewGo(string name, Transform parent, params System.Type[] comps)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (var c in comps) go.AddComponent(c);
            return go;
        }

        static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static TextMeshProUGUI MakeTmp(Transform parent, string name, string text,
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
            return tmp;
        }

        static void AddPE(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(action);
            et.triggers.Add(e);
        }
    }
}
