using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Animation
{
    /// <summary>
    /// Battle-specific animation orchestrator. Owns the two RouletteAnimationController
    /// instances (player + opponent) and exposes a single coroutine that plays both
    /// roulettes in sync, then fires OnRoundAnimationComplete.
    ///
    /// Knows NOTHING about which side wins, the skin values, or the RNG outcome.
    /// The caller (orchestrator) tells it where each strip should land via
    /// `playerTargetX` / `opponentTargetX` and what label/badge text to flash.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaseBattleAnimation : MonoBehaviour
    {
        [SerializeField] RouletteAnimationController playerRoulette;
        [SerializeField] RouletteAnimationController opponentRoulette;
        [SerializeField] Transform                   winnerBadgeTarget; // optional
        [SerializeField] Image                       resultFlashImage;   // optional
        [SerializeField] TextMeshProUGUI             resultLabel;        // optional

        [SerializeField] float spinDuration = 4.0f;

        /// <summary>Fires after BOTH roulettes settle.</summary>
        public event Action OnRoundAnimationComplete;

        Coroutine _pulseCo;
        Coroutine _flashCo;
        Coroutine _roundCo;

        // ── Wire references at runtime (controller calls this after building UI) ──
        public void BindRoulettes(RouletteAnimationController player, RouletteAnimationController opponent)
        {
            playerRoulette   = player;
            opponentRoulette = opponent;
        }

        public void BindWinnerBadge(Transform badge) => winnerBadgeTarget = badge;
        public void BindResultFlash(Image flash, TextMeshProUGUI label)
        {
            resultFlashImage = flash;
            resultLabel      = label;
        }

        public bool IsAnimating =>
            (playerRoulette   != null && playerRoulette.IsSpinning) ||
            (opponentRoulette != null && opponentRoulette.IsSpinning) ||
            _roundCo != null;

        // ── Round animation ───────────────────────────────────────────────────
        public Coroutine PlayRound(RectTransform playerContent, float playerTargetX,
                                   RectTransform opponentContent, float opponentTargetX,
                                   float? duration = null)
        {
            StopRound();
            _roundCo = StartCoroutine(RoundCo(playerContent, playerTargetX,
                                              opponentContent, opponentTargetX,
                                              duration ?? spinDuration));
            return _roundCo;
        }

        public void StopRound()
        {
            if (_roundCo != null) StopCoroutine(_roundCo);
            _roundCo = null;
            if (playerRoulette   != null) playerRoulette.Stop();
            if (opponentRoulette != null) opponentRoulette.Stop();
        }

        IEnumerator RoundCo(RectTransform pContent, float pX,
                            RectTransform oContent, float oX,
                            float duration)
        {
            if (playerRoulette   != null) playerRoulette.Play(pContent, pX, duration);
            if (opponentRoulette != null) opponentRoulette.Play(oContent, oX, duration);

            // Wait both controllers (or duration as fallback) to settle.
            var t = 0f;
            while (t < duration + 0.05f)
            {
                t += Time.deltaTime;
                var pDone = playerRoulette   == null || !playerRoulette.IsSpinning;
                var oDone = opponentRoulette == null || !opponentRoulette.IsSpinning;
                if (pDone && oDone) break;
                yield return null;
            }
            _roundCo = null;
            OnRoundAnimationComplete?.Invoke();
        }

        // ── Winner badge pulse ────────────────────────────────────────────────
        public void StartWinnerPulse(float amplitude = 0.08f, float period = 1.2f)
        {
            StopWinnerPulse();
            if (winnerBadgeTarget == null) return;
            _pulseCo = StartCoroutine(UIAnimationService.PulseLoop(winnerBadgeTarget, amplitude, period));
        }

        public void StopWinnerPulse()
        {
            if (_pulseCo != null) StopCoroutine(_pulseCo);
            _pulseCo = null;
            if (winnerBadgeTarget != null) winnerBadgeTarget.localScale = Vector3.one;
        }

        // ── Result flash (success/fail color sweep with optional label) ──────
        public Coroutine PlayResultFlash(Color color, string label = null, float duration = 0.85f)
        {
            if (_flashCo != null) StopCoroutine(_flashCo);
            _flashCo = StartCoroutine(ResultFlashCo(color, label, duration));
            return _flashCo;
        }

        IEnumerator ResultFlashCo(Color color, string label, float duration)
        {
            if (resultLabel != null && label != null) resultLabel.text = label;

            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var a = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                if (resultFlashImage != null)
                    resultFlashImage.color = new Color(color.r, color.g, color.b, a * 0.55f);
                if (resultLabel != null)
                    resultLabel.color = new Color(color.r, color.g, color.b, a);
                yield return null;
            }
            if (resultFlashImage != null) resultFlashImage.color = new Color(0, 0, 0, 0);
            if (resultLabel != null) resultLabel.text = string.Empty;
            _flashCo = null;
        }

        void OnDisable()
        {
            StopRound();
            StopWinnerPulse();
            if (_flashCo != null) { StopCoroutine(_flashCo); _flashCo = null; }
        }
    }
}
