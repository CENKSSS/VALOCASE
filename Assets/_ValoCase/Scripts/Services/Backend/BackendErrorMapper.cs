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
    }
}
