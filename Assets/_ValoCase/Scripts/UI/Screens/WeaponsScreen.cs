using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;

namespace ValoCase.UI.Screens
{
    public sealed class WeaponsScreen : UIScreenBase
    {
        // ── Inspector refs ────────────────────────────────────────────────────
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;
        [SerializeField] Transform tabRoot;      // weapon filter row (68 px strip)
        [SerializeField] Transform gridRoot;
        [SerializeField] WeaponSkinCardView cardPrefab;

        // ── Weapon dropdown ───────────────────────────────────────────────────
        TextMeshProUGUI _weaponDropdownLabel;
        RectTransform   _weaponDropdownPopup;     // root (animated)
        RectTransform   _weaponDropdownContent;  // scroll content — items added here
        bool            _weaponDropdownOpen;
        Coroutine       _weaponDropdownAnim;

        // ── Rarity filter row (built at runtime) ──────────────────────────────
        RectTransform _rarityRow;

        // (button, rarity|null=All, baseColor, accentColor)
        readonly List<(Button btn, SkinRarity? rar, Color baseColor, Color accent)>
            _rarityBtns = new List<(Button btn, SkinRarity? rar, Color baseColor, Color accent)>();

        // ── Object pool ───────────────────────────────────────────────────────
        readonly List<WeaponSkinCardView> _pool = new List<WeaponSkinCardView>();

        bool _filtersBuilt;

        // ── Layout constants ──────────────────────────────────────────────────
        const float TopBarH     = 72f;
        const float TabStripH   = 68f;
        const float RarityRowH  = 56f;
        const float ScrollPad   = 12f;
        // Total pixels the scroll area must be pushed down from top:
        const float ScrollTopInset = TopBarH + TabStripH + RarityRowH + ScrollPad;

        // ── Neon palette (matches ValoCaseUIBuilder) ──────────────────────────
        static readonly Color ActiveColor = new Color(1f,    0.176f, 0.667f, 1f);  // neon pink "All" active
        static readonly Color IdleColor   = new Color(0.07f, 0.10f,  0.16f,  0.92f); // glass dark

