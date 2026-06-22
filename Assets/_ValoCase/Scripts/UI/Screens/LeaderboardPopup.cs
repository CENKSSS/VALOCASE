using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Profile;
using ValoCase.Services.Backend;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    // Battle leaderboard modal. Read-only: fetches GET /leaderboards per tab, caches each
    // tab while open, and renders the top 10 plus the current player's "YOU" row.
    public sealed class LeaderboardPopup : MonoBehaviour
    {
        static readonly string[] TabTypes  = { "MOST_BATTLES", "BEST_BATTLE_WIN_RATE", "HIGHEST_WALLET_VALUE" };
        static readonly string[] TabLabels = { "MOST BATTLES", "WIN RATE", "WALLET" };

        bool _built;
        int  _activeTab;

        readonly LeaderboardResponse[] _cache    = new LeaderboardResponse[3];
        readonly bool[]                _loading  = new bool[3];

        readonly Image[]           _tabBgs  = new Image[3];
        readonly TextMeshProUGUI[] _tabLbls = new TextMeshProUGUI[3];

        RectTransform _listContent;
        readonly List<GameObject> _rowGos = new();

        GameObject      _stateGo;
        TextMeshProUGUI _stateLbl;
        GameObject      _retryBtn;

        GameObject      _youPanel;
        TextMeshProUGUI _youRank;
        TextMeshProUGUI _youName;
        TextMeshProUGUI _youValue;
        TextMeshProUGUI _youSecondary;

        public void Show()
        {
            Build();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            for (int i = 0; i < _cache.Length; i++) _cache[i] = null;
            SelectTab(0);
        }

        public void Hide() => gameObject.SetActive(false);

        // ── Build ──────────────────────────────────────────────────────────────
        void Build()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;
            Stretch(rt);

            var overlay = gameObject.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.82f);
            overlay.raycastTarget = true;
            var dismiss = gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            var panel = MakeImage("Panel", rt, ColorPalette.BgDeep, raycast: true);
            var pRt = panel.rectTransform;
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.offsetMin = new Vector2(16f, 16f);
            pRt.offsetMax = new Vector2(-16f, -16f);
            var pBorder = panel.gameObject.AddComponent<Outline>();
            pBorder.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.45f);
            pBorder.effectDistance = new Vector2(1.5f, -1.5f);

            var topAccent = MakeImage("TopAccent", panel.transform, ColorPalette.ActiveRed);
            SetRect(topAccent.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 3f));

            var backBg = MakeImage("Back", panel.transform, ColorPalette.Surface, raycast: true);
            SetRect(backBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -12f), new Vector2(36f, 36f));
            var backBorder = backBg.gameObject.AddComponent<Outline>();
            backBorder.effectColor    = ColorPalette.Border;
            backBorder.effectDistance = new Vector2(1f, -1f);
            var backArrow = MakeTmp(backBg.transform, "Arrow", "<", 26f, FontStyles.Bold, ColorPalette.TextBright);
            backArrow.alignment = TextAlignmentOptions.Center;
            SetRect(backArrow.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(1f, 1f), Vector2.zero);
            var backBtn = backBg.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Hide);

            var title = MakeTmp(panel.transform, "Title", "LEADERBOARD", 27f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(-104f, 36f));

            BuildTabs(panel.transform);
            BuildScroll(panel.transform);
            BuildStateOverlay(panel.transform);
            BuildYouPanel(panel.transform);
        }

        void BuildTabs(Transform panel)
        {
            var rowGo = NewGo("Tabs", panel, typeof(HorizontalLayoutGroup));
            var rowRt = (RectTransform)rowGo.transform;
            SetRect(rowRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -64f), new Vector2(-24f, 34f));
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 6f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                var bg = MakeImage("Tab_" + i, rowGo.transform, ColorPalette.Surface, raycast: true);
                bg.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var border = bg.gameObject.AddComponent<Outline>();
                border.effectColor    = ColorPalette.Border;
                border.effectDistance = new Vector2(1f, -1f);

                var lbl = MakeTmp(bg.transform, "Lbl", TabLabels[i], 13.5f, FontStyles.Bold, ColorPalette.TextDim);
                lbl.alignment = TextAlignmentOptions.Center;
                lbl.characterSpacing = 1f;
                Stretch(lbl.rectTransform);

                var btn = bg.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SelectTab(captured));

                _tabBgs[i]  = bg;
                _tabLbls[i] = lbl;
            }
        }

        void BuildScroll(Transform panel)
        {
            var scrollGo = NewGo("Scroll", panel, typeof(ScrollRect), typeof(Image), typeof(RectMask2D));
            scrollGo.GetComponent<Image>().color = Color.clear;
            var sRt = (RectTransform)scrollGo.transform;
            sRt.anchorMin = Vector2.zero;
            sRt.anchorMax = Vector2.one;
            sRt.offsetMin = new Vector2(10f, 86f);
            sRt.offsetMax = new Vector2(-10f, -104f);

            var content = NewGo("Content", scrollGo.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin        = new Vector2(0f, 1f);
            _listContent.anchorMax        = new Vector2(1f, 1f);
            _listContent.pivot            = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta        = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing               = 6f;
            vlg.padding               = new RectOffset(0, 0, 2, 8);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content          = _listContent;
            sr.viewport         = sRt;
            sr.horizontal       = false;
            sr.vertical         = true;
            sr.scrollSensitivity = 28f;
            sr.movementType     = ScrollRect.MovementType.Elastic;
        }

        void BuildStateOverlay(Transform panel)
        {
            _stateGo = MakeImage("State", panel, Color.clear).gameObject;
            var stRt = (RectTransform)_stateGo.transform;
            stRt.anchorMin = Vector2.zero;
            stRt.anchorMax = Vector2.one;
            stRt.offsetMin = new Vector2(10f, 86f);
            stRt.offsetMax = new Vector2(-10f, -104f);

            _stateLbl = MakeTmp(_stateGo.transform, "Lbl", "", 13f, FontStyles.Bold, ColorPalette.TextDim);
            _stateLbl.alignment = TextAlignmentOptions.Center;
            _stateLbl.enableWordWrapping = true;
            _stateLbl.characterSpacing = 2f;
            SetRect(_stateLbl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 16f), new Vector2(280f, 60f));

            var retryBg = MakeImage("Retry", _stateGo.transform, ColorPalette.Surface, raycast: true);
            SetRect(retryBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -28f), new Vector2(132f, 38f));
            var retryBorder = retryBg.gameObject.AddComponent<Outline>();
            retryBorder.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.7f);
            retryBorder.effectDistance = new Vector2(1f, -1f);
            var retryLbl = MakeTmp(retryBg.transform, "Lbl", "TEKRAR DENE", 12f, FontStyles.Bold, ColorPalette.TextBright);
            retryLbl.alignment = TextAlignmentOptions.Center;
            Stretch(retryLbl.rectTransform);
            var retryButton = retryBg.gameObject.AddComponent<Button>();
            retryButton.transition = Selectable.Transition.None;
            retryButton.onClick.AddListener(() => FetchTab(_activeTab));
            _retryBtn = retryBg.gameObject;

            _stateGo.SetActive(false);
        }

        void BuildYouPanel(Transform panel)
        {
            _youPanel = MakeImage("YouPanel", panel, ColorPalette.Surface).gameObject;
            var yRt = (RectTransform)_youPanel.transform;
            yRt.anchorMin = new Vector2(0f, 0f);
            yRt.anchorMax = new Vector2(1f, 0f);
            yRt.pivot     = new Vector2(0.5f, 0f);
            yRt.anchoredPosition = new Vector2(0f, 12f);
            yRt.sizeDelta = new Vector2(-20f, 62f);
            var yBorder = _youPanel.AddComponent<Outline>();
            yBorder.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.85f);
            yBorder.effectDistance = new Vector2(1.5f, -1.5f);

            var accent = MakeImage("Accent", _youPanel.transform, ColorPalette.ActiveRed);
            SetRect(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(3.5f, 0f));

            var tag = MakeTmp(_youPanel.transform, "Tag", "YOU", 12f, FontStyles.Bold, ColorPalette.ActiveRed);
            tag.alignment = TextAlignmentOptions.TopLeft;
            tag.characterSpacing = 2f;
            SetRect(tag.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -7f), new Vector2(64f, 16f));

            _youRank = MakeTmp(_youPanel.transform, "Rank", "-", 19f, FontStyles.Bold, ColorPalette.GoldAccent);
            _youRank.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(_youRank.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(14f, -4f), new Vector2(72f, 0f));

            _youName = MakeTmp(_youPanel.transform, "Name", "-", 17.5f, FontStyles.Bold, ColorPalette.TextBright);
            _youName.alignment = TextAlignmentOptions.MidlineLeft;
            var nRt = _youName.rectTransform;
            nRt.anchorMin = new Vector2(0f, 0f); nRt.anchorMax = new Vector2(1f, 1f);
            nRt.offsetMin = new Vector2(90f, 0f); nRt.offsetMax = new Vector2(-100f, -2f);

            _youValue = MakeTmp(_youPanel.transform, "Value", "-", 18.5f, FontStyles.Bold, ColorPalette.GoldAccent);
            _youValue.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(_youValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-14f, 10f), new Vector2(100f, 24f));

            _youSecondary = MakeTmp(_youPanel.transform, "Secondary", "", 13f, FontStyles.Normal, ColorPalette.TextDim);
            _youSecondary.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(_youSecondary.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-14f, -10f), new Vector2(122f, 18f));

            _youPanel.SetActive(false);
        }

        // ── Tab + fetch ────────────────────────────────────────────────────────
        void SelectTab(int tab)
        {
            _activeTab = tab;
            UpdateTabVisuals();

            if (_cache[tab] != null) { RenderData(_cache[tab]); return; }
            FetchTab(tab);
        }

        void UpdateTabVisuals()
        {
            for (int i = 0; i < 3; i++)
            {
                bool on = i == _activeTab;
                if (_tabBgs[i]  != null) _tabBgs[i].color  = on ? ColorPalette.ActiveRed : ColorPalette.Surface;
                if (_tabLbls[i] != null) _tabLbls[i].color = on ? ColorPalette.TextBright : ColorPalette.TextDim;
            }
        }

        void FetchTab(int tab)
        {
            if (_loading[tab]) return;
            _loading[tab] = true;
            ClearRows();
            if (_youPanel != null) _youPanel.SetActive(false);
            SetState("LOADING…", retry: false);

            var ctx = GameContext.Instance;
            if (ctx == null)
            {
                _loading[tab] = false;
                SetState("Sunucu kullanılamıyor.", retry: true);
                return;
            }

            ctx.RefreshLeaderboardBackend(TabTypes[tab],
                res =>
                {
                    if (this == null) return;
                    _loading[tab] = false;
                    _cache[tab] = res;
                    if (tab == _activeTab) RenderData(res);
                },
                err =>
                {
                    if (this == null) return;
                    _loading[tab] = false;
                    if (tab == _activeTab) SetState(string.IsNullOrEmpty(err) ? "İşlem başarısız." : err, retry: true);
                });
        }

        // ── Render ─────────────────────────────────────────────────────────────
        void RenderData(LeaderboardResponse res)
        {
            ClearRows();

            var entries = res?.entries;
            int count = entries != null ? Mathf.Min(25, entries.Length) : 0;

            if (count == 0)
            {
                SetState("HENÜZ SIRALAMA YOK", retry: false);
            }
            else
            {
                HideState();
                for (int i = 0; i < count; i++)
                {
                    var e = entries[i];
                    if (e == null) continue;
                    Color rankColor = e.rank <= 3 ? ColorPalette.GoldAccent : ColorPalette.TextDim;
                    BuildRow("#" + e.rank, rankColor, e.displayName, FormatMain(_activeTab, e.value),
                        e.secondaryValue, e.avatarKey, false);
                }
            }

            RenderYou(res?.me);
        }

        void RenderYou(LeaderboardMeResponse me)
        {
            if (_youPanel == null) return;
            if (me == null) { _youPanel.SetActive(false); return; }

            _youPanel.SetActive(true);
            _youRank.text = MeRankText(me);
            _youName.text = string.IsNullOrEmpty(me.displayName) ? "—" : me.displayName;
            _youValue.text = FormatMain(_activeTab, me.value);
            _youSecondary.text = me.secondaryValue ?? "";
        }

        GameObject BuildRow(string rankText, Color rankColor, string name, string mainVal,
                            string secondary, string avatarKey, bool isMe)
        {
            var row = MakeImage("Row", _listContent, isMe ? ColorPalette.Surface : ColorPalette.CardBg);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 54f;
            var border = row.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            var rank = MakeTmp(row.transform, "Rank", rankText, 18f, FontStyles.Bold, rankColor);
            rank.alignment = TextAlignmentOptions.Center;
            SetRect(rank.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 0f), new Vector2(36f, 26f));

            var avatar = MakeImage("Avatar", row.transform, ColorPalette.Surface);
            var avatarSprite = ProfileManager.ResolveAvatarSprite(avatarKey);
            avatar.sprite        = avatarSprite;
            avatar.color         = avatarSprite != null ? Color.white : ColorPalette.Surface;
            avatar.preserveAspect = true;
            SetRect(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(46f, 0f), new Vector2(38f, 38f));

            var nm = MakeTmp(row.transform, "Name", string.IsNullOrEmpty(name) ? "—" : name, 17.5f, FontStyles.Bold, ColorPalette.TextBright);
            nm.alignment = TextAlignmentOptions.MidlineLeft;
            var nmRt = nm.rectTransform;
            nmRt.anchorMin = new Vector2(0f, 0f); nmRt.anchorMax = new Vector2(1f, 1f);
            nmRt.offsetMin = new Vector2(92f, 0f); nmRt.offsetMax = new Vector2(-100f, 0f);

            bool hasSecondary = !string.IsNullOrEmpty(secondary);

            var val = MakeTmp(row.transform, "Value", mainVal, 18.5f, FontStyles.Bold, ColorPalette.TextBright);
            val.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(val.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-12f, hasSecondary ? 9f : 0f), new Vector2(94f, 24f));

            if (hasSecondary)
            {
                var sec = MakeTmp(row.transform, "Secondary", secondary, 13f, FontStyles.Normal, ColorPalette.TextDim);
                sec.alignment = TextAlignmentOptions.MidlineRight;
                SetRect(sec.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-12f, -10f), new Vector2(112f, 18f));
            }

            _rowGos.Add(row.gameObject);
            return row.gameObject;
        }

        void ClearRows()
        {
            foreach (var go in _rowGos) Destroy(go);
            _rowGos.Clear();
        }

        void SetState(string text, bool retry)
        {
            if (_stateGo == null) return;
            _stateGo.SetActive(true);
            _stateGo.transform.SetAsLastSibling();
            if (_stateLbl != null) _stateLbl.text = text;
            if (_retryBtn != null) _retryBtn.SetActive(retry);
        }

        void HideState()
        {
            if (_stateGo != null) _stateGo.SetActive(false);
        }

        // ── Formatting ─────────────────────────────────────────────────────────
        static string FormatMain(int tab, long value)
        {
            switch (tab)
            {
                case 1:  return value + "%";
                case 2:  return value.ToString("N0") + " VP";
                default: return value.ToString();
            }
        }

        static string MeRankText(LeaderboardMeResponse me)
        {
            if (!string.IsNullOrEmpty(me.rankLabel)) return me.rankLabel;
            return me.rank > 0 ? "#" + me.rank : "—";
        }
    }
}
