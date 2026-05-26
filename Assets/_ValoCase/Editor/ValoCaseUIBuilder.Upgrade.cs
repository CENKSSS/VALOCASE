#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.UI;
using ValoCase.UI.Screens;

namespace ValoCase.Editor
{
    // UpgradeScreen layout builder — iki yan panel, center spin, VP filter butonlari,
    // tab bar ve scroll grid'i bir arada kurar.
    public static partial class ValoCaseUIBuilder
    {
        // ── Upgrade / Gamble screen ───────────────────────────────────────────
        // CS2 / Hellcase dark casino style:
        //   TOP (48 %): [LEFT large skin display] [CENTER spin+btn] [RIGHT large skin display]
        //   BOTTOM (52 %): tab bar → [ENVANTER | TÜM SKİNLER] → card grid
        //                  filter bar (weapon + rarity dropdowns) visible in TÜM SKİNLER mode only
        static RectTransform BuildUpgradeScreen(RectTransform parent, UINavigator navigator, WeaponSkinCardView cardPrefab)
        {
            var screen = CreateScreenPanel(parent, "UpgradeScreen", ScreenType.Upgrade, out var group);
            var comp   = screen.gameObject.AddComponent<UpgradeScreen>();

            const float headerH  = 64f;
            const float topFrac  = 0.48f;   // top 48 % = three-panel display area

            // ── Header ────────────────────────────────────────────────────────
            var header = CreateRect("Header", screen, Vector2.zero);
            header.anchorMin = new Vector2(0, 1 - topFrac);
            header.anchorMax = new Vector2(1, 1);
            header.offsetMin = Vector2.zero;
            header.offsetMax = new Vector2(0, 0);
            // Actual header strip
            var hBar = CreateRect("HBar", screen, Vector2.zero);
            hBar.anchorMin = new Vector2(0, 1);
            hBar.anchorMax = new Vector2(1, 1);
            hBar.pivot     = new Vector2(0.5f, 1);
            hBar.sizeDelta = new Vector2(0, headerH);
            hBar.anchoredPosition = Vector2.zero;
            hBar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var back = CreateMenuButton(hBar, "Back", "← GERİ", AccentRed, Vector2.zero, new Vector2(150, 48));
            var backRt = back.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0, 0.5f); backRt.anchorMax = new Vector2(0, 0.5f);
            backRt.pivot     = new Vector2(0, 0.5f);
            backRt.anchoredPosition = new Vector2(10, 0);

            var title = CreateTmp("Title", hBar, "YÜKSELT", 26, TextAlignmentOptions.Center);
            ApplyTitleGlow(title, NeonPurple);
            title.rectTransform.anchorMin = new Vector2(0, 0); title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.offsetMin = new Vector2(170, 0); title.rectTransform.offsetMax = new Vector2(-170, 0);

            var wallet = CreateTmp("Wallet", hBar, "0 VP", 19, TextAlignmentOptions.Right);
            wallet.color = new Color(0.6f, 1f, 0.6f);
            wallet.rectTransform.anchorMin = new Vector2(1, 0.5f); wallet.rectTransform.anchorMax = new Vector2(1, 0.5f);
            wallet.rectTransform.pivot     = new Vector2(1, 0.5f);
            wallet.rectTransform.anchoredPosition = new Vector2(-12, 0);
            wallet.rectTransform.sizeDelta = new Vector2(200, 36);

            // ── Top section (three panels) ────────────────────────────────────
            var topSection = CreateRect("TopSection", screen, Vector2.zero);
            topSection.anchorMin = new Vector2(0, 1 - topFrac);
            topSection.anchorMax = new Vector2(1, 1);
            topSection.offsetMin = new Vector2(0, 0);
            topSection.offsetMax = new Vector2(0, -headerH);
            topSection.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            // ── Left panel — INPUT skin display ───────────────────────────────
            Image inputIcon; TextMeshProUGUI inputNameLbl, inputRarityLbl, inputVpLbl, inputChanceLbl;
            Image inputRarityStrip; GameObject inputPlaceholder;
            BuildSkinDisplayPanel("LeftPanel", topSection, 0f, 0.33f, NeonCyan, "SEÇİLİ SKİN", "ENVANTER'den skin seç",
                out inputIcon, out inputNameLbl, out inputRarityLbl, out inputVpLbl,
                out inputChanceLbl, out inputRarityStrip, out inputPlaceholder);

