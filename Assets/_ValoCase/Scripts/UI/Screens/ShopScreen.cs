using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Progression;
using ValoCase.UI;   // CaseListItemView — reuse its procedural chain/padlock sprites

namespace ValoCase.UI.Screens
{
    public sealed class ShopScreen : UIScreenBase
    {
        // ── Inspector wiring (kept for builder compatibility) ──────────────────
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;
        [SerializeField] Transform featuredRoot;          // hidden at runtime
        [SerializeField] Transform dealsRoot;             // hidden at runtime
        [SerializeField] CaseListItemView caseItemPrefab; // unused in new flow
        [SerializeField] TextMeshProUGUI rotationTimerLabel;

        readonly List<GameObject> _cards = new();
        // Visible case cards paired with their definition, so lock overlays can be
        // re-evaluated per card whenever PlayerProgression changes (no grid rebuild).
        readonly List<(GameObject card, CaseDefinitionSO caseDef)> _lockCards = new();
        readonly List<GridLayoutGroup> _groupGrids = new();
        bool _builtOnce;
        bool _gridSized;          // true once the grid has real cell sizes applied
        bool _dealsSectionHidden;

        RectTransform _viewportRt;
        RectTransform _contentRt;
        Vector2 _lastViewportSize;

        GameObject _placeholder;
        TextMeshProUGUI _placeholderLabel;

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color GreenBadge = new Color(0.09f,  0.55f,  0.26f,  1f);
        static readonly Color TextBright = new Color(0.925f, 0.910f, 0.882f, 1f);

        // ── Grid constants (compact, multi-column boxed cards) ─────────────────
        const int   DefaultColumns = 2;     // phones (portrait)
        const int   MaxColumns     = 4;     // wider screens / editor
        const float TargetCellW    = 200f;  // placeholder card width before the grid is sized
        const float MaxCellW       = 224f;  // cards stay compact instead of stretching wide
        const float CellAspect     = 1.45f; // cellHeight = cellWidth × CellAspect (tall card)
        const float SidePad        = 4f;    // grid left & right padding
        const float Gap            = 4f;    // spacing between cells
        const float MinCardW       = 64f;   // mobile-safe floor before dropping below 4 columns

        static readonly Color Accent = new Color(1f, 0.275f, 0.333f, 1f);
        static readonly string[] PreferredGroupOrder = { "Classic", "Ghost", "Bulldog", "Vandal", "Melee" };

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            // Back button is no longer needed on the Shop/Cases screen.
            // Hide it immediately so it never appears above the BottomNavBar.
            if (backButton != null)
            {
                backButton.gameObject.SetActive(false);
                Log("[SHOP] Old bottom BACK button hidden");
            }
        }

        protected override void OnShown()
        {
            Refresh();
            GameEvents.OnShopRotated    += Refresh;
            PlayerProgression.OnChanged += Refresh;   // re-evaluate lock overlays on level change
        }

        protected override void OnHidden()
        {
            GameEvents.OnShopRotated    -= Refresh;
            PlayerProgression.OnChanged -= Refresh;
        }

        // ── Core refresh ──────────────────────────────────────────────────────

        void Refresh()
        {
            Log("[SHOP_DEBUG] Refresh called");
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Shop == null || ctx.CaseProgression == null)
            {
                ShowPlaceholder("Cases are loading...");
                return;
            }
            ctx.Shop.EnsureRotation();

            int caseCount = (ctx.Content?.Cases?.Count(c => c != null)) ?? 0;
            if (caseCount == 0)
            {
                ShowPlaceholder("No cases available.");
                return;
            }
            HidePlaceholder();

            // _builtOnce is only set once the grid is built with cases present, so an
            // empty first pass never latches the screen into a permanent blank state.
            if (!_builtOnce)
            {
                HideLegacyBuilderUI();
                BuildGrid(ctx);
                _builtOnce = true;
            }
            HideDailyDealsSection();

