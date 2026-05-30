using UnityEngine;
using ValoCase.Systems;
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
        [Header("Missions")]
        [SerializeField] ToolsScreen toolsScreen;

        [Header("Case Battle Lobby")]
        [SerializeField] LobbyListScreen lobbyListScreen;

        void Start()
        {
            var ctx = GameContext.Instance;
            if (ctx == null)
            {
                Debug.LogError("[CompositionRoot] GameContext bulunamadı.");
                return;
            }

            var battle = WireCaseBattle(ctx);
            WireMissions(ctx, battle);

            // ── Yeni özellikler buraya eklenir ────────────────────────────────
            // WireUpgrade(ctx);
            // WireInventory(ctx);
            // WireShop(ctx);
        }

        // ─────────────────────────────────────────────────────────────────────
        // CASE BATTLE
        // ─────────────────────────────────────────────────────────────────────

        // Creates CaseBattleSystem for mission event hooks (PlayBattle / WinBattle).
        // CaseBattleScreen is retired; the system is kept only so MissionSystem
        // can subscribe to OnBattleStarted / OnBattleSettled when a battle runs.
        CaseBattleSystem WireCaseBattle(GameContext ctx)
        {
            return new CaseBattleSystem(
                ctx.Vp,
                ctx.Inventory,
                ctx.CaseOpening,
                ctx.Save
            );
        }

        void WireMissions(GameContext ctx, CaseBattleSystem battle)
        {
            var missions = new MissionSystem(ctx.Save);
            missions.Initialize(battle);

            var injected = new System.Collections.Generic.HashSet<ToolsScreen>();
            if (toolsScreen != null) { toolsScreen.Inject(missions); injected.Add(toolsScreen); }

            var all = FindObjectsOfType<ToolsScreen>(includeInactive: true);
            foreach (var ts in all)
            {
                if (injected.Add(ts)) ts.Inject(missions);
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