            // ── Right panel — TARGET skin display ─────────────────────────────
            Image targetIcon; TextMeshProUGUI targetNameLbl, targetRarityLbl, targetVpLbl, targetChanceLbl;
            Image targetRarityStrip; GameObject targetPlaceholder;
            BuildSkinDisplayPanel("RightPanel", topSection, 0.67f, 1f, NeonPink, "HEDEF SKİN", "TÜM SKİNLER'den seç",
                out targetIcon, out targetNameLbl, out targetRarityLbl, out targetVpLbl,
                out targetChanceLbl, out targetRarityStrip, out targetPlaceholder);

            // ── Center panel — spin wheel + upgrade button ────────────────────
            var centerPanel = CreateRect("CenterPanel", topSection, Vector2.zero);
            centerPanel.anchorMin = new Vector2(0.33f, 0); centerPanel.anchorMax = new Vector2(0.67f, 1);
            centerPanel.offsetMin = new Vector2(4, 8); centerPanel.offsetMax = new Vector2(-4, -8);
            centerPanel.GetComponent<Image>().color = new Color(0.05f, 0.03f, 0.10f, 0.60f);
            AddNeonGlow(centerPanel.gameObject, NeonPurple, 1.2f);

            // Chance % label (inside ring hole)
            var chanceLabel = CreateTmp("Chance", centerPanel, "--%", 48, TextAlignmentOptions.Center);
            chanceLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            chanceLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            chanceLabel.rectTransform.pivot     = new Vector2(0.5f, 0.5f);
            chanceLabel.rectTransform.anchoredPosition = new Vector2(0, 28);
            chanceLabel.rectTransform.sizeDelta = new Vector2(220, 70);
            chanceLabel.fontStyle = TMPro.FontStyles.Bold;
            ApplyTitleGlow(chanceLabel, NeonGreen);

            var chanceHint = CreateTmp("ChanceHint", centerPanel, "ENVANTERden skin seç", 12, TextAlignmentOptions.Center);
            chanceHint.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            chanceHint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            chanceHint.rectTransform.pivot     = new Vector2(0.5f, 0.5f);
            chanceHint.rectTransform.anchoredPosition = new Vector2(0, -14);
            chanceHint.rectTransform.sizeDelta = new Vector2(240, 22);
            chanceHint.color = new Color(0.65f, 0.72f, 0.85f);

            // UPGRADE button (bottom of center panel)
            var upgradeBtn = CreateMenuButton(centerPanel, "UpgradeBtn", "YÜKSELT", NeonPurple,
                Vector2.zero, new Vector2(220, 58));
            var upgBtnRt = upgradeBtn.GetComponent<RectTransform>();
            upgBtnRt.anchorMin = new Vector2(0.5f, 0); upgBtnRt.anchorMax = new Vector2(0.5f, 0);
            upgBtnRt.pivot     = new Vector2(0.5f, 0);
            upgBtnRt.anchoredPosition = new Vector2(0, 14);
            var upgBtnLabel = upgradeBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (upgBtnLabel != null) { upgBtnLabel.fontSize = 20; upgBtnLabel.fontStyle = TMPro.FontStyles.Bold; }

            // ── VP hızlı filtre butonları (upgrade butonunun altında) ─────────
            var vpBar = CreateRect("VpFilterBar", centerPanel, Vector2.zero);
            vpBar.anchorMin = new Vector2(0, 0); vpBar.anchorMax = new Vector2(1, 0);
            vpBar.pivot     = new Vector2(0.5f, 0);
            vpBar.sizeDelta = new Vector2(-8, 30);
            vpBar.anchoredPosition = new Vector2(0, 80);
            vpBar.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var vpBtn1000 = BuildVpFilterButton(vpBar, "+1000 VP", 0f,    0.33f);
            var vpBtn2000 = BuildVpFilterButton(vpBar, "+2000 VP", 0.33f, 0.67f);
            var vpBtn3000 = BuildVpFilterButton(vpBar, "+3000 VP", 0.67f, 1f);

