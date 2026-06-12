using System.IO;
using UnityEngine;

namespace ValoCase.Data
{
    /// <summary>
    /// Single source of truth for every on-disk folder the project reads at runtime.
    ///
    /// Folder layout (all under Assets/_ValoCase/Art/):
    ///   Skins/               ← weapon skin images  (FileSystemSkinLoader)
    ///     Vandal/
    ///       Ozel/  Ustun/  Ihtisamli/  Ultra/  Seckin/
    ///   UI/Semboller/        ← rarity symbol PNGs   (RaritySymbolLoader)
    ///   Cases/               ← case icon images     (VandalCaseBuilder)
    ///   UI/                  ← UI art (backgrounds, logos, etc.)
    ///   Avatars/             ← agent portrait images (ProfileAvatarLoader)
    ///
    /// To relocate the whole art tree, change only ProjectRoot below.
    /// </summary>
    public static class ProjectPaths
    {
        // ── Root ──────────────────────────────────────────────────────────────

        /// <summary>Assets/_ValoCase/Art/ — all runtime-loaded art lives here.</summary>
        public static readonly string ProjectRoot =
            Path.Combine(Application.dataPath, "_ValoCase", "Art");

        // ── Sub-folders ───────────────────────────────────────────────────────

        /// <summary>
        /// Assets/_ValoCase/Art/Skins/ — weapon skin images.
        /// Layout: WeaponName / RarityFolder / SkinName.png
        /// </summary>
        public static readonly string SkinsRoot =
            Path.Combine(ProjectRoot, "Skins");

        /// <summary>
        /// Assets/_ValoCase/Art/UI/Semboller/ — rarity symbol PNGs.
        /// </summary>
        public static readonly string SymbolsRoot =
            Path.Combine(ProjectRoot, "UI", "Semboller");

        /// <summary>
        /// Assets/_ValoCase/Art/Cases/ — case icon images (VCase.png etc.).
        /// </summary>
        public static readonly string CaseIconsRoot =
            Path.Combine(ProjectRoot, "Cases");

        /// <summary>
        /// Assets/_ValoCase/Art/UI/ — UI art assets (backgrounds, logos, etc.).
        /// </summary>
        public static readonly string ArayuzRoot =
            Path.Combine(ProjectRoot, "UI");

        /// <summary>
        /// Assets/_ValoCase/Art/UI/ArkaPlan.jpg — main menu full-screen background.
        /// </summary>
        public static readonly string ArkaPlanPath =
            Path.Combine(ArayuzRoot, "ArkaPlan.jpg");

        /// <summary>
        /// Assets/_ValoCase/Art/Avatars/ — agent portrait images for player profiles.
        /// Expected filenames: Chamber_icon.png, Jett_icon.png, etc.
        /// </summary>
        public static readonly string FaceCardsRoot =
            Path.Combine(ProjectRoot, "Avatars");
    }
}
