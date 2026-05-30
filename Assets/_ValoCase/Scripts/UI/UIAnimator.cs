using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// Static coroutine helpers for ValoCase lobby UI animations.
    /// All routines use unscaled time so they keep running while the game is paused.
    /// Callers own the lifetime: start via MonoBehaviour.StartCoroutine and stop via
    /// StopAllCoroutines in OnDisable to avoid leaks.
    /// </summary>
    public static class UIAnimator
    {
        // ── Looping: opacity pulse ────────────────────────────────────────────
        public static IEnumerator PulseOpacity(Graphic g, float min, float max, float duration)
        {
            if (g == null) yield break;
            float half = Mathf.Max(0.01f, duration * 0.5f);
            while (true)
            {
                yield return Lerp(half, t => SetAlpha(g, Mathf.Lerp(min, max, t)));
                yield return Lerp(half, t => SetAlpha(g, Mathf.Lerp(max, min, t)));
            }
        }

        // ── Looping: glow pulse via Shadow/Outline effect color alpha ─────────
        public static IEnumerator PulseGlow(Graphic g, Color glowColor, float duration)
        {
            if (g == null) yield break;
            var shadow = g.GetComponent<Shadow>();
            if (shadow == null) shadow = g.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(0f, 0f);
            float half = Mathf.Max(0.01f, duration * 0.5f);
            while (true)
            {
                yield return Lerp(half, t => shadow.effectColor = WithA(glowColor, Mathf.Lerp(0.2f, 0.8f, t)));
                yield return Lerp(half, t => shadow.effectColor = WithA(glowColor, Mathf.Lerp(0.8f, 0.2f, t)));
            }
        }

        // ── One-shot: press scale and release ─────────────────────────────────
        public static IEnumerator ScalePress(Transform t, float targetScale, float duration)
        {
            if (t == null) yield break;
            Vector3 start = Vector3.one;
            Vector3 target = Vector3.one * targetScale;
            float half = Mathf.Max(0.01f, duration * 0.5f);
            yield return Lerp(half, p => t.localScale = Vector3.Lerp(start, target, p));
            yield return Lerp(half, p => t.localScale = Vector3.Lerp(target, start, p));
            t.localScale = start;
        }

        // ── One-shot: slide in from bottom ────────────────────────────────────
        public static IEnumerator SlideFromBottom(RectTransform rt, float duration)
        {
            if (rt == null) yield break;
            Vector2 end   = rt.anchoredPosition;
            Vector2 start = end - new Vector2(0f, rt.rect.height > 0f ? rt.rect.height : 844f);
            rt.anchoredPosition = start;
            yield return Lerp(duration, t => rt.anchoredPosition = Vector2.Lerp(start, end, EaseOut(t)));
            rt.anchoredPosition = end;
        }

        // ── One-shot: slide in from right (push left) ─────────────────────────
        public static IEnumerator SlideLeft(RectTransform rt, float duration)
        {
            if (rt == null) yield break;
            Vector2 end   = rt.anchoredPosition;
            Vector2 start = end + new Vector2(rt.rect.width > 0f ? rt.rect.width : 390f, 0f);
            rt.anchoredPosition = start;
            yield return Lerp(duration, t => rt.anchoredPosition = Vector2.Lerp(start, end, EaseOut(t)));
            rt.anchoredPosition = end;
        }

        // ── One-shot: cross-fade two canvas groups ────────────────────────────
        public static IEnumerator CrossFade(CanvasGroup from, CanvasGroup to, float duration)
        {
            if (to != null)
            {
                to.gameObject.SetActive(true);
                to.alpha = 0f;
                to.blocksRaycasts = false;
            }
            if (from != null) from.blocksRaycasts = false;

            yield return Lerp(duration, t =>
            {
                if (from != null) from.alpha = 1f - t;
                if (to   != null) to.alpha   = t;
            });

            if (from != null)
            {
                from.alpha = 0f;
                from.gameObject.SetActive(false);
            }
            if (to != null)
            {
                to.alpha = 1f;
                to.interactable = true;
                to.blocksRaycasts = true;
            }
        }

        // ── Looping: animated "...", "..", "." suffix on a TMP label ──────────
        public static IEnumerator DottedSuffix(TMP_Text t, string baseText, float interval)
        {
            if (t == null) yield break;
            int step = 0;
            var wait = new WaitForSecondsRealtime(Mathf.Max(0.05f, interval));
            while (true)
            {
                string dots = new string('.', (step % 3) + 1);
                t.text = baseText + dots;
                step++;
                yield return wait;
            }
        }

        // ── Internals ─────────────────────────────────────────────────────────
        static IEnumerator Lerp(float duration, System.Action<float> step)
        {
            if (duration <= 0f) { step(1f); yield break; }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                step(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            step(1f);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        static void SetAlpha(Graphic g, float a)
        {
            var c = g.color; c.a = a; g.color = c;
        }

        static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
