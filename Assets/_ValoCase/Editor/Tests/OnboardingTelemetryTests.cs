using NUnit.Framework;
using ValoCase.Services.Backend;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// Covers the two decisions in the telemetry path that are pure logic and that break
    /// quietly when wrong: which failures are worth retrying, and which category name a
    /// failure reports. Both matter because the endpoint is unauthenticated and rate
    /// limited — retrying something that can never succeed spends the budget that a
    /// recoverable send needs.
    ///
    /// The queue and the coroutine drain are not covered here: they need a running
    /// player loop and a reachable backend, so they are verified on device through the
    /// QA checklist in Docs/ONBOARDING-QA.md.
    /// </summary>
    public sealed class OnboardingTelemetryTests
    {
        static BackendError Http(int status) => new BackendError(status, $"HTTP {status}");

        // ── Retry policy ──────────────────────────────────────────────────────

        [Test]
        public void OfflineIsTransient() =>
            Assert.IsTrue(OnboardingTelemetry.IsTransient(new BackendError(0, "offline", isOffline: true)));

        [Test]
        public void TimeoutIsTransient() =>
            Assert.IsTrue(OnboardingTelemetry.IsTransient(new BackendError(0, "timeout", isTimeout: true)));

        [Test]
        public void TransportFailureIsTransient() =>
            Assert.IsTrue(OnboardingTelemetry.IsTransient(Http(0)));

        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(429)]
        public void ServerSideAndRateLimitAreTransient(int status) =>
            Assert.IsTrue(OnboardingTelemetry.IsTransient(Http(status)));

        [Test]
        public void UnparseableSuccessBodyIsTransient()
        {
            // Safe to retry only because the eventId is reused: if the server did store
            // the row, the retry collapses into a duplicate rather than a second step.
            Assert.IsTrue(OnboardingTelemetry.IsTransient(
                new BackendError(202, "bad body", isInvalidResponse: true)));
        }

        [Test]
        public void BadRequestIsNotRetried()
        {
            // An identical body cannot become valid. Retrying a 400 would burn the
            // per-installation rate limit against a request that will never be accepted.
            Assert.IsFalse(OnboardingTelemetry.IsTransient(Http(400)));
        }

        [Test]
        public void EndpointDisabledIsNotRetried() =>
            Assert.IsFalse(OnboardingTelemetry.IsTransient(Http(404)));

        [Test]
        public void NoErrorIsNotTransient() =>
            Assert.IsFalse(OnboardingTelemetry.IsTransient(null));

        // ── Category vocabulary (must stay inside the backend's allowlist) ─────

        [Test]
        public void OfflineMapsToOffline() =>
            Assert.AreEqual("offline",
                BackendErrorMapper.NetworkCategory(new BackendError(0, "x", isOffline: true)));

        [Test]
        public void TimeoutMapsToTimeout() =>
            Assert.AreEqual("timeout",
                BackendErrorMapper.NetworkCategory(new BackendError(0, "x", isTimeout: true)));

        [TestCase(400)]
        [TestCase(429)]
        [TestCase(500)]
        public void AnyHttpStatusMapsToHttpError(int status) =>
            Assert.AreEqual("http_error", BackendErrorMapper.NetworkCategory(Http(status)));

        [Test]
        public void UnresolvedHostMapsToDns() =>
            Assert.AreEqual("dns",
                BackendErrorMapper.NetworkCategory(new BackendError(0, "Cannot resolve destination host")));

        [Test]
        public void OtherTransportFailureMapsToTransport() =>
            Assert.AreEqual("transport",
                BackendErrorMapper.NetworkCategory(new BackendError(0, "Connection refused")));

        [Test]
        public void NullErrorMapsToUnknown() =>
            Assert.AreEqual("unknown", BackendErrorMapper.NetworkCategory(null));

        [Test]
        public void EveryCategoryIsOnTheBackendAllowlist()
        {
            // The server drops anything it does not recognise, so a typo here would not
            // fail loudly — it would silently blank the column on every failed
            // registration, which is the one row you most want populated.
            var allowed = new[]
            {
                "offline", "timeout", "dns", "transport",
                "http_error", "invalid_response", "unknown"
            };

            var produced = new[]
            {
                BackendErrorMapper.NetworkCategory(null),
                BackendErrorMapper.NetworkCategory(new BackendError(0, "x", isOffline: true)),
                BackendErrorMapper.NetworkCategory(new BackendError(0, "x", isTimeout: true)),
                BackendErrorMapper.NetworkCategory(new BackendError(0, "Cannot resolve host")),
                BackendErrorMapper.NetworkCategory(new BackendError(0, "Connection refused")),
                BackendErrorMapper.NetworkCategory(Http(400)),
                BackendErrorMapper.NetworkCategory(Http(500))
            };

            foreach (var category in produced)
                CollectionAssert.Contains(allowed, category);
        }
    }
}
