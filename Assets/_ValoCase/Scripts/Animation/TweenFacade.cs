using System;
using UnityEngine;

namespace ValoCase.Animation
{
    /// <summary>
    /// Wraps DOTween when VALOCASE_DOTWEEN is defined; otherwise uses coroutine-friendly fallbacks.
    /// Install DOTween and add Scripting Define Symbol: VALOCASE_DOTWEEN
    /// </summary>
    public static class TweenFacade
    {
        public static ITweenHandle ToFloat(MonoBehaviour host, float from, float to, float duration, Action<float> onUpdate, Action onComplete = null, EaseMode ease = EaseMode.OutCubic)
        {
#if VALOCASE_DOTWEEN
            return new DotweenFloatTween(from, to, duration, onUpdate, onComplete, ease);
#else
            return new CoroutineFloatTween(host, from, to, duration, onUpdate, onComplete, ease);
#endif
        }

        public static ITweenHandle MoveAnchorX(RectTransform target, MonoBehaviour host, float toX, float duration, Action onComplete = null, EaseMode ease = EaseMode.OutCubic)
        {
#if VALOCASE_DOTWEEN
            return new DotweenAnchorXTween(target, toX, duration, onComplete, ease);
#else
            return new CoroutineAnchorXTween(target, host, toX, duration, onComplete, ease);
#endif
        }

        public static void Kill(ITweenHandle handle) => handle?.Kill();
    }

    public enum EaseMode
    {
        Linear,
        OutCubic,
        OutQuint,
        InOutQuad
    }

    public interface ITweenHandle
    {
        void Kill();
    }
}
