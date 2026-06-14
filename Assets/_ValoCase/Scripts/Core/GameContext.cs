using System.Collections;
using UnityEngine;
using ValoCase.Data;
using ValoCase.Profile;
using ValoCase.Save;
using ValoCase.Services;
using ValoCase.Services.Backend;

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
        public IEconomyService Economy { get; private set; }

        // Backend (Spring Boot). Non-null only when GameConfig.UseBackend is true.
        // Exposed so a future Step-2 remote case-opening path can reuse the client.
        public BackendApiClient Backend { get; private set; }
        public bool BackendEnabled => gameConfig != null && gameConfig.UseBackend && Backend != null;

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
            Economy = services.Economy;

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

            // Backend mode (opt-in via GameConfig.useBackend). Local mode skips this
            // entirely and behaves exactly as before.
            TryStartBackendSync();
        }

        // ── Backend boot sync ───────────────────────────────────────────────────
        // No-op in local mode. In backend mode: ensure a guest token, then pull the
        // authoritative wallet + inventory into the local save (used as a cache).
        // Any failure leaves the cached local state intact — never faked as success.
        void TryStartBackendSync()
        {
            if (gameConfig == null || !gameConfig.UseBackend) return;
            if (Save?.Data == null) return;

            Backend = new BackendApiClient(
                gameConfig.BackendBaseUrl,
                gameConfig.RequestTimeoutSeconds,
                Save.Data.guestToken);

            StartCoroutine(BackendBootSync());
        }

        IEnumerator BackendBootSync()
        {
            Debug.Log("[Backend] Boot sync started — baseUrl=" + gameConfig.BackendBaseUrl);

            // 1) Ensure a guest token.
            if (string.IsNullOrEmpty(Save.Data.guestToken))
            {
                var registered = false;
                yield return Backend.RegisterGuest(
                    res =>
                    {
                        registered = true;
                        Save.Data.guestToken = res.guestToken;
                        Save.Data.guestAccountId = res.guestAccountId;
                        Backend.GuestToken = res.guestToken;
                        Save.Save();
                        Debug.Log("[Backend] Guest registered — accountId=" + res.guestAccountId);
                    },
                    err => Debug.LogWarning("[Backend] Guest registration failed — staying on local cache. " + err));

                if (!registered)
                {
                    Debug.LogWarning("[Backend] No guest token — aborting boot sync (offline/local cache in use).");
                    yield break;
                }
            }
            else
            {
                Backend.GuestToken = Save.Data.guestToken;
            }

            // 2) Wallet — backend is authoritative; overwrite the local cached balance.
            yield return Backend.GetWallet(
                res =>
                {
                    Vp?.SetBalance(res.vpBalance);
                    Save.Save();
                    Debug.Log("[Backend] Wallet synced — vp=" + res.vpBalance);
                },
                err => Debug.LogWarning("[Backend] Wallet sync failed — keeping cached balance. " + err));

            // 3) Inventory — replace the local cached inventory wholesale (idempotent;
            //    no duplication on repeat sync).
            yield return Backend.GetInventory(
                ApplyInventoryFromBackend,
                err => Debug.LogWarning("[Backend] Inventory sync failed — keeping cached inventory. " + err));

            Debug.Log("[Backend] Boot sync complete.");
        }

        // ── Backend live helpers (used by the case-opening backend path) ────────

        // Apply the server's authoritative wallet balance to the local VP cache.
        // Never spends locally — this is a direct overwrite of the cached balance.
        public void ApplyBackendWallet(int vpBalance)
        {
            Vp?.SetBalance(vpBalance);
            Save?.Save();
        }

        // Best-effort wallet + inventory re-sync. Used after an ambiguous open
        // (transport timeout that may have committed) or an unresolved skin, so the
        // local cache reconciles to the authoritative server state.
        public void RequestBackendResync()
        {
            if (!BackendEnabled) return;
            StartCoroutine(ResyncWalletAndInventory());
        }

        IEnumerator ResyncWalletAndInventory()
        {
            yield return Backend.GetWallet(
                res => { Vp?.SetBalance(res.vpBalance); Save.Save(); Debug.Log("[Backend] Wallet re-synced — vp=" + res.vpBalance); },
                err => Debug.LogWarning("[Backend] Wallet re-sync failed — keeping cached balance. " + err));

            yield return Backend.GetInventory(
                ApplyInventoryFromBackend,
                err => Debug.LogWarning("[Backend] Inventory re-sync failed — keeping cached inventory. " + err));
        }

        // Server-authoritative inventory replace. The backend is the source of truth,
        // so the local list is rebuilt from the response rather than merged — this is
        // what makes repeated syncs idempotent (no double-grant). Unknown skin IDs are
        // kept (so nothing is silently dropped) and logged; downstream value/display
        // code already null-checks via ContentDatabaseSO.GetSkin.
        void ApplyInventoryFromBackend(InventoryResponse res)
        {
            if (res?.items == null)
            {
                Debug.LogWarning("[Backend] Inventory response had no items array — leaving cache untouched.");
                return;
            }

            var now = TimeUtil.NowUnix();
            var list = Save.Data.inventory;
            list.Clear();

            int unknown = 0;
            foreach (var item in res.items)
            {
                if (item == null || string.IsNullOrEmpty(item.skinId) || item.quantity <= 0) continue;
                if (contentDatabase != null && contentDatabase.GetSkin(item.skinId) == null)
                {
                    unknown++;
                    Debug.LogWarning("[Backend] Inventory skinId not in local catalog (kept): " + item.skinId);
                }
                list.Add(new OwnedSkinSaveEntry
                {
                    skinId = item.skinId,
                    quantity = item.quantity,
                    obtainedUnix = now
                });
            }

            Statistics?.RecalculateInventoryStats(Inventory, contentDatabase, notify: false);
            Save.Save();
            GameEvents.RaiseInventoryChanged();
            Debug.Log($"[Backend] Inventory synced — {list.Count} entries ({unknown} unknown to local catalog).");
        }

        public void Persist() => Save?.Save();

        void OnApplicationPause(bool pause) { if (pause) Persist(); }
        void OnApplicationQuit() => Persist();
    }
}
