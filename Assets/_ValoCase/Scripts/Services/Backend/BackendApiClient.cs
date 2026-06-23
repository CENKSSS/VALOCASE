using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ValoCase.Services;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// Step-1 Spring Boot client. Owns HTTP only — no economy rules, no save writes,
    /// no RNG. All methods are coroutine-driven (return IEnumerator) so the caller's
    /// MonoBehaviour (GameContext) hosts them and the main thread is never blocked.
    ///
    /// Server-authoritative endpoints covered:
    ///   POST /api/v1/guest
    ///   GET  /api/v1/wallet
    ///   GET  /api/v1/inventory
    ///   POST /api/v1/cases/{caseId}/open   (capability present; live flow wired in Step 2)
    ///
    /// Auth: the guest token is sent as the X-Guest-Token header on authenticated
    /// calls. <see cref="GuestToken"/> is set after a successful guest registration.
    ///
    /// NOTE ON DTO FIELD NAMES: JsonUtility maps by exact field name. The DTO fields
    /// below assume conventional Spring Boot JSON keys; if the backend uses different
    /// keys, adjust ONLY the DTO field names here — nothing else changes.
    /// </summary>
    public sealed class BackendApiClient
    {
        const string ApiPrefix = "/api/v1";

        /// <summary>Shared default request timeout (seconds). One value, not scattered.</summary>
        public const int DefaultTimeoutSeconds = 15;

        readonly string _baseUrl;
        readonly int _timeoutSeconds;

        /// <summary>Current guest token; sent as X-Guest-Token on authed requests.</summary>
        public string GuestToken { get; set; }

        public BackendApiClient(string baseUrl, int timeoutSeconds, string guestToken = null)
        {
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://valocase-backend-production.up.railway.app" : baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
            GuestToken = guestToken;
        }

        // ── Endpoints ─────────────────────────────────────────────────────────

        public IEnumerator RegisterGuest(Action<GuestRegisterResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/guest", "{}", auth: false, onSuccess, onError);

        public IEnumerator GetWallet(Action<WalletResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/wallet", null, auth: true, onSuccess, onError);

        public IEnumerator GetInventory(Action<InventoryResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/inventory", null, auth: true, onSuccess, onError, wrapArrayKey: "items");

        public IEnumerator OpenCase(string caseId, Action<OpenCaseResultResponse> onSuccess, Action<BackendError> onError)
        {
            var path = ApiPrefix + "/cases/" + UnityWebRequest.EscapeURL(caseId) + "/open";
            var debugTag = caseId == "melee_case" ? caseId : null;
            return Send("POST", path, "{}", auth: true, onSuccess, onError, debugTag: debugTag);
        }

        // ── Case catalog (server-authoritative economy values, read-only) ───────
        // GET returns a bare array; wrap under "cases" so JsonUtility can map it.
        public IEnumerator GetCases(Action<CaseSummaryListResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/cases", null, auth: true, onSuccess, onError, wrapArrayKey: "cases");

        public IEnumerator GetCaseDetail(string caseId, Action<CaseDetailResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/cases/" + UnityWebRequest.EscapeURL(caseId), null, auth: true, onSuccess, onError);

        // ── Account display name (server-authoritative profile) ─────────────────
        // Backend trims, validates (required, max 20), persists, and echoes the stored
        // displayName. Unity updates its local cache only after this succeeds.
        public IEnumerator SetDisplayName(string displayName, Action<DisplayNameResponse> onSuccess, Action<BackendError> onError)
            => Send("PUT", ApiPrefix + "/account/display-name",
                    JsonUtility.ToJson(new DisplayNameRequest { displayName = displayName }),
                    auth: true, onSuccess, onError);

        // Backend trims, validates (required, safe chars, max 50), persists, and echoes
        // the stored avatarId. Unity updates its local cache only after this succeeds.
        public IEnumerator SetAvatar(string avatarId, Action<AvatarResponse> onSuccess, Action<BackendError> onError)
            => Send("PUT", ApiPrefix + "/account/avatar",
                    JsonUtility.ToJson(new AvatarRequest { avatarId = avatarId }),
                    auth: true, onSuccess, onError);

        // ── Inventory selling (server-authoritative) ────────────────────────────
        // Backend validates ownership, removes inventory, credits VP, writes the
        // transaction, and returns the authoritative wallet. Unity never mutates VP
        // or inventory before these succeed.

        public IEnumerator SellOne(string skinId, Action<SellOneResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/inventory/sell",
                    JsonUtility.ToJson(new SellOneRequest { skinId = skinId }),
                    auth: true, onSuccess, onError);

        public IEnumerator SellAll(Action<SellBulkResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/inventory/sell-all", "{}", auth: true, onSuccess, onError);

        public IEnumerator SellBelowValue(int maxVpValue, Action<SellBulkResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/inventory/sell-below-value",
                    JsonUtility.ToJson(new SellBelowValueRequest { maxVpValue = maxVpValue }),
                    auth: true, onSuccess, onError);

        // ── Upgrade (server-authoritative) ──────────────────────────────────────
        // Backend consumes the given per-instance itemIds, decides success/failure,
        // and grants the target only on success. Unity sends real itemIds (never
        // skinIds) and never consumes or grants locally.
        public IEnumerator Upgrade(string[] inputItemIds, string targetSkinId, string[] targetSkinIds,
                                   Action<UpgradeResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/upgrade",
                    JsonUtility.ToJson(new UpgradeRequest { inputItemIds = inputItemIds, targetSkinId = targetSkinId, targetSkinIds = targetSkinIds }),
                    auth: true, onSuccess, onError);

        // ── Upgrade preview (server-authoritative chance, read-only) ────────────
        public IEnumerator UpgradePreview(string[] inputInventoryItemIds, string targetSkinId,
                                          Action<UpgradePreviewResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/upgrade/preview",
                    JsonUtility.ToJson(new UpgradePreviewRequest { inputInventoryItemIds = inputInventoryItemIds, targetSkinId = targetSkinId }),
                    auth: true, onSuccess, onError);

        // ── Case Battle (server-authoritative, bots) ────────────────────────────
        // Backend charges entry cost, rolls every participant's rounds, decides the
        // winner, grants rewards, and returns the full result + authoritative wallet.
        // Unity only presents the result and refreshes local state.
        public IEnumerator CreateBotBattle(string caseId, int rounds, int participantCount,
                                           Action<BotBattleResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/battles/bot",
                    JsonUtility.ToJson(new BotBattleRequest
                    {
                        caseId = caseId,
                        rounds = rounds,
                        participantCount = participantCount
                    }),
                    auth: true, onSuccess, onError);

        // ── Case Battle public lobbies (server-authoritative, online) ───────────
        // The backend owns lobby lifecycle, slot assignment, the add-bot delay, the
        // entry-cost charge, the roll/winner resolution and reward grants. Unity only
        // creates/joins/cancels, polls for state, and presents the result. The legacy
        // CreateBotBattle path above stays as a dev/offline fallback.
        public IEnumerator CreateBattleLobby(List<CaseSelectionRequest> caseSelections, int maxSlots,
                                             Action<LobbyResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/battles/lobbies",
                    JsonUtility.ToJson(new CreateLobbyRequest { maxSlots = maxSlots, caseSelections = caseSelections }),
                    auth: true, onSuccess, onError, debugTag: "BATTLE_CREATE");

        // GET returns a bare array; wrap under "lobbies" so JsonUtility can map it.
        // If the backend already returns { "lobbies": [...] } the wrap is skipped
        // (only applied when the body starts with '['), so this stays compatible.
        public IEnumerator GetBattleLobbies(Action<LobbyListResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/battles/lobbies", null, auth: true, onSuccess, onError,
                    wrapArrayKey: "lobbies", debugTag: "BATTLE_LIST");

        public IEnumerator GetBattleLobby(string battleId, Action<LobbyResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/battles/lobbies/" + UnityWebRequest.EscapeURL(battleId), null,
                    auth: true, onSuccess, onError);

        public IEnumerator JoinBattleLobby(string battleId, int slotIndex, Action<LobbyResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/battles/lobbies/" + UnityWebRequest.EscapeURL(battleId) + "/join",
                    JsonUtility.ToJson(new JoinLobbyRequest { slotIndex = slotIndex }),
                    auth: true, onSuccess, onError);

        public IEnumerator AddBotToBattleLobby(string battleId, Action<LobbyResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/battles/lobbies/" + UnityWebRequest.EscapeURL(battleId) + "/add-bot", "{}",
                    auth: true, onSuccess, onError);

        public IEnumerator CancelBattleLobby(string battleId, Action<LobbyResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/battles/lobbies/" + UnityWebRequest.EscapeURL(battleId) + "/cancel", "{}",
                    auth: true, onSuccess, onError);

        // ── Daily rewards (server-authoritative) ────────────────────────────────
        public IEnumerator GetDailyStatus(Action<DailyStatusResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/daily", null, auth: true, onSuccess, onError);

        public IEnumerator ClaimDailyReward(Action<DailyClaimResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/daily/claim", "{}", auth: true, onSuccess, onError);

        // ── Missions (server-authoritative) ─────────────────────────────────────
        // GET returns a bare array; wrap it under "missions" so JsonUtility can map it.
        public IEnumerator GetMissions(Action<MissionListResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/missions", null, auth: true, onSuccess, onError, wrapArrayKey: "missions");

        public IEnumerator ClaimMissionReward(string missionId, Action<MissionClaimResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/missions/" + UnityWebRequest.EscapeURL(missionId) + "/claim", "{}",
                    auth: true, onSuccess, onError);

        // ── Leaderboards (server-authoritative, read-only) ──────────────────────
        public IEnumerator GetLeaderboard(string type, Action<LeaderboardResponse> onSuccess, Action<BackendError> onError)
            => Send("GET", ApiPrefix + "/leaderboards?type=" + UnityWebRequest.EscapeURL(type), null,
                    auth: true, onSuccess, onError);

        // ── Earn VP session start (server-authoritative session window) ─────────────
        public IEnumerator StartEarnVpSession(Action<EarnVpStartResponse> onSuccess, Action<BackendError> onError)
        {
            const string path = "/api/earn-vp/session/start";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EarnVp] POST {_baseUrl}{path} auth={!string.IsNullOrEmpty(GuestToken)}");
#endif
            return Send("POST", path, "{}", auth: true, onSuccess, onError);
        }

        // ── Earn VP session (server-authoritative; server calculates the VP) ────────
        public IEnumerator ClaimEarnVpSession(int tapCount, long sessionDurationMs, string clientSessionId,
            int[] tapOffsetsMs, Action<EarnVpClaimResponse> onSuccess, Action<BackendError> onError)
        {
            const string path = "/api/earn-vp/session/claim";
            var body = JsonUtility.ToJson(new EarnVpClaimRequest
            {
                tapCount = tapCount,
                sessionDurationMs = sessionDurationMs,
                clientSessionId = clientSessionId,
                tapOffsetsMs = tapOffsetsMs
            });
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EarnVp] POST {_baseUrl}{path} auth={!string.IsNullOrEmpty(GuestToken)} body={body}");
#endif
            return Send("POST", path, body, auth: true, onSuccess, onError);
        }

        // ── Rewarded ads (server-authoritative; claim only ARMS the reward) ─────────
        // Status is context-aware: the upgrade placement needs the current selection and
        // the earn-vp placement may carry the current earn session. A bare array is wrapped
        // under "placements"; an object that already wraps it is left as-is.
        public IEnumerator GetAdRewardStatus(string[] inputItemIds, string[] targetSkinIds, string earnSessionId,
            Action<AdRewardStatusResponse> onSuccess, Action<BackendError> onError)
        {
            var sb = new StringBuilder(ApiPrefix + "/ads/rewards/status");
            bool first = true;
            void Append(string key, string value)
            {
                sb.Append(first ? '?' : '&'); first = false;
                sb.Append(key).Append('=').Append(UnityWebRequest.EscapeURL(value));
            }
            if (!string.IsNullOrEmpty(earnSessionId)) Append("earnSessionId", earnSessionId);
            if (inputItemIds != null)
                foreach (var id in inputItemIds) if (!string.IsNullOrEmpty(id)) Append("inputItemIds", id);
            if (targetSkinIds != null)
                foreach (var id in targetSkinIds) if (!string.IsNullOrEmpty(id)) Append("targetSkinIds", id);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var inputCount = inputItemIds != null ? inputItemIds.Length : 0;
            var targetCount = targetSkinIds != null ? targetSkinIds.Length : 0;
            var context = !string.IsNullOrEmpty(earnSessionId) ? "earn" : (inputCount > 0 || targetCount > 0 ? "upgrade" : "none");
            Debug.Log($"[AdsStatus] context={context} authPresent={!string.IsNullOrEmpty(GuestToken)} earnSessionIdPresent={!string.IsNullOrEmpty(earnSessionId)} inputItemIds={inputCount} targetSkinIds={targetCount}");
#endif
            return Send("GET", sb.ToString(), null, auth: true, onSuccess, onError, wrapArrayKey: "placements");
        }

        // Claim is sent ONLY after a rewarded ad reports completed. It ARMS the reward for
        // the given context; it does not grant VP. EARN_VP_2X is applied later by the normal
        // earn-vp claim; UPGRADE_PLUS_5 is applied by upgrade preview/execute server-side.
        public IEnumerator ClaimAdReward(AdRewardClaimRequest request,
            Action<AdRewardClaimResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/ads/rewards/claim",
                    JsonUtility.ToJson(request), auth: true, onSuccess, onError);

        // Clears the active tap-screen 2x bonus when leaving the Earn VP screen.
        public IEnumerator ClearEarnVp2x(string earnSessionId,
            Action<AdRewardStatusResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/ads/rewards/earn-vp-2x/clear",
                    JsonUtility.ToJson(new AdRewardClearRequest { earnSessionId = earnSessionId }),
                    auth: true, onSuccess, onError);

        // ── Core request pipeline ───────────────────────────────────────────────

        IEnumerator Send<T>(string method, string path, string body, bool auth,
                            Action<T> onSuccess, Action<BackendError> onError,
                            string wrapArrayKey = null, string debugTag = null) where T : class
        {
            var url = _baseUrl + path;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugTag != null)
                Debug.Log($"[BACKEND_DEBUG] {debugTag} -> {method} {url} authed={(auth && !string.IsNullOrEmpty(GuestToken))} body={body}");
#endif
            // Battle-lobby request trace stays on in player builds so the live multi-device
            // create/list flow can be diagnosed from device logs (no token value emitted).
            if (debugTag != null && debugTag.StartsWith("BATTLE"))
                Debug.Log($"[BATTLE_LOBBY_DIAG] REQ {debugTag} {method} {url} authed={(auth && !string.IsNullOrEmpty(GuestToken))} body={body}");

            // Offline pre-check at the shared layer: never even open the socket when the
            // device reports no reachability. Callers map this to the offline message and
            // restore their UI; no spend/grant/inventory logic runs.
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugTag != null)
                    Debug.LogWarning($"[CASE_OPEN_ERROR] {debugTag} aborted BEFORE request — offline (no reachability)");
#endif
                onError?.Invoke(new BackendError(0, $"{method} {path} -> offline (no reachability)",
                                                 isTimeout: false, isOffline: true));
                yield break;
            }

            using var req = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds
            };

            if (!string.IsNullOrEmpty(body))
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));

            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            if (auth && !string.IsNullOrEmpty(GuestToken))
                req.SetRequestHeader("X-Guest-Token", GuestToken);

            // Never log the token/account id itself — only whether the call is
            // authenticated. Keeps tokens out of player/release logs.
            if (auth)
                Debug.Log($"[BackendAuth] -> {method} {path} | " +
                          (string.IsNullOrEmpty(GuestToken) ? "auth missing" : "authenticated"));

            yield return req.SendWebRequest();

            // Transport-level failure (no HTTP response, DNS, timeout, etc.)
            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                var errText  = req.error ?? string.Empty;
                bool timeout = errText.ToLowerInvariant().Contains("timeout") ||
                               errText.ToLowerInvariant().Contains("timed out");
                bool offline = Application.internetReachability == NetworkReachability.NotReachable;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugTag != null)
                    Debug.LogWarning($"[CASE_OPEN_ERROR] {debugTag} transport failure DURING request — result={req.result} error={req.error} timeout={timeout} offline={offline}");
#endif
                onError?.Invoke(new BackendError(0, $"{method} {path} -> {req.error}",
                                                 isTimeout: timeout, isOffline: offline));
                yield break;
            }

            var status = (int)req.responseCode;
            var text = req.downloadHandler != null ? req.downloadHandler.text : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugTag != null)
                Debug.Log($"[BACKEND_DEBUG] {debugTag} <- HTTP {status} rawBody={text}");
