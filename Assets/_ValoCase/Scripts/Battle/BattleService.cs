using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services;
using ValoCase.Services.Backend;

namespace ValoCase.Battle
{
    /// <summary>
    /// Outcome of a battle-start request. Mirrors the observable results the old
    /// WaitingRoomScreen.StartBattle produced, plus a BackendError for the async path.
    /// </summary>
    public enum BattleStartStatus
    {
        Success,
        InvalidConfig,
        InsufficientFunds,
        ServiceUnavailable,
        BackendError
    }

    public sealed class BattleStartResult
    {
        public BattleStartStatus Status;
        public BattleResult Battle;   // non-null only on Success
        public int EntryCost;
        public string Message;        // safe, player-facing Turkish message on failure (optional)

        public bool IsSuccess => Status == BattleStartStatus.Success;

        public static BattleStartResult Ok(BattleResult battle, int cost)
            => new BattleStartResult { Status = BattleStartStatus.Success, Battle = battle, EntryCost = cost };
        public static BattleStartResult Invalid()
            => new BattleStartResult { Status = BattleStartStatus.InvalidConfig };
        public static BattleStartResult Insufficient()
            => new BattleStartResult { Status = BattleStartStatus.InsufficientFunds };
        public static BattleStartResult Unavailable()
            => new BattleStartResult { Status = BattleStartStatus.ServiceUnavailable };
        public static BattleStartResult BackendFailed(string message = null)
            => new BattleStartResult { Status = BattleStartStatus.BackendError, Message = message };
    }

    /// <summary>
    /// Battle authority seam. The local fallback resolves synchronously via
    /// <see cref="TryStartBattle"/>; the backend path is asynchronous via
    /// <see cref="BeginBattle"/> (the screen drives the returned coroutine).
    /// <see cref="IsAsync"/> tells the screen which to use; <see cref="Settle"/> finalizes
    /// rewards/stats once after the animation.
    /// </summary>
    public interface IBattleService
    {
        bool IsAsync { get; }
        BattleStartResult TryStartBattle(BattleLobbyData lobby);
        IEnumerator BeginBattle(BattleLobbyData lobby, Action<BattleStartResult> onResult);
        void Settle(BattleResult battle);
    }

    /// <summary>
    /// Local (offline / bot) battle authority. Holds the exact economy/result/reward
    /// logic that previously lived inside WaitingRoomScreen, with identical ordering,
    /// pricing, RNG, winner rule, rewards, statistics, and save behavior.
    /// </summary>
    public sealed class LocalBattleService : IBattleService
    {
        readonly IVpCurrencyService  _vp;
        readonly IInventoryService   _inventory;
        readonly IStatisticsService  _stats;
        readonly ISaveService        _save;
        readonly ContentDatabaseSO   _content;
        readonly ICaseOpeningService _rng;

        bool _rewardsDistributed;
        bool _statsRecorded;

        public bool IsAsync => false;

        public LocalBattleService(IVpCurrencyService vp, IInventoryService inventory,
                                  IStatisticsService stats, ISaveService save,
                                  ContentDatabaseSO content, ICaseOpeningService rng)
        {
            _vp        = vp;
            _inventory = inventory;
            _stats     = stats;
            _save      = save;
            _content   = content;
            _rng       = rng;
        }

        public BattleStartResult TryStartBattle(BattleLobbyData lobby)
        {
            // 1) Generate the full result first — same engine, same RNG, same order as
            //    the previous WaitingRoomScreen.StartBattle (generate BEFORE charging).
            var engine = new BattleOpeningEngine(_content, _rng);
            var battle = engine.Generate(lobby);
            if (battle == null || !battle.IsValid)
                return BattleStartResult.Invalid();

            // 2) Charge the entry cost exactly once (== lobby.WagerVP).
            int cost = lobby != null ? lobby.WagerVP : 0;
            if (cost > 0)
            {
                if (_vp == null) return BattleStartResult.Unavailable();
                if (!_vp.TrySpend(cost)) return BattleStartResult.Insufficient();
                _save?.Save();
            }

            _rewardsDistributed = false;
            _statsRecorded = false;

            return BattleStartResult.Ok(battle, cost);
        }

        // Async wrapper so the screen can drive both paths uniformly when needed.
        // Completes immediately (no network) — local timing is unchanged.
        public IEnumerator BeginBattle(BattleLobbyData lobby, Action<BattleStartResult> onResult)
        {
            onResult?.Invoke(TryStartBattle(lobby));
            yield break;
        }

