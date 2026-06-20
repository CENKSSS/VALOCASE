using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// Persistent bottom navigation bar — Valorant dark tactical style.
    ///
    /// Setup in Unity Editor:
    ///   1. Create an empty child GameObject inside your main Canvas.
    ///   2. Place it as the LAST sibling so it renders above all screens.
    ///   3. Add this component and assign the UINavigator reference.
    ///
    /// No screen logic, no inventory/upgrade/battle systems are touched.
    /// Navigation is delegated entirely to UINavigator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BottomNavBar : MonoBehaviour
    {
        [SerializeField] UINavigator navigator;

        // ── Palette (Valorant: dark navy + neon red) ──────────────────────────
        static readonly Color NavBg      = new Color(0.031f, 0.055f, 0.102f, 1f);    // #080E1A fully opaque
        static readonly Color BorderCol  = new Color(1f, 0.122f, 0.224f, 0.22f);     // #FF1F3A 22%
        static readonly Color ActiveRed  = new Color(1f, 0.122f, 0.224f, 1f);        // #FF1F3A
        static readonly Color ActiveTint = new Color(1f, 0.122f, 0.224f, 0.11f);     // active bg
        static readonly Color DimWhite   = new Color(1f, 1f, 1f, 0.38f);             // inactive

        // ── Tab definitions ───────────────────────────────────────────────────
        enum TabIcon { Cases, Grid, Bolt, Swords, Tools, Market }

        static readonly (ScreenType Screen, string Label, TabIcon Icon)[] Tabs =
        {
            (ScreenType.Shop,        "CASES",     TabIcon.Cases),   // CASES → Shop (ana menü SHOP davranışı)
            (ScreenType.Tools,       "TOOLS",     TabIcon.Tools),
            (ScreenType.Inventory,   "INVENTORY", TabIcon.Grid),
            (ScreenType.Upgrade,     "UPGRADE",   TabIcon.Bolt),
            (ScreenType.CaseBattleLobby, "BATTLE", TabIcon.Swords),  // entry point → Case Battle Lobby flow
            (ScreenType.Market,      "MARKET",    TabIcon.Market),
        };

        // ── Per-tab visual refs ───────────────────────────────────────────────
        struct TabUI
        {
            public Image            Bg;
            public Image            TopBar;
            public RectTransform    IconRoot;
            public TextMeshProUGUI  Label;
            public GameObject       Dot;
        }

        TabUI[]    _tabUIs;
        ScreenType _active = (ScreenType)(-1);   // nothing selected initially

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            Debug.Log("[BOTTOM_NAV] Awake active=" + gameObject.activeInHierarchy);
            BuildUI();
            var rt = (RectTransform)transform;
            Debug.Log("[BOTTOM_NAV] anchorMin=" + rt.anchorMin + " anchorMax=" + rt.anchorMax
                + " pivot=" + rt.pivot + " size=" + rt.sizeDelta + " pos=" + rt.anchoredPosition);
            Debug.Log("[BOTTOM_NAV] Built childCount=" + transform.childCount);
            Debug.Log("[BOTTOM_NAV] siblingIndex=" + transform.GetSiblingIndex()
                + " siblingCount=" + (transform.parent != null ? transform.parent.childCount : -1));
        }

        void OnEnable()
        {
            Debug.Log("[BOTTOM_NAV] OnEnable parent=" + transform.parent?.name);
            Debug.Log("[BOTTOM_NAV] Canvas=" + GetComponentInParent<Canvas>()?.name);
        }

        void Start()
        {
            Debug.Log("[BOTTOM_NAV] Start");

            // Fallback: if navigator was not wired by the builder, find it at runtime
            if (navigator == null)
                navigator = GetComponentInParent<UINavigator>()
                         ?? Object.FindFirstObjectByType<UINavigator>();

            Debug.Log("[BOTTOM_NAV] navigator=" + (navigator != null ? navigator.name : "NULL"));
            if (navigator == null) return;
            // Sync to whatever the navigator already navigated to in its Awake
            _active = navigator.CurrentScreen;
            navigator.OnNavigated += HandleNavigated;
            Repaint();
        }

        void OnDestroy()
        {
            if (navigator != null) navigator.OnNavigated -= HandleNavigated;
        }

        void HandleNavigated(ScreenType t)
        {
            bool ours = false;
            foreach (var tab in Tabs)
                if (tab.Screen == t) { ours = true; break; }
            _active = ours ? t : (ScreenType)(-1);
            Repaint();
        }

        /// <summary>Fixed nav height — shared content root insets by this. Single source of truth.</summary>
        public const float Height = 162f;

        // ── Build ─────────────────────────────────────────────────────────────
        void BuildUI()
        {
            // ── Outer rect: anchored to screen bottom, full width ──────────────
            var rt = (RectTransform)transform;
            rt.anchorMin        = Vector2.zero;
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;      // flush with SafeArea bottom
            rt.sizeDelta        = new Vector2(0f, Height);

            // ── Dark panel (inset left/right, covers full container height)
            var panel = NewRT("NavPanel", rt, Vector2.zero, Vector2.one);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = NavBg;

            var panelOl = panel.gameObject.AddComponent<Outline>();
            panelOl.effectColor    = BorderCol;
            panelOl.effectDistance = new Vector2(1f, -1f);

            // ── Thin red top border on panel ──────────────────────────────────
            var topBorderRt = NewRT("TopBorder", panel,
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            topBorderRt.pivot            = new Vector2(0.5f, 1f);
            topBorderRt.anchoredPosition = Vector2.zero;
            topBorderRt.sizeDelta        = new Vector2(0f, 2.5f);
            var topBorderImg = topBorderRt.gameObject.AddComponent<Image>();
            topBorderImg.color        = BorderCol;
            topBorderImg.raycastTarget = false;

            // ── Corner accent marks (tactical look) ───────────────────────────
            AddCornerMark(panel, true,  true);   // top-left
            AddCornerMark(panel, true,  false);  // top-right
            AddCornerMark(panel, false, true);   // bottom-left
            AddCornerMark(panel, false, false);  // bottom-right

            // ── Tab row ───────────────────────────────────────────────────────
            var row = NewRT("TabRow", panel, Vector2.zero, Vector2.one);
            row.offsetMin = row.offsetMax = Vector2.zero;
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment         = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(2, 2, 0, 0);
            hlg.spacing = 0f;

            _tabUIs = new TabUI[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++)
                _tabUIs[i] = BuildTab(row, i);
        }

        // Small L-shaped corner marks for tactical feel
        static void AddCornerMark(RectTransform parent, bool top, bool left)
        {
            float xAnchor = left ? 0f : 1f;
            float yAnchor = top  ? 1f : 0f;
            float xPivot  = left ? 0f : 1f;
            float yPivot  = top  ? 1f : 0f;
            float xOff    = left ?  5f : -5f;
            float yOff    = top  ? -5f :  5f;

            // Horizontal arm
            var h = NewRT("CH", parent,
                new Vector2(xAnchor, yAnchor), new Vector2(xAnchor, yAnchor));
            h.pivot            = new Vector2(xPivot, yPivot);
            h.anchoredPosition = new Vector2(xOff, yOff);
            h.sizeDelta        = new Vector2(14f, 1.7f);
            var hImg = h.gameObject.AddComponent<Image>();
            hImg.color = ActiveRed;
            hImg.raycastTarget = false;

            // Vertical arm
            var v = NewRT("CV", parent,
                new Vector2(xAnchor, yAnchor), new Vector2(xAnchor, yAnchor));
            v.pivot            = new Vector2(xPivot, yPivot);
            v.anchoredPosition = new Vector2(xOff, yOff);
            v.sizeDelta        = new Vector2(1.7f, 14f);
            var vImg = v.gameObject.AddComponent<Image>();
            vImg.color = ActiveRed;
            vImg.raycastTarget = false;
        }

        // ── Single tab ────────────────────────────────────────────────────────
        TabUI BuildTab(RectTransform parent, int idx)
        {
            var (_, label, ico) = Tabs[idx];
            int captured = idx;
            var ui = new TabUI();

            // Root
            var go = new GameObject("Tab_" + label,
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var tabRt = (RectTransform)go.transform;

            ui.Bg       = go.GetComponent<Image>();
            ui.Bg.color = Color.clear;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => TabClicked(captured));

            // Active top bar (2 px, anchored to tab top)
            var barRt = NewRT("Bar", tabRt,
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            barRt.pivot            = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = Vector2.zero;
            barRt.sizeDelta        = new Vector2(0f, 3.4f);
            ui.TopBar              = barRt.gameObject.AddComponent<Image>();
            ui.TopBar.color        = Color.clear;
            ui.TopBar.raycastTarget = false;

            // Vertical column (icon + label, centered)
            var col = NewRT("Col", tabRt, Vector2.zero, Vector2.one);
            col.offsetMin = col.offsetMax = Vector2.zero;
            var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment         = TextAnchor.MiddleCenter;
            vlg.spacing                = 9f;
            vlg.childForceExpandWidth  = false;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 12, 16);

            // Icon root (40×40 reserved; drawn graphics scaled ~2.0× ≈ 1.7× larger)
            var iconGo = new GameObject("Ico", typeof(RectTransform));
            iconGo.transform.SetParent(col, false);
            ui.IconRoot = (RectTransform)iconGo.transform;
            var iLE = iconGo.AddComponent<LayoutElement>();
            iLE.minWidth = iLE.preferredWidth   = 40f;
            iLE.minHeight = iLE.preferredHeight = 40f;
            ui.IconRoot.localScale = new Vector3(2.04f, 2.04f, 1f);
            DrawIcon(ui.IconRoot, ico, DimWhite);

            // Label
            var lblGo = new GameObject("Lbl",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(col, false);
            var lLE = lblGo.AddComponent<LayoutElement>();
            lLE.minHeight = lLE.preferredHeight = 24f;
            ui.Label                  = lblGo.GetComponent<TextMeshProUGUI>();
            ui.Label.text             = label;
            ui.Label.fontStyle        = FontStyles.Bold;
            ui.Label.alignment        = TextAlignmentOptions.Center;
            ui.Label.color            = DimWhite;
            ui.Label.characterSpacing = 1f;
            ui.Label.raycastTarget    = false;
            ui.Label.enableAutoSizing = true;   // grow short labels, keep INVENTORY clean in the fixed tab width
            ui.Label.fontSizeMin      = 11f;
            ui.Label.fontSizeMax      = 17f;

            // Glow dot (active only, bottom-centre of tab)
            var dotGo = new GameObject("Dot",
                typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(tabRt, false);
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.anchorMin        = new Vector2(0.5f, 0f);
            dotRt.anchorMax        = new Vector2(0.5f, 0f);
            dotRt.pivot            = new Vector2(0.5f, 0f);
            dotRt.anchoredPosition = new Vector2(0f, 7f);
            dotRt.sizeDelta        = new Vector2(7f, 7f);
            var dotImg = dotGo.GetComponent<Image>();
            dotImg.color        = ActiveRed;
            dotImg.raycastTarget = false;
            var dotOl = dotGo.AddComponent<Outline>();
            dotOl.effectColor    = new Color(1f, 0.122f, 0.224f, 0.6f);
            dotOl.effectDistance = new Vector2(5f, -5f);
            ui.Dot = dotGo;

            return ui;
        }

        // ── Icon drawing (purely procedural, no assets needed) ────────────────
        static void DrawIcon(RectTransform root, TabIcon ico, Color col)
        {
            switch (ico)
            {
                case TabIcon.Cases:  DrawCases(root, col);  break;
                case TabIcon.Grid:   DrawGrid(root, col);   break;
                case TabIcon.Bolt:   DrawBolt(root, col);   break;
                case TabIcon.Swords: DrawSwords(root, col); break;
                case TabIcon.Tools:  DrawTools(root, col);  break;
                case TabIcon.Market: DrawMarket(root, col); break;
            }
        }

        // CASES — rotated diamond outline + horizontal midline
        static void DrawCases(RectTransform r, Color col)
        {
            var d = Rect(r, new Vector2(13f, 13f), new Vector2(0f, 1f));
            d.localRotation = Quaternion.Euler(0f, 0f, 45f);
            d.GetComponent<Image>().color = Color.clear;
            var ol = d.gameObject.AddComponent<Outline>();
            ol.effectColor    = col;
            ol.effectDistance = new Vector2(1.2f, -1.2f);

            Rect(r, new Vector2(11f, 1.5f), new Vector2(0f, 1f))
                .GetComponent<Image>().color = col;
        }

        // INVENTORY — 2x2 grid of squares
        static void DrawGrid(RectTransform r, Color col)
        {
            const float s = 5.5f, g = 3.5f;
            Rect(r, new Vector2(s, s), new Vector2(-g,  g)).GetComponent<Image>().color = col;
            Rect(r, new Vector2(s, s), new Vector2( g,  g)).GetComponent<Image>().color = col;
            Rect(r, new Vector2(s, s), new Vector2(-g, -g)).GetComponent<Image>().color = col;
            Rect(r, new Vector2(s, s), new Vector2( g, -g)).GetComponent<Image>().color = col;
        }

        // UPGRADE — upward chevron (^)
        static void DrawBolt(RectTransform r, Color col)
        {
            var left = Rect(r, new Vector2(10f, 1.8f), new Vector2(-3.5f, 1f));
            left.localRotation = Quaternion.Euler(0f, 0f, 55f);
            left.GetComponent<Image>().color = col;

            var right = Rect(r, new Vector2(10f, 1.8f), new Vector2(3.5f, 1f));
            right.localRotation = Quaternion.Euler(0f, 0f, -55f);
            right.GetComponent<Image>().color = col;
        }

        // BATTLE — two crossed lines (X / swords)
        static void DrawSwords(RectTransform r, Color col)
        {
            var d1 = Rect(r, new Vector2(17f, 1.8f), Vector2.zero);
            d1.localRotation = Quaternion.Euler(0f, 0f, 45f);
            d1.GetComponent<Image>().color = col;

            var d2 = Rect(r, new Vector2(17f, 1.8f), Vector2.zero);
            d2.localRotation = Quaternion.Euler(0f, 0f, -45f);
            d2.GetComponent<Image>().color = col;

            // Small square tips at the four ends
            const float dist = 6f;
            Rect(r, new Vector2(2.5f, 2.5f), new Vector2(-dist,  dist)).With(t => { t.localRotation = Quaternion.Euler(0f, 0f, 45f); t.GetComponent<Image>().color = col; });
            Rect(r, new Vector2(2.5f, 2.5f), new Vector2( dist, -dist)).With(t => { t.localRotation = Quaternion.Euler(0f, 0f, 45f); t.GetComponent<Image>().color = col; });
            Rect(r, new Vector2(2.5f, 2.5f), new Vector2( dist,  dist)).With(t => { t.localRotation = Quaternion.Euler(0f, 0f, 45f); t.GetComponent<Image>().color = col; });
            Rect(r, new Vector2(2.5f, 2.5f), new Vector2(-dist, -dist)).With(t => { t.localRotation = Quaternion.Euler(0f, 0f, 45f); t.GetComponent<Image>().color = col; });
        }

        // TOOLS — wrench silhouette: diagonal handle + perpendicular head
        static void DrawTools(RectTransform r, Color col)
        {
            // Handle — diagonal bar
            var handle = Rect(r, new Vector2(14f, 1.8f), new Vector2(1f, -1f));
            handle.localRotation = Quaternion.Euler(0f, 0f, -45f);
            handle.GetComponent<Image>().color = col;

            // Head — short wide bar at top-left end of handle, perpendicular
            var head = Rect(r, new Vector2(7f, 1.8f), new Vector2(-4f, 5f));
            head.localRotation = Quaternion.Euler(0f, 0f, 45f);
            head.GetComponent<Image>().color = col;

            // Second jaw of wrench head
            var jaw = Rect(r, new Vector2(7f, 1.8f), new Vector2(-7f, 2f));
            jaw.localRotation = Quaternion.Euler(0f, 0f, 45f);
            jaw.GetComponent<Image>().color = col;
        }

        // MARKET — shopping cart: basket rect + two wheel dots + handle bar
        static void DrawMarket(RectTransform r, Color col)
        {
            // Basket body
            var basket = Rect(r, new Vector2(12f, 8f), new Vector2(1f, 2f));
            basket.GetComponent<Image>().color = col;

            // Handle — angled bar going up-left from basket
            var hBar = Rect(r, new Vector2(9f, 1.8f), new Vector2(-4f, 8f));
            hBar.localRotation = Quaternion.Euler(0f, 0f, -30f);
            hBar.GetComponent<Image>().color = col;

            // Left wheel
            Rect(r, new Vector2(3f, 3f), new Vector2(-2f, -5f))
                .GetComponent<Image>().color = col;

            // Right wheel
            Rect(r, new Vector2(3f, 3f), new Vector2(4f, -5f))
                .GetComponent<Image>().color = col;
        }

        // ── Tab state ─────────────────────────────────────────────────────────
        void TabClicked(int idx)
        {
            _active = Tabs[idx].Screen;
            if (idx == 0)
                Debug.Log("[BOTTOM_NAV] CASES clicked -> opening SHOP");
            if (Tabs[idx].Screen == ScreenType.CaseBattleLobby)
                Debug.Log($"[BottomNavBar] Battle tapped → requesting ScreenType.CaseBattleLobby");
            navigator?.Navigate(_active);
            Repaint();
        }

        void Repaint()
        {
            for (int i = 0; i < _tabUIs.Length; i++)
            {
                bool on = Tabs[i].Screen == _active;
                ref var ui = ref _tabUIs[i];

                ui.Bg.color     = on ? ActiveTint  : Color.clear;
                ui.TopBar.color = on ? ActiveRed   : Color.clear;
                ui.Label.color  = on ? Color.white : DimWhite;
                ui.Dot.SetActive(on);
                PaintIcon(ui.IconRoot, on ? ActiveRed : DimWhite);
            }
        }

        static void PaintIcon(RectTransform root, Color col)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                if (img.color.a > 0.01f)   // skip transparent placeholders (e.g. diamond fill)
                    img.color = col;
            foreach (var ol in root.GetComponentsInChildren<Outline>(true))
                ol.effectColor = col;
        }

        // ── Rect helpers ──────────────────────────────────────────────────────
        static RectTransform NewRT(string n, RectTransform p,
            Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(p, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Creates a centred RectTransform with an Image, no raycast target.</summary>
        static RectTransform Rect(RectTransform p, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("_", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(p, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = size;
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().raycastTarget = false;
            return rt;
        }
    }

    /// <summary>Inline fluent helper — keeps DrawSwords one-liners readable.</summary>
    internal static class RectTransformExt
    {
        public static RectTransform With(this RectTransform rt,
            System.Action<RectTransform> act) { act(rt); return rt; }
    }
}
