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
        bool _sellInFlight;   // guards against double-sell while a backend call is pending

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
            // Reset the in-flight guard each time the popup opens so a previous
            // backend sale (which disabled the button) never leaves it stuck.
            _sellInFlight = false;
            if (sellButton != null) sellButton.interactable = true;
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
            if (ctx == null) return;

            // ── Backend mode: server is authoritative. No local VP add, no local
            //    inventory remove. Disable the button until the response lands. ──
            if (ctx.BackendEnabled)
            {
                if (_sellInFlight) return;
                _sellInFlight = true;
                if (sellButton != null) sellButton.interactable = false;

                ctx.SellOneBackend(_skinId,
                    onSold: _ =>
                    {
                        _sellInFlight = false;
                        SoundManager.Instance?.Play(SoundId.SellSkin);
                        Hide();   // Show() re-enables the button next time it opens
                    },
                    onFailed: msg =>
                    {
                        _sellInFlight = false;
                        if (sellButton != null) sellButton.interactable = true;
                        if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
                    });
                return;
            }

            // ── Local mode (unchanged): one economy call (TrySell + record + save). ──
            if (ctx.Economy != null && ctx.Economy.SellOne(_skinId, out _))
            {
                SoundManager.Instance?.Play(SoundId.SellSkin);
                Hide();
            }
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
