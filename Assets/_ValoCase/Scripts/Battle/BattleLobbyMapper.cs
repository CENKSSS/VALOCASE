using System.Collections.Generic;
using UnityEngine;
using ValoCase.Data;
using ValoCase.Services.Backend;

namespace ValoCase.Battle
{
    /// <summary>
    /// Read-only mappers between the backend public-lobby DTOs and the existing
    /// client-side battle models. NOTHING here spends VP, grants skins, rolls RNG, or
    /// decides a winner — the backend already did all of that. These mappers only shape
    /// authoritative server data into the structures the unchanged lobby list, waiting
    /// room summary, and battle reel animation already consume.
    /// </summary>
    public static class BattleLobbyMapper
    {
        // LobbyResponse → BattleLobbyData (list cards + waiting-room summary header).
        // Status maps onto the existing 3-state LobbyStatus used by the card visuals.
        public static BattleLobbyData ToLobbyData(LobbyResponse r)
        {
            if (r == null) return null;

            int maxSlots = Mathf.Clamp(r.maxSlots > 0 ? r.maxSlots : 2, 2, 4);

            var selections = new List<BattleCaseSelection>();
            int totalOpens = 0;
            if (r.caseSelections != null)
            {
                foreach (var cs in r.caseSelections)
                {
                    if (cs == null) continue;
                    int qty = Mathf.Max(1, cs.quantity);
                    selections.Add(new BattleCaseSelection(
                        cs.caseId,
                        !string.IsNullOrEmpty(cs.caseName) ? cs.caseName : cs.caseId,
                        qty, cs.priceVp));
                    totalOpens += qty;
                }
            }

            string firstCaseId   = selections.Count > 0 ? selections[0].CaseId   : r.caseId;
            string firstCaseName = selections.Count > 0 ? selections[0].CaseName
                                  : (!string.IsNullOrEmpty(r.caseName) ? r.caseName : r.caseId);

            return new BattleLobbyData
            {
                LobbyId          = r.battleId,
                CaseId           = firstCaseId,
                CaseName         = firstCaseName,
                CaseSelections   = selections,
                HostName         = r.creator != null ? r.creator.displayName : null,
                HostAvatarId     = r.creator != null ? r.creator.avatarId : null,
                CreatorAccountId = r.creator != null ? r.creator.accountId : null,
                Rounds           = totalOpens > 0 ? totalOpens : Mathf.Max(1, r.rounds),
                PlayerCount      = (BattlePlayerCount)maxSlots,
                CurrentPlayers   = Mathf.Clamp(r.filledSlots, 0, maxSlots),
                WagerVP          = r.entryCost,
                Mode             = BattleMode.Normal,
                Rarity           = SkinRarity.Select,
                Status           = MapStatus(r.status),
            };
        }

        static LobbyStatus MapStatus(string status)
        {
            switch ((status ?? "").ToUpperInvariant())
            {
                case "STARTING":
                case "COMPLETED": return LobbyStatus.Live;
                default:          return LobbyStatus.Waiting;   // WAITING / CANCELLED / unknown
            }
        }

