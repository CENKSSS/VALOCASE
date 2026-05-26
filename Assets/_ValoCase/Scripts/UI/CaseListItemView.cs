using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;

namespace ValoCase.UI
{
    public sealed class CaseListItemView : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI price;
        [SerializeField] GameObject lockedOverlay;
        [SerializeField] GameObject selectedFrame;

        public CaseDefinitionSO Case { get; private set; }

        Action<CaseDefinitionSO> _onSelect;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => _onSelect?.Invoke(Case));
        }

        public void Bind(CaseDefinitionSO caseDef, bool unlocked, Action<CaseDefinitionSO> onSelect)
        {
            Case = caseDef;
            _onSelect = onSelect;
            if (title != null) title.text = caseDef.DisplayName;
            if (price != null) price.text = $"{caseDef.VpPrice:N0} VP";
            if (icon != null)
            {
                icon.sprite = caseDef.CaseIcon;
                // When a real sprite is present, show it untinted. Fall back to the
                // theme color only as a placeholder swatch when no icon is set.
                icon.color = caseDef.CaseIcon != null ? Color.white : caseDef.ThemeColor;
                icon.preserveAspect = true;
            }

            if (lockedOverlay != null) lockedOverlay.SetActive(!unlocked);
            if (button != null) button.interactable = unlocked;
        }

        public void SetSelected(bool selected)
        {
            if (selectedFrame != null) selectedFrame.SetActive(selected);
        }
    }
}
