using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ValoCase.Core;
using ValoCase.Services.Backend;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// Country selection: the catalog, the search the picker runs on every keystroke, the
    /// rule that gates the setup panel's CONFIRM button, and the shape of what registration
    /// puts on the wire.
    ///
    /// These are the parts that fail quietly. A catalog with a duplicate or a missing code
    /// looks fine until a player from that country cannot find it; a search that does not
    /// fold diacritics makes Türkiye unreachable from a plain keyboard; a payload carrying
    /// the localized name instead of the code stores garbage the server cannot group by.
    ///
    /// Lives in the Editor folder for the same reason NicknameValidatorTests does — see
    /// the note there.
    /// </summary>
    public sealed class CountrySelectionTests
    {
        static readonly List<Country> Results = new List<Country>();

        static List<Country> Search(string query)
        {
            CountryCatalog.Search(query, Results);
            return Results;
        }

        // ── Catalog integrity ─────────────────────────────────────────────────

        [Test]
        public void HoldsEveryOfficiallyAssignedCode()
        {
            // ISO-3166-1 currently assigns 249 alpha-2 codes. A number that drifts means
            // an entry was dropped or duplicated during an edit.
            Assert.AreEqual(249, CountryCatalog.All.Count);
        }

        [Test]
        public void CodesAreUniqueAndWellFormed()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var country in CountryCatalog.All)
            {
                Assert.AreEqual(2, country.Code.Length, $"{country.Code} is not two characters");
                Assert.AreEqual(country.Code.ToUpperInvariant(), country.Code,
                    $"{country.Code} is not upper case");
                Assert.IsTrue(country.Code.All(char.IsLetter), $"{country.Code} is not letters only");
                Assert.IsFalse(string.IsNullOrWhiteSpace(country.Name), $"{country.Code} has no name");
                Assert.IsTrue(seen.Add(country.Code), $"{country.Code} appears twice");
            }
        }

        [Test]
        public void CoversTheCodesTheSpecCallsOut()
        {
            foreach (var code in new[] { "TR", "IN", "PK", "DZ", "US" })
                Assert.IsTrue(CountryCatalog.IsValidCode(code), $"{code} is missing from the catalog");
        }

        // ── Display format ────────────────────────────────────────────────────

        [TestCase("TR", "Türkiye - TR")]
        [TestCase("IN", "India - IN")]
        [TestCase("PK", "Pakistan - PK")]
        [TestCase("DZ", "Algeria - DZ")]
        [TestCase("US", "United States - US")]
        public void LabelsAreNameThenCode(string code, string expected) =>
            Assert.AreEqual(expected, CountryCatalog.LabelFor(code));

        [Test]
        public void UnknownCodeHasNoLabel()
        {
            // Empty rather than the raw code: a code the catalog does not know was never
            // picked here, and echoing it back would look like a working selection.
            Assert.AreEqual(string.Empty, CountryCatalog.LabelFor("XX"));
            Assert.AreEqual(string.Empty, CountryCatalog.LabelFor(null));
        }

        // ── Normalisation ─────────────────────────────────────────────────────

        [TestCase("tr", "TR")]
        [TestCase("Tr", "TR")]
        [TestCase("  tr  ", "TR")]
        [TestCase("TR", "TR")]
        public void NormalizeUpperCasesAKnownCode(string raw, string expected) =>
            Assert.AreEqual(expected, CountryCatalog.Normalize(raw));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("XX")]
        [TestCase("TUR")]
        [TestCase("Türkiye")]
        public void NormalizeRejectsAnythingNotInTheCatalog(string raw)
        {
            // The alpha-3 code and the display name are both plausible things to end up
            // holding by accident. Neither may reach the wire.
            Assert.AreEqual(string.Empty, CountryCatalog.Normalize(raw));
            Assert.IsFalse(CountryCatalog.IsValidCode(raw));
        }

        // ── Search: by ISO code ───────────────────────────────────────────────

        [TestCase("TR", "TR")]
        [TestCase("tr", "TR")]
        [TestCase("PK", "PK")]
        [TestCase("pk", "PK")]
        [TestCase("IN", "IN")]
        [TestCase("in", "IN")]
        [TestCase("DZ", "DZ")]
        public void ExactCodeMatchRanksFirst(string query, string expectedCode) =>
            Assert.AreEqual(expectedCode, Search(query)[0].Code);

        [Test]
        public void CodeSearchStillOffersNameMatches()
        {
            // "IN" is India's code and the start of Indonesia's name. Both belong in the
            // list; only the order between them is decided.
            var codes = Search("IN").Select(c => c.Code).ToList();
            Assert.AreEqual("IN", codes[0]);
            CollectionAssert.Contains(codes, "ID");
        }

        // ── Search: by name ───────────────────────────────────────────────────

        [TestCase("tur")]
        [TestCase("TUR")]
        [TestCase("Tür")]
        [TestCase("türk")]
        public void FindsTurkiyeWithOrWithoutTheDiaeresis(string query) =>
            Assert.AreEqual("TR", Search(query)[0].Code);

        [Test]
        public void FindsNamesByAnInnerSubstring()
        {
            var codes = Search("land").Select(c => c.Code).ToList();
            // Nothing is named "land…", so every hit here is a contains-match: the search
            // reaches into the middle of a name, not just its start.
            CollectionAssert.Contains(codes, "IS");   // Iceland
            CollectionAssert.Contains(codes, "IE");   // Ireland
            CollectionAssert.Contains(codes, "NZ");   // New Zealand
        }

        [TestCase("algeria", "DZ")]
        [TestCase("ALGERIA", "DZ")]
        [TestCase("AlGeRiA", "DZ")]
        [TestCase("pakistan", "PK")]
        [TestCase("india", "IN")]
        public void NameSearchIsCaseInsensitive(string query, string expectedCode) =>
            Assert.AreEqual(expectedCode, Search(query)[0].Code);

        [Test]
        public void FindsAccentedNamesTypedPlainly()
        {
            Assert.AreEqual("CI", Search("cote")[0].Code);       // Côte d'Ivoire
            Assert.AreEqual("CW", Search("curacao")[0].Code);    // Curaçao
            Assert.AreEqual("RE", Search("reunion")[0].Code);    // Réunion
        }

        [Test]
        public void EmptyQueryReturnsTheWholeCatalog()
        {
            Assert.AreEqual(CountryCatalog.All.Count, Search("").Count);
            Assert.AreEqual(CountryCatalog.All.Count, Search("   ").Count);
            Assert.AreEqual(CountryCatalog.All.Count, Search(null).Count);
        }

        [Test]
        public void NoMatchReturnsNothing() => Assert.IsEmpty(Search("zzzzqq"));

        [Test]
        public void ResultsNeverRepeatACountry()
        {
            // Each country lands in exactly one ranking bucket. If that ever stopped being
            // true the picker would show the same row twice.
            foreach (var query in new[] { "in", "tr", "united", "a", "is" })
            {
                var codes = Search(query).Select(c => c.Code).ToList();
                CollectionAssert.AllItemsAreUnique(codes, $"duplicate result for \"{query}\"");
            }
        }

        // ── CONFIRM gate ──────────────────────────────────────────────────────

        [Test]
        public void ConfirmIsBlockedUntilACountryIsChosen()
        {
            Assert.IsFalse(ProfileSetupGate.IsReady("Player123", "Jett", null));
            Assert.IsFalse(ProfileSetupGate.IsReady("Player123", "Jett", ""));
            Assert.IsTrue(ProfileSetupGate.IsReady("Player123", "Jett", "TR"));
        }

        [Test]
        public void ConfirmIsBlockedByACountryOutsideTheCatalog()
        {
            // A code restored from an older draft or a hand-edited pref must not pass.
            Assert.IsFalse(ProfileSetupGate.IsReady("Player123", "Jett", "XX"));
            Assert.IsFalse(ProfileSetupGate.IsReady("Player123", "Jett", "Türkiye"));
        }

        [Test]
        public void ConfirmIsBlockedUntilAnAvatarIsChosen()
        {
            Assert.IsFalse(ProfileSetupGate.IsReady("Player123", null, "TR"));
            Assert.IsFalse(ProfileSetupGate.IsReady("Player123", "", "TR"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ab")]
        [TestCase("Ahmet Yılmaz")]
        [TestCase("Jean-Luc")]
        [TestCase("abcdefghijklmnop")]
        public void ACountryCannotRescueARefusedNickname(string nickname)
        {
            // The gate defers to NicknameValidator; adding country selection must not have
            // opened a second, laxer path to CONFIRM.
            Assert.IsFalse(ProfileSetupGate.IsReady(nickname, "Jett", "TR"));
        }

        [TestCase("Player123")]
        [TestCase("Çınar")]
        [TestCase("Yiğit")]
        [TestCase("한국어")]
        public void NicknamesTheValidatorAcceptsStillPass(string nickname) =>
            Assert.IsTrue(ProfileSetupGate.IsReady(nickname, "Jett", "TR"));

        [Test]
        public void GateAcceptsALowerCaseCodeTheSameWayTheCatalogDoes() =>
            Assert.IsTrue(ProfileSetupGate.IsReady("Player123", "Jett", "tr"));

        // ── Registration payload ──────────────────────────────────────────────

        // Every top-level JSON key in a flat object, so a payload can be pinned to its
        // exact field set rather than to a substring that happens to appear.
        static List<string> KeysOf(string json) =>
            Regex.Matches(json, "\"([^\"]+)\":").Select(m => m.Groups[1].Value).ToList();

        [Test]
        public void RegistrationBodyCarriesTheCodeAndNothingElseAboutTheCountry()
        {
            // The real builder the request uses, not a reconstruction of it.
            var json = BackendApiClient.BuildGuestBody("Player123", "TR", InstallId);

            // The whole field set, not just a contains-check: a second country field —
            // a localized name, a region, a flag — could not be added without failing here.
            // The server's GuestRegisterRequest record declares exactly these three.
            CollectionAssert.AreEquivalent(
                new[] { "displayName", "countryCode", "installationId" }, KeysOf(json));
            StringAssert.Contains("\"countryCode\":\"TR\"", json);
            StringAssert.Contains("\"displayName\":\"Player123\"", json);
        }

        [Test]
        public void RegistrationBodyStaysEmptyWhenThereIsNothingToSend()
        {
            // Preserves the older bodiless call. The server reads a missing body as a
            // missing displayName and refuses it, which is the intended outcome — it must
            // not become a body that looks like a real registration.
            Assert.AreEqual("{}", BackendApiClient.BuildGuestBody(null, null, null));
            Assert.AreEqual("{}", BackendApiClient.BuildGuestBody("", "", ""));
        }

        [Test]
        public void RegistrationBodyNeverCarriesTheLocalizedName()
        {
            foreach (var country in CountryCatalog.All)
            {
                var json = BackendApiClient.BuildGuestBody("Player123", country.Code, InstallId);
                StringAssert.DoesNotContain(country.Name, json);
            }
        }

        // A fixed id rather than ClientIdentity.InstallationId: these tests assert the
        // payload shape, and reading the real PlayerPrefs value would make them depend on
        // machine state. That the request passes the canonical one is asserted separately
        // in InstallationLinkPayloadTests.
        const string InstallId = "550e8400-e29b-41d4-a716-446655440000";

        [Test]
        public void RegistrationBodyCarriesTheInstallIdItWasGiven()
        {
            var json = BackendApiClient.BuildGuestBody("Player123", "TR", InstallId);

            StringAssert.Contains("\"installationId\":\"" + InstallId + "\"", json);
        }

        [Test]
        public void RegistrationBodyStillRegistersWhenThereIsNoInstallId()
        {
            // The id is analytics data. A device without one must still send a body the
            // server accepts, or a measurement would be costing us a player.
            var json = BackendApiClient.BuildGuestBody("Player123", "TR", null);

            StringAssert.Contains("\"displayName\":\"Player123\"", json);
            StringAssert.Contains("\"installationId\":\"\"", json);
            Assert.AreNotEqual("{}", json);
        }

        [Test]
        public void CountryUpdateBodyIsTheCodeAlone()
        {
            // The server's UpdateCountryRequest is a single-field record and deliberately
            // has no accountId: a body that could name an account could edit another
            // player's. Sending one would be a request shape the server does not have.
            var json = BackendApiClient.BuildCountryBody("TR");
            CollectionAssert.AreEqual(new[] { "countryCode" }, KeysOf(json));
            StringAssert.Contains("\"countryCode\":\"TR\"", json);
        }

        [Test]
        public void CountryUpdateBodyNeverCarriesTheLocalizedName()
        {
            foreach (var country in CountryCatalog.All)
                StringAssert.DoesNotContain(country.Name, BackendApiClient.BuildCountryBody(country.Code));
        }

        // ── One picker, not two ───────────────────────────────────────────────

        [Test]
        public void ThereIsExactlyOneCountryPicker()
        {
            // Both the first-launch panel and Settings call CountryPickerPopup.Show; the
            // compiler guarantees they call the same type. What this guards is the other
            // failure mode — a second picker being added alongside it later.
            var pickers = typeof(CountryCatalog).Assembly.GetTypes()
                .Where(t => t.Name.IndexOf("CountryPicker", StringComparison.Ordinal) >= 0)
                .Select(t => t.FullName)
                .ToList();

            CollectionAssert.AreEqual(new[] { "ValoCase.UI.CountryPickerPopup" }, pickers);
        }

        // ── Draft persistence ─────────────────────────────────────────────────

        [TearDown]
        public void ClearDraft() => ProfileSetupDraft.Clear();

        [Test]
        public void DraftKeepsAllThreeChoices()
        {
            ProfileSetupDraft.SetNickname("Player123");
            ProfileSetupDraft.SetAvatarKey("Jett");
            ProfileSetupDraft.SetCountryCode("TR");

            Assert.AreEqual("Player123", ProfileSetupDraft.Nickname);
            Assert.AreEqual("Jett", ProfileSetupDraft.AvatarKey);
            Assert.AreEqual("TR", ProfileSetupDraft.CountryCode);
        }

        [Test]
        public void DraftStoresTheCanonicalCode()
        {
            ProfileSetupDraft.SetCountryCode("tr");
            Assert.AreEqual("TR", ProfileSetupDraft.CountryCode);
        }

        [Test]
        public void DraftDropsACodeThatIsNoLongerACountry()
        {
            ProfileSetupDraft.SetCountryCode("XX");
            Assert.AreEqual(string.Empty, ProfileSetupDraft.CountryCode);
        }

        [Test]
        public void ClearingTheDraftLeavesNothingBehind()
        {
            ProfileSetupDraft.SetNickname("Player123");
            ProfileSetupDraft.SetAvatarKey("Jett");
            ProfileSetupDraft.SetCountryCode("TR");
            ProfileSetupDraft.Clear();

            Assert.AreEqual(string.Empty, ProfileSetupDraft.Nickname);
            Assert.AreEqual(string.Empty, ProfileSetupDraft.AvatarKey);
            Assert.AreEqual(string.Empty, ProfileSetupDraft.CountryCode);
        }
    }
}
