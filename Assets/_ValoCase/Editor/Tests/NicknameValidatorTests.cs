using NUnit.Framework;
using ValoCase.Core;

namespace ValoCase.EditorTests
{
    /// <summary>
    /// The client validator has one job: agree with the server's. These cases are the
    /// ones that would have to change if it ever stopped agreeing — the scripts the old
    /// ASCII rule refused, the check order, and the difference between counting
    /// UTF-16 units and counting what a player sees.
    ///
    /// Lives in the Editor folder because the project has no assembly definitions: the
    /// predefined Assembly-CSharp-Editor already references nunit and UnityEngine.TestRunner
    /// and can see game code, so EditMode tests compile here without restructuring the
    /// project into asmdefs.
    /// </summary>
    public sealed class NicknameValidatorTests
    {
        static void AssertAccepted(string raw, string expectedNormalized = null)
        {
            var ok = NicknameValidator.TryValidate(raw, out var normalized, out var reason);
            Assert.IsTrue(ok, $"expected \"{raw}\" to be accepted but it was refused as {reason}");
            Assert.AreEqual(expectedNormalized ?? raw, normalized);
        }

        static void AssertRefused(string raw, NicknameRejectionReason expected)
        {
            var ok = NicknameValidator.TryValidate(raw, out _, out var reason);
            Assert.IsFalse(ok, $"expected \"{raw}\" to be refused as {expected} but it was accepted");
            Assert.AreEqual(expected, reason);
        }

        // ── Accepted: the Part 5 vocabulary ───────────────────────────────────

        [TestCase("Player123")]
        [TestCase("player_name")]
        [TestCase("Çınar")]
        [TestCase("Yiğit")]
        [TestCase("José")]
        [TestCase("Łukasz")]
        [TestCase("Ελληνικά")]
        [TestCase("한국어")]
        [TestCase("محمد")]
        [TestCase("अर्जुन")]
        public void AcceptsLettersFromAnyScript(string raw) => AssertAccepted(raw);

        [Test]
        public void AcceptsUnderscoreOnItsOwn() => AssertAccepted("___");

        [Test]
        public void AcceptsDigitsOnly() => AssertAccepted("123");

        // ── Refused: whitespace ───────────────────────────────────────────────

        [TestCase("Ahmet Yılmaz")]
        [TestCase("John Smith")]
        [TestCase("a b")]
        [TestCase("tab\there")]
        [TestCase("line\nbreak")]
        public void RefusesInternalWhitespace(string raw) =>
            AssertRefused(raw, NicknameRejectionReason.Whitespace);

        [Test]
        public void RefusesNonBreakingSpaceAsWhitespace()
        {
            // U+00A0 is whitespace to the backend (Character.isSpaceChar) but not to
            // .NET's char.IsWhiteSpace-equivalent notion in every framework, which is why
            // the validator spells the set out rather than inheriting one.
            AssertRefused("a b", NicknameRejectionReason.Whitespace);
        }

        [Test]
        public void RefusesIdeographicSpaceAsWhitespace() =>
            AssertRefused("a　b", NicknameRejectionReason.Whitespace);

        // ── Trimming ──────────────────────────────────────────────────────────

        [Test]
        public void TrimsLeadingAndTrailingWhitespace() => AssertAccepted("  Player  ", "Player");

        [Test]
        public void TrimsNonBreakingSpaceToo() => AssertAccepted(" Player ", "Player");

        [Test]
        public void WhitespaceOnlyIsBlankNotWhitespace()
        {
            // Trimming runs before the whitespace check, so nothing is left to complain
            // about — the same order the backend uses.
            AssertRefused("   ", NicknameRejectionReason.Blank);
            AssertRefused("\t\t\t", NicknameRejectionReason.Blank);
            AssertRefused("\n\n", NicknameRejectionReason.Blank);
        }

        [Test]
        public void NullAndEmptyAreBlank()
        {
            AssertRefused(null, NicknameRejectionReason.Blank);
            AssertRefused("", NicknameRejectionReason.Blank);
        }

        // ── Refused: punctuation, symbols, emoji ──────────────────────────────

