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
            // an entry was dropped or duplicated during an edit. Asserted against Official,
            // not All: AA belongs to the picker, not to the standard, and merging the two
            // would be exactly the edit this check exists to catch.
            Assert.AreEqual(249, CountryCatalog.Official.Count);
            Assert.AreEqual(250, CountryCatalog.All.Count);
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
        // Nothing on the setup panel is required: the server names an unnamed account
        // AgentXXXX, stores a missing country as NULL, and has always given new accounts a
        // default avatar. The gate exists for the one thing the server would refuse — a
        // nickname that was typed and breaks a rule.

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ConfirmWorksOnAnUntouchedPanel(string nickname)
        {
            // A blank field is a choice, not a mistake. Blocking it was the whole reason a
            // player could get stuck on the one screen there is no way past.
            Assert.IsTrue(ProfileSetupGate.IsReady(nickname));
        }

        [TestCase("ab")]
        [TestCase("Ahmet Yılmaz")]
        [TestCase("Jean-Luc")]
        [TestCase("abcdefghijklmnop")]
        public void ATypedNicknameIsStillHeldToEveryRule(string nickname)
        {
            // Too short, whitespace, an illegal character, too long. The server answers all
            // four with a 400, so letting the blank case through must not have loosened
            // them — the gate still defers to NicknameValidator for anything typed.
            Assert.IsFalse(ProfileSetupGate.IsReady(nickname));
        }

        [TestCase("Player123")]
        [TestCase("Çınar")]
        [TestCase("Yiğit")]
        [TestCase("한국어")]
        public void NicknamesTheValidatorAcceptsStillPass(string nickname) =>
            Assert.IsTrue(ProfileSetupGate.IsReady(nickname));

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
        public void RegistrationBodySendsBlanksRatherThanInventingThem()
        {
            // A player who touched nothing. The name travels empty — that is how the
            // server is asked to assign AgentXXXX — while the country travels as AA, the
            // code that says the question was asked and declined. Neither is a value this
            // client made up, and neither is a dropped field the server would have to
            // guess about.
            var json = BackendApiClient.BuildGuestBody("", CountryCatalog.NoCountryCode, InstallId);

            CollectionAssert.AreEquivalent(
                new[] { "displayName", "countryCode", "installationId" }, KeysOf(json));
            StringAssert.Contains("\"displayName\":\"\"", json);
            StringAssert.Contains("\"countryCode\":\"AA\"", json);
            Assert.AreNotEqual("{}", json);
        }

        [Test]
        public void RegistrationBodyCarriesANameWithoutACountry()
        {
            // The other half of the skip: named, but no country.
            var json = BackendApiClient.BuildGuestBody("Player123", CountryCatalog.NoCountryCode, InstallId);

            StringAssert.Contains("\"displayName\":\"Player123\"", json);
            StringAssert.Contains("\"countryCode\":\"AA\"", json);
        }

        [Test]
        public void RegistrationBodyNeverCarriesTheLocalizedName()
        {
            // Official, not All: AA's "name" is the string "AA", which is exactly what the
            // payload is supposed to contain when it is the chosen code.
            foreach (var country in CountryCatalog.Official)
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
            foreach (var country in CountryCatalog.Official)
                StringAssert.DoesNotContain(country.Name, BackendApiClient.BuildCountryBody(country.Code));
        }

        // ── AA: asked, chose not to say ───────────────────────────────────────

        [Test]
        public void NoCountryIsAPickableValue()
        {
            // The backend stores and validates AA like any other code, so the client must
            // be able to hold, send and display it. It is not a placeholder that has to be
            // translated into something else on the way out.
            Assert.IsTrue(CountryCatalog.IsValidCode(CountryCatalog.NoCountryCode));
            Assert.AreEqual("AA", CountryCatalog.Normalize("aa"));
            Assert.AreEqual("AA - AA", CountryCatalog.LabelFor("AA"));
            Assert.AreEqual("AA - AA", CountryCatalog.NoCountry.Label);
        }

        [Test]
        public void NoCountryIsNotInTheOfficialList()
        {
            // The two must stay apart: Official is the ISO standard, and AA is not in it.
            // Merging them would put a non-country in every place that asks "which real
            // countries exist".
            foreach (var country in CountryCatalog.Official)
                Assert.AreNotEqual(CountryCatalog.NoCountryCode, country.Code);
        }

        [Test]
        public void NoCountryLeadsTheList()
        {
            // "1st row" is the requirement. It is also alphabetically where "AA" belongs,
            // so the list needs no special case to put it there.
            Assert.AreEqual(CountryCatalog.NoCountryCode, CountryCatalog.All[0].Code);
            Assert.AreEqual(CountryCatalog.NoCountryCode, Search("")[0].Code);
            Assert.AreEqual(CountryCatalog.NoCountryCode, Search("aa")[0].Code);
        }

        [TestCase(null, "AA")]
        [TestCase("", "AA")]
        [TestCase("   ", "AA")]
        [TestCase("XX", "AA")]
        [TestCase("Türkiye", "AA")]
        [TestCase("AA", "AA")]
        [TestCase("aa", "AA")]
        [TestCase("tr", "TR")]
        [TestCase("TR", "TR")]
        public void ForWireAlwaysStatesACountry(string raw, string expected)
        {
            // Never empty. A registration that stated no country left the save holding
            // whatever the previous account on the device had, which is how a player who
            // touched nothing ended up with someone else's country.
            Assert.AreEqual(expected, CountryCatalog.ForWire(raw));
        }

        // ── Name tag ──────────────────────────────────────────────────────────

        [TestCase("TR")]
        [TestCase("tr")]
        [TestCase("US")]
        public void NameTagShowsTheCodeWhenThereIsOne(string code)
        {
            var tag = ValoCase.UI.UIBuild.WithCountryTag(code, "CENK");
            StringAssert.Contains(">" + code.ToUpperInvariant() + "<", tag);
            StringAssert.EndsWith(" - CENK", tag);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("XX")]
        [TestCase("Türkiye")]
        public void NameTagFallsBackToNoCountry(string code)
        {
            // Skipping the country is ordinary now, so the tag has to hold its shape
            // instead of vanishing — a name that sometimes has a code in front of it and
            // sometimes does not reads as a broken layout.
            var tag = ValoCase.UI.UIBuild.WithCountryTag(code, "CENK");
            StringAssert.Contains(">" + CountryCatalog.NoCountryCode + "<", tag);
            StringAssert.EndsWith(" - CENK", tag);
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
