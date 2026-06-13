using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Data
{
    [Serializable]
    public class RarityWeightEntry
    {
        public SkinRarity rarity;
        [Range(0f, 100f)] public float weightPercent = 10f;
    }

    [Serializable]
    public class SkinDropEntry
    {
        public SkinDefinitionSO skin;
        [Tooltip("Optional override weight inside its rarity bucket. 0 = equal split.")]
        public float skinWeightOverride;
    }

    [CreateAssetMenu(fileName = "DropTable_", menuName = "ValoCase/Case Drop Table", order = 3)]
    public class CaseDropTableSO : ScriptableObject
    {
        [SerializeField] List<RarityWeightEntry> rarityWeights = new();
        [SerializeField] List<SkinDropEntry> possibleDrops = new();

        // ── Manual drop pool (the editable source of truth) ───────────────────
        // Explicit list of skin IDs (weapon_rarity_rawName) that belong to this case.
        // This is what designers edit later. At runtime these IDs are resolved into
        // possibleDrops above, which the opening logic actually consumes — so editing
        // this list changes case contents WITHOUT touching any opening/odds code.
        [SerializeField] List<string> manualSkinIds = new();

        public IReadOnlyList<RarityWeightEntry> RarityWeights => rarityWeights;
        public IReadOnlyList<SkinDropEntry> PossibleDrops => possibleDrops;
        public IReadOnlyList<string> ManualSkinIds => manualSkinIds;

        // Populate at runtime (not from Inspector).
        // manualIds is the explicit skin-ID pool; possibleDrops is its resolved form.
        public void InitializeRuntime(List<RarityWeightEntry> weights, List<SkinDropEntry> drops,
                                      List<string> manualIds = null)
        {
            rarityWeights = weights ?? new List<RarityWeightEntry>();
            possibleDrops = drops  ?? new List<SkinDropEntry>();
            manualSkinIds = manualIds ?? new List<string>();
        }
    }
}
