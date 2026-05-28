using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValoCase.Data
{
    [CreateAssetMenu(fileName = "ContentDatabase", menuName = "ValoCase/Content Database", order = 10)]
    public class ContentDatabaseSO : ScriptableObject
    {
        // Skin list is now populated at RUNTIME by FileSystemSkinLoader.
        // This field is kept for Editor-assigned fallback skins (e.g. hand-crafted SOs).
        [SerializeField] List<SkinDefinitionSO> skins = new List<SkinDefinitionSO>();
        [SerializeField] List<CaseDefinitionSO> cases = new List<CaseDefinitionSO>();

        // Optional: override the desktop path. Leave empty to use the default
        // (Desktop/ValorantProject/ValoSkinss). Set in Inspector if the folder is elsewhere.
        [Header("FileSystem Loader")]
        [SerializeField] string skinRootPathOverride = "";

        // When true (default), skins are always replaced by filesystem scan at runtime.
        // Set to false to use only the manually assigned SO list above.
        [SerializeField] bool loadSkinsFromFileSystem = true;

        Dictionary<string, SkinDefinitionSO> _skinLookup;
        Dictionary<string, CaseDefinitionSO> _caseLookup;

        public IReadOnlyList<SkinDefinitionSO> Skins => skins;
        public IReadOnlyList<CaseDefinitionSO> Cases => cases;

        // ── Startup ──────────────────────────────────────────────────────────

        // Called by GameContext.Awake. Loads skins from filesystem then builds lookups.
        public void BuildLookups()
        {
            if (loadSkinsFromFileSystem)
                ScanFileSystem();

            _skinLookup = skins
                .Where(s => s != null)
                .GroupBy(s => s.SkinId)
                .ToDictionary(g => g.Key, g => g.First());

            _caseLookup = cases
                .Where(c => c != null)
                .GroupBy(c => c.CaseId)
                .ToDictionary(g => g.Key, g => g.First());

            Debug.Log($"[ContentDatabase] Hazır — {skins.Count} skin, {cases.Count} kasa.");
        }

        void ScanFileSystem()
        {
            var path = string.IsNullOrWhiteSpace(skinRootPathOverride)
                ? FileSystemSkinLoader.DefaultRootPath
                : skinRootPathOverride;

            var loaded = FileSystemSkinLoader.LoadAll(path);
            if (loaded.Count > 0)
            {
                skins = loaded;
            }
            else
            {
                Debug.LogWarning($"[ContentDatabase] FileSystem'den hiç skin yüklenemedi ({path}). " +
                                 $"Inspector'daki mevcut {skins.Count} SO skin kullanılıyor.");
            }

            // Replace case list with the 5 runtime-built Vandal Cases.
            // All SO-based case assets are intentionally discarded here.
            var vandalCases = VandalCaseBuilder.BuildAll(this);
            cases = vandalCases.Count > 0
                ? vandalCases
                : new List<CaseDefinitionSO>();
        }

        // ── Queries ───────────────────────────────────────────────────────────

        public SkinDefinitionSO GetSkin(string skinId)
        {
            if (_skinLookup == null) BuildLookups();
            return _skinLookup != null && _skinLookup.TryGetValue(skinId, out var skin) ? skin : null;
        }

        public CaseDefinitionSO GetCase(string caseId)
        {
            if (_caseLookup == null) BuildLookups();
            return _caseLookup != null && _caseLookup.TryGetValue(caseId, out var c) ? c : null;
        }

        // Runtime: group by weapon (used by WeaponsScreen)
        public List<SkinDefinitionSO> GetSkinsByWeaponRuntime(string weaponName)
        {
            return skins.FindAll(s => s != null &&
                string.Equals(s.WeaponName, weaponName, StringComparison.OrdinalIgnoreCase));
        }

        // Returns all unique weapon names sorted alphabetically
        public List<string> GetUniqueWeaponNames()
        {
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var s in skins)
            {
                if (s == null || string.IsNullOrEmpty(s.WeaponName)) continue;
                if (seen.Add(s.WeaponName)) result.Add(s.WeaponName);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // Returns all unique rarities present in the DB, sorted by RaritySystem rank (lowest first).
        public List<SkinRarity> GetUniqueRarities()
        {
            var seen   = new HashSet<SkinRarity>();
            var result = new List<SkinRarity>();
            foreach (var s in skins)
            {
                if (s == null) continue;
                if (seen.Add(s.Rarity)) result.Add(s.Rarity);
            }
            result.Sort((a, b) => RaritySystem.GetRank(a).CompareTo(RaritySystem.GetRank(b)));
            return result;
        }

        // AND-logic filter: null weapon or no rarity means "all" for that dimension
        public List<SkinDefinitionSO> GetFilteredSkins(string weaponFilter, SkinRarity? rarityFilter)
        {
            return skins.FindAll(s =>
            {
                if (s == null) return false;
                if (!string.IsNullOrEmpty(weaponFilter) &&
                    !string.Equals(s.WeaponName, weaponFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (rarityFilter.HasValue && s.Rarity != rarityFilter.Value)
                    return false;
                return true;
            });
        }

        // ── Editor-only helpers ───────────────────────────────────────────────

#if UNITY_EDITOR
        public void EditorSetContent(List<SkinDefinitionSO> skinList, List<CaseDefinitionSO> caseList)
        {
            skins = skinList;
            cases = caseList;
            BuildLookups();
        }

        public void EditorMergeSkins(List<SkinDefinitionSO> newSkins)
        {
            foreach (var skin in newSkins)
            {
                if (skin == null) continue;
                if (skins.Exists(s => s != null && s.SkinId == skin.SkinId)) continue;
                skins.Add(skin);
            }
            BuildLookups();
        }

        public List<SkinDefinitionSO> GetSkinsByWeapon(string weaponName)
        {
            return skins.FindAll(s => s != null &&
                string.Equals(s.WeaponName, weaponName, StringComparison.OrdinalIgnoreCase));
        }
#endif
    }
}
