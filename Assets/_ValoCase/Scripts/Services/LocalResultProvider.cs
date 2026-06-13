using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.Services
{
    /// <summary>
    /// Local (offline) implementation of <see cref="IResultProvider"/>.
    ///
    /// Owns the canonical roll logic that previously lived inline in CaseOpeningService.
    /// The odds, RNG source (UnityEngine.Random via WeightedRandomizer) and selection
    /// rules are IDENTICAL to the pre-Phase-3 behavior — this is a relocation, not a
    /// rebalance. A future remote provider would implement the same interface and return
    /// the same ID-based result contracts from the server instead.
    /// </summary>
    public sealed class LocalResultProvider : IResultProvider
    {
        // ── Skin roll (verbatim from the previous CaseOpeningService.RollSkin) ──
        public SkinDefinitionSO RollSkin(CaseDefinitionSO caseDef)
        {
            var table = caseDef?.DropTable;
            if (table == null || table.PossibleDrops.Count == 0) return null;

            var rarity = RollRarity(table);
            var pool = table.PossibleDrops
                .Where(d => d.skin != null && d.skin.Rarity == rarity)
                .ToList();

            if (pool.Count == 0)
                pool = table.PossibleDrops.Where(d => d.skin != null).ToList();

            if (pool.Count == 0) return null;

            var skins   = pool.Select(p => p.skin).ToList();
            var weights = pool.Select(p => p.skinWeightOverride > 0 ? p.skinWeightOverride : 1f).ToList();
            return WeightedRandomizer.Pick(skins, weights);
        }

        static SkinRarity RollRarity(CaseDropTableSO table)
        {
            var entries = table.RarityWeights;
            if (entries == null || entries.Count == 0) return SkinRarity.Select;

            var rarities = entries.Select(e => e.rarity).ToList();
            var weights  = entries.Select(e => e.weightPercent).ToList();
            return WeightedRandomizer.Pick(rarities, weights);
        }

        // ── High-level, ID-based result contracts ───────────────────────────────
        public CaseOpeningResult GenerateCaseOpening(CaseDefinitionSO caseDef, int vpSpent)
        {
            var skin = RollSkin(caseDef);
            return new CaseOpeningResult(
                caseDef != null ? caseDef.CaseId : null,
                skin    != null ? skin.SkinId    : null,
                skin    != null ? skin.Rarity.ToString() : null,
                vpSpent);
        }

        public UpgradeResult GenerateUpgrade(IReadOnlyList<SkinDefinitionSO> inputs, SkinDefinitionSO target, float chance)
        {
            var ids = new List<string>();
            if (inputs != null)
                foreach (var s in inputs)
                    if (s != null) ids.Add(s.SkinId);

            // Same RNG decision as the previous inline `UnityEngine.Random.value < chance`.
            var success = Random.value < chance;

            return new UpgradeResult(ids.ToArray(), target != null ? target.SkinId : null, chance, success);
        }
    }
}
