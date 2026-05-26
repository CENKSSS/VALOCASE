using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Data
{
    /// <summary>
    /// Single source of truth for the rarity hierarchy, VP values, labels and upgrade maths.
    ///
    /// Global rank order (0 = weakest → 4 = strongest):
    ///   0  Özel      Select
    ///   1  Üstün     Deluxe
    ///   2  İhtişamlı Premium
    ///   3  Ultra     Ultra
    ///   4  Seçkin    Exclusive
    ///
    /// All screens, services and loaders must use this class instead of
    /// hardcoding their own VP tables, labels or ordering logic.
    /// </summary>
    public static class RaritySystem
    {
        // ── Explicit hierarchy ────────────────────────────────────────────────
        // Ordered weakest → strongest.  Use this array (not Enum.GetValues)
        // whenever a deterministic rarity order is required.
        public static readonly SkinRarity[] OrderedRarities =
        {
            SkinRarity.Select,     // Özel      rank 0
            SkinRarity.Deluxe,     // Üstün     rank 1
            SkinRarity.Premium,    // İhtişamlı rank 2
            SkinRarity.Ultra,      // Ultra     rank 3
            SkinRarity.Exclusive,  // Seçkin    rank 4
        };

        // ── Rank lookup ───────────────────────────────────────────────────────
        static readonly Dictionary<SkinRarity, int> _rankMap =
            new Dictionary<SkinRarity, int>();

        static RaritySystem()
        {
            for (var i = 0; i < OrderedRarities.Length; i++)
                _rankMap[OrderedRarities[i]] = i;
        }

        /// <summary>Returns the hierarchy rank (0 = lowest, 4 = highest).</summary>
        public static int GetRank(SkinRarity rarity) =>
            _rankMap.TryGetValue(rarity, out var r) ? r : 0;

        // ── VP values ─────────────────────────────────────────────────────────
        // Özel=1000  Üstün=2000  İhtişamlı=3000  Ultra=4000  Seçkin=5000
        public static int GetVp(SkinRarity rarity) => rarity switch
        {
            SkinRarity.Select    => 1000,
            SkinRarity.Deluxe    => 2000,
            SkinRarity.Premium   => 3000,
            SkinRarity.Ultra     => 4000,
            SkinRarity.Exclusive => 5000,
            _                    => 1000,
        };

        // ── Display labels ────────────────────────────────────────────────────
        /// <summary>Full Turkish labels ("Özel Seri", "Seçkin Seri", …).</summary>
        public static readonly Dictionary<SkinRarity, string> Labels =
            new Dictionary<SkinRarity, string>
            {
                { SkinRarity.Select,    "Özel Seri"      },
                { SkinRarity.Deluxe,    "Üstün Seri"     },
                { SkinRarity.Premium,   "İhtişamlı Seri" },
                { SkinRarity.Ultra,     "Ultra Seri"      },
                { SkinRarity.Exclusive, "Seçkin Seri"    },
            };

        /// <summary>Short Turkish labels ("Özel", "Seçkin", …) for pills and chips.</summary>
        public static readonly Dictionary<SkinRarity, string> ShortLabels =
            new Dictionary<SkinRarity, string>
            {
                { SkinRarity.Select,    "Özel"      },
                { SkinRarity.Deluxe,    "Üstün"     },
                { SkinRarity.Premium,   "İhtişamlı" },
                { SkinRarity.Ultra,     "Ultra"     },
                { SkinRarity.Exclusive, "Seçkin"    },
            };

        // ── Upgrade eligibility ───────────────────────────────────────────────
        /// <summary>
        /// A target is a valid upgrade destination when its rank is ≥ the input rank.
        /// Same rarity  → 100 % chance trade.
        /// Higher rarity → decreasing chance (see ComputeChance).
        /// Lower rarity  → NOT eligible (disabled in UI, blocked in service).
        /// </summary>
        public static bool IsEligibleTarget(SkinRarity input, SkinRarity target) =>
            GetRank(target) >= GetRank(input);

        // ── Upgrade chance ────────────────────────────────────────────────────
        /// <summary>
        /// chance = 100 – (rankDifference × 20) clamped to [0, 1].
        ///
        /// Same rarity (+0) → 1.00 (100 %)
        /// +1 rank          → 0.80  (80 %)
        /// +2 ranks         → 0.60  (60 %)
        /// +3 ranks         → 0.40  (40 %)
        /// +4 ranks         → 0.20  (20 %)
        /// Lower rank       → 0.00   (0 %)
        /// </summary>
        public static float ComputeChance(SkinRarity input, SkinRarity target)
        {
            var dist = GetRank(target) - GetRank(input);
            var pct  = 100 - dist * 20;
            return Mathf.Clamp(pct, 0, 100) / 100f;
        }
    }
}
