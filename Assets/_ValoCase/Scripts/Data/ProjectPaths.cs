using System;
using System.IO;

namespace ValoCase.Data
{
    /// <summary>
    /// Single source of truth for every on-disk folder the project reads at runtime.
    ///
    /// Folder layout (all under Desktop/ValorantProject/):
    ///   ValoSkinss/          ← weapon skin images  (FileSystemSkinLoader)
    ///     Vandal/
    ///       Ozel/  Ustun/  Ihtisamli/  Ultra/  Seckin/
    ///   Semboller/           ← rarity symbol PNGs   (RaritySymbolLoader)
    ///   Case/                ← case icon images     (VandalCaseBuilder)
    ///
    /// To relocate the whole project tree, change only ProjectRoot below.
    /// </summary>
    public static class ProjectPaths
    {
        // ── Root ──────────────────────────────────────────────────────────────
        static readonly string Desktop =
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        /// <summary>Desktop/ValorantProject/</summary>
        public static readonly string ProjectRoot =
            Path.Combine(Desktop, "ValorantProject");

        // ── Sub-folders ───────────────────────────────────────────────────────

        /// <summary>
        /// Desktop/ValorantProject/ValoSkinss/ — weapon skin images.
        /// Layout: WeaponName / RarityFolder / SkinName.png
        /// </summary>
        public static readonly string SkinsRoot =
            Path.Combine(ProjectRoot, "ValoSkinss");

        /// <summary>
        /// Desktop/ValorantProject/Semboller/ — rarity symbol PNGs.
        /// </summary>
        public static readonly string SymbolsRoot =
            Path.Combine(ProjectRoot, "Semboller");

        /// <summary>
        /// Desktop/ValorantProject/Cases/ — case icon images (VCase.png etc.).
        /// </summary>
        public static readonly string CaseIconsRoot =
            Path.Combine(ProjectRoot, "Cases");

        /// <summary>
        /// Desktop/ValorantProject/Arayuz/ — UI art assets (backgrounds, logos, etc.).
        /// </summary>
        public static readonly string ArayuzRoot =
            Path.Combine(ProjectRoot, "Arayuz");

        /// <summary>
        /// Desktop/ValorantProject/Arayuz/ArkaPlan.jpg — main menu full-screen background.
        /// </summary>
        public static readonly string ArkaPlanPath =
            Path.Combine(ArayuzRoot, "ArkaPlan.jpg");

        /// <summary>
        /// Desktop/ValorantProject/FaceCards/ — agent portrait images for player profiles.
        /// Expected filenames: Chamber_icon.png, Jett_icon.png, etc.
        /// </summary>
        public static readonly string FaceCardsRoot =
            Path.Combine(ProjectRoot, "FaceCards");
    }
}
