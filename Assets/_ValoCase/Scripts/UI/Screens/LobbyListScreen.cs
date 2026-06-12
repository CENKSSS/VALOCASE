using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Battle;
using ValoCase.Core;
using ValoCase.Data;
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
        const float StatsH      = 78f;
        const float CreateH     = 52f;
        const float SectionH    = 44f;
        const float NavReserve  = 98f;   // clear the persistent 90dp bottom nav

        bool _built;

        TextMeshProUGUI _balanceLbl;
        TextMeshProUGUI _activeCountLbl;
        AngledCutImage  _createBg;
        RectTransform   _listContent;

        CreateBattleScreen _createPanel;
        WaitingRoomScreen  _waitingPanel;

        readonly List<GameObject> _cardGos = new();
        List<BattleLobbyData>     _lobbies = new();

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

        protected override void OnShown()
        {
            BuildOnce();

            if (BattleViewOpen)
            {
                // Returning from Settings (or any other screen) mid-battle:
                // restore the exact battle view, state and panels untouched.
                Debug.Log("[BATTLE_NAV] Returned to active battle room — state preserved");
                return;
            }

            _createPanel?.Hide();
            _waitingPanel?.Hide();
            SetLobbyChromeActive(true);   // restore lobby if returning from a battle
            RefreshBalance();
            RefreshStats();
            StartCoroutine(UIAnimator.SlideFromBottom((RectTransform)transform, 0.25f));
        }

        protected override void OnHidden()
        {
            StopAllCoroutines();

            // Battle room stays alive while Settings (or another screen) is open;
            // its coroutines keep running because the GameObject is not deactivated.
            if (BattleViewOpen) return;

            _createPanel?.Hide();
            _waitingPanel?.Hide();
        }

        void OnDisable() => StopAllCoroutines();

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
            BuildCreateButton(rt);
            BuildSectionHeader(rt);
            BuildScrollList(rt);
            BuildSubPanels(rt);

            _lobbies = BuildBotLobbies();
            PopulateList();
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
                new Vector2(0f, -11f), new Vector2(15f, 15f));
            switch (iconType)
            {
                case StatIcon.Trophy:    BuildTrophyIcon(iconRoot.transform, iconColor);    break;
                case StatIcon.Crosshair: BuildCrosshairIcon(iconRoot.transform, iconColor); break;
                case StatIcon.Coin:      BuildCoinIcon(iconRoot.transform, iconColor);      break;
                case StatIcon.Defeat:    BuildDefeatIcon(iconRoot.transform, iconColor);    break;
                case StatIcon.Star:      BuildStarIcon(iconRoot.transform, iconColor);      break;
            }

            var val = MakeTmp(card.transform, "Value", value, 15f, FontStyles.Bold, valueColor);
            val.alignment = TextAlignmentOptions.Center;
            SetRect(val.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -30f), new Vector2(-4f, 20f));

            var lbl = MakeTmp(card.transform, "Label", label, 8f, FontStyles.Bold, ColorPalette.TextDim);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.characterSpacing = 1f;
            SetRect(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 9f), new Vector2(-2f, 12f));

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
            createGlow.effectDistance = new Vector2(0f, -4f);            var cRt = _createBg.rectTransform;
            TopStrip(cRt, CreateH, -(TopInset + HeaderH + Gap + StatsH + Gap));
            cRt.offsetMin = new Vector2(SidePad, cRt.offsetMin.y);
            cRt.offsetMax = new Vector2(-SidePad, cRt.offsetMax.y);

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
            TopStrip(sRt, SectionH, -(TopInset + HeaderH + Gap + StatsH + Gap + CreateH + Gap));

            // Red accent tick.
            var tick = MakeImage("Tick", sec.transform, ColorPalette.ActiveRed);
            SetRect(tick.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad, -4f), new Vector2(3f, 16f));

            var title = MakeTmp(sec.transform, "Title", "ACTIVE BATTLES", 13f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 2f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad + 10f, -2f), new Vector2(-150f, 18f));

            _activeCountLbl = MakeTmp(sec.transform, "Count", "", 11f, FontStyles.Bold, ColorPalette.ActiveRed);
            _activeCountLbl.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(_activeCountLbl.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-SidePad, -2f), new Vector2(80f, 18f));

            var sub = MakeTmp(sec.transform, "Sub", "Default bot lobbies available", 10f, FontStyles.Normal, ColorPalette.TextDim);
            sub.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad + 10f, -24f), new Vector2(-30f, 14f));
        }

        void BuildScrollList(RectTransform rt)
        {
            var scrollGo = NewGo("Scroll", rt, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0f, NavReserve);
            scrollRt.offsetMax = new Vector2(0f, -(TopInset + HeaderH + Gap + StatsH + Gap + CreateH + Gap + SectionH));

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport);

            var content = NewGo("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _listContent = (RectTransform)content.transform;
            _listContent.anchorMin        = new Vector2(0f, 1f);
            _listContent.anchorMax        = new Vector2(1f, 1f);
            _listContent.pivot            = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding               = new RectOffset(14, 14, 8, 20);
            vlg.spacing               = 16f;
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
                var card = LobbyCard.Create(_listContent, lobby, _basicCaseIcon, OnJoinLobby);
                _cardGos.Add(card.gameObject);
            }

            if (_activeCountLbl != null)
                _activeCountLbl.text = _lobbies.Count + " LIVE";
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
            // Block entry unless the player can afford the battle cost.
            if (!CanAffordEntry(lobby)) return;

            // Joining a default bot lobby → full-screen Battle Room.
            SetLobbyChromeActive(false);
            _waitingPanel.Show(lobby, isHost: false);
        }

        void OpenWaitingFromCreate(BattleLobbyData lobby)
        {
            // Block entry unless the player can afford the battle cost; the create
            // panel stays open so the player can adjust or cancel.
            if (!CanAffordEntry(lobby)) return;

            _createPanel.Hide();
            SetLobbyChromeActive(false);
            _waitingPanel.Show(lobby, isHost: true);
        }

        // Entry cost = Case Price × Round Count, stored as WagerVP on the lobby.
        // Shows a message and returns false when the balance is insufficient.
        bool CanAffordEntry(BattleLobbyData lobby)
        {
            int cost = lobby?.WagerVP ?? 0;
            var vp   = GameContext.Instance?.Vp;

            if (vp != null && vp.Balance < cost)
            {
                GameEvents.RaiseToast("Not enough VP to join this battle");
                return false;
            }

            return true;
        }

        void CloseWaiting()
        {
            _waitingPanel.Hide();
            SetLobbyChromeActive(true);

            // The battle may have changed the balance (entry cost) and stats; keep
            // the lobby chrome in sync on return.
            RefreshBalance();
            RefreshStats();
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
