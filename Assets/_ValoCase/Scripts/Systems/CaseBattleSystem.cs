using System;
using System.Collections.Generic;
using ValoCase.Battle;
using ValoCase.Data;
using ValoCase.Services;

namespace ValoCase.Systems
{
    /// <summary>
    /// CaseBattle feature'ının tüm oyun mantığı bu sınıfta yaşar.
    ///
    /// YAPTIĞI İŞLER:
    ///   - VP kontrolü ve harcama
    ///   - Battle session oluşturma
    ///   - Round sırasını ilerletme
    ///   - Winner belirleme
    ///   - Skin dağıtma (settle)
    ///   - Event yayma → UI ve Animation bu event'leri dinler
    ///
    /// YAPMADIĞI İŞLER:
    ///   - Panel show/hide
    ///   - Animasyon tetikleme
    ///   - GameContext.Instance çağrısı (CompositionRoot inject eder)
    ///
    /// UI → System.TryStartBattle()
    /// System → OnBattleStarted + OnRoundBegan
    /// Animation tamamlanınca → System.NotifyAnimationComplete()
    /// System → sonraki round veya OnBattleSettled
    /// </summary>
    public sealed class CaseBattleSystem
    {
        // ── Bağımlılıklar (CompositionRoot tarafından inject edilir) ──────────
        readonly IVpCurrencyService  _vp;
        readonly IInventoryService   _inventory;
        readonly ICaseOpeningService _rng;
        readonly ISaveService        _save;

        // ── Aktif oturum ──────────────────────────────────────────────────────
        public CaseBattleSession ActiveSession { get; private set; }
        public bool HasActiveSession => ActiveSession != null;

        // Double-settle koruması
        readonly HashSet<CaseBattleSession> _settled = new HashSet<CaseBattleSession>();

        // ── Eventler (UI ve Animation dinler) ─────────────────────────────────
        public event Action<CaseBattleSession>      OnBattleStarted;
        public event Action<CaseBattleSession, int> OnRoundBegan;
        public event Action<CaseBattleSession, int> OnRoundResolved;
        public event Action<CaseBattleSession>      OnBattleSettled;
        public event Action<string>                 OnBattleFailed;

        // ── Constructor ───────────────────────────────────────────────────────
        public CaseBattleSystem(IVpCurrencyService vp, IInventoryService inventory,
                                 ICaseOpeningService rng, ISaveService save)
        {
            _vp        = vp;
            _inventory = inventory;
            _rng       = rng;
            _save      = save;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC API — CaseBattleScreen yalnızca bu 3 metodu çağırır
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Battle başlatır. VP kesilir → session oluşur → OnBattleStarted.
        /// Pre-roll YAPILMAZ — animasyonu screen'in coroutine'i yönetir,
        /// her round için doğrudan session.RollNextRound() çağırır.
        /// </summary>
        public bool TryStartBattle(CaseDefinitionSO caseDef, int caseCount,
                                   out CaseBattleSession session)
        {
            session = null;

            if (HasActiveSession)
            {
                OnBattleFailed?.Invoke("Zaten aktif bir battle var.");
                return false;
            }

            if (caseDef == null || caseCount <= 0)
            {
                OnBattleFailed?.Invoke("Geçersiz kasa veya round sayısı.");
                return false;
            }

            if (_vp == null || _rng == null)
            {
                OnBattleFailed?.Invoke("Servisler hazır değil.");
                return false;
            }

            var totalCost = caseDef.VpPrice * caseCount;

            if (!_vp.CanAfford(totalCost))
            {
                OnBattleFailed?.Invoke($"Yetersiz VP. Gereken: {totalCost}");
                return false;
            }

            if (!_vp.TrySpend(totalCost))
            {
                OnBattleFailed?.Invoke("VP harcama başarısız.");
                return false;
            }

            var playerName = _save?.Data?.playerName ?? "Player";
            var player     = new BattleParticipant(playerName, isBot: false);
            var opponent   = new BattleParticipant(BattleConstants.BotName, isBot: true);

            ActiveSession = new CaseBattleSession(caseDef, caseCount, player, opponent, _rng);
            session       = ActiveSession;

            _save?.Save();
            OnBattleStarted?.Invoke(ActiveSession);
            return true;
        }

        /// <summary>
        /// Screen'in coroutine'i tüm roundları tamamlayınca çağırır.
        /// Skin dağıtımı ve kayıt burada yapılır.
        /// </summary>
        public void SettleActiveSession() => Settle();

        /// <summary>
        /// CaseBattleAnimation animasyonu tamamlayınca UI bu metodu çağırır.
        /// Sonraki round'a geçer veya battle'ı bitirir — karar burada verilir.
        /// </summary>
        public void NotifyAnimationComplete()
        {
            if (ActiveSession == null) return;

            OnRoundResolved?.Invoke(ActiveSession, ActiveSession.CurrentRound);

            if (ActiveSession.HasMoreRounds)
                RollNextRound();
            else
                Settle();
        }

        /// <summary>
        /// Ekran kapandığında veya back'e basıldığında aktif session'ı iptal eder.
        /// Settle çağrılmaz — skin ve VP değişikliği olmaz.
        /// </summary>
        public void Abort()
        {
            ActiveSession = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // INTERNAL — dışarıdan erişilmez
        // ─────────────────────────────────────────────────────────────────────

        void RollNextRound()
        {
            if (ActiveSession == null) return;
            if (!ActiveSession.RollNextRound(out _, out _)) return;
            OnRoundBegan?.Invoke(ActiveSession, ActiveSession.CurrentRound);
        }

        void Settle()
        {
            if (ActiveSession == null) return;
            if (ActiveSession.State != BattleState.Finished) return;
            if (!_settled.Add(ActiveSession)) return; // double-settle koruması

            // Winner kuralı: PlayerWins → her iki tarafın skinleri oyuncuya
            //                Tie        → yalnızca oyuncunun skinleri
            //                OpponentWins → oyuncu hiçbir şey alamaz
            switch (ActiveSession.Outcome)
            {
                case BattleOutcome.PlayerWins:
                    foreach (var s in ActiveSession.Player.WonSkins)
                        _inventory?.AddSkin(s, out _);
                    foreach (var s in ActiveSession.Opponent.WonSkins)
                        _inventory?.AddSkin(s, out _);
                    break;

                case BattleOutcome.Tie:
                    foreach (var s in ActiveSession.Player.WonSkins)
                        _inventory?.AddSkin(s, out _);
                    break;

                case BattleOutcome.OpponentWins:
                    // Oyuncu hiçbir şey alamaz — battle skinleri kaybolur
                    break;
            }

            _save?.Save();

            OnBattleSettled?.Invoke(ActiveSession);
            ActiveSession = null;
        }
    }
}
