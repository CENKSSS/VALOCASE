using System;
using System.Collections;
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

        readonly string _baseUrl;
        readonly int _timeoutSeconds;

        /// <summary>Current guest token; sent as X-Guest-Token on authed requests.</summary>
        public string GuestToken { get; set; }

        public BackendApiClient(string baseUrl, int timeoutSeconds, string guestToken = null)
        {
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://valocase-backend-production.up.railway.app" : baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 15;
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
            => Send("POST", ApiPrefix + "/cases/" + UnityWebRequest.EscapeURL(caseId) + "/open", "{}", auth: true, onSuccess, onError);

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
        public IEnumerator Upgrade(string[] inputItemIds, string targetSkinId,
                                   Action<UpgradeResponse> onSuccess, Action<BackendError> onError)
            => Send("POST", ApiPrefix + "/upgrade",
                    JsonUtility.ToJson(new UpgradeRequest { inputItemIds = inputItemIds, targetSkinId = targetSkinId }),
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

        // ── Core request pipeline ───────────────────────────────────────────────

        IEnumerator Send<T>(string method, string path, string body, bool auth,
                            Action<T> onSuccess, Action<BackendError> onError,
                            string wrapArrayKey = null) where T : class
        {
            var url = _baseUrl + path;

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

            // Verification log: the exact token sent on each authenticated call
            // (covers GET /wallet and POST /cases/{id}/open).
            if (auth)
                Debug.Log($"[BackendAuth] -> {method} {path} | X-Guest-Token=" +
                          (string.IsNullOrEmpty(GuestToken) ? "<none>" : GuestToken));

            yield return req.SendWebRequest();

            // Transport-level failure (no HTTP response, DNS, timeout, etc.)
            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
            {
                onError?.Invoke(new BackendError(0, $"{method} {path} -> {req.error}"));
                yield break;
            }

            var status = (int)req.responseCode;
            var text = req.downloadHandler != null ? req.downloadHandler.text : null;

            // HTTP error status (4xx/5xx)
            if (req.result == UnityWebRequest.Result.ProtocolError || status >= 400)
            {
                var message = req.error;
                var parsed = TryParse<ErrorResponse>(text, null);
                if (parsed != null && !string.IsNullOrEmpty(parsed.message))
                    message = parsed.message;
                onError?.Invoke(new BackendError(status, $"{method} {path} -> HTTP {status}: {message}"));
                yield break;
            }

            // Success — parse JSON body into T
            var result = TryParse<T>(text, wrapArrayKey);
            if (result == null)
            {
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

    // ── Error envelope (transport-agnostic) ─────────────────────────────────────
    public sealed class BackendError
    {
        public readonly int HttpStatus; // 0 == transport/connection failure (no HTTP response)
        public readonly string Message;
        public BackendError(int httpStatus, string message) { HttpStatus = httpStatus; Message = message; }
        public bool IsAuthError => HttpStatus == 401 || HttpStatus == 403;
        public override string ToString() => $"BackendError(status={HttpStatus}, msg={Message})";
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
        public int vpBalance;        // server may include the starting wallet here

        public string ResolveToken()
        {
            if (!string.IsNullOrEmpty(guestToken)) return guestToken;
            if (!string.IsNullOrEmpty(token))      return token;
            return accountId;        // no-JWT guest: the token is the account UUID
        }
    }

    [Serializable]
    public sealed class WalletResponse
    {
        public string accountId;     // for same-account verification across boots
        public int vpBalance;
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
        // NOTE: the backend does not return a vpSpent field. The amount spent is
        // derived caller-side from the selected case price (see CaseOpeningFlowController).
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

    [Serializable]
    public sealed class ErrorResponse
    {
        public int status;
        public string error;
        public string message;
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
