namespace ValoCase.Core
{
    /// <summary>
    /// The single rule deciding whether first-launch setup can be confirmed.
    ///
    /// It lives here rather than inside the popup so it can be tested without a Canvas,
    /// and so the setup panel and any later caller cannot disagree about what "ready"
    /// means. All three choices are required: a name the shared validator accepts, an
    /// avatar, and a country from the ISO catalog.
    /// </summary>
    public static class ProfileSetupGate
    {
        public static bool IsReady(string rawNickname, string avatarKey, string countryCode)
        {
            if (!NicknameValidator.TryValidate(rawNickname, out _, out _)) return false;
            if (string.IsNullOrEmpty(avatarKey)) return false;
            return CountryCatalog.IsValidCode(countryCode);
        }
    }
}