        [TestCase("Jean-Luc")]
        [TestCase("O'Connor")]
        [TestCase("hello!")]
        [TestCase("a.b.c")]
        [TestCase("player@1")]
        [TestCase("100%pure")]
        public void RefusesPunctuationAndSymbols(string raw) =>
            AssertRefused(raw, NicknameRejectionReason.InvalidCharacter);

        [Test]
        public void RefusesEmoji()
        {
            AssertRefused("Player\U0001F600", NicknameRejectionReason.InvalidCharacter);
            AssertRefused("\U0001F600\U0001F600\U0001F600", NicknameRejectionReason.InvalidCharacter);
        }

        [Test]
        public void RefusesZeroWidthJoiner() =>
            AssertRefused("ab‍cd", NicknameRejectionReason.InvalidCharacter);

        // ── Check order: content before size ──────────────────────────────────

        [Test]
        public void IllegalCharacterBeatsTooShort()
        {
            // "a!" is both too short and illegal. The backend reports the character, so
            // this one must too, or the two would show the player different reasons for
            // the same name.
            AssertRefused("a!", NicknameRejectionReason.InvalidCharacter);
        }

        [Test]
        public void WhitespaceBeatsTooLong() =>
            AssertRefused("aaaaaaaa bbbbbbbb cccccccc", NicknameRejectionReason.Whitespace);

        // ── Length in user-visible characters ─────────────────────────────────

        [Test]
        public void RefusesShorterThanThree()
        {
            AssertRefused("a", NicknameRejectionReason.TooShort);
            AssertRefused("ab", NicknameRejectionReason.TooShort);
        }

        [Test]
        public void AcceptsExactlyThree() => AssertAccepted("abc");

        [Test]
        public void AcceptsExactlyFifteen() => AssertAccepted("abcdefghijklmno");

        [Test]
        public void RefusesSixteen() =>
            AssertRefused("abcdefghijklmnop", NicknameRejectionReason.TooLong);

        [Test]
        public void CountsPrecomposedAndDecomposedTheSame()
        {
            // "Çınar" typed as C + combining cedilla is one name, not a longer one:
            // NFC folds it to the precomposed form before anything counts it.
            const string decomposed = "Çınar";
            Assert.AreEqual(5, NicknameValidator.GraphemeLength(NicknameValidator.Normalize(decomposed)));
            AssertAccepted(decomposed, "Çınar");
        }

        [Test]
        public void RefusesPastTheStorageGuardBeforeCounting()
        {
            // Past 60 UTF-16 units the backend reports TOO_LONG without running its break
            // iterator; the client takes the same shortcut so the reasons still match.
            var long61 = new string('a', 61);
            AssertRefused(long61, NicknameRejectionReason.TooLong);
        }

        // ── Wire vocabulary for telemetry ─────────────────────────────────────

        [Test]
        public void WireNamesMatchTheBackendEnum()
        {
            Assert.AreEqual("BLANK",             NicknameValidator.WireName(NicknameRejectionReason.Blank));
            Assert.AreEqual("TOO_SHORT",         NicknameValidator.WireName(NicknameRejectionReason.TooShort));
            Assert.AreEqual("TOO_LONG",          NicknameValidator.WireName(NicknameRejectionReason.TooLong));
            Assert.AreEqual("WHITESPACE",        NicknameValidator.WireName(NicknameRejectionReason.Whitespace));
            Assert.AreEqual("INVALID_CHARACTER", NicknameValidator.WireName(NicknameRejectionReason.InvalidCharacter));
            Assert.AreEqual("",                  NicknameValidator.WireName(NicknameRejectionReason.None));
        }

        // ── Messages ──────────────────────────────────────────────────────────

        [Test]
        public void EveryRejectionReasonHasAMessage()
        {
            foreach (NicknameRejectionReason reason in System.Enum.GetValues(typeof(NicknameRejectionReason)))
            {
                var message = NicknameMessages.For(reason);
                if (reason == NicknameRejectionReason.None)
                    Assert.IsEmpty(message);
                else
                    Assert.IsNotEmpty(message, $"{reason} has no player-facing message");
            }
        }
    }
}
