using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ValoCase.Data;

namespace ValoCase.EditorTools.Economy
{
    /// <summary>
    /// Read-only economy analysis tool. Parses the real project catalog
    /// (cases.json + skins.json) and computes per-case expected value, sink
    /// ratios, structural risks, and recommended price ranges.
    ///
    /// This window never writes files. It is intentionally analysis-only.
    /// </summary>
    public sealed class EconomyBalanceEditorWindow : EditorWindow
    {
        // ── Data source paths (project-relative) ─────────────────────────────
        const string CasesPath = "Assets/_ValoCase/Resources/Config/cases.json";
        const string SkinsPath = "Assets/_ValoCase/Resources/Config/skins.json";

        // ── Fallback economy constants ───────────────────────────────────────
        const float FallbackSellMultiplier  = 0.35f;
        const float FallbackDuplicateBonus   = 0.15f;

        static readonly string[] RarityOrder = { "Select", "Deluxe", "Premium", "Exclusive", "Ultra" };

        enum Severity { Info, Warning, Critical }

        sealed class Issue
        {
            public Severity Sev;
            public string Code;
            public string Message;
            public Issue(Severity sev, string code, string message) { Sev = sev; Code = code; Message = message; }
        }

        sealed class TierTarget
        {
            public float SellLow, SellHigh, KeepLow, KeepHigh, PreferredFactor;
            public TierTarget(float sl, float sh, float kl, float kh, float pf)
            { SellLow = sl; SellHigh = sh; KeepLow = kl; KeepHigh = kh; PreferredFactor = pf; }
        }

        // Strictness targets per tier (sell-EV/price and keep-EV/price windows).
        static readonly Dictionary<string, TierTarget> Targets = new()
        {
            { "basic",    new TierTarget(0.25f, 0.35f, 0.75f, 0.90f, 0.82f) },
            { "protocol", new TierTarget(0.23f, 0.32f, 0.65f, 0.82f, 0.74f) },
            { "radiant",  new TierTarget(0.20f, 0.30f, 0.55f, 0.78f, 0.66f) },
            { "arcane",   new TierTarget(0.16f, 0.28f, 0.45f, 0.75f, 0.58f) },
        };

        sealed class CategoryAnalysis
        {
            public string Category;
            public int Total;
            public readonly Dictionary<string, int>   RarityCounts = new();
            public readonly Dictionary<string, int>   RarityMin    = new();
            public readonly Dictionary<string, int>   RarityMax    = new();
            public readonly Dictionary<string, float> RarityAvg    = new();
            public int MinVp, MaxVp;
            public float AvgVp;
            public bool UltraExists;
            public readonly List<string> Notes  = new();
            public readonly List<Issue>  Issues = new();
        }

        sealed class CaseAnalysis
        {
            public string CaseId;
            public string DisplayName;
            public string Category;
            public string Tier;
            public int    Price;
            public int    PoolCount;

            public readonly Dictionary<string, int>   PoolRarityCounts = new();
            public readonly Dictionary<string, float> PoolRarityAvg    = new();
            public readonly Dictionary<string, float> Weights          = new();

            public float KeepEV, SellEV, DupEV;
            public float KeepRatio, SellRatio, DupRatio;
            public int   HighestValue, LowestValue;

            public readonly List<Issue> Issues = new();

            // Recommendation outputs.
            public int    RecMinPrice, RecMaxPrice, PreferredPrice;
            public string PriceStatus;
            public string SuggestedAction;
        }

        // ── State ────────────────────────────────────────────────────────────
        Dictionary<string, SkinCatalogEntry> _skinById;
        List<CaseAnalysis>     _cases;
        List<CategoryAnalysis> _categories;
        string[]               _knownCategories = Array.Empty<string>();

        float  _sellMul = FallbackSellMultiplier;
        float  _dupFrac = FallbackDuplicateBonus;
        string _configSource = "fallback";
        string _loadError;

        Vector2 _scroll;
        bool _catSectionOpen = true;
        readonly Dictionary<string, bool> _caseFoldouts = new();
        readonly Dictionary<string, bool> _catFoldouts  = new();

