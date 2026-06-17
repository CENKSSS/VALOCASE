using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValoCase.Save;

namespace ValoCase.Data
{
    [CreateAssetMenu(fileName = "ContentDatabase", menuName = "ValoCase/Content Database", order = 10)]
    public class ContentDatabaseSO : ScriptableObject
    {
        // Skin list is now populated at RUNTIME by FileSystemSkinLoader.
        // This field is kept for Editor-assigned fallback skins (e.g. hand-crafted SOs).
        [SerializeField] List<SkinDefinitionSO> skins = new List<SkinDefinitionSO>();
        [SerializeField] List<CaseDefinitionSO> cases = new List<CaseDefinitionSO>();

        // Optional: override the skins folder path. Leave empty to use the default
        // (Assets/_ValoCase/Art/Skins). Set in Inspector if the folder is elsewhere.
        [Header("FileSystem Loader")]
        [SerializeField] string skinRootPathOverride = "";

        // When true (default), skins are always replaced by filesystem scan at runtime.
        // Set to false to use only the manually assigned SO list above.
        [SerializeField] bool loadSkinsFromFileSystem = true;

        Dictionary<string, SkinDefinitionSO> _skinLookup;
        Dictionary<string, CaseDefinitionSO> _caseLookup;

        // Maps a legacy generated skin ID ("Vandal_Ultra_Reaver") to its stable
        // catalog ID ("skin_reaver_vandal"). Empty unless a skin catalog is loaded.
        // Drives both lenient GetSkin and the one-time save migration.
        Dictionary<string, string> _legacyToStable = new(StringComparer.Ordinal);

        public IReadOnlyList<SkinDefinitionSO> Skins => skins;
        public IReadOnlyList<CaseDefinitionSO> Cases => cases;

        // ── Startup ──────────────────────────────────────────────────────────

        // Called by GameContext.Awake. Loads skins (catalog or filesystem), builds
        // the skin lookup, THEN builds cases — order matters so case building can
        // safely resolve skin IDs via GetSkin without re-entering BuildLookups.
        public void BuildLookups()
        {
            if (loadSkinsFromFileSystem)
                LoadSkinsFromSource();

            _skinLookup = skins
                .Where(s => s != null)
                .GroupBy(s => s.SkinId)
                .ToDictionary(g => g.Key, g => g.First());

            if (loadSkinsFromFileSystem)
                BuildCasesFromSource();

            ApplyBalanceOverrides();

            _caseLookup = cases
                .Where(c => c != null)
                .GroupBy(c => c.CaseId)
                .ToDictionary(g => g.Key, g => g.First());

            Debug.Log($"[ContentDatabase] Hazır — {skins.Count} skin, {cases.Count} kasa, " +
                      $"{_legacyToStable.Count} legacy eşleme.");
        }

        // Loads the skin list + legacy→stable map. Prefers the authored catalog
        // (Resources/Config/skins.json); falls back to the legacy filesystem scan
        // when the catalog is absent or yields nothing, so the project never breaks.
        void LoadSkinsFromSource()
        {
            _legacyToStable = new Dictionary<string, string>(StringComparer.Ordinal);

            var catalog = CatalogLoader.LoadSkinCatalog();
            if (catalog?.skins != null && catalog.skins.Count > 0)
            {
                var built = FileSystemSkinLoader.BuildFromCatalog(catalog);
                if (built.Count > 0)
                {
                    skins = built;
                    foreach (var e in catalog.skins)
                    {
                        if (e == null || string.IsNullOrEmpty(e.legacyId) || string.IsNullOrEmpty(e.skinId))
                            continue;
                        _legacyToStable[e.legacyId] = e.skinId;
                    }
                    Debug.Log($"[ContentDatabase] Skin catalog yüklendi — {built.Count} skin, " +
                              $"{_legacyToStable.Count} legacy eşleme.");
                    return;
                }
                Debug.LogWarning("[ContentDatabase] Skin catalog 0 skin üretti — filesystem taramasına dönülüyor.");
            }

            // ── Fallback: legacy filesystem scan (original behavior, derived IDs) ──
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
        }

        // Builds the case list. Prefers the authored case catalog; falls back to the
        // runtime VandalCaseBuilder generation when absent or empty.
        void BuildCasesFromSource()
        {
            List<CaseDefinitionSO> built;

            var caseCatalog = CatalogLoader.LoadCaseCatalog();
            if (caseCatalog?.cases != null && caseCatalog.cases.Count > 0)
            {
                built = VandalCaseBuilder.BuildAllFromCatalog(this, caseCatalog);
                if (built.Count == 0)
                {
                    Debug.LogWarning("[ContentDatabase] Case catalog 0 kasa üretti — üretilen Vandal kasalarına dönülüyor.");
                    built = VandalCaseBuilder.BuildAll(this);
                }
            }
            else
            {
                built = VandalCaseBuilder.BuildAll(this);
            }

            cases = built ?? new List<CaseDefinitionSO>();
        }

