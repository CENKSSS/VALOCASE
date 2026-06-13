using System;
using UnityEngine;

namespace ValoCase.Data
{
    /// <summary>
    /// Reads the authored catalogs from Resources/Config at runtime.
    ///
    /// MOBILE-SAFE: uses Resources.Load&lt;TextAsset&gt; (Unity imports *.json as
    /// TextAsset), so it works identically in the Editor and in player builds.
    ///
    /// Returns null when a catalog file is absent — callers MUST treat null as
    /// "fall back to the legacy filesystem path" so the project never breaks while
    /// the catalogs are still under review (only *.generated.json exists).
    /// </summary>
    public static class CatalogLoader
    {
        // Resources keys (no extension). These resolve to skins.json / cases.json.
        // The *.generated.json review files are deliberately NOT loaded here.
        public const string SkinCatalogResourceKey = "Config/skins";
        public const string CaseCatalogResourceKey = "Config/cases";

        public static SkinCatalogRoot LoadSkinCatalog() =>
            LoadJson<SkinCatalogRoot>(SkinCatalogResourceKey);

        public static CaseCatalogRoot LoadCaseCatalog() =>
            LoadJson<CaseCatalogRoot>(CaseCatalogResourceKey);

        static T LoadJson<T>(string resourceKey) where T : class
        {
            var asset = Resources.Load<TextAsset>(resourceKey);
            if (asset == null) return null;

            try
            {
                var parsed = JsonUtility.FromJson<T>(asset.text);
                if (parsed == null)
                    Debug.LogWarning($"[CatalogLoader] {resourceKey} parsed to null — ignoring.");
                return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CatalogLoader] Failed to parse {resourceKey}: {ex.Message}");
                return null;
            }
        }
    }
}
