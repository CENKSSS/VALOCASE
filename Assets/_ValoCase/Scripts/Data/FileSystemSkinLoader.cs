using System;
using System.Collections.Generic;
using UnityEngine;
// Paths managed centrally in ProjectPaths.cs

namespace ValoCase.Data
{
    // Loads weapon-skin sprites from Resources and produces SkinDefinitionSO
    // instances at runtime. MOBILE-SAFE: uses Resources.LoadAll<Sprite> instead of
    // System.IO, so it works in Android/iOS player builds (no Application.dataPath).
    //
    // Resources layout (under Assets/_ValoCase/Resources/, see ProjectPaths.SkinsRoot):
    //   Art/Skins/
    //     <WeaponName>/
    //       <RarityFolder>/
    //         <SkinName>.png   → loaded as a Sprite (see ResourcesArtTextureImporter)
    //
    // Resources has NO runtime directory enumeration, so the weapon list and the
    // five rarity folders are declared explicitly below. Adding a new weapon =
    // add its folder under Art/Skins AND add its name to Weapons[].
    //
    // Rarity folder names map to SkinRarity (case-insensitive, Turkish or English):
    //   Ozel / Özel / Select   → SkinRarity.Select    rank 0
    //   Ustun / Üstün / Deluxe → SkinRarity.Deluxe    rank 1
    //   Ihtisamli / Premium    → SkinRarity.Premium   rank 2
    //   Ultra                  → SkinRarity.Ultra     rank 3
    //   Seckin / Exclusive     → SkinRarity.Exclusive rank 4
    public static class FileSystemSkinLoader
    {
        // Default Resources root from central ProjectPaths — change it there, not here.
        public static readonly string DefaultRootPath = ProjectPaths.SkinsRoot;

        // Weapon folders present under Art/Skins. Resources cannot enumerate folders
        // at runtime, so this list is explicit.
        static readonly string[] Weapons =
        {
            "Ares", "Bandit", "Bucky", "Bulldog", "Classic", "Frenzy", "Ghost",
            "Guardian", "Judge", "Marshal", "Odin", "Operator", "Outlaw", "Phantom",
            "Sheriff", "Shorty", "Spectre", "Stinger", "Vandal",
        };

        // The five rarity sub-folders scanned inside each weapon folder.
        static readonly string[] RarityFolders =
        {
            "Ozel", "Ustun", "Ihtisamli", "Seckin", "Ultra",
        };

        // ── Scan metadata (read-only, for the editor catalog generator) ───────
        // Exposes the otherwise-private scan tables so the bootstrap tool can walk
        // the exact same folders the runtime loader does, without duplicating them.
        public static IReadOnlyList<string> WeaponFolders   => Weapons;
        public static IReadOnlyList<string> RarityFolderNames => RarityFolders;
        public static SkinRarity? ParseRarityFolderName(string folderName) => ParseRarityFolder(folderName);

        // ── Public API ───────────────────────────────────────────────────────

        // Builds runtime SkinDefinitionSO instances from an authored skin catalog.
        // Identity (skinId) comes from the catalog, NOT from file/folder names — the
        // resourceKey is used only to locate the sprite. All entries are loaded
        // (including enabled=false) so saved inventories referencing them still resolve;
        // filtering disabled skins out of pools/shop is a deliberate future concern.
        public static List<SkinDefinitionSO> BuildFromCatalog(SkinCatalogRoot root)
        {
            var result = new List<SkinDefinitionSO>();
            if (root?.skins == null) return result;

            foreach (var e in root.skins)
            {
                if (e == null || string.IsNullOrEmpty(e.skinId)) continue;

                if (!Enum.TryParse<SkinRarity>(e.rarity, true, out var rarity))
                {
                    Debug.LogWarning($"[FileSystemSkinLoader] Unknown rarity '{e.rarity}' for {e.skinId} " +
                                     $"— defaulting to Select.");
                    rarity = SkinRarity.Select;
                }

                var vp = e.vpValue > 0 ? e.vpValue : RaritySystem.GetVp(rarity);
                var displayName = string.IsNullOrEmpty(e.displayName) ? e.skinId : e.displayName;

                // Phase-6 mobile hardening: store the resourceKey and resolve the sprite
                // LAZILY on first Icon access — startup no longer loads ~1000 textures.
                var so = ScriptableObject.CreateInstance<SkinDefinitionSO>();
                so.name = e.skinId;
                so.InitializeRuntimeLazy(e.skinId, displayName, e.weapon, rarity, e.resourceKey, vp);
                result.Add(so);
            }

            Debug.Log($"[FileSystemSkinLoader] Catalog: {result.Count} skin kaydedildi (lazy sprite).");
            return result;
        }

