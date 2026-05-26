using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Data
{
    [Serializable]
    public class RarityVisualEntry
    {
        public SkinRarity rarity;
        public Color primaryColor = Color.white;
        public Color glowColor    = Color.white;
        public Color textColor    = Color.white;
        public float glowIntensity = 1f;

        // Full-card background color for Valorant-style rarity cards.
        // Deep, rich tone derived from primaryColor. Set by the content generator.
        public Color cardBgColor = new Color(0.10f, 0.12f, 0.15f, 1f);
    }

    [CreateAssetMenu(fileName = "RarityVisuals", menuName = "ValoCase/Rarity Visual Database", order = 11)]
    public class RarityVisualSO : ScriptableObject
    {
        [SerializeField] List<RarityVisualEntry> entries = new();

        public bool TryGet(SkinRarity rarity, out RarityVisualEntry entry)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].rarity == rarity)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
