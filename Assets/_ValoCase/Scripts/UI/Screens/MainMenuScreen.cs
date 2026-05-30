using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Services;

namespace ValoCase.UI.Screens
{
    public sealed class MainMenuScreen : UIScreenBase
    {
        [Header("Navigation")]
        [SerializeField] Button openCaseButton;
        [SerializeField] Button inventoryButton;
        [SerializeField] Button shopButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button weaponsButton;
        [SerializeField] Button upgradeButton;
        [SerializeField] Button caseBattleButton;
        [SerializeField] Button earnVpButton;
        [SerializeField] UINavigator navigator;

        [Header("Profile")]
        [SerializeField] TextMeshProUGUI playerNameLabel;
        [SerializeField] TextMeshProUGUI onlineCountLabel;
        [SerializeField] TextMeshProUGUI statsSummaryLabel;
        [SerializeField] Slider progressionBar;

        [Header("Daily")]
        [SerializeField] Button dailyRewardButton;
        [SerializeField] DailyRewardPopup dailyRewardPopup;

        void Awake()
        {
            if (openCaseButton != null) openCaseButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.CaseOpening));
            if (inventoryButton != null) inventoryButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.Inventory));
            if (shopButton != null) shopButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.Shop));
            if (settingsButton != null) settingsButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.Settings));
            if (weaponsButton != null) weaponsButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.Weapons));
            if (upgradeButton != null) upgradeButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.Upgrade));
            // ScreenType.CaseBattle legacy screen is retired. The button now goes
            // directly to the new Lobby flow (same destination as the BATTLE tab).
            if (caseBattleButton != null) caseBattleButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.CaseBattleLobby));
            if (earnVpButton     != null) earnVpButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.EarnVp));
            if (dailyRewardButton != null) dailyRewardButton.onClick.AddListener(() => dailyRewardPopup?.TryShow());
        }

        protected override void OnShown()
        {
            Refresh();
            GameEvents.OnStatisticsChanged += Refresh;
        }

        protected override void OnHidden() => GameEvents.OnStatisticsChanged -= Refresh;

        void Refresh()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Save == null) return;

            if (playerNameLabel != null)
                playerNameLabel.text = ctx.Save.Data.playerName;

            if (ctx.FakeOnline != null)
            {
                ctx.FakeOnline.Refresh();
                if (onlineCountLabel != null)
                    onlineCountLabel.text = $"{ctx.FakeOnline.CurrentOnlineCount:N0} agents online";
            }

            if (ctx.Statistics != null && statsSummaryLabel != null)
            {
                var stats = ctx.Statistics.Data;
                statsSummaryLabel.text =
                    $"Cases: {stats.totalCasesOpened}  |  Skins: {stats.totalSkinsOwned}  |  Spent: {stats.totalVpSpent:N0} VP";
            }

            if (ctx.CaseProgression != null && progressionBar != null)
                progressionBar.value = ctx.CaseProgression.TierProgress01;
        }
    }
}
