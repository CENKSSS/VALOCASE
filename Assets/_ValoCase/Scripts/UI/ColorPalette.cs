using UnityEngine;
using ValoCase.Data;

namespace ValoCase.UI
{
    /// <summary>
    /// Central ValoCase color palette — premium dark, Valorant-inspired red/black.
    /// All values are static readonly so they allocate once and never per-frame.
    /// </summary>
    public static class ColorPalette
    {
        // ── Surfaces ──────────────────────────────────────────────────────────
        public static readonly Color BgDeep    = Hex(0x0D, 0x0E, 0x11);  // #0D0E11
        public static readonly Color CardBg    = Hex(0x14, 0x15, 0x19);  // #141519
        public static readonly Color Surface   = Hex(0x1E, 0x20, 0x26);  // #1E2026
        public static readonly Color Border    = Hex(0x2D, 0x32, 0x40);  // #2D3240

        // ── Accents ───────────────────────────────────────────────────────────
        public static readonly Color ActiveRed = Hex(0xC8, 0x25, 0x3C);  // #C8253C
        public static readonly Color RedDim    = Hex(0x8B, 0x1A, 0x28);  // #8B1A28
        public static readonly Color GoldAccent= Hex(0xE6, 0xCF, 0x6F);  // #E6CF6F

        // ── Text ──────────────────────────────────────────────────────────────
        public static readonly Color TextBright= Hex(0xFF, 0xFF, 0xFF);  // #FFFFFF
        public static readonly Color TextDim   = Hex(0x66, 0x6B, 0x7A);  // #666B7A

        // ── Rarity (mapped to ValoCase.Data.SkinRarity ordering) ──────────────
        public static readonly Color RarityCommon    = Hex(0xB0, 0xB8, 0xC4); // Select
        public static readonly Color RarityUncommon  = Hex(0x4D, 0xA6, 0xFF); // Deluxe
        public static readonly Color RarityRare      = Hex(0xA2, 0x59, 0xFF); // Premium
        public static readonly Color RarityEpic      = Hex(0xFF, 0x8C, 0x00); // Exclusive
        public static readonly Color RarityLegendary = Hex(0xFF, 0x1F, 0x39); // Ultra

        /// <summary>Maps the existing SkinRarity enum to its accent color.</summary>
        public static Color ForRarity(SkinRarity rarity)
        {
            switch (rarity)
            {
                case SkinRarity.Deluxe:    return RarityUncommon;
                case SkinRarity.Premium:   return RarityRare;
                case SkinRarity.Exclusive: return RarityEpic;
                case SkinRarity.Ultra:     return RarityLegendary;
                default:                   return RarityCommon;
            }
        }

        public static bool IsLegendary(SkinRarity rarity) => rarity == SkinRarity.Ultra;

        // ── Helpers ───────────────────────────────────────────────────────────
        static Color Hex(int r, int g, int b, float a = 1f)
            => new Color(r / 255f, g / 255f, b / 255f, a);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
