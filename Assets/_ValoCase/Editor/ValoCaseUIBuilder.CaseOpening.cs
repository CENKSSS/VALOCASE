#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.UI;
using ValoCase.UI.Screens;

namespace ValoCase.Editor
{
    // CaseOpeningScreen layout builder — top bar, case selector, display panel,
    // drop list ve spin overlay'i tek metodda kurar.
    public static partial class ValoCaseUIBuilder
    {
        static RectTransform BuildCaseOpeningScreen(RectTransform parent, UINavigator navigator, CaseListItemView caseItemPrefab, DropItemView dropItemPrefab)
        {
            var screen = CreateScreenPanel(parent, "CaseOpeningScreen", ScreenType.CaseOpening, out var group);
            screen.gameObject.AddComponent<CaseOpeningScreen>();

            // ── Top bar ──────────────────────────────────────────────────────
            var topBar = CreateRect("TopBar", screen, new Vector2(0, 72));
            StretchTop(topBar, 0, 72);
            topBar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var back = CreateMenuButton(topBar, "Back", "← BACK", Panel, Vector2.zero, new Vector2(160, 52));
            back.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.5f);
            back.GetComponent<RectTransform>().anchorMax = new Vector2(0, 0.5f);
            back.GetComponent<RectTransform>().pivot = new Vector2(0, 0.5f);
            back.GetComponent<RectTransform>().anchoredPosition = new Vector2(12, 0);

            var walletLabel = CreateTmp("Wallet", topBar, "2,500 VP", 20, TextAlignmentOptions.Right);
            walletLabel.rectTransform.anchorMin = new Vector2(1, 0.5f);
            walletLabel.rectTransform.anchorMax = new Vector2(1, 0.5f);
            walletLabel.rectTransform.pivot = new Vector2(1, 0.5f);
            walletLabel.rectTransform.anchoredPosition = new Vector2(-16, 0);
            walletLabel.rectTransform.sizeDelta = new Vector2(220, 36);
            walletLabel.color = new Color(0.6f, 1f, 0.6f);

            // ── Case tab strip (horizontal scroll) ───────────────────────────
            var caseTabStrip = CreateHorizontalScrollContent(
                "CaseTabs", screen,
                new Vector2(0, 108), new Vector2(0, 576),   // anchored center-ish
                new Vector2(200, 96), 10f);
            // Reposition: stretch full width, fixed height below top bar
            var tabParent = caseTabStrip.parent.parent as RectTransform; // the scroll root
            tabParent.anchorMin = new Vector2(0, 1);
            tabParent.anchorMax = new Vector2(1, 1);
            tabParent.pivot = new Vector2(0.5f, 1);
            tabParent.anchoredPosition = new Vector2(0, -72);
            tabParent.sizeDelta = new Vector2(0, 108);
            tabParent.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);

            // ── caseDisplayPanel ─────────────────────────────────────────────
            // Covers from below tab strip to bottom of screen
            var displayPanel = CreateRect("CaseDisplayPanel", screen, Vector2.zero);
            displayPanel.anchorMin = new Vector2(0, 0);
            displayPanel.anchorMax = new Vector2(1, 1);
            displayPanel.offsetMin = new Vector2(0, 0);
            displayPanel.offsetMax = new Vector2(0, -180);  // leave room for topBar + tabs
            DisableRaycast(displayPanel);

            // Radial tint background (changes color with selected case)
            var themeBg = CreateRect("ThemeBg", displayPanel, Vector2.zero);
            StretchFull(themeBg);
            var themeBgImg = themeBg.GetComponent<Image>();
            themeBgImg.color = new Color(1f, 0.27f, 0.33f, 0.12f);
            themeBgImg.raycastTarget = false;

            // Large case icon
            var caseIconRect = CreateRect("CaseIcon", displayPanel, new Vector2(300, 300));
            caseIconRect.anchorMin = new Vector2(0.5f, 1);
            caseIconRect.anchorMax = new Vector2(0.5f, 1);
            caseIconRect.pivot = new Vector2(0.5f, 1);
            caseIconRect.anchoredPosition = new Vector2(0, -16);
            caseIconRect.sizeDelta = new Vector2(300, 300);
            var caseIconImg = caseIconRect.GetComponent<Image>();
            caseIconImg.color = AccentRed;
            caseIconImg.preserveAspect = true;

            // Case name below icon
            var caseNameLbl = CreateTmp("CaseName", displayPanel, "PROTOCOL CRATE", 26, TextAlignmentOptions.Center);
            caseNameLbl.rectTransform.anchorMin = new Vector2(0.5f, 1);
            caseNameLbl.rectTransform.anchorMax = new Vector2(0.5f, 1);
            caseNameLbl.rectTransform.pivot = new Vector2(0.5f, 1);
            caseNameLbl.rectTransform.anchoredPosition = new Vector2(0, -320);
            caseNameLbl.rectTransform.sizeDelta = new Vector2(700, 38);
            caseNameLbl.fontStyle = TMPro.FontStyles.Bold;
            ApplyTitleGlow(caseNameLbl, NeonPink);

