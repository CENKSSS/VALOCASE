using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Profile;
using ValoCase.Progression;

namespace ValoCase.UI
{
    /// <summary>
    /// Persistent global top profile bar — Valorant dark tactical style.
    /// Matches BottomNavBar's color palette and corner-mark design language.
    ///
    /// Shows on every screen: avatar, username, level, VP balance, gear button.
    /// Auto-instantiated by ValoCaseUIBuilder — no manual Inspector work needed.
    ///
    /// Gear button → UINavigator.Navigate(Settings).
    /// Avatar click → no action (settings only via gear button).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TopProfileBar : MonoBehaviour
    {
        [SerializeField] UINavigator navigator;

        // ── Palette — identical to BottomNavBar ───────────────────────────────
        static readonly Color NavBg      = new Color(0.031f, 0.055f, 0.102f, 1f);    // #080E1A opaque
        static readonly Color BorderCol  = new Color(1f, 0.122f, 0.224f, 0.22f);     // #FF1F3A 22%
        static readonly Color ActiveRed  = new Color(1f, 0.122f, 0.224f, 1f);        // #FF1F3A
        static readonly Color DimWhite   = new Color(1f, 1f, 1f, 0.38f);             // inactive
        static readonly Color TextBright = new Color(0.925f, 0.910f, 0.882f, 1f);    // #ECE8E1
        static readonly Color XpGreen    = new Color(0.290f, 0.831f, 0.392f, 1f);    // progress fill
        static readonly Color XpTrack    = new Color(1f, 1f, 1f, 0.12f);             // bar background

        // ── Runtime UI refs ───────────────────────────────────────────────────
        Image           _avatarImg;
        TextMeshProUGUI _usernameLabel;
        TextMeshProUGUI _vpLabel;
        TextMeshProUGUI _levelLabel;
        TextMeshProUGUI _xpLabel;
        RectTransform   _xpFill;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            Debug.Log("[TOP_BAR_DEBUG] Awake");
            BuildUI();
            Debug.Log("[TOP_BAR] Created");
        }

        void Start()
        {
            Debug.Log("[TOP_BAR_DEBUG] Start");
            Debug.Log("[TOP_BAR_DEBUG] username="   + PlayerProfileData.Username);
            Debug.Log("[TOP_BAR_DEBUG] avatarKey="  + PlayerProfileData.AvatarKey);
            Debug.Log("[TOP_BAR_DEBUG] avatar null=" + (PlayerProfileData.Avatar == null));
            var _ctxAtStart = GameContext.Instance;
            Debug.Log("[TOP_BAR_DEBUG] ctx null="        + (_ctxAtStart == null));
            Debug.Log("[TOP_BAR_DEBUG] vp service null=" + (_ctxAtStart?.Vp == null));
            Debug.Log("[TOP_BAR_DEBUG] balance="         + (_ctxAtStart?.Vp?.Balance ?? 0));

            // Fallback: wire navigator at runtime if builder didn't set it
            if (navigator == null)
                navigator = GetComponentInParent<UINavigator>()
                         ?? Object.FindFirstObjectByType<UINavigator>();

            SyncProfile();
            SyncVp();
            SyncProgression();
        }

        void OnEnable()
        {
            Debug.Log("[TOP_BAR_DEBUG] OnEnable");
            PlayerProfileData.OnProfileChanged += OnProfileChanged;
            GameEvents.OnVpChanged             += OnVpChanged;
            PlayerProgression.OnChanged        += SyncProgression;
        }

        void OnDisable()
        {
            PlayerProfileData.OnProfileChanged -= OnProfileChanged;
            GameEvents.OnVpChanged             -= OnVpChanged;
            PlayerProgression.OnChanged        -= SyncProgression;
        }

        // ── Event handlers ────────────────────────────────────────────────────
        void OnProfileChanged()
        {
            SyncProfile();
            Debug.Log("[TOP_BAR] Profile updated");
        }

        void OnVpChanged(int _, int current)
        {
            SyncVp();
            Debug.Log("[TOP_BAR] VP updated");
        }

        void OnGearClicked()
        {
            Debug.Log("[TOP_BAR] Settings clicked");
            navigator?.Navigate(ScreenType.Settings);
        }

        // ── Data sync ─────────────────────────────────────────────────────────
        void SyncProfile()
        {
            Debug.Log("[TOP_BAR_DEBUG] SyncProfile" +
                      " username="   + PlayerProfileData.Username +
                      " avatarKey="  + PlayerProfileData.AvatarKey +
                      " avatar null=" + (PlayerProfileData.Avatar == null));
            if (_avatarImg != null)
            {
                _avatarImg.color  = Color.white;
                _avatarImg.sprite = PlayerProfileData.Avatar;
            }
            if (_usernameLabel != null)
                _usernameLabel.text = PlayerProfileData.Username.ToUpper();
        }

