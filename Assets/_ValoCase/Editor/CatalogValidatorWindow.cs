using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.EditorTools
{
    public sealed class CatalogValidatorWindow : EditorWindow
    {
        const string SkinsPath = "Assets/_ValoCase/Resources/Config/skins.json";
        const string CasesPath = "Assets/_ValoCase/Resources/Config/cases.json";
        const string CaseArtFolder = "Assets/_ValoCase/Resources/Art/Cases";

        const string BackendDir = @"C:\Users\cenk_\Desktop\valocase-backend";
        const string BackendScript = "tools/catalog/generate_catalog_migration.py";

        static readonly string[] RarityOrder = { "Select", "Deluxe", "Premium", "Exclusive", "Ultra" };

        enum Sev { Error, Warning, Info }

        struct Result
        {
            public Sev sev;
            public string msg;
            public string pingPath;
        }

        readonly List<Result> _results = new();
        int _skinCount, _caseCount, _errorCount, _warnCount;
        bool _hasRun;
        Vector2 _scroll;

        string _backendStatus = "";
        string _backendOutput = "";
        bool _backendOk;
        Vector2 _backendScroll;

        [MenuItem("ValoCase/Catalog/Catalog Validator")]
        static void Open()
        {
            var w = GetWindow<CatalogValidatorWindow>("Catalog Validator");
            w.minSize = new Vector2(720, 520);
            w.Show();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Validate Catalog", EditorStyles.toolbarButton, GUILayout.Width(120))) Validate();
                using (new EditorGUI.DisabledScope(!_hasRun))
                    if (GUILayout.Button("Copy Report", EditorStyles.toolbarButton, GUILayout.Width(90)))
                        EditorGUIUtility.systemCopyBuffer = BuildReport();
                if (GUILayout.Button("Open cases.json", EditorStyles.toolbarButton, GUILayout.Width(110))) OpenAsset(CasesPath);
                if (GUILayout.Button("Open skins.json", EditorStyles.toolbarButton, GUILayout.Width(110))) OpenAsset(SkinsPath);
                GUILayout.FlexibleSpace();
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Backend Sync:", EditorStyles.miniLabel, GUILayout.Width(82));
                if (GUILayout.Button("Backend Dry-Run", EditorStyles.toolbarButton, GUILayout.Width(120))) RunBackend(true);
                if (GUILayout.Button("Generate Backend Migration", EditorStyles.toolbarButton, GUILayout.Width(190))) RunBackend(false);
                GUILayout.FlexibleSpace();
            }

            DrawBackendOutput();

            if (!_hasRun)
            {
                EditorGUILayout.HelpBox("Click 'Validate Catalog' to check skins.json and cases.json. This never modifies files.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Skins: {_skinCount}    Cases: {_caseCount}    Errors: {_errorCount}    Warnings: {_warnCount}");
                EditorGUILayout.LabelField(_errorCount == 0 ? "RESULT: PASS (errors block backend sync)" : "RESULT: FAIL — errors must be fixed before backend sync",
                    _errorCount == 0 ? EditorStyles.boldLabel : EditorStyles.boldLabel);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGroup("Errors", Sev.Error);
            DrawGroup("Warnings", Sev.Warning);
            DrawGroup("Info", Sev.Info);
            EditorGUILayout.EndScrollView();
        }

        void DrawGroup(string title, Sev sev)
        {
            var items = _results.Where(r => r.sev == sev).ToList();
            if (items.Count == 0) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"{title} ({items.Count})", EditorStyles.boldLabel);
            var msgType = sev == Sev.Error ? MessageType.Error : sev == Sev.Warning ? MessageType.Warning : MessageType.Info;
            foreach (var r in items)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(r.msg, msgType);
                    if (!string.IsNullOrEmpty(r.pingPath))
                        if (GUILayout.Button("Ping", GUILayout.Width(48), GUILayout.Height(38)))
                            Ping(r.pingPath);
                }
            }
        }

        // ── Backend sync ────────────────────────────────────────────────────────

        void DrawBackendOutput()
        {
            if (string.IsNullOrEmpty(_backendStatus)) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Backend Sync", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_backendStatus, EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(_backendOutput))
                {
                    _backendScroll = EditorGUILayout.BeginScrollView(_backendScroll, GUILayout.Height(160));
                    EditorGUILayout.TextArea(_backendOutput, GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        void RunBackend(bool dryRun)
        {
            Validate();
            if (_errorCount > 0)
            {
                _backendOk = false;
                _backendOutput = "";
                _backendStatus = $"BLOCKED — {_errorCount} validation error(s). Fix errors before backend sync.";
                Debug.LogError($"[CatalogValidator] Backend sync blocked: {_errorCount} validation error(s).");
                return;
            }

            var args = BackendScript + (dryRun ? " --dry-run" : "");
            _backendStatus = $"Running: python {args} (cwd {BackendDir}) ...";
            _backendOutput = "";
            Repaint();

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = args,
                    WorkingDirectory = BackendDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var p = System.Diagnostics.Process.Start(psi);
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                int code = p.ExitCode;

                _backendOutput = (stdout ?? "") + (string.IsNullOrEmpty(stderr) ? "" : "\n[stderr]\n" + stderr);
                _backendOk = code == 0;
                _backendStatus = $"{(_backendOk ? "SUCCESS" : "FAILED")} (exit {code}) — python {args}";
                if (_backendOk) Debug.Log($"[CatalogValidator] Backend command SUCCESS (exit 0): python {args}\n{_backendOutput}");
                else Debug.LogError($"[CatalogValidator] Backend command FAILED (exit {code}): python {args}\n{_backendOutput}");
            }
            catch (Exception e)
            {
                _backendOk = false;
                _backendStatus = "ERROR launching python — " + e.Message;
                _backendOutput = e.ToString();
                Debug.LogError("[CatalogValidator] Could not launch python: " + e);
            }
            Repaint();
        }

        // ── Validation (never writes) ───────────────────────────────────────────

        void Validate()
        {
            _results.Clear();
            _skinCount = _caseCount = 0;
            _hasRun = true;

            var skinsAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SkinsPath);
            var casesAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CasesPath);

            SkinCatalogRoot skinRoot = null;
            CaseCatalogRoot caseRoot = null;

            if (skinsAsset == null) Add(Sev.Error, $"skins.json not found at {SkinsPath}", SkinsPath);
            else { skinRoot = TryParse<SkinCatalogRoot>(skinsAsset.text); if (skinRoot == null) Add(Sev.Error, "skins.json failed to parse.", SkinsPath); else Add(Sev.Info, "skins.json parsed OK."); }

            if (casesAsset == null) Add(Sev.Error, $"cases.json not found at {CasesPath}", CasesPath);
            else { caseRoot = TryParse<CaseCatalogRoot>(casesAsset.text); if (caseRoot == null) Add(Sev.Error, "cases.json failed to parse.", CasesPath); else Add(Sev.Info, "cases.json parsed OK."); }

            var skinById = new Dictionary<string, SkinCatalogEntry>(StringComparer.Ordinal);
            bool skinsOk = skinRoot?.skins != null;

            if (skinsOk)
            {
                _skinCount = skinRoot.skins.Count;
                ValidateSkins(skinRoot.skins, skinById);
            }
            if (caseRoot?.cases != null)
            {
                _caseCount = caseRoot.cases.Count;
                ValidateCases(caseRoot.cases, skinById, skinsOk);
            }

            _errorCount = _results.Count(r => r.sev == Sev.Error);
            _warnCount = _results.Count(r => r.sev == Sev.Warning);
            Add(Sev.Info, $"Validation complete — {_errorCount} error(s), {_warnCount} warning(s).");
        }

        void ValidateSkins(List<SkinCatalogEntry> skins, Dictionary<string, SkinCatalogEntry> skinById)
        {
            foreach (var s in skins)
            {
                if (s == null) { Add(Sev.Error, "skins.json contains a null skin entry."); continue; }
                if (string.IsNullOrEmpty(s.skinId)) { Add(Sev.Error, "A skin is missing 'skinId'."); continue; }

                if (!skinById.ContainsKey(s.skinId)) skinById[s.skinId] = s;
                else Add(Sev.Error, $"Duplicate skinId '{s.skinId}'.");

                if (string.IsNullOrEmpty(s.displayName)) Add(Sev.Error, $"Skin '{s.skinId}' missing displayName.");
                if (string.IsNullOrEmpty(s.weapon)) Add(Sev.Error, $"Skin '{s.skinId}' missing weapon.");
                if (string.IsNullOrEmpty(s.rarity)) Add(Sev.Error, $"Skin '{s.skinId}' missing rarity.");
                if (string.IsNullOrEmpty(s.resourceKey)) Add(Sev.Error, $"Skin '{s.skinId}' missing resourceKey.");
                else if (Resources.Load<Sprite>(s.resourceKey) == null)
                    Add(Sev.Error, $"Skin '{s.skinId}' resourceKey '{s.resourceKey}' does not resolve to a PNG.", ResourcePingPath(s.resourceKey));

                if (s.vpValue <= 0) Add(Sev.Warning, $"Skin '{s.skinId}' has vpValue {s.vpValue} (<= 0).");
            }
        }

        void ValidateCases(List<CaseCatalogEntry> cases, Dictionary<string, SkinCatalogEntry> skinById, bool skinsOk)
        {
            var seenCaseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cases)
            {
                if (c == null) { Add(Sev.Error, "cases.json contains a null case entry."); continue; }
                if (string.IsNullOrEmpty(c.caseId)) { Add(Sev.Error, "A case is missing 'caseId'."); continue; }
                if (!seenCaseIds.Add(c.caseId)) Add(Sev.Error, $"Duplicate caseId '{c.caseId}'.");

                if (string.IsNullOrEmpty(c.displayName)) Add(Sev.Error, $"Case '{c.caseId}' missing displayName.");
                if (string.IsNullOrEmpty(c.resourceKey)) Add(Sev.Error, $"Case '{c.caseId}' missing resourceKey.");
                else if (Resources.Load<Sprite>(c.resourceKey) == null)
                    Add(Sev.Error, $"Case '{c.caseId}' resourceKey '{c.resourceKey}' does not resolve to a PNG.", ResourcePingPath(c.resourceKey));
                if (c.rarityWeights == null) Add(Sev.Error, $"Case '{c.caseId}' missing rarityWeights.");
                if (c.manualDropPool == null) Add(Sev.Error, $"Case '{c.caseId}' missing manualDropPool.");
                if (c.price <= 0) Add(Sev.Warning, $"Case '{c.caseId}' has price {c.price} (<= 0).");

                var pool = c.manualDropPool ?? Array.Empty<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in pool)
                {
                    if (!seen.Add(id)) Add(Sev.Error, $"Case '{c.caseId}' has duplicate skinId '{id}' in its pool.");
                    if (skinsOk && !skinById.ContainsKey(id))
                        Add(Sev.Error, $"Case '{c.caseId}' references skinId '{id}' that does not exist in skins.json.");
                    else if (c.enabled && skinById.TryGetValue(id, out var s) && !s.enabled)
                        Add(Sev.Error, $"Enabled case '{c.caseId}' references disabled skin '{id}'.");
                }

                if (c.enabled && pool.Length == 0)
                    Add(Sev.Error, $"Enabled case '{c.caseId}' has an empty manualDropPool.");

                ValidateWeights(c, skinById, skinsOk);
            }
        }

        void ValidateWeights(CaseCatalogEntry c, Dictionary<string, SkinCatalogEntry> skinById, bool skinsOk)
        {
            if (c.rarityWeights == null) return;
            float total = c.rarityWeights.Where(w => w != null && RarityOrder.Contains(w.rarity)).Sum(w => w.weight);
            if (Mathf.Abs(total - 100f) > 0.01f)
                Add(Sev.Warning, $"Case '{c.caseId}' rarity weight total is {total:0.##}, not 100.");

            if (!skinsOk) return;
            var poolRarities = (c.manualDropPool ?? Array.Empty<string>())
                .Where(skinById.ContainsKey).Select(id => skinById[id].rarity).ToHashSet();
            foreach (var w in c.rarityWeights)
            {
                if (w == null || w.weight <= 0f || !RarityOrder.Contains(w.rarity)) continue;
                if (!poolRarities.Contains(w.rarity))
                    Add(Sev.Warning, $"Case '{c.caseId}' weights '{w.rarity}' at {w.weight:0.##} but no '{w.rarity}' skin is in its pool.");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        void Add(Sev sev, string msg, string pingPath = null) =>
            _results.Add(new Result { sev = sev, msg = msg, pingPath = pingPath });

        static T TryParse<T>(string text) where T : class
        {
            try { return JsonUtility.FromJson<T>(text); }
            catch { return null; }
        }

        static string ResourcePingPath(string resourceKey)
        {
            var asset = "Assets/_ValoCase/Resources/" + resourceKey + ".png";
            return System.IO.File.Exists(asset) ? asset : System.IO.Path.GetDirectoryName(asset).Replace('\\', '/');
        }

        static void Ping(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj == null) obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CaseArtFolder);
            if (obj != null) { EditorGUIUtility.PingObject(obj); Selection.activeObject = obj; }
            else Debug.LogWarning("[CatalogValidator] Could not locate to ping: " + path);
        }

        static void OpenAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null) AssetDatabase.OpenAsset(asset);
            else Debug.LogWarning("[CatalogValidator] Not found: " + path);
        }

        string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ValoCase Catalog Validation Report");
            sb.AppendLine($"Generated: {DateTime.Now:u}");
            sb.AppendLine($"Skins: {_skinCount}  Cases: {_caseCount}  Errors: {_errorCount}  Warnings: {_warnCount}");
            sb.AppendLine($"Result: {(_errorCount == 0 ? "PASS" : "FAIL")}");
            sb.AppendLine();
            foreach (var sev in new[] { Sev.Error, Sev.Warning, Sev.Info })
            {
                var items = _results.Where(r => r.sev == sev).ToList();
                if (items.Count == 0) continue;
                sb.AppendLine($"== {sev} ({items.Count}) ==");
                foreach (var r in items) sb.AppendLine("  " + r.msg);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
