namespace ValoCase.Core
{
    /// <summary>
    /// The single rule deciding whether first-launch setup can be confirmed.
    ///
    /// It lives here rather than inside the popup so it can be tested without a Canvas,
    /// and so the setup panel and any later caller cannot disagree about what "ready"
    /// means.
    ///
    /// None of the three choices is required. The backend fills in whatever the player
    /// leaves out — a blank name becomes AgentXXXX, a blank country is stored as NULL,
    /// and registration never took an avatar in the first place — so demanding them here
    /// was friction the server did not ask for, on the one screen a player cannot get
    /// past. All three stay editable in Settings.
    ///
    /// That leaves one thing to gate, which is why the country and avatar are no longer
    /// arguments: a nickname the player actually typed and got wrong. Every rule in
    /// <see cref="NicknameValidator"/> applies to a non-empty name exactly as before,
    /// since the server refuses those with a 400. Only
    /// <see cref="NicknameRejectionReason.Blank"/> is let through.
    /// </summary>
    public static class ProfileSetupGate
    {
        public static bool IsReady(string rawNickname)
        {
            var reason = NicknameValidator.Classify(rawNickname);
            return reason == NicknameRejectionReason.None ||
                   reason == NicknameRejectionReason.Blank;
        }
    }
}
