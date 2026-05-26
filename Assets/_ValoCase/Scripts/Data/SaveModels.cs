using System;
using System.Collections.Generic;

namespace ValoCase.Save
{
    [Serializable]
    public class OwnedSkinSaveEntry
    {
        public string skinId;
        public int quantity;
        public long obtainedUnix;
    }

    [Serializable]
    public class CaseOpenCountEntry
    {
        public string caseId;
        public int count;
    }

    [Serializable]
    public class PlayerStatisticsSave
    {
        public int totalCasesOpened;
        public int totalVpSpent;
        public int totalVpEarned;
        public int totalSkinsOwned;
        public int inventoryValue;
        public string rarestSkinId;
        public int rarestSkinRarity;
        public List<CaseOpenCountEntry> casesOpenedById = new();
    }

    [Serializable]
    public class DailyRewardSave
    {
        public long lastClaimUnix;
        public int currentStreak;
        public int totalClaims;
    }

    [Serializable]
    public class ShopSave
    {
        public long rotationSeedUnix;
        public List<string> featuredCaseIds = new();
        public List<string> dailyDealCaseIds = new();
    }

    [Serializable]
    public class CaseProgressSave
    {
        public int progressionTier;
        public List<string> unlockedCaseIds = new();
    }

    [Serializable]
    public class SaveDataRoot
    {
        public int version = 1;
        public string playerName = "Agent";
        public int vpBalance;
        public List<OwnedSkinSaveEntry> inventory = new();
        public PlayerStatisticsSave statistics = new();
        public DailyRewardSave dailyReward = new();
        public ShopSave shop = new();
        public CaseProgressSave caseProgress = new();
        public long createdUnix;
        public long lastSaveUnix;
    }
}
