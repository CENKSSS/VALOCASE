using System.Collections.Generic;
using UnityEngine;
#if VALOCASE_FIREBASE
using Firebase.Extensions;
#endif

namespace ValoCase.Services.Backend
{
    /// <summary>
    /// Mirrors the onboarding funnel into Google Analytics for Firebase, so Google Ads
    /// can bid on what a person did after installing rather than on the install itself.
    ///
    /// The campaign's problem is not which countries it targets: bidding for install
    /// volume buys the cheapest possible install, which is why a day of ads produced
    /// 28 downloads, 8 launches and 2 registrations. An App campaign can only optimise
    /// for an in-app action if that action reaches Google, and the only supported route
    /// from a Unity client is this SDK.
    ///
    /// Compiled out unless VALOCASE_FIREBASE is defined, so the project still builds for
    /// anyone who has not imported the Firebase SDK.
    ///
    /// Event names are the backend's own wire names, which already satisfy Firebase's
    /// rules (snake_case, starts with a letter, under 40 characters), so the funnel
    /// reads the same in Firebase, in Google Ads and in the database.
    /// </summary>
    public static class AdsConversions
    {
#if VALOCASE_FIREBASE
        /// <summary>
        /// Firebase refuses to log until it has checked its Android dependencies, and
        /// that check is asynchronous. The first funnel steps — app_launched above all —
        /// fire before it finishes, so they are held here rather than dropped: losing
        /// the top of the funnel is exactly what would make the Ads bidding useless.
        /// </summary>
        static readonly List<string> Pending = new List<string>();
        static bool _ready;
        static bool _failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            try
            {
                Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
                {
                    if (task.IsFaulted || task.Result != Firebase.DependencyStatus.Available)
                    {
                        _failed = true;
                        Pending.Clear();
                        Debug.LogWarning("[AdsConversions] Firebase unavailable: "
                                         + (task.IsFaulted ? task.Exception?.Message : task.Result.ToString()));
                        return;
                    }

                    _ready = true;
                    foreach (var name in Pending) Send(name);
                    Pending.Clear();
                });
            }
            catch (System.Exception e)
            {
                _failed = true;
                Debug.LogWarning("[AdsConversions] Initialize failed: " + e.Message);
            }
        }

        static void Send(string eventName)
        {
            try
            {
                Firebase.Analytics.FirebaseAnalytics.LogEvent(eventName);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AdsConversions] LogEvent failed and was dropped: " + e.Message);
            }
        }
#endif

        /// <summary>
        /// Reports one funnel step. Never throws: an analytics failure must not be able
        /// to break the screen that triggered it.
        /// </summary>
        public static void Report(string eventName)
        {
#if VALOCASE_FIREBASE
            if (string.IsNullOrEmpty(eventName) || _failed) return;

            if (_ready) { Send(eventName); return; }

            // Bounded on purpose. A player who never gets past a broken dependency check
            // must not accumulate an unbounded list; nine events is the whole funnel.
            if (Pending.Count < 16) Pending.Add(eventName);
#endif
        }
    }
}
