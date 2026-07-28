using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using UnityEngine;
using ValoCase.Core;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// Persistent, single-instance app-session presence tracker for the V76 analytics
    /// lifecycle API. Reports start / heartbeat / pause / resume / end through the existing
    /// <see cref="BackendApiClient"/> (same base URL, X-Guest-Token auth, timeout, JSON).
    /// All durations and online state stay server-authoritative — this client only reports
    /// foreground transitions. Every request is best-effort: a failure never blocks or
    /// surfaces to gameplay.
    ///
    /// Bootstrapped code-only (no scene/prefab edits) via RuntimeInitializeOnLoadMethod, so
    /// it survives scene loads and cannot be duplicated.
    /// </summary>
    public sealed class AnalyticsLifecycleService : MonoBehaviour
    {
        const string InstallationIdPrefKey = "valocase_installation_id";
        const float HeartbeatIntervalSeconds = 30f;
        const int MaxStartResumeAttempts = 3;
        const float StartResumeRetryBaseSeconds = 2f;
        const float StartRetryCooldownSeconds = 30f;
        const string EndReasonQuit = "QUIT";   // V76 SessionEndReason.fromClient accepts only QUIT/LOGOUT

        static AnalyticsLifecycleService _instance;

        string _installationId;
        string _clientSessionId;
        string _platform;
        long _sequence;

        bool _foreground = true;   // app starts foregrounded; authoritative internal state
        bool _started;             // server session start acknowledged
        bool _startInFlight;
        bool _resumeInFlight;
        bool _heartbeatInFlight;
        string _activeToken;       // token the current server session was started with
        string _startBlockedToken; // token rejected with a 4xx; do not retry until it changes
        float _heartbeatTimer;
        float _nextStartAttemptTime;
        int _generation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("[AnalyticsLifecycle]");
            go.AddComponent<AnalyticsLifecycleService>();
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _installationId = LoadOrCreateInstallationId();
            _clientSessionId = Guid.NewGuid().ToString();
            _platform = DetectPlatform();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        static BackendApiClient Backend =>
            GameContext.Instance != null ? GameContext.Instance.Backend : null;

        static string CurrentToken()
        {
            var backend = Backend;
            return backend != null ? backend.GuestToken : null;
        }

        void Update()
        {
            var token = CurrentToken();

            if (_started)
            {
                if (!string.IsNullOrEmpty(token) && token != _activeToken)
                {
                    RotateSession();
                    return;
                }
                if (string.IsNullOrEmpty(token)) return;   // token lost; pause reporting until it recovers
                if (!_foreground) return;

                _heartbeatTimer += Time.unscaledDeltaTime;
                if (_heartbeatTimer >= HeartbeatIntervalSeconds && !_heartbeatInFlight)
                {
                    _heartbeatTimer = 0f;
                    StartCoroutine(SendHeartbeat());
                }
                return;
            }

            if (_startInFlight || _resumeInFlight) return;
            if (!_foreground) return;
            if (string.IsNullOrEmpty(token)) return;
            if (token == _startBlockedToken) return;
            if (Time.unscaledTime < _nextStartAttemptTime) return;
            StartCoroutine(SendStartWithRetry());
        }

        void OnApplicationPause(bool paused) => SetForeground(!paused);

        void SetForeground(bool value)
        {
            if (_foreground == value) return;
            _foreground = value;
            if (value) HandleResume();
            else HandlePause();
        }

        void HandlePause()
        {
            _heartbeatTimer = 0f;
            if (_started && !string.IsNullOrEmpty(CurrentToken()))
                StartCoroutine(SendPause());
        }

        void HandleResume()
        {
            _heartbeatTimer = 0f;
            if (_started && !_startInFlight && !_resumeInFlight && !string.IsNullOrEmpty(CurrentToken()))
                StartCoroutine(SendResumeWithRetry());
        }

        void OnApplicationQuit()
        {
            if (!_started || string.IsNullOrEmpty(CurrentToken())) return;
            if (Application.internetReachability == NetworkReachability.NotReachable) return;
            StartCoroutine(SendEnd());   // best-effort; app teardown may interrupt it
        }

        // Old server session is abandoned rather than ended: we cannot attach the previous
        // account's token without corrupting other in-flight requests, so the backend
        // timeout closes it. A fresh client session starts for the new account.
        void RotateSession()
        {
            _generation++;
            _started = false;
            _startInFlight = false;
            _resumeInFlight = false;
            _startBlockedToken = null;
            _nextStartAttemptTime = 0f;
            _heartbeatTimer = 0f;
            _heartbeatInFlight = false;
            _activeToken = null;
            _clientSessionId = Guid.NewGuid().ToString();
            Interlocked.Exchange(ref _sequence, 0);
        }

        void RecoverMissingSession(int generation)
        {
            if (generation != _generation) return;
            _generation++;
            _started = false;
            _startInFlight = false;
            _resumeInFlight = false;
            _heartbeatInFlight = false;
            _activeToken = null;
            _startBlockedToken = null;
            _heartbeatTimer = 0f;
            _nextStartAttemptTime = 0f;
        }

        IEnumerator SendStartWithRetry()
        {
            _startInFlight = true;
            var generation = _generation;
            var token = CurrentToken();
            var backoff = StartResumeRetryBaseSeconds;
            try
            {
                for (int attempt = 1; attempt <= MaxStartResumeAttempts; attempt++)
                {
                    if (generation != _generation || !_foreground || string.IsNullOrEmpty(token) || CurrentToken() != token)
                        break;

                    bool accepted = false, rejected = false;
                    yield return SendOnce((ok, err) => Backend.PostSessionStart(NewStartRequest(), ok, err),
                        (a, r, ack) => { accepted = a; rejected = r; });

                    if (generation != _generation || CurrentToken() != token)
                        break;

                    if (accepted)
                    {
                        _started = true;
                        _activeToken = token;
                        _startBlockedToken = null;
                        _heartbeatTimer = 0f;
                        if (!_foreground) StartCoroutine(SendPause());
                        yield break;
                    }
                    if (rejected)
                    {
                        _startBlockedToken = token;
                        yield break;
                    }
                    if (attempt < MaxStartResumeAttempts)
                    {
                        yield return new WaitForSecondsRealtime(backoff);
                        backoff *= 2f;
                    }
                }

                if (generation != _generation) yield break;
                var currentToken = CurrentToken();
                if (!string.IsNullOrEmpty(currentToken) && currentToken != token)
                {
                    RotateSession();
                    yield break;
                }
                _nextStartAttemptTime = Time.unscaledTime + StartRetryCooldownSeconds;
            }
            finally
            {
                if (generation == _generation) _startInFlight = false;
            }
        }

        IEnumerator SendResumeWithRetry()
        {
            if (_startInFlight || _resumeInFlight) yield break;
            _resumeInFlight = true;
            var generation = _generation;
            var token = CurrentToken();
            var backoff = StartResumeRetryBaseSeconds;
            try
            {
                for (int attempt = 1; attempt <= MaxStartResumeAttempts; attempt++)
                {
                    if (generation != _generation || !_started || !_foreground || string.IsNullOrEmpty(token) || CurrentToken() != token)
                        break;

                    bool accepted = false, rejected = false, missing = false;
                    yield return SendOnce((ok, err) => Backend.PostSessionResume(NewStartRequest(), ok, err),
                        (a, r, ack) => { accepted = a; rejected = r; missing = IsNone(ack); });

                    if (generation != _generation || CurrentToken() != token)
                        break;

                    if (missing)
                    {
                        RecoverMissingSession(generation);
                        yield break;
                    }

                    if (accepted || rejected) yield break;
                    if (attempt < MaxStartResumeAttempts)
                    {
                        yield return new WaitForSecondsRealtime(backoff);
                        backoff *= 2f;
                    }
                }
            }
            finally
            {
                if (generation == _generation) _resumeInFlight = false;
            }
        }

        IEnumerator SendPause()
        {
            var generation = _generation;
            var token = CurrentToken();
            bool missing = false;
            yield return SendOnce((ok, err) => Backend.PostSessionPause(NewSignalRequest(), ok, err),
                (accepted, rejected, ack) => missing = IsNone(ack));
            if (generation == _generation && CurrentToken() == token && missing)
                RecoverMissingSession(generation);
        }

        IEnumerator SendHeartbeat()
        {
            var generation = _generation;
            var token = CurrentToken();
            _heartbeatInFlight = true;
            try
            {
                bool missing = false;
                yield return SendOnce((ok, err) => Backend.PostSessionHeartbeat(NewSignalRequest(), ok, err),
                    (accepted, rejected, ack) => missing = IsNone(ack));
                if (generation != _generation) yield break;
                if (CurrentToken() == token && missing)
                    RecoverMissingSession(generation);
            }
            finally
            {
                if (generation == _generation) _heartbeatInFlight = false;
            }
        }

        IEnumerator SendEnd()
        {
            var body = new AnalyticsEndRequest
            {
                clientSessionId = _clientSessionId,
                clientSentAtUtc = UtcNowIso(),
                lifecycleSequence = NextSequence(),
                endReason = EndReasonQuit
            };
            yield return SendOnce((ok, err) => Backend.PostSessionEnd(body, ok, err), null);
        }

        // One send attempt. Reports (accepted, clientRejected). A 2xx with an empty body
        // parses to null in the shared pipeline and surfaces as a 2xx-status error, so it is
        // treated as accepted. Offline is skipped up front (not a client rejection).
        IEnumerator SendOnce(Func<Action<AnalyticsAckResponse>, Action<BackendError>, IEnumerator> call,
                             Action<bool, bool, AnalyticsAckResponse> onResult)
        {
            var backend = Backend;
            if (backend == null || Application.internetReachability == NetworkReachability.NotReachable)
            {
                onResult?.Invoke(false, false, null);
                yield break;
            }

            AnalyticsAckResponse ok = null;
            BackendError err = null;
            yield return call(r => ok = r, e => err = e);

            bool accepted = ok != null || (err != null && err.HttpStatus >= 200 && err.HttpStatus < 300);
            bool rejected = err != null && err.HttpStatus >= 400 && err.HttpStatus < 500 && err.HttpStatus != 409;
            onResult?.Invoke(accepted, rejected, ok);
        }

        static bool IsNone(AnalyticsAckResponse ack) =>
            ack != null && string.Equals(ack.lifecycleState, "NONE", StringComparison.OrdinalIgnoreCase);

        AnalyticsLifecycleRequest NewStartRequest() => new AnalyticsLifecycleRequest
        {
            clientSessionId = _clientSessionId,
            installationId = _installationId,
            appVersion = Application.version,
            platform = _platform,
            clientSentAtUtc = UtcNowIso(),
            lifecycleSequence = NextSequence()
        };

        AnalyticsSignalRequest NewSignalRequest() => new AnalyticsSignalRequest
        {
            clientSessionId = _clientSessionId,
            clientSentAtUtc = UtcNowIso(),
            lifecycleSequence = NextSequence()
        };

        long NextSequence() => Interlocked.Increment(ref _sequence);

        static string UtcNowIso() =>
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        static string LoadOrCreateInstallationId()
        {
            var existing = PlayerPrefs.GetString(InstallationIdPrefKey, string.Empty);
            if (Guid.TryParse(existing, out var parsed) && parsed != Guid.Empty)
                return parsed.ToString();
            var id = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(InstallationIdPrefKey, id);
            PlayerPrefs.Save();
            return id;
        }

        static string DetectPlatform()
        {
#if UNITY_EDITOR
            return "EDITOR";
#elif UNITY_ANDROID
            return "ANDROID";
#elif UNITY_IOS
            return "IOS";
#else
            return "UNKNOWN";
#endif
        }
    }
}
