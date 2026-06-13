using UnityEditor;
using UnityEngine;

namespace ValoCase.EditorTools
{
    /// <summary>
    /// Forces every texture under Assets/_ValoCase/Resources/Art/ to import as a
    /// Sprite so it can be loaded with Resources.Load&lt;Sprite&gt;() at runtime
    /// (works in Editor AND in Android/iOS player builds).
    ///
    /// WHY THIS EXISTS
    ///   The project's Default Behavior Mode is 3D, so newly added PNG/JPG files
    ///   import as plain Textures (textureType = Default). Resources.Load&lt;Sprite&gt;
    ///   returns null for those. This post-processor guarantees Sprite import for
    ///   the runtime-loaded art tree without touching any other textures in the
    ///   project and without hand-authoring 1000+ .meta files.
    ///
    /// SCOPE
    ///   Only assets whose path contains "_ValoCase/Resources/Art/" are affected.
    ///   Everything else imports with its normal project defaults.
    ///
    /// If sprites ever fail to load after a fresh checkout, select the
    /// Assets/_ValoCase/Resources/Art folder and choose "Reimport" — this
    /// post-processor will run again and fix the import settings.
    /// </summary>
    public sealed class ResourcesArtTextureImporter : AssetPostprocessor
    {
        // Unity asset paths always use forward slashes, regardless of OS.
        const string ArtRoot = "_ValoCase/Resources/Art/";

        void OnPreprocessTexture()
        {
            if (assetPath.IndexOf(ArtRoot, System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var importer = (TextureImporter)assetImporter;

            // The critical setting: makes Resources.Load<Sprite> work.
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;

            // Only apply UI-friendly defaults on first import so manual tweaks
            // made later in the Inspector are preserved on subsequent reimports.
            if (importer.importSettingsMissing)
            {
                importer.mipmapEnabled      = false;            // UI sprites need no mips
                importer.alphaIsTransparency = true;
                importer.wrapMode           = TextureWrapMode.Clamp;
                importer.filterMode         = FilterMode.Bilinear;
            }
        }
    }
}
