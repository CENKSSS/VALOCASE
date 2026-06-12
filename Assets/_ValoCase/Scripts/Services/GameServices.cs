using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValoCase.Battle;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Save;

namespace ValoCase.Services
{
    public static class WeightedRandomizer
    {
        public static T Pick<T>(IReadOnlyList<T> items, IReadOnlyList<float> weights, System.Random rng = null)
        {
            if (items == null || items.Count == 0) return default;

            rng ??= new System.Random();
            var total = 0f;
            for (var i = 0; i < weights.Count; i++) total += Mathf.Max(0f, weights[i]);

            if (total <= 0f) return items[UnityEngine.Random.Range(0, items.Count)];

            var roll = (float)rng.NextDouble() * total;
            var cumulative = 0f;
            for (var i = 0; i < items.Count; i++)
            {
                cumulative += Mathf.Max(0f, weights[i]);
                if (roll <= cumulative) return items[i];
            }

            return items[items.Count - 1];
        }
    }

    public sealed class SaveService : ISaveService
    {
        readonly ISaveRepository _repository;
        readonly GameConfigSO _config;

        public SaveDataRoot Data { get; private set; }

        public SaveService(ISaveRepository repository, GameConfigSO config)
        {
            _repository = repository;
            _config = config;
        }

        public void LoadOrCreate()
        {
            if (_repository.TryLoad(out var loaded) && loaded != null)
            {
                Data = loaded;
                return;
            }

            Data = CreateNewSave();
            Save();
        }

        SaveDataRoot CreateNewSave()
        {
            var now = TimeUtil.NowUnix();
            return new SaveDataRoot
            {
                version = 1,
                playerName = "Agent",
                vpBalance = _config != null ? _config.StartingVp : GameConstants.DefaultStartingVp,
                createdUnix = now,
                lastSaveUnix = now
            };
        }

        public void Save()
        {
            if (Data == null) return;
            _repository.Save(Data);
        }

        public void ResetSave()
        {
            _repository.Delete();
            Data = CreateNewSave();
            Save();
        }
    }

    public sealed class VpCurrencyService : IVpCurrencyService
    {
        readonly ISaveService _save;

        public VpCurrencyService(ISaveService save) => _save = save;

        public int Balance => _save.Data.vpBalance;

        public bool CanAfford(int amount) => Balance >= amount;

        public bool TrySpend(int amount)
        {
            if (amount < 0 || !CanAfford(amount)) return false;
            var prev = Balance;
            _save.Data.vpBalance -= amount;
            GameEvents.RaiseVpChanged(prev, Balance);
            return true;
        }

        public void Add(int amount, bool notify = true)
        {
            if (amount <= 0) return;
            var prev = Balance;
            _save.Data.vpBalance += amount;
            if (notify) GameEvents.RaiseVpChanged(prev, Balance);
        }

        public void SetBalance(int amount, bool notify = true)
        {
            var prev = Balance;
            _save.Data.vpBalance = Mathf.Max(0, amount);
            if (notify) GameEvents.RaiseVpChanged(prev, Balance);
        }
    }

    public sealed class InventoryService : IInventoryService
    {
        readonly ISaveService _save;
        readonly ContentDatabaseSO _database;
        readonly GameConfigSO _config;
        readonly IVpCurrencyService _vp;

        public InventoryService(ISaveService save, ContentDatabaseSO database, GameConfigSO config, IVpCurrencyService vp)
        {
            _save = save;
            _database = database;
            _config = config;
            _vp = vp;
        }

        public IReadOnlyList<OwnedSkinSaveEntry> Items => _save.Data.inventory;

        public int UniqueCount => _save.Data.inventory.Count;

        public int TotalCount => _save.Data.inventory.Sum(i => i.quantity);

        public int InventoryValue
        {
            get
            {
                var total = 0;
                foreach (var entry in _save.Data.inventory)
                {
                    var skin = _database.GetSkin(entry.skinId);
                    if (skin != null) total += skin.VpValue * entry.quantity;
                }
                return total;
            }
        }

