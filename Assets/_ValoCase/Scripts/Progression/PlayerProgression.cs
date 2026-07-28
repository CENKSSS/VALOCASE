using System;
using System.Collections.Generic;
using UnityEngine;
using ValoCase.Data;

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
        public const int MaxLevel = 15;

        // Cumulative total XP required to REACH each level (index = level). Mirrors the
        // backend threshold table; used ONLY as a display fallback when the backend omits
        // the per-level XP breakdown. Never authorizes unlocks.
        static readonly int[] LevelTotalXp =
        {
            0,    // [0] unused
            0,    // Lv 1
            40,   // Lv 2
            95,   // Lv 3
            160,  // Lv 4
            250,  // Lv 5
            350,  // Lv 6
            465,  // Lv 7
            610,  // Lv 8
            775,  // Lv 9
            860,  // Lv 10
            945,  // Lv 11
            1050, // Lv 12
            1155, // Lv 13
            1250, // Lv 14
            1350, // Lv 15
        };

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

        public static bool IsMaxLevel => Level >= MaxLevel;

        public static float Fill01
        {
            get
            {
                if (IsMaxLevel) return 1f;
                return XpRequiredForNextLevel > 0
                    ? Mathf.Clamp01((float)CurrentLevelXp / XpRequiredForNextLevel) : 0f;
            }
        }

        /// <summary>Overwrites the cache with a backend-reported snapshot. Primitive
        /// arguments keep this layer free of any backend-DTO dependency.</summary>
        public static void Apply(int level, int currentLevelXp, int xpRequiredForNextLevel,
                                 int totalXp, string[] unlockedCategories)
        {
            Level   = level > 0 ? level : 1;
            TotalXp = Mathf.Max(0, totalXp);

            if (xpRequiredForNextLevel > 0)
            {
                CurrentLevelXp         = Mathf.Max(0, currentLevelXp);
                XpRequiredForNextLevel = xpRequiredForNextLevel;
            }
            else
            {
                ApplyTableFallback(currentLevelXp);   // backend omitted per-level breakdown
            }

            if (unlockedCategories != null) UnlockedCategories = unlockedCategories;
            HasBackendSnapshot     = true;
            OnChanged?.Invoke();
        }

        static void ApplyTableFallback(int backendCurrentLevelXp)
        {
            if (IsMaxLevel)
            {
                CurrentLevelXp         = Mathf.Max(0, TotalXp - LevelTotalXp[MaxLevel]);
                XpRequiredForNextLevel = 0;
                return;
            }

            int lvl     = Mathf.Clamp(Level, 1, MaxLevel - 1);
            int floorXp = LevelTotalXp[lvl];
            XpRequiredForNextLevel = Mathf.Max(1, LevelTotalXp[lvl + 1] - floorXp);
            CurrentLevelXp = TotalXp > 0
                ? Mathf.Clamp(TotalXp - floorXp, 0, XpRequiredForNextLevel)
                : Mathf.Max(0, backendCurrentLevelXp);
        }

        // Display-only: true when a level transition crossed a new category-unlock tier
        // (3/7/9/15). Level 1 (Classic) never counts — play starts already at level 1.
        public static bool CrossedNewUnlockLevel(int previousLevel, int newLevel)
        {
            if (newLevel <= previousLevel) return false;
            foreach (var kv in UnlockLevels)
                if (kv.Value > previousLevel && kv.Value <= newLevel) return true;
            return false;
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

            // Cumulative authored rule: every tier at or below the player's level is unlocked.
            if (Level >= GetUnlockLevelForCategory(category)) return true;

            // A backend list may grant an extra unlock, but it can never re-lock a tier the
            // level rule already opened — the list can be partial or arrive a frame late.
            if (UnlockedCategories != null)
                foreach (var c in UnlockedCategories)
                    if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        public static bool IsCaseUnlocked(string caseId) => IsCategoryUnlocked(CategoryForCaseId(caseId));

        /// <summary>Resolves a case id to a weapon category, or null when it matches none
        /// (unlike <see cref="CategoryForCaseId"/>, which defaults unknown ids to Classic).</summary>
        public static string TryResolveCategory(string caseId)
        {
            if (string.IsNullOrEmpty(caseId)) return null;
            var id = caseId.ToLowerInvariant();
            foreach (var key in CategoryKeys)
                if (id.Contains(key))
                    return char.ToUpperInvariant(key[0]) + key.Substring(1);
            return null;
        }

        /// <summary>Required unlock level for a case. Authored Level data wins; otherwise the
        /// weapon-category map is used. Returns 0 when neither resolves (treated as locked).</summary>
        public static int RequiredLevelForCase(string caseId, CaseUnlockType unlockType, int unlockRequirement)
        {
            if (unlockType == CaseUnlockType.Level && unlockRequirement > 0)
                return unlockRequirement;
            var category = TryResolveCategory(caseId);
            return category != null ? GetUnlockLevelForCategory(category) : 0;
        }

        /// <summary>Unlock state for a case. Authored Level/Achievement data is authoritative;
        /// otherwise the weapon-category map is used. Unknown category with no authored data
        /// stays locked rather than defaulting to Classic.</summary>
        public static bool IsCaseUnlocked(string caseId, CaseUnlockType unlockType, int unlockRequirement)
        {
            if (unlockType == CaseUnlockType.Level && unlockRequirement > 0)
                return Level >= unlockRequirement;
            if (unlockType == CaseUnlockType.Achievement)
                return false;
            var category = TryResolveCategory(caseId);
            return category != null && IsCategoryUnlocked(category);
        }

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
