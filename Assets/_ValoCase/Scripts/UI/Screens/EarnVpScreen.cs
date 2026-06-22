using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Services.Ads;
using ValoCase.Services.Backend;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// "EARN VP" screen — tap-driven VP Reactor minigame.
    ///
    /// ── GAMEPLAY ─────────────────────────────────────────────────────────────
    ///   Reward      = 1.6 VP × multiplier, exact — the fractional part is
    ///               carried between taps because the wallet stores int VP
    ///   Multiplier  starts at 1.00×, +0.02× per tap, capped at 3.00×
    ///   Decay       drains continuously toward 1.00× — no grace period — and
    ///               ramps up in tiers the longer the player stays idle
    ///   Critical    5% chance per tap → reward ×2
    ///   Combo       consecutive-tap counter, visual only (resets after 3 s idle)
    ///
    /// ── UI BUILDS ITSELF ─────────────────────────────────────────────────────
    /// The editor builder only lays down the chrome (back button, wallet label,
    /// navigator ref). All gameplay UI is constructed in BuildUiOnce() via code.
    /// All per-tap visuals (floating text, particles) are pooled and driven from
    /// a single Update — no per-tap allocations, no coroutines.
    /// </summary>
    public sealed class EarnVpScreen : UIScreenBase
    {
        // ── Inspector refs (wired by builder — names must not change) ─────────
        [SerializeField] UINavigator     navigator;
        [SerializeField] Button          backButton;
        [SerializeField] TextMeshProUGUI walletLabel;  // legacy top-bar VP (hidden, kept in sync)

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color BgDeep     = new Color(0.020f, 0.027f, 0.039f, 1f); // #05070A
        static readonly Color BgMid      = new Color(0.031f, 0.043f, 0.078f, 1f); // #080B14
        static readonly Color BgCard     = new Color(0.051f, 0.067f, 0.090f, 1f); // #0D1117
        static readonly Color Accent     = new Color(1.000f, 0.275f, 0.333f, 1f); // #FF4655
        static readonly Color TextMain   = new Color(0.961f, 0.961f, 0.961f, 1f); // #F5F5F5
        static readonly Color TextSub    = new Color(0.541f, 0.569f, 0.651f, 1f); // #8A91A6
        static readonly Color CritGold   = new Color(1.000f, 0.823f, 0.290f, 1f);
        static readonly Color TierOrange = new Color(1.000f, 0.624f, 0.180f, 1f);
        static readonly Color TierGreen  = new Color(0.290f, 0.890f, 0.545f, 1f);

        // ── Economy constants ─────────────────────────────────────────────────
        const float BaseReward      = 1.6f;
        const float MultStep        = 0.02f;
        const float MultMax         = 3.0f;
        const float ComboResetDelay = 3.0f;   // seconds of inactivity before the combo counter resets (visual only)
        const float CritChance      = 0.05f;

        // Progressive decay — drain rate ramps up the longer the player is idle.
        // The slow tier runs between taps too, so it must stay below the gain
        // rate of normal tapping (~0.06-0.10/s at 3-5 taps per second).
        const float DecaySlow     = 0.05f;    // 0.0-1.0 s idle
        const float DecayFast     = 0.325f;   // 1.0-1.75 s idle
        const float DecayVeryFast = 0.78f;    // 1.75 s+ idle
        const float FastAfter     = 1.0f;
        const float VeryFastAfter = 1.75f;

        static readonly float[] Milestones = { 1.5f, 2f, 2.5f, 3f };

        // ── Runtime state ─────────────────────────────────────────────────────
        bool       _builtUi;
        ScreenType _prevScreen;
        float      _multiplier = 1f;
        float      _lastTapTime;
        int        _combo;
        bool       _comboActive;
        bool       _atMax;
        float      _fracCarry;             // sub-1 VP not yet credited to the int wallet
        int        _shownMultCents = -1;   // label refresh throttle

        // Backend Earn VP session: server is authoritative for granted VP.
        const float ClaimIdleSeconds     = 1f;
        const float ClaimSafetySeconds   = 20f;
        const float RetryCooldownSeconds = 3f;
        const int   MaxClaimRetries      = 3;
        int    _pendingTaps;
        long   _sessionStartMs;
        float  _pendingEstimateVp;
        readonly List<int> _pendingOffsetsMs = new List<int>(64);
        string _sessionId;          // backend-issued id for the current accumulating batch
        bool   _sessionStarting;
        float  _claimTimer;
        float  _retryCooldown;
        bool   _claimInFlight;
        bool   _hasInflight;
        int    _inflightTaps;
        long   _inflightDurationMs;
        string _inflightSessionId;
        int[]  _inflightOffsetsMs;
        float  _inflightEstimateVp;
        int    _inflightRetries;

        // Animation timers (Update-driven, no coroutines)
        float _punchT      = 99f;
        float _glowKick;                   // extra glow alpha, relaxes to 0
        float _flashT      = 99f;
        float _walletPunchT = 99f;
        float _tipTimer;
        int   _tipIndex;

        // ── UI refs ───────────────────────────────────────────────────────────
        TextMeshProUGUI _balanceLabel;
        TextMeshProUGUI _multiplierLabel;
        TextMeshProUGUI _maxLabel;
        TextMeshProUGUI _comboLabel;
        TextMeshProUGUI _flashLabel;
        TextMeshProUGUI _tipLabel;
        Image           _glowImg;
        Image           _barFill;
        RectTransform   _coreVisual;       // punched on tap
        RectTransform   _ringSlow;
        RectTransform   _ringFast;
        RectTransform   _glowRt;
        RectTransform   _floatRoot;
        RectTransform   _particleRoot;
        RectTransform   _balanceRt;
        TextMeshProUGUI _walletCaption;
        TextMeshProUGUI _flyLabel;
        RectTransform   _flyRt;
        bool    _flyActive;
        float   _flyT;
        Vector2 _flyStart, _flyTarget;

        // ── Rewarded ad bonus (2x VP) ──────────────────────────────────────────
        RectTransform   _adBlock;
        Button          _adButton;
        Image           _adButtonImg;
        TextMeshProUGUI _adTitleLabel;
        TextMeshProUGUI _adSubLabel;
        bool            _adClaimInFlight;
        bool            _adAvailable = true;
        bool            _adActive;
        string          _adUnavailableReason;
        float           _adCooldownRemaining;
        float           _adCountdownTick;
        float           _adStatusRetryTick;
        string          _adEarnSessionId;
        bool            _clearAdOnHidden;
        bool            _clearAdPending;

        static readonly Color AdGold    = new Color(1.000f, 0.823f, 0.290f, 1f);

        static readonly string[] Tips =
        {
            "Keep tapping to increase your multiplier.",
            "The multiplier starts dropping as soon as you stop tapping.",
            "Critical taps grant double VP.",
        };

        // ── Pools ─────────────────────────────────────────────────────────────
        const int FloatPoolSize    = 14;
        const int ParticlePoolSize = 24;
        const int ParticlesPerTap  = 6;

        struct FloatEntry
        {
            public RectTransform rt;
            public TextMeshProUGUI tmp;
            public float t, dur;
            public Vector2 start;
            public Color color;
            public bool active;
        }

        struct ParticleEntry
        {
            public RectTransform rt;
            public Image img;
            public float t, dur;
            public Vector2 start, dir;
            public bool active;
        }

        readonly FloatEntry[]    _floats    = new FloatEntry[FloatPoolSize];
        readonly ParticleEntry[] _particles = new ParticleEntry[ParticlePoolSize];

        // ═════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════════

        void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
        }

        void OnBackClicked()
        {
            _clearAdOnHidden = true;
            var target = (_prevScreen != ScreenType.EarnVp &&
                          _prevScreen != ScreenType.Settings)
                ? _prevScreen
                : ScreenType.Tools;
            navigator?.Navigate(target);
        }

        protected override void OnShown()
        {
            _prevScreen = navigator?.PreviousScreen ?? ScreenType.Tools;
            BuildUiOnce();

            _multiplier  = 1f;
            _combo       = 0;
            _comboActive = false;
            _fracCarry   = 0f;
            _lastTapTime = -999f;
            _tipTimer    = 0f;
            _tipIndex    = 0;

            RefreshBalance();
            RefreshMultiplierUi(force: true);
            RefreshComboLabel();
            if (_tipLabel != null) _tipLabel.text = Tips[0];

            _claimTimer    = 0f;
            _retryCooldown = 0f;
            _pendingTaps   = 0;
            _pendingOffsetsMs.Clear();
            _pendingEstimateVp = 0f;
            _sessionId       = null;
            _sessionStarting = false;
            _flyActive     = false;
            if (_flyLabel != null) _flyLabel.gameObject.SetActive(false);
            RefreshPendingLabel();

            _adClaimInFlight = false;
            _adActive = false;
            _adAvailable = true;
            _adUnavailableReason = null;
            _adCooldownRemaining = 0f;
            _adCountdownTick = 0f;
            _adStatusRetryTick = 0f;
            _adEarnSessionId = null;
            _clearAdOnHidden = false;
            _clearAdPending = false;
            RefreshAdBonusBlock();
            RefreshAdBonusStatus();

            GameEvents.OnVpChanged += HandleVpChanged;
        }

        protected override void OnHidden()
        {
            GameEvents.OnVpChanged -= HandleVpChanged;

            TryFlushClaim();
            if (_clearAdOnHidden)
            {
                _clearAdPending = true;
                TryClearEarnVp2xAfterClaims();
            }

            // Kill transient visuals so nothing lingers on next show
            for (int i = 0; i < _floats.Length; i++)
                if (_floats[i].active) { _floats[i].active = false; _floats[i].rt.gameObject.SetActive(false); }
            for (int i = 0; i < _particles.Length; i++)
                if (_particles[i].active) { _particles[i].active = false; _particles[i].rt.gameObject.SetActive(false); }
            _punchT = _flashT = _walletPunchT = 99f;
            _glowKick = 0f;
            _flyActive = false;
            if (_flyLabel != null) _flyLabel.gameObject.SetActive(false);
        }

        void HandleVpChanged(int _, int __)
        {
            RefreshBalance();
            if (!IsBackendEarnVp()) _walletPunchT = 0f;
        }

        static bool IsBackendEarnVp()
        {
            var ctx = GameContext.Instance;
            return ctx != null && !ctx.CanUseLocalEconomy;
        }

        void RefreshBalance()
        {
            int bal = GameContext.Instance?.Vp?.Balance ?? 0;
            if (walletLabel != null)
                walletLabel.text = $"{bal:N0} VP";

            if (IsBackendEarnVp())
            {
                RefreshPendingLabel();
                return;
            }

            if (_walletCaption != null) _walletCaption.text = "WALLET";
            if (_balanceLabel != null)
                _balanceLabel.text = $"{bal:N0} <color=#FF4655>VP</color>";
        }

        // ═════════════════════════════════════════════════════════════════════
        // TAP MECHANICS
        // ═════════════════════════════════════════════════════════════════════

        void OnTap()
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;

            float prevMult = _multiplier;

            float rewardVp = BaseReward * _multiplier;
            if (_adActive) rewardVp *= 2f;
            bool crit = Random.value < CritChance;
            if (crit) rewardVp *= 2f;

            if (ctx.CanUseLocalEconomy)
            {
                // The wallet only stores whole VP; carry the fraction to the next tap.
                _fracCarry += rewardVp;
                int grant = Mathf.FloorToInt(_fracCarry);
                _fracCarry -= grant;
                if (grant > 0)
                    ctx.Economy?.GrantReward(grant, "earn_vp", save: false);
            }
            else
            {
                // Backend-required: record tap timing only; the server grants VP on claim.
                if (_pendingTaps == 0)
                {
                    _sessionStartMs = NowMs();
                    EnsureSessionStarted();
                }
                long offset = NowMs() - _sessionStartMs;
                if (offset < 0) offset = 0;
                _pendingOffsetsMs.Add((int)offset);
                _pendingTaps++;
                _pendingEstimateVp += rewardVp;
                RefreshPendingLabel();
            }

            _combo++;
            _comboActive = true;
            _multiplier  = Mathf.Min(_multiplier + MultStep, MultMax);
            _lastTapTime = Time.time;

            // ── Feedback ──────────────────────────────────────────────────────
            _punchT   = 0f;
            _glowKick = crit ? 0.45f : 0.30f;

            SpawnFloat($"+{FormatVpEstimate(rewardVp)} VP", crit ? CritGold : TextMain, crit ? 1.25f : 1f);
            if (crit) SpawnFloat("CRITICAL!", CritGold, 1.45f);
            SpawnParticles(crit ? CritGold : Accent);

            // Milestone flash when crossing 2× / 3× / 5× / 10×
            for (int i = 0; i < Milestones.Length; i++)
                if (prevMult < Milestones[i] && _multiplier >= Milestones[i])
                {
                    ShowMilestoneFlash(Milestones[i]);
                    break;
                }

            RefreshMultiplierUi();
            RefreshComboLabel();
        }

        static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Starts one server-authorized session per accumulating batch. The backend-issued
        // sessionId becomes the claim's clientSessionId; without it no claim is sent.
        void EnsureSessionStarted()
        {
            if (!string.IsNullOrEmpty(_sessionId) || _sessionStarting) return;
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.CanUseLocalEconomy) return;

            _sessionStarting = true;
            ctx.StartEarnVpSessionBackend(
                res =>
                {
                    if (this == null) return;
                    _sessionStarting = false;
                    _sessionId = res != null ? res.sessionId : null;
                },
                msg =>
                {
                    if (this == null) return;
                    _sessionStarting = false;
                    ResetPendingBatch();
                    if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
                });
        }

        void ResetPendingBatch()
        {
            _pendingTaps = 0;
            _pendingOffsetsMs.Clear();
            _pendingEstimateVp = 0f;
            _sessionId  = null;
            _claimTimer = 0f;
            RefreshPendingLabel();
        }

        // Sends one backend claim at a time using the server-issued sessionId. Transient
        // failures retry a few times; a dead/expired session is dropped rather than retried
        // forever. A flushed batch frees the id so the next batch starts a fresh session.
        void TryFlushClaim()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.CanUseLocalEconomy) return;
            if (_claimInFlight) return;

            if (!_hasInflight)
            {
                if (_pendingTaps <= 0) return;
                if (string.IsNullOrEmpty(_sessionId)) return;

                long dur = NowMs() - _sessionStartMs;
                if (dur < 0) dur = 0;
                if (dur > 240000) dur = 240000;
                _inflightTaps       = _pendingTaps;
                _inflightDurationMs = dur;
                _inflightOffsetsMs  = _pendingOffsetsMs.ToArray();
                _inflightEstimateVp = _pendingEstimateVp;
                _inflightSessionId  = _sessionId;
                _inflightRetries    = 0;
                _pendingTaps        = 0;
                _pendingOffsetsMs.Clear();
                _pendingEstimateVp  = 0f;
                _sessionId          = null;
                _hasInflight        = true;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EarnVp] TryFlushClaim sending — taps={_inflightTaps} durMs={_inflightDurationMs} offsets={(_inflightOffsetsMs != null ? _inflightOffsetsMs.Length : 0)} sessionId={_inflightSessionId}");
