namespace ValoCase.Data
{
    /// <summary>
    /// Single source of truth for every art location the project loads at runtime.
    ///
    /// MOBILE-SAFE: every value here is a Unity *Resources path* — relative to a
    /// Resources folder, forward-slashed, and WITHOUT a file extension. These are
    /// passed to Resources.Load&lt;Sprite&gt; / Resources.LoadAll&lt;Sprite&gt;, which
    /// works identically in the Editor and in Android/iOS player builds.
    ///
    /// (The old System.IO + Application.dataPath scheme did NOT work on device,
    /// because Application.dataPath points inside the compressed APK/app bundle.)
    ///
    /// Physical layout (all under Assets/_ValoCase/Resources/Art/):
    ///   Skins/               ← weapon skin images   (FileSystemSkinLoader)
    ///     &lt;Weapon&gt;/
    ///       Ozel/  Ustun/  Ihtisamli/  Ultra/  Seckin/
    ///   UI/Semboller/        ← rarity symbol PNGs    (RaritySymbolLoader)
    ///   Cases/               ← case icon images      (VandalCaseBuilder)
    ///   Avatars/             ← agent portrait images (ProfileAvatarLoader)
    ///   Backgorunds/         ← screen / card backgrounds (FullscreenBackground)
    ///   UI/                  ← misc UI art (ArkaPlan, etc.)
    ///
    /// To relocate the whole art tree, change only Root below (and move the files).
    /// </summary>
    public static class ProjectPaths
    {
        // ── Root ──────────────────────────────────────────────────────────────

        /// <summary>Resources/Art — all runtime-loaded art lives here.</summary>
        public const string ProjectRoot = "Art";

        // ── Sub-folders (Resources roots) ──────────────────────────────────────

        /// <summary>Art/Skins — weapon skin images. Layout: Weapon/RarityFolder/Skin.png</summary>
        public const string SkinsRoot = ProjectRoot + "/Skins";

        /// <summary>Art/UI/Semboller — rarity symbol sprites.</summary>
        public const string SymbolsRoot = ProjectRoot + "/UI/Semboller";

        /// <summary>Art/Cases — case icon sprites.</summary>
        public const string CaseIconsRoot = ProjectRoot + "/Cases";

        /// <summary>Art/UI — misc UI art.</summary>
        public const string ArayuzRoot = ProjectRoot + "/UI";

        /// <summary>Art/Backgorunds — screen / card background sprites.
        /// (Folder name matches the existing on-disk spelling.)</summary>
        public const string BackgroundsRoot = ProjectRoot + "/Backgorunds";

        /// <summary>Art/Avatars — agent portrait sprites for player profiles.</summary>
        public const string FaceCardsRoot = ProjectRoot + "/Avatars";

        // ── Individual sprites (Resources paths, no extension) ──────────────────

        /// <summary>Art/UI/ArkaPlan — main menu full-screen background.</summary>
        public const string ArkaPlanPath = ArayuzRoot + "/ArkaPlan";

        /// <summary>Art/Backgorunds/background01 — shared background for the
        /// Cases, Tools, Inventory, Upgrade and Market screens.</summary>
        public const string SharedScreenBackgroundPath = BackgroundsRoot + "/background01";

        /// <summary>Art/Backgorunds/background02 — background inside every case
        /// card on the Cases screen.</summary>
        public const string CaseCardBackgroundPath = BackgroundsRoot + "/background02";

        /// <summary>Art/UI/MeleeMysteryIcon — generic golden icon shown for Melee
        /// rewards during the case-opening roll only (reveal shows the real skin).</summary>
        public const string MeleeMysteryIconPath = ArayuzRoot + "/MeleeIcon/MeleeMysteryIcon";
    }
}
