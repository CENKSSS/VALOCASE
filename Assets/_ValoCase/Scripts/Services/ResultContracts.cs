using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Services
{
    /// <summary>
    /// Phase-3 result contracts — immutable, ID-based, JsonUtility-serializable DTOs
    /// describing the OUTCOME of a roll/result, independent of any animation or UI.
    ///
    /// These are the shapes a backend (Spring Boot) could later return verbatim:
    /// they carry skin IDs / case IDs and primitive values only — NO ScriptableObject
    /// references. Result GENERATION flows through IResultProvider; these are its output.
    ///
    /// Fields are [SerializeField]-private with read-only accessors, so instances are
    /// effectively immutable in code while still round-tripping through JsonUtility.
    /// </summary>

    // ── Case Opening ─────────────────────────────────────────────────────────
    [Serializable]
    public sealed class CaseOpeningResult
    {
        [SerializeField] string caseId;
        [SerializeField] string rolledSkinId;
        [SerializeField] string rarity;
        [SerializeField] int    vpSpent;

        public string CaseId       => caseId;
        public string RolledSkinId => rolledSkinId;
        public string Rarity       => rarity;
        public int    VpSpent      => vpSpent;

        public CaseOpeningResult() { }
        public CaseOpeningResult(string caseId, string rolledSkinId, string rarity, int vpSpent)
        {
            this.caseId       = caseId;
            this.rolledSkinId = rolledSkinId;
            this.rarity       = rarity;
            this.vpSpent      = vpSpent;
        }
    }

    // ── Upgrade ──────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class UpgradeResult
    {
        [SerializeField] string[] inputSkinIds;
        [SerializeField] string   targetSkinId;
        [SerializeField] float    chance;
        [SerializeField] bool     success;

        public IReadOnlyList<string> InputSkinIds => inputSkinIds;
        public string TargetSkinId                => targetSkinId;
        public float  Chance                      => chance;
        public bool   Success                     => success;

        public UpgradeResult() { }
        public UpgradeResult(string[] inputSkinIds, string targetSkinId, float chance, bool success)
        {
            this.inputSkinIds = inputSkinIds ?? Array.Empty<string>();
            this.targetSkinId = targetSkinId;
            this.chance       = chance;
            this.success      = success;
        }
    }

    // ── Battle (contract-only; the live BattleOpeningEngine flow is unchanged) ──
    // NOTE: intentionally named BattleResultDto to avoid colliding with the existing
    // ValoCase.Battle.BattleResult (which is ScriptableObject-ref based and drives the
    // live visual flow). This DTO is the backend-shaped, ID-based mirror.
    [Serializable]
    public sealed class BattlePlayerResultDto
    {
        [SerializeField] string       name;
        [SerializeField] bool         isUser;
        [SerializeField] bool         isWinner;
        [SerializeField] int          totalVp;
        [SerializeField] List<string> skinIds = new();

        public string                Name     => name;
        public bool                  IsUser   => isUser;
        public bool                  IsWinner => isWinner;
        public int                   TotalVp  => totalVp;
        public IReadOnlyList<string> SkinIds  => skinIds;

        public BattlePlayerResultDto() { }
        public BattlePlayerResultDto(string name, bool isUser, bool isWinner, int totalVp, List<string> skinIds)
        {
            this.name     = name;
            this.isUser   = isUser;
            this.isWinner = isWinner;
            this.totalVp  = totalVp;
            this.skinIds  = skinIds ?? new List<string>();
        }
    }

    [Serializable]
    public sealed class BattleResultDto
    {
        [SerializeField] string                     caseId;
        [SerializeField] int                        rounds;
        [SerializeField] int                        playerCount;
        [SerializeField] int                        winnerIndex;
        [SerializeField] bool                        userWon;
        [SerializeField] int                        totalPotVp;
        [SerializeField] List<BattlePlayerResultDto> players = new();

        public string                            CaseId      => caseId;
        public int                               Rounds      => rounds;
        public int                               PlayerCount => playerCount;
        public int                               WinnerIndex => winnerIndex;
        public bool                              UserWon     => userWon;
        public int                               TotalPotVp  => totalPotVp;
        public IReadOnlyList<BattlePlayerResultDto> Players  => players;

        public BattleResultDto() { }
        public BattleResultDto(string caseId, int rounds, int playerCount, int winnerIndex,
                               bool userWon, int totalPotVp, List<BattlePlayerResultDto> players)
        {
            this.caseId      = caseId;
            this.rounds      = rounds;
            this.playerCount = playerCount;
            this.winnerIndex = winnerIndex;
            this.userWon     = userWon;
            this.totalPotVp  = totalPotVp;
            this.players     = players ?? new List<BattlePlayerResultDto>();
        }
    }

    /// <summary>
    /// Pure, read-only adapter: converts the live (SO-based) ValoCase.Battle.BattleResult
    /// into the ID-based BattleResultDto. Does NOT generate, roll, or mutate anything —
    /// it only maps an already-produced result, so it cannot affect the live battle flow.
    /// Provided as Phase-3 backend preparation; the live flow does not depend on it.
    /// </summary>
    public static class BattleResultAdapter
    {
        public static BattleResultDto ToDto(ValoCase.Battle.BattleResult result)
        {
            if (result == null) return null;

            var players = new List<BattlePlayerResultDto>();
            if (result.Players != null)
            {
                foreach (var p in result.Players)
                {
                    if (p == null) continue;
                    var ids = new List<string>();
                    if (p.Skins != null)
                        foreach (var s in p.Skins)
                            if (s != null) ids.Add(s.SkinId);
                    players.Add(new BattlePlayerResultDto(p.Name, p.IsUser, p.IsWinner, p.TotalVp, ids));
                }
            }

            return new BattleResultDto(
                result.Case != null ? result.Case.CaseId : null,
                result.Rounds,
                result.PlayerCount,
                result.WinnerIndex,
                result.UserWon,
                result.TotalPotVp,
                players);
        }
    }
}