        public bool Owns(string skinId) => GetQuantity(skinId) > 0;

        public int GetQuantity(string skinId)
        {
            var entry = _save.Data.inventory.Find(i => i.skinId == skinId);
            return entry?.quantity ?? 0;
        }

        public void AddSkin(SkinDefinitionSO skin, out bool isDuplicate, bool grantDuplicateBonus = true)
        {
            isDuplicate = false;
            if (skin == null) return;

            var entry = _save.Data.inventory.Find(i => i.skinId == skin.SkinId);
            if (entry == null)
            {
                _save.Data.inventory.Add(new OwnedSkinSaveEntry
                {
                    skinId = skin.SkinId,
                    quantity = 1,
                    obtainedUnix = TimeUtil.NowUnix()
                });
            }
            else
            {
                entry.quantity++;
                isDuplicate = true;
                if (grantDuplicateBonus)
                {
                    var bonus = Mathf.RoundToInt(skin.VpValue * (_config.DuplicateBonusPercent / 100f));
                    if (bonus > 0) _vp.Add(bonus);
                }
            }

            GameEvents.RaiseSkinObtained(skin);
            GameEvents.RaiseInventoryChanged();
        }

        public bool TrySell(string skinId, out int vpGained)
        {
            vpGained = 0;
            var entry = _save.Data.inventory.Find(i => i.skinId == skinId);
            if (entry == null || entry.quantity <= 0) return false;

            var skin = _database.GetSkin(skinId);
            if (skin == null) return false;

            vpGained = Mathf.RoundToInt(skin.VpValue * _config.SellMultiplier);
            entry.quantity--;
            if (entry.quantity <= 0) _save.Data.inventory.Remove(entry);

            _vp.Add(vpGained);
            GameEvents.RaiseSkinSold(skin, vpGained);
            GameEvents.RaiseInventoryChanged();
            return true;
        }

        // Consumes one unit without granting VP. Used by the upgrade/gamble flow.
        // Returns false if the skin isn't owned.
        public bool ConsumeOne(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return false;
            var entry = _save.Data.inventory.Find(i => i.skinId == skinId);
            if (entry == null || entry.quantity <= 0) return false;

            entry.quantity--;
            if (entry.quantity <= 0) _save.Data.inventory.Remove(entry);

            GameEvents.RaiseInventoryChanged();
            return true;
        }

        public List<OwnedSkinSaveEntry> GetFilteredSorted(SkinFilterMode filter, InventorySortMode sort)
        {
            IEnumerable<OwnedSkinSaveEntry> query = _save.Data.inventory;

            query = filter switch
            {
                SkinFilterMode.DuplicatesOnly => query.Where(e => e.quantity > 1),
                SkinFilterMode.All => query,
                _ => query.Where(e =>
                {
                    var skin = _database.GetSkin(e.skinId);
                    return skin != null && MatchesRarityFilter(skin.Rarity, filter);
                })
            };

            var list = query.ToList();
            list.Sort((a, b) => CompareEntries(a, b, sort));
            return list;
        }

        static bool MatchesRarityFilter(SkinRarity rarity, SkinFilterMode filter) =>
            filter switch
            {
                SkinFilterMode.Select => rarity == SkinRarity.Select,
                SkinFilterMode.Deluxe => rarity == SkinRarity.Deluxe,
                SkinFilterMode.Premium => rarity == SkinRarity.Premium,
                SkinFilterMode.Exclusive => rarity == SkinRarity.Exclusive,
                SkinFilterMode.Ultra => rarity == SkinRarity.Ultra,
                _ => true
            };

