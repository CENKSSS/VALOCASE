using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Audio;
using ValoCase.Core;
using ValoCase.Data;

namespace ValoCase.UI
{
    public sealed class SkinDetailPopup : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI skinName;
        [SerializeField] TextMeshProUGUI weaponName;
        [SerializeField] TextMeshProUGUI rarityLabel;
        [SerializeField] TextMeshProUGUI description;
        [SerializeField] TextMeshProUGUI collection;
        [SerializeField] TextMeshProUGUI vpValue;
        [SerializeField] TextMeshProUGUI quantity;
        [SerializeField] Button sellButton;
        [SerializeField] Button closeButton;

        string _skinId;

        void Awake()
        {
            if (sellButton != null) sellButton.onClick.AddListener(Sell);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            Hide();
        }

        public void Show(string skinId)
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Content == null || ctx.Inventory == null) return;
            var skin = ctx.Content.GetSkin(skinId);
            if (skin == null) return;

            _skinId = skinId;
            if (root != null) root.SetActive(true);

            if (icon != null)
            {
                icon.sprite = skin.Icon;
                icon.enabled = skin.Icon != null;
                // Keep the weapon's original proportions (no vertical squash) and show it
                // at full brightness/opacity — the prefab icon ships at alpha 0.12.
                icon.preserveAspect = true;
                icon.color = Color.white;
            }

            if (skinName != null) skinName.text = skin.SkinName;
            if (weaponName != null) weaponName.text = skin.WeaponName;
            if (rarityLabel != null) rarityLabel.text = skin.Rarity.ToString().ToUpperInvariant();
            if (description != null) description.text = skin.Description;
            if (collection != null) collection.text = skin.CollectionName;
            if (vpValue != null) vpValue.text = $"{skin.VpValue:N0} VP";
            if (quantity != null) quantity.text = $"Owned: {ctx.Inventory.GetQuantity(skinId)}";

            if (rarityLabel != null && ctx.RarityVisuals != null && ctx.RarityVisuals.TryGet(skin.Rarity, out var visual))
                rarityLabel.color = visual.textColor;
        }

        void Sell()
        {
            var ctx = GameContext.Instance;
            if (ctx?.Inventory != null && ctx.Inventory.TrySell(_skinId, out var gained))
            {
                SoundManager.Instance?.Play(SoundId.SellSkin);
                ctx.Statistics?.RecordVpEarned(gained);
                ctx.Statistics?.RecalculateInventoryStats(ctx.Inventory, ctx.Content);
                ctx.Save?.Save();
                Hide();
            }
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
