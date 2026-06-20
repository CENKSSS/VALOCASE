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

        // Backend (Spring Boot). Exposed so flows can reuse the client.
        public BackendApiClient Backend { get; private set; }

        // SECURITY: local-authoritative economy is allowed ONLY in the editor or an
        // explicit offline-demo build, and only when the asset opts out of backend.
        // Release/player builds always fail closed — never local rewards/VP.
        public bool CanUseLocalEconomy
        {
            get
            {
#if UNITY_EDITOR || OFFLINE_DEMO
                return gameConfig == null || !gameConfig.UseBackend;
#else
                return false;
#endif
            }
        }

        // Routing flag every screen branches on. True for all release/player builds and
        // for any editor session that opted into backend; false only for an allowed
        // local-economy editor/demo session. Independent of whether the client is ready,
        // so an unavailable backend fails closed instead of falling back to local.
        public bool BackendEnabled => !CanUseLocalEconomy;

        // Backend client is constructed and ready to send requests.
        public bool BackendReady => Backend != null;

        // Device-visible battle-lobby diagnostics (no token/account values exposed).
        public string BackendBaseUrl => gameConfig != null ? gameConfig.BackendBaseUrl : null;
        public bool HasGuestToken => Backend != null && !string.IsNullOrEmpty(Backend.GuestToken);
        public bool HasGuestAccountId => Save?.Data != null && !string.IsNullOrEmpty(Save.Data.guestAccountId);

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

            // Starting/loaded balance comes from the normal save flow (and, in backend
            // mode, is overwritten by the authoritative wallet during BackendBootSync).
            // No client-side starter/admin grant is applied here. The legacy
            // adminVpGrantApplied save field is intentionally left untouched and unused
            // so old saves still load.

            if (Vp != null)
                GameEvents.RaiseVpChanged(Vp.Balance, Vp.Balance);
            GameEvents.RaiseStatisticsChanged();
            GameEvents.RaiseShopRotated();

            // Boot profile system — loads FaceCards + restores PlayerPrefs once
            ProfileManager.EnsureInitialized();

#if (DEVELOPMENT_BUILD && !UNITY_EDITOR)
            if (gameConfig != null && !gameConfig.UseBackend)
                Debug.LogWarning("[ValoCase] Local economy mode is disabled in player builds.");
