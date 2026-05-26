using System.Collections;
using TMPro;
using UnityEngine;
using ValoCase.Core;

namespace ValoCase.UI
{
    public sealed class VpCounterView : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] float animateDuration = 0.45f;
        [SerializeField] Color gainFlashColor = new(0.3f, 1f, 0.55f);
        [SerializeField] Color lossFlashColor = new(1f, 0.35f, 0.35f);

        int _displayed;
        Coroutine _anim;

        void OnEnable()
        {
            GameEvents.OnVpChanged += HandleVpChanged;
            RefreshImmediate();
        }

        void OnDisable() => GameEvents.OnVpChanged -= HandleVpChanged;

        void RefreshImmediate()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Vp == null) return;
            _displayed = ctx.Vp.Balance;
            if (label != null) label.text = FormatVp(_displayed);
        }

        void HandleVpChanged(int previous, int current)
        {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimateCounter(previous, current));
        }

        IEnumerator AnimateCounter(int from, int to)
        {
            if (label != null)
                label.color = to >= from ? gainFlashColor : lossFlashColor;

            var t = 0f;
            while (t < animateDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.SmoothStep(0f, 1f, t / animateDuration);
                _displayed = Mathf.RoundToInt(Mathf.Lerp(from, to, p));
                if (label != null) label.text = FormatVp(_displayed);
                yield return null;
            }

            _displayed = to;
            if (label != null)
            {
                label.text = FormatVp(_displayed);
                label.color = Color.white;
            }
        }

        static string FormatVp(int value) => $"{value:N0} VP";
    }
}