            // Price + open button row
            var openBtn = CreateMenuButton(displayPanel, "Open", "KASAYI AÇ", AccentRed, new Vector2(60, -370), new Vector2(340, 80));
            var openBtnRt = openBtn.GetComponent<RectTransform>();
            openBtnRt.anchorMin = new Vector2(0.5f, 1);
            openBtnRt.anchorMax = new Vector2(0.5f, 1);
            openBtnRt.pivot = new Vector2(0.5f, 1);
            openBtnRt.anchoredPosition = new Vector2(60, -370);

            var priceLbl = CreateTmp("Price", displayPanel, "475 VP", 22, TextAlignmentOptions.Center);
            priceLbl.rectTransform.anchorMin = new Vector2(0.5f, 1);
            priceLbl.rectTransform.anchorMax = new Vector2(0.5f, 1);
            priceLbl.rectTransform.pivot = new Vector2(0.5f, 1);
            priceLbl.rectTransform.anchoredPosition = new Vector2(-140, -383);
            priceLbl.rectTransform.sizeDelta = new Vector2(180, 54);
            priceLbl.color = new Color(0.6f, 1f, 0.6f);

            // "KASA İÇERİR" header label
            var containsLbl = CreateTmp("ContainsHeader", displayPanel, "KASA İÇERİR", 18, TextAlignmentOptions.Left);
            containsLbl.rectTransform.anchorMin = new Vector2(0, 1);
            containsLbl.rectTransform.anchorMax = new Vector2(1, 1);
            containsLbl.rectTransform.pivot = new Vector2(0.5f, 1);
            containsLbl.rectTransform.anchoredPosition = new Vector2(0, -462);
            containsLbl.rectTransform.sizeDelta = new Vector2(-32, 28);
            containsLbl.color = new Color(0.65f, 0.72f, 0.82f);
            containsLbl.fontStyle = TMPro.FontStyles.Bold;

            // Drop list — vertical scroll below header
            var dropScroll = CreateRect("DropScroll", displayPanel, Vector2.zero);
            dropScroll.anchorMin = new Vector2(0, 0);
            dropScroll.anchorMax = new Vector2(1, 1);
            dropScroll.offsetMin = new Vector2(0, 0);
            dropScroll.offsetMax = new Vector2(0, -496);
            dropScroll.GetComponent<Image>().color = new Color(0, 0, 0, 0.15f);

            var dropScrollComp = dropScroll.gameObject.AddComponent<ScrollRect>();
            dropScrollComp.horizontal = false;
            dropScrollComp.vertical = true;
            dropScrollComp.movementType = ScrollRect.MovementType.Clamped;

            var dropViewport = CreateRect("Viewport", dropScroll, Vector2.zero);
            StretchFull(dropViewport);
            dropViewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            dropViewport.gameObject.AddComponent<RectMask2D>();

            var dropContent = CreateRect("DropContent", dropViewport, Vector2.zero);
            dropContent.anchorMin = new Vector2(0, 1);
            dropContent.anchorMax = new Vector2(1, 1);
            dropContent.pivot = new Vector2(0.5f, 1);
            dropContent.anchoredPosition = Vector2.zero;
            dropContent.sizeDelta = Vector2.zero;
            DestroyImage(dropContent);

            var dropLayout = dropContent.gameObject.AddComponent<VerticalLayoutGroup>();
            dropLayout.childControlWidth = true;
            dropLayout.childControlHeight = true;
            dropLayout.childForceExpandWidth = true;
            dropLayout.childForceExpandHeight = false;
            dropLayout.spacing = 2f;
            dropLayout.padding = new RectOffset(0, 0, 4, 4);

            var dropFitter = dropContent.gameObject.AddComponent<ContentSizeFitter>();
            dropFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            dropScrollComp.viewport = dropViewport;
            dropScrollComp.content = dropContent;