        public static List<SkinDefinitionSO> LoadAll(string rootPath = null)
        {
            if (string.IsNullOrEmpty(rootPath))
                rootPath = DefaultRootPath;

            var result = new List<SkinDefinitionSO>();

            foreach (var weaponName in Weapons)
            {
                var beforeCount = result.Count;

                foreach (var rarityFolder in RarityFolders)
                {
                    var rarity = ParseRarityFolder(rarityFolder) ?? SkinRarity.Select;
                    LoadSpritesFromResourceFolder(
                        $"{rootPath}/{weaponName}/{rarityFolder}", weaponName, rarity, result);
                }

                // ── Per-weapon rarity summary ───────────────────────────────────
                var loaded = result.Count - beforeCount;
                if (loaded == 0) continue;

                int cnt(SkinRarity r)
                {
                    var c = 0;
                    for (var i = beforeCount; i < result.Count; i++)
                        if (result[i].Rarity == r) c++;
                    return c;
                }

                Debug.Log($"[WEAPON SUMMARY] {weaponName} => TOPLAM:{loaded} | " +
                          $"Özel:{cnt(SkinRarity.Select)} Üstün:{cnt(SkinRarity.Deluxe)} " +
                          $"İhtişamlı:{cnt(SkinRarity.Premium)} " +
                          $"Seçkin:{cnt(SkinRarity.Exclusive)} Ultra:{cnt(SkinRarity.Ultra)}");
            }

            Debug.Log($"[FileSystemSkinLoader] TOPLAM: {result.Count} skin yüklendi (Resources).");
            return result;
        }

        // ── Resource folder loader ────────────────────────────────────────────
        // Loads every Sprite in one Resources sub-folder and registers it.
        //
        // skinId includes rarity so identically-named files in different rarity
        // folders ("Ozel/Skin1" vs "Ustun/Skin1") stay DISTINCT — this matches the
        // original on-disk loader's IDs exactly, so saved inventories keep working.
        static void LoadSpritesFromResourceFolder(
            string resourceFolder, string weaponName, SkinRarity rarity,
            List<SkinDefinitionSO> output)
        {
            var sprites = Resources.LoadAll<Sprite>(resourceFolder);
            if (sprites == null || sprites.Length == 0) return;

            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;

                // sprite.name == the source filename without extension == rawName.
                var rawName = sprite.name;
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                var skinName = rawName.Replace('_', ' ').Trim();
                var skinId   = $"{weaponName}_{rarity}_{rawName}";

                var vp = GetVpForRarity(rarity);
                var so = ScriptableObject.CreateInstance<SkinDefinitionSO>();
                so.name = skinId;
                so.InitializeRuntime(skinId, skinName, weaponName, rarity, sprite, vp);
                output.Add(so);
            }
        }

        // ── Single-sprite loader (Resources path, no extension) ─────────────────
        // Kept for callers that need one sprite by its Resources path.
        public static Sprite LoadSpriteFromResources(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
                Debug.LogWarning($"[FileSystemSkinLoader] Sprite bulunamadı (Resources): {resourcePath}");
            return sprite;
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

            if (n.Contains("ozel")  || n == "select")    return SkinRarity.Select;
            if (n.Contains("ustun") || n == "deluxe")    return SkinRarity.Deluxe;
            if (n.Contains("ihtis") || n == "premium")   return SkinRarity.Premium;
            if (n.Contains("seckin")|| n == "exclusive") return SkinRarity.Exclusive;
            if (n.Contains("ultra"))                      return SkinRarity.Ultra;
            if (n.Contains("melee"))                      return SkinRarity.Melee;

            return null;
        }
    }
}
