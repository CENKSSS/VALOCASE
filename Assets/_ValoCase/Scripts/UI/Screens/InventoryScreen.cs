using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Audio;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Pooling;

namespace ValoCase.UI.Screens
{
    public sealed class InventoryScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;
        [SerializeField] Transform gridRoot;
        [SerializeField] Button backButton;
        [SerializeField] TextMeshProUGUI inventoryValueLabel;
        [SerializeField] SkinDetailPopup detailPopup;
        [SerializeField] TextMeshProUGUI walletLabel;

        // Legacy filter controls — kept for layout reference only. They are hidden at
        // runtime; their header slots are reused for the new Sort / Sell buttons.
        [SerializeField] TMP_Dropdown filterDropdown;
        [SerializeField] TMP_Dropdown sortDropdown;
        [SerializeField] TMP_Dropdown weaponDropdown;

        readonly List<SkinCardView> _cards = new();

        SellFlow _sellFlow;
        Button _sortButton;
        TextMeshProUGUI _sortLabel;
        Button _sellButton;
        bool _headerBuilt;
        bool _priceDescending = true; // default: highest value first (Price ↓)
        bool _bulkSellInFlight;       // guards backend bulk sells against double-fire

        // Scroll-panel insets (px, 1080×1920 ref). Top is larger than the prefab's 240
        // so the grid sits below the Price / SELL row; bottom is lowered from 176 into
        // the freed Back-button band, stopping ~20px above the 90px BottomNavBar.
        const float GridTopPadding = 330f;
        const float GridBottomPadding = 110f;

        // Downward nudge (px, 1080×1920 ref) applied to the Price / SELL buttons relative
        // to the old dropdown slot. Stays above the grid (grid top inset is 330).
        const float HeaderButtonDrop = 25f;

