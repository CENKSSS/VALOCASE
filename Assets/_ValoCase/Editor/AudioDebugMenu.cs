using UnityEditor;
using ValoCase.Audio;

namespace ValoCase.EditorTools
{
    static class AudioDebugMenu
    {
        [MenuItem("Tools/Audio Debug/Diagnose")]
        static void Diagnose() => AudioDebug.Diagnose();

        [MenuItem("Tools/Audio Debug/Play ButtonClick")]
        static void PlayButton() => AudioDebug.PlayButton();

        [MenuItem("Tools/Audio Debug/Play CaseOpen")]
        static void PlayCase() => AudioDebug.PlayCase();

        [MenuItem("Tools/Audio Debug/Report Probe")]
        static void Report() => AudioDebug.ReportProbe();

        [MenuItem("Tools/Audio Debug/Unmute Editor Audio")]
        static void UnmuteEditor() => EditorUtility.audioMasterMute = false;
    }
}
