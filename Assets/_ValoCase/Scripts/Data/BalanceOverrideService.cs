using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Data
{
    /// <summary>
    /// Centralized read layer for balance/economy overrides. The default catalog stays
    /// authoritative; this only supplies override values when an entry exists. With no
    /// override file (or an empty one) every query falls back to the catalog value, so
    /// the game behaves exactly as before. JSON reads live ONLY here.
    /// </summary>
    public sealed class BalanceOverrideService
    {
        public const string ResourceKey = "Config/balance_overrides";
        public const string AssetPath   = "Assets/_ValoCase/Resources/Config/balance_overrides.json";

        static BalanceOverrideService _instance;
        public static BalanceOverrideService Instance => _instance ??= LoadFromResources();

        readonly Dictionary<string, SkinBalanceOverride> _skins = new(StringComparer.Ordinal);
        readonly Dictionary<string, CaseBalanceOverride> _cases = new(StringComparer.Ordinal);
        readonly Dictionary<string, CaseDropBalanceOverride> _drops = new(StringComparer.Ordinal);

        public bool HasAny { get; }

        BalanceOverrideService(BalanceOverrideRoot root)
        {
            if (root != null)
            {
                if (root.skinOverrides != null)
                    foreach (var s in root.skinOverrides)
                        if (s != null && !string.IsNullOrEmpty(s.skinId)) _skins[s.skinId] = s;

                if (root.caseOverrides != null)
                    foreach (var c in root.caseOverrides)
                        if (c != null && !string.IsNullOrEmpty(c.caseId)) _cases[c.caseId] = c;

                if (root.dropOverrides != null)
                    foreach (var d in root.dropOverrides)
                        if (d != null && !string.IsNullOrEmpty(d.caseId) && !string.IsNullOrEmpty(d.skinId))
                            _drops[DropKey(d.caseId, d.skinId)] = d;
            }

            HasAny = _skins.Count > 0 || _cases.Count > 0 || _drops.Count > 0;
        }

        public static void Reload() => _instance = LoadFromResources();

        static BalanceOverrideService LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>(ResourceKey);
            var root = asset != null ? Deserialize(asset.text) : null;
            return new BalanceOverrideService(root);
        }

        public static BalanceOverrideRoot Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<BalanceOverrideRoot>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[BalanceOverride] Could not parse overrides — ignoring. {e.Message}");
                return null;
            }
        }

        public static string Serialize(BalanceOverrideRoot root) =>
            JsonUtility.ToJson(root ?? new BalanceOverrideRoot(), true);

        static string DropKey(string caseId, string skinId) => caseId + "|" + skinId;

        public bool TryGetSkinVp(string skinId, out int vp)
        {
            vp = 0;
            if (_skins.TryGetValue(skinId, out var o) && o.hasVpOverride && o.vpValueOverride >= 0)
            {
                vp = o.vpValueOverride;
                return true;
            }
            return false;
        }

        public bool IsSkinEnabled(string skinId) =>
            !_skins.TryGetValue(skinId, out var o) || o.enabled;

        public bool TryGetCasePrice(string caseId, out int price)
        {
            price = 0;
            if (_cases.TryGetValue(caseId, out var o) && o.hasPriceOverride && o.priceOverride >= 0)
            {
                price = o.priceOverride;
                return true;
            }
            return false;
        }

        public bool IsCaseEnabled(string caseId) =>
            !_cases.TryGetValue(caseId, out var o) || o.enabled;

        public bool IsDropEnabled(string caseId, string skinId) =>
            !_drops.TryGetValue(DropKey(caseId, skinId), out var o) || o.enabled;

        public float GetDropWeight(string caseId, string skinId, float defaultWeight)
        {
            if (_drops.TryGetValue(DropKey(caseId, skinId), out var o) && o.hasWeightOverride && o.weightOverride >= 0f)
                return o.weightOverride;
            return defaultWeight;
        }
    }
}
