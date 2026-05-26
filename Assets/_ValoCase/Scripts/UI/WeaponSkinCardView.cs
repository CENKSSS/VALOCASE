using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;

namespace ValoCase.UI
{
    public sealed class WeaponSkinCardView : MonoBehaviour
    {
        [SerializeField] Image skinIcon;
        [SerializeField] Image rarityStripe;
        [SerializeField] TextMeshProUGUI skinNameLabel;
        [SerializeField] TextMeshProUGUI weaponLabel;

        public void Bind(SkinDefinitionSO skin, RarityVisualSO visuals)
        {
            if (skin == null) return;

            if (skinIcon != null)
            {
                skinIcon.sprite = skin.Icon;
                skinIcon.enabled = skin.Icon != null;
                skinIcon.preserveAspect = true;
            }

            if (skinNameLabel != null) skinNameLabel.text = skin.SkinName;
            if (weaponLabel != null) weaponLabel.text = skin.WeaponName;

            if (visuals != null && visuals.TryGet(skin.Rarity, out var v))
            {
                // Full-card rarity background.
                var bg = GetComponent<Image>();
                if (bg != null) bg.color = v.cardBgColor;

                // Rarity symbol PNG — same logic as SkinCardView.
                if (rarityStripe != null)
                {
                    if (RaritySymbolLoader.TryGet(skin.Rarity, out var sym))
                    {
                        rarityStripe.sprite         = sym;
                        rarityStripe.color          = Color.white;
                        rarityStripe.preserveAspect  = true;
                        rarityStripe.type            = Image.Type.Simple;
                    }
                    else
                    {
                        // Loader already reported the failure once — run fallback silently.
                        rarityStripe.sprite = null;
                        rarityStripe.color  = v.primaryColor;
                    }
                }

                // Border glow matches rarity accent.
                var outline = GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = new Color(v.primaryColor.r, v.primaryColor.g, v.primaryColor.b, 0.75f);
            }
        }
    }
}
