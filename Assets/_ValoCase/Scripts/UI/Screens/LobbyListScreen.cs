using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Battle;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Progression;
using ValoCase.Services.Backend;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Case Battle Lobby — entry screen for the battle flow.
    ///
    /// Built from the Case Battle reference design system: a dark depth hierarchy
    /// (BgDeep / CardBg / Surface), a single red accent (ActiveRed), gold for value
    /// (GoldAccent) and the Valorant angular-corner motif (AngledCutImage). Layout,
    /// spacing and density follow the reference; colours stay on the project palette.
    ///
    /// Sections (top → bottom): header + VP balance · 5 quick-stat cards ·
    /// CREATE LOBBY button · ACTIVE BATTLES header · scrollable bot-lobby list.
    ///
    /// Hosts CreateBattleScreen and WaitingRoomScreen as internal overlays so the
    /// existing create/join flow stays self-contained (no new ScreenType entries).
    /// </summary>
    public sealed class LobbyListScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;

        // ── Layout constants (≈390-wide logical mobile space) ──────────────────
        const float SidePad     = 16f;
        const float TopInset    = 14f;
        const float HeaderH     = 48f;
        const float Gap         = 12f;
        const float StatsH      = 117f;
        const float CreateH     = 104f;
        const float SectionH    = 44f;
        const float NavReserve  = 16f;    // bottom nav space reserved by shared Screens host
        const float CreateW     = 440f;
        const float CreateBottomInset = 58f;
        const float ListChromeTop = TopInset + HeaderH + Gap + StatsH + Gap + SectionH;
        const float MinListH    = 120f;
        const int TableHorizontalPad = 14;
        const float LobbyListErrorToastCooldown = 12f;

        bool _built;
        RectTransform _scrollRt;

        TextMeshProUGUI _balanceLbl;
        TextMeshProUGUI _activeCountLbl;
        AngledCutImage  _createBg;
        RectTransform   _listContent;

        CreateBattleScreen _createPanel;
        WaitingRoomScreen  _waitingPanel;

        readonly List<GameObject> _cardGos = new();
        List<BattleLobbyData>     _lobbies = new();

        // Online public-lobby state. When the backend is ready the list is fetched from
        // GET /battles/lobbies every ~3s; otherwise the legacy bot lobbies are shown as a
        // dev/offline fallback (the old /battles/bot flow stays available behind them).
        bool      _onlineMode;
        bool      _botFallbackShown;
        bool      _creating;
        bool      _lobbyListHadSuccessfulFetch;
        int       _lobbyListFetchFailures;
        float     _nextLobbyListErrorToastAt;
        Coroutine _refreshCo;

        // Resolved once from the content database.
        Sprite           _basicCaseIcon;
        CaseDefinitionSO _basicCase;

        // Live stat card value labels — populated in BuildStatsRow, updated in RefreshStats.
        TextMeshProUGUI _statBattles;
        TextMeshProUGUI _statWinRate;
        TextMeshProUGUI _statEarned;
        TextMeshProUGUI _statLost;
        TextMeshProUGUI _statStreak;

        // True while the Battle Room overlay is up (ready, running or finished).
        // While open, this screen survives navigation (e.g. Settings) without
        // resetting the battle — only an explicit Leave closes the room.
        bool BattleViewOpen => _waitingPanel != null && _waitingPanel.IsOpen;

        protected override bool KeepAliveWhenHidden => BattleViewOpen;

        // Battle tab re-tapped while already on this screen: always return to the PvP
        // lobby list. Closes the Create Battle overlay, and leaves the waiting room only
        // when that is safe (never interrupts a starting/running battle).
        public override void OnReselected()
        {
            if (_createPanel != null && _createPanel.gameObject.activeSelf)
            {
                CloseCreate();
                SetLobbyChromeActive(true);
                return;
            }

            if (_waitingPanel != null && _waitingPanel.IsOpen)
            {
                if (_waitingPanel.CanLeaveSafely)
                    _waitingPanel.RequestLeaveExternally();
                return;
            }
        }

        protected override void OnShown()
        {
            BuildOnce();

            var rt = (RectTransform)transform;
            rt.anchoredPosition = Vector2.zero;

            if (BattleViewOpen)
            {
                Debug.Log("[ONLINE_BATTLE] battle screen shown after navbar return — preserving state");
                _waitingPanel.NotifyReturnedToScreen();
                return;
            }

            _createPanel?.Hide();
            _waitingPanel?.Hide();
            SetLobbyChromeActive(true);   // restore lobby if returning from a battle
            RefreshBalance();
            RefreshStats();
            StartLobbyRefresh();          // live online list (or bot fallback)

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            Debug.Log("[ONLINE_BATTLE] lobby layout rebuilt on show");
            StartCoroutine(UIAnimator.SlideFromBottom(rt, 0.25f));
        }

        protected override void OnHidden()
        {
            StopAllCoroutines();
            _creating = false;
            _createPanel?.SetConfirmInFlight(false);

            // Battle room stays alive while Settings (or another screen) is open;
            // its coroutines keep running because the GameObject is not deactivated.
            if (BattleViewOpen) return;

            _createPanel?.Hide();
            _waitingPanel?.Hide();
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _creating = false;
            _createPanel?.SetConfirmInFlight(false);
        }

        // ── Build ──────────────────────────────────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            EnsureCanvasGroup(gameObject);
            var rt = (RectTransform)transform;

            var bg = MakeImage("Bg", rt, ColorPalette.BgDeep, raycast: true);
            Stretch(bg.rectTransform);
            bg.raycastTarget = false;

            BuildHeader(rt);
            BuildStatsRow(rt);
            BuildSectionHeader(rt);
            BuildScrollList(rt);
            BuildCreateButton(rt);
            BuildSubPanels(rt);

            // The lobby list is filled by StartLobbyRefresh() (online fetch or, when the
            // backend is unavailable, the legacy bot lobbies). Nothing is built here so a
            // stale bot list never flashes before the first online refresh.
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = NewGo("Header", rt);
            var hRt = (RectTransform)hdr.transform;
            TopStrip(hRt, HeaderH, -TopInset);

            // Red live dot.
            var dot = MakeImage("LiveDot", hdr.transform, ColorPalette.ActiveRed);
            SetRect(dot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad, -4f), new Vector2(8f, 8f));

            // Title + subtitle (left).
            var title = MakeTmp(hdr.transform, "Title", "CASE BATTLE", 20f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 2f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad + 14f, -2f), new Vector2(-180f, 24f));

            var sub = MakeTmp(hdr.transform, "Sub", "BOT LOBBY ARENA", 9f, FontStyles.Normal, ColorPalette.TextDim);
            sub.characterSpacing = 3f;
            sub.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad + 14f, -28f), new Vector2(-180f, 14f));

            // VP balance chip (right).
            var chip = MakeAngled("BalanceChip", hdr.transform, ColorPalette.Surface, 5f);
            SetRect(chip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-SidePad, -8f), new Vector2(108f, 32f));
            var chipBorder = chip.gameObject.AddComponent<Outline>();
            chipBorder.effectColor    = ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.45f);
            chipBorder.effectDistance = new Vector2(1f, -1f);

            var coin = MakeAngled("Coin", chip.transform, ColorPalette.GoldAccent, 2f);
            SetRect(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(12f, 12f));

            _balanceLbl = MakeTmp(chip.transform, "Balance", "—", 13f, FontStyles.Bold, ColorPalette.GoldAccent);
            _balanceLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(_balanceLbl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                new Vector2(28f, 0f), new Vector2(-34f, 0f));

            chip.gameObject.SetActive(false);   // wallet shown in top navbar
        }

        enum StatIcon { Trophy, Crosshair, Coin, Defeat, Star }

        void BuildStatsRow(RectTransform rt)
        {
            var rowGo = NewGo("StatsRow", rt, typeof(HorizontalLayoutGroup));
            var rowRt = (RectTransform)rowGo.transform;
            TopStrip(rowRt, StatsH, -(TopInset + HeaderH + Gap));
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset((int)SidePad, (int)SidePad, 0, 0);
            hlg.spacing               = 6f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleCenter;

            _statBattles = BuildStatCard(rowGo.transform, ColorPalette.ActiveRed,  "0", ColorPalette.TextBright, "BATTLES", StatIcon.Trophy);
            _statWinRate = BuildStatCard(rowGo.transform, ColorPalette.ActiveRed,  "0%", ColorPalette.TextBright, "WIN %",   StatIcon.Crosshair);
            _statEarned  = BuildStatCard(rowGo.transform, ColorPalette.GoldAccent, "0", ColorPalette.GoldAccent, "EARNED",  StatIcon.Coin);
            _statLost    = BuildStatCard(rowGo.transform, ColorPalette.RedDim,     "0", ColorPalette.TextBright, "LOST",    StatIcon.Defeat);
            _statStreak  = BuildStatCard(rowGo.transform, ColorPalette.GoldAccent, "0", ColorPalette.GoldAccent, "STREAK",  StatIcon.Star);
        }

        TextMeshProUGUI BuildStatCard(Transform parent, Color iconColor, string value, Color valueColor, string label, StatIcon iconType)
        {
            var card = MakeImage("Stat_" + label, parent, ColorPalette.CardBg);
            var le = card.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth      = 48f;
            var border = card.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            var iconRoot = NewGo("Icon", card.transform);
            var iconRt   = (RectTransform)iconRoot.transform;
            SetRect(iconRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -17f), new Vector2(15f, 15f));
            iconRt.localScale = Vector3.one * 1.5f;
            switch (iconType)
            {
                case StatIcon.Trophy:    BuildTrophyIcon(iconRoot.transform, iconColor);    break;
                case StatIcon.Crosshair: BuildCrosshairIcon(iconRoot.transform, iconColor); break;
                case StatIcon.Coin:      BuildCoinIcon(iconRoot.transform, iconColor);      break;
                case StatIcon.Defeat:    BuildDefeatIcon(iconRoot.transform, iconColor);    break;
                case StatIcon.Star:      BuildStarIcon(iconRoot.transform, iconColor);      break;
            }

            var val = MakeTmp(card.transform, "Value", value, 22.5f, FontStyles.Bold, valueColor);
            val.alignment = TextAlignmentOptions.Center;
            SetRect(val.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -46f), new Vector2(-4f, 30f));

            var lbl = MakeTmp(card.transform, "Label", label, 12f, FontStyles.Bold, ColorPalette.TextDim);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.characterSpacing = 1f;
            SetRect(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(-2f, 18f));

            return val;
        }

        // Returns a centered bar Image inside a 15x15 icon root.
        Image IconBar(string n, Transform p, Color c, float x, float y, float w, float h)
        {
            var img = MakeImage(n, p, c);
            var r   = img.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta        = new Vector2(w, h);
            return img;
        }

        // Trophy: cup outline + handles + stem + base.
        void BuildTrophyIcon(Transform p, Color c)
        {
            IconBar("T",  p, c,  0f,     +4.5f, 9f,   1.5f);   // cup top
            IconBar("L",  p, c, -3.75f,  +1f,   1.5f, 7f);     // left wall
            IconBar("R",  p, c, +3.75f,  +1f,   1.5f, 7f);     // right wall
            IconBar("B",  p, c,  0f,    -2.5f,  9f,   1.5f);   // cup bottom
            IconBar("LH", p, c, -5.5f,   +1f,   2.5f, 3.5f);   // left handle
            IconBar("RH", p, c, +5.5f,   +1f,   2.5f, 3.5f);   // right handle
            IconBar("St", p, c,  0f,    -4.25f, 1.5f, 3.5f);   // stem
            IconBar("Ba", p, c,  0f,    -6.5f,  7f,   1.5f);   // base
        }

        // Crosshair: four line segments with center gap + center dot.
        void BuildCrosshairIcon(Transform p, Color c)
        {
            IconBar("T", p, c,  0f,    +4.5f, 1.5f, 3f);
            IconBar("B", p, c,  0f,   -4.5f,  1.5f, 3f);
            IconBar("L", p, c, -4.5f,   0f,   3f,   1.5f);
            IconBar("R", p, c, +4.5f,   0f,   3f,   1.5f);
            IconBar("C", p, c,  0f,     0f,   2f,   2f);
        }

        // Coin: gold ring using outer filled shape with inner CardBg overlay.
        void BuildCoinIcon(Transform p, Color c)
        {
            var outer = MakeAngled("Outer", p, c, 4f);
            outer.rectTransform.anchorMin = outer.rectTransform.anchorMax = outer.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            outer.rectTransform.anchoredPosition = Vector2.zero;
            outer.rectTransform.sizeDelta        = new Vector2(13f, 13f);

            var inner = MakeAngled("Inner", p, ColorPalette.CardBg, 3f);
            inner.rectTransform.anchorMin = inner.rectTransform.anchorMax = inner.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            inner.rectTransform.anchoredPosition = Vector2.zero;
            inner.rectTransform.sizeDelta        = new Vector2(8f, 8f);
        }

        // Defeat: two diagonal lines forming an X.
        void BuildDefeatIcon(Transform p, Color c)
        {
            var l1 = IconBar("L1", p, c, 0f, 0f, 1.5f, 12f);
            l1.rectTransform.localRotation = Quaternion.Euler(0f, 0f,  45f);

            var l2 = IconBar("L2", p, c, 0f, 0f, 1.5f, 12f);
            l2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        // Streak: 8-ray starburst (H + V + two diagonals) with center dot.
        void BuildStarIcon(Transform p, Color c)
        {
            IconBar("H", p, c, 0f, 0f, 13f, 2f);
            IconBar("V", p, c, 0f, 0f, 2f, 13f);

            var d1 = IconBar("D1", p, c, 0f, 0f, 2f, 13f);
            d1.rectTransform.localRotation = Quaternion.Euler(0f, 0f,  45f);

            var d2 = IconBar("D2", p, c, 0f, 0f, 2f, 13f);
            d2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);

            IconBar("C", p, c, 0f, 0f, 3f, 3f);
        }

        void BuildCreateButton(RectTransform rt)
        {
            ColorUtility.TryParseHtmlString("#FF003C", out var createRed);
            
            _createBg = MakeAngled("CreateBtn", rt, createRed, 10f, raycast: true);
            
            var createGlow = _createBg.gameObject.AddComponent<Shadow>();
            createGlow.effectColor = ColorPalette.WithAlpha(createRed, 0.75f);
            createGlow.effectDistance = new Vector2(0f, -4f);

            var cRt = _createBg.rectTransform;
            cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(0.5f, 0f);
            cRt.anchoredPosition = new Vector2(0f, NavReserve + CreateBottomInset);
            cRt.sizeDelta = new Vector2(CreateW, CreateH);
            _createBg.transform.SetAsLastSibling();

            var btn = _createBg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnCreatePressed);

            var plus = MakeTmp(_createBg.transform, "Plus", "+", 24f, FontStyles.Bold, Color.white);
            plus.alignment = TextAlignmentOptions.Center;
            SetRect(plus.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(30f, 0f), new Vector2(26f, 26f));

            var lbl = MakeTmp(_createBg.transform, "Lbl", "CREATE LOBBY", 15f, FontStyles.Bold, Color.white);
            lbl.characterSpacing = 2f;
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);
        }

        void BuildSectionHeader(RectTransform rt)
        {
            var sec = NewGo("SectionHeader", rt);
            var sRt = (RectTransform)sec.transform;
            TopStrip(sRt, SectionH, -(TopInset + HeaderH + Gap + StatsH + Gap));

            var panel = MakeImage("LobbyTableHeader", sec.transform, ColorPalette.Surface);
            var pRt = panel.rectTransform;
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.offsetMin = new Vector2(TableHorizontalPad, 5f);
            pRt.offsetMax = new Vector2(-TableHorizontalPad, -5f);

            var border = panel.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            AddCasesHeaderCell(panel.transform);
            AddCenteredHeaderCell(panel.transform, "COST", LobbyCard.CostCenterA, 0.10f);
            AddCenteredHeaderCell(panel.transform, "PLAYERS", LobbyCard.PlayersCenterA, 0.085f);
        }

        void AddHeaderCell(Transform parent, string text, float minX, float maxX, TextAlignmentOptions align)
        {
            var cell = NewGo("Header_" + (string.IsNullOrEmpty(text) ? "Action" : text), parent);
            var cRt = (RectTransform)cell.transform;
            cRt.anchorMin = new Vector2(minX, 0f);
            cRt.anchorMax = new Vector2(maxX, 1f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;

            var lbl = MakeTmp(cell.transform, "Lbl", text, 11.5f, FontStyles.Bold, ColorPalette.TextDim);
            lbl.alignment = align;
            lbl.characterSpacing = 1f;
            Stretch(lbl.rectTransform);
            if (align == TextAlignmentOptions.MidlineLeft)
            {
                lbl.rectTransform.offsetMin = Vector2.zero;
                lbl.rectTransform.offsetMax = Vector2.zero;
            }
        }

        void AddCasesHeaderCell(Transform parent)
        {
            float slotW = (LobbyCard.CostA - LobbyCard.CasesA) / LobbyCard.MaxCaseSlots;
            AddHeaderCell(parent, "CASES", LobbyCard.CasesA, LobbyCard.CasesA + slotW * 0.72f, TextAlignmentOptions.Center);
        }

        void AddCenteredHeaderCell(Transform parent, string text, float centerX, float width)
        {
            AddHeaderCell(parent, text, centerX - width * 0.5f, centerX + width * 0.5f, TextAlignmentOptions.Center);
        }

        void BuildScrollList(RectTransform rt)
        {
            var scrollGo = NewGo("Scroll", rt, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;
            _scrollRt = (RectTransform)scrollGo.transform;
            _scrollRt.anchorMin = new Vector2(0f, 1f);
            _scrollRt.anchorMax = new Vector2(1f, 1f);
            _scrollRt.pivot     = new Vector2(0.5f, 1f);
            SizeListViewport();

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var content = NewGo("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin        = new Vector2(0f, 1f);
            _listContent.anchorMax        = new Vector2(1f, 1f);
            _listContent.pivot            = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta        = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding               = new RectOffset(TableHorizontalPad, TableHorizontalPad, 0, 160);
            vlg.spacing               = 0f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content          = _listContent;
            sr.viewport         = (RectTransform)viewport.transform;
            sr.horizontal       = false;
            sr.vertical         = true;
            sr.scrollSensitivity = 30f;
            sr.movementType     = ScrollRect.MovementType.Elastic;
        }

        // Keeps the list below the fixed top chrome and above the reserved bottom, with a
        // clamped minimum so a short usable area scrolls instead of inverting the viewport.
        void SizeListViewport()
        {
            if (_scrollRt == null) return;
            float parentH = ((RectTransform)transform).rect.height;
            float h = parentH > 1f
                ? Mathf.Max(MinListH, parentH - ListChromeTop - NavReserve)
                : MinListH;
            _scrollRt.offsetMax = new Vector2(0f, -ListChromeTop);
            _scrollRt.offsetMin = new Vector2(0f, -(ListChromeTop + h));
        }

        void OnRectTransformDimensionsChange()
        {
            if (_built) SizeListViewport();
        }

        void BuildSubPanels(RectTransform rt)
        {
            var createGo = NewGo("CreatePanel", rt);
            Stretch(createGo);
            _createPanel = createGo.AddComponent<CreateBattleScreen>();
            _createPanel.OnBack    += CloseCreate;
            _createPanel.OnConfirm += OpenWaitingFromCreate;
            createGo.SetActive(false);

            var waitGo = NewGo("WaitingPanel", rt);
            Stretch(waitGo);
            _waitingPanel = waitGo.AddComponent<WaitingRoomScreen>();
            _waitingPanel.OnLeave       += CloseWaiting;
            _waitingPanel.OnStartBattle += OnBattleStart;
            waitGo.SetActive(false);
        }

        // ── List population ──────────────────────────────────────────────────────
        void PopulateList()
        {
            foreach (var go in _cardGos) Destroy(go);
            _cardGos.Clear();

            foreach (var lobby in _lobbies)
            {
                var card = LobbyCard.Create(_listContent, lobby, ResolveCaseIcons(lobby), OnJoinLobby,
                    !CanAffordLobby(lobby));
                _cardGos.Add(card.gameObject);
            }

            Debug.Log("[BATTLE_LOBBY_DIAG] rendered " + _cardGos.Count + " lobby cards (onlineMode=" + _onlineMode + ")");

            if (_activeCountLbl != null)
                _activeCountLbl.text = _lobbies.Count + " LIVE";
        }

        // Per-lobby case icon (online lobbies carry a real caseId); falls back to the
        // basic-case icon resolved for the bot fallback, then to null (card handles it).
        Sprite ResolveCaseIcon(BattleLobbyData lobby)
        {
            var content = GameContext.Instance?.Content;
            if (content != null && !string.IsNullOrEmpty(lobby?.CaseId))
            {
                var c = content.GetCase(lobby.CaseId);
                if (c != null && c.CaseIcon != null) return c.CaseIcon;
            }
            return _basicCaseIcon;
        }

        List<Sprite> ResolveCaseIcons(BattleLobbyData lobby)
        {
            var icons = new List<Sprite>(5);
            var content = GameContext.Instance?.Content;
            if (lobby?.CaseSelections != null && lobby.CaseSelections.Count > 0)
            {
                for (int i = 0; i < lobby.CaseSelections.Count && icons.Count < 5; i++)
                {
                    var id = lobby.CaseSelections[i].CaseId;
                    var icon = content != null && !string.IsNullOrEmpty(id)
                        ? content.GetCase(id)?.CaseIcon
                        : null;
                    icons.Add(icon != null ? icon : ResolveCaseIcon(lobby));
                }
            }

            if (icons.Count == 0)
                icons.Add(ResolveCaseIcon(lobby));

            return icons;
        }

        // ── Online lobby list (GET /api/v1/battles/lobbies, auto-refresh ~3s) ────────
        void StartLobbyRefresh()
        {
            StopLobbyRefresh();
            _botFallbackShown = false;
            _lobbyListHadSuccessfulFetch = false;
            _lobbyListFetchFailures = 0;
            _nextLobbyListErrorToastAt = 0f;
            _refreshCo = StartCoroutine(LobbyAutoRefresh());
        }

        void StopLobbyRefresh()
        {
            if (_refreshCo != null) { StopCoroutine(_refreshCo); _refreshCo = null; }
        }

        IEnumerator LobbyAutoRefresh()
        {
            // Stops automatically when the screen is hidden (OnHidden → StopAllCoroutines)
            // or while the battle room is up — public lobbies aren't refreshed mid-battle.
            while (isActiveAndEnabled && !BattleViewOpen)
            {
                var ctx = GameContext.Instance;
                if (ctx != null && ctx.BackendReady && ctx.Backend != null)
                {
                    _onlineMode       = true;
                    _botFallbackShown = false;

                    Debug.Log("[BATTLE_LOBBY_DIAG] GET /api/v1/battles/lobbies — online mode, requesting… baseUrl=" +
                              ctx.BackendBaseUrl + " backendReady=" + ctx.BackendReady +
                              " hasToken=" + ctx.HasGuestToken + " hasAccountId=" + ctx.HasGuestAccountId);

                    LobbyListResponse resp = null;
                    BackendError      err  = null;
                    yield return ctx.Backend.GetBattleLobbies(r => resp = r, e => err = e);

                    if (resp != null)
                    {
                        ClearLobbyListNetworkError();
                        ApplyOnlineLobbies(resp);
                    }
                    else if (err != null) HandleLobbyListNetworkError(err);
                }
                else if (!_botFallbackShown)
                {
                    // Backend not ready (editor/offline): show the legacy bot lobbies once.
                    _onlineMode       = false;
                    _botFallbackShown = true;
                    Debug.LogWarning("[BATTLE_LOBBY_DIAG] backend NOT ready — showing LOCAL bot fallback (not online). " +
                                     "ctxNull=" + (ctx == null) + " backendReady=" + (ctx != null && ctx.BackendReady) +
                                     " hasToken=" + (ctx != null && ctx.HasGuestToken));
                    _lobbies = BuildBotLobbies();
                    PopulateList();
                }

                yield return new WaitForSecondsRealtime(3f);
            }
        }

        void ClearLobbyListNetworkError()
        {
            _lobbyListHadSuccessfulFetch = true;
            _lobbyListFetchFailures = 0;
        }

        void HandleLobbyListNetworkError(BackendError err)
        {
            _lobbyListFetchFailures++;
            Debug.LogWarning("[BATTLE_LOBBY_DIAG] list fetch FAILED — " + err);

            if (Time.unscaledTime < _nextLobbyListErrorToastAt) return;
            if (!_lobbyListHadSuccessfulFetch || _lobbyListFetchFailures >= 3)
            {
                GameEvents.RaiseToast(_lobbyListHadSuccessfulFetch
                    ? "Lobi bilgileri güncellenemedi, tekrar deneniyor..."
                    : "Bağlantı yenileniyor...");
                _nextLobbyListErrorToastAt = Time.unscaledTime + LobbyListErrorToastCooldown;
            }
        }

        // Shows only public WAITING lobbies (all players', not just the current user's).
        // A lobby that has started/finished/cancelled — or that still says WAITING but
        // already carries result data — has spent its single PvP lifetime and is dropped
        // so it can never be reopened as an active card.
        void ApplyOnlineLobbies(LobbyListResponse resp)
        {
            var list = new List<BattleLobbyData>();
            int rawCount = resp.lobbies != null ? resp.lobbies.Length : 0;
            Debug.Log("[BATTLE_LOBBY_DIAG] backend returned " + rawCount + " lobbies (raw, pre-filter)");
            if (resp.lobbies != null)
            {
                foreach (var r in resp.lobbies)
                {
                    if (r == null) continue;

                    if (!string.Equals(r.status, "WAITING", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log("[BATTLE_LOBBY_DIAG] DROP id=" + r.battleId + " reason=status!=WAITING status=" + r.status);
                        continue;
                    }

                    if (HasResultData(r))
                    {
                        Debug.Log("[BATTLE_LOBBY_DIAG] DROP id=" + r.battleId + " reason=hasResultData");
                        continue;
                    }

                    var data = BattleLobbyMapper.ToLobbyData(r);
                    if (data == null)
                    {
                        Debug.Log("[BATTLE_LOBBY_DIAG] DROP id=" + r.battleId + " reason=mapNull");
                        continue;
                    }

                    Debug.Log("[BATTLE_LOBBY_DIAG] KEEP id=" + r.battleId + " status=" + r.status +
                              " case=" + r.caseId + " creator=" + (r.creator != null ? r.creator.accountId : "-"));
                    list.Add(data);
                }
            }
            Debug.Log("[BATTLE_LOBBY_DIAG] accepted " + list.Count + " of " + rawCount + " lobbies after filters");
            _lobbies = list;
            PopulateList();
        }

        // True when a lobby already carries battle-result fields, regardless of a stale
        // status string — winner, completed-PvP progression, or rolled per-slot rounds.
        static bool HasResultData(LobbyResponse r)
        {
            // JsonUtility never leaves nested serializable fields null, so progression is
            // checked for real content — a non-null empty instance is not a result.
            if (r.progression != null && (r.progression.level > 0 || r.progression.totalXp > 0)) return true;
            if (!string.IsNullOrEmpty(r.winnerDisplayName)) return true;
            if (r.slots != null)
                foreach (var s in r.slots)
                    if (s != null && s.rounds != null && s.rounds.Length > 0) return true;
            return false;
        }

        // ── Default bot lobbies (Basic Vandal Case, 1x, 1v1 / 1v1v1) ─────────────
        List<BattleLobbyData> BuildBotLobbies()
        {
            int    price = 500;
            string name  = "Basic Vandal Case";

            var basic = ResolveBasicCase();
            if (basic != null)
            {
                price          = basic.VpPrice;
                name           = basic.DisplayName;
                _basicCaseIcon = basic.CaseIcon;
                _basicCase     = basic;
            }

            return new List<BattleLobbyData>
            {
                MakeBotLobby("BV01", name, BattlePlayerCount.OneVOne,     rounds: 1, current: 1, price),
                MakeBotLobby("BV02", name, BattlePlayerCount.ThreePlayer, rounds: 1, current: 2, price),
                MakeBotLobby("BV03", name, BattlePlayerCount.OneVOne,     rounds: 3, current: 1, price),
                MakeBotLobby("BV04", name, BattlePlayerCount.ThreePlayer, rounds: 3, current: 1, price),
            };
        }

        static BattleLobbyData MakeBotLobby(string id, string caseName, BattlePlayerCount pc,
            int rounds, int current, int price)
        {
            return new BattleLobbyData
            {
                LobbyId        = id,
                HostName       = "BOT",
                CaseName       = caseName,
                Rounds         = rounds,
                Mode           = BattleMode.Normal,
                PlayerCount    = pc,
                CurrentPlayers = current,
                Status         = LobbyStatus.Waiting,
                Rarity         = SkinRarity.Select,
                WagerVP        = price * rounds,   // entry cost — prize pot = WagerVP × players
            };
        }

        CaseDefinitionSO ResolveBasicCase()
        {
            var cases = GameContext.Instance?.Content?.Cases;
            if (cases == null || cases.Count == 0) return null;

            foreach (var c in cases)
                if (c != null && c.CaseId == "vandal_basic") return c;
            foreach (var c in cases)
                if (c != null && !string.IsNullOrEmpty(c.DisplayName) &&
                    c.DisplayName.IndexOf("Basic", System.StringComparison.OrdinalIgnoreCase) >= 0) return c;
            return cases[0];
        }

        // ── Balance ──────────────────────────────────────────────────────────────
        void RefreshBalance()
        {
            if (_balanceLbl == null) return;
            var vp = GameContext.Instance?.Vp;
            _balanceLbl.text = vp != null ? vp.Balance.ToString("N0") : "—";
        }

        // ── Stats ─────────────────────────────────────────────────────────────
        void RefreshStats()
        {
            var data = GameContext.Instance?.Statistics?.Data;
            if (data == null) return;
            if (_statBattles != null) _statBattles.text = data.battleTotal.ToString();
            if (_statWinRate != null) _statWinRate.text  = data.battleWinRate.ToString("F0") + "%";
            if (_statEarned  != null) _statEarned.text   = FormatK(data.battleEarnings);
            if (_statLost    != null) _statLost.text      = data.battleLosses.ToString();
            if (_statStreak  != null) _statStreak.text    = data.battleStreak.ToString();
        }

        static string FormatK(int n) => n >= 1000 ? (n / 1000f).ToString("F1") + "K" : n.ToString();

        // ── Battle simulation ─────────────────────────────────────────────────
        // Runs a silent round-by-round battle to determine win/loss, then records
        // the result. No VP is spent and no skins are distributed here — the
        // existing CaseOpening screen handles the player's actual reward.
        void RunBattleSimulation(BattleLobbyData lobby)
        {
            var ctx = GameContext.Instance;
            if (ctx?.Statistics == null || ctx.CaseOpening == null || _basicCase == null) return;

            var playerName = ctx.Save?.Data?.playerName ?? "Player";
            var player     = new BattleParticipant(playerName, isBot: false);
            var opponent   = new BattleParticipant(BattleConstants.BotName, isBot: true);
            var session    = new CaseBattleSession(_basicCase, lobby.Rounds, player, opponent, ctx.CaseOpening);

            while (session.HasMoreRounds)
                session.RollNextRound(out _, out _);

            var earnings = session.Outcome == BattleOutcome.PlayerWins ? session.Opponent.TotalVp : 0;
            ctx.Statistics.RecordBattleResult(session.Outcome, earnings);
            ctx.Save?.Save();
        }

        // ── Flow ───────────────────────────────────────────────────────────────
        void OnCreatePressed()
        {
            StartCoroutine(UIAnimator.ScalePress(_createBg.transform, 0.97f, 0.12f));
            _createPanel.Show();
        }

        void CloseCreate() => _createPanel.Hide();

        void OnJoinLobby(BattleLobbyData lobby)
        {
            // VP affordability is the only join gate; case-level locks never apply here.
            if (!CanAffordEntry(lobby)) return;

            if (_onlineMode)
            {
                Debug.Log("[ONLINE_BATTLE] open lobby as viewer battleId=" + lobby?.LobbyId);
                OpenOnlineRoom(lobby.LobbyId, lobby);
                return;
            }

            // ── legacy local/bot fallback ──
            SetLobbyChromeActive(false);
            _waitingPanel.Show(lobby, isHost: false);
        }

        void OpenWaitingFromCreate(BattleLobbyData lobby)
        {
            if (_onlineMode)
            {
                if (_creating) return;
                // POST /battles/lobbies, then open the waiting room on the returned id.
                StartCoroutine(CreateOnlineRoutine(lobby));
                return;
            }

            // ── legacy local fallback ──
            if (!CanAffordEntry(lobby))
            {
                _createPanel.SetConfirmInFlight(false);
                return;
            }
            _createPanel.Hide();
            SetLobbyChromeActive(false);
            _waitingPanel.Show(lobby, isHost: true);
        }

        // ── Online create / join ────────────────────────────────────────────────
        IEnumerator CreateOnlineRoutine(BattleLobbyData lobby)
        {
            if (_creating) yield break;
            _creating = true;
            _createPanel.SetConfirmInFlight(true);

            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Backend == null)
            {
                _creating = false;
                _createPanel.SetConfirmInFlight(false);
                GameEvents.RaiseToast("Sunucu kullanılamıyor.");
                yield break;
            }

            var selections = new List<CaseSelectionRequest>();
            if (lobby.CaseSelections != null)
                foreach (var s in lobby.CaseSelections)
                    selections.Add(new CaseSelectionRequest { caseId = s.CaseId, quantity = Mathf.Clamp(s.Quantity, 1, 5) });
            if (selections.Count == 0 && !string.IsNullOrEmpty(lobby.CaseId))
                selections.Add(new CaseSelectionRequest { caseId = lobby.CaseId, quantity = Mathf.Max(1, lobby.Rounds) });

            Debug.Log("[BATTLE_LOBBY_DIAG] CREATE POST /api/v1/battles/lobbies selections=" + selections.Count +
                      " maxSlots=" + lobby.MaxPlayers + " hasToken=" + ctx.HasGuestToken);
            foreach (var s in selections)
                Debug.Log("[BATTLE_LOBBY_DIAG] create selection caseId=" + s.caseId + " qty=" + s.quantity +
                          " locallyUnlocked=" + PlayerProgression.IsCaseUnlocked(s.caseId) +
                          " requiredLevel=" + PlayerProgression.RequiredLevelForCaseId(s.caseId));

            LobbyResponse resp = null;
            BackendError  err  = null;
            yield return ctx.Backend.CreateBattleLobby(selections, lobby.MaxPlayers,
                r => resp = r, e => err = e);

            if (err != null || resp == null)
            {
                Debug.LogWarning("[BATTLE_LOBBY_DIAG] CREATE FAILED — status=" + (err != null ? err.HttpStatus : -1) +
                                 " lockedCategory=" + (err != null ? err.LockedCategory : null) +
                                 " requiredLevel=" + (err != null ? err.RequiredLevel : 0) +
                                 " msg=" + (err != null ? err.Message : "null response"));
                GameEvents.RaiseToast(WaitingRoomScreen.MapLobbyError(err));
                _creating = false;
                _createPanel.SetConfirmInFlight(false);
                yield break;
            }

            Debug.Log("[BATTLE_LOBBY_DIAG] CREATE ok battleId=" + resp.battleId + " status=" + resp.status);
            ctx.RequestBackendResync();   // entry cost is charged server-side on create
            _creating = false;
            _createPanel.Hide();
            OpenOnlineRoom(resp.battleId, BattleLobbyMapper.ToLobbyData(resp));
        }

        void OpenOnlineRoom(string battleId, BattleLobbyData summary)
        {
            StopLobbyRefresh();
            SetLobbyChromeActive(false);
            _waitingPanel.ShowOnline(battleId, summary);
        }

        // Entry cost = Case Price × Round Count, stored as WagerVP on the lobby.
        // Shows a message and returns false when the balance is insufficient.
        bool CanAffordEntry(BattleLobbyData lobby)
        {
            if (CanAffordLobby(lobby)) return true;
            GameEvents.RaiseToast("Yetersiz VP");
            return false;
        }

        bool CanAffordLobby(BattleLobbyData lobby)
        {
            var vp = GameContext.Instance?.Vp;
            return vp == null || vp.Balance >= (lobby?.WagerVP ?? 0);
        }

        void CloseWaiting()
        {
            _waitingPanel.Hide();
            SetLobbyChromeActive(true);

            // Drop any cached lobby (including the just-completed one) so it cannot linger
            // on screen, then force an immediate fresh fetch of the live public list.
            _lobbies = new List<BattleLobbyData>();
            PopulateList();

            // The battle may have changed the balance (entry cost) and stats; keep
            // the lobby chrome in sync on return.
            RefreshBalance();
            RefreshStats();
            StartLobbyRefresh();   // resume the live public list
        }

        // Toggles the lobby's own UI so the Battle Room shows full-screen with no
        // lobby content visible behind it. The create/waiting overlays are left alone.
        void SetLobbyChromeActive(bool active)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                string n = child.name;
                if (n == "WaitingPanel" || n == "CreatePanel") continue;
                child.gameObject.SetActive(active);
            }
        }

        void OnBattleStart(BattleLobbyData lobby)
        {
            RunBattleSimulation(lobby);
            _waitingPanel.Hide();
            navigator?.Navigate(ScreenType.CaseOpening);
        }
    }
}
