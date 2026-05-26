using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
// Paths managed centrally in ProjectPaths.cs

namespace ValoCase.Data
{
    // Builds the single "Vandal Kasası" at runtime from filesystem-loaded skins.
    // No SO assets needed — everything is created with ScriptableObject.CreateInstance.
    public static class VandalCaseBuilder
    {
        const string CaseId      = "vandal_case";
        const string CaseName    = "Vandal Kasası";
        const int    CasePrice   = 500;
        const string WeaponName  = "Vandal";

        // Image used in shop card and case selector.
        // Path sourced from ProjectPaths — do NOT hardcode here.
        static readonly string CaseIconDir = ProjectPaths.CaseIconsRoot;
        const string CaseIconBaseName = "VCase";

        // Weighted probability per rarity tier.
        // Total must be <= 100. Higher rarity = lower weight.
        static readonly Dictionary<SkinRarity, float> RarityWeights =
            new Dictionary<SkinRarity, float>
            {
                { SkinRarity.Select,    50f },   // Özel Seri    — most common
                { SkinRarity.Deluxe,    30f },   // Üstün Seri
                { SkinRarity.Premium,   15f },   // İhtişamlı Seri
                { SkinRarity.Exclusive,  4f },   // Seçkin Seri
                { SkinRarity.Ultra,      1f },   // Ultra Seri   — rarest
            };

        // ── Icon file search ─────────────────────────────────────────────────

        // Looks for any file named "VCase" (any extension) inside CaseIconDir.
        // Falls back to the project root and skins root if not found in Case/.
        static string FindCaseIconFile()
        {
            var searchDirs = new[]
            {
                CaseIconDir,                    // ValorantProject/Case/        ← canonical
                ProjectPaths.SkinsRoot,         // ValorantProject/ValoSkinss/
                ProjectPaths.ProjectRoot,       // ValorantProject/
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                // Wildcard: find any file whose name starts with "VCase" (case-insensitive)
                var files = Directory.GetFiles(dir, CaseIconBaseName + ".*",
                    SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                        ext == ".webp" || ext == ".bmp")
                        return f;
                }
                // Also try case-insensitive wildcard match (Windows is case-insensitive anyway,
                // but do an explicit check in case of mixed-case filenames on non-Windows)
                var allFiles = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var f in allFiles)
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(f);
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (!string.Equals(nameNoExt, CaseIconBaseName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
                        ext == ".webp" || ext == ".bmp")
                        return f;
                }
            }
            return null;
        }

        // ── VCase icon loader ────────────────────────────────────────────────

        static Sprite LoadVCaseIcon()
        {
            var path = FindCaseIconFile();
            if (path == null) return null;

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[VandalCaseBuilder] İkon okunamadı ({path}): {ex.Message}");
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            tex.name = "VandalCaseIcon";
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[VandalCaseBuilder] İkon yüklenemedi (format desteklenmiyor?): {path}");
                UnityEngine.Object.Destroy(tex);
                return null;
            }

            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            try
            {
                var sprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "VandalCaseIcon";
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VandalCaseBuilder] Sprite oluşturulamadı: {ex.Message}");
                UnityEngine.Object.Destroy(tex);
                return null;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public static CaseDefinitionSO Build(ContentDatabaseSO db)
        {
            var vandalSkins = db.GetFilteredSkins(WeaponName, null);

            if (vandalSkins.Count == 0)
            {
                Debug.LogWarning($"[VandalCaseBuilder] '{WeaponName}' için hiç skin bulunamadı. " +
                                 "Kasa oluşturulamadı.");
                return null;
            }

            var rarityWeightEntries = new List<RarityWeightEntry>();
            var dropEntries         = new List<SkinDropEntry>();

            // Group skins by rarity, build weight entries only for tiers that exist
            var groups = vandalSkins
                .GroupBy(s => s.Rarity)
                .OrderBy(g => (int)g.Key)
                .ToList();

            foreach (var group in groups)
            {
                if (!RarityWeights.TryGetValue(group.Key, out var w)) continue;

                rarityWeightEntries.Add(new RarityWeightEntry
                    { rarity = group.Key, weightPercent = w });

                foreach (var skin in group)
                    dropEntries.Add(new SkinDropEntry { skin = skin, skinWeightOverride = 0f });
            }

            // Build runtime DropTable
            var dropTable = ScriptableObject.CreateInstance<CaseDropTableSO>();
            dropTable.name = "DropTable_VandalCase";
            dropTable.InitializeRuntime(rarityWeightEntries, dropEntries);

            // Build runtime CaseDefinition
            var caseDef = ScriptableObject.CreateInstance<CaseDefinitionSO>();
            caseDef.name = CaseId;
            caseDef.InitializeRuntime(CaseId, CaseName, dropTable, CasePrice);

            caseDef.SetIconRuntime(LoadVCaseIcon());


            Debug.Log($"[VandalCaseBuilder] Kasa hazır — '{CaseName}' | " +
                      $"{vandalSkins.Count} skin | {rarityWeightEntries.Count} rarity katmanı | {CasePrice} VP");

            return caseDef;
        }
    }
}
