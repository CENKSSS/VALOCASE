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
        static readonly Color TextDim    = new Color(1f,     1f,     1f,     0.38f);

        // ── Grid constants ────────────────────────────────────────────────────
        const int   DefaultColumns = 2;
        const float MinCellWidth   = 80f;   // fallback to 2 columns if narrower
        const float CellAspect     = 1.2f;  // height = width × CellAspect (2-column portrait)
        const float HPad           = 8f;    // grid left & right padding
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

        // ── 2-column grid ─────────────────────────────────────────────────────

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

            // Column count is fixed for portrait; the cell dimensions are applied
            // later by SizeGridToViewport() once the Viewport rect is valid (see
            // coroutine), so we never rely on the unreliable Awake-time selfRt.rect.
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
            grid.childAlignment  = TextAnchor.UpperCenter;
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

            // Size the grid from the REAL Viewport rect after layout settles next
            // frame — avoids the unreliable Awake-time rect read and the 1600f
            // fallback, and guarantees the grid fills the bar-to-nav viewport.
            StartCoroutine(SizeGridToViewport(grid, vpRt, ctRt, sortedCases.Count, cols));
        }

        // Computes cellSize from the actual Viewport rect one frame after the grid is
        // built, when the Canvas layout is valid. The viewport height is the true space
        // between the TopProfileBar and the BottomNavBar, so rows are scaled to fill it
        // instead of clustering at the top.
        IEnumerator SizeGridToViewport(GridLayoutGroup grid, RectTransform vpRt,
                                       RectTransform ctRt, int cardCount, int cols)
        {
            // Let the freshly-built hierarchy run at least one layout pass, then wait
            // until the Viewport actually has a size (guards a late layout frame).
            yield return null;
            for (int i = 0; i < 8 && (vpRt == null || vpRt.rect.width < 1f); i++)
                yield return null;
            if (grid == null || vpRt == null) yield break;

            float vpW = vpRt.rect.width;
            float vpH = vpRt.rect.height;

            // 2-column width straight from the viewport — no selfRt / fallback math.
            float cellW = (vpW - HPad * 2f - Gap * (cols - 1)) / cols;

            // Scale cell height so the rows fill the viewport vertically. Clamped to a
            // sane aspect range so a single short row can't produce a giant card.
            int   rows   = Mathf.Max(1, Mathf.CeilToInt(cardCount / (float)cols));
            float availH = vpH - 20f - Gap * Mathf.Max(0, rows - 1); // 20 = grid top+bottom padding
            float cellH  = Mathf.Clamp(availH / rows, cellW * 0.9f, cellW * 1.8f);

            grid.cellSize = new Vector2(cellW, cellH);
            Debug.Log("[SHOP_GRID] sized grid vpW=" + vpW + " vpH=" + vpH +
                      " cols=" + cols + " rows=" + rows + " cellW=" + cellW + " cellH=" + cellH);

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
                    tmp.fontSize           = 9.5f;
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
                    rt.anchorMin = new Vector2(0.08f, 0.13f);
                    rt.anchorMax = new Vector2(0.92f, 0.28f);
                    rt.offsetMin = new Vector2(0f,  2f);
                    rt.offsetMax = new Vector2(0f, -2f);
                    g.AddComponent<Image>().color = GreenBadge;   // badge background

                    var lblGo = Child(g.transform, "PriceLabel"); // separate child for text ← FIX
                    StretchFill(lblGo.GetComponent<RectTransform>());
                    var tmp = lblGo.AddComponent<TextMeshProUGUI>();
                    tmp.text               = caseDef.VpPrice.ToString("N0") + " VP";
                    tmp.fontSize           = 9.5f;
                    tmp.fontStyle          = FontStyles.Bold;
                    tmp.alignment          = TextAlignmentOptions.Center;
                    tmp.color              = Color.white;
                    tmp.enableWordWrapping = false;
                    Debug.Log("[SHOP_CARD_DEBUG] price text created");
                }

                // ── Drop rates (bottom 13 %) ──────────────────────────────────
                {
                    var g  = Child(card.transform, "Rates");
                    var rt = g.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(1f, 0.13f);
                    rt.offsetMin = new Vector2(3f,  1f);
                    rt.offsetMax = new Vector2(-3f, -1f);
                    var tmp = g.AddComponent<TextMeshProUGUI>();
                    tmp.text               = BuildRateString(caseDef); // always returns a string
                    tmp.fontSize           = 6.5f;
                    tmp.alignment          = TextAlignmentOptions.Center;
                    tmp.color              = TextDim;
                    tmp.enableWordWrapping = true;
                    tmp.overflowMode       = TextOverflowModes.Ellipsis;
                    Debug.Log("[SHOP_CARD_DEBUG] drop text created");
                }

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
