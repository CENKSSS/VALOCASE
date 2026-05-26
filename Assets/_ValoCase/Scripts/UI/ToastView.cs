using System.Collections;
using TMPro;
using UnityEngine;
using ValoCase.Core;

namespace ValoCase.UI
{
    public sealed class ToastView : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] TextMeshProUGUI messageLabel;
        [SerializeField] float displaySeconds = 2f;

        Coroutine _routine;

        void OnEnable() => GameEvents.OnToastRequested += Show;
        void OnDisable() => GameEvents.OnToastRequested -= Show;

        void Show(string message)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Display(message));
        }

        IEnumerator Display(string message)
        {
            root.SetActive(true);
            messageLabel.text = message;
            yield return new WaitForSecondsRealtime(displaySeconds);
            root.SetActive(false);
        }
    }
}
