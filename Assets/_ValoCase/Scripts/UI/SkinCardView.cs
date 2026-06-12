using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Pooling;
using ValoCase.Save;

namespace ValoCase.UI
{
    public sealed class SkinCardView : MonoBehaviour, IPoolable
    {
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] Image rarityStripe;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI subtitle;
        [SerializeField] TextMeshProUGUI quantityBadge;
        [SerializeField] GameObject duplicateMarker;
        [SerializeField] TextMeshProUGUI vpValueLabel;

        public OwnedSkinSaveEntry Entry { get; private set; }
        public SkinDefinitionSO Skin { get; private set; }

        Action<SkinCardView> _onClick;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => _onClick?.Invoke(this));
        }

        public void Bind(OwnedSkinSaveEntry entry, SkinDefinitionSO skin, RarityVisualSO visuals,
            Action<SkinCardView> onClick)
        {
            Entry = entry;
            Skin = skin;
            _onClick = onClick;

            if (skin == null) return;

            if (icon != null)
            {
                icon.sprite = skin.Icon;
                icon.enabled = skin.Icon != null;
                icon.preserveAspect = true;
            }

            if (title != null) title.text = skin.SkinName;
            if (subtitle != null) subtitle.text = skin.WeaponName;
            if (quantityBadge != null) quantityBadge.text = entry.quantity > 1 ? $"x{entry.quantity}" : string.Empty;
            if (duplicateMarker != null) duplicateMarker.SetActive(entry.quantity > 1);

            if (vpValueLabel != null)
            {
                var ctx = GameContext.Instance;
                var sellPrice = ctx?.Config != null
                    ? Mathf.RoundToInt(skin.VpValue * ctx.Config.SellMultiplier)
                    : Mathf.RoundToInt(skin.VpValue * GameConstants.SellValueMultiplier);
                vpValueLabel.text = $"{sellPrice:N0} VP";
            }

            if (visuals != null && visuals.TryGet(skin.Rarity, out var v))
            {
                // Full-card background — Valorant-style deep rarity color.
                var bg = GetComponent<Image>();
                if (bg != null) bg.color = v.cardBgColor;

                // Rarity symbol PNG from Desktop/ValorantProject/Semboller/ (see ProjectPaths).
                // Falls back to a solid-color dot if the file is not found.
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

        public void OnSpawned() { }
        public void OnDespawned()
        {
            Entry = null;
            Skin = null;
            _onClick = null;
        }
    }
}
