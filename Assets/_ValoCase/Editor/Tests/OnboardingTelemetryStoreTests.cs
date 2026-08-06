using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ValoCase.Services.Backend;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// The durable half of the onboarding funnel.
    ///
    /// <para>Every test here is really the same question asked about a different failure:
    /// does the game keep running? The queue is a measurement, and the moment it can crash
    /// a launch, fail a registration or wedge a device in a loop, it has cost more than it
    /// could ever report. So the corrupt-file, unparseable-payload and missing-field cases
    /// are not edge cases in this file — they are the point of it.</para>
    ///
    /// <para>The one behavioural test is the reason the file exists at all: an event emitted
    /// and never delivered has to survive the process ending, because that is precisely the
    /// device whose story the funnel could not tell.</para>
    /// </summary>
    public class OnboardingTelemetryStoreTests
    {
        static string QueuePath =>
            Path.Combine(Application.persistentDataPath, "onboarding_queue.json");

        static string CorruptPath =>
            Path.Combine(Application.persistentDataPath, "onboarding_queue.corrupt");

        [SetUp]
        [TearDown]
        public void ResetFiles()
        {
            OnboardingTelemetryStore.Clear();
            if (File.Exists(CorruptPath)) File.Delete(CorruptPath);
        }

        static OnboardingEventRequest Event(string name, string eventId = null) =>
            new OnboardingEventRequest
            {
                installationId     = "550e8400-e29b-41d4-a716-446655440000",
                eventId            = eventId ?? System.Guid.NewGuid().ToString(),
                eventName          = name,
                clientTimestampUtc = "2026-08-06T09:00:00.000Z",
                appVersion         = "1.0.22",
                platform           = "ANDROID",
                rejectionReason    = "",
                networkErrorCategory = "",
                httpStatus         = 0
            };

        // --- the case the whole file exists for ----------------------------------

        [Test]
        public void AnUndeliveredEventSurvivesTheProcessEnding()
        {
            OnboardingTelemetryStore.Save(new[] { Event("app_launched") });

            // A new run reads what the previous one left behind.
            var restored = OnboardingTelemetryStore.Load();

            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual("app_launched", restored[0].eventName);
        }

        [Test]
        public void OrderIsPreservedSoTheFunnelStillReadsAsAWalkThrough()
        {
            OnboardingTelemetryStore.Save(new[]
            {
                Event("app_launched"), Event("fan_notice_shown"), Event("fan_notice_accepted")
            });

            var restored = OnboardingTelemetryStore.Load();

            Assert.AreEqual("app_launched", restored[0].eventName);
            Assert.AreEqual("fan_notice_shown", restored[1].eventName);
            Assert.AreEqual("fan_notice_accepted", restored[2].eventName);
        }

        [Test]
        public void TheEventIdSurvivesSoAResendIsStillIdempotent()
        {
            // The backend's uniqueness is on event_id. If a restored event came back with a
            // fresh id, a crash between send and ack would double-count the step forever.
            var original = Event("registration_succeeded", "fixed-event-id-1234");

            OnboardingTelemetryStore.Save(new[] { original });
            var restored = OnboardingTelemetryStore.Load();

            Assert.AreEqual("fixed-event-id-1234", restored[0].eventId);
        }

        // --- delivery clears the state -------------------------------------------

        [Test]
        public void AnEmptyQueueDeletesTheFileRatherThanWritingAnEmptyOne()
        {
            OnboardingTelemetryStore.Save(new[] { Event("app_launched") });
            Assert.IsTrue(File.Exists(QueuePath), "precondition: the file was written");

            OnboardingTelemetryStore.Save(new List<OnboardingEventRequest>());

            Assert.IsFalse(File.Exists(QueuePath), "everything delivered leaves nothing behind");
            Assert.AreEqual(0, OnboardingTelemetryStore.Load().Count);
        }

        [Test]
        public void LoadingWithNoFileIsAnEmptyQueueNotAnError()
        {
            Assert.IsFalse(File.Exists(QueuePath));

            Assert.AreEqual(0, OnboardingTelemetryStore.Load().Count);
        }

        // --- bounds ---------------------------------------------------------------

        [Test]
        public void TheStoredQueueIsCappedAndKeepsTheNewestEvents()
        {
            var many = new List<OnboardingEventRequest>();
            for (int i = 0; i < OnboardingTelemetryStore.MaxPersistedEvents + 25; i++)
                many.Add(Event("app_launched", "id-" + i));

            OnboardingTelemetryStore.Save(many);
            var restored = OnboardingTelemetryStore.Load();

            Assert.AreEqual(OnboardingTelemetryStore.MaxPersistedEvents, restored.Count);
            // The later step implies the earlier ones happened, so the tail is what is worth
            // keeping when something has to be dropped.
            Assert.AreEqual("id-" + (many.Count - 1), restored[restored.Count - 1].eventId);
        }

        // --- every way the file can be unusable -----------------------------------

        [Test]
        public void ACorruptFileIsQuarantinedAndTheQueueStartsEmpty()
        {
            File.WriteAllText(QueuePath, "{ this is not json at all ][");

            var restored = OnboardingTelemetryStore.Load();

            Assert.AreEqual(0, restored.Count, "starts empty rather than throwing");
            Assert.IsFalse(File.Exists(QueuePath), "the bad file is moved out of the way");
            Assert.IsTrue(File.Exists(CorruptPath), "and kept as evidence");
        }

        [Test]
        public void AQuarantinedFileIsNotReadAgainOnTheNextLaunch()
        {
            // The failure mode this prevents: a file that cannot parse, retried on every
            // launch forever, is a permanent error loop caused by telemetry.
            File.WriteAllText(QueuePath, "totally invalid");
            OnboardingTelemetryStore.Load();

            var second = OnboardingTelemetryStore.Load();

            Assert.AreEqual(0, second.Count);
            Assert.IsFalse(File.Exists(QueuePath));
        }

        [Test]
        public void AnEmptyOrWhitespaceFileIsTreatedAsAnEmptyQueue()
        {
            foreach (var content in new[] { "", "   ", "\n\t " })
            {
                File.WriteAllText(QueuePath, content);

                Assert.AreEqual(0, OnboardingTelemetryStore.Load().Count, "[" + content + "]");
            }
        }

        [Test]
        public void ValidJsonOfTheWrongShapeDoesNotThrow()
        {
            File.WriteAllText(QueuePath, "{\"somethingElse\":123}");

            Assert.AreEqual(0, OnboardingTelemetryStore.Load().Count);
        }

        [Test]
        public void EntriesMissingTheFieldsTheEndpointRequiresAreDropped()
        {
            // A restored entry with no install id, event id or name is a guaranteed 400 on
            // every launch. Dropping it at the door is the difference between losing one
            // measurement and retrying a doomed request forever.
            var incomplete = new List<OnboardingEventRequest>
            {
                Event("app_launched"),
                new OnboardingEventRequest { installationId = "", eventId = "a", eventName = "app_launched" },
                new OnboardingEventRequest { installationId = "i", eventId = "",  eventName = "app_launched" },
                new OnboardingEventRequest { installationId = "i", eventId = "b", eventName = "" },
            };

            OnboardingTelemetryStore.Save(incomplete);
            var restored = OnboardingTelemetryStore.Load();

            Assert.AreEqual(1, restored.Count, "only the complete one survives");
            Assert.AreEqual("app_launched", restored[0].eventName);
        }

        // --- privacy --------------------------------------------------------------

        [Test]
        public void TheFileNeverContainsATokenANicknameOrAnyIdentifierBesideTheInstallId()
        {
            // This file sits in plain text in the app sandbox. The restriction on what may
            // be written is the reason writing it is safe at all, so it is asserted rather
            // than trusted to code review.
            OnboardingTelemetryStore.Save(new[] { Event("registration_failed") });

            var raw = File.ReadAllText(QueuePath);

            foreach (var forbidden in new[]
                     { "guestToken", "token", "accountId", "displayName", "nickname",
                       "advertisingId", "deviceId", "email", "Authorization" })
            {
                StringAssert.DoesNotContain(forbidden, raw, forbidden + " must never be persisted");
            }
        }

        [Test]
        public void OnlyTheDeclaredPayloadFieldsAreWritten()
        {
            OnboardingTelemetryStore.Save(new[] { Event("nickname_rejected") });

            var raw = File.ReadAllText(QueuePath);

            foreach (var expected in new[]
                     { "installationId", "eventId", "eventName", "clientTimestampUtc",
                       "appVersion", "platform", "rejectionReason", "networkErrorCategory",
                       "httpStatus" })
            {
                StringAssert.Contains(expected, raw, expected + " is part of the payload");
            }
        }
    }
}
