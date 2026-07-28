using System.IO;
using UnityEditor;
using UnityEngine;
using ValoCase.Core;
using ValoCase.Profile;
using ValoCase.Save;

namespace ValoCase.EditorTools
{
    static class FirstLaunchProfileResetMenu
    {
        [MenuItem("ValoCase/Profile/Reset First-Launch Setup")]
        static void ResetFirstLaunchSetup()
        {
            var path = Path.Combine(Application.persistentDataPath, GameConstants.SaveFileName);
            var clearedOnDisk = false;

            if (File.Exists(path))
            {
                var data = JsonUtility.FromJson<SaveDataRoot>(File.ReadAllText(path));
                if (data != null)
                {
                    data.profileSetupCompleted = false;
                    data.playerName = "Agent";
                    File.WriteAllText(path, JsonUtility.ToJson(data, true));
                    clearedOnDisk = true;
                }
            }

            PlayerProfileData.ClearSavedProfile();

            var clearedInMemory = false;
            if (Application.isPlaying && GameContext.Instance != null)
            {
                var ctx = GameContext.Instance;
                if (ctx.Save?.Data != null)
                {
                    ctx.Save.Data.profileSetupCompleted = false;
                    ctx.Save.Data.playerName = "Agent";
                    ctx.Save.Save();
                    clearedInMemory = true;
                }
            }

            Debug.Log($"[FirstLaunchProfileReset] path={path} clearedOnDisk={clearedOnDisk} " +
                      $"clearedInMemory={clearedInMemory}. Enter/re-enter Play mode to see the setup popup.");
        }
    }
}
