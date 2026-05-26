using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Animation
{
    /// <summary>
    /// Easing curves used by UI animations. All in [0,1] domain → [0,1] range.
    /// </summary>
    public static class Easing
    {
        public static float Linear(float t)       => t;
        public static float QuadIn(float t)       => t * t;
        public static float QuadOut(float t)      => 1f - (1f - t) * (1f - t);
        public static float CubicOut(float t)     => 1f - Mathf.Pow(1f - t, 3f);
        public static float QuintOut(float t)     => 1f - Mathf.Pow(1f - t, 5f);
        public static float ExpoOut(float t)      => Mathf.Approximately(t, 1f) ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        public static float BackOut(float t, float s = 1.70158f)
        {
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }

    /// <summary>
    /// Reusable UI animation primitives.
    ///
    /// Stateless static facade — every method returns an IEnumerator that the caller
    /// must drive via StartCoroutine on its own MonoBehaviour. This keeps the service
    /// free of singleton lifetime concerns.
    ///
    /// CONTRACT
    ///   • Pure visual side-effects (alpha, scale, position, color).
    ///   • No game logic, no service access, no data persistence.
    ///   • Caller is responsible for stopping coroutines if a target is destroyed.
    /// </summary>
    public static class UIAnimationService
    {
        public delegate float EaseFn(float t);

        // ── Fade (CanvasGroup) ────────────────────────────────────────────────
        public static IEnumerator Fade(CanvasGroup cg, float toAlpha, float duration,
            EaseFn ease = null, Action onComplete = null)
        {
            if (cg == null) { onComplete?.Invoke(); yield break; }
            ease ??= Easing.QuadOut;

            var from = cg.alpha;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, toAlpha, ease(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            cg.alpha = toAlpha;
            onComplete?.Invoke();
        }

        // ── Scale (Transform) ─────────────────────────────────────────────────
        public static IEnumerator Scale(Transform target, Vector3 toScale, float duration,
            EaseFn ease = null, Action onComplete = null)
        {
            if (target == null) { onComplete?.Invoke(); yield break; }
            ease ??= Easing.QuintOut;

            var from = target.localScale;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                target.localScale = Vector3.LerpUnclamped(from, toScale, ease(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            target.localScale = toScale;
            onComplete?.Invoke();
        }

        // ── Bounce: target scales down then snaps back via ease-out ──────────
        public static IEnumerator BounceScale(Transform target,
            float depressTo = 0.88f, float depressDur = 0.07f,
            float reboundDur = 0.10f, Action onMid = null)
        {
            if (target == null) yield break;

            var orig = target.localScale;
            var down = orig * depressTo;

            yield return Scale(target, down, depressDur, Easing.QuadIn);
            onMid?.Invoke();
            yield return Scale(target, orig, reboundDur, Easing.QuintOut);
        }

        // ── Pulse forever (returns coroutine that loops until externally stopped) ──
        public static IEnumerator PulseLoop(Transform target, float amplitude = 0.06f,
            float period = 1.4f, EaseFn ease = null)
        {
            if (target == null) yield break;
            ease ??= Easing.CubicOut;

            var baseScale = target.localScale;
            var t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                var phase = (Mathf.Sin(t * Mathf.PI * 2f / period) + 1f) * 0.5f;
                var k = ease(phase);
                target.localScale = baseScale * (1f + amplitude * k);
                yield return null;
            }
        }

        // ── Shake (anchoredPosition jitter) ───────────────────────────────────
        public static IEnumerator Shake(RectTransform target, float magnitude = 4f, float duration = 0.10f)
        {
            if (target == null) yield break;

            var origin = target.anchoredPosition;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var falloff = 1f - Mathf.Clamp01(t / duration);
                target.anchoredPosition = origin + new Vector2(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f)) * (magnitude * falloff);
                yield return null;
            }
            target.anchoredPosition = origin;
        }

        // ── Float-up text (position + fade) ──────────────────────────────────
        public static IEnumerator FloatUp(RectTransform target, CanvasGroup cg,
            Vector2 deltaPosition, float duration,
            EaseFn ease = null, Action onComplete = null)
        {
            if (target == null) { onComplete?.Invoke(); yield break; }
            ease ??= Easing.CubicOut;

            var startPos = target.anchoredPosition;
            var endPos   = startPos + deltaPosition;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var k = ease(Mathf.Clamp01(t / duration));
                target.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, k);
                if (cg != null) cg.alpha = Mathf.Lerp(1f, 0f, k);
                yield return null;
            }
            target.anchoredPosition = endPos;
            if (cg != null) cg.alpha = 0f;
            onComplete?.Invoke();
        }

        // ── Color flash (Image) — fades to color then back to original ───────
        public static IEnumerator ColorFlash(Image image, Color flashColor, float duration)
        {
            if (image == null) yield break;
            var orig = image.color;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);   // 0→1→0
                image.color = Color.Lerp(orig, flashColor, k);
                yield return null;
            }
            image.color = orig;
        }

        // ── Tween anchoredPosition.x to target ───────────────────────────────
        public static IEnumerator MoveX(RectTransform target, float toX, float duration,
            EaseFn ease = null, Action onComplete = null)
        {
            if (target == null) { onComplete?.Invoke(); yield break; }
            ease ??= Easing.QuintOut;

            var start = target.anchoredPosition;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var k = ease(Mathf.Clamp01(t / duration));
                target.anchoredPosition = new Vector2(Mathf.Lerp(start.x, toX, k), start.y);
                yield return null;
            }
            target.anchoredPosition = new Vector2(toX, start.y);
            onComplete?.Invoke();
        }
    }
}
