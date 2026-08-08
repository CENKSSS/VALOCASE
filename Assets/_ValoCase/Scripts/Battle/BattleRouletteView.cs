using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Battle;
using ValoCase.CaseOpening;
using ValoCase.Data;
using ValoCase.Profile;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI
{
    public sealed class BattleRouletteView : MonoBehaviour
    {
        const int ReelCardCount = 20;
        const int TargetIdx     = 3;
        const int SpinCards     = 13;

        RectTransform                   _reelContent;
        readonly List<ReelCard>         _cards = new List<ReelCard>();
        IReadOnlyList<SkinDefinitionSO> _pool;
        readonly List<SkinDefinitionSO> _fillerPool = new List<SkinDefinitionSO>();
        IReadOnlyList<RarityWeightEntry> _weights;
        RarityVisualSO                  _visuals;

        AngledCutImage  _panelBg;
        Outline         _panelBorder;
        RectTransform   _reelWindow;
        CanvasGroup     _reelCg;
        Image           _centerFrame;
        Outline         _frameOutline;
        Shadow          _frameGlow;
        CanvasGroup     _cg;
        TextMeshProUGUI _statusLbl;
        TextMeshProUGUI _totalLbl;
        TextMeshProUGUI _winnerLbl;
        RectTransform   _listContent;
        Image           _resultSeparator;

        Image _winnerTopBorder;
        Image _winnerBottomBorder;
        Image _winnerLeftBorder;
        Image _winnerRightBorder;

        RectTransform   _finalRoot;
        CanvasGroup     _finalCg;
        TextMeshProUGUI _finalAmountLbl;
        Shadow          _finalAmountGlow;

        RectTransform   _waitingRoot;
        CanvasGroup     _waitingCg;

        RectTransform   _fastSkipRoot;
        CanvasGroup     _fastSkipCg;
        RectTransform   _fastSkipIcons;
        Coroutine       _fastSkipPulseCo;

        float _reelH;
        float _cardH;
        bool  _isUser;

        float           _panelW;
        GridLayoutGroup _resultGrid;
        float           _resultListW;
        float           _resultListH;

        struct ReelCard
        {
            public AngledCutImage Bg;
            public Image          Icon;
            public Image          Stripe;
            public CanvasGroup    Cg;
        }

        public void Initialize(BattlePlayerResult player, RarityVisualSO visuals,
                               IReadOnlyList<SkinDefinitionSO> reelPool,
                               float panelW, float panelH)
        {
            _visuals = visuals;
            _pool    = (reelPool != null && reelPool.Count > 0) ? reelPool : player?.Skins;
            RebuildFillerPool();
            _isUser  = player != null && player.IsUser;
            _panelW  = panelW;

            var rt = (RectTransform)transform;
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();

            Color accent = _isUser ? ColorPalette.GoldAccent : ColorPalette.ActiveRed;

            _panelBg = MakeAngled("PanelBg", rt, ColorPalette.CardBg, 6f, raycast: false);
            Stretch(_panelBg.rectTransform);

            _panelBorder = _panelBg.gameObject.AddComponent<Outline>();
            _panelBorder.effectColor    = Color.Lerp(ColorPalette.Border, Color.white, 0.25f);
            _panelBorder.effectDistance = new Vector2(2f, -2f);

            const float pad     = 6f;
            const float headerH = 62f;
            const float gap     = 5f;

            _cardH = Mathf.Clamp(panelH * 0.17f, 52f, 78f);
            _reelH = _cardH * 2.8f;

            float reelTop = headerH + gap;
            float listTop = reelTop + _reelH + gap;
            float listH   = Mathf.Max(64f, panelH - listTop - gap - 8f);

            BuildHeader(rt, player, accent, pad, headerH);
            BuildReel(rt, accent, pad, reelTop, _reelH);
            BuildFinalEarnings(rt, pad, reelTop, _reelH);
            BuildFastSkipIndicator(rt, pad, reelTop, _reelH);
            BuildResultList(rt, pad, listTop, listH);
            BuildWinnerLabel(rt);
            BuildWaitingPresentation(rt, accent, reelTop, _reelH, player);

            SetStatus("READY", ColorPalette.TextDim);
            ShowTotal(0);
        }

        void BuildHeader(RectTransform rt, BattlePlayerResult player, Color accent, float pad, float h)
        {
            var header = NewGo("Header", rt);
            TopStrip((RectTransform)header.transform, h, -4f);
            ((RectTransform)header.transform).offsetMin = new Vector2(pad, ((RectTransform)header.transform).offsetMin.y);
            ((RectTransform)header.transform).offsetMax = new Vector2(-pad, ((RectTransform)header.transform).offsetMax.y);

            var avatar = MakeAngled("Avatar", header.transform, accent, 6f);
            SetRect(avatar.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(48f, 48f));

            // Every participant shows their backend avatar when provided; the local user
            // falls back to their own configured profile. Bots/unknowns keep the initial.
            Sprite userAvatar  = ResolveHeaderAvatar(player);
            string displayName = _isUser
                ? (!string.IsNullOrEmpty(PlayerProfileData.Username) ? PlayerProfileData.Username : "YOU")
                : (player != null ? player.Name : "?");

            if (userAvatar != null)
            {
                var photo = MakeImage("Photo", avatar.transform, Color.white, raycast: false);
                photo.sprite = userAvatar;
                photo.preserveAspect = true;
                var phRt = photo.rectTransform;
                phRt.anchorMin = new Vector2(0f, 0f);
                phRt.anchorMax = new Vector2(1f, 1f);
                phRt.offsetMin = new Vector2(2f, 2f);
                phRt.offsetMax = new Vector2(-2f, -2f);
            }
            else
            {
                string initial = _isUser
                    ? (displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "Y")
                    : (player != null && player.Name.Length > 0
                        ? player.Name.Substring(player.Name.Length - 1) : "B");

                var aLbl = MakeTmp(avatar.transform, "I", initial, 26f, FontStyles.Bold,
                    _isUser ? ColorPalette.BgDeep : Color.white);
                aLbl.alignment = TextAlignmentOptions.Center;
                Stretch(aLbl.rectTransform);
            }

            // Tagged only here, at the label. The initial above is derived from the plain
            // name and must stay clear of the rich text.
            var name = MakeTmp(header.transform, "Name", TagName(player, displayName),
                26f, FontStyles.Bold, ColorPalette.TextBright);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            SetRect(name.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(58f, -4f), new Vector2(-66f, 32f));

            _statusLbl = MakeTmp(header.transform, "Status", "READY", 17f, FontStyles.Bold, ColorPalette.TextDim);
            _statusLbl.characterSpacing = 1f;
            _statusLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(_statusLbl.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(58f, 3f), new Vector2(-66f, 22f));

            var sep = MakeImage("HeaderSeparator", rt, ColorPalette.WithAlpha(Color.white, 0.12f), raycast: false);
            var sepRt = sep.rectTransform;
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot     = new Vector2(0.5f, 1f);
            sepRt.sizeDelta = new Vector2(0f, 1.5f);
            sepRt.anchoredPosition = new Vector2(0f, -(4f + h));
            sepRt.offsetMin = new Vector2(pad + 6f, sepRt.offsetMin.y);
            sepRt.offsetMax = new Vector2(-(pad + 6f), sepRt.offsetMax.y);
        }

        void BuildReel(RectTransform rt, Color accent, float pad, float top, float reelH)
        {
            var window = NewGo("ReelWindow", rt, typeof(Image), typeof(RectMask2D));
            var windowImg = window.GetComponent<Image>();
            windowImg.color = ColorPalette.CardBg;
            windowImg.raycastTarget = false;

            _reelWindow = (RectTransform)window.transform;
            _reelCg = window.AddComponent<CanvasGroup>();

            var reelBorder = window.AddComponent<Outline>();
            reelBorder.effectColor = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.55f);
            reelBorder.effectDistance = new Vector2(1f, -1f);

            var wRt = (RectTransform)window.transform;
            wRt.anchorMin = new Vector2(0f, 1f);
            wRt.anchorMax = new Vector2(1f, 1f);
            wRt.pivot     = new Vector2(0.5f, 1f);
            wRt.sizeDelta = new Vector2(0f, reelH);
            wRt.anchoredPosition = new Vector2(0f, -top);
            wRt.offsetMin = new Vector2(pad, wRt.offsetMin.y);
            wRt.offsetMax = new Vector2(-pad, wRt.offsetMax.y);

            var content = NewGo("Content", window.transform);
            _reelContent = (RectTransform)content.transform;
            _reelContent.anchorMin = new Vector2(0f, 1f);
            _reelContent.anchorMax = new Vector2(1f, 1f);
            _reelContent.pivot     = new Vector2(0.5f, 1f);
            _reelContent.offsetMin = new Vector2(0f, 0f);
            _reelContent.offsetMax = new Vector2(0f, 0f);
            _reelContent.sizeDelta = new Vector2(0f, _cardH * ReelCardCount);
            _reelContent.anchoredPosition = Vector2.zero;

            for (int i = 0; i < ReelCardCount; i++)
                _cards.Add(BuildCard(_reelContent, i));

            _centerFrame = MakeImage("CenterFrame", window.transform, Color.clear);
            var fRt = _centerFrame.rectTransform;
            fRt.anchorMin = new Vector2(0f, 0.5f);
            fRt.anchorMax = new Vector2(1f, 0.5f);
            fRt.pivot     = new Vector2(0.5f, 0.5f);
            fRt.anchoredPosition = Vector2.zero;
            fRt.sizeDelta = new Vector2(2f, _cardH + 4f);

            _frameGlow = _centerFrame.gameObject.AddComponent<Shadow>();
            _frameGlow.effectColor    = ColorPalette.WithAlpha(accent, 0.55f);
            _frameGlow.effectDistance = new Vector2(0f, -5f);

            _frameOutline = _centerFrame.gameObject.AddComponent<Outline>();
            _frameOutline.effectColor    = ColorPalette.WithAlpha(accent, 1f);
            _frameOutline.effectDistance = new Vector2(2.5f, -2.5f);
        }

        void BuildFinalEarnings(RectTransform rt, float pad, float top, float h)
        {
            var root = NewGo("FinalEarningsRoot", rt, typeof(Image));
            var rootImg = root.GetComponent<Image>();
            rootImg.color = Color.clear;
            rootImg.raycastTarget = false;

            _finalRoot = (RectTransform)root.transform;
            _finalRoot.anchorMin = new Vector2(0f, 1f);
            _finalRoot.anchorMax = new Vector2(1f, 1f);
            _finalRoot.pivot     = new Vector2(0.5f, 1f);
            _finalRoot.sizeDelta = new Vector2(0f, h);
            _finalRoot.anchoredPosition = new Vector2(0f, -top);
            _finalRoot.offsetMin = new Vector2(pad, _finalRoot.offsetMin.y);
            _finalRoot.offsetMax = new Vector2(-pad, _finalRoot.offsetMax.y);

            _finalCg = root.AddComponent<CanvasGroup>();

            var bgPanel = MakeAngled("FinalBg", root.transform,
                Color.clear, 6f, raycast: false);
            Stretch(bgPanel.rectTransform);

            var bgBorder = bgPanel.gameObject.AddComponent<Outline>();
            bgBorder.effectColor    = Color.clear;
            bgBorder.effectDistance = new Vector2(1f, -1f);

            var title = MakeTmp(root.transform, "Title", "FINAL EARNINGS", 11f, FontStyles.Bold, ColorPalette.TextDim);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(-16f, 18f));

            var arrow = MakeTmp(root.transform, "Arrow", "▲", 16f, FontStyles.Bold, ColorPalette.GoldAccent);
            arrow.alignment = TextAlignmentOptions.Center;
            SetRect(arrow.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(40f, 18f));

            _finalAmountLbl = MakeTmp(root.transform, "Amount", "0", 40f, FontStyles.Bold, ColorPalette.GoldAccent);
            _finalAmountLbl.alignment = TextAlignmentOptions.Center;
            _finalAmountLbl.enableWordWrapping = false;
            _finalAmountLbl.enableAutoSizing = true;
            _finalAmountLbl.fontSizeMin = 20f;
            _finalAmountLbl.fontSizeMax = 48f;
            SetRect(_finalAmountLbl.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -6f), new Vector2(-12f, 58f));

            _finalAmountGlow = _finalAmountLbl.gameObject.AddComponent<Shadow>();
            _finalAmountGlow.effectColor    = ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.5f);
            _finalAmountGlow.effectDistance = new Vector2(0f, -3f);

            var vp = MakeTmp(root.transform, "VP", "VP", 14f, FontStyles.Bold, ColorPalette.TextBright);
            vp.characterSpacing = 4f;
            vp.alignment = TextAlignmentOptions.Center;
            SetRect(vp.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(-16f, 20f));

            root.SetActive(false);
        }

        void BuildFastSkipIndicator(RectTransform rt, float pad, float top, float h)
        {
            var root = NewGo("FastSkipRoot", rt, typeof(Image));
            var img = root.GetComponent<Image>();
            img.color = ColorPalette.WithAlpha(ColorPalette.BgDeep, 0.72f);
            img.raycastTarget = false;

            _fastSkipRoot = (RectTransform)root.transform;
            _fastSkipRoot.anchorMin = new Vector2(0f, 1f);
            _fastSkipRoot.anchorMax = new Vector2(1f, 1f);
            _fastSkipRoot.pivot     = new Vector2(0.5f, 1f);
            _fastSkipRoot.sizeDelta = new Vector2(0f, h);
            _fastSkipRoot.anchoredPosition = new Vector2(0f, -top);
            _fastSkipRoot.offsetMin = new Vector2(pad, _fastSkipRoot.offsetMin.y);
            _fastSkipRoot.offsetMax = new Vector2(-pad, _fastSkipRoot.offsetMax.y);

            _fastSkipCg = root.AddComponent<CanvasGroup>();

            _fastSkipIcons = (RectTransform)NewGo("Icons", root.transform).transform;
            SetRect(_fastSkipIcons,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 14f), new Vector2(70f, 40f));

            var tri = FastForwardTriangleSprite();
            for (int k = 0; k < 2; k++)
            {
                var arrow = NewGo("Arrow" + k, _fastSkipIcons, typeof(Image));
                var ai = arrow.GetComponent<Image>();
                ai.sprite        = tri;
                ai.color         = ColorPalette.GoldAccent;
                ai.raycastTarget = false;
                SetRect((RectTransform)arrow.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(-14f + k * 26f, 0f), new Vector2(34f, 34f));
            }

            var lbl = MakeTmp(root.transform, "Lbl", "FAST SKIPPING...", 10f, FontStyles.Bold, ColorPalette.TextBright);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.characterSpacing = 2f;
            SetRect(lbl.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -22f), new Vector2(-8f, 16f));

            root.SetActive(false);
        }

        public void ShowFastSkipIndicator()
        {
            if (_fastSkipRoot == null) return;
            _fastSkipRoot.gameObject.SetActive(true);
            if (_fastSkipCg != null) _fastSkipCg.alpha = 1f;
            _fastSkipRoot.SetAsLastSibling();
            if (_fastSkipPulseCo != null) StopCoroutine(_fastSkipPulseCo);
            _fastSkipPulseCo = StartCoroutine(FastSkipPulse());
        }

        public void HideFastSkipIndicator()
        {
            if (_fastSkipPulseCo != null) { StopCoroutine(_fastSkipPulseCo); _fastSkipPulseCo = null; }
            if (_fastSkipRoot == null) return;
            if (_fastSkipIcons != null) { _fastSkipIcons.localScale = Vector3.one; _fastSkipIcons.anchoredPosition = new Vector2(0f, 14f); }
            _fastSkipRoot.gameObject.SetActive(false);
        }

        IEnumerator FastSkipPulse()
        {
            float t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                float pulse = 1f + 0.1f * Mathf.Sin(t * Mathf.PI * 4f);
                if (_fastSkipIcons != null)
                {
                    _fastSkipIcons.localScale = new Vector3(pulse, pulse, 1f);
                    _fastSkipIcons.anchoredPosition = new Vector2(Mathf.Sin(t * Mathf.PI * 3f) * 6f, 14f);
                }
                yield return null;
            }
        }

        // Procedural right-pointing filled triangle (white, tinted via Image.color).
        static Sprite s_ffTriangle;
        static Sprite FastForwardTriangleSprite()
        {
            if (s_ffTriangle != null) return s_ffTriangle;

            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            for (int x = 0; x < s; x++)
            {
                float t = 1f - (float)x / (s - 1);
                float halfHeight = t * (s * 0.5f);
                for (int y = 0; y < s; y++)
                {
                    float dy = Mathf.Abs(y - s * 0.5f);
                    byte a = dy <= halfHeight ? (byte)255 : (byte)0;
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            s_ffTriangle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return s_ffTriangle;
        }

        ReelCard BuildCard(RectTransform content, int index)
        {
            float cardInner = _cardH - 6f;
            float centerY   = -(index * _cardH) - _cardH * 0.5f;
            const float hpad = 4f;

            var bg = MakeAngled("Card" + index, content, Color.clear, 3f);
            var bRt = bg.rectTransform;
            bRt.anchorMin = new Vector2(0f, 1f);
            bRt.anchorMax = new Vector2(1f, 1f);
            bRt.pivot     = new Vector2(0.5f, 0.5f);
            bRt.sizeDelta = new Vector2(-2f * hpad, cardInner);
            bRt.anchoredPosition = new Vector2(0f, centerY);

            var icon = MakeImage("Icon", bg.transform, Color.white);
            icon.preserveAspect = true;
            icon.raycastTarget  = false;

            var iRt = icon.rectTransform;
            iRt.anchorMin = new Vector2(0.5f, 0.5f);
            iRt.anchorMax = new Vector2(0.5f, 0.5f);
            iRt.pivot     = new Vector2(0.5f, 0.5f);
            iRt.anchoredPosition = Vector2.zero;

            float iconSize = (cardInner - 8f) * 1.35f;
            iRt.sizeDelta = new Vector2(iconSize * 1.6f, iconSize);

            var stripe = MakeImage("Stripe", bg.transform, Color.clear);
            stripe.raycastTarget = false;
            BottomStrip(stripe.rectTransform, 0f);
            stripe.gameObject.SetActive(false);

            var cg = bg.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0.8f;
            bRt.localScale = new Vector3(0.9f, 0.9f, 1f);

            return new ReelCard { Bg = bg, Icon = icon, Stripe = stripe, Cg = cg };
        }

        void BuildResultList(RectTransform rt, float pad, float top, float listH)
        {
            // Thin divider that separates the Final Earnings / VP block from the
            // result skin cards. Sits a few px above the list window so it never
            // overlaps the VP text or the cards.
            var sep = MakeImage("ResultSeparator", rt, ColorPalette.WithAlpha(Color.white, 0.12f), raycast: false);
            _resultSeparator = sep;
            var sepRt = sep.rectTransform;
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot     = new Vector2(0.5f, 1f);
            sepRt.sizeDelta = new Vector2(0f, 1.5f);
            sepRt.anchoredPosition = new Vector2(0f, -(top - 5f));
            sepRt.offsetMin = new Vector2(pad + 6f, sepRt.offsetMin.y);
            sepRt.offsetMax = new Vector2(-(pad + 6f), sepRt.offsetMax.y);

            var window = NewGo("ResultWindow", rt, typeof(RectMask2D), typeof(Image));
            window.GetComponent<Image>().color = Color.clear;

            var wRt = (RectTransform)window.transform;
            wRt.anchorMin = new Vector2(0f, 1f);
            wRt.anchorMax = new Vector2(1f, 1f);
            wRt.pivot     = new Vector2(0.5f, 1f);
            wRt.sizeDelta = new Vector2(0f, listH);
            wRt.anchoredPosition = new Vector2(0f, -top);
            wRt.offsetMin = new Vector2(pad, wRt.offsetMin.y);
            wRt.offsetMax = new Vector2(-pad, wRt.offsetMax.y);

            var content = NewGo("List", window.transform, typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot     = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(82f, 82f);
            grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(8, 8, 8, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _resultGrid  = grid;
            _resultListW = _panelW - 2f * pad;
            _resultListH = listH;
        }

        // Sizes the result grid so every dropped card fits fully inside the panel without
        // clipping or vertical scrolling — cells shrink as the round count grows.
        public void PrepareResults(int count)
        {
            if (_resultGrid == null || count <= 0) return;

            float w = _resultListW > 1f ? _resultListW : (_listContent != null ? _listContent.rect.width : 0f);
            float h = _resultListH;
            if (w < 1f || h < 1f) return;

            const float gap     = 6f;
            const float sidePad = 8f;
            const float topPad  = 8f;
            const float botPad  = 12f;
            const float maxCell = 82f;
            const float minCell = 44f;

            float availW = Mathf.Max(1f, w - 2f * sidePad);
            float availH = Mathf.Max(1f, h - topPad - botPad);

            // Columns come from the available width so cards flow horizontally and wrap.
            int cols    = Mathf.FloorToInt((availW + gap) / (maxCell + gap));
            int fitCols = Mathf.FloorToInt((availW + gap) / (minCell + gap));
            cols = Mathf.Clamp(Mathf.Max(cols, Mathf.Min(2, fitCols)), 1, count);

            int   rows = Mathf.CeilToInt(count / (float)cols);
            float cw   = (availW - (cols - 1) * gap) / cols;
            float ch   = (availH - (rows - 1) * gap) / rows;
            float cell = Mathf.Clamp(Mathf.Min(cw, ch), 1f, maxCell);

            _resultGrid.padding         = new RectOffset((int)sidePad, (int)sidePad, (int)topPad, (int)botPad);
            _resultGrid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            _resultGrid.constraintCount = cols;
            _resultGrid.cellSize        = new Vector2(cell, cell);
            _resultGrid.spacing         = new Vector2(gap, gap);
        }

        void BuildWinnerLabel(RectTransform rt)
        {
            var chip = MakeAngled("WinnerChip", rt, ColorPalette.GoldAccent, 4f);
            SetRect(chip.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 6f), new Vector2(78f, 20f));

            _winnerLbl = MakeTmp(chip.transform, "Lbl", "WINNER", 10f, FontStyles.Bold, ColorPalette.BgDeep);
            _winnerLbl.alignment = TextAlignmentOptions.Center;
            _winnerLbl.characterSpacing = 2f;
            Stretch(_winnerLbl.rectTransform);

            chip.gameObject.SetActive(false);
        }

        // ── Pre-battle waiting presentation ──────────────────────────────────────
        // Built inactive; only shown when the screen explicitly stages the panel
        // before the battle starts. The existing running/finished flow never calls
        // ShowWaiting(), so it is unaffected.
        void BuildWaitingPresentation(RectTransform rt, Color accent, float top, float reelH, BattlePlayerResult player)
        {
            var root = NewGo("WaitingRoot", rt);
            _waitingRoot = (RectTransform)root.transform;
            _waitingRoot.anchorMin = new Vector2(0f, 1f);
            _waitingRoot.anchorMax = new Vector2(1f, 1f);
            _waitingRoot.pivot     = new Vector2(0.5f, 1f);
            _waitingRoot.sizeDelta = new Vector2(0f, reelH);
            _waitingRoot.anchoredPosition = new Vector2(0f, -top);
            _waitingRoot.offsetMin = new Vector2(6f, _waitingRoot.offsetMin.y);
            _waitingRoot.offsetMax = new Vector2(-6f, _waitingRoot.offsetMax.y);

            _waitingCg = root.AddComponent<CanvasGroup>();

            // Every participant uses their backend avatar when provided; the local user
            // falls back to their own configured profile. Bots/unknowns keep the initial.
            Sprite userAvatar  = ResolveHeaderAvatar(player);
            string displayName = _isUser
                ? (!string.IsNullOrEmpty(PlayerProfileData.Username) ? PlayerProfileData.Username : "YOU")
                : (player != null ? player.Name : "?");

            string initial = _isUser ? "Y" : (player != null && player.Name.Length > 0
                ? player.Name.Substring(player.Name.Length - 1) : "B");

            float avatarSize = Mathf.Clamp(reelH * 0.42f, 80f, 152f);
            float blockH     = avatarSize + 16f + 26f + 6f + 18f;
            float padTop     = Mathf.Max(8f, (reelH - blockH) * 0.5f);

            var ring = MakeAngled("AvatarRing", root.transform,
                ColorPalette.WithAlpha(accent, 0.14f), 8f, raycast: false);
            SetRect(ring.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(padTop - 6f)), new Vector2(avatarSize + 12f, avatarSize + 12f));
            var ringB = ring.gameObject.AddComponent<Outline>();
            ringB.effectColor    = ColorPalette.WithAlpha(accent, 0.5f);
            ringB.effectDistance = new Vector2(1f, -1f);

            var avatar = MakeAngled("Avatar", root.transform, accent, 7f, raycast: false);
            SetRect(avatar.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -padTop), new Vector2(avatarSize, avatarSize));
            var glow = avatar.gameObject.AddComponent<Shadow>();
            glow.effectColor    = ColorPalette.WithAlpha(accent, 0.55f);
            glow.effectDistance = new Vector2(0f, -4f);

            if (userAvatar != null)
            {
                // Real profile photo, inset so the accent frame still shows.
                var photo = MakeImage("Photo", avatar.transform, Color.white, raycast: false);
                photo.sprite = userAvatar;
                photo.preserveAspect = true;
                var phRt = photo.rectTransform;
                phRt.anchorMin = new Vector2(0f, 0f);
                phRt.anchorMax = new Vector2(1f, 1f);
                phRt.offsetMin = new Vector2(4f, 4f);
                phRt.offsetMax = new Vector2(-4f, -4f);
            }
            else
            {
                var initialLbl = MakeTmp(avatar.transform, "Initial", initial,
                    avatarSize * 0.42f, FontStyles.Bold, _isUser ? ColorPalette.BgDeep : Color.white);
                initialLbl.alignment = TextAlignmentOptions.Center;
                Stretch(initialLbl.rectTransform);
            }

            var nameLbl = MakeTmp(root.transform, "Name", TagName(player, displayName),
                18f, FontStyles.Bold, ColorPalette.TextBright);
            nameLbl.alignment = TextAlignmentOptions.Center;
            nameLbl.enableWordWrapping = false;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(padTop + avatarSize + 14f)), new Vector2(-8f, 26f));

            Color statusColor = _isUser ? ColorPalette.GoldAccent : ColorPalette.ActiveRed;
            var statusLbl = MakeTmp(root.transform, "Status", "READY",
                11f, FontStyles.Bold, statusColor);
            statusLbl.alignment = TextAlignmentOptions.Center;
            statusLbl.characterSpacing = 3f;
            SetRect(statusLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(padTop + avatarSize + 14f + 26f + 6f)), new Vector2(-8f, 16f));

            root.SetActive(false);
        }

        // Puts the country code in front of a participant's name. The local player's code
        // comes from the save; an opponent's comes from whatever the backend sent with the
        // battle, and "00" stands in when that is absent — bots included.
        string TagName(BattlePlayerResult player, string displayName)
        {
            var code = _isUser
                ? ValoCase.Core.GameContext.Instance?.Save?.Data?.countryCode
                : player?.CountryCode;
            return UIBuild.WithCountryTag(code, displayName);
        }

        Sprite ResolveHeaderAvatar(BattlePlayerResult player)
        {
            if (player != null && player.Avatar != null) return player.Avatar;
            return _isUser ? PlayerProfileData.Avatar : null;
        }

        public void SetReelPool(IReadOnlyList<SkinDefinitionSO> pool, IReadOnlyList<RarityWeightEntry> weights)
        {
            if (pool != null && pool.Count > 0) { _pool = pool; RebuildFillerPool(); }
            if (weights != null) _weights = weights;
        }

        // ── Optimistic warmup spin (backend-authoritative battle) ────────────────
        // Reveals the reel and free-scrolls random filler the instant the player taps,
        // before the server result is known. StopWarmupSpin halts it; the real
        // per-round SpinRound (which sets its own start position + bindings) then takes
        // over. No outcome is shown — warmup is pure filler motion.
        bool _warmupActive;
        Coroutine _warmupCo;
        readonly List<SkinDefinitionSO> _warmupBind = new List<SkinDefinitionSO>();

        public void BeginWarmupSpin(IReadOnlyList<SkinDefinitionSO> pool, IReadOnlyList<RarityWeightEntry> weights)
        {
            if (pool != null && pool.Count > 0) { _pool = pool; RebuildFillerPool(); }
            if (weights != null) _weights = weights;
            if (_reelContent == null || _cards.Count == 0) return;

            if (_reelWindow != null)
            {
                _reelWindow.gameObject.SetActive(true);
                if (_reelCg != null) _reelCg.alpha = 1f;
            }

            _warmupBind.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                var s = RandomFiller();
                _warmupBind.Add(s);
                BindCard(_cards[i], s);
            }
            ResetReelStyle();
            _reelContent.anchoredPosition = Vector2.zero;

            _warmupActive = true;
            if (_warmupCo != null) StopCoroutine(_warmupCo);
            _warmupCo = StartCoroutine(WarmupLoop());
        }

        // Seamless infinite scroll: slides the content down by up to one card, then
        // wraps back up while rotating the card bindings down one slot (card 0 gets a
        // fresh filler). The wrap+rotate is visually identical, so it loops forever.
        IEnumerator WarmupLoop()
        {
            float speed = _cardH * 9f;
            float aY    = 0f;

            while (_warmupActive)
            {
                aY -= speed * Time.unscaledDeltaTime;
                while (aY <= -_cardH)
                {
                    aY += _cardH;
                    for (int i = _cards.Count - 1; i > 0; i--)
                        _warmupBind[i] = _warmupBind[i - 1];
                    _warmupBind[0] = RandomFiller();
                    for (int i = 0; i < _cards.Count; i++)
                        BindCard(_cards[i], _warmupBind[i]);
                }
                _reelContent.anchoredPosition = new Vector2(0f, aY);
                yield return null;
            }
        }

        public void StopWarmupSpin()
        {
            _warmupActive = false;
            if (_warmupCo != null) { StopCoroutine(_warmupCo); _warmupCo = null; }
            if (_reelContent != null) _reelContent.anchoredPosition = Vector2.zero;
        }

        public void ShowWaiting()
        {
            if (_reelWindow != null)
            {
                if (_reelCg != null) _reelCg.alpha = 1f;
                _reelWindow.gameObject.SetActive(false);
            }

            if (_waitingRoot != null)
            {
                _waitingRoot.gameObject.SetActive(true);
                if (_waitingCg != null) _waitingCg.alpha = 1f;
                _waitingRoot.localScale = Vector3.one;
            }

            SetStatus("READY", _isUser ? ColorPalette.GoldAccent : ColorPalette.TextDim);
        }

        public IEnumerator HideWaiting(float duration)
        {
            if (_reelWindow != null)
            {
                _reelWindow.gameObject.SetActive(true);
                if (_reelCg != null) _reelCg.alpha = 1f;
            }

            if (_waitingRoot == null) yield break;

            float t = 0f;
            float dur = Mathf.Max(0.05f, duration);

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float eased = p * p;

                if (_waitingCg != null) _waitingCg.alpha = 1f - eased;
                _waitingRoot.localScale = Vector3.Lerp(Vector3.one, new Vector3(0.92f, 0.92f, 1f), eased);

                yield return null;
            }

            if (_waitingCg != null) _waitingCg.alpha = 0f;
            _waitingRoot.gameObject.SetActive(false);
        }

        public void SetStatus(string text, Color color)
        {
            if (_statusLbl == null) return;
            _statusLbl.text  = text;
            _statusLbl.color = color;
        }

        public void ShowTotal(int totalVp)
        {
            if (_totalLbl != null) _totalLbl.text = totalVp.ToString("N0") + " VP";
        }

        public void SetTotalColor(Color color)
        {
            if (_totalLbl != null) _totalLbl.color = color;
        }

        void ShowFinalAmount(int v)
        {
            if (_finalAmountLbl != null) _finalAmountLbl.text = v.ToString("N0");
        }

        void SetFinalAmountColor(Color color)
        {
            if (_finalAmountLbl != null) _finalAmountLbl.color = color;
            if (_finalAmountGlow != null) _finalAmountGlow.effectColor = ColorPalette.WithAlpha(color, 0.5f);
        }

        public IEnumerator ShowFinalEarnings(float duration)
        {
            if (_finalRoot == null) yield break;

            _finalRoot.gameObject.SetActive(true);
            if (_finalCg != null) _finalCg.alpha = 0f;
            _finalRoot.localScale = new Vector3(0.9f, 0.9f, 1f);

            float t = 0f;
            float dur = Mathf.Max(0.05f, duration);

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float eased = 1f - (1f - p) * (1f - p);

                if (_finalCg != null) _finalCg.alpha = eased;
                _finalRoot.localScale = Vector3.Lerp(new Vector3(0.9f, 0.9f, 1f), Vector3.one, eased);

                yield return null;
            }

            if (_finalCg != null) _finalCg.alpha = 1f;
            _finalRoot.localScale = Vector3.one;
        }

        public IEnumerator CountUpTotal(int finalTotalVp, float duration)
        {
            SetTotalColor(ColorPalette.GoldAccent);
            SetFinalAmountColor(ColorPalette.GoldAccent);
            ShowTotal(0);
            ShowFinalAmount(0);

            float t = 0f;
            float dur = Mathf.Max(0.05f, duration);

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float eased = 1f - Mathf.Pow(1f - p, 3f);

                int value = Mathf.RoundToInt(Mathf.Lerp(0f, finalTotalVp, eased));
                ShowTotal(value);
                ShowFinalAmount(value);

                yield return null;
            }

            ShowTotal(finalTotalVp);
            ShowFinalAmount(finalTotalVp);
        }

        public IEnumerator HideReel(float duration)
        {
            if (_reelWindow == null)
                yield break;

            float t = 0f;
            float dur = Mathf.Max(0.05f, duration);
            Vector3 startScale = _reelWindow.localScale;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float eased = p * p;

                if (_reelCg != null) _reelCg.alpha = 1f - eased;
                _reelWindow.localScale = Vector3.Lerp(startScale, startScale * 0.85f, eased);

                yield return null;
            }

            if (_reelCg != null) _reelCg.alpha = 0f;
            _reelWindow.gameObject.SetActive(false);
        }

        public IEnumerator SpinRound(SkinDefinitionSO target, float duration)
        {
            if (_reelContent == null || _cards.Count == 0) yield break;

            for (int i = 0; i < _cards.Count; i++)
            {
                var skin = i == TargetIdx ? target : RandomFiller();
                BindCard(_cards[i], skin);
            }

            ResetReelStyle();

            float cEnd   = TargetIdx * _cardH + _cardH * 0.5f - _reelH * 0.5f;
            float cStart = (TargetIdx + SpinCards) * _cardH + _cardH * 0.5f - _reelH * 0.5f;

            _reelContent.anchoredPosition = new Vector2(0f, cStart);

            float t = 0f;
            float dur = Mathf.Max(0.1f, duration);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float e = EaseOutQuart(Mathf.Clamp01(t / dur));
                _reelContent.anchoredPosition = new Vector2(0f, Mathf.Lerp(cStart, cEnd, e));
                yield return null;
            }

            _reelContent.anchoredPosition = new Vector2(0f, cEnd);
            ApplyLandedFocus(target);
        }

        void ResetReelStyle()
        {
            foreach (var c in _cards)
            {
                if (c.Cg != null) c.Cg.alpha = 0.8f;
                if (c.Bg != null) c.Bg.rectTransform.localScale = new Vector3(0.9f, 0.9f, 1f);
            }
        }

        void ApplyLandedFocus(SkinDefinitionSO target)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (i == TargetIdx)
                {
                    if (c.Cg != null) c.Cg.alpha = 1f;
                    if (c.Bg != null) c.Bg.rectTransform.localScale = new Vector3(1.1f, 1.1f, 1f);
                }
                else if (i == TargetIdx - 1 || i == TargetIdx + 1)
                {
                    if (c.Cg != null) c.Cg.alpha = 0.45f;
                    if (c.Bg != null) c.Bg.rectTransform.localScale = new Vector3(0.78f, 0.78f, 1f);
                }
            }

            if (target != null)
            {
                // Reel sonucunda da rarity rengi gösterilmesin.
                // Sadece sabit kırmızı/premium frame kalsın.
                if (_frameOutline != null)
                    _frameOutline.effectColor = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 1f);

                if (_frameGlow != null)
                    _frameGlow.effectColor = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.45f);
            }
        }

        public void AddResultRow(SkinDefinitionSO skin)
        {
            if (skin == null || _listContent == null) return;

            Color rc = ColorPalette.ForRarity(skin.Rarity);

            var box = MakeAngled("SkinBox", _listContent.transform, ColorPalette.WithAlpha(rc, 0.95f), 4f);

            var fill = MakeAngled("Fill", box.transform, ColorPalette.Surface, 4f);
            Stretch(fill.rectTransform);
            fill.rectTransform.offsetMin = new Vector2(2f, 2f);
            fill.rectTransform.offsetMax = new Vector2(-2f, -2f);
            var cardRoot = fill.transform;

            var icon = MakeImage("Icon", cardRoot, Color.white);
            icon.sprite = skin.Icon;
            icon.enabled = skin.Icon != null;
            icon.preserveAspect = true;
            SetRect(icon.rectTransform,
                new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.95f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            ApplyItemScale(icon);

            var nameLbl = MakeTmp(cardRoot, "Name", skin.SkinName, 9f, FontStyles.Bold, rc);
            nameLbl.alignment = TextAlignmentOptions.Center;
            nameLbl.enableWordWrapping = false;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            nameLbl.enableAutoSizing = true;
            nameLbl.fontSizeMin = 6f;
            nameLbl.fontSizeMax = 11f;
            SetRect(nameLbl.rectTransform,
                new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.42f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            var vpLbl = MakeTmp(cardRoot, "Vp", skin.VpValue.ToString("N0"), 10f, FontStyles.Bold, ColorPalette.GoldAccent);
            vpLbl.alignment = TextAlignmentOptions.Center;
            vpLbl.enableAutoSizing = true;
            vpLbl.fontSizeMin = 6f;
            vpLbl.fontSizeMax = 12f;
            SetRect(vpLbl.rectTransform,
                new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.2f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        void ShowFullWinnerBorder(Color color)
        {
            var rt = (RectTransform)transform;

            if (_winnerTopBorder == null)
            {
                _winnerTopBorder = MakeImage("WinnerTopBorder", rt, color);
                var topRt = _winnerTopBorder.rectTransform;
                topRt.anchorMin = new Vector2(0f, 1f);
                topRt.anchorMax = new Vector2(1f, 1f);
                topRt.pivot = new Vector2(0.5f, 1f);
                topRt.sizeDelta = new Vector2(0f, 3f);
                topRt.anchoredPosition = Vector2.zero;
            }

            if (_winnerBottomBorder == null)
            {
                _winnerBottomBorder = MakeImage("WinnerBottomBorder", rt, color);
                var bottomRt = _winnerBottomBorder.rectTransform;
                bottomRt.anchorMin = new Vector2(0f, 0f);
                bottomRt.anchorMax = new Vector2(1f, 0f);
                bottomRt.pivot = new Vector2(0.5f, 0f);
                bottomRt.sizeDelta = new Vector2(0f, 3f);
                bottomRt.anchoredPosition = Vector2.zero;
            }

            if (_winnerLeftBorder == null)
            {
                _winnerLeftBorder = MakeImage("WinnerLeftBorder", rt, color);
                var leftRt = _winnerLeftBorder.rectTransform;
                leftRt.anchorMin = new Vector2(0f, 0f);
                leftRt.anchorMax = new Vector2(0f, 1f);
                leftRt.pivot = new Vector2(0f, 0.5f);
                leftRt.sizeDelta = new Vector2(3f, 0f);
                leftRt.anchoredPosition = Vector2.zero;
            }

            if (_winnerRightBorder == null)
            {
                _winnerRightBorder = MakeImage("WinnerRightBorder", rt, color);
                var rightRt = _winnerRightBorder.rectTransform;
                rightRt.anchorMin = new Vector2(1f, 0f);
                rightRt.anchorMax = new Vector2(1f, 1f);
                rightRt.pivot = new Vector2(0.5f, 0.5f);
                rightRt.sizeDelta = new Vector2(4f, 0f);
                rightRt.anchoredPosition = new Vector2(-2f, 0f);
            }

            _winnerTopBorder.color = color;
            _winnerBottomBorder.color = color;
            _winnerLeftBorder.color = color;
            _winnerRightBorder.color = color;

            _winnerTopBorder.gameObject.SetActive(true);
            _winnerBottomBorder.gameObject.SetActive(true);
            _winnerLeftBorder.gameObject.SetActive(true);
            _winnerRightBorder.gameObject.SetActive(true);

            _winnerTopBorder.transform.SetAsLastSibling();
            _winnerBottomBorder.transform.SetAsLastSibling();
            _winnerLeftBorder.transform.SetAsLastSibling();
            _winnerRightBorder.transform.SetAsLastSibling();
        }

        public void MarkWinner(bool isWinner)
        {
            if (isWinner) ApplyHighlight(new Color(0.18f, 0.80f, 0.44f, 1f), "WINNER");
            else          ClearHighlight();
        }

        /// <summary>
        /// Draw: every player finished on the same total, so there is no winner. Uses the
        /// same highlight treatment in gold on every panel, labelled DRAW.
        /// </summary>
        public void MarkDraw() => ApplyHighlight(ColorPalette.GoldAccent, "DRAW");

        void ApplyHighlight(Color c, string label)
        {
            var chip = _winnerLbl != null ? _winnerLbl.transform.parent.gameObject : null;

            if (_panelBorder != null)
            {
                _panelBorder.effectColor    = c;
                _panelBorder.effectDistance = new Vector2(2.5f, -2.5f);
            }

            if (_panelBg != null)
            {
                var glow = _panelBg.gameObject.GetComponent<Shadow>();
                if (glow == null) glow = _panelBg.gameObject.AddComponent<Shadow>();
                glow.effectColor    = ColorPalette.WithAlpha(c, 0.75f);
                glow.effectDistance = new Vector2(0f, -4f);
            }

            if (_frameOutline != null)
                _frameOutline.effectColor = c;

            if (_frameGlow != null)
                _frameGlow.effectColor = ColorPalette.WithAlpha(c, 0.65f);

            SetTotalColor(c);
            SetFinalAmountColor(c);
            ShowFullWinnerBorder(c);

            if (_resultSeparator != null)
                _resultSeparator.color = ColorPalette.WithAlpha(c, 0.3f);

            if (chip != null)
            {
                chip.SetActive(true);

                var chipBg = chip.GetComponent<AngledCutImage>();
                if (chipBg != null) chipBg.color = c;

                if (_winnerLbl != null)
                {
                    _winnerLbl.text  = label;
                    _winnerLbl.color = ColorPalette.BgDeep;
                }
            }

            if (_cg != null) _cg.alpha = 1f;
        }

        void ClearHighlight()
        {
            // Kaybeden panel sadece winner highlight'ı almasın.
            // Tüm paneli (ve alttaki result/skin kutularını) soluklaştırmıyoruz;
            // böylece rarity renkleri ve skin kutuları normal görünümünü korur.
            var chip = _winnerLbl != null ? _winnerLbl.transform.parent.gameObject : null;
            if (chip != null) chip.SetActive(false);
            if (_cg != null) _cg.alpha = 1f;
        }

        void BindCard(ReelCard card, SkinDefinitionSO skin)
        {
            if (skin == null)
            {
                if (card.Icon != null) card.Icon.enabled = false;
                return;
            }

            Color rc = ColorPalette.ForRarity(skin.Rarity);

            // Reel kartlarında rarity rengi gösterilmesin.
            // Rarity sadece alttaki result/inventory kutularında border olarak görünecek.
            Color cardBg = Color.clear;

            if (_visuals != null && _visuals.TryGet(skin.Rarity, out var entry))
            {
                rc = entry.primaryColor;
            }

            if (card.Bg != null)
                card.Bg.color = cardBg;

            if (card.Stripe != null)
            {
                card.Stripe.color = Color.clear;
                card.Stripe.gameObject.SetActive(false);
            }

            if (card.Icon != null)
            {
                card.Icon.sprite  = skin.Icon;
                card.Icon.enabled = skin.Icon != null;
                card.Icon.color   = Color.white;
                ApplyItemScale(card.Icon);
            }
        }

        // Mirrors the Inventory / Upgrade thumbnail balancing: preserveAspect fits each
        // weapon by its own crop, so short sprites (pistols, charms) read oversized and
        // long sprites (rifles) collapse. Scaling the icon by sprite aspect evens out the
        // visual mass — bounded to [0.85, 1.15], reset to 1 when the sprite is missing.
        static void ApplyItemScale(Image icon)
        {
            float scale = 1f;
            var sprite = icon.sprite;
            if (icon.enabled && sprite != null && sprite.rect.height > 0f)
            {
                float t = Mathf.InverseLerp(1.5f, 4.3f, sprite.rect.width / sprite.rect.height);
                scale = Mathf.Lerp(0.85f, 1.15f, t);
            }
            icon.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        SkinDefinitionSO RandomFiller()
        {
            if (_pool == null || _pool.Count == 0) return null;
            var pick = CaseReelBuilder.PickFiller(_fillerPool, _weights);
            return pick != null ? pick : _pool[Random.Range(0, _pool.Count)];
        }

        void RebuildFillerPool()
        {
            _fillerPool.Clear();
            if (_pool == null) return;
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null) _fillerPool.Add(_pool[i]);
        }

        static float EaseOutQuart(float t)
        {
            float inv = 1f - t;
            return 1f - inv * inv * inv * inv;
        }
    }
}