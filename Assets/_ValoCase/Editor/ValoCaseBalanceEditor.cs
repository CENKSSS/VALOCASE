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
    public sealed class ValoCaseBalanceEditor : EditorWindow
    {
        enum Tab { Skins, Cases, Drops }

        sealed class SkinRow
        {
            public string skinId, name, weapon, rarityName, resourceKey;
            public SkinRarity rarity;
            public int defaultVp;
            Sprite _icon;
            bool _iconTried;
            public Sprite Icon
            {
                get
                {
                    if (!_iconTried)
                    {
                        _iconTried = true;
                        if (!string.IsNullOrEmpty(resourceKey))
                            _icon = Resources.Load<Sprite>(resourceKey);
                    }
                    return _icon;
                }
            }
        }

        sealed class CaseRow
        {
            public string caseId, name, resourceKey;
            public int defaultPrice;
            public List<string> dropSkinIds = new();
            public Dictionary<SkinRarity, float> rarityWeights = new();
            Sprite _icon;
            bool _iconTried;
            public Sprite Icon
            {
                get
                {
                    if (!_iconTried)
                    {
                        _iconTried = true;
                        if (!string.IsNullOrEmpty(resourceKey))
                            _icon = Resources.Load<Sprite>(resourceKey);
                    }
                    return _icon;
                }
            }
        }

        static readonly Color Accent  = new(1.000f, 0.275f, 0.333f, 1f);
        static readonly Color Missing = new(0.15f, 0.15f, 0.15f, 1f);

        Tab _tab;
        BalanceOverrideRoot _root = new();
        readonly Dictionary<string, SkinBalanceOverride> _skinOv = new(StringComparer.Ordinal);
        readonly Dictionary<string, CaseBalanceOverride> _caseOv = new(StringComparer.Ordinal);
        readonly Dictionary<string, CaseDropBalanceOverride> _dropOv = new(StringComparer.Ordinal);

        readonly List<SkinRow> _skins = new();
        readonly List<CaseRow> _cases = new();
        readonly Dictionary<string, SkinRow> _skinById = new(StringComparer.Ordinal);

        List<SkinRow> _filteredSkins = new();
        List<CaseRow> _filteredCases = new();
        bool _refilterSkins = true, _refilterCases = true;

        string _skinSearch = "", _caseSearch = "", _dropSearch = "";
        int _skinWeaponIdx, _skinRarityIdx, _dropWeaponIdx, _dropRarityIdx;
        int _skinSortIdx, _caseSortIdx;
        string[] _weaponOptions = { "All" };
        string[] _rarityOptions = { "All" };

        int _selectedCaseIdx;
        bool _dirty;

        Vector2 _skinScroll, _caseScroll, _dropScroll, _reportScroll;
        int _skinFirstVisible = -1, _dropFirstVisible = -1;
        List<string> _report = new();
        bool _showReport;

        const float RowH = 58f;

        [MenuItem("ValoCase/Balance Editor")]
        static void Open()
        {
            var w = GetWindow<ValoCaseBalanceEditor>("ValoCase Balance");
            w.minSize = new Vector2(840, 520);
            w.Show();
        }

        void OnEnable()
        {
            LoadCatalog();
            LoadOverrides();
        }

        // ── Loading ───────────────────────────────────────────────────────────

        void LoadCatalog()
        {
            _skins.Clear();
            _cases.Clear();
            _skinById.Clear();

            var skinCat = CatalogLoader.LoadSkinCatalog();
            if (skinCat?.skins != null)
            {
                foreach (var e in skinCat.skins)
                {
                    if (e == null || string.IsNullOrEmpty(e.skinId)) continue;
                    Enum.TryParse(e.rarity, out SkinRarity rar);
                    var row = new SkinRow
                    {
                        skinId = e.skinId,
                        name = e.displayName,
                        weapon = e.weapon,
                        rarity = rar,
                        rarityName = e.rarity,
                        defaultVp = e.vpValue,
                        resourceKey = e.resourceKey
                    };
                    _skins.Add(row);
                    _skinById[row.skinId] = row;
                }
            }

            var caseCat = CatalogLoader.LoadCaseCatalog();
            if (caseCat?.cases != null)
            {
                foreach (var e in caseCat.cases)
                {
                    if (e == null || string.IsNullOrEmpty(e.caseId)) continue;
                    var row = new CaseRow
                    {
                        caseId = e.caseId,
                        name = e.displayName,
                        defaultPrice = e.price,
                        resourceKey = e.resourceKey,
                        dropSkinIds = e.manualDropPool != null ? e.manualDropPool.ToList() : new List<string>()
                    };
                    if (e.rarityWeights != null)
                        foreach (var rw in e.rarityWeights)
                            if (rw != null && Enum.TryParse(rw.rarity, out SkinRarity rr))
                                row.rarityWeights[rr] = rw.weight;
                    _cases.Add(row);
                }
            }

            var weapons = _skins.Select(s => s.weapon)
                .Where(w => !string.IsNullOrEmpty(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
            _weaponOptions = new[] { "All" }.Concat(weapons).ToArray();
            _rarityOptions = new[] { "All" }.Concat(Enum.GetNames(typeof(SkinRarity))).ToArray();

            _refilterSkins = _refilterCases = true;
        }

        void LoadOverrides()
        {
            _root = new BalanceOverrideRoot();
            if (File.Exists(BalanceOverrideService.AssetPath))
            {
                var parsed = BalanceOverrideService.Deserialize(File.ReadAllText(BalanceOverrideService.AssetPath));
                if (parsed != null) _root = parsed;
            }
            RebuildOverrideMaps();
            _dirty = false;
        }

        void RebuildOverrideMaps()
        {
            _skinOv.Clear();
            _caseOv.Clear();
            _dropOv.Clear();
            _root.skinOverrides ??= new List<SkinBalanceOverride>();
            _root.caseOverrides ??= new List<CaseBalanceOverride>();
            _root.dropOverrides ??= new List<CaseDropBalanceOverride>();
            foreach (var s in _root.skinOverrides)
                if (s != null && !string.IsNullOrEmpty(s.skinId)) _skinOv[s.skinId] = s;
            foreach (var c in _root.caseOverrides)
                if (c != null && !string.IsNullOrEmpty(c.caseId)) _caseOv[c.caseId] = c;
            foreach (var d in _root.dropOverrides)
                if (d != null && !string.IsNullOrEmpty(d.caseId) && !string.IsNullOrEmpty(d.skinId))
                    _dropOv[DropKey(d.caseId, d.skinId)] = d;
        }

        static string DropKey(string caseId, string skinId) => caseId + "|" + skinId;

        // Commit the in-flight delayed field before the row layout shifts (reorder /
        // virtualized scroll), so an edit can never land on a remapped row.
        static void DefocusIfEditing()
        {
            var n = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(n)) return;
            if (n.StartsWith("vp:") || n.StartsWith("se:") ||
                n.StartsWith("price:") || n.StartsWith("ce:") ||
                n.StartsWith("dw:") || n.StartsWith("de:"))
                GUI.FocusControl(null);
        }

        // ── Override accessors ────────────────────────────────────────────────

        SkinBalanceOverride GetSkinOv(string id, bool create)
        {
            if (_skinOv.TryGetValue(id, out var o)) return o;
            if (!create) return null;
            o = new SkinBalanceOverride { skinId = id };
            _root.skinOverrides.Add(o);
            _skinOv[id] = o;
            return o;
        }

        CaseBalanceOverride GetCaseOv(string id, bool create)
        {
            if (_caseOv.TryGetValue(id, out var o)) return o;
            if (!create) return null;
            o = new CaseBalanceOverride { caseId = id };
            _root.caseOverrides.Add(o);
            _caseOv[id] = o;
            return o;
        }

        CaseDropBalanceOverride GetDropOv(string caseId, string skinId, bool create)
        {
            var k = DropKey(caseId, skinId);
            if (_dropOv.TryGetValue(k, out var o)) return o;
            if (!create) return null;
            o = new CaseDropBalanceOverride { caseId = caseId, skinId = skinId };
            _root.dropOverrides.Add(o);
            _dropOv[k] = o;
            return o;
        }

        void NormalizeSkin(string id)
        {
            var o = GetSkinOv(id, false);
            if (o == null) return;
            if (o.enabled && !o.hasVpOverride)
            {
                _root.skinOverrides.Remove(o);
                _skinOv.Remove(id);
            }
        }

        void NormalizeCase(string id)
        {
            var o = GetCaseOv(id, false);
            if (o == null) return;
            if (o.enabled && !o.hasPriceOverride)
            {
                _root.caseOverrides.Remove(o);
                _caseOv.Remove(id);
            }
        }

        void NormalizeDrop(string caseId, string skinId)
        {
            var o = GetDropOv(caseId, skinId, false);
            if (o == null) return;
            if (o.enabled && !o.hasWeightOverride)
            {
                _root.dropOverrides.Remove(o);
                _dropOv.Remove(DropKey(caseId, skinId));
            }
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawActionBar();
            DrawReportPanel();

            var newTab = (Tab)GUILayout.Toolbar((int)_tab,
                new[] { "Skin Balance", "Case Balance", "Case Drop Balance" }, GUILayout.Height(24));
            if (newTab != _tab) { _tab = newTab; DefocusIfEditing(); }
            GUILayout.Space(4);

            switch (_tab)
            {
                case Tab.Skins: DrawSkinsTab(); break;
                case Tab.Cases: DrawCasesTab(); break;
                case Tab.Drops: DrawDropsTab(); break;
            }

            DrawStatusBar();
        }

        void DrawActionBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Save Overrides", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    SaveOverrides();
                if (GUILayout.Button("Reload Overrides", EditorStyles.toolbarButton, GUILayout.Width(120)))
                    LoadOverrides();
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    Validate();
                if (GUILayout.Button("Reset All Overrides", EditorStyles.toolbarButton, GUILayout.Width(140)))
                    ResetAll();
                if (GUILayout.Button("Export Summary", EditorStyles.toolbarButton, GUILayout.Width(120)))
                    ExportSummary();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload Catalog", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    LoadCatalog();
            }
        }

        void DrawReportPanel()
        {
            if (!_showReport) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Validation Report — {_report.Count} entr{(_report.Count == 1 ? "y" : "ies")}",
                        EditorStyles.boldLabel);
                    if (GUILayout.Button("Hide", GUILayout.Width(50))) _showReport = false;
                }
                _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.Height(140));
                if (_report.Count == 0)
                    EditorGUILayout.LabelField("No issues found.");
                else
                    foreach (var line in _report)
                        EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"Overrides — skins: {_skinOv.Count}  cases: {_caseOv.Count}  drops: {_dropOv.Count}",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(_dirty ? "Unsaved changes" : "Saved",
                    _dirty ? EditorStyles.boldLabel : EditorStyles.miniLabel, GUILayout.Width(120));
            }
        }

        // ── Skins tab ─────────────────────────────────────────────────────────

        void DrawSkinsTab()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _skinSearch = SearchField(_skinSearch);
                _skinWeaponIdx = EditorGUILayout.Popup(_skinWeaponIdx, _weaponOptions, EditorStyles.toolbarPopup, GUILayout.Width(120));
                _skinRarityIdx = EditorGUILayout.Popup(_skinRarityIdx, _rarityOptions, EditorStyles.toolbarPopup, GUILayout.Width(110));
                GUILayout.Label("Sort", GUILayout.Width(30));
                _skinSortIdx = EditorGUILayout.Popup(_skinSortIdx, new[] { "Name", "VP Value" }, EditorStyles.toolbarPopup, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) { _refilterSkins = true; DefocusIfEditing(); }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{_filteredSkins.Count}/{_skins.Count}", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            if (_skins.Count == 0)
            {
                EditorGUILayout.HelpBox("No skins loaded. Ensure Resources/Config/skins.json exists, then Reload Catalog.", MessageType.Warning);
                return;
            }

            DrawSkinHeader();
            if (_refilterSkins) RefilterSkins();

            _skinScroll = EditorGUILayout.BeginScrollView(_skinScroll);
            int n = _filteredSkins.Count;
            var area = GUILayoutUtility.GetRect(0, n * RowH, GUILayout.ExpandWidth(true));
            int first = Mathf.Clamp(Mathf.FloorToInt(_skinScroll.y / RowH) - 2, 0, Mathf.Max(0, n - 1));
            int last = Mathf.Clamp(Mathf.CeilToInt((_skinScroll.y + position.height) / RowH) + 2, 0, n);
            if (first != _skinFirstVisible) { _skinFirstVisible = first; DefocusIfEditing(); }
            for (int i = first; i < last; i++)
                DrawSkinRow(new Rect(area.x, area.y + i * RowH, area.width, RowH), _filteredSkins[i]);
            EditorGUILayout.EndScrollView();
        }

        void RefilterSkins()
        {
            _refilterSkins = false;
            string weapon = _skinWeaponIdx > 0 ? _weaponOptions[_skinWeaponIdx] : null;
            SkinRarity? rar = _skinRarityIdx > 0 ? (SkinRarity?)Enum.Parse(typeof(SkinRarity), _rarityOptions[_skinRarityIdx]) : null;
            string q = _skinSearch?.Trim();

            IEnumerable<SkinRow> r = _skins;
            if (!string.IsNullOrEmpty(weapon)) r = r.Where(s => string.Equals(s.weapon, weapon, StringComparison.OrdinalIgnoreCase));
            if (rar.HasValue) r = r.Where(s => s.rarity == rar.Value);
            if (!string.IsNullOrEmpty(q))
                r = r.Where(s => (s.name != null && s.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                               || (s.skinId != null && s.skinId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            r = _skinSortIdx == 1
                ? r.OrderByDescending(s => EffectiveVp(s))
                : r.OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase);
            _filteredSkins = r.ToList();
        }

        void DrawSkinHeader()
        {
            var r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.15f));
            float x = r.xMax - 6;
            HeaderLabel(ref x, 58, "Reset");
            HeaderLabel(ref x, 60, "Enabled");
            HeaderLabel(ref x, 84, "VP Ovr");
            HeaderLabel(ref x, 70, "Default");
            GUI.Label(new Rect(r.x + 60, r.y, x - r.x - 60, 18), "Skin / Weapon / Rarity", EditorStyles.miniBoldLabel);
        }

        void DrawSkinRow(Rect r, SkinRow s)
        {
            var ov = GetSkinOv(s.skinId, false);
            bool overridden = ov != null && (ov.hasVpOverride || !ov.enabled);
            if (overridden) EditorGUI.DrawRect(new Rect(r.x, r.y + 2, 3, r.height - 6), Accent);

            float cy = r.y + (r.height - 22) * 0.5f;
            DrawThumb(new Rect(r.x + 8, r.y + (r.height - 48) * 0.5f, 48, 48), s.Icon);

            float x = r.xMax - 6;
            var resetR = NextRect(ref x, 58, cy);
            var toggleR = NextRect(ref x, 60, cy);
            var vpR = NextRect(ref x, 84, cy + 1, 18);
            var defR = NextRect(ref x, 70, cy);

            var textR = new Rect(r.x + 64, r.y + 6, x - (r.x + 64), r.height - 12);
            GUI.Label(new Rect(textR.x, textR.y, textR.width, 18), s.name, EditorStyles.boldLabel);
            GUI.Label(new Rect(textR.x, textR.y + 18, textR.width, 16),
                $"{s.skinId}", EditorStyles.miniLabel);
            GUI.Label(new Rect(textR.x, textR.y + 32, textR.width, 16),
                $"{s.weapon}  •  {s.rarityName}", EditorStyles.miniLabel);

            GUI.Label(defR, s.defaultVp.ToString(), EditorStyles.miniLabel);

            int curVp = EffectiveVp(s);
            GUI.SetNextControlName("vp:" + s.skinId);
            EditorGUI.BeginChangeCheck();
            int newVp = EditorGUI.DelayedIntField(vpR, curVp);
            if (EditorGUI.EndChangeCheck()) SetSkinVp(s, Mathf.Max(0, newVp));

            bool enabled = ov?.enabled ?? true;
            GUI.SetNextControlName("se:" + s.skinId);
            EditorGUI.BeginChangeCheck();
            bool newEnabled = EditorGUI.ToggleLeft(toggleR, "On", enabled);
            if (EditorGUI.EndChangeCheck()) { GetSkinOv(s.skinId, true).enabled = newEnabled; NormalizeSkin(s.skinId); _dirty = true; }

            using (new EditorGUI.DisabledScope(!overridden))
                if (GUI.Button(resetR, "Reset")) ResetSkin(s.skinId);

            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0, 0, 0, 0.08f));
        }

        int EffectiveVp(SkinRow s)
        {
            var ov = GetSkinOv(s.skinId, false);
            return ov != null && ov.hasVpOverride ? ov.vpValueOverride : s.defaultVp;
        }

        void SetSkinVp(SkinRow s, int vp)
        {
            var o = GetSkinOv(s.skinId, true);
            if (vp == s.defaultVp) o.hasVpOverride = false;
            else { o.hasVpOverride = true; o.vpValueOverride = vp; }
            NormalizeSkin(s.skinId);
            _dirty = true;
            if (_skinSortIdx == 1) _refilterSkins = true;
        }

        void ResetSkin(string id)
        {
            if (_skinOv.TryGetValue(id, out var o)) { _root.skinOverrides.Remove(o); _skinOv.Remove(id); _dirty = true; }
        }

        // ── Cases tab ─────────────────────────────────────────────────────────

        void DrawCasesTab()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _caseSearch = SearchField(_caseSearch);
                GUILayout.Label("Sort", GUILayout.Width(30));
                _caseSortIdx = EditorGUILayout.Popup(_caseSortIdx, new[] { "Name", "Price" }, EditorStyles.toolbarPopup, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck()) { _refilterCases = true; DefocusIfEditing(); }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{_filteredCases.Count}/{_cases.Count}", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            if (_cases.Count == 0)
            {
                EditorGUILayout.HelpBox("No cases loaded. Ensure Resources/Config/cases.json exists, then Reload Catalog.", MessageType.Warning);
                return;
            }

            if (_refilterCases) RefilterCases();

            _caseScroll = EditorGUILayout.BeginScrollView(_caseScroll);
            foreach (var c in _filteredCases)
                DrawCaseRow(c);
            EditorGUILayout.EndScrollView();
        }

        void RefilterCases()
        {
            _refilterCases = false;
            string q = _caseSearch?.Trim();
            IEnumerable<CaseRow> r = _cases;
            if (!string.IsNullOrEmpty(q))
                r = r.Where(c => (c.name != null && c.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                               || (c.caseId != null && c.caseId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            r = _caseSortIdx == 1
                ? r.OrderByDescending(EffectivePrice)
                : r.OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase);
            _filteredCases = r.ToList();
        }

        void DrawCaseRow(CaseRow c)
        {
            var ov = GetCaseOv(c.caseId, false);
            bool overridden = ov != null && (ov.hasPriceOverride || !ov.enabled);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var thumb = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                DrawThumb(thumb, c.Icon);

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(c.name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(c.caseId, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Default price: {c.defaultPrice} VP   •   {c.dropSkinIds.Count} drops", EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(150)))
                {
                    EditorGUILayout.LabelField("Price Override", EditorStyles.miniLabel);
                    int cur = EffectivePrice(c);
                    GUI.SetNextControlName("price:" + c.caseId);
                    EditorGUI.BeginChangeCheck();
                    int next = EditorGUILayout.DelayedIntField(cur);
                    if (EditorGUI.EndChangeCheck()) SetCasePrice(c, Mathf.Max(0, next));

                    bool enabled = ov?.enabled ?? true;
                    GUI.SetNextControlName("ce:" + c.caseId);
                    EditorGUI.BeginChangeCheck();
                    bool ne = EditorGUILayout.ToggleLeft("Enabled", enabled);
                    if (EditorGUI.EndChangeCheck()) { GetCaseOv(c.caseId, true).enabled = ne; NormalizeCase(c.caseId); _dirty = true; }
                }

                using (new EditorGUI.DisabledScope(!overridden))
                    if (GUILayout.Button("Reset", GUILayout.Width(60), GUILayout.Height(40)))
                        ResetCase(c.caseId);
            }
        }

        int EffectivePrice(CaseRow c)
        {
            var ov = GetCaseOv(c.caseId, false);
            return ov != null && ov.hasPriceOverride ? ov.priceOverride : c.defaultPrice;
        }

        void SetCasePrice(CaseRow c, int price)
        {
            var o = GetCaseOv(c.caseId, true);
            if (price == c.defaultPrice) o.hasPriceOverride = false;
            else { o.hasPriceOverride = true; o.priceOverride = price; }
            NormalizeCase(c.caseId);
            _dirty = true;
            if (_caseSortIdx == 1) _refilterCases = true;
        }

        void ResetCase(string id)
        {
            if (_caseOv.TryGetValue(id, out var o)) { _root.caseOverrides.Remove(o); _caseOv.Remove(id); _dirty = true; }
        }

        // ── Drops tab ─────────────────────────────────────────────────────────

        void DrawDropsTab()
        {
            if (_cases.Count == 0)
            {
                EditorGUILayout.HelpBox("No cases loaded.", MessageType.Warning);
                return;
            }
            _selectedCaseIdx = Mathf.Clamp(_selectedCaseIdx, 0, _cases.Count - 1);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Case", GUILayout.Width(34));
                EditorGUI.BeginChangeCheck();
                _selectedCaseIdx = EditorGUILayout.Popup(_selectedCaseIdx, _cases.Select(c => c.name).ToArray(),
                    EditorStyles.toolbarPopup, GUILayout.Width(220));
                _dropSearch = SearchField(_dropSearch);
                _dropWeaponIdx = EditorGUILayout.Popup(_dropWeaponIdx, _weaponOptions, EditorStyles.toolbarPopup, GUILayout.Width(120));
                _dropRarityIdx = EditorGUILayout.Popup(_dropRarityIdx, _rarityOptions, EditorStyles.toolbarPopup, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck()) DefocusIfEditing();
            }

            var c = _cases[_selectedCaseIdx];
            var rows = BuildDropRows(c, out var warnings, out float totalWeight, out var rarityShare,
                                     out var perSkinChance);

            DrawDropSummary(c, rows.Count, totalWeight, rarityShare, warnings);

            string weapon = _dropWeaponIdx > 0 ? _weaponOptions[_dropWeaponIdx] : null;
            SkinRarity? rar = _dropRarityIdx > 0 ? (SkinRarity?)Enum.Parse(typeof(SkinRarity), _rarityOptions[_dropRarityIdx]) : null;
            string q = _dropSearch?.Trim();

            var visible = rows.Where(row =>
            {
                if (row.skin == null) return true;
                if (!string.IsNullOrEmpty(weapon) && !string.Equals(row.skin.weapon, weapon, StringComparison.OrdinalIgnoreCase)) return false;
                if (rar.HasValue && row.skin.rarity != rar.Value) return false;
                if (!string.IsNullOrEmpty(q) &&
                    (row.skin.name?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (row.skinId?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) < 0) return false;
                return true;
            }).ToList();

            _dropScroll = EditorGUILayout.BeginScrollView(_dropScroll);
            int n = visible.Count;
            var area = GUILayoutUtility.GetRect(0, n * RowH, GUILayout.ExpandWidth(true));
            int first = Mathf.Clamp(Mathf.FloorToInt(_dropScroll.y / RowH) - 2, 0, Mathf.Max(0, n - 1));
            int last = Mathf.Clamp(Mathf.CeilToInt((_dropScroll.y + position.height) / RowH) + 2, 0, n);
            if (first != _dropFirstVisible) { _dropFirstVisible = first; DefocusIfEditing(); }
            for (int i = first; i < last; i++)
            {
                var row = visible[i];
                float chance = row.skin != null && perSkinChance.TryGetValue(row.skinId, out var ch) ? ch : 0f;
                DrawDropRow(new Rect(area.x, area.y + i * RowH, area.width, RowH), c, row, chance);
            }
            EditorGUILayout.EndScrollView();
        }

        sealed class DropRow
        {
            public string skinId;
            public SkinRow skin;
            public bool duplicate;
            public bool enabled;
            public float effectiveWeight;
        }

        List<DropRow> BuildDropRows(CaseRow c, out List<string> warnings, out float totalWeight,
            out Dictionary<SkinRarity, float> rarityShare, out Dictionary<string, float> perSkinChance)
        {
            warnings = new List<string>();
            var rows = new List<DropRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (c.dropSkinIds == null || c.dropSkinIds.Count == 0)
                warnings.Add("This case has no drop pool.");

            foreach (var id in c.dropSkinIds)
            {
                _skinById.TryGetValue(id, out var skin);
                bool dup = !seen.Add(id);
                bool skinEnabled = SkinEnabled(id);
                bool dropEnabled = DropEnabled(c.caseId, id);
                bool enabled = skin != null && skinEnabled && dropEnabled;
                float w = enabled ? EffectiveDropWeight(c.caseId, id) : 0f;
                rows.Add(new DropRow { skinId = id, skin = skin, duplicate = dup, enabled = enabled, effectiveWeight = w });
            }

            var enabledRows = rows.Where(r => r.enabled && r.skin != null).ToList();
            var raritiesPresent = enabledRows.Select(r => r.skin.rarity).Distinct().ToList();
            float rarityWeightSum = raritiesPresent.Sum(rr => c.rarityWeights.TryGetValue(rr, out var w) ? w : 0f);

            rarityShare = new Dictionary<SkinRarity, float>();
            foreach (var rr in raritiesPresent)
            {
                float rw = c.rarityWeights.TryGetValue(rr, out var w) ? w : 0f;
                rarityShare[rr] = rarityWeightSum > 0f ? rw / rarityWeightSum : (raritiesPresent.Count > 0 ? 1f / raritiesPresent.Count : 0f);
            }

            perSkinChance = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var rr in raritiesPresent)
            {
                var inRarity = enabledRows.Where(r => r.skin.rarity == rr).ToList();
                float wsum = inRarity.Sum(r => r.effectiveWeight);
                foreach (var r in inRarity)
                {
                    float within = wsum > 0f ? r.effectiveWeight / wsum : (inRarity.Count > 0 ? 1f / inRarity.Count : 0f);
                    perSkinChance[r.skinId] = (rarityShare.TryGetValue(rr, out var rs) ? rs : 0f) * within * 100f;
                }
            }

            totalWeight = enabledRows.Sum(r => r.effectiveWeight);

            if (enabledRows.Count == 0) warnings.Add("No enabled skins in this case.");
            if (totalWeight <= 0f && enabledRows.Count > 0) warnings.Add("Total weight is 0.");
            if (rows.Any(r => r.duplicate)) warnings.Add("Duplicate skin entries in the drop pool.");
            foreach (var r in rows.Where(r => r.skin == null))
                warnings.Add($"Missing skin: '{r.skinId}' is not in the catalog.");

            return rows;
        }

        void DrawDropSummary(CaseRow c, int poolCount, float totalWeight,
            Dictionary<SkinRarity, float> rarityShare, List<string> warnings)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{c.name}  ({c.caseId})", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Pool size: {poolCount}    Total enabled weight: {totalWeight:0.##}", EditorStyles.miniLabel);

                if (rarityShare.Count > 0)
                {
                    var sb = new StringBuilder("Rarity distribution: ");
                    foreach (var kv in rarityShare.OrderBy(k => (int)k.Key))
                        sb.Append($"{kv.Key} {kv.Value * 100f:0.#}%   ");
                    EditorGUILayout.LabelField(sb.ToString().TrimEnd(), EditorStyles.miniLabel);
                }

                foreach (var w in warnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }
        }

        void DrawDropRow(Rect r, CaseRow c, DropRow row, float chance)
        {
            var ov = GetDropOv(c.caseId, row.skinId, false);
            bool overridden = ov != null && (ov.hasWeightOverride || !ov.enabled);
            if (overridden) EditorGUI.DrawRect(new Rect(r.x, r.y + 2, 3, r.height - 6), Accent);

            float cy = r.y + (r.height - 22) * 0.5f;

            if (row.skin == null)
            {
                DrawThumb(new Rect(r.x + 8, r.y + (r.height - 48) * 0.5f, 48, 48), null);
                GUI.Label(new Rect(r.x + 64, cy, r.width - 200, 22), $"MISSING: {row.skinId}", EditorStyles.boldLabel);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0, 0, 0, 0.08f));
                return;
            }

            DrawThumb(new Rect(r.x + 8, r.y + (r.height - 48) * 0.5f, 48, 48), row.skin.Icon);

            float x = r.xMax - 6;
            var resetR = NextRect(ref x, 58, cy);
            var toggleR = NextRect(ref x, 60, cy);
            var weightR = NextRect(ref x, 84, cy + 1, 18);
            var chanceR = NextRect(ref x, 70, cy);

            var textR = new Rect(r.x + 64, r.y + 6, x - (r.x + 64), r.height - 12);
            GUI.Label(new Rect(textR.x, textR.y, textR.width, 18),
                row.duplicate ? row.skin.name + "  (dup)" : row.skin.name, EditorStyles.boldLabel);
            GUI.Label(new Rect(textR.x, textR.y + 18, textR.width, 16),
                $"{row.skin.weapon}  •  {row.skin.rarityName}  •  {EffectiveVp(row.skin)} VP", EditorStyles.miniLabel);

            GUI.Label(chanceR, $"{chance:0.##}%", EditorStyles.miniLabel);

            float curW = EffectiveDropWeight(c.caseId, row.skinId);
            GUI.SetNextControlName("dw:" + c.caseId + "|" + row.skinId);
            EditorGUI.BeginChangeCheck();
            float newW = EditorGUI.DelayedFloatField(weightR, curW);
            if (EditorGUI.EndChangeCheck()) SetDropWeight(c.caseId, row.skinId, Mathf.Max(0f, newW));

            bool enabled = ov?.enabled ?? true;
            GUI.SetNextControlName("de:" + c.caseId + "|" + row.skinId);
            EditorGUI.BeginChangeCheck();
            bool ne = EditorGUI.ToggleLeft(toggleR, "On", enabled);
            if (EditorGUI.EndChangeCheck()) { GetDropOv(c.caseId, row.skinId, true).enabled = ne; NormalizeDrop(c.caseId, row.skinId); _dirty = true; }

            using (new EditorGUI.DisabledScope(!overridden))
                if (GUI.Button(resetR, "Reset")) ResetDrop(c.caseId, row.skinId);

            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0, 0, 0, 0.08f));
        }

        bool SkinEnabled(string id) => !_skinOv.TryGetValue(id, out var o) || o.enabled;
        bool DropEnabled(string caseId, string skinId) => !_dropOv.TryGetValue(DropKey(caseId, skinId), out var o) || o.enabled;

        float EffectiveDropWeight(string caseId, string skinId)
        {
            var ov = GetDropOv(caseId, skinId, false);
            if (ov != null && ov.hasWeightOverride) return ov.weightOverride;
            return 1f;
        }

        void SetDropWeight(string caseId, string skinId, float w)
        {
            var o = GetDropOv(caseId, skinId, true);
            if (w <= 0f || Mathf.Approximately(w, 1f)) o.hasWeightOverride = false;
            else { o.hasWeightOverride = true; o.weightOverride = w; }
            NormalizeDrop(caseId, skinId);
            _dirty = true;
        }

        void ResetDrop(string caseId, string skinId)
        {
            var k = DropKey(caseId, skinId);
            if (_dropOv.TryGetValue(k, out var o)) { _root.dropOverrides.Remove(o); _dropOv.Remove(k); _dirty = true; }
        }

        // ── Actions ───────────────────────────────────────────────────────────

        void SaveOverrides()
        {
            PruneNoOps();
            var dir = Path.GetDirectoryName(BalanceOverrideService.AssetPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(BalanceOverrideService.AssetPath, BalanceOverrideService.Serialize(_root));
            AssetDatabase.Refresh();
            BalanceOverrideService.Reload();
            _dirty = false;
            ShowNotification(new GUIContent("Overrides saved"));
        }

        void PruneNoOps()
        {
            _root.skinOverrides.RemoveAll(o => o == null || (o.enabled && !o.hasVpOverride));
            _root.caseOverrides.RemoveAll(o => o == null || (o.enabled && !o.hasPriceOverride));
            _root.dropOverrides.RemoveAll(o => o == null || (o.enabled && !o.hasWeightOverride));
            RebuildOverrideMaps();
        }

        void ResetAll()
        {
            if (!EditorUtility.DisplayDialog("Reset All Overrides",
                "Delete every balance override and restore the original catalog values?\nThis removes balance_overrides.json.",
                "Reset All", "Cancel"))
                return;

            _root = new BalanceOverrideRoot();
            RebuildOverrideMaps();
            if (File.Exists(BalanceOverrideService.AssetPath))
            {
                AssetDatabase.DeleteAsset(BalanceOverrideService.AssetPath);
                AssetDatabase.Refresh();
            }
            BalanceOverrideService.Reload();
            _dirty = false;
            _report.Clear();
            ShowNotification(new GUIContent("All overrides reset"));
        }

        void Validate()
        {
            PruneNoOps();
            _report = BuildValidationReport();
            _showReport = true;
            Debug.Log($"[BalanceEditor] Validation: {_report.Count} issue(s).");
            foreach (var line in _report) Debug.LogWarning("[BalanceEditor] " + line);
        }

        List<string> BuildValidationReport()
        {
            var report = new List<string>();
            var caseById = _cases.ToDictionary(c => c.caseId, c => c, StringComparer.Ordinal);

            foreach (var o in _root.skinOverrides)
            {
                if (string.IsNullOrEmpty(o.skinId)) { report.Add("Skin override with missing Skin ID."); continue; }
                if (!_skinById.ContainsKey(o.skinId)) report.Add($"Unknown Skin Reference: '{o.skinId}'.");
                if (o.hasVpOverride && o.vpValueOverride < 0) report.Add($"Negative VP value on skin '{o.skinId}'.");
            }

            foreach (var o in _root.caseOverrides)
            {
                if (string.IsNullOrEmpty(o.caseId)) { report.Add("Case override with missing Case ID."); continue; }
                if (!caseById.ContainsKey(o.caseId)) report.Add($"Unknown Case Reference: '{o.caseId}'.");
                if (o.hasPriceOverride && o.priceOverride < 0) report.Add($"Negative price on case '{o.caseId}'.");
            }

            foreach (var o in _root.dropOverrides)
            {
                if (string.IsNullOrEmpty(o.caseId)) report.Add("Drop override with missing Case ID.");
                else if (!caseById.ContainsKey(o.caseId)) report.Add($"Unknown Case Reference in drop override: '{o.caseId}'.");
                if (string.IsNullOrEmpty(o.skinId)) report.Add("Drop override with missing Skin ID.");
                else if (!_skinById.ContainsKey(o.skinId)) report.Add($"Unknown Skin Reference in drop override: '{o.skinId}'.");
                if (o.hasWeightOverride && o.weightOverride < 0f) report.Add($"Negative weight on '{o.caseId}' / '{o.skinId}'.");
            }

            foreach (var c in _cases)
            {
                if (!IsCaseEnabledEditor(c.caseId)) continue;
                var enabledDrops = c.dropSkinIds.Where(id => _skinById.ContainsKey(id) && SkinEnabled(id) && DropEnabled(c.caseId, id)).ToList();
                if (enabledDrops.Count == 0)
                {
                    report.Add($"Enabled case '{c.caseId}' has no enabled drops.");
                    continue;
                }
                float total = enabledDrops.Sum(id => EffectiveDropWeight(c.caseId, id));
                if (total <= 0f) report.Add($"Enabled case '{c.caseId}' has total weight 0.");
            }

            return report;
        }

        bool IsCaseEnabledEditor(string id) => !_caseOv.TryGetValue(id, out var o) || o.enabled;

        void ExportSummary()
        {
            var path = EditorUtility.SaveFilePanel("Export Balance Summary", Application.dataPath, "balance_summary", "txt");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("ValoCase Balance Override Summary");
            sb.AppendLine($"Generated: {DateTime.Now:u}");
            sb.AppendLine();
            sb.AppendLine($"Skin overrides: {_skinOv.Count}");
            foreach (var o in _root.skinOverrides)
                sb.AppendLine($"  {o.skinId}: vp={(o.hasVpOverride ? o.vpValueOverride.ToString() : "-")} enabled={o.enabled}");
            sb.AppendLine();
            sb.AppendLine($"Case overrides: {_caseOv.Count}");
            foreach (var o in _root.caseOverrides)
                sb.AppendLine($"  {o.caseId}: price={(o.hasPriceOverride ? o.priceOverride.ToString() : "-")} enabled={o.enabled}");
            sb.AppendLine();
            sb.AppendLine($"Drop overrides: {_dropOv.Count}");
            foreach (var o in _root.dropOverrides)
                sb.AppendLine($"  {o.caseId} / {o.skinId}: weight={(o.hasWeightOverride ? o.weightOverride.ToString("0.##") : "-")} enabled={o.enabled}");

            var report = BuildValidationReport();
            sb.AppendLine();
            sb.AppendLine($"Validation issues: {report.Count}");
            foreach (var line in report) sb.AppendLine("  " + line);

            File.WriteAllText(path, sb.ToString());
            ShowNotification(new GUIContent("Summary exported"));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static string SearchField(string value)
        {
            var style = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField;
            return GUILayout.TextField(value ?? "", style, GUILayout.MinWidth(140));
        }

        static Rect NextRect(ref float xRight, float w, float y, float h = 20f)
        {
            var r = new Rect(xRight - w, y, w, h);
            xRight -= w + 6;
            return r;
        }

        static void HeaderLabel(ref float xRight, float w, string text)
        {
            var r = new Rect(xRight - w, 0, w, 18);
            GUI.Label(r, text, EditorStyles.miniBoldLabel);
            xRight -= w + 6;
        }

        static void DrawThumb(Rect r, Sprite s)
        {
            if (s != null && s.texture != null)
            {
                var t = s.texture;
                var tr = s.textureRect;
                var uv = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
                GUI.DrawTextureWithTexCoords(r, t, uv, true);
            }
            else
            {
                EditorGUI.DrawRect(r, Missing);
                var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                GUI.Label(r, "Missing\nIcon", st);
            }
        }
    }
}
