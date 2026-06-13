using System;
using System.Collections.Generic;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.Profile
{
    /// <summary>
    /// Loads agent portrait sprites from Resources (Art/Avatars) and returns them
    /// as (agentName, Sprite) pairs. MOBILE-SAFE: uses Resources.LoadAll&lt;Sprite&gt;
    /// instead of System.IO, so it works in Android/iOS player builds.
    ///
    /// Expected files:  Resources/Art/Avatars/Chamber_icon.png, Jett_icon.png, …
    ///   Common suffixes (_icon, _face, _portrait, _card) are stripped so the
    ///   display name is clean ("Chamber", "Jett", etc.).
    /// </summary>
    public static class ProfileAvatarLoader
    {
        static readonly string[] StripSuffixes =
            { "_icon", "_face", "_portrait", "_card" };

        public static string DefaultPath => ProjectPaths.FaceCardsRoot;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Loads all avatar sprites under <paramref name="resourceFolder"/> and
        /// returns them as (agentName, Sprite) pairs sorted alphabetically.
        /// Returns an empty list if none are found.
        /// </summary>
        public static List<(string name, Sprite sprite)> LoadAll(string resourceFolder = null)
        {
            if (string.IsNullOrEmpty(resourceFolder))
                resourceFolder = DefaultPath;

            var result  = new List<(string, Sprite)>();
            var sprites = Resources.LoadAll<Sprite>(resourceFolder);

            if (sprites == null || sprites.Length == 0)
            {
                Debug.LogWarning($"[ProfileAvatarLoader] No avatars found in Resources/{resourceFolder}/");
                return result;
            }

            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;

                var agentName = CleanName(sprite.name);
                if (string.IsNullOrWhiteSpace(agentName)) continue;

                result.Add((agentName, sprite));
            }

            result.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase));

            Debug.Log($"[ProfileAvatarLoader] Total face cards loaded: {result.Count}");
            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            foreach (var suffix in StripSuffixes)
            {
                if (raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return raw.Substring(0, raw.Length - suffix.Length);
            }
            return raw;
        }
    }
}
