using System;
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
    /// Screen 2 — Create Battle. Full-screen overlay panel hosted by LobbyListScreen.
    /// Lets the player pick a case, player count, rounds and mode, then start a battle.
    /// Emits OnConfirm(BattleLobbyData) when START BATTLE is pressed; OnBack on cancel.
    /// </summary>
    public sealed class CreateBattleScreen : MonoBehaviour
    {
        const float HeaderH = 88f;
        const float FooterH = 112f;
        const float SidePad = 16f;
        const float CutCard = 8f;

        public event Action OnBack;
        public event Action<BattleLobbyData> OnConfirm;

        static readonly string[] CaseNames =
            { "Vandal Vault", "Operator Elite", "Sheriff Prime", "Spectre Mix", "Phantom Kit" };
        static readonly int[]    CaseCosts  = { 1200, 1900, 800, 600, 1500 };
        static readonly SkinRarity[] CaseRarity =
            { SkinRarity.Ultra, SkinRarity.Exclusive, SkinRarity.Premium, SkinRarity.Deluxe, SkinRarity.Exclusive };

        bool _built;
        int  _selectedCase = -1;
        BattlePlayerCount _playerCount = BattlePlayerCount.OneVOne;
        int  _rounds = 1;
        BattleMode _mode = BattleMode.Normal;

        TextMeshProUGUI _totalChip;
        TextMeshProUGUI _footerCost;
        AngledCutImage  _confirmBg;
        TextMeshProUGUI _confirmLabel;
        Button          _confirmButton;

        readonly List<(AngledCutImage bg, Outline border, Shadow glow)> _caseCards = new();
        readonly List<(Image bg, TextMeshProUGUI lbl)> _playerBtns = new();
        readonly List<(Image bg, TextMeshProUGUI lbl)> _roundBtns  = new();
        readonly List<(Image bg, TextMeshProUGUI lbl, Outline border)> _modeBtns = new();

        public void Show()
        {
            gameObject.SetActive(true);
            BuildOnce();
            // Reset to defaults each open.
            _selectedCase = -1;
            _playerCount  = BattlePlayerCount.OneVOne;
            _rounds       = 1;
            _mode         = BattleMode.Normal;
            RefreshAll();
        }

        public void Hide() => gameObject.SetActive(false);

        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            var bg = MakeImage("Bg", rt, ColorPalette.BgDeep, raycast: true);
            Stretch(bg.rectTransform);

            BuildHeader(rt);
            BuildScrollContent(rt);
            BuildFooter(rt);
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.Surface, raycast: true);
            TopStrip(hdr.rectTransform, HeaderH);

            var back = NewGo("Back", hdr.transform, typeof(Image), typeof(Button));
            back.GetComponent<Image>().color = ColorPalette.CardBg;
            var backRt = (RectTransform)back.transform;
            SetRect(backRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, -6f), new Vector2(44f, 44f));
            var backLbl = MakeTmp(back.transform, "Lbl", "<", 22f, FontStyles.Bold, ColorPalette.TextBright);
            backLbl.alignment = TextAlignmentOptions.Center;
            Stretch(backLbl.rectTransform);
            var backBtn = back.GetComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(() => OnBack?.Invoke());

            var title = MakeTmp(hdr.transform, "Title", "CREATE BATTLE", 20f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -6f), new Vector2(0f, 28f));

            _totalChip = MakeTmp(hdr.transform, "TotalChip", "0 VP", 14f, FontStyles.Bold, ColorPalette.GoldAccent);
            _totalChip.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(_totalChip.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, -6f), new Vector2(110f, 24f));
        }

        void BuildScrollContent(RectTransform rt)
        {
            var scrollGo = NewGo("Scroll", rt, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero; scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0f, FooterH);
            scrollRt.offsetMax = new Vector2(0f, -HeaderH);

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var content = NewGo("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0f, 1f); cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f); cRt.anchoredPosition = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)SidePad, (int)SidePad, 14, 24);
            vlg.spacing = 18f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content = cRt; sr.viewport = (RectTransform)viewport.transform;
            sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 30f;

            BuildCaseSection(content.transform);
            BuildPlayerCountSection(content.transform);
            BuildRoundsSection(content.transform);
            BuildModeSection(content.transform);
        }

        void SectionLabel(Transform parent, string text)
        {
            var lbl = MakeTmp(parent, "SectionLabel", text, 11f, FontStyles.Bold, ColorPalette.ActiveRed);
            lbl.characterSpacing = 4f;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.gameObject.AddComponent<LayoutElement>().minHeight = 16f;
        }

        void BuildCaseSection(Transform parent)
        {
            SectionLabel(parent, "SELECT CASE");

            var rowGo = NewGo("CaseGrid", parent, typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
            rowGo.GetComponent<Image>().color = Color.clear;
            rowGo.GetComponent<LayoutElement>().minHeight = 130f;

            var vp = NewGo("VP", rowGo.transform, typeof(RectMask2D), typeof(Image));
            vp.GetComponent<Image>().color = Color.clear;
            Stretch(vp);

            var inner = NewGo("Inner", vp.transform, typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var iRt = (RectTransform)inner.transform;
            iRt.anchorMin = new Vector2(0f, 0.5f); iRt.anchorMax = new Vector2(0f, 0.5f);
            iRt.pivot = new Vector2(0f, 0.5f);
            var hlg = inner.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            inner.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = rowGo.GetComponent<ScrollRect>();
            sr.content = iRt; sr.viewport = (RectTransform)vp.transform;
            sr.horizontal = true; sr.vertical = false;

            for (int i = 0; i < CaseNames.Length; i++)
                BuildCaseCard(inner.transform, i);
        }

        void BuildCaseCard(Transform parent, int index)
        {
            var cardGo = NewGo("Case_" + index, parent, typeof(LayoutElement), typeof(Button));
            var le = cardGo.GetComponent<LayoutElement>();
            le.minWidth = 90f; le.preferredWidth = 90f; le.minHeight = 110f; le.preferredHeight = 110f;

            var bg = MakeAngled("Bg", cardGo.transform, ColorPalette.CardBg, CutCard, raycast: true);
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);
            var glow = bg.gameObject.AddComponent<Shadow>();
            glow.effectColor = Color.clear;
            glow.effectDistance = new Vector2(0f, 0f);

            var thumb = MakeImage("Thumb", cardGo.transform, ColorPalette.Surface);
            SetRect(thumb.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(60f, 60f));
            var tb = thumb.gameObject.AddComponent<Outline>();
            tb.effectColor = ColorPalette.WithAlpha(ColorPalette.ForRarity(CaseRarity[index]), 0.5f);
            tb.effectDistance = new Vector2(1f, -1f);

            var name = MakeTmp(cardGo.transform, "Name", CaseNames[index], 10f, FontStyles.Bold, ColorPalette.TextBright);
            name.alignment = TextAlignmentOptions.Top;
            name.enableWordWrapping = true;
            SetRect(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), new Vector2(-8f, 32f));

            int cap = index;
            var btn = cardGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { _selectedCase = cap; RefreshAll(); });

            _caseCards.Add((bg, border, glow));
        }

        (Image bg, TextMeshProUGUI lbl) BuildPill(Transform parent, string text, Action onClick)
        {
            var go = NewGo("Pill_" + text, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 48f; le.flexibleWidth = 1f;
            var img = go.GetComponent<Image>();
            img.color = ColorPalette.Border;
            var lbl = MakeTmp(go.transform, "Lbl", text, 14f, FontStyles.Bold, ColorPalette.TextBright);
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            return (img, lbl);
        }

        GameObject BuildButtonRow(Transform parent)
        {
            var row = NewGo("Row", parent, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.GetComponent<LayoutElement>().minHeight = 48f;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            return row;
        }

        void BuildPlayerCountSection(Transform parent)
        {
            SectionLabel(parent, "PLAYERS");
            var row = BuildButtonRow(parent);
            var defs = new (string, BattlePlayerCount)[]
            { ("1v1", BattlePlayerCount.OneVOne), ("1v1v1", BattlePlayerCount.ThreePlayer), ("1v1v1v1", BattlePlayerCount.FourPlayer) };
            foreach (var d in defs)
            {
                var pc = d.Item2;
                _playerBtns.Add(BuildPill(row.transform, d.Item1, () => { _playerCount = pc; RefreshAll(); }));
            }
        }

        void BuildRoundsSection(Transform parent)
        {
            SectionLabel(parent, "ROUNDS");
            var row = BuildButtonRow(parent);
            for (int i = 1; i <= 3; i++)
            {
                int r = i;
                _roundBtns.Add(BuildPill(row.transform, "x" + i, () => { _rounds = r; RefreshAll(); }));
            }
        }

        void BuildModeSection(Transform parent)
        {
            SectionLabel(parent, "MODE");
            var row = BuildButtonRow(parent);
            var normal = BuildPill(row.transform, "NORMAL", () => { _mode = BattleMode.Normal; RefreshAll(); });
            var crazy  = BuildPill(row.transform, "CRAZY",  () => { _mode = BattleMode.Crazy;  RefreshAll(); });

            var nBorder = normal.bg.gameObject.AddComponent<Outline>();
            nBorder.effectColor = Color.clear; nBorder.effectDistance = new Vector2(1f, -1f);
            var cBorder = crazy.bg.gameObject.AddComponent<Outline>();
            cBorder.effectColor = ColorPalette.GoldAccent; cBorder.effectDistance = new Vector2(1f, -1f);

            // HOT badge on crazy.
            var hot = MakeTmp(crazy.bg.transform, "Hot", "HOT", 8f, FontStyles.Bold, ColorPalette.BgDeep);
            var hotBg = MakeImage("HotBg", crazy.bg.transform, ColorPalette.GoldAccent);
            SetRect(hotBg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-2f, -2f), new Vector2(28f, 14f));
            hot.transform.SetParent(hotBg.transform, false);
            hot.alignment = TextAlignmentOptions.Center;
            Stretch(hot.rectTransform);

            _modeBtns.Add((normal.bg, normal.lbl, nBorder));
            _modeBtns.Add((crazy.bg, crazy.lbl, cBorder));
        }

        void BuildFooter(RectTransform rt)
        {
            var footer = MakeImage("Footer", rt, ColorPalette.Surface, raycast: true);
            BottomStrip(footer.rectTransform, FooterH);

            var costLbl = MakeTmp(footer.transform, "CostLbl", "TOTAL COST", 11f, FontStyles.Bold, ColorPalette.TextDim);
            costLbl.characterSpacing = 2f;
            costLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(costLbl.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad, -12f), new Vector2(0f, 18f));

            _footerCost = MakeTmp(footer.transform, "Cost", "0 VP", 16f, FontStyles.Bold, ColorPalette.GoldAccent);
            _footerCost.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(_footerCost.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-SidePad, -12f), new Vector2(0f, 20f));

            _confirmBg = MakeAngled("Confirm", footer.transform, ColorPalette.Border, 10f, raycast: true);
            SetRect(_confirmBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(358f, 52f));
            _confirmButton = _confirmBg.gameObject.AddComponent<Button>();
            _confirmButton.transition = Selectable.Transition.None;
            _confirmButton.onClick.AddListener(OnConfirmPressed);

            _confirmLabel = MakeTmp(_confirmBg.transform, "Lbl", "START BATTLE", 16f, FontStyles.Bold, ColorPalette.TextDim);
            _confirmLabel.alignment = TextAlignmentOptions.Center;
            Stretch(_confirmLabel.rectTransform);
        }

        int ComputeCost()
        {
            if (_selectedCase < 0) return 0;
            return CaseCosts[_selectedCase] * _rounds * (_mode == BattleMode.Crazy ? 2 : 1);
        }

        void RefreshAll()
        {
            // Case selection visuals.
            for (int i = 0; i < _caseCards.Count; i++)
            {
                bool sel = i == _selectedCase;
                var (bg, border, glow) = _caseCards[i];
                border.effectColor = sel ? ColorPalette.ActiveRed : ColorPalette.Border;
                border.effectDistance = sel ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                if (glow != null) glow.effectColor = sel ? ColorPalette.RedDim : Color.clear;
            }

            ApplyToggle(_playerBtns, PlayerIndex());
            ApplyToggle(_roundBtns, _rounds - 1);

            // Mode (normal keeps no border; crazy keeps gold border always).
            for (int i = 0; i < _modeBtns.Count; i++)
            {
                bool active = (i == 0 && _mode == BattleMode.Normal) || (i == 1 && _mode == BattleMode.Crazy);
                _modeBtns[i].bg.color  = active ? ColorPalette.ActiveRed : ColorPalette.Border;
                _modeBtns[i].lbl.color = active ? Color.white : ColorPalette.TextBright;
            }

            int cost = ComputeCost();
            _totalChip.text  = cost.ToString("N0") + " VP";
            _footerCost.text = cost.ToString("N0") + " VP";

            bool enabled = _selectedCase >= 0;
            _confirmBg.color         = enabled ? ColorPalette.ActiveRed : ColorPalette.Border;
            _confirmLabel.color      = enabled ? ColorPalette.TextBright : ColorPalette.TextDim;
            _confirmButton.interactable = enabled;
        }

        int PlayerIndex()
        {
            switch (_playerCount)
            {
                case BattlePlayerCount.ThreePlayer: return 1;
                case BattlePlayerCount.FourPlayer:  return 2;
                default: return 0;
            }
        }

        static void ApplyToggle(List<(Image bg, TextMeshProUGUI lbl)> btns, int activeIndex)
        {
            for (int i = 0; i < btns.Count; i++)
            {
                bool active = i == activeIndex;
                btns[i].bg.color  = active ? ColorPalette.ActiveRed : ColorPalette.Border;
                btns[i].lbl.color = active ? Color.white : ColorPalette.TextBright;
            }
        }

        void OnConfirmPressed()
        {
            if (_selectedCase < 0) return;
            StartCoroutine(UIAnimator.ScalePress(_confirmBg.transform, 0.97f, 0.12f));

            var data = new BattleLobbyData
            {
                LobbyId        = GenId(),
                HostName       = "YOU",
                CaseName       = CaseNames[_selectedCase],
                Rounds         = _rounds,
                Mode           = _mode,
                PlayerCount    = _playerCount,
                CurrentPlayers = 1,
                Status         = LobbyStatus.Waiting,
                Rarity         = CaseRarity[_selectedCase],
                WagerVP        = ComputeCost()
            };
            OnConfirm?.Invoke(data);
        }

        static string GenId()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
            var arr = new char[4];
            for (int i = 0; i < 4; i++) arr[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            return new string(arr);
        }

        void OnDisable() => StopAllCoroutines();
    }
}
