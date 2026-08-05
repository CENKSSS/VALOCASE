using System;
using System.Globalization;
using System.Text;

namespace ValoCase.Core
{
    /// <summary>
    /// Why a nickname was refused. The names match the backend's
    /// RegistrationRejectionReason enum, and <see cref="NicknameValidator.WireName"/>
    /// produces the exact token the telemetry endpoint accepts.
    /// </summary>
    public enum NicknameRejectionReason
    {
        None,
        Blank,
        TooShort,
        TooLong,
        Whitespace,
        InvalidCharacter
    }

    /// <summary>
    /// Client-side twin of the backend nickname rule (AccountService.classifyDisplayName).
    ///
    /// This exists so a name the player types is refused on the device with a readable
    /// reason instead of costing a round trip and coming back as an opaque 400. That is
    /// only worth anything if the two agree, so this is a deliberate line-by-line port
    /// rather than an approximation:
    ///
    ///   * NFC first, then leading/trailing whitespace removed. Both sides validate,
    ///     store and compare the same canonical string, so a name typed with a combining
    ///     accent and the same name typed precomposed are one value.
    ///   * Whitespace means the same set the backend uses:
    ///     Character.isWhitespace || Character.isSpaceChar. That union is exactly
    ///     U+0009-U+000D, U+001C-U+001F and the separator categories Zs/Zl/Zp, which is
    ///     what is spelled out below — .NET's char.IsWhiteSpace is not the same set
    ///     (it omits U+001C-U+001F and adds U+0085).
    ///   * The accepted characters are letters of any script, decimal digits, combining
    ///     marks and underscore. Combining marks have to be accepted: the Devanagari
    ///     virama and vowel signs are not letters, so a name like अर्जुन would otherwise
    ///     be refused as an illegal character despite being ordinary text.
    ///   * Checks run content-before-size, in the backend's order:
    ///     BLANK, WHITESPACE, INVALID_CHARACTER, TOO_LONG (storage guard), TOO_SHORT,
    ///     TOO_LONG (grapheme bound). A name that is both short and illegal reports the
    ///     illegal character, which is the more useful complaint — and, more to the
    ///     point, it reports whatever the server would have reported.
    ///
    /// Known limit: length is counted in grapheme clusters via
    /// <see cref="StringInfo.LengthInTextElements"/>, the backend counts them with
    /// java.text.BreakIterator. The two implement different revisions of the cluster
    /// rules, so a name sitting exactly on the 3 or 15 boundary AND containing a complex
    /// cluster could in principle be counted differently. It cannot happen for emoji or
    /// zero-width joiners, where the two differ most, because those are refused as
    /// illegal characters before anything is counted.
    /// </summary>
    public static class NicknameValidator
    {
        /// <summary>Minimum length in user-visible characters (grapheme clusters).</summary>
        public const int MinLength = 3;

        /// <summary>Maximum length in user-visible characters (grapheme clusters).</summary>
        public const int MaxLength = 15;

        /// <summary>
        /// Storage guard in UTF-16 units, mirroring the backend's DISPLAY_NAME_MAX_CHARS.
        /// A single cluster can carry an unbounded run of combining marks, so the
        /// grapheme bound alone does not bound the stored string.
        /// </summary>
        public const int MaxUtf16Length = 60;

        /// <summary>
        /// The canonical form the backend stores and echoes: NFC, then leading and
        /// trailing whitespace removed. Returns null for null, mirroring the backend.
        /// Send this value, not the raw field text, so the name the server stores is the
        /// name the client already validated.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (raw == null) return null;

            string nfc;
            try
            {
                nfc = raw.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                // Unpaired surrogate. Java's Normalizer passes these through and the
                // character check refuses them a moment later; .NET throws instead, so
                // the raw text is carried forward to reach the same refusal.
                nfc = raw;
            }

            int start = 0;
            int end = nfc.Length;
            while (start < end)
            {
                int cp = CodePointAt(nfc, start, out int size);
                if (!IsUnicodeWhitespace(cp)) break;
                start += size;
            }
            while (end > start)
            {
                int cp = CodePointBefore(nfc, end, out int size);
                if (!IsUnicodeWhitespace(cp)) break;
                end -= size;
            }
            return nfc.Substring(start, end - start);
        }

