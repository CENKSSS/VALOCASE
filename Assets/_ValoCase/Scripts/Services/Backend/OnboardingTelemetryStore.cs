using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// The on-disk half of <see cref="OnboardingTelemetry"/>: the queue of funnel events
    /// that have not reached the server yet.
    ///
    /// <para><strong>Why this exists.</strong> The queue used to live only in memory, which
    /// meant the one case the funnel was built to explain was the one case it could not
    /// record. A device that launches the app and dies — a crash on a low-memory handset, a
    /// player who swipes the app away at the legal notice, a process the OS kills during
    /// the first network call — emitted <c>app_launched</c> into a queue that was never
    /// flushed, so it looked identical to a device that never opened the app at all. On
    /// 2026-08-05 that was the difference between "43 installs never launched" and "43
    /// installs launched and crashed", and the data could not tell them apart.</para>
    ///
    /// <para><strong>What it may hold.</strong> Exactly the fields of
    /// <see cref="OnboardingEventRequest"/>: an opaque install UUID, a per-event UUID, a
    /// step name from the backend allowlist, a timestamp, the app version, the platform,
    /// and two coarse failure codes. No nickname, no guest token, no authorization header,
    /// no advertising id, no email, no IP. This file sits in plain text in the app's
    /// sandbox, so that restriction is not a style preference — it is the reason the file
    /// is safe to write at all.</para>
    ///
    /// <para><strong>What it may never do.</strong> Fail the game. Every entry point here
    /// swallows its exceptions and degrades to an empty queue: a full disk, a read-only
    /// sandbox, a file half-written by a process the OS killed mid-save, or a JSON payload
    /// from a future version of the client all end the same way — a warning in the log and
    /// a player who never notices.</para>
    /// </summary>
    public static class OnboardingTelemetryStore
    {
        /// <summary>The queue file, in the app sandbox. Cleared with the app's data.</summary>
        const string FileName = "onboarding_queue.json";

        /// <summary>
        /// Written first, then moved over the real file. A process killed mid-write leaves
        /// the temp file behind and the previous queue intact, rather than a truncated file
        /// that parses as zero events.
        /// </summary>
        const string TempFileName = "onboarding_queue.tmp";

        /// <summary>
        /// Where a file we could not parse is moved. Kept rather than deleted: it is
        /// evidence about a client bug, and it is small. A second corruption overwrites it,
        /// so it cannot grow.
        /// </summary>
        const string CorruptFileName = "onboarding_queue.corrupt";

        /// <summary>
        /// Hard cap on what may be restored from disk. Matches the in-memory bound in
        /// <see cref="OnboardingTelemetry"/>. A file that somehow carries more is truncated
        /// to the newest entries rather than trusted, so a tampered or runaway file cannot
        /// turn into an unbounded send loop on the next launch.
        /// </summary>
        public const int MaxPersistedEvents = 64;

        static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);
        static string TempPath => System.IO.Path.Combine(Application.persistentDataPath, TempFileName);
        static string CorruptPath => System.IO.Path.Combine(Application.persistentDataPath, CorruptFileName);

        [Serializable]
        sealed class Envelope
        {
            public List<OnboardingEventRequest> events = new List<OnboardingEventRequest>();
        }

        /// <summary>
        /// Restores the pending queue. Returns an empty list for every failure mode: no
        /// file, an unreadable file, a file that is not JSON, JSON that is not our shape,
        /// or entries missing the fields that make them sendable.
        /// </summary>
        public static List<OnboardingEventRequest> Load()
        {
            var restored = new List<OnboardingEventRequest>();
            try
            {
                if (!File.Exists(Path)) return restored;

                var json = File.ReadAllText(Path);
                if (string.IsNullOrWhiteSpace(json)) return restored;

                // JsonUtility returns null for malformed JSON on some platforms and throws
                // on others, so both are handled rather than one being assumed.
                var envelope = JsonUtility.FromJson<Envelope>(json);
                if (envelope?.events == null)
                {
                    Quarantine("payload did not parse into the expected shape");
                    return restored;
                }

                foreach (var e in envelope.events)
                {
                    if (IsSendable(e)) restored.Add(e);
                }

                // Newest wins if a file somehow exceeds the cap: the later step implies the
                // earlier ones happened, so the tail is the informative end.
                if (restored.Count > MaxPersistedEvents)
                    restored.RemoveRange(0, restored.Count - MaxPersistedEvents);
            }
            catch (Exception e)
            {
                Quarantine(e.Message);
                return new List<OnboardingEventRequest>();
            }
            return restored;
        }

        /// <summary>
        /// Replaces the stored queue. An empty list deletes the file rather than writing an
        /// empty one, so the common case — everything delivered — leaves nothing behind.
        /// </summary>
        public static void Save(IEnumerable<OnboardingEventRequest> events)
        {
            try
            {
                var envelope = new Envelope();
                foreach (var e in events)
                {
                    if (IsSendable(e)) envelope.events.Add(e);
                }
                if (envelope.events.Count > MaxPersistedEvents)
                {
                    envelope.events.RemoveRange(0, envelope.events.Count - MaxPersistedEvents);
                }

                if (envelope.events.Count == 0)
                {
                    Clear();
                    return;
                }

                // Temp then replace. File.Replace is not available on every Unity platform,
                // so a delete-then-move is used, which is the same guarantee for our case:
                // the worst interleaving loses the queue rather than corrupting it.
                File.WriteAllText(TempPath, JsonUtility.ToJson(envelope));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(TempPath, Path);
            }
            catch (Exception e)
            {
                // A queue that cannot be written is a measurement we lose, not a session we
                // break. Nothing above this call reacts to the failure.
                Debug.LogWarning("[OnboardingTelemetry] queue could not be persisted: " + e.Message);
            }
        }

        /// <summary>Removes the queue file and any temp left by an interrupted write.</summary>
        public static void Clear()
        {
            TryDelete(Path);
            TryDelete(TempPath);
        }

        /// <summary>
        /// Whether an entry still carries what the endpoint requires. A restored entry with
        /// no install id, no event id or no name would be a guaranteed 400 on every launch
        /// forever, so it is dropped at the door instead of being retried.
        /// </summary>
        static bool IsSendable(OnboardingEventRequest e) =>
            e != null
            && !string.IsNullOrEmpty(e.installationId)
            && !string.IsNullOrEmpty(e.eventId)
            && !string.IsNullOrEmpty(e.eventName);

        /// <summary>
        /// Moves an unusable file aside and warns. The game continues with an empty queue —
        /// the alternative, reading it again on every launch, would be a permanent failure
        /// loop caused by telemetry, which is the one thing telemetry must never do.
        /// </summary>
        static void Quarantine(string reason)
        {
            Debug.LogWarning("[OnboardingTelemetry] queue file unusable (" + reason
                             + "); moving it aside and starting empty.");
            try
            {
                if (!File.Exists(Path)) return;
                TryDelete(CorruptPath);
                File.Move(Path, CorruptPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OnboardingTelemetry] could not quarantine the queue file: " + e.Message);
                TryDelete(Path);
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OnboardingTelemetry] could not delete " + path + ": " + e.Message);
            }
        }
    }
}