        void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.MainMenu));
        }

        protected override void OnShown()
        {
            // Shared section background (cover image, aspect preserved)
            FullscreenBackground.AttachShared(gameObject);
            EnsureSellFlow();
            BuildHeaderControls();
            Refresh();
            GameEvents.OnInventoryChanged += Refresh;
            GameEvents.OnVpChanged += OnVpChanged;
            RefreshWallet();
        }

        protected override void OnHidden()
        {
            GameEvents.OnInventoryChanged -= Refresh;
            GameEvents.OnVpChanged -= OnVpChanged;
            _sellFlow?.CloseAll();
        }

        // ── Header construction ────────────────────────────────────────────────
        void EnsureSellFlow()
        {
            if (_sellFlow != null) return;
            var canvas = GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.transform : transform;
            _sellFlow = new SellFlow(root,
                onSellAll:   HandleSellAll,
                onSellBelow: HandleSellBelow);
        }

        void BuildHeaderControls()
        {
            if (_headerBuilt) return;

            HideLegacyFilters();
            RemoveBackButton();
            ResizeGrid();

            // Reuse the former filter slot for the sort toggle, the sort slot for SELL.
            _sortButton = MakeHeaderButton("SortButton", filterDropdown, SellUIFactory.Grey,
                SellUIFactory.Light, out _sortLabel);
            if (_sortButton != null) _sortButton.onClick.AddListener(ToggleSort);

            _sellButton = MakeHeaderButton("SellButton", sortDropdown, SellUIFactory.Red,
                Color.white, out var sellLabel);
            if (_sellButton != null)
            {
                sellLabel.text = "SELL";
                _sellButton.onClick.AddListener(() => _sellFlow?.OpenOptions());
            }

            UpdateSortLabel();
            _headerBuilt = true;
        }

        void HideLegacyFilters()
        {
            if (filterDropdown != null) filterDropdown.gameObject.SetActive(false);
            if (sortDropdown != null) sortDropdown.gameObject.SetActive(false);
            if (weaponDropdown != null) weaponDropdown.gameObject.SetActive(false);
        }

        // Completely removes the Back button GameObject (label, dark rectangle, and the
        // space it occupied). Navigation off the screen is handled by the BottomNavBar.
        void RemoveBackButton()
        {
            if (backButton == null) return;
            Destroy(backButton.gameObject);
        }

        // Resizes the scroll panel: top edge pushed down below the Price / SELL row, and
        // bottom edge extended into the freed Back-button band so the grid is taller and
        // reaches near the BottomNavBar with a small safe margin. Idempotent across shows.
        void ResizeGrid()
        {
            var scroll = gridRoot != null ? gridRoot.GetComponentInParent<ScrollRect>() : null;
            if (scroll == null || scroll.transform is not RectTransform rt) return;
            rt.offsetMax = new Vector2(rt.offsetMax.x, -GridTopPadding);
            rt.offsetMin = new Vector2(rt.offsetMin.x, GridBottomPadding);
        }

        // Builds a fresh button placed in the given dropdown's former layout slot.
        Button MakeHeaderButton(string name, TMP_Dropdown slotSource, Color bg, Color textColor,
            out TextMeshProUGUI label)
        {
            var parent = slotSource != null ? slotSource.transform.parent : transform;
            var btn = SellUIFactory.Button(parent, name, "", bg, textColor, 36, out label);
            var rt = (RectTransform)btn.transform;

            if (slotSource != null)
            {
                var src = slotSource.GetComponent<RectTransform>();
                rt.anchorMin        = src.anchorMin;
                rt.anchorMax        = src.anchorMax;
                rt.pivot            = src.pivot;
                rt.sizeDelta        = src.sizeDelta;
                // Nudge the Price / SELL row slightly down from the old dropdown slot,
                // keeping both buttons on the same line (same offset applied to each).
                rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, -HeaderButtonDrop);
                rt.SetSiblingIndex(src.GetSiblingIndex());
            }
            else
            {
                // Fallback: top-right corner if the prefab slot is missing.
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(280f, 90f);
                rt.anchoredPosition = new Vector2(-40f, -40f);
            }
            return btn;
        }

        void ToggleSort()
        {
            _priceDescending = !_priceDescending;
            UpdateSortLabel();
            Refresh();
        }

        void UpdateSortLabel()
        {
            if (_sortLabel != null)
                _sortLabel.text = _priceDescending ? "Price ↓" : "Price ↑";
        }

        // ── Selling ────────────────────────────────────────────────────────────
        // Both bulk actions route through these handlers, which pick the backend or
        // local path. Backend mode is server-authoritative (no local VP/inventory
        // mutation); local mode keeps the exact Phase-4 behavior.

        void HandleSellAll()
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;

            if (ctx.BackendEnabled) { SellAllBackend(); return; }
            SellMatching(null);
        }

        void HandleSellBelow(int threshold)
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;

            if (ctx.BackendEnabled) { SellBelowBackend(threshold); return; }
            SellMatching(s => s.VpValue <= threshold);
        }

        // ── Backend bulk sells (server-authoritative) ───────────────────────────
        void SellAllBackend()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || _bulkSellInFlight) return;

            _bulkSellInFlight = true;
            SetSellInteractable(false);
            ctx.SellAllBackend(OnBackendBulkSold, OnBackendBulkFailed);
        }

        void SellBelowBackend(int threshold)
        {
            var ctx = GameContext.Instance;
            if (ctx == null || _bulkSellInFlight) return;

            _bulkSellInFlight = true;
            SetSellInteractable(false);
            ctx.SellBelowValueBackend(threshold, OnBackendBulkSold, OnBackendBulkFailed);
        }

        void OnBackendBulkSold(int soldCount, int totalVpGained)
        {
            // Guard: runs from the persistent GameContext; may fire after this screen
            // was destroyed by navigation.
            if (this == null) return;
            _bulkSellInFlight = false;
            SetSellInteractable(true);
            if (soldCount > 0) SoundManager.Instance?.Play(SoundId.SellSkin);
            // Inventory + wallet already refreshed via GameContext's authoritative
            // sync (RaiseInventoryChanged → Refresh). Refresh again is harmless.
            Refresh();
        }

        void OnBackendBulkFailed(string msg)
        {
            if (this == null) return;
            _bulkSellInFlight = false;
            SetSellInteractable(true);
            if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
        }

        void SetSellInteractable(bool value)
        {
            if (_sellButton != null) _sellButton.interactable = value;
        }

        // ── Local bulk sell (unchanged Phase-4 path) ────────────────────────────
        // predicate == null  → sell every skin.
        void SellMatching(Func<SkinDefinitionSO, bool> predicate)
        {
            var ctx = GameContext.Instance;
            if (ctx?.Economy == null) return;

            // Suppress per-item grid rebuilds; we rebuild once at the end. The economy
            // facade performs the actual loop + record-stat + single save (Phase-4).
            GameEvents.OnInventoryChanged -= Refresh;
            ctx.Economy.SellMatching(predicate, out var totalGained);
            GameEvents.OnInventoryChanged += Refresh;

            if (totalGained > 0)
                SoundManager.Instance?.Play(SoundId.SellSkin);

            Refresh();
        }

        // ── Rendering ──────────────────────────────────────────────────────────
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
            if (ctx == null || ctx.Inventory == null || ctx.Content == null ||
                PoolManager.Instance == null || gridRoot == null)
                return;

            if (inventoryValueLabel != null)
                inventoryValueLabel.text = $"Collection Value: {ctx.Inventory.InventoryValue:N0} VP";

            RefreshWallet();

            foreach (var card in _cards)
                PoolManager.Instance.ReleaseSkinCard(card);
            _cards.Clear();

            // Sort explicitly here (right before rendering) so the rendered order
            // always reflects the toggle. OrderBy is stable, so equal VP values keep
            // a deterministic secondary order by skin name.
            var entries = ctx.Inventory.GetFilteredSorted(SkinFilterMode.All, InventorySortMode.ValueDesc);
            var items = (_priceDescending
                    ? entries.OrderByDescending(e => SkinValue(ctx, e.skinId))
                    : entries.OrderBy(e => SkinValue(ctx, e.skinId)))
                .ThenBy(e => SkinName(ctx, e.skinId), StringComparer.Ordinal)
                .ToList();

            foreach (var entry in items)
            {
                var skin = ctx.Content.GetSkin(entry.skinId);
                if (skin == null) continue;

                var card = PoolManager.Instance.GetSkinCard();
                card.transform.SetParent(gridRoot, false);
                // Pooled cards stay children of gridRoot when released, so SetParent
                // alone won't reorder them — force sibling order to match the sort.
                card.transform.SetAsLastSibling();
                card.Bind(entry, skin, ctx.RarityVisuals, OnCardClicked);
                _cards.Add(card);
            }
        }

        static int SkinValue(GameContext ctx, string skinId)
        {
            var skin = ctx.Content.GetSkin(skinId);
            return skin != null ? skin.VpValue : 0;
        }

        static string SkinName(GameContext ctx, string skinId)
        {
            var skin = ctx.Content.GetSkin(skinId);
            return skin != null ? skin.SkinName : string.Empty;
        }

        void OnCardClicked(SkinCardView card)
        {
            if (card?.Skin != null)
                detailPopup?.Show(card.Skin.SkinId);
        }
    }
}
