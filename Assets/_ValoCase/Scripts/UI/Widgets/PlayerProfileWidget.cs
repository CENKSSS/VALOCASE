using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Profile;

namespace ValoCase.UI.Widgets
{
    /// <summary>
    /// Self-contained Settings / Profile popup widget.
    ///
    /// Two entry points:
    ///   <see cref="Initialise"/>         — top-bar button + modal (used by CaseBattleScreen).
    ///   <see cref="InitialiseModalOnly"/> — modal only, no button (used by ProfileManager).
    ///
    /// In both cases the modal lives on the root Canvas so it is never hidden
    /// when a screen's CanvasGroup is toggled.
    ///
    /// <see cref="OpenSettings"/> is public so ProfileManager can open the
    /// global instance from any screen (e.g. the embedded SettingsScreen button).
    ///
    /// Avatar PNG fix: every Image that displays a sprite uses Color.white so
    /// Unity's default multiply-blend never darkens the texture.
    /// </summary>
    public sealed class PlayerProfileWidget : MonoBehaviour
    {
        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color BgPanel        = new Color(0.032f, 0.022f, 0.072f, 0.99f);
        static readonly Color BgCard         = new Color(0.058f, 0.044f, 0.115f, 1.00f);
        static readonly Color BgInput        = new Color(0.020f, 0.014f, 0.055f, 1.00f);
        static readonly Color AccentPink     = new Color(1.00f, 0.18f, 0.55f, 1.00f);
        static readonly Color AccentPinkSoft = new Color(1.00f, 0.18f, 0.55f, 0.22f);
        static readonly Color AccentPinkGlow = new Color(1.00f, 0.18f, 0.55f, 0.07f);
        static readonly Color TextWhite      = new Color(0.97f, 0.97f, 1.00f, 1.00f);
        static readonly Color TextDim        = new Color(0.48f, 0.45f, 0.62f, 1.00f);
        static readonly Color BorderPink     = new Color(1.00f, 0.18f, 0.55f, 0.55f);
        static readonly Color BorderPinkFull = new Color(1.00f, 0.18f, 0.55f, 1.00f);

        // ── Top-bar button refs ───────────────────────────────────────────────
        RectTransform   _btnRt;
        Image           _btnGlowImg;
        Image           _widgetAvatarImg;

        // ── Settings modal refs ───────────────────────────────────────────────
        RectTransform   _popupRoot;
        RectTransform   _popupPanel;
        CanvasGroup     _popupCG;
        Image           _popupBigAvatarImg;
        TextMeshProUGUI _popupAgentNameLbl;
        TMP_InputField  _nameInput;
        Transform       _avatarGridContent;

        // ── Pending selection ─────────────────────────────────────────────────
        Sprite _pendingSprite;
        string _pendingKey;

        // ── Data ──────────────────────────────────────────────────────────────
        IReadOnlyList<(string name, Sprite sprite)> _avatars;
        Sprite    _circleMaskSprite;
        Coroutine _animCo;
        Coroutine _scaleCo;

        // ═════════════════════════════════════════════════════════════════════
        // ENTRY POINTS
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Full init: top-bar button (LEFT, after Back button gap) + modal on canvas root.
        /// Call from CaseBattleScreen.BuildUiOnce().
        /// </summary>
        public void Initialise(RectTransform screenRoot, RectTransform topBarRect)
        {
            _circleMaskSprite = MakeCircleSprite(128);

            ProfileManager.EnsureInitialized();
            _avatars = ProfileManager.Avatars;

            BuildTopBarButton(topBarRect);
            BuildSettingsModal(FindCanvasRoot(screenRoot));

            ProfileManager.RegisterWidget(this);

            PlayerProfileData.OnProfileChanged += SyncWidgetToProfile;
            SyncWidgetToProfile();
        }

        /// <summary>
        /// Modal-only init: no top-bar button. Used by ProfileManager for the global instance.
        /// </summary>
        public void InitialiseModalOnly(RectTransform canvasRoot)
        {
            _circleMaskSprite = MakeCircleSprite(128);

            ProfileManager.EnsureInitialized();
            _avatars = ProfileManager.Avatars;

            BuildSettingsModal(canvasRoot);

            PlayerProfileData.OnProfileChanged += SyncWidgetToProfile;
            SyncWidgetToProfile();
        }

