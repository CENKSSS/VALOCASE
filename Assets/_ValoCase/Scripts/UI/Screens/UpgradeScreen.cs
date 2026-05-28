using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Audio;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services;

namespace ValoCase.UI.Screens
{
    public sealed class UpgradeScreen : UIScreenBase
    {
        // ── Inspector refs ────────────────────────────────────────────────────
        [SerializeField] UINavigator         navigator;
        [SerializeField] Button              backButton;
        [SerializeField] TextMeshProUGUI     walletLabel;
        [SerializeField] WeaponSkinCardView  cardPrefab;

        // Left panel
        [SerializeField] Image           inputIcon;
        [SerializeField] TextMeshProUGUI inputName;
        [SerializeField] TextMeshProUGUI inputRarityLabel;
        [SerializeField] TextMeshProUGUI inputVpLabel;
        [SerializeField] TextMeshProUGUI inputChanceLabel;
        [SerializeField] Image           inputRarityStrip;
        [SerializeField] GameObject      inputPlaceholder;

        // Right panel
        [SerializeField] Image           targetIcon;
        [SerializeField] TextMeshProUGUI targetName;
        [SerializeField] TextMeshProUGUI targetRarityLabel;
        [SerializeField] TextMeshProUGUI targetVpLabel;
        [SerializeField] TextMeshProUGUI targetChanceLabel;
        [SerializeField] Image           targetRarityStrip;
        [SerializeField] GameObject      targetPlaceholder;

        // Center panel
        [SerializeField] RectTransform   spinCenter;
        [SerializeField] TextMeshProUGUI chanceLabel;
        [SerializeField] TextMeshProUGUI chanceHint;
        [SerializeField] Button          upgradeButton;
        [SerializeField] TextMeshProUGUI upgradeButtonLabel;

        // VP hızlı filtre butonları (center panelin altı)
        [SerializeField] Button          vpBtn1000;
        [SerializeField] Button          vpBtn2000;
        [SerializeField] Button          vpBtn3000;

        // Bottom section
        [SerializeField] Button          inventoryTabBtn;
        [SerializeField] Button          allSkinsTabBtn;
        [SerializeField] Image           inventoryTabLine;
        [SerializeField] Image           allSkinsTabLine;
        [SerializeField] Transform       skinGridRoot;
        [SerializeField] RectTransform   skinScrollRt;
        [SerializeField] RectTransform   filterBar;

        // Result overlay
        [SerializeField] Image           resultFlash;
        [SerializeField] TextMeshProUGUI resultLabel;

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color NeonGreen   = new Color(0.404f, 0.859f, 0.686f, 1f);
        static readonly Color NeonRed     = new Color(1f,    0.275f, 0.333f, 1f);
        static readonly Color NeonPink    = new Color(1f,    0.275f, 0.333f, 1f);
        static readonly Color NeonCyan    = new Color(0.545f, 0.592f, 0.561f, 1f);
        static readonly Color NeonPurple  = new Color(0.122f, 0.161f, 0.204f, 1f);
        static readonly Color Panel       = new Color(0.122f, 0.161f, 0.204f, 0.95f);
        static readonly Color BtnOff      = new Color(0.15f,  0.18f,  0.22f,  0.85f);
        static readonly Color PopupBg     = new Color(0.059f, 0.098f, 0.137f, 0.97f);
        static readonly Color TabActive   = new Color(0.925f, 0.910f, 0.882f, 1f);
        static readonly Color TabInactive = new Color(0.545f, 0.592f, 0.561f, 1f);
        static readonly Color VpBtnActive = new Color(1f,    0.275f, 0.333f, 1f);
        static readonly Color VpBtnOff    = new Color(0.15f, 0.18f,  0.22f,  1f);

        static readonly Dictionary<SkinRarity, Color> RarityAccents = new Dictionary<SkinRarity, Color>
        {
            { SkinRarity.Select,    new Color(0.55f, 0.65f, 0.78f, 1f) },
            { SkinRarity.Deluxe,    new Color(0f,    0.96f, 1f,    1f) },
            { SkinRarity.Premium,   new Color(0.69f, 0.15f, 1f,    1f) },
            { SkinRarity.Exclusive, new Color(1f,    0.18f, 0.67f, 1f) },
            { SkinRarity.Ultra,     new Color(0.22f, 1f,    0.08f, 1f) },
        };

        // ── State ─────────────────────────────────────────────────────────────
        bool        _showingInventory = true;
        string      _weaponFilter;
        SkinRarity? _rarityFilter;
        int?        _vpDeltaFilter;      // null=tümü | 1000/2000/3000 = VP farkı filtresi
        bool        _filtersBuilt;

        TextMeshProUGUI _weaponDdLabel;
        RectTransform   _weaponDdPopup, _weaponDdContent;
        bool            _weaponDdOpen;
        Coroutine       _weaponDdAnim;

        TextMeshProUGUI _rarityDdLabel;
        RectTransform   _rarityDdPopup, _rarityDdContent;
        bool            _rarityDdOpen;
        Coroutine       _rarityDdAnim;

