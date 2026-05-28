using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Battle;
using ValoCase.Data;
using ValoCase.Profile;
using ValoCase.UI.Factory;

using static ValoCase.UI.Screens.CaseBattlePalette;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Stateless UI constructor for CaseBattleScreen.
    /// Builds every visual element, wires layout, and returns all refs via CaseBattleUiRefs.
    /// No game logic here — only GameObject creation and styling.
    /// </summary>
    internal static class CaseBattleUiBuilder
    {
        // ── Roulette card constants (shared with animator) ────────────────────
        internal const float CardW           = 132f;
        internal const float CardH           = 156f;
        internal const float CardSpacing     = 10f;
        // For VERTICAL reel, stride must be CardH + spacing (not CardW)
        internal const float CardStride      = CardH + CardSpacing;
        internal const int   ResultCardIndex = 22;
        internal const int   PaddingAfter    = 5;
        internal const int   TotalCards      = ResultCardIndex + 1 + PaddingAfter; // 28

        // Reel viewport shows exactly 2 card-heights → half / full / half layout.
        internal const float ReelViewportH   = CardH * 2f;

        // Internal column-build counter (reset in BuildArena, used for debug logs)
        static int _buildColumnIndex;

        // ── Entry point ───────────────────────────────────────────────────────
        internal static CaseBattleUiRefs Build(RectTransform root)
        {
            var refs = new CaseBattleUiRefs();

            BuildBackground(root);
            BuildTitleBar(root, refs);
            BuildCostStrip(root, refs);

            // Wrap arena / footer / action bar under one container so the
            // Setup and CasePicker states can hide them together.
            var arenaPanelRt = UIFactory.CreateRectAnchored("ArenaPanel", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            arenaPanelRt.offsetMin = arenaPanelRt.offsetMax = Vector2.zero;
            refs.ArenaPanel = arenaPanelRt.gameObject;

            BuildArena(arenaPanelRt, refs);
            BuildFooter(arenaPanelRt, refs);
            BuildActionBar(arenaPanelRt, refs);
            BuildLobbyOverlay(arenaPanelRt, refs);

            // New flow panels (Setup / CasePicker) sit above ArenaPanel
            BuildSetupPanel(root, refs);
            BuildCasePickerPanel(root, refs);

            // Final result popup — floats above everything
            BuildFinalPopup(root, refs);

            return refs;
        }

        // ── Background ────────────────────────────────────────────────────────
        static void BuildBackground(RectTransform root)
        {
            var bg = UIFactory.CreateRectAnchored("PageBg", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = BgDeep;
            bg.GetComponent<Image>().raycastTarget = false;
            bg.SetSiblingIndex(0);

            var vig = UIFactory.CreateRectAnchored("Vignette", root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            vig.sizeDelta = new Vector2(900, 600);
            vig.GetComponent<Image>().color = new Color(0.08f, 0.05f, 0.18f, 0.18f);
            vig.GetComponent<Image>().raycastTarget = false;
            vig.SetSiblingIndex(1);
        }

        // ── Title bar (72 px top) ─────────────────────────────────────────────
        static void BuildTitleBar(RectTransform root, CaseBattleUiRefs refs)
        {
            const float h = 72f;
            var bar = UIFactory.CreateRectAnchored("TitleBar", root,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0, h);
            bar.GetComponent<Image>().color = new Color(0.02f, 0.015f, 0.05f, 0.70f);
            bar.GetComponent<Image>().raycastTarget = false;
            UIFactory.AddGlowLine(bar, AccentPink, bottom: true, height: 1.5f, alpha: 0.45f);
            refs.TopBarRect = bar;

            // Center icon + title
            var titleRow = UIFactory.CreateRectAnchored("TitleRow", bar,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            titleRow.anchoredPosition = Vector2.zero;
            titleRow.sizeDelta = new Vector2(500, 44);
            var hl = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.spacing = 12f;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;

            var swordTmp = UIFactory.CreateText(titleRow, "Swords", "x", 18,
                TextAlignmentOptions.Center, AccentRed, FontStyles.Bold);
            var sLE = swordTmp.gameObject.AddComponent<LayoutElement>();
            sLE.minWidth = sLE.preferredWidth = 22f;

            var titleTmp = UIFactory.CreateText(titleRow, "Title", "VANDAL CASE BATTLE", 18,
                TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            titleTmp.characterSpacing = 3f;

            // Round counter (top-right)
            refs.RoundLabel = UIFactory.CreateText(bar, "RoundLabel", "", 11,
                TextAlignmentOptions.Right, AccentPink, FontStyles.Bold);
            var rlRt = refs.RoundLabel.rectTransform;
            rlRt.anchorMin = new Vector2(1, 0.5f); rlRt.anchorMax = new Vector2(1, 0.5f);
            rlRt.pivot = new Vector2(1, 0.5f);
            rlRt.anchoredPosition = new Vector2(-18, 0);
            rlRt.sizeDelta = new Vector2(120, 28);
        }

        // ── Cost strip (96 px below title bar) ───────────────────────────────
        static void BuildCostStrip(RectTransform root, CaseBattleUiRefs refs)
        {
            const float topH = 72f, stripH = 96f;
            var strip = UIFactory.CreateRectAnchored("CostStrip", root,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
            strip.anchoredPosition = new Vector2(0, -topH);
            strip.sizeDelta = new Vector2(0, stripH);
            strip.GetComponent<Image>().color = new Color(0.05f, 0.034f, 0.095f, 0.97f);
            UIFactory.AddGlowLine(strip, AccentPink, bottom: true,  height: 1.5f, alpha: 0.40f);
            UIFactory.AddGlowLine(strip, AccentPink, bottom: false, height: 1.5f, alpha: 0.15f);

            // Center spotlight
            var spotlight = UIFactory.CreateRectAnchored("Spotlight", strip,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            spotlight.sizeDelta = new Vector2(220, stripH);
            spotlight.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.055f);
            spotlight.GetComponent<Image>().raycastTarget = false;

            var spotCore = UIFactory.CreateRectAnchored("SpotCore", spotlight,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            spotCore.sizeDelta = new Vector2(110, stripH);
            spotCore.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.045f);
            spotCore.GetComponent<Image>().raycastTarget = false;

            // Left: total cost
            var left = UIFactory.CreateRectAnchored("Left", strip,
                new Vector2(0, 0), new Vector2(0.32f, 1), new Vector2(0, 0.5f));
            left.offsetMin = new Vector2(22, 0); left.offsetMax = Vector2.zero;
            var lVl = left.gameObject.AddComponent<VerticalLayoutGroup>();
            lVl.childAlignment = TextAnchor.MiddleLeft;
            lVl.spacing = 2f;
            lVl.childForceExpandWidth = lVl.childForceExpandHeight = false;

            var hintL = UIFactory.CreateText(left, "Hint", "TOTAL COST", 10,
                TextAlignmentOptions.MidlineLeft, TextDim, FontStyles.Bold);
            hintL.characterSpacing = 2f;
            refs.CostAmountLabel = UIFactory.CreateText(left, "Amount", "(G) 0", 22,
                TextAlignmentOptions.MidlineLeft, AccentGreen, FontStyles.Bold);

            // Center: case icons row
            var center = UIFactory.CreateRectAnchored("CaseIconsRow", strip,
                new Vector2(0.32f, 0), new Vector2(0.68f, 1), new Vector2(0.5f, 0.5f));
            center.offsetMin = center.offsetMax = Vector2.zero;
            var cHl = center.gameObject.AddComponent<HorizontalLayoutGroup>();
            cHl.childAlignment = TextAnchor.MiddleCenter;
            cHl.spacing = 12f;
            cHl.childForceExpandWidth = cHl.childForceExpandHeight = false;
            refs.CaseIconsRow = center;

            // Right: case count
            var right = UIFactory.CreateRectAnchored("Right", strip,
                new Vector2(0.68f, 0), new Vector2(1, 1), new Vector2(1, 0.5f));
            right.offsetMin = Vector2.zero; right.offsetMax = new Vector2(-22, 0);
            var rVl = right.gameObject.AddComponent<VerticalLayoutGroup>();
            rVl.childAlignment = TextAnchor.MiddleRight;
            rVl.spacing = 2f;
            rVl.childForceExpandWidth = rVl.childForceExpandHeight = false;

            var hintR = UIFactory.CreateText(right, "CasesHint", "CASES", 10,
                TextAlignmentOptions.MidlineRight, TextDim, FontStyles.Bold);
            hintR.characterSpacing = 2f;
            refs.CaseCountLabel = UIFactory.CreateText(right, "Count", "0", 30,
                TextAlignmentOptions.MidlineRight, AccentPink, FontStyles.Bold);
        }

        // ── Arena (fills remaining space) ─────────────────────────────────────
        static void BuildArena(RectTransform root, CaseBattleUiRefs refs)
        {
            const float topInset    = 72f + 96f + 8f;
            const float bottomInset = 110f + 72f + 8f;

            var arena = UIFactory.CreateRectAnchored("Arena", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            arena.offsetMin = new Vector2(10, bottomInset);
            arena.offsetMax = new Vector2(-10, -topInset);

            BuildPlayerColumn(arena, refs.Player,   isPlayer: true,
                aMin: new Vector2(0,      0), aMax: new Vector2(0.487f, 1), refs);
            BuildPlayerColumn(arena, refs.Opponent, isPlayer: false,
                aMin: new Vector2(0.513f, 0), aMax: new Vector2(1,      1), refs);

            // Extra bot columns built up-front, hidden by default.
            // Screen sets visibility + anchors based on selected player count.
            BuildPlayerColumn(arena, refs.Bot2, isPlayer: false,
                aMin: new Vector2(0f, 0f), aMax: new Vector2(0.001f, 1f), refs);
            refs.Bot2.ColumnRect.gameObject.name = "Bot2Col";
            refs.Bot2.ColumnRect.gameObject.SetActive(false);

            BuildPlayerColumn(arena, refs.Bot3, isPlayer: false,
                aMin: new Vector2(0f, 0f), aMax: new Vector2(0.001f, 1f), refs);
            refs.Bot3.ColumnRect.gameObject.name = "Bot3Col";
            refs.Bot3.ColumnRect.gameObject.SetActive(false);

            BuildVsBadge(arena, refs);
        }

        static void BuildPlayerColumn(RectTransform arena, CaseBattlePanelRefs refs,
            bool isPlayer, Vector2 aMin, Vector2 aMax, CaseBattleUiRefs uiRefs)
        {
            Color accent     = isPlayer ? AccentPink     : CyberBlue;
            Color accentSoft = isPlayer ? AccentPinkSoft : CyberBlueSoft;
            Color accentGlow = isPlayer ? AccentPinkGlow : CyberBlueGlow;
            Color border     = isPlayer ? BorderPink     : BorderBlue;

            var col = UIFactory.CreateRectAnchored(
                isPlayer ? "PlayerCol" : "OpponentCol",
                arena, aMin, aMax, new Vector2(0.5f, 0.5f));
            col.offsetMin = col.offsetMax = Vector2.zero;
            refs.ColumnRect = col;

            // Glassmorphism background
            var bgPanel = UIFactory.CreateRectAnchored("Bg", col,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            bgPanel.offsetMin = bgPanel.offsetMax = Vector2.zero;
            bgPanel.GetComponent<Image>().color = new Color(0.03f, 0.022f, 0.068f, 0.62f);
            UIFactory.AddColoredBorder(bgPanel, accent, top: true,  height: 2f);
            UIFactory.AddSideGlow(bgPanel, accentSoft, left: true);
            UIFactory.AddSideGlow(bgPanel, accentSoft, left: false);
            refs.PanelBackground = bgPanel.GetComponent<Image>();

            var topGlow = UIFactory.CreateRectAnchored("TopGlow", bgPanel,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
            topGlow.sizeDelta = new Vector2(0, 60f);
            topGlow.anchoredPosition = Vector2.zero;
            topGlow.GetComponent<Image>().color = accentGlow;
            topGlow.GetComponent<Image>().raycastTarget = false;
            refs.HeaderGlow = topGlow.GetComponent<Image>();

            // ── Header (140 px) ───────────────────────────────────────────────
            const float headerH = 140f;
            var header = UIFactory.CreateRectAnchored("Header", col,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0, headerH);
            header.GetComponent<Image>().color = Color.clear;
            header.GetComponent<Image>().raycastTarget = false;

            var headerVl = header.gameObject.AddComponent<VerticalLayoutGroup>();
            headerVl.childAlignment = TextAnchor.MiddleCenter;
            headerVl.spacing = 6f;
            headerVl.childForceExpandWidth = headerVl.childForceExpandHeight = false;
            headerVl.padding = new RectOffset(0, 0, 16, 0);

            // Avatar diamond (45° rotated square)
            var avRoot = UIFactory.CreateRectAnchored("AvatarRoot", header,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            avRoot.sizeDelta = new Vector2(58f, 58f);
            avRoot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var avLE = avRoot.gameObject.AddComponent<LayoutElement>();
            avLE.minWidth = avLE.preferredWidth = 58f;
            avLE.minHeight = avLE.preferredHeight = 58f;
            avRoot.GetComponent<Image>().color = new Color(0.08f, 0.055f, 0.165f, 1f);
            var avOl = avRoot.gameObject.AddComponent<Outline>();
            avOl.effectColor = accent; avOl.effectDistance = new Vector2(3f, -3f);

            if (isPlayer)
            {
                var avImgGo = new GameObject("ProfileImg", typeof(RectTransform), typeof(Image));
                avImgGo.transform.SetParent(avRoot, false);
                var avImgRt = (RectTransform)avImgGo.transform;
                avImgRt.localRotation = Quaternion.Euler(0f, 0f, -45f);
                UIFactory.StretchFull(avImgRt);
                uiRefs.PlayerAvatarImg = avImgGo.GetComponent<Image>();
                uiRefs.PlayerAvatarImg.sprite = PlayerProfileData.Avatar;
                uiRefs.PlayerAvatarImg.preserveAspect = true;
                uiRefs.PlayerAvatarImg.color = Color.white;
            }
            else
            {
                var avTag = UIFactory.CreateText(avRoot, "Tag", "B", 22,
                    TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
                avTag.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
                UIFactory.StretchFull(avTag.rectTransform);
            }

            // Name label
            refs.NameLabel = UIFactory.CreateText(header, "Name",
                isPlayer ? PlayerProfileData.Username.ToUpper() : BattleConstants.BotName.ToUpper(),
                16, TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            refs.NameLabel.characterSpacing = 1.5f;
            var nmLE = refs.NameLabel.gameObject.AddComponent<LayoutElement>();
            nmLE.minHeight = nmLE.preferredHeight = 22f;

            // VP label
            refs.VpLabel = UIFactory.CreateText(header, "Vp", "(G) 0", 14,
                TextAlignmentOptions.Center, AccentGreen, FontStyles.Bold);
            var vpLE = refs.VpLabel.gameObject.AddComponent<LayoutElement>();
            vpLE.minHeight = vpLE.preferredHeight = 20f;

            // ── Vertical roulette reel ────────────────────────────────────────
            // Viewport ~= 2 cards tall  →  half / full / half visible
            const float rouletteH = CardH * 2f + 12f;
            float rouletteW = CardW + 18f;
            var rouletteArea = UIFactory.CreateRectAnchored("RouletteArea", col,
                new Vector2(0.5f, 1),
                new Vector2(0.5f, 1),
                new Vector2(0.5f, 1));
            rouletteArea.anchoredPosition = new Vector2(0, -headerH - 10f);
            rouletteArea.sizeDelta = new Vector2(rouletteW, rouletteH);
            refs.RouletteAreaObject = rouletteArea.gameObject;
            BuildVerticalRoulette(rouletteArea, refs, border);

            Debug.Log($"[CB_UI] Build column={col.gameObject.name} rouletteH={rouletteH} cardH={CardH} cardStride={CardStride}");

            // ── History scroll ────────────────────────────────────────────────
            const float scrollTop = headerH + rouletteH + 10f;
            var scrollGo = new GameObject("HistoryScroll",
                typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(col, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(6, 6);
            srt.offsetMax = new Vector2(-6, -scrollTop);
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.10f);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.10f; sr.inertia = true;
            sr.decelerationRate = 0.14f; sr.scrollSensitivity = 40f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vprt = (RectTransform)vpGo.transform;
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = vprt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var crt = (RectTransform)contentGo.transform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = crt.sizeDelta = Vector2.zero;

            var glg = contentGo.GetComponent<GridLayoutGroup>();
            // Default = 2-player size; reconfigured dynamically by ApplyColumnLayout.
            glg.cellSize    = new Vector2(104f, 122f);
            glg.spacing     = new Vector2(6f, 6f);
            glg.padding     = new RectOffset(4, 4, 4, 4);
            glg.childAlignment = TextAnchor.UpperCenter;
            glg.constraint  = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vprt; sr.content = crt;
            refs.GridRoot = contentGo.transform;
        }

        static void BuildRouletteStrip(RectTransform parent, CaseBattlePanelRefs refs, Color border)
        {
            var frame = UIFactory.CreateRectAnchored("RouletteFrame", parent,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            frame.offsetMin = new Vector2(0, 3); frame.offsetMax = new Vector2(0, -3);
            frame.GetComponent<Image>().color = new Color(0.028f, 0.020f, 0.065f, 1f);
            UIFactory.AddColoredBorder(frame, border, top: true,  height: 1.5f);
            UIFactory.AddColoredBorder(frame, border, top: false, height: 1.5f);

            var mask = UIFactory.CreateRectAnchored("Mask", frame,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            mask.offsetMin = new Vector2(4, 4); mask.offsetMax = new Vector2(-4, -4);
            mask.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            mask.gameObject.AddComponent<RectMask2D>();
            refs.RouletteContainer = mask;

            // Scrolling content strip
            var rContent = UIFactory.CreateRectAnchored("Content", mask,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
            rContent.anchoredPosition = Vector2.zero;
            rContent.sizeDelta = new Vector2(TotalCards * CardStride, 0);
            refs.RouletteContent = rContent;

            // Center indicator (3-layer glow)
            BuildIndicator(mask);

            // Fade edges
            var fadeL = UIFactory.CreateRectAnchored("FadeLeft", mask,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), addImage: true);
            fadeL.sizeDelta = new Vector2(60, 0);
            fadeL.GetComponent<Image>().color = new Color(0.028f, 0.020f, 0.065f, 0.88f);
            fadeL.GetComponent<Image>().raycastTarget = false;
            fadeL.SetAsLastSibling();

            var fadeR = UIFactory.CreateRectAnchored("FadeRight", mask,
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), addImage: true);
            fadeR.sizeDelta = new Vector2(60, 0);
            fadeR.GetComponent<Image>().color = new Color(0.028f, 0.020f, 0.065f, 0.88f);
            fadeR.GetComponent<Image>().raycastTarget = false;
            fadeR.SetAsLastSibling();
        }

        static void BuildVerticalRoulette(
            RectTransform parent,
            CaseBattlePanelRefs refs,
            Color border)
        {
            var frame = UIFactory.CreateRectAnchored(
                "VerticalReelFrame",
                parent,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                addImage: true);

            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            var frameImg = frame.GetComponent<Image>();
            frameImg.color = new Color(0.028f, 0.020f, 0.065f, 0.98f);

            UIFactory.AddColoredBorder(frame, border, top: true,  height: 2f);
            UIFactory.AddColoredBorder(frame, border, top: false, height: 2f);

            // Center glow
            var centerGlow = UIFactory.CreateRectAnchored(
                "CenterGlow",
                frame,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f),
                addImage: true);

            centerGlow.sizeDelta = new Vector2(0, 120f);

            centerGlow.GetComponent<Image>().color =
                new Color(1f, 0.18f, 0.55f, 0.06f);

            // Top fade
            var topFade = UIFactory.CreateRectAnchored(
                "TopFade",
                frame,
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(0.5f, 1),
                addImage: true);

            topFade.sizeDelta = new Vector2(0, 90f);

            topFade.GetComponent<Image>().color =
                new Color(0.01f, 0.01f, 0.02f, 0.92f);

            // Bottom fade
            var bottomFade = UIFactory.CreateRectAnchored(
                "BottomFade",
                frame,
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0.5f, 0),
                addImage: true);

            bottomFade.sizeDelta = new Vector2(0, 90f);

            bottomFade.GetComponent<Image>().color =
                new Color(0.01f, 0.01f, 0.02f, 0.92f);

            // Mask viewport
            var viewport = UIFactory.CreateRectAnchored(
                "Viewport",
                frame,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                addImage: true);

            viewport.offsetMin = new Vector2(6, 6);
            viewport.offsetMax = new Vector2(-6, -6);

            viewport.GetComponent<Image>().color =
                new Color(1f, 1f, 1f, 0.01f);

            viewport.gameObject.AddComponent<RectMask2D>();

            refs.ReelViewport = viewport;

            // Reel content
            var content = UIFactory.CreateRectAnchored(
                "ReelContent",
                viewport,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f));

            content.anchoredPosition = Vector2.zero;
            content.sizeDelta =
                new Vector2(CardW, TotalCards * CardStride);

            refs.ReelContent = content;

            // Center focus window — soft glow only, NO line / outline indicator.
            // The winning card itself gets the highlight (handled by animator).
            var selector = UIFactory.CreateRectAnchored(
                "FocusWindow",
                frame,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f),
                addImage: true);

            selector.sizeDelta = new Vector2(0, CardH + 14f);

            var selectorImg = selector.GetComponent<Image>();
            selectorImg.color           = new Color(1f, 0.18f, 0.55f, 0.07f);
            selectorImg.raycastTarget   = false;
            // (no Outline component — line indicator removed)

            // Bump the soft center glow a bit so the focus window stands out
            var cg = centerGlow.GetComponent<Image>();
            cg.color = new Color(1f, 0.18f, 0.55f, 0.10f);

            refs.CenterGlow  = cg;
            refs.CenterFrame = selectorImg;
        }

        static void BuildIndicator(RectTransform mask)
        {
            var indGlow = UIFactory.CreateRectAnchored("IndicatorGlow", mask,
                new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), addImage: true);
            indGlow.sizeDelta = new Vector2(16f, 0f);
            indGlow.anchoredPosition = Vector2.zero;
            indGlow.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.08f);
            indGlow.GetComponent<Image>().raycastTarget = false;

            var indMid = UIFactory.CreateRectAnchored("IndicatorMid", mask,
                new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), addImage: true);
            indMid.sizeDelta = new Vector2(4f, 0f);
            indMid.anchoredPosition = Vector2.zero;
            indMid.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.30f);
            indMid.GetComponent<Image>().raycastTarget = false;

            var indLine = UIFactory.CreateRectAnchored("IndicatorLine", mask,
                new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), addImage: true);
            indLine.sizeDelta = new Vector2(1.5f, 0f);
            indLine.anchoredPosition = Vector2.zero;
            indLine.GetComponent<Image>().color = AccentPink;
            indLine.GetComponent<Image>().raycastTarget = false;
            indLine.SetAsLastSibling();
        }

        static void BuildVsBadge(RectTransform arena, CaseBattleUiRefs refs)
        {
            var halo = UIFactory.CreateRectAnchored("VsHalo", arena,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            halo.anchoredPosition = Vector2.zero;
            halo.sizeDelta = new Vector2(82f, 82f);
            halo.localRotation = Quaternion.Euler(0f, 0f, 45f);
            halo.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.08f);
            halo.GetComponent<Image>().raycastTarget = false;

            var vs = UIFactory.CreateRectAnchored("VsBadge", arena,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            vs.anchoredPosition = Vector2.zero;
            vs.sizeDelta = new Vector2(62f, 62f);
            vs.localRotation = Quaternion.Euler(0f, 0f, 45f);
            vs.GetComponent<Image>().color = new Color(0.07f, 0.04f, 0.14f, 1f);
            var ol = vs.gameObject.AddComponent<Outline>();
            ol.effectColor = AccentPink; ol.effectDistance = new Vector2(3f, -3f);

            var vsInner = UIFactory.CreateRectAnchored("Inner", vs,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            vsInner.offsetMin = new Vector2(4, 4); vsInner.offsetMax = new Vector2(-4, -4);
            vsInner.GetComponent<Image>().color = new Color(0.04f, 0.025f, 0.09f, 1f);
            vsInner.GetComponent<Image>().raycastTarget = false;

            var lbl = UIFactory.CreateText(vs, "Label", "VS", 22,
                TextAlignmentOptions.Center, AccentPink, FontStyles.Bold);
            lbl.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            lbl.characterSpacing = 2f;
            UIFactory.StretchFull(lbl.rectTransform);

            refs.VsBadge = vs.gameObject;
        }

        // ── Footer (110 px above action bar) ─────────────────────────────────
        static void BuildFooter(RectTransform root, CaseBattleUiRefs refs)
        {
            var footer = UIFactory.CreateRectAnchored("Footer", root,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), addImage: true);
            footer.anchoredPosition = new Vector2(0, 72);
            footer.sizeDelta = new Vector2(-16, 110);
            footer.GetComponent<Image>().color = new Color(0.038f, 0.026f, 0.082f, 0.97f);
            UIFactory.AddColoredBorder(footer, AccentPink,  top: true,  height: 2f);
            UIFactory.AddColoredBorder(footer, BorderPink,  top: false, height: 1f);

            // Spotlight
            foreach (var (w, a) in new[] { (260f, 0.045f), (130f, 0.050f) })
            {
                var s = UIFactory.CreateRectAnchored("Spot", footer,
                    new Vector2(0.5f, 0), new Vector2(0.5f, 1),
                    new Vector2(0.5f, 0.5f), addImage: true);
                s.sizeDelta = new Vector2(w, 0);
                s.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, a);
                s.GetComponent<Image>().raycastTarget = false;
            }

            // Winner badge — full-width (left/right total areas removed)
            refs.WinnerBadge = UIFactory.CreateRectAnchored("WinnerBadge", footer,
                new Vector2(0f, 0), new Vector2(1f, 1),
                new Vector2(0.5f, 0.5f), addImage: true).gameObject;
            ((RectTransform)refs.WinnerBadge.transform).offsetMin =
                ((RectTransform)refs.WinnerBadge.transform).offsetMax = Vector2.zero;
            refs.WinnerBadge.GetComponent<Image>().color = Color.clear;
            UIFactory.AddVerticalLayout(refs.WinnerBadge.transform, spacing: -1,
                align: TextAnchor.MiddleCenter, controlW: false, forceExpandW: false);

            var trophyRow = new GameObject("TrophyRow", typeof(RectTransform));
            trophyRow.transform.SetParent(refs.WinnerBadge.transform, false);
            var trHl = trophyRow.AddComponent<HorizontalLayoutGroup>();
            trHl.childAlignment = TextAnchor.MiddleCenter; trHl.spacing = 6f;
            trHl.childForceExpandWidth = trHl.childForceExpandHeight = false;
            var trLE = trophyRow.AddComponent<LayoutElement>();
            trLE.minHeight = trLE.preferredHeight = 32f;

            UIFactory.CreateText(trophyRow.transform, "Trophy", "WIN", 16,
                TextAlignmentOptions.Center, GoldAccent, FontStyles.Bold);
            var winWord = UIFactory.CreateText(trophyRow.transform, "Word", "WINNER", 11,
                TextAlignmentOptions.Center, TextDim, FontStyles.Bold);
            winWord.characterSpacing = 3f;

            refs.WinnerNameLabel = UIFactory.CreateText(refs.WinnerBadge.transform, "Name",
                "—", 22, TextAlignmentOptions.Center, AccentPink, FontStyles.Bold);
            var wnLE = refs.WinnerNameLabel.gameObject.AddComponent<LayoutElement>();
            wnLE.minHeight = wnLE.preferredHeight = 28f;
            refs.WinnerBadge.SetActive(false);
        }

        // ── Action bar (64 px bottom) ─────────────────────────────────────────
        static void BuildActionBar(RectTransform root, CaseBattleUiRefs refs)
        {
            refs.ActionBar = UIFactory.CreateRectAnchored("ActionBar", root,
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0)).gameObject;
            var abRt = (RectTransform)refs.ActionBar.transform;
            abRt.anchoredPosition = new Vector2(0, 14);
            abRt.sizeDelta = new Vector2(-16, 64);
            UIFactory.AddHorizontalLayout(refs.ActionBar.transform, spacing: 16,
                align: TextAnchor.MiddleCenter, forceExpandW: false, forceExpandH: false);

            refs.PlayAgainButton  = MakeCasinoBtn(refs.ActionBar.transform,
                "PlayAgainBtn", "PLAY AGAIN", 268, 52,
                new Color(0.06f, 0.04f, 0.12f, 0.95f), AccentPink);

            refs.InventoryButton  = MakeCasinoBtn(refs.ActionBar.transform,
                "InventoryBtn", "VIEW INVENTORY", 268, 52,
                new Color(0.06f, 0.04f, 0.12f, 0.95f), AccentOrange);

            refs.ActionBar.SetActive(false);
        }

        // ── Lobby overlay ─────────────────────────────────────────────────────
        static void BuildLobbyOverlay(RectTransform root, CaseBattleUiRefs refs)
        {
            refs.LobbyOverlay = UIFactory.CreateRectAnchored("LobbyOverlay", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true).gameObject;
            var ovRt = (RectTransform)refs.LobbyOverlay.transform;
            ovRt.offsetMin = new Vector2(0, 182); ovRt.offsetMax = new Vector2(0, -168);
            refs.LobbyOverlay.GetComponent<Image>().color = new Color(0.022f, 0.015f, 0.055f, 0.92f);

            var card = UIFactory.CreateRectAnchored("Card", refs.LobbyOverlay.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            card.sizeDelta = new Vector2(640, 350);
            card.GetComponent<Image>().color = new Color(0.045f, 0.032f, 0.095f, 0.98f);
            UIFactory.AddColoredBorder(card, AccentPink,     top: true,  height: 2f);
            UIFactory.AddColoredBorder(card, BorderPink,     top: false, height: 1f);
            UIFactory.AddSideGlow(card, AccentPinkSoft, left: true);
            UIFactory.AddSideGlow(card, AccentPinkSoft, left: false);

            var spot = UIFactory.CreateRectAnchored("Spot", card,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), addImage: true);
            spot.sizeDelta = new Vector2(400, 80); spot.anchoredPosition = Vector2.zero;
            spot.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.04f);
            spot.GetComponent<Image>().raycastTarget = false;

            // Title
            var titleTmp = UIFactory.CreateText(card, "Title", "SELECT ROUND COUNT",
                18, TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            titleTmp.characterSpacing = 2.5f;
            var titleRt = titleTmp.rectTransform;
            titleRt.anchorMin = new Vector2(0, 1); titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.anchoredPosition = new Vector2(0, -30);
            titleRt.sizeDelta = new Vector2(0, 28);

            // Count chips row
            var row = UIFactory.CreateRectAnchored("Row", card,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
            row.anchoredPosition = new Vector2(0, -82);
            row.sizeDelta = new Vector2(0, 96);
            var rowHl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowHl.childAlignment = TextAnchor.MiddleCenter; rowHl.spacing = 12f;
            rowHl.childForceExpandWidth = rowHl.childForceExpandHeight = false;

            refs.CountButtons.Clear();
            foreach (var c in BattleConstants.CaseCountChoices)
            {
                var (btn, bg, ol) = MakeCountChip(row, c.ToString(), 86, 86);
                refs.CountButtons.Add((btn, c, bg, ol));
            }

            // Cost label
            refs.LobbyCostLabel = UIFactory.CreateText(card, "Cost", "Total: (G) 0",
                17, TextAlignmentOptions.Center, AccentGreen, FontStyles.Bold);
            var costRt = refs.LobbyCostLabel.rectTransform;
            costRt.anchorMin = new Vector2(0, 0); costRt.anchorMax = new Vector2(1, 0);
            costRt.pivot = new Vector2(0.5f, 0);
            costRt.anchoredPosition = new Vector2(0, 112);
            costRt.sizeDelta = new Vector2(0, 28);

            // Balance label
            refs.LobbyBalanceLabel = UIFactory.CreateText(card, "Balance", "Balance: (G) 0",
                11, TextAlignmentOptions.Center, TextDim, FontStyles.Normal);
            var balRt = refs.LobbyBalanceLabel.rectTransform;
            balRt.anchorMin = new Vector2(0, 0); balRt.anchorMax = new Vector2(1, 0);
            balRt.pivot = new Vector2(0.5f, 0);
            balRt.anchoredPosition = new Vector2(0, 88);
            balRt.sizeDelta = new Vector2(0, 20);

            // Start button
            refs.StartButton = MakeCasinoBtn(card, "StartBtn", "START BATTLE",
                300, 56, AccentPink, AccentPink);
            var startRt = refs.StartButton.GetComponent<RectTransform>();
            startRt.anchorMin = new Vector2(0.5f, 0); startRt.anchorMax = new Vector2(0.5f, 0);
            startRt.pivot = new Vector2(0.5f, 0);
            startRt.anchoredPosition = new Vector2(0, 18);
        }

        // ── Shared widget builders ────────────────────────────────────────────
        internal static Button MakeCasinoBtn(Transform parent, string name, string label,
            float width, float height, Color bgColor, Color accent)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(Outline), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = le.preferredHeight = height;
            go.GetComponent<Image>().color = bgColor;
            var ol = go.GetComponent<Outline>();
            ol.effectColor = accent; ol.effectDistance = new Vector2(1.5f, -1.5f);
            var tmp = UIFactory.CreateText(go.transform, "Label", label,
                Mathf.Min(16f, height * 0.34f),
                TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            tmp.characterSpacing = 1.5f;
            UIFactory.StretchFull(tmp.rectTransform);
            return go.GetComponent<Button>();
        }

        internal static (Button btn, Image bg, Outline ol) MakeCountChip(Transform parent,
            string label, float width, float height)
        {
            var go = new GameObject($"Chip_{label}",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(Outline), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = le.preferredHeight = height;
            var bg = go.GetComponent<Image>(); bg.color = BgCard;
            var ol = go.GetComponent<Outline>();
            ol.effectColor = new Color(1f, 0.18f, 0.55f, 0.10f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            var tmp = UIFactory.CreateText(go.transform, "Label", label, 24,
                TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            UIFactory.StretchFull(tmp.rectTransform);
            return (go.GetComponent<Button>(), bg, ol);
        }

        // ─────────────────────────────────────────────────────────────────────
        // SETUP PANEL — ADD CASES card + player count + CREATE GAME
        // ─────────────────────────────────────────────────────────────────────
        static void BuildSetupPanel(RectTransform root, CaseBattleUiRefs refs)
        {
            const float topInset    = 72f + 96f + 12f;
            const float bottomInset = 12f;

            var panelRt = UIFactory.CreateRectAnchored("SetupPanel", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            panelRt.offsetMin = new Vector2(0, bottomInset);
            panelRt.offsetMax = new Vector2(0, -topInset);
            panelRt.GetComponent<Image>().color = new Color(0.022f, 0.015f, 0.055f, 0.95f);
            refs.SetupPanel = panelRt.gameObject;

            // Title
            var titleTmp = UIFactory.CreateText(panelRt, "Title", "BATTLE SETUP",
                18, TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            titleTmp.characterSpacing = 3f;
            var titleRt = titleTmp.rectTransform;
            titleRt.anchorMin = new Vector2(0, 1); titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.anchoredPosition = new Vector2(0, -18);
            titleRt.sizeDelta = new Vector2(0, 28);

            // ADD CASES card (large central button)
            var addBtnGo = new GameObject("AddCasesButton",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            addBtnGo.transform.SetParent(panelRt, false);
            var addBtnRt = (RectTransform)addBtnGo.transform;
            addBtnRt.anchorMin = new Vector2(0.5f, 1); addBtnRt.anchorMax = new Vector2(0.5f, 1);
            addBtnRt.pivot = new Vector2(0.5f, 1);
            addBtnRt.anchoredPosition = new Vector2(0, -64);
            addBtnRt.sizeDelta = new Vector2(440, 220);
            addBtnGo.GetComponent<Image>().color = new Color(0.05f, 0.035f, 0.10f, 0.95f);
            var addOl = addBtnGo.GetComponent<Outline>();
            addOl.effectColor = AccentPink; addOl.effectDistance = new Vector2(2f, -2f);
            refs.AddCasesButton = addBtnGo.GetComponent<Button>();

            var plusTmp = UIFactory.CreateText(addBtnRt, "Plus", "+", 64,
                TextAlignmentOptions.Center, AccentPink, FontStyles.Bold);
            var plusRt = plusTmp.rectTransform;
            plusRt.anchorMin = plusRt.anchorMax = new Vector2(0.5f, 0.5f);
            plusRt.pivot = new Vector2(0.5f, 0.5f);
            plusRt.anchoredPosition = new Vector2(0, 22);
            plusRt.sizeDelta = new Vector2(80, 80);

            var hintTmp = UIFactory.CreateText(addBtnRt, "Hint", "ADD CASES", 14,
                TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            hintTmp.characterSpacing = 3f;
            var hintRt = hintTmp.rectTransform;
            hintRt.anchorMin = new Vector2(0, 0); hintRt.anchorMax = new Vector2(1, 0);
            hintRt.pivot = new Vector2(0.5f, 0);
            hintRt.anchoredPosition = new Vector2(0, 22);
            hintRt.sizeDelta = new Vector2(0, 20);

            // Selected cases list (occupies the same area when non-empty)
            var selRoot = UIFactory.CreateRectAnchored("SelectedCases", panelRt,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            selRoot.anchoredPosition = new Vector2(0, -64);
            selRoot.sizeDelta = new Vector2(440, 220);
            var selHl = selRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            selHl.childAlignment = TextAnchor.MiddleCenter;
            selHl.spacing = 10f;
            selHl.childForceExpandWidth = selHl.childForceExpandHeight = false;
            selHl.padding = new RectOffset(10, 10, 10, 10);
            refs.SelectedCasesRoot = selRoot.transform;

            // Make the selected-cases area itself a button so the user can tap
            // it to re-open the picker even after AddCasesButton is hidden.
            var editBtn = selRoot.gameObject.AddComponent<Button>();
            editBtn.transition = Selectable.Transition.None;
            refs.EditCasesButton = editBtn;

            // Player count row
            var pcRow = UIFactory.CreateRectAnchored("PlayerCountRow", panelRt,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            pcRow.anchoredPosition = new Vector2(0, 140);
            pcRow.sizeDelta = new Vector2(340, 72);
            var pcHl = pcRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            pcHl.childAlignment = TextAnchor.MiddleCenter;
            pcHl.spacing = 12f;
            pcHl.childForceExpandWidth = pcHl.childForceExpandHeight = false;

            refs.PlayerCountButtons.Clear();
            foreach (var c in new[] { 2, 3, 4 })
            {
                var (btn, bg, ol) = MakeCountChip(pcRow, c.ToString(), 72, 72);
                refs.PlayerCountButtons.Add((btn, c, bg, ol));
            }

            // Total cost & cases labels
            refs.TotalCostLabel = UIFactory.CreateText(panelRt, "TotalCost",
                "TOTAL: (G) 0", 14, TextAlignmentOptions.Center, AccentGreen, FontStyles.Bold);
            var tcRt = refs.TotalCostLabel.rectTransform;
            tcRt.anchorMin = new Vector2(0, 0); tcRt.anchorMax = new Vector2(1, 0);
            tcRt.pivot = new Vector2(0.5f, 0);
            tcRt.anchoredPosition = new Vector2(0, 100);
            tcRt.sizeDelta = new Vector2(0, 20);

            refs.TotalCasesLabel = UIFactory.CreateText(panelRt, "TotalCases",
                "CASES: 0", 11, TextAlignmentOptions.Center, TextDim, FontStyles.Bold);
            var tcaRt = refs.TotalCasesLabel.rectTransform;
            tcaRt.anchorMin = new Vector2(0, 0); tcaRt.anchorMax = new Vector2(1, 0);
            tcaRt.pivot = new Vector2(0.5f, 0);
            tcaRt.anchoredPosition = new Vector2(0, 82);
            tcaRt.sizeDelta = new Vector2(0, 16);

            // CREATE GAME button
            refs.CreateGameButton = MakeCasinoBtn(panelRt, "CreateGameButton",
                "CREATE GAME", 320, 56, AccentPink, AccentPink);
            var cgRt = refs.CreateGameButton.GetComponent<RectTransform>();
            cgRt.anchorMin = new Vector2(0.5f, 0); cgRt.anchorMax = new Vector2(0.5f, 0);
            cgRt.pivot = new Vector2(0.5f, 0);
            cgRt.anchoredPosition = new Vector2(0, 18);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CASE PICKER PANEL — scrollable grid of all cases + DONE
        // ─────────────────────────────────────────────────────────────────────
        static void BuildCasePickerPanel(RectTransform root, CaseBattleUiRefs refs)
        {
            const float topInset    = 72f + 96f + 12f;
            const float bottomInset = 12f;

            var panelRt = UIFactory.CreateRectAnchored("CasePickerPanel", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            panelRt.offsetMin = new Vector2(0, bottomInset);
            panelRt.offsetMax = new Vector2(0, -topInset);
            panelRt.GetComponent<Image>().color = new Color(0.022f, 0.015f, 0.055f, 0.97f);
            refs.CasePickerPanel = panelRt.gameObject;

            // Title
            var titleTmp = UIFactory.CreateText(panelRt, "Title", "SELECT CASES",
                18, TextAlignmentOptions.Center, TextWhite, FontStyles.Bold);
            titleTmp.characterSpacing = 3f;
            var titleRt = titleTmp.rectTransform;
            titleRt.anchorMin = new Vector2(0, 1); titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.anchoredPosition = new Vector2(0, -18);
            titleRt.sizeDelta = new Vector2(0, 28);

            // Scroll
            var scrollGo = new GameObject("PickerScroll",
                typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(panelRt, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(16, 86);
            srt.offsetMax = new Vector2(-16, -56);
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.10f);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.10f; sr.inertia = true;
            sr.decelerationRate = 0.14f; sr.scrollSensitivity = 40f;

            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vprt = (RectTransform)vpGo.transform;
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = vprt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            var contentGo = new GameObject("PickerContent",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            var crt = (RectTransform)contentGo.transform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.anchoredPosition = crt.sizeDelta = Vector2.zero;

            var glg = contentGo.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(180, 220);
            glg.spacing = new Vector2(12, 12);
            glg.padding = new RectOffset(8, 8, 8, 8);
            glg.childAlignment = TextAnchor.UpperCenter;
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 3;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vprt; sr.content = crt;
            refs.CasePickerGridRoot = contentGo.transform;

            // DONE button
            refs.DoneButton = MakeCasinoBtn(panelRt, "DoneButton",
                "DONE", 260, 48, AccentPink, AccentPink);
            var dRt = refs.DoneButton.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0.5f, 0); dRt.anchorMax = new Vector2(0.5f, 0);
            dRt.pivot = new Vector2(0.5f, 0);
            dRt.anchoredPosition = new Vector2(0, 14);
        }

        // ─────────────────────────────────────────────────────────────────────
        // FINAL RESULT POPUP — small modal that appears after battle ends
        // ─────────────────────────────────────────────────────────────────────
        static void BuildFinalPopup(RectTransform root, CaseBattleUiRefs refs)
        {
            // Full-screen dim overlay — blocks clicks on elements behind it
            var overlay = UIFactory.CreateRectAnchored("FinalPopupOverlay", root,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), addImage: true);
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f);
            refs.FinalPopup = overlay.gameObject;

            // Card
            var card = UIFactory.CreateRectAnchored("FinalCard", overlay,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            card.sizeDelta = new Vector2(400, 290);
            card.GetComponent<Image>().color = new Color(0.045f, 0.032f, 0.095f, 0.98f);
            UIFactory.AddColoredBorder(card, AccentPink, top: true,  height: 2f);
            UIFactory.AddColoredBorder(card, BorderPink, top: false, height: 1f);
            UIFactory.AddSideGlow(card, AccentPinkSoft, left: true);
            UIFactory.AddSideGlow(card, AccentPinkSoft, left: false);

            // Top glow strip
            var topGlow = UIFactory.CreateRectAnchored("TopGlow", card,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
            topGlow.sizeDelta = new Vector2(0, 70f);
            topGlow.anchoredPosition = Vector2.zero;
            topGlow.GetComponent<Image>().color = new Color(1f, 0.18f, 0.55f, 0.07f);
            topGlow.GetComponent<Image>().raycastTarget = false;

            // Title label — "YOU WIN!" / "BOT 1 WINS" / "BERABERE"
            refs.FinalPopupTitleLabel = UIFactory.CreateText(card, "Title", "YOU WIN!",
                38, TextAlignmentOptions.Center, AccentGreen, FontStyles.Bold);
            refs.FinalPopupTitleLabel.characterSpacing = 2f;
            var tRt = refs.FinalPopupTitleLabel.rectTransform;
            tRt.anchorMin = new Vector2(0, 1); tRt.anchorMax = new Vector2(1, 1);
            tRt.pivot     = new Vector2(0.5f, 1);
            tRt.anchoredPosition = new Vector2(0, -36);
            tRt.sizeDelta = new Vector2(0, 50);

            // Body label — draw-only secondary line (e.g. "Bakiye iade edildi"), hidden by default
            refs.FinalPopupBodyLabel = UIFactory.CreateText(card, "Body", "",
                13, TextAlignmentOptions.Center, TextWhite, FontStyles.Normal);
            var bRt = refs.FinalPopupBodyLabel.rectTransform;
            bRt.anchorMin = new Vector2(0, 1); bRt.anchorMax = new Vector2(1, 1);
            bRt.pivot     = new Vector2(0.5f, 1);
            bRt.anchoredPosition = new Vector2(0, -94);
            bRt.sizeDelta = new Vector2(0, 22);
            refs.FinalPopupBodyLabel.gameObject.SetActive(false);

            // Total / refund label
            refs.FinalPopupTotalLabel = UIFactory.CreateText(card, "Total", "Total: (G) 0",
                15, TextAlignmentOptions.Center, TextDim, FontStyles.Normal);
            var totRt = refs.FinalPopupTotalLabel.rectTransform;
            totRt.anchorMin = new Vector2(0, 1); totRt.anchorMax = new Vector2(1, 1);
            totRt.pivot     = new Vector2(0.5f, 1);
            totRt.anchoredPosition = new Vector2(0, -124);
            totRt.sizeDelta = new Vector2(0, 24);

            // Button row — TEKRAR OYNA (draw-only, hidden) + TAMAM/OK (always visible)
            var btnRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            btnRowGo.transform.SetParent(card, false);
            var btnRowRt = (RectTransform)btnRowGo.transform;
            btnRowRt.anchorMin = new Vector2(0.5f, 0); btnRowRt.anchorMax = new Vector2(0.5f, 0);
            btnRowRt.pivot = new Vector2(0.5f, 0);
            btnRowRt.anchoredPosition = new Vector2(0, 16);
            btnRowRt.sizeDelta = new Vector2(384, 52);
            var rowHl = btnRowGo.AddComponent<HorizontalLayoutGroup>();
            rowHl.childAlignment = TextAnchor.MiddleCenter;
            rowHl.spacing = 12f;
            rowHl.childForceExpandWidth = rowHl.childForceExpandHeight = false;

            // "TEKRAR OYNA" — shown only on draw; goes back to setup
            refs.FinalPopupPlayAgainButton = MakeCasinoBtn(btnRowGo.transform,
                "PlayAgainBtn", "TEKRAR OYNA", 196, 52,
                new Color(0.06f, 0.04f, 0.12f, 0.95f), AccentOrange);
            refs.FinalPopupPlayAgainButton.gameObject.SetActive(false);

            // "TAMAM" / "OK" — always present; just closes the popup
            refs.FinalPopupOkButton = MakeCasinoBtn(btnRowGo.transform,
                "OkButton", "TAMAM", 172, 52, AccentPink, AccentPink);

            // CanvasGroup — belt-and-suspenders: blocksRaycasts stays false when hidden
            // so the overlay can NEVER intercept clicks on panels behind it.
            var cg = overlay.gameObject.AddComponent<CanvasGroup>();
            cg.alpha          = 0f;
            cg.blocksRaycasts = false;
            cg.interactable   = false;
            refs.FinalPopupCanvasGroup = cg;

            overlay.gameObject.SetActive(false);
        }
    }
}