            // ── Spin Overlay (hidden by default) ─────────────────────────────
            var spinOverlay = CreateRect("SpinOverlay", screen, Vector2.zero);
            StretchFull(spinOverlay);
            spinOverlay.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.1f, 0.97f);

            var reelViewport = CreateRect("ReelViewport", spinOverlay, new Vector2(0, 300));
            reelViewport.anchorMin = new Vector2(0, 0.5f);
            reelViewport.anchorMax = new Vector2(1, 0.5f);
            reelViewport.pivot = new Vector2(0.5f, 0.5f);
            reelViewport.anchoredPosition = new Vector2(0, 60);
            reelViewport.sizeDelta = new Vector2(0, 300);
            reelViewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.4f);
            reelViewport.gameObject.AddComponent<RectMask2D>();

            var reelContent = CreateRect("ReelContent", reelViewport, new Vector2(4000, 280));
            reelContent.pivot = new Vector2(0, 0.5f);
            reelContent.anchorMin = new Vector2(0, 0.5f);
            reelContent.anchorMax = new Vector2(0, 0.5f);
            reelContent.anchoredPosition = Vector2.zero;
            reelContent.sizeDelta = new Vector2(4000, 280);

            var marker = CreateRect("CenterMarker", reelViewport, new Vector2(4, 320));
            StretchCenter(marker, 0, 0, 4, 320);
            marker.GetComponent<Image>().color = AccentRed;
            AddNeonGlow(marker.gameObject, AccentRed, 4f);

            var centerLine = CreateRect("CenterLine", reelViewport, new Vector2(6, 320));
            StretchCenter(centerLine, 0, 0, 6, 320);
            var centerLineImg = centerLine.GetComponent<Image>();
            centerLineImg.color = NeonCyan;
            AddNeonGlow(centerLine.gameObject, NeonCyan, 4f);

            // "Opening..." label in spin overlay
            var spinningLbl = CreateTmp("SpinningLabel", spinOverlay, "Kasa açılıyor...", 22, TextAlignmentOptions.Center);
            spinningLbl.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            spinningLbl.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            spinningLbl.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            spinningLbl.rectTransform.anchoredPosition = new Vector2(0, 220);
            spinningLbl.rectTransform.sizeDelta = new Vector2(600, 36);
            spinningLbl.color = new Color(0.7f, 0.8f, 0.9f);
            ApplyTitleGlow(spinningLbl, NeonCyan);

            var skipBtn = CreateMenuButton(spinOverlay, "Skip", "ATLA", Panel, new Vector2(0, -200), new Vector2(240, 68));
            var skipBtnRt = skipBtn.GetComponent<RectTransform>();
            skipBtnRt.anchorMin = new Vector2(0.5f, 0.5f);
            skipBtnRt.anchorMax = new Vector2(0.5f, 0.5f);
            skipBtnRt.anchoredPosition = new Vector2(0, -200);

            spinOverlay.gameObject.SetActive(false);

            // ── Wire components ───────────────────────────────────────────────
            var flow = screen.gameObject.AddComponent<ValoCase.CaseOpening.CaseOpeningFlowController>();
            var spin = screen.gameObject.AddComponent<ValoCase.CaseOpening.CaseSpinController>();

            var flowSo = new SerializedObject(flow);
            flowSo.FindProperty("spinController").objectReferenceValue = spin;
            flowSo.ApplyModifiedPropertiesWithoutUndo();

            var spinSo = new SerializedObject(spin);
            spinSo.FindProperty("reelContent").objectReferenceValue = reelContent;
            spinSo.FindProperty("centerMarker").objectReferenceValue = marker;
            spinSo.FindProperty("centerLine").objectReferenceValue = centerLineImg;
            spinSo.ApplyModifiedPropertiesWithoutUndo();

            var comp = screen.GetComponent<CaseOpeningScreen>();
            var so = new SerializedObject(comp);
            so.FindProperty("navigator").objectReferenceValue = navigator;
            so.FindProperty("backButton").objectReferenceValue = back;
            so.FindProperty("walletLabel").objectReferenceValue = walletLabel;
            so.FindProperty("caseListRoot").objectReferenceValue = caseTabStrip;
            so.FindProperty("caseItemPrefab").objectReferenceValue = caseItemPrefab;
            so.FindProperty("caseDisplayPanel").objectReferenceValue = displayPanel.gameObject;
            so.FindProperty("caseIconDisplay").objectReferenceValue = caseIconImg;
            so.FindProperty("caseThemeBg").objectReferenceValue = themeBgImg;
            so.FindProperty("selectedCaseLabel").objectReferenceValue = caseNameLbl;
            so.FindProperty("priceLabel").objectReferenceValue = priceLbl;
            so.FindProperty("openButton").objectReferenceValue = openBtn;
            so.FindProperty("dropListRoot").objectReferenceValue = dropContent;
            so.FindProperty("dropItemPrefab").objectReferenceValue = dropItemPrefab;
            so.FindProperty("spinOverlay").objectReferenceValue = spinOverlay.gameObject;
            so.FindProperty("flow").objectReferenceValue = flow;
            so.FindProperty("skipButton").objectReferenceValue = skipBtn;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex = (int)ScreenType.CaseOpening;
            so.ApplyModifiedPropertiesWithoutUndo();

            return screen;
        }
    }
}
#endif
