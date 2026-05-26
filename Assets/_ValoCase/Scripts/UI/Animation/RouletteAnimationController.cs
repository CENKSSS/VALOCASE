using System;
using System.Collections;
using UnityEngine;

namespace ValoCase.UI.Animation
{
    /// <summary>
    /// Stand-alone slot-machine spin controller.
    /// Pure visual — given a content RectTransform and a target X offset,
    /// scrolls the content over a duration with configurable easing.
    ///
    /// Knows NOTHING about skins, RNG, rewards, sessions, or VP.
    /// The caller populates the content with card visuals BEFORE calling Play().
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RouletteAnimationController : MonoBehaviour
    {
        public enum State { Idle, Spinning, Settled }

        [SerializeField] float defaultDuration = 4.0f;

        public State CurrentState { get; private set; } = State.Idle;
        public bool  IsSpinning   => CurrentState == State.Spinning;

        /// <summary>Fired right after the spin coroutine finishes (UI-thread safe).</summary>
        public event Action OnSpinComplete;

        Coroutine _spinCo;

        /// <summary>
        /// Spins content so its anchoredPosition.x interpolates from current → targetX.
        /// </summary>
        /// <param name="content">Strip that gets moved (typically a long row of cards).</param>
        /// <param name="targetX">Final anchoredPosition.x — caller computes this from card stride.</param>
        /// <param name="duration">Override; null = defaultDuration.</param>
        /// <param name="ease">Override; null = QuintOut.</param>
        public Coroutine Play(RectTransform content, float targetX,
            float? duration = null, UIAnimationService.EaseFn ease = null)
        {
            if (content == null) return null;
            Stop();

            CurrentState = State.Spinning;
            _spinCo = StartCoroutine(SpinCo(content, targetX,
                duration ?? defaultDuration,
                ease ?? Easing.QuintOut));
            return _spinCo;
        }

        public void Stop()
        {
            if (_spinCo != null) StopCoroutine(_spinCo);
            _spinCo = null;
            CurrentState = State.Idle;
        }

        public void SnapToTarget(RectTransform content, float targetX)
        {
            if (content == null) return;
            Stop();
            var pos = content.anchoredPosition;
            pos.x = targetX;
            content.anchoredPosition = pos;
            CurrentState = State.Settled;
        }

        IEnumerator SpinCo(RectTransform content, float toX, float duration, UIAnimationService.EaseFn ease)
        {
            var start = content.anchoredPosition;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var k = ease(Mathf.Clamp01(t / duration));
                content.anchoredPosition = new Vector2(Mathf.Lerp(start.x, toX, k), start.y);
                yield return null;
            }
            content.anchoredPosition = new Vector2(toX, start.y);
            CurrentState = State.Settled;
            _spinCo = null;
            OnSpinComplete?.Invoke();
        }
    }
}
