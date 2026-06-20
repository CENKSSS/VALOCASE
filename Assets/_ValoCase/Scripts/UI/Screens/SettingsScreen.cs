using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Audio;
using ValoCase.Core;
using ValoCase.Haptics;
using ValoCase.Profile;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Settings screen — audio toggles, save/reset, and embedded Profile section.
    ///
    /// The Profile section (right panel) is built programmatically once on first
    /// OnShown() and reads/writes <see cref="PlayerProfileData"/> directly.
    /// It does NOT depend on CaseBattle being loaded.
    /// </summary>
    public sealed class SettingsScreen : UIScreenBase
    {
        // Settings must appear with zero delay (no fade), e.g. when opened over
        // an active Case Battle. Closing Settings keeps the normal transition.
        public override bool OpensInstantly => true;

        // ── Serialized refs wired by ValoCaseUIBuilder ────────────────────────
        [SerializeField] UINavigator     navigator;
        [SerializeField] Button          backButton;
        [SerializeField] Toggle          sfxToggle;
        [SerializeField] Toggle          musicToggle;
        [SerializeField] Toggle          hapticsToggle;
        [SerializeField] TMP_InputField  playerNameInput;
        [SerializeField] Button          saveProfileButton;
        [SerializeField] Button          resetSaveButton;

        // ── Profile section state ─────────────────────────────────────────────
        bool            _profileBuilt;
        Image           _bigAvatarImg;
        TextMeshProUGUI _agentNameLbl;
        TMP_InputField  _displayNameInput;
        Transform       _gridContent;
        string          _pendingKey;
        Sprite          _pendingSprite;
        Sprite          _circleMaskSprite;

        // ── Save-button dirty state ───────────────────────────────────────────
        bool            _hasUnsavedChanges = true;
        Image           _saveBtnImg;
        Outline         _saveBtnOl;
        TextMeshProUGUI _saveBtnLbl;

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color BgPanel       = new Color(0.022f, 0.015f, 0.060f, 0.97f);
        static readonly Color BgCard        = new Color(0.055f, 0.042f, 0.110f, 1.00f);
        static readonly Color BgInput       = new Color(0.018f, 0.012f, 0.050f, 1.00f);
        static readonly Color AccentPink    = new Color(1.00f, 0.18f, 0.55f, 1.00f);
        static readonly Color AccentPinkSoft= new Color(1.00f, 0.18f, 0.55f, 0.22f);
        static readonly Color AccentPinkGlow= new Color(1.00f, 0.18f, 0.55f, 0.07f);
        static readonly Color TextWhite     = new Color(0.97f, 0.97f, 1.00f, 1.00f);
        static readonly Color TextDim       = new Color(0.48f, 0.45f, 0.62f, 1.00f);
        static readonly Color BorderPink    = new Color(1.00f, 0.18f, 0.55f, 0.55f);

        // ═════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════════

        void Awake()
        {
            if (backButton        != null) backButton.onClick.AddListener(OnBackClicked);
            if (saveProfileButton != null) saveProfileButton.onClick.AddListener(SaveLegacyProfile);
            if (resetSaveButton   != null) resetSaveButton.onClick.AddListener(ResetSave);
            if (sfxToggle     != null) sfxToggle.onValueChanged.AddListener(OnSfxToggle);
            if (musicToggle   != null) musicToggle.onValueChanged.AddListener(OnMusicToggle);
            if (hapticsToggle != null) hapticsToggle.onValueChanged.AddListener(OnHapticsToggle);
        }

        protected override void OnShown()
        {
            var previousScreen = (navigator != null) ? navigator.PreviousScreen : ScreenType.MainMenu;
            Debug.Log("[SETTINGS] Opened from: " + previousScreen);

            // Legacy name input — restore from save data
            if (playerNameInput != null)
            {
                var ctx = GameContext.Instance;
                if (ctx?.Save != null)
                    playerNameInput.text = ctx.Save.Data.playerName;
            }

            ProfileManager.EnsureInitialized();
            BuildProfileSectionOnce();
            RefreshProfileSection();

            // Reset dirty state each time the screen opens — SAVE starts active
            _hasUnsavedChanges = true;
            SetSaveButtonActive(true);

            PlayerProfileData.OnProfileChanged += RefreshProfileSection;
        }

        protected override void OnHidden()
        {
            PlayerProfileData.OnProfileChanged -= RefreshProfileSection;
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEGACY SETTINGS HANDLERS
        // ═════════════════════════════════════════════════════════════════════

        void OnBackClicked()
        {
            var dest = (navigator != null && navigator.PreviousScreen != ScreenType.Settings)
                ? navigator.PreviousScreen
                : ScreenType.MainMenu;
            Debug.Log("[SETTINGS] Exit returning to: " + dest);
            navigator?.Navigate(dest);
        }

        void SaveLegacyProfile()
        {
            if (playerNameInput == null) return;
            var ctx = GameContext.Instance;
            if (ctx?.Save == null) return;
            ctx.Save.Data.playerName = playerNameInput.text;
            ctx.Save.Save();
            GameEvents.RaiseToast("Profile saved.");
        }

        void ResetSave()
        {
            GameContext.Instance?.Save?.ResetSave();
            GameEvents.RaiseToast("Save reset.");
        }

        public void OnSfxToggle(bool on)     { SoundManager.Instance?.SetSfxEnabled(on);  MarkDirty(); }
        public void OnMusicToggle(bool on)   { SoundManager.Instance?.SetMusicEnabled(on); MarkDirty(); }
        public void OnHapticsToggle(bool on) { HapticManager.Instance?.SetEnabled(on);     MarkDirty(); }

        // ═════════════════════════════════════════════════════════════════════
        // PROFILE SECTION — built once, right-anchored panel
        // ═════════════════════════════════════════════════════════════════════

        void BuildProfileSectionOnce()
        {
            if (_profileBuilt) return;
            _profileBuilt = true;

            _circleMaskSprite = MakeCircleSprite(128);

            var rt = (RectTransform)transform;

            // ── Outer panel — fills the already-safe screen with shared content
            // padding only. Navbar space is reserved by the Screens host
            // (ScreenContentFitter); no navbar offset is duplicated here. The panel
            // scrolls internally when its content does not fit.
            const float sidePad = 60f;
            var panel = PR(rt, "ProfilePanel",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            panel.offsetMin = new Vector2(sidePad,  ScreenContentFitter.ContentPadding);
            panel.offsetMax = new Vector2(-sidePad, -ScreenContentFitter.ContentPadding);

            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = BgPanel;

            // Top glow bleed
            var tg = PR(panel, "TGlow",
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f));
            tg.anchoredPosition = Vector2.zero;
            tg.sizeDelta        = new Vector2(0f, 44f);
            tg.gameObject.AddComponent<Image>().color = AccentPinkGlow;

            // ── "PROFILE" section header (36 px) ─────────────────────────────
            var header = PR(panel, "ProfileHeader",
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f));
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta        = new Vector2(0f, 44f);
            header.gameObject.AddComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.10f);

            var hLbl = PT(header, "HLbl", "SETTINGS",
                18f, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            hLbl.characterSpacing = 4f;
            SFull(hLbl.rectTransform);

            var hLine = PR(header, "HLine",
                Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            hLine.anchoredPosition = Vector2.zero;
            hLine.sizeDelta        = new Vector2(0f, 1f);
            hLine.gameObject.AddComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.20f);

            // ── Scrollable content below header ───────────────────────────────
            var scroll = PR(panel, "Scroll",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            scroll.offsetMin = new Vector2(0f, 0f);
            scroll.offsetMax = new Vector2(0f, -44f);

            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.movementType      = ScrollRect.MovementType.Elastic;
            sr.elasticity        = 0.10f;
            sr.inertia           = true;
            sr.decelerationRate  = 0.14f;
            sr.scrollSensitivity = 40f;
            scroll.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            // Viewport
            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scroll, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            sr.viewport = vpRt;

            // Content (VerticalLayoutGroup drives height)
            var cGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            cGo.transform.SetParent(vpRt, false);
            var cRt = (RectTransform)cGo.transform;
            cRt.anchorMin        = new Vector2(0f, 1f);
            cRt.anchorMax        = new Vector2(1f, 1f);
            cRt.pivot            = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta        = Vector2.zero;
            sr.content           = cRt;

            var vlg = cGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.spacing               = 6f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(14, 14, 10, 16);

            cGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // ── Avatar preview block ──────────────────────────────────────────
            BuildAvatarPreviewBlock(cRt.transform);

            // ── Display name block ────────────────────────────────────────────
            BuildDisplayNameBlock(cRt.transform);

            // ── Divider ───────────────────────────────────────────────────────
            BuildDivider(cRt.transform);

            // ── "SELECT AVATAR" label ─────────────────────────────────────────
            var selHint = PT(cRt, "GridHint", "SELECT AVATAR",
                8f, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            selHint.characterSpacing = 3.5f;
            selHint.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            // ── Avatar grid ───────────────────────────────────────────────────
            BuildAvatarGrid(cRt.transform);

            // ── Spacer ────────────────────────────────────────────────────────
            var sp1 = new GameObject("Sp1", typeof(RectTransform), typeof(LayoutElement));
            sp1.transform.SetParent(cRt.transform, false);
            sp1.GetComponent<LayoutElement>().minHeight = 8f;

            // ── Audio section header ──────────────────────────────────────────
            var audioHdr = PT(cRt, "AudioHdr", "AUDIO",
                8f, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            audioHdr.characterSpacing = 3.5f;
            audioHdr.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            BuildDivider(cRt.transform);

            // ── Toggles ───────────────────────────────────────────────────────
            sfxToggle     = BuildSettingsToggle(cRt.transform, "SFX");
            musicToggle   = BuildSettingsToggle(cRt.transform, "Music");
            hapticsToggle = BuildSettingsToggle(cRt.transform, "Haptics");
            sfxToggle.onValueChanged.AddListener(OnSfxToggle);
            musicToggle.onValueChanged.AddListener(OnMusicToggle);
            hapticsToggle.onValueChanged.AddListener(OnHapticsToggle);

            // ── Spacer ────────────────────────────────────────────────────────
            var sp2 = new GameObject("Sp2", typeof(RectTransform), typeof(LayoutElement));
            sp2.transform.SetParent(cRt.transform, false);
            sp2.GetComponent<LayoutElement>().minHeight = 12f;

            // ── Action button row: [SAVE] [ÇIK] ──────────────────────────────
            var btnRow = new GameObject("BtnRow",
                typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            btnRow.transform.SetParent(cRt.transform, false);
            btnRow.GetComponent<LayoutElement>().minHeight = 44f;
            var hlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 8f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;

            saveProfileButton = BuildActionButton(btnRow.transform, "SAVE", AccentPink);
            _saveBtnImg = saveProfileButton.GetComponent<Image>();
            _saveBtnOl  = saveProfileButton.GetComponent<Outline>();
            _saveBtnLbl = saveProfileButton.GetComponentInChildren<TextMeshProUGUI>();
            saveProfileButton.onClick.AddListener(OnProfileSaveClicked);

            var exitBtn = BuildActionButton(btnRow.transform, "ÇIK", TextDim);
            exitBtn.onClick.AddListener(OnBackClicked);

            // ── Bottom spacer ─────────────────────────────────────────────────
            var sp3 = new GameObject("Sp3", typeof(RectTransform), typeof(LayoutElement));
            sp3.transform.SetParent(cRt.transform, false);
            sp3.GetComponent<LayoutElement>().minHeight = 8f;

            // ── Wire legacy input ref so SaveLegacyProfile still works ────────
            playerNameInput = _displayNameInput;
        }

        // ── Audio toggle row (LayoutGroup driven) ─────────────────────────────
        Toggle BuildSettingsToggle(Transform parent, string label)
        {
            var row = new GameObject($"Toggle_{label}",
                typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().minHeight = 32f;

            var toggle = row.GetComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;

            // Checkbox background
            var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(row.transform, false);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin        = new Vector2(0f, 0.5f);
            bgRt.anchorMax        = new Vector2(0f, 0.5f);
            bgRt.pivot            = new Vector2(0f, 0.5f);
            bgRt.anchoredPosition = new Vector2(12f, 0f);
            bgRt.sizeDelta        = new Vector2(36f, 20f);
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color        = BgCard;
            toggle.targetGraphic = bgImg;

            // Checkmark fill
            var ckGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            ckGo.transform.SetParent(bgGo.transform, false);
            var ckRt = (RectTransform)ckGo.transform;
            ckRt.anchorMin = Vector2.zero; ckRt.anchorMax = Vector2.one;
            ckRt.offsetMin = new Vector2(2f, 2f); ckRt.offsetMax = new Vector2(-2f, -2f);
            var ckImg = ckGo.GetComponent<Image>();
            ckImg.color   = AccentPink;
            toggle.graphic = ckImg;

            // Label
            var lbl = PT(row.transform, "Label", label,
                13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, TextWhite);
            var lRt = lbl.rectTransform;
            lRt.anchorMin        = Vector2.zero;
            lRt.anchorMax        = Vector2.one;
            lRt.offsetMin        = new Vector2(58f, 0f);
            lRt.offsetMax        = Vector2.zero;

            toggle.isOn = true;
            return toggle;
        }

        // ── Full-width action button (LayoutGroup driven) ─────────────────────
        Button BuildActionButton(Transform parent, string label, Color tint)
        {
            var btnGo = new GameObject($"Btn_{label}",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(Outline), typeof(LayoutElement));
            btnGo.transform.SetParent(parent, false);
            btnGo.GetComponent<LayoutElement>().minHeight = 44f;
            var btnImg = btnGo.GetComponent<Image>();
            btnImg.color = new Color(0.03f, 0.020f, 0.075f, 0.96f);
            var ol = btnGo.GetComponent<Outline>();
            ol.effectColor    = tint;
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var lbl = PT(btnGo.transform, "Lbl", label,
                13f, FontStyles.Bold, TextAlignmentOptions.Center, tint);
            SFull(lbl.rectTransform);

            var btn = btnGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var et = btnGo.AddComponent<EventTrigger>();
            AddPE(et, EventTriggerType.PointerEnter,
                _ => btnImg.color = new Color(tint.r * 0.15f, tint.g * 0.15f, tint.b * 0.15f, 0.96f));
            AddPE(et, EventTriggerType.PointerExit,
                _ => btnImg.color = new Color(0.03f, 0.020f, 0.075f, 0.96f));

            return btn;
        }

        // ── Avatar preview: ring + circle + agent name ────────────────────────
        void BuildAvatarPreviewBlock(Transform parent)
        {
            var block = new GameObject("AvPreviewBlock", typeof(RectTransform),
                typeof(LayoutElement));
            block.transform.SetParent(parent, false);
            block.GetComponent<LayoutElement>().minHeight = 104f;

            const float bigAv = 76f;

            // Neon ring
            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(block.transform, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.anchorMin        = new Vector2(0.5f, 0.5f);
            ringRt.anchorMax        = new Vector2(0.5f, 0.5f);
            ringRt.pivot            = new Vector2(0.5f, 0.5f);
            ringRt.anchoredPosition = new Vector2(0f, 8f);
            ringRt.sizeDelta        = new Vector2(bigAv + 10f, bigAv + 10f);
            var rImg = ringGo.GetComponent<Image>();
            rImg.sprite        = _circleMaskSprite;
            rImg.type          = Image.Type.Simple;
            rImg.color         = AccentPinkSoft;
            rImg.raycastTarget = false;
            var rOl = ringGo.AddComponent<Outline>();
            rOl.effectColor    = AccentPink;
            rOl.effectDistance = new Vector2(1.5f, -1.5f);

            // Circular avatar
            _bigAvatarImg = BuildCircleMaskedImage(ringRt, "BigAv", bigAv);
            var bigMaskRt = (RectTransform)_bigAvatarImg.transform.parent;
            bigMaskRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bigMaskRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bigMaskRt.pivot            = new Vector2(0.5f, 0.5f);
            bigMaskRt.anchoredPosition = Vector2.zero;
            bigMaskRt.sizeDelta        = new Vector2(bigAv, bigAv);

            // Agent name below ring
            _agentNameLbl = PT(block.transform, "AgentName", "—",
                11f, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            _agentNameLbl.rectTransform.anchorMin        = new Vector2(0f, 0f);
            _agentNameLbl.rectTransform.anchorMax        = new Vector2(1f, 0f);
            _agentNameLbl.rectTransform.pivot            = new Vector2(0.5f, 0f);
            _agentNameLbl.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            _agentNameLbl.rectTransform.sizeDelta        = new Vector2(0f, 18f);
            _agentNameLbl.enableWordWrapping             = false;
        }

        // ── Display name input + save button ──────────────────────────────────
        void BuildDisplayNameBlock(Transform parent)
        {
            // Küçük üst boşluk
            var topSp = new GameObject("DisplayNameTopSp", typeof(RectTransform), typeof(LayoutElement));
            topSp.transform.SetParent(parent, false);
            topSp.GetComponent<LayoutElement>().minHeight = 2f;

            // "DISPLAY NAME" label
            var hint = PT(parent, "DNHint", "DISPLAY NAME",
                8f, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            hint.characterSpacing = 3f;
            hint.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            // Input wrapper
            var iwGo = new GameObject("InputWrap",
                typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            iwGo.transform.SetParent(parent, false);
            iwGo.GetComponent<LayoutElement>().minHeight = 42f;
            iwGo.GetComponent<Image>().color = BgInput;

            var iwOl = iwGo.GetComponent<Outline>();
            iwOl.effectColor    = new Color(1f, 0.18f, 0.55f, 0.40f);
            iwOl.effectDistance = new Vector2(1f, -1f);

            _displayNameInput = BuildInputField(
                (RectTransform)iwGo.transform, ResolveDisplayNameForUi());

            _displayNameInput.onValueChanged.AddListener(_ => MarkDirty());
        }

        // ── Separator ─────────────────────────────────────────────────────────
        void BuildDivider(Transform parent)
        {
            var div = new GameObject("Div",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            div.transform.SetParent(parent, false);
            div.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.10f);
            div.GetComponent<LayoutElement>().minHeight = 1f;
        }

        // ── Scrollable avatar grid ────────────────────────────────────────────
        void BuildAvatarGrid(Transform parent)
        {
            var gridGo = new GameObject("AvatarGrid",
                typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(ContentSizeFitter), typeof(LayoutElement));
            gridGo.transform.SetParent(parent, false);

            var glg = gridGo.GetComponent<GridLayoutGroup>();
            glg.cellSize        = new Vector2(78f, 98f);
            glg.spacing         = new Vector2(8f, 8f);
            glg.padding         = new RectOffset(4, 4, 4, 4);
            glg.childAlignment  = TextAnchor.UpperLeft;
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 4;

            gridGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var le = gridGo.GetComponent<LayoutElement>();
            le.minHeight = 105f;

            _gridContent = gridGo.transform;
        }

        void PopulateGrid()
        {
            if (_gridContent == null) return;
            for (int i = _gridContent.childCount - 1; i >= 0; i--)
                Destroy(_gridContent.GetChild(i).gameObject);

            var avatars = ProfileManager.Avatars;
            if (avatars == null || avatars.Count == 0)
            {
                var msg = PT(_gridContent, "NoCards",
                    "Place images in:\nAssets/_ValoCase/Art/Avatars/",
                    9f, FontStyles.Normal, TextAlignmentOptions.Center, TextDim);
                msg.enableWordWrapping = true;
                return;
            }

            foreach (var (name, sprite) in avatars)
                BuildCell(name, sprite);
        }

        void BuildCell(string agentName, Sprite sprite)
        {
            bool sel = agentName == (_pendingKey ?? PlayerProfileData.AvatarKey);

            var cell    = PR(_gridContent, $"AV_{agentName}",
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            var cellImg = cell.gameObject.AddComponent<Image>();
            cellImg.color = BgCard;

            var ol = cell.gameObject.AddComponent<Outline>();
            ol.effectColor    = sel ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.07f);
            ol.effectDistance = new Vector2(sel ? 2.5f : 0.6f, -(sel ? 2.5f : 0.6f));

            // Hover
            var cellEt = cell.gameObject.AddComponent<EventTrigger>();
            string cn = agentName; Outline col = ol;
            AddPE(cellEt, EventTriggerType.PointerEnter, _ => OnCellEnter(cn, col));
            AddPE(cellEt, EventTriggerType.PointerExit,  _ => OnCellExit(cn, col));

            // Circular avatar (top-centre, 68 px)
            var avImg  = BuildCircleMaskedImage(cell, "Av", 68f);
            var avMask = (RectTransform)avImg.transform.parent;
            avMask.anchorMin        = new Vector2(0.5f, 1f);
            avMask.anchorMax        = new Vector2(0.5f, 1f);
            avMask.pivot            = new Vector2(0.5f, 1f);
            avMask.anchoredPosition = new Vector2(0f, -8f);
            avMask.sizeDelta        = new Vector2(68f, 68f);
            avImg.sprite            = sprite;

            // Name label
            var nLbl = PT(cell, "Name", agentName,
                8f, FontStyles.Bold, TextAlignmentOptions.Center,
                sel ? AccentPink : TextDim);
            var nRt = nLbl.rectTransform;
            nRt.anchorMin        = new Vector2(0f, 0f);
            nRt.anchorMax        = new Vector2(1f, 0f);
            nRt.pivot            = new Vector2(0.5f, 0f);
            nRt.anchoredPosition = new Vector2(0f, 7f);
            nRt.sizeDelta        = new Vector2(0f, 14f);
            nLbl.enableWordWrapping = false;
            nLbl.overflowMode       = TextOverflowModes.Ellipsis;

            // Button
            string capN = agentName; Sprite capSp = sprite;
            var btn = cell.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => OnCellClicked(capN, capSp));
        }

        // ── Grid interaction ──────────────────────────────────────────────────

        void OnCellEnter(string key, Outline ol)
        {
            if (key == _pendingKey) return;
            ol.effectColor    = new Color(1f, 0.18f, 0.55f, 0.38f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
        }

        void OnCellExit(string key, Outline ol)
        {
            bool sel = key == _pendingKey;
            ol.effectColor    = sel ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.07f);
            ol.effectDistance = new Vector2(sel ? 2.5f : 0.6f, -(sel ? 2.5f : 0.6f));
        }

        void OnCellClicked(string name, Sprite sprite)
        {
            _pendingKey    = name;
            _pendingSprite = sprite;

            Debug.Log("[SETTINGS_AVATAR] selected key=" + _pendingKey + " spriteNull=" + (_pendingSprite == null));

            if (_bigAvatarImg  != null) { _bigAvatarImg.color = Color.white; _bigAvatarImg.sprite = sprite; }
            if (_agentNameLbl  != null) _agentNameLbl.text = name;

            RefreshGridHighlights();
            MarkDirty();
        }

        void RefreshGridHighlights()
        {
            if (_gridContent == null) return;
            foreach (Transform child in _gridContent)
            {
                bool sel = child.name == $"AV_{_pendingKey}";
                var ol = child.GetComponent<Outline>();
                if (ol != null)
                {
                    ol.effectColor    = sel ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.07f);
                    ol.effectDistance = new Vector2(sel ? 2.5f : 0.6f, -(sel ? 2.5f : 0.6f));
                }
                var lbl = child.Find("Name")?.GetComponent<TextMeshProUGUI>();
                if (lbl != null) lbl.color = sel ? AccentPink : TextDim;
            }
        }

        // ── Save clicked ──────────────────────────────────────────────────────
        void OnProfileSaveClicked()
        {
            // Capture pending values into locals FIRST.
            // SetUsername fires OnProfileChanged → RefreshProfileSection() which
            // overwrites _pendingKey/_pendingSprite with the old saved data.
            // Using locals ensures SetAvatar always receives the user's selection.
            var rawName   = (_displayNameInput != null) ? _displayNameInput.text : null;
            var newKey    = _pendingKey;
            var newSprite = _pendingSprite;

            Debug.Log("[SETTINGS_AVATAR] saving key=" + newKey + " spriteNull=" + (newSprite == null));

            // Validate locally before any backend request — invalid names never leave the client.
            if (!TryValidateNickname(rawName, out var newName, out var validationError))
            {
                GameEvents.RaiseToast(validationError);
                SetSaveButtonActive(true);
                return;
            }

            var ctx = GameContext.Instance;

            // Both nickname and avatar are server-authoritative: save the name, then the
            // avatar, and update the local cache only on success. Never persist either
            // locally first, so other players always see what the backend stored.
            if (ctx != null && ctx.BackendEnabled)
            {
                SetSaveButtonActive(false);
                ctx.SaveDisplayNameBackend(newName,
                    nameSaved => SaveAvatarThenFinish(ctx, newKey, newSprite),
                    err =>
                    {
                        GameEvents.RaiseToast(string.IsNullOrEmpty(err) ? "İsim kaydedilemedi." : err);
                        SetSaveButtonActive(true);   // allow retry
                    });
                return;
            }

            // Local fallback (no backend available, e.g. offline editor session).
            if (newSprite != null) PlayerProfileData.SetAvatar(newSprite, newKey);
            PlayerProfileData.SetUsername(newName);

            GameEvents.RaiseToast("Profile saved.");

            _hasUnsavedChanges = false;
            SetSaveButtonActive(false);
            Debug.Log("[SETTINGS] Saved changes — save button disabled");
        }

        // Persists the chosen avatar to the backend after the name save succeeded, then
        // adopts it into the local cache. With no avatar selected there is nothing to
        // push, so the profile save is already complete.
        void SaveAvatarThenFinish(GameContext ctx, string avatarKey, Sprite avatarSprite)
        {
            if (string.IsNullOrEmpty(avatarKey) || avatarSprite == null)
            {
                FinishProfileSaved();
                return;
            }

            ctx.SaveAvatarBackend(avatarKey,
                saved =>
                {
                    PlayerProfileData.SetAvatar(avatarSprite, avatarKey);
                    FinishProfileSaved();
                },
                err =>
                {
                    GameEvents.RaiseToast(string.IsNullOrEmpty(err) ? "Avatar kaydedilemedi." : err);
                    SetSaveButtonActive(true);   // name saved; allow retry for the avatar
                });
        }

        void FinishProfileSaved()
        {
            GameEvents.RaiseToast("Profil kaydedildi.");
            _hasUnsavedChanges = false;
            SetSaveButtonActive(false);
            Debug.Log("[SETTINGS] Profile saved to backend — save button disabled");
        }

        // Mirrors the backend rules: 3–20 chars, English letters/digits/underscore only.
        static bool TryValidateNickname(string raw, out string trimmed, out string error)
        {
            trimmed = (raw ?? string.Empty).Trim();
            error = null;

            if (string.IsNullOrEmpty(trimmed)) { error = "İsim boş bırakılamaz."; return false; }
            if (trimmed.Length < 3)            { error = "İsim en az 3 karakter olmalı."; return false; }
            if (trimmed.Length > 20)           { error = "İsim en fazla 20 karakter olmalı."; return false; }

            foreach (var c in trimmed)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                          (c >= '0' && c <= '9') || c == '_';
                if (!ok) { error = "Sadece harf, rakam ve _ kullanabilirsin."; return false; }
            }
            return true;
        }

        // New players have no saved nickname yet — show the backend-style AgentXXXX
        // default (derived from the guest account id) instead of the bare "Agent".
        static string ResolveDisplayNameForUi()
        {
            var name = PlayerProfileData.Username;
            if (!string.IsNullOrWhiteSpace(name) && name != "Agent") return name;

            var accountId = GameContext.Instance?.Save?.Data?.guestAccountId;
            if (string.IsNullOrEmpty(accountId)) return name;
            return "Agent" + accountId.Substring(0, Mathf.Min(4, accountId.Length));
        }

        // ── Dirty-state helpers ───────────────────────────────────────────────
        void MarkDirty()
        {
            if (_hasUnsavedChanges) return;
            _hasUnsavedChanges = true;
            SetSaveButtonActive(true);
        }

        void SetSaveButtonActive(bool active)
        {
            if (saveProfileButton == null) return;
            saveProfileButton.interactable = active;
            Color labelColor = active
                ? AccentPink
                : new Color(AccentPink.r * 0.35f, AccentPink.g * 0.35f, AccentPink.b * 0.35f, 0.50f);
            Color bgColor = active
                ? new Color(0.03f, 0.020f, 0.075f, 0.96f)
                : new Color(0.02f, 0.015f, 0.050f, 0.55f);
            if (_saveBtnImg != null) _saveBtnImg.color         = bgColor;
            if (_saveBtnOl  != null) _saveBtnOl.effectColor    = labelColor;
            if (_saveBtnLbl != null) _saveBtnLbl.color         = labelColor;
        }

        // ── Sync profile → UI ─────────────────────────────────────────────────
        void RefreshProfileSection()
        {
            if (!_profileBuilt) return;

            _pendingKey    = PlayerProfileData.AvatarKey;
            _pendingSprite = PlayerProfileData.Avatar;

            if (_bigAvatarImg != null)
            {
                _bigAvatarImg.color  = Color.white;
                _bigAvatarImg.sprite = PlayerProfileData.Avatar;
            }
            if (_agentNameLbl     != null) _agentNameLbl.text     = PlayerProfileData.AvatarKey;
            if (_displayNameInput != null) _displayNameInput.text = ResolveDisplayNameForUi();

            // Grid might not have been populated yet
            if (_gridContent != null && _gridContent.childCount == 0)
                PopulateGrid();

            RefreshGridHighlights();
        }

        // ═════════════════════════════════════════════════════════════════════
        // UI BUILDER HELPERS (self-contained so SettingsScreen has no deps)
        // ═════════════════════════════════════════════════════════════════════

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
            img.color          = Color.white;   // ← PURE WHITE: no tint on avatar sprites
            img.preserveAspect = true;
            return img;
        }

        TMP_InputField BuildInputField(RectTransform parent, string initialText)
        {
            var root = new GameObject("Field",
                typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            SFull((RectTransform)root.transform);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var field = root.GetComponent<TMP_InputField>();

            var taGo = new GameObject("TextArea",
                typeof(RectTransform), typeof(RectMask2D));
            taGo.transform.SetParent(root.transform, false);
            var taRt = (RectTransform)taGo.transform;
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(12f, 6f); taRt.offsetMax = new Vector2(-12f, -6f);

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(taGo.transform, false);
            SFull((RectTransform)phGo.transform);
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
            SFull((RectTransform)itGo.transform);
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

        // ── Tiny layout primitives ────────────────────────────────────────────

        static RectTransform PR(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            return rt;
        }

        static TextMeshProUGUI PT(Transform parent, string name, string text,
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

        static void SFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void AddPE(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(action);
            et.triggers.Add(e);
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
    }
}
