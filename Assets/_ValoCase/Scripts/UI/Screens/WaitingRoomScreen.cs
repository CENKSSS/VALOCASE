using System;
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
    public sealed class WaitingRoomScreen : MonoBehaviour
    {
        public event Action OnLeave;
        public event Action<BattleLobbyData> OnStartBattle;

        /// <summary>True while the battle room is up (ready, running or finished).
        /// Only an explicit Leave (OnLeave → CloseWaiting) closes it; navigating
        /// to Settings and back must not.</summary>
        public bool IsOpen => gameObject.activeSelf;

        const float SidePad    = 16f;
        const float HeaderH    = 60f;
        const float CtaH       = 54f;
        const float NavReserve = 98f;

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

        // Battle authority seam (economy / result generation / rewards / stats / save).
        // The screen no longer spends VP or grants skins directly — it calls this.
        IBattleService               _battleService;

        // Battle entry cost = Case Price × Round Count, already baked into WagerVP
        // when the lobby is created (see LobbyListScreen.MakeBotLobby).
        int EntryCost => _lobby?.WagerVP ?? 0;

        public void Show(BattleLobbyData lobby, bool isHost)
        {
            _lobby  = lobby;
            _isHost = isHost;
            _state  = RoomState.Ready;
            _result = null;
            _panels.Clear();
            _panelsStaged = false;
            _warmupRunning = false;
            _warmupShown   = false;
            DismissResultPopup();

            EnsureBattleService();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            BuildOnce();
            RebuildDynamic();
            UpdateCta();

            StartCoroutine(UIAnimator.SlideFromBottom((RectTransform)transform, 0.24f));
        }

        public void Hide()
        {
            StopAllCoroutines();
            DismissResultPopup();
            gameObject.SetActive(false);
        }

        void DismissResultPopup()
        {
            if (_resultOverlay != null) Destroy(_resultOverlay);
            _resultOverlay = null;
        }

        void OnDisable() => StopAllCoroutines();

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
            leaveGo.GetComponent<Image>().color = Color.clear;
            SetRect((RectTransform)leaveGo.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(SidePad, 0f), new Vector2(80f, 44f));

            var leaveLbl = MakeTmp(leaveGo.transform, "Lbl", "✕", 16f, FontStyles.Bold, ColorPalette.ActiveRed);
            leaveLbl.alignment = TextAlignmentOptions.Center;
            leaveLbl.characterSpacing = 1f;
            Stretch(leaveLbl.rectTransform);

            var leaveBtn = leaveGo.GetComponent<Button>();
            leaveBtn.transition = Selectable.Transition.None;
            leaveBtn.onClick.AddListener(() => OnLeave?.Invoke());

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
            const float height    = 118f;

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
            bRt.sizeDelta = new Vector2(3f, 0f);

            const float il = 14f;

            var nameLbl = MakeTmp(card.transform, "CaseName",
                _lobby?.CaseName ?? "Basic Vandal Case",
                15f, FontStyles.Bold, ColorPalette.TextBright);
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(il, -14f), new Vector2(-8f, 22f));

            string modeStr = (_lobby?.MaxPlayers ?? 2) == 4 ? "1V1V1V1" :
                             (_lobby?.MaxPlayers ?? 2) == 3 ? "1V1V1" : "1V1";
            float modeW = modeStr == "1V1V1V1" ? 72f : modeStr == "1V1V1" ? 52f : 40f;

            var modeBadge = MakeAngled("ModeBadge", card.transform, ColorPalette.Surface, 3f);
            SetRect(modeBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(il, -42f), new Vector2(modeW, 18f));

            var mb = modeBadge.gameObject.AddComponent<Outline>();
            mb.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.55f);
            mb.effectDistance = new Vector2(1f, -1f);

            var modeLbl = MakeTmp(modeBadge.transform, "Lbl", modeStr, 9f, FontStyles.Bold, ColorPalette.ActiveRed);
            modeLbl.alignment = TextAlignmentOptions.Center;
            modeLbl.characterSpacing = 1f;
            Stretch(modeLbl.rectTransform);

            int rounds = _lobby?.Rounds ?? 1;
            string roundStr = rounds + (rounds == 1 ? " ROUND" : " ROUNDS");

            var roundBadge = MakeImage("RoundBadge", card.transform, ColorPalette.Surface);
            SetRect(roundBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(il + modeW + 6f, -42f), new Vector2(72f, 18f));

            var rb = roundBadge.gameObject.AddComponent<Outline>();
            rb.effectColor    = ColorPalette.Border;
            rb.effectDistance = new Vector2(1f, -1f);

            var roundLbl = MakeTmp(roundBadge.transform, "Lbl", roundStr, 9f, FontStyles.Bold, ColorPalette.TextDim);
            roundLbl.alignment = TextAlignmentOptions.Center;
            roundLbl.characterSpacing = 1f;
            Stretch(roundLbl.rectTransform);

            var roundCircle = MakeAngled("RoundCircleBadge", card.transform, ColorPalette.ActiveRed, 14f);
            SetRect(roundCircle.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-il, -14f), new Vector2(30f, 30f));

            var roundNumLbl = MakeTmp(roundCircle.transform, "RoundNum",
                rounds.ToString(), 14f, FontStyles.Bold, Color.white);
            roundNumLbl.alignment = TextAlignmentOptions.Center;
            Stretch(roundNumLbl.rectTransform);

            // Show the exact amount that will be deducted at START (Case Price ×
            // Rounds == EntryCost), so the summary stays consistent with the charge.
            string costStr = EntryCost.ToString("N0") + " VP";

            var totalCostLbl = MakeTmp(card.transform, "TotalCost",
                $"Total cost: <color=#{Hex(ColorPalette.GoldAccent)}><b>{costStr}</b></color>",
                10f, FontStyles.Normal, ColorPalette.TextDim);
            totalCostLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(totalCostLbl.rectTransform,
                new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(il, 10f), new Vector2(-4f, 18f));

            var openingLbl = MakeTmp(card.transform, "OpeningCases",
                "Opening cases", 10f, FontStyles.Bold, ColorPalette.TextBright);
            openingLbl.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(openingLbl.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 10f), new Vector2(-il, 18f));
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
                GameEvents.RaiseToast("Battle could not start. Please try again.");
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
            const float topOffset = HeaderH + 16f + 118f + 12f;
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
        IEnumerator StageBattlePanels()
        {
            yield return null;
            yield return null;

            if (_battleArea == null) yield break;

            float areaW = _battleArea.rect.width;
            float areaH = _battleArea.rect.height;

            const float statusH = 24f;
            const float gap     = 5f;

            int count = Mathf.Clamp(_lobby?.MaxPlayers ?? 2, 2, 4);

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
            float startY = -Mathf.Max(16f, (areaH - statusH - contentH) * 0.18f);

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
                    Name   = i == 0 ? "YOU" : "BOT " + i,
                    IsUser = i == 0
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
            _battleService?.Settle(_result);

            _state = RoomState.Finished;
            UpdateCta();

            yield return new WaitForSecondsRealtime(0.25f);
            ShowResultPopup();
        }

        void SetRoomStatus(string text, Color color)
        {
            if (_statusLbl == null) return;
            _statusLbl.text  = text;
            _statusLbl.color = color;
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