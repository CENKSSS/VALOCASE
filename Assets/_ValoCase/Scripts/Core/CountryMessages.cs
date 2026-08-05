using UnityEngine;

namespace ValoCase.Core
{
    /// <summary>
    /// Player-facing text for the country feature, in the device language — the same
    /// smallest-possible arrangement as <see cref="NicknameMessages"/>, and for the same
    /// reason: the project has no localization system, and these strings are shared
    /// between the Settings row, its confirmation dialog, and its toasts.
    /// </summary>
    public static class CountryMessages
    {
        static bool IsTurkish => Application.systemLanguage == SystemLanguage.Turkish;

        /// <summary>Shown in place of a country by accounts that predate country selection.</summary>
        public static string NotSet => IsTurkish ? "Ayarlanmadı" : "Not set";

        /// <summary>The confirmation question before a country change is sent.</summary>
        public static string ConfirmChange => IsTurkish
            ? "Ülkenizi değiştirmek istediğinize emin misiniz?"
            : "Are you sure you want to change your country?";

        public static string ConfirmYes => IsTurkish ? "EVET"   : "YES";
        public static string ConfirmNo  => IsTurkish ? "VAZGEÇ" : "CANCEL";

        public static string Saving  => IsTurkish ? "KAYDEDİLİYOR..." : "SAVING...";
        public static string Updated => IsTurkish ? "Ülke güncellendi." : "Country updated.";

        /// <summary>Fallback when the backend failure carried no message of its own.</summary>
        public static string SaveFailed => IsTurkish ? "Ülke kaydedilemedi." : "Could not save country.";
    }
}
