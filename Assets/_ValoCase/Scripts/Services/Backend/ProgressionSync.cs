using ValoCase.Core;
using ValoCase.Progression;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// Copies backend progression payloads into the client-side PlayerProgression
    /// cache. The backend is the source of truth; this never computes XP — it only
    /// mirrors what the server returned and (optionally) shows a small toast.
    /// Parsing is null-safe: a missing progression object is simply ignored.
    /// </summary>
    public static class ProgressionSync
    {
        public static void ApplyFromWallet(WalletResponse wallet)
        {
            if (wallet?.progression == null) return;
            ApplySnapshot(wallet.progression, showXpToast: false);
        }

        public static void ApplyFromOpen(OpenCaseResultResponse open, bool showXpToast)
        {
            if (open?.progression == null) return;
            ApplySnapshot(open.progression, showXpToast);
        }

        public static void ApplyFromProgression(ProgressionResponse p, bool showXpToast)
        {
            if (p == null) return;
            ApplySnapshot(p, showXpToast);
        }

        static void ApplySnapshot(ProgressionResponse p, bool showXpToast)
        {
            int prevLevel = PlayerProgression.Level;

            PlayerProgression.Apply(p.level, p.currentLevelXp, p.xpRequiredForNextLevel,
                                    p.totalXp, p.unlockedCategories);

            // leveledUp is a case-open-only backend field, so both notifications fire only
            // on the open that caused the transition — never repeated on wallet/resync.
            if (p.leveledUp)
            {
                GameEvents.RaiseToast($"Seviye atladın! Lv. {PlayerProgression.Level}");
                if (PlayerProgression.CrossedNewUnlockLevel(prevLevel, PlayerProgression.Level))
                    GameEvents.RaiseToast("Yeni kasa kilidi açıldı!");
            }
            else if (showXpToast && p.xpGranted > 0)
            {
                GameEvents.RaiseToast($"+{p.xpGranted} XP");
            }
        }
    }
}
