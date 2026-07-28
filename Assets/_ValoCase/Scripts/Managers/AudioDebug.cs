using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ValoCase.Audio
{
    // Manual audio diagnostic. Trigger from the Tools/Audio Debug editor menu.
    // Writes to AudioDebug.log at the project root and to the console. Nothing
    // here runs on its own — it is inert until a method is called.
    public static class AudioDebug
    {
        static AudioSource _probe;
        static readonly string LogPath = Path.Combine(Application.dataPath, "..", "AudioDebug.log");

        static void Reset() { try { File.WriteAllText(LogPath, ""); } catch { } }

        static void Emit(string s)
        {
            Debug.Log(s);
            try { File.AppendAllText(LogPath, s + "\n"); } catch { }
        }

        public static void Diagnose()
        {
            Reset();
            var sb = new StringBuilder();
            sb.AppendLine("===== AUDIO DIAGNOSE =====");

            var sm = SoundManager.Instance;
            sb.AppendLine("SoundManager.Instance null=" + (sm == null));
            sb.AppendLine("SoundManager objects=" + Object.FindObjectsByType<SoundManager>(FindObjectsSortMode.None).Length);
            if (sm != null) sb.AppendLine("SoundManager state: " + sm.DebugState());

            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            sb.AppendLine("AudioListener count=" + listeners.Length);
            foreach (var l in listeners)
                sb.AppendLine("  listener '" + l.gameObject.name + "' enabled=" + l.enabled + " active=" + l.gameObject.activeInHierarchy);
            sb.AppendLine("AudioListener.volume=" + AudioListener.volume + " pause=" + AudioListener.pause);

            var cfg = AudioSettings.GetConfiguration();
            sb.AppendLine("AudioSettings sampleRate=" + cfg.sampleRate + " speakerMode=" + cfg.speakerMode +
                          " dspBufferSize=" + cfg.dspBufferSize + " outputSampleRate=" + AudioSettings.outputSampleRate);
            sb.AppendLine("EditorAudioMute=" + ReadEditorMute());

            var ca = Resources.Load<AudioClip>("Audio/caseopen");
            var cb = Resources.Load<AudioClip>("Audio/buttonclick");
            sb.AppendLine("caseopen null=" + (ca == null) + Describe(ca));
            sb.AppendLine("buttonclick null=" + (cb == null) + Describe(cb));

            sb.AppendLine("PlayerPrefs has(valocase_sound_muted)=" + PlayerPrefs.HasKey("valocase_sound_muted") +
                          " value=" + PlayerPrefs.GetInt("valocase_sound_muted", -1));
            sb.AppendLine("SoundManager.Muted=" + (sm != null ? sm.Muted.ToString() : "n/a"));

            Emit(sb.ToString());
        }

        // The Game-view "Mute Audio" toggle silences all play-mode audio; read it via
        // reflection since this is runtime code (returns "n/a" in a player build).
        static string ReadEditorMute()
        {
            var p = System.Type.GetType("UnityEditor.EditorUtility, UnityEditor")
                ?.GetProperty("audioMasterMute", BindingFlags.Public | BindingFlags.Static);
            return p != null ? p.GetValue(null).ToString() : "n/a";
        }

        static string Describe(AudioClip c) => c == null ? ""
            : " name=" + c.name + " len=" + c.length.ToString("F3") + " freq=" + c.frequency +
              " ch=" + c.channels + " samples=" + c.samples + " loadState=" + c.loadState;

        public static void PlayButton() => PlayProbe("Audio/buttonclick");
        public static void PlayCase()   => PlayProbe("Audio/caseopen");

        static void PlayProbe(string path)
        {
            if (Object.FindFirstObjectByType<AudioListener>() == null)
            {
                var lgo = new GameObject("AudioDebug_Listener");
                Object.DontDestroyOnLoad(lgo);
                lgo.AddComponent<AudioListener>();
                Emit("added missing AudioListener");
            }

            if (_probe == null)
            {
                var go = new GameObject("AudioDebug_Probe");
                Object.DontDestroyOnLoad(go);
                _probe = go.AddComponent<AudioSource>();
            }

            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) { Emit("PROBE clip NULL for " + path); return; }
            clip.LoadAudioData();

            _probe.clip = clip;
            _probe.volume = 1f;
            _probe.mute = false;
            _probe.spatialBlend = 0f;
            _probe.pitch = 1f;
            _probe.outputAudioMixerGroup = null;
            _probe.Play();

            Emit("PROBE PLAY path=" + path + " clip=" + clip.name + " loadState=" + clip.loadState +
                 " isPlaying=" + _probe.isPlaying + " vol=" + _probe.volume + " mute=" + _probe.mute +
                 " blend=" + _probe.spatialBlend + " pitch=" + _probe.pitch +
                 " listenerVol=" + AudioListener.volume + " listenerPause=" + AudioListener.pause);
        }

        public static void ReportProbe()
        {
            if (_probe == null) { Emit("PROBE null (not started)"); return; }
            Emit("PROBE REPORT isPlaying=" + _probe.isPlaying + " time=" + _probe.time.ToString("F3") +
                 " clip=" + (_probe.clip != null ? _probe.clip.name : "null") + " vol=" + _probe.volume);
        }
    }
}