        /// <summary>Opens the Settings / Profile modal. Call from any screen.</summary>
        public void OpenSettings() => OpenSettingsPopup();

        void OnDestroy() => PlayerProfileData.OnProfileChanged -= SyncWidgetToProfile;

        // ═════════════════════════════════════════════════════════════════════
        // TOP-BAR PROFILE BUTTON
        // ═════════════════════════════════════════════════════════════════════

        void BuildTopBarButton(RectTransform topBar)
        {
            // ── Position: LEFT side, CLEAR of the Back button ────────────────
            // Back button occupies roughly X=0..60 from left edge.
            // We sit at X=72 so there is zero hitbox overlap.
            _btnRt = MakeRect(topBar, "ProfileBtn",
                Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            _btnRt.anchoredPosition = new Vector2(72f, 0f);
            _btnRt.sizeDelta        = new Vector2(44f, 44f);

            // Transparent hit-area backing (required for Button raycasting)
            _btnGlowImg       = _btnRt.gameObject.AddComponent<Image>();
            _btnGlowImg.color = new Color(1f, 0.18f, 0.55f, 0f);

            var btn = _btnRt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnProfileButtonClicked);

            // Hover / exit events
            var et = _btnRt.gameObject.AddComponent<EventTrigger>();
            AddPointerEvent(et, EventTriggerType.PointerEnter, _ => OnBtnHoverEnter());
            AddPointerEvent(et, EventTriggerType.PointerExit,  _ => OnBtnHoverExit());

            // ── Neon circle ring ──────────────────────────────────────────────
            var ringGo = new GameObject("NeonRing", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(_btnRt, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.anchorMin = Vector2.zero;  ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = new Vector2(1f, 1f);
            ringRt.offsetMax = new Vector2(-1f, -1f);
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite        = _circleMaskSprite;
            ringImg.type          = Image.Type.Simple;
            ringImg.color         = new Color(1f, 0.18f, 0.55f, 0.50f);
            ringImg.raycastTarget = false;
            var ringOl = ringGo.AddComponent<Outline>();
            ringOl.effectColor    = new Color(1f, 0.18f, 0.55f, 0.35f);
            ringOl.effectDistance = new Vector2(1.5f, -1.5f);

            // ── 38 px circular avatar ─────────────────────────────────────────
            _widgetAvatarImg = BuildCircleMaskedImage(_btnRt, "WidgetAv", 38f);
            var avMaskRt     = (RectTransform)_widgetAvatarImg.transform.parent;
            avMaskRt.anchorMin        = new Vector2(0.5f, 0.5f);
            avMaskRt.anchorMax        = new Vector2(0.5f, 0.5f);
            avMaskRt.pivot            = new Vector2(0.5f, 0.5f);
            avMaskRt.anchoredPosition = Vector2.zero;
            avMaskRt.sizeDelta        = new Vector2(38f, 38f);
        }

        void OnProfileButtonClicked()
        {
            if (_scaleCo != null) StopCoroutine(_scaleCo);
            _scaleCo = StartCoroutine(BounceScale(_btnRt, OpenSettingsPopup));
        }

        void OnBtnHoverEnter()
        {
            if (_btnGlowImg) _btnGlowImg.color = new Color(1f, 0.18f, 0.55f, 0.14f);
        }

        void OnBtnHoverExit()
        {
            if (_btnGlowImg) _btnGlowImg.color = new Color(1f, 0.18f, 0.55f, 0f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // SETTINGS MODAL
        // ═════════════════════════════════════════════════════════════════════

        void BuildSettingsModal(RectTransform canvasRoot)
        {
            // Full-screen overlay — sits on the CANVAS ROOT so it's never
            // occluded by a screen's CanvasGroup hide.
            _popupRoot = MakeRect(canvasRoot, "SettingsModal",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            _popupRoot.offsetMin = Vector2.zero;
            _popupRoot.offsetMax = Vector2.zero;

            _popupCG = _popupRoot.gameObject.AddComponent<CanvasGroup>();
            _popupCG.alpha          = 0f;
            _popupCG.interactable   = false;
            _popupCG.blocksRaycasts = false;

            // Dark backdrop — click closes
            var backdrop = _popupRoot.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.84f);
            var backdropBtn = _popupRoot.gameObject.AddComponent<Button>();
            backdropBtn.onClick.AddListener(CloseSettingsPopup);

            // ── 680 × 580 glassmorphism panel ─────────────────────────────────
            _popupPanel = MakeRect(_popupRoot, "Panel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _popupPanel.sizeDelta  = new Vector2(680f, 580f);
            _popupPanel.localScale = Vector3.one * 0.90f;

            var panelImg = _popupPanel.gameObject.AddComponent<Image>();
            panelImg.color = BgPanel;

            var blocker = _popupPanel.gameObject.AddComponent<Button>();
            blocker.onClick.AddListener(() => { });

            // Borders & glow
            AddBorderLine(_popupPanel, BorderPinkFull, top: true,  h: 2.5f);
            AddBorderLine(_popupPanel, BorderPink,     top: false, h: 1f);
            AddSideStrip(_popupPanel, AccentPinkSoft, left: true,  w: 2f);
            AddSideStrip(_popupPanel, AccentPinkSoft, left: false, w: 2f);

            var topGlow = MakeRect(_popupPanel, "TopGlow",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            topGlow.sizeDelta        = new Vector2(0f, 100f);
            topGlow.anchoredPosition = Vector2.zero;
            var tgImg = topGlow.gameObject.AddComponent<Image>();
            tgImg.color         = AccentPinkGlow;
            tgImg.raycastTarget = false;

            // Title bar
            BuildModalTitleBar(_popupPanel);

            // Body
            var body = MakeRect(_popupPanel, "Body",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            body.offsetMin = new Vector2(0f, 0f);
            body.offsetMax = new Vector2(0f, -58f);

            BuildSectionHeader(body, "PROFILE", 32f);

            var content = MakeRect(body, "ProfileContent",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, -32f);

            var leftCol = MakeRect(content, "LeftCol",
                new Vector2(0f, 0f), new Vector2(0.34f, 1f), new Vector2(0f, 0.5f));
            leftCol.offsetMin = new Vector2(20f, 14f);
            leftCol.offsetMax = new Vector2(-6f, -14f);

            var rightCol = MakeRect(content, "RightCol",
                new Vector2(0.34f, 0f), Vector2.one, new Vector2(0f, 0.5f));
            rightCol.offsetMin = new Vector2(8f, 14f);
            rightCol.offsetMax = new Vector2(-20f, -14f);

            BuildLeftColumn(leftCol);
            BuildRightColumn(rightCol);

            _popupRoot.gameObject.SetActive(false);
        }

        void BuildModalTitleBar(RectTransform panel)
        {
            var bar = MakeRect(panel, "TitleBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta        = new Vector2(0f, 58f);

            var barBg = bar.gameObject.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.18f);

            var div = MakeRect(bar, "Divider",
                Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            div.sizeDelta        = new Vector2(0f, 1f);
            div.anchoredPosition = Vector2.zero;
            div.gameObject.AddComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.22f);

            var titleTmp = MakeTmp(bar, "Title", "⚙  SETTINGS",
                14, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            titleTmp.characterSpacing = 2.5f;
            StretchFull(titleTmp.rectTransform);

            var closeRt = MakeRect(bar, "CloseBtn",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            closeRt.sizeDelta        = new Vector2(48f, 48f);
            closeRt.anchoredPosition = new Vector2(-4f, 0f);

            var closeBtn = closeRt.gameObject.AddComponent<Button>();
            closeBtn.onClick.AddListener(CloseSettingsPopup);
            closeBtn.transition = Selectable.Transition.None;

            var closeTmp = MakeTmp(closeRt, "X", "✕",
                18, FontStyles.Bold, TextAlignmentOptions.Center, TextDim);
            StretchFull(closeTmp.rectTransform);
        }

        void BuildSectionHeader(RectTransform parent, string label, float height)
        {
            var sh = MakeRect(parent, $"Sec_{label}",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            sh.anchoredPosition = Vector2.zero;
            sh.sizeDelta        = new Vector2(0f, height);

            sh.gameObject.AddComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.055f);

            var lbl = MakeTmp(sh, "Lbl", label,
                9, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            lbl.characterSpacing = 4f;
            var lRt = lbl.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = new Vector2(24f, 0f); lRt.offsetMax = Vector2.zero;

            var line = MakeRect(sh, "Line",
                Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            line.sizeDelta        = new Vector2(0f, 1f);
            line.anchoredPosition = Vector2.zero;
            line.gameObject.AddComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.18f);
        }

        void BuildLeftColumn(RectTransform col)
        {
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.spacing               = 10f;
            vlg.childForceExpandWidth  = false;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 28, 0);

            const float bigAv = 120f;

            var ringWrap = MakeRect(col, "RingWrap",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ringWrap.sizeDelta = new Vector2(bigAv + 10f, bigAv + 10f);
            var rwImg  = ringWrap.gameObject.AddComponent<Image>();
            rwImg.sprite        = _circleMaskSprite;
            rwImg.type          = Image.Type.Simple;
            rwImg.color         = AccentPinkSoft;
            rwImg.raycastTarget = false;
            var rwOl = ringWrap.gameObject.AddComponent<Outline>();
            rwOl.effectColor    = AccentPink;
            rwOl.effectDistance = new Vector2(2f, -2f);
            var rwLE = ringWrap.gameObject.AddComponent<LayoutElement>();
            rwLE.minWidth  = bigAv + 10f; rwLE.preferredWidth  = bigAv + 10f;
            rwLE.minHeight = bigAv + 10f; rwLE.preferredHeight = bigAv + 10f;

            _popupBigAvatarImg = BuildCircleMaskedImage(ringWrap, "BigAv", bigAv);
            var bigMaskRt = (RectTransform)_popupBigAvatarImg.transform.parent;
            bigMaskRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bigMaskRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bigMaskRt.pivot            = new Vector2(0.5f, 0.5f);
            bigMaskRt.anchoredPosition = Vector2.zero;
            bigMaskRt.sizeDelta        = new Vector2(bigAv, bigAv);
            // Color already set to white inside BuildCircleMaskedImage
            _popupBigAvatarImg.sprite  = PlayerProfileData.Avatar;

            var avHint = MakeTmp(col, "AvHint", "AVATAR",
                8, FontStyles.Bold, TextAlignmentOptions.Center, AccentPink);
            avHint.characterSpacing = 4f;
            avHint.gameObject.AddComponent<LayoutElement>().minHeight = 13f;

            _popupAgentNameLbl = MakeTmp(col, "AgentName",
                PlayerProfileData.AvatarKey,
                12, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            _popupAgentNameLbl.enableWordWrapping = false;
            _popupAgentNameLbl.overflowMode       = TextOverflowModes.Ellipsis;
            _popupAgentNameLbl.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
        }

        void BuildRightColumn(RectTransform col)
        {
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperLeft;
            vlg.spacing               = 8f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var uHint = MakeTmp(col, "UHint", "USERNAME",
                8, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            uHint.characterSpacing = 3f;
            uHint.gameObject.AddComponent<LayoutElement>().minHeight = 12f;

            var iwGo = new GameObject("InputWrap",
                typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            iwGo.transform.SetParent(col, false);
            iwGo.GetComponent<LayoutElement>().minHeight = 46f;
            iwGo.GetComponent<Image>().color = BgInput;
            var iwOl = iwGo.GetComponent<Outline>();
            iwOl.effectColor    = new Color(1f, 0.18f, 0.55f, 0.38f);
            iwOl.effectDistance = new Vector2(1f, -1f);

            _nameInput = BuildInputField(
                (RectTransform)iwGo.transform, PlayerProfileData.Username);

            var saveBtn = BuildActionButton(col, "SAVE  ✓", AccentPink, 46f);
            saveBtn.onClick.AddListener(OnSaveClicked);

            var divGo = new GameObject("Div",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            divGo.transform.SetParent(col, false);
            divGo.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.10f);
            divGo.GetComponent<LayoutElement>().minHeight = 1f;

            var gHint = MakeTmp(col, "GridHint", "SELECT AVATAR",
                8, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            gHint.characterSpacing = 3f;
            gHint.gameObject.AddComponent<LayoutElement>().minHeight = 12f;

            BuildAvatarGrid(col);
        }

        void BuildAvatarGrid(RectTransform parent)
        {
            var scrollGo = new GameObject("AvatarScroll",
                typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
            scrollGo.transform.SetParent(parent, false);
            var scrollLE = scrollGo.GetComponent<LayoutElement>();
            scrollLE.minHeight       = 175f;
            scrollLE.preferredHeight = 200f;
            scrollLE.flexibleHeight  = 1f;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.10f);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.movementType      = ScrollRect.MovementType.Elastic;
            sr.elasticity        = 0.10f;
            sr.inertia           = true;
            sr.decelerationRate  = 0.14f;
            sr.scrollSensitivity = 38f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            sr.viewport = vpRt;

            var cGo = new GameObject("Content",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            cGo.transform.SetParent(vpRt, false);
            var cRt = (RectTransform)cGo.transform;
            cRt.anchorMin        = new Vector2(0f, 1f);
            cRt.anchorMax        = new Vector2(1f, 1f);
            cRt.pivot            = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta        = Vector2.zero;
            sr.content           = cRt;

            var glg = cGo.GetComponent<GridLayoutGroup>();
            glg.cellSize        = new Vector2(76f, 98f);
            glg.spacing         = new Vector2(7f, 7f);
            glg.padding         = new RectOffset(4, 4, 4, 4);
            glg.childAlignment  = TextAnchor.UpperLeft;
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 4;

            cGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _avatarGridContent = cRt;
            PopulateAvatarGrid();
        }

        void PopulateAvatarGrid()
        {
            if (_avatarGridContent == null) return;
            for (int i = _avatarGridContent.childCount - 1; i >= 0; i--)
                Destroy(_avatarGridContent.GetChild(i).gameObject);

            if (_avatars == null || _avatars.Count == 0)
            {
                var msg = MakeTmp(_avatarGridContent, "NoCards",
                    "No face cards found.\n\nPlace images in:\nDesktop/ValorantProject/FaceCards/",
                    10, FontStyles.Normal, TextAlignmentOptions.Center, TextDim);
                msg.enableWordWrapping = true;
                return;
            }

            foreach (var (name, sprite) in _avatars)
                BuildAvatarCell(_avatarGridContent, name, sprite);
        }

        void BuildAvatarCell(Transform grid, string agentName, Sprite sprite)
        {
            bool sel = agentName == PlayerProfileData.AvatarKey;

            var cell    = MakeRect(grid, $"AV_{agentName}",
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            var cellImg = cell.gameObject.AddComponent<Image>();
            cellImg.color = BgCard;

            var selOl = cell.gameObject.AddComponent<Outline>();
            selOl.effectColor    = sel ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.07f);
            selOl.effectDistance = new Vector2(sel ? 2.5f : 0.6f, -(sel ? 2.5f : 0.6f));

            var cellEt = cell.gameObject.AddComponent<EventTrigger>();
            string capN2 = agentName; Outline capOl = selOl;
            AddPointerEvent(cellEt, EventTriggerType.PointerEnter,
                _ => OnCellHoverEnter(capOl, capN2));
            AddPointerEvent(cellEt, EventTriggerType.PointerExit,
                _ => OnCellHoverExit(capOl, capN2));

            var avImg  = BuildCircleMaskedImage(cell, "Av", 62f);
            var avMask = (RectTransform)avImg.transform.parent;
            avMask.anchorMin        = new Vector2(0.5f, 1f);
            avMask.anchorMax        = new Vector2(0.5f, 1f);
            avMask.pivot            = new Vector2(0.5f, 1f);
            avMask.anchoredPosition = new Vector2(0f, -7f);
            avMask.sizeDelta        = new Vector2(62f, 62f);
            avImg.sprite            = sprite;  // color already = white from BuildCircleMaskedImage

            var nameLbl = MakeTmp(cell, "Name", agentName,
                8, FontStyles.Bold, TextAlignmentOptions.Center,
                sel ? AccentPink : TextDim);
            var nRt = nameLbl.rectTransform;
            nRt.anchorMin        = new Vector2(0f, 0f);
            nRt.anchorMax        = new Vector2(1f, 0f);
            nRt.pivot            = new Vector2(0.5f, 0f);
            nRt.anchoredPosition = new Vector2(0f, 6f);
            nRt.sizeDelta        = new Vector2(0f, 14f);
            nameLbl.enableWordWrapping = false;
            nameLbl.overflowMode       = TextOverflowModes.Ellipsis;

            string capN  = agentName;
            Sprite capSp = sprite;
            var btn = cell.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SelectAvatarInGrid(capN, capSp));
        }

        void OnCellHoverEnter(Outline ol, string key)
        {
            if (key == _pendingKey) return;
            ol.effectColor    = new Color(1f, 0.18f, 0.55f, 0.38f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
        }

        void OnCellHoverExit(Outline ol, string key)
        {
            bool sel = key == _pendingKey;
            ol.effectColor    = sel ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.07f);
            ol.effectDistance = new Vector2(sel ? 2.5f : 0.6f, -(sel ? 2.5f : 0.6f));
        }

        // ═════════════════════════════════════════════════════════════════════
        // INTERACTION HANDLERS
        // ═════════════════════════════════════════════════════════════════════

        void OpenSettingsPopup()
        {
            _pendingSprite = PlayerProfileData.Avatar;
            _pendingKey    = PlayerProfileData.AvatarKey;

            if (_nameInput         != null) _nameInput.text           = PlayerProfileData.Username;
            if (_popupBigAvatarImg != null)
            {
                _popupBigAvatarImg.color  = Color.white;
                _popupBigAvatarImg.sprite = PlayerProfileData.Avatar;
            }
            if (_popupAgentNameLbl != null) _popupAgentNameLbl.text   = PlayerProfileData.AvatarKey;

            RefreshGridHighlights();

            _popupRoot.gameObject.SetActive(true);
            _popupRoot.SetAsLastSibling();

            if (_animCo != null) StopCoroutine(_animCo);
            _animCo = StartCoroutine(AnimatePopup(open: true));
        }

        void CloseSettingsPopup()
        {
            if (_animCo != null) StopCoroutine(_animCo);
            _animCo = StartCoroutine(AnimatePopup(open: false));
        }

        void OnSaveClicked()
        {
            var newName = (_nameInput != null && !string.IsNullOrWhiteSpace(_nameInput.text))
                ? _nameInput.text.Trim() : PlayerProfileData.Username;
            PlayerProfileData.SetUsername(newName);

            if (_pendingSprite != null)
                PlayerProfileData.SetAvatar(_pendingSprite, _pendingKey);

            CloseSettingsPopup();
        }

        void SelectAvatarInGrid(string name, Sprite sprite)
        {
            _pendingKey    = name;
            _pendingSprite = sprite;

            if (_popupBigAvatarImg != null)
            {
                _popupBigAvatarImg.color  = Color.white;
                _popupBigAvatarImg.sprite = sprite;
            }
            if (_popupAgentNameLbl != null) _popupAgentNameLbl.text = name;

            RefreshGridHighlights();
        }

        void RefreshGridHighlights()
        {
            if (_avatarGridContent == null) return;
            foreach (Transform child in _avatarGridContent)
            {
                bool sel = child.name == $"AV_{_pendingKey}";
                var ol   = child.GetComponent<Outline>();
                if (ol != null)
                {
                    ol.effectColor    = sel ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.07f);
                    ol.effectDistance = new Vector2(sel ? 2.5f : 0.6f, -(sel ? 2.5f : 0.6f));
                }
                var lbl = child.Find("Name")?.GetComponent<TextMeshProUGUI>();
                if (lbl != null) lbl.color = sel ? AccentPink : TextDim;
            }
        }

        void SyncWidgetToProfile()
        {
            if (_widgetAvatarImg != null)
            {
                _widgetAvatarImg.color  = Color.white;   // ensure never tinted
                _widgetAvatarImg.sprite = PlayerProfileData.Avatar;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ANIMATION
        // ═════════════════════════════════════════════════════════════════════

        IEnumerator AnimatePopup(bool open)
        {
            const float dur = 0.18f;
            float fromAlpha = _popupCG.alpha;
            float toAlpha   = open ? 1f : 0f;
            float fromScale = _popupPanel.localScale.x;
            float toScale   = open ? 1f : 0.90f;

            _popupCG.interactable   = open;
            _popupCG.blocksRaycasts = open;

            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOutQuint(Mathf.Clamp01(t / dur));
                _popupCG.alpha         = Mathf.Lerp(fromAlpha, toAlpha, p);
                float s                = Mathf.Lerp(fromScale, toScale, p);
                _popupPanel.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            _popupCG.alpha         = toAlpha;
            _popupPanel.localScale = new Vector3(toScale, toScale, 1f);
            if (!open) _popupRoot.gameObject.SetActive(false);
        }

        IEnumerator BounceScale(RectTransform rt, System.Action onDone)
        {
            const float cDur = 0.07f, rDur = 0.10f;
            float t = 0f;
            while (t < cDur)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(1f, 0.88f, t / cDur);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            t = 0f;
            while (t < rDur)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOutQuint(Mathf.Clamp01(t / rDur));
                float s = Mathf.Lerp(0.88f, 1f, p);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
            onDone?.Invoke();
        }

        static float EaseOutQuint(float t) => 1f - Mathf.Pow(1f - t, 5f);

        // ═════════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Circular-masked avatar image.  The INNER Image always starts with
        /// <c>Color.white</c> so Unity's default multiply never darkens the sprite.
        /// </summary>
        Image BuildCircleMaskedImage(Transform parent, string name, float diameter)
        {
            var maskGo = new GameObject(name + "_Mask",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            maskGo.transform.SetParent(parent, false);
            var maskRt   = (RectTransform)maskGo.transform;
            maskRt.anchorMin = new Vector2(0.5f, 0.5f);
            maskRt.anchorMax = new Vector2(0.5f, 0.5f);
            maskRt.pivot     = new Vector2(0.5f, 0.5f);
            maskRt.sizeDelta = new Vector2(diameter, diameter);

            var maskImg         = maskGo.GetComponent<Image>();
            maskImg.sprite      = _circleMaskSprite;
            maskImg.type        = Image.Type.Simple;
            maskImg.raycastTarget = false;
            maskGo.GetComponent<Mask>().showMaskGraphic = false;

            var imgGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(maskGo.transform, false);
            var imgRt = (RectTransform)imgGo.transform;
            imgRt.anchorMin = Vector2.zero; imgRt.anchorMax = Vector2.one;
            imgRt.offsetMin = Vector2.zero; imgRt.offsetMax = Vector2.zero;

            var img = imgGo.GetComponent<Image>();
            img.color          = Color.white;   // ← PURE WHITE — never tint avatar sprites
            img.material       = null;          // ensure default UI material (no custom shader)
            img.preserveAspect = true;
            return img;
        }

        TMP_InputField BuildInputField(RectTransform parent, string initialText)
        {
            var root = new GameObject("Field",
                typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            StretchFull((RectTransform)root.transform);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var field = root.GetComponent<TMP_InputField>();

            var taGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            taGo.transform.SetParent(root.transform, false);
            var taRt = (RectTransform)taGo.transform;
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(12f, 6f); taRt.offsetMax = new Vector2(-12f, -6f);

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(taGo.transform, false);
            StretchFull((RectTransform)phGo.transform);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text               = "Enter name…";
            phTmp.fontSize           = 13f;
            phTmp.fontStyle          = FontStyles.Italic;
            phTmp.alignment          = TextAlignmentOptions.MidlineLeft;
            phTmp.color              = TextDim;
            phTmp.raycastTarget      = false;
            phTmp.enableWordWrapping = false;

            var itGo = new GameObject("Text", typeof(RectTransform));
            itGo.transform.SetParent(taGo.transform, false);
            StretchFull((RectTransform)itGo.transform);
            var itTmp = itGo.AddComponent<TextMeshProUGUI>();
            itTmp.fontSize           = 13f;
            itTmp.fontStyle          = FontStyles.Bold;
            itTmp.alignment          = TextAlignmentOptions.MidlineLeft;
            itTmp.color              = TextWhite;
            itTmp.raycastTarget      = false;
            itTmp.enableWordWrapping = false;

            field.textViewport   = taRt;
            field.textComponent  = itTmp;
            field.placeholder    = phTmp;
            field.text           = initialText;
            field.characterLimit = 20;
            field.contentType    = TMP_InputField.ContentType.Standard;
            field.caretColor     = AccentPink;
            field.selectionColor = new Color(1f, 0.18f, 0.55f, 0.28f);

            return field;
        }

        Button BuildActionButton(Transform parent, string label, Color accent, float height)
        {
            var go = new GameObject("Btn_" + label,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(Outline), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var le = go.GetComponent<LayoutElement>();
            le.minHeight     = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var btnImg = go.GetComponent<Image>();
            btnImg.color = new Color(0.035f, 0.024f, 0.082f, 0.96f);
            var ol = go.GetComponent<Outline>();
            ol.effectColor    = accent;
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var lbl = MakeTmp(go.transform, "Lbl", label,
                12, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            lbl.characterSpacing = 1f;
            StretchFull(lbl.rectTransform);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var et = go.AddComponent<EventTrigger>();
            AddPointerEvent(et, EventTriggerType.PointerEnter,
                _ => btnImg.color = new Color(accent.r, accent.g, accent.b, 0.12f));
            AddPointerEvent(et, EventTriggerType.PointerExit,
                _ => btnImg.color = new Color(0.035f, 0.024f, 0.082f, 0.96f));

            return btn;
        }

        // ── Primitives ────────────────────────────────────────────────────────

        /// <summary>Traverses up from screenRoot to find the root Canvas RectTransform.</summary>
        static RectTransform FindCanvasRoot(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas != null) return (RectTransform)canvas.transform;
            // Fallback: use screenRoot itself
            return rt;
        }

        static Sprite MakeCircleSprite(int size)
        {
            var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            float r    = half - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx   = x + 0.5f - half;
                float dy   = y + 0.5f - half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a    = Mathf.Clamp01(r - dist + 1f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
        }

        static RectTransform MakeRect(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            return rt;
        }

        static TextMeshProUGUI MakeTmp(Transform parent, string name, string text,
            float size, FontStyles style, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.fontStyle          = style;
            tmp.alignment          = align;
            tmp.color              = color;
            tmp.raycastTarget      = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void AddBorderLine(RectTransform parent, Color color, bool top, float h)
        {
            var ln = MakeRect(parent,
                top ? "BTop" : "BBot",
                top ? new Vector2(0f, 1f) : Vector2.zero,
                top ? Vector2.one         : new Vector2(1f, 0f),
                top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f));
            ln.anchoredPosition = Vector2.zero;
            ln.sizeDelta        = new Vector2(0f, h);
            var img = ln.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        static void AddSideStrip(RectTransform parent, Color color, bool left, float w)
        {
            var ln = MakeRect(parent, left ? "GL" : "GR",
                left ? Vector2.zero          : new Vector2(1f, 0f),
                left ? new Vector2(0f, 1f)   : Vector2.one,
                left ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f));
            ln.anchoredPosition = Vector2.zero;
            ln.sizeDelta        = new Vector2(w, 0f);
            var img = ln.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        static void AddPointerEvent(EventTrigger et, EventTriggerType type,
            UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            et.triggers.Add(entry);
        }
    }
}
