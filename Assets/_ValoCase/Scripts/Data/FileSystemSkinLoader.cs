using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
// Paths managed centrally in ProjectPaths.cs

namespace ValoCase.Data
{
    // Scans the skin folder and produces SkinDefinitionSO instances at runtime.
    // Folder layout expected (see ProjectPaths.SkinsRoot):
    //   <root>/
    //     <WeaponName>/
    //       <RarityFolder>/
    //         <SkinName>.png
    //
    // Rarity folder names (case-insensitive, Turkish or English accepted):
    //   Ozel / Özel / Select   → SkinRarity.Select    rank 0
    //   Ustun / Üstün / Deluxe → SkinRarity.Deluxe    rank 1
    //   Ihtisamli / Premium    → SkinRarity.Premium   rank 2
    //   Ultra                  → SkinRarity.Ultra     rank 3
    //   Seckin / Exclusive     → SkinRarity.Exclusive rank 4
    public static class FileSystemSkinLoader
    {
        // Default path from central ProjectPaths — change the root there, not here.
        public static readonly string DefaultRootPath = ProjectPaths.SkinsRoot;

        // ── Public API ───────────────────────────────────────────────────────

        public static List<SkinDefinitionSO> LoadAll(string rootPath = null)
        {
            if (string.IsNullOrEmpty(rootPath))
                rootPath = DefaultRootPath;

            var result = new List<SkinDefinitionSO>();

            if (!Directory.Exists(rootPath))
            {
                Debug.LogWarning($"[FileSystemSkinLoader] Klasör bulunamadı: {rootPath}");
                return result;
            }

            var weaponDirs = Directory.GetDirectories(rootPath);
            Debug.Log($"[FileSystemSkinLoader] {weaponDirs.Length} silah klasörü taranıyor: {rootPath}");

            foreach (var weaponDir in weaponDirs)
            {
                var weaponName   = Path.GetFileName(weaponDir);
                var beforeCount  = result.Count;

                // ── Rarity sub-folders ──────────────────────────────────────────
                var rarityDirs = Directory.GetDirectories(weaponDir);
                Debug.Log($"[FileSystemSkinLoader] {weaponName}: {rarityDirs.Length} rarity klasörü bulundu");

                foreach (var rarityDir in rarityDirs)
                {
                    var rarityFolderName = Path.GetFileName(rarityDir);
                    var rarity           = ParseRarityFolder(rarityFolderName);

                    if (rarity == null)
                    {
                        Debug.LogWarning($"[FileSystemSkinLoader]   ? '{rarityFolderName}' tanınamadı — " +
                                         $"Özel Seri (Select) olarak yüklenecek");
                        rarity = SkinRarity.Select;
                    }
                    else
                    {
                        Debug.Log($"[FileSystemSkinLoader]   '{rarityFolderName}' → {rarity}");
                    }

                    LoadImagesFromDir(rarityDir, weaponName, rarity.Value, result);
                }

                // ── Images placed directly in the weapon folder (no rarity sub-folder) ─
                LoadImagesFromDir(weaponDir, weaponName, SkinRarity.Select, result,
                                  onlyTopLevelFiles: true);

                // ── Per-weapon rarity summary ───────────────────────────────────
                var weaponSkins = result.FindAll(s => s != null &&
                    string.Equals(s.WeaponName, weaponName, StringComparison.OrdinalIgnoreCase));

                var cntOzel    = weaponSkins.FindAll(s => s.Rarity == SkinRarity.Select).Count;
                var cntUstun   = weaponSkins.FindAll(s => s.Rarity == SkinRarity.Deluxe).Count;
                var cntIhtis   = weaponSkins.FindAll(s => s.Rarity == SkinRarity.Premium).Count;
                var cntSeckin  = weaponSkins.FindAll(s => s.Rarity == SkinRarity.Exclusive).Count;
                var cntUltra   = weaponSkins.FindAll(s => s.Rarity == SkinRarity.Ultra).Count;
                var totalLoaded = result.Count - beforeCount;

                Debug.Log($"[WEAPON SUMMARY] {weaponName} => TOPLAM:{totalLoaded} | " +
                          $"Özel:{cntOzel} Üstün:{cntUstun} İhtişamlı:{cntIhtis} " +
                          $"Seçkin:{cntSeckin} Ultra:{cntUltra}");
            }

            Debug.Log($"[FileSystemSkinLoader] TOPLAM: {result.Count} skin yüklendi.");
            return result;
        }