        [MenuItem("Tools/ValoCase/Economy Balance Editor")]
        static void Open()
        {
            var w = GetWindow<EconomyBalanceEditorWindow>("Economy Balance");
            w.minSize = new Vector2(560f, 480f);
            w.Analyze();
        }

        void OnEnable()
        {
            if (_cases == null) Analyze();
        }

        // ── Analysis pipeline ────────────────────────────────────────────────

        void Analyze()
        {
            _loadError = null;
            _cases = new List<CaseAnalysis>();
            _categories = new List<CategoryAnalysis>();

            LoadEconomyConfig();

            if (!TryReadJson(SkinsPath, out var skinsText, out _loadError)) return;
            if (!TryReadJson(CasesPath, out var casesText, out _loadError)) return;

            SkinCatalogRoot skinRoot;
            CaseCatalogRoot caseRoot;
            try
            {
                skinRoot = JsonUtility.FromJson<SkinCatalogRoot>(skinsText);
                caseRoot = JsonUtility.FromJson<CaseCatalogRoot>(casesText);
            }
            catch (Exception e)
            {
                _loadError = "JSON parse failed: " + e.Message;
                return;
            }

            if (skinRoot?.skins == null || caseRoot?.cases == null)
            {
                _loadError = "Parsed catalog was empty or malformed.";
                return;
            }

            _skinById = new Dictionary<string, SkinCatalogEntry>();
            foreach (var s in skinRoot.skins)
            {
                if (s == null || string.IsNullOrEmpty(s.skinId)) continue;
                _skinById[s.skinId] = s; // last wins; duplicate authoring is a separate concern
            }

            _knownCategories = skinRoot.skins
                .Where(s => s != null && !string.IsNullOrEmpty(s.weapon))
                .Select(s => s.weapon)
                .Distinct()
                .OrderBy(w => w)
                .ToArray();

            BuildCategoryAnalysis(skinRoot);

            foreach (var c in caseRoot.cases)
            {
                if (c == null) continue;
                _cases.Add(AnalyzeCase(c));
            }

            CheckMeleeBandSpread();
        }

        void LoadEconomyConfig()
        {
            _sellMul = FallbackSellMultiplier;
            _dupFrac = FallbackDuplicateBonus;
            _configSource = "fallback (sell 0.35, duplicate 0.15)";

            try
            {
                var cfg = Resources.Load<GameConfigSO>("GameConfig");
                if (cfg != null)
                {
                    _sellMul = cfg.SellMultiplier > 0f ? cfg.SellMultiplier : FallbackSellMultiplier;
                    // GameConfigSO stores the duplicate bonus as an integer percent.
                    _dupFrac = cfg.DuplicateBonusPercent > 0 ? cfg.DuplicateBonusPercent / 100f : FallbackDuplicateBonus;
                    _configSource = $"GameConfig.asset (sell {_sellMul:0.###}, duplicate {_dupFrac:0.###})";
                }
            }
            catch (Exception e)
            {
                _configSource = "fallback (GameConfig load failed: " + e.Message + ")";
            }
        }

        static bool TryReadJson(string assetPath, out string text, out string error)
        {
            text = null;
            error = null;
            try
            {
                var full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
                if (!File.Exists(full))
                {
                    error = "File not found: " + assetPath;
                    return false;
                }
                text = File.ReadAllText(full);
                return true;
            }
            catch (Exception e)
            {
                error = "Read failed for " + assetPath + ": " + e.Message;
                return false;
            }
        }

