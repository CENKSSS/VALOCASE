using UnityEngine;
using ValoCase.Data;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Shared colors, formatters and rarity helpers for the Case Battle feature.
    /// Used by CaseBattleUiBuilder, CaseBattleRouletteAnimator and CaseBattleScreen.
    /// </summary>
    internal static class CaseBattlePalette
    {
        // ── Backgrounds ───────────────────────────────────────────────────────
        internal static readonly Color BgDeep     = new Color(0.022f, 0.016f, 0.055f, 1.00f);
        internal static readonly Color BgCard     = new Color(0.068f, 0.055f, 0.135f, 1.00f);
        internal static readonly Color BgCardDark = new Color(0.038f, 0.028f, 0.085f, 1.00f);
        internal static readonly Color BgCardHi   = new Color(0.120f, 0.090f, 0.220f, 1.00f);

        // ── Neon accents ──────────────────────────────────────────────────────
        internal static readonly Color AccentPink     = new Color(1.00f, 0.18f, 0.55f, 1.00f);
        internal static readonly Color AccentPinkSoft = new Color(1.00f, 0.18f, 0.55f, 0.25f);
        internal static readonly Color AccentPinkGlow = new Color(1.00f, 0.18f, 0.55f, 0.07f);
        internal static readonly Color AccentOrange   = new Color(1.00f, 0.55f, 0.15f, 1.00f);
        internal static readonly Color AccentGreen    = new Color(0.30f, 0.96f, 0.42f, 1.00f);
        internal static readonly Color AccentRed      = new Color(0.96f, 0.22f, 0.22f, 1.00f);
        internal static readonly Color CyberBlue      = new Color(0.12f, 0.42f, 1.00f, 1.00f);
        internal static readonly Color CyberBlueSoft  = new Color(0.12f, 0.42f, 1.00f, 0.18f);
        internal static readonly Color CyberBlueGlow  = new Color(0.12f, 0.42f, 1.00f, 0.07f);
        internal static readonly Color GoldAccent     = new Color(1.00f, 0.82f, 0.12f, 1.00f);

        // ── Text ─────────────────────────────────────────────────────────────
        internal static readonly Color TextWhite = new Color(0.97f, 0.97f, 1.00f, 1.00f);
        internal static readonly Color TextDim   = new Color(0.48f, 0.45f, 0.62f, 1.00f);

        // ── Structural ────────────────────────────────────────────────────────
        internal static readonly Color BorderPink = new Color(1.00f, 0.18f, 0.55f, 0.55f);
        internal static readonly Color BorderBlue = new Color(0.12f, 0.42f, 1.00f, 0.55f);

        // ── Reel ──────────────────────────────────────────────────────────────
        internal static readonly Color PremiumDark     = new Color(0.012f, 0.010f, 0.028f, 1f);
        internal static readonly Color PremiumPanel    = new Color(0.038f, 0.028f, 0.082f, 1f);
        internal static readonly Color ReelShadow      = new Color(0f, 0f, 0f, 0.55f);
        internal static readonly Color ReelCenterGlow  = new Color(1f, 0.18f, 0.55f, 0.08f);
        internal static readonly Color ReelFrame       = new Color(1f, 0.18f, 0.55f, 0.30f);

        // ── Utilities ─────────────────────────────────────────────────────────
        internal static string FormatGp(int amount)
            => amount >= 1000 ? $"(G) {amount / 1000f:0.##}K" : $"(G) {amount}";

        internal static Color GetRarityColor(SkinRarity rarity) => rarity switch
        {
            SkinRarity.Exclusive => new Color(1.00f, 0.82f, 0.10f, 1f),  // gold
            SkinRarity.Ultra     => new Color(0.58f, 0.08f, 0.98f, 1f),  // deep purple
            SkinRarity.Premium   => new Color(0.10f, 0.52f, 1.00f, 1f),  // cyber blue
            SkinRarity.Deluxe    => new Color(0.22f, 0.85f, 0.38f, 1f),  // neon green
            _                    => new Color(0.50f, 0.50f, 0.58f, 1f),  // grey
        };
    }
}