#endif

            TryStartBackendSync();
        }

        // ── Backend boot sync ───────────────────────────────────────────────────
        // No-op in local mode. In backend mode: ensure a guest token, then pull the
        // authoritative wallet + inventory into the local save (used as a cache).
        // Any failure leaves the cached local state intact — never faked as success.
        void TryStartBackendSync()
        {
            if (gameConfig == null) return;
            // Boot the backend client when the asset opts in OR when backend is mandatory
            // (release/player builds), so a misconfigured useBackend=false still connects.
            if (!gameConfig.UseBackend && CanUseLocalEconomy) return;
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
                        AdoptBackendAvatar(res.avatarId);
                        Debug.Log("[BackendAuth] Guest registered and token resolved.");
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
                Debug.Log("[BackendAuth] Reusing saved guest token.");
            }

            // 2) Wallet — backend is authoritative; overwrite the local cached balance.
            yield return Backend.GetWallet(
                res =>
                {
                    Vp?.SetBalance(res.vpBalance);
                    ProgressionSync.ApplyFromWallet(res);   // mirror backend level/XP into the UI cache
                    Save.Save();
                    Debug.Log($"[BackendAuth] Wallet synced — vp={res.vpBalance}");
                },
                err => Debug.LogWarning("[Backend] Wallet sync failed — keeping cached balance. " + err));

            // 3) Inventory — replace the local cached inventory wholesale (idempotent;
            //    no duplication on repeat sync).
            yield return Backend.GetInventory(
                ApplyInventoryFromBackend,
                err => Debug.LogWarning("[Backend] Inventory sync failed — keeping cached inventory. " + err));

            Debug.Log("[Backend] Boot sync complete.");
        }

        // Adopt a backend avatarId into the local profile cache, but only when it maps to a
        // real loaded face card. The generic "avatar_1" default has no card, so the local
        // default sprite is kept — new players still show the default avatar.
        void AdoptBackendAvatar(string avatarId)
        {
            if (string.IsNullOrEmpty(avatarId)) return;
            foreach (var av in ProfileManager.Avatars)
                if (string.Equals(av.name, avatarId, StringComparison.OrdinalIgnoreCase))
                {
                    PlayerProfileData.SetAvatar(av.sprite, av.name);
                    return;
                }
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
            if (!BackendReady) return;
            StartCoroutine(ResyncWalletAndInventory());
        }

        IEnumerator ResyncWalletAndInventory()
        {
            yield return Backend.GetWallet(
                res => { Vp?.SetBalance(res.vpBalance); ProgressionSync.ApplyFromWallet(res); Save.Save(); Debug.Log("[Backend] Wallet re-synced — vp=" + res.vpBalance); },
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
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
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
                onFailed?.Invoke(BackendErrorMapper.Map(error));
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
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            StartCoroutine(SellBulkRoutine(
                run: (ok, err) => Backend.SellAll(ok, err),
                onSold, onFailed, label: "sell all"));
        }

        // Sell every owned skin valued at or below maxVpValue.
        public void SellBelowValueBackend(int maxVpValue, Action<int, int> onSold, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
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
                onFailed?.Invoke(BackendErrorMapper.Map(error));
                yield break;
            }

            ApplyBackendWallet(response.newVpBalance);   // authoritative; no local Add
            yield return SyncInventoryFromBackend();      // reconcile removed items
            Debug.Log($"[Backend] Bulk {label} OK — soldCount={response.soldCount} totalVpGained={response.totalVpGained} newBalance={response.newVpBalance}");
            onSold?.Invoke(response.soldCount, response.totalVpGained);
        }

        // ── Backend account display name (server-authoritative profile) ─────────
        // Backend trims/validates and echoes the stored name. Only on success do we
        // update the local cache (save + PlayerProfileData) so names never diverge from
        // the server. A 400 surfaces a clear validation message; nothing is cached.
        public void SaveDisplayNameBackend(string displayName, Action<string> onSaved, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            var trimmed = (displayName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) { onFailed?.Invoke("İsim boş olamaz."); return; }
            if (trimmed.Length > 20) trimmed = trimmed.Substring(0, 20);
            StartCoroutine(SaveDisplayNameRoutine(trimmed, onSaved, onFailed));
        }

        IEnumerator SaveDisplayNameRoutine(string displayName, Action<string> onSaved, Action<string> onFailed)
        {
            DisplayNameResponse res = null;
            BackendError error = null;
            yield return Backend.SetDisplayName(displayName, r => res = r, e => error = e);

            if (error != null || res == null)
            {
                Debug.LogWarning("[Backend] Display name save failed — " + (error?.ToString() ?? "null response"));
                var msg = error != null && error.HttpStatus == 400
                    ? "İsim geçersiz. En fazla 20 karakter olmalı."
                    : BackendErrorMapper.Map(error);
                onFailed?.Invoke(msg);
                yield break;
            }

            var saved = !string.IsNullOrEmpty(res.displayName) ? res.displayName : displayName;
            Save.Data.playerName = saved;
            Save.Save();
            PlayerProfileData.SetUsername(saved);
            Debug.Log("[Backend] Display name saved.");
            onSaved?.Invoke(saved);
        }

        // ── Backend account avatar (server-authoritative profile) ───────────────
        // Avatar is server-owned like the display name: the backend trims/validates and
        // echoes the stored avatarId. The local cache (PlayerProfileData) is updated only
        // on success via onSaved, so a real player's avatar is the same one other clients
        // receive in lobby/battle responses. A failure surfaces a message and caches nothing.
        public void SaveAvatarBackend(string avatarId, Action<string> onSaved, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            var trimmed = (avatarId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) { onFailed?.Invoke("Avatar geçersiz."); return; }
            if (trimmed.Length > 50) trimmed = trimmed.Substring(0, 50);
            StartCoroutine(SaveAvatarRoutine(trimmed, onSaved, onFailed));
        }

        IEnumerator SaveAvatarRoutine(string avatarId, Action<string> onSaved, Action<string> onFailed)
        {
            AvatarResponse res = null;
            BackendError error = null;
            yield return Backend.SetAvatar(avatarId, r => res = r, e => error = e);

            if (error != null || res == null)
            {
                Debug.LogWarning("[Backend] Avatar save failed — " + (error?.ToString() ?? "null response"));
                var msg = error != null && error.HttpStatus == 400
                    ? "Avatar geçersiz."
                    : BackendErrorMapper.Map(error);
                onFailed?.Invoke(msg);
                yield break;
            }

            var saved = !string.IsNullOrEmpty(res.avatarId) ? res.avatarId : avatarId;
            Debug.Log("[Backend] Avatar saved.");
            onSaved?.Invoke(saved);
        }

        // ── Backend daily rewards (server-authoritative) ────────────────────────
        // Fetch returns the status to the UI; claim applies the authoritative wallet
        // (no local VP add) and returns the claim payload. Failures surface a message
        // and never mutate local VP.

        public void RefreshDailyBackend(Action<DailyStatusResponse> onDone, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            StartCoroutine(RefreshDailyRoutine(onDone, onFailed));
        }

        IEnumerator RefreshDailyRoutine(Action<DailyStatusResponse> onDone, Action<string> onFailed)
        {
            DailyStatusResponse res = null;
            BackendError error = null;
            yield return Backend.GetDailyStatus(r => res = r, e => error = e);

            if (error != null || res == null)
            {
                Debug.LogWarning("[Backend] Daily status failed — " + (error?.ToString() ?? "null response"));
                onFailed?.Invoke(BackendErrorMapper.Map(error));
                yield break;
            }
            onDone?.Invoke(res);
        }

        public void ClaimDailyBackend(Action<DailyClaimResponse> onDone, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            StartCoroutine(ClaimDailyRoutine(onDone, onFailed));
        }

        IEnumerator ClaimDailyRoutine(Action<DailyClaimResponse> onDone, Action<string> onFailed)
        {
            DailyClaimResponse res = null;
            BackendError error = null;
            yield return Backend.ClaimDailyReward(r => res = r, e => error = e);

            if (error != null || res == null)
            {
                Debug.LogWarning("[Backend] Daily claim failed — " + (error?.ToString() ?? "null response"));
                onFailed?.Invoke(BackendErrorMapper.Map(error));
                yield break;
            }

            ApplyBackendWallet(res.newVpBalance);   // authoritative; no local Add
            Debug.Log($"[Backend] Daily claimed — rewardVp={res.rewardVp} streak={res.currentStreak} newBalance={res.newVpBalance}");
            onDone?.Invoke(res);
        }

        // ── Backend Earn VP session (server-authoritative; server calculates VP) ──

        public void ClaimEarnVpSession(int tapCount, long sessionDurationMs, string clientSessionId,
            int[] tapOffsetsMs, Action<EarnVpClaimResponse> onDone, Action<string> onFailed)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EarnVp] ClaimEarnVpSession — BackendReady={BackendReady} tapCount={tapCount} durationMs={sessionDurationMs} offsetsCount={(tapOffsetsMs != null ? tapOffsetsMs.Length : 0)} sessionId={clientSessionId}");
