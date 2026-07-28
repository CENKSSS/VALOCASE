using System;
using System.Collections;
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
        DailyClaim,
        Success,
        Failed,
        CoinDrops
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

        const string MutePref     = "valocase_sound_muted";
        const float  TickVolume   = 0.5f;
        const float  CaseFadeTime = 0.18f;
        const float  CoinFadeTime = 0.25f;

        [SerializeField] List<SoundEntry> sounds = new();
        [SerializeField] AudioSource musicSource;
        [SerializeField] AudioSource sfxSource;
        [SerializeField] bool sfxEnabled = true;
        [SerializeField] bool musicEnabled = true;
        [SerializeField] bool playCaseOpenLoop = false;

        AudioSource _caseSource;
        AudioSource _coinSource;
        Coroutine   _caseFade;
        Coroutine   _coinFade;
        Dictionary<SoundId, SoundEntry> _lookup;
        bool _muted;

        public static event Action<bool> OnMuteChanged;
        public bool Muted => _muted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject("SoundManager").AddComponent<SoundManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // The project ships without an AudioListener in any scene/prefab, so without
            // this nothing would ever be audible. Only add one when none exists.
            if (FindFirstObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();

            sfxSource  = ConfigureSource(sfxSource);
            _caseSource = ConfigureSource(null);
            _coinSource = ConfigureSource(null);

            _lookup = new Dictionary<SoundId, SoundEntry>();
            foreach (var s in sounds)
                if (s != null) _lookup[s.id] = s;

            // WAVs live under Assets/_ValoCase/Resources/Audio. Missing files load as
            // null and simply never play — the game keeps running.
            LoadResourceClip(SoundId.CaseSpinLoop, "Audio/caseopen");
            LoadResourceClip(SoundId.UiClick, "Audio/buttonclick");
            LoadResourceClip(SoundId.Success, "Audio/success");
            LoadResourceClip(SoundId.Failed, "Audio/failed");
            LoadResourceClip(SoundId.CoinDrops, "Audio/coindrops");

            _muted = PlayerPrefs.GetInt(MutePref, 0) == 1;
            ApplyMute();
        }

        AudioSource ConfigureSource(AudioSource src)
        {
            if (src == null) src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake   = false;
            src.spatialBlend  = 0f;
            src.volume        = 1f;
            src.mute          = false;
            src.loop          = false;
            return src;
        }

        void LoadResourceClip(SoundId id, string resourcePath)
        {
            if (_lookup.TryGetValue(id, out var existing) && existing.clip != null) return;
            var clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null) { Debug.LogWarning($"[SoundManager] missing audio clip: Resources/{resourcePath}"); return; }
            clip.LoadAudioData();   // decode up front so the first Play has no latency
            _lookup[id] = new SoundEntry { id = id, clip = clip, volume = 1f };
        }

        public void Play(SoundId id)
        {
            if (_muted || !sfxEnabled || sfxSource == null || _lookup == null) return;
            if (!_lookup.TryGetValue(id, out var entry) || entry.clip == null) return;
            sfxSource.PlayOneShot(entry.clip, entry.volume);
        }

        public void PlayButtonClick() => Play(SoundId.UiClick);
        public void PlaySuccess()     => Play(SoundId.Success);
        public void PlayFailed()      => Play(SoundId.Failed);

        public IEnumerator WaitForCoinDropsReady()
        {
            if (_lookup == null) yield break;
            if (!_lookup.TryGetValue(SoundId.CoinDrops, out var entry) || entry.clip == null) yield break;

            if (entry.clip.loadState == AudioDataLoadState.Unloaded)
                entry.clip.LoadAudioData();

            while (entry.clip.loadState == AudioDataLoadState.Loading)
                yield return null;
        }

        // Starts the coin count-up sound on its own source and returns the clip length
        // (0 if missing) so the caller can fit the count-up duration to it. Returns the
        // length even when muted so visual pacing stays the same with sound off.
        public float PlayCoinDrops()
        {
            if (_coinSource == null || _lookup == null) return 0f;
            if (!_lookup.TryGetValue(SoundId.CoinDrops, out var entry) || entry.clip == null) return 0f;
            if (_muted || !sfxEnabled) return entry.clip.length;

            if (_coinFade != null) { StopCoroutine(_coinFade); _coinFade = null; }
            _coinSource.Stop();
            _coinSource.clip         = entry.clip;
            _coinSource.time         = 0f;
            _coinSource.volume       = entry.volume;
            _coinSource.pitch        = 1f;
            _coinSource.spatialBlend = 0f;
            _coinSource.mute         = false;
            _coinSource.Play();
            return entry.clip.length;
        }

        public void StopCoinDrops()
        {
            if (_coinSource == null || !_coinSource.isPlaying) return;
            if (_coinFade != null) StopCoroutine(_coinFade);
            _coinFade = StartCoroutine(FadeAndStop(_coinSource, CoinFadeTime));
        }

        // Short roulette tick — reuses the button-click clip at reduced volume.
        public void PlayTick()
        {
            if (_muted || !sfxEnabled || sfxSource == null || _lookup == null) return;
            if (!_lookup.TryGetValue(SoundId.UiClick, out var entry) || entry.clip == null) return;
            sfxSource.PlayOneShot(entry.clip, entry.volume * TickVolume);
        }

        // Plays the case-opening clip at natural pitch on its own source. The spin is
        // ended with StopCaseOpen (a short fade), so the clip never needs time-stretching.
        public void PlayCaseOpen()
        {
            if (!playCaseOpenLoop) return;
            if (_muted || !sfxEnabled || _caseSource == null || _lookup == null) return;
            if (!_lookup.TryGetValue(SoundId.CaseSpinLoop, out var entry) || entry.clip == null) return;

            if (_caseFade != null) { StopCoroutine(_caseFade); _caseFade = null; }
            _caseSource.pitch  = 1f;
            _caseSource.clip   = entry.clip;
            _caseSource.volume = entry.volume;
            _caseSource.Play();
        }

        public void StopCaseOpen()
        {
            if (_caseSource == null || !_caseSource.isPlaying) return;
            if (_caseFade != null) StopCoroutine(_caseFade);
            _caseFade = StartCoroutine(FadeAndStop(_caseSource, CaseFadeTime));
        }

        IEnumerator FadeAndStop(AudioSource src, float dur)
        {
            float start = src.volume;
            float t = 0f;
            while (t < dur && src != null && src.isPlaying)
            {
                t += Time.unscaledDeltaTime;
                src.volume = Mathf.Lerp(start, 0f, t / dur);
                yield return null;
            }
            if (src != null) { src.Stop(); src.volume = 1f; }
        }

        public void SetMuted(bool muted)
        {
            if (_muted == muted) return;
            _muted = muted;
            PlayerPrefs.SetInt(MutePref, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMute();
            OnMuteChanged?.Invoke(muted);
        }

        public void ToggleMute() => SetMuted(!_muted);

        void ApplyMute()
        {
            if (sfxSource   != null) sfxSource.mute   = _muted;
            if (_caseSource != null) _caseSource.mute = _muted;
            if (_coinSource != null) _coinSource.mute = _muted;
            if (musicSource != null) musicSource.mute = _muted || !musicEnabled;
        }

        public string DebugState()
        {
            string Src(string n, AudioSource s) => s == null ? n + "=null"
                : $"{n} enabled={s.enabled} active={s.gameObject.activeInHierarchy} mute={s.mute} vol={s.volume} pitch={s.pitch} blend={s.spatialBlend} mixer={(s.outputAudioMixerGroup != null)} playing={s.isPlaying}";
            return $"sfxEnabled={sfxEnabled} musicEnabled={musicEnabled} | {Src("sfx", sfxSource)} | {Src("case", _caseSource)}";
        }

        public void SetSfxEnabled(bool enabled) => sfxEnabled = enabled;
        public void SetMusicEnabled(bool enabled)
        {
            musicEnabled = enabled;
            ApplyMute();
        }
    }
}
