using UnityEngine;

namespace ValoCase.Core
{
    /// <summary>
    /// What the player typed and picked on the first-launch setup panel, before an
    /// account exists to hold it.
    ///
    /// Registration is the only thing that can persist these choices server-side, and it
    /// runs last. Everything before it — a refused nickname, a failed request, an app the
    /// player swiped away while the picker was open — used to leave the panel empty on
    /// the next launch and ask for all three choices again. This keeps them.
    ///
    /// PlayerPrefs rather than the save file: the draft is pre-account state that belongs
    /// to the install, like the installation id, and writing it must not touch the save
    /// data a running session owns. <see cref="Flush"/> is separate from the setters so a
    /// per-keystroke nickname write does not hit the disk; the panel flushes when the app
    /// goes to the background, which is where an unfinished setup usually ends.
    /// </summary>
    public static class ProfileSetupDraft
    {
        const string PrefNickname = "valocase_setup_draft_nickname";
        const string PrefAvatarKey = "valocase_setup_draft_avatar_key";
        const string PrefCountryCode = "valocase_setup_draft_country_code";

        public static string Nickname => PlayerPrefs.GetString(PrefNickname, string.Empty);

        public static string AvatarKey => PlayerPrefs.GetString(PrefAvatarKey, string.Empty);

        /// <summary>Empty unless a code still in the catalog was stored.</summary>
        public static string CountryCode =>
            CountryCatalog.Normalize(PlayerPrefs.GetString(PrefCountryCode, string.Empty));

        public static void SetNickname(string value) =>
            PlayerPrefs.SetString(PrefNickname, value ?? string.Empty);

        public static void SetAvatarKey(string value) =>
            PlayerPrefs.SetString(PrefAvatarKey, value ?? string.Empty);

        public static void SetCountryCode(string value) =>
            PlayerPrefs.SetString(PrefCountryCode, CountryCatalog.Normalize(value));

        /// <summary>Writes pending changes to disk. Cheap enough to call on pause and on quit.</summary>
        public static void Flush() => PlayerPrefs.Save();

        /// <summary>Drops the draft once setup completed and the choices live on the account.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefNickname);
            PlayerPrefs.DeleteKey(PrefAvatarKey);
            PlayerPrefs.DeleteKey(PrefCountryCode);
            PlayerPrefs.Save();
        }
    }
}
