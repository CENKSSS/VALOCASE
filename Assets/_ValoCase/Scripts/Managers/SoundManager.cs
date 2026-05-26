using System.Collections.Generic;
using UnityEngine;

namespace ValoCase.Audio
{
    public enum SoundId
    {
        UiClick,
        UiBack,
        VpGain,
        VpSpend,
        CaseSpinLoop,
        CaseReveal,
        UltraReveal,
        SellSkin,
        DailyClaim
    }

    [System.Serializable]
    public class SoundEntry
    {
        public SoundId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    public sealed class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [SerializeField] List<SoundEntry> sounds = new();
        [SerializeField] AudioSource musicSource;
        [SerializeField] AudioSource sfxSource;
        [SerializeField] bool sfxEnabled = true;
        [SerializeField] bool musicEnabled = true;

        Dictionary<SoundId, SoundEntry> _lookup;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _lookup = new Dictionary<SoundId, SoundEntry>();
            foreach (var s in sounds)
            {
                if (s != null) _lookup[s.id] = s;
            }
        }

        public void Play(SoundId id)
        {
            if (!sfxEnabled || sfxSource == null) return;
            if (!_lookup.TryGetValue(id, out var entry) || entry.clip == null) return;
            sfxSource.PlayOneShot(entry.clip, entry.volume);
        }

        public void SetSfxEnabled(bool enabled) => sfxEnabled = enabled;
        public void SetMusicEnabled(bool enabled)
        {
            musicEnabled = enabled;
            if (musicSource != null) musicSource.mute = !enabled;
        }
    }
}