        int CompareEntries(OwnedSkinSaveEntry a, OwnedSkinSaveEntry b, InventorySortMode sort)
        {
            var skinA = _database.GetSkin(a.skinId);
            var skinB = _database.GetSkin(b.skinId);
            if (skinA == null || skinB == null) return 0;

            return sort switch
            {
                InventorySortMode.RarityDesc => skinB.Rarity.CompareTo(skinA.Rarity),
                InventorySortMode.RarityAsc => skinA.Rarity.CompareTo(skinB.Rarity),
                InventorySortMode.ValueDesc => skinB.VpValue.CompareTo(skinA.VpValue),
                InventorySortMode.ValueAsc => skinA.VpValue.CompareTo(skinB.VpValue),
                InventorySortMode.NameAsc => string.Compare(skinA.SkinName, skinB.SkinName, StringComparison.Ordinal),
                InventorySortMode.WeaponAsc => string.Compare(skinA.WeaponName, skinB.WeaponName, StringComparison.Ordinal),
                InventorySortMode.Newest => b.obtainedUnix.CompareTo(a.obtainedUnix),
                _ => 0
            };
        }
    }

    public sealed class StatisticsService : IStatisticsService
    {
        readonly ISaveService _save;

        public StatisticsService(ISaveService save, ContentDatabaseSO database)
        {
            _save = save;
            _ = database;
        }

        public PlayerStatisticsSave Data => _save.Data.statistics;

        public void RecordCaseOpened(CaseDefinitionSO caseDef, SkinDefinitionSO skin, int vpSpent)
        {
            var stats = Data;
            stats.totalCasesOpened++;
            stats.totalVpSpent += vpSpent;

            var entry = stats.casesOpenedById.Find(c => c.caseId == caseDef.CaseId);
            if (entry == null)
            {
                entry = new CaseOpenCountEntry { caseId = caseDef.CaseId, count = 0 };
                stats.casesOpenedById.Add(entry);
            }
            entry.count++;

            if (skin != null)
            {
                if (string.IsNullOrEmpty(stats.rarestSkinId) || (int)skin.Rarity > stats.rarestSkinRarity)
                {
                    stats.rarestSkinId = skin.SkinId;
                    stats.rarestSkinRarity = (int)skin.Rarity;
                }
            }

            GameEvents.RaiseStatisticsChanged();
        }

        public void RecordVpEarned(int amount)
        {
            Data.totalVpEarned += amount;
            GameEvents.RaiseStatisticsChanged();
        }

        public void RecalculateInventoryStats(IInventoryService inventory, ContentDatabaseSO database, bool notify = true)
        {
            _ = database;
            Data.totalSkinsOwned = inventory.TotalCount;
            Data.inventoryValue = inventory.InventoryValue;
            if (notify)
                GameEvents.RaiseStatisticsChanged();
        }

        public void RecordBattleResult(BattleOutcome outcome, int earningsVp)
        {
            var stats = Data;
            stats.battleTotal++;

            if (outcome == BattleOutcome.PlayerWins)
            {
                stats.battleWins++;
                stats.battleStreak++;
                if (stats.battleStreak > stats.battleBestStreak)
                    stats.battleBestStreak = stats.battleStreak;
                stats.battleEarnings += earningsVp;
            }
            else
            {
                stats.battleLosses++;
                stats.battleStreak = 0;
            }

            stats.battleWinRate = stats.battleTotal > 0
                ? stats.battleWins * 100f / stats.battleTotal
                : 0f;

            GameEvents.RaiseStatisticsChanged();
        }
    }

    public sealed class CaseProgressionService : ICaseProgressionService
    {
        readonly ISaveService _save;
        readonly GameConfigSO _config;

        public CaseProgressionService(ISaveService save, GameConfigSO config)
        {
            _save = save;
            _config = config;
        }

        CaseProgressSave State => _save.Data.caseProgress;

        public int ProgressionTier => State.progressionTier;

        public float TierProgress01
        {
            get
            {
                var opened = _save.Data.statistics.totalCasesOpened;
                var perTier = _config.CasesOpenedPerUnlockTier;
                if (perTier <= 0) return 1f;
                var mod = opened % perTier;
                return mod / (float)perTier;
            }
        }

