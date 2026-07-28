using System;
using System.Collections.Generic;

namespace ValoCase.Services.Iap
{
    /// <summary>
    /// Authored diamond-pack catalog — the single source of truth for USD diamond
    /// package ids, amounts, and display labels. Mirrors the CatalogModels.cs /
    /// CatalogLoader.cs pattern used for skins.json / cases.json.
    ///
    /// Physical file (loaded via Resources): Assets/_ValoCase/Resources/Config/diamond_packages.json
    /// </summary>
    [Serializable]
    public class IapPackageCatalogRoot
    {
        public int version = 1;
        public List<IapPackageEntry> packages = new();
    }

    [Serializable]
    public class IapPackageEntry
    {
        /// <summary>Permanent product id (e.g. "diamonds_100"). Will map 1:1 to the future Google Play product id.</summary>
        public string packageId;
        public int amount;
        /// <summary>Display tier badge, e.g. "STARTER" / "POPULAR" / "PRO" / "BEST VALUE".</summary>
        public string tier;
        /// <summary>Display-only price text. Not wired to real billing yet.</summary>
        public string priceDisplay;
        public bool enabled = true;
    }

    public enum IapPurchaseResult { Granted, Failed, Cancelled, Unavailable }
}
