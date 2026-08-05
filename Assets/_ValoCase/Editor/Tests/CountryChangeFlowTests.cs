using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ValoCase.Core;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// The Settings country change: pick → confirm → one PATCH → apply only the echo.
    ///
    /// Every case here is a rule that fails silently when broken: a double tap that
    /// sends two requests, a failure that leaves the new country on screen anyway, a
    /// lower-case code or a localized name reaching the wire, an account with no country
    /// that cannot set one. The flow is pure logic (CountryChangeFlow), so the save
    /// delegate is a recorder and no Canvas or backend is involved.
    /// </summary>
    public sealed class CountryChangeFlowTests
    {
        /// <summary>
        /// Stands in for GameContext.SaveCountryBackend: records every request and lets
        /// the test decide when and how each one completes, like the real coroutine does.
        /// </summary>
        sealed class RecordingSaver
        {
            public readonly List<string> Requests = new List<string>();
            Action<string> _onSaved;
            Action<string> _onFailed;

            public void Save(string code, Action<string> onSaved, Action<string> onFailed)
            {
                Requests.Add(code);
                _onSaved  = onSaved;
                _onFailed = onFailed;
            }

            public void SucceedWith(string echo) => _onSaved?.Invoke(echo);
            public void Fail(string message)     => _onFailed?.Invoke(message);
        }

        RecordingSaver _saver;
        List<string>   _applied;
        List<string>   _failed;

        [SetUp]
        public void SetUp()
        {
            _saver   = new RecordingSaver();
            _applied = new List<string>();
            _failed  = new List<string>();
        }

        CountryChangeFlow Flow(string current) =>
            new CountryChangeFlow(current, _saver.Save, _applied.Add, _failed.Add);

        // ── Selection ─────────────────────────────────────────────────────────

        [Test]
        public void LowercaseSelectionIsNormalized()
        {
            var flow = Flow("TR");
            Assert.IsTrue(flow.Select("in"));
            Assert.AreEqual("IN", flow.PendingCode);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("XX")]
        [TestCase("TUR")]
        [TestCase("Türkiye")]
        [TestCase("India")]
        public void InvalidAndLocalizedSelectionsAreRefused(string picked)
        {
            var flow = Flow("TR");
            Assert.IsFalse(flow.Select(picked));
            Assert.AreEqual(CountryChangeFlow.Phase.Idle, flow.State);
            Assert.IsEmpty(_saver.Requests);
        }

        [Test]
        public void RepickingTheCurrentCountryIsANoop()
        {
            var flow = Flow("TR");
            Assert.IsFalse(flow.Select("TR"));
            Assert.IsFalse(flow.Select("tr"));   // same country, just typed differently
            Assert.AreEqual(CountryChangeFlow.Phase.Idle, flow.State);
        }

        // ── Confirmation and the double-click guard ───────────────────────────

        [Test]
        public void RapidDoubleConfirmSendsExactlyOneRequest()
        {
            var flow = Flow("TR");
            flow.Select("IN");

            Assert.IsTrue(flow.Confirm());
            Assert.IsFalse(flow.Confirm());   // second tap lands while the first is in flight
            Assert.IsFalse(flow.Confirm());

            Assert.AreEqual(1, _saver.Requests.Count);
        }

        [Test]
        public void ConfirmWithoutASelectionSendsNothing()
        {
            var flow = Flow("TR");
            Assert.IsFalse(flow.Confirm());
            Assert.IsEmpty(_saver.Requests);
        }

        [Test]
        public void CancelSendsNothingAndKeepsTheOldCountry()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.Cancel();

            Assert.AreEqual(CountryChangeFlow.Phase.Idle, flow.State);
            Assert.AreEqual("TR", flow.CurrentCode);
            Assert.IsEmpty(_saver.Requests);
        }

        // ── The request itself ────────────────────────────────────────────────

        [Test]
        public void RequestCarriesTheIsoCodeNeverTheName()
        {
            var flow = Flow("TR");
            flow.Select("in");   // lower case on purpose
            flow.Confirm();

            Assert.AreEqual("IN", _saver.Requests.Single());
            foreach (var country in CountryCatalog.All)
                Assert.AreNotEqual(country.Name, _saver.Requests[0]);
        }

        // ── Success ───────────────────────────────────────────────────────────

        [Test]
        public void SuccessAppliesTheServerEcho()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.Confirm();
            _saver.SucceedWith("IN");

            Assert.AreEqual("IN", flow.CurrentCode);
            Assert.AreEqual(CountryChangeFlow.Phase.Idle, flow.State);
            // onApplied is what the screen refreshes the Settings and profile labels
            // from, so it must carry the stored code.
            Assert.AreEqual(new[] { "IN" }, _applied.ToArray());
            Assert.IsEmpty(_failed);
        }

        [Test]
        public void ServerEchoWinsEvenWhenItsCaseDiffers()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.Confirm();
            _saver.SucceedWith("in");   // hypothetical lax echo — still one canonical form

            Assert.AreEqual("IN", flow.CurrentCode);
        }

        [Test]
        public void AnAccountWithNoCountryCanSetOne()
        {
            // Accounts created during the migration window have countryCode null.
            var flow = Flow(null);
            Assert.AreEqual(string.Empty, flow.CurrentCode);

            Assert.IsTrue(flow.Select("IN"));
            flow.Confirm();
            _saver.SucceedWith("IN");

            Assert.AreEqual("IN", flow.CurrentCode);
        }

        [Test]
        public void ADuplicateSuccessCallbackIsIgnored()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.Confirm();
            _saver.SucceedWith("IN");
            _saver.SucceedWith("US");   // stale second invocation must change nothing

            Assert.AreEqual("IN", flow.CurrentCode);
            Assert.AreEqual(1, _applied.Count);
        }

        // ── Failure ───────────────────────────────────────────────────────────

        [Test]
        public void FailureKeepsThePreviousCountry()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.Confirm();
            _saver.Fail("Sunucu kullanılamıyor.");

            Assert.AreEqual("TR", flow.CurrentCode);
            Assert.AreEqual(CountryChangeFlow.Phase.Idle, flow.State);
            Assert.AreEqual(new[] { "Sunucu kullanılamıyor." }, _failed.ToArray());
            Assert.IsEmpty(_applied);
        }

        [Test]
        public void AnotherAttemptIsPossibleAfterAFailure()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.Confirm();
            _saver.Fail("timeout");

            Assert.IsTrue(flow.Select("IN"), "retry selection must be accepted");
            flow.Confirm();
            _saver.SucceedWith("IN");

            Assert.AreEqual(2, _saver.Requests.Count);
            Assert.AreEqual("IN", flow.CurrentCode);
        }

        [Test]
        public void FailureForAnAccountWithNoCountryLeavesItUnset()
        {
            var flow = Flow(null);
            flow.Select("IN");
            flow.Confirm();
            _saver.Fail("500");

            Assert.AreEqual(string.Empty, flow.CurrentCode);
        }

        // ── Screen refresh interplay ──────────────────────────────────────────

        [Test]
        public void SyncCurrentIsIgnoredMidChange()
        {
            var flow = Flow("TR");
            flow.Select("IN");
            flow.SyncCurrent("US");                 // profile refresh while confirming
            Assert.AreEqual("TR", flow.CurrentCode);

            flow.Confirm();
            flow.SyncCurrent("US");                 // and while the request is in flight
            Assert.AreEqual("TR", flow.CurrentCode);

            _saver.SucceedWith("IN");
            flow.SyncCurrent("US");                 // idle again — now it applies
            Assert.AreEqual("US", flow.CurrentCode);
        }

        // ── Wiring pins (source-level) ────────────────────────────────────────
        // These read the shipped source because the facts they pin — which verb goes on
        // the wire, which picker each screen opens — live in code no EditMode test can
        // execute against a server or a Canvas. Editor tests run with the project root
        // as the working directory.

        [Test]
        public void CountryUpdateUsesPatchNotPut()
        {
            var client = File.ReadAllText("Assets/_ValoCase/Scripts/Services/Backend/BackendApiClient.cs");
            StringAssert.Contains("Send(\"PATCH\", ApiPrefix + \"/account/country\"", client);
            StringAssert.DoesNotContain("Send(\"PUT\", ApiPrefix + \"/account/country\"", client);
        }

        [Test]
        public void SettingsAndSetupOpenTheSameSharedPicker()
        {
            var settings = File.ReadAllText("Assets/_ValoCase/Scripts/UI/Screens/SettingsScreen.cs");
            var setup    = File.ReadAllText("Assets/_ValoCase/Scripts/UI/FirstLaunchProfilePopup.cs");
            StringAssert.Contains("CountryPickerPopup.Show", settings);
            StringAssert.Contains("CountryPickerPopup.Show", setup);
        }
    }
}
