using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Profile
{
    /// <summary>
    /// Runtime singleton for the local player's profile.
    /// Data is persisted to PlayerPrefs so it survives app restarts.
    ///
    /// Call <see cref="Initialize"/> once (from PlayerProfileWidget.Initialise)
    /// with the loaded avatar list to resolve the saved avatar key → Sprite.
    ///
    /// Subscribe to <see cref="OnProfileChanged"/> to react to any mutation.
    /// </summary>
    public static class PlayerProfileData
    {
        // ── PlayerPrefs keys ──────────────────────────────────────────────────
        const string PrefUsername  = "ValoCase_Profile_Username";
        const string PrefAvatarKey = "ValoCase_Profile_AvatarKey";

        // ── State ─────────────────────────────────────────────────────────────
        static string _username  = "Agent";
        static Sprite _avatar;
        static string _avatarKey = "";

        // ── Properties ───────────────────────────────────────────────────────
        public static string Username  => _username;
        public static Sprite Avatar    => _avatar;
        public static string AvatarKey => _avatarKey;   // e.g. "Jett", "Reyna"

        // ── Event ─────────────────────────────────────────────────────────────
        public static event Action OnProfileChanged;

        // ── Initialization ────────────────────────────────────────────────────

        /// <summary>
        /// Loads the saved profile from PlayerPrefs and resolves the avatar sprite
        /// from <paramref name="availableAvatars"/>.  Call exactly once at startup.
        /// </summary>
        public static void Initialize(List<(string name, Sprite sprite)> availableAvatars)
        {
            // Username
            _username = PlayerPrefs.GetString(PrefUsername, "Agent");
            if (string.IsNullOrWhiteSpace(_username)) _username = "Agent";

            // Avatar — resolve saved key → Sprite
            var savedKey = PlayerPrefs.GetString(PrefAvatarKey, "");
            _avatar    = null;
            _avatarKey = "";

            if (!string.IsNullOrEmpty(savedKey) && availableAvatars != null)
            {
                foreach (var av in availableAvatars)
                {
                    if (string.Equals(av.name, savedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _avatar    = av.sprite;
                        _avatarKey = av.name;
                        break;
                    }
                }
            }

            // Fallback to first avatar if saved key not found or nothing saved
            if (_avatar == null && availableAvatars != null && availableAvatars.Count > 0)
            {
                _avatar    = availableAvatars[0].sprite;
                _avatarKey = availableAvatars[0].name;
            }
        }

        // ── Mutators ──────────────────────────────────────────────────────────

        /// <summary>Sets a new username, persists to PlayerPrefs, fires <see cref="OnProfileChanged"/>.</summary>
        public static void SetUsername(string name)
        {
            _username = string.IsNullOrWhiteSpace(name) ? "Agent" : name.Trim();
            PlayerPrefs.SetString(PrefUsername, _username);
            PlayerPrefs.Save();
            OnProfileChanged?.Invoke();
        }

        /// <summary>Sets the avatar sprite/key, persists to PlayerPrefs, fires <see cref="OnProfileChanged"/>.</summary>
        public static void SetAvatar(Sprite sprite, string key)
        {
            _avatar    = sprite;
            _avatarKey = key ?? "";
            PlayerPrefs.SetString(PrefAvatarKey, _avatarKey);
            PlayerPrefs.Save();
            OnProfileChanged?.Invoke();
        }
    }
}
