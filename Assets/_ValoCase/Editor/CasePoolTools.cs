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
    /// Phase-2 case-pool authoring tools — EDITOR-ONLY, with ZERO runtime impact.
    ///
    /// These tools operate purely on the authored catalogs (skins.json / cases.json).
    /// They do NOT change runtime loading, opening odds, saves, inventory, VP, upgrade,
    /// battle or UI. They never mint or rename stable IDs — IDs are treated as FROZEN
    /// (deferred to a future dedicated "ID Cleanup Migration Phase").
    ///
    ///   • Validate Case Pools        — loud, itemised validation + summary report.
    ///   • Rebuild Case Pools (Keep IDs) — regenerate auto-pools using the EXISTING
    ///                                     frozen IDs, written to cases.generated.json
    ///                                     for review (never auto-promotes).
    ///
    /// Pool policy: a skin MAY appear in multiple cases. Cross-case overlap is
    /// intentional and is reported as information, never as an error. Only duplicates
    /// WITHIN a single pool are flagged.
    /// </summary>
    public static class CasePoolTools
    {
        const string ConfigDir          = "Assets/_ValoCase/Resources/Config";
        const string CasesGeneratedFile = "cases.generated.json";

        // ── Validation ─────────────────────────────────────────────────────────

        [MenuItem("ValoCase/Stable IDs/Validate Case Pools")]
        public static void ValidateCasePools()
        {
            var skinRoot = CatalogLoader.LoadSkinCatalog();
            var caseRoot = CatalogLoader.LoadCaseCatalog();

            if (skinRoot?.skins == null || skinRoot.skins.Count == 0)
            {
                Debug.LogError("[PoolValidate] skins.json missing or empty — cannot validate. " +
                               "Promote a generated catalog first.");
                return;
            }
            if (caseRoot?.cases == null || caseRoot.cases.Count == 0)
            {
                Debug.LogError("[PoolValidate] cases.json missing or empty — cannot validate. " +
                               "Promote a generated catalog first.");
                return;
            }

            // Skin lookup by stable id; also catch duplicate skinIds in the catalog itself.
            var skinById   = new Dictionary<string, SkinCatalogEntry>(StringComparer.Ordinal);
            var dupSkinIds = new List<string>();
            foreach (var e in skinRoot.skins)
            {
                if (e == null || string.IsNullOrEmpty(e.skinId)) continue;
                if (!skinById.ContainsKey(e.skinId)) skinById[e.skinId] = e;
                else dupSkinIds.Add(e.skinId);
            }

            // Accumulators (caseId :: id formatting for easy console scanning).
            var totalRefs       = 0;
            var invalidRefs     = new List<string>();
            var blankRefs       = new List<string>();
            var withinPoolDupes = new List<string>();
            var disabledRefs    = new List<string>();
            var missingSprite   = new List<string>();
            var emptyPools      = new List<string>();
            var rarityGaps      = new List<string>();
            var usageCount      = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var c in caseRoot.cases)
            {
                if (c == null || string.IsNullOrEmpty(c.caseId)) continue;

                var pool = c.manualDropPool;
                if (pool == null || pool.Length == 0)
                {
                    emptyPools.Add(c.caseId);
                    continue;
                }

                var seen             = new HashSet<string>(StringComparer.Ordinal);
                var resolvedByRarity = new Dictionary<SkinRarity, int>();

                foreach (var id in pool)
                {
                    totalRefs++;

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        blankRefs.Add(c.caseId);
                        continue;
                    }

                    if (!seen.Add(id))
                        withinPoolDupes.Add($"{c.caseId} :: {id}");

                    usageCount.TryGetValue(id, out var n);
                    usageCount[id] = n + 1;

                    if (!skinById.TryGetValue(id, out var skin))
                    {
                        invalidRefs.Add($"{c.caseId} :: {id}");
                        continue;
                    }

                    if (!skin.enabled)
                        disabledRefs.Add($"{c.caseId} :: {id}");

                    if (!string.IsNullOrEmpty(skin.resourceKey) &&
                        Resources.Load<Sprite>(skin.resourceKey) == null)
                        missingSprite.Add($"{c.caseId} :: {id}  (resourceKey={skin.resourceKey})");

                    if (Enum.TryParse<SkinRarity>(skin.rarity, true, out var r))
                    {
                        resolvedByRarity.TryGetValue(r, out var rc);
                        resolvedByRarity[r] = rc + 1;
                    }
                }

                // Rarity coverage: any rarity with weight > 0 should have >=1 skin in the pool,
                // otherwise that weight is orphaned at runtime (matches BuildAllFromCatalog).
                if (c.rarityWeights != null)
                {
                    foreach (var w in c.rarityWeights)
                    {
                        if (w == null || w.weight <= 0f) continue;
                        if (!Enum.TryParse<SkinRarity>(w.rarity, true, out var r)) continue;
                        if (!resolvedByRarity.TryGetValue(r, out var rc) || rc == 0)
                            rarityGaps.Add($"{c.caseId} :: weight {w.weight}% for {w.rarity} but 0 skins of that rarity in pool");
                    }
                }
            }

            var overlaps = usageCount.Where(kv => kv.Value > 1).ToList();

            // ── Loud, itemised reporting ────────────────────────────────────────
            void Err(string title, List<string> items)
            {
                if (items.Count == 0) return;
                Debug.LogError($"[PoolValidate] {title} ({items.Count}):\n  " + string.Join("\n  ", items));
            }
            void Warn(string title, List<string> items)
            {
                if (items.Count == 0) return;
                Debug.LogWarning($"[PoolValidate] {title} ({items.Count}):\n  " + string.Join("\n  ", items));
            }

            Err ("INVALID skin IDs (not found in skins.json)", invalidRefs);
            Err ("BLANK / empty pool entries",                 blankRefs);
            Err ("EMPTY pools (no skins at all)",              emptyPools);
            Err ("DUPLICATE skinIds inside skins.json",        dupSkinIds);
            Warn("WITHIN-POOL duplicate IDs",                  withinPoolDupes);
            Warn("DISABLED skins referenced by pools",         disabledRefs);
            Warn("MISSING sprite (resourceKey won't load)",    missingSprite);
            Warn("RARITY coverage gaps (orphaned weight)",     rarityGaps);

            // Overlap across cases is INTENTIONAL → informational, never an error.
            if (overlaps.Count > 0)
                Debug.Log($"[PoolValidate] Cross-case overlap is allowed by policy — " +
                          $"{overlaps.Count} skin(s) appear in more than one case (informational).");

            var errors = invalidRefs.Count + blankRefs.Count + emptyPools.Count + dupSkinIds.Count;
            var warnings = withinPoolDupes.Count + disabledRefs.Count + missingSprite.Count + rarityGaps.Count;

            var summary = new StringBuilder();
            summary.AppendLine("──────── CASE POOL VALIDATION SUMMARY ────────");
            summary.AppendLine($"Pools (cases)             : {caseRoot.cases.Count}");
            summary.AppendLine($"Total pool references     : {totalRefs}");
            summary.AppendLine($"Unique skins used         : {usageCount.Count}");
            summary.AppendLine($"Catalog skins available   : {skinById.Count}");
            summary.AppendLine($"Invalid IDs               : {invalidRefs.Count}");
            summary.AppendLine($"Blank entries             : {blankRefs.Count}");
            summary.AppendLine($"Empty pools               : {emptyPools.Count}");
            summary.AppendLine($"Within-pool duplicates    : {withinPoolDupes.Count}");
            summary.AppendLine($"Disabled references       : {disabledRefs.Count}");
            summary.AppendLine($"Missing sprites           : {missingSprite.Count}");
            summary.AppendLine($"Rarity coverage gaps      : {rarityGaps.Count}");
            summary.AppendLine($"Cross-case overlaps (ok)  : {overlaps.Count}");
            summary.AppendLine($"Catalog duplicate skinIds : {dupSkinIds.Count}");
            summary.AppendLine($"RESULT                    : {(errors == 0 ? "PASS" : "FAIL")}  " +
                               $"({errors} error(s), {warnings} warning(s))");
            summary.AppendLine("──────────────────────────────────────────────");

            if (errors == 0) Debug.Log(summary.ToString());
            else             Debug.LogError(summary.ToString());

            EditorUtility.DisplayDialog(
                "Case Pool Validation",
                (errors == 0 ? "PASS" : "FAIL") +
                $"\n\nErrors:   {errors}\nWarnings: {warnings}\n\nSee the Console for the full itemised report.",
                "OK");
        }

        // ── Pool rebuild (keeps frozen IDs) ─────────────────────────────────────

        [MenuItem("ValoCase/Stable IDs/Rebuild Case Pools (Keep IDs)")]
        public static void RebuildCasePools()
        {
            var skinRoot = CatalogLoader.LoadSkinCatalog();
            if (skinRoot?.skins == null || skinRoot.skins.Count == 0)
            {
                Debug.LogError("[PoolRebuild] skins.json missing or empty — aborting. " +
                               "This tool reuses frozen IDs and will NOT mint new ones.");
                return;
            }

            // Map legacy → stable from the EXISTING (frozen) skin catalog. No scanning,
            // no new IDs: every ID in the rebuilt pools comes straight from skins.json.
            var legacyToStable = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in skinRoot.skins)
            {
                if (e == null || string.IsNullOrEmpty(e.legacyId) || string.IsNullOrEmpty(e.skinId)) continue;
                if (!legacyToStable.ContainsKey(e.legacyId))
                    legacyToStable[e.legacyId] = e.skinId;
            }

            // Recreate the Vandal skin list keyed on LEGACY ids — exactly what the
            // auto-pool generator expects — then ExportCaseCatalog translates the
            // generated pools to the frozen STABLE ids via the map above.
            var vandalSkins = new List<SkinDefinitionSO>();
            foreach (var e in skinRoot.skins)
            {
                if (e == null) continue;
                if (!string.Equals(e.weapon, "Vandal", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(e.legacyId)) continue;

                if (!Enum.TryParse<SkinRarity>(e.rarity, true, out var rarity))
                    rarity = SkinRarity.Select;

                var so = ScriptableObject.CreateInstance<SkinDefinitionSO>();
                so.InitializeRuntime(e.legacyId, e.displayName, e.weapon, rarity, null,
                                     e.vpValue > 0 ? e.vpValue : RaritySystem.GetVp(rarity));
                vandalSkins.Add(so);
            }

            if (vandalSkins.Count == 0)
            {
                Debug.LogError("[PoolRebuild] No Vandal skins found in skins.json — aborting.");
                return;
            }

            var caseRoot = VandalCaseBuilder.ExportCaseCatalog(vandalSkins, legacyToStable);

            Directory.CreateDirectory(ConfigDir);
            var path = Path.Combine(ConfigDir, CasesGeneratedFile);
            File.WriteAllText(path, JsonUtility.ToJson(caseRoot, true));
            AssetDatabase.Refresh();

            Debug.Log($"[PoolRebuild] Rebuilt {caseRoot.cases.Count} case pool(s) from frozen IDs → {path}\n" +
                      "Stable IDs were NOT changed. Review the file, then activate by renaming " +
                      "cases.generated.json → cases.json. Run 'Validate Case Pools' afterwards.");

            EditorUtility.DisplayDialog(
                "Rebuild Case Pools",
                $"Rebuilt {caseRoot.cases.Count} pool(s) using the existing frozen IDs.\n\n" +
                "Written to cases.generated.json for review (cases.json was NOT touched).\n\n" +
                "Promote by renaming it to cases.json, then run Validate Case Pools.",
                "OK");
        }
    }
}
