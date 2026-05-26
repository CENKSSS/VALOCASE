using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;

namespace ValoCase.UI
{
    public sealed class DropItemView : MonoBehaviour
    {
        [SerializeField] Image rarityStripe;
        [SerializeField] Image skinIcon;
        [SerializeField] TextMeshProUGUI weaponLabel;
        [SerializeField] TextMeshProUGUI skinNameLabel;
        [SerializeField] TextMeshProUGUI chanceLabel;
        [SerializeField] TextMeshProUGUI priceLabel;
        [SerializeField] Image background;

        public void Bind(SkinDefinitionSO skin, float dropChancePercent, RarityVisualSO visuals, float sellMultiplier)
        {
            if (skin == null) return;

            if (skinIcon != null)
            {
                skinIcon.sprite = skin.Icon;
                skinIcon.enabled = skin.Icon != null;
                skinIcon.preserveAspect = true;
            }

            if (weaponLabel != null) weaponLabel.text = skin.WeaponName;
            if (skinNameLabel != null) skinNameLabel.text = skin.SkinName;

            if (chanceLabel != null)
            {
                chanceLabel.text = dropChancePercent < 0.01f
                    ? $"{dropChancePercent:F3}%"
                    : dropChancePercent < 1f
                        ? $"{dropChancePercent:F2}%"
                        : $"{dropChancePercent:F1}%";
            }

            var sellPrice = Mathf.RoundToInt(skin.VpValue * sellMultiplier);
            if (priceLabel != null) priceLabel.text = $"{sellPrice:N0} VP";

            if (visuals != null && visuals.TryGet(skin.Rarity, out var v))
            {
                if (rarityStripe != null) rarityStripe.color = v.primaryColor;
                if (background != null)
                {
                    var c = v.primaryColor;
                    c.a = 0.08f;
                    background.color = c;
                }
            }
        }
    }
}
