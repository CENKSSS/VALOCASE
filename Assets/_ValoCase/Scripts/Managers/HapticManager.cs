using UnityEngine;

namespace ValoCase.Haptics
{
    public enum HapticPattern
    {
        Light,
        Medium,
        Heavy,
        Success,
        UltraReveal
    }

    public sealed class HapticManager : MonoBehaviour
    {
        public static HapticManager Instance { get; private set; }

        [SerializeField] bool hapticsEnabled = true;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Play(HapticPattern pattern)
        {
            if (!hapticsEnabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            TriggerAndroid(pattern);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void TriggerAndroid(HapticPattern pattern)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                var duration = pattern switch
                {
                    HapticPattern.Light => 20L,
                    HapticPattern.Medium => 35L,
                    HapticPattern.Heavy => 55L,
                    HapticPattern.Success => 80L,
                    HapticPattern.UltraReveal => 140L,
                    _ => 25L
                };
                vibrator.Call("vibrate", duration);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Haptics] {ex.Message}");
            }
        }
#endif

        public void SetEnabled(bool enabled) => hapticsEnabled = enabled;
    }
}
