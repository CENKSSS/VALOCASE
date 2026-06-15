using System;
using System.Collections;
using System.Collections.Generic;
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

        // Runtime-only per-instance view of the backend inventory (itemId identity that
        // the quantity/skinId save cache aggregates away). Rebuilt on every inventory
        // sync; empty in local mode. Never persisted. Used by future itemId-based
        // systems (Upgrade, Trade, Market, gifting, item history).
        public BackendInventoryCache BackendInventory { get; } = new BackendInventoryCache();

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
                        var token = res.ResolveToken();
                        if (string.IsNullOrEmpty(token))
                        {
                            // Registration succeeded but no usable token was parsed — do NOT
                            // persist an empty token (that caused endless re-registration and
                            // token-less, 0-balance requests). Treat as failure.
                            Debug.LogError("[BackendAuth] Guest registration returned no usable token " +
                                           "(check backend JSON field names). Aborting sync.");
                            return;
                        }
                        registered = true;
                        Save.Data.guestToken = token;
                        Save.Data.guestAccountId = res.accountId;
                        Backend.GuestToken = token;
                        Save.Save();
                        Debug.Log($"[BackendAuth] guest registered — accountId={res.accountId} token={token}");
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
                Debug.Log($"[BackendAuth] reusing saved token — accountId={Save.Data.guestAccountId} token={Save.Data.guestToken}");
            }

            // 2) Wallet — backend is authoritative; overwrite the local cached balance.
            yield return Backend.GetWallet(
                res =>
                {
                    Vp?.SetBalance(res.vpBalance);
                    Save.Save();
                    Debug.Log($"[BackendAuth] wallet synced — accountId={res.accountId} vp={res.vpBalance}");
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

        // ── Backend selling (server-authoritative) ──────────────────────────────
        // The backend validates ownership, removes inventory, credits VP, writes the
        // transaction, and returns the authoritative wallet. Unity never adds VP or
        // removes inventory locally — on success it applies the returned balance and
        // re-pulls inventory so the local cache mirrors the server. On any failure the
        // local cache is left untouched and the UI re-enables via the onFailed callback.

        // Sell one unit of one skin. onSold receives the VP gained for that unit.
        public void SellOneBackend(string skinId, Action<int> onSold, Action<string> onFailed)
        {
            if (!BackendEnabled) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            if (string.IsNullOrEmpty(skinId)) { onFailed?.Invoke("Geçersiz skin."); return; }
            StartCoroutine(SellOneRoutine(skinId, onSold, onFailed));
        }

        IEnumerator SellOneRoutine(string skinId, Action<int> onSold, Action<string> onFailed)
        {
            SellOneResponse response = null;
            BackendError error = null;

            yield return Backend.SellOne(skinId, r => response = r, e => error = e);

            if (error != null || response == null)
            {
                Debug.LogWarning("[Backend] Sell one failed — " + (error?.ToString() ?? "null response"));
                // Ambiguous transport failure (status 0) may have committed server-side:
                // reconcile so the local cache reflects whatever the server actually did.
                if (error == null || error.HttpStatus == 0) RequestBackendResync();
                onFailed?.Invoke("Satış başarısız. Lütfen tekrar deneyin.");
                yield break;
            }

            ApplyBackendWallet(response.newVpBalance);   // authoritative; no local Add
            yield return SyncInventoryFromBackend();      // reconcile the sold unit
            Debug.Log($"[Backend] Sold one — skinId={response.skinId} vpGained={response.vpGained} newBalance={response.newVpBalance}");
            onSold?.Invoke(response.vpGained);
        }

        // Sell every owned skin. onSold receives (soldCount, totalVpGained).
        public void SellAllBackend(Action<int, int> onSold, Action<string> onFailed)
        {
            if (!BackendEnabled) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            StartCoroutine(SellBulkRoutine(
                run: (ok, err) => Backend.SellAll(ok, err),
                onSold, onFailed, label: "sell all"));
        }

        // Sell every owned skin valued at or below maxVpValue.
        public void SellBelowValueBackend(int maxVpValue, Action<int, int> onSold, Action<string> onFailed)
        {
            if (!BackendEnabled) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            StartCoroutine(SellBulkRoutine(
                run: (ok, err) => Backend.SellBelowValue(maxVpValue, ok, err),
                onSold, onFailed, label: "sell below value"));
        }

        // Shared bulk-sell driver: runs the supplied endpoint, applies the authoritative
        // wallet, then re-pulls inventory (Option B — robust for many-item deltas).
        IEnumerator SellBulkRoutine(
            Func<Action<SellBulkResponse>, Action<BackendError>, IEnumerator> run,
            Action<int, int> onSold, Action<string> onFailed, string label)
        {
            SellBulkResponse response = null;
            BackendError error = null;

            yield return run(r => response = r, e => error = e);

            if (error != null || response == null)
            {
                Debug.LogWarning($"[Backend] Bulk {label} failed — " + (error?.ToString() ?? "null response"));
                if (error == null || error.HttpStatus == 0) RequestBackendResync();
                onFailed?.Invoke("Satış başarısız. Lütfen tekrar deneyin.");
                yield break;
            }

            ApplyBackendWallet(response.newVpBalance);   // authoritative; no local Add
            yield return SyncInventoryFromBackend();      // reconcile removed items
            Debug.Log($"[Backend] Bulk {label} OK — soldCount={response.soldCount} totalVpGained={response.totalVpGained} newBalance={response.newVpBalance}");
            onSold?.Invoke(response.soldCount, response.totalVpGained);
        }

        // ── Backend upgrade (server-authoritative) ──────────────────────────────
        // Resolves the selected input skins to REAL backend itemIds (never skinIds),
        // posts the upgrade, and returns the server's decision. The backend consumes
        // the inputs and grants the target; Unity never mutates inventory locally here.
        // The caller (UpgradeScreen) plays the existing animation against the returned
        // success, then calls RequestBackendResync() once the result UI is shown so the
        // inventory reconciles. On any failure this resyncs (when state may be stale)
        // and surfaces a safe message — no spin, no local consume, no local grant.
        public IEnumerator UpgradeBackendRoutine(
            List<SkinDefinitionSO> inputs, SkinDefinitionSO target, Action<UpgradeBackendResult> onResult)
        {
            var result = new UpgradeBackendResult { Target = target };

            if (!BackendEnabled)
            {
                result.Error = "Sunucu kullanılamıyor.";
                onResult?.Invoke(result);
                yield break;
            }
            if (inputs == null || inputs.Count == 0 || target == null)
            {
                result.Error = "Geçersiz seçim.";
                onResult?.Invoke(result);
                yield break;
            }

            // Map the selected input skins to owned backend instance itemIds.
            if (!TryResolveUpgradeItemIds(inputs, out var itemIds, out var resolveError))
            {
                Debug.LogWarning("[Backend] Upgrade itemId resolution failed — " + resolveError);
                RequestBackendResync();   // local cache may be stale vs the server
                result.Error = resolveError;
                onResult?.Invoke(result);
                yield break;
            }

            UpgradeResponse response = null;
            BackendError error = null;
            yield return Backend.Upgrade(itemIds.ToArray(), target.SkinId,
                r => response = r,
                e => error = e);

            if (error != null || response == null)
            {
                Debug.LogWarning("[Backend] Upgrade failed — " + (error?.ToString() ?? "null response"));
                // Ambiguous transport failure (status 0) may have committed server-side.
                if (error == null || error.HttpStatus == 0) RequestBackendResync();
                result.Error = "Yükseltme başarısız. Lütfen tekrar deneyin.";
                onResult?.Invoke(result);
                yield break;
            }

            // Resolve the target from the backend's authoritative targetSkinId.
            var resolvedTarget = !string.IsNullOrEmpty(response.targetSkinId) && Content != null
                ? Content.GetSkin(response.targetSkinId)
                : null;
            if (resolvedTarget != null) result.Target = resolvedTarget;

            if (result.Target == null)
            {
                // Server committed but the target isn't in the local catalog — reconcile
                // and show a safe message rather than animating an unknown skin.
                Debug.LogError("[Backend] Upgrade target not resolvable locally: " + response.targetSkinId);
                RequestBackendResync();
                result.Error = "Sonuç eşitleniyor...";
                onResult?.Invoke(result);
                yield break;
            }

            result.Ok = true;
            result.Success = response.success;
            result.Chance = response.chance;
            Debug.Log($"[Backend] Upgrade OK — success={response.success} chance={response.chance} " +
                      $"consumed={(response.consumedItemIds != null ? response.consumedItemIds.Length : 0)} " +
                      $"target={response.targetSkinId} granted={response.grantedInventoryItemId ?? "<none>"}");
            onResult?.Invoke(result);
        }

        // Translate the selected input skins into real backend instance itemIds. Inputs
        // are grouped by skinId and that many candidate instances are pulled from the
        // runtime BackendInventoryCache. Returns false (with a user-facing message) if
        // any skin lacks enough owned instances — the caller then resyncs and aborts,
        // so Unity never sends faked itemIds.
        bool TryResolveUpgradeItemIds(List<SkinDefinitionSO> inputs, out List<string> itemIds, out string error)
        {
            itemIds = new List<string>();
            error = null;

            var need = new Dictionary<string, int>();
            foreach (var skin in inputs)
            {
                if (skin == null || string.IsNullOrEmpty(skin.SkinId))
                {
                    error = "Geçersiz skin seçimi.";
                    return false;
                }
                need[skin.SkinId] = need.TryGetValue(skin.SkinId, out var c) ? c + 1 : 1;
            }

            foreach (var kv in need)
            {
                if (!BackendInventory.TryConsumeCandidatesForSkin(kv.Key, kv.Value, out var ids))
                {
                    error = "Bu skin için yeterli envanter bulunamadı.";
                    return false;
                }
                itemIds.AddRange(ids);
            }
            return true;
        }

        // Inventory-only pull used after a successful sale. Sequenced (yielded) so the
        // caller's onSold fires only after the local cache mirrors the server.
        IEnumerator SyncInventoryFromBackend()
        {
            yield return Backend.GetInventory(
                ApplyInventoryFromBackend,
                err => Debug.LogWarning("[Backend] Inventory sync after sale failed — keeping cached inventory. " + err));
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

            // Preserve full per-instance identity (itemId, source, acquiredAt) in the
            // runtime instance cache BEFORE aggregating. This keeps backend item identity
            // available for itemId-based systems without changing the save format or the
            // quantity-based UI below. Rebuild is idempotent (clears + repopulates).
            BackendInventory.Rebuild(res.items);

            var now = TimeUtil.NowUnix();

            // Backend inventory is per-instance (one object per owned unit, no aggregate
            // quantity). Aggregate by skinId so the local cache stays quantity-based:
            // N backend instances of a skinId collapse into one local entry of quantity N.
            // Using a dictionary keyed by skinId makes the rebuild idempotent and avoids
            // duplicate entries for the same skin.
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var order  = new System.Collections.Generic.List<string>();

            int received = res.items.Length;
            int unknown = 0;

            foreach (var item in res.items)
            {
                if (item == null || string.IsNullOrEmpty(item.skinId)) continue;

                if (!counts.ContainsKey(item.skinId))
                {
                    order.Add(item.skinId);
                    counts[item.skinId] = 0;

                    if (contentDatabase != null && contentDatabase.GetSkin(item.skinId) == null)
                    {
                        unknown++;
                        Debug.LogWarning("[Backend] Inventory skinId not in local catalog (kept): " + item.skinId);
                    }
                }

                counts[item.skinId] += item.EffectiveQuantity; // missing/zero quantity counts as 1
            }

            // Rebuild the cache wholesale from the aggregated counts.
            var list = Save.Data.inventory;
            list.Clear();
            foreach (var skinId in order)
            {
                list.Add(new OwnedSkinSaveEntry
                {
                    skinId = skinId,
                    quantity = counts[skinId],
                    obtainedUnix = now
                });
            }

            Statistics?.RecalculateInventoryStats(Inventory, contentDatabase, notify: false);
            Save.Save();
            GameEvents.RaiseInventoryChanged();
            Debug.Log($"[Backend] Inventory sync received {received} backend items, rebuilt {list.Count} local entries " +
                      $"({unknown} unknown to local catalog); {BackendInventory.Count} instances cached.");
        }

        public void Persist() => Save?.Save();

        void OnApplicationPause(bool pause) { if (pause) Persist(); }
        void OnApplicationQuit() => Persist();
    }

    /// <summary>
    /// Outcome of a server-authoritative upgrade, returned by
    /// <see cref="GameContext.UpgradeBackendRoutine"/> to the UpgradeScreen.
    /// When <see cref="Ok"/> is false, <see cref="Error"/> holds a user-facing message
    /// and no spin should play. When true, <see cref="Success"/> is the backend's
    /// decision and <see cref="Target"/> is the resolved target skin to reveal.
    /// </summary>
    public sealed class UpgradeBackendResult
    {
        public bool Ok;                  // request + itemId resolution + target resolve succeeded
        public bool Success;             // backend upgrade success/fail
        public float Chance;             // backend-reported chance (for log/display)
        public SkinDefinitionSO Target;  // resolved target skin
        public string Error;             // user-facing message when Ok == false
    }
}
