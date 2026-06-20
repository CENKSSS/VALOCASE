using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Progression
{
    /// <summary>
    /// Client-side, NON-authoritative cache of the player's progression as last
    /// reported by the backend. Unity never computes or awards XP locally — it only
    /// stores what the backend returned (GET /wallet, case-open response) for display
    /// and to drive case-card lock visuals. A fresh backend response always overwrites
    /// this cache. If progression has never been received, the defaults below (level 1,
    /// 0/20) keep the UI safe without any crash.
    /// </summary>
    public static class PlayerProgression
    {
        public const int DefaultXpPerLevel = 20;

        public static int Level { get; private set; } = 1;
        public static int CurrentLevelXp { get; private set; }
        public static int XpRequiredForNextLevel { get; private set; } = DefaultXpPerLevel;
        public static int TotalXp { get; private set; }
        public static IReadOnlyList<string> UnlockedCategories { get; private set; } = Array.Empty<string>();

        /// <summary>True once a backend progression snapshot has been applied this session.</summary>
        public static bool HasBackendSnapshot { get; private set; }

        /// <summary>Raised after the cache is overwritten by a backend snapshot.</summary>
        public static event Action OnChanged;

        // Category → required unlock level (mirrors the backend rules).
        static readonly Dictionary<string, int> UnlockLevels = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Classic", 1 },
            { "Ghost",   3 },
            { "Bulldog", 7 },
            { "Vandal",  9 },
            { "Melee",  15 },
        };

        // Weapon keywords used to infer a category from a case id via substring match,
        // so "vandal_basic", "protocol_melee", and "melee_arcane" all resolve correctly.
        static readonly string[] CategoryKeys = { "classic", "ghost", "bulldog", "vandal", "melee" };

        public static float Fill01 =>
            XpRequiredForNextLevel > 0 ? Mathf.Clamp01((float)CurrentLevelXp / XpRequiredForNextLevel) : 0f;

        /// <summary>Overwrites the cache with a backend-reported snapshot. Primitive
        /// arguments keep this layer free of any backend-DTO dependency.</summary>
        public static void Apply(int level, int currentLevelXp, int xpRequiredForNextLevel,
                                 int totalXp, string[] unlockedCategories)
        {
            Level                  = level > 0 ? level : 1;
            CurrentLevelXp         = Mathf.Max(0, currentLevelXp);
            XpRequiredForNextLevel = xpRequiredForNextLevel > 0 ? xpRequiredForNextLevel : DefaultXpPerLevel;
            TotalXp                = Mathf.Max(0, totalXp);
            if (unlockedCategories != null) UnlockedCategories = unlockedCategories;
            HasBackendSnapshot     = true;
            OnChanged?.Invoke();
        }

        public static int GetCurrentLevel() => Level;
        public static int GetCurrentLevelXp() => CurrentLevelXp;
        public static int GetTotalXp() => TotalXp;
        public static int GetRequiredXpForNextLevel() => XpRequiredForNextLevel;

        public static int GetUnlockLevelForCategory(string category) =>
            !string.IsNullOrEmpty(category) && UnlockLevels.TryGetValue(category, out var lvl) ? lvl : 1;

        /// <summary>Maps a case id to its weapon category via substring match.</summary>
        public static string CategoryForCaseId(string caseId)
        {
            if (string.IsNullOrEmpty(caseId)) return "Classic";
            var id = caseId.ToLowerInvariant();
            foreach (var key in CategoryKeys)
                if (id.Contains(key))
                    return char.ToUpperInvariant(key[0]) + key.Substring(1);
            return "Classic";
        }

        public static int RequiredLevelForCaseId(string caseId) =>
            GetUnlockLevelForCategory(CategoryForCaseId(caseId));

        public static bool IsCategoryUnlocked(string category)
        {
            if (string.IsNullOrEmpty(category)) return true;

            // When the backend sends an explicit unlocked-category list, it is authoritative.
            if (UnlockedCategories != null && UnlockedCategories.Count > 0)
            {
                foreach (var c in UnlockedCategories)
                    if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            // Otherwise fall back to the cached level vs the required unlock level.
            return Level >= GetUnlockLevelForCategory(category);
        }

        public static bool IsCaseUnlocked(string caseId) => IsCategoryUnlocked(CategoryForCaseId(caseId));

        // Strict unlock for actions the backend authorizes (battle create). Unknown
        // progression is locked except the always-open base tier, so the client never
        // sends a locked case the backend would 403.
        public static bool IsCategoryUnlockedAuthoritative(string category)
        {
            if (string.IsNullOrEmpty(category)) return true;

            if (UnlockedCategories != null && UnlockedCategories.Count > 0)
            {
                foreach (var c in UnlockedCategories)
                    if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            if (HasBackendSnapshot) return Level >= GetUnlockLevelForCategory(category);
            return GetUnlockLevelForCategory(category) <= 1;
        }

        public static bool IsCaseUnlockedAuthoritative(string caseId)
            => IsCategoryUnlockedAuthoritative(CategoryForCaseId(caseId));
    }
}
