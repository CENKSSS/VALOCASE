using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValoCase.Animation;
using ValoCase.Audio;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Haptics;
using ValoCase.Pooling;
using ValoCase.Services;
using ValoCase.UI;

namespace ValoCase.CaseOpening
{
    public sealed class CaseSpinController : MonoBehaviour
    {
        [SerializeField] RectTransform reelContent;
        [SerializeField] float itemWidth = 220f;
        [SerializeField] float spinDuration = GameConstants.CaseSpinDurationSeconds;
        [SerializeField] RectTransform centerMarker;
        [SerializeField] UltraRevealEffect ultraRevealEffect;

        [Header("Mobile Focus Style")]
        [SerializeField] float preBounceStrength = 0f;
        [SerializeField] float postBounceStrength = 8f;
        [SerializeField] float postBounceDuration = 0.18f;
        [SerializeField] UnityEngine.UI.Image centerLine;

        readonly List<ReelItemView> _activeItems = new();

        ITweenHandle _spinTween;
        Coroutine _highlightRoutine;
        Coroutine _bounceRoutine;
        bool _isSpinning;
        Action<SkinDefinitionSO> _onComplete;

        // Bug fix: store winner and winnerIndex to avoid reading wrong item
        SkinDefinitionSO _predeterminedWinner;
        int _winnerIndex;
        float _targetX;

        public bool IsSpinning => _isSpinning;

        public void BeginSpin(CaseDefinitionSO caseDef, SkinDefinitionSO predeterminedWinner, Action<SkinDefinitionSO> onComplete)
        {
            if (_isSpinning || caseDef == null || predeterminedWinner == null) return;
            if (reelContent == null || PoolManager.Instance == null)
            {
                onComplete?.Invoke(predeterminedWinner);
                return;
            }

            ClearReel();  // release any previous reel state before setting new
            // Mobile style: hide the old flashing center line — the focus frame
            // (built by CaseOpeningScreen) and item scaling handle the highlight.
            if (centerLine != null) centerLine.enabled = false;
            _onComplete = onComplete;
            _predeterminedWinner = predeterminedWinner;
            _isSpinning = true;

            var total = GameConstants.ReelPaddingItems + GameConstants.ReelVisibleItemCount;
            _winnerIndex = total - Mathf.CeilToInt(GameConstants.ReelVisibleItemCount / 2f) - 2;
            var strip = CaseReelBuilder.BuildReelStrip(caseDef, predeterminedWinner, total, _winnerIndex);

            for (var i = 0; i < strip.Count; i++)
            {
                var view = PoolManager.Instance.GetReelItem();
                view.transform.SetParent(reelContent, false);
                view.Bind(strip[i], GameContext.Instance?.RarityVisuals);
                var rt = view.RectTransform;
                rt.anchoredPosition = new Vector2(i * itemWidth, 0f);
                _activeItems.Add(view);
            }

            // CS:GO style: start slightly past center to give a pre-bounce feel.
            // Setting this BEFORE measuring so items have stable world positions to read.
            reelContent.anchoredPosition = new Vector2(preBounceStrength, 0f);

            // Pivot-agnostic target calculation:
            // Force the layout so RectTransform world positions are valid, then
            // measure the actual offset (in the viewport's local space) needed to
            // bring item[winnerIndex] under the center marker. This works for any
            // anchor/pivot setup on items, reelContent, or the marker.
            Canvas.ForceUpdateCanvases();
            var viewport = reelContent.parent as RectTransform;
            if (viewport != null && _activeItems.Count > _winnerIndex)
            {
                var winnerWorld = _activeItems[_winnerIndex].RectTransform.position;
                var markerWorld = centerMarker != null ? centerMarker.position : viewport.position;
                var winnerInViewport = viewport.InverseTransformPoint(winnerWorld);
                var markerInViewport = viewport.InverseTransformPoint(markerWorld);
                var deltaX = markerInViewport.x - winnerInViewport.x;
                _targetX = reelContent.anchoredPosition.x + deltaX;

                Debug.Log($"[SPIN] BeginSpin — winner='{predeterminedWinner.SkinName}' " +
                          $"winnerIndex={_winnerIndex} placedSkin='{_activeItems[_winnerIndex].Skin?.SkinName}' " +
                          $"deltaX={deltaX:F1} targetX={_targetX:F1}");
            }
            else
            {
                // Defensive fallback
                _targetX = -(_winnerIndex * itemWidth);
                Debug.LogWarning($"[SPIN] BeginSpin — viewport/items missing, using fallback targetX={_targetX}");
            }

            SoundManager.Instance?.Play(SoundId.CaseSpinLoop);

            // Spin slightly past target then snap back for bounce feel
            var overshootX = _targetX - postBounceStrength;
            _spinTween = TweenFacade.MoveAnchorX(reelContent, this, overshootX, spinDuration, OnSpinOvershoot, EaseMode.OutQuint);

            if (_highlightRoutine != null) StopCoroutine(_highlightRoutine);
            _highlightRoutine = StartCoroutine(HighlightCenterItem());
        }