        void BuildCategoryAnalysis(SkinCatalogRoot root)
        {
            foreach (var cat in _knownCategories)
            {
                var rows = root.skins.Where(s => s != null && s.weapon == cat && s.enabled).ToList();
                if (rows.Count == 0) continue;

                var ca = new CategoryAnalysis { Category = cat, Total = rows.Count };
                ca.MinVp = rows.Min(s => s.vpValue);
                ca.MaxVp = rows.Max(s => s.vpValue);
                ca.AvgVp = (float)rows.Average(s => s.vpValue);

                foreach (var r in RarityOrder)
                {
                    var rr = rows.Where(s => RarityEq(s.rarity, r)).ToList();
                    ca.RarityCounts[r] = rr.Count;
                    if (rr.Count > 0)
                    {
                        ca.RarityMin[r] = rr.Min(s => s.vpValue);
                        ca.RarityMax[r] = rr.Max(s => s.vpValue);
                        ca.RarityAvg[r] = (float)rr.Average(s => s.vpValue);
                    }
                }

                ca.UltraExists = ca.RarityCounts.TryGetValue("Ultra", out var u) && u > 0;

                int presentRarities = RarityOrder.Count(r => ca.RarityCounts.TryGetValue(r, out var n) && n > 0);
                int topCount = (ca.RarityCounts.GetValueOrDefault("Exclusive")) + (ca.RarityCounts.GetValueOrDefault("Ultra"));

                if (presentRarities == 1)
                    ca.Notes.Add("Single-rarity category — analyze by VP value bands, not rarity mix.");
                if (!ca.UltraExists)
                    ca.Notes.Add("No Ultra skins — do not assign Ultra weight to this category's cases.");
                if (topCount > 0 && topCount < 8)
                    ca.Notes.Add($"Thin top pool — only {topCount} Exclusive+Ultra skins available to share across high-tier cases.");

                _categories.Add(ca);
            }
        }

        CaseAnalysis AnalyzeCase(CaseCatalogEntry c)
        {
            var a = new CaseAnalysis
            {
                CaseId      = c.caseId ?? "(no id)",
                DisplayName = string.IsNullOrEmpty(c.displayName) ? c.caseId : c.displayName,
                Price       = c.price,
                Tier        = InferTier(c.caseId),
            };

            // Weights map (rarity → weight).
            if (c.rarityWeights != null)
                foreach (var w in c.rarityWeights)
                    if (w != null && !string.IsNullOrEmpty(w.rarity))
                        a.Weights[CanonRarity(w.rarity)] = w.weight;

            var pool = c.manualDropPool ?? Array.Empty<string>();
            a.PoolCount = pool.Length;

            // Resolve pool skins, tracking missing ids and duplicates.
            var resolved = new List<SkinCatalogEntry>();
            var missingIds = new List<string>();
            var seen = new HashSet<string>();
            var dupIds = new List<string>();
            foreach (var id in pool)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!seen.Add(id)) dupIds.Add(id);
                if (_skinById != null && _skinById.TryGetValue(id, out var s)) resolved.Add(s);
                else missingIds.Add(id);
            }

            a.Category = InferCategory(c.caseId, resolved);

            // Pool rarity counts + averages (over resolved skins).
            foreach (var r in RarityOrder)
            {
                var rr = resolved.Where(s => RarityEq(s.rarity, r)).ToList();
                if (rr.Count == 0) continue;
                a.PoolRarityCounts[r] = rr.Count;
                a.PoolRarityAvg[r]    = (float)rr.Average(s => s.vpValue);
            }

            ComputeExpectedValue(a, resolved);
            BuildIssues(a, resolved, missingIds, dupIds);
            BuildRecommendation(a);

