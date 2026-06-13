using System;
using System.Linq;
using ValoCase.Data;

namespace ValoCase.Services
{
    /// <summary>
    /// Phase-4 economy transaction facade (see <see cref="IEconomyService"/>).
    ///
    /// Composes the existing primitives — <see cref="IVpCurrencyService"/>,
    /// <see cref="IInventoryService"/>, <see cref="IStatisticsService"/> — into complete,
    /// consistent transactions. VP amounts, odds, rewards and the save format are
    /// unchanged; this only centralizes the mutate + record-stat + persist composition
    /// that was previously scattered across UI screens.
    /// </summary>
    public sealed class EconomyService : IEconomyService
    {
        readonly IVpCurrencyService _vp;
        readonly IInventoryService  _inventory;
        readonly IStatisticsService _statistics;
        readonly ISaveService       _save;
        readonly ContentDatabaseSO  _database;

        public EconomyService(IVpCurrencyService vp, IInventoryService inventory,
                              IStatisticsService statistics, ISaveService save,
                              ContentDatabaseSO database)
        {
            _vp         = vp;
            _inventory  = inventory;
            _statistics = statistics;
            _save       = save;
            _database   = database;
        }

        // ── VP rewards ───────────────────────────────────────────────────────
        // Every VP grant now flows through here so it is consistently counted in
        // totalVpEarned (Phase-4 Option A normalization). `source` is for telemetry.
        public void GrantReward(int amount, string source, bool save = true)
        {
            if (amount <= 0) return;
            _vp.Add(amount);
            _statistics?.RecordVpEarned(amount);
            if (save) _save?.Save();
        }

        // ── Sell one unit ────────────────────────────────────────────────────
        public bool SellOne(string skinId, out int vpGained)
        {
            vpGained = 0;
            if (_inventory == null || !_inventory.TrySell(skinId, out vpGained)) return false;

            _statistics?.RecordVpEarned(vpGained);
            _statistics?.RecalculateInventoryStats(_inventory, _database);
            _save?.Save();
            return true;
        }

        // ── Bulk sell (batched single persist) ───────────────────────────────
        // Mirrors the previous InventoryScreen loop exactly: distinct ids, every unit
        // of each matching skin sold, then a single record + recalc + save.
        public int SellMatching(Func<SkinDefinitionSO, bool> match, out int totalVpGained)
        {
            totalVpGained = 0;
            if (_inventory == null) return 0;

            var ids = _inventory.Items
                .Where(e => e != null && !string.IsNullOrEmpty(e.skinId))
                .Select(e => e.skinId)
                .Distinct()
                .ToList();

            var sold = 0;
            foreach (var id in ids)
            {
                var skin = _database != null ? _database.GetSkin(id) : null;
                if (skin == null) continue;
                if (match != null && !match(skin)) continue;

                var qty = _inventory.GetQuantity(id);
                for (var i = 0; i < qty; i++)
                {
                    if (_inventory.TrySell(id, out var gained)) { totalVpGained += gained; sold++; }
                    else break;
                }
            }

            if (sold > 0)
            {
                _statistics?.RecordVpEarned(totalVpGained);
                _statistics?.RecalculateInventoryStats(_inventory, _database);
                _save?.Save();
            }
            return sold;
        }
    }
}