        public void Skip()
        {
            if (!_isSpinning) return;
            TweenFacade.Kill(_spinTween);
            if (_highlightRoutine != null) { StopCoroutine(_highlightRoutine); _highlightRoutine = null; }
            if (_bounceRoutine != null) { StopCoroutine(_bounceRoutine); _bounceRoutine = null; }

            // Snap to correct winner position
            var pos = reelContent.anchoredPosition;
            pos.x = _targetX;
            reelContent.anchoredPosition = pos;

            OnSpinComplete();
        }

        void OnSpinOvershoot()
        {
            if (_bounceRoutine != null) StopCoroutine(_bounceRoutine);
            _bounceRoutine = StartCoroutine(BounceToTarget());
        }

        IEnumerator BounceToTarget()
        {
            var from = reelContent.anchoredPosition.x;
            var t = 0f;
            while (t < postBounceDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / postBounceDuration);
                // Elastic-style settle
                var eased = 1f - Mathf.Pow(1f - p, 3f);
                var pos = reelContent.anchoredPosition;
                pos.x = Mathf.Lerp(from, _targetX, eased);
                reelContent.anchoredPosition = pos;
                yield return null;
            }

            var final = reelContent.anchoredPosition;
            final.x = _targetX;
            reelContent.anchoredPosition = final;
            OnSpinComplete();
        }

        // Mobile style: scale up whichever reel item is closest to the center
        // marker, scaling neighbours down — a smooth "focus" feel instead of a
        // flashing line.
        IEnumerator HighlightCenterItem()
        {
            if (centerLine != null) centerLine.enabled = false;

            while (_isSpinning)
            {
                var markerX = centerMarker != null ? centerMarker.position.x
                            : (reelContent != null && reelContent.parent is RectTransform vp ? vp.position.x : 0f);

                foreach (var item in _activeItems)
                {
                    if (item == null) continue;
                    var rt = item.RectTransform;
                    var dist = Mathf.Abs(rt.position.x - markerX);
                    var norm = Mathf.Clamp01(dist / (itemWidth * 1.2f));
                    var scale = Mathf.Lerp(1.06f, 0.94f, norm);
                    rt.localScale = new Vector3(scale, scale, 1f);
                }
                yield return null;
            }
        }

        void OnSpinComplete()
        {
            _isSpinning = false;
            if (_highlightRoutine != null) { StopCoroutine(_highlightRoutine); _highlightRoutine = null; }
            if (centerLine != null) centerLine.enabled = false;

            // Use stored predetermined winner — same skin that was placed at winnerIndex.
            var winner = _predeterminedWinner;
            Debug.Log($"[SPIN] OnSpinComplete — winner='{winner?.SkinName ?? "NULL"}' " +
                      $"reelX={reelContent?.anchoredPosition.x} targetX={_targetX}");

            if (winner != null)
            {
                var isUltra = winner.Rarity == SkinRarity.Ultra;
                SoundManager.Instance?.Play(isUltra ? SoundId.UltraReveal : SoundId.CaseReveal);
                HapticManager.Instance?.Play(isUltra ? HapticPattern.UltraReveal : HapticPattern.Success);
                if (isUltra) ultraRevealEffect?.Play(winner);
            }

            _onComplete?.Invoke(winner);
            _predeterminedWinner = null;
        }

        public void ClearReel()
        {
            TweenFacade.Kill(_spinTween);
            if (_highlightRoutine != null) { StopCoroutine(_highlightRoutine); _highlightRoutine = null; }
            if (_bounceRoutine != null) { StopCoroutine(_bounceRoutine); _bounceRoutine = null; }
            _isSpinning = false;
            _predeterminedWinner = null;
            // Reset any focus-scaling applied during the spin before pooling.
            foreach (var v in _activeItems)
                if (v != null && v.RectTransform != null) v.RectTransform.localScale = Vector3.one;
            if (PoolManager.Instance != null)
                PoolManager.Instance.ReleaseAll(_activeItems);
            _activeItems.Clear();
        }

        void OnDisable() => ClearReel();
    }
}