            // On the first build the cards have no real size yet (SizeGridToViewport
            // runs next frame and calls this itself once cells are measured). On every
            // later refresh — e.g. PlayerProgression.OnChanged after a case opening —
            // the grid is already sized, so re-evaluate locks live here.
            if (_gridSized)
                RefreshLockStates();
        }

        void ShowPlaceholder(string message)
        {
            if (_placeholder == null)
            {
                FullscreenBackground.AttachShared(gameObject);
                _placeholder = Child(transform, "ShopPlaceholder");
                StretchFill(_placeholder.GetComponent<RectTransform>());
                _placeholderLabel = _placeholder.AddComponent<TextMeshProUGUI>();
                _placeholderLabel.fontSize       = 26f;
                _placeholderLabel.fontStyle      = FontStyles.Bold;
                _placeholderLabel.alignment      = TextAlignmentOptions.Center;
                _placeholderLabel.color          = TextBright;
                _placeholderLabel.raycastTarget  = false;
            }
            _placeholderLabel.text = message;
            _placeholder.SetActive(true);
            _placeholder.transform.SetAsLastSibling();
        }

        void HidePlaceholder()
        {
            if (_placeholder != null && _placeholder.activeSelf)
                _placeholder.SetActive(false);
        }

        // Recomputes cell size when the viewport changes (orientation / safe-area).
        void Update()
        {
            if (!_gridSized || _viewportRt == null) return;
            var size = _viewportRt.rect.size;
            if (Mathf.Abs(size.x - _lastViewportSize.x) < 1f &&
                Mathf.Abs(size.y - _lastViewportSize.y) < 1f) return;

            ApplyGridSizing();
            RebuildLockOverlays();
        }

        // ── Legacy UI teardown ────────────────────────────────────────────────

        // Hides the old horizontal Featured scroll and any sibling labels.
        void HideLegacyBuilderUI()
        {
            // featuredRoot = Content inside Featured/Viewport/Content
            // parent.parent  = the "Featured" ScrollView root
            if (featuredRoot != null)
            {
                var root = featuredRoot.parent?.parent;
                if (root != null && root.gameObject.activeSelf)
                    root.gameObject.SetActive(false);
            }
            Deactivate("FeaturedLabel");
            Deactivate("Title");
        }

        // Hides every Daily Deals element the builder could have created. Idempotent.
        void HideDailyDealsSection()
        {
            Log("[SHOP_DEBUG] HideDailyDealsSection called");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (var i = 0; i < transform.childCount; i++)
            {
                var ch = transform.GetChild(i);
                Log("[SHOP_DEBUG] child=" + ch.name + " active=" + ch.gameObject.activeSelf);
            }
#endif

            if (dealsRoot != null)
            {
                // dealsRoot = Content; .parent = Viewport; .parent.parent = scroll root
                var scrollRoot = dealsRoot.parent?.parent;
                if (scrollRoot != null)
                {
                    Log("[SHOP_DEBUG] hiding daily deals object=" +scrollRoot.name);
                    scrollRoot.gameObject.SetActive(false);
                }
                // Hide Viewport too in case hierarchy depth differs
                if (dealsRoot.parent != null)
                {
                    Log("[SHOP_DEBUG] hiding daily deals object=" +dealsRoot.parent.name);
                    dealsRoot.parent.gameObject.SetActive(false);
                }
                Log("[SHOP_DEBUG] hiding daily deals object=" +dealsRoot.name);
                dealsRoot.gameObject.SetActive(false);
            }
            else
            {
                Log("[SHOP_DEBUG] dealsRoot is null — searching by name");
            }

            // Cover every name the builder might use for the Deals section
            foreach (var n in new[] { "Deals", "DailyDeals", "Daily Deals",
                                      "DealsLabel", "RotationTimer", "DailyDealsLabel" })
            {
                var t = transform.Find(n);
                if (t != null)
                    Log("[SHOP_DEBUG] hiding daily deals object=" +t.name);
                Deactivate(n);
            }

            if (rotationTimerLabel != null)
            {
                Log("[SHOP_DEBUG] hiding daily deals object=rotationTimerLabel");
                rotationTimerLabel.gameObject.SetActive(false);
            }

            if (!_dealsSectionHidden)
            {
                _dealsSectionHidden = true;
                Log("[SHOP] Daily Deals hidden");
            }
        }

        void Deactivate(string childName)
        {
            var t = transform.Find(childName);
            if (t != null && t.gameObject.activeSelf)
                t.gameObject.SetActive(false);
        }

        // ── Grouped, compact case grid ─────────────────────────────────────────
        // Cases are split into weapon/category sections (inferred from their drop
        // pool); each section is a header + its own responsive GridLayoutGroup, all
        // stacked in a VerticalLayoutGroup. Column count and compact cell size are
        // decided in SizeGridToViewport once the real Viewport rect is known.

        void BuildGrid(GameContext ctx)
        {
            FullscreenBackground.AttachShared(gameObject);

            var allCases = ctx.Content?.Cases;
            var cases = (allCases ?? Enumerable.Empty<CaseDefinitionSO>())
                .Where(c => c != null).ToList();
            Log("[SHOP_GRID] total cases from content=" + cases.Count);

            // ── ScrollRect ────────────────────────────────────────────────────
            var scrollGo = new GameObject("CaseGrid_Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            // Navbar space is reserved by the shared Screens host; these are content margins.
            scrollRt.offsetMin = new Vector2(16f,  16f);
            scrollRt.offsetMax = new Vector2(-16f, -16f);

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.scrollSensitivity = 30f;
            sr.movementType      = ScrollRect.MovementType.Clamped;

            // ── Viewport ──────────────────────────────────────────────────────
            var vpGo = new GameObject("Viewport", typeof(RectTransform));
            vpGo.transform.SetParent(scrollGo.transform, false);
            vpGo.AddComponent<RectMask2D>();
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            sr.viewport = vpRt;

            // ── Content (vertical stack of sections) ──────────────────────────
            var ctGo = new GameObject("Content", typeof(RectTransform));
            ctGo.transform.SetParent(vpGo.transform, false);
            var ctRt = ctGo.GetComponent<RectTransform>();
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(1f, 1f);
            ctRt.pivot     = new Vector2(0.5f, 1f);
            ctRt.offsetMin = Vector2.zero;
            ctRt.offsetMax = Vector2.zero;
            sr.content = ctRt;

            var vlg = ctGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding               = new RectOffset(0, 0, 8, 18);
            vlg.spacing               = 8f;
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = ctGo.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── Sections ──────────────────────────────────────────────────────
            _groupGrids.Clear();
            _lockCards.Clear();
            foreach (var (header, groupCases) in GroupCasesByWeapon(cases))
            {
                BuildSectionHeader(ctGo.transform, header);

                var gridGo = Child(ctGo.transform, "Grid_" + header.Replace(' ', '_'));
                var grid = gridGo.AddComponent<GridLayoutGroup>();
                grid.padding         = new RectOffset((int)SidePad, (int)SidePad, 4, 6);
                grid.cellSize        = new Vector2(TargetCellW, TargetCellW * CellAspect); // placeholder
                grid.spacing         = new Vector2(Gap, Gap);
                grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis       = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment  = TextAnchor.UpperCenter;
                grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = DefaultColumns;

                foreach (var caseDef in groupCases.OrderBy(c => c.VpPrice))
                    BuildCard(gridGo.transform, caseDef);

                _groupGrids.Add(grid);
            }

            _viewportRt = vpRt;
            _contentRt  = ctRt;
            StartCoroutine(SizeGridToViewport());
        }

        // Splits cases into ordered weapon sections. The category source is shared with
        // the lock logic (PlayerProgression.TryResolveCategory) so a card's section and its
        // lock requirement can never come from two different classifiers.
        List<(string header, List<CaseDefinitionSO> cases)> GroupCasesByWeapon(List<CaseDefinitionSO> cases)
        {
            var byGroup = new Dictionary<string, List<CaseDefinitionSO>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cases)
            {
                var key = PlayerProgression.TryResolveCategory(c.CaseId) ?? "Other";
                if (!byGroup.TryGetValue(key, out var list))
                    byGroup[key] = list = new List<CaseDefinitionSO>();
                list.Add(c);
            }

            var keys = byGroup.Keys.ToList();
            int Rank(string k)
            {
                if (string.Equals(k, "Other", StringComparison.OrdinalIgnoreCase)) return 1000;
                int p = Array.FindIndex(PreferredGroupOrder, x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
                return p >= 0 ? p : 100;
            }
            keys.Sort((a, b) =>
            {
                int ra = Rank(a), rb = Rank(b);
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            var ordered = keys
                .Select(k => (header: string.Equals(k, "Other", StringComparison.OrdinalIgnoreCase) ? "Other Cases" : k + " Cases", cases: byGroup[k]))
                .ToList();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[SHOP_SECTION_ORDER] " + string.Join(" > ", ordered.Select(o => o.header)));
#endif
            return ordered;
        }

        void BuildSectionHeader(Transform parent, string title)
        {
            var h = Child(parent, "Header_" + title.Replace(' ', '_'));
            var le = h.AddComponent<LayoutElement>();
            le.preferredHeight = 34f;
            le.minHeight       = 34f;

            var line = Child(h.transform, "Divider");
            var lrt = line.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.06f, 0f);
            lrt.anchorMax = new Vector2(0.94f, 0f);
            lrt.pivot     = new Vector2(0.5f, 0f);
            lrt.offsetMin = new Vector2(0f, 2f);
            lrt.offsetMax = new Vector2(0f, 4f);
            var li = line.AddComponent<Image>();
            li.color         = new Color(Accent.r, Accent.g, Accent.b, 0.28f);
            li.raycastTarget = false;

            var lblGo = Child(h.transform, "Label");
            StretchFill(lblGo.GetComponent<RectTransform>());
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text               = title.ToUpperInvariant();
            tmp.fontSize           = 17f;
            tmp.fontStyle          = FontStyles.Bold;
            tmp.characterSpacing   = 4f;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.color              = TextBright;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.raycastTarget      = false;
        }

        // Decides one compact, capped cell size and column count from the real Viewport
        // rect, then applies it to every section grid. Card width is capped so cards stay
        // compact (centered) instead of stretching to fill wide screens.
        IEnumerator SizeGridToViewport()
        {
            yield return null;
            for (int i = 0; i < 8 && (_viewportRt == null || _viewportRt.rect.width < 1f); i++)
                yield return null;
            if (_viewportRt == null) yield break;

            ApplyGridSizing();

            // Cards now have their real size — build the chain/padlock overlays at the
            // correct diagonal length, and from here on let Refresh() keep them current.
            _gridSized = true;
            RefreshLockStates();
        }

        void ApplyGridSizing()
        {
            if (_viewportRt == null) return;
            float vpW = _viewportRt.rect.width;
            if (vpW < 1f) return;

            int cols = MaxColumns;
            while (cols > DefaultColumns && (vpW - SidePad * 2f - Gap * (cols - 1)) / cols < MinCardW)
                cols--;

            float available = Mathf.Max(40f, vpW - SidePad * 2f - Gap * (cols - 1));
            float cellW     = Mathf.Min(available / cols, MaxCellW);
            cellW = Mathf.Max(1f, cellW);
            float cellH     = cellW * CellAspect;

            int pad = Mathf.RoundToInt(Mathf.Max(SidePad, (vpW - cols * cellW - Gap * (cols - 1)) * 0.5f));

            foreach (var grid in _groupGrids)
            {
                if (grid == null) continue;
                grid.constraintCount = cols;
                grid.cellSize        = new Vector2(cellW, cellH);
                grid.padding         = new RectOffset(pad, pad, 4, 6);
            }
            Log("[SHOP_GRID] sized vpW=" + vpW + " cols=" + cols + " cellW=" + cellW + " cellH=" + cellH);

            if (_contentRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRt);

            _lastViewportSize = _viewportRt.rect.size;
        }

        // Destroys existing lock overlays so RefreshLockStates rebuilds them at the new
        // card size after a viewport change. Only locked cards pay for this.
        void RebuildLockOverlays()
        {
            foreach (var (card, _) in _lockCards)
            {
                if (card == null) continue;
                var ov = card.transform.Find("LockOverlay");
                if (ov != null) DestroyImmediate(ov.gameObject);
            }
            RefreshLockStates();
        }

        // ── Portrait case card ─────────────────────────────────────────────────
        //
        //  ┌──────────────┐  ← 3 px theme colour strip
        //  │   [  ICON  ] │  ← top ~58 % of card (preserveAspect sprite or placeholder)
        //  │  Case Name   │  ← next ~14 %  (bold, centre)
        //  │  [500 VP]    │  ← next ~15 %  (green badge)
        //  │  Sel%65|...  │  ← bottom ~13 % (tiny drop rates)
        //  └──────────────┘

        void BuildCard(Transform parent, CaseDefinitionSO caseDef)
        {
            Log("[SHOP_CARD_DEBUG] BuildCard started parent=" + (parent != null) + " caseDef=" + (caseDef != null));

            if (caseDef == null)
            {
                Debug.LogWarning("[SHOP_CARD] skipped null caseDef");
                return;
            }

            var iconNull      = caseDef.CaseIcon == null;
            var dropTableNull = caseDef.DropTable == null;
            var caseId        = caseDef.CaseId ?? "";
            var caseName      = !string.IsNullOrEmpty(caseDef.DisplayName)
                ? caseDef.DisplayName
                : (caseId.Length > 0 ? caseId.Replace('_', ' ') : "Unknown Case");

            Log("[SHOP_CARD] Build start case=" + caseName +
                " iconNull=" + iconNull + " dropTableNull=" + dropTableNull);

            try
            {
                // ── Card root ─────────────────────────────────────────────────
                var card = Child(parent, "Card_" + (caseId.Length > 0 ? caseId : "unknown"));
                // Transparent root image — keeps the button raycast surface; the
                // visible fill is the shared background02 cover image below.
                card.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

                // Card background — background02 as a cover image (aspect kept,
                // overflow clipped to the card). Texture is shared between cards.
                FullscreenBackground.Attach(card, ProjectPaths.CaseCardBackgroundPath);

                // Subtle dark scrim above the photo so name / price / drop-rate
                // text stays readable. Sits below all card content (sibling 1).
                {
                    var scrim = Child(card.transform, "BgScrim");
                    StretchFill(scrim.GetComponent<RectTransform>());
                    var sImg = scrim.AddComponent<Image>();
                    sImg.color         = new Color(0f, 0f, 0f, 0.35f);
                    sImg.raycastTarget = false;
                }

                var btn = card.AddComponent<Button>();
                var bc  = btn.colors;
                bc.highlightedColor = new Color(0.08f, 0.12f, 0.22f, 1f);
                bc.pressedColor     = new Color(0.06f, 0.10f, 0.18f, 1f);
                btn.colors = bc;
                Log("[SHOP_CARD] Button added case=" + caseName);

                // Wire click BEFORE child widgets — navigation works even if a widget throws.
                // Uses the same lock source as the overlay so visual and click never disagree.
                btn.onClick.AddListener(() =>
                {
                    Log("[SHOP_CARD] clicked case=" + caseName + " id=" + caseId);
                    if (!PlayerProgression.IsCaseUnlocked(caseId, caseDef.UnlockType, caseDef.UnlockRequirement))
                    {
                        int req = PlayerProgression.RequiredLevelForCase(caseId, caseDef.UnlockType, caseDef.UnlockRequirement);
                        GameEvents.RaiseToast(req > 0 ? $"Seviye {req}'te açılır" : "Kilitli");
                        return;
                    }
                    CaseOpeningScreen.PendingCaseId = caseId;
                    navigator?.Navigate(ScreenType.CaseOpening);
                });
                Log("[SHOP_CARD] Click bound case=" + caseName + " id=" + caseId);

                // ── Top theme strip (3 px) ────────────────────────────────────
                {
                    var g  = Child(card.transform, "TopBar");
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot     = new Vector2(0.5f, 1f);
                    rt.offsetMin = new Vector2(0f, -3f);
                    rt.offsetMax = Vector2.zero;
                    g.AddComponent<Image>().color = caseDef.ThemeColor;
                }

                // ── Icon / placeholder (top 58 %) ─────────────────────────────
                {
                    var g  = Child(card.transform, "IconArea");
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0f, 0.43f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.offsetMin = new Vector2(4f,   2f);
                    rt.offsetMax = new Vector2(-4f, -2f);

                    if (!iconNull)
                    {
                        var img = g.AddComponent<Image>();
                        img.sprite         = caseDef.CaseIcon;
                        img.preserveAspect = true;
                        img.color          = Color.white;
                    }
                    else
                    {
                        var pc = caseDef.ThemeColor;
                        pc.a = 0.30f;
                        g.AddComponent<Image>().color = pc;

                        // TMP on a separate child — cannot share a GameObject with Image
                        var qGo = Child(g.transform, "Placeholder");
                        StretchFill(qGo.GetComponent<RectTransform>());
                        var q = qGo.AddComponent<TextMeshProUGUI>();
                        q.text      = "?";
                        q.fontSize  = 20;
                        q.alignment = TextAlignmentOptions.Center;
                        q.color     = TextBright;
                    }
                    Log("[SHOP_CARD_DEBUG] icon image created");
                }

                // ── Case name (14 %) ──────────────────────────────────────────
                {
                    var g  = Child(card.transform, "Name");
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0f, 0.28f);
                    rt.anchorMax = new Vector2(1f, 0.42f);
                    rt.offsetMin = new Vector2(4f,  0f);
                    rt.offsetMax = new Vector2(-4f, 0f);
                    var tmp = g.AddComponent<TextMeshProUGUI>();
                    tmp.text               = caseName;
                    tmp.enableAutoSizing   = true;   // scales with card → readable on phone & tablet
                    tmp.fontSizeMin        = 10f;
                    tmp.fontSizeMax        = 18f;
                    tmp.fontStyle          = FontStyles.Bold;
                    tmp.alignment          = TextAlignmentOptions.Center;
                    tmp.color              = TextBright;
                    tmp.enableWordWrapping = true;
                    tmp.overflowMode       = TextOverflowModes.Ellipsis;
                    Log("[SHOP_CARD_DEBUG] name text created");
                }

                // ── Price badge (15 %) ────────────────────────────────────────
                // ROOT CAUSE FIX: Image and TextMeshProUGUI both inherit from Graphic.
                // Adding a second Graphic-derived component to the same GameObject
                // makes Unity return null — causing the NullReferenceException on tmp.text.
                // Fix: Image stays on the badge container (g); TMP goes on a child (lblGo).
                {
                    var g  = Child(card.transform, "PriceBadge");
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.11f, 0.14f);  // ≈78 % width, ≈12 % height — not full card
                    rt.anchorMax = new Vector2(0.89f, 0.26f);
                    rt.offsetMin = new Vector2(0f,  2f);
                    rt.offsetMax = new Vector2(0f, -2f);
                    g.AddComponent<Image>().color = GreenBadge;   // badge background

                    var lblGo = Child(g.transform, "PriceLabel"); // separate child for text ← FIX
                    StretchFill(lblGo.GetComponent<RectTransform>());
                    var tmp = lblGo.AddComponent<TextMeshProUGUI>();
                    tmp.text               = caseDef.VpPrice.ToString("N0") + " VP";
                    tmp.enableAutoSizing   = true;   // fits the price bar at any card size
                    tmp.fontSizeMin        = 9f;
                    tmp.fontSizeMax        = 15f;
                    tmp.fontStyle          = FontStyles.Bold;
                    tmp.alignment          = TextAlignmentOptions.Center;
                    tmp.color              = Color.white;
                    tmp.enableWordWrapping = false;
                    Log("[SHOP_CARD_DEBUG] price text created");
                }

                // Drop-rate text intentionally omitted from the shop card for a cleaner
                // layout. BuildRateString is kept (not deleted) for potential reuse.

                // Lock overlay is applied/refreshed by RefreshLockStates() once the
                // card has its real size, and again on every PlayerProgression change.
                _cards.Add(card);
                _lockCards.Add((card, caseDef));
            }
            catch (Exception ex)
            {
                Debug.LogError("[SHOP_CARD_DEBUG] BuildCard failed for " + caseName + " error=" + ex);
            }
        }

        // Re-evaluates every visible card's lock state against the current player level.
        // Locked → ensure the crossed-chain/padlock overlay exists and is shown.
        // Unlocked → hide the overlay so the card looks and behaves like a normal card
        // (the card Button itself is always interactable; its click handler already
        // routes locked cases to the unlock toast and unlocked cases to opening).
        void RefreshLockStates()
        {
            int current = PlayerProgression.Level;
            foreach (var (card, caseDef) in _lockCards)
            {
                if (card == null || caseDef == null) continue;
                var  caseId   = caseDef.CaseId;
                bool locked   = !PlayerProgression.IsCaseUnlocked(caseId, caseDef.UnlockType, caseDef.UnlockRequirement);
                int  required = PlayerProgression.RequiredLevelForCase(caseId, caseDef.UnlockType, caseDef.UnlockRequirement);
                var  overlay  = card.transform.Find("LockOverlay");

                if (locked)
                {
                    if (overlay == null)
                    {
                        BuildLockOverlay(card.transform, required);
                        Log($"[SHOP_LOCK] overlay created case={caseId} required={required} current={current} screen=ShopScreen");
                    }
                    else if (!overlay.gameObject.activeSelf)
                    {
                        overlay.gameObject.SetActive(true);
                    }
                }
                else if (overlay != null && overlay.gameObject.activeSelf)
                {
                    overlay.gameObject.SetActive(false);
                    Log($"[SHOP_LOCK] overlay hidden (unlocked) case={caseId} required={required} current={current} screen=ShopScreen");
                }
            }
        }

        // Locked-card overlay: card stays visible but darkened, with two steel chains
        // crossed diagonally into an X, a golden padlock in the centre, and a small dark
        // rounded "LEVEL N" box directly above it. Reuses CaseListItemView's procedural
        // chain-link and padlock sprites so both code paths share one visual. The overlay
        // raycastTarget stays false so the card Button beneath still receives the tap and
        // shows the unlock toast. Does not alter card size, layout, sorting, or price.
        static void BuildLockOverlay(Transform card, int requiredLevel)
        {
            var cardRt = card.GetComponent<RectTransform>();
            float w = cardRt != null && cardRt.rect.width  > 1f ? cardRt.rect.width  : TargetCellW;
            float h = cardRt != null && cardRt.rect.height > 1f ? cardRt.rect.height : TargetCellW * CellAspect;
            float diag  = Mathf.Sqrt(w * w + h * h);
            float angle = Mathf.Atan2(h, w) * Mathf.Rad2Deg;

            var ov = Child(card, "LockOverlay");
            StretchFill(ov.GetComponent<RectTransform>());
            var ovImg = ov.AddComponent<Image>();
            ovImg.color         = new Color(0.02f, 0.03f, 0.06f, 0.55f);   // darken the card
            ovImg.raycastTarget = false;                                    // tap falls to card Button
            ov.transform.SetAsLastSibling();                                // above icon/title/price

            // Crossed chains forming the X.
            MakeChainBar(ov.transform,  angle, diag);
            MakeChainBar(ov.transform, -angle, diag);

            // Golden padlock in the centre.
            float lockSize = Mathf.Clamp(h * 0.30f, 30f, 54f);
            var lockGo = Child(ov.transform, "LockBadge");
            var lrt = lockGo.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot            = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(0f, -lockSize * 0.18f);
            lrt.sizeDelta        = new Vector2(lockSize * (40f / 48f), lockSize);
            var lImg = lockGo.AddComponent<Image>();
            lImg.sprite         = CaseListItemView.PadlockSprite();
            lImg.preserveAspect = true;
            lImg.raycastTarget  = false;

            // Dark rounded "LEVEL N" box directly above the padlock.
            var box = Child(ov.transform, "LockLevelBox");
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot            = new Vector2(0.5f, 0.5f);
            boxRt.anchoredPosition = new Vector2(0f, lockSize * 0.62f + 12f);
            boxRt.sizeDelta        = new Vector2(Mathf.Clamp(w * 0.62f, 56f, 96f), 22f);
            var boxImg = box.AddComponent<Image>();
            boxImg.sprite        = RoundedBoxSprite();
            boxImg.type          = Image.Type.Sliced;
            boxImg.color         = new Color(0.04f, 0.05f, 0.08f, 0.92f);
            boxImg.raycastTarget = false;

            var lbl = Child(box.transform, "LockLevelLabel");
            var lblRt = lbl.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(4f, 0f); lblRt.offsetMax = new Vector2(-4f, 0f);
            var tmp = lbl.AddComponent<TextMeshProUGUI>();
            tmp.text               = requiredLevel > 0 ? $"LEVEL {requiredLevel}" : "LOCKED";
            tmp.fontStyle          = FontStyles.Bold;
            tmp.enableAutoSizing   = true;
            tmp.fontSizeMin        = 9f;
            tmp.fontSizeMax        = 14f;
            tmp.characterSpacing   = 2f;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.color              = new Color(0.98f, 0.84f, 0.45f, 1f);   // golden
            tmp.enableWordWrapping = false;
            tmp.raycastTarget      = false;
        }

        static Sprite s_roundedBox;

        // 9-sliced rounded-rect sprite generated in code (replaces the unreliable built-in
        // UI/Skin/Background.psd) so the LEVEL box keeps rounded corners at any size.
        static Sprite RoundedBoxSprite()
        {
            if (s_roundedBox != null) return s_roundedBox;

            const int size = 24, r = 7;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px  = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float dx = (x + 0.5f) - cx, dy = (y + 0.5f) - cy;
                byte a = dx * dx + dy * dy <= r * r ? (byte)255 : (byte)0;
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();

            s_roundedBox = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return s_roundedBox;
        }

        // One diagonal chain: a rotated bar filled with overlapping oval steel links,
        // reusing the cached procedural link sprite shared with CaseListItemView.
        static void MakeChainBar(Transform parent, float angleDeg, float length)
        {
            const float thickness = 22f, pitch = 26f;

            var bar = Child(parent, "Chain");
            var rt  = bar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(length, thickness);
            rt.localEulerAngles = new Vector3(0f, 0f, angleDeg);

            // Drop one link from each end (top-right/bottom-right on this diagonal, and
            // their mirrors) and re-centre, so the chain terminates inside the card rect
            // instead of poking past the corners.
            int   n      = Mathf.Max(1, Mathf.CeilToInt(length / pitch) - 2);
            float startX = -(n - 1) * pitch * 0.5f;
            var   sprite = CaseListItemView.ChainLinkSprite();
            var   steel  = new Color(0.45f, 0.48f, 0.55f, 1f);   // darker, lower-contrast steel

            for (int i = 0; i < n; i++)
            {
                var lk  = new GameObject("Link", typeof(RectTransform), typeof(Image));
                lk.transform.SetParent(rt, false);
                var lrt = (RectTransform)lk.transform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot            = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(startX + i * pitch, 0f);
                lrt.sizeDelta        = new Vector2(pitch + 6f, thickness);
                var img = lk.GetComponent<Image>();
                img.sprite        = sprite;
                img.color         = steel;
                img.raycastTarget = false;
            }
        }

        // ── Static helpers ─────────────────────────────────────────────────────

        // Creates a plain RectTransform child.
        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        // Stretches a RectTransform to fill its parent.
        static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // Verbose shop diagnostics — compiled out of production builds.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        static void Log(string message) => Debug.Log(message);

        // Builds "Select %65 | Deluxe %15 | ..." from the case's RarityWeights.
        // Returns "Drop rates unavailable" when DropTable is null or empty.
        static string BuildRateString(CaseDefinitionSO caseDef)
        {
            if (caseDef == null || caseDef.DropTable == null)
                return "Drop rates unavailable";

            var sb = new StringBuilder();
            foreach (var w in caseDef.DropTable.RarityWeights)
            {
                if (w == null || w.weightPercent <= 0f) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(w.rarity).Append(" %").Append((int)w.weightPercent);
            }
            return sb.Length > 0 ? sb.ToString() : "Drop rates unavailable";
        }
    }
}
