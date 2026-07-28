using System.IO;
using UnityEditor;
using UnityEngine;
using ValoCase.Core;
using ValoCase.Save;

namespace ValoCase.EditorTools
{
    static class BackendAuthResetMenu
    {
        [MenuItem("ValoCase/Backend/Clear Guest Token (Reset Auth)")]
        static void ClearGuestToken()
        {
            var path = Path.Combine(Application.persistentDataPath, GameConstants.SaveFileName);
            var bakPath = path + ".bak";
            var tmpPath = path + ".tmp";
            var clearedOnDisk = false;

            if (File.Exists(path))
            {
                var data = JsonUtility.FromJson<SaveDataRoot>(File.ReadAllText(path));
                if (data != null)
                {
                    data.guestToken = null;
                    data.guestAccountId = null;
                    File.WriteAllText(path, JsonUtility.ToJson(data, true));
                    clearedOnDisk = true;
                }
            }

            // Delete the backup too so a corrupted-main-file recovery can't resurrect the old token.
            if (File.Exists(bakPath)) File.Delete(bakPath);
            if (File.Exists(tmpPath)) File.Delete(tmpPath);

            var clearedInMemory = false;
            if (Application.isPlaying && GameContext.Instance != null)
            {
                var ctx = GameContext.Instance;
                if (ctx.Save?.Data != null)
                {
                    ctx.Save.Data.guestToken = null;
                    ctx.Save.Data.guestAccountId = null;
                    ctx.Save.Save();
                }
                if (ctx.Backend != null) ctx.Backend.GuestToken = null;
                clearedInMemory = true;
            }

            Debug.Log($"[BackendAuthReset] path={path} clearedOnDisk={clearedOnDisk} clearedInMemory={clearedInMemory}. Enter/re-enter Play mode to register a fresh guest against the new backend.");
        }
    }
}
