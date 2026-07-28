using System.Collections.Generic;
using UnityEngine;
using ValoCase.Core;
using ValoCase.Data;

namespace ValoCase.CaseOpening
{
    public static class CaseReelBuilder
    {
        public static List<SkinDefinitionSO> BuildReelStrip(CaseDefinitionSO caseDef, SkinDefinitionSO winner, int totalItems, int winnerIndex)
        {
            var strip = new List<SkinDefinitionSO>(totalItems);
            var pool = BuildPool(caseDef);
            if (pool.Count == 0) pool.Add(winner);

            var weights = caseDef?.DropTable?.RarityWeights;

            for (var i = 0; i < totalItems; i++)
                strip.Add(i == winnerIndex ? winner : PickWeighted(pool, weights));

            return strip;
        }

        // Builds the flat pool of possible drop skins for a case (used by the
        // optimistic warmup spin to draw random filler items before the backend
        // result is known). No winner involved — purely cosmetic filler source.
        public static List<SkinDefinitionSO> BuildPool(CaseDefinitionSO caseDef)
        {
            var pool = new List<SkinDefinitionSO>();
            var drops = caseDef?.DropTable?.PossibleDrops;
            if (drops != null)
                foreach (var d in drops)
                    if (d.skin != null) pool.Add(d.skin);
            return pool;
        }

        // Picks one filler skin from a prebuilt pool. The case's authored rarity weights
        // drive the distribution so the visual reel matches real drop odds. Returns null
        // only when the pool is empty.
        public static SkinDefinitionSO PickFiller(List<SkinDefinitionSO> pool, IReadOnlyList<RarityWeightEntry> rarityWeights)
        {
            if (pool == null || pool.Count == 0) return null;
            return PickWeighted(pool, rarityWeights);
        }

        // Chooses a rarity by its authored weight (only rarities actually present in the
        // pool count), then a random skin of that rarity. Without usable weights it falls
        // back to a uniform pick over the pool. This keeps the reel visual close to the
        // backend roll odds instead of over-showing rarities that happen to have many
        // distinct skins (e.g. a ~1% Melee tier no longer floods the strip).
        static SkinDefinitionSO PickWeighted(List<SkinDefinitionSO> pool, IReadOnlyList<RarityWeightEntry> rarityWeights)
        {
            if (pool == null || pool.Count == 0) return null;

            if (rarityWeights != null && rarityWeights.Count > 0)
            {
                float total = 0f;
                foreach (var w in rarityWeights)
                    if (w != null && w.weightPercent > 0f && HasRarity(pool, w.rarity))
                        total += w.weightPercent;

                if (total > 0f)
                {
                    var r = Random.value * total;
                    foreach (var w in rarityWeights)
                    {
                        if (w == null || w.weightPercent <= 0f || !HasRarity(pool, w.rarity)) continue;
                        r -= w.weightPercent;
                        if (r <= 0f)
                        {
                            var pick = PickByRarity(pool, w.rarity);
                            if (pick != null) return pick;
                        }
                    }
                }
            }

            return pool[Random.Range(0, pool.Count)];
        }

        static bool HasRarity(List<SkinDefinitionSO> pool, SkinRarity rarity)
        {
            foreach (var s in pool)
                if (s != null && s.Rarity == rarity) return true;
            return false;
        }

        static SkinDefinitionSO PickByRarity(List<SkinDefinitionSO> pool, SkinRarity rarity)
        {
            var matches = pool.FindAll(s => s != null && s.Rarity == rarity);
            if (matches.Count == 0) return null;
            return matches[Random.Range(0, matches.Count)];
        }
    }
}
