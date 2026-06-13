using System;
using System.Collections.Generic;

namespace ValoCase.Data
{
    /// <summary>
    /// Authored catalog data — the future source of truth for skin/case identity.
    ///
    /// These are plain, JsonUtility-compatible DTOs (public fields, [Serializable]).
    /// The same shapes serialize to local JSON now and can be served by a backend
    /// (Spring Boot + PostgreSQL) later WITHOUT changing the client schema.
    ///
    /// Physical files (loaded via Resources, see CatalogLoader):
    ///   Assets/_ValoCase/Resources/Config/skins.json   → SkinCatalogRoot
    ///   Assets/_ValoCase/Resources/Config/cases.json   → CaseCatalogRoot
    ///
    /// The editor tool writes *.generated.json first for human review; only after a
    /// human promotes them to skins.json / cases.json does the runtime use them.
    /// Until then the runtime falls back to the legacy filesystem scan, so the game
    /// keeps working with zero behavior change.
    /// </summary>

    // ── Skins ────────────────────────────────────────────────────────────────

    [Serializable]
    public class SkinCatalogRoot
    {
        public int version = 1;
        public List<SkinCatalogEntry> skins = new();
    }

    [Serializable]
    public class SkinCatalogEntry
    {
        /// <summary>Permanent, authored identity. NEVER derived from files. e.g. "skin_reaver_vandal".</summary>
        public string skinId;
        public string displayName;
        public string weapon;
        /// <summary>SkinRarity enum name: Select / Deluxe / Premium / Exclusive / Ultra.</summary>
        public string rarity;
        public int vpValue;
        /// <summary>Unity Resources path WITHOUT extension (e.g. "Art/Skins/Vandal/Ultra/Reaver").</summary>
        public string resourceKey;
        public string collectionName;
        public bool enabled = true;
        /// <summary>Migration-only: the old generated ID this entry replaces ("Vandal_Ultra_Reaver").</summary>
        public string legacyId;
    }

    // ── Cases ────────────────────────────────────────────────────────────────

    [Serializable]
    public class CaseCatalogRoot
    {
        public int version = 1;
        public List<CaseCatalogEntry> cases = new();
    }

    [Serializable]
    public class CaseCatalogEntry
    {
        public string caseId;
        public string displayName;
        public int price;
        /// <summary>Unity Resources path WITHOUT extension for the case icon.</summary>
        public string resourceKey;
        public bool enabled = true;
        /// <summary>Hex accent color, e.g. "#8C0DCC".</summary>
        public string themeColor;
        /// <summary>Rarity roll weights (kept as a list for JsonUtility / SQL friendliness).</summary>
        public List<CaseRarityWeight> rarityWeights = new();
        /// <summary>Explicit, hand-editable drop pool of STABLE skin IDs.</summary>
        public string[] manualDropPool = Array.Empty<string>();
    }

    [Serializable]
    public class CaseRarityWeight
    {
        /// <summary>SkinRarity enum name.</summary>
        public string rarity;
        public float weight;
    }
}
