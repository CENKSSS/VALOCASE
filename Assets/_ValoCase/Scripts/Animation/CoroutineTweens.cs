using System;
using System.Collections;
using UnityEngine;

namespace ValoCase.Animation
{
    public sealed class CoroutineFloatTween : ITweenHandle
    {
        readonly MonoBehaviour _host;
        readonly Coroutine _routine;

        public CoroutineFloatTween(MonoBehaviour host, float from, float to, float duration, Action<float> onUpdate, Action onComplete, EaseMode ease)
        {
            _host = host;
            _routine = host.StartCoroutine(Run(from, to, duration, onUpdate, onComplete, ease));
        }

        public void Kill()
        {
            if (_host != null && _routine != null) _host.StopCoroutine(_routine);
        }

        static IEnumerator Run(float from, float to, float duration, Action<float> onUpdate, Action onComplete, EaseMode ease)
        {
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / duration);
                var eased = ApplyEase(p, ease);
                onUpdate?.Invoke(Mathf.Lerp(from, to, eased));
                yield return null;
            }

            onUpdate?.Invoke(to);
            onComplete?.Invoke();
        }

        internal static float ApplyEase(float t, EaseMode mode) => mode switch
        {
            EaseMode.OutCubic => 1f - Mathf.Pow(1f - t, 3f),
            EaseMode.OutQuint => 1f - Mathf.Pow(1f - t, 5f),
            EaseMode.InOutQuad => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f,
            _ => t
        };
    }

    public sealed class CoroutineAnchorXTween : ITweenHandle
    {
        readonly MonoBehaviour _host;
        readonly Coroutine _routine;

        public CoroutineAnchorXTween(RectTransform target, MonoBehaviour host, float toX, float duration, Action onComplete, EaseMode ease)
        {
            _host = host;
            _routine = host.StartCoroutine(Run(target, toX, duration, onComplete, ease));
        }

        public void Kill()
        {
            if (_host != null && _routine != null) _host.StopCoroutine(_routine);
        }

        static IEnumerator Run(RectTransform target, float toX, float duration, Action onComplete, EaseMode ease)
        {
            var from = target.anchoredPosition.x;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var p = CoroutineFloatTween.ApplyEase(Mathf.Clamp01(t / duration), ease);
                var pos = target.anchoredPosition;
                pos.x = Mathf.Lerp(from, toX, p);
                target.anchoredPosition = pos;
                yield return null;
            }

            var final = target.anchoredPosition;
            final.x = toX;
            target.anchoredPosition = final;
            onComplete?.Invoke();
        }
    }
}
