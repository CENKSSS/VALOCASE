using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Screen 1 — Case Battle Lobby List, and the entry point for the whole lobby flow.
    /// Registered with the UINavigator as ScreenType.CaseBattle. Hosts the Create Battle
    /// and Waiting Room panels as internal overlays (matching ToolsScreen → MissionsScreen),
    /// so the multi-step flow stays self-contained without new ScreenType entries.
    /// </summary>
    public sealed class LobbyListScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;

        const float HeaderH  = 48f;
        const float FilterH  = 44f;
        const float FooterH  = 88f;
        const float SidePad  = 14f;

        bool _built;
        ScreenType _prevScreen = ScreenType.CaseOpening;

        bool _showMineOnly;
        List<BattleLobbyData> _lobbies = new();

        RectTransform   _listContent;
        GameObject      _emptyState;
        Image           _tabAll;
        TextMeshProUGUI _tabAllLbl;
        Image           _tabMine;
        TextMeshProUGUI _tabMineLbl;
        AngledCutImage  _createBg;
        TextMeshProUGUI _activeCountLabel;

        CreateBattleScreen _createPanel;
        WaitingRoomScreen  _waitingPanel;
        CanvasGroup        _selfGroup;

        readonly List<GameObject> _cardGos = new();

        void Awake() { }

        protected override void OnShown()
        {
            _prevScreen = navigator != null ? navigator.PreviousScreen : ScreenType.CaseOpening;
            BuildOnce();
            _createPanel?.Hide();
            _waitingPanel?.Hide();
            RefreshList();
            StartCoroutine(EntranceAnim());
        }

        IEnumerator EntranceAnim()
        {
            var rt = (RectTransform)transform;
            yield return UIAnimator.SlideFromBottom(rt, 0.25f);
        }

        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            _selfGroup = EnsureCanvasGroup(gameObject);
            var rt = (RectTransform)transform;

            var bg = MakeImage("Bg", rt, ColorPalette.BgDeep, raycast: true);
            Stretch(bg.rectTransform);
            bg.raycastTarget = false;

            BuildHeader(rt);
            BuildFilterBar(rt);
            BuildScrollList(rt);
            BuildFooter(rt);
            BuildSubPanels(rt);

            _lobbies = CaseBattleSampleData.BuildSampleLobbies();
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.CardBg, raycast: false);
            TopStrip(hdr.rectTransform, HeaderH);

            // Bottom border
            var hdrBorder = MakeImage("BottomBorder", hdr.transform, ColorPalette.Border);
            hdrBorder.raycastTarget = false;
            SetRect(hdrBorder.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 1f));

            // Back button — compact, left side
            var back = NewGo("BackBtn", hdr.transform, typeof(AngledCutImage), typeof(Button));
            var backImg = back.GetComponent<AngledCutImage>();
            backImg.color = ColorPalette.Surface; backImg.CutSize = 4f; backImg.raycastTarget = true;
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin        = new Vector2(0f, 0.5f);
            backRt.anchorMax        = new Vector2(0f, 0.5f);
            backRt.pivot            = new Vector2(0f, 0.5f);
            backRt.anchoredPosition = new Vector2(12f, 0f);
            backRt.sizeDelta        = new Vector2(32f, 28f);
            var backLbl = MakeTmp(back.transform, "Lbl", "‹", 18f, FontStyles.Bold, ColorPalette.TextBright);
            backLbl.alignment = TextAlignmentOptions.Center;
            Stretch(backLbl.rectTransform);
            var backBtn = back.GetComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(OnBackClicked);

            // Title — left-aligned, matching reference
            var title = MakeTmp(hdr.transform, "Title", "LIVE LOBBIES", 16f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 2f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            var titleRt = title.rectTransform;
            titleRt.anchorMin        = new Vector2(0f, 0.5f);
            titleRt.anchorMax        = new Vector2(1f, 0.5f);
            titleRt.pivot            = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(52f, 0f);
            titleRt.sizeDelta        = new Vector2(-160f, 20f);

            // Active count — right side, brighter for premium feel
            _activeCountLabel = MakeTmp(hdr.transform, "ActiveCount", "0 ACTIVE", 11f, FontStyles.Bold, ColorPalette.ActiveRed);
            _activeCountLabel.alignment = TextAlignmentOptions.MidlineRight;
            var acRt = _activeCountLabel.rectTransform;
            acRt.anchorMin        = new Vector2(1f, 0.5f);
            acRt.anchorMax        = new Vector2(1f, 0.5f);
            acRt.pivot            = new Vector2(1f, 0.5f);
            acRt.anchoredPosition = new Vector2(-12f, 0f);
            acRt.sizeDelta        = new Vector2(80f, 16f);
        }

        void BuildFilterBar(RectTransform rt)
        {
            var bar = MakeImage("FilterBar", rt, ColorPalette.CardBg, raycast: false);
            var barRt = bar.rectTransform;
            TopStrip(barRt, FilterH, -HeaderH);

            // Bottom border
            var barBorder = MakeImage("BottomBorder", bar.transform, ColorPalette.Border);
            barBorder.raycastTarget = false;
            SetRect(barBorder.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 1f));

            // HorizontalLayoutGroup distributes tabs automatically
            var hlg = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset(16, 16, 0, 0);
            hlg.spacing               = 8f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            _tabAll  = BuildFilterTab(bar.transform, "ALL",  72f, out _tabAllLbl,  () => SetFilter(false));
            _tabMine = BuildFilterTab(bar.transform, "MINE", 80f, out _tabMineLbl, () => SetFilter(true));
        }

        Image BuildFilterTab(Transform parent, string text, float width, out TextMeshProUGUI lbl, System.Action onClick)
        {
            var go = NewGo("Tab_" + text, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            var img = go.GetComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
            go.GetComponent<LayoutElement>().minWidth = width;
            var tabRt = (RectTransform)go.transform;
            tabRt.sizeDelta = new Vector2(width, 36f);
            lbl = MakeTmp(go.transform, "Lbl", text, 11f, FontStyles.Bold, ColorPalette.TextDim);
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            return img;
        }

        void BuildScrollList(RectTransform rt)
        {
            var scrollGo = NewGo("Scroll", rt, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0f, FooterH);
            scrollRt.offsetMax = new Vector2(0f, -(HeaderH + FilterH));

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var content = NewGo("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin        = new Vector2(0f, 1f);
            _listContent.anchorMax        = new Vector2(1f, 1f);
            _listContent.pivot            = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding               = new RectOffset(12, 12, 10, 24);
            vlg.spacing               = 8f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content         = _listContent;
            sr.viewport        = (RectTransform)viewport.transform;
            sr.horizontal      = false;
            sr.vertical        = true;
            sr.scrollSensitivity = 30f;

            BuildEmptyState(viewport.transform);
        }

        void BuildEmptyState(Transform parent)
        {
            _emptyState = NewGo("EmptyState", parent);
            var eRt = (RectTransform)_emptyState.transform;
            eRt.anchorMin        = new Vector2(0.5f, 0.5f);
            eRt.anchorMax        = new Vector2(0.5f, 0.5f);
            eRt.pivot            = new Vector2(0.5f, 0.5f);
            eRt.anchoredPosition = new Vector2(0f, 40f);
            eRt.sizeDelta        = new Vector2(300f, 220f);

            var t1 = MakeTmp(_emptyState.transform, "Title", "NO ACTIVE BATTLES", 16f, FontStyles.Bold, ColorPalette.TextBright);
            t1.alignment = TextAlignmentOptions.Center;
            SetRect(t1.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(0f, 24f));

            var t2 = MakeTmp(_emptyState.transform, "Sub", "Be the first to start a battle", 13f, FontStyles.Normal, ColorPalette.TextDim);
            t2.alignment = TextAlignmentOptions.Center;
            SetRect(t2.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -52f), new Vector2(0f, 20f));

            var nudge = NewGo("Nudge", _emptyState.transform, typeof(AngledCutImage), typeof(Button));
            var nudgeImg = nudge.GetComponent<AngledCutImage>();
            nudgeImg.color = ColorPalette.ActiveRed; nudgeImg.CutSize = 8f; nudgeImg.raycastTarget = true;
            SetRect((RectTransform)nudge.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -90f), new Vector2(180f, 44f));
            var nudgeLbl = MakeTmp(nudge.transform, "Lbl", "CREATE BATTLE", 13f, FontStyles.Bold, Color.white);
            nudgeLbl.alignment = TextAlignmentOptions.Center;
            Stretch(nudgeLbl.rectTransform);
            var nudgeBtn = nudge.GetComponent<Button>();
            nudgeBtn.transition = Selectable.Transition.None;
            nudgeBtn.onClick.AddListener(OpenCreate);

            _emptyState.SetActive(false);
        }

        void BuildFooter(RectTransform rt)
        {
            var footer = MakeImage("Footer", rt, ColorPalette.CardBg, raycast: true);
            BottomStrip(footer.rectTransform, FooterH);

            // Top border
            var borderImg = MakeImage("TopBorder", footer.transform, ColorPalette.Border);
            borderImg.raycastTarget = false;
            TopStrip(borderImg.rectTransform, 1f);

            // Create button — stretched horizontally, fixed height 56
            _createBg = MakeAngled("CreateBtn", footer.transform, ColorPalette.ActiveRed, 12f, raycast: true);
            var createRt = _createBg.rectTransform;
            createRt.anchorMin        = new Vector2(0f, 0.5f);
            createRt.anchorMax        = new Vector2(1f, 0.5f);
            createRt.pivot            = new Vector2(0.5f, 0.5f);
            createRt.anchoredPosition = new Vector2(0f, 6f);
            createRt.offsetMin        = new Vector2(16f, createRt.offsetMin.y);
            createRt.offsetMax        = new Vector2(-16f, createRt.offsetMax.y);
            createRt.sizeDelta        = new Vector2(createRt.sizeDelta.x, 56f);
            var createBtn = _createBg.gameObject.AddComponent<Button>();
            createBtn.transition = Selectable.Transition.None;
            createBtn.onClick.AddListener(OnCreatePressed);

            var plus = MakeTmp(_createBg.transform, "Plus", "＋", 20f, FontStyles.Bold, Color.white);
            plus.alignment = TextAlignmentOptions.Center;
            SetRect(plus.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(28f, 0f), new Vector2(28f, 28f));

            var lbl = MakeTmp(_createBg.transform, "Lbl", "CREATE BATTLE", 15f, FontStyles.Bold, Color.white);
            lbl.characterSpacing = 2f;
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);
        }

        void BuildSubPanels(RectTransform rt)
        {
            var createGo = NewGo("CreatePanel", rt);
            Stretch(createGo);
            _createPanel = createGo.AddComponent<CreateBattleScreen>();
            _createPanel.OnBack    += CloseCreate;
            _createPanel.OnConfirm += OpenWaitingFromCreate;
            createGo.SetActive(false);

            var waitGo = NewGo("WaitingPanel", rt);
            Stretch(waitGo);
            _waitingPanel = waitGo.AddComponent<WaitingRoomScreen>();
            _waitingPanel.OnLeave       += CloseWaiting;
            _waitingPanel.OnStartBattle += OnBattleStart;
            waitGo.SetActive(false);
        }

        // ── List refresh ──────────────────────────────────────────────────────
        void RefreshList()
        {
            foreach (var go in _cardGos) Destroy(go);
            _cardGos.Clear();

            int shown = 0;
            foreach (var lobby in _lobbies)
            {
                if (_showMineOnly && lobby.HostName != "YOU") continue;
                var card = LobbyCard.Create(_listContent, lobby, OnJoinLobby);
                _cardGos.Add(card.gameObject);
                shown++;
            }

            _emptyState.SetActive(shown == 0);

            if (_activeCountLabel != null)
                _activeCountLabel.text = shown + " ACTIVE";

            // Tab active state
            bool allActive = !_showMineOnly;
            _tabAll.color     = allActive ? ColorPalette.ActiveRed : Color.clear;
            _tabAllLbl.color  = allActive ? ColorPalette.TextBright : ColorPalette.TextDim;
            ApplyPillBorder(_tabAll, !allActive);

            _tabMine.color    = _showMineOnly ? ColorPalette.ActiveRed : Color.clear;
            _tabMineLbl.color = _showMineOnly ? ColorPalette.TextBright : ColorPalette.TextDim;
            ApplyPillBorder(_tabMine, !_showMineOnly);
        }

        static void ApplyPillBorder(Image pill, bool showBorder)
        {
            var ol = pill.GetComponent<Outline>();
            if (ol == null) ol = pill.gameObject.AddComponent<Outline>();
            ol.effectColor    = showBorder ? ColorPalette.Border : Color.clear;
            ol.effectDistance = new Vector2(1f, -1f);
        }

        void SetFilter(bool mineOnly)
        {
            _showMineOnly = mineOnly;
            RefreshList();
        }

        // ── Navigation / flow ─────────────────────────────────────────────────
        void OnBackClicked()
        {
            var target = (_prevScreen != ScreenType.CaseBattleLobby) ? _prevScreen : ScreenType.CaseOpening;
            navigator?.Navigate(target);
        }

        void OnCreatePressed()
        {
            StartCoroutine(UIAnimator.ScalePress(_createBg.transform, 0.97f, 0.12f));
            OpenCreate();
        }

        void OpenCreate()
        {
            _createPanel.Show();
            StartCoroutine(UIAnimator.SlideLeft((RectTransform)_createPanel.transform, 0.22f));
        }

        void CloseCreate() => _createPanel.Hide();

        void OnJoinLobby(BattleLobbyData lobby)
        {
            bool isHost = lobby.HostName == "YOU";
            _waitingPanel.Show(lobby, isHost);
            var cg = EnsureCanvasGroup(_waitingPanel.gameObject);
            StartCoroutine(UIAnimator.CrossFade(null, cg, 0.2f));
        }

        void OpenWaitingFromCreate(BattleLobbyData lobby)
        {
            _createPanel.Hide();
            _waitingPanel.Show(lobby, isHost: true);
            var cg = EnsureCanvasGroup(_waitingPanel.gameObject);
            StartCoroutine(ScaleCrossFade(cg));
        }

        IEnumerator ScaleCrossFade(CanvasGroup cg)
        {
            var rt = (RectTransform)cg.transform;
            rt.localScale = Vector3.one * 0.95f;
            cg.alpha = 0f;
            float t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.25f);
                cg.alpha = k;
                rt.localScale = Vector3.Lerp(Vector3.one * 0.95f, Vector3.one, k);
                yield return null;
            }
            cg.alpha = 1f;
            rt.localScale = Vector3.one;
        }

        void CloseWaiting()
        {
            _waitingPanel.Hide();
            RefreshList();
        }

        void OnBattleStart(BattleLobbyData lobby)
        {
            _waitingPanel.Hide();
            navigator?.Navigate(ScreenType.CaseOpening);
        }

        protected override void OnHidden()
        {
            StopAllCoroutines();
            _createPanel?.Hide();
            _waitingPanel?.Hide();
        }

        void OnDisable() => StopAllCoroutines();
    }
}
