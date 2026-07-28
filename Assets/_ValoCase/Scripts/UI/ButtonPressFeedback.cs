using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// Instant touch feedback for a Button. Every button in the project sets
    /// Selectable.Transition.None, so without this nothing on screen reacts until the
    /// action itself completes — which reads as lag even when the code is fast.
    ///
    /// Press scales down immediately (feedback should never wait), release eases back.
    /// Only pointer down/up/exit are handled: implementing a drag interface here would
    /// swallow the drag from a parent ScrollRect and break list scrolling. Exit is what
    /// releases the pressed look when a finger slides off the button to scroll.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class ButtonPressFeedback : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        const float PressedScale = 0.96f;
        const float ReleaseDuration = 0.08f;

        Button _button;
        Transform _target;
        bool _pressed;
        float _releaseTimer;
        float _releaseFrom;

        void Awake() => EnsureRefs();

        // Resolved lazily rather than only in Awake: the component is added at runtime by
        // UIBuild.WireButtonClick, and relying on Awake having run would make it silently
        // do nothing if a press arrives first.
        void EnsureRefs()
        {
            if (_button == null) _button = GetComponent<Button>();
            if (_target == null) _target = transform;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            EnsureRefs();
            // A locked button must not look pressable.
            if (_button == null || !_button.IsInteractable()) return;
            _pressed = true;
            _releaseTimer = 0f;
            _target.localScale = new Vector3(PressedScale, PressedScale, 1f);
        }

        public void OnPointerUp(PointerEventData eventData)   => BeginRelease();
        public void OnPointerExit(PointerEventData eventData) => BeginRelease();

        void BeginRelease()
        {
            if (!_pressed) return;
            EnsureRefs();
            _pressed = false;
            _releaseFrom = _target.localScale.x;
            _releaseTimer = ReleaseDuration;
        }

        void Update()
        {
            if (_releaseTimer <= 0f) return;

            _releaseTimer -= Time.unscaledDeltaTime;
            if (_releaseTimer <= 0f)
            {
                _target.localScale = Vector3.one;
                return;
            }

            float p = 1f - (_releaseTimer / ReleaseDuration);
            float s = Mathf.Lerp(_releaseFrom, 1f, p);
            _target.localScale = new Vector3(s, s, 1f);
        }

        // A screen can be hidden mid-press; never leave the button stuck small. Reset on
        // the way out AND on the way back in, because the navigator toggles screens
        // constantly and a missed callback would strand a button at the pressed size.
        void OnDisable() => ResetScale();
        void OnEnable()  => ResetScale();

        void ResetScale()
        {
            EnsureRefs();
            _pressed = false;
            _releaseTimer = 0f;
            if (_target != null) _target.localScale = Vector3.one;
        }
    }
}
