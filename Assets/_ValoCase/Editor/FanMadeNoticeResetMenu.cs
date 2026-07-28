using UnityEditor;
using UnityEngine;
using ValoCase.UI;

namespace ValoCase.EditorTools
{
    static class FanMadeNoticeResetMenu
    {
        [MenuItem("ValoCase/Legal/Reset Fan-Made Notice")]
        static void ResetFanMadeNotice()
        {
            bool had = PlayerPrefs.HasKey(FanMadeNoticePopup.AcceptedKey);
            PlayerPrefs.DeleteKey(FanMadeNoticePopup.AcceptedKey);
            PlayerPrefs.Save();
            Debug.Log($"[FanMadeNoticeReset] cleared={had}. Re-enter Play mode / relaunch to see the notice again.");
        }
    }
}