        public void Settle(BattleResult battle)
        {
            DistributeRewards(battle);
            RecordStatistics(battle);
        }

        void DistributeRewards(BattleResult battle)
        {
            if (_rewardsDistributed) return;
            if (battle == null || !battle.UserWon) return;
            if (_inventory == null) return;

            foreach (var skin in battle.AllSkins)
                _inventory.AddSkin(skin, out _, grantDuplicateBonus: false);

            _rewardsDistributed = true;
            _save?.Save();
        }

        void RecordStatistics(BattleResult battle)
        {
            if (_statsRecorded) return;
            if (battle == null || _stats == null) return;

            var outcome  = battle.UserWon ? BattleOutcome.PlayerWins : BattleOutcome.OpponentWins;
            int earnings = battle.UserWon ? battle.TotalPotVp - BattleStatsRecorder.UserTotalVp(battle) : 0;

            _stats.RecordBattleResult(outcome, earnings);
            _statsRecorded = true;
            _save?.Save();
        }
    }

    /// <summary>
    /// Server-authoritative bot battle. The backend charges the entry cost, rolls all
    /// rounds, decides the winner, grants rewards, and updates inventory. Unity sends the
    /// request, applies the authoritative wallet, maps the response into the existing
    /// <see cref="BattleResult"/> to drive the unchanged animation, and on settle records
    /// local stats (for the stats UI) and resyncs inventory so granted skins arrive as
    /// real backend instances. It never spends VP or grants skins locally.
    /// </summary>
    public sealed class BackendBattleService : IBattleService
    {
        readonly GameContext _ctx;
        bool _settled;

        public bool IsAsync => true;

        public BackendBattleService(GameContext ctx) { _ctx = ctx; }

        // Not used in backend mode (the screen calls BeginBattle); present to satisfy
        // the seam and to fail safe if ever called.
        public BattleStartResult TryStartBattle(BattleLobbyData lobby)
            => BattleStartResult.Unavailable();

        public IEnumerator BeginBattle(BattleLobbyData lobby, Action<BattleStartResult> onResult)
        {
            if (_ctx == null || !_ctx.BackendReady)
            {
                onResult?.Invoke(BattleStartResult.BackendFailed("Sunucu kullanılamıyor."));
                yield break;
            }

            // Offline pre-check: no entry-cost spend, no battle creation when offline.
            if (BackendErrorMapper.IsOffline)
            {
                onResult?.Invoke(BattleStartResult.BackendFailed(BackendErrorMapper.Offline));
                yield break;
            }

            // Resolve the case the lobby refers to (reuses the engine's resolver) so we
            // send the backend the canonical caseId.
            var caseDef = new BattleOpeningEngine(_ctx.Content, _ctx.CaseOpening).ResolveCase(lobby);
            if (caseDef == null)
            {
                onResult?.Invoke(BattleStartResult.Invalid());
                yield break;
            }

            int rounds = Mathf.Max(1, lobby != null ? lobby.Rounds : 1);
            int participantCount = Mathf.Clamp(lobby != null ? lobby.MaxPlayers : 2, 2, 4);

            BotBattleResponse response = null;
            BackendError error = null;
            yield return _ctx.Backend.CreateBotBattle(caseDef.CaseId, rounds, participantCount,
                r => response = r,
                e => error = e);

            if (error != null || response == null)
            {
                Debug.LogWarning("[Backend] Bot battle failed — " + (error?.ToString() ?? "null response"));
                onResult?.Invoke(BattleStartResult.BackendFailed(BackendErrorMapper.Map(error)));
                yield break;
            }

            // Authoritative wallet immediately (req. 6). No local spend.
            _ctx.ApplyBackendWallet(response.newVpBalance);

            var battle = BuildBattleResult(response, caseDef);
            if (battle == null || !battle.IsValid)
            {
                // Committed server-side but unmappable locally — reconcile, surface safe error.
                Debug.LogError("[Backend] Bot battle result not mappable locally — resyncing.");
                _ctx.RequestBackendResync();
                onResult?.Invoke(BattleStartResult.BackendFailed());
                yield break;
            }

            _settled = false;
            Debug.Log($"[Backend] Bot battle OK — battleId={response.battleId} userWon={response.userWon} " +
                      $"winnerIndex={response.winnerIndex} entryCost={response.entryCost} newBalance={response.newVpBalance}");
            onResult?.Invoke(BattleStartResult.Ok(battle, response.entryCost));
        }