        /// <summary>
        /// Which rule the name breaks, or <see cref="NicknameRejectionReason.None"/> when
        /// it is acceptable. Pure and allocation-light enough to call on every keystroke.
        /// </summary>
        public static NicknameRejectionReason Classify(string raw)
        {
            if (raw == null) return NicknameRejectionReason.Blank;

            var name = Normalize(raw);
            if (string.IsNullOrEmpty(name)) return NicknameRejectionReason.Blank;

            for (int i = 0; i < name.Length; )
            {
                int cp = CodePointAt(name, i, out int size);
                if (IsUnicodeWhitespace(cp)) return NicknameRejectionReason.Whitespace;
                if (!IsAllowedCodePoint(name, i, cp)) return NicknameRejectionReason.InvalidCharacter;
                i += size;
            }

            // Cheap guard first, same as the backend: a name past the storage bound
            // cannot be inside the grapheme bound either.
            if (name.Length > MaxUtf16Length) return NicknameRejectionReason.TooLong;

            int length = GraphemeLength(name);
            if (length < MinLength) return NicknameRejectionReason.TooShort;
            if (length > MaxLength) return NicknameRejectionReason.TooLong;
            return NicknameRejectionReason.None;
        }

        /// <summary>
        /// Validates and hands back the canonical name. <paramref name="normalized"/> is
        /// the value to send to the backend; it is empty when the name is refused.
        /// </summary>
        public static bool TryValidate(string raw, out string normalized, out NicknameRejectionReason reason)
        {
            reason = Classify(raw);
            normalized = reason == NicknameRejectionReason.None ? Normalize(raw) : string.Empty;
            return reason == NicknameRejectionReason.None;
        }

        /// <summary>
        /// The token the telemetry endpoint accepts for a rejection reason, matching the
        /// backend's RegistrationRejectionReason names. Empty when nothing was refused.
        /// </summary>
        public static string WireName(NicknameRejectionReason reason)
        {
            switch (reason)
            {
                case NicknameRejectionReason.Blank:            return "BLANK";
                case NicknameRejectionReason.TooShort:         return "TOO_SHORT";
                case NicknameRejectionReason.TooLong:          return "TOO_LONG";
                case NicknameRejectionReason.Whitespace:       return "WHITESPACE";
                case NicknameRejectionReason.InvalidCharacter: return "INVALID_CHARACTER";
                default:                                       return string.Empty;
            }
        }

        /// <summary>Length in user-visible characters (grapheme clusters).</summary>
        public static int GraphemeLength(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return new StringInfo(value).LengthInTextElements;
        }

        // ── Character classification ──────────────────────────────────────────

        /// <summary>
        /// Whitespace in the backend's broad sense: everything Java's
        /// Character.isWhitespace accepts plus every Unicode space separator. Written out
        /// explicitly rather than deferring to char.IsWhiteSpace, which covers a
        /// different set and would put a handful of control characters in the wrong
        /// rejection bucket.
        /// </summary>
        static bool IsUnicodeWhitespace(int codePoint)
        {
            if (codePoint >= 0x09 && codePoint <= 0x0D) return true;   // tab, LF, VT, FF, CR
            if (codePoint >= 0x1C && codePoint <= 0x1F) return true;   // file/group/record/unit separators
            if (codePoint > 0xFFFF) return false;                      // no separator lives above the BMP

            var category = CharUnicodeInfo.GetUnicodeCategory((char)codePoint);
            return category == UnicodeCategory.SpaceSeparator
                || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator;
        }

        /// <summary>Letters of any script, decimal digits, combining marks, and underscore.</summary>
        static bool IsAllowedCodePoint(string text, int index, int codePoint)
        {
            if (codePoint == '_') return true;

            var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.NonSpacingMark:        // Mn - Devanagari virama, Arabic harakat
                case UnicodeCategory.SpacingCombiningMark:  // Mc - Devanagari matras
                    return true;
                default:
                    return false;
            }
        }

        // ── Code point walking (mirrors Java's codePointAt / codePointBefore) ──
        // An unpaired surrogate yields the surrogate value itself and a size of 1, which
        // is what Java does and what keeps it falling through to INVALID_CHARACTER.

        static int CodePointAt(string text, int index, out int size)
        {
            char high = text[index];
            if (char.IsHighSurrogate(high) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                size = 2;
                return char.ConvertToUtf32(high, text[index + 1]);
            }
            size = 1;
            return high;
        }

        static int CodePointBefore(string text, int index, out int size)
        {
            char low = text[index - 1];
            if (char.IsLowSurrogate(low) && index - 2 >= 0 && char.IsHighSurrogate(text[index - 2]))
            {
                size = 2;
                return char.ConvertToUtf32(text[index - 2], low);
            }
            size = 1;
            return low;
        }
    }
}