        public bool IsCaseUnlocked(CaseDefinitionSO caseDef)
        {
            if (caseDef == null) return false;
            if (State.unlockedCaseIds.Contains(caseDef.CaseId)) return true;

            return caseDef.UnlockType switch
            {
                CaseUnlockType.Available => true,
                CaseUnlockType.Level => ProgressionTier >= caseDef.UnlockRequirement,
                CaseUnlockType.Achievement => _save.Data.statistics.totalCasesOpened >= caseDef.UnlockRequirement,
                _ => true
            };
        }

        public void OnCaseOpened()
        {
            var perTier = _config.CasesOpenedPerUnlockTier;
            var opened = _save.Data.statistics.totalCasesOpened;
            if (perTier > 0 && opened > 0 && opened % perTier == 0)
                State.progressionTier++;
        }
    }

    public sealed class ShopService : IShopService
    {
        readonly ISaveService _save;
        readonly ContentDatabaseSO _database;
        readonly GameConfigSO _config;

        readonly List<CaseDefinitionSO> _featured = new();
        readonly List<CaseDefinitionSO> _deals = new();

        public ShopService(ISaveService save, ContentDatabaseSO database, GameConfigSO config)
        {
            _save = save;
            _database = database;
            _config = config;
        }

        public IReadOnlyList<CaseDefinitionSO> FeaturedCases => _featured;
        public IReadOnlyList<CaseDefinitionSO> DailyDeals => _deals;
        public IReadOnlyList<CaseDefinitionSO> AllPurchasableCases => _database.Cases;

        public void EnsureRotation(bool notify = true)
        {
            var now = TimeUtil.NowUnix();
            var rotationSeconds = _config.ShopRotationHours * 3600L;
            var seed = _save.Data.shop.rotationSeedUnix;
            if (seed > 0 && now - seed < rotationSeconds && _featured.Count > 0) return;

            RotateShop(now, notify);
        }

        void RotateShop(long nowUnix, bool notify)
        {
            _featured.Clear();
            _deals.Clear();

            var all = _database.Cases.Where(c => c != null).ToList();
            var rng = new System.Random((int)(nowUnix % int.MaxValue));

            var featuredPool = all.Where(c => c.IsFeatured).ToList();
            if (featuredPool.Count == 0) featuredPool = all;

            PickUnique(featuredPool, _config.FeaturedCaseSlots, _featured, rng);
            PickUnique(all, _config.DailyDealSlots, _deals, rng);

            _save.Data.shop.rotationSeedUnix = nowUnix;
            _save.Data.shop.featuredCaseIds = _featured.Select(c => c.CaseId).ToList();
            _save.Data.shop.dailyDealCaseIds = _deals.Select(c => c.CaseId).ToList();

            if (notify)
                GameEvents.RaiseShopRotated();
        }

        static void PickUnique(List<CaseDefinitionSO> pool, int count, List<CaseDefinitionSO> target, System.Random rng)
        {
            var copy = pool.ToList();
            for (var i = 0; i < count && copy.Count > 0; i++)
            {
                var idx = rng.Next(copy.Count);
                target.Add(copy[idx]);
                copy.RemoveAt(idx);
            }
        }

        public int GetDiscountedPrice(CaseDefinitionSO caseDef)
        {
            if (caseDef == null) return 0;
            if (_deals.Contains(caseDef)) return Mathf.RoundToInt(caseDef.VpPrice * 0.85f);
            return caseDef.VpPrice;
        }
    }

    public sealed class DailyRewardService : IDailyRewardService
    {
        readonly ISaveService _save;
        readonly GameConfigSO _config;
        readonly IVpCurrencyService _vp;

        public DailyRewardService(ISaveService save, GameConfigSO config, IVpCurrencyService vp)
        {
            _save = save;
            _config = config;
            _vp = vp;
        }

        DailyRewardSave State => _save.Data.dailyReward;

        public int CurrentStreak => State.currentStreak;

        public bool CanClaimToday
        {
            get
            {
                if (State.lastClaimUnix <= 0) return true;
                return !TimeUtil.IsSameUtcDay(State.lastClaimUnix, TimeUtil.NowUnix());
            }
        }

        public int PeekTodayReward()
        {
            var rewards = _config.DailyVpStreakRewards;
            if (rewards == null || rewards.Count == 0) return 150;
            return rewards[RewardIndexForClaim()];
        }

