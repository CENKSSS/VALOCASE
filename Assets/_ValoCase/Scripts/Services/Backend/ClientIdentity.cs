using System;
using UnityEngine;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// The install's own identity, as the backend understands it.
    ///
    /// One owner on purpose. Onboarding telemetry and the session lifecycle report the
    /// same installationId, and the backend's funnel view joins pre-account events to
    /// post-account sessions through it — two independently generated ids would break
    /// that join silently and only show up as a funnel that never converts.
    ///
    /// The id is a random UUID generated on this device. It is not derived from an
    /// advertising id, a device id, or anything else that identifies a person or a
    /// handset, and it is never sent alongside a nickname or a token.
    /// </summary>
    public static class ClientIdentity
    {
        // Unchanged from the value AnalyticsLifecycleService has always written, so
        // existing installs keep the id they already reported.
        const string InstallationIdPrefKey = "valocase_installation_id";

        static string _installationId;

        /// <summary>
        /// Stable per-install UUID. Created and persisted on first use; safe to call from
        /// any Unity main-thread context.
        /// </summary>
        public static string InstallationId
        {
            get
            {
                if (!string.IsNullOrEmpty(_installationId)) return _installationId;
                _installationId = LoadOrCreate();
                return _installationId;
            }
        }

        /// <summary>Platform token matching the backend's ClientPlatform enum.</summary>
        public static string Platform
        {
            get
            {
#if UNITY_EDITOR
                return "EDITOR";
#elif UNITY_ANDROID
                return "ANDROID";
#elif UNITY_IOS
                return "IOS";
#else
                return "UNKNOWN";
#endif
            }
        }

        static string LoadOrCreate()
        {
            var existing = PlayerPrefs.GetString(InstallationIdPrefKey, string.Empty);
            if (Guid.TryParse(existing, out var parsed) && parsed != Guid.Empty)
                return parsed.ToString();

            var id = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(InstallationIdPrefKey, id);
            PlayerPrefs.Save();
            return id;
        }
    }
}
