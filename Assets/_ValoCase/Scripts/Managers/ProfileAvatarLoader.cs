using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.Profile
{
    /// <summary>
    /// Scans the FaceCards folder and returns (agentName, Sprite) pairs.
    /// Reuses FileSystemSkinLoader's proven image-loading pipeline.
    ///
    /// Expected folder:  Desktop/ValorantProject/FaceCards/
    /// Expected filenames: Chamber_icon.png, Jett_icon.png, Phoenix_icon.png …
    ///   The loader strips common suffixes (_icon, _face, _portrait, _card)
    ///   so the display name is clean ("Chamber", "Jett", etc.).
    /// </summary>
    public static class ProfileAvatarLoader
    {
        static readonly HashSet<string> ImageExts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

        static readonly string[] StripSuffixes =
            { "_icon", "_face", "_portrait", "_card", "_Icon", "_Face", "_Portrait", "_Card" };

        public static string DefaultPath => ProjectPaths.FaceCardsRoot;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Loads all images in <paramref name="folderPath"/> and returns them as
        /// (agentName, Sprite) pairs sorted alphabetically.
        /// Returns an empty list if the folder does not exist.
        /// </summary>
        public static List<(string name, Sprite sprite)> LoadAll(string folderPath = null)
        {
            if (string.IsNullOrEmpty(folderPath))
                folderPath = DefaultPath;

            var result = new List<(string, Sprite)>();

            if (!Directory.Exists(folderPath))
            {
                Debug.LogWarning($"[ProfileAvatarLoader] FaceCards folder not found: {folderPath}");
                return result;
            }

            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (!ImageExts.Contains(Path.GetExtension(file))) continue;

                var agentName = CleanName(Path.GetFileNameWithoutExtension(file));
                if (string.IsNullOrWhiteSpace(agentName)) continue;

                // Reuse the production-tested loader from FileSystemSkinLoader
                var sprite = FileSystemSkinLoader.LoadSpriteFromFile(file, agentName);
                if (sprite == null) continue;

                result.Add((agentName, sprite));
                Debug.Log($"[ProfileAvatarLoader] Loaded face card: {agentName}");
            }

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
