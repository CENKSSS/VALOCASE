using UnityEngine;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// Single place that turns a transport/HTTP/network failure into a clean Turkish
    /// player-facing message. Player UI must never show raw exception text, endpoint
    /// URLs, JSON, DTO names, or status codes — it shows only these mapped strings.
    /// Developer detail stays in Debug.Log on the BackendError itself.
    /// </summary>
    public static class BackendErrorMapper
    {
        public const string Offline      = "İnternet bağlantısı yok. Lütfen bağlantını kontrol et.";
        public const string Timeout      = "Sunucu yanıt vermedi. Lütfen tekrar dene.";
        public const string Unauthorized = "Oturum süresi doldu. Lütfen tekrar giriş yap.";
        public const string Forbidden    = "Bu işlem için yetkin yok.";
        public const string Conflict     = "İşlem tamamlanamadı. VP bakiyeni veya mevcut durumunu kontrol et.";
        public const string NoFunds      = "Yeterli VP'n yok.";
        public const string TooManyReq   = "Çok hızlı işlem yapıyorsun. Lütfen biraz bekle.";
        public const string ServerError  = "Sunucu hatası oluştu. Lütfen biraz sonra tekrar dene.";
        public const string Generic      = "İşlem başarısız. Lütfen tekrar dene.";
        public const string Unknown      = "Beklenmeyen bir hata oluştu. Lütfen tekrar dene.";

        /// <summary>Unity-safe reachability check (works in player builds, not editor-only).</summary>
        public static bool IsOffline => Application.internetReachability == NetworkReachability.NotReachable;

        /// <summary>Maps a BackendError (may be null) to a safe Turkish message.</summary>
        public static string Map(BackendError error)
        {
            if (error == null) return IsOffline ? Offline : Unknown;
            if (error.IsOffline) return Offline;
            if (error.IsTimeout) return Timeout;

            // HttpStatus 0 == transport failure with no HTTP response.
            if (error.HttpStatus == 0) return IsOffline ? Offline : Generic;

            // Checked before the switch: the wallet case has its own message and must not
            // fall through to the generic 4xx text.
            if (error.IsInsufficientFunds) return NoFunds;

            switch (error.HttpStatus)
            {
                case 401: return Unauthorized;
                case 403: return Forbidden;
                case 409: return Conflict;
                case 429: return TooManyReq;
            }
            if (error.HttpStatus >= 500) return ServerError;
            if (error.HttpStatus >= 400) return Generic;
            return Generic;
        }

        /// <summary>
        /// Maps a failure onto the backend's NetworkErrorCategory allowlist, for the
        /// registration_failed telemetry event. Lives here so the telemetry vocabulary
        /// and the player-facing vocabulary are decided from the same error object and
        /// cannot drift into disagreeing about what went wrong.
        ///
        /// Only these seven tokens exist server-side; anything else is discarded on
        /// arrival, which is why this returns a fixed string rather than error text.
        /// No URL, hostname, or exception message is ever included.
        /// </summary>
        public static string NetworkCategory(BackendError error)
        {
            if (error == null) return "unknown";
            if (error.IsOffline) return "offline";
            if (error.IsTimeout) return "timeout";
            if (error.HttpStatus >= 400) return "http_error";
            if (error.IsInvalidResponse) return "invalid_response";

            // HttpStatus 0 is a transport failure with no HTTP response. Name resolution
            // is worth telling apart from a refused or reset connection: it usually means
            // captive-wifi or DNS trouble on the device rather than a backend problem.
            if (error.HttpStatus == 0)
                return LooksLikeDnsFailure(error.Message) ? "dns" : "transport";

            return "unknown";
        }

        static bool LooksLikeDnsFailure(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            var text = message.ToLowerInvariant();
            // UnityWebRequest phrasing varies by platform and version, so match on the
            // words all of them share rather than on one exact string.
            return text.Contains("resolve") || text.Contains("dns") || text.Contains("name not resolved");
        }
    }
}
