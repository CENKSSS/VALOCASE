using System.Collections.Generic;
using UnityEngine;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services;

namespace ValoCase.Battle
{
    /// <summary>
    /// Independent Case Battle results engine.
    ///
    /// Fully separate from the normal case-opening flow: it ONLY reads existing
    /// case/skin data and rolls skins through the existing RNG service. It never
    /// spends VP, never touches inventory, builds no UI and drives no animation.
    /// WaitingRoomScreen calls <see cref="Generate"/> once before any reel spins,
    /// then animates toward the pre-determined results.
    /// </summary>
    public sealed class BattleOpeningEngine
    {
        readonly ContentDatabaseSO   _content;
        readonly ICaseOpeningService _rng;

        public BattleOpeningEngine(ContentDatabaseSO content, ICaseOpeningService rng)
        {
            _content = content;
            _rng     = rng;
        }

        /// <summary>Convenience builder pulling services off the live GameContext.</summary>
        public static BattleOpeningEngine FromGameContext()
        {
            var ctx = GameContext.Instance;
            return new BattleOpeningEngine(ctx != null ? ctx.Content : null,
                                           ctx != null ? ctx.CaseOpening : null);
        }

        // ── Case resolution ───────────────────────────────────────────────────
        /// <summary>
        /// Finds the case the lobby refers to. Tries id match, then display-name
        /// match, then the canonical "vandal_basic", then the first available case.
        /// </summary>
        public CaseDefinitionSO ResolveCase(BattleLobbyData lobby)
        {
            var cases = _content != null ? _content.Cases : null;
            if (cases == null || cases.Count == 0) return null;

            string wanted = lobby != null ? lobby.CaseName : null;

            if (!string.IsNullOrEmpty(wanted))
            {
                foreach (var c in cases)
                    if (c != null && string.Equals(c.CaseId, wanted, System.StringComparison.OrdinalIgnoreCase))
                        return c;

                foreach (var c in cases)
                    if (c != null && !string.IsNullOrEmpty(c.DisplayName) &&
                        string.Equals(c.DisplayName, wanted, System.StringComparison.OrdinalIgnoreCase))
                        return c;
            }

            foreach (var c in cases)
                if (c != null && c.CaseId == "vandal_basic") return c;

            return cases[0];
        }

        // ── Result generation ───────────────────────────────────────────────────
        /// <summary>
        /// Generates the complete battle outcome BEFORE any animation. Each player
        /// gets one rolled skin per round; totals and the winner are decided here.
        /// </summary>
        public BattleResult Generate(BattleLobbyData lobby)
        {
            var result = new BattleResult();
            if (lobby == null) return result;

            var caseDef = ResolveCase(lobby);
            int players = Mathf.Clamp(lobby.MaxPlayers, 2, 4);
            int rounds  = Mathf.Max(1, lobby.Rounds);

            result.Case        = caseDef;
            result.Rounds      = rounds;
            result.PlayerCount = players;
            result.ReelPool    = BuildReelPool(caseDef);

            // Build players (index 0 = YOU, others = BOT n).
            for (int p = 0; p < players; p++)
            {
                result.Players.Add(new BattlePlayerResult
                {
                    Name   = p == 0 ? "YOU" : "BOT " + p,
                    IsUser = p == 0
                });
            }

            // Roll every result up front.
            for (int p = 0; p < players; p++)
            {
                var player = result.Players[p];
                for (int r = 0; r < rounds; r++)
                {
                    var skin = RollOne(caseDef, result.ReelPool);
                    if (skin == null) continue;
                    player.Skins.Add(skin);
                    player.TotalVp += skin.VpValue;
                    result.TotalPotVp += skin.VpValue;
                    result.AllSkins.Add(skin);
                }
            }

            // Winner: highest total VP. Tie → first player who reached that total.
            int bestIdx = 0;
            int bestVp  = result.Players.Count > 0 ? result.Players[0].TotalVp : 0;
            for (int p = 1; p < result.Players.Count; p++)
            {
                if (result.Players[p].TotalVp > bestVp)
                {
                    bestVp  = result.Players[p].TotalVp;
                    bestIdx = p;
                }
            }

            result.WinnerIndex = result.Players.Count > 0 ? bestIdx : -1;
            if (result.WinnerIndex >= 0)
                result.Players[result.WinnerIndex].IsWinner = true;
            result.UserWon = result.WinnerIndex == 0;

            return result;
        }

        // ── Internals ───────────────────────────────────────────────────────────
        SkinDefinitionSO RollOne(CaseDefinitionSO caseDef, IReadOnlyList<SkinDefinitionSO> pool)
        {
            if (_rng != null && caseDef != null)
            {
                var rolled = _rng.RollSkin(caseDef);
                if (rolled != null) return rolled;
            }

            // Fallback only if the RNG/drop-table is unavailable — keeps real data.
            if (pool != null && pool.Count > 0)
                return pool[Random.Range(0, pool.Count)];

            return null;
        }

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

    // ─────────────────────────────────────────────────────────────────────────
    // RESULT MODELS (UI-agnostic)
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class BattlePlayerResult
    {
        public string Name;
        public bool   IsUser;
        public bool   IsWinner;
        public int    TotalVp;
        public readonly List<SkinDefinitionSO> Skins = new List<SkinDefinitionSO>();
    }

    public sealed class BattleResult
    {
        public CaseDefinitionSO Case;
        public int Rounds;
        public int PlayerCount;
        public int WinnerIndex = -1;
        public bool UserWon;
        public int TotalPotVp;

        public readonly List<BattlePlayerResult> Players  = new List<BattlePlayerResult>();
        public readonly List<SkinDefinitionSO>   AllSkins = new List<SkinDefinitionSO>();
        public IReadOnlyList<SkinDefinitionSO>    ReelPool = new List<SkinDefinitionSO>();

        public bool IsValid => Players.Count >= 2 && Rounds >= 1;
    }
}
