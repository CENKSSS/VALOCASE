using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Audio;
using ValoCase.Core;

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
            RefreshState();
        }

        void RefreshState()
        {
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

        void Claim()
        {
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

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