        public bool TryClaim(out int vpReward)
        {
            vpReward = 0;
            if (!CanClaimToday) return false;

            var now = TimeUtil.NowUnix();
            if (State.lastClaimUnix > 0)
            {
                var yesterday = TimeUtil.FromUnix(now).AddDays(-1).Date;
                var last = TimeUtil.FromUnix(State.lastClaimUnix).Date;
                State.currentStreak = last == yesterday ? State.currentStreak + 1 : 1;
            }
            else
            {
                State.currentStreak = 1;
            }

            vpReward = PeekTodayReward();
            State.lastClaimUnix = now;
            State.totalClaims++;
            _vp.Add(vpReward);
            GameEvents.RaiseDailyRewardClaimed();
            return true;
        }

        public TimeSpan TimeUntilNextClaim =>
            CanClaimToday ? TimeSpan.Zero : TimeUtil.TimeUntilNextUtcDay();

        int RewardIndexForClaim()
        {
            var max = _config.DailyVpStreakRewards.Count - 1;
            return Mathf.Clamp(State.currentStreak - 1, 0, max);
        }
    }

    public sealed class FakeOnlineCountService : IFakeOnlineService
    {
        readonly GameConfigSO _config;
        int _current;

        public FakeOnlineCountService(GameConfigSO config) => _config = config;

        public int CurrentOnlineCount => _current;

        public void Refresh()
        {
            _current = UnityEngine.Random.Range(_config.OnlineCountMin, _config.OnlineCountMax + 1);
        }
    }

    public sealed class CaseOpeningService : ICaseOpeningService
    {
        readonly IVpCurrencyService _vp;
        readonly IInventoryService _inventory;
        readonly IStatisticsService _statistics;
        readonly ICaseProgressionService _progression;
        readonly IShopService _shop;

        public CaseOpeningService(
            IVpCurrencyService vp,
            IInventoryService inventory,
            IStatisticsService statistics,
            ICaseProgressionService progression,
            IShopService shop)
        {
            _vp = vp;
            _inventory = inventory;
            _statistics = statistics;
            _progression = progression;
            _shop = shop;
        }

        public bool CanOpen(CaseDefinitionSO caseDef)
        {
            if (caseDef == null || caseDef.DropTable == null) return false;
            if (!_progression.IsCaseUnlocked(caseDef)) return false;
            return _vp.CanAfford(_shop.GetDiscountedPrice(caseDef));
        }

        public bool TryBeginOpen(CaseDefinitionSO caseDef, out SkinDefinitionSO rolled, out int vpSpent)
        {
            rolled = null;
            vpSpent = 0;
            if (!CanOpen(caseDef)) return false;

            vpSpent = _shop.GetDiscountedPrice(caseDef);
            if (!_vp.TrySpend(vpSpent)) return false;

            rolled = RollSkin(caseDef);
            if (rolled == null)
            {
                _vp.Add(vpSpent);
                vpSpent = 0;
                return false;
            }

            return true;
        }

        public void CompleteOpen(CaseDefinitionSO caseDef, SkinDefinitionSO skin, int vpSpent)
        {
            if (caseDef == null || skin == null) return;

            _inventory.AddSkin(skin, out _);
            _statistics.RecordCaseOpened(caseDef, skin, vpSpent);
            _progression.OnCaseOpened();
            GameEvents.RaiseCaseOpened(caseDef, skin);
        }

        public bool TryOpenCaseInstant(CaseDefinitionSO caseDef, out SkinDefinitionSO result)
        {
            if (!TryBeginOpen(caseDef, out result, out var spent)) return false;
            CompleteOpen(caseDef, result, spent);
            return true;
        }

