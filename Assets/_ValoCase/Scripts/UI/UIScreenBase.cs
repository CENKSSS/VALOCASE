using System.Collections;
using UnityEngine;

namespace ValoCase.UI
{
    public abstract class UIScreenBase : MonoBehaviour
    {
        [SerializeField] CanvasGroup canvasGroup;
        [SerializeField] ScreenType screenType;
        [SerializeField] float fadeDuration = 0.25f;

        public ScreenType ScreenType => screenType;
        public bool IsVisible { get; private set; }

        /// <summary>Screens that must appear with zero delay (e.g. Settings)
        /// override this; the whole transition then skips the fade animation.</summary>
        public virtual bool OpensInstantly => false;

        Coroutine _fadeRoutine;

        const float SlideDuration = 0.18f;
        Vector2 _restPos;
        bool _restCaptured;

        void CaptureRest()
        {
            if (_restCaptured) return;
            _restPos = ((RectTransform)transform).anchoredPosition;
            _restCaptured = true;
        }

        public virtual void ShowImmediate()
        {
            CancelFade();
            CaptureRest();
            ((RectTransform)transform).anchoredPosition = _restPos;
            gameObject.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            IsVisible = true;
            HandleShown();
        }

        public virtual void HideImmediate()
        {
            // Keep-alive screens stay active while hidden, so a mid-flight fade
            // coroutine would survive and re-raise the alpha — cancel it here.
            CancelFade();
            CaptureRest();
            ((RectTransform)transform).anchoredPosition = _restPos;
            bool keepAlive = KeepAliveWhenHidden;
            if (keepAlive) EnsureCanvasGroupExists();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            IsVisible = false;
            if (!keepAlive || canvasGroup == null)
                gameObject.SetActive(false);
            OnHidden();
        }

        // Horizontal page slide. dir = +1 → forward (enter from right / exit to left);
        // dir = -1 → backward (enter from left / exit to right).
        public void SlideIn(int dir)
        {
            if (OpensInstantly) { ShowImmediate(); return; }
            CaptureRest();
            gameObject.SetActive(true);
            EnsureCanvasGroupExists();
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(Slide(dir, true));
        }

        public void SlideOut(int dir)
        {
            CaptureRest();
            EnsureCanvasGroupExists();
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(Slide(dir, false));
        }

        IEnumerator Slide(int dir, bool visibleAfter)
        {
            if (!visibleAfter && KeepAliveWhenHidden) EnsureCanvasGroupExists();

            var rt = (RectTransform)transform;
            float w = rt.rect.width > 1f ? rt.rect.width : 390f;

            if (visibleAfter)
            {
                if (canvasGroup != null) { canvasGroup.alpha = 1f; canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
                rt.anchoredPosition = _restPos + new Vector2(dir * w, 0f);
            }
            else if (canvasGroup != null) canvasGroup.blocksRaycasts = false;

            Vector2 from = rt.anchoredPosition;
            Vector2 to   = visibleAfter ? _restPos : _restPos - new Vector2(dir * w, 0f);

            float t = 0f;
            while (t < SlideDuration)
            {
                t += Time.unscaledDeltaTime;
                rt.anchoredPosition = Vector2.Lerp(from, to, EaseOut(Mathf.Clamp01(t / SlideDuration)));
                yield return null;
            }
            rt.anchoredPosition = to;

            if (visibleAfter)
            {
                if (canvasGroup != null) { canvasGroup.alpha = 1f; canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; }
                IsVisible = true;
                OnShown();
            }
            else
            {
                if (canvasGroup != null) { canvasGroup.alpha = 0f; canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; }
                rt.anchoredPosition = _restPos;
                IsVisible = false;
                OnHidden();
                if (!KeepAliveWhenHidden) gameObject.SetActive(false);
            }
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        public void ShowAnimated()
        {
            if (OpensInstantly)
            {
                ShowImmediate();
                return;
            }

            gameObject.SetActive(true);
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(Fade(1f, true));
        }

        public void HideAnimated()
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(Fade(0f, false));
        }

        IEnumerator Fade(float target, bool visibleAfter)
        {
            if (!visibleAfter && KeepAliveWhenHidden) EnsureCanvasGroupExists();

            if (canvasGroup == null)
            {
                if (visibleAfter) ShowImmediate();
                else HideImmediate();
                yield break;
            }

            canvasGroup.blocksRaycasts = false;
            var start = canvasGroup.alpha;
            var t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = target;
            canvasGroup.interactable = visibleAfter;
            canvasGroup.blocksRaycasts = visibleAfter;
            IsVisible = visibleAfter;
            if (visibleAfter) HandleShown();
            else
            {
                OnHidden();
                if (!KeepAliveWhenHidden)
                    gameObject.SetActive(false);
            }
        }

        // Screens that must keep running while another screen is shown (e.g. an
        // active Case Battle under the Settings screen) opt in by overriding this.
        // The screen is faded out and made non-interactive but stays active, so
        // its coroutines and child overlays survive the navigation round-trip.
        protected virtual bool KeepAliveWhenHidden => false;

        void CancelFade()
        {
            if (_fadeRoutine == null) return;
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }

        void EnsureCanvasGroupExists()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // Call this from a derived screen whenever an async operation (e.g. case spin)
        // finishes and input needs to be restored without re-triggering OnShown.
        protected void EnsureInteractive()
        {
            if (canvasGroup == null) return;
            canvasGroup.interactable    = true;
            canvasGroup.blocksRaycasts  = true;
            canvasGroup.alpha           = 1f;
        }

        void HandleShown()
        {
            OnShown();
            if (AutoWireButtonClicks) UIBuild.WireButtonClicks(gameObject);
        }

        // Screens that manage their own click sounds (e.g. via per-button handlers)
        // opt out so the recursive wiring never double-plays.
        protected virtual bool AutoWireButtonClicks => true;

        protected virtual void OnShown() { }
        protected virtual void OnHidden() { }

        /// <summary>Called when the already-active screen is navigated to again (e.g. the
        /// user taps the current tab in the bottom nav). Lets a screen reset its internal
        /// sub-state. Default is a no-op.</summary>
        public virtual void OnReselected() { }
    }
}