        public void Settle(BattleResult battle)
        {
            if (_settled) return;
            _settled = true;

            // Backend already spent VP + granted rewards. Locally: record stats for the
            // stats UI (display only) and resync inventory so granted skins arrive as
            // backend instances. No local grant, no local spend.
            BattleStatsRecorder.Record(_ctx?.Statistics, _ctx?.Save, battle);
            _ctx?.RequestBackendResync();
        }

        // Maps the backend battle response into the existing BattleResult so the reels,
        // result cards, totals, winner highlight, and win/lose popup all run unchanged.
        BattleResult BuildBattleResult(BotBattleResponse r, CaseDefinitionSO caseDef)
        {
            var battle = new BattleResult
            {
                Case        = caseDef,
                Rounds      = Mathf.Max(1, r.rounds),
                WinnerIndex = r.winnerIndex,
                UserWon     = r.userWon,
                ReelPool    = BuildReelPool(caseDef),   // cosmetic filler, same as local
            };

            if (r.participants == null) return battle;

            // Order by index so Players[0] == user, matching the staged panels.
            var ordered = r.participants.Where(p => p != null).OrderBy(p => p.index).ToList();
            battle.PlayerCount = ordered.Count;

            int pot = 0;
            foreach (var p in ordered)
            {
                var pr = new BattlePlayerResult
                {
                    Name     = p.name,
                    IsUser   = p.isUser,
                    IsWinner = p.index == r.winnerIndex,
                    TotalVp  = p.totalVp,   // authoritative; do not recompute
                };

                if (p.rounds != null)
                {
                    foreach (var rd in p.rounds)
                    {
                        if (rd == null || string.IsNullOrEmpty(rd.skinId)) continue;
                        var skin = _ctx.Content != null ? _ctx.Content.GetSkin(rd.skinId) : null;
                        if (skin == null)
                        {
                            Debug.LogWarning("[Backend] Battle skinId not in local catalog: " + rd.skinId);
                            continue;
                        }
                        pr.Skins.Add(skin);
                        battle.AllSkins.Add(skin);
                    }
                }

                pot += p.totalVp;
                battle.Players.Add(pr);
            }

            battle.TotalPotVp = pot;
            return battle;
        }

        // Same reel-pool construction the local engine uses (drop-table skins).
        static List<SkinDefinitionSO> BuildReelPool(CaseDefinitionSO caseDef)
        {
            var pool = new List<SkinDefinitionSO>();
            var table = caseDef != null ? caseDef.DropTable : null;
            if (table == null) return pool;

            foreach (var drop in table.PossibleDrops)
                if (drop != null && drop.skin != null)
                    pool.Add(drop.skin);

            return pool;
        }
    }

    /// <summary>Shared battle-statistics recording (same formula for local and backend).</summary>
    static class BattleStatsRecorder
    {
        public static void Record(IStatisticsService stats, ISaveService save, BattleResult battle)
        {
            if (battle == null || stats == null || battle.Players.Count == 0) return;

            var outcome  = battle.UserWon ? BattleOutcome.PlayerWins : BattleOutcome.OpponentWins;
            int earnings = battle.UserWon ? battle.TotalPotVp - UserTotalVp(battle) : 0;

            stats.RecordBattleResult(outcome, earnings);
            save?.Save();
        }

        public static int UserTotalVp(BattleResult battle)
        {
            if (battle == null || battle.Players.Count == 0) return 0;
            foreach (var player in battle.Players)
                if (player != null && player.IsUser)
                    return player.TotalVp;
            return 0;
        }
    }

    /// <summary>
    /// Single construction/swap point for the battle authority. Backend mode is the
    /// default when enabled; otherwise the local service is used as fallback.
    /// </summary>
    public static class BattleServiceFactory
    {
        public static IBattleService Create(GameContext ctx)
        {
            if (ctx == null) return null;

            if (ctx.BackendEnabled)
                return new BackendBattleService(ctx);

            return new LocalBattleService(
                ctx.Vp, ctx.Inventory, ctx.Statistics, ctx.Save, ctx.Content, ctx.CaseOpening);
        }
    }
}
