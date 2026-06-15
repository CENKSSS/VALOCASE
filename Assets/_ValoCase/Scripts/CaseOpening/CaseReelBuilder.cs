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
            var drops = caseDef.DropTable.PossibleDrops;
            var pool = new List<SkinDefinitionSO>();
            foreach (var d in drops)
            {
                if (d.skin != null) pool.Add(d.skin);
            }

            if (pool.Count == 0) pool.Add(winner);

            for (var i = 0; i < totalItems; i++)
            {
                if (i == winnerIndex)
                {
                    strip.Add(winner);
                    continue;
                }

                // Bias filler toward common rarities for visual authenticity
                var roll = Random.value;
                SkinDefinitionSO pick;
                if (roll < 0.55f) pick = PickByRarity(pool, SkinRarity.Select) ?? pool[Random.Range(0, pool.Count)];
                else if (roll < 0.8f) pick = PickByRarity(pool, SkinRarity.Deluxe) ?? pool[Random.Range(0, pool.Count)];
                else pick = pool[Random.Range(0, pool.Count)];
                strip.Add(pick);
            }

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

        // Picks one filler skin from a prebuilt pool, biased toward common rarities
        // for the same visual cadence the determined strip uses. Returns null only
        // when the pool is empty.
        public static SkinDefinitionSO PickFiller(List<SkinDefinitionSO> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            var roll = Random.value;
            if (roll < 0.55f) return PickByRarity(pool, SkinRarity.Select) ?? pool[Random.Range(0, pool.Count)];
            if (roll < 0.8f)  return PickByRarity(pool, SkinRarity.Deluxe) ?? pool[Random.Range(0, pool.Count)];
            return pool[Random.Range(0, pool.Count)];
        }

        static SkinDefinitionSO PickByRarity(List<SkinDefinitionSO> pool, SkinRarity rarity)
        {
            var matches = pool.FindAll(s => s.Rarity == rarity);
            if (matches.Count == 0) return null;
            return matches[Random.Range(0, matches.Count)];
        }
    }
}
