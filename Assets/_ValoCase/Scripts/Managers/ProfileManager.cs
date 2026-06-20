using System.Collections.Generic;
using UnityEngine;
using ValoCase.UI.Widgets;

namespace ValoCase.Profile
{
    /// <summary>
    /// Application-wide profile boot and global settings modal gate.
    ///
    /// ── Startup ─────────────────────────────────────────────────────────────
    /// Call <see cref="EnsureInitialized"/> once from <c>GameContext.Awake()</c>.
    /// It loads FaceCards from disk and restores saved profile from PlayerPrefs.
    /// Subsequent calls are no-ops, so it is safe to call defensively anywhere.
    ///
    /// ── Opening the modal from any screen ───────────────────────────────────
    /// Call <see cref="OpenSettingsModal"/>.  The first call lazily creates a
    /// <see cref="PlayerProfileWidget"/> on the root Canvas (DontDestroyOnLoad),
    /// so the modal persists for the entire app lifetime independently of which
    /// UI screen is currently active.
    /// </summary>
    public static class ProfileManager
    {
        static bool _initialized;
        static readonly List<(string name, Sprite sprite)> _avatars =
            new List<(string, Sprite)>();

        // The single global modal widget (created lazily on first open)
        static PlayerProfileWidget _globalWidget;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>All loaded face-card avatars, sorted alphabetically.</summary>
        public static IReadOnlyList<(string name, Sprite sprite)> Avatars => _avatars;

        /// <summary>The sprite shown when an avatarId is null/blank/unknown (e.g. the backend
        /// "avatar_1" default that has no matching face card). First loaded avatar, or null.</summary>
        public static Sprite DefaultAvatarSprite
        {
            get
            {
                EnsureInitialized();
                return _avatars.Count > 0 ? _avatars[0].sprite : null;
            }
        }

        /// <summary>Resolves a backend avatarId (an agent face-card name) to its sprite,
        /// falling back to <see cref="DefaultAvatarSprite"/> when it does not match a
        /// loaded avatar. Used to render real players' chosen avatars in PvP.</summary>
        public static Sprite ResolveAvatarSprite(string avatarId)
        {
            EnsureInitialized();
            if (!string.IsNullOrEmpty(avatarId))
                foreach (var av in _avatars)
                    if (string.Equals(av.name, avatarId, System.StringComparison.OrdinalIgnoreCase))
                        return av.sprite;
            return DefaultAvatarSprite;
        }

        /// <summary>
        /// Loads FaceCards from disk and restores saved profile from PlayerPrefs.
        /// Idempotent — safe to call multiple times.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            var loaded = ProfileAvatarLoader.LoadAll();
            _avatars.AddRange(loaded);
            PlayerProfileData.Initialize(loaded);

            Debug.Log($"[ProfileManager] Initialized — {_avatars.Count} face cards loaded.");
        }

        /// <summary>
        /// Opens the global Settings / Profile popup modal.
        /// Creates the modal lazily on the first call (attaches to the active Canvas).
        /// </summary>
        public static void OpenSettingsModal()
        {
            EnsureInitialized();

            if (_globalWidget == null)
            {
                // Find the topmost active Canvas in the scene
                var canvas = Object.FindObjectOfType<Canvas>();
                if (canvas == null)
                {
                    Debug.LogWarning("[ProfileManager] Cannot open Settings modal — no Canvas found.");
                    return;
                }

                var go = new GameObject("GlobalSettingsModal",
                    typeof(PlayerProfileWidget));
                go.transform.SetParent(canvas.transform, false);
                Object.DontDestroyOnLoad(go);

                _globalWidget = go.GetComponent<PlayerProfileWidget>();
                _globalWidget.InitialiseModalOnly(
                    (RectTransform)canvas.transform);
            }

            _globalWidget.OpenSettings();
        }

        /// <summary>
        /// Registers an externally-created widget as the global modal instance.
        /// Called by <see cref="PlayerProfileWidget.Initialise"/> so the CaseBattle
        /// widget doubles as the global one.
        /// </summary>
        internal static void RegisterWidget(PlayerProfileWidget widget)
        {
            if (_globalWidget == null)
                _globalWidget = widget;
        }
    }
}
