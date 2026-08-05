using UnityEngine;

namespace ValoCase.Core
{
    /// <summary>
    /// Player-facing text for a refused nickname.
    ///
    /// The project has no localization system — no Unity Localization package, no string
    /// table, and UI text is written inline in each screen — so this is not routed
    /// through one. It is deliberately the smallest thing that gives the two screens a
    /// single source for these five strings: previously each screen carried its own
    /// wording, in different languages, describing a different rule than the one it
    /// actually enforced.
    ///
    /// Language follows the device, matching how the rest of the app is written for a
    /// Turkish-first audience with English as the fallback. If a real localization
    /// system arrives later, <see cref="For"/> is the only call site to redirect.
    /// </summary>
    public static class NicknameMessages
    {
        public static string For(NicknameRejectionReason reason)
        {
            return IsTurkish ? Turkish(reason) : English(reason);
        }

        static bool IsTurkish => Application.systemLanguage == SystemLanguage.Turkish;

        static string Turkish(NicknameRejectionReason reason)
        {
            switch (reason)
            {
                case NicknameRejectionReason.Blank:
                    return "Kullanıcı adı boş bırakılamaz.";
                case NicknameRejectionReason.TooShort:
                    return "Kullanıcı adı en az 3 karakter olmalıdır.";
                case NicknameRejectionReason.TooLong:
                    return "Kullanıcı adı en fazla 15 karakter olabilir.";
                case NicknameRejectionReason.Whitespace:
                    return "Kullanıcı adında boşluk kullanılamaz.";
                case NicknameRejectionReason.InvalidCharacter:
                    return "Kullanıcı adında yalnızca harf, rakam ve alt çizgi (_) kullanılabilir.";
                default:
                    return string.Empty;
            }
        }

        static string English(NicknameRejectionReason reason)
        {
            switch (reason)
            {
                case NicknameRejectionReason.Blank:
                    return "Nickname cannot be empty.";
                case NicknameRejectionReason.TooShort:
                    return "Nickname must contain at least 3 characters.";
                case NicknameRejectionReason.TooLong:
                    return "Nickname may contain at most 15 characters.";
                case NicknameRejectionReason.Whitespace:
                    return "Nickname cannot contain spaces.";
                case NicknameRejectionReason.InvalidCharacter:
                    return "Nickname may only contain letters, numbers and underscore (_).";
                default:
                    return string.Empty;
            }
        }
    }
}