#endif
            if (debugTag != null && debugTag.StartsWith("BATTLE"))
                Debug.Log($"[BATTLE_LOBBY_DIAG] RES {debugTag} HTTP {status} rawBody={text}");

            // HTTP error status (4xx/5xx)
            if (req.result == UnityWebRequest.Result.ProtocolError || status >= 400)
            {
                var message = req.error;
                var parsed = TryParse<ErrorResponse>(text, null);
                if (parsed != null && !string.IsNullOrEmpty(parsed.message))
                    message = parsed.message;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugTag != null)
                    Debug.LogError($"[CASE_OPEN_ERROR] {debugTag} HTTP {status} AFTER response — {method} {path} | mappedMessage=\"{message}\" | rawBody={text}");
#endif
                onError?.Invoke(new BackendError(status, $"{method} {path} -> HTTP {status}: {message}",
                                                 lockedCategory: parsed?.category,
                                                 requiredLevel: parsed?.requiredLevel ?? 0,
                                                 currentLevel: parsed?.currentLevel ?? 0));
                yield break;
            }

            // Success — parse JSON body into T
            var result = TryParse<T>(text, wrapArrayKey);
            if (result == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (debugTag != null)
                    Debug.LogError($"[CASE_OPEN_ERROR] {debugTag} response parse FAILED AFTER response — DTO={typeof(T).Name} | rawBody={text}");
#endif
                onError?.Invoke(new BackendError(status, $"{method} {path} -> could not parse response body"));
                yield break;
            }

            onSuccess?.Invoke(result);
        }

        // JsonUtility cannot deserialize a top-level JSON array. When the backend
        // returns a bare array (e.g. GET /inventory -> [ {...}, {...} ]) we wrap it
        // as { "<wrapArrayKey>": [ ... ] } so it maps onto a DTO with that field.
        static T TryParse<T>(string text, string wrapArrayKey) where T : class
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var json = text.TrimStart();
            if (!string.IsNullOrEmpty(wrapArrayKey) && json.Length > 0 && json[0] == '[')
                json = "{\"" + wrapArrayKey + "\":" + json + "}";

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackendApiClient] JSON parse failed for {typeof(T).Name}: {e.Message}");
                return null;
            }
        }
    }

    [Serializable]
    public sealed class EarnVpClaimRequest
    {
        public int tapCount;
        public long sessionDurationMs;
        public string clientSessionId;
        public int[] tapOffsetsMs;
    }

    [Serializable]
    public sealed class EarnVpClaimResponse
    {
        public int vpGranted;
        public int newBalance;
        public int acceptedTapCount;
        public string message;   // "OK" or "DUPLICATE"
    }

    [Serializable]
    public sealed class EarnVpStartResponse
    {
        public string sessionId;
        public long maxDurationMs;
        public int maxTapRatePerSecond;
        public int maxTaps;
    }

    // ── Error envelope (transport-agnostic) ─────────────────────────────────────
    public sealed class BackendError
    {
        public readonly int HttpStatus; // 0 == transport/connection failure (no HTTP response)
        public readonly string Message;
        public readonly bool IsTimeout; // transport failure caused by a request timeout
        public readonly bool IsOffline; // request not sent / failed due to no reachability

        // Populated only for a 403 locked-category error (otherwise null/0).
        public readonly string LockedCategory;
        public readonly int RequiredLevel;
        public readonly int CurrentLevel;

        public BackendError(int httpStatus, string message, bool isTimeout = false, bool isOffline = false,
                            string lockedCategory = null, int requiredLevel = 0, int currentLevel = 0)
        {
            HttpStatus     = httpStatus;
            Message        = message;
            IsTimeout      = isTimeout;
            IsOffline      = isOffline;
            LockedCategory = lockedCategory;
            RequiredLevel  = requiredLevel;
            CurrentLevel   = currentLevel;
        }
        public bool IsAuthError => HttpStatus == 401 || HttpStatus == 403;
        public bool IsLockedCategory => HttpStatus == 403 && !string.IsNullOrEmpty(LockedCategory);
        public override string ToString() => $"BackendError(status={HttpStatus}, timeout={IsTimeout}, offline={IsOffline}, msg={Message})";
    }

    // ── Response DTOs (JsonUtility-serializable; adjust field names to backend) ──

    [Serializable]
    public sealed class GuestRegisterResponse
    {
        // Field names mirror the backend guest JSON. The wallet endpoint uses
        // "accountId" (confirmed), so the guest endpoint is expected to as well.
        // Token aliases are declared so whichever key the server emits is captured;
        // in the Phase-1 no-JWT guest model the token can BE the account UUID, so
        // accountId is the final fallback. Read the effective token via ResolveToken().
        public string accountId;
        public string guestToken;
        public string token;
        public string displayName;   // backend-assigned default (AgentXXXX) for new guests
        public string avatarId;      // backend-assigned default (avatar_1) for new guests
        public int vpBalance;        // server may include the starting wallet here

        public string ResolveToken()
        {
            if (!string.IsNullOrEmpty(guestToken)) return guestToken;
            if (!string.IsNullOrEmpty(token))      return token;
            return accountId;        // no-JWT guest: the token is the account UUID
        }
    }

    // Player level / XP progression. Returned inside the wallet and case-open
    // responses. All fields are optional from a parsing standpoint — JsonUtility
    // leaves this object null when the backend omits it, so every reader null-checks.
    [Serializable]
    public sealed class ProgressionResponse
    {
        public int level;
        public int currentLevelXp;
        public int xpRequiredForNextLevel;
        public int totalXp;
        public int xpGranted;   // case-open response only
        public bool leveledUp;  // case-open response only
        public string[] unlockedCategories;
    }

    [Serializable]
    public sealed class WalletResponse
    {
        public string accountId;     // for same-account verification across boots
        public int vpBalance;
        public ProgressionResponse progression;   // null on older backends — readers null-check
    }

    [Serializable]
    public sealed class InventoryItemResponse
    {
        // Backend inventory is PER-INSTANCE: one object per owned item, identified by
        // itemId, with no aggregated quantity. The fields below mirror that shape.
        // skinId is the only one Unity needs for the local cache; the rest are kept so
        // JsonUtility maps cleanly and future displays can use them without re-fetching.
        public string itemId;
        public string skinId;
        public string displayName;
        public string weapon;
        public string rarity;
        public int vpValue;
        public string imageRef;
        public string source;
        public string acquiredAt;

        // Optional legacy aggregate. Backend per-instance items omit this, so it stays
        // 0; ApplyInventoryFromBackend treats a missing/zero quantity as 1 (one instance).
        public int quantity;

        // Each backend object represents one owned unit. If a quantity is present and
        // positive (legacy aggregate shape) honor it; otherwise count the instance as 1.
        public int EffectiveQuantity => quantity > 0 ? quantity : 1;
    }

    [Serializable]
    public sealed class InventoryResponse
    {
        public InventoryItemResponse[] items;
    }

    [Serializable]
    public sealed class WonSkinResponse
    {
        public string skinId;
        public string displayName;
        public string weapon;
        public string rarity;
        public int vpValue;
        public string imageRef;
    }

    [Serializable]
    public sealed class OpenCaseResultResponse
    {
        public string openingId;
        public string caseId;
        public WonSkinResponse wonSkin;
        public int newVpBalance;        // authoritative wallet balance AFTER the open
        public string inventoryItemId;
        public ProgressionResponse progression;   // level/XP after this open; null on older backends
        // NOTE: the backend does not return a vpSpent field. The amount spent is
        // derived caller-side from the selected case price (see CaseOpeningFlowController).
    }

    // ── Case catalog DTOs (player-aware fields default when no token / absent) ──

    [Serializable]
    public sealed class CaseSummaryResponse
    {
        public string caseId;
        public string displayName;
        public int priceVp;
        public string weaponCategory;
        public int requiredLevel;
        public bool canOpen;
        public string lockedReason;
        public int currentLevel;
        public bool affordable;
    }

    [Serializable]
    public sealed class CaseSummaryListResponse
    {
        public CaseSummaryResponse[] cases;
    }

    [Serializable]
    public sealed class CaseDropResponse
    {
        public string skinId;
        public string displayName;
        public string weapon;
        public string rarity;
        public int vpValue;
        public string imageRef;
        public float dropChance;
    }

    [Serializable]
    public sealed class CaseDetailResponse
    {
        public string caseId;
        public string displayName;
        public int priceVp;
        public string weaponCategory;
        public int requiredLevel;
        public bool canOpen;
        public string lockedReason;
        public int currentLevel;
        public bool affordable;
        public int expectedValueVp;
        public CaseDropResponse[] drops;
    }

    // ── Account display name DTOs ───────────────────────────────────────────────

    [Serializable]
    public sealed class DisplayNameRequest
    {
        public string displayName;
    }

    [Serializable]
    public sealed class DisplayNameResponse
    {
        public string accountId;
        public string displayName;
    }

    // ── Account avatar DTOs ─────────────────────────────────────────────────────

    [Serializable]
    public sealed class AvatarRequest
    {
        public string avatarId;
    }

    [Serializable]
    public sealed class AvatarResponse
    {
        public string accountId;
        public string avatarId;
    }

    // ── Sell request/response DTOs ──────────────────────────────────────────────

    [Serializable]
    public sealed class SellOneRequest
    {
        public string skinId;
    }

    [Serializable]
    public sealed class SellBelowValueRequest
    {
        public int maxVpValue;
    }

    [Serializable]
    public sealed class SellOneResponse
    {
        public string skinId;
        public int vpGained;
        public int newVpBalance;   // authoritative wallet balance AFTER the sale
    }

    [Serializable]
    public sealed class SellBulkResponse
    {
        public int soldCount;
        public int totalVpGained;
        public int newVpBalance;   // authoritative wallet balance AFTER the sale
    }

    // ── Upgrade request/response DTOs ───────────────────────────────────────────

    [Serializable]
    public sealed class UpgradeRequest
    {
        public string[] inputItemIds;
        public string targetSkinId;
        public string[] targetSkinIds;
    }

    [Serializable]
    public sealed class UpgradePreviewRequest
    {
        public string[] inputInventoryItemIds;
        public string targetSkinId;
    }

    [Serializable]
    public sealed class UpgradeResponse
    {
        public string upgradeId;
        public bool success;
        public float chance;
        public string[] consumedItemIds;
        public string targetSkinId;
        public string grantedInventoryItemId;   // informational for now (null on failure)
    }

    [Serializable]
    public sealed class UpgradePreviewResponse
    {
        public bool canUpgrade;
        public float chancePercent;
        public string reason;
        public int inputValue;
        public int targetValue;
    }

    // ── Case Battle request/response DTOs ───────────────────────────────────────

    [Serializable]
    public sealed class BotBattleRequest
    {
        public string caseId;
        public int rounds;
        public int participantCount;
    }

    [Serializable]
    public sealed class BotBattleRoundResponse
    {
        public string skinId;
        public string displayName;
        public string weapon;
        public string rarity;
        public int vpValue;
        public string imageRef;
    }

    [Serializable]
    public sealed class BotBattleParticipantResponse
    {
        public int index;
        public bool isUser;
        public string name;
        public string avatarId;
        public int totalVp;
        public BotBattleRoundResponse[] rounds;
    }

    [Serializable]
    public sealed class BotBattleResponse
    {
        public string battleId;
        public string caseId;
        public int rounds;
        public int entryCost;
        public int newVpBalance;                 // authoritative wallet AFTER the battle
        public int winnerIndex;
        public bool userWon;
        public string[] grantedInventoryItemIds; // informational; Unity resyncs inventory
        public BotBattleParticipantResponse[] participants;
    }

    // ── Public battle lobby DTOs ────────────────────────────────────────────────
    // Field names mirror the backend LobbyResponse / Slot contract exactly. Parsing is
    // tolerant: any field the backend omits is left at its default and every reader
    // null-checks (JsonUtility never throws on missing keys). The slot per-round shape
    // reuses BotBattleRoundResponse so the completed-lobby → BattleResult mapper can
    // resolve skins the same way the legacy bot path does.

    [Serializable]
    public sealed class CaseSelectionRequest
    {
        public string caseId;
        public int quantity;
    }

    [Serializable]
    public sealed class CreateLobbyRequest
    {
        public int maxSlots;
        public List<CaseSelectionRequest> caseSelections;
    }

    [Serializable]
    public sealed class CaseSelectionResponse
    {
        public string caseId;
        public string caseName;
        public int quantity;
        public int priceVp;
    }

    [Serializable]
    public sealed class JoinLobbyRequest
    {
        public int slotIndex;
    }

    [Serializable]
    public sealed class LobbyCreatorResponse
    {
        public string accountId;
        public string displayName;
        public string avatarId;
    }

    [Serializable]
    public sealed class LobbySlotResponse
    {
        public int slotIndex;
        public string type;            // EMPTY | REAL | BOT
        public string accountId;
        public string displayName;
        public string avatarId;
        public bool addBotAllowed;
        public int totalVp;
        public BotBattleRoundResponse[] rounds;   // per-round rolled skins (completed lobby)
    }

    [Serializable]
    public sealed class LobbyResponse
    {
        public string battleId;
        public string status;          // WAITING | STARTING | COMPLETED | CANCELLED
        public LobbyCreatorResponse creator;
        public string caseId;
        public string caseName;
        public CaseSelectionResponse[] caseSelections;
        public int rounds;
        public int entryCost;
        public int maxSlots;
        public int filledSlots;
        public LobbySlotResponse[] slots;
        public string createdAt;
        public string addBotAvailableAt;
        public bool addBotAvailable;
        public string readyAt;
        public int winnerSlotIndex;
        public string winnerDisplayName;
        public string winnerAvatarId;
        public ProgressionResponse progression;   // present on completed PvP; null otherwise
    }

    [Serializable]
    public sealed class LobbyListResponse
    {
        public LobbyResponse[] lobbies;
    }

    // ── Daily reward DTOs ───────────────────────────────────────────────────────

    [Serializable]
    public sealed class DailyStatusResponse
    {
        public bool claimable;
        public int currentStreak;
        public int nextRewardVp;
        public string lastClaimDate;
        public string nextClaimDate;
        public long secondsUntilNextClaim;
    }

    [Serializable]
    public sealed class DailyClaimResponse
    {
        public int rewardVp;
        public int currentStreak;
        public int newVpBalance;       // authoritative wallet AFTER the claim
        public string claimDate;
    }

    // ── Mission DTOs ────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class MissionResponse
    {
        public string missionId;
        public string code;
        public string title;
        public string description;
        public string eventType;
        public int targetCount;
        public int progress;
        public int rewardVp;
        public string status;          // IN_PROGRESS | CLAIMABLE | CLAIMED
        public string nextResetAt;     // backend-authoritative reset time (ISO-8601 or epoch)
        public long secondsUntilReset; // backend-provided remaining seconds (preferred when > 0)
    }

    [Serializable]
    public sealed class MissionListResponse
    {
        public MissionResponse[] missions;
    }

    [Serializable]
    public sealed class MissionClaimResponse
    {
        public int rewardVp;
        public int newVpBalance;       // authoritative wallet AFTER the claim
        public string status;
    }

    // ── Leaderboard DTOs ────────────────────────────────────────────────────────

    [Serializable]
    public sealed class LeaderboardEntryResponse
    {
        public int rank;
        public string displayName;
        public string avatarKey;
        public long value;
        public string secondaryValue;
    }

    [Serializable]
    public sealed class LeaderboardMeResponse
    {
        public int rank;
        public string rankLabel;
        public string displayName;
        public string avatarKey;
        public long value;
        public string secondaryValue;
    }

    [Serializable]
    public sealed class LeaderboardResponse
    {
        public string type;
        public LeaderboardEntryResponse[] entries;
        public LeaderboardMeResponse me;
    }

    // ── Rewarded ad DTOs ────────────────────────────────────────────────────────
    // No daily-limit fields: EARN_VP_2X and UPGRADE_PLUS_5 have no daily cap.

    [Serializable]
    public sealed class AdRewardClaimRequest
    {
        public string   rewardType;      // EARN_VP_2X | UPGRADE_PLUS_5
        public string   adToken;         // per-watch token from the rewarded ad; backend requires it to arm the reward
        public string   earnSessionId;   // EARN_VP_2X context
        public string[] inputItemIds;    // UPGRADE_PLUS_5 context
        public string   targetSkinId;
        public string[] targetSkinIds;
    }

    [Serializable]
    public sealed class AdRewardClearRequest
    {
        public string earnSessionId;
    }

    [Serializable]
    public sealed class AdRewardPlacementStatus
    {
        public string rewardType;
        public bool   isAvailable;
        public int    remainingToday;
        public string unavailableReason;
        public bool   earnVp2xActive;
        public long   earnVp2xRemainingSeconds;
        public bool   upgradePlus5Active;
        public bool   upgradePlus5AlreadyUsedForCurrentContext;
        public long   cooldownRemainingSeconds;   // non-authoritative unless non-zero
    }

    [Serializable]
    public sealed class AdRewardStatusResponse
    {
        public AdRewardPlacementStatus[] placements;

        public AdRewardPlacementStatus Find(string rewardType)
        {
            if (placements == null || string.IsNullOrEmpty(rewardType)) return null;
            foreach (var p in placements)
                if (p != null && p.rewardType == rewardType) return p;
            return null;
        }
    }

    [Serializable]
    public sealed class AdRewardClaimResponse
    {
        public string rewardType;
        public bool   earnVp2xActive;
        public long   earnVp2xRemainingSeconds;
        public bool   upgradePlus5Active;
        public bool   upgradePlus5AlreadyUsedForCurrentContext;
        public long   cooldownRemainingSeconds;
        public string message;
        public bool   isAvailable;
        public string unavailableReason;
        public long   grantedVp;
        public long   newVpBalance;
        public long   marketRemainingClaims;
        public bool   marketCooldownActive;
        public long   marketCooldownRemainingSeconds;
    }

    [Serializable]
    public sealed class ErrorResponse
    {
        public int status;
        public string error;
        public string message;
        // Present on a 403 locked-category error so the client can show the exact
        // unlock level. Absent (null/0) on every other error.
        public string category;
        public int requiredLevel;
        public int currentLevel;
    }

    /// <summary>
    /// Pure read-only mapper: backend OpenCaseResultResponse -> existing ID-based
    /// <see cref="CaseOpeningResult"/>. Does NOT resolve skins, spend VP, grant
    /// inventory, or persist. Skin resolution stays caller-side via
    /// ContentDatabaseSO.GetSkin(result.RolledSkinId).
    /// </summary>
    public static class BackendResultMapper
    {
        public static CaseOpeningResult ToCaseOpeningResult(OpenCaseResultResponse r)
        {
            if (r == null) return null;
            // vpSpent is 0 here: the backend response carries no spend field, so the
            // caller derives it from the case price. The authoritative wallet comes
            // from r.newVpBalance, applied separately.
            return new CaseOpeningResult(
                r.caseId,
                r.wonSkin != null ? r.wonSkin.skinId : null,
                r.wonSkin != null ? r.wonSkin.rarity : null,
                0);
        }
    }
}
