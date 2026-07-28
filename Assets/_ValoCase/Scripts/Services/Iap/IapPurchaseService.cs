using System;
using System.Collections;
using UnityEngine;

namespace ValoCase.Services.Iap
{
    /// <summary>
    /// USD diamond-purchase provider abstraction. A real Google Play Billing adapter
    /// implements this same interface and is swapped in later without touching callers
    /// (mirrors IRewardedAdService). onResult never implies diamonds were granted —
    /// the caller (GameContext) only credits the balance on IapPurchaseResult.Granted.
    /// </summary>
    public interface IIapPurchaseService
    {
        /// <summary>True only when purchases can actually be attempted (dev mock). False in any
        /// build where real billing isn't wired yet — callers must fail closed, not grant currency.</summary>
        bool IsAvailable { get; }

        void Purchase(IapPackageEntry package, Action<IapPurchaseResult, string> onResult);
    }

    /// <summary>
    /// Production placeholder — Google Play Billing is not integrated yet (developer
    /// verification pending). Always reports Unavailable and never grants diamonds.
    /// Selected automatically for Android release builds; replace with a real billing
    /// adapter once Play Console approval lands.
    /// </summary>
    public sealed class UnavailableIapPurchaseService : IIapPurchaseService
    {
        public bool IsAvailable => false;

        public void Purchase(IapPackageEntry package, Action<IapPurchaseResult, string> onResult)
            => onResult?.Invoke(IapPurchaseResult.Unavailable,
                "Purchase system not ready — pending Google Play approval.");
    }

    /// <summary>
    /// Development-only mock purchase provider. Simulates a short processing delay then
    /// reports success — no real money, no store, no backend call. Only ever selected
    /// outside Android release builds (see GameContext.InitializeServices), so it can
    /// never run in a shipped production build.
    /// </summary>
    public sealed class MockIapPurchaseService : MonoBehaviour, IIapPurchaseService
    {
        const float ProcessSeconds = 0.6f;

        bool _processing;
        public bool IsAvailable => true;

        public static MockIapPurchaseService Create(Transform parent)
        {
            var go = new GameObject("MockIapPurchaseService");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<MockIapPurchaseService>();
        }

        public void Purchase(IapPackageEntry package, Action<IapPurchaseResult, string> onResult)
        {
            if (_processing) { onResult?.Invoke(IapPurchaseResult.Failed, "Purchase already in progress."); return; }
            StartCoroutine(ProcessRoutine(package, onResult));
        }

        IEnumerator ProcessRoutine(IapPackageEntry package, Action<IapPurchaseResult, string> onResult)
        {
            _processing = true;
            yield return new WaitForSecondsRealtime(ProcessSeconds);
            _processing = false;
            onResult?.Invoke(IapPurchaseResult.Granted,
                $"DEV TEST PURCHASE — +{package.amount:N0} Diamonds (not a real purchase)");
        }
    }
}
