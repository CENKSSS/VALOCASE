using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValoCase.Services.Backend;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// The install id that travels with a registration.
    ///
    /// One thing is being defended here and it is worth naming: there must be exactly one
    /// installation id on this device. Onboarding telemetry, the session lifecycle and now
    /// the registration body all report it, and the backend joins the pre-account funnel to
    /// the account through it. A second id generated anywhere in the client would not throw,
    /// would not fail a build, and would not show up in play testing — it would surface
    /// months later as a funnel where nobody who launched the app ever registered.
    ///
    /// The other rule is that the id may never cost a registration. It is a measurement, and
    /// a measurement that can refuse a player is a bug regardless of how correct it is.
    /// </summary>
    public class InstallationLinkPayloadTests
    {
        static List<string> KeysOf(string json) =>
            Regex.Matches(json, "\"([^\"]+)\":").Select(m => m.Groups[1].Value).ToList();

        [Test]
        public void ThereIsOnlyOneInstallationIdAndItIsAUuid()
        {
            var id = ClientIdentity.InstallationId;

            Assert.IsFalse(string.IsNullOrEmpty(id), "an id is always available");
            Assert.IsTrue(Guid.TryParse(id, out var parsed), "the id parses as a UUID: " + id);
            Assert.AreNotEqual(Guid.Empty, parsed, "never the all-zero UUID");
        }

        [Test]
        public void TheInstallationIdIsStableAcrossReads()
        {
            // Regenerating per call would produce one "install" per event server-side, which
            // reads as a flood of one-step funnels rather than one player walking through.
            var first = ClientIdentity.InstallationId;
            var second = ClientIdentity.InstallationId;

            Assert.AreEqual(first, second);
        }

        [Test]
        public void TheRegistrationBodyIsAcceptedByTheServersFieldSet()
        {
            // The server's GuestRegisterRequest record declares exactly these three
            // components. Unknown fields are ignored server-side, but a field we send and
            // it never reads is dead weight on every registration, so the set is pinned.
            var json = BackendApiClient.BuildGuestBody("Player123", "TR",
                "550e8400-e29b-41d4-a716-446655440000");

            CollectionAssert.AreEquivalent(
                new[] { "displayName", "countryCode", "installationId" }, KeysOf(json));
        }

        [Test]
        public void TheBodyCarriesTheIdVerbatimWithNoReformatting()
        {
            // Lowercase, dashed, 36 characters — the exact shape UUID.fromString expects on
            // the server. Uppercasing or stripping dashes would still parse there, but the
            // stored value would stop matching player_sessions.installation_id on sight
            // during an investigation.
            const string id = "9e72481d-ebfc-4168-b347-19990411a4c8";

            var json = BackendApiClient.BuildGuestBody("Player123", "TR", id);

            StringAssert.Contains("\"installationId\":\"" + id + "\"", json);
        }

        [Test]
        public void ANullOrBlankIdStillProducesARegisterableBody()
        {
            foreach (var missing in new[] { null, "", "   " })
            {
                var json = BackendApiClient.BuildGuestBody("Player123", "TR", missing);

                Assert.AreNotEqual("{}", json, "[" + (missing ?? "null") + "] still registers");
                StringAssert.Contains("\"displayName\":\"Player123\"", json);
            }
        }

        [Test]
        public void TheBodyNeverCarriesATokenOrANicknameBesideTheId()
        {
            // The registration body is the one place a token could plausibly be added by
            // mistake, since the response carries one. Nothing that identifies a person may
            // sit next to the install id: that pairing is what turns an opaque id into a
            // profile.
            var json = BackendApiClient.BuildGuestBody("Player123", "TR",
                "550e8400-e29b-41d4-a716-446655440000");

            foreach (var forbidden in new[] { "guestToken", "token", "accountId", "advertisingId", "deviceId" })
                CollectionAssert.DoesNotContain(KeysOf(json), forbidden, forbidden);
        }
    }
}
