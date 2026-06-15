using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Audio;
using ValoCase.Core;
using ValoCase.Services.Backend;

namespace ValoCase.UI
{
    public sealed class DailyRewardPopup : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] TextMeshProUGUI streakLabel;
        [SerializeField] TextMeshProUGUI rewardLabel;
        [SerializeField] TextMeshProUGUI timerLabel;
        [SerializeField] Button claimButton;
        [SerializeField] Button closeButton;

        // Backend mode state (runtime only). Empty/false in local mode.
        bool _backend;
        bool _statusLoaded;
        bool _claimInFlight;
        DailyStatusResponse _status;
        double _nextClaimRealtime;   // unscaled time when the next claim becomes available

        void Awake()
        {
            if (claimButton != null) claimButton.onClick.AddListener(Claim);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            Hide();
        }

        void Update()
        {
            if (root == null || !root.activeSelf) return;
            RefreshState();
        }

        public void TryShow()
        {
            if (root == null) return;
            root.SetActive(true);

            _backend = GameContext.Instance != null && GameContext.Instance.BackendEnabled;
            if (_backend)
            {
                _statusLoaded  = false;
                _claimInFlight = false;
                FetchBackendStatus();
            }

            RefreshState();
        }

        void FetchBackendStatus()
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;
            ctx.RefreshDailyBackend(
                res =>
                {
                    _status = res;
                    _statusLoaded = true;
                    _nextClaimRealtime = Time.unscaledTimeAsDouble +
                                         Math.Max(0L, res != null ? res.secondsUntilNextClaim : 0L);
                    RefreshState();
                },
                err => { if (!string.IsNullOrEmpty(err)) GameEvents.RaiseToast(err); });
        }

        void RefreshState()
        {
            if (_backend) { RefreshStateBackend(); return; }

            // ── Local mode (unchanged) ──
            var daily = GameContext.Instance?.DailyRewards;
            if (daily == null) return;

            if (streakLabel != null) streakLabel.text = $"Streak: {daily.CurrentStreak} days";
            if (rewardLabel != null) rewardLabel.text = $"+{daily.PeekTodayReward():N0} VP";
            if (claimButton != null) claimButton.interactable = daily.CanClaimToday;
            if (timerLabel != null)
                timerLabel.text = daily.CanClaimToday
                    ? "Reward available!"
                    : $"Next reward in {daily.TimeUntilNextClaim:hh\\:mm\\:ss}";
        }

        void RefreshStateBackend()
        {
            if (streakLabel != null)
                streakLabel.text = _status != null ? $"Streak: {_status.currentStreak} days" : "Streak: …";
            if (rewardLabel != null)
                rewardLabel.text = _status != null ? $"+{_status.nextRewardVp:N0} VP" : "…";

            bool claimable = _statusLoaded && _status != null && _status.claimable && !_claimInFlight;
            if (claimButton != null) claimButton.interactable = claimable;

            if (timerLabel != null)
            {
                if (!_statusLoaded || _status == null)
                    timerLabel.text = "Loading…";
                else if (_status.claimable)
                    timerLabel.text = "Reward available!";
                else
                {
                    double remain = _nextClaimRealtime - Time.unscaledTimeAsDouble;
                    if (remain < 0) remain = 0;
                    var ts = TimeSpan.FromSeconds(remain);
                    timerLabel.text = $"Next reward in {ts:hh\\:mm\\:ss}";
                }
            }
        }

        void Claim()
        {
            if (_backend) { ClaimBackend(); return; }

            // ── Local mode (unchanged) ──
            var ctx = GameContext.Instance;
            if (ctx?.DailyRewards != null && ctx.DailyRewards.TryClaim(out var reward))
            {
                SoundManager.Instance?.Play(SoundId.DailyClaim);
                ctx.Statistics?.RecordVpEarned(reward);
                ctx.Save?.Save();
                GameEvents.RaiseToast($"Daily reward: +{reward:N0} VP");
                RefreshState();
            }
        }

        void ClaimBackend()
        {
            if (_claimInFlight) return;
            if (!_statusLoaded || _status == null || !_status.claimable) return;

            var ctx = GameContext.Instance;
            if (ctx == null) return;

            _claimInFlight = true;
            if (claimButton != null) claimButton.interactable = false;

            ctx.ClaimDailyBackend(
                res =>
                {
                    _claimInFlight = false;
                    // Wallet already applied by the helper (authoritative). Update the
                    // cached status so the button flips to the cooldown immediately.
                    if (_status != null)
                    {
                        _status.claimable = false;
                        if (res != null) _status.currentStreak = res.currentStreak;
                    }
                    SoundManager.Instance?.Play(SoundId.DailyClaim);
                    int reward = res != null ? res.rewardVp : 0;
                    GameEvents.RaiseToast($"Daily reward: +{reward:N0} VP");
                    // Re-pull for the authoritative next-claim countdown.
                    FetchBackendStatus();
                    RefreshState();
                },
                err =>
                {
                    _claimInFlight = false;
                    if (!string.IsNullOrEmpty(err)) GameEvents.RaiseToast(err);
                    RefreshState();
                });
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