        void SyncVp()
        {
            var ctx = GameContext.Instance;
            var bal = ctx?.Vp?.Balance ?? 0;
            Debug.Log("[TOP_BAR_DEBUG] SyncVp" +
                      " ctx null="        + (ctx == null) +
                      " vp service null=" + (ctx?.Vp == null) +
                      " balance="         + bal);
            if (_vpLabel == null) return;
            _vpLabel.text = FormatVp(bal);
        }

        // Updates the level label, XP text, and green bar fill from the cached
        // backend progression. Safe before any backend data arrives (defaults Lv. 1, 0/20).
        void SyncProgression()
        {
            int lvl = PlayerProgression.Level;
            int cur = PlayerProgression.CurrentLevelXp;
            int req = PlayerProgression.XpRequiredForNextLevel > 0
                ? PlayerProgression.XpRequiredForNextLevel
                : PlayerProgression.DefaultXpPerLevel;

            if (_levelLabel != null) _levelLabel.text = $"Lv. {lvl}";
            if (_xpLabel != null)    _xpLabel.text    = $"{cur}/{req}";
            if (_xpFill != null)     _xpFill.anchorMax = new Vector2(PlayerProgression.Fill01, 1f);
        }

        static string FormatVp(int amount)
        {
            if (amount >= 1_000_000) return (amount / 1_000_000f).ToString("0.##") + "M VP";
            if (amount >= 1_000)     return (amount / 1_000f).ToString("0.##") + "K VP";
            return amount + " VP";
        }

        /// <summary>Fixed bar height — shared content root insets by this. Single source of truth.</summary>
        public const float Height = 112f;

