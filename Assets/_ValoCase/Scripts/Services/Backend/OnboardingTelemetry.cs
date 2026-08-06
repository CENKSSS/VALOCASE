using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using ValoCase.Core;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// Reports the pre-account onboarding funnel to POST /api/v1/telemetry/onboarding.
    ///
    /// Everything that happens before a nickname is confirmed is otherwise invisible: an
    /// install that never opened the app, a player who quit at the legal notice, one who
    /// quit at the nickname screen, and one whose nickname the client itself refused all
    /// leave exactly the same evidence, which is none. These nine events tell those
    /// outcomes apart.
    ///
    /// Three rules govern the implementation, in this order of importance:
    ///
    ///   1. It cannot affect the player. <see cref="Emit"/> only appends to a queue and
    ///      returns; every send happens on a coroutine, every failure is swallowed, and
    ///      nothing here is ever awaited by registration or by gameplay. The endpoint is
    ///      built to agree — it answers 202 even when its own storage fails.
    ///   2. It cannot grow without bound. The queue is capped, retries are capped, and
    ///      only transient failures are retried at all. A 400 or a 404 is dropped, since
    ///      resending an identical body cannot produce a different answer.
    ///   3. It cannot leak. The payload is a fixed set of fields (see
    ///      <see cref="OnboardingEventRequest"/>): no nickname, no guest or auth token,
    ///      no email, no advertising id. Emit's signature is the whole surface, so there
    ///      is no call shape that could pass one.
    ///
    /// Retries reuse the same eventId, which is the backend's idempotency key, so a send
    /// that timed out after the server stored it does not double-count the step.
    ///
    /// Deliberately NOT compiled out of release builds. This measures live players; a
    /// UNITY_EDITOR or DEVELOPMENT_BUILD guard anywhere on the send path would make it
    /// report only from machines that never had the problem. Only the verbose logging
    /// below is dev-only.
    /// </summary>
    public sealed class OnboardingTelemetry : MonoBehaviour
    {
        // Wire names from the backend's OnboardingEventName allowlist. Anything else is
        // a 400 and is never stored.
        public const string AppLaunched             = "app_launched";
        public const string FanNoticeShown          = "fan_notice_shown";
        public const string FanNoticeAccepted       = "fan_notice_accepted";
        public const string NicknameScreenShown     = "nickname_screen_shown";
        public const string NicknameConfirmClicked  = "nickname_confirm_clicked";
        public const string NicknameRejected        = "nickname_rejected";
        public const string RegistrationAttempted   = "registration_attempted";
        public const string RegistrationFailed      = "registration_failed";
        public const string RegistrationSucceeded   = "registration_succeeded";

        /// <summary>
        /// Queue bound. The funnel is nine events long, so anything past this means the
        /// backend has been unreachable for the whole session and the extra events are
        /// worth less than the memory.
        /// </summary>
        const int MaxQueuedEvents = 32;

        /// <summary>Send attempts per event before it is dropped.</summary>
        const int MaxAttemptsPerEvent = 4;

        const float RetryBaseSeconds = 2f;

        /// <summary>
        /// How long a queued event waits for the backend client to exist. In local
        /// editor mode it never will, so the wait has to end rather than spin.
        /// </summary>
        const float MaxWaitForClientSeconds = 20f;

        static OnboardingTelemetry _instance;
        static bool _quitting;

        readonly Queue<OnboardingEventRequest> _queue = new Queue<OnboardingEventRequest>();
        bool _draining;
        int _droppedCount;

        /// <summary>
        /// Set whenever the queue changes; cleared once it has been written to disk. The
        /// write is coalesced through <see cref="PersistPump"/> rather than done inline so
        /// a burst of Emits during boot produces one file write instead of nine.
        /// </summary>
        bool _dirty;

        /// <summary>
        /// How long a change may sit unwritten. Short enough that a crash a second after
        /// launch still keeps the event, long enough that the boot burst coalesces.
        /// </summary>
        const float PersistIntervalSeconds = 1f;

        /// <summary>Whether the disk queue has been restored into this instance yet.</summary>
        bool _restored;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Records one funnel step. Returns immediately; never throws, never blocks, and
        /// never reports a failure to the caller — a caller that could react to a
        /// telemetry problem would be a caller telemetry can break.
        /// </summary>
        /// <param name="rejectionReason">
        /// Only read for <see cref="NicknameRejected"/>. Use
        /// <see cref="NicknameValidator.WireName"/>.
        /// </param>
        /// <param name="networkErrorCategory">
        /// Only read for <see cref="RegistrationFailed"/>. Use
        /// <see cref="BackendErrorMapper.NetworkCategory"/>.
        /// </param>
        /// <param name="httpStatus">
        /// Only read for <see cref="RegistrationFailed"/>. 0 when there was no HTTP
        /// response; the backend discards anything outside 100-599.
        /// </param>
        public static void Emit(string eventName,
                                string rejectionReason = null,
                                string networkErrorCategory = null,
                                int httpStatus = 0)
        {
            try
            {
                if (_quitting || string.IsNullOrEmpty(eventName)) return;

                var instance = EnsureInstance();
                if (instance == null) return;

                instance.Enqueue(new OnboardingEventRequest
                {
                    installationId       = ClientIdentity.InstallationId,
                    eventId              = Guid.NewGuid().ToString(),
                    eventName            = eventName,
                    clientTimestampUtc   = UtcNowIso(),
                    appVersion           = Application.version,
                    platform             = ClientIdentity.Platform,
                    rejectionReason      = rejectionReason ?? string.Empty,
                    networkErrorCategory = networkErrorCategory ?? string.Empty,
                    httpStatus           = httpStatus
                });
            }
            catch (Exception e)
            {
                // A funnel measurement must not be able to take down the funnel.
                Debug.LogWarning("[OnboardingTelemetry] Emit failed and was dropped: " + e.Message);
            }
        }

        // ── Lifetime ──────────────────────────────────────────────────────────

        static OnboardingTelemetry EnsureInstance()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null;

            var go = new GameObject("[OnboardingTelemetry]");
            _instance = go.AddComponent<OnboardingTelemetry>();
            DontDestroyOnLoad(go);
            return _instance;
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

            // A fresh instance means a fresh run. The flag is static and survives a domain
            // reload in the editor, where a stale true would leave the pump exiting on its
            // first tick and nothing ever reaching disk.
            _quitting = false;

            RestoreFromDisk();
            StartCoroutine(PersistPump());
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// Loads whatever the previous run could not deliver, ahead of anything this run
        /// emits. Order matters: a crash-loop device would otherwise keep reporting its
        /// newest launch and never the launch that explains the crash.
        /// </summary>
        void RestoreFromDisk()
        {
            if (_restored) return;
            _restored = true;
            try
            {
                var pending = OnboardingTelemetryStore.Load();
                if (pending.Count == 0) return;

                var carried = new Queue<OnboardingEventRequest>(pending);
                while (_queue.Count > 0) carried.Enqueue(_queue.Dequeue());
                while (carried.Count > 0) _queue.Enqueue(carried.Dequeue());

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[OnboardingTelemetry] restored {pending.Count} pending event(s) from disk.");
#endif
                if (!_draining) StartCoroutine(Drain());
            }
            catch (Exception e)
            {
                // Restoring is a best-effort improvement on losing the events entirely.
                Debug.LogWarning("[OnboardingTelemetry] restore failed and was skipped: " + e.Message);
            }
        }

        /// <summary>
        /// Writes the queue at most once per <see cref="PersistIntervalSeconds"/>, and only
        /// when something changed. Runs for the life of the app; the cost when idle is one
        /// bool check per second.
        /// </summary>
        IEnumerator PersistPump()
        {
            var wait = new WaitForSecondsRealtime(PersistIntervalSeconds);
            while (!_quitting)
            {
                yield return wait;
                if (_dirty) PersistNow();
            }
        }

        /// <summary>
        /// Writes the queue synchronously. Small by construction — at most
        /// <see cref="MaxQueuedEvents"/> flat records — so this is a sub-millisecond write
        /// rather than work that needs a thread.
        /// </summary>
        void PersistNow()
        {
            _dirty = false;
            OnboardingTelemetryStore.Save(_queue);
        }

        // The last moment the OS reliably gives us on mobile. A player who swipes the app
        // away, or an OS that kills a backgrounded process, never reaches OnApplicationQuit,
        // so the pause hook is the one that actually saves those events.
        void OnApplicationPause(bool paused)
        {
            if (paused && _dirty) PersistNow();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && _dirty) PersistNow();
        }

        // Stops a send being started during teardown, when coroutines will not finish
        // and Unity APIs are unreliable. The queue is written before that flag is set:
        // anything still undelivered belongs to the next launch.
        void OnApplicationQuit()
        {
            if (_dirty) PersistNow();
            _quitting = true;
        }

        // ── Queue ─────────────────────────────────────────────────────────────

        void Enqueue(OnboardingEventRequest request)
        {
            // Oldest first: the newer step is the more informative one, because reaching
            // it implies the earlier steps happened.
            while (_queue.Count >= MaxQueuedEvents)
            {
                _queue.Dequeue();
                _droppedCount++;
            }
            _queue.Enqueue(request);
            _dirty = true;

            if (!_draining) StartCoroutine(Drain());
        }

        IEnumerator Drain()
        {
            _draining = true;
            try
            {
                while (_queue.Count > 0 && !_quitting)
                {
                    var request = _queue.Peek();
                    yield return SendWithRetry(request);
                    // Delivered, permanently refused, or out of attempts — either way it
                    // leaves the queue, so a stuck event cannot block the ones behind it.
                    if (_queue.Count > 0 && ReferenceEquals(_queue.Peek(), request))
                    {
                        _queue.Dequeue();
                        _dirty = true;
                    }
                }
            }
            finally
            {
                _draining = false;
                // Write the shrunken queue now rather than waiting for the pump. An empty
                // one deletes the file, so the ordinary case — everything delivered during
                // this launch — leaves no state behind for the next one to reason about.
                if (_dirty && !_quitting) PersistNow();
            }
        }

        IEnumerator SendWithRetry(OnboardingEventRequest request)
        {
            var backoff = RetryBaseSeconds;

            for (int attempt = 1; attempt <= MaxAttemptsPerEvent; attempt++)
            {
                if (_quitting) yield break;

                var client = ResolveClient();
                if (client == null)
                {
                    // Backend client not built yet (early boot), or never will be
                    // (local editor mode). Wait once, then give up on this event.
                    float waited = 0f;
                    while (client == null && waited < MaxWaitForClientSeconds && !_quitting)
                    {
                        yield return new WaitForSecondsRealtime(1f);
                        waited += 1f;
                        client = ResolveClient();
                    }
                    if (client == null) yield break;
                }

                OnboardingEventAck ack = null;
                BackendError error = null;
                yield return client.PostOnboardingEvent(request, r => ack = r, e => error = e);

                if (ack != null || error == null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[OnboardingTelemetry] {request.eventName} -> {(ack != null ? ack.result : "accepted")}");
#endif
                    yield break;
                }

                if (!IsTransient(error))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"[OnboardingTelemetry] {request.eventName} refused (status={error.HttpStatus}); not retrying.");
#endif
                    yield break;
                }

                if (attempt < MaxAttemptsPerEvent)
                {
                    yield return new WaitForSecondsRealtime(backoff);
                    backoff *= 2f;
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[OnboardingTelemetry] {request.eventName} dropped after {MaxAttemptsPerEvent} attempts.");
#endif
        }

        static BackendApiClient ResolveClient() =>
            GameContext.Instance != null ? GameContext.Instance.Backend : null;

        /// <summary>
        /// Whether resending an identical body could plausibly succeed. A 400 means the
        /// payload is wrong and always will be; a 404 means the endpoint is switched off
        /// server-side. Retrying either is pure noise.
        /// </summary>
        public static bool IsTransient(BackendError error)
        {
            if (error == null) return false;
            if (error.IsOffline || error.IsTimeout) return true;
            if (error.IsInvalidResponse) return true;   // idempotent: eventId is reused
            if (error.HttpStatus == 0) return true;     // transport failure, no response
            if (error.HttpStatus == 429) return true;
            return error.HttpStatus >= 500;
        }

        static string UtcNowIso() =>
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
