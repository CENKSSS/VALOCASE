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
        // grantDuplicateBonus: when the skin is already owned, also credit the
        // configured duplicate VP bonus. Case opening leaves this true; Case Battle
        // rewards pass false so skin value is never converted into VP balance.
        void AddSkin(SkinDefinitionSO skin, out bool isDuplicate, bool grantDuplicateBonus = true);
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

        /// <summary>
        /// Value-ratio success chance for a value-based (multi-skin) upgrade.
        /// Falls as the target value grows relative to the combined input value.
        /// 0..1; returns 0 when either value is non-positive.
        /// </summary>
        float ComputeValueChance(int totalInputValue, int targetValue);

        /// <summary>
        /// Resolves a value-based multi-skin upgrade. All inputs are consumed
        /// (success and failure alike); the target is added on success.
        /// Returns true on resolution; `success` indicates the outcome.
        /// </summary>
        bool TryUpgradeMulti(IReadOnlyList<SkinDefinitionSO> inputs, SkinDefinitionSO target, out bool success);

        /// <summary>
        /// Resolves a value-based upgrade with multiple targets. All inputs are consumed
        /// (success and failure alike); every target is added on success. The upgrade
        /// only resolves when the target total VP is strictly greater than the input total.
        /// Returns true on resolution; `success` indicates the outcome.
        /// </summary>
        bool TryUpgradeMultiTarget(IReadOnlyList<SkinDefinitionSO> inputs, IReadOnlyList<SkinDefinitionSO> targets, out bool success);

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

        /// <summary>
        /// Backend-mode completion. The Spring Boot server has ALREADY spent VP and
        /// granted the skin authoritatively; this only updates the local CACHE:
        /// adds the skin WITHOUT the duplicate-VP bonus (no local VP mutation) and
        /// records the same cosmetic stats/progression as the local path. Wallet is
        /// applied separately from the server's authoritative newVpBalance.
        /// </summary>
        void CompleteOpenFromBackend(CaseDefinitionSO caseDef, SkinDefinitionSO skin, int vpSpent);

        bool TryOpenCaseInstant(CaseDefinitionSO caseDef, out SkinDefinitionSO result);
        SkinDefinitionSO RollSkin(CaseDefinitionSO caseDef);
    }

    /// <summary>
    /// Phase-3 result-generation seam. The SINGLE place authoritative/random results
    /// are produced. Today a local implementation reuses the existing odds + RNG; a
    /// future Spring Boot integration replaces ONLY this provider (same call sites,
    /// same ID-based result contracts). Generation is separated here from the
    /// animation/presentation layers, which only consume the results.
    /// </summary>
    public interface IResultProvider
    {
        /// <summary>Roll a single skin from a case's drop table (the shared primitive).</summary>
        SkinDefinitionSO RollSkin(CaseDefinitionSO caseDef);

        /// <summary>Produce a full, ID-based case-opening result (one roll).</summary>
        CaseOpeningResult GenerateCaseOpening(CaseDefinitionSO caseDef, int vpSpent);

        /// <summary>Produce a full, ID-based upgrade result (performs the success roll).</summary>
        UpgradeResult GenerateUpgrade(IReadOnlyList<SkinDefinitionSO> inputs, SkinDefinitionSO target, float chance);
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
        // notify=false records the value without raising OnStatisticsChanged — used when
        // the recording happens inside a larger mutation that refreshes stats once at the end.
        void RecordVpEarned(int amount, bool notify = true);
        void RecalculateInventoryStats(IInventoryService inventory, ContentDatabaseSO database, bool notify = true);
        void RecordBattleResult(BattleOutcome outcome, int earningsVp);
    }

    /// <summary>
    /// Phase-4 economy transaction facade. Composes the existing VP / inventory /
    /// statistics primitives into COMPLETE, consistent transactions (mutate + record
    /// statistic + persist) so callers stop reassembling that composition ad-hoc.
    ///
    /// It does NOT introduce new economy rules, amounts, odds or rewards — it only
    /// centralizes the orchestration. This is the single boundary a future Spring Boot
    /// integration would make server-authoritative.
    /// </summary>
    public interface IEconomyService
    {
        /// <summary>Grant a VP reward: Vp.Add + RecordVpEarned (+ persist unless save=false).
        /// `source` is a free-text tag for telemetry/future backend (e.g. "mission").</summary>
        void GrantReward(int amount, string source, bool save = true);

        /// <summary>Sell one unit: Inventory.TrySell + RecordVpEarned + recalc + persist.</summary>
        bool SellOne(string skinId, out int vpGained);

        /// <summary>Bulk-sell every unit of skins matching <paramref name="match"/>
        /// (null = all). Batched: records + persists ONCE. Returns the units sold.</summary>
        int SellMatching(System.Func<SkinDefinitionSO, bool> match, out int totalVpGained);
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