        // ── Build ─────────────────────────────────────────────────────────────
        void BuildUI()
        {
            // Outer rect — anchored to SafeArea TOP, full width
            var rt = (RectTransform)transform;
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(0f, Height);

            // ── Dark panel (full width, full height) ──────────────────────────
            var panel = NewRT("TopPanel", rt, Vector2.zero, Vector2.one);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = NavBg;

            var panelOl = panel.gameObject.AddComponent<Outline>();
            panelOl.effectColor    = BorderCol;
            panelOl.effectDistance = new Vector2(1f, -1f);

            // Bottom border line — mirrors BottomNavBar's top red line
            var botBorder = NewRT("BotBorder", panel,
                Vector2.zero, new Vector2(1f, 0f));
            botBorder.pivot            = new Vector2(0.5f, 0f);
            botBorder.anchoredPosition = Vector2.zero;
            botBorder.sizeDelta        = new Vector2(0f, 1.5f);
            botBorder.gameObject.AddComponent<Image>().color = BorderCol;
            botBorder.GetComponent<Image>().raycastTarget = false;

            // Tactical corner accent marks (same as BottomNavBar)
            AddCornerMark(panel, top: true,  left: true);
            AddCornerMark(panel, top: true,  left: false);
            AddCornerMark(panel, top: false, left: true);
            AddCornerMark(panel, top: false, left: false);

            // ── Circular avatar (44 px, masked) ───────────────────────────────
            var circleSpr = MakeCircleSprite(64);

            var maskGo = new GameObject("AvMask",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            maskGo.transform.SetParent(panel, false);
            var maskRt = (RectTransform)maskGo.transform;
            maskRt.anchorMin        = new Vector2(0f, 0.5f);
            maskRt.anchorMax        = new Vector2(0f, 0.5f);
            maskRt.pivot            = new Vector2(0f, 0.5f);
            maskRt.anchoredPosition = new Vector2(14f, 0f);
            maskRt.sizeDelta        = new Vector2(64f, 64f);
            var maskImg = maskGo.GetComponent<Image>();
            maskImg.sprite        = circleSpr;
            maskImg.type          = Image.Type.Simple;
            maskImg.raycastTarget = false;
            maskGo.GetComponent<Mask>().showMaskGraphic = false;

            var avGo = new GameObject("AvatarImg",
                typeof(RectTransform), typeof(Image));
            avGo.transform.SetParent(maskGo.transform, false);
            var avRt = (RectTransform)avGo.transform;
            avRt.anchorMin = Vector2.zero; avRt.anchorMax = Vector2.one;
            avRt.offsetMin = Vector2.zero; avRt.offsetMax = Vector2.zero;
            _avatarImg               = avGo.GetComponent<Image>();
            _avatarImg.color         = Color.white;
            _avatarImg.preserveAspect = true;
            _avatarImg.raycastTarget  = false;

            // Red neon ring outline around the avatar
            var ringGo = new GameObject("AvRing",
                typeof(RectTransform), typeof(Image), typeof(Outline));
            ringGo.transform.SetParent(panel, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.anchorMin        = new Vector2(0f, 0.5f);
            ringRt.anchorMax        = new Vector2(0f, 0.5f);
            ringRt.pivot            = new Vector2(0f, 0.5f);
            ringRt.anchoredPosition = new Vector2(14f, 0f);
            ringRt.sizeDelta        = new Vector2(64f, 64f);
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite        = circleSpr;
            ringImg.color         = new Color(0f, 0f, 0f, 0f);   // transparent fill
            ringImg.raycastTarget = false;
            var ringOl = ringGo.GetComponent<Outline>();
            ringOl.effectColor    = new Color(1f, 0.122f, 0.224f, 0.50f);
            ringOl.effectDistance = new Vector2(1.5f, -1.5f);

            // ── Username + Level column (left side) ───────────────────────────
            var colGo = new GameObject("NameCol", typeof(RectTransform));
            colGo.transform.SetParent(panel, false);
            var colRt = (RectTransform)colGo.transform;
            colRt.anchorMin        = new Vector2(0f, 0.5f);
            colRt.anchorMax        = new Vector2(0f, 0.5f);
            colRt.pivot            = new Vector2(0f, 0.5f);
            colRt.anchoredPosition = new Vector2(88f, 0f);   // 14 pad + 64 avatar + 10 gap
            colRt.sizeDelta        = new Vector2(240f, 64f);

            var vlg = colGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment         = TextAnchor.MiddleLeft;
            vlg.spacing                = 2f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 5, 5);

            _usernameLabel              = MakeTmp(colGo.transform, "Username", "AGENT", 18f, TextBright);
            _usernameLabel.fontStyle    = FontStyles.Bold;
            _usernameLabel.raycastTarget = false;
            _usernameLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            _levelLabel              = MakeTmp(colGo.transform, "Level", "Lv. 1", 13f, DimWhite);
            _levelLabel.fontStyle    = FontStyles.Bold;
            _levelLabel.raycastTarget = false;
            _levelLabel.gameObject.AddComponent<LayoutElement>().minHeight = 16f;

            // ── Level XP bar (green fill + centered XP text) ──────────────────
            var barGo = new GameObject("XpBar", typeof(RectTransform), typeof(Image));
            barGo.transform.SetParent(colGo.transform, false);
            var barLe = barGo.AddComponent<LayoutElement>();
            barLe.minHeight       = 11f;
            barLe.preferredHeight = 11f;
            barLe.preferredWidth  = 205f;
            var barImg = barGo.GetComponent<Image>();
            barImg.color         = XpTrack;
            barImg.raycastTarget = false;

            var fillGo = new GameObject("XpFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barGo.transform, false);
            _xpFill = (RectTransform)fillGo.transform;
            _xpFill.anchorMin        = new Vector2(0f, 0f);
            _xpFill.anchorMax        = new Vector2(0f, 1f);   // right edge set to Fill01 at sync
            _xpFill.pivot            = new Vector2(0f, 0.5f);
            _xpFill.offsetMin        = Vector2.zero;
            _xpFill.offsetMax        = Vector2.zero;
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color         = XpGreen;
            fillImg.raycastTarget = false;

            _xpLabel              = MakeTmp(barGo.transform, "XpText", "0/20", 10f, TextBright);
            _xpLabel.alignment    = TextAlignmentOptions.Center;
            _xpLabel.fontStyle    = FontStyles.Bold;
            _xpLabel.raycastTarget = false;
            var xpLblRt = _xpLabel.rectTransform;
            xpLblRt.anchorMin = Vector2.zero; xpLblRt.anchorMax = Vector2.one;
            xpLblRt.offsetMin = Vector2.zero; xpLblRt.offsetMax = Vector2.zero;

            // ── Gear / Settings button (far right) ────────────────────────────
            var gearGo = new GameObject("GearBtn",
                typeof(RectTransform), typeof(Image), typeof(Button));
            gearGo.transform.SetParent(panel, false);
            var gearRt = (RectTransform)gearGo.transform;
            gearRt.anchorMin        = new Vector2(1f, 0.5f);
            gearRt.anchorMax        = new Vector2(1f, 0.5f);
            gearRt.pivot            = new Vector2(1f, 0.5f);
            gearRt.anchoredPosition = new Vector2(-10f, 0f);
            gearRt.sizeDelta        = new Vector2(64f, 64f);
            var gearBg = gearGo.GetComponent<Image>();
            gearBg.color = new Color(0f, 0f, 0f, 0f);
            var gearBtn = gearGo.GetComponent<Button>();
            gearBtn.transition = Selectable.Transition.None;
            gearBtn.onClick.AddListener(OnGearClicked);

            BuildGearIcon(gearGo.transform, 38f);

            // ── VP balance label (right of gear) ──────────────────────────────
            var vpGo = new GameObject("VpRow", typeof(RectTransform));
            vpGo.transform.SetParent(panel, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin        = new Vector2(1f, 0.5f);
            vpRt.anchorMax        = new Vector2(1f, 0.5f);
            vpRt.pivot            = new Vector2(1f, 0.5f);
            vpRt.anchoredPosition = new Vector2(-82f, 0f);
            vpRt.sizeDelta        = new Vector2(220f, 40f);

            _vpLabel              = MakeTmp(vpGo.transform, "VpLbl", "0 VP", 18f, TextBright);
            _vpLabel.alignment    = TextAlignmentOptions.Right;
            _vpLabel.fontStyle    = FontStyles.Bold;
            _vpLabel.raycastTarget = false;
            var vpLblRt = _vpLabel.rectTransform;
            vpLblRt.anchorMin = Vector2.zero; vpLblRt.anchorMax = Vector2.one;
            vpLblRt.offsetMin = Vector2.zero; vpLblRt.offsetMax = Vector2.zero;
        }

        // ── Corner marks (identical code path to BottomNavBar) ────────────────
        static void AddCornerMark(RectTransform parent, bool top, bool left)
        {
            float xAnchor = left ? 0f : 1f;
            float yAnchor = top  ? 1f : 0f;
            float xPivot  = left ? 0f : 1f;
            float yPivot  = top  ? 1f : 0f;
            float xOff    = left ?  3f : -3f;
            float yOff    = top  ? -3f :  3f;

            var h = NewRT("CH", parent,
                new Vector2(xAnchor, yAnchor), new Vector2(xAnchor, yAnchor));
            h.pivot            = new Vector2(xPivot, yPivot);
            h.anchoredPosition = new Vector2(xOff, yOff);
            h.sizeDelta        = new Vector2(8f, 1f);
            h.gameObject.AddComponent<Image>().color       = ActiveRed;
            h.GetComponent<Image>().raycastTarget          = false;

            var v = NewRT("CV", parent,
                new Vector2(xAnchor, yAnchor), new Vector2(xAnchor, yAnchor));
            v.pivot            = new Vector2(xPivot, yPivot);
            v.anchoredPosition = new Vector2(xOff, yOff);
            v.sizeDelta        = new Vector2(1f, 8f);
            v.gameObject.AddComponent<Image>().color       = ActiveRed;
            v.GetComponent<Image>().raycastTarget          = false;
        }

        static void BuildGearIcon(Transform parent, float size)
        {
            var root = new GameObject("GearIco", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);

            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                float rad = angle * Mathf.Deg2Rad;
                var tooth = new GameObject("Tooth", typeof(RectTransform), typeof(Image));
                tooth.transform.SetParent(root.transform, false);
                var tr = (RectTransform)tooth.transform;
                tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0.5f, 0.5f);
                tr.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (size * 0.38f);
                tr.sizeDelta = new Vector2(size * 0.13f, size * 0.27f);
                tr.localRotation = Quaternion.Euler(0f, 0f, -angle);
                var img = tooth.GetComponent<Image>();
                img.color = TextBright;
                img.raycastTarget = false;
            }

            var outer = new GameObject("Outer", typeof(RectTransform), typeof(Image));
            outer.transform.SetParent(root.transform, false);
            var or = (RectTransform)outer.transform;
            or.anchorMin = or.anchorMax = or.pivot = new Vector2(0.5f, 0.5f);
            or.anchoredPosition = Vector2.zero;
            or.sizeDelta = new Vector2(size * 0.72f, size * 0.72f);
            var outerImg = outer.GetComponent<Image>();
            outerImg.sprite = MakeCircleSprite(64);
            outerImg.color = TextBright;
            outerImg.raycastTarget = false;

            var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            inner.transform.SetParent(root.transform, false);
            var ir = (RectTransform)inner.transform;
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0.5f, 0.5f);
            ir.anchoredPosition = Vector2.zero;
            ir.sizeDelta = new Vector2(size * 0.36f, size * 0.36f);
            var innerImg = inner.GetComponent<Image>();
            innerImg.sprite = MakeCircleSprite(64);
            innerImg.color = NavBg;
            innerImg.raycastTarget = false;
        }

        // ── Rect helpers ──────────────────────────────────────────────────────
        static RectTransform NewRT(string n, RectTransform p, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(p, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static TextMeshProUGUI MakeTmp(Transform parent, string name, string text,
                                        float size, Color color)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.color              = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            return tmp;
        }

        /// <summary>Generates a soft-edged circle sprite at runtime. No asset required.</summary>
        static Sprite MakeCircleSprite(int size)
        {
            var   tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            float r    = half - 1f;
            var   px   = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float a  = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 1f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