        // Labels sourced from the central RaritySystem — do not duplicate here.

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.MainMenu));
        }

        protected override void OnShown()
        {
            EnsureFiltersBuilt();
            EnsureScrollLayout();
            RefreshButtonVisuals();
            ApplyFilters();
        }

        protected override void OnHidden()
        {
            if (_weaponDropdownOpen) CloseWeaponDropdown();
        }

        // ── Scroll layout fix ─────────────────────────────────────────────────
        // Pushes the scroll area below TopBar + TabStrip + RarityRow so the
        // back button and filter rows are never hidden behind the grid.

        void EnsureScrollLayout()
        {
            if (gridRoot == null) return;
            var scrollRoot = gridRoot.parent?.parent as RectTransform;
            if (scrollRoot == null) return;

            var max = scrollRoot.offsetMax;
            if (max.y > -ScrollTopInset)
            {
                max.y = -ScrollTopInset;
                scrollRoot.offsetMax = max;
            }
        }

        // ── One-time filter build ─────────────────────────────────────────────

        void EnsureFiltersBuilt()
        {
            if (_filtersBuilt) return;

            var db = GameContext.Instance?.Content;
            if (db == null)
            {
                Debug.LogWarning("[WeaponsScreen] ContentDatabase yüklenemedi — filtreler atlandı.");
                return;
            }

            _filtersBuilt = true;
            BuildWeaponDropdown(db);
            BuildRarityRow(db);
        }

        // ── Weapon dropdown ───────────────────────────────────────────────────

        void BuildWeaponDropdown(ContentDatabaseSO db)
        {
            if (tabRoot == null) return;

            // Header button
            var headerGo = new GameObject("WeaponDropdownHeader",
                typeof(RectTransform), typeof(Image), typeof(Button));
            headerGo.transform.SetParent(tabRoot, false);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = Vector2.zero;
            headerRt.anchorMax = Vector2.one;
            headerRt.offsetMin = new Vector2(12, 8);
            headerRt.offsetMax = new Vector2(-12, -8);
            headerGo.GetComponent<Image>().color = IdleColor;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(headerGo.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(20, 0);
            labelRt.offsetMax = new Vector2(-20, 0);
            _weaponDropdownLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _weaponDropdownLabel.text = "TÜM SİLAHLAR  ▼";
            _weaponDropdownLabel.alignment = TextAlignmentOptions.Center;
            _weaponDropdownLabel.fontSize = 22;
            _weaponDropdownLabel.fontStyle = FontStyles.Bold;
            _weaponDropdownLabel.color = Color.white;
            _weaponDropdownLabel.raycastTarget = false;
            _weaponDropdownLabel.enableWordWrapping = false;

            headerGo.GetComponent<Button>().onClick.AddListener(ToggleWeaponDropdown);

            // Popup root — fixed height, y-scale animated open/close
            const float popupMaxH = 380f;
            var popupGo = new GameObject("WeaponDropdownPopup", typeof(RectTransform), typeof(Image));
            popupGo.transform.SetParent(transform, false);
            _weaponDropdownPopup = popupGo.GetComponent<RectTransform>();
            _weaponDropdownPopup.anchorMin        = new Vector2(0, 1);
            _weaponDropdownPopup.anchorMax        = new Vector2(1, 1);
            _weaponDropdownPopup.pivot            = new Vector2(0.5f, 1);
            _weaponDropdownPopup.sizeDelta        = new Vector2(-48f, popupMaxH);
            _weaponDropdownPopup.anchoredPosition = new Vector2(0, -(TopBarH + TabStripH));
            popupGo.GetComponent<Image>().color   = new Color(0.06f, 0.09f, 0.13f, 0.97f);

            // ScrollRect
            var sGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            sGo.transform.SetParent(popupGo.transform, false);
            var sRt = sGo.GetComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = new Vector2(2, 2); sRt.offsetMax = new Vector2(-2, -2);
            var sr = sGo.GetComponent<ScrollRect>();
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.movementType      = ScrollRect.MovementType.Elastic;
            sr.elasticity        = 0.1f;
            sr.inertia           = true;
            sr.decelerationRate  = 0.135f;
            sr.scrollSensitivity = 40f;

            // Viewport
            var vpGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(sGo.transform, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);

            // Scroll content — items parented here
            var cGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            cGo.transform.SetParent(vpGo.transform, false);
            _weaponDropdownContent = cGo.GetComponent<RectTransform>();
            _weaponDropdownContent.anchorMin        = new Vector2(0, 1);
            _weaponDropdownContent.anchorMax        = new Vector2(1, 1);
            _weaponDropdownContent.pivot            = new Vector2(0.5f, 1);
            _weaponDropdownContent.anchoredPosition = Vector2.zero;
            _weaponDropdownContent.sizeDelta        = Vector2.zero;

            var vlg = cGo.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(8, 8, 8, 8);

            var fitter = cGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRt;
            sr.content  = _weaponDropdownContent;

            AddDropdownItem("Tümü", null);
            foreach (var weapon in db.GetUniqueWeaponNames())
                AddDropdownItem(weapon, weapon);

            _weaponDropdownPopup.localScale = new Vector3(1f, 0f, 1f);
            popupGo.SetActive(false);
            popupGo.transform.SetAsLastSibling();
        }

        void AddDropdownItem(string display, string weaponValue)
        {
            var itemGo = new GameObject(display + "Item",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            itemGo.transform.SetParent(_weaponDropdownContent, false);
            itemGo.GetComponent<Image>().color = IdleColor;

            var le = itemGo.GetComponent<LayoutElement>();
            le.minHeight       = 54f;
            le.preferredHeight = 54f;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(itemGo.transform, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20, 0);
            rt.offsetMax = new Vector2(-20, 0);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = display;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontSize = 20;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;

            var capturedValue   = weaponValue;
            var capturedDisplay = display;
            itemGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                SetWeapon(capturedValue);
                if (_weaponDropdownLabel != null)
                    _weaponDropdownLabel.text = capturedDisplay.ToUpperInvariant() + "  ▼";
                CloseWeaponDropdown();
            });
        }

        // ── Rarity pill row ───────────────────────────────────────────────────

        void BuildRarityRow(ContentDatabaseSO db)
        {
            // Horizontal strip anchored below weapon tab strip.
            var rowGo = new GameObject("RarityFilterRow",
                typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(transform, false);
            _rarityRow = rowGo.GetComponent<RectTransform>();
            _rarityRow.anchorMin        = new Vector2(0, 1);
            _rarityRow.anchorMax        = new Vector2(1, 1);
            _rarityRow.pivot            = new Vector2(0.5f, 1);
            _rarityRow.sizeDelta        = new Vector2(0, RarityRowH);
            _rarityRow.anchoredPosition = new Vector2(0, -(TopBarH + TabStripH));
            rowGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.30f);

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.childAlignment        = TextAnchor.MiddleCenter;
            hlg.childControlWidth     = false;
            hlg.childControlHeight    = false;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(10, 10, 8, 8);

            var visuals = GameContext.Instance?.RarityVisuals;

            // "Tümü" — always present
            var allBtn = CreateRarityPill("Tümü", null, IdleColor, ActiveColor);
            _rarityBtns.Add((allBtn, null, IdleColor, ActiveColor));

            // ALL rarity tiers always shown, in canonical order from RaritySystem.
            // Selecting a tier with no matching skins shows an empty grid — that's correct UX.
            foreach (var r in RaritySystem.OrderedRarities)
            {
                var cap    = r;
                var label  = RaritySystem.ShortLabels.TryGetValue(r, out var lbl) ? lbl : r.ToString();
                var baseBg = IdleColor;
                var accent = Color.white;

                if (visuals != null && visuals.TryGet(r, out var v))
                {
                    baseBg = v.cardBgColor;
                    accent = v.primaryColor;
                }

                var btn = CreateRarityPill(label, cap, baseBg, accent);
                _rarityBtns.Add((btn, cap, baseBg, accent));
            }

            // Keep below the dropdown popup in draw order
            rowGo.transform.SetSiblingIndex(rowGo.transform.parent.childCount - 2);
        }

        Button CreateRarityPill(string label, SkinRarity? rarity,
                                Color baseColor, Color accentColor)
        {
            var pillH = RarityRowH - 16f;   // 40 px tall pill

            var go = new GameObject(label + "Pill",
                typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(LayoutElement), typeof(Outline));
            go.transform.SetParent(_rarityRow, false);

            // Layout element — flexible width pill
            var le = go.GetComponent<LayoutElement>();
            le.minWidth       = 110f;
            le.preferredWidth = 140f;
            le.minHeight      = pillH;
            le.preferredHeight = pillH;

            var img = go.GetComponent<Image>();
            img.color = baseColor;

            // Outline — visible when this rarity is selected
            var outline = go.GetComponent<Outline>();
            outline.effectColor    = new Color(accentColor.r, accentColor.g, accentColor.b, 0f);
            outline.effectDistance = new Vector2(3, 3);

            // Label
            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 0);
            textRt.offsetMax = new Vector2(-6, 0);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text               = label;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.fontSize           = 14;
            tmp.fontStyle          = FontStyles.Bold;
            tmp.color              = Color.white;
            tmp.raycastTarget      = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;

            var cap = rarity;
            go.GetComponent<Button>().onClick.AddListener(() => SetRarity(cap));

            return go.GetComponent<Button>();
        }

        // ── Dropdown open / close ─────────────────────────────────────────────

        void ToggleWeaponDropdown()
        {
            if (_weaponDropdownOpen) CloseWeaponDropdown();
            else OpenWeaponDropdown();
        }

        void OpenWeaponDropdown()
        {
            if (_weaponDropdownPopup == null) return;
            _weaponDropdownOpen = true;
            _weaponDropdownPopup.gameObject.SetActive(true);
            _weaponDropdownPopup.SetAsLastSibling();
            if (_weaponDropdownAnim != null) StopCoroutine(_weaponDropdownAnim);
            _weaponDropdownAnim = StartCoroutine(AnimateDropdown(
                _weaponDropdownPopup.localScale.y, 1f, hideOnEnd: false));
        }

        void CloseWeaponDropdown()
        {
            if (_weaponDropdownPopup == null || !_weaponDropdownOpen) return;
            _weaponDropdownOpen = false;
            if (_weaponDropdownAnim != null) StopCoroutine(_weaponDropdownAnim);
            _weaponDropdownAnim = StartCoroutine(AnimateDropdown(
                _weaponDropdownPopup.localScale.y, 0f, hideOnEnd: true));
        }

        IEnumerator AnimateDropdown(float fromY, float toY, bool hideOnEnd)
        {
            const float dur = 0.18f;
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var p     = Mathf.Clamp01(t / dur);
                var eased = 1f - Mathf.Pow(1f - p, 3f);
                var s     = _weaponDropdownPopup.localScale;
                s.y = Mathf.Lerp(fromY, toY, eased);
                _weaponDropdownPopup.localScale = s;
                yield return null;
            }
            _weaponDropdownPopup.localScale = new Vector3(1f, toY, 1f);
            if (hideOnEnd && Mathf.Approximately(toY, 0f))
                _weaponDropdownPopup.gameObject.SetActive(false);
            _weaponDropdownAnim = null;
        }

        // ── Filter state ──────────────────────────────────────────────────────

        void SetWeapon(string weapon)
        {
            WeaponFilterState.SelectedWeapon = weapon;
            RefreshButtonVisuals();
            ApplyFilters();
        }

        void SetRarity(SkinRarity? rarity)
        {
            WeaponFilterState.SelectedRarity = rarity;
            RefreshButtonVisuals();
            ApplyFilters();
        }

        void RefreshButtonVisuals()
        {
            // Weapon dropdown header
            if (_weaponDropdownLabel != null)
            {
                var w = WeaponFilterState.SelectedWeapon;
                _weaponDropdownLabel.text =
                    (string.IsNullOrEmpty(w) ? "TÜM SİLAHLAR" : w.ToUpperInvariant()) + "  ▼";
            }

            // Rarity pills — active = full vivid color + outline; idle = darkened + no outline
            foreach (var (btn, r, baseColor, accent) in _rarityBtns)
            {
                if (btn == null) continue;

                bool active = (r == WeaponFilterState.SelectedRarity);

                var img = btn.GetComponent<Image>();
                if (img != null)
                {
                    if (active)
                        // "Tümü" (null) uses ActiveColor when selected; rarity pills use their own base
                        img.color = (r == null) ? ActiveColor : baseColor;
                    else
                        // Idle: desaturate/darken the base color
                        img.color = (r == null)
                            ? IdleColor
                            : new Color(baseColor.r * 0.45f, baseColor.g * 0.45f,
                                        baseColor.b * 0.45f, 1f);
                }

                var outline = btn.GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = new Color(
                        accent.r, accent.g, accent.b,
                        active ? 0.9f : 0f);
            }
        }

        // ── Grid / pool ───────────────────────────────────────────────────────

        void ApplyFilters()
        {
            if (gridRoot == null || cardPrefab == null)
            {
                Debug.LogWarning("[WeaponsScreen] gridRoot veya cardPrefab atanmamış.");
                return;
            }

            var ctx = GameContext.Instance;
            if (ctx?.Content == null)
            {
                Debug.LogWarning("[WeaponsScreen] GameContext veya Content null.");
                return;
            }

            var weapon   = WeaponFilterState.SelectedWeapon;
            var rarity   = WeaponFilterState.SelectedRarity;
            var filtered = ctx.Content.GetFilteredSkins(weapon, rarity);

            // Hide all pooled cards first
            foreach (var c in _pool)
                if (c != null) c.gameObject.SetActive(false);

            // Activate or instantiate cards
            for (var i = 0; i < filtered.Count; i++)
            {
                WeaponSkinCardView card;
                if (i < _pool.Count)
                    card = _pool[i];
                else
                {
                    card = Instantiate(cardPrefab, gridRoot, false);
                    _pool.Add(card);
                }
                card.gameObject.SetActive(true);
                card.Bind(filtered[i], ctx.RarityVisuals);
            }
        }
    }
}
