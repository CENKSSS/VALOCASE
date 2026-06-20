using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Battle;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Profile;
using ValoCase.Services.Backend;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    public sealed class WaitingRoomScreen : MonoBehaviour
    {
        public event Action OnLeave;
        public event Action<BattleLobbyData> OnStartBattle;

        /// <summary>True while the battle room is up (ready, running or finished).
        /// Only an explicit Leave (OnLeave → CloseWaiting) closes it; navigating
        /// to Settings and back must not.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>True only when leaving the room right now is safe — i.e. the room is up
        /// and the Leave affordance is visible (a viewer, or a not-yet-started local lobby).
        /// A seated player in a starting/running/finished battle returns false, so a Battle
        /// re-tap never interrupts an ongoing match.</summary>
        public bool CanLeaveSafely => IsOpen && _backVisible && _state == RoomState.Ready;

        /// <summary>Public entry for an external reset (bottom-nav Battle re-tap) to leave
        /// the room back to the lobby list. Only call when <see cref="CanLeaveSafely"/>.</summary>
        public void RequestLeaveExternally() => RequestLeave();

        const float SidePad    = 16f;
        const float HeaderH    = 60f;
        const float CtaH       = 54f;
        const float NavReserve = 150f;
        const float SummaryH   = 270f;

        enum RoomState { Ready, Running, Finished }

        bool            _built;
        BattleLobbyData _lobby;
        bool            _isHost;

        Transform       _dynamicRoot;
        AngledCutImage  _startBg;
        TextMeshProUGUI _startLbl;

        RoomState                    _state = RoomState.Ready;
        BattleResult                 _result;
        readonly List<BattleRouletteView> _panels = new List<BattleRouletteView>();
        RectTransform                _battleArea;
        TextMeshProUGUI              _statusLbl;
        GameObject                   _resultOverlay;
        bool                         _panelsStaged;

        // Optimistic warmup state (backend async path only). _warmupRunning gates the
        // in-flight warmup coroutine; _warmupShown records that the reels were already
        // revealed + spinning so RunBattle skips its own waiting→reel transition.
        bool                         _warmupRunning;
        bool                         _warmupShown;

        // True once this battle's result has been settled (rewards/stats), so leaving
        // mid-animation cannot leave VP spent without settlement, and cannot double-settle.
        bool                         _resultSettled;

        // Battle authority seam (economy / result generation / rewards / stats / save).
        // The screen no longer spends VP or grants skins directly — it calls this.
        IBattleService               _battleService;

        // ── Online public-lobby mode ──────────────────────────────────────────────
        // When _online is true the room is driven entirely by backend lobby state: it
        // polls GET /battles/lobbies/{id} every second, renders REAL/BOT/EMPTY slots,
        // offers Add Bot, and only when the lobby reports COMPLETED does it hand the
        // mapped result to the unchanged reel/result animation below. There is NO local
        // start, RNG, winner, or reward in this mode — the backend owns all of it.
        bool            _online;
        string          _battleId;
        bool            _completedHandled;
        GameObject      _slotsRoot;
        LobbyResponse   _lastLobby;
        Coroutine       _pollCo;
        string          _lastStatus;
        GameObject      _leaveGo;
        Button          _leaveBtn;
        bool            _backVisible = true;
        GameObject      _confettiRoot;
        bool            _amHost;
        bool            _amSeated;
        bool            _joining;

        // Battle entry cost = Case Price × Round Count, already baked into WagerVP
        // when the lobby is created (see LobbyListScreen.MakeBotLobby).
        int EntryCost => _lobby?.WagerVP ?? 0;

        // Local/offline fallback entry (legacy bot lobby flow — unchanged behavior).
        public void Show(BattleLobbyData lobby, bool isHost)
        {
            _online   = false;
            _battleId = null;
            ShowCore(lobby, isHost);
        }

        // Online public-lobby entry: drive the room from backend lobby state.
        // <paramref name="summary"/> only feeds the header (case / mode / rounds / cost).
        public void ShowOnline(string battleId, BattleLobbyData summary)
        {
            _online           = true;
            _battleId         = battleId;
            _completedHandled = false;
            _joining          = false;

            var myId = GameContext.Instance?.Save?.Data?.guestAccountId;
            _amHost = summary != null && !string.IsNullOrEmpty(summary.CreatorAccountId) &&
                      !string.IsNullOrEmpty(myId) &&
                      string.Equals(summary.CreatorAccountId, myId, StringComparison.OrdinalIgnoreCase);
            _amSeated = _amHost;
            ShowCore(summary, isHost: _amHost);
        }

        void ShowCore(BattleLobbyData lobby, bool isHost)
        {
            _lobby  = lobby;
            _isHost = isHost;
            _state  = RoomState.Ready;
            _result = null;
            _panels.Clear();
            _panelsStaged = false;
            _warmupRunning = false;
            _warmupShown   = false;
            _resultSettled = false;
            _lastLobby     = null;
            _lastStatus    = null;
            _slotsRoot     = null;
            if (_online) _completedHandled = false;
            DismissResultPopup();

            EnsureBattleService();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            BuildOnce();
            SetBackButton(!_online || !_amSeated);

            if (_online)
            {
                Debug.Log("[ONLINE_BATTLE] enter waiting room battleId=" + _battleId);
                BuildOnlineRoom();
            }
            else
            {
                RebuildDynamic();
                UpdateCta();
            }

            var rt = (RectTransform)transform;
            rt.anchoredPosition = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            StartCoroutine(UIAnimator.SlideFromBottom(rt, 0.24f));
        }

        public void NotifyReturnedToScreen()
        {
            if (!IsOpen) return;
            var rt = (RectTransform)transform;
            rt.anchoredPosition = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            Debug.Log("[ONLINE_BATTLE] waiting room layout rebuilt on navbar return");
        }

        void SetBackButton(bool visible)
        {
            if (_leaveGo  != null) _leaveGo.SetActive(visible);
            if (_leaveBtn != null) _leaveBtn.interactable = visible;
            if (visible != _backVisible)
            {
                _backVisible = visible;
                Debug.Log("[ONLINE_BATTLE] leave button visible=" + visible);
            }
        }

        public void Hide()
        {
            if (_online) Debug.Log("[ONLINE_BATTLE] leave waiting room battleId=" + _battleId);
            ForceSettleIfNeeded();
            StopPolling();
            StopAllCoroutines();
            DismissResultPopup();
            DestroyConfetti();
            _online        = false;
            _battleId      = null;
            _lastLobby     = null;
            _lastStatus    = null;
            _slotsRoot     = null;
            gameObject.SetActive(false);
        }

        // Safety net for leaving/disabling mid-battle: if a battle is running and its
        // result is already known but not yet settled, settle it ONCE before the running
        // coroutine is killed by StopAllCoroutines — otherwise local mode would have
        // spent the entry cost with no reward/stat settlement. Idempotent via
        // _resultSettled and the service's own distributed/recorded guards. If the
        // result is not known yet (e.g. a backend battle still awaiting the server), we
        // do NOT settle — no rewards are invented.
        void ForceSettleIfNeeded()
        {
            if (_resultSettled) return;
            if (_state != RoomState.Running) return;
            if (_result == null) return;
            _battleService?.Settle(_result);
            _resultSettled = true;
        }

        void DismissResultPopup()
        {
            if (_resultOverlay != null) Destroy(_resultOverlay);
            _resultOverlay = null;
        }

        void OnDisable()
        {
            ForceSettleIfNeeded();
            StopAllCoroutines();
        }

        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            var bg = MakeImage("Bg", rt, ColorPalette.BgDeep, raycast: true);
            Stretch(bg.rectTransform);

            _dynamicRoot = NewGo("DynamicRoot", rt).transform;
            Stretch((RectTransform)_dynamicRoot);

            BuildHeader(rt);
            BuildCta(rt);
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.CardBg, raycast: true);
            TopStrip(hdr.rectTransform, HeaderH);

            var accent = MakeImage("TopAccent", hdr.transform, ColorPalette.ActiveRed);
            accent.raycastTarget = false;
            TopStrip(accent.rectTransform, 2f);

            var divider = MakeImage("BottomBorder", hdr.transform, ColorPalette.Border);
            divider.raycastTarget = false;
            BottomStrip(divider.rectTransform, 1f);

            var leaveGo = NewGo("LeaveBtn", hdr.transform, typeof(Image), typeof(Button));
            leaveGo.GetComponent<Image>().color = ColorPalette.Surface;
            var leaveBorder = leaveGo.AddComponent<Outline>();
            leaveBorder.effectColor = ColorPalette.Border; leaveBorder.effectDistance = new Vector2(1f, -1f);
            SetRect((RectTransform)leaveGo.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(SidePad, 0f), new Vector2(74f, 36f));

            var leaveLbl = MakeTmp(leaveGo.transform, "Lbl", "LEAVE", 13f, FontStyles.Bold, ColorPalette.TextBright);
            leaveLbl.alignment = TextAlignmentOptions.Center;
            leaveLbl.characterSpacing = 1f;
            Stretch(leaveLbl.rectTransform);

            var leaveBtn = leaveGo.GetComponent<Button>();
            leaveBtn.transition = Selectable.Transition.None;
            leaveBtn.onClick.AddListener(RequestLeave);
            _leaveGo  = leaveGo;
            _leaveBtn = leaveBtn;

            var title = MakeTmp(hdr.transform, "Title", "CASE BATTLE", 18f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -4f), new Vector2(-160f, 0f));
        }

        void BuildCta(RectTransform rt)
        {
            _startBg = MakeAngled("StartBtn", rt, ColorPalette.ActiveRed, 10f, raycast: true);
            var cRt = _startBg.rectTransform;
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(1f, 0f);
            cRt.pivot     = new Vector2(0.5f, 0f);
            cRt.offsetMin = new Vector2(SidePad, NavReserve);
            cRt.offsetMax = new Vector2(-SidePad, NavReserve + CtaH);

            var btn = _startBg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnCtaPressed);

            _startLbl = MakeTmp(_startBg.transform, "Lbl", "START BATTLE", 17f, FontStyles.Bold, Color.white);
            _startLbl.characterSpacing = 3f;
            _startLbl.alignment = TextAlignmentOptions.Center;
            Stretch(_startLbl.rectTransform);
        }

        void RebuildDynamic()
        {
            if (_dynamicRoot == null) return;

            for (int i = _dynamicRoot.childCount - 1; i >= 0; i--)
                Destroy(_dynamicRoot.GetChild(i).gameObject);

            var rt = (RectTransform)_dynamicRoot;

            BuildLobbySummary(rt);
            BuildBattleArea(rt);

            // The battle panels exist before the match begins: each one shows a
            // pre-battle waiting presentation (avatar / name / status) and reveals
            // its roulette reel only when START BATTLE is pressed.
            _panels.Clear();
            _panelsStaged = false;
            _warmupShown  = false;
            StartCoroutine(StageBattlePanels());
        }

        void BuildLobbySummary(RectTransform rt)
        {
            const float topOffset = HeaderH + 16f;
            const float height    = SummaryH;

            var card = MakeImage("SummaryCard", rt, ColorPalette.CardBg);
            TopStrip(card.rectTransform, height, -topOffset);
            card.rectTransform.offsetMin = new Vector2(10f, card.rectTransform.offsetMin.y);
            card.rectTransform.offsetMax = new Vector2(-10f, card.rectTransform.offsetMax.y);

            var cardBorder = card.gameObject.AddComponent<Outline>();
            cardBorder.effectColor    = ColorPalette.Border;
            cardBorder.effectDistance = new Vector2(1f, -1f);

            var bar = MakeImage("AccentBar", card.transform, ColorPalette.ActiveRed);
            var bRt = bar.rectTransform;
            bRt.anchorMin = new Vector2(0f, 0f);
            bRt.anchorMax = new Vector2(0f, 1f);
            bRt.pivot     = new Vector2(0f, 0.5f);
            bRt.sizeDelta = new Vector2(5f, 0f);

            const float il = 25f;

            var caseIcon = ResolveCaseIcon();
            if (caseIcon != null)
            {
                var thumb = MakeImage("CaseIcon", card.transform, Color.white);
                thumb.sprite         = caseIcon;
                thumb.preserveAspect = true;
                thumb.raycastTarget  = false;
                SetRect(thumb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 10f), new Vector2(200f, 200f));
            }

            var nameLbl = MakeTmp(card.transform, "CaseName",
                CasesSummary(),
                27f, FontStyles.Bold, ColorPalette.TextBright);
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            nameLbl.enableWordWrapping = false;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(il, -24f), new Vector2(-14f, 40f));

            string modeStr = (_lobby?.MaxPlayers ?? 2) == 4 ? "1V1V1V1" :
                             (_lobby?.MaxPlayers ?? 2) == 3 ? "1V1V1" : "1V1";
            float modeW = modeStr == "1V1V1V1" ? 130f : modeStr == "1V1V1" ? 94f : 72f;

            var modeBadge = MakeAngled("ModeBadge", card.transform, ColorPalette.Surface, 5f);
            SetRect(modeBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(il, -76f), new Vector2(modeW, 32f));

            var mb = modeBadge.gameObject.AddComponent<Outline>();
            mb.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.55f);
            mb.effectDistance = new Vector2(1f, -1f);

            var modeLbl = MakeTmp(modeBadge.transform, "Lbl", modeStr, 16f, FontStyles.Bold, ColorPalette.ActiveRed);
            modeLbl.alignment = TextAlignmentOptions.Center;
            modeLbl.characterSpacing = 1f;
            Stretch(modeLbl.rectTransform);

            int rounds = _lobby?.Rounds ?? 1;
            string roundStr = rounds + (rounds == 1 ? " ROUND" : " ROUNDS");

            var roundBadge = MakeImage("RoundBadge", card.transform, ColorPalette.Surface);
            SetRect(roundBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(il + modeW + 11f, -76f), new Vector2(130f, 32f));

            var rb = roundBadge.gameObject.AddComponent<Outline>();
            rb.effectColor    = ColorPalette.Border;
            rb.effectDistance = new Vector2(1f, -1f);

            var roundLbl = MakeTmp(roundBadge.transform, "Lbl", roundStr, 16f, FontStyles.Bold, ColorPalette.TextDim);
            roundLbl.alignment = TextAlignmentOptions.Center;
            roundLbl.characterSpacing = 1f;
            Stretch(roundLbl.rectTransform);

            var roundCircle = MakeAngled("RoundCircleBadge", card.transform, ColorPalette.ActiveRed, 14f);
            SetRect(roundCircle.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-25f, -25f), new Vector2(54f, 54f));

            var roundNumLbl = MakeTmp(roundCircle.transform, "RoundNum",
                rounds.ToString(), 25f, FontStyles.Bold, Color.white);
            roundNumLbl.alignment = TextAlignmentOptions.Center;
            Stretch(roundNumLbl.rectTransform);

            // Show the exact amount that will be deducted at START (Case Price ×
            // Rounds == EntryCost), so the summary stays consistent with the charge.
            string costStr = EntryCost.ToString("N0") + " VP";

            var totalCostLbl = MakeTmp(card.transform, "TotalCost",
                $"Total cost: <color=#{Hex(ColorPalette.GoldAccent)}><b>{costStr}</b></color>",
                18f, FontStyles.Normal, ColorPalette.TextDim);
            totalCostLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(totalCostLbl.rectTransform,
                new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(il, 18f), new Vector2(-4f, 32f));

            var openingLbl = MakeTmp(card.transform, "OpeningCases",
                "Opening cases", 18f, FontStyles.Bold, ColorPalette.TextBright);
            openingLbl.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(openingLbl.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 18f), new Vector2(-25f, 32f));
        }

        // Single case → its name; multiple → "First +N more" so the big title stays legible.
        string CasesSummary()
        {
            var sels = _lobby?.CaseSelections;
            if (sels == null || sels.Count == 0)
                return _lobby?.CaseName ?? "Basic Vandal Case";
            if (sels.Count == 1)
                return sels[0].CaseName + "  x" + sels[0].Quantity;
            return sels[0].CaseName + "  +" + (sels.Count - 1) + " more";
        }

        Sprite ResolveCaseIcon()
        {
            var content = GameContext.Instance?.Content;
            if (content == null || string.IsNullOrEmpty(_lobby?.CaseId)) return null;
            var c = content.GetCase(_lobby.CaseId);
            return c != null ? c.CaseIcon : null;
        }

        void OnCtaPressed()
        {
            switch (_state)
            {
                case RoomState.Ready:
                    if (_startBg != null)
                        StartCoroutine(UIAnimator.ScalePress(_startBg.transform, 0.97f, 0.12f));
                    StartBattle();
                    break;
                case RoomState.Running:
                    break;
                case RoomState.Finished:
                    OnLeave?.Invoke();
                    break;
            }
        }

        void UpdateCta()
        {
            if (_startBg == null || _startLbl == null) return;

            switch (_state)
            {
                case RoomState.Ready:
                    _startBg.color  = ColorPalette.ActiveRed;
                    _startLbl.text  = "START BATTLE";
                    _startLbl.color = Color.white;
                    break;
                case RoomState.Running:
                    _startBg.color  = ColorPalette.WithAlpha(ColorPalette.RedDim, 0.7f);
                    _startLbl.text  = "BATTLE RUNNING…";
                    _startLbl.color = ColorPalette.WithAlpha(Color.white, 0.7f);
                    break;
                case RoomState.Finished:
                    _startBg.color  = ColorPalette.Surface;
                    _startLbl.text  = "BACK TO LOBBY";
                    _startLbl.color = ColorPalette.TextBright;
                    break;
            }
        }

        void EnsureBattleService()
        {
            if (_battleService == null)
                _battleService = BattleServiceFactory.Create(GameContext.Instance);
        }

        void StartBattle()
        {
            EnsureBattleService();
            if (_battleService == null)
            {
                // No services available — mirror the old missing-wallet path (stay Ready).
                return;
            }

            // Backend (async) is the default when enabled: start the reel warmup
            // INSTANTLY and call the server in parallel. Flip to Running immediately so
            // the CTA can't be re-pressed during the network call; revert to Ready on
            // failure. The reels only LAND on the authoritative server rolls/winner.
            if (_battleService.IsAsync)
            {
                if (_state != RoomState.Ready) return;

                // Offline pre-check: stay Ready, restore CTA, no warmup, no spend.
                if (BackendErrorMapper.IsOffline)
                {
                    GameEvents.RaiseToast(BackendErrorMapper.Offline);
                    return;
                }

                _state = RoomState.Running;
                UpdateCta();
                StartCoroutine(StartBattleRoutine());
                return;
            }

            // ── Local fallback (unchanged sync path) ──
            var start = _battleService.TryStartBattle(_lobby);
            ApplyStartResult(start);
        }

        // Drives the async backend start: kick off the warmup immediately, run the
        // request in parallel, then stop the warmup and either run the determined
        // animation (success) or revert to a usable Ready room (failure).
        IEnumerator StartBattleRoutine()
        {
            StartCoroutine(BattleWarmupRoutine());

            BattleStartResult start = null;
            yield return _battleService.BeginBattle(_lobby, r => start = r);

            StopBattleWarmup();
            ApplyStartResult(start);
        }

        // Instant feedback: transition the staged panels into spinning reels and free-
        // scroll filler while the server decides. No winner, rewards, VP, or inventory
        // are touched here — this is purely the loading/reel animation.
        IEnumerator BattleWarmupRoutine()
        {
            _warmupRunning = true;

            while (!_panelsStaged) yield return null;
            if (!_warmupRunning) yield break;   // resolved/failed before staging finished

            var pool  = ResolveWarmupPool();
            int count = _panels.Count;

            SetRoomStatus("OPENING CASES…", ColorPalette.ActiveRed);
            for (int i = 0; i < count; i++)
            {
                if (pool != null) _panels[i].SetReelPool(pool);
                StartCoroutine(_panels[i].HideWaiting(0.25f));
                _panels[i].SetStatus("PLAYING", ColorPalette.ActiveRed);
            }
            _warmupShown = true;   // reels are now revealed — RunBattle won't redo this

            yield return new WaitForSecondsRealtime(0.28f);
            if (!_warmupRunning) yield break;

            for (int i = 0; i < count; i++)
                _panels[i].BeginWarmupSpin(pool);
        }

        void StopBattleWarmup()
        {
            _warmupRunning = false;
            foreach (var p in _panels)
                p?.StopWarmupSpin();
        }

        // Resolves the case's drop-table skins for warmup filler — the same pool the
        // backend result will carry (BuildReelPool), available locally at tap time.
        IReadOnlyList<SkinDefinitionSO> ResolveWarmupPool()
        {
            var ctx = GameContext.Instance;
            if (ctx?.Content == null) return null;

            var caseDef = new BattleOpeningEngine(ctx.Content, ctx.CaseOpening).ResolveCase(_lobby);
            if (caseDef?.DropTable?.PossibleDrops == null) return null;

            var pool = new List<SkinDefinitionSO>();
            foreach (var drop in caseDef.DropTable.PossibleDrops)
                if (drop?.skin != null) pool.Add(drop.skin);
            return pool.Count > 0 ? pool : null;
        }

        // Returns the reels to the pre-battle waiting presentation after a failed start
        // that had already begun the optimistic warmup.
        void RevertPanelsToWaiting()
        {
            foreach (var p in _panels)
                p?.ShowWaiting();
            _warmupShown = false;
            SetRoomStatus("WAITING TO START", ColorPalette.TextDim);
        }

        // Shared handling of a start result for both paths. The screen reproduces the
        // same observable outcomes; on backend failure it shows a toast and returns to
        // the Ready state without starting any animation.
        void ApplyStartResult(BattleStartResult start)
        {
            if (start == null || start.Status == BattleStartStatus.BackendError)
            {
                GameEvents.RaiseToast(!string.IsNullOrEmpty(start?.Message)
                    ? start.Message
                    : BackendErrorMapper.Generic);
                RevertPanelsToWaiting();
                _state = RoomState.Ready;
                UpdateCta();
                return;
            }

            switch (start.Status)
            {
                case BattleStartStatus.InvalidConfig:
                    _result = null;
                    _state  = RoomState.Finished;
                    UpdateCta();
                    return;

                case BattleStartStatus.InsufficientFunds:
                    GameEvents.RaiseToast("Not enough VP to join this battle");
                    RevertPanelsToWaiting();
                    _state = RoomState.Ready;
                    UpdateCta();
                    return;

                case BattleStartStatus.ServiceUnavailable:
                    RevertPanelsToWaiting();
                    _state = RoomState.Ready;
                    UpdateCta();
                    return;
            }

            _result = start.Battle;
            _state  = RoomState.Running;
            UpdateCta();

            StartCoroutine(RunBattle());
        }

        void BuildBattleArea(RectTransform rt)
        {
            const float topOffset = HeaderH + 16f + SummaryH + 12f;
            const float botOffset = NavReserve + CtaH + 16f;

            var areaGo = NewGo("BattleArea", rt);
            _battleArea = (RectTransform)areaGo.transform;
            _battleArea.anchorMin = new Vector2(0f, 0f);
            _battleArea.anchorMax = new Vector2(1f, 1f);
            _battleArea.offsetMin = new Vector2(SidePad, botOffset);
            _battleArea.offsetMax = new Vector2(-SidePad, -topOffset);

            _statusLbl = MakeTmp(_battleArea, "Status", "WAITING TO START", 12f, FontStyles.Bold, ColorPalette.TextDim);
            _statusLbl.characterSpacing = 3f;
            _statusLbl.alignment = TextAlignmentOptions.Center;
            TopStrip(_statusLbl.rectTransform, 20f, 0f);
        }

        // Builds the real battle panels up front, in the pre-battle waiting state.
        // Each panel shows an avatar/name/status presentation; its roulette reel is
        // hidden until START BATTLE is pressed.
        IEnumerator StageBattlePanels() => StageBattlePanels(null);

        // identities (online mode): per-slot display name + isUser + avatar, in the exact
        // order the panels should appear (local player first). When null, the legacy
        // YOU / BOT n identities and _lobby.MaxPlayers count are used.
        IEnumerator StageBattlePanels(List<(string name, bool isUser, Sprite avatar)> identities)
        {
            yield return null;
            yield return null;

            if (_battleArea == null) yield break;

            // Root cause of the intermittent low-content bug: panel geometry was computed
            // from _battleArea.rect before the layout system finalized the area's size, so
            // on a bad frame an oversized area height pushed the centered content far down.
            // Force the canvas/layout to settle and wait for a valid rect before measuring.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_battleArea);
            for (int guard = 0; guard < 8 && (_battleArea.rect.width < 1f || _battleArea.rect.height < 1f); guard++)
                yield return null;
            Debug.Log($"[ONLINE_BATTLE] battle view layout rebuilt area={_battleArea.rect.width:F0}x{_battleArea.rect.height:F0}");

            float areaW = _battleArea.rect.width;
            float areaH = _battleArea.rect.height;

            const float statusH = 24f;
            const float gap     = 5f;

            int count = identities != null && identities.Count >= 2
                ? Mathf.Clamp(identities.Count, 2, 4)
                : Mathf.Clamp(_lobby?.MaxPlayers ?? 2, 2, 4);

            float panelW = count switch
            {
                2 => Mathf.Min(460f, (areaW - gap) / 1.05f),
                3 => Mathf.Min(360f, (areaW - gap * 2f) / 1.35f),
                _ => Mathf.Min(420f, (areaW - gap) / 1.05f),
            };

            float panelH = count == 4
                ? Mathf.Min(680f, (areaH - statusH - gap) / 1.35f)
                : Mathf.Min(1120f, areaH - statusH);

            int cols = count == 4 ? 2 : count;
            int rows = count == 4 ? 2 : 1;

            var scrollGo = NewGo("PanelScroll", _battleArea, typeof(ScrollRect), typeof(Image));
            scrollGo.GetComponent<Image>().color = Color.clear;

            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(0f, 0f);
            scrollRt.offsetMax = new Vector2(0f, -statusH);

            var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch((RectTransform)viewport.transform);

            var content = NewGo("Content", viewport.transform);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);

            float contentW = cols * panelW + (cols - 1) * gap;
            float contentH = rows * panelH + (rows - 1) * gap;

            float startX = Mathf.Max(0f, (areaW - contentW) * 0.5f);
            float startY = -Mathf.Clamp((areaH - statusH - contentH) * 0.18f, 16f, 80f);

            contentRt.anchoredPosition = new Vector2(startX, startY);
            contentRt.sizeDelta = new Vector2(contentW, contentH);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.viewport = (RectTransform)viewport.transform;
            sr.content = contentRt;
            sr.horizontal = contentW > areaW;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 0f;

            var visuals = GameContext.Instance != null ? GameContext.Instance.RarityVisuals : null;

            for (int i = 0; i < count; i++)
            {
                int col = count == 4 ? i % 2 : i;
                int row = count == 4 ? i / 2 : 0;

                float x = col * (panelW + gap);
                float y = row * (panelH + gap);

                var panelGo = NewGo("Panel_" + i, contentRt);
                var pRt = (RectTransform)panelGo.transform;
                pRt.anchorMin = new Vector2(0f, 1f);
                pRt.anchorMax = new Vector2(0f, 1f);
                pRt.pivot = new Vector2(0f, 1f);
                pRt.anchoredPosition = new Vector2(x, -y);
                pRt.sizeDelta = new Vector2(panelW, panelH);

                // Staged identity matches the engine's player order exactly
                // (index 0 = YOU, others = BOT n), so these same panels are reused
                // when the battle starts — no rebuild, no flash.
                var stagePlayer = new BattlePlayerResult
                {
                    Name   = identities != null && i < identities.Count ? identities[i].name : (i == 0 ? "YOU" : "BOT " + i),
                    IsUser = identities != null && i < identities.Count ? identities[i].isUser : i == 0,
                    Avatar = identities != null && i < identities.Count ? identities[i].avatar : null
                };

                var view = panelGo.AddComponent<BattleRouletteView>();
                view.Initialize(stagePlayer, visuals, null, panelW, panelH);
                view.ShowWaiting();
                _panels.Add(view);
            }

            _panelsStaged = true;
        }

        IEnumerator RunBattle()
        {
            // Panels were already staged in the Ready state; wait for them if the
            // user somehow pressed START before staging completed.
            while (!_panelsStaged) yield return null;

            int count = Mathf.Min(_result.PlayerCount, _panels.Count);

            // Always apply the authoritative reel pool from the result.
            for (int i = 0; i < count; i++)
                _panels[i].SetReelPool(_result.ReelPool);

            // Transition staged panels into the reel — UNLESS the optimistic warmup
            // already revealed and spun them, in which case we flow straight into the
            // determined rounds with no extra delay or flash.
            if (!_warmupShown)
            {
                for (int i = 0; i < count; i++)
                    StartCoroutine(_panels[i].HideWaiting(0.25f));

                yield return new WaitForSecondsRealtime(0.35f);

                SetRoomStatus("OPENING CASES…", ColorPalette.ActiveRed);
                for (int i = 0; i < count; i++)
                    _panels[i].SetStatus("PLAYING", ColorPalette.ActiveRed);
            }

            const float baseDur = 1.9f;

            for (int round = 0; round < _result.Rounds; round++)
            {
                // Tüm oyuncuların reel'leri aynı anda başlasın, aynı hızda dönsün ve aynı anda dursun.
                for (int i = 0; i < count; i++)
                {
                    var player = _result.Players[i];
                    var skin = round < player.Skins.Count ? player.Skins[round] : null;
                    StartCoroutine(_panels[i].SpinRound(skin, baseDur));
                }

                float maxDur = baseDur;
                yield return new WaitForSecondsRealtime(maxDur + 0.15f);

                for (int i = 0; i < count; i++)
                {
                    var player = _result.Players[i];
                    var skin = round < player.Skins.Count ? player.Skins[round] : null;
                    if (skin == null) continue;

                    _panels[i].AddResultRow(skin);
                }

                yield return new WaitForSecondsRealtime(0.35f);
            }

            // Son skin durduktan sonra kısa bir bekleme, ardından roulette alanını gizle.
            yield return new WaitForSecondsRealtime(0.3f);

            for (int i = 0; i < count; i++)
                StartCoroutine(_panels[i].HideReel(0.25f));

            yield return new WaitForSecondsRealtime(0.3f);

            // Reel'in yerine büyük "FINAL EARNINGS" alanını getir.
            for (int i = 0; i < count; i++)
                StartCoroutine(_panels[i].ShowFinalEarnings(0.2f));

            yield return new WaitForSecondsRealtime(0.2f);

            for (int i = 0; i < count; i++)
            {
                _panels[i].ShowTotal(0);
                _panels[i].SetTotalColor(ColorPalette.GoldAccent);
                _panels[i].SetStatus("COUNTING", ColorPalette.GoldAccent);
            }

            SetRoomStatus("FINAL EARNINGS", ColorPalette.GoldAccent);

            const float countUpDuration = 1.15f;

            for (int i = 0; i < count; i++)
            {
                int finalTotal = _result.Players[i].TotalVp;
                StartCoroutine(_panels[i].CountUpTotal(finalTotal, countUpDuration));
            }

            yield return new WaitForSecondsRealtime(countUpDuration + 0.1f);

            for (int i = 0; i < count; i++)
            {
                _panels[i].SetStatus("FINISHED", ColorPalette.TextDim);
                _panels[i].MarkWinner(i == _result.WinnerIndex);
            }

            SetRoomStatus("BATTLE FINISHED!", ColorPalette.GoldAccent);

            // Service owns rewards (inventory grant) + statistics + saves, in the same
            // order as before. The screen no longer touches VP or inventory directly.
            // Guarded by _resultSettled so a leave-triggered force-settle and this normal
            // settle never both run (and the service's own guards back this up).
            if (!_resultSettled)
            {
                _battleService?.Settle(_result);
                _resultSettled = true;
            }

            _state = RoomState.Finished;
            // Online mode hides the bottom CTA while waiting/running; bring it back as
            // the "BACK TO LOBBY" affordance once the battle has resolved.
            if (_online && _startBg != null) _startBg.gameObject.SetActive(true);
            UpdateCta();

            yield return new WaitForSecondsRealtime(0.25f);
            ShowResultPopup();

            if (_result != null && _result.UserWon)
                PlayWinCelebration();
        }

        void PlayWinCelebration()
        {
            DestroyConfetti();
            _confettiRoot = NewGo("WinConfetti", (RectTransform)transform);
            Stretch((RectTransform)_confettiRoot.transform);
            var cg = _confettiRoot.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable   = false;
            _confettiRoot.transform.SetAsLastSibling();
            Debug.Log("[ONLINE_BATTLE] win celebration triggered");
            StartCoroutine(ConfettiRoutine((RectTransform)_confettiRoot.transform));
        }

        void DestroyConfetti()
        {
            if (_confettiRoot == null) return;
            Destroy(_confettiRoot);
            _confettiRoot = null;
            Debug.Log("[ONLINE_BATTLE] win celebration cleaned");
        }

        IEnumerator ConfettiRoutine(RectTransform root)
        {
            if (root == null) yield break;

            const int   count    = 80;
            const float duration = 3f;
            const float gravity  = -1700f;

            float w = root.rect.width  > 1f ? root.rect.width  : 800f;
            float h = root.rect.height > 1f ? root.rect.height : 1280f;

            var palette = new[]
            {
                new Color(1f, 0.27f, 0.33f), new Color(0.98f, 0.84f, 0.35f),
                new Color(0.20f, 0.80f, 0.45f), new Color(0.30f, 0.62f, 1f),
                Color.white, new Color(0.78f, 0.45f, 1f),
            };

            var rts  = new RectTransform[count];
            var vel  = new Vector2[count];
            var spin = new float[count];

            for (int i = 0; i < count; i++)
            {
                var piece = MakeImage("c" + i, root, palette[UnityEngine.Random.Range(0, palette.Length)]);
                piece.raycastTarget = false;
                var prt = piece.rectTransform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
                prt.pivot     = new Vector2(0.5f, 0.5f);
                float sw = UnityEngine.Random.Range(7f, 15f);
                float sh = UnityEngine.Random.Range(7f, 15f);
                prt.sizeDelta        = new Vector2(sw, sh);
                prt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-w * 0.5f, w * 0.5f), UnityEngine.Random.Range(0f, 80f));
                prt.localEulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                rts[i]  = prt;
                vel[i]  = new Vector2(UnityEngine.Random.Range(-260f, 260f), UnityEngine.Random.Range(-620f, -180f));
                spin[i] = UnityEngine.Random.Range(-360f, 360f);
            }

            float t = 0f;
            while (t < duration)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                float fade = t > duration - 0.5f ? Mathf.Clamp01((duration - t) / 0.5f) : 1f;
                for (int i = 0; i < count; i++)
                {
                    if (rts[i] == null) continue;
                    vel[i].y += gravity * dt;
                    rts[i].anchoredPosition += vel[i] * dt;
                    rts[i].localEulerAngles += new Vector3(0f, 0f, spin[i] * dt);
                    var img = rts[i].GetComponent<Image>();
                    if (img != null) { var c = img.color; c.a = fade; img.color = c; }
                    if (rts[i].anchoredPosition.y < -h - 40f) rts[i].gameObject.SetActive(false);
                }
                yield return null;
            }

            DestroyConfetti();
        }

        void SetRoomStatus(string text, Color color)
        {
            if (_statusLbl == null) return;
            _statusLbl.text  = text;
            _statusLbl.color = color;
        }

        // ── Online public-lobby flow ──────────────────────────────────────────────

        // Builds the waiting room for an online lobby: summary header, the battle area
        // (reused later for the reel animation), a slots panel, and the 1s poll loop.
        void BuildOnlineRoom()
        {
            if (_dynamicRoot == null) return;

            for (int i = _dynamicRoot.childCount - 1; i >= 0; i--)
                Destroy(_dynamicRoot.GetChild(i).gameObject);

            var rt = (RectTransform)_dynamicRoot;
            BuildLobbySummary(rt);
            BuildBattleArea(rt);
            BuildSlotsOverlay();

            // No local START in online mode — the backend starts and resolves the battle.
            if (_startBg != null) _startBg.gameObject.SetActive(false);

            _panels.Clear();
            _panelsStaged = false;
            _warmupShown  = false;

            SetRoomStatus("BAĞLANIYOR…", ColorPalette.TextDim);

            if (_pollCo != null)
            {
                Debug.Log("[ONLINE_BATTLE] stopping existing poll before restart (no duplicate) battleId=" + _battleId);
                StopCoroutine(_pollCo);
            }
            Debug.Log("[ONLINE_BATTLE] start polling battleId=" + _battleId);
            _pollCo = StartCoroutine(PollLobbyRoutine());
        }

        void BuildSlotsOverlay()
        {
            if (_battleArea == null) return;

            var go = NewGo("SlotsRoot", _battleArea);
            _slotsRoot = go;
            Stretch((RectTransform)go.transform);
        }

        // Polls the lobby once per second until it completes, cancels, or the room closes.
        IEnumerator PollLobbyRoutine()
        {
            var ctx = GameContext.Instance;
            while (_online && gameObject.activeInHierarchy && !string.IsNullOrEmpty(_battleId))
            {
                if (ctx == null || ctx.Backend == null)
                {
                    ctx = GameContext.Instance;
                    yield return new WaitForSecondsRealtime(1f);
                    continue;
                }

                LobbyResponse resp = null;
                BackendError  err  = null;
                yield return ctx.Backend.GetBattleLobby(_battleId, r => resp = r, e => err = e);

                if (resp != null) HandleLobbyUpdate(resp);
                // Transient fetch errors are tolerated silently — the next tick retries
                // so a single dropped poll never tears down the room or spams toasts.

                if (!_online || _completedHandled) yield break;
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        void HandleLobbyUpdate(LobbyResponse lobby)
        {
            // A late poll response that arrives after the lobby's PvP lifetime ended must
            // never rebuild the room UI on top of the result/animation.
            if (_completedHandled)
            {
                Debug.Log("[ONLINE_BATTLE] stale completed lobby ignored battleId=" + lobby?.battleId);
                return;
            }

            _lastLobby = lobby;
            RecomputeSeating(lobby);
            string status = (lobby.status ?? "").ToUpperInvariant();

            if (status != _lastStatus)
            {
                Debug.Log($"[ONLINE_BATTLE] status {_lastStatus ?? "-"}->{status} battleId={lobby.battleId}");
                _lastStatus = status;
            }

            switch (status)
            {
                case "WAITING":
                    SetBackButton(!_amSeated);
                    RenderSlots(lobby);
                    SetRoomStatus("OYUNCU BEKLENİYOR…", ColorPalette.TextDim);
                    break;

                case "STARTING":
                    if (!_amSeated) { LeaveAsViewer(); break; }
                    SetBackButton(false);
                    RenderSlots(lobby);
                    SetRoomStatus("BAŞLIYOR…", ColorPalette.ActiveRed);
                    break;

                case "COMPLETED":
                    SetBackButton(false);
                    if (!_amSeated) { LeaveAsViewer(); break; }
                    if (!_completedHandled)
                    {
                        _completedHandled = true;
                        StopPolling();
                        Debug.Log("[ONLINE_BATTLE] completed lobby polling stopped battleId=" + lobby.battleId);
                        BeginCompletedBattle(lobby);
                    }
                    break;

                case "CANCELLED":
                    SetBackButton(false);
                    StopPolling();
                    GameEvents.RaiseToast("Kimse katılmadığı için lobi kapandı.");
                    OnLeave?.Invoke();
                    break;

                default:
                    RenderSlots(lobby);
                    break;
            }
        }

        // A viewer who never seated must not see the battle animation/result. Once the
        // lobby leaves WAITING the room is no longer joinable, so stop polling, inform
        // the user, and return to the lobby list. Guarded so it runs at most once.
        void LeaveAsViewer()
        {
            if (_completedHandled) return;
            _completedHandled = true;
            StopPolling();
            Debug.Log("[ONLINE_BATTLE] viewer left — battle started without them battleId=" + _battleId);
            GameEvents.RaiseToast("Battle started.");
            OnLeave?.Invoke();
        }

        void StopPolling()
        {
            if (_pollCo != null)
            {
                Debug.Log("[ONLINE_BATTLE] stop polling battleId=" + _battleId);
                StopCoroutine(_pollCo);
                _pollCo = null;
            }
        }

        void RecomputeSeating(LobbyResponse lobby)
        {
            var myId = GameContext.Instance?.Save?.Data?.guestAccountId;
            _amHost = lobby?.creator != null && !string.IsNullOrEmpty(myId) &&
                      string.Equals(lobby.creator.accountId, myId, StringComparison.OrdinalIgnoreCase);
            _amSeated = false;
            if (lobby?.slots != null && !string.IsNullOrEmpty(myId))
            {
                foreach (var s in lobby.slots)
                    if (s != null && !string.Equals(s.type, "EMPTY", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(s.accountId, myId, StringComparison.OrdinalIgnoreCase))
                    { _amSeated = true; break; }
            }
        }

        // Rebuilds the slots as battle-style participant panels from the latest snapshot.
        // Geometry mirrors StageBattlePanels (same panelW/panelH/grid math) so the waiting
        // room reads as the pre-battle version of the same panels.
        void RenderSlots(LobbyResponse lobby)
        {
            if (_slotsRoot == null || _battleArea == null) return;

            var host = _slotsRoot.transform;
            for (int i = host.childCount - 1; i >= 0; i--)
                Destroy(host.GetChild(i).gameObject);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_battleArea);

            float areaW = _battleArea.rect.width;
            float areaH = _battleArea.rect.height;
            if (areaW < 1f) areaW = ((RectTransform)transform).rect.width - SidePad * 2f;
            if (areaH < 1f) areaH = 600f;

            const float statusH = 24f;
            const float gap     = 5f;

            int max = Mathf.Clamp(lobby.maxSlots > 0 ? lobby.maxSlots : 2, 2, 4);
            string myId = GameContext.Instance?.Save?.Data?.guestAccountId;

            float panelW = max switch
            {
                2 => Mathf.Min(460f, (areaW - gap) / 1.05f),
                3 => Mathf.Min(360f, (areaW - gap * 2f) / 1.35f),
                _ => Mathf.Min(420f, (areaW - gap) / 1.05f),
            };
            float panelH = max == 4
                ? Mathf.Min(680f, (areaH - statusH - gap) / 1.35f)
                : Mathf.Min(1120f, areaH - statusH);

            int cols = max == 4 ? 2 : max;
            int rows = max == 4 ? 2 : 1;

            float contentW = cols * panelW + (cols - 1) * gap;
            float contentH = rows * panelH + (rows - 1) * gap;

            float startX = Mathf.Max(0f, (areaW - contentW) * 0.5f);
            float startY = -Mathf.Clamp((areaH - statusH - contentH) * 0.18f, 16f, 80f);

            Transform content;
            Vector2   basePos;
            if (contentW > areaW)
            {
                var scrollGo = NewGo("SlotScroll", host, typeof(ScrollRect), typeof(Image));
                scrollGo.GetComponent<Image>().color = Color.clear;
                var scrollRt = (RectTransform)scrollGo.transform;
                Stretch(scrollRt);
                scrollRt.offsetMax = new Vector2(0f, -statusH);

                var viewport = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D), typeof(Image));
                viewport.GetComponent<Image>().color = Color.clear;
                Stretch((RectTransform)viewport.transform);

                var cgo = NewGo("Content", viewport.transform);
                var cRt = (RectTransform)cgo.transform;
                cRt.anchorMin = new Vector2(0f, 1f);
                cRt.anchorMax = new Vector2(0f, 1f);
                cRt.pivot     = new Vector2(0f, 1f);
                cRt.sizeDelta = new Vector2(contentW, contentH);
                cRt.anchoredPosition = new Vector2(startX, startY);

                var sr = scrollGo.GetComponent<ScrollRect>();
                sr.viewport         = (RectTransform)viewport.transform;
                sr.content          = cRt;
                sr.horizontal       = true;
                sr.vertical         = false;
                sr.movementType     = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 0f;

                content = cRt;
                basePos = Vector2.zero;
            }
            else
            {
                content = host;
                basePos = new Vector2(startX, startY);
            }

            for (int i = 0; i < max; i++)
            {
                int col = max == 4 ? i % 2 : i;
                int row = max == 4 ? i / 2 : 0;
                var pos = new Vector2(basePos.x + col * (panelW + gap),
                                      basePos.y - row * (panelH + gap));
                BuildSlotPanel(content, i, FindSlot(lobby, i), lobby, myId, panelW, panelH, pos);
            }
        }

        static LobbySlotResponse FindSlot(LobbyResponse lobby, int slotIndex)
        {
            if (lobby?.slots == null) return null;
            foreach (var s in lobby.slots)
                if (s != null && s.slotIndex == slotIndex) return s;
            return null;
        }

        // One waiting-room participant panel, styled to match BattleRouletteView's
        // pre-battle panel: dark angled card, accent outline, avatar/name/status header,
        // and an interior that is either the seated identity or the empty-slot actions.
        void BuildSlotPanel(Transform parent, int index, LobbySlotResponse slot, LobbyResponse lobby,
                            string myId, float w, float h, Vector2 pos)
        {
            string type  = slot != null ? (slot.type ?? "EMPTY") : "EMPTY";
            bool   empty = slot == null || string.Equals(type, "EMPTY", StringComparison.OrdinalIgnoreCase);
            bool   isBot = string.Equals(type, "BOT", StringComparison.OrdinalIgnoreCase);
            bool   mine  = !empty && !isBot && slot != null && !string.IsNullOrEmpty(myId) &&
                           string.Equals(slot.accountId, myId, StringComparison.OrdinalIgnoreCase);

            Color accent = mine ? ColorPalette.GoldAccent : ColorPalette.ActiveRed;

            var panelGo = NewGo("SlotPanel_" + index, parent);
            var pRt = (RectTransform)panelGo.transform;
            pRt.anchorMin = new Vector2(0f, 1f);
            pRt.anchorMax = new Vector2(0f, 1f);
            pRt.pivot     = new Vector2(0f, 1f);
            pRt.anchoredPosition = pos;
            pRt.sizeDelta = new Vector2(w, h);

            var bg = MakeAngled("PanelBg", pRt, ColorPalette.CardBg, 6f, raycast: true);
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = empty ? ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.35f) : accent;
            border.effectDistance = new Vector2(empty ? 1f : 2f, empty ? -1f : -2f);

            const float pad     = 6f;
            const float headerH = 28f;

            string headerName = empty ? "BOŞ SLOT"
                : isBot ? (string.IsNullOrEmpty(slot.displayName) ? "BOT" : slot.displayName)
                : (string.IsNullOrEmpty(slot.displayName) ? "OYUNCU" : slot.displayName) + (mine ? "  (SEN)" : "");
            string headerStatus = empty ? "—" : isBot ? "BOT" : (mine ? "SEN" : "OYUNCU");
            Color  headerColor  = empty ? ColorPalette.TextDim : ColorPalette.TextBright;

            BuildPanelHeader(pRt, slot, empty, isBot, mine, accent, headerName, headerStatus, headerColor, pad, headerH);

            float reelH       = Mathf.Clamp(h * 0.17f, 52f, 78f) * 2.8f;
            float interiorTop = headerH + 5f;

            if (!empty)
            {
                BuildOccupiedInterior(pRt, slot, isBot, mine, accent, interiorTop, reelH);
                return;
            }

            var emptyLbl = MakeTmp(pRt, "EmptyLbl", "BOŞ SLOT", 13f, FontStyles.Bold, ColorPalette.TextDim);
            emptyLbl.characterSpacing = 2f;
            emptyLbl.alignment = TextAlignmentOptions.Center;
            SetRect(emptyLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(interiorTop + reelH * 0.32f)), new Vector2(-12f, 22f));

            bool waiting   = string.Equals(lobby.status, "WAITING", StringComparison.OrdinalIgnoreCase);
            bool canJoin   = waiting && !_amSeated;
            bool canAddBot = waiting && _amHost && (lobby.addBotAvailable || (slot != null && slot.addBotAllowed));

            if (canJoin)
            {
                int captured = index;
                BuildPanelButton(pRt, "KATIL", new Color(0.20f, 0.62f, 1f), () => OnJoinSlotPressed(captured));
            }
            else if (canAddBot)
            {
                BuildPanelButton(pRt, "BOT EKLE", ColorPalette.ActiveRed, OnAddBotPressed);
            }
            else
            {
                var hint = MakeTmp(pRt, "Hint", "BEKLENİYOR", 10f, FontStyles.Bold, ColorPalette.TextDim);
                hint.characterSpacing = 2f;
                hint.alignment = TextAlignmentOptions.Center;
                SetRect(hint.rectTransform,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 18f), new Vector2(-12f, 16f));
            }
        }

        void BuildPanelHeader(RectTransform pRt, LobbySlotResponse slot, bool empty, bool isBot, bool mine,
                              Color accent, string name, string status, Color nameColor, float pad, float h)
        {
            var header = NewGo("Header", pRt);
            var hRt = (RectTransform)header.transform;
            TopStrip(hRt, h, -4f);
            hRt.offsetMin = new Vector2(pad, hRt.offsetMin.y);
            hRt.offsetMax = new Vector2(-pad, hRt.offsetMax.y);

            var avatar = MakeAngled("Avatar", header.transform, empty ? ColorPalette.Surface : accent, 4f);
            SetRect(avatar.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(22f, 22f));

            if (!empty)
            {
                var sprite = ResolveSlotAvatar(slot, isBot, mine);
                if (sprite != null)
                {
                    var photo = MakeImage("Photo", avatar.transform, Color.white);
                    photo.sprite = sprite;
                    photo.preserveAspect = true;
                    var ph = photo.rectTransform;
                    ph.anchorMin = new Vector2(0f, 0f);
                    ph.anchorMax = new Vector2(1f, 1f);
                    ph.offsetMin = new Vector2(1f, 1f);
                    ph.offsetMax = new Vector2(-1f, -1f);
                }
                else
                {
                    string initial = !string.IsNullOrEmpty(name) ? name.Substring(0, 1).ToUpperInvariant() : "?";
                    var aLbl = MakeTmp(avatar.transform, "I", initial, 12f, FontStyles.Bold,
                        mine ? ColorPalette.BgDeep : Color.white);
                    aLbl.alignment = TextAlignmentOptions.Center;
                    Stretch(aLbl.rectTransform);
                }
            }

            var nameLbl = MakeTmp(header.transform, "Name", name, 12f, FontStyles.Bold, nameColor);
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -1f), new Vector2(-28f, 15f));

            var statusLbl = MakeTmp(header.transform, "Status", status, 8f, FontStyles.Bold,
                empty ? ColorPalette.TextDim : accent);
            statusLbl.characterSpacing = 1f;
            statusLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(statusLbl.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 1f), new Vector2(-28f, 12f));

            var sep = MakeImage("HeaderSeparator", pRt, ColorPalette.WithAlpha(Color.white, 0.12f), raycast: false);
            var sepRt = sep.rectTransform;
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot     = new Vector2(0.5f, 1f);
            sepRt.sizeDelta = new Vector2(0f, 1.5f);
            sepRt.anchoredPosition = new Vector2(0f, -(4f + h));
            sepRt.offsetMin = new Vector2(pad + 6f, sepRt.offsetMin.y);
            sepRt.offsetMax = new Vector2(-(pad + 6f), sepRt.offsetMax.y);
        }

        // Centered avatar/name/status block for a seated slot, mirroring
        // BattleRouletteView's pre-battle waiting presentation.
        void BuildOccupiedInterior(RectTransform pRt, LobbySlotResponse slot, bool isBot, bool mine,
                                   Color accent, float top, float reelH)
        {
            var sprite = ResolveSlotAvatar(slot, isBot, mine);
            string name = isBot
                ? (string.IsNullOrEmpty(slot.displayName) ? "BOT" : slot.displayName)
                : (string.IsNullOrEmpty(slot.displayName) ? "OYUNCU" : slot.displayName);
            string initial = !string.IsNullOrEmpty(name) ? name.Substring(0, 1).ToUpperInvariant() : "?";

            float avatarSize = Mathf.Clamp(reelH * 0.34f, 64f, 132f);
            float blockH     = avatarSize + 16f + 24f + 6f + 16f;
            float padTop     = Mathf.Max(8f, (reelH - blockH) * 0.5f);

            var ring = MakeAngled("AvatarRing", pRt, ColorPalette.WithAlpha(accent, 0.14f), 8f);
            SetRect(ring.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(top + padTop - 6f)), new Vector2(avatarSize + 12f, avatarSize + 12f));
            var ringB = ring.gameObject.AddComponent<Outline>();
            ringB.effectColor    = ColorPalette.WithAlpha(accent, 0.5f);
            ringB.effectDistance = new Vector2(1f, -1f);

            var avatar = MakeAngled("Avatar", pRt, accent, 7f);
            SetRect(avatar.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(top + padTop)), new Vector2(avatarSize, avatarSize));
            var glow = avatar.gameObject.AddComponent<Shadow>();
            glow.effectColor    = ColorPalette.WithAlpha(accent, 0.55f);
            glow.effectDistance = new Vector2(0f, -4f);

            if (sprite != null)
            {
                var photo = MakeImage("Photo", avatar.transform, Color.white);
                photo.sprite = sprite;
                photo.preserveAspect = true;
                var ph = photo.rectTransform;
                ph.anchorMin = new Vector2(0f, 0f);
                ph.anchorMax = new Vector2(1f, 1f);
                ph.offsetMin = new Vector2(4f, 4f);
                ph.offsetMax = new Vector2(-4f, -4f);
            }
            else
            {
                var initialLbl = MakeTmp(avatar.transform, "Initial", initial,
                    avatarSize * 0.42f, FontStyles.Bold, mine ? ColorPalette.BgDeep : Color.white);
                initialLbl.alignment = TextAlignmentOptions.Center;
                Stretch(initialLbl.rectTransform);
            }

            var nameLbl = MakeTmp(pRt, "CName", name + (mine ? "  (SEN)" : ""),
                14f, FontStyles.Bold, ColorPalette.TextBright);
            nameLbl.alignment = TextAlignmentOptions.Center;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(top + padTop + avatarSize + 14f)), new Vector2(-8f, 24f));

            var statusLbl = MakeTmp(pRt, "CStatus", isBot ? "BOT" : (mine ? "SEN" : "HAZIR"),
                9f, FontStyles.Bold, mine ? ColorPalette.GoldAccent : ColorPalette.ActiveRed);
            statusLbl.alignment = TextAlignmentOptions.Center;
            statusLbl.characterSpacing = 3f;
            SetRect(statusLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(top + padTop + avatarSize + 14f + 24f + 6f)), new Vector2(-8f, 16f));
        }

        // Backend-authoritative avatar: every real player / bot shows the avatarId the
        // server stored for that slot. The local user prefers their own configured avatar.
        // Unknown / blank ids fall back to the shared default — never a faked per-slot face.
        Sprite ResolveSlotAvatar(LobbySlotResponse slot, bool isBot, bool mine)
        {
            if (mine && PlayerProfileData.Avatar != null) return PlayerProfileData.Avatar;
            return ProfileManager.ResolveAvatarSprite(slot != null ? slot.avatarId : null);
        }

        // Bottom-centered action button inside an empty participant panel. The opaque
        // Image is the only raycast target, the label is non-blocking, and the button is
        // forced last sibling so no panel element can swallow the tap on mobile.
        void BuildPanelButton(RectTransform pRt, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go  = NewGo("PanelBtn", pRt, typeof(Image), typeof(Button));
            var img = go.GetComponent<Image>();
            img.color         = color;
            img.raycastTarget = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 20f);
            rt.sizeDelta = new Vector2(150f, 48f);
            go.transform.SetAsLastSibling();

            var btn = go.GetComponent<Button>();
            btn.transition   = Selectable.Transition.None;
            btn.interactable = true;
            btn.onClick.AddListener(onClick);

            var lbl = MakeTmp(go.transform, "Lbl", label, 13f, FontStyles.Bold, Color.white);
            lbl.alignment        = TextAlignmentOptions.Center;
            lbl.characterSpacing = 1f;
            Stretch(lbl.rectTransform);
        }

        void OnJoinSlotPressed(int slotIndex)
        {
            var ctx = GameContext.Instance;
            if (_joining || _amSeated || ctx == null || ctx.Backend == null || string.IsNullOrEmpty(_battleId)) return;
            Debug.Log("[ONLINE_BATTLE] join requested slot=" + slotIndex + " battleId=" + _battleId);
            StartCoroutine(JoinSlotRoutine(slotIndex));
        }

        IEnumerator JoinSlotRoutine(int slotIndex)
        {
            _joining = true;
            var ctx = GameContext.Instance;
            LobbyResponse resp = null;
            BackendError  err  = null;
            yield return ctx.Backend.JoinBattleLobby(_battleId, slotIndex, r => resp = r, e => err = e);
            _joining = false;

            if (err != null || resp == null)
            {
                Debug.LogWarning("[ONLINE_BATTLE] join failed — " + (err?.ToString() ?? "null response"));
                GameEvents.RaiseToast(MapLobbyError(err));
                yield break;
            }

            Debug.Log("[ONLINE_BATTLE] join success battleId=" + _battleId);
            ctx.RequestBackendResync();
            HandleLobbyUpdate(resp);
        }

        // Sends Add Bot. The bot is NOT shown until the backend confirms — on success the
        // returned lobby (or the next poll) re-renders the now-filled slot.
        void OnAddBotPressed()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Backend == null || string.IsNullOrEmpty(_battleId)) return;
            StartCoroutine(AddBotRoutine());
        }

        IEnumerator AddBotRoutine()
        {
            var ctx = GameContext.Instance;
            LobbyResponse resp = null;
            BackendError  err  = null;
            yield return ctx.Backend.AddBotToBattleLobby(_battleId, r => resp = r, e => err = e);

            if (err != null || resp == null)
            {
                Debug.LogWarning("[BATTLE_LOBBY] add-bot failed — " + (err?.ToString() ?? "null response"));
                GameEvents.RaiseToast(MapLobbyError(err));
                yield break;
            }
            HandleLobbyUpdate(resp);   // reflect the filled slot immediately
        }

        // Lobby/battle completed: map the authoritative result and reuse the existing
        // reel/result animation. No local rewards — the backend already granted them;
        // Settle() (BackendBattleService) only records stats and resyncs inventory.
        void BeginCompletedBattle(LobbyResponse lobby)
        {
            SetBackButton(false);

            Debug.Log("[ONLINE_BATTLE] completed lobby cleared battleId=" + lobby.battleId);
            _battleId  = null;
            _lastLobby = null;

            var ctx  = GameContext.Instance;
            var myId = ctx?.Save?.Data?.guestAccountId;
            _result  = BattleLobbyMapper.ToBattleResult(lobby, ctx?.Content, myId);

            Debug.Log($"[ONLINE_BATTLE] completed result received battleId={lobby.battleId} players={_result?.PlayerCount} " +
                      $"winnerSlot={lobby.winnerSlotIndex} userWon={_result?.UserWon}");

            if (_result == null || !_result.IsValid)
            {
                Debug.LogWarning("[ONLINE_BATTLE] completed lobby not mappable locally — resyncing.");
                ResyncAfterPvp(lobby);
                GameEvents.RaiseToast(BackendErrorMapper.Generic);
                OnLeave?.Invoke();
                return;
            }

            Debug.Log("[ONLINE_BATTLE] local player " + (_result.UserWon ? "WIN" : "LOSS") + " detected");
            ResyncAfterPvp(lobby);

            if (_slotsRoot != null) { Destroy(_slotsRoot); _slotsRoot = null; }

            var identities = new List<(string name, bool isUser, Sprite avatar)>();
            foreach (var p in _result.Players) identities.Add((p.Name, p.IsUser, p.Avatar));

            _panels.Clear();
            _panelsStaged = false;
            _warmupShown  = false;

            _state = RoomState.Running;
            StartCoroutine(StageBattlePanels(identities));
            StartCoroutine(RunBattle());
        }

        void ResyncAfterPvp(LobbyResponse lobby)
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;

            if (lobby?.progression != null)
                ProgressionSync.ApplyFromProgression(lobby.progression, showXpToast: _result != null && _result.UserWon);

            Debug.Log("[ONLINE_BATTLE] wallet/inventory/progression resync started battleId=" + lobby?.battleId);
            void OnInv()
            {
                GameEvents.OnInventoryChanged -= OnInv;
                Debug.Log("[ONLINE_BATTLE] inventory UI refresh after PvP — resync completed");
            }
            GameEvents.OnInventoryChanged += OnInv;
            ctx.RequestBackendResync();
        }

        // Only unseated viewers (and local fallback) can press Leave; it never mutates
        // backend state, refunds VP, or cancels the lobby. Seated players have Leave hidden.
        void RequestLeave()
        {
            SetBackButton(false);
            Debug.Log("[ONLINE_BATTLE] viewer leave battleId=" + _battleId);
            OnLeave?.Invoke();
        }

        // Player-facing message for lobby action failures (join / add-bot / create).
        // internal so LobbyListScreen reuses the same mapping for create/join errors.
        internal static string MapLobbyError(BackendError e)
        {
            if (e == null) return BackendErrorMapper.Generic;
            if (e.IsOffline) return BackendErrorMapper.Offline;
            if (e.IsTimeout) return BackendErrorMapper.Timeout;
            if (e.IsLockedCategory) return $"Bu kasa Seviye {e.RequiredLevel}'te açılır.";
            switch (e.HttpStatus)
            {
                case 402: return "Yetersiz VP.";
                case 403: return "Bu işleme izin yok.";
                case 409: return "İşlem yapılamadı. Lobi dolu, başlamış veya iptal olmuş olabilir.";
                case 429: return BackendErrorMapper.TooManyReq;
            }
            return BackendErrorMapper.Map(e);
        }

        void ShowResultPopup()
        {
            if (_result == null) return;

            bool won = _result.UserWon;
            ColorUtility.TryParseHtmlString("#2ECC71", out var green);
            Color theme = won ? green : ColorPalette.ActiveRed;

            var root = (RectTransform)transform;

            var overlay = MakeImage("ResultOverlay", root, ColorPalette.WithAlpha(Color.black, 0.78f), raycast: true);
            Stretch(overlay.rectTransform);
            _resultOverlay = overlay.gameObject;

            var card = MakeAngled("ResultCard", overlay.transform, ColorPalette.CardBg, 12f, raycast: true);
            SetRect(card.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(300f, won ? 240f : 210f));

            var border = card.gameObject.AddComponent<Outline>();
            border.effectColor    = theme;
            border.effectDistance = new Vector2(2f, -2f);

            var glow = card.gameObject.AddComponent<Shadow>();
            glow.effectColor    = ColorPalette.WithAlpha(theme, 0.6f);
            glow.effectDistance = new Vector2(0f, -4f);

            var accent = MakeImage("Accent", card.transform, theme);
            accent.raycastTarget = false;
            TopStrip(accent.rectTransform, 4f);

            var title = MakeTmp(card.transform, "Title", won ? "YOU WIN" : "YOU LOSE",
                34f, FontStyles.Bold, theme);
            title.characterSpacing = 3f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -34f), new Vector2(-20f, 44f));

            if (won)
            {
                string prize = $"<color=#{Hex(ColorPalette.GoldAccent)}><b>{_result.TotalPotVp:N0} VP</b></color>";

                var prizeLbl = MakeTmp(card.transform, "Prize", "TOTAL PRIZE\n" + prize,
                    16f, FontStyles.Normal, ColorPalette.TextDim);
                prizeLbl.alignment = TextAlignmentOptions.Center;
                prizeLbl.lineSpacing = 12f;
                SetRect(prizeLbl.rectTransform,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -92f), new Vector2(-24f, 50f));

                var skinsLbl = MakeTmp(card.transform, "Skins",
                    $"{_result.AllSkins.Count} SKINS WON", 11f, FontStyles.Bold, ColorPalette.TextBright);
                skinsLbl.characterSpacing = 1.5f;
                skinsLbl.alignment = TextAlignmentOptions.Center;
                SetRect(skinsLbl.rectTransform,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -150f), new Vector2(-24f, 18f));
            }
            else
            {
                var msg = MakeTmp(card.transform, "Msg", "Better luck next time.",
                    13f, FontStyles.Normal, ColorPalette.TextDim);
                msg.alignment = TextAlignmentOptions.Center;
                SetRect(msg.rectTransform,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -100f), new Vector2(-24f, 24f));
            }

            var btnBg = MakeAngled("ContinueBtn", card.transform, theme, 8f, raycast: true);
            SetRect(btnBg.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(180f, 42f));

            var btn = btnBg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(DismissResultPopup);

            var btnLbl = MakeTmp(btnBg.transform, "Lbl", won ? "CLAIM" : "CONTINUE",
                14f, FontStyles.Bold, won ? ColorPalette.BgDeep : Color.white);
            btnLbl.characterSpacing = 2f;
            btnLbl.alignment = TextAlignmentOptions.Center;
            Stretch(btnLbl.rectTransform);

            StartCoroutine(PopIn(card.rectTransform));
        }

        static IEnumerator PopIn(RectTransform rt)
        {
            if (rt == null) yield break;

            float t = 0f;
            while (t < 0.22f)
            {
                t += Time.unscaledDeltaTime;
                float e = Mathf.Clamp01(t / 0.22f);
                float s = Mathf.Lerp(0.7f, 1f, 1f - (1f - e) * (1f - e));
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            rt.localScale = Vector3.one;
        }

        static string Hex(Color c)
        {
            Color32 c32 = c;
            return c32.r.ToString("X2") + c32.g.ToString("X2") + c32.b.ToString("X2");
        }
    }
}