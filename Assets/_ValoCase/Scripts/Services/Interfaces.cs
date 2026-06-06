using System.Collections.Generic;
using ValoCase.Battle;
using ValoCase.Data;
using ValoCase.Save;

namespace ValoCase.Services
{
    public interface IVpCurrencyService
    {
        int Balance { get; }
        bool CanAfford(int amount);
        bool TrySpend(int amount);
        void Add(int amount, bool notify = true);
        void SetBalance(int amount, bool notify = true);
    }

    public interface IInventoryService
    {
        IReadOnlyList<OwnedSkinSaveEntry> Items { get; }
        int UniqueCount { get; }
        int TotalCount { get; }
        int InventoryValue { get; }
        bool Owns(string skinId);
        int GetQuantity(string skinId);
        void AddSkin(SkinDefinitionSO skin, out bool isDuplicate);
        bool TrySell(string skinId, out int vpGained);
        // Removes one unit of a skin from inventory WITHOUT granting VP.
        // Used by gamble / upgrade flows where the input is consumed.
        bool ConsumeOne(string skinId);
        List<OwnedSkinSaveEntry> GetFilteredSorted(SkinFilterMode filter, InventorySortMode sort);
    }

    /// <summary>
    /// Skin upgrade / gamble service.
    /// Modular by design so it can later back online PvP, multi-skin upgrades, etc.
    /// </summary>
    public interface IUpgradeService
    {
        /// <summary>
        /// 0..1 chance of success based on rarity rank difference.
        /// Same rank = 1.0 (100%). Each rank step up costs 0.20. Min = 0.0, Max = 1.0.
        /// </summary>
        float ComputeChance(SkinDefinitionSO input, SkinDefinitionSO target);

        /// <summary>Skins valid as an upgrade target for the given input.</summary>
        List<SkinDefinitionSO> GetEligibleTargets(SkinDefinitionSO input);

        /// <summary>Returns true on resolution (regardless of success/fail).
        /// `success` indicates whether the upgrade succeeded.
        /// Input is consumed in either case; target added on success.</summary>
        bool TryUpgrade(SkinDefinitionSO input, SkinDefinitionSO target, out bool success);

        /// <summary>Hook for animation systems, telemetry, future networking.</summary>
        event System.Action<SkinDefinitionSO, SkinDefinitionSO, bool> OnUpgradeResolved;

        float MinChance { get; }
        float MaxChance { get; }
    }

    public interface ICaseOpeningService
    {
        bool CanOpen(CaseDefinitionSO caseDef);
        bool TryBeginOpen(CaseDefinitionSO caseDef, out SkinDefinitionSO rolled, out int vpSpent);
        void CompleteOpen(CaseDefinitionSO caseDef, SkinDefinitionSO skin, int vpSpent);
        bool TryOpenCaseInstant(CaseDefinitionSO caseDef, out SkinDefinitionSO result);
        SkinDefinitionSO RollSkin(CaseDefinitionSO caseDef);
    }

    public interface IShopService
    {
        IReadOnlyList<CaseDefinitionSO> FeaturedCases { get; }
        IReadOnlyList<CaseDefinitionSO> DailyDeals { get; }
        IReadOnlyList<CaseDefinitionSO> AllPurchasableCases { get; }
        void EnsureRotation(bool notify = true);
        int GetDiscountedPrice(CaseDefinitionSO caseDef);
    }

    public interface IDailyRewardService
    {
        int CurrentStreak { get; }
        bool CanClaimToday { get; }
        int PeekTodayReward();
        bool TryClaim(out int vpReward);
        System.TimeSpan TimeUntilNextClaim { get; }
    }

    public interface IStatisticsService
    {
        PlayerStatisticsSave Data { get; }
        void RecordCaseOpened(CaseDefinitionSO caseDef, SkinDefinitionSO skin, int vpSpent);
        void RecordVpEarned(int amount);
        void RecalculateInventoryStats(IInventoryService inventory, ContentDatabaseSO database, bool notify = true);
        void RecordBattleResult(BattleOutcome outcome, int earningsVp);
    }

    public interface ICaseProgressionService
    {
        bool IsCaseUnlocked(CaseDefinitionSO caseDef);
        int ProgressionTier { get; }
        float TierProgress01 { get; }
        void OnCaseOpened();
    }

    public interface IFakeOnlineService
    {
        int CurrentOnlineCount { get; }
        void Refresh();
    }

    public interface ISaveService
    {
        SaveDataRoot Data { get; }
        void LoadOrCreate();
        void Save();
        void ResetSave();
    }
}
