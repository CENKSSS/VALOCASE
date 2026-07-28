using System;
using System.Collections;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace ValoCase.Services.Ads
{
    // Real Google AdMob rewarded provider. Consent (UMP) runs first; the SDK initializes
    // only when ads can be requested. Rewards are granted strictly from the SDK's
    // user-earned-reward callback — early close, load failure, or show failure never grants.
    public sealed class AdMobRewardedAdService : MonoBehaviour, IRewardedAdService
    {
        const string Tag = "[Ads]";
        const string TestRewardedUnitId = "ca-app-pub-3940256099942544/5224354917";
        const float MaxRetrySeconds = 64f;
        const float ConsentTimeoutSeconds = 12f;

        static readonly Dictionary<string, string> ProductionUnitIds = new Dictionary<string, string>
        {
            { AdRewardTypes.EarnVp2x,     "ca-app-pub-8798966996914943/1954117591" },
            { AdRewardTypes.MarketVp2500, "ca-app-pub-8798966996914943/6819431463" },
            { AdRewardTypes.UpgradePlus5, "ca-app-pub-8798966996914943/6587497179" },
            { AdRewardTypes.Diamond1,     "ca-app-pub-8798966996914943/8409779531" },
        };

        readonly Dictionary<string, RewardedAd> _loadedAds = new Dictionary<string, RewardedAd>();
        readonly HashSet<string> _loading = new HashSet<string>();
        readonly Dictionary<string, int> _retryCounts = new Dictionary<string, int>();
        // UMP/init callbacks can arrive on a background thread; everything is marshalled
        // through this queue so a hidden thread exception can never kill the ad pipeline.
        readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        readonly object _queueLock = new object();

        bool _consentRequesting;
        bool _initStarted;
        bool _sdkInitialized;
        bool _showing;

        public bool IsReady => _sdkInitialized && !_showing;

        static bool UseTestUnits
        {
            get
            {
#if DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        public static AdMobRewardedAdService Create(Transform parent)
        {
            var go = new GameObject("AdMobRewardedAdService");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<AdMobRewardedAdService>();
        }

        void Start()
        {
            Debug.Log($"{Tag} AdMob service starting — testUnits={UseTestUnits}");
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            RequestConsent();
            StartCoroutine(ConsentTimeoutFallback());
        }

        // On some devices (observed with UMP "Publisher misconfiguration") the UMP callback
        // never reaches C#. Without this fallback the SDK would never initialize and every
        // rewarded button would stay dead.
        IEnumerator ConsentTimeoutFallback()
        {
            yield return new WaitForSecondsRealtime(ConsentTimeoutSeconds);
            if (_initStarted) yield break;
            var status = SafeConsentStatus();
            Debug.LogWarning($"{Tag} UMP consent timed out after {ConsentTimeoutSeconds}s — status={status} " +
                             $"canRequestAds={SafeCanRequestAds()}. Check AdMob Privacy & messaging configuration.");
            if (status == ConsentStatus.Required)
            {
                Debug.LogWarning($"{Tag} Consent is REQUIRED but unavailable — ads stay disabled.");
                yield break;
            }
            Debug.LogWarning($"{Tag} Initializing without completed consent flow (status={status}).");
            InitializeSdk();
        }

        static ConsentStatus SafeConsentStatus()
        {
            try { return ConsentInformation.ConsentStatus; }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} ConsentStatus threw — {e.Message}");
                return ConsentStatus.Unknown;
            }
        }

        void Update()
        {
            while (true)
            {
                Action action;
                lock (_queueLock)
                {
                    if (_mainThreadQueue.Count == 0) return;
                    action = _mainThreadQueue.Dequeue();
                }
                try { action(); }
                catch (Exception e) { Debug.LogError($"{Tag} Callback failed — {e}"); }
            }
        }

        void Post(Action action)
        {
            lock (_queueLock) _mainThreadQueue.Enqueue(action);
        }

        void RequestConsent()
        {
            if (_consentRequesting || _initStarted) return;
            _consentRequesting = true;
            Debug.Log($"{Tag} UMP consent update start");
            try
            {
                ConsentInformation.Update(new ConsentRequestParameters(), updateError => Post(() =>
                {
                    _consentRequesting = false;
                    if (updateError != null)
                    {
                        var status = SafeConsentStatus();
                        Debug.LogWarning($"{Tag} UMP consent update FAILED — code={updateError.ErrorCode} " +
                                         $"msg={updateError.Message} status={status} " +
                                         $"canRequestAds={SafeCanRequestAds()}");
                        if (status == ConsentStatus.Required)
                            Debug.LogWarning($"{Tag} Consent is REQUIRED but unavailable — ads stay disabled.");
                        else
                            InitializeSdk();
                        return;
                    }

                    Debug.Log($"{Tag} UMP consent update OK — status={ConsentInformation.ConsentStatus} " +
                              $"canRequestAds={SafeCanRequestAds()}");
                    ConsentForm.LoadAndShowConsentFormIfRequired(formError => Post(() =>
                    {
                        if (formError != null)
                            Debug.LogWarning($"{Tag} UMP consent form FAILED — code={formError.ErrorCode} msg={formError.Message}");

                        Debug.Log($"{Tag} UMP consent flow done — status={ConsentInformation.ConsentStatus} " +
                                  $"canRequestAds={SafeCanRequestAds()}");
                        if (SafeCanRequestAds()) InitializeSdk();
                        else Debug.LogWarning($"{Tag} Ads unavailable — consent not granted (status={ConsentInformation.ConsentStatus})");
                    }));
                }));
            }
            catch (Exception e)
            {
                _consentRequesting = false;
                Debug.LogError($"{Tag} UMP consent update threw — {e}");
            }
        }

        static bool SafeCanRequestAds()
        {
            try { return ConsentInformation.CanRequestAds(); }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} CanRequestAds threw — {e.Message}");
                return false;
            }
        }

        void InitializeSdk()
        {
            if (_initStarted) return;
            _initStarted = true;
            Debug.Log($"{Tag} MobileAds.Initialize start");
            MobileAds.Initialize(status => Post(() =>
            {
                _sdkInitialized = true;
                Debug.Log($"{Tag} MobileAds.Initialize DONE — adapters={DescribeAdapters(status)}");
                foreach (var placementId in ProductionUnitIds.Keys)
                    Load(placementId);
            }));
        }

        static string DescribeAdapters(InitializationStatus status)
        {
            if (status == null) return "<null>";
            var map = status.getAdapterStatusMap();
            if (map == null || map.Count == 0) return "<none>";
            var parts = new List<string>(map.Count);
            foreach (var kv in map)
                parts.Add($"{kv.Key}={kv.Value?.InitializationState}");
            return string.Join(", ", parts);
        }

        static string ResolveUnitId(string placementId)
        {
            if (!ProductionUnitIds.TryGetValue(placementId, out var unitId)) return null;
            return UseTestUnits ? TestRewardedUnitId : unitId;
        }

        void Load(string placementId)
        {
            var unitId = ResolveUnitId(placementId);
            if (unitId == null)
            {
                Debug.LogWarning($"{Tag} Load skipped — unknown placement '{placementId}'");
                return;
            }
            if (_loading.Contains(placementId)) return;
            if (_loadedAds.TryGetValue(placementId, out var existing) && existing != null && existing.CanShowAd()) return;

            _loading.Add(placementId);
            Debug.Log($"{Tag} Load start — placement={placementId} unit={unitId}");
            RewardedAd.Load(unitId, new AdRequest(), (ad, error) => Post(() =>
            {
                _loading.Remove(placementId);
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"{Tag} Load FAILED — placement={placementId} " +
                                     $"code={error?.GetCode()} msg={error?.GetMessage() ?? "null ad"}");
                    ScheduleRetry(placementId);
                    return;
                }
                _retryCounts[placementId] = 0;
                _loadedAds[placementId] = ad;
                Debug.Log($"{Tag} Load OK — placement={placementId}");
            }));
        }

        void ScheduleRetry(string placementId)
        {
            _retryCounts.TryGetValue(placementId, out var count);
            _retryCounts[placementId] = count + 1;
            var delay = Mathf.Min(Mathf.Pow(2f, count + 1), MaxRetrySeconds);
            Debug.Log($"{Tag} Load retry in {delay:0}s — placement={placementId} attempt={count + 1}");
            StartCoroutine(RetryAfter(placementId, delay));
        }

        IEnumerator RetryAfter(string placementId, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Load(placementId);
        }

        public void Show(string placementId, Action<RewardedAdResult, string> onResult)
        {
            Debug.Log($"{Tag} Show requested — placement={placementId}");
            if (_showing)
            {
                Debug.LogWarning($"{Tag} Show blocked — another ad is already showing");
                onResult?.Invoke(RewardedAdResult.Failed, null);
                return;
            }
            if (!_sdkInitialized)
            {
                Debug.LogWarning($"{Tag} Show blocked — SDK not initialized " +
                                 $"(consentRequesting={_consentRequesting} initStarted={_initStarted} " +
                                 $"canRequestAds={SafeCanRequestAds()})");
                if (!_initStarted) RequestConsent();
                onResult?.Invoke(RewardedAdResult.Failed, null);
                return;
            }
            if (!_loadedAds.TryGetValue(placementId, out var ad) || ad == null || !ad.CanShowAd())
            {
                Debug.LogWarning($"{Tag} Show blocked — ad not loaded (placement={placementId} " +
                                 $"loading={_loading.Contains(placementId)})");
                Load(placementId);
                onResult?.Invoke(RewardedAdResult.Failed, null);
                return;
            }

            _loadedAds.Remove(placementId);
            var token = $"admob:{placementId}:{Guid.NewGuid():N}";
            ad.SetServerSideVerificationOptions(
                new ServerSideVerificationOptions { CustomData = token });

            var earned = false;
            var finished = false;
            void Finish(RewardedAdResult result, string resultToken)
            {
                if (finished) return;
                finished = true;
                _showing = false;
                ad.Destroy();
                Load(placementId);
                onResult?.Invoke(result, resultToken);
            }

            ad.OnAdFullScreenContentClosed += () =>
            {
                Debug.Log($"{Tag} Ad closed — placement={placementId} earned={earned}");
                Finish(earned ? RewardedAdResult.Completed : RewardedAdResult.Cancelled, earned ? token : null);
            };
            ad.OnAdFullScreenContentFailed += err =>
            {
                Debug.LogWarning($"{Tag} Show FAILED — placement={placementId} " +
                                 $"code={err?.GetCode()} msg={err?.GetMessage()}");
                Finish(RewardedAdResult.Failed, null);
            };

            _showing = true;
            Debug.Log($"{Tag} Showing ad — placement={placementId}");
            ad.Show(reward =>
            {
                earned = true;
                Debug.Log($"{Tag} Reward earned — placement={placementId} amount={reward?.Amount}");
            });
        }
    }
}
