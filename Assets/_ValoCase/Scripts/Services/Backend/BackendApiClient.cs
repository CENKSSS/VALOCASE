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
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? "http://localhost:8080" : baseUrl.TrimEnd('/');
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
        public string guestToken;
        public string guestAccountId;
        public int vpBalance;        // server may include the starting wallet here
    }

    [Serializable]
    public sealed class WalletResponse
    {
        public int vpBalance;
    }

    [Serializable]
    public sealed class InventoryItemResponse
    {
        public string skinId;
        public int quantity;
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
