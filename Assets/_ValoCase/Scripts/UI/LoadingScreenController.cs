using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    public sealed class LoadingScreenController : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Slider progressBar;
        [SerializeField] TextMeshProUGUI statusLabel;

        public void Show(float progress01)
        {
            if (root != null) root.SetActive(true);
            if (progressBar != null) progressBar.value = progress01;
            if (statusLabel != null) statusLabel.text = "Connecting to Store...";
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