            // ── Bottom section (tabs + grid) ──────────────────────────────────
            var bottomSection = CreateRect("BottomSection", screen, Vector2.zero);
            bottomSection.anchorMin = new Vector2(0, 0);
            bottomSection.anchorMax = new Vector2(1, 1 - topFrac);
            bottomSection.offsetMin = Vector2.zero; bottomSection.offsetMax = Vector2.zero;
            bottomSection.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.08f, 0.95f);

            // Tab bar (56 px tall, anchored top)
            var tabBar = CreateRect("TabBar", bottomSection, Vector2.zero);
            tabBar.anchorMin = new Vector2(0, 1); tabBar.anchorMax = new Vector2(1, 1);
            tabBar.pivot     = new Vector2(0.5f, 1);
            tabBar.sizeDelta = new Vector2(0, 56);
            tabBar.anchoredPosition = Vector2.zero;
            tabBar.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.14f, 1f);

            Image invTabLine, allTabLine;
            var invTabBtn = BuildTab(tabBar, "ENVANTER", 0f, 0.5f, NeonCyan,  out invTabLine);
            var allTabBtn = BuildTab(tabBar, "TÜM SKİNLER", 0.5f, 1f, NeonPurple, out allTabLine);

            // Filter bar (48 px, below tab bar, hidden initially)
            var filterBar = CreateRect("FilterBar", bottomSection, Vector2.zero);
            filterBar.anchorMin = new Vector2(0, 1); filterBar.anchorMax = new Vector2(1, 1);
            filterBar.pivot     = new Vector2(0.5f, 1);
            filterBar.sizeDelta = new Vector2(0, 48);
            filterBar.anchoredPosition = new Vector2(0, -56);
            filterBar.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.11f, 1f);
            // Filtre bar her iki sekmede de görünür — UpgradeScreen yönetir

            // Skin scroll view — fills remainder below tab bar (offsetMax.y managed by UpgradeScreen)
            var skinScrollRoot = CreateRect("SkinScroll", bottomSection, Vector2.zero);
            skinScrollRoot.anchorMin = new Vector2(0, 0); skinScrollRoot.anchorMax = new Vector2(1, 1);
            skinScrollRoot.offsetMin = new Vector2(0, 0); skinScrollRoot.offsetMax = new Vector2(0, -104);
            skinScrollRoot.GetComponent<Image>().color = new Color(0, 0, 0, 0.12f);

            var skinScrollView = skinScrollRoot.gameObject.AddComponent<ScrollRect>();
            skinScrollView.horizontal = false; skinScrollView.vertical = true;
            skinScrollView.movementType = ScrollRect.MovementType.Elastic;
            skinScrollView.elasticity   = 0.12f; skinScrollView.inertia = true;
            skinScrollView.decelerationRate = 0.14f; skinScrollView.scrollSensitivity = 60f;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(skinScrollRoot, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            gridGo.transform.SetParent(vpGo.transform, false);
            var gridRt = gridGo.GetComponent<RectTransform>();
            gridRt.anchorMin = new Vector2(0, 1); gridRt.anchorMax = new Vector2(1, 1);
            gridRt.pivot     = new Vector2(0.5f, 1);
            gridRt.anchoredPosition = Vector2.zero; gridRt.sizeDelta = Vector2.zero;

            var glg = gridGo.GetComponent<GridLayoutGroup>();
            glg.cellSize     = new Vector2(150, 210);
            glg.spacing      = new Vector2(8, 8);
            glg.padding      = new RectOffset(8, 8, 8, 8);
            glg.childAlignment = TextAnchor.UpperLeft;

            var csf = gridGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            skinScrollView.viewport = vpRt;
            skinScrollView.content  = gridRt;

            // ── Result flash overlay ──────────────────────────────────────────
            var flash = CreateRect("ResultFlash", screen, Vector2.zero);
            StretchFull(flash);
            var flashImg = flash.GetComponent<Image>();
            flashImg.color = new Color(0, 0, 0, 0);
            flashImg.raycastTarget = false;
            flash.SetAsLastSibling();

            var resultLbl = CreateTmp("ResultLabel", flash, "", 72, TextAlignmentOptions.Center);
            StretchFull(resultLbl);
            resultLbl.fontStyle = TMPro.FontStyles.Bold;
            resultLbl.raycastTarget = false;
            ApplyTitleGlow(resultLbl, NeonGreen);

            // ── Wire serialized refs ─────────────────────────────────────────
            var so = new SerializedObject(comp);
            so.FindProperty("navigator").objectReferenceValue           = navigator;
            so.FindProperty("backButton").objectReferenceValue          = back;
            so.FindProperty("walletLabel").objectReferenceValue         = wallet;
            so.FindProperty("cardPrefab").objectReferenceValue          = cardPrefab;
            // Left (input) panel
            so.FindProperty("inputIcon").objectReferenceValue           = inputIcon;
            so.FindProperty("inputName").objectReferenceValue           = inputNameLbl;
            so.FindProperty("inputRarityLabel").objectReferenceValue    = inputRarityLbl;
            so.FindProperty("inputVpLabel").objectReferenceValue        = inputVpLbl;
            so.FindProperty("inputChanceLabel").objectReferenceValue    = inputChanceLbl;
            so.FindProperty("inputRarityStrip").objectReferenceValue    = inputRarityStrip;
            so.FindProperty("inputPlaceholder").objectReferenceValue    = inputPlaceholder;
            // Right (target) panel
            so.FindProperty("targetIcon").objectReferenceValue          = targetIcon;
            so.FindProperty("targetName").objectReferenceValue          = targetNameLbl;
            so.FindProperty("targetRarityLabel").objectReferenceValue   = targetRarityLbl;
            so.FindProperty("targetVpLabel").objectReferenceValue       = targetVpLbl;
            so.FindProperty("targetChanceLabel").objectReferenceValue   = targetChanceLbl;
            so.FindProperty("targetRarityStrip").objectReferenceValue   = targetRarityStrip;
            so.FindProperty("targetPlaceholder").objectReferenceValue   = targetPlaceholder;
            // Center
            so.FindProperty("spinCenter").objectReferenceValue          = centerPanel;
            so.FindProperty("chanceLabel").objectReferenceValue         = chanceLabel;
            so.FindProperty("chanceHint").objectReferenceValue          = chanceHint;
            so.FindProperty("upgradeButton").objectReferenceValue       = upgradeBtn;
            so.FindProperty("upgradeButtonLabel").objectReferenceValue  = upgBtnLabel;
            so.FindProperty("vpBtn1000").objectReferenceValue           = vpBtn1000;
            so.FindProperty("vpBtn2000").objectReferenceValue           = vpBtn2000;
            so.FindProperty("vpBtn3000").objectReferenceValue           = vpBtn3000;
            // Bottom
            so.FindProperty("inventoryTabBtn").objectReferenceValue     = invTabBtn;
            so.FindProperty("allSkinsTabBtn").objectReferenceValue      = allTabBtn;
            so.FindProperty("inventoryTabLine").objectReferenceValue    = invTabLine;
            so.FindProperty("allSkinsTabLine").objectReferenceValue     = allTabLine;
            so.FindProperty("skinGridRoot").objectReferenceValue        = gridRt.transform;
            so.FindProperty("skinScrollRt").objectReferenceValue        = skinScrollRoot;
            so.FindProperty("filterBar").objectReferenceValue           = filterBar;
            // Result
            so.FindProperty("resultFlash").objectReferenceValue         = flashImg;
            so.FindProperty("resultLabel").objectReferenceValue         = resultLbl;
            so.FindProperty("canvasGroup").objectReferenceValue         = group;
            so.FindProperty("screenType").enumValueIndex                = (int)ScreenType.Upgrade;
            so.ApplyModifiedPropertiesWithoutUndo();

            return screen;
        }

        /// <summary>
        /// Builds a premium skin display panel (large icon + name/rarity/VP/chance labels).
        /// </summary>
        static void BuildSkinDisplayPanel(
            string name, RectTransform parent,
            float xMin, float xMax,
            Color accentColor, string headerText, string placeholderText,
            out Image      iconImg,
            out TextMeshProUGUI nameLabel,
            out TextMeshProUGUI rarityLabel,
            out TextMeshProUGUI vpLabel,
            out TextMeshProUGUI chanceLabel,
            out Image      rarityStrip,
            out GameObject placeholder)
        {
            var panel = CreateRect(name, parent, Vector2.zero);
            panel.anchorMin = new Vector2(xMin, 0); panel.anchorMax = new Vector2(xMax, 1);
            panel.offsetMin = new Vector2(4, 8); panel.offsetMax = new Vector2(-4, -8);
            panel.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.13f, 0.90f);
            AddNeonGlow(panel.gameObject, accentColor, 0.9f);

            // Bottom rarity color strip
            var stripGo = new GameObject("RarityStrip", typeof(RectTransform), typeof(Image));
            stripGo.transform.SetParent(panel, false);
            var stripRt = stripGo.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(0, 0); stripRt.anchorMax = new Vector2(1, 0);
            stripRt.pivot     = new Vector2(0.5f, 0);
            stripRt.sizeDelta = new Vector2(0, 4);
            stripRt.anchoredPosition = Vector2.zero;
            rarityStrip = stripGo.GetComponent<Image>();
            rarityStrip.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.7f);

            // Panel header label (small, top)
            var hdr = CreateTmp("Header", panel, headerText, 13, TextAlignmentOptions.Center);
            hdr.fontStyle = TMPro.FontStyles.Bold;
            hdr.color     = new Color(accentColor.r, accentColor.g, accentColor.b, 0.85f);
            hdr.rectTransform.anchorMin = new Vector2(0, 1); hdr.rectTransform.anchorMax = new Vector2(1, 1);
            hdr.rectTransform.pivot     = new Vector2(0.5f, 1);
            hdr.rectTransform.sizeDelta = new Vector2(0, 28);
            hdr.rectTransform.anchoredPosition = new Vector2(0, -4);

            // Icon (large, centered, top 55% of panel)
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(panel, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.1f, 0.38f); iconRt.anchorMax = new Vector2(0.9f, 0.92f);
            iconRt.offsetMin = new Vector2(4, 4); iconRt.offsetMax = new Vector2(-4, -32);
            iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;
            iconGo.SetActive(false);

            // Placeholder (visible when no skin selected)
            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(panel, false);
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(8, 32); phRt.offsetMax = new Vector2(-8, -32);
            placeholder = phGo;

            var phLbl = CreateTmp("PhLabel", phRt, placeholderText, 14, TextAlignmentOptions.Center);
            StretchFull(phLbl);
            phLbl.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.45f);
            phLbl.fontStyle = TMPro.FontStyles.Italic;

            // Neon dashed border effect on placeholder
            var phBorder = new GameObject("Border", typeof(RectTransform), typeof(Image), typeof(Outline));
            phBorder.transform.SetParent(phGo.transform, false);
            var phBRt = phBorder.GetComponent<RectTransform>();
            phBRt.anchorMin = new Vector2(0.15f, 0.25f); phBRt.anchorMax = new Vector2(0.85f, 0.85f);
            phBRt.offsetMin = Vector2.zero; phBRt.offsetMax = Vector2.zero;
            phBorder.GetComponent<Image>().color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.07f);
            var phOl = phBorder.GetComponent<Outline>();
            phOl.effectColor    = new Color(accentColor.r, accentColor.g, accentColor.b, 0.50f);
            phOl.effectDistance = new Vector2(2, -2);

            // Info labels (bottom area)
            nameLabel = CreateTmp("Name", panel, "", 15, TextAlignmentOptions.Center);
            nameLabel.fontStyle = TMPro.FontStyles.Bold; nameLabel.color = Color.white;
            nameLabel.rectTransform.anchorMin = new Vector2(0, 0); nameLabel.rectTransform.anchorMax = new Vector2(1, 0);
            nameLabel.rectTransform.pivot     = new Vector2(0.5f, 0);
            nameLabel.rectTransform.sizeDelta = new Vector2(-8, 22); nameLabel.rectTransform.anchoredPosition = new Vector2(0, 66);
            nameLabel.enableWordWrapping = false; nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            nameLabel.gameObject.SetActive(false);

            rarityLabel = CreateTmp("Rarity", panel, "", 12, TextAlignmentOptions.Center);
            rarityLabel.fontStyle = TMPro.FontStyles.Bold; rarityLabel.color = accentColor;
            rarityLabel.rectTransform.anchorMin = new Vector2(0, 0); rarityLabel.rectTransform.anchorMax = new Vector2(1, 0);
            rarityLabel.rectTransform.pivot     = new Vector2(0.5f, 0);
            rarityLabel.rectTransform.sizeDelta = new Vector2(-8, 18); rarityLabel.rectTransform.anchoredPosition = new Vector2(0, 48);
            rarityLabel.gameObject.SetActive(false);

            vpLabel = CreateTmp("VP", panel, "", 13, TextAlignmentOptions.Center);
            vpLabel.color = NeonGold; vpLabel.fontStyle = TMPro.FontStyles.Bold;
            vpLabel.rectTransform.anchorMin = new Vector2(0, 0); vpLabel.rectTransform.anchorMax = new Vector2(1, 0);
            vpLabel.rectTransform.pivot     = new Vector2(0.5f, 0);
            vpLabel.rectTransform.sizeDelta = new Vector2(-8, 18); vpLabel.rectTransform.anchoredPosition = new Vector2(0, 30);
            vpLabel.gameObject.SetActive(false);

            chanceLabel = CreateTmp("Chance", panel, "--", 12, TextAlignmentOptions.Center);
            chanceLabel.color = new Color(0.65f, 0.75f, 0.88f, 1f);
            chanceLabel.rectTransform.anchorMin = new Vector2(0, 0); chanceLabel.rectTransform.anchorMax = new Vector2(1, 0);
            chanceLabel.rectTransform.pivot     = new Vector2(0.5f, 0);
            chanceLabel.rectTransform.sizeDelta = new Vector2(-8, 18); chanceLabel.rectTransform.anchoredPosition = new Vector2(0, 12);
            chanceLabel.gameObject.SetActive(false);
        }

        /// <summary>VP delta hızlı filtre butonu (+1000 / +2000 / +3000).</summary>
        static Button BuildVpFilterButton(RectTransform parent, string label, float xMin, float xMax)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0); rt.anchorMax = new Vector2(xMax, 1);
            rt.offsetMin = new Vector2(2, 0); rt.offsetMax = new Vector2(-2, 0);
            go.GetComponent<Image>().color = new Color(0.15f, 0.18f, 0.22f, 1f);

            var lbl = CreateTmp("Label", rt, label, 11, TextAlignmentOptions.Center);
            lbl.fontStyle = TMPro.FontStyles.Bold;
            lbl.color = new Color(0.7f, 0.75f, 0.85f);
            StretchFull(lbl);

            return go.GetComponent<Button>();
        }

        /// <summary>Builds a tab button with colored underline indicator.</summary>
        static Button BuildTab(RectTransform parent, string label, float xMin, float xMax,
                                Color accentColor, out Image underline)
        {
            var go = new GameObject(label + "Tab", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0); rt.anchorMax = new Vector2(xMax, 1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0);  // transparent bg

            var lbl = CreateTmp("Label", rt, label, 15, TextAlignmentOptions.Center);
            lbl.fontStyle = TMPro.FontStyles.Bold;
            lbl.color = new Color(0.40f, 0.45f, 0.55f, 1f);
            lbl.rectTransform.anchorMin = Vector2.zero; lbl.rectTransform.anchorMax = Vector2.one;
            lbl.rectTransform.offsetMin = new Vector2(0, 4); lbl.rectTransform.offsetMax = new Vector2(0, -4);

            // Bottom underline
            var ulGo = new GameObject("Underline", typeof(RectTransform), typeof(Image));
            ulGo.transform.SetParent(go.transform, false);
            var ulRt = ulGo.GetComponent<RectTransform>();
            ulRt.anchorMin = new Vector2(0.1f, 0); ulRt.anchorMax = new Vector2(0.9f, 0);
            ulRt.pivot     = new Vector2(0.5f, 0);
            ulRt.sizeDelta = new Vector2(0, 3);
            ulRt.anchoredPosition = Vector2.zero;
            underline = ulGo.GetComponent<Image>();
            underline.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.25f);

            return go.GetComponent<Button>();
        }
    }
}
#endif