#endif
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            if (tapCount <= 0 || string.IsNullOrEmpty(clientSessionId)) { onFailed?.Invoke("Geçersiz oturum."); return; }
            StartCoroutine(ClaimEarnVpRoutine(tapCount, sessionDurationMs, clientSessionId, tapOffsetsMs, onDone, onFailed));
        }

        IEnumerator ClaimEarnVpRoutine(int tapCount, long sessionDurationMs, string clientSessionId,
            int[] tapOffsetsMs, Action<EarnVpClaimResponse> onDone, Action<string> onFailed)
        {
            EarnVpClaimResponse res = null;
            BackendError error = null;
            yield return Backend.ClaimEarnVpSession(tapCount, sessionDurationMs, clientSessionId, tapOffsetsMs,
                r => res = r, e => error = e);

            if (error != null || res == null)
            {
                var mapped = BackendErrorMapper.Map(error);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[EarnVp] claim FAILED — userMsg=\"{mapped}\" status={(error != null ? error.HttpStatus : -1)} offline={(error != null && error.IsOffline)} timeout={(error != null && error.IsTimeout)} detail={(error != null ? error.ToString() : "null response")}");
#endif
                onFailed?.Invoke(mapped);
                yield break;
            }

            int oldBalance = Vp?.Balance ?? 0;
            ApplyBackendWallet(res.newBalance);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EarnVp] claim OK — vpGranted={res.vpGranted} accepted={res.acceptedTapCount} newBalance={res.newBalance} msg={res.message}; wallet {oldBalance} -> {(Vp?.Balance ?? 0)}");
