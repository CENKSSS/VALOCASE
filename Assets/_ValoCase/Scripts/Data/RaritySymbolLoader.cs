using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
// Paths managed centrally in ProjectPaths.cs

namespace ValoCase.Data
{
    /// <summary>
    /// Loads rarity-symbol images from Desktop/ValorantProject/Semboller at runtime.
    ///
    /// SPAM POLICY
    ///   • Every error / warning is logged AT MOST ONCE per rarity per session.
    ///   • After the first attempt (success or failure) the result is cached;
    ///     no disk I/O, no Texture2D work, no logging ever repeats.
    ///
    /// PNG ICC-PROFILE FIX
    ///   Unity's Texture2D.LoadImage rejects PNGs with embedded ICC chunks.
    ///   We strip iCCP / sRGB / gAMA / cHRM bytes before decoding.
    ///
    /// WEBP
    ///   Unity cannot decode WebP natively.  A one-time error tells the user
    ///   to convert the file; after that the fallback runs silently.
    /// </summary>
    public static class RaritySymbolLoader
    {
        const string Tag = "[RaritySymbol]";

        // ── Caches ────────────────────────────────────────────────────────────
        // Sprite cache: null entry = "failed, never retry".
        static readonly Dictionary<SkinRarity, Sprite> _sprites =
            new Dictionary<SkinRarity, Sprite>();

        // Rarities whose load result (success OR failure) has already been
        // reported to the console. Further calls stay completely silent.
        static readonly HashSet<SkinRarity> _reported =
            new HashSet<SkinRarity>();

        // ── Keyword map ───────────────────────────────────────────────────────
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
        /// Returns true + sets sprite when the symbol image could be loaded.
        /// Returns false + null on any failure — silently after the first attempt.
        /// Never touches the disk more than once per rarity per session.
        /// </summary>
        public static bool TryGet(SkinRarity rarity, out Sprite sprite)
        {
            // Already attempted (success or failure) — return cached result, no log.
            if (_sprites.TryGetValue(rarity, out sprite))
                return sprite != null;

            // First attempt: load from disk.
            sprite = LoadFromDisk(rarity);
            _sprites[rarity] = sprite;   // cache null on failure too
            return sprite != null;
        }

        /// <summary>Force re-scan on next access (e.g. after files are replaced).</summary>
        public static void InvalidateCache()
        {
            _sprites.Clear();
            _reported.Clear();
        }

        // ── Core pipeline ─────────────────────────────────────────────────────

        static Sprite LoadFromDisk(SkinRarity rarity)
        {
            if (!Keywords.TryGetValue(rarity, out var kws)) return null;

            // ── 1. Find file ──────────────────────────────────────────────────
            string path = FindFile(kws);
            if (path == null)
            {
                // Log once: file not found in any search root.
                if (_reported.Add(rarity))
                    Debug.LogWarning(
                        $"{Tag} No symbol file found for rarity={rarity} " +
                        $"(keywords: {string.Join(", ", kws)}). " +
                        $"Expected in Desktop/ValorantProject/Semboller/");
                return null;
            }

            // ── 2. Read bytes ─────────────────────────────────────────────────
            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                if (_reported.Add(rarity))
                    Debug.LogError($"{Tag} Read failed '{path}': {ex.Message}");
                return null;
            }

            // ── 3. Detect format ──────────────────────────────────────────────
            string fmt = DetectFormat(raw);

            if (fmt == "WEBP")
            {
                if (_reported.Add(rarity))
                    Debug.LogError(
                        $"{Tag} '{Path.GetFileName(path)}' is a WebP file (despite .png extension).\n" +
                        $"Unity cannot decode WebP. Fix: open in any image editor " +
                        $"and re-save as PNG-24 (File → Export As → PNG, 8-bit RGBA, no ICC profile).\n" +
                        $"Affected rarity: {rarity}");
                return null;
            }

