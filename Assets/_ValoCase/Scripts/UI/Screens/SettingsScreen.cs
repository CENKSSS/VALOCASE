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

            PlayerProfileData.OnProfileChanged += RefreshProfileSection;
        }

        protected override void OnHidden()
        {
            PlayerProfileData.OnProfileChanged -= RefreshProfileSection;
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEGACY SETTINGS HANDLERS
        // ═════════════════════════════════════════════════════════════════════

        void OnBackClicked() => navigator?.Navigate(ScreenType.MainMenu);

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

        public void OnSfxToggle(bool on)     => SoundManager.Instance?.SetSfxEnabled(on);
        public void OnMusicToggle(bool on)   => SoundManager.Instance?.SetMusicEnabled(on);
        public void OnHapticsToggle(bool on) => HapticManager.Instance?.SetEnabled(on);

        // ═════════════════════════════════════════════════════════════════════
        // PROFILE SECTION — built once, right-anchored panel
        // ═════════════════════════════════════════════════════════════════════

        void BuildProfileSectionOnce()
        {
            if (_profileBuilt) return;
            _profileBuilt = true;

            _circleMaskSprite = MakeCircleSprite(128);

            var rt = (RectTransform)transform;

            // ── Outer panel — right edge, full height, 420 px wide ────────────
            var panel = PR(rt, "ProfilePanel",
                new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f));
            panel.anchoredPosition = new Vector2(-10f, 0f);
            panel.sizeDelta        = new Vector2(420f, 0f);

            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = BgPanel;

            // Left border line
            var lBorder = PR(panel, "LBorder",
                Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            lBorder.anchoredPosition = Vector2.zero;
            lBorder.sizeDelta        = new Vector2(2f, 0f);
            lBorder.gameObject.AddComponent<Image>().color = AccentPink;

            // Top glow bleed
            var tg = PR(panel, "TGlow",
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f));
            tg.anchoredPosition = Vector2.zero;
            tg.sizeDelta        = new Vector2(0f, 90f);
            tg.gameObject.AddComponent<Image>().color = AccentPinkGlow;

            // ── "PROFILE" section header (36 px) ─────────────────────────────
            var header = PR(panel, "ProfileHeader",
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f));
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta        = new Vector2(0f, 36f);
            header.gameObject.AddComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.06f);

            var hLbl = PT(header, "HLbl", "  PROFILE",
                9f, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
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
            scroll.offsetMax = new Vector2(0f, -36f);

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
            vlg.spacing               = 0f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(14, 14, 16, 16);

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
            selHint.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            // ── Avatar grid ───────────────────────────────────────────────────
            BuildAvatarGrid(cRt.transform);
        }

        // ── Avatar preview: ring + circle + agent name ────────────────────────
        void BuildAvatarPreviewBlock(Transform parent)
        {
            var block = new GameObject("AvPreviewBlock", typeof(RectTransform),
                typeof(LayoutElement));
            block.transform.SetParent(parent, false);
            block.GetComponent<LayoutElement>().minHeight = 148f;

            const float bigAv = 100f;

            // Neon ring
            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(block.transform, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.anchorMin        = new Vector2(0.5f, 0.5f);
            ringRt.anchorMax        = new Vector2(0.5f, 0.5f);
            ringRt.pivot            = new Vector2(0.5f, 0.5f);
            ringRt.anchoredPosition = Vector2.zero;
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
            _agentNameLbl.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            _agentNameLbl.rectTransform.sizeDelta        = new Vector2(0f, 18f);
            _agentNameLbl.enableWordWrapping             = false;
        }

        // ── Display name input + save button ──────────────────────────────────
        void BuildDisplayNameBlock(Transform parent)
        {
            // "DISPLAY NAME" label
            var hint = PT(parent, "DNHint", "DISPLAY NAME",
                8f, FontStyles.Bold, TextAlignmentOptions.Left, AccentPink);
            hint.characterSpacing = 3f;
            hint.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            // Input wrapper
            var iwGo = new GameObject("InputWrap",
                typeof(RectTransform), typeof(Image), typeof(Outline), typeof(LayoutElement));
            iwGo.transform.SetParent(parent, false);
            iwGo.GetComponent<LayoutElement>().minHeight = 48f;
            iwGo.GetComponent<Image>().color = BgInput;
            var iwOl = iwGo.GetComponent<Outline>();
            iwOl.effectColor    = new Color(1f, 0.18f, 0.55f, 0.40f);
            iwOl.effectDistance = new Vector2(1f, -1f);

            _displayNameInput = BuildInputField(
                (RectTransform)iwGo.transform, PlayerProfileData.Username);

            // SAVE button
            var saveGo = new GameObject("SaveBtn",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(Outline), typeof(LayoutElement));
            saveGo.transform.SetParent(parent, false);
            saveGo.GetComponent<LayoutElement>().minHeight = 46f;
            saveGo.GetComponent<Image>().color = new Color(0.03f, 0.020f, 0.075f, 0.96f);
            var saveOl = saveGo.GetComponent<Outline>();
            saveOl.effectColor    = AccentPink;
            saveOl.effectDistance = new Vector2(1.5f, -1.5f);

            var saveLbl = PT(saveGo.transform, "Lbl", "SAVE  ✓",
                12f, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            SFull(saveLbl.rectTransform);

            var saveBtn = saveGo.GetComponent<Button>();
            saveBtn.transition = Selectable.Transition.None;
            saveBtn.onClick.AddListener(OnProfileSaveClicked);

            // Hover tint
            var saveBtnImg = saveGo.GetComponent<Image>();
            var et = saveGo.AddComponent<EventTrigger>();
            AddPE(et, EventTriggerType.PointerEnter,
                _ => saveBtnImg.color = new Color(1f, 0.18f, 0.55f, 0.12f));
            AddPE(et, EventTriggerType.PointerExit,
                _ => saveBtnImg.color = new Color(0.03f, 0.020f, 0.075f, 0.96f));

            // Top spacing
            var topSp = new GameObject("TopSp", typeof(RectTransform), typeof(LayoutElement));
            topSp.transform.SetParent(parent, false);
            topSp.GetComponent<LayoutElement>().minHeight = 8f;
            topSp.transform.SetSiblingIndex(0);   // before hint
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
            glg.cellSize        = new Vector2(84f, 108f);
            glg.spacing         = new Vector2(8f, 8f);
            glg.padding         = new RectOffset(4, 4, 6, 6);
            glg.childAlignment  = TextAnchor.UpperLeft;
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 4;

            gridGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var le = gridGo.GetComponent<LayoutElement>();
            le.minHeight = 120f;

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
                    "Place images in:\nDesktop/ValorantProject/FaceCards/",
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

            if (_bigAvatarImg  != null) { _bigAvatarImg.color = Color.white; _bigAvatarImg.sprite = sprite; }
            if (_agentNameLbl  != null) _agentNameLbl.text = name;

            RefreshGridHighlights();
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
            if (_displayNameInput != null &&
                !string.IsNullOrWhiteSpace(_displayNameInput.text))
                PlayerProfileData.SetUsername(_displayNameInput.text.Trim());

            if (_pendingSprite != null)
                PlayerProfileData.SetAvatar(_pendingSprite, _pendingKey);

            GameEvents.RaiseToast("Profile saved.");
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
            if (_displayNameInput != null) _displayNameInput.text = PlayerProfileData.Username;

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