#endif
            onDone?.Invoke(res);
        }

        // ── Backend missions (server-authoritative) ─────────────────────────────

        public void RefreshMissionsBackend(Action<MissionResponse[]> onDone, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            StartCoroutine(RefreshMissionsRoutine(onDone, onFailed));
        }

        IEnumerator RefreshMissionsRoutine(Action<MissionResponse[]> onDone, Action<string> onFailed)
        {
            MissionListResponse res = null;
            BackendError error = null;
            yield return Backend.GetMissions(r => res = r, e => error = e);

            if (error != null || res == null)
            {
                Debug.LogWarning("[Backend] Missions fetch failed — " + (error?.ToString() ?? "null response"));
                onFailed?.Invoke(BackendErrorMapper.Map(error));
                yield break;
            }
            onDone?.Invoke(res.missions ?? Array.Empty<MissionResponse>());
        }

        public void ClaimMissionBackend(string missionId, Action<MissionClaimResponse> onDone, Action<string> onFailed)
        {
            if (!BackendReady) { onFailed?.Invoke("Sunucu kullanılamıyor."); return; }
            if (string.IsNullOrEmpty(missionId)) { onFailed?.Invoke("Geçersiz görev."); return; }
            StartCoroutine(ClaimMissionRoutine(missionId, onDone, onFailed));
        }

        IEnumerator ClaimMissionRoutine(string missionId, Action<MissionClaimResponse> onDone, Action<string> onFailed)
        {
            MissionClaimResponse res = null;
            BackendError error = null;
            yield return Backend.ClaimMissionReward(missionId, r => res = r, e => error = e);

            if (error != null || res == null)
            {
                Debug.LogWarning("[Backend] Mission claim failed — " + (error?.ToString() ?? "null response"));
                onFailed?.Invoke(BackendErrorMapper.Map(error));
                yield break;
            }

            ApplyBackendWallet(res.newVpBalance);   // authoritative; no local Add
            Debug.Log($"[Backend] Mission claimed — missionId={missionId} rewardVp={res.rewardVp} newBalance={res.newVpBalance} status={res.status}");
            onDone?.Invoke(res);
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
            List<SkinDefinitionSO> inputs, IReadOnlyList<SkinDefinitionSO> targets, Action<UpgradeBackendResult> onResult)
        {
            var target = targets != null && targets.Count > 0 ? targets[0] : null;
            var result = new UpgradeBackendResult { Target = target };

            if (!BackendReady)
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

            var targetSkinIds = new List<string>();
            foreach (var t in targets)
                if (t != null) targetSkinIds.Add(t.SkinId);

            UpgradeResponse response = null;
            BackendError error = null;
            yield return Backend.Upgrade(itemIds.ToArray(), target.SkinId, targetSkinIds.ToArray(),
                r => response = r,
                e => error = e);

            if (error != null || response == null)
            {
                Debug.LogWarning("[Backend] Upgrade failed — " + (error?.ToString() ?? "null response"));
                // Ambiguous transport failure (status 0) may have committed server-side.
                if (error == null || error.HttpStatus == 0) RequestBackendResync();
                result.Error = BackendErrorMapper.Map(error);
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