        // Completed LobbyResponse → BattleResult, reusing the exact structure the reel
        // animation, totals count-up, winner highlight and win/lose popup already read.
        // The local player's slot is placed at index 0 so the staged "YOU" panel order
        // is preserved; UserWon and WinnerIndex come straight from winnerSlotIndex.
        public static BattleResult ToBattleResult(LobbyResponse r, ContentDatabaseSO content, string myAccountId)
        {
            var battle = new BattleResult();
            if (r == null) return battle;

            var caseDef = ResolveCase(content, r.caseId, r.caseName);
            battle.Case     = caseDef;
            battle.Rounds   = ResolveRollCount(r);
            battle.ReelPool = BuildReelPool(caseDef);

            // Keep only real participants (REAL/BOT); skip EMPTY. A COMPLETED lobby is
            // full, but we never assume that — we map whatever the server returned.
            var slots = new List<LobbySlotResponse>();
            if (r.slots != null)
                foreach (var s in r.slots)
                    if (s != null && !string.Equals(s.type, "EMPTY", System.StringComparison.OrdinalIgnoreCase))
                        slots.Add(s);

            // Local player first (matches the staged-panel convention), then by slotIndex.
            slots.Sort((a, b) =>
            {
                bool aMine = IsMine(a, myAccountId);
                bool bMine = IsMine(b, myAccountId);
                if (aMine != bMine) return aMine ? -1 : 1;
                return a.slotIndex.CompareTo(b.slotIndex);
            });

            int pot = 0;
            foreach (var s in slots)
            {
                bool isUser = IsMine(s, myAccountId);
                var pr = new BattlePlayerResult
                {
                    Name     = ResolveName(s, isUser),
                    IsUser   = isUser,
                    IsWinner = s.slotIndex == r.winnerSlotIndex,
                    TotalVp  = s.totalVp,            // authoritative — never recomputed
                    Avatar   = ValoCase.Profile.ProfileManager.ResolveAvatarSprite(s.avatarId),
                };

                if (s.rounds != null)
                {
                    foreach (var rd in s.rounds)
                    {
                        if (rd == null || string.IsNullOrEmpty(rd.skinId)) continue;
                        var skin = content != null ? content.GetSkin(rd.skinId) : null;
                        if (skin == null)
                        {
                            Debug.LogWarning("[BATTLE_LOBBY] round skinId not in local catalog: " + rd.skinId);
                            continue;
                        }
                        pr.Skins.Add(skin);
                        battle.AllSkins.Add(skin);
                    }
                }

                pot += s.totalVp;
                battle.Players.Add(pr);
            }

            battle.PlayerCount = battle.Players.Count;
            battle.TotalPotVp  = pot;

            // Winner index is the POSITION of the winning slot in the reordered list.
            battle.WinnerIndex = -1;
            for (int i = 0; i < battle.Players.Count; i++)
                if (battle.Players[i].IsWinner) { battle.WinnerIndex = i; break; }
            battle.UserWon = battle.WinnerIndex >= 0 && battle.Players[battle.WinnerIndex].IsUser;

            return battle;
        }

        // Rolls actually returned per slot are authoritative for the reel length
        // (one per case-open across all selections), so the animation never under/over-runs.
        static int ResolveRollCount(LobbyResponse r)
        {
            int max = 0;
            if (r.slots != null)
                foreach (var s in r.slots)
                    if (s != null && s.rounds != null) max = Mathf.Max(max, s.rounds.Length);
            return max > 0 ? max : Mathf.Max(1, r.rounds);
        }

        static bool IsMine(LobbySlotResponse s, string myAccountId)
            => s != null && !string.IsNullOrEmpty(myAccountId) &&
               string.Equals(s.accountId, myAccountId, System.StringComparison.OrdinalIgnoreCase);

        static string ResolveName(LobbySlotResponse s, bool isUser)
        {
            if (!string.IsNullOrEmpty(s.displayName)) return s.displayName;
            if (isUser) return "YOU";
            return string.Equals(s.type, "BOT", System.StringComparison.OrdinalIgnoreCase) ? "BOT" : "PLAYER";
        }

        static CaseDefinitionSO ResolveCase(ContentDatabaseSO content, string caseId, string caseName)
        {
            if (content == null) return null;
            var byId = !string.IsNullOrEmpty(caseId) ? content.GetCase(caseId) : null;
            if (byId != null) return byId;

            var cases = content.Cases;
            if (cases == null) return null;
            if (!string.IsNullOrEmpty(caseName))
                foreach (var c in cases)
                    if (c != null && !string.IsNullOrEmpty(c.DisplayName) &&
                        string.Equals(c.DisplayName, caseName, System.StringComparison.OrdinalIgnoreCase))
                        return c;
            return cases.Count > 0 ? cases[0] : null;
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
}
