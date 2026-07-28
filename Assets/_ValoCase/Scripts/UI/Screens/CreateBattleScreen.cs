using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Progression;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Create Battle — full-screen modal overlay hosted by LobbyListScreen.
    ///
    /// Layout is split into disjoint anchored bands so sections can never overlap at any
    /// scale: header (top), a bounded scrolling case grid (flex middle), a bounded
    /// selected-cases panel and the mode + CREATE action block (both pinned above the
    /// bottom). Pick up to 4 cases (each opened 1..5 times), a battle type, then create.
    /// Entry cost = sum(case price × quantity); the server is authoritative for the charge.
    /// </summary>
    public sealed class CreateBattleScreen : MonoBehaviour
    {
        public event Action OnBack;
        public event Action<BattleLobbyData> OnConfirm;

        const float SidePad    = 16f;
        const float HeaderH    = 60f;
        const float SelLabelH  = 18f;
        const float TypeH      = 48f;
        const float CtaH       = 62f;
        const float RowGap     = 10f;
        const float BotPad     = 28f;

        const float SelRowH     = 70f;
        const float SelRowGap   = 10f;
        const float SelEmptyH   = 28f;
        // Tall enough to show every selectable case at once; the inner ScrollRect only
        // engages if rows ever exceed this, and the body scroll reaches it on short screens.
        const float MaxSelectedH = SelRowH * MaxCases + SelRowGap * (MaxCases - 1);

        const int   MaxCases = 5;
        const int   MinQty   = 1;
        const int   MaxQty   = 5;

        readonly struct CaseOption
        {
            public readonly string CaseId;
            public readonly string Name;
            public readonly int    Price;
            public readonly Sprite Icon;
            public readonly bool   Locked;
            public readonly int    RequiredLevel;
            public CaseOption(string caseId, string name, int price, Sprite icon, bool locked, int requiredLevel)
            { CaseId = caseId; Name = name; Price = price; Icon = icon; Locked = locked; RequiredLevel = requiredLevel; }
        }

        bool _built;
        bool _anyUnlocked;

        readonly List<CaseOption> _cases = new();
        readonly List<(AngledCutImage bg, Outline border, GameObject badge, TextMeshProUGUI badgeLbl)> _caseCards = new();
        readonly List<(Image bg, TextMeshProUGUI lbl, BattlePlayerCount pc)> _typeBtns = new();

        readonly Dictionary<int, int> _selection = new();   // case index → opens (1..5)

        BattlePlayerCount _playerCount = BattlePlayerCount.OneVOne;

        RectTransform   _selectedRoot;
        LayoutElement   _selectedFrameLe;
        AngledCutImage  _ctaBg;
        Button          _ctaButton;
        TextMeshProUGUI _ctaLbl;
        bool            _confirmInFlight;

        GridLayoutGroup _caseGrid;
        LayoutElement   _gridFrameLe;

        const int   CaseCols    = 4;       // target: 4 per row → each weapon is one clean row
        const int   MinCaseCols = 3;       // last-resort fallback for very narrow screens
        const float CaseGap     = 10f;
        const float CaseAspect  = 122f / 108f;
        const float MaxCaseCellW = 230f;   // caps card growth so wide rows stay tidy, not huge
        const float MinCaseCellW = 120f;   // below this at 4 cols, fall back to 3 cols
        const float MinCaseGap   = 6f;     // gap shrinks before cells get tiny on narrow screens
        const float GridVisibleRows = 2.5f; // case grid viewport caps at this many visible rows, rest scrolls

        // Create Battle case selection order: weapon category first, then tier — so all 4
        // cases of a weapon sit together, mirroring the Cases menu grouping. Categories and
        // tiers are keyed off the caseId ("{category}_{tier}", e.g. "classic_radiant").
        // Unknown categories/tiers fall to the end, preserving catalog order.
        static readonly string[] CategoryOrder = { "classic", "ghost", "bulldog", "vandal", "melee" };
        static readonly string[] TierOrder     = { "basic", "protocol", "radiant", "arcane" };

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public void Show()
        {
            gameObject.SetActive(true);
            BuildOnce();

            SetConfirmInFlight(false);
            _playerCount = BattlePlayerCount.OneVOne;
            ResetSelectionDefault();
            RefreshAll();

            var rt = (RectTransform)transform;
            rt.anchoredPosition = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            StartCoroutine(SizeCaseGrid());
            StartCoroutine(UIAnimator.SlideFromBottom(rt, 0.24f));
        }

        public void Hide()
        {
            SetConfirmInFlight(false);
            StopAllCoroutines();
            gameObject.SetActive(false);
        }

        void OnDisable()
        {
            _confirmInFlight = false;
            if (_ctaButton != null) _ctaButton.interactable = true;
            StopAllCoroutines();
        }

        public void SetConfirmInFlight(bool inFlight)
        {
            _confirmInFlight = inFlight;
            if (_ctaButton != null) _ctaButton.interactable = !inFlight;
        }

        // ── Build ──────────────────────────────────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            LoadCases();

            var rt = (RectTransform)transform;

            var scrim = MakeImage("Scrim", rt, ColorPalette.WithAlpha(ColorPalette.BgDeep, 0.97f), raycast: true);
            Stretch(scrim.rectTransform);

            BuildHeader(rt);
            BuildBody(rt);
        }

        // Header stays fixed at the top; everything else lives in one bounded vertical
        // scroll so the selected list, mode pills and CREATE button are always reachable
        // above the navbar at any height. The case grid is capped to ~GridVisibleRows and
        // scrolls independently inside that outer scroll so a long case list can't push
        // the rest of the panel off-screen.
        void BuildBody(RectTransform rt)
        {
            var scrollGo = NewGo("BodyScroll", rt, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;
            var sRt = (RectTransform)scrollGo.transform;
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(0f, 0f);
            sRt.offsetMax = new Vector2(0f, -HeaderH);

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            // Modal dismiss: tapping the empty area below/around the content closes the
            // panel. Interactive children (cards, steppers, mode pills, CREATE) consume
            // their own taps, and ScrollRect drags are unaffected, so only empty-space
            // taps reach this catcher.
            var dismiss = viewport.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => OnBack?.Invoke());

            var content = NewGo("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot     = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = Vector2.zero;   // width tracks the viewport; CSF drives height only

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding                = new RectOffset((int)SidePad, (int)SidePad, 8, (int)BotPad);
            vlg.spacing                = RowGap;
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content           = cRt;
            sr.viewport          = (RectTransform)viewport.transform;
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.movementType      = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            BuildGridSection(content.transform);
            BuildSelectedSection(content.transform);
            BuildActionBlock(content.transform);
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.CardBg, raycast: true);
            TopStrip(hdr.rectTransform, HeaderH);

            var accent = MakeImage("TopAccent", hdr.transform, ColorPalette.ActiveRed);
            accent.raycastTarget = false;
            TopStrip(accent.rectTransform, 2f);

            var handle = MakeImage("Handle", hdr.transform, ColorPalette.WithAlpha(ColorPalette.TextDim, 0.6f));
            handle.raycastTarget = false;
            SetRect(handle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(40f, 4f));

            var border = MakeImage("BottomBorder", hdr.transform, ColorPalette.Border);
            border.raycastTarget = false;
            BottomStrip(border.rectTransform, 1f);

            // No close/X control — the back button (bottom-left) is the only exit, so the
            // title reclaims the right side that used to be reserved for it.
            var title = MakeTmp(hdr.transform, "Title", "CREATE BATTLE", 18f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0.5f, 0.5f);
            titleRt.offsetMin = new Vector2(60f, 0f);
            titleRt.offsetMax = new Vector2(-SidePad, -4f);

            var back = NewGo("Back", hdr.transform, typeof(Image), typeof(Button));
            back.GetComponent<Image>().color = ColorPalette.Surface;
            var backBorder = back.AddComponent<Outline>();
            backBorder.effectColor = ColorPalette.Border; backBorder.effectDistance = new Vector2(1f, -1f);
            SetRect((RectTransform)back.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(SidePad, -2f), new Vector2(40f, 40f));
            var backLbl = MakeTmp(back.transform, "Lbl", "", 20f, FontStyles.Bold, ColorPalette.TextBright);
            Stretch(backLbl.rectTransform);
            StyleCircleBack(back.GetComponent<Image>(), backLbl, 40f);
            var backBtn = back.GetComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(() => OnBack?.Invoke());
        }

        // ── Case grid — bounded frame (~GridVisibleRows tall) that scrolls its own
        // content independently, so the case list never pushes the selected panel,
        // mode pills and CREATE button far down the shared body scroll.
        void BuildGridSection(Transform parent)
        {
            var frameGo = NewGo("GridFrame", parent, typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
            frameGo.GetComponent<Image>().color = Color.clear;
            _gridFrameLe = frameGo.GetComponent<LayoutElement>();

            var viewport = NewGo("Viewport", frameGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var gridGo = NewGo("Grid", viewport.transform, typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var gRt = (RectTransform)gridGo.transform;
            gRt.anchorMin = new Vector2(0f, 1f);
            gRt.anchorMax = new Vector2(1f, 1f);
            gRt.pivot     = new Vector2(0.5f, 1f);
            gRt.anchoredPosition = Vector2.zero;
            gRt.sizeDelta = Vector2.zero;

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(84f, 84f * CaseAspect);
            grid.spacing         = new Vector2(CaseGap, CaseGap);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = CaseCols;
            grid.childAlignment  = TextAnchor.UpperCenter;
            grid.padding         = new RectOffset(0, 0, 0, 6);
            _caseGrid = grid;
            _gridFrameLe.preferredHeight = GridVisibleRows * grid.cellSize.y + (GridVisibleRows - 1f) * CaseGap;

            var fitter = gridGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var sr = frameGo.GetComponent<ScrollRect>();
            sr.content           = gRt;
            sr.viewport          = (RectTransform)viewport.transform;
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.movementType      = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;

            for (int i = 0; i < _cases.Count; i++)
                BuildCaseCard(gridGo.transform, i);
        }

        // Sizes the grid cells from the screen width so all four columns fit; the
        // ContentSizeFitter then drives the grid height inside the body scroll.
        System.Collections.IEnumerator SizeCaseGrid()
        {
            if (_caseGrid == null) yield break;

            yield return null;
            Canvas.ForceUpdateCanvases();
            for (int g = 0; g < 8 && ((RectTransform)transform).rect.width < 1f; g++) yield return null;

            if (RefitGridCells())
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        // Derives cell size from the root (screen) width — reliable inside
        // OnRectTransformDimensionsChange, unlike the grid's own mid-layout rect.
        // Prefer 4 columns so each weapon's four cases form one row; only drop to 3 on
        // screens too narrow to keep 4 cards readable. Gap shrinks before the cell does,
        // and the cell is capped so rows stay tidy (not huge) on wide screens.
        bool RefitGridCells()
        {
            if (_caseGrid == null) return false;
            float rootW = ((RectTransform)transform).rect.width;
            if (rootW < 1f) return false;

            float innerW = rootW - SidePad * 2f;
            if (innerW < CaseCols) return false;

            // Try the target 4 columns; fall back to 3 only when 4 can't stay readable.
            int cols = CaseCols;
            if (!FitColumns(innerW, CaseCols, out float gap, out float cellW) || cellW < MinCaseCellW)
            {
                if (FitColumns(innerW, MinCaseCols, out float gap3, out float cellW3) && cellW3 >= 1f)
                {
                    cols  = MinCaseCols;
                    gap   = gap3;
                    cellW = cellW3;
                }
            }
            if (cellW < 1f) return false;
            cellW = Mathf.Min(cellW, MaxCaseCellW);

            float cellH = Mathf.Floor(cellW * CaseAspect);
            _caseGrid.constraintCount = cols;
            _caseGrid.spacing  = new Vector2(gap, gap);
            _caseGrid.cellSize = new Vector2(cellW, cellH);
            if (_gridFrameLe != null)
                _gridFrameLe.preferredHeight = GridVisibleRows * cellH + (GridVisibleRows - 1f) * gap;
            return true;
        }

        // Computes the gap (full, tightening to the minimum) and the resulting cell width
        // for a given column count across the available inner width.
        static bool FitColumns(float innerW, int cols, out float gap, out float cellW)
        {
            gap = CaseGap;
            cellW = Mathf.Floor((innerW - gap * (cols - 1)) / cols);
            if (cellW < MaxCaseCellW)   // tighten the gap before letting cells shrink further
            {
                gap = MinCaseGap;
                cellW = Mathf.Floor((innerW - gap * (cols - 1)) / cols);
            }
            return cellW >= 1f;
        }

        void OnRectTransformDimensionsChange()
        {
            if (_built) RefitGridCells();
        }

        void BuildCaseCard(Transform parent, int index)
        {
            var opt = _cases[index];

            var cardGo = NewGo("Case_" + index, parent, typeof(Button));
            var bg = MakeAngled("Bg", cardGo.transform, ColorPalette.CardBg, 8f, raycast: true);
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            if (opt.Icon != null)
            {
                var icon = MakeImage("Thumb", cardGo.transform, Color.white);
                icon.sprite = opt.Icon;
                icon.preserveAspect = true;
                var iRt = icon.rectTransform;
                iRt.anchorMin = new Vector2(0f, 0f);
                iRt.anchorMax = new Vector2(1f, 1f);
                iRt.offsetMin = new Vector2(8f, 72f);
                iRt.offsetMax = new Vector2(-8f, -8f);
            }

            var name = MakeTmp(cardGo.transform, "Name", opt.Name, 15f, FontStyles.Bold, ColorPalette.TextBright);
            name.alignment = TextAlignmentOptions.Top;
            name.enableWordWrapping = true;
            name.enableAutoSizing = true;
            name.fontSizeMin = 11f;
            name.fontSizeMax = 15f;
            SetRect(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 30f), new Vector2(-12f, 36f));

            var price = MakeTmp(cardGo.transform, "Price", opt.Price.ToString("N0") + " VP", 14.5f, FontStyles.Bold, ColorPalette.GoldAccent);
            price.alignment = TextAlignmentOptions.Center;
            SetRect(price.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(-8f, 20f));

            var badge = MakeAngled("Badge", cardGo.transform, ColorPalette.ActiveRed, 3f, raycast: false);
            SetRect(badge.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-5f, -5f), new Vector2(32f, 22f));
            var badgeLbl = MakeTmp(badge.transform, "Lbl", "x1", 12f, FontStyles.Bold, Color.white);
            badgeLbl.alignment = TextAlignmentOptions.Center;
            badgeLbl.raycastTarget = false;
            Stretch(badgeLbl.rectTransform);
            badge.gameObject.SetActive(false);

            // Locked categories stay visible but cannot be selected for creation — dim
            // overlay + required-level label; tapping shows the unlock toast.
            if (opt.Locked)
            {
                var lockOv = MakeImage("Lock", cardGo.transform, ColorPalette.WithAlpha(Color.black, 0.62f));
                Stretch(lockOv.rectTransform);
                lockOv.raycastTarget = false;

                var lockLbl = MakeTmp(cardGo.transform, "LockLvl", "LEVEL " + opt.RequiredLevel,
                    10f, FontStyles.Bold, ColorPalette.GoldAccent);
                lockLbl.alignment = TextAlignmentOptions.Center;
                lockLbl.raycastTarget = false;
                SetRect(lockLbl.rectTransform, new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(0f, 18f));
            }

            int captured = index;
            var btn = cardGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => ToggleCase(captured));

            _caseCards.Add((bg, border, badge.gameObject, badgeLbl));
        }

        // ── Selected cases — content-sized list inside the shared body scroll ───────
        void BuildSelectedSection(Transform parent)
        {
            var panelGo = NewGo("SelectedPanel", parent,
                typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.GetComponent<Image>().color = ColorPalette.WithAlpha(ColorPalette.Surface, 0.30f);

            // A tap anywhere inside this panel (rows, name, icon, gaps, the SELECTED
            // header) must be consumed here so it never bubbles up to the body's
            // empty-space dismiss button. Real controls inside keep their own clicks.
            var blocker = panelGo.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;

            var pBorder = panelGo.AddComponent<Outline>();
            pBorder.effectColor = ColorPalette.Border; pBorder.effectDistance = new Vector2(1f, -1f);

            var pv = panelGo.GetComponent<VerticalLayoutGroup>();
            pv.padding                 = new RectOffset(10, 10, 8, 10);
            pv.spacing                 = 6f;
            pv.childForceExpandWidth   = true;
            pv.childForceExpandHeight  = false;
            pv.childControlWidth       = true;
            pv.childControlHeight      = true;
            panelGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var label = MakeTmp(panelGo.transform, "SelectedLabel", "SELECTED", 12f, FontStyles.Bold, ColorPalette.ActiveRed);
            label.characterSpacing = 4f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.gameObject.AddComponent<LayoutElement>().minHeight = SelLabelH;

            // Bounded frame: height is set from the row count (capped at MaxSelectedH),
            // so the section is compact and scrolls only when the rows exceed the cap.
            var frameGo = NewGo("SelectedFrame", panelGo.transform, typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
            frameGo.GetComponent<Image>().color = Color.clear;
            _selectedFrameLe = frameGo.GetComponent<LayoutElement>();

            var viewport = NewGo("Viewport", frameGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var listGo = NewGo("SelectedList", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var lRt = (RectTransform)listGo.transform;
            lRt.anchorMin = new Vector2(0f, 1f);
            lRt.anchorMax = new Vector2(1f, 1f);
            lRt.pivot     = new Vector2(0.5f, 1f);
            lRt.anchoredPosition = Vector2.zero;
            lRt.sizeDelta = Vector2.zero;
            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing                = SelRowGap;
            vlg.childForceExpandWidth   = true;
            vlg.childForceExpandHeight  = false;
            vlg.childControlWidth       = true;
            vlg.childControlHeight      = true;
            listGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _selectedRoot = lRt;

            // The frame masks the rows but never scrolls them itself — the rows always fit
            // (MaxSelectedH holds every selectable case). Disabled so its drag handling can
            // never swallow a swipe meant for the body scroll, leaving CREATE unreachable.
            var sr = frameGo.GetComponent<ScrollRect>();
            sr.content      = lRt;
            sr.viewport     = (RectTransform)viewport.transform;
            sr.horizontal   = false;
            sr.vertical     = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.enabled      = false;
        }

        void RebuildSelectedRows()
        {
            if (_selectedRoot == null) return;

            for (int i = _selectedRoot.childCount - 1; i >= 0; i--)
                Destroy(_selectedRoot.GetChild(i).gameObject);

            if (_selection.Count == 0)
            {
                var empty = MakeTmp(_selectedRoot, "Empty", "Select up to 4 cases above", 11f, FontStyles.Normal, ColorPalette.TextDim);
                empty.alignment = TextAlignmentOptions.MidlineLeft;
                empty.gameObject.AddComponent<LayoutElement>().minHeight = SelEmptyH;
                if (_selectedFrameLe != null) _selectedFrameLe.preferredHeight = SelEmptyH;
                return;
            }

            for (int i = 0; i < _cases.Count; i++)
                if (_selection.TryGetValue(i, out var qty))
                    BuildSelectedRow(i, qty);

            int rows = _selection.Count;
            float needed = rows * SelRowH + (rows - 1) * SelRowGap;
            if (_selectedFrameLe != null) _selectedFrameLe.preferredHeight = Mathf.Min(needed, MaxSelectedH);
        }

        void BuildSelectedRow(int index, int qty)
        {
            var opt = _cases[index];

            var row = NewGo("Sel_" + index, _selectedRoot, typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.GetComponent<Image>().color = ColorPalette.CardBg;
            var rb = row.AddComponent<Outline>();
            rb.effectColor = ColorPalette.Border; rb.effectDistance = new Vector2(1f, -1f);
            var rowLe = row.GetComponent<LayoutElement>();
            rowLe.minHeight = rowLe.preferredHeight = SelRowH;

            var rh = row.GetComponent<HorizontalLayoutGroup>();
            rh.padding                 = new RectOffset(10, 10, 8, 8);
            rh.spacing                 = 10f;
            rh.childAlignment          = TextAnchor.MiddleLeft;
            rh.childForceExpandWidth   = false;
            rh.childForceExpandHeight  = true;
            rh.childControlWidth       = true;
            rh.childControlHeight      = true;

            if (opt.Icon != null)
            {
                var icon = MakeImage("Icon", row.transform, Color.white);
                icon.sprite = opt.Icon; icon.preserveAspect = true; icon.raycastTarget = false;
                var iLe = icon.gameObject.AddComponent<LayoutElement>();
                iLe.minWidth = iLe.preferredWidth = 54f;
            }

            var midGo = NewGo("Mid", row.transform, typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var midLe = midGo.GetComponent<LayoutElement>();
            midLe.flexibleWidth = 1f; midLe.minWidth = 40f;
            var midV = midGo.GetComponent<VerticalLayoutGroup>();
            midV.spacing = 2f; midV.childAlignment = TextAnchor.MiddleLeft;
            midV.childForceExpandWidth = true; midV.childForceExpandHeight = false;
            midV.childControlWidth = true; midV.childControlHeight = true;

            var name = MakeTmp(midGo.transform, "Name", opt.Name, 18f, FontStyles.Bold, ColorPalette.TextBright);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.enableWordWrapping = false; name.overflowMode = TextOverflowModes.Ellipsis;
            name.gameObject.AddComponent<LayoutElement>().minHeight = 24f;

            var lineCost = MakeTmp(midGo.transform, "LineCost", (opt.Price * qty).ToString("N0") + " VP",
                15f, FontStyles.Bold, ColorPalette.GoldAccent);
            lineCost.alignment = TextAlignmentOptions.MidlineLeft;
            lineCost.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

            int captured = index;

            var stepGo = NewGo("Stepper", row.transform, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var stepLe = stepGo.GetComponent<LayoutElement>();
            stepLe.minWidth = stepLe.preferredWidth = 158f;
            var sh = stepGo.GetComponent<HorizontalLayoutGroup>();
            sh.padding = new RectOffset(2, 2, 0, 0);
            sh.spacing = 8f; sh.childAlignment = TextAnchor.MiddleCenter;
            sh.childForceExpandWidth = false; sh.childForceExpandHeight = false;
            sh.childControlWidth = true; sh.childControlHeight = true;

            MakeMiniStep(stepGo.transform, "−", () => ChangeQty(captured, -1));

            var qLbl = MakeTmp(stepGo.transform, "Q", qty.ToString(), 22f, FontStyles.Bold, ColorPalette.TextBright);
            qLbl.alignment = TextAlignmentOptions.Center;
            var qLe = qLbl.gameObject.AddComponent<LayoutElement>();
            qLe.minWidth = qLe.preferredWidth = 46f;
            qLe.minHeight = qLe.preferredHeight = 46f;

            MakeMiniStep(stepGo.transform, "+", () => ChangeQty(captured, +1));
        }

        void MakeMiniStep(Transform parent, string glyph, Action onClick)
        {
            var go = NewGo("Step_" + glyph, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<Image>().color = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.95f);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 46f;
            le.minHeight = le.preferredHeight = 46f;

            const float borderW = 3f;
            var fill = MakeImage("Fill", go.transform, ColorPalette.Surface);
            var fillRt = fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(borderW, borderW);
            fillRt.offsetMax = new Vector2(-borderW, -borderW);

            var lbl = MakeTmp(go.transform, "Lbl", glyph, 28f, FontStyles.Bold, ColorPalette.ActiveRed);
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
        }

        // ── Action block — mode pills + CREATE, last rows of the body scroll ─────────
        void BuildActionBlock(Transform parent)
        {
            var typeGo = NewGo("TypeRow", parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            typeGo.GetComponent<LayoutElement>().minHeight = TypeH;
            var hlg = typeGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 10f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            _typeBtns.Add(BuildTypePill(typeGo.transform, "1V1",     BattlePlayerCount.OneVOne));
            _typeBtns.Add(BuildTypePill(typeGo.transform, "1V1V1",   BattlePlayerCount.ThreePlayer));
            _typeBtns.Add(BuildTypePill(typeGo.transform, "1V1V1V1", BattlePlayerCount.FourPlayer));

            _ctaBg = MakeAngled("Cta", parent, ColorPalette.ActiveRed, 10f, raycast: true);
            _ctaBg.gameObject.AddComponent<LayoutElement>().minHeight = CtaH;

            var glow = _ctaBg.gameObject.AddComponent<Shadow>();
            glow.effectColor = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.7f);
            glow.effectDistance = new Vector2(0f, -4f);

            _ctaButton = _ctaBg.gameObject.AddComponent<Button>();
            _ctaButton.transition = Selectable.Transition.None;
            _ctaButton.onClick.AddListener(OnConfirmPressed);

            _ctaLbl = MakeTmp(_ctaBg.transform, "Lbl", "CREATE BATTLE", 15f, FontStyles.Bold, Color.white);
            _ctaLbl.characterSpacing = 1f;
            _ctaLbl.alignment = TextAlignmentOptions.Center;
            Stretch(_ctaLbl.rectTransform);
        }

        (Image bg, TextMeshProUGUI lbl, BattlePlayerCount pc) BuildTypePill(Transform parent, string text, BattlePlayerCount pc)
        {
            var go = NewGo("Type_" + text, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().minHeight = TypeH;
            var img = go.GetComponent<Image>();
            img.color = ColorPalette.Surface;
            var bdr = go.AddComponent<Outline>();
            bdr.effectColor = ColorPalette.Border; bdr.effectDistance = new Vector2(1f, -1f);

            var lbl = MakeTmp(go.transform, "Lbl", text, 15f, FontStyles.Bold, ColorPalette.TextBright);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.characterSpacing = 2f;
            Stretch(lbl.rectTransform);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { _playerCount = pc; RefreshAll(); });
            return (img, lbl, pc);
        }

        // ── Selection state ────────────────────────────────────────────────────────
        void ToggleCase(int index)
        {
            if (index < 0 || index >= _cases.Count) return;

            if (_cases[index].Locked)
            {
                GameEvents.RaiseToast($"This case unlocks at level {_cases[index].RequiredLevel}");
                return;
            }

            if (_selection.ContainsKey(index))
            {
                _selection.Remove(index);
            }
            else
            {
                if (_selection.Count >= MaxCases)
                {
                    GameEvents.RaiseToast($"You can select up to {MaxCases} cases");
                    return;
                }
                _selection[index] = MinQty;
            }

            RefreshAll();
        }

        void ChangeQty(int index, int delta)
        {
            if (!_selection.TryGetValue(index, out var q)) return;
            int next = q + delta;
            if (next < MinQty) _selection.Remove(index);
            else _selection[index] = Mathf.Min(next, MaxQty);
            RefreshAll();
        }

        void RefreshAll()
        {
            for (int i = 0; i < _caseCards.Count; i++)
            {
                bool sel = _selection.ContainsKey(i);
                var (bg, border, badge, badgeLbl) = _caseCards[i];
                border.effectColor    = sel ? ColorPalette.ActiveRed : ColorPalette.Border;
                border.effectDistance = sel ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                bg.color              = sel ? ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.16f) : ColorPalette.CardBg;
                if (badge != null) badge.SetActive(sel);
                if (sel && badgeLbl != null) badgeLbl.text = "x" + _selection[i];
            }

            for (int i = 0; i < _typeBtns.Count; i++)
            {
                bool active = _typeBtns[i].pc == _playerCount;
                _typeBtns[i].bg.color  = active ? ColorPalette.ActiveRed : ColorPalette.Surface;
                _typeBtns[i].lbl.color = active ? Color.white : ColorPalette.TextBright;
                var ol = _typeBtns[i].bg.GetComponent<Outline>();
                if (ol != null) ol.effectColor = active ? ColorPalette.ActiveRed : ColorPalette.Border;
            }

            RebuildSelectedRows();

            int cost = CurrentCost();
            if (_ctaLbl != null)
                _ctaLbl.text = !_anyUnlocked
                    ? "NO CASES UNLOCKED"
                    : _selection.Count == 0
                        ? "SELECT A CASE"
                        : "CREATE BATTLE  -  " + cost.ToString("N0") + " VP";

            WireButtonClicks(gameObject);
        }

        int CurrentCost()
        {
            int total = 0;
            foreach (var kv in _selection)
                total += _cases[kv.Key].Price * kv.Value;
            return total;
        }

        // ── Confirm ──────────────────────────────────────────────────────────────
        void OnConfirmPressed()
        {
            if (_confirmInFlight) return;

            if (!_anyUnlocked)
            {
                GameEvents.RaiseToast("No cases unlocked — level up");
                return;
            }

            if (_selection.Count == 0)
            {
                GameEvents.RaiseToast("Please select at least one case");
                return;
            }

            var selections = new List<BattleCaseSelection>();
            int totalOpens = 0;
            for (int i = 0; i < _cases.Count; i++)
            {
                if (!_selection.TryGetValue(i, out var qty)) continue;
                var opt = _cases[i];
                // Revalidate live against current progression before the POST.
                if (!CaseUnlocked(opt.CaseId))
                {
                    GameEvents.RaiseToast($"This case unlocks at level {PlayerProgression.RequiredLevelForCaseId(opt.CaseId)}");
                    return;
                }
                selections.Add(new BattleCaseSelection(opt.CaseId, opt.Name, qty, opt.Price));
                totalOpens += qty;
            }

            StartCoroutine(UIAnimator.ScalePress(_ctaBg.transform, 0.97f, 0.12f));

            var data = new BattleLobbyData
            {
                LobbyId        = GenId(),
                HostName       = "YOU",
                CaseId         = selections[0].CaseId,
                CaseName       = selections[0].CaseName,
                CaseSelections = selections,
                Rounds         = totalOpens,
                Mode           = BattleMode.Normal,
                PlayerCount    = _playerCount,
                CurrentPlayers = 1,
                Status         = LobbyStatus.Waiting,
                Rarity         = SkinRarity.Select,
                WagerVP        = CurrentCost(),
            };
            SetConfirmInFlight(true);
            OnConfirm?.Invoke(data);
        }

        // ── Case loading ───────────────────────────────────────────────────────────
        void LoadCases()
        {
            _cases.Clear();
            var cases = GameContext.Instance?.Content?.Cases;
            if (cases != null)
            {
                foreach (var c in cases)
                    if (c != null)
                    {
                        bool unlocked = CaseUnlocked(c.CaseId);
                        int  required = PlayerProgression.RequiredLevelForCaseId(c.CaseId);
                        _cases.Add(new CaseOption(c.CaseId, c.DisplayName, c.VpPrice, c.CaseIcon, !unlocked, required));
                    }
            }

            if (_cases.Count == 0)
                _cases.Add(new CaseOption("vandal_basic", "Basic Vandal Case", 500, null, false, 1));

            ApplyCategoryOrder();

            _anyUnlocked = false;
            foreach (var opt in _cases)
                if (!opt.Locked) { _anyUnlocked = true; break; }
        }

        // Stable reorder so the grid reads Classic → Ghost → Bulldog → Vandal → Melee, and
        // within each weapon the four cases read Basic → Protocol → Radiant → Arcane. Cases
        // with an unknown tier trail their category; unknown categories trail the whole list.
        void ApplyCategoryOrder()
        {
            if (_cases.Count < 2) return;

            var ordered = new List<CaseOption>(_cases.Count);
            var taken   = new bool[_cases.Count];

            for (int cat = 0; cat < CategoryOrder.Length; cat++)
            {
                for (int tier = 0; tier < TierOrder.Length; tier++)
                    for (int i = 0; i < _cases.Count; i++)
                        if (!taken[i] && CategoryRank(_cases[i].CaseId) == cat && TierRank(_cases[i].CaseId) == tier)
                        { ordered.Add(_cases[i]); taken[i] = true; }

                // Same-category cases with an unrecognised tier keep their catalog order.
                for (int i = 0; i < _cases.Count; i++)
                    if (!taken[i] && CategoryRank(_cases[i].CaseId) == cat)
                    { ordered.Add(_cases[i]); taken[i] = true; }
            }

            // Unknown categories trail at the end, preserving catalog order.
            for (int i = 0; i < _cases.Count; i++)
                if (!taken[i]) ordered.Add(_cases[i]);

            _cases.Clear();
            _cases.AddRange(ordered);
        }

        static int CategoryRank(string caseId)
        {
            if (!string.IsNullOrEmpty(caseId))
                for (int i = 0; i < CategoryOrder.Length; i++)
                    if (caseId.StartsWith(CategoryOrder[i], StringComparison.OrdinalIgnoreCase))
                        return i;
            return CategoryOrder.Length;
        }

        static int TierRank(string caseId)
        {
            if (!string.IsNullOrEmpty(caseId))
            {
                int us = caseId.IndexOf('_');
                string tier = (us >= 0 && us < caseId.Length - 1) ? caseId.Substring(us + 1) : caseId;
                for (int i = 0; i < TierOrder.Length; i++)
                    if (tier.Equals(TierOrder[i], StringComparison.OrdinalIgnoreCase))
                        return i;
            }
            return TierOrder.Length;
        }

        // The creator can only ever send a case the backend account has unlocked; in
        // backend mode unknown progression fails safe to locked (see PlayerProgression).
        static bool CaseUnlocked(string caseId)
        {
            bool backend = GameContext.Instance != null && GameContext.Instance.BackendEnabled;
            return backend
                ? PlayerProgression.IsCaseUnlockedAuthoritative(caseId)
                : PlayerProgression.IsCaseUnlocked(caseId);
        }

        void ResetSelectionDefault()
        {
            _selection.Clear();
            for (int i = 0; i < _cases.Count; i++)
                if (!_cases[i].Locked) { _selection[i] = MinQty; break; }
        }

        static string GenId()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
            var arr = new char[4];
            for (int i = 0; i < 4; i++) arr[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            return new string(arr);
        }
    }
}