#endif
            _claimInFlight = true;
            ctx.ClaimEarnVpSession(_inflightTaps, _inflightDurationMs, _inflightSessionId, _inflightOffsetsMs,
                res =>
                {
                    if (this == null) return;
                    _claimInFlight = false;
                    _hasInflight   = false;
                    _inflightSessionId  = null;
                    _inflightEstimateVp = 0f;
                    int granted = res != null ? res.vpGranted : 0;
                    if (granted > 0) StartFly(granted);
                    RefreshPendingLabel();
                    RefreshBalance();
                    TryClearEarnVp2xAfterClaims();
                },
                msg =>
                {
                    if (this == null) return;
                    _claimInFlight = false;
                    _inflightRetries++;
                    if (_inflightRetries >= MaxClaimRetries)
                    {
                        _hasInflight        = false;
                        _inflightSessionId  = null;
                        _inflightEstimateVp = 0f;
                    }
                    else
                    {
                        _retryCooldown = RetryCooldownSeconds;
                    }
                    RefreshPendingLabel();
                    if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
                    TryClearEarnVp2xAfterClaims();
                });
        }

        string CurrentEarnSessionId()
        {
            if (!string.IsNullOrEmpty(_sessionId)) return _sessionId;
            if (!string.IsNullOrEmpty(_inflightSessionId)) return _inflightSessionId;
            return _adEarnSessionId;
        }

        void TryClearEarnVp2xAfterClaims()
        {
            if (!_clearAdPending || _claimInFlight || _hasInflight || _pendingTaps > 0) return;
            var id = CurrentEarnSessionId();
            if (!string.IsNullOrEmpty(id))
                GameContext.Instance?.ClearEarnVp2xBackend(id);
            _clearAdPending = false;
            _clearAdOnHidden = false;
            _adActive = false;
            _adEarnSessionId = null;
        }

        void UpdateBackendClaim(float dt)
        {
            if (_hasInflight && !_claimInFlight)
            {
                _retryCooldown -= dt;
                if (_retryCooldown <= 0f) TryFlushClaim();
                return;
            }
            if (_claimInFlight) return;
            if (_pendingTaps <= 0) { _claimTimer = 0f; return; }

            _claimTimer += dt;
            float idle = Time.time - _lastTapTime;
            if (idle >= ClaimIdleSeconds || _multiplier <= 1.0001f || _claimTimer >= ClaimSafetySeconds)
                TryFlushClaim();
        }

        void RefreshPendingLabel()
        {
            if (_balanceLabel == null || !IsBackendEarnVp()) return;
            if (_walletCaption != null) _walletCaption.text = "SESSION VP";
            float shown = _pendingEstimateVp + (_hasInflight ? _inflightEstimateVp : 0f);
            _balanceLabel.text = shown > 0f
                ? $"+{FormatVpEstimate(shown)} <color=#FF4655>VP</color>"
                : $"0 <color=#FF4655>VP</color>";
        }

        static string FormatVpEstimate(float vp) => vp.ToString("0.###");

        void StartFly(int amount)
        {
            if (_flyLabel == null) return;
            var rootRect = ((RectTransform)transform).rect;
            _flyStart  = new Vector2(0f, rootRect.height * 0.5f - 150f);
            _flyTarget = new Vector2(rootRect.width * 0.5f - 70f, rootRect.height * 0.5f - 60f);
            _flyLabel.text  = $"+{amount} <color=#FF4655>VP</color>";
            _flyLabel.color = CritGold;
            _flyRt.anchoredPosition = _flyStart;
            _flyRt.localScale       = Vector3.one;
            _flyLabel.gameObject.SetActive(true);
            _flyRt.SetAsLastSibling();
            _flyActive = true;
            _flyT      = 0f;
        }

        void ShowMilestoneFlash(float milestone)
        {
            if (_flashLabel == null) return;
            _flashLabel.text = milestone >= MultMax
                ? "MAX MULTIPLIER ×3"
                : $"MULTIPLIER ×{milestone:0.#}";
            _flashLabel.color = MultColor(milestone);
            _flashT = 0f;
            _flashLabel.gameObject.SetActive(true);
        }

        static Color MultColor(float m)
        {
            if (m >= 2.5f) return TierGreen;
            if (m >= 1.5f) return TierOrange;
            return TextMain;
        }

        static float MultToBar(float m)
        {
            // Linear fill 1.00×→3.00×; milestones (1.5/2/2.5×) land on the quarter marks
            return Mathf.Clamp01((m - 1f) / (MultMax - 1f));
        }

        void RefreshMultiplierUi(bool force = false)
        {
            int cents = Mathf.RoundToInt(_multiplier * 100f);
            if (!force && cents == _shownMultCents)
            {
                if (_barFill != null) _barFill.fillAmount = MultToBar(_multiplier);
                return;
            }
            _shownMultCents = cents;

            bool wasMax = _atMax;
            _atMax = _multiplier >= MultMax - 0.001f;

            if (_multiplierLabel != null)
            {
                _multiplierLabel.text  = $"{_multiplier:F2}x";
                _multiplierLabel.color = MultColor(_multiplier);
                if (wasMax && !_atMax)
                    _multiplierLabel.rectTransform.localScale = Vector3.one;
            }
            if (_maxLabel != null && _maxLabel.gameObject.activeSelf != _atMax)
                _maxLabel.gameObject.SetActive(_atMax);
            if (_barFill != null)
            {
                _barFill.fillAmount = MultToBar(_multiplier);
                _barFill.color      = MultColor(_multiplier) == TextMain ? Accent : MultColor(_multiplier);
            }
        }

        void RefreshComboLabel()
        {
            if (_comboLabel == null) return;
            if (_comboActive && _combo > 0)
            {
                _comboLabel.text  = $"COMBO  <color=#F5F5F5>x{_combo}</color>";
                _comboLabel.color = Accent;
            }
            else
            {
                _comboLabel.text  = "COMBO  x0";
                _comboLabel.color = TextSub;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // UPDATE — decay, idle motion, pooled visuals
        // ═════════════════════════════════════════════════════════════════════

        void Update()
        {
            if (!IsVisible) return;
            float dt = Time.deltaTime;

            var ctx = GameContext.Instance;
            if (ctx != null && !ctx.CanUseLocalEconomy)
                UpdateBackendClaim(dt);

            if (ctx != null && ctx.BackendEnabled && IsAuthPending(_adUnavailableReason))
            {
                _adStatusRetryTick += Time.unscaledDeltaTime;
                if (_adStatusRetryTick >= 1f)
                {
                    _adStatusRetryTick = 0f;
                    if (ctx.HasGuestToken)
                        RefreshAdBonusStatus();
                }
            }

            if (_adCooldownRemaining > 0f && !_adClaimInFlight)
            {
                _adCountdownTick += Time.unscaledDeltaTime;
                if (_adCountdownTick >= 1f)
                {
                    var steps = Mathf.FloorToInt(_adCountdownTick);
                    _adCountdownTick -= steps;
                    _adCooldownRemaining = Mathf.Max(0f, _adCooldownRemaining - steps);
                    if (_adCooldownRemaining <= 0f) _adActive = false;
                    RefreshAdBonusBlock();
                }
            }

            // ── Multiplier decay — starts immediately, ramps up while idle ────
            if (_multiplier > 1f)
            {
                float idle  = Time.time - _lastTapTime;
                float speed = idle < FastAfter     ? DecaySlow
                            : idle < VeryFastAfter ? DecayFast
                            :                        DecayVeryFast;
                _multiplier = Mathf.Max(1f, _multiplier - speed * dt);
                RefreshMultiplierUi();
            }

            // Combo counter keeps its own inactivity window (visual only)
            if (_comboActive && Time.time - _lastTapTime > ComboResetDelay)
            {
                _comboActive = false;
                _combo = 0;
                RefreshComboLabel();
            }

            // ── Idle motion: rotating rings + breathing glow ──────────────────
            if (_ringSlow != null) _ringSlow.Rotate(0f, 0f, 18f * dt);
            if (_ringFast != null) _ringFast.Rotate(0f, 0f, -32f * dt);

            if (_glowImg != null)
            {
                _glowKick = Mathf.MoveTowards(_glowKick, 0f, dt * 1.2f);
                float idle = _atMax
                    ? 0.14f + Mathf.Sin(Time.time * 3f) * 0.03f    // strong glow at MAX
                    : 0.06f + Mathf.Sin(Time.time * 2f) * 0.015f;
                var c = _glowImg.color;
                _glowImg.color = new Color(c.r, c.g, c.b, idle + _glowKick);
                float s = 1f + Mathf.Sin(Time.time * 2f) * 0.025f + _glowKick * 0.4f;
                _glowRt.localScale = new Vector3(s, s, 1f);
            }

            // ── MAX multiplier pulse ──────────────────────────────────────────
            if (_atMax && _multiplierLabel != null)
            {
                float ms = 1f + Mathf.Sin(Time.time * 5f) * 0.04f;
                _multiplierLabel.rectTransform.localScale = new Vector3(ms, ms, 1f);
            }

            // ── Tap punch on core ─────────────────────────────────────────────
            if (_punchT < 1f && _coreVisual != null)
            {
                _punchT += dt / 0.18f;
                float p = Mathf.Clamp01(_punchT);
                float s = p < 0.30f ? Mathf.Lerp(1.00f, 0.92f, p / 0.30f)
                        : p < 0.65f ? Mathf.Lerp(0.92f, 1.06f, (p - 0.30f) / 0.35f)
                        :             Mathf.Lerp(1.06f, 1.00f, (p - 0.65f) / 0.35f);
                _coreVisual.localScale = new Vector3(s, s, 1f);
            }

            // ── Wallet punch ──────────────────────────────────────────────────
            if (_walletPunchT < 1f && _balanceRt != null)
            {
                _walletPunchT += dt / 0.15f;
                float s = Mathf.Lerp(1.08f, 1f, Mathf.Clamp01(_walletPunchT));
                _balanceRt.localScale = new Vector3(s, s, 1f);
            }

            // ── Pending VP fly-to-wallet ──────────────────────────────────────
            if (_flyActive && _flyRt != null)
            {
                _flyT += dt / 0.6f;
                float p = Mathf.Clamp01(_flyT);
                float ease = 1f - (1f - p) * (1f - p);
                _flyRt.anchoredPosition = Vector2.LerpUnclamped(_flyStart, _flyTarget, ease);
                float s = Mathf.Lerp(1f, 0.6f, p);
                _flyRt.localScale = new Vector3(s, s, 1f);
                float a = p < 0.7f ? 1f : Mathf.Lerp(1f, 0f, (p - 0.7f) / 0.3f);
                var c = _flyLabel.color;
                _flyLabel.color = new Color(c.r, c.g, c.b, a);
                if (p >= 1f)
                {
                    _flyActive = false;
                    _flyLabel.gameObject.SetActive(false);
                }
            }

            // ── Milestone flash ───────────────────────────────────────────────
            if (_flashT < 1f && _flashLabel != null)
            {
                _flashT += dt / 0.80f;
                float p = Mathf.Clamp01(_flashT);
                float s = p < 0.18f ? Mathf.Lerp(1.55f, 1f, p / 0.18f) : 1f;
                float a = p < 0.18f ? Mathf.Lerp(0f, 1f, p / 0.18f)
                        : p < 0.62f ? 1f
                        :             Mathf.Lerp(1f, 0f, (p - 0.62f) / 0.38f);
                _flashLabel.rectTransform.localScale = new Vector3(s, s, 1f);
                var c = _flashLabel.color;
                _flashLabel.color = new Color(c.r, c.g, c.b, a);
                if (p >= 1f) _flashLabel.gameObject.SetActive(false);
            }

            // ── Floating reward texts ─────────────────────────────────────────
            for (int i = 0; i < _floats.Length; i++)
            {
                if (!_floats[i].active) continue;
                _floats[i].t += dt;
                float p = _floats[i].t / _floats[i].dur;
                if (p >= 1f)
                {
                    _floats[i].active = false;
                    _floats[i].rt.gameObject.SetActive(false);
                    continue;
                }
                _floats[i].rt.anchoredPosition = _floats[i].start + Vector2.up * (p * 130f);
                float fs = p < 0.12f ? Mathf.Lerp(0.5f, 1.2f, p / 0.12f)
                         : p < 0.24f ? Mathf.Lerp(1.2f, 1.0f, (p - 0.12f) / 0.12f)
                         : 1f;
                _floats[i].rt.localScale = Vector3.one * fs;
                var col = _floats[i].color;
                float fa = p < 0.45f ? 1f : Mathf.Lerp(1f, 0f, (p - 0.45f) / 0.55f);
                _floats[i].tmp.color = new Color(col.r, col.g, col.b, fa);
            }

            // ── Particles ─────────────────────────────────────────────────────
            for (int i = 0; i < _particles.Length; i++)
            {
                if (!_particles[i].active) continue;
                _particles[i].t += dt;
                float p = _particles[i].t / _particles[i].dur;
                if (p >= 1f)
                {
                    _particles[i].active = false;
                    _particles[i].rt.gameObject.SetActive(false);
                    continue;
                }
                float ease = 1f - (1f - p) * (1f - p);   // ease-out
                _particles[i].rt.anchoredPosition = _particles[i].start + _particles[i].dir * ease;
                var c = _particles[i].img.color;
                _particles[i].img.color = new Color(c.r, c.g, c.b, 1f - p);
                float ps = Mathf.Lerp(1f, 0.3f, p);
                _particles[i].rt.localScale = new Vector3(ps, ps, 1f);
            }

            // ── Tip rotation ──────────────────────────────────────────────────
            _tipTimer += dt;
            if (_tipTimer >= 6f && _tipLabel != null)
            {
                _tipTimer = 0f;
                _tipIndex = (_tipIndex + 1) % Tips.Length;
                _tipLabel.text = Tips[_tipIndex];
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // POOLED VISUAL SPAWNERS
        // ═════════════════════════════════════════════════════════════════════

        void SpawnFloat(string text, Color color, float scaleMul)
        {
            int slot = -1;
            float oldest = -1f;
            for (int i = 0; i < _floats.Length; i++)
            {
                if (!_floats[i].active) { slot = i; break; }
                if (_floats[i].t > oldest) { oldest = _floats[i].t; slot = i; }
            }
            if (slot < 0 || _floats[slot].rt == null) return;

            _floats[slot].active = true;
            _floats[slot].t      = 0f;
            _floats[slot].dur    = 1.1f;
            _floats[slot].start  = new Vector2(Random.Range(-60f, 60f), Random.Range(10f, 50f));
            _floats[slot].color  = color;
            _floats[slot].tmp.text     = text;
            _floats[slot].tmp.color    = color;
            _floats[slot].tmp.fontSize = 34f * scaleMul;
            _floats[slot].rt.anchoredPosition = _floats[slot].start;
            _floats[slot].rt.localScale       = Vector3.one * 0.5f;
            _floats[slot].rt.gameObject.SetActive(true);
            _floats[slot].rt.SetAsLastSibling();
        }

        void SpawnParticles(Color color)
        {
            int spawned = 0;
            for (int i = 0; i < _particles.Length && spawned < ParticlesPerTap; i++)
            {
                if (_particles[i].active || _particles[i].rt == null) continue;
                float ang  = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(90f, 160f);

                _particles[i].active = true;
                _particles[i].t      = 0f;
                _particles[i].dur    = Random.Range(0.35f, 0.55f);
                _particles[i].start  = Vector2.zero;
                _particles[i].dir    = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                _particles[i].img.color = color;
                _particles[i].rt.anchoredPosition = Vector2.zero;
                _particles[i].rt.localScale       = Vector3.one;
                _particles[i].rt.gameObject.SetActive(true);
                spawned++;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ONE-SHOT UI CONSTRUCTION
        // ═════════════════════════════════════════════════════════════════════

        void BuildUiOnce()
        {
            if (_builtUi) return;
            _builtUi = true;

            var rt = (RectTransform)transform;

            // Root panel tint + hide legacy builder chrome (TopBar with old back/wallet)
            var rootImg = GetComponent<Image>();
            if (rootImg != null) rootImg.color = BgDeep;
            if (backButton != null && backButton.transform.parent != rt)
                backButton.transform.parent.gameObject.SetActive(false);

            // ── Background layers ─────────────────────────────────────────────
            var bgTop = NewRect(rt, "BgTop", new Vector2(0f, 0.55f), Vector2.one, new Vector2(0.5f, 1f));
            bgTop.offsetMin = bgTop.offsetMax = Vector2.zero;
            var bgTopImg = bgTop.gameObject.AddComponent<Image>();
            bgTopImg.color = BgMid; bgTopImg.raycastTarget = false;
            bgTop.SetSiblingIndex(0);

            _glowRt = NewRect(rt, "CenterGlow", Center, Center, Center);
            _glowRt.anchoredPosition = new Vector2(0f, 40f);
            _glowRt.sizeDelta        = new Vector2(640f, 640f);
            _glowRt.localRotation    = Quaternion.Euler(0f, 0f, 45f);
            _glowImg = _glowRt.gameObject.AddComponent<Image>();
            _glowImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.06f);
            _glowImg.raycastTarget = false;

            // ── Top: wallet ───────────────────────────────────────────────────
            BuildWalletBlock(rt);

            // ── Center container ──────────────────────────────────────────────
            var center = NewRect(rt, "Center", Center, Center, Center);
            center.anchoredPosition = new Vector2(0f, -30f);
            center.sizeDelta        = new Vector2(620f, 860f);

            BuildVpCore(center);
            BuildMultiplierBlock(center);

            // Milestone flash (above the core)
            _flashLabel = CreateTmp(center, "MilestoneFlash", "MULTIPLIER ×2",
                34f, FontStyles.Bold, TextAlignmentOptions.Center, TierOrange);
            _flashLabel.characterSpacing = 3f;
            var flashRt = _flashLabel.rectTransform;
            flashRt.anchorMin = flashRt.anchorMax = flashRt.pivot = new Vector2(0.5f, 0.5f);
            flashRt.anchoredPosition = new Vector2(0f, 290f);
            flashRt.sizeDelta        = new Vector2(560f, 48f);
            _flashLabel.gameObject.SetActive(false);

            // Fly-to-wallet chip (parented to root so it can travel to the corner)
            _flyLabel = CreateTmp(rt, "FlyVp", "",
                30f, FontStyles.Bold, TextAlignmentOptions.Center, CritGold);
            _flyLabel.richText = true;
            _flyRt = _flyLabel.rectTransform;
            _flyRt.anchorMin = _flyRt.anchorMax = _flyRt.pivot = new Vector2(0.5f, 0.5f);
            _flyRt.sizeDelta = new Vector2(220f, 48f);
            var flyOl = _flyLabel.gameObject.AddComponent<Outline>();
            flyOl.effectColor    = new Color(0f, 0f, 0f, 0.6f);
            flyOl.effectDistance = new Vector2(1.5f, -1.5f);
            _flyLabel.gameObject.SetActive(false);

            // ── Bottom: rotating tip ──────────────────────────────────────────
            _tipLabel = CreateTmp(rt, "Tip", Tips[0],
                13f, FontStyles.Normal, TextAlignmentOptions.Center, TextSub);
            var tipRt = _tipLabel.rectTransform;
            tipRt.anchorMin        = new Vector2(0f, 0f);
            tipRt.anchorMax        = new Vector2(1f, 0f);
            tipRt.pivot            = new Vector2(0.5f, 0f);
            tipRt.anchoredPosition = new Vector2(0f, 140f);
            tipRt.sizeDelta        = new Vector2(0f, 24f);

            // ── Back button (top-left, below global profile bar) ──────────────
            BuildBackButton(rt);

            // ── Rewarded ad bonus (2x VP) ─────────────────────────────────────
            BuildAdBonusBlock(rt);

            // ── Pools ─────────────────────────────────────────────────────────
            BuildPools(center);
        }

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        // ── Wallet block: large, premium, centered ───────────────────────────
        void BuildWalletBlock(RectTransform rt)
        {
            _walletCaption = CreateTmp(rt, "WalletCaption", "WALLET",
                12f, FontStyles.Bold, TextAlignmentOptions.Center, TextSub);
            _walletCaption.characterSpacing = 6f;
            var capRt = _walletCaption.rectTransform;
            capRt.anchorMin        = new Vector2(0f, 1f);
            capRt.anchorMax        = new Vector2(1f, 1f);
            capRt.pivot            = new Vector2(0.5f, 1f);
            capRt.anchoredPosition = new Vector2(0f, -96f);
            capRt.sizeDelta        = new Vector2(0f, 20f);

            _balanceLabel = CreateTmp(rt, "WalletAmount", "0 <color=#FF4655>VP</color>",
                46f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);
            _balanceLabel.richText = true;
            _balanceRt = _balanceLabel.rectTransform;
            _balanceRt.anchorMin        = new Vector2(0f, 1f);
            _balanceRt.anchorMax        = new Vector2(1f, 1f);
            _balanceRt.pivot            = new Vector2(0.5f, 1f);
            _balanceRt.anchoredPosition = new Vector2(0f, -118f);
            _balanceRt.sizeDelta        = new Vector2(0f, 58f);

            // Accent underline
            var line = NewRect(rt, "WalletLine", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            line.anchoredPosition = new Vector2(0f, -184f);
            line.sizeDelta        = new Vector2(140f, 3f);
            var lineImg = line.gameObject.AddComponent<Image>();
            lineImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            lineImg.raycastTarget = false;
        }

        // ── VP Core: layered diamond reactor ─────────────────────────────────
        void BuildVpCore(RectTransform parent)
        {
            const float coreY = 110f;

            // Punched visual root (scaled on tap)
            _coreVisual = NewRect(parent, "CoreVisual", Center, Center, Center);
            _coreVisual.anchoredPosition = new Vector2(0f, coreY);
            _coreVisual.sizeDelta        = new Vector2(400f, 400f);

            // Rotating decorative rings (thin diamond frames)
            _ringSlow = BuildRingFrame(_coreVisual, "RingSlow", 300f, new Color(Accent.r, Accent.g, Accent.b, 0.35f), 2f);
            _ringFast = BuildRingFrame(_coreVisual, "RingFast", 252f, new Color(TextSub.r, TextSub.g, TextSub.b, 0.30f), 1.5f);

            // Core diamond (#0D1117 with accent edge)
            var coreOuter = BuildDiamond(_coreVisual, "CoreOuter", 206f, BgCard);
            var coreOl = coreOuter.gameObject.AddComponent<Outline>();
            coreOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.9f);
            coreOl.effectDistance = new Vector2(2.5f, -2.5f);

            var coreInner = BuildDiamond(_coreVisual, "CoreInner", 168f, new Color(0.067f, 0.086f, 0.118f, 1f));
            var innerOl = coreInner.gameObject.AddComponent<Outline>();
            innerOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.25f);
            innerOl.effectDistance = new Vector2(1.5f, -1.5f);

            // Center labels (not rotated)
            var vpLbl = CreateTmp(_coreVisual, "VpLabel", "VP",
                58f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);
            var vpRt = vpLbl.rectTransform;
            vpRt.anchorMin = vpRt.anchorMax = vpRt.pivot = Center;
            vpRt.anchoredPosition = new Vector2(0f, 12f);
            vpRt.sizeDelta        = new Vector2(200f, 64f);
            var vpOl = vpLbl.gameObject.AddComponent<Outline>();
            vpOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
            vpOl.effectDistance = new Vector2(2f, -2f);

            var tapLbl = CreateTmp(_coreVisual, "TapLabel", "TAP",
                14f, FontStyles.Bold, TextAlignmentOptions.Center, TextSub);
            tapLbl.characterSpacing = 6f;
            var tapRt = tapLbl.rectTransform;
            tapRt.anchorMin = tapRt.anchorMax = tapRt.pivot = Center;
            tapRt.anchoredPosition = new Vector2(0f, -34f);
            tapRt.sizeDelta        = new Vector2(200f, 22f);

            // Particle root — outside the punched visual so bursts don't scale with it
            _particleRoot = NewRect(parent, "Particles", Center, Center, Center);
            _particleRoot.anchoredPosition = new Vector2(0f, coreY);
            _particleRoot.sizeDelta        = new Vector2(10f, 10f);

            // Float text root — above the core
            _floatRoot = NewRect(parent, "Floats", Center, Center, Center);
            _floatRoot.anchoredPosition = new Vector2(0f, coreY + 130f);
            _floatRoot.sizeDelta        = new Vector2(360f, 200f);

            // Invisible tap hit area on top
            var hit = NewRect(parent, "TapZone", Center, Center, Center);
            hit.anchoredPosition = new Vector2(0f, coreY);
            hit.sizeDelta        = new Vector2(360f, 360f);
            var hitImg = hit.gameObject.AddComponent<Image>();
            hitImg.color = Color.clear;
            hitImg.raycastTarget = true;

            var et = hit.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ => OnTap());
            et.triggers.Add(entry);
        }

        // ── Multiplier + combo bar + combo counter ───────────────────────────
        void BuildMultiplierBlock(RectTransform parent)
        {
            _multiplierLabel = CreateTmp(parent, "Multiplier", "1.00x",
                52f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);
            var mRt = _multiplierLabel.rectTransform;
            mRt.anchorMin = mRt.anchorMax = mRt.pivot = Center;
            mRt.anchoredPosition = new Vector2(0f, -150f);
            mRt.sizeDelta        = new Vector2(400f, 64f);

            // MAX badge — shown only while the multiplier is capped at 3.00×
            _maxLabel = CreateTmp(parent, "MaxBadge", "MAX",
                15f, FontStyles.Bold, TextAlignmentOptions.Center, TierGreen);
            _maxLabel.characterSpacing = 4f;
            var maxRt = _maxLabel.rectTransform;
            maxRt.anchorMin = maxRt.anchorMax = maxRt.pivot = Center;
            maxRt.anchoredPosition = new Vector2(138f, -150f);
            maxRt.sizeDelta        = new Vector2(70f, 24f);
            var maxOl = _maxLabel.gameObject.AddComponent<Outline>();
            maxOl.effectColor    = new Color(TierGreen.r, TierGreen.g, TierGreen.b, 0.45f);
            maxOl.effectDistance = new Vector2(1.5f, -1.5f);
            _maxLabel.gameObject.SetActive(false);

            var multCaption = CreateTmp(parent, "MultCaption", "MULTIPLIER",
                11f, FontStyles.Bold, TextAlignmentOptions.Center, TextSub);
            multCaption.characterSpacing = 5f;
            var mcRt = multCaption.rectTransform;
            mcRt.anchorMin = mcRt.anchorMax = mcRt.pivot = Center;
            mcRt.anchoredPosition = new Vector2(0f, -192f);
            mcRt.sizeDelta        = new Vector2(300f, 18f);

            // ── Combo bar ─────────────────────────────────────────────────────
            const float barW = 480f, barH = 12f;
            var barBg = NewRect(parent, "ComboBar", Center, Center, Center);
            barBg.anchoredPosition = new Vector2(0f, -242f);
            barBg.sizeDelta        = new Vector2(barW, barH);
            var barBgImg = barBg.gameObject.AddComponent<Image>();
            barBgImg.color = BgCard;
            barBgImg.raycastTarget = false;
            var barOl = barBg.gameObject.AddComponent<Outline>();
            barOl.effectColor    = new Color(1f, 1f, 1f, 0.06f);
            barOl.effectDistance = new Vector2(1f, -1f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBg, false);
            _barFill = fillGo.GetComponent<Image>();
            _barFill.type          = Image.Type.Filled;
            _barFill.fillMethod    = Image.FillMethod.Horizontal;
            _barFill.fillOrigin    = 0;
            _barFill.fillAmount    = 0f;
            _barFill.color         = Accent;
            _barFill.raycastTarget = false;
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1.5f, 1.5f); fillRt.offsetMax = new Vector2(-1.5f, -1.5f);

            // Milestone tick marks inside the bar (2x / 3x / 5x boundaries)
            float[] ticks = { 0.25f, 0.50f, 0.75f };
            for (int i = 0; i < ticks.Length; i++)
            {
                var mk = new GameObject($"Tick{i}", typeof(RectTransform), typeof(Image));
                mk.transform.SetParent(barBg, false);
                var mkRt = (RectTransform)mk.transform;
                mkRt.anchorMin        = new Vector2(ticks[i], 0f);
                mkRt.anchorMax        = new Vector2(ticks[i], 1f);
                mkRt.pivot            = Center;
                mkRt.anchoredPosition = Vector2.zero;
                mkRt.sizeDelta        = new Vector2(2f, 0f);
                var mkImg = mk.GetComponent<Image>();
                mkImg.color = new Color(1f, 1f, 1f, 0.22f);
                mkImg.raycastTarget = false;
            }

            // Milestone labels under the bar
            string[] labels = { "1x", "1.5x", "2x", "2.5x", "3x" };
            float[]  fracs  = { 0f, 0.25f, 0.50f, 0.75f, 1f };
            var labelRow = NewRect(parent, "BarLabels", Center, Center, Center);
            labelRow.anchoredPosition = new Vector2(0f, -264f);
            labelRow.sizeDelta        = new Vector2(barW, 16f);
            for (int i = 0; i < labels.Length; i++)
            {
                var l = CreateTmp(labelRow, $"L{i}", labels[i],
                    10f, FontStyles.Normal, TextAlignmentOptions.Center, TextSub);
                var lRt = l.rectTransform;
                lRt.anchorMin        = new Vector2(fracs[i], 0.5f);
                lRt.anchorMax        = new Vector2(fracs[i], 0.5f);
                lRt.pivot            = Center;
                lRt.anchoredPosition = Vector2.zero;
                lRt.sizeDelta        = new Vector2(40f, 16f);
            }

            // ── Combo counter ─────────────────────────────────────────────────
            _comboLabel = CreateTmp(parent, "Combo", "COMBO  x0",
                16f, FontStyles.Bold, TextAlignmentOptions.Center, TextSub);
            _comboLabel.richText = true;
            _comboLabel.characterSpacing = 2f;
            var cRt = _comboLabel.rectTransform;
            cRt.anchorMin = cRt.anchorMax = cRt.pivot = Center;
            cRt.anchoredPosition = new Vector2(0f, -310f);
            cRt.sizeDelta        = new Vector2(300f, 24f);
        }

        // ── Back button ───────────────────────────────────────────────────────
        void BuildBackButton(RectTransform rt)
        {
            var backBtn = new GameObject("BackBtn",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            backBtn.transform.SetParent(rt, false);
            var backRt = (RectTransform)backBtn.transform;
            backRt.anchorMin        = new Vector2(0f, 1f);
            backRt.anchorMax        = new Vector2(0f, 1f);
            backRt.pivot            = new Vector2(0f, 1f);
            backRt.anchoredPosition = new Vector2(28f, -88f);
            backRt.sizeDelta        = new Vector2(96f, 36f);

            backBtn.GetComponent<Image>().color = BgCard;
            var backOl = backBtn.GetComponent<Outline>();
            backOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
            backOl.effectDistance = new Vector2(1.5f, -1.5f);

            var backLbl = CreateTmp(backBtn.transform, "Lbl", "BACK",
                12f, FontStyles.Bold, TextAlignmentOptions.Center, TextMain);
            backLbl.characterSpacing = 2f;
            var bRt = backLbl.rectTransform;
            bRt.anchorMin = Vector2.zero; bRt.anchorMax = Vector2.one;
            bRt.offsetMin = bRt.offsetMax = Vector2.zero;

            var btn = backBtn.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnBackClicked);
            backBtn.transform.SetAsLastSibling();
        }

        // ── Rewarded ad bonus block (bottom, above the tip) ──────────────────────
        void BuildAdBonusBlock(RectTransform rt)
        {
            _adBlock = NewRect(rt, "AdBonus", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            _adBlock.anchoredPosition = new Vector2(0f, 188f);
            _adBlock.sizeDelta        = new Vector2(380f, 76f);

            var btnGo = new GameObject("AdButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            btnGo.transform.SetParent(_adBlock, false);
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin = Vector2.zero; btnRt.anchorMax = Vector2.one;
            btnRt.offsetMin = Vector2.zero; btnRt.offsetMax = Vector2.zero;

            _adButtonImg = btnGo.GetComponent<Image>();
            _adButtonImg.color = BgCard;
            var ol = btnGo.GetComponent<Outline>();
            ol.effectColor    = new Color(AdGold.r, AdGold.g, AdGold.b, 0.8f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            _adButton = btnGo.GetComponent<Button>();
            _adButton.transition = Selectable.Transition.None;
            _adButton.onClick.AddListener(OnAdBonusClicked);

            _adTitleLabel = CreateTmp(btnRt, "AdTitle", "REKLAM İZLE → 2X VP",
                19f, FontStyles.Bold, TextAlignmentOptions.Center, AdGold);
            _adTitleLabel.characterSpacing = 2f;
            var tRt = _adTitleLabel.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0.5f); tRt.anchorMax = new Vector2(1f, 1f);
            tRt.offsetMin = new Vector2(12f, 0f);  tRt.offsetMax = new Vector2(-12f, -8f);

            _adSubLabel = CreateTmp(btnRt, "AdSub", "",
                12f, FontStyles.Normal, TextAlignmentOptions.Center, TextSub);
            var sRt = _adSubLabel.rectTransform;
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 0.5f);
            sRt.offsetMin = new Vector2(12f, 8f); sRt.offsetMax = new Vector2(-12f, 0f);

            _adBlock.gameObject.SetActive(IsBackendEarnVp());
        }

        void RefreshAdBonusBlock()
        {
            if (_adBlock == null) return;

            if (!IsBackendEarnVp())
            {
                _adBlock.gameObject.SetActive(false);
                return;
            }
            _adBlock.gameObject.SetActive(true);

            if (_adActive)
            {
                SetAdButtonState(false, TierGreen, "2X BONUS ACTIVE", FormatCooldown(_adCooldownRemaining));
                return;
            }

            if (_adClaimInFlight)
            {
                SetAdButtonState(false, AdGold, "REKLAM İZLENİYOR...", "");
                return;
            }
            if (_adCooldownRemaining > 0.5f)
            {
                SetAdButtonState(false, TextSub, "REKLAM İZLE → 2X VP", $"Tekrar: {FormatCooldown(_adCooldownRemaining)}");
                return;
            }
            if (IsEarnNoActiveSession(_adUnavailableReason))
            {
                SetAdButtonState(true, AdGold, "2X BONUS ICIN REKLAM IZLE", "Tap oturumu reklamla baslar");
                return;
            }
            if (!_adAvailable)
            {
                SetAdButtonState(false, TextSub, "REKLAM İZLE → 2X VP", AdRewardMessages.MapUnavailable(_adUnavailableReason));
                return;
            }

            SetAdButtonState(true, AdGold, "REKLAM İZLE → 2X VP", "Tap ödülünü 2x yap");
        }

        void SetAdButtonState(bool interactable, Color titleColor, string title, string sub)
        {
            if (_adButton != null) _adButton.interactable = interactable;
            if (_adButtonImg != null) _adButtonImg.color = _adActive ? new Color(TierGreen.r, TierGreen.g, TierGreen.b, 0.24f) : BgCard;
            if (_adTitleLabel != null)
            {
                _adTitleLabel.text  = title;
                _adTitleLabel.color = titleColor;
            }
            if (_adSubLabel != null) _adSubLabel.text = sub;
        }

        static string FormatCooldown(float seconds)
        {
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        static bool IsEarnNoActiveSession(string reason)
            => string.Equals(reason, "EARN_VP_NO_ACTIVE_SESSION", System.StringComparison.OrdinalIgnoreCase);

        static bool IsAuthPending(string reason)
            => string.Equals(reason, "AUTH_PENDING", System.StringComparison.OrdinalIgnoreCase);

        void RefreshAdBonusStatus()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) return;
            if (!ctx.HasGuestToken)
            {
                _adAvailable = false;
                _adUnavailableReason = "AUTH_PENDING";
                _adStatusRetryTick = 0f;
                RefreshAdBonusBlock();
                return;
            }

            ctx.RefreshEarnVpAdStatus(CurrentEarnSessionId(),
                res =>
                {
                    if (this == null) return;
                    ApplyAdStatus(res != null ? res.Find(AdRewardTypes.EarnVp2x) : null);
                },
                reason =>
                {
                    if (this == null) return;
                    _adAvailable = false;
                    _adUnavailableReason = string.IsNullOrEmpty(reason) ? null : reason;
                    RefreshAdBonusBlock();
                });
        }

        void ApplyAdStatus(AdRewardPlacementStatus status)
        {
            if (status != null)
            {
                _adAvailable         = status.isAvailable;
                _adUnavailableReason = status.unavailableReason;
                _adActive            = status.earnVp2xActive;
                _adCooldownRemaining = status.earnVp2xRemainingSeconds > 0
                    ? status.earnVp2xRemainingSeconds
                    : status.cooldownRemainingSeconds;
                _adCountdownTick = 0f;
            }
            else
            {
                _adAvailable = true;
                _adUnavailableReason = null;
                _adActive = false;
                _adCooldownRemaining = 0f;
            }
            RefreshAdBonusBlock();
        }

        void OnAdBonusClicked()
        {
            if (_adClaimInFlight || _adActive) return;
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) return;
            if (!ctx.HasGuestToken)
            {
                _adUnavailableReason = "AUTH_PENDING";
                RefreshAdBonusBlock();
                return;
            }

            _adClaimInFlight = true;
            RefreshAdBonusBlock();

            StartCoroutine(WatchEarnVp2xWhenSessionReady(ctx));
        }

        IEnumerator WatchEarnVp2xWhenSessionReady(GameContext ctx)
        {
            if (string.IsNullOrEmpty(_sessionId))
            {
                if (_pendingTaps == 0)
                    _sessionStartMs = NowMs();
                EnsureSessionStarted();
                while (_sessionStarting)
                    yield return null;
            }

            var earnSessionId = _sessionId;
            if (string.IsNullOrEmpty(earnSessionId))
            {
                _adClaimInFlight = false;
                RefreshAdBonusBlock();
                GameEvents.RaiseToast("GeÃ§ersiz oturum.");
                yield break;
            }

            _adEarnSessionId = earnSessionId;
            ctx.WatchEarnVp2xAd(earnSessionId,
                res =>
                {
                    if (this == null) return;
                    _adClaimInFlight = false;
                    if (res != null)
                    {
                        _adActive            = res.earnVp2xActive;
                        _adCooldownRemaining = res.earnVp2xRemainingSeconds > 0
                            ? res.earnVp2xRemainingSeconds
                            : res.cooldownRemainingSeconds;
                        _adCountdownTick = 0f;
                        if (res.earnVp2xActive) GameEvents.RaiseToast("2X VP bonusu aktif");
                    }
                    RefreshAdBonusBlock();
                    RefreshAdBonusStatus();
                },
                msg =>
                {
                    if (this == null) return;
                    _adClaimInFlight = false;
                    RefreshAdBonusBlock();
                    if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
                },
                onCancelled: () =>
                {
                    if (this == null) return;
                    _adClaimInFlight = false;
                    RefreshAdBonusBlock();
                });
        }

        // ── Pools ─────────────────────────────────────────────────────────────
        void BuildPools(RectTransform center)
        {
            for (int i = 0; i < FloatPoolSize; i++)
            {
                var go = new GameObject($"Float{i}", typeof(RectTransform));
                go.transform.SetParent(_floatRoot, false);
                var frt = (RectTransform)go.transform;
                frt.anchorMin = frt.anchorMax = frt.pivot = Center;
                frt.sizeDelta = new Vector2(260f, 56f);

                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.fontSize           = 34f;
                tmp.fontStyle          = FontStyles.Bold;
                tmp.alignment          = TextAlignmentOptions.Center;
                tmp.raycastTarget      = false;
                tmp.enableWordWrapping = false;
                var ol = go.AddComponent<Outline>();
                ol.effectColor    = new Color(0f, 0f, 0f, 0.6f);
                ol.effectDistance = new Vector2(1.5f, -1.5f);

                go.SetActive(false);
                _floats[i] = new FloatEntry { rt = frt, tmp = tmp };
            }

            for (int i = 0; i < ParticlePoolSize; i++)
            {
                var go = new GameObject($"P{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_particleRoot, false);
                var prt = (RectTransform)go.transform;
                prt.anchorMin = prt.anchorMax = prt.pivot = Center;
                prt.sizeDelta     = new Vector2(10f, 10f);
                prt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;

                go.SetActive(false);
                _particles[i] = new ParticleEntry { rt = prt, img = img };
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // PRIMITIVE HELPERS
        // ═════════════════════════════════════════════════════════════════════

        // Solid diamond: default UI square rotated 45°
        static Image BuildDiamond(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var drt = (RectTransform)go.transform;
            drt.anchorMin = drt.anchorMax = drt.pivot = Center;
            drt.anchoredPosition = Vector2.zero;
            drt.sizeDelta        = new Vector2(size, size);
            drt.localRotation    = Quaternion.Euler(0f, 0f, 45f);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // Thin square frame (4 bars) — rotated continuously to read as a ring
        static RectTransform BuildRingFrame(Transform parent, string name, float size, Color color, float thickness)
        {
            var pivot = new GameObject(name, typeof(RectTransform));
            pivot.transform.SetParent(parent, false);
            var prt = (RectTransform)pivot.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = Center;
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta        = new Vector2(size, size);
            prt.localRotation    = Quaternion.Euler(0f, 0f, 45f);

            float half = size * 0.5f;
            Vector2[] pos  = { new Vector2(0f, half), new Vector2(0f, -half), new Vector2(-half, 0f), new Vector2(half, 0f) };
            bool[] horiz   = { true, true, false, false };
            for (int i = 0; i < 4; i++)
            {
                var bar = new GameObject($"Edge{i}", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(pivot.transform, false);
                var brt = (RectTransform)bar.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = Center;
                brt.anchoredPosition = pos[i];
                brt.sizeDelta = horiz[i]
                    ? new Vector2(size + thickness, thickness)
                    : new Vector2(thickness, size + thickness);
                var img = bar.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            return prt;
        }

        static RectTransform NewRect(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var nrt = (RectTransform)go.transform;
            nrt.anchorMin = aMin; nrt.anchorMax = aMax; nrt.pivot = pivot;
            return nrt;
        }

        static TextMeshProUGUI CreateTmp(Transform parent, string name, string text,
            float size, FontStyles style, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.fontStyle          = style;
            tmp.alignment          = align;
            tmp.color              = color;
            tmp.raycastTarget      = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }
    }
}
