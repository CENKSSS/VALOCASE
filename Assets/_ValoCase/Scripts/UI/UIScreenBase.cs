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

        public virtual void ShowImmediate()
        {
            CancelFade();
            gameObject.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            IsVisible = true;
            OnShown();
        }

        public virtual void HideImmediate()
        {
            // Keep-alive screens stay active while hidden, so a mid-flight fade
            // coroutine would survive and re-raise the alpha — cancel it here.
            CancelFade();
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
            if (visibleAfter) OnShown();
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

        protected virtual void OnShown() { }
        protected virtual void OnHidden() { }
    }
}
