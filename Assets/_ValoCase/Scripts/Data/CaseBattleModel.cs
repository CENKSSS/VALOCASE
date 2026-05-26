using System;
using System.Collections.Generic;
using UnityEngine;
using ValoCase.Data;
using ValoCase.Services;

// Namespace ValoCase.Battle olarak kalıyor —
// CaseBattleSystem ve diğer referanslar "using ValoCase.Battle" ile bulmaya devam eder.
// Dosya Data/ klasörüne taşındı çünkü bu saf veri modeli.
namespace ValoCase.Battle
{
    // ─────────────────────────────────────────────────────────────────────────
    // KATILIMCI
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Battle'ın bir tarafı. Geçici olarak rollanan skinleri tutar.</summary>
    public sealed class BattleParticipant
    {
        public string DisplayName { get; }
        public bool   IsBot       { get; }

        readonly List<SkinDefinitionSO> _won = new List<SkinDefinitionSO>();
        public IReadOnlyList<SkinDefinitionSO> WonSkins => _won;
        public int TotalVp { get; private set; }

        public BattleParticipant(string displayName, bool isBot)
        {
            DisplayName = displayName;
            IsBot       = isBot;
        }

        internal void RecordRoll(SkinDefinitionSO skin)
        {
            if (skin == null) return;
            _won.Add(skin);
            TotalVp += skin.VpValue;
        }

        internal void Clear()
        {
            _won.Clear();
            TotalVp = 0;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DURUM ENUMlARi
    // ─────────────────────────────────────────────────────────────────────────

    public enum BattleState
    {
        Pending,      // henüz round başlamadı
        InProgress,   // en az bir round oynandı
        Finished      // son round tamamlandı
    }

    public enum BattleOutcome
    {
        Undecided,
        PlayerWins,
        OpponentWins,
        Tie
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OTURUM
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tek bir battle oturumu. Tüm round verisi ve sonuç burada tutulur.
    /// Saf C# — MonoBehaviour veya Unity singleton bağımlılığı yok.
    /// </summary>
    public sealed class CaseBattleSession
    {
        // ── Sabit config ──────────────────────────────────────────────────────
        public CaseDefinitionSO  Case      { get; }
        public int               CaseCount { get; }
        public int               TotalCost { get; }
        public BattleParticipant Player    { get; }
        public BattleParticipant Opponent  { get; }

        // ── Değişken durum ────────────────────────────────────────────────────
        public int           CurrentRound { get; private set; }
        public BattleState   State        { get; private set; }
        public BattleOutcome Outcome      { get; private set; }

        readonly ICaseOpeningService _rng;

        // ── Eventler ──────────────────────────────────────────────────────────
        public event Action<int, SkinDefinitionSO, SkinDefinitionSO> OnRoundRolled;
        public event Action<BattleOutcome>                            OnFinished;

        public CaseBattleSession(CaseDefinitionSO caseDef, int caseCount,
                                  BattleParticipant player, BattleParticipant opponent,
                                  ICaseOpeningService rng)
        {
            Case      = caseDef;
            CaseCount = Mathf.Max(1, caseCount);
            TotalCost = caseDef != null ? caseDef.VpPrice * CaseCount : 0;
            Player    = player;
            Opponent  = opponent;
            _rng      = rng;
            State     = BattleState.Pending;
            Outcome   = BattleOutcome.Undecided;
        }

        public bool HasMoreRounds => CurrentRound < CaseCount;

        /// <summary>Her iki taraf için bir round roll eder. Tüm roundlar bittiyse false döner.</summary>
        public bool RollNextRound(out SkinDefinitionSO playerSkin, out SkinDefinitionSO opponentSkin)
        {
            playerSkin   = null;
            opponentSkin = null;

            if (State == BattleState.Finished || _rng == null || Case == null) return false;
            if (!HasMoreRounds) return false;

            if (State == BattleState.Pending) State = BattleState.InProgress;

            playerSkin   = _rng.RollSkin(Case);
            opponentSkin = _rng.RollSkin(Case);

            Player  .RecordRoll(playerSkin);
            Opponent.RecordRoll(opponentSkin);

            CurrentRound++;
            OnRoundRolled?.Invoke(CurrentRound, playerSkin, opponentSkin);

            if (!HasMoreRounds) Finish();

            return true;
        }

        void Finish()
        {
            State = BattleState.Finished;

            if      (Player.TotalVp > Opponent.TotalVp) Outcome = BattleOutcome.PlayerWins;
            else if (Player.TotalVp < Opponent.TotalVp) Outcome = BattleOutcome.OpponentWins;
            else                                         Outcome = BattleOutcome.Tie;

            OnFinished?.Invoke(Outcome);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SABİTLER
    // ─────────────────────────────────────────────────────────────────────────

    public static class BattleConstants
    {
        public const string BotName = "Admin";
        public static readonly int[] CaseCountChoices = { 1, 2, 3, 5 };
    }
}
