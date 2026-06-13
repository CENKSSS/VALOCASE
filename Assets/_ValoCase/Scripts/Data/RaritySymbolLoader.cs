using System.Collections.Generic;
using System.Text;
using UnityEngine;
// Paths managed centrally in ProjectPaths.cs

namespace ValoCase.Data
{
    /// <summary>
    /// Loads rarity-symbol sprites from Resources (Art/UI/Semboller) at runtime.
    /// MOBILE-SAFE: uses Resources.LoadAll&lt;Sprite&gt; instead of System.IO, so it
    /// works in Android/iOS player builds.
    ///
    /// CACHING
    ///   All symbols are loaded once on first access and cached for the session.
    ///   A missing rarity is logged at most once.
    ///
    /// (Unity's importer decodes the PNGs at import time, so the previous
    ///  byte-level ICC-profile stripping / WebP detection is no longer needed.)
    /// </summary>
    public static class RaritySymbolLoader
    {
        const string Tag = "[RaritySymbol]";

        // Sprite cache. A rarity absent from the dict after EnsureLoaded() = "not found".
        static readonly Dictionary<SkinRarity, Sprite> _sprites =
            new Dictionary<SkinRarity, Sprite>();

        static bool _loaded;

        // Rarities whose "not found" warning has already been logged this session.
        static readonly HashSet<SkinRarity> _reported = new HashSet<SkinRarity>();

        // ── Keyword map: matched against each loaded sprite's name ──────────────
        static readonly Dictionary<SkinRarity, string[]> Keywords =
            new Dictionary<SkinRarity, string[]>
            {
                { SkinRarity.Select,    new[] { "ozel",    "select"    } },
                { SkinRarity.Deluxe,    new[] { "ustun",   "deluxe"    } },
                { SkinRarity.Premium,   new[] { "ihtisam", "premium"   } },
                { SkinRarity.Exclusive, new[] { "seckin",  "exclusive" } },
                { SkinRarity.Ultra,     new[] { "ultra"               } },
            };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true + sets sprite when the symbol could be loaded.
        /// Returns false + null on failure — logged at most once per rarity.
        /// </summary>
        public static bool TryGet(SkinRarity rarity, out Sprite sprite)
        {
            EnsureLoaded();

            if (_sprites.TryGetValue(rarity, out sprite) && sprite != null)
                return true;

            sprite = null;
            if (_reported.Add(rarity))
                Debug.LogWarning(
                    $"{Tag} No symbol sprite for rarity={rarity}. " +
                    $"Expected one matching [{string.Join(", ", Keywords[rarity])}] " +
                    $"in Resources/{ProjectPaths.SymbolsRoot}/");
            return false;
        }

        /// <summary>Force a re-scan on next access (e.g. after files are replaced).</summary>
        public static void InvalidateCache()
        {
            _sprites.Clear();
            _reported.Clear();
            _loaded = false;
        }

        // ── Core ────────────────────────────────────────────────────────────────

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var sprites = Resources.LoadAll<Sprite>(ProjectPaths.SymbolsRoot);
            if (sprites == null || sprites.Length == 0)
            {
                Debug.LogWarning($"{Tag} No symbol sprites found in Resources/{ProjectPaths.SymbolsRoot}/");
                return;
            }

            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                var norm = Normalise(sprite.name);

                foreach (var kv in Keywords)
                {
                    if (_sprites.ContainsKey(kv.Key)) continue;   // first match wins
                    foreach (var kw in kv.Value)
                    {
                        if (!norm.Contains(kw)) continue;
                        _sprites[kv.Key] = sprite;
                        Debug.Log($"{Tag} '{sprite.name}' → {kv.Key}");
                        break;
                    }
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static string Normalise(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s.ToLowerInvariant())
                switch (c)
                {
                    case 'ı': sb.Append('i'); break;
                    case 'ğ': sb.Append('g'); break;
                    case 'ü': sb.Append('u'); break;
                    case 'ş': sb.Append('s'); break;
                    case 'ö': sb.Append('o'); break;
                    case 'ç': sb.Append('c'); break;
                    case 'â': sb.Append('a'); break;
                    case 'î': sb.Append('i'); break;
                    case 'û': sb.Append('u'); break;
                    default:  sb.Append(c);   break;
                }
            return sb.ToString();
        }
    }
}