        UpgradeSpinAnimator _spinAnimator;
        ScrollRect          _skinScrollRect;

        SkinDefinitionSO _selectedInput;
        SkinDefinitionSO _selectedTarget;
        bool             _isUpgrading;
        Coroutine        _pulseCo;

        readonly List<UpgradeCard> _cardPool = new List<UpgradeCard>();

        // ── Awake ─────────────────────────────────────────────────────────────
        void Awake()
        {
            _spinAnimator = gameObject.AddComponent<UpgradeSpinAnimator>();

            backButton?.onClick.AddListener(() => { if (!_isUpgrading) navigator?.Navigate(ScreenType.MainMenu); });
            upgradeButton?.onClick.AddListener(OnUpgradeClicked);
            inventoryTabBtn?.onClick.AddListener(() => SetTab(true));
            allSkinsTabBtn?.onClick.AddListener(() => SetTab(false));

            // VP hızlı filtreler
            vpBtn1000?.onClick.AddListener(() => SetVpFilter(1000));
            vpBtn2000?.onClick.AddListener(() => SetVpFilter(2000));
            vpBtn3000?.onClick.AddListener(() => SetVpFilter(3000));
        }

        protected override void OnShown()
        {
            GameEvents.OnInventoryChanged += HandleInventoryChanged;
            GameEvents.OnVpChanged        += HandleVpChanged;
            RefreshWallet();

            _skinScrollRect = skinScrollRt != null ? skinScrollRt.GetComponent<ScrollRect>() : null;

            _selectedInput  = null;
            _selectedTarget = null;
            _showingInventory = true;
            _vpDeltaFilter    = null;

            UpdateInputPanel(null);
            UpdateTargetPanel(null);
            UpdateTabVisuals();
            UpdateVpFilterVisuals();
            EnsureFiltersBuilt();
            RebuildGrid();
            RefreshChance();

            if (resultFlash != null) resultFlash.color = new Color(0, 0, 0, 0);
            if (resultLabel != null) resultLabel.text  = string.Empty;

            StartCoroutine(InitSpinAnimator());
            if (_pulseCo != null) StopCoroutine(_pulseCo);
            _pulseCo = StartCoroutine(PulseButton());
        }

        protected override void OnHidden()
        {
            GameEvents.OnInventoryChanged -= HandleInventoryChanged;
            GameEvents.OnVpChanged        -= HandleVpChanged;

            if (_weaponDdOpen) CloseDropdown(ref _weaponDdOpen, _weaponDdPopup, ref _weaponDdAnim);
            if (_rarityDdOpen) CloseDropdown(ref _rarityDdOpen, _rarityDdPopup, ref _rarityDdAnim);
            if (_pulseCo != null) { StopCoroutine(_pulseCo); _pulseCo = null; }
            _spinAnimator?.ResetNeedle();
        }

        void HandleInventoryChanged()
        {
            if (_selectedInput != null)
            {
                var ctx = GameContext.Instance;
                if (ctx?.Inventory == null || !ctx.Inventory.Owns(_selectedInput.SkinId))
                {
                    _selectedInput = _selectedTarget = null;
                    UpdateInputPanel(null);
                    UpdateTargetPanel(null);
                }
            }
            RebuildGrid();
            RefreshChance();
        }

        void HandleVpChanged(int _, int __) => RefreshWallet();

        void RefreshWallet()
        {
            if (walletLabel == null) return;
            var ctx = GameContext.Instance;
            walletLabel.text = ctx?.Vp != null ? $"{ctx.Vp.Balance:N0} VP" : "0 VP";
        }

        // ─────────────────────────────────────────────────────────────────────
        // TAB SİSTEMİ
        // ─────────────────────────────────────────────────────────────────────

        void SetTab(bool inventory)
        {
            _showingInventory = inventory;
            UpdateTabVisuals();
            RebuildGrid();
            // Tab değişince scroll'u en üste al
            if (_skinScrollRect != null)
                _skinScrollRect.normalizedPosition = new Vector2(0, 1f);
        }

        void UpdateTabVisuals()
        {
            // Tab label renkleri
            SetTabLabelColor(inventoryTabBtn,  _showingInventory);
            SetTabLabelColor(allSkinsTabBtn,   !_showingInventory);

            // Underline
            if (inventoryTabLine != null)
                inventoryTabLine.color = _showingInventory
                    ? new Color(TabActive.r, TabActive.g, TabActive.b, 1f)
                    : new Color(TabInactive.r, TabInactive.g, TabInactive.b, 0.25f);
            if (allSkinsTabLine != null)
                allSkinsTabLine.color = !_showingInventory
                    ? new Color(TabActive.r, TabActive.g, TabActive.b, 1f)
                    : new Color(TabInactive.r, TabInactive.g, TabInactive.b, 0.25f);

            // Filtre bar her iki sekmede de görünür
            if (filterBar != null) filterBar.gameObject.SetActive(true);

            // Scroll view offset: tab(56) + filter(48) = 104 her zaman
            if (skinScrollRt != null)
            {
                var oMax = skinScrollRt.offsetMax;
                oMax.y = -104f;
                skinScrollRt.offsetMax = oMax;
            }
        }