        public SkinDefinitionSO RollSkin(CaseDefinitionSO caseDef)
        {
            var table = caseDef?.DropTable;
            if (table == null || table.PossibleDrops.Count == 0) return null;

            var rarity = RollRarity(table);
            var pool = table.PossibleDrops
                .Where(d => d.skin != null && d.skin.Rarity == rarity)
                .ToList();

            if (pool.Count == 0)
                pool = table.PossibleDrops.Where(d => d.skin != null).ToList();

            if (pool.Count == 0) return null;

            var skins = pool.Select(p => p.skin).ToList();
            var weights = pool.Select(p => p.skinWeightOverride > 0 ? p.skinWeightOverride : 1f).ToList();
            return WeightedRandomizer.Pick(skins, weights);
        }

        static SkinRarity RollRarity(CaseDropTableSO table)
        {
            var entries = table.RarityWeights;
            if (entries == null || entries.Count == 0) return SkinRarity.Select;

            var rarities = entries.Select(e => e.rarity).ToList();
            var weights = entries.Select(e => e.weightPercent).ToList();
            return WeightedRandomizer.Pick(rarities, weights);
        }
    }

    /// <summary>
    /// Concrete upgrade / gamble logic. Pure C#, no MonoBehaviour or scene refs.
    /// Architecture stays compatible with later online PvP / multi-skin upgrades —
    /// the screen calls into this service via IUpgradeService only.
    /// </summary>
    public sealed class UpgradeService : IUpgradeService
    {
        readonly IInventoryService _inventory;
        readonly ContentDatabaseSO _database;
        readonly ISaveService      _save;

        public event System.Action<SkinDefinitionSO, SkinDefinitionSO, bool> OnUpgradeResolved;

        // Chance range is now 0..1 (0 % → 100 %) defined by the rarity formula.
        public float MinChance => 0f;
        public float MaxChance => 1f;

        public UpgradeService(IInventoryService inventory, ContentDatabaseSO database, ISaveService save)
        {
            _inventory = inventory;
            _database  = database;
            _save      = save;
        }

        /// <summary>
        /// Rarity-based chance via RaritySystem.ComputeChance.
        /// Same rarity = 100 %, each rank step up costs 20 %.
        /// Target lower than input = 0 %.
        /// </summary>
        public float ComputeChance(SkinDefinitionSO input, SkinDefinitionSO target)
        {
            if (input == null || target == null) return 0f;
            return RaritySystem.ComputeChance(input.Rarity, target.Rarity);
        }

        /// <summary>
        /// Returns all skins whose rarity rank is ≥ the input's rank (excluding input itself).
        /// Sorted by rank ascending so cheapest / easiest targets appear first.
        /// </summary>
        public List<SkinDefinitionSO> GetEligibleTargets(SkinDefinitionSO input)
        {
            var result = new List<SkinDefinitionSO>();
            if (input == null || _database == null) return result;

            foreach (var s in _database.Skins)
            {
                if (s == null) continue;
                if (s.SkinId == input.SkinId) continue;
                if (!RaritySystem.IsEligibleTarget(input.Rarity, s.Rarity)) continue;
                result.Add(s);
            }
            result.Sort((a, b) =>
                RaritySystem.GetRank(a.Rarity).CompareTo(RaritySystem.GetRank(b.Rarity)));
            return result;
        }

        public bool TryUpgrade(SkinDefinitionSO input, SkinDefinitionSO target, out bool success)
        {
            success = false;
            if (input == null || target == null) return false;
            if (_inventory == null) return false;
            if (!_inventory.Owns(input.SkinId)) return false;

            // Target must be same rank or higher; lower-rank upgrades are blocked.
            if (!RaritySystem.IsEligibleTarget(input.Rarity, target.Rarity)) return false;

            var chance = ComputeChance(input, target);
            success = UnityEngine.Random.value < chance;

            // Input is ALWAYS consumed — success and failure alike.
            if (!_inventory.ConsumeOne(input.SkinId))
            {
                success = false;
                return false;
            }

            if (success)
                _inventory.AddSkin(target, out _);

            _save?.Save();
            OnUpgradeResolved?.Invoke(input, target, success);
            return true;
        }

        // ── Value-based (multi-skin) upgrade ──────────────────────────────────
        // The screen now drives upgrades by combined VP value instead of rarity.
        // The single-skin TryUpgrade above is left intact for any legacy callers.

