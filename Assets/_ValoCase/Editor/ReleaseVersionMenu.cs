using UnityEditor;
using UnityEngine;

namespace ValoCase.EditorTools
{
    /// <summary>
    /// Explicit, idempotent release stamp. Hardcoded on purpose: a "bump by one"
    /// menu run twice would silently skip a version, and Play rejects a reused
    /// versionCode with a far less readable error. Edit the two constants per
    /// release, run the menu once, build.
    /// </summary>
    public static class ReleaseVersionMenu
    {
        // 1.0.29/29, not 1.0.28/28: a code-28 bundle was uploaded to Play's artifact
        // library on 2026-08-17 and withdrawn, and Play never accepts a different file
        // under a used versionCode. The owner stamped the working release as 1.0.29.
        const string Version = "1.0.29";
        const int VersionCode = 29;

        [MenuItem("ValoCase/Release/Stamp Version " + Version)]
        public static void StampVersion()
        {
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = VersionCode;

            // An MCP-triggered build on 2026-08-17 left the Android texture subtarget on
            // PVRTC, which puts <supports-gl-texture pvrtc> into the manifest and makes
            // Play filter out every non-PowerVR device (-9,703 devices). Generic keeps
            // the manifest free of texture claims, like every store build before it.
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.Generic;

            AssetDatabase.SaveAssets();
            Debug.Log($"[Release] bundleVersion={PlayerSettings.bundleVersion} " +
                      $"versionCode={PlayerSettings.Android.bundleVersionCode} " +
                      $"buildAppBundle={EditorUserBuildSettings.buildAppBundle} " +
                      $"textureSubtarget={EditorUserBuildSettings.androidBuildSubtarget}");
        }
    }
}