        // ── Image directory loader ────────────────────────────────────────────
        // Scans ONE directory (top-level only) for image files and registers them.
        //
        // BUG FIX: skinId now includes rarity so that identically-named files
        // in different rarity folders ("Ozel/Skin1.png" vs "Ustun/Skin1.png")
        // produce DIFFERENT SkinDefinitionSO entries instead of being de-duped.
        //
        // onlyTopLevelFiles = true → skip files that are inside sub-folders
        //   (used when scanning the weapon root dir to avoid re-picking rarity skins).
        static void LoadImagesFromDir(string dir, string weaponName, SkinRarity rarity,
                                      List<SkinDefinitionSO> output,
                                      bool onlyTopLevelFiles = false)
        {
            if (!Directory.Exists(dir)) return;

            var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg" };

            // When scanning the weapon root we want ONLY files that live directly
            // there (not inside rarity sub-folders, which GetFiles already excludes
            // with TopDirectoryOnly — but sub-folder *entries* also appear in
            // GetDirectories, not GetFiles, so TopDirectoryOnly is always correct).
            foreach (var filePath in Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(filePath);
                if (!imageExts.Contains(ext)) continue;

                var skinName = Path.GetFileNameWithoutExtension(filePath);
                if (string.IsNullOrWhiteSpace(skinName)) continue;

                // ── KEY FIX: rarity is part of the ID ────────────────────────
                // "Vandal_Select_Skin1" and "Vandal_Deluxe_Skin1" are DIFFERENT
                // skins — no false de-duplication even when filenames match.
                var skinId = $"{weaponName}_{rarity}_{skinName}";

                var sprite = LoadSprite(filePath, skinId);
                if (sprite == null) continue;

                var vp = GetVpForRarity(rarity);
                var so = ScriptableObject.CreateInstance<SkinDefinitionSO>();
                so.name = skinId;
                so.InitializeRuntime(skinId, skinName, weaponName, rarity, sprite, vp);
                output.Add(so);

                Debug.Log($"[SKIN LOADED] {weaponName} | {rarity} | {skinName}  (id={skinId})");
            }
        }

        // ── Rarity → VP value ────────────────────────────────────────────────

        // Delegates to the central RaritySystem — VP values are NOT duplicated here.
        public static int GetVpForRarity(SkinRarity rarity) => RaritySystem.GetVp(rarity);

        // ── Rarity folder → enum ─────────────────────────────────────────────

        static SkinRarity? ParseRarityFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return null;
            var n = folderName.ToLowerInvariant()
                              .Replace("ı", "i")
                              .Replace("ş", "s")
                              .Replace("ğ", "g")
                              .Replace("ü", "u")
                              .Replace("ö", "o")
                              .Replace("ç", "c");

            // Select  (Özel / Ozel / ozel seri / ozel_seri)
            if (n.Contains("ozel") || n == "select") return SkinRarity.Select;

            // Deluxe  (Üstün / Ustun / üstün seri)
            if (n.Contains("ustun") || n == "deluxe") return SkinRarity.Deluxe;

            // Premium (İhtişamlı / Ihtisamli / premium)
            if (n.Contains("ihtis") || n == "premium") return SkinRarity.Premium;

            // Exclusive (Seçkin / Seckin / exclusive)
            if (n.Contains("seckin") || n == "exclusive") return SkinRarity.Exclusive;

            // Ultra
            if (n.Contains("ultra")) return SkinRarity.Ultra;

            return null;
        }

        // ── PNG → Sprite ─────────────────────────────────────────────────────

        // Loads a single image file → Sprite. Tries the given path first,
        // then common extensions (.png, .jpg, .jpeg) when none is provided.
        // Returns null if the file can't be found or decoded.
        public static Sprite LoadSpriteFromFile(string fullPath, string spriteName = null)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;

            // Resolve missing extension by probing common image formats.
            if (!File.Exists(fullPath))
            {
                string[] candidates = { fullPath + ".png", fullPath + ".jpg", fullPath + ".jpeg",
                                        fullPath + ".PNG", fullPath + ".JPG" };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { fullPath = c; break; }
                }
            }

            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[FileSystemSkinLoader] Dosya bulunamadı: {fullPath}");
                return null;
            }

            return LoadSprite(fullPath, spriteName ?? Path.GetFileNameWithoutExtension(fullPath));
        }

        static Sprite LoadSprite(string fullPath, string spriteName)
        {
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                var tex   = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                tex.name  = spriteName;

                if (!tex.LoadImage(bytes))
                {
                    Debug.LogWarning($"[FileSystemSkinLoader] PNG yüklenemedi: {fullPath}");
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }

                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode   = TextureWrapMode.Clamp;

                var sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 100f);

                sprite.name = spriteName;
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileSystemSkinLoader] Hata ({fullPath}): {ex.Message}");
                return null;
            }
        }
    }
}
