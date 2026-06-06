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

        // Battle stats
        public int   battleTotal;
        public int   battleWins;
        public int   battleLosses;
        public float battleWinRate;
        public int   battleEarnings;
        public int   battleStreak;
        public int   battleBestStreak;
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
    public class MissionProgressEntry
    {
        public int  missionIndex;
        public int  currentAmount;
        public bool claimed;
        public int  claimOrder;
    }

    [Serializable]
    public class WeeklyMissionsSave
    {
        public List<MissionProgressEntry> missions = new();
    }

    [Serializable]
    public class SaveDataRoot
    {
        public int version = 1;
        public string playerName = "Agent";
        public int vpBalance;
        public bool adminVpGrantApplied;   // true after the one-time 500 000 VP grant
        public List<OwnedSkinSaveEntry> inventory = new();
        public PlayerStatisticsSave statistics = new();
        public DailyRewardSave dailyReward = new();
        public ShopSave shop = new();
        public CaseProgressSave caseProgress = new();
        public WeeklyMissionsSave weeklyMissions = new();
        public long createdUnix;
        public long lastSaveUnix;
    }
}
