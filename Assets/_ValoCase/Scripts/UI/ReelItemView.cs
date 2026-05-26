using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using ValoCase.Pooling;

namespace ValoCase.UI
{
    public sealed class ReelItemView : MonoBehaviour, IPoolable
    {
        [SerializeField] Image icon;
        [SerializeField] Image rarityFrame;
        [SerializeField] Image glow;
        [SerializeField] TextMeshProUGUI weaponLabel;
        [SerializeField] TextMeshProUGUI skinLabel;

        public RectTransform RectTransform { get; private set; }
        public SkinDefinitionSO Skin { get; private set; }

        void Awake() => RectTransform = transform as RectTransform;

        public void Bind(SkinDefinitionSO skin, RarityVisualSO visuals)
        {
            Skin = skin;
            if (skin == null) return;

            if (icon != null)
            {
                icon.sprite = skin.Icon;
                icon.enabled = skin.Icon != null;
                icon.preserveAspect = true;
            }

            if (weaponLabel != null) weaponLabel.text = skin.WeaponName;
            if (skinLabel != null) skinLabel.text = skin.SkinName;

            if (visuals != null && visuals.TryGet(skin.Rarity, out var entry))
            {
                // rarityFrame is the full-card background — use deep rarity color.
                if (rarityFrame != null)
                    rarityFrame.color = entry.cardBgColor;

                // Glow overlay: soft neon tint over the background.
                // Show for all rarities (subtle for lower tiers, vivid for high).
                if (glow != null)
                {
                    var alpha = skin.Rarity >= SkinRarity.Premium ? 0.28f : 0.14f;
                    glow.color = new Color(entry.primaryColor.r, entry.primaryColor.g, entry.primaryColor.b, alpha);
                    glow.gameObject.SetActive(true);
                }

                // Border glow matches rarity accent.
                var outline = GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = new Color(entry.primaryColor.r, entry.primaryColor.g, entry.primaryColor.b, 0.75f);
            }
        }

        public void OnSpawned() { }
        public void OnDespawned() => Skin = null;
    }
}