        // ── Balance overrides (no-op when no override file exists) ────────────
        // Mutates only the runtime-built SO instances; never writes catalog assets.
        void ApplyBalanceOverrides()
        {
            var svc = BalanceOverrideService.Instance;
            if (svc == null || !svc.HasAny) return;

            foreach (var s in skins)
                if (s != null && svc.TryGetSkinVp(s.SkinId, out int vp))
                    s.SetVpValueRuntime(vp);

            var kept = new List<CaseDefinitionSO>(cases.Count);
            foreach (var c in cases)
            {
                if (c == null) continue;
                if (!svc.IsCaseEnabled(c.CaseId)) continue;
                if (svc.TryGetCasePrice(c.CaseId, out int price)) c.SetVpPriceRuntime(price);
                ApplyDropOverrides(c, svc);
                kept.Add(c);
            }
            cases = kept;
        }

        void ApplyDropOverrides(CaseDefinitionSO c, BalanceOverrideService svc)
        {
            var table = c.DropTable;
            if (table == null) return;

            var drops = new List<SkinDropEntry>();
            foreach (var d in table.PossibleDrops)
            {
                if (d?.skin == null) continue;
                var sid = d.skin.SkinId;
                if (!svc.IsSkinEnabled(sid)) continue;
                if (!svc.IsDropEnabled(c.CaseId, sid)) continue;
                drops.Add(new SkinDropEntry
                {
                    skin = d.skin,
                    skinWeightOverride = svc.GetDropWeight(c.CaseId, sid, d.skinWeightOverride)
                });
            }
            table.RebuildDropsRuntime(drops);
        }

        // ── Save migration (one-time, version-gated) ──────────────────────────
        // Rewrites legacy skin IDs in a loaded save to stable catalog IDs.
        // NO-OP unless a skin catalog is loaded (legacy map present) and the save is
        // below version 2 — so saves stay untouched while the catalog is unpromoted.
        // Never drops an owned skin: unknown IDs are kept as-is and only logged.
        // Returns true if the save was changed (caller should persist it).
        public bool MigrateSaveIfNeeded(SaveDataRoot data)
        {
            if (data == null || data.version >= 2) return false;
            if (_legacyToStable == null || _legacyToStable.Count == 0) return false;

            int rewritten = 0, unknown = 0;

            if (data.inventory != null)
            {
                foreach (var entry in data.inventory)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.skinId)) continue;
                    if (_legacyToStable.TryGetValue(entry.skinId, out var stable))
                    {
                        if (stable != entry.skinId) { entry.skinId = stable; rewritten++; }
                    }
                    else if (_skinLookup != null && !_skinLookup.ContainsKey(entry.skinId))
                    {
                        unknown++;
                        Debug.LogWarning($"[Migrate] Unknown skinId kept as-is: {entry.skinId}");
                    }
                }
            }

            if (data.statistics != null && !string.IsNullOrEmpty(data.statistics.rarestSkinId) &&
                _legacyToStable.TryGetValue(data.statistics.rarestSkinId, out var rarestStable) &&
                rarestStable != data.statistics.rarestSkinId)
            {
                data.statistics.rarestSkinId = rarestStable;
                rewritten++;
            }

            data.version = 2;
            Debug.Log($"[Migrate] Save v2'ye taşındı — {rewritten} ID yeniden yazıldı, {unknown} bilinmeyen korundu.");
            return true;
        }

        // ── Queries ───────────────────────────────────────────────────────────

        // Lenient resolution: direct stable-ID hit first, then legacy→stable fallback
        // so old/un-migrated saves still resolve. Returns null + warning if neither hits.
        public SkinDefinitionSO GetSkin(string skinId)
        {
            if (_skinLookup == null) BuildLookups();
            if (string.IsNullOrEmpty(skinId)) return null;

            if (_skinLookup != null && _skinLookup.TryGetValue(skinId, out var skin))
                return skin;

            if (_legacyToStable != null && _legacyToStable.TryGetValue(skinId, out var stable) &&
                _skinLookup != null && _skinLookup.TryGetValue(stable, out var mapped))
                return mapped;

            Debug.LogWarning($"[ContentDatabase] GetSkin: çözülemeyen ID '{skinId}'.");
            return null;
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
