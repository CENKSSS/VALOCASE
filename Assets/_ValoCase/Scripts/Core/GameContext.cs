using UnityEngine;
using ValoCase.Data;
using ValoCase.Profile;
using ValoCase.Services;

namespace ValoCase.Core
{
    public sealed class GameContext : MonoBehaviour
    {
        public static GameContext Instance { get; private set; }

        [SerializeField] ContentDatabaseSO contentDatabase;
        [SerializeField] GameConfigSO gameConfig;
        [SerializeField] RarityVisualSO rarityVisuals;

        public ContentDatabaseSO Content => contentDatabase;
        public GameConfigSO Config => gameConfig;
        public RarityVisualSO RarityVisuals => rarityVisuals;

        public ISaveService Save { get; private set; }
        public IVpCurrencyService Vp { get; private set; }
        public IInventoryService Inventory { get; private set; }
        public ICaseOpeningService CaseOpening { get; private set; }
        public IShopService Shop { get; private set; }
        public IDailyRewardService DailyRewards { get; private set; }
        public IStatisticsService Statistics { get; private set; }
        public ICaseProgressionService CaseProgression { get; private set; }
        public IFakeOnlineService FakeOnline { get; private set; }
        public IUpgradeService Upgrade { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (contentDatabase == null)
                contentDatabase = Resources.Load<ContentDatabaseSO>(GameConstants.ContentDatabaseResourcePath);
            if (gameConfig == null)
                gameConfig = Resources.Load<GameConfigSO>(GameConstants.GameConfigResourcePath);
            if (rarityVisuals == null)
                rarityVisuals = Resources.Load<RarityVisualSO>(GameConstants.RarityVisualsResourcePath);

            if (contentDatabase == null || gameConfig == null || rarityVisuals == null)
            {
                Debug.LogError("[ValoCase] Missing Resources asset. Run ValoCase > Generate Sample Content, then ValoCase > Setup Current Scene.");
                return;
            }

            contentDatabase?.BuildLookups();
            InitializeServices();
        }

        void InitializeServices()
        {
            var services = GameServicesFactory.Create(contentDatabase, gameConfig);
            Save = services.Save;
            Vp = services.Vp;
            Inventory = services.Inventory;
            Statistics = services.Statistics;
            CaseProgression = services.CaseProgression;
            Shop = services.Shop;
            DailyRewards = services.DailyRewards;
            FakeOnline = services.FakeOnline;
            CaseOpening = services.CaseOpening;
            Upgrade = services.Upgrade;

            // ── Admin VP grant (one-time) ─────────────────────────────────────
            // Gives 500 000 VP exactly once, keyed on the adminVpGrantApplied flag
            // in SaveDataRoot.  After the flag is set, restarts keep whatever
            // balance the player has saved — even if it dropped below 500 000.
            var savePath = System.IO.Path.Combine(
                UnityEngine.Application.persistentDataPath, GameConstants.SaveFileName);
            Debug.Log("[VP_FIX] save path=" + savePath);
            Debug.Log("[VP_FIX] balance loaded=" + (Vp?.Balance ?? -1));
            Debug.Log("[VP_FIX] grantApplied=" + (Save?.Data?.adminVpGrantApplied ?? false));

            if (Vp != null && Save?.Data != null && !Save.Data.adminVpGrantApplied)
            {
                Debug.Log("[VP_FIX] applying admin grant");
                Vp.SetBalance(500000);
                Save.Data.adminVpGrantApplied = true;
                Save.Save();
                Debug.Log("[VP_FIX] first grant applied, balance=" + Vp.Balance);
                Debug.Log("[VP_FIX] save written");
            }
            else
            {
                Debug.Log("[VP_FIX] grant already applied, keeping saved balance=" + (Vp?.Balance ?? -1));
            }
            // ─────────────────────────────────────────────────────────────────

            if (Vp != null)
                GameEvents.RaiseVpChanged(Vp.Balance, Vp.Balance);
            GameEvents.RaiseStatisticsChanged();
            GameEvents.RaiseShopRotated();

            // Boot profile system — loads FaceCards + restores PlayerPrefs once
            ProfileManager.EnsureInitialized();
        }

        public void Persist() => Save?.Save();

        void OnApplicationPause(bool pause) { if (pause) Persist(); }
        void OnApplicationQuit() => Persist();
    }
}
