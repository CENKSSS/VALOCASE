using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.EditorTools
{
    /// <summary>
    /// Phase-1 bootstrap: scans the current Resources/Art/Skins tree and emits an
    /// authored skin catalog (and a matching case catalog) for REVIEW.
    ///
    /// Output is written to *.generated.json on purpose — the runtime only loads
    /// skins.json / cases.json, so nothing changes in-game until a human reviews the
    /// generated files (especially the collision report) and promotes them by renaming
    /// skins.generated.json → skins.json and cases.generated.json → cases.json.
    ///
    /// Stable skin IDs are "skin_{snake(name)}_{snake(weapon)}" with rarity DELIBERATELY
    /// excluded. Legacy IDs ("{weapon}_{rarity}_{rawName}") are preserved exactly so the
    /// save migration can map old → new. Collisions are suffixed _2, _3 deterministically
    /// and logged loudly so they can be renamed to something meaningful before freezing.
    /// </summary>
    public static class StableIdCatalogGenerator
    {
        const string ConfigDirAbsolute = "Assets/_ValoCase/Resources/Config";
        const string SkinsGeneratedFile = "skins.generated.json";
        const string CasesGeneratedFile = "cases.generated.json";

        [MenuItem("ValoCase/Stable IDs/Generate Catalogs (Review)")]
        public static void GenerateCatalogs()
        {
            // 1. Scan the exact same folders the runtime loader walks.
            var records = ScanSkins();
            if (records.Count == 0)
            {
                Debug.LogWarning("[StableIdGen] No skins found under " + FileSystemSkinLoader.DefaultRootPath +
                                 ". Nothing generated.");
                return;
            }

            // 2. Deterministic order → deterministic collision suffixing.
            records.Sort((a, b) => string.CompareOrdinal(a.legacyId, b.legacyId));

            // 3. Assign stable IDs, detecting + suffixing collisions.
            var used = new HashSet<string>(StringComparer.Ordinal);
            var collisions = new List<string>();
            foreach (var r in records)
            {
                var baseId = $"skin_{Snake(r.displayName)}_{Snake(r.weapon)}";
                var id = baseId;
                var n = 2;
                while (used.Contains(id)) { id = $"{baseId}_{n}"; n++; }
                used.Add(id);
                r.stableId = id;
                if (id != baseId)
                    collisions.Add($"  {baseId}  ←  {r.legacyId}  →  assigned {id}");
            }

            // 4. Build the skin catalog + legacy→stable map.
            var skinRoot = new SkinCatalogRoot { version = 1, skins = new List<SkinCatalogEntry>() };
            var legacyToStable = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                skinRoot.skins.Add(new SkinCatalogEntry
                {
                    skinId         = r.stableId,
                    displayName    = r.displayName,
                    weapon         = r.weapon,
                    rarity         = r.rarity.ToString(),
                    vpValue        = RaritySystem.GetVp(r.rarity),
                    resourceKey    = r.resourceKey,
                    collectionName = "",
                    enabled        = true,
                    legacyId       = r.legacyId,
                });
                if (!legacyToStable.ContainsKey(r.legacyId))
                    legacyToStable[r.legacyId] = r.stableId;
            }

            // 5. Build the case catalog from VandalCaseBuilder, pools translated to stable IDs.
            var vandalSkins = records
                .Where(r => string.Equals(r.weapon, "Vandal", StringComparison.OrdinalIgnoreCase))
                .Select(r =>
                {
                    var so = ScriptableObject.CreateInstance<SkinDefinitionSO>();
                    so.InitializeRuntime(r.legacyId, r.displayName, r.weapon, r.rarity, null,
                                         RaritySystem.GetVp(r.rarity));
                    return so;
                })
                .ToList();
            var caseRoot = VandalCaseBuilder.ExportCaseCatalog(vandalSkins, legacyToStable);

            // 6. Write both files (review copies — never overwrites skins.json / cases.json).
            Directory.CreateDirectory(ConfigDirAbsolute);
            WriteJson(Path.Combine(ConfigDirAbsolute, SkinsGeneratedFile), skinRoot);
            WriteJson(Path.Combine(ConfigDirAbsolute, CasesGeneratedFile), caseRoot);
            AssetDatabase.Refresh();

            // 7. Report.
            Debug.Log($"[StableIdGen] Generated {skinRoot.skins.Count} skin entries " +
                      $"({collisions.Count} collisions) and {caseRoot.cases.Count} case entries.");
            if (collisions.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[StableIdGen] {collisions.Count} stable-ID COLLISION(S) — review before promoting:");
                foreach (var c in collisions) sb.AppendLine(c);
                Debug.LogWarning(sb.ToString());
            }
            Debug.Log("[StableIdGen] Review " + ConfigDirAbsolute + "/" + SkinsGeneratedFile + " and " +
                      CasesGeneratedFile + ". To activate: rename them to skins.json / cases.json.");
        }

        // ── Scanning ───────────────────────────────────────────────────────────

        class SkinRecord
        {
            public string weapon;
            public string rarityFolder;
            public SkinRarity rarity;
            public string rawName;
            public string displayName;
            public string legacyId;
            public string resourceKey;
            public string stableId;
        }

        static List<SkinRecord> ScanSkins()
        {
            var records = new List<SkinRecord>();
            var root = FileSystemSkinLoader.DefaultRootPath; // "Art/Skins"

            foreach (var weapon in FileSystemSkinLoader.WeaponFolders)
            {
                foreach (var folder in FileSystemSkinLoader.RarityFolderNames)
                {
                    var rarity = FileSystemSkinLoader.ParseRarityFolderName(folder) ?? SkinRarity.Select;
                    var resourceFolder = $"{root}/{weapon}/{folder}";
                    var sprites = Resources.LoadAll<Sprite>(resourceFolder);
                    if (sprites == null || sprites.Length == 0) continue;

                    foreach (var sprite in sprites)
                    {
                        if (sprite == null) continue;
                        var rawName = sprite.name;
                        if (string.IsNullOrWhiteSpace(rawName)) continue;

                        records.Add(new SkinRecord
                        {
                            weapon       = weapon,
                            rarityFolder = folder,
                            rarity       = rarity,
                            rawName      = rawName,
                            displayName  = rawName.Replace('_', ' ').Trim(),
                            // EXACTLY matches FileSystemSkinLoader's legacy ID format.
                            legacyId     = $"{weapon}_{rarity}_{rawName}",
                            resourceKey  = $"{resourceFolder}/{rawName}",
                        });
                    }
                }
            }

            return records;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        // Lowercase snake_case: runs of non-alphanumerics collapse to a single '_',
        // leading/trailing underscores trimmed. "Reaver 2.0" → "reaver_2_0".
        static string Snake(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            var sb = new StringBuilder(s.Length);
            var lastUnderscore = false;
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                    lastUnderscore = false;
                }
                else if (!lastUnderscore)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
            }
            var result = sb.ToString().Trim('_');
            return result.Length == 0 ? "unknown" : result;
        }

        static void WriteJson(string absolutePath, object root)
        {
            var json = JsonUtility.ToJson(root, true);
            File.WriteAllText(absolutePath, json);
            Debug.Log("[StableIdGen] Wrote " + absolutePath);
        }
    }
}