            // ── 4. Strip PNG color-profile chunks that break Unity's decoder ──
            byte[] toLoad = raw;
            if (fmt == "PNG")
                toLoad = StripPngColorProfileChunks(raw);

            // ── 5. Decode with Texture2D.LoadImage ────────────────────────────
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            bool ok = tex.LoadImage(toLoad);
            if (!ok && !ReferenceEquals(toLoad, raw))   // retry with original
                ok = tex.LoadImage(raw);

            if (!ok)
            {
                if (_reported.Add(rarity))
                    Debug.LogError(
                        $"{Tag} Texture2D.LoadImage failed for '{Path.GetFileName(path)}' (rarity={rarity}).\n" +
                        $"The PNG may use 16-bit depth, indexed colour, or interlacing.\n" +
                        $"Fix: open in MS Paint or GIMP → Export As → PNG → 8-bit RGBA, no interlace, no ICC profile.");
                UnityEngine.Object.Destroy(tex);
                return null;
            }

            // ── 6. Wrap as Sprite — log success once ──────────────────────────
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);

            if (_reported.Add(rarity))
                Debug.Log($"{Tag} Loaded '{Path.GetFileName(path)}' → {tex.width}×{tex.height}px  rarity={rarity}");

            return sprite;
        }

        // ── PNG chunk stripper ────────────────────────────────────────────────

        /// <summary>
        /// Removes iCCP, sRGB, gAMA, cHRM, hIST chunks from PNG bytes.
        /// These colour-profile metadata chunks cause Unity's decoder to reject the file.
        /// Pixel data is completely untouched.
        /// </summary>
        static byte[] StripPngColorProfileChunks(byte[] data)
        {
            if (data == null || data.Length < 12) return data;

            // Verify PNG signature  89 50 4E 47 0D 0A 1A 0A
            if (data[0] != 0x89 || data[1] != 0x50 ||
                data[2] != 0x4E || data[3] != 0x47) return data;

            var output = new MemoryStream(data.Length);
            output.Write(data, 0, 8);   // signature

            int pos = 8;
            while (pos + 12 <= data.Length)
            {
                int dataLen = ReadBE32(data, pos);
                if (dataLen < 0 || (long)pos + 12 + dataLen > data.Length)
                {
                    output.Write(data, pos, data.Length - pos);
                    break;
                }

                string type      = Encoding.ASCII.GetString(data, pos + 4, 4);
                int    chunkSize = 4 + 4 + dataLen + 4;

                bool strip = type == "iCCP" || type == "sRGB" ||
                             type == "gAMA" || type == "cHRM" || type == "hIST";
                if (!strip)
                    output.Write(data, pos, chunkSize);

                pos += chunkSize;
            }

            return output.ToArray();
        }

        // ── Format detection ──────────────────────────────────────────────────

        static string DetectFormat(byte[] b)
        {
            if (b.Length < 4) return "UNKNOWN";
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "PNG";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)                  return "JPEG";
            if (b.Length >= 12 &&
                b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' &&
                b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')    return "WEBP";
            if (b[0] == 0x42 && b[1] == 0x4D)                                   return "BMP";
            return "UNKNOWN";
        }

        // ── File search ───────────────────────────────────────────────────────

        static string FindFile(string[] keywords)
        {
            // Primary location + fallbacks, all from ProjectPaths.
            var roots = new[]
            {
                ProjectPaths.SymbolsRoot,                          // ValorantProject/Semboller   ← canonical
                Path.Combine(ProjectPaths.SkinsRoot, "Semboller"), // ValorantProject/ValoSkinss/Semboller
                ProjectPaths.ProjectRoot,                          // ValorantProject/
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string[] files;
                try { files = Directory.GetFiles(root); }
                catch { continue; }

                foreach (var f in files)
                {
                    var norm = Normalise(Path.GetFileName(f));
                    foreach (var kw in keywords)
                        if (norm.Contains(kw)) return f;
                }
            }
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static int ReadBE32(byte[] d, int o) =>
            (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

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
