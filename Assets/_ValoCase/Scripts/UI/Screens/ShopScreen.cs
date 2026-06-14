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
        bool _builtOnce;
        bool _dealsSectionHidden;

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color GreenBadge = new Color(0.09f,  0.55f,  0.26f,  1f);
        static readonly Color TextBright = new Color(0.925f, 0.910f, 0.882f, 1f);

        // ── Grid constants ────────────────────────────────────────────────────
        const int   DefaultColumns = 2;     // phones (portrait)
        const int   WideColumns    = 3;     // tablets / iPad portrait
        const int   MaxColumns     = 4;     // iPad landscape / very wide screens
        const float WideMinViewportWidth     = 1080f; // ≥ this (canvas units) → 3 columns
        const float VeryWideMinViewportWidth = 1500f; // ≥ this (canvas units) → 4 columns
        const float CellAspect     = 1.3f;  // cellHeight = cellWidth × CellAspect (1.25–1.35 range)
        const float HPad           = 8f;    // grid left & right padding (tablet / iPad)
        const float PhoneHPad      = 20f;   // larger safe side padding on phones (2-col)
        const float Gap            = 8f;    // spacing between cells

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            // Back button is no longer needed on the Shop/Cases screen.
            // Hide it immediately so it never appears above the BottomNavBar.
            if (backButton != null)
            {
                backButton.gameObject.SetActive(false);
                Debug.Log("[SHOP] Old bottom BACK button hidden");
            }
        }

        protected override void OnShown()
        {
            Refresh();
            GameEvents.OnShopRotated += Refresh;
        }

        protected override void OnHidden() => GameEvents.OnShopRotated -= Refresh;

        // ── Core refresh ──────────────────────────────────────────────────────

        void Refresh()
        {
            Debug.Log("[SHOP_DEBUG] Refresh called");
            var ctx = GameContext.Instance;
            Debug.Log("[SHOP_DEBUG] ctx null=" + (ctx == null));
            Debug.Log("[SHOP_DEBUG] ctx.Content null=" + (ctx?.Content == null));
            Debug.Log("[SHOP_DEBUG] ctx.Content.Cases null=" + (ctx?.Content?.Cases == null));
            Debug.Log("[SHOP_DEBUG] ctx.Content.Cases count=" + (ctx?.Content?.Cases?.Count ?? -1));
            Debug.Log("[SHOP_DEBUG] ctx.Shop null=" + (ctx?.Shop == null));
            Debug.Log("[SHOP_DEBUG] ctx.Shop.AllPurchasableCases count=" +
                      (ctx?.Shop?.AllPurchasableCases == null ? -1
                       : ctx.Shop.AllPurchasableCases.Count()));
            if (ctx == null || ctx.Shop == null || ctx.CaseProgression == null) return;
            ctx.Shop.EnsureRotation();
            if (!_builtOnce)
            {
                HideLegacyBuilderUI();
                BuildGrid(ctx);
                _builtOnce = true;
            }
            HideDailyDealsSection();
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
            Debug.Log("[SHOP_DEBUG] HideDailyDealsSection called");

            // Enumerate ALL direct children so the full scene hierarchy is visible in logs
            for (var i = 0; i < transform.childCount; i++)
            {
                var ch = transform.GetChild(i);
                Debug.Log("[SHOP_DEBUG] child=" + ch.name + " active=" + ch.gameObject.activeSelf);
            }

            if (dealsRoot != null)
            {
                // dealsRoot = Content; .parent = Viewport; .parent.parent = scroll root
                var scrollRoot = dealsRoot.parent?.parent;
                if (scrollRoot != null)
                {
                    Debug.Log("[SHOP_DEBUG] hiding daily deals object=" + scrollRoot.name);
                    scrollRoot.gameObject.SetActive(false);
                }
                // Hide Viewport too in case hierarchy depth differs
                if (dealsRoot.parent != null)
                {
                    Debug.Log("[SHOP_DEBUG] hiding daily deals object=" + dealsRoot.parent.name);
                    dealsRoot.parent.gameObject.SetActive(false);
                }
                Debug.Log("[SHOP_DEBUG] hiding daily deals object=" + dealsRoot.name);
                dealsRoot.gameObject.SetActive(false);
            }
            else
            {
                Debug.Log("[SHOP_DEBUG] dealsRoot is null — searching by name");
            }

            // Cover every name the builder might use for the Deals section
            foreach (var n in new[] { "Deals", "DailyDeals", "Daily Deals",
                                      "DealsLabel", "RotationTimer", "DailyDealsLabel" })
            {
                var t = transform.Find(n);
                if (t != null)
                    Debug.Log("[SHOP_DEBUG] hiding daily deals object=" + t.name);
                Deactivate(n);
            }

            if (rotationTimerLabel != null)
            {
                Debug.Log("[SHOP_DEBUG] hiding daily deals object=rotationTimerLabel");
                rotationTimerLabel.gameObject.SetActive(false);
            }

            if (!_dealsSectionHidden)
            {
                _dealsSectionHidden = true;
                Debug.Log("[SHOP] Daily Deals hidden");
            }
        }

        void Deactivate(string childName)
        {
            var t = transform.Find(childName);
            if (t != null && t.gameObject.activeSelf)
                t.gameObject.SetActive(false);
        }

        // ── Responsive case grid (2 / 3 / 4 columns) ──────────────────────────

        void BuildGrid(GameContext ctx)
        {
            // Shared section background (cover image, aspect preserved)
            FullscreenBackground.AttachShared(gameObject);

            // Load cases first so row count is known before computing cellH.
            var allCases = ctx.Content?.Cases;
            Debug.Log("[SHOP_GRID] total cases from content=" + (allCases?.Count ?? 0));
            var sortedCases = (allCases ?? Enumerable.Empty<CaseDefinitionSO>())
                .Where(c => c != null)
                .OrderBy(c => c.VpPrice)
                .ToList();
            Debug.Log("[SHOP_GRID] vandal featured cases=" + sortedCases.Count +
                      " columns=" + DefaultColumns);

            // Column count and cell dimensions are both decided later by
            // SizeGridToViewport() from the live Viewport rect (see coroutine), so we
            // never rely on the unreliable Awake-time selfRt.rect. Start at the phone
            // default; the coroutine widens to 3 / 4 columns on tablets.
            int cols = DefaultColumns;

            // ── ScrollRect ────────────────────────────────────────────────────
            var scrollGo = new GameObject("CaseGrid_Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(16f,   112f);  // clear BottomNavBar
            scrollRt.offsetMax = new Vector2(-16f, -118f);  // clear TopProfileBar

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

            // ── Content ───────────────────────────────────────────────────────
            var ctGo = new GameObject("Content", typeof(RectTransform));
            ctGo.transform.SetParent(vpGo.transform, false);
            var ctRt = ctGo.GetComponent<RectTransform>();
            ctRt.anchorMin = new Vector2(0f, 1f);
            ctRt.anchorMax = new Vector2(1f, 1f);
            ctRt.pivot     = new Vector2(0.5f, 1f);
            ctRt.offsetMin = Vector2.zero;
            ctRt.offsetMax = Vector2.zero;
            sr.content = ctRt;

            // GridLayoutGroup — fixed column count, rows grow downward
            var grid = ctGo.AddComponent<GridLayoutGroup>();
            grid.padding         = new RectOffset((int)HPad, (int)HPad, 10, 10);
            grid.cellSize        = new Vector2(200f, 260f); // placeholder; SizeGridToViewport overwrites next frame
            grid.spacing         = new Vector2(Gap, Gap);
            grid.startCorner     = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis       = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment  = TextAnchor.UpperLeft;  // cards start top-left; partial last row fills from left
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;

            var csf = ctGo.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── Populate cards ─────────────────────────────────────────────────
            for (var i = 0; i < sortedCases.Count; i++)
            {
                var caseDef = sortedCases[i];
                Debug.Log("[SHOP_DEBUG] raw case index=" + i);
                Debug.Log("[SHOP_DEBUG] case null=" + (caseDef == null));
                if (caseDef != null)
                {
                    Debug.Log("[SHOP_DEBUG] case id="             + caseDef.CaseId);
                    Debug.Log("[SHOP_DEBUG] case display="        + caseDef.DisplayName);
                    Debug.Log("[SHOP_DEBUG] case price="          + caseDef.VpPrice);
                    Debug.Log("[SHOP_DEBUG] case icon null="      + (caseDef.CaseIcon == null));
                    Debug.Log("[SHOP_DEBUG] case dropTable null=" + (caseDef.DropTable == null));
                }
                Debug.Log("[SHOP_GRID] case raw name=" + (caseDef?.CaseId ?? "(null)") +
                          " display=" + (caseDef?.DisplayName ?? "(null)"));
                BuildCard(ctGo.transform, caseDef);
            }

            // Decide columns and cell size from the REAL Viewport rect after layout
            // settles next frame — avoids the unreliable Awake-time rect read.
            StartCoroutine(SizeGridToViewport(grid, vpRt, ctRt));
        }

        // Picks the column count and cell size from the actual Viewport rect one frame
        // after the grid is built, when the Canvas layout is valid. Card height is kept
        // proportional to card WIDTH (not the viewport), so cards never stretch to fill
        // the screen; ContentSizeFitter then derives the Content height from the row
        // count and the ScrollRect scrolls when needed.
        IEnumerator SizeGridToViewport(GridLayoutGroup grid, RectTransform vpRt,
                                       RectTransform ctRt)
        {
            // Let the freshly-built hierarchy run at least one layout pass, then wait
            // until the Viewport actually has a size (guards a late layout frame).
            yield return null;
            for (int i = 0; i < 8 && (vpRt == null || vpRt.rect.width < 1f); i++)
                yield return null;
            if (grid == null || vpRt == null) yield break;

            float vpW = vpRt.rect.width;
            float vpH = vpRt.rect.height;

            // Responsive column count from the actual device/game-view aspect ratio.
            // The ScrollRect viewport can be shorter than the full screen, so vpW / vpH
            // may falsely classify a tall iPhone as a tablet. Screen aspect is safer:
            // iPhone portrait ≈ 0.46 → 2 columns, iPad portrait ≈ 0.70 → 3 columns,
            // iPad landscape >= 1.0 → 4 columns when there is enough viewport width.
            float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 0f;
            int cols;
            if (screenAspect < 0.60f)                                                   cols = DefaultColumns; // phone portrait → 2
            else if (screenAspect >= 1.0f && vpW >= VeryWideMinViewportWidth)           cols = MaxColumns;     // landscape wide → 4
            else if (screenAspect >= 0.60f && vpW >= WideMinViewportWidth)              cols = WideColumns;    // tablet portrait → 3
            else                                                                        cols = DefaultColumns; // fallback → 2
            grid.constraintCount = cols;

            // Card width from the viewport width, side padding and gaps. Phones use a
            // larger safe side padding; tablets/iPad keep the tight padding.
            float sidePad   = cols == DefaultColumns ? PhoneHPad : HPad;
            float available = Mathf.Max(40f, vpW - sidePad * 2f - Gap * (cols - 1));
            float cellW     = available / cols;

            // Safety cap (phone): one column never exceeds ~46% of the viewport, so two
            // columns always fit with slack even if the measured width is slightly large.
            if (cols == DefaultColumns)
                cellW = Mathf.Min(cellW, vpW * 0.46f);
            cellW = Mathf.Max(1f, cellW);

            // Re-center: spread any leftover width as equal left/right padding, so the row
            // is guaranteed to sit inside the viewport (right column can never overflow).
            int pad = Mathf.RoundToInt(Mathf.Max(HPad, (vpW - cols * cellW - Gap * (cols - 1)) * 0.5f));
            grid.padding = new RectOffset(pad, pad, 10, 10);

            // Card height is proportional to its OWN width — natural portrait shape,
            // no artificial stretching to fill the viewport.
            float cellH = cellW * CellAspect;

            grid.cellSize = new Vector2(cellW, cellH);
            Debug.Log("[SHOP_GRID] sized grid vpW=" + vpW + " vpH=" + vpH +
                      " screenAspect=" + screenAspect +
                      " cols=" + cols + " cellW=" + cellW + " cellH=" + cellH);

            // Apply the new Content height immediately so ScrollRect bounds are correct.
            if (ctRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(ctRt);
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
            // ── Entry diagnostics ─────────────────────────────────────────────
            Debug.Log("[SHOP_CARD_DEBUG] BuildCard started");
            Debug.Log("[SHOP_CARD_DEBUG] parent null=" + (parent == null));
            Debug.Log("[SHOP_CARD_DEBUG] caseDef null=" + (caseDef == null));

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

            Debug.Log("[SHOP_CARD] Build start case=" + caseName +
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
                Debug.Log("[SHOP_CARD] Button added case=" + caseName);

                // Wire click BEFORE child widgets — navigation works even if a widget throws.
                btn.onClick.AddListener(() =>
                {
                    Debug.Log("[SHOP_CARD] clicked case=" + caseName + " id=" + caseId);
                    CaseOpeningScreen.PendingCaseId = caseId;
                    navigator?.Navigate(ScreenType.CaseOpening);
                });
                Debug.Log("[SHOP_CARD] Click bound case=" + caseName + " id=" + caseId);

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
                    rt.anchorMin = new Vector2(0f, 0.42f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.offsetMin = new Vector2(6f,   5f);
                    rt.offsetMax = new Vector2(-6f, -6f);

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
                        q.fontSize  = 26;
                        q.alignment = TextAlignmentOptions.Center;
                        q.color     = TextBright;
                    }
                    Debug.Log("[SHOP_CARD_DEBUG] icon image created");
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
                    tmp.fontSizeMin        = 12f;
                    tmp.fontSizeMax        = 38f;
                    tmp.fontStyle          = FontStyles.Bold;
                    tmp.alignment          = TextAlignmentOptions.Center;
                    tmp.color              = TextBright;
                    tmp.enableWordWrapping = true;
                    tmp.overflowMode       = TextOverflowModes.Ellipsis;
                    Debug.Log("[SHOP_CARD_DEBUG] name text created");
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
                    tmp.fontSizeMin        = 12f;
                    tmp.fontSizeMax        = 34f;
                    tmp.fontStyle          = FontStyles.Bold;
                    tmp.alignment          = TextAlignmentOptions.Center;
                    tmp.color              = Color.white;
                    tmp.enableWordWrapping = false;
                    Debug.Log("[SHOP_CARD_DEBUG] price text created");
                }

                // Drop-rate text intentionally omitted from the shop card for a cleaner
                // layout. BuildRateString is kept (not deleted) for potential reuse.

                _cards.Add(card);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SHOP_CARD_DEBUG] BuildCard failed for " + caseName + " error=" + ex);
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
