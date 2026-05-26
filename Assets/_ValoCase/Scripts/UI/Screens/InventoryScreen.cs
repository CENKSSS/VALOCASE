using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Pooling;
using ValoCase.Save;

namespace ValoCase.UI.Screens
{
    public sealed class InventoryScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;
        [SerializeField] Transform gridRoot;
        [SerializeField] Button backButton;
        [SerializeField] TMP_Dropdown filterDropdown;
        [SerializeField] TMP_Dropdown sortDropdown;
        [SerializeField] TextMeshProUGUI inventoryValueLabel;
        [SerializeField] SkinDetailPopup detailPopup;
        [SerializeField] TextMeshProUGUI walletLabel;

        // Weapon-type filter (cloned at runtime from filterDropdown).
        // Optional inspector field — if null, created automatically beside the rarity dropdown.
        [SerializeField] TMP_Dropdown weaponDropdown;

        readonly List<SkinCardView> _cards = new();
        bool _weaponDropdownReady;

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.MainMenu));
            if (filterDropdown != null) filterDropdown.onValueChanged.AddListener(_ => Refresh());
            if (sortDropdown != null) sortDropdown.onValueChanged.AddListener(_ => Refresh());
        }

        protected override void OnShown()
        {
            EnsureWeaponDropdown();
            Refresh();
            GameEvents.OnInventoryChanged += Refresh;
            GameEvents.OnVpChanged += OnVpChanged;
            RefreshWallet();
        }

        // Clones the existing rarity dropdown to create a weapon-type filter.
        // Runs once; no prefab modifications needed.
        void EnsureWeaponDropdown()
        {
            if (_weaponDropdownReady) return;

            var db = GameContext.Instance?.Content;
            if (db == null) return;

            // Build the dropdown at runtime if not wired in the prefab.
            if (weaponDropdown == null && filterDropdown != null)
            {
                weaponDropdown = Instantiate(filterDropdown, filterDropdown.transform.parent, false);
                weaponDropdown.name = "WeaponDropdown";
                weaponDropdown.onValueChanged.RemoveAllListeners();

                var src = filterDropdown.GetComponent<RectTransform>();
                var rt  = weaponDropdown.GetComponent<RectTransform>();
                if (src != null && rt != null)
                {
                    rt.anchorMin = src.anchorMin;
                    rt.anchorMax = src.anchorMax;
                    rt.pivot     = src.pivot;
                    rt.sizeDelta = src.sizeDelta;
                    // Place below the filter+sort row.
                    rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, -50f);
                }
            }

            if (weaponDropdown == null) return;

            // Populate with "Tüm Silahlar" + each unique weapon name.
            weaponDropdown.ClearOptions();
            var opts = new List<string> { "Tüm Silahlar" };
            opts.AddRange(db.GetUniqueWeaponNames());
            weaponDropdown.AddOptions(opts);
            weaponDropdown.value = 0;
            weaponDropdown.RefreshShownValue();
            weaponDropdown.onValueChanged.AddListener(_ => Refresh());

            _weaponDropdownReady = true;
        }

        protected override void OnHidden()
        {
            GameEvents.OnInventoryChanged -= Refresh;
            GameEvents.OnVpChanged -= OnVpChanged;
        }

        void OnVpChanged(int _, int current) => RefreshWallet();

        void RefreshWallet()
        {
            if (walletLabel == null) return;
            var ctx = GameContext.Instance;
            if (ctx?.Vp == null) return;
            walletLabel.text = $"Wallet: {ctx.Vp.Balance:N0} VP";
        }

        void Refresh()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Inventory == null || ctx.Content == null || PoolManager.Instance == null || gridRoot == null)
                return;

            if (inventoryValueLabel != null)
                inventoryValueLabel.text = $"Collection Value: {ctx.Inventory.InventoryValue:N0} VP";

            RefreshWallet();

            foreach (var card in _cards)
                PoolManager.Instance.ReleaseSkinCard(card);
            _cards.Clear();

            var filter = filterDropdown != null ? (SkinFilterMode)filterDropdown.value : SkinFilterMode.All;
            var sort = sortDropdown != null ? (InventorySortMode)sortDropdown.value : InventorySortMode.RarityDesc;
            var items = ctx.Inventory.GetFilteredSorted(filter, sort);

            // Apply weapon-type filter on top of rarity filter (index 0 = "All").
            if (weaponDropdown != null && weaponDropdown.value > 0)
            {
                var weaponName = weaponDropdown.options[weaponDropdown.value].text;
                items = items.Where(e =>
                {
                    var s = ctx.Content.GetSkin(e.skinId);
                    return s != null && string.Equals(s.WeaponName, weaponName, System.StringComparison.OrdinalIgnoreCase);
                }).ToList();
            }

            foreach (var entry in items)
            {
                var skin = ctx.Content.GetSkin(entry.skinId);
                if (skin == null) continue;

                var card = PoolManager.Instance.GetSkinCard();
                card.transform.SetParent(gridRoot, false);
                card.Bind(entry, skin, ctx.RarityVisuals, OnCardClicked);
                _cards.Add(card);
            }
        }

        void OnCardClicked(SkinCardView card)
        {
            if (card?.Skin != null)
                detailPopup?.Show(card.Skin.SkinId);
        }
    }
}
