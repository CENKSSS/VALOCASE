using UnityEditor;
using UnityEngine;
using ValoCase.UI;

namespace ValoCase.EditorTools
{
    /// <summary>
    /// Editor-only preview of the mandatory update wall.
    ///
    /// <para>Exists because the wall is the one screen that cannot be reached by playing
    /// the game normally: it only appears when the server names a version newer than this
    /// build, and pointing the real server at a version that is not in the store is exactly
    /// the mistake that walls every live player in front of a download that does not exist.
    /// This shows the same runtime-built panel, from the same code path, without touching
    /// any server value.</para>
    ///
    /// <para>Lives under Editor/ so it is never compiled into a player build. It has no
    /// effect on what ships, and nothing in the game references it.</para>
    /// </summary>
    public static class UpdateWallPreview
    {
        const string PreviewVersion = "1.0.23";

        [MenuItem("ValoCase/Debug/Show Update Wall", false, 100)]
        static void ShowWall()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Enter Play mode first",
                    "The update wall is built at runtime and needs a live Canvas.\n\n" +
                    "Press Play, then run this menu item again.",
                    "OK");
                return;
            }

            // The wall shows at most once per session; clearing that lets it be previewed
            // repeatedly. This forgets that it was shown — it is not a dismiss path, and
            // there is no dismiss path.
            UpdateAvailablePopup.ResetForTests();
            UpdateAvailablePopup.TryShow(PreviewVersion);

            if (GameObject.Find("UpdateAvailablePopup") == null)
            {
                Debug.LogWarning($"[UpdateWallPreview] Nothing was shown. " +
                                 $"IsNewer(\"{PreviewVersion}\", \"{Application.version}\") is false — " +
                                 $"raise PreviewVersion above the running build.");
            }
            else
            {
                Debug.Log($"[UpdateWallPreview] Wall shown — pretending the store serves " +
                          $"{PreviewVersion} while this build is {Application.version}. " +
                          $"UPDATE opens the real store page for {Application.identifier}.");
            }
        }

        [MenuItem("ValoCase/Debug/Hide Update Wall (preview only)", false, 101)]
        static void HideWall()
        {
            // Only the preview can take the wall down. The shipped game has no such path —
            // that is the whole point of the feature.
            var root = GameObject.Find("UpdateAvailablePopup");
            if (root == null)
            {
                Debug.Log("[UpdateWallPreview] No wall is up.");
                return;
            }
            Object.DestroyImmediate(root);
            UpdateAvailablePopup.ResetForTests();
            Debug.Log("[UpdateWallPreview] Preview wall removed.");
        }
    }
}
