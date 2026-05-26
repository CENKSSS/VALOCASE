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
            ShowLobby();
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

            // Profile widget — top-right button + edit popup
            _profileWidget = gameObject.AddComponent<PlayerProfileWidget>();
            _profileWidget.Initialise(rt, _ui.TopBarRect);
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

            _roulette.ClearPanel(_ui.Player);
            _roulette.ClearPanel(_ui.Opponent);
            SelectCount(_selectedCount);
            UpdateCaseIcons(_selectedCount);
        }

        void ShowArena()
        {
            _ui.LobbyOverlay.SetActive(false);
            _ui.ActionBar.SetActive(false);
            _ui.WinnerBadge.SetActive(false);
            UpdateCaseIcons(_session?.CaseCount ?? 0);
        }

        void ShowResults()
        {
            _ui.LobbyOverlay.SetActive(false);
            _ui.ActionBar.SetActive(true);
            _ui.WinnerBadge.SetActive(true);
            if (_ui.RoundLabel != null) _ui.RoundLabel.text = "FINAL";

            _ui.Player.VpLabel.color   = AccentGreen;
            _ui.Opponent.VpLabel.color = AccentGreen;

            if (_session == null) return;

            switch (_session.Outcome)
            {
                case BattleOutcome.PlayerWins:
                    _ui.WinnerNameLabel.text          = _session.Player.DisplayName.ToUpper();
                    _ui.WinnerNameLabel.color         = AccentGreen;
                    _ui.PlayerTotalLabel.color        = AccentGreen;
                    _ui.OpponentTotalLabel.color      = TextDim;
                    break;

                case BattleOutcome.OpponentWins:
                    _ui.WinnerNameLabel.text          = _session.Opponent.DisplayName.ToUpper();
                    _ui.WinnerNameLabel.color         = AccentOrange;
                    _ui.PlayerTotalLabel.color        = TextDim;
                    _ui.OpponentTotalLabel.color      = AccentGreen;
                    break;

                case BattleOutcome.Tie:
                    _ui.WinnerNameLabel.text          = "DRAW";
                    _ui.WinnerNameLabel.color         = TextDim;
                    _ui.PlayerTotalLabel.color        = AccentGreen;
                    _ui.OpponentTotalLabel.color      = AccentGreen;
                    break;
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
            _ui.Player.VpLabel.color     = AccentGreen;
            _ui.Opponent.VpLabel.color   = AccentGreen;
            _ui.PlayerTotalLabel.text    = FormatGp(0);
            _ui.OpponentTotalLabel.text  = FormatGp(0);
            _ui.PlayerTotalLabel.color   = AccentGreen;
            _ui.OpponentTotalLabel.color = AccentGreen;

            ShowArena();
            _battleCo = StartCoroutine(RunBattleRounds());
        }

        void OnPlayAgainClicked()
        {
            _session = null;
            ShowLobby();
        }

        // ─────────────────────────────────────────────────────────────────────
        // BATTLE LOOP
        // ─────────────────────────────────────────────────────────────────────
        IEnumerator RunBattleRounds()
        {
            yield return new WaitForSecondsRealtime(0.2f);
            var skinPool = BuildSkinPool();

            while (_session != null && _session.HasMoreRounds)
            {
                if (!_session.RollNextRound(out var playerSkin, out var opponentSkin))
                    break;

                if (_ui.RoundLabel != null)
                    _ui.RoundLabel.text = $"ROUND {_session.CurrentRound}/{_session.CaseCount}";

                yield return StartCoroutine(_roulette.AnimateRoulettePair(
                    _ui.Player, _ui.Opponent, playerSkin, opponentSkin, skinPool));

                bool playerWon = (playerSkin?.VpValue ?? 0) >= (opponentSkin?.VpValue ?? 0);

                _ui.Player.VpLabel.text    = FormatGp(_session.Player.TotalVp);
                _ui.Opponent.VpLabel.text  = FormatGp(_session.Opponent.TotalVp);
                _ui.Player.VpLabel.color   = playerWon ? AccentGreen : AccentRed;
                _ui.Opponent.VpLabel.color = playerWon ? AccentRed   : AccentGreen;

                _ui.PlayerTotalLabel.text   = FormatGp(_session.Player.TotalVp);
                _ui.OpponentTotalLabel.text = FormatGp(_session.Opponent.TotalVp);

                if (playerSkin   != null) _roulette.AddHistoryCard(_ui.Player,   playerSkin);
                if (opponentSkin != null) _roulette.AddHistoryCard(_ui.Opponent, opponentSkin);

                yield return new WaitForSecondsRealtime(0.75f);

                _ui.Player.VpLabel.color   = AccentGreen;
                _ui.Opponent.VpLabel.color = AccentGreen;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            _system?.SettleActiveSession();
            ShowResults();
            _battleCo = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────
        void UpdateCaseIcons(int count)
        {
            if (_ui?.CaseIconsRow == null) return;
            for (int i = _ui.CaseIconsRow.childCount - 1; i >= 0; i--)
                Destroy(_ui.CaseIconsRow.GetChild(i).gameObject);

            var ctx     = GameContext.Instance;
            var caseDef = ctx?.Content?.Cases != null && ctx.Content.Cases.Count > 0
                          ? ctx.Content.Cases[0] : null;

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
    }
}