        /// <summary>
        /// Pure value ratio: chance = totalInputValue / targetValue, clamped to 0..1.
        /// More expensive targets yield a lower chance; a target equal to the input
        /// value would be 100 %. With the screen's 1.5× minimum-target rule the
        /// realistic ceiling is ~0.67.
        /// </summary>
        public float ComputeValueChance(int totalInputValue, int targetValue)
        {
            if (totalInputValue <= 0 || targetValue <= 0) return 0f;
            return Mathf.Clamp01((float)totalInputValue / targetValue);
        }

        public bool TryUpgradeMulti(IReadOnlyList<SkinDefinitionSO> inputs, SkinDefinitionSO target, out bool success)
        {
            success = false;
            if (inputs == null || inputs.Count == 0 || target == null || _inventory == null) return false;

            // Tally required units per skin and the combined value.
            var required = new Dictionary<string, int>();
            var totalValue = 0;
            foreach (var s in inputs)
            {
                if (s == null) return false;
                required.TryGetValue(s.SkinId, out var c);
                required[s.SkinId] = c + 1;
                totalValue += s.VpValue;
            }

            // Verify the player actually owns every selected unit.
            foreach (var kv in required)
                if (_inventory.GetQuantity(kv.Key) < kv.Value) return false;

            // Target must satisfy the 1.5× combined-value rule (also enforced in UI).
            if (target.VpValue < totalValue * 1.5f) return false;

            var chance = ComputeValueChance(totalValue, target.VpValue);
            success = UnityEngine.Random.value < chance;

            // Inputs are ALWAYS consumed — success and failure alike.
            foreach (var s in inputs)
            {
                if (!_inventory.ConsumeOne(s.SkinId))
                {
                    // Should not happen after the ownership check; bail defensively.
                    success = false;
                    _save?.Save();
                    return false;
                }
            }

            if (success)
                _inventory.AddSkin(target, out _);

            _save?.Save();
            OnUpgradeResolved?.Invoke(inputs[inputs.Count - 1], target, success);
            return true;
        }
    }

    public static class GameServicesFactory
    {
        public sealed class ServiceBundle
        {
            public ISaveService Save;
            public IVpCurrencyService Vp;
            public IInventoryService Inventory;
            public IStatisticsService Statistics;
            public ICaseProgressionService CaseProgression;
            public IShopService Shop;
            public IDailyRewardService DailyRewards;
            public IFakeOnlineService FakeOnline;
            public ICaseOpeningService CaseOpening;
            public IUpgradeService Upgrade;
        }

        public static ServiceBundle Create(ContentDatabaseSO contentDatabase, GameConfigSO gameConfig)
        {
            var repository = new JsonSaveRepository();
            var save = new SaveService(repository, gameConfig);
            save.LoadOrCreate();

            var vp = new VpCurrencyService(save);
            var inventory = new InventoryService(save, contentDatabase, gameConfig, vp);
            var statistics = new StatisticsService(save, contentDatabase);
            var caseProgression = new CaseProgressionService(save, gameConfig);
            var shop = new ShopService(save, contentDatabase, gameConfig);
            var dailyRewards = new DailyRewardService(save, gameConfig, vp);
            var fakeOnline = new FakeOnlineCountService(gameConfig);
            var caseOpening = new CaseOpeningService(vp, inventory, statistics, caseProgression, shop);
            var upgrade = new UpgradeService(inventory, contentDatabase, save);

            shop.EnsureRotation(notify: false);
            fakeOnline.Refresh();
            statistics.RecalculateInventoryStats(inventory, contentDatabase, notify: false);

            return new ServiceBundle
            {
                Save = save,
                Vp = vp,
                Inventory = inventory,
                Statistics = statistics,
                CaseProgression = caseProgression,
                Shop = shop,
                DailyRewards = dailyRewards,
                FakeOnline = fakeOnline,
                CaseOpening = caseOpening,
                Upgrade = upgrade
            };
        }
    }
}
