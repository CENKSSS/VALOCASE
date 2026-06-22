using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.EditorTools
{
    /// <summary>
    /// Phase-2 Case Pool Editor — EDITOR-ONLY GUI for safely curating the
    /// manualDropPool of each case in cases.json.
    ///
    /// Reuses existing logic:
    ///   • CatalogLoader.LoadSkinCatalog() / LoadCaseCatalog()  — loading.
    ///   • CasePoolTools.ValidateCasePools()                    — post-save validation.
    ///
    /// Guarantees (matches the follow-up brief):
    ///   • Never mints, renames, or cleans skin IDs — IDs are FROZEN.
    ///   • Writes ONLY to cases.json; cases.generated.json is left untouched.
    ///   • A skin MAY belong to multiple cases (no cross-case exclusivity).
    ///   • Duplicate IDs inside the SAME case are prevented.
    ///   • No runtime / save / inventory / VP / upgrade / battle / opening / UI impact.
    /// </summary>
    public sealed class CasePoolEditorWindow : EditorWindow
    {
        const string ConfigDir = "Assets/_ValoCase/Resources/Config";
        const string CasesFile  = "cases.json";
        const int    MaxRows    = 300; // cap rendered "available" rows for IMGUI perf

        // ── State ────────────────────────────────────────────────────────────
        SkinCatalogRoot _skinRoot;
        CaseCatalogRoot _caseRoot;
        Dictionary<string, SkinCatalogEntry> _skinById;

        string[] _caseLabels = Array.Empty<string>();
        int      _caseIndex;

        string   _search = "";
        string[] _rarityOptions = { "All", "Select", "Deluxe", "Premium", "Exclusive", "Ultra", "Melee" };
        int      _rarityIndex;
        string[] _weaponOptions = { "All" };
        int      _weaponIndex;

        readonly List<SkinCatalogEntry> _filtered = new();
        Vector2 _poolScroll, _availScroll;
        bool _dirty;

        [MenuItem("ValoCase/Stable IDs/Case Pool Editor")]
        public static void Open()
        {
            var win = GetWindow<CasePoolEditorWindow>("Case Pool Editor");
            win.minSize = new Vector2(820, 480);
            win.LoadCatalogs();
            win.Show();
        }

        void OnEnable()
        {
            if (_skinRoot == null || _caseRoot == null) LoadCatalogs();
        }

        // ── Loading ──────────────────────────────────────────────────────────
        void LoadCatalogs()
        {
            _skinRoot = CatalogLoader.LoadSkinCatalog();
            _caseRoot = CatalogLoader.LoadCaseCatalog();
            _dirty = false;

            _skinById = new Dictionary<string, SkinCatalogEntry>(StringComparer.Ordinal);
            if (_skinRoot?.skins != null)
                foreach (var e in _skinRoot.skins)
                    if (e != null && !string.IsNullOrEmpty(e.skinId) && !_skinById.ContainsKey(e.skinId))
                        _skinById[e.skinId] = e;

            _caseLabels = _caseRoot?.cases != null
                ? _caseRoot.cases.Select(c => c == null ? "<null>"
                    : $"{c.caseId}  ({(c.manualDropPool?.Length ?? 0)})").ToArray()
                : Array.Empty<string>();
            _caseIndex = Mathf.Clamp(_caseIndex, 0, Mathf.Max(0, _caseLabels.Length - 1));

            // Weapon filter options from the skin catalog.
            var weapons = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_skinRoot?.skins != null)
                foreach (var e in _skinRoot.skins)
                    if (e != null && !string.IsNullOrEmpty(e.weapon)) weapons.Add(e.weapon);
            _weaponOptions = new[] { "All" }.Concat(weapons).ToArray();
            _weaponIndex = Mathf.Clamp(_weaponIndex, 0, _weaponOptions.Length - 1);

            RecomputeFilter();
        }

        void RecomputeFilter()
        {
            _filtered.Clear();
            if (_skinRoot?.skins == null) return;

            var q = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim().ToLowerInvariant();
            SkinRarity? rf = _rarityIndex > 0 && Enum.TryParse<SkinRarity>(_rarityOptions[_rarityIndex], true, out var r)
                ? r : (SkinRarity?)null;
            var wf = _weaponIndex > 0 ? _weaponOptions[_weaponIndex] : null;

            foreach (var e in _skinRoot.skins)
            {
                if (e == null || string.IsNullOrEmpty(e.skinId)) continue;
                if (wf != null && !string.Equals(e.weapon, wf, StringComparison.OrdinalIgnoreCase)) continue;
                if (rf.HasValue)
                {
                    if (!Enum.TryParse<SkinRarity>(e.rarity, true, out var er) || er != rf.Value) continue;
                }
                if (q != null)
                {
                    var hit = (e.skinId?.ToLowerInvariant().Contains(q) ?? false)
                           || (e.displayName?.ToLowerInvariant().Contains(q) ?? false)
                           || (e.weapon?.ToLowerInvariant().Contains(q) ?? false);
                    if (!hit) continue;
                }
                _filtered.Add(e);
            }
        }

        // ── GUI ──────────────────────────────────────────────────────────────
        void OnGUI()
        {
            DrawToolbar();

            if (_skinRoot?.skins == null || _caseRoot?.cases == null || _caseRoot.cases.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "skins.json and/or cases.json could not be loaded from Resources/Config.\n" +
                    "Promote the generated catalogs first (rename *.generated.json), then Reload.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _caseIndex = EditorGUILayout.Popup("Editing Case", _caseIndex, _caseLabels);
            if (EditorGUI.EndChangeCheck())
                _caseIndex = Mathf.Clamp(_caseIndex, 0, _caseRoot.cases.Count - 1);

            EditorGUILayout.Space(4);
            var leftW = Mathf.Max(300f, position.width * 0.46f);

            EditorGUILayout.BeginHorizontal();
            DrawPoolColumn(leftW);
            GUILayout.Space(8);
            DrawAvailableColumn();
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                if (!_dirty || EditorUtility.DisplayDialog("Reload",
                        "Discard unsaved changes and reload from disk?", "Discard", "Cancel"))
                    LoadCatalogs();
            }

            using (new EditorGUI.DisabledScope(!_dirty))
            {
                if (GUILayout.Button("Save → cases.json", EditorStyles.toolbarButton, GUILayout.Width(140)))
                    Save();
            }

            if (GUILayout.Button("Validate Now", EditorStyles.toolbarButton, GUILayout.Width(100)))
                CasePoolTools.ValidateCasePools();

            GUILayout.FlexibleSpace();
            GUILayout.Label(_dirty ? "● Unsaved changes" : "Saved",
                _dirty ? Warn() : EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        void DrawPoolColumn(float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));

            var c = _caseRoot.cases[_caseIndex];
            var pool = c.manualDropPool ?? Array.Empty<string>();
            EditorGUILayout.LabelField($"Current Pool — {c.displayName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{pool.Length} skin(s)", EditorStyles.miniLabel);

            _poolScroll = EditorGUILayout.BeginScrollView(_poolScroll,
                GUILayout.Width(width), GUILayout.ExpandHeight(true));

            string removeId = null;
            foreach (var id in pool)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                if (_skinById.TryGetValue(id, out var s))
                {
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(s.displayName, EditorStyles.label);
                    EditorGUILayout.LabelField($"{s.weapon} · {s.rarity} · {s.vpValue:N0} VP{(s.enabled ? "" : " · DISABLED")}  —  {id}",
                        EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.LabelField($"MISSING (not in skins.json): {id}", Warn());
                }
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    removeId = id;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (removeId != null)
            {
                c.manualDropPool = pool.Where(x => x != removeId).ToArray();
                MarkDirty();
            }

            EditorGUILayout.EndVertical();
        }

        void DrawAvailableColumn()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField("Available Skins (skins.json)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _search = EditorGUILayout.TextField("Search (id/name/weapon)", _search);
            EditorGUILayout.BeginHorizontal();
            _weaponIndex = EditorGUILayout.Popup("Weapon", _weaponIndex, _weaponOptions);
            _rarityIndex = EditorGUILayout.Popup("Rarity", _rarityIndex, _rarityOptions);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
                RecomputeFilter();

            var c = _caseRoot.cases[_caseIndex];
            var poolSet = new HashSet<string>(c.manualDropPool ?? Array.Empty<string>(), StringComparer.Ordinal);

            var shown = Mathf.Min(_filtered.Count, MaxRows);
            EditorGUILayout.LabelField(
                _filtered.Count > MaxRows
                    ? $"Showing {shown} of {_filtered.Count} (refine search to see more)"
                    : $"{_filtered.Count} match(es)",
                EditorStyles.miniLabel);

            _availScroll = EditorGUILayout.BeginScrollView(_availScroll, GUILayout.ExpandHeight(true));

            string addId = null;
            for (var i = 0; i < shown; i++)
            {
                var e = _filtered[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(e.displayName + (e.enabled ? "" : "  (DISABLED)"), EditorStyles.label);
                EditorGUILayout.LabelField($"{e.weapon} · {e.rarity} · {e.vpValue:N0} VP  —  {e.skinId}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                if (poolSet.Contains(e.skinId))
                    GUILayout.Label("✓ In case", EditorStyles.miniLabel, GUILayout.Width(70));
                else if (GUILayout.Button("Add", GUILayout.Width(70)))
                    addId = e.skinId;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (addId != null && !poolSet.Contains(addId)) // dup-in-case guard
            {
                c.manualDropPool = (c.manualDropPool ?? Array.Empty<string>()).Append(addId).ToArray();
                MarkDirty();
            }

            EditorGUILayout.EndVertical();
        }

        // ── Save ─────────────────────────────────────────────────────────────
        void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var path = Path.Combine(ConfigDir, CasesFile);
                File.WriteAllText(path, JsonUtility.ToJson(_caseRoot, true));
                AssetDatabase.Refresh();
                _dirty = false;

                // Refresh the case-count labels after save.
                _caseLabels = _caseRoot.cases.Select(c => c == null ? "<null>"
                    : $"{c.caseId}  ({(c.manualDropPool?.Length ?? 0)})").ToArray();

                Debug.Log($"[CasePoolEditor] Saved {_caseRoot.cases.Count} case(s) → {path}. " +
                          "cases.generated.json was NOT touched. Running validation…");

                // Reuse the existing Phase-2 validator (re-loads from disk + reports).
                CasePoolTools.ValidateCasePools();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CasePoolEditor] Save failed: {ex.Message}");
                EditorUtility.DisplayDialog("Save failed", ex.Message, "OK");
            }
        }

        void MarkDirty()
        {
            _dirty = true;
            _caseLabels = _caseRoot.cases.Select(c => c == null ? "<null>"
                : $"{c.caseId}  ({(c.manualDropPool?.Length ?? 0)})").ToArray();
        }

        static GUIStyle _warn;
        static GUIStyle Warn()
        {
            if (_warn == null)
                _warn = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.9f, 0.4f, 0.4f) } };
            return _warn;
        }
    }
}
