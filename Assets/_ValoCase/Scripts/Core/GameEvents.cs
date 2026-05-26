using System;
using ValoCase.Data;

namespace ValoCase.Core
{
    public static class GameEvents
    {
        public static event Action<int, int> OnVpChanged;
        public static event Action<SkinDefinitionSO> OnSkinObtained;
        public static event Action<SkinDefinitionSO, int> OnSkinSold;
        public static event Action<CaseDefinitionSO, SkinDefinitionSO> OnCaseOpened;
        public static event Action OnInventoryChanged;
        public static event Action OnStatisticsChanged;
        public static event Action OnDailyRewardClaimed;
        public static event Action OnShopRotated;
        public static event Action<string> OnToastRequested;

        public static void RaiseVpChanged(int previous, int current) => OnVpChanged?.Invoke(previous, current);
        public static void RaiseSkinObtained(SkinDefinitionSO skin) => OnSkinObtained?.Invoke(skin);
        public static void RaiseSkinSold(SkinDefinitionSO skin, int vpGained) => OnSkinSold?.Invoke(skin, vpGained);
        public static void RaiseCaseOpened(CaseDefinitionSO caseDef, SkinDefinitionSO skin) => OnCaseOpened?.Invoke(caseDef, skin);
        public static void RaiseInventoryChanged() => OnInventoryChanged?.Invoke();
        public static void RaiseStatisticsChanged() => OnStatisticsChanged?.Invoke();
        public static void RaiseDailyRewardClaimed() => OnDailyRewardClaimed?.Invoke();
        public static void RaiseShopRotated() => OnShopRotated?.Invoke();
        public static void RaiseToast(string message) => OnToastRequested?.Invoke(message);

        public static void ClearAll()
        {
            OnVpChanged = null;
            OnSkinObtained = null;
            OnSkinSold = null;
            OnCaseOpened = null;
            OnInventoryChanged = null;
            OnStatisticsChanged = null;
            OnDailyRewardClaimed = null;
            OnShopRotated = null;
            OnToastRequested = null;
        }
    }
}