            return a;
        }

        // EV: roll rarity by weight, then near-uniform skin pick within that rarity.
        // Normalized over rarities whose weight > 0 AND that have at least one
        // matching pool skin (the reachable distribution).
        void ComputeExpectedValue(CaseAnalysis a, List<SkinCatalogEntry> resolved)
        {
            float reachableWeight = 0f;
            var reachable = new List<(string rarity, float weight, float avg)>();

            foreach (var kv in a.Weights)
            {
                if (kv.Value <= 0f) continue;
                if (!a.PoolRarityAvg.TryGetValue(kv.Key, out var avg)) continue; // no matching skin (flagged elsewhere)
                reachable.Add((kv.Key, kv.Value, avg));
                reachableWeight += kv.Value;
            }

            float keepEv = 0f;
            if (reachableWeight > 0f)
                foreach (var e in reachable)
                    keepEv += (e.weight / reachableWeight) * e.avg;

            a.KeepEV = keepEv;
            a.SellEV = keepEv * _sellMul;
            a.DupEV  = keepEv * _dupFrac;

            if (a.Price > 0)
            {
                a.KeepRatio = a.KeepEV / a.Price;
                a.SellRatio = a.SellEV / a.Price;
                a.DupRatio  = a.DupEV  / a.Price;
            }

            // Highest/lowest reachable skin value (only rarities with weight > 0).
            var reachableSkins = resolved
                .Where(s => a.Weights.TryGetValue(CanonRarity(s.rarity), out var w) && w > 0f)
                .ToList();
            if (reachableSkins.Count > 0)
            {
                a.HighestValue = reachableSkins.Max(s => s.vpValue);
                a.LowestValue  = reachableSkins.Min(s => s.vpValue);
            }
        }

        void BuildIssues(CaseAnalysis a, List<SkinCatalogEntry> resolved, List<string> missingIds, List<string> dupIds)
        {
            // Price.
            if (a.Price <= 0)
                a.Issues.Add(new Issue(Severity.Critical, "PRICE", "Case price is zero or negative."));

            // Empty pool.
            if (a.PoolCount == 0)
                a.Issues.Add(new Issue(Severity.Critical, "EMPTY_POOL", "manualDropPool is empty."));

            // Missing skin ids.
            if (missingIds.Count > 0)
                a.Issues.Add(new Issue(Severity.Critical, "MISSING_SKIN",
                    $"{missingIds.Count} pool skin id(s) not found in skins.json: {Join(missingIds, 6)}"));

            // Duplicate skin ids.
            if (dupIds.Count > 0)
                a.Issues.Add(new Issue(Severity.Warning, "DUP_SKIN",
                    $"Duplicate skin id(s) in pool: {Join(dupIds.Distinct(), 6)}"));

            // Missing-rarity / null-roll: weight > 0 but no matching pool skin.
            foreach (var kv in a.Weights)
            {
                if (kv.Value <= 0f) continue;
                bool hasMatch = a.PoolRarityCounts.ContainsKey(kv.Key);
                if (!hasMatch)
                {
                    bool isClassicUltra = string.Equals(a.Category, "Classic", StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(kv.Key, "Ultra", StringComparison.OrdinalIgnoreCase);
                    string extra = isClassicUltra ? " (Classic has no Ultra skins — remove Ultra weight)" : "";
                    a.Issues.Add(new Issue(Severity.Critical, "NULL_ROLL",
                        $"Rarity '{kv.Key}' has weight {kv.Value:0.##} but no matching skin in pool — roll can return null.{extra}"));
                }
            }

            // Unused pool rarity: skins present but rarity weight is 0 / absent.
            foreach (var r in a.PoolRarityCounts.Keys)
            {
                float w = a.Weights.TryGetValue(r, out var wv) ? wv : 0f;
                if (w <= 0f)
                    a.Issues.Add(new Issue(Severity.Warning, "UNREACHABLE",
                        $"Pool contains {a.PoolRarityCounts[r]} '{r}' skin(s) but its weight is 0 — those skins are unreachable."));
            }

            // Prefer >= 2 skins for any weighted rarity.
            foreach (var kv in a.Weights)
            {
                if (kv.Value <= 0f) continue;
                if (a.PoolRarityCounts.TryGetValue(kv.Key, out var n) && n == 1)
                    a.Issues.Add(new Issue(Severity.Info, "THIN_RARITY",
                        $"Rarity '{kv.Key}' has only 1 skin in pool — prefer at least 2 for variety."));
            }

            // Economy balance checks.
            if (a.Price > 0)
            {
                if (a.SellEV >= a.Price)
                    a.Issues.Add(new Issue(Severity.Critical, "VP_FARM",
                        $"sell-EV ({a.SellEV:0}) >= price ({a.Price}) — VP farm exploit."));
                else if (a.SellRatio > 0.45f)
                    a.Issues.Add(new Issue(Severity.Warning, "GENEROUS_SELL",
                        $"sell-EV/price = {a.SellRatio:0.###} (> 0.45) — too generous."));

                if (a.KeepRatio > 0.95f)
                    a.Issues.Add(new Issue(Severity.Warning, "GENEROUS_KEEP",
                        $"keep-EV/price = {a.KeepRatio:0.###} (> 0.95) — too generous."));

                bool isArcane = string.Equals(a.Tier, "arcane", StringComparison.OrdinalIgnoreCase);
                if (a.KeepRatio < 0.45f && !isArcane)
                    a.Issues.Add(new Issue(Severity.Warning, "HARSH_KEEP",
                        $"keep-EV/price = {a.KeepRatio:0.###} (< 0.45) — too harsh for a {a.Tier} case."));
            }

            if (string.IsNullOrEmpty(a.Tier))
                a.Issues.Add(new Issue(Severity.Info, "TIER",
                    "Tier could not be inferred from case id (expected basic/protocol/radiant/arcane)."));
        }

        void BuildRecommendation(CaseAnalysis a)
        {
            if (!Targets.TryGetValue(a.Tier ?? "", out var t) || a.SellEV <= 0f)
            {
                a.PriceStatus = "n/a";
                a.SuggestedAction = "Insufficient data to recommend a price (missing tier, EV, or pool).";
                return;
            }

            a.RecMinPrice = RoundPrice(a.SellEV / t.SellHigh);
            a.RecMaxPrice = RoundPrice(a.SellEV / t.SellLow);
            a.PreferredPrice = RoundPrice(a.KeepEV / t.PreferredFactor);

            if (a.Price <= 0)
            {
                a.PriceStatus = "INVALID";
                a.SuggestedAction = $"Set a price. Preferred ~{a.PreferredPrice} VP (range {a.RecMinPrice}-{a.RecMaxPrice}).";
            }
            else if (a.Price < a.RecMinPrice)
            {
                a.PriceStatus = "TOO CHEAP";
                a.SuggestedAction = $"Raise price from {a.Price} toward ~{a.PreferredPrice} VP (min {a.RecMinPrice}).";
            }
            else if (a.Price > a.RecMaxPrice)
            {
                a.PriceStatus = "TOO EXPENSIVE";
                a.SuggestedAction = $"Lower price from {a.Price} toward ~{a.PreferredPrice} VP (max {a.RecMaxPrice}).";
            }
            else
            {
                a.PriceStatus = "OK";
                a.SuggestedAction = $"Price {a.Price} is within target ({a.RecMinPrice}-{a.RecMaxPrice}); preferred ~{a.PreferredPrice}.";
            }
        }

        // Melee cross-case check: warn if tiers all sit on near-identical value bands.
        void CheckMeleeBandSpread()
        {
            var melee = _cases.Where(c => string.Equals(c.Category, "Melee", StringComparison.OrdinalIgnoreCase)
                                          && c.KeepEV > 0f).ToList();
            if (melee.Count < 2) return;

            float min = melee.Min(c => c.KeepEV);
            float max = melee.Max(c => c.KeepEV);
            if (min > 0f && max / min < 1.15f)
            {
                foreach (var c in melee)
                    c.Issues.Add(new Issue(Severity.Warning, "MELEE_BANDS",
                        "Melee tiers use nearly the same VP value band — higher tiers should pull from clearly higher-value Exclusive skins."));
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static string InferTier(string caseId)
        {
            if (string.IsNullOrEmpty(caseId)) return "";
            var id = caseId.ToLowerInvariant();
            foreach (var tier in new[] { "basic", "protocol", "radiant", "arcane" })
                if (id.Contains(tier)) return tier;
            return "";
        }

        string InferCategory(string caseId, List<SkinCatalogEntry> resolved)
        {
            if (!string.IsNullOrEmpty(caseId))
            {
                var id = caseId.ToLowerInvariant();
                foreach (var cat in _knownCategories)
                    if (id.Contains(cat.ToLowerInvariant())) return cat;
            }
            if (resolved != null && resolved.Count > 0)
                return resolved.GroupBy(s => s.weapon)
                               .OrderByDescending(g => g.Count())
                               .First().Key;
            return "(unknown)";
        }

        static bool RarityEq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string CanonRarity(string r)
        {
            foreach (var ro in RarityOrder)
                if (RarityEq(ro, r)) return ro;
            return r;
        }

        static int RoundPrice(float v)
        {
            if (v <= 0f) return 0;
            if (v > 1000f) return Mathf.Max(100, Mathf.RoundToInt(v / 100f) * 100);
            return Mathf.Max(50, Mathf.RoundToInt(v / 50f) * 50);
        }

        static string Join(IEnumerable<string> items, int max)
        {
            var list = items.Take(max).ToList();
            var s = string.Join(", ", list);
            return list.Count >= max ? s + ", ..." : s;
        }

        static string Money(float v) => Mathf.RoundToInt(v).ToString("N0", CultureInfo.InvariantCulture);

        bool HasSeverity(CaseAnalysis a, Severity sev) => a.Issues.Any(i => i.Sev == sev);

        // ── GUI ──────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
                return;
            }
            if (_cases == null)
            {
                EditorGUILayout.HelpBox("Press Refresh / Analyze.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Economy config: " + _configSource, EditorStyles.miniLabel);
            int crit = _cases.Sum(c => c.Issues.Count(i => i.Sev == Severity.Critical));
            int warn = _cases.Sum(c => c.Issues.Count(i => i.Sev == Severity.Warning));
            EditorGUILayout.LabelField($"Cases: {_cases.Count}   Critical: {crit}   Warning: {warn}", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawCategorySection();
            EditorGUILayout.Space(6);
            DrawCaseSection();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh / Analyze", EditorStyles.toolbarButton, GUILayout.Width(130))) Analyze();
            using (new EditorGUI.DisabledScope(_cases == null))
            {
                if (GUILayout.Button("Copy Full Report", EditorStyles.toolbarButton, GUILayout.Width(120)))
                    CopyToClipboard(BuildFullReport());
                if (GUILayout.Button("Copy Critical Only", EditorStyles.toolbarButton, GUILayout.Width(130)))
                    CopyToClipboard(BuildCriticalReport());
                if (GUILayout.Button("Copy Claude Fix Prompt", EditorStyles.toolbarButton, GUILayout.Width(160)))
                    CopyToClipboard(BuildClaudePrompt());
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void CopyToClipboard(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            ShowNotification(new GUIContent("Copied to clipboard"));
        }

        void DrawCategorySection()
        {
            _catSectionOpen = EditorGUILayout.Foldout(_catSectionOpen, "Skin Catalog Analysis", true, EditorStyles.foldoutHeader);
            if (!_catSectionOpen) return;

            EditorGUI.indentLevel++;
            foreach (var ca in _categories)
            {
                bool open = _catFoldouts.TryGetValue(ca.Category, out var o) && o;
                open = EditorGUILayout.Foldout(open, $"{ca.Category}  ({ca.Total} skins, avg {Money(ca.AvgVp)} VP)", true);
                _catFoldouts[ca.Category] = open;
                if (!open) continue;

                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"VP range: {ca.MinVp} - {ca.MaxVp}   avg {Money(ca.AvgVp)}");
                EditorGUILayout.LabelField("Ultra exists: " + (ca.UltraExists ? "yes" : "no"));
                foreach (var r in RarityOrder)
                {
                    int n = ca.RarityCounts.GetValueOrDefault(r);
                    if (n == 0) { EditorGUILayout.LabelField($"  {r}: 0"); continue; }
                    EditorGUILayout.LabelField($"  {r}: {n}   min {ca.RarityMin[r]}  max {ca.RarityMax[r]}  avg {Money(ca.RarityAvg[r])}");
                }
                foreach (var note in ca.Notes)
                    EditorGUILayout.HelpBox(note, MessageType.Info);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
            EditorGUI.indentLevel--;
        }

        void DrawCaseSection()
        {
            EditorGUILayout.LabelField("Case Analysis", EditorStyles.foldoutHeader);
            foreach (var a in _cases.OrderBy(c => c.Category).ThenBy(c => TierIndex(c.Tier)))
                DrawCase(a);
        }

        static int TierIndex(string tier) => tier switch
        {
            "basic" => 0, "protocol" => 1, "radiant" => 2, "arcane" => 3, _ => 4
        };

        void DrawCase(CaseAnalysis a)
        {
            string badge = HasSeverity(a, Severity.Critical) ? "  [CRITICAL]"
                         : HasSeverity(a, Severity.Warning)  ? "  [warn]" : "";
            bool open = _caseFoldouts.TryGetValue(a.CaseId, out var o) && o;
            open = EditorGUILayout.Foldout(open, $"{a.CaseId}  ({a.Tier}, {a.Price} VP){badge}", true);
            _caseFoldouts[a.CaseId] = open;
            if (!open) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(a.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Category {a.Category}   Tier {a.Tier}   Price {a.Price}   Pool {a.PoolCount}");

            EditorGUILayout.LabelField("Rarity weights:", EditorStyles.miniBoldLabel);
            foreach (var r in RarityOrder)
            {
                float w = a.Weights.GetValueOrDefault(r);
                int n = a.PoolRarityCounts.GetValueOrDefault(r);
                if (w <= 0f && n == 0) continue;
                string avg = a.PoolRarityAvg.TryGetValue(r, out var av) ? $"avg {Money(av)}" : "no pool skins";
                EditorGUILayout.LabelField($"   {r}: weight {w:0.##}, pool {n}, {avg}");
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"keep-EV {Money(a.KeepEV)}   sell-EV {Money(a.SellEV)}   dup-EV {Money(a.DupEV)}");
            EditorGUILayout.LabelField($"keep/price {a.KeepRatio:0.###}   sell/price {a.SellRatio:0.###}   dup/price {a.DupRatio:0.###}");
            if (a.HighestValue > 0 || a.LowestValue > 0)
                EditorGUILayout.LabelField($"Reachable value range: {a.LowestValue} - {a.HighestValue}");

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Recommendation:", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"   Current {a.Price}   Range {a.RecMinPrice}-{a.RecMaxPrice}   Preferred {a.PreferredPrice}   [{a.PriceStatus}]");
            if (!string.IsNullOrEmpty(a.SuggestedAction))
                EditorGUILayout.LabelField("   " + a.SuggestedAction, EditorStyles.wordWrappedMiniLabel);

            foreach (var issue in a.Issues.OrderByDescending(i => (int)i.Sev))
            {
                var mt = issue.Sev == Severity.Critical ? MessageType.Error
                       : issue.Sev == Severity.Warning  ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox($"[{issue.Code}] {issue.Message}", mt);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // ── Report builders ──────────────────────────────────────────────────

        string BuildFullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ValoCase Economy Balance Report");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Economy config: " + _configSource);
            sb.AppendLine();

            sb.AppendLine("== Skin Catalog by Category ==");
            foreach (var ca in _categories)
            {
                sb.AppendLine($"{ca.Category}: {ca.Total} skins, VP {ca.MinVp}-{ca.MaxVp} avg {Money(ca.AvgVp)}, Ultra {(ca.UltraExists ? "yes" : "no")}");
                foreach (var r in RarityOrder)
                {
                    int n = ca.RarityCounts.GetValueOrDefault(r);
                    if (n == 0) continue;
                    sb.AppendLine($"   {r}: {n}  min {ca.RarityMin[r]} max {ca.RarityMax[r]} avg {Money(ca.RarityAvg[r])}");
                }
                foreach (var note in ca.Notes) sb.AppendLine("   note: " + note);
            }
            sb.AppendLine();

            sb.AppendLine("== Cases ==");
            foreach (var a in _cases.OrderBy(c => c.Category).ThenBy(c => TierIndex(c.Tier)))
                AppendCase(sb, a);
            return sb.ToString();
        }

        void AppendCase(StringBuilder sb, CaseAnalysis a)
        {
            sb.AppendLine($"- {a.CaseId} [{a.Tier}] {a.Category}");
            sb.AppendLine($"   price {a.Price}, pool {a.PoolCount}");
            sb.Append("   weights:");
            foreach (var r in RarityOrder)
                if (a.Weights.GetValueOrDefault(r) > 0f) sb.Append($" {r}={a.Weights[r]:0.##}");
            sb.AppendLine();
            sb.Append("   pool rarities:");
            foreach (var r in RarityOrder)
                if (a.PoolRarityCounts.GetValueOrDefault(r) > 0) sb.Append($" {r}={a.PoolRarityCounts[r]}(avg {Money(a.PoolRarityAvg[r])})");
            sb.AppendLine();
            sb.AppendLine($"   keep-EV {Money(a.KeepEV)} sell-EV {Money(a.SellEV)} dup-EV {Money(a.DupEV)}");
            sb.AppendLine($"   keep/price {a.KeepRatio:0.###} sell/price {a.SellRatio:0.###} dup/price {a.DupRatio:0.###}");
            sb.AppendLine($"   recommend range {a.RecMinPrice}-{a.RecMaxPrice} preferred {a.PreferredPrice} [{a.PriceStatus}] {a.SuggestedAction}");
            foreach (var i in a.Issues.OrderByDescending(x => (int)x.Sev))
                sb.AppendLine($"   {i.Sev.ToString().ToUpperInvariant()} [{i.Code}] {i.Message}");
            sb.AppendLine();
        }

        string BuildCriticalReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ValoCase Economy — Critical Issues Only");
            sb.AppendLine("Economy config: " + _configSource);
            sb.AppendLine();
            bool any = false;
            foreach (var a in _cases.OrderBy(c => c.Category).ThenBy(c => TierIndex(c.Tier)))
            {
                var crits = a.Issues.Where(i => i.Sev == Severity.Critical).ToList();
                if (crits.Count == 0) continue;
                any = true;
                sb.AppendLine($"{a.CaseId} [{a.Tier}] price {a.Price} sell-EV {Money(a.SellEV)} (sell/price {a.SellRatio:0.###})");
                foreach (var i in crits) sb.AppendLine($"   [{i.Code}] {i.Message}");
            }
            if (!any) sb.AppendLine("No critical issues found.");
            return sb.ToString();
        }

        string BuildClaudePrompt()
        {
            var problem = _cases.Where(c => c.Issues.Any(i => i.Sev != Severity.Info)
                                            || c.PriceStatus == "TOO CHEAP"
                                            || c.PriceStatus == "TOO EXPENSIVE"
                                            || c.PriceStatus == "INVALID").ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Fix the ValoCase economy data based on the analysis below.");
            sb.AppendLine();
            sb.AppendLine("Files you may edit (only these):");
            sb.AppendLine("- Assets/_ValoCase/Resources/Config/cases.json");
            sb.AppendLine("- Assets/_ValoCase/Resources/Config/skins.json");
            sb.AppendLine();
            sb.AppendLine("Economy constants in effect: " + _configSource);
            sb.AppendLine();
            sb.AppendLine("Per-case recommended prices and required fixes:");
            foreach (var a in problem.OrderBy(c => c.Category).ThenBy(c => TierIndex(c.Tier)))
            {
                sb.AppendLine($"* {a.CaseId} [{a.Tier}]");
                sb.AppendLine($"  - current price {a.Price}; recommended {a.RecMinPrice}-{a.RecMaxPrice}; preferred {a.PreferredPrice} ({a.PriceStatus})");
                sb.AppendLine($"  - keep-EV {Money(a.KeepEV)}, sell-EV {Money(a.SellEV)}, sell/price {a.SellRatio:0.###}");
                foreach (var i in a.Issues.Where(x => x.Sev == Severity.Critical))
                    sb.AppendLine($"  - CRITICAL [{i.Code}] {i.Message}");
                foreach (var i in a.Issues.Where(x => x.Sev == Severity.Warning))
                    sb.AppendLine($"  - WARNING [{i.Code}] {i.Message}");
            }
            if (problem.Count == 0) sb.AppendLine("* No problematic cases detected.");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Do not edit any files other than the two listed above.");
            sb.AppendLine("- Every rarity with weight > 0 must have at least one real matching skin in that case pool; prefer at least 2.");
            sb.AppendLine("- Every rarity with weight 0 must not appear in that pool.");
            sb.AppendLine("- Do not invent skin ids; only use ids that exist in skins.json.");
            sb.AppendLine("- Do not add Ultra to categories that have no Ultra skins (e.g. Classic, Melee).");
            sb.AppendLine("- Keep every case a VP sink (sell-EV well below price).");
            sb.AppendLine("- Do not claim tests you did not run.");
            sb.AppendLine("- Do not say backend was changed.");
            return sb.ToString();
        }
    }
}
