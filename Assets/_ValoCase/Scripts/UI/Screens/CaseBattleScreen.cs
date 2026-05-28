using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Battle;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Profile;
using ValoCase.Systems;
using ValoCase.UI.Factory;
using ValoCase.UI.Widgets;

using static ValoCase.UI.Screens.CaseBattlePalette;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Case Battle screen controller — state machine + event wiring only.
    ///
    /// UI construction → CaseBattleUiBuilder.Build()
    /// Roulette animation → CaseBattleRouletteAnimator (added as component)
    /// Game logic (VP, settle) → CaseBattleSystem (injected by CompositionRoot)
    /// Colors / utils → CaseBattlePalette
    /// </summary>
    public sealed class CaseBattleScreen : UIScreenBase
    {
        // ── Inspector refs (wired by ValoCaseUIBuilder) ───────────────────────
        [SerializeField] UINavigator          navigator;
        [SerializeField] Button               backButton;
        [SerializeField] TextMeshProUGUI      walletLabel;   // compat — unused
        [SerializeField] WeaponSkinCardView   cardPrefab;    // compat — unused

        // ── Injected system ───────────────────────────────────────────────────
        CaseBattleSystem _system;

        public void Inject(CaseBattleSystem system) => _system = system;

        // ── Sub-components (added in Awake) ───────────────────────────────────
        CaseBattleRouletteAnimator _roulette;
        PlayerProfileWidget        _profileWidget;

        // ── UI refs (set once by builder) ─────────────────────────────────────
        CaseBattleUiRefs _ui;
        bool             _builtUi;

        // ── Runtime state ─────────────────────────────────────────────────────
        int               _selectedCount = BattleConstants.CaseCountChoices[0];
        CaseBattleSession _session;
        Coroutine         _battleCo;

        // ── New flow state ────────────────────────────────────────────────────
        enum BattleScreenState { Setup, CasePicker, Battle, Results }
        BattleScreenState _state = BattleScreenState.Setup;

        readonly Dictionary<CaseDefinitionSO, int> _selectedCases =
            new Dictionary<CaseDefinitionSO, int>();
        int _selectedPlayerCount = 2;

        // Extra-bot tallies (Bot2 / Bot3) — only used when _selectedPlayerCount > 2
        int _bot2TotalVp;
        int _bot3TotalVp;

        // Draw refund guard — ensures VP is returned exactly once per battle
        bool _drawRefunded;
        int  _battleTotalCost;

        // Bot2 / Bot3 skin lists — populated each round, used in unified settle
        readonly List<SkinDefinitionSO> _bot2Skins = new List<SkinDefinitionSO>();
        readonly List<SkinDefinitionSO> _bot3Skins = new List<SkinDefinitionSO>();
        // Guard — skins are awarded exactly once per battle
        bool _settled;

        readonly System.Collections.Generic.List<CaseBattlePanelRefs> _activePanels =
            new System.Collections.Generic.List<CaseBattlePanelRefs>();
        readonly System.Collections.Generic.List<SkinDefinitionSO> _activeSkins =
            new System.Collections.Generic.List<SkinDefinitionSO>();

        // ─────────────────────────────────────────────────────────────────────
        // LIFECYCLE
        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            _roulette = gameObject.AddComponent<CaseBattleRouletteAnimator>();

            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
        }

        protected override void OnShown()
        {
            GameEvents.OnVpChanged          += HandleVpChanged;
            PlayerProfileData.OnProfileChanged += HandleProfileChanged;

            BuildUiOnce();
            EnterSetup();
        }

        protected override void OnHidden()
        {
            GameEvents.OnVpChanged          -= HandleVpChanged;
            PlayerProfileData.OnProfileChanged -= HandleProfileChanged;

            if (_battleCo != null) { StopCoroutine(_battleCo); _battleCo = null; }
            _session = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ONE-SHOT UI BUILD
        // ─────────────────────────────────────────────────────────────────────
        void BuildUiOnce()
        {
            if (_builtUi) return;
            _builtUi = true;

            var rt = (RectTransform)transform;
            _ui = CaseBattleUiBuilder.Build(rt);

            // Wire buttons
            _ui.StartButton.onClick.AddListener(OnStartClicked);
            _ui.PlayAgainButton.onClick.AddListener(OnPlayAgainClicked);
            _ui.InventoryButton.onClick.AddListener(() =>
            {
                _session = null;
                navigator?.Navigate(ScreenType.Inventory);
            });

            foreach (var (btn, count, _, _) in _ui.CountButtons)
            {
                var captured = count;
                btn.onClick.AddListener(() => SelectCount(captured));
            }

            // ── New flow wiring (Setup / CasePicker / CreateGame) ─────────────
            if (_ui.AddCasesButton != null)
                _ui.AddCasesButton.onClick.AddListener(EnterCasePicker);
            if (_ui.EditCasesButton != null)
                _ui.EditCasesButton.onClick.AddListener(EnterCasePicker);
            if (_ui.DoneButton != null)
                _ui.DoneButton.onClick.AddListener(OnDoneClicked);
            if (_ui.CreateGameButton != null)
                _ui.CreateGameButton.onClick.AddListener(OnCreateGameClicked);
            if (_ui.FinalPopupOkButton != null)
                _ui.FinalPopupOkButton.onClick.AddListener(OnFinalPopupOkClicked);
            if (_ui.FinalPopupPlayAgainButton != null)
                _ui.FinalPopupPlayAgainButton.onClick.AddListener(OnFinalPopupPlayAgainClicked);
            Debug.Log("[CB] create listener bound");

            foreach (var (btn, count, _, _) in _ui.PlayerCountButtons)
            {
                var captured = count;
                btn.onClick.AddListener(() => SelectPlayerCount(captured));
            }

            // Profile widget disabled — TopProfileBar handles settings globally.
            // Avatar click no longer opens a popup; gear button in TopProfileBar
            // is the single Settings entry point across all screens.
            // _profileWidget intentionally left null (field stays declared for compile safety).

            Debug.Log("[CASE BATTLE] Roulette view updated to centered focus layout");
        }

        // ─────────────────────────────────────────────────────────────────────
        // EVENT HANDLERS
        // ─────────────────────────────────────────────────────────────────────
        void HandleVpChanged(int _, int __) => RefreshCostUi();

        void HandleProfileChanged()
        {
            if (_ui == null) return;
            if (_ui.PlayerAvatarImg != null)
            {
                _ui.PlayerAvatarImg.color  = Color.white;
                _ui.PlayerAvatarImg.sprite = PlayerProfileData.Avatar;
            }
            if (_ui.Player.NameLabel != null &&
                (_session == null || _session.State != BattleState.InProgress))
                _ui.Player.NameLabel.text = PlayerProfileData.Username.ToUpper();
        }

        void OnBackClicked()
        {
            if (_session != null && _session.State == BattleState.InProgress) return;
            navigator?.Navigate(ScreenType.MainMenu);
        }

        // ─────────────────────────────────────────────────────────────────────
        // STATE TRANSITIONS
        // ─────────────────────────────────────────────────────────────────────
        void ShowLobby()
        {
            _ui.LobbyOverlay.SetActive(true);
            _ui.ActionBar.SetActive(false);
            _ui.WinnerBadge.SetActive(false);
            if (_ui.RoundLabel != null) _ui.RoundLabel.text = "";

            if (_ui.Player.RouletteAreaObject   != null) _ui.Player.RouletteAreaObject.SetActive(false);
            if (_ui.Opponent.RouletteAreaObject != null) _ui.Opponent.RouletteAreaObject.SetActive(false);

            _roulette.ClearPanel(_ui.Player);
            _roulette.ClearPanel(_ui.Opponent);
            SelectCount(_selectedCount);
            UpdateCaseIcons(_selectedCount);
        }

        void ShowArena()
        {
            _state = BattleScreenState.Battle;
            if (_ui.SetupPanel       != null) _ui.SetupPanel.SetActive(false);
            if (_ui.CasePickerPanel  != null) _ui.CasePickerPanel.SetActive(false);
            if (_ui.ArenaPanel       != null) _ui.ArenaPanel.SetActive(true);
            _ui.LobbyOverlay.SetActive(false);
            _ui.ActionBar.SetActive(false);
            _ui.WinnerBadge.SetActive(false);
            UpdateCaseIcons(_session?.CaseCount ?? 0);

            if (_ui.Player.RouletteAreaObject   != null) _ui.Player.RouletteAreaObject.SetActive(true);
            if (_ui.Opponent.RouletteAreaObject != null) _ui.Opponent.RouletteAreaObject.SetActive(true);
            if (_ui.Bot2.RouletteAreaObject     != null) _ui.Bot2.RouletteAreaObject.SetActive(_selectedPlayerCount >= 3);
            if (_ui.Bot3.RouletteAreaObject     != null) _ui.Bot3.RouletteAreaObject.SetActive(_selectedPlayerCount >= 4);
        }

        void ShowResults()
        {
            Debug.Log("[CB_RESULTS] ShowResults called - do not hide roulette areas");
            _state = BattleScreenState.Results;
            if (_ui.SetupPanel       != null) _ui.SetupPanel.SetActive(false);
            if (_ui.CasePickerPanel  != null) _ui.CasePickerPanel.SetActive(false);
            if (_ui.ArenaPanel       != null) _ui.ArenaPanel.SetActive(true);
            _ui.LobbyOverlay.SetActive(false);
            _ui.ActionBar.SetActive(true);
            _ui.WinnerBadge.SetActive(true);
            if (_ui.RoundLabel != null) _ui.RoundLabel.text = "FINAL";

            // Roulette areas STAY visible — last rolled skin remains highlighted in each panel.

            _ui.Player.VpLabel.color   = AccentGreen;
            _ui.Opponent.VpLabel.color = AccentGreen;

            if (_session == null) return;

            // Compute overall winner across all visible participants
            int playerVp   = _session.Player.TotalVp;
            int bot1Vp     = _session.Opponent.TotalVp;
            int bestVp     = playerVp;
            string bestName = PlayerProfileData.Username.ToUpper();
            int bestCount  = 1;
            void Consider(int vp, string name)
            {
                if (vp > bestVp)      { bestVp = vp; bestName = name; bestCount = 1; }
                else if (vp == bestVp){ bestCount++; }
            }
            Consider(bot1Vp, "BOT 1");
            if (_selectedPlayerCount >= 3) Consider(_bot2TotalVp, "BOT 2");
            if (_selectedPlayerCount >= 4) Consider(_bot3TotalVp, "BOT 3");

            bool playerWonOverall = (playerVp == bestVp) && (bestCount == 1);

            if (bestCount > 1)
            {
                _ui.WinnerNameLabel.text  = "DRAW";
                _ui.WinnerNameLabel.color = TextDim;
            }
            else
            {
                _ui.WinnerNameLabel.text  = bestName;
                _ui.WinnerNameLabel.color = playerWonOverall ? AccentGreen : AccentOrange;
            }

            // ── Final popup ──────────────────────────────────────────────────────
            if (bestCount > 1)
            {
                // DRAW — refund battle cost to the player exactly once
                if (!_drawRefunded)
                {
                    var ctx2 = GameContext.Instance;
                    ctx2?.Vp?.Add(_battleTotalCost);
                    ctx2?.Save?.Save();
                    _drawRefunded = true;
                    Debug.Log($"[CB] DRAW — refunded {_battleTotalCost} VP to player");
                }
                ShowFinalPopup(
                    "BERABERE",
                    $"İade: (G) {FormatGp(_battleTotalCost)}",
                    TextDim,
                    "Bakiye iade edildi");
            }
            else if (playerWonOverall)
            {
                ShowFinalPopup("YOU WIN!", $"Total: {FormatGp(bestVp)}", AccentGreen);
            }
            else
            {
                ShowFinalPopup(bestName + " WINS", $"Total: {FormatGp(bestVp)}", AccentOrange);
            }

            StartCoroutine(_roulette.PulseWinnerBadge(_ui.WinnerBadge));
        }

        // ─────────────────────────────────────────────────────────────────────
        // LOBBY INTERACTIONS
        // ─────────────────────────────────────────────────────────────────────
        void SelectCount(int count)
        {
            _selectedCount = count;
            foreach (var (_, c, bg, ol) in _ui.CountButtons)
            {
                if (bg == null || ol == null) continue;
                bool active    = (c == count);
                bg.color       = active ? new Color(0.18f, 0.06f, 0.20f, 1f) : BgCard;
                ol.effectColor = active ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.10f);
            }
            RefreshCostUi();
            UpdateCaseIcons(count);
        }

        void RefreshCostUi()
        {
            if (_ui == null) return;

            // New flow: in Setup, balance/cost are driven by the multi-case selection.
            if (_state == BattleScreenState.Setup)
            {
                RefreshSetupUi();
                return;
            }

            var ctx     = GameContext.Instance;
            var caseDef = ctx?.Content?.Cases != null && ctx.Content.Cases.Count > 0
                          ? ctx.Content.Cases[0] : null;
            int cost = caseDef != null ? caseDef.VpPrice * _selectedCount : 0;
            int bal  = ctx?.Vp  != null ? ctx.Vp.Balance : 0;

            if (_ui.CostAmountLabel   != null) _ui.CostAmountLabel.text   = FormatGp(cost);
            if (_ui.CaseCountLabel    != null) _ui.CaseCountLabel.text    = _selectedCount.ToString();
            if (_ui.LobbyCostLabel    != null) _ui.LobbyCostLabel.text    = $"Total: {FormatGp(cost)}";
            if (_ui.LobbyBalanceLabel != null) _ui.LobbyBalanceLabel.text = $"Balance: {FormatGp(bal)}";

            bool canAfford = ctx?.Vp != null && ctx.Vp.CanAfford(cost) && caseDef != null;
            if (_ui.StartButton != null)
            {
                _ui.StartButton.interactable = canAfford;
                var img = _ui.StartButton.GetComponent<Image>();
                if (img != null)
                    img.color = canAfford ? AccentPink
                        : new Color(AccentPink.r * 0.32f, AccentPink.g * 0.32f,
                                    AccentPink.b * 0.32f, 0.70f);
            }
        }

        void OnStartClicked()
        {
            var ctx     = GameContext.Instance;
            var caseDef = ctx?.Content?.Cases != null && ctx.Content.Cases.Count > 0
                          ? ctx.Content.Cases[0] : null;
            if (caseDef == null) return;

            // CompositionRoot inject etmediyse kendi sistemini oluştur
            if (_system == null)
            {
                if (ctx?.Vp == null || ctx.Inventory == null ||
                    ctx.CaseOpening == null || ctx.Save == null)
                {
                    Debug.LogWarning("[CaseBattleScreen] GameContext servisleri hazır değil.");
                    return;
                }
                _system = new CaseBattleSystem(ctx.Vp, ctx.Inventory, ctx.CaseOpening, ctx.Save);
            }

            if (!_system.TryStartBattle(caseDef, _selectedCount, out _session))
            {
                GameEvents.RaiseToast("Insufficient VP or battle could not start.");
                return;
            }

            // Override player name with live profile data
            if (_ui.Player.NameLabel   != null)
                _ui.Player.NameLabel.text   = PlayerProfileData.Username.ToUpper();
            if (_ui.Opponent.NameLabel != null)
                _ui.Opponent.NameLabel.text = _session.Opponent.DisplayName.ToUpper();

            _roulette.ClearPanel(_ui.Player);
            _roulette.ClearPanel(_ui.Opponent);

            _ui.Player.VpLabel.text      = FormatGp(0);
            _ui.Opponent.VpLabel.text    = FormatGp(0);
            _ui.Player.VpLabel.color   = AccentGreen;
            _ui.Opponent.VpLabel.color = AccentGreen;

            ShowArena();
            _battleCo = StartCoroutine(RunBattleRounds());
        }

        void OnPlayAgainClicked()
        {
            ResetCaseBattleSetup();
            EnterSetup();
        }

        // Full reset — identical to opening the screen for the first time.
        void ResetCaseBattleSetup()
        {
            _session             = null;
            _selectedCases.Clear();
            _selectedPlayerCount = 2;
            _bot2TotalVp         = 0;
            _bot3TotalVp         = 0;
            _bot2Skins.Clear();
            _bot3Skins.Clear();
            _settled             = false;
            _drawRefunded        = false;
            _battleTotalCost     = 0;
            HideFinalPopup();
            Debug.Log("[CASE BATTLE] Play again — full reset to initial setup state");
        }

        // ── Unified settle (2 / 3 / 4 players) ──────────────────────────────────
        /// <summary>
        /// Determines the real winner across all active participants and awards
        /// every skin that was rolled to the player's inventory if they won.
        /// Guarded by _settled — runs exactly once per battle.
        /// </summary>
        void SettleAndAward()
        {
            if (_settled) return;
            _settled = true;

            if (_session == null) return;
            var ctx = GameContext.Instance;
            if (ctx?.Inventory == null) return;

            // ── Determine winner across ALL active participants ────────────────
            int playerVp = _session.Player.TotalVp;
            int bot1Vp   = _session.Opponent.TotalVp;

            int bestVp    = playerVp;
            int bestCount = 1;                     // how many share the top score

            void Consider(int vp)
            {
                if      (vp > bestVp)  { bestVp = vp; bestCount = 1; }
                else if (vp == bestVp) { bestCount++; }
            }
            Consider(bot1Vp);
            if (_selectedPlayerCount >= 3) Consider(_bot2TotalVp);
            if (_selectedPlayerCount >= 4) Consider(_bot3TotalVp);

            bool playerWon = (playerVp == bestVp) && (bestCount == 1);
            bool isDraw    = (playerVp == bestVp) && (bestCount > 1);

            string winnerName = playerWon ? PlayerProfileData.Username.ToUpper() : "BOT";
            Debug.Log($"[CASE BATTLE] Winner={winnerName} playerWon={playerWon} isDraw={isDraw}");

            if (playerWon)
            {
                // Award every skin from every participant to the player
                int count = 0;
                foreach (var s in _session.Player.WonSkins)  { ctx.Inventory.AddSkin(s, out _); count++; }
                foreach (var s in _session.Opponent.WonSkins){ ctx.Inventory.AddSkin(s, out _); count++; }
                foreach (var s in _bot2Skins)                { ctx.Inventory.AddSkin(s, out _); count++; }
                foreach (var s in _bot3Skins)                { ctx.Inventory.AddSkin(s, out _); count++; }
                Debug.Log($"[CASE BATTLE] Awarding total skins to player: {count}");
            }
            else if (isDraw)
            {
                // Draw: player keeps only their own skins (VP refund handled in ShowResults)
                int count = 0;
                foreach (var s in _session.Player.WonSkins)  { ctx.Inventory.AddSkin(s, out _); count++; }
                Debug.Log($"[CASE BATTLE] DRAW — awarding player's own skins only: {count}");
            }
            // Loss → no skins awarded

            ctx.Save?.Save();
        }

        void OnFinalPopupOkClicked()
        {
            HideFinalPopup();
        }

        void OnFinalPopupPlayAgainClicked()
        {
            HideFinalPopup();
            ResetCaseBattleSetup();
            EnterSetup();
        }

        // ── Final popup helpers ───────────────────────────────────────────────
        // drawBodyText: when non-null the popup shows the extra body line and
        // the "TEKRAR OYNA" button (draw mode).  Pass null for normal win/loss/warning.
        void ShowFinalPopup(string title, string body, Color titleColor,
                            string drawBodyText = null)
        {
            if (_ui?.FinalPopup == null) return;

            if (_ui.FinalPopupTitleLabel != null)
            {
                _ui.FinalPopupTitleLabel.text  = title;
                _ui.FinalPopupTitleLabel.color = titleColor;
            }

            bool isDrawMode = drawBodyText != null;

            // Body line (draw-only)
            if (_ui.FinalPopupBodyLabel != null)
            {
                _ui.FinalPopupBodyLabel.gameObject.SetActive(isDrawMode);
                if (isDrawMode) _ui.FinalPopupBodyLabel.text = drawBodyText;
            }

            if (_ui.FinalPopupTotalLabel != null)
                _ui.FinalPopupTotalLabel.text = body;

            // "TEKRAR OYNA" button — draw only
            if (_ui.FinalPopupPlayAgainButton != null)
                _ui.FinalPopupPlayAgainButton.gameObject.SetActive(isDrawMode);

            if (_ui.FinalPopupCanvasGroup != null)
            {
                _ui.FinalPopupCanvasGroup.alpha          = 1f;
                _ui.FinalPopupCanvasGroup.blocksRaycasts = true;
                _ui.FinalPopupCanvasGroup.interactable   = true;
            }
            _ui.FinalPopup.SetActive(true);
        }

        void HideFinalPopup()
        {
            if (_ui?.FinalPopup == null) return;
            if (_ui.FinalPopupCanvasGroup != null)
            {
                _ui.FinalPopupCanvasGroup.alpha          = 0f;
                _ui.FinalPopupCanvasGroup.blocksRaycasts = false;
                _ui.FinalPopupCanvasGroup.interactable   = false;
            }
            _ui.FinalPopup.SetActive(false);
            Debug.Log("[CB] popup closed, raycast disabled");
        }

        // ─────────────────────────────────────────────────────────────────────
        // BATTLE LOOP
        // ─────────────────────────────────────────────────────────────────────
        IEnumerator RunBattleRounds()
        {
            yield return new WaitForSecondsRealtime(0.2f);
            var skinPool = BuildSkinPool();
            var ctx      = GameContext.Instance;
            var caseDef  = _session?.Case;

            while (_session != null && _session.HasMoreRounds)
            {
                if (!_session.RollNextRound(out var playerSkin, out var opponentSkin))
                    break;

                if (_ui.RoundLabel != null)
                    _ui.RoundLabel.text = $"ROUND {_session.CurrentRound}/{_session.CaseCount}";

                // Roll extra-bot skins from the same case and accumulate into skin lists
                SkinDefinitionSO bot2Skin = null, bot3Skin = null;
                if (_selectedPlayerCount >= 3 && ctx?.CaseOpening != null && caseDef != null)
                {
                    bot2Skin = ctx.CaseOpening.RollSkin(caseDef);
                    if (bot2Skin != null) _bot2Skins.Add(bot2Skin);
                }
                if (_selectedPlayerCount >= 4 && ctx?.CaseOpening != null && caseDef != null)
                {
                    bot3Skin = ctx.CaseOpening.RollSkin(caseDef);
                    if (bot3Skin != null) _bot3Skins.Add(bot3Skin);
                }

                // Build the animation lists based on visible panels
                _activePanels.Clear();
                _activeSkins.Clear();
                _activePanels.Add(_ui.Player);   _activeSkins.Add(playerSkin);
                _activePanels.Add(_ui.Opponent); _activeSkins.Add(opponentSkin);
                if (_selectedPlayerCount >= 3) { _activePanels.Add(_ui.Bot2); _activeSkins.Add(bot2Skin); }
                if (_selectedPlayerCount >= 4) { _activePanels.Add(_ui.Bot3); _activeSkins.Add(bot3Skin); }

                yield return StartCoroutine(_roulette.AnimateRoulettes(
                    _activePanels, _activeSkins, skinPool));

                // Update running totals (Bot2/Bot3 tracked locally)
                if (bot2Skin != null) _bot2TotalVp += bot2Skin.VpValue;
                if (bot3Skin != null) _bot3TotalVp += bot3Skin.VpValue;

                bool playerWon = (playerSkin?.VpValue ?? 0) >= (opponentSkin?.VpValue ?? 0);

                _ui.Player.VpLabel.text    = FormatGp(_session.Player.TotalVp);
                _ui.Opponent.VpLabel.text  = FormatGp(_session.Opponent.TotalVp);
                _ui.Player.VpLabel.color   = playerWon ? AccentGreen : AccentRed;
                _ui.Opponent.VpLabel.color = playerWon ? AccentRed   : AccentGreen;

                if (_ui.Bot2.VpLabel != null) _ui.Bot2.VpLabel.text = FormatGp(_bot2TotalVp);
                if (_ui.Bot3.VpLabel != null) _ui.Bot3.VpLabel.text = FormatGp(_bot3TotalVp);

                if (playerSkin   != null) _roulette.AddHistoryCard(_ui.Player,   playerSkin);
                if (opponentSkin != null) _roulette.AddHistoryCard(_ui.Opponent, opponentSkin);
                if (bot2Skin     != null) _roulette.AddHistoryCard(_ui.Bot2,     bot2Skin);
                if (bot3Skin     != null) _roulette.AddHistoryCard(_ui.Bot3,     bot3Skin);

                yield return new WaitForSecondsRealtime(0.75f);

                _ui.Player.VpLabel.color   = AccentGreen;
                _ui.Opponent.VpLabel.color = AccentGreen;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            SettleAndAward();      // unified settle — handles 2 / 3 / 4 players
            _system?.Abort();      // clear the session tracker (no double-settle risk)
            ShowResults();
            _battleCo = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────
        void UpdateCaseIcons(int count, CaseDefinitionSO overrideCase = null)
        {
            if (_ui?.CaseIconsRow == null) return;
            for (int i = _ui.CaseIconsRow.childCount - 1; i >= 0; i--)
                Destroy(_ui.CaseIconsRow.GetChild(i).gameObject);

            var ctx     = GameContext.Instance;
            var caseDef = overrideCase ?? (ctx?.Content?.Cases != null && ctx.Content.Cases.Count > 0
                          ? ctx.Content.Cases[0] : null);

            int cap = Mathf.Clamp(count, 1, 5);
            for (int i = 0; i < cap; i++)
            {
                var go = new GameObject($"CI{i}",
                    typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.transform.SetParent(_ui.CaseIconsRow, false);
                var le  = go.GetComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 52;
                var img = go.GetComponent<Image>();
                img.sprite         = caseDef?.CaseIcon;
                img.preserveAspect = true;
                img.color          = img.sprite != null ? Color.white : AccentPinkSoft;
            }
        }

        SkinDefinitionSO[] BuildSkinPool()
        {
            var ctx = GameContext.Instance;
            if (ctx?.Content?.Skins == null || ctx.Content.Skins.Count == 0)
                return new SkinDefinitionSO[0];
            var list = new List<SkinDefinitionSO>();
            foreach (var s in ctx.Content.Skins)
                if (s != null) list.Add(s);
            return list.ToArray();
        }

        // ═════════════════════════════════════════════════════════════════════
        // NEW FLOW — Setup / CasePicker / CreateGame
        // ═════════════════════════════════════════════════════════════════════

        void EnterSetup()
        {
            _state = BattleScreenState.Setup;
            if (_ui.SetupPanel       != null) _ui.SetupPanel.SetActive(true);
            HideFinalPopup();  // guarantee overlay never blocks Setup inputs
            Debug.Log("[CB] setup active, finalPopup=" + (_ui.FinalPopup != null && _ui.FinalPopup.activeSelf));
            if (_ui.CasePickerPanel  != null) _ui.CasePickerPanel.SetActive(false);
            if (_ui.ArenaPanel       != null) _ui.ArenaPanel.SetActive(false);
            if (_ui.LobbyOverlay     != null) _ui.LobbyOverlay.SetActive(false);
            if (_ui.ActionBar        != null) _ui.ActionBar.SetActive(false);
            if (_ui.WinnerBadge      != null) _ui.WinnerBadge.SetActive(false);
            if (_ui.RoundLabel       != null) _ui.RoundLabel.text = "";

            if (_ui.Player.RouletteAreaObject   != null) _ui.Player.RouletteAreaObject.SetActive(false);
            if (_ui.Opponent.RouletteAreaObject != null) _ui.Opponent.RouletteAreaObject.SetActive(false);
            if (_ui.Bot2.RouletteAreaObject     != null) _ui.Bot2.RouletteAreaObject.SetActive(false);
            if (_ui.Bot3.RouletteAreaObject     != null) _ui.Bot3.RouletteAreaObject.SetActive(false);

            _roulette.ClearPanel(_ui.Player);
            _roulette.ClearPanel(_ui.Opponent);
            _roulette.ClearPanel(_ui.Bot2);
            _roulette.ClearPanel(_ui.Bot3);

            RefreshSetupUi();
        }

        void EnterCasePicker()
        {
            Debug.Log("[CASE BATTLE] Case picker opened");
            _state = BattleScreenState.CasePicker;
            if (_ui.SetupPanel       != null) _ui.SetupPanel.SetActive(false);
            if (_ui.CasePickerPanel  != null) _ui.CasePickerPanel.SetActive(true);
            RebuildCasePickerGrid();
        }

        void OnDoneClicked()
        {
            Debug.Log("[CASE BATTLE] Case picker closed");
            EnterSetup();
        }

        void SelectPlayerCount(int count)
        {
            bool changed = _selectedPlayerCount != count;
            _selectedPlayerCount = count;
            if (changed)
                Debug.Log("[CASE BATTLE] Player count selected: " + _selectedPlayerCount);
            foreach (var (_, c, bg, ol) in _ui.PlayerCountButtons)
            {
                if (bg == null || ol == null) continue;
                bool active    = (c == count);
                bg.color       = active ? new Color(0.18f, 0.06f, 0.20f, 1f) : BgCard;
                ol.effectColor = active ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.10f);
            }
        }

        void RefreshSetupUi()
        {
            int totalCases = GetTotalSelectedCases();
            int totalCost  = GetTotalSelectedCost();
            var ctx = GameContext.Instance;

            if (_ui.TotalCostLabel  != null) _ui.TotalCostLabel.text  = $"TOTAL: {FormatGp(totalCost)}";
            if (_ui.TotalCasesLabel != null) _ui.TotalCasesLabel.text = $"CASES: {totalCases}";

            int balance    = ctx?.Vp?.Balance ?? 0;
            bool canStart  = totalCases > 0 && totalCost > 0;
            bool canAfford = canStart && balance >= totalCost;
            Debug.Log("[CB] totalCases=" + totalCases + " totalCost=" + totalCost +
                      " balance=" + balance + " canStart=" + canStart);
            if (_ui.CreateGameButton != null)
            {
                // Interactable whenever cases are selected; balance is checked on click.
                _ui.CreateGameButton.interactable = canStart;
                var img = _ui.CreateGameButton.GetComponent<Image>();
                if (img != null)
                    img.color = canAfford  ? AccentPink                                              // bright — ready
                        : canStart         ? new Color(AccentOrange.r * 0.55f,                       // dimmed orange — can't afford
                                                       AccentOrange.g * 0.55f,
                                                       AccentOrange.b * 0.55f, 0.85f)
                        :                    new Color(AccentPink.r   * 0.32f,                       // very dark — nothing selected
                                                       AccentPink.g   * 0.32f,
                                                       AccentPink.b   * 0.32f, 0.70f);
            }

            // Keep top cost strip in sync with the selection
            if (_ui.CostAmountLabel != null) _ui.CostAmountLabel.text = FormatGp(totalCost);
            if (_ui.CaseCountLabel  != null) _ui.CaseCountLabel.text  = totalCases.ToString();
            UpdateCaseIcons(totalCases, GetFirstSelectedCase());

            RebuildSelectedCasesDisplay();
            SelectPlayerCount(_selectedPlayerCount);
        }

        void OnCreateGameClicked()
        {
            Debug.Log("[CB] CREATE FIRED");

            int totalCases = GetTotalSelectedCases();
            if (totalCases <= 0) return;

            var firstCaseDef = GetFirstSelectedCase();
            if (firstCaseDef == null) return;

            // Balance check — show warning popup if insufficient; don't start battle.
            int totalCost = GetTotalSelectedCost();
            int balance   = GameContext.Instance?.Vp?.Balance ?? 0;
            if (balance < totalCost)
            {
                ShowFinalPopup("UYARI", "YETERLİ BAKİYE YOKTUR", AccentOrange);
                RefreshSetupUi(); // re-dim button immediately
                return;
            }

            int botCount = Mathf.Max(1, _selectedPlayerCount - 1);
            Debug.Log("[CASE BATTLE] Creating lobby with " + botCount + " bot(s)");
            if (_selectedPlayerCount > 2)
                Debug.Log("[CASE BATTLE] 3/4 player UI selected but current battle system supports 2 players; falling back to 1 bot for resolve.");

            var ctx = GameContext.Instance;
            if (_system == null)
            {
                if (ctx?.Vp == null || ctx.Inventory == null ||
                    ctx.CaseOpening == null || ctx.Save == null)
                {
                    Debug.LogWarning("[CaseBattleScreen] GameContext servisleri hazır değil.");
                    return;
                }
                _system = new CaseBattleSystem(ctx.Vp, ctx.Inventory, ctx.CaseOpening, ctx.Save);
            }

            if (!_system.TryStartBattle(firstCaseDef, totalCases, out _session))
            {
                ShowFinalPopup("UYARI", "YETERLİ BAKİYE YOKTUR", AccentOrange);
                RefreshSetupUi();
                return;
            }

            // Reset extra-bot tallies and capture cost for potential draw refund
            _bot2TotalVp     = 0;
            _bot3TotalVp     = 0;
            _bot2Skins.Clear();
            _bot3Skins.Clear();
            _settled         = false;
            _drawRefunded    = false;
            _battleTotalCost = totalCost;

            // Apply the N-column layout and configure names
            ApplyColumnLayout(_selectedPlayerCount);

            if (_ui.Player.NameLabel   != null)
                _ui.Player.NameLabel.text   = PlayerProfileData.Username.ToUpper();
            if (_ui.Opponent.NameLabel != null)
                _ui.Opponent.NameLabel.text = "BOT 1";
            if (_ui.Bot2.NameLabel != null) _ui.Bot2.NameLabel.text = "BOT 2";
            if (_ui.Bot3.NameLabel != null) _ui.Bot3.NameLabel.text = "BOT 3";

            _roulette.ClearPanel(_ui.Player);
            _roulette.ClearPanel(_ui.Opponent);
            _roulette.ClearPanel(_ui.Bot2);
            _roulette.ClearPanel(_ui.Bot3);

            _ui.Player.VpLabel.text      = FormatGp(0);
            _ui.Opponent.VpLabel.text    = FormatGp(0);
            if (_ui.Bot2.VpLabel != null) { _ui.Bot2.VpLabel.text = FormatGp(0); _ui.Bot2.VpLabel.color = AccentGreen; }
            if (_ui.Bot3.VpLabel != null) { _ui.Bot3.VpLabel.text = FormatGp(0); _ui.Bot3.VpLabel.color = AccentGreen; }
            _ui.Player.VpLabel.color   = AccentGreen;
            _ui.Opponent.VpLabel.color = AccentGreen;

            ShowArena();
            _battleCo = StartCoroutine(RunBattleRounds());
        }

        // ── Column layout for 2/3/4 players ───────────────────────────────────
        void ApplyColumnLayout(int playerCount)
        {
            bool showBot2 = playerCount >= 3;
            bool showBot3 = playerCount >= 4;
            if (_ui.Bot2.ColumnRect != null) _ui.Bot2.ColumnRect.gameObject.SetActive(showBot2);
            if (_ui.Bot3.ColumnRect != null) _ui.Bot3.ColumnRect.gameObject.SetActive(showBot3);

            if (_ui.VsBadge != null) _ui.VsBadge.SetActive(playerCount == 2);

            float rouletteScale = playerCount switch
            {
                3 => 0.78f,
                4 => 0.62f,
                _ => 1.00f,
            };
            ScaleRoulette(_ui.Player,   rouletteScale);
            ScaleRoulette(_ui.Opponent, rouletteScale);
            ScaleRoulette(_ui.Bot2,     rouletteScale);
            ScaleRoulette(_ui.Bot3,     rouletteScale);

            // Equal column fractions — Player=0, Opponent=1, Bot2=2, Bot3=3
            float colW = 1f / playerCount;
            CaseBattlePanelRefs[] panelArr =
                { _ui.Player, _ui.Opponent, _ui.Bot2, _ui.Bot3 };

            for (int i = 0; i < playerCount; i++)
            {
                float minX = i       * colW;
                float maxX = (i + 1) * colW;
                Debug.Log($"[CB_LAYOUT] playerCount={playerCount} index={i} minX={minX:F3} maxX={maxX:F3}");
                SetColAnchors(panelArr[i].ColumnRect, minX, maxX);
                ReconfigureHistoryGrid(panelArr[i], playerCount, i);
            }
        }

        /// <summary>Updates the history GridLayoutGroup cell-size and column count
        /// to fit the column width for the current player count.</summary>
        static void ReconfigureHistoryGrid(CaseBattlePanelRefs refs, int playerCount, int panelIndex)
        {
            if (refs?.GridRoot == null) return;
            var glg = refs.GridRoot.GetComponent<GridLayoutGroup>();
            if (glg == null) return;

            float hcW, hcH;
            switch (playerCount)
            {
                case 4:  hcW = 62f;  hcH = 72f;  break;
                case 3:  hcW = 84f;  hcH = 98f;  break;
                default: hcW = 104f; hcH = 122f; break;
            }

            glg.cellSize        = new Vector2(hcW, hcH);
            glg.spacing         = new Vector2(5f,  5f);
            glg.padding         = new RectOffset(4, 4, 4, 4);
            glg.constraintCount = 2;
            Debug.Log($"[CB_HISTORY] playerCount={playerCount} panel={panelIndex} cardW={hcW} cardH={hcH}");
        }

        static void SetColAnchors(RectTransform col, float aMinX, float aMaxX)
        {
            if (col == null) return;
            col.anchorMin = new Vector2(aMinX, 0f);
            col.anchorMax = new Vector2(aMaxX, 1f);
            col.offsetMin = Vector2.zero;
            col.offsetMax = Vector2.zero;
        }

        static void ScaleRoulette(CaseBattlePanelRefs refs, float scale)
        {
            if (refs?.RouletteAreaObject == null) return;
            refs.RouletteAreaObject.transform.localScale = Vector3.one * scale;
        }

        // ── Selection helpers ─────────────────────────────────────────────────
        int GetTotalSelectedCases()
        {
            int n = 0;
            foreach (var kv in _selectedCases) n += kv.Value;
            return n;
        }

        int GetTotalSelectedCost()
        {
            int c = 0;
            foreach (var kv in _selectedCases)
                if (kv.Key != null) c += kv.Key.VpPrice * kv.Value;
            return c;
        }

        CaseDefinitionSO GetFirstSelectedCase()
        {
            foreach (var kv in _selectedCases)
                if (kv.Value > 0 && kv.Key != null) return kv.Key;
            return null;
        }

        // ── Case picker grid (runtime build) ──────────────────────────────────
        void RebuildCasePickerGrid()
        {
            if (_ui.CasePickerGridRoot == null) return;
            for (int i = _ui.CasePickerGridRoot.childCount - 1; i >= 0; i--)
                Destroy(_ui.CasePickerGridRoot.GetChild(i).gameObject);

            var ctx = GameContext.Instance;
            if (ctx?.Content?.Cases == null) return;
            foreach (var caseDef in ctx.Content.Cases)
            {
                if (caseDef == null) continue;
                BuildPickerCard(_ui.CasePickerGridRoot, caseDef);
            }
        }

        void BuildPickerCard(Transform parent, CaseDefinitionSO caseDef)
        {
            int currentQty = _selectedCases.TryGetValue(caseDef, out var q) ? q : 0;

            var card = new GameObject($"Pick_{caseDef.CaseId}",
                typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            var cardImg = card.GetComponent<Image>();
            cardImg.color = BgCard;
            var cardOl = card.AddComponent<Outline>();
            cardOl.effectColor    = currentQty > 0 ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.15f);
            cardOl.effectDistance = new Vector2(2f, -2f);
            var rt = (RectTransform)card.transform;

            // Icon
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rt, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = new Vector2(0.5f, 1); iconRt.anchorMax = new Vector2(0.5f, 1);
            iconRt.pivot = new Vector2(0.5f, 1);
            iconRt.anchoredPosition = new Vector2(0, -10);
            iconRt.sizeDelta = new Vector2(96, 96);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite        = caseDef.CaseIcon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget  = false;
            if (iconImg.sprite == null) iconImg.color = AccentPinkSoft;

            // Name
            string displayName = string.IsNullOrEmpty(caseDef.DisplayName)
                ? caseDef.name : caseDef.DisplayName;
            var nameTmp = UIFactory.CreateText(rt, "Name", displayName.ToUpper(),
                12, TMPro.TextAlignmentOptions.Center, TextWhite, TMPro.FontStyles.Bold);
            var nrt = nameTmp.rectTransform;
            nrt.anchorMin = new Vector2(0, 1); nrt.anchorMax = new Vector2(1, 1);
            nrt.pivot = new Vector2(0.5f, 1);
            nrt.anchoredPosition = new Vector2(0, -114);
            nrt.sizeDelta = new Vector2(0, 18);

            // Price
            var priceTmp = UIFactory.CreateText(rt, "Price", FormatGp(caseDef.VpPrice),
                13, TMPro.TextAlignmentOptions.Center, AccentGreen, TMPro.FontStyles.Bold);
            var prt = priceTmp.rectTransform;
            prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(1, 1);
            prt.pivot = new Vector2(0.5f, 1);
            prt.anchoredPosition = new Vector2(0, -134);
            prt.sizeDelta = new Vector2(0, 20);

            // +/- row + qty label
            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(rt, false);
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.anchorMin = new Vector2(0.5f, 0); rowRt.anchorMax = new Vector2(0.5f, 0);
            rowRt.pivot = new Vector2(0.5f, 0);
            rowRt.anchoredPosition = new Vector2(0, 10);
            rowRt.sizeDelta = new Vector2(150, 36);
            var hl = rowGo.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.spacing = 6f;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;

            var minusBtn = CaseBattleUiBuilder.MakeCasinoBtn(rowGo.transform,
                "Minus", "-", 40, 34, BgCardDark, AccentPink);
            var qtyTmp = UIFactory.CreateText(rowGo.transform, "Qty",
                currentQty.ToString(), 16, TMPro.TextAlignmentOptions.Center,
                TextWhite, TMPro.FontStyles.Bold);
            var qLE = qtyTmp.gameObject.AddComponent<LayoutElement>();
            qLE.minWidth  = qLE.preferredWidth  = 34f;
            qLE.minHeight = qLE.preferredHeight = 34f;
            var plusBtn  = CaseBattleUiBuilder.MakeCasinoBtn(rowGo.transform,
                "Plus", "+", 40, 34, BgCardDark, AccentPink);

            var capCase = caseDef;
            var capQty  = qtyTmp;
            var capOl   = cardOl;
            minusBtn.onClick.AddListener(() => OnQuantityChanged(capCase, -1, capQty, capOl));
            plusBtn .onClick.AddListener(() => OnQuantityChanged(capCase, +1, capQty, capOl));
        }

        void OnQuantityChanged(CaseDefinitionSO caseDef, int delta,
            TextMeshProUGUI qtyLbl, Outline cardOl)
        {
            int current = _selectedCases.TryGetValue(caseDef, out var q) ? q : 0;
            int next    = Mathf.Max(0, current + delta);
            if (next == 0) _selectedCases.Remove(caseDef);
            else           _selectedCases[caseDef] = next;

            if (qtyLbl != null) qtyLbl.text = next.ToString();
            if (cardOl != null)
                cardOl.effectColor = next > 0 ? AccentPink : new Color(1f, 0.18f, 0.55f, 0.15f);

            int totalCases = GetTotalSelectedCases();
            int totalCost  = GetTotalSelectedCost();
            Debug.Log($"[CASE BATTLE] Case quantity changed after play again: totalCases={totalCases}, totalCost={totalCost}");
            RefreshSetupUi(); // keep button state live while in picker
        }

        // ── Selected-cases summary (shown on Setup panel) ─────────────────────
        void RebuildSelectedCasesDisplay()
        {
            if (_ui.SelectedCasesRoot == null) return;
            for (int i = _ui.SelectedCasesRoot.childCount - 1; i >= 0; i--)
                Destroy(_ui.SelectedCasesRoot.GetChild(i).gameObject);

            bool anySelected = false;
            foreach (var kv in _selectedCases)
                if (kv.Key != null && kv.Value > 0) { anySelected = true; break; }

            // Show the big ADD CASES card only when empty; otherwise show summary
            if (_ui.AddCasesButton != null)
                _ui.AddCasesButton.gameObject.SetActive(!anySelected);

            if (!anySelected) return;

            foreach (var kv in _selectedCases)
            {
                if (kv.Key == null || kv.Value <= 0) continue;
                BuildSelectedCaseChip(_ui.SelectedCasesRoot, kv.Key, kv.Value);
            }
        }

        void BuildSelectedCaseChip(Transform parent, CaseDefinitionSO caseDef, int qty)
        {
            var chip = new GameObject($"Sel_{caseDef.CaseId}",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            chip.transform.SetParent(parent, false);
            var le = chip.GetComponent<LayoutElement>();
            le.minWidth  = le.preferredWidth  = 110f;
            le.minHeight = le.preferredHeight = 150f;
            chip.GetComponent<Image>().color = BgCard;
            var ol = chip.AddComponent<Outline>();
            ol.effectColor    = AccentPink;
            ol.effectDistance = new Vector2(2f, -2f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(chip.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = new Vector2(0.5f, 1); iconRt.anchorMax = new Vector2(0.5f, 1);
            iconRt.pivot = new Vector2(0.5f, 1);
            iconRt.anchoredPosition = new Vector2(0, -8);
            iconRt.sizeDelta = new Vector2(78, 78);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite        = caseDef.CaseIcon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget  = false;
            if (iconImg.sprite == null) iconImg.color = AccentPinkSoft;

            string displayName = string.IsNullOrEmpty(caseDef.DisplayName)
                ? caseDef.name : caseDef.DisplayName;
            var nameTmp = UIFactory.CreateText(chip.transform, "Name",
                displayName.ToUpper(), 9, TMPro.TextAlignmentOptions.Center,
                TextWhite, TMPro.FontStyles.Bold);
            var nrt = nameTmp.rectTransform;
            nrt.anchorMin = new Vector2(0, 0); nrt.anchorMax = new Vector2(1, 0);
            nrt.pivot = new Vector2(0.5f, 0);
            nrt.anchoredPosition = new Vector2(0, 28);
            nrt.sizeDelta = new Vector2(0, 14);

            var qtyTmp = UIFactory.CreateText(chip.transform, "Qty", $"x{qty}",
                13, TMPro.TextAlignmentOptions.Center, AccentGreen, TMPro.FontStyles.Bold);
            var qrt = qtyTmp.rectTransform;
            qrt.anchorMin = new Vector2(0, 0); qrt.anchorMax = new Vector2(1, 0);
            qrt.pivot = new Vector2(0.5f, 0);
            qrt.anchoredPosition = new Vector2(0, 8);
            qrt.sizeDelta = new Vector2(0, 18);
        }
    }
}
