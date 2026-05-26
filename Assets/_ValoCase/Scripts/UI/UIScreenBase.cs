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

        Coroutine _fadeRoutine;

        public virtual void ShowImmediate()
        {
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
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            IsVisible = false;
            gameObject.SetActive(false);
            OnHidden();
        }

        public void ShowAnimated()
        {
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
                gameObject.SetActive(false);
            }
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
