using UnityEngine;
using ValoCase.Systems;
using ValoCase.UI.Animation;
using ValoCase.UI.Screens;

namespace ValoCase.Core
{
    /// <summary>
    /// CompositionRoot — tek bootstrap ve bağlantı noktası.
    ///
    /// Kural: GameContext.Instance yalnızca burada çağrılır.
    /// Kural: Sistemler burada oluşturulur ve ekranlara inject edilir.
    /// Kural: UI ile System arasındaki cross-layer event'ler burada bağlanır.
    ///
    /// Yeni özellik eklemek için bu dosyaya 3-5 satır eklemek yeterli.
    /// </summary>
    public sealed class CompositionRoot : MonoBehaviour
    {
        [Header("Case Battle")]
        [SerializeField] CaseBattleScreen           caseBattleScreen;
        [SerializeField] CaseBattleAnimation        caseBattleAnimation;
        [SerializeField] RouletteAnimationController playerRoulette;
        [SerializeField] RouletteAnimationController opponentRoulette;

        void Start()
        {
            var ctx = GameContext.Instance;
            if (ctx == null)
            {
                Debug.LogError("[CompositionRoot] GameContext bulunamadı.");
                return;
            }

            WireCaseBattle(ctx);

            // ── Yeni özellikler buraya eklenir ────────────────────────────────
            // WireUpgrade(ctx);
            // WireInventory(ctx);
            // WireShop(ctx);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CASE BATTLE
        // ─────────────────────────────────────────────────────────────────────

        void WireCaseBattle(GameContext ctx)
        {
            // Inspector'da atanmamışsa sahnede ara (builder runtime'da oluşturur)
            if (caseBattleScreen == null)
                caseBattleScreen = FindObjectOfType<CaseBattleScreen>(includeInactive: true);

            // CaseBattleSystem bağımlılıklarını doğrudan alır — ICaseBattleService yok
            var system = new CaseBattleSystem(
                ctx.Vp,
                ctx.Inventory,
                ctx.CaseOpening,
                ctx.Save
            );

            // Screen → sistem bağlantısı (VP kesme + settle)
            caseBattleScreen?.Inject(system);

            // Animasyon → roulette controller'larını bağla
            caseBattleAnimation?.BindRoulettes(playerRoulette, opponentRoulette);

            // Cross-layer: system eventi → animasyon tepkisi
            // (Screen bu event'leri dinleyemez çünkü animation ayrı katman)
            if (caseBattleAnimation != null)
            {
                system.OnBattleSettled += _ => caseBattleAnimation.StartWinnerPulse();
                system.OnBattleStarted += _ => caseBattleAnimation.StopWinnerPulse();
                system.OnBattleFailed  += _ => caseBattleAnimation.StopRound();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GELECEK ÖZELLİKLER — şablon
        // ─────────────────────────────────────────────────────────────────────

        // void WireUpgrade(GameContext ctx)
        // {
        //     var system = new UpgradeSystem(ctx.Upgrade, ctx.Inventory);
        //     upgradeScreen?.Inject(system);
        // }

        // void WireInventory(GameContext ctx)
        // {
        //     var system = new InventorySystem(ctx.Inventory);
        //     inventoryScreen?.Inject(system);
        // }
    }
}
