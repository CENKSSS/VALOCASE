#if VALOCASE_DOTWEEN
using System;
using DG.Tweening;
using UnityEngine;

namespace ValoCase.Animation
{
    public sealed class DotweenFloatTween : ITweenHandle
    {
        readonly Tween _tween;

        public DotweenFloatTween(float from, float to, float duration, Action<float> onUpdate, Action onComplete, EaseMode ease)
        {
            var value = from;
            _tween = DOTween.To(() => value, v =>
            {
                value = v;
                onUpdate?.Invoke(v);
            }, to, duration).SetEase(MapEase(ease)).SetUpdate(true).OnComplete(() => onComplete?.Invoke());
        }

        public void Kill() => _tween?.Kill();

        static Ease MapEase(EaseMode mode) => mode switch
        {
            EaseMode.OutCubic => Ease.OutCubic,
            EaseMode.OutQuint => Ease.OutQuint,
            EaseMode.InOutQuad => Ease.InOutQuad,
            _ => Ease.Linear
        };
    }

    public sealed class DotweenAnchorXTween : ITweenHandle
    {
        readonly Tween _tween;

        public DotweenAnchorXTween(RectTransform target, float toX, float duration, Action onComplete, EaseMode ease)
        {
            _tween = target.DOAnchorPosX(toX, duration).SetEase(MapEase(ease)).SetUpdate(true).OnComplete(() => onComplete?.Invoke());
        }

        public void Kill() => _tween?.Kill();

        static Ease MapEase(EaseMode mode) => mode switch
        {
            EaseMode.OutCubic => Ease.OutCubic,
            EaseMode.OutQuint => Ease.OutQuint,
            EaseMode.InOutQuad => Ease.InOutQuad,
            _ => Ease.Linear
        };
    }
}
#endif
