using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValoCase.Data
{
    /// <summary>
    /// Builds the Vandal cases at runtime from filesystem-loaded skins.
    /// No SO assets needed — everything is created via ScriptableObject.CreateInstance.
    ///
    /// Cases (cheapest → most expensive):
    ///   Basic Vandal Case     500  VP  — Select 65 / Deluxe 15 / Premium 10 / Exclusive 7 / Ultra 3
    ///   Tactical Vandal Case  1000 VP  — Select 40 / Deluxe 30 / Premium 15 / Exclusive 10 / Ultra 5
    ///   Elite Vandal Case     2000 VP  — Select 20 / Deluxe 35 / Premium 25 / Exclusive 15 / Ultra 5
    ///   Protocol Vandal Case  3500 VP  — Select 10 / Deluxe 20 / Premium 35 / Exclusive 25 / Ultra 10
    ///   Radiant Vandal Case   5000 VP  — Select 5  / Deluxe 10 / Premium 25 / Exclusive 35 / Ultra 25
    ///   Arcane Vandal Case    7000 VP  — Select 2  / Deluxe 5  / Premium 18 / Exclusive 35 / Ultra 40
    ///
    /// ── Manual drop pools ─────────────────────────────────────────────────
    /// Each case has an EXPLICIT pool of skin IDs (see CaseConfig.ManualSkinIds and
    /// CaseDropTableSO.ManualSkinIds). The drop table consumed by the opening logic
    /// is RESOLVED from that ID list — it is no longer "every Vandal skin of the
    /// rarity". To curate a case by hand, just fill its ManualSkinIds array below.
    ///
    /// For the initial migration every ManualSkinIds entry is empty, so the pools are
    /// auto-generated ONCE, deterministically (fixed seed), by partitioning the loaded
    /// Vandal skins across the cases per rarity. The result is stable across launches
    /// and can be replaced with hand-picked IDs later without touching opening code.
    /// </summary>
    public static class VandalCaseBuilder
    {
        const string WeaponName = "Vandal";

        // Fixed seed → the auto-generated pools are identical on every launch.
        const int AutoPoolSeed = 0x5A1D;

        // ── Case definitions ─────────────────────────────────────────────────
        // Order must be cheapest → most expensive (shop displays them sorted by price).
        // Weights: (Select, Deluxe, Premium, Exclusive, Ultra)
        //
        // ManualSkinIds: leave null/empty to auto-generate a pool for that case.
        // Fill it with explicit skin IDs (weapon_rarity_rawName) to curate by hand —
        // e.g. new[] { "Vandal_Ultra_Reaver", "Vandal_Premium_Prime", ... }.
        static readonly CaseConfig[] CaseConfigs =
        {
            new CaseConfig("vandal_basic",    "Basic Vandal Case",    500,  65f, 15f, 10f,  7f,  3f),
            new CaseConfig("vandal_tactical", "Tactical Vandal Case", 1000, 40f, 30f, 15f, 10f,  5f),
            new CaseConfig("vandal_elite",    "Elite Vandal Case",    2000, 20f, 35f, 25f, 15f,  5f),
            new CaseConfig("vandal_protocol", "Protocol Vandal Case", 3500, 10f, 20f, 35f, 25f, 10f),
            new CaseConfig("vandal_radiant",  "Radiant Vandal Case",  5000,  5f, 10f, 25f, 35f, 25f),
            new CaseConfig("vandal_arcane",   "Arcane Vandal Case",   7000,  2f,  5f, 18f, 35f, 40f),
        };

        // Theme colors for each case (used as accent + fallback when no icon exists)
        static readonly Color[] CaseColors =
        {
            new Color(0.40f, 0.70f, 1.00f, 1f),   // Basic    — steel blue
            new Color(0.20f, 0.80f, 0.40f, 1f),   // Tactical — green
            new Color(1.00f, 0.55f, 0.10f, 1f),   // Elite    — orange
            new Color(0.70f, 0.20f, 1.00f, 1f),   // Protocol — purple
            new Color(1.00f, 0.80f, 0.10f, 1f),   // Radiant  — gold
            new Color(0.55f, 0.05f, 0.80f, 1f),   // Arcane   — deep violet-magenta
        };

        // Exact base names of icon files inside ProjectPaths.CaseIconsRoot.
        // FindImageFile normalises underscores/spaces so minor filename quirks
        // (e.g. "Radiant_Vandal_ Case" with an accidental space) are tolerated.
        static readonly string[] CaseIconNames =
        {
            "Basic_Vandal_Case",
            "Tactical_Vandal_Case",
            "Elite_Vandal_Case",
            "Protocol_Vandal_Case",
            "Radiant_Vandal_Case",
            "Arcane_Vandal_Case",
        };

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds all 5 Vandal cases from the skins already loaded into <paramref name="db"/>.
        /// Returns an empty list (with warnings) if no Vandal skins were found.
        /// </summary>
        public static List<CaseDefinitionSO> BuildAll(ContentDatabaseSO db)
        {
            var vandalSkins = db.GetFilteredSkins(WeaponName, null);

            // ── Per-rarity skin counts (required debug logs) ─────────────────
            var selectCount    = vandalSkins.Count(s => s.Rarity == SkinRarity.Select);
            var deluxeCount    = vandalSkins.Count(s => s.Rarity == SkinRarity.Deluxe);
            var premiumCount   = vandalSkins.Count(s => s.Rarity == SkinRarity.Premium);
            var exclusiveCount = vandalSkins.Count(s => s.Rarity == SkinRarity.Exclusive);
            var ultraCount     = vandalSkins.Count(s => s.Rarity == SkinRarity.Ultra);

            Debug.Log("[VANDAL_CASES] Loaded Select skins: "    + selectCount);
            Debug.Log("[VANDAL_CASES] Loaded Deluxe skins: "    + deluxeCount);
            Debug.Log("[VANDAL_CASES] Loaded Premium skins: "   + premiumCount);
            Debug.Log("[VANDAL_CASES] Loaded Exclusive skins: " + exclusiveCount);
            Debug.Log("[VANDAL_CASES] Loaded Ultra skins: "     + ultraCount);

            if (vandalSkins.Count == 0)
            {
                Debug.LogWarning("[VandalCaseBuilder] No Vandal skins found — no cases built.");
                return new List<CaseDefinitionSO>();
            }

            // Pre-group skins by rarity so we don't re-filter per case
            var byRarity = new Dictionary<SkinRarity, List<SkinDefinitionSO>>();
            foreach (var skin in vandalSkins)
            {
                if (!byRarity.ContainsKey(skin.Rarity))
                    byRarity[skin.Rarity] = new List<SkinDefinitionSO>();
                byRarity[skin.Rarity].Add(skin);
            }

            // Resolve skin IDs → SkinDefinitionSO locally. We CANNOT use db.GetSkin here:
            // BuildAll runs inside ContentDatabaseSO.ScanFileSystem, before _skinLookup is
            // built, so db.GetSkin would re-enter BuildLookups → ScanFileSystem (recursion).
            var byId = new Dictionary<string, SkinDefinitionSO>();
            foreach (var skin in vandalSkins)
                if (skin != null && !byId.ContainsKey(skin.SkinId))
                    byId[skin.SkinId] = skin;

            // Auto-generate the manual ID pools for cases that don't define their own.
            var autoPools = GenerateAutoDropPools(byRarity);

            var result = new List<CaseDefinitionSO>();
            for (var i = 0; i < CaseConfigs.Length; i++)
            {
                var cfg   = CaseConfigs[i];
                var color = CaseColors[i];
                var icon  = LoadCaseIcon(i);

                // Manual pool wins; otherwise use the auto-generated pool for this case.
                var manualIds = cfg.ManualSkinIds != null && cfg.ManualSkinIds.Length > 0
                    ? new List<string>(cfg.ManualSkinIds)
                    : (autoPools.TryGetValue(cfg.Id, out var gen) ? gen : new List<string>());

                var caseDef = BuildSingleCase(cfg, color, manualIds, byId, byRarity, icon);
                if (caseDef != null)
                {
                    result.Add(caseDef);
                    Debug.Log("[VANDAL_CASES] Created case: " + cfg.DisplayName +
                              " (pool=" + manualIds.Count + " skins)");
                }
            }

            return result;
        }

        // ── Deterministic auto-generation of manual drop pools ─────────────────
        // Partitions the loaded Vandal skins across the cases, per rarity, using a
        // fixed-seed shuffle so the result is identical on every launch. Each case
        // that weights a rarity ( > 0 ) gets a share of that rarity's skins; scarce
        // rarities may be shared so no weighted rarity is left with an empty pool.
        // Returns caseId → list of skin IDs.
        static Dictionary<string, List<string>> GenerateAutoDropPools(
            Dictionary<SkinRarity, List<SkinDefinitionSO>> byRarity)
        {
            var pools = new Dictionary<string, List<string>>();
            foreach (var cfg in CaseConfigs)
                pools[cfg.Id] = new List<string>();

            var rng = new System.Random(AutoPoolSeed);

            foreach (var rarity in RaritySystem.OrderedRarities)
            {
                if (!byRarity.TryGetValue(rarity, out var skins) || skins.Count == 0)
                    continue;

                // Sort by SkinId first → deterministic regardless of Resources load order.
                var ordered = new List<SkinDefinitionSO>(skins);
                ordered.Sort((a, b) => string.CompareOrdinal(a.SkinId, b.SkinId));
                Shuffle(ordered, rng);

                // Cases that actually want this rarity (weight > 0).
                var claimants = new List<CaseConfig>();
                foreach (var cfg in CaseConfigs)
                    if (cfg.WeightFor(rarity) > 0f) claimants.Add(cfg);
                if (claimants.Count == 0) continue;

                // Track which claimants received at least one skin of this rarity.
                var received = new HashSet<string>();

                // Round-robin deal: distinct subsets, evenly split.
                for (var i = 0; i < ordered.Count; i++)
                {
                    var cfg = claimants[i % claimants.Count];
                    pools[cfg.Id].Add(ordered[i].SkinId);
                    received.Add(cfg.Id);
                }

                // Scarce rarity (fewer skins than claimants): give the leftover claimants
                // one shared random skin so their weighted rarity isn't orphaned.
                foreach (var cfg in claimants)
                {
                    if (received.Contains(cfg.Id)) continue;
                    pools[cfg.Id].Add(ordered[rng.Next(ordered.Count)].SkinId);
                }
            }

            return pools;
        }

        // Fisher–Yates shuffle driven by a seeded System.Random (deterministic).
        static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ── Single-case builder ───────────────────────────────────────────────

        static CaseDefinitionSO BuildSingleCase(
            CaseConfig cfg, Color color, List<string> manualIds,
            Dictionary<string, SkinDefinitionSO> byId,
            Dictionary<SkinRarity, List<SkinDefinitionSO>> byRarity,
            Sprite icon)
        {
            // Resolve the explicit skin-ID pool into SkinDefinitionSO instances,
            // grouped by rarity. Unknown IDs are warned about and skipped.
            var poolByRarity = new Dictionary<SkinRarity, List<SkinDefinitionSO>>();
            var resolvedIds  = new List<string>();
            if (manualIds != null)
            {
                foreach (var id in manualIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!byId.TryGetValue(id, out var skin) || skin == null)
                    {
                        Debug.LogWarning("[VandalCaseBuilder] " + cfg.DisplayName +
                                         " — drop pool skin ID not found, skipped: " + id);
                        continue;
                    }
                    if (!poolByRarity.TryGetValue(skin.Rarity, out var list))
                        poolByRarity[skin.Rarity] = list = new List<SkinDefinitionSO>();
                    list.Add(skin);
                    resolvedIds.Add(id);
                }
            }

            // ── Fallback ──────────────────────────────────────────────────────
            // Empty/unresolvable pool → fall back to every Vandal skin of the case's
            // weighted rarities (the legacy behaviour) so the case is never broken.
            if (resolvedIds.Count == 0)
            {
                Debug.LogWarning("[VandalCaseBuilder] " + cfg.DisplayName +
                                 " — manual drop pool empty/unresolved; falling back to all " +
                                 "Vandal skins of its weighted rarities.");
                poolByRarity = byRarity;
                resolvedIds  = null; // signals "use full buckets, store nothing manual"
            }

            // Rarity tiers in display order; weights come from the config.
            var rarityDefs = new (SkinRarity rarity, float weight)[]
            {
                (SkinRarity.Select,    cfg.SelectWeight),
                (SkinRarity.Deluxe,    cfg.DeluxeWeight),
                (SkinRarity.Premium,   cfg.PremiumWeight),
                (SkinRarity.Exclusive, cfg.ExclusiveWeight),
                (SkinRarity.Ultra,     cfg.UltraWeight),
            };

            return AssembleCase(cfg.Id, cfg.DisplayName, cfg.Price, color, icon,
                                poolByRarity, rarityDefs, resolvedIds);
        }

        // ── Catalog-driven case building ───────────────────────────────────────
        // Builds cases from an authored CaseCatalogRoot. manualDropPool holds STABLE
        // skin IDs which are resolved via db.GetSkin (safe + non-recursive here: the
        // skin lookup is already built before BuildLookups calls into case building).
        // Behaviour matches the generated path: rarity-roll weights + equal split
        // inside each rarity bucket, with the same empty-pool fallback.
        public static List<CaseDefinitionSO> BuildAllFromCatalog(ContentDatabaseSO db, CaseCatalogRoot catalog)
        {
            var result = new List<CaseDefinitionSO>();
            if (db == null || catalog?.cases == null || catalog.cases.Count == 0)
            {
                Debug.LogWarning("[VandalCaseBuilder] Empty case catalog — no cases built.");
                return result;
            }

            foreach (var entry in catalog.cases)
            {
                if (entry == null || string.IsNullOrEmpty(entry.caseId)) continue;
                if (!entry.enabled)
                {
                    Debug.Log("[VANDAL_CASES] (catalog) Skipping disabled case: " + entry.caseId);
                    continue;
                }

                // Resolve the explicit stable-ID pool via the DB.
                var poolByRarity = new Dictionary<SkinRarity, List<SkinDefinitionSO>>();
                var resolvedIds  = new List<string>();
                if (entry.manualDropPool != null)
                {
                    foreach (var id in entry.manualDropPool)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        var skin = db.GetSkin(id);
                        if (skin == null)
                        {
                            Debug.LogWarning("[VandalCaseBuilder] " + entry.caseId +
                                             " — pool skin ID not found, skipped: " + id);
                            continue;
                        }
                        if (!poolByRarity.TryGetValue(skin.Rarity, out var list))
                            poolByRarity[skin.Rarity] = list = new List<SkinDefinitionSO>();
                        list.Add(skin);
                        resolvedIds.Add(id);
                    }
                }

                // Fallback: empty/unresolved pool → all Vandal skins of weighted rarities.
                if (resolvedIds.Count == 0)
                {
                    Debug.LogWarning("[VandalCaseBuilder] " + entry.caseId +
                                     " — catalog drop pool empty/unresolved; falling back to all " +
                                     "Vandal skins of its weighted rarities.");
                    foreach (var skin in db.GetFilteredSkins(WeaponName, null))
                    {
                        if (skin == null) continue;
                        if (!poolByRarity.TryGetValue(skin.Rarity, out var list))
                            poolByRarity[skin.Rarity] = list = new List<SkinDefinitionSO>();
                        list.Add(skin);
                    }
                    resolvedIds = null;
                }

                var rarityDefs = BuildRarityDefs(entry.rarityWeights);
                var color      = ParseHexColor(entry.themeColor);
                var icon       = LoadCatalogIcon(entry.resourceKey, entry.caseId);

                var caseDef = AssembleCase(entry.caseId, entry.displayName, entry.price, color, icon,
                                           poolByRarity, rarityDefs, resolvedIds);
                if (caseDef != null)
                {
                    result.Add(caseDef);
                    Debug.Log("[VANDAL_CASES] (catalog) Created case: " + entry.displayName +
                              " (pool=" + (resolvedIds?.Count.ToString() ?? "fallback") + " skins)");
                }
            }

            return result;
        }

        // ── Shared case assembly ───────────────────────────────────────────────
        // Builds the drop table + case definition from a resolved per-rarity pool.
        // Used by BOTH the generated path and the catalog path so opening behaviour
        // (rarity weights + equal split) stays identical regardless of source.
        static CaseDefinitionSO AssembleCase(
            string id, string displayName, int price, Color color, Sprite icon,
            Dictionary<SkinRarity, List<SkinDefinitionSO>> poolByRarity,
            (SkinRarity rarity, float weight)[] rarityDefs,
            List<string> manualIdsForRecord)
        {
            var rarityWeightEntries = new List<RarityWeightEntry>();
            var dropEntries         = new List<SkinDropEntry>();

            foreach (var (rarity, weight) in rarityDefs)
            {
                if (weight <= 0f) continue;

                if (!poolByRarity.TryGetValue(rarity, out var skins) || skins.Count == 0)
                {
                    Debug.LogWarning("[VandalCaseBuilder] " + displayName +
                                     " — no skins for rarity " + rarity +
                                     " (weight " + weight + "% orphaned).");
                    continue;
                }

                rarityWeightEntries.Add(new RarityWeightEntry
                    { rarity = rarity, weightPercent = weight });

                // skinWeightOverride = 0 → equal split inside the rarity bucket.
                // Example: Select 65 % / 10 skins → each skin effectively 6.5 %.
                foreach (var skin in skins)
                    dropEntries.Add(new SkinDropEntry { skin = skin, skinWeightOverride = 0f });
            }

            if (dropEntries.Count == 0)
            {
                Debug.LogWarning("[VandalCaseBuilder] Skipping case with no drops: " + displayName);
                return null;
            }

            var dropTable = ScriptableObject.CreateInstance<CaseDropTableSO>();
            dropTable.name = "DropTable_" + id;
            dropTable.InitializeRuntime(rarityWeightEntries, dropEntries, manualIdsForRecord);

            var caseDef = ScriptableObject.CreateInstance<CaseDefinitionSO>();
            caseDef.name = id;
            caseDef.InitializeRuntime(id, displayName, dropTable, price, color);
            caseDef.SetIconRuntime(icon);

            return caseDef;
        }

        // Builds the Select..Ultra weight tuples from a catalog weight list (display
        // order preserved; missing rarities default to 0 → skipped by AssembleCase).
        static (SkinRarity rarity, float weight)[] BuildRarityDefs(List<CaseRarityWeight> weights)
        {
            float W(SkinRarity r)
            {
                if (weights == null) return 0f;
                foreach (var w in weights)
                    if (w != null && System.Enum.TryParse<SkinRarity>(w.rarity, true, out var parsed) && parsed == r)
                        return w.weight;
                return 0f;
            }

            return new (SkinRarity, float)[]
            {
                (SkinRarity.Select,    W(SkinRarity.Select)),
                (SkinRarity.Deluxe,    W(SkinRarity.Deluxe)),
                (SkinRarity.Premium,   W(SkinRarity.Premium)),
                (SkinRarity.Exclusive, W(SkinRarity.Exclusive)),
                (SkinRarity.Ultra,     W(SkinRarity.Ultra)),
            };
        }

        static Color ParseHexColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c))
                return c;
            return new Color(0.92f, 0.23f, 0.29f, 1f);
        }

        // Loads a case icon for the catalog path: exact resourceKey first, then the
        // existing fuzzy basename fallback (tolerates stray spaces in filenames).
        static Sprite LoadCatalogIcon(string resourceKey, string caseId)
        {
            if (!string.IsNullOrEmpty(resourceKey))
            {
                var exact = Resources.Load<Sprite>(resourceKey);
                if (exact != null) return exact;

                var slash = resourceKey.LastIndexOf('/');
                var baseName = slash >= 0 ? resourceKey.Substring(slash + 1) : resourceKey;
                var fuzzy = FindCaseIconSprite(baseName);
                if (fuzzy != null) return fuzzy;
            }
            Debug.LogWarning("[VANDAL_CASE_ICON] (catalog) loaded=False case=" + caseId +
                             " | resourceKey=" + resourceKey);
            return null;
        }

        // ── Icon loading (Resources, mobile-safe) ──────────────────────────────

        // Loaded once and reused across all case lookups this session.
        static Sprite[] _caseIconCache;

        static Sprite LoadCaseIcon(int caseIndex)
        {
            var caseName = caseIndex >= 0 && caseIndex < CaseConfigs.Length
                ? CaseConfigs[caseIndex].DisplayName
                : "index=" + caseIndex;

            if (caseIndex < 0 || caseIndex >= CaseIconNames.Length)
            {
                Debug.LogWarning("[VANDAL_CASE_ICON] loaded=False case=" + caseName +
                                 " | no icon name defined for this index");
                return null;
            }

            var iconName = CaseIconNames[caseIndex];
            var sprite   = FindCaseIconSprite(iconName);

            Debug.Log("[VANDAL_CASE_ICON] loaded=" + (sprite != null) +
                      " case=" + caseName + " | resource=" + ProjectPaths.CaseIconsRoot +
                      "/" + iconName);
            return sprite;
        }

        // Matches a case-icon sprite by name. Resource filenames may differ slightly
        // from CaseIconNames (e.g. "Radiant_Vandal_ Case" has a stray space), so we
        // compare on the normalised form after an exact-path attempt.
        static Sprite FindCaseIconSprite(string iconName)
        {
            // Pass 1: exact Resources path (covers the well-named files).
            var exact = Resources.Load<Sprite>(ProjectPaths.CaseIconsRoot + "/" + iconName);
            if (exact != null) return exact;

            // Pass 2: fuzzy match against everything in the Cases folder.
            _caseIconCache ??= Resources.LoadAll<Sprite>(ProjectPaths.CaseIconsRoot);
            if (_caseIconCache == null || _caseIconCache.Length == 0) return null;

            var normBase = NormalizeName(iconName);
            foreach (var s in _caseIconCache)
            {
                if (s != null && NormalizeName(s.name) == normBase)
                    return s;
            }
            return null;
        }

        // Strips underscores, spaces and lowercases for fuzzy name comparison.
        // "Radiant_Vandal_ Case" → "radiantvandalcase"
        // "Radiant_Vandal_Case"  → "radiantvandalcase"  ← same → match
        static string NormalizeName(string s) =>
            s.Replace("_", "").Replace(" ", "").ToLowerInvariant();

        // ── Catalog export (editor bootstrap only) ─────────────────────────────
        // Produces a CaseCatalogRoot from the hardcoded CaseConfigs, with each case's
        // auto-generated pool translated from legacy IDs to STABLE IDs via the map.
        // Keeps caseId/displayName/price/weights/icon intact. Editor tool writes this
        // to cases.generated.json for review.
        public static CaseCatalogRoot ExportCaseCatalog(
            List<SkinDefinitionSO> loadedVandalSkins,
            IReadOnlyDictionary<string, string> legacyToStable)
        {
            var byRarity = new Dictionary<SkinRarity, List<SkinDefinitionSO>>();
            if (loadedVandalSkins != null)
                foreach (var skin in loadedVandalSkins)
                {
                    if (skin == null) continue;
                    if (!byRarity.TryGetValue(skin.Rarity, out var list))
                        byRarity[skin.Rarity] = list = new List<SkinDefinitionSO>();
                    list.Add(skin);
                }

            var autoPools = GenerateAutoDropPools(byRarity); // caseId → legacy IDs

            string ToStable(string legacy) =>
                legacyToStable != null && legacyToStable.TryGetValue(legacy, out var s) ? s : legacy;

            var root = new CaseCatalogRoot { version = 1, cases = new List<CaseCatalogEntry>() };

            for (var i = 0; i < CaseConfigs.Length; i++)
            {
                var cfg = CaseConfigs[i];

                var pool = autoPools.TryGetValue(cfg.Id, out var legacyPool)
                    ? legacyPool
                    : new List<string>();

                var entry = new CaseCatalogEntry
                {
                    caseId      = cfg.Id,
                    displayName = cfg.DisplayName,
                    price       = cfg.Price,
                    resourceKey = ProjectPaths.CaseIconsRoot + "/" + CaseIconNames[i],
                    enabled     = true,
                    themeColor  = "#" + ColorUtility.ToHtmlStringRGB(CaseColors[i]),
                    rarityWeights = new List<CaseRarityWeight>
                    {
                        new() { rarity = SkinRarity.Select.ToString(),    weight = cfg.SelectWeight },
                        new() { rarity = SkinRarity.Deluxe.ToString(),    weight = cfg.DeluxeWeight },
                        new() { rarity = SkinRarity.Premium.ToString(),   weight = cfg.PremiumWeight },
                        new() { rarity = SkinRarity.Exclusive.ToString(), weight = cfg.ExclusiveWeight },
                        new() { rarity = SkinRarity.Ultra.ToString(),     weight = cfg.UltraWeight },
                    },
                    manualDropPool = pool.Select(ToStable).ToArray(),
                };
                root.cases.Add(entry);
            }

            return root;
        }

        // ── Case config (value type, stack allocated) ─────────────────────────

        struct CaseConfig
        {
            public readonly string   Id;
            public readonly string   DisplayName;
            public readonly int      Price;
            public readonly float    SelectWeight;
            public readonly float    DeluxeWeight;
            public readonly float    PremiumWeight;
            public readonly float    ExclusiveWeight;
            public readonly float    UltraWeight;

            // Explicit, hand-editable drop pool (skin IDs: weapon_rarity_rawName).
            // null/empty → the pool is auto-generated for this case at build time.
            public readonly string[] ManualSkinIds;

            public CaseConfig(string id, string displayName, int price,
                float sel, float del, float pre, float exc, float ult,
                string[] manualSkinIds = null)
            {
                Id              = id;
                DisplayName     = displayName;
                Price           = price;
                SelectWeight    = sel;
                DeluxeWeight    = del;
                PremiumWeight   = pre;
                ExclusiveWeight = exc;
                UltraWeight     = ult;
                ManualSkinIds   = manualSkinIds;
            }

            // Configured weight for a rarity (used by the auto-pool generator).
            public float WeightFor(SkinRarity rarity) => rarity switch
            {
                SkinRarity.Select    => SelectWeight,
                SkinRarity.Deluxe    => DeluxeWeight,
                SkinRarity.Premium   => PremiumWeight,
                SkinRarity.Exclusive => ExclusiveWeight,
                SkinRarity.Ultra     => UltraWeight,
                _                    => 0f,
            };
        }
    }
}
