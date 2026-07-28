using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Services.Iap
{
    /// <summary>
    /// Reads the authored diamond-pack catalog from Resources/Config/diamond_packages.json.
    /// Falls back to a fixed built-in set if the file is missing/unparsable, so the Market
    /// screen never breaks. MOBILE-SAFE: uses Resources.Load&lt;TextAsset&gt;.
    /// </summary>
    public static class IapProductCatalog
    {
        public const string ResourceKey = "Config/diamond_packages";

        static List<IapPackageEntry> _cache;

        public static IReadOnlyList<IapPackageEntry> Packages
        {
            get
            {
                if (_cache != null) return _cache;
                _cache = LoadFromResources() ?? Fallback();
                return _cache;
            }
        }

        static List<IapPackageEntry> LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>(ResourceKey);
            if (asset == null) return null;

            try
            {
                var root = JsonUtility.FromJson<IapPackageCatalogRoot>(asset.text);
                if (root?.packages == null || root.packages.Count == 0) return null;
                var enabled = root.packages.FindAll(p => p.enabled);
                return enabled.Count > 0 ? enabled : null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IapProductCatalog] Failed to parse {ResourceKey}: {ex.Message}");
                return null;
            }
        }

        static List<IapPackageEntry> Fallback() => new()
        {
            new IapPackageEntry { packageId = "diamonds_100",  amount = 100,  tier = "STARTER",    priceDisplay = "$0.99",  enabled = true },
            new IapPackageEntry { packageId = "diamonds_550",  amount = 550,  tier = "POPULAR",    priceDisplay = "$4.99",  enabled = true },
            new IapPackageEntry { packageId = "diamonds_1200", amount = 1200, tier = "PRO",        priceDisplay = "$9.99",  enabled = true },
            new IapPackageEntry { packageId = "diamonds_2500", amount = 2500, tier = "BEST VALUE", priceDisplay = "$19.99", enabled = true },
        };
    }
}
