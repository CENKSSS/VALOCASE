using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ValoCase.Data
{
    /// <summary>
    /// Builds 5 Vandal cases at runtime from filesystem-loaded skins.
    /// No SO assets needed — everything is created via ScriptableObject.CreateInstance.
    ///
    /// Cases (cheapest → most expensive):
    ///   Basic Vandal Case     500  VP  — Select 65 / Deluxe 15 / Premium 10 / Exclusive 7 / Ultra 3
    ///   Tactical Vandal Case  1000 VP  — Select 40 / Deluxe 30 / Premium 15 / Exclusive 10 / Ultra 5
    ///   Elite Vandal Case     2000 VP  — Select 20 / Deluxe 35 / Premium 25 / Exclusive 15 / Ultra 5
    ///   Protocol Vandal Case  3500 VP  — Select 10 / Deluxe 20 / Premium 35 / Exclusive 25 / Ultra 10
    ///   Radiant Vandal Case   5000 VP  — Select 5  / Deluxe 10 / Premium 25 / Exclusive 35 / Ultra 25
    /// </summary>
    public static class VandalCaseBuilder
    {
        const string WeaponName = "Vandal";

        // ── Case definitions ─────────────────────────────────────────────────
        // Order must be cheapest → most expensive (shop displays them sorted by price).
        // Weights: (Select, Deluxe, Premium, Exclusive, Ultra)
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

            var result = new List<CaseDefinitionSO>();
            for (var i = 0; i < CaseConfigs.Length; i++)
            {
                var cfg     = CaseConfigs[i];
                var color   = CaseColors[i];
                var icon    = LoadCaseIcon(i);
                var caseDef = BuildSingleCase(cfg, color, byRarity, icon);
                if (caseDef != null)
                {
                    result.Add(caseDef);
                    Debug.Log("[VANDAL_CASES] Created case: " + cfg.DisplayName);
                }
            }

            return result;
        }

        // ── Single-case builder ───────────────────────────────────────────────

        static CaseDefinitionSO BuildSingleCase(
            CaseConfig cfg, Color color,
            Dictionary<SkinRarity, List<SkinDefinitionSO>> byRarity,
            Sprite icon)
        {
            // Rarity tiers in display order; weights come from the config.
            var rarityDefs = new (SkinRarity rarity, float weight)[]
            {
                (SkinRarity.Select,    cfg.SelectWeight),
                (SkinRarity.Deluxe,    cfg.DeluxeWeight),
                (SkinRarity.Premium,   cfg.PremiumWeight),
                (SkinRarity.Exclusive, cfg.ExclusiveWeight),
                (SkinRarity.Ultra,     cfg.UltraWeight),
            };

            var rarityWeightEntries = new List<RarityWeightEntry>();
            var dropEntries         = new List<SkinDropEntry>();

            foreach (var (rarity, weight) in rarityDefs)
            {
                if (weight <= 0f) continue;

                if (!byRarity.TryGetValue(rarity, out var skins) || skins.Count == 0)
                {
                    Debug.LogWarning("[VandalCaseBuilder] " + cfg.DisplayName +
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
                Debug.LogWarning("[VandalCaseBuilder] Skipping case with no drops: " + cfg.DisplayName);
                return null;
            }

            var dropTable = ScriptableObject.CreateInstance<CaseDropTableSO>();
            dropTable.name = "DropTable_" + cfg.Id;
            dropTable.InitializeRuntime(rarityWeightEntries, dropEntries);

            var caseDef = ScriptableObject.CreateInstance<CaseDefinitionSO>();
            caseDef.name = cfg.Id;
            caseDef.InitializeRuntime(cfg.Id, cfg.DisplayName, dropTable, cfg.Price, color);
            caseDef.SetIconRuntime(icon);

            return caseDef;
        }

        // ── Icon loading ──────────────────────────────────────────────────────

        static Sprite LoadCaseIcon(int caseIndex)
        {
            var iconDir  = ProjectPaths.CaseIconsRoot;
            var caseName = caseIndex >= 0 && caseIndex < CaseConfigs.Length
                ? CaseConfigs[caseIndex].DisplayName
                : "index=" + caseIndex;

            // ── Directory diagnostics ─────────────────────────────────────────
            Debug.Log("[VANDAL_ICON_DEBUG] dir=" + iconDir);
            Debug.Log("[VANDAL_ICON_DEBUG] dir exists=" + Directory.Exists(iconDir));
            if (Directory.Exists(iconDir))
                Debug.Log("[VANDAL_ICON_DEBUG] files=" +
                          string.Join(", ", Directory.GetFiles(iconDir).Select(Path.GetFileName)));
            // ─────────────────────────────────────────────────────────────────

            if (caseIndex < 0 || caseIndex >= CaseIconNames.Length)
            {
                Debug.LogWarning("[VANDAL_CASE_ICON] loaded=False case=" + caseName +
                                 " | no icon name defined for this index");
                return null;
            }

            var iconName = CaseIconNames[caseIndex];
            var tryPath  = Path.Combine(iconDir, iconName + ".png");

            // Log the primary path being attempted so it's easy to verify folder contents.
            Debug.Log("[VANDAL_CASE_ICON] trying=" + tryPath);
            Debug.Log("[VANDAL_ICON_DEBUG] case=" + caseName + " trying=" + tryPath);

            var path = FindImageFile(iconDir, iconName);

            if (path == null)
            {
                Debug.LogWarning("[VANDAL_CASE_ICON] loaded=False case=" + caseName +
                                 " | dir=" + iconDir + " | tried: " + iconName);
                Debug.LogWarning("[VANDAL_ICON_DEBUG] icon missing for case=" + caseName);
                return null;
            }

            var sprite = LoadSpriteFromFile(path, iconName);
            Debug.Log("[VANDAL_CASE_ICON] loaded=" + (sprite != null) +
                      " case=" + caseName + " | path=" + path);
            Debug.Log("[VANDAL_ICON_DEBUG] loaded icon=" + path);
            return sprite;
        }

        static string FindImageFile(string dir, string baseName)
        {
            if (!Directory.Exists(dir)) return null;

            var imageExts = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp",
                                    ".PNG", ".JPG", ".JPEG" };

            // Pass 1: Fast exact path probes (no directory scan needed)
            foreach (var ext in imageExts)
            {
                var p = Path.Combine(dir, baseName + ext);
                if (File.Exists(p)) return p;
            }

            try
            {
                var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);

                // Pass 2: Case-insensitive exact name match
                foreach (var f in files)
                {
                    if (!string.Equals(
                            Path.GetFileNameWithoutExtension(f), baseName,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    var ext2 = Path.GetExtension(f).ToLowerInvariant();
                    if (ext2 == ".png" || ext2 == ".jpg" || ext2 == ".jpeg" ||
                        ext2 == ".webp" || ext2 == ".bmp")
                        return f;
                }

                // Pass 3: Normalise underscores/spaces/case
                // Catches "Radiant_Vandal_ Case" when we search for "Radiant_Vandal_Case"
                var normBase = NormalizeName(baseName);
                foreach (var f in files)
                {
                    if (!string.Equals(
                            NormalizeName(Path.GetFileNameWithoutExtension(f)), normBase,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    var ext3 = Path.GetExtension(f).ToLowerInvariant();
                    if (ext3 == ".png" || ext3 == ".jpg" || ext3 == ".jpeg" ||
                        ext3 == ".webp" || ext3 == ".bmp")
                        return f;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VandalCaseBuilder] Icon directory scan error: " + ex.Message);
            }

            return null;
        }

        // Strips underscores, spaces and lowercases for fuzzy filename comparison.
        // "Radiant_Vandal_ Case" → "radiantvandalcase"
        // "Radiant_Vandal_Case"  → "radiantvandalcase"  ← same → match
        static string NormalizeName(string s) =>
            s.Replace("_", "").Replace(" ", "").ToLowerInvariant();

        static Sprite LoadSpriteFromFile(string path, string spriteName)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Debug.LogError("[VandalCaseBuilder] Cannot read icon (" + path + "): " + ex.Message);
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            tex.name = spriteName;
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning("[VandalCaseBuilder] Icon load failed (unsupported format?): " + path);
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            try
            {
                var sprite = Sprite.Create(tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit: 100f);
                sprite.name = spriteName;
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogError("[VandalCaseBuilder] Sprite create failed: " + ex.Message);
                UnityEngine.Object.Destroy(tex);
                return null;
            }
        }

        // ── Case config (value type, stack allocated) ─────────────────────────

        struct CaseConfig
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly int    Price;
            public readonly float  SelectWeight;
            public readonly float  DeluxeWeight;
            public readonly float  PremiumWeight;
            public readonly float  ExclusiveWeight;
            public readonly float  UltraWeight;

            public CaseConfig(string id, string displayName, int price,
                float sel, float del, float pre, float exc, float ult)
            {
                Id              = id;
                DisplayName     = displayName;
                Price           = price;
                SelectWeight    = sel;
                DeluxeWeight    = del;
                PremiumWeight   = pre;
                ExclusiveWeight = exc;
                UltraWeight     = ult;
            }
        }
    }
}