        static void SetTabLabelColor(Button btn, bool active)
        {
            if (btn == null) return;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = active ? TabActive : TabInactive;
        }

        // ─────────────────────────────────────────────────────────────────────
        // VP DELTA FİLTRE
        // ─────────────────────────────────────────────────────────────────────

        void SetVpFilter(int delta)
        {
            // Aynı butona tekrar basınca filtre kapanır
            _vpDeltaFilter = _vpDeltaFilter == delta ? (int?)null : delta;

            // Otomatik olarak TÜM SKİNLER sekmesine geç
            if (_vpDeltaFilter.HasValue && _showingInventory)
            {
                _showingInventory = false;
                UpdateTabVisuals();
            }

            UpdateVpFilterVisuals();
            RebuildGrid();
        }

        void UpdateVpFilterVisuals()
        {
            SetVpBtnColor(vpBtn1000, _vpDeltaFilter == 1000);
            SetVpBtnColor(vpBtn2000, _vpDeltaFilter == 2000);
            SetVpBtnColor(vpBtn3000, _vpDeltaFilter == 3000);
        }

        static void SetVpBtnColor(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? VpBtnActive : VpBtnOff;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = active ? Color.white : new Color(0.7f, 0.75f, 0.85f);
        }

        // ─────────────────────────────────────────────────────────────────────
        // PANEL GÜNCELLEME
        // ─────────────────────────────────────────────────────────────────────

        void UpdateInputPanel(SkinDefinitionSO skin)
        {
            bool has = skin != null;
            if (inputPlaceholder != null) inputPlaceholder.SetActive(!has);
            if (inputIcon  != null) { inputIcon.gameObject.SetActive(has);  if (has && skin.Icon != null) inputIcon.sprite = skin.Icon; }
            if (inputName  != null) { inputName.gameObject.SetActive(has);  if (has) inputName.text = skin.SkinName; }
            if (inputRarityLabel != null)
            {
                inputRarityLabel.gameObject.SetActive(has);
                if (has)
                {
                    inputRarityLabel.text  = RaritySystem.Labels.TryGetValue(skin.Rarity, out var n) ? n : skin.Rarity.ToString();
                    inputRarityLabel.color = RarityAccents.TryGetValue(skin.Rarity, out var c) ? c : Color.white;
                }
            }
            if (inputVpLabel != null) { inputVpLabel.gameObject.SetActive(has); if (has) inputVpLabel.text = $"{skin.VpValue:N0} VP"; }
            if (inputRarityStrip != null && has)
                inputRarityStrip.color = RarityAccents.TryGetValue(skin.Rarity, out var sc) ? sc : Color.white;
        }

        void UpdateTargetPanel(SkinDefinitionSO skin)
        {
            bool has = skin != null;
            if (targetPlaceholder != null) targetPlaceholder.SetActive(!has);
            if (targetIcon  != null) { targetIcon.gameObject.SetActive(has);  if (has && skin.Icon != null) targetIcon.sprite = skin.Icon; }
            if (targetName  != null) { targetName.gameObject.SetActive(has);  if (has) targetName.text = skin.SkinName; }
            if (targetRarityLabel != null)
            {
                targetRarityLabel.gameObject.SetActive(has);
                if (has)
                {
                    targetRarityLabel.text  = RaritySystem.Labels.TryGetValue(skin.Rarity, out var n) ? n : skin.Rarity.ToString();
                    targetRarityLabel.color = RarityAccents.TryGetValue(skin.Rarity, out var c) ? c : Color.white;
                }
            }
            if (targetVpLabel != null) { targetVpLabel.gameObject.SetActive(has); if (has) targetVpLabel.text = $"{skin.VpValue:N0} VP"; }
            if (targetRarityStrip != null && has)
                targetRarityStrip.color = RarityAccents.TryGetValue(skin.Rarity, out var sc) ? sc : Color.white;
        }

        // ─────────────────────────────────────────────────────────────────────
        // GRİD
        // ─────────────────────────────────────────────────────────────────────

