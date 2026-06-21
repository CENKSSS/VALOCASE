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
using ValoCase.Services.Backend;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Value-based upgrade screen.
    ///
    /// Flow:  multi-select inventory skins → combined value updates → pick any target
    /// → success chance (value ratio) updates → UPGRADE.
    ///
    /// No weapon / rarity / category filters — a single Price sort toggle only.
    /// </summary>
    public sealed class UpgradeScreen : UIScreenBase
    {
        // ── Inspector refs ────────────────────────────────────────────────────
        [SerializeField] UINavigator         navigator;
        [SerializeField] Button              backButton;
        [SerializeField] TextMeshProUGUI     walletLabel;
        [SerializeField] WeaponSkinCardView  cardPrefab;

        // Left panel — repurposed as the multi-selection summary.
        [SerializeField] Image           inputIcon;
        [SerializeField] TextMeshProUGUI inputName;
        [SerializeField] TextMeshProUGUI inputRarityLabel;
        [SerializeField] TextMeshProUGUI inputVpLabel;
        [SerializeField] TextMeshProUGUI inputChanceLabel;
        [SerializeField] Image           inputRarityStrip;
        [SerializeField] GameObject      inputPlaceholder;

        // Right panel — chosen target.
        [SerializeField] Image           targetIcon;
        [SerializeField] TextMeshProUGUI targetName;
        [SerializeField] TextMeshProUGUI targetRarityLabel;
        [SerializeField] TextMeshProUGUI targetVpLabel;
        [SerializeField] TextMeshProUGUI targetChanceLabel;
        [SerializeField] Image           targetRarityStrip;
        [SerializeField] GameObject      targetPlaceholder;

        // Center panel.
        [SerializeField] RectTransform   spinCenter;
        [SerializeField] TextMeshProUGUI chanceLabel;
        [SerializeField] TextMeshProUGUI chanceHint;
        [SerializeField] Button          upgradeButton;
        [SerializeField] TextMeshProUGUI upgradeButtonLabel;

        // Legacy VP quick-filter buttons — removed from the flow; destroyed at runtime.
        [SerializeField] Button          vpBtn1000;
        [SerializeField] Button          vpBtn2000;
        [SerializeField] Button          vpBtn3000;

        // Bottom section.
        [SerializeField] Button          inventoryTabBtn;
        [SerializeField] Button          allSkinsTabBtn;
        [SerializeField] Image           inventoryTabLine;
        [SerializeField] Image           allSkinsTabLine;
        [SerializeField] Transform       skinGridRoot;
        [SerializeField] RectTransform   skinScrollRt;
        [SerializeField] RectTransform   filterBar;

        // Result overlay.
        [SerializeField] Image           resultFlash;
        [SerializeField] TextMeshProUGUI resultLabel;

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color NeonGreen   = new Color(0.404f, 0.859f, 0.686f, 1f);
        static readonly Color NeonRed     = new Color(1f,    0.275f, 0.333f, 1f);
        static readonly Color BtnOff      = new Color(0.15f,  0.18f,  0.22f,  0.85f);
        static readonly Color SortBg      = new Color(0.157f, 0.172f, 0.204f, 1f);
        static readonly Color TabActive   = new Color(0.925f, 0.910f, 0.882f, 1f);
        static readonly Color TabInactive = new Color(0.545f, 0.592f, 0.561f, 1f);
        static readonly Color Muted       = new Color(0.560f, 0.588f, 0.643f, 1f);
        static readonly Color SlotBg      = new Color(0.055f, 0.070f, 0.098f, 1f);
        static readonly Color SlotBorder  = new Color(0.235f, 0.267f, 0.325f, 1f);

        // Dark-navy theme shared with Inventory / Battle / Main Menu.
        static readonly Color PanelBg     = new Color(0.055f, 0.078f, 0.157f, 1f); // #0E1428
        static readonly Color CenterBg    = new Color(0.067f, 0.094f, 0.153f, 1f); // #111827
        static readonly Color PanelBorder = new Color(0.165f, 0.204f, 0.278f, 1f); // #2A3447

        static readonly Dictionary<SkinRarity, Color> RarityAccents = new Dictionary<SkinRarity, Color>
        {
            { SkinRarity.Select,    new Color(0.55f, 0.65f, 0.78f, 1f) },
            { SkinRarity.Deluxe,    new Color(0f,    0.96f, 1f,    1f) },
            { SkinRarity.Premium,   new Color(0.69f, 0.15f, 1f,    1f) },
            { SkinRarity.Exclusive, new Color(1f,    0.18f, 0.67f, 1f) },
            { SkinRarity.Ultra,     new Color(0.22f, 1f,    0.08f, 1f) },
        };

        // ── State ─────────────────────────────────────────────────────────────
        bool _invPriceDescending = true;   // ENVANTER sort, default Price ↓
        bool _tgtPriceDescending = true;   // HEDEFLER sort, independent of the left side

        readonly List<SkinDefinitionSO> _selectedInputs  = new List<SkinDefinitionSO>();
        readonly List<SkinDefinitionSO> _selectedTargets = new List<SkinDefinitionSO>();

        bool      _isUpgrading;
        Coroutine _pulseCo;

        UpgradeSpinAnimator _spinAnimator;
        ScrollRect          _skinScrollRect;     // left (ENVANTER) list

        // Right (HEDEFLER) list — a runtime clone of the inventory scroll view so it
        // inherits the exact same viewport/grid/cell setup.
        ScrollRect    _targetScrollRect;
        RectTransform _targetScrollRt;
        Transform     _targetGridRoot;
        bool          _splitBuilt;

        Button          _sortButton;       // ENVANTER price sort
        TextMeshProUGUI _sortLabel;
        Button          _tgtSortButton;    // HEDEFLER price sort
        TextMeshProUGUI _tgtSortLabel;
        GameObject      _emptyState;
        TextMeshProUGUI _emptyLabel;
        bool            _themed;
        TextMeshProUGUI _chanceCaption;

        // Center module (ring + caption + value + UPGRADE) is held in a container that
        // fills spinCenter and is uniformly scaled down when the panel is too small, so
        // the stack can never spill out of the center panel into the lists below.
        RectTransform   _centerModule;
        const float     CenterDesignH  = 340f;
        const float     CenterDesignW  = 252f;
        const float     CenterMinScale = 0.5f;

        // Selected-input slot grid (replaces the large preview): a fixed 2×2 = 4 box
        // grid centered in the panel with very large, slightly rectangular cells (wider
        // than tall) so weapon thumbnails read clearly. The last box is reserved as a
        // "+N" overflow indicator, so at most 3 thumbnails are shown individually.
        const int   SlotColumns     = 2;
        const int   SlotRows        = 2;
        const int   TotalSlots      = SlotColumns * SlotRows; // 4
        const int   OverflowSlotIdx = TotalSlots - 1;         // 3 (last box)
        const int   MaxThumbSlots   = TotalSlots - 1;         // 3 visible skins max
        const float SlotCellAspect  = 70f / 55f;              // cell width : height
        RectTransform _slotGridRoot;
        readonly List<InputSlot> _slots = new List<InputSlot>();
        RectTransform _targetSlotGridRoot;
        readonly List<InputSlot> _targetSlots = new List<InputSlot>();

        readonly List<UpgradeCard> _cardPool   = new List<UpgradeCard>();  // ENVANTER cards
        readonly List<UpgradeCard> _targetPool = new List<UpgradeCard>();  // HEDEFLER cards

        // ── Awake ─────────────────────────────────────────────────────────────
        void Awake()
        {
            _spinAnimator = gameObject.AddComponent<UpgradeSpinAnimator>();

            backButton?.onClick.AddListener(() => { if (!_isUpgrading) navigator?.Navigate(ScreenType.MainMenu); });
            upgradeButton?.onClick.AddListener(OnUpgradeClicked);

            // No tab switching anymore — both lists are always visible side by side,
            // and the former tab buttons act as static section headers.
            SetTabLabel(inventoryTabBtn, "ENVANTER");
            SetTabLabel(allSkinsTabBtn,  "HEDEFLER");
        }

        protected override void OnShown()
        {
            GameEvents.OnInventoryChanged += HandleInventoryChanged;
            GameEvents.OnVpChanged        += HandleVpChanged;
            RefreshWallet();
            if (walletLabel != null) walletLabel.gameObject.SetActive(false);   // wallet shown in top navbar

            _skinScrollRect = skinScrollRt != null ? skinScrollRt.GetComponent<ScrollRect>() : null;

            // A mid-upgrade tab switch deactivates this screen and kills the upgrade
            // coroutine before it can clear this flag; reset on entry or the button stays
            // permanently disabled.
            _isUpgrading = false;
            _selectedInputs.Clear();
            _selectedTargets.Clear();

            RemoveLegacyVpButtons();
            ApplyPanelTheme();
            BuildCenterModule();
            BuildSplitLists();
            BuildSortButtons();
            BuildEmptyState();
            BuildInputSlots();
            BuildTargetSlots();

            RefreshInputSummary();
            RefreshTargetSummary();
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
            if (_pulseCo != null) { StopCoroutine(_pulseCo); _pulseCo = null; }
            _spinAnimator?.ResetNeedle();
        }

        void HandleInventoryChanged()
        {
            // Drop any selected inputs the player no longer owns.
            var ctx = GameContext.Instance;
            if (ctx?.Inventory != null && _selectedInputs.Count > 0)
            {
                _selectedInputs.RemoveAll(s => s == null || !ctx.Inventory.Owns(s.SkinId));
                if (_selectedInputs.Count == 0) _selectedTargets.Clear();
            }
            ValidateTargets();
            RefreshInputSummary();
            RefreshTargetSummary();
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

        // ── Selection helpers ──────────────────────────────────────────────────
        int SelectedTotal()
        {
            var total = 0;
            for (int i = 0; i < _selectedInputs.Count; i++) total += _selectedInputs[i].VpValue;
            return total;
        }

        int TargetTotal()
        {
            var total = 0;
            for (int i = 0; i < _selectedTargets.Count; i++) total += _selectedTargets[i].VpValue;
            return total;
        }

        bool IsSelectedInput(SkinDefinitionSO skin)
        {
            if (skin == null) return false;
            for (int i = 0; i < _selectedInputs.Count; i++)
                if (_selectedInputs[i].SkinId == skin.SkinId) return true;
            return false;
        }

        bool IsSelectedTarget(SkinDefinitionSO skin)
        {
            if (skin == null) return false;
            for (int i = 0; i < _selectedTargets.Count; i++)
                if (_selectedTargets[i].SkinId == skin.SkinId) return true;
            return false;
        }

        // Targets are cleared when no inputs remain; any target that became a selected
        // input is also dropped so the same item can't be both sides.
        void ValidateTargets()
        {
            if (SelectedTotal() <= 0) { _selectedTargets.Clear(); return; }
            _selectedTargets.RemoveAll(t => t == null || IsSelectedInput(t));
        }

        // ─────────────────────────────────────────────────────────────────────
        // SPLIT LISTS (ENVANTER left | HEDEFLER right, both always visible)
        // ─────────────────────────────────────────────────────────────────────

        // One-time layout pass: the former tab buttons become static section headers
        // over their halves, the prefab scroll view is narrowed to the left half for
        // the inventory, a clone of it becomes the right-half target list, and a thin
        // vertical divider separates the two columns.
        void BuildSplitLists()
        {
            if (_splitBuilt || skinScrollRt == null) return;
            _splitBuilt = true;

            StyleHeader(inventoryTabBtn, inventoryTabLine, 0f,   0.5f);
            StyleHeader(allSkinsTabBtn,  allSkinsTabLine,  0.5f, 1f);

            if (filterBar != null) filterBar.gameObject.SetActive(true);

            // Left half: inventory list. Vertical offsets: header (56) + sort row (48).
            skinScrollRt.anchorMin = new Vector2(0f, 0f);
            skinScrollRt.anchorMax = new Vector2(0.5f, 1f);
            skinScrollRt.offsetMin = new Vector2(8f, 8f);
            skinScrollRt.offsetMax = new Vector2(-6f, -104f);

            // Right half: clone the whole scroll view so viewport, mask, grid layout
            // and cell sizing carry over 1:1; only its content (cards) differs.
            var cloneGo = Instantiate(skinScrollRt.gameObject, skinScrollRt.parent);
            cloneGo.name = "TargetScroll";
            _targetScrollRt = (RectTransform)cloneGo.transform;
            _targetScrollRt.anchorMin = new Vector2(0.5f, 0f);
            _targetScrollRt.anchorMax = new Vector2(1f, 1f);
            _targetScrollRt.offsetMin = new Vector2(6f, 8f);
            _targetScrollRt.offsetMax = new Vector2(-8f, -104f);
            _targetScrollRect = cloneGo.GetComponent<ScrollRect>();
            _targetGridRoot   = _targetScrollRect != null ? _targetScrollRect.content : null;

            // The clone must start empty — drop anything copied from the source grid.
            if (_targetGridRoot != null)
                for (int i = _targetGridRoot.childCount - 1; i >= 0; i--)
                    Destroy(_targetGridRoot.GetChild(i).gameObject);

            // Full-height divider between the two list columns, below the header row.
            var div = new GameObject("ListDivider", typeof(RectTransform), typeof(Image));
            div.transform.SetParent(skinScrollRt.parent, false);
            var drt = (RectTransform)div.transform;
            drt.anchorMin = new Vector2(0.5f, 0f);
            drt.anchorMax = new Vector2(0.5f, 1f);
            drt.pivot     = new Vector2(0.5f, 0.5f);
            drt.offsetMin = new Vector2(-1f, 10f);
            drt.offsetMax = new Vector2(1f, -58f);
            var dImg = div.GetComponent<Image>();
            dImg.color         = new Color(PanelBorder.r, PanelBorder.g, PanelBorder.b, 0.6f);
            dImg.raycastTarget = false;
        }

        // Former tab button → static section header: pinned to its half, always
        // bright, red underline accent, no click behavior.
        static void StyleHeader(Button btn, Image line, float xMin, float xMax)
        {
            if (btn == null) return;
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            btn.interactable = false;

            SetTabLabelColor(btn, true);
            if (line != null) line.color = NeonRed;
        }

        static void SetTabLabel(Button btn, string text)
        {
            if (btn == null) return;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = text;
        }

        static void SetTabLabelColor(Button btn, bool active)
        {
            if (btn == null) return;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.color = active ? TabActive : TabInactive;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRICE SORT
        // ─────────────────────────────────────────────────────────────────────

        // The VP-delta quick filters are gone from the flow. Destroy the prefab objects
        // outright so no inactive filter controls linger in the hierarchy.
        void RemoveLegacyVpButtons()
        {
            if (vpBtn1000 != null) Destroy(vpBtn1000.gameObject);
            if (vpBtn2000 != null) Destroy(vpBtn2000.gameObject);
            if (vpBtn3000 != null) Destroy(vpBtn3000.gameObject);
        }

        // ─────────────────────────────────────────────────────────────────────
        // VISUAL THEME (one-time, visuals only — no behavior changes)
        // ─────────────────────────────────────────────────────────────────────

        // Replaces the flat gray panel look with the dark-navy palette used by the
        // rest of the game, restyles the chance readout and the UPGRADE button.
        void ApplyPanelTheme()
        {
            if (_themed) return;
            _themed = true;

            // Shared section background (cover image, aspect preserved) instead of
            // the old flat ScreenBg recolor.
            FullscreenBackground.AttachShared(gameObject);

            StylePanel(inputIcon  != null ? inputIcon.transform.parent  : null, PanelBg);
            StylePanel(targetIcon != null ? targetIcon.transform.parent : null, PanelBg);
            StylePanel(spinCenter, CenterBg);

            CompactLayout();
            StyleChanceReadout();
            StyleUpgradeButton();
            StyleTargetPanel();
            StyleTabs();
            StyleBottomPanel();
        }

        // One unified dark panel behind tabs + sort row + skin list, styled exactly
        // like the three upper panels. The tab row, tab buttons and filter bar lose
        // their own backgrounds so they read as content ON the panel instead of
        // separate floating sections, and a subtle vertical divider splits
        // ENVANTER from HEDEFLER.
        void StyleBottomPanel()
        {
            var section = skinScrollRt != null ? skinScrollRt.parent as RectTransform : null;
            if (section == null) return;

            StylePanel(section, PanelBg);

            var tabRow = inventoryTabBtn != null ? inventoryTabBtn.transform.parent : null;
            ClearBackground(tabRow);
            ClearBackground(inventoryTabBtn != null ? inventoryTabBtn.transform : null);
            ClearBackground(allSkinsTabBtn  != null ? allSkinsTabBtn.transform  : null);
            ClearBackground(filterBar);

            // Centered vertical divider between the two tabs.
            if (tabRow != null && tabRow.Find("TabDivider") == null)
            {
                var go = new GameObject("TabDivider", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(tabRow, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.22f);
                rt.anchorMax = new Vector2(0.5f, 0.78f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2f, 0f);
                rt.anchoredPosition = Vector2.zero;
                var img = go.GetComponent<Image>();
                img.color         = new Color(PanelBorder.r, PanelBorder.g, PanelBorder.b, 0.6f);
                img.raycastTarget = false;
            }
        }

        // Makes a row/button background invisible without touching its raycast or
        // layout role — buttons stay clickable, only the box outline disappears.
        static void ClearBackground(Transform t)
        {
            if (t == null) return;
            var img = t.GetComponent<Image>();
            if (img != null) img.color = new Color(0f, 0f, 0f, 0f);
        }

        // Hairline borders instead of the old thick outlines — the three areas should
        // read as one connected module, not three heavy desktop panels.
        static void StylePanel(Transform panel, Color bg)
        {
            if (panel == null) return;
            var img = panel.GetComponent<Image>();
            if (img == null) return;
            img.color = bg;

            var border = panel.GetComponent<Outline>();
            if (border == null) border = panel.gameObject.AddComponent<Outline>();
            border.effectColor    = new Color(PanelBorder.r, PanelBorder.g, PanelBorder.b, 0.55f);
            border.effectDistance = new Vector2(1f, -1f);
        }

        // Shifts the top-band/list boundary up a little: the upper module had far more
        // vertical room than it uses, the skin list never has enough. Anchors only —
        // panel contents are all relatively anchored and follow automatically.
        void CompactLayout()
        {
            const float SplitY = 0.56f;   // prefab boundary is 0.52

            var topBand = spinCenter != null ? spinCenter.parent as RectTransform : null;
            if (topBand != null)
                topBand.anchorMin = new Vector2(topBand.anchorMin.x, SplitY);

            var listSection = skinScrollRt != null ? skinScrollRt.parent as RectTransform : null;
            if (listSection != null)
                listSection.anchorMax = new Vector2(listSection.anchorMax.x, SplitY);
        }

        // Compact upgrade module, vertically grouped around the panel center:
        // ring (% inside) → "BAŞARI ŞANSI" → value line (X VP → Y VP) → UPGRADE.
        // The ring (radius 88) is centered on the chance label, so the label position
        // also moves the ring; everything below hugs the ring's bottom edge.
        void StyleChanceReadout()
        {
            if (chanceLabel != null)
            {
                chanceLabel.alignment    = TextAlignmentOptions.Center;
                chanceLabel.fontStyle    = FontStyles.Bold;
                chanceLabel.enableWordWrapping = false;
                // "100.00%" must fit the ~149 px ring hole, "0.00%" should fill it.
                chanceLabel.enableAutoSizing = true;
                chanceLabel.fontSizeMin  = 20;
                chanceLabel.fontSizeMax  = 38;
                var rt = chanceLabel.rectTransform;
                rt.sizeDelta = new Vector2(132f, 60f);
                rt.anchoredPosition = new Vector2(0f, 52f);   // ring sits slightly high
            }

            if (_chanceCaption == null && spinCenter != null)
            {
                var go = new GameObject("ChanceCaption", typeof(RectTransform));
                go.transform.SetParent(spinCenter, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(260f, 22f);
                rt.anchoredPosition = new Vector2(0f, -54f);

                _chanceCaption = go.AddComponent<TextMeshProUGUI>();
                _chanceCaption.text             = "BAŞARI ŞANSI";
                _chanceCaption.alignment        = TextAlignmentOptions.Center;
                _chanceCaption.fontSize         = 14;
                _chanceCaption.fontStyle        = FontStyles.Bold;
                _chanceCaption.characterSpacing = 6f;
                _chanceCaption.color            = Muted;
                _chanceCaption.raycastTarget    = false;
                _chanceCaption.enableWordWrapping = false;
            }

            // Value line directly under the caption (input → target VP).
            if (chanceHint != null)
            {
                var rt = chanceHint.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(280f, 26f);
                rt.anchoredPosition = new Vector2(0f, -80f);
                chanceHint.alignment = TextAlignmentOptions.Center;
                chanceHint.fontSize  = 17;
                chanceHint.fontStyle = FontStyles.Bold;
                chanceHint.color     = TabActive;
                chanceHint.enableWordWrapping = false;
            }
        }

        void StyleUpgradeButton()
        {
            if (upgradeButton == null) return;

            // Pull the button off the panel bottom and into the center module, right
            // under the value line, so ring + caption + values + button read as one unit.
            var rt = upgradeButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(236f, 56f);
                rt.anchoredPosition = new Vector2(0f, -136f);
            }

            var border = upgradeButton.GetComponent<Outline>();
            if (border == null) border = upgradeButton.gameObject.AddComponent<Outline>();
            border.effectColor    = new Color(PanelBorder.r, PanelBorder.g, PanelBorder.b, 0.6f);
            border.effectDistance = new Vector2(1f, -1f);

            if (upgradeButtonLabel != null)
            {
                upgradeButtonLabel.fontSize         = 23;
                upgradeButtonLabel.fontStyle        = FontStyles.Bold;
                upgradeButtonLabel.characterSpacing = 4f;
                upgradeButtonLabel.alignment        = TextAlignmentOptions.Center;
            }
        }

        // Reparents the center stack into a single container that fills spinCenter, so
        // it can be scaled as one unit. Element positions are preserved, so visuals are
        // unchanged on panels large enough to hold the design-size stack.
        void BuildCenterModule()
        {
            if (spinCenter == null) return;
            if (_centerModule != null) { LayoutCenterModule(); return; }

            var go = new GameObject("CenterModule", typeof(RectTransform));
            go.transform.SetParent(spinCenter, false);
            _centerModule = (RectTransform)go.transform;
            _centerModule.anchorMin = Vector2.zero;
            _centerModule.anchorMax = Vector2.one;
            _centerModule.offsetMin = Vector2.zero;
            _centerModule.offsetMax = Vector2.zero;
            _centerModule.pivot     = new Vector2(0.5f, 0.5f);

            ReparentToModule(chanceLabel   != null ? chanceLabel.rectTransform   : null);
            ReparentToModule(_chanceCaption != null ? _chanceCaption.rectTransform : null);
            ReparentToModule(chanceHint     != null ? chanceHint.rectTransform     : null);
            ReparentToModule(upgradeButton  != null ? (RectTransform)upgradeButton.transform : null);

            LayoutCenterModule();
        }

        void ReparentToModule(RectTransform rt)
        {
            if (rt == null || _centerModule == null) return;
            var pos = rt.anchoredPosition;
            rt.SetParent(_centerModule, false);
            rt.anchoredPosition = pos;
        }

        // Scales the module by the limiting panel dimension so the ring + value + button
        // always fit inside spinCenter; clamped to 1 so large panels keep the original size.
        void LayoutCenterModule()
        {
            if (_centerModule == null || spinCenter == null) return;
            float h = spinCenter.rect.height;
            float w = spinCenter.rect.width;
            if (h < 1f || w < 1f) return;
            float s = Mathf.Clamp(Mathf.Min(h / CenterDesignH, w / CenterDesignW), CenterMinScale, 1f);
            _centerModule.localScale = new Vector3(s, s, 1f);
        }

        void OnRectTransformDimensionsChange()
        {
            if (_centerModule != null) LayoutCenterModule();
        }

        // The target panel is the reward preview: a large weapon image with name,
        // rarity and value stacked under it — no probability numbers here, chance
        // lives only in the center ring. Re-anchors the existing prefab refs.
        void StyleTargetPanel()
        {
            if (targetIcon != null)
            {
                var rt = targetIcon.rectTransform;
                rt.anchorMin = new Vector2(0.06f, 0.40f);
                rt.anchorMax = new Vector2(0.94f, 0.96f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                targetIcon.preserveAspect = true;
            }

            SetBand(targetName, 0.285f, 0.385f);
            CenterLabel(targetName);
            if (targetName != null)
            {
                targetName.fontSize           = 22;
                targetName.fontStyle          = FontStyles.Bold;
                targetName.enableWordWrapping = false;
                targetName.overflowMode       = TextOverflowModes.Ellipsis;
            }

            SetBand(targetRarityLabel, 0.20f, 0.275f);
            CenterLabel(targetRarityLabel);
            if (targetRarityLabel != null)
            {
                targetRarityLabel.fontSize         = 14;
                targetRarityLabel.fontStyle        = FontStyles.Bold;
                targetRarityLabel.characterSpacing = 4f;
            }

            SetBand(targetVpLabel, 0.075f, 0.19f);
            CenterLabel(targetVpLabel);
            if (targetVpLabel != null)
            {
                targetVpLabel.fontSize  = 26;
                targetVpLabel.fontStyle = FontStyles.Bold;
            }
        }

        // Cleaner tab row: bolder, spaced labels with a strong red underline on the
        // active tab so sacrifice (ENVANTER) vs reward (HEDEFLER) is always obvious.
        void StyleTabs()
        {
            StyleTabLabel(inventoryTabBtn);
            StyleTabLabel(allSkinsTabBtn);
            StyleTabLine(inventoryTabLine);
            StyleTabLine(allSkinsTabLine);
        }

        static void StyleTabLabel(Button btn)
        {
            if (btn == null) return;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl == null) return;
            lbl.fontSize         = 18;
            lbl.fontStyle        = FontStyles.Bold;
            lbl.characterSpacing = 3f;
            lbl.alignment        = TextAlignmentOptions.Center;
        }

        static void StyleTabLine(Image line)
        {
            if (line == null) return;
            var rt = line.rectTransform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 4f);
        }

        // One independent price-sort button per list half, in the shared sort row.
        void BuildSortButtons()
        {
            if (filterBar == null) return;

            if (_sortButton == null)
                _sortButton = CreateSortButton("InvSortButton", 0.04f, 0.46f,
                                               () => ToggleSort(true), out _sortLabel);
            if (_tgtSortButton == null)
                _tgtSortButton = CreateSortButton("TgtSortButton", 0.54f, 0.96f,
                                                  () => ToggleSort(false), out _tgtSortLabel);
            UpdateSortLabels();
        }

        Button CreateSortButton(string name, float xMin, float xMax,
                                UnityEngine.Events.UnityAction onClick,
                                out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(filterBar, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = new Vector2(0, 4);
            rt.offsetMax = new Vector2(0, -4);

            var img = go.GetComponent<Image>();
            img.color = SortBg;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(go.transform, false);
            var lrt = lgo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(12, 0); lrt.offsetMax = new Vector2(-12, 0);
            label = lgo.AddComponent<TextMeshProUGUI>();
            label.alignment          = TextAlignmentOptions.Center;
            label.fontSize           = 16;
            label.fontStyle          = FontStyles.Bold;
            label.color              = TabActive;
            label.raycastTarget      = false;
            label.enableWordWrapping = false;

            return btn;
        }

        void ToggleSort(bool inventorySide)
        {
            if (inventorySide) _invPriceDescending = !_invPriceDescending;
            else               _tgtPriceDescending = !_tgtPriceDescending;
            SoundManager.Instance?.Play(SoundId.UiClick);
            UpdateSortLabels();
            if (inventorySide) RebuildInventoryGrid();
            else               RebuildTargetGrid();
        }

        void UpdateSortLabels()
        {
            if (_sortLabel    != null) _sortLabel.text    = _invPriceDescending ? "Fiyat ↓" : "Fiyat ↑";
            if (_tgtSortLabel != null) _tgtSortLabel.text = _tgtPriceDescending ? "Fiyat ↓" : "Fiyat ↑";
        }

        // ─────────────────────────────────────────────────────────────────────
        // PANELS
        // ─────────────────────────────────────────────────────────────────────

        void RefreshInputSummary()
        {
            int count = _selectedInputs.Count;
            int total = SelectedTotal();

            UpdateSlots(_slots, _selectedInputs);

            // The large preview and old placeholder are gone — the slot grid is the
            // empty/filled state now.
            if (inputPlaceholder != null) inputPlaceholder.SetActive(false);
            if (inputIcon != null && inputIcon.gameObject.activeSelf) inputIcon.gameObject.SetActive(false);

            // Summary block under the slot grid (Selected Skins: X / Total Value).
            if (inputName != null)
            {
                inputName.gameObject.SetActive(true);
                inputName.text  = count == 0 ? "SKİN SEÇİLMEDİ" : $"{count} SKİN SEÇİLDİ";
                inputName.color = count == 0 ? Muted : TabActive;
            }
            if (inputRarityLabel != null)
            {
                inputRarityLabel.gameObject.SetActive(true);
                inputRarityLabel.text  = "TOPLAM DEĞER";
                inputRarityLabel.color = Muted;
            }
            if (inputVpLabel != null)
            {
                inputVpLabel.gameObject.SetActive(true);
                inputVpLabel.text  = $"{total:N0} VP";
                inputVpLabel.color = count == 0 ? Muted : TabActive;
            }
            if (inputRarityStrip != null)
                inputRarityStrip.color = count > 0 ? NeonRed : BtnOff;
        }

        // ── Selected-input slot grid ───────────────────────────────────────────

        // Builds the fixed 2×2 slot grid filling most of the selected-input panel, and
        // drops the summary labels into the band below it. The grid is the largest box
        // with SlotCellAspect-shaped cells that fits the available area, centered — so
        // cells stay slightly wider than tall on any panel size.
        void BuildInputSlots()
        {
            if (_slotGridRoot != null) return;

            Transform parent = inputIcon != null ? inputIcon.transform.parent
                             : (inputName != null ? inputName.transform.parent : transform);
            if (parent == null) return;

            if (inputIcon != null) inputIcon.gameObject.SetActive(false);   // remove large preview
            if (inputPlaceholder != null) inputPlaceholder.SetActive(false);

            _slotGridRoot = BuildSlotGrid(parent, "InputSlot", _slots, RemoveSelectedInputSlot);

            // Push the summary labels into the lower band, full width and centered.
            SetBand(inputName,        0.185f, 0.275f);
            SetBand(inputRarityLabel, 0.095f, 0.165f);
            SetBand(inputVpLabel,     0.010f, 0.090f);
            CenterLabel(inputName);
            CenterLabel(inputRarityLabel);
            CenterLabel(inputVpLabel);
        }

        // Mirrors BuildInputSlots for the right (HEDEF SKİN) panel using the same grid.
        void BuildTargetSlots()
        {
            if (_targetSlotGridRoot != null) return;

            Transform parent = targetIcon != null ? targetIcon.transform.parent
                             : (targetName != null ? targetName.transform.parent : transform);
            if (parent == null) return;

            if (targetIcon != null) targetIcon.gameObject.SetActive(false);
            if (targetPlaceholder != null) targetPlaceholder.SetActive(false);

            _targetSlotGridRoot = BuildSlotGrid(parent, "TargetSlot", _targetSlots, RemoveSelectedTargetSlot);

            SetBand(targetName,        0.185f, 0.275f);
            SetBand(targetRarityLabel, 0.095f, 0.165f);
            SetBand(targetVpLabel,     0.010f, 0.090f);
            CenterLabel(targetName);
            CenterLabel(targetRarityLabel);
            CenterLabel(targetVpLabel);
        }

        // Builds the fixed 2×2 slot grid filling most of a panel and returns its root.
        // The grid is the largest box with SlotCellAspect-shaped cells that fits the
        // available area, centered — so cells stay slightly wider than tall.
        RectTransform BuildSlotGrid(Transform parent, string namePrefix,
                                    List<InputSlot> slots, Action<InputSlot> onSlotClick)
        {
            var areaGo = new GameObject(namePrefix + "Area", typeof(RectTransform));
            areaGo.transform.SetParent(parent, false);
            var area = areaGo.GetComponent<RectTransform>();
            area.anchorMin = new Vector2(0.025f, 0.295f);
            area.anchorMax = new Vector2(0.975f, 0.985f);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            area.SetAsFirstSibling();   // render behind the summary labels

            var go = new GameObject(namePrefix + "Grid", typeof(RectTransform));
            go.transform.SetParent(area, false);
            var gridRoot = go.GetComponent<RectTransform>();
            gridRoot.anchorMin = new Vector2(0.5f, 0.5f);
            gridRoot.anchorMax = new Vector2(0.5f, 0.5f);
            gridRoot.pivot     = new Vector2(0.5f, 0.5f);
            var fitter = go.AddComponent<AspectRatioFitter>();
            fitter.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = SlotColumns * SlotCellAspect / SlotRows;

            for (int i = 0; i < TotalSlots; i++)
                slots.Add(CreateSlot(gridRoot, i, onSlotClick));

            return gridRoot;
        }

        // Fills the slots with the given skins in selection order; remaining boxes stay
        // empty. The last box (index 3) is reserved: it shows "+N" once the selection
        // exceeds the 3 visible thumbnail positions.
        static void UpdateSlots(List<InputSlot> slots, List<SkinDefinitionSO> source)
        {
            if (slots.Count == 0) return;

            int count = source.Count;
            int visibleThumbs = Mathf.Min(count, MaxThumbSlots);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (i < visibleThumbs)
                {
                    var skin = source[i];
                    slot.Skin          = skin;
                    slot.Thumb.sprite  = skin.Icon;
                    slot.Thumb.enabled = skin.Icon != null;
                    slot.Border.effectColor = RarityAccents.TryGetValue(skin.Rarity, out var rc) ? rc : SlotBorder;
                    slot.Overflow.gameObject.SetActive(false);
                }
                else if (i == OverflowSlotIdx && count > MaxThumbSlots)
                {
                    slot.Skin          = null;
                    slot.Thumb.enabled = false;
                    slot.Border.effectColor = NeonRed;
                    slot.Overflow.gameObject.SetActive(true);
                    slot.Overflow.text = $"+{count - MaxThumbSlots}";
                }
                else
                {
                    slot.Skin          = null;
                    slot.Thumb.enabled = false;
                    slot.Border.effectColor = SlotBorder;
                    slot.Overflow.gameObject.SetActive(false);
                }
            }
        }

        InputSlot CreateSlot(RectTransform gridRoot, int index, Action<InputSlot> onSlotClick)
        {
            int col = index % SlotColumns;
            int row = index / SlotColumns;

            var go = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(gridRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(col / (float)SlotColumns,        1f - (row + 1) / (float)SlotRows);
            rt.anchorMax = new Vector2((col + 1) / (float)SlotColumns,  1f - row / (float)SlotRows);
            const float gap = 8f;
            rt.offsetMin = new Vector2(gap, gap);
            rt.offsetMax = new Vector2(-gap, -gap);

            var bg = go.GetComponent<Image>();
            bg.color = SlotBg;
            bg.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var border = go.AddComponent<Outline>();
            border.effectColor    = SlotBorder;
            border.effectDistance = new Vector2(2f, -2f);   // clean but clearly visible rarity border

            var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            thumbGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)thumbGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8, 8); trt.offsetMax = new Vector2(-8, -8);
            var thumb = thumbGo.GetComponent<Image>();
            thumb.preserveAspect = true;
            thumb.raycastTarget  = false;
            thumb.enabled        = false;

            var ovGo = new GameObject("Overflow", typeof(RectTransform));
            ovGo.transform.SetParent(go.transform, false);
            var ort = (RectTransform)ovGo.transform;
            ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
            ort.offsetMin = Vector2.zero; ort.offsetMax = Vector2.zero;
            var ov = ovGo.AddComponent<TextMeshProUGUI>();
            ov.alignment          = TextAlignmentOptions.Center;
            ov.enableAutoSizing   = true;
            ov.fontSizeMin        = 24;
            ov.fontSizeMax        = 72;
            ov.fontStyle          = FontStyles.Bold;
            ov.color              = Color.white;
            ov.raycastTarget      = false;
            ov.enableWordWrapping = false;
            ovGo.SetActive(false);

            var slot = new InputSlot { Root = go, Bg = bg, Thumb = thumb, Border = border, Overflow = ov, Button = btn };
            btn.onClick.AddListener(() => onSlotClick(slot));
            return slot;
        }

        // Clicking a filled selected-input slot returns that skin to the inventory list.
        void RemoveSelectedInputSlot(InputSlot slot)
        {
            if (_isUpgrading || slot?.Skin == null) return;
            int idx = _selectedInputs.FindIndex(s => s.SkinId == slot.Skin.SkinId);
            if (idx < 0) return;

            _selectedInputs.RemoveAt(idx);
            SoundManager.Instance?.Play(SoundId.UiClick);

            ValidateTargets();
            RefreshInputSummary();
            RefreshTargetSummary();
            RebuildGrid();
            RefreshChance();
        }

        // Clicking a filled selected-target slot returns that skin to the target list.
        void RemoveSelectedTargetSlot(InputSlot slot)
        {
            if (_isUpgrading || slot?.Skin == null) return;
            int idx = _selectedTargets.FindIndex(s => s.SkinId == slot.Skin.SkinId);
            if (idx < 0) return;

            _selectedTargets.RemoveAt(idx);
            SoundManager.Instance?.Play(SoundId.UiClick);

            RefreshTargetSummary();
            RebuildTargetGrid();
            RefreshChance();
        }

        // Anchors a label to a horizontal band [yMin, yMax] of its parent, full width
        // (5 % side margins), letting its own alignment center the text within.
        static void SetBand(Graphic g, float yMin, float yMax)
        {
            if (g == null) return;
            var rt = g.rectTransform;
            rt.anchorMin = new Vector2(0.05f, yMin);
            rt.anchorMax = new Vector2(0.95f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void CenterLabel(TextMeshProUGUI t)
        {
            if (t != null) t.alignment = TextAlignmentOptions.Center;
        }

        // Right panel mirrors the input summary: selected target count + combined value.
        void RefreshTargetSummary()
        {
            int count = _selectedTargets.Count;
            int total = TargetTotal();

            UpdateSlots(_targetSlots, _selectedTargets);

            if (targetPlaceholder != null) targetPlaceholder.SetActive(false);
            if (targetIcon != null && targetIcon.gameObject.activeSelf) targetIcon.gameObject.SetActive(false);

            if (targetName != null)
            {
                targetName.gameObject.SetActive(true);
                targetName.text  = count == 0 ? "HEDEF SEÇİLMEDİ" : $"{count} HEDEF SEÇİLDİ";
                targetName.color = count == 0 ? Muted : TabActive;
            }
            if (targetRarityLabel != null)
            {
                targetRarityLabel.gameObject.SetActive(true);
                targetRarityLabel.text  = "TOPLAM DEĞER";
                targetRarityLabel.color = Muted;
            }
            if (targetVpLabel != null)
            {
                targetVpLabel.gameObject.SetActive(true);
                targetVpLabel.text  = $"{total:N0} VP";
                targetVpLabel.color = count == 0 ? Muted : TabActive;
            }
            if (targetRarityStrip != null)
                targetRarityStrip.color = count > 0 ? NeonRed : BtnOff;
        }

        // ─────────────────────────────────────────────────────────────────────
        // GRID
        // ─────────────────────────────────────────────────────────────────────

        // Both halves rebuild together; each can also rebuild independently
        // (sort toggles, multiplier clicks).
        void RebuildGrid()
        {
            RebuildInventoryGrid();
            RebuildTargetGrid();
        }

        void RebuildInventoryGrid()
        {
            if (skinGridRoot == null || cardPrefab == null) return;
            var ctx = GameContext.Instance;
            var skins = new List<SkinDefinitionSO>();

            if (ctx?.Inventory != null && ctx.Content != null)
            {
                foreach (var entry in ctx.Inventory.Items)
                {
                    var s = ctx.Content.GetSkin(entry?.skinId);
                    if (s != null) skins.Add(s);
                }
            }

            SortByPrice(skins, _invPriceDescending);
            BindList(skins, _cardPool, skinGridRoot, isTarget: false);
            ForceGridRebuild(skinGridRoot);
        }

        void RebuildTargetGrid()
        {
            if (_targetGridRoot == null || cardPrefab == null) return;
            var ctx = GameContext.Instance;
            var skins = new List<SkinDefinitionSO>();

            // Targets: every skin (no minimum-value rule), excluding the skins already
            // selected as inputs so the same item can't be both input and target.
            int total = SelectedTotal();
            if (total > 0 && ctx?.Content != null)
            {
                IReadOnlyList<SkinDefinitionSO> allSkins = ctx.Content.Skins;
                if (allSkins == null || allSkins.Count == 0)
                    allSkins = ctx.Content.GetFilteredSkins(null, null);

                foreach (var s in allSkins)
                {
                    if (s == null || IsSelectedInput(s)) continue;
                    skins.Add(s);
                }
            }

            SortByPrice(skins, _tgtPriceDescending);
            BindList(skins, _targetPool, _targetGridRoot, isTarget: true);
            UpdateEmptyState(skins.Count);
            ForceGridRebuild(_targetGridRoot);
        }

        void BindList(List<SkinDefinitionSO> skins, List<UpgradeCard> pool,
                      Transform gridRoot, bool isTarget)
        {
            foreach (var c in pool)
                if (c.Root != null) c.Root.SetActive(false);

            for (int i = 0; i < skins.Count; i++)
            {
                UpgradeCard card;
                if (i < pool.Count) card = pool[i];
                else { card = CreateCard(gridRoot, isTarget); pool.Add(card); }
                BindCard(card, skins[i]);
            }
        }

        static void ForceGridRebuild(Transform gridRoot)
        {
            if (gridRoot == null) return;
            var gridRt = gridRoot.GetComponent<RectTransform>();
            if (gridRt != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(gridRt);
        }

        // Sort by VP value (desc/asc per toggle), stable on equal values via name.
        static void SortByPrice(List<SkinDefinitionSO> skins, bool descending)
        {
            int dir = descending ? 1 : -1;
            skins.Sort((a, b) =>
            {
                int cmp = dir * b.VpValue.CompareTo(a.VpValue);
                return cmp != 0 ? cmp : string.Compare(a.SkinName, b.SkinName, StringComparison.Ordinal);
            });
        }

        // PF_WeaponSkinCard is authored for the Weapons grid's 200×290 cells; the Upgrade
        // grid uses 150×210 cells, so the prefab's fixed-size Icon (178×120, center-
        // anchored) overflows the smaller card. Re-anchor the icon to a relative, padded
        // band so it scales with the cell; preserveAspect keeps the weapon's proportions,
        // so every sprite fits inside the card without stretch, crop, or overflow.
        static void FitCardThumbnail(WeaponSkinCardView view)
        {
            var iconRt = view.transform.Find("Icon") as RectTransform;
            if (iconRt == null) return;

            // Band sized so the thumbnail reads ~10–15 % larger than the original
            // 0.08–0.92 / 0.46–0.92 fit while still clearing the card edges.
            iconRt.anchorMin = new Vector2(0.04f, 0.45f);
            iconRt.anchorMax = new Vector2(0.96f, 0.97f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;

            var img = iconRt.GetComponent<Image>();
            if (img != null)
            {
                img.type           = Image.Type.Simple;
                img.preserveAspect = true;
            }
        }

        // The prefab's labels are likewise authored for the 200×290 card: fixed 184 px
        // width at fixed offsets, so on the 150×210 Upgrade cell long skin names poke
        // past the card edges and overlap neighbours. Re-anchor each label to a relative
        // band with side padding and clamp it to one centered, ellipsized line so text
        // can never escape the card. Upgrade-screen instances only — the prefab and the
        // other screens that use it are untouched.
        static void FitCardLabel(WeaponSkinCardView view, string childName,
                                 float yMin, float yMax, float fontSize)
        {
            var rt = view.transform.Find(childName) as RectTransform;
            if (rt == null) return;

            rt.anchorMin = new Vector2(0.06f, yMin);
            rt.anchorMax = new Vector2(0.94f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = rt.GetComponent<TextMeshProUGUI>();
            if (tmp == null) return;
            tmp.fontSize           = fontSize;
            tmp.enableAutoSizing   = false;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
        }

        UpgradeCard CreateCard(Transform parent, bool isTarget)
        {
            var view = Instantiate(cardPrefab, parent, false);
            FitCardThumbnail(view);
            // Text bands sit lower than the prefab defaults: the name clears the rarity
            // symbol and the weapon type gets a clear gap under the name for a cleaner
            // hierarchy. The rarity symbol itself is nudged ~4 px above its prefab spot
            // so it and the name get a little extra breathing room on each side.
            var dotRt = view.transform.Find("RarityDot") as RectTransform;
            if (dotRt != null)
                dotRt.anchoredPosition = dotRt.anchoredPosition + new Vector2(0f, 4f);
            FitCardLabel(view, "SkinName",    0.21f, 0.36f, 15f);
            FitCardLabel(view, "WeaponLabel", 0.06f, 0.18f, 11f);
            var go   = view.gameObject;
            var btn  = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            var img  = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var card = new UpgradeCard { Root = go, View = view, Button = btn, BaseScale = 1f, IsTarget = isTarget };

            // Selection frame — a thin red border + top accent, toggled when selected.
            var frame = new GameObject("SelectFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(go.transform, false);
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            var fImg = frame.GetComponent<Image>();
            fImg.color = new Color(NeonRed.r, NeonRed.g, NeonRed.b, 0.16f);
            fImg.raycastTarget = false;
            var strip = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(frame.transform, false);
            var srt = strip.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 1f); srt.sizeDelta = new Vector2(0, 6);
            srt.anchoredPosition = Vector2.zero;
            var stripImg = strip.GetComponent<Image>();
            stripImg.color = NeonRed; stripImg.raycastTarget = false;
            card.SelectFrame = frame;
            frame.SetActive(false);
            frame.transform.SetAsLastSibling();

            btn.onClick.AddListener(() => OnCardClicked(card));

            var pass = go.AddComponent<ScrollRectPassthrough>();
            pass.Target = isTarget ? _targetScrollRect : _skinScrollRect;

            var et = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            AddHoverTrigger(et, EventTriggerType.PointerEnter, () => ScaleCard(card, card.BaseScale * 1.07f, 0.10f));
            AddHoverTrigger(et, EventTriggerType.PointerExit,  () => ScaleCard(card, card.BaseScale, 0.10f));

            return card;
        }

        void BindCard(UpgradeCard card, SkinDefinitionSO skin)
        {
            card.Skin = skin;
            card.Root.SetActive(true);
            card.View.Bind(skin, GameContext.Instance?.RarityVisuals);

            var pass = card.Root.GetComponent<ScrollRectPassthrough>();
            if (pass != null) pass.Target = card.IsTarget ? _targetScrollRect : _skinScrollRect;

            bool isSelected = card.IsTarget ? IsSelectedTarget(skin) : IsSelectedInput(skin);

            if (card.SelectFrame != null)
            {
                card.SelectFrame.SetActive(isSelected);
                if (isSelected) card.SelectFrame.transform.SetAsLastSibling();
            }

            card.Button.interactable = true;

            float targetScale = isSelected ? 1.05f : 1f;
            card.BaseScale = targetScale;
            card.Root.transform.localScale = new Vector3(targetScale, targetScale, 1f);
        }

        void OnCardClicked(UpgradeCard card)
        {
            if (_isUpgrading || card?.Skin == null) return;
            SoundManager.Instance?.Play(SoundId.UiClick);

            if (!card.IsTarget)
            {
                // Toggle multi-selection; the target list depends on the combined
                // value, so both halves refresh.
                int idx = _selectedInputs.FindIndex(s => s.SkinId == card.Skin.SkinId);
                if (idx >= 0) _selectedInputs.RemoveAt(idx);
                else          _selectedInputs.Add(card.Skin);

                ValidateTargets();
                RefreshInputSummary();
                RefreshTargetSummary();
                RebuildGrid();
            }
            else
            {
                if (_selectedInputs.Count == 0)
                {
                    GameEvents.RaiseToast("Önce ENVANTERden skin seç");
                    return;
                }
                int idx = _selectedTargets.FindIndex(s => s.SkinId == card.Skin.SkinId);
                if (idx >= 0) _selectedTargets.RemoveAt(idx);
                else          _selectedTargets.Add(card.Skin);
                RefreshTargetSummary();
                RebuildTargetGrid();   // selection frames update; inventory side unaffected
            }

            RefreshChance();
        }

        // ─────────────────────────────────────────────────────────────────────
        // EMPTY STATE
        // ─────────────────────────────────────────────────────────────────────

        void BuildEmptyState()
        {
            if (_emptyState != null || _targetScrollRt == null) return;

            var go = new GameObject("TargetEmptyState", typeof(RectTransform));
            go.transform.SetParent(_targetScrollRt, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(40, 40); rt.offsetMax = new Vector2(-40, -40);

            _emptyLabel = go.AddComponent<TextMeshProUGUI>();
            _emptyLabel.alignment        = TextAlignmentOptions.Center;
            _emptyLabel.fontSize         = 22;
            _emptyLabel.fontStyle        = FontStyles.Bold;
            _emptyLabel.color            = Muted;
            _emptyLabel.raycastTarget    = false;
            _emptyLabel.enableWordWrapping = true;

            _emptyState = go;
            _emptyState.SetActive(false);
        }

        void UpdateEmptyState(int shownCount)
        {
            if (_emptyState == null) return;

            // Empty state lives in the HEDEFLER list only.
            bool show = shownCount == 0;
            _emptyState.SetActive(show);
            if (!show) return;

            _emptyLabel.text = _selectedInputs.Count == 0
                ? "Önce ENVANTERden\nskin seç"
                : "Bu değer için uygun hedef yok\nDaha fazla skin seç";
        }

        // ─────────────────────────────────────────────────────────────────────
        // CHANCE + BUTTON
        // ─────────────────────────────────────────────────────────────────────

        bool CanUpgradeNow()
        {
            if (_selectedInputs.Count == 0 || _selectedTargets.Count == 0) return false;
            return TargetTotal() > SelectedTotal();
        }

        void RefreshChance()
        {
            var upgrade = GameContext.Instance?.Upgrade;
            bool valid  = CanUpgradeNow();
            int  total  = SelectedTotal();
            int  tgtTotal = TargetTotal();

            float chance = 0f;
            if (upgrade != null && valid)
                chance = upgrade.ComputeValueChance(total, tgtTotal);

            // Always a two-decimal percentage — "0.00%" when nothing is selected,
            // never a "--%" placeholder. Color follows the red/orange/green bands.
            if (chanceLabel != null)
            {
                chanceLabel.text  = $"{chance * 100f:0.00}%";
                chanceLabel.color = UpgradeSpinAnimator.GetChanceColor(chance);
            }

            // Probability is communicated only by the center chance ring — the
            // side-panel BAŞARI/KAYIP readouts stay hidden permanently.
            if (inputChanceLabel  != null) inputChanceLabel.gameObject.SetActive(false);
            if (targetChanceLabel != null) targetChanceLabel.gameObject.SetActive(false);

            if (chanceHint != null)
            {
                if (_selectedInputs.Count == 0) chanceHint.text = "ENVANTERden skin seç";
                else if (_selectedTargets.Count == 0) chanceHint.text = "Hedef skin seç";
                else if (!valid) chanceHint.text = "Yükseltilemez";
                else chanceHint.text = $"{total:N0} VP → {tgtTotal:N0} VP";

                // Bright value line when a real pairing is shown, muted guidance otherwise.
                chanceHint.color = valid ? TabActive : Muted;
            }

            _spinAnimator?.SetChance(chance);

            bool canUpgrade = !_isUpgrading && valid;
            if (upgradeButton != null)
            {
                upgradeButton.interactable = canUpgrade;
                var img = upgradeButton.GetComponent<Image>();
                if (img != null) img.color = canUpgrade ? NeonRed : BtnOff;
            }
            if (upgradeButtonLabel != null)
                upgradeButtonLabel.text = _isUpgrading ? "..." : "YÜKSELT";
        }

        // ─────────────────────────────────────────────────────────────────────
        // UPGRADE FLOW
        // ─────────────────────────────────────────────────────────────────────

        void OnUpgradeClicked()
        {
            if (_isUpgrading || !CanUpgradeNow()) return;
            var ctx = GameContext.Instance;
            if (ctx?.Upgrade == null) return;

            // Offline pre-check (backend mode only): do not start the spin or lock the
            // button when offline — no skins removed, no VP change, no target grant.
            if (ctx.BackendEnabled && BackendErrorMapper.IsOffline)
            {
                GameEvents.RaiseToast(BackendErrorMapper.Offline);
                return;
            }

            _isUpgrading = true;
            SoundManager.Instance?.Play(SoundId.UiClick);
            RefreshChance();
            StartCoroutine(UpgradeSequence(ctx,
                new List<SkinDefinitionSO>(_selectedInputs),
                new List<SkinDefinitionSO>(_selectedTargets)));
        }

        // The single skin shown in the win popup / toasts when several targets are won.
        static SkinDefinitionSO RepresentativeTarget(List<SkinDefinitionSO> targets)
        {
            SkinDefinitionSO best = null;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null && (best == null || targets[i].VpValue > best.VpValue))
                    best = targets[i];
            return best;
        }

        IEnumerator UpgradeSequence(GameContext ctx, List<SkinDefinitionSO> inputs, List<SkinDefinitionSO> targets)
        {
            int total = 0;
            for (int i = 0; i < inputs.Count; i++) total += inputs[i].VpValue;
            int targetTotal = 0;
            for (int i = 0; i < targets.Count; i++) targetTotal += targets[i].VpValue;
            float chance = ctx.Upgrade.ComputeValueChance(total, targetTotal);
            _spinAnimator?.SetChance(chance);

            // Backend mode: the server decides + commits. Play the existing animation
            // against the server result, then resync inventory. Local mode below is
            // left exactly as it was.
            if (ctx.BackendEnabled)
            {
                yield return BackendUpgradeSequence(ctx, inputs, targets, chance);
                yield break;
            }

            // Suppress reactive rebuilds while the upgrade resolves and animates.
            // TryUpgradeMulti's ConsumeOne/AddSkin raise OnInventoryChanged synchronously;
            // letting HandleInventoryChanged run mid-sequence would wipe the selection,
            // target preview and both lists before the result is even shown. We keep the
            // on-screen state frozen through the animation/popup, then reconcile once.
            GameEvents.OnInventoryChanged -= HandleInventoryChanged;

            if (!ctx.Upgrade.TryUpgradeMultiTarget(inputs, targets, out var success))
            {
                GameEvents.OnInventoryChanged += HandleInventoryChanged;
                _isUpgrading = false;
                RefreshChance();
                GameEvents.RaiseToast("Yükseltme gerçekleştirilemedi");
                yield break;
            }

            // Inputs are consumed in the real inventory now, but the target preview and
            // both lists stay exactly as they were through the spin and result flash.
            yield return StartCoroutine(_spinAnimator.AnimateSpin(chance, success, null));
            SoundManager.Instance?.Play(success ? SoundId.CaseReveal : SoundId.UiBack);
            yield return StartCoroutine(PlayResultFlash(success));

            // Result popup — the target preview is still visible behind it.
            if (success)
            {
                var rep = RepresentativeTarget(targets);
                var popup = ValoCase.UI.SkinWinPopup.EnsureExists();
                if (popup != null && rep != null) popup.Show(rep, null);
                else if (rep != null) GameEvents.RaiseToast("Tebrikler! " + rep.SkinName + " kazanildi");
            }
            else
            {
                GameEvents.RaiseToast("BASARISIZ — skinler kaybedildi");
            }

            // ── Reconcile with the real inventory (single, controlled rebuild) ──────
            GameEvents.OnInventoryChanged += HandleInventoryChanged;

            // Inputs were consumed and targets resolved, so both selections clear.
            _selectedInputs.Clear();
            _selectedTargets.Clear();

            _isUpgrading = false;
            _spinAnimator?.ResetNeedle();

            RefreshInputSummary();
            RefreshTargetSummary();
            RebuildGrid();                          // lists refresh from real inventory data
            RefreshChance();
        }

        // ── Backend-authoritative upgrade (optimistic spin) ─────────────────────
        // The needle starts spinning the instant the player taps; the backend request
        // runs in parallel. The decision and the consume/grant still happen server-side
        // — Unity never calls TryUpgradeMulti and never guesses success/fail. The wheel
        // free-spins until the server answers, then lands on the authoritative result.
        IEnumerator BackendUpgradeSequence(GameContext ctx, List<SkinDefinitionSO> inputs,
            List<SkinDefinitionSO> targets, float localChance)
        {
            // Freeze on-screen selection/preview through the request + animation, exactly
            // like the local path, so the win/lose flow renders against stable state.
            GameEvents.OnInventoryChanged -= HandleInventoryChanged;

            // Start the spin NOW (instant feedback) and fire the request in parallel.
            UpgradeBackendResult result = null;
            bool requestDone = false;
            StartCoroutine(RequestUpgrade(ctx, inputs, targets, r => { result = r; requestDone = true; }));

            bool spinFinished = false;
            var spinCo = StartCoroutine(
                _spinAnimator.SpinUntilResolved(localChance, _ => spinFinished = true));

            // Wait for the authoritative server result (the wheel keeps spinning).
            while (!requestDone) yield return null;

            // ── Failure: stop the spin gracefully, re-enable UI. The routine already
            //    resynced when the local cache may be stale. Keep selection usable. ──
            if (result == null || !result.Ok)
            {
                if (spinCo != null) StopCoroutine(spinCo);
                GameEvents.OnInventoryChanged += HandleInventoryChanged;
                _isUpgrading = false;
                _spinAnimator?.ResetNeedle();
                if (!string.IsNullOrEmpty(result?.Error)) GameEvents.RaiseToast(result.Error);
                RefreshInputSummary();
                RefreshTargetSummary();
                RebuildGrid();
                RefreshChance();
                yield break;
            }

            bool success = result.Success;
            var resolvedTarget = result.Target != null ? result.Target : RepresentativeTarget(targets);

            // Hand the authoritative result to the wheel so it decelerates onto it.
            // The chance keeps the same local estimate the player saw before clicking.
            _spinAnimator.ProvideResult(success, localChance);

            // Wait for the spin + arc flash to finish on the real result.
            while (!spinFinished) yield return null;

            SoundManager.Instance?.Play(success ? SoundId.CaseReveal : SoundId.UiBack);
            yield return StartCoroutine(PlayResultFlash(success));

            if (success)
            {
                var popup = ValoCase.UI.SkinWinPopup.EnsureExists();
                if (popup != null) popup.Show(resolvedTarget, null);
                else GameEvents.RaiseToast("Tebrikler! " + resolvedTarget.SkinName + " kazanildi");
            }
            else
            {
                GameEvents.RaiseToast("BASARISIZ — skinler kaybedildi");
            }

            // ── Reconcile with authoritative backend inventory. The resync raises
            //    OnInventoryChanged asynchronously; HandleInventoryChanged (re-subscribed)
            //    then drops the consumed inputs. We also clear locally for an immediate UI. ──
            GameEvents.OnInventoryChanged += HandleInventoryChanged;
            ctx.RequestBackendResync();

            _selectedInputs.Clear();
            _selectedTargets.Clear();
            _isUpgrading = false;
            _spinAnimator?.ResetNeedle();

            RefreshInputSummary();
            RefreshTargetSummary();
            RebuildGrid();
            RefreshChance();
        }

        // Runs the backend upgrade request to completion and reports the result.
        IEnumerator RequestUpgrade(GameContext ctx, List<SkinDefinitionSO> inputs,
            List<SkinDefinitionSO> targets, Action<UpgradeBackendResult> onDone)
        {
            UpgradeBackendResult r = null;
            yield return ctx.UpgradeBackendRoutine(inputs, targets, x => r = x);
            onDone?.Invoke(r);
        }

        // ─────────────────────────────────────────────────────────────────────
        // COROUTINE HELPERS
        // ─────────────────────────────────────────────────────────────────────

        IEnumerator InitSpinAnimator()
        {
            yield return null;
            _spinAnimator?.Initialize(_centerModule != null ? _centerModule : spinCenter, chanceLabel?.rectTransform);
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
        // INNER TYPES
        // ─────────────────────────────────────────────────────────────────────

        sealed class UpgradeCard
        {
            public GameObject         Root;
            public WeaponSkinCardView View;
            public Button             Button;
            public SkinDefinitionSO   Skin;
            public GameObject         SelectFrame;
            public float              BaseScale;
            public bool               IsTarget;   // right (HEDEFLER) list card
        }

        sealed class InputSlot
        {
            public GameObject       Root;
            public Image            Bg;
            public Image            Thumb;
            public Outline          Border;
            public TextMeshProUGUI  Overflow;
            public Button           Button;
            public SkinDefinitionSO Skin;
        }

        /// <summary>
        /// Forwards drag events from a card button to the parent ScrollRect so the
        /// list can be scrolled by dragging over cards.
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
