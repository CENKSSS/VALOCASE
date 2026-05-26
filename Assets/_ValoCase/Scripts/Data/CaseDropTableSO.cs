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

        public IReadOnlyList<RarityWeightEntry> RarityWeights => rarityWeights;
        public IReadOnlyList<SkinDropEntry> PossibleDrops => possibleDrops;

        // Populate at runtime (not from Inspector).
        public void InitializeRuntime(List<RarityWeightEntry> weights, List<SkinDropEntry> drops)
        {
            rarityWeights = weights ?? new List<RarityWeightEntry>();
            possibleDrops = drops  ?? new List<SkinDropEntry>();
        }
    }
}