        void RebuildGrid()
        {
            if (skinGridRoot == null || cardPrefab == null) return;
            var ctx = GameContext.Instance;
            var skins = new List<SkinDefinitionSO>();

            if (_showingInventory)
            {
                // ENVANTER — sahip olunan skinler, silah filtresi uygulanır
                if (ctx?.Inventory != null && ctx.Content != null)
                {
                    foreach (var entry in ctx.Inventory.Items)
                    {
                        var s = ctx.Content.GetSkin(entry?.skinId);
                        if (s == null) continue;
                        if (!string.IsNullOrEmpty(_weaponFilter) &&
                            !string.Equals(s.WeaponName, _weaponFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        if (_rarityFilter.HasValue && s.Rarity != _rarityFilter.Value) continue;
                        skins.Add(s);
                    }
                }
                // Envanter: nadirlik yüksekten düşüğe (5→1), aynı nadirlikte VP yüksekten düşüğe
                skins.Sort((a, b) =>
                {
                    int rd = RaritySystem.GetRank(b.Rarity).CompareTo(RaritySystem.GetRank(a.Rarity));
                    return rd != 0 ? rd : b.VpValue.CompareTo(a.VpValue);
                });
            }
            else
            {
                // TÜM SKİNLER — filtreler uygulanır
                if (ctx?.Content != null)
                {
                    int inputVp = _selectedInput?.VpValue ?? 0;

                    // ctx.Content.Skins boş olabilirse GetFilteredSkins fallback'i kullan
                    IReadOnlyList<SkinDefinitionSO> allSkins = ctx.Content.Skins;
                    if (allSkins == null || allSkins.Count == 0)
                        allSkins = ctx.Content.GetFilteredSkins(null, null);

                    foreach (var s in allSkins)
                    {
                        if (s == null) continue;
                        if (_selectedInput != null && s.SkinId == _selectedInput.SkinId) continue;
                        if (!string.IsNullOrEmpty(_weaponFilter) &&
                            !string.Equals(s.WeaponName, _weaponFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        if (_rarityFilter.HasValue && s.Rarity != _rarityFilter.Value) continue;

                        // VP delta filtresi: seçilen skinin VP'sine eklenen aralıkta skinler
                        if (_vpDeltaFilter.HasValue && _selectedInput != null)
                        {
                            int lo = inputVp + _vpDeltaFilter.Value - 500;
                            int hi = inputVp + _vpDeltaFilter.Value + 500;
                            if (s.VpValue < lo || s.VpValue > hi) continue;
                        }

                        skins.Add(s);
                    }
                }

                // Sıralama: nadirlik yüksekten düşüğe (5→1), uygun hedefler en önde
                if (_selectedInput != null)
                {
                    int inputRank = RaritySystem.GetRank(_selectedInput.Rarity);
                    skins.Sort((a, b) =>
                    {
                        bool ae = RaritySystem.GetRank(a.Rarity) >= inputRank;
                        bool be = RaritySystem.GetRank(b.Rarity) >= inputRank;
                        if (ae != be) return be.CompareTo(ae);
                        // Her grupta nadirlik yüksekten düşüğe
                        return RaritySystem.GetRank(b.Rarity).CompareTo(RaritySystem.GetRank(a.Rarity));
                    });
                }
                else
                {
                    skins.Sort((a, b) =>
                        RaritySystem.GetRank(b.Rarity).CompareTo(RaritySystem.GetRank(a.Rarity)));
                }
            }

            foreach (var c in _cardPool)
                if (c.Root != null) c.Root.SetActive(false);

            for (int i = 0; i < skins.Count; i++)
            {
                UpgradeCard card;
                if (i < _cardPool.Count) card = _cardPool[i];
                else { card = CreateCard(skinGridRoot); _cardPool.Add(card); }
                BindCard(card, skins[i]);
            }

            // ContentSizeFitter aynı frame'de güncellemez — layout'u zorla tetikle
            if (skinGridRoot != null)
            {
                var gridRt = skinGridRoot.GetComponent<RectTransform>();
                if (gridRt != null)
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(gridRt);
            }
        }

        UpgradeCard CreateCard(Transform parent)
        {
            var view = Instantiate(cardPrefab, parent, false);
            var go   = view.gameObject;
            var btn  = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            var img  = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var card = new UpgradeCard { Root = go, View = view, Button = btn, BaseScale = 1f };
            btn.onClick.AddListener(() => OnCardClicked(card));

            // Scroll drag geçiş
            var pass = go.AddComponent<ScrollRectPassthrough>();
            pass.Target = _skinScrollRect;

            // Hover scale
            var et = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            AddHoverTrigger(et, EventTriggerType.PointerEnter, () => ScaleCard(card, 1.07f, 0.10f));
            AddHoverTrigger(et, EventTriggerType.PointerExit,  () => ScaleCard(card, card.BaseScale, 0.10f));

            return card;
        }

        void BindCard(UpgradeCard card, SkinDefinitionSO skin)
        {
            card.Skin = skin;
            card.Root.SetActive(true);
            card.View.Bind(skin, GameContext.Instance?.RarityVisuals);

            // Scroll passthrough hedefini güncelle (pool'daki eski kartlar için)
            var pass = card.Root.GetComponent<ScrollRectPassthrough>();
            if (pass != null) pass.Target = _skinScrollRect;

            bool isSelected = _showingInventory
                ? (_selectedInput  != null && _selectedInput.SkinId  == skin.SkinId)
                : (_selectedTarget != null && _selectedTarget.SkinId == skin.SkinId);

            bool eligible = _showingInventory || _selectedInput == null ||
                            RaritySystem.IsEligibleTarget(_selectedInput.Rarity, skin.Rarity);

            if (!_showingInventory)
            {
                card.Button.interactable = eligible;
                float alpha = eligible ? 1f : 0.38f;
                foreach (var i in card.Root.GetComponentsInChildren<Image>(true))
                    i.color = new Color(i.color.r, i.color.g, i.color.b, alpha);
                foreach (var t in card.Root.GetComponentsInChildren<TextMeshProUGUI>(true))
                    t.color = new Color(t.color.r, t.color.g, t.color.b, alpha);
            }
            else
            {
                card.Button.interactable = true;
            }

            float targetScale = isSelected ? 1.04f : 1f;
            card.BaseScale = targetScale;
            card.Root.transform.localScale = new Vector3(targetScale, targetScale, 1f);
        }

        void OnCardClicked(UpgradeCard card)
        {
            if (_isUpgrading || card?.Skin == null) return;
            SoundManager.Instance?.Play(SoundId.UiClick);

            if (_weaponDdOpen) CloseDropdown(ref _weaponDdOpen, _weaponDdPopup, ref _weaponDdAnim);
            if (_rarityDdOpen) CloseDropdown(ref _rarityDdOpen, _rarityDdPopup, ref _rarityDdAnim);

            if (_showingInventory)
            {
                _selectedInput  = card.Skin;
                _selectedTarget = null;
                _weaponFilter   = null;
                if (_weaponDdLabel != null) _weaponDdLabel.text = "SİLAH  ▼";
                UpdateInputPanel(_selectedInput);
                UpdateTargetPanel(null);
            }
            else
            {
                if (_selectedInput == null)
                {
                    GameEvents.RaiseToast("Önce ENVANTER sekmesinden bir skin seç");
                    return;
                }
                _selectedTarget = card.Skin;
                UpdateTargetPanel(_selectedTarget);
            }

            RebuildGrid();
            RefreshChance();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ŞANS + BUTON
        // ─────────────────────────────────────────────────────────────────────

        void RefreshChance()
        {
            var upgrade = GameContext.Instance?.Upgrade;
            bool targetEligible = _selectedInput  != null &&
                                  _selectedTarget != null &&
                                  RaritySystem.IsEligibleTarget(_selectedInput.Rarity, _selectedTarget.Rarity);

            float chance = 0f;
            if (upgrade != null && targetEligible)
                chance = upgrade.ComputeChance(_selectedInput, _selectedTarget);

            if (chanceLabel != null)
            {
                chanceLabel.text = _selectedInput == null || _selectedTarget == null
                    ? "--%"
                    : !targetEligible ? "0%" : $"{Mathf.RoundToInt(chance * 100f)}%";
                chanceLabel.color = Color.Lerp(NeonRed, NeonGreen, chance);
            }

            if (inputChanceLabel != null)
            {
                inputChanceLabel.gameObject.SetActive(targetEligible);
                if (targetEligible)
                {
                    inputChanceLabel.text  = $"BAŞARI %{Mathf.RoundToInt(chance * 100f)}";
                    inputChanceLabel.color = Color.Lerp(NeonRed, NeonGreen, chance);
                }
            }
            if (targetChanceLabel != null)
            {
                targetChanceLabel.gameObject.SetActive(targetEligible);
                if (targetEligible)
                {
                    targetChanceLabel.text  = $"KAYIP %{Mathf.RoundToInt((1f - chance) * 100f)}";
                    targetChanceLabel.color = Color.Lerp(NeonGreen, NeonRed, chance);
                }
            }

            if (chanceHint != null)
            {
                if (_selectedInput == null)       chanceHint.text = "ENVANTERden skin seç";
                else if (_selectedTarget == null) chanceHint.text = "Hedef skin seç";
                else if (!targetEligible)         chanceHint.text = "!! Nadirlik yetersiz";
                else chanceHint.text = $"{_selectedInput.VpValue:N0} VP → {_selectedTarget.VpValue:N0} VP";
            }

            _spinAnimator?.SetChance(chance);

            bool canUpgrade = !_isUpgrading && targetEligible;
            if (upgradeButton != null)
            {
                upgradeButton.interactable = canUpgrade;
                var img = upgradeButton.GetComponent<Image>();
                if (img != null) img.color = canUpgrade ? NeonPink : BtnOff;
            }
            if (upgradeButtonLabel != null)
                upgradeButtonLabel.text = _isUpgrading ? "..." : "YÜKSELT";
        }

        // ─────────────────────────────────────────────────────────────────────
        // YÜKSELTME AKIŞI
        // ─────────────────────────────────────────────────────────────────────

        void OnUpgradeClicked()
        {
            if (_isUpgrading || _selectedInput == null || _selectedTarget == null) return;
            var ctx = GameContext.Instance;
            if (ctx?.Upgrade == null) return;
            _isUpgrading = true;
            SoundManager.Instance?.Play(SoundId.UiClick);
            RefreshChance();
            StartCoroutine(UpgradeSequence(ctx, _selectedInput, _selectedTarget));
        }

        IEnumerator UpgradeSequence(GameContext ctx, SkinDefinitionSO input, SkinDefinitionSO target)
        {
            float chance = ctx.Upgrade.ComputeChance(input, target);
            _spinAnimator?.SetChance(chance);

            if (!ctx.Upgrade.TryUpgrade(input, target, out var success))
            {
                // TryUpgrade başarısız — paneller olduğu gibi kalsın, sadece kilidi aç
                _isUpgrading = false;
                RefreshChance();
                GameEvents.RaiseToast("Yükseltme gerçekleştirilemedi");
                yield break;
            }

            // ── Animasyon sırasında paneller GÖRÜNÜR kalır ────────────────────
            yield return StartCoroutine(_spinAnimator.AnimateSpin(chance, success, null));
            SoundManager.Instance?.Play(success ? SoundId.CaseReveal : SoundId.UiBack);
            yield return StartCoroutine(PlayResultFlash(success));

            // ── Animasyon bitti, sonuca göre panelleri güncelle ───────────────
            if (success)
            {
                _selectedInput  = target;
                _selectedTarget = null;
                _weaponFilter   = null;
                _vpDeltaFilter  = null;
                if (_weaponDdLabel != null) _weaponDdLabel.text = "SİLAH  ▼";
                UpdateInputPanel(target);
                UpdateTargetPanel(null);
                UpdateVpFilterVisuals();
            }
            else
            {
                _selectedInput = _selectedTarget = null;
                UpdateInputPanel(null);
                UpdateTargetPanel(null);
            }

            _isUpgrading = false;
            _spinAnimator?.ResetNeedle();
            RebuildGrid();
            RefreshChance();

            if (success)
            {
                Debug.Log("[DEBUG][UPGRADE] Success reached");
                var popup = ValoCase.UI.SkinWinPopup.EnsureExists();
                Debug.Log("[DEBUG][UPGRADE] EnsureExists() returned: " + popup);
                if (popup != null)
                    popup.Show(target, () => Debug.Log("[DEBUG][UPGRADE] Confirm clicked"));
                else
                    GameEvents.RaiseToast("Tebrikler! " + target.SkinName + " kazanildi");
            }
            else
            {
                GameEvents.RaiseToast($"BASARISIZ — {input.SkinName} kaybedildi");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // FİLTRE DROPDOWN'LARI
        // ─────────────────────────────────────────────────────────────────────

        void EnsureFiltersBuilt()
        {
            if (_filtersBuilt || filterBar == null) return;
            _filtersBuilt = true;

            var ctx      = GameContext.Instance;
            var screenRt = transform as RectTransform;

            // Silah dropdown
            BuildDropdownHeader(filterBar, "SİLAH  ▼", 0f, 0.5f, ToggleWeaponDropdown, out _weaponDdLabel);
            _weaponDdPopup = BuildDropdownPopupUpward(screenRt, 0f, 0.5f, out _weaponDdContent);

            AddDdItem(_weaponDdContent, "Tüm Silahlar", NeonCyan, () => SetWeapon(null, "SİLAH  ▼"));

            if (ctx?.Content != null)
            {
                var weapons = new List<string>();
                var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in ctx.Content.Skins)
                {
                    if (s == null || string.IsNullOrEmpty(s.WeaponName)) continue;
                    if (seen.Add(s.WeaponName)) weapons.Add(s.WeaponName);
                }
                weapons.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var w in weapons)
                {
                    var cap = w;
                    AddDdItem(_weaponDdContent, cap, Color.white, () => SetWeapon(cap, cap.ToUpperInvariant() + "  ▼"));
                }
            }

            _weaponDdPopup.localScale = new Vector3(1f, 0f, 1f);
            _weaponDdPopup.gameObject.SetActive(false);
            _weaponDdPopup.SetAsLastSibling();

            // Nadirlik dropdown
            BuildDropdownHeader(filterBar, "NADİRLİK  ▼", 0.5f, 1f, ToggleRarityDropdown, out _rarityDdLabel);
            _rarityDdPopup = BuildDropdownPopupUpward(screenRt, 0.5f, 1f, out _rarityDdContent);

            AddDdItem(_rarityDdContent, "Tüm Nadirlikler", new Color(0.69f, 0.15f, 1f),
                () => SetRarity(null, "NADİRLİK  ▼"));

            foreach (var r in RaritySystem.OrderedRarities)
            {
                var cap   = r;
                var name  = RaritySystem.Labels.TryGetValue(r, out var n) ? n : r.ToString();
                var color = RarityAccents.TryGetValue(r, out var c) ? c : Color.white;
                AddDdItem(_rarityDdContent, name, color, () => SetRarity(cap, name.ToUpperInvariant() + "  ▼"));
            }

            _rarityDdPopup.localScale = new Vector3(1f, 0f, 1f);
            _rarityDdPopup.gameObject.SetActive(false);
            _rarityDdPopup.SetAsLastSibling();
        }

        static void BuildDropdownHeader(RectTransform parent, string placeholder,
                                         float xMin, float xMax,
                                         UnityEngine.Events.UnityAction onClick,
                                         out TextMeshProUGUI labelOut)
        {
            var go = new GameObject("DdHeader", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0); rt.anchorMax = new Vector2(xMax, 1);
            rt.offsetMin = new Vector2(4, 4); rt.offsetMax = new Vector2(-4, -4);
            go.GetComponent<Image>().color = Panel;
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(go.transform, false);
            var lrt = lgo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10, 0); lrt.offsetMax = new Vector2(-10, 0);
            var tmp = lgo.AddComponent<TextMeshProUGUI>();
            tmp.text = placeholder; tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontSize = 14; tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white; tmp.raycastTarget = false; tmp.enableWordWrapping = false;
            labelOut = tmp;
        }

        static RectTransform BuildDropdownPopupUpward(RectTransform screenRoot,
                                                       float xMin, float xMax,
                                                       out RectTransform contentRoot)
        {
            const float maxH    = 280f;
            const float yOffset = 112f;

            var go = new GameObject("DdPopup", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(screenRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0); rt.anchorMax = new Vector2(xMax, 0);
            rt.pivot     = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(-8f, maxH);
            rt.anchoredPosition = new Vector2(0, yOffset);
            go.GetComponent<Image>().color = PopupBg;

            var sGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            sGo.transform.SetParent(go.transform, false);
            var sRt = sGo.GetComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = new Vector2(2, 2); sRt.offsetMax = new Vector2(-2, -2);
            var sr = sGo.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.1f; sr.inertia = true;
            sr.decelerationRate = 0.135f; sr.scrollSensitivity = 40f;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(sGo.transform, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            var cGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            cGo.transform.SetParent(vpGo.transform, false);
            var cRt = cGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1); cRt.anchoredPosition = Vector2.zero; cRt.sizeDelta = Vector2.zero;

            var vlg = cGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 2f; vlg.padding = new RectOffset(6, 6, 6, 6);
            var csf = cGo.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRt; sr.content = cRt;
            contentRoot = cRt;
            return rt;
        }

        void AddDdItem(RectTransform content, string display, Color accent, Action onSelect)
        {
            var go = new GameObject(display + "Item",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(content, false);
            go.GetComponent<Image>().color = Panel;
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 42f;

            var strip = new GameObject("Strip", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(go.transform, false);
            var sRt = strip.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0, 0); sRt.anchorMax = new Vector2(0, 1);
            sRt.pivot = new Vector2(0, 0.5f); sRt.sizeDelta = new Vector2(3, 0);
            strip.GetComponent<Image>().color = new Color(accent.r, accent.g, accent.b, 0.85f);

            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(go.transform, false);
            var lrt = lgo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(14, 0); lrt.offsetMax = new Vector2(-8, 0);
            var tmp = lgo.AddComponent<TextMeshProUGUI>();
            tmp.text = display; tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontSize = 15; tmp.color = Color.white;
            tmp.raycastTarget = false; tmp.enableWordWrapping = false;
            go.GetComponent<Button>().onClick.AddListener(() => onSelect());
        }

        void ToggleWeaponDropdown()
        {
            if (_rarityDdOpen) CloseDropdown(ref _rarityDdOpen, _rarityDdPopup, ref _rarityDdAnim);
            if (_weaponDdOpen) CloseDropdown(ref _weaponDdOpen, _weaponDdPopup, ref _weaponDdAnim);
            else               OpenDropdown(ref _weaponDdOpen,  _weaponDdPopup, ref _weaponDdAnim);
        }

        void ToggleRarityDropdown()
        {
            if (_weaponDdOpen) CloseDropdown(ref _weaponDdOpen, _weaponDdPopup, ref _weaponDdAnim);
            if (_rarityDdOpen) CloseDropdown(ref _rarityDdOpen, _rarityDdPopup, ref _rarityDdAnim);
            else               OpenDropdown(ref _rarityDdOpen,  _rarityDdPopup, ref _rarityDdAnim);
        }

        void OpenDropdown(ref bool isOpen, RectTransform popup, ref Coroutine anim)
        {
            if (popup == null) return;
            isOpen = true;
            popup.gameObject.SetActive(true);
            popup.SetAsLastSibling();
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(AnimateDd(popup, popup.localScale.y, 1f, false));
        }

        void CloseDropdown(ref bool isOpen, RectTransform popup, ref Coroutine anim)
        {
            if (popup == null || !isOpen) return;
            isOpen = false;
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(AnimateDd(popup, popup.localScale.y, 0f, true));
        }

        IEnumerator AnimateDd(RectTransform popup, float from, float to, bool hide)
        {
            const float dur = 0.14f;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                var s = popup.localScale; s.y = Mathf.Lerp(from, to, e); popup.localScale = s;
                yield return null;
            }
            popup.localScale = new Vector3(1f, to, 1f);
            if (hide && Mathf.Approximately(to, 0f)) popup.gameObject.SetActive(false);
        }

        void SetWeapon(string weapon, string displayText)
        {
            _weaponFilter = weapon;
            if (_weaponDdLabel != null) _weaponDdLabel.text = displayText ?? "SİLAH  ▼";
            CloseDropdown(ref _weaponDdOpen, _weaponDdPopup, ref _weaponDdAnim);
            if (_selectedTarget != null && !string.IsNullOrEmpty(weapon) &&
                !string.Equals(_selectedTarget.WeaponName, weapon, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTarget = null;
                UpdateTargetPanel(null);
            }
            RebuildGrid(); RefreshChance();
        }

        void SetRarity(SkinRarity? rarity, string displayText)
        {
            _rarityFilter = rarity;
            if (_rarityDdLabel != null) _rarityDdLabel.text = displayText ?? "NADİRLİK  ▼";
            CloseDropdown(ref _rarityDdOpen, _rarityDdPopup, ref _rarityDdAnim);
            if (_selectedTarget != null && rarity.HasValue && _selectedTarget.Rarity != rarity.Value)
            {
                _selectedTarget = null;
                UpdateTargetPanel(null);
            }
            RebuildGrid(); RefreshChance();
        }

        // ─────────────────────────────────────────────────────────────────────
        // COROUTINE YARDIMCILARI
        // ─────────────────────────────────────────────────────────────────────

        IEnumerator InitSpinAnimator()
        {
            yield return null;
            _spinAnimator?.Initialize(spinCenter, chanceLabel?.rectTransform);
            _spinAnimator?.ResetNeedle();
        }

        IEnumerator PlayResultFlash(bool success)
        {
            if (resultFlash == null && resultLabel == null) yield break;
            var col = success ? NeonGreen : NeonRed;
            if (resultLabel != null) { resultLabel.text = success ? "BAŞARILI!" : "BAŞARISIZ"; resultLabel.color = col; }

            if (resultFlash != null)
            {
                resultFlash.color = new Color(col.r, col.g, col.b, 0f);
                const float fadeIn = 0.25f;
                for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
                {
                    resultFlash.color = new Color(col.r, col.g, col.b, Mathf.Lerp(0f, 0.55f, t / fadeIn));
                    yield return null;
                }
                resultFlash.color = new Color(col.r, col.g, col.b, 0.55f);
                yield return new WaitForSecondsRealtime(0.8f);
                const float fadeOut = 0.35f;
                for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
                {
                    resultFlash.color = new Color(col.r, col.g, col.b, Mathf.Lerp(0.55f, 0f, t / fadeOut));
                    yield return null;
                }
                resultFlash.color = new Color(col.r, col.g, col.b, 0f);
            }
            else yield return new WaitForSecondsRealtime(0.8f);

            if (resultLabel != null) resultLabel.text = string.Empty;
        }

        IEnumerator PulseButton()
        {
            if (upgradeButton == null) yield break;
            var btnTransform = upgradeButton.GetComponent<RectTransform>();
            while (true)
            {
                for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.75f)
                {
                    float s = Mathf.Lerp(1f, 1.03f, Mathf.Sin(t * Mathf.PI));
                    btnTransform.localScale = new Vector3(s, s, 1f);
                    yield return null;
                }
            }
        }

        static void AddHoverTrigger(EventTrigger et, EventTriggerType type, Action cb)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => cb());
            et.triggers.Add(entry);
        }

        void ScaleCard(UpgradeCard card, float to, float dur)
        {
            if (card?.Root == null) return;
            StartCoroutine(ScaleRoutine(card.Root.transform, to, dur));
        }

        static IEnumerator ScaleRoutine(Transform t, float to, float dur)
        {
            float from = t.localScale.x;
            for (float e = 0f; e < dur; e += Time.unscaledDeltaTime)
            {
                float s = Mathf.Lerp(from, to, e / dur);
                t.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            t.localScale = new Vector3(to, to, 1f);
        }

        // ─────────────────────────────────────────────────────────────────────
        // İÇ SINIFLAR
        // ─────────────────────────────────────────────────────────────────────

        sealed class UpgradeCard
        {
            public GameObject         Root;
            public WeaponSkinCardView View;
            public Button             Button;
            public SkinDefinitionSO   Skin;
            public float              BaseScale;
        }

        /// <summary>
        /// Kart butonunun drag olaylarını üstündeki ScrollRect'e iletir.
        /// Böylece liste üzerinde basılı tutup sürükleyerek scroll yapılabilir.
        /// </summary>
        sealed class ScrollRectPassthrough : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public ScrollRect Target;
            public void OnBeginDrag(PointerEventData e) => Target?.OnBeginDrag(e);
            public void OnDrag(PointerEventData e)      => Target?.OnDrag(e);
            public void OnEndDrag(PointerEventData e)   => Target?.OnEndDrag(e);
        }
    }
}
