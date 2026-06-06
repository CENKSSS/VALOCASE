using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Create Battle — full-screen modal overlay hosted by LobbyListScreen.
    ///
    /// Full-screen (rather than a partial bottom-sheet) because the persistent bottom
    /// nav renders above every screen; a partial sheet would sit under it. The sheet
    /// identity is kept via a top drag handle, red accent line and slide-up entrance.
    ///
    /// Flow: pick a case (grid, 3 per row), battle type (1v1 / 1v1v1) and rounds, then
    /// CREATE BATTLE → emits OnConfirm(BattleLobbyData) into the existing battle flow.
    /// Cost = case price × rounds, shown live on the CTA. Logic systems are untouched.
    /// </summary>
    public sealed class CreateBattleScreen : MonoBehaviour
    {
        public event Action OnBack;
        public event Action<BattleLobbyData> OnConfirm;

        const float SidePad    = 16f;
        const float HeaderH    = 60f;
        const float CtaH       = 54f;
        const float NavReserve = 98f;
        const int   MinRounds  = 1;
        const int   MaxRounds  = 5;

        readonly struct CaseOption
        {
            public readonly string Name;
            public readonly int    Price;
            public readonly Sprite Icon;
            public CaseOption(string name, int price, Sprite icon) { Name = name; Price = price; Icon = icon; }
        }

        bool _built;

        readonly List<CaseOption> _cases = new();
        readonly List<(AngledCutImage bg, Outline border)> _caseCards = new();
        readonly List<(Image bg, TextMeshProUGUI lbl)>      _typeBtns  = new();

        int               _selected   = 0;
        BattlePlayerCount _playerCount = BattlePlayerCount.OneVOne;
        int               _rounds      = 1;

        TextMeshProUGUI _roundsLbl;
        AngledCutImage  _ctaBg;
        TextMeshProUGUI _ctaLbl;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public void Show()
        {
            gameObject.SetActive(true);
            BuildOnce();

            _playerCount = BattlePlayerCount.OneVOne;
            _rounds      = 1;
            RefreshAll();

            StartCoroutine(UIAnimator.SlideFromBottom((RectTransform)transform, 0.24f));
        }

        public void Hide()
        {
            StopAllCoroutines();
            gameObject.SetActive(false);
        }

        void OnDisable() => StopAllCoroutines();

        // ── Build ──────────────────────────────────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            LoadCases();

            var rt = (RectTransform)transform;

            // Scrim — near-opaque so the modal reads as a focused surface.
            var scrim = MakeImage("Scrim", rt, ColorPalette.WithAlpha(ColorPalette.BgDeep, 0.97f), raycast: true);
            Stretch(scrim.rectTransform);

            BuildHeader(rt);
            BuildScrollContent(rt);
            BuildCta(rt);
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.CardBg, raycast: true);
            TopStrip(hdr.rectTransform, HeaderH);

            // Red accent line (top) + drag handle — sheet identity.
            var accent = MakeImage("TopAccent", hdr.transform, ColorPalette.ActiveRed);
            accent.raycastTarget = false;
            TopStrip(accent.rectTransform, 2f);

            var handle = MakeImage("Handle", hdr.transform, ColorPalette.WithAlpha(ColorPalette.TextDim, 0.6f));
            handle.raycastTarget = false;
            SetRect(handle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(40f, 4f));

            // Bottom border.
            var border = MakeImage("BottomBorder", hdr.transform, ColorPalette.Border);
            border.raycastTarget = false;
            BottomStrip(border.rectTransform, 1f);

            var title = MakeTmp(hdr.transform, "Title", "CREATE BATTLE", 18f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -4f), new Vector2(-120f, 0f));

            // Close (×).
            var close = NewGo("Close", hdr.transform, typeof(Image), typeof(Button));
            close.GetComponent<Image>().color = ColorPalette.Surface;
            var closeBorder = close.AddComponent<Outline>();
            closeBorder.effectColor = ColorPalette.Border; closeBorder.effectDistance = new Vector2(1f, -1f);
            SetRect((RectTransform)close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-SidePad, -2f), new Vector2(36f, 36f));
            var closeLbl = MakeTmp(close.transform, "Lbl", "×", 24f, FontStyles.Bold, ColorPalette.TextBright);
            closeLbl.alignment = TextAlignmentOptions.Center;
            Stretch(closeLbl.rectTransform);
            var closeBtn = close.GetComponent<Button>();
            closeBtn.transition = Selectable.Transition.None;
            closeBtn.onClick.AddListener(() => OnBack?.Invoke());
        }

        void BuildScrollContent(RectTransform rt)
        {
            var scrollGo = NewGo("Scroll", rt, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0f, NavReserve + CtaH + 12f);
            scrollRt.offsetMax = new Vector2(0f, -HeaderH);

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var content = NewGo("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot     = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding               = new RectOffset((int)SidePad, (int)SidePad, 16, 24);
            vlg.spacing               = 14f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content          = cRt;
            sr.viewport         = (RectTransform)viewport.transform;
            sr.horizontal       = false;
            sr.vertical         = true;
            sr.scrollSensitivity = 30f;

            BuildCaseSection(content.transform);
            BuildTypeSection(content.transform);
            BuildRoundsSection(content.transform);
        }

        void SectionLabel(Transform parent, string text)
        {
            var lbl = MakeTmp(parent, "Section", text, 11f, FontStyles.Bold, ColorPalette.ActiveRed);
            lbl.characterSpacing = 4f;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.gameObject.AddComponent<LayoutElement>().minHeight = 16f;
        }

        void BuildCaseSection(Transform parent)
        {
            SectionLabel(parent, "SELECT CASE");

            var gridGo = NewGo("CaseGrid", parent, typeof(GridLayoutGroup), typeof(LayoutElement));
            const float cellH = 126f, gapY = 8f;
            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(108f, cellH);
            grid.spacing         = new Vector2(8f, gapY);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment  = TextAnchor.UpperCenter;

            // Deterministic height so the parent VLG sizes the grid without a
            // ContentSizeFitter conflict (child is layout-controlled by the VLG).
            int rows = Mathf.Max(1, Mathf.CeilToInt(_cases.Count / 3f));
            gridGo.GetComponent<LayoutElement>().preferredHeight = rows * cellH + (rows - 1) * gapY;

            for (int i = 0; i < _cases.Count; i++)
                BuildCaseCard(gridGo.transform, i);
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

            // Thumbnail.
            var tile = MakeImage("ThumbBg", cardGo.transform, ColorPalette.Surface);
            SetRect(tile.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(52f, 52f));
            if (opt.Icon != null)
            {
                var icon = MakeImage("Thumb", tile.transform, Color.white);
                icon.sprite = opt.Icon;
                icon.preserveAspect = true;
                var iRt = icon.rectTransform;
                iRt.anchorMin = new Vector2(0.5f, 0.5f);
                iRt.anchorMax = new Vector2(0.5f, 0.5f);
                iRt.pivot     = new Vector2(0.5f, 0.5f);
                iRt.sizeDelta = new Vector2(46f, 46f);
            }

            // Name (wraps to 2 lines).
            var name = MakeTmp(cardGo.transform, "Name", opt.Name, 9.5f, FontStyles.Bold, ColorPalette.TextBright);
            name.alignment = TextAlignmentOptions.Top;
            name.enableWordWrapping = true;
            SetRect(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 28f), new Vector2(-10f, 30f));

            // Price.
            var price = MakeTmp(cardGo.transform, "Price", opt.Price.ToString("N0") + " VP", 10f, FontStyles.Bold, ColorPalette.GoldAccent);
            price.alignment = TextAlignmentOptions.Center;
            SetRect(price.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(-8f, 16f));

            int captured = index;
            var btn = cardGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { _selected = captured; RefreshAll(); });

            _caseCards.Add((bg, border));
        }

        void BuildTypeSection(Transform parent)
        {
            SectionLabel(parent, "BATTLE TYPE");

            var row = NewGo("TypeRow", parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().minHeight = 48f;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 10f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            _typeBtns.Add(BuildTypePill(row.transform, "1V1",   BattlePlayerCount.OneVOne));
            _typeBtns.Add(BuildTypePill(row.transform, "1V1V1", BattlePlayerCount.ThreePlayer));
        }

        (Image bg, TextMeshProUGUI lbl) BuildTypePill(Transform parent, string text, BattlePlayerCount pc)
        {
            var go = NewGo("Type_" + text, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().minHeight = 48f;
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
            return (img, lbl);
        }

        void BuildRoundsSection(Transform parent)
        {
            SectionLabel(parent, "ROUNDS");

            var row = NewGo("RoundsRow", parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().minHeight = 52f;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 14f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childAlignment         = TextAnchor.MiddleCenter;

            BuildStepButton(row.transform, "-", () => ChangeRounds(-1));

            var box = NewGo("Count", row.transform, typeof(Image), typeof(LayoutElement));
            box.GetComponent<Image>().color = ColorPalette.CardBg;
            var boxBorder = box.AddComponent<Outline>();
            boxBorder.effectColor = ColorPalette.Border; boxBorder.effectDistance = new Vector2(1f, -1f);
            var boxLe = box.GetComponent<LayoutElement>();
            boxLe.minWidth = 84f; boxLe.minHeight = 48f;
            ((RectTransform)box.transform).sizeDelta = new Vector2(84f, 48f);
            _roundsLbl = MakeTmp(box.transform, "Lbl", "1", 24f, FontStyles.Bold, ColorPalette.TextBright);
            _roundsLbl.alignment = TextAlignmentOptions.Center;
            Stretch(_roundsLbl.rectTransform);

            BuildStepButton(row.transform, "+", () => ChangeRounds(+1));
        }

        void BuildStepButton(Transform parent, string glyph, Action onClick)
        {
            var go = NewGo("Step_" + glyph, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.GetComponent<Image>().color = ColorPalette.Surface;
            var bdr = go.AddComponent<Outline>();
            bdr.effectColor = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.6f);
            bdr.effectDistance = new Vector2(1f, -1f);
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 48f; le.minHeight = 48f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(48f, 48f);

            var lbl = MakeTmp(go.transform, "Lbl", glyph, 26f, FontStyles.Bold, ColorPalette.ActiveRed);
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
        }

        void BuildCta(RectTransform rt)
        {
            _ctaBg = MakeAngled("Cta", rt, ColorPalette.ActiveRed, 10f, raycast: true);
            var cRt = _ctaBg.rectTransform;
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(1f, 0f);
            cRt.pivot     = new Vector2(0.5f, 0f);
            cRt.offsetMin = new Vector2(SidePad, NavReserve);
            cRt.offsetMax = new Vector2(-SidePad, NavReserve + CtaH);

            var btn = _ctaBg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnConfirmPressed);

            _ctaLbl = MakeTmp(_ctaBg.transform, "Lbl", "CREATE BATTLE", 15f, FontStyles.Bold, Color.white);
            _ctaLbl.characterSpacing = 1f;
            _ctaLbl.alignment = TextAlignmentOptions.Center;
            Stretch(_ctaLbl.rectTransform);
        }

        // ── State / refresh ──────────────────────────────────────────────────────
        void ChangeRounds(int delta)
        {
            _rounds = Mathf.Clamp(_rounds + delta, MinRounds, MaxRounds);
            RefreshAll();
        }

        void RefreshAll()
        {
            // Case selection visuals.
            for (int i = 0; i < _caseCards.Count; i++)
            {
                bool sel = i == _selected;
                var (bg, border) = _caseCards[i];
                border.effectColor    = sel ? ColorPalette.ActiveRed : ColorPalette.Border;
                border.effectDistance = sel ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                bg.color              = sel ? ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.16f) : ColorPalette.CardBg;
            }

            // Battle type visuals.
            for (int i = 0; i < _typeBtns.Count; i++)
            {
                bool active = (i == 0 && _playerCount == BattlePlayerCount.OneVOne)
                           || (i == 1 && _playerCount == BattlePlayerCount.ThreePlayer);
                _typeBtns[i].bg.color  = active ? ColorPalette.ActiveRed : ColorPalette.Surface;
                _typeBtns[i].lbl.color = active ? Color.white : ColorPalette.TextBright;
                var ol = _typeBtns[i].bg.GetComponent<Outline>();
                if (ol != null) ol.effectColor = active ? ColorPalette.ActiveRed : ColorPalette.Border;
            }

            if (_roundsLbl != null) _roundsLbl.text = _rounds.ToString();

            int cost = CurrentCost();
            if (_ctaLbl != null) _ctaLbl.text = "CREATE BATTLE  -  " + cost.ToString("N0") + " VP";
        }

        int CurrentCost()
        {
            if (_selected < 0 || _selected >= _cases.Count) return 0;
            return _cases[_selected].Price * _rounds;   // case price × round count
        }

        // ── Confirm ──────────────────────────────────────────────────────────────
        void OnConfirmPressed()
        {
            if (_cases.Count == 0) return;
            StartCoroutine(UIAnimator.ScalePress(_ctaBg.transform, 0.97f, 0.12f));

            var opt = _cases[_selected];
            var data = new BattleLobbyData
            {
                LobbyId        = GenId(),
                HostName       = "YOU",
                CaseName       = opt.Name,
                Rounds         = _rounds,
                Mode           = BattleMode.Normal,
                PlayerCount    = _playerCount,
                CurrentPlayers = 1,
                Status         = LobbyStatus.Waiting,
                Rarity         = SkinRarity.Select,
                WagerVP        = CurrentCost(),
            };
            OnConfirm?.Invoke(data);
        }

        // ── Case loading (from existing content database) ────────────────────────
        void LoadCases()
        {
            _cases.Clear();
            var cases = GameContext.Instance?.Content?.Cases;
            if (cases != null)
            {
                foreach (var c in cases)
                    if (c != null)
                        _cases.Add(new CaseOption(c.DisplayName, c.VpPrice, c.CaseIcon));
            }

            // Guarantee Basic Vandal Case is present even if content is unavailable.
            if (_cases.Count == 0)
                _cases.Add(new CaseOption("Basic Vandal Case", 500, null));

            // Default selection → Basic Vandal Case when present.
            _selected = 0;
            for (int i = 0; i < _cases.Count; i++)
                if (_cases[i].Name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) { _selected = i; break; }
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
