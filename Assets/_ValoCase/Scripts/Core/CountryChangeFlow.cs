using System;

namespace ValoCase.Core
{
    /// <summary>
    /// The rules of a Settings country change: pick → confirm → one request → apply only
    /// what the server echoed.
    ///
    /// Lives here rather than inside SettingsScreen so every rule is testable without a
    /// Canvas, and because each one exists to prevent a specific quiet failure:
    ///
    ///   * <see cref="Select"/> normalises through CountryCatalog, so a lower-case code
    ///     or a localized name can never become the request value.
    ///   * <see cref="Confirm"/> refuses to fire while a request is in flight — a rapid
    ///     double tap produces exactly one PATCH, not two.
    ///   * <see cref="CurrentCode"/> changes only inside the success callback, with the
    ///     server's echo. A failed or abandoned change leaves the old country exactly as
    ///     it was, on screen and in the save.
    ///
    /// The save delegate is injected: the screen hands in the backend call (or its local
    /// offline fallback), and tests hand in a recorder.
    /// </summary>
    public sealed class CountryChangeFlow
    {
        /// <summary>Shape of GameContext.SaveCountryBackend: code in, echo or error out.</summary>
        public delegate void SaveCountryDelegate(string countryCode, Action<string> onSaved, Action<string> onFailed);

        public enum Phase
        {
            Idle,
            AwaitingConfirmation,
            Saving
        }

        readonly SaveCountryDelegate _save;
        readonly Action<string> _onApplied;
        readonly Action<string> _onFailed;

        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>The country the account has now. Empty when none is set yet.</summary>
        public string CurrentCode { get; private set; }

        /// <summary>The selection waiting for the player's confirmation. Empty outside a change.</summary>
        public string PendingCode { get; private set; } = string.Empty;

        public CountryChangeFlow(string currentCode, SaveCountryDelegate save,
                                 Action<string> onApplied, Action<string> onFailed)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _onApplied = onApplied;
            _onFailed = onFailed;
            CurrentCode = CountryCatalog.Normalize(currentCode);
        }

        /// <summary>
        /// Takes the picker's result. True when a confirmation should now be shown; false
        /// when there is nothing to confirm — an invalid code, the country the account
        /// already has, or a change already being saved.
        /// </summary>
        public bool Select(string pickedCode)
        {
            if (State == Phase.Saving) return false;

            var normalized = CountryCatalog.Normalize(pickedCode);
            if (normalized.Length == 0) return false;
            if (normalized == CurrentCode) return false;

            PendingCode = normalized;
            State = Phase.AwaitingConfirmation;
            return true;
        }

        /// <summary>The player declined the confirmation. Nothing was sent, nothing changes.</summary>
        public void Cancel()
        {
            if (State != Phase.AwaitingConfirmation) return;
            PendingCode = string.Empty;
            State = Phase.Idle;
        }

        /// <summary>
        /// Sends the pending change. True when a request actually left; false otherwise —
        /// including a second tap while the first request is still in flight, which is the
        /// double-click guard.
        /// </summary>
        public bool Confirm()
        {
            if (State != Phase.AwaitingConfirmation) return false;

            State = Phase.Saving;
            var requested = PendingCode;

            _save(requested,
                saved =>
                {
                    if (State != Phase.Saving) return;   // stale callback after completion
                    // The server echoes the stored code; it is the one truth. The code we
                    // asked for is only the fallback for a backend that stores without
                    // echoing.
                    var applied = CountryCatalog.Normalize(saved);
                    if (applied.Length == 0) applied = requested;
                    CurrentCode = applied;
                    PendingCode = string.Empty;
                    State = Phase.Idle;
                    _onApplied?.Invoke(applied);
                },
                err =>
                {
                    if (State != Phase.Saving) return;
                    // The old country stays: nothing was applied, so nothing changes.
                    PendingCode = string.Empty;
                    State = Phase.Idle;
                    _onFailed?.Invoke(err);
                });
            return true;
        }

        /// <summary>
        /// Re-reads the stored country when the screen reopens. Ignored mid-change so a
        /// refresh cannot yank the state out from under an open confirmation or request.
        /// </summary>
        public void SyncCurrent(string code)
        {
            if (State != Phase.Idle) return;
            CurrentCode = CountryCatalog.Normalize(code);
        }
    }
}
