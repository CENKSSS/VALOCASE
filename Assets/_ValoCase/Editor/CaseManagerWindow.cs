using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.EditorTools
{
    public sealed class CaseManagerWindow : EditorWindow
    {
        const string CasesPath = "Assets/_ValoCase/Resources/Config/cases.json";
        const string SkinsPath = "Assets/_ValoCase/Resources/Config/skins.json";
        const string CaseArtDir = "Assets/_ValoCase/Resources/Art/Cases";
        const string CaseArtResourcePrefix = "Art/Cases/";

        static readonly string[] RarityOrder = { "Select", "Deluxe", "Premium", "Exclusive", "Ultra", "Melee" };

        sealed class SkinRow
        {
            public string skinId, displayName, weapon, rarity, resourceKey;
            public int vpValue;
            public bool enabled;
        }

        CaseCatalogRoot _root;
        readonly List<SkinRow> _skins = new();
        readonly Dictionary<string, SkinRow> _skinById = new(StringComparer.Ordinal);
        string[] _weaponOptions = { "All" };
        string[] _rarityFilterOptions = { "All" };
        string[] _caseIconNames = Array.Empty<string>();

        int _selectedCase = -1;
        bool _dirty;

        Vector2 _caseListScroll, _detailScroll, _catalogScroll, _poolScroll;
        string _caseSearch = "";
        string _poolSearch = "";
        int _poolWeaponIdx, _poolRarityIdx;
        string _selectedCatalogSkin;
        readonly HashSet<string> _checkedCatalogSkins = new(StringComparer.Ordinal);
        int _selectedPoolIdx = -1;

        int _randWeaponIdx;
        int _randVpMin, _randVpMax = 99999, _randCount = 20;

        [MenuItem("ValoCase/Catalog/Case Manager")]
        static void Open()
        {
            var w = GetWindow<CaseManagerWindow>("Case Manager");
            w.minSize = new Vector2(960, 600);
            w.Show();
        }

        void OnEnable()
        {
            LoadSkins();
            LoadCases();
        }

        // ── Load / save ─────────────────────────────────────────────────────────

        void LoadSkins()
        {
            _skins.Clear();
            _skinById.Clear();
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SkinsPath);
            if (asset != null)
            {
                var root = JsonUtility.FromJson<SkinCatalogRoot>(asset.text);
                if (root?.skins != null)
                {
                    foreach (var e in root.skins)
                    {
                        if (e == null || string.IsNullOrEmpty(e.skinId)) continue;
                        var row = new SkinRow
                        {
                            skinId = e.skinId, displayName = e.displayName, weapon = e.weapon,
                            rarity = e.rarity, resourceKey = e.resourceKey, vpValue = e.vpValue, enabled = e.enabled
                        };
                        _skins.Add(row);
                        _skinById[row.skinId] = row;
                    }
                }
            }
            var weapons = _skins.Select(s => s.weapon).Where(w => !string.IsNullOrEmpty(w))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
            _weaponOptions = new[] { "All" }.Concat(weapons).ToArray();
            _rarityFilterOptions = new[] { "All" }.Concat(RarityOrder).ToArray();
        }

        void LoadCases()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(CasesPath);
            _root = asset != null ? JsonUtility.FromJson<CaseCatalogRoot>(asset.text) : null;
            _root ??= new CaseCatalogRoot { version = 1, cases = new List<CaseCatalogEntry>() };
            _root.cases ??= new List<CaseCatalogEntry>();
            _selectedCase = _root.cases.Count > 0 ? 0 : -1;
            RefreshCaseIcons();
            _dirty = false;
        }

        void RefreshCaseIcons()
        {
            if (!Directory.Exists(CaseArtDir)) { _caseIconNames = Array.Empty<string>(); return; }
            _caseIconNames = Directory.GetFiles(CaseArtDir, "*.png")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        void Save()
        {
            var issues = Validate();
            var blocking = issues.Where(i => i.StartsWith("ERROR")).ToList();
            if (blocking.Count > 0 &&
                !EditorUtility.DisplayDialog("Validation errors",
                    $"{blocking.Count} blocking issue(s):\n\n" + string.Join("\n", blocking.Take(12)) +
                    "\n\nSave anyway?", "Save anyway", "Cancel"))
                return;

            File.WriteAllText(CasesPath, JsonUtility.ToJson(_root, true));
            AssetDatabase.ImportAsset(CasesPath);

            var reparsed = JsonUtility.FromJson<CaseCatalogRoot>(File.ReadAllText(CasesPath));
            bool ok = reparsed?.cases != null && reparsed.cases.Count == _root.cases.Count;
            _dirty = false;
            ShowNotification(new GUIContent(ok ? "Saved + parsed OK" : "Saved (parse check FAILED)"));
            Debug.Log($"[CaseManager] Saved {_root.cases.Count} cases to {CasesPath} — reparse {(ok ? "OK" : "FAILED")}, {issues.Count} validation note(s).");
        }

        // ── GUI ─────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60))) Save();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60))) { LoadSkins(); LoadCases(); }
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70))) ReportValidation();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(_dirty ? "Unsaved changes" : "Saved",
                    _dirty ? EditorStyles.boldLabel : EditorStyles.miniLabel, GUILayout.Width(120));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCaseList();
                DrawDetail();
            }
        }

        void DrawCaseList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
            {
                EditorGUILayout.LabelField($"Cases ({_root.cases.Count})", EditorStyles.boldLabel);
                _caseSearch = EditorGUILayout.TextField("Search", _caseSearch);
                if (GUILayout.Button("+ New Case")) CreateNewCase();

                _caseListScroll = EditorGUILayout.BeginScrollView(_caseListScroll);
                int shown = 0;
                for (int i = 0; i < _root.cases.Count; i++)
                {
                    var c = _root.cases[i];
                    if (!CaseMatchesSearch(c)) continue;
                    shown++;
                    var label = $"{(c.enabled ? "" : "(off) ")}{c.caseId}";
                    var style = i == _selectedCase ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUILayout.Button(label, style)) { _selectedCase = i; _selectedPoolIdx = -1; }
                }
                if (shown == 0 && !string.IsNullOrEmpty(_caseSearch))
                    EditorGUILayout.LabelField("No matches", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_selectedCase < 0))
                    if (GUILayout.Button("Delete Selected Case")) DeleteSelectedCase();
            }
        }

        bool CaseMatchesSearch(CaseCatalogEntry c)
        {
            if (string.IsNullOrWhiteSpace(_caseSearch)) return true;
            var q = _caseSearch.Trim();
            return (c.caseId != null && c.caseId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (c.displayName != null && c.displayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void CreateNewCase()
        {
            string id = "new_case";
            int n = 2;
            while (_root.cases.Any(c => c.caseId == id)) { id = "new_case_" + n; n++; }
            var entry = new CaseCatalogEntry
            {
                caseId = id, displayName = "New Case", price = 1000, resourceKey = "",
                enabled = true, themeColor = "#FF4655",
                rarityWeights = RarityOrder.Select(r => new CaseRarityWeight { rarity = r, weight = r == "Exclusive" ? 100f : 0f }).ToList(),
                manualDropPool = Array.Empty<string>()
            };
            _root.cases.Add(entry);
            _selectedCase = _root.cases.Count - 1;
            _dirty = true;
        }

        void DeleteSelectedCase()
        {
            var c = _root.cases[_selectedCase];
            if (!EditorUtility.DisplayDialog("Delete case",
                $"Delete case '{c.caseId}'? This cannot be undone (until you choose not to save).", "Delete", "Cancel"))
                return;
            _root.cases.RemoveAt(_selectedCase);
            _selectedCase = Mathf.Clamp(_selectedCase, -1, _root.cases.Count - 1);
            _dirty = true;
        }

        void DrawDetail()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_selectedCase < 0 || _selectedCase >= _root.cases.Count)
                {
                    EditorGUILayout.HelpBox("Select a case on the left, or create a new one.", MessageType.Info);
                    return;
                }
                var c = _root.cases[_selectedCase];

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.LabelField("Case Fields", EditorStyles.boldLabel);
                c.caseId = EditorGUILayout.TextField("Case Id", c.caseId);
                c.displayName = EditorGUILayout.TextField("Display Name", c.displayName);
                c.price = Mathf.Max(0, EditorGUILayout.IntField("Price", c.price));
                c.enabled = EditorGUILayout.Toggle("Enabled", c.enabled);
                DrawThemeColor(c);

                if (EditorGUI.EndChangeCheck()) _dirty = true;

                EditorGUILayout.Space(6);
                DrawImageSection(c);

                EditorGUILayout.Space(6);
                DrawRarityWeights(c);

                EditorGUILayout.Space(6);
                DrawPoolSection(c);

                EditorGUILayout.EndScrollView();
            }
        }

        void DrawThemeColor(CaseCatalogEntry c)
        {
            var col = ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(c.themeColor) ? "#FF4655" : c.themeColor, out var parsed)
                ? parsed : Color.white;
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = EditorGUILayout.ColorField("Theme Color", col);
                if (next != col) { c.themeColor = "#" + ColorUtility.ToHtmlStringRGB(next); _dirty = true; }
                c.themeColor = EditorGUILayout.TextField(c.themeColor, GUILayout.Width(90));
            }
        }

        void DrawImageSection(CaseCatalogEntry c)
        {
            EditorGUILayout.LabelField("Case Image", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                c.resourceKey = EditorGUILayout.TextField("Resource Key", c.resourceKey);
                if (GUILayout.Button("Pick PNG", GUILayout.Width(80)))
                {
                    RefreshCaseIcons();
                    var menu = new GenericMenu();
                    foreach (var name in _caseIconNames)
                    {
                        var rk = CaseArtResourcePrefix + name;
                        menu.AddItem(new GUIContent(name), c.resourceKey == rk, () =>
                        {
                            c.resourceKey = rk; _dirty = true; Repaint();
                        });
                    }
                    if (_caseIconNames.Length == 0) menu.AddDisabledItem(new GUIContent("No PNGs in Art/Cases"));
                    menu.ShowAsContext();
                }
            }

            var sprite = string.IsNullOrEmpty(c.resourceKey) ? null : Resources.Load<Sprite>(c.resourceKey);
            if (sprite != null && sprite.texture != null)
            {
                var r = GUILayoutUtility.GetRect(96, 96, GUILayout.Width(96), GUILayout.Height(96));
                var tr = sprite.textureRect; var t = sprite.texture;
                GUI.DrawTextureWithTexCoords(r, t,
                    new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height), true);
            }
            else if (!string.IsNullOrEmpty(c.resourceKey))
            {
                EditorGUILayout.HelpBox($"resourceKey '{c.resourceKey}' does not resolve to a loadable Sprite under Resources.", MessageType.Warning);
            }
        }

        void DrawRarityWeights(CaseCatalogEntry c)
        {
            EditorGUILayout.LabelField("Rarity Weights", EditorStyles.boldLabel);
            c.rarityWeights ??= new List<CaseRarityWeight>();
            foreach (var r in RarityOrder)
                if (!c.rarityWeights.Any(w => w != null && w.rarity == r))
                    c.rarityWeights.Add(new CaseRarityWeight { rarity = r, weight = 0f });

            EditorGUI.BeginChangeCheck();
            foreach (var r in RarityOrder)
            {
                var w = c.rarityWeights.First(x => x.rarity == r);
                w.weight = Mathf.Max(0f, EditorGUILayout.FloatField(r, w.weight));
            }
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            float total = c.rarityWeights.Where(w => RarityOrder.Contains(w.rarity)).Sum(w => w.weight);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Normalize to 100"))
                {
                    if (total > 0f) foreach (var w in c.rarityWeights) w.weight = w.weight / total * 100f;
                    _dirty = true;
                }
                if (GUILayout.Button("Melee 100"))
                {
                    foreach (var w in c.rarityWeights) w.weight = w.rarity == "Melee" ? 100f : 0f;
                    _dirty = true;
                }
            }
            if (Mathf.Abs(total - 100f) > 0.01f)
                EditorGUILayout.HelpBox($"Total weight is {total:0.##}, not 100.", MessageType.Warning);

            var poolRarities = c.manualDropPool.Where(_skinById.ContainsKey).Select(id => _skinById[id].rarity).ToHashSet();
            foreach (var r in RarityOrder)
            {
                var w = c.rarityWeights.First(x => x.rarity == r);
                if (w.weight > 0f && !poolRarities.Contains(r))
                    EditorGUILayout.HelpBox($"Rarity '{r}' has weight {w.weight:0.##} but no skin of that rarity is in the pool.", MessageType.Warning);
            }

            EditorGUILayout.Space(2);
            var ev = ComputeExpectedValue(c, out var evMissing);
            EditorGUILayout.LabelField("Average Reward Value",
                ev.HasValue ? $"{ev.Value:N0} VP" : "—", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(evMissing))
                EditorGUILayout.HelpBox(evMissing, MessageType.Info);
        }

        // Display-only EV: per-rarity average pool VP weighted by the rarity weights,
        // normalized over rarities that actually have pool skins. Never mutates state.
        float? ComputeExpectedValue(CaseCatalogEntry c, out string missingWarning)
        {
            missingWarning = null;
            var pool = c.manualDropPool ?? Array.Empty<string>();
            if (pool.Length == 0 || c.rarityWeights == null) return null;

            var vpByRarity = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in pool)
            {
                if (string.IsNullOrEmpty(id) || !_skinById.TryGetValue(id, out var s)) continue;
                if (string.IsNullOrEmpty(s.rarity)) continue;
                if (!vpByRarity.TryGetValue(s.rarity, out var list))
                    vpByRarity[s.rarity] = list = new List<int>();
                list.Add(Mathf.Max(0, s.vpValue));
            }

            float weightedSum = 0f, totalWeight = 0f;
            var missing = new List<string>();
            foreach (var w in c.rarityWeights)
            {
                if (w == null || w.weight <= 0f) continue;
                if (!vpByRarity.TryGetValue(w.rarity, out var vps) || vps.Count == 0)
                {
                    missing.Add(w.rarity);
                    continue;
                }
                weightedSum += (float)vps.Average() * w.weight;
                totalWeight += w.weight;
            }

            if (missing.Count > 0) missingWarning = "Missing pool for: " + string.Join(", ", missing);
            if (totalWeight <= 0f) return null;
            return weightedSum / totalWeight;
        }

        void DrawPoolSection(CaseCatalogEntry c)
        {
            EditorGUILayout.LabelField($"Drop Pool ({c.manualDropPool.Length})", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCatalogColumn(c);
                DrawPoolColumn(c);
            }

            EditorGUILayout.Space(4);
            DrawRandomTools(c);
        }

        void DrawCatalogColumn(CaseCatalogEntry c)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(420)))
            {
                EditorGUILayout.LabelField("Catalog Skins", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _poolSearch = EditorGUILayout.TextField(_poolSearch);
                    _poolWeaponIdx = EditorGUILayout.Popup(_poolWeaponIdx, _weaponOptions, GUILayout.Width(110));
                    _poolRarityIdx = EditorGUILayout.Popup(_poolRarityIdx, _rarityFilterOptions, GUILayout.Width(100));
                }

                var filtered = FilterSkins(_poolWeaponIdx, _poolRarityIdx, _poolSearch);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{filtered.Count} match", EditorStyles.miniLabel, GUILayout.Width(70));
                    EditorGUILayout.LabelField($"Selected: {_checkedCatalogSkins.Count}", EditorStyles.miniBoldLabel, GUILayout.Width(90));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Select All", GUILayout.Width(80)))
                        foreach (var s in filtered) _checkedCatalogSkins.Add(s.skinId);
                    if (GUILayout.Button("Deselect All", GUILayout.Width(90)))
                        _checkedCatalogSkins.Clear();
                }

                _catalogScroll = EditorGUILayout.BeginScrollView(_catalogScroll, GUILayout.Height(220));
                foreach (var s in filtered)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool wasChecked = _checkedCatalogSkins.Contains(s.skinId);
                        bool nowChecked = EditorGUILayout.Toggle(wasChecked, GUILayout.Width(18));
                        if (nowChecked != wasChecked)
                        {
                            if (nowChecked) _checkedCatalogSkins.Add(s.skinId);
                            else _checkedCatalogSkins.Remove(s.skinId);
                        }

                        bool inPool = c.manualDropPool.Contains(s.skinId);
                        var style = s.skinId == _selectedCatalogSkin ? EditorStyles.boldLabel : EditorStyles.label;
                        if (GUILayout.Button($"{(inPool ? "● " : "")}{s.displayName}  {s.weapon} · {s.rarity} · {s.vpValue}vp  {s.skinId}", style))
                            _selectedCatalogSkin = s.skinId;
                    }
                }
                EditorGUILayout.EndScrollView();

                if (_selectedCatalogSkin != null && _skinById.TryGetValue(_selectedCatalogSkin, out var sel))
                    EditorGUILayout.LabelField($"{sel.displayName} | {sel.weapon} | {sel.rarity} | {sel.vpValue} VP | id={sel.skinId}", EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_checkedCatalogSkins.Count == 0))
                        if (GUILayout.Button($"Add Selected → ({_checkedCatalogSkins.Count})"))
                            AddCheckedToPool(c);
                    using (new EditorGUI.DisabledScope(_selectedCatalogSkin == null))
                        if (GUILayout.Button("Add Highlighted →", GUILayout.Width(140)))
                            AddToPool(c, _selectedCatalogSkin);
                }
            }
        }

        void DrawPoolColumn(CaseCatalogEntry c)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Current Pool ({c.manualDropPool.Length})", EditorStyles.miniBoldLabel);
                _poolScroll = EditorGUILayout.BeginScrollView(_poolScroll, GUILayout.Height(220));
                for (int i = 0; i < c.manualDropPool.Length; i++)
                {
                    var id = c.manualDropPool[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool known = _skinById.TryGetValue(id, out var s);
                        var label = known ? $"{s.displayName} [{s.rarity} {s.vpValue}]" : $"{id}  (MISSING)";
                        var style = i == _selectedPoolIdx ? EditorStyles.boldLabel : (known ? EditorStyles.label : EditorStyles.miniBoldLabel);
                        if (GUILayout.Button(label, style)) _selectedPoolIdx = i;
                        if (GUILayout.Button("✕", GUILayout.Width(24))) { RemoveFromPool(c, id); break; }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        void DrawRandomTools(CaseCatalogEntry c)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Random / Pool Tools", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Weapon", GUILayout.Width(50));
                    _randWeaponIdx = EditorGUILayout.Popup(_randWeaponIdx, _weaponOptions, GUILayout.Width(110));
                    GUILayout.Label("VP", GUILayout.Width(20));
                    _randVpMin = EditorGUILayout.IntField(_randVpMin, GUILayout.Width(70));
                    GUILayout.Label("-", GUILayout.Width(10));
                    _randVpMax = EditorGUILayout.IntField(_randVpMax, GUILayout.Width(70));
                    GUILayout.Label("Count", GUILayout.Width(40));
                    _randCount = Mathf.Max(1, EditorGUILayout.IntField(_randCount, GUILayout.Width(50)));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Generate Random Pool")) GenerateRandomPool(c);
                    if (GUILayout.Button("Clear Pool")) { c.manualDropPool = Array.Empty<string>(); _selectedPoolIdx = -1; _dirty = true; }
                    if (GUILayout.Button("Validate Pool")) ReportPoolValidation(c);
                }
            }
        }

        // ── Pool operations ───────────────────────────────────────────────────────

        List<SkinRow> FilterSkins(int weaponIdx, int rarityIdx, string search)
        {
            string weapon = weaponIdx > 0 ? _weaponOptions[weaponIdx] : null;
            string rarity = rarityIdx > 0 ? _rarityFilterOptions[rarityIdx] : null;
            string q = search?.Trim();
            return _skins.Where(s =>
            {
                if (!s.enabled) return false;
                if (weapon != null && !string.Equals(s.weapon, weapon, StringComparison.OrdinalIgnoreCase)) return false;
                if (rarity != null && s.rarity != rarity) return false;
                if (!string.IsNullOrEmpty(q) &&
                    (s.displayName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (s.skinId?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) < 0) return false;
                return true;
            }).ToList();
        }

        void AddToPool(CaseCatalogEntry c, string skinId)
        {
            if (string.IsNullOrEmpty(skinId) || c.manualDropPool.Contains(skinId)) return;
            c.manualDropPool = c.manualDropPool.Append(skinId).ToArray();
            _dirty = true;
        }

        void AddCheckedToPool(CaseCatalogEntry c)
        {
            if (_checkedCatalogSkins.Count == 0) return;
            var existing = new HashSet<string>(c.manualDropPool, StringComparer.Ordinal);
            var toAdd = _checkedCatalogSkins.Where(id => _skinById.ContainsKey(id) && !existing.Contains(id)).ToList();
            if (toAdd.Count == 0) { ShowNotification(new GUIContent("All checked skins already in pool")); return; }
            c.manualDropPool = c.manualDropPool.Concat(toAdd).ToArray();
            _checkedCatalogSkins.Clear();
            _selectedPoolIdx = -1;
            _dirty = true;
            ShowNotification(new GUIContent($"Added {toAdd.Count} skin(s)"));
        }

        void RemoveFromPool(CaseCatalogEntry c, string skinId)
        {
            c.manualDropPool = c.manualDropPool.Where(x => x != skinId).ToArray();
            _selectedPoolIdx = -1;
            _dirty = true;
        }

        void GenerateRandomPool(CaseCatalogEntry c)
        {
            var pool = FilterSkins(_randWeaponIdx, 0, null)
                .Where(s => s.vpValue >= _randVpMin && s.vpValue <= _randVpMax)
                .Select(s => s.skinId).ToList();
            if (pool.Count == 0) { ShowNotification(new GUIContent("No skins match the filter")); return; }

            var rng = new System.Random();
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            c.manualDropPool = pool.Take(Mathf.Min(_randCount, pool.Count)).ToArray();
            _selectedPoolIdx = -1;
            _dirty = true;
        }

        // ── Validation ────────────────────────────────────────────────────────────

        List<string> Validate()
        {
            var issues = new List<string>();
            var seenCaseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in _root.cases)
            {
                if (string.IsNullOrEmpty(c.caseId)) { issues.Add("ERROR: a case has an empty caseId."); continue; }
                if (!seenCaseIds.Add(c.caseId)) issues.Add($"ERROR: duplicate caseId '{c.caseId}'.");

                var pool = c.manualDropPool ?? Array.Empty<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in pool)
                {
                    if (!seen.Add(id)) issues.Add($"ERROR: '{c.caseId}' has duplicate skinId '{id}'.");
                    if (!_skinById.ContainsKey(id)) issues.Add($"ERROR: '{c.caseId}' references unknown skinId '{id}'.");
                    else if (!_skinById[id].enabled) issues.Add($"WARN: '{c.caseId}' references disabled skin '{id}'.");
                }
                if (c.enabled && pool.Length == 0) issues.Add($"ERROR: enabled case '{c.caseId}' has an empty pool.");
                if (!string.IsNullOrEmpty(c.resourceKey) && Resources.Load<Sprite>(c.resourceKey) == null)
                    issues.Add($"WARN: '{c.caseId}' resourceKey '{c.resourceKey}' does not resolve to a PNG.");
            }
            return issues;
        }

        void ReportValidation()
        {
            var issues = Validate();
            if (issues.Count == 0) { Debug.Log("[CaseManager] Validation: no issues."); ShowNotification(new GUIContent("Validation OK")); return; }
            foreach (var i in issues) { if (i.StartsWith("ERROR")) Debug.LogError("[CaseManager] " + i); else Debug.LogWarning("[CaseManager] " + i); }
            ShowNotification(new GUIContent($"{issues.Count} validation note(s) — see Console"));
        }

        void ReportPoolValidation(CaseCatalogEntry c)
        {
            var pool = c.manualDropPool ?? Array.Empty<string>();
            int dupes = pool.Length - pool.Distinct().Count();
            int unknown = pool.Count(id => !_skinById.ContainsKey(id));
            Debug.Log($"[CaseManager] Pool '{c.caseId}': size={pool.Length} duplicates={dupes} unknownIds={unknown}");
            ShowNotification(new GUIContent(dupes == 0 && unknown == 0 ? "Pool OK" : "Pool has issues — see Console"));
        }
    }
}
